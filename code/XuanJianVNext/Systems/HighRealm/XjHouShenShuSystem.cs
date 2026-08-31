using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 全丹「候神殊」的第一段原著生命周期：自然坐化后神尸起坐。
/// 原著明确：自然坐化可借神尸延续128载，每多一道神通再增20年，神尸不能继续求道。
/// 被他人杀害后“逃出一点性命亦能显化神尸”还依赖散白羽落大成条件，本版不伪造。
/// </summary>
internal static class XjHouShenShuSystem
{
    internal static bool IsShenShi(Actor actor)
    {
        if (actor?.data == null
            || !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHouShenShuShenShi, out int value)
            || value <= 0)
        {
            return false;
        }

        // 神尸只属于紫府这一层。旧档若曾把候神殊错误落在筑基或其他境界，
        // 这里不再把它当作有效神尸，后续投影对账会清理旧标记。
        return XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal);
    }

    internal static string DecorateRealmDisplay(Actor actor, string realmDisplay)
    {
        // “神尸”是姓名末尾对“紫府”的身份替代，不是一个叠加在境界后的第二后缀。
        // 境界栏、排行榜和照录仍显示真实的紫府阶段，例如“紫府后期”。
        return (realmDisplay ?? string.Empty).Trim();
    }

    internal static void EnsureShenShiRealmProjection(Actor actor)
    {
        if (actor?.data == null) return;

        bool hasMarker = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHouShenShuShenShi, out int marker)
            && marker > 0;
        if (!hasMarker) return;

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
        string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
        if (!string.Equals(normalizedRealm, XjRealmIds.ZiFu, StringComparison.Ordinal))
        {
            // 旧版曾允许筑基等非紫府角色留下神尸标记。现在直接撤销这些非法状态；
            // 境界标题本身仍由当前真实境界的标题系统负责。
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuShenShi, 0);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuLifespanBonus, 0);
            XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuShenShiYear, 0);
            string invalidName = actor.getName() ?? string.Empty;
            if (invalidName.EndsWith("-神尸", StringComparison.Ordinal))
            {
                string fallbackRealm = XjRealmHelper.GetDisplayName(normalizedRealm);
                if (!string.IsNullOrWhiteSpace(fallbackRealm))
                {
                    XjActorStateWriteGateway.SetDisplayName(
                        actor,
                        invalidName.Substring(0, invalidName.Length - "-神尸".Length) + "-" + fallbackRealm,
                        customName: true);
                }
            }
            return;
        }

        // XjNameRealmDisplay继续保留真实境界“紫府”，只把角色姓名末尾的“-紫府”替换为“-神尸”。
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, "紫府");
        string currentName = actor.getName() ?? string.Empty;
        string newName;
        if (currentName.EndsWith("-神尸", StringComparison.Ordinal))
        {
            newName = currentName;
        }
        else if (currentName.EndsWith("-紫府", StringComparison.Ordinal))
        {
            newName = currentName.Substring(0, currentName.Length - "-紫府".Length) + "-神尸";
        }
        else
        {
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string storedBase);
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
            string baseName = (storedBase ?? string.Empty).Trim();
            string title = (storedTitle ?? string.Empty).Trim();
            if (baseName.Length == 0) return;
            newName = title.Length == 0 ? baseName + "-神尸" : title + "·" + baseName + "-神尸";
        }

        if (!string.Equals(actor.getName()?.Trim(), newName.Trim(), StringComparison.Ordinal))
            XjActorStateWriteGateway.SetDisplayName(actor, newName, customName: true);
    }

    internal static bool TryConvertNaturalDeathToShenShi(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive() || IsShenShi(actor)) return false;

        // 候神殊的神尸只允许紫府在自然坐化时显化。筑基、炼气、金丹、
        // 服气体系与其他境界都不进入这条终局替代。
        if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            || !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal))
        {
            return false;
        }

        string[] learned = XjXianJiAccessor.ReadRawIds(actor);
        if (!ContainsAny(learned, "候神殊", "侯神殊")) return false;

        int count = 0;
        for (int i = 0; i < learned.Length; i++)
            if (!string.IsNullOrWhiteSpace(learned[i])) count++;
        int bonusYears = 128 + Math.Max(0, count) * 20;
        int year = Math.Max(0, World.world?.map_stats?.year ?? 0);

        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuShenShi, 1);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuLifespanBonus, bonusYears);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHouShenShuShenShiYear, year);
        try { actor.setStatsDirty(); actor.updateStats(); } catch { }
        try
        {
            float maxHealth = Math.Max(1f, XuanJianVNext.Core.XjSafeCore.GetMaxHealthSafe(actor, 1f));
            int restore = Math.Max(1, (int)Math.Ceiling(maxHealth * 0.45f));
            actor.restoreHealth(restore);
        }
        catch { }

        try
        {
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string currentRealmId);
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
            if (!string.IsNullOrWhiteSpace(currentRealmId))
                XjRealmTitleApplyService.EnsureTitleForRealm(actor, currentRealmId, daoTu);
            EnsureShenShiRealmProjection(actor);
        }
        catch { }

        XjCanonicalShenTongVisualSystem.TryPlayHouShenShuShenShi(actor);
        try
        {
            XjWorldHistoryStore.RecordActorEvent(
                actor,
                actor.getName() + "自然坐化，候神殊所孕铅汞神尸自遗蜕中起坐，续得" + bonusYears + "年性命；自此神尸只可存世，不再求道。",
                XjEventIconCatalog.HistoryWorld);
        }
        catch { }
        return true;
    }

    private static bool ContainsAny(string[] learned, params string[] names)
    {
        if (learned == null || names == null) return false;
        for (int i = 0; i < learned.Length; i++)
        {
            string value = (learned[i] ?? string.Empty).Trim();
            if (value.Length == 0) continue;
            for (int j = 0; j < names.Length; j++)
                if (string.Equals(value, names[j], StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
