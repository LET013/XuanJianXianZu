using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 妖属大圣的轻量“果位化生”运行态。
///
/// 原生实体边界严格对齐龙属：
/// 1. 十二种大圣 ActorAsset 全部从同一个已验证的 civ_seal 底盘克隆；
/// 2. spawnNewUnit 时永远传 null subspecies，让 WorldBox 先完成自己的原生创建事务；
/// 3. 原生 tile/文明状态完整以后，才写名字、亚种、修炼身份、排行榜和果位映照；
/// 4. 不再强制 setDefaultKingdom，也不把“kingdom 必须非空”当作野生单位成立条件；
/// 5. 十二席仍各自保留独立 ActorAsset/贴图/动作/技能，兼容已有存档中的 asset_id。
/// </summary>
internal static class XjYaoShuGreatSageSystem
{
	internal const string ModuleId = "world.yaoshu-great-sage";
	internal const string GreatSageTraitId = "XjYaoShuGreatSage";

	private const string StableActorTemplateId = "civ_seal";
	private const int ManifestationStartYear = 300;
	// 大圣的降临不是短周期刷怪。每二百年只开一次天数窗口，窗口内再随机落下三至五席。
	private const int ManifestationCheckIntervalYears = 200;
	private const int RequiredIndependentZhenJunCount = 3;
	private const int PopulationManifestationThreshold = 4000;
	private const int MinimumManifestationBatchSize = 3;
	private const int MaximumManifestationBatchSize = 5;
	private const int StrategicPulseYears = 10;
	private const int SpawnProbeBudget = 96;
	private const int SpriteFrameCount = 6;
	private const string SpritePathRoot = "actors/species/other/";
	private const float GreatSageAttributeMultiplier = 1.5f;
	private const float GreatSageHealthBaseline = 40000000f;
	private const float GreatSageDamageBaseline = 1500000f;
	private const float GreatSageArmorBaseline = 2500f;
	private const float GreatSageScale = 0.16f;
	private const int ReentryCooldownYears = 150;
	private const float AttackCueCooldownSeconds = 0.75f;
	private const float AttackAnimationFrameSeconds = 0.033f;
	private const float SkillCooldownSeconds = 14f;
	private const int TerrainSampleBudget = 16;
	private const int MinTerrainRadius = 4;
	private const int MaxTerrainRadius = 7;
	private const string GreatSageEffectRoot = "effects/YaoShu/DaSheng/";
	private const string GreatSageSubspeciesName = "妖属大圣";

	private static readonly string[] GreatSageSubspeciesTraitIds =
	{
		"accelerated_healing",
		"cold_resistance",
		"heat_resistance",
		"long_lifespan",
		"photosynthetic_skin",
		"gaia_roots",
		"gift_of_void",
		"hovering"
	};

	private static readonly string[] GreatSageForbiddenSubspeciesTraitIds =
	{
		"population_minimal",
		"population_small",
		"population_moderate",
		"population_large",
		"population_expansive",
		"advanced_hippocampus",
		"prefrontal_cortex",
		"exoskeleton",
		"stomach",
		"diet_carnivore",
		"monophasic_sleep",
		"polyphasic_sleep",
		"nocturnal_dormancy",
		"reproduction_sexual",
		"reproduction_asexual",
		"reproduction_vegetative",
		"reproduction_strategy_oviparity",
		"reproduction_strategy_viviparity",
		"gestation_short",
		"gestation_moderate",
		"gestation_long",
		"gestation_extremely_long",
		"egg_orb",
		"fins"
	};

	// WorldBox 的 Subspecies 归属于具体 ActorAsset。十二圣显示名/特质可以相同，
	// 但 runtime Subspecies 绝不跨 ActorAsset 共享。
	private static readonly Dictionary<string, Subspecies> RuntimeSubspeciesByAssetId =
		new Dictionary<string, Subspecies>(StringComparer.Ordinal);

	private sealed class Definition
	{
		internal string SlotId;
		internal string ActorAssetId;
		internal string TemplateId; // 仅保留旧数据语义；新注册统一使用 StableActorTemplateId。
		internal string DisplayName;
		internal string DaoTu;
		internal string SkillName;
		internal string YaoMinName;
		internal bool PreferWater;
		internal float Health;
		internal float Damage;
		internal float Armor;
		internal float Speed;
		internal float AttackSpeed;
		internal float Mass;
		internal string[] NativeTraits;
		internal string VisualPrefix;
		internal int AttackFrameCount;
		internal string EffectId;
		internal string TerrainStyle;
		internal float SkillEffectScale;
		internal float SkillDamageMultiplier;
		internal int TerrainRadius;

		internal string FruitId => XjGuoWeiCalculator.BuildGuoWeiSlotName(DaoTu, XjGuoWeiCalculator.ZhengWei, 1);
	}

	private sealed class SlotState
	{
		internal long ActorId;
		internal int NextAttemptYear;
		internal int LastStrategicYear;
		internal int LastManifestationYear;
		internal int LastDepartureYear;
		internal int ManifestationCount;
	}

	private sealed class GreatSageVisualFrames
	{
		internal Sprite[] Idle;
		internal Sprite[] Walk;
		internal Sprite[] Breathing;
		internal Sprite[] Run;
		internal Sprite[] Attack;
		internal Sprite Fallback;
	}

	internal sealed class CodexItem
	{
		internal string Name;
		internal string DaoTu;
		internal string Skill;
		internal string YaoMin;
		internal string Fruit;
		internal int YaoMinPopulation;
		internal bool Alive;
		internal int ManifestationCount;
		internal int LastManifestationYear;
		internal int LastDepartureYear;
		internal int NextAttemptYear;
	}

