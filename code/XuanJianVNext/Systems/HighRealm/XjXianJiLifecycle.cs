using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjZiFuProgression
{
	private static readonly Dictionary<string, ZhengWeiManifestCacheEntry> ZhengWeiManifestCache =
		new Dictionary<string, ZhengWeiManifestCacheEntry>(StringComparer.Ordinal);
	private const int AttemptIntervalYears = 5;
	private const int LatePenaltyElapsedYears = 3000;
	private const float CostUpPerTier = 0.2f;

	internal static bool EnsureZhuJiFoundationXianJi(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(daoTu)
			|| XjXianJiAccessor.BuildState(actor).Count > 0)
		{
			return false;
		}

		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found
			|| string.IsNullOrWhiteSpace(gongFa.Name)
			|| !XjXianJiCatalog.TryResolveMappedXianJi(daoTu, gongFa.Name, out string id)
			|| !XjXianJiCatalog.IsAvailableForProgression(
				daoTu, 1, Array.Empty<string>(), false, !XjLongShuSystem.IsLongShu(actor), id))
		{
			return false;
		}

		return XjXianJiAccessor.Add(actor, id, Math.Max(0, currentYear));
	}

	internal static void TickActor(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Count >= XjXianJiState.MaxCount)
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		// 筑基仙基在晋升紫府时直接成为第一门神通。若因手动补录或异常写入
		// 导致首神通缺失，按已经提升到五品的主功法补齐，不进入后四门概率判定。
		if (state.Count == 0)
		{
			TryGrantFirstShenTong(actor, snapshot, state, currentYear);
			return;
		}
		int targetCount = state.Count + 1;
		if (!TryResolveShenTongEligibilityYear(actor, targetCount, currentYear, out int minimumEligibilityYear))
		{
			return;
		}
		int stageActivationYear = XjXianJiOpportunitySchedule.EnsureStage(
			actor,
			targetCount,
			minimumEligibilityYear,
			currentYear,
			out int lastLogicalAttemptYear);

		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || snapshot.ZhenYuan < GetRequiredZhenYuan(state.Count))
		{
			return;
		}

		int attemptIntervalYears = XjAlchemyPillEffectSystem.ResolveShenTongAttemptIntervalYears(
			actor, currentYear, AttemptIntervalYears);
		long intervalDue = (long)Math.Max(1, stageActivationYear) + Math.Max(1, attemptIntervalYears);
		int eligibilityYear = Math.Max(
			minimumEligibilityYear,
			intervalDue > int.MaxValue ? int.MaxValue : (int)intervalDue);
		if (!XjProgressionOpportunityClock.TryResolveIntervalDueYear(
				lastLogicalAttemptYear, attemptIntervalYears, eligibilityYear, currentYear, out int opportunityYear)
			|| !XjProgressionOpportunityClock.HasExecutionSlot(
				actor, XjActorDataKeys.XjXianJiLastExecutionYear, currentYear))
		{
			return;
		}

		// 主功法未到五品时，本周期不进行神通抽取，而是进行一次五品参悟。
		// 无论成功与否都属于真实判定并消耗本次三年/五年周期。
		if (gongFa.Grade < 5)
		{
			MarkOpportunityExecution(actor, currentYear, opportunityYear);
			XjGongFaProgression.TryPromoteMainToGrade5ForShenTong(actor, snapshot, opportunityYear);
			MarkLogicalAttempt(actor, opportunityYear);
			return;
		}

		// 人丹计划也必须通过正常的五年/三年门槛。目标尚未成熟时只检查
		// 计划状态，不消耗本次判定周期；真正获得神通时 Add 会写入本年。
		if (XjRenDan.TryAdvancePreparedPlan(actor, snapshot, state, currentYear))
		{
			XjXianJiState postPlanState = XjXianJiAccessor.BuildState(actor);
			if (postPlanState.Count > state.Count)
			{
				MarkOpportunityExecution(actor, currentYear, opportunityYear);
			}
			return;
		}

		if (actor.hasTrait("ChuShen8"))
		{
			MarkOpportunityExecution(actor, currentYear, opportunityYear);
			TryGrantDaoZhuShenTong(actor, snapshot, state, currentYear);
			return;
		}
		if (XjRenDan.TryAcquireDuringXianJi(actor, snapshot, state, currentYear))
		{
			MarkOpportunityExecution(actor, currentYear, opportunityYear);
			return;
		}

		// 仓库传法优先于自行领悟：家族先于宗门。只有成功写入真实神通
		// 与映射五品功法时才由 Add 更新判定年份。
		if (TryGrantFamilyMapped(actor, snapshot, state, currentYear)
			|| TryGrantZongMenMapped(actor, snapshot, state, currentYear))
		{
			MarkOpportunityExecution(actor, currentYear, opportunityYear);
			return;
		}

		MarkOpportunityExecution(actor, currentYear, opportunityYear);
		TryComprehendNewShenTong(actor, snapshot, state, currentYear, opportunityYear);
	}

	private static bool TryResolveShenTongEligibilityYear(Actor actor, int ordinal, int currentYear, out int eligibilityYear)
	{
		eligibilityYear = 0;
		if (ordinal <= 1)
		{
			eligibilityYear = Math.Max(1, currentYear);
			return currentYear > 0;
		}

		int requiredZiFuYears = ordinal switch { 2 => 20, 3 => 50, 4 => 90, 5 => 140, _ => int.MaxValue };
		int minimumAge = ordinal switch { 2 => 120, 3 => 150, 4 => 190, 5 => 240, _ => int.MaxValue };
		if (requiredZiFuYears == int.MaxValue || minimumAge == int.MaxValue || currentYear <= 0) return false;

		int age;
		try { age = actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch { age = 0; }
		int ziFuYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(actor);
		if (ziFuYear <= 0) return false;

		long birthYear = Math.Max(0L, (long)currentYear - age);
		long ageEligibleYear = birthYear + minimumAge;
		long tenureEligibleYear = (long)ziFuYear + requiredZiFuYears;
		long resolved = Math.Max(ageEligibleYear, tenureEligibleYear);
		if (resolved > int.MaxValue) return false;

		eligibilityYear = Math.Max(1, (int)resolved);
		return currentYear >= eligibilityYear;
	}

	private static bool TryGrantFirstShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || gongFa.Grade < 5 || string.IsNullOrWhiteSpace(gongFa.Name))
		{
			return false;
		}

		if (actor.hasTrait("ChuShen8")
			&& XjXianJiCatalog.TryPickUpperForProgression(
				snapshot.DaoTu, 1, GetActorId(actor), state.Ids, out string upperId))
		{
			return TryAddAndResolveGrant(actor, snapshot, state, upperId, currentYear, gongFa.Name, "仙基升格");
		}

		return XjXianJiCatalog.TryResolveMappedXianJi(snapshot.DaoTu, gongFa.Name, out string mappedId)
			&& XjXianJiCatalog.IsAvailableForProgression(
				snapshot.DaoTu, 1, state.Ids, false, !XjLongShuSystem.IsLongShu(actor), mappedId)
			&& TryAddAndResolveGrant(actor, snapshot, state, mappedId, currentYear, gongFa.Name, "仙基升格");
	}

	private static bool TryGrantDaoZhuShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		return XjXianJiCatalog.TryPickDaoZhuForProgression(
				snapshot.DaoTu, ordinal, GetActorId(actor), state.Ids,
				IsZhengWeiManifested(snapshot.DaoTu), out string id)
			&& TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, string.Empty, "道主悟法");
	}

	private static bool TryGrantFamilyMapped(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		return XjFamilyHighGradeTransmission.TryResolveFamilyMappedGongFa(
				actor, snapshot.DaoTu, ordinal, state.Ids, IsZhengWeiManifested(snapshot.DaoTu),
				out string id, out string gongFaName)
			&& TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, gongFaName, "家族传法");
	}

	private static bool TryGrantZongMenMapped(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		return XjZongMenGongFaBorrow.TryResolveMappedGongFa(
				actor, snapshot.DaoTu, ordinal, state.Ids, IsZhengWeiManifested(snapshot.DaoTu),
				out string id, out string gongFaName)
			&& TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, gongFaName, "宗门传法");
	}

	private static bool TryComprehendNewShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear,
		int opportunityYear)
	{
		int ordinal = state.Count + 1;
		if (!XjXianJiCatalog.TryPickForProgression(
				snapshot.DaoTu, ordinal, GetActorId(actor) + opportunityYear, state.Ids,
				IsZhengWeiManifested(snapshot.DaoTu), !XjLongShuSystem.IsLongShu(actor), out string id))
		{
			return false;
		}

		float maximumChance = ResolveMaximumSuccessChance(state.Count + 1);
		float chance = CalculateSuccessChance(snapshot, state);
		if (XjRuntimeCadence.HasElapsedSinceLoad(currentYear, LatePenaltyElapsedYears))
		{
			chance *= 0.75f;
		}

		// 丹药、符箓、听讲属于同一类辅助收益，合计最多增加25个百分点；
		// 最终概率仍不得越过该神通序位自身上限，避免叠加到80%—90%。
		float aidBonus = Math.Min(0.25f,
			XjAlchemyPillEffectSystem.TryConsumeShenTongChanceBonus(actor, currentYear)
			+ XjTalismanCombatService.TryConsumeShenTongAid(actor)
			+ TryConsumeLectureShenTongAid(actor, currentYear));
		chance = Math.Min(maximumChance, Math.Max(0f, chance + aidBonus));

		MarkLogicalAttempt(actor, opportunityYear);
		ConsumeAttemptCost(actor, state.Count);
		long seed = GetActorId(actor) + opportunityYear + state.Count * 29L;
		if (XjDeterministicHash.PositiveIndex(seed, "xianji_comprehension", 10000)
			>= (int)Math.Floor(chance * 10000f))
		{
			return false;
		}

		// Add 内部同时创建映射该神通的五品功法；神通与功法不会再出现
		// “必须先有对方才能生成”的循环依赖。
		return TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, string.Empty, "自行悟法");
	}

	private static bool TryAddAndResolveGrant(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		string id,
		int currentYear,
		string gongFaName = "",
		string gongFaSource = "神通领悟")
	{
		if (!XjXianJiAccessor.Add(actor, id, currentYear, gongFaName, gongFaSource))
		{
			return false;
		}

		XjThreeBookWriter.RecordShenTongComprehended(actor, id, currentYear, gongFaSource);
		XjFaBaoAcquisition.TryGrantZiFuLingBaoOnXianJi(actor, snapshot, state.Count + 1, currentYear);
		if (XjXianJiCatalog.GetPoolKind(snapshot.DaoTu, id) == XjXianJiPoolKind.Other
			&& XjXianJiCatalog.TryResolveOwningDaoTu(id, out string owningDaoTu))
		{
			XjChronicleWriter.RecordOtherShenTongMisled(actor, currentYear, owningDaoTu, id);
		}
		return true;
	}

	private static float CalculateSuccessChance(
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state)
	{
		float maximumChance = ResolveMaximumSuccessChance(state.Count + 1);
		return HuiGuangCurveChance(snapshot.HuiGuang, 45f, 100f, maximumChance);
	}

	private static float ResolveMaximumSuccessChance(int ordinal)
	{
		return ordinal switch
		{
			2 => 0.18f,
			3 => 0.10f,
			4 => 0.055f,
			5 => 0.03f,
			_ => 0f
		};
	}

	private static float TryConsumeLectureShenTongAid(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiLectureAidYear, out int aidYear)
			|| aidYear <= 0
			|| currentYear < aidYear
			|| currentYear > aidYear + 10
			|| !XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus, out float bonus)
			|| bonus <= 0f)
		{
			return 0f;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLectureAidYear, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus, 0f);
		return Math.Clamp(bonus, 0f, 0.18f);
	}

	private static void MarkOpportunityExecution(Actor actor, int executionYear, int opportunityYear)
	{
		XjProgressionOpportunityClock.MarkExecuted(
			actor, XjActorDataKeys.XjXianJiLastExecutionYear, executionYear);
		XjStageZeroObservation.RecordOpportunityDebtConsumed("XianJi", opportunityYear, executionYear);
	}

	private static void MarkLogicalAttempt(Actor actor, int opportunityYear)
	{
		XjXianJiOpportunitySchedule.MarkLogicalAttempt(actor, opportunityYear);
	}

	private static void ConsumeAttemptCost(Actor actor, int currentCount)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float currentZhenYuan);
		float cost = 300f * (1f + CostUpPerTier * Math.Max(0, currentCount - 1));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, Math.Max(0f, currentZhenYuan - cost));
	}

	private static bool IsZhengWeiManifested(string daoTu)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		int registryRevision = XjGuoWeiRegistry.Revision;
		if (ZhengWeiManifestCache.TryGetValue(normalizedDaoTu, out ZhengWeiManifestCacheEntry cached)
			&& cached.RegistryRevision == registryRevision)
		{
			return cached.Manifested;
		}

		bool manifested = XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(normalizedDaoTu);
		ZhengWeiManifestCache[normalizedDaoTu] = new ZhengWeiManifestCacheEntry(registryRevision, manifested);
		return manifested;
	}

	private readonly struct ZhengWeiManifestCacheEntry
	{
		internal readonly int RegistryRevision;
		internal readonly bool Manifested;

		internal ZhengWeiManifestCacheEntry(int registryRevision, bool manifested)
		{
			RegistryRevision = registryRevision;
			Manifested = manifested;
		}
	}

	private static float GetRequiredZhenYuan(int currentCount)
	{
		return currentCount switch
		{
			1 => 40000f,
			2 => 55000f,
			3 => 70000f,
			4 => 86000f,
			_ => 94000f
		};
	}

	private static float HuiGuangCurveChance(float huiGuang, float minimum, float peak, float maximumChance)
	{
		if (huiGuang < minimum || peak <= minimum || maximumChance <= 0f)
		{
			return 0f;
		}

		float t = Math.Min(1f, Math.Max(0f, (huiGuang - minimum) / (peak - minimum)));
		float smooth = t * t * (3f - 2f * t);
		return maximumChance * smooth;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}

