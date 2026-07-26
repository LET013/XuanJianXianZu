using System;
using System.Collections.Generic;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Core;

[Flags]
internal enum XjProgressionCandidateFlags : ushort
{
	None = 0,
	GongFa = 1 << 0,
	CaiQi = 1 << 1,
	Breakthrough = 1 << 2,
	QingXuan = 1 << 3,
	HighRealm = 1 << 4,
	ZiFu = 1 << 5,
	JinDan = 1 << 6
}

internal enum XjProgressionBlockReason : byte
{
	None = 0,
	NoAptitude = 1,
	NoNextRealm = 2,
	RuleNotImplemented = 3,
	ZhenYuan = 4,
	CaiQi = 5,
	GongFa = 6,
	Chronology = 7,
	Aptitude = 8,
	AptitudeInjury = 9,
	ZaQiRealmLock = 10,
	FiveXianJi = 11,
	QiuJinFa = 12,
	HighRealmManaged = 13
}

internal readonly struct XjProgressionCandidateState
{
	internal readonly int RealmTier;
	internal readonly string RealmId;
	internal readonly string TargetRealmId;
	internal readonly XjProgressionCandidateFlags Flags;
	internal readonly XjProgressionBlockReason BlockReason;
	internal readonly float RequiredZhenYuan;
	internal readonly int NextGongFaYear;
	internal readonly int NextCaiQiYear;
	internal readonly int NextBreakthroughYear;

	internal XjProgressionCandidateState(
		int realmTier,
		string realmId,
		string targetRealmId,
		XjProgressionCandidateFlags flags,
		XjProgressionBlockReason blockReason,
		float requiredZhenYuan,
		int nextGongFaYear,
		int nextCaiQiYear,
		int nextBreakthroughYear)
	{
		RealmTier = realmTier;
		RealmId = realmId ?? string.Empty;
		TargetRealmId = targetRealmId ?? string.Empty;
		Flags = flags;
		BlockReason = blockReason;
		RequiredZhenYuan = Math.Max(0f, requiredZhenYuan);
		NextGongFaYear = Math.Max(0, nextGongFaYear);
		NextCaiQiYear = Math.Max(0, nextCaiQiYear);
		NextBreakthroughYear = Math.Max(0, nextBreakthroughYear);
	}

	internal bool Has(XjProgressionCandidateFlags flag) => (Flags & flag) != 0;
	internal bool ShouldProcessGongFa(int currentYear) => Has(XjProgressionCandidateFlags.GongFa)
		&& currentYear > 0 && (NextGongFaYear <= 0 || NextGongFaYear <= currentYear);
	internal bool ShouldProcessCaiQi(int currentYear) => Has(XjProgressionCandidateFlags.CaiQi)
		&& currentYear > 0 && (NextCaiQiYear <= 0 || NextCaiQiYear <= currentYear);
	internal bool ShouldProcessBreakthrough(int currentYear) => Has(XjProgressionCandidateFlags.Breakthrough)
		&& currentYear > 0 && (NextBreakthroughYear <= 0 || NextBreakthroughYear <= currentYear);
}

internal readonly struct XjProgressionCandidateCounts
{
	internal readonly int Indexed;
	internal readonly int Dirty;
	internal readonly int ZhenYuanBlocked;
	internal readonly int GongFaBlocked;
	internal readonly int CaiQiBlocked;
	internal readonly int ChronologyBlocked;
	internal readonly int BreakthroughReady;
	internal readonly int GongFaDue;
	internal readonly int CaiQiDue;
	internal readonly int ZiFuTracked;
	internal readonly int JinDanTracked;

	internal XjProgressionCandidateCounts(
		int indexed,
		int dirty,
		int zhenYuanBlocked,
		int gongFaBlocked,
		int caiQiBlocked,
		int chronologyBlocked,
		int breakthroughReady,
		int gongFaDue,
		int caiQiDue,
		int ziFuTracked,
		int jinDanTracked)
	{
		Indexed = indexed;
		Dirty = dirty;
		ZhenYuanBlocked = zhenYuanBlocked;
		GongFaBlocked = gongFaBlocked;
		CaiQiBlocked = caiQiBlocked;
		ChronologyBlocked = chronologyBlocked;
		BreakthroughReady = breakthroughReady;
		GongFaDue = gongFaDue;
		CaiQiDue = caiQiDue;
		ZiFuTracked = ziFuTracked;
		JinDanTracked = jinDanTracked;
	}
}

