using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// 玄鉴可见角色统一渲染修正通道。
///
/// 闭关偏移、器物缩放与金丹/道胎光环统一复用 visible_units。
/// 0.9.8.12 起彻底移除紫府/真人常驻 Halo：紫府人口远多于金丹，常驻逐帧
/// 跟随既遮挡画面，也会把视觉对象和 Sprite 写入放大成不必要的渲染热负担。
/// </summary>
internal static class XjVisibleActorRenderLane
{
    private const int NearVisibleCount = 96;
    private const int MediumVisibleCount = 320;
    private const float MediumCameraSize = 55f;
    private const float FarCameraSize = 95f;

    private static int _lastFrame = -1;
    private static ActorRenderData _lastRenderData;
    private static Camera _cachedMainCamera;
    private static int _nextCameraRefreshFrame;

    internal static void Apply(ActorManager manager)
    {
        if (manager?.visible_units == null || manager.render_data == null)
        {
            return;
        }

        int frame = Time.frameCount;
        if (_lastFrame == frame && ReferenceEquals(_lastRenderData, manager.render_data))
        {
            return;
        }
        _lastFrame = frame;
        _lastRenderData = manager.render_data;

        // 紫府/真人常驻 Halo 已删除。不要再触发其帧加载、可见角色发现或对象池创建。
        bool scanJinDan = XjJinDanHaloVisualSystem.BeginRenderFrame(frame);

        Actor[] actors = manager.visible_units.array;
        int count = actors == null ? 0 : Math.Min(manager.visible_units.count, actors.Length);
        int lodLevel = ResolveLodLevel(frame, count);
        if (count <= 0)
        {
            XjJinDanHaloVisualSystem.EndRenderFrame(frame, lodLevel);
            return;
        }

        bool bootstrapPending = XjWorldBootstrapLane.HasPending;
        bool applyClosedCultivation = XjClosedCultivationGuard.HasActive;
        bool applyFaBaoScale = bootstrapPending || XjRuntimeActorInterestIndex.HasAnyFaBaoInterest;
        Vector3[] itemScales = applyFaBaoScale ? manager.render_data.item_scale : null;
        if (itemScales == null) applyFaBaoScale = false;

        bool needEffectPosition = scanJinDan;
        Vector3[] renderPositions = needEffectPosition ? TryGetRenderPositions(manager.render_data) : null;
        bool needVisibleLoop = applyClosedCultivation || applyFaBaoScale || needEffectPosition;
        if (needVisibleLoop)
        {
            for (int i = 0; i < count; i++)
            {
                Actor actor = actors[i];
                if (actor?.data == null)
                {
                    continue;
                }

                if (applyClosedCultivation)
                {
                    XjClosedCultivationGuard.ApplyRenderOffsetCandidate(actor, manager.render_data, i);
                }

                if (applyFaBaoScale && i < itemScales.Length)
                {
                    XjFaBaoMapRenderScale.ApplyVisibleActor(actor, itemScales, i, bootstrapPending);
                }

                if (!needEffectPosition
                    || !TryGetRenderPosition(renderPositions, i, actor, out Vector3 position))
                {
                    continue;
                }

                if (scanJinDan)
                {
                    XjJinDanHaloVisualSystem.ObserveVisibleActor(actor, manager, position, frame);
                }
            }
        }

        XjJinDanHaloVisualSystem.EndRenderFrame(frame, lodLevel);
    }

    internal static void Clear()
    {
        _lastFrame = -1;
        _lastRenderData = null;
        _cachedMainCamera = null;
        _nextCameraRefreshFrame = 0;
    }

    /// <summary>
    /// 以可见人口密度为主、相机正交尺寸为辅判定远景。这样不依赖 WorldBox
    /// 私有相机字段，游戏版本变化时也只会退化为人口密度判断。
    /// 0=近景完整动画，1=中景降帧，2=远景静帧。
    /// </summary>
    private static int ResolveLodLevel(int frame, int visibleCount)
    {
        float cameraSize = 0f;
        try
        {
            if (_cachedMainCamera == null || frame >= _nextCameraRefreshFrame)
            {
                _cachedMainCamera = Camera.main;
                _nextCameraRefreshFrame = frame + 120;
            }
            if (_cachedMainCamera != null && _cachedMainCamera.orthographic)
            {
                cameraSize = _cachedMainCamera.orthographicSize;
            }
        }
        catch (System.Exception xjCaught139) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjVisibleActorRenderLane.cs:139", xjCaught139); }

        if (visibleCount > MediumVisibleCount || cameraSize >= FarCameraSize)
        {
            return 2;
        }
        if (visibleCount > NearVisibleCount || cameraSize >= MediumCameraSize)
        {
            return 1;
        }
        return 0;
    }

    private static Vector3[] TryGetRenderPositions(ActorRenderData renderData)
    {
        return XjNativeActorRenderInterop.TryGetPositions(renderData, out Vector3[] positions)
            ? positions
            : null;
    }

    private static bool TryGetRenderPosition(Vector3[] renderPositions, int index, Actor actor, out Vector3 position)
    {
        position = default;
        if (renderPositions != null && index >= 0 && index < renderPositions.Length)
        {
            position = renderPositions[index];
            return true;
        }

        try
        {
            position = actor.current_position;
            return true;
        }
        catch (System.Exception xjCaught178) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjVisibleActorRenderLane.cs:178", xjCaught178); }

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            if (tile != null)
            {
                position = tile.posV3;
                return true;
            }
        }
        catch (System.Exception xjCaught189) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjVisibleActorRenderLane.cs:189", xjCaught189); }
        return false;
    }
}
