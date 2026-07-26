using System;
using HarmonyLib;
using UnityEngine;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object) })]
	private static bool XuanJian_Debug_Log_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object), typeof(UnityEngine.Object) })]
	private static bool XuanJian_Debug_LogWithContext_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.LogWarning), new Type[] { typeof(object) })]
	private static bool XuanJian_Debug_LogWarning_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.LogWarning), new Type[] { typeof(object), typeof(UnityEngine.Object) })]
	private static bool XuanJian_Debug_LogWarningWithContext_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.LogError), new Type[] { typeof(object) })]
	private static bool XuanJian_Debug_LogError_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.LogError), new Type[] { typeof(object), typeof(UnityEngine.Object) })]
	private static bool XuanJian_Debug_LogErrorWithContext_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianLog(message);
	}

	private static bool ShouldSuppressXuanJianLog(object message)
	{
		string text = message?.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		return text.IndexOf("玄鉴", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("XuanJian", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("xuanjian", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("登名石", StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
