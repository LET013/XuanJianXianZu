using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
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
    // Performance A/B: halo sprites are purely decorative. Keep all cultivation
    // mechanics intact while preventing per-frame actor-effect rendering.
    private const bool HaloRenderingEnabled = false;
    private const string HaloAnimationId = "effects/Halo/JinDan";
    private const string DaoTaiHaloAnimationId = "effects/Halo/DaoTai";
    private const string HaloFramePrefix = "tree_of_glory";
    private const string DefaultPaletteId = "Default";
    private const float HaloYOffset = 0.46f;
    private const float DaoTaiHaloYOffset = 0.52f;
    private const float HaloScale = 0.0385f;
    private const float DaoTaiHaloScale = 0.044f;
    private const float BackZOffset = -0.05f;
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
            [XjCaiQiPlaceTypeIds.BingGu] = new HaloPalette(
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
    private static Sprite[] _daoTaiSourceFrames;
    private static int _lastCleanupFrame = -1;
    private static int _lastVisibleScanFrame = -1;

    /// <summary>
    /// 准备统一可见角色渲染帧。返回 true 表示本帧需要重新发现可见金丹。
    /// </summary>
    internal static bool BeginRenderFrame(int frame)
    {
        if (!HaloRenderingEnabled)
        {
            // Cleanup only hides entries; Clear additionally destroys the runtime
            // recolor textures/sprites retained by a prior loaded world.
            if (EntriesByActorId.Count > 0 || _sourceFrames != null || _daoTaiSourceFrames != null || RuntimeAssets.Count > 0)
            {
                Clear();
            }
            return false;
        }

        if (!XjCultivatorCache.HasZhenJunOrHigher)
        {
            if (EntriesByActorId.Count > 0) Cleanup(frame, true);
            return false;
        }

        Sprite[] sourceFrames = _sourceFrames;
        if (sourceFrames == null || sourceFrames.Length == 0)
        {
            sourceFrames = ResolveHaloSourceFrames(HaloAnimationId);
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
		if (!HaloRenderingEnabled) return;
		if (XjCultivationPathRules.IsShi(actor))
		{
			RemoveActorHalo(actor);
			return;
		}

        if (!IsEligible(actor, out long actorId, out bool isDaoTai))
        {
            return;
        }

        Sprite[] sourceFrames = isDaoTai ? GetOrCreateDaoTaiSourceFrames() : _sourceFrames;
        if (sourceFrames == null || sourceFrames.Length == 0)
        {
            return;
        }

        string paletteId = isDaoTai ? "DaoTai" : DefaultPaletteId;
        Sprite[] displayFrames = sourceFrames;

        HaloEntry entry = GetOrCreateEntry(actor, actorId, paletteId, manager, position, displayFrames);
        if (entry == null) return;
        entry.Actor = actor;
        entry.LastSeenFrame = frame;
        entry.LastRenderPosition = position;
        entry.IsDaoTai = isDaoTai;

        if (!string.Equals(entry.PaletteId, paletteId, StringComparison.Ordinal)
            || !ReferenceEquals(entry.BoundFrames, displayFrames))
        {
            entry.PaletteId = paletteId;
            entry.BoundFrames = displayFrames;
        }
    }

	private static void RemoveActorHalo(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try
		{
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L && EntriesByActorId.TryGetValue(actorId, out HaloEntry entry))
			{
				DestroyEntry(entry);
				EntriesByActorId.Remove(actorId);
			}
		}
		catch (System.Exception xjCaught126)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:126", xjCaught126);
		}
	}

    internal static void EndRenderFrame(int frame, int lodLevel)
    {
        if (!HaloRenderingEnabled)
        {
            if (EntriesByActorId.Count > 0 || _sourceFrames != null || _daoTaiSourceFrames != null || RuntimeAssets.Count > 0)
            {
                Clear();
            }
            return;
        }

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
        _daoTaiSourceFrames = null;
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
            try { actorId = ((BaseSystemData)entry.Actor.data).id; } catch (System.Exception xjCaught179) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:179", xjCaught179); }

            bool updatePosition = lodLevel < 2 || ((frame + (int)(actorId & 1L)) & 1) == 0;
            Vector3 position = entry.LastRenderPosition;
            if (updatePosition)
            {
                TryGetActorPosition(entry.Actor, ref position);
                entry.LastRenderPosition = position;
                float yOffset = entry.IsDaoTai ? DaoTaiHaloYOffset : HaloYOffset;
                entry.Transform.position = new Vector3(position.x, position.y + yOffset, position.z + BackZOffset);
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
            float scale = entry.IsDaoTai ? DaoTaiHaloScale : HaloScale;
            entry.Transform.localScale = new Vector3(scale, scale, 1f);
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
        catch (System.Exception xjCaught216) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:216", xjCaught216); }

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            if (tile != null) position = tile.posV3;
        }
        catch (System.Exception xjCaught223) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:223", xjCaught223); }
    }

    private static Sprite[] ResolveHaloSourceFrames(string animationId)
    {
        Sprite[] frames = XjCombatEffectPool.GetCachedAnimationFrames(animationId);
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

    private static Sprite[] GetOrCreateDaoTaiSourceFrames()
    {
        Sprite[] sourceFrames = _daoTaiSourceFrames;
        if (sourceFrames != null && sourceFrames.Length > 0)
        {
            return sourceFrames;
        }

        sourceFrames = ResolveHaloSourceFrames(DaoTaiHaloAnimationId);
        _daoTaiSourceFrames = sourceFrames;
        return sourceFrames;
    }

    private static bool IsEligible(Actor actor, out long actorId, out bool isDaoTai)
    {
        actorId = 0L;
        isDaoTai = false;
        if (actor?.data == null
            || !actor.isAlive()
            || XjLongShuSystem.IsLongShu(actor)
            || XjCultivationPathRules.IsShi(actor)) return false;

        try { actorId = ((BaseSystemData)actor.data).id; }
        catch { return false; }
        if (actorId <= 0L) return false;
        if (XjDaoTaiPresenceArchive.IsBeyondWorld(actorId)) return false;

        isDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(actor);
        if (isDaoTai) return true;

        if (XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier))
        {
            return realmTier >= XjRealmSuppression.TierJinDan;
        }

        return actor.hasTrait(XjRealmIds.JinDan)
            || actor.hasTrait(XjRealmIds.ShenDan)
            || actor.hasTrait(XjRealmIds.ZhenJunYuShi)
            || (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
                && (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)
                    || string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.DaoTai, StringComparison.Ordinal)
                    || string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
                    || string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal)));
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
            if (!XjWorldSpriteRenderLayer.TrySpawnActorBackEffect(
                    new Vector2(position.x, position.y + (XjDaoTaiSpellScale.IsDaoTaiActor(actor) ? DaoTaiHaloYOffset : HaloYOffset)),
                    XjDaoTaiSpellScale.IsDaoTaiActor(actor) ? DaoTaiHaloScale : HaloScale,
                    manager,
                    -1,
                    out BaseEffect effect,
                    out SpriteRenderer renderer))
            {
                return null;
            }

            Component component = effect as Component;
            if (component == null)
            {
                effect.kill();
                return null;
            }

            component.gameObject.name = "xj_jindan_halo_" + actorId;
            component.gameObject.hideFlags = HideFlags.DontSave;
            XjWorldSpriteRenderLayer.TryBindLoopAnimation(
                effect,
                frames,
                FrameIntervalSeconds,
                (int)(actorId % frames.Length));
            DisableNativeAnimator(effect);
            renderer.color = Color.white;
            entry = new HaloEntry(actor, effect, component.gameObject, component.transform, renderer, paletteId, frames, position, XjDaoTaiSpellScale.IsDaoTaiActor(actor));
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


    private static void DestroyRuntimePaletteAssets()
    {
        RecoloredFramesByPaletteId.Clear();
        SourcePixelsByTexture.Clear();
        for (int i = 0; i < RuntimeAssets.Count; i++)
        {
            UnityEngine.Object asset = RuntimeAssets[i];
            if (asset == null) continue;
            try { UnityEngine.Object.Destroy(asset); } catch (System.Exception xjCaught487) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:487", xjCaught487); }
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
        // 该对象来自 EffectsLibrary/BaseEffectController 原生对象池。kill() 已负责
        // 将它退回控制器；绝不能随后 Destroy(gameObject)，否则对象池仍保留一个
        // Unity 已销毁的 BaseEffect，下一次 MapBox.clearWorld -> StackEffects.clear
        // 会在 BaseEffect.deactivate 读取 transform 时直接 NullReference。
        try { entry?.Effect?.kill(); } catch (System.Exception xjCaught519) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:519", xjCaught519); }
    }

    private static void DisableNativeAnimator(BaseEffect effect)
    {
        try
        {
            Component component = effect as Component;
            SpriteAnimation spriteAnimation = component != null ? component.GetComponent<SpriteAnimation>() : null;
            if (spriteAnimation != null)
            {
                spriteAnimation.isOn = false;
                if (spriteAnimation is Behaviour spriteAnimationBehaviour)
                    spriteAnimationBehaviour.enabled = false;
            }
            Behaviour animator = component != null ? component.GetComponent("SpriteAnimator") as Behaviour : null;
            if (animator != null) animator.enabled = false;
        }
        catch (System.Exception xjCaught534) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjJinDanHaloVisualSystem.cs:534", xjCaught534); }
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
            BaseEffect effect,
            GameObject gameObject,
            Transform transform,
            SpriteRenderer renderer,
            string paletteId,
            Sprite[] boundFrames,
            Vector3 position,
            bool isDaoTai)
        {
            Actor = actor;
            Effect = effect;
            GameObject = gameObject;
            Transform = transform;
            Renderer = renderer;
            PaletteId = paletteId ?? DefaultPaletteId;
            BoundFrames = boundFrames;
            LastRenderPosition = position;
            LastSeenFrame = Time.frameCount;
            IsDaoTai = isDaoTai;
        }

        internal Actor Actor { get; set; }
        internal BaseEffect Effect { get; }
        internal GameObject GameObject { get; }
        internal Transform Transform { get; }
        internal SpriteRenderer Renderer { get; }
        internal string PaletteId { get; set; }
        internal Sprite[] BoundFrames { get; set; }
        internal Vector3 LastRenderPosition { get; set; }
        internal int LastSeenFrame { get; set; }
        internal bool IsDaoTai { get; set; }
    }
}
