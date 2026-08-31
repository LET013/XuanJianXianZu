using System;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Doctrine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private void DrawCultivationClimatePage(XjCodexSnapshot snapshot)
	{
		XjCodexCultivationClimate climate = snapshot?.CultivationClimate ?? new XjCodexCultivationClimate();
		DrawPageHeader("修道大势", "汇合当世修行气数与百年世谱，照见境界结构、四道修法、道统形势、古今释分布与高境承续。");

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9FC9C0");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=21><color=#9FC9C0>当世修道气象</color></size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=grey>统计纪年：" + (climate.HasAnnualObservation ? XjChronology.FormatYear(climate.ObservationYear) : "待统一检测") + "</color>");
		GUILayout.EndHorizontal();
		GUILayout.Label("<size=17>" + Rich(Empty(climate.Summary, "尚无可照录的修道气象。")) + "</size>");
		GUILayout.Label("<color=grey>修道气象随年度更迭更新。</color>");
		GUILayout.EndVertical();

		DrawSectionTitle("世运与传承", "#E6B86E");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#E6B86E");
		GUILayout.Label("<b><size=19><color=#E6B86E>" + Rich(XjWorldFortuneSystem.CurrentDisplay) + "</color></size></b>"
			+ (XjWorldFortuneSystem.SinceYear > 0 ? "　<color=grey>自" + XjChronology.FormatYear(XjWorldFortuneSystem.SinceYear) + "</color>" : string.Empty));
		GUILayout.Label(Rich(Empty(XjWorldFortuneSystem.Summary, "世运尚未结算。")));
		GUILayout.Label("<color=grey>奇遇洞天显化严格服从配置的玄鉴历固定周期；世运只归纳世界态势，不再改写该周期，也不暗改求金、果位容量或基础突破率。</color>");
		GUILayout.Space(6f);
		GUILayout.Label("<b><color=#74E8FF>宗门传承覆盖</color></b>　" + Rich(Empty(XjSectTransmissionCoverageSystem.LastGlobalSummary, "尚未结算。")));
		GUILayout.Label("<color=grey>按真实道途与五品、六品、求金法分格观照；高阶传承缺口会推动宗门换法、访秘境与补撰。</color>");
		GUILayout.EndVertical();

		DrawSectionTitle("境界结构", "#9FC9C0");
		if (!climate.HasAnnualObservation)
		{
			DrawEmptyCard("尚无本年度修道气象记录。", "#777777");
		}
		else
		{
			DrawCultivationClimateMetricGrid(climate);
			int maximum = Math.Max(1, Math.Max(
				Math.Max(climate.Unentered, climate.TaiXi),
				Math.Max(
					Math.Max(climate.LianQi, climate.ZhuJi + climate.HuangGuan),
					Math.Max(climate.ZhenRen, climate.ZhenJun))));
			DrawMetricBar("未入道", climate.Unentered, maximum, "#888888", "已有修炼身份但尚未进入可辨境界");
			DrawMetricBar("胎息", climate.TaiXi, maximum, "#A7E08A");
			DrawMetricBar("炼气", climate.LianQi, maximum, "#9CD7FF");
			DrawMetricBar("筑基", climate.ZhuJi, maximum, "#B7A7FF");
			DrawMetricBar("黄冠", climate.HuangGuan, maximum, "#74E8FF", "服气养性中境");
			DrawMetricBar("真人", climate.ZhenRen, maximum, "#D2B5FF", "紫府真人与服气真人合计");
			DrawMetricBar("真君", climate.ZhenJun, maximum, "#FFD37A", "金丹、神丹、郁仪仙、结璘仙与真君羽士合计");
		}

		DrawSectionTitle("古今释与释位结构", "#D9B86C");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D9B86C");
		if (!climate.HasAnnualObservation)
		{
			GUILayout.Label("<color=grey>古释、今释与各释位将在统一年度观测完成后显示。</color>");
		}
		else
		{
			DrawShiClimateMetricGrid(climate);
			int shiMax = Math.Max(1, Math.Max(Math.Max(climate.ShiMonk, climate.ShiDharmaMaster),
				Math.Max(Math.Max(climate.ShiLianMin, climate.ShiMoHe), Math.Max(climate.ShiDharmaForm, climate.ShiWorldHonored))));
			DrawMetricBar("僧侣", climate.ShiMonk, shiMax, "#D9C78A");
			DrawMetricBar("法师", climate.ShiDharmaMaster, shiMax, "#C8B57A");
			DrawMetricBar("怜愍", climate.ShiLianMin, shiMax, "#E8A7FF", "今释不退转地，挂靠摩诃位");
			DrawMetricBar("摩诃", climate.ShiMoHe, shiMax, "#FFD37A", "摩诃位形念三不退");
			DrawMetricBar("法相", climate.ShiDharmaForm, shiMax, "#D2B5FF");
			DrawMetricBar("世尊", climate.ShiWorldHonored, shiMax, "#FFB77A");
		}
		GUILayout.EndVertical();


		DrawSectionTitle("四道修法", "#74E8FF");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#74E8FF");
		if (!climate.HasAnnualObservation)
		{
			GUILayout.Label("<color=grey>紫金、服气、古释、今释人数将在同一份年度观测生成后显示。</color>");
		}
		else
		{
			int pathTotal = Math.Max(1, climate.ZiFuJinDanPath + climate.FuQiYangXingPath + climate.AncientShi + climate.ModernShi);
			DrawCultivationPathMetricGrid(climate);
			GUILayout.Label("<color=#FFD37A>紫金 · 紫府金丹道</color>");
			DrawInlineBar((float)climate.ZiFuJinDanPath / pathTotal, "#FFD37A", GUILayout.Height(13f), GUILayout.ExpandWidth(true));
			GUILayout.Label("<color=#74E8FF>服气 · 服气养性道</color>");
			DrawInlineBar((float)climate.FuQiYangXingPath / pathTotal, "#74E8FF", GUILayout.Height(13f), GUILayout.ExpandWidth(true));
			GUILayout.Label("<color=#D9C78A>古释</color>");
			DrawInlineBar((float)climate.AncientShi / pathTotal, "#D9C78A", GUILayout.Height(13f), GUILayout.ExpandWidth(true));
			GUILayout.Label("<color=#D2B5FF>今释</color>");
			DrawInlineBar((float)climate.ModernShi / pathTotal, "#D2B5FF", GUILayout.Height(13f), GUILayout.ExpandWidth(true));
		}
		GUILayout.EndVertical();

		DrawDoctrineRelations(climate);


		DrawSectionTitle("百年承续", "#8FA9C7");
		if (climate.Trends == null || climate.Trends.Count == 0)
		{
			DrawEmptyCard("尚无两个可比较的修道时间点；首卷百年世谱生成后将显示境界升降。", "#777777");
		}
		else
		{
			GUILayout.Label("<color=grey>对照卷：" + Rich(Empty(climate.TrendBaseline, "百年世谱")) + "</color>");
			int trendMaximum = 1;
			for (int i = 0; i < climate.Trends.Count; i++)
			{
				trendMaximum = Math.Max(trendMaximum, Math.Max(climate.Trends[i].Current, climate.Trends[i].Previous));
			}
			for (int i = 0; i < climate.Trends.Count; i++)
			{
				XjCodexCultivationTrendItem item = climate.Trends[i];
				string color = item.Delta > 0 ? "#A7E08A" : item.Delta < 0 ? "#FF8877" : "#CFC7B2";
				string delta = item.Delta > 0 ? "较前卷 +" + item.Delta
					: item.Delta < 0 ? "较前卷 " + item.Delta : "与前卷持平";
				DrawMetricBar(item.Name, item.Current, trendMaximum, color, "前卷 " + item.Previous + " · " + delta);
			}
		}

		DrawSectionTitle("近百年高境出入", "#FFD37A");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.Label("<color=grey>依据仙鉴史册中" + XjChronology.FormatYear(climate.RecentWindowStartYear) + "至" + XjChronology.FormatYear(snapshot.WorldYear) + "的可确证记录归纳。</color>");
		DrawCultivationRecentMetricGrid(climate);
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("查看修士名录", GUILayout.Width(155f), GUILayout.Height(38f))) SelectCodexTab(8);
		if (GUILayout.Button("查看百年世谱", GUILayout.Width(155f), GUILayout.Height(38f))) SelectCodexTab(12);
		if (GUILayout.Button("查看释修总览", GUILayout.Width(155f), GUILayout.Height(38f))) SelectCodexTab(22);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();

	}

	private void DrawDoctrineRelations(XjCodexCultivationClimate climate)
	{
		DrawSectionTitle("四道形势", "#E6B86E");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#E6B86E");
		GUILayout.Label("<color=grey>只按四个道统查看其对其余三方的态度。固有态度只说明道统立场；当世积怨必须由实际冲突产生，新世界开局不会凭空拥有敌意数值。</color>");

		int columns = ContentWidth < 980f ? 2 : 4;
		float width = Mathf.Max(205f, (ContentWidth - 120f) / columns);
		string[] doctrineIds =
		{
			XjDoctrineIds.ZiJin,
			XjDoctrineIds.FuQi,
			XjDoctrineIds.AncientShi,
			XjDoctrineIds.ModernShi
		};
		for (int i = 0; i < doctrineIds.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawDoctrineSourceCard(climate, doctrineIds[i], width);
			if (i % columns == columns - 1 || i == doctrineIds.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}

		if (climate?.DoctrineRecentEvents != null && climate.DoctrineRecentEvents.Count > 0)
		{
			GUILayout.Space(7f);
			GUILayout.Label("<b><color=#E6B86E>近世积怨</color></b>");
			for (int i = 0; i < climate.DoctrineRecentEvents.Count; i++)
			{
				XjCodexDoctrineConflictEventItem item = climate.DoctrineRecentEvents[i];
				if (item == null) continue;
				GUILayout.Label("<color=grey>" + XjChronology.FormatYear(item.Year) + "</color>　"
					+ Rich(item.SourceDoctrineName) + " → " + Rich(item.TargetDoctrineName)
					+ "　<color=#FFAA66>+" + item.Delta + "</color>　" + Rich(Empty(item.Reason, "异道冲突")));
			}
		}
		GUILayout.EndVertical();
	}

	private void DrawDoctrineSourceCard(XjCodexCultivationClimate climate, string sourceDoctrineId, float width)
	{
		string sourceName = XjDoctrineRules.GetDisplayName(sourceDoctrineId);
		string accent = GetDoctrineAccentColor(sourceDoctrineId);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.MinHeight(188f));
		DrawCardStripe(accent);
		GUILayout.Label("<b><size=19>" + Rich(sourceName) + "</size></b>");
		DrawDoctrineDirectionCompact(FindDoctrineRelation(climate, sourceDoctrineId, XjDoctrineIds.ZiJin), sourceDoctrineId);
		DrawDoctrineDirectionCompact(FindDoctrineRelation(climate, sourceDoctrineId, XjDoctrineIds.FuQi), sourceDoctrineId);
		DrawDoctrineDirectionCompact(FindDoctrineRelation(climate, sourceDoctrineId, XjDoctrineIds.AncientShi), sourceDoctrineId);
		DrawDoctrineDirectionCompact(FindDoctrineRelation(climate, sourceDoctrineId, XjDoctrineIds.ModernShi), sourceDoctrineId);
		GUILayout.FlexibleSpace();
		GUILayout.EndVertical();
	}


	private void DrawDoctrineDirectionCompact(XjCodexDoctrineRelationItem item, string sourceDoctrineId)
	{
		if (item == null || string.Equals(item.TargetDoctrineId, sourceDoctrineId, StringComparison.Ordinal)) return;
		string stance = XjDoctrineRules.GetInherentStance(sourceDoctrineId, item.TargetDoctrineId);
		string color = item.Grievance >= 80 ? "#FF6655"
			: item.Grievance >= 60 ? "#FF9966"
			: item.Grievance >= 40 ? "#FFD37A"
			: item.Grievance >= 20 ? "#CFC7B2" : "#9FC9C0";
		GUILayout.BeginHorizontal();
		GUILayout.Label(Rich(item.TargetDoctrineName), GUILayout.Width(58f));
		GUILayout.Label("<b><color=" + GetDoctrineAccentColor(sourceDoctrineId) + ">" + Rich(stance) + "</color></b>", GUILayout.Width(72f));
		if (item.Grievance > 0)
		{
			GUILayout.Label("<color=" + color + ">当世积怨 " + item.Grievance + " · " + Rich(item.Status) + "</color>");
		}
		else
		{
			GUILayout.Label("<color=grey>当世无怨</color>");
		}
		GUILayout.EndHorizontal();
	}

	private static string GetDoctrineAccentColor(string doctrineId)
	{
		return doctrineId switch
		{
			XjDoctrineIds.ZiJin => "#FFD37A",
			XjDoctrineIds.FuQi => "#74E8FF",
			XjDoctrineIds.AncientShi => "#D9C78A",
			XjDoctrineIds.ModernShi => "#D2B5FF",
			_ => "#E6B86E"
		};
	}

	private static XjCodexDoctrineRelationItem FindDoctrineRelation(
		XjCodexCultivationClimate climate,
		string sourceId,
		string targetId)
	{
		if (climate?.DoctrineRelations == null) return null;
		for (int i = 0; i < climate.DoctrineRelations.Count; i++)
		{
			XjCodexDoctrineRelationItem item = climate.DoctrineRelations[i];
			if (item != null
				&& string.Equals(item.SourceDoctrineId, sourceId, StringComparison.Ordinal)
				&& string.Equals(item.TargetDoctrineId, targetId, StringComparison.Ordinal))
			{
				return item;
			}
		}
		return null;
	}

	private void DrawCultivationPathMetricGrid(XjCodexCultivationClimate climate)
	{
		int columns = ContentWidth < 900f ? 2 : 3;
		float width = Mathf.Max(185f, (ContentWidth - 120f) / columns);
		for (int i = 0; i < 6; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0: DrawOverviewPill("紫金", climate.ZiFuJinDanPath + "人", "#FFD37A", GUILayout.Width(width), GUILayout.Height(80f)); break;
				case 1: DrawOverviewPill("服气", climate.FuQiYangXingPath + "人", "#74E8FF", GUILayout.Width(width), GUILayout.Height(80f)); break;
				case 2: DrawOverviewPill("古释", climate.AncientShi + "人", "#D9C78A", GUILayout.Width(width), GUILayout.Height(80f)); break;
				case 3: DrawOverviewPill("今释", climate.ModernShi + "人", "#D2B5FF", GUILayout.Width(width), GUILayout.Height(80f)); break;
				case 4: DrawOverviewPill("释修总数", climate.ShiPath + "人", "#D9B86C", GUILayout.Width(width), GUILayout.Height(80f)); break;
				case 5: DrawOverviewPill("尚未定路", climate.Unentered + "人", "#888888", GUILayout.Width(width), GUILayout.Height(80f)); break;
			}
			if (i % columns == columns - 1 || i == 5) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
		}
	}

	private void DrawShiClimateMetricGrid(XjCodexCultivationClimate climate)
	{
		int columns = ContentWidth < 900f ? 2 : 4;
		float width = Mathf.Max(150f, (ContentWidth - 120f) / columns);
		for (int i = 0; i < 4; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0: DrawOverviewPill("释修总数", climate.ShiPath + "人", "#D9B86C", GUILayout.Width(width), GUILayout.Height(78f)); break;
				case 1: DrawOverviewPill("古释 · 今释", "古" + climate.AncientShi + " · 今" + climate.ModernShi, "#D9C78A", GUILayout.Width(width), GUILayout.Height(78f)); break;
				case 2: DrawOverviewPill("怜愍 · 摩诃", "怜" + climate.ShiLianMin + " · 摩" + climate.ShiMoHe, "#E8A7FF", GUILayout.Width(width), GUILayout.Height(78f)); break;
				case 3: DrawOverviewPill("法相 · 世尊", "法" + climate.ShiDharmaForm + " · 尊" + climate.ShiWorldHonored, "#FFB77A", GUILayout.Width(width), GUILayout.Height(78f)); break;
			}
			if (i % columns == columns - 1 || i == 3) { GUILayout.FlexibleSpace(); GUILayout.EndHorizontal(); }
		}
	}

	private void DrawCultivationRecentMetricGrid(XjCodexCultivationClimate climate)
	{
		int columns = ContentWidth < 900f ? 2 : ContentWidth < 1260f ? 4 : 7;
		float width = Mathf.Max(130f, (ContentWidth - 120f) / columns);
		for (int i = 0; i < 7; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawOverviewPill("存世真君", climate.ZhenJun + "人", "#FFD37A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 1:
					DrawOverviewPill("新晋真人", climate.NewZhenRen + "人", "#D2B5FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 2:
					DrawOverviewPill("新晋真君", climate.NewZhenJun + "人", "#FFD37A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 3:
					DrawOverviewPill("真人身故", climate.FallenZhenRen + "人", "#FFAA66", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 4:
					DrawOverviewPill("真君身故", climate.FallenZhenJun + "人", "#FF8877", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 5:
					DrawOverviewPill("存世摩诃", climate.ShiMoHe + "人", "#D9B86C", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 6:
					DrawOverviewPill("法相 · 世尊", "法" + climate.ShiDharmaForm + " · 尊" + climate.ShiWorldHonored, "#FFB77A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
			}
			if (i % columns == columns - 1 || i == 6)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void DrawCultivationClimateMetricGrid(XjCodexCultivationClimate climate)
	{
		int columns = ContentWidth < 900f ? 2 : ContentWidth < 1260f ? 3 : 6;
		float width = Mathf.Max(128f, (ContentWidth - 100f) / columns);
		for (int i = 0; i < 6; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			switch (i)
			{
				case 0:
					DrawOverviewPill("天下修士", climate.Total + "人", "#9CD7FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 1:
					DrawOverviewPill("未入道", climate.Unentered + "人", "#888888", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 2:
					DrawOverviewPill("胎息 · 炼气", "胎" + climate.TaiXi + " · 炼" + climate.LianQi, "#A7E08A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 3:
					DrawOverviewPill("筑基 · 黄冠", "筑" + climate.ZhuJi + " · 黄" + climate.HuangGuan, "#B7A7FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 4:
					DrawOverviewPill("存世真人", climate.ZhenRen + "人", "#D2B5FF", GUILayout.Width(width), GUILayout.Height(80f));
					break;
				case 5:
					DrawOverviewPill("存世真君", climate.ZhenJun + "人", "#FFD37A", GUILayout.Width(width), GUILayout.Height(80f));
					break;
			}
			if (i % columns == columns - 1 || i == 5)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}
}
