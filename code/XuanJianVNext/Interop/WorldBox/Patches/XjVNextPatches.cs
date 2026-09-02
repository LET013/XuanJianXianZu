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
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.Systems.Visual;
using XuanJianVNext.Core;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.UI.QianKunDai;
using ai;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Actor), "saveKingdomCiv")]
	private static bool XuanJian_Actor_SaveKingdomCiv_ReadOnlyBoundary_Prefix(Actor __instance)
	{
		if (__instance?.data == null) return false;

		bool isCivilizationActor;
		try { isCivilizationActor = __instance.isKingdomCiv(); }
		catch { return true; }

		if (!isCivilizationActor || __instance.kingdom?.data != null) return true;

		// Save 是序列化边界，不是玩法修复入口。旧逻辑会在这里尝试 setKingdom、
		// buildCityAndStartCivilization 乃至 makeNewCivKingdom；保存一次就可能改变
		// 正在运行的世界。对于已经断裂的文明引用，只跳过该角色的 kingdom/civ
		// 原生序列化段，运行期修复器在正常帧处理；绝不在保存事务里建城、建国、
		// 改居民或清空 culture/clan/family。
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.currentWorldToSavedMap))]
	private static void XuanJian_SaveManager_CurrentWorldToSavedMap_CommitArchive_Prefix(out long __state)
	{
		__state = System.Diagnostics.Stopwatch.GetTimestamp();
		try
		{
			long totalStarted = __state;

			long stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
			// 保存只确保归档容器可读，不再同步跑完 bootstrap。读档恢复属于正常
			// runtime 事务，不能因为玩家点“保存”就无预算推进修复/迁移玩法。
			XjWorldArchiveSystem.EnsureLoadedForWorldSnapshot();
			ObserveSaveBoundaryStage("save.archiveReadyMs", stageStarted);

			bool bootstrapPending = XjWorldBootstrapLane.HasPending;
			if (!bootstrapPending)
			{
				stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				XjScheduler.FlushPendingAnnualStatesForSave();
				ObserveSaveBoundaryStage("save.annualFlushMs", stageStarted);

				stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				XjGuoWeiQuanBingRegistry.PrepareLiveSnapshotsForSave();
				ObserveSaveBoundaryStage("save.authoritySnapshotMs", stageStarted);

				stageStarted = System.Diagnostics.Stopwatch.GetTimestamp();
				XjWorldArchiveSystem.CommitPendingBeforeWorldSnapshot();
				ObserveSaveBoundaryStage("save.archiveCommitMs", stageStarted);
			}
			else
			{
				// ArchiveReady -> BootstrapComplete 期间，已解码的原始 archive 仍在
				// WorldBox custom_data 中。不要把“半恢复 runtime”重新导出覆盖它；
				// WorldBox 继续保存自己的世界，玄鉴归档原样随存档保留。
				XjPerformanceTelemetry.ObserveQueue("save.bootstrapArchivePreserved", 1);
			}

			ObserveSaveBoundaryStage("save.totalPrefixMs", totalStarted);
		}
		catch (Exception ex)
		{
			// A mod-side archive/sanitization defect must not abort WorldBox's own save.
			// The persistent diagnostic identifies the failed stage on the next report.
			XjExceptionDiagnostics.Report("Interop/SaveManager/currentWorldToSavedMap-prefix", ex);
		}
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.currentWorldToSavedMap))]
	private static void XuanJian_SaveManager_CurrentWorldToSavedMap_Performance_Postfix(long __state)
	{
		try
		{
			ObserveSaveBoundaryStage("save.totalCallMs", __state);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Interop/SaveManager/currentWorldToSavedMap-postfix", ex);
		}
	}

	private static void ObserveSaveBoundaryStage(string key, long startedTimestamp)
	{
		if (startedTimestamp <= 0L || string.IsNullOrWhiteSpace(key)) return;
		double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startedTimestamp)
			* 1000d / System.Diagnostics.Stopwatch.Frequency;
		int rounded = elapsedMs >= int.MaxValue
			? int.MaxValue
			: Math.Max(1, (int)Math.Ceiling(elapsedMs));
		XjPerformanceTelemetry.ObserveQueue(key, rounded);
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
	[HarmonyPatch(typeof(Subspecies), "getUnitSpriteForBanner")]
	private static bool XuanJian_Subspecies_GetUnitSpriteForBanner_YaoShuGreatSage_Prefix(Subspecies __instance, ref Sprite __result)
	{
		if (!XjYaoShuGreatSageSystem.IsGreatSageSubspecies(__instance))
		{
			return true;
		}

		// 大圣实际战斗外观由自己的帧库驱动，不能让原生横幅再依赖 walk_0。
		// 这只返回已加载的首帧，不改亚种、王国或原生单位容器。
		__result = XjYaoShuGreatSageSystem.TryGetBannerSprite(__instance);
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
			// Civilization actors overwhelmingly have a valid kingdom. Check that
			// reference first and avoid the virtual isAnimal() call on the hot common path.
			Kingdom kingdom = __instance.kingdom;
			if (kingdom != null)
			{
				if (kingdom.data != null && kingdom.asset != null) return true;
				__result = false;
				return false;
			}
			if (__instance.isAnimal()) return true;
		}
		catch { }

		__result = false;
		return false;
	}


	private static int _xuanJianGetSpriteEmptyPathWarnCount;

	private static int _xuanJianGetSpriteNullKeyWarnCount;

	private static int _xuanJianGetSpriteListEmptyPathWarnCount;

	private static int _xuanJianGetSpriteListNullKeyWarnCount;

	private static int _xuanJianResourceLibraryNullKeyWarnCount;

	private static readonly object _xuanJianSpriteTraceLock = new object();

	private static readonly HashSet<string> _xuanJianSpriteTraceSourceKeys = new HashSet<string>();

	private const int XuanJianMaxSpriteTraceSources = 30;

	private const BindingFlags SubspeciesOptionalMemberFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

	// 0.51.x 的 Subspecies.has_mutation_reskin 在不同构建中可能是属性而不是字段。
	// AccessTools.Field 在字段不存在时会主动写 HarmonyX warning；可选版本能力改用 CLR
	// 反射静默探测，不把“此构建没有该字段”伪装成运行错误。
	private static readonly FieldInfo SubspeciesHasMutationReskinField = typeof(Subspecies).GetField("has_mutation_reskin", SubspeciesOptionalMemberFlags);

	private static readonly FieldInfo SubspeciesHasMutationReskinBackingField = typeof(Subspecies).GetField("<has_mutation_reskin>k__BackingField", SubspeciesOptionalMemberFlags);

	private static readonly PropertyInfo SubspeciesHasMutationReskinProperty = typeof(Subspecies).GetProperty("has_mutation_reskin", SubspeciesOptionalMemberFlags);

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
		catch (System.Exception xjCaught265) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:265", xjCaught265); }
		try
		{
			object value = (SubspeciesHasMutationReskinField != null) ? SubspeciesHasMutationReskinField.GetValue(subspecies) : ((SubspeciesHasMutationReskinBackingField != null) ? SubspeciesHasMutationReskinBackingField.GetValue(subspecies) : null);
			if (value is bool result)
			{
				return result;
			}
		}
		catch (System.Exception xjCaught276) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:276", xjCaught276); }
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
		catch (System.Exception xjCaught303) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:303", xjCaught303); }
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
				text4 = " | 异常=" + exception.GetType().Name + ": " + exception.Message;
			}
			Debug.LogWarning("[玄鉴][图像追踪] 空路径来源捕获 site=" + site + " path=" + text2 + " first=" + text + text4 + "\n" + text3);
		}
		catch (System.Exception xjCaught346) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:346", xjCaught346); }
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
					// 龙属有自己的非空 override sprite；缺失的仅是原生阴影
					// 纹理。绝不能把 visible_units 槽位改成 null——该数组属于
					// ActorManager 的本帧工作集，原生并行循环默认元素均有效。
					// 关闭可选阴影即可避开 texture_asset 解引用，同时保留角色
					// 正常参与原生主体绘制与后续 UI/选择流程。
					val.setShowShadow(false);
				}
			}
		}
		catch (System.Exception xjCaught585) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:585", xjCaught585); }
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataParallel")]
	private static Exception XuanJian_ActorManager_PrecalculateRenderDataParallel_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		// 没有龙属缺失纹理时，这个旧 finalizer 不得接管原生并行渲染的异常。
		if (!XjLongShuSystem.RequiresRenderTextureGuard) return __exception;
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
					// 与并行阶段一致：不改写原生可见列表，只关闭会访问
					// 缺失 texture_asset 的可选阴影分支。
					actor.setShowShadow(false);
				}
			}
		}
		catch (System.Exception xjCaught657) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Patches/XjVNextPatches.cs:657", xjCaught657); }
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorManager), "precalculateRenderDataNormal")]
	private static Exception XuanJian_ActorManager_PrecalculateRenderDataNormal_NullGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}
		// 与并行路径同一边界：仅在龙属纹理保护实际启用时隔离对应坏帧。
		if (!XjLongShuSystem.RequiresRenderTextureGuard) return __exception;

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
