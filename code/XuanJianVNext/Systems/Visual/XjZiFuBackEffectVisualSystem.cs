using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// 紫府常驻身后雾气。使用独立 SpriteRenderer 对象，只处理当前可见角色；
/// 不再占用或污染原生 fx_slash 对象池。
/// </summary>
internal static class XjZiFuBackEffectVisualSystem
{
    private const string AnimationId = "effects/Halo/ZiFu";
    private const float YOffset = 1.50f;
    private const float Scale = 0.025f;
    private const float BackZOffset = 0.15f;
    private const int FrameStep = 2;
    private const float FrameIntervalSeconds = 0.06f;
    private const int VisibleScanCadenceFrames = 10;
    private const int CleanupIntervalFrames = 45;
    private const int StaleFrameThreshold = 24;

    private static readonly Dictionary<long, EffectEntry> EntriesByActorId = new Dictionary<long, EffectEntry>(64);
    private static readonly List<long> CleanupBuffer = new List<long>(64);
    private static int _lastCleanupFrame = -1;
    private static int _lastVisibleScanFrame = -1;
    private static Sprite[] _frames;

    /// <summary>
    /// 准备统一可见角色渲染帧。返回 true 表示本帧需要重新发现可见紫府。
    /// </summary>
    internal static bool BeginRenderFrame(int frame)
    {
        if (!XjCultivatorCache.HasZiFuOrHigher)
        {
            if (EntriesByActorId.Count > 0) Cleanup(frame, true);
            return false;
        }

        Sprite[] frames = _frames;
        if (frames == null || frames.Length == 0)
        {
            frames = XjCombatEffectPool.GetCachedAnimationFrames(AnimationId);
            if (frames == null || frames.Length == 0)
            {
                Cleanup(frame, true);
                return false;
            }
            _frames = frames;
        }

        bool shouldScan = _lastVisibleScanFrame < 0
            || frame - _lastVisibleScanFrame >= VisibleScanCadenceFrames;
        if (shouldScan) _lastVisibleScanFrame = frame;
        return shouldScan;
    }

    internal static void ObserveVisibleActor(Actor actor, ActorManager manager, Vector3 position, int frame)
    {
        Sprite[] frames = _frames;
        if (frames == null || frames.Length == 0
            || !IsEligible(actor, out long actorId))
        {
            return;
        }

        EffectEntry entry = GetOrCreateEntry(actor, actorId, manager, position, frames);
        if (entry == null) return;
        entry.Actor = actor;
        entry.LastRenderPosition = position;
        entry.LastSeenFrame = frame;
    }

    internal static void EndRenderFrame(int frame, int lodLevel)
    {
        Sprite[] frames = _frames;
        if (frames != null && frames.Length > 0)
        {
            UpdateExistingEntries(frame, frames, lodLevel);
        }

        if (_lastCleanupFrame < 0 || frame - _lastCleanupFrame >= CleanupIntervalFrames)
        {
            Cleanup(frame, false);
        }
    }

    internal static void Clear()
    {
        foreach (EffectEntry entry in EntriesByActorId.Values) DestroyEntry(entry);
        EntriesByActorId.Clear();
        CleanupBuffer.Clear();
        _lastCleanupFrame = -1;
        _lastVisibleScanFrame = -1;
        _frames = null;
        XjWorldSpriteRenderLayer.ResetActorLayerCache();
    }

