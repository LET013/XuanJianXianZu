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
			_familyCultivatorDetailFamilyId = item.FamilyId;
			_familyChronicleDetailFamilyId = 0L;
			_familyWarehouseDetailFamilyId = 0L;
			_scrollPosition = Vector2.zero;
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
		DrawOverviewPill("高境", "紫府" + family.ZiFuCount + " 真君" + family.JinDanCount,
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

		for (int i = 0; i < family.Members.Count; i += 3)
		{
			GUILayout.BeginHorizontal();
			for (int column = 0; column < 3 && i + column < family.Members.Count; column++)
			{
				DrawFamilyCultivatorCard(family, family.Members[i + column]);
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
		}
		DrawListLimitNotice(family.CultivatorCount, family.Members.Count);
	}

	private void DrawFamilyCultivatorCard(XjCodexFamilyItem family, XjCodexFamilyMemberItem member)
	{
		if (member == null) return;
		string role = ResolveFamilyMemberRole(family, member);
		string color = member.RealmOrder >= 5 ? "#FFD37A"
			: member.RealmOrder >= 4 ? "#B7A7FF"
			: member.RealmOrder >= 3 ? "#9CD7FF" : "#A7E08A";
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(455f), GUILayout.MinHeight(218f));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b>" + Rich(Empty(member.Name, "无名修士")) + "</b>", GUILayout.Width(280f), GUILayout.Height(30f));
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=#FFD37A>" + Rich(role) + "</color>", GUILayout.Width(100f), GUILayout.Height(30f));
		GUILayout.EndHorizontal();
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
