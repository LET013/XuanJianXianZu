using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Version-tolerant access to the WorldBox world-persistence custom-data container.
/// Archive serialization, revisioning and backup policy stay in XjWorldArchiveSystem;
/// only native member discovery and string accessor invocation live here.
/// </summary>
internal static class XjNativeWorldArchiveInterop
{
    private static readonly string[] DirectFieldCandidates =
    {
        "data", "_data", "world_data", "_world_data", "map_data", "_map_data", "save_data", "_save_data"
    };

    private static readonly string[] DirectPropertyCandidates =
    {
        "data", "world_data", "map_data", "save_data"
    };

    private static readonly string[][] NestedMemberPathCandidates =
    {
        new[] { "map_stats", "custom_data" },
        new[] { "mapStats", "custom_data" },
        new[] { "stats", "custom_data" },
        new[] { "map_stats", "data" },
        new[] { "mapStats", "data" },
        new[] { "stats", "data" }
    };

    private static readonly string[] SetterMethodCandidates =
    {
        "set", "Set", "setString", "SetString", "setValue", "SetValue", "set_value", "SetValueByKey", "add", "Add"
    };

    private static readonly string[] GetterMethodCandidates =
    {
        "get", "Get", "getString", "GetString", "getValue", "GetValue", "get_value", "GetValueByKey"
    };

    private static MapBox _cachedWorld;
    private static int _cachedWorldSeedId = int.MinValue;
    private static object _cachedWorldData;
    private static Type _cachedWorldDataType;
    private static MethodInfo _cachedGetStringMethod;
    private static MethodInfo _cachedSetStringMethod;
    private static bool _hasLoggedReadFailure;
    private static bool _hasLoggedWriteFailure;
    private static bool _hasLoggedAccessorFailure;

    internal static bool TryReadString(object dataObject, string key, out string value)
    {
        value = string.Empty;
        if (dataObject == null || string.IsNullOrWhiteSpace(key)) return false;

        try
        {
            if (!EnsureDataAccessors(dataObject) || _cachedGetStringMethod == null) return false;

            object[] args = BuildGetterArguments(_cachedGetStringMethod, key.Trim());
            object result = _cachedGetStringMethod.Invoke(dataObject, args);
            value = args.Length > 1 ? (args[1] as string ?? string.Empty) : string.Empty;
            return _cachedGetStringMethod.ReturnType != typeof(bool)
                || result is not bool found
                || found
                || string.IsNullOrWhiteSpace(value);
        }
        catch (Exception ex)
        {
            if (!_hasLoggedReadFailure)
            {
                _hasLoggedReadFailure = true;
                UnityEngine.Debug.LogError("[玄鉴][存档] 读取世界归档字段失败：" + ex);
            }
            return false;
        }
    }

