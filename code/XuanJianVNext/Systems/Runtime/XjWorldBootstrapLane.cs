using System;
using System.Collections.Generic;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// One-time, bounded runtime-index rebuild after a world load. A cold-path world
/// unit snapshot is permitted here; all hot paths continue to use incremental
/// indexes. This lane never executes annual progression.
/// </summary>
internal static class XjWorldBootstrapLane
{
	private static IReadOnlyList<long> ActorIds = Array.Empty<long>();
	private static int _cursor;
	private static int _baselineYear;
	private static bool _archiveReadyPhaseCompleted = true;
	private static bool _timelineBaseInitialized;
	private static bool _historyBaseRepaired;
	private static bool _yuanZhaoTimelineReconciled;
	private static bool _ancientShiPhaseInitialized;
	private static bool _ancientShiPhaseActive;
	private static int _ancientShiCursor;
	private static bool _centuryBackfillPhaseInitialized;
	private static bool _centuryBackfillPhaseActive;

	internal static bool HasPending => !_archiveReadyPhaseCompleted
		|| (_baselineYear > 0 && (!_timelineBaseInitialized || !_historyBaseRepaired))
		|| (_baselineYear > 0 && !_yuanZhaoTimelineReconciled)
		|| _cursor < ActorIds.Count
		|| (_baselineYear > 0 && !_ancientShiPhaseInitialized)
		|| _ancientShiPhaseActive
		|| (_baselineYear > 0 && !_centuryBackfillPhaseInitialized)
		|| _centuryBackfillPhaseActive;

	internal static void ScheduleAfterLoad(int baselineYear)
	{
		if (!XjRealmTitleNameLibrary.AuditExactDaoTuTitleProfiles(out string titleAuditError))
		{
			UnityEngine.Debug.LogError("[玄鉴][尊号字库] " + titleAuditError);
		}
		_cursor = 0;
		_baselineYear = Math.Max(0, baselineYear);
		_archiveReadyPhaseCompleted = false;
		_timelineBaseInitialized = false;
		_historyBaseRepaired = false;
		_yuanZhaoTimelineReconciled = false;
		_ancientShiPhaseInitialized = false;
		_ancientShiPhaseActive = false;
		_ancientShiCursor = 0;
		_centuryBackfillPhaseInitialized = false;
		_centuryBackfillPhaseActive = false;
		if (_baselineYear > 0 && XjWorldArchiveSystem.HasLoadedArchive)
		{
			XjCenturyAnnalsStore.TryEnsureBaseWorldYear(_baselineYear, out int resolvedBaseWorldYear);
			_timelineBaseInitialized = true;
			XjWorldHistoryStore.PruneRecordsBeforeYear(resolvedBaseWorldYear);
			XjWorldHistoryStore.PruneSuppressedRecords();
			_historyBaseRepaired = true;
		}
		// finishingUpLoading is only the native ingress. Do not assume the live unit
		// collection has reached its stable post-load identity here; the bounded ID
		// snapshot is captured once the XuanJian archive-ready boundary is confirmed.
		ActorIds = Array.Empty<long>();
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
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			AbortAfterRejectedArchive();
			return;
		}

		if (!_archiveReadyPhaseCompleted)
		{
			int loadYear = Math.Max(0, Math.Max(_baselineYear, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)));
			XjRuntimeCadence.CompleteAfterArchiveLoad(loadYear);
			IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
			ActorIds = CaptureActorIds(units);
			_cursor = 0;
			XjLongShuSystem.BeginBootstrapTracking();
			XjSectLegacyMigration.BeginAfterLoad(loadYear);

