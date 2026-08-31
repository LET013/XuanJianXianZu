using System;
using System.Collections.Generic;
using System.Reflection;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Cached access to WorldBox/NML selection wrappers. Callers receive the current
/// Actor only and never inspect SelectedMetas/private wrapper members themselves.
/// </summary>
internal static class XjNativeSelectionInterop
{
    private static readonly BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] SelectedMemberNames =
    {
        "selected_actor", "selected_unit", "selected_creature", "selected_meta_object", "selected"
    };
    private static readonly string[] ActorMemberNames = { "actor", "unit", "data" };
    private static readonly MemberInfo[] SelectedMembers = ResolveSelectedMembers();
    private static readonly Dictionary<Type, MemberInfo[]> WrapperActorMembers = new Dictionary<Type, MemberInfo[]>();

    internal static bool TryGetSelectedActor(out Actor actor)
    {
        actor = null;
        for (int i = 0; i < SelectedMembers.Length; i++)
        {
            object value = ReadMember(null, SelectedMembers[i]);
            if (TryResolveActor(value, out actor)) return true;
        }
        return false;
    }

    internal static void ClearRuntimeCache()
    {
        WrapperActorMembers.Clear();
    }

    private static MemberInfo[] ResolveSelectedMembers()
    {
        Type type = typeof(SelectedMetas);
        List<MemberInfo> result = new List<MemberInfo>();
        for (int i = 0; i < SelectedMemberNames.Length; i++)
        {
            FieldInfo field = type.GetField(SelectedMemberNames[i], StaticFlags);
            if (field != null) result.Add(field);
            PropertyInfo property = type.GetProperty(SelectedMemberNames[i], StaticFlags);
            if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) result.Add(property);
        }
        return result.ToArray();
    }

    private static bool TryResolveActor(object value, out Actor actor)
    {
        actor = value as Actor;
        if (actor?.data != null) return true;
        if (value == null) return false;

        Type type = value.GetType();
        if (!WrapperActorMembers.TryGetValue(type, out MemberInfo[] members))
        {
            List<MemberInfo> found = new List<MemberInfo>();
            for (int i = 0; i < ActorMemberNames.Length; i++)
            {
                FieldInfo field = type.GetField(ActorMemberNames[i], InstanceFlags);
                if (field != null) found.Add(field);
                PropertyInfo property = type.GetProperty(ActorMemberNames[i], InstanceFlags);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0) found.Add(property);
            }
            members = found.ToArray();
            WrapperActorMembers[type] = members;
        }

        for (int i = 0; i < members.Length; i++)
        {
            if (ReadMember(value, members[i]) is Actor foundActor && foundActor.data != null)
            {
                actor = foundActor;
                return true;
            }
        }
        return false;
    }

    private static object ReadMember(object target, MemberInfo member)
    {
        try
        {
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property) return property.GetValue(target, null);
        }
        catch
        {
            // Selection is best-effort UI state.
        }
        return null;
    }
}
