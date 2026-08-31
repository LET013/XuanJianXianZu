using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanImmortalityRegistry
{
	private static readonly Dictionary<long, XjJinDanImmortalityArchiveRecord> ByActorId = new Dictionary<long, XjJinDanImmortalityArchiveRecord>();
	private static IReadOnlyList<XjJinDanImmortalityArchiveRecord> CachedReadAll = Array.Empty<XjJinDanImmortalityArchiveRecord>();
	private static int _recordRevision = 1;
	private static int _cachedReadRevision;

	internal static int Count => ByActorId.Count;

	internal static bool IsNaturalDeathExempt(Actor actor)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || XjGuZunRegistry.IsManifestationActor(actor)) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjRealmHelper.GetOrder(realmId) < XjRealmHelper.GetOrder(XjRealmIds.JinDan))
		{
			return false;
		}
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		if (shenDan.Found)
		{
			return XjScheduler.ResolveActor(shenDan.AnchorActorId, out Actor anchor)
				&& XjSafeCore.IsAliveActor(anchor);
		}
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (jinDan.Found && !string.IsNullOrWhiteSpace(jinDan.GuoWei)
			&& jinDan.GuoWei.IndexOf(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) >= 0)
		{
			return false;
		}
		return true;
	}

	internal static bool IsPastYinSiAttentionAge(Actor actor)
	{
		if (!IsNaturalDeathExempt(actor) || actor?.data == null)
		{
			return false;
		}
		if (XjShenDanAccessor.BuildState(actor).Found)
		{
			return false;
		}

		float lifespan = XjRealmLifespanService.GetFiniteYinSiAttentionLifespan(actor);
		if (lifespan <= 0f)
		{
			return false;
		}
		return Math.Max(0f, actor.getAge()) > lifespan;
	}

	internal static bool EnsureActivated(Actor actor, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || XjGuZunRegistry.IsManifestationActor(actor) || !IsNaturalDeathExempt(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (ByActorId.TryGetValue(actorId, out XjJinDanImmortalityArchiveRecord existing))
		{
			bool changed = !existing.IsAlive;
			bool persistedChanged = changed || existing.DeathYear != 0 || !string.IsNullOrEmpty(existing.DeathReason);
			existing.IsAlive = true;
			existing.DeathYear = 0;
			existing.DeathReason = string.Empty;
			persistedChanged |= CaptureDisplaySnapshot(existing, actor, Math.Max(0, currentYear), forceAliveStatus: true);
			// P4 archive section caching means display/affiliation/authority snapshot updates must
			// invalidate HighRealm persistence, but repeated EnsureActivated calls in the same
			// unchanged state must stay allocation/dirty-free.
			if (persistedChanged)
			{
				NotifyRecordChanged();
				XjWorldArchiveSystem.MarkChanged();
				XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan);
			}
			return changed;
		}

		int year = Math.Max(0, currentYear);
		XjJinDanImmortalityArchiveRecord created = new XjJinDanImmortalityArchiveRecord
		{
			ActorId = actorId,
			ActivatedYear = year,
			LastExposureUpdateYear = year,
			YinSiState = XjJinDanYinSiState.Hidden,
			IsAlive = true
		};
		CaptureDisplaySnapshot(created, actor, year, forceAliveStatus: true);
		ByActorId.Add(actorId, created);
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan);
		return true;
	}

	internal static void MarkDead(Actor actor)
	{
		int currentYear = 0;
		try { currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch (System.Exception xjCaught88) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanImmortalityRegistry.cs:88", xjCaught88); }
		MarkDead(actor, currentYear);
	}

	internal static void MarkDead(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjGuZunRegistry.IsManifestationActor(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (!ByActorId.TryGetValue(actorId, out XjJinDanImmortalityArchiveRecord record))
		{
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (XjRealmHelper.GetOrder(realmId) < XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return;
			int observedYear = Math.Max(0, currentYear);
			record = new XjJinDanImmortalityArchiveRecord
			{
				ActorId = actorId,
				ActivatedYear = observedYear,
				LastExposureUpdateYear = observedYear,
				YinSiState = XjJinDanYinSiState.Hidden,
				IsAlive = true
			};
			ByActorId[actorId] = record;
		}
		if (!record.IsAlive && string.Equals(record.YinSiState, XjJinDanYinSiState.Dead, StringComparison.Ordinal)) return;
		int deathYear = Math.Max(0, currentYear);
		// Actor 尚未被原生池回收，此时抓取最后一份完整高境快照。
		CaptureDisplaySnapshot(record, actor, deathYear, forceAliveStatus: false);
		record.IsAlive = false;
		record.YinSiState = XjJinDanYinSiState.Dead;
		record.DeathYear = deathYear;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, out string deathReason);
		record.DeathReason = (deathReason ?? string.Empty).Trim();
		record.LifeStatus = IsReincarnationReason(record.DeathReason) ? "转世" : "死亡";
		record.LastKnownYear = Math.Max(record.LastKnownYear, deathYear);
		record.LastObservedYear = Math.Max(record.LastObservedYear, deathYear);
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
	}

	/// <summary>
	/// 技术换身不应制造死亡档案。若目标仍保有金丹以上身份，则把当前阴司/长生
	/// 状态原位换键；若目标因物种门禁脱离玄鉴修炼，则移除这条“仍存活”记录，
	/// 避免旧 actorId 永久作为活金丹残留。
	/// </summary>
	internal static bool MarkRuntimeUnavailable(long actorId, int currentYear)
	{
		if (actorId <= 0L || !ByActorId.TryGetValue(actorId, out XjJinDanImmortalityArchiveRecord record) || record == null || !record.IsAlive)
			return false;
		record.IsAlive = false;
		record.LifeStatus = "失联";
		record.LastKnownYear = Math.Max(record.LastKnownYear, Math.Max(0, currentYear));
		record.LastObservedYear = Math.Max(record.LastObservedYear, Math.Max(0, currentYear));
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
		return true;
	}

	internal static bool RebindActorAfterExternalReplacement(long oldActorId, Actor target, int currentYear)
	{
		if (oldActorId <= 0L || target?.data == null || !ByActorId.TryGetValue(oldActorId, out XjJinDanImmortalityArchiveRecord record))
			return false;
		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId) return false;

		ByActorId.Remove(oldActorId);
		if (IsNaturalDeathExempt(target))
		{
			record.ActorId = newActorId;
			record.IsAlive = true;
			record.DeathYear = 0;
			record.DeathReason = string.Empty;
			CaptureDisplaySnapshot(record, target, Math.Max(0, currentYear), forceAliveStatus: true);
			ByActorId[newActorId] = record;
		}
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
		return true;
	}

	internal static bool TryGet(long actorId, out XjJinDanImmortalityArchiveRecord record) => ByActorId.TryGetValue(actorId, out record);

	internal static bool RemoveByActorId(long actorId)
	{
		if (actorId <= 0L || !ByActorId.Remove(actorId)) return false;
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
		return true;
	}

	internal static bool RemoveForRealmLoss(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !ByActorId.Remove(actorId)) return false;
		NotifyRecordChanged();
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
		return true;
	}

	internal static IReadOnlyList<XjJinDanImmortalityArchiveRecord> ReadAll()
	{
		if (_cachedReadRevision == _recordRevision) return CachedReadAll;
		if (ByActorId.Count == 0)
		{
			CachedReadAll = Array.Empty<XjJinDanImmortalityArchiveRecord>();
			_cachedReadRevision = _recordRevision;
			return CachedReadAll;
		}

		XjJinDanImmortalityArchiveRecord[] result = new XjJinDanImmortalityArchiveRecord[ByActorId.Count];
		int index = 0;
		foreach (XjJinDanImmortalityArchiveRecord record in ByActorId.Values)
		{
			if (record != null) result[index++] = Clone(record);
		}
		if (index != result.Length) Array.Resize(ref result, index);
		Array.Sort(result, (left, right) => left.ActorId.CompareTo(right.ActorId));
		CachedReadAll = result.Length == 0
			? Array.Empty<XjJinDanImmortalityArchiveRecord>()
			: Array.AsReadOnly(result);
		_cachedReadRevision = _recordRevision;
		return CachedReadAll;
	}

	internal static void ExportArchiveRecords(List<XjJinDanImmortalityArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> snapshot = ReadAll();
		for (int i = 0; i < snapshot.Count; i++)
		{
			if (snapshot[i] != null) target.Add(Clone(snapshot[i]));
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjJinDanImmortalityArchiveRecord> source)
	{
		ByActorId.Clear();
		if (source != null)
		{
			for (int i = 0; i < source.Count; i++)
			{
				XjJinDanImmortalityArchiveRecord record = source[i];
				if (record == null || record.ActorId <= 0L) continue;
				ByActorId[record.ActorId] = Clone(record);
			}
		}
		NotifyRecordChanged();
	}

	internal static void NotifyRecordChanged()
	{
		unchecked
		{
			_recordRevision++;
			if (_recordRevision <= 0) _recordRevision = 1;
		}
	}

	internal static void Clear()
	{
		ByActorId.Clear();
		CachedReadAll = Array.Empty<XjJinDanImmortalityArchiveRecord>();
		NotifyRecordChanged();
		_cachedReadRevision = 0;
	}

	private static XjJinDanImmortalityArchiveRecord Clone(XjJinDanImmortalityArchiveRecord source)
	{
		return new XjJinDanImmortalityArchiveRecord
		{
			ActorId = source.ActorId,
			ActivatedYear = source.ActivatedYear,
			YinSiExposure = Math.Max(0f, source.YinSiExposure),
			LastExposureReason = source.LastExposureReason ?? string.Empty,
			LastExposureUpdateYear = source.LastExposureUpdateYear,
			YinSiKnown = source.YinSiKnown,
			YinSiState = string.IsNullOrWhiteSpace(source.YinSiState) ? XjJinDanYinSiState.Hidden : source.YinSiState,
			PursuitCount = Math.Max(0, source.PursuitCount),
			LastKnownYear = source.LastKnownYear,
			IsAlive = source.IsAlive,
			Name = source.Name ?? string.Empty,
			Age = Math.Max(0, source.Age),
			RealmId = source.RealmId ?? string.Empty,
			CultivationPath = source.CultivationPath ?? string.Empty,
			Achievement = source.Achievement ?? string.Empty,
			DaoTu = source.DaoTu ?? string.Empty,
			JinXing = source.JinXing ?? string.Empty,
			GuoWei = source.GuoWei ?? string.Empty,
			IsDaoTai = source.IsDaoTai,
			IsKongZheng = source.IsKongZheng,
			SectId = Math.Max(0L, source.SectId),
			SectName = source.SectName ?? string.Empty,
			FamilyId = Math.Max(0L, source.FamilyId),
			DaoTaiPresenceStatus = source.DaoTaiPresenceStatus ?? string.Empty,
			DaoTaiNextReturnYear = Math.Max(0, source.DaoTaiNextReturnYear),
			AuthorityCount = Math.Max(0, source.AuthorityCount),
			LocalAuthoritySummary = source.LocalAuthoritySummary ?? string.Empty,
			SeizedAuthoritySummary = source.SeizedAuthoritySummary ?? string.Empty,
			PendingAuthoritySummary = source.PendingAuthoritySummary ?? string.Empty,
			AuthorityLifecycleStatus = source.AuthorityLifecycleStatus ?? string.Empty,
			IntegrationRetreatActive = source.IntegrationRetreatActive,
			IntegrationRetreatEndYear = Math.Max(0, source.IntegrationRetreatEndYear),
			GuoWeiImageSummary = source.GuoWeiImageSummary ?? string.Empty,
			ShenTongMutationSummary = source.ShenTongMutationSummary ?? string.Empty,
			DaoXing = Math.Max(0, source.DaoXing),
			XiuChi = Math.Max(0, source.XiuChi),
			LifeStatus = string.IsNullOrWhiteSpace(source.LifeStatus) ? (source.IsAlive ? "存世" : "死亡") : source.LifeStatus,
			DeathYear = Math.Max(0, source.DeathYear),
			DeathReason = source.DeathReason ?? string.Empty,
			LastObservedYear = Math.Max(0, source.LastObservedYear)
		};
	}

	private static bool CaptureDisplaySnapshot(
		XjJinDanImmortalityArchiveRecord record,
		Actor actor,
		int currentYear,
		bool forceAliveStatus)
	{
		if (record == null || actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		bool changed = false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		realmId = XjRealmHelper.NormalizeId(realmId);
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool isDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		bool isJieLin = XjXuanJianShenTongSpecials.IsJieLinXian(actor);
		bool isYuYi = XjXuanJianShenTongSpecials.IsYuYiXian(actor);
		bool isKongZheng = XjQingXuanKongZhengSystem.HasCompletedKongZheng(actor)
			|| XjFuQiSwordWorldState.IsFounderActor(actorId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string persistedJinXing);

		string nextName = actor.getName() ?? record.Name ?? string.Empty;
		if (!Same(record.Name, nextName)) { record.Name = nextName; changed = true; }
		int nextAge = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		if (record.Age != nextAge) { record.Age = nextAge; changed = true; }
		string nextRealmId = realmId ?? string.Empty;
		if (!Same(record.RealmId, nextRealmId)) { record.RealmId = nextRealmId; changed = true; }
		string nextPath = isFuQi ? "服气养性" : "紫府金丹";
		if (!Same(record.CultivationPath, nextPath)) { record.CultivationPath = nextPath; changed = true; }
		if (record.IsDaoTai != isDaoTai) { record.IsDaoTai = isDaoTai; changed = true; }
		if (record.IsKongZheng != isKongZheng) { record.IsKongZheng = isKongZheng; changed = true; }
		string nextAchievement = isDaoTai ? "道胎"
			: isKongZheng ? "空证真君"
			: isYuYi ? "郁仪仙"
			: isJieLin ? "结璘仙"
			: shenDan.Found ? "神丹真君"
			: isFuQi ? "真君羽士" : "金丹真君";
		if (!Same(record.Achievement, nextAchievement)) { record.Achievement = nextAchievement; changed = true; }
		string nextDaoTu = daoTu ?? string.Empty;
		if (!Same(record.DaoTu, nextDaoTu)) { record.DaoTu = nextDaoTu; changed = true; }
		string nextJinXing = string.IsNullOrWhiteSpace(persistedJinXing)
			? (jinDan.JinXing ?? string.Empty)
			: persistedJinXing.Trim();
		if (!Same(record.JinXing, nextJinXing)) { record.JinXing = nextJinXing; changed = true; }

		string actualGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(jinDan.GuoWei);
		bool specialImmortalHasPosition = (isYuYi || isJieLin)
			&& !string.IsNullOrWhiteSpace(jinDan.GuoWei)
			&& (jinDan.GuoWei.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				|| jinDan.GuoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				|| jinDan.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal));
		string nextGuoWei = specialImmortalHasPosition ? actualGuoWei
			: isYuYi ? "太阳果位垂照"
			: isJieLin ? "太阴果位垂照"
			: shenDan.Found ? XjGuoWeiCalculator.GetDisplayGuoWeiName(shenDan.GuoWei)
			: actualGuoWei;
		if (!Same(record.GuoWei, nextGuoWei)) { record.GuoWei = nextGuoWei; changed = true; }

		if (XjSectRepository.TryGetByActor(actor, out XjSectArchiveRecord sect) && sect != null)
		{
			long nextSectId = Math.Max(0L, sect.SectId);
			if (record.SectId != nextSectId) { record.SectId = nextSectId; changed = true; }
			string nextSectName = sect.Name ?? string.Empty;
			if (!Same(record.SectName, nextSectName)) { record.SectName = nextSectName; changed = true; }
		}
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId);
		long nextFamilyId = Math.Max(0L, familyId);
		if (record.FamilyId != nextFamilyId) { record.FamilyId = nextFamilyId; changed = true; }

		if (isDaoTai && XjDaoTaiPresenceArchive.TryGetRecord(actorId, out XjDaoTaiPresenceArchiveRecord presence) && presence != null)
		{
			string nextPresenceStatus = presence.Status ?? string.Empty;
			if (!Same(record.DaoTaiPresenceStatus, nextPresenceStatus)) { record.DaoTaiPresenceStatus = nextPresenceStatus; changed = true; }
			int nextReturnYear = Math.Max(0, presence.NextReturnYear);
			if (record.DaoTaiNextReturnYear != nextReturnYear) { record.DaoTaiNextReturnYear = nextReturnYear; changed = true; }
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
		int nextDaoXing = Math.Max(0, daoXing);
		if (record.DaoXing != nextDaoXing) { record.DaoXing = nextDaoXing; changed = true; }
		int nextXiuChi = Math.Max(0, xiuChi);
		if (record.XiuChi != nextXiuChi) { record.XiuChi = nextXiuChi; changed = true; }
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState authorityState))
		{
			string nextLocal = authorityState.LocalQuanBing ?? string.Empty;
			if (!Same(record.LocalAuthoritySummary, nextLocal)) { record.LocalAuthoritySummary = nextLocal; changed = true; }
			string nextSeized = authorityState.SeizedQuanBing ?? string.Empty;
			if (!Same(record.SeizedAuthoritySummary, nextSeized)) { record.SeizedAuthoritySummary = nextSeized; changed = true; }
			string nextPending = authorityState.ForeignQuanBing ?? string.Empty;
			if (!Same(record.PendingAuthoritySummary, nextPending)) { record.PendingAuthoritySummary = nextPending; changed = true; }
			string nextLifecycle = authorityState.LifecycleStatus ?? string.Empty;
			if (!Same(record.AuthorityLifecycleStatus, nextLifecycle)) { record.AuthorityLifecycleStatus = nextLifecycle; changed = true; }
			if (record.IntegrationRetreatActive != authorityState.IntegrationRetreatActive)
			{
				record.IntegrationRetreatActive = authorityState.IntegrationRetreatActive;
				changed = true;
			}
			int nextRetreatEndYear = Math.Max(0, authorityState.IntegrationRetreatEndYear);
			if (record.IntegrationRetreatEndYear != nextRetreatEndYear)
			{
				record.IntegrationRetreatEndYear = nextRetreatEndYear;
				changed = true;
			}
			int nextAuthorityCount = CountAuthorityValues(authorityState.LocalQuanBing) + CountAuthorityValues(authorityState.SeizedQuanBing);
			if (record.AuthorityCount != nextAuthorityCount) { record.AuthorityCount = nextAuthorityCount; changed = true; }
		}
		if (XjHighRealmAggregateStore.TryGetAuthorityCount(actorId, out int authorityCount))
		{
			int nextAuthorityCount = Math.Max(0, authorityCount);
			if (record.AuthorityCount != nextAuthorityCount) { record.AuthorityCount = nextAuthorityCount; changed = true; }
		}
		if (XjHighRealmAggregateStore.TryGetImageSummary(actorId, out string imageSummary))
		{
			string nextImageSummary = imageSummary ?? string.Empty;
			if (!Same(record.GuoWeiImageSummary, nextImageSummary)) { record.GuoWeiImageSummary = nextImageSummary; changed = true; }
		}
		string nextMutationSummary = BuildMutationSummary(actorId);
		if (!Same(record.ShenTongMutationSummary, nextMutationSummary)) { record.ShenTongMutationSummary = nextMutationSummary; changed = true; }
		int nextObservedYear = Math.Max(record.LastObservedYear, Math.Max(0, currentYear));
		if (record.LastObservedYear != nextObservedYear) { record.LastObservedYear = nextObservedYear; changed = true; }
		if (forceAliveStatus)
		{
			string nextLifeStatus = ResolveLiveStatus(actor, isDaoTai, record.DaoTaiPresenceStatus);
			if (!Same(record.LifeStatus, nextLifeStatus)) { record.LifeStatus = nextLifeStatus; changed = true; }
		}
		return changed;
	}

	private static bool Same(string left, string right)
	{
		return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
	}

	private static string ResolveLiveStatus(Actor actor, bool isDaoTai, string daoTaiPresenceStatus)
	{
		if (isDaoTai)
		{
			string status = (daoTaiPresenceStatus ?? string.Empty).Trim();
			if (string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal)
				|| string.Equals(status, XjDaoTaiPresenceStatus.LegacyBeyondWorld, StringComparison.Ordinal)) return "天外";
			if (string.Equals(status, XjDaoTaiPresenceStatus.ClosedCultivation, StringComparison.Ordinal)) return "闭关";
		}
		if (XjClosedCultivationGuard.IsInClosedCultivation(actor)) return "闭关";
		return "存世";
	}

	private static bool IsReincarnationReason(string reason)
	{
		return !string.IsNullOrWhiteSpace(reason)
			&& reason.IndexOf("Reincarnation", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static int CountAuthorityValues(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return 0;
		return raw.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries).Length;
	}

	private static string BuildMutationSummary(long actorId)
	{
		if (!XjHighRealmAggregateStore.TryGetMutationBindings(actorId, out IReadOnlyList<XjHighRealmMutationBinding> bindings)
			|| bindings == null || bindings.Count == 0) return string.Empty;
		List<string> rows = new List<string>(Math.Min(3, bindings.Count));
		for (int i = 0; i < bindings.Count && rows.Count < 3; i++)
		{
			XjHighRealmMutationBinding binding = bindings[i];
			if (!binding.IsValid) continue;
			rows.Add(binding.Lower + " → " + binding.Upper + "（" + binding.SourceDaoTu + "·" + binding.Authority + "）");
		}
		return string.Join("；", rows);
	}
}
