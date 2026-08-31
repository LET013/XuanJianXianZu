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
	public const string DaoTai = "XjRealm6";
	public const string ShenDan = "XjShenDan";
	public const string HuangGuan = "XjRealm11";
	public const string FuQiZhenRen = "XjRealm12";
	public const string ZhenJunYuShi = "XjRealm13";
	public const string FuQiDaoTai = "XjRealm14";

	// 旧档兼容哨兵。真正的道胎晋升由 HighRealm/XjDaoTaiAscensionSystem 专门结算；
	// 通用境界推进器不得把 XjRealm7 当作可晋升目标。
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
	// IsImplemented 只表示“是否由通用境界推进器负责”。
	// 道胎/服气道胎已经实装，但必须经功绩、位序等高境专用规则晋升，故这里保持 false，
	// 防止基础真元推进绕过 XjDaoTaiAscensionSystem。
	internal static readonly IReadOnlyList<XjRealmRule> All = new XjRealmRule[]
	{
		new XjRealmRule(XjRealmIds.TaiXi, "胎息", 10f, false, false, false, true),
		new XjRealmRule(XjRealmIds.LianQi, "炼气", 300f, true, false, false, true),
		new XjRealmRule(XjRealmIds.ZhuJi, "筑基", 1200f, false, false, false, true),
		new XjRealmRule(XjRealmIds.ZiFu, "紫府", 36000f, false, false, false, true),
		new XjRealmRule(XjRealmIds.JinDan, "金丹", 129600f, false, true, false, true),
		new XjRealmRule(XjRealmIds.DaoTai, "道胎", 1000000f, false, false, false, false),
		new XjRealmRule(XjRealmIds.ShenDan, "神丹", 129600f, false, true, false, true),
		new XjRealmRule(XjRealmIds.FuQiDaoTai, "道胎", 1000000f, false, false, false, false),
		new XjRealmRule(XjRealmIds.DaoTaiPlaceholder, "道胎", 1000000f, false, false, false, false)
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
