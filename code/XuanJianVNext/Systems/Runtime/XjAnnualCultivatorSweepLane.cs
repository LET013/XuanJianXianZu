using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 年度修士补漏通道。原生 updateAge 仍是主要入口；本通道每年只遍历已经确认修士的一个稳定分片，
/// 在高倍速、低帧率或外部模组跳过年龄回调时，把漏掉的修士重新并入年度队列。
/// 八年轮换覆盖一次全体，不扫描全世界生物，也不直接执行修炼业务。
///
/// ID buffer is retained and refilled in-place; its high-water mark约为修士总数的八分之一。
/// </summary>
internal static class XjAnnualCultivatorSweepLane
{
	private static readonly List<long> ActorIds = new List<long>(1024);
	private static int _cursor;
	private static int _latestRequestedYear;
	private static int _passRequestedYear;
	private static bool _restartAfterPass;

	internal static bool HasPending => _cursor < ActorIds.Count || _restartAfterPass;
	internal static int PendingCount => Math.Max(0, ActorIds.Count - _cursor)
		+ (_restartAfterPass ? XjCultivatorCache.GetAnnualAuditIds(_latestRequestedYear).Count : 0);

	internal static void Schedule(int currentYear)
	{
		if (currentYear <= 0) return;
		if (currentYear > _latestRequestedYear)
		{
			_latestRequestedYear = currentYear;
		}

		if (ActorIds.Count == 0)
		{
			BeginPass();
		}
		else if (_latestRequestedYear > _passRequestedYear)
		{
			// 高倍速跨年时合并为最新年度；补漏审计不是玩法结算，不追缴中间年份。
			_restartAfterPass = true;
		}
	}

	internal static int Tick(int maxActors, double timeBudgetMs)
	{
		if (maxActors <= 0 || timeBudgetMs <= 0d) return 0;
		if (ActorIds.Count == 0)
		{
			if (!_restartAfterPass) return 0;
			_restartAfterPass = false;
			BeginPass();
		}

		int processed = 0;
		long started = Stopwatch.GetTimestamp();
		while (_cursor < ActorIds.Count && processed < maxActors)
		{
			long actorId = ActorIds[_cursor++];
			processed++;
			if (actorId > 0L
				&& XjScheduler.ResolveActor(actorId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive())
			{
				XjScheduler.EnqueueAnnualActorForSweep(actor, _latestRequestedYear);
			}

			if (ElapsedMilliseconds(started) >= timeBudgetMs)
			{
				break;
			}
		}

		if (_cursor >= ActorIds.Count)
		{
			ActorIds.Clear();
			_cursor = 0;
			_passRequestedYear = 0;
			if (_restartAfterPass)
			{
				_restartAfterPass = false;
				BeginPass();
			}
		}
		return processed;
	}

	internal static void Clear()
	{
		ActorIds.Clear();
		ActorIds.TrimExcess();
		_cursor = 0;
		_latestRequestedYear = 0;
		_passRequestedYear = 0;
		_restartAfterPass = false;
	}

	private static void BeginPass()
	{
		IReadOnlyList<long> ids = XjCultivatorCache.GetAnnualAuditIds(_latestRequestedYear);
		int count = ids?.Count ?? 0;
		ActorIds.Clear();
		if (count <= 0)
		{
			_cursor = 0;
			_passRequestedYear = _latestRequestedYear;
			return;
		}

		int capacityBefore = ActorIds.Capacity;
		if (ActorIds.Capacity < count)
		{
			ActorIds.Capacity = count;
		}
		for (int i = 0; i < count; i++) ActorIds.Add(ids[i]);
		XjStageZeroObservation.RecordIdSnapshotFill(
			"AnnualAudit",
			count,
			capacityBefore,
			ActorIds.Capacity);
		_cursor = 0;
		_passRequestedYear = _latestRequestedYear;
	}

	private static double ElapsedMilliseconds(long started)
	{
		return (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
	}
}
