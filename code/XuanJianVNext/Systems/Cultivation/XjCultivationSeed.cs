using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjCultivationSeed
{
	internal static void EnsureSeedState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (!XjCultivationEligibility.CanCultivate(actor))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		XjReincarnation.TryApplyToActor(actor);

		float existingMingShu = 0f;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out existingMingShu);
		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float mingShuCongenital) || mingShuCongenital <= 0f)
		{
			float seed = existingMingShu > 0f ? ToIntegerValue(existingMingShu) : XjDeterministicHash.BuildSeedInteger(actorId, actor.getName(), 0, 12, 72);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuCongenital, seed);
			mingShuCongenital = seed;
		}
		else
		{
			mingShuCongenital = ToIntegerValue(mingShuCongenital);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuCongenital, mingShuCongenital);
		}

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float mingShuAcquired))
		{
			mingShuAcquired = 0f;
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, mingShuAcquired);
		}
		else
		{
			mingShuAcquired = ToIntegerValue(mingShuAcquired);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, mingShuAcquired);
		}

		XjMingShuState.Set(actor, mingShuCongenital, mingShuAcquired);

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang) || huiGuang <= 0f)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, XjDeterministicHash.BuildSeedInteger(actorId, actor.getName(), 1, 6, 60));
		}
		else
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, ToIntegerValue(huiGuang));
		}

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan) || zhenYuan < 0f)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, 0f);
		}
		else
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, ToIntegerValue(zhenYuan));
		}

		TryApplyBloodlineSeedInheritance(actor);
		XjLongShuSystem.EnsureSeedFloors(actor);
		EnsureChuShen(actor, actorId);
	}

	internal static bool TryApplyBloodlineSeedInheritance(Actor actor)
	{
		if (actor?.data == null
			|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineSeedInheritanceApplied, out int applied) && applied > 0))
		{
			return false;
		}

		float inheritanceBias = XjBloodlineBirthRules.GetEarlyLifeInheritanceBias(actor);
		XjBloodlineBirthRules.GetDirectParentRealmSeedFloors(actor, out float congenitalMingShuFloor, out float huiGuangFloor);
		bool hasConfirmedBloodline = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineApplied, out int bloodlineApplied)
			&& bloodlineApplied > 0;
		if (!hasConfirmedBloodline && congenitalMingShuFloor <= 0f && huiGuangFloor <= 0f)
		{
			return false;
		}

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenitalMingShu)
			|| congenitalMingShu <= 0f
			|| !XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang)
			|| huiGuang <= 0f)
		{
			return false;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquiredMingShu);
		acquiredMingShu = ToIntegerValue(acquiredMingShu);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, acquiredMingShu);

		float inheritedCongenitalMingShu = Math.Max(
			ToIntegerValue(congenitalMingShu),
			ToIntegerValue(Math.Min(100f, Math.Max(
				Lerp(Math.Max(1f, congenitalMingShu), 100f, inheritanceBias),
				congenitalMingShuFloor))));
		float inheritedHuiGuang = Math.Max(
			ToIntegerValue(huiGuang),
			ToIntegerValue(Math.Min(120f, Math.Max(
				Lerp(Math.Max(1f, huiGuang), 120f, inheritanceBias),
				huiGuangFloor))));

		XjMingShuState.Set(actor, inheritedCongenitalMingShu, acquiredMingShu);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, inheritedHuiGuang);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineSeedInheritanceApplied, 1);
		return true;
	}

	internal static void RefreshChuShenForCultivationState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (XjLongShuSystem.IsLongShu(actor))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, 0);
			XjVisibleTraitSync.SyncChuShenTrait(actor, string.Empty);
			XjVisibleTraitSync.SyncChuShenSpecialTrait(actor, string.Empty);
			return;
		}

		if (IsChuShenManuallyRemoved(actor, special: false))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, 0);
			XjVisibleTraitSync.SyncChuShenTrait(actor, string.Empty);
		}
		else if (TryGetManualChuShenOverride(actor, special: false, out int manualRank))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, manualRank);
			XjVisibleTraitSync.SyncChuShenTrait(actor, BuildChuShenTraitId(manualRank));
		}
		else
		{
			int resolved = ResolveChuShenRank(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, resolved);
			XjVisibleTraitSync.SyncChuShenTrait(actor, BuildChuShenTraitId(resolved));
		}

		if (IsChuShenManuallyRemoved(actor, special: true))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, 0);
			XjVisibleTraitSync.SyncChuShenSpecialTrait(actor, string.Empty);
		}
		else if (TryGetManualChuShenOverride(actor, special: true, out int specialRank))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, specialRank);
			XjVisibleTraitSync.SyncChuShenSpecialTrait(actor, BuildChuShenTraitId(specialRank));
		}
		else
		{
			int existingSpecial = ResolveExistingSpecialChuShenRank(actor);
			if (existingSpecial <= 0)
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShenSpecial, out existingSpecial);
			}
			existingSpecial = existingSpecial >= 6 && existingSpecial <= 8 ? existingSpecial : 0;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, existingSpecial);
			XjVisibleTraitSync.SyncChuShenSpecialTrait(actor, existingSpecial > 0 ? BuildChuShenTraitId(existingSpecial) : string.Empty);
		}
	}

	private static void EnsureChuShen(Actor actor, long actorId)
	{
		_ = actorId;
		RefreshChuShenForCultivationState(actor);
	}

	private static bool IsChuShenManuallyRemoved(Actor actor, bool special)
	{
		string key = special ? XjActorDataKeys.ChuShenSpecialManualRemoved : XjActorDataKeys.ChuShenManualRemoved;
		return XjActorAccessor.TryGetInt(actor, key, out int removed) && removed > 0;
	}

	private static bool TryGetManualChuShenOverride(Actor actor, bool special, out int rank)
	{
		rank = 0;
		string key = special ? XjActorDataKeys.ChuShenSpecialManualOverride : XjActorDataKeys.ChuShenManualOverride;
		return XjActorAccessor.TryGetInt(actor, key, out rank)
			&& (special ? rank >= 6 && rank <= 8 : rank >= 1 && rank <= 5);
	}

	private static int ResolveChuShenRank(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		if (!IsBasicRace(actor))
		{
			return 1;
		}

		bool hasCultivation = HasCultivationState(actor);
		if (!hasCultivation)
		{
			return 1;
		}

		bool isZhuJiOrHigher = IsZhuJiOrHigher(actor);
		bool hasZongMen = XjZongMenAccessor.BuildIdentity(actor).Found;
		if (hasZongMen)
		{
			return isZhuJiOrHigher ? 4 : 3;
		}

		return isZhuJiOrHigher ? 5 : 2;
	}

	private static int ResolveExistingSpecialChuShenRank(Actor actor)
	{
		for (int i = 8; i >= 6; i--)
		{
			if (actor.hasTrait(BuildChuShenTraitId(i)))
			{
				return i;
			}
		}

		return 0;
	}

	private static bool HasCultivationState(Actor actor)
	{
		return CanActorCultivateForChuShen(actor);
	}

	private static bool CanActorCultivateForChuShen(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int overlayMask);
		bool hasBlockedOverlay = (overlayMask & (1 << 8)) != 0 || (overlayMask & (1 << 9)) != 0;
		if (hasBlockedOverlay)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz) && xjZz >= 1 && xjZz <= 6)
		{
			return true;
		}


		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& !string.IsNullOrWhiteSpace(realmId);
	}

	private static bool IsZhuJiOrHigher(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		return string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
	}
	private static bool IsBasicRace(Actor actor)
	{
		if (actor?.asset == null)
		{
			return false;
		}

		string id = ((Asset)actor.asset).id ?? string.Empty;
		if (string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		string normalized = id.Trim().ToLowerInvariant();
		return normalized == "human"
			|| normalized == "elf"
			|| normalized == "orc"
			|| normalized == "dwarf"
			|| normalized == "unit_human"
			|| normalized == "unit_elf"
			|| normalized == "unit_orc"
			|| normalized == "unit_dwarf";
	}

	private static string BuildChuShenTraitId(int rank)
	{
		return "ChuShen" + Math.Max(1, Math.Min(8, rank)).ToString(System.Globalization.CultureInfo.InvariantCulture);
	}

	private static float ToIntegerValue(float value)
	{
		return (float)Math.Floor(Math.Max(0f, value));
	}


	private static float Lerp(float from, float to, float amount)
	{
		float clamped = Math.Max(0f, Math.Min(1f, amount));
		return from + (to - from) * clamped;
	}
}
