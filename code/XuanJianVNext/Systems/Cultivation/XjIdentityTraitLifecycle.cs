using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjIdentityTraitLifecycle
{
	private static bool _isReconciling;

	internal static bool IsReconciling => _isReconciling;

	internal static bool IsIdentityTrait(string traitId)
	{
		return TryResolveChuShenRank(traitId, out _)
			|| XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out _);
	}

	internal static bool IsIdentityTraitPresent(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		for (int rank = 1; rank <= 8; rank++)
		{
			if (actor.hasTrait("ChuShen" + rank))
			{
				return true;
			}
		}

		string[] daoTuTraits = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < daoTuTraits.Length; i++)
		{
			if (actor.hasTrait(daoTuTraits[i]))
			{
				return true;
			}
		}

		return false;
	}

	internal static void RecordVisibleTraitGranted(Actor actor, string traitId)
	{
		if (actor?.data == null || _isReconciling)
		{
			return;
		}

		try
		{
			_isReconciling = true;
			XjCultivationEligibility.RecordManualCultivationGrant(actor);
			if (!XjCultivationEligibility.CanCultivate(actor))
			{
				if (!string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId))
				{
					actor.removeTrait(traitId);
				}
				XjCultivatorCache.Remove(((BaseSystemData)actor.data).id);
				return;
			}
			if (TryResolveChuShenRank(traitId, out int chuShenRank))
			{
				bool special = chuShenRank >= 6;
				string valueKey = special ? XjActorDataKeys.ChuShenSpecial : XjActorDataKeys.ChuShen;
				string overrideKey = special ? XjActorDataKeys.ChuShenSpecialManualOverride : XjActorDataKeys.ChuShenManualOverride;
				string removedKey = special ? XjActorDataKeys.ChuShenSpecialManualRemoved : XjActorDataKeys.ChuShenManualRemoved;
				XjActorAccessor.TryGetInt(actor, valueKey, out int currentRank);
				XjActorAccessor.TryGetInt(actor, removedKey, out int wasRemoved);
				XjActorAccessor.SetInt(actor, valueKey, chuShenRank);
				if (currentRank != chuShenRank || wasRemoved > 0)
				{
					XjActorAccessor.SetInt(actor, overrideKey, chuShenRank);
				}
				XjActorAccessor.SetInt(actor, removedKey, 0);
				XjVisibleTraitSync.SyncChuShenTrait(actor, traitId);
				if (chuShenRank == 8)
				{
					XjCultivationSeed.EnsureSeedState(actor);
					XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 6);
					XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
					XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, 6);
					XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, 6);
					XjVisibleTraitSync.SyncAptitudeTrait(actor, 6);
				}
				XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false);
				return;
			}

			if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string daoTu))
			{
				XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, true);
				XjManualCultivationWake.EnsureAwake(actor, ensureMinimumAptitude: false);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static bool TryReconcileDaoTuFromVisibleTraits(Actor actor)
	{
		if (actor?.data == null || _isReconciling)
		{
			return false;
		}

		string visibleDaoTu = ResolveVisibleDaoTu(actor);
		if (string.IsNullOrWhiteSpace(visibleDaoTu))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
		if (string.Equals(Normalize(currentDaoTu), Normalize(visibleDaoTu), StringComparison.Ordinal))
		{
			return false;
		}

		try
		{
			_isReconciling = true;
			XjCultivationStateTransitions.TrySetDaoTu(actor, visibleDaoTu, true);
			return true;
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static void RecordVisibleTraitRemoved(Actor actor, string traitId)
	{
		if (actor?.data == null || _isReconciling)
		{
			return;
		}

		try
		{
			_isReconciling = true;
			if (TryResolveChuShenRank(traitId, out int removedRank))
			{
				bool special = removedRank >= 6;
				string valueKey = special ? XjActorDataKeys.ChuShenSpecial : XjActorDataKeys.ChuShen;
				string overrideKey = special ? XjActorDataKeys.ChuShenSpecialManualOverride : XjActorDataKeys.ChuShenManualOverride;
				string removedKey = special ? XjActorDataKeys.ChuShenSpecialManualRemoved : XjActorDataKeys.ChuShenManualRemoved;
				XjActorAccessor.TryGetInt(actor, valueKey, out int currentRank);
				if (currentRank == removedRank)
				{
					XjActorAccessor.SetInt(actor, valueKey, 0);
					XjActorAccessor.SetInt(actor, overrideKey, 0);
					XjActorAccessor.SetInt(actor, removedKey, 1);
				}
				return;
			}

			if (!XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string removedDaoTu))
			{
				return;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
			if (string.Equals(Normalize(currentDaoTu), Normalize(removedDaoTu), StringComparison.Ordinal))
			{
				XjCultivationStateTransitions.ClearDaoTu(actor, false);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}

	private static bool TryResolveChuShenRank(string traitId, out int rank)
	{
		rank = 0;
		if (string.IsNullOrWhiteSpace(traitId)
			|| !traitId.StartsWith("ChuShen", StringComparison.Ordinal)
			|| !int.TryParse(traitId.Substring("ChuShen".Length), out int parsed)
			|| parsed < 1
			|| parsed > 8)
		{
			return false;
		}

		rank = parsed;
		return true;
	}

	private static string ResolveVisibleDaoTu(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		string[] traitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId)
				&& actor.hasTrait(traitId)
				&& XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out string daoTu)
				&& !string.IsNullOrWhiteSpace(daoTu))
			{
				return daoTu.Trim();
			}
		}

		return string.Empty;
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
