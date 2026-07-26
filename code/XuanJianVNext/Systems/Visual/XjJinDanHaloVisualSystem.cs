using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// 金丹常驻身后光环。使用独立 SpriteRenderer，并通过世界渲染桥复用
/// 原生材质、父节点和相机层；不再占用或污染原生 fx_slash 对象池。
///
/// 九大道途配色不新增 PNG：首次遇到某一道途时，运行时读取原 Halo 帧，
/// 保留透明度与明暗层次，并按中心/外环两个区域重着色后缓存。
/// </summary>
internal static class XjJinDanHaloVisualSystem
{
    private const string HaloAnimationId = "effects/Halo/JinDan";
    private const string HaloFramePrefix = "tree_of_glory";
    private const string DefaultPaletteId = "Default";
    private const float HaloYOffset = 1.75f;
    private const float HaloScale = 0.0385f;
    private const float BackZOffset = 0.15f;
    private const int FrameStep = 3;
    private const float FrameIntervalSeconds = 0.08f;
    private const int VisibleScanCadenceFrames = 10;
    private const int CleanupIntervalFrames = 45;
    private const int StaleFrameThreshold = 24;

    private static readonly Dictionary<string, HaloPalette> PalettesById =
        new Dictionary<string, HaloPalette>(StringComparer.Ordinal)
        {
            [XjCaiQiPlaceTypeIds.SanYang] = new HaloPalette(
                new Color32(255, 229, 112, 255),
                new Color32(255, 179, 45, 255)),
            [XjCaiQiPlaceTypeIds.SanYin] = new HaloPalette(
                new Color32(218, 231, 255, 255),
                new Color32(131, 158, 228, 255)),
            [XjCaiQiPlaceTypeIds.SanLei] = new HaloPalette(
                new Color32(235, 211, 255, 255),
                new Color32(164, 104, 233, 255)),
            [XjCaiQiPlaceTypeIds.JinDe] = new HaloPalette(
                new Color32(255, 250, 220, 255),
                new Color32(220, 185, 97, 255)),
            [XjCaiQiPlaceTypeIds.MuDe] = new HaloPalette(
                new Color32(201, 238, 168, 255),
                new Color32(91, 174, 89, 255)),
            [XjCaiQiPlaceTypeIds.ShuiDe] = new HaloPalette(
                new Color32(191, 232, 255, 255),
                new Color32(69, 143, 222, 255)),
            [XjCaiQiPlaceTypeIds.HuoDe] = new HaloPalette(
                new Color32(255, 194, 104, 255),
                new Color32(235, 74, 42, 255)),
            [XjCaiQiPlaceTypeIds.TuDe] = new HaloPalette(
                new Color32(239, 211, 151, 255),
                new Color32(164, 111, 61, 255)),
            [XjCaiQiPlaceTypeIds.SuDe] = new HaloPalette(
                new Color32(238, 244, 226, 255),
                new Color32(122, 184, 157, 255)),
            [XjCaiQiPlaceTypeIds.ShiErQi] = new HaloPalette(
                new Color32(245, 221, 255, 255),
                new Color32(145, 93, 205, 255)),
            [DefaultPaletteId] = new HaloPalette(
                new Color32(255, 228, 104, 255),
                new Color32(255, 180, 42, 255))
        };

    private static readonly Dictionary<long, HaloEntry> EntriesByActorId = new Dictionary<long, HaloEntry>(64);
    private static readonly Dictionary<string, Sprite[]> RecoloredFramesByPaletteId =
        new Dictionary<string, Sprite[]>(StringComparer.Ordinal);
    private static readonly List<UnityEngine.Object> RuntimeAssets = new List<UnityEngine.Object>(512);
    private static readonly Dictionary<Texture2D, Color32[]> SourcePixelsByTexture = new Dictionary<Texture2D, Color32[]>();
    private static readonly List<long> CleanupBuffer = new List<long>(64);
    private static Sprite[] _sourceFrames;
    private static int _lastCleanupFrame = -1;
    private static int _lastVisibleScanFrame = -1;

    /// <summary>
    /// 准备统一可见角色渲染帧。返回 true 表示本帧需要重新发现可见金丹。
    /// </summary>
    internal static bool BeginRenderFrame(int frame)
    {
        if (!XjCultivatorCache.HasJinDan)
        {
            if (EntriesByActorId.Count > 0) Cleanup(frame, true);
            return false;
        }

        Sprite[] sourceFrames = _sourceFrames;
        if (sourceFrames == null || sourceFrames.Length == 0)
        {
            sourceFrames = ResolveHaloSourceFrames();
            if (sourceFrames == null || sourceFrames.Length == 0)
            {
                Cleanup(frame, true);
                return false;
            }
            EnsureSourceFrames(sourceFrames);
        }

        bool shouldScan = _lastVisibleScanFrame < 0
            || frame - _lastVisibleScanFrame >= VisibleScanCadenceFrames;
        if (shouldScan) _lastVisibleScanFrame = frame;
        return shouldScan;
    }

