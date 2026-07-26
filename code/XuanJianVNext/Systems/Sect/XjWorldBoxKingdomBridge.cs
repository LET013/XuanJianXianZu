using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Central boundary for WorldBox kingdom and city ownership mutations.
/// XuanJian data may mirror ownership, but native city transfers must pass
/// through WorldBox methods so formation gates, borders and engine caches stay valid.
/// </summary>
internal static class XjWorldBoxKingdomBridge
{
	private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	// High-realm suppression is an explicit resolution, not a normal siege. Keep
	// this scope narrow so operational sect formations still gate ordinary wars.
	[ThreadStatic]
	private static int forcedConquestDepth;
	// 城镇居民归属修复只在首次读档核验、或城镇切换所属政体后才有意义。
	// 不能在每次宗门领土对账时重新枚举整座城的所有居民。
	private static readonly Dictionary<long, long> VerifiedCityKingdomIds = new Dictionary<long, long>();
	private static readonly HashSet<long> CityRepairSeenActorIds = new HashSet<long>();
	private static readonly List<Actor> CityRepairActors = new List<Actor>();
	private static object verifiedOwnershipWorld;

	internal static bool IsForcedConquest => forcedConquestDepth > 0;

	internal static bool TryRenameKingdom(Kingdom kingdom, string nextName, out string reason)
	{
		reason = string.Empty;
		if (kingdom?.data == null)
		{
			reason = "王国不存在";
			return false;
		}

		string normalized = (nextName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			reason = "王国名称为空";
			return false;
		}

		try
		{
			kingdom.name = normalized;
			kingdom.data.name = normalized;
			return true;
		}
		catch (Exception ex)
		{
			reason = ex.GetType().Name;
			return false;
		}
	}

	internal static bool TryRestoreKingdomName(Kingdom kingdom, string previousName)
	{
		return TryRenameKingdom(kingdom, previousName, out _);
	}

	internal static bool TryTransferCity(City city, Kingdom targetKingdom, string operationName, out string reason, bool bypassFormation = false)
	{
		reason = string.Empty;
		if (city?.data == null)
		{
			reason = "城镇不存在";
			return false;
		}
		if (targetKingdom?.data == null)
		{
			reason = "目标政体不存在";
			return false;
		}
		if (IsCityInKingdom(city, targetKingdom))
		{
			TryRepairCityPopulationOwnership(city);
			return true;
		}

		Kingdom previousKingdom = city.kingdom;
		string label = string.IsNullOrWhiteSpace(operationName) ? "转归属" : operationName.Trim();
		if (!TryInvokeCityTransfer(city, targetKingdom, true, bypassFormation, out reason))
		{
			reason = label + "未找到可用的 WorldBox 城镇转归属 API：" + reason;
			return false;
		}
		if (IsCityInKingdom(city, targetKingdom))
		{
			TryRepairCityPopulationOwnership(city, clearNativeArmies: true);
			XjNativeArmySaveGuard.EnqueueTouchedCity(city);
			return true;
		}

		TryRollbackCity(city, previousKingdom, bypassFormation);
		reason = label + "被原生逻辑或护宗大阵拒绝";
		return false;
	}

	/// <summary>
	/// Repairs cities converted by older builds that used the load-only ownership setter.
	/// The city flag was changed but residents remained attached to their former kingdom,
	/// which made the new sect state report a population of zero.
	/// </summary>
	internal static bool TryRepairCityPopulationOwnership(City city) => TryRepairCityPopulationOwnership(city, clearNativeArmies: false);

