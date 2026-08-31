using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 无名剑道Handler只负责长庚特有的128剑气、16道剑意与〖养青冥〗入门。
/// 黄冠以后全部交由XjFuQiCultivationSystem处理。
/// </summary>
internal static class XjFuQiSwordDaoSystem
{
	internal const int SwordQiTarget = 128;
	internal const int StudiedIntentTarget = 16;
	internal const string YangQingMingId = "yang_qing_ming";
	private const char IntentIdSeparator = ';';

	internal static void TickEntryActor(Actor actor, int currentYear)
	{
		if (!IsSwordCandidate(actor) || currentYear <= 0) return;
		XjLongGengSwordSteleSystem.TickActor(actor, currentYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId))) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordLastAnnualYear, out int lastYear)
			&& lastYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordLastAnnualYear, currentYear);
		XjWeaponArtSystem.EnsureFuQiSwordIdentity(actor, currentYear);
		AdvanceSwordQi(actor, currentYear);
		HashSet<string> studied = ReadStudiedIntentIds(actor);
		AdvanceIntentStudy(actor, currentYear, studied);
		TryCompleteYangQingMing(actor, currentYear, studied);
	}

	internal static bool TryComprehendFromSwordStele(Actor actor, int currentYear, out string intentName)
	{
		intentName = string.Empty;
		if (!IsSwordCandidate(actor) || currentYear <= 0) return false;
		HashSet<string> studied = ReadStudiedIntentIds(actor);
		if (studied.Count >= StudiedIntentTarget
			|| !XjSwordIntentRegistry.TrySelectUnstudied(actor, studied, out XjSwordIntentArchiveRecord selected)
			|| selected == null || string.IsNullOrWhiteSpace(selected.IntentId))
		{
			return false;
		}
		studied.Add(selected.IntentId);
		WriteStudiedIntentIds(actor, studied);
		ClearCurrentStudy(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordQi, out int swordQi);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, Math.Min(SwordQiTarget, Math.Max(0, swordQi) + 16));
		intentName = string.IsNullOrWhiteSpace(selected.IntentName) ? selected.IntentId : selected.IntentName;
		XjThreeBookWriter.RecordFuQiIntentStudy(actor, selected, Math.Min(studied.Count, StudiedIntentTarget), currentYear);
		TryCompleteYangQingMing(actor, currentYear, studied);
		return true;
	}

	internal static string BuildEntryDisplaySummary(Actor actor)
	{
		if (!IsSwordCandidate(actor)) return string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!string.IsNullOrWhiteSpace(XjRealmHelper.NormalizeId(realmId))) return string.Empty;
		int currentYear = XjAnnualExecutionContext.ResolveYear(actor);
		StringBuilder builder = new StringBuilder(220);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordQi, out int swordQi);
		HashSet<string> studied = ReadStudiedIntentIds(actor);
		builder.Append("剑气：").Append(Math.Clamp(swordQi, 0, SwordQiTarget)).Append('/').AppendLine(SwordQiTarget.ToString(CultureInfo.InvariantCulture));
		builder.Append("参悟剑意：").Append(Math.Min(StudiedIntentTarget, studied.Count)).Append('/').AppendLine(StudiedIntentTarget.ToString(CultureInfo.InvariantCulture));
		if (TryReadCurrentStudy(actor, out XjSwordIntentArchiveRecord record, out int intentCompleteYear))
		{
			int remaining = Math.Max(0, intentCompleteYear - currentYear);
			bool alive = XjSwordIntentRegistry.IsCreatorAlive(record);
			builder.Append("当前修行：").Append(alive ? "拜访" : "寻访")
				.Append(string.IsNullOrWhiteSpace(record.CreatorName) ? "前人" : record.CreatorName)
				.Append(alive ? "，观其剑意《" : "旧迹，揣摩《").Append(record.IntentName).Append(alive ? "》" : "》遗意");
			if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
			builder.AppendLine();
		}
		else if (studied.Count < StudiedIntentTarget)
		{
			builder.AppendLine(XjSwordIntentRegistry.Count <= studied.Count + 1
				? "当前修行：天下可寻之剑意尚少，暂候后来之人。"
				: "当前修行：正在寻访下一位可供观剑的剑修。");
		}
		return builder.ToString().TrimEnd();
	}

	internal static bool IsSwordCandidate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			|| !string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal)) return false;
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			&& XjFuQiAptitudeRules.CanReachHuangGuan(aptitude);
	}

	private static void AdvanceSwordQi(Actor actor, int currentYear)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordQi, out int current);
		if (current >= SwordQiTarget) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		int gain = aptitude switch { 4 => 3, 5 => 4, 6 => 4, _ => 1 };
		if (huiGuang >= 90f) gain++;
		long actorId = ((BaseSystemData)actor.data).id;
		if (huiGuang >= 45f && XjDeterministicHash.PositiveIndex(actorId + currentYear, "fuqi_sword_qi_extra_v1", 4) == 0) gain++;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, Math.Min(SwordQiTarget, current + Math.Clamp(gain, 1, 5)));
	}

	private static void AdvanceIntentStudy(Actor actor, int currentYear, HashSet<string> studied)
	{
		if (studied.Count >= StudiedIntentTarget) return;
		if (TryReadCurrentStudy(actor, out XjSwordIntentArchiveRecord current, out int completeYear))
		{
			if (currentYear < completeYear) return;
			studied.Add(current.IntentId);
			WriteStudiedIntentIds(actor, studied);
			ClearCurrentStudy(actor);
			if (studied.Count % 4 == 0 || studied.Count >= StudiedIntentTarget)
			{
				XjThreeBookWriter.RecordFuQiIntentStudy(actor, current, Math.Min(studied.Count, StudiedIntentTarget), currentYear);
			}
		}
		else ClearCurrentStudy(actor);
		if (studied.Count >= StudiedIntentTarget) return;
		if (!XjSwordIntentRegistry.TrySelectUnstudied(actor, studied, out XjSwordIntentArchiveRecord selected)) return;
		int duration = ResolveIntentStudyYears(actor, studied.Count, selected.IntentId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCurrentIntentId, selected.IntentId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyStartYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyCompleteYear, currentYear + duration);
	}

	private static int ResolveIntentStudyYears(Actor actor, int studiedCount, string intentId)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		int baseYears = aptitude >= 5 || huiGuang >= 90f ? 1 : 2;
		long actorId = ((BaseSystemData)actor.data).id;
		int variance = XjDeterministicHash.PositiveIndex(actorId + studiedCount, "fuqi_intent_study_years|" + (intentId ?? string.Empty), 3) - 1;
		return Math.Clamp(baseYears + variance, 1, 3);
	}

	private static void TryCompleteYangQingMing(Actor actor, int currentYear, HashSet<string> studied)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordQi, out int swordQi);
		if (swordQi < SwordQiTarget || studied.Count < StudiedIntentTarget) return;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiShenMiaoId, out string existing)
			&& string.Equals(existing, YangQingMingId, StringComparison.Ordinal)) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, YangQingMingId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiYangQingMingCompletedYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 10000);
		if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.HuangGuan, true, true))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiYangQingMingCompletedYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
			return;
		}
		XjWeaponArtSystem.CompleteFuQiSwordIntent(actor, currentYear);
		XjThreeBookWriter.RecordFuQiHuangGuan(actor, currentYear);
	}

	private static bool TryReadCurrentStudy(Actor actor, out XjSwordIntentArchiveRecord record, out int completeYear)
	{
		record = null;
		completeYear = 0;
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiCurrentIntentId, out string currentId)
			&& !string.IsNullOrWhiteSpace(currentId)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiIntentStudyCompleteYear, out completeYear)
			&& completeYear > 0 && XjSwordIntentRegistry.TryGet(currentId, out record);
	}

	private static HashSet<string> ReadStudiedIntentIds(Actor actor)
	{
		HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
		if (actor?.data == null || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiStudiedIntentIds, out string raw)
			|| string.IsNullOrWhiteSpace(raw)) return result;
		string[] parts = raw.Split(new[] { IntentIdSeparator }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length && result.Count < StudiedIntentTarget; i++)
		{
			string id = parts[i].Trim();
			if (id.Length > 0) result.Add(id);
		}
		return result;
	}

	private static void WriteStudiedIntentIds(Actor actor, HashSet<string> studied)
	{
		if (actor?.data == null || studied == null) return;
		List<string> ids = new List<string>(studied);
		ids.Sort(StringComparer.Ordinal);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiStudiedIntentIds, string.Join(IntentIdSeparator.ToString(), ids));
	}

	private static void ClearCurrentStudy(Actor actor)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCurrentIntentId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiIntentStudyCompleteYear, 0);
	}
}
