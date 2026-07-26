using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjVisibleTraitSync
{
	private static readonly string[] RealmTraitIds =
	{
		"XjRealm1",
		"XjRealm2",
		"XjRealm3",
		"XjRealm4",
		"XjRealm5"
	};

	private static readonly string[] PrimaryAptitudeTraitIds =
	{
		"XjZz1",
		"XjZz2",
		"XjZz3",
		"XjZz4",
		"XjZz5",
		"XjZz6"
	};

	private static readonly string[] OverlayAptitudeTraitIds =
	{
		"XjZz7",
		"XjZz8",
		"XjZz9"
	};

	private static readonly string[] ChuShenBaseTraitIds =
	{
		"ChuShen1",
		"ChuShen2",
		"ChuShen3",
		"ChuShen4",
		"ChuShen5"
	};

	private static readonly string[] ChuShenSpecialTraitIds =
	{
		"ChuShen6",
		"ChuShen7",
		"ChuShen8"
	};

	private static readonly string[] LianQiNativeTraitIds =
	{
		"acid_proof", "heat_resistance", "cold_resistance", "poison_immune",
		"attractive", "eagle_eyed", "genius"
	};

	private static readonly string[] ZhuJiNativeTraitIds =
	{
		"dash", "block", "dodge", "backstep", "deflect_projectile"
	};

	private static readonly string[] ZiFuNativeTraitIds =
	{
		"regeneration", "immune", "boosted_vitality", "sunblessed"
	};

	private static readonly string[] JinDanNativeTraitIds =
	{
		"regeneration", "long_liver", "immune", "boosted_vitality", "sunblessed"
	};

	internal static void ClearCultivationDerivedNativeTraits(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		bool changed = RemoveTraits(actor, LianQiNativeTraitIds)
			| RemoveTraits(actor, ZhuJiNativeTraitIds)
			| RemoveTraits(actor, ZiFuNativeTraitIds)
			| RemoveTraits(actor, JinDanNativeTraitIds);
		if (changed)
		{
			actor.setStatsDirty();
		}
	}

	internal static void SyncCultivationTraits(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			if (!XjCultivationEligibility.CanCultivate(actor))
			{
				ClearUnsupportedCultivationState(actor);
				return;
			}

			EnsureCultivatorNoMadness(actor);
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId)
				&& !string.IsNullOrWhiteSpace(currentRealmId))
			{
				XjCultivationStateTransitions.EnsureDaoTuForRealm(actor, currentRealmId, false);
			}
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			XjRealmTitleApplyService.EnsureNoPreZiFuTitle(actor, snapshot.RealmId);
			bool lineageChanged = XuanJianVNext.Systems.Family.XjHighRealmDescendantRules.ReconcileStoredLineage(actor);
			lineageChanged |= XuanJianVNext.Systems.Family.XjHighRealmDescendantRules.RefreshFromParents(actor);
			if (lineageChanged)
			{
				snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			}
			SyncAptitudeTrait(actor, snapshot.XjZz);
			SyncAptitudeOverlayTraits(actor, snapshot.XjZzOverlayMask);
			string visibleDaoTu = snapshot.DaoTu;
			if (!CanShowDaoTu(snapshot.RealmId))
			{
				// Visibility is only a UI/trait projection. Aptitude initialization writes
				// DaoTu before the actor reaches LianQi; clearing it here recreated the
				// exact “有资质、无道途、无入门功法” half-state this release fixes.
				visibleDaoTu = string.Empty;
			}
			SyncDaoTuTrait(actor, visibleDaoTu);
			SyncSingleTraitInGroup(actor, ResolveChuShenBaseTraitId(actor), ChuShenBaseTraitIds);
			SyncSingleTraitInGroup(actor, ResolveChuShenSpecialTraitId(actor), ChuShenSpecialTraitIds);
			SyncRealmTrait(actor, snapshot.RealmId, snapshot.ZhenYuan);
			XjRealmSuppression.SyncCombatLevel(actor);
			XjRealmTitleApplyService.EnsureZiFuTitle(actor, visibleDaoTu, snapshot.XianJiCount);
			if (string.Equals(snapshot.RealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
			{
				XjRealmTitleApplyService.EnsureShenDanTitle(actor, visibleDaoTu);
			}
			XjYinSiTraitLifecycle.ReconcileExclusiveOrigin(actor);
			EnsureCultivatorNoMadness(actor);
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	internal static void ClearUnsupportedCultivationState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		bool permanentZaQiLock = XjCaiQiActorAccessor.IsLianQiByZaQi(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.RealmId, permanentZaQiLock ? XjRealmIds.LianQi : string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, 0f);
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.ClearSuccess(actor);
		XjQiuJinFaAccessor.Clear(actor, "UnsupportedCultivationState");
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjGongFaAccessor.Clear(actor);

		bool changed = RemoveTraits(actor, RealmTraitIds)
			| RemoveTraits(actor, PrimaryAptitudeTraitIds)
			| RemoveTraits(actor, OverlayAptitudeTraitIds)
			| RemoveTraits(actor, ChuShenBaseTraitIds)
			| RemoveTraits(actor, ChuShenSpecialTraitIds)
			| RemoveTraits(actor, XjDaoTuVisibleTraitCatalog.AllTraitIds);
		XjRealmSuppression.SyncCombatLevel(actor);
		XjYinSiTraitLifecycle.ReconcileExclusiveOrigin(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			// Unsupported actors must leave every cultivator-derived runtime index at
			// the same authoritative clear boundary; otherwise candidate/interest
			// snapshots can retain a dead entry until the next cleanup sweep.
			XjCultivatorCache.Remove(actorId);
			XjCombatHotPathCache.Remove(actorId);
		}
		if (changed)
		{
			actor.setStatsDirty();
		}
	}

	private static bool RemoveTraits(Actor actor, string[] traitIds)
	{
		if (actor?.data == null || traitIds == null)
		{
			return false;
		}

		bool changed = false;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId) && ActorHasTrait(actor, traitId))
			{
				actor.removeTrait(traitId);
				changed = true;
			}
		}
		return changed;
	}

	internal static void SyncRealmTrait(Actor actor, string realmId, float zhenYuan)
	{
		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			SyncSingleTraitInGroup(actor, ResolveRealmTraitId(realmId, zhenYuan), RealmTraitIds);
			EnsureRealmNativeTraits(actor, realmId);
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	internal static bool ShouldBlockMadnessTrait(Actor actor, string traitId)
	{
		return string.Equals(traitId, "madness", StringComparison.Ordinal)
			&& IsCultivatorState(actor)
			&& !XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	internal static void EnsureCultivatorNoMadness(Actor actor)
	{
		if (actor?.data == null || !IsCultivatorState(actor))
		{
			return;
		}

		if (XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			XjTrueDamageSystem.EnsureJinXingYaoXieMadState(actor);
			return;
		}

		if (ActorHasTrait(actor, "madness"))
		{
			actor.removeTrait("madness");
			actor.setStatsDirty();
		}
	}

	internal static void EnsureRealmNativeTraits(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return;
		}

		int rank = ResolveRealmRank(realmId);
		bool changed = false;
		if (rank >= 2)
		{
			changed |= EnsureTraits(actor, LianQiNativeTraitIds);
		}

		if (rank >= 3 && ZhuJiNativeTraitIds.Length > 0)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			changed |= EnsureTrait(actor, ZhuJiNativeTraitIds[(int)(Math.Abs(actorId) % ZhuJiNativeTraitIds.Length)]);
		}

		if (rank >= 4)
		{
			changed |= EnsureTraits(actor, ZiFuNativeTraitIds);
		}

		if (rank >= 5)
		{
			changed |= EnsureTraits(actor, JinDanNativeTraitIds);
		}

		// Explicit realm grants can add several companion traits in one pass.
		// Force the native stat cache to reflect them immediately.
		if (changed)
		{
			actor.setStatsDirty();
		}
	}

	private static int ResolveRealmRank(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return 5;
		return 0;
	}

	private static bool EnsureTraits(Actor actor, string[] traitIds)
	{
		if (traitIds == null)
		{
			return false;
		}

		bool changed = false;
		for (int i = 0; i < traitIds.Length; i++)
		{
			changed |= EnsureTrait(actor, traitIds[i]);
		}
		return changed;
	}

	private static bool EnsureTrait(Actor actor, string traitId)
	{
		if (actor?.data != null && IsRegisteredTrait(traitId) && !ActorHasTrait(actor, traitId))
		{
			return actor.addTrait(traitId, false);
		}
		return false;
	}

	internal static void SyncAptitudeTrait(Actor actor, int xjZz)
	{
		if (xjZz >= 1 && xjZz <= 6)
		{
			SyncSingleTraitInGroup(actor, ResolveAptitudeTraitId(xjZz), PrimaryAptitudeTraitIds);
			return;
		}

		SyncSingleTraitInGroup(actor, string.Empty, PrimaryAptitudeTraitIds);
	}

	internal static void SyncAptitudeOverlayTraits(Actor actor, int overlayMask)
	{
		if (actor?.data == null)
		{
			return;
		}

		int normalizedMask = NormalizeAptitudeOverlayMask(actor, overlayMask);
		if (normalizedMask != overlayMask)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, normalizedMask);
		}

		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			for (int i = 0; i < OverlayAptitudeTraitIds.Length; i++)
			{
				string traitId = OverlayAptitudeTraitIds[i];
				bool shouldHave = traitId switch
				{
					"XjZz7" => HasOverlay(normalizedMask, 7),
					"XjZz8" => HasOverlay(normalizedMask, 8),
					"XjZz9" => HasOverlay(normalizedMask, 9),
					_ => false
				};

				if (shouldHave && IsRegisteredTrait(traitId) && !ActorHasTrait(actor, traitId))
				{
					actor.addTrait(traitId, false);
					continue;
				}

				if (!shouldHave && ActorHasTrait(actor, traitId))
				{
					actor.removeTrait(traitId);
				}
			}
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	internal static void SyncDaoTuTrait(Actor actor, string daoTu)
	{
		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			SyncSingleTraitInGroup(actor, ResolveDaoTuTraitId(daoTu), XjDaoTuVisibleTraitCatalog.AllTraitIds);
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	private static bool CanShowDaoTu(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	internal static void SyncChuShenTrait(Actor actor, string chuShenTraitId)
	{
		if (TryParseChuShenRank(chuShenTraitId, out int rank) && rank >= 6)
		{
			SyncSingleTraitInGroup(actor, chuShenTraitId, ChuShenSpecialTraitIds);
			return;
		}

		SyncSingleTraitInGroup(actor, chuShenTraitId, ChuShenBaseTraitIds);
	}

	internal static void SyncChuShenSpecialTrait(Actor actor, string chuShenTraitId)
	{
		SyncSingleTraitInGroup(actor, chuShenTraitId, ChuShenSpecialTraitIds);
	}

	internal static void SyncSingleTraitInGroup(Actor actor, string targetTraitId, string[] groupTraitIds)
	{
		if (actor?.data == null || groupTraitIds == null || groupTraitIds.Length == 0)
		{
			return;
		}

		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			string safeTarget = IsRegisteredTrait(targetTraitId) ? targetTraitId : string.Empty;
			for (int i = 0; i < groupTraitIds.Length; i++)
			{
				string traitId = groupTraitIds[i];
				if (string.IsNullOrWhiteSpace(traitId) || !ActorHasTrait(actor, traitId))
				{
					continue;
				}

				if (!string.Equals(traitId, safeTarget, StringComparison.Ordinal))
				{
					actor.removeTrait(traitId);
				}
			}

			if (!string.IsNullOrWhiteSpace(safeTarget) && !ActorHasTrait(actor, safeTarget))
			{
				actor.addTrait(safeTarget, false);
			}
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	private static string ResolveRealmTraitId(string realmId, float zhenYuan)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return XjTaiXiStageRules.HasEnteredTaiXi(zhenYuan) ? "XjRealm1" : string.Empty;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return "XjRealm2";
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return "XjRealm3";
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return "XjRealm4";
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return "XjRealm5";
		}
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return "XjRealm5";
		}

		return string.Empty;
	}

	private static string ResolveAptitudeTraitId(int xjZz)
	{
		return xjZz switch
		{
			1 => "XjZz1",
			2 => "XjZz2",
			3 => "XjZz3",
			4 => "XjZz4",
			5 => "XjZz5",
			6 => "XjZz6",
			_ => string.Empty
		};
	}

	private static string ResolveChuShenBaseTraitId(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShen, out int chuShen) && chuShen >= 1 && chuShen <= 5)
		{
			return "ChuShen" + chuShen.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		return ResolveExistingTrait(actor, ChuShenBaseTraitIds);
	}

	private static string ResolveChuShenSpecialTraitId(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShenSpecial, out int rank) && rank >= 6 && rank <= 8)
		{
			return "ChuShen" + rank.ToString(System.Globalization.CultureInfo.InvariantCulture);
		}

		return ResolveExistingTrait(actor, ChuShenSpecialTraitIds);
	}

	private static bool TryParseChuShenRank(string traitId, out int rank)
	{
		rank = 0;
		return !string.IsNullOrWhiteSpace(traitId)
			&& traitId.StartsWith("ChuShen", StringComparison.Ordinal)
			&& int.TryParse(traitId.Substring("ChuShen".Length), out rank)
			&& rank >= 1
			&& rank <= 8;
	}

	private static bool HasOverlay(int mask, int overlayId)
	{
		return (mask & (1 << overlayId)) != 0;
	}

	private static int NormalizeAptitudeOverlayMask(Actor actor, int overlayMask)
	{
		if (actor?.data != null
			&& (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan
				|| XjShenDanAccessor.BuildState(actor).Found))
		{
			return overlayMask & ~(1 << 9);
		}

		bool hasXjZz7 = HasOverlay(overlayMask, 7) || ActorHasTrait(actor, "XjZz7");
		if (!hasXjZz7)
		{
			return overlayMask;
		}

		return (overlayMask | (1 << 7)) & ~(1 << 9);
	}

	private static string ResolveDaoTuTraitId(string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (string.IsNullOrEmpty(normalized))
		{
			return string.Empty;
		}

		return XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out string traitId) ? traitId : string.Empty;
	}

	private static string ResolveExistingTrait(Actor actor, string[] traitIds)
	{
		if (actor?.data == null || traitIds == null)
		{
			return string.Empty;
		}

		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId) && ActorHasTrait(actor, traitId))
			{
				return traitId;
			}
		}

		return string.Empty;
	}

	private static bool ActorHasTrait(Actor actor, string traitId)
	{
		return actor?.data != null && !string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId);
	}

	private static bool IsCultivatorState(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& ResolveRealmRank(realmId) > 0)
		{
			return true;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz) && xjZz > 0)
		{
			return true;
		}

		if (!string.IsNullOrWhiteSpace(ResolveExistingTrait(actor, RealmTraitIds)))
		{
			return true;
		}

		return !string.IsNullOrWhiteSpace(ResolveExistingTrait(actor, PrimaryAptitudeTraitIds));
	}

	private static bool IsRegisteredTrait(string traitId)
	{
		if (string.IsNullOrWhiteSpace(traitId) || AssetManager.traits == null)
		{
			return false;
		}

		return AssetManager.traits.get(traitId) != null;
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
