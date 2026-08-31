using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Rank;
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.Traits;
using XuanJianVNext.UI.ActorInfo;

namespace XuanJianVNext.Patches;

/// <summary>
/// 妖属大圣与原生 Actor/UI 的收口兼容层。
/// 这里只修正固定十二席的大圣，不把妖属扩散成新的常驻全图扫描系统。
/// </summary>
internal partial class XjVNextPatches
{
	[ThreadStatic] private static int _yaoSageDebugManifestScopeDepth;

	private sealed class YaoSageExtraVisualFrames
	{
		internal Sprite[] Idle = Array.Empty<Sprite>();
		internal Sprite[] Walk = Array.Empty<Sprite>();
		internal Sprite[] Run = Array.Empty<Sprite>();
		internal Sprite[] Breathing = Array.Empty<Sprite>();
	}

	private static readonly Dictionary<string, YaoSageExtraVisualFrames> YaoSageExtraFramesByAssetId =
		new Dictionary<string, YaoSageExtraVisualFrames>(StringComparer.Ordinal);

	private static readonly FieldInfo YaoSageAttackAnimationStartedAtField =
		AccessTools.Field(typeof(XjYaoShuGreatSageSystem), "AttackAnimationStartedAt");

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryDebugManifestAll")]
	[HarmonyPrefix]
	private static void XuanJian_YaoShuGreatSage_DebugManifestAll_Scope_Prefix()
	{
		_yaoSageDebugManifestScopeDepth++;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryDebugManifestAll")]
	[HarmonyFinalizer]
	private static Exception XuanJian_YaoShuGreatSage_DebugManifestAll_Scope_Finalizer(Exception __exception)
	{
		if (_yaoSageDebugManifestScopeDepth > 0) _yaoSageDebugManifestScopeDepth--;
		return __exception;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "BuildOccupiedFruitSet")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_DebugManifestAll_IgnoreNormalFruit_Postfix(ref HashSet<string> __result)
	{
		if (_yaoSageDebugManifestScopeDepth <= 0 || __result == null) return;
		__result.Clear();
	}

	[HarmonyPatch(typeof(XjFruitPositionWorldState), "IsDaoTaiSecondaryOccupied")]
	[HarmonyPrefix]
	private static bool XuanJian_YaoShuGreatSage_DebugManifestAll_IgnoreDaoTaiSecondary_Prefix(ref bool __result)
	{
		if (_yaoSageDebugManifestScopeDepth <= 0) return true;
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(XjGuoWeiRegistry), "IsPermanentlyLockedGuoWei")]
	[HarmonyPrefix]
	private static bool XuanJian_YaoShuGreatSage_DebugManifestAll_IgnorePermanentLock_Prefix(ref bool __result)
	{
		if (_yaoSageDebugManifestScopeDepth <= 0) return true;
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "CanAttemptManifestation")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_DebugManifestAll_ForceCatalogCondition_Postfix(ref bool __result)
	{
		if (_yaoSageDebugManifestScopeDepth > 0) __result = true;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryDebugManifestAll")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_DebugManifestAll_ReportLivingTotal_Postfix(ref int __result)
	{
		IReadOnlyList<XjYaoShuGreatSageSystem.CodexItem> items = XjYaoShuGreatSageSystem.BuildCodexItems();
		int living = 0;
		for (int i = 0; i < items.Count; i++) if (items[i].Alive) living++;
		__result = living;
	}

	/// <summary>
	/// 大圣只是果位象生之妖，不是修士果位持有人；不可反向锁死普通正位。
	/// </summary>
	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "IsExternalPositionOccupied")]
	[HarmonyPrefix]
	private static bool XuanJian_YaoShuGreatSage_DoesNotOccupyCultivatorFruit_Prefix(ref bool __result)
	{
		__result = false;
		return false;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "EnsureGreatSageIdentity")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_CompleteAptitudeAndRank_Postfix(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjYaoShuGreatSageSystem.IsGreatSage(actor)) return;

		bool changed = false;
		long actorId = ((BaseSystemData)actor.data).id;
		string seedName = actor.getName() ?? actor.data.asset_id ?? string.Empty;

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float storedDaoHui) || storedDaoHui <= 0f)
		{
			float daoHui = XjDeterministicHash.BuildSeedInteger(
				actorId, seedName, 23061 + Math.Max(0, currentYear) % 997, 88, 100);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, daoHui);
			changed = true;
		}
		else
		{
			XjDaoHuiPolicy.NormalizeStoredValue(actor);
		}

		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float totalMingShu) || totalMingShu <= 0f)
		{
			float congenital = XjDeterministicHash.BuildSeedInteger(actorId, seedName, 23062, 78, 100);
			float acquired = XjDeterministicHash.BuildSeedInteger(actorId, seedName, 23063, 24, 60);
			XjMingShuState.Set(actor, congenital, acquired);
			changed = true;
		}
		else
		{
			XjMingShuState.Normalize(actor);
		}

		XjNativeActorDataSanitizerInterop.NormalizeLegacyJsonTokenValues(actor.data);
		YaoSageHardenActorAsset(actor.asset);
		if (changed)
		{
			XjRankReadModel.InvalidateCache();
			XjActorInfoPanelRenderer.Invalidate();
		}
	}

	[HarmonyPatch(typeof(XjActorInfoPanelRenderer), "ShouldDisplayFor")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_ForceXuanJianRecordPanel_Postfix(Actor actor, ref bool __result)
	{
		if (!__result && XjYaoShuGreatSageSystem.IsGreatSage(actor)) __result = true;
	}

	[HarmonyPatch(typeof(XjActorInfoDisplayFormatter), "Format", new Type[] { typeof(Actor) })]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_AppendRecordDetails_Postfix(Actor actor, ref string __result)
	{
		if (actor?.data == null || !XjYaoShuGreatSageSystem.IsGreatSage(actor)) return;
		if ((__result ?? string.Empty).IndexOf("◆ 大圣位格", StringComparison.Ordinal) >= 0) return;

		float daoHui = XjDaoHuiPolicy.Read(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
		XjYaoShuGreatSageSystem.TryResolveGreatSageFruit(actor, out string fruit);

		string section =
			"\n\n<color=#B9EEE4><b>◆ 大圣位格</b></color>\n"
			+ "<color=#D8CDAA>道慧</color>　<color=#E6EDF2>" + Mathf.FloorToInt(daoHui).ToString(CultureInfo.InvariantCulture) + "/100</color>\n"
			+ "<color=#D8CDAA>命数</color>　<color=#E6EDF2>" + Mathf.FloorToInt(Mathf.Max(0f, mingShu)).ToString(CultureInfo.InvariantCulture) + "</color>\n"
			+ "<color=#D8CDAA>映照果位</color>　<color=#E6EDF2>" + ((fruit ?? string.Empty).Trim().Length == 0 ? "未显" : fruit.Trim()) + "</color>\n"
			+ "<color=#9CD7FF>果位象生，不占修士正常位序</color>";

		__result = (__result ?? string.Empty).TrimEnd() + section;
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "NormalizeGreatSageSubspecies")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_SubspeciesHoverAndIcon_Postfix(Subspecies subspecies)
	{
		if (subspecies == null || subspecies.isRekt()) return;

		bool changed = false;
		if (!subspecies.hasTrait("hovering") && AssetManager.subspecies_traits?.get("hovering") != null)
		{
			subspecies.addTrait("hovering", true);
			changed = true;
		}

		YaoSageApplyIcon(subspecies);
		if (changed) subspecies.forceRecalcBaseStats();
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "ApplyGreatSageDefaultSubspeciesTraits")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_DefaultSubspeciesHover_Postfix(ActorAsset asset)
	{
		if (asset == null || AssetManager.subspecies_traits?.get("hovering") == null) return;
		if (asset.default_subspecies_traits == null)
		{
			asset.default_subspecies_traits = new List<string>();
		}
		if (!asset.default_subspecies_traits.Contains("hovering"))
		{
			asset.addSubspeciesTrait("hovering");
		}
		YaoSageHardenActorAsset(asset);
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryConfigureActorVisuals")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_PreloadFullAnimations_Postfix(ActorAsset asset, object[] __args)
	{
		if (asset == null || string.IsNullOrWhiteSpace(asset.id)) return;
		object definition = __args != null && __args.Length >= 3 ? __args[2] : null;
		string visualPrefix = XjNativeReflectionInterop.ReadMemberValue(definition, "VisualPrefix") as string;
		if (string.IsNullOrWhiteSpace(visualPrefix)) return;

		var frames = new YaoSageExtraVisualFrames
		{
			Idle = YaoSageLoadVisualSequence(asset.id, visualPrefix, "idle", allowUnprefixedFallback: true),
			Walk = YaoSageLoadVisualSequence(asset.id, visualPrefix, "walk", allowUnprefixedFallback: true),
			Run = YaoSageLoadVisualSequence(asset.id, visualPrefix, "run", allowUnprefixedFallback: false),
			Breathing = YaoSageLoadVisualSequence(asset.id, visualPrefix, "breathing", allowUnprefixedFallback: false)
		};
		YaoSageExtraFramesByAssetId[asset.id] = frames;

		YaoSageHardenActorAsset(asset);
	}

	[HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "GetGreatSageOverrideSprite")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_SelectFullAnimationState_Postfix(Actor actor, ref Sprite __result)
	{
		if (actor?.data == null || !XjYaoShuGreatSageSystem.IsGreatSage(actor)) return;
		if (YaoSageIsInAttackAnimation(actor)) return;

		string assetId = actor.data.asset_id ?? string.Empty;
		if (!YaoSageExtraFramesByAssetId.TryGetValue(assetId, out YaoSageExtraVisualFrames frames) || frames == null) return;

		long actorId = ((BaseSystemData)actor.data).id;
		bool moving = YaoSageIsMoving(actor, out int targetDistance);
		Sprite[] sequence;
		int divisor;
		if (moving)
		{
			sequence = targetDistance >= 3 && YaoSageHasFrames(frames.Run)
				? frames.Run
				: (YaoSageHasFrames(frames.Walk) ? frames.Walk : frames.Run);
			divisor = 6;
		}
		else
		{
			int phase = Math.Abs((Time.frameCount / 90) + (int)(actorId % 3L)) % 3;
			sequence = phase != 0 && YaoSageHasFrames(frames.Breathing)
				? frames.Breathing
				: (YaoSageHasFrames(frames.Idle) ? frames.Idle : frames.Breathing);
			divisor = 11;
		}

		if (!YaoSageHasFrames(sequence)) return;
		int offset = (int)(Math.Abs(actorId) % sequence.Length);
		int index = Math.Abs(Time.frameCount / Math.Max(1, divisor) + offset) % sequence.Length;
		Sprite selected = sequence[index];
		if (selected != null) __result = selected;
	}

	[HarmonyPatch(typeof(XjVNextAssetRegistration), "EnsureRuntimeLocalization")]
	[HarmonyPostfix]
	private static void XuanJian_YaoShuGreatSage_RefreshLocalization_Postfix()
	{
		YaoSageOverwriteLocale("trait_XjYaoShuGreatSage", "妖属大圣");
		YaoSageOverwriteLocale("trait_XjYaoShuGreatSage_info",
			"受天数垂顾，寄身鳞羽毛角之间；映照一道果位，餐炁养形，俟其道胎。此映照不占修士正常果位席次。");
		YaoSageOverwriteLocale("trait_XjYaoShuGreatSage_info_2", "果位映照·真君羽士");
		YaoSageOverwriteLocale("xj_yao_shu_great_sage", "妖属大圣");
		YaoSageOverwriteLocale("xj_yao_shu_great_sage_info",
			"受天数垂顾，寄身鳞羽毛角之间；映照一道果位，餐炁养形，俟其道胎。此映照不占修士正常果位席次。");
		YaoSageOverwriteLocale("xj_yao_shu_great_sage_info_2", "果位映照·真君羽士");
	}

	private static void YaoSageHardenActorAsset(ActorAsset asset)
	{
		if (asset == null) return;
		try
		{
			asset.has_advanced_textures = false;
			asset.need_colored_sprite = false;
			asset.inspect_show_species = false;
			asset.can_be_inspected = true;
			asset.show_icon_inspect_window = true;
			asset.check_flip = delegate { return true; };
			asset.icon = "trait/XjYaoShuGreatSage";
			asset.show_icon_inspect_window_id = "trait/XjYaoShuGreatSage";
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.AssetHarden", ex);
		}
	}

	private static void YaoSageApplyIcon(Subspecies subspecies)
	{
		const string fallbackIcon = "trait/XjYaoShuGreatSage";
		string iconPath = fallbackIcon;
		try
		{
			object trait = AssetManager.traits?.get(XjYaoShuGreatSageSystem.GreatSageTraitId);
			string reflected = XjNativeReflectionInterop.ReadMemberValue(trait, "path_icon") as string;
			if (!string.IsNullOrWhiteSpace(reflected)) iconPath = reflected;

			XjNativeReflectionInterop.TryWriteMemberValue(subspecies, "icon", iconPath);
			XjNativeReflectionInterop.TryWriteMemberValue(subspecies, "icon_id", iconPath);
			XjNativeReflectionInterop.TryWriteMemberValue(subspecies, "path_icon", iconPath);
			if (subspecies.data != null)
			{
				XjNativeReflectionInterop.TryWriteMemberValue(subspecies.data, "icon", iconPath);
				XjNativeReflectionInterop.TryWriteMemberValue(subspecies.data, "icon_id", iconPath);
				XjNativeReflectionInterop.TryWriteMemberValue(subspecies.data, "path_icon", iconPath);
			}
			YaoSageHardenActorAsset(subspecies.getActorAsset());
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.SubspeciesIcon", ex);
		}
	}

	private static Sprite[] YaoSageLoadVisualSequence(string actorAssetId, string visualPrefix, string state, bool allowUnprefixedFallback)
	{
		var result = new List<Sprite>(32);
		for (int i = 0; i < 64; i++)
		{
			string path = "actors/species/other/" + actorAssetId + "/main/"
				+ visualPrefix + "_" + state + "_" + i.ToString("D3", CultureInfo.InvariantCulture);
			Sprite sprite = YaoSageTryLoadSprite(path);
			if (sprite == null)
			{
				if (i == 0) break;
				break;
			}
			result.Add(sprite);
		}

		if (result.Count == 0 && allowUnprefixedFallback)
		{
			for (int i = 0; i < 16; i++)
			{
				string path = "actors/species/other/" + actorAssetId + "/main/"
					+ state + "_" + i.ToString(CultureInfo.InvariantCulture);
				Sprite sprite = YaoSageTryLoadSprite(path);
				if (sprite == null) break;
				result.Add(sprite);
			}
		}
		return result.ToArray();
	}

	private static Sprite YaoSageTryLoadSprite(string path)
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

	private static bool YaoSageIsInAttackAnimation(Actor actor)
	{
		if (actor?.data == null || YaoSageAttackAnimationStartedAtField == null) return false;
		try
		{
			var map = YaoSageAttackAnimationStartedAtField.GetValue(null) as Dictionary<long, float>;
			if (map == null) return false;
			long actorId = ((BaseSystemData)actor.data).id;
			return map.TryGetValue(actorId, out float startedAt)
				&& Time.time >= startedAt
				&& Time.time - startedAt <= 1.25f;
		}
		catch
		{
			return false;
		}
	}

	private static bool YaoSageIsMoving(Actor actor, out int targetDistance)
	{
		targetDistance = 0;
		if (actor == null) return false;
		try
		{
			WorldTile current = actor.current_tile;
			WorldTile target = XjNativeReflectionInterop.ReadMemberValue(actor, "tileTarget") as WorldTile;
			if (current == null || target == null || current == target) return false;
			targetDistance = Math.Max(
				Math.Abs(current.pos.x - target.pos.x),
				Math.Abs(current.pos.y - target.pos.y));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool YaoSageHasFrames(Sprite[] frames)
	{
		if (frames == null || frames.Length == 0) return false;
		for (int i = 0; i < frames.Length; i++) if (frames[i] != null) return true;
		return false;
	}

	private static void YaoSageOverwriteLocale(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
		try
		{
			LocalizedTextManager.add(key, value, false, string.Empty, true);
			LocalizedTextManager manager = LocalizedTextManager.instance;
			if (manager?._localized_text != null) manager._localized_text[key] = value;
			if (manager?._localized_text_files != null) manager._localized_text_files[key] = "xuanjian.runtime";
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("YaoShuGreatSage.Locale." + key, ex);
		}
	}
}
