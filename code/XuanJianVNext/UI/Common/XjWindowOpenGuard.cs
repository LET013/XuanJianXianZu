using UnityEngine;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// WindowCreator leaves a newly-created window active. ScrollWindow.showWindow is a toggle,
/// so invoking it immediately used to close the fresh window and caused the first-click flash.
/// Normalize every new window to inactive first, then use the normal opening path exactly once.
/// </summary>
internal static class XjWindowOpenGuard
{
	internal static void Show(ScrollWindow window, string windowId, bool createdNow)
	{
		if (window == null || string.IsNullOrWhiteSpace(windowId))
		{
			return;
		}

		GameObject gameObject = ((Component)window).gameObject;
		if (createdNow && gameObject != null && gameObject.activeSelf)
		{
			gameObject.SetActive(false);
		}

		ScrollWindow.showWindow(windowId);
		Canvas.ForceUpdateCanvases();
	}
}
