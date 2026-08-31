using System;
using System.Collections.Generic;
using ai;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjHighRealmMovement
{
	private const string FlightFlag = "xuanjian.vnext.realm_flight";
	private const float FlightHeight = 6f;
	private const int ZiFuLongTravelMinDistance = 36;
	private const int JinDanLongTravelMinDistance = 28;
	private const float ZiFuTravelChance = 0.72f;
	private const float JinDanTravelChance = 0.92f;
	private const float ZiFuTravelCooldownSeconds = 12f;
	private const float JinDanTravelCooldownSeconds = 7f;
	private const int ZiFuCombatMinDistance = 24;
	private const int JinDanCombatMinDistance = 20;
	private const float ZiFuCombatChance = 0.65f;
	private const float JinDanCombatChance = 0.85f;
	private const float ZiFuCombatCooldownSeconds = 7f;
	private const float JinDanCombatCooldownSeconds = 4.5f;
	private const float AttemptGapSeconds = 0.75f;
	private const int MaximumRuntimeEntries = 2048;
	private const int NativePathFailureBackoffFrames = 90;
	private const int SanctuaryPathFailureBackoffFrames = 180;
	private const int NativePathFailureWarningIntervalFrames = 300;
	private static readonly Dictionary<long, float> LastTravelTimeByActorId = new Dictionary<long, float>(256);
	private static readonly Dictionary<long, float> LastCombatTimeByActorId = new Dictionary<long, float>(256);
	private static readonly Dictionary<long, float> NextAttemptTimeByActorId = new Dictionary<long, float>(256);
	private static readonly Dictionary<long, int> NativePathFailureUntilFrameByActorId = new Dictionary<long, int>(128);
	private static int LastNativePathFailureWarningFrame = -10000;

	internal static bool TryHandleGoTo(Actor actor, WorldTile target, ref ExecuteEvent result)
	{
		if (actor?.data == null || target == null) { ClearFlight(actor); return false; }
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier) || !actor.isAlive()) return false;

		if (realmTier >= XjRealmSuppression.TierJinDan)
		{
			ClearFlight(actor);
			// 金丹只在“太虚横渡”真正触发时接管一次位移。普通短途/未触发横渡
			// 一律交回 WorldBox 原生 goTo，避免玄鉴用 spawnOn 取代日常寻路生命周期。
			return TryTaiXuTraverse(actor, target, actorId, realmTier, ref result);
		}
		if (realmTier != XjRealmSuppression.TierZiFu) { ClearFlight(actor); return false; }
		if (TryTaiXuTraverse(actor, target, actorId, realmTier, ref result)) return true;

		// 紫府只表达“飞行能力”这一玄鉴规则，不再自己伪造 current_path。
		// 目标选择、路径生成、路径替换和抵达事务全部由 WorldBox 原生 goTo 维护。
		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag))
		{
			data.addFlag(FlightFlag);
			actor.setFlying(true);
			actor.precalcMovementSpeed(true);
		}
		return false;
	}

	private static bool TryTaiXuTraverse(Actor actor, WorldTile target, long actorId, int realmTier, ref ExecuteEvent result)
	{
		if (actorId <= 0L) return false;
		bool combatMove = actor.attackedBy != null;
		int minDistance = combatMove
			? (realmTier >= XjRealmSuppression.TierJinDan ? JinDanCombatMinDistance : ZiFuCombatMinDistance)
			: (realmTier >= XjRealmSuppression.TierJinDan ? JinDanLongTravelMinDistance : ZiFuLongTravelMinDistance);
		if (!IsLongTravel(actor, target, minDistance)) return false;
		float now = Time.time;
		if (NextAttemptTimeByActorId.TryGetValue(actorId, out float nextAttempt) && now < nextAttempt) return false;
		NextAttemptTimeByActorId[actorId] = now + AttemptGapSeconds;
		float cooldown = combatMove
			? (realmTier >= XjRealmSuppression.TierJinDan ? JinDanCombatCooldownSeconds : ZiFuCombatCooldownSeconds)
			: (realmTier >= XjRealmSuppression.TierJinDan ? JinDanTravelCooldownSeconds : ZiFuTravelCooldownSeconds);
		Dictionary<long, float> cooldownMap = combatMove ? LastCombatTimeByActorId : LastTravelTimeByActorId;
		if (cooldownMap.TryGetValue(actorId, out float lastUse) && now - lastUse < cooldown) return false;
		float chance = combatMove
			? (realmTier >= XjRealmSuppression.TierJinDan ? JinDanCombatChance : ZiFuCombatChance)
			: (realmTier >= XjRealmSuppression.TierJinDan ? JinDanTravelChance : ZiFuTravelChance);
		if (UnityEngine.Random.value > chance) return false;
		if (!TryImmediateTeleport(actor, target)) return false;
		if (combatMove) LastCombatTimeByActorId[actorId] = now;
		else LastTravelTimeByActorId[actorId] = now;
		TrimRuntimeStateIfNeeded();
		result = ExecuteEvent.True;
		return true;
	}

	/// <summary>
	/// 金丹/真君高境位移的实体瞬移语义。所有真正的跨格搬迁都通过原生 spawnOn
	/// 重挂地图实体关系；普通瞬移保留当前AI行为，领域边界则使用专门的
	/// TryImmediateDomainTransfer 终止旧任务后再转移。
	/// </summary>
	internal static bool IsSafeTeleportDestination(WorldTile target)
	{
		if (target?.chunk == null) return false;
		try
		{
			return target.Type != null && !target.Type.ocean && !target.Type.liquid && !target.Type.lava;
		}
		catch
		{
			return false;
		}
	}

	internal static bool TryImmediateTeleportSafe(Actor actor, WorldTile target)
	{
		return IsSafeTeleportDestination(target) && TryImmediateTeleport(actor, target);
	}

	internal static bool TryImmediateTeleport(Actor actor, WorldTile target)
	{
		return TryNativeRelocate(actor, target, showEffect: true, resetBehaviour: false);
	}

	/// <summary>
	/// 领域边界专用迁移。玄门道界/幽冥的稳定点在于：领域进出属于一次完整
	/// 实体转移，不让原生 BehGoToTileTarget 带着旧任务继续追逐跨域目标。
	/// 因此这里在 spawnOn 前终止当前行为与路径；普通金丹战斗瞬移仍走上面的
	/// TryImmediateTeleport，不会因此丢失正常战斗目标。
	/// </summary>
	internal static bool TryImmediateDomainTransfer(Actor actor, WorldTile target)
	{
		return TryNativeRelocate(actor, target, showEffect: true, resetBehaviour: true);
	}

	/// <summary>旃檀林邻海错误寻路恢复：完整重挂 Region/Chunk，但不播放高境瞬移特效。</summary>
	internal static bool TryImmediateDomainRecovery(Actor actor, WorldTile target)
	{
		return IsSafeTeleportDestination(target)
			&& TryNativeRelocate(actor, target, showEffect: false, resetBehaviour: true);
	}

	private static bool TryNativeRelocate(Actor actor, WorldTile target, bool showEffect, bool resetBehaviour)
	{
		if (actor?.data == null || target?.chunk == null || target.Type == null) return false;
		try
		{
			ClearFlight(actor);
			if (resetBehaviour)
			{
				try { actor.cancelAllBeh(); }
				catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjHighRealmMovement.DomainTransfer.CancelBeh", ex); }
			}
			// stopMovement/cancelAllBeh 让原生状态机自行作废当前路径。不要再直接改
			// current_path；那会让玄鉴成为 RegionPathFinder 之外的第二个路径写者。
			try { actor.stopMovement(); }
			catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjHighRealmMovement.Relocate.Stop", ex); }
			if (showEffect)
			{
				try { ActionLibrary.teleportEffect(actor, target); }
				catch (System.Exception ex)
				{
					XjExceptionDiagnostics.Report("XjHighRealmMovement.Relocate.Effect", ex);
				}
			}

			// spawnOn 是原生“重新落格”入口，会同步实体与地图区域关系。直接调用
			// setCurrentTilePosition 容易留下 RegionPathFinder 看见的半更新状态。
			actor.spawnOn(target);
			return ((BaseSimObject)actor).current_tile != null;
		}
		catch (System.Exception ex)
		{
			XjExceptionDiagnostics.Report(resetBehaviour
				? "XjHighRealmMovement.TryImmediateDomainTransfer"
				: "XjHighRealmMovement.TryImmediateTeleport", ex);
			return false;
		}
	}


	/// <summary>
	/// 原生 RegionPathFinder 不能处理缺失当前格/Chunk 的实体，也不能安全接收
	/// 已经脱离地图 Chunk 的目标格。此门只做 O(1) 端点完整性检查，在真正进入
	/// 原生 goTo 之前截断已损坏的旧档/第三方目标，避免 finalPath 才空引用。
	/// </summary>
	internal static bool TryBlockInvalidNativePathEndpoint(Actor actor, WorldTile target, ref ExecuteEvent result)
	{
		if (actor?.data == null) return false;
		WorldTile current = ((BaseSimObject)actor).current_tile;
		if (current?.chunk != null && target?.chunk != null) return false;
		RecoverNativePathFailure(actor, target, sanctuaryRelated: false);
		result = ExecuteEvent.True;
		return true;
	}

	internal static bool TryBlockNativePathDuringBackoff(Actor actor, ref ExecuteEvent result)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !NativePathFailureUntilFrameByActorId.TryGetValue(actorId, out int untilFrame)) return false;
		int frame = Time.frameCount;
		if (frame >= untilFrame)
		{
			NativePathFailureUntilFrameByActorId.Remove(actorId);
			return false;
		}
		try { actor.stopMovement(); } catch { }
		result = ExecuteEvent.True;
		return true;
	}

	internal static void RecoverNativePathFailure(Actor actor, WorldTile target, bool sanctuaryRelated)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int frame = Time.frameCount;
		if (actorId > 0L)
		{
			NativePathFailureUntilFrameByActorId[actorId] = frame
				+ (sanctuaryRelated ? SanctuaryPathFailureBackoffFrames : NativePathFailureBackoffFrames);
		}
		try { actor.stopMovement(); } catch { }
		try { actor.cancelAllBeh(); } catch { }

		// RegionPathFinder 的空引用若在同一批 AI 上反复发生，完整异常日志本身就会
		// 成为严重卡顿源。恢复仍逐个执行，提示只全局限频一次。
		if (frame - LastNativePathFailureWarningFrame >= NativePathFailureWarningIntervalFrames)
		{
			LastNativePathFailureWarningFrame = frame;
			Debug.LogWarning("[玄鉴][寻路保护] 已中止一条原生 RegionPathFinder 异常路线"
				+ (sanctuaryRelated ? "（旃檀林/领域边界相关）" : string.Empty)
				+ "，并短暂退避该角色的重复寻路。");
		}
		TrimRuntimeStateIfNeeded();
	}

	private static bool IsLongTravel(Actor actor, WorldTile target, int minDistance)
	{
		WorldTile origin;
		try { origin = ((BaseSimObject)actor).current_tile; } catch (System.Exception xjCaught107_3) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjHighRealmMovement.cs:107", xjCaught107_3);
			 origin = null; }
		return origin != null && target != null
			&& Toolbox.SquaredDistVec2(origin.pos, target.pos) >= minDistance * minDistance;
	}

	private static void TrimRuntimeStateIfNeeded()
	{
		if (LastTravelTimeByActorId.Count <= MaximumRuntimeEntries
			&& LastCombatTimeByActorId.Count <= MaximumRuntimeEntries
			&& NextAttemptTimeByActorId.Count <= MaximumRuntimeEntries
			&& NativePathFailureUntilFrameByActorId.Count <= MaximumRuntimeEntries) return;
		LastTravelTimeByActorId.Clear();
		LastCombatTimeByActorId.Clear();
		NextAttemptTimeByActorId.Clear();
		NativePathFailureUntilFrameByActorId.Clear();
	}

	internal static bool ShouldSkipFall(Actor actor)
	{
		if (actor?.data == null) { ClearFlight(actor); return false; }
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier)
			|| realmTier != XjRealmSuppression.TierZiFu || !actor.isAlive())
		{
			ClearFlight(actor);
			return false;
		}
		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag))
		{
			data.addFlag(FlightFlag);
			actor.setFlying(true);
			actor.precalcMovementSpeed(true);
		}
		actor.position_height = FlightHeight;
		return true;
	}

	/// <summary>
	/// 仅供领域/原生航运兼容判断玄鉴自己管理的紫府飞行状态。
	/// </summary>
	internal static bool IsManagedFlightActive(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		try { return ((BaseSystemData)actor.data).hasFlag(FlightFlag); }
		catch { return false; }
	}

	internal static void ReconcileFlightStateAfterRealmWrite(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjCultivatorCache.TryGetRealmTier(actorId, out int realmTier) || realmTier != XjRealmSuppression.TierZiFu) ClearFlight(actor);
	}

	internal static void ForgetActorRuntime(long actorId)
	{
		if (actorId <= 0L) return;
		LastTravelTimeByActorId.Remove(actorId);
		LastCombatTimeByActorId.Remove(actorId);
		NextAttemptTimeByActorId.Remove(actorId);
		NativePathFailureUntilFrameByActorId.Remove(actorId);
	}

	internal static void ClearRuntime()
	{
		LastTravelTimeByActorId.Clear();
		LastCombatTimeByActorId.Clear();
		NextAttemptTimeByActorId.Clear();
		NativePathFailureUntilFrameByActorId.Clear();
		LastNativePathFailureWarningFrame = -10000;
	}

	private static void ClearFlight(Actor actor)
	{
		if (actor?.data == null) return;
		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.hasFlag(FlightFlag)) return;
		data.removeFlag(FlightFlag);
		actor.setFlying(false);
		actor.precalcMovementSpeed(true);
	}
}
