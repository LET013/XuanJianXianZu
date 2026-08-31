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
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildGuoWeiZhongAiSummary(in XjGuoWeiQuanBingState state)
		{
			return state.Found && !string.IsNullOrWhiteSpace(state.GuoWeiZhongAi)
				? "果位钟爱"
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
	
			if (string.Equals(mode, "FuQiJinXing", StringComparison.Ordinal))
			{
				string realmId = XjRealmHelper.NormalizeId(
					XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
				if (XjCultivationPathRules.IsFuQiYangXing(actor)
					&& string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
				{
					XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiTrueSpirit, out int trueSpirit);
					XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent, out int bonus);
					return "金性转世 · 真灵值" + Math.Clamp(trueSpirit, 0, 3) + " · 上限3"
						+ (bonus > 0 ? " · 求证余荫" + DescribeReincarnationAid(bonus) : string.Empty);
				}
				return "金性转世";
			}

			if (string.Equals(mode, "ZiFuJinXing", StringComparison.Ordinal)
				|| string.Equals(mode, "zifu_jindan_jinxing", StringComparison.Ordinal)
				|| string.Equals(mode, "FamilyBorrowJinXing", StringComparison.Ordinal)
				|| string.Equals(mode, "family_borrow_jinxing", StringComparison.Ordinal))
			{
				return "真人转世";
			}
	
			return "真君转世";
		}

		private static string DescribeReincarnationAid(int bonus)
		{
			int value = Math.Max(0, bonus);
			if (value <= 0) return "";
			if (value <= 3) return "微薄";
			if (value <= 6) return "渐显";
			if (value <= 10) return "显著";
			return "深厚";
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

		private static string BuildXianGuoStatusSummary(in XjXianGuoSummary xianGuo)
		{
			string text = "仙国法　" + xianGuo.DynastyName
				+ "\n国势：" + xianGuo.NationalPotential.ToString(CultureInfo.InvariantCulture)
				+ "\n国运：" + xianGuo.NationalFortune.ToString(CultureInfo.InvariantCulture)
				+ "\n城土：" + xianGuo.CityCount.ToString(CultureInfo.InvariantCulture) + "城 / "
				+ xianGuo.Population.ToString(CultureInfo.InvariantCulture) + "众"
				+ "\n百官借玄：" + (xianGuo.CourtFakeJinDanActive
					? "假金丹命额已开（品秩" + xianGuo.CourtBorrowedCombatGrade.ToString(CultureInfo.InvariantCulture) + "）"
					: "假金丹命额未开")
				+ "\n帝明阳：天光" + xianGuo.TianGuang.ToString(CultureInfo.InvariantCulture)
				+ "　紫焰" + xianGuo.ZiYan.ToString(CultureInfo.InvariantCulture)
				+ "　君臣" + xianGuo.JunChen.ToString(CultureInfo.InvariantCulture)
				+ "　帝皇" + xianGuo.DiHuang.ToString(CultureInfo.InvariantCulture);
			if (xianGuo.FuZiXiangSha > 0 || xianGuo.MouNi > 0)
			{
				text += "\n政象：父子相杀" + xianGuo.FuZiXiangSha.ToString(CultureInfo.InvariantCulture)
					+ "　谋逆" + xianGuo.MouNi.ToString(CultureInfo.InvariantCulture);
			}
			return text;
		}

		private static string BuildJinDanSummary(Actor actor, in XjJinDanState state, in XjShenDanState shenDanState, string daoTu)
		{
			// 帝明阳本人只显示真实境界；这里的仙国法摘要只展示国朝制度状态。
			// 百官的假金丹属于朝廷命额，不再把帝明阳本人标记成“仙国假金丹”。
			if (!state.Found
				&& XjXianGuoSystem.TryGetActiveSummary(actor, out XjXianGuoSummary xianGuo))
			{
				return BuildXianGuoStatusSummary(in xianGuo);
			}
			if (shenDanState.Found)
			{
				string anchor = string.IsNullOrWhiteSpace(shenDanState.AnchorName) ? "未明真君" : shenDanState.AnchorName.Trim();
				string year = shenDanState.Year > 0 ? " - " + XjChronology.FormatYear(shenDanState.Year) : string.Empty;
				// XjShenDanGuoWei 只是内部保存的“锚点位序标识”，绝不是神丹自己的果位。
				return "挂靠-" + anchor + year;
			}
	
			if (XjXuanJianShenTongSpecials.IsYuYiXian(actor))
			{
				string jinXing = SanitizeJinDanTerm(state.JinXing, daoTu);
				string year = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYuYiXianYear, out int yuYiYear) && yuYiYear > 0
					? " - " + XjChronology.FormatYear(yuYiYear)
					: string.Empty;
				string position = state.Found
					? " - " + SanitizeJinDanTerm(XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei), daoTu)
					: string.Empty;
				return (string.IsNullOrWhiteSpace(jinXing) ? "金性未定" : jinXing) + " - 郁仪仙" + position + year;
			}

			if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
			{
				string jinXing = SanitizeJinDanTerm(state.JinXing, daoTu);
				string year = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJieLinXianYear, out int jieLinYear) && jieLinYear > 0
					? " - " + XjChronology.FormatYear(jieLinYear)
					: string.Empty;
				string position = state.Found
					? " - " + SanitizeJinDanTerm(XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei), daoTu)
					: string.Empty;
				return (string.IsNullOrWhiteSpace(jinXing) ? "金性未定" : jinXing) + " - 结璘仙" + position + year;
			}
	
			if (state.Found)
			{
				string year = state.SuccessYear > 0 ? " - " + XjChronology.FormatYear(state.SuccessYear) : string.Empty;
				string jinXing = SanitizeJinDanTerm(state.JinXing, daoTu);
				string guoWei = SanitizeJinDanTerm(XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei), daoTu);
				if (string.IsNullOrWhiteSpace(jinXing) && string.IsNullOrWhiteSpace(guoWei))
				{
					return "暂无金丹";
				}
	
				string result = jinXing + " - " + guoWei + year;
				if (XjXianGuoSystem.TryGetActiveSummary(actor, out XjXianGuoSummary trueJinDanXianGuo))
				{
					result += "\n" + BuildXianGuoStatusSummary(in trueJinDanXianGuo);
				}
				return result;
			}
	
			if (XjJinDanResidualJinXing.TryGetValidGrant(actor, out string residualJinXing))
			{
				return SanitizeJinDanTerm(residualJinXing, daoTu);
			}
	
			string failureDisplay = XjDisplayNameSanitizer.JinDanFailureState(state.FailedState);
			return string.IsNullOrWhiteSpace(failureDisplay)
				? "暂无金丹"
				: "金丹未成（" + failureDisplay + "）";
		}

		private static XjJinDanState BuildDaoTaiDisplayJinDanState(Actor actor, in XjJinDanState state, bool isDaoTaiRealm)
		{
			if (state.Found || !isDaoTaiRealm || actor?.data == null)
			{
				return state;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, out int lastAttemptYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L && XjGuoWeiRegistry.TryGetHistoricalEntry(actorId, out XjGuoWeiRegistryEntry entry) && entry.Found)
			{
				if (string.IsNullOrWhiteSpace(jinXing)) jinXing = entry.JinXing;
				if (string.IsNullOrWhiteSpace(guoWei)) guoWei = entry.GuoWei;
				if (successYear <= 0) successYear = entry.Year;
			}
			if (successYear <= 0
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmEnteredYear)
				&& realmEnteredYear > 0)
			{
				successYear = realmEnteredYear;
			}

			jinXing = XjJinXingNamePolicy.NormalizeLegacyName(jinXing);
			guoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			bool found = !string.IsNullOrWhiteSpace(jinXing)
				&& !string.IsNullOrWhiteSpace(guoWei)
				&& successYear > 0;
			return new XjJinDanState(
				found,
				jinXing,
				guoWei,
				failedState,
				lastAttemptYear,
				successYear,
				found ? "DaoTaiDisplay" : state.ReasonCode);
		}

		private static string BuildDaoTaiBindingSummary(long actorId)
		{
			if (actorId <= 0L
				|| !XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
				|| binding == null)
			{
				return string.Empty;
			}

			string title = XjDaoTaiDualPositionSystem.GetBindingTitle(binding);
			string pair = string.Empty;
			if (XjDaoTaiDualPositionSystem.TryResolveBindingPair(binding,
				out XjDerivedPositionArchiveRecord fruit, out XjDerivedPositionArchiveRecord derived))
			{
				pair = XjGuoWeiCalculator.GetDisplayGuoWeiName(fruit.PositionId)
					+ " ＋ " + XjGuoWeiCalculator.GetDisplayGuoWeiName(derived.PositionId);
			}
			string year = binding.BoundYear > 0
				? XjChronology.FormatYear(binding.BoundYear)
				: string.Empty;
			return title
				+ (string.IsNullOrWhiteSpace(pair) ? string.Empty : "\n" + pair)
				+ (string.IsNullOrWhiteSpace(year) ? string.Empty : "\n" + year);
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

		private static string BuildJinDanDaoSpellSummary(Actor actor, string daoTu, string realmId, in XjJinDanState state)
		{
			if (string.Equals(ResolveMechanicsDaoTu(daoTu), "长庚", StringComparison.Ordinal))
			{
				return "无主动法术；普攻以飞剑显化";
			}
			if (!state.Found
				&& !XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)
				&& !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.DaoTai, StringComparison.Ordinal)
				&& !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
				&& !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal))
			{
				return "暂无真君法术";
			}
	
			System.Collections.Generic.List<XjJinDanDaoSpellDefinition> definitions =
				new System.Collections.Generic.List<XjJinDanDaoSpellDefinition>(3);
			if (XjJinDanDaoSpellCatalog.CollectForDaoTu(ResolveMechanicsDaoTu(daoTu), definitions) == 0)
			{
				return "暂无真君法术";
			}
	
			bool hasExactDirect = false;
			for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
			{
				XjJinDanDaoSpellDefinition definition = definitions[definitionIndex];
				if (definition.RequiredShenTongIds != null
					&& definition.RequiredShenTongIds.Count > 0
					&& ActorMeetsSpellRequirement(actor, definition))
				{
					hasExactDirect = true;
					break;
				}
			}
			bool hasNamedDomain = XjDomainSkillCatalog.TryResolve(actor, out _);

			System.Collections.Generic.List<string> spells = new System.Collections.Generic.List<string>(definitions.Count);
			for (int definitionIndex = 0; definitionIndex < definitions.Count; definitionIndex++)
			{
				XjJinDanDaoSpellDefinition definition = definitions[definitionIndex];
				if ((hasExactDirect || hasNamedDomain)
					&& (definition.RequiredShenTongIds == null || definition.RequiredShenTongIds.Count == 0)) continue;
				if (!ActorMeetsSpellRequirement(actor, definition)) continue;
				if (!string.IsNullOrWhiteSpace(definition.DisplayName))
				{
					spells.Add(definition.DisplayName.Trim());
				}
			}
	
			return string.Join("；", spells);
		}

		private static bool ActorMeetsSpellRequirement(Actor actor, in XjJinDanDaoSpellDefinition definition)
		{
			IReadOnlyList<string> required = definition.RequiredShenTongIds;
			if (required == null || required.Count == 0) return true;
			if (actor?.data == null) return false;
			string[] learned = XjXianJiAccessor.ReadRawIds(actor);
			for (int i = 0; i < learned.Length; i++)
			{
				string learnedId = (learned[i] ?? string.Empty).Trim();
				for (int j = 0; j < required.Count; j++)
				{
					if (string.Equals(learnedId, (required[j] ?? string.Empty).Trim(), StringComparison.Ordinal)) return true;
				}
			}
			return false;
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