    private static void UpdateExistingEntries(int frame, Sprite[] frames, int lodLevel)
    {
        foreach (EffectEntry entry in EntriesByActorId.Values)
        {
            if (entry == null || entry.Transform == null || entry.Renderer == null || entry.Actor?.data == null)
            {
                continue;
            }

            if (frame - entry.LastSeenFrame > VisibleScanCadenceFrames + 2)
            {
                if (entry.GameObject != null) entry.GameObject.SetActive(false);
                continue;
            }

            long actorId = 0L;
            try { actorId = ((BaseSystemData)entry.Actor.data).id; } catch { }

            // 远景只需隔帧同步位置，近景仍保持逐帧跟随。
            bool updatePosition = lodLevel < 2 || ((frame + (int)(actorId & 1L)) & 1) == 0;
            Vector3 position = entry.LastRenderPosition;
            if (updatePosition)
            {
                TryGetActorPosition(entry.Actor, ref position);
                entry.LastRenderPosition = position;
                entry.Transform.position = new Vector3(position.x, position.y + YOffset, position.z + BackZOffset);
            }

            int spriteIndex;
            if (lodLevel >= 2)
            {
                // 远景固定一帧，避免大量紫府同时反复写 Sprite。
                spriteIndex = Math.Abs((int)(actorId % frames.Length));
            }
            else
            {
                int animationStride = lodLevel == 1 ? 3 : 1;
                spriteIndex = Math.Abs((frame / (FrameStep * animationStride)) + (int)(actorId % frames.Length)) % frames.Length;
            }
            Sprite sprite = frames[spriteIndex];
            if (!ReferenceEquals(entry.Renderer.sprite, sprite)) entry.Renderer.sprite = sprite;
            entry.Renderer.color = Color.white;
            entry.Transform.localScale = new Vector3(Scale, Scale, 1f);
            if (entry.GameObject != null) entry.GameObject.SetActive(true);
        }
    }

    private static void TryGetActorPosition(Actor actor, ref Vector3 position)
    {
        try
        {
            position = actor.current_position;
            return;
        }
        catch { }

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            if (tile != null) position = tile.posV3;
        }
        catch { }
    }

    private static bool IsEligible(Actor actor, out long actorId)
    {
        actorId = 0L;
        if (actor?.data == null || !actor.isAlive() || XjLongShuSystem.IsLongShu(actor)) return false;

        try { actorId = ((BaseSystemData)actor.data).id; }
        catch { return false; }
        if (actorId <= 0L) return false;

        if (XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier))
        {
            return realmTier == XjRealmSuppression.TierZiFu;
        }

        return actor.hasTrait(XjRealmIds.ZiFu)
            || (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
                && string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal));
    }

    private static EffectEntry GetOrCreateEntry(Actor actor, long actorId, ActorManager manager, Vector3 position, Sprite[] frames)
    {
        if (EntriesByActorId.TryGetValue(actorId, out EffectEntry entry)
            && entry != null
            && entry.Transform != null
            && entry.Renderer != null)
        {
            return entry;
        }

        try
        {
            if (!XjWorldSpriteRenderLayer.TryCreateActorBackSprite(
                    new Vector2(position.x, position.y + YOffset),
                    Scale,
                    manager,
                    -1,
                    "xj_zifu_back_effect_" + actorId,
                    out GameObject gameObject,
                    out SpriteRenderer renderer))
            {
                return null;
            }

            renderer.color = Color.white;
            entry = new EffectEntry(actor, gameObject, gameObject.transform, renderer, position);
            EntriesByActorId[actorId] = entry;
            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static void Cleanup(int frame, bool force)
    {
        CleanupBuffer.Clear();
        foreach (KeyValuePair<long, EffectEntry> pair in EntriesByActorId)
        {
            EffectEntry entry = pair.Value;
            if (force || entry == null || entry.Transform == null || frame - entry.LastSeenFrame > StaleFrameThreshold)
            {
                CleanupBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < CleanupBuffer.Count; i++)
        {
            long actorId = CleanupBuffer[i];
            if (EntriesByActorId.TryGetValue(actorId, out EffectEntry entry)) DestroyEntry(entry);
            EntriesByActorId.Remove(actorId);
        }
        CleanupBuffer.Clear();
        _lastCleanupFrame = frame;
    }

    private static void DestroyEntry(EffectEntry entry)
    {
        try
        {
            if (entry?.GameObject != null) UnityEngine.Object.Destroy(entry.GameObject);
        }
        catch { }
    }

    private sealed class EffectEntry
    {
        internal EffectEntry(Actor actor, GameObject gameObject, Transform transform, SpriteRenderer renderer, Vector3 position)
        {
            Actor = actor;
            GameObject = gameObject;
            Transform = transform;
            Renderer = renderer;
            LastRenderPosition = position;
            LastSeenFrame = Time.frameCount;
        }

        internal Actor Actor { get; set; }
        internal GameObject GameObject { get; }
        internal Transform Transform { get; }
        internal SpriteRenderer Renderer { get; }
        internal Vector3 LastRenderPosition { get; set; }
        internal int LastSeenFrame { get; set; }
    }
}
