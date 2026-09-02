using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.UI.ActorInfo;

internal static class XjActorInfoDisplayFormatter
{
	private const string TitleColor = "#FFD37A";
	private const string SectionColor = "#B9EEE4";
	private const string LabelColor = "#D8CDAA";
	private const string ValueColor = "#E6EDF2";
	private const string AccentColor = "#9CD7FF";
	private const string GoodColor = "#A7E08A";
	private const string WarnColor = "#FFD37A";
	private const string DangerColor = "#FF8877";
	private const string PurpleColor = "#B7A7FF";

	internal static string Format(Actor actor)
	{
		string formatted;
		if (XjCultivationPathRules.IsShi(actor))
		{
			formatted = XjShiDisplayFormatter.Format(actor);
		}
		else
		{
			string bottleneck = XjBottleneckEventSystem.BuildStatusSummary(actor);
			bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
			bool isZiJin = XjCultivationPathRules.IsZiFuJinDan(actor);
			string fuQi = XjFuQiCoreRouter.BuildDisplaySummary(actor);
			XjActorInfoReadModel model = XjActorInfoReadModel.BuildForActor(actor);
			string daoTuDisplayOverride = XjXianGuoSystem.ResolveDaoTuDisplay(actor,
				XjYaoShuHalfBloodlineSystem.ResolveDisplayedDaoTu(actor, model.DaoTu));
			formatted = Format(model, bottleneck, isFuQi, isZiJin, fuQi, daoTuDisplayOverride);
		}
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		XjActorRevisionToken revisionToken = XjActorStateRevisionStore.GetToken(actorId);
		XjActorCraftReadModel craft = XjActorSupplementReadModelStore.GetCraft(actor, actorId, in revisionToken);
		string weaponArt = craft.WeaponArt;
		string ziJinSword = craft.ZiJinSword;
		string swordCombat = craft.SwordCombat;
		string alchemy = craft.Alchemy;
		string artifact = craft.Artifact;
		string talisman = craft.Talisman;
		string formation = craft.Formation;
		XjGuZunRegistry.TryGetManifestationSummary(actor, out string guZunSummary);
		string xianGuoSummary = XjXianGuoSystem.BuildActorStatusSummary(actor);
		if (string.IsNullOrWhiteSpace(xianGuoSummary)
			&& string.IsNullOrWhiteSpace(guZunSummary)
			&& string.IsNullOrWhiteSpace(weaponArt)
			&& string.IsNullOrWhiteSpace(ziJinSword)
			&& string.IsNullOrWhiteSpace(swordCombat)
			&& string.IsNullOrWhiteSpace(alchemy)
			&& string.IsNullOrWhiteSpace(artifact)
			&& string.IsNullOrWhiteSpace(talisman)
			&& string.IsNullOrWhiteSpace(formation))
		{
			return formatted;
		}

		StringBuilder builder = new StringBuilder(formatted.Length + 512);
		builder.Append(formatted.TrimEnd());
		if (!string.IsNullOrWhiteSpace(xianGuoSummary))
		{
			builder.AppendLine();
			AppendSection(builder, "仙国法");
			AppendSummaryLines(builder, xianGuoSummary, "法统");
		}
		if (!string.IsNullOrWhiteSpace(guZunSummary))
		{
			builder.AppendLine();
			AppendSection(builder, "故尊命痕");
			AppendLine(builder, "重现", guZunSummary);
		}
		if (!string.IsNullOrWhiteSpace(weaponArt))
		{
			builder.AppendLine();
			AppendSection(builder, "器艺");
			AppendWeaponArtSummary(builder, weaponArt);
		}
		if (!string.IsNullOrWhiteSpace(ziJinSword))
		{
			builder.AppendLine();
			AppendSection(builder, "紫金剑道");
			AppendSummaryLines(builder, ziJinSword, "推演");
		}
		if (!string.IsNullOrWhiteSpace(swordCombat))
		{
			builder.AppendLine();
			AppendSection(builder, "剑意显化");
			AppendSummaryLines(builder, swordCombat, "剑道");
		}
		if (!string.IsNullOrWhiteSpace(alchemy)
			|| !string.IsNullOrWhiteSpace(artifact)
			|| !string.IsNullOrWhiteSpace(talisman)
			|| !string.IsNullOrWhiteSpace(formation))
		{
			builder.AppendLine();
			AppendSection(builder, "修仙百艺");
			AppendOptionalLine(builder, "炼丹", alchemy);
			AppendOptionalLine(builder, "炼器", artifact);
			AppendOptionalLine(builder, "符箓", talisman);
			AppendOptionalLine(builder, "阵法", formation);
		}
		return builder.ToString().TrimEnd();
	}

