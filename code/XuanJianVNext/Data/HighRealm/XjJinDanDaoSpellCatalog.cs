using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.HighRealm;

internal readonly struct XjJinDanDaoSpellDefinition
{
	internal XjJinDanDaoSpellDefinition(
		string id,
		string displayName,
		string profile,
		string daoTuGroup,
		IReadOnlyList<string> daoTuNames,
		float cooldownSeconds,
		int radius,
		int terrainRadius,
		float damageMultiplier,
		float sameRealmMaxHealthDamageRatio,
		float statusDurationSeconds,
		float terrainDurationSeconds,
		int maxTargets,
		string targetMode,
		string terrainEffect,
		string statusEffectId,
		float healMaxHealthRatio,
		string animationPath,
		float animationScale,
		float animationFrameIntervalSeconds,
		float animationLifetimeSeconds,
		float floatDurationSeconds,
		float smashDurationSeconds,
		string specialFlags,
		int durationYears = 0,
		int cooldownYears = 0,
		float selfDodgeBonusPercent = 0f,
		IReadOnlyList<string> requiredShenTongIds = null)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		Profile = profile ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		DaoTuNames = daoTuNames ?? Array.Empty<string>();
		// 真君/金丹战斗法术统一 30 秒独立冷却。具体法术的多年存续、
		// 召唤实体寿命等属于额外状态，不再改变这一层战斗施法 CD。
		CooldownSeconds = 30f;
		Radius = radius < 0 ? 0 : radius;
		TerrainRadius = terrainRadius < 0 ? 0 : terrainRadius;
		DamageMultiplier = damageMultiplier < 0f ? 0f : damageMultiplier;
		SameRealmMaxHealthDamageRatio = sameRealmMaxHealthDamageRatio < 0f ? 0f : sameRealmMaxHealthDamageRatio;
		StatusDurationSeconds = statusDurationSeconds < 0f ? 0f : statusDurationSeconds;
		TerrainDurationSeconds = terrainDurationSeconds < 0f ? 0f : terrainDurationSeconds;
		MaxTargets = maxTargets < 0 ? 0 : maxTargets;
		TargetMode = targetMode ?? string.Empty;
		TerrainEffect = terrainEffect ?? string.Empty;
		StatusEffectId = statusEffectId ?? string.Empty;
		HealMaxHealthRatio = healMaxHealthRatio < 0f ? 0f : healMaxHealthRatio;
		AnimationPath = animationPath ?? string.Empty;
		AnimationScale = animationScale < 0f ? 0f : animationScale;
		AnimationFrameIntervalSeconds = animationFrameIntervalSeconds < 0f ? 0f : animationFrameIntervalSeconds;
		AnimationLifetimeSeconds = animationLifetimeSeconds < 0f ? 0f : animationLifetimeSeconds;
		FloatDurationSeconds = floatDurationSeconds < 0f ? 0f : floatDurationSeconds;
		SmashDurationSeconds = smashDurationSeconds < 0f ? 0f : smashDurationSeconds;
		SpecialFlags = specialFlags ?? string.Empty;
		DurationYears = durationYears < 0 ? 0 : durationYears;
		CooldownYears = cooldownYears < 0 ? 0 : cooldownYears;
		SelfDodgeBonusPercent = selfDodgeBonusPercent < 0f ? 0f : Math.Min(100f, selfDodgeBonusPercent);
		RequiredShenTongIds = requiredShenTongIds ?? Array.Empty<string>();
	}

	internal string Id { get; }
	internal string DisplayName { get; }
	internal string Profile { get; }
	internal string DaoTuGroup { get; }
	internal IReadOnlyList<string> DaoTuNames { get; }
	internal float CooldownSeconds { get; }
	internal int Radius { get; }
	internal int TerrainRadius { get; }
	internal float DamageMultiplier { get; }
	internal float SameRealmMaxHealthDamageRatio { get; }
	internal float StatusDurationSeconds { get; }
	internal float TerrainDurationSeconds { get; }
	internal int MaxTargets { get; }
	internal string TargetMode { get; }
	internal string TerrainEffect { get; }
	internal string StatusEffectId { get; }
	internal float HealMaxHealthRatio { get; }
	internal string AnimationPath { get; }
	internal float AnimationScale { get; }
	internal float AnimationFrameIntervalSeconds { get; }
	internal float AnimationLifetimeSeconds { get; }
	internal float FloatDurationSeconds { get; }
	internal float SmashDurationSeconds { get; }
	internal string SpecialFlags { get; }
	internal int DurationYears { get; }
	internal int CooldownYears { get; }
	internal float SelfDodgeBonusPercent { get; }
	/// <summary>
	/// 非空时，本定义只允许真正掌握其中至少一门神通的角色进入主动轮询。
	/// 0.9.8.6 用它把 Excel 原著神通表现与“按道途发一个通用技能”彻底分开。
	/// </summary>
	internal IReadOnlyList<string> RequiredShenTongIds { get; }
}

