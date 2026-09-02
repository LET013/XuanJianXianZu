using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 扎克体系的观察者通过 Harmony 拦截伤害、死亡和单位删除，并在攻击前直接调用
/// ErasePower。道胎接触该生物时不进入数值对冲：解除观察者锚定后交回原生死亡链。
/// 此处没有世界扫描；只在双方真实发生受击接触时执行。
/// </summary>
internal static class XjZackObserverErasure
{
    private const string ObserverTraitId = "zack_first_mod";
    private const string ObserverTypeName = "ZhaKe.ObserverPatches";

    private static Type _observerType;
    private static FieldInfo _observerIdsField;

    internal static bool TryResolveDaoTaiContact(Actor defender, BaseSimObject attackerObject)
    {
        Actor attacker = null;
        try { if (attackerObject != null && attackerObject.isActor()) attacker = attackerObject.a; }
        catch { }

        if (XjDaoTaiSpellScale.IsDaoTaiActor(defender) && IsObserver(attacker))
        {
            EraseObserver(attacker);
            return true;
        }

        if (XjDaoTaiSpellScale.IsDaoTaiActor(attacker) && IsObserver(defender))
        {
            EraseObserver(defender);
            return true;
        }

        return false;
    }

    internal static bool IsDaoTaiProtectedFromExternalErase(Actor actor) => XjDaoTaiSpellScale.IsDaoTaiActor(actor);

    private static bool IsObserver(Actor actor)
    {
        if (actor == null) return false;
        try
        {
            if (actor.hasTrait(ObserverTraitId)) return true;
            object ids = GetObserverIds();
            MethodInfo contains = ids == null ? null : ids.GetType().GetMethod("Contains", new[] { typeof(long) });
            object result = contains?.Invoke(ids, new object[] { actor.id });
            return result is bool hasObserver && hasObserver;
        }
        catch
        {
            return false;
        }
    }

    private static void EraseObserver(Actor actor)
    {
        if (actor == null) return;
        try
        {
            // 先拔除第三方的永久 ID 锚定，再直删特质。之后其全部保护前缀会把
            // 该单位当作普通生物，原生 dieAndDestroy/ActorManager 清理即可完整完成。
            object ids = GetObserverIds();
            MethodInfo remove = ids == null ? null : ids.GetType().GetMethod("Remove", new[] { typeof(long) });
            remove?.Invoke(ids, new object[] { actor.id });

            if (actor.traits != null)
            {
                List<ActorTrait> toRemove = new List<ActorTrait>();
                foreach (ActorTrait trait in actor.traits)
                {
                    if (string.Equals(trait?.id, ObserverTraitId, StringComparison.Ordinal)) toRemove.Add(trait);
                }
                for (int i = 0; i < toRemove.Count; i++) actor.traits.Remove(toRemove[i]);
            }

            if (actor.data != null) actor.data.health = 0;
            actor.dieAndDestroy(AttackType.Divine);
            if (actor.isAlive()) World.world?.units?.destroyObject(actor);
        }
        catch (Exception ex)
        {
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("ZackObserver.DaoTaiErasure", ex);
        }
    }

    private static object GetObserverIds()
    {
        try
        {
            _observerType ??= HarmonyLib.AccessTools.TypeByName(ObserverTypeName);
            _observerIdsField ??= _observerType?.GetField("ObserverIds", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            return _observerIdsField?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }
}
