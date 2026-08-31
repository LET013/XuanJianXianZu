using System;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Central boundary for WorldBox kingdom and city ownership mutations.
/// XuanJian data may mirror ownership, but native city transfers must pass
/// through WorldBox methods so formation gates, borders and engine caches stay valid.
/// </summary>
internal static class XjWorldBoxKingdomBridge
{
	// High-realm suppression is an explicit resolution, not a normal siege. Keep
	// this scope narrow so operational sect formations still gate ordinary wars.
	[ThreadStatic]
	private static int forcedConquestDepth;
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
		if (IsCityInKingdom(city, targetKingdom)) return true;

		string label = string.IsNullOrWhiteSpace(operationName) ? "转归属" : operationName.Trim();
		if (!TryInvokeCityTransfer(city, targetKingdom, true, bypassFormation, out reason))
		{
			reason = label + "调用原生城镇转归属失败：" + reason;
			return false;
		}

		// joinAnotherKingdom 是 WorldBox 的完整城镇转国事务。玄鉴只验证最终结果并
		// 登记军队引用为 dirty；不再逐居民 setKingdom、forceBuildingsToKingdom、
		// switchedKingdom 或失败后反向再转一次，从根上消除第二套城市归属生命周期。
		if (!IsCityInKingdom(city, targetKingdom))
		{
			reason = label + "未被原生城镇转归属事务接受";
			return false;
		}

