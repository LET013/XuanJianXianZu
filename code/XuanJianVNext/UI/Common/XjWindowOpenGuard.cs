using UnityEngine;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// WindowCreator leaves a newly-created custom window active while ScrollWindow.showWindow
/// behaves as a toggle. Normalize the new custom window to inactive once, then delegate the
/// complete open/tween/history transaction to WorldBox. No global Canvas force-update here:
/// forcing every canvas while another native window is closing can advance layout against a
/// tween target that WorldBox has just killed.
/// </summary>
internal static class XjWindowOpenGuard
{
	internal static void Show(ScrollWindow window, string windowId, bool createdNow)
	{
		if (window == null || string.IsNullOrWhiteSpace(windowId)) return;
		GameObject gameObject = ((Component)window).gameObject;
		if (createdNow && gameObject != null && gameObject.activeSelf) gameObject.SetActive(false);
		ScrollWindow.showWindow(windowId);
	}
}
