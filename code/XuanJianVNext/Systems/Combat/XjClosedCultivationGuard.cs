using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 闭关无敌 Guard
/// 语义：金丹修士在宗门闭关修炼时免疫一切外来伤害
/// 对应 0.5.4 XuanJianZongMenCultivationSystem.BlockClosedCultivationDamage
/// 
/// 约束：仅对金丹及以上境界的修士生效
/// 标记由 ZongMen 系统在进入/离开闭关时调用
/// 
/// 玄门化原则：
/// - O(1) 数据键读取
/// - 纯函数，无副作用
/// - 无外部持久化依赖
/// </summary>
internal static class XjClosedCultivationGuard
{
    internal static bool HasActive => ActiveActorIds.Count > 0;
    // 闭关状态键：存储是否处于宗门闭关修炼状态
    private const string ClosedCultivationKey = "xuanjian.vnext.closed_cultivation";
    private const string RuntimeAppliedKey = "xuanjian.vnext.closed_cultivation.runtime_applied";
    private const string TempPeacefulKey = "xuanjian.vnext.closed_cultivation.temp_peaceful";
    private const float FloatYOffset = 12f;
    private const float VisualStatusSeconds = 3f;

    private static readonly HashSet<long> ActiveActorIds = new HashSet<long>();
    private static readonly Queue<long> ActiveActorQueue = new Queue<long>();
    private static readonly FieldInfo PositionsField = ResolveRenderDataField("positions");
    private static readonly FieldInfo ShadowPositionsField = ResolveRenderDataField("shadow_positions");
    private static readonly FieldInfo ItemPositionsField = ResolveRenderDataField("item_positions");
    private static readonly FieldInfo ItemPositionField = ResolveRenderDataField("item_position");

    /// <summary>
    /// 将 Actor 标记为闭关修炼状态（仅金丹可被标记）
    /// 调用方：ZongMen 系统在金丹修士进入/离开宗门闭关时调用
    /// </summary>
    internal static void MarkClosedCultivation(Actor actor, bool inCultivation)
    {
        if (actor?.data == null) return;
        try
        {
            if (inCultivation && IsShenDan(actor))
            {
                return;
            }

            // 防御性检查：仅金丹及以上可被标记
            if (inCultivation && XjRealmSuppression.GetRealmTier(actor) < 5)
                return;

            BaseSystemData data = (BaseSystemData)actor.data;
            data.set(ClosedCultivationKey, inCultivation);
            if (inCultivation)
            {
                AddActiveActor(actor);
                ApplyRuntimeState(actor);
                return;
            }

            RestoreRuntimeState(actor);
            ActiveActorIds.Remove(data.id);
        }
        catch { }
    }

    internal static void TrackIfClosedCultivation(Actor actor)
    {
        if (actor?.data == null || !IsInClosedCultivation(actor))
        {
            return;
        }

        if (IsShenDan(actor))
        {
            MarkClosedCultivation(actor, false);
            return;
        }

        // 读档时不能只相信旧布尔标记。只有权威闭关状态仍成立时才恢复运行时无敌；
        // 否则立即清除，避免旧档/异常中断留下永久 invincible 与“完全打不动”。
        if (!HasAuthoritativeRetreatState(actor))
        {
            MarkClosedCultivation(actor, false);
            return;
        }

        AddActiveActor(actor);
    }

    internal static void TickRuntime(int budget, Func<long, Actor> resolveActor)
    {
        if (budget <= 0 || resolveActor == null || ActiveActorIds.Count == 0)
        {
            return;
        }

        int processed = 0;
        while (processed < budget && ActiveActorQueue.Count > 0)
        {
            long actorId = ActiveActorQueue.Dequeue();
            processed++;
            if (!ActiveActorIds.Contains(actorId))
            {
                continue;
            }

            Actor actor = resolveActor(actorId);
            if (actor?.data == null || !actor.isAlive())
            {
                ActiveActorIds.Remove(actorId);
                continue;
            }

            if (!IsInClosedCultivation(actor)
                || !HasAuthoritativeRetreatState(actor))
            {
                MarkClosedCultivation(actor, false);
                ActiveActorIds.Remove(actorId);
                continue;
            }

            ApplyRuntimeState(actor);
            ActiveActorQueue.Enqueue(actorId);
        }
    }

