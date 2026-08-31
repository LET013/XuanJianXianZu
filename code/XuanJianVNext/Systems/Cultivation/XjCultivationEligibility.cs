using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.YaoShu;

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

	/// <summary>
	/// 登名石等“普通玄鉴身份恢复”入口使用的物种门禁。
	/// 只允许原生人类、精灵、矮人、兽人；手动授予与其他文明化种族都不能绕过。
	/// 龙属只由自身专属系统生成和维护，不走登名石/手动补录入口。
	/// </summary>
	internal static bool CanReceiveXuanJianContent(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}
		if (XjYaoShuGreatSageSystem.IsGreatSage(actor) || XjYaoShuSapientSpecies.IsYaoMin(actor)) return true;
		if (IsHardBlockedSpecies(actor)) return false;

		// 玄鉴修炼身份只允许原生人类、精灵、矮人、兽人承载。
		// 登名石恢复与玩家手动补录也不能绕过这一物种硬边界。
		return IsSupportedNativeCultivationSpecies(actor);
	}

	internal static bool CanCultivate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjTrueDamageSystem.IsJinXingYaoXie(actor))
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
		// 大圣是受管高境例外；妖民则只扩展物种准入，后续家族、城镇与宗门完全沿用原生链路。
		if (XjYaoShuGreatSageSystem.IsGreatSage(actor) || XjYaoShuSapientSpecies.IsYaoMin(actor)) return true;
		if (IsHardBlockedSpecies(actor)) return false;

		if (!IsSupportedNativeCultivationSpecies(actor)) return false;
		// 手动授予仍可让四族角色在无城市/国家上下文时进入修炼（例如玩家直接放置的角色），
		// 但再也不能用来绕过物种白名单。
		return HasManualCultivationGrant(actor) || IsEligibleSapientActor(actor, includeLongShu: false);
	}

	/// <summary>
	/// 龙属不属于普通四族自然入道入口，但它是玄鉴自身生成并维护的独立高境种族。
	/// 这里只允许已经具备龙属身份的实体继续走自身紫府→求金生命周期，不能被手动赋予给其他种族。
	/// </summary>
	internal static bool CanRunManagedLongShuCultivation(Actor actor)
	{
		return XjRuntimeSettings.CultivationEnabled
			&& actor?.data != null
			&& actor.isAlive()
			&& XjLongShuSystem.IsLongShu(actor)
			&& !XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	internal static bool CanRunManagedYaoShuGreatSageCultivation(Actor actor)
	{
		return XjRuntimeSettings.CultivationEnabled
			&& actor?.data != null
			&& actor.isAlive()
			&& XjYaoShuGreatSageSystem.IsGreatSage(actor)
			&& !XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	internal static bool IsCultivationHardBlocked(Actor actor)
	{
		return IsHardBlockedSpecies(actor) || XjTrueDamageSystem.IsJinXingYaoXie(actor);
	}

	/// <summary>
	/// 旧档迁移门禁。凡物种不属于原生四族而又携带普通玄鉴修炼状态者，
	/// 一律在统一对账边界退出修炼链路；玄鉴自身生成并标记的龙属属于独立受管例外。
	/// </summary>
	internal static bool ShouldClearUnsupportedCultivationState(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		// 旧档、手动授予、转世恢复都遵守同一四族硬白名单；
		// 只有玄鉴自身生成并维护的龙属保留专属紫府→求金生命周期。
		if (XjLongShuSystem.IsLongShu(actor)
			|| XjYaoShuGreatSageSystem.IsGreatSage(actor)
			|| XjYaoShuSapientSpecies.IsYaoMin(actor)) return false;
		return !IsSupportedNativeCultivationSpecies(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| IsHardBlockedSpecies(actor);
	}

	/// <summary>
	/// 手动授予只能唤醒原生四族的修炼资格；不能把其他物种强行写入修炼链路。
	/// </summary>
	internal static void RecordManualCultivationGrant(Actor actor)
	{
		if (actor?.data != null
			&& actor.isAlive()
			&& IsSupportedNativeCultivationSpecies(actor)
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

	/// <summary>
	/// 修炼链路的最终物种硬门槛。任何自然入道、手动补录、登名石恢复、
	/// 转世或旧档迁移都必须经过这里；仅允许原生人类、精灵、矮人、兽人。
	/// </summary>
	internal static bool IsSupportedNativeCultivationSpecies(Actor actor)
	{
		if (actor?.data == null || actor.asset == null) return false;
		if (IsEligibleAsset(actor.asset)) return true;

		// 少数原生/兼容层会让 actor.asset.id 与 data.asset_id 形式不同，
		// 但只接受同一四族的显式 ID，不接受文明化自定义种族。
		string dataAssetId = NormalizeSpeciesId(actor.data.asset_id);
		return !string.IsNullOrWhiteSpace(dataAssetId)
			&& NativeCultivationSpecies.Contains(dataAssetId);
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

		if (XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}
		if (XjYaoShuSapientSpecies.IsYaoMin(actor)) return true;
		if (IsHardBlockedSpecies(actor)) return false;

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

		return XjCultivationPathRules.IsShi(actor)
			|| (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
				&& !string.IsNullOrWhiteSpace(shiRealm))
			|| (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
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
