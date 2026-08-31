using System;

namespace XuanJianVNext.Data.Rules;

/// <summary>
/// 跨修法高境身份。它只回答角色在家族、宗门与世界秩序中属于真人还是真君，
/// 不把服气养性与紫府金丹道合并成同一套修炼状态机。
/// </summary>
internal enum XjHighRealmClass
{
	None = 0,
	ZhenRen = 1,
	ZhenJun = 2
}

/// <summary>
/// 高境身份下的实际修法拆分。外部统计使用真人／真君，内部仍保留
/// 紫府、服气真人、金丹系高境与真君羽士四类数据。
/// </summary>
internal enum XjHighRealmKind
{
	None = 0,
	ZiFu = 1,
	FuQiZhenRen = 2,
	ZiJinJinDan = 3,
	ZhenJunYuShi = 4
}

internal static class XjHighRealmIdentity
{
	internal static XjHighRealmClass ResolveClass(string realmId)
	{
		return ResolveKind(realmId) switch
		{
			XjHighRealmKind.ZiFu => XjHighRealmClass.ZhenRen,
			XjHighRealmKind.FuQiZhenRen => XjHighRealmClass.ZhenRen,
			XjHighRealmKind.ZiJinJinDan => XjHighRealmClass.ZhenJun,
			XjHighRealmKind.ZhenJunYuShi => XjHighRealmClass.ZhenJun,
			_ => XjHighRealmClass.None
		};
	}

	internal static XjHighRealmKind ResolveKind(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal))
			return XjHighRealmKind.ZiFu;
		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
			return XjHighRealmKind.FuQiZhenRen;
		if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal))
			return XjHighRealmKind.ZiJinJinDan;
		if (string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
			return XjHighRealmKind.ZhenJunYuShi;
		return XjHighRealmKind.None;
	}

	internal static XjHighRealmKind ResolveKind(Actor actor, string realmId)
	{
		XjHighRealmKind kind = ResolveKind(realmId);
		return kind == XjHighRealmKind.ZiJinJinDan
			&& string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal)
			&& XjCultivationPathRules.IsFuQiYangXing(actor)
				? XjHighRealmKind.ZhenJunYuShi
				: kind;
	}

	internal static bool IsZhenRen(string realmId)
		=> ResolveClass(realmId) == XjHighRealmClass.ZhenRen;

	internal static bool IsZhenJun(string realmId)
		=> ResolveClass(realmId) == XjHighRealmClass.ZhenJun;

	internal static bool IsHighRealm(string realmId)
		=> ResolveClass(realmId) != XjHighRealmClass.None;

	internal static int ResolveBloodlineRank(string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.DaoTaiPlaceholder, StringComparison.Ordinal))
		{
			return 3;
		}
		return ResolveClass(normalized) switch
		{
			XjHighRealmClass.ZhenJun => 2,
			XjHighRealmClass.ZhenRen => 1,
			_ => 0
		};
	}

	internal static string GetClassDisplayName(string realmId)
	{
		return ResolveClass(realmId) switch
		{
			XjHighRealmClass.ZhenJun => "真君",
			XjHighRealmClass.ZhenRen => "真人",
			_ => string.Empty
		};
	}
}
