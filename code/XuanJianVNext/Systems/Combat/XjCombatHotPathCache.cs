using System.Collections.Concurrent;
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
	// 金丹级统一品秩与仙国借玄位格在缓存刷新时物化。战斗热路径只读数值，
	// 不再重复解析释修快照、神丹状态或仙国档案。
	internal readonly int HighRealmGrade;
	internal readonly int BorrowedCombatTier;
	internal readonly float BorrowedOutgoingDamageMultiplier;
	internal readonly float BorrowedIncomingDamageMultiplier;

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
		bool hasImmortalityProtection,
		int highRealmGrade,
		int borrowedCombatTier,
		float borrowedOutgoingDamageMultiplier,
		float borrowedIncomingDamageMultiplier)
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
		HighRealmGrade = highRealmGrade;
		BorrowedCombatTier = borrowedCombatTier;
		BorrowedOutgoingDamageMultiplier = borrowedOutgoingDamageMultiplier > 0f ? borrowedOutgoingDamageMultiplier : 1f;
		BorrowedIncomingDamageMultiplier = borrowedIncomingDamageMultiplier > 0f ? borrowedIncomingDamageMultiplier : 1f;
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
/// Materializes values used by Actor.getHit when stats change.
/// Actor.updateStats is executed by WorldBox inside BatchActors.updateJobsParallel,
/// therefore every collection touched from this class must be safe for concurrent
/// worker access. The hot path performs only O(1) concurrent lookups/writes.
/// </summary>
internal static class XjCombatHotPathCache
{
	private static readonly ConcurrentDictionary<long, XjCombatHotProfile> Profiles = new();

	internal static XjCombatHotProfile Get(Actor actor)
	{
		if (!TryGetActorId(actor, out long actorId))
		{
			return default;
		}

		if (Profiles.TryGetValue(actorId, out XjCombatHotProfile cached))
		{
			return cached;
		}

		XjCombatHotProfile built = Build(actor);
		XjCombatHotProfile profile = Profiles.GetOrAdd(actorId, built);
		XjCombatRegenerationSystem.Observe(actor, profile.HealbackPerSecond);
		return profile;
	}

	internal static bool Contains(long actorId)
	{
		return actorId > 0L && Profiles.ContainsKey(actorId);
	}

	internal static void Refresh(Actor actor)
	{
		if (!TryGetActorId(actor, out long actorId)) return;
		XjCombatHotProfile profile = Build(actor);
		Profiles[actorId] = profile;
		// Observe only publishes a coalesced thread-safe request. All Queue/HashSet
		// mutation for regeneration is drained later on the main scheduler lane.
		XjCombatRegenerationSystem.Observe(actor, profile.HealbackPerSecond);
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L) return;
		Profiles.TryRemove(actorId, out _);
		XjCombatRegenerationSystem.Forget(actorId);
	}

	internal static void Clear()
	{
		Profiles.Clear();
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

		// 统一高境品秩只在缓存刷新时做一次权威解析。真正命中时严禁再 BuildState/
		// TryBuildSnapshot/反射国家数据。仙国借玄倍率也在此一并物化。
		int highRealmGrade = 0;
		int borrowedCombatTier = 0;
		int effectiveCombatTier = XjHighRealmCombatGrade.ResolveEffectiveCombatTier(actor, realmTier);
		if (effectiveCombatTier > realmTier) borrowedCombatTier = effectiveCombatTier;
		float borrowedOutgoingDamageMultiplier = 1f;
		float borrowedIncomingDamageMultiplier = 1f;
		if (XjHighRealmCombatGrade.TryResolveGrade(actor, out int resolvedGrade, out XjHighRealmCombatGrade.Kind highRealmKind))
		{
			highRealmGrade = resolvedGrade;
			if (highRealmKind == XjHighRealmCombatGrade.Kind.XianGuoFalseJinDan)
			{
				borrowedCombatTier = XjRealmSuppression.TierJinDan;
				float progress = System.Math.Clamp(
					(resolvedGrade - XjHighRealmCombatGrade.XianGuoMinimum)
					/ (float)System.Math.Max(1, XjHighRealmCombatGrade.XianGuoMaximum - XjHighRealmCombatGrade.XianGuoMinimum),
					0f, 1f);
				borrowedOutgoingDamageMultiplier = 1.55f + progress * 0.28f;
				borrowedIncomingDamageMultiplier = 0.68f - progress * 0.08f;
			}
		}

		float healbackPerSecond = XjSafeCore.GetStatSafe(actor, XjSafeCore.Healback);
		// 渊照由太阴之“照/藏”与坎水之“渊/涵”空证而来。金丹后常态保留一份
		// 坎水式伤势回流：金丹每秒额外恢复1%最大生命，道胎提高到1.5%。
		// CombatRegenerationSystem 自身仍有2%/秒硬上限，因此装备/其他来源不会无限叠加。
		if (string.Equals((daoTu ?? string.Empty).Trim(), "渊照", System.StringComparison.Ordinal)
			&& effectiveCombatTier >= XjRealmSuppression.TierJinDan)
		{
			healbackPerSecond += effectiveCombatTier >= XjRealmSuppression.TierDaoTai ? 1.5f : 1.0f;
		}

		return new XjCombatHotProfile(
			daoTu,
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Accuracy),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Dodge),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.Vampire),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.DamageReduce) + familyFormationDamageReduce,
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ArmorPenPercent),
			healbackPerSecond,
			XjSafeCore.GetStatSafe(actor, XjSafeCore.CritChance),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.CritTakenReduction),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ShieldRatio),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.ShieldBreak),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.SameRealmDamage),
			XjSafeCore.GetStatSafe(actor, XjSafeCore.TrueDamageRatio),
			XjTrueDamageSystem.IsJinXingYaoXie(actor),
			XjVanillaDeathGuard.HasImmortalityProtection(actor, realmTier),
			highRealmGrade,
			borrowedCombatTier,
			borrowedOutgoingDamageMultiplier,
			borrowedIncomingDamageMultiplier);
	}

	private static bool TryGetActorId(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (actor?.data == null) return false;
		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}
}