			// Feature load callbacks are deliberately published only after archive import.
			// Their background work remains blocked by HasPending until actor/index
			// rehydration below reaches a stable bootstrap boundary.
			XjWorldLifecycleRegistry.PublishLoaded(loadYear);
			XjThreeBookDiagnostics.AuditAfterLoad(loadYear);
			_archiveReadyPhaseCompleted = true;
		}

		if (!_timelineBaseInitialized && _baselineYear > 0)
		{
			// 玄鉴起录年必须在加载完成后尽早固化；本次“载入年份”只用于恢复调度，
			// 绝不能作为世界史裁剪线。裁剪只认世谱解析出的权威起录年。
			XjCenturyAnnalsStore.TryEnsureBaseWorldYear(_baselineYear, out int resolvedBaseWorldYear);
			_timelineBaseInitialized = true;
			XjWorldHistoryStore.PruneRecordsBeforeYear(resolvedBaseWorldYear);
			XjWorldHistoryStore.PruneSuppressedRecords();
			_historyBaseRepaired = true;
		}

		if (!_yuanZhaoTimelineReconciled && _baselineYear > 0)
		{
			// RC8以前的渊照事件使用绝对玄历1000年。归档一旦加载，立即按仙鉴
			// 持久起录年修正一次，避免玩家在下一次跨年前先存档而把旧时间轴续写回去。
			XjYuanZhaoKongZhengEvent.ReconcileTimelineAfterLoad(_baselineYear);
			_yuanZhaoTimelineReconciled = true;
		}

		long started = Stopwatch.GetTimestamp();
		int processed = 0;
		while (_cursor < ActorIds.Count && processed < budget)
		{
			long actorId = ActorIds[_cursor++];
			XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor);
			processed++;
			if (actor?.data != null && actor.isAlive())
			{
				XjSectLegacyMigration.ObserveActor(actor);
				XjSongXuanEasterEggSystem.ObserveBootstrapActor(actor);
				// 旧档首次载入时立即补写“紫府前未悟器意”的永久封锁，
				// 防止境界回退、旁路恢复或当年结算顺序重新开放刀意、枪意、弓意、剑意。
				XjWeaponArtSystem.OnRealmChanged(actor);
				// 冷路径允许做一次权威重建：真君录、权柄、家族纪事和已有
				// 法宝均在此恢复，不能只建一个只读显示缓存。
				if (!XjHighRealmRehydration.ReconcileActor(actor))
				{
					XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
				}
				// 旧档中已正式入位的郁仪仙/结璘仙必须在打开任何仙鉴页面前
				// 就完成身份收口，不能等到下一次年度结算才改名、清特质。
				XjXuanJianShenTongSpecials.ReconcileSpecialXianPositionIdentityOnLoad(actor);
				// Old saves may already contain ZiFu+ actors in native Warrior/Army state.
				// Bootstrap is already visiting each actor once, so reuse that cold pass
				// instead of restoring the former permanent annual army sweep.
				XjHighRealmArmyExclusion.EnqueueIfHighRealm(actor);

				if (!XjXuanJianShenTongSpecials.ReconcileYiDuiYingMirrorOnLoad(actor)
					&& (XjCultivationEligibility.CanCultivate(actor)
						|| XjCultivationEligibility.CanRunManagedLongShuCultivation(actor)
						|| XjCultivationEligibility.CanRunManagedYaoShuGreatSageCultivation(actor)))
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

		if (_cursor >= ActorIds.Count)
		{
			int effectiveYear = Math.Max(1, Math.Max(_baselineYear, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)));
			if (!_ancientShiPhaseInitialized)
			{
				// 首批古释仍保留每世界18个名额：前五十年隐世，五十年后开放。
				// 后续不再设置逐个年份硬间隔，其余名额由角色首次定路时O(1)自然承接。
				_ancientShiPhaseActive = XjShiOpeningPrologueSystem.TryBeginAncientSeedBootstrap(effectiveYear, false);
				_ancientShiCursor = 0;
				_ancientShiPhaseInitialized = true;
			}

			while (_ancientShiPhaseActive
				&& _ancientShiCursor < ActorIds.Count
				&& processed < budget
				&& XjShiOpeningPrologueSystem.IsAncientSeedBootstrapOpen(effectiveYear))
			{
				long candidateId = ActorIds[_ancientShiCursor++];
				XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor candidate);
				processed++;
				if (candidate?.data != null && candidate.isAlive()
					&& XjShiEntrySystem.IsAncientSeedBootstrapCandidate(candidate))
				{
					XjShiEntrySystem.TryEnterAncientSeedBootstrapCandidate(candidate, effectiveYear);
				}
				if ((processed & 15) == 0
					&& maxMilliseconds > 0d
					&& Stopwatch.GetTimestamp() - started > maxMilliseconds * Stopwatch.Frequency / 1000d)
				{
					break;
				}
			}

			if (_ancientShiPhaseActive
				&& (_ancientShiCursor >= ActorIds.Count || !XjShiOpeningPrologueSystem.IsAncientSeedBootstrapOpen(effectiveYear)))
			{
				// Complete只表示“这份冷启动快照扫描完成”；未满18时权威名额窗口保持开放，
				// 但本bootstrap lane不再长期持有世界角色快照。
				XjShiOpeningPrologueSystem.CompleteAncientSeedBootstrap(effectiveYear);
				_ancientShiPhaseActive = false;
			}
			if (_ancientShiPhaseActive) return;

			if (!_centuryBackfillPhaseInitialized)
			{
				_centuryBackfillPhaseActive = XjCenturyAnnalsBuilder.HasPendingHistoricalBackfill(effectiveYear);
				_centuryBackfillPhaseInitialized = true;
			}
			if (_centuryBackfillPhaseActive)
			{
				// 每个bootstrap帧最多补一卷。历史补卷只读取持久账本/世界史，
				// 不做当前人口扫描；336年缺201~300等卷会在数帧内自动补齐。
				bool builtBackfill = XjCenturyAnnalsBuilder.TryBuildOneHistoricalBackfill(effectiveYear);
				_centuryBackfillPhaseActive = builtBackfill
					&& XjCenturyAnnalsBuilder.HasPendingHistoricalBackfill(effectiveYear);
				// 若旧档账本本身损坏导致单卷无法重建，绝不能把整个玄鉴bootstrap永久卡死；
				// 后续仍会交给CenturyCatchUp的正常检测链重试并保留诊断边界。
				if (_centuryBackfillPhaseActive) return;
			}

			XjFuQiSwordWorldState.ReconcileAfterBootstrap(ResolveBootstrapActors(), effectiveYear);
			XjLongShuSystem.CompleteBootstrapTracking();
			XjLongShuSystem.ReconcileAfterBootstrap(effectiveYear);
			XjSongXuanEasterEggSystem.CompleteBootstrap(effectiveYear);
			// 读档恢复完成后再以修复后的纪年权威补一次固定里程碑与缺失世谱。
			// 这些都是O(1)/最多50卷的有界操作，不重放世界人口年度流水线。
			XjHongXiaLuoXiaEvent.ReconcileAfterLoad(effectiveYear);
			XjYuanZhaoKongZhengEvent.ReconcileTimelineAfterLoad(effectiveYear);
			XjAnnualWorldRuntimeLane.ScheduleCenturyCatchUp(effectiveYear);
			XjSectLegacyMigration.CompleteAfterBootstrap();
			// Native army inspection reads live unit/city/army references. Schedule it
			// only after actor/index rehydration has reached the stable load boundary,
			// so even its bounded transaction never spans the bootstrap phase.
			XjNativeArmySaveGuard.ScheduleAfterLoadInspection();
			ActorIds = Array.Empty<long>();
			_cursor = 0;
			_baselineYear = 0;
			_archiveReadyPhaseCompleted = true;
			_timelineBaseInitialized = false;
			_historyBaseRepaired = false;
			_yuanZhaoTimelineReconciled = false;
			_ancientShiPhaseInitialized = false;
			_ancientShiPhaseActive = false;
			_ancientShiCursor = 0;
			_centuryBackfillPhaseInitialized = false;
			_centuryBackfillPhaseActive = false;
		}
	}

	private static IReadOnlyList<long> CaptureActorIds(IReadOnlyList<Actor> units)
	{
		if (units == null || units.Count == 0) return Array.Empty<long>();
		List<long> ids = new List<long>(units.Count);
		for (int i = 0; i < units.Count; i++)
		{
			Actor actor = units[i];
			if (actor?.data == null) continue;
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId > 0L) ids.Add(actorId);
		}
		return ids.Count == 0 ? Array.Empty<long>() : ids.ToArray();
	}

	private static IReadOnlyList<Actor> ResolveBootstrapActors()
	{
		if (ActorIds.Count == 0) return Array.Empty<Actor>();
		List<Actor> actors = new List<Actor>(ActorIds.Count);
		for (int i = 0; i < ActorIds.Count; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(ActorIds[i], out Actor actor) && actor?.data != null)
			{
				actors.Add(actor);
			}
		}
		return actors;
	}

	internal static void AbortAfterRejectedArchive()
	{
		ActorIds = Array.Empty<long>();
		_cursor = 0;
		_baselineYear = 0;
		_archiveReadyPhaseCompleted = true;
		_timelineBaseInitialized = false;
		_historyBaseRepaired = false;
		_yuanZhaoTimelineReconciled = false;
		_ancientShiPhaseInitialized = false;
		_ancientShiPhaseActive = false;
		_ancientShiCursor = 0;
		_centuryBackfillPhaseInitialized = false;
		_centuryBackfillPhaseActive = false;
		XjSectLegacyMigration.Clear();
	}

	internal static void Clear()
	{
		ActorIds = Array.Empty<long>();
		_cursor = 0;
		_baselineYear = 0;
		_archiveReadyPhaseCompleted = true;
		_timelineBaseInitialized = false;
		_historyBaseRepaired = false;
		_yuanZhaoTimelineReconciled = false;
		_ancientShiPhaseInitialized = false;
		_ancientShiPhaseActive = false;
		_ancientShiCursor = 0;
		_centuryBackfillPhaseInitialized = false;
		_centuryBackfillPhaseActive = false;
		XjSectLegacyMigration.Clear();
	}
}
