using System;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Data.Rules;

internal static class XjCraftTraitRules
{
	internal const string AlchemyTraitId = "XjAlchemyCrafter";
	internal const string ArtifactRefiningTraitId = "XjArtifactRefiner";
	internal const string TalismanTraitId = "XjTalismanCrafter";
	internal const string FormationTraitId = "XjFormationMaster";

	private static readonly string[] AllTraitIds =
	{
		AlchemyTraitId,
		ArtifactRefiningTraitId,
		TalismanTraitId,
		FormationTraitId
	};

	internal static bool CanPracticeAlchemy(Actor actor) => HasPrimaryTrait(actor, AlchemyTraitId);
	internal static bool CanRefineArtifacts(Actor actor) => HasPrimaryTrait(actor, ArtifactRefiningTraitId);
	internal static bool CanPracticeTalismans(Actor actor) => HasPrimaryTrait(actor, TalismanTraitId);
	internal static bool CanPracticeFormations(Actor actor) => HasPrimaryTrait(actor, FormationTraitId);

	internal static bool HasAnyCraftTrait(Actor actor)
	{
		if (actor?.data == null) return false;
		for (int i = 0; i < AllTraitIds.Length; i++)
		{
			if (actor.hasTrait(AllTraitIds[i])) return true;
		}
		return false;
	}

	internal static bool IsCraftTraitId(string traitId)
	{
		if (string.IsNullOrWhiteSpace(traitId)) return false;
		for (int i = 0; i < AllTraitIds.Length; i++)
		{
			if (string.Equals(traitId, AllTraitIds[i], StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static string GetPrimaryTraitId(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		for (int i = 0; i < AllTraitIds.Length; i++)
		{
			if (actor.hasTrait(AllTraitIds[i])) return AllTraitIds[i];
		}
		return string.Empty;
	}

	internal static bool TryGrantExclusive(Actor actor, string traitId)
	{
		if (actor?.data == null || !IsCraftTraitId(traitId) || HasAnyCraftTrait(actor)) return false;
		bool granted = actor.addTrait(traitId, false);
		if (granted)
		{
			NormalizeExclusive(actor, traitId);
			XjCraftActorIndex.Observe(actor);
		}
		return granted;
	}

	internal static void HandleTraitGranted(Actor actor, string traitId, bool granted)
	{
		if (!granted || actor?.data == null || !IsCraftTraitId(traitId)) return;
		NormalizeExclusive(actor, traitId);
		XjCraftActorIndex.Observe(actor);
	}

	internal static void NormalizeExclusive(Actor actor, string preferredTraitId = null)
	{
		if (actor?.data == null) return;
		string keep = IsCraftTraitId(preferredTraitId) && actor.hasTrait(preferredTraitId)
			? preferredTraitId
			: GetPrimaryTraitId(actor);
		if (string.IsNullOrWhiteSpace(keep)) return;

		for (int i = 0; i < AllTraitIds.Length; i++)
		{
			string candidate = AllTraitIds[i];
			if (!string.Equals(candidate, keep, StringComparison.Ordinal) && actor.hasTrait(candidate)) actor.removeTrait(candidate);
		}
		XjCraftActorIndex.Observe(actor);
	}

	private static bool HasPrimaryTrait(Actor actor, string traitId)
	{
		return actor?.data != null && actor.hasTrait(traitId)
			&& string.Equals(GetPrimaryTraitId(actor), traitId, StringComparison.Ordinal);
	}
}