		XjNativeArmySaveGuard.EnqueueTouchedCity(city);
		return true;
	}

	/// <summary>
	/// 仅用于极窄的兼容场景：角色 kingdom 指针丢失/漂移，但角色当前 city 本身
	/// 仍明确属于 expectedKingdom。此时以 WorldBox 当前城市事实为权威补一个指针。
	/// 不改 city、不搬居民、不改军队，也不允许跨国把角色拖回旧制度。
	/// </summary>
	internal static bool TryRepairActorKingdomFromCurrentCity(Actor actor, Kingdom expectedKingdom)
	{
		if (actor?.data == null || !actor.isAlive() || expectedKingdom?.data == null) return false;
		if (actor.kingdom?.data?.id == expectedKingdom.data.id) return true;
		City city = actor.city;
		if (city?.data == null || city.kingdom?.data?.id != expectedKingdom.data.id) return false;
		try
		{
			actor.setKingdom(expectedKingdom);
			return actor.kingdom?.data?.id == expectedKingdom.data.id;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjWorldBoxKingdomBridge.RepairActorKingdomFromCity", ex);
			return false;
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
			// 只调用一次 WorldBox 原生建国事务。makeOwnKingdom 自己负责首都、
			// 居民、军队与国朝索引；玄鉴不再在成功后补做转城/逐角色修复。
			kingdom = capital.makeOwnKingdom(founder, true, true);
			if (kingdom?.data == null || ReferenceEquals(kingdom, before)) kingdom = capital.kingdom;
			if (kingdom?.data == null || ReferenceEquals(kingdom, before))
			{
				reason = "建国后未能形成新的国朝";
				return false;
			}
			if (!IsCityInKingdom(capital, kingdom))
			{
				reason = "原生建国事务未完成首都归属";
				return false;
			}
			TryRenameKingdom(kingdom, normalized, out _);
			XjNativeArmySaveGuard.EnqueueTouchedCity(capital);
			return true;
		}
		catch (Exception ex)
		{
			reason = "建国未成：" + ex.GetType().Name;
			return false;
		}
		finally
		{
			XjNationSectRebellionGuard.EndControlledFounding();
		}
	}


	/// <summary>
	/// 仙国等外部制度只读判断角色是否已经由 WorldBox 自身推为城镇领袖。
	/// 不任命、不改ID、不触发任何政治写入；用于把“自立为王”限制在已有政治根基上。
	/// </summary>
	internal static bool IsNativeCityLeaderForActor(City city, Actor actor)
	{
		if (city?.data == null || actor?.data == null || !actor.isAlive()) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L && IsNativeCityLeader(city, actorId, actor);
	}

	/// <summary>
	/// 只读解析 WorldBox 已存在的城主。一次读取原生 leader/leaderId 即可，
	/// 避免制度事件为了找城主逐个枚举整座城市居民并重复做反射探针。
	/// </summary>
	internal static bool TryResolveNativeCityLeader(City city, out Actor leader)
	{
		leader = null;
		if (city?.data == null) return false;

		string[] names = { "leader", "_leader", "city_leader", "cityLeader", "mayor", "ruler" };
		for (int i = 0; i < names.Length; i++)
		{
			object value = XjNativeReflectionInterop.ReadMemberValue(city, names[i]);
			if (value is Actor actor && actor?.data != null && actor.isAlive())
			{
				leader = actor;
				return true;
			}
			if (TryExtractActorId(value, out long actorId)
				&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor resolved)
				&& resolved?.data != null
				&& resolved.isAlive())
			{
				leader = resolved;
				return true;
			}
		}

		string[] idNames = { "leaderID", "leaderId", "leader_id", "cityLeaderID", "cityLeaderId", "city_leader_id", "mayorID", "mayorId", "mayor_id" };
		for (int i = 0; i < idNames.Length; i++)
		{
			long leaderId = 0L;
			bool found = XjNativeReflectionInterop.TryReadLongMember(city, idNames[i], out leaderId)
				|| XjNativeReflectionInterop.TryReadLongMember(city.data, idNames[i], out leaderId);
			if (!found || leaderId <= 0L) continue;
			if (XjActorRegistry.ResolveKnownOrWorld(leaderId, out Actor resolved)
				&& resolved?.data != null
				&& resolved.isAlive())
			{
				leader = resolved;
				return true;
			}
		}

		return false;
	}

	private static bool IsNativeCityLeader(City city, long actorId, Actor actor)
	{
		if (city?.data == null || actorId <= 0L) return false;
		string[] names = { "leader", "_leader", "city_leader", "cityLeader", "mayor", "ruler" };
		for (int i = 0; i < names.Length; i++)
		{
			object value = XjNativeReflectionInterop.ReadMemberValue(city, names[i]);
			if (ReferenceEquals(value, actor)) return true;
			if (TryExtractActorId(value, out long currentId) && currentId == actorId) return true;
		}

		string[] idNames = { "leaderID", "leaderId", "leader_id", "cityLeaderID", "cityLeaderId", "city_leader_id", "mayorID", "mayorId", "mayor_id" };
		for (int i = 0; i < idNames.Length; i++)
		{
			if (XjNativeReflectionInterop.TryReadLongMember(city, idNames[i], out long cityLeaderId) && cityLeaderId == actorId) return true;
			if (XjNativeReflectionInterop.TryReadLongMember(city.data, idNames[i], out long dataLeaderId) && dataLeaderId == actorId) return true;
		}
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
					if (XjNativeReflectionInterop.TryInvokeCompatible(kingdomManager, managerMethods[i], managerArgs[j], out _, out reason)) return true;
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
				if (XjNativeReflectionInterop.TryInvokeCompatible(left, kingdomMethods[i], kingdomArgs[j], out _, out reason)) return true;
			}
		}

		reason = label + "未找到可用的停战之法：" + reason;
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
					if (XjNativeReflectionInterop.TryInvokeCompatible(kingdomManager, managerMethods[i], argumentSets[j], out _, out reason)) return true;
				}
			}
		}

		string[] kingdomMethods = { "destroy", "kill", "die", "remove" };
		for (int i = 0; i < kingdomMethods.Length; i++)
		{
			if (XjNativeReflectionInterop.TryInvokeCompatible(kingdom, kingdomMethods[i], Array.Empty<object>(), out _, out reason)) return true;
		}

		reason = label + "未找到可用的国朝移除之法：" + reason;
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

	internal static bool IsCityInKingdom(City city, Kingdom kingdom)
	{
		if (city?.data == null || kingdom?.data == null || city.kingdom?.data == null) return false;
		return ReferenceEquals(city.kingdom, kingdom) || city.kingdom.data.id == kingdom.data.id;
	}

	private static bool TryExtractActorId(object value, out long actorId)
	{
		actorId = 0L;
		if (value == null) return false;
		if (value is Actor actor && actor.data != null)
		{
			actorId = ((BaseSystemData)actor.data).id;
			return actorId > 0L;
		}
		try
		{
			actorId = Convert.ToInt64(value);
			return actorId > 0L;
		}
		catch
		{
			object data = XjNativeReflectionInterop.ReadMemberValue(value, "data");
			if (data is BaseSystemData systemData && systemData.id > 0L)
			{
				actorId = systemData.id;
				return true;
			}
			return XjNativeReflectionInterop.TryReadLongMember(value, "id", out actorId) && actorId > 0L;
		}
	}

}