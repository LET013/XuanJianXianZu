using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 金丹/道胎主动法术的“高境余威”。
///
/// 0.21 的金丹压迫感主要并不来自 trait.range，而来自一次攻击会在数十格范围内
/// 同时波及大量角色、按最大生命造成高比例伤害，并伴随大范围环境异象。
/// 0.9.8.28 保留现行原著具名神通的主目标语义，只把这种“高境出手，低境难以立足”
/// 抽成统一余威层：不把单体神通改成群攻，不伤同境金丹，却会横扫附近低境。
///
/// 性能边界：只使用 XjLocalActorQuery 的有界区块查询；一次最多处理 112/160 个目标，
/// 不遍历 World.world.units，不逐帧常驻扫描，也不绕过原生 getHit/死亡归因链。
/// </summary>
internal static class XjHighRealmPressureWaveSystem
{
	private const int JinDanEarlyRadius = 60;
	private const int JinDanMiddleRadius = 72;
	private const int JinDanLateRadius = 84;
	private const int JinDanPeakRadius = 96;
	private const int ZiJinDaoTaiPressureRadius = 140;
	private const int FuQiDaoTaiPressureRadius = 132;

	private const int JinDanEarlyTargetCap = 64;
	private const int JinDanMiddleTargetCap = 80;
	private const int JinDanLateTargetCap = 96;
	private const int JinDanPeakTargetCap = 112;
	private const int DaoTaiTargetCap = 160;

	internal static int ResolveLocalCandidateCap(Actor caster)
	{
		if (XjDaoTaiSpellScale.IsDaoTaiActor(caster)) return 192;
		int tier = XjRealmSuppression.GetRealmTier(caster);
		return tier >= XjRealmSuppression.TierJinDan ? 128 : 64;
	}

	internal static void TryApplyAfterSpell(
		Actor caster,
		in XjJinDanDaoSpellDefinition definition,
		in XjJinDanDaoSpellTargetContext context)
	{
		if (!XjSafeCore.IsAliveActor(caster) || context.CenterTile == null) return;
		int casterTier = XjRealmSuppression.GetRealmTier(caster);
		if (casterTier < XjRealmSuppression.TierJinDan) return;
		if (string.Equals(definition.TargetMode, "Self", StringComparison.Ordinal)) return;

		// 纯治疗/纯自益定义不凭空生成杀伤余威。只要本次属于攻击、控制或地形法术，
		// 即使主神通本身是单体，也允许低境被高位法力波及。
		bool offensive = definition.DamageMultiplier > 0f
			|| definition.StatusDurationSeconds > 0f
			|| definition.FloatDurationSeconds > 0f
			|| definition.SmashDurationSeconds > 0f
			|| definition.TerrainRadius > 0
			|| !string.IsNullOrWhiteSpace(definition.TerrainEffect);
		if (!offensive) return;

		int radius = ResolvePressureRadius(caster);
		int targetCap = ResolvePressureTargetCap(caster);
		if (radius <= 0 || targetCap <= 0) return;

		IReadOnlyList<Actor> local = XjJinDanDaoSpellTargeting.CollectLocalImpactTargets(
			caster,
			null,
			context.CenterTile,
			radius);
		if (local == null || local.Count == 0)
		{
			XjJinDanCombatApi.TryPlayHighRealmPressureVisuals(context.CenterTile, radius, casterTier >= XjRealmSuppression.TierDaoTai);
			return;
		}

		HashSet<long> primaryIds = BuildPrimaryTargetIds(context.Targets);
		List<Actor> affected = new List<Actor>(Math.Min(targetCap, local.Count));
		int combatLevel = XjRealmSuppression.GetCombatLevel(caster);
		float baseDamage = Math.Max(1f, XjJinDanCombatApi.GetBaseDamage(caster));

		for (int i = 0; i < local.Count && affected.Count < targetCap; i++)
		{
			Actor target = local[i];
			if (!XjSafeCore.IsAliveActor(target)) continue;
			long targetId = GetActorId(target);
			if (targetId > 0L && primaryIds.Contains(targetId)) continue;

			int targetTier = XjRealmSuppression.GetRealmTier(target);
			// 余威只用于表现大境界压迫。金丹同级、道胎同级与更高位者必须由神通本身决胜，
			// 不能被一个统一后台波纹抹掉各道途/果位之间的差异。
			if (targetTier >= casterTier) continue;

			float damage = ResolvePressureDamage(casterTier, combatLevel, targetTier, target, baseDamage);
			if (damage <= 0f) continue;

			if (!XjJinDanCombatApi.TryDamageActor(caster, target, damage, "高境余威", out _)) continue;
			affected.Add(target);

			// 活过余威者仍会被法力冲击短暂压住。这里只走既有高境控制服务，
			// 不另建常驻状态，也不强制位移造成寻路/落水等原生兼容问题。
			if (XjSafeCore.IsAliveActor(target))
			{
				float hold = casterTier >= XjRealmSuppression.TierDaoTai ? 0.75f : 0.35f;
				XjJinDanCombatApi.TryApplyImpactHold(caster, target, hold, "高境余威", out _);
			}
		}

		XjJinDanCombatApi.TryPlayHighRealmPressureVisuals(
			context.CenterTile,
			radius,
			casterTier >= XjRealmSuppression.TierDaoTai);
	}

