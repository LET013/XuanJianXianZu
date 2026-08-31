using System;
using System.Text;
using UnityEngine;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// 玄鉴世界精灵渲染桥。
/// 前景战斗特效沿用原版 EffectsTop 渲染链；紫府雾气与金丹光环使用
/// 独立精灵对象并投到 EffectsTop 下方的世界对象层，使用略低于透明前景的材质队列。
/// 仅在 EffectsTop 内降低 sortingOrder 无法跨 sorting layer 把特效压到角色身后。
/// </summary>
internal static class XjWorldSpriteRenderLayer
{
    private const string TemplateEffectId = "fx_slash";
    private const int NativeResolveRetryFrames = 30;
    private const int BackLayerResolveRetryFrames = 90;
    private const int BackMaterialQueueOffset = 10;
    private static readonly Vector2 HiddenTemplatePosition = new Vector2(-10000f, -10000f);
    private static readonly string[] RejectedBackLayerTokens =
    {
        "effect", "fx", "top", "front", "foreground", "overlay", "ui", "icon",
        "status", "weather", "cloud", "particle", "projectile", "spell", "light",
        "cursor", "selection", "debug", "screen", "text"
    };

    private static EffectLayerDescriptor _effectLayer;
    private static bool _hasEffectLayer;
    private static int _lastEffectResolveFrame = -10000;

    private static BackLayerDescriptor _backLayer;
    private static bool _hasBackLayer;
    private static int _lastBackLayerResolveFrame = -10000;
    private static Material _backMaterial;

    /// <summary>
    /// 为火鸟等普通世界精灵复制原生前景特效材质、父节点、Unity Layer与排序层。
    /// </summary>
    internal static SpriteRenderer AddRenderer(GameObject root, int relativeSortingOrder)
    {
        if (root == null || !TryResolveNativeEffectLayer(out EffectLayerDescriptor effectLayer)) return null;
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        if (!ApplyNativePath(root, renderer, in effectLayer, useBackMaterial: false))
        {
            UnityEngine.Object.Destroy(renderer);
            return null;
        }

        renderer.sortingLayerID = effectLayer.SortingLayerId;
        renderer.sortingOrder = effectLayer.SortingOrder + relativeSortingOrder;
        renderer.enabled = true;
        return renderer;
    }

    internal static SpriteRenderer AddActorBackRenderer(GameObject root, ActorManager manager, int relativeSortingOrder)
    {
        if (root == null) return null;
        SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
        if (!TryConfigureActorBackRenderer(root, renderer, manager, relativeSortingOrder))
        {
            UnityEngine.Object.Destroy(renderer);
            return null;
        }
        return renderer;
    }

    /// <summary>
    /// 创建独立的角色身后精灵对象。常驻紫府雾气与金丹光环不再借用
    /// fx_slash 对象池，避免自定义动画帧、排序层和 returnToPool 状态污染
    /// 后续战斗特效。调用方必须在失效时 Destroy 返回的 GameObject。
    /// </summary>
    internal static bool TryCreateActorBackSprite(
        Vector2 position,
        float scale,
        ActorManager manager,
        int relativeSortingOrder,
        string objectName,
        out GameObject root,
        out SpriteRenderer renderer)
    {
        root = null;
        renderer = null;
        try
        {
            root = new GameObject(string.IsNullOrWhiteSpace(objectName) ? "xj_actor_back_sprite" : objectName);
            root.hideFlags = HideFlags.DontSave;
            root.transform.position = new Vector3(position.x, position.y, 0f);
            root.transform.localScale = Vector3.one * (scale <= 0f ? 1f : scale);
            renderer = AddActorBackRenderer(root, manager, relativeSortingOrder);
            if (renderer != null)
            {
                renderer.sprite = null;
                renderer.color = Color.white;
                renderer.enabled = true;
                return true;
            }
        }
        catch (System.Exception xjCaught98) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:98", xjCaught98); }

        if (root != null)
        {
            try { UnityEngine.Object.Destroy(root); } catch (System.Exception xjCaught104) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:104", xjCaught104); }
        }
        root = null;
        renderer = null;
        return false;
    }