    internal static void ObserveVisibleActor(Actor actor, ActorManager manager, Vector3 position, int frame)
    {
        Sprite[] sourceFrames = _sourceFrames;
        if (sourceFrames == null || sourceFrames.Length == 0
            || !IsEligible(actor, out long actorId))
        {
            return;
        }

        string paletteId = ResolvePaletteId(actor);
        Sprite[] displayFrames = GetOrCreateRecoloredFrames(sourceFrames, paletteId);
        if (displayFrames == null || displayFrames.Length == 0) displayFrames = sourceFrames;

        HaloEntry entry = GetOrCreateEntry(actor, actorId, paletteId, manager, position, displayFrames);
        if (entry == null) return;
        entry.Actor = actor;
        entry.LastSeenFrame = frame;
        entry.LastRenderPosition = position;

        if (!string.Equals(entry.PaletteId, paletteId, StringComparison.Ordinal)
            || !ReferenceEquals(entry.BoundFrames, displayFrames))
        {
            entry.PaletteId = paletteId;
            entry.BoundFrames = displayFrames;
        }
    }

    internal static void EndRenderFrame(int frame, int lodLevel)
    {
        UpdateExistingEntries(frame, lodLevel);
        if (_lastCleanupFrame < 0 || frame - _lastCleanupFrame >= CleanupIntervalFrames)
        {
            Cleanup(frame, false);
        }
    }

    internal static void Clear()
    {
        foreach (HaloEntry entry in EntriesByActorId.Values) DestroyEntry(entry);
        EntriesByActorId.Clear();
        CleanupBuffer.Clear();
        DestroyRuntimePaletteAssets();
        _sourceFrames = null;
        _lastCleanupFrame = -1;
        _lastVisibleScanFrame = -1;
        XjWorldSpriteRenderLayer.ResetActorLayerCache();
    }