internal static class XjJinDanDaoSpellCatalog
{
	internal static readonly IReadOnlyList<XjJinDanDaoSpellDefinition> All = new[]
	{
		new XjJinDanDaoSpellDefinition(
			"CommonLeiFa",
			"金丹雷法",
			"CommonLeiFa",
			"Common",
			Array.Empty<string>(),
			30f,
			// 0.9.8.28：0.21 金丹通用雷法的核心观感是约60格主爆区，而不是
			// 依赖小 trait.range。现行金丹静态range本就达到48，因此只恢复主动天灾尺度。
			60,
			32,
			1.2f,
			0.15f,
			0f,
			0f,
			0,
			"Area",
			"CommonLeiFaTerrain",
			string.Empty,
			0f,
			"effects/Skills/JinDanLeiFa",
			0.5f,
			0.08f,
			1.4f,
			0f,
			0f,
			"TerrainLightning"),
		new XjJinDanDaoSpellDefinition(
			"JiuXiaoShenLei",
			"九霄神雷",
			"JiuXiaoShenLei",
			"SanLei",
			new[] { "玄雷", "霄雷", "元雷" },
			30f,
			8,
			1,
			1.5f,
			0.15f,
			0f,
			0f,
			1,
			"Single",
			"JiuXiaoTerrain",
			string.Empty,
			0f,
			"effects/Skills/JiuXiaoShenLei",
			0.5f,
			0.1f,
			1.4f,
			0f,
			0f,
			"SingleTargetLightning;TerrainLightning"),
		// 0.9.8.5：金德五支不再共用一门“金德剑阵”。各分支只保留一门
		// 主动战斗法术，长庚则彻底退出主动法术池，改由飞剑普攻承载剑道。
		new XjJinDanDaoSpellDefinition(
			"DuiJinQingLiang",
			"请两忘",
			"DuiJinQingLiang",
			"JinDe",
			new[] { "兑金" },
			30f, 38, 0, 1.50f, 0.16f, 0f, 0f, 3, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Shared/JinDeRadiantSlash", 0.18f, 0.055f, 1.1f, 0f, 0f,
			"JinDeDui;MaxTargets=3;ColdMetalSlash"),
		new XjJinDanDaoSpellDefinition(
			"GengJinXiaoGuFeng",
			"削故锋",
			"GengJinXiaoGuFeng",
			"JinDe",
			new[] { "庚金" },
			30f, 54, 0, 1.75f, 0.18f, 0f, 0f, 1, "Single", string.Empty, string.Empty, 0f,
			string.Empty, 0f, 0f, 0f, 0f, 0f,
			"JinDeGeng;SingleSword;HighBreak"),
		new XjJinDanDaoSpellDefinition(
			"JinDeJianZhen",
			"金戈阵",
			"JinDeJianZhen",
			"JinDe",
			new[] { "齐金" },
			30f, 44, 0, 1.45f, 0.15f, 0f, 0f, 5, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/JinDeJianZhen", 0.34f, 0.08f, 1.1f, 0f, 0f,
			"JinDeQi;MaxTargets=5;SwordArray"),
		new XjJinDanDaoSpellDefinition(
			"KuJinJinXiaoDong",
			"金销洞",
			"KuJinJinXiaoDong",
			"JinDe",
			new[] { "库金" },
			30f, 36, 0, 1.15f, 0.11f, 0f, 0f, 3, "Limited", string.Empty, string.Empty, 0.12f,
			"effects/Skills/JinDeJianZhen", 0.22f, 0.08f, 1.1f, 0f, 0f,
			"JinDeKu;MaxTargets=3;SelfHeal=12;HiddenArmory"),
		new XjJinDanDaoSpellDefinition(
			"XiaoJinWangShangFeng",
			"望商锋",
			"XiaoJinWangShangFeng",
			"JinDe",
			new[] { "逍金" },
			30f, 50, 0, 1.35f, 0.13f, 0f, 0f, 4, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Shared/JinDeRadiantSlash", 0.14f, 0.055f, 1.0f, 0f, 0f,
			"JinDeXiao;MaxTargets=4;SwiftMetalSlash"),

		// 0.9.8.6：以下定义只在角色真正掌握对应原著神通时进入候选池。
		// 它们排在十二炁/并古的旧“组合技”之前；未学会时仍保留旧组合技作为兜底，
		// 因此旧档和低神通数量角色不会突然失去战斗能力。
		new XjJinDanDaoSpellDefinition(
			"SuiQiAnTianYang",
			"闇天殃",
			"SuiQiAnTianYang",
			"ShiErQi",
			new[] { "邃炁" },
			30f, 42, 0, 1.48f, 0.14f, 2.6f, 0f, 4, "Limited", string.Empty, "root", 0f,
			"effects/Skills/Canon0986/InnerWorld", 0.28f, 0.075f, 1.25f, 0f, 0f,
			"Canon0986;SuiQi;MindDisorder;ArtifactSuppression",
			0, 0, 0f, new[] { "闇天殃" }),
		new XjJinDanDaoSpellDefinition(
			"ZiQiDaoShiZhao",
			"道始兆",
			"ZiQiDaoShiZhao",
			"ShiErQi",
			new[] { "紫炁" },
			30f, 48, 0, 1.36f, 0.12f, 1.8f, 0f, 5, "Limited", string.Empty, "root", 0f,
			string.Empty, 0f, 0f, 0f, 0f, 0f,
			"Canon0986;ZiQi;FourQiGods;CompositeVisual",
			0, 0, 0f, new[] { "道始兆", "蕴宝瓶" }),
		new XjJinDanDaoSpellDefinition(
			"ZiQiKunChenXiu",
			"坤辰修",
			"ZiQiKunChenXiu",
			"ShiErQi",
			new[] { "紫炁" },
			30f, 40, 0, 0f, 0f, 2.8f, 0f, 4, "Limited", string.Empty, "root", 0f,
			"effects/Skills/Canon0986/PurpleLightning", 0.16f, 0.065f, 1.0f, 0.18f, 0f,
			"Canon0986;ZiQi;Interrupt;Disarm",
			0, 0, 0f, new[] { "坤辰修" }),
		new XjJinDanDaoSpellDefinition(
			"XiQiWeiQueHua",
			"未阕华",
			"XiQiWeiQueHua",
			"ShiErQi",
			new[] { "晞炁" },
			30f, 48, 5, 1.42f, 0.13f, 3.0f, 3.0f, 5, "Limited", "BurningGround", "burning", 0f,
			"effects/Skills/Canon0986/FireFormation", 0.22f, 0.075f, 1.2f, 0f, 0f,
			"Canon0986;XiQi;SunriseGlow;SuppressColdWater",
			0, 0, 0f, new[] { "未阕华", "焜煌复" }),
		new XjJinDanDaoSpellDefinition(
			"XiQiQiDaiYe",
			"乞代夜",
			"XiQiQiDaiYe",
			"ShiErQi",
			new[] { "晞炁" },
			30f, 44, 0, 1.20f, 0.10f, 0f, 0f, 4, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Canon0986/InnerWorld", 0.22f, 0.075f, 1.2f, 0f, 0f,
			"Canon0986;XiQi;DimLight;AbsorbLightFire",
			0, 0, 0f, new[] { "乞代夜", "掩尘雾" }),
		new XjJinDanDaoSpellDefinition(
			"YuZhenYuTingJiang",
			"玉庭将",
			"YuZhenYuTingJiang",
			"BingGu",
			new[] { "玉真" },
			30f, 42, 0, 1.34f, 0.12f, 0f, 0f, 4, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Canon0986/SpearRain", 0.15f, 0.065f, 1.1f, 0f, 0f,
			"Canon0986;YuZhen;JadeWeapon;Shock",
			0, 0, 0f, new[] { "玉中人", "玉庭将" }),
		new XjJinDanDaoSpellDefinition(
			"ShangWuYinMinXue",
			"饮民血",
			"ShangWuYinMinXue",
			"BingGu",
			new[] { "上巫" },
			30f, 40, 0, 1.50f, 0.15f, 0f, 0f, 3, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Canon0986/FireFormation", 0.13f, 0.07f, 0.9f, 0f, 0f,
			"Canon0986;ShangWu;DeepRedLawLight",
			0, 0, 0f, new[] { "饮民血" }),
		new XjJinDanDaoSpellDefinition(
			"HengZhuDianYangHu",
			"殿阳虎",
			"HengZhuDianYangHu",
			"BingGu",
			new[] { "衡祝" },
			30f, 46, 0, 1.52f, 0.15f, 0f, 0f, 3, "Limited", string.Empty, string.Empty, 0f,
			"effects/Skills/Canon0986/FlameLance", 0.15f, 0.06f, 0.9f, 0f, 0f,
			"Canon0986;HengZhu;BloodWind;EyeStrike",
			0, 0, 0f, new[] { "殿阳虎" }),
		// 0.9.8.7 第二批：只把 Excel 明确具有可战斗“动作”的神通放入主动轮询。
		// 抱石眠、好功箓、冠灵旒、候神殊等以被动/显化为主，不为了凑数量硬编直伤。
		new XjJinDanDaoSpellDefinition(
			"HanQiSongShangXue",
			"松上雪",
			"HanQiSongShangXue",
			"ShiErQi",
			new[] { "寒炁" },
			30f, 0, 0, 0f, 0f, 0f, 0f, 0, "Self", string.Empty, string.Empty, 0f,
			string.Empty, 0f, 0f, 0f, 0f, 0f,
			"Canon0987;HanQi;SnowWindBuff;NoInventedDamage",
			0, 0, 0f, new[] { "松上雪" }),
		new XjJinDanDaoSpellDefinition(
			"ShangYiSheShouWang",
			"射狩王",
			"ShangYiSheShouWang",
			"ShiErQi",
			new[] { "上仪" },
			30f, 48, 0, 0f, 0f, 0f, 0f, 1, "Single", string.Empty, string.Empty, 0f,
			string.Empty, 0f, 0f, 0f, 0f, 0f,
			"Canon0987;ShangYi;SwapBodyPosition;EscapeEncirclement",
			0, 0, 0f, new[] { "射狩王" }),
		new XjJinDanDaoSpellDefinition(
			"QingXuanFuQingShan",
			"伏青山",
			"QingXuanFuQingShan",
			"BingGu",
			new[] { "青宣" },
			30f, 0, 0, 0f, 0f, 0f, 0f, 0, "Self", string.Empty, string.Empty, 0f,
			string.Empty, 0f, 0f, 0f, 0f, 0f,
			"Canon0987;QingXuan;BattleEscalation;DemonSuppressionAbstractedAsStack",
			0, 0, 0f, new[] { "伏青山" }),
		new XjJinDanDaoSpellDefinition(
			"ZhiBoJianKuangRang",
			"僭劻勷",
			"ZhiBoJianKuangRang",
			"BingGu",
			new[] { "执孛" },
			30f, 46, 0, 0f, 0f, 1.8f, 0f, 4, "Limited", string.Empty, "root", 0f,
			string.Empty, 0f, 0f, 0f, 0.15f, 0f,
			"Canon0987;ZhiBo;PartitionFourDirections;SeverQi;FormationBreakApprox",
			0, 0, 0f, new[] { "僭劻勷", "倏动勃" }),
		new XjJinDanDaoSpellDefinition(
			"XuanQiFengZhen",
			"玄炁风阵",
			"XuanQiFengZhen",
			"ShiErQi",
			new[] { "十二炁" },
			30f,
			46,
			0,
			1.35f,
			0.12f,
			2.4f,
			0f,
			6,
			"Limited",
			string.Empty,
			"root",
			0f,
			"effects/Skills/XuanQiFengZhen",
			0.20f,
			0.08f,
			2.4f,
			0f,
			0f,
			"RootTwelveQi;MaxTargets=6;WindFormation"),
		new XjJinDanDaoSpellDefinition(
			"TianYiXuanGang",
			"天仪玄罡",
			"TianYiXuanGang",
			"ShiErQi",
			new[] { "清炁", "紫炁", "真炁", "上仪" },
			30f, 46, 0, 1.30f, 0.12f, 2.8f, 0f, 5, "Limited", string.Empty, "root", 0f,
			"effects/Skills/XuanQiFengZhen", 0.20f, 0.08f, 2.4f, 0f, 0f,
			"ShiErQiOrderGroup;MaxTargets=5;Root"),
		new XjJinDanDaoSpellDefinition(
			"HanXiHuaJing",
			"寒晞华景",
			"HanXiHuaJing",
			"ShiErQi",
			new[] { "寒炁", "晞炁", "瑞炁", "华炁" },
			30f, 50, 0, 1.38f, 0.13f, 1.8f, 0f, 6, "Limited", string.Empty, "root", 0f,
			"effects/Skills/XuanQiFengZhen", 0.22f, 0.08f, 2.1f, 0f, 0f,
			"ShiErQiManifestGroup;MaxTargets=6;BriefHold"),
		new XjJinDanDaoSpellDefinition(
			"YouShaZheMing",
			"幽煞谪冥",
			"YouShaZheMing",
			"ShiErQi",
			new[] { "邃炁", "煞炁", "谪炁", "下仪" },
			30f, 48, 0, 1.55f, 0.15f, 1.2f, 0f, 6, "Limited", string.Empty, "root", 0f,
			"effects/Skills/JiuXiaoShenLei", 0.42f, 0.10f, 1.5f, 0f, 0f,
			"ShiErQiNetherGroup;MaxTargets=6;HighDamage"),
		new XjJinDanDaoSpellDefinition(
			"WuZhuYouHuo",
			"巫祝幽火",
			"WuZhuYouHuo",
			"BingGu",
			new[] { "鸺葵", "上巫", "衡祝" },
			30f, 44, 5, 1.40f, 0.12f, 3f, 4f, 4, "Limited", "BurningGround", "burning", 0f,
			"effects/Skills/YangYanLianJi", 0.42f, 0.09f, 1.4f, 0f, 0f,
			"BingGuWitchGroup;MaxTargets=4;BurningGround"),
		new XjJinDanDaoSpellDefinition(
			"YuTingQuanZhenYin",
			"玉庭全真印",
			"YuTingQuanZhenYin",
			"BingGu",
			new[] { "玉真", "青宣", "全丹" },
			30f, 42, 0, 1.25f, 0.11f, 2.4f, 0f, 5, "Limited", string.Empty, "root", 0.12f,
			"effects/Skills/ZhenYueBeng/ZhenYueBeng", 0.18f, 0.09f, 1.5f, 0f, 0f,
			"BingGuInnerGroup;MaxTargets=5;SelfHeal=12;Root"),
		new XjJinDanDaoSpellDefinition(
			"BoXingDuTianWei",
			"孛星都天卫",
			"BoXingDuTianWei",
			"BingGu",
			new[] { "执孛", "司天", "都卫" },
			30f, 50, 0, 1.45f, 0.14f, 1.5f, 0f, 6, "Limited", string.Empty, "root", 0f,
			"effects/Skills/JiuXiaoShenLei", 0.40f, 0.10f, 1.5f, 0f, 0f,
			"BingGuAstralGroup;MaxTargets=6;Root"),
		new XjJinDanDaoSpellDefinition(
			"DaRiZhuiYuan",
			"大日坠渊",
			"DaRiZhuiYuan",
			"SanYang",
			new[] { "太阳" },
			30f,
			96,
			32,
			1.5f,
			0.25f,
			0f,
			0f,
			0,
			"Area",
			"DaRiTerrain",
			string.Empty,
			0f,
			"effects/Skills/DaRiZhuiYuan",
			1f,
			0.1f,
			1.6f,
			0f,
			0f,
			string.Empty),
		new XjJinDanDaoSpellDefinition(
			"YingYueLianHua",
			"映月莲华",
			"YingYueLianHua",
			"SanYin",
			new[] { "太阴" },
			30f,
			60,
			120,
			2f,
			0.2f,
			15f,
			8f,
			0,
			"Area",
			"FrozenTerrain",
			"frozen",
			0.3f,
			"effects/Skills/YingYueLianHua",
			0.35f,
			0.22f,
			3.2f,
			0f,
			0f,
			"SelfHealMaxHealthRatio=0.3;FrozenTileSeconds=8"),
		new XjJinDanDaoSpellDefinition(
			"SanYinBaoYiXuanLun",
			"三阴抱一玄轮",
			"SanYinBaoYiXuanLun",
			"SanYin",
			new[] { "太阴", "少阴", "厥阴" },
			60f,
			0,
			0,
			0f,
			0f,
			30f,
			0f,
			0,
			"Self",
			string.Empty,
			string.Empty,
			0f,
			"effects/Skills/SanYinBaoYiXuanLun",
			1f,
			0.14f,
			30f,
			0f,
			0f,
			"SelfBuff;TaiYinLowHealthDodge;At20Percent=100;OtherSanYinDodgeBonus=90;LoopOverhead;VisualSize=2Actors",
			0,
			0,
			90f),
		new XjJinDanDaoSpellDefinition(
			"BinFengTuCi",
			"冰封突刺",
			"BinFengTuCi",
			"ShuiDe",
			new[] { "坎水", "渌水", "合水", "府水", "牝水" },
			30f,
			46,
			6,
			1.5f,
			0.15f,
			4f,
			6f,
			3,
			"Limited",
			"FrozenTerrain",
			"frozen",
			0f,
			"effects/Skills/BinFengTuCi",
			0.275f,
			0.08f,
			1.5f,
			0f,
			0f,
			"MaxTargets=3;FrozenTileSeconds=6"),
		new XjJinDanDaoSpellDefinition(
			"QingTengFu",
			"青藤符",
			"QingTengFu",
			"MuDe",
			new[] { "角木", "正木", "集木", "更木", "保木" },
			28f,
			40,
			0,
			1.5f,
			0.15f,
			5f,
			0f,
			5,
			"Limited",
			string.Empty,
			"root",
			0f,
			"effects/Skills/QingTengFu",
			0.14f,
			0.08f,
			1.3f,
			0f,
			0f,
			"MaxTargets=5;Root"),
		new XjJinDanDaoSpellDefinition(
			"YangYanLianJi",
			"阳炎敛迹",
			"YangYanLianJi",
			"SanYang",
			new[] { "太阳", "少阳", "明阳" },
			28f,
			52,
			52,
			1.5f,
			0.15f,
			0f,
			0f,
			0,
			"Area",
			"FireTerrain",
			string.Empty,
			0f,
			"effects/Skills/YangYanLianJi",
			0.75f,
			0.09f,
			1.5f,
			0f,
			0f,
			"TerrainFire"),
		new XjJinDanDaoSpellDefinition(
			"ZhuMingFenTianQue",
			"朱明焚天雀",
			"ZhuMingFenTianQue",
			"HuoDe",
			new[] { "离火", "灴火", "并火", "真火", "牡火" },
			0f,
			52,
			8,
			0.8f,
			0.08f,
			0f,
			4f,
			3,
			"Limited",
			"BurningGround",
			"burning",
			0f,
			"effects/Skills/ZhuMingFenTianQue",
			1f,
			0.09f,
			0f,
			0f,
			0f,
			"SummonFireBird;DurationYears=5;CooldownYears=3;FollowCasterTarget;AlternatingAttacks;ProjectileFire",
			5,
			3),
		new XjJinDanDaoSpellDefinition(
			"ZhenYueBeng",
			"镇岳崩",
			"ZhenYueBeng",
			"TuDe",
			new[] { "艮土", "戊土", "归土", "宝土", "宣土" },
			32f,
			96,
			32,
			1.5f,
			0.15f,
			0f,
			0f,
			0,
			"Area",
			"SmashTerrain",
			string.Empty,
			0f,
			"effects/Skills/ZhenYueBeng/ZhenYueBeng",
			0.2f,
			0f,
			0f,
			0.16f,
			0.24f,
			"SmashDuration=0.24;ImpactHold=0.03")
	};