    internal static void Clear()
    {
        ActiveActorIds.Clear();
        ActiveActorQueue.Clear();
        XjZongMenDongTianLifecycle.ClearRuntimeCache();
        XjLongShuDongTianSystem.Clear();
    }

    /// <summary>
    /// 统一可见角色渲染通道调用。调用方已经完成 visible_units 的单次遍历，
    /// 此处只对命中的闭关角色修改对应渲染槽，避免本系统再次扫描全体可见角色。
    /// </summary>
    internal static void ApplyRenderOffsetCandidate(Actor actor, ActorRenderData renderData, int index)
    {
        if (ActiveActorIds.Count == 0)
        {
            return;
        }

        ApplyRenderOffset(actor, renderData, index);
    }

    /// <summary>
    /// 判断 Actor 是否正在闭关修炼（纯数据查询，不做境界校验）
    /// </summary>
    internal static bool IsInClosedCultivation(Actor actor)
    {
        if (actor?.data == null) return false;
        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.get(ClosedCultivationKey, out bool inCultivation, false);
            return inCultivation;
        }
        catch { return false; }
    }

    /// <summary>
    /// 检查并阻止闭关修士受到的伤害
    /// 仅当目标为金丹修士且处于宗门闭关状态时生效
    /// 返回值：true = 伤害已被屏蔽（调用方应跳过原方法），false = 正常继续
    /// </summary>
    internal static bool BlockIfClosedCultivation(ref float damage, Actor defender)
	{
		return BlockIfClosedCultivation(ref damage, defender, XjRealmSuppression.GetRealmTier(defender));
	}

	internal static bool BlockIfClosedCultivation(ref float damage, Actor defender, int defenderTier)
    {
        if (damage <= 0f || defender == null)
            return false;

        // 仅金丹修士可享受闭关无敌
		if (defenderTier < 5)
            return false;

        if (!IsInClosedCultivation(defender))
            return false;

        if (IsShenDan(defender))
        {
            MarkClosedCultivation(defender, false);
            return false;
        }

        // 每次伤害入口做 O(1) 权威状态校验。闭关标记、果位合道状态或洞天建筑
        // 任一失效时都不得继续享受无敌，并同步清理旧 invincible 状态。
        if (!HasAuthoritativeRetreatState(defender))
        {
            MarkClosedCultivation(defender, false);
            return false;
        }

        damage = 0f;
        return true;
    }

    private static void ApplyRuntimeState(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive())
        {
            return;
        }

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            if (!TryResolveAnyRetreatBuilding(actor, out Building targetBuilding)
                || targetBuilding?.current_tile == null)
            {
                // 没有可用洞天建筑时不能制造“原地永久无敌”。仅撤销本系统施加的
                // 运行时保护，不中断角色正常行为；权威年度流程可在建筑恢复后再次进入。
                ClearRuntimeProtectionOnly(actor);
                return;
            }

            if (actor.is_inside_building && actor.inside_building != targetBuilding)
            {
                actor.exitBuilding();
            }

            if (actor.is_inside_boat)
            {
                if (actor.inside_boat != null)
                {
                    actor.inside_boat.removePassenger(actor);
                }
                actor.exitBoat();
            }

            if (!actor.is_inside_building || actor.inside_building != targetBuilding)
            {
                actor.cancelAllBeh();
                actor.stopMovement();
                actor.spawnOn(targetBuilding.current_tile);
                actor.setTileTarget(targetBuilding.current_tile);
                actor.stayInBuilding(targetBuilding);
            }

            if (!actor.hasTrait("peaceful"))
            {
                actor.addTrait("peaceful", false);
                data.set(TempPeacefulKey, true);
            }

            data.set(RuntimeAppliedKey, true);
            ((BaseSimObject)actor).addStatusEffect("invincible", VisualStatusSeconds, true);
            actor.attackedBy = null;
            actor.stayInBuilding(targetBuilding);
            actor.stopMovement();
            EnsureCultivationSleepState(actor);
        }
        catch { }
    }

    private static bool HasAuthoritativeRetreatState(Actor actor)
    {
        if (actor?.data == null
            || IsShenDan(actor)
            || XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierJinDan)
        {
            return false;
        }

        if (XjQuanBingStruggleSystem.IsActive
            && XjQuanBingStruggleSystem.IsWithdrawnParticipant(actor))
        {
            return true;
        }

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenDongTianState, out string state)
            && string.Equals(state, "Retreat", StringComparison.Ordinal))
        {
            return true;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L
            && XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState authorityState)
            && (authorityState.IntegrationRetreatActive
                || !string.IsNullOrWhiteSpace(authorityState.WithdrawnToDongTian)))
        {
            return true;
        }

        return false;
    }

    private static bool IsShenDan(Actor actor)
    {
        return actor?.data != null
            && XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
            && string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
    }

    private static void ClearRuntimeProtectionOnly(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.set(RuntimeAppliedKey, false);
            data.get(TempPeacefulKey, out bool tempPeaceful, false);
            data.set(TempPeacefulKey, false);
            if (tempPeaceful && actor.hasTrait("peaceful"))
            {
                actor.removeTrait("peaceful");
            }
            ((BaseSimObject)actor).finishStatusEffect("invincible");
        }
        catch
        {
        }
    }

    private static bool TryResolveAnyRetreatBuilding(Actor actor, out Building building)
    {
        if (XjLongShuSystem.IsLongShu(actor))
        {
            return XjLongShuDongTianSystem.TryResolveRetreatBuilding(actor, out building);
        }
        return XjZongMenDongTianLifecycle.TryResolveRetreatBuilding(actor, out building);
    }

    private static void RestoreRuntimeState(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        try
        {
            BaseSystemData data = (BaseSystemData)actor.data;
            data.set(RuntimeAppliedKey, false);
            data.get(TempPeacefulKey, out bool tempPeaceful, false);
            data.set(TempPeacefulKey, false);
            if (tempPeaceful && actor.hasTrait("peaceful"))
            {
                actor.removeTrait("peaceful");
            }

            XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenDongTianTargetBuildingId, out long targetBuildingId);
            if (targetBuildingId > 0L
                && actor.is_inside_building
                && actor.inside_building?.data != null
                && ((BaseSystemData)actor.inside_building.data).id == targetBuildingId)
            {
                actor.exitBuilding();
            }

            actor.timer_action = 0f;
            ((BaseSimObject)actor).finishStatusEffect("invincible");
            actor.cancelAllBeh();
        }
        catch { }
    }

    private static void EnsureCultivationSleepState(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenDongTianRetreatEndYear, out int endYear);
        float remainSeconds = 1f;
        if (World.world != null && endYear > 0)
        {
            double endWorldTime = Math.Max(0.0, (endYear - 1) * 60.0);
            remainSeconds = Mathf.Max(1f, (float)Math.Max(0.0, endWorldTime - World.world.getCurWorldTime()));
        }

        actor.makeSleep(remainSeconds);
        actor.timer_action = remainSeconds;
    }

    private static void ApplyRenderOffset(Actor actor, ActorRenderData renderData, int index)
    {
        if (actor?.data == null || renderData == null || index < 0)
        {
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L
            || !ActiveActorIds.Contains(actorId)
            || !IsInClosedCultivation(actor)
            || actor.is_inside_building)
        {
            return;
        }

        ApplyVector3ArrayYOffset(renderData, index, PositionsField, FloatYOffset);
        ApplyVector3ArrayYOffset(renderData, index, ShadowPositionsField, FloatYOffset);
        ApplyVector3ArrayYOffset(renderData, index, ItemPositionsField, FloatYOffset);
        ApplyVector3ArrayYOffset(renderData, index, ItemPositionField, FloatYOffset);
    }

    private static void ApplyVector3ArrayYOffset(ActorRenderData renderData, int index, FieldInfo field, float offset)
    {
        try
        {
            if (field == null || field.GetValue(renderData) is not Vector3[] values || index >= values.Length)
            {
                return;
            }

            Vector3 value = values[index];
            values[index] = new Vector3(value.x, value.y + offset, value.z);
        }
        catch
        {
        }
    }

    private static FieldInfo ResolveRenderDataField(string fieldName)
    {
        return typeof(ActorRenderData).GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static void AddActiveActor(Actor actor)
    {
        if (actor?.data == null)
        {
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L)
        {
            if (ActiveActorIds.Add(actorId))
            {
                ActiveActorQueue.Enqueue(actorId);
            }
        }
    }
}
