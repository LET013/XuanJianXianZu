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
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return;
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

			if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out _))
			{
				// DaoTu visible traits are projection-only here. Explicit editor writes are
				// handled once by XjManualRealmTraitReconciliation via ManualTraitEditContext.
				// Script/mod grants must not reverse-project a UI trait into cultivation state.
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string authoritativeDaoTu);
				if (!string.IsNullOrWhiteSpace(authoritativeDaoTu))
				{
					XjVisibleTraitSync.SyncDaoTuTrait(actor, authoritativeDaoTu);
				}
				else if (actor.hasTrait(traitId))
				{
					actor.removeTrait(traitId);
				}
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
		string normalizedCurrent = Normalize(currentDaoTu);
		string normalizedVisible = Normalize(visibleDaoTu);
		string projectedVisible = Normalize(
			XjVisibleTraitSync.ResolveVisibleDaoTuTraitProjection(actor, currentDaoTu));

		// 闰位的可见特质是 ManifestDaoTu，而底层 DaoTu 必须继续保留 SourceDaoTu。
		// 因此“坎水根道 + 渊照可见特质”已经是一致状态，绝不能把渊照反写成根道。
		// 旧档若还残留根道特质，也在这里直接收敛到显道特质。
		if (normalizedCurrent.Length > 0
			&& (string.Equals(normalizedVisible, projectedVisible, StringComparison.Ordinal)
				|| (!string.Equals(projectedVisible, normalizedCurrent, StringComparison.Ordinal)
					&& string.Equals(normalizedVisible, normalizedCurrent, StringComparison.Ordinal))))
		{
			XjVisibleTraitSync.SyncDaoTuTrait(actor, currentDaoTu);
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
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return;
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
			string expectedVisibleDaoTu = XjVisibleTraitSync.ResolveVisibleDaoTuTraitProjection(actor, currentDaoTu);
			if (string.Equals(Normalize(expectedVisibleDaoTu), Normalize(removedDaoTu), StringComparison.Ordinal))
			{
				// 道途是权威修炼身份，不允许单独删除当前应显示的道途特质。
				// 闰位这里保护的是显道特质，而不是底层根道特质。
				XjVisibleTraitSync.SyncDaoTuTrait(actor, currentDaoTu);
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
