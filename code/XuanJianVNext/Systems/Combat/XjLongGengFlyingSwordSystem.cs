using System;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 长庚不再走真君主动法术：剑道本身即是出手方式。
/// 普攻仍由 WorldBox 原生攻击链负责命中、伤害、暴击与境界压制，本系统只把
/// 攻击距离与表现改造成飞剑，避免再复制一套伤害状态机。
/// </summary>
internal static class XjLongGengFlyingSwordSystem
{
    internal const string ProjectileId = "xj_longgeng_flying_sword_visual";
    private const string ProjectileTexture = "proj_JueyingSword_normar";
    private const string CriticalVisualPath = "effects/Skills/Shared/LongGengSwordFeather";

    internal static void RegisterProjectileAsset()
    {
        try
        {
            if (AssetManager.projectiles == null || AssetManager.projectiles.dict.ContainsKey(ProjectileId)) return;
            AssetManager.projectiles.add(new ProjectileAsset
            {
                id = ProjectileId,
                speed = 72f,
                texture = ProjectileTexture,
                sound_launch = "event:/SFX/HIT/HitSwordSword",
                sound_impact = "event:/SFX/HIT/HitSwordSword",
                can_be_left_on_ground = false,
                can_be_blocked = false,
                look_at_target = true,
                can_be_collided = false,
                scale_start = 0.031f,
                scale_target = 0.031f,
                world_actions = (_, __, ___) => true
            });
        }
        catch (Exception ex)
        {
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjLongGengFlyingSwordSystem.RegisterProjectileAsset", ex);
        }
    }

    internal static bool IsLongGengCombatant(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive()) return false;
        if (!XjFuQiSwordWorldState.IsCombatDoctrineEstablished) return false;
        return XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
            && string.Equals((daoTu ?? string.Empty).Trim(), "长庚", StringComparison.Ordinal);
    }

    internal static float ResolveAttackRange(Actor actor)
    {
        if (!IsLongGengCombatant(actor)) return 0f;
        int tier = XjRealmSuppression.GetRealmTier(actor);
        if (tier >= XjRealmSuppression.TierDaoTai) return 20f;
        if (tier >= XjRealmSuppression.TierJinDan) return 16f;
        if (tier >= XjRealmSuppression.TierZiFu) return 12f;
        return 8f;
    }

    internal static void TrySpawnNormalAttackVisual(Actor actor, BaseSimObject target)
    {
        if (!IsLongGengCombatant(actor)) return;
        TrySpawnSwordProjectileVisual(actor, target);
    }

    internal static void TrySpawnSwordProjectileVisual(Actor actor, BaseSimObject target)
    {
        if (actor?.data == null || !actor.isAlive() || target == null || !target.isAlive() || World.world?.projectiles == null) return;
        RegisterProjectileAsset();
        if (!XjProjectileGuard.IsValidProjectileAssetId(ProjectileId)) return;
        try
        {
            Vector2 from = actor.current_position;
            Vector2 to = target.current_position;
            World.world.projectiles.spawn(
                actor,
                target,
                ProjectileId,
                new Vector3(from.x, from.y + 0.25f, 0f),
                new Vector3(to.x, to.y + 0.25f, 0f),
                target.getHeight());
        }
        catch (Exception ex)
        {
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjLongGengFlyingSwordSystem.TrySpawnSwordProjectileVisual", ex);
        }
    }

    internal static void TryPlayCriticalSwordFeather(Actor attacker, Actor defender)
    {
        if (!IsLongGengCombatant(attacker) || defender?.data == null || !defender.isAlive()) return;
        // 剑羽只作为真实暴击的强化反馈；高压时不播放这层纯视觉，不影响伤害。
        if (XjCombatEffectPool.IsHighLoad()) return;
        try
        {
            Vector2 pos = defender.current_position;
            XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
                CriticalVisualPath,
                pos.x,
                pos.y + 0.45f,
                0.075f,
                0.055f);
        }
        catch (Exception ex)
        {
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjLongGengFlyingSwordSystem.TryPlayCriticalSwordFeather", ex);
        }
    }
}
