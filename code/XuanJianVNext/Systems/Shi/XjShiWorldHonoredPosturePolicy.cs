using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 【世尊之姿】统一政策：
/// - 只有角色已真实进入释修体系时，才把人物自身的随机修证判定视作通过；
/// - 不绕过金地/三十二天/世数/宏愿/修持/命数/唯一世尊位等结构前置；
/// - 全世界最多两名在世持有者。
///
/// 性能边界：常态只做 hasTrait O(1)；限额只在该特质真实 add/remove 时维护 ID 集。
/// 旧档/读档后的第一次新增会懒加载一次已知角色快照，此后不做年度/逐帧扫描，
/// 也不跨帧持有 Actor 引用。
/// </summary>
internal static class XjShiWorldHonoredPosturePolicy
{
    internal const string TraitId = "XjShiWorldHonoredPosture";
    internal const int MaximumLivingHolders = 2;

    private static readonly HashSet<long> HolderIds = new HashSet<long>();
    private static readonly List<long> StaleIds = new List<long>(MaximumLivingHolders);
    private static bool _indexed;

    internal static bool IsGuaranteedWorldHonored(Actor actor)
    {
        return actor?.data != null
            && XjCultivationPathRules.IsShi(actor)
            && HasTraitSafe(actor);
    }

    internal static bool ShouldAllowGrant(Actor actor, string traitId)
    {
        if (!string.Equals(traitId, TraitId, StringComparison.Ordinal)) return true;
        if (actor?.data == null) return false;
        if (HasTraitSafe(actor)) return true;

        EnsureIndexedOnce();
        PruneTrackedHolders();
        if (HolderIds.Count < MaximumLivingHolders) return true;

        Debug.Log("[玄鉴][世尊之姿] 在世持有者已达上限(" + MaximumLivingHolders + "人)，阻止添加");
        return false;
    }

    internal static void ObserveGrant(Actor actor, string traitId, bool presentAfterGrant)
    {
        if (!string.Equals(traitId, TraitId, StringComparison.Ordinal)
            || !presentAfterGrant || actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L) HolderIds.Add(actorId);
    }

    internal static void ObserveRemoval(Actor actor, string traitId, bool removed)
    {
        if (!removed || !string.Equals(traitId, TraitId, StringComparison.Ordinal) || actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L) HolderIds.Remove(actorId);
    }

    internal static void ClearRuntime()
    {
        HolderIds.Clear();
        StaleIds.Clear();
        _indexed = false;
    }

    private static void EnsureIndexedOnce()
    {
        if (_indexed) return;
        _indexed = true;
        HolderIds.Clear();

        IReadOnlyList<Actor> actors = XjScheduler.GetKnownActorsSnapshot();
        for (int i = 0; i < actors.Count; i++)
        {
            Actor actor = actors[i];
            if (!XjSafeCore.IsAliveActor(actor) || !HasTraitSafe(actor)) continue;
            long actorId = ((BaseSystemData)actor.data).id;
            if (actorId > 0L) HolderIds.Add(actorId);
        }
    }

    private static void PruneTrackedHolders()
    {
        if (HolderIds.Count == 0) return;
        StaleIds.Clear();
        foreach (long actorId in HolderIds)
        {
            if (actorId <= 0L
                || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
                || !XjSafeCore.IsAliveActor(actor)
                || !HasTraitSafe(actor))
            {
                StaleIds.Add(actorId);
            }
        }
        for (int i = 0; i < StaleIds.Count; i++) HolderIds.Remove(StaleIds[i]);
        StaleIds.Clear();
    }

    private static bool HasTraitSafe(Actor actor)
    {
        if (actor?.data == null) return false;
        try { return actor.hasTrait(TraitId); }
        catch { return false; }
    }
}
