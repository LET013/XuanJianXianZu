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
		int totalManifestations = 0;
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Alive) living++;
			totalManifestations += Math.Max(0, items[i].ManifestationCount);
		}

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D6BE86");
		GUILayout.Label("<b>妖属大圣 · 果位映照</b>　<color=#E1C46A>现世 " + living + " / " + items.Count + "</color>");
		GUILayout.Label("<color=#A9D9C8>首位大圣显世后</color>，具备智慧的动物开放玄鉴修炼资格；不再点化或繁衍“妖民”。", GUI.skin.label);
		GUILayout.Label("大圣以真君羽士为修炼起点，承命数、道慧与对应道途；性情统一为<color=#A8D9B8>崇尚和平</color>。身灭后静候150年，方可再化生。", GUI.skin.label);
		GUILayout.Label("历次化生总计：<color=#D8C585>" + totalManifestations + "</color>", GUI.skin.label);
		GUILayout.EndVertical();

		GUILayout.Space(9f);
		for (int i = 0; i < items.Count; i++)
		{
			XjYaoShuGreatSageSystem.CodexItem item = items[i];
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(item.Alive ? "#76D7A2" : "#687687");

			string status = item.Alive
				? "<color=#76D7A2>现世</color>"
				: "<color=#AAB4C0>空位</color>";
			GUILayout.Label("<b>" + item.Name + "</b>　" + status);
			GUILayout.Label(
				"<color=#9ED9FF>道途</color>　" + item.DaoTu
				+ "　<color=#D9C27A>映照果位</color>　" + item.Fruit);
			GUILayout.Label(
				"<color=#D8A6FF>神通</color>　【" + item.Skill + "】"
				+ "　<color=#A8D9B8>性情</color>　崇尚和平");

			if (item.Alive)
			{
				GUILayout.Label(
					"本次化生：" + FormatYaoShuYear(item.LastManifestationYear)
					+ "　历次化生：" + Math.Max(1, item.ManifestationCount),
					GUI.skin.label);
			}
			else
			{
				GUILayout.Label(
					"历次化生：" + item.ManifestationCount
					+ "　最近化生：" + FormatYaoShuYear(item.LastManifestationYear)
					+ "　最近归寂：" + FormatYaoShuYear(item.LastDepartureYear),
					GUI.skin.label);
				GUILayout.Label(
					item.NextAttemptYear > 0
						? "<color=#B8C6D8>下一次可判定</color>　世界" + item.NextAttemptYear + "年"
						: "<color=#B8C6D8>等待果位进入化生条件</color>",
					GUI.skin.label);
			}

			GUILayout.EndVertical();
			GUILayout.Space(6f);
		}
	}

	private static string FormatYaoShuYear(int year) => year > 0 ? "世界" + year + "年" : "未有记载";
}
