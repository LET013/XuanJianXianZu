using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.LongShu;

internal static partial class XjLongShuSystem
{
	internal static bool HasKnownLongShu => KnownLongShuIds.Count > 0;
	// The first cohort is intentionally built over multiple background passes.
	// 每轮只显化三名龙属；九子是起源史实，不等于每次刷新九个活动实体。
	// 三名仍分散到多个后台阶段生成，避免原生单位创建与深海寻位挤在同一帧。
        internal static bool HasPendingSpawnWork =>
            XjRuntimeSettings.SpawnLongShuEnabled && _pendingSpawnCount > 0;
	internal const int PopulationCap = 3;
	internal const int RequiredZhenRenLevelCultivatorCount = 9;
	private const int MapEdgeSafetyMargin = 12;
	internal const int FixedLifespanYears = 800;
	internal const float RealmStatMultiplier = 1.6f;
	internal const float LifespanMultiplier = 1.0f;
	internal const float CultivationSpeedMultiplier = 1.2f;
	internal const float ShenTongHealthMultiplierPerCount = 0.18f;
	internal const float ShenTongDamageMultiplierPerCount = 0.10f;
	internal const float ShenTongAttackSpeedMultiplierPerCount = 0.025f;
	internal const float ShenTongDamageReducePerCount = 2.5f;
	internal const float ShenTongDamageReduceCap = 60f;
	private const string PrimaryActorAssetId = "longshu";
	private const float ZiFuInitialZhenYuan = 112000f;
	private const float LongShuMinimumMingShu = 50f;
	private const float LongShuMinimumHuiGuang = 90f;
	private const int DeepSeaProbeBudgetPerDragon = 48;
	private const int SeaFallbackProbeBudgetPerAttempt = 192;
	private const int MaxSpawnAttemptsPerYear = PopulationCap * 2;
	private const int MinSpawnDistanceSquared = 48 * 48;
	private const int FullCohortRespawnCooldownMinYears = 50;
	private const int FullCohortRespawnCooldownMaxYears = 100;

	private static readonly string[] ShuiDeDaoTu =
	{
		"坎水", "渌水", "合水", "府水", "牝水"
	};

	private static readonly string[] RealmStatIds =
	{
		"Resist", "warfare", "damage", "mass", "armor", "health", "speed",
		"area_of_effect", "targets", "critical_chance", "accuracy", "multiplier_speed",
		"stamina", "range", "attack_speed", "scale", "multiplier_health", "multiplier_damage",
		"Dodge", "Accuracy"
	};

	private static int _spawnAttemptYear = -1;
	private static int _spawnAttemptsThisYear;
	private static bool _populationUnlocked;
	private static bool _originPreludeRecorded;
	private static bool _activeCohortComplete;
	private static int _pendingSpawnCount;
	private static int _cohortSpawnedCount;
	private static int _nextFullCohortRespawnYear;
	private static bool _bootstrapTracking;
	internal static bool _requiresRenderTextureGuard;
	private static readonly List<long> KnownLongShuIds = new List<long>(PopulationCap);

	internal static void Init()
	{
		if (_assetInitialized)
		{
			return;
		}

		TryRegisterActorAsset();
	}

	internal static void TickRuntime(bool isYearChange, int growthPasses)
	{
		if (isYearChange)
		{
			RefreshPopulationPlan();
			TickHeShuiInfluence(Math.Max(0, XjYearTracker.CurrentYear));
		}

		// One native spawn per background phase keeps the three-dragon cohort
		// visible without turning a realm promotion into a frame-time spike.
		TrySpawnPendingLongShu();

		if (growthPasses > 0)
		{
			TickHostility(growthPasses);
		}
	}

	internal static void Clear()
	{
		_spawnAttemptYear = -1;
		_spawnAttemptsThisYear = 0;
		_hostilityTickCounter = 0;
		_hostilityCursor = 0;
		ClearInnateCombatRuntime();
		_populationUnlocked = false;
		_originPreludeRecorded = false;
		_activeCohortComplete = false;
		_pendingSpawnCount = 0;
		_cohortSpawnedCount = 0;
		_nextFullCohortRespawnYear = 0;
		_heShuiInfluenceStage = 0;
		_bootstrapTracking = false;
		_sharedLongShuSubspecies = null;
		_sharedLongShuSubspeciesId = -1L;
		KnownLongShuIds.Clear();
	}

	internal static XjLongShuPopulationArchiveData ExportPopulationState()
	{
		return new XjLongShuPopulationArchiveData
		{
			PopulationUnlocked = _populationUnlocked,
			OriginPreludeRecorded = _originPreludeRecorded,
			ActiveCohortComplete = _activeCohortComplete,
			CohortSpawnedCount = Math.Max(0, _cohortSpawnedCount),
			NextFullCohortRespawnYear = Math.Max(0, _nextFullCohortRespawnYear),
			HeShuiInfluenceStage = Math.Max(0, _heShuiInfluenceStage)
		};
	}

