using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace XuanJianVNext.Systems.Combat;

internal static class XjNativeArmySaveGuard
{
	private const int MaximumArmiesPerSave = 4096;
	private const int MaximumCitiesPerSave = 4096;
	private const int MaximumActorsPerSave = 12000;
	private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly List<Army> ArmyScratch = new List<Army>(128);
	private static readonly List<Actor> ActorScratch = new List<Actor>(512);
	private static readonly List<City> CityScratch = new List<City>(128);
	private static readonly Queue<Actor> PendingActors = new Queue<Actor>(512);
	private static readonly Queue<City> PendingCities = new Queue<City>(128);
	private static readonly Queue<Army> PendingArmies = new Queue<Army>(128);
	private static readonly HashSet<long> PendingActorIds = new HashSet<long>();
	private static readonly HashSet<long> PendingCityIds = new HashSet<long>();
	private static readonly HashSet<int> PendingArmyKeys = new HashSet<int>();
	private static int _logCount;

	internal static void SanitizeBeforeSave()
	{
		ArmyScratch.Clear();
		ActorScratch.Clear();
		CityScratch.Clear();
		try
		{
			XjHighRealmArmyExclusion.FlushBeforeSave();
			object manager = ReadMember(World.world, "armies");
			CollectArmies(manager, ArmyScratch);
			CollectActors(ReadMember(World.world, "units"), ActorScratch);
			CollectCities(ReadMember(World.world, "cities"), CityScratch);
			for (int i = 0; i < ActorScratch.Count; i++)
			{
				SanitizeActorArmyReference(ActorScratch[i]);
			}
			for (int i = 0; i < CityScratch.Count; i++)
			{
				SanitizeCityArmyReference(CityScratch[i]);
			}
			for (int i = 0; i < ArmyScratch.Count; i++)
			{
				SanitizeArmy(ArmyScratch[i]);
			}
			for (int i = 0; i < ActorScratch.Count; i++)
			{
				SanitizeActorArmyReference(ActorScratch[i]);
			}
			for (int i = 0; i < CityScratch.Count; i++)
			{
				SanitizeCityArmyReference(CityScratch[i]);
			}
		}
		catch (Exception ex)
		{
			LogOnce("[玄鉴][军队存档] 保存前军队引用检查失败：" + ex.GetType().Name);
		}
		finally
		{
			ArmyScratch.Clear();
			ActorScratch.Clear();
			CityScratch.Clear();
		}
	}

	internal static void ScheduleAfterLoadInspection()
	{
		// 读档后只排入真正持有军队引用的对象。不要把全部人口放进
		// 运行队列，否则大世界加载后会产生无意义的长尾扫描。
		ArmyScratch.Clear();
		ActorScratch.Clear();
		CityScratch.Clear();
		try
		{
			CollectArmies(ReadMember(World.world, "armies"), ArmyScratch);
			CollectActors(ReadMember(World.world, "units"), ActorScratch);
			CollectCities(ReadMember(World.world, "cities"), CityScratch);
			for (int i = 0; i < CityScratch.Count; i++)
			{
				City city = CityScratch[i];
				if (ResolveCityArmy(city) != null)
				{
					EnqueueCity(city);
				}
			}
			for (int i = 0; i < ActorScratch.Count; i++)
			{
				Actor actor = ActorScratch[i];
				if (ResolveActorArmy(actor) != null)
				{
					EnqueueActor(actor);
				}
			}
			for (int i = 0; i < ArmyScratch.Count; i++)
			{
				EnqueueArmy(ArmyScratch[i]);
			}
		}
		catch
		{
		}
		finally
		{
			ArmyScratch.Clear();
			ActorScratch.Clear();
			CityScratch.Clear();
		}
	}

	internal static bool HasPendingRuntimeInspection =>
		PendingCities.Count > 0 || PendingActors.Count > 0 || PendingArmies.Count > 0;

