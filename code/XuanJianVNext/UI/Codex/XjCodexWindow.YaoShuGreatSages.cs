using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static void DrawYaoShuGreatSagePage()
	{
		IReadOnlyList<XjYaoShuGreatSageSystem.CodexItem> items = XjYaoShuGreatSageSystem.BuildCodexItems();
		int living = 0;
		for (int i = 0; i < items.Count; i++) if (items[i].Alive) living++;
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D6BE86");
		GUILayout.Label("<b>现世大圣</b>　<color=#E1C46A>" + living + " / " + items.Count + "</color>");
		GUILayout.Label("现世占位 · 大圣身灭后静候150年 · 渊照寒渊螭须待渊照空证成功", GUI.skin.label);
		GUILayout.EndVertical();
		GUILayout.Space(8f);
		for (int i = 0; i < items.Count; i++)
		{
			XjYaoShuGreatSageSystem.CodexItem item = items[i];
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(item.Alive ? "#76D7A2" : "#687687");
			GUILayout.Label("<b>" + item.Name + "</b>　" + (item.Alive ? "<color=#76D7A2>现世</color>" : "<color=#AAB4C0>空位</color>"));
			GUILayout.Label("道途：" + item.DaoTu + "　正位：" + item.Fruit + "　神通：【" + item.Skill + "】");
			GUILayout.Label("对应妖民：" + item.YaoMin + "　历次化生：" + item.ManifestationCount + "　最近化生：" + FormatYaoShuYear(item.LastManifestationYear));
			if (!item.Alive)
			{
				GUILayout.Label(item.NextAttemptYear > 0 ? "下一次可判定：世界" + item.NextAttemptYear + "年" : "等待世界进入化生条件。", GUI.skin.label);
			}
			GUILayout.EndVertical();
			GUILayout.Space(5f);
		}
	}

	private static string FormatYaoShuYear(int year) => year > 0 ? "世界" + year + "年" : "未有记载";
}
