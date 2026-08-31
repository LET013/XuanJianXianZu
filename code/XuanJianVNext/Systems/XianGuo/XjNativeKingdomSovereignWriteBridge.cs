using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// 帝明阳使用的原生王位写窄桥。
///
/// 首次人工登基来自“帝统明阳”模拟器；帝明阳成位后若原生/第三方政治逻辑
/// 扰动王位，后台兼容投影也只复用完整的 Kingdom.setKing 事务。
/// 禁止反射写 kingID、禁止在王位写桥里直接改城镇/居民/寻路归属。
/// </summary>
internal static class XjNativeKingdomSovereignWriteBridge
{
    internal static bool TrySetExistingKingdomSovereign(Actor actor, out string reason)
    {
        reason = string.Empty;
        if (actor?.data == null || !actor.isAlive())
        {
            reason = "角色无效";
            return false;
        }

        Kingdom kingdom = actor.kingdom;
        if (kingdom?.data == null)
        {
            reason = "角色当前没有所属国家";
            return false;
        }

        try
        {
            if (!kingdom.isCiv())
            {
                reason = "角色不属于文明国家";
                return false;
            }

            if (XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor current)
                && SameActor(current, actor))
            {
                return true;
            }

            // 复用原生 KingdomBehCheckKing.makeKingAndMoveToCapital 的交接顺序：
            // 先解除城主/武士职务，再把新王纳入既有都城，最后才写王位。此前只做
            // setKing 而没有入都，继承人所在城会留下一个“城主离任、国王未入都”
            // 的中间态；多城国家会把它误判为王统失序，进而走向分裂检查。
            if (actor.hasCity())
            {
                actor.stopBeingWarrior();
                if (actor.isCityLeader() && actor.city?.data != null)
                {
                    actor.city.removeLeader();
                }
            }
            if (kingdom.hasCapital() && actor.city != kingdom.capital)
            {
                actor.joinCity(kingdom.capital);
            }

            Actor previous = kingdom.king;
            if (previous != null && !SameActor(previous, actor))
            {
                previous.setProfession(UnitProfession.Unit);
            }

            kingdom.setKing(actor);
            actor.startShake();
            actor.startColorEffect();

            return XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor resolved)
                && SameActor(resolved, actor);
        }
        catch (Exception ex)
        {
            reason = ex.GetType().Name;
            XjExceptionDiagnostics.Report("XjNativeKingdomSovereignWriteBridge.TrySetExistingKingdomSovereign", ex);
            return false;
        }
    }

    private static bool SameActor(Actor left, Actor right)
    {
        if (ReferenceEquals(left, right)) return true;
        if (left?.data is not BaseSystemData leftData || right?.data is not BaseSystemData rightData) return false;
        return leftData.id > 0L && leftData.id == rightData.id;
    }
}
