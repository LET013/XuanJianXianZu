using System;
using UnityEngine;

namespace XuanJianVNext.UI.Common;

internal sealed class XjUiSurfaceOwner : MonoBehaviour
{
	internal string SurfaceId;
	internal string OwnerId;
}

/// <summary>
/// Read-only surface lookup + ownership marker. Lookup must never mutate native
/// ScrollWindow/WindowMetaTab collections: destroying/removing a tab during a read path
/// leaves DragOrderContainer with stale runtime references and breaks later window close.
/// </summary>
internal static class XjUiSurfaceOwnership
{
	internal static Transform FindSingleRoot(Transform parent, string rootName, string surfaceId, string ownerId)
	{
		if (parent == null || string.IsNullOrWhiteSpace(rootName)) return null;
		Transform retained = null;
		for (int i = 0; i < parent.childCount; i++)
		{
			Transform child = parent.GetChild(i);
			if (child == null || !string.Equals(child.name, rootName, StringComparison.Ordinal)) continue;
			retained = child;
			break;
		}
		if (retained != null) Claim(retained, surfaceId, ownerId);
		return retained;
	}

	internal static Transform Claim(Transform root, string surfaceId, string ownerId)
	{
		if (root == null) return null;
		XjUiSurfaceOwner marker = root.GetComponent<XjUiSurfaceOwner>()
			?? root.gameObject.AddComponent<XjUiSurfaceOwner>();
		marker.SurfaceId = surfaceId ?? string.Empty;
		marker.OwnerId = ownerId ?? string.Empty;
		return root;
	}

	internal static WindowMetaTab FindSingleTab(ScrollWindow scrollWindow, string tabId)
	{
		if (scrollWindow?.tabs?._tabs == null || string.IsNullOrWhiteSpace(tabId)) return null;
		for (int i = 0; i < scrollWindow.tabs._tabs.Count; i++)
		{
			WindowMetaTab tab = scrollWindow.tabs._tabs[i];
			if (tab != null && string.Equals(tab.name, tabId, StringComparison.Ordinal)) return tab;
		}
		return null;
	}
}
