using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.CaiQi;

internal readonly struct XjCaiQiFaState
{
	internal static XjCaiQiFaState Empty { get; } = new XjCaiQiFaState(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		0,
		"Empty");

	internal readonly bool Found;
	internal readonly string Name;
	internal readonly string DaoTu;
	internal readonly string SourcePlace;
	internal readonly int SourceYear;
	internal readonly string ReasonCode;

	internal XjCaiQiFaState(
		bool found,
		string name,
		string daoTu,
		string sourcePlace,
		int sourceYear,
		string reasonCode)
	{
		Found = found;
		Name = name ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		SourcePlace = sourcePlace ?? string.Empty;
		SourceYear = sourceYear < 0 ? 0 : sourceYear;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjCaiQiFaAccessor
{
	internal static XjCaiQiFaState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjCaiQiFaState.Empty;
		}

		if (!XjSafeCore.IsAliveActor(actor))
		{
			return new XjCaiQiFaState(false, string.Empty, string.Empty, string.Empty, 0, "ActorInvalidOrDead");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjCaiQiFaName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjCaiQiFaDaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjCaiQiFaSourcePlace, out string sourcePlace);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjCaiQiFaSourceYear, out int sourceYear);

		if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(daoTu))
		{
			return XjCaiQiFaState.Empty;
		}

		return new XjCaiQiFaState(
			true,
			name.Trim(),
			daoTu.Trim(),
			sourcePlace,
			sourceYear,
			"Ok");
	}

	internal static void WriteState(Actor actor, in XjCaiQiFaState state)
	{
		if (actor?.data == null
			|| !XjSafeCore.IsAliveActor(actor)
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !state.Found
			|| string.IsNullOrWhiteSpace(state.Name)
			|| string.IsNullOrWhiteSpace(state.DaoTu))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaName, state.Name.Trim());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaDaoTu, state.DaoTu.Trim());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaSourcePlace, state.SourcePlace);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCaiQiFaSourceYear, state.SourceYear);
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjCaiQiFaSourcePlace, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCaiQiFaSourceYear, 0);
	}
}

internal static class XjCaiQiFaAcquisition
{
	internal static bool TryInheritFromFamily(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| currentYear <= 0)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		XjCaiQiFaState current = XjCaiQiFaAccessor.BuildState(actor);
		if (current.Found && string.Equals(Normalize(current.DaoTu), daoTu, StringComparison.Ordinal))
		{
			return false;
		}

		return TryApplyFamilyCaiQiFa(actor, daoTu, currentYear);
	}

	internal static void TryAcquireFromCaiQiResult(Actor actor, in XjCaiQiResolvedResult result)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !result.Success)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		XjCaiQiFaState current = XjCaiQiFaAccessor.BuildState(actor);
		if (current.Found && string.Equals(Normalize(current.DaoTu), daoTu, System.StringComparison.Ordinal))
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		if (TryApplyFamilyCaiQiFa(actor, daoTu, currentYear))
		{
			return;
		}

		string sourcePlace = BuildSourcePlace(result);
		string name = XjCaiQiFaNameLibrary.GenerateCaiQiFaName(daoTu, GetActorId(actor) + currentYear);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		XjCaiQiFaState state = new XjCaiQiFaState(
			true,
			name,
			daoTu,
			sourcePlace,
			currentYear,
			"Ok");

		XjCaiQiFaAccessor.WriteState(actor, state);
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.CaiQiFaObtained(actor, state.Name, state.DaoTu, state.SourcePlace));
	}

	private static bool TryApplyFamilyCaiQiFa(Actor actor, string daoTu, int currentYear)
	{
		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord familyRecord)
			|| !familyRecord.Found
			|| !string.Equals(familyRecord.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(familyRecord.FamilyKey))
		{
			return false;
		}

		IReadOnlyDictionary<string, int> resources =
			XjFamilyWarehouseReadModel.Shared.ReadFamilyCaiQiFaResources(familyRecord.FamilyKey);
		string selectedName = string.Empty;
		foreach (KeyValuePair<string, int> resource in resources)
		{
			if (resource.Value <= 0)
			{
				continue;
			}

			XjFamilyCaiQiWarehouse.ParseCaiQiFaResourceId(resource.Key, out string candidateName, out string candidateDaoTu);
			if (!string.Equals(Normalize(candidateDaoTu), daoTu, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(candidateName)
				|| (!string.IsNullOrWhiteSpace(selectedName)
					&& string.CompareOrdinal(candidateName, selectedName) >= 0))
			{
				continue;
			}

			selectedName = candidateName.Trim();
		}

		if (string.IsNullOrWhiteSpace(selectedName))
		{
			return false;
		}

		XjCaiQiFaAccessor.WriteState(actor, new XjCaiQiFaState(
			true,
			selectedName,
			daoTu,
			"家族传承",
			currentYear,
			"InheritedFamilyCaiQiFa"));
		return true;
	}

	private static string BuildSourcePlace(in XjCaiQiResolvedResult result)
	{
		if (!string.IsNullOrWhiteSpace(result.SiteName))
		{
			return result.SiteName.Trim();
		}

		if (!string.IsNullOrWhiteSpace(result.BranchId))
		{
			return result.BranchId.Trim();
		}

		return result.PlaceTypeId ?? string.Empty;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static string Normalize(string value)
	{
		return XjStringHelper.Normalize(value);
	}
}
