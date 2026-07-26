using System;
using HarmonyLib;
using UnityEngine;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	private static int _xuanJianPowerTipMetaGuardLogCount;
	private static int _xuanJianSelectedUnitStatsGuardLogCount;
	private static int _xuanJianUnitAvatarLoadGuardLogCount;
	private static int _xuanJianActorAvatarDataGuardLogCount;
	private static int _xuanJianSelectedMetaBannersGuardLogCount;
	private static int _xuanJianUiUnitAvatarElementGuardLogCount;
	private static int _xuanJianActorTraitsEditorGuardLogCount;
	private static int _xuanJianArmyBannerGuardLogCount;

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(PowerTabLibrary), "getWorldTipTextMetaName")]
	private static Exception XuanJian_PowerTabLibrary_GetWorldTipTextMetaName_NullRefGuard_Finalizer(Exception __exception, ref string __result)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			__result = string.Empty;
			if (_xuanJianPowerTipMetaGuardLogCount < 4)
			{
				_xuanJianPowerTipMetaGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生提示名称空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(SelectedUnitTab), "showStatsGeneral")]
	private static Exception XuanJian_SelectedUnitTab_ShowStatsGeneral_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			if (_xuanJianSelectedUnitStatsGuardLogCount < 4)
			{
				_xuanJianSelectedUnitStatsGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生选中单位统计空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UnitAvatarLoader), "load", new[] { typeof(Actor) })]
	private static Exception XuanJian_UnitAvatarLoader_Load_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			if (_xuanJianUnitAvatarLoadGuardLogCount < 4)
			{
				_xuanJianUnitAvatarLoadGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生单位头像加载空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UiUnitAvatarElement), "show", new[] { typeof(Actor) })]
	private static Exception XuanJian_UiUnitAvatarElement_Show_NullRefGuard_Finalizer(Exception __exception)
	{
		return SwallowAvatarElementNullRef(__exception, "头像元素显示");
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UiUnitAvatarElement), "load", new[] { typeof(NanoObject) })]
	private static Exception XuanJian_UiUnitAvatarElement_Load_NullRefGuard_Finalizer(Exception __exception)
	{
		return SwallowAvatarElementNullRef(__exception, "头像元素加载");
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorAvatarData), "setData", new[] { typeof(Actor) })]
	private static Exception XuanJian_ActorAvatarData_SetData_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			if (_xuanJianActorAvatarDataGuardLogCount < 4)
			{
				_xuanJianActorAvatarDataGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生头像数据空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorSelectedMetaBanners), "update", new[] { typeof(Actor) })]
	private static Exception XuanJian_ActorSelectedMetaBanners_Update_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			if (_xuanJianSelectedMetaBannersGuardLogCount < 4)
			{
				_xuanJianSelectedMetaBannersGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生单位标记刷新空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ActorTraitsEditor), "addTrait", new[] { typeof(ActorTrait) })]
	private static Exception XuanJian_ActorTraitsEditor_AddTrait_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception == null)
		{
			return null;
		}

		if (__exception is NullReferenceException)
		{
			if (_xuanJianActorTraitsEditorGuardLogCount < 4)
			{
				_xuanJianActorTraitsEditorGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生特质编辑器空引用：" + __exception.GetType().Name);
			}
			return null;
		}

		return __exception;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(ArmyBanner), "setupBanner")]
	private static Exception XuanJian_ArmyBanner_SetupBanner_NullRefGuard_Finalizer(Exception __exception)
	{
		if (__exception is not NullReferenceException) return __exception;
		if (_xuanJianArmyBannerGuardLogCount < 4)
		{
			_xuanJianArmyBannerGuardLogCount++;
			Debug.LogWarning("[玄鉴][UI] 已拦截原生单位旗帜空引用：" + __exception.GetType().Name);
		}
		return null;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UiUnitAvatarElement), "tooltipActionActor")]
	private static Exception XuanJian_UiUnitAvatarElement_TooltipActionActor_NullRefGuard_Finalizer(Exception __exception)
	{
		return SwallowAvatarElementNullRef(__exception, "头像悬浮提示");
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(TooltipLibrary), "showActor", new[] { typeof(string), typeof(Tooltip), typeof(TooltipData) })]
	private static Exception XuanJian_TooltipLibrary_ShowActor_NullRefGuard_Finalizer(Exception __exception)
	{
		return SwallowAvatarElementNullRef(__exception, "角色悬浮提示");
	}

	private static Exception SwallowAvatarElementNullRef(Exception exception, string source)
	{
		if (exception == null)
		{
			return null;
		}

		if (exception is NullReferenceException)
		{
			if (_xuanJianUiUnitAvatarElementGuardLogCount < 4)
			{
				_xuanJianUiUnitAvatarElementGuardLogCount++;
				Debug.LogWarning("[玄鉴][UI] 已拦截原生" + source + "空引用：" + exception.GetType().Name);
			}
			return null;
		}

		return exception;
	}
}
