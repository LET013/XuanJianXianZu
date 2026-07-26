using System;
using System.Collections.Generic;
using System.Reflection;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Cached bridge to the native attack target API. Reflection is resolved once
/// per runtime actor type instead of once per hostility tick.
/// </summary>
internal static class XjActorAggroBridge
{
	private static readonly string[] TargetMemberNames =
	{
		"attack_target",
		"attackTarget",
		"target_attack",
		"target"
	};

	private sealed class Binding
	{
		internal readonly FieldInfo[] Fields;
		internal readonly PropertyInfo[] Properties;
		internal readonly MethodInfo[] AttackMethods;

		internal Binding(FieldInfo[] fields, PropertyInfo[] properties, MethodInfo[] attackMethods)
		{
			Fields = fields;
			Properties = properties;
			AttackMethods = attackMethods;
		}
	}

	private static readonly Dictionary<Type, Binding> Bindings = new Dictionary<Type, Binding>();

	internal static void ForceAggro(Actor actor, Actor target)
	{
		if (actor?.data == null || target?.data == null)
		{
			return;
		}

		Binding binding = GetBinding(actor.GetType());
		SetTargetMembers(binding, actor, target);
		TryInvokeAttack(binding, actor, target);
	}

	internal static void ClearTargets(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try { actor.attackedBy = null; } catch { }
		try { actor.stopMovement(); } catch { }

		Binding binding = GetBinding(actor.GetType());
		SetTargetMembers(binding, actor, null);
	}

	internal static void Clear()
	{
		Bindings.Clear();
	}

	private static Binding GetBinding(Type actorType)
	{
		if (Bindings.TryGetValue(actorType, out Binding binding))
		{
			return binding;
		}

		List<FieldInfo> fields = new List<FieldInfo>(TargetMemberNames.Length);
		List<PropertyInfo> properties = new List<PropertyInfo>(TargetMemberNames.Length);
		for (int i = 0; i < TargetMemberNames.Length; i++)
		{
			string name = TargetMemberNames[i];
			FieldInfo field = actorType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (field != null && field.FieldType.IsAssignableFrom(typeof(Actor)))
			{
				fields.Add(field);
			}

			PropertyInfo property = actorType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(typeof(Actor)))
			{
				properties.Add(property);
			}
		}

		List<MethodInfo> methods = new List<MethodInfo>();
		MethodInfo[] candidates = actorType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		for (int i = 0; i < candidates.Length; i++)
		{
			MethodInfo method = candidates[i];
			if ((method.Name.Contains("tryToAttack", StringComparison.OrdinalIgnoreCase)
					|| method.Name.Contains("setAttack", StringComparison.OrdinalIgnoreCase))
				&& method.GetParameters().Length is > 0 and <= 4)
			{
				methods.Add(method);
			}
		}

		binding = new Binding(fields.ToArray(), properties.ToArray(), methods.ToArray());
		Bindings[actorType] = binding;
		return binding;
	}

	private static void SetTargetMembers(Binding binding, Actor actor, Actor target)
	{
		for (int i = 0; i < binding.Fields.Length; i++)
		{
			try { binding.Fields[i].SetValue(actor, target); } catch { }
		}
		for (int i = 0; i < binding.Properties.Length; i++)
		{
			try { binding.Properties[i].SetValue(actor, target, null); } catch { }
		}
	}

	private static void TryInvokeAttack(Binding binding, Actor actor, Actor target)
	{
		WorldTile targetTile;
		try { targetTile = ((BaseSimObject)target).current_tile; }
		catch { targetTile = null; }

		for (int i = 0; i < binding.AttackMethods.Length; i++)
		{
			MethodInfo method = binding.AttackMethods[i];
			if (!TryBuildArgs(method.GetParameters(), target, targetTile, out object[] args))
			{
				continue;
			}
			try
			{
				method.Invoke(actor, args);
				return;
			}
			catch { }
		}
	}

	private static bool TryBuildArgs(ParameterInfo[] parameters, Actor target, WorldTile targetTile, out object[] args)
	{
		args = new object[parameters.Length];
		for (int i = 0; i < parameters.Length; i++)
		{
			Type type = parameters[i].ParameterType;
			if (type.IsAssignableFrom(typeof(Actor))) args[i] = target;
			else if (type == typeof(WorldTile))
			{
				if (targetTile == null) return false;
				args[i] = targetTile;
			}
			else if (type == typeof(bool)) args[i] = true;
			else if (type == typeof(int)) args[i] = 0;
			else if (type == typeof(float)) args[i] = 0f;
			else if (!type.IsValueType) args[i] = null;
			else return false;
		}
		return true;
	}
}
