using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

internal readonly struct XjCombatHotProfile
{
	internal readonly string DaoTu;
	internal readonly float Accuracy;
	internal readonly float Dodge;
	internal readonly float Vampire;
	internal readonly float DamageReduce;
	internal readonly float ArmorPenPercent;
	internal readonly float HealbackPerSecond;
	internal readonly float CritChance;
	internal readonly float CritTakenReduction;
	internal readonly float ShieldRatio;
	internal readonly float ShieldBreak;
	internal readonly float SameRealmDamage;
	internal readonly float TrueDamageRatio;
	internal readonly bool IsJinXingYaoXie;
	internal readonly bool HasImmortalityProtection;

	internal XjCombatHotProfile(
		string daoTu,
		float accuracy,
		float dodge,
		float vampire,
		float damageReduce,
		float armorPenPercent,
		float healbackPerSecond,
		float critChance,
		float critTakenReduction,
		float shieldRatio,
		float shieldBreak,
		float sameRealmDamage,
		float trueDamageRatio,
		bool isJinXingYaoXie,
		bool hasImmortalityProtection)
	{
		DaoTu = (daoTu ?? string.Empty).Trim();
		Accuracy = accuracy;
		Dodge = dodge;
		Vampire = vampire;
		DamageReduce = damageReduce;
		ArmorPenPercent = armorPenPercent;
		HealbackPerSecond = healbackPerSecond;
		CritChance = critChance;
		CritTakenReduction = critTakenReduction;
		ShieldRatio = shieldRatio;
		ShieldBreak = shieldBreak;
		SameRealmDamage = sameRealmDamage;
		TrueDamageRatio = trueDamageRatio;
		IsJinXingYaoXie = isJinXingYaoXie;
		HasImmortalityProtection = hasImmortalityProtection;
	}

	internal bool HasCustomCombatStats => Accuracy > 0f
		|| Dodge > 0f
		|| Vampire > 0f
		|| DamageReduce > 0f
		|| ArmorPenPercent > 0f
		|| HealbackPerSecond > 0f
		|| CritChance > 0f
		|| CritTakenReduction > 0f
		|| ShieldRatio > 0f
		|| ShieldBreak > 0f
		|| SameRealmDamage > 0f
		|| TrueDamageRatio > 0f;
}

/// <summary>
/// Materializes values used by Actor.getHit when stats change. The hot path
/// only reads this cache and never resolves family, sect, equipment or world indexes.
/// </summary>
internal static class XjCombatHotPathCache
{
	private static readonly Dictionary<long, XjCombatHotProfile> Profiles = new Dictionary<long, XjCombatHotProfile>();
	private static readonly object Sync = new object();

	internal static XjCombatHotProfile Get(Actor actor)
	{
		if (!TryGetActorId(actor, out long actorId))
		{
			return default;
		}

		lock (Sync)
		{
			if (Profiles.TryGetValue(actorId, out XjCombatHotProfile cached))
			{
				return cached;
			}
		}

		XjCombatHotProfile profile = Build(actor);
		lock (Sync)
		{
			if (!Profiles.ContainsKey(actorId))
			{
				Profiles[actorId] = profile;
			}
			else
			{
				profile = Profiles[actorId];
			}
		}
		XjCombatRegenerationSystem.Observe(actor, profile.HealbackPerSecond);
		return profile;
	}

	internal static bool Contains(long actorId)
	{
		if (actorId <= 0L)
		{
			return false;
		}
		lock (Sync)
		{
			return Profiles.ContainsKey(actorId);
		}
	}

	internal static void Refresh(Actor actor)
	{
		if (TryGetActorId(actor, out long actorId))
		{
			XjCombatHotProfile profile = Build(actor);
			lock (Sync)
			{
				Profiles[actorId] = profile;
			}
			XjCombatRegenerationSystem.Observe(actor, profile.HealbackPerSecond);
		}
	}

	internal static void Remove(long actorId)
	{
		if (actorId > 0L)
		{
			lock (Sync)
			{
				Profiles.Remove(actorId);
			}
			XjCombatRegenerationSystem.Forget(actorId);
		}
	}

	internal static void Clear()
	{
		lock (Sync)
		{
			Profiles.Clear();
		}
		XjCombatRegenerationSystem.Clear();
	}

	private static XjCombatHotProfile Build(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int realmTier = XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier) ? cachedTier : 0;
		XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetString(
			actor,
			XuanJianVNext.Data.Rules.XjActorDataKeys.DaoTu,
			out string daoTu);
		XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetInt(
			actor,
			XuanJianVNext.Data.Rules.XjActorDataKeys.XjFamilyFormationGuardGrade,
			out int familyFormationGuardGrade);
		float familyFormationDamageReduce = familyFormationGuardGrade >= 2 ? 8f : familyFormationGuardGrade == 1 ? 4f : 0f;
		return new XjCombatHotProfile(
			daoTu,
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Accuracy),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Dodge),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Vampire),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.DamageReduce) + familyFormationDamageReduce,
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ArmorPenPercent),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Healback),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.CritChance),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.CritTakenReduction),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ShieldRatio),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ShieldBreak),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.SameRealmDamage),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.TrueDamageRatio),
			XjTrueDamageSystem.IsJinXingYaoXie(actor),
			XjVanillaDeathGuard.HasImmortalityProtection(actor, realmTier));
	}

	private static bool TryGetActorId(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (actor?.data == null)
		{
			return false;
		}

		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}
}
