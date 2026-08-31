using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Family;

/// <summary>
/// Compatibility shell for the retired native UnitWindow family-inheritance tab.
///
/// Family rendering helpers remain in the other partial files because XuanJian-owned
/// detail/codex surfaces still reuse them.  What is intentionally absent here is any
/// WindowMetaTab cloning, tab-list mutation, DragOrder participation or native-window
/// lifecycle work.
/// </summary>
public static partial class XjFamilyInheritanceTabPatches
{
	// Shared renderer constants retained for XjFamilyInheritanceGenealogyRenderer.
	private const int XuanJianVNextFamilyInheritanceSectionCount = 17;
	private const float XuanJianVNextFamilyInheritanceTextCellWidth = XjIconShelfRenderer.DefaultContentWidth;
	private const float XuanJianVNextFamilyInheritanceIconCellSize = XjIconShelfRenderer.DefaultIconCellSize;
	private const float XuanJianVNextFamilyInheritanceRowSpacing = XjIconShelfRenderer.DefaultRowSpacing;
	private const int XuanJianVNextFamilyInheritanceIconColumns = XjIconShelfRenderer.DefaultIconColumns;
	private static UiUnitAvatarElement XuanJianVNextFamilyAvatarPrefab;

	internal static bool XuanJianVNext_TryAddOrUpdateFamilyInheritanceTab(UnitWindow window)
	{
		// Native UnitWindow tabs are WorldBox-owned. Family inheritance is shown only
		// on XuanJian-owned surfaces.
		return false;
	}

	internal static void XuanJianVNext_RemoveFamilyInheritanceTab(UnitWindow window)
	{
		// Compatibility no-op. Never remove/destroy a native WindowMetaTab while its
		// DragOrderContainer or closing tween may still be using it.
	}

	// Kept because the XuanJian-owned family renderer uses the borrowed genealogy
	// visual grammar. This changes only the cloned, XuanJian-owned content tree.
	internal static void XuanJianVNext_ReplaceBorrowedFamilyInheritanceHeader(
		Transform content,
		List<Transform> sections)
	{
		if (content == null)
		{
			return;
		}

		Text[] texts = content.GetComponentsInChildren<Text>(includeInactive: true);
		Text title = null;
		for (int i = 0; i < texts.Length; i++)
		{
			Text candidate = texts[i];
			if (candidate == null || XuanJianVNext_IsInsideAnySection(candidate.transform, sections))
			{
				continue;
			}

			if (title == null
				|| string.Equals(candidate.text, "家谱", StringComparison.Ordinal)
				|| string.Equals(candidate.gameObject.name, "Title", StringComparison.OrdinalIgnoreCase))
			{
				title = candidate;
			}
		}

		if (title != null)
		{
			XuanJianVNext_ApplyFamilyInheritanceTitle(
				title,
				XjFamilyInheritanceTabFormatter.GetTabTitle(),
				Resources.GetBuiltinResource<Font>("Arial.ttf"));
		}
	}

	private static bool XuanJianVNext_IsInsideAnySection(Transform candidate, List<Transform> sections)
	{
		if (candidate == null || sections == null)
		{
			return false;
		}

		for (int i = 0; i < sections.Count; i++)
		{
			if (XuanJianVNext_IsDescendantOf(candidate, sections[i]))
			{
				return true;
			}
		}
		return false;
	}
}
