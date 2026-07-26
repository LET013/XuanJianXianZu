using System;
using HarmonyLib;
using UnityEngine;

namespace XuanJianVNext.Core;

internal static class XjSafeCore
{
	internal const string Accuracy = "Accuracy";
	internal const string Dodge = "Dodge";
	internal const string Vampire = "Vampire";
	internal const string Resist = "Resist";
	internal const string ArmorPenPercent = "ArmorPenPercent";
	internal const string Healback = "Healback";
	internal const string DamageReduce = "DamageReduce";
	internal const string CritChance = "XjCritChance";
	internal const string CritTakenReduction = "XjCritTakenReduction";
	internal const string ShieldRatio = "XjShieldRatio";
	internal const string ShieldBreak = "XjShieldBreak";
	internal const string SameRealmDamage = "XjSameRealmDamage";
	internal const string TrueDamageRatio = "XjTrueDamageRatio";

	internal const float DamageNormalizeMax = 210000000f;
	internal const float HealthNormalizeMax = 1000000000f;

	private static bool _initialized;

	public static void Init()
	{
		if (_initialized)
		{
			return;
		}

		_initialized = true;
		XjSafeStatsRegistration.Init();
	}

	internal static float GetStatSafe(Actor actor, string id, float defaultValue = 0f)
	{
		if (actor == null || string.IsNullOrWhiteSpace(id))
		{
			return defaultValue;
		}

		try
		{
			return actor.stats?[id] ?? defaultValue;
		}
		catch
		{
			return defaultValue;
		}
	}

	internal static float GetStatSafe(BaseSimObject obj, string id, float defaultValue = 0f)
	{
		Actor actor = obj?.a;
		return actor == null ? defaultValue : GetStatSafe(actor, id, defaultValue);
	}

	internal static float GetHealthSafe(Actor actor, float defaultValue = 0f)
	{
		try
		{
			return actor?.data == null ? defaultValue : actor.data.health;
		}
		catch
		{
			return defaultValue;
		}
	}

	internal static float GetMaxHealthSafe(Actor actor, float defaultValue = 0f)
	{
		try
		{
			return actor == null ? defaultValue : actor.getMaxHealth();
		}
		catch
		{
			return defaultValue;
		}
	}

	internal static bool IsAliveActor(Actor actor)
	{
		try
		{
			return actor != null && ((NanoObject)actor).isAlive() && actor.data != null;
		}
		catch
		{
			return false;
		}
	}

	internal static void HealActorSafe(Actor actor, float amount)
	{
		if (!IsAliveActor(actor) || amount <= 0f)
		{
			return;
		}

		try
		{
			float maxHealth = GetMaxHealthSafe(actor);
			if (maxHealth > 0f && GetHealthSafe(actor) >= maxHealth)
			{
				return;
			}

			int healAmount = Mathf.Max(1, Mathf.FloorToInt(amount));
			actor.changeHealth(healAmount);
		}
		catch
		{
		}
	}

	internal static bool IsLightningImmortalTraitBlocked(string traitId)
	{
		if (!string.Equals(traitId, "immortal", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string stack = Environment.StackTrace ?? string.Empty;
		return stack.IndexOf("MapAction", StringComparison.OrdinalIgnoreCase) >= 0
			&& stack.IndexOf("checkLightningAction", StringComparison.OrdinalIgnoreCase) >= 0;
	}
}

internal static class XjSafeStatsRegistration
{
	internal static void Init()
	{
		TryUpsertBaseStat("damage", 1f, XjSafeCore.DamageNormalizeMax, false);
		TryUpsertBaseStat("health", 0f, XjSafeCore.HealthNormalizeMax, false);
		TryUpsertBaseStat(XjSafeCore.Accuracy, 0f, 99999f, true);
		TryUpsertBaseStat(XjSafeCore.Dodge, 0f, 99999f, true);
		TryUpsertBaseStat(XjSafeCore.Vampire, 0f, 100f, true);
		TryUpsertBaseStat(XjSafeCore.Resist, 0f, 99999f, false);
		TryUpsertBaseStat(XjSafeCore.ArmorPenPercent, 0f, 100f, true);
		TryUpsertBaseStat(XjSafeCore.Healback, 0f, 2f, true);
		TryUpsertBaseStat(XjSafeCore.DamageReduce, 0f, 90f, true);
		TryUpsertBaseStat(XjSafeCore.CritChance, 0f, 100f, true);
		TryUpsertBaseStat(XjSafeCore.CritTakenReduction, 0f, 100f, true);
		TryUpsertBaseStat(XjSafeCore.ShieldRatio, 0f, 90f, true);
		TryUpsertBaseStat(XjSafeCore.ShieldBreak, 0f, 90f, true);
		TryUpsertBaseStat(XjSafeCore.SameRealmDamage, 0f, 100f, true);
		TryUpsertBaseStat(XjSafeCore.TrueDamageRatio, 0f, 50f, true);
	}

	private static void TryUpsertBaseStat(string id, float normalizeMin, float normalizeMax, bool showAsPercents)
	{
		if (string.IsNullOrWhiteSpace(id) || AssetManager.base_stats_library == null)
		{
			return;
		}

		try
		{
			BaseStatAsset asset = AssetManager.base_stats_library.get(id);
			if (asset == null)
			{
				asset = new BaseStatAsset();
				((Asset)asset).id = id;
				((AssetLibrary<BaseStatAsset>)(object)AssetManager.base_stats_library).add(asset);
			}

			asset.normalize = true;
			asset.normalize_min = normalizeMin;
			asset.normalize_max = normalizeMax;
			asset.show_as_percents = showAsPercents;
			asset.used_only_for_civs = false;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][SafeCore] base stat 注册跳过 id=" + id + " ex=" + ex.GetType().Name);
		}
	}
}
