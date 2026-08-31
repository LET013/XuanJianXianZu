using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.HighRealm;

using XuanJianVNext.Systems.Sect;
namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修入门与度化的年度入口。今释初次入释读取今释父母、同城今释法师与世界是否
/// 已有释修；古释不以师承扩张，只保留遗经自悟与手动补录。“度化”不把目标转成释修。0.9.11.1起今释法师及以上每个世界年最多
/// 真实度化三名非修士，主动击杀与后台事务共享同一硬上限；显示与纪事按一具
/// 肉身记十人。文明人口达到5000才开启度化，开启后低于3000关闭，3000~4999
/// 保持上一状态。法师及以上今释都可进入有界后台度化车道，七相只调整收益倾向；
/// 年度角色管线只做O(1)排程，目标选择不扫描全世界人口。
/// </summary>
internal static class XjShiEntrySystem
{
	private const int CandidateBudget = 12;
	private const int HighRealmConversionCandidateBudget = 24;
	internal const int ModernShiPopulationLimit = 300;
	// 仅供已经成立的今释转世/真灵归返使用。普通新入释永远只看基础在世容量。
	// 该缓冲是短时回归保险，不作为新的常驻人口池；只要在世人数高于基础线，
	// 所有普通新增今释入口仍保持关闭，直到人口自然回落。
	internal const int ModernShiReincarnationElasticReserve = 30;
	internal const int AncientShiPopulationLimit = 150;
	private static int _presenceYear;
	private static bool _hasLivingShi;
	private static bool _presenceDirty = true;

	internal static bool TryEnterInitialIndependent(Actor actor, int currentYear)
	{
		// 释修缘法与仙修资质彻底分离：这里不接收、更不读取 XjZz。
		if (actor?.data == null || !actor.isAlive()
			|| XjCultivationPathRules.TryGetPath(actor, out _)) return false;

		int year = Math.Max(1, currentYear);
		if (HasTrait(actor, XjShiTraitIds.Ancient))
		{
			return XjShiState.TryApplyManualTraditionRecord(actor, XjShiTraditionIds.Ancient, year);
		}
		if (HasTrait(actor, XjShiTraitIds.Modern))
		{
			return XjShiState.TryApplyManualTraditionRecord(actor, XjShiTraditionIds.Modern, year);
		}

		// 首批十八枚古释种子前五十年保持隐世；五十年后随角色首次定路自然陆续显现，
		// 不再设置逐个年份硬间隔。首批窗口未完成以前，普通1%~3%遗经自悟暂不另开旁路。
		XjShiOpeningPrologueSystem.TryBeginAncientSeedBootstrap(year, false);
		bool ancientSeedWindowOpen = XjShiOpeningPrologueSystem.IsAncientSeedBootstrapOpen(year);
		if (ancientSeedWindowOpen
			&& XjShiOpeningPrologueSystem.CanManifestAncientSeed(year)
			&& IsAncientSeedBootstrapCandidate(actor)
			&& TryEnterAncientSeedBootstrapCandidate(actor, year))
		{
			return true;
		}

		// 十八首批名额全部落定后，才恢复正常世代的低频遗经自悟。
		// 这是一次性、按角色确定的自悟判定，不靠师承扩张，也不会年度重复抽取。
		if (!ancientSeedWindowOpen
			&& PassAncientSelfAwakeningCheck(actor)
			&& XjShiState.TryEnter(actor, XjShiTraditionIds.Ancient, year,
				XjShiSourceIds.Scripture, 0L, XjShiLineageIds.NorthWorldHonored, string.Empty))
		{
			NotifyShiEntered();
			XjCultivatorCache.CheckAndUpdate(actor);
			XjScheduler.EnsureRuntimeIndexesForActor(actor);
			return true;
		}

		// 血缘不等于自动入释。释修父母只作为最容易接触到的传法者，
		// 仍需通过一次确定性求缘判定，避免法师后裔整族无条件改修。
		if (TryResolveDirectShiParent(actor, out Actor parent)
			&& PassInitialTeachingCheck(actor, parent, year, directParent: true)
			&& XjShiState.TryEnterThroughTeacher(actor, parent, year, XjShiSourceIds.Master))
		{
			NotifyShiEntered();
			return true;
		}

		if (TryResolveCityTeacher(actor, year, out Actor teacher)
			&& PassInitialTeachingCheck(actor, teacher, year, directParent: false)
			&& XjShiState.TryEnterThroughTeacher(actor, teacher, year, XjShiSourceIds.Master))
		{
			NotifyShiEntered();
			return true;
		}

		return false;
	}

