using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
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
/// - 世界三百年后，十二个固定槽位按各自果位映照，每五十年作一次五成化生判定；
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
	// Subspecies 在 WorldBox 中隶属于具体 ActorAsset。十二圣虽然共用“妖属大圣”亚种特质与显示名，
	// 但绝不能把第一只大圣创建出的 Subspecies 对象强塞给另外十一种 ActorAsset；
	// 这会破坏 actor.asset <-> subspecies.getActorAsset() 的原生不变量，并可在人物面板
	// UnitStatsElement / 预渲染阶段制造空引用。每个大圣 ActorAsset 只复用自己的原生亚种。
	private static readonly Dictionary<string, Subspecies> RuntimeSubspeciesByAssetId =
		new Dictionary<string, Subspecies>(StringComparer.Ordinal);

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
	// 排行榜不能只依赖“普通修士缓存最终一致性”。十二圣只有十二席，直接维护稳定 ID 小集合，
	// 榜单构建时与普通修士 ID 合并即可；不缓存 Actor 引用，也不扫描 World.units。
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

			if (currentYear < ManifestationStartYear || currentYear < state.NextAttemptYear
				|| !CanAttemptManifestation(definition)) continue;

			string fruitId = definition.FruitId;
			// 大圣是果位“映照化生”，不是正位持有人：普通修士/道胎次位占用不得阻止大圣，
			// 也不得被大圣反向占座。只有果位本身被永久封锁时暂停化生。
			if (XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(fruitId))
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
		_reconcileDepartureGraceThroughYear = Math.Max(_reconcileDepartureGraceThroughYear, currentYear);
		if (!_assetsInitialized) Init();
		// 仅在载档/新世界初始化的冷路径做一次旧0.9.11兼容恢复。该版本曾把仍存活的
		// 大圣槽位误清为0，单靠 ActorId 已无法找回；这里允许一次 getSimpleList()，随后
		// 运行态仍完全回到十二席ID + ActorRegistry，不进入年度/UI热路径扫描。
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
			if (state.ActorId > 0L)
			{
				// 载档恢复阶段不以一次 Resolve miss 宣判大圣死亡。WorldBox 的单位索引与
				// Mod ActorRegistry 可能仍在回填；真正的离世判断交给后续年度 Tick，
				// 避免把仍存活 Actor 的权威槽位提前清成 0。
				continue;
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
	/// 仍尊重果位永久封锁；普通修士正位与大圣映照可以并存。
	/// </summary>
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

			string fruitId = definition.FruitId;
			if (XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(fruitId))
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
	/// 大圣只是果位映照，不占修士正位席次。保留该 API 供旧调用点兼容，但永远不作为
	/// GuoWeiRegistry 的外部占位源。果位永久封锁仍由注册表自身处理。
	/// </summary>
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
		_rankRegistryRecoveryPending = true;
		_legacyWorldRecoveryPending = true;
	}

	/// <summary>
	/// 修士榜打开时的固定十二席回填。正常路径只按已有槽位 id 取 Actor；
	/// 对 0.9.11 早期包已经出现“Actor 仍活着但槽位被误清”的旧运行态，仅在载档后
	/// 第一次打开榜单时从玄鉴 ActorRegistry 做一次迁移恢复，不枚举 World.units。
	/// </summary>
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
			else if (state.ActorId != actorId)
			{
				// 一个槽位只承认一个权威 Actor；旧包意外残留的重复实例不进入十二圣榜单。
				continue;
			}
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
			// 对齐龙属与诡秘 StarRiftCreatureAssets 的安全自定义 Actor 元数据：
			// 非高级纹理、非染色、明确可检查/亚种/翻转，避免模板遗留状态让
			// UnitStatsElement 或预渲染读取到不完整 texture/species 元数据。
			asset.has_advanced_textures = false;
			asset.need_colored_sprite = false;
			asset.color_hex = "#FFFFFF";
			asset.color = Toolbox.makeColor(asset.color_hex);
			asset.has_baby_form = false;
			asset.can_have_subspecies = true;
			asset.can_evolve_into_new_species = false;
			asset.can_turn_into_zombie = false;
			asset.needs_to_be_explored = false;
			asset.unlocked_with_achievement = false;
			asset.show_in_knowledge_window = true;
			asset.show_for_unlockables_ui = true;
			asset.has_soul = true;
			asset.inspect_home = false;
			asset.inspect_show_species = false;
			asset.can_be_inspected = true;
			asset.visible_on_minimap = false;
			// 自定义世界级单位不进入原生 trait-editor 反向打开 UnitWindow 的路径。
			// 当前 Player.log 的海量 NRE 正发生在该路径的 UnitStatsElement.showContent；
			// 关闭编辑只影响原生调试编辑入口，不影响正常人物查看、特质展示或玄鉴面板。
			asset.can_edit_traits = false;
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
			// 与龙属完全一致：大圣不属于任何文明、城市或势力，但 Actor 必须挂入
			// 原生 dragons 野生容器，才能获得非空 kingdom、空间分区和 AI 生命周期。
			// 这不是玩法上的“加入野生王国”，也不会使其加入野生阵营的社交/繁殖逻辑。
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
		// clone 可能继承模板遗留的 override；注册阶段先清空，再完整建立 texture metadata。
		// 旧实现先读取 attack Sprite、后 loadTexturesAndSprites，导致 attack 帧在首次注册时
		// 尚未进入 SpriteTextureLoader 缓存，VisualFramesByAssetId 永久保存了一组 null。
		asset.has_override_sprite = false;
		asset.get_override_sprite = null;
		VisualFramesByAssetId.Remove(definition.ActorAssetId);

		try
		{
			string mainPath = GetMainSpritePath(definition);
			asset.texture_id = definition.ActorAssetId;
			asset.texture_asset = new ActorTextureSubAsset(mainPath, false);
			if (asset.texture_asset != null)
			{
				// 对齐诡秘 StarRiftCreatureAssets：显式指定实际 main 目录，避免非高级
				// ActorTextureSubAsset 继续猜 male_1/heads_male 而得到空动画容器。
				asset.texture_asset.texture_path_main = mainPath.TrimEnd('/', '\\');
			}
			asset.animation_walk = BuildAnimationNames("walk_");
			asset.animation_idle = BuildAnimationNames("idle_");
			asset.animation_swim = asset.animation_walk;
			asset.animation_walk_speed = 6f;
			asset.animation_idle_speed = 6f;
			asset.animation_swim_speed = 6f;
			asset.animation_speed_based_on_walk_speed = false;
			asset._cached_sprite = null;
			asset.cached_sprite = null;

			// 必须先让 ActorAssetLibrary 建立该目录的基础动画，再缓存大圣专属 attack/run/breathing。
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
					+ " expected=" + definition.AttackFrameCount
					+ " loaded=" + (frames.Attack?.Length ?? 0));
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
			Idle = new Sprite[SpriteFrameCount],
			Walk = new Sprite[SpriteFrameCount],
			Breathing = Array.Empty<Sprite>(),
			Run = Array.Empty<Sprite>(),
			Attack = Array.Empty<Sprite>()
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
		frames.Fallback = FirstNonNull(frames.Idle)
			?? FirstNonNull(frames.Breathing)
			?? FirstNonNull(frames.Walk)
			?? FirstNonNull(frames.Run)
			?? FirstNonNull(frames.Attack);
		return frames;
	}

	private static Sprite[] LoadPrefixedSequence(Definition definition, string action, int expectedOrMaximum)
	{
		if (definition == null || string.IsNullOrWhiteSpace(action) || expectedOrMaximum <= 0) return Array.Empty<Sprite>();
		List<Sprite> result = new List<Sprite>(expectedOrMaximum);
		for (int i = 0; i < expectedOrMaximum; i++)
		{
			Sprite sprite = TryLoadSprite(GetSpritePath(
				definition,
				definition.VisualPrefix + "_" + action + "_" + i.ToString("D3", CultureInfo.InvariantCulture)));
			if (sprite == null)
			{
				// attack 使用精确帧数，其他序列使用上限探测；素材均从 000 连续编号。
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
		try
		{
			// getSpriteList 会让 NeoModLoader 的 GameResources 目录进入同一 Sprite 缓存；
			// 返回顺序不参与业务，只用于确保 attack/run/breathing 这些非原生动画名可按路径读取。
			SpriteTextureLoader.getSpriteList(GetMainSpritePath(definition).TrimEnd('/'), false);
		}
		catch { }
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
		if (actorId > 0L && AttackAnimationStartedAt.TryGetValue(actorId, out float startedAt))
		{
			float elapsed = Mathf.Max(0f, Time.time - startedAt);
			int attackIndex = Mathf.FloorToInt(elapsed / AttackAnimationFrameSeconds);
			if (attackIndex >= 0 && attackIndex < (frames.Attack?.Length ?? 0))
			{
				Sprite attack = frames.Attack[attackIndex];
				if (attack != null) return attack;
			}
		}

		bool moving = false;
		try { moving = actor != null && (actor.is_moving || actor.isUsingPath()); }
		catch { }
		Sprite[] sequence = moving && frames.Run?.Length > 0
			? frames.Run
			: (!moving && frames.Breathing?.Length > 0
				? frames.Breathing
				: (moving ? frames.Walk : frames.Idle));
		if (sequence == null || sequence.Length == 0) sequence = moving ? frames.Walk : frames.Idle;
		if (sequence != null && sequence.Length > 0)
		{
			int actorOffset = (int)(Math.Abs(actorId) % Math.Max(1, sequence.Length));
			float interval = moving ? 0.085f : 0.11f;
			int index = Math.Abs(Mathf.FloorToInt(Time.time / interval) + actorOffset) % sequence.Length;
			Sprite current = sequence[index];
			if (current != null) return current;
		}

		return frames.Fallback
			?? actor?.asset?._cached_sprite
			?? actor?.asset?.cached_sprite;
	}

	/// <summary>
	/// 供 calculateMainSprite 的专用前缀调用。这里绝不触发贴图读取：十二圣的所有
	/// Sprite 都已在 ActorAsset 注册阶段缓存完毕，渲染热路径只从小型只读帧表取值。
	/// </summary>
	internal static bool TryGetRenderSprite(Actor actor, out Sprite sprite)
	{
		sprite = null;
		if (!TryFindDefinition(actor, out Definition definition)) return false;
		sprite = GetGreatSageOverrideSprite(definition, actor);
		return sprite != null;
	}

	/// <summary>
	/// WorldBox 的单位渲染还会读取 frame_data 计算锚点与绘制数据。若只返回 override
	/// Sprite，攻击帧虽已选中，却可能沿用 idle 的 frame_data；这里按天地之魄的做法
	/// 令其同步到同名帧，找不到时退回容器内第一个有效帧，避免空引用和日志风暴。
	/// </summary>
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
		catch
		{
			// 渲染同步为可选边界；已缓存 Sprite 仍可安全显示，不能把单个单位拖入 Player.log 循环。
		}
	}

	private static string GetMainSpritePath(Definition definition)
	{
		return SpritePathRoot + definition.ActorAssetId + "/main/";
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

	private static bool TryManifest(Definition definition, int currentYear, int ordinal, out Actor actor)
	{
		actor = null;
		MapBox world = World.world;
		if (world?.units == null || AssetManager.actor_library?.get(definition.ActorAssetId) == null) return false;
		if (!TryFindSpawnTile(definition, currentYear, ordinal, out WorldTile tile)) return false;
		Actor spawned = null;
		try
		{
			// 第一次由原生 spawn 为该 ActorAsset 建立亚种；之后只复用“同一 ActorAsset”
			// 已绑定的亚种。不能跨十二圣共享 Subspecies 对象。
			Subspecies slotSubspecies = TryGetBoundGreatSageSubspecies(definition, out Subspecies boundSubspecies)
				? boundSubspecies
				: null;
			spawned = world.units.spawnNewUnit(definition.ActorAssetId, tile, false, false, 0f, slotSubspecies, false, true);
			if (spawned?.data == null) return false;
			// 先闭合 WorldBox 自身的 tile/野生容器关系，再写玄鉴身份。绝不能让一只
			// kingdom 为 null 的半成 Actor 进入 BatchActors；那会在原生 AI 更新中逐帧 NRE。
			if (!EnsureGreatSageNativeState(spawned, tile))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
				return false;
			}
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

	/// <summary>
	/// 对齐龙属的原生创建闭环。大圣没有文明归属，也不进城市；但原版 ActorManager
	/// 无条件读取 actor.kingdom.wild，故必须保留 dragons 野生容器这一底层引用。
	/// </summary>
	private static bool EnsureGreatSageNativeState(Actor actor, WorldTile requestedTile)
	{
		if (actor?.data == null || requestedTile?.chunk == null) return false;
		try
		{
			WorldTile currentTile = actor.current_tile;
			if (currentTile?.chunk == null)
			{
				actor.spawnOn(requestedTile);
				currentTile = actor.current_tile;
			}
			if (currentTile?.chunk == null) return false;

			if (actor.kingdom == null)
			{
				actor.setDefaultKingdom();
			}
			if (actor.kingdom == null || !actor.kingdom.wild) return false;

			// 大圣不进城市；只保留野生容器，宗门归属另由玄鉴宗门逻辑维护。
			actor.setCity(null);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void EnsureGreatSageSubspecies(Actor actor, Definition definition)
	{
		if (actor?.data == null || definition == null) return;
		try
		{
			Subspecies current = actor.subspecies;
			if (!IsUsableGreatSageSubspeciesForDefinition(current, definition))
			{
				// 0.9.11早期实现曾把十二种 ActorAsset 强行共用第一只大圣的 Subspecies。
				// 对已经写进存档的错配亚种做一次就地迁移，避免人物面板/预渲染继续读取
				// actor.asset 与 subspecies.species_id 不一致的原生元数据。
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
				// 同一 ActorAsset 的后续化生统一回自己的亚种；仅写 Actor 引用，
				// 避免 setSubspecies 在运行中触发集合重入。
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
		if (definition == null
			|| !RuntimeSubspeciesByAssetId.TryGetValue(definition.ActorAssetId, out Subspecies candidate)
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
		try
		{
			return string.Equals(subspecies.getActorAsset()?.id, definition.ActorAssetId, StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static Subspecies TryRepairLegacyCrossAssetSubspecies(Actor actor, Definition definition, Subspecies source)
	{
		if (actor?.data == null || definition == null || source?.data == null
			|| World.world?.subspecies == null || World.world?.map_stats == null) return null;
		try
		{
			SubspeciesData copy = JsonConvert.DeserializeObject<SubspeciesData>(
				JsonConvert.SerializeObject(source.data, Formatting.None));
			if (copy == null) return null;
			copy.id = World.world.map_stats.getNextId("subspecies");
			copy.name = GreatSageSubspeciesName;
			copy.custom_name = true;
			copy.species_id = definition.ActorAssetId;
			// 原 parent_subspecies 属于另一 ActorAsset，不可继续保留跨种父链。
			copy.parent_subspecies = 0L;
			copy.created_time = World.world.map_stats.world_time;
			copy.total_deaths = 0L;
			copy.total_births = 0L;
			copy.total_kills = 0L;
			World.world.subspecies.loadObject(copy);
			Subspecies repaired = World.world.subspecies.get(copy.id);
			if (!IsUsableGreatSageSubspeciesForDefinition(repaired, definition)) return null;
			actor.setSubspecies(repaired);
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
		int currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		EnsureGreatSageIdentity(actor, definition, currentYear);
		// 妖民正常人口由原生 BabyMaker 维持；十年脉冲只在该族已真正灭绝时低频再点化。
		XjYaoShuSapientSpecies.TryMaintainForGreatSage(definition.SlotId, actor.current_tile, currentYear);
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
		// 仙鉴与排行榜共用同一十二席恢复入口，避免旧0.9.11存档里 Actor 仍存活、
		// 但历史槽位曾被误清后出现“仙鉴0/12、世界里却看得到大圣”的分裂状态。
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
			// 载入旧存档时也按龙属规则修复遗留的空 kingdom，不创建文明、不强加势力。
			if (!EnsureGreatSageNativeState(actor, actor.current_tile)) return;
			EnsureGreatSageSubspecies(actor, definition);
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
			TrackRankActor(GetActorId(actor));
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
			string.Empty, string.Empty, string.Empty, "妖属映照", string.Empty, 0, false, 0,
			"妖属大圣应果位而化，只作映照，不占修士正位；所执本柄随果位同显", "Active", Math.Max(1, currentYear)));
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
		if (actorId <= 0L || definition == null) return false;
		// 刚 spawn/刚载档的自定义单位可能已经存在于玄鉴 ActorRegistry，但尚未稳定进入
		// World.world.units.get(id) 的原生索引。旧实现只查后者，会把仍活着的大圣误判为
		// “身殒”，清空槽位，正是仙鉴显示0/12且世界里Actor仍存在的根因。
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
		RemoveRankActor(actorId);
		XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
		string body = definition.DisplayName + "身殒，" + definition.FruitId
			+ "映照沉寂。天数沉寂百五十年，方许后继再度化生。";
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
			"【大圣身殒】" + definition.DisplayName + "归寂，" + definition.DaoTu + "映照暂寂。",
			XjAnnouncementCategory.HighRealm,
			duration: 8f,
			color: "#8DA2B8");
	}

	private static void RecordManifestation(Actor actor, Definition definition, int currentYear)
	{
		long actorId = GetActorId(actor);
		string body = definition.DisplayName + "应" + definition.FruitId + "而现，"
			+ "神通【" + definition.SkillName + "】初鸣；只映其位，不夺修士正席。";
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