/// <summary>
/// Specialized candidate sets for systems that previously scanned every
/// aptitude holder. Realm/DaoTu sets serve world systems; progression state
/// routes annual cultivation into full GongFa/CaiQi/breakthrough rules only
/// when a persisted due year or a state transition makes the actor relevant.
/// </summary>
internal static class XjCultivatorCandidateIndex
{
	private static readonly HashSet<long> RealmEnteredIds = new HashSet<long>();
	private static readonly Dictionary<string, HashSet<long>> ZhuJiIdsByDaoTu =
		new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
	private static readonly Dictionary<long, string> ZhuJiDaoTuByActorId = new Dictionary<long, string>();
	private static long[] realmEnteredSnapshot = Array.Empty<long>();
	private static bool realmEnteredSnapshotDirty;
	private static readonly Dictionary<string, long[]> ZhuJiSnapshotsByDaoTu =
		new Dictionary<string, long[]>(StringComparer.Ordinal);
	private static readonly HashSet<string> DirtyZhuJiSnapshotDaoTu = new HashSet<string>(StringComparer.Ordinal);

	private static readonly Dictionary<long, XjProgressionCandidateState> ProgressionStates =
		new Dictionary<long, XjProgressionCandidateState>();
	private static readonly HashSet<long> DirtyProgressionIds = new HashSet<long>();
	private static readonly HashSet<long> ZhenYuanChangedIds = new HashSet<long>();
	private static readonly HashSet<long> ZiFuProgressionIds = new HashSet<long>();
	private static readonly HashSet<long> JinDanProgressionIds = new HashSet<long>();

