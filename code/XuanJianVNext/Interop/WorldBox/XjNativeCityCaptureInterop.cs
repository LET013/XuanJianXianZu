using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Cached access to WorldBox city capture-unit wrappers. Formation logic consumes
/// IList/Actor values without knowing native private field or wrapper member names.
/// </summary>
internal static class XjNativeCityCaptureInterop
{
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] CapturingUnitFieldNames = { "_capturing_units", "capturing_units", "capturingUnits" };
    private static readonly string[] ActorMemberNames = { "actor", "_actor", "unit" };
    private static FieldInfo _capturingUnitsField;
    private static bool _capturingUnitsFieldResolved;
    private static readonly Dictionary<Type, MemberInfo> ActorMemberByWrapperType = new Dictionary<Type, MemberInfo>();
    private static readonly HashSet<Type> MissingActorMemberTypes = new HashSet<Type>();

    internal static IList GetCapturingUnits(City city)
    {
        if (city == null) return null;
        if (!_capturingUnitsFieldResolved)
        {
            _capturingUnitsFieldResolved = true;
            for (int i = 0; i < CapturingUnitFieldNames.Length; i++)
            {
                _capturingUnitsField = typeof(City).GetField(CapturingUnitFieldNames[i], InstanceFlags);
                if (_capturingUnitsField != null) break;
            }
        }
        try
        {
            return _capturingUnitsField?.GetValue(city) as IList;
        }
        catch
        {
            return null;
        }
    }

    internal static Actor ExtractActor(object value)
    {
        if (value is Actor direct) return direct;
        if (value == null) return null;
        Type type = value.GetType();
        if (MissingActorMemberTypes.Contains(type)) return null;
        if (!ActorMemberByWrapperType.TryGetValue(type, out MemberInfo member))
        {
            for (int i = 0; i < ActorMemberNames.Length && member == null; i++)
            {
                member = type.GetField(ActorMemberNames[i], InstanceFlags);
            }
            if (member == null)
            {
                for (int i = 0; i < ActorMemberNames.Length && member == null; i++)
                {
                    PropertyInfo property = type.GetProperty(ActorMemberNames[i], InstanceFlags);
                    if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) member = property;
                }
            }
            if (member == null)
            {
                MissingActorMemberTypes.Add(type);
                return null;
            }
            ActorMemberByWrapperType[type] = member;
        }
        try
        {
            if (member is FieldInfo field) return field.GetValue(value) as Actor;
            if (member is PropertyInfo property) return property.GetValue(value, null) as Actor;
        }
        catch
        {
            // Native wrapper disappeared or changed shape; fail closed.
        }
        return null;
    }

    internal static void ClearRuntimeCache()
    {
        _capturingUnitsField = null;
        _capturingUnitsFieldResolved = false;
        ActorMemberByWrapperType.Clear();
        MissingActorMemberTypes.Clear();
    }
}
