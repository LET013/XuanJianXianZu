using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 太阴金丹的基础“藏”性，以及渊照从太阴一侧空证继承的“藏真”机制：
/// 自己没有主动攻击目标时，其他角色不能主动把它作为攻击对象；一旦本人持有
/// 有效攻击目标，保护即时解除。只屏蔽角色造成的主动战斗伤害，饥饿、火焰等
/// 环境规则仍走原有结算。
/// </summary>
internal static class XjTaiYinNonAggressionPolicy
{
	internal static bool IsPassivelyProtected(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || !XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)) return false;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (!string.Equals(normalizedDaoTu, "太阴", StringComparison.Ordinal)
			&& !string.Equals(normalizedDaoTu, "渊照", StringComparison.Ordinal)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string realm = XjRealmHelper.NormalizeId(realmId);
		if (!string.Equals(realm, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(realm, XjRealmIds.DaoTai, StringComparison.Ordinal)) return false;

		return !HasActiveAttackTarget(actor);
	}

	internal static bool ShouldBlockIncomingAttack(Actor attacker, Actor defender)
	{
		if (attacker?.data == null || defender?.data == null || attacker == defender
			|| !IsPassivelyProtected(defender))
		{
			return false;
		}

		// “藏”只能约束同层与下层对手，不能把金丹太阴变成连道胎都无法触及的
		// 绝对无敌单位。道胎对金丹/真君存在完整大境界压制时可直接破藏；
		// 太阴自身若已是道胎，则仍保留同级非主动不可攻的原语义。
		if (XjDaoTaiSpellScale.IsDaoTaiActor(attacker)
			&& !XjDaoTaiSpellScale.IsDaoTaiActor(defender))
		{
			return false;
		}

		return true;
	}

	internal static void SuppressPassiveRetaliation(Actor actor)
	{
		if (!IsPassivelyProtected(actor)) return;
		try { actor.attackedBy = null; }
		catch (Exception ex) { XjExceptionDiagnostics.Report("TaiYin.NonAggression.ClearRetaliation", ex); }
	}

	private static bool HasActiveAttackTarget(Actor actor)
	{
		try
		{
			if (!actor.has_attack_target || actor.attack_target == null) return false;
			BaseSimObject target = actor.attack_target;
			return target != null && target.isAlive() && target.current_tile != null;
		}
		catch
		{
			return false;
		}
	}
}
