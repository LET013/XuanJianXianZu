using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildGuoWeiZhongAiSummary(in XjGuoWeiQuanBingState state)
		{
			return state.Found && !string.IsNullOrWhiteSpace(state.GuoWeiZhongAi)
				? "已得"
				: string.Empty;
		}

		private static string BuildReincarnationSummary(Actor actor)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int applied)
				|| applied <= 0)
			{
				return string.Empty;
			}
	
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationMode, out string mode);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationGuoWeiZhongAi, out string favored);
			bool isFavored = string.Equals(mode, "GuoWeiZhongAi", StringComparison.Ordinal)
				|| string.Equals(mode, "guowei_zhongai", StringComparison.Ordinal)
				|| !string.IsNullOrWhiteSpace(favored);
			if (isFavored)
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationDaoTu, out string reincarnationDaoTu);
				return string.IsNullOrWhiteSpace(reincarnationDaoTu)
					? "果位钟爱转世"
					: reincarnationDaoTu.Trim() + "果位钟爱转世";
			}
	
			if (string.Equals(mode, "ZiFuJinXing", StringComparison.Ordinal)
				|| string.Equals(mode, "zifu_jindan_jinxing", StringComparison.Ordinal))
			{
				return "真人转世";
			}
	
			return "真君转世";
		}

		private static string BuildFormerLifeSummary(Actor actor)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int applied)
				|| applied <= 0)
			{
				return string.Empty;
			}
	
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationSourceName, out string sourceActorName);
			if (string.IsNullOrWhiteSpace(sourceActorName))
			{
				return "前世金丹修士";
			}
	
			string text = sourceActorName.Trim();
			int realmSuffix = text.IndexOf("-金丹", StringComparison.Ordinal);
			if (realmSuffix > 0)
			{
				text = text.Substring(0, realmSuffix).Trim();
			}
			else
			{
				int separator = text.IndexOf('-');
				if (separator > 0)
				{
					text = text.Substring(0, separator).Trim();
				}
			}
	
			return string.IsNullOrWhiteSpace(text) ? "前世金丹修士" : text;
		}

		private static string BuildJinDanSummary(Actor actor, in XjJinDanState state, in XjShenDanState shenDanState, string daoTu)
		{
			if (shenDanState.Found)
			{
				string guoWei = SanitizeJinDanTerm(XjGuoWeiCalculator.GetDisplayGuoWeiName(shenDanState.GuoWei), daoTu);
				string anchor = string.IsNullOrWhiteSpace(shenDanState.AnchorName) ? "未明金丹" : shenDanState.AnchorName.Trim();
				string year = shenDanState.Year > 0 ? " - " + shenDanState.Year.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
				return "无金性 - " + guoWei + " - 挂靠金丹-" + anchor + year;
			}
	
			if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
			{
				string jinXing = SanitizeJinDanTerm(state.JinXing, daoTu);
				string year = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJieLinXianYear, out int jieLinYear) && jieLinYear > 0
					? " - " + jieLinYear.ToString(CultureInfo.InvariantCulture) + "年"
					: string.Empty;
				return (string.IsNullOrWhiteSpace(jinXing) ? "金性未定" : jinXing) + " - 结璘仙" + year;
			}
	
			if (state.Found)
			{
				string year = state.SuccessYear > 0 ? " - " + state.SuccessYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
				string jinXing = SanitizeJinDanTerm(state.JinXing, daoTu);
				string guoWei = SanitizeJinDanTerm(XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei), daoTu);
				if (string.IsNullOrWhiteSpace(jinXing) && string.IsNullOrWhiteSpace(guoWei))
				{
					return "暂无金丹";
				}
	
				return jinXing + " - " + guoWei + year;
			}
	
			if (XjJinDanResidualJinXing.TryGetValidGrant(actor, out string residualJinXing))
			{
				return SanitizeJinDanTerm(residualJinXing, daoTu);
			}
	
			return string.IsNullOrWhiteSpace(state.FailedState)
				? "暂无金丹"
				: "金丹未成（" + state.FailedState.Trim() + "）";
		}

		private static string SanitizeJinDanTerm(string value, string daoTu)
		{
			string text = Normalize(value);
			if (string.IsNullOrWhiteSpace(text))
			{
				return string.Empty;
			}
	
			if (text.Contains("玄门"))
			{
				string replacement = IsInvalidDaoTuAlias(daoTu) ? string.Empty : Normalize(daoTu);
				if (string.IsNullOrWhiteSpace(replacement))
				{
					return string.Empty;
				}
	
				text = text.Replace("玄门", replacement);
			}
	
			return text;
		}

		private static string BuildJinDanDaoSpellSummary(Actor actor, string daoTu, in XjJinDanState state)
		{
			if (!state.Found)
			{
				return "暂无金丹法术";
			}
	
			System.Collections.Generic.List<XjJinDanDaoSpellDefinition> definitions =
				new System.Collections.Generic.List<XjJinDanDaoSpellDefinition>(3);
			if (XjJinDanDaoSpellCatalog.CollectForDaoTu(daoTu, definitions) == 0)
			{
				return "暂无金丹法术";
			}
	
			System.Collections.Generic.List<string> spells = new System.Collections.Generic.List<string>(definitions.Count);
			for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
			{
				XjJinDanDaoSpellDefinition definition = definitions[definitionIndex];
				if (!string.IsNullOrWhiteSpace(definition.DisplayName))
				{
					spells.Add(definition.DisplayName.Trim());
				}
			}
	
			return string.Join("；", spells);
		}

		private static string BuildJinDanSpellRuntimeState(Actor actor, in XjJinDanDaoSpellDefinition definition)
		{
			if (actor?.data == null || string.IsNullOrWhiteSpace(definition.Id)) return string.Empty;
			long actorId = ((BaseSystemData)actor.data).id;
	
			if (string.Equals(definition.Id, "ZhuMingFenTianQue", StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFireBirdActiveUntilYear, out int activeUntilYear);
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFireBirdReadyYear, out int readyYear);
				int currentYear = 0;
				try { currentYear = World.world?.map_stats?.year ?? 0; } catch { }
				if (currentYear > 0 && activeUntilYear > currentYear) return "当前存续至" + activeUntilYear + "年";
				if (currentYear > 0 && readyYear > currentYear) return "冷却至" + readyYear + "年";
				return "当前可施展";
			}
	
			if (string.Equals(definition.Id, "SanYinBaoYiXuanLun", StringComparison.Ordinal))
			{
				float active = XjSanYinBaoYiXuanLunSystem.GetRemainingDurationSeconds(actor);
				if (active > 0.05f) return "增益剩余" + active.ToString("0.#", CultureInfo.InvariantCulture) + "秒";
			}
	
			float cooldown = XjJinDanDaoSpellCooldown.GetRemainingSeconds(actorId, definition.Id, UnityEngine.Time.time);
			return cooldown > 0.05f
				? "冷却剩余" + cooldown.ToString("0.#", CultureInfo.InvariantCulture) + "秒"
				: "当前可施展";
		}

		private static string BuildTitleSummary(Actor actor)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string title);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string realmDisplay);
			if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(realmDisplay))
			{
				return "暂无尊号";
			}
	
			if (string.IsNullOrWhiteSpace(title))
			{
				return realmDisplay.Trim();
			}
	
			if (string.IsNullOrWhiteSpace(realmDisplay))
			{
				return title.Trim();
			}
	
			return title.Trim() + " - " + realmDisplay.Trim();
		}
}

