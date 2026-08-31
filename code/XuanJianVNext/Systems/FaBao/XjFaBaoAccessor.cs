using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoAccessor
{
	internal static bool HasState(Actor actor)
	{
		if (actor?.data == null || !XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoId, out string id);
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoName, out string name);
		return !string.IsNullOrWhiteSpace(name);
	}

	internal static XjFaBaoState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjFaBaoState(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, "ActorInvalid");
		}

		if (!XjSafeCore.IsAliveActor(actor))
		{
			return new XjFaBaoState(false, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, 0, "ActorInvalidOrDead");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoId, out string id);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoDaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoClass, out string className);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoKind, out string kind);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoRole, out string role);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoAffixes, out string affixes);
		role = XjFaBaoCatalog.NormalizeRole(kind, role);
		affixes = XjFaBaoCatalog.NormalizeAffixesForClass(affixes, role, className);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoDescription, out string description);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoSource, out string source);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFaBaoYear, out int year);
		description = XjFaBaoDescriptionFormatter.NormalizeGeneratedDescription(
			actor, name, daoTu, className, kind, role, source, description);

		bool found = !string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(name);
		return new XjFaBaoState(
			found,
			id,
			name,
			daoTu,
			className,
			kind,
			role,
			affixes,
			description,
			source,
			year,
			found ? "Ok" : "NoFaBao");
	}

	internal static void ClearState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoId, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoClass, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoKind, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoRole, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoAffixes, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoDescription, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoYear, 0);

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			XjFaBaoBonusService.Forget(actorId);
			XjCombatHotPathCache.Remove(actorId);
		}
	}

	internal static void WriteState(Actor actor, in XjFaBaoState state)
	{
		if (actor?.data == null || !state.Found || string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.Name))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoId, state.Id);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoName, state.Name);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoDaoTu, state.DaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoClass, state.ClassName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoKind, state.Kind);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoRole, state.Role);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoAffixes, state.Affixes);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoDescription, state.Description);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFaBaoSource, state.Source);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoYear, state.Year);
		XjFaBaoAcquisition.RegisterKnownName(state.Name);
		long actorId = ((BaseSystemData)actor.data).id;
		XjFaBaoBonusService.Forget(actorId);
		XjCombatHotPathCache.Remove(actorId);
		XjRuntimeActorInterestIndex.Observe(actor);
	}
}
