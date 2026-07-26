using System;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// 玄鉴可见角色统一渲染修正通道。
///
/// 原先闭关偏移、器物缩放、紫府雾气、金丹光环分别遍历 visible_units，
/// 在高人口和远景下会把同一批角色重复扫描三至四次。本通道每个渲染数据帧
/// 只遍历一次，并把命中角色分发给各自系统。
/// </summary>
internal static class XjVisibleActorRenderLane
{
    private const int NearVisibleCount = 96;
    private const int MediumVisibleCount = 320;
    private const float MediumCameraSize = 55f;
    private const float FarCameraSize = 95f;

    private static readonly FieldInfo PositionsField = typeof(ActorRenderData).GetField(
        "positions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

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

        bool scanZiFu = XjZiFuBackEffectVisualSystem.BeginRenderFrame(frame);
        bool scanJinDan = XjJinDanHaloVisualSystem.BeginRenderFrame(frame);

        Actor[] actors = manager.visible_units.array;
        int count = actors == null ? 0 : Math.Min(manager.visible_units.count, actors.Length);
        int lodLevel = ResolveLodLevel(frame, count);
        if (count <= 0)
        {
            XjZiFuBackEffectVisualSystem.EndRenderFrame(frame, lodLevel);
            XjJinDanHaloVisualSystem.EndRenderFrame(frame, lodLevel);
            return;
        }

        bool bootstrapPending = XjWorldBootstrapLane.HasPending;
        bool applyClosedCultivation = XjClosedCultivationGuard.HasActive;
        bool applyFaBaoScale = bootstrapPending || XjRuntimeActorInterestIndex.HasAnyFaBaoInterest;
        Vector3[] itemScales = applyFaBaoScale ? manager.render_data.item_scale : null;
        if (itemScales == null) applyFaBaoScale = false;

        bool needEffectPosition = scanZiFu || scanJinDan;
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

                if (scanZiFu)
                {
                    XjZiFuBackEffectVisualSystem.ObserveVisibleActor(actor, manager, position, frame);
                }
                if (scanJinDan)
                {
                    XjJinDanHaloVisualSystem.ObserveVisibleActor(actor, manager, position, frame);
                }
            }
        }

        XjZiFuBackEffectVisualSystem.EndRenderFrame(frame, lodLevel);
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
        catch { }

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
        try
        {
            return PositionsField != null ? PositionsField.GetValue(renderData) as Vector3[] : null;
        }
        catch
        {
            return null;
        }
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
        catch { }

        try
        {
            WorldTile tile = ((BaseSimObject)actor).current_tile;
            if (tile != null)
            {
                position = tile.posV3;
                return true;
            }
        }
        catch { }
        return false;
    }
}
