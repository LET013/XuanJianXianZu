using System;
using System.Collections;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Visual;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 太阳上位神通“极盛御”。只有紫府金丹法太阳紫府且真实持有该仙基的角色，
/// 在攻击时才会自动显化：身后直立展示日轮三秒，随后获得三十秒伤害增幅，
/// 六十秒内不能再次显化。视觉资源缺失时只跳过展示，不中断战斗或增益。
/// </summary>
internal static class XjJiShengYuSystem
{
    private const string TaiYangDaoTu = "太阳";
    private const string JiShengYuXianJi = "极盛御";
    private const string SpritePath = "effects/ShenTong/TaiYang/JiShengYu";
    private const float DamageMultiplier = 1.5f;
    private const float BuffDurationSeconds = 30f;
    private const float CooldownSeconds = 60f;
    private const float VisualDurationSeconds = 3f;
    // 洞天建筑使用 2/27；此处按用户口径取其四分之一。
    private const float VisualScale = 1f / 54f;
    private const float VisualYOffset = 0.35f;
    private const int RelativeSortingOrder = -2;

    private static Sprite _sprite;
    private static bool _spriteLoadAttempted;

    internal static float GetOrActivateDamageMultiplier(Actor actor, int attackerTier)
    {
        if (actor?.data == null)
        {
            return 1f;
        }

        if (attackerTier != XjRealmSuppression.TierZiFu
            || !XjCultivationPathRules.IsZiFuJinDan(actor)
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
            || !string.Equals((daoTu ?? string.Empty).Trim(), TaiYangDaoTu, StringComparison.Ordinal))
        {
            return 1f;
        }

        float now = Time.unscaledTime;
        float buffUntil = ReadRuntimeDeadline(actor, XjActorDataKeys.XjJiShengYuBuffUntilTime, now, BuffDurationSeconds);
        if (buffUntil > now)
        {
            return DamageMultiplier;
        }

        float cooldownUntil = ReadRuntimeDeadline(actor, XjActorDataKeys.XjJiShengYuCooldownUntilTime, now, CooldownSeconds);
        if (cooldownUntil > now)
        {
            return 1f;
        }

        XjXianJiState state = XjXianJiAccessor.BuildState(actor);
        if (!HasXianJi(in state, JiShengYuXianJi))
        {
            return 1f;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        float combatNow = Time.time;
        if (!XjJinDanDaoSpellCooldown.IsReady(actorId, "Special:JiShengYu", combatNow))
        {
            return 1f;
        }

        XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjJiShengYuBuffUntilTime, now + BuffDurationSeconds);
        XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjJiShengYuCooldownUntilTime, now + CooldownSeconds);
        XjJinDanDaoSpellCooldown.MarkUsed(actorId, "Special:JiShengYu", combatNow, CooldownSeconds);
        TryShowVisual(actor);
        return DamageMultiplier;
    }

    internal static void Clear()
    {
        _sprite = null;
        _spriteLoadAttempted = false;
    }

    private static float ReadRuntimeDeadline(Actor actor, string key, float now, float maximumDuration)
    {
        if (!XjActorAccessor.TryGetFloat(actor, key, out float deadline)
            || float.IsNaN(deadline)
            || float.IsInfinity(deadline)
            || deadline <= 0f)
        {
            return 0f;
        }

        // Time.unscaledTime 在重新启动游戏后会归零。旧进程留下的绝对时刻不能
        // 让角色在新会话中被锁数小时；超过本效果理论窗口即视为旧会话残值。
        if (deadline - now > maximumDuration + 1f)
        {
            XjActorAccessor.SetFloat(actor, key, 0f);
            return 0f;
        }
        return deadline;
    }

    private static bool HasXianJi(in XjXianJiState state, string xianJi)
    {
        if (!state.Found || state.Ids == null || string.IsNullOrWhiteSpace(xianJi))
        {
            return false;
        }

        for (int i = 0; i < state.Ids.Length; i++)
        {
            if (string.Equals(state.Ids[i], xianJi, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void TryShowVisual(Actor actor)
    {
        MapBox world = World.world;
        if (actor?.data == null || (UnityEngine.Object)(object)world == (UnityEngine.Object)null)
        {
            return;
        }

        Sprite sprite = LoadSprite();
        if ((UnityEngine.Object)(object)sprite == (UnityEngine.Object)null)
        {
            return;
        }

        try
        {
            ((MonoBehaviour)world).StartCoroutine(RunVisual(actor, sprite));
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjJiShengYuSystem.TryShowVisual", ex);
        }
    }

    private static IEnumerator RunVisual(Actor actor, Sprite sprite)
    {
        Vector3 position = ResolvePosition(actor);
        if (!XjWorldSpriteRenderLayer.TryCreateActorBackSprite(
                new Vector2(position.x, position.y + VisualYOffset),
                VisualScale,
                null,
                RelativeSortingOrder,
                "xj_jishengyu",
                out GameObject root,
                out SpriteRenderer renderer))
        {
            yield break;
        }

        renderer.sprite = sprite;
        renderer.color = Color.white;
        renderer.flipX = false;
        renderer.flipY = false;
        root.transform.rotation = Quaternion.identity;

        float elapsed = 0f;
        while (elapsed < VisualDurationSeconds && actor?.data != null && XjSafeCore.IsAliveActor(actor))
        {
            elapsed += Mathf.Max(0f, Time.unscaledDeltaTime);
            position = ResolvePosition(actor);
            root.transform.position = new Vector3(position.x, position.y + VisualYOffset, 0f);
            root.transform.rotation = Quaternion.identity;
            yield return null;
        }

        if (root != null)
        {
            UnityEngine.Object.Destroy(root);
        }
    }

    private static Vector3 ResolvePosition(Actor actor)
    {
        try
        {
            Vector3 current = actor.current_position;
            if (!float.IsNaN(current.x) && !float.IsNaN(current.y))
            {
                return current;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjJiShengYuSystem.ResolvePosition.Current", ex);
        }

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            if (tile != null)
            {
                return tile.posV3;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjJiShengYuSystem.ResolvePosition.Tile", ex);
        }
        return Vector3.zero;
    }

    private static Sprite LoadSprite()
    {
        if ((UnityEngine.Object)(object)_sprite != (UnityEngine.Object)null)
        {
            return _sprite;
        }
        if (_spriteLoadAttempted)
        {
            return null;
        }
        _spriteLoadAttempted = true;

        try
        {
            _sprite = SpriteTextureLoader.getSprite(SpritePath)
                ?? SpriteTextureLoader.getSprite("GameResources/" + SpritePath)
                ?? SpriteTextureLoader.getSprite(SpritePath + ".png")
                ?? SpriteTextureLoader.getSprite("GameResources/" + SpritePath + ".png");
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjJiShengYuSystem.LoadSprite", ex);
            _sprite = null;
        }
        return _sprite;
    }
}
