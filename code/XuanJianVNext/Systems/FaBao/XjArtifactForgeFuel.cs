using System;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.FaBao;

internal enum XjArtifactForgeFuelKind
{
	None = 0,
	XianTianQi = 1,
	LingWu = 2,
	JinXing = 3
}

internal readonly struct XjArtifactForgeFuelReceipt
{
	internal readonly bool Found;
	internal readonly XjArtifactForgeFuelKind Kind;
	internal readonly string ResourceId;
	internal readonly string DisplayName;

	internal XjArtifactForgeFuelReceipt(bool found, XjArtifactForgeFuelKind kind, string resourceId, string displayName)
	{
		Found = found;
		Kind = kind;
		ResourceId = resourceId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
	}
}

/// <summary>
/// 炼器燃料统一入口：筑基法器消耗十二缕同道途先天之气；
/// 紫府本命灵宝只消耗一件同道途紫府灵物；金丹法宝只消耗一缕金丹遗留金性。
/// 两条高境器物素材链严格分离，不允许紫府以金性替代灵物。
/// </summary>
internal static class XjArtifactForgeFuel
{
	internal const int ZhuJiFaQiXianTianQiCost = 12;

	internal static bool TryConsumeForZhuJiFaQi(Actor actor, string daoTu, out XjArtifactForgeFuelReceipt receipt)
	{
		receipt = default;
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu)
			|| !XjCaiQiCatalog.TryGetOldResourceIdByDaoTuName(daoTu, out string resourceId)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		if (XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord family)
			&& family.Found
			&& string.Equals(family.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(family.FamilyKey)
			&& XjFamilyCaiQiWarehouse.TryGetCount(family.FamilyKey, resourceId, out int familyCount)
			&& familyCount >= ZhuJiFaQiXianTianQiCost
			&& XjFamilyCaiQiWarehouse.TryConsume(family.FamilyKey, resourceId, ZhuJiFaQiXianTianQiCost))
		{
			receipt = new XjArtifactForgeFuelReceipt(true, XjArtifactForgeFuelKind.XianTianQi, resourceId, daoTu + "先天之气×" + ZhuJiFaQiXianTianQiCost);
			return true;
		}

		XjSectIdentitySnapshot zongMen = XjSectIdentityReader.BuildIdentity(actor);
		if (zongMen.Found
			&& zongMen.ZongMenId > 0L
			&& XjSectCaiQiWarehouse.TryGetCaiQiCount(zongMen.ZongMenId, resourceId, out int zongMenCount)
			&& zongMenCount >= ZhuJiFaQiXianTianQiCost
			&& XjSectCaiQiWarehouse.TryConsumeCaiQi(zongMen.ZongMenId, resourceId, ZhuJiFaQiXianTianQiCost))
		{
			receipt = new XjArtifactForgeFuelReceipt(true, XjArtifactForgeFuelKind.XianTianQi, resourceId, daoTu + "先天之气×" + ZhuJiFaQiXianTianQiCost);
			return true;
		}

		return false;
	}

	internal static bool HasZiFuForgeFuel(Actor actor, string daoTu)
	{
		return TryResolveConfirmedFamily(actor, out long familyStableId)
			&& !string.IsNullOrWhiteSpace(daoTu)
			&& XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef lingWu)
			&& XjFamilyLingWuWarehouse.TryGetCount(familyStableId, lingWu.Id, out int count)
			&& count > 0;
	}

	internal static bool TryConsumeForZiFu(Actor actor, string daoTu, out XjArtifactForgeFuelReceipt receipt)
	{
		receipt = default;
		if (!TryResolveConfirmedFamily(actor, out long familyStableId)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef lingWu)
			|| !XjFamilyLingWuWarehouse.TryGetCount(familyStableId, lingWu.Id, out int count)
			|| count <= 0
			|| !XjFamilyLingWuWarehouse.TryConsume(familyStableId, lingWu.Id, 1))
		{
			return false;
		}

		receipt = new XjArtifactForgeFuelReceipt(true, XjArtifactForgeFuelKind.LingWu, lingWu.Id, lingWu.Name);
		return true;
	}

	internal static bool HasJinDanForgeFuel(Actor actor)
	{
		return TryResolveConfirmedFamily(actor, out long familyStableId)
			&& XjFamilyLingWuWarehouse.HasAnyJinXing(familyStableId);
	}

	internal static bool TryConsumeForJinDan(Actor actor, out XjArtifactForgeFuelReceipt receipt)
	{
		receipt = default;
		if (!TryResolveConfirmedFamily(actor, out long familyStableId)
			|| !XjFamilyLingWuWarehouse.TryConsumeFirstJinXing(familyStableId, out string jinXing))
		{
			return false;
		}

		string normalized = string.IsNullOrWhiteSpace(jinXing) ? "未定" : jinXing.Trim();
		receipt = new XjArtifactForgeFuelReceipt(
			true,
			XjArtifactForgeFuelKind.JinXing,
			XjFamilyTreasureCatalog.BuildJinXingTreasureId(jinXing),
			"金丹金性·" + normalized);
		return true;
	}

	private static bool TryResolveConfirmedFamily(Actor actor, out long familyStableId)
	{
		familyStableId = 0L;
		if (actor?.data == null) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyStableId)
			&& familyStableId > 0L;
	}

	internal static int ResolveZhuJiFaQiChancePercent(Actor actor, in XjArtifactForgeFuelReceipt receipt)
	{
		if (!receipt.Found || receipt.Kind != XjArtifactForgeFuelKind.XianTianQi) return 0;
		return Math.Clamp(XjFaBaoForgePolicy.ZhuJiFaQiChancePercent, 0, 90);
	}

	internal static int ResolveZiFuChancePercent(Actor actor, in XjArtifactForgeFuelReceipt receipt)
	{
		if (!receipt.Found || receipt.Kind != XjArtifactForgeFuelKind.LingWu) return 0;
		return Math.Clamp(XjFaBaoForgePolicy.ZiFuLingWuChancePercent, 0, 95);
	}
}
