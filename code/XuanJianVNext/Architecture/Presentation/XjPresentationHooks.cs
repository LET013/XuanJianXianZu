using System;

namespace XuanJianVNext.Architecture.Presentation;

/// <summary>
/// One-way presentation port. Domain/runtime systems may publish invalidation or
/// display metadata through this class, but never reference a concrete UI type.
/// The UI composition module binds the handlers once during startup.
/// </summary>
internal static class XjPresentationHooks
{
	private static Action _ensureWorldPresentation;
	private static Action _resetWorldPresentation;
	private static Action _markSectMapDirty;
	private static Action _invalidateActorInfo;
	private static Action<long> _removeActorInfoProjection;
	private static Action _clearActorInfoProjectionCache;
	private static Action<string[]> _registerTooltipTexts;
	private static Action<bool> _setFpsOverlayEnabled;
	private static Func<string, bool, string, float, string, bool> _showWorldTip;

	internal static void Configure(
		Action ensureWorldPresentation,
		Action resetWorldPresentation,
		Action markSectMapDirty,
		Action invalidateActorInfo,
		Action<long> removeActorInfoProjection,
		Action clearActorInfoProjectionCache,
		Action<string[]> registerTooltipTexts,
		Action<bool> setFpsOverlayEnabled,
		Func<string, bool, string, float, string, bool> showWorldTip)
	{
		_ensureWorldPresentation = ensureWorldPresentation;
		_resetWorldPresentation = resetWorldPresentation;
		_markSectMapDirty = markSectMapDirty;
		_invalidateActorInfo = invalidateActorInfo;
		_removeActorInfoProjection = removeActorInfoProjection;
		_clearActorInfoProjectionCache = clearActorInfoProjectionCache;
		_registerTooltipTexts = registerTooltipTexts;
		_setFpsOverlayEnabled = setFpsOverlayEnabled;
		_showWorldTip = showWorldTip;
	}

	internal static void EnsureWorldPresentation()
	{
		_ensureWorldPresentation?.Invoke();
	}

	internal static void ResetWorldPresentation()
	{
		_resetWorldPresentation?.Invoke();
	}

	internal static void MarkSectMapDirty()
	{
		_markSectMapDirty?.Invoke();
	}

	internal static void InvalidateActorInfo()
	{
		_invalidateActorInfo?.Invoke();
	}

	internal static void RemoveActorInfoProjection(long actorId)
	{
		if (actorId > 0L) _removeActorInfoProjection?.Invoke(actorId);
	}

	internal static void ClearActorInfoProjectionCache()
	{
		_clearActorInfoProjectionCache?.Invoke();
	}

	internal static void SetFpsOverlayEnabled(bool enabled)
	{
		_setFpsOverlayEnabled?.Invoke(enabled);
	}

	internal static bool TryShowWorldTip(string text, bool pause, string position, float duration, string color)
	{
		return _showWorldTip?.Invoke(text, pause, position, duration, color) == true;
	}

	internal static void RegisterTooltipTexts(params string[] texts)
	{
		_registerTooltipTexts?.Invoke(texts ?? Array.Empty<string>());
	}
}