    internal static bool TryConfigureActorBackRenderer(
        GameObject root,
        SpriteRenderer renderer,
        ActorManager manager,
        int relativeSortingOrder)
    {
        if (root == null || renderer == null
            || !TryResolveNativeEffectLayer(out EffectLayerDescriptor effectLayer)
            || !TryResolveBackLayer(in effectLayer, out BackLayerDescriptor backLayer)
            || !ApplyNativePath(root, renderer, in effectLayer, useBackMaterial: true))
        {
            return false;
        }

        ConfigureActorBackSorting(renderer, in effectLayer, in backLayer, relativeSortingOrder);
        renderer.enabled = true;
        return true;
    }

    /// <summary>
    /// 使用WorldBox原生fx_slash对象池创建常驻特效底座，再将渲染器移出EffectsTop。
    /// 对象池、父节点、相机层仍使用原生路径，避免陆地和浅水中完全不可见。
    /// </summary>
    internal static bool TrySpawnActorBackEffect(
        Vector2 position,
        float scale,
        ActorManager manager,
        int relativeSortingOrder,
        out BaseEffect effect,
        out SpriteRenderer renderer)
    {
        effect = null;
        renderer = null;
        try
        {
            if (!TryResolveNativeEffectLayer(out EffectLayerDescriptor effectLayer)
                || !TryResolveBackLayer(in effectLayer, out BackLayerDescriptor backLayer))
            {
                return false;
            }

            effect = EffectsLibrary.spawnAt(TemplateEffectId, position, scale <= 0f ? 1f : scale);
            if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null) return false;

            Component component = effect as Component;
            renderer = effect.sprite_renderer;
            if (renderer == null && component != null)
            {
                renderer = component.GetComponent<SpriteRenderer>()
                    ?? component.GetComponentInChildren<SpriteRenderer>(true);
            }
            if (renderer == null)
            {
                effect.kill();
                effect = null;
                return false;
            }

            if (component != null)
            {
                SpriteAnimation animation = component.GetComponent<SpriteAnimation>();
                if (animation != null)
                {
                    animation.looped = true;
                    animation.returnToPool = false;
                }
            }

            ApplyBackMaterial(renderer, in effectLayer);
            ConfigureActorBackSorting(renderer, in effectLayer, in backLayer, relativeSortingOrder);
            renderer.enabled = true;
            return true;
        }
        catch
        {
            try { effect?.kill(); } catch (System.Exception xjCaught186) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:186", xjCaught186); }
            effect = null;
            renderer = null;
            return false;
        }
    }

    internal static bool TryBindLoopAnimation(
        BaseEffect effect,
        Sprite[] frames,
        float frameIntervalSeconds,
        int startFrame)
    {
        if ((UnityEngine.Object)(object)effect == (UnityEngine.Object)null
            || frames == null
            || frames.Length == 0)
        {
            return false;
        }

        try
        {
            Component component = effect as Component;
            if (component == null) return false;
            SpriteAnimation animation = component.GetComponent<SpriteAnimation>();
            if (animation == null) return false;

            animation.setFrames(frames);
            animation.timeBetweenFrames = frameIntervalSeconds > 0f ? frameIntervalSeconds : 0.08f;
            animation.looped = true;
            animation.returnToPool = false;
            animation.currentFrameIndex = Math.Abs(startFrame) % frames.Length;
            animation.isOn = true;

            SpriteRenderer renderer = effect.sprite_renderer
                ?? component.GetComponent<SpriteRenderer>()
                ?? component.GetComponentInChildren<SpriteRenderer>(true);
            if (renderer != null)
            {
                renderer.sprite = null;
                renderer.enabled = true;
            }

            try { World.world?.resetRedrawTimer(); } catch (System.Exception xjCaught229) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:229", xjCaught229); }
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryConfigureActorBackEffect(
        SpriteRenderer renderer,
        ActorManager manager,
        int relativeSortingOrder)
    {
        if (renderer == null
            || !TryResolveNativeEffectLayer(out EffectLayerDescriptor effectLayer)
            || !TryResolveBackLayer(in effectLayer, out BackLayerDescriptor backLayer))
        {
            return false;
        }

        try
        {
            ApplyBackMaterial(renderer, in effectLayer);
            ConfigureActorBackSorting(renderer, in effectLayer, in backLayer, relativeSortingOrder);
            renderer.enabled = true;
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ResetActorLayerCache()
    {
        _effectLayer = default;
        _hasEffectLayer = false;
        _lastEffectResolveFrame = -10000;
        _backLayer = default;
        _hasBackLayer = false;
        _lastBackLayerResolveFrame = -10000;

        if (_backMaterial != null)
        {
            try { UnityEngine.Object.Destroy(_backMaterial); } catch (System.Exception xjCaught274) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:274", xjCaught274); }
            _backMaterial = null;
        }
    }

    private static void ConfigureActorBackSorting(
        SpriteRenderer renderer,
        in EffectLayerDescriptor effectLayer,
        in BackLayerDescriptor backLayer,
        int relativeSortingOrder)
    {
        if (renderer == null) return;
        renderer.sortingLayerID = backLayer.SortingLayerId;
        renderer.sortingOrder = Math.Min(-1, relativeSortingOrder);

    }

    private static bool ApplyNativePath(
        GameObject root,
        SpriteRenderer renderer,
        in EffectLayerDescriptor effectLayer,
        bool useBackMaterial)
    {
        try
        {
            if (useBackMaterial)
            {
                ApplyBackMaterial(renderer, in effectLayer);
            }
            else if (effectLayer.SharedMaterial != null)
            {
                renderer.sharedMaterial = effectLayer.SharedMaterial;
            }

            root.layer = effectLayer.GameObjectLayer;
            if (effectLayer.Parent != null && root.transform.parent != effectLayer.Parent)
            {
                root.transform.SetParent(effectLayer.Parent, true);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void ApplyBackMaterial(SpriteRenderer renderer, in EffectLayerDescriptor effectLayer)
    {
        if (renderer == null || effectLayer.SharedMaterial == null) return;
        if (_backMaterial == null)
        {
            _backMaterial = new Material(effectLayer.SharedMaterial)
            {
                name = "XjActorBackEffectMaterial",
                hideFlags = HideFlags.HideAndDontSave
            };
            int queue = effectLayer.MaterialQueue;
            if (queue >= 3000)
            {
                _backMaterial.renderQueue = queue - BackMaterialQueueOffset;
            }
        }
        renderer.sharedMaterial = _backMaterial;
    }

    private static bool TryResolveBackLayer(
        in EffectLayerDescriptor effectLayer,
        out BackLayerDescriptor descriptor)
    {
        descriptor = default;
        if (_hasBackLayer)
        {
            descriptor = _backLayer;
            return true;
        }

        int frame = Time.frameCount;
        if (frame - _lastBackLayerResolveFrame < BackLayerResolveRetryFrames) return false;
        _lastBackLayerResolveFrame = frame;

        SortingLayer[] layers;
        try { layers = SortingLayer.layers; }
        catch { return false; }
        if (layers == null || layers.Length == 0) return false;

        int nativeValue = effectLayer.SortingLayerValue;
        int bestScore = int.MinValue;
        SortingLayer best = default;
        bool found = false;
        StringBuilder snapshot = new StringBuilder(160);

        for (int i = 0; i < layers.Length; i++)
        {
            SortingLayer layer = layers[i];
            int value;
            try { value = SortingLayer.GetLayerValueFromID(layer.id); }
            catch { value = i; }

            if (snapshot.Length > 0) snapshot.Append(',');
            snapshot.Append(layer.name).Append('(').Append(value).Append(')');

            if (layer.id == effectLayer.SortingLayerId || value >= nativeValue) continue;
            string name = layer.name ?? string.Empty;
            if (ContainsRejectedToken(name)) continue;

            int score = ScoreBackLayerName(name);
            // 同类候选中优先选择最靠近EffectsTop的世界对象层。
            score += value;
            if (score <= bestScore) continue;
            bestScore = score;
            best = layer;
            found = true;
        }

        if (!found)
        {
            // 最保守回退：任取EffectsTop下方最高的sorting layer，仍不回到EffectsTop。
            int highestValue = int.MinValue;
            for (int i = 0; i < layers.Length; i++)
            {
                SortingLayer layer = layers[i];
                int value;
                try { value = SortingLayer.GetLayerValueFromID(layer.id); }
                catch { value = i; }
                if (layer.id == effectLayer.SortingLayerId || value >= nativeValue || value <= highestValue) continue;
                highestValue = value;
                best = layer;
                found = true;
            }
        }

        if (!found) return false;

        int bestValue;
        try { bestValue = SortingLayer.GetLayerValueFromID(best.id); }
        catch { bestValue = 0; }

        _backLayer = new BackLayerDescriptor
        {
            SortingLayerId = best.id,
            SortingLayerName = best.name ?? best.id.ToString(),
            SortingLayerValue = bestValue,
            LayerSnapshot = snapshot.ToString()
        };
        _hasBackLayer = true;
        descriptor = _backLayer;
        return true;
    }

    private static int ScoreBackLayerName(string layerName)
    {
        string name = (layerName ?? string.Empty).ToLowerInvariant();
        int score = 0;
        if (name.Contains("actor") || name.Contains("unit")) score += 100000;
        if (name.Contains("creature") || name.Contains("character") || name.Contains("living")) score += 90000;
        if (name.Contains("object") || name.Contains("entity")) score += 70000;
        if (name.Contains("world") || name.Contains("game") || name.Contains("map")) score += 50000;
        if (name.Contains("building") || name.Contains("structure")) score += 20000;
        if (name.Contains("default")) score += 1000;
        return score;
    }

    private static bool ContainsRejectedToken(string layerName)
    {
        string name = (layerName ?? string.Empty).ToLowerInvariant();
        for (int i = 0; i < RejectedBackLayerTokens.Length; i++)
        {
            if (name.Contains(RejectedBackLayerTokens[i])) return true;
        }
        return false;
    }

    private static bool TryResolveNativeEffectLayer(out EffectLayerDescriptor descriptor)
    {
        descriptor = default;
        if (_hasEffectLayer)
        {
            descriptor = _effectLayer;
            return true;
        }

        int frame = Time.frameCount;
        if (frame - _lastEffectResolveFrame < NativeResolveRetryFrames) return false;
        _lastEffectResolveFrame = frame;

        BaseEffect template = null;
        try
        {
            template = EffectsLibrary.spawnAt(TemplateEffectId, HiddenTemplatePosition, 0.01f);
            if ((UnityEngine.Object)(object)template == (UnityEngine.Object)null) return false;

            Component component = template as Component;
            SpriteRenderer source = template.sprite_renderer;
            if (source == null && component != null)
            {
                source = component.GetComponent<SpriteRenderer>()
                    ?? component.GetComponentInChildren<SpriteRenderer>(true);
            }
            if (source == null) return false;

            int layerValue;
            try { layerValue = SortingLayer.GetLayerValueFromID(source.sortingLayerID); }
            catch { layerValue = 0; }

            _effectLayer = new EffectLayerDescriptor
            {
                SortingLayerId = source.sortingLayerID,
                SortingLayerValue = layerValue,
                SortingOrder = source.sortingOrder,
                GameObjectLayer = component != null ? component.gameObject.layer : source.gameObject.layer,
                Parent = component != null ? component.transform.parent : source.transform.parent,
                SharedMaterial = source.sharedMaterial,
                MaterialQueue = source.sharedMaterial != null ? source.sharedMaterial.renderQueue : -1
            };
            _hasEffectLayer = true;
            descriptor = _effectLayer;
            return true;
        }
        catch
        {
            _hasEffectLayer = false;
            return false;
        }
        finally
        {
            try { template?.kill(); } catch (System.Exception xjCaught500) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjWorldSpriteRenderLayer.cs:500", xjCaught500); }
        }
    }

    private struct EffectLayerDescriptor
    {
        internal int SortingLayerId;
        internal int SortingLayerValue;
        internal int SortingOrder;
        internal int GameObjectLayer;
        internal Transform Parent;
        internal Material SharedMaterial;
        internal int MaterialQueue;
    }

    private struct BackLayerDescriptor
    {
        internal int SortingLayerId;
        internal string SortingLayerName;
        internal int SortingLayerValue;
        internal string LayerSnapshot;
    }
}