	private static readonly XjJinDanDaoSpellDefinition[] EmptyDefinitions = Array.Empty<XjJinDanDaoSpellDefinition>();
	private static readonly Dictionary<string, XjJinDanDaoSpellDefinition[]> DefinitionsByDaoTu = BuildDaoTuIndex();
	private static readonly XjJinDanDaoSpellDefinition[] CommonFallback = BuildCommonFallback();

	/// <summary>
	/// Allocation-free runtime view. The catalog is immutable after type initialization,
	/// so combat can reuse pre-indexed arrays instead of scanning All and constructing a
	/// temporary List on every attack callback. Ordering is identical to All.
	/// </summary>
	internal static IReadOnlyList<XjJinDanDaoSpellDefinition> ReadForDaoTu(string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		if (string.Equals(normalized, "长庚", StringComparison.Ordinal)) return EmptyDefinitions;
		return DefinitionsByDaoTu.TryGetValue(normalized, out XjJinDanDaoSpellDefinition[] definitions)
			? definitions
			: CommonFallback;
	}

	private static Dictionary<string, XjJinDanDaoSpellDefinition[]> BuildDaoTuIndex()
	{
		Dictionary<string, List<XjJinDanDaoSpellDefinition>> staged =
			new Dictionary<string, List<XjJinDanDaoSpellDefinition>>(StringComparer.Ordinal);
		for (int i = 0; i < All.Count; i++)
		{
			XjJinDanDaoSpellDefinition candidate = All[i];
			for (int j = 0; j < candidate.DaoTuNames.Count; j++)
			{
				// Preserve the original CollectForDaoTu comparison exactly: the incoming
				// daoTu is trimmed, catalog entries are compared verbatim.
				string name = candidate.DaoTuNames[j] ?? string.Empty;
				if (!staged.TryGetValue(name, out List<XjJinDanDaoSpellDefinition> list))
				{
					list = new List<XjJinDanDaoSpellDefinition>(2);
					staged[name] = list;
				}
				// CollectForDaoTu historically adds each catalog entry at most once.
				if (!list.Contains(candidate)) list.Add(candidate);
			}
		}

		Dictionary<string, XjJinDanDaoSpellDefinition[]> result =
			new Dictionary<string, XjJinDanDaoSpellDefinition[]>(staged.Count, StringComparer.Ordinal);
		foreach (KeyValuePair<string, List<XjJinDanDaoSpellDefinition>> pair in staged)
		{
			result[pair.Key] = pair.Value.ToArray();
		}
		return result;
	}

