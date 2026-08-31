using System;
using System.Collections.Generic;
using System.Reflection;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Cached native ActorAsset compatibility reads shared by death/reincarnation flows.
/// </summary>
internal static class XjNativeActorAssetInterop
{
    private static readonly Dictionary<Type, PropertyInfo> RacePropertyByType = new Dictionary<Type, PropertyInfo>();
    private static readonly HashSet<Type> MissingRacePropertyTypes = new HashSet<Type>();

    internal static string ReadRaceName(Actor actor)
    {
        object asset = actor?.asset;
        if (asset == null) return string.Empty;
        Type type = asset.GetType();
        if (MissingRacePropertyTypes.Contains(type)) return string.Empty;
        if (!RacePropertyByType.TryGetValue(type, out PropertyInfo property))
        {
            property = type.GetProperty(
                "race",
                BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public);
            if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                MissingRacePropertyTypes.Add(type);
                return string.Empty;
            }
            RacePropertyByType[type] = property;
        }
        try
        {
            return property.GetValue(asset, null)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static void ClearRuntimeCache()
    {
        RacePropertyByType.Clear();
        MissingRacePropertyTypes.Clear();
    }
}
