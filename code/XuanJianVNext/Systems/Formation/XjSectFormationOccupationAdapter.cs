using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Talisman;

namespace XuanJianVNext.Systems.Formation;

internal static class XjSectFormationOccupationAdapter
{
	private readonly struct BreakProfile
	{
		internal BreakProfile(float realmScore, float formationMasterModifier, float talismanModifier)
		{
			RealmScore = realmScore;
			FormationMasterModifier = formationMasterModifier;
			TalismanModifier = talismanModifier;
		}
		internal float RealmScore { get; }
		internal float FormationMasterModifier { get; }
		internal float TalismanModifier { get; }
	}

	private sealed class CachedBreakProfile
	{
		internal int Frame;
		internal long AttackerKingdomId;
		internal long AttackerSectId;
		internal BreakProfile Profile;
	}

	private sealed class CachedPopulation
	{
		internal int FrameBucket;
		internal int Count;
	}

	private const int MaximumCapturingUnitScan = 128;
	private const int PopulationCacheFrames = 30;
	private const float DamageScale = 0.10f;
	private const bool NativeKingdomWarFormationGateEnabled = false;
	private static readonly BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
	private static readonly Dictionary<long, int> LastDamageFrameByCity = new Dictionary<long, int>();
	private static readonly Dictionary<long, CachedBreakProfile> BreakProfileByCity = new Dictionary<long, CachedBreakProfile>();
	private static readonly Dictionary<long, CachedPopulation> PopulationByKingdom = new Dictionary<long, CachedPopulation>();
	private static readonly Dictionary<(long CityId, long AttackerId), float> CaptureRemainders = new Dictionary<(long, long), float>();
	private static readonly Dictionary<(long CityId, long AttackerId), int> LastBreakTalismanYear = new Dictionary<(long, long), int>();
	private static readonly Dictionary<(long CityId, long AttackerId, long ActorId), int> LastJinDanExposureYear = new Dictionary<(long, long, long), int>();
	private static readonly Dictionary<Type, MemberInfo> ActorMemberByWrapperType = new Dictionary<Type, MemberInfo>();
	private static readonly List<(long CityId, long AttackerId)> ExpiredTalismanCooldowns = new List<(long, long)>();
	private static readonly List<(long CityId, long AttackerId, long ActorId)> ExpiredExposureCooldowns = new List<(long, long, long)>();
	private static FieldInfo capturingUnitsField;
	private static bool capturingUnitsFieldResolved;
	private static int lastTransientCachePruneYear = int.MinValue;

	[ThreadStatic] private static bool hasCaptureContext;
	[ThreadStatic] private static City captureContextCity;
	[ThreadStatic] private static long captureContextSectId;
	[ThreadStatic] private static long captureContextAttackerKingdomId;
	[ThreadStatic] private static long captureContextAttackerSectId;
	[ThreadStatic] private static float[] topRealmScores;

	internal static void BeginNativeCapture(City city, Kingdom attackingKingdom, ref int nativeDelta)
	{
		hasCaptureContext = false;
		captureContextCity = null;
		captureContextSectId = 0L;
		captureContextAttackerKingdomId = 0L;
		captureContextAttackerSectId = 0L;
		if (!NativeKingdomWarFormationGateEnabled) return;
		if (city?.data == null || attackingKingdom?.data == null || nativeDelta <= 0 || city.kingdom?.data == null || city.kingdom == attackingKingdom) return;

		long cityId = city.data.id;
		long attackerId = attackingKingdom.data.id;
		long defenderKingdomId = city.kingdom.data.id;
		if (cityId <= 0L || attackerId <= 0L || !XjSectRepository.TryGetByCity(city, out var sect)
			|| sect == null || !XjSectFormationRegistry.TryGetOperational(sect.SectId, out XjSectFormationArchiveRecord formation)) return;
		if (!TryResolveOpposingAttackerSectId(city, attackingKingdom, sect.SectId, out long attackerSectId)) return;
		int currentYear = GetCurrentYear();
		PruneTransientCaches(currentYear);

		int originalDelta = nativeDelta;
		int frame = Time.frameCount;
		if (IsDefenderPopulationRouted(city.kingdom, defenderKingdomId, frame))
		{
			XjSectFormationRegistry.ForceBreakByPopulationRout(sect.SectId, currentYear);
			BreakProfileByCity.Remove(cityId);
			CaptureRemainders.Remove((cityId, attackerId));
			return;
		}

		if (!LastDamageFrameByCity.TryGetValue(cityId, out int lastFrame) || lastFrame != frame)
		{
			LastDamageFrameByCity[cityId] = frame;
			BreakProfile profile = GetBreakProfile(city, attackingKingdom, cityId, attackerId, attackerSectId, frame);
			float rawDamage = Math.Max(1, originalDelta)
				* Math.Max(1f, profile.RealmScore)
				* Math.Max(1f, profile.FormationMasterModifier)
				* Math.Max(1f, profile.TalismanModifier)
				* XjSectFormationBalance.GetGradeResistance(formation.Grade)
				* DamageScale;
			XjSectFormationRegistry.ApplyOccupationDamage(sect.SectId, Math.Max(1, (int)Math.Ceiling(rawDamage)), currentYear);
		}

		if (!XjSectFormationRegistry.TryGetOperational(sect.SectId, out formation)) return;
		nativeDelta = ScaleNativeDelta(cityId, attackerId, originalDelta, formation.OccupationSpeedMultiplier);
		hasCaptureContext = true;
		captureContextCity = city;
		captureContextSectId = sect.SectId;
		captureContextAttackerKingdomId = attackerId;
		captureContextAttackerSectId = attackerSectId;
	}

