using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
internal static void ShowSectPeaks(long sectId)
	{
		if (sectId <= 0L) return;
		Show(1);
		if (_instance == null) return;
		_instance._selectedSectId = sectId;
		_instance._sectArchiveView = "山峰门人";
		_instance._sectPeakSelectedPeakId = int.MinValue;
		_instance._sectDetailScrollPosition = Vector2.zero;
		_instance._focusMessage = string.Empty;
		XjCodexSnapshotPublisher.RequestRefresh();
	}

private void DrawSectPeakDetailButton(XjCodexSectItem sect, params GUILayoutOption[] options)
	{
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && sect != null && sect.SectId > 0L && sect.Peaks != null && sect.Peaks.Count > 0;
		if (GUILayout.Button("查看诸峰弟子", options))
		{
			_selectedSectId = sect.SectId;
			_sectPeakSelectedPeakId = SelectDefaultPeakId(sect);
			SelectSectArchiveView("山峰门人");
		}
		GUI.enabled = oldEnabled;
	}

private void DrawSectPeakDetailPage(XjCodexSnapshot snapshot)
	{
		XjCodexSectItem sect = FindSectById(snapshot?.Sects, _sectPeakDetailSectId);
		if (sect == null)
		{
			_sectPeakDetailSectId = 0L;
			_sectPeakSelectedPeakId = int.MinValue;
			DrawEmptyCard("该宗门已不在当前名录中。", "#777777");
			return;
		}

		DrawPageHeader("宗门诸峰 · " + sect.Name, "以峰为卷，峰主居首，弟子列席；弟子卡片压缩为三列，只保留境界、道途、资质、年龄、百艺与仙基。");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("返回宗门格局", GUILayout.Width(145f), GUILayout.Height(34f)))
		{
			_sectPeakDetailSectId = 0L;
			_sectPeakSelectedPeakId = int.MinValue;
			_scrollPosition = Vector2.zero;
			return;
		}
		DrawActorFocusButton("定位宗主", sect.SovereignActorId, GUILayout.Width(100f), GUILayout.Height(34f));
		DrawActorFocusButton("定位开宗者", sect.FounderActorId, GUILayout.Width(115f), GUILayout.Height(34f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
		GUILayout.BeginHorizontal();
		DrawOverviewPill("宗门", sect.Name, "#FFD37A", GUILayout.Width(220f));
		DrawOverviewPill("山门", Empty(sect.CapitalCityName, "未定"), "#9CD7FF", GUILayout.Width(165f));
		DrawOverviewPill("层次", TranslateSectStatus(sect.Status, sect.JinDanCount), sect.JinDanCount > 0 ? "#FFD37A" : "#B7A7FF", GUILayout.Width(145f));
		DrawOverviewPill("山峰", Math.Max(0, sect.PeakCount).ToString(), "#9CD7FF", GUILayout.Width(125f));
		DrawOverviewPill("修士", sect.CultivatorCount.ToString(), "#CFC7B2", GUILayout.Width(125f));
		DrawOverviewPill("高境", "真人" + sect.ZiFuCount + " 真君" + sect.JinDanCount, sect.JinDanCount > 0 ? "#FFD37A" : "#B7A7FF", GUILayout.Width(175f));
		GUILayout.EndHorizontal();
		GUILayout.Space(10f);

		if (sect.Peaks == null || sect.Peaks.Count == 0)
		{
			DrawEmptyCard("尚无峰脉记录。", "#777777");
			return;
		}

		if (_sectPeakSelectedPeakId == int.MinValue || FindPeakById(sect.Peaks, _sectPeakSelectedPeakId) == null)
		{
			_sectPeakSelectedPeakId = SelectDefaultPeakId(sect);
		}
		DrawSectPeakSelector(sect.Peaks);
		XjCodexSectPeakItem selectedPeak = FindPeakById(sect.Peaks, _sectPeakSelectedPeakId);
		if (selectedPeak != null) DrawSectPeakDetail(sect, selectedPeak);
	}

private void DrawSectPeakSelector(IReadOnlyList<XjCodexSectPeakItem> peaks)
	{
		GUILayout.Label("<b>峰位筛选</b>");
		int column = 0;
		GUILayout.BeginHorizontal();
		for (int i = 0; i < peaks.Count; i++)
		{
			XjCodexSectPeakItem peak = peaks[i];
			if (peak == null) continue;
			if (column > 0 && column % 6 == 0)
			{
				GUILayout.EndHorizontal();
				GUILayout.BeginHorizontal();
			}
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = _sectPeakSelectedPeakId == peak.PeakId
				? new Color(0.32f, 0.38f, 0.45f)
				: new Color(0.22f, 0.22f, 0.22f);
			if (GUILayout.Button(peak.PeakName + " " + peak.MemberCount + "人", GUILayout.Width(170f), GUILayout.Height(34f)))
			{
				_sectPeakSelectedPeakId = peak.PeakId;
				_focusMessage = string.Empty;
			}
			GUI.backgroundColor = old;
			column++;
		}
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(8f);
	}

private void DrawSectPeakDetail(XjCodexSectItem sect, XjCodexSectPeakItem peak)
	{
		string color = peak.JinDanCount > 0 ? "#FFD37A" : peak.ZiFuCount > 0 ? "#B7A7FF" : "#A7E08A";
		string masterLabel = peak.PeakId == 0 ? "宗主" : "峰主";
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe(color);
		DrawOrnamentDivider(color, Empty(peak.PeakName, peak.PeakId == 0 ? "主峰" : "未名峰"));
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=21>" + Rich(Empty(peak.PeakName, "未名峰")) + "</size></b>", GUILayout.Width(260f));
		DrawTag("门下 " + Math.Max(0, peak.MemberCount) + " 人", "#CFC7B2");
		DrawTag("真人 " + Math.Max(0, peak.ZiFuCount), "#B7A7FF");
		DrawTag("真君 " + Math.Max(0, peak.JinDanCount), "#FFD37A");
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal(GUI.skin.box);
		DrawMiniStat(masterLabel, Empty(peak.PeakMasterName, "未任命"), string.IsNullOrWhiteSpace(peak.PeakMasterName) ? "#888888" : color, GUILayout.Width(330f));
		GUILayout.FlexibleSpace();
		DrawActorFocusButton("打开" + masterLabel, peak.PeakMasterActorId, GUILayout.Width(105f), GUILayout.Height(34f));
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=grey>峰脉有序，师承有迹；弟子名录只保留判断修行层次所需信息。</color>");
		GUILayout.EndVertical();
		GUILayout.Space(6f);

		GUILayout.Label("<b><color=" + color + ">◇ " + masterLabel + "席</color></b>");
		XjCodexSectPeakMemberItem master = FindPeakMember(peak.Members, peak.PeakMasterActorId);
		if (master != null)
		{
			GUILayout.BeginHorizontal();
			DrawSectPeakMemberCard(master, 560f, 112f);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
		else DrawEmptyCard(string.IsNullOrWhiteSpace(peak.PeakMasterName) ? "本峰尚未任命" + masterLabel + "。" : masterLabel + "记录尚未照入本卷。", "#777777");

		GUILayout.Space(6f);
		GUILayout.Label("<b><color=#A7E08A>◇ 门下弟子</color></b>");
		if (peak.Members == null || peak.Members.Count == 0)
		{
			DrawEmptyCard("本峰暂无可展示弟子。", "#777777");
			return;
		}

		int rendered = 0;
		for (int i = 0; i < peak.Members.Count && rendered < MaxRenderedSectPeakMembers; )
		{
			GUILayout.BeginHorizontal();
			int row = 0;
			while (i < peak.Members.Count && row < 3 && rendered < MaxRenderedSectPeakMembers)
			{
				XjCodexSectPeakMemberItem member = peak.Members[i++];
				if (member == null || member.ActorId == peak.PeakMasterActorId) continue;
				DrawSectPeakMemberCard(member);
				row++;
				rendered++;
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (row > 0) GUILayout.Space(5f);
		}
		int discipleCount = Math.Max(0, peak.MemberCount - (peak.PeakMasterActorId > 0L ? 1 : 0));
		if (rendered == 0) DrawEmptyCard("本峰暂无可展示弟子。", "#777777");
		else if (discipleCount > rendered) GUILayout.Label("<color=grey>本峰另有 " + (discipleCount - rendered) + " 名弟子未展开。</color>");
	}

	private static XjCodexSectPeakMemberItem FindPeakMember(IReadOnlyList<XjCodexSectPeakMemberItem> members, long actorId)
	{
		if (members == null || actorId <= 0L) return null;
		for (int i = 0; i < members.Count; i++) if (members[i] != null && members[i].ActorId == actorId) return members[i];
		return null;
	}

private void DrawSectPeakMemberCard(XjCodexSectPeakMemberItem member, float width = 310f, float height = 108f)
	{
		if (member == null) return;
		string color = ResolvePeakMemberColor(member);
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(width), GUILayout.Height(height));
		DrawCardStripe(color);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><size=17>" + Rich(Empty(member.Name, "未名修士")) + "</size></b>");
		GUILayout.FlexibleSpace();
		GUILayout.Label("<color=#FFD37A>" + Rich(Empty(member.Role, "弟子")) + "</color>", GUILayout.Width(58f));
		GUILayout.EndHorizontal();
		GUILayout.Label("<color=" + color + "><b>" + Rich(XjDisplayNameSanitizer.GameTerm(member.Realm, "境界未载")) + "</b></color>　"
			+ "<color=#9CD7FF>" + Rich(Empty(member.DaoTu, "未定道途")) + "</color>　"
			+ "<color=#A7E08A>" + Rich(Empty(member.Aptitude, "未测资质")) + "</color>");
		GUILayout.Label("年龄 " + Math.Max(0, member.Age) + "　百艺 " + Rich(Empty(member.CraftSummary, "无")) + "　仙基 " + Math.Max(0, member.XianJiCount));
		GUILayout.BeginHorizontal();
		GUILayout.Label("<color=grey>峰籍在册</color>");
		GUILayout.FlexibleSpace();
		DrawActorFocusButton("打开角色", member.ActorId, GUILayout.Width(88f), GUILayout.Height(26f));
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		GUILayout.Space(5f);
	}

private static int SelectDefaultPeakId(XjCodexSectItem sect)
	{
		if (sect?.Peaks == null || sect.Peaks.Count == 0) return int.MinValue;
		for (int i = 0; i < sect.Peaks.Count; i++)
		{
			if (sect.Peaks[i] != null && sect.Peaks[i].PeakId == 0) return 0;
		}
		return sect.Peaks[0]?.PeakId ?? int.MinValue;
	}

private static XjCodexSectPeakItem FindPeakById(IReadOnlyList<XjCodexSectPeakItem> peaks, int peakId)
	{
		if (peaks == null) return null;
		for (int i = 0; i < peaks.Count; i++)
		{
			XjCodexSectPeakItem peak = peaks[i];
			if (peak != null && peak.PeakId == peakId) return peak;
		}
		return null;
	}

private static string ResolvePeakMemberColor(XjCodexSectPeakMemberItem member)
	{
		if (member == null) return "#CFC7B2";
		if (string.Equals(member.Role, "宗主", StringComparison.Ordinal) || string.Equals(member.Role, "真君", StringComparison.Ordinal)) return "#FFD37A";
		if (string.Equals(member.Role, "峰主", StringComparison.Ordinal) || string.Equals(member.Role, "真人", StringComparison.Ordinal)) return "#B7A7FF";
		return "#A7E08A";
	}
}
