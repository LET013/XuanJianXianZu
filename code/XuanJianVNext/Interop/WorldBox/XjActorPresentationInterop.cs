using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// WorldBox-version tolerant visibility adapter for an existing Actor presentation object.
/// It never removes or kills the actor; persistent identity and domain ownership stay untouched.
/// </summary>
internal static class XjActorPresentationInterop
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] RendererNames =
    {
        "spriteRenderer", "sprite_renderer", "current_sprite_renderer", "renderer"
    };
    private static readonly Dictionary<Type, VisualAccessor> AccessorsByActorType = new Dictionary<Type, VisualAccessor>();
    private static readonly Dictionary<Type, MethodInfo> SetActiveByGameObjectType = new Dictionary<Type, MethodInfo>();
    private static readonly HashSet<Type> MissingSetActiveTypes = new HashSet<Type>();
    private static readonly Dictionary<Type, PropertyInfo> EnabledByRendererType = new Dictionary<Type, PropertyInfo>();
    private static readonly HashSet<Type> MissingEnabledTypes = new HashSet<Type>();

    internal static void SetVisible(Actor actor, bool visible)
    {
        if (actor == null) return;
        try
        {
            Type actorType = actor.GetType();
            if (!AccessorsByActorType.TryGetValue(actorType, out VisualAccessor accessor))
            {
                accessor = ResolveAccessor(actorType);
                AccessorsByActorType[actorType] = accessor;
            }

            object gameObject = ReadMember(accessor.GameObjectMember, actor);
            MethodInfo setActive = ResolveSetActive(gameObject?.GetType());
            setActive?.Invoke(gameObject, new object[] { visible });

            for (int i = 0; i < accessor.RendererMembers.Length; i++)
            {
                object renderer = ReadMember(accessor.RendererMembers[i], actor);
                PropertyInfo enabled = ResolveEnabled(renderer?.GetType());
                if (enabled?.CanWrite == true) enabled.SetValue(renderer, visible, null);
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjActorPresentationInterop.SetVisible", ex);
        }
    }

    internal static void ClearRuntimeCache()
    {
        AccessorsByActorType.Clear();
        SetActiveByGameObjectType.Clear();
        MissingSetActiveTypes.Clear();
        EnabledByRendererType.Clear();
        MissingEnabledTypes.Clear();
    }

    private static MethodInfo ResolveSetActive(Type type)
    {
        if (type == null || MissingSetActiveTypes.Contains(type)) return null;
        if (SetActiveByGameObjectType.TryGetValue(type, out MethodInfo cached)) return cached;

        MethodInfo method = type.GetMethod("SetActive", Flags, null, new[] { typeof(bool) }, null);
        if (method != null) SetActiveByGameObjectType[type] = method;
        else MissingSetActiveTypes.Add(type);
        return method;
    }

    private static PropertyInfo ResolveEnabled(Type type)
    {
        if (type == null || MissingEnabledTypes.Contains(type)) return null;
        if (EnabledByRendererType.TryGetValue(type, out PropertyInfo cached)) return cached;

        PropertyInfo property = type.GetProperty("enabled", Flags);
        if (property != null) EnabledByRendererType[type] = property;
        else MissingEnabledTypes.Add(type);
        return property;
    }

    private static VisualAccessor ResolveAccessor(Type actorType)
    {
        MemberInfo gameObjectMember = ResolveMember(actorType, "gameObject");
        var renderers = new List<MemberInfo>(RendererNames.Length);
        for (int i = 0; i < RendererNames.Length; i++)
        {
            MemberInfo member = ResolveMember(actorType, RendererNames[i]);
            if (member != null) renderers.Add(member);
        }
        return new VisualAccessor(gameObjectMember, renderers.Count == 0 ? Array.Empty<MemberInfo>() : renderers.ToArray());
    }

    private static MemberInfo ResolveMember(Type type, string name)
    {
        return (MemberInfo)type?.GetProperty(name, Flags) ?? type?.GetField(name, Flags);
    }

    private static object ReadMember(MemberInfo member, object target)
    {
        return member switch
        {
            FieldInfo field => field.GetValue(target),
            PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(target, null),
            _ => null
        };
    }

    private readonly struct VisualAccessor
    {
        internal readonly MemberInfo GameObjectMember;
        internal readonly MemberInfo[] RendererMembers;

        internal VisualAccessor(MemberInfo gameObjectMember, MemberInfo[] rendererMembers)
        {
            GameObjectMember = gameObjectMember;
            RendererMembers = rendererMembers ?? Array.Empty<MemberInfo>();
        }
    }
}
