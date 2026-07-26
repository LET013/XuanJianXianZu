using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 战斗特效基础设施
/// 包含：特效熔断、精灵帧缓存、自适应FPS
/// 
/// 对应 0.5.4 的 RotateEffectsUpdater / 精灵帧缓存 / 自适应FPS / 特效熔断
/// 
/// 玄门化原则：
/// - 纯工具类，无业务耦合
/// - 限频节流避免性能陷阱
/// </summary>
internal static class XjCombatEffectPool
{
    // ==================== 特效熔断系统 ====================

    // 特效失败计数（effectId → 失败次数）
    private static readonly Dictionary<string, int> _effectFailureCount = new Dictionary<string, int>(32);

    // 特效黑名单（effectId → 解禁时间）
    private static readonly Dictionary<string, float> _effectBlacklist = new Dictionary<string, float>(16);

    // 熔断阈值：连续失败3次即熔断
    private const int FailureThreshold = 3;

    // 黑名单冷却时间（秒）
    private const float BlacklistCooldown = 20f;

    /// <summary>
    /// 安全生成特效（带熔断保护）
    /// 连续失败3次后，同一特效会被拉黑20秒
    /// </summary>
    internal static bool TrySpawnEffectSafe(string effectId, float x, float y, float scale = 1f)
    {
        if (string.IsNullOrWhiteSpace(effectId))
            return false;

        // 检查黑名单
        if (IsEffectBlacklisted(effectId))
            return false;

        try
        {
            BaseEffect effect = EffectsLibrary.spawnAt(effectId, new Vector2(x, y), scale <= 0f ? 1f : scale);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
            {
                RecordFailure(effectId);
                return false;
            }

            // 成功：重置失败计数
            ResetFailureCount(effectId);
            return true;
        }
        catch
        {
            RecordFailure(effectId);
            return false;
        }
    }

    /// <summary>
    /// Uses a native pooled effect as a container for a mod sprite animation.
    /// The animation path is data, not an EffectsLibrary effect id.
    /// </summary>
    internal static bool TrySpawnSpriteAnimationSafe(
        string animationPath,
        float x,
        float y,
        float scale,
        float frameIntervalSeconds,
        int repeatFrameSequence = 1)
    {
        if (string.IsNullOrWhiteSpace(animationPath))
            return false;

        string failureKey = "sprite:" + animationPath.Trim();
        if (IsEffectBlacklisted(failureKey))
            return false;

        BaseEffect effect = null;
        try
        {
            Sprite[] frames = GetCachedAnimationFrames(animationPath);
            if (frames.Length == 0)
            {
                frames = GetCachedSingleSpriteFrame(animationPath);
            }

            if (frames.Length == 0)
            {
                RecordFailure(failureKey);
                return false;
            }

            frames = ExpandFrameSequence(frames, repeatFrameSequence);
            effect = EffectsLibrary.spawnAt("fx_slash", new Vector2(x, y), scale <= 0f ? 1f : scale);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
            {
                RecordFailure(failureKey);
                return false;
            }

            SpriteAnimation animation = ((Component)effect).GetComponent<SpriteAnimation>();
            if ((UnityEngine.Object)(object)animation == (UnityEngine.Object)null)
            {
                effect.kill();
                RecordFailure(failureKey);
                return false;
            }

            animation.setFrames(frames);
            ResetAnimationPlayback(effect, animation);
            animation.looped = false;
            animation.returnToPool = true;
            animation.isOn = true;
            animation.timeBetweenFrames = frameIntervalSeconds > 0f ? frameIntervalSeconds : 0.1f;
            ResetFailureCount(failureKey);
            return true;
        }
        catch
        {
            try
            {
                effect?.kill();
            }
            catch
            {
            }

            RecordFailure(failureKey);
            return false;
        }
    }

