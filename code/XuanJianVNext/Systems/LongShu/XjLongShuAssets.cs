using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.LongShu;

internal static partial class XjLongShuSystem
{
	private const string ActorTemplateId = "civ_seal";
	private const string ActorLocaleKey = "xuanjian_race_longshu";
	private const string ActorIconId = "xj_iconLongShu";
	private const string ActorTextureId = "longshu";
	private const string ActorTexturePath = "actors/species/civs/longshu/main/";
	private const string ActorBannerSpritePath = "actors/species/civs/longshu/main/walk_0";
	private const string ActorColorHex = "#A6E9FF";

	private static readonly string[] LongShuSubspeciesTraitIds =
	{
		"amygdala",
		"wernicke_area",
		"cautious_instincts",
		"hyper_intelligence",
		"pure",
		"accelerated_healing",
		"aquatic",
		"cold_resistance",
		"enhanced_strength",
		"long_lifespan",
		"diet_omnivore",
		"photosynthetic_skin",
		"gift_of_water",
		"hovering"
	};

	private static readonly string[] LongShuForbiddenSubspeciesTraitIds =
	{
		"population_minimal",
		"population_small",
		"population_moderate",
		"population_large",
		"population_expansive",
		"advanced_hippocampus",
		"prefrontal_cortex",
		"heat_resistance",
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

	private static readonly string[] LongShuSubspeciesNamePrefixes =
	{
		"浮岚", "玄澜", "清溟", "沧汐", "镜海", "灵泽", "霁涛", "云渊",
		"星澜", "寒潮", "渌川", "瀚汐", "素波", "玄潋", "青溟", "琼澜"
	};

	private static bool _assetInitialized;
	private static long _sharedLongShuSubspeciesId = -1L;
	private static Subspecies _sharedLongShuSubspecies;

	private static void TryRegisterActorAsset()
	{
		ActorAssetLibrary library = AssetManager.actor_library;
		if (library == null)
		{
			return;
		}

		try
		{
			string templateId = ResolveActorTemplateId(library);
			ActorAsset asset = library.get(PrimaryActorAssetId) ?? library.clone(PrimaryActorAssetId, templateId);
			if (asset == null)
			{
				return;
			}

			asset.name_locale = ActorLocaleKey;
			asset.texture_id = ActorTextureId;
			asset.icon = ActorIconId;
			asset.show_icon_inspect_window = true;
			asset.show_icon_inspect_window_id = ActorIconId;
			asset.color_hex = ActorColorHex;
			asset.color = Toolbox.makeColor(ActorColorHex);
			asset.default_animal = false;
			asset.civ = false;
			asset.unit_other = true;
			asset.use_phenotypes = false;
			asset.has_advanced_textures = false;
			asset.has_baby_form = false;
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
			asset.can_be_inspected = true;
			// 原生野生单位会在镜头外显示敌对边缘指示器。龙属长期位于远海，
			// 开启小地图可见会把该指示器投影成世界边缘的红色龙属虚影。
			asset.visible_on_minimap = false;
			asset.can_edit_traits = true;
			asset.can_talk_with = false;
			asset.control_can_talk = false;
			asset.use_items = false;
			asset.take_items = false;
			asset.job = new[] { "random_move" };
			asset.civ_base_cities = 0;
			asset.kingdom_id_civilization = string.Empty;
			asset.kingdom_id_wild = "dragons";
			asset.actor_size = ActorSize.S17_Dragon;
			asset.animation_walk = new[]
			{
				"walk_0", "walk_1", "walk_2", "walk_3", "walk_4", "walk_5"
			};
			asset.animation_idle = asset.animation_walk;
			asset.animation_swim = new[]
			{
				"swim_0", "swim_1", "swim_2", "swim_3", "swim_4", "swim_5"
			};
			asset.animation_walk_speed = 5f;
			asset.animation_idle_speed = 5f;
			asset.animation_swim_speed = 5f;
			asset.animation_speed_based_on_walk_speed = false;
			asset.check_flip = delegate { return true; };
			asset.base_stats["birth_rate"] = 0f;
			asset.base_stats["lifespan"] = FixedLifespanYears;
			asset.base_stats["health"] = 650f;
			asset.base_stats["damage"] = 72f;
			asset.base_stats["armor"] = 14f;
			asset.base_stats["speed"] = 88f;
			asset.base_stats["attack_speed"] = 64f;
			asset.base_stats["mass"] = 80f;
			asset.base_stats["scale"] = 0.12f;
			asset.base_stats["area_of_effect"] = 2f;
			asset.base_stats["targets"] = 3f;
			ApplyLongShuShadowSize(asset);
			ApplyLongShuDefaultSubspeciesTraits(asset);
			asset.unlock(false);
			TryReloadActorTextures(library, asset);
			asset.get_override_sprite = GetLongShuOverrideSprite;
			asset.has_override_sprite = true;
			_assetInitialized = true;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴][龙属] ActorAsset 注册跳过: " + ex.GetType().Name);
		}
	}