	internal static void ImportPopulationState(XjLongShuPopulationArchiveData state)
	{
		XjLongShuPopulationArchiveData source = state ?? new XjLongShuPopulationArchiveData();
		_cohortSpawnedCount = Math.Clamp(source.CohortSpawnedCount, 0, PopulationCap);
		_populationUnlocked = source.PopulationUnlocked || _cohortSpawnedCount > 0;
		_originPreludeRecorded = source.OriginPreludeRecorded || _cohortSpawnedCount > 0;
		_activeCohortComplete = source.ActiveCohortComplete || _cohortSpawnedCount >= PopulationCap;
		_nextFullCohortRespawnYear = Math.Max(0, source.NextFullCohortRespawnYear);
		_heShuiInfluenceStage = Math.Max(0, source.HeShuiInfluenceStage);
		_pendingSpawnCount = 0;
	}

	internal static void BeginBootstrapTracking()
	{
		_bootstrapTracking = true;
		_pendingSpawnCount = 0;
	}

	internal static void CompleteBootstrapTracking()
	{
		if (!_bootstrapTracking) return;
		_bootstrapTracking = false;
		int aliveCount = CollectAliveKnownLongShu().Count;
		int historicalBirths = Math.Min(PopulationCap, XjWorldHistoryStore.CountLongShuBirthRecords());
		_cohortSpawnedCount = Math.Max(_cohortSpawnedCount, Math.Max(historicalBirths, aliveCount));
		if (aliveCount > 0 || _cohortSpawnedCount > 0) _populationUnlocked = true;

		// 生成计划以“本轮已经诞生过多少龙王”为准，而不是以当前存活数为准。
		// 因此已经被杀死的龙王不会在读档后被当作缺口补回。
		_activeCohortComplete = _activeCohortComplete || _cohortSpawnedCount >= PopulationCap;
		_pendingSpawnCount = _populationUnlocked && !_activeCohortComplete
			? Math.Max(0, PopulationCap - _cohortSpawnedCount)
			: 0;
	}

	internal static void ReconcileAfterBootstrap(int currentYear)
	{
		if (!XjRuntimeSettings.SpawnLongShuEnabled || currentYear <= 0) return;
		bool changed = false;
		if (!_populationUnlocked && HasRequiredZhenRenLevelPopulation(null))
		{
			_populationUnlocked = true;
			changed = true;
		}
		if (_populationUnlocked && !_activeCohortComplete && _cohortSpawnedCount < PopulationCap)
		{
			int pending = Math.Max(0, PopulationCap - _cohortSpawnedCount);
			if (_pendingSpawnCount != pending)
			{
				_pendingSpawnCount = pending;
				changed = true;
			}
		}
		if (changed) XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
	}

	internal static void NotifyZiFuAppeared(Actor actor)
	{
		NotifyZhenRenLevelAppeared(actor);
	}

	internal static void NotifyZhenRenLevelAppeared(Actor actor)
	{
		if (!IsZhenRenLevelCultivator(actor))
		{
			return;
		}

		// Promotion runs inside the annual actor pipeline. Only mark the cohort
		// request here; the bounded background lane performs actual native spawns.
		QueuePopulationMaintenance(actor);
	}