    internal static bool TrySpawnMovingSpriteAnimationSafe(
        string animationPath,
        Vector2 startPosition,
        Vector2 endPosition,
        float startScale,
        float endScale,
        float frameIntervalSeconds,
        float moveDurationSeconds,
        float holdSeconds)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || moveDurationSeconds <= 0f)
            return false;

        string failureKey = "moving_sprite:" + animationPath.Trim();
        if (IsEffectBlacklisted(failureKey))
            return false;

        BaseEffect effect = null;
        try
        {
            Sprite[] frames = GetCachedAnimationFrames(animationPath);
            if (frames.Length == 0)
            {
                frames = GetCachedSingleSpriteFrame(animationPath);
            }

            if (frames.Length == 0)
            {
                RecordFailure(failureKey);
                return false;
            }

            effect = EffectsLibrary.spawnAt("fx_slash", startPosition, startScale <= 0f ? 1f : startScale);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
            {
                RecordFailure(failureKey);
                return false;
            }

            SpriteAnimation animation = ((Component)effect).GetComponent<SpriteAnimation>();
            if ((UnityEngine.Object)(object)animation == (UnityEngine.Object)null)
            {
                effect.kill();
                RecordFailure(failureKey);
                return false;
            }

            animation.setFrames(frames);
            ResetAnimationPlayback(effect, animation);
            animation.looped = false;
            animation.returnToPool = false;
            animation.isOn = true;
            animation.timeBetweenFrames = frameIntervalSeconds > 0f ? frameIntervalSeconds : 0.1f;

            MapBox world = World.world;
            if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
            {
                effect.kill();
                RecordFailure(failureKey);
                return false;
            }

            ((MonoBehaviour)world).StartCoroutine(MoveAndRecycleEffect(effect, startPosition, endPosition, startScale, endScale, moveDurationSeconds, holdSeconds));
            ResetFailureCount(failureKey);
            return true;
        }
        catch
        {
            try
            {
                effect?.kill();
            }
            catch
            {
            }

            RecordFailure(failureKey);
            return false;
        }
    }

    internal static bool TrySpawnTrackingSpriteAnimationSafe(
        string animationPath,
        Func<Vector2> resolveAnchor,
        float startHeight,
        float startScale,
        float endScale,
        float frameIntervalSeconds,
        float hoverDurationSeconds,
        float smashDurationSeconds,
        float holdSeconds,
        int sortingOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || resolveAnchor == null || smashDurationSeconds <= 0f)
            return false;

        string failureKey = "tracking_sprite:" + animationPath.Trim();
        if (IsEffectBlacklisted(failureKey))
            return false;

        BaseEffect effect = null;
        try
        {
            Sprite[] frames = GetCachedAnimationFrames(animationPath);
            if (frames.Length == 0)
            {
                frames = GetCachedSingleSpriteFrame(animationPath);
            }

            if (frames.Length == 0)
            {
                RecordFailure(failureKey);
                return false;
            }

            Vector2 anchor = resolveAnchor();
            Vector2 startPosition = anchor + Vector2.up * Mathf.Max(0f, startHeight);
            effect = SpawnSpriteAnimationContainer(failureKey, animationPath, frames, startPosition, startScale, frameIntervalSeconds);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
            {
                return false;
            }

            if (sortingOrder != 0 && (UnityEngine.Object)(object)effect.sprite_renderer != (UnityEngine.Object)null)
            {
                effect.sprite_renderer.sortingOrder = sortingOrder;
            }

            MapBox world = World.world;
            if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
            {
                effect.kill();
                RecordFailure(failureKey);
                return false;
            }

            ((MonoBehaviour)world).StartCoroutine(TrackSmashAndRecycleEffect(
                effect,
                resolveAnchor,
                startHeight,
                startScale,
                endScale,
                hoverDurationSeconds,
                smashDurationSeconds,
                holdSeconds));
            ResetFailureCount(failureKey);
            return true;
        }
        catch
        {
            try
            {
                effect?.kill();
            }
            catch
            {
            }

            RecordFailure(failureKey);
            return false;
        }
    }

    internal static bool TrySpawnTrackedOverlaySpriteAnimationSafe(
        string animationPath,
        Func<Vector2> resolveAnchor,
        float scale,
        float frameIntervalSeconds,
        float animationDurationSeconds,
        float holdDurationSeconds,
        int sortingOrder = 64,
        float rotationZ = 0f,
        bool loopFrames = false,
        Func<bool> keepAlive = null)
    {
        if (string.IsNullOrWhiteSpace(animationPath) || resolveAnchor == null)
            return false;

        string failureKey = "tracked_overlay:" + animationPath.Trim();
        if (IsEffectBlacklisted(failureKey))
            return false;

        BaseEffect effect = null;
        try
        {
            Sprite[] frames = GetCachedAnimationFrames(animationPath);
            if (frames.Length == 0)
            {
                frames = GetCachedSingleSpriteFrame(animationPath);
            }

            if (frames.Length == 0)
            {
                RecordFailure(failureKey);
                return false;
            }

            Vector2 position = resolveAnchor();
            effect = SpawnSpriteAnimationContainer(failureKey, animationPath, frames, position, scale, frameIntervalSeconds);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
            {
                return false;
            }

            if ((UnityEngine.Object)(object)effect.sprite_renderer != (UnityEngine.Object)null)
            {
                effect.sprite_renderer.sortingOrder = sortingOrder;
            }

            // This overlay is frame-driven by TrackOverlayAndRecycleEffect so it can
            // loop for the full buff duration. Disable the native one-shot animator to
            // prevent it from racing the tracked frame loop.
            SpriteAnimation trackedAnimation = ((Component)effect).GetComponent<SpriteAnimation>();
            if ((UnityEngine.Object)(object)trackedAnimation != (UnityEngine.Object)null)
            {
                trackedAnimation.isOn = false;
            }

            Component component = effect as Component;
            if ((UnityEngine.Object)(object)component != (UnityEngine.Object)null)
            {
                component.transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
            }

            MapBox world = World.world;
            if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null)
            {
                effect.kill();
                RecordFailure(failureKey);
                return false;
            }

            ((MonoBehaviour)world).StartCoroutine(TrackOverlayAndRecycleEffect(
                effect,
                frames,
                resolveAnchor,
                scale,
                frameIntervalSeconds,
                animationDurationSeconds,
                holdDurationSeconds,
                rotationZ,
                loopFrames,
                keepAlive));
            ResetFailureCount(failureKey);
            return true;
        }
        catch
        {
            try
            {
                effect?.kill();
            }
            catch
            {
            }

            RecordFailure(failureKey);
            return false;
        }
    }

    /// <summary>
    /// 安全生成特效（Actor 位置版本）
    /// </summary>
    internal static bool TrySpawnEffectSafe(string effectId, Actor actor, float scale = 1f)
    {
        if (actor == null || string.IsNullOrWhiteSpace(effectId))
            return false;

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            Vector3 pos = tile != null ? tile.posV3 : Vector3.zero;
            return TrySpawnEffectSafe(effectId, pos.x, pos.y, scale);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsEffectBlacklisted(string effectId)
    {
        if (_effectBlacklist.TryGetValue(effectId, out float unblockTime))
        {
            if (Time.unscaledTime < unblockTime)
                return true;

            // 黑名单过期，移除
            _effectBlacklist.Remove(effectId);
            _effectFailureCount.Remove(effectId);
        }
        return false;
    }

    private static void RecordFailure(string effectId)
    {
        if (!_effectFailureCount.TryGetValue(effectId, out int count))
            count = 0;

        count++;
        _effectFailureCount[effectId] = count;

        if (count >= FailureThreshold)
        {
            _effectBlacklist[effectId] = Time.unscaledTime + BlacklistCooldown;
            Debug.Log("[玄鉴][特效熔断] effect=" + effectId
                + " 已连续失败" + count + "次，拉黑" + BlacklistCooldown + "秒");
        }
    }

    private static void ResetFailureCount(string effectId)
    {
        _effectFailureCount.Remove(effectId);
    }

    // ==================== 精灵帧缓存 ====================

    // 精灵动画帧缓存
    private static readonly Dictionary<string, Sprite[]> _cachedFrames = new Dictionary<string, Sprite[]>(128);

    private const int MaxCachedFrames = 128;

    /// <summary>
    /// 获取缓存的动画帧序列（带容量上限保护）
    /// </summary>
    internal static Sprite[] GetCachedAnimationFrames(string animationId)
    {
        if (string.IsNullOrWhiteSpace(animationId))
            return Array.Empty<Sprite>();

        // 缓存命中
        if (_cachedFrames.TryGetValue(animationId, out Sprite[] cached) && cached != null)
            return cached;

        // 缓存未命中：加载
        try
        {
            string normalizedPath = animationId.Trim().Replace("\\", "/");
            string path = normalizedPath.StartsWith("effects/", StringComparison.OrdinalIgnoreCase)
                ? normalizedPath
                : "effects/" + normalizedPath;
            Sprite[] frames = Resources.LoadAll<Sprite>(path);

            if (frames == null || frames.Length == 0)
            {
                frames = SpriteTextureLoader.getSpriteList(normalizedPath, false);
            }

            frames = SanitizeAnimationFrames(frames);

            // 写入缓存（带容量保护）
            if (frames != null && frames.Length > 0 && _cachedFrames.Count < MaxCachedFrames)
            {
                _cachedFrames[animationId] = frames;
            }

            return frames ?? Array.Empty<Sprite>();
        }
        catch
        {
            return Array.Empty<Sprite>();
        }
    }

    private static Sprite[] ExpandFrameSequence(Sprite[] frames, int repeatFrameSequence)
    {
        if (frames == null || frames.Length == 0 || repeatFrameSequence <= 1)
            return frames ?? Array.Empty<Sprite>();

        int repeat = Mathf.Clamp(repeatFrameSequence, 1, 4);
        Sprite[] expanded = new Sprite[frames.Length * repeat];
        for (int i = 0; i < repeat; i++)
            Array.Copy(frames, 0, expanded, i * frames.Length, frames.Length);
        return expanded;
    }

    private static void ResetAnimationPlayback(BaseEffect effect, SpriteAnimation animation)
    {
        if ((UnityEngine.Object)(object)animation == (UnityEngine.Object)null)
            return;

        try
        {
            animation.currentFrameIndex = 0;
            SpriteRenderer renderer = ((Component)effect).GetComponent<SpriteRenderer>();
            if ((UnityEngine.Object)(object)renderer != (UnityEngine.Object)null)
                renderer.sprite = null;
        }
        catch
        {
        }
    }

    /// <summary>
    /// 清空动画帧缓存（读档/世界重置时需要）
    /// </summary>
    internal static void ClearFrameCache()
    {
        _cachedFrames.Clear();
        _cachedActorCount = 0;
    }

    private static BaseEffect SpawnSpriteAnimationContainer(
        string failureKey,
        string animationPath,
        Sprite[] frames,
        Vector2 position,
        float scale,
        float frameIntervalSeconds)
    {
        BaseEffect effect = EffectsLibrary.spawnAt("fx_slash", position, scale <= 0f ? 1f : scale);
        if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
        {
            RecordFailure(failureKey);
            return null;
        }

        SpriteAnimation animation = ((Component)effect).GetComponent<SpriteAnimation>();
        if ((UnityEngine.Object)(object)animation == (UnityEngine.Object)null)
        {
            effect.kill();
            RecordFailure(failureKey);
            return null;
        }

        animation.setFrames(frames);
        ResetAnimationPlayback(effect, animation);
        animation.looped = false;
        animation.returnToPool = false;
        animation.isOn = true;
        animation.timeBetweenFrames = frameIntervalSeconds > 0f ? frameIntervalSeconds : 0.1f;
        return effect;
    }

    internal static Sprite[] GetCachedSingleSpriteFrame(string animationId)
    {
        if (string.IsNullOrWhiteSpace(animationId))
            return Array.Empty<Sprite>();

        string cacheKey = "single:" + animationId.Trim();
        if (_cachedFrames.TryGetValue(cacheKey, out Sprite[] cached) && cached != null)
            return cached;

        try
        {
            string normalizedPath = animationId.Trim().Replace("\\", "/");
            Sprite sprite = SpriteTextureLoader.getSprite(normalizedPath);
            if ((UnityEngine.Object)(object)sprite == (UnityEngine.Object)null
                && normalizedPath.StartsWith("effects/", StringComparison.OrdinalIgnoreCase))
            {
                sprite = SpriteTextureLoader.getSprite(normalizedPath.Substring("effects/".Length));
            }

            if ((UnityEngine.Object)(object)sprite == (UnityEngine.Object)null)
                return Array.Empty<Sprite>();

            Sprite[] frames = new[] { sprite };
            if (_cachedFrames.Count < MaxCachedFrames)
            {
                _cachedFrames[cacheKey] = frames;
            }

            return frames;
        }
        catch
        {
            return Array.Empty<Sprite>();
        }
    }

    private static Sprite[] SanitizeAnimationFrames(Sprite[] frames)
    {
        if (frames == null || frames.Length == 0)
            return Array.Empty<Sprite>();

        List<Sprite> valid = new List<Sprite>(frames.Length);
        for (int i = 0; i < frames.Length; i++)
        {
            Sprite frame = frames[i];
            if ((UnityEngine.Object)(object)frame != (UnityEngine.Object)null)
                valid.Add(frame);
        }

        valid.Sort((left, right) =>
        {
            int frame = GetFrameNumber(left).CompareTo(GetFrameNumber(right));
            return frame != 0 ? frame : string.Compare(left?.name, right?.name, StringComparison.Ordinal);
        });
        return valid.ToArray();
    }

    private static int GetFrameNumber(Sprite sprite)
    {
        string name = sprite?.name;
        if (string.IsNullOrWhiteSpace(name))
            return int.MaxValue;

        int end = name.Length - 1;
        while (end >= 0 && !char.IsDigit(name[end]))
            end--;
        if (end < 0)
            return int.MaxValue;

        int start = end;
        while (start > 0 && char.IsDigit(name[start - 1]))
            start--;

        return int.TryParse(name.Substring(start, end - start + 1), out int number)
            ? number
            : int.MaxValue;
    }

    private static IEnumerator MoveAndRecycleEffect(
        BaseEffect effect,
        Vector2 startPosition,
        Vector2 endPosition,
        float startScale,
        float endScale,
        float moveDurationSeconds,
        float holdSeconds)
    {
        if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null)
        {
            yield break;
        }

        Component component = effect as Component;
        if ((UnityEngine.Object)(object)component == (UnityEngine.Object)null)
        {
            effect.kill();
            yield break;
        }

        Transform transform = component.transform;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, moveDurationSeconds);
        while (elapsed < duration && (UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = t * t * (3f - 2f * t);
            Vector2 position = Vector2.Lerp(startPosition, endPosition, eased);
            float scale = Mathf.Lerp(startScale <= 0f ? 1f : startScale, endScale <= 0f ? 1f : endScale, eased);
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            transform.localScale = Vector3.one * scale;
            yield return null;
        }

        if ((UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
        {
            transform.position = new Vector3(endPosition.x, endPosition.y, transform.position.z);
            transform.localScale = Vector3.one * (endScale <= 0f ? 1f : endScale);
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            effect.kill();
        }
    }

    private static IEnumerator TrackSmashAndRecycleEffect(
        BaseEffect effect,
        Func<Vector2> resolveAnchor,
        float startHeight,
        float startScale,
        float endScale,
        float hoverDurationSeconds,
        float smashDurationSeconds,
        float holdSeconds)
    {
        if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null || resolveAnchor == null)
        {
            yield break;
        }

        Component component = effect as Component;
        if ((UnityEngine.Object)(object)component == (UnityEngine.Object)null)
        {
            effect.kill();
            yield break;
        }

        Transform transform = component.transform;
        float hoverDuration = Mathf.Max(0f, hoverDurationSeconds);
        if (hoverDuration > 0f)
        {
            float elapsed = 0f;
            while (elapsed < hoverDuration && (UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / hoverDuration);
                float eased = t * t * (3f - 2f * t);
                Vector2 anchor = resolveAnchor();
                float scale = Mathf.Lerp(startScale <= 0f ? 1f : startScale, endScale <= 0f ? 1f : endScale, eased);
                transform.position = new Vector3(anchor.x, anchor.y + Mathf.Max(0f, startHeight), transform.position.z);
                transform.localScale = Vector3.one * scale;
                yield return null;
            }
        }

        float smashDuration = Mathf.Max(0.01f, smashDurationSeconds);
        float smashElapsed = 0f;
        while (smashElapsed < smashDuration && (UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
        {
            smashElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(smashElapsed / smashDuration);
            Vector2 anchor = resolveAnchor();
            float height = Mathf.Lerp(Mathf.Max(0f, startHeight), 0f, t);
            transform.position = new Vector3(anchor.x, anchor.y + height, transform.position.z);
            transform.localScale = Vector3.one * (endScale <= 0f ? 1f : endScale);
            yield return null;
        }

        if ((UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
        {
            Vector2 anchor = resolveAnchor();
            transform.position = new Vector3(anchor.x, anchor.y, transform.position.z);
            transform.localScale = Vector3.one * (endScale <= 0f ? 1f : endScale);
            if (holdSeconds > 0f)
            {
                yield return new WaitForSeconds(holdSeconds);
            }

            effect.kill();
        }
    }

    private static IEnumerator TrackOverlayAndRecycleEffect(
        BaseEffect effect,
        Sprite[] frames,
        Func<Vector2> resolveAnchor,
        float scale,
        float frameIntervalSeconds,
        float animationDurationSeconds,
        float holdDurationSeconds,
        float rotationZ,
        bool loopFrames,
        Func<bool> keepAlive)
    {
        if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null || resolveAnchor == null)
        {
            yield break;
        }

        Component component = effect as Component;
        if ((UnityEngine.Object)(object)component == (UnityEngine.Object)null)
        {
            effect.kill();
            yield break;
        }

        Transform transform = component.transform;
        int lastFrame = Mathf.Max(0, (frames?.Length ?? 0) - 1);
        float interval = frameIntervalSeconds > 0f ? frameIntervalSeconds : 0.1f;
        float elapsed = 0f;
        float animationDuration = Mathf.Max(0.01f, animationDurationSeconds);
        while (elapsed < animationDuration
            && (UnityEngine.Object)(object)effect != (UnityEngine.Object)null
            && (keepAlive == null || keepAlive()))
        {
            elapsed += Time.deltaTime;
            Vector2 position = resolveAnchor();
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
            if ((UnityEngine.Object)(object)effect.sprite_renderer != (UnityEngine.Object)null && frames != null && frames.Length > 0)
            {
                int rawIndex = Mathf.Max(0, Mathf.FloorToInt(elapsed / interval));
                int index = loopFrames ? rawIndex % frames.Length : Mathf.Clamp(rawIndex, 0, lastFrame);
                effect.sprite_renderer.sprite = frames[index];
            }

            yield return null;
        }

        float holdElapsed = 0f;
        float holdDuration = Mathf.Max(0f, holdDurationSeconds);
        while (holdElapsed < holdDuration
            && (UnityEngine.Object)(object)effect != (UnityEngine.Object)null
            && (keepAlive == null || keepAlive()))
        {
            holdElapsed += Time.deltaTime;
            Vector2 position = resolveAnchor();
            transform.position = new Vector3(position.x, position.y, transform.position.z);
            transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            transform.rotation = Quaternion.Euler(0f, 0f, rotationZ);
            if ((UnityEngine.Object)(object)effect.sprite_renderer != (UnityEngine.Object)null && frames != null && frames.Length > 0)
            {
                effect.sprite_renderer.sprite = frames[lastFrame];
            }

            yield return null;
        }

        if ((UnityEngine.Object)(object)effect != (UnityEngine.Object)null)
        {
            effect.kill();
        }
    }

    // ==================== 自适应FPS ====================

    // 高负载判定阈值
    private const int HighLoadActorCount = 300;
    private const int CriticalLoadActorCount = 600;

    // 自适应帧率上限
    private const int NormalFps = 60;
    private const int ReducedFps = 30;
    private const int MinimumFps = 15;

    // ==================== Actor 计数（运行期索引） ====================

    private static int _cachedActorCount;

    /// <summary>
    /// 根据当前 Actor 数量和负载，返回建议的目标 FPS
    /// 用于特效生成节奏控制
    /// </summary>
    internal static int GetAdaptiveTargetFps()
    {
        int actorCount = GetCachedActorCount();
        if (actorCount >= CriticalLoadActorCount)
            return MinimumFps;
        if (actorCount >= HighLoadActorCount)
            return ReducedFps;

        return NormalFps;
    }

    /// <summary>
    /// 获取 Scheduler 已知 Actor 数量，不访问世界单位容器。
    /// </summary>
    private static int GetCachedActorCount()
    {
        _cachedActorCount = XjScheduler.GetKnownActorCount();
        return _cachedActorCount;
    }

    /// <summary>
    /// 判断当前是否处于高负载（应该减少特效生成）
    /// </summary>
    internal static bool IsHighLoad()
    {
        return GetAdaptiveTargetFps() < NormalFps;
    }

    // ==================== 特效帧更新控制 ====================

    // 特效更新间隔（与自适应FPS联动）
    private static float _lastEffectUpdateTime;

    /// <summary>
    /// 是否应该在本帧更新特效（基于自适应FPS节流）
    /// </summary>
    internal static bool ShouldUpdateEffect()
    {
        int targetFps = GetAdaptiveTargetFps();
        float interval = 1f / Mathf.Max(targetFps, MinimumFps);
        float currentTime = Time.unscaledTime;

        if (currentTime - _lastEffectUpdateTime >= interval)
        {
            _lastEffectUpdateTime = currentTime;
            return true;
        }

        return false;
    }

}