	private static string ResolveActorTemplateId(ActorAssetLibrary library)
	{
		if (library == null)
		{
			return "$mob$";
		}

		if (library.get(ActorTemplateId) != null) return ActorTemplateId;
		if (library.get("human") != null) return "human";
		if (library.get("$mob$") != null) return "$mob$";
		return ActorTemplateId;
	}

	private static void ApplyLongShuShadowSize(ActorAsset asset)
	{
		if (asset == null)
		{
			return;
		}

		Vector2 shadowSize = new Vector2(0.12f, 0.07f);
		SetVector2Member(asset, "shadow_size", shadowSize);
		SetVector2Member(asset, "shadowSize", shadowSize);
		SetVector2Member(asset, "shadow_size_unit", shadowSize);
		SetVector2Member(asset, "shadow_size_default", shadowSize);
		SetVector2Member(asset, "shadow_size_baby", shadowSize);
		SetVector2Member(asset, "shadow_size_egg", shadowSize);
		SetVector2Member(asset, "baby_shadow_size", shadowSize);
		SetVector2Member(asset, "egg_shadow_size", shadowSize);
		SetVector2Member(asset, "babyShadowSize", shadowSize);
		SetVector2Member(asset, "eggShadowSize", shadowSize);
		asset.shadow = false;
	}

	private static void SetVector2Member(object target, string name, Vector2 value)
	{
		XjNativeReflectionInterop.TryWriteMemberValue(target, name, value);
	}

	private static void TryReloadActorTextures(ActorAssetLibrary library, ActorAsset asset)
	{
		try
		{
			asset.texture_asset = new ActorTextureSubAsset(ActorTexturePath, false);
			asset._cached_sprite = null;
			asset.cached_sprite = null;

			if (library != null)
			{
				XjNativeReflectionInterop.TryInvokeCompatible(
					library, "loadTexturesAndSprites", new object[] { asset }, out _, out _);
			}

			Sprite banner = SpriteTextureLoader.getSprite(ActorBannerSpritePath)
				?? Resources.Load<Sprite>("GameResources/" + ActorBannerSpritePath);
			if (banner != null)
			{
				asset._cached_sprite = banner;
				asset.cached_sprite = banner;
			}
			_requiresRenderTextureGuard = asset.texture_asset == null;
		}
		catch (Exception ex)
		{
			_requiresRenderTextureGuard = false;
			Debug.LogWarning("[玄鉴][龙属] 贴图动画加载跳过: " + ex.GetType().Name);
		}
	}

	private static Sprite GetLongShuOverrideSprite(Actor actor)
	{
		if (!IsLongShu(actor))
		{
			return null;
		}

		return TryGetLongShuFrameSprite(actor) ?? TryGetBannerSprite();
	}

	private static Sprite TryGetLongShuFrameSprite(Actor actor)
	{
		try
		{
			string prefix = IsMovingForLongShuSprite(actor) ? "swim" : "walk";
			int frameCount = 6;
			long actorId = GetActorId(actor);
			int index = Math.Abs((Time.frameCount / 10) + (int)(actorId % frameCount)) % frameCount;
			return SpriteTextureLoader.getSprite(ActorTexturePath + prefix + "_" + index);
		}
		catch
		{
			return null;
		}
	}

	private static bool IsMovingForLongShuSprite(Actor actor)
	{
		try
		{
			if (actor == null)
			{
				return false;
			}

			WorldTile current = GetCurrentTile(actor);
			WorldTile target = TryGetTileTarget(actor);
			return current != null && target != null && current != target;
		}
		catch
		{
			return false;
		}
	}

	private static WorldTile TryGetTileTarget(Actor actor)
	{
		return actor == null
			? null
			: XjNativeReflectionInterop.ReadMemberValue(actor, "tileTarget") as WorldTile;
	}

	internal static bool RequiresRenderTextureGuard => _requiresRenderTextureGuard;

