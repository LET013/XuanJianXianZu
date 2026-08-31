using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Aptitude;

internal readonly struct XjAptitudeTraitState
{
	internal readonly int PrimaryAptitude;
	internal readonly int OverlayMask;

	internal XjAptitudeTraitState(int primaryAptitude, int overlayMask)
	{
		PrimaryAptitude = primaryAptitude;
		OverlayMask = overlayMask;
	}
}

internal static class XjAptitudeTraitLifecycle
{
	private static readonly HashSet<long> ManualXjZz6HolderIds = new HashSet<long>();

	internal const int MaxManualXjZz6Holders = 49;
	internal static int ManualXjZz6HolderCount => ManualXjZz6HolderIds.Count;
	internal const int XjZz7Bit = 1 << 7;
	internal const int XjZz8Bit = 1 << 8;
	internal const int XjZz9Bit = 1 << 9;

	internal static bool TryResolveBit(string traitId, out int bit)
	{
		bit = traitId switch
		{
			"XjZz7" => XjZz7Bit,
			"XjZz8" => XjZz8Bit,
			"XjZz9" => XjZz9Bit,
			_ => 0
		};
		return bit != 0;
	}

	internal static bool TryResolvePrimaryAptitude(string traitId, out int aptitude)
	{
		aptitude = traitId switch
		{
			"XjZz1" => 1,
			"XjZz2" => 2,
			"XjZz3" => 3,
			"XjZz4" => 4,
			"XjZz5" => 5,
			"XjZz6" => 6,
			_ => 0
		};
		return aptitude != 0;
	}

	internal static bool IsAptitudeTrait(string traitId)
	{
		return TryResolvePrimaryAptitude(traitId, out _) || TryResolveBit(traitId, out _);
	}

	internal static bool ShouldBlockVisibleGrant(Actor actor, string traitId)
	{
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return false;
		if (actor?.data != null && XjCultivationPathRules.IsShi(actor) && IsAptitudeTrait(traitId)) return true;

		if (string.Equals(traitId, "XjZz6", StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int currentPrimary);
			if (currentPrimary == 6) return false;
			return ManualXjZz6HolderIds.Count >= MaxManualXjZz6Holders;
		}

		return (string.Equals(traitId, "XjZz8", StringComparison.Ordinal)
				|| string.Equals(traitId, "XjZz9", StringComparison.Ordinal))
			&& HasXjZz7Protection(actor);
	}

