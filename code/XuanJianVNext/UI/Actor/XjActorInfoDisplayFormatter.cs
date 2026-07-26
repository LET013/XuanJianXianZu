using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.WeaponArt;

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
		string bottleneck = XjBottleneckEventSystem.BuildStatusSummary(actor);
		string formatted = Format(XjActorInfoReadModel.BuildForActor(actor), bottleneck);
		string weaponArt = XjWeaponArtSystem.BuildDisplaySummary(actor);
		string alchemy = SimplifyCraftSummary(XjCraftProficiencySystem.BuildAlchemyProgressSummary(actor));
		string artifact = SimplifyCraftSummary(XjCraftProficiencySystem.BuildArtifactProgressSummary(actor));
		string talisman = SimplifyCraftSummary(XjCraftProficiencySystem.BuildTalismanProgressSummary(actor));
		string formation = SimplifyCraftSummary(XjCraftProficiencySystem.BuildFormationProgressSummary(actor));
		if (string.IsNullOrWhiteSpace(weaponArt)
			&& string.IsNullOrWhiteSpace(alchemy)
			&& string.IsNullOrWhiteSpace(artifact)
			&& string.IsNullOrWhiteSpace(talisman)
			&& string.IsNullOrWhiteSpace(formation))
		{
			return formatted;
		}

		StringBuilder builder = new StringBuilder(formatted.Length + 384);
		builder.Append(formatted.TrimEnd());
		if (!string.IsNullOrWhiteSpace(weaponArt))
		{
			builder.AppendLine();
			AppendSection(builder, "器艺");
			AppendWeaponArtSummary(builder, weaponArt);
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

	private static string SimplifyCraftSummary(string summary)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return string.Empty;
		}

		int detailStart = summary.IndexOf('（');
		return (detailStart > 0 ? summary.Substring(0, detailStart) : summary).Trim();
	}

	private static string SimplifyCraftNextRankText(string summary)
	{
		if (string.IsNullOrWhiteSpace(summary))
		{
			return string.Empty;
		}

		int detailStart = summary.IndexOf('（');
		int detailEnd = summary.LastIndexOf('）');
		if (detailStart < 0 || detailEnd <= detailStart)
		{
			return string.Empty;
		}

		string detail = summary.Substring(detailStart + 1, detailEnd - detailStart - 1);
		int marker = detail.LastIndexOf('，');
		string next = marker >= 0 ? detail.Substring(marker + 1) : detail;
		next = PlainText(next).Trim();
		if (string.IsNullOrWhiteSpace(next))
		{
			return string.Empty;
		}

		return next;
	}

	internal static string Format(in XjActorInfoReadModel model)
	{
		return Format(in model, string.Empty);
	}

	private static string Format(in XjActorInfoReadModel model, string bottleneckSummary)
	{
		if (!model.Found)
		{
			return Title("玄鉴照录") + Line("状态", "暂无角色信息");
		}

		StringBuilder builder = new StringBuilder(512);
		bool isJinDan = string.Equals(model.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		bool isShenDan = string.Equals(model.RealmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		bool isJieLinXian = isJinDan
			&& model.JinDanSummary.IndexOf("结璘仙", StringComparison.Ordinal) >= 0;
		AppendHeader(builder, in model);
		AppendSection(builder, "道途");
		if (isShenDan)
		{
			AppendOptionalLine(builder, "果位", ExtractGuoWei(model.JinDanSummary));
			AppendOptionalLine(builder, "挂靠金丹", ExtractShenDanAnchor(model.JinDanSummary));
		}
		else
		{
			AppendOptionalLine(builder, "金性", ExtractJinXing(model.JinDanSummary));
		}
		if (isJinDan)
		{
			if (isJieLinXian)
			{
			}
			else
			{
				AppendOptionalLine(builder, "果位", ExtractGuoWei(model.JinDanSummary));
				AppendOptionalLine(builder, "果位钟爱", model.GuoWeiZhongAiSummary);
				AppendWrappedListField(builder, "权柄", ExtractQuanBing(model.QuanBingSummary));
			}
		}
		bool showEstablishedDaoTu = CanShowEstablishedDaoTu(model.RealmId);
		if (showEstablishedDaoTu)
		{
			AppendLine(builder, "道途", string.IsNullOrWhiteSpace(model.DaoTu) ? "无道途" : model.DaoTu);
			string guoWeiBlessing = ResolveGuoWeiBlessing(model.DaoTu);
			if (!string.Equals(guoWeiBlessing, "0%", StringComparison.Ordinal))
			{
				AppendLine(builder, "果位庇佑", guoWeiBlessing);
			}
		}
		AppendOptionalRealLine(builder, "纪元加成", model.EraBonusSummary);

		if (IsRealSummary(model.XianJiSummary))
		{
			string xianJiLabel = ResolveXianJiLabel(model.RealmId);
			AppendSection(builder, xianJiLabel);
			AppendWrappedListField(builder, xianJiLabel, model.XianJiSummary);
		}

		AppendSection(builder, "修炼");
		AppendLine(builder, "道行", AppendBottleneckMarker(model.RealmDisplay, !string.IsNullOrWhiteSpace(bottleneckSummary)));
		AppendOptionalLine(builder, "瓶颈", bottleneckSummary);
		AppendFaBaoLine(builder, model.FaBaoSummary);
		if (isJinDan && !isJieLinXian && model.JinDanYiXiang > 0)
		{
			AppendLine(builder, "果位意象", model.JinDanYiXiang.ToString(CultureInfo.InvariantCulture));
		}
		AppendOptionalRealLine(builder, "突破后损", model.BreakthroughSummary);
		AppendOptionalRealLine(builder, "紫府余荫", model.StageBonusSummary);
		AppendOptionalRealLine(builder, "血脉", model.BloodlineSummary);
		if (IsRealSummary(model.CaiQiSummary))
		{
			AppendLine(builder, "采气进度", model.CaiQiSummary);
		}
		if (isJinDan && IsRealSummary(model.JinDanDaoSpellSummary))
		{
			AppendSection(builder, "金丹法术");
			AppendWrappedListField(builder, "法术", model.JinDanDaoSpellSummary.Replace("；", "\n"));
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
		bool hasQiuJinFa = IsRealSummary(model.QiuJinFaSummary);
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
			AppendSection(builder, "家族宗门");
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
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
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

	private static void AppendHeader(StringBuilder builder, in XjActorInfoReadModel model)
	{
		builder.Append(Title("玄鉴照录"));
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
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(text) && (text.Contains("金丹") || text.Contains("结璘仙"))))
		{
			return WarnColor;
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(text) && text.Contains("紫府")))
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

	private static void AppendListField(StringBuilder builder, string label, string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return;
		}

		string normalized = value.Trim();
		int split = normalized.IndexOf('：');
		string prefix = split >= 0 ? normalized.Substring(0, split + 1) : string.Empty;
		string list = split >= 0 ? normalized.Substring(split + 1) : normalized;
		string[] items = list.Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries);
		if (items.Length <= 1)
		{
			AppendLine(builder, label, normalized);
			return;
		}

		AppendLine(builder, label, prefix + items[0].Trim());
		for (int i = 1; i < items.Length; i++)
		{
			builder.AppendLine(PlainText(items[i].Trim()));
		}
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

		const string marker = "挂靠金丹-";
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

		return quanBingSummary.Trim()
			.Replace(" - 夺得：", "\n夺得：")
			.Replace(" - 外道：", "\n外道：")
			.Replace(" - 洞天：", "\n洞天：")
			.Replace("\n\n", "\n");
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
			return "0%";
		}

		return XjGuoWeiRegistry.HasManifestedDaoTu(daoTu) ? "20%" : "0%";
	}

	private static string ExtractFaBao(string faBaoSummary)
	{
		if (string.IsNullOrWhiteSpace(faBaoSummary))
		{
			return string.Empty;
		}

		string value = faBaoSummary.Trim();
		if (value.StartsWith("法器：", StringComparison.Ordinal)) return value.Substring("法器：".Length).Trim();
		if (value.StartsWith("灵宝：", StringComparison.Ordinal)) return value.Substring("灵宝：".Length).Trim();
		return value.StartsWith("法宝：", StringComparison.Ordinal)
			? value.Substring("法宝：".Length).Trim()
			: value;
	}

	private static string NormalizeEmptySummary(string value, string emptyValue)
	{
		return string.IsNullOrWhiteSpace(value) ? emptyValue : value.Trim();
	}

	private static string ShortHeaderMeta(string value)
	{
		string text = PlainText(value ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Trim();
		if (text.Length == 0)
		{
			return string.Empty;
		}

		text = text.Replace("发源城：", string.Empty).Replace("发源城:", string.Empty).Trim();
		int split = text.IndexOfAny(new[] { '；', ';', '，', ',', '（', '(' });
		if (split > 0)
		{
			text = text.Substring(0, split).Trim();
		}

		const int maxChars = 18;
		return text.Length <= maxChars ? text : text.Substring(0, maxChars).Trim();
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
