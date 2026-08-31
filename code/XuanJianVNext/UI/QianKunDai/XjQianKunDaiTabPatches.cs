using UnityEngine;

namespace XuanJianVNext.UI.QianKunDai;

/// <summary>
/// Compatibility shell for the retired native UnitWindow tab integration.
///
/// WorldBox 0.51.2 owns UnitWindow's WindowMetaTab/DragOrderContainer lifecycle.
/// Injecting a cloned WindowMetaTab copied runtime DragOrderElement/Canvas state and
/// left DragOrderContainer with half-initialized children during window close/switch.
/// QianKunDai is now opened from a fixed safe shortcut into a XuanJian-owned window.
/// This class intentionally performs no native tab/list/layout mutation.
/// </summary>
internal static class XjQianKunDaiTabPatches
{
	internal static bool TryAddOrUpdate(UnitWindow window)
	{
		return false;
	}
}
