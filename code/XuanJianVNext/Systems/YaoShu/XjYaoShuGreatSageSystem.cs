using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 妖属大圣的轻量“果位化生”运行态。
///
/// 设计边界：
/// - 不创建新文明种族，不接入玄鉴普通修炼、家族、宗门、百艺流水线；
/// - 世界三百年后，十二个固定槽位仅在各自正位空缺时，每五十年作一次五成化生判定；
/// - 活着时绝不重复生成；年度工作量固定O(12)，不扫描全世界Actor；
/// - 生物本体使用WorldBox原生ActorAsset克隆与spawnNewUnit创建，保留原生AI/空间容器生命周期。
/// </summary>
internal static class XjYaoShuGreatSageSystem
{
	internal const string ModuleId = "world.yaoshu-great-sage";
	internal const string GreatSageTraitId = "XjYaoShuGreatSage";
	private const int ManifestationStartYear = 300;
	private const int ManifestationCheckIntervalYears = 50;
	private const float ManifestationChance = 0.5f;
	private const int StrategicPulseYears = 10;
	private const int SpawnProbeBudget = 96;
	private const int SpriteFrameCount = 6;
	private const string SpritePathRoot = "actors/species/other/";
	private const float GreatSageAttributeMultiplier = 1.5f;
	// 大圣的“基础数值 × 1.5”以真君级底盘为准，而不是以普通原生动物的几万血量为准。
	// 否则一名叠到百万面板的金丹便会把十二圣逐个当作普通中立单位清掉。
	private const float GreatSageHealthBaseline = 40000000f;
	private const float GreatSageDamageBaseline = 1500000f;
	private const float GreatSageArmorBaseline = 2500f;
	// 大圣维持龙属体型档位；按当前基准放大一倍。
	private const float GreatSageScale = 0.16f;
	private const int ReentryCooldownYears = 150;
	// 大圣的普攻仍完全走 WorldBox 原生伤害链；这里只在命中回调上附着表现与低频神通。
	// 两张表最多各存十二个活着的大圣，绝不参与普通 Actor 的逐帧或全图轮询。
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
		"gift_of_void"
	};
	// 大圣与龙属同样不走进食、睡眠与繁殖链；耐热是大圣既定先天，不能照搬龙属的移除项。
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
	private static long _sharedGreatSageSubspeciesId = -1L;
	private static Subspecies _sharedGreatSageSubspecies;

	private sealed class Definition
	{
		internal string SlotId;
		internal string ActorAssetId;
		internal string TemplateId;
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

	// Unity 贴图 API 不能在 Actor 渲染回调内调用。所有帧在资产注册时由主线程缓存，
	// 回调只读数组，避免 calculateMainSprite 与资源加载并发造成空引用。
	private sealed class GreatSageVisualFrames
	{
		internal Sprite[] Idle;
		internal Sprite[] Walk;
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
			YaoMinName = "原生鸡妖民",
			Health = 28000f, Damage = 1180f, Armor = 62f, Speed = 92f, AttackSpeed = 86f, Mass = 55f,
			NativeTraits = new[] { "heat_resistance", "sunblessed", "enhanced_strength" },
			VisualPrefix = "neutral_zurael", AttackFrameCount = 29,
			EffectId = "YangYanLianJi", TerrainStyle = "fire", SkillEffectScale = 0.19f, SkillDamageMultiplier = 0.85f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "jinghai", ActorAssetId = "xj_yaoshu_dasheng_jinghai", TemplateId = "dragon",
			DisplayName = "靖海大圣", DaoTu = "合水", SkillName = "镇潮靖海", PreferWater = true,
			YaoMinName = "原生龙妖民",
			Health = 42000f, Damage = 940f, Armor = 78f, Speed = 68f, AttackSpeed = 62f, Mass = 110f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "regeneration", "boosted_vitality", "peaceful" },
			VisualPrefix = "f6_whitewyvern", AttackFrameCount = 21,
			EffectId = "YingYueLianHua", TerrainStyle = "tide", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.72f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "bailin", ActorAssetId = "xj_yaoshu_dasheng_bailin", TemplateId = "civ_rhino",
			DisplayName = "明阳白麟大圣", DaoTu = "明阳", SkillName = "明阳照骨", PreferWater = false,
			YaoMinName = "原生犀牛妖民",
			Health = 36000f, Damage = 1040f, Armor = 74f, Speed = 76f, AttackSpeed = 70f, Mass = 95f,
			NativeTraits = new[] { "sunblessed", "regeneration", "immune", "peaceful" },
			VisualPrefix = "f6_elklodon", AttackFrameCount = 13,
			EffectId = "DaRiZhuiYuan", TerrainStyle = "fire", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.82f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "baixiang", ActorAssetId = "xj_yaoshu_dasheng_baixiang", TemplateId = "civ_buffalo",
			DisplayName = "煞炁白象大圣", DaoTu = "煞炁", SkillName = "煞岳镇身", PreferWater = false,
			YaoMinName = "原生水牛妖民",
			Health = 50000f, Damage = 1320f, Armor = 82f, Speed = 58f, AttackSpeed = 55f, Mass = 145f,
			NativeTraits = new[] { "enhanced_strength", "tough", "boosted_vitality" },
			VisualPrefix = "neutral_thunderhorn", AttackFrameCount = 19,
			EffectId = "ZhenYueBeng", TerrainStyle = "smash", SkillEffectScale = 0.27f, SkillDamageMultiplier = 0.95f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "tingsongli", ActorAssetId = "xj_yaoshu_dasheng_tingsongli", TemplateId = "civ_cat",
			DisplayName = "寒炁雪岭听松狸大圣", DaoTu = "寒炁", SkillName = "听雪藏形", PreferWater = false,
			YaoMinName = "原生猫妖民",
			Health = 24000f, Damage = 860f, Armor = 55f, Speed = 128f, AttackSpeed = 105f, Mass = 34f,
			NativeTraits = new[] { "cold_resistance", "dodge", "eagle_eyed", "peaceful" },
			VisualPrefix = "neutral_ghostlynx", AttackFrameCount = 26,
			EffectId = "TaiYinBingFeng", TerrainStyle = "frost", SkillEffectScale = 0.18f, SkillDamageMultiplier = 0.70f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "jinchifu", ActorAssetId = "xj_yaoshu_dasheng_jinchifu", TemplateId = "civ_bee",
			DisplayName = "瑞炁玄匮金翅蝠大圣", DaoTu = "瑞炁", SkillName = "玄匮振金", PreferWater = false,
			YaoMinName = "原生蜂妖民",
			Health = 26000f, Damage = 960f, Armor = 58f, Speed = 145f, AttackSpeed = 112f, Mass = 28f,
			NativeTraits = new[] { "hovering", "dodge", "eagle_eyed", "peaceful" },
			VisualPrefix = "neutral_silverbeak", AttackFrameCount = 19,
			EffectId = "JinDanLeiFa", TerrainStyle = "thunder", SkillEffectScale = 0.17f, SkillDamageMultiplier = 0.74f, TerrainRadius = 5
		},
		new Definition
		{
			SlotId = "xuanlei", ActorAssetId = "xj_yaoshu_dasheng_xuanlei", TemplateId = "civ_crocodile",
			DisplayName = "玄雷金鳞大圣", DaoTu = "玄雷", SkillName = "玄雷裂天", PreferWater = false,
			YaoMinName = "原生鳄妖民",
			Health = 34000f, Damage = 1410f, Armor = 68f, Speed = 98f, AttackSpeed = 94f, Mass = 78f,
			NativeTraits = new[] { "enhanced_strength", "eagle_eyed", "tough" },
			VisualPrefix = "boss_paragon", AttackFrameCount = 25,
			EffectId = "JinDanLeiFa", TerrainStyle = "thunder", SkillEffectScale = 0.22f, SkillDamageMultiplier = 1.00f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "yuanzhao", ActorAssetId = "xj_yaoshu_dasheng_yuanzhao", TemplateId = "civ_crocodile",
			DisplayName = "渊照寒渊螭大圣", DaoTu = "渊照", SkillName = "渊镜寒照", PreferWater = true,
			YaoMinName = "原生鳄妖民",
			Health = 40000f, Damage = 1120f, Armor = 75f, Speed = 86f, AttackSpeed = 78f, Mass = 102f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "regeneration", "peaceful" },
			VisualPrefix = "f6_frostdrake", AttackFrameCount = 28,
			EffectId = "YingYueLianHua", TerrainStyle = "frost", SkillEffectScale = 0.20f, SkillDamageMultiplier = 0.86f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "zheqi", ActorAssetId = "xj_yaoshu_dasheng_zheqi", TemplateId = "civ_crocodile",
			DisplayName = "谪炁九首蛟大圣", DaoTu = "谪炁", SkillName = "九首谪潮", PreferWater = false,
			YaoMinName = "原生鳄妖民",
			Health = 45500f, Damage = 1260f, Armor = 78f, Speed = 74f, AttackSpeed = 76f, Mass = 122f,
			NativeTraits = new[] { "enhanced_strength", "tough", "regeneration" },
			VisualPrefix = "neutral_hydrax", AttackFrameCount = 24,
			EffectId = "YingYueLianHua", TerrainStyle = "tide", SkillEffectScale = 0.21f, SkillDamageMultiplier = 0.93f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "kanshui", ActorAssetId = "xj_yaoshu_dasheng_kanshui", TemplateId = "civ_buffalo",
			DisplayName = "坎水负岳冰罴大圣", DaoTu = "坎水", SkillName = "负岳沉渊", PreferWater = true,
			YaoMinName = "原生水牛妖民",
			Health = 52500f, Damage = 1110f, Armor = 92f, Speed = 54f, AttackSpeed = 54f, Mass = 158f,
			NativeTraits = new[] { "aquatic", "cold_resistance", "tough", "boosted_vitality", "peaceful" },
			VisualPrefix = "f6_waterbear", AttackFrameCount = 33,
			EffectId = "ZhenYueBeng", TerrainStyle = "smash", SkillEffectScale = 0.29f, SkillDamageMultiplier = 0.92f, TerrainRadius = 7
		},
		new Definition
		{
			SlotId = "taiyin", ActorAssetId = "xj_yaoshu_dasheng_taiyin", TemplateId = "civ_cat",
			DisplayName = "太阴啸月霜狼大圣", DaoTu = "太阴", SkillName = "啸月追魂", PreferWater = false,
			YaoMinName = "原生猫妖民",
			Health = 27500f, Damage = 1010f, Armor = 61f, Speed = 138f, AttackSpeed = 110f, Mass = 42f,
			NativeTraits = new[] { "cold_resistance", "dodge", "eagle_eyed" },
			VisualPrefix = "f6_spiritwolf", AttackFrameCount = 29,
			EffectId = "TaiYinBingFeng", TerrainStyle = "frost", SkillEffectScale = 0.21f, SkillDamageMultiplier = 0.78f, TerrainRadius = 6
		},
		new Definition
		{
			SlotId = "ziqi", ActorAssetId = "xj_yaoshu_dasheng_ziqi", TemplateId = "civ_bee",
			DisplayName = "紫炁云翼大圣", DaoTu = "紫炁", SkillName = "紫云遁空", PreferWater = false,
			YaoMinName = "原生蜂妖民",
			Health = 29500f, Damage = 1030f, Armor = 65f, Speed = 142f, AttackSpeed = 114f, Mass = 32f,
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
	private static readonly AttackAction GreatSageAttackAction = OnGreatSageAttackTarget;
	private static bool _assetsInitialized;
	private static bool _reconciledAfterLoad;

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

		HashSet<string> occupiedFruitIds = BuildOccupiedFruitSet();
		bool changed = false;
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

			if (currentYear < ManifestationStartYear || currentYear < state.NextAttemptYear
				|| !CanAttemptManifestation(definition)) continue;

			string fruitId = definition.FruitId;
			if (occupiedFruitIds.Contains(NormalizeFruit(fruitId))
				|| XjFruitPositionWorldState.IsDaoTaiSecondaryOccupied(fruitId)
				|| XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(fruitId))
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
				continue;
			}

			if (!RollManifestation(definition, currentYear))
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
				continue;
			}

			if (TryManifest(definition, currentYear, i, out Actor manifested))
			{
				state.ActorId = GetActorId(manifested);
				state.NextAttemptYear = 0;
				state.LastStrategicYear = currentYear;
				state.LastManifestationYear = currentYear;
				state.ManifestationCount++;
				occupiedFruitIds.Add(NormalizeFruit(fruitId));
				changed = true;
				XjYaoShuSapientSpecies.TryAwakenForGreatSage(definition.SlotId, manifested.current_tile, currentYear);
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

	internal static void ReconcileAfterLoad(int currentYear)
	{
		currentYear = Math.Max(0, currentYear);
		_reconciledAfterLoad = true;
		if (!_assetsInitialized) Init();
		bool changed = false;
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
			if (state.ActorId > 0L)
			{
				state.ActorId = 0L;
				state.LastStrategicYear = 0;
				state.LastDepartureYear = currentYear;
				state.NextAttemptYear = ScheduleReentryAttempt(currentYear);
				changed = true;
			}
			if (state.NextAttemptYear <= 0)
			{
				state.NextAttemptYear = ScheduleNextAttempt(Definitions[i], currentYear);
				changed = true;
			}
		}
		if (changed) MarkChanged();
	}

	/// <summary>
	/// 陆江仙模拟器使用的一次性检验入口：跳过三百年门槛、五十年节拍与五成掷骰，
	/// 但仍严格尊重活着的大圣及普通修士已经占用的正位。
	/// </summary>
	internal static int TryDebugManifestAll(int currentYear)
	{
		currentYear = Math.Max(0, currentYear);
		if (!_reconciledAfterLoad) ReconcileAfterLoad(currentYear);
		if (!_assetsInitialized) Init();
		if (World.world?.units == null || !_assetsInitialized) return 0;

		HashSet<string> occupiedFruitIds = BuildOccupiedFruitSet();
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

			string fruitId = definition.FruitId;
			if (occupiedFruitIds.Contains(NormalizeFruit(fruitId))
				|| XjFruitPositionWorldState.IsDaoTaiSecondaryOccupied(fruitId)
				|| XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(fruitId))
			{
				state.NextAttemptYear = ScheduleNextAttempt(definition, currentYear);
				changed = true;
				continue;
			}

			if (TryManifest(definition, currentYear, i, out Actor manifested))
			{
				state.ActorId = GetActorId(manifested);
				state.NextAttemptYear = 0;
				state.LastStrategicYear = currentYear;
				state.LastManifestationYear = currentYear;
				state.ManifestationCount++;
				occupiedFruitIds.Add(NormalizeFruit(fruitId));
				manifestedCount++;
				changed = true;
				XjYaoShuSapientSpecies.TryAwakenForGreatSage(definition.SlotId, manifested.current_tile, currentYear);
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

	/// <summary>
	/// 果位注册表的外部占位探针。大圣不伪装成金丹，因此不写JinDan/GuoWeiRegistry；
	/// 仅在对应大圣真实存活时把该正位视为被果位化生占据。
	/// </summary>
	internal static bool IsExternalPositionOccupied(string guoWei)
	{
		string normalized = NormalizeFruit(guoWei);
		if (normalized.Length == 0) return false;
		for (int i = 0; i < Definitions.Length; i++)
		{
			if (!string.Equals(normalized, NormalizeFruit(Definitions[i].FruitId), StringComparison.Ordinal)) continue;
			return TryResolveLivingSage(Definitions[i], States[i].ActorId, out _);
		}
		return false;
	}

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
			if (fields.Length > 0 && long.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId))
				States[i].ActorId = Math.Max(0L, actorId);
			if (fields.Length > 1 && int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int nextYear))
				States[i].NextAttemptYear = Math.Max(0, nextYear);
			if (fields.Length > 2 && int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int pulseYear))
				States[i].LastStrategicYear = Math.Max(0, pulseYear);
			if (fields.Length > 3 && int.TryParse(fields[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int manifestationYear))
				States[i].LastManifestationYear = Math.Max(0, manifestationYear);
			if (fields.Length > 4 && int.TryParse(fields[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int departureYear))
				States[i].LastDepartureYear = Math.Max(0, departureYear);
			if (fields.Length > 5 && int.TryParse(fields[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out int countValue))
				States[i].ManifestationCount = Math.Max(0, countValue);
		}
		_reconciledAfterLoad = false;
	}

	/// <summary>
	/// 修士榜打开时的固定十二席回填。只按已有槽位 id 取 Actor，不枚举世界单位。
	/// </summary>
	internal static void EnsureRankMembership()
	{
		if (World.world?.units == null) return;
		int currentYear = Math.Max(0, World.world.map_stats?.year ?? 0);
		for (int i = 0; i < Definitions.Length; i++)
		{
			Definition definition = Definitions[i];
			if (!TryResolveLivingSage(definition, States[i].ActorId, out Actor sage)) continue;
			EnsureGreatSageIdentity(sage, definition, currentYear);
			XjActorRegistry.Register(sage, out _);
			XjCultivatorCache.CheckAndUpdate(sage);
		}
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
		_sharedGreatSageSubspeciesId = -1L;
		_sharedGreatSageSubspecies = null;
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
			string templateId = ResolveTemplate(library, definition.TemplateId);
			ActorAsset asset = library.get(definition.ActorAssetId) ?? library.clone(definition.ActorAssetId, templateId);
			if (asset == null) return;

			string localeKey = "xuanjian_" + definition.ActorAssetId;
			LocalizedTextManager.add(localeKey, definition.DisplayName, false, string.Empty, true);
			asset.name_locale = localeKey;
			asset.default_animal = false;
			asset.civ = false;
			asset.unit_other = true;
			asset.use_phenotypes = false;
			asset.has_baby_form = false;
			asset.can_evolve_into_new_species = false;
			asset.can_turn_into_zombie = false;
			asset.needs_to_be_explored = false;
			asset.unlocked_with_achievement = false;
			asset.show_in_knowledge_window = true;
			asset.show_for_unlockables_ui = true;
			asset.has_soul = true;
			asset.inspect_home = false;
			asset.can_be_inspected = true;
			asset.visible_on_minimap = false;
			asset.can_edit_traits = true;
			asset.can_talk_with = false;
			asset.control_can_talk = false;
			asset.use_items = false;
			asset.take_items = false;
			asset.job = new[] { "random_move" };
			asset.civ_base_cities = 0;
			asset.kingdom_id_civilization = string.Empty;
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
			asset.icon = "XjYaoShuGreatSage";
			asset.show_icon_inspect_window = true;
			asset.show_icon_inspect_window_id = "XjYaoShuGreatSage";
			ApplyGreatSageDefaultSubspeciesTraits(asset);
			TryConfigureActorVisuals(library, asset, definition);
			asset.unlock(false);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][妖属大圣] ActorAsset注册跳过 " + definition.SlotId + ": " + ex.GetType().Name);
		}
	}

	private static string ResolveTemplate(ActorAssetLibrary library, string preferred)
	{
		if (library.get(preferred) != null) return preferred;
		if (preferred.StartsWith("civ_", StringComparison.Ordinal))
		{
			string plain = preferred.Substring(4);
			if (library.get(plain) != null) return plain;
		}
		if (library.get("$mob$") != null) return "$mob$";
		if (library.get("human") != null) return "human";
		return preferred;
	}

	private static void TryConfigureActorVisuals(ActorAssetLibrary library, ActorAsset asset, Definition definition)
	{
		if (library == null || asset == null || definition == null) return;
		// clone 可能继承模板遗留的 has_override_sprite，而该委托不会随存档保存。
		// 若贴图资源不完整，必须先回退原生渲染，不能留下 true + null delegate 让渲染线程崩溃。
		asset.has_override_sprite = false;
		asset.get_override_sprite = null;
		GreatSageVisualFrames frames = BuildGreatSageVisualFrames(definition);
		Sprite banner = frames.Fallback;
		if (banner == null)
		{
			Debug.LogWarning("[玄鉴][妖属大圣] 未找到贴图，保留原生外观 " + definition.SlotId);
			return;
		}

		try
		{
			string basePath = SpritePathRoot + definition.ActorAssetId + "/";
			asset.texture_id = definition.ActorAssetId;
			asset.texture_asset = new ActorTextureSubAsset(basePath, false);
			asset.animation_walk = BuildAnimationNames("walk_");
			asset.animation_idle = BuildAnimationNames("idle_");
			asset.animation_walk_speed = 6f;
			asset.animation_idle_speed = 6f;
			asset.animation_speed_based_on_walk_speed = false;
			library.loadTexturesAndSprites(asset);
			asset._cached_sprite = banner;
			asset.cached_sprite = banner;
			VisualFramesByAssetId[definition.ActorAssetId] = frames;
			// 原生的 animation_container 只认识 idle/walk；大圣的源素材另有完整 attack 序列。
			// 因此只在本 Actor 自己的命中窗口覆写当前帧，不能再把所有状态硬塞为 walk_0~5。
			asset.get_override_sprite = actor => GetGreatSageOverrideSprite(definition, actor);
			asset.has_override_sprite = true;
		}
		catch (Exception ex)
		{
			asset.has_override_sprite = false;
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
			Idle = new Sprite[SpriteFrameCount],
			Walk = new Sprite[SpriteFrameCount],
			Attack = new Sprite[Math.Max(0, definition?.AttackFrameCount ?? 0)]
		};
		if (definition == null) return frames;

		for (int i = 0; i < SpriteFrameCount; i++)
		{
			frames.Idle[i] = TryLoadSprite(GetSpritePath(definition, "idle_" + i));
			frames.Walk[i] = TryLoadSprite(GetSpritePath(definition, "walk_" + i));
		}
		for (int i = 0; i < frames.Attack.Length; i++)
		{
			frames.Attack[i] = TryLoadSprite(GetSpritePath(
				definition,
				definition.VisualPrefix + "_attack_" + i.ToString("D3", CultureInfo.InvariantCulture)));
		}
		frames.Fallback = frames.Idle[0] ?? frames.Walk[0];
		return frames;
	}

	private static Sprite GetGreatSageOverrideSprite(Definition definition, Actor actor)
	{
		if (definition == null) return null;
		if (!VisualFramesByAssetId.TryGetValue(definition.ActorAssetId, out GreatSageVisualFrames frames)
			|| frames == null)
		{
			return actor?.asset?._cached_sprite ?? actor?.asset?.cached_sprite;
		}
		long actorId = GetActorId(actor);
		if (actorId > 0L
			&& AttackAnimationStartedAt.TryGetValue(actorId, out float startedAt)
			&& definition.AttackFrameCount > 0)
		{
			float elapsed = Mathf.Max(0f, Time.time - startedAt);
			int attackIndex = Mathf.FloorToInt(elapsed / AttackAnimationFrameSeconds);
			if (attackIndex >= 0 && attackIndex < frames.Attack.Length)
			{
				Sprite attack = frames.Attack[attackIndex];
				if (attack != null) return attack;
			}
		}

		int actorOffset = (int)(Math.Abs(actorId) % SpriteFrameCount);
		int index = Math.Abs(Time.frameCount / 10 + actorOffset) % SpriteFrameCount;
		return frames.Idle[index]
				?? frames.Walk[index]
				?? frames.Fallback
				?? actor?.asset?._cached_sprite
				?? actor?.asset?.cached_sprite;
	}

	private static string GetSpritePath(Definition definition, string frameName)
	{
		return SpritePathRoot + definition.ActorAssetId + "/main/" + frameName;
	}

	private static Sprite TryLoadSprite(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		try
		{
			return SpriteTextureLoader.getSprite(path)
				?? SpriteTextureLoader.getSprite("GameResources/" + path)
				?? Resources.Load<Sprite>(path)
				?? Resources.Load<Sprite>("GameResources/" + path);
		}
		catch
		{
			return null;
		}
	}

	private static HashSet<string> BuildOccupiedFruitSet()
	{
		HashSet<string> occupied = new HashSet<string>(StringComparer.Ordinal);
		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found || !entry.IsActive || entry.ActorId <= 0L) continue;
			string normalized = NormalizeFruit(entry.GuoWei);
			if (normalized.Length > 0) occupied.Add(normalized);
		}
		return occupied;
	}

	private static bool TryManifest(Definition definition, int currentYear, int ordinal, out Actor actor)
	{
		actor = null;
		MapBox world = World.world;
		if (world?.units == null || AssetManager.actor_library?.get(definition.ActorAssetId) == null) return false;
		if (!TryFindSpawnTile(definition, currentYear, ordinal, out WorldTile tile)) return false;
		Actor spawned = null;
		try
		{
			// 第一次完全交给原生 spawn 建立亚种；之后才传入已存在的共享亚种。
			// 不可在这里直接 newSpecies：该入口没有原生 spawn 的上下文，在新档会空引用。
			Subspecies sharedSubspecies = IsUsableGreatSageSubspecies(_sharedGreatSageSubspecies)
				? _sharedGreatSageSubspecies
				: null;
			spawned = world.units.spawnNewUnit(definition.ActorAssetId, tile, false, false, 0f, sharedSubspecies, false, true);
			if (spawned?.data == null) return false;
			spawned.data.age_overgrowth = 18;
			XjActorStateWriteGateway.SetDisplayName(spawned, definition.DisplayName, customName: true);
			EnsureGreatSageIdentity(spawned, definition, currentYear);
			EnsureGreatSageAttackCallback(spawned);
			XjActorRegistry.Register(spawned, out _);
			spawned.setStatsDirty();
			actor = spawned;
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][妖属大圣] 化生失败 " + definition.DisplayName + ": " + ex.GetType().Name);
			if (spawned?.data != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
			actor = null;
			return false;
		}
	}

	private static void EnsureSharedGreatSageSubspecies(Actor actor)
	{
		if (actor?.data == null || !IsUsableGreatSageSubspecies(actor.subspecies)) return;
		try
		{
			Subspecies current = actor.subspecies;
			NormalizeGreatSageSubspecies(current);
			if (!IsUsableGreatSageSubspecies(_sharedGreatSageSubspecies))
			{
				BindSharedGreatSageSubspecies(current);
			}
			else if (current.id != _sharedGreatSageSubspeciesId)
			{
				// 与龙属相同，只写 Actor 的亚种引用，避免 setSubspecies 触发运行中集合重入。
				XjNativeReflectionInterop.TryWriteMemberValue(actor, "subspecies", _sharedGreatSageSubspecies);
			}

			SetGreatSageSubspeciesId(actor, _sharedGreatSageSubspeciesId);
			NormalizeGreatSageSubspecies(_sharedGreatSageSubspecies);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.SharedSubspecies.Assign", ex);
		}
	}

	private static void BindSharedGreatSageSubspecies(Subspecies subspecies)
	{
		if (!IsUsableGreatSageSubspecies(subspecies)) return;
		_sharedGreatSageSubspecies = subspecies;
		_sharedGreatSageSubspeciesId = subspecies.id;
		NormalizeGreatSageSubspecies(subspecies);
	}

	private static bool IsUsableGreatSageSubspecies(Subspecies subspecies)
	{
		return subspecies != null && !subspecies.isRekt();
	}

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
			if (AssetManager.subspecies_traits?.get(traitId) != null)
			{
				asset.addSubspeciesTrait(traitId);
			}
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
		// 水域偏好单位在无水地图仍允许原生落地；这只是低频化生的固定预算兜底，
		// 不为了寻找理想地形扫描全图。
		if (definition.PreferWater)
		{
			for (int i = 0; i < SpawnProbeBudget / 2; i++)
			{
				int x = XjDeterministicHash.PositiveIndex(seed + 4001L + i * 71L, definition.SlotId + ".fallback.x", MapBox.width);
				int y = XjDeterministicHash.PositiveIndex(seed + 5003L + i * 89L, definition.SlotId + ".fallback.y", MapBox.height);
				WorldTile candidate = world.GetTileSimple(x, y);
				if (IsSuitableTile(candidate, preferWater: false))
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
		if (tile?.Type == null || tile.Type.lava || tile.hasBuilding()) return false;
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
		EnsureGreatSageIdentity(actor, definition, Math.Max(0, World.world?.map_stats?.year ?? 0));
		try
		{
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, Math.Max(1f, definition.Health));
			float heal = Math.Max(1f, maxHealth * ResolveStrategicHealFraction(definition.SlotId));
			XjSafeCore.HealActorSafe(actor, heal);
			actor.setStatsDirty();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.StrategicPulse." + definition.SlotId, ex);
		}
	}

	/// <summary>
	/// 把大圣神通附在该 Actor 自己的原生攻击回调上。没有批量 Actor 补丁、没有 AI 替换；
	/// 普攻命中、伤害、闪避和原生攻击节奏仍由 WorldBox 结算。
	/// </summary>
	private static void EnsureGreatSageAttackCallback(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			AttackAction current = actor.s_action_attack_target;
			if (ContainsAttackCallback(current, GreatSageAttackAction)) return;
			actor.s_action_attack_target = (AttackAction)Delegate.Combine(current, GreatSageAttackAction);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.BindAttack", ex);
		}
	}

	private static bool ContainsAttackCallback(AttackAction actions, AttackAction expected)
	{
		if (actions == null || expected == null) return false;
		Delegate[] callbacks = actions.GetInvocationList();
		for (int i = 0; i < callbacks.Length; i++)
		{
			if (callbacks[i] is AttackAction callback
				&& callback.Method == expected.Method
				&& ReferenceEquals(callback.Target, expected.Target))
			{
				return true;
			}
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
			try { impactTile = pTarget?.current_tile ?? pTile; }
			catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.TargetTile", ex); }
			if (impactTile == null)
			{
				try { impactTile = pSelf.current_tile; }
				catch (Exception ex) { XjExceptionDiagnostics.Report("YaoShuGreatSage.CasterTile", ex); }
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
		catch (Exception ex)
		{
			// 攻击回调绝不能中断原生攻击流程。
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Attack", ex);
		}
		return true;
	}

	private static bool IsReady(Dictionary<long, float> cooldowns, long actorId, float now)
	{
		return !cooldowns.TryGetValue(actorId, out float nextReadyAt) || now >= nextReadyAt;
	}

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
		string assetId = actor?.data?.asset_id;
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
			XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
				GreatSageEffectRoot + definition.EffectId,
				pos.x + 0.5f,
				pos.y + 0.5f,
				Mathf.Max(0.04f, scale),
				frameIntervalSeconds);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Visual." + definition.SlotId, ex);
		}
	}

	private static void ApplyBoundedTerrainImpact(Actor caster, WorldTile center, Definition definition, float now)
	{
		if (caster?.data == null || center == null || definition == null || World.world == null) return;
		int radius = Mathf.Clamp(definition.TerrainRadius, MinTerrainRadius, MaxTerrainRadius);
		TerraformOptions options = CreateTerrainOptions(definition.TerrainStyle);
		if (options == null) return;

		Vector2Int origin = center.pos;
		long seed = XjDeterministicHash.PositiveHash(
			GetActorId(caster) + Mathf.FloorToInt(now) * 31L,
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
				if (string.Equals(definition.TerrainStyle, "frost", StringComparison.Ordinal)
					&& tile.canBeFrozen())
				{
					tile.freeze(20);
				}
				XjAttributedTerrainInterop.TryDecreaseTile(tile, options, caster);
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("YaoShuGreatSage.Terrain." + definition.SlotId, ex);
			}
		}
	}

	private static TerraformOptions CreateTerrainOptions(string terrainStyle)
	{
		TerraformOptions options = new TerraformOptions { damage = 0 };
		switch (terrainStyle)
		{
			case "fire":
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				options.set_fire = true;
				options.add_burned = true;
				options.add_heat = 95;
				break;
			case "thunder":
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				options.lightning_effect = true;
				options.shake = true;
				options.shake_duration = 0.18f;
				options.shake_intensity = 0.006f;
				break;
			case "smash":
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				break;
			case "frost":
				options.damage_buildings = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				break;
			case "tide":
				options.damage_buildings = true;
				options.destroy_buildings = true;
				options.make_ruins = true;
				options.remove_roads = true;
				options.remove_trees_fully = true;
				break;
			default:
				return null;
		}
		return options;
	}

	private static float ResolveStrategicHealFraction(string slotId)
	{
		return slotId switch
		{
			"kanshui" => 0.45f,
			"jinghai" => 0.45f,
			"yuanzhao" => 0.38f,
			"bailin" => 0.35f,
			"zheqi" => 0.35f,
			"baixiang" => 0.30f,
			"xuanlei" => 0.28f,
			"chunhuo" => 0.25f,
			"taiyin" => 0.22f,
			"ziqi" => 0.22f,
			"tingsongli" => 0.20f,
			"jinchifu" => 0.20f,
			_ => 0.20f
		};
	}

	internal static bool IsGreatSage(Actor actor)
	{
		return actor?.data != null
			&& TryFindDefinition(actor, out _);
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
			EnsureSharedGreatSageSubspecies(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, 6);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualXjZz6Grant, 0);
			// 大圣走服气养性道脉：化生即为真君羽士初期，之后完全进入正常服气修炼与道胎链路。
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
				// 原生生育链以此特质为闸；大圣不留后嗣，也不会把共用亚种扩散给普通单位。
				EnsureTrait(actor, "infertile");
				EnsureTrait(actor, XjRealmIds.ZhenJunYuShi);
				if (XjDaoTuVisibleTraitCatalog.TryResolveTraitId(definition.DaoTu, out string daoTuTraitId))
				{
					EnsureTrait(actor, daoTuTraitId);
				}
				EnsureNativeTraits(actor, definition);
			}
			finally
			{
				XjExternalUnitTransferContext.ExitTraitTransfer();
			}
			actor.clearTraitCache();
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			EnsureGreatSageAuthority(actor, definition, currentYear);
			XjActorRegistry.Register(actor, out _);
			XjCultivatorCache.CheckAndUpdate(actor);
			actor.setStatsDirty();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Identity." + definition.SlotId, ex);
		}
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
			string.Empty, string.Empty, string.Empty, "妖属正位", string.Empty, 0, false, 0,
			"妖属大圣应正位而化，所执本柄随果位同显", "Active", Math.Max(1, currentYear)));
	}

	private static void EnsureNativeTraits(Actor actor, Definition definition)
	{
		if (actor?.data == null || definition == null) return;
		if (definition.NativeTraits == null) return;
		for (int i = 0; i < definition.NativeTraits.Length; i++)
		{
			EnsureTrait(actor, definition.NativeTraits[i]);
		}
	}

	private static void EnsureTrait(Actor actor, string traitId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId) || AssetManager.traits?.get(traitId) == null) return;
		try
		{
			if (!actor.hasTrait(traitId)) actor.addTrait(traitId, false);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Trait." + traitId, ex);
		}
	}

	private static bool TryResolveLivingSage(Definition definition, long actorId, out Actor actor)
	{
		actor = null;
		if (actorId <= 0L || definition == null || World.world?.units == null) return false;
		actor = World.world.units.get(actorId);
		if (!XjSafeCore.IsAliveActor(actor) || actor.data == null || !string.Equals(actor.data.asset_id, definition.ActorAssetId, StringComparison.Ordinal))
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
	{
		return definition == null
			|| !string.Equals(definition.SlotId, "yuanzhao", StringComparison.Ordinal)
			|| XjYuanZhaoKongZhengEvent.IsTriggered;
	}

	private static bool RollManifestation(Definition definition, int currentYear)
	{
		long slotSeed = XjDeterministicHash.PositiveHash(0L, "yaoshu.greatsage." + definition.SlotId);
		return XjDeterministicHash.Roll01(slotSeed, currentYear, "yaoshu_great_sage", "manifest") < ManifestationChance;
	}

	private static void RecordDeparture(Definition definition, long actorId, int currentYear)
	{
		if (definition == null || actorId <= 0L) return;
		XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
		string body = definition.DisplayName + "身殒，" + definition.FruitId
			+ "失主。天数沉寂百五十年，方许后继再问此位。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			definition.DisplayName + "身灭",
			body,
			importance: 3,
			isProtected: true,
			actorId: actorId,
			actorName: definition.DisplayName,
			year: currentYear,
			eventType: "YaoShuGreatSageDeparture",
			mirrorToWorldLog: true);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			"【大圣身殒】" + definition.DisplayName + "归寂，" + definition.DaoTu + "正位暂空。",
			XjAnnouncementCategory.HighRealm,
			duration: 8f,
			color: "#8DA2B8");
	}

	private static void RecordManifestation(Actor actor, Definition definition, int currentYear)
	{
		long actorId = GetActorId(actor);
		string body = definition.DisplayName + "应" + definition.FruitId + "而现，"
			+ "神通【" + definition.SkillName + "】初鸣；此位由其镇守。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			definition.DisplayName + "化生",
			body,
			importance: 4,
			isProtected: true,
			actorId: actorId,
			actorName: definition.DisplayName,
			year: currentYear,
			eventType: "YaoShuGreatSageManifestation",
			mirrorToWorldLog: true);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			"【大圣降临】" + definition.DisplayName + "应位而现，" + definition.SkillName + "初鸣。",
			XjAnnouncementCategory.HighRealm,
			duration: 8f,
			color: "#E1C46A");
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string NormalizeFruit(string guoWei)
	{
		return XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei ?? string.Empty).Trim();
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkModuleChanged(ModuleId);
	}
}
