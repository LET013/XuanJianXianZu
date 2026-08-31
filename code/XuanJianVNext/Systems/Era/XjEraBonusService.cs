using System;
using XuanJianVNext.Data.Era;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Era;

internal readonly struct XjEraBonusProfile
{
	internal XjEraBonusProfile(
		string eraId,
		string displayName,
		string effectText,
		float attackBonus = 0f,
		float healthBonus = 0f,
		float movementSpeedBonus = 0f,
		float attackSpeedBonus = 0f,
		float dodgeBonus = 0f,
		float healbackBonus = 0f,
		float critBonus = 0f,
		float armorPenetration = 0f,
		float damageReduction = 0f,
		float cultivationBonus = 0.1f)
	{
		EraId = eraId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		EffectText = effectText ?? string.Empty;
		AttackBonus = Math.Max(0f, attackBonus);
		HealthBonus = Math.Max(0f, healthBonus);
		MovementSpeedBonus = Math.Max(0f, movementSpeedBonus);
		AttackSpeedBonus = Math.Max(0f, attackSpeedBonus);
		DodgeBonus = Math.Max(0f, dodgeBonus);
		HealbackBonus = Math.Max(0f, healbackBonus);
		CritBonus = Math.Max(0f, critBonus);
		ArmorPenetration = Math.Max(0f, armorPenetration);
		DamageReduction = Math.Max(0f, damageReduction);
		CultivationBonus = Math.Max(0f, cultivationBonus);
	}

	internal string EraId { get; }
	internal string DisplayName { get; }
	internal string EffectText { get; }
	internal float AttackBonus { get; }
	internal float HealthBonus { get; }
	internal float MovementSpeedBonus { get; }
	internal float AttackSpeedBonus { get; }
	internal float DodgeBonus { get; }
	internal float HealbackBonus { get; }
	internal float CritBonus { get; }
	internal float ArmorPenetration { get; }
	internal float DamageReduction { get; }
	internal float CultivationBonus { get; }
}

/// <summary>
/// 纪元对对应道途的唯一规则源。UI、修炼速度与运行时属性均读取同一份配置，
/// 防止再次出现“文字写了加成、实际属性没有变化”的分叉。
/// </summary>
internal static class XjEraBonusService
{
	internal static bool TryGetProfileForDaoTu(string daoTu, out XjEraBonusProfile profile)
	{
		profile = default;
		if (!XjEraCatalog.TryResolveAgeIdForDaoTu(daoTu, out string eraId))
		{
			return false;
		}

		// 并古九途共用一个纪元，但不共用一份模板。每条道途只读取自身
		// 道势，UI、修炼倍率和运行时战斗属性始终来自同一个 profile。
		if (string.Equals(eraId, "age_binggu", StringComparison.Ordinal)
			&& TryGetBingGuProfile(daoTu, out profile))
		{
			return true;
		}

		profile = eraId switch
		{
			"age_jinde" => new XjEraBonusProfile(eraId, "金德纪元", "兵锋更盛，护身更坚，修持亦得纪元之助", attackBonus: 0.20f, damageReduction: 0.10f),
			"age_mude" => new XjEraBonusProfile(eraId, "木德纪元", "生机大盛，伤势自愈，修持亦得纪元之助", healthBonus: 0.30f, healbackBonus: 0.002f),
			"age_shuide" => new XjEraBonusProfile(eraId, "水德纪元", "身法更疾，趋避更灵，修持亦得纪元之助", movementSpeedBonus: 0.20f, dodgeBonus: 0.15f),
			"age_huode" => new XjEraBonusProfile(eraId, "火德纪元", "杀伐炽盛，锋芒更烈，修持亦得纪元之助", critBonus: 0.25f, attackBonus: 0.15f),
			"age_tude" => new XjEraBonusProfile(eraId, "土德纪元", "体魄厚重，护身尤坚，修持亦得纪元之助", damageReduction: 0.30f, healthBonus: 0.20f),
			"age_taiyin" => new XjEraBonusProfile(eraId, "太阴纪元", "身影幽捷，出手更疾，修持亦得纪元之助", dodgeBonus: 0.20f, attackSpeedBonus: 0.15f),
			"age_taiyang" => new XjEraBonusProfile(eraId, "太阳纪元", "兵锋更盛，护身更坚，修持亦得纪元之助", attackBonus: 0.20f, damageReduction: 0.10f),
			"age_qingqi" => new XjEraBonusProfile(eraId, "清炁纪元", "行炁更畅，生机绵长，修持亦得纪元之助", attackSpeedBonus: 0.20f, healbackBonus: 0.0015f),
			"age_tianlei" => new XjEraBonusProfile(eraId, "天雷纪元", "雷威更盛，破法尤锐，修持亦得纪元之助", attackBonus: 0.20f, armorPenetration: 0.20f),
			_ => default
		};
		return !string.IsNullOrWhiteSpace(profile.EraId);
	}

