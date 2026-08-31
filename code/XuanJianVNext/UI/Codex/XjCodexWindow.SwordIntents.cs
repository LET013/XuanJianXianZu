using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.WeaponArt;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private const int SwordIntentPageSize = 24;
	private int _swordIntentPage;
	private int _swordIntentSummaryVersion = -1;
	private int _swordIntentTypeCount;

	private void DrawSwordIntentPage(XjCodexSnapshot snapshot)
	{
		IReadOnlyList<XjSwordIntentArchiveRecord> intents = snapshot?.SwordIntents ?? Array.Empty<XjSwordIntentArchiveRecord>();
		DrawPageHeader("剑意谱", "玄鉴所录天下一己剑意。剑意属于创者自身，创者身故后，其名与剑理仍留于谱中。剑意按新近在前分页展示，不再截断后世剑意。");

		if (_swordIntentSummaryVersion != (snapshot?.Version ?? -1))
		{
			HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < intents.Count; i++)
			{
				XjSwordIntentArchiveRecord item = intents[i];
				if (item == null) continue;
				types.Add(string.IsNullOrWhiteSpace(item.IntentType) ? "未定" : item.IntentType);
			}
			_swordIntentTypeCount = types.Count;
			_swordIntentSummaryVersion = snapshot?.Version ?? -1;
		}

		GUILayout.BeginHorizontal();
		DrawOverviewPill("已录剑意", intents.Count.ToString(), "#E9D99A", GUILayout.Width(180f));
		DrawOverviewPill("最早剑意", intents.Count > 0 ? XjChronology.FormatYear(intents[0].CreatedYear) : "尚无", "#9CD7FF", GUILayout.Width(220f));
		DrawOverviewPill("最新剑意", intents.Count > 0 ? XjChronology.FormatYear(intents[intents.Count - 1].CreatedYear) : "尚无", "#A7E08A", GUILayout.Width(220f));
		DrawOverviewPill("剑理分型", _swordIntentTypeCount.ToString(), "#B7A7FF", GUILayout.Width(180f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();

		if (intents.Count == 0)
		{
			_swordIntentPage = 0;
			DrawEmptyCard("天下尚未诞生可载入玄鉴的一己剑意。待有用剑者自成一家，此页自会显现。", "#9AA4B2");
			return;
		}

		int pageCount = Math.Max(1, (intents.Count + SwordIntentPageSize - 1) / SwordIntentPageSize);
		_swordIntentPage = Mathf.Clamp(_swordIntentPage, 0, pageCount - 1);
		DrawSwordIntentPager(pageCount);

		int newestOffset = _swordIntentPage * SwordIntentPageSize;
		int pageItemCount = Math.Min(SwordIntentPageSize, intents.Count - newestOffset);
		for (int i = 0; i < pageItemCount; i++)
		{
			int sourceIndex = intents.Count - 1 - newestOffset - i;
			if (sourceIndex < 0 || sourceIndex >= intents.Count) continue;
			XjSwordIntentArchiveRecord item = intents[sourceIndex];
			if (item == null) continue;
			bool alive = XjSwordIntentRegistry.IsCreatorAlive(item);
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(alive ? "#9CD7FF" : "#777D86");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b><size=20>《" + Rich(item.IntentName) + "》</size></b>");
			GUILayout.FlexibleSpace();
			GUILayout.Label("<color=" + (alive ? "#A7E08A" : "#A8A8A8") + ">" + (alive ? "创者在世" : "创者已逝") + "</color>", GUILayout.Width(105f));
			if (alive) DrawActorFocusButton("定位创者", item.CreatorActorId, GUILayout.Width(100f), GUILayout.Height(30f));
			GUILayout.EndHorizontal();
			GUILayout.BeginHorizontal();
			DrawMiniStat("创者", string.IsNullOrWhiteSpace(item.CreatorName) ? "无名剑修" : item.CreatorName, "#E6EDF2", GUILayout.Width(260f));
			DrawMiniStat("诞生年代", XjChronology.FormatYear(item.CreatedYear), "#E9D99A", GUILayout.Width(190f));
			DrawMiniStat("创立时境界", string.IsNullOrWhiteSpace(item.CreatorRealm) ? "未载" : item.CreatorRealm, "#9CD7FF", GUILayout.Width(180f));
			DrawMiniStat("修炼体系", string.IsNullOrWhiteSpace(item.CreatorPath) ? "未载" : item.CreatorPath, "#B7A7FF", GUILayout.Width(190f));
			DrawMiniStat("剑理", string.IsNullOrWhiteSpace(item.IntentType) ? "未定" : item.IntentType, "#FFD37A", GUILayout.Width(135f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			if (!string.IsNullOrWhiteSpace(item.Description)) GUILayout.Label("<color=#C8CDD4>" + Rich(item.Description) + "</color>");
			GUILayout.EndVertical();
		}

		DrawSwordIntentPager(pageCount);
		GUILayout.Label("<color=grey>当前显示第 " + (_swordIntentPage + 1) + " 页 · 共 " + pageCount
			+ " 页，本页 " + pageItemCount + " 条；天下共收录 " + intents.Count + " 道剑意。</color>");
	}

	private void DrawSwordIntentPager(int pageCount)
	{
		GUILayout.BeginHorizontal();
		GUI.enabled = _swordIntentPage > 0;
		if (GUILayout.Button("← 更新剑意", GUILayout.Width(130f), GUILayout.Height(34f)))
		{
			_swordIntentPage--;
			_scrollPosition = Vector2.zero;
		}
		GUI.enabled = true;
		GUILayout.FlexibleSpace();
		GUILayout.Label("<b>第 " + (_swordIntentPage + 1) + " 页 · 共 " + pageCount + " 页</b>", GUILayout.Width(150f));
		GUILayout.FlexibleSpace();
		GUI.enabled = _swordIntentPage + 1 < pageCount;
		if (GUILayout.Button("更早剑意 →", GUILayout.Width(130f), GUILayout.Height(34f)))
		{
			_swordIntentPage++;
			_scrollPosition = Vector2.zero;
		}
		GUI.enabled = true;
		GUILayout.EndHorizontal();
	}
}
