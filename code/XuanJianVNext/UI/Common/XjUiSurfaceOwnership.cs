using System;
using UnityEngine;

namespace XuanJianVNext.UI.Common;

internal sealed class XjUiSurfaceOwner : MonoBehaviour
{
	internal string SurfaceId;
	internal string OwnerId;
}

internal static class XjUiSurfaceOwnership
{
	internal static Transform FindSingleRoot(Transform parent, string rootName, string surfaceId, string ownerId)
	{
		if (parent == null || string.IsNullOrWhiteSpace(rootName))
		{
			return null;
		}

		Transform retained = null;
		for (int i = parent.childCount - 1; i >= 0; i--)
		{
			Transform child = parent.GetChild(i);
			if (child == null || !string.Equals(child.name, rootName, StringComparison.Ordinal))
			{
				continue;
			}

			if (retained == null)
			{
				retained = child;
				continue;
			}

			UnityEngine.Object.DestroyImmediate(child.gameObject);
		}

		if (retained != null)
		{
			Claim(retained, surfaceId, ownerId);
		}
		return retained;
	}

	internal static Transform Claim(Transform root, string surfaceId, string ownerId)
	{
		if (root == null)
		{
			return null;
		}

		XjUiSurfaceOwner marker = root.GetComponent<XjUiSurfaceOwner>()
			?? root.gameObject.AddComponent<XjUiSurfaceOwner>();
		marker.SurfaceId = surfaceId ?? string.Empty;
		marker.OwnerId = ownerId ?? string.Empty;
		return root;
	}

	internal static WindowMetaTab FindSingleTab(ScrollWindow scrollWindow, string tabId)
	{
		if (scrollWindow?.tabs?._tabs == null || string.IsNullOrWhiteSpace(tabId))
		{
			return null;
		}

		WindowMetaTab retained = null;
		for (int i = scrollWindow.tabs._tabs.Count - 1; i >= 0; i--)
		{
			WindowMetaTab tab = scrollWindow.tabs._tabs[i];
			if (tab == null)
			{
				scrollWindow.tabs._tabs.RemoveAt(i);
				continue;
			}

			if (!string.Equals(tab.name, tabId, StringComparison.Ordinal))
			{
				continue;
			}

			if (retained == null)
			{
				retained = tab;
				continue;
			}

			scrollWindow.tabs._tabs.RemoveAt(i);
			if (tab != retained)
			{
				UnityEngine.Object.DestroyImmediate(tab.gameObject);
			}
		}
		return retained;
	}
}