	private static readonly Definition[] Definitions =
	{
		new Definition
		{
			SlotId = "chunhuo", ActorAssetId = "xj_yaoshu_dasheng_chunhuo", TemplateId = "civ_chicken",
			DisplayName = "鹑火大圣", DaoTu = "并火", SkillName = "赤羽焚天", PreferWater = false,
			YaoMinName = "原生鸡妖民", Health = 28000f, Damage = 1180f, Armor = 62f, Speed = 92f, AttackSpeed = 86f, Mass = 55f,
			NativeTraits = new[] { "heat_resistance", "sunblessed", "enhanced_strength" },
			VisualPrefix = "neutral_zurael", AttackFrameCount = 29,
			EffectId = "YangYanLianJi", TerrainStyle = "fire", SkillEffectScale = 0.19f, SkillDamageMultiplier = 0.85f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "jinghai", ActorAssetId = "xj_yaoshu_dasheng_jinghai", TemplateId = "dragon",
			DisplayName = "靖海大圣", DaoTu = "合水", SkillName = "镇潮靖海", PreferWater = true,
			YaoMinName = "原生龙妖民", Health = 42000f, Damage = 940f, Armor = 78f, Speed = 68f, AttackSpeed = 62f, Mass = 110f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "regeneration", "boosted_vitality", "peaceful" },
			VisualPrefix = "f6_whitewyvern", AttackFrameCount = 21,
			EffectId = "YingYueLianHua", TerrainStyle = "tide", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.72f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "bailin", ActorAssetId = "xj_yaoshu_dasheng_bailin", TemplateId = "civ_rhino",
			DisplayName = "明阳白麟大圣", DaoTu = "明阳", SkillName = "明阳照骨", PreferWater = false,
			YaoMinName = "原生犀牛妖民", Health = 36000f, Damage = 1040f, Armor = 74f, Speed = 76f, AttackSpeed = 70f, Mass = 95f,
			NativeTraits = new[] { "sunblessed", "regeneration", "immune", "peaceful" },
			VisualPrefix = "f6_elklodon", AttackFrameCount = 13,
			EffectId = "DaRiZhuiYuan", TerrainStyle = "fire", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.82f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "baixiang", ActorAssetId = "xj_yaoshu_dasheng_baixiang", TemplateId = "civ_buffalo",
			DisplayName = "煞炁白象大圣", DaoTu = "煞炁", SkillName = "煞岳镇身", PreferWater = false,
			YaoMinName = "原生水牛妖民", Health = 50000f, Damage = 1320f, Armor = 82f, Speed = 58f, AttackSpeed = 55f, Mass = 145f,
			NativeTraits = new[] { "enhanced_strength", "tough", "boosted_vitality" },
			VisualPrefix = "neutral_thunderhorn", AttackFrameCount = 19,
			EffectId = "ZhenYueBeng", TerrainStyle = "smash", SkillEffectScale = 0.27f, SkillDamageMultiplier = 0.95f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "tingsongli", ActorAssetId = "xj_yaoshu_dasheng_tingsongli", TemplateId = "civ_cat",
			DisplayName = "寒炁雪岭听松狸大圣", DaoTu = "寒炁", SkillName = "听雪藏形", PreferWater = false,
			YaoMinName = "原生猫妖民", Health = 24000f, Damage = 860f, Armor = 55f, Speed = 128f, AttackSpeed = 105f, Mass = 34f,
			NativeTraits = new[] { "cold_resistance", "dodge", "eagle_eyed", "peaceful" },
			VisualPrefix = "neutral_ghostlynx", AttackFrameCount = 26,
			EffectId = "TaiYinBingFeng", TerrainStyle = "frost", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.70f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "jinchifu", ActorAssetId = "xj_yaoshu_dasheng_jinchifu", TemplateId = "civ_bee",
			DisplayName = "瑞炁玄匮金翅蝠大圣", DaoTu = "瑞炁", SkillName = "玄匮振金", PreferWater = false,
			YaoMinName = "原生蜂妖民", Health = 26000f, Damage = 960f, Armor = 58f, Speed = 145f, AttackSpeed = 112f, Mass = 28f,
			NativeTraits = new[] { "hovering", "dodge", "eagle_eyed", "peaceful" },
			VisualPrefix = "neutral_silverbeak", AttackFrameCount = 19,
			EffectId = "JinDanLeiFa", TerrainStyle = "thunder", SkillEffectScale = 0.17f, SkillDamageMultiplier = 0.74f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "xuanlei", ActorAssetId = "xj_yaoshu_dasheng_xuanlei", TemplateId = "civ_crocodile",
			DisplayName = "玄雷金鳞大圣", DaoTu = "玄雷", SkillName = "玄雷裂天", PreferWater = false,
			YaoMinName = "原生鳄妖民", Health = 34000f, Damage = 1410f, Armor = 68f, Speed = 98f, AttackSpeed = 94f, Mass = 78f,
			NativeTraits = new[] { "enhanced_strength", "eagle_eyed", "tough" },
			VisualPrefix = "boss_paragon", AttackFrameCount = 25,
			EffectId = "JinDanLeiFa", TerrainStyle = "thunder", SkillEffectScale = 0.22f, SkillDamageMultiplier = 1.00f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "yuanzhao", ActorAssetId = "xj_yaoshu_dasheng_yuanzhao", TemplateId = "civ_crocodile",
			DisplayName = "渊照寒渊螭大圣", DaoTu = "渊照", SkillName = "渊镜寒照", PreferWater = true,
			YaoMinName = "原生鳄妖民", Health = 40000f, Damage = 1120f, Armor = 75f, Speed = 86f, AttackSpeed = 78f, Mass = 102f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "regeneration", "peaceful" },
			VisualPrefix = "f6_frostdrake", AttackFrameCount = 28,
			EffectId = "YingYueLianHua", TerrainStyle = "frost", SkillEffectScale = 0.20f, SkillDamageMultiplier = 0.86f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "zheqi", ActorAssetId = "xj_yaoshu_dasheng_zheqi", TemplateId = "civ_crocodile",
			DisplayName = "谪炁九首蛟大圣", DaoTu = "谪炁", SkillName = "九首谪潮", PreferWater = false,
			YaoMinName = "原生鳄妖民", Health = 45500f, Damage = 1260f, Armor = 78f, Speed = 74f, AttackSpeed = 76f, Mass = 122f,
			NativeTraits = new[] { "enhanced_strength", "tough", "regeneration" },
			VisualPrefix = "neutral_hydrax", AttackFrameCount = 24,
			EffectId = "YingYueLianHua", TerrainStyle = "tide", SkillEffectScale = 0.21f, SkillDamageMultiplier = 0.93f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "kanshui", ActorAssetId = "xj_yaoshu_dasheng_kanshui", TemplateId = "civ_buffalo",
			DisplayName = "坎水负岳冰罴大圣", DaoTu = "坎水", SkillName = "负岳沉渊", PreferWater = true,
			YaoMinName = "原生水牛妖民", Health = 52500f, Damage = 1110f, Armor = 92f, Speed = 54f, AttackSpeed = 54f, Mass = 158f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "tough", "boosted_vitality", "peaceful" },
			VisualPrefix = "f6_waterbear", AttackFrameCount = 33,
			EffectId = "ZhenYueBeng", TerrainStyle = "smash", SkillEffectScale = 0.29f, SkillDamageMultiplier = 0.92f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "taiyin", ActorAssetId = "xj_yaoshu_dasheng_taiyin", TemplateId = "civ_cat",
			DisplayName = "太阴啸月霜狼大圣", DaoTu = "太阴", SkillName = "啸月追魂", PreferWater = false,
			YaoMinName = "原生猫妖民", Health = 27500f, Damage = 1010f, Armor = 61f, Speed = 138f, AttackSpeed = 110f, Mass = 42f,
			NativeTraits = new[] { "cold_resistance", "dodge", "eagle_eyed" },
			VisualPrefix = "f6_spiritwolf", AttackFrameCount = 29,
			EffectId = "TaiYinBingFeng", TerrainStyle = "frost", SkillEffectScale = 0.21f, SkillDamageMultiplier = 0.78f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "ziqi", ActorAssetId = "xj_yaoshu_dasheng_ziqi", TemplateId = "civ_bee",
			DisplayName = "紫炁云翼大圣", DaoTu = "紫炁", SkillName = "紫云遁空", PreferWater = false,
			YaoMinName = "原生蜂妖民", Health = 29500f, Damage = 1030f, Armor = 65f, Speed = 142f, AttackSpeed = 114f, Mass = 32f,
			NativeTraits = new[] { "hovering", "dodge", "eagle_eyed" },
			VisualPrefix = "neutral_skywing", AttackFrameCount = 24,
			EffectId = "JinDanLeiFa", TerrainStyle = "thunder", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.82f, TerrainRadius = 6
		}
	};

	private static readonly SlotState[] States = CreateEmptyStates();
	private static readonly Dictionary<long, float> NextAttackCueAt = new Dictionary<long, float>(12);
	private static readonly Dictionary<long, float> NextSkillAt = new Dictionary<long, float>(12);
	private static readonly Dictionary<long, float> AttackAnimationStartedAt = new Dictionary<long, float>(12);
	private static readonly Dictionary<string, GreatSageVisualFrames> VisualFramesByAssetId = new Dictionary<string, GreatSageVisualFrames>(StringComparer.Ordinal);
	private static readonly HashSet<long> RankActorIds = new HashSet<long>();
	private static long[] _rankActorIdSnapshot = Array.Empty<long>();
	private static bool _rankActorIdSnapshotDirty = true;
	private static int _rankMembershipRevision;
	private static bool _rankRegistryRecoveryPending = true;
	private static bool _legacyWorldRecoveryPending = true;
	private static int _reconcileDepartureGraceThroughYear = -1;
	private static readonly AttackAction GreatSageAttackAction = OnGreatSageAttackTarget;
	private static bool _assetsInitialized;
	private static bool _reconciledAfterLoad;

	internal static int RankMembershipRevision => _rankMembershipRevision;

	internal static void Init()
	{
		if (_assetsInitialized) return;
		ActorAssetLibrary library = AssetManager.actor_library;
		if (library == null) return;

		for (int i = 0; i < Definitions.Length; i++)
		{
			TryRegisterActorAsset(library, Definitions[i]);
		}
		XjYaoShuSapientSpecies.Init();
		_assetsInitialized = true;
	}

	internal static void TickAnnual(int currentYear)
	{
		currentYear = Math.Max(0, currentYear);
		if (!_reconciledAfterLoad) ReconcileAfterLoad(currentYear);
		if (!_assetsInitialized) Init();
		if (World.world?.units == null || !_assetsInitialized) return;

		bool changed = false;
		// 先完成十二席的存亡对账，再从同一批到期空席中随机抽取三至五席。
		// 不能在逐槽循环中即时化生，否则定义数组的先后顺序会固定每一轮的降临者。
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			SlotState state = States[i];
			if (TryResolveLivingSage(definition, state.ActorId, out Actor living))
			{
				EnsureGreatSageIdentity(living, definition, currentYear);
				EnsureGreatSageAttackCallback(living);
				if (state.NextAttemptYear != 0)
				{
					state.NextAttemptYear = 0;
					changed = true;
				}
				if (currentYear - state.LastStrategicYear >= StrategicPulseYears)
				{
					ApplyStrategicPulse(living, definition);
					state.LastStrategicYear = currentYear;
					changed = true;
				}
				continue;
			}

			if (state.ActorId > 0L)
			{
				if (currentYear <= _reconcileDepartureGraceThroughYear) continue;
				long departedActorId = state.ActorId;
				NextAttackCueAt.Remove(departedActorId);
				NextSkillAt.Remove(departedActorId);
				AttackAnimationStartedAt.Remove(departedActorId);
				RecordDeparture(definition, departedActorId, currentYear);
				state.ActorId = 0L;
				state.LastStrategicYear = 0;
				state.LastDepartureYear = currentYear;
				state.NextAttemptYear = ScheduleReentryAttempt(currentYear);
				changed = true;
			}
			else if (state.NextAttemptYear <= 0)
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
			}

		}

		bool hasManifestationCondition = HasManifestationCondition();
		// 绝大多数年份没有到期空席，不创建候选 List/HashSet，也不读取果位表。
		HashSet<int> selectedSlots = hasManifestationCondition && HasDueManifestationSlot(currentYear)
			? SelectManifestationBatch(currentYear)
			: null;
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			SlotState state = States[i];
			if (state.ActorId > 0L
				|| currentYear < ManifestationStartYear
				|| currentYear < state.NextAttemptYear
				|| !CanAttemptManifestation(definition)) continue;

			if (!hasManifestationCondition
				|| selectedSlots == null
				|| !selectedSlots.Contains(i)
				|| IsFruitHeldByLivingRealActor(definition.FruitId))
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
				continue;
			}

			if (TryManifest(definition, currentYear, i, out Actor manifested))
			{
				CommitManifestationState(state, manifested, currentYear);
				changed = true;
				XjYaoShuSapientSpecies.TryAwakenForGreatSage(definition.SlotId, manifested.current_tile, currentYear);
				XjYaoShuHalfBloodlineSystem.TryBestowAfterGreatSage(manifested, definition.SlotId, currentYear);
				RecordManifestation(manifested, definition, currentYear);
			}
			else
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
			}
		}

		if (changed) MarkChanged();
	}

	private static void CommitManifestationState(SlotState state, Actor manifested, int currentYear)
	{
		state.ActorId = GetActorId(manifested);
		state.NextAttemptYear = 0;
		state.LastStrategicYear = currentYear;
		state.LastManifestationYear = currentYear;
		state.ManifestationCount++;
	}

	internal static void ReconcileAfterLoad(int currentYear)
	{
		currentYear = Math.Max(0, currentYear);
		_reconciledAfterLoad = true;
		_reconcileDepartureGraceThroughYear = Math.Max(_reconcileDepartureGraceThroughYear, currentYear);
		if (!_assetsInitialized) Init();
		bool changed = RecoverLegacyLivingSagesFromWorldCold(currentYear);
		for (int i = 0; i < Definitions.Length; i++)
		{
			SlotState state = States[i];
			if (TryResolveLivingSage(Definitions[i], state.ActorId, out Actor living))
			{
				EnsureGreatSageIdentity(living, Definitions[i], currentYear);
				EnsureGreatSageAttackCallback(living);
				if (state.NextAttemptYear != 0)
				{
					state.NextAttemptYear = 0;
					changed = true;
				}
				continue;
			}
			if (state.ActorId > 0L) continue;
			if (state.NextAttemptYear <= 0)
			{
				state.NextAttemptYear = ScheduleNextAttempt(Definitions[i], currentYear);
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	internal static int TryDebugManifestAll(int currentYear)
	{
		currentYear = Math.Max(0, currentYear);
		if (!_reconciledAfterLoad) ReconcileAfterLoad(currentYear);
		if (!_assetsInitialized) Init();
		if (World.world?.units == null || !_assetsInitialized) return 0;

		int manifestedCount = 0;
		bool changed = false;
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			SlotState state = States[i];
			if (TryResolveLivingSage(definition, state.ActorId, out Actor living))
			{
				EnsureGreatSageIdentity(living, definition, currentYear);
				EnsureGreatSageAttackCallback(living);
				continue;
			}
			if (state.ActorId > 0L)
			{
				if (currentYear <= _reconcileDepartureGraceThroughYear) continue;
				long departedActorId = state.ActorId;
				NextAttackCueAt.Remove(departedActorId);
				NextSkillAt.Remove(departedActorId);
				AttackAnimationStartedAt.Remove(departedActorId);
				RecordDeparture(definition, departedActorId, currentYear);
				state.ActorId = 0L;
				state.LastStrategicYear = 0;
				state.LastDepartureYear = currentYear;
				state.NextAttemptYear = ScheduleReentryAttempt(currentYear);
				changed = true;
			}
			if (!CanAttemptManifestation(definition)) continue;
			if (IsFruitHeldByLivingRealActor(definition.FruitId))
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
				continue;
			}
			if (TryManifest(definition, currentYear, i, out Actor manifested))
			{
				CommitManifestationState(state, manifested, currentYear);
				manifestedCount++;
				changed = true;
				XjYaoShuSapientSpecies.TryAwakenForGreatSage(definition.SlotId, manifested.current_tile, currentYear);
				XjYaoShuHalfBloodlineSystem.TryBestowAfterGreatSage(manifested, definition.SlotId, currentYear);
				RecordManifestation(manifested, definition, currentYear);
			}
			else
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
			}
		}
		if (changed) MarkChanged();
		return manifestedCount;
	}

	internal static bool IsExternalPositionOccupied(string guoWei) => false;

	internal static string ExportPayload()
	{
		string[] parts = new string[States.Length];
		for (int i = 0; i < States.Length; i++)
		{
			SlotState state = States[i];
			parts[i] = state.ActorId.ToString(CultureInfo.InvariantCulture) + ","
				+ state.NextAttemptYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.LastStrategicYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.LastManifestationYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.LastDepartureYear.ToString(CultureInfo.InvariantCulture) + ","
				+ state.ManifestationCount.ToString(CultureInfo.InvariantCulture);
		}
		return string.Join(";", parts);
	}

	internal static void ImportPayload(int schemaVersion, string payload)
	{
		ClearRuntime();
		if (schemaVersion <= 0 || string.IsNullOrWhiteSpace(payload)) return;
		string[] slots = payload.Split(';');
		int count = Math.Min(slots.Length, States.Length);
		for (int i = 0; i < count; i++)
		{
			string[] fields = (slots[i] ?? string.Empty).Split(',');
			if (fields.Length > 0 && long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId)) States[i].ActorId = Math.Max(0L, actorId);
			if (fields.Length > 1 && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nextYear)) States[i].NextAttemptYear = Math.Max(0, nextYear);
			if (fields.Length > 2 && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pulseYear)) States[i].LastStrategicYear = Math.Max(0, pulseYear);
			if (fields.Length > 3 && int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int manifestationYear)) States[i].LastManifestationYear = Math.Max(0, manifestationYear);
			if (fields.Length > 4 && int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int departureYear)) States[i].LastDepartureYear = Math.Max(0, departureYear);
			if (fields.Length > 5 && int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int countValue)) States[i].ManifestationCount = Math.Max(0, countValue);
		}
		_reconciledAfterLoad = false;
		_rankRegistryRecoveryPending = true;
		_legacyWorldRecoveryPending = true;
	}

	internal static void EnsureRankMembership()
	{
		if (World.world?.units == null) return;
		int currentYear = Math.Max(0, World.world.map_stats?.year ?? 0);
		if (!_reconciledAfterLoad) ReconcileAfterLoad(currentYear);
		RecoverLegacyLivingSagesFromRegistry(currentYear);
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			if (!TryResolveLivingSage(definition, States[i].ActorId, out Actor sage)) continue;
			EnsureGreatSageIdentity(sage, definition, currentYear);
			XjActorRegistry.Register(sage, out _);
			XjCultivatorCache.CheckAndUpdate(sage);
			TrackRankActor(GetActorId(sage));
		}
	}

	internal static IReadOnlyList<long> GetRankActorIds()
	{
		if (_rankActorIdSnapshotDirty)
		{
			_rankActorIdSnapshot = new long[RankActorIds.Count];
			RankActorIds.CopyTo(_rankActorIdSnapshot);
			Array.Sort(_rankActorIdSnapshot);
			_rankActorIdSnapshotDirty = false;
		}
		return _rankActorIdSnapshot;
	}

	private static bool RecoverLegacyLivingSagesFromWorldCold(int currentYear)
	{
		if (!_legacyWorldRecoveryPending) return false;
		IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
		if (units == null || units.Count == 0) return false;
		_legacyWorldRecoveryPending = false;
		bool changed = false;
		for (int i = 0; i < units.Count; i++)
		{
			Actor actor = units[i];
			if (!XjSafeCore.IsAliveActor(actor) || !TryFindDefinition(actor, out Definition definition)) continue;
			int slotIndex = FindDefinitionIndex(definition.ActorAssetId);
			if (slotIndex < 0) continue;
			long actorId = GetActorId(actor);
			if (actorId <= 0L) continue;
			SlotState state = States[slotIndex];
			if (state.ActorId > 0L && TryResolveLivingSage(definition, state.ActorId, out Actor alreadyBound))
			{
				if (GetActorId(alreadyBound) != actorId) continue;
			}
			else if (state.ActorId != actorId)
			{
				state.ActorId = actorId;
				state.NextAttemptYear = 0;
				state.LastStrategicYear = Math.Max(state.LastStrategicYear, currentYear);
				changed = true;
			}
			EnsureGreatSageIdentity(actor, definition, currentYear);
			EnsureGreatSageAttackCallback(actor);
			XjActorRegistry.Register(actor, out _);
			TrackRankActor(actorId);
		}
		return changed;
	}

	private static void RecoverLegacyLivingSagesFromRegistry(int currentYear)
	{
		if (!_rankRegistryRecoveryPending || XjActorRegistry.Count <= 0) return;
		_rankRegistryRecoveryPending = false;
		IReadOnlyList<Actor> known = XjActorRegistry.Snapshot();
		bool changed = false;
		for (int i = 0; i < known.Count; i++)
		{
			Actor actor = known[i];
			if (!XjSafeCore.IsAliveActor(actor) || !TryFindDefinition(actor, out Definition definition)) continue;
			int slotIndex = FindDefinitionIndex(definition.ActorAssetId);
			if (slotIndex < 0) continue;
			long actorId = GetActorId(actor);
			if (actorId <= 0L) continue;
			SlotState state = States[slotIndex];
			if (state.ActorId <= 0L)
			{
				state.ActorId = actorId;
				state.NextAttemptYear = 0;
				state.LastStrategicYear = Math.Max(state.LastStrategicYear, currentYear);
				changed = true;
			}
			else if (state.ActorId != actorId) continue;
			EnsureGreatSageIdentity(actor, definition, currentYear);
			EnsureGreatSageAttackCallback(actor);
			TrackRankActor(actorId);
		}
		if (changed) MarkChanged();
	}

	private static int FindDefinitionIndex(string actorAssetId)
	{
		if (string.IsNullOrWhiteSpace(actorAssetId)) return -1;
		for (int i = 0; i < Definitions.Length; i++)
		{
			if (string.Equals(Definitions[i].ActorAssetId, actorAssetId, StringComparison.Ordinal)) return i;
		}
		return -1;
	}

	private static void TrackRankActor(long actorId)
	{
		if (actorId <= 0L || !RankActorIds.Add(actorId)) return;
		_rankActorIdSnapshotDirty = true;
		unchecked { _rankMembershipRevision++; }
	}

	private static void RemoveRankActor(long actorId)
	{
		if (actorId <= 0L || !RankActorIds.Remove(actorId)) return;
		_rankActorIdSnapshotDirty = true;
		unchecked { _rankMembershipRevision++; }
	}

	internal static void ClearRuntime()
	{
		for (int i = 0; i < States.Length; i++)
		{
			States[i].ActorId = 0L;
			States[i].NextAttemptYear = 0;
			States[i].LastStrategicYear = 0;
			States[i].LastManifestationYear = 0;
			States[i].LastDepartureYear = 0;
			States[i].ManifestationCount = 0;
		}
		NextAttackCueAt.Clear();
		NextSkillAt.Clear();
		AttackAnimationStartedAt.Clear();
		RankActorIds.Clear();
		_rankActorIdSnapshot = Array.Empty<long>();
		_rankActorIdSnapshotDirty = true;
		unchecked { _rankMembershipRevision++; }
		_rankRegistryRecoveryPending = true;
		_legacyWorldRecoveryPending = true;
		_reconcileDepartureGraceThroughYear = -1;
		RuntimeSubspeciesByAssetId.Clear();
		_reconciledAfterLoad = false;
	}

	private static SlotState[] CreateEmptyStates()
	{
		SlotState[] states = new SlotState[Definitions.Length];
		for (int i = 0; i < states.Length; i++) states[i] = new SlotState();
		return states;
	}

	private static void TryRegisterActorAsset(ActorAssetLibrary library, Definition definition)
	{
		if (library == null || definition == null) return;
		try
		{
			string templateId = ResolveStableTemplate(library);
			ActorAsset asset = library.get(definition.ActorAssetId) ?? library.clone(definition.ActorAssetId, templateId);
			if (asset == null) return;

			string localeKey = "xuanjian_" + definition.ActorAssetId;
			LocalizedTextManager.add(localeKey, definition.DisplayName, false, string.Empty, true);
			asset.name_locale = localeKey;
			asset.texture_id = definition.ActorAssetId;
			asset.icon = "XjYaoShuGreatSage";
			asset.show_icon_inspect_window = true;
			asset.show_icon_inspect_window_id = "XjYaoShuGreatSage";
			asset.color_hex = "#FFFFFF";
			asset.color = Toolbox.makeColor(asset.color_hex);
			asset.default_animal = false;
			asset.civ = false;
			asset.unit_other = true;
			asset.use_phenotypes = false;
			asset.has_advanced_textures = false;
			asset.has_baby_form = false;
			asset.can_have_subspecies = true;
			asset.can_evolve_into_new_species = false;
			asset.can_turn_into_zombie = false;
			asset.need_colored_sprite = false;
			asset.needs_to_be_explored = false;
			asset.unlocked_with_achievement = false;
			asset.show_in_knowledge_window = true;
			asset.show_for_unlockables_ui = true;
			asset.has_soul = true;
			asset.inspect_home = false;
			asset.inspect_show_species = false;
			// 大圣不使用 WorldBox 的等级/经验体系。civ_seal 底盘默认开启该面板，
			// 但妖属面板没有 i_lifespan 原生节点，开启后会在 UnitStatsElement:64
			// 空引用。龙属沿用 dragon 底盘本来就是 false；这里显式对齐龙属，
			// 不影响特质编辑、玄鉴修炼、道途、境界或排行榜。
			asset.inspect_experience = false;
			asset.can_be_inspected = true;
			asset.visible_on_minimap = false;
			// 龙属该值为 true 且稳定；不再为了绕 UI NRE 改变 ActorAsset 原生能力面。
			asset.can_edit_traits = true;
			asset.can_talk_with = false;
			asset.control_can_talk = false;
			asset.use_items = false;
			asset.take_items = false;
			asset.can_flip = true;
			asset.check_flip = delegate { return true; };
			asset.disable_jump_animation = true;
			asset.job = new[] { "random_move" };
			asset.civ_base_cities = 0;
			asset.kingdom_id_civilization = string.Empty;
			asset.kingdom_id_wild = "dragons";
			asset.base_stats["birth_rate"] = 0f;
			asset.base_stats["lifespan"] = 5000f;
			asset.actor_size = ActorSize.S17_Dragon;
			asset.base_stats["health"] = Mathf.Max(definition.Health, GreatSageHealthBaseline) * GreatSageAttributeMultiplier;
			asset.base_stats["damage"] = Mathf.Max(definition.Damage, GreatSageDamageBaseline) * GreatSageAttributeMultiplier;
			asset.base_stats["armor"] = Mathf.Max(definition.Armor, GreatSageArmorBaseline) * GreatSageAttributeMultiplier;
			asset.base_stats["speed"] = definition.Speed * GreatSageAttributeMultiplier;
			asset.base_stats["attack_speed"] = definition.AttackSpeed * GreatSageAttributeMultiplier;
			asset.base_stats["mass"] = definition.Mass * GreatSageAttributeMultiplier;
			asset.base_stats["scale"] = GreatSageScale;
			asset.base_stats["area_of_effect"] = 2f;
			asset.base_stats["targets"] = 3f;
			ApplyGreatSageDefaultSubspeciesTraits(asset);

			// 顺序与龙属一致：ActorAsset 元数据先完整提交/unlock，再建立贴图缓存与 override。
			asset.unlock(false);
			TryConfigureActorVisuals(library, asset, definition);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][妖属大圣] ActorAsset注册跳过 " + definition.SlotId + ": " + ex.GetType().Name);
		}
	}

	private static string ResolveStableTemplate(ActorAssetLibrary library)
	{
		if (library == null) return StableActorTemplateId;
		if (library.get(StableActorTemplateId) != null) return StableActorTemplateId;
		if (library.get("human") != null) return "human";
		if (library.get("$mob$") != null) return "$mob$";
		if (library.get("dragon") != null) return "dragon";
		return StableActorTemplateId;
	}

	private static void TryConfigureActorVisuals(ActorAssetLibrary library, ActorAsset asset, Definition definition)
	{
		if (library == null || asset == null || definition == null) return;
		asset.has_override_sprite = false;
		asset.get_override_sprite = null;
		VisualFramesByAssetId.Remove(definition.ActorAssetId);
		try
		{
			string mainPath = GetMainSpritePath(definition);
			asset.texture_id = definition.ActorAssetId;
			asset.texture_asset = new ActorTextureSubAsset(mainPath, false);
			if (asset.texture_asset != null) asset.texture_asset.texture_path_main = mainPath.TrimEnd('/', '\\');
			asset.animation_walk = BuildAnimationNames("walk_");
			asset.animation_idle = BuildAnimationNames("idle_");
			asset.animation_swim = asset.animation_walk;
			asset.animation_walk_speed = 6f;
			asset.animation_idle_speed = 6f;
			asset.animation_swim_speed = 6f;
			asset.animation_speed_based_on_walk_speed = false;
			asset._cached_sprite = null;
			asset.cached_sprite = null;

			// 与龙属相同：先让原生 ActorAssetLibrary 建完整动画容器，再读取自定义动作。
			library.loadTexturesAndSprites(asset);
			PrimeSpriteDirectory(definition);
			GreatSageVisualFrames frames = BuildGreatSageVisualFrames(definition);
			Sprite banner = frames.Fallback;
			if (banner == null)
			{
				Debug.LogWarning("[玄鉴][妖属大圣] 未找到贴图，保留原生外观 " + definition.SlotId);
				return;
			}
			asset._cached_sprite = banner;
			asset.cached_sprite = banner;
			VisualFramesByAssetId[definition.ActorAssetId] = frames;
			asset.get_override_sprite = actor => GetGreatSageOverrideSprite(definition, actor);
			asset.has_override_sprite = true;
			if (frames.Attack == null || frames.Attack.Length < definition.AttackFrameCount)
			{
				Debug.LogWarning("[玄鉴][妖属大圣] attack帧未完整载入 " + definition.SlotId
					+ " expected=" + definition.AttackFrameCount + " loaded=" + (frames.Attack?.Length ?? 0));
			}
		}
		catch (Exception ex)
		{
			asset.has_override_sprite = false;
			asset.get_override_sprite = null;
			VisualFramesByAssetId.Remove(definition.ActorAssetId);
			Debug.LogWarning("[玄鉴][妖属大圣] 贴图加载跳过 " + definition.SlotId + ": " + ex.GetType().Name);
		}
	}

	private static string[] BuildAnimationNames(string prefix)
	{
		string[] result = new string[SpriteFrameCount];
		for (int i = 0; i < result.Length; i++) result[i] = prefix + i;
		return result;
	}

	private static GreatSageVisualFrames BuildGreatSageVisualFrames(Definition definition)
	{
		GreatSageVisualFrames frames = new GreatSageVisualFrames
		{
			Idle = new Sprite[SpriteFrameCount], Walk = new Sprite[SpriteFrameCount],
			Breathing = Array.Empty<Sprite>(), Run = Array.Empty<Sprite>(), Attack = Array.Empty<Sprite>()
		};
		if (definition == null) return frames;
		for (int i = 0; i < SpriteFrameCount; i++)
		{
			frames.Idle[i] = TryLoadSprite(GetSpritePath(definition, "idle_" + i));
			frames.Walk[i] = TryLoadSprite(GetSpritePath(definition, "walk_" + i));
		}
		frames.Breathing = LoadPrefixedSequence(definition, "breathing", 48);
		frames.Run = LoadPrefixedSequence(definition, "run", 24);
		frames.Attack = LoadPrefixedSequence(definition, "attack", Math.Max(0, definition.AttackFrameCount));
		frames.Fallback = FirstNonNull(frames.Idle) ?? FirstNonNull(frames.Breathing) ?? FirstNonNull(frames.Walk)
			?? FirstNonNull(frames.Run) ?? FirstNonNull(frames.Attack);
		return frames;
	}

	private static Sprite[] LoadPrefixedSequence(Definition definition, string action, int expectedOrMaximum)
	{
		if (definition == null || string.IsNullOrWhiteSpace(action) || expectedOrMaximum <= 0) return Array.Empty<Sprite>();
		List<Sprite> result = new List<Sprite>(expectedOrMaximum);
		for (int i = 0; i < expectedOrMaximum; i++)
		{
			Sprite sprite = TryLoadSprite(GetSpritePath(definition,
				definition.VisualPrefix + "_" + action + "_" + i.ToString("D3", CultureInfo.InvariantCulture)));
			if (sprite == null)
			{
				if (!string.Equals(action, "attack", StringComparison.Ordinal)) break;
				continue;
			}
			result.Add(sprite);
		}
		return result.ToArray();
	}

	private static Sprite FirstNonNull(Sprite[] frames)
	{
		if (frames == null) return null;
		for (int i = 0; i < frames.Length; i++) if (frames[i] != null) return frames[i];
		return null;
	}

	private static void PrimeSpriteDirectory(Definition definition)
	{
		if (definition == null) return;
		try { SpriteTextureLoader.getSpriteList(GetMainSpritePath(definition).TrimEnd('/'), false); }
		catch { }
	}

	private static Sprite GetGreatSageOverrideSprite(Definition definition, Actor actor)
	{
		if (definition == null) return null;
		if (!VisualFramesByAssetId.TryGetValue(definition.ActorAssetId, out GreatSageVisualFrames frames) || frames == null)
			return actor?.asset?._cached_sprite ?? actor?.asset?.cached_sprite;

		long actorId = GetActorId(actor);
		if (actorId > 0L && AttackAnimationStartedAt.TryGetValue(actorId, out float startedAt))
		{
			int attackIndex = Mathf.FloorToInt(Mathf.Max(0f, Time.time - startedAt) / AttackAnimationFrameSeconds);
			if (attackIndex >= 0 && attackIndex < (frames.Attack?.Length ?? 0) && frames.Attack[attackIndex] != null)
				return frames.Attack[attackIndex];
		}

		bool moving = false;
		try { moving = actor != null && (actor.is_moving || actor.isUsingPath()); } catch { }
		Sprite[] sequence = moving && frames.Run?.Length > 0
			? frames.Run
			: (!moving && frames.Breathing?.Length > 0 ? frames.Breathing : (moving ? frames.Walk : frames.Idle));
		if (sequence == null || sequence.Length == 0) sequence = moving ? frames.Walk : frames.Idle;
		if (sequence != null && sequence.Length > 0)
		{
			int actorOffset = (int)(Math.Abs(actorId) % Math.Max(1, sequence.Length));
			float interval = moving ? 0.085f : 0.11f;
			int index = Math.Abs(Mathf.FloorToInt(Time.time / interval) + actorOffset) % sequence.Length;
			if (sequence[index] != null) return sequence[index];
		}
		return frames.Fallback ?? actor?.asset?._cached_sprite ?? actor?.asset?.cached_sprite;
	}

	internal static bool TryGetRenderSprite(Actor actor, out Sprite sprite)
	{
		sprite = null;
		if (!TryFindDefinition(actor, out Definition definition)) return false;
		sprite = GetGreatSageOverrideSprite(definition, actor);
		return sprite != null;
	}

	internal static bool IsGreatSageSubspecies(Subspecies subspecies)
	{
		try
		{
			string assetId = subspecies?.getActorAsset()?.id ?? string.Empty;
			return assetId.StartsWith("xj_yaoshu_dasheng_", StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	internal static Sprite TryGetBannerSprite(Subspecies subspecies)
	{
		try
		{
			ActorAsset asset = subspecies?.getActorAsset();
			if (asset == null) return null;
			if (VisualFramesByAssetId.TryGetValue(asset.id, out GreatSageVisualFrames frames)
				&& frames?.Fallback != null)
			{
				return frames.Fallback;
			}
			return asset._cached_sprite ?? asset.cached_sprite;
		}
		catch
		{
			return null;
		}
	}

	internal static void SyncRenderFrameData(Actor actor, Sprite sprite)
	{
		if (actor == null || sprite == null) return;
		try
		{
			actor.checkAnimationContainer();
			if (actor.animation_container?.dict_frame_data == null) return;
			if (!string.IsNullOrWhiteSpace(sprite.name)
				&& actor.animation_container.dict_frame_data.TryGetValue(sprite.name, out var matchingFrame))
			{
				actor.frame_data = matchingFrame;
				return;
			}
			foreach (var frame in actor.animation_container.dict_frame_data.Values)
			{
				if (frame == null) continue;
				actor.frame_data = frame;
				return;
			}
		}
		catch { }
	}

	private static string GetMainSpritePath(Definition definition) => SpritePathRoot + definition.ActorAssetId + "/main/";
	private static string GetSpritePath(Definition definition, string frameName) => SpritePathRoot + definition.ActorAssetId + "/main/" + frameName;

	private static Sprite TryLoadSprite(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		try
		{
			return SpriteTextureLoader.getSprite(path) ?? SpriteTextureLoader.getSprite("GameResources/" + path)
				?? Resources.Load<Sprite>(path) ?? Resources.Load<Sprite>("GameResources/" + path);
		}
		catch { return null; }
	}

	private static bool TryManifest(Definition definition, int currentYear, int ordinal, out Actor actor)
	{
		actor = null;
		MapBox world = World.world;
		if (definition == null || world?.units == null || AssetManager.actor_library?.get(definition.ActorAssetId) == null) return false;
		if (!TryFindSpawnTile(definition, currentYear, ordinal, out WorldTile tile)) return false;

		Actor spawned = null;
		try
		{
			// 与龙属完全一致：原生 spawn 事务中绝不提前注入自定义 Subspecies。
			// 这一步必须先让 WorldBox 建完 ActorData、tile/chunk、AI、species/subspecies 等原生关系。
			spawned = world.units.spawnNewUnit(definition.ActorAssetId, tile, false, false, 0f, null, false, true);
			if (spawned?.data == null) return false;
			if (!EnsureGreatSageNativeState(spawned, tile))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
				return false;
			}

			spawned.data.age_overgrowth = 18;
			XjActorStateWriteGateway.SetDisplayName(spawned, definition.DisplayName, customName: true);

			// 与龙属相同：名字写完后才统一亚种，亚种成功以后才进入玄鉴身份层。
			EnsureGreatSageSubspecies(spawned, definition);
			if (!IsUsableGreatSageSubspeciesForDefinition(spawned.subspecies, definition))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
				return false;
			}

			EnsureGreatSageIdentity(spawned, definition, currentYear);
			EnsureGreatSageAttackCallback(spawned);
			if (!ValidateCommittedGreatSage(spawned, definition))
			{
				RollbackGreatSageDomainState(spawned);
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
				return false;
			}

			XjActorRegistry.Register(spawned, out _);
			spawned.setStatsDirty();
			actor = spawned;
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][妖属大圣] 化生失败 " + definition.DisplayName + ": " + ex.GetType().Name);
			RollbackGreatSageDomainState(spawned);
			if (spawned?.data != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
			actor = null;
			return false;
		}
	}

	private static bool ValidateCommittedGreatSage(Actor actor, Definition definition)
	{
		if (!XjSafeCore.IsAliveActor(actor) || definition == null) return false;
		try
		{
			if (actor.current_tile?.chunk == null) return false;
			if (!IsUsableGreatSageSubspeciesForDefinition(actor.subspecies, definition)) return false;
			if (!actor.hasTrait(GreatSageTraitId)) return false;
			string assetId = actor.asset?.id ?? actor.data?.asset_id ?? string.Empty;
			return string.Equals(assetId, definition.ActorAssetId, StringComparison.Ordinal);
		}
		catch { return false; }
	}

	private static void RollbackGreatSageDomainState(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return;
		NextAttackCueAt.Remove(actorId);
		NextSkillAt.Remove(actorId);
		AttackAnimationStartedAt.Remove(actorId);
		RemoveRankActor(actorId);
		try { XjGuoWeiQuanBingRegistry.RemoveActor(actorId); } catch { }
	}

	/// <summary>
	/// 严格复制龙属 EnsureLongShuNativeState 的语义。
	/// 非文明单位允许 kingdom == null；只要求 ActorData 与 tile/chunk 完整。
	/// 只有 actor.isKingdomCiv() 为 true 时才检查原生 City/Kingdom 图关系。
	/// </summary>
	private static bool EnsureGreatSageNativeState(Actor actor, WorldTile requestedTile)
	{
		if (actor?.data == null || requestedTile?.chunk == null) return false;
		try
		{
			WorldTile current = actor.current_tile;
			if (current?.chunk == null)
			{
				actor.spawnOn(requestedTile);
				current = actor.current_tile;
			}
			if (current?.chunk == null) return false;

			bool isKingdomCiv = false;
			try { isKingdomCiv = actor.isKingdomCiv(); } catch { }
			if (isKingdomCiv && !XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor)) return false;
			if (!isKingdomCiv)
			{
				try { actor.setCity(null); } catch { return false; }
			}
			return true;
		}
		catch { return false; }
	}

	private static void EnsureGreatSageSubspecies(Actor actor, Definition definition)
	{
		if (actor?.data == null || definition == null) return;
		try
		{
			Subspecies current = actor.subspecies;
			if (!IsUsableGreatSageSubspeciesForDefinition(current, definition))
			{
				// 只为旧0.9.11交叉绑定存档做一次迁移。新生 Actor 若没有原生亚种，
				// 不在玄鉴层“凭空造亚种”，由 TryManifest 直接判失败并清理。
				current = TryRepairLegacyCrossAssetSubspecies(actor, definition, current);
				if (!IsUsableGreatSageSubspeciesForDefinition(current, definition)) return;
			}

			NormalizeGreatSageSubspecies(current);
			if (!TryGetBoundGreatSageSubspecies(definition, out Subspecies bound))
			{
				RuntimeSubspeciesByAssetId[definition.ActorAssetId] = current;
				bound = current;
			}
			else if (bound.id != current.id)
			{
				// 与龙属 AssignActorSubspecies 相同，只写 runtime 引用+data id，不调用 setSubspecies 触发集合重入。
				XjNativeReflectionInterop.TryWriteMemberValue(actor, "subspecies", bound);
			}
			SetGreatSageSubspeciesId(actor, bound.id);
			NormalizeGreatSageSubspecies(bound);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Subspecies.Assign." + definition.SlotId, ex);
		}
	}

	private static bool TryGetBoundGreatSageSubspecies(Definition definition, out Subspecies subspecies)
	{
		subspecies = null;
		if (definition == null || !RuntimeSubspeciesByAssetId.TryGetValue(definition.ActorAssetId, out Subspecies candidate)
			|| !IsUsableGreatSageSubspeciesForDefinition(candidate, definition))
		{
			if (definition != null) RuntimeSubspeciesByAssetId.Remove(definition.ActorAssetId);
			return false;
		}
		subspecies = candidate;
		return true;
	}

	private static bool IsUsableGreatSageSubspeciesForDefinition(Subspecies subspecies, Definition definition)
	{
		if (subspecies == null || definition == null || subspecies.isRekt()) return false;
		try { return string.Equals(subspecies.getActorAsset()?.id, definition.ActorAssetId, StringComparison.Ordinal); }
		catch { return false; }
	}

	private static Subspecies TryRepairLegacyCrossAssetSubspecies(Actor actor, Definition definition, Subspecies source)
	{
		if (actor?.data == null || definition == null || source?.data == null || World.world?.subspecies == null || World.world?.map_stats == null) return null;
		try
		{
			SubspeciesData copy = JsonConvert.DeserializeObject<SubspeciesData>(JsonConvert.SerializeObject(source.data, Formatting.None));
			if (copy == null) return null;
			copy.id = World.world.map_stats.getNextId("subspecies");
			copy.name = GreatSageSubspeciesName;
			copy.custom_name = true;
			copy.species_id = definition.ActorAssetId;
			copy.parent_subspecies = 0L;
			copy.created_time = World.world.map_stats.world_time;
			copy.total_deaths = 0L;
			copy.total_births = 0L;
			copy.total_kills = 0L;
			World.world.subspecies.loadObject(copy);
			Subspecies repaired = World.world.subspecies.get(copy.id);
			if (!IsUsableGreatSageSubspeciesForDefinition(repaired, definition)) return null;
			XjNativeReflectionInterop.TryWriteMemberValue(actor, "subspecies", repaired);
			actor.data.subspecies = repaired.id;
			RuntimeSubspeciesByAssetId[definition.ActorAssetId] = repaired;
			return repaired;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Subspecies.Migrate." + definition.SlotId, ex);
			return null;
		}
	}

	private static bool IsUsableGreatSageSubspecies(Subspecies subspecies) => subspecies != null && !subspecies.isRekt();

	private static void SetGreatSageSubspeciesId(Actor actor, long subspeciesId)
	{
		if (actor?.data == null || subspeciesId <= 0L) return;
		actor.data.subspecies = subspeciesId;
	}

	private static void NormalizeGreatSageSubspecies(Subspecies subspecies)
	{
		if (!IsUsableGreatSageSubspecies(subspecies)) return;
		bool changed = false;
		for (int i = 0; i < GreatSageSubspeciesTraitIds.Length; i++)
		{
			string traitId = GreatSageSubspeciesTraitIds[i];
			if (!subspecies.hasTrait(traitId) && AssetManager.subspecies_traits?.get(traitId) != null)
			{
				subspecies.addTrait(traitId, true);
				changed = true;
			}
		}
		for (int i = 0; i < GreatSageForbiddenSubspeciesTraitIds.Length; i++)
		{
			string traitId = GreatSageForbiddenSubspeciesTraitIds[i];
			if (subspecies.hasTrait(traitId))
			{
				subspecies.removeTrait(traitId);
				changed = true;
			}
		}
		if (subspecies.data != null && !string.Equals(subspecies.data.name, GreatSageSubspeciesName, StringComparison.Ordinal))
		{
			subspecies.data.name = GreatSageSubspeciesName;
			subspecies.data.custom_name = true;
			changed = true;
		}
		if (changed) subspecies.forceRecalcBaseStats();
	}

	private static void ApplyGreatSageDefaultSubspeciesTraits(ActorAsset asset)
	{
		if (asset == null) return;
		asset.default_subspecies_traits = new List<string>();
		for (int i = 0; i < GreatSageSubspeciesTraitIds.Length; i++)
		{
			string traitId = GreatSageSubspeciesTraitIds[i];
			if (AssetManager.subspecies_traits?.get(traitId) != null) asset.addSubspeciesTrait(traitId);
		}
	}

	private static bool TryFindSpawnTile(Definition definition, int currentYear, int ordinal, out WorldTile tile)
	{
		tile = null;
		MapBox world = World.world;
		if (world == null || MapBox.width <= 0 || MapBox.height <= 0) return false;
		long seed = XjDeterministicHash.PositiveHash(currentYear + ordinal * 977L, "yaoshu.greatsage.spawn." + definition.SlotId);
		for (int i = 0; i < SpawnProbeBudget; i++)
		{
			int x = XjDeterministicHash.PositiveIndex(seed + i * 37L, definition.SlotId + ".x", MapBox.width);
			int y = XjDeterministicHash.PositiveIndex(seed + i * 53L, definition.SlotId + ".y", MapBox.height);
			WorldTile candidate = world.GetTileSimple(x, y);
			if (IsSuitableTile(candidate, definition.PreferWater))
			{
				tile = candidate;
				return true;
			}
		}
		if (definition.PreferWater)
		{
			for (int i = 0; i < SpawnProbeBudget / 2; i++)
			{
				int x = XjDeterministicHash.PositiveIndex(seed + 4001L + i * 71L, definition.SlotId + ".fallback.x", MapBox.width);
				int y = XjDeterministicHash.PositiveIndex(seed + 5003L + i * 89L, definition.SlotId + ".fallback.y", MapBox.height);
				WorldTile candidate = world.GetTileSimple(x, y);
				if (IsSuitableTile(candidate, false))
				{
					tile = candidate;
					return true;
				}
			}
		}
		return false;
	}

	private static bool IsSuitableTile(WorldTile tile, bool preferWater)
	{
		if (tile?.Type == null || tile.Type.lava || tile.hasBuilding() || XjZhantanlinSystem.IsInsideProtectedBoundary(tile)) return false;
		bool water = tile.Type.ocean || tile.Type.liquid;
		if (!water)
		{
			string id = tile.Type.id ?? string.Empty;
			water = id.IndexOf("ocean", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("water", StringComparison.OrdinalIgnoreCase) >= 0
				|| id.IndexOf("sea", StringComparison.OrdinalIgnoreCase) >= 0;
		}
		return preferWater ? water : !water;
	}

	private static void ApplyStrategicPulse(Actor actor, Definition definition)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return;
		int currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		EnsureGreatSageIdentity(actor, definition, currentYear);
		XjYaoShuSapientSpecies.TryMaintainForGreatSage(definition.SlotId, actor.current_tile, currentYear);
		try
		{
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, Math.Max(1f, definition.Health));
			XjSafeCore.HealActorSafe(actor, Math.Max(1f, maxHealth * ResolveStrategicHealFraction(definition.SlotId)));
			actor.setStatsDirty();
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.StrategicPulse." + definition.SlotId, ex); }
	}

	private static void EnsureGreatSageAttackCallback(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			AttackAction current = actor.s_action_attack_target;
			if (ContainsAttackCallback(current, GreatSageAttackAction)) return;
			actor.s_action_attack_target = (AttackAction)Delegate.Combine(current, GreatSageAttackAction);
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.BindAttack", ex); }
	}

	private static bool ContainsAttackCallback(AttackAction actions, AttackAction expected)
	{
		if (actions == null || expected == null) return false;
		Delegate[] callbacks = actions.GetInvocationList();
		for (int i = 0; i < callbacks.Length; i++)
		{
			if (callbacks[i] is AttackAction callback && callback.Method == expected.Method && ReferenceEquals(callback.Target, expected.Target)) return true;
		}
		return false;
	}

	private static bool OnGreatSageAttackTarget(BaseSimObject pSelf, BaseSimObject pTarget, WorldTile pTile = null)
	{
		try
		{
			if (pSelf == null || !pSelf.isActor()) return true;
			Actor caster = pSelf.a;
			if (!XjSafeCore.IsAliveActor(caster) || !TryFindDefinition(caster, out Definition definition)) return true;
			WorldTile impactTile = pTile;
			try { impactTile = pTarget?.current_tile ?? pTile; } catch { }
			if (impactTile == null)
			{
				try { impactTile = pSelf.current_tile; } catch { }
			}
			if (impactTile == null) return true;
			long actorId = GetActorId(caster);
			if (actorId <= 0L) return true;
			float now = Mathf.Max(0f, Time.time);
			if (IsReady(NextAttackCueAt, actorId, now))
			{
				SetNextReadyAt(NextAttackCueAt, actorId, now + AttackCueCooldownSeconds);
				BeginAttackAnimation(actorId, now);
				TryPlaySkillVisual(impactTile, definition, definition.SkillEffectScale * 0.52f, 0.075f);
			}
			if (!IsReady(NextSkillAt, actorId, now)) return true;
			SetNextReadyAt(NextSkillAt, actorId, now + SkillCooldownSeconds);
			TryPlaySkillVisual(impactTile, definition, definition.SkillEffectScale, 0.055f);
			Actor target = pTarget != null && pTarget.isActor() ? pTarget.a : null;
			if (XjSafeCore.IsAliveActor(target))
			{
				float baseDamage = XjJinDanCombatApi.GetBaseDamage(caster);
				float bonusDamage = Mathf.Max(25000f, baseDamage * Mathf.Max(0.1f, definition.SkillDamageMultiplier) * 0.80f);
				XjJinDanCombatApi.TryDamageActor(caster, target, bonusDamage, "YaoShuGreatSage." + definition.SlotId, out _);
			}
			ApplyBoundedTerrainImpact(caster, impactTile, definition, now);
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.Attack", ex); }
		return true;
	}

	private static bool IsReady(Dictionary<long, float> cooldowns, long actorId, float now)
		=> !cooldowns.TryGetValue(actorId, out float nextReadyAt) || now >= nextReadyAt;

	private static void SetNextReadyAt(Dictionary<long, float> cooldowns, long actorId, float nextReadyAt)
	{
		if (cooldowns.Count >= Definitions.Length && !cooldowns.ContainsKey(actorId)) return;
		cooldowns[actorId] = nextReadyAt;
	}

	private static void BeginAttackAnimation(long actorId, float now)
	{
		if (actorId <= 0L) return;
		if (AttackAnimationStartedAt.Count >= Definitions.Length && !AttackAnimationStartedAt.ContainsKey(actorId)) return;
		AttackAnimationStartedAt[actorId] = now;
	}

	private static bool TryFindDefinition(Actor actor, out Definition definition)
	{
		definition = null;
		string assetId = actor?.asset?.id ?? actor?.data?.asset_id;
		if (string.IsNullOrWhiteSpace(assetId)) return false;
		for (int i = 0; i < Definitions.Length; i++)
		{
			if (!string.Equals(Definitions[i].ActorAssetId, assetId, StringComparison.Ordinal)) continue;
			definition = Definitions[i];
			return true;
		}
		return false;
	}

	private static void TryPlaySkillVisual(WorldTile tile, Definition definition, float scale, float frameIntervalSeconds)
	{
		if (tile == null || definition == null || string.IsNullOrWhiteSpace(definition.EffectId)) return;
		try
		{
			Vector2Int pos = tile.pos;
			XjCombatEffectPool.TrySpawnSpriteAnimationSafe(GreatSageEffectRoot + definition.EffectId,
				pos.x + 0.5f, pos.y + 0.5f, Mathf.Max(0.04f, scale), frameIntervalSeconds);
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.Visual." + definition.SlotId, ex); }
	}

	private static void ApplyBoundedTerrainImpact(Actor caster, WorldTile center, Definition definition, float now)
	{
		if (caster?.data == null || center == null || definition == null || World.world == null) return;
		int radius = Mathf.Clamp(definition.TerrainRadius, MinTerrainRadius, MaxTerrainRadius);
		TerraformOptions options = CreateTerrainOptions(definition.TerrainStyle);
		if (options == null) return;
		Vector2Int origin = center.pos;
		long seed = XjDeterministicHash.PositiveHash(GetActorId(caster) + Mathf.FloorToInt(now) * 31L,
			"yaoshu.greatsage.skill." + definition.SlotId);
		for (int i = 0; i < TerrainSampleBudget; i++)
		{
			int x = origin.x;
			int y = origin.y;
			if (i > 0)
			{
				float angle = XjDeterministicHash.PositiveIndex(seed + i * 17L, definition.SlotId + ".angle", 360) * Mathf.Deg2Rad;
				int distance = 1 + XjDeterministicHash.PositiveIndex(seed + i * 29L, definition.SlotId + ".radius", radius);
				x += Mathf.RoundToInt(Mathf.Cos(angle) * distance);
				y += Mathf.RoundToInt(Mathf.Sin(angle) * distance);
			}
			if (x < 0 || y < 0 || x >= MapBox.width || y >= MapBox.height) continue;
			WorldTile tile = World.world.GetTileSimple(x, y);
			if (tile?.Type == null || tile.Type.lava || tile.Type.ocean || tile.Type.liquid) continue;
			try
			{
				if (string.Equals(definition.TerrainStyle, "frost", StringComparison.Ordinal) && tile.canBeFrozen()) tile.freeze(20);
				XjAttributedTerrainInterop.TryDecreaseTile(tile, options, caster);
			}
			catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.Terrain." + definition.SlotId, ex); }
		}
	}

	private static TerraformOptions CreateTerrainOptions(string terrainStyle)
	{
		TerraformOptions options = new TerraformOptions { damage = 0 };
		switch (terrainStyle)
		{
			case "fire":
				options.damage_buildings = true; options.destroy_buildings = true; options.make_ruins = true;
				options.remove_roads = true; options.remove_trees_fully = true; options.set_fire = true;
				options.add_burned = true; options.add_heat = 95; break;
			case "thunder":
				options.damage_buildings = true; options.destroy_buildings = true; options.make_ruins = true;
				options.remove_roads = true; options.remove_trees_fully = true; options.lightning_effect = true;
				options.shake = true; options.shake_duration = 0.18f; options.shake_intensity = 0.006f; break;
			case "smash":
				options.damage_buildings = true; options.destroy_buildings = true; options.make_ruins = true;
				options.remove_roads = true; options.remove_trees_fully = true; break;
			case "frost":
				options.damage_buildings = true; options.remove_roads = true; options.remove_trees_fully = true; break;
			case "tide":
				options.damage_buildings = true; options.destroy_buildings = true; options.make_ruins = true;
				options.remove_roads = true; options.remove_trees_fully = true; break;
			default: return null;
		}
		return options;
	}

	private static float ResolveStrategicHealFraction(string slotId)
	{
		return slotId switch
		{
			"kanshui" => 0.45f, "jinghai" => 0.45f, "yuanzhao" => 0.38f, "bailin" => 0.35f,
			"zheqi" => 0.35f, "baixiang" => 0.30f, "xuanlei" => 0.28f, "chunhuo" => 0.25f,
			"taiyin" => 0.22f, "ziqi" => 0.22f, "tingsongli" => 0.20f, "jinchifu" => 0.20f, _ => 0.20f
		};
	}

	internal static bool IsGreatSage(Actor actor) => actor?.data != null && TryFindDefinition(actor, out _);

	internal static bool IsGreatSageRaceKey(string raceKey)
	{
		string value = raceKey ?? string.Empty;
		for (int i = 0; i < Definitions.Length; i++)
		{
			if (value.IndexOf(Definitions[i].ActorAssetId, StringComparison.Ordinal) >= 0) return true;
		}
		return false;
	}

	internal static void ObserveConfirmedDeath(in XjDeathSnapshot snapshot, XjDeathCause cause)
	{
		if (!snapshot.Found || !IsGreatSageRaceKey(snapshot.RaceKey)) return;
		if (cause != XjDeathCause.Combat || snapshot.LastAttackerId <= 0L) return;
		string sageName = string.IsNullOrWhiteSpace(snapshot.Name) ? "妖属大圣" : snapshot.Name;
		string killerName = string.IsNullOrWhiteSpace(snapshot.LastAttackerName) ? "无名修士" : snapshot.LastAttackerName;
		string body = killerName + "斩落" + sageName + "，其" + (string.IsNullOrWhiteSpace(snapshot.DaoTu) ? "果位" : snapshot.DaoTu + "果位")
			+ "映照暂寂；此战记入天地功绩。";
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, sageName + "伏诛", body,
			importance: 4, isProtected: true, actorId: snapshot.ActorId, actorName: sageName,
			year: snapshot.Year, eventType: "YaoShuGreatSageSlain", mirrorToWorldLog: true);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical("【大圣伏诛】" + killerName + "斩落" + sageName + "。",
			XjAnnouncementCategory.HighRealm, duration: 8f, color: "#D97272");
	}

	internal static bool TryResolveGreatSageFruit(Actor actor, out string fruit)
	{
		fruit = string.Empty;
		if (!TryFindDefinition(actor, out Definition definition)) return false;
		fruit = definition.FruitId;
		return !string.IsNullOrWhiteSpace(fruit);
	}

	internal static IReadOnlyList<CodexItem> BuildCodexItems()
	{
		EnsureRankMembership();
		List<CodexItem> items = new List<CodexItem>(Definitions.Length);
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			SlotState state = States[i];
			items.Add(new CodexItem
			{
				Name = definition.DisplayName,
				DaoTu = definition.DaoTu,
				Skill = definition.SkillName,
				YaoMin = definition.YaoMinName,
				Fruit = definition.FruitId,
				YaoMinPopulation = XjYaoShuSapientSpecies.GetPopulationCountForSlot(definition.SlotId),
				Alive = TryResolveLivingSage(definition, state.ActorId, out _),
				ManifestationCount = state.ManifestationCount,
				LastManifestationYear = state.LastManifestationYear,
				LastDepartureYear = state.LastDepartureYear,
				NextAttemptYear = state.NextAttemptYear
			});
		}
		return items;
	}

	private static void EnsureGreatSageIdentity(Actor actor, Definition definition, int currentYear)
	{
		if (actor?.data == null || definition == null) return;
		currentYear = Math.Max(0, currentYear);
		try
		{
			if (!EnsureGreatSageNativeState(actor, actor.current_tile)) return;
			EnsureGreatSageSubspecies(actor, definition);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 6);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
			XjCultivationPathTransitions.TrySetInitialPath(actor, XjCultivationPathIds.FuQiYangXing, definition.DaoTu, string.Empty, false);
			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, definition.DaoTu, syncVisibleTraits: false);
			if (!XjDaoTaiSpellScale.IsDaoTaiActor(actor))
			{
				XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.ZhenJunYuShi, syncVisibleTraits: false, clearManualRemoved: false);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
			}
			XjExternalUnitTransferContext.EnterTraitTransfer();
			try
			{
				EnsureTrait(actor, GreatSageTraitId);
				EnsureTrait(actor, "XjZz6");
				EnsureTrait(actor, "infertile");
				EnsureTrait(actor, XjRealmIds.ZhenJunYuShi);
				if (XjDaoTuVisibleTraitCatalog.TryResolveTraitId(definition.DaoTu, out string daoTuTraitId)) EnsureTrait(actor, daoTuTraitId);
				EnsureNativeTraits(actor, definition);
			}
			finally { XjExternalUnitTransferContext.ExitTraitTransfer(); }
			actor.clearTraitCache();
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			EnsureGreatSageAuthority(actor, definition, currentYear);
			XjActorRegistry.Register(actor, out _);
			XjCultivatorCache.CheckAndUpdate(actor);
			TrackRankActor(GetActorId(actor));
			actor.setStatsDirty();
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.Identity." + definition.SlotId, ex); }
	}

	private static void EnsureGreatSageAuthority(Actor actor, Definition definition, int currentYear)
	{
		if (actor?.data == null || definition == null) return;
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return;
		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(definition.DaoTu);
		int count = Math.Min(3, catalog.Count);
		string[] authorities = new string[count];
		for (int i = 0; i < count; i++) authorities[i] = catalog[i];
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, actorId, actor.getName(), definition.DaoTu, definition.FruitId, string.Join(",", authorities),
			string.Empty, string.Empty, string.Empty, "妖属映照", string.Empty, 0, false, 0,
			"妖属大圣应果位而化，只作映照，不占修士正位；所执本柄随果位同显", "Active", Math.Max(1, currentYear)));
	}

	private static void EnsureNativeTraits(Actor actor, Definition definition)
	{
		if (actor?.data == null || definition?.NativeTraits == null) return;
		for (int i = 0; i < definition.NativeTraits.Length; i++) EnsureTrait(actor, definition.NativeTraits[i]);
	}

	private static void EnsureTrait(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId) || AssetManager.traits?.get(traitId) == null) return;
		try { if (!actor.hasTrait(traitId)) actor.addTrait(traitId, false); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.Trait." + traitId, ex); }
	}

	private static bool TryResolveLivingSage(Definition definition, long actorId, out Actor actor)
	{
		actor = null;
		if (actorId <= 0L || definition == null) return false;
		if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out actor) || !XjSafeCore.IsAliveActor(actor))
		{
			actor = null;
			return false;
		}
		string assetId = (actor.asset?.id ?? actor.data?.asset_id ?? string.Empty).Trim();
		if (!string.Equals(assetId, definition.ActorAssetId, StringComparison.Ordinal))
		{
			actor = null;
			return false;
		}
		return true;
	}

	private static int ScheduleNextAttempt(Definition definition, int currentYear)
	{
		int year = Math.Max(0, currentYear);
		if (year < ManifestationStartYear) return ManifestationStartYear;
		int remainder = (year - ManifestationStartYear) % ManifestationCheckIntervalYears;
		return year + (remainder == 0 ? ManifestationCheckIntervalYears : ManifestationCheckIntervalYears - remainder);
	}

	private static int ScheduleReentryAttempt(int departureYear)
	{
		int earliestYear = Math.Max(ManifestationStartYear, departureYear + ReentryCooldownYears);
		int remainder = (earliestYear - ManifestationStartYear) % ManifestationCheckIntervalYears;
		return remainder == 0 ? earliestYear : earliestYear + ManifestationCheckIntervalYears - remainder;
	}

	private static bool CanAttemptManifestation(Definition definition)
		=> definition == null || !string.Equals(definition.SlotId, "yuanzhao", StringComparison.Ordinal) || XjYuanZhaoKongZhengEvent.IsTriggered;

	private static bool HasManifestationCondition()
	{
		// 修士缓存只保存当前高境者；逐项排除大圣，保证大圣本身不会反过来满足前置。
		IReadOnlyList<long> highRealmIds = XjCultivatorCache.GetZhenJunOrHigherIds();
		int independentCount = 0;
		for (int i = 0; i < highRealmIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(highRealmIds[i], out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor)
				|| IsGreatSage(actor)) continue;
			independentCount++;
			if (independentCount >= RequiredIndependentZhenJunCount) return true;
		}

		// getSimpleList 是 WorldBox 的存活单位索引；这里只读取 Count，不枚举人口。
		IReadOnlyList<Actor> livingUnits = World.world?.units?.getSimpleList();
		return livingUnits != null && livingUnits.Count >= PopulationManifestationThreshold;
	}

	private static HashSet<int> SelectManifestationBatch(int currentYear)
	{
		List<int> candidates = new List<int>(Definitions.Length);
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			SlotState state = States[i];
			if (state.ActorId > 0L
				|| currentYear < state.NextAttemptYear
				|| !CanAttemptManifestation(definition)
				|| IsFruitHeldByLivingRealActor(definition.FruitId)) continue;
			candidates.Add(i);
		}

		candidates.Sort((left, right) =>
		{
			long leftSeed = XjDeterministicHash.PositiveHash(currentYear, "yaoshu.batch." + Definitions[left].SlotId);
			long rightSeed = XjDeterministicHash.PositiveHash(currentYear, "yaoshu.batch." + Definitions[right].SlotId);
			int compared = leftSeed.CompareTo(rightSeed);
			return compared != 0 ? compared : left.CompareTo(right);
		});
		int batchSize = MinimumManifestationBatchSize
			+ (int)(XjDeterministicHash.PositiveHash(currentYear, "yaoshu.batch.size")
				% (MaximumManifestationBatchSize - MinimumManifestationBatchSize + 1));
		HashSet<int> selected = new HashSet<int>();
		for (int i = 0; i < candidates.Count && i < batchSize; i++) selected.Add(candidates[i]);
		return selected;
	}

	private static bool HasDueManifestationSlot(int currentYear)
	{
		for (int i = 0; i < Definitions.Length; i++)
		{
			SlotState state = States[i];
			if (state.ActorId <= 0L
				&& currentYear >= state.NextAttemptYear
				&& CanAttemptManifestation(Definitions[i])) return true;
		}
		return false;
	}

	private static bool IsFruitHeldByLivingRealActor(string fruitId)
	{
		string target = NormalizeFruit(fruitId);
		if (target.Length == 0) return false;
		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found
				|| !entry.IsActive
				|| entry.ActorId <= 0L
				|| !string.Equals(NormalizeFruit(entry.GuoWei), target, StringComparison.Ordinal)) continue;
			if (XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out Actor holder)
				&& XjSafeCore.IsAliveActor(holder)) return true;
		}
		return false;
	}

	private static void RecordDeparture(Definition definition, long actorId, int currentYear)
	{
		if (definition == null || actorId <= 0L) return;
		RemoveRankActor(actorId);
		XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
		string body = definition.DisplayName + "身殒，" + definition.FruitId + "映照沉寂。天数沉寂百五十年，方许后继再度化生。";
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, definition.DisplayName + "身灭", body,
			importance: 3, isProtected: true, actorId: actorId, actorName: definition.DisplayName,
			year: currentYear, eventType: "YaoShuGreatSageDeparture", mirrorToWorldLog: true);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical("【大圣身殒】" + definition.DisplayName + "归寂，" + definition.DaoTu + "映照暂寂。",
			XjAnnouncementCategory.HighRealm, duration: 8f, color: "#8DA2B8");
	}

	private static void RecordManifestation(Actor actor, Definition definition, int currentYear)
	{
		long actorId = GetActorId(actor);
		string body = definition.DisplayName + "应" + definition.FruitId + "而现，神通【" + definition.SkillName + "】初鸣；只映其位，不夺修士正席。";
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.HighRealm, definition.DisplayName + "化生", body,
			importance: 4, isProtected: true, actorId: actorId, actorName: definition.DisplayName,
			year: currentYear, eventType: "YaoShuGreatSageManifestation", mirrorToWorldLog: true);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical("【大圣降临】" + definition.DisplayName + "应位而现，" + definition.SkillName + "初鸣。",
			XjAnnouncementCategory.HighRealm, duration: 8f, color: "#E1C46A");
	}

	private static long GetActorId(Actor actor) => actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;

	private static string NormalizeFruit(string guoWei)
		=> XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei ?? string.Empty).Trim();

	private static void MarkChanged() => XjWorldArchiveSystem.MarkModuleChanged(ModuleId);
}
