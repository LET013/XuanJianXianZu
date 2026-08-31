using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Native territory/building mutation adapter. All version-dependent reflection stays here;
/// gameplay systems only ask for semantic operations.
/// </summary>
internal static class XjTerritoryInterop
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly Dictionary<Type, MethodInfo> BuildingDestroyMethodCache = new Dictionary<Type, MethodInfo>();
    private static readonly Dictionary<Type, MethodInfo> CityRemoveZoneMethodCache = new Dictionary<Type, MethodInfo>();

    internal static bool TryDestroyBuilding(Building building)
    {
        if (building == null) return false;
        Type type = building.GetType();
        if (!BuildingDestroyMethodCache.TryGetValue(type, out MethodInfo method))
        {
            string[] methods = { "removeFromWorld", "remove", "killHimself", "kill", "destroy", "Destroy" };
            for (int i = 0; i < methods.Length && method == null; i++)
            {
                method = type.GetMethod(methods[i], Flags, null, Type.EmptyTypes, null);
            }
            BuildingDestroyMethodCache[type] = method;
        }
        if (method == null) return false;
        try
        {
            method.Invoke(building, null);
            return true;
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjTerritoryInterop.DestroyBuilding", ex);
            return false;
        }
    }

    internal static bool TryDetachTerritoryZone(TileZone zone)
    {
        if (zone == null || zone.city == null) return false;
        City owner = zone.city;

        try
        {
            Type ownerType = owner.GetType();
            if (!CityRemoveZoneMethodCache.TryGetValue(ownerType, out MethodInfo remove))
            {
                remove = ResolveCityRemoveZoneMethod(ownerType);
                CityRemoveZoneMethodCache[ownerType] = remove;
            }
            if (remove != null)
            {
                remove.Invoke(owner, BuildRemoveZoneArguments(remove, zone));
                // 原生 removeZone 可能把 zone 立即转交给别的城，而不一定写 null。
                // 只要旧 owner 已经不再持有它，就视为本次事务完整结束。
                if (!ReferenceEquals(zone.city, owner))
                {
                    TryRecalculateNeighbourZones(owner);
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjTerritoryInterop.DetachTerritory.Native", ex);
        }

        // 禁止再反射修改 City.zones 后直接把 TileZone.city 置空。那会让战争/AI
        // 缓存仍持有旧城与旧Zone组合，下一次攻城检查便解引用半事务对象。
        // 找不到兼容的原生 removeZone 时本轮失败关闭，等待后续维护重试。
        return false;
    }

    private static MethodInfo ResolveCityRemoveZoneMethod(Type ownerType)
    {
        if (ownerType == null) return null;
        try
        {
            MethodInfo exact = ownerType.GetMethod("removeZone", Flags, null, new[] { typeof(TileZone) }, null);
            if (exact != null) return exact;

            MethodInfo[] methods = ownerType.GetMethods(Flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || !string.Equals(method.Name, "removeZone", StringComparison.Ordinal)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(TileZone)) continue;
                return method;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjTerritoryInterop.ResolveRemoveZone", ex);
        }
        return null;
    }

    private static object[] BuildRemoveZoneArguments(MethodInfo method, TileZone zone)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] args = new object[parameters.Length];
        if (args.Length > 0) args[0] = zone;
        for (int i = 1; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
                continue;
            }
            Type type = parameter.ParameterType;
            args[i] = type.IsValueType ? Activator.CreateInstance(type) : null;
        }
        return args;
    }

    internal static void ClearRuntimeCache()
    {
        BuildingDestroyMethodCache.Clear();
        CityRemoveZoneMethodCache.Clear();
    }

    private static void TryRecalculateNeighbourZones(City owner)
    {
        if (owner == null) return;
        try
        {
            owner.recalculateNeighbourZones();
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjTerritoryInterop.RecalculateNeighbourZones", ex);
        }
    }
}
