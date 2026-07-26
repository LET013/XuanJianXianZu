using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjCultivationEligibility
{
	private static readonly HashSet<string> NativeCultivationSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"human",
		"elf",
		"dwarf",
		"orc",
		"unit_human",
		"unit_elf",
		"unit_dwarf",
		"unit_orc"
	};

	private static readonly HashSet<string> ExplicitlyBlockedSpecies = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"boat",
		"ship",
		"ghost",
		"skeleton",
		"skeleton_warrior",
		"zombie",
		"demon",
		"dragon",
		"robot",
		"ufo",
		"alien",
		"crab",
		"crabzilla",
		"necromancer"
	};

	private static readonly string[] BlockedSpeciesFragments =
	{
		"boat",
		"ship",
		"skeleton",
		"zombie",
		"ghost",
		"demon",
		"dragon",
		"robot",
		"ufo",
		"crabzilla"
	};

	private static readonly string[] AnimalSpeciesFragments =
	{
		"animal",
		"cat",
		"dog",
		"wolf",
		"bear",
		"rabbit",
		"sheep",
		"cow",
		"buffalo",
		"horse",
		"camel",
		"deer",
		"fox",
		"rat",
		"monkey",
		"chicken",
		"duck",
		"goose",
		"bird",
		"penguin",
		"turtle",
		"snake",
		"frog",
		"fish",
		"piranha",
		"crocodile",
		"rhino",
		"elephant",
		"hyena",
		"bison",
		"ant",
		"bee",
		"butterfly",
		"beetle",
		"grasshopper",
		"crab"
	};

	// 资质门控依赖的 actor asset 属性在运行期基本不变。
	private static readonly Dictionary<ActorAsset, bool> AssetEligibilityCache = new Dictionary<ActorAsset, bool>();

	internal static bool CanCultivate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || IsHardBlockedSpecies(actor) || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		if (!XjRuntimeSettings.CultivationEnabled)
		{
			return false;
		}

		if (!XjJianDaoCompatibility.ShouldAllowCultivation(actor))
		{
			return false;
		}

		return HasManualCultivationGrant(actor) || IsEligibleSapientActor(actor, includeLongShu: true);
	}

	internal static bool IsCultivationHardBlocked(Actor actor)
	{
		return IsHardBlockedSpecies(actor) || XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	/// <summary>
	/// 旧档迁移门禁。只清理“物种本身不受支持、且不是玩家手动授予”的
	/// 误入修炼者；不会因为离开城市、临时失去国家或关闭总开关而抹除
	/// 合法修士。龙属走独立白名单。
	/// </summary>
	internal static bool ShouldClearUnsupportedCultivationState(Actor actor)
	{
		if (actor?.data == null || HasManualCultivationGrant(actor) || XjLongShuSystem.IsLongShu(actor))
		{
			return false;
		}

		if (XjTrueDamageSystem.IsJinXingYaoXie(actor) || IsHardBlockedSpecies(actor))
		{
			return true;
		}

		return actor.asset == null || !IsEligibleAsset(actor.asset);
	}

	/// <summary>
	/// Manual trait grants are an explicit player decision. They may bypass the
	/// natural birth eligibility gate, but never the demon or JianDao exclusions.
	/// </summary>
	internal static void RecordManualCultivationGrant(Actor actor)
	{
		if (actor?.data != null
			&& actor.isAlive()
			&& !IsHardBlockedSpecies(actor)
			&& !XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ManualCultivationGrant, 1);
		}
	}

	/// <summary>
	/// 玩家显式授予的修炼资格是年度调度的唤醒标记。普通角色不应为了
	/// 检查这一项而逐年进入完整的资格、剑意兼容与特质链判断。
	/// </summary>
	internal static bool HasExplicitCultivationGrant(Actor actor)
	{
		return HasManualCultivationGrant(actor);
	}

	private static bool HasManualCultivationGrant(Actor actor)
	{
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ManualCultivationGrant, out int granted)
			&& granted > 0;
	}

	internal static bool CanEnterFamilyLedger(Actor actor)
	{
		return IsEligibleSapientActor(actor, includeLongShu: false);
	}

	private static bool IsEligibleSapientActor(Actor actor, bool includeLongShu)
	{
		if (actor?.data == null || !actor.isAlive() || actor.asset == null)
		{
			return false;
		}

		if (IsHardBlockedSpecies(actor) || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		if (XjLongShuSystem.IsLongShu(actor))
		{
			return includeLongShu;
		}

		return IsEligibleAsset(actor.asset)
			&& HasCivilizationContext(actor)
			&& actor.isSapient();
	}

	internal static bool HasCultivationMarkers(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
				&& !string.IsNullOrWhiteSpace(realmId))
			|| (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan)
				&& zhenYuan > 0f)
			|| HasCultivationAptitudeTrait(actor);
	}

	internal static bool HasCultivationAptitudeTrait(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			&& aptitude >= 1
			&& aptitude <= 6;
	}

	private static bool IsEligibleAsset(ActorAsset asset)
	{
		if (asset == null)
		{
			return false;
		}

		if (AssetEligibilityCache.TryGetValue(asset, out bool eligible))
		{
			return eligible;
		}

		string assetId = NormalizeSpeciesId(asset.id);
		// 自然修炼只对玄鉴明确支持的原生四族开放。不能再把
		// kingdom_id_civilization 当作通行证：其他模组会把蛇、动物或
		// 自定义灵族文明化，它们依然不应自动进入玄鉴修炼体系。
		eligible = !string.IsNullOrWhiteSpace(assetId)
			&& !asset.is_boat
			&& !asset.default_animal
			&& !IsExplicitlyNonSapient(assetId)
			&& !IsAnimalSpeciesId(assetId)
			&& NativeCultivationSpecies.Contains(assetId);
		AssetEligibilityCache[asset] = eligible;
		return eligible;
	}

	private static bool HasCivilizationContext(Actor actor)
	{
		return actor?.kingdom?.data != null || actor?.city?.data != null;
	}

	internal static void ClearRuntimeCache()
	{
		AssetEligibilityCache.Clear();
	}

	private static bool IsExplicitlyNonSapient(string assetId)
	{
		string normalized = NormalizeSpeciesId(assetId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		if (ExplicitlyBlockedSpecies.Contains(normalized))
		{
			return true;
		}

		for (int i = 0; i < BlockedSpeciesFragments.Length; i++)
		{
			if (normalized.IndexOf(BlockedSpeciesFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsAnimalSpeciesId(string assetId)
	{
		string normalized = NormalizeSpeciesId(assetId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		for (int i = 0; i < AnimalSpeciesFragments.Length; i++)
		{
			if (normalized.IndexOf(AnimalSpeciesFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsHardBlockedSpecies(Actor actor)
	{
		if (actor?.asset == null)
		{
			return true;
		}

		return actor.asset.is_boat
			|| actor.asset.default_animal
			|| IsExplicitlyNonSapient(actor.asset.id)
			|| IsExplicitlyNonSapient(actor.data?.asset_id)
			|| IsAnimalSpeciesId(actor.asset.id)
			|| IsAnimalSpeciesId(actor.data?.asset_id);
	}

	private static string NormalizeSpeciesId(string assetId)
	{
		string normalized = (assetId ?? string.Empty).Trim();
		if (normalized.StartsWith("civ_", StringComparison.OrdinalIgnoreCase))
		{
			normalized = normalized.Substring(4);
		}

		return normalized;
	}
}
