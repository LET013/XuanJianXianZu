using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 金丹、真君成位后的尊修事：果位行“嬗”、余位行“移”、闰位行“变”。
/// 仅走年度/闭关稀疏队列，不增加逐帧扫描。
/// </summary>
internal static class XjRespectPracticeSystem
{
	private const int CompletionProgress = 1000;

	internal static void TickAnnual(Actor actor, int currentYear, bool retreat)
	{
		if (actor?.data == null || currentYear <= 0) return;
		XjHighRealmDoctrineSnapshot snapshot = XjHighRealmDaoStateService.BuildSnapshot(actor);
		if (!snapshot.Found) return;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjRespectPracticeStage, out string completedStage)
			&& string.Equals(completedStage?.Trim(), "已成", StringComparison.Ordinal)) return;

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjRespectPracticeType, out string practiceType)
			|| string.IsNullOrWhiteSpace(practiceType))
		{
			if (!TryStart(actor, snapshot, currentYear, out practiceType)) return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, out int progress);
		int gain = retreat ? 24 : 8;
		gain += Math.Min(12, Math.Max(0, snapshot.PositionImage / 1000));
		if (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float daoHui))
			gain += Math.Min(10, Math.Max(0, (int)daoHui / 12));
		progress = Math.Min(CompletionProgress, Math.Max(0, progress) + gain);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, progress);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeStage,
			progress < 350 ? "积行" : progress < 700 ? "合位" : "定玄");
		if (progress < CompletionProgress) return;

		Complete(actor, snapshot, practiceType.Trim(), currentYear);
	}

	private static bool TryStart(Actor actor, in XjHighRealmDoctrineSnapshot snapshot, int currentYear, out string practiceType)
	{
		practiceType = string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		string type = XjGuoWeiCalculator.NormalizePositionType(snapshot.PositionType);
		if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			if (snapshot.PositionImage < 2800) return false;
			XjDaoLineageArchiveRecord lineage = XjDaoLineageStateRegistry.GetOrCreate(snapshot.ManifestDaoTu);
			if (lineage == null || lineage.Vitality > 60) return false;
			practiceType = "嬗";
		}
		else if (string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			bool designatedSuccessor = XjUpperCultivatorGoldSupportSystem.IsDesignatedSuccessor(actor, snapshot.ManifestDaoTu);
			if ((!designatedSuccessor && snapshot.PositionImage < 2400)
				|| !HasOpenFruit(snapshot.ManifestDaoTu, actorId, currentYear)) return false;
			practiceType = "移";
		}
		else if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			if (snapshot.PositionImage < 3200 || !HasOpenFruit(snapshot.ManifestDaoTu, actorId, currentYear)) return false;
			practiceType = "变";
		}
		else return false;

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeType, practiceType);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeStage, "积行");
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeTargetDaoTu, snapshot.ManifestDaoTu);
		bool designatedImmediateSuccession = string.Equals(practiceType, "移", StringComparison.Ordinal)
			&& XjUpperCultivatorGoldSupportSystem.IsDesignatedSuccessor(actor, snapshot.ManifestDaoTu);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, designatedImmediateSuccession ? CompletionProgress : 0);
		string text = "【尊修事·" + practiceType + "】" + (actor.getName() ?? "无名真君")
			+ "以" + snapshot.ManifestDaoTu + "为图，开始积行合位，求成道后之尊修。";
		XjWorldHistoryRegistry.AddActorEvent(actor, text, XjEventIconCatalog.JinDanUpgrade);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
			text, XjAnnouncementCategory.HighRealm, duration: 7f, color: "#E4BE72", iconId: XjEventIconCatalog.JinDanUpgrade);
		return true;
	}

	private static void Complete(Actor actor, in XjHighRealmDoctrineSnapshot snapshot, string practiceType, int currentYear)
	{
		if (string.Equals(practiceType, "嬗", StringComparison.Ordinal))
		{
			CompleteShan(actor, snapshot, currentYear);
			return;
		}
		if (!string.Equals(practiceType, "移", StringComparison.Ordinal)
			&& !string.Equals(practiceType, "变", StringComparison.Ordinal)) return;
		CompleteSuccession(actor, snapshot, practiceType, currentYear);
	}

	private static void CompleteShan(Actor actor, in XjHighRealmDoctrineSnapshot snapshot, int currentYear)
	{
		string direction = "更新";
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjLineageRevivalDirection, out string saved)
			&& !string.IsNullOrWhiteSpace(saved)) direction = saved.Trim();
		XjDaoLineageStateRegistry.CompleteRevival(actor, snapshot.ManifestDaoTu, direction, currentYear);
		AppendJinXingHistory(actor, currentYear, "嬗正受·" + direction);
		string message = "【正受易位】" + (actor.getName() ?? "无名真君") + "行嬗有成，重定【"
			+ snapshot.ManifestDaoTu + "】主象，道统核心自此改易。";
		Finish(actor, message);
	}

	private static void CompleteSuccession(Actor actor, in XjHighRealmDoctrineSnapshot snapshot, string practiceType, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjGuoWeiRegistry.TryResolveAvailableGuoWei(snapshot.ManifestDaoTu, XjGuoWeiCalculator.ZhengWei,
			actorId, actorId + currentYear, false, out _, out string fruitGuoWei))
		{
			// 主位再度有主，保留积行但退回合位，等待下一次年度判断。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, 700);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeStage, "合位待主");
			return;
		}

		XjJinDanState oldState = XjJinDanAccessor.BuildState(actor);
		if (!oldState.Found) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
		string newJinXing = XjJinXingCalculator.Calculate(snapshot.ManifestDaoTu, actorId);
		if (string.IsNullOrWhiteSpace(newJinXing)) newJinXing = oldState.JinXing;

		XjGuoWeiRegistry.ReleaseForActor(actorId, oldState.GuoWei);
		if (!XjGuoWeiRegistry.TryClaim(actor, snapshot.ManifestDaoTu, newJinXing, fruitGuoWei, currentYear, string.Empty))
		{
			XjGuoWeiRegistry.TryClaim(actor, snapshot.ManifestDaoTu, oldState.JinXing, oldState.GuoWei,
				oldState.SuccessYear > 0 ? oldState.SuccessYear : currentYear,
				string.Equals(snapshot.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? sourceDaoTu : string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, 700);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeStage, "合位待主");
			return;
		}
		XjDaoLineageStateRegistry.OnHolderReleased(
			actorId, snapshot.ManifestDaoTu, oldState.GuoWei,
			string.Empty, currentYear, penalizeVitality: false);

		XjJinDanAccessor.WriteSuccess(actor, newJinXing, fruitGuoWei, currentYear);
		XjHighRealmDaoStateService.ApplyRespectPositionChange(
			actor, XjGuoWeiCalculator.ZhengWei, fruitGuoWei, newJinXing, currentYear, practiceType);
		XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
		XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, snapshot.ManifestDaoTu, fruitGuoWei, currentYear);
		string message = string.Equals(practiceType, "移", StringComparison.Ordinal)
			? "【以裨继主】" + (actor.getName() ?? "无名真君") + "持偏裨而承正受，今日由余迁果，继掌【" + snapshot.ManifestDaoTu + "】。"
			: "【玄置夺君】" + (actor.getName() ?? "无名真君") + "积年" + snapshot.RunFormula + "，今日闰以变正，成为【" + snapshot.ManifestDaoTu + "】主君。";
		if (string.Equals(practiceType, "移", StringComparison.Ordinal))
			XjUpperCultivatorGoldSupportSystem.OnDesignatedSuccessionCompleted(actor);
		Finish(actor, message);
	}

	private static bool HasOpenFruit(string daoTu, long actorId, int currentYear)
	{
		return XjGuoWeiRegistry.TryResolveAvailableGuoWei(daoTu, XjGuoWeiCalculator.ZhengWei,
			actorId, actorId + currentYear, false, out _, out _);
	}

	private static void Finish(Actor actor, string message)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjRespectPracticeStage, "已成");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, CompletionProgress);
		XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanUpgrade);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTip(message, XjAnnouncementCategory.HighRealm, iconId: XjEventIconCatalog.JinDanUpgrade);
	}

	private static void AppendJinXingHistory(Actor actor, int year, string eventName)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string history);
		string item = XjChronology.FormatYear(Math.Max(1, year)) + "·" + (eventName ?? string.Empty).Trim();
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingHistory,
			string.IsNullOrWhiteSpace(history) ? item : history.Trim() + "；" + item);
	}
}
