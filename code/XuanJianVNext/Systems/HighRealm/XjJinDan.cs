using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjJinDanState
{
	internal static XjJinDanState Empty { get; } = new XjJinDanState(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		0,
		0,
		"Empty");

	internal readonly bool Found;
	internal readonly string JinXing;
	internal readonly string GuoWei;
	internal readonly string FailedState;
	internal readonly int LastAttemptYear;
	internal readonly int SuccessYear;
	internal readonly string ReasonCode;

	internal XjJinDanState(
		bool found,
		string jinXing,
		string guoWei,
		string failedState,
		int lastAttemptYear,
		int successYear,
		string reasonCode)
	{
		Found = found;
		JinXing = jinXing ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		FailedState = failedState ?? string.Empty;
		LastAttemptYear = lastAttemptYear < 0 ? 0 : lastAttemptYear;
		SuccessYear = successYear < 0 ? 0 : successYear;
		ReasonCode = reasonCode ?? string.Empty;
	}
}


internal static partial class XjJinDanBreakthroughSystem
{
	private const int SuccessEventSchemaVersion = 2;
	private const float RequiredZhenYuan = 129600f;
	private const int JinDanFailureYaoXieChancePercent = 100;

	internal static void TickActor(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| IsAlreadyResolvedHighRealm(actor, snapshot)
			|| !string.Equals(XjRealmHelper.NormalizeId(snapshot.RealmId), XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return;
		}

		// 金丹主链独立于普通境界突破器，必须在此处执行相同资质门槛。
		// 五六档会形成稳定的主动求金之志；四档默认止步紫府，只有完整上修扶金
		// 推进到“法成候金”后才重启求金。
		bool daoZhu = actor.hasTrait("ChuShen8");
		if (!daoZhu && (snapshot.XjZz < 4 || snapshot.XjZz > 6))
		{
			// 资质被外部编辑/转修压回求金门槛以下时，旧的求金之志不能继续残留。
			XjQiuJinIntentSystem.Clear(actor);
			return;
		}

		// 求金之志只属于真正走到五门圆满的紫府大真人；普通紫府仍在积修，
		// 不应过早被标记为“止步紫府”或“志在叩金”。
		if (!XjXianJiAccessor.HasFive(actor))
		{
			XjQiuJinIntentSystem.Clear(actor);
			return;
		}

		int currentYear = GetCurrentYear(actor);
		bool fourthAptitude = !daoZhu && snapshot.XjZz == 4;
		int upperGuidanceStage = fourthAptitude ? XjUpperCultivatorGoldSupportSystem.ResolveJinDanGuidanceStage(actor) : 0;

		// 求金之志从五门圆满、成为紫府大真人后作为长期人物决意存在；仍复用现有高境年度车道，
		// 但签名不含年份，因此不会每年重抽或反复写盘。重大条件改变才会换志。
		XjQiuJinIntentSystem.Decision qiuJinIntent = XjQiuJinIntentSystem.ResolveForEligibleZiFu(
			actor, snapshot, currentYear, upperGuidanceStage);
		bool hasMatureUpperGuidance = upperGuidanceStage >= 4
			|| string.Equals(qiuJinIntent.Reason, "UpperGuidanceMature", StringComparison.Ordinal);
		if (!qiuJinIntent.AllowsAttempt)
		{
			string deferredReason = qiuJinIntent.State == XjQiuJinIntentSystem.StateAwaitUpperGuidance
				? "UpperGuidanceInProgress"
				: "QiuJinIntentHold";
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, deferredReason);
			return;
		}

		// 有求金之志不等于金门条件已经完备。这里之后只检查真实硬条件；
		// 一旦全部成立，本轮直接叩门，不再追加年度“触发率”。
		if (snapshot.ZhenYuan < RequiredZhenYuan)
		{
			return;
		}