	internal static void EndNativeCapture(City city)
	{
		if (!hasCaptureContext) return;
		if (city == null || ReferenceEquals(captureContextCity, city))
		{
			hasCaptureContext = false;
			captureContextCity = null;
			captureContextSectId = 0L;
			captureContextAttackerKingdomId = 0L;
			captureContextAttackerSectId = 0L;
		}
	}

	internal static bool AllowKingdomTransfer(City city, Kingdom targetKingdom)
	{
		if (!NativeKingdomWarFormationGateEnabled) return true;
		if (XjNationSectRebellionGuard.IsControlledFounding || XjWorldBoxKingdomBridge.IsForcedConquest) return true;
		if (city?.data == null || targetKingdom?.data == null) return true;
		if (hasCaptureContext && ReferenceEquals(captureContextCity, city) && targetKingdom.data.id == captureContextAttackerKingdomId && captureContextAttackerSectId > 0L)
		{
			return !XjSectFormationRegistry.TryGetOperational(captureContextSectId, out _);
		}
		return true;
	}

	internal static int ApplySectWarDamage(long attackerSectId, long defenderSectId, int baseDamage, int currentYear, out bool defenderDefeated)
	{
		defenderDefeated = false;
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| attackerSectId <= 0L
			|| defenderSectId <= 0L
			|| attackerSectId == defenderSectId
			|| baseDamage <= 0)
		{
			return 0;
		}

		if (!XjSectRepository.TryGetBySectId(attackerSectId, out XuanJianVNext.Data.Sect.XjSectArchiveRecord attacker)
			|| attacker == null
			|| !XjSectRepository.TryGetBySectId(defenderSectId, out XuanJianVNext.Data.Sect.XjSectArchiveRecord defender)
			|| defender == null)
		{
			return 0;
		}

		if (!XjSectFormationRegistry.TryGet(defenderSectId, out XjSectFormationArchiveRecord formation)
			|| formation == null
			|| formation.MaxDurability <= 0)
		{
			return 0;
		}

