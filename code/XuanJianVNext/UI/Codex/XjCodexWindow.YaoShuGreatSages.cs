using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static void DrawYaoShuGreatSagePage()
	{
		// 先做固定十二席回填，保证刚化生/刚读档时仙鉴与排行榜看到的是同一批 Actor。
		XjYaoShuGreatSageSystem.EnsureRankMembership();
		IReadOnlyList<XjYaoShuGreatSageSystem.CodexItem> items = XjYaoShuGreatSageSystem.BuildCodexItems();
		Dictionary<string, Actor> livingByDaoTu = BuildLivingGreatSageLookup();

		int living = 0;
		for (int i = 0; i < items.Count; i++) if (items[i].Alive) living++;

		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#D6BE86");
		GUILayout.Label("<b>妖属大圣 · 十二圣图谱</b>");
		GUILayout.Label(
			"<color=#E1C46A>现世 " + living + " / " + items.Count + "</color>　"
			+ "<color=#A8C7D8>果位映照，不占修士正常位序</color>");
		GUILayout.Label(
			"大圣身灭后静候150年再判；陆江仙检验会逐席检查十二圣，不再受普通修士正位占用截断。",
			GUI.skin.label);
		GUILayout.EndVertical();

		GUILayout.Space(8f);
		for (int i = 0; i < items.Count; i++)
		{
			XjYaoShuGreatSageSystem.CodexItem item = items[i];
			livingByDaoTu.TryGetValue((item.DaoTu ?? string.Empty).Trim(), out Actor actor);

			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(item.Alive ? "#76D7A2" : "#687687");
			GUILayout.Label(
				"<b>" + item.Name + "</b>　"
				+ (item.Alive ? "<color=#76D7A2>◆ 现世</color>" : "<color=#AAB4C0>◇ 待化</color>"));

			GUILayout.Label(
				"<color=#D8CDAA>道途</color>　" + item.DaoTu
				+ "　　<color=#D8CDAA>映照果位</color>　" + item.Fruit);
			GUILayout.Label(
				"<color=#D8CDAA>神通</color>　【" + item.Skill + "】"
				+ "　　<color=#D8CDAA>对应妖民</color>　" + item.YaoMin);

			if (actor?.data != null)
			{
				float daoHui = XjDaoHuiPolicy.Read(actor);
				XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
				GUILayout.Label(
					"<color=#9CD7FF>道慧 " + Mathf.FloorToInt(daoHui) + "/100</color>"
					+ "　　<color=#E1C46A>命数 " + Mathf.FloorToInt(Mathf.Max(0f, mingShu)) + "</color>");
			}
			else if (item.Alive)
			{
				GUILayout.Label("<color=#AAB4C0>道慧 / 命数正在等待角色索引回填</color>");
			}

			GUILayout.Label(
				"<color=#D8CDAA>化生</color>　" + item.ManifestationCount + "次"
				+ "　　<color=#D8CDAA>最近</color>　" + FormatYaoShuYear(item.LastManifestationYear));

			if (!item.Alive)
			{
				string next = item.NextAttemptYear > 0
					? "世界" + item.NextAttemptYear + "年"
					: "尚未进入化生条件";
				GUILayout.Label("<color=#AAB4C0>下一次判定　" + next + "</color>", GUI.skin.label);
			}

			GUILayout.EndVertical();
			GUILayout.Space(5f);
		}
	}

	private static Dictionary<string, Actor> BuildLivingGreatSageLookup()
	{
		var result = new Dictionary<string, Actor>(StringComparer.Ordinal);
		IReadOnlyList<long> actorIds = XjCultivatorCache.GetAllIds();
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(actorIds[i], out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| !XjYaoShuGreatSageSystem.IsGreatSage(actor))
			{
				continue;
			}

			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)) continue;
			string normalized = (daoTu ?? string.Empty).Trim();
			if (normalized.Length > 0) result[normalized] = actor;
		}
		return result;
	}

	private static string FormatYaoShuYear(int year) => year > 0 ? "世界" + year + "年" : "未有记载";
}
