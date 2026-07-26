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
			TryWriteActiveActorSnapshot(state);
		}
		else if (activeEntriesByActorId.Remove(state.ActorId))
		{
			changed = true;
		}

		if (changed)
		{
			Touch(protectedCommit: true);
		}
	}

	internal static bool TryGet(long actorId, out XjGuoWeiQuanBingState state)
	{
		if (actorId > 0L && activeEntriesByActorId.TryGetValue(actorId, out state))
		{
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
	/// 0.5.4果位钟爱正位锁：锁属于道途正位，而不是死者仍占据的运行时果位。
	/// 锁期内仅当前果位钟爱转世承载者可以占据该道途正位；没有承载者时所有人均被拦截。
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
		return true;
	}

	/// <summary>
	/// 世界正式保存前统一固化活体金丹权柄。保存属于冷路径，允许扫描一次世界角色：
	/// 同时更新角色原生镜像与世界权柄档案，保证“保存后立刻读档”也不会丢失。
	/// </summary>
	internal static void PrepareLiveSnapshotsForSave()
	{
		// 正常运行只遍历金丹索引；仅在读档 bootstrap 尚未完成时退回
		// 世界扫描，保证玩家刚载入便保存也不会遗漏。
		if (!XjWorldBootstrapLane.HasPending)
		{
			IReadOnlyList<long> jinDanIds = XjCultivatorCache.GetJinDanIds();
			for (int i = 0; i < jinDanIds.Count; i++)
			{
				if (!XjScheduler.ResolveActor(jinDanIds[i], out Actor actor)
					|| !TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState state))
				{
					continue;
				}
				Record(state);
			}
			return;
		}

		IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
		if (units == null)
		{
			return;
		}
		for (int i = 0; i < units.Count; i++)
		{
			if (TryGetForLiveDisplay(units[i], out XjGuoWeiQuanBingState state))
			{
				Record(state);
			}
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
		return lostAuthoritiesByKey.ContainsKey(BuildLostAuthorityKey(sourceDaoTu, authority));
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
		int required = (guoWei ?? string.Empty).Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			? XjGuoWeiQuanBingRules.YuWeiSlotCount
			: XjGuoWeiQuanBingRules.RunWeiSlotCount;
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
		Touch(protectedCommit: true);
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
			XjGuoWeiQuanBingState candidate = new XjGuoWeiQuanBingState(
				true,
				record.ActorId,
				record.ActorName,
				record.DaoTu,
				record.GuoWei,
				record.LocalQuanBing,
				record.SeizedQuanBing,
				record.ForeignQuanBing,
				record.WithdrawnToDongTian,
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
				record.SeizedQuanBingSources);

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
			}
		}
	}

	internal static void ImportLostAuthorityRecords(IEnumerable<XjGuoWeiQuanBingLostAuthorityArchiveData> records)
	{
		lostAuthoritiesByKey.Clear();
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

			lostAuthoritiesByKey[BuildLostAuthorityKey(record.SourceDaoTu, record.Authority)] = new XjGuoWeiQuanBingLostAuthorityArchiveData
			{
				SourceDaoTu = Normalize(record.SourceDaoTu),
				Authority = Normalize(record.Authority),
				TargetDaoTu = Normalize(record.TargetDaoTu),
				Year = Math.Max(0, record.Year),
				Reason = Normalize(record.Reason)
			};
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
		activeEntriesByActorId.Clear();
		historyEntriesByActorId.Clear();
		lostAuthoritiesByKey.Clear();
	}

	private static string Format(in XjGuoWeiQuanBingState state)
	{
		string daoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? "未定道途" : state.DaoTu.Trim();
		string guoWei = string.IsNullOrWhiteSpace(state.GuoWei) ? "未知果位" : state.GuoWei.Trim();
		string lockText = state.LockUntilYear > 0 ? " - 封锁至" + state.LockUntilYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
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
		if (actor?.data == null || !actor.isAlive() || !XjJinDanAccessor.BuildState(actor).Found)
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

		string localQuanBing = source.LocalQuanBing;
		string guoWeiZhongAi = source.GuoWeiZhongAi;
		string summary = source.Summary;
		if (XjGuoWeiQuanBingLifecycle.TryBuildReadOnlyRecoveryState(actor, out XjGuoWeiQuanBingState rebuilt))
		{
			if (CountAuthorities(rebuilt.LocalQuanBing) > CountAuthorities(localQuanBing))
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
			string.IsNullOrWhiteSpace(source.DaoTu) ? actorDaoTu : source.DaoTu,
			string.IsNullOrWhiteSpace(source.GuoWei) ? actorGuoWei : source.GuoWei,
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
		XjWorldArchiveSystem.MarkChanged();
		if (protectedCommit)
		{
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
	}
}
