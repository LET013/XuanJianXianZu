using System;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// WorldBox Actor.tileTarget并非稳定公开API。0.9.9起反射绑定只允许存在于Interop边界，
/// 业务系统只能请求“读取/清除原生导航目标”，不能自行认识字段或属性实现。
/// </summary>
internal static class XjActorTileTargetBridge
{
    private static Type _boundActorType;
    private static FieldInfo _tileTargetField;
    private static PropertyInfo _tileTargetProperty;

    internal static WorldTile Read(Actor actor)
    {
        if (actor == null) return null;
        try
        {
            EnsureBinding(actor.GetType());
            if (_tileTargetField != null) return _tileTargetField.GetValue(actor) as WorldTile;
            return _tileTargetProperty?.GetValue(actor, null) as WorldTile;
        }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/ActorTileTarget.Read", exception);
            return null;
        }
    }

    internal static void Clear(Actor actor)
    {
        if (actor == null) return;
        try
        {
            EnsureBinding(actor.GetType());
            if (_tileTargetField != null) _tileTargetField.SetValue(actor, null);
            else if (_tileTargetProperty != null && _tileTargetProperty.CanWrite)
                _tileTargetProperty.SetValue(actor, null, null);
        }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/ActorTileTarget.Clear", exception);
        }
    }

    internal static void ClearCache()
    {
        _boundActorType = null;
        _tileTargetField = null;
        _tileTargetProperty = null;
    }

    private static void EnsureBinding(Type actorType)
    {
        if (actorType == null || ReferenceEquals(_boundActorType, actorType)) return;
        _boundActorType = actorType;
        _tileTargetField = actorType.GetField(
            "tileTarget",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _tileTargetProperty = _tileTargetField == null
            ? actorType.GetProperty("tileTarget", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            : null;
    }
}
