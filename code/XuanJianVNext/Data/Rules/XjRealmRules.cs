using System.Collections.Generic;
using System;

namespace XuanJianVNext.Data.Rules
{
internal static class XjRealmIds
{
	public const string TaiXi = "XjRealm1";
	public const string LianQi = "XjRealm2";
	public const string ZhuJi = "XjRealm3";
	public const string ZiFu = "XjRealm4";
	public const string JinDan = "XjRealm5";
	public const string ShenDan = "XjShenDan";

	// 仅记录 0.5.4 中未开放占位，VNext 不主动实现。
	public const string DaoTaiPlaceholder = "XjRealm7";
}

internal readonly struct XjRealmRule
{
	internal readonly string RealmId;
	internal readonly string DisplayName;
	internal readonly float RequiredZhenYuan;
	internal readonly bool RequiresCaiQi;
	internal readonly bool RequiresFiveXianJi;
	internal readonly bool RequiresQiuJinFa;
	internal readonly bool IsImplemented;

	internal XjRealmRule(
		string realmId,
		string displayName,
		float requiredZhenYuan,
		bool requiresCaiQi,
		bool requiresFiveXianJi,
		bool requiresQiuJinFa,
		bool isImplemented)
	{
		RealmId = realmId;
		DisplayName = displayName;
		RequiredZhenYuan = requiredZhenYuan;
		RequiresCaiQi = requiresCaiQi;
		RequiresFiveXianJi = requiresFiveXianJi;
		RequiresQiuJinFa = requiresQiuJinFa;
		IsImplemented = isImplemented;
	}
}

internal static class XjRealmRules
{
	// 金丹后续境界在 0.5.4 当前证据中未开放，VNext 不主动实现。
	internal static readonly IReadOnlyList<XjRealmRule> All = new XjRealmRule[]
	{
		new XjRealmRule(XjRealmIds.TaiXi, "胎息", 10f, false, false, false, true),
		new XjRealmRule(XjRealmIds.LianQi, "炼气", 300f, true, false, false, true),
		new XjRealmRule(XjRealmIds.ZhuJi, "筑基", 1200f, false, false, false, true),
		new XjRealmRule(XjRealmIds.ZiFu, "紫府", 36000f, false, false, false, true),
		new XjRealmRule(XjRealmIds.JinDan, "金丹", 129600f, false, true, false, true),
		new XjRealmRule(XjRealmIds.ShenDan, "神丹", 129600f, false, true, false, true),
		new XjRealmRule(XjRealmIds.DaoTaiPlaceholder, "道胎占位", 1000000f, false, false, false, false)
	};

	internal static bool TryGet(string realmId, out XjRealmRule rule)
	{
		for (int i = 0; i < All.Count; i++)
		{
			XjRealmRule candidate = All[i];
			if (string.Equals(candidate.RealmId, realmId, StringComparison.Ordinal))
			{
				rule = candidate;
				return true;
			}
		}

		rule = default;
		return false;
	}
}
}

namespace XuanJianVNext.Data.Rules
{
internal static class XjRuleNames
{
	public const string ZhenYuan = "ZhenYuan";
	public const string MingShu = "MingShu";
	public const string HuiGuang = "HuiGuang";
	public const string XjZz = "XjZz";
	public const string ChuShen = "ChuShen";
}
}

