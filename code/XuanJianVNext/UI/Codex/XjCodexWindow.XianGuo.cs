using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private long _selectedXianGuoKingdomId;
	private string _mingYangStrategyView = "仙国法";

	private void DrawXianGuoPage()
	{
		DrawPageHeader("明阳经略", "明阳之道既可由帝统集众成法，也可能成为上修长期布局的棋眼。此卷分录仙国法与明阳局。");
		DrawMingYangStrategyPicker();
		if (string.Equals(_mingYangStrategyView, "明阳局", StringComparison.Ordinal))
		{
			DrawMingYangSchemePage();
			return;
		}
		DrawXianGuoLawPage();
	}

	private void DrawMingYangStrategyPicker()
	{
		const float gap = 8f;
		float usable = Mathf.Max(360f, ContentWidth - 34f);
		float buttonWidth = Mathf.Max(170f, (usable - gap) * 0.5f);
		GUILayout.BeginHorizontal(GUI.skin.box, GUILayout.ExpandWidth(true));
		string[] views = { "仙国法", "明阳局" };
		for (int i = 0; i < views.Length; i++)
		{
			if (i > 0) GUILayout.Space(gap);
			string view = views[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_mingYangStrategyView, view, StringComparison.Ordinal)
				? new Color(0.55f, 0.43f, 0.21f, 1f)
				: new Color(0.24f, 0.25f, 0.28f, 1f);
			if (GUILayout.Button(view, GUILayout.Width(buttonWidth), GUILayout.Height(38f)))
			{
				_mingYangStrategyView = view;
				_scrollPosition = Vector2.zero;
			}
			GUI.backgroundColor = old;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(7f);
	}

	private void DrawMingYangSchemePage()
	{
		IReadOnlyList<XjMingYangSchemeSummary> schemes = XjMingShuSchemeSystem.ReadActiveSummaries();
		int active = schemes?.Count ?? 0;
		int stage1 = 0, stage2 = 0, stage3 = 0;
		if (schemes != null)
		{
			for (int i = 0; i < schemes.Count; i++)
			{
				switch (schemes[i].Stage)
				{
					case 1: stage1++; break;
					case 2: stage2++; break;
					case 3: stage3++; break;
				}
			}
		}

		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#D9C78A");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=23><color=#F0CC75>明 阳 局</color></size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(active > 0 ? "当世有局" : "当世无局", active > 0 ? "#A7E08A" : "#8F8F8F");
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#CFC7B2>上修观命落子，借世家、人事、传承与时势推一名明阳命数子入局；局成与否，最终仍由局中之人自己的道途与破境决定。</color>");
		GUILayout.EndVertical();

		DrawSectionTitle("当世局势", "#D9C78A");
		float usable = Mathf.Max(360f, ContentWidth - 48f);
		int columns = usable >= 900f ? 4 : usable >= 520f ? 2 : 1;
		const float metricGap = 10f;
		float width = Mathf.Max(185f, (usable - metricGap * (columns - 1)) / columns);
		string[] labels = { "正在做局", "识命", "引势", "推局" };
		string[] values = { active.ToString(), stage1.ToString(), stage2.ToString(), stage3.ToString() };
		string[] colors = { "#D9C78A", "#9CD7FF", "#A7E08A", "#F0B66E" };
		for (int i = 0; i < labels.Length; i += columns)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < columns; column++)
			{
				if (column > 0) GUILayout.Space(metricGap);
				int index = i + column;
				if (index < labels.Length) DrawOverviewPill(labels[index], values[index], colors[index], GUILayout.Width(width), GUILayout.Height(70f));
				else GUILayout.Space(width);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

		DrawSectionTitle("局法纲要", "#D9C78A");
		float ruleWidth = Mathf.Max(270f, (usable - 10f) * 0.5f);
		GUILayout.BeginHorizontal();
		DrawMingYangRuleCard("主局者", "必须是当世真实存在的金丹、真君羽士或道胎；仙国借境、神丹挂靠等虚位不能主持。", "#F0CC75", ruleWidth);
		GUILayout.Space(10f);
		DrawMingYangRuleCard("局眼", "只取真正的明阳命数子。上修以自身气数压住局眼，长期观察、移势与授益。", "#D9C78A", ruleWidth);
		GUILayout.EndHorizontal();
		GUILayout.Space(7f);
		GUILayout.BeginHorizontal();
		DrawMingYangRuleCard("三重局势", "识命定眼，引势聚缘，推局压向真人与帝统节点；阶段越深，布局越实。", "#9CD7FF", ruleWidth);
		GUILayout.Space(10f);
		DrawMingYangRuleCard("破局", "改道、身死、超时或主局者失格都会断局；命数子踏入真人后还可能回看气数，识破布局。", "#FFAA66", ruleWidth);
		GUILayout.EndHorizontal();

		DrawOrnamentDivider("#D9C78A", "三 重 局 势");
		const float stageGap = 10f;
		float stageWidth = Mathf.Max(210f, (usable - stageGap * 2f) / 3f);
		GUILayout.BeginHorizontal();
		DrawMingYangStageCard("识命", "观其命数与道途，锁定局眼；上修只在暗处落下第一着。", "#9CD7FF", stageWidth);
		GUILayout.Space(stageGap);
		DrawMingYangStageCard("引势", "牵动棋子身边的人事、机缘与修行条件，使明阳之势逐渐汇聚。", "#A7E08A", stageWidth);
		GUILayout.Space(stageGap);
		DrawMingYangStageCard("推局", "把世家、传承和时势压上棋盘，推动棋子冲击真人并等待收束。", "#F0B66E", stageWidth);
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		if (active <= 0)
		{
			GUILayout.Space(6f);
			DrawEmptyCard("当世暂无正在推进的明阳局。只有真实高境上修生出做局之意，并找到合适的明阳命数子后，棋局才会在此入录。", "#777777");
			return;
		}

		DrawOrnamentDivider("#D9C78A", "局 中 人 物");
		for (int i = 0; i < schemes.Count; i++)
		{
			XjMingYangSchemeSummary summary = schemes[i];
			string stripe = summary.Stage >= 3 ? "#F0B66E" : summary.Stage == 2 ? "#A7E08A" : "#9CD7FF";
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
			DrawCardStripe(stripe);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=20><color=" + stripe + ">" + Rich(summary.StageName) + "</color></size></b>　" + Rich(summary.TargetName));
			GUILayout.FlexibleSpace();
			DrawTag("已行 " + summary.YearsRunning + " 年", "#D9C78A");
			GUILayout.EndHorizontal();
			DrawMingYangStageRail(summary.Stage, usable);
			GUILayout.Space(7f);

			float personWidth = Mathf.Max(280f, (usable - 10f) * 0.5f);
			GUILayout.BeginHorizontal();
			DrawMingYangPersonCard("局眼 · 命数子", summary.TargetName, summary.TargetRealm, summary.TargetDaoTu, summary.TargetFamilyName, "#9CD7FF", personWidth);
			GUILayout.Space(10f);
			DrawMingYangPersonCard("主局 · 上修", summary.PatronName, summary.PatronRealm, summary.PatronDaoTu, summary.PatronFamilyName, "#F0CC75", personWidth);
			GUILayout.EndHorizontal();

			GUILayout.Space(7f);
			GUILayout.BeginHorizontal();
			DrawMingYangNarrativeCard("当前局势", ResolveMingYangStageNarrative(summary.Stage, summary.TargetRealm), stripe, personWidth);
			GUILayout.Space(10f);
			DrawMingYangNarrativeCard("当前助势", ResolveMingYangSupportDisplay(summary.Stage), "#A7E08A", personWidth);
			GUILayout.EndHorizontal();
			GUILayout.Label("<color=#8F8F8F>" + XjChronology.FormatYear(summary.StartedYear) + "开局　·　最近推进" + XjChronology.FormatYear(summary.LastAdvanceYear) + "　·　至今" + summary.YearsRunning + "年</color>");
			GUILayout.BeginHorizontal();
			DrawActorFocusButton("定位命数子", summary.TargetActorId, GUILayout.Width(118f), GUILayout.Height(32f));
			DrawActorFocusButton("定位主局者", summary.PatronActorId, GUILayout.Width(118f), GUILayout.Height(32f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.EndVertical();
			GUILayout.Space(8f);
		}
	}

	private void DrawMingYangStageCard(string title, string body, string color, float width)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(126f));
		DrawCardStripe(color);
		GUILayout.Label("<b><size=19><color=" + color + ">" + Rich(title) + "</color></size></b>");
		GUILayout.Space(3f);
		GUILayout.Label("<color=#CFCFCF>" + Rich(body) + "</color>");
		GUILayout.FlexibleSpace();
		GUILayout.EndVertical();
	}

	private void DrawMingYangRuleCard(string title, string body, string color, float width)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.MinHeight(100f));
		DrawCardStripe(color);
		GUILayout.Label("<b><color=" + color + ">" + Rich(title) + "</color></b>");
		GUILayout.Label("<color=#CFCFCF>" + Rich(body) + "</color>");
		GUILayout.EndVertical();
	}

	private void DrawMingYangPersonCard(string role, string name, string realm, string daoTu, string family, string color, float width)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.MinHeight(92f));
		DrawCardStripe(color);
		GUILayout.Label("<b><color=" + color + ">" + Rich(role) + "</color></b>");
		GUILayout.Label("<b><size=19>" + Rich(Empty(name, "未名")) + "</size></b>　<color=#AAAAAA>[" + Rich(Empty(realm, "未载")) + "]</color>");
		GUILayout.Label("<color=#CFCFCF>道途　" + Rich(Empty(daoTu, "未定")) + "　　家族　" + Rich(Empty(family, "未载")) + "</color>");
		GUILayout.EndVertical();
	}

	private void DrawMingYangNarrativeCard(string title, string body, string color, float width)
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.MinHeight(88f));
		GUILayout.Label("<b><color=" + color + ">" + Rich(title) + "</color></b>");
		GUILayout.Label("<color=#CFCFCF>" + Rich(body) + "</color>");
		GUILayout.EndVertical();
	}

	private void DrawMingYangStageRail(int stage, float usable)
	{
		string[] names = { "识命", "引势", "推局" };
		string[] colors = { "#9CD7FF", "#A7E08A", "#F0B66E" };
		const float gap = 6f;
		float width = Mathf.Max(90f, (usable - gap * 2f) / 3f);
		GUILayout.BeginHorizontal();
		for (int i = 0; i < names.Length; i++)
		{
			if (i > 0) GUILayout.Space(gap);
			bool reached = stage >= i + 1;
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(32f));
			GUILayout.Label(reached
				? "<b><color=" + colors[i] + ">◆ " + names[i] + "</color></b>"
				: "<color=#666666>◇ " + names[i] + "</color>");
			GUILayout.EndVertical();
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private static string ResolveMingYangSupportDisplay(int stage)
	{
		return stage switch
		{
			1 => "初步扶持：修持与破境开始受到上修暗助。",
			2 => "引势加深：机缘、资源与修行条件已被明显调动。",
			3 => "强力推局：上修正集中资源把命数子推向真人与帝统节点。",
			_ => "局势未明。"
		};
	}

	private static string ResolveMingYangStageNarrative(int stage, string targetRealm)
	{
		string realm = (targetRealm ?? string.Empty).Trim();
		if (stage >= 3 && (realm.IndexOf("紫府", StringComparison.Ordinal) >= 0 || realm.IndexOf("真人", StringComparison.Ordinal) >= 0))
			return "命数子已经踏入真人层次，棋局进入回看与收束节点；下一次推进可能成局，也可能被其识破。";
		return stage switch
		{
			1 => "上修已经认定局眼，以观察与暗中护持为主。",
			2 => "棋局开始实质引动，机缘、人事和传承向明阳之路聚拢。",
			3 => "棋局进入主动推演阶段，长期积累的资源与影响正压向高境节点。",
			_ => "局势未明。"
		};
	}

	private void DrawXianGuoLawPage()
	{
		IReadOnlyList<XjXianGuoSummary> summaries = XjXianGuoSystem.ReadActiveSummaries();
		if (summaries == null || summaries.Count == 0)
		{
			DrawEmptyCard("当世尚无帝明阳仙国法统。明阳修士真正承帝统、立国行法后，国朝才会在此入录。", "#777777");
			return;
		}

		int fakeCount = 0;
		int officialCount = 0;
		int strongestXuan = 0;
		for (int i = 0; i < summaries.Count; i++)
		{
			XjXianGuoSummary summary = summaries[i];
			if (summary.CourtFakeJinDanActive) fakeCount++;
			strongestXuan = Math.Max(strongestXuan, Math.Min(summary.NationalPotential, summary.NationalFortune));
			IReadOnlyList<XjXianGuoOfficialSummary> officials = XjXianGuoSystem.ReadCurrentOfficialSummaries(summary.KingdomId);
			if (officials != null)
			{
				for (int officialIndex = 0; officialIndex < officials.Count; officialIndex++)
				{
					if (officials[officialIndex].ActorId > 0L) officialCount++;
				}
			}
		}

		DrawXianGuoOverviewStrip(summaries.Count, fakeCount, officialCount, strongestXuan);
		GUILayout.Space(8f);
		XjXianGuoSummary selected = ResolveSelectedXianGuo(summaries);
		DrawXianGuoSelector(summaries, selected);
		GUILayout.Space(8f);
		DrawXianGuoFullDetail(in selected);
	}

	private void DrawXianGuoOverviewStrip(int dynastyCount, int fakeCount, int officialCount, int strongestXuan)
	{
		float usable = Mathf.Max(300f, ContentWidth - 48f);
		int columns = usable >= 1080f ? 4 : usable >= 520f ? 2 : 1;
		float cardWidth = Mathf.Max(190f, (usable - 14f * (columns - 1)) / columns);
		string[] labels = { "仙朝在世", "众玄归一", "承命官员", "最盛国玄" };
		string[] values = { dynastyCount.ToString(), fakeCount + "朝", officialCount.ToString(), strongestXuan.ToString() };
		string[] colors = { "#F0CC75", "#FFD37A", "#D6BE86", "#9CD7FF" };
		for (int i = 0; i < labels.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawOverviewPill(labels[i], values[i], colors[i], GUILayout.Width(cardWidth), GUILayout.Height(72f));
			if (i % columns == columns - 1 || i == labels.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private XjXianGuoSummary ResolveSelectedXianGuo(IReadOnlyList<XjXianGuoSummary> summaries)
	{
		XjXianGuoSummary first = summaries[0];
		for (int i = 0; i < summaries.Count; i++)
		{
			if (summaries[i].KingdomId == _selectedXianGuoKingdomId) return summaries[i];
		}
		_selectedXianGuoKingdomId = first.KingdomId;
		return first;
	}

	private void DrawXianGuoSelector(IReadOnlyList<XjXianGuoSummary> summaries, XjXianGuoSummary selected)
	{
		DrawSectionTitle("当世仙朝", "#D6BE86");
		float usable = Mathf.Max(300f, ContentWidth - 56f);
		int columns = usable >= 1220f ? 4 : usable >= 900f ? 3 : usable >= 540f ? 2 : 1;
		float width = Mathf.Max(230f, (usable - 12f * (columns - 1)) / columns);
		for (int i = 0; i < summaries.Count; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			XjXianGuoSummary summary = summaries[i];
			bool active = summary.KingdomId == selected.KingdomId;
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = active ? new Color(0.52f, 0.41f, 0.20f, 1f) : new Color(0.23f, 0.23f, 0.24f, 1f);
			string name = Empty(summary.DynastyName, "未名仙朝");
			string sovereign = Empty(summary.SovereignName, summary.SuccessionPending ? "王统待定" : "未载");
			string status = summary.SuccessionPending ? "王统待定" : Empty(summary.Status, "仙国行法");
			string sovereignTitle = ResolveSovereignTitle(in summary);
			string label = "<b><size=18>" + Rich(name) + "</size></b>　<color=#D6BE86>" + Rich(status) + "</color>\n"
				+ "<size=12><color=#CFCFCF>" + sovereignTitle + " " + Rich(sovereign) + "　　" + summary.CityCount + "城　　国玄 "
				+ Math.Min(summary.NationalPotential, summary.NationalFortune) + "</color></size>";
			if (GUILayout.Button(label, GUILayout.Width(width), GUILayout.Height(62f)))
			{
				_selectedXianGuoKingdomId = summary.KingdomId;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == summaries.Count - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void DrawXianGuoFullDetail(in XjXianGuoSummary summary)
	{
		int currentYear = Math.Max(1, XjYearTracker.CurrentYear);
		int dynastyAge = Math.Max(0, currentYear - summary.FoundedYear);
		int effective = Math.Min(summary.NationalPotential, summary.NationalFortune);
		string sovereignTitle = ResolveSovereignTitle(in summary);
		string capitalTitle = ResolveCapitalTitle(in summary);

		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#F0CC75");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=25><color=#F0CC75>" + Rich(Empty(summary.DynastyName, "未名仙朝")) + "</color></size></b>");
		GUILayout.FlexibleSpace();
		DrawTag(Empty(summary.Status, "仙国行法"), summary.SuccessionPending ? "#FFAA66" : "#A7E08A");
		GUILayout.EndHorizontal();
		GUILayout.Label("<b>" + sovereignTitle + "</b>　" + Rich(Empty(summary.SovereignName, summary.SuccessionPending ? "王统待定" : "未载"))
			+ "　　<b>国朝</b>　第" + summary.DynastyGeneration + "世　　<b>立朝</b>　" + dynastyAge + "年　　<b>安定</b>　" + Math.Max(0, summary.StableYears) + "年");
		if (summary.SuccessionPending)
			GUILayout.Label("<color=#FFAA66>王统一时悬而未定，旧契仍守国朝，待帝统真正重定。</color>");
		GUILayout.EndVertical();

		GUILayout.Space(6f);
		DrawOrnamentDivider("#F0CC75", "国 朝 正 统");
		DrawXianGuoStateMetrics(in summary, effective);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#9FC9C0");
		GUILayout.Label("<b><color=#9FC9C0>治世助破</color></b>　炼气 " + FormatXianGuoBonus(summary, XjRealmIds.LianQi)
			+ "　　筑基 " + FormatXianGuoBonus(summary, XjRealmIds.ZhuJi)
			+ "　　紫府 " + FormatXianGuoBonus(summary, XjRealmIds.ZiFu)
			+ "　　金丹 " + FormatXianGuoBonus(summary, XjRealmIds.JinDan));
		GUILayout.Label("<color=#888888>国势主治世根基，国运主法统承载；两者共同决定百官可承国命与修士受国朝扶助的强弱。</color>");
		GUILayout.EndVertical();

		DrawOrnamentDivider("#FFD37A", "众 玄 归 一");
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe(summary.CourtFakeJinDanActive ? "#A7E08A" : "#8B8066");
		GUILayout.Label(summary.CourtFakeJinDanActive
			? "<b><color=#A7E08A>众玄已合</color></b>　国朝已足以开假金丹命额，当前承载品秩 <b>" + summary.CourtBorrowedCombatGrade + "</b>；随国朝继续壮大可逐层扩展，群臣至多三席。"
			: "<b><color=#CFC7B2>众玄未合</color></b>　城土、臣民、国势、国运与国祚尚未同时足以承载众玄归一。");
		GUILayout.EndVertical();
		DrawXianGuoRequirementGrid(in summary, dynastyAge);
		GUILayout.Label("<color=#888888>众玄只使重臣得承更厚国命，不改人物本命与真实金丹位序；官去、朝易或法统断绝，国命归朝，借境随之散去。</color>");

		DrawOrnamentDivider("#D6BE86", "仙 朝 百 官　承 国 之 命");
		DrawXianGuoCourt(in summary);

		DrawOrnamentDivider("#F0B66E", "帝 明 阳 六 象");
		DrawXianGuoManifestationGrid(in summary);
		GUILayout.Label("<color=#888888>六象由治世、征伐、君臣秩序、宗室相残与谋逆等真实国事留痕，并非逐年凭空增长。</color>");

		GUILayout.Space(7f);
		GUILayout.BeginHorizontal();
		DrawActorFocusButton("定位" + sovereignTitle, summary.SovereignActorId, GUILayout.Width(96f), GUILayout.Height(34f));
		DrawCityFocusButton("定位" + capitalTitle, summary.CapitalCityId, GUILayout.Width(96f), GUILayout.Height(34f));
		if (GUILayout.Button("仙国律典", GUILayout.Width(96f), GUILayout.Height(34f)))
		{
			_mechanicsGuideView = "仙国法";
			BeginContextNavigation(20, "返回明阳经略");
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}

	private void DrawXianGuoStateMetrics(in XjXianGuoSummary summary, int effective)
	{
		float usable = Mathf.Max(300f, ContentWidth - 56f);
		int columns = usable >= 1040f ? 4 : usable >= 520f ? 2 : 1;
		float width = Mathf.Max(200f, (usable - 14f * (columns - 1)) / columns);
		string[] labels = { "国势", "国运", "有效国玄", "城土臣民" };
		string[] values = { summary.NationalPotential.ToString(), summary.NationalFortune.ToString(), effective.ToString(), summary.CityCount + "城　" + summary.Population + "众" };
		string[] colors = { "#F0CC75", "#A7E08A", "#9CD7FF", "#D6BE86" };
		for (int i = 0; i < labels.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawOverviewPill(labels[i], values[i], colors[i], GUILayout.Width(width), GUILayout.Height(72f));
			if (i % columns == columns - 1 || i == labels.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void DrawXianGuoRequirementGrid(in XjXianGuoSummary summary, int dynastyAge)
	{
		string[] labels = { "城土", "臣民", "国势", "国运", "国祚" };
		int[] current = { summary.CityCount, summary.Population, summary.NationalPotential, summary.NationalFortune, dynastyAge };
		int[] required = { XjXianGuoSystem.FakeJinDanMinimumCities, XjXianGuoSystem.FakeJinDanMinimumPopulation, XjXianGuoSystem.FakeJinDanMinimumPotential, XjXianGuoSystem.FakeJinDanMinimumFortune, XjXianGuoSystem.FakeJinDanMinimumDynastyAge };
		string[] unit = { "城", "众", "", "", "年" };
		float usable = Mathf.Max(300f, ContentWidth - 56f);
		int columns = usable >= 1120f ? 5 : usable >= 760f ? 3 : usable >= 520f ? 2 : 1;
		float width = Mathf.Max(170f, (usable - 12f * (columns - 1)) / columns);
		for (int i = 0; i < labels.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			bool reached = current[i] >= required[i];
			string color = reached ? "#A7E08A" : "#FFAA66";
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(74f));
			DrawCardStripe(color);
			GUILayout.Label("<b>" + labels[i] + "</b>　<color=" + color + "><b>" + (reached ? "已足" : "未足") + "</b></color>");
			GUILayout.Label("<size=16>" + current[i] + unit[i] + " / " + required[i] + unit[i] + "</size>");
			GUILayout.EndVertical();
			if (i % columns == columns - 1 || i == labels.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private void DrawXianGuoCourt(in XjXianGuoSummary summary)
	{
		IReadOnlyList<XjXianGuoOfficialSummary> officials = XjXianGuoSystem.ReadCurrentOfficialSummaries(summary.KingdomId);
		if (officials == null || officials.Count == 0)
		{
			DrawEmptyCard("仙朝百官尚未敕定；下一次国朝结算会依中枢与诸城空缺补授玄秩。", "#777777");
			return;
		}
		List<XjXianGuoOfficialSummary> central = new List<XjXianGuoOfficialSummary>();
		List<XjXianGuoOfficialSummary> local = new List<XjXianGuoOfficialSummary>();
		for (int i = 0; i < officials.Count; i++)
		{
			if (officials[i].CityId > 0L) local.Add(officials[i]); else central.Add(officials[i]);
		}
		float usable = Mathf.Max(300f, ContentWidth - 56f);
		int centralColumns = usable >= 820f ? 2 : 1;
		DrawXianGuoOfficialGroup("中枢六官", central, in summary, centralColumns, usable);
		if (local.Count > 0)
		{
			GUILayout.Space(7f);
			int localColumns = usable >= 820f ? 2 : 1;
			DrawXianGuoOfficialGroup("诸城持玄使", local, in summary, localColumns, usable);
		}

		int borrowedZiFu = 0;
		int fakeJinDan = 0;
		for (int i = 0; i < officials.Count; i++)
		{
			string projection = officials[i].Projection ?? string.Empty;
			if (string.Equals(projection, "持玄紫府", StringComparison.Ordinal)) borrowedZiFu++;
			if (string.Equals(projection, "仙国假金丹", StringComparison.Ordinal)) fakeJinDan++;
		}
		int sovereignTier = 0;
		if (summary.SovereignActorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(summary.SovereignActorId, out Actor sovereign)
			&& sovereign?.data != null && sovereign.isAlive())
		{
			sovereignTier = XjXianGuoSystem.ResolveImperialSovereignTier(sovereign);
		}
		int ziFuLimit = XjXianGuoCourtSystem.ResolveBorrowedZiFuLimitForSovereign(
			summary.CityCount, summary.Population, summary.NationalPotential, summary.NationalFortune, sovereignTier);
		int fakeLimit = summary.CourtFakeJinDanActive
			? XjXianGuoCourtSystem.ResolveBorrowedFakeJinDanLimitForSovereign(
				summary.CityCount, summary.Population, summary.NationalPotential, summary.NationalFortune, sovereignTier)
			: 0;
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#B7A7FF");
		GUILayout.Label("<b><color=#B7A7FF>国命承载</color></b>　持玄紫府 <b>" + borrowedZiFu + " / " + ziFuLimit
			+ "</b>　　仙国假金丹 <b>" + fakeJinDan + " / " + fakeLimit + "</b>");
		GUILayout.Label("<color=#9A9A9A>百官以官身承一朝之命：持玄紫府随国朝承载逐层开放，群臣最多九席；帝明阳真正踏入金丹后，众玄归一方可再开假金丹命额，群臣最多三席。借境始终不得高过帝明阳本人的真实大境界。</color>");
		GUILayout.EndVertical();

		GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true));
		DrawCardStripe("#8B8066");
		GUILayout.Label("<color=#9A9A9A>中枢高官受敕为【国之重臣】，可承帝统国命；诸城持玄使只承一城之命。所承国命不写入人物本命，也不能用于正常修行破境；官去则命归，朝亡则法散。借境仅以对应真修境界八成底盘托举，不另叠官品攻防。</color>");
		GUILayout.EndVertical();
	}

	private void DrawXianGuoOfficialGroup(string title, IReadOnlyList<XjXianGuoOfficialSummary> officials, in XjXianGuoSummary summary, int columns, float availableWidth)
	{
		if (officials == null || officials.Count == 0) return;
		GUILayout.Label("<b><size=18><color=#D6BE86>" + title + "</color></size></b>");
		float width = Mathf.Max(220f, (availableWidth - 12f * Math.Max(0, columns - 1)) / Math.Max(1, columns));
		for (int i = 0; i < officials.Count; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			DrawXianGuoOfficialCard(officials[i], in summary, width);
			if (i % columns == columns - 1 || i == officials.Count - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Space(6f);
			}
		}
	}

	private void DrawXianGuoOfficialCard(XjXianGuoOfficialSummary official, in XjXianGuoSummary summary, float width)
	{
		bool vacant = official.ActorId <= 0L;
		string projection = Empty(official.Projection, "虚位");
		string stripe = vacant ? "#746B59" : (official.HeavyMinister ? "#F0CC75" : "#9CD7FF");
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(vacant ? 94f : 142f));
		DrawCardStripe(stripe);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=16><color=#F0CC75>" + Rich(Empty(official.OfficeName, "仙官")) + "</color></size></b>　<color=#B9B09A>" + Rich(Empty(official.XuanRank, "玄秩")) + "</color>");
		GUILayout.FlexibleSpace();
		if (official.CityId > 0L) GUILayout.Label("<color=#9CD7FF>" + Rich(Empty(official.CityName, "未名城")) + "</color>", GUILayout.ExpandWidth(false));
		GUILayout.EndHorizontal();

		if (vacant)
		{
			GUILayout.Label("<color=#8F8F8F><b>虚席</b>　待国朝敕补</color>");
			GUILayout.Label("<color=#B59A65>官位未授，无人承此处国命。</color>");
		}
		else
		{
			string identity = official.HeavyMinister ? "【国之重臣】" : "【仙朝持玄官】";
			string identityColor = official.HeavyMinister ? "#F0CC75" : "#9CD7FF";
			GUILayout.Label("<b><color=" + identityColor + ">" + identity + "</color></b>　<b><size=17>"
				+ Rich(Empty(official.ActorName, "未名仙官")) + "</size></b>", GUILayout.Height(22f));

			GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.Height(48f));
			GUILayout.Label("<size=12><color=#8F968C>实境</color>　<color=#9CD7FF>" + Rich(Empty(official.RealRealm, "未载"))
				+ "</color>　　<color=#8F968C>道行</color>　<color=#F0CC75>" + Rich(projection) + "</color></size>");
			GUILayout.Label("<size=12><color=#8F968C>本命</color>　<color=#CFCFCF>" + official.TrueFate
				+ "</color>　　<color=#8F968C>承命</color>　<color=#F0CC75>" + (official.NationalFate > 0 ? "+" + official.NationalFate : "待敕")
				+ "</color>　　<color=#8F968C>持玄</color>　<color=#A7E08A>" + official.EffectiveFate
				+ "</color>　　<color=#8F968C>品秩</color>　<color=#D6BE86>" + (official.Grade > 0 ? official.Grade.ToString() : "未载")
				+ "</color></size>");
			GUILayout.EndVertical();
		}

		GUILayout.BeginHorizontal();
		if (!vacant) DrawActorFocusButton("人物", official.ActorId, GUILayout.Width(64f), GUILayout.Height(27f));
		if (official.CityId > 0L) DrawCityFocusButton("城镇", official.CityId, GUILayout.Width(64f), GUILayout.Height(27f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void DrawXianGuoManifestationGrid(in XjXianGuoSummary summary)
	{
		string[] labels = { "天光", "紫焰", "君臣", "帝皇", "父子相杀", "谋逆" };
		int[] values = { summary.TianGuang, summary.ZiYan, summary.JunChen, summary.DiHuang, summary.FuZiXiangSha, summary.MouNi };
		string[] colors = { "#F0CC75", "#B7A7FF", "#9CD7FF", "#FFD37A", "#FFAA66", "#FF8877" };
		float usable = Mathf.Max(300f, ContentWidth - 56f);
		int columns = usable >= 1180f ? 6 : usable >= 820f ? 3 : usable >= 520f ? 2 : 1;
		float width = Mathf.Max(145f, (usable - 12f * (columns - 1)) / columns);
		for (int i = 0; i < labels.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string state = FormatManifestationState(values[i]);
			DrawOverviewPill(labels[i], state + "　" + Math.Max(0, values[i]), colors[i], GUILayout.Width(width), GUILayout.Height(68f));
			if (i % columns == columns - 1 || i == labels.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
	}

	private static string FormatManifestationState(int value)
	{
		value = Math.Max(0, value);
		if (value <= 0) return "未显";
		if (value < 10) return "微痕";
		if (value < 25) return "渐显";
		if (value < 50) return "盛";
		return "极盛";
	}

	// 天下舆图只保留疆域辨识和简略法统摘要，完整朝廷档案统一进入“明阳经略”页。
	private void DrawXianGuoAtlasOverview()
	{
		IReadOnlyList<XjXianGuoSummary> summaries = XjXianGuoSystem.ReadActiveSummaries();
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#F0CC75");
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><color=#F0CC75>明阳经略</color></b>　" + (summaries?.Count ?? 0) + "朝在世");
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("查看完整法统", GUILayout.Width(125f), GUILayout.Height(30f)))
			BeginContextNavigation(19, "返回天下舆图");
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=#888888>此卷只辨仙朝疆域、帝都与国属；国势国运、众玄归一、百官玄秩和六象另入明阳经略正卷。</color>");
		GUILayout.EndVertical();
	}

	private void DrawXianGuoAtlasDetail(XjCodexCityItem city, in XjXianGuoSummary summary)
	{
		int effective = Math.Min(summary.NationalPotential, summary.NationalFortune);
		string sovereignTitle = ResolveSovereignTitle(in summary);
		string capitalTitle = ResolveCapitalTitle(in summary);
		DrawCardStripe("#F0CC75");
		GUILayout.Label("<b><size=21><color=#F0CC75>" + Rich(Empty(summary.DynastyName, "未名仙朝")) + "</color></size></b>");
		GUILayout.Label("<color=grey>所选城镇　" + Rich(city.Name) + "　　山河坐标 " + city.TileX + "，" + city.TileY + "</color>");
		GUILayout.Label("<b>" + sovereignTitle + "</b>　" + Rich(Empty(summary.SovereignName, summary.SuccessionPending ? "王统待定" : "未载")));
		GUILayout.Label("<b>国势 / 国运</b>　" + summary.NationalPotential + " / " + summary.NationalFortune + "　　<b>国玄</b>　" + effective);
		GUILayout.Label("<b>城土臣民</b>　" + summary.CityCount + "城 / " + summary.Population + "众");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("查看明阳经略", GUILayout.Width(125f), GUILayout.Height(34f)))
		{
			_selectedXianGuoKingdomId = summary.KingdomId;
			BeginContextNavigation(19, "返回天下舆图");
		}
		DrawActorFocusButton("定位" + sovereignTitle, summary.SovereignActorId, GUILayout.Width(96f), GUILayout.Height(34f));
		DrawCityFocusButton("定位" + capitalTitle, summary.CapitalCityId, GUILayout.Width(96f), GUILayout.Height(34f));
		GUILayout.EndHorizontal();
	}

	private static string ResolveSovereignTitle(in XjXianGuoSummary summary)
	{
		if (summary.SovereignActorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(summary.SovereignActorId, out Actor actor)
			&& actor != null
			&& XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan)
		{
			return "帝君";
		}
		return "国王";
	}

	private static string ResolveCapitalTitle(in XjXianGuoSummary summary)
	{
		return ResolveSovereignTitle(in summary) == "帝君" ? "帝都" : "王都";
	}

	private static string FormatXianGuoBonus(in XjXianGuoSummary summary, string realmId)
	{
		float value = XjXianGuoSystem.ResolveBreakthroughSuccessBonus(in summary, realmId);
		if (value <= 0.0001f) return "无助";
		if (value < 0.025f) return "微助";
		if (value < 0.060f) return "小助";
		if (value < 0.120f) return "显助";
		return "厚助";
	}
}
