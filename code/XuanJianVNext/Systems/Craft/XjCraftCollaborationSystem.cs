using System;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.FaBao;

namespace XuanJianVNext.Systems.Craft;

internal static class XjCraftCollaborationSystem
{
	internal const string FormationFlagLow = "xj_formation_flag_low";
	internal const string FormationFlagHigh = "xj_formation_flag_high";
	internal const string FormationPlateLow = "xj_formation_plate_low";
	internal const string FormationPlateHigh = "xj_formation_plate_high";
	internal const string FormationRune = "xj_formation_rune";
	internal const string SpiritMaterial = "xj_formation_spirit_material";

	internal static void RecordArtifactOutput(Actor actor, string className, int currentYear)
	{
		if (actor?.data == null || !XjCraftOwnerResolver.TryResolvePreferred(actor, out XjCraftOwnerKey owner)) return;
		if (XjFaBaoCatalog.IsJinDanFaBao(className))
		{
			XjCraftDomainRegistry.TryAddResource(owner, FormationPlateHigh, 3, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, FormationFlagHigh, 6, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, SpiritMaterial, 3, currentYear);
		}
		else if (XjFaBaoCatalog.IsZiFuLingBao(className))
		{
			XjCraftDomainRegistry.TryAddResource(owner, FormationPlateHigh, 1, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, FormationFlagHigh, 3, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, SpiritMaterial, 1, currentYear);
		}
		else
		{
			XjCraftDomainRegistry.TryAddResource(owner, FormationPlateLow, 1, currentYear);
			XjCraftDomainRegistry.TryAddResource(owner, FormationFlagLow, 2, currentYear);
		}
	}

	internal static void RecordTalismanOutput(Actor actor, int quantity, int currentYear)
	{
		if (actor?.data == null || quantity <= 0 || !XjCraftOwnerResolver.TryResolvePreferred(actor, out XjCraftOwnerKey owner)) return;
		XjCraftDomainRegistry.TryAddResource(owner, FormationRune, Math.Max(1, quantity), currentYear);
	}
}
