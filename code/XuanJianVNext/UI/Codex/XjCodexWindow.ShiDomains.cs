using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static readonly string[] ShiDomainViews = { "古今释", "应土", "金地", "三十二天", "位次", "旃檀林", "七相" };
	private string _shiDomainView = "古今释";

	private void DrawShiDomainPage()
	{
		DrawPageHeader("释修总览",
			"总览古释、今释、应土金地、三十二天与位次承载；旃檀林另列今释高位归返、摩诃席位和北世尊金地。 ");
		DrawShiDomainViewBar();
		int year = Math.Max(1, XjYearTracker.CurrentYear > 0
			? XjYearTracker.CurrentYear : World.world?.map_stats?.year ?? 1);
		XjShiWorldRegistry.EnsureYear(year);
		IReadOnlyList<XjShiDomainRecord> domains = XjShiDomainState.ReadSnapshot(year);
		if (string.Equals(_shiDomainView, "古今释", StringComparison.Ordinal))
		{
			DrawShiTraditionSummary(domains, year);
			return;
		}
		if (string.Equals(_shiDomainView, "三十二天", StringComparison.Ordinal))
		{
			DrawShiHeavenSummary(domains);
			return;
		}
		if (string.Equals(_shiDomainView, "位次", StringComparison.Ordinal))
		{
			DrawShiPositionSummary(domains, year);
			return;
		}
		if (string.Equals(_shiDomainView, "旃檀林", StringComparison.Ordinal))
		{
			DrawShiYouTanLinSummary(domains, year);
			return;
		}
		if (string.Equals(_shiDomainView, "七相", StringComparison.Ordinal))
		{
			DrawShiLineageSummary(domains, year);
			return;
		}

		bool showJinDi = string.Equals(_shiDomainView, "金地", StringComparison.Ordinal);
		string requiredType = showJinDi ? XjShiDomainTypeIds.JinDi : XjShiDomainTypeIds.YingTu;
		int shown = 0;
		for (int i = 0; i < domains.Count; i++)
		{
			XjShiDomainRecord domain = domains[i];
			if (domain == null) continue;
			bool typeMatches = showJinDi
				? string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
					|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal)
				: string.Equals(domain.DomainType, requiredType, StringComparison.Ordinal);
			if (!typeMatches) continue;
			DrawShiDomainCard(domain);
			shown++;
		}
		if (shown == 0)
		{
			DrawEmptyCard(requiredType == XjShiDomainTypeIds.JinDi
				? "当世尚无已登记金地。" : "当世尚无已登记应土。", "#777777");
		}
	}

	private void DrawShiDomainViewBar()
	{
		GUILayout.BeginHorizontal(GUI.skin.box);
		for (int i = 0; i < ShiDomainViews.Length; i++)
		{
			string view = ShiDomainViews[i];
			bool selected = string.Equals(_shiDomainView, view, StringComparison.Ordinal);
			Color old = GUI.backgroundColor;
			if (selected) GUI.backgroundColor = new Color(0.45f, 0.64f, 0.72f, 1f);
			if (GUILayout.Button(view, GUILayout.Height(32f))) _shiDomainView = view;
			GUI.backgroundColor = old;
		}
		GUILayout.EndHorizontal();
	}

	private static void DrawShiDomainCard(XjShiDomainRecord domain)
	{
		string visibilityColor = string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
			? "#A7E08A"
			: string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Unstable, StringComparison.Ordinal)
				? "#FFD37A"
				: string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
					? "#777777" : "#B7A7FF";
		bool ancientLegacy = string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal)
			&& string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		bool concealedJinDi = (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			&& string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal);
		string visibleDomainName = concealedJinDi ? "隐世金地" : XjShiDomainCatalog.GetDomainDisplayName(domain);
		string visibleLineage = concealedJinDi ? "法脉不可测" : XjShiCatalog.GetLineageDisplay(domain.LineageId);
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(visibilityColor);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=20>" + Rich(visibleDomainName)
			+ " · " + Rich(visibleLineage) + "</size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<b><color=" + visibilityColor + ">"
			+ Rich(XjShiDomainCatalog.GetVisibilityDisplay(domain.Visibility)) + "</color></b>",
			GUILayout.Width(120f));
		GUILayout.EndHorizontal();
		if (concealedJinDi)
		{
			GUILayout.Label(ancientLegacy
				? "<color=#B7A7FF>此为隐世古释遗金地，旧应身与所藏法意皆不可测；偶有因缘显世，方能被当世修士感得。</color>"
				: "<color=#B7A7FF>此金地已隐世，除勾连此地的摩诃外，主人、位次与内部承载皆不可测算。</color>");
			GUILayout.EndVertical();
			return;
		}
		if (ancientLegacy && domain.AncientLegacyResponseAwakened > 0)
		{
			GUILayout.Label("<b><color=#D2B5FF>应身已醒</color></b>　古释旧应身由寂转灵，此遗地自此常显于世。");
		}
		else if (ancientLegacy && !string.Equals(domain.AncientLegacyLastEventId, XjAncientShiLegacyEventIds.None, StringComparison.Ordinal))
		{
			string legacyEvent = XjAncientShiLegacyEventIds.GetDisplay(domain.AncientLegacyLastEventId);
			if (!string.IsNullOrWhiteSpace(legacyEvent))
				GUILayout.Label("<color=#D9C78A>古释遗地 · " + Rich(legacyEvent) + "</color>");
		}
		GUILayout.BeginHorizontal();
		string ownerDisplay = ancientLegacy && !string.IsNullOrWhiteSpace(domain.AncientLegacyFormerOwnerName)
			? domain.AncientLegacyFormerOwnerName.Trim() + "（已故）"
			: ResolveShiActorName(domain.OwnerActorId);
		DrawMiniStat(domain.IsNorthWorldHonoredFragment > 0 ? "庙主"
			: string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				? "自证者" : "主人",
			ownerDisplay, "#E8D69B", GUILayout.Width(260f));
		DrawMiniStat("承载增长", Math.Max(0, domain.Growth).ToString(), "#A7E08A", GUILayout.Width(150f));
		if (domain.AbsorbedJinDiCount > 0)
			DrawMiniStat("已吞并金地", domain.AbsorbedJinDiCount.ToString(), "#D9B36C", GUILayout.Width(170f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (domain.IsNorthWorldHonoredFragment > 0)
		{
			GUILayout.Label("<color=#D9C78A>应身来源："
				+ Rich(XjShiHeavenCatalog.GetHeavenDisplayName(domain.SourceHeavenIndex))
				+ " · " + Rich(XjShiHeavenCatalog.GetHeavenMeaning(domain.SourceHeavenIndex))
				+ " · 第" + Math.Max(0, domain.SourceHeavenFragmentOrdinal) + "片"
				+ " · 共" + Math.Max(0, domain.SourceHeavenFragmentCount) + "片</color>");
		}
		GUILayout.BeginHorizontal();
		if (domain.IsNorthWorldHonoredFragment > 0)
		{
			DrawMiniStat("金地归属", domain.OwnerActorId > 0L ? "庙主已持有" : "无主",
				"#D9C78A", GUILayout.Width(170f));
			DrawMiniStat("法相根基", "已占" + Math.Max(0, domain.OccupiedDharmaFormPositions)
				+ " · 容" + Math.Max(0, domain.DharmaFormPositionCapacity), "#74E8FF", GUILayout.Width(170f));
		}
		else if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
			|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
		{
			DrawMiniStat("法相位", "已占" + Math.Max(0, domain.OccupiedDharmaFormPositions)
				+ " · 容" + Math.Max(0, domain.DharmaFormPositionCapacity), "#74E8FF", GUILayout.Width(170f));
		}
		if (domain.IsNorthWorldHonoredFragment <= 0)
		{
			DrawMiniStat("摩诃承载", "已占" + Math.Max(0, domain.OccupiedMoHePositions)
				+ " · 容" + Math.Max(0, domain.MoHePositionCapacity), "#FFD37A", GUILayout.Width(170f));
			DrawMiniStat("怜愍承载", "已占" + Math.Max(0, domain.OccupiedLianMinPositions)
				+ " · 容" + Math.Max(0, domain.LianMinPositionCapacity), "#E8A7FF", GUILayout.Width(170f));
		}
		if ((string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
			|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			&& domain.DharmaFormCandidateCount > 0)
		{
			DrawMiniStat("候位法相", Math.Max(0, domain.DharmaFormCandidateCount).ToString(),
				"#74E8FF", GUILayout.Width(170f));
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal))
		{
			GUILayout.Label("<color=#999999>已并入：</color>"
				+ Rich(XjShiDomainState.ResolveDomainDisplayName(domain.AbsorbedByDomainId)));
		}
		if (!string.IsNullOrWhiteSpace(XjShiDomainCatalog.GetMigrationDisplay(domain.LegacyMigrationState)))
		{
			GUILayout.Label("<color=#FFD37A>"
				+ Rich(XjShiDomainCatalog.GetMigrationDisplay(domain.LegacyMigrationState)) + "</color>");
		}
		GUILayout.EndVertical();
	}


	private static void DrawShiTraditionSummary(IReadOnlyList<XjShiDomainRecord> domains, int year)
	{
		int ancient = XjShiWorldRegistry.GetLiveTraditionCount(XjShiTraditionIds.Ancient, year);
		int modern = XjShiWorldRegistry.GetLiveTraditionCount(XjShiTraditionIds.Modern, year);
		int ancientDharma = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Ancient, XjShiRealmIds.DharmaForm, year);
		int ancientWorldHonored = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Ancient, XjShiRealmIds.WorldHonored, year);
		int modernLianMin = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Modern, XjShiRealmIds.LianMin, year);
		int modernMoHe = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Modern, XjShiRealmIds.MoHe, year);
		int modernDharma = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Modern, XjShiRealmIds.DharmaForm, year);
		int modernWorldHonored = XjShiWorldRegistry.GetLiveTraditionRealmCount(XjShiTraditionIds.Modern, XjShiRealmIds.WorldHonored, year);
		int ancientJinDi = 0;
		if (domains != null)
		{
			for (int i = 0; i < domains.Count; i++)
			{
				XjShiDomainRecord domain = domains[i];
				if (domain == null) continue;
				if (string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
					&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)) ancientJinDi++;
			}
		}

		GUILayout.BeginHorizontal();
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#D9C78A");
		GUILayout.Label("<b><size=21><color=#D9C78A>古释 · " + ancient + "人</color></size></b>");
		GUILayout.Label("<color=grey>自修证己 · 宏愿持行 · 应身金地</color>");
		GUILayout.BeginHorizontal();
		DrawMiniStat("法相", ancientDharma.ToString(), "#D2B5FF", GUILayout.Width(115f));
		DrawMiniStat("世尊", ancientWorldHonored.ToString(), "#FFB77A", GUILayout.Width(115f));
		DrawMiniStat("自证金地", ancientJinDi.ToString(), "#E0B44D", GUILayout.Width(145f));
		GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
		GUILayout.Label("古释循僧侣、法师、法相、世尊而进，不设怜愍与摩诃；重自证与宏愿，不依释土轮回，高位修证与应身、金地相连。");
		GUILayout.EndVertical();
		GUILayout.Space(8f);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#D2B5FF");
		GUILayout.Label("<b><size=21><color=#D2B5FF>今释 · " + modern + "人</color></size></b>");
		GUILayout.Label("<color=grey>依止释土 · 真灵归返 · 七相分流</color>");
		GUILayout.BeginHorizontal();
		DrawMiniStat("怜愍", modernLianMin.ToString(), "#E8A7FF", GUILayout.Width(105f));
		DrawMiniStat("摩诃", modernMoHe.ToString(), "#FFD37A", GUILayout.Width(105f));
		DrawMiniStat("法相", modernDharma.ToString(), "#D2B5FF", GUILayout.Width(105f));
		DrawMiniStat("世尊", modernWorldHonored.ToString(), "#FFB77A", GUILayout.Width(105f));
		GUILayout.FlexibleSpace(); GUILayout.EndHorizontal();
		GUILayout.Label("今释依释土与七相而修，循法师、怜愍、摩诃向法相与世尊推进；高位修士与承载、归返和位次关系更深。");
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#9FC9C0");
		GUILayout.Label("<b>古今释概览</b>");
		GUILayout.Label("古释与今释同属释修而修证路数不同：古释重自证与宏愿，今释重释土与七相。本页只录当世法脉、高位规模与承载关系。");
		GUILayout.EndVertical();

		DrawAncientShiTempleSummary();
	}

	private static void DrawAncientShiTempleSummary()
	{
		IReadOnlyList<XjAncientShiTempleRecord> temples = XjAncientShiTempleSystem.ReadActiveTemples();
		DrawSectionTitle("古释寺庙", "#D9C78A");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D9C78A");
		GUILayout.Label("<color=grey>古释寺庙是古释自己的清净共修之所，与仙修宗门分开：只记古释，不收今释、紫金或服气修士；不设山峰与宗门大阵，底蕴另看愿基、法藏与应身余泽。</color>");
		int count = temples?.Count ?? 0;
		if (count <= 0)
		{
			GUILayout.Label("<color=grey>当世尚无古释立寺。</color>");
			GUILayout.EndVertical();
			return;
		}
		int shown = Math.Min(count, 12);
		int columns = count <= 1 ? 1 : 2;
		for (int i = 0; i < shown; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			XjAncientShiTempleRecord temple = temples[i];
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
			DrawCardStripe("#E4BE72");
			GUILayout.Label("<b><size=19>" + Rich(string.IsNullOrWhiteSpace(temple?.Name) ? "未名古寺" : temple.Name) + "</size></b>");
			GUILayout.Label("<color=grey>" + Rich(string.IsNullOrWhiteSpace(temple?.CityName) ? "所在未载" : temple.CityName)
				+ " · 古释成员 " + Math.Max(0, temple?.LivingMemberCount ?? 0) + "人</color>");
			GUILayout.Label("住持：" + Rich(string.IsNullOrWhiteSpace(temple?.AbbotName) ? "暂缺" : temple.AbbotName)
				+ "　·　本愿：" + Rich(XjAncientShiVowCatalog.GetShortDisplay(temple?.PrincipalVowId)));
			GUILayout.BeginHorizontal();
			DrawMiniStat("愿基", DescribeAncientTempleFoundation(temple?.VowFoundation ?? 0), "#F0CC75", GUILayout.Width(120f));
			DrawMiniStat("法藏", DescribeAncientTempleFoundation(temple?.DharmaArchive ?? 0), "#9CD7FF", GUILayout.Width(120f));
			DrawMiniStat("应身余泽", DescribeAncientTempleFoundation(temple?.ResponseLegacy ?? 0), "#D2B5FF", GUILayout.Width(135f));
			if ((temple?.LegacyJinDiDomainIds?.Count ?? 0) > 0)
				DrawMiniStat("祖师遗地", (temple.LegacyJinDiDomainIds.Count).ToString() + "处", "#C7B6E8", GUILayout.Width(110f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			if (i % columns == columns - 1 || i == shown - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		if (count > shown) GUILayout.Label("<color=grey>另有 " + (count - shown) + " 座古寺未展开。</color>");
		GUILayout.EndVertical();
	}

	private static string DescribeAncientTempleFoundation(int value)
	{
		int safe = Math.Max(0, value);
		if (safe <= 0) return "未显";
		if (safe < 250) return "初具";
		if (safe < 500) return "渐成";
		if (safe < 800) return "充盈";
		return "深厚";
	}

	private static void DrawShiHeavenSummary(IReadOnlyList<XjShiDomainRecord> domains)
	{
		int totalRecorded = 0;
		int absorbed = 0;
		int manifest = 0;
		int ownedFragments = 0;
		int completed = 0;
		for (int i = 0; i < domains.Count; i++)
		{
			XjShiDomainRecord domain = domains[i];
			if (domain == null || domain.IsNorthWorldHonoredFragment <= 0) continue;
			totalRecorded++;
			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) absorbed++;
			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) manifest++;
			if (domain.OwnerActorId > 0L) ownedFragments++;
		}
		for (int heavenIndex = 0; heavenIndex < XjShiHeavenCatalog.TotalHeavens; heavenIndex++)
		{
			long owner = 0L;
			bool oneOwner = true;
			int owned = 0;
			int required = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			for (int i = 0; i < domains.Count; i++)
			{
				XjShiDomainRecord domain = domains[i];
				if (domain == null || domain.IsNorthWorldHonoredFragment <= 0
					|| domain.SourceHeavenIndex != heavenIndex || domain.OwnerActorId <= 0L) continue;
				owned++;
				if (owner <= 0L) owner = domain.OwnerActorId;
				else if (owner != domain.OwnerActorId) oneOwner = false;
			}
			if (oneOwner && owner > 0L && owned >= required) completed++;
		}

		DrawSectionTitle("北世尊三十二应身", "#E4BE72");
		GUILayout.BeginHorizontal();
		DrawMiniStat("三十二天", XjShiHeavenCatalog.TotalHeavens.ToString(), "#F0CC75", GUILayout.Width(170f));
		DrawMiniStat("金地碎片", "已录" + totalRecorded + " · 总" + XjShiHeavenCatalog.TotalFragments, "#E4BE72", GUILayout.Width(190f));
		DrawMiniStat("旃檀林吸纳", "已并入" + absorbed + " · 固定" + XjShiHeavenCatalog.ZhantanlinFragmentCount, "#9FC9C0", GUILayout.Width(190f));
		DrawMiniStat("现世显化", manifest.ToString(), "#A7E08A", GUILayout.Width(170f));
		DrawMiniStat("已有归属", ownedFragments.ToString(), "#FFD37A", GUILayout.Width(170f));
		DrawMiniStat("已重组天", completed.ToString(), "#D2B5FF", GUILayout.Width(170f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>三十二应身各有专名与独立法意，分属无量、无边、无央、无等四类。五重天碎为三地，其余二十七重天碎为两地；只有同源碎片归于同一庙主法相，才能重组原本的一重天。</color>");

		for (int heavenIndex = 0; heavenIndex < XjShiHeavenCatalog.TotalHeavens; heavenIndex++)
		{
			int required = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			int recorded = 0;
			int absorbedCount = 0;
			int visibleCount = 0;
			int ownedCount = 0;
			long commonOwner = 0L;
			bool oneOwner = true;
			for (int i = 0; i < domains.Count; i++)
			{
				XjShiDomainRecord domain = domains[i];
				if (domain == null || domain.IsNorthWorldHonoredFragment <= 0
					|| domain.SourceHeavenIndex != heavenIndex) continue;
				recorded++;
				if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) absorbedCount++;
				if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) visibleCount++;
				if (domain.OwnerActorId <= 0L) continue;
				ownedCount++;
				if (commonOwner <= 0L) commonOwner = domain.OwnerActorId;
				else if (commonOwner != domain.OwnerActorId) oneOwner = false;
			}
			bool reformed = oneOwner && commonOwner > 0L && ownedCount >= required;
			string state = reformed ? "已重组三十二天"
				: ownedCount > 0 ? "聚合中 · 已得" + ownedCount + " · 需" + required
				: absorbedCount > 0 ? "入旃檀林 · 已有" + absorbedCount + " · 需" + required
				: visibleCount > 0 ? "已显世"
				: "碎片隐世";
			string color = reformed ? "#D2B5FF"
				: ownedCount > 0 ? "#FFD37A"
				: absorbedCount > 0 ? "#9FC9C0"
				: "#777777";
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>" + Rich(XjShiHeavenCatalog.GetHeavenDisplayName(heavenIndex)) + "</b>");
			GUILayout.FlexibleSpace();
			GUILayout.Label("<color=" + color + ">" + Rich(state) + "</color>", GUILayout.Width(180f));
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=#D8CDAA>"
				+ Rich(XjShiHeavenCatalog.GetHeavenMeaning(heavenIndex))
				+ "</color>");
			GUILayout.Label("<color=grey>金地已录 " + recorded + "处 · 成天需 " + required + "处"
				+ "　入林 " + absorbedCount + "　现世 " + visibleCount + "　持有 " + ownedCount
				+ (reformed ? "　重组者 " + Rich(ResolveShiActorName(commonOwner)) : string.Empty)
				+ "</color>");
			GUILayout.EndVertical();
		}
	}

	private void DrawShiPositionSummary(IReadOnlyList<XjShiDomainRecord> domains, int year)
	{
		int dharmaFormUsed = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.DharmaForm, year);
		int worldHonoredUsed = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.WorldHonored, year);
		int dharmaFormCapacity = 0;
		int dharmaFormCandidates = 0;
		int moHeUsed = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.MoHe, year);
		int moHeReserved = 0;
		if (XjShiDomainState.TryGetMoHePositionUsage(
			XjShiDomainCatalog.ZhantanlinDomainId, out _, out int pendingMoHe, out _))
			moHeReserved = Math.Max(0, pendingMoHe);
		int moHeOccupied = Math.Min(108, Math.Max(0, moHeUsed) + Math.Max(0, moHeReserved));
		int moHeOpen = Math.Max(0, 108 - moHeOccupied);
		int lianMinUsed = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.LianMin, year);
		int lianMinCapacity = 0;
		int manifest = 0;
		int unstable = 0;
		int hidden = 0;
		int absorbed = 0;
		for (int i = 0; i < domains.Count; i++)
		{
			XjShiDomainRecord domain = domains[i];
			if (domain == null) continue;
			if (!string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal))
				dharmaFormCapacity += Math.Max(0, domain.DharmaFormPositionCapacity);
			dharmaFormCandidates += Math.Max(0, domain.DharmaFormCandidateCount);
			lianMinCapacity += Math.Max(0, domain.LianMinPositionCapacity);
			if (domain.Visibility == XjShiDomainVisibilityIds.Manifest) manifest++;
			else if (domain.Visibility == XjShiDomainVisibilityIds.Unstable) unstable++;
			else if (domain.Visibility == XjShiDomainVisibilityIds.Absorbed) absorbed++;
			else hidden++;
		}

		DrawSectionTitle("今释高位位序", "#74E8FF");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=21><color=#FFD37A>摩诃一百零八席</color></size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=#FFD37A><b>已占 " + moHeOccupied + " / 108</b></color>", GUILayout.Width(135f));
		GUILayout.EndHorizontal();
		DrawInlineBar(moHeOccupied / 108f, "#FFD37A", GUILayout.Height(14f), GUILayout.ExpandWidth(true));
		GUILayout.Space(4f);
		DrawShiStatGrid(
			new[] { "在世摩诃", "轮回预留", "尚余空席", "位序规则" },
			new[] { moHeUsed.ToString(), moHeReserved.ToString(), moHeOpen.ToString(), "真灵留席" },
			new[] { "#FFD37A", "#E4BE72", "#A7E08A", "#D9C78A" },
			4);
		GUILayout.EndVertical();

		GUILayout.BeginHorizontal();
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#74E8FF");
		GUILayout.Label("<b><color=#74E8FF>法相与世尊</color></b>");
		DrawShiStatGrid(
			new[] { "在世法相", "在世世尊", "庙主金地", "待证法相" },
			new[] { dharmaFormUsed.ToString(), worldHonoredUsed.ToString(), dharmaFormCapacity.ToString(), dharmaFormCandidates.ToString() },
			new[] { "#74E8FF", "#D2B5FF", "#D9C78A", "#9CD7FF" },
			4);
		GUILayout.Label("<color=#888888>法相、世尊已经超出摩诃一百零八席；法相之位依自身庙主金地而立，不继续占用摩诃席。</color>");
		GUILayout.EndVertical();
		GUILayout.EndHorizontal();

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#E8A7FF");
		GUILayout.Label("<b><color=#E8A7FF>怜愍承载</color></b>");
		DrawShiStatGrid(
			new[] { "在世怜愍", "可承载位", "承载余量" },
			new[] { lianMinUsed.ToString(), lianMinCapacity.ToString(), Math.Max(0, lianMinCapacity - lianMinUsed).ToString() },
			new[] { "#E8A7FF", "#D2B5FF", "#A7E08A" },
			3);
		GUILayout.Label("<color=#888888>怜愍依附座主真灵；座主轮回时等待同一真灵归返，只有座主真灵真正俱灭才会断绝直属承载。</color>");
		GUILayout.EndVertical();

		DrawSectionTitle("承载地显隐", "#9FC9C0");
		DrawShiStatGrid(
			new[] { "显世", "不稳定", "隐世", "并入释土" },
			new[] { manifest.ToString(), unstable.ToString(), hidden.ToString(), absorbed.ToString() },
			new[] { "#A7E08A", "#FFD37A", "#B7A7FF", "#777777" },
			4);
	}

	private void DrawShiYouTanLinSummary(IReadOnlyList<XjShiDomainRecord> domains, int year)
	{
		int modernTotal = XjShiWorldRegistry.GetLiveTraditionCount(XjShiTraditionIds.Modern, year);
		int lianMin = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.LianMin, year);
		int liveMoHe = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.MoHe, year);
		int dharmaForms = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.DharmaForm, year);
		int worldHonored = XjShiWorldRegistry.GetLiveRealmCount(XjShiRealmIds.WorldHonored, year);
		int candidates = 0;
		int absorbedFragments = 0;
		int ownedFragments = 0;
		int manifestFragments = 0;
		XjShiDomainRecord zhantanlin = null;
		for (int i = 0; i < domains.Count; i++)
		{
			XjShiDomainRecord domain = domains[i];
			if (domain == null) continue;
			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal))
			{
				zhantanlin = domain;
			}
			candidates += Math.Max(0, domain.DharmaFormCandidateCount);
			if (domain.IsNorthWorldHonoredFragment <= 0) continue;
			bool inZhantanlin = string.Equals(domain.AbsorbedByDomainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)
				|| string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal);
			if (inZhantanlin)
			{
				absorbedFragments++;
				if (domain.OwnerActorId > 0L) ownedFragments++;
			}
			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) manifestFragments++;
		}
		bool placed = zhantanlin != null && zhantanlin.MapRadius >= XjZhantanlinSystem.MinimumRadius;
		int reservedMoHe = 0;
		if (XjShiDomainState.TryGetMoHePositionUsage(
			XjShiDomainCatalog.ZhantanlinDomainId, out _, out int pendingMoHe, out _))
			reservedMoHe = Math.Max(0, pendingMoHe);
		int occupiedMoHe = Math.Min(108, liveMoHe + reservedMoHe);

		DrawSectionTitle("旃檀林 · 今释中央释土", "#74E8FF");
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(placed ? "#74E8FF" : "#FFD37A");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=22>旃檀林</size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label(placed ? "<color=#A7E08A><b>已开辟 · 常驻当世</b></color>" : "<color=#FFD37A><b>尚未开辟</b></color>", GUILayout.Width(190f));
		GUILayout.EndHorizontal();
		if (placed)
		{
			GUILayout.Label("<color=#CFCFCF>山河坐标　" + zhantanlin.MapCenterX + "，" + zhantanlin.MapCenterY
				+ "　·　固定半径 " + zhantanlin.MapRadius + "　·　今释高位归返与北世尊金地共同系于此土。</color>");
		}
		else
		{
			GUILayout.Label("<color=#FFD37A>旃檀林尚未于此世显化。低位释修照常修行与轮回；摩诃及以上高位归返会守住真灵与原位，静待释土真正开辟。</color>");
		}
		GUILayout.EndVertical();

		DrawSectionTitle("今释在世", "#9FC9C0");
		DrawShiStatGrid(
			new[] { "今释总数", "怜愍", "摩诃", "法相", "世尊" },
			new[] { modernTotal.ToString(), lianMin.ToString(), liveMoHe.ToString(), dharmaForms.ToString(), worldHonored.ToString() },
			new[] { "#9FC9C0", "#E8A7FF", "#FFD37A", "#74E8FF", "#D2B5FF" },
			5);

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#FFD37A");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><color=#FFD37A>摩诃一百零八席</color></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<b>实占 " + liveMoHe + "　·　轮回留席 " + reservedMoHe + "　·　空席 " + Math.Max(0, 108 - occupiedMoHe) + "</b>");
		GUILayout.EndHorizontal();
		DrawInlineBar(occupiedMoHe / 108f, "#FFD37A", GUILayout.Height(14f), GUILayout.ExpandWidth(true));
		GUILayout.EndVertical();

		DrawSectionTitle("北世尊金地", "#D9C78A");
		DrawShiStatGrid(
			new[] { "已纳入林", "已有庙主", "现世碎片", "待证法相" },
			new[] { absorbedFragments + "片", ownedFragments + "片", manifestFragments + "片", candidates.ToString() },
			new[] { "#D9C78A", "#FFD37A", "#A7E08A", "#74E8FF" },
			4);
		GUILayout.Label("<color=#888888>金地是法相自身的位格根基；并入旃檀林只改变其所在释土，不抹去庙主权属，也不把法相位变成旃檀林共有席位。</color>");

		DrawSectionTitle("释土法则", "#B7A7FF");
		GUILayout.BeginHorizontal();
		DrawShiRuleCard("高位归返", "今释摩诃及以上肉身以旃檀林为归处；轮回期间原摩诃席位继续保留。", "#FFD37A");
		DrawShiRuleCard("绝对止战", "身在林中的角色不得彼此攻伐；此规则只约束释土内部，不替外界角色改写行动。", "#9FC9C0");
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		DrawShiRuleCard("法相根基", "法相由庙主金地承位，旃檀林本体不提供共享法相位，也不占用摩诃席。", "#74E8FF");
		DrawShiRuleCard("山河归属", "旃檀林自成释土，不列入世俗国土；外围只作为领域边界，不参与城镇封邑。", "#D2B5FF");
		GUILayout.EndHorizontal();
	}

	private void DrawShiStatGrid(string[] labels, string[] values, string[] colors, int maxColumns, float height = 66f)
	{
		if (labels == null || values == null || colors == null) return;
		int count = Math.Min(labels.Length, Math.Min(values.Length, colors.Length));
		if (count <= 0) return;
		float usable = Mathf.Max(280f, ContentWidth - 42f);
		int columns = Math.Max(1, Math.Min(maxColumns, count));
		while (columns > 1 && (usable - (columns - 1) * 8f) / columns < 145f) columns--;
		float width = Mathf.Max(125f, (usable - (columns - 1) * 8f) / columns);
		for (int i = 0; i < count; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawMiniStat(labels[i], values[i], colors[i], GUILayout.Width(width), GUILayout.Height(height));
			if (i % columns == columns - 1 || i == count - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private static void DrawShiRuleCard(string title, string body, string color)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.MinHeight(92f));
		DrawCardStripe(color);
		GUILayout.Label("<b><color=" + color + ">" + Rich(title) + "</color></b>");
		GUILayout.Label("<color=#CFCFCF>" + Rich(body) + "</color>");
		GUILayout.EndVertical();
	}

	private static void DrawShiLineageSummary(IReadOnlyList<XjShiDomainRecord> domains, int year)
	{
		string[] lineages =
		{
			XjShiLineageIds.GreatDesire, XjShiLineageIds.Wrath, XjShiLineageIds.DharmaAdmiration,
			XjShiLineageIds.Discipline, XjShiLineageIds.GoodJoy, XjShiLineageIds.Compassion,
			XjShiLineageIds.Emptiness, XjShiLineageIds.ModernUnassigned, XjShiLineageIds.NorthWorldHonored
		};
		DrawSectionTitle("法脉承载", "#B7A7FF");
		for (int l = 0; l < lineages.Length; l++)
		{
			string lineage = lineages[l];
			int domainCount = 0;
			int growth = 0;
			int moHe = XjShiWorldRegistry.GetLiveLineageRealmCount(lineage, XjShiRealmIds.MoHe, year);
			int lianMin = XjShiWorldRegistry.GetLiveLineageRealmCount(lineage, XjShiRealmIds.LianMin, year);
			int manifest = 0;
			int absorbedCount = 0;
			for (int i = 0; i < domains.Count; i++)
			{
				XjShiDomainRecord domain = domains[i];
				if (domain == null || !string.Equals(domain.LineageId, lineage, StringComparison.Ordinal)) continue;
				domainCount++;
				growth += Math.Max(0, domain.Growth);
				if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) manifest++;
				else if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) absorbedCount++;
			}
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#B7A7FF");
			GUILayout.Label("<b>" + Rich(XjShiCatalog.GetLineageDisplay(lineage)) + "</b>");
			GUILayout.Label("<color=#D8CDAA>" + Rich(XjShiCatalog.GetLineageIdeaDisplay(lineage)) + "</color>");
			GUILayout.Label("<color=#B7A7FF>分支：" + Rich(XjShiLineagePolicy.GetBranchFunctionDisplay(lineage)) + "</color>");
			GUILayout.Label("<color=grey>倾向：" + Rich(XjShiLineagePolicy.GetAiTendencyDisplay(lineage)) + "</color>");
			GUILayout.Label("<color=grey>承载地 " + domainCount + "　显世 " + manifest
				+ "　已并入 " + absorbedCount + "　释土增长 " + growth + "　在世摩诃 " + moHe + "　在世怜愍 " + lianMin + "</color>");
			GUILayout.EndVertical();
		}
	}

	private static string ResolveShiActorName(long actorId)
	{
		if (actorId <= 0L) return "无";
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null ? actor.getName() : "已失联#" + actorId;
	}
}
