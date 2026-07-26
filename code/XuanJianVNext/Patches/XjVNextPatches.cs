using XuanJianVNext.Systems.Warehouse;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Visual;
using XuanJianVNext.Core;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.UI.QianKunDai;
using static XuanJianVNext.UI.Family.XjFamilyInheritanceTabPatches;
using ai;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "prepareForSave")]
	private static void XuanJian_Actor_PrepareForSave_ArmyReferenceGuard_Prefix(Actor __instance)
	{
		// 保存遍历中的最后一道逐角色边界。即使世界单位管理器漏收了某个角色，
		// 也会在 Actor.saveArmy 读取 NanoObject.id 前清理断裂军队引用。
		XjNativeArmySaveGuard.SanitizeActorArmyReference(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "saveKingdomCiv")]
	private static bool XuanJian_Actor_SaveKingdomCiv_BrokenReferenceGuard_Prefix(Actor __instance)
	{
		if (__instance?.data == null) return false;
		bool isCivilizationActor;
		try { isCivilizationActor = __instance.isKingdomCiv(); }
		catch { return true; }
		if (!isCivilizationActor || __instance.kingdom?.data != null) return true;
		if (XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(__instance)) return true;
		try
		{
			__instance.data.cityID = -1L;
			__instance.data.civ_kingdom_id = -1L;
			__instance.data.culture = -1L;
			__instance.data.clan = -1L;
			__instance.data.family = -1L;
		}
		catch { }
		// 单个损坏角色不应使整个自动保存失败；其文明归属按无归属保存。
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(Actor), "saveKingdomCiv")]
	private static Exception XuanJian_Actor_SaveKingdomCiv_BrokenReferenceGuard_Finalizer(Exception __exception)
	{
		return __exception is NullReferenceException ? null : __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.currentWorldToSavedMap))]
	private static void XuanJian_SaveManager_CurrentWorldToSavedMap_CommitArchive_Prefix()
	{
		XjScheduler.FlushPendingAnnualStatesForSave();
		XjNativeArmySaveGuard.SanitizeBeforeSave();
		XjXuanJianShenTongSpecials.PrepareYiDuiYingMirrorsForSave();
		XjGuoWeiQuanBingRegistry.PrepareLiveSnapshotsForSave();
		XjWorldArchiveSystem.CommitPendingBeforeWorldSnapshot();
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(Subspecies), "generateName")]
	private static void XuanJian_Subspecies_GenerateName_LongShu_Postfix(Subspecies __instance)
	{
		XjLongShuSystem.NormalizeSubspecies(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Subspecies), "getUnitSpriteForBanner")]
	private static bool XuanJian_Subspecies_GetUnitSpriteForBanner_LongShu_Prefix(Subspecies __instance, ref Sprite __result)
	{
		if (!XjLongShuSystem.IsLongShuSubspecies(__instance))
		{
			return true;
		}

		__result = XjLongShuSystem.TryGetBannerSprite();
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), nameof(Actor.isAllowedToLookForEnemies))]
	private static bool XuanJian_Actor_IsAllowedToLookForEnemies_NativeStateGuard_Prefix(Actor __instance, ref bool __result)
	{
		if (__instance?.data == null || __instance.asset == null || __instance.profession_asset == null)
		{
			__result = false;
			return false;
		}

		try
		{
			if (!__instance.isAnimal())
			{
				Kingdom kingdom = __instance.kingdom;
				if (kingdom?.data == null || kingdom.asset == null)
				{
					__result = false;
					return false;
				}
			}
		}
		catch
		{
			__result = false;
			return false;
		}

		return true;
	}


	private static int _xuanJianGetSpriteEmptyPathWarnCount;

	private static int _xuanJianGetSpriteNullKeyWarnCount;

	private static int _xuanJianGetSpriteListEmptyPathWarnCount;

	private static int _xuanJianGetSpriteListNullKeyWarnCount;

	private static int _xuanJianResourceLibraryNullKeyWarnCount;

	private static readonly object _xuanJianSpriteTraceLock = new object();

	private static readonly HashSet<string> _xuanJianSpriteTraceSourceKeys = new HashSet<string>();

	private const int XuanJianMaxSpriteTraceSources = 30;

	private static readonly FieldInfo SubspeciesHasMutationReskinField = AccessTools.Field(typeof(Subspecies), "has_mutation_reskin");

	private static readonly FieldInfo SubspeciesHasMutationReskinBackingField = AccessTools.Field(typeof(Subspecies), "<has_mutation_reskin>k__BackingField");

	private static readonly PropertyInfo SubspeciesHasMutationReskinProperty = AccessTools.Property(typeof(Subspecies), "has_mutation_reskin");
	private static MethodInfo _xuanJianCityShowStatRowMethod;
	private static MethodInfo _xuanJianKingdomShowStatRowMethod;

	private static int ActorManagerPrecalculateNullGuardLogCount = 0;
	private static int ActorManagerMetaNullGuardLogCount = 0;
	private static int SimObjectsZonesAddUnitNullGuardLogCount = 0;
	private static int ChunkObjectContainerAddActorNullGuardLogCount = 0;
	private static int UnitAvatarLoaderNullGuardLogCount = 0;



	private static object XuanJian_GetMetaNoneValue(Type type)
	{
		if (type.IsEnum)
		{
			return Enum.ToObject(type, 0);
		}
		if (type == typeof(long))
		{
			return 0L;
		}
		if (type == typeof(int))
		{
			return 0;
		}
		if (type == typeof(short))
		{
			return (short)0;
		}
		if (type == typeof(byte))
		{
			return (byte)0;
		}
		return XuanJian_GetDefaultValue(type);
	}

	private static object XuanJian_GetMetaIdValue(Type type)
	{
		if (type == typeof(long))
		{
			return -1L;
		}
		if (type == typeof(int))
		{
			return -1;
		}
		if (type == typeof(short))
		{
			return (short)(-1);
		}
		if (type.IsEnum)
		{
			return Enum.ToObject(type, 0);
		}
		return XuanJian_GetDefaultValue(type);
	}

	private static object XuanJian_GetDefaultParameterValue(ParameterInfo parameter)
	{
		if (parameter.HasDefaultValue)
		{
			return parameter.DefaultValue;
		}
		return XuanJian_GetDefaultValue(parameter.ParameterType);
	}

	private static object XuanJian_GetDefaultValue(Type type)
	{
		if (!type.IsValueType)
		{
			return null;
		}
		try
		{
			return Activator.CreateInstance(type);
		}
		catch
		{
			return null;
		}
	}

	private static bool GetSubspeciesHasMutationReskin(Subspecies subspecies)
	{
		if (subspecies == null)
		{
			return false;
		}
		try
		{
			return subspecies.has_mutation_reskin;
		}
		catch
		{
		}
		try
		{
			object value = (SubspeciesHasMutationReskinField != null) ? SubspeciesHasMutationReskinField.GetValue(subspecies) : ((SubspeciesHasMutationReskinBackingField != null) ? SubspeciesHasMutationReskinBackingField.GetValue(subspecies) : null);
			if (value is bool result)
			{
				return result;
			}
		}
		catch
		{
		}
		return false;
	}

	private static void SetSubspeciesHasMutationReskin(Subspecies subspecies, bool value)
	{
		if (subspecies == null)
		{
			return;
		}
		try
		{
			if (SubspeciesHasMutationReskinField != null)
			{
				SubspeciesHasMutationReskinField.SetValue(subspecies, value);
			}
			else if (SubspeciesHasMutationReskinBackingField != null)
			{
				SubspeciesHasMutationReskinBackingField.SetValue(subspecies, value);
			}
			else if (SubspeciesHasMutationReskinProperty != null && SubspeciesHasMutationReskinProperty.CanWrite)
			{
				SubspeciesHasMutationReskinProperty.SetValue(subspecies, value);
			}
		}
		catch
		{
		}
	}


	private static bool XuanJian_ShouldLogLimited(ref int counter)
	{
		int num = ++counter;
		if (num != 1 && num != 10)
		{
			return num % 100 == 0;
		}
		return true;
	}

	private static void XuanJian_LogEmptySpritePathSourceOnce(string site, string path, Exception exception = null)
	{
		try
		{
			string stack = Environment.StackTrace ?? string.Empty;
			string text = XuanJian_GetFirstExternalSpriteFrame(stack);
			if (string.IsNullOrEmpty(text))
			{
				text = "<unknown>";
			}
			string item = site + "|" + text;
			lock (_xuanJianSpriteTraceLock)
			{
				if (_xuanJianSpriteTraceSourceKeys.Count >= 30 || !_xuanJianSpriteTraceSourceKeys.Add(item))
				{
					return;
				}
			}
			string text2 = (string.IsNullOrWhiteSpace(path) ? "<empty>" : path.Trim());
			string text3 = XuanJian_BuildSpriteTraceStackPreview(stack, 8);
			string text4 = string.Empty;
			if (exception != null)
			{
				text4 = " | ex=" + exception.GetType().Name + ": " + exception.Message;
			}
			Debug.LogWarning("[玄鉴][SpriteTrace] 空路径来源捕获 site=" + site + " path=" + text2 + " first=" + text + text4 + "\n" + text3);
		}
		catch
		{
		}
	}

	private static string XuanJian_GetFirstExternalSpriteFrame(string stack)
	{
		if (string.IsNullOrEmpty(stack))
		{
			return string.Empty;
		}
		string[] array = stack.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim();
			if (text.StartsWith("at ", StringComparison.Ordinal) && text.IndexOf("XuanJianVNext.Patches.XjVNextPatches", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("System.", StringComparison.OrdinalIgnoreCase) < 0)
			{
				return text;
			}
		}
		return string.Empty;
	}

	private static string XuanJian_BuildSpriteTraceStackPreview(string stack, int maxFrames)
	{
		if (string.IsNullOrEmpty(stack) || maxFrames <= 0)
		{
			return "<no stack>";
		}
		StringBuilder stringBuilder = new StringBuilder();
		string[] array = stack.Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
		int num = 0;
		for (int i = 0; i < array.Length; i++)
		{
			if (num >= maxFrames)
			{
				break;
			}
			string text = array[i].Trim();
			if (text.StartsWith("at ", StringComparison.Ordinal) && text.IndexOf("XuanJian_LogEmptySpritePathSourceOnce", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("XuanJian_BuildSpriteTraceStackPreview", StringComparison.OrdinalIgnoreCase) < 0 && text.IndexOf("XuanJian_GetFirstExternalSpriteFrame", StringComparison.OrdinalIgnoreCase) < 0)
			{
				if (stringBuilder.Length > 0)
				{
					stringBuilder.Append('\n');
				}
				stringBuilder.Append(text);
				num++;
			}
		}
		if (stringBuilder.Length == 0)
		{
			return "<no stack>";
		}
		return stringBuilder.ToString();
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(SpriteTextureLoader), "getSprite", new Type[] { typeof(string) })]
	private static bool SpriteTextureLoader_getSprite_NullPathGuard(ref Sprite __result, string __0)
	{
		if (!string.IsNullOrWhiteSpace(__0))
		{
			return true;
		}
		XuanJian_LogEmptySpritePathSourceOnce("SpriteTextureLoader.getSprite.prefix", __0);
		if (XuanJian_ShouldLogLimited(ref _xuanJianGetSpriteEmptyPathWarnCount))
		{
			Debug.LogWarning("[玄鉴] 拦截到空的 Sprite 路径(getSprite)，已返回 null 避免崩溃。累计: " + _xuanJianGetSpriteEmptyPathWarnCount);
		}
		__result = null;
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(SpriteTextureLoader), "getSprite", new Type[] { typeof(string) })]
	private static Exception SpriteTextureLoader_getSprite_Finalizer(Exception __exception, ref Sprite __result, string __0)
	{
		if (__exception is ArgumentNullException ex && string.Equals(ex.ParamName, "key", StringComparison.OrdinalIgnoreCase))
		{
			XuanJian_LogEmptySpritePathSourceOnce("SpriteTextureLoader.getSprite.finalizer", __0, __exception);
			if (XuanJian_ShouldLogLimited(ref _xuanJianGetSpriteNullKeyWarnCount))
			{
				Debug.LogWarning("[玄鉴] getSprite 遇到空 key 异常，路径: " + (string.IsNullOrEmpty(__0) ? "<empty>" : __0) + "，已返回 null 避免崩溃。累计: " + _xuanJianGetSpriteNullKeyWarnCount);
			}
			__result = null;
			return null;
		}
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(SpriteTextureLoader), "getSpriteList", new Type[]
	{
		typeof(string),
		typeof(bool)
	})]
	private static bool SpriteTextureLoader_getSpriteList_NullPathGuard(ref Sprite[] __result, string __0, bool __1)
	{
		if (!string.IsNullOrWhiteSpace(__0))
		{
			return true;
		}
		XuanJian_LogEmptySpritePathSourceOnce("SpriteTextureLoader.getSpriteList.prefix", __0);
		if (XuanJian_ShouldLogLimited(ref _xuanJianGetSpriteListEmptyPathWarnCount))
		{
			Debug.LogWarning("[玄鉴] 拦截到空的 Sprite 路径，已返回空数组避免崩溃。累计: " + _xuanJianGetSpriteListEmptyPathWarnCount);
		}
		__result = Array.Empty<Sprite>();
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(SpriteTextureLoader), "getSpriteList", new Type[]
	{
		typeof(string),
		typeof(bool)
	})]
	private static Exception SpriteTextureLoader_getSpriteList_Finalizer(Exception __exception, ref Sprite[] __result, string __0, bool __1)
	{
		if (__exception is ArgumentNullException ex && string.Equals(ex.ParamName, "key", StringComparison.OrdinalIgnoreCase))
		{
			XuanJian_LogEmptySpritePathSourceOnce("SpriteTextureLoader.getSpriteList.finalizer", __0, __exception);
			if (XuanJian_ShouldLogLimited(ref _xuanJianGetSpriteListNullKeyWarnCount))
			{
				Debug.LogWarning("[玄鉴] getSpriteList 遇到空 key 异常，路径: " + (string.IsNullOrEmpty(__0) ? "<empty>" : __0) + "，已返回空数组避免崩溃。累计: " + _xuanJianGetSpriteListNullKeyWarnCount);
			}
			__result = Array.Empty<Sprite>();
			return null;
		}
		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ResourceLibrary), "loadSprites")]
	private static Exception ResourceLibrary_loadSprites_Finalizer(Exception __exception)
	{
		if (__exception is ArgumentNullException ex && string.Equals(ex.ParamName, "key", StringComparison.OrdinalIgnoreCase))
		{
			XuanJian_LogEmptySpritePathSourceOnce("ResourceLibrary.loadSprites.finalizer", string.Empty, __exception);
			if (XuanJian_ShouldLogLimited(ref _xuanJianResourceLibraryNullKeyWarnCount))
			{
				Debug.LogWarning("[玄鉴] ResourceLibrary.loadSprites 捕获到空 key 异常，已拦截避免中断加载。累计: " + _xuanJianResourceLibraryNullKeyWarnCount);
			}
			return null;
		}
		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UnitAvatarLoader), "showStatic")]
	private static Exception XuanJian_UnitAvatarLoader_ShowStatic_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		if (UnitAvatarLoaderNullGuardLogCount < 20)
		{
			UnitAvatarLoaderNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 UnitAvatarLoader.showStatic 空引用，选中头像本帧跳过。");
		}

		return null;
	}


	private static bool EnsureActorTextureAssetReadyForParallelRender(Actor actor)
	{
		if (actor == null || actor.asset == null)
		{
			return false;
		}
		if (actor.asset.texture_asset != null)
		{
			return true;
		}

		Subspecies subspecies = actor.subspecies;
		bool subspeciesHasMutationReskin = GetSubspeciesHasMutationReskin(subspecies);
		if (subspecies != null && subspeciesHasMutationReskin)
		{
			SubspeciesTrait mutation_skin_asset = subspecies.mutation_skin_asset;
			if (mutation_skin_asset == null || mutation_skin_asset.texture_asset == null)
			{
				SetSubspeciesHasMutationReskin(subspecies, value: false);
				subspeciesHasMutationReskin = GetSubspeciesHasMutationReskin(subspecies);
			}
		}
		if (subspecies == null || !subspeciesHasMutationReskin)
		{
			return false;
		}
		SubspeciesTrait mutation_skin_asset2 = subspecies.mutation_skin_asset;
		if (mutation_skin_asset2 != null)
		{
			return mutation_skin_asset2.texture_asset != null;
		}
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataParallel")]
	private static void XuanJian_ActorManager_PrecalculateRenderDataParallel_NullGuard_Prefix(ActorManager __instance)
	{
		if (!XjLongShuSystem.RequiresRenderTextureGuard)
		{
			return;
		}

		try
		{
			if (__instance == null || __instance.visible_units == null)
			{
				return;
			}
			Actor[] array = __instance.visible_units.array;
			if (array == null || array.Length == 0)
			{
				return;
			}
			int num = Mathf.Min(__instance.visible_units.count, array.Length);
			for (int i = 0; i < num; i++)
			{
				Actor val = array[i];
				if (val != null
					&& XjLongShuSystem.IsLongShu(val)
					&& val.asset?.texture_asset == null
					&& !EnsureActorTextureAssetReadyForParallelRender(val))
				{
					array[i] = null;
				}
			}
		}
		catch
		{
		}
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataParallel")]
	private static Exception XuanJian_ActorManager_PrecalculateRenderDataParallel_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		bool flag = __exception is NullReferenceException;
		if (!flag && __exception is AggregateException { InnerExceptions: not null } ex)
		{
			for (int i = 0; i < ex.InnerExceptions.Count; i++)
			{
				if (ex.InnerExceptions[i] is NullReferenceException)
				{
					flag = true;
					break;
				}
			}
		}
		if (!flag)
		{
			return __exception;
		}
		if (ActorManagerPrecalculateNullGuardLogCount < 20)
		{
			ActorManagerPrecalculateNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 precalculateRenderDataParallel 空引用，当前帧跳过异常对象。ex=" + __exception.GetType().Name);
		}
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataNormal")]
	private static void XuanJian_ActorManager_PrecalculateRenderDataNormal_NullGuard_Prefix(ActorManager __instance)
	{
		if (!XjLongShuSystem.RequiresRenderTextureGuard)
		{
			return;
		}

		try
		{
			if (__instance == null || __instance.visible_units == null)
			{
				return;
			}

			Actor[] array = __instance.visible_units.array;
			if (array == null || array.Length == 0)
			{
				return;
			}

			int count = Mathf.Min(__instance.visible_units.count, array.Length);
			for (int i = 0; i < count; i++)
			{
				Actor actor = array[i];
				if (actor != null
					&& XjLongShuSystem.IsLongShu(actor)
					&& actor.asset?.texture_asset == null
					&& !EnsureActorTextureAssetReadyForParallelRender(actor))
				{
					array[i] = null;
				}
			}
		}
		catch
		{
		}
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataNormal")]
	private static Exception XuanJian_ActorManager_PrecalculateRenderDataNormal_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is not NullReferenceException)
		{
			return __exception;
		}

		if (ActorManagerPrecalculateNullGuardLogCount < 20)
		{
			ActorManagerPrecalculateNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 precalculateRenderDataNormal 空引用，当前帧跳过异常对象。");
		}
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorManager), "prepareForMetaChecks")]
	private static Exception XuanJian_ActorManager_PrepareForMetaChecks_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}
		if (ActorManagerMetaNullGuardLogCount < 20)
		{
			ActorManagerMetaNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 ActorManager.prepareForMetaChecks 空引用，当前帧跳过原生元信息检测异常对象。");
		}
		return null;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(SimObjectsZones), "addUnit")]
	private static bool XuanJian_SimObjectsZones_AddUnit_NullGuard_Prefix(Actor pActor, WorldTile pTile)
	{
		if (pActor?.data != null && pTile?.chunk != null)
		{
			return true;
		}
		if (SimObjectsZonesAddUnitNullGuardLogCount < 20)
		{
			SimObjectsZonesAddUnitNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已跳过 SimObjectsZones.addUnit 的无效单位或无效地块。");
		}
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(ChunkObjectContainer), "addActor")]
	private static bool XuanJian_ChunkObjectContainer_AddActor_NullGuard_Prefix(Actor pActor)
	{
		if (pActor?.data != null && pActor.asset != null)
		{
			return true;
		}
		if (ChunkObjectContainerAddActorNullGuardLogCount < 20)
		{
			ChunkObjectContainerAddActorNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已跳过 ChunkObjectContainer.addActor 的无效单位。");
		}
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ChunkObjectContainer), "addActor")]
	private static Exception XuanJian_ChunkObjectContainer_AddActor_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}
		if (ChunkObjectContainerAddActorNullGuardLogCount < 20)
		{
			ChunkObjectContainerAddActorNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 ChunkObjectContainer.addActor 空引用，当前帧跳过无效单位分区。");
		}
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(SimObjectsZones), "addUnit")]
	private static Exception XuanJian_SimObjectsZones_AddUnit_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}
		if (SimObjectsZonesAddUnitNullGuardLogCount < 20)
		{
			SimObjectsZonesAddUnitNullGuardLogCount++;
			Debug.LogWarning("[玄鉴] 已拦截 SimObjectsZones.addUnit 空引用，当前帧跳过无效单位分区。");
		}
		return null;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataParallel")]
	private static void XuanJian_ActorManager_PrecalculateRenderDataParallel_ClosedCultivationOffset_Postfix(ActorManager __instance)
	{
		XjVisibleActorRenderLane.Apply(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataNormal")]
	private static void XuanJian_ActorManager_PrecalculateRenderDataNormal_ClosedCultivationOffset_Postfix(ActorManager __instance)
	{
		XjVisibleActorRenderLane.Apply(__instance);
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(BuildingManager), "precalculateRenderDataNormal")]
	private static Exception XuanJian_BuildingManager_PrecalculateRenderDataNormal_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		if (__exception is not NullReferenceException)
		{
			return __exception;
		}
		return null;
	}


}
