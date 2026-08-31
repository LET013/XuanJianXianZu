using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Patches;

/// <summary>
/// 尊号与金性共用的语义去重层。不是禁用同一道途词汇，而是禁止相邻构词段
/// 用同义字反复表达同一个意象（如雷/霆、火/炎/焰、光/辉/曜）。
/// </summary>
internal partial class XjVNextPatches
{
	private static readonly string[] NamingDedupSemanticGroups =
	{
		"雷霆震电殛霹雳",
		"火炎焰焱炽焚煌燧",
		"光辉晖曜耀明昭朗照景",
		"月阴魄桂蟾幽夜",
		"水潮海渊溟波泉川河泽",
		"寒冰霜雪凛冽冻",
		"风云岚霞霄飙",
		"土山岳峰岭壑坤地",
		"金锋钧庚铄锐白",
		"木林森青荣华春",
		"阳日乌曦昼",
		"玄黑冥暗晦"
	};

	private static readonly string[] NamingDedupNeutralPairs =
	{
		"含真", "抱一", "守素", "清衡", "元和", "凝章",
		"藏真", "摄神", "承命", "返照", "正仪", "玄章",
		"道枢", "寂照", "清虚", "素元", "宣真", "合机"
	};

	[HarmonyPatch(typeof(XjRealmTitleNameLibrary), "GenerateDaoHaoStem", new Type[] { typeof(string), typeof(long) })]
	[HarmonyPostfix]
	private static void XuanJian_HighRealm_DaoHao_SemanticDedup_Postfix(string daoTu, long actorId, ref string __result)
	{
		__result = NamingDedupSanitizeDaoHaoStem(daoTu, actorId, __result);
	}

	[HarmonyPatch(typeof(XjRealmTitleNameLibrary), "GeneratePositionJinXingStem",
		new Type[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(int), typeof(long) })]
	[HarmonyPostfix]
	private static void XuanJian_HighRealm_JinXingStem_SemanticDedup_Postfix(
		string daoTu,
		string positionType,
		int slot,
		long seed,
		ref string __result)
	{
		__result = NamingDedupSanitizeJinXingStem(daoTu, positionType, slot, seed, __result);
	}

	/// <summary>
	/// 旧档中已经保存的金性也走一次同一语义审计；不只修未来新生成角色。
	/// </summary>
	[HarmonyPatch(typeof(XjJinXingNamePolicy), "NormalizeOrBuild")]
	[HarmonyPostfix]
	private static void XuanJian_HighRealm_StoredJinXing_SemanticDedup_Postfix(
		string daoTu,
		string positionType,
		string primaryAuthority,
		string externalDaoTu,
		int slot,
		long seed,
		ref string __result)
	{
		string value = (__result ?? string.Empty).Trim();
		if (value.Length == 0 || string.Equals(value, XjJinXingNamePolicy.CanonicalDuiJinFruitName, StringComparison.Ordinal))
		{
			return;
		}

		bool hasSuffix = value.EndsWith("性", StringComparison.Ordinal);
		string stem = hasSuffix ? value.Substring(0, value.Length - 1) : value;
		if (stem.Length != 6)
		{
			string rebuilt = XjRealmTitleNameLibrary.GeneratePositionJinXingStem(
				daoTu, positionType, primaryAuthority, externalDaoTu, slot, seed);
			if (!string.IsNullOrWhiteSpace(rebuilt))
			{
				__result = rebuilt + (hasSuffix ? "性" : string.Empty);
			}
			return;
		}

		string sanitized = NamingDedupSanitizeJinXingStem(daoTu, positionType, slot, seed, stem);
		if (!string.Equals(sanitized, stem, StringComparison.Ordinal))
		{
			__result = sanitized + (hasSuffix ? "性" : string.Empty);
		}
	}

	private static string NamingDedupSanitizeDaoHaoStem(string daoTu, long actorId, string stem)
	{
		string value = (stem ?? string.Empty).Trim();
		if (value.Length != 4) return value;

		string first = value.Substring(0, 2);
		string second = value.Substring(2, 2);
		if (!NamingDedupHasSemanticOverlap(first, second)) return value;

		second = NamingDedupPickNeutralPair(actorId, (daoTu ?? string.Empty) + "|zunhao-semantic", first);
		return first + second;
	}

	private static string NamingDedupSanitizeJinXingStem(string daoTu, string positionType, int slot, long seed, string stem)
	{
		string value = (stem ?? string.Empty).Trim();
		if (value.Length != 6) return value;

		string head = value.Substring(0, 2);
		string body = value.Substring(2, 2);
		string tail = value.Substring(4, 2);
		string salt = (daoTu ?? string.Empty) + "|" + (positionType ?? string.Empty) + "|" + Math.Max(1, slot);

		if (NamingDedupHasSemanticOverlap(head, body))
		{
			head = NamingDedupPickNeutralPair(seed + 17L, salt + "|jinxing-head-semantic", body, tail);
		}
		if (NamingDedupHasSemanticOverlap(tail, body) || NamingDedupHasSemanticOverlap(tail, head))
		{
			tail = NamingDedupPickNeutralPair(seed + 41L, salt + "|jinxing-tail-semantic", body, head);
		}
		return head + body + tail;
	}

	private static string NamingDedupPickNeutralPair(long seed, string salt, params string[] blockedSegments)
	{
		int start = XjDeterministicHash.PositiveIndex(seed, salt, NamingDedupNeutralPairs.Length);
		for (int offset = 0; offset < NamingDedupNeutralPairs.Length; offset++)
		{
			string candidate = NamingDedupNeutralPairs[(start + offset) % NamingDedupNeutralPairs.Length];
			bool blocked = false;
			if (blockedSegments != null)
			{
				for (int i = 0; i < blockedSegments.Length; i++)
				{
					string segment = blockedSegments[i] ?? string.Empty;
					if (segment.Length > 0 && NamingDedupHasSemanticOverlap(candidate, segment))
					{
						blocked = true;
						break;
					}
				}
			}
			if (!blocked) return candidate;
		}

		return "守素";
	}

	private static bool NamingDedupHasSemanticOverlap(string left, string right)
	{
		string a = (left ?? string.Empty).Trim();
		string b = (right ?? string.Empty).Trim();
		if (a.Length == 0 || b.Length == 0) return false;

		for (int i = 0; i < a.Length; i++)
		{
			if (b.IndexOf(a[i]) >= 0) return true;
		}

		for (int groupIndex = 0; groupIndex < NamingDedupSemanticGroups.Length; groupIndex++)
		{
			string group = NamingDedupSemanticGroups[groupIndex];
			if (NamingDedupContainsAny(a, group) && NamingDedupContainsAny(b, group)) return true;
		}
		return false;
	}

	private static bool NamingDedupContainsAny(string value, string group)
	{
		for (int i = 0; i < value.Length; i++)
		{
			if (group.IndexOf(value[i]) >= 0) return true;
		}
		return false;
	}
}