    internal static bool TrySetString(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key)
            || !TryGetDataObject(out object dataObject, createIfMissing: true))
        {
            return false;
        }

        try
        {
            if (!EnsureDataAccessors(dataObject) || _cachedSetStringMethod == null) return false;

            string payload = value ?? string.Empty;
            object[] args = BuildSetterArguments(_cachedSetStringMethod, key.Trim(), payload);
            object result = _cachedSetStringMethod.Invoke(dataObject, args);
            if (_cachedSetStringMethod.ReturnType == typeof(bool) && result is bool written && !written)
            {
                return false;
            }

            return TryReadString(dataObject, key, out string verify)
                && string.Equals(payload, verify ?? string.Empty, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            if (!_hasLoggedWriteFailure)
            {
                _hasLoggedWriteFailure = true;
                UnityEngine.Debug.LogError("[玄鉴][存档] 写入世界归档字段失败：" + ex);
            }
            return false;
        }
    }

    internal static bool TryGetDataObject(out object dataObject, bool createIfMissing)
    {
        EnsureWorldIdentity();
        dataObject = null;
        MapBox world = World.world;
        if ((UnityEngine.Object)(object)world == null) return false;

        if (ReferenceEquals(_cachedWorld, world)
            && _cachedWorldData != null
            && EnsureDataAccessors(_cachedWorldData))
        {
            dataObject = _cachedWorldData;
            return true;
        }

        try
        {
            MapStats mapStats = world.map_stats;
            if (mapStats != null)
            {
                if (mapStats.custom_data == null && createIfMissing)
                {
                    mapStats.custom_data = new SaveCustomData();
                }
                if (IsWorldDataObject(mapStats.custom_data))
                {
                    CacheWorldData(world, mapStats.custom_data);
                    dataObject = _cachedWorldData;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjNativeWorldArchiveInterop.TryGetDataObject.strong", ex);
        }

        for (int i = 0; i < NestedMemberPathCandidates.Length; i++)
        {
            if (!TryResolveNestedPersistentDataObject(
                    world,
                    NestedMemberPathCandidates[i],
                    createIfMissing,
                    out object nested))
            {
                continue;
            }
            CacheWorldData(world, nested);
            dataObject = _cachedWorldData;
            return true;
        }

        Type worldType = world.GetType();
        for (int i = 0; i < DirectFieldCandidates.Length; i++)
        {
            FieldInfo field = FindFieldInHierarchy(worldType, DirectFieldCandidates[i]);
            if (field == null) continue;
            object candidate = field.GetValue(world);
            if (!IsWorldDataObject(candidate)) continue;
            CacheWorldData(world, candidate);
            dataObject = _cachedWorldData;
            return true;
        }

        for (int i = 0; i < DirectPropertyCandidates.Length; i++)
        {
            PropertyInfo property = FindPropertyInHierarchy(worldType, DirectPropertyCandidates[i]);
            if (property == null || !property.CanRead) continue;
            object candidate = property.GetValue(world, null);
            if (!IsWorldDataObject(candidate)) continue;
            CacheWorldData(world, candidate);
            dataObject = _cachedWorldData;
            return true;
        }

        if (!_hasLoggedAccessorFailure)
        {
            _hasLoggedAccessorFailure = true;
            UnityEngine.Debug.LogWarning("[玄鉴][存档] 尚未找到可读写的世界持久化容器，将在后续帧重试。");
        }
        return false;
    }

    internal static void ClearRuntimeCache()
    {
        _cachedWorld = null;
        _cachedWorldSeedId = int.MinValue;
        _cachedWorldData = null;
        _cachedWorldDataType = null;
        _cachedGetStringMethod = null;
        _cachedSetStringMethod = null;
        _hasLoggedReadFailure = false;
        _hasLoggedWriteFailure = false;
        _hasLoggedAccessorFailure = false;
    }

    private static void EnsureWorldIdentity()
    {
        MapBox world = World.world;
        int seedId;
        try
        {
            seedId = world == null ? int.MinValue : MapBox.current_world_seed_id;
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjNativeWorldArchiveInterop.EnsureWorldIdentity", ex);
            seedId = int.MinValue;
        }

        if (ReferenceEquals(_cachedWorld, world) && _cachedWorldSeedId == seedId) return;
        ClearRuntimeCache();
        _cachedWorld = world;
        _cachedWorldSeedId = seedId;
    }

    private static void CacheWorldData(MapBox world, object dataObject)
    {
        _cachedWorld = world;
        _cachedWorldData = dataObject;
        _cachedWorldDataType = null;
        _cachedGetStringMethod = null;
        _cachedSetStringMethod = null;
        EnsureDataAccessors(dataObject);
    }

    private static bool EnsureDataAccessors(object dataObject)
    {
        if (dataObject == null) return false;
        Type dataType = dataObject.GetType();
        if (_cachedWorldDataType == dataType
            && _cachedGetStringMethod != null
            && _cachedSetStringMethod != null)
        {
            return true;
        }

        _cachedWorldDataType = dataType;
        MethodInfo[] methods = dataType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        _cachedSetStringMethod = FindSetterMethod(methods);
        _cachedGetStringMethod = FindGetterMethod(methods);
        return _cachedSetStringMethod != null && _cachedGetStringMethod != null;
    }

    private static MethodInfo FindSetterMethod(IReadOnlyList<MethodInfo> methods)
    {
        for (int nameIndex = 0; nameIndex < SetterMethodCandidates.Length; nameIndex++)
        {
            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, SetterMethodCandidates[nameIndex], StringComparison.Ordinal)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2
                    || parameters[0].ParameterType != typeof(string)
                    || parameters[1].ParameterType != typeof(string)
                    || !TrailingParametersAreOptional(parameters))
                {
                    continue;
                }
                return method;
            }
        }
        return null;
    }

    private static MethodInfo FindGetterMethod(IReadOnlyList<MethodInfo> methods)
    {
        for (int nameIndex = 0; nameIndex < GetterMethodCandidates.Length; nameIndex++)
        {
            for (int i = 0; i < methods.Count; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, GetterMethodCandidates[nameIndex], StringComparison.Ordinal)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length < 2
                    || parameters[0].ParameterType != typeof(string)
                    || !parameters[1].ParameterType.IsByRef
                    || parameters[1].ParameterType.GetElementType() != typeof(string)
                    || !TrailingParametersAreOptional(parameters))
                {
                    continue;
                }
                return method;
            }
        }
        return null;
    }

    private static bool TrailingParametersAreOptional(IReadOnlyList<ParameterInfo> parameters)
    {
        for (int i = 2; i < parameters.Count; i++)
        {
            if (parameters[i].ParameterType.IsByRef
                || (!parameters[i].IsOptional && !parameters[i].HasDefaultValue))
            {
                return false;
            }
        }
        return true;
    }

    private static object[] BuildSetterArguments(MethodInfo method, string key, string value)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] args = new object[parameters.Length];
        args[0] = key;
        args[1] = value;
        for (int i = 2; i < parameters.Length; i++) args[i] = ResolveDefaultArgument(parameters[i]);
        return args;
    }

    private static object[] BuildGetterArguments(MethodInfo method, string key)
    {
        ParameterInfo[] parameters = method.GetParameters();
        object[] args = new object[parameters.Length];
        args[0] = key;
        args[1] = string.Empty;
        for (int i = 2; i < parameters.Length; i++) args[i] = ResolveDefaultArgument(parameters[i]);
        return args;
    }

    private static object ResolveDefaultArgument(ParameterInfo parameter)
    {
        if (parameter.HasDefaultValue) return parameter.DefaultValue;
        if (parameter.IsOptional) return Type.Missing;
        Type type = parameter.ParameterType;
        if (type == typeof(string)) return string.Empty;
        if (type == typeof(bool)) return false;
        if (type.IsEnum)
        {
            Array values = Enum.GetValues(type);
            return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(type);
        }
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static bool IsWorldDataObject(object candidate)
    {
        if (candidate == null) return false;
        MethodInfo[] methods = candidate.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return FindSetterMethod(methods) != null && FindGetterMethod(methods) != null;
    }

    private static bool TryResolveNestedPersistentDataObject(
        object root,
        string[] path,
        bool createIfMissing,
        out object dataObject)
    {
        dataObject = null;
        if (root == null || path == null || path.Length == 0) return false;

        object current = root;
        for (int i = 0; i < path.Length; i++)
        {
            if (current == null
                || !TryGetMemberValue(current, path[i], out object next, out MemberInfo member))
            {
                return false;
            }
            if (next == null && i == path.Length - 1 && createIfMissing)
            {
                TryInstantiateMember(current, member, out next);
            }
            current = next;
        }

        if (!IsWorldDataObject(current)) return false;
        dataObject = current;
        return true;
    }

    private static bool TryGetMemberValue(object instance, string memberName, out object value, out MemberInfo member)
    {
        value = null;
        member = null;
        Type type = instance.GetType();
        FieldInfo field = FindFieldInHierarchy(type, memberName);
        if (field != null)
        {
            member = field;
            value = field.GetValue(instance);
            return true;
        }
        PropertyInfo property = FindPropertyInHierarchy(type, memberName);
        if (property != null && property.CanRead)
        {
            member = property;
            value = property.GetValue(instance, null);
            return true;
        }
        return false;
    }

    private static bool TryInstantiateMember(object instance, MemberInfo member, out object created)
    {
        created = null;
        Type memberType = member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null
        };
        if (memberType == null || memberType.IsAbstract || memberType.IsInterface) return false;

        try
        {
            created = Activator.CreateInstance(memberType);
            if (member is FieldInfo field)
            {
                field.SetValue(instance, created);
                return true;
            }
            if (member is PropertyInfo property && property.CanWrite)
            {
                property.SetValue(instance, created, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjNativeWorldArchiveInterop.TryInstantiateMember", ex);
        }
        created = null;
        return false;
    }

    private static FieldInfo FindFieldInHierarchy(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            FieldInfo field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field != null) return field;
        }
        return null;
    }

    private static PropertyInfo FindPropertyInHierarchy(Type type, string name)
    {
        for (Type current = type; current != null; current = current.BaseType)
        {
            PropertyInfo property = current.GetProperty(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null) return property;
        }
        return null;
    }
}