	private static bool TryGetBingGuProfile(string daoTu, out XjEraBonusProfile profile)
	{
		profile = default;
		if (!XjDaoTuCatalog.TryResolve(daoTu, out XjDaoTuDefinition definition)
			|| !definition.IsBingGu)
		{
			return false;
		}

		profile = definition.RootId switch
		{
			XjDaoTuRootIds.XiaoKui => new XjEraBonusProfile(
				"age_binggu", "并古纪元·鸺葵道势", "身影幽捷，出手更疾，修持亦得纪元之助",
				attackSpeedBonus: 0.15f, dodgeBonus: 0.20f),
			XjDaoTuRootIds.ShangWu => new XjEraBonusProfile(
				"age_binggu", "并古纪元·上巫道势", "杀伐与趋避并进，修持亦得纪元之助",
				attackBonus: 0.15f, dodgeBonus: 0.15f),
			XjDaoTuRootIds.YuZhen => new XjEraBonusProfile(
				"age_binggu", "并古纪元·玉真道势", "生机与护身并盛，修持亦得纪元之助",
				healthBonus: 0.25f, damageReduction: 0.15f),
			XjDaoTuRootIds.HengZhu => new XjEraBonusProfile(
				"age_binggu", "并古纪元·衡祝道势", "攻伐凶烈，更易一击定势，修持亦得纪元之助",
				attackBonus: 0.15f, critBonus: 0.20f),
			XjDaoTuRootIds.QingXuan => new XjEraBonusProfile(
				"age_binggu", "并古纪元·青宣道势", "根基稳厚，护持周全，修持亦得纪元之助",
				healthBonus: 0.20f, damageReduction: 0.20f),
			XjDaoTuRootIds.QuanDan => new XjEraBonusProfile(
				"age_binggu", "并古纪元·全丹道势", "生机充盈，伤势徐复，修持亦得纪元之助",
				healthBonus: 0.25f, healbackBonus: 0.002f),
			XjDaoTuRootIds.ZhiBo => new XjEraBonusProfile(
				"age_binggu", "并古纪元·执孛道势", "身法更疾，趋避更灵，修持亦得纪元之助",
				movementSpeedBonus: 0.20f, dodgeBonus: 0.15f),
			XjDaoTuRootIds.SiTian => new XjEraBonusProfile(
				"age_binggu", "并古纪元·司天道势", "出手迅捷，破法更利，修持亦得纪元之助",
				attackSpeedBonus: 0.15f, armorPenetration: 0.15f),
			XjDaoTuRootIds.DuWei => new XjEraBonusProfile(
				"age_binggu", "并古纪元·都卫道势", "护身坚实，体魄厚重，修持亦得纪元之助",
				healthBonus: 0.20f, damageReduction: 0.25f),
			_ => default
		};
		return !string.IsNullOrWhiteSpace(profile.EraId);
	}

	internal static bool TryGetActiveProfile(Actor actor, out XjEraBonusProfile profile)
	{
		profile = default;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !TryGetProfileForDaoTu(daoTu, out profile))
		{
			return false;
		}

		return string.Equals(GetCurrentEraId(), profile.EraId, StringComparison.Ordinal);
	}

	internal static bool IsActiveForDaoTu(string daoTu)
	{
		return TryGetProfileForDaoTu(daoTu, out XjEraBonusProfile profile)
			&& string.Equals(GetCurrentEraId(), profile.EraId, StringComparison.Ordinal);
	}

	internal static float GetCultivationMultiplier(Actor actor)
	{
		return TryGetActiveProfile(actor, out XjEraBonusProfile profile)
			? 1f + profile.CultivationBonus
			: 1f;
	}

	internal static string GetCurrentEraId()
	{
		try
		{
			return World.world?.era_manager?.getCurrentAge()?.id ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}