    private static void UpdateExistingEntries(int frame, int lodLevel)
    {
        foreach (HaloEntry entry in EntriesByActorId.Values)
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

            Sprite[] frames = entry.BoundFrames;
            if (frames == null || frames.Length == 0) continue;

            long actorId = 0L;
            try { actorId = ((BaseSystemData)entry.Actor.data).id; } catch { }

            bool updatePosition = lodLevel < 2 || ((frame + (int)(actorId & 1L)) & 1) == 0;
            Vector3 position = entry.LastRenderPosition;
            if (updatePosition)
            {
                TryGetActorPosition(entry.Actor, ref position);
                entry.LastRenderPosition = position;
                entry.Transform.position = new Vector3(position.x, position.y + HaloYOffset, position.z + BackZOffset);
            }

            int spriteIndex;
            if (lodLevel >= 2)
            {
                // 远景固定一帧，仍保留不同角色的相位差。
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
            entry.Transform.localScale = new Vector3(HaloScale, HaloScale, 1f);
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

    private static Sprite[] ResolveHaloSourceFrames()
    {
        Sprite[] frames = XjCombatEffectPool.GetCachedAnimationFrames(HaloAnimationId);
        if (frames == null || frames.Length == 0)
        {
            return Array.Empty<Sprite>();
        }

        List<Sprite> filtered = null;
        for (int i = 0; i < frames.Length; i++)
        {
            Sprite sprite = frames[i];
            if (sprite == null)
            {
                continue;
            }

            string name = sprite.name ?? string.Empty;
            if (!name.StartsWith(HaloFramePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (filtered == null) filtered = new List<Sprite>(frames.Length);
            filtered.Add(sprite);
        }

        return filtered != null && filtered.Count > 0 ? filtered.ToArray() : frames;
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
            return realmTier >= XjRealmSuppression.TierJinDan;
        }

        return actor.hasTrait(XjRealmIds.JinDan)
            || (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
                && string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal));
    }

    private static HaloEntry GetOrCreateEntry(
        Actor actor,
        long actorId,
        string paletteId,
        ActorManager manager,
        Vector3 position,
        Sprite[] frames)
    {
        if (EntriesByActorId.TryGetValue(actorId, out HaloEntry entry)
            && entry != null
            && entry.Transform != null
            && entry.Renderer != null)
        {
            return entry;
        }

        try
        {
            if (!XjWorldSpriteRenderLayer.TryCreateActorBackSprite(
                    new Vector2(position.x, position.y + HaloYOffset),
                    HaloScale,
                    manager,
                    -1,
                    "xj_jindan_halo_" + actorId,
                    out GameObject gameObject,
                    out SpriteRenderer renderer))
            {
                return null;
            }

            renderer.color = Color.white;
            entry = new HaloEntry(actor, gameObject, gameObject.transform, renderer, paletteId, frames, position);
            EntriesByActorId[actorId] = entry;
            return entry;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureSourceFrames(Sprite[] sourceFrames)
    {
        if (ReferenceEquals(_sourceFrames, sourceFrames)) return;

        DestroyRuntimePaletteAssets();
        _sourceFrames = sourceFrames;
    }

    private static string ResolvePaletteId(Actor actor)
    {
        if (actor?.data == null) return DefaultPaletteId;

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu))
        {
            string normalized = (daoTu ?? string.Empty).Trim();
            if (XjCaiQiCatalog.TryGetEntryByDisplayName(normalized, out XjCaiQiCatalogEntry entry)
                && PalettesById.ContainsKey(entry.PlaceTypeId))
            {
                return entry.PlaceTypeId;
            }

            return normalized switch
            {
                "三阳" => XjCaiQiPlaceTypeIds.SanYang,
                "三阴" => XjCaiQiPlaceTypeIds.SanYin,
                "三雷" => XjCaiQiPlaceTypeIds.SanLei,
                "金德" => XjCaiQiPlaceTypeIds.JinDe,
                "木德" => XjCaiQiPlaceTypeIds.MuDe,
                "水德" => XjCaiQiPlaceTypeIds.ShuiDe,
                "火德" => XjCaiQiPlaceTypeIds.HuoDe,
                "土德" => XjCaiQiPlaceTypeIds.TuDe,
                "素德" => XjCaiQiPlaceTypeIds.SuDe,
                "十二炁" => XjCaiQiPlaceTypeIds.ShiErQi,
                _ => DefaultPaletteId
            };
        }

        return DefaultPaletteId;
    }

    private static Sprite[] GetOrCreateRecoloredFrames(Sprite[] sourceFrames, string paletteId)
    {
        if (string.IsNullOrWhiteSpace(paletteId) || !PalettesById.TryGetValue(paletteId, out HaloPalette palette))
        {
            paletteId = DefaultPaletteId;
            palette = PalettesById[DefaultPaletteId];
        }

        if (RecoloredFramesByPaletteId.TryGetValue(paletteId, out Sprite[] cached)
            && cached != null
            && cached.Length == sourceFrames.Length)
        {
            return cached;
        }

        Sprite[] recolored = new Sprite[sourceFrames.Length];
        for (int i = 0; i < sourceFrames.Length; i++)
        {
            recolored[i] = TryCreateRecoloredSprite(sourceFrames[i], palette, paletteId, i) ?? sourceFrames[i];
        }

        RecoloredFramesByPaletteId[paletteId] = recolored;
        return recolored;
    }

    private static Sprite TryCreateRecoloredSprite(Sprite source, HaloPalette palette, string paletteId, int frameIndex)
    {
        if (source == null || source.texture == null) return null;

        try
        {
            Texture2D sourceTexture = source.texture;
            Rect sourceRect = source.textureRect;
            int width = Mathf.Max(1, Mathf.RoundToInt(sourceRect.width));
            int height = Mathf.Max(1, Mathf.RoundToInt(sourceRect.height));
            int startX = Mathf.RoundToInt(sourceRect.x);
            int startY = Mathf.RoundToInt(sourceRect.y);

            if (!SourcePixelsByTexture.TryGetValue(sourceTexture, out Color32[] sourcePixels))
            {
                sourcePixels = sourceTexture.GetPixels32();
                if (sourcePixels != null) SourcePixelsByTexture[sourceTexture] = sourcePixels;
            }
            if (sourcePixels == null || sourcePixels.Length < sourceTexture.width * sourceTexture.height) return null;

            Color32[] targetPixels = new Color32[width * height];
            Color centerColor = palette.Center;
            Color outerColor = palette.Outer;

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int sourceIndex = (startY + y) * sourceTexture.width + startX + x;
                    int targetIndex = y * width + x;
                    if (sourceIndex < 0 || sourceIndex >= sourcePixels.Length)
                    {
                        targetPixels[targetIndex] = default;
                        continue;
                    }

                    Color32 sourcePixel = sourcePixels[sourceIndex];
                    if (sourcePixel.a == 0)
                    {
                        targetPixels[targetIndex] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    float normalizedX = ((x + 0.5f) / width - 0.5f) * 2f;
                    float normalizedY = ((y + 0.5f) / height - 0.5f) * 2f;
                    float radius = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    float outerBlend = Mathf.Clamp01((radius - 0.29f) / 0.13f);
                    outerBlend = outerBlend * outerBlend * (3f - 2f * outerBlend);
                    Color routeColor = Color.Lerp(centerColor, outerColor, outerBlend);

                    float luminance = (sourcePixel.r * 0.2126f + sourcePixel.g * 0.7152f + sourcePixel.b * 0.0722f) / 255f;
                    float shade = Mathf.Clamp01((luminance - 0.18f) / 0.58f);
                    Color dark = Color.Lerp(routeColor * 0.72f, routeColor, 0.18f);
                    dark.a = 1f;
                    Color light = Color.Lerp(routeColor, Color.white, 0.22f);
                    light.a = 1f;
                    Color result = Color.Lerp(dark, light, shade);

                    targetPixels[targetIndex] = new Color32(
                        (byte)Mathf.Clamp(Mathf.RoundToInt(result.r * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(result.g * 255f), 0, 255),
                        (byte)Mathf.Clamp(Mathf.RoundToInt(result.b * 255f), 0, 255),
                        sourcePixel.a);
                }
            }

            Texture2D targetTexture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "xj_halo_" + paletteId + "_" + frameIndex,
                filterMode = sourceTexture.filterMode,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            targetTexture.SetPixels32(targetPixels);
            targetTexture.Apply(false, false);

            Vector2 pivot = new Vector2(
                source.rect.width <= 0f ? 0.5f : source.pivot.x / source.rect.width,
                source.rect.height <= 0f ? 0.5f : source.pivot.y / source.rect.height);
            Sprite targetSprite = Sprite.Create(
                targetTexture,
                new Rect(0f, 0f, width, height),
                pivot,
                source.pixelsPerUnit);
            targetSprite.name = "xj_halo_" + paletteId + "_" + frameIndex;
            targetSprite.hideFlags = HideFlags.DontSave;

            RuntimeAssets.Add(targetSprite);
            RuntimeAssets.Add(targetTexture);
            return targetSprite;
        }
        catch
        {
            return null;
        }
    }

    private static void DestroyRuntimePaletteAssets()
    {
        RecoloredFramesByPaletteId.Clear();
        SourcePixelsByTexture.Clear();
        for (int i = 0; i < RuntimeAssets.Count; i++)
        {
            UnityEngine.Object asset = RuntimeAssets[i];
            if (asset == null) continue;
            try { UnityEngine.Object.Destroy(asset); } catch { }
        }
        RuntimeAssets.Clear();
    }

    private static void Cleanup(int frame, bool force)
    {
        CleanupBuffer.Clear();
        foreach (KeyValuePair<long, HaloEntry> pair in EntriesByActorId)
        {
            HaloEntry entry = pair.Value;
            if (force || entry == null || entry.Transform == null || frame - entry.LastSeenFrame > StaleFrameThreshold)
            {
                CleanupBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < CleanupBuffer.Count; i++)
        {
            long actorId = CleanupBuffer[i];
            if (EntriesByActorId.TryGetValue(actorId, out HaloEntry entry)) DestroyEntry(entry);
            EntriesByActorId.Remove(actorId);
        }
        CleanupBuffer.Clear();
        _lastCleanupFrame = frame;
    }

    private static void DestroyEntry(HaloEntry entry)
    {
        try
        {
            if (entry?.GameObject != null) UnityEngine.Object.Destroy(entry.GameObject);
        }
        catch { }
    }

    private readonly struct HaloPalette
    {
        internal HaloPalette(Color32 center, Color32 outer)
        {
            Center = center;
            Outer = outer;
        }

        internal Color32 Center { get; }
        internal Color32 Outer { get; }
    }

    private sealed class HaloEntry
    {
        internal HaloEntry(
            Actor actor,
            GameObject gameObject,
            Transform transform,
            SpriteRenderer renderer,
            string paletteId,
            Sprite[] boundFrames,
            Vector3 position)
        {
            Actor = actor;
            GameObject = gameObject;
            Transform = transform;
            Renderer = renderer;
            PaletteId = paletteId ?? DefaultPaletteId;
            BoundFrames = boundFrames;
            LastRenderPosition = position;
            LastSeenFrame = Time.frameCount;
        }

        internal Actor Actor { get; set; }
        internal GameObject GameObject { get; }
        internal Transform Transform { get; }
        internal SpriteRenderer Renderer { get; }
        internal string PaletteId { get; set; }
        internal Sprite[] BoundFrames { get; set; }
        internal Vector3 LastRenderPosition { get; set; }
        internal int LastSeenFrame { get; set; }
    }
}
