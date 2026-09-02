using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjVisibleTraitSync
{
	private static int _shiEquivalentNativeTraitSyncDepth;

	private static readonly string[] RealmTraitIds =
	{
		XjRealmIds.TaiXi,
		XjRealmIds.LianQi,
		XjRealmIds.ZhuJi,
		XjRealmIds.ZiFu,
		XjRealmIds.JinDan,
		XjXianGuoSystem.InstitutionalZhuJiTraitId,
		XjXianGuoSystem.InstitutionalZiFuTraitId,
		XjXianGuoSystem.InstitutionalFakeJinDanTraitId,
		XjRealmIds.DaoTai,
		XjRealmIds.HuangGuan,
		XjRealmIds.FuQiZhenRen,
		XjRealmIds.ZhenJunYuShi,
		XjRealmIds.FuQiDaoTai
	};

	private static readonly string[] FuQiLineageTraitIds =
	{
		"XjLongGengDaoTong"
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
		"ChuShen8",
		"XjJinXingReincarnation"
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
		"regeneration", "immune", "boosted_vitality", "sunblessed"
	};

	private static readonly string[] HiddenLegacyShenTongMarkerTraitIds =
	{
		XjXuanJianShenTongSpecials.LegacyJieLinZhangTraitId,
		XjXuanJianShenTongSpecials.LegacyYuYiWenTraitId
	};

	private static readonly string[] RemovedLegacyShiDebugTraitIds =
	{
		"DebugYaoShuGreatSageManifest",
		"DebugEnterGuShi", "DebugEnterJinShi",
		"DebugShiGreatDesire", "DebugShiWrath", "DebugShiDharmaAdmiration",
		"DebugShiDiscipline", "DebugShiGoodJoy", "DebugShiCompassion", "DebugShiEmptiness",
		"DebugShiMingShu", "DebugShiAdvance", "DebugShiSeedMoHe", "DebugShiSeedDharmaForm",
		"DebugShiAdvanceDharmaFormStage", "DebugShiToggleDomain", "DebugShiAbsorbJinDi",
		"DebugShiReincarnation", "DebugShiTrueSpiritLock"
	};

	internal static void ClearCultivationDerivedNativeTraits(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		bool changed = XjForbiddenVanillaTraitPolicy.Reconcile(actor);
		changed |= RemoveTraits(actor, LianQiNativeTraitIds)
			| RemoveTraits(actor, ZhuJiNativeTraitIds)
			| RemoveTraits(actor, ZiFuNativeTraitIds)
			| RemoveTraits(actor, JinDanNativeTraitIds);
		if (changed)
		{
			actor.setStatsDirty();
		}
	}

	internal static bool HasCultivationDerivedNativeTraits(Actor actor)
	{
		if (actor?.data == null) return false;
		return HasAnyTrait(actor, LianQiNativeTraitIds)
			|| HasAnyTrait(actor, ZhuJiNativeTraitIds)
			|| HasAnyTrait(actor, ZiFuNativeTraitIds)
			|| HasAnyTrait(actor, JinDanNativeTraitIds);
	}

	/// <summary>
	/// 释修种子从获得之日起就与仙修境界伴生特质互斥。禁止发生在原生 addTrait
	/// 入口，而不是等释修年度/人物页同步时再删除，避免 trait 集与 UnitWindow 反复震荡。
	/// </summary>
	internal static bool ShouldBlockRealmNativeTraitForShi(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)
			|| !IsCultivationDerivedNativeTrait(traitId)) return false;
		// 释修仍不能从原生境界/编辑器路径任意获得仙修伴生特质；唯一例外是
		// XjShiVisibleTraitSync 根据现有战斗等效境界进行的受控投影。
		if (_shiEquivalentNativeTraitSyncDepth > 0) return false;
		return actor.hasTrait(XjShiTraitIds.Seed) || XjCultivationPathRules.IsShi(actor);
	}

	private static bool IsCultivationDerivedNativeTrait(string traitId)
	{
		return ContainsTraitId(LianQiNativeTraitIds, traitId)
			|| ContainsTraitId(ZhuJiNativeTraitIds, traitId)
			|| ContainsTraitId(ZiFuNativeTraitIds, traitId)
			|| ContainsTraitId(JinDanNativeTraitIds, traitId);
	}

	private static bool HasAnyTrait(Actor actor, string[] traitIds)
	{
		if (actor?.data == null || traitIds == null) return false;
		for (int i = 0; i < traitIds.Length; i++)
		{
			string traitId = traitIds[i];
			if (!string.IsNullOrWhiteSpace(traitId) && ActorHasTrait(actor, traitId)) return true;
		}
		return false;
	}

	internal static void SyncCultivationTraits(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjForbiddenVanillaTraitPolicy.Reconcile(actor);
		// 仙朝中枢官位是独立身份投影：是否为【国之重臣】完全由朝廷权威档案决定。
		XjXianGuoSystem.SyncCourtIdentityTrait(actor);
		if (RemoveTraits(actor, RemovedLegacyShiDebugTraitIds))
		{
			// 0.9.9.2起释修测试入口不再注册为陆江仙模拟器特质。
			// 旧档只清除可见遗留，不回放任何调试行为。
			actor.setStatsDirty();
		}
		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			if (!XjCultivationEligibility.CanCultivate(actor)
				&& !XjCultivationEligibility.CanRunManagedLongShuCultivation(actor))
			{
				ClearUnsupportedCultivationState(actor);
				return;
			}

			// 人物页刷新时同步修复旧档中的果位钟爱错道，玩家无需等待
			// 下一次世界年度结算。
			XjGuoWeiFavoredDaoTuLock.ReconcileActor(actor, syncVisibleTraits: false);

			EnsureCultivatorNoMadness(actor);
			if (XjCultivationPathRules.IsShi(actor))
			{
				XjShiState.EnsureConsistent(actor, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
				// XjZz 是仙修根骨投影，不是释修命数。旧档/转修可保留潜在数据，
				// 但处于释修体系时永远不显示资质与资质叠加特质。
				SyncAptitudeTrait(actor, 0);
				SyncAptitudeOverlayTraits(actor, 0);
				SyncDaoTuTrait(actor, string.Empty);
				SyncFuQiLineageTrait(actor);
				SyncSingleTraitInGroup(actor, ResolveChuShenBaseTraitId(actor), ChuShenBaseTraitIds);
				SyncSingleTraitInGroup(actor, ResolveChuShenSpecialTraitId(actor), ChuShenSpecialTraitIds);
				SyncSingleTraitInGroup(actor, string.Empty, RealmTraitIds);
				XjShiVisibleTraitSync.Sync(actor);
				XjRealmSuppression.SyncCombatLevel(actor);
				RemoveTraits(actor, HiddenLegacyShenTongMarkerTraitIds);
				EnsureCultivatorNoMadness(actor);
				return;
			}

			// 非释修角色必须清掉旧档或失败补录遗留的释修投影。
			XjShiVisibleTraitSync.Sync(actor);
			XjCultivationPathTransitions.ReconcileFuQiDaoTuIdentity(actor);
			XjFuQiSwordWorldState.EnsureEstablishedDaoIdentity(actor, false);
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId)
				&& !string.IsNullOrWhiteSpace(currentRealmId))
			{
				XjCultivationStateTransitions.EnsureDaoTuForRealm(actor, currentRealmId, false);
			}
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			bool lineageChanged = XuanJianVNext.Systems.Family.XjHighRealmDescendantRules.ReconcileStoredLineage(actor);
			lineageChanged |= XuanJianVNext.Systems.Family.XjHighRealmDescendantRules.RefreshFromParents(actor);
			if (lineageChanged)
			{
				snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			}
			SyncAptitudeTrait(actor, snapshot.XjZz);
			SyncAptitudeOverlayTraits(actor, snapshot.XjZzOverlayMask);
			string visibleDaoTu = snapshot.DaoTu;
			if (!ShouldProjectDaoTuTrait(actor, snapshot.RealmId))
			{
				// Visibility is only a UI/trait projection. Aptitude initialization writes
				// DaoTu before the actor reaches LianQi; clearing it here recreated the
				// exact “有资质、无道途、无入门功法” half-state this release fixes.
				visibleDaoTu = string.Empty;
			}
			// 闰位显道投影统一由 SyncDaoTuTrait 处理。这里只传递权威根道途，
			// 避免 full sync 与 direct sync 各自解释一次，形成两个投影权威。
			SyncDaoTuTrait(actor, visibleDaoTu);
			SyncFuQiLineageTrait(actor);
			SyncSingleTraitInGroup(actor, ResolveChuShenBaseTraitId(actor), ChuShenBaseTraitIds);
			SyncSingleTraitInGroup(actor, ResolveChuShenSpecialTraitId(actor), ChuShenSpecialTraitIds);
			SyncRealmTrait(actor, snapshot.RealmId, snapshot.ZhenYuan);
			XjRealmSuppression.SyncCombatLevel(actor);
			XjRealmTitleApplyService.EnsureTitleForRealm(actor, snapshot.RealmId, visibleDaoTu);
			XjYinSiTraitLifecycle.ReconcileExclusiveOrigin(actor);
			bool removedLegacyShenTongMarkers = RemoveTraits(actor, HiddenLegacyShenTongMarkerTraitIds);
			EnsureCultivatorNoMadness(actor);
			if (removedLegacyShenTongMarkers)
			{
				actor.setStatsDirty();
			}
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
		if (XjCultivationEligibility.IsSupportedNativeCultivationSpecies(actor)
			&& XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _))
		{
			XjGuoWeiFavoredDaoTuLock.ReconcileActor(actor, syncVisibleTraits: false);
			return;
		}

		bool permanentZaQiLock = XjCultivationEligibility.IsSupportedNativeCultivationSpecies(actor)
			&& XjCaiQiActorAccessor.IsLianQiByZaQi(actor);
		XjCultivationStateTransitions.ResetIdentityMetadataForAuthorityClear(
			actor, permanentZaQiLock ? XjRealmIds.LianQi : string.Empty);
		XjCultivationPathTransitions.ClearAll(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualCultivationGrant, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
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
			| RemoveTraits(actor, XjShiVisibleTraitSync.RealmTraitIds)
			| RemoveTraits(actor, XjShiVisibleTraitSync.TraditionTraitIds)
			| RemoveTraits(actor, FuQiLineageTraitIds)
			| RemoveTraits(actor, PrimaryAptitudeTraitIds)
			| RemoveTraits(actor, OverlayAptitudeTraitIds)
			| RemoveTraits(actor, ChuShenBaseTraitIds)
			| RemoveTraits(actor, ChuShenSpecialTraitIds)
			| RemoveTraits(actor, HiddenLegacyShenTongMarkerTraitIds)
			| RemoveTraits(actor, XjDaoTuVisibleTraitCatalog.AllTraitIds);
		XjRealmSuppression.SyncCombatLevel(actor);
		XjYinSiTraitLifecycle.ReconcileExclusiveOrigin(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			// Unsupported actors must leave every cultivator-derived runtime index at
			// the same authoritative clear boundary; otherwise candidate/interest
			// snapshots can retain a dead entry until the next cleanup sweep.
			XjAptitudeTraitLifecycle.ForgetRuntimeActor(actorId);
			XjCultivatorCache.Remove(actorId);
			XjCombatHotPathCache.Remove(actorId);
		}
		if (changed)
		{
			actor.setStatsDirty();
		}
	}


	/// <summary>
	/// Clears XuanJian cultivation authority from a third-party independent clone.
	/// Unlike ClearUnsupportedCultivationState this path intentionally ignores favored-DaoTu
	/// locks and ZaQi permanence: a clone is a new actor and must not duplicate the source's
	/// realm, fruit-position authority, aptitude identity, gongfa or high-realm success state.
	/// Family/bloodline data is intentionally left alone.
	/// </summary>
	internal static void ClearIndependentCloneCultivationState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjCultivationStateTransitions.ResetIdentityMetadataForAuthorityClear(actor, string.Empty);
		XjCultivationPathTransitions.ClearAll(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualCultivationGrant, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzEffectApplied, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz9LastPenaltyYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenManualOverride, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenManualRemoved, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecialManualOverride, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecialManualRemoved, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmManualRemoved, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, 0f);

		// A Duplicator result is not a reincarnation or a second bearer of the source's
		// Shi authority. Clear the independent Shi payload before registering the new actor.
		XjShiState.ClearDataOnly(actor);
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.ClearSuccess(actor);
		XjQiuJinFaAccessor.Clear(actor, "IndependentExternalClone");
		XjXianJiAccessor.ReconcileRealmLimit(actor);
		XjGongFaAccessor.Clear(actor);

		bool changed = RemoveTraits(actor, RealmTraitIds)
			| RemoveTraits(actor, XjShiVisibleTraitSync.RealmTraitIds)
			| RemoveTraits(actor, XjShiVisibleTraitSync.TraditionTraitIds)
			| RemoveTraits(actor, FuQiLineageTraitIds)
			| RemoveTraits(actor, PrimaryAptitudeTraitIds)
			| RemoveTraits(actor, OverlayAptitudeTraitIds)
			| RemoveTraits(actor, ChuShenBaseTraitIds)
			| RemoveTraits(actor, ChuShenSpecialTraitIds)
			| RemoveTraits(actor, HiddenLegacyShenTongMarkerTraitIds)
			| RemoveTraits(actor, XjDaoTuVisibleTraitCatalog.AllTraitIds);

		// ActorTool.copyUnitToOtherUnit may also carry saved traits that are not active yet.
		// Remove those projections or a later reload can resurrect the source's realm/DaoTu.
		ClearIndependentCloneSavedCultivationTraits(actor.data.saved_traits);

		XjRealmSuppression.SyncCombatLevel(actor);
		XjYinSiTraitLifecycle.ReconcileExclusiveOrigin(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			XjAptitudeTraitLifecycle.ForgetRuntimeActor(actorId);
			XjCultivatorCache.Remove(actorId);
			XjCombatHotPathCache.Remove(actorId);
		}
		if (changed)
		{
			actor.setStatsDirty();
		}
	}

	private static void ClearIndependentCloneSavedCultivationTraits(System.Collections.Generic.List<string> savedTraits)
	{
		if (savedTraits == null)
		{
			return;
		}

		savedTraits.RemoveAll(traitId =>
			ContainsTraitId(RealmTraitIds, traitId)
			|| ContainsTraitId(XjShiVisibleTraitSync.RealmTraitIds, traitId)
			|| ContainsTraitId(XjShiVisibleTraitSync.TraditionTraitIds, traitId)
			|| ContainsTraitId(FuQiLineageTraitIds, traitId)
			|| ContainsTraitId(PrimaryAptitudeTraitIds, traitId)
			|| ContainsTraitId(OverlayAptitudeTraitIds, traitId)
			|| ContainsTraitId(ChuShenBaseTraitIds, traitId)
			|| ContainsTraitId(ChuShenSpecialTraitIds, traitId)
			|| ContainsTraitId(HiddenLegacyShenTongMarkerTraitIds, traitId)
			|| ContainsTraitId(XjDaoTuVisibleTraitCatalog.AllTraitIds, traitId));
	}

	private static bool ContainsTraitId(string[] traitIds, string traitId)
	{
		if (traitIds == null || string.IsNullOrWhiteSpace(traitId))
		{
			return false;
		}

		for (int i = 0; i < traitIds.Length; i++)
		{
			if (string.Equals(traitIds[i], traitId, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
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
			if (string.IsNullOrWhiteSpace(traitId)) continue;

			if (ActorHasTrait(actor, traitId))
			{
				actor.removeTrait(traitId);
				changed = true;
			}

			// 已移除注册的旧调试特质可能只残留在 saved_traits，无法再由
			// ActorTrait 资源反查。直接修剪持久化标记，防止旧存档留下幽灵特质。
			if (actor.data.saved_traits == null) continue;
			for (int savedIndex = actor.data.saved_traits.Count - 1; savedIndex >= 0; savedIndex--)
			{
				if (!string.Equals(actor.data.saved_traits[savedIndex], traitId, StringComparison.Ordinal)) continue;
				actor.data.saved_traits.RemoveAt(savedIndex);
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
			string visibleRealmTraitId = ResolveRealmTraitId(realmId, zhenYuan);
			int realTier = XjRealmSuppression.GetRealmTierFromIdForRuntime(XjRealmHelper.NormalizeId(realmId));
			if (XjXianGuoSystem.TryGetInstitutionalProjectionTier(actor, out int projectedTier)
				&& projectedTier > realTier)
			{
				// 持玄只抬高“当前可见境界”，不创造第二套境界特质。
				// 权威 RealmId 仍保持本人真实修为，离朝后自然回落。
				visibleRealmTraitId = projectedTier switch
				{
					XjRealmSuppression.TierZhuJi => XjRealmIds.ZhuJi,
					XjRealmSuppression.TierZiFu => XjRealmIds.ZiFu,
					XjRealmSuppression.TierJinDan => XjRealmIds.JinDan,
					_ => visibleRealmTraitId
				};
			}
			SyncSingleTraitInGroup(actor, visibleRealmTraitId, RealmTraitIds);
			// 伴生境界印记只跟本人真实 RealmId，不跟借来的制度位格；否则离朝后
			// 会遗留紫府/金丹伴生收益。
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
			&& IsManagedCultivatorState(actor)
			&& !XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	internal static void EnsureCultivatorNoMadness(Actor actor)
	{
		if (actor?.data == null || !IsManagedCultivatorState(actor))
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


	internal static void SyncShiEquivalentNativeTraits(Actor actor, string equivalentRealmId)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;

		int rank = ResolveRealmRank(equivalentRealmId);
		if (rank <= 0) return;

		bool changed = false;
		_shiEquivalentNativeTraitSyncDepth++;
		try
		{
			if (rank >= 2) changed |= EnsureTraits(actor, LianQiNativeTraitIds);
			if (rank >= 3 && ZhuJiNativeTraitIds.Length > 0)
			{
				long actorId = ((BaseSystemData)actor.data).id;
				int index = (int)(Math.Abs(actorId) % ZhuJiNativeTraitIds.Length);
				changed |= EnsureTrait(actor, ZhuJiNativeTraitIds[index]);
			}
			if (rank >= 4) changed |= EnsureTraits(actor, ZiFuNativeTraitIds);
			if (rank >= 5) changed |= EnsureTraits(actor, JinDanNativeTraitIds);
		}
		finally
		{
			_shiEquivalentNativeTraitSyncDepth--;
		}

		if (changed) actor.setStatsDirty();
	}

	internal static void EnsureRealmNativeTraits(Actor actor, string realmId)
	{
		if (actor?.data == null
			|| actor.hasTrait(XjShiTraitIds.Seed)
			|| XjCultivationPathRules.IsShi(actor))
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
		realmId = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 4;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 5;
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)) return 6;
		if (string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 6;
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
			// All callers pass the authoritative/root DaoTu. RunWei is the one display
			// exception: its visible trait is the persisted manifest DaoTu. Keeping this
			// projection inside the single sink prevents maintenance/repair callers from
			// periodically replacing the manifest trait with the root trait.
			string projected = ResolveVisibleDaoTuTraitProjection(actor, daoTu);
			SyncSingleTraitInGroup(actor, ResolveDaoTuTraitId(projected), XjDaoTuVisibleTraitCatalog.AllTraitIds);
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	internal static void SyncFuQiLineageTrait(Actor actor)
	{
		string targetTraitId = string.Empty;
		if (actor?.data != null)
		{
			if (XjCultivationPathRules.IsFuQiYangXing(actor)
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineageId)
				&& string.Equals(lineageId, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
			{
				targetTraitId = XjFuQiSwordWorldState.IsEstablished
					? "XjLongGengDaoTong"
					: string.Empty;
			}
			else if (XjCultivationPathRules.IsZiFuJinDan(actor)
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
				&& string.Equals((daoTu ?? string.Empty).Trim(), XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal))
			{
				targetTraitId = "XjLongGengDaoTong";
			}
		}
		SyncSingleTraitInGroup(actor, targetTraitId, FuQiLineageTraitIds);
	}

	/// <summary>
	/// Returns whether the authoritative DaoTu should currently have a visible trait projection.
	/// DaoTu may be established before LianQi, but that is an internal preference/identity field
	/// and must stay hidden until the route reaches the display stage. This method intentionally
	/// does not reinterpret CaiQi/ZaQi gameplay semantics; it only answers projection timing.
	/// </summary>
	internal static bool ShouldProjectDaoTuTrait(Actor actor, string realmId)
	{
		if (actor?.data == null || XjCultivationPathRules.IsShi(actor))
		{
			return false;
		}

		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string fuQiDaoTu)
				&& !string.IsNullOrWhiteSpace(fuQiDaoTu);
		}

		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalizedRealm, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	/// <summary>
	/// 闰位需要把果位的显道投影为可见道途特质，但修行根基、功法与神通归属
	/// 始终保留在 source DaoTu。该方法是纯 UI 投影，绝不写入角色数据。
	/// </summary>
	internal static string ResolveVisibleDaoTuTraitProjection(Actor actor, string rootDaoTu)
	{
		string fallback = (rootDaoTu ?? string.Empty).Trim();
		if (actor?.data == null || string.IsNullOrWhiteSpace(fallback)) return fallback;

		try
		{
			XjJinDanState position = XjJinDanAccessor.BuildPositionCarrierState(actor);
			if (!position.Found
				|| !string.Equals(
					XjGuoWeiRegistry.ResolveTypeFromName(position.GuoWei),
					XjGuoWeiCalculator.RunWei,
					StringComparison.Ordinal))
			{
				return fallback;
			}

			if (!XjHighRealmDaoStateService.TryReadPersistedIntercalaryIdentity(
				actor, out string sourceDaoTu, out string manifestDaoTu)
				|| string.IsNullOrWhiteSpace(sourceDaoTu)
				|| string.IsNullOrWhiteSpace(manifestDaoTu)
				|| string.Equals(sourceDaoTu, manifestDaoTu, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(ResolveDaoTuTraitId(manifestDaoTu)))
			{
				return fallback;
			}

			return manifestDaoTu.Trim();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Cultivation.VisibleTraitSync.ResolveRunWeiProjection", ex);
			return fallback;
		}
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
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal))
		{
			return XjRealmIds.DaoTai;
		}
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return XjRealmIds.JinDan;
		}
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return XjRealmIds.HuangGuan;
		}
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return XjRealmIds.FuQiZhenRen;
		}
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			return XjRealmIds.ZhenJunYuShi;
		}
		if (string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
		{
			return XjRealmIds.FuQiDaoTai;
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

		// 金性转世是本次轮回的权威特殊出身，必须优先于新生角色随机取得的
		// ChuShen6—8；否则后续可见特质同步会把用户要求的转世标记移除。
		if (ActorHasTrait(actor, "XjJinXingReincarnation"))
		{
			return "XjJinXingReincarnation";
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
			&& XjCultivationPathRules.IsZiFuJinDan(actor)
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

	private static bool IsManagedCultivatorState(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		// 释修与仙修都属于玄鉴托管修炼体系。疯狂属于 WorldBox 原生行为特质，
		// 正常修士一旦进入任一玄鉴修炼链就必须清除；唯一例外仍是金性妖邪。
		return IsCultivatorState(actor)
			|| actor.hasTrait(XjShiTraitIds.Seed)
			|| XjCultivationPathRules.IsShi(actor);
	}

	private static bool IsCultivatorState(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjRealmHelper.IsKnownTag(realmId))
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
