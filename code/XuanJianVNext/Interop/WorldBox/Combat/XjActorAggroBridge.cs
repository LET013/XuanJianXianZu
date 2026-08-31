using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 原生索敌字段的窄桥接层。只写 attack_target 的明确别名与 has_attack_target，
/// 不再匹配泛化的 target 字段，也不再反射调用未知 tryToAttack/setAttack 重载。
/// </summary>
internal static class XjActorAggroBridge
{
	private static readonly string[] TargetMemberNames =
	{
		"attack_target",
		"attackTarget",
		"target_attack"
	};

	private static readonly string[] HasTargetMemberNames =
	{
		"has_attack_target",
		"hasAttackTarget"
	};

	private sealed class Binding
	{
		internal readonly FieldInfo[] TargetFields;
		internal readonly PropertyInfo[] TargetProperties;
		internal readonly FieldInfo[] HasTargetFields;
		internal readonly PropertyInfo[] HasTargetProperties;

		internal Binding(
			FieldInfo[] targetFields,
			PropertyInfo[] targetProperties,
			FieldInfo[] hasTargetFields,
			PropertyInfo[] hasTargetProperties)
		{
			TargetFields = targetFields;
			TargetProperties = targetProperties;
			HasTargetFields = hasTargetFields;
			HasTargetProperties = hasTargetProperties;
		}
	}

	private static readonly Dictionary<Type, Binding> Bindings = new Dictionary<Type, Binding>();

	internal static void ForceAggro(Actor actor, Actor target)
	{
		if (!XjSafeCore.IsAliveActor(actor) || !IsValidTarget(actor, target)) return;
		Binding binding = GetBinding(actor.GetType());
		SetTargetMembers(binding, actor, target);
		SetHasTargetMembers(binding, actor, true);
		try { actor._timeout_targets = 0f; }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.ResetTargetTimeout", ex); }
	}

	internal static void ClearTargets(Actor actor, bool stopMovement = true, bool clearRetaliation = true)
	{
		if (actor == null) return;
		if (clearRetaliation)
		{
			try { actor.attackedBy = null; }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.ClearRetaliation", ex); }
		}
		if (stopMovement)
		{
			try { actor.stopMovement(); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.StopMovement", ex); }
		}

		Binding binding = GetBinding(actor.GetType());
		SetTargetMembers(binding, actor, null);
		SetHasTargetMembers(binding, actor, false);
	}

	/// <summary>
	/// 在进入原生 b2_checkCurrentEnemyTarget 前清理已死亡、已移除或无地图格的目标。
	/// 返回 false 表示本帧应跳过原生检查，避免 BatchActors 被同一空引用持续击穿。
	/// </summary>
	internal static bool NormalizeCurrentTargetForNativeCheck(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || actor.asset == null) return false;
		try
		{
			if (((BaseSimObject)actor).current_tile == null) return false;
			BaseSimObject target = actor.attack_target;
			if (target == null)
			{
				if (actor.has_attack_target)
				{
					ClearTargets(actor, stopMovement: false, clearRetaliation: false);
					return false;
				}
				return true;
			}
			if (IsValidTarget(actor, target)) return true;
			ClearTargets(actor, stopMovement: false, clearRetaliation: false);
			return false;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Aggro.NormalizeCurrentTarget", ex);
			ClearTargets(actor, stopMovement: false, clearRetaliation: false);
			return false;
		}
	}

	internal static bool HasValidCurrentTarget(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return false;
		try
		{
			BaseSimObject target = actor.attack_target;
			return target != null && IsValidTarget(actor, target);
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsValidTarget(Actor owner, BaseSimObject target)
	{
		if (!XjSafeCore.IsAliveActor(owner) || target == null || ReferenceEquals(owner, target)) return false;
		try
		{
			if (!target.isAlive() || target.current_tile == null) return false;
			if (target.isActor())
			{
				Actor targetActor = target.a;
				if (targetActor == null || targetActor == owner || !XjSafeCore.IsAliveActor(targetActor)) return false;
				return !XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(owner, targetActor);
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	internal static void Clear()
	{
		Bindings.Clear();
	}

	private static Binding GetBinding(Type actorType)
	{
		if (Bindings.TryGetValue(actorType, out Binding binding)) return binding;
		List<FieldInfo> targetFields = new List<FieldInfo>(TargetMemberNames.Length);
		List<PropertyInfo> targetProperties = new List<PropertyInfo>(TargetMemberNames.Length);
		for (int i = 0; i < TargetMemberNames.Length; i++)
		{
			string name = TargetMemberNames[i];
			FieldInfo field = actorType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType.IsAssignableFrom(typeof(Actor))) targetFields.Add(field);
			PropertyInfo property = actorType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(typeof(Actor))) targetProperties.Add(property);
		}

		List<FieldInfo> hasTargetFields = new List<FieldInfo>(HasTargetMemberNames.Length);
		List<PropertyInfo> hasTargetProperties = new List<PropertyInfo>(HasTargetMemberNames.Length);
		for (int i = 0; i < HasTargetMemberNames.Length; i++)
		{
			string name = HasTargetMemberNames[i];
			FieldInfo field = actorType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType == typeof(bool)) hasTargetFields.Add(field);
			PropertyInfo property = actorType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite && property.PropertyType == typeof(bool)) hasTargetProperties.Add(property);
		}

		binding = new Binding(targetFields.ToArray(), targetProperties.ToArray(), hasTargetFields.ToArray(), hasTargetProperties.ToArray());
		Bindings[actorType] = binding;
		return binding;
	}

	private static void SetTargetMembers(Binding binding, Actor actor, BaseSimObject target)
	{
		for (int i = 0; i < binding.TargetFields.Length; i++)
		{
			try { binding.TargetFields[i].SetValue(actor, target); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.SetTargetField", ex); }
		}
		for (int i = 0; i < binding.TargetProperties.Length; i++)
		{
			try { binding.TargetProperties[i].SetValue(actor, target, null); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.SetTargetProperty", ex); }
		}
	}

	private static void SetHasTargetMembers(Binding binding, Actor actor, bool value)
	{
		for (int i = 0; i < binding.HasTargetFields.Length; i++)
		{
			try { binding.HasTargetFields[i].SetValue(actor, value); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.SetHasTargetField", ex); }
		}
		for (int i = 0; i < binding.HasTargetProperties.Length; i++)
		{
			try { binding.HasTargetProperties[i].SetValue(actor, value, null); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("Aggro.SetHasTargetProperty", ex); }
		}
	}
}