	private static int ResolvePressureRadius(Actor caster)
	{
		if (XjDaoTaiSpellScale.IsDaoTaiActor(caster))
		{
			return IsFuQiDaoTai(caster) ? FuQiDaoTaiPressureRadius : ZiJinDaoTaiPressureRadius;
		}

		int combatLevel = XjRealmSuppression.GetCombatLevel(caster);
		if (combatLevel >= 27) return JinDanPeakRadius;
		if (combatLevel >= 26) return JinDanLateRadius;
		if (combatLevel >= 25) return JinDanMiddleRadius;
		return JinDanEarlyRadius;
	}

	private static int ResolvePressureTargetCap(Actor caster)
	{
		if (XjDaoTaiSpellScale.IsDaoTaiActor(caster)) return DaoTaiTargetCap;
		int combatLevel = XjRealmSuppression.GetCombatLevel(caster);
		if (combatLevel >= 27) return JinDanPeakTargetCap;
		if (combatLevel >= 26) return JinDanLateTargetCap;
		if (combatLevel >= 25) return JinDanMiddleTargetCap;
		return JinDanEarlyTargetCap;
	}

	private static float ResolvePressureDamage(
		int casterTier,
		int combatLevel,
		int targetTier,
		Actor target,
		float baseDamage)
	{
		float maxHealth = Math.Max(1f, XjSafeCore.GetMaxHealthSafe(target, 1f));
		float currentHealth = Math.Max(1f, XjSafeCore.GetHealthSafe(target, maxHealth));

		if (casterTier >= XjRealmSuppression.TierDaoTai)
		{
			if (targetTier >= XjRealmSuppression.TierJinDan)
			{
				// 道胎对金丹：余威本身已逼近半条命；统一伤害结算器仍会套用道胎
				// 60%生命下限，保持“真正命中两次内决出生死”的现有位格规则。
				return Math.Max(baseDamage * 0.20f, maxHealth * 0.45f);
			}

			// 道胎对紫府及以下，既有位格压制要求一次有效命中具有终结性。
			return Math.Max(baseDamage * 0.15f, currentHealth + maxHealth * 0.02f);
		}

		if (targetTier >= XjRealmSuppression.TierZiFu)
		{
			float ratio = combatLevel >= 27 ? 0.55f
				: combatLevel >= 26 ? 0.50f
				: combatLevel >= 25 ? 0.45f
				: 0.40f;
			return Math.Max(baseDamage * 0.15f, maxHealth * ratio);
		}

		// 金丹真正出手时，筑基及以下不应还能站在战场中心承受数轮余波。
		// 仍然只提高本次 getHit 伤害，不直接调用 die/kill，保证死亡史册、收藏、
		// 家族/宗门清理与击杀归因全部沿现行统一链路触发。
		return Math.Max(baseDamage * 0.12f, currentHealth + maxHealth * 0.01f);
	}

	private static HashSet<long> BuildPrimaryTargetIds(IReadOnlyList<Actor> targets)
	{
		HashSet<long> result = new HashSet<long>();
		if (targets == null) return result;
		for (int i = 0; i < targets.Count; i++)
		{
			long actorId = GetActorId(targets[i]);
			if (actorId > 0L) result.Add(actorId);
		}
		return result;
	}

	private static bool IsFuQiDaoTai(Actor actor)
	{
		if (actor?.data == null) return false;
		try
		{
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
			{
				return string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
			}
			return actor.hasTrait(XjRealmIds.FuQiDaoTai);
		}
		catch
		{
			return false;
		}
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch { return 0L; }
	}
}