	private static XjJinDanDaoSpellDefinition[] BuildCommonFallback()
	{
		for (int i = 0; i < All.Count; i++)
		{
			if (string.Equals(All[i].Id, "CommonLeiFa", StringComparison.Ordinal))
			{
				return new[] { All[i] };
			}
		}
		return EmptyDefinitions;
	}

	internal static int CollectForDaoTu(string daoTu, List<XjJinDanDaoSpellDefinition> buffer)
	{
		if (buffer == null) return 0;
		buffer.Clear();
		IReadOnlyList<XjJinDanDaoSpellDefinition> definitions = ReadForDaoTu(daoTu);
		for (int i = 0; i < definitions.Count; i++) buffer.Add(definitions[i]);
		return buffer.Count;
	}

	internal static bool TryResolveForDaoTu(string daoTu, out XjJinDanDaoSpellDefinition definition)
	{
		IReadOnlyList<XjJinDanDaoSpellDefinition> definitions = ReadForDaoTu(daoTu);
		if (definitions.Count > 0)
		{
			definition = definitions[0];
			return true;
		}

		definition = default;
		return false;
	}

	internal static bool TryGetById(string id, out XjJinDanDaoSpellDefinition definition)
	{
		for (int i = 0; i < All.Count; i++)
		{
			XjJinDanDaoSpellDefinition candidate = All[i];
			if (string.Equals(candidate.Id, id, StringComparison.Ordinal))
			{
				definition = candidate;
				return true;
			}
		}

		definition = default;
		return false;
	}
}