	internal static bool IsLongShuSubspecies(Subspecies subspecies)
	{
		try
		{
			return subspecies != null
				&& string.Equals(subspecies.getActorAsset()?.id, PrimaryActorAssetId, StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	internal static Sprite TryGetBannerSprite()
	{
		try
		{
			return SpriteTextureLoader.getSprite(ActorBannerSpritePath)
				?? Resources.Load<Sprite>("GameResources/" + ActorBannerSpritePath);
		}
		catch
		{
			return null;
		}
	}

	internal static void NormalizeSubspecies(Subspecies subspecies)
	{
		if (!IsLongShuSubspecies(subspecies))
		{
			return;
		}

		bool changed = false;
		for (int i = 0; i < LongShuSubspeciesTraitIds.Length; i++)
		{
			string traitId = LongShuSubspeciesTraitIds[i];
			if (!subspecies.hasTrait(traitId) && AssetManager.subspecies_traits?.get(traitId) != null)
			{
				subspecies.addTrait(traitId, true);
				changed = true;
			}
		}

		for (int i = 0; i < LongShuForbiddenSubspeciesTraitIds.Length; i++)
		{
			string traitId = LongShuForbiddenSubspeciesTraitIds[i];
			if (subspecies.hasTrait(traitId))
			{
				subspecies.removeTrait(traitId);
				changed = true;
			}
		}

		if (subspecies.data != null && !string.Equals(subspecies.data.name, "龙属", StringComparison.Ordinal))
		{
			subspecies.data.name = "龙属";
			try { subspecies.data.custom_name = true; } catch (System.Exception xjCaught400) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuAssets.cs:400", xjCaught400); }
			changed = true;
		}

		if (changed)
		{
			subspecies.forceRecalcBaseStats();
		}
	}

	internal static void EnsureSharedLongShuSubspecies(Actor actor)
	{
		if (actor?.data == null || !IsLongShu(actor))
		{
			return;
		}

		try
		{
			Subspecies current = actor.subspecies;
			if (!IsLongShuSubspecies(current))
			{
				return;
			}

			NormalizeSubspecies(current);
			if (_sharedLongShuSubspecies == null || !IsLongShuSubspecies(_sharedLongShuSubspecies))
			{
				_sharedLongShuSubspecies = current;
				_sharedLongShuSubspeciesId = current.id;
				NormalizeSubspecies(_sharedLongShuSubspecies);
				SetActorSubspeciesId(actor, _sharedLongShuSubspeciesId);
				return;
			}

			if (current.id == _sharedLongShuSubspeciesId)
			{
				SetActorSubspeciesId(actor, _sharedLongShuSubspeciesId);
				return;
			}

			AssignActorSubspecies(actor, _sharedLongShuSubspecies);
			SetActorSubspeciesId(actor, _sharedLongShuSubspeciesId);
		}
		catch (System.Exception xjCaught444) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuAssets.cs:444", xjCaught444); }
	}

	private static void AssignActorSubspecies(Actor actor, Subspecies subspecies)
	{
		if (actor == null || subspecies == null) return;
		XjNativeReflectionInterop.TryWriteMemberValue(actor, "subspecies", subspecies);
	}

	private static void SetActorSubspeciesId(Actor actor, long subspeciesId)
	{
		if (actor?.data == null || subspeciesId <= 0L)
		{
			return;
		}

		try
		{
			actor.data.subspecies = subspeciesId;
		}
		catch (System.Exception xjCaught490) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/LongShu/XjLongShuAssets.cs:490", xjCaught490); }
	}

	private static void ApplyLongShuDefaultSubspeciesTraits(ActorAsset asset)
	{
		asset.default_subspecies_traits = new List<string>();
		for (int i = 0; i < LongShuSubspeciesTraitIds.Length; i++)
		{
			string traitId = LongShuSubspeciesTraitIds[i];
			if (AssetManager.subspecies_traits?.get(traitId) != null)
			{
				asset.addSubspeciesTrait(traitId);
			}
		}
	}

	private static bool ShouldReplacePlaceholderName(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return true;
		}

		string normalized = value.Trim();
		return string.Equals(normalized, "Name", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "City Name", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "Kingdom Name", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(normalized, "??", StringComparison.Ordinal)
			|| string.Equals(normalized, "???", StringComparison.Ordinal);
	}

	private static bool TryResolveActorAssetId(out string actorAssetId)
	{
		actorAssetId = string.Empty;
		if (AssetManager.actor_library?.get(PrimaryActorAssetId) == null)
		{
			TryRegisterActorAsset();
		}

		if (AssetManager.actor_library?.get(PrimaryActorAssetId) != null)
		{
			actorAssetId = PrimaryActorAssetId;
			return true;
		}

		return false;
	}
}
