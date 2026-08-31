using System;
using UnityEngine;
using XuanJianVNext.Data.Codex;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private void DrawFamilyCultivatorButton(XjCodexFamilyItem item, params GUILayoutOption[] options)
	{
		if (item == null)
		{
			return;
		}

		int count = Math.Max(0, item.CultivatorCount);
		bool previousEnabled = GUI.enabled;
		GUI.enabled = previousEnabled && count > 0;
		if (GUILayout.Button("查看修士", options))
		{
			OpenFamilyArchive(item.FamilyId, "在世族人");
		}
		GUI.enabled = previousEnabled;
	}

	private void DrawFamilyCultivatorDetailPage(XjCodexSnapshot snapshot)
	{
		XjCodexFamilyItem family = FindFamily(snapshot, _familyCultivatorDetailFamilyId);
		if (family == null)
		{
			_familyCultivatorDetailFamilyId = 0L;
			DrawFamilyPage(snapshot);
			return;
		}

		DrawPageHeader("家族修士 · " + family.Name, "按境界由高到低列出本族在世修士，集中展示资质、道途、百艺、仙基与家族身份。");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("返回家族诸脉", GUILayout.Width(145f), GUILayout.Height(34f)))
		{
			_familyCultivatorDetailFamilyId = 0L;
			_scrollPosition = Vector2.zero;
			return;
		}
		DrawOverviewPill("修士", family.CultivatorCount.ToString(), "#9CD7FF", GUILayout.Width(120f));
		DrawOverviewPill("高境", "真人" + family.ZiFuCount + " 真君" + family.JinDanCount,
			family.JinDanCount > 0 ? "#FFD37A" : "#B7A7FF", GUILayout.Width(175f));
		DrawOverviewPill("最高境界", Empty(family.HighestRealm, "未载"), "#B7A7FF", GUILayout.Width(160f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);

		if (family.Members == null || family.Members.Count == 0)
		{
			DrawEmptyCard("该家族暂无可显示的在世修士。", "#777777");
			return;
		}

		float availableWidth = ResolveFamilyCultivatorAvailableWidth();
		int columns = ResolveFamilyCultivatorColumns(availableWidth);
		for (int i = 0; i < family.Members.Count; i += columns)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < columns && i + column < family.Members.Count; column++)
			{
				DrawFamilyCultivatorCard(family, family.Members[i + column]);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
		}
		DrawListLimitNotice(family.CultivatorCount, family.Members.Count);
	}

	private float ResolveFamilyCultivatorAvailableWidth()
	{
		return _familyCultivatorDetailFamilyId > 0L
			? Mathf.Max(260f, ContentWidth - 24f)
			: ResolveArchiveDetailWidth(true);
	}

	private static int ResolveFamilyCultivatorColumns(float availableWidth)
	{
		if (availableWidth < 960f) return 1;
		return availableWidth < 1420f ? 2 : 3;
	}

	private void DrawFamilyCultivatorCard(XjCodexFamilyItem family, XjCodexFamilyMemberItem member)
	{
		if (member == null) return;
		string role = ResolveFamilyMemberRole(family, member);
		string color = member.RealmOrder >= 5 ? "#FFD37A"
			: member.RealmOrder >= 4 ? "#B7A7FF"
			: member.RealmOrder >= 3 ? "#9CD7FF" : "#A7E08A";
		float availableWidth = ResolveFamilyCultivatorAvailableWidth();
		int columns = ResolveFamilyCultivatorColumns(availableWidth);
		float cardWidth = Mathf.Max(240f, (availableWidth - ((columns - 1) * 12f)) / columns);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(cardWidth), GUILayout.MinHeight(218f));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>" + Rich(Empty(member.Name, "无名修士")) + "</b>", GUILayout.ExpandWidth(true), GUILayout.Height(30f));
		GUILayout.Label("<color=#FFD37A>" + Rich(role) + "</color>",
			GUILayout.Width(Mathf.Clamp(cardWidth * 0.24f, 76f, 108f)), GUILayout.Height(30f));
		GUILayout.EndHorizontal();
		if (cardWidth < 420f)
		{
			GUILayout.Label("<color=grey>境界</color>　<color=" + color + "><b>" + Rich(XjDisplayNameSanitizer.GameTerm(member.Realm, "境界未载"))
				+ "</b></color>　　<color=grey>道途</color>　<color=#9CD7FF>" + Rich(Empty(member.DaoTu, "未定")) + "</color>");
			GUILayout.Label("<color=grey>资质</color>　<color=#A7E08A>" + Rich(Empty(member.Aptitude, "未测"))
				+ "</color>　　<color=grey>年龄</color>　" + member.Age);
			GUILayout.Label("<color=grey>百艺</color>　<color=#FFD37A>" + Rich(Empty(member.CraftSummary, "无"))
				+ "</color>　　<color=grey>仙基</color>　" + member.XianJiCount);
		}
		else
		{
			GUILayout.BeginHorizontal();
			DrawMiniStat("境界", Empty(member.Realm, "未载"), color, GUILayout.Width(135f));
			DrawMiniStat("道途", Empty(member.DaoTu, "未定"), "#9CD7FF", GUILayout.Width(120f));
			DrawMiniStat("资质", Empty(member.Aptitude, "未测"), "#A7E08A", GUILayout.Width(145f));
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("年龄", member.Age.ToString(), "#CFC7B2", GUILayout.Width(110f));
			DrawMiniStat("百艺", Empty(member.CraftSummary, "无"), "#FFD37A", GUILayout.Width(130f));
			DrawMiniStat("仙基", member.XianJiCount.ToString(), "#A7E08A", GUILayout.Width(105f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		GUILayout.BeginHorizontal();
		GUILayout.FlexibleSpace();
		DrawActorFocusButton("打开角色", member.ActorId, GUILayout.Width(112f), GUILayout.Height(38f));
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private static string ResolveFamilyMemberRole(XjCodexFamilyItem family, XjCodexFamilyMemberItem member)
	{
		if (family == null || member == null)
		{
			return "族人";
		}
		if (member.ActorId > 0L && member.ActorId == family.ClanLeaderActorId) return "家主";
		if (member.ActorId > 0L && member.ActorId == family.HeirActorId) return "继承人";
		if (member.ActorId > 0L && member.ActorId == family.AncestorActorId) return family.FounderTitle;
		if (member.ActorId > 0L && member.ActorId == family.RepresentativeActorId) return "代表人物";
		return "族人";
	}
}
