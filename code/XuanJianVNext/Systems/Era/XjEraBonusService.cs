using System;
using XuanJianVNext.Data.Era;
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

		profile = eraId switch
		{
			"age_jinde" => new XjEraBonusProfile(eraId, "金德纪元", "攻击+20%  减伤+10%  修炼+10%", attackBonus: 0.20f, damageReduction: 0.10f),
			"age_mude" => new XjEraBonusProfile(eraId, "木德纪元", "生命+30%  每秒回血+0.2%  修炼+10%", healthBonus: 0.30f, healbackBonus: 0.002f),
			"age_shuide" => new XjEraBonusProfile(eraId, "水德纪元", "速度+20%  闪避+15%  修炼+10%", movementSpeedBonus: 0.20f, dodgeBonus: 0.15f),
			"age_huode" => new XjEraBonusProfile(eraId, "火德纪元", "暴击+25%  攻击+15%  修炼+10%", critBonus: 0.25f, attackBonus: 0.15f),
			"age_tude" => new XjEraBonusProfile(eraId, "土德纪元", "减伤+30%  生命+20%  修炼+10%", damageReduction: 0.30f, healthBonus: 0.20f),
			"age_taiyin" => new XjEraBonusProfile(eraId, "太阴纪元", "闪避+20%  攻速+15%  修炼+10%", dodgeBonus: 0.20f, attackSpeedBonus: 0.15f),
			"age_taiyang" => new XjEraBonusProfile(eraId, "太阳纪元", "攻击+20%  减伤+10%  修炼+10%", attackBonus: 0.20f, damageReduction: 0.10f),
			"age_qingqi" => new XjEraBonusProfile(eraId, "清炁纪元", "攻速+20%  每秒回血+0.15%  修炼+10%", attackSpeedBonus: 0.20f, healbackBonus: 0.0015f),
			"age_tianlei" => new XjEraBonusProfile(eraId, "天雷纪元", "攻击+20%  破防+20%  修炼+10%", attackBonus: 0.20f, armorPenetration: 0.20f),
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
