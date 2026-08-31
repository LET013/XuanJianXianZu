using System;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.ActorSystem;

/// <summary>
/// 玄鉴角色持久化字段的唯一写入网关。业务服务不再自行决定缓存失效范围；
/// 网关只在值真实变化时落盘，并统一发布角色数据变化事件。
/// </summary>
internal static class XjActorStateWriteGateway
{
    internal static bool SetString(Actor actor, string key, string value)
    {
        if (actor?.data == null) return false;
        string next = value ?? string.Empty;
        BaseSystemData data = (BaseSystemData)actor.data;
        data.get(key, out string current, string.Empty);
        if (string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) return false;
        data.set(key, next);
        XjInternalEventBus.PublishActorDataChanged(actor, key);
        return true;
    }

    internal static bool SetFloat(Actor actor, string key, float value)
    {
        if (actor?.data == null) return false;
        BaseSystemData data = (BaseSystemData)actor.data;
        data.get(key, out float current, 0f);
        if (Math.Abs(current - value) <= 0.0001f) return false;
        data.set(key, value);
        XjInternalEventBus.PublishActorDataChanged(actor, key);
        return true;
    }

    internal static bool SetInt(Actor actor, string key, int value)
    {
        if (actor?.data == null) return false;
        BaseSystemData data = (BaseSystemData)actor.data;
        data.get(key, out int current, 0);
        if (current == value) return false;
        data.set(key, value);
        XjInternalEventBus.PublishActorDataChanged(actor, key);
        return true;
    }

    internal static bool SetLong(Actor actor, string key, long value)
    {
        if (actor?.data == null) return false;
        BaseSystemData data = (BaseSystemData)actor.data;
        try
        {
            data.get(key, out long current, 0L);
            if (current == value) return false;
            data.set(key, value);
            XjInternalEventBus.PublishActorDataChanged(actor, key);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool SetDisplayName(Actor actor, string value, bool customName = true)
    {
        if (actor?.data == null) return false;
        string next = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        bool changed = !string.Equals(actor.getName() ?? string.Empty, next, StringComparison.Ordinal)
            || actor.data.custom_name != customName;
        if (!changed) return false;
        // setName 是 WorldBox 对显示名/自定义名标记的完整原生事务；不得再手写镜像字段。
        actor.setName(next, customName);
        MarkExternal(actor, XuanJianVNext.Core.XjActorStateDomain.Identity);
        return true;
    }

    internal static bool SetNativeName(Actor actor, string value, bool customName = true)
    {
        if (actor?.data == null) return false;
        string next = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        bool changed = !string.Equals(actor.data.name ?? string.Empty, next, StringComparison.Ordinal)
            || actor.data.custom_name != customName;
        if (!changed) return false;
        actor.data.name = next;
        actor.data.custom_name = customName;
        MarkExternal(actor, XuanJianVNext.Core.XjActorStateDomain.Identity);
        return true;
    }

    internal static bool SetExternalFloat(Actor actor, string key, float value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return false;
        actor.data.get(key, out float current, 0f);
        if (Math.Abs(current - value) <= 0.0001f) return false;
        actor.data.set(key, value);
        MarkExternal(actor, domain);
        return true;
    }

    internal static bool SetExternalInt(Actor actor, string key, int value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return false;
        actor.data.get(key, out int current, 0);
        if (current == value) return false;
        actor.data.set(key, value);
        MarkExternal(actor, domain);
        return true;
    }

    internal static bool SetExternalLong(Actor actor, string key, long value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            actor.data.get(key, out long current, 0L);
            if (current == value) return false;
            actor.data.set(key, value);
            MarkExternal(actor, domain);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool SetExternalBool(Actor actor, string key, bool value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return false;
        actor.data.get(key, out bool current, false);
        if (current == value) return false;
        actor.data.set(key, value);
        MarkExternal(actor, domain);
        return true;
    }

    internal static bool SetExternalString(Actor actor, string key, string value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(key)) return false;
        string next = value ?? string.Empty;
        actor.data.get(key, out string current, string.Empty);
        if (string.Equals(current ?? string.Empty, next, StringComparison.Ordinal)) return false;
        actor.data.set(key, next);
        MarkExternal(actor, domain);
        return true;
    }

    internal static bool SetDetachedInt(ActorData data, string key, int value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (data is not BaseSystemData baseData || string.IsNullOrWhiteSpace(key)) return false;
        baseData.get(key, out int current, 0);
        if (current == value) return false;
        baseData.set(key, value);
        MarkDetached(baseData, domain);
        return true;
    }

    internal static bool SetDetachedLong(ActorData data, string key, long value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (data is not BaseSystemData baseData || string.IsNullOrWhiteSpace(key)) return false;
        try
        {
            baseData.get(key, out long current, 0L);
            if (current == value) return false;
            baseData.set(key, value);
            MarkDetached(baseData, domain);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool SetDetachedBool(ActorData data, string key, bool value, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (data is not BaseSystemData baseData || string.IsNullOrWhiteSpace(key)) return false;
        baseData.get(key, out bool current, false);
        if (current == value) return false;
        baseData.set(key, value);
        MarkDetached(baseData, domain);
        return true;
    }

    private static void MarkExternal(Actor actor, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (actor?.data == null) return;
        XjInternalEventBus.PublishActorDomainChanged(actor, domain, "external.actor_data");
    }

    private static void MarkDetached(BaseSystemData data, XuanJianVNext.Core.XjActorStateDomain domain)
    {
        if (data == null || data.id <= 0L || domain == XuanJianVNext.Core.XjActorStateDomain.None) return;
        XuanJianVNext.Core.XjActorStateRevisionStore.Mark(data.id, domain);
    }
}
