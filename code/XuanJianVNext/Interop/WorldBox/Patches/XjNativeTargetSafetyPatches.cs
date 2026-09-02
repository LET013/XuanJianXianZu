using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ai.behaviours;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Patches;

/// <summary>
/// BatchActors 的 b2 索敌检查在目标对象刚死亡/移除、地图格已清空时会连续抛空引用。
/// 前缀只清理失效目标；正常目标、正常索敌与原生行动逻辑完全放行。
/// </summary>
internal static class XjNativeTargetSafetyPatches
{
	// makeBabyViaVegetative 在 0.51.2 中存在原生状态不完整仍被 taking_roots
	// 完成回调重复调用的情况。一个 Actor 一旦在该原生事务中实际抛过 NRE，
	// 同一世界内继续重试只会重复异常，没有成功语义可保留。按稳定 ActorId
	// fail-closed；换世界时用 world instance token 自动清空，不长期持有 World 对象。
	private static readonly ConcurrentDictionary<long, byte> VegetativeFaultedActorIds = new ConcurrentDictionary<long, byte>();
	private static int _vegetativeFaultWorldToken;
	private static int _vegetativeUnexpectedFaultLogged;
	// 原生 taking_roots 状态完成时会调用 BabyMaker。角色若在该状态跨帧期间
	// 被替换/销毁，BabyMaker 会对已失效的 pActor 解引用，进而中断整段状态更新。
	// 这不是修炼逻辑可修复的生育事件；只对已无原生 data/asset 的对象跳过本次结算。
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyMaker), "makeBabyViaVegetative", new Type[] { typeof(Actor) })]
	private static bool BabyMakerMakeBabyViaVegetativeInvalidActorPrefix(Actor pActor)
	{
		// taking_roots 完成回调不会重新检查角色是否还活着、是否仍在地图上。
		// 同时，0.51.2 的 vegetative 高层事务假定 makeBaby 一定返回 child；如果玄鉴
		// 到底层 makeBaby 才因高境/旃檀林/子嗣上限拒绝，原生方法会继续解引用 null。
		// 因此可预知的业务拒绝必须在进入原生事务前完成，底层 makeBaby 门禁仅作兜底。
		EnsureVegetativeFaultWorld();
		if (!IsValidBirthActor(pActor)) return false;
		long actorId = pActor?.data?.id ?? 0L;
		if (actorId > 0L && VegetativeFaultedActorIds.ContainsKey(actorId)) return false;
		// 实机诊断已把重复 vegetative NRE 收敛到同一原生坏状态：garl 仍挂 kingdom，
		// 但 city 已为空。该事务没有成功出生语义，直接在进入 BabyMaker 前静默拒绝，
		// 不再先抛一次异常再把每个 Actor 加入 fault set。
		if (IsKnownUnsafeVegetativeContext(pActor)) return false;
		return !XjHighRealmBirthPatches.ShouldBlockBirth(pActor);
	}


	/// <summary>
	/// vegetative 是 0.51.2 已证实的原生故障边界。前置有效性判断仍无法覆盖
	/// taking_roots 跨帧残留的全部内部状态，因此第一次真实 NRE 后将该 Actor
	/// 记入本世界故障集合，后续同一事务 fail-closed，避免年年异常重试。
	/// </summary>
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(BabyMaker), "makeBabyViaVegetative", new Type[] { typeof(Actor) })]
	private static Exception BabyMakerMakeBabyViaVegetativeFinalizer(Actor pActor, Exception __exception)
	{
		if (__exception is not NullReferenceException) return __exception;
		EnsureVegetativeFaultWorld();
		long actorId = 0L;
		try { actorId = pActor?.data?.id ?? 0L; } catch { }
		if (actorId > 0L) VegetativeFaultedActorIds[actorId] = 1;

		// 这里只保留“本世界第一次未知 vegetative NRE”的完整诊断。已知 garl/cityless
		// 情形在 Prefix 已静默拒绝；其他故障 Actor 也只在首次异常后加入 fault set，
		// 不允许日志按 Actor 数量刷屏。
		if (_vegetativeUnexpectedFaultLogged == 0)
		{
			_vegetativeUnexpectedFaultLogged = 1;
			try
			{
				string assetId = pActor?.asset?.id ?? "null";
				string city = pActor?.city?.data != null ? "yes" : "no";
				string kingdom = pActor?.kingdom?.data != null ? "yes" : "no";
				string tile = ((BaseSimObject)pActor)?.current_tile != null ? "yes" : "no";
				UnityEngine.Debug.LogWarning("[玄鉴][原生生育隔离] 首次未知 vegetative NRE actor=" + actorId
					+ " asset=" + assetId + " tile=" + tile + " city=" + city + " kingdom=" + kingdom
					+ "；该 Actor 本世界后续 vegetative 事务已 fail-closed，同型故障不再重复打印。");
			}
			catch { }
		}
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Actor), "b2_checkCurrentEnemyTarget")]
	private static bool CheckCurrentEnemyTargetPrefix(Actor __instance)
	{
		// b2 is queried constantly. The dangerous case is a stale/non-null attack
		// target; actors with no target at all can stay entirely on the native path.
		if (__instance != null)
		{
			try
			{
				if (!__instance.has_attack_target && __instance.attack_target == null) return true;
			}
			catch { }
		}
		return XjActorAggroBridge.NormalizeCurrentTargetForNativeCheck(__instance);
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "b2_checkCurrentEnemyTarget")]
	private static Exception CheckCurrentEnemyTargetFinalizer(Actor __instance, Exception __exception)
	{
		if (__exception == null) return null;
		if (__exception is not NullReferenceException) return __exception;
		XjActorAggroBridge.ClearTargets(__instance, stopMovement: false, clearRetaliation: false);
		XjExceptionDiagnostics.Report("NativeAI.b2_checkCurrentEnemyTarget", __exception);
		return null;
	}
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "u1_checkInside")]
	private static bool CheckInsideReferenceSafetyPrefix(Actor __instance)
	{
		if (__instance == null) return false;
		// The vast majority of actors are neither inside a boat nor a building.
		// Avoid entering the try/catch normalization helper on every u1 AI check.
		if (!__instance.is_inside_boat && !__instance.is_inside_building) return true;
		NormalizeInsideReferences(__instance);
		return true;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyMaker), "newImmediateBabySpawn", new Type[] { typeof(Actor), typeof(Actor) })]
	private static bool ImmediateBabySpawnReferenceSafetyPrefix(Actor __0, Actor __1)
	{
		if (!IsValidReproductionParent(__0) || !IsValidReproductionParent(__1)) return false;
		// newImmediateBabySpawn 同样会在 makeBaby 返回后继续操作 child。把玄鉴可预测的
		// 高境生育拒绝前移到事务入口，避免把 NullReferenceException 当成正常控制流。
		return !XjHighRealmBirthPatches.ShouldBlockBirth(__0, __1);
	}


	[HarmonyTranspiler]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BabyMaker), "newImmediateBabySpawn", new Type[] { typeof(Actor), typeof(Actor) })]
	private static IEnumerable<CodeInstruction> ImmediateBabySpawnNullChildTranspiler(
		IEnumerable<CodeInstruction> instructions, ILGenerator generator, MethodBase __originalMethod)
	{
		return InjectNullChildEarlyReturn(instructions, generator, __originalMethod,
			"NativeAI.BabyMaker.newImmediateBabySpawn");
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(BabyMaker), "newImmediateBabySpawn", new Type[] { typeof(Actor), typeof(Actor) })]
	private static Exception ImmediateBabySpawnReferenceSafetyFinalizer(Exception __exception)
	{
		if (__exception == null) return null;
		if (__exception is not NullReferenceException) return __exception;
		// 生育事务遇到“已经离界/移除但仍在本批次队列”的旧引用时直接终止本次事务，
		// 不允许同一无效单位在BatchActors里每帧刷出成千上万条空引用。
		XjExceptionDiagnostics.Report("NativeAI.BabyMaker.newImmediateBabySpawn", __exception);
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "updateParallelChecks")]
	private static Exception UpdateParallelChecksStaleActorFinalizer(Actor __instance, Exception __exception)
	{
		if (__exception == null) return null;
		if (__exception is not NullReferenceException && __exception is not IndexOutOfRangeException) return __exception;
		XjActorAggroBridge.ClearTargets(__instance, stopMovement: false, clearRetaliation: false);
		XjExceptionDiagnostics.Report("NativeParallel.Actor.updateParallelChecks", __exception);
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(BehCityActorCheckAttack), "isAttackingZoneAvailable")]
	private static bool CityAttackZonePrefix(Actor pActor, TileZone pAttackZone, City pAttackCity, ref bool __result)
	{
		if (IsCityAttackContextUsable(pActor, pAttackZone, pAttackCity)) return true;
		__result = false;
		TryResetAttackBehaviour(pActor, "NativeAI.BehCityActorCheckAttack.InvalidContext");
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(BehCityActorCheckAttack), "isAttackingZoneAvailable")]
	private static Exception CityAttackZoneFinalizer(Actor pActor, Exception __exception, ref bool __result)
	{
		if (__exception == null) return null;
		if (__exception is not NullReferenceException) return __exception;
		__result = false;
		TryResetAttackBehaviour(pActor, "NativeAI.BehCityActorCheckAttack.NRE");
		XjExceptionDiagnostics.Report("NativeAI.BehCityActorCheckAttack.isAttackingZoneAvailable", __exception);
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "b6_updateAI")]
	private static Exception ActorUpdateAiCityAttackFinalizer(Actor __instance, Exception __exception)
	{
		if (__exception == null) return null;
		if (!IsCityAttackBehaviourFault(__exception)) return __exception;
		TryResetAttackBehaviour(__instance, "NativeAI.Actor.b6_updateAI.CityAttack");
		XjExceptionDiagnostics.Report("NativeAI.Actor.b6_updateAI.BehCityActorCheckAttack", __exception);
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(WindowMetaElementBase), "OnEnable")]
	private static bool XuanJianOwnedStatsCloneOnEnablePrefix(object __instance)
	{
		// 只处理玄鉴旧版本/热重载遗留的 UnitStatsElement 克隆。正常原生属性元素、
		// 其他模组元素以及玄鉴当前已剥离 UnitStatsElement 的节点全部原样放行。
		if (__instance == null
			|| !string.Equals(__instance.GetType().Name, "UnitStatsElement", StringComparison.Ordinal)
			|| __instance is not UnityEngine.Component component
			|| component == null)
		{
			return true;
		}

		UnityEngine.Transform current = component.transform;
		bool ownedNodeSeen = false;
		for (int depth = 0; current != null && depth < 8; depth++, current = current.parent)
		{
			string nodeName = current.name ?? string.Empty;
			if (IsXuanJianOverviewStatsNode(nodeName))
			{
				ownedNodeSeen = true;
			}
			if (string.Equals(nodeName, "content_more_icons", StringComparison.Ordinal))
			{
				// 原生 UnitStatsElement 挂在 content_more_icons 根节点本身；它必须继续
				// 执行。任何挂在其子节点上的 UnitStatsElement 都只能是旧版玄鉴克隆：
				// 原生 showContent 会误把该子节点当作整块属性容器，随后找不到
				// i_lifespan 而在 UnitStatsElement.cs:64 空引用。这里按层级而非
				// 自定义节点名识别，兼容热重载遗留的改名节点。
				return ReferenceEquals(current, component.transform);
			}
		}

		// A copied overview stat node is unsafe regardless of which native parent
		// it ended up under after a hot reload.  Never suppress the native element
		// unless an explicitly named 玄鉴 node was encountered.
		return !ownedNodeSeen;
	}

	private static bool IsXuanJianOverviewStatsNode(string name)
	{
		return string.Equals(name, "XjOverviewCoreStatsRow", StringComparison.Ordinal)
			|| string.Equals(name, "XjOverviewCombatStatsRow1", StringComparison.Ordinal)
			|| string.Equals(name, "XjOverviewCombatStatsRow2", StringComparison.Ordinal)
			|| string.Equals(name, "XjOverviewCombatStatsRow3", StringComparison.Ordinal)
			|| string.Equals(name, "MingShu", StringComparison.Ordinal)
			|| string.Equals(name, "HuiGuang", StringComparison.Ordinal)
			|| string.Equals(name, "ZhenYuan", StringComparison.Ordinal)
			|| string.Equals(name, "XjArmorPen", StringComparison.Ordinal)
			|| string.Equals(name, "XjTrueDamage", StringComparison.Ordinal)
			|| string.Equals(name, "XjAccuracy", StringComparison.Ordinal)
			|| string.Equals(name, "XjCrit", StringComparison.Ordinal)
			|| string.Equals(name, "XjAttackSpeed", StringComparison.Ordinal)
			|| string.Equals(name, "XjSameRealmDamage", StringComparison.Ordinal)
			|| string.Equals(name, "XjShieldBreak", StringComparison.Ordinal)
			|| string.Equals(name, "XjLifesteal", StringComparison.Ordinal)
			|| string.Equals(name, "XjDamageReduction", StringComparison.Ordinal)
			|| string.Equals(name, "XjHealthShield", StringComparison.Ordinal)
			|| string.Equals(name, "XjDodge", StringComparison.Ordinal)
			|| string.Equals(name, "XjCritTakenReduction", StringComparison.Ordinal)
			|| string.Equals(name, "XjHealback", StringComparison.Ordinal)
			|| string.Equals(name, "XjBreakthrough", StringComparison.Ordinal);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ActorManager), "destroyObject")]
	private static bool ActorManagerDestroyObjectProtectionPrefix(Actor pActor, out bool __state)
	{
		// destroyObject 已是 WorldBox 的最终销毁边界。道胎/转世的死亡保护已经在
		// Actor.die / dieAndDestroy / 扣血入口完成；这里再“救回”会让原生对象进入
		// 半销毁状态，后续 BatchActors.updateVisibility 可能持续读到残缺引用。
		// 保留原有两项无争议保护：第三方显式替换事务要完整通过；已经只剩空壳的
		// 队列项没有可执行的原生销毁语义，继续进入只会重复 NRE。除此之外全部放行。
		// 真正进入最终销毁边界时，道胎已经没有可靠的原生回退点。这里只做
		// 只读快照并延后一帧重塑，绝不在 destroyObject 的半销毁事务中 spawn。
		XjDaoTaiResurrectionSystem.QueueFinalDestruction(pActor);
		__state = XjExternalUnitTransferContext.IsExplicitReplacementRemoval(pActor);
		if (__state) return true;
		if (pActor == null || pActor.data == null) return false;
		return true;
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(ActorManager), "destroyObject")]
	private static Exception ActorManagerDestroyObjectFinalizer(Actor pActor, bool __state, Exception __exception)
	{
		if (__state)
		{
			XjExternalUnitTransferContext.CompleteExplicitReplacementRemoval(pActor);
		}
		if (__exception == null) return null;
		if (__exception is not NullReferenceException) return __exception;
		// destroyObject 已经进入原生移除事务后出现半销毁引用时，向上抛会让
		// checkObjectsToDestroy 同一条目逐帧重试。只隔离确认过的 NRE，让队列完成消费。
		XjActorAggroBridge.ClearTargets(pActor, stopMovement: false, clearRetaliation: false);
		XjExceptionDiagnostics.Report("NativeLifecycle.ActorManager.destroyObject", __exception);
		return null;
	}

	private static bool IsCityAttackContextUsable(Actor actor, TileZone attackZone, City attackCity)
	{
		try
		{
			if (actor?.data == null || actor.asset == null || !actor.isAlive()) return false;
			if (actor.city?.data == null || actor.kingdom?.data == null || actor.city.kingdom?.data == null) return false;
			if (((BaseSimObject)actor).current_tile == null) return false;
			if (attackZone == null || attackCity?.data == null || attackCity.kingdom?.data == null) return false;
			City zoneCity = attackZone.city;
			if (zoneCity?.data == null || zoneCity.kingdom?.data == null) return false;
			if (!ReferenceEquals(zoneCity, attackCity)) return false;
			if (attackZone.centerTile == null && (attackZone.tiles == null || attackZone.tiles.Length == 0)) return false;
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("NativeAI.BehCityActorCheckAttack.Validate", ex);
			return false;
		}
	}

	private static bool IsCityAttackBehaviourFault(Exception exception)
	{
		if (exception is not NullReferenceException) return false;
		try
		{
			string declaringType = exception.TargetSite?.DeclaringType?.FullName ?? string.Empty;
			if (declaringType.IndexOf("BehCityActorCheckAttack", StringComparison.Ordinal) >= 0) return true;
			return (exception.StackTrace ?? string.Empty).IndexOf("BehCityActorCheckAttack", StringComparison.Ordinal) >= 0;
		}
		catch { return false; }
	}

	private static void TryResetAttackBehaviour(Actor actor, string context)
	{
		if (actor == null) return;
		XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
		try { actor.cancelAllBeh(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report(context + ".CancelBehaviours", ex); }
		try { actor.clearOldPath(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report(context + ".ClearPath", ex); }
	}


	private static IEnumerable<CodeInstruction> InjectNullChildEarlyReturn(
		IEnumerable<CodeInstruction> instructions,
		ILGenerator generator,
		MethodBase originalMethod,
		string context)
	{
		List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
		if (originalMethod is not MethodInfo owner || owner.ReturnType != typeof(void) || generator == null)
		{
			return codes;
		}

		for (int i = 0; i < codes.Count; i++)
		{
			if ((codes[i].opcode != OpCodes.Call && codes[i].opcode != OpCodes.Callvirt)
				|| codes[i].operand is not MethodInfo called
				|| called.DeclaringType != typeof(BabyMaker)
				|| !string.Equals(called.Name, "makeBaby", StringComparison.Ordinal)
				|| called.ReturnType != typeof(Actor))
			{
				continue;
			}

			Label hasChild = generator.DefineLabel();
			CodeInstruction dup = new CodeInstruction(OpCodes.Dup);
			CodeInstruction branch = new CodeInstruction(OpCodes.Brtrue, hasChild);
			CodeInstruction pop = new CodeInstruction(OpCodes.Pop);
			CodeInstruction ret = new CodeInstruction(OpCodes.Ret);
			CodeInstruction continuation = i + 1 < codes.Count ? codes[i + 1] : null;
			if (continuation == null) return codes;
			continuation.labels.Add(hasChild);
			codes.InsertRange(i + 1, new[] { dup, branch, pop, ret });
			return codes;
		}

		try { UnityEngine.Debug.LogWarning("[玄鉴][兼容层] 未在 " + context + " 找到 BabyMaker.makeBaby 调用，null-child 防护未注入。"); }
		catch { }
		return codes;
	}

	private static void EnsureVegetativeFaultWorld()
	{
		int token = 0;
		try
		{
			if (World.world != null) token = RuntimeHelpers.GetHashCode(World.world);
		}
		catch { }
		if (token == _vegetativeFaultWorldToken) return;
		_vegetativeFaultWorldToken = token;
		VegetativeFaultedActorIds.Clear();
		_vegetativeUnexpectedFaultLogged = 0;
	}

	private static bool IsKnownUnsafeVegetativeContext(Actor actor)
	{
		if (actor?.data == null) return true;
		try
		{
			// 739+ 年实机样本全部为 garl：tile/kingdom 有效但 city 已丢失。
			// 这是完成 taking_roots 后的断裂原生文明归属，不是一次成功生育。
			return string.Equals(actor.asset?.id, "garl", StringComparison.Ordinal)
				&& actor.kingdom?.data != null
				&& actor.city?.data == null;
		}
		catch
		{
			return true;
		}
	}

	private static bool IsValidReproductionParent(Actor actor)
	{
		if (actor?.data == null) return false;
		try
		{
			if (!actor.isAlive() || actor.asset == null) return false;
			NormalizeInsideReferences(actor);
			return ((BaseSimObject)actor).current_tile != null;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("NativeAI.BabyMaker.ValidateParent", ex);
			return false;
		}
	}

	private static bool IsValidBirthActor(Actor actor)
	{
		if (actor?.data == null) return false;
		try
		{
			if (!actor.isAlive() || actor.asset == null) return false;
			NormalizeInsideReferences(actor);
			return ((BaseSimObject)actor).current_tile != null;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("NativeStatus.BabyMaker.ValidateBirthActor", ex);
			return false;
		}
	}

	private static void NormalizeInsideReferences(Actor actor)
	{
		if (actor == null) return;
		try
		{
			if (actor.is_inside_boat)
			{
				if (actor.inside_boat == null
					|| actor.inside_boat.actor == null
					|| actor.inside_boat.actor.current_tile == null)
				{
					actor.is_inside_boat = false;
					actor.inside_boat = null;
				}
			}
			if (actor.is_inside_building && actor.inside_building == null)
				actor.is_inside_building = false;
		}
		catch (Exception ex)
		{
			// 引用链本身已经被原生移除时，宁可清理inside状态，也不能继续让原生方法解引用。
			try
			{
				actor.is_inside_boat = false;
				actor.inside_boat = null;
				if (actor.inside_building == null) actor.is_inside_building = false;
			}
			catch (Exception cleanupEx)
			{
				XjExceptionDiagnostics.Report("NativeAI.Actor.u1_checkInside.CleanupFallback", cleanupEx);
			}
			XjExceptionDiagnostics.Report("NativeAI.Actor.u1_checkInside.Normalize", ex);
		}
	}

}