	internal static bool TryRepairCityPopulationOwnership(City city, bool clearNativeArmies)
	{
		Kingdom kingdom = city?.kingdom;
		if (city?.data == null || kingdom?.data == null || city.units == null)
		{
			return false;
		}
		EnsureOwnershipCacheWorld();
		long cityId = city.data.id;
		long kingdomId = kingdom.data.id;
		if (cityId > 0L
			&& kingdomId > 0L
			&& VerifiedCityKingdomIds.TryGetValue(cityId, out long verifiedKingdomId)
			&& verifiedKingdomId == kingdomId
			&& !clearNativeArmies)
		{
			return false;
		}

		BuildCityRepairActorSnapshot(city);
		bool needsKingdomRepair = false;
		bool needsArmyClear = false;
		for (int i = 0; i < CityRepairActors.Count; i++)
		{
			Actor actor = CityRepairActors[i];
			if (actor?.data != null && !actor.isRekt() && !ReferenceEquals(actor.kingdom, kingdom))
			{
				needsKingdomRepair = true;
			}
			if (clearNativeArmies && HasNativeArmy(actor))
			{
				needsArmyClear = true;
			}
			if (needsKingdomRepair && needsArmyClear)
			{
				break;
			}
		}
		if (!needsKingdomRepair && !needsArmyClear)
		{
			CityRepairActors.Clear();
			RememberVerifiedCityOwnership(cityId, kingdomId);
			return false;
		}

		try
		{
			for (int i = 0; i < CityRepairActors.Count; i++)
			{
				TryRepairActorNativeOwnership(CityRepairActors[i], city, kingdom, clearNativeArmies);
			}
			CityRepairActors.Clear();

			if (needsKingdomRepair)
			{
				city.forceBuildingsToKingdom(city.buildings, kingdom);
				city.switchedKingdom();
			}
			RememberVerifiedCityOwnership(cityId, kingdomId);
			XjNativeArmySaveGuard.EnqueueTouchedCity(city);
			return true;
		}
		catch
		{
			CityRepairActors.Clear();
			return false;
		}
	}

