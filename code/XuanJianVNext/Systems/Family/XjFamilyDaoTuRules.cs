using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// Resolves a stable paternal-family DaoTu only at lifecycle boundaries.
/// It never scans the world and never runs from UI reads.
/// </summary>
internal static class XjFamilyDaoTuRules
{
	internal static bool TryEnsureCultivatorDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null || !HasCultivationIdentity(actor))
		{
			return false;
		}

		if (TryReadValidForActor(actor, XjActorDataKeys.DaoTu, out string current))
		{
			daoTu = current;
			EnsureOriginAfterSuccessfulAssignment(actor, daoTu);
			return true;
		}

		if (!TryResolvePreferredDaoTu(actor, out daoTu)
			|| !XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false)
			|| !TryReadValidForActor(actor, XjActorDataKeys.DaoTu, out string persisted)
			|| !string.Equals(persisted, daoTu, StringComparison.Ordinal))
		{
			daoTu = string.Empty;
			return false;
		}

		EnsureOriginAfterSuccessfulAssignment(actor, daoTu);
		return true;
	}

	/// <summary>
	/// Resolves the identity DaoTu used when a cultivator is first initialized.
	/// Restricted paths such as QingXuan never participate in the ordinary random pool.
	/// </summary>
	internal static bool TryResolvePreferredDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		if (TryReadValidForActor(actor, XjActorDataKeys.DaoTu, out daoTu)
			|| TryReadAssignable(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu))
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		long stableSeed = actorId;
		if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			stableSeed = identity.FamilyStableIdValue;
			foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
			{
				if (member?.data == null)
				{
					continue;
				}
				if (TryReadAssignable(member, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu)
					|| TryReadAssignable(member, XjActorDataKeys.DaoTu, out daoTu))
				{
					return true;
				}
			}
		}

		List<XjDaoTuVisibleTraitEntry> entries = BuildOrdinaryAssignableEntries();
		if (entries.Count == 0)
		{
			return false;
		}

		daoTu = entries[XjDeterministicHash.PositiveIndex(stableSeed, "family_daotu", entries.Count)].DisplayName;
		return IsOrdinaryAssignable(daoTu);
	}

	/// <summary>
	/// CaiQi family preference is deliberately independent from the actor's current DaoTu.
	/// Only another confirmed family member's persisted bloodline origin can bias a site.
	/// This prevents the identity chain from deciding its own CaiQi result.
	/// </summary>
	internal static bool TryResolveCaiQiFamilyPreference(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			return false;
		}

		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
		{
			if (member?.data == null || ((BaseSystemData)member.data).id == actorId)
			{
				continue;
			}
			if (TryReadAssignable(member, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu))
			{
				return true;
			}
		}

		// 首名家族修士尚无可继承来源时不制造“虚拟家族偏好”，
		// 直接让采气候选回落到当前城市的普通稳定哈希。
		return false;
	}

	private static void EnsureOriginAfterSuccessfulAssignment(Actor actor, string daoTu)
	{
		if (!IsOrdinaryAssignable(daoTu))
		{
			return;
		}
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out string origin)
			|| !IsOrdinaryAssignable(origin))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, daoTu.Trim());
		}
	}

	private static List<XjDaoTuVisibleTraitEntry> BuildOrdinaryAssignableEntries()
	{
		var source = XjDaoTuVisibleTraitCatalog.Entries;
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			if (IsOrdinaryAssignable(source[i].DisplayName))
			{
				result.Add(source[i]);
			}
		}
		return result;
	}

	private static bool HasCultivationIdentity(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)
		{
			return true;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjCultivationStateTransitions.IsKnownRealm(realmId))
		{
			return true;
		}

		return XjGongFaAccessor.BuildState(actor).Found;
	}

	private static bool TryReadValidForActor(Actor actor, string key, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, key, out daoTu)
			|| !IsValid(daoTu))
		{
			return false;
		}

		return !string.Equals(daoTu.Trim(), XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor);
	}

	private static bool TryReadAssignable(Actor actor, string key, out string daoTu)
	{
		daoTu = string.Empty;
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, key, out daoTu)
			&& IsOrdinaryAssignable(daoTu);
	}

	private static bool IsOrdinaryAssignable(string daoTu)
	{
		return IsValid(daoTu)
			&& !string.Equals(daoTu.Trim(), XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal);
	}

	private static bool IsValid(string daoTu)
	{
		return !string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu.Trim(), out _);
	}
}