	internal static bool IsLongShu(Actor actor)
	{
		if (actor == null)
		{
			return false;
		}

		if (string.Equals(actor.asset?.id, PrimaryActorAssetId, StringComparison.Ordinal))
		{
			return true;
		}

		if (IsLongShuSubspecies(actor.subspecies))
		{
			return true;
		}

		return actor.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLongShu, out int marker)
			&& marker > 0;
	}

	internal static bool IsHeShuiFruitPosition(string daoTu, string positionType)
	{
		return string.Equals((daoTu ?? string.Empty).Trim(), HeShuiDaoTuId, StringComparison.Ordinal)
			&& string.Equals(
				XjGuoWeiCalculator.NormalizePositionType(positionType),
				XjGuoWeiCalculator.ZhengWei,
				StringComparison.Ordinal);
	}

	internal static bool CanClaimHeShuiFruitPosition(Actor actor, string daoTu, string positionType)
	{
		return !IsHeShuiFruitPosition(daoTu, positionType) || IsLongShu(actor);
	}

	internal static bool CanClaimHeShuiFruitPosition(long actorId, string daoTu, string positionType)
	{
		if (!IsHeShuiFruitPosition(daoTu, positionType)) return true;
		return actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& IsLongShu(actor);
	}

	internal static bool IsExcludedFromInheritance(Actor actor)
	{
		return IsLongShu(actor);
	}

	internal static float GetCultivationSpeedMultiplier(Actor actor)
	{
		return IsLongShu(actor) ? CultivationSpeedMultiplier : 1f;
	}

	internal static void ApplyRealmStatMultiplier(Actor actor)
	{
		if (actor?.data == null || actor.stats == null)
		{
			return;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(realmId))
		{
			return;
		}

		ActorTrait realmTrait = AssetManager.traits?.get(realmId);
		if (realmTrait?.base_stats != null)
		{
			for (int i = 0; i < RealmStatIds.Length; i++)
			{
				string statId = RealmStatIds[i];
				float realmContribution = realmTrait.base_stats.get(statId);
				if (Math.Abs(realmContribution) > float.Epsilon)
				{
					float current = XjSafeCore.GetStatSafe(actor, statId, 0f);
					actor.stats[statId] = current + realmContribution * (RealmStatMultiplier - 1f);
				}
			}
		}

		ApplyShenTongCountScaling(actor);

		float lifespan = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		if (lifespan > 0f) actor.stats["lifespan"] = lifespan * LifespanMultiplier;
		ClampRuntimeLifespan(actor);
	}


	private static void ApplyShenTongCountScaling(Actor actor)
	{
		if (!IsLongShu(actor) || actor?.stats == null) return;
		int count = Math.Clamp(XjXianJiAccessor.BuildState(actor).Count, 0, 6);
		if (count <= 0) return;

		// 龙属缺少完整的人族宗门、家族与器物增益链，因此仍保留神通对龙躯的直接增幅；
		// 但只作为稀有生灵的补偿底盘，不再让四至六门神通形成第二套指数级属性膨胀。
		float healthBonus = XjSafeCore.GetStatSafe(actor, "multiplier_health", 0f);
		float healthFactor = Math.Max(0.01f, 1f + healthBonus) * (1f + ShenTongHealthMultiplierPerCount * count);
		actor.stats["multiplier_health"] = healthFactor - 1f;

		float damage = XjSafeCore.GetStatSafe(actor, "damage", 0f);
		if (damage > 0f) actor.stats["damage"] = damage * (1f + ShenTongDamageMultiplierPerCount * count);

		float attackSpeed = XjSafeCore.GetStatSafe(actor, "attack_speed", 0f);
		if (attackSpeed > 0f) actor.stats["attack_speed"] = attackSpeed * (1f + ShenTongAttackSpeedMultiplierPerCount * count);

		float damageReduce = XjSafeCore.GetStatSafe(actor, XjSafeCore.DamageReduce, 0f);
		actor.stats[XjSafeCore.DamageReduce] = Math.Min(ShenTongDamageReduceCap, damageReduce + ShenTongDamageReducePerCount * count);
	}

	internal static void ClampRuntimeLifespan(Actor actor)
	{
		if (!IsLongShu(actor) || actor?.stats == null || IsLongShuJinDanOrAbove(actor))
		{
			return;
		}

		actor.stats["lifespan"] = FixedLifespanYears;
	}

	internal static float GetFixedLifespanLimit(Actor actor)
	{
		return IsLongShu(actor) && !IsLongShuJinDanOrAbove(actor) ? FixedLifespanYears : 0f;
	}

	private static bool IsLongShuJinDanOrAbove(Actor actor)
	{
		if (actor?.data == null) return false;
		return XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan
			|| XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor);
	}

	private static void QueuePopulationMaintenance(Actor seedZiFuActor = null)
	{
		if (_bootstrapTracking) return;
		if (!XjRuntimeSettings.SpawnLongShuEnabled)
		{
			return;
		}

		int currentYear = ResolveCurrentYear(seedZiFuActor);
		if (currentYear <= 0)
		{
			return;
		}

		List<Actor> aliveLongShu = CollectAliveKnownLongShu();
		if (IsFullCohortRespawnCoolingDown(currentYear, aliveLongShu.Count))
		{
			_pendingSpawnCount = 0;
			return;
		}
		if (!_populationUnlocked && HasRequiredZhenRenLevelPopulation(seedZiFuActor))
		{
			_populationUnlocked = true;
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
		}
		if (_populationUnlocked && !_activeCohortComplete && _cohortSpawnedCount < PopulationCap)
		{
			_pendingSpawnCount = Math.Max(_pendingSpawnCount, PopulationCap - _cohortSpawnedCount);
		}
	}

	private static void RefreshPopulationPlan()
	{
		if (!XjRuntimeSettings.SpawnLongShuEnabled)
		{
			return;
		}

		int currentYear = ResolveCurrentYear(null);
		if (currentYear <= 0)
		{
			return;
		}

		bool hadTrackedCohort = KnownLongShuIds.Count > 0;
		List<Actor> aliveLongShu = CollectAliveKnownLongShu();
		if (aliveLongShu.Count == 0 && _activeCohortComplete)
		{
			// The completed cohort may restart only after every member has died.
			_activeCohortComplete = false;
			if (_nextFullCohortRespawnYear <= 0)
			{
				_nextFullCohortRespawnYear = currentYear + RollFullCohortRespawnCooldown(currentYear);
			}
			_pendingSpawnCount = 0;
			if (currentYear < _nextFullCohortRespawnYear)
			{
				return;
			}
			_nextFullCohortRespawnYear = 0;
			_cohortSpawnedCount = 0;
		}
		if (aliveLongShu.Count > 0)
		{
			_populationUnlocked = true;
			_nextFullCohortRespawnYear = 0;
		}

		if (!_populationUnlocked
			&& HasRequiredZhenRenLevelPopulation(null))
		{
			_populationUnlocked = true;
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
		}

		if (!_populationUnlocked)
		{
			return;
		}
		if (IsFullCohortRespawnCoolingDown(currentYear, aliveLongShu.Count))
		{
			_pendingSpawnCount = 0;
			return;
		}

		if (aliveLongShu.Count >= PopulationCap || _cohortSpawnedCount >= PopulationCap)
		{
			_activeCohortComplete = true;
			_pendingSpawnCount = 0;
			return;
		}

		if (hadTrackedCohort && aliveLongShu.Count > 0 && _activeCohortComplete)
		{
			_pendingSpawnCount = 0;
			return;
		}

		_pendingSpawnCount = Math.Max(0, PopulationCap - _cohortSpawnedCount);
	}

	private static void TrySpawnPendingLongShu()
	{
		if (!XjRuntimeSettings.SpawnLongShuEnabled || _pendingSpawnCount <= 0 || _activeCohortComplete)
		{
			return;
		}

		int currentYear = ResolveCurrentYear(null);
		if (currentYear <= 0)
		{
			return;
		}
		if (IsFullCohortRespawnCoolingDown(currentYear, 0))
		{
			_pendingSpawnCount = 0;
			return;
		}
		ResetSpawnAttemptWindow(currentYear);
            if (_spawnAttemptsThisYear >= MaxSpawnAttemptsPerYear)
            {
                // 当前年份已经完成多组彼此不同的有界海域探测仍失败，交由下一年度再规划。
                // 真正无水地图因此不会持续占用后台调度；有水地图则通常会在本年独立重试中完成。
                _pendingSpawnCount = 0;
                return;
            }

		List<Actor> aliveLongShu = CollectAliveKnownLongShu();
		if (aliveLongShu.Count >= PopulationCap || _cohortSpawnedCount >= PopulationCap)
		{
			_activeCohortComplete = true;
			_pendingSpawnCount = 0;
			return;
		}

		// A completed cohort never backfills individual deaths. It can only restart
		// after the last dragon is gone, preserving the intended three-dragon cycle.
		if (_activeCohortComplete && aliveLongShu.Count > 0)
		{
			_pendingSpawnCount = 0;
			return;
		}

		if (aliveLongShu.Count == 0 && KnownLongShuIds.Count == 0)
		{
			_activeCohortComplete = false;
		}

		int attemptOrdinal = _spawnAttemptsThisYear++;
		if (TrySpawnLongShu(currentYear, aliveLongShu.Count, attemptOrdinal, aliveLongShu, out _))
		{
			_cohortSpawnedCount = Math.Min(PopulationCap, _cohortSpawnedCount + 1);
			_pendingSpawnCount = Math.Max(0, PopulationCap - _cohortSpawnedCount);
			_activeCohortComplete = _cohortSpawnedCount >= PopulationCap;
			if (_activeCohortComplete)
			{
				_nextFullCohortRespawnYear = 0;
			}
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
		}
	}

	private static bool IsFullCohortRespawnCoolingDown(int currentYear, int aliveLongShuCount)
	{
		if (aliveLongShuCount > 0 || _nextFullCohortRespawnYear <= 0)
		{
			return false;
		}
		if (currentYear < _nextFullCohortRespawnYear)
		{
			return true;
		}
		_nextFullCohortRespawnYear = 0;
		_cohortSpawnedCount = 0;
		_activeCohortComplete = false;
		return false;
	}

	private static int RollFullCohortRespawnCooldown(int currentYear)
	{
		int span = FullCohortRespawnCooldownMaxYears - FullCohortRespawnCooldownMinYears + 1;
		return FullCohortRespawnCooldownMinYears
			+ XjDeterministicHash.PositiveIndex(currentYear, "longshu.full_cohort.respawn_cooldown", Math.Max(1, span));
	}

	private static bool TrySpawnLongShu(int currentYear, int ordinal, int attemptOrdinal, IReadOnlyList<Actor> existingLongShu, out Actor actor)
	{
		actor = null;
		ActorManager manager = World.world?.units;
		if (manager == null || !TryResolveActorAssetId(out string actorAssetId) || !TryFindDeepSeaTile(currentYear, ordinal, attemptOrdinal, existingLongShu, out WorldTile tile))
		{
			return false;
		}

		Actor spawnedActor = null;
		try
		{
			// Native creation registers the actor in all spatial and AI containers before the
			// LongShu state is applied. Loading ActorData here caused stale-container faults.
			spawnedActor = manager.spawnNewUnit(actorAssetId, tile, false, false, 0f, null, false, true);
			if (spawnedActor?.data == null)
			{
				return false;
			}

			if (!EnsureLongShuNativeState(spawnedActor, tile))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawnedActor);
				return false;
			}

			spawnedActor.data.age_overgrowth = 18;
			XjActorStateWriteGateway.SetDisplayName(spawnedActor, BuildLongShuName(GetActorId(spawnedActor), currentYear, ordinal), customName: true);
			XjActorAccessor.SetInt(spawnedActor, XjActorDataKeys.XjLongShu, 1);
			EnsureSharedLongShuSubspecies(spawnedActor);

			actor = spawnedActor;
			ApplyLongShuState(spawnedActor, currentYear);
			if (!EnsureLongShuNativeState(spawnedActor, tile))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawnedActor);
				actor = null;
				return false;
			}
			XjScheduler.RegisterActor(spawnedActor);
			RegisterKnownLongShu(spawnedActor);
			// “龙属肇生”只能在第一条真实龙脉已经成功进入原生世界容器之后播报。
			// 过去在解锁门槛时先播报、随后深海落点失败，会出现“公告有了却没有龙”的假事实。
			TryRecordLongShuOriginPrelude(currentYear);
			RecordLongShuBirth(spawnedActor, currentYear);
			return true;
		}
		catch
		{
			XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawnedActor);
			return false;
		}
	}

	private static bool EnsureLongShuNativeState(Actor actor, WorldTile requestedTile)
	{
		if (actor?.data == null || requestedTile?.chunk == null)
		{
			return false;
		}

		try
		{
			WorldTile current = GetCurrentTile(actor);
			if (current?.chunk == null)
			{
				try { actor.spawnOn(requestedTile); } catch (System.Exception xjCaught531) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuSystem.cs:531", xjCaught531); }
				// spawnOn 已经负责原生实体落格/Region 关系与当前位置提交；不要再
				// 写 tileTarget / current_path，避免生成后残留一份伪造寻路状态。
				// 也不能再用低层
				// setCurrentTilePosition 二次覆盖，避免后续寻路读取半状态。
				current = GetCurrentTile(actor);
			}

			if (current?.chunk == null)
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				return false;
			}

			bool isKingdomCiv = false;
			try { isKingdomCiv = actor.isKingdomCiv(); } catch (System.Exception xjCaught544) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuSystem.cs:544", xjCaught544); }
			if (isKingdomCiv && !XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				return false;
			}

			if (!isKingdomCiv)
			{
				try { actor.setCity(null); } catch (System.Exception xjCaught553) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuSystem.cs:553", xjCaught553); }
			}

			return true;
		}
		catch
		{
			XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			return false;
		}
	}

	private static void ApplyLongShuState(Actor actor, int currentYear)
	{
		string daoTu = ResolveLongShuDaoTu(GetActorId(actor));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLongShu, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, ResolveLongShuAptitude(GetActorId(actor)));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		if (!XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false)
			|| !XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false))
		{
			throw new InvalidOperationException("龙属生成无法建立修炼身份链");
		}
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, ZiFuInitialZhenYuan);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShen, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, 0);
		XjVisibleTraitSync.SyncChuShenTrait(actor, string.Empty);
		XjVisibleTraitSync.SyncChuShenSpecialTrait(actor, string.Empty);
		// 龙属总命数仍为180，但先天底盘遵守全体系100上限；血脉余量记入后天。
		XjMingShuState.Set(actor, 100f, 80f);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, 90f);
		EnsureSeedFloors(actor);
		NormalizeSubspecies(actor.subspecies);
		EnsureSharedLongShuSubspecies(actor);
		EnsureLongShuZiFuGongFa(actor, daoTu);
		EnsureLongShuBirthXianJiSet(actor, daoTu, currentYear, 4);
		XjActorGongFaCollection.ReconcileWithActor(actor, "LongShuBirth");
		XjEquipmentForgeConsumer.EnsureLongShuBirthLingBaoSet(actor, daoTu, currentYear, 3);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		TryApplyRealmTitle(actor, XjRealmIds.ZiFu, daoTu);
		RefreshActor(actor);
		XjCultivationStateTransitions.RestoreHealthAfterRealmPromotion(actor);
	}

	private static bool TryFindDeepSeaTile(int currentYear, int ordinal, int attemptOrdinal, IReadOnlyList<Actor> existingLongShu, out WorldTile tile)
	{
		tile = null;
		MapBox world = World.world;
		if (world == null || MapBox.width <= 0 || MapBox.height <= 0)
		{
			return false;
		}

		// 0.9.9.20: 同一年内的重试必须真正换一批海域。
		// 旧实现只用 currentYear + ordinal 作为种子，六次重试会反复探测同一组坐标，
		// 一旦该组没命中海域就只能等下一年换种子，表现为“有水却隔数年才出龙”。
		long attemptSalt = Math.Max(0, attemptOrdinal) * 1009L;
		long seed = XjDeterministicHash.PositiveHash(
			currentYear,
			"longshu.spawn." + ordinal.ToString(CultureInfo.InvariantCulture)
			+ ".attempt." + attemptOrdinal.ToString(CultureInfo.InvariantCulture));

		for (int i = 0; i < DeepSeaProbeBudgetPerDragon; i++)
		{
			int x = ResolveSafeMapCoordinate(seed + attemptSalt + i * 37L, "x", MapBox.width);
			int y = ResolveSafeMapCoordinate(seed + attemptSalt + i * 53L, "y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsDeepSeaTile(candidate) && IsFarFromExistingLongShu(candidate, existingLongShu))
			{
				tile = candidate;
				return true;
			}
		}

		for (int i = 0; i < DeepSeaProbeBudgetPerDragon / 2; i++)
		{
			int x = ResolveSafeMapCoordinate(seed + attemptSalt + i * 71L, "sea.x", MapBox.width);
			int y = ResolveSafeMapCoordinate(seed + attemptSalt + i * 89L, "sea.y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsSeaTile(candidate) && IsFarFromExistingLongShu(candidate, existingLongShu))
			{
				tile = candidate;
				return true;
			}
		}

		for (int i = 0; i < DeepSeaProbeBudgetPerDragon / 2; i++)
		{
			int x = ResolveSafeMapCoordinate(seed + attemptSalt + i * 97L, "loose.x", MapBox.width);
			int y = ResolveSafeMapCoordinate(seed + attemptSalt + i * 113L, "loose.y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsDeepSeaTile(candidate) || IsSeaTile(candidate))
			{
				tile = candidate;
				return true;
			}
		}

		// 稀疏水域兜底：仍然是固定小预算，不扫描整张地图。
		// 只在前面的深海/远离现存龙属探测全部失败时执行；每个尝试最多192个 O(1) GetTileSimple。
		// 对真正无水地图仍保留年度失败上限，不会因为 pending 状态形成逐帧全图搜索。
		for (int i = 0; i < SeaFallbackProbeBudgetPerAttempt; i++)
		{
			long probe = seed + attemptSalt + 1709L + i * 7919L;
			int x = ResolveSafeMapCoordinate(probe, "fallback.x", MapBox.width);
			int y = ResolveSafeMapCoordinate(probe + i * 104729L, "fallback.y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsSeaTile(candidate))
			{
				tile = candidate;
				return true;
			}
		}

		return false;
	}

	internal static bool IsRemoteSeaAnchor(WorldTile tile)
	{
		return (IsDeepSeaTile(tile) && IsFarFromLand(tile, 10))
			|| (IsSeaTile(tile) && IsFarFromLand(tile, 6));
	}

	internal static bool TryFindRemoteSeaTile(long seed, out WorldTile tile)
	{
		tile = null;
		MapBox world = World.world;
		if (world == null || MapBox.width <= 0 || MapBox.height <= 0)
		{
			return false;
		}

		const int probeBudget = 240;
		for (int i = 0; i < probeBudget; i++)
		{
			int x = ResolveSafeMapCoordinate(seed + i * 101L, "longshu.dongtian.x", MapBox.width);
			int y = ResolveSafeMapCoordinate(seed + i * 137L, "longshu.dongtian.y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsDeepSeaTile(candidate) && IsFarFromLand(candidate, 10))
			{
				tile = candidate;
				return true;
			}
		}

		for (int i = 0; i < probeBudget / 2; i++)
		{
			int x = ResolveSafeMapCoordinate(seed + i * 173L, "longshu.dongtian.fallback.x", MapBox.width);
			int y = ResolveSafeMapCoordinate(seed + i * 211L, "longshu.dongtian.fallback.y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsSeaTile(candidate) && IsFarFromLand(candidate, 6))
			{
				tile = candidate;
				return true;
			}
		}
		return false;
	}

	private static bool IsFarFromLand(WorldTile center, int radius)
	{
		if (!IsSeaTile(center))
		{
			return false;
		}
		MapBox world = World.world;
		for (int dx = -radius; dx <= radius; dx += 2)
		{
			for (int dy = -radius; dy <= radius; dy += 2)
			{
				WorldTile tile = world?.GetTileSimple(center.pos.x + dx, center.pos.y + dy);
				if (tile == null || !IsSeaTile(tile))
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool IsFarFromExistingLongShu(WorldTile tile, IReadOnlyList<Actor> existingLongShu)
	{
		if (tile == null || existingLongShu == null || existingLongShu.Count == 0)
		{
			return true;
		}

		for (int i = 0; i < existingLongShu.Count; i++)
		{
			WorldTile otherTile = GetCurrentTile(existingLongShu[i]);
			if (otherTile == null)
			{
				continue;
			}

			int dx = tile.pos.x - otherTile.pos.x;
			int dy = tile.pos.y - otherTile.pos.y;
			if ((dx * dx) + (dy * dy) < MinSpawnDistanceSquared)
			{
				return false;
			}
		}

		return true;
	}

	private static bool IsDeepSeaTile(WorldTile tile)
	{
		if (!IsSeaTile(tile))
		{
			return false;
		}

		string id = tile.Type?.id ?? string.Empty;
		return id.IndexOf("deep", StringComparison.OrdinalIgnoreCase) >= 0
			|| id.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsSeaTile(WorldTile tile)
	{
		if (tile == null || tile.Type == null || tile.Type.lava || tile.hasBuilding())
		{
			return false;
		}

		if (tile.Type.ocean || tile.Type.liquid)
		{
			return true;
		}

		string id = tile.Type.id ?? string.Empty;
		return id.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0
			|| id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
			|| id.IndexOf("sea", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool HasRequiredZhenRenLevelPopulation(Actor seedActor)
	{
		// 龙属现世门槛按跨修法“真人级及以上”统计：
		// 紫府、服气真人、金丹与真君羽士共用九人门槛。
		int count = XjCultivatorCache.ZhenRenOrHigherCount;
		long seedActorId = GetActorId(seedActor);
		if (seedActorId > 0L
			&& !XjCultivatorCache.IsCultivator(seedActorId)
			&& IsZhenRenLevelCultivator(seedActor))
		{
			count++;
		}
		return count >= RequiredZhenRenLevelCultivatorCount;
	}

	private static bool IsZhenRenLevelCultivator(Actor actor)
	{
		if (!IsAlive(actor) || IsLongShu(actor))
		{
			return false;
		}

		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjHighRealmIdentity.IsHighRealm(realmId);
	}

	private static int ResolveSafeMapCoordinate(long seed, string salt, int size)
	{
		if (size <= 0) return 0;
		int margin = Math.Min(MapEdgeSafetyMargin, Math.Max(0, (size - 1) / 4));
		int span = size - margin * 2;
		return span <= 0
			? XjDeterministicHash.PositiveIndex(seed, salt, size)
			: margin + XjDeterministicHash.PositiveIndex(seed, salt, span);
	}

	private static List<Actor> CollectAliveKnownLongShu()
	{
		List<Actor> result = new List<Actor>(KnownLongShuIds.Count);
		for (int i = KnownLongShuIds.Count - 1; i >= 0; i--)
		{
			long actorId = KnownLongShuIds[i];
			if (!XjScheduler.ResolveActor(actorId, out Actor actor) || !IsAlive(actor) || !IsLongShu(actor))
			{
				KnownLongShuIds.RemoveAt(i);
				continue;
			}

			WorldTile tile = GetCurrentTile(actor);
			if (tile?.chunk == null || !EnsureLongShuNativeState(actor, tile))
			{
				KnownLongShuIds.RemoveAt(i);
				continue;
			}

			// Birth equipment, XianJi and titles are initialized once in
			// ApplyLongShuState. Re-running them for every living dragon each year
			// was both redundant and expensive on large maps.
			EnsureSharedLongShuSubspecies(actor);
			ClampRuntimeLifespan(actor);
			result.Add(actor);
		}

		return result;
	}

	private static void RegisterKnownLongShu(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId > 0L && !KnownLongShuIds.Contains(actorId))
		{
			KnownLongShuIds.Add(actorId);
		}
	}

	internal static void TrackKnownActor(Actor actor)
	{
		if (IsLongShu(actor))
		{
			RegisterKnownLongShu(actor);
			EnsureSharedLongShuSubspecies(actor);
		}
	}

	internal static void EnsureSeedFloors(Actor actor)
	{
		if (!IsLongShu(actor))
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		acquired = (float)Math.Floor(Math.Max(0f, acquired));
		congenital = (float)Math.Floor(Math.Max(0f, congenital));
		float total = Math.Max(LongShuMinimumMingShu, congenital + acquired);
		// 旧档龙属可能把180全部写入先天命数。统一状态写口会把超过100的
		// 部分迁入后天命数，同时保留总命数和龙属最低命数。
		XjMingShuState.Set(actor, Math.Max(LongShuMinimumMingShu, congenital),
			Math.Max(0f, total - Math.Max(LongShuMinimumMingShu, congenital)));

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjXianJiAccessor.BuildState(actor).Count >= 4
			&& (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan)
				|| zhenYuan < ZiFuInitialZhenYuan))
		{
			// 兼容旧档：龙属出生即有四门真实神通，真元直接对齐四神通紫府上限，
			// 不再从紫府最低值重新积累。
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, ZiFuInitialZhenYuan);
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang,
			XjDaoHuiPolicy.Clamp(Math.Max(LongShuMinimumHuiGuang, huiGuang)));

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (aptitude < 5 || aptitude > 6)
		{
			// 兼容已经生成的旧龙属：道慧与新生龙属统一到90，再只抬资质到自然求金最低门槛，
			// 不重复发放资质初始真元/命数奖励。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 5);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjVisibleTraitSync.SyncAptitudeTrait(actor, 5);
		}
	}

	private static int ResolveCurrentYear(Actor seedActor)
	{
		int currentYear = XjYearTracker.CurrentYear;
		if (currentYear > 0)
		{
			return currentYear;
		}

		try
		{
			return seedActor == null ? 0 : Math.Max(0, (int)Math.Floor(Convert.ToDouble(seedActor.getAge(), CultureInfo.InvariantCulture)));
		}
		catch
		{
			return 0;
		}
	}

	private static void ResetSpawnAttemptWindow(int currentYear)
	{
		if (_spawnAttemptYear == currentYear)
		{
			return;
		}

		_spawnAttemptYear = currentYear;
		_spawnAttemptsThisYear = 0;
	}

	private static string ResolveLongShuDaoTu(long actorId)
	{
		// 合水向来与蛟宫、龙属最为亲近。新生龙属仍可能分入其余四水，
		// 但合水占更高权重；不强制改写已经生成的旧龙属道途。
		int roll = XjDeterministicHash.PositiveIndex(actorId, "longshu.shuide", 100);
		if (roll < 40) return HeShuiDaoTuId;
		if (roll < 55) return "坎水";
		if (roll < 70) return "渌水";
		if (roll < 85) return "府水";
		return "牝水";
	}

	private static int ResolveLongShuAptitude(long actorId)
	{
		int roll = XjDeterministicHash.PositiveIndex(actorId, "longshu.aptitude", 1000);
		// 龙属天生紫府且无法借用家族/宗门求金法。四等资质会同时锁死
		// 六品功法与金丹门槛，导致绝大多数龙属寿尽前根本没有求金资格。
		return roll < 5 ? 6 : 5;
	}

	private static void EnsureLongShuZiFuGongFa(Actor actor, string daoTu)
	{
		if (!IsAlive(actor) || string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (current.Found && current.Grade >= 5)
		{
			return;
		}

		long actorId = GetActorId(actor);
		string name = current.Found && !string.IsNullOrWhiteSpace(current.Name)
			? XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, 5)
			: XjGongFaNameLibrary.GenerateName(daoTu, 5, actorId + 5005L);
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		XjGongFaState state = new XjGongFaState(
			true,
			name,
			5,
			0,
			0f,
			daoTu,
			true,
			"LongShuZiFuBirth");
		XjGongFaAccessor.WriteState(actor, state);
		XjGongFaAccessor.WriteSource(actor, "龙属天生紫府");
	}

	private static void EnsureLongShuBirthXianJiSet(Actor actor, string daoTu, int currentYear, int targetCount)
	{
		if (!IsAlive(actor) || string.IsNullOrWhiteSpace(daoTu) || targetCount <= 0)
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return;
		}

		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		int guard = 0;
		while (state.Count < targetCount && guard++ < targetCount + 2)
		{
			int ordinal = state.Count + 1;
			if (!XjXianJiCatalog.TryPickUpperForProgression(
					daoTu,
					ordinal,
					actorId + ordinal * 17L,
					state.Ids,
					out string id)
				|| string.IsNullOrWhiteSpace(id)
				|| !XjXianJiAccessor.Add(actor, id, Math.Max(0, currentYear)))
			{
				break;
			}
			state = XjXianJiAccessor.BuildState(actor);
		}
	}

	private static bool IsAlive(Actor actor)
	{
		return XjSafeCore.IsAliveActor(actor);
	}

	private static WorldTile GetCurrentTile(Actor actor)
	{
		try
		{
			return actor == null ? null : ((BaseSimObject)actor).current_tile;
		}
		catch
		{
			return null;
		}
	}

	private static string GetActorAssetId(Actor actor)
	{
		try
		{
			return actor?.asset?.id ?? string.Empty;
		}
		catch
		{
			return string.Empty;
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

	private static void RefreshActor(Actor actor)
	{
		try
		{
			actor?.clearOldPath();
			actor?.clearTraitCache();
			actor?.setStatsDirty();
		}
		catch (System.Exception xjCaught1040) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuSystem.cs:1040", xjCaught1040); }
	}

	private static void TryRecordLongShuOriginPrelude(int currentYear)
	{
		if (_originPreludeRecorded || currentYear <= 0) return;
		_originPreludeRecorded = true;
		const string history = "【龙属肇生】昔有真螭吞羽蛇，继而以合水夺渌，水德与异血相激，遂爆出九子。九子散衍鳞脉，自此天下始有‘龙属’之名。今沧溟龙气复动，新一轮三脉将循海潮现世。";
		const string tip = "真螭吞羽蛇、合水夺渌，九子散衍，龙属自此肇生；今有三脉将现于沧溟。";
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastSLevelDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			"LongShuOrigin",
			history,
			tip,
			year: currentYear,
			iconId: XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.LongShu);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static void RecordLongShuBirth(Actor actor, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		string name = actor.getName();
		if (string.IsNullOrWhiteSpace(name)) name = "无名龙君";
		string body = "海天之间玄潮倒卷，" + name + "自深海龙渊现世，鳞光照夜，水德诸气为之退避。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			name + "出海",
			body,
			4,
			true,
			actorId: GetActorId(actor),
			actorName: name,
			year: Math.Max(0, currentYear),
			iconIdOverride: XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.LongShu,
			eventType: "LongShuBirth");
		XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			body,
			XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.HighRealm,
			duration: 9f,
			color: "#78BFEA",
			delayFrames: 1,
			iconId: XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.LongShu);
	}
}