	internal static void Observe(Actor actor, bool isCultivator, int realmTier)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return;
		}

		SetRealmEntered(actorId, isCultivator && realmTier > XjRealmSuppression.TierNone);
		if (isCultivator && realmTier == XjRealmSuppression.TierZhuJi)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			SetZhuJiDaoTu(actorId, NormalizeDaoTu(daoTu));
		}
		else
		{
			SetZhuJiDaoTu(actorId, string.Empty);
		}

		if (isCultivator && actor?.data != null && actor.isAlive())
		{
			if (!ProgressionStates.TryGetValue(actorId, out XjProgressionCandidateState state)
				|| state.RealmTier != realmTier)
			{
				RefreshProgression(actor, ResolveCurrentYear(0));
			}
			else
			{
				SetMembership(ZiFuProgressionIds, actorId, realmTier == XjRealmSuppression.TierZiFu);
				SetMembership(JinDanProgressionIds, actorId, realmTier >= XjRealmSuppression.TierJinDan);
			}
		}
		else
		{
			RemoveProgression(actorId);
		}
	}

	internal static void RefreshDaoTu(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier != XjRealmSuppression.TierZhuJi)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		SetZhuJiDaoTu(actorId, NormalizeDaoTu(daoTu));
		MarkProgressionDirty(actorId);
	}

	internal static XjProgressionCandidateState GetOrRefreshProgression(Actor actor, int currentYear)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || actor?.data == null || !actor.isAlive())
		{
			return default;
		}

		if (!ProgressionStates.TryGetValue(actorId, out XjProgressionCandidateState state)
			|| DirtyProgressionIds.Contains(actorId))
		{
			return RefreshProgression(actor, currentYear);
		}
		if (ZhenYuanChangedIds.Contains(actorId))
		{
			return RefreshAfterGrowth(actor, currentYear);
		}
		return state;
	}

	internal static XjProgressionCandidateState RefreshAfterGrowth(Actor actor, int currentYear)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || actor?.data == null || !actor.isAlive())
		{
			return default;
		}

		if (!ProgressionStates.TryGetValue(actorId, out XjProgressionCandidateState state)
			|| DirtyProgressionIds.Contains(actorId))
		{
			return RefreshProgression(actor, currentYear);
		}

		if (!ZhenYuanChangedIds.Remove(actorId))
		{
			return state;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		bool reachedThreshold = state.BlockReason == XjProgressionBlockReason.ZhenYuan
			&& state.RequiredZhenYuan > 0f
			&& zhenYuan >= state.RequiredZhenYuan;
		bool fellBelowThreshold = state.Has(XjProgressionCandidateFlags.Breakthrough)
			&& state.RequiredZhenYuan > 0f
			&& zhenYuan < state.RequiredZhenYuan;
		return reachedThreshold || fellBelowThreshold
			? RefreshProgression(actor, currentYear)
			: state;
	}

	internal static XjProgressionCandidateState RefreshProgression(Actor actor, int currentYear)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || actor?.data == null || !actor.isAlive()
			|| !XjCultivatorCache.IsCultivator(actorId))
		{
			RemoveProgression(actorId);
			return default;
		}

		bool wasDirty = DirtyProgressionIds.Contains(actorId);
		int year = ResolveCurrentYear(currentYear);
		XjProgressionCandidateState state = BuildProgressionState(actor, actorId, year);
		ProgressionStates[actorId] = state;
		DirtyProgressionIds.Remove(actorId);
		ZhenYuanChangedIds.Remove(actorId);
		SetMembership(ZiFuProgressionIds, actorId, state.Has(XjProgressionCandidateFlags.ZiFu));
		SetMembership(JinDanProgressionIds, actorId, state.Has(XjProgressionCandidateFlags.JinDan));
		XjStageZeroObservation.RecordCandidateRefresh(wasDirty);
		return state;
	}

	internal static void NotifyActorDataChanged(Actor actor, string key)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return;
		if (key == XjActorDataKeys.ZhenYuan)
		{
			if (ProgressionStates.ContainsKey(actorId))
			{
				ZhenYuanChangedIds.Add(actorId);
			}
			return;
		}
		if (!IsProgressionKey(key)) return;
		MarkProgressionDirty(actorId);
	}

	internal static void MarkProgressionDirty(Actor actor)
	{
		MarkProgressionDirty(GetActorId(actor));
	}

	internal static void MarkProgressionDirty(long actorId)
	{
		if (actorId > 0L && ProgressionStates.ContainsKey(actorId))
		{
			DirtyProgressionIds.Add(actorId);
		}
	}

	internal static XjProgressionCandidateCounts GetProgressionCounts(int currentYear)
	{
		int year = ResolveCurrentYear(currentYear);
		int zhenYuanBlocked = 0;
		int gongFaBlocked = 0;
		int caiQiBlocked = 0;
		int chronologyBlocked = 0;
		int breakthroughReady = 0;
		int gongFaDue = 0;
		int caiQiDue = 0;
		foreach (XjProgressionCandidateState state in ProgressionStates.Values)
		{
			switch (state.BlockReason)
			{
				case XjProgressionBlockReason.ZhenYuan: zhenYuanBlocked++; break;
				case XjProgressionBlockReason.GongFa: gongFaBlocked++; break;
				case XjProgressionBlockReason.CaiQi: caiQiBlocked++; break;
				case XjProgressionBlockReason.Chronology: chronologyBlocked++; break;
			}
			if (state.ShouldProcessBreakthrough(year)) breakthroughReady++;
			if (state.ShouldProcessGongFa(year)) gongFaDue++;
			if (state.ShouldProcessCaiQi(year)) caiQiDue++;
		}
		return new XjProgressionCandidateCounts(
			ProgressionStates.Count,
			DirtyProgressionIds.Count,
			zhenYuanBlocked,
			gongFaBlocked,
			caiQiBlocked,
			chronologyBlocked,
			breakthroughReady,
			gongFaDue,
			caiQiDue,
			ZiFuProgressionIds.Count,
			JinDanProgressionIds.Count);
	}

	internal static IReadOnlyList<long> GetRealmEnteredIds()
	{
		if (realmEnteredSnapshotDirty)
		{
			realmEnteredSnapshot = new long[RealmEnteredIds.Count];
			RealmEnteredIds.CopyTo(realmEnteredSnapshot);
			realmEnteredSnapshotDirty = false;
		}
		return realmEnteredSnapshot;
	}

	internal static IReadOnlyList<long> GetZhuJiIdsByDaoTu(string daoTu)
	{
		string normalized = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalized)
			|| !ZhuJiIdsByDaoTu.TryGetValue(normalized, out HashSet<long> actorIds)
			|| actorIds.Count == 0)
		{
			return Array.Empty<long>();
		}

		if (!ZhuJiSnapshotsByDaoTu.TryGetValue(normalized, out long[] snapshot)
			|| DirtyZhuJiSnapshotDaoTu.Contains(normalized))
		{
			snapshot = new long[actorIds.Count];
			actorIds.CopyTo(snapshot);
			ZhuJiSnapshotsByDaoTu[normalized] = snapshot;
			DirtyZhuJiSnapshotDaoTu.Remove(normalized);
		}
		return snapshot;
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		if (RealmEnteredIds.Remove(actorId))
		{
			realmEnteredSnapshotDirty = true;
		}
		SetZhuJiDaoTu(actorId, string.Empty);
		RemoveProgression(actorId);
	}

	internal static void Clear()
	{
		RealmEnteredIds.Clear();
		ZhuJiIdsByDaoTu.Clear();
		ZhuJiDaoTuByActorId.Clear();
		realmEnteredSnapshot = Array.Empty<long>();
		realmEnteredSnapshotDirty = false;
		ZhuJiSnapshotsByDaoTu.Clear();
		DirtyZhuJiSnapshotDaoTu.Clear();
		ProgressionStates.Clear();
		DirtyProgressionIds.Clear();
		ZhenYuanChangedIds.Clear();
		ZiFuProgressionIds.Clear();
		JinDanProgressionIds.Clear();
	}

	private static XjProgressionCandidateState BuildProgressionState(Actor actor, long actorId, int currentYear)
	{
		int realmTier = XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier)
			? cachedTier
			: XjRealmSuppression.GetRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		realmId = XjRealmHelper.NormalizeId(realmId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int overlayMask);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);

		XjProgressionCandidateFlags flags = XjProgressionCandidateFlags.None;
		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			flags |= XjProgressionCandidateFlags.HighRealm;
		}
		if (realmTier == XjRealmSuppression.TierZiFu)
		{
			flags |= XjProgressionCandidateFlags.ZiFu;
		}
		if (realmTier >= XjRealmSuppression.TierJinDan)
		{
			flags |= XjProgressionCandidateFlags.JinDan;
		}

		int nextCaiQiYear = 0;
		if (XjCaiQiActorAccessor.ShouldEnqueueForCaiQi(actor))
		{
			flags |= XjProgressionCandidateFlags.CaiQi;
			nextCaiQiYear = XjCaiQiActorAccessor.GetNextCaiQiYear(actor);
			if (nextCaiQiYear <= 0) nextCaiQiYear = Math.Max(1, currentYear);
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (XjQingXuanKongZhengSystem.HasAnnualInterest(actor, realmId, daoTu))
		{
			flags |= XjProgressionCandidateFlags.QingXuan;
		}

		int nextGongFaYear = ResolveNextGongFaYear(actor, realmTier, xjZz, daoTu, currentYear, ref flags);
		if (xjZz <= 0)
		{
			return new XjProgressionCandidateState(realmTier, realmId, string.Empty, flags,
				XjProgressionBlockReason.NoAptitude, 0f, nextGongFaYear, nextCaiQiYear, 0);
		}

		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			return new XjProgressionCandidateState(realmTier, realmId,
				realmTier == XjRealmSuppression.TierZiFu ? XjRealmIds.JinDan : XjRealmIds.ShenDan,
				flags, XjProgressionBlockReason.HighRealmManaged, 0f, nextGongFaYear, nextCaiQiYear, currentYear);
		}

		if (!XjCultivationNextRealmResolver.TryGetNextRule(realmId, out XjRealmRule targetRule))
		{
			return new XjProgressionCandidateState(realmTier, realmId, string.Empty, flags,
				XjProgressionBlockReason.NoNextRealm, 0f, nextGongFaYear, nextCaiQiYear, 0);
		}
		if (!targetRule.IsImplemented)
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.RuleNotImplemented, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if (targetRule.RequiresFiveXianJi)
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.FiveXianJi, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if ((overlayMask & ((1 << 8) | (1 << 9))) != 0)
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.AptitudeInjury, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		bool daoZhu = false;
		try { daoZhu = actor.hasTrait("ChuShen8"); } catch { }
		if (!daoZhu && !XjBreakthroughRules.CanAttemptByAptitude(xjZz, targetRule.RealmId))
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.Aptitude, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if (zhenYuan < targetRule.RequiredZhenYuan)
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.ZhenYuan, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& XjCaiQiActorAccessor.IsLianQiByZaQi(actor))
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.ZaQiRealmLock, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if (targetRule.RequiresCaiQi && !XjCaiQiActorAccessor.HasCompletedCaiQi(actor))
		{
			return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
				XjProgressionBlockReason.CaiQi, targetRule.RequiredZhenYuan,
				nextGongFaYear, nextCaiQiYear, 0);
		}
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int gongFaGrade);
			if (gongFaGrade < 4)
			{
				return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
					XjProgressionBlockReason.GongFa, targetRule.RequiredZhenYuan,
					nextGongFaYear, nextCaiQiYear, 0);
			}
		}

		int nextBreakthroughYear = ResolveNextBreakthroughYear(actor, targetRule.RealmId, currentYear, daoZhu);
		flags |= XjProgressionCandidateFlags.Breakthrough;
		XjProgressionBlockReason reason = nextBreakthroughYear > currentYear
			? XjProgressionBlockReason.Chronology
			: XjProgressionBlockReason.None;
		return new XjProgressionCandidateState(realmTier, realmId, targetRule.RealmId, flags,
			reason, targetRule.RequiredZhenYuan, nextGongFaYear, nextCaiQiYear, nextBreakthroughYear);
	}

	private static int ResolveNextGongFaYear(
		Actor actor,
		int realmTier,
		int xjZz,
		string daoTu,
		int currentYear,
		ref XjProgressionCandidateFlags flags)
	{
		if (actor?.data == null || xjZz <= 0) return 0;
		bool hasGrade = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade);
		bool hasName = XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaName, out string name)
			&& !string.IsNullOrWhiteSpace(name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string gongFaDaoTu);
		bool needsRepair = !hasGrade || grade <= 0 || grade > XjGongFaDefinition.MaxGrade || !hasName;
		if (!needsRepair && !string.IsNullOrWhiteSpace(daoTu)
			&& (string.IsNullOrWhiteSpace(gongFaDaoTu)
				|| !string.Equals(gongFaDaoTu.Trim(), daoTu.Trim(), StringComparison.Ordinal)))
		{
			needsRepair = true;
		}
		if (needsRepair)
		{
			flags |= XjProgressionCandidateFlags.GongFa;
			return Math.Max(1, currentYear);
		}

		int maximumAllowedGrade = Math.Min(
			XjGongFaDefinition.MaxGrade,
			Math.Min(
				XjGongFaAptitudeRules.GetAptitudeGradeCap(actor, xjZz),
				XjGongFaAptitudeRules.GetRealmGradeCap(realmTier)));
		if (grade >= maximumAllowedGrade) return 0;
		int nextGrade = grade + 1;
		if (nextGrade > XjGongFaDefinition.MaxGrade) return 0;
		// 六品只由求金法绑定链处理，普通功法候选到五品即停止。
		if (nextGrade == 6) return 0;

		flags |= XjProgressionCandidateFlags.GongFa;
		if (XjGongFaAttemptSchedule.TryGetDueYear(actor, nextGrade, Math.Max(1, currentYear), out int dueYear))
		{
			return dueYear;
		}
		return Math.Max(1, currentYear);
	}

	private static int ResolveNextBreakthroughYear(Actor actor, string targetRealmId, int currentYear, bool daoZhu)
	{
		int nextYear = Math.Max(1, currentYear);
		if (!daoZhu)
		{
			int minimumAge = XjBreakthroughRules.ResolveMinimumBreakthroughAge(targetRealmId);
			int age = 0;
			try { age = (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch { }
			if (minimumAge > age)
			{
				nextYear = Math.Max(nextYear, currentYear + (minimumAge - age));
			}

			int minimumStay = XjBreakthroughRules.ResolveMinimumRealmStay(targetRealmId);
			if (minimumStay > 0
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int enteredYear)
				&& enteredYear > 0 && enteredYear <= currentYear)
			{
				nextYear = Math.Max(nextYear, enteredYear + minimumStay);
			}
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmBreakthroughLastAttemptYear, out int lastAttemptYear)
			&& lastAttemptYear == currentYear)
		{
			nextYear = Math.Max(nextYear, lastAttemptYear + 1);
		}
		return nextYear;
	}

	private static int ResolveCurrentYear(int requestedYear)
	{
		int trackedYear = Math.Max(0, XjYearTracker.CurrentYear);
		int worldYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		return Math.Max(Math.Max(0, requestedYear), Math.Max(trackedYear, worldYear));
	}

	private static bool IsProgressionKey(string key)
	{
		return key == XjActorDataKeys.XjZz
			|| key == XjActorDataKeys.XjZzOverlayMask
			|| key == XjActorDataKeys.RealmId
			|| key == XjActorDataKeys.RealmManualRemoved
			|| key == XjActorDataKeys.RealmEnteredYear
			|| key == XjActorDataKeys.RealmBreakthroughLastAttemptYear
			|| key == XjActorDataKeys.DaoTu
			|| key == XjActorDataKeys.XjGongFaName
			|| key == XjActorDataKeys.XjGongFaGrade
			|| key == XjActorDataKeys.XjGongFaDaoTu
			|| key == XjActorDataKeys.XjQiuJinFaReady
			|| key == XjActorDataKeys.XjQiuJinFaName
			|| key == XjActorDataKeys.XjXianJiCount
			|| key == XjActorDataKeys.XjXianJiIds
			|| key == XjActorDataKeys.CaiQiCompleted
			|| key == XjActorDataKeys.CaiQiConsumedForBreakthrough
			|| key == XjActorDataKeys.CaiQiResultType
			|| key == XjActorDataKeys.CaiQiResourceId
			|| key == XjActorDataKeys.CaiQiResourceCount
			|| key == XjActorDataKeys.LianQiByZaQi
			|| key == XjActorDataKeys.NextCaiQiYear
			|| key == XjActorDataKeys.QingXuanQingCanQi
			|| key == XjActorDataKeys.QingXuanChuYangJi
			|| key == XjActorDataKeys.QingXuanXuanYangZiFoundation
			|| key == XjActorDataKeys.QingXuanUnlocked
			|| key == XjActorDataKeys.QingXuanKongZhengCompleted;
	}

	private static void RemoveProgression(long actorId)
	{
		if (actorId <= 0L) return;
		ProgressionStates.Remove(actorId);
		DirtyProgressionIds.Remove(actorId);
		ZhenYuanChangedIds.Remove(actorId);
		ZiFuProgressionIds.Remove(actorId);
		JinDanProgressionIds.Remove(actorId);
	}

	private static void SetRealmEntered(long actorId, bool included)
	{
		bool changed = included ? RealmEnteredIds.Add(actorId) : RealmEnteredIds.Remove(actorId);
		if (changed)
		{
			realmEnteredSnapshotDirty = true;
		}
	}

	private static void SetZhuJiDaoTu(long actorId, string daoTu)
	{
		ZhuJiDaoTuByActorId.TryGetValue(actorId, out string previousDaoTu);
		if (string.Equals(previousDaoTu, daoTu, StringComparison.Ordinal))
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(previousDaoTu)
			&& ZhuJiIdsByDaoTu.TryGetValue(previousDaoTu, out HashSet<long> previousSet))
		{
			previousSet.Remove(actorId);
			DirtyZhuJiSnapshotDaoTu.Add(previousDaoTu);
			if (previousSet.Count == 0)
			{
				ZhuJiIdsByDaoTu.Remove(previousDaoTu);
				ZhuJiSnapshotsByDaoTu.Remove(previousDaoTu);
				DirtyZhuJiSnapshotDaoTu.Remove(previousDaoTu);
			}
		}

		if (string.IsNullOrWhiteSpace(daoTu))
		{
			ZhuJiDaoTuByActorId.Remove(actorId);
			return;
		}

		if (!ZhuJiIdsByDaoTu.TryGetValue(daoTu, out HashSet<long> nextSet))
		{
			nextSet = new HashSet<long>();
			ZhuJiIdsByDaoTu[daoTu] = nextSet;
		}
		nextSet.Add(actorId);
		ZhuJiDaoTuByActorId[actorId] = daoTu;
		DirtyZhuJiSnapshotDaoTu.Add(daoTu);
	}

	private static void SetMembership(HashSet<long> set, long actorId, bool included)
	{
		if (included) set.Add(actorId);
		else set.Remove(actorId);
	}

	private static string NormalizeDaoTu(string daoTu)
	{
		return string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
