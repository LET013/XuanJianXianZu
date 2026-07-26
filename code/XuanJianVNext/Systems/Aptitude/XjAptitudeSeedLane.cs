using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Aptitude;

/// <summary>
/// Event-driven seed and age-five aptitude lane.
/// Actor callbacks enqueue work; runtime ticks drain a bounded amount.
/// </summary>
internal static class XjAptitudeSeedLane
{
	private static readonly Queue<long> PendingActorIds = new Queue<long>();
	private static readonly HashSet<long> PendingActorIdSet = new HashSet<long>();
	private static readonly HashSet<long> CompletedActorIds = new HashSet<long>();

	internal static int PendingCount => PendingActorIds.Count;

	internal static bool IsComplete(long actorId)
	{
		return actorId > 0L && CompletedActorIds.Contains(actorId);
	}

	internal static void Forget(long actorId)
	{
		if (actorId <= 0L)
		{
			return;
		}

		PendingActorIdSet.Remove(actorId);
		CompletedActorIds.Remove(actorId);
	}

	internal static void Enqueue(long actorId)
	{
		if (actorId <= 0L || !PendingActorIdSet.Add(actorId))
		{
			return;
		}

		PendingActorIds.Enqueue(actorId);
	}

	/// <summary>
	/// 五岁资质判定是一次性的出生门槛，不能等待普通限额队列。
	/// 仅在原生 updateAge 刚把年龄推进到五岁时调用；初始投放的成年角色
	/// 与任何已超过窗口的角色都不会被补发资质。
	/// </summary>
	internal static bool TryProcessAgeFiveDeadline(Actor actor)
	{
		if (actor?.data == null
			|| !actor.isAlive()
			|| !XjCultivationEligibility.CanCultivate(actor))
		{
			return false;
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		if (!XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear))
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzCheckedAge5, out int checkedAgeFive);
		if (checkedAgeFive != 0)
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		// 与普通种子通道复用同一套初始化和概率规则，仅提升五岁窗口的时效性。
		XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
		XjCultivationSeed.EnsureSeedState(actor);
		XjAptitudeExecuteResult result = XjAptitudeLocalExecutor.TryRunAgeFiveCheck(actor);
		if (!result.Checked)
		{
			return false;
		}

		PendingActorIdSet.Remove(actorId);
		CompletedActorIds.Add(actorId);
		if (result.Granted)
		{
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.AptitudeGranted(actor));
		}
		XjCultivatorCache.CheckAndUpdate(actor);
		return true;
	}

	internal static bool NeedsProcessing(Actor actor)
	{
		return actor?.data != null
			&& XjCultivationEligibility.CanCultivate(actor)
			&& NeedsProcessingEligibleActor(actor);
	}

	/// <summary>
	/// 已通过修炼资格门控后的轻量检查。供年度注册入口复用，避免同一
	/// updateAge 回调重复读取种族/脑域 trait。
	/// </summary>
	internal static bool NeedsProcessingEligibleActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		bool hasSeedState = XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float mingShu) && mingShu > 0f
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang) && huiGuang > 0f
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out _);
		if (!hasSeedState)
		{
			return true;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzCheckedAge5, out int checkedAgeFive)
			&& checkedAgeFive != 0)
		{
			return false;
		}

		// Five-year aptitude has a synchronous deadline path in the scheduler.
		// Before that deadline, seeded children should not re-enter this bounded
		// lane every year just because the final roll has not matured yet.
		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		return ageYear >= 5;
	}

	internal static void Tick(int budget, double maxMilliseconds)
	{
		int remaining = Math.Max(0, budget);
		long started = Stopwatch.GetTimestamp();
		while (remaining-- > 0 && PendingActorIds.Count > 0)
		{
			long actorId = PendingActorIds.Dequeue();
			// 五岁临界角色可能已由 updateAge 的保底路径完成。队列节点无须
			// O(n) 删除，出队时丢弃即可，避免再次执行一整套种子初始化。
			if (!PendingActorIdSet.Remove(actorId))
			{
				continue;
			}
			if (!XjActorRegistry.Resolve(actorId, out Actor actor)
				|| actor?.data == null
				|| !actor.isAlive()
				|| !XjCultivationEligibility.CanCultivate(actor))
			{
				continue;
			}

			ProcessActor(actor);
			if (maxMilliseconds > 0.0
				&& (Stopwatch.GetTimestamp() - started) > maxMilliseconds * Stopwatch.Frequency / 1000.0)
			{
				break;
			}
		}
	}

	internal static void Clear()
	{
		PendingActorIds.Clear();
		PendingActorIdSet.Clear();
		CompletedActorIds.Clear();
	}

	private static void ProcessActor(Actor actor)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		bool shouldPrepareFamily = ShouldPrepareFamilyBeforeAptitude(actor);
		if (shouldPrepareFamily)
		{
			XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
		}

		if (!HasCompleteSeedState(actor))
		{
			XjCultivationSeed.EnsureSeedState(actor);
		}
		XjAptitudeExecuteResult aptitudeResult = XjAptitudeLocalExecutor.TryRunAgeFiveCheck(actor);
		if (aptitudeResult.Granted)
		{
			if (!shouldPrepareFamily)
			{
				XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
			}
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.AptitudeGranted(actor));
		}

		if (XjCultivatorCache.CheckAndUpdate(actor))
		{
			XjScheduler.EnsureRuntimeIndexesForActor(actor);
			XjScheduler.EnqueueAnnualActor(actor);
		}
		// Birth/early-life seeding is not the completion boundary. The actor must
		// be eligible for re-enqueue until the age-five roll actually executes.
		if (aptitudeResult.Checked)
		{
			CompletedActorIds.Add(actorId);
		}
	}

	private static bool ShouldPrepareFamilyBeforeAptitude(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)
		{
			return true;
		}

		if (XjCultivationEligibility.HasExplicitCultivationGrant(actor)
			|| XjCultivationEligibility.HasCultivationMarkers(actor))
		{
			return true;
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		return XjAptitudeRuleEvaluator.IsAgeFiveEligibilityWindow(ageYear);
	}

	private static bool HasCompleteSeedState(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float mingShu) && mingShu > 0f
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang) && huiGuang > 0f
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out _);
	}
}
