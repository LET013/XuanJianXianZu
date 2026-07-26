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
		float selfDodgeBonusPercent = 0f)
	{
		Id = id ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		Profile = profile ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		DaoTuNames = daoTuNames ?? Array.Empty<string>();
		CooldownSeconds = cooldownSeconds < 0f ? 0f : cooldownSeconds;
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
			24,
			10,
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
		new XjJinDanDaoSpellDefinition(
			"JinDeJianZhen",
			"金德剑阵",
			"JinDeJianZhen",
			"JinDe",
			new[] { "金德", "兑金", "逍金", "齐金", "库金", "庚金" },
			28f,
			44,
			0,
			1.45f,
			0.15f,
			0f,
			0f,
			5,
			"Limited",
			string.Empty,
			string.Empty,
			0f,
			"effects/Skills/JinDeJianZhen",
			0.34f,
			0.08f,
			1.1f,
			0f,
			0f,
			"MaxTargets=5;SwordArray"),
		new XjJinDanDaoSpellDefinition(
			"XuanQiFengZhen",
			"玄炁风阵",
			"XuanQiFengZhen",
			"ShiErQi",
			new[] { "十二炁", "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" },
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
			"MaxTargets=6;WindFormation"),
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
			"SelfBuff;DodgeBonus=90;LoopOverhead;VisualSize=2Actors",
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

	internal static int CollectForDaoTu(string daoTu, List<XjJinDanDaoSpellDefinition> buffer)
	{
		if (buffer == null)
		{
			return 0;
		}

		buffer.Clear();
		string normalized = (daoTu ?? string.Empty).Trim();
		for (int i = 0; i < All.Count; i++)
		{
			XjJinDanDaoSpellDefinition candidate = All[i];
			for (int j = 0; j < candidate.DaoTuNames.Count; j++)
			{
				if (!string.Equals(normalized, candidate.DaoTuNames[j], StringComparison.Ordinal))
				{
					continue;
				}

				buffer.Add(candidate);
				break;
			}
		}

		if (buffer.Count == 0 && TryGetById("CommonLeiFa", out XjJinDanDaoSpellDefinition common))
		{
			buffer.Add(common);
		}
		return buffer.Count;
	}

	internal static bool TryResolveForDaoTu(string daoTu, out XjJinDanDaoSpellDefinition definition)
	{
		List<XjJinDanDaoSpellDefinition> candidates = new List<XjJinDanDaoSpellDefinition>(2);
		if (CollectForDaoTu(daoTu, candidates) > 0)
		{
			definition = candidates[0];
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
