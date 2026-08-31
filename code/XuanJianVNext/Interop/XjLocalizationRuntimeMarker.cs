using System;

namespace XuanJianVNext.Interop;

/// <summary>
/// Public runtime marker for companion localization/name-pack mods.
/// Presence on disk is not enough: companion mods should read IsRuntimeLoaded.
/// </summary>
public static class XjLocalizationRuntimeMarker
{
	public const int ApiVersion = 1;
	public const string ModId = "shiyue.worldbox.mod.XuanJian";
	public const string LocalizationProfile = "xuanjian";
	public const string DisplayName = "玄鉴仙族";

	public static bool IsRuntimeLoaded { get; private set; }
	public static DateTime LoadedAtUtc { get; private set; }

	internal static void MarkLoaded()
	{
		IsRuntimeLoaded = true;
		LoadedAtUtc = DateTime.UtcNow;
	}
}
