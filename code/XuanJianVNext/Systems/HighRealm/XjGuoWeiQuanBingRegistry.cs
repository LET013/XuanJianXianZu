using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiQuanBingRegistry
{
	private const string StatusActive = "Active";
	private const string StatusReleased = "Released";
	private const string StatusPendingReincarnatedZhengWei = "PendingReincarnatedZhengWei";

	// 运行时权柄表只包含仍可参与权柄逻辑的角色。
	private static readonly Dictionary<long, XjGuoWeiQuanBingState> activeEntriesByActorId =
		new Dictionary<long, XjGuoWeiQuanBingState>();

	// 历史账本保留角色生前最后持有的全部本地/夺取/外道权柄及释放原因。
	private static readonly Dictionary<long, XjGuoWeiQuanBingState> historyEntriesByActorId =
		new Dictionary<long, XjGuoWeiQuanBingState>();

	private static readonly Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> lostAuthoritiesByKey =
		new Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>(StringComparer.Ordinal);
	// 热读取二级索引。道统年度结算会反复询问“某道某根柄是否已失”，
	// 不应每次通过 source + "|" + authority 创建临时字符串键。持久化仍以原 lostAuthoritiesByKey 为准。
	private static readonly Dictionary<string, Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>> lostAuthoritiesBySource =
		new Dictionary<string, Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>>(StringComparer.Ordinal);
	private static int revision;

	internal static int Revision => revision;

	internal static void Record(XjGuoWeiQuanBingState state)
	{
		if (!state.Found || state.ActorId <= 0L)
		{
			return;
		}

		bool isActive = IsRuntimeActive(state);
		bool changed = !historyEntriesByActorId.TryGetValue(state.ActorId, out XjGuoWeiQuanBingState oldHistory)
			|| !StatesEqual(oldHistory, state);

		historyEntriesByActorId[state.ActorId] = state;
		if (isActive)
		{
			if (!activeEntriesByActorId.TryGetValue(state.ActorId, out XjGuoWeiQuanBingState oldActive)
				|| !StatesEqual(oldActive, state))
			{
				changed = true;
			}
			activeEntriesByActorId[state.ActorId] = state;
			XjHighRealmAggregateStore.ApplyAuthority(state);
			TryWriteActiveActorSnapshot(state);
		}
		else
		{
			XjHighRealmAggregateStore.RemoveAuthority(state.ActorId);
			if (activeEntriesByActorId.Remove(state.ActorId))
			{
				changed = true;
			}
		}

		if (changed)
		{
			Touch(protectedCommit: true);
		}
	}

	internal static bool TryGet(long actorId, out XjGuoWeiQuanBingState state)
	{
		if (XjHighRealmAggregateStore.TryGetAuthority(actorId, out state)) return true;
		if (actorId > 0L && activeEntriesByActorId.TryGetValue(actorId, out state))
		{
			if (state.Found) XjHighRealmAggregateStore.ApplyAuthority(in state);
			return state.Found;
		}

		state = default;
		return false;
	}

	internal static bool TryGetHistorical(long actorId, out XjGuoWeiQuanBingState state)
	{
		if (actorId > 0L && historyEntriesByActorId.TryGetValue(actorId, out state))
		{
			return state.Found;
		}

		state = default;
		return false;
	}

	/// <summary>
	/// 0.5.4果位钟爱主果锁：锁属于道途果位，而不是死者仍占据的运行时果位。
	/// 锁期内仅当前果位钟爱转世承载者可以占据该道途果位；没有承载者时所有人均被拦截。
	/// 历史记录也参与锁判定，保证原身陨落到新身降世之间不会出现抢位空窗。
	/// </summary>
	internal static bool IsZhengWeiLockedForActor(string daoTu, long actorId, int currentYear, out int lockUntilYear)
	{
		lockUntilYear = 0;
		string normalizedDaoTu = Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		int normalizedYear = ResolveCurrentWorldYear(currentYear);
		long authorizedActorId = 0L;
		foreach (XjGuoWeiQuanBingState state in historyEntriesByActorId.Values)
		{
			if (!state.Found
				|| state.LockUntilYear <= 0
				|| normalizedYear > state.LockUntilYear
				|| string.IsNullOrWhiteSpace(state.GuoWeiZhongAi))
			{
				continue;
			}

			string lockedDaoTu = string.IsNullOrWhiteSpace(state.PendingExternalZhengWeiDaoTu)
				? state.DaoTu
				: state.PendingExternalZhengWeiDaoTu;
			if (!string.Equals(Normalize(lockedDaoTu), normalizedDaoTu, StringComparison.Ordinal))
			{
				continue;
			}

			if (state.LockUntilYear > lockUntilYear)
			{
				lockUntilYear = state.LockUntilYear;
				authorizedActorId = 0L;
			}

			if (state.LockUntilYear == lockUntilYear
				&& IsRuntimeActive(state)
				&& state.ActorId > 0L)
			{
				authorizedActorId = state.ActorId;
			}
		}

		return lockUntilYear > 0 && (actorId <= 0L || actorId != authorizedActorId);
	}

	internal static bool TryGetZhengWeiLock(string daoTu, int currentYear, out int lockUntilYear)
	{
		bool lockedForNobody = IsZhengWeiLockedForActor(daoTu, 0L, currentYear, out lockUntilYear);
		return lockedForNobody || lockUntilYear > 0;
	}

	/// <summary>
	/// 果位钟爱转世身另选异道时，立即释放原道途正位封锁。
	/// 这里只消费已经登记在 PendingReincarnatedZhengWei 上的旧果锁，
	/// 普通求金者与非钟爱转世不受影响。
	/// </summary>
	internal static bool TryReleaseReincarnatedZhengWeiLock(
		string daoTu,
		long actorId,
		int currentYear,
		out int previousLockUntilYear)
	{
		previousLockUntilYear = 0;
		string normalizedDaoTu = Normalize(daoTu);
		if (actorId <= 0L || string.IsNullOrWhiteSpace(normalizedDaoTu)) return false;

		int normalizedYear = ResolveCurrentWorldYear(currentYear);
		if (!TryGet(actorId, out XjGuoWeiQuanBingState carrier)
			|| !carrier.Found
			|| !string.Equals(carrier.LifecycleStatus, StatusPendingReincarnatedZhengWei, StringComparison.Ordinal)
			|| carrier.LockUntilYear <= 0
			|| normalizedYear > carrier.LockUntilYear
			|| string.IsNullOrWhiteSpace(carrier.GuoWeiZhongAi)
			|| !string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(carrier.GuoWei),
				XjGuoWeiCalculator.ZhengWei,
				StringComparison.Ordinal))
		{
			return false;
		}

		string carrierLockedDaoTu = string.IsNullOrWhiteSpace(carrier.PendingExternalZhengWeiDaoTu)
			? carrier.DaoTu
			: carrier.PendingExternalZhengWeiDaoTu;
		if (!string.Equals(Normalize(carrierLockedDaoTu), normalizedDaoTu, StringComparison.Ordinal)) return false;

		previousLockUntilYear = carrier.LockUntilYear;
		List<long> keys = new List<long>(historyEntriesByActorId.Keys);
		List<XjGuoWeiQuanBingState> rewrites = new List<XjGuoWeiQuanBingState>();
		for (int i = 0; i < keys.Count; i++)
		{
			if (!historyEntriesByActorId.TryGetValue(keys[i], out XjGuoWeiQuanBingState state)
				|| !state.Found
				|| state.LockUntilYear <= 0
				|| state.LockUntilYear < normalizedYear
				|| state.LockUntilYear > previousLockUntilYear
				|| string.IsNullOrWhiteSpace(state.GuoWeiZhongAi))
			{
				continue;
			}

			string lockedDaoTu = string.IsNullOrWhiteSpace(state.PendingExternalZhengWeiDaoTu)
				? state.DaoTu
				: state.PendingExternalZhengWeiDaoTu;
			if (!string.Equals(Normalize(lockedDaoTu), normalizedDaoTu, StringComparison.Ordinal)) continue;

			string summary = string.IsNullOrWhiteSpace(state.Summary)
				? "转世旧主重叩金门，果位封锁提前解除"
				: state.Summary.Trim() + "；转世旧主于" + XjChronology.FormatYear(normalizedYear) + "重叩金门，果位封锁提前解除";
			rewrites.Add(new XjGuoWeiQuanBingState(
				state.Found, state.ActorId, state.ActorName, state.DaoTu, state.GuoWei,
				state.LocalQuanBing, state.SeizedQuanBing, state.ForeignQuanBing, state.WithdrawnToDongTian,
				state.GuoWeiZhongAi, string.Empty, 0,
				state.IntegrationRetreatActive, state.IntegrationRetreatEndYear,
				summary, state.LifecycleStatus, state.AcquiredYear, state.ReleasedYear, state.ReleaseReason,
				state.SeizedQuanBingSources));
		}

		if (rewrites.Count == 0) return false;
		for (int i = 0; i < rewrites.Count; i++) Record(rewrites[i]);
		return true;
	}

	private static int ResolveCurrentWorldYear(int fallbackYear)
	{
		if (XjYearTracker.CurrentYear > 0)
		{
			return XjYearTracker.CurrentYear;
		}
		int worldYear = World.world?.map_stats?.year ?? 0;
		return worldYear > 0 ? worldYear : Math.Max(0, fallbackYear);
	}

	/// <summary>
	/// 角色信息页只读解析：活动表优先；缺失时在角色原生存档镜像与历史账本中择取信息更完整者。
	/// 对旧档缺失的本地权柄只做确定性补全，不写角色数据、不标记档案 dirty。
	/// </summary>
	internal static bool TryGetForLiveDisplay(Actor actor, out XjGuoWeiQuanBingState state)
	{
		state = default;
		if (!IsAliveJinDan(actor, out long actorId))
		{
			return false;
		}

		if (TryGet(actorId, out XjGuoWeiQuanBingState active))
		{
			state = NormalizeForLiveActor(actor, active);
			return true;
		}

		if (TryResolveBestPersistentCandidate(actor, actorId, out XjGuoWeiQuanBingState persistent))
		{
			state = NormalizeForLiveActor(actor, persistent);
			return true;
		}

		return XjGuoWeiQuanBingLifecycle.TryBuildReadOnlyRecoveryState(actor, out state);
	}

	/// <summary>
	/// 读档后的只读运行态重建。只改内存索引，不写世界档案、不写 ActorData。
	/// </summary>
	internal static bool ReconcileLiveActorReadOnly(Actor actor)
	{
		if (!IsAliveJinDan(actor, out long actorId))
		{
			return false;
		}

		if (TryGet(actorId, out XjGuoWeiQuanBingState active))
		{
			XjGuoWeiQuanBingState normalizedActive = NormalizeForLiveActor(actor, active);
			if (!StatesEqual(active, normalizedActive))
			{
				activeEntriesByActorId[actorId] = normalizedActive;
				historyEntriesByActorId[actorId] = normalizedActive;
				XjHighRealmAggregateStore.ApplyAuthority(normalizedActive);
			}
			return true;
		}

		XjGuoWeiQuanBingState candidate;
		if (!TryResolveBestPersistentCandidate(actor, actorId, out candidate)
			&& !XjGuoWeiQuanBingLifecycle.TryBuildReadOnlyRecoveryState(actor, out candidate))
		{
			return false;
		}

		XjGuoWeiQuanBingState restored = NormalizeForLiveActor(actor, candidate);

		activeEntriesByActorId[actorId] = restored;
		historyEntriesByActorId[actorId] = restored;
		XjHighRealmAggregateStore.ApplyAuthority(restored);
		return true;
	}

	/// <summary>
	/// 世界正式保存前只从既有高境索引固化活体权柄快照。
	/// 保存边界不扫描全世界，也不为补齐快照强行推进读档恢复。
	/// </summary>
	internal static void PrepareLiveSnapshotsForSave()
	{
		// 保存不再为了“完整”而扫描世界或强行完成读档 bootstrap。bootstrap 尚未
		// 完成时保留刚导入的权柄档案；等正常 runtime 恢复完成后再由事件/索引更新。
		if (XjWorldBootstrapLane.HasPending) return;

		IReadOnlyList<long> jinDanIds = XjCultivatorCache.GetZhenJunOrHigherIds();
		for (int i = 0; i < jinDanIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(jinDanIds[i], out Actor actor)
				|| !TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState state))
			{
				continue;
			}
			Record(state);
		}
	}

	/// <summary>
	/// 正常运行阶段把活动权柄同步到角色原生存档镜像。
	/// </summary>
	internal static void SyncActiveSnapshotToActor(Actor actor)
	{
		if (!IsAliveJinDan(actor, out long actorId)
			|| !TryGet(actorId, out XjGuoWeiQuanBingState state))
		{
			return;
		}

		XjGuoWeiQuanBingActorSnapshot.WriteActive(actor, state);
	}

	internal static void RemoveActor(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		XjHighRealmAggregateStore.RemoveActor(actorId);
		bool changed = activeEntriesByActorId.Remove(actorId);
		changed |= historyEntriesByActorId.Remove(actorId);
		if (changed)
		{
			Touch(protectedCommit: false);
		}
	}

	internal static string GetSummary(long actorId)
	{
		if (TryGet(actorId, out XjGuoWeiQuanBingState active))
		{
			return Format(active);
		}
		return TryGetHistorical(actorId, out XjGuoWeiQuanBingState historical)
			? Format(historical)
			: "权柄记录：无";
	}

	internal static IReadOnlyList<XjGuoWeiQuanBingState> ReadAllEntries()
	{
		if (historyEntriesByActorId.Count == 0)
		{
			return Array.Empty<XjGuoWeiQuanBingState>();
		}

		List<XjGuoWeiQuanBingState> entries = new List<XjGuoWeiQuanBingState>(historyEntriesByActorId.Values);
		entries.Sort((left, right) =>
		{
			int byYear = NormalizeSortYear(left.AcquiredYear).CompareTo(NormalizeSortYear(right.AcquiredYear));
			if (byYear != 0) return byYear;
			int byDaoTu = string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal);
			return byDaoTu != 0 ? byDaoTu : left.ActorId.CompareTo(right.ActorId);
		});
		return entries;
	}

	internal static bool IsAuthorityLost(string sourceDaoTu, string authority)
	{
		return TryGetLostAuthorityIndexed(sourceDaoTu, authority, out _);
	}

	internal static bool TryGetLostAuthorityRecord(
		string sourceDaoTu,
		string authority,
		out XjGuoWeiQuanBingLostAuthorityArchiveData record)
	{
		return TryGetLostAuthorityIndexed(sourceDaoTu, authority, out record);
	}

	/// <summary>
	/// 道统年度修复的无快照查询：失柄账本本身已经按“源道|根柄”建索引，
	/// 不再为了判断一个根柄的目标道途创建并排序整张 lost 列表。
	/// </summary>
	internal static bool IsAuthorityLostToTarget(string sourceDaoTu, string authority, string targetDaoTu)
	{
		if (!TryGetLostAuthorityRecord(sourceDaoTu, authority, out XjGuoWeiQuanBingLostAuthorityArchiveData record)
			|| record == null) return false;
		return string.Equals(Normalize(record.TargetDaoTu), Normalize(targetDaoTu), StringComparison.Ordinal);
	}

	/// <summary>
	/// 无分配查询某一原道根柄是否正在外道融合。活动权柄持有人很少，直接遍历活动字典；
	/// 不创建历史快照、不排序，也不 Split 权柄字符串。
	/// </summary>
	internal static bool HasPendingIntegration(string sourceDaoTu, string authority)
	{
		string source = Normalize(sourceDaoTu);
		string expectedAuthority = Normalize(authority);
		if (source.Length == 0 || expectedAuthority.Length == 0) return false;
		foreach (XjGuoWeiQuanBingState state in activeEntriesByActorId.Values)
		{
			if (!state.Found || !state.IntegrationRetreatActive
				|| !string.Equals(state.LifecycleStatus, StatusActive, StringComparison.Ordinal)
				|| !string.Equals(Normalize(state.PendingExternalZhengWeiDaoTu), source, StringComparison.Ordinal)) continue;
			if (ContainsAuthorityToken(state.ForeignQuanBing, expectedAuthority)) return true;
		}
		return false;
	}

	/// <summary>
	/// 已知夺柄者时直接按 ActorId O(1) 定位活动权柄状态，避免遍历并排序全部历史权柄记录。
	/// </summary>
	internal static bool HasBorrowPending(
		string sourceDaoTu,
		string targetDaoTu,
		string authority,
		long actorId)
	{
		if (actorId <= 0L || !activeEntriesByActorId.TryGetValue(actorId, out XjGuoWeiQuanBingState state)
			|| !state.Found || !state.IntegrationRetreatActive
			|| !string.Equals(state.LifecycleStatus, StatusActive, StringComparison.Ordinal)) return false;
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		string expectedAuthority = Normalize(authority);
		return source.Length > 0 && target.Length > 0 && expectedAuthority.Length > 0
			&& string.Equals(Normalize(state.DaoTu), target, StringComparison.Ordinal)
			&& string.Equals(Normalize(state.PendingExternalZhengWeiDaoTu), source, StringComparison.Ordinal)
			&& ContainsAuthorityToken(state.ForeignQuanBing, expectedAuthority);
	}

	/// <summary>
	/// 与旧 Split(',', '，', '|', '、') 语义一致的无数组版本；忽略 token 两端空白。
	/// </summary>
	internal static bool ContainsAuthorityToken(string raw, string authority)
	{
		string expected = Normalize(authority);
		if (string.IsNullOrEmpty(raw) || expected.Length == 0) return false;
		int index = 0;
		while (index < raw.Length)
		{
			while (index < raw.Length && (IsAuthoritySeparator(raw[index]) || char.IsWhiteSpace(raw[index]))) index++;
			if (index >= raw.Length) break;
			int start = index;
			while (index < raw.Length && !IsAuthoritySeparator(raw[index])) index++;
			int end = index;
			while (end > start && char.IsWhiteSpace(raw[end - 1])) end--;
			while (start < end && char.IsWhiteSpace(raw[start])) start++;
			int length = end - start;
			if (length == expected.Length
				&& string.Compare(raw, start, expected, 0, length, StringComparison.Ordinal) == 0) return true;
		}
		return false;
	}

	private static bool IsAuthoritySeparator(char value)
	{
		return value == ',' || value == '，' || value == '|' || value == '、';
	}

	internal static int CountAvailableAuthorities(string daoTu)
	{
		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(daoTu);
		if (catalog == null || catalog.Count == 0)
		{
			return 0;
		}

		int available = 0;
		for (int i = 0; i < catalog.Count; i++)
		{
			if (!IsAuthorityLost(daoTu, catalog[i]))
			{
				available++;
			}
		}
		return available;
	}

	internal static bool IsAuthorityAvailable(string daoTu, string authority)
	{
		if (string.IsNullOrWhiteSpace(daoTu) || string.IsNullOrWhiteSpace(authority))
		{
			return false;
		}

		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(daoTu);
		for (int i = 0; i < catalog.Count; i++)
		{
			if (string.Equals(catalog[i], authority.Trim(), StringComparison.Ordinal))
			{
				return !IsAuthorityLost(daoTu, catalog[i]);
			}
		}
		return false;
	}

	internal static bool HasEnoughAvailableAuthorities(string daoTu, string guoWei)
	{
		// 果位唯一完整持有六道根权柄；余闰位只派生有限解释，不再按
		// “第几个槽位就消耗几道权柄”计算。余位至少需一道可派生根权柄，
		// 闰位至少需两道可供间错的根权柄。
		string type = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		int required = string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			? XjGuoWeiQuanBingRules.QuanBingCountPerDaoTu
			: string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 2 : 1;
		return CountAvailableAuthorities(daoTu) >= required;
	}

	internal static void RecordLostAuthority(string sourceDaoTu, string authority, string targetDaoTu, int year, string reason)
	{
		string normalizedSource = Normalize(sourceDaoTu);
		string normalizedAuthority = Normalize(authority);
		if (string.IsNullOrWhiteSpace(normalizedSource) || string.IsNullOrWhiteSpace(normalizedAuthority))
		{
			return;
		}

		string key = BuildLostAuthorityKey(normalizedSource, normalizedAuthority);
		XjGuoWeiQuanBingLostAuthorityArchiveData next = new XjGuoWeiQuanBingLostAuthorityArchiveData
		{
			SourceDaoTu = normalizedSource,
			Authority = normalizedAuthority,
			TargetDaoTu = Normalize(targetDaoTu),
			Year = Math.Max(0, year),
			Reason = string.IsNullOrWhiteSpace(reason) ? "外道权柄合道" : reason.Trim()
		};
		if (lostAuthoritiesByKey.TryGetValue(key, out XjGuoWeiQuanBingLostAuthorityArchiveData current)
			&& LostAuthorityRecordsEqual(current, next))
		{
			return;
		}

		lostAuthoritiesByKey[key] = next;
		IndexLostAuthority(next);
		Touch(protectedCommit: true);
	}

	internal static bool ClearLostAuthority(string sourceDaoTu, string authority)
	{
		string normalizedSource = Normalize(sourceDaoTu);
		string normalizedAuthority = Normalize(authority);
		string key = normalizedSource + "|" + normalizedAuthority;
		if (!lostAuthoritiesByKey.Remove(key)) return false;
		if (lostAuthoritiesBySource.TryGetValue(normalizedSource, out Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> byAuthority))
		{
			byAuthority.Remove(normalizedAuthority);
			if (byAuthority.Count == 0) lostAuthoritiesBySource.Remove(normalizedSource);
		}
		Touch(protectedCommit: true);
		return true;
	}

	/// <summary>
	/// 旧档“失坠天地”修复的零分配探针。正常新档的失柄记录都有 TargetDaoTu，
	/// 因而年度道统维护不应为仅供迁移使用的检查创建并排序整份失柄快照。
	/// </summary>
	internal static bool HasUntargetedLostAuthorityRecords()
	{
		foreach (XjGuoWeiQuanBingLostAuthorityArchiveData record in lostAuthoritiesByKey.Values)
		{
			if (record != null && string.IsNullOrWhiteSpace(record.TargetDaoTu)) return true;
		}
		return false;
	}

	internal static IReadOnlyList<XjGuoWeiQuanBingLostAuthorityArchiveData> ReadLostAuthorityRecords()
	{
		if (lostAuthoritiesByKey.Count == 0)
		{
			return Array.Empty<XjGuoWeiQuanBingLostAuthorityArchiveData>();
		}

		List<XjGuoWeiQuanBingLostAuthorityArchiveData> records = new List<XjGuoWeiQuanBingLostAuthorityArchiveData>(lostAuthoritiesByKey.Values);
		records.Sort((left, right) =>
		{
			int byDaoTu = string.Compare(left.SourceDaoTu, right.SourceDaoTu, StringComparison.Ordinal);
			return byDaoTu != 0 ? byDaoTu : string.Compare(left.Authority, right.Authority, StringComparison.Ordinal);
		});
		return records;
	}

	internal static void ExportArchiveRecords(List<XjGuoWeiQuanBingArchiveData> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjGuoWeiQuanBingState state in historyEntriesByActorId.Values)
		{
			if (!state.Found || state.ActorId <= 0L)
			{
				continue;
			}

			records.Add(new XjGuoWeiQuanBingArchiveData
			{
				ActorId = state.ActorId,
				ActorName = state.ActorName,
				DaoTu = state.DaoTu,
				GuoWei = state.GuoWei,
				LocalQuanBing = state.LocalQuanBing,
				SeizedQuanBing = state.SeizedQuanBing,
				SeizedQuanBingSources = state.SeizedQuanBingSources,
				ForeignQuanBing = state.ForeignQuanBing,
				WithdrawnToDongTian = state.WithdrawnToDongTian,
				GuoWeiZhongAi = state.GuoWeiZhongAi,
				PendingExternalZhengWeiDaoTu = state.PendingExternalZhengWeiDaoTu,
				LockUntilYear = state.LockUntilYear,
				IntegrationRetreatActive = state.IntegrationRetreatActive,
				IntegrationRetreatEndYear = state.IntegrationRetreatEndYear,
				Summary = state.Summary,
				LifecycleStatus = state.LifecycleStatus,
				AcquiredYear = state.AcquiredYear,
				ReleasedYear = state.ReleasedYear,
				ReleaseReason = state.ReleaseReason
			});
		}
	}

	internal static void ExportLostAuthorityRecords(List<XjGuoWeiQuanBingLostAuthorityArchiveData> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjGuoWeiQuanBingLostAuthorityArchiveData record in ReadLostAuthorityRecords())
		{
			records.Add(new XjGuoWeiQuanBingLostAuthorityArchiveData
			{
				SourceDaoTu = record.SourceDaoTu,
				Authority = record.Authority,
				TargetDaoTu = record.TargetDaoTu,
				Year = record.Year,
				Reason = record.Reason
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjGuoWeiQuanBingArchiveData> records)
	{
		activeEntriesByActorId.Clear();
		historyEntriesByActorId.Clear();
		// 世界档案导入意味着运行态换档，权柄与真景热缓存都必须清空，
		// 避免不同世界复用相同ActorId时读到上一档的战斗分值。
		XjHighRealmAggregateStore.Clear();
		if (records == null)
		{
			return;
		}

		foreach (XjGuoWeiQuanBingArchiveData record in records)
		{
			if (record == null || record.ActorId <= 0L)
			{
				continue;
			}

			string status = NormalizeLifecycleStatus(record.LifecycleStatus, record.ReleasedYear);
			string daoTu = Normalize(record.DaoTu);
			string localQuanBing = XjGuoWeiAuthorityCatalog.NormalizeAuthoritySet(record.LocalQuanBing, daoTu);
			string seizedQuanBing = XjGuoWeiAuthorityCatalog.NormalizeAuthoritySet(record.SeizedQuanBing);
			string foreignQuanBing = XjGuoWeiAuthorityCatalog.NormalizeAuthoritySet(record.ForeignQuanBing);
			string withdrawnToDongTian = XjGuoWeiAuthorityCatalog.NormalizeAuthoritySet(record.WithdrawnToDongTian);
			string seizedSources = XjGuoWeiAuthorityCatalog.NormalizeAuthoritySourceMap(record.SeizedQuanBingSources);
			XjGuoWeiQuanBingState candidate = new XjGuoWeiQuanBingState(
				true,
				record.ActorId,
				record.ActorName,
				daoTu,
				record.GuoWei,
				localQuanBing,
				seizedQuanBing,
				foreignQuanBing,
				withdrawnToDongTian,
				record.GuoWeiZhongAi,
				record.PendingExternalZhengWeiDaoTu,
				record.LockUntilYear,
				record.IntegrationRetreatActive,
				record.IntegrationRetreatEndYear,
				record.Summary,
				status,
				record.AcquiredYear,
				record.ReleasedYear,
				record.ReleaseReason,
				seizedSources);

			if (!historyEntriesByActorId.TryGetValue(candidate.ActorId, out XjGuoWeiQuanBingState existing)
				|| ShouldPreferHistory(candidate, existing))
			{
				historyEntriesByActorId[candidate.ActorId] = candidate;
			}
		}

		foreach (XjGuoWeiQuanBingState state in historyEntriesByActorId.Values)
		{
			if (IsRuntimeActive(state))
			{
				activeEntriesByActorId[state.ActorId] = state;
				XjHighRealmAggregateStore.ApplyAuthority(state);
			}
		}
	}

	internal static void ImportLostAuthorityRecords(IEnumerable<XjGuoWeiQuanBingLostAuthorityArchiveData> records)
	{
		lostAuthoritiesByKey.Clear();
		lostAuthoritiesBySource.Clear();
		if (records == null)
		{
			return;
		}

		foreach (XjGuoWeiQuanBingLostAuthorityArchiveData record in records)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.SourceDaoTu) || string.IsNullOrWhiteSpace(record.Authority))
			{
				continue;
			}

			XjGuoWeiQuanBingLostAuthorityArchiveData imported = new XjGuoWeiQuanBingLostAuthorityArchiveData
			{
				SourceDaoTu = Normalize(record.SourceDaoTu),
				Authority = XjGuoWeiAuthorityCatalog.NormalizeAuthorityName(record.SourceDaoTu, record.Authority),
				TargetDaoTu = Normalize(record.TargetDaoTu),
				Year = Math.Max(0, record.Year),
				Reason = Normalize(record.Reason)
			};
			lostAuthoritiesByKey[BuildLostAuthorityKey(imported.SourceDaoTu, imported.Authority)] = imported;
			IndexLostAuthority(imported);
		}
	}

	// 果位历史存在但旧档没有权柄行时，补一条最小历史记录，避免真君/权柄窗口再次因活体扫描而消失。
	internal static void BackfillHistoricalRecords(IEnumerable<XjWorldArchiveGuoWeiRecord> guoWeiRecords)
	{
		if (guoWeiRecords == null)
		{
			return;
		}

		bool changed = false;
		foreach (XjWorldArchiveGuoWeiRecord record in guoWeiRecords)
		{
			if (record == null || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.GuoWei)
				|| historyEntriesByActorId.ContainsKey(record.ActorId))
			{
				continue;
			}

			bool active = string.Equals(record.LifecycleStatus, StatusActive, StringComparison.Ordinal)
				&& record.EndedYear <= 0;
			XjGuoWeiQuanBingState state = new XjGuoWeiQuanBingState(
				true,
				record.ActorId,
				record.ActorName,
				record.DaoTu,
				record.GuoWei,
				string.Empty,
				string.Empty,
				string.Empty,
				string.Empty,
				string.Empty,
				string.Empty,
				0,
				false,
				0,
				"旧档历史回填：权柄明细无可恢复快照",
				active ? StatusActive : StatusReleased,
				record.Year,
				record.EndedYear,
				active ? string.Empty : (string.IsNullOrWhiteSpace(record.EndReason) ? "Death" : record.EndReason));
			historyEntriesByActorId[state.ActorId] = state;
			if (active)
			{
				activeEntriesByActorId[state.ActorId] = state;
				XjHighRealmAggregateStore.ApplyAuthority(state);
			}
			changed = true;
		}

		if (changed)
		{
			Touch(protectedCommit: false);
		}
	}

	internal static void Clear()
	{
		if (activeEntriesByActorId.Count > 0 || historyEntriesByActorId.Count > 0 || lostAuthoritiesByKey.Count > 0)
		{
			revision++;
		}
		activeEntriesByActorId.Clear();
		historyEntriesByActorId.Clear();
		lostAuthoritiesByKey.Clear();
		lostAuthoritiesBySource.Clear();
		XjHighRealmAggregateStore.Clear();
	}

	private static string Format(in XjGuoWeiQuanBingState state)
	{
		string daoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? "未定道途" : state.DaoTu.Trim();
		string guoWei = string.IsNullOrWhiteSpace(state.GuoWei) ? "未知果位" : state.GuoWei.Trim();
		string lockText = state.LockUntilYear > 0 ? " - 封锁至" + XjChronology.FormatYear(state.LockUntilYear) : string.Empty;
		string statusText = IsRuntimeActive(state) ? string.Empty : " - 已释放";
		return daoTu + " - " + guoWei + lockText + statusText;
	}

	private static bool IsRuntimeActive(in XjGuoWeiQuanBingState state)
	{
		return state.Found
			&& state.ActorId > 0L
			&& state.ReleasedYear <= 0
			&& (string.Equals(state.LifecycleStatus, StatusActive, StringComparison.Ordinal)
				|| string.Equals(state.LifecycleStatus, StatusPendingReincarnatedZhengWei, StringComparison.Ordinal));
	}

	private static string NormalizeLifecycleStatus(string value, int releasedYear)
	{
		string normalized = Normalize(value);
		if (releasedYear > 0 || string.Equals(normalized, StatusReleased, StringComparison.Ordinal))
		{
			return StatusReleased;
		}
		if (string.Equals(normalized, StatusPendingReincarnatedZhengWei, StringComparison.Ordinal))
		{
			return StatusPendingReincarnatedZhengWei;
		}
		return StatusActive;
	}

	private static bool ShouldPreferHistory(in XjGuoWeiQuanBingState candidate, in XjGuoWeiQuanBingState existing)
	{
		if (!existing.Found) return true;
		if (candidate.ReleasedYear != existing.ReleasedYear) return candidate.ReleasedYear > existing.ReleasedYear;
		if (IsRuntimeActive(candidate) != IsRuntimeActive(existing)) return !IsRuntimeActive(candidate);
		if (candidate.AcquiredYear != existing.AcquiredYear)
		{
			return candidate.AcquiredYear > 0 && (existing.AcquiredYear <= 0 || candidate.AcquiredYear < existing.AcquiredYear);
		}
		return Completeness(candidate) > Completeness(existing);
	}

	private static int Completeness(in XjGuoWeiQuanBingState state)
	{
		int score = 0;
		if (!string.IsNullOrWhiteSpace(state.ActorName)) score++;
		if (!string.IsNullOrWhiteSpace(state.DaoTu)) score++;
		if (!string.IsNullOrWhiteSpace(state.GuoWei)) score++;
		if (!string.IsNullOrWhiteSpace(state.LocalQuanBing)) score += 2;
		if (!string.IsNullOrWhiteSpace(state.SeizedQuanBing)) score += 2;
		if (!string.IsNullOrWhiteSpace(state.ForeignQuanBing)) score += 2;
		if (!string.IsNullOrWhiteSpace(state.SeizedQuanBingSources)) score++;
		if (!string.IsNullOrWhiteSpace(state.GuoWeiZhongAi)) score++;
		return score;
	}

	private static bool StatesEqual(in XjGuoWeiQuanBingState left, in XjGuoWeiQuanBingState right)
	{
		return left.Found == right.Found
			&& left.ActorId == right.ActorId
			&& string.Equals(left.ActorName, right.ActorName, StringComparison.Ordinal)
			&& string.Equals(left.DaoTu, right.DaoTu, StringComparison.Ordinal)
			&& string.Equals(left.GuoWei, right.GuoWei, StringComparison.Ordinal)
			&& string.Equals(left.LocalQuanBing, right.LocalQuanBing, StringComparison.Ordinal)
			&& string.Equals(left.SeizedQuanBing, right.SeizedQuanBing, StringComparison.Ordinal)
			&& string.Equals(left.SeizedQuanBingSources, right.SeizedQuanBingSources, StringComparison.Ordinal)
			&& string.Equals(left.ForeignQuanBing, right.ForeignQuanBing, StringComparison.Ordinal)
			&& string.Equals(left.WithdrawnToDongTian, right.WithdrawnToDongTian, StringComparison.Ordinal)
			&& string.Equals(left.GuoWeiZhongAi, right.GuoWeiZhongAi, StringComparison.Ordinal)
			&& string.Equals(left.PendingExternalZhengWeiDaoTu, right.PendingExternalZhengWeiDaoTu, StringComparison.Ordinal)
			&& left.LockUntilYear == right.LockUntilYear
			&& left.IntegrationRetreatActive == right.IntegrationRetreatActive
			&& left.IntegrationRetreatEndYear == right.IntegrationRetreatEndYear
			&& string.Equals(left.Summary, right.Summary, StringComparison.Ordinal)
			&& string.Equals(left.LifecycleStatus, right.LifecycleStatus, StringComparison.Ordinal)
			&& left.AcquiredYear == right.AcquiredYear
			&& left.ReleasedYear == right.ReleasedYear
			&& string.Equals(left.ReleaseReason, right.ReleaseReason, StringComparison.Ordinal);
	}

	private static bool LostAuthorityRecordsEqual(
		XjGuoWeiQuanBingLostAuthorityArchiveData left,
		XjGuoWeiQuanBingLostAuthorityArchiveData right)
	{
		return left != null && right != null
			&& string.Equals(left.SourceDaoTu, right.SourceDaoTu, StringComparison.Ordinal)
			&& string.Equals(left.Authority, right.Authority, StringComparison.Ordinal)
			&& string.Equals(left.TargetDaoTu, right.TargetDaoTu, StringComparison.Ordinal)
			&& left.Year == right.Year
			&& string.Equals(left.Reason, right.Reason, StringComparison.Ordinal);
	}

	private static bool TryGetLostAuthorityIndexed(
		string sourceDaoTu,
		string authority,
		out XjGuoWeiQuanBingLostAuthorityArchiveData record)
	{
		record = null;
		string source = Normalize(sourceDaoTu);
		string expectedAuthority = Normalize(authority);
		return source.Length > 0 && expectedAuthority.Length > 0
			&& lostAuthoritiesBySource.TryGetValue(source, out Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> byAuthority)
			&& byAuthority.TryGetValue(expectedAuthority, out record)
			&& record != null;
	}

	private static void IndexLostAuthority(XjGuoWeiQuanBingLostAuthorityArchiveData record)
	{
		if (record == null) return;
		string source = Normalize(record.SourceDaoTu);
		string authority = Normalize(record.Authority);
		if (source.Length == 0 || authority.Length == 0) return;
		if (!lostAuthoritiesBySource.TryGetValue(source, out Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> byAuthority))
		{
			byAuthority = new Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>(StringComparer.Ordinal);
			lostAuthoritiesBySource[source] = byAuthority;
		}
		byAuthority[authority] = record;
	}

	private static string BuildLostAuthorityKey(string sourceDaoTu, string authority)
	{
		return Normalize(sourceDaoTu) + "|" + Normalize(authority);
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}

	private static int NormalizeSortYear(int year)
	{
		return year <= 0 ? int.MaxValue : year;
	}

	private static bool TryResolveBestPersistentCandidate(
		Actor actor,
		long actorId,
		out XjGuoWeiQuanBingState state)
	{
		bool hasHistorical = TryGetHistorical(actorId, out XjGuoWeiQuanBingState historical);
		bool hasActorSnapshot = XjGuoWeiQuanBingActorSnapshot.TryReadActive(actor, out XjGuoWeiQuanBingState actorSnapshot);
		if (!hasHistorical && !hasActorSnapshot)
		{
			state = default;
			return false;
		}

		if (!hasHistorical)
		{
			state = actorSnapshot;
			return true;
		}

		if (!hasActorSnapshot)
		{
			state = historical;
			return true;
		}

		int historicalCompleteness = Completeness(historical);
		int actorCompleteness = Completeness(actorSnapshot);
		if (actorCompleteness > historicalCompleteness)
		{
			state = actorSnapshot;
			return true;
		}

		state = historical;
		return true;
	}

	private static void TryWriteActiveActorSnapshot(in XjGuoWeiQuanBingState state)
	{
		if (!IsRuntimeActive(state)
			|| !XjScheduler.ResolveActor(state.ActorId, out Actor actor)
			|| actor?.data == null
			|| !actor.isAlive())
		{
			return;
		}

		XjGuoWeiQuanBingActorSnapshot.WriteActive(actor, state);
	}

	private static bool IsAliveJinDan(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (actor?.data == null || !actor.isAlive() || !XjJinDanAccessor.BuildPositionCarrierState(actor).Found)
		{
			return false;
		}

		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}

	private static XjGuoWeiQuanBingState NormalizeForLiveActor(Actor actor, in XjGuoWeiQuanBingState source)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string actorGuoWei);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);
		XjHighRealmDaoStateService.ResolvePositionIdentity(
			actor, actorGuoWei, out _, out string liveDaoTu);
		if (string.IsNullOrWhiteSpace(liveDaoTu))
		{
			liveDaoTu = string.IsNullOrWhiteSpace(source.DaoTu) ? actorDaoTu : source.DaoTu;
		}
		string liveGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(
			string.IsNullOrWhiteSpace(actorGuoWei) ? source.GuoWei : actorGuoWei);
		string liveType = XjGuoWeiRegistry.ResolveTypeFromName(liveGuoWei);
		if (string.Equals(liveType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !string.Equals(
				XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(liveGuoWei), liveDaoTu, StringComparison.Ordinal))
		{
			liveGuoWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
				liveDaoTu, liveType, XjGuoWeiCalculator.ResolveSlotIndex(liveGuoWei));
		}

		string localQuanBing = source.LocalQuanBing;
		string guoWeiZhongAi = source.GuoWeiZhongAi;
		string summary = source.Summary;
		if (XjGuoWeiQuanBingLifecycle.TryBuildReadOnlyRecoveryState(actor, out XjGuoWeiQuanBingState rebuilt))
		{
			string currentGuoWei = liveGuoWei;
			string currentType = liveType;
			bool mustUseDerivedAuthority = string.Equals(currentType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				|| string.Equals(currentType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
			// 旧档余闰权柄曾直接保存果位根权柄，数量虽然相同，语义已经错误。
			// 动态位置文档存在后必须以派生权柄覆盖；果位仍按完整度补全六权。
			if ((!string.IsNullOrWhiteSpace(rebuilt.LocalQuanBing) && mustUseDerivedAuthority)
				|| CountAuthorities(rebuilt.LocalQuanBing) > CountAuthorities(localQuanBing))
			{
				localQuanBing = rebuilt.LocalQuanBing;
			}
			if (string.IsNullOrWhiteSpace(guoWeiZhongAi))
			{
				guoWeiZhongAi = rebuilt.GuoWeiZhongAi;
			}
			if (string.IsNullOrWhiteSpace(summary)
				|| summary.Contains("权柄明细无可恢复快照", StringComparison.Ordinal))
			{
				summary = string.IsNullOrWhiteSpace(source.SeizedQuanBing)
					&& string.IsNullOrWhiteSpace(source.ForeignQuanBing)
					? "读档恢复本地权柄；夺取与外道权柄以持久快照为准"
					: "读档恢复权柄运行态";
			}
		}

		return new XjGuoWeiQuanBingState(
			true,
			actorId,
			actor.getName(),
			liveDaoTu,
			liveGuoWei,
			localQuanBing,
			source.SeizedQuanBing,
			source.ForeignQuanBing,
			source.WithdrawnToDongTian,
			guoWeiZhongAi,
			source.PendingExternalZhengWeiDaoTu,
			source.LockUntilYear,
			source.IntegrationRetreatActive,
			source.IntegrationRetreatEndYear,
			string.IsNullOrWhiteSpace(summary) ? "读档恢复权柄运行态" : summary,
			StatusActive,
			source.AcquiredYear > 0 ? source.AcquiredYear : Math.Max(0, successYear),
			0,
			string.Empty,
			source.SeizedQuanBingSources);
	}

	private static int CountAuthorities(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return 0;
		}

		return raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Length;
	}

	private static void Touch(bool protectedCommit)
	{
		unchecked { revision++; }
		XjWorldArchiveSystem.MarkChanged();
		if (protectedCommit)
		{
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
	}
}
