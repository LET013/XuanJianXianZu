using System;
using HarmonyLib;
using UnityEngine;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	// Phase 8C.1：这里只压制玄鉴的普通信息噪声，绝不能再吞 Warning / Error。
	// 旧实现同时 Patch Debug.LogWarning / Debug.LogError，并且只要文本包含“玄鉴”
	// 就返回 false，导致读档 P0、归档回滚、补丁安装失败等诊断全部从 Player.log 消失。
	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object) })]
	private static bool XuanJian_Debug_Log_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianInfoLog(message);
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(Debug), nameof(Debug.Log), new Type[] { typeof(object), typeof(UnityEngine.Object) })]
	private static bool XuanJian_Debug_LogWithContext_SuppressXuanJian_Prefix(object message)
	{
		return !ShouldSuppressXuanJianInfoLog(message);
	}

	private static bool ShouldSuppressXuanJianInfoLog(object message)
	{
		string text = message?.ToString();
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		if (!ContainsXuanJianMarker(text))
		{
			return false;
		}

		// 启动、补丁、存档、Phase8 与 P0 日志属于验收链本身，必须可见。
		// 这些前缀即使以后增加新子阶段，也不能再次被“降噪”误吞。
		if (IsCriticalLifecycleLog(text))
		{
			return false;
		}

		// 保留既有“普通玄鉴信息不刷 Player.log”的行为，避免公告、旃檀林维护、
		// 调试干预等低价值信息重新形成日志洪水。
		return true;
	}

	private static bool ContainsXuanJianMarker(string text)
	{
		return text.IndexOf("玄鉴", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("XuanJian", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("登名石", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool IsCriticalLifecycleLog(string text)
	{
		return text.IndexOf("[玄鉴][P0]", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][Phase8", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][存档", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][归档", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][兼容层]", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][补丁]", StringComparison.Ordinal) >= 0
			|| text.IndexOf("[玄鉴][模块]", StringComparison.Ordinal) >= 0;
	}
}
