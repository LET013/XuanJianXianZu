using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Talisman;

namespace XuanJianVNext.Systems.Craft;

internal static class XjCraftAnnualRouter
{
	internal static void TickActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		XjCraftTraitRules.NormalizeExclusive(actor);
		string traitId = XjCraftTraitRules.GetPrimaryTraitId(actor);
		if (!string.IsNullOrWhiteSpace(traitId))
		{
			XjCraftProficiencySystem.TickRankProgression(actor, currentYear);
		}
		if (string.Equals(traitId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal))
		{
			XjAlchemyAnnualSystem.TickActor(actor, currentYear);
		}
		else if (string.Equals(traitId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal))
		{
			XjTalismanAnnualSystem.TickActor(actor, currentYear);
		}
		else if (string.Equals(traitId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal))
		{
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			XjEquipmentForgeConsumer.TryForgeAnnual(actor, realmId, currentYear);
		}
		else if (string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal))
		{
			if (XjSecretRealmConstructionSystem.TickActor(actor, currentYear))
			{
				XjCraftProficiencySystem.RecordFormationProgress(actor, 1);
			}
			else
			{
				XjFormationEngineeringSystem.TickActor(actor, currentYear);
			}
		}
	}
}
