using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 长庚独有的世界唯一果位裁决。公共服气状态机负责候选成熟、概率和失败，
/// 本Handler只负责同年唯一胜者、世界果位事务与“长庚”首次命名。
/// </summary>
internal static class XjFuQiLongGengPositionHandler
{
	private static int _arbitrationYear = -1;
	private static long _winnerActorId;

	internal static void ClearRuntimeState()
	{
		_arbitrationYear = -1;
		_winnerActorId = 0L;
	}

	internal static void ProcessYear(int currentYear)
	{
		if (_arbitrationYear == currentYear || currentYear <= 0 || XjFuQiSwordWorldState.HasCurrentHolder) return;
		_arbitrationYear = currentYear;
		_winnerActorId = 0L;
		bool firstEstablishment = !XjFuQiSwordWorldState.IsEstablished;
		if (!XjFuQiCoreCatalog.TryGetByRootId(XjDaoTuRootIds.LongGeng, out XjFuQiCoreDefinition definition)) return;

		List<Actor> candidates = new List<Actor>();
		List<int> aptitudes = new List<int>();
		IReadOnlyList<long> ids = XjFuQiCandidateIndex.GetActorIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor candidate)
				|| !XjFuQiCultivationSystem.TryPrepareJinDanCandidate(candidate, currentYear, definition, out int aptitude)) continue;
			candidates.Add(candidate);
			aptitudes.Add(aptitude);
		}
		if (candidates.Count == 0) return;

		float bestScore = float.MinValue;
		for (int i = 0; i < candidates.Count; i++)
		{
			Actor candidate = candidates[i];
			long candidateId = ((BaseSystemData)candidate.data).id;
			float chance = XjFuQiCultivationSystem.ResolveJinDanSuccessChance(candidate, aptitudes[i]);
			if (chance <= 0f
				|| XjDeterministicHash.Roll01(candidateId, currentYear, "fuqi_sword_jindan", "success") >= chance) continue;
			XjActorAccessor.TryGetFloat(candidate, XjActorDataKeys.HuiGuang, out float huiGuang);
			float score = chance * 1000f
				+ XjBreakthroughRules.CalculateMingShuFactor(candidate, XjRealmIds.JinDan) * 100f
				+ Math.Max(0f, huiGuang);
			if (_winnerActorId <= 0L || score > bestScore
				|| Math.Abs(score - bestScore) < 0.0001f && candidateId < _winnerActorId)
			{
				bestScore = score;
				_winnerActorId = candidateId;
			}
		}

		bool winnerCompleted = false;
		for (int i = 0; i < candidates.Count; i++)
		{
			Actor candidate = candidates[i];
			long candidateId = ((BaseSystemData)candidate.data).id;
			XjActorAccessor.SetInt(candidate, XjActorDataKeys.FuQiJinDanLastAttemptYear, currentYear);
			if (candidateId == _winnerActorId)
			{
				winnerCompleted = CompleteZhenJunYuShi(candidate, currentYear, firstEstablishment);
				if (winnerCompleted) continue;
			}
			XjFuQiCultivationSystem.ResolveJinDanFailure(candidate, currentYear, definition);
		}
		if (!winnerCompleted) _winnerActorId = 0L;
	}

	internal static bool TryCompleteManualZhenJun(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || XjFuQiSwordWorldState.HasCurrentHolder)
		{
			return false;
		}
		return CompleteZhenJunYuShi(actor, currentYear, !XjFuQiSwordWorldState.IsEstablished);
	}

	private static bool CompleteZhenJunYuShi(Actor actor, int currentYear, bool firstEstablishment)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFuQiCoreCatalog.TryGetByRootId(
				XjDaoTuRootIds.LongGeng,
				out XjFuQiCoreDefinition definition))
		{
			return false;
		}
		int previousVacantSinceYear = 0;
		bool worldCommitted = firstEstablishment
			? XjFuQiSwordWorldState.TryEstablish(actor, currentYear)
			: XjFuQiSwordWorldState.TryClaimVacantPosition(actor, currentYear, out previousVacantSinceYear);
		if (!worldCommitted) return false;
		string promotionDaoTitle = XjHighRealmDaoStateService.ResolvePromotionDaoTitle(actor);
		string daoTu = "长庚";
		string guoWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
			daoTu, XjGuoWeiCalculator.ZhengWei, XjGuoWeiQuanBingRules.ZhengWeiSlotCount);
		string jinXing = XjJinXingCalculator.Calculate(daoTu, actorId);
		if (!XjGuoWeiRegistry.TryClaim(actor, daoTu, jinXing, guoWei, currentYear))
		{
			RollbackWorldPosition(actorId, currentYear, firstEstablishment, previousVacantSinceYear);
			return false;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string previousDaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, daoTu);
		if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.ZhenJunYuShi, false, true))
		{
			RollbackActorPromotion(
				actor,
				actorId,
				guoWei,
				previousDaoTu,
				currentYear,
				firstEstablishment,
				previousVacantSinceYear,
				in definition);
			return false;
		}
		XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, currentYear);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			RollbackActorPromotion(
				actor,
				actorId,
				guoWei,
				previousDaoTu,
				currentYear,
				firstEstablishment,
				previousVacantSinceYear,
				in definition);
			return false;
		}
		if (!XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear))
		{
			RollbackActorPromotion(
				actor,
				actorId,
				guoWei,
				previousDaoTu,
				currentYear,
				firstEstablishment,
				previousVacantSinceYear,
				in definition);
			return false;
		}
		if (firstEstablishment && !XjFuQiSwordWorldState.CommitEstablishment(actorId, currentYear))
		{
			RollbackActorPromotion(
				actor,
				actorId,
				guoWei,
				previousDaoTu,
				currentYear,
				true,
				previousVacantSinceYear,
				in definition);
			return false;
		}
		XjHighRealmDaoStateService.InitializeOnPromotion(
			actor, daoTu, daoTu, XjGuoWeiCalculator.ZhengWei, guoWei, jinXing, currentYear,
			isFuQi: true, daoTitleOverride: promotionDaoTitle);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			XjRealmIds.ZhenJunYuShi,
			currentYear);
		XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.ZhenJunYuShi, daoTu);
		XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, daoTu, guoWei, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, 0);
		XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		if (firstEstablishment)
		{
			XjDaoTuManifestRegistry.MarkCaiQiUnlocked(
				XjDaoTuRootIds.LongGeng,
				actorId,
				currentYear);
			XjFuQiSwordWorldState.SyncLineageTraits();
			XjThreeBookWriter.RecordFuQiZhenJunYuShi(actor, currentYear);
			XjLongGengSwordSteleSystem.EnsureCreated(actor, currentYear);
			PublishLongGengEstablishment(actor);
		}
		else
		{
			XjThreeBookWriter.RecordFuQiLongGengSuccession(actor, currentYear);
			PublishLongGengSuccession(actor);
		}
		XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZhenJunYuShi, "FuQiLongGengZhenJunPromotion");
		XjActorCultivationSnapshot successSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjJinDanBreakthroughSystem.RunJinDanSuccessEventChain(
			actor,
			daoTu,
			jinXing,
			guoWei,
			currentYear,
			in successSnapshot,
			publishPromotionAnnouncement: false,
			eraChangeCauseOverride: XjAnnouncementText.BuildFuQiZhenJunEraChangeCause(
				actor,
				daoTu,
				jinXing,
				guoWei));
		return true;
	}

	private static void RollbackActorPromotion(
		Actor actor,
		long actorId,
		string guoWei,
		string previousDaoTu,
		int currentYear,
		bool firstEstablishment,
		int previousVacantSinceYear,
		in XjFuQiCoreDefinition definition)
	{
		XjJinDanAccessor.ClearSuccess(actor);
		XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
		XjActorAccessor.SetString(
			actor,
			XjActorDataKeys.DaoTu,
			(previousDaoTu ?? string.Empty).Trim());
		XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
		XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
		RollbackWorldPosition(actorId, currentYear, firstEstablishment, previousVacantSinceYear);
	}

	private static void RollbackWorldPosition(
		long actorId,
		int currentYear,
		bool firstEstablishment,
		int previousVacantSinceYear)
	{
		if (firstEstablishment)
			XjFuQiSwordWorldState.RollbackEstablishment(actorId, currentYear);
		else
			XjFuQiSwordWorldState.RollbackClaim(actorId, currentYear, previousVacantSinceYear);
	}

	private static void PublishLongGengEstablishment(Actor actor)
	{
		string actorName = actor?.getName() ?? "无名羽士";
		string historyText = "【天地认位·长庚初名】此前天下剑意虽盛，却从未有一位真正属于剑。"
			+ actorName + "将神妙〖养青冥〗修至圆满，性命、剑意与金性浑然为一，以此求证真君羽士。"
			+ "一息之间，四海剑器无风自鸣，天下剑意尽映其锋。天地照见其性命之果，承认此道足以自立一位。"
			+ "无名果位遂落于其身，其登真君羽士，并为此位命名——长庚。自此无名剑道始有其名，天下万剑亦有大道归处。";
		string tipText = "【天地认位·长庚初名】\n" + actorName
			+ "以圆满神妙求证真君羽士，天地认可其果。\n无名果位由此落世，其命之曰长庚！";
		XjBroadcastSystem.BroadcastSLevelActorEvent(actor, historyText, tipText, "#D9E8FF", 15f, XjEventIconCatalog.JinDanUpgrade);
	}

	private static void PublishLongGengSuccession(Actor actor)
	{
		string actorName = actor?.getName() ?? "无名羽士";
		string historyText = "【天地承位·长庚有主】长庚果位空悬既久，"
			+ actorName + "将〖养青冥〗温养至圆满，以性命、剑意与金性重新叩问天地。"
			+ "天地认可其果，果位遂由空悬归于有主；其承长庚旧名而登真君羽士，为后世持位者。";
		string tipText = "【天地承位·长庚有主】\n" + actorName
			+ "求证得天地认可，继任长庚果位！";
		XjBroadcastSystem.BroadcastSLevelActorEvent(
			actor,
			historyText,
			tipText,
			"#D9E8FF",
			15f,
			XjEventIconCatalog.JinDanUpgrade);
	}
}