	internal static void TickRuntimeInspection(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		int processed = 0;
		while (processed < budget && PendingCities.Count > 0)
		{
			processed++;
			City city = PendingCities.Dequeue();
			ForgetCity(city);
			SanitizeCityArmyReference(city);
		}
		while (processed < budget && PendingActors.Count > 0)
		{
			processed++;
			Actor actor = PendingActors.Dequeue();
			ForgetActor(actor);
			SanitizeActorArmyReference(actor);
		}
		while (processed < budget && PendingArmies.Count > 0)
		{
			processed++;
			Army army = PendingArmies.Dequeue();
			ForgetArmy(army);
			SanitizeArmy(army);
		}
	}

	internal static bool DetachActorFromNativeArmy(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return false;
		}

		Army previousArmy = ResolveActorArmy(actor);
		City touchedCity = previousArmy == null
			? actor.city
			: (ReadMember(previousArmy, "city") as City ?? actor.city);
		bool changed = ClearActorArmyReference(
			actor,
			string.IsNullOrWhiteSpace(reason) ? "detach_actor_army" : reason);

		// removeFromArmy 只保证角色侧完成退出。军队若以该角色为 captain，
		// 仍需在边界队列中检查 Army 与 City 两端，避免留下可运行但不可保存的孤儿对象。
		if (previousArmy != null)
		{
			EnqueueArmy(previousArmy);
		}
		if (touchedCity?.data != null)
		{
			EnqueueCity(touchedCity);
		}
		return changed;
	}

	internal static void EnqueueTouchedCity(City city)
	{
		EnqueueCity(city);
	}

	internal static void ClearRuntime()
	{
		PendingActors.Clear();
		PendingCities.Clear();
		PendingArmies.Clear();
		PendingActorIds.Clear();
		PendingCityIds.Clear();
		PendingArmyKeys.Clear();
		ArmyScratch.Clear();
		ActorScratch.Clear();
		CityScratch.Clear();
	}

	internal static void SanitizeCityArmyReference(City city)
	{
		if (city?.data == null)
		{
			return;
		}

		Army army = ResolveCityArmy(city);
		if (army == null)
		{
			return;
		}

		if (IsBrokenArmyForCity(city, army))
		{
			ClearCityArmyReference(city, army, "city_army_invalid");
		}
	}

	internal static bool RecoverCityArmyReference(City city, Exception exception, string source)
	{
		if (exception is not NullReferenceException || city?.data == null)
		{
			return false;
		}

		Army army = ResolveCityArmy(city);
		ClearCityArmyReference(city, army, string.IsNullOrWhiteSpace(source) ? "city_army_exception" : source);
		return true;
	}

	private static void CollectActors(object manager, List<Actor> target)
	{
		if (manager == null || target == null)
		{
			return;
		}

		AddActorEnumerable(manager, target);

		string[] memberNames =
		{
			"list",
			"objects",
			"active",
			"base_objects",
			"all_objects",
			"_list",
			"_objects"
		};
		for (int i = 0; i < memberNames.Length; i++)
		{
			if (target.Count >= MaximumActorsPerSave)
			{
				return;
			}
			AddActorEnumerable(ReadMember(manager, memberNames[i]), target);
		}
	}

	private static void CollectCities(object manager, List<City> target)
	{
		if (manager == null || target == null)
		{
			return;
		}

		AddCityEnumerable(manager, target);

		string[] memberNames =
		{
			"list",
			"objects",
			"active",
			"base_objects",
			"all_objects",
			"_list",
			"_objects"
		};
		for (int i = 0; i < memberNames.Length && target.Count < MaximumCitiesPerSave; i++)
		{
			AddCityEnumerable(ReadMember(manager, memberNames[i]), target);
		}
	}

	private static void AddCityEnumerable(object source, List<City> target)
	{
		if (source == null || target == null || source is string || source is not IEnumerable enumerable)
		{
			return;
		}

		if (source is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (target.Count >= MaximumCitiesPerSave)
				{
					return;
				}
				if (entry.Value is City dictionaryCity && !target.Contains(dictionaryCity))
				{
					target.Add(dictionaryCity);
				}
			}
			return;
		}

		foreach (object item in enumerable)
		{
			if (target.Count >= MaximumCitiesPerSave)
			{
				return;
			}
			if (item is City city && !target.Contains(city))
			{
				target.Add(city);
			}
		}
	}

	private static void AddActorEnumerable(object source, List<Actor> target)
	{
		if (source == null || target == null || source is string || source is not IEnumerable enumerable)
		{
			return;
		}

		if (source is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (target.Count >= MaximumActorsPerSave)
				{
					return;
				}
				if (entry.Value is Actor dictionaryActor && !target.Contains(dictionaryActor))
				{
					target.Add(dictionaryActor);
				}
			}
			return;
		}

		foreach (object item in enumerable)
		{
			if (target.Count >= MaximumActorsPerSave)
			{
				return;
			}
			if (item is Actor actor && !target.Contains(actor))
			{
				target.Add(actor);
			}
		}
	}

	private static void CollectArmies(object manager, List<Army> target)
	{
		if (manager == null || target == null)
		{
			return;
		}

		AddEnumerable(manager, target);

		string[] memberNames =
		{
			"list",
			"objects",
			"active",
			"base_objects",
			"all_objects",
			"_list",
			"_objects"
		};
		for (int i = 0; i < memberNames.Length && target.Count < MaximumArmiesPerSave; i++)
		{
			AddEnumerable(ReadMember(manager, memberNames[i]), target);
		}
	}

	private static void AddEnumerable(object source, List<Army> target)
	{
		if (source == null || target == null || source is string || source is not IEnumerable enumerable)
		{
			return;
		}

		if (source is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (target.Count >= MaximumArmiesPerSave)
				{
					return;
				}
				if (entry.Value is Army dictionaryArmy && !target.Contains(dictionaryArmy))
				{
					target.Add(dictionaryArmy);
				}
			}
			return;
		}

		foreach (object item in enumerable)
		{
			if (target.Count >= MaximumArmiesPerSave)
			{
				return;
			}

			if (item is Army army && !target.Contains(army))
			{
				target.Add(army);
			}
		}
	}

	internal static void SanitizeActorArmyReference(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		Army army = ResolveActorArmy(actor);
		bool reportsArmy = false;
		try
		{
			reportsArmy = actor.hasArmy();
		}
		catch
		{
			reportsArmy = true;
		}

		if (army == null)
		{
			// 某些 WorldBox 构建的实际军队字段名不在反射候选中，但
			// Actor.hasArmy()/saveArmy 仍会认为角色持有军队。此时必须调用
			// 原生 removeFromArmy 清掉隐藏引用，不能直接返回。
			if (reportsArmy)
			{
				ClearActorArmyReference(actor, "actor_army_unresolved");
			}
			return;
		}

		if (IsBrokenArmyForActorSave(army))
		{
			ClearActorArmyReference(actor, "actor_army_invalid");
			return;
		}

		if (!reportsArmy)
		{
			ClearActorArmyReference(actor, "actor_army_flag_mismatch");
		}
	}

	private static Army ResolveActorArmy(Actor actor)
	{
		if (actor == null)
		{
			return null;
		}

		string[] memberNames =
		{
			"army",
			"current_army",
			"currentArmy",
			"_army",
			"_current_army",
			"unit_army"
		};
		for (int i = 0; i < memberNames.Length; i++)
		{
			if (ReadMember(actor, memberNames[i]) is Army army)
			{
				return army;
			}
		}

		return null;
	}

	private static Army ResolveCityArmy(City city)
	{
		if (city == null)
		{
			return null;
		}

		string[] memberNames =
		{
			"army",
			"current_army",
			"currentArmy",
			"_army",
			"_current_army",
			"city_army"
		};
		for (int i = 0; i < memberNames.Length; i++)
		{
			if (ReadMember(city, memberNames[i]) is Army army)
			{
				return army;
			}
		}

		return null;
	}

	private static bool IsBrokenArmyForActorSave(Army army)
	{
		if (army == null)
		{
			return true;
		}

		object data = ReadMember(army, "data");
		if (data == null)
		{
			return true;
		}

		try
		{
			_ = ((NanoObject)army).id;
		}
		catch
		{
			return true;
		}

		return false;
	}

	private static bool IsBrokenArmyForCity(City city, Army army)
	{
		if (city?.data == null || IsBrokenArmyForActorSave(army))
		{
			return true;
		}

		object armyCity = ReadMember(army, "city") ?? ReadMember(army, "_city");
		if (armyCity == null)
		{
			return true;
		}
		if (armyCity is City boundCity)
		{
			return boundCity.data == null || !ReferenceEquals(boundCity, city);
		}

		return false;
	}

	private static bool ClearActorArmyReference(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return false;
		}

		bool changed = false;
		try
		{
			// 不先调用 hasArmy。断裂引用本身可能让 hasArmy 抛异常，
			// 但原生 removeFromArmy 仍可能完成双向解绑。
			actor.removeFromArmy();
			changed = true;
		}
		catch
		{
		}

		changed |= WriteMember(actor, "army", null);
		changed |= WriteMember(actor, "current_army", null);
		changed |= WriteMember(actor, "currentArmy", null);
		changed |= WriteMember(actor, "_army", null);
		changed |= WriteMember(actor, "_current_army", null);
		changed |= WriteMember(actor, "unit_army", null);

		// 保存兜底只解绑损坏的 Army 引用，不修改国王、领袖等职业身份。

		if (changed)
		{
			LogOnce("[玄鉴][军队存档] 已清理角色断裂军队引用：" + reason);
		}
		return changed;
	}

	private static void ClearCityArmyReference(City city, Army army, string reason)
	{
		if (city?.data == null)
		{
			return;
		}

		WriteMember(city, "army", null);
		WriteMember(city, "current_army", null);
		WriteMember(city, "currentArmy", null);
		WriteMember(city, "_army", null);
		WriteMember(city, "_current_army", null);
		WriteMember(city, "city_army", null);

		if (army != null)
		{
			WriteMember(army, "city", null);
			WriteMember(army, "_city", null);
			TryRetireArmy(army, reason);
		}

		LogOnce("[玄鉴][军队存档] 已清理城市断裂军队引用：" + reason);
	}

	private static void EnqueueActor(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !PendingActorIds.Add(actorId))
		{
			return;
		}

		PendingActors.Enqueue(actor);
	}

	private static void EnqueueCity(City city)
	{
		long cityId = city?.data == null ? 0L : city.data.id;
		if (cityId <= 0L || !PendingCityIds.Add(cityId))
		{
			return;
		}

		PendingCities.Enqueue(city);
	}

	private static void EnqueueArmy(Army army)
	{
		if (army == null)
		{
			return;
		}

		int key = RuntimeHelpers.GetHashCode(army);
		if (!PendingArmyKeys.Add(key))
		{
			return;
		}

		PendingArmies.Enqueue(army);
	}

	private static void ForgetActor(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId > 0L)
		{
			PendingActorIds.Remove(actorId);
		}
	}

	private static void ForgetCity(City city)
	{
		long cityId = city?.data == null ? 0L : city.data.id;
		if (cityId > 0L)
		{
			PendingCityIds.Remove(cityId);
		}
	}

	private static void ForgetArmy(Army army)
	{
		if (army != null)
		{
			PendingArmyKeys.Remove(RuntimeHelpers.GetHashCode(army));
		}
	}

	private static void SanitizeArmy(Army army)
	{
		if (army == null)
		{
			return;
		}

		object data = ReadMember(army, "data");
		if (data == null)
		{
			TryRetireArmy(army, "data_missing");
			return;
		}

		City city = ReadMember(army, "city") as City;
		Kingdom kingdom = ReadMember(army, "kingdom") as Kingdom;
		Actor captain = ResolveCaptain(army);

		if (IsBrokenActor(captain))
		{
			// Army.save 会直接读取 captain 的 NanoObject id；空 captain 不是可保存状态。
			TryRetireArmy(army, captain == null ? "captain_missing" : "captain_invalid");
			return;
		}

		City fallbackCity = ResolveValidCity(city) ?? ResolveValidCity(captain.city);
		Kingdom fallbackKingdom = ResolveValidKingdom(kingdom)
			?? ResolveValidKingdom(fallbackCity?.kingdom)
			?? ResolveValidKingdom(captain.kingdom);

		if (city == null || IsBrokenCity(city))
		{
			if (fallbackCity != null)
			{
				WriteMember(army, "city", fallbackCity);
			}
		}

		if (kingdom == null || IsBrokenKingdom(kingdom))
		{
			if (fallbackKingdom != null)
			{
				WriteMember(army, "kingdom", fallbackKingdom);
			}
		}

		if (IsBrokenActor(ResolveCaptain(army))
			|| IsBrokenCity(ReadMember(army, "city") as City)
			|| IsBrokenKingdom(ReadMember(army, "kingdom") as Kingdom))
		{
			TryRetireArmy(army, "owner_missing");
		}
	}

	private static Actor ResolveCaptain(Army army)
	{
		if (army == null)
		{
			return null;
		}

		object direct = ReadMember(army, "captain") ?? ReadMember(army, "_captain") ?? ReadMember(army, "leader");
		if (direct is Actor actor)
		{
			return actor;
		}

		string[] methods = { "getCaptain", "getLeader" };
		for (int i = 0; i < methods.Length; i++)
		{
			try
			{
				MethodInfo method = army.GetType().GetMethod(methods[i], Flags, null, Type.EmptyTypes, null);
				if (method?.Invoke(army, null) is Actor result)
				{
					return result;
				}
			}
			catch
			{
			}
		}

		return null;
	}

	private static void ClearCaptain(Army army)
	{
		if (army == null)
		{
			return;
		}

		try
		{
			MethodInfo setCaptain = army.GetType().GetMethod("setCaptain", Flags);
			ParameterInfo[] parameters = setCaptain?.GetParameters();
			if (setCaptain != null && parameters != null)
			{
				if (parameters.Length == 2)
				{
					setCaptain.Invoke(army, new object[] { null, false });
					return;
				}
				if (parameters.Length == 1)
				{
					setCaptain.Invoke(army, new object[] { null });
					return;
				}
			}
		}
		catch
		{
		}

		WriteMember(army, "captain", null);
		WriteMember(army, "_captain", null);
		WriteMember(army, "leader", null);
	}

	private static City ResolveValidCity(City city)
	{
		return IsBrokenCity(city) ? null : city;
	}

	private static Kingdom ResolveValidKingdom(Kingdom kingdom)
	{
		return IsBrokenKingdom(kingdom) ? null : kingdom;
	}

	private static bool IsBrokenActor(Actor actor)
	{
		try
		{
			return actor == null || actor.data == null || actor.asset == null || !actor.isAlive();
		}
		catch
		{
			return true;
		}
	}

	private static bool IsBrokenCity(City city)
	{
		try
		{
			return city == null || city.data == null;
		}
		catch
		{
			return true;
		}
	}

	private static bool IsBrokenKingdom(Kingdom kingdom)
	{
		try
		{
			return kingdom == null || kingdom.data == null || kingdom.asset == null;
		}
		catch
		{
			return true;
		}
	}

	private static bool TryRetireArmy(Army army, string reason)
	{
		if (army == null)
		{
			return false;
		}

		string[] methods = { "removeFromWorld", "remove", "destroy", "kill" };
		for (int i = 0; i < methods.Length; i++)
		{
			try
			{
				MethodInfo method = army.GetType().GetMethod(methods[i], Flags, null, Type.EmptyTypes, null);
				if (method == null)
				{
					continue;
				}

				method.Invoke(army, null);
				// 某些原生版本只把 Army 标记为退役，不会立刻从管理器容器移除。
				// 保存前再做一次幂等移除，确保 Army.save 不会继续遍历到空 captain。
				TryRemoveFromArmyManager(army);
				LogOnce("[玄鉴][军队存档] 已清理断裂军队引用：" + reason);
				return true;
			}
			catch
			{
			}
		}

		if (TryRemoveFromArmyManager(army))
		{
			LogOnce("[玄鉴][军队存档] 已从管理器移除断裂军队：" + reason);
			return true;
		}

		LogOnce("[玄鉴][军队存档] 发现断裂军队引用但未找到原生清理入口：" + reason);
		return false;
	}

	private static bool TryRemoveFromArmyManager(Army army)
	{
		object manager = ReadMember(World.world, "armies");
		if (manager == null || army == null)
		{
			return false;
		}

		string[] methodNames = { "removeObject", "remove", "deleteObject", "destroyObject" };
		for (int i = 0; i < methodNames.Length; i++)
		{
			MethodInfo[] methods = manager.GetType().GetMethods(Flags);
			for (int m = 0; m < methods.Length; m++)
			{
				MethodInfo method = methods[m];
				ParameterInfo[] parameters = method.GetParameters();
				if (!string.Equals(method.Name, methodNames[i], StringComparison.Ordinal)
					|| parameters.Length != 1
					|| !parameters[0].ParameterType.IsAssignableFrom(army.GetType()))
				{
					continue;
				}
				try
				{
					method.Invoke(manager, new object[] { army });
					return true;
				}
				catch
				{
				}
			}
		}

		bool removed = RemoveFromCollection(manager, army);
		string[] memberNames = { "list", "objects", "active", "base_objects", "all_objects", "_list", "_objects" };
		for (int i = 0; i < memberNames.Length; i++)
		{
			removed |= RemoveFromCollection(ReadMember(manager, memberNames[i]), army);
		}
		return removed;
	}

	private static bool RemoveFromCollection(object collection, Army army)
	{
		if (collection == null || army == null)
		{
			return false;
		}

		try
		{
			if (collection is IList list && list.Contains(army))
			{
				list.Remove(army);
				return true;
			}
			if (collection is IDictionary dictionary)
			{
				object keyToRemove = null;
				foreach (DictionaryEntry entry in dictionary)
				{
					if (ReferenceEquals(entry.Value, army))
					{
						keyToRemove = entry.Key;
						break;
					}
				}
				if (keyToRemove != null)
				{
					dictionary.Remove(keyToRemove);
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	private static object ReadMember(object target, string name)
	{
		if (target == null || string.IsNullOrWhiteSpace(name))
		{
			return null;
		}

		Type type = target.GetType();
		try
		{
			FieldInfo field = type.GetField(name, Flags);
			if (field != null)
			{
				return field.GetValue(target);
			}
		}
		catch
		{
		}

		try
		{
			PropertyInfo property = type.GetProperty(name, Flags);
			if (property != null && property.CanRead)
			{
				return property.GetValue(target, null);
			}
		}
		catch
		{
		}

		return null;
	}

	private static bool WriteMember(object target, string name, object value)
	{
		if (target == null || string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		Type type = target.GetType();
		try
		{
			FieldInfo field = type.GetField(name, Flags);
			if (field != null && !field.IsInitOnly)
			{
				field.SetValue(target, value);
				return true;
			}
		}
		catch
		{
		}

		try
		{
			PropertyInfo property = type.GetProperty(name, Flags);
			if (property != null && property.CanWrite)
			{
				property.SetValue(target, value, null);
				return true;
			}
		}
		catch
		{
		}

		return false;
	}

	private static void LogOnce(string message)
	{
		if (_logCount >= 6 || string.IsNullOrWhiteSpace(message))
		{
			return;
		}

		_logCount++;
		Debug.LogWarning(message);
	}
}