	private static void AppendWeaponArtSummary(StringBuilder builder, string summary)
	{
		if (string.IsNullOrWhiteSpace(summary)) return;
		string[] lines = summary.Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0) continue;
			int separator = line.IndexOf('：');
			if (separator > 0)
			{
				AppendLine(builder, line.Substring(0, separator), line.Substring(separator + 1));
			}
			else
			{
				AppendLine(builder, "器艺", line);
			}
		}
	}

	private static void AppendSummaryLines(StringBuilder builder, string summary, string fallbackLabel)
	{
		if (string.IsNullOrWhiteSpace(summary)) return;
		string[] lines = summary.Replace("\r", string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0) continue;
			int separator = line.IndexOf('：');
			if (separator > 0) AppendLine(builder, line.Substring(0, separator), line.Substring(separator + 1));
			else AppendLine(builder, fallbackLabel, line);
		}
	}

	private static string SimplifyCraftSummary(string summary)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return string.Empty;
		}

		int detailStart = summary.IndexOf('（');
		return (detailStart > 0 ? summary.Substring(0, detailStart) : summary).Trim();
	}


	internal static string Format(in XjActorInfoReadModel model)
	{
		return Format(in model, string.Empty, false, false, string.Empty, string.Empty);
	}

	private static string Format(
		in XjActorInfoReadModel model,
		string bottleneckSummary,
		bool isFuQi,
		bool isZiJin,
		string fuQiSummary,
		string daoTuDisplayOverride)
	{
		if (!model.Found)
		{
			return Line("状态", "暂无角色信息");
		}

		StringBuilder builder = new StringBuilder(512);
		bool isZiJinJinDan = isZiJin && string.Equals(model.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		bool isFuQiZhenJun = isFuQi && string.Equals(model.RealmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		bool isDaoTai = string.Equals(model.RealmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(model.RealmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
		bool isShenDan = string.Equals(model.RealmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		bool usesFruitPositionTemplate = XjHighRealmDisplayPolicy.UsesFruitPositionTemplate(model.RealmId);
		bool isFruitlessXian = (isZiJinJinDan || isFuQiZhenJun)
			&& (model.JinDanSummary.IndexOf("结璘仙", StringComparison.Ordinal) >= 0
				|| model.JinDanSummary.IndexOf("郁仪仙", StringComparison.Ordinal) >= 0);
		bool hasCultivationIdentity = isFuQi || isZiJin || isShenDan || CanShowEstablishedDaoTu(model.RealmId)
			|| !string.IsNullOrWhiteSpace(model.DaoTu);
		if (hasCultivationIdentity)
		{
			AppendSection(builder, "道途");
			AppendLine(builder, "修法", isFuQi ? "服气养性" : isZiJin || isShenDan ? "紫府金丹道" : "尚未定修法");
			if (isShenDan)
			{
				// 神丹只记录自己依附的高境锚点，不拥有独立果位。
				AppendOptionalLine(builder, "挂靠", ExtractShenDanAnchor(model.JinDanSummary));
			}
			else if (usesFruitPositionTemplate)
			{
				AppendOptionalLine(builder, "金性", ExtractJinXing(model.JinDanSummary));
				if (!isFruitlessXian)
				{
					string guoWei = ExtractGuoWei(model.JinDanSummary);
					AppendOptionalLine(builder, "果位", guoWei);
					if (IsRealSummary(model.GuoWeiZhongAiSummary))
					{
						AppendPlainLabelLine(builder, "果位钟爱");
					}
					AppendQuanBingField(builder, ExtractQuanBing(model.QuanBingSummary));
					AppendOptionalRealLine(builder, "高境事务", model.QuanBingProcessSummary);
				}
			}
			if (isFuQi || CanShowEstablishedDaoTu(model.RealmId) || !string.IsNullOrWhiteSpace(model.DaoTu))
			{
				string displayedDaoTu = string.IsNullOrWhiteSpace(daoTuDisplayOverride) ? model.DaoTu : daoTuDisplayOverride;
				AppendLine(builder, "道途", XjDisplayNameSanitizer.GameTerm(displayedDaoTu, "无道途"));
				if (usesFruitPositionTemplate)
				{
					string guoWeiBlessing = ResolveGuoWeiBlessing(model.DaoTu);
					if (!string.IsNullOrWhiteSpace(guoWeiBlessing)) AppendLine(builder, "果位庇佑", guoWeiBlessing);
				}
			}
			AppendOptionalRealLine(builder, "纪元加成", model.EraBonusSummary);
		}

		if (isZiJin)
		{
			string xianJiLabel = ResolveXianJiLabel(model.RealmId);
			AppendSection(builder, xianJiLabel);
			AppendWrappedListField(builder, xianJiLabel,
				IsRealSummary(model.XianJiSummary) ? model.XianJiSummary : "暂无神通");
		}

		AppendSection(builder, "修炼");
		AppendLine(builder, "道行", AppendBottleneckMarker(model.RealmDisplay, !string.IsNullOrWhiteSpace(bottleneckSummary)));
		if (isZiJin && IsRealSummary(model.QiuJinIntentSummary))
		{
			AppendLine(builder, "求金之志", model.QiuJinIntentSummary);
		}
		AppendOptionalLine(builder, "瓶颈", bottleneckSummary);
		AppendFaBaoLine(builder, model.FaBaoSummary);
		if ((isZiJinJinDan || isFuQiZhenJun || isDaoTai) && !isFruitlessXian && model.JinDanYiXiang > 0)
		{
			AppendLine(builder, isDaoTai ? "道胎修持" : isFuQiZhenJun ? "真君修持" : "金丹道行", model.JinDanYiXiang.ToString(CultureInfo.InvariantCulture));
		}
		if ((isZiJinJinDan || isFuQiZhenJun || isDaoTai) && !isFruitlessXian && model.GuoWeiImage > 0)
		{
			AppendLine(builder, "果位意象", model.GuoWeiImage.ToString(CultureInfo.InvariantCulture));
		}
		if (usesFruitPositionTemplate && !isFruitlessXian && IsRealSummary(model.HighRealmDoctrineSummary))
		{
			AppendSection(builder, "证道宣法");
			AppendSummaryLines(builder, model.HighRealmDoctrineSummary, "证道");
		}
		AppendOptionalRealLine(builder, "道胎功绩", model.DaoTaiMeritSummary);
		AppendOptionalRealLine(builder, "持位修炼", model.DaoTaiBindingSummary);
		AppendOptionalRealLine(builder, "突破后损", model.BreakthroughSummary);
		AppendOptionalRealLine(builder, "道脉余荫", model.StageBonusSummary);
		AppendOptionalRealLine(builder, "血脉", model.BloodlineSummary);
		if (isZiJin && IsRealSummary(model.CaiQiSummary)) AppendLine(builder, "采气进度", model.CaiQiSummary);
		if (usesFruitPositionTemplate && IsRealSummary(model.JinDanDaoSpellSummary))
		{
			AppendSection(builder, "真君法术");
			AppendWrappedListField(builder, "法术", model.JinDanDaoSpellSummary.Replace("；", "\n"));
		}
		if (isFuQi && !string.IsNullOrWhiteSpace(fuQiSummary))
		{
			AppendSection(builder, "服气修持");
			AppendSummaryLines(builder, fuQiSummary, "修持");
		}

		AppendSection(builder, "命数");
		AppendLine(builder, "命数", FormatInteger(model.MingShu));
		if (model.MingShuCongenital > 0f)
		{
			AppendLine(builder, "先天命数", FormatInteger(model.MingShuCongenital));
		}
		if (Math.Abs(model.MingShuAcquired) > 0.01f)
		{
			AppendLine(builder, "后天命数", FormatSignedInteger(model.MingShuAcquired));
		}
		AppendOptionalRealLine(builder, "转世", model.ReincarnationSummary);
		AppendOptionalRealLine(builder, "前世", model.FormerLifeSummary);
		AppendOptionalRealLine(builder, "命数加成", model.MingShuBonusSummary);

		bool hasGongFa = IsRealSummary(model.GongFaSummary);
		bool hasQiuJinFa = isZiJin && IsRealSummary(model.QiuJinFaSummary);
		if (hasGongFa || hasQiuJinFa)
		{
			AppendSection(builder, "功法");
			if (hasGongFa)
			{
				AppendLine(builder, "功法名称", ExtractGongFaName(model.GongFaSummary));
				AppendLine(builder, "功法品级", ExtractGrade(model.GongFaSummary));
				AppendOptionalRealLine(builder, "功法加成", model.GongFaBonusSummary);
			}

			if (hasQiuJinFa)
			{
				AppendLine(builder, "求金法", model.QiuJinFaSummary);
			}
		}

		bool hasFamily = IsRealSummary(model.FamilySummary);
		bool hasZongMen = IsRealSummary(model.ZongMenSummary);
		if (hasFamily || hasZongMen)
		{
			AppendSection(builder, "家族律典");
			if (hasFamily)
			{
				AppendLine(builder, "家族", model.FamilySummary);
			}
			if (hasZongMen)
			{
				AppendLine(builder, "宗门", model.ZongMenSummary);
			}
		}

		return builder.ToString().TrimEnd();
	}

	private static bool CanShowEstablishedDaoTu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static string AppendBottleneckMarker(string realmDisplay, bool hasBottleneck)
	{
		string normalized = string.IsNullOrWhiteSpace(realmDisplay) ? "未入道" : realmDisplay.Trim();
		if (!hasBottleneck || normalized.IndexOf("瓶颈", StringComparison.Ordinal) >= 0)
		{
			return normalized;
		}

		return normalized + "（瓶颈）";
	}


	private static void AppendSection(StringBuilder builder, string title)
	{
		if (builder.Length > 0)
		{
			builder.AppendLine();
		}

		builder.Append(Section(title));
	}

	private static void AppendLine(StringBuilder builder, string label, string value)
	{
		builder.Append(Line(label, DecorateValue(label, value)));
	}

	private static string DecorateValue(string label, string value)
	{
		string text = PlainText(EmptyWhenMissing(value)).Trim();
		string color = ValueColor;
		if (string.Equals(label, "道行", StringComparison.Ordinal))
		{
			color = ResolveRealmColor(string.Empty, text);
		}
		else if (string.Equals(label, "道途", StringComparison.Ordinal)
			|| string.Equals(label, "功法名称", StringComparison.Ordinal)
			|| string.Equals(label, "求金法", StringComparison.Ordinal)
			|| string.Equals(label, "神通", StringComparison.Ordinal)
			|| string.Equals(label, "仙基", StringComparison.Ordinal))
		{
			color = AccentColor;
		}
		else if (string.Equals(label, "果位", StringComparison.Ordinal)
			|| string.Equals(label, "金性", StringComparison.Ordinal)
			|| string.Equals(label, "功法品级", StringComparison.Ordinal)
			|| string.Equals(label, "器物", StringComparison.Ordinal)
			|| label.Contains("法宝")
			|| label.Contains("灵宝"))
		{
			color = WarnColor;
		}
		else if (string.Equals(label, "命数", StringComparison.Ordinal)
			|| string.Equals(label, "先天命数", StringComparison.Ordinal)
			|| string.Equals(label, "后天命数", StringComparison.Ordinal))
		{
			color = AccentColor;
		}
		else if (text.Contains("瓶颈") || text.Contains("失败") || text.Contains("后损"))
		{
			color = DangerColor;
		}
		else if (text.Contains("紫府") || text.Contains("权柄") || text.Contains("转世") || text.Contains("前世"))
		{
			color = PurpleColor;
		}
		else if (text.Contains("暂无") || text.Contains("无") || text.Contains("未入"))
		{
			color = ValueColor;
		}
		return Highlight(text, color);
	}

	private static string ResolveRealmColor(string realmId, string text)
	{
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(text) && text.Contains("道胎")))
		{
			return "#A8D8FF";
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(text) && (text.Contains("金丹") || text.Contains("真君羽士") || text.Contains("结璘仙"))))
		{
			return WarnColor;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(text) && (text.Contains("紫府") || text.Contains("真人"))))
		{
			return "#B7A7FF";
		}

		if (!string.IsNullOrWhiteSpace(text) && text.Contains("筑基"))
		{
			return AccentColor;
		}

		if (!string.IsNullOrWhiteSpace(text)
			&& (text.Contains("炼气") || text.Contains("胎息") || text.Contains("未入道")))
		{
			return GoodColor;
		}

		return ValueColor;
	}

	private static string Title(string title)
	{
		return "<color=" + TitleColor + "><b>" + PlainText(title ?? string.Empty) + "</b></color>\n";
	}

	private static string Section(string title)
	{
		return "<color=" + SectionColor + "><b>◆ " + PlainText(title ?? string.Empty) + "</b></color>\n";
	}

	private static string Line(string label, string value)
	{
		return "<color=" + LabelColor + ">" + PlainText(label ?? string.Empty) + "</color> " + (string.IsNullOrWhiteSpace(value) ? Highlight("暂无", ValueColor) : value) + "\n";
	}

	private static string Highlight(string value, string color)
	{
		string text = PlainText(value ?? string.Empty).Trim();
		return text.Length == 0 ? string.Empty : "<color=" + color + ">" + text + "</color>";
	}

	private static void AppendOptionalLine(StringBuilder builder, string label, string value)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			AppendLine(builder, label, value);
		}
	}

	private static void AppendOptionalRealLine(StringBuilder builder, string label, string value)
	{
		if (IsRealSummary(value))
		{
			AppendLine(builder, label, value);
		}
	}

	private static void AppendFaBaoLine(StringBuilder builder, string value)
	{
		if (!IsRealSummary(value))
		{
			return;
		}

		string normalized = value.Trim();
		// 去掉冗余前缀: "紫府灵宝：灵宝：xxx" → "紫府灵宝：xxx"
		int firstColon = normalized.IndexOf('：');
		if (firstColon > 0)
		{
			string prefix = normalized.Substring(0, firstColon);
			string rest = normalized.Substring(firstColon + 1).Trim();
			// 检查 rest 是否以“法器/灵宝/法宝：”开头
			if (rest.StartsWith("法器：", StringComparison.Ordinal))
			{
				AppendLine(builder, prefix, rest.Substring(3).Trim());
				return;
			}
			if (rest.StartsWith("灵宝：", StringComparison.Ordinal))
			{
				AppendLine(builder, prefix, rest.Substring(3).Trim());
				return;
			}
			if (rest.StartsWith("法宝：", StringComparison.Ordinal))
			{
				AppendLine(builder, prefix, rest.Substring(3).Trim());
				return;
			}
			AppendLine(builder, prefix, rest);
			return;
		}

		AppendLine(builder, "器物", normalized);
	}


	private static void AppendWrappedListField(StringBuilder builder, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		string[] lines = value.Replace("\r", string.Empty)
			.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		bool wroteFirst = false;
		for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
		{
			string[] items = lines[lineIndex].Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries);
			for (int itemIndex = 0; itemIndex < items.Length; itemIndex++)
			{
				string item = items[itemIndex].Trim();
				if (string.IsNullOrWhiteSpace(item))
				{
					continue;
				}

				if (!wroteFirst)
				{
					string[] chunks = WrapPanelText(item);
					if (chunks.Length == 0)
					{
						continue;
					}

					AppendLine(builder, label, chunks[0]);
					wroteFirst = true;
					for (int chunkIndex = 1; chunkIndex < chunks.Length; chunkIndex++)
					{
						AppendContinuationLine(builder, chunks[chunkIndex]);
					}
				}
				else
				{
					string[] chunks = WrapPanelText(item);
					for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
					{
						AppendContinuationLine(builder, chunks[chunkIndex]);
					}
				}
			}
		}
	}

	private static void AppendQuanBingField(StringBuilder builder, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		AppendPlainLabelLine(builder, "权柄");
		string[] lines = value.Replace("\r", string.Empty)
			.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0)
			{
				continue;
			}

			AppendContinuationLine(builder, line);
		}
	}

	private static void AppendPlainLabelLine(StringBuilder builder, string label)
	{
		if (string.IsNullOrWhiteSpace(label))
		{
			return;
		}

		builder.Append("<color=").Append(LabelColor).Append(">");
		builder.Append(PlainText(label.Trim()));
		builder.AppendLine("</color>");
	}

	private static void AppendContinuationLine(StringBuilder builder, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		builder.Append("<color=").Append(ValueColor).Append(">");
		builder.Append(PlainText(value.Trim()));
		builder.AppendLine("</color>");
	}

	private static string[] WrapPanelText(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0)
		{
			return Array.Empty<string>();
		}

		const int maxChars = 18;
		if (text.Length <= maxChars)
		{
			return new[] { text };
		}

		List<string> chunks = new List<string>();
		int start = 0;
		while (start < text.Length)
		{
			int length = Math.Min(maxChars, text.Length - start);
			int split = FindWrapSplit(text, start, length);
			if (split <= start)
			{
				split = start + length;
			}

			chunks.Add(text.Substring(start, split - start).Trim());
			start = split;
			while (start < text.Length && (text[start] == '、' || text[start] == '，' || text[start] == ' ' || text[start] == '/'))
			{
				start++;
			}
		}

		return chunks.ToArray();
	}

	private static int FindWrapSplit(string text, int start, int length)
	{
		int end = Math.Min(text.Length, start + length);
		for (int i = end - 1; i > start; i--)
		{
			char c = text[i];
			if (c == '、' || c == '，' || c == '/' || c == ' ')
			{
				return i;
			}
		}

		return end;
	}

	private static string FormatInteger(float value)
	{
		return ((int)Math.Floor(Math.Max(0f, value))).ToString(CultureInfo.InvariantCulture);
	}

	private static string FormatSignedInteger(float value)
	{
		return ((int)Math.Floor(value)).ToString(CultureInfo.InvariantCulture);
	}

	private static string ResolveXianJiLabel(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			? "神通"
			: "仙基";
	}

	private static string ExtractJinXing(string jinDanSummary)
	{
		if (string.IsNullOrWhiteSpace(jinDanSummary) || jinDanSummary.Contains("暂无") || jinDanSummary.Contains("未成"))
		{
			return string.Empty;
		}

		string[] parts = SplitHighRealmSummary(jinDanSummary);
		return parts.Length > 0 ? parts[0].Trim() : string.Empty;
	}

	private static string ExtractGuoWei(string jinDanSummary)
	{
		if (string.IsNullOrWhiteSpace(jinDanSummary) || jinDanSummary.Contains("暂无") || jinDanSummary.Contains("未成"))
		{
			return string.Empty;
		}

		string[] parts = SplitHighRealmSummary(jinDanSummary);
		return parts.Length > 1 ? parts[1].Trim() : string.Empty;
	}

	private static string ExtractShenDanAnchor(string jinDanSummary)
	{
		if (string.IsNullOrWhiteSpace(jinDanSummary))
		{
			return string.Empty;
		}

		const string marker = "挂靠-";
		int index = jinDanSummary.IndexOf(marker, StringComparison.Ordinal);
		if (index < 0)
		{
			return string.Empty;
		}

		string value = jinDanSummary.Substring(index + marker.Length).Trim();
		int yearIndex = value.IndexOf(" - ", StringComparison.Ordinal);
		if (yearIndex >= 0)
		{
			value = value.Substring(0, yearIndex).Trim();
		}
		return value;
	}

	private static string[] SplitHighRealmSummary(string summary)
	{
		string text = summary ?? string.Empty;
		return text.IndexOf(" - ", StringComparison.Ordinal) >= 0
			? text.Split(new[] { " - " }, StringSplitOptions.None)
			: text.Split('/');
	}

	private static string ExtractQuanBing(string quanBingSummary)
	{
		if (string.IsNullOrWhiteSpace(quanBingSummary)
			|| quanBingSummary.Contains("暂无")
			|| quanBingSummary.Contains("暂未")
			|| quanBingSummary.Contains("规则"))
		{
			return string.Empty;
		}

		string normalized = quanBingSummary.Trim()
			.Replace(" - 夺得：", "\n夺得：")
			.Replace(" - 融入：", "\n融入：")
			.Replace(" - 外道：", "\n融入：")
			.Replace(" - 洞天：", "\n洞天：")
			.Replace("\n\n", "\n");
		string[] lines = normalized.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> result = new List<string>();
		for (int i = 0; i < lines.Length; i++)
		{
			AppendQuanBingRows(result, lines[i]);
		}
		return string.Join("\n", result);
	}

	private static void AppendQuanBingRows(List<string> result, string line)
	{
		string text = (line ?? string.Empty).Trim();
		if (text.Length == 0) return;
		string prefix = string.Empty;
		string valuesText = text;
		string[] prefixes = { "夺得：", "融入：", "洞天：" };
		for (int i = 0; i < prefixes.Length; i++)
		{
			if (!text.StartsWith(prefixes[i], StringComparison.Ordinal)) continue;
			prefix = prefixes[i];
			valuesText = text.Substring(prefix.Length).Trim();
			break;
		}

		string[] values = valuesText.Split(new[] { '、', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		if (values.Length == 0)
		{
			result.Add(text);
			return;
		}

		if (!string.IsNullOrEmpty(prefix))
		{
			result.Add(prefix.TrimEnd('：'));
		}

		for (int index = 0; index < values.Length; index += 3)
		{
			List<string> chunk = new List<string>(3);
			for (int col = 0; col < 3 && index + col < values.Length; col++)
			{
				string value = values[index + col].Trim();
				if (value.Length > 0) chunk.Add(value);
			}
			if (chunk.Count == 0) continue;
			result.Add(string.Join("、", chunk));
		}
	}

	private static string ExtractGongFaName(string gongFaSummary)
	{
		if (string.IsNullOrWhiteSpace(gongFaSummary) || gongFaSummary.Contains("暂无"))
		{
			return "暂无功法";
		}

		int start = gongFaSummary.IndexOf('（');
		return start > 0 ? gongFaSummary.Substring(0, start).Trim() : gongFaSummary.Trim();
	}

	private static string ExtractGrade(string gongFaSummary)
	{
		if (string.IsNullOrWhiteSpace(gongFaSummary) || gongFaSummary.Contains("暂无"))
		{
			return "暂无";
		}

		int start = gongFaSummary.IndexOf('（');
		int grade = gongFaSummary.IndexOf("品", StringComparison.Ordinal);
		if (start < 0 || grade <= start)
		{
			return "暂无";
		}

		return gongFaSummary.Substring(start + 1, grade - start);
	}


	private static string ResolveGuoWeiBlessing(string daoTu)
	{
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return string.Empty;
		}

		string manifestDaoTu = daoTu.Trim();
		int marker = manifestDaoTu.LastIndexOf('闰');
		if (marker >= 0 && marker + 1 < manifestDaoTu.Length)
			manifestDaoTu = manifestDaoTu.Substring(marker + 1).Trim();
		return XjGuoWeiRegistry.HasManifestedDaoTu(manifestDaoTu) ? "道势已显" : string.Empty;
	}


	private static string PlainText(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(value.Length);
		bool insideTag = false;
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (c == '<')
			{
				insideTag = true;
				continue;
			}
			if (insideTag)
			{
				if (c == '>')
				{
					insideTag = false;
				}
				continue;
			}
			builder.Append(c);
		}
		return builder.ToString();
	}

	private static bool IsRealSummary(string value)
	{
		return !string.IsNullOrWhiteSpace(value)
			&& !value.Contains("暂无")
			&& !value.Contains("未接入")
			&& !value.Contains("未成");
	}

	private static string EmptyWhenMissing(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "暂无" : value;
	}
}
