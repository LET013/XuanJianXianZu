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
		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].Alive) living++;
		}

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D6BE86");
		GUILayout.Label("<b>妖属大圣 · 果位映照</b>　<color=#E1C46A>现世 " + living + " / " + items.Count + "</color>");
		GUILayout.Label("大圣显世后，对应妖属中的幼兽会在成长节点自行分化；只有真正成为妖民者才进入玄鉴修炼链。", GUI.skin.label);
		GUILayout.Label("大圣以服气真君羽士为修炼起点，承命数、道慧与对应道途。其果位仅为妖属映照，不占修士果位，身殒后归寂待下一次显世。", GUI.skin.label);
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
			GUILayout.Label("<color=#D8A6FF>神通</color>　【" + item.Skill + "】");

			if (item.Alive)
			{
				GUILayout.Label("本次显世：" + FormatYaoShuYear(item.LastManifestationYear), GUI.skin.label);
			}
			else
			{
				GUILayout.Label(
					"最近显世：" + FormatYaoShuYear(item.LastManifestationYear)
					+ "　最近归寂：" + FormatYaoShuYear(item.LastDepartureYear),
					GUI.skin.label);
				GUILayout.Label(
					item.NextAttemptYear > 0
						? "<color=#B8C6D8>下一次可判定</color>　世界" + item.NextAttemptYear + "年"
						: "<color=#B8C6D8>等待下一次显世条件</color>",
					GUI.skin.label);
			}

			GUILayout.EndVertical();
			GUILayout.Space(6f);
		}
	}

	private static string FormatYaoShuYear(int year) => year > 0 ? "世界" + year + "年" : "未有记载";
}