	internal static XjAptitudeTraitState CaptureState(Actor actor)
	{
		if (actor?.data == null)
		{
			return default;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int primary);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int overlayMask);
		return new XjAptitudeTraitState(primary, overlayMask);
	}

	/// <summary>
	/// 释修种子和仙道资质是两种互斥的定路，不是“隐藏一下就能并存”的两层外观。
	/// 入释时必须同时清掉主资质、叠加资质与手动授予标记；旧档则由年度入口补做同一收口。
	/// </summary>
	internal static void ClearForShiCommitment(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzEffectApplied, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAptitudeBaseBoundTier, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz9LastPenaltyYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualCultivationGrant, 0);
		XjVisibleTraitSync.SyncAptitudeTrait(actor, 0);
		XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, 0);
		long actorId = ((BaseSystemData)actor.data).id;
		ForgetRuntimeActor(actorId);
		try { actor.setStatsDirty(); } catch { }
	}

	internal static void RecordVisibleTraitGranted(Actor actor, string traitId, in XjAptitudeTraitState previousState)
	{
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return;
		if (actor?.data == null)
		{
			return;
		}
		if (XjCultivationPathRules.IsShi(actor) && IsAptitudeTrait(traitId))
		{
			if (!string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId)) actor.removeTrait(traitId);
			XjVisibleTraitSync.SyncAptitudeTrait(actor, 0);
			XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, 0);
			return;
		}
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

		if (TryResolvePrimaryAptitude(traitId, out int primary))
		{
			if (primary == 6 && previousState.PrimaryAptitude != 6)
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 1);
			else if (primary != 6)
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);

			XjCultivationSeed.EnsureSeedState(actor);
			XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, primary);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, primary);
			ObserveRuntimeActor(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, previousState.OverlayMask);
			XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, primary);
			XjCultivationPathEntrySystem.EnsureInitialPath(actor, primary);
			XjVisibleTraitSync.SyncAptitudeTrait(actor, primary);
			XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, previousState.OverlayMask);
			if (primary >= 1 && primary <= 6)
			{
				XjFamilySupportSystem.NotifySupportTalentMember(actor);
			}
			if (primary == 6)
			{
				XjAutoCollectSystem.TryCollectTianShouDaoMai(actor, "TianShouDaoMaiGranted");
			}
			XjManualCultivationWake.EnsureAwake(actor);
			return;
		}

		if (!TryResolveBit(traitId, out int bit))
		{
			return;
		}

		if ((bit == XjZz8Bit || bit == XjZz9Bit) && HasXjZz7Protection(actor))
		{
			string blockedTrait = bit == XjZz8Bit ? "XjZz8" : "XjZz9";
			if (actor.hasTrait(blockedTrait))
			{
				actor.removeTrait(blockedTrait);
			}
			int protectedMask = previousState.OverlayMask & ~XjZz8Bit & ~XjZz9Bit;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, protectedMask);
			XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, protectedMask);
			XjCultivatorCache.CheckAndUpdate(actor);
			return;
		}

		int nextMask = previousState.OverlayMask | bit;
		if (bit == XjZz7Bit)
		{
			nextMask &= ~XjZz8Bit & ~XjZz9Bit;
		}
		if (previousState.PrimaryAptitude >= 1 && previousState.PrimaryAptitude <= 6)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, previousState.PrimaryAptitude);
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, nextMask);
		XjVisibleTraitSync.SyncAptitudeTrait(actor, previousState.PrimaryAptitude);
		XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, nextMask);
		XjManualCultivationWake.EnsureAwake(actor);
	}

	internal static void RecordVisibleTraitRemoved(Actor actor, string traitId)
	{
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return;
		if (actor?.data == null)
		{
			return;
		}

		if (TryResolvePrimaryAptitude(traitId, out int primary))
		{
			if (primary == 6)
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int current);
			if (current == primary)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 0);
				XjCultivatorCache.CheckAndUpdate(actor);
			}
			ObserveRuntimeActor(actor);
			return;
		}

		if (!TryResolveBit(traitId, out int bit))
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int mask);
		if ((mask & bit) != 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, mask & ~bit);
		}
	}

	internal static void TickAnnual(Actor actor)
	{
		TickAnnual(actor, XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
	}

	internal static void TickAnnual(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive() || year <= 0)
		{
			return;
		}
		if (XjCultivationPathRules.IsShi(actor))
		{
			// 旧档曾只隐藏 XjZz 的可见特质，导致释修种子与仙道资质数据并存。
			ClearForShiCommitment(actor);
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int mask);
		if (IsJinDanOrShenDan(actor))
		{
			ClearXjZz9(actor, mask);
			return;
		}
		if (HasXjZz7Protection(actor))
		{
			int protectedMask = (mask | XjZz7Bit) & ~XjZz8Bit & ~XjZz9Bit;
			if (protectedMask != mask)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, protectedMask);
				XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, protectedMask);
			}
			return;
		}

		bool hadXjZz9 = (mask & XjZz9Bit) != 0;
		if (hadXjZz9)
		{
			ApplyAnnualXjZz9Penalty(actor, year);
			return;
		}

		if (!IsCultivatorInDecliningLifespanWindow(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjDeterministicHash.Roll01(actorId, year, "aptitude_overlay", "xjzz9_lifespan_window") >= 0.1f)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, mask | XjZz9Bit);
		XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, mask | XjZz9Bit);
	}

	internal static bool HasAnnualInterest(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive() || year <= 0)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int mask)
			&& mask != 0)
		{
			return true;
		}

		if (actor.hasTrait("XjZz9"))
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return XjDetectionGate.IsEntityMaintenanceSlot(XjEntityDetectionJob.AptitudeDecliningLifespan, actorId, year)
			&& IsCultivatorInDecliningLifespanWindow(actor);
	}

	internal static void ObserveRuntimeActor(Actor actor)
	{
		if (actor?.data == null) return;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		bool qualifies = XjSafeCore.IsAliveActor(actor)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ManualXjZz6Grant, out int manual)
			&& manual == 1
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int primary)
			&& primary == 6;

		if (qualifies)
		{
			ManualXjZz6HolderIds.Add(actorId);
		}
		else
		{
			ManualXjZz6HolderIds.Remove(actorId);
		}
	}

	internal static void ForgetRuntimeActor(long actorId)
	{
		if (actorId > 0L) ManualXjZz6HolderIds.Remove(actorId);
	}

	internal static void ClearRuntimeIndex()
	{
		ManualXjZz6HolderIds.Clear();
	}

	private static bool HasXjZz7Protection(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (actor.hasTrait("XjZz7"))
		{
			return true;
		}

		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int mask)
			&& (mask & XjZz7Bit) != 0;
	}

	private static bool IsJinDanOrShenDan(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan
			|| XjShenDanAccessor.BuildState(actor).Found;
	}

	private static void ClearXjZz9(Actor actor, int mask)
	{
		if (actor?.data == null)
		{
			return;
		}

		int clearedMask = mask & ~XjZz9Bit;
		bool changed = clearedMask != mask;
		try
		{
			if (actor.hasTrait("XjZz9"))
			{
				actor.removeTrait("XjZz9");
				changed = true;
			}
		}
		catch (System.Exception xjCaught302) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Aptitude/XjAptitudeTraitLifecycle.cs:302", xjCaught302); }
		if (!changed)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzOverlayMask, clearedMask);
		XjVisibleTraitSync.SyncAptitudeOverlayTraits(actor, clearedMask);
		XjCultivatorCache.CheckAndUpdate(actor);
	}

	private static void ApplyAnnualXjZz9Penalty(Actor actor, int year)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz9LastPenaltyYear, out int lastYear);
		if (lastYear >= year)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		float zhenYuanLoss = XjDeterministicHash.RollRange(actorId, year, 9, 1.5f, 15f);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);

		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, (float)Math.Floor(Math.Max(0f, zhenYuan - zhenYuanLoss)));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz9LastPenaltyYear, year);
	}

	private static bool IsCultivatorInDecliningLifespanWindow(Actor actor)
	{
		if (XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(realmId))
		{
			return false;
		}

		float lifespan;
		try
		{
			lifespan = actor.stats == null ? 0f : Mathf.Max(0f, actor.stats["lifespan"]);
		}
		catch
		{
			return false;
		}

		float age = Mathf.Max(0f, actor.getAge());
		return lifespan > 0f && lifespan - age <= Mathf.Max(1f, lifespan * 0.1f);
	}
}
