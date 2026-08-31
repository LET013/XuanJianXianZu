using System;
using HarmonyLib;
using UnityEngine.UI;
using XuanJianVNext.Core;

namespace XuanJianVNext.Patches;

/// <summary>
/// 原生世界日志格式化的最后边界。旧档、其他模组或失效资产造成格式化异常时，
/// 直接显示消息保存的正文，避免 WorldLogWindow 每帧重复抛错。
/// </summary>
[HarmonyPatch(typeof(WorldLogMessageExtensions), "getFormatedText", new Type[] { typeof(WorldLogMessage), typeof(Text) })]
internal static class XjWorldLogFormattedTextSafetyPatch
{
	[HarmonyFinalizer]
	private static Exception Finalizer(Exception __exception, WorldLogMessage __0, Text __1)
	{
		if (__exception == null) return null;
		try
		{
			if (__1 != null)
			{
				__1.text = __0?.special1 ?? string.Empty;
			}
		}
		catch
		{
			// 最后边界不得再抛出异常。
		}
		XjExceptionDiagnostics.Report("WorldLogMessageExtensions.getFormatedText.fallback", __exception);
		return null;
	}
}