	internal static bool IsAncientSeedBootstrapCandidate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjCultivationPathRules.TryGetPath(actor, out _)) return false;
		return ReadOrdinaryMingShu(actor) >= XjShiCatalog.AncientSeedMingShuThreshold;
	}

	internal static bool TryEnterAncientSeedBootstrapCandidate(Actor actor, int currentYear)
	{
		int year = Math.Max(1, currentYear);
		if (!XjShiOpeningPrologueSystem.IsAncientSeedBootstrapOpen(year)
			|| !XjShiOpeningPrologueSystem.CanManifestAncientSeed(year)
			|| !IsAncientSeedBootstrapCandidate(actor)) return false;

		bool entered = XjShiState.TryEnter(actor, XjShiTraditionIds.Ancient, year,
			XjShiSourceIds.Scripture, 0L, XjShiLineageIds.NorthWorldHonored, string.Empty);
		if (!entered) return false;
		XjShiOpeningPrologueSystem.RecordAncientSeedSuccess(year);
		NotifyShiEntered();
		XjCultivatorCache.CheckAndUpdate(actor);
		XjScheduler.EnsureRuntimeIndexesForActor(actor);
		return true;
	}

	internal static bool CanAddLivingModernShi(Actor candidate)
	{
		return CanAddLivingTradition(candidate, XjShiTraditionIds.Modern);
	}

	internal static bool CanAddLivingAncientShi(Actor candidate)
	{
		return CanAddLivingTradition(candidate, XjShiTraditionIds.Ancient);
	}

	internal static bool CanAddLivingTradition(Actor candidate, string tradition)
	{
		if (candidate?.data == null || !candidate.isAlive() || !XjShiCatalog.IsKnownTradition(tradition)) return false;
		if (XjShiState.TryBuildSnapshot(candidate, out XjShiSnapshot existing)
			&& string.Equals(existing.Tradition, tradition, StringComparison.Ordinal))
		{
			// 已在该道统中的存活角色不重新消费名额。
			return true;
		}
		return HasLivingCapacity(tradition);
	}

	internal static bool HasLivingCapacity(string tradition)
	{
		if (!XjShiCatalog.IsKnownTradition(tradition)) return false;
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			return CountLivingTradition(tradition, AncientShiPopulationLimit) < AncientShiPopulationLimit;
		}

		// 今释待归返真灵会把旧身死亡后释放出的在世名额预留出来，普通新入释
		// 不能抢占这些位置。预留数随Pending记录实时增减，因此属于动态弹性，
		// 不增加常驻人口上限，也不会在没有转世时压低正常承载。
		int reservedForReturns = Math.Min(
			ModernShiPopulationLimit,
			Math.Max(0, XjReincarnation.PendingModernShiReturnReservationCount));
		int ordinaryLimit = Math.Max(0, ModernShiPopulationLimit - reservedForReturns);
		return CountLivingTradition(tradition, ModernShiPopulationLimit) < ordinaryLimit;
	}

	internal static bool HasReincarnationReturnCapacity(string tradition)
	{
		if (!XjShiCatalog.IsKnownTradition(tradition)) return false;
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			return HasLivingCapacity(tradition);
		}

		// 今释的普通新增仍受基础在世容量限制；只有已经成立的转世/真灵归返
		// 可以使用这段专属弹性，避免旧身死亡后名额被其他新入释者占满，
		// 导致同一真灵长期卡在Pending。弹性本身不开放给任何新入释入口。
		int returnLimit = ModernShiPopulationLimit + ModernShiReincarnationElasticReserve;
		return CountLivingTradition(tradition, returnLimit) < returnLimit;
	}

	private static int CountLivingTradition(string tradition, int stopAt)
	{
		int living = 0;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
				|| !string.Equals(snapshot.Tradition, tradition, StringComparison.Ordinal)) continue;
			living++;
			if (stopAt > 0 && living >= stopAt) break;
		}
		// 只统计“现在还活着”的肉身；死亡或转出本道统后，下一次资格检查自然释放名额。
		return living;
	}

	internal static bool HasAnyLivingShi(int currentYear) => HasLivingShi(Math.Max(1, currentYear));

	internal static bool HasAnyLivingAncient(int currentYear)
	{
		_ = currentYear;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) continue;
			if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static void TryAnnualDuhua(Actor teacher, int annualYear)
	{
		if (teacher?.data == null || !teacher.isAlive() || annualYear <= 0
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)) return;

		XjShiSentientConsumptionSystem.EnsureDuhuaRule(teacher);
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			// 古释不执行杀生度化：每名古释都进入五年一次的清静点化通道。
			XjShiAncientDuhuaSystem.TryAnnualBlessing(teacher, annualYear);
			return;
		}
		if (string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster))
		{
			// 已修行角色的投释只发生在真实同城接触中。法师/法相每年有限观察一名
			// 已经对本路前景失去把握的候选，再由统一修法重择策略作低频判定。
			TryAnnualHighRealmConversion(teacher, annualYear);
			XjShiDuhuaRuntimeLane.Schedule(teacher, annualYear);
		}
	}

	private static void TryAnnualHighRealmConversion(Actor teacher, int annualYear)
	{
		if (teacher?.data == null || teacher.city == null
			|| !XjSectCultivatorCityIndex.TryGetActorIds(teacher.city, out List<long> ids)
			|| ids == null || ids.Count == 0) return;

		long teacherId = ((BaseSystemData)teacher.data).id;
		int start = XjDeterministicHash.PositiveIndex(teacherId + annualYear,
			"shi_high_realm_conversion_candidate_v1", ids.Count);
		Actor selected = null;
		int selectedTier = -1;
		float selectedMingShu = -1f;
		long selectedId = long.MaxValue;
		int examined = 0;
		for (int offset = 0; offset < ids.Count && examined < HighRealmConversionCandidateBudget; offset++)
		{
			examined++;
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == teacherId
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor candidate)
				|| candidate?.data == null || !candidate.isAlive() || candidate.city != teacher.city
				|| XjCultivationPathRules.IsShi(candidate)
				|| !XjShiConversionSystem.CanBuildTeacherConversionPlan(candidate, teacher, annualYear, out int tier)) continue;

			float mingShu = ReadOrdinaryMingShu(candidate);
			if (selected == null || tier > selectedTier
				|| tier == selectedTier && mingShu > selectedMingShu
				|| tier == selectedTier && Math.Abs(mingShu - selectedMingShu) < 0.001f && candidateId < selectedId)
			{
				selected = candidate;
				selectedTier = tier;
				selectedMingShu = mingShu;
				selectedId = candidateId;
			}
		}

		if (selected != null)
		{
			XjShiState.TryEnterThroughTeacher(selected, teacher, annualYear, XjShiSourceIds.Master);
		}
	}

	internal static void NotifyShiEntered()
	{
		_hasLivingShi = true;
		_presenceDirty = false;
		_presenceYear = Math.Max(1, XjYearTracker.CurrentYear);
	}

	internal static void InvalidatePresence()
	{
		_presenceDirty = true;
		XjShiDuhuaRuntimeLane.InvalidateCandidateSnapshot();
	}

	internal static void ClearRuntime()
	{
		_presenceYear = 0;
		_hasLivingShi = false;
		_presenceDirty = true;
	}

	private static bool TryResolveDirectShiParent(Actor actor, out Actor teacher)
	{
		teacher = null;
		if (actor?.data == null) return false;
		if (IsQualifiedTeacher(actor.data.parent_id_1, out teacher)) return true;
		return IsQualifiedTeacher(actor.data.parent_id_2, out teacher);
	}

	private static bool IsQualifiedTeacher(long actorId, out Actor teacher)
	{
		teacher = null;
		return actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out teacher)
			&& IsQualifiedTeacher(teacher);
	}

	private static bool IsQualifiedTeacher(Actor actor)
	{
		return actor?.data != null && actor.isAlive()
			&& XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			&& string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster);
	}

	private static bool TryResolveCityTeacher(Actor actor, int currentYear, out Actor selected)
	{
		selected = null;
		if (actor?.city == null || !XjSectCultivatorCityIndex.TryGetActorIds(actor.city, out List<long> ids)
			|| ids == null || ids.Count == 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		int start = XjDeterministicHash.PositiveIndex(actorId + currentYear, "shi_initial_teacher", ids.Count);
		int bestRank = -1;
		int examinedCount = 0;
		int budget = CandidateBudget;
		for (int offset = 0; offset < ids.Count && examinedCount < budget; offset++)
		{
			examinedCount++;
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == actorId
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor candidate)
				|| candidate?.city != actor.city || !IsQualifiedTeacher(candidate)) continue;
			XjShiState.TryBuildSnapshot(candidate, out XjShiSnapshot candidateSnapshot);
			int rank = XjShiCatalog.GetRank(candidateSnapshot.Realm);
			if (selected == null || rank > bestRank
				|| rank == bestRank && candidateId < ((BaseSystemData)selected.data).id)
			{
				selected = candidate;
				bestRank = rank;
			}
		}
		return selected != null;
	}

	private static bool PassInitialTeachingCheck(Actor actor, Actor teacher, int currentYear, bool directParent)
	{
		float mingShu = ReadOrdinaryMingShu(actor);
		if (mingShu < XjShiCatalog.NaturalEntryMingShuThreshold) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		long teacherId = ((BaseSystemData)teacher.data).id;
		int excess = Math.Max(0, (int)Math.Floor(mingShu - XjShiCatalog.NaturalEntryMingShuThreshold));
		// 0.9.9.3：在不改变命数门槛、师承结构与终身判定方式的前提下，
		// 将今释自然进入率整体轻调低约15%。同城约4.25%起，直系父母约+0.85%，
		// 最终硬上限约6.8%，避免入口过快挤占紫金/服气人口。
		int basisPoints = 425 + excess * 17 + (directParent ? 85 : 0);
		return XjDeterministicHash.PositiveIndex(actorId + teacherId + currentYear,
			"shi_initial_teaching_mingshu_v2", 10000) < Math.Min(680, basisPoints);
	}

	private static bool PassAncientSelfAwakeningCheck(Actor actor)
	{
		if (actor?.data == null) return false;
		float mingShu = ReadOrdinaryMingShu(actor);
		if (mingShu < XjShiCatalog.AncientSeedMingShuThreshold) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		// 古释仍保持“高命数者中的极少数自悟”：85命数为1%，随命数平滑升高，
		// 100命数封顶3%。判定盐只使用角色自身，确保每人一生只有一次自悟结果，
		// 不会因年度重试把低概率最终滚成必然。
		int excess = Math.Max(0, (int)Math.Floor(mingShu - XjShiCatalog.AncientSeedMingShuThreshold));
		int basisPoints = Math.Min(300, 100 + excess * 14);
		return XjDeterministicHash.PositiveIndex(actorId, "shi_ancient_self_awaken_v3", 10000) < basisPoints;
	}


	internal static bool IsDuhuaTarget(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		try
		{
			if (actor.asset == null || !actor.asset.civ) return false;
		}
		catch { return false; }
		return !XjCultivationPathRules.TryGetPath(actor, out _);
	}

	private static float ReadOrdinaryMingShu(Actor actor)
	{
		if (actor?.data == null) return 0f;
		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital)
			|| congenital <= 0f)
		{
			XjCultivationSeed.EnsureSeedState(actor);
		}
		XjMingShuState.Normalize(actor);
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float value)
			? Math.Max(0f, value) : 0f;
	}

	private static bool HasLivingShi(int currentYear)
	{
		int year = Math.Max(1, currentYear);
		if (!_presenceDirty && _presenceYear == year) return _hasLivingShi;
		_presenceYear = year;
		_presenceDirty = false;
		_hasLivingShi = false;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				&& actor?.data != null && actor.isAlive() && XjCultivationPathRules.IsShi(actor))
			{
				_hasLivingShi = true;
				break;
			}
		}
		return _hasLivingShi;
	}

	private static bool HasTrait(Actor actor, string traitId)
	{
		try { return actor?.data != null && actor.hasTrait(traitId); }
		catch { return false; }
	}
}