		// 百官一旦受仙国法持玄，最高只能到制度性的“仙国假金丹”。
		// 在法统有效期间不进入真正求金主链，因此不会消耗求金尝试、丹药/符箓，
		// 更不会产生果位意象、金性、果位、权柄或真实金丹成功状态。离朝后标记
		// 清除，本人仍可凭自己的真实紫府根基重新正常求金。
		if (XjXianGuoSystem.HasActiveInstitutionalCultivation(actor))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, "XianGuoFalseJinDanCap");
			return;
		}
		if (XjXianGuoSystem.IsFruitAttemptSuppressedForHeir(actor))
		{
			XjQiuJinIntentSystem.Clear(actor);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, "XianGuoHeirFruitLine");
			return;
		}

		XjJinDanState jinDanState = XjJinDanAccessor.BuildState(actor);
		if (jinDanState.Found)
		{
			return;
		}
		if (!string.IsNullOrWhiteSpace(jinDanState.FailedState))
		{
			// 旧存档失败态由 Prepare 阶段统一迁移/结算；此处不允许
			// 一个字符串把活着的紫府永久锁死在金丹门外。
			ReconcileFailureDemonization(actor);
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}
			jinDanState = XjJinDanAccessor.BuildState(actor);
			if (!string.IsNullOrWhiteSpace(jinDanState.FailedState))
			{
				return;
			}
		}

		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || gongFa.Grade != 6 || !XjActorGongFaCollection.HasJinDanGongFaSet(actor))
		{
			// 金丹前必须真实持有一部六品与四部五品功法，且五部功法
			// 分别映射五门仙基。临时 UI 快照不能作为突破凭据。
			return;
		}
		// 求金法只用于五品功法晋升六品功法。角色已经真实持有六品
		// 主功法后，成丹不再以求金法状态作为前置。
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);

		if (string.IsNullOrWhiteSpace(snapshot.DaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(snapshot.DaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		}
		if (string.IsNullOrWhiteSpace(snapshot.DaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(snapshot.DaoTu.Trim(), out _))
		{
			// 数据修复失败时延后重试，不能把角色错误处死。
			return;
		}

		if (XjQingXuanKongZhengSystem.IsQingXuanDaoTu(snapshot.DaoTu)
			&& !XjQingXuanKongZhengSystem.HasPreJinDanFiveShenTong(actor))
		{
			// 五仙基尚未全部被玄羊子抬举为神通时，不消耗丹药、符箓或真实求金机会。
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, "QingXuanFiveShenTongIncomplete");
			return;
		}
		if (jinDanState.LastAttemptYear == currentYear)
		{
			return;
		}

		long actorId = GetActorId(actor);

		XjXianGuoSystem.EnsureImperialNonIntercalaryXianJi(actor, currentYear);
		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		if (!daoZhu && HasCrossDaoTuXianJi(actor, snapshot.DaoTu))
		{
			// 错道仙基属于人物自身真实求金结构错误，不是系统延期；这里才正式消费一次叩门。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, currentYear);
			XjStageZeroObservation.RecordJinDanAttemptStarted();
			XjStageZeroObservation.RecordJinDanResult("CrossDaoTuSpell", false);
			ResolveForcedDeathFailure(actor, currentYear, "CrossDaoTuSpell");
			return;
		}

		string candidateGuoWeiType = XjGuoWeiCalculator.Calculate(actor, snapshot.DaoTu, xianJiState);
		// 正果叩门时确认已有活着的持位者，才会写入这项人物决意；之后仍沿
		// 原有金丹/果位事务，只把本道的下一次目标改为余位，不启用跨道途兜底。
		if (string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& IsPursuingResidualPosition(actor))
		{
			candidateGuoWeiType = XjGuoWeiCalculator.YuWei;
		}
		// 帝明阳是正统明阳仙法：最终高境席位只允许本道果位或余位。
		// 闰位、神丹与任何未知分支都不能成为帝明阳的成丹出口。
		if (XjXianGuoSystem.IsDiMingYang(actor)
			&& !string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& !string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			candidateGuoWeiType = XjGuoWeiCalculator.NoDoor;
		}

		string candidateJinDanDaoTu = string.Empty;
		string candidateIntercalarySourceDaoTu = string.Empty;
		if (string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			// 闰位不以角色当前道途作为果位归属。五门神通必须唯一解出
			// “根本道途→外显道途”，例如府水根基修牝水神通即显为牝水闰位。
			if (!XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
				actor, xianJiState, out candidateIntercalarySourceDaoTu, out candidateJinDanDaoTu))
			{
				candidateGuoWeiType = XjGuoWeiCalculator.NoDoor;
			}
		}
		else if (!string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			candidateJinDanDaoTu = XjGuoWeiCalculator.ResolveManifestDaoTu(
				snapshot.DaoTu, xianJiState, candidateGuoWeiType);
		}

		// P0：纯系统/世界容量前置必须在“真实叩门”之前完成。
		// 这些状态只代表眼下还没有可结算的金门，绝不能先掷成功率再把程序延期伪装成求金。
		string sourceDaoTu = string.IsNullOrWhiteSpace(candidateIntercalarySourceDaoTu)
			? snapshot.DaoTu
			: candidateIntercalarySourceDaoTu;
		string proofDaoTitle = XjHighRealmDaoStateService.ResolvePromotionDaoTitle(actor);
		string jinXing = string.Empty;
		if (!string.Equals(candidateGuoWeiType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			if (string.IsNullOrWhiteSpace(candidateJinDanDaoTu))
			{
				RecordDeferredAttempt(actor, "NoGuoWeiDaoTu");
				return;
			}
			if (!XjGuoWeiQuanBingRegistry.HasEnoughAvailableAuthorities(candidateJinDanDaoTu, candidateGuoWeiType))
			{
				RecordDeferredAttempt(actor, "QuanBingDeficient");
				return;
			}

			jinXing = XjJinXingCalculator.Calculate(candidateJinDanDaoTu, actorId);
			jinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
				actor, sourceDaoTu, candidateJinDanDaoTu, candidateGuoWeiType, jinXing);
			if (string.IsNullOrWhiteSpace(jinXing))
			{
				RecordDeferredAttempt(actor, "JinXingUnavailable");
				return;
			}
		}

		// 条件与求金之志都已成立，而且不存在纯系统延期：到这里才算一次真实叩金。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, currentYear);
		XjStageZeroObservation.RecordJinDanAttemptStarted();

		XjGuoWeiRegistryEntry schemer = default;
		float schemerMultiplier = 1f;
		bool hasZhengWeiSchemer = false;
		if (!daoZhu)
		{
			hasZhengWeiSchemer = XjGuoWeiRegistry.TryResolveZhengWeiSchemerInterference(
				candidateJinDanDaoTu,
				candidateGuoWeiType,
				actorId,
				out schemer,
				out schemerMultiplier);
		}
		float longShuHeShuiBonus = 0f;
		float rawSuccessChance;
		if (daoZhu)
		{
			// 道胎之姿优先于所有修炼失败标签；污染可以作为世界状态存在，
			// 但不能把这一次破境改写成必败或修炼死亡。
			rawSuccessChance = 1f;
		}
		else if (XjRenDan.HasPollution(actor))
		{
			rawSuccessChance = 0f;
		}
		else if (fourthAptitude && !hasMatureUpperGuidance)
		{
			// 防御性兜底：正常主链中的四档在此之前已被“止步紫府/静候扶金”拦住；
			// 即使未来某个外部入口绕过求金之志，也不能靠丹药、符箓或仙国反哺代替成熟上修指引。
			rawSuccessChance = 0f;
		}
		else
		{
			// 四档资质在成熟上修指引下，以15%作为自身根基部分，再叠加扶持项目
			// 的成功加成；普通丹药与符箓仍不能替四档补足这道先天缺口。
			float aidBonus = snapshot.XjZz == 4
				? 0f
				: XjAlchemyPillEffectSystem.TryConsumeBreakthroughBonus(actor, XjRealmIds.JinDan, currentYear)
					+ XjTalismanCombatService.TryConsumeBreakthroughAid(actor, XjRealmIds.JinDan);
			aidBonus = Math.Min(0.10f, Math.Max(0f, aidBonus));
			float aptitudeCap = ResolveJinDanAptitudeSuccessCap(snapshot.XjZz);
			float imperialBonus = XjXianGuoSystem.ResolveBreakthroughSuccessBonus(actor, XjRealmIds.JinDan);
			float upperSupportBonus = XjUpperCultivatorGoldSupportSystem.ResolveJinDanSuccessBonus(actor);
			longShuHeShuiBonus = XjLongShuSystem.ResolveHeShuiBreakthroughSuccessBonus(
				actor, candidateJinDanDaoTu, candidateGuoWeiType);
			float effectiveCap = Math.Min(0.98f, aptitudeCap + imperialBonus + upperSupportBonus + longShuHeShuiBonus);
			rawSuccessChance = Math.Min(effectiveCap, CalculateSuccessChance(actor, snapshot) + aidBonus
				+ imperialBonus + upperSupportBonus + longShuHeShuiBonus);
			rawSuccessChance = XjLongevityRacePenalty.ApplyJinDanBreakthroughPenalty(actor, rawSuccessChance);
		}
		float successChance = hasZhengWeiSchemer ? rawSuccessChance * schemerMultiplier : rawSuccessChance;
		if (!daoZhu)
		{
			// 普通修士仍受原资质上限；帝明阳的仙国反哺可在该上限之上提供额外助力。
			float imperialCapBonus = XjXianGuoSystem.ResolveBreakthroughSuccessBonus(actor, XjRealmIds.JinDan);
			float upperSupportCapBonus = XjUpperCultivatorGoldSupportSystem.ResolveJinDanSuccessBonus(actor);
			successChance = Math.Min(successChance, Math.Min(0.98f, ResolveJinDanAptitudeSuccessCap(snapshot.XjZz)
				+ imperialCapBonus + upperSupportCapBonus + longShuHeShuiBonus));
		}
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		int successRoll = PositiveRollBasisPoints(actorId, currentYear, "jindan_success");
		if (successRoll >= (int)(successChance * 10000f))
		{
			if (hasZhengWeiSchemer && successRoll < (int)(rawSuccessChance * 10000f))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative,
					BuildZhengWeiSchemerFailureNarrative(actor, schemer));
			}
			XjStageZeroObservation.RecordJinDanResult("BreakthroughFailed", false);
			ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "BreakthroughFailed");
			return;
		}

		// 太阴、太阳触及金门时，有极低概率被道胎直接截断。该结算只会
		// 出现在原本应当通过的成丹尝试中，不能把普通失败伪装成道胎之劫。
		if (!daoZhu && XjJinDanDaoTaiInterception.TryResolve(actor, candidateJinDanDaoTu, currentYear))
		{
			XjStageZeroObservation.RecordJinDanResult("DaoTaiInterception", false);
			return;
		}

		// 法门组合自身无门仍属于人物求金失败；真正的席位占用、封锁、神丹改路等
		// 只有在一次真实叩门通过自身成功率以后才进入最终位序结算。纯系统容量与金性
		// 不可用已经在上方前置延期，不会再先消费一次求金。
		string guoWeiType = candidateGuoWeiType;
		if (string.Equals(guoWeiType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			XjStageZeroObservation.RecordJinDanResult("NoGuoWei", false);
			ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "NoGuoWei");
			return;
		}

		string jinDanDaoTu = candidateJinDanDaoTu;
		if (!XjGuoWeiRegistry.TryResolveAvailableGuoWeiDetailed(
				jinDanDaoTu,
				guoWeiType,
				actorId,
				actorId + currentYear,
				false,
				out _,
				out string guoWei,
				out XjGuoWeiAvailabilityReason availabilityReason))
		{
			if (availabilityReason == XjGuoWeiAvailabilityReason.RuleBlocked
				&& XjLongShuSystem.IsHeShuiFruitPosition(jinDanDaoTu, guoWeiType)
				&& !XjLongShuSystem.IsLongShu(actor))
			{
				XjBroadcastSystem.BroadcastSLevelDomainEvent(
					XjWorldHistoryCategory.HighRealm,
					"HeShuiFruitReservedForLongShu",
					"【合水果位只认龙血】" + actor.getName() + "求证合水果位，金门将合之际，九子遗脉自水德深处翻涌，将其法身生生排斥。此位自龙属肇生后，只认龙血，不纳异脉。",
					actor.getName() + "求证合水果位，于最后一步被龙属血契拒绝。",
					actorId: actorId, actorName: actor.getName(), year: currentYear,
					iconId: XjEventIconCatalog.HistoryWorld);
				XjStageZeroObservation.RecordJinDanResult("HeShuiFruitReservedForLongShu", false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "HeShuiFruitReservedForLongShu");
				return;
			}

			if (availabilityReason == XjGuoWeiAvailabilityReason.Hidden
				&& IsPermanentlyLockedGuoWeiAttempt(jinDanDaoTu, guoWeiType))
			{
				BroadcastPermanentlyLockedGuoWei(actor, jinDanDaoTu, guoWeiType);
				XjStageZeroObservation.RecordJinDanResult("PermanentlyLockedGuoWei", false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, "PermanentlyLockedGuoWei");
				return;
			}

			if (availabilityReason == XjGuoWeiAvailabilityReason.Occupied)
			{
				if (string.Equals(guoWeiType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
				{
					SwitchToResidualPositionPursuit(actor);
					XjGuoWeiOwnerProbeEvent.TryRevealOnOccupiedAttempt(
						actor, jinDanDaoTu, guoWeiType, currentYear);
					RecordDeferredAttempt(actor, "ZhengWeiOccupiedTurnYuWei");
					return;
				}
				if (XjZiJinSwordDaoCatalog.IsLongGeng(jinDanDaoTu))
				{
					RecordDeferredAttempt(actor, "LongGengYuWeiOccupied");
					return;
				}
				bool hasShenDanMethod = XjShenDanMethodSystem.CanPursue(
					actor, jinDanDaoTu, guoWeiType, qiuJinFa, currentYear);
				if (hasShenDanMethod
					&& TryPromoteShenDan(actor, snapshot, xianJiState, gongFa, qiuJinFa, jinDanDaoTu, guoWeiType, currentYear))
				{
					return;
				}
				string occupiedFailure = hasShenDanMethod
					? "ShenDanCapacityFull" : "GuoWeiOccupiedNoShenDanMethod";
				XjGuoWeiOwnerProbeEvent.TryRevealOnOccupiedAttempt(
					actor, jinDanDaoTu, guoWeiType, currentYear);
				XjStageZeroObservation.RecordJinDanResult(occupiedFailure, false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, occupiedFailure);
				return;
			}

			RecordDeferredAttempt(actor, "GuoWeiUnavailable:" + availabilityReason);
			return;
		}
		// 可选上修阻道只在求金者已经通过自身成丹判定、且本次确有可承证位序后触发。
		// 直接由高境事务裁定，不驱动寻路或真实战斗；同族同宗上修永不入选。
		if (XjUpperCultivatorDaoObstructionSystem.TryResolve(actor, currentYear))
		{
			XjStageZeroObservation.RecordJinDanResult("UpperCultivatorDaoObstruction", false);
			return;
		}

		if (!XjGuoWeiRegistry.TryClaim(actor, jinDanDaoTu, jinXing, guoWei, currentYear, candidateIntercalarySourceDaoTu))
		{
			if (XjZiJinSwordDaoCatalog.IsLongGeng(jinDanDaoTu))
			{
				RecordDeferredAttempt(actor, "LongGengYuWeiClaimConflict");
				return;
			}
			// 同年抢位后只有注册表已经出现真实锚点，且本人早已参悟对应托果法门，才可转神丹。
			bool anchorNowExists = XjGuoWeiRegistry.TryFindActiveAnchor(jinDanDaoTu, guoWeiType, out _);
			bool hasShenDanMethod = anchorNowExists && XjShenDanMethodSystem.CanPursue(
				actor, jinDanDaoTu, guoWeiType, qiuJinFa, currentYear);
			if (hasShenDanMethod
				&& TryPromoteShenDan(actor, snapshot, xianJiState, gongFa, qiuJinFa, jinDanDaoTu, guoWeiType, currentYear))
			{
				return;
			}
			if (anchorNowExists)
			{
				string occupiedFailure = hasShenDanMethod
					? "ShenDanCapacityFull" : "GuoWeiOccupiedNoShenDanMethod";
				XjGuoWeiOwnerProbeEvent.TryRevealOnOccupiedAttempt(
					actor, jinDanDaoTu, guoWeiType, currentYear, guoWei);
				XjStageZeroObservation.RecordJinDanResult(occupiedFailure, false);
				ResolveAttemptFailure(actor, snapshot, xianJiState, currentYear, occupiedFailure);
				return;
			}

			RecordDeferredAttempt(actor, "GuoWeiClaimConflict");
			return;
		}
		if (!string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			jinXing = XjFruitPositionWorldState.ResolveJinXingName(guoWei, jinXing);
		}
		string originalDaoTu = snapshot.DaoTu;
		bool preserveRunWeiCultivation =
			string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
		// 闰位的“显道”只属于果位身份，角色修炼根道必须继续保持 sourceDaoTu。
		// 过去把 DaoTu 写成 jinDanDaoTu(显道)，随后仙基 Reconcile 会按显道修前三门，
		// 直接制造“厥阴闰太阴却被改成四门太阴”的反向重写。果位/余位仍走完整道途事务。
		if (preserveRunWeiCultivation)
		{
			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, sourceDaoTu, false);
		}
		else if (!XjCultivationStateTransitions.TrySetDaoTu(actor, jinDanDaoTu, false))
		{
			XjStageZeroObservation.RecordJinDanResult("DaoTuWriteRejected", false);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}
		if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false))
		{
			XjStageZeroObservation.RecordJinDanResult("RealmWriteRejected", false);
			RestoreDaoTuAfterFailedPromotion(actor, originalDaoTu);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}

		XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, currentYear);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			XjStageZeroObservation.RecordJinDanResult("SuccessStateWriteFailed", false);
			XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
			RestoreDaoTuAfterFailedPromotion(actor, originalDaoTu);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return;
		}

		RunJinDanSuccessStep("YinSiCleanup", () => XjYinSiTraitLifecycle.EnsureRemovedFromJinDan(actor));
		RunJinDanSuccessStep("CommonPromotion", () =>
			XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.JinDan, currentYear));
		RunJinDanSuccessStep("Title", () =>
			XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.JinDan, jinDanDaoTu));
		// 先写入根道、显道、闰法与动态权辖，再生成实际权柄快照；
		// 否则证道时会退回旧版固定权柄目录。
		XjHighRealmDaoStateService.InitializeOnPromotion(
			actor, sourceDaoTu, jinDanDaoTu, guoWeiType, guoWei, jinXing,
			currentYear, false, proofDaoTitle);
		RunJinDanSuccessStep("Authority", () =>
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, jinDanDaoTu, guoWei, currentYear));
		RunJinDanSuccessStep("HighGradeTransmission", () =>
			XjFamilyHighGradeTransmission.RecordJinDanGongFaSet(actor, jinDanDaoTu, gongFa, qiuJinFa, xianJiState, currentYear));
		RunJinDanSuccessStep("UpperGoldSupportSettlement", () =>
			XjUpperCultivatorGoldSupportSystem.OnJinDanPromotionSuccess(actor, currentYear));
		XjStageZeroObservation.RecordJinDanResult("JinDanSuccess", true);
		RunJinDanSuccessEventChain(actor, jinDanDaoTu, jinXing, guoWei, currentYear, new XjActorCultivationSnapshot(
			snapshot.RealmId,
			jinDanDaoTu,
			snapshot.ZhenYuan,
			snapshot.MingShu,
			snapshot.HuiGuang,
			snapshot.XjZz,
			snapshot.XjZzOverlayMask,
			snapshot.XianJiCount,
			snapshot.HasQiuJinFa));
	}

	private static bool IsAlreadyResolvedHighRealm(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null) return true;
		string realmId = XjRealmHelper.NormalizeId(snapshot.RealmId);
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| XjShenDanAccessor.BuildState(actor).Found
			|| XjJinDanAccessor.BuildState(actor).Found
			|| XjXuanJianShenTongSpecials.IsJieLinXian(actor)
			|| XjXuanJianShenTongSpecials.IsYuYiXian(actor)
			|| XjDaoTaiSpellScale.IsDaoTaiActor(actor);
	}

	private static void RecordDeferredAttempt(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return;
		}
		string normalizedReason = (reason ?? string.Empty).Trim();
		XjStageZeroObservation.RecordJinDanResult("Deferred:" + normalizedReason, false);
		XjActorAccessor.SetString(
			actor,
			XjActorDataKeys.XjJinDanDeferredReason,
			normalizedReason);
	}

	private static bool IsPursuingResidualPosition(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanPositionPursuit, out string pursuit)
			&& string.Equals((pursuit ?? string.Empty).Trim(), XjGuoWeiCalculator.YuWei, StringComparison.Ordinal);
	}

	private static void SwitchToResidualPositionPursuit(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanPositionPursuit, XjGuoWeiCalculator.YuWei);
	}

	private static void ResolveAttemptFailure(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState xianJiState,
		int currentYear,
		string reason)
	{
		if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
		{
			// 容量/果位等结构性阻塞只延后到下一次年度尝试，绝不转化为结璘、妖邪或死亡。
			XjJinDanAccessor.ClearFailure(actor);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason,
				"DaoTaiPosture:" + ((reason ?? string.Empty).Trim()));
			return;
		}

		// 真实叩金门失败后结算上修扶金的探月/扶持关系；结构性延期不会进入这里。
		XjUpperCultivatorGoldSupportSystem.OnJinDanAttemptFailed(actor, currentYear, reason);

		if (snapshot.XjZz == 4)
		{
			// 四档资质的求金本就是明知根基不足的搏命之举：一旦真实
			// 尝试失败，直接身死，不再转结璘仙或金性妖邪。
			XjActorAccessor.SetString(
				actor,
				XjActorDataKeys.XjJinDanFailureNarrative,
				BuildFourthAptitudeFailureNarrative(actor));
			ResolveTerminalFailure(actor, currentYear, reason);
			return;
		}

		// 太阴果位在世时，其他资质修士的真实求金失败优先转为结璘仙。
		// 只有不满足结璘条件时，才进入金性妖邪/死亡的通用终局。
		if (XjXuanJianShenTongSpecials.TryResolveJieLinXianOnJinDanFailure(
			actor, snapshot, xianJiState, currentYear))
		{
			return;
		}
		if (XjXuanJianShenTongSpecials.TryResolveYuYiXianOnJinDanFailure(
			actor, snapshot, xianJiState, currentYear))
		{
			return;
		}

		ResolveTerminalFailure(actor, currentYear, reason);
	}

	private static void RestoreDaoTuAfterFailedPromotion(Actor actor, string originalDaoTu)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(originalDaoTu)
			&& XjCultivationStateTransitions.TrySetDaoTu(actor, originalDaoTu, false))
		{
			return;
		}

		XjCultivationStateTransitions.ClearDaoTu(actor, false);
	}
	/// <summary>
	/// 计算金丹突破成功率 = maxChance * mingShuFactor；资质仅负责境界上限，不参与概率
	/// mingShu（0.5.4中称为xueQi）使用境界乘数放大先天部分
	/// </summary>
	/// <summary>
	/// 金丹赐福（金丹自生）：金丹修士每10年自动向家族注入对应道途灵气
	/// 对应 0.5.4 TickAnnualJinDanQiGift
	/// </summary>
}