		int year = Math.Max(0, currentYear);
		int suppressionMultiplier = GetSectWarSuppressionMultiplier(attackerSectId, defenderSectId);
		int damage = Math.Max(1, baseDamage) * Math.Max(1, suppressionMultiplier);
		int applied = XjSectFormationRegistry.ApplyOccupationDamage(defenderSectId, damage, year);
		if (applied <= 0) return 0;
		if (!XjSectFormationRegistry.TryGetOperational(defenderSectId, out _))
		{
			defenderDefeated = XjSectRepository.TryDefeatSectByFormationBreak(defenderSectId, attackerSectId, year, "宗门大阵被破");
		}
		return applied;
	}

	private static int GetSectWarSuppressionMultiplier(long attackerSectId, long defenderSectId)
	{
		int attackerRank = XjSectRepository.ResolveSectRealmRank(attackerSectId);
		int defenderRank = XjSectRepository.ResolveSectRealmRank(defenderSectId);
		return attackerRank >= XjRealmSuppression.TierJinDan && defenderRank == XjRealmSuppression.TierZiFu ? 2 : 1;
	}

	internal static void Clear()
	{
		hasCaptureContext = false;
		captureContextCity = null;
		captureContextSectId = 0L;
		captureContextAttackerKingdomId = 0L;
		captureContextAttackerSectId = 0L;
		LastDamageFrameByCity.Clear();
		BreakProfileByCity.Clear();
		PopulationByKingdom.Clear();
		CaptureRemainders.Clear();
		LastBreakTalismanYear.Clear();
		LastJinDanExposureYear.Clear();
		ActorMemberByWrapperType.Clear();
		ExpiredTalismanCooldowns.Clear();
		ExpiredExposureCooldowns.Clear();
		capturingUnitsField = null;
		capturingUnitsFieldResolved = false;
		lastTransientCachePruneYear = int.MinValue;
	}

	private static void PruneTransientCaches(int currentYear)
	{
		if (currentYear == lastTransientCachePruneYear)
		{
			return;
		}

		lastTransientCachePruneYear = currentYear;
		// 这些缓存只服务当前帧或当前一年的攻城过程，不应随历史战役常驻。
		LastDamageFrameByCity.Clear();
		BreakProfileByCity.Clear();
		PopulationByKingdom.Clear();
		CaptureRemainders.Clear();

		ExpiredTalismanCooldowns.Clear();
		foreach (KeyValuePair<(long CityId, long AttackerId), int> pair in LastBreakTalismanYear)
		{
			if (pair.Value != currentYear)
			{
				ExpiredTalismanCooldowns.Add(pair.Key);
			}
		}
		for (int i = 0; i < ExpiredTalismanCooldowns.Count; i++)
		{
			LastBreakTalismanYear.Remove(ExpiredTalismanCooldowns[i]);
		}

		ExpiredExposureCooldowns.Clear();
		foreach (KeyValuePair<(long CityId, long AttackerId, long ActorId), int> pair in LastJinDanExposureYear)
		{
			if (pair.Value != currentYear)
			{
				ExpiredExposureCooldowns.Add(pair.Key);
			}
		}
		for (int i = 0; i < ExpiredExposureCooldowns.Count; i++)
		{
			LastJinDanExposureYear.Remove(ExpiredExposureCooldowns[i]);
		}
	}

	private static int ScaleNativeDelta(long cityId, long attackerId, int originalDelta, float multiplier)
	{
		float safeMultiplier = Math.Clamp(multiplier, 0.30f, 1f);
		var key = (cityId, attackerId);
		CaptureRemainders.TryGetValue(key, out float remainder);
		float total = Math.Max(0, originalDelta) * safeMultiplier + remainder;
		int scaled = Math.Max(0, (int)Math.Floor(total));
		CaptureRemainders[key] = Math.Clamp(total - scaled, 0f, 0.999999f);
		return scaled;
	}

	private static bool IsDefenderPopulationRouted(Kingdom defenderKingdom, long defenderKingdomId, int frame)
	{
		if (defenderKingdom?.data == null || defenderKingdomId <= 0L) return false;
		int population = CountKingdomPopulationUpTo(defenderKingdom, defenderKingdomId, frame, XjSectFormationBalance.FormationRoutPopulationThreshold);
		return population < XjSectFormationBalance.FormationRoutPopulationThreshold;
	}

	private static int CountKingdomPopulationUpTo(Kingdom kingdom, long kingdomId, int frame, int limit)
	{
			if (kingdom?.data == null || kingdomId <= 0L) return 0;
			int frameBucket = frame / PopulationCacheFrames;
			if (PopulationByKingdom.TryGetValue(kingdomId, out CachedPopulation cached) && cached.FrameBucket == frameBucket) return cached.Count;
			int count = 0;
			IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
			for (int i = 0; i < cities.Count; i++)
			{
				City city = cities[i];
			if (city?.data == null || city.kingdom?.data?.id != kingdomId) continue;
			count += Math.Max(0, city.units?.Count ?? 0);
			if (count >= limit) break;
		}
		PopulationByKingdom[kingdomId] = new CachedPopulation { FrameBucket = frameBucket, Count = count };
		return count;
	}

	private static bool TryResolveOpposingAttackerSectId(City city, Kingdom attackingKingdom, long defenderSectId, out long attackerSectId)
	{
		attackerSectId = 0L;
		if (city == null || attackingKingdom == null || defenderSectId <= 0L) return false;
		IList capturingUnits = GetCapturingUnits(city);
		if (capturingUnits == null) return false;
		int scan = Math.Min(MaximumCapturingUnitScan, capturingUnits.Count);
		for (int i = 0; i < scan; i++)
		{
			Actor actor = ExtractActor(capturingUnits[i]);
			if (actor?.data == null || !actor.isAlive()) continue;
			Kingdom actorKingdom = actor.kingdom ?? actor.city?.kingdom;
			if (actorKingdom != attackingKingdom) continue;
			if (!TryResolveStoredActorSectId(actor, out long candidateSectId) || candidateSectId <= 0L || candidateSectId == defenderSectId) continue;
			attackerSectId = candidateSectId;
			return true;
		}
		return false;
	}

	private static bool TryResolveStoredActorSectId(Actor actor, out long sectId)
	{
		sectId = 0L;
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long storedSectId) && storedSectId > 0L)
		{
			sectId = storedSectId;
			return XjSectRepository.TryGetBySectId(sectId, out _);
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacySectId) && legacySectId > 0)
		{
			sectId = legacySectId;
			return XjSectRepository.TryGetBySectId(sectId, out _);
		}
		return false;
	}

	private static BreakProfile GetBreakProfile(City city, Kingdom attackingKingdom, long cityId, long attackerId, long attackerSectId, int frame)
	{
		if (BreakProfileByCity.TryGetValue(cityId, out CachedBreakProfile cached) && cached.Frame == frame && cached.AttackerKingdomId == attackerId && cached.AttackerSectId == attackerSectId) return cached.Profile;

		float[] realmScores = GetTopRealmScoreBuffer();
		int realmScoreCount = 0;
		int bestFormationRank = XjCraftProficiencySystem.RankNone;
		int assistantFormationMasters = 0;
		Actor breakTalismanBearer = null;
		Actor firstJinDanContributor = null;
		Actor secondJinDanContributor = null;
		IList capturingUnits = GetCapturingUnits(city);
		if (capturingUnits != null)
		{
			int scan = Math.Min(MaximumCapturingUnitScan, capturingUnits.Count);
			for (int i = 0; i < scan; i++)
			{
				Actor actor = ExtractActor(capturingUnits[i]);
				if (actor?.data == null || !actor.isAlive()) continue;
				Kingdom actorKingdom = actor.kingdom ?? actor.city?.kingdom;
				if (actorKingdom != attackingKingdom) continue;
				if (!TryResolveStoredActorSectId(actor, out long actorSectId) || actorSectId != attackerSectId) continue;
				InsertTopScore(realmScores, ref realmScoreCount, GetRealmBreakScore(actor));
				long participantId = ((BaseSystemData)actor.data).id;
				if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan)
				{
					if (firstJinDanContributor == null) firstJinDanContributor = actor;
					else if (secondJinDanContributor == null) secondJinDanContributor = actor;
				}
				if (breakTalismanBearer == null && XjCraftDomainRegistry.HasCarry(participantId, XjTalismanCatalog.BreakFormation)) breakTalismanBearer = actor;
				if (!actor.hasTrait(XjCraftTraitRules.FormationTraitId)) continue;
				int rank = XjCraftProficiencySystem.GetFormationRank(actor);
				if (rank > bestFormationRank)
				{
					if (bestFormationRank > XjCraftProficiencySystem.RankNone) assistantFormationMasters++;
					bestFormationRank = rank;
				}
				else if (rank > XjCraftProficiencySystem.RankNone) assistantFormationMasters++;
			}
		}

		float realmScore = 0f;
		for (int i = 0; i < realmScoreCount; i++) realmScore += realmScores[i];
		if (realmScore <= 0f) realmScore = 1f;
		float modifier = GetFormationMasterModifier(bestFormationRank)
			+ Math.Min(XjSectFormationBalance.MaximumAssistantFormationMasters, assistantFormationMasters) * 0.05f;
		float talismanModifier = 1f;
		int year = GetCurrentYear();
		var talismanKey = (cityId, attackerId);
		if (breakTalismanBearer != null && (!LastBreakTalismanYear.TryGetValue(talismanKey, out int usedYear) || usedYear != year)
			&& XjTalismanCombatService.TryConsumeBreakFormation(breakTalismanBearer))
		{
			LastBreakTalismanYear[talismanKey] = year;
			talismanModifier = 1.25f;
		}
		RecordJinDanBreakExposure(firstJinDanContributor, secondJinDanContributor, cityId, attackerId, year);
		BreakProfile result = new BreakProfile(realmScore, modifier, talismanModifier);
		BreakProfileByCity[cityId] = new CachedBreakProfile { Frame = frame, AttackerKingdomId = attackerId, AttackerSectId = attackerSectId, Profile = result };
		return result;
	}

	private static void RecordJinDanBreakExposure(Actor firstActor, Actor secondActor, long cityId, long attackerId, int year)
	{
		if (year <= 0) return;
		RecordJinDanBreakExposureForActor(firstActor, cityId, attackerId, year);
		if (!ReferenceEquals(firstActor, secondActor)) RecordJinDanBreakExposureForActor(secondActor, cityId, attackerId, year);
	}

	private static void RecordJinDanBreakExposureForActor(Actor actor, long cityId, long attackerId, int year)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		var key = (cityId, attackerId, actorId);
		if (LastJinDanExposureYear.TryGetValue(key, out int lastYear) && lastYear == year) return;
		LastJinDanExposureYear[key] = year;
		XjYinSiExposurePursuitSystem.RecordExternalJinDanAction(actor, 2f, "在外界强行撕裂宗门大阵", year);
	}

	private static IList GetCapturingUnits(City city)
	{
		if (city == null) return null;
		if (!capturingUnitsFieldResolved)
		{
			capturingUnitsFieldResolved = true;
			capturingUnitsField = typeof(City).GetField("_capturing_units", InstanceFlags)
				?? typeof(City).GetField("capturing_units", InstanceFlags)
				?? typeof(City).GetField("capturingUnits", InstanceFlags);
		}
		try { return capturingUnitsField?.GetValue(city) as IList; }
		catch { return null; }
	}

	private static Actor ExtractActor(object value)
	{
		if (value is Actor direct) return direct;
		if (value == null) return null;
		Type type = value.GetType();
		if (!ActorMemberByWrapperType.TryGetValue(type, out MemberInfo member))
		{
			FieldInfo field = type.GetField("actor", InstanceFlags)
				?? type.GetField("_actor", InstanceFlags)
				?? type.GetField("unit", InstanceFlags);
			member = field ?? (MemberInfo)(type.GetProperty("actor", InstanceFlags)
				?? type.GetProperty("unit", InstanceFlags));
			ActorMemberByWrapperType[type] = member;
		}
		try
		{
			return member switch
			{
				FieldInfo field => field.GetValue(value) as Actor,
				PropertyInfo property => property.GetValue(value) as Actor,
				_ => null
			};
		}
		catch { return null; }
	}

	private static float[] GetTopRealmScoreBuffer()
	{
		int length = Math.Max(1, XjSectFormationBalance.MaximumContributors);
		if (topRealmScores == null || topRealmScores.Length != length)
		{
			topRealmScores = new float[length];
		}
		else
		{
			Array.Clear(topRealmScores, 0, topRealmScores.Length);
		}
		return topRealmScores;
	}

	private static void InsertTopScore(float[] scores, ref int count, float score)
	{
		if (score <= 0f || scores.Length == 0) return;
		if (count >= scores.Length && score <= scores[scores.Length - 1]) return;
		int index = Math.Min(count, scores.Length - 1);
		while (index > 0 && scores[index - 1] < score)
		{
			if (index < scores.Length) scores[index] = scores[index - 1];
			index--;
		}
		scores[index] = score;
		if (count < scores.Length) count++;
	}

	private static float GetRealmBreakScore(Actor actor)
	{
		return XjRealmSuppression.GetRealmTier(actor) switch
		{
			XjRealmSuppression.TierJinDan => 20f,
			XjRealmSuppression.TierZiFu => 8f,
			XjRealmSuppression.TierZhuJi => 3f,
			XjRealmSuppression.TierLianQi => 1.5f,
			_ => 1f
		};
	}

	private static float GetFormationMasterModifier(int rank)
	{
		// 阵法师品级不再直接放大占领或破阵数值，只影响布阵速度与维修效果。
		return 1f;
	}

	private static int GetCurrentYear()
	{
		try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
		catch { return 0; }
	}

}
