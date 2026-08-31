using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.XianGuo;

using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;

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
		if (!gongFa.Found || string.IsNullOrWhiteSpace(gongFa.Name))
		{
			return false;
		}

		string id;
		if (XjQingXuanKongZhengSystem.IsQingXuanDaoTu(daoTu)
			&& XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			// 青宣的第一基不能由功法名称哈希到任意上位池；其核心必须固定为玄羊子，
			// 后四基才由紫府阶段逐步形成并等待玄羊子抬举。
			id = XjQingXuanKongZhengSystem.FoundationXianJi;
		}
		else if (!XjXianJiCatalog.TryResolveMappedXianJi(daoTu, gongFa.Name, out id))
		{
			// 虹霞是后接入的独立道途；如果旧功法实体没有可解析的映射，
			// 仍只从虹霞自己的上位池确定第一仙基，保持与普通紫金道相同的
			// “筑基成基 -> 紫府升格”时序，而不是等紫府后凭空补五门预览。
			if (!string.Equals((daoTu ?? string.Empty).Trim(), "虹霞", StringComparison.Ordinal)
				|| !XjXianJiCatalog.TryPickUpperForProgression(daoTu, 1, GetActorId(actor), Array.Empty<string>(), out id))
			{
				return false;
			}
		}

		if (!XjXianJiCatalog.IsAvailableForProgression(
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

		// 0.9.9.2 上修扶金：只替换一次本来就到期的神通机会，不凭空追加年度神通。
		if (XjUpperCultivatorGoldSupportSystem.TryResolveGuidedShenTong(actor, snapshot, state, out string patronGuidedId)
			&& TryAddAndResolveGrant(actor, snapshot, state, patronGuidedId, currentYear, string.Empty, "上修扶金·补基授业"))
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
		try { age = actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge())); } catch (System.Exception xjCaught178_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjXianJiLifecycle.cs:178", xjCaught178_1);
			 age = 0; }
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

		if (XjQingXuanKongZhengSystem.IsQingXuanDaoTu(snapshot.DaoTu)
			&& XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return TryAddAndResolveGrant(
				actor, snapshot, state, XjQingXuanKongZhengSystem.FoundationXianJi,
				currentYear, gongFa.Name, "玄羊子成基");
		}

		if (actor.hasTrait("ChuShen8")
			&& XjXianJiCatalog.TryPickUpperForProgression(
				snapshot.DaoTu, 1, GetActorId(actor), state.Ids, out string upperId))
		{
			return TryAddAndResolveGrant(actor, snapshot, state, upperId, currentYear, gongFa.Name, "仙基升格");
		}

		if (XjXianJiCatalog.TryResolveMappedXianJi(snapshot.DaoTu, gongFa.Name, out string mappedId)
			&& XjXianJiCatalog.IsAvailableForProgression(
				snapshot.DaoTu, 1, state.Ids, false, !XjLongShuSystem.IsLongShu(actor), mappedId)
			&& TryAddAndResolveGrant(actor, snapshot, state, mappedId, currentYear, gongFa.Name, "仙基升格"))
		{
			return true;
		}

		// 虹霞是后接入的独立道途，旧功法映射表可能没有它的五品功法名。
		// 这里只在“第一神通的常规映射确实失败”时退回本道上位池，不改其它道途。
		if (string.Equals((snapshot.DaoTu ?? string.Empty).Trim(), "虹霞", StringComparison.Ordinal)
			&& XjXianJiCatalog.TryPickUpperForProgression(
				snapshot.DaoTu, 1, GetActorId(actor), state.Ids, out string hongXiaUpperId))
		{
			return TryAddAndResolveGrant(actor, snapshot, state, hongXiaUpperId, currentYear, gongFa.Name, "仙基升格");
		}
		return false;
	}

	private static bool TryGrantDaoZhuShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		long seed = GetActorId(actor);
		string id;
		bool picked;
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			picked = TryPickImperialNonIntercalaryShenTong(snapshot.DaoTu, state, seed, out id);
		}
		else
		{
			picked = ShouldAvoidSameFamilyZhengWeiRoute(actor, snapshot.DaoTu, state)
				? TryPickNonZhengWeiBranchShenTong(snapshot.DaoTu, state, seed, allowOtherPool: false, out id)
				: XjXianJiCatalog.TryPickDaoZhuForProgression(
					snapshot.DaoTu, ordinal, seed, state.Ids,
					IsZhengWeiManifested(snapshot.DaoTu), out id);
		}
		return picked
			&& TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, string.Empty, "道主悟法");
	}

	private static bool TryGrantFamilyMapped(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		if (!XjFamilyHighGradeTransmission.TryResolveFamilyMappedGongFa(
			actor, snapshot.DaoTu, ordinal, state.Ids, IsZhengWeiManifested(snapshot.DaoTu),
			out string id, out string gongFaName))
		{
			return false;
		}
		XjXianJiPoolKind poolKind = XjXianJiCatalog.GetPoolKind(snapshot.DaoTu, id);
		if (XjXianGuoSystem.IsDiMingYang(actor) && poolKind != XjXianJiPoolKind.Native && poolKind != XjXianJiPoolKind.Lower) return false;
		if (ShouldAvoidSameFamilyZhengWeiRoute(actor, snapshot.DaoTu, state)
			&& poolKind == XjXianJiPoolKind.Native)
		{
			return false;
		}
		return TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, gongFaName, "家族传法");
	}

	private static bool TryGrantZongMenMapped(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		int ordinal = state.Count + 1;
		if (!XjSectGongFaBorrow.TryResolveMappedGongFa(
			actor, snapshot.DaoTu, ordinal, state.Ids, IsZhengWeiManifested(snapshot.DaoTu),
			out string id, out string gongFaName))
		{
			return false;
		}
		XjXianJiPoolKind poolKind = XjXianJiCatalog.GetPoolKind(snapshot.DaoTu, id);
		if (XjXianGuoSystem.IsDiMingYang(actor) && poolKind != XjXianJiPoolKind.Native && poolKind != XjXianJiPoolKind.Lower) return false;
		if (ShouldAvoidSameFamilyZhengWeiRoute(actor, snapshot.DaoTu, state)
			&& poolKind == XjXianJiPoolKind.Native)
		{
			return false;
		}
		return TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, gongFaName, "宗门传法");
	}

	private static bool TryComprehendNewShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear,
		int opportunityYear)
	{
		int ordinal = state.Count + 1;
		long pickSeed = GetActorId(actor) + opportunityYear;
		string id;
		bool picked;
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			picked = TryPickImperialNonIntercalaryShenTong(snapshot.DaoTu, state, pickSeed, out id);
		}
		else if (ShouldAvoidSameFamilyZhengWeiRoute(actor, snapshot.DaoTu, state))
		{
			picked = TryPickNonZhengWeiBranchShenTong(
				snapshot.DaoTu, state, pickSeed, !XjLongShuSystem.IsLongShu(actor), out id);
		}
		else
		{
			picked = XjXianJiCatalog.TryPickForProgression(
				snapshot.DaoTu, ordinal, pickSeed, state.Ids,
				IsZhengWeiManifested(snapshot.DaoTu), !XjLongShuSystem.IsLongShu(actor), out id);
		}

		if (!picked)
		{
			return false;
		}

		float maximumChance = ResolveMaximumSuccessChance(state.Count + 1);
		// 龙属不加入家族/宗门，也不能借仓库传法；第五神通只能靠自身。
		// 对这一处孤立瓶颈给两倍上限（2%→4%），仍保留年龄、紫府年限、
		// 五年一判与道慧曲线，不改前四门，也不让龙属稳定批量成丹。
		if (state.Count + 1 == 5 && XjLongShuSystem.IsLongShu(actor))
		{
			maximumChance = Math.Max(maximumChance, 0.04f);
		}
		float chance = HuiGuangCurveChance(snapshot.HuiGuang, 45f, 100f, maximumChance);
		if (XjRuntimeCadence.HasElapsedSinceLoad(currentYear, LatePenaltyElapsedYears))
		{
			chance *= 0.75f;
		}

		// 丹药、符箓、听讲属于同一类辅助收益，合计最多增加25个百分点；
		// 最终概率仍不得越过该神通序位自身上限，避免叠加到80%—90%。
		float aidBonus = Math.Min(0.25f,
			XjAlchemyPillEffectSystem.TryConsumeShenTongChanceBonus(actor, currentYear)
			+ XjTalismanCombatService.TryConsumeShenTongAid(actor)
			+ TryConsumeLectureShenTongAid(actor, currentYear)
			+ XjEventDongTianBonusService.ResolveShenTongBonus(actor, currentYear)
			+ XjSectMentorshipTeachingService.ResolveShenTongBonus(actor, currentYear)
			+ XjDaoTaiGongFaService.ResolveShenTongBonus(actor));
		chance = Math.Min(maximumChance, Math.Max(0f, chance + aidBonus));

		MarkLogicalAttempt(actor, opportunityYear);
		ConsumeAttemptCost(actor, state.Count);
		long successSeed = GetActorId(actor) + opportunityYear + state.Count * 29L;
		if (XjDeterministicHash.PositiveIndex(successSeed, "xianji_comprehension", 10000)
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

		bool qingXuanUnraisedFoundation = XjQingXuanKongZhengSystem.IsQingXuanDaoTu(snapshot.DaoTu)
			&& XjQingXuanKongZhengSystem.IsQingXuanFoundationId(id)
			&& !string.Equals(id, XjQingXuanKongZhengSystem.FoundationXianJi, StringComparison.Ordinal);
		if (qingXuanUnraisedFoundation)
		{
			// 青宣的后四项此时只是仙基；正式神通纪事与灵宝奖励在玄羊子抬举完成时产生。
			XjThreeBookWriter.RecordQingXuanFoundationFormed(actor, id, currentYear, gongFaSource);
		}
		else
		{
			XjThreeBookWriter.RecordShenTongComprehended(actor, id, currentYear, gongFaSource);
			XjBroadcastSystem.AnnounceShenTongComprehended(actor, id, gongFaSource);
			XjFaBaoAcquisition.TryGrantZiFuLingBaoOnXianJi(actor, snapshot, state.Count + 1, currentYear);
		}
		return true;
	}


	internal static bool TryGrantEventDongTianShenTong(Actor actor, int currentYear, out string shenTongId)
	{
		shenTongId = string.Empty;
		if (actor?.data == null || currentYear <= 0
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !string.Equals(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter),
				XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return false;
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Count <= 0 || state.Count >= XjXianJiState.MaxCount) return false;
		int ordinal = state.Count + 1;
		string id;
		long seed = GetActorId(actor) + currentYear * 17L;
		bool picked;
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			picked = TryPickImperialNonIntercalaryShenTong(snapshot.DaoTu, state, seed, out id);
		}
		else
		{
			bool avoidZhengWeiRoute = ShouldAvoidSameFamilyZhengWeiRoute(actor, snapshot.DaoTu, state);
			picked = avoidZhengWeiRoute
				? TryPickNonZhengWeiBranchShenTong(
					snapshot.DaoTu, state, seed,
					!actor.hasTrait("ChuShen8") && !XjLongShuSystem.IsLongShu(actor), out id)
				: actor.hasTrait("ChuShen8")
					? XjXianJiCatalog.TryPickDaoZhuForProgression(
						snapshot.DaoTu, ordinal, seed, state.Ids,
						IsZhengWeiManifested(snapshot.DaoTu), out id)
					: XjXianJiCatalog.TryPickForProgression(
						snapshot.DaoTu, ordinal, seed, state.Ids,
						IsZhengWeiManifested(snapshot.DaoTu), !XjLongShuSystem.IsLongShu(actor), out id);
		}

		if (!picked
			|| !TryAddAndResolveGrant(actor, snapshot, state, id, currentYear, string.Empty, "事件洞天交易"))
		{
			return false;
		}
		shenTongId = id;
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
			2 => 0.16f,
			3 => 0.08f,
			4 => 0.04f,
			5 => 0.02f,
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

	private static bool ShouldAvoidSameFamilyZhengWeiRoute(
		Actor actor,
		string daoTu,
		in XjXianJiState state)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(daoTu)
			|| state.Count < 3
			|| !HasOnlyNativeUpper(daoTu, state))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| ActorAlreadyHoldsActiveZhengWei(actorId, daoTu)
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L)
		{
			return false;
		}

		return XjDaoTuHeritageService.ResolveFamilyControl(familyId, daoTu).HoldsFruit;
	}

	private static bool HasOnlyNativeUpper(string daoTu, in XjXianJiState state)
	{
		if (state.Ids == null || state.Count <= 0)
		{
			return false;
		}

		int limit = Math.Min(state.Count, state.Ids.Length);
		for (int i = 0; i < limit; i++)
		{
			if (XjXianJiCatalog.GetPoolKind(daoTu, state.Ids[i]) != XjXianJiPoolKind.Native)
			{
				return false;
			}
		}
		return limit == state.Count;
	}

	private static bool ActorAlreadyHoldsActiveZhengWei(long actorId, string daoTu)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (actorId <= 0L || string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found || !entry.IsActive || entry.ActorId != actorId
				|| !string.Equals(entry.DaoTu, normalizedDaoTu, StringComparison.Ordinal))
			{
				continue;
			}
			if (string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei),
				XjGuoWeiCalculator.ZhengWei,
				StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// 帝明阳的紫金仙基只在本道上位/下位中生发。相邻道途在普通紫金里仍属
	/// 合法闰路来源，但帝统已经把明阳仙基重新收束，故这里也必须排除 Adjacent。
	/// 正位尚未显化时优先上位，正位已显化后优先下位，以便自然形成果/余两路。
	/// </summary>
	internal static bool TryPickImperialNonIntercalaryShenTong(
		string daoTu,
		in XjXianJiState state,
		long seed,
		out string id)
	{
		string[] existingIds = state.Ids ?? Array.Empty<string>();
		bool zhengWeiManifested = IsZhengWeiManifested(daoTu);
		XjXianJiPoolKind first = zhengWeiManifested ? XjXianJiPoolKind.Lower : XjXianJiPoolKind.Native;
		XjXianJiPoolKind second = zhengWeiManifested ? XjXianJiPoolKind.Native : XjXianJiPoolKind.Lower;
		if (XjXianJiCatalog.TryPickFromPool(daoTu, first, seed + 43L, existingIds, string.Empty, out id)) return true;
		if (XjXianJiCatalog.TryPickFromPool(daoTu, second, seed + 97L, existingIds, string.Empty, out id)) return true;
		id = string.Empty;
		return false;
	}

	private static bool TryPickNonZhengWeiBranchShenTong(
		string daoTu,
		in XjXianJiState state,
		long seed,
		bool allowOtherPool,
		out string id)
	{
		string[] existingIds = state.Ids ?? Array.Empty<string>();
		if (XjXianJiCatalog.TryPickFromPool(
			daoTu, XjXianJiPoolKind.Lower, seed + 101L, existingIds, string.Empty, out id))
		{
			return true;
		}
		if (XjXianJiCatalog.TryPickFromPool(
			daoTu, XjXianJiPoolKind.Adjacent, seed + 211L, existingIds, string.Empty, out id))
		{
			return true;
		}
		if (allowOtherPool
			&& XjXianJiCatalog.TryPickFromPool(
				daoTu, XjXianJiPoolKind.Other, seed + 307L, existingIds, string.Empty, out id))
		{
			return true;
		}

		id = string.Empty;
		return false;
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
