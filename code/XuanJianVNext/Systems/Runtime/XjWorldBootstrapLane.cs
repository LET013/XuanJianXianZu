using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// One-time, bounded runtime-index rebuild after a world load. A cold-path world
/// unit snapshot is permitted here; all hot paths continue to use incremental
/// indexes. This lane never executes annual progression.
/// </summary>
internal static class XjWorldBootstrapLane
{
	private static IReadOnlyList<Actor> Actors = Array.Empty<Actor>();
	private static int _cursor;
	private static int _baselineYear;
	private static bool _annalsBaseInitialized;
	private static bool _historyBaseRepaired;

	internal static bool HasPending => (_baselineYear > 0 && (!_annalsBaseInitialized || !_historyBaseRepaired)) || _cursor < Actors.Count;

	internal static void ScheduleAfterLoad(int baselineYear)
	{
		if (!XjRealmTitleNameLibrary.AuditExactDaoTuTitleProfiles(out string titleAuditError))
		{
			UnityEngine.Debug.LogError("[玄鉴][尊号字库] " + titleAuditError);
		}
		_cursor = 0;
		XjLongShuSystem.BeginBootstrapTracking();
		_baselineYear = Math.Max(0, baselineYear);
		_annalsBaseInitialized = false;
		_historyBaseRepaired = false;
		if (_baselineYear > 0 && XjWorldArchiveSystem.HasLoadedArchive)
		{
			XjCenturyAnnalsStore.TryEnsureBaseWorldYear(_baselineYear, out _);
			_annalsBaseInitialized = true;
			XjWorldHistoryStore.PruneRecordsBeforeYear(_baselineYear);
			XjWorldHistoryStore.PruneSuppressedRecords();
			_historyBaseRepaired = true;
		}
		IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
		Actors = units ?? Array.Empty<Actor>();
	}

	internal static void Tick(int budget, double maxMilliseconds)
	{
		if (budget <= 0 || !HasPending)
		{
			return;
		}

		// 部分 WorldBox 版本会在 finishingUpLoading 之后才挂好
		// map_stats.custom_data。必须先确认归档已读取，再开始重建真君录、
		// 家族纪事和法宝；否则空内存会先生成假快照，随后又被迟到的
		// 归档导入覆盖，表现为读档后记录反复消失。
		XjWorldArchiveSystem.LoadReadOnly();
		if (!XjWorldArchiveSystem.HasLoadedArchive)
		{
			return;
		}

		if (!_annalsBaseInitialized && _baselineYear > 0)
		{
			// 百年世谱起点必须是本局加载完成时的初始世界年，而不是第一次
			// 出现家族事件或第一次跨年结算的年份。旧档已有起点时此调用只读不改。
			XjCenturyAnnalsStore.TryEnsureBaseWorldYear(_baselineYear, out _);
			_annalsBaseInitialized = true;
			XjWorldHistoryStore.PruneRecordsBeforeYear(_baselineYear);
			XjWorldHistoryStore.PruneSuppressedRecords();
			_historyBaseRepaired = true;
		}

		long started = Stopwatch.GetTimestamp();
		int processed = 0;
		while (_cursor < Actors.Count && processed < budget)
		{
			Actor actor = Actors[_cursor++];
			processed++;
			if (actor?.data != null && actor.isAlive())
			{
				// 旧档首次载入时立即补写“紫府前未悟器意”的永久封锁，
				// 防止境界回退、旁路恢复或当年结算顺序重新开放刀意、枪意、弓意、剑意。
				XjWeaponArtSystem.OnRealmChanged(actor);
				// 冷路径允许做一次权威重建：真君录、权柄、家族纪事和已有
				// 法宝均在此恢复，不能只建一个只读显示缓存。
				if (!XjHighRealmRehydration.ReconcileActor(actor))
				{
					XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
				}
				if (!XjXuanJianShenTongSpecials.ReconcileYiDuiYingMirrorOnLoad(actor)
					&& XjCultivationEligibility.CanCultivate(actor))
				{
					XjScheduler.RegisterActorForBootstrap(actor, _baselineYear);
				}
			}

			if ((processed & 15) == 0
				&& maxMilliseconds > 0d
				&& Stopwatch.GetTimestamp() - started > maxMilliseconds * Stopwatch.Frequency / 1000d)
			{
				break;
			}
		}

		if (_cursor >= Actors.Count)
		{
			XjLongShuSystem.CompleteBootstrapTracking();
			Actors = Array.Empty<Actor>();
			_cursor = 0;
			_baselineYear = 0;
			_annalsBaseInitialized = false;
			_historyBaseRepaired = false;
		}
	}

	internal static void Clear()
	{
		Actors = Array.Empty<Actor>();
		_cursor = 0;
		_baselineYear = 0;
		_annalsBaseInitialized = false;
		_historyBaseRepaired = false;
	}
}
