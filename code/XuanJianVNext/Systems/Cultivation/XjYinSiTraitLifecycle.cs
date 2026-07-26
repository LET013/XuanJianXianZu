using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjYinSiTraitLifecycle
{
	internal const string TraitId = "YinSi1";
	private const int YinSiDeathAttackType = 11;
	private const float ZiFuInitialZhenYuan = 36000f;
	private const float MinSpawnDelaySeconds = 10f;
	private const float MaxSpawnDelaySeconds = 20f;
	private const int SpawnTickBudget = 1;
	private const int MaxSpawnRetryCount = 3;
	private const int RequiredYinSiCount = 2;
	private const int MaxPendingSpawnTargets = 64;
	private const float MissionAggroRefreshSeconds = 3f;
	private const float CompleteBindCheckIntervalSeconds = 20f;
	private const float RuntimeStateRetentionSeconds = 90f;
	private const float RuntimeMaintenanceIntervalSeconds = 30f;
	private const string YinSiDaoTu = "谪炁";
	private const string YinSiAlternateDaoTu = "下仪";
	private const string JinXingManifestKind = "阴司使者";
	private const string JinDanManifestKind = "幽冥府君";
	private const string YinSiRaceId = "human";
	private const string YinSiSubspeciesName = "阴司人族";
	private const string YinSiRuntimeJobId = "random_move";
	private static readonly Dictionary<long, float> PendingSpawnDueTimeByYaoXieId = new Dictionary<long, float>();
	private static readonly Dictionary<long, int> PendingSpawnRetryCountByYaoXieId = new Dictionary<long, int>();
	private static readonly Dictionary<long, float> LastBindAttemptTimeByYaoXieId = new Dictionary<long, float>();
	private static readonly Dictionary<long, float> LastCompleteBindTimeByYaoXieId = new Dictionary<long, float>();
	private static readonly Dictionary<long, float> LastMissionAggroTimeByYinSiId = new Dictionary<long, float>();
	private static readonly List<long> PendingSpawnReadyIds = new List<long>(8);
	private static readonly List<long> StaleRuntimeStateIds = new List<long>(8);
	private static float nextRuntimeMaintenanceTime;
	private static Subspecies _yinSiHumanSubspecies;
	private static readonly int[] SpawnOffsets =
	{
		6, 0,
		-6, 0,
		0, 6,
		0, -6,
		5, 4,
		-5, 4,
		5, -4,
		-5, -4,
		8, 2,
		-8, 2,
		8, -2,
		-8, -2
	};

	private static bool _isReconciling;

	internal static bool IsReconciling => _isReconciling;
	internal static bool HasPendingSpawns => false;
	internal static bool HasPendingRuntimeWork => ((LastBindAttemptTimeByYaoXieId.Count > 0
				|| LastCompleteBindTimeByYaoXieId.Count > 0)
			&& Time.unscaledTime >= nextRuntimeMaintenanceTime);
	internal static bool IsYinSiTrait(string traitId)
	{
		return string.Equals(traitId, TraitId, StringComparison.Ordinal);
	}

	internal static bool IsYinSi(Actor actor)
	{
		if (actor?.data == null || !HasAuthorizedOrigin(actor)) return false;
		// 1.1.0-alpha.2起不再注册可见阴司特质；旧档仅保留内部授权状态。
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YinSi, out int yinsi);
		return yinsi > 0;
	}

	internal static bool ShouldAllowVisibleGrant(Actor actor, string traitId)
	{
		// 阴司实体生成已经关闭，旧档阴司身份只作内部兼容，不再允许可见特质进入角色栏。
		return !IsYinSiTrait(traitId);
	}

	internal static bool TryBindForJinXingYaoXie(Actor yaoXie)
	{
		if (yaoXie?.data == null || !IsAlive(yaoXie))
		{
			return false;
		}

		long yaoXieId = GetActorId(yaoXie);
		if (yaoXieId <= 0L)
		{
			return false;
		}

		// This is reached from the bounded actor pipeline.  Do not parse the
		// persisted bound-id list every time the same target is observed.
		if (PendingSpawnDueTimeByYaoXieId.ContainsKey(yaoXieId))
		{
			return true;
		}

		float now = Time.unscaledTime;
		if (LastCompleteBindTimeByYaoXieId.TryGetValue(yaoXieId, out float lastComplete)
			&& now - lastComplete < CompleteBindCheckIntervalSeconds)
		{
			return true;
		}

		if (LastBindAttemptTimeByYaoXieId.TryGetValue(yaoXieId, out float lastAttempt)
			&& now - lastAttempt < 5f)
		{
			return true;
		}

		LastBindAttemptTimeByYaoXieId[yaoXieId] = now;
		bool scheduled = XjTrueDamageSystem.ScheduleJinXingYaoXieSuppression(yaoXie);
		if (scheduled)
		{
			LastCompleteBindTimeByYaoXieId[yaoXieId] = now;
		}
		return scheduled;
	}

	internal static void ForceOnePendingSpawnForDebug(Actor yaoXie)
	{
		long yaoXieId = GetActorId(yaoXie);
		if (yaoXieId <= 0L || yaoXie?.data == null || !IsAlive(yaoXie))
		{
			return;
		}

		if (!XjTrueDamageSystem.IsJinXingYaoXie(yaoXie))
		{
			return;
		}

		XjTrueDamageSystem.ScheduleJinXingYaoXieSuppression(yaoXie);
	}

	internal static void TickPendingSpawns(int budget = SpawnTickBudget)
	{
		float now = Time.unscaledTime;
		PruneTransientRuntimeState(now);
		PendingSpawnDueTimeByYaoXieId.Clear();
		PendingSpawnRetryCountByYaoXieId.Clear();
		PendingSpawnReadyIds.Clear();
		if (budget <= 0 || PendingSpawnDueTimeByYaoXieId.Count == 0)
		{
			return;
		}

		PendingSpawnReadyIds.Clear();
		foreach (KeyValuePair<long, float> pending in PendingSpawnDueTimeByYaoXieId)
		{
			if (pending.Value <= now)
			{
				PendingSpawnReadyIds.Add(pending.Key);
				if (PendingSpawnReadyIds.Count >= budget) break;
			}
		}

		for (int i = 0; i < PendingSpawnReadyIds.Count; i++)
		{
			long yaoXieId = PendingSpawnReadyIds[i];
			PendingSpawnDueTimeByYaoXieId.Remove(yaoXieId);
			if (!XjActorRegistry.ResolveKnownOrWorld(yaoXieId, out Actor yaoXie)
				|| !IsAlive(yaoXie)
				|| !XjTrueDamageSystem.IsJinXingYaoXie(yaoXie))
			{
				PendingSpawnRetryCountByYaoXieId.Remove(yaoXieId);
				continue;
			}

			XjTrueDamageSystem.ScheduleJinXingYaoXieSuppression(yaoXie);
			PendingSpawnRetryCountByYaoXieId.Remove(yaoXieId);
			LastCompleteBindTimeByYaoXieId[yaoXieId] = now;
		}
		PendingSpawnReadyIds.Clear();
	}

	internal static void ClearRuntimeQueue()
	{
		PendingSpawnDueTimeByYaoXieId.Clear();
		PendingSpawnRetryCountByYaoXieId.Clear();
		LastBindAttemptTimeByYaoXieId.Clear();
		LastCompleteBindTimeByYaoXieId.Clear();
		LastMissionAggroTimeByYinSiId.Clear();
		PendingSpawnReadyIds.Clear();
		StaleRuntimeStateIds.Clear();
		nextRuntimeMaintenanceTime = 0f;
		_yinSiHumanSubspecies = null;
	}

	private static void PruneTransientRuntimeState(float now)
	{
		if (now < nextRuntimeMaintenanceTime)
		{
			return;
		}

		nextRuntimeMaintenanceTime = now + RuntimeMaintenanceIntervalSeconds;
		PruneExpiredTimeMap(LastBindAttemptTimeByYaoXieId, now);
		PruneExpiredTimeMap(LastCompleteBindTimeByYaoXieId, now);
	}
	private static void PruneExpiredTimeMap(Dictionary<long, float> values, float now)
	{
		if (values.Count == 0)
		{
			return;
		}

		StaleRuntimeStateIds.Clear();
		foreach (KeyValuePair<long, float> pair in values)
		{
			if (now - pair.Value > RuntimeStateRetentionSeconds)
			{
				StaleRuntimeStateIds.Add(pair.Key);
			}
		}
		for (int i = 0; i < StaleRuntimeStateIds.Count; i++)
		{
			values.Remove(StaleRuntimeStateIds[i]);
		}
	}

	internal static void OnJinXingYaoXieDied(Actor yaoXie)
	{
		long yaoXieId = GetActorId(yaoXie);
		PendingSpawnDueTimeByYaoXieId.Remove(yaoXieId);
		PendingSpawnRetryCountByYaoXieId.Remove(yaoXieId);
		LastBindAttemptTimeByYaoXieId.Remove(yaoXieId);
		LastCompleteBindTimeByYaoXieId.Remove(yaoXieId);
		if (yaoXie?.data == null) return;
		XjActorAccessor.SetString(yaoXie, XjActorDataKeys.JinXingYaoXieBoundYinSiActorId, string.Empty);
		XjActorAccessor.SetString(yaoXie, XjActorDataKeys.JinXingYaoXieBoundYinSiActorIds, string.Empty);
	}
	internal static bool IsBoundYinSiForYaoXie(Actor yinSi, Actor yaoXie)
	{
		return false;
	}
	internal static bool IsBoundYinSiForLivingJinDan(Actor yinSi, Actor target)
	{
		return false;
	}

	internal static bool TryGrant(Actor actor)
	{
		if (actor?.data == null || !HasAuthorizedOrigin(actor))
		{
			ReconcileExclusiveOrigin(actor);
			return false;
		}

		try
		{
			_isReconciling = true;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.YinSi, 1);
			if (ActorHasTrait(actor, TraitId)) actor.removeTrait(TraitId);
			return true;
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static void RecordVisibleTraitGranted(Actor actor, string traitId)
	{
		if (_isReconciling || actor?.data == null || !IsYinSiTrait(traitId))
		{
			return;
		}

		ReconcileExclusiveOrigin(actor);
	}

	internal static void RecordVisibleTraitRemoved(Actor actor, string traitId)
	{
		if (_isReconciling || actor?.data == null || !IsYinSiTrait(traitId))
		{
			return;
		}

		ReconcileExclusiveOrigin(actor);
	}

	internal static void EnsureRemovedFromJinDan(Actor actor)
	{
		ReconcileExclusiveOrigin(actor);
	}

	internal static void ReconcileExclusiveOrigin(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		bool authorized = HasAuthorizedOrigin(actor);
		try
		{
			_isReconciling = true;
			if (ActorHasTrait(actor, TraitId)) actor.removeTrait(TraitId);
			if (authorized)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.YinSi, 1);
			}
			else
			{
				ClearState(actor);
			}
		}
		finally
		{
			_isReconciling = false;
		}
	}

	internal static bool TryCreateForMission(Actor target, long missionId, int ordinal, out Actor yinSi)
	{
		yinSi = null;
		return false;
	}

	internal static void ForceMissionAggro(Actor yinSi, Actor target)
	{
		// 阴司追索已改为非实体规则结算，不再强制 Actor 仇恨。
	}
	internal static void EndMissionManifest(Actor yinSi)
	{
		long yinSiId = GetActorId(yinSi);
		if (yinSiId > 0L) LastMissionAggroTimeByYinSiId.Remove(yinSiId);
		if (yinSi?.data == null) return;
		ClearState(yinSi);
	}
	// 阴司不再生成实体。旧版实体创建、绑定、命名、功法和神通补齐逻辑已移除；
	// 金性妖邪与金丹追索只保留延迟规则结算和历史公告。
	internal static void EnsureTransientState(Actor actor)
	{
		if (!IsYinSi(actor))
		{
			return;
		}

		PurgeWorldlyTies(actor);
		EnsureFixedYinSiSubspecies(actor);
		PrepareYinSiRuntimeAi(actor);
		MaintainTransientTarget(actor);
		RefreshActor(actor);
	}

	internal static void EnsureYinSiNativeSaveStateForSave(Actor actor)
	{
		if (!IsYinSi(actor))
		{
			return;
		}

		PurgeWorldlyTies(actor);
		EnsureFixedYinSiSubspecies(actor);
		PrepareYinSiRuntimeAi(actor);
	}

	internal static bool ShouldUseYinSiRuntimeJob(Actor actor)
	{
		return IsYinSi(actor);
	}

	internal static string GetYinSiRuntimeJobId()
	{
		return YinSiRuntimeJobId;
	}

	private static void MaintainTransientTarget(Actor actor)
	{
		// 非实体阴司不再维护临时追击目标；旧档残留状态在授权校验时自然失效。
	}
	private static void PurgeWorldlyTies(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		bool isKingdomCiv = false;
		try { isKingdomCiv = actor.isKingdomCiv(); } catch { }

		if (isKingdomCiv)
		{
			// Native save code requires civilization actors to keep a complete city/kingdom/culture
			// state. Breaking those links produces Actor.saveKingdomCiv null references on autosave.
			if (!XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			}
			return;
		}

		try { actor.setCity(null); } catch { }
		try { actor.setKingdom(null); } catch { }
		try
		{
			ActorData data = actor.data;
			data.cityID = -1L;
			data.civ_kingdom_id = -1L;
			data.culture = -1L;
			data.clan = -1L;
			data.family = -1L;
			data.homeBuildingID = -1L;
			data.transportID = -1L;
		}
		catch { }
	}

	private static void BroadcastYinSiLeave(Actor yinSi)
	{
		string name = SafeActorName(yinSi, "阴司");
		string announcement = "【阴司离去】" + name + "事毕归幽，形迹自人间散去。";
		XjBroadcastSystem.BroadcastBLevelWorldEvent(announcement, XjEventIconCatalog.YinSiLeave);
	}

	private static string SafeActorName(Actor actor, string fallback)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
		}
		catch
		{
			return fallback;
		}
	}

	private static void EnsureFixedYinSiSubspecies(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try
		{
			ActorAsset humanAsset = AssetManager.actor_library?.get(YinSiRaceId);
			if (humanAsset != null && !string.Equals(actor.asset?.id, YinSiRaceId, StringComparison.OrdinalIgnoreCase))
			{
				actor.asset = humanAsset;
			}

			if (!string.Equals(actor.data.asset_id, YinSiRaceId, StringComparison.OrdinalIgnoreCase))
			{
				actor.data.asset_id = YinSiRaceId;
			}

			Subspecies yinSiSubspecies = ResolveYinSiHumanSubspecies(actor);
			if (yinSiSubspecies != null && actor.subspecies != yinSiSubspecies)
			{
				actor.setSubspecies(yinSiSubspecies);
				actor.data.subspecies = yinSiSubspecies.id;
				TryGeneratePhenotype(actor);
			}

			RefreshActor(actor);
		}
		catch
		{
		}
	}

	private static Subspecies ResolveYinSiHumanSubspecies(Actor actor)
	{
		if (IsYinSiHumanSubspecies(_yinSiHumanSubspecies))
		{
			return _yinSiHumanSubspecies;
		}

		Subspecies source = TryFindNativeHumanSubspecies(actor);
		if (source == null || World.world?.subspecies == null || World.world?.map_stats == null)
		{
			return null;
		}

		try
		{
			SubspeciesData copy = JsonConvert.DeserializeObject<SubspeciesData>(
				JsonConvert.SerializeObject(source.data, Formatting.None));
			if (copy == null)
			{
				return null;
			}

			copy.id = World.world.map_stats.getNextId("subspecies");
			copy.name = YinSiSubspeciesName;
			copy.custom_name = true;
			copy.species_id = YinSiRaceId;
			copy.parent_subspecies = source.id;
			copy.created_time = World.world.map_stats.world_time;
			copy.total_deaths = 0L;
			copy.total_births = 0L;
			copy.total_kills = 0L;
			World.world.subspecies.loadObject(copy);
			Subspecies loaded = World.world.subspecies.get(copy.id);
			if (IsYinSiHumanSubspecies(loaded))
			{
				_yinSiHumanSubspecies = loaded;
				return _yinSiHumanSubspecies;
			}
		}
		catch
		{
		}

		return null;
	}

	private static Subspecies TryFindNativeHumanSubspecies(Actor self)
	{
		try
		{
			IReadOnlyList<Actor> indexed = XjActorRegistry.Snapshot();
			for (int i = 0; i < indexed.Count; i++)
			{
				Actor actor = indexed[i];
				if (!IsNormalHumanSubspeciesSource(actor, self))
				{
					continue;
				}
				return actor.subspecies;
			}

			IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
			int count = Math.Min(units?.Count ?? 0, 512);
			for (int i = 0; i < count; i++)
			{
				Actor actor = units[i];
				if (!IsNormalHumanSubspeciesSource(actor, self))
				{
					continue;
				}
				return actor.subspecies;
			}
		}
		catch
		{
		}

		return null;
	}

	private static bool IsNormalHumanSubspeciesSource(Actor actor, Actor self)
	{
		if (actor?.data == null || ReferenceEquals(actor, self))
		{
			return false;
		}

		if (!string.Equals(actor.data.asset_id, YinSiRaceId, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		if (ActorHasTrait(actor, TraitId) || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		return IsNativeHumanSubspecies(actor.subspecies);
	}

	private static bool IsNativeHumanSubspecies(Subspecies subspecies)
	{
		try
		{
			string name = subspecies?.data?.name ?? string.Empty;
			return subspecies?.data != null
				&& string.Equals(subspecies.getActorAsset()?.id, YinSiRaceId, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(name, YinSiSubspeciesName, StringComparison.Ordinal)
				&& name.IndexOf("阴司", StringComparison.Ordinal) < 0
				&& name.IndexOf("龙属", StringComparison.Ordinal) < 0
				&& name.IndexOf("天地之魄", StringComparison.Ordinal) < 0;
		}
		catch
		{
			return false;
		}
	}

	private static bool IsYinSiHumanSubspecies(Subspecies subspecies)
	{
		try
		{
			return subspecies?.data != null
				&& string.Equals(subspecies.getActorAsset()?.id, YinSiRaceId, StringComparison.OrdinalIgnoreCase)
				&& string.Equals(subspecies.data.name, YinSiSubspeciesName, StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static void TryGeneratePhenotype(Actor actor)
	{
		try
		{
			if (actor?.subspecies != null && actor.subspecies.hasPhenotype())
			{
				actor.generatePhenotypeAndShade();
			}
		}
		catch
		{
		}
	}

	private static void PrepareYinSiRuntimeAi(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try { actor.setProfession(UnitProfession.Unit, true); } catch { }
		try { actor.cancelAllBeh(); } catch { }
		try { actor.clearOldPath(); } catch { }
		try { actor.stopMovement(); } catch { }
	}

	private static void RefreshActor(Actor actor)
	{
		try
		{
			actor?.clearOldPath();
			actor?.clearTraitCache();
			actor?.setStatsDirty();
		}
		catch
		{
		}
	}

	private static bool HasAuthorizedOrigin(Actor actor)
	{
		// 阴司不再显化为实体；任何旧绑定或 mission 字段都不能再授权 Actor 成为阴司。
		return false;
	}

	private static void ClearState(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.YinSi, 0);
	}

	private static bool IsAlive(Actor actor)
	{
		try
		{
			return actor != null && ((NanoObject)actor).isAlive();
		}
		catch
		{
			return false;
		}
	}

	private static bool ActorHasTrait(Actor actor, string traitId)
	{
		try
		{
			return actor?.data != null && !string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId);
		}
		catch
		{
			return false;
		}
	}

	private static long GetActorId(Actor actor)
	{
		try
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}
		catch
		{
			return 0L;
		}
	}
}







