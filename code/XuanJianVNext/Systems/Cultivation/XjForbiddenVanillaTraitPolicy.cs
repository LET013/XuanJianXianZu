using System;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjForbiddenVanillaTraitPolicy
{
	internal const string LongLiverTraitId = "long_liver";
	internal static bool ShouldBlock(string traitId) => string.Equals((traitId ?? string.Empty).Trim(), LongLiverTraitId, StringComparison.Ordinal);

	internal static bool Reconcile(Actor actor)
	{
		if (actor?.data == null || !actor.hasTrait(LongLiverTraitId)) return false;
		actor.removeTrait(LongLiverTraitId);
		actor.setStatsDirty();
		return true;
	}

	internal static void DisableManualGrant()
	{
		try
		{
			ActorTrait trait = AssetManager.traits.get(LongLiverTraitId);
			if (trait == null) return;
			trait.can_be_given = false;
			trait.show_in_meta_editor = false;
		}
		catch (System.Exception xjCaught27) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjForbiddenVanillaTraitPolicy.cs:27", xjCaught27); }
	}
}