	private static void BuildCityRepairActorSnapshot(City city)
	{
		CityRepairActors.Clear();
		if (city?.units == null || city.units.Count == 0)
		{
			return;
		}

		CityRepairSeenActorIds.Clear();
		for (int i = 0; i < city.units.Count; i++)
		{
			Actor actor = city.units[i];
			long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || !actor.isAlive() || !CityRepairSeenActorIds.Add(actorId))
			{
				continue;
			}
			CityRepairActors.Add(actor);
		}
		CityRepairSeenActorIds.Clear();
	}

	internal static bool TryRepairActorNativeOwnership(Actor actor, City expectedCity, Kingdom expectedKingdom, bool clearNativeArmy)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}

		bool changed = false;
		if (expectedCity?.data != null && actor.city == null)
		{
			try
			{
				actor.setCity(expectedCity);
				changed = true;
			}
			catch
			{
			}
		}

		if (expectedKingdom?.data != null && !ReferenceEquals(actor.kingdom, expectedKingdom))
		{
			try
			{
				actor.setKingdom(expectedKingdom);
				changed = true;
			}
			catch
			{
			}
		}

		if (clearNativeArmy)
		{
			changed |= ClearNativeArmyState(actor);
			XjNativeArmySaveGuard.EnqueueTouchedCity(expectedCity);
		}

		return changed;
	}

	private static bool HasNativeArmy(Actor actor)
	{
		try
		{
			return actor?.data != null && actor.isAlive() && actor.hasArmy();
		}
		catch
		{
			return false;
		}
	}

	private static bool ClearNativeArmyState(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}

		return XjNativeArmySaveGuard.DetachActorFromNativeArmy(
			actor,
			"worldbox_kingdom_bridge_ownership_change");
	}

	private static void EnsureOwnershipCacheWorld()
	{
		object currentWorld = World.world;
		if (ReferenceEquals(verifiedOwnershipWorld, currentWorld))
		{
			return;
		}

		verifiedOwnershipWorld = currentWorld;
		VerifiedCityKingdomIds.Clear();
	}

	private static void RememberVerifiedCityOwnership(long cityId, long kingdomId)
	{
		if (cityId > 0L && kingdomId > 0L)
		{
			VerifiedCityKingdomIds[cityId] = kingdomId;
		}
	}

	internal static bool TryCreateKingdomForCity(City capital, Actor founder, string kingdomName, out Kingdom kingdom, out string reason)
	{
		kingdom = null;
		reason = string.Empty;
		if (capital?.data == null)
		{
			reason = "建国城镇不存在";
			return false;
		}
		if (founder?.data == null || !founder.isAlive())
		{
			reason = "建国角色不存在";
			return false;
		}
		string normalized = (kingdomName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalized))
		{
			reason = "建国名称为空";
			return false;
		}

		Kingdom before = capital.kingdom;
		try
		{
			XjNationSectRebellionGuard.BeginControlledFounding();
			kingdom = capital.makeOwnKingdom(founder, true, true);
			if (kingdom?.data == null || ReferenceEquals(kingdom, before))
			{
				kingdom = capital.kingdom;
			}
			if (kingdom?.data == null || ReferenceEquals(kingdom, before))
			{
				reason = "WorldBox 建国未生成新政体";
				return false;
			}
			TryRenameKingdom(kingdom, normalized, out _);
			if (!IsCityInKingdom(capital, kingdom)
				&& !TryTransferCity(capital, kingdom, "建国定都", out reason))
			{
				return false;
			}
			TryRepairCityPopulationOwnership(capital, clearNativeArmies: true);
			TryRepairActorNativeOwnership(founder, capital, kingdom, clearNativeArmy: true);
			return true;
		}
		catch (Exception ex)
		{
			reason = "WorldBox 建国失败：" + ex.GetType().Name;
			return false;
		}
		finally
		{
			XjNationSectRebellionGuard.EndControlledFounding();
		}
	}

	internal static bool TryMoveActorToCity(Actor actor, City targetCity, string operationName, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive())
		{
			reason = "角色不存在";
			return false;
		}
		if (targetCity?.data == null)
		{
			reason = "目标城镇不存在";
			return false;
		}
		if (actor.city == targetCity) return true;

		string[] methodNames = { "setCity", "joinCity", "moveToCity", "changeCity" };
		object[] args = { targetCity };
		for (int i = 0; i < methodNames.Length; i++)
		{
			if (!TryInvokeCompatible(actor, methodNames[i], args, out _, out _)) continue;
			if (actor.city == targetCity) return true;
		}

		reason = (string.IsNullOrWhiteSpace(operationName) ? "迁城" : operationName.Trim()) + "未找到可用的 WorldBox 角色迁城 API";
		return false;
	}

	internal static bool TryStopWar(Kingdom left, Kingdom right, string operationName, out string reason)
	{
		reason = string.Empty;
		if (left?.data == null || right?.data == null || ReferenceEquals(left, right))
		{
			reason = "交战双方不存在";
			return false;
		}

		object kingdomManager = World.world?.kingdoms;
		string label = string.IsNullOrWhiteSpace(operationName) ? "停战" : operationName.Trim();
		string[] managerMethods =
		{
			"makePeace", "stopWar", "endWar", "removeWar", "setPeace", "declarePeace", "peace"
		};
		object[][] managerArgs =
		{
			new object[] { left, right },
			new object[] { right, left },
			new object[] { left, right, true },
			new object[] { right, left, true },
			new object[] { left, right, false },
			new object[] { right, left, false }
		};

		if (kingdomManager != null)
		{
			for (int i = 0; i < managerMethods.Length; i++)
			{
				for (int j = 0; j < managerArgs.Length; j++)
				{
					if (TryInvokeCompatible(kingdomManager, managerMethods[i], managerArgs[j], out _, out reason)) return true;
				}
			}
		}

		string[] kingdomMethods = { "makePeace", "stopWar", "endWar", "removeWar", "setPeace", "peace" };
		object[][] kingdomArgs =
		{
			new object[] { right },
			new object[] { right, true },
			new object[] { right, false }
		};
		for (int i = 0; i < kingdomMethods.Length; i++)
		{
			for (int j = 0; j < kingdomArgs.Length; j++)
			{
				if (TryInvokeCompatible(left, kingdomMethods[i], kingdomArgs[j], out _, out reason)) return true;
			}
		}

		reason = label + "未找到可用的 WorldBox 停战 API：" + reason;
		return false;
	}

	internal static bool TryRetireKingdom(Kingdom kingdom, string operationName, out string reason)
	{
		reason = string.Empty;
		if (kingdom?.data == null)
		{
			reason = "政体不存在";
			return false;
		}

		object kingdomManager = World.world?.kingdoms;
		string label = string.IsNullOrWhiteSpace(operationName) ? "政体退场" : operationName.Trim();
		string[] managerMethods = { "removeKingdom", "destroyKingdom", "deleteKingdom", "killKingdom", "removeObject", "remove" };
		object[][] argumentSets =
		{
			new object[] { kingdom },
			new object[] { kingdom, true },
			new object[] { kingdom, false }
		};

		if (kingdomManager != null)
		{
			for (int i = 0; i < managerMethods.Length; i++)
			{
				for (int j = 0; j < argumentSets.Length; j++)
				{
					if (TryInvokeCompatible(kingdomManager, managerMethods[i], argumentSets[j], out _, out reason)) return true;
				}
			}
		}

		string[] kingdomMethods = { "destroy", "kill", "die", "remove" };
		for (int i = 0; i < kingdomMethods.Length; i++)
		{
			if (TryInvokeCompatible(kingdom, kingdomMethods[i], Array.Empty<object>(), out _, out reason)) return true;
		}

		reason = label + "未找到可用的 WorldBox 政体移除 API：" + reason;
		return false;
	}

	private static bool TryInvokeCityTransfer(City city, Kingdom targetKingdom, bool captured, bool bypassFormation, out string reason)
	{
		reason = string.Empty;
		try
		{
			// setKingdom(..., true) is only a save-load assignment. It leaves residents,
			// buildings and kingdom population indexes in their previous state. The native
			// conquest path transfers all city-owned simulation objects atomically.
			if (bypassFormation) forcedConquestDepth++;
			city.joinAnotherKingdom(targetKingdom, captured, false);
			return true;
		}
		catch (Exception ex)
		{
			reason = ex.GetType().Name;
			return false;
		}
		finally
		{
			if (bypassFormation && forcedConquestDepth > 0) forcedConquestDepth--;
		}
	}

	private static bool TryRollbackCity(City city, Kingdom previousKingdom, bool bypassFormation)
	{
		if (city?.data == null || previousKingdom?.data == null) return false;
		TryInvokeCityTransfer(city, previousKingdom, false, bypassFormation, out _);
		return IsCityInKingdom(city, previousKingdom);
	}

	internal static bool IsCityInKingdom(City city, Kingdom kingdom)
	{
		if (city?.data == null || kingdom?.data == null || city.kingdom?.data == null) return false;
		return ReferenceEquals(city.kingdom, kingdom) || city.kingdom.data.id == kingdom.data.id;
	}

	private static bool TryInvokeCompatible(object target, string methodName, object[] args, out object result, out string reason)
	{
		result = null;
		reason = string.Empty;
		if (target == null || string.IsNullOrWhiteSpace(methodName)) return false;
		Type type = target.GetType();
		MethodInfo[] methods = type.GetMethods(InstanceFlags);
		for (int i = 0; i < methods.Length; i++)
		{
			MethodInfo method = methods[i];
			if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
			ParameterInfo[] parameters = method.GetParameters();
			if (parameters.Length != args.Length) continue;
			if (!ArgumentsMatch(parameters, args)) continue;
			try
			{
				result = method.Invoke(target, args);
				return true;
			}
			catch (TargetInvocationException ex)
			{
				reason = ex.InnerException?.GetType().Name ?? ex.GetType().Name;
				return false;
			}
			catch (Exception ex)
			{
				reason = ex.GetType().Name;
				return false;
			}
		}
		reason = methodName + "(" + args.Length + ")";
		return false;
	}

	private static bool ArgumentsMatch(ParameterInfo[] parameters, object[] args)
	{
		for (int i = 0; i < parameters.Length; i++)
		{
			Type parameterType = parameters[i].ParameterType;
			object arg = args[i];
			if (arg == null)
			{
				if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null) return false;
				continue;
			}
			if (!parameterType.IsInstanceOfType(arg)) return false;
		}
		return true;
	}
}
