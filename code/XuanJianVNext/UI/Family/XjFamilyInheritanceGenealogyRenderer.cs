using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

namespace XuanJianVNext.UI.Family;

public static partial class XjFamilyInheritanceTabPatches
{
	internal static void XuanJianVNext_RefreshFamilyInheritanceContent(UnitWindow window, Transform content)
	{
		if (!(content == null))
		{
			List<Transform> list = XjNativeGenealogyTemplateProvider.CollectSectionRoots(content, null);
			if (list.Count > 0)
			{
				XjNativeGenealogyTemplateProvider.DisableLocalizedText(content);
				XuanJianVNext_ReplaceBorrowedFamilyInheritanceHeader(content, list);
			}
			Transform transform = content.Find("XjFamilyInheritanceTitle");
			Text text = ((transform != null) ? transform.GetComponent<Text>() : null);
			if (text != null)
			{
				text.text = XjFamilyInheritanceTabFormatter.GetTabTitle();
			}
			XuanJianVNext_RebuildBorrowedFamilyInheritanceSections(content, (window != null) ? window.actor : null);
		}
	}

	private static void XuanJianVNext_ApplyFamilyGenealogySafetyInset(Transform content)
	{
		if (content == null) return;
		RectTransform rect = content as RectTransform ?? content.GetComponent<RectTransform>();
		if (rect == null) return;
		// ConvergeLayout rewrites the content size on every refresh. Apply a small
		// non-accumulating right/down safety inset afterwards so the final row is
		// not clipped by the native frame.
		rect.anchoredPosition = new Vector2(8f, rect.anchoredPosition.y);
		rect.sizeDelta = new Vector2(rect.sizeDelta.x, rect.sizeDelta.y + 12f);
	}

	internal static void XuanJianVNext_RebuildBorrowedFamilyInheritanceSections(Transform content, Actor actor)
	{
		if (content == null)
		{
			return;
		}
		List<Transform> list = XjNativeGenealogyTemplateProvider.CollectSectionRoots(content, null);
		XjNativeGenealogyTemplateProvider.EnsureSectionCount(list, XuanJianVNextFamilyInheritanceSectionCount);
		XjFamilyInheritanceSection[] array = XjFamilyInheritanceTabFormatter.BuildSectionsForActor(actor);
		Font builtinResource = Resources.GetBuiltinResource<Font>("Arial.ttf");
		for (int i = 0; i < list.Count; i++)
		{
			bool flag = i < array.Length;
			list[i].gameObject.SetActive(flag);
			if (flag)
			{
				try
				{
					XuanJianVNext_RebuildBorrowedFamilyInheritanceSection(list[i], array[i], builtinResource, actor);
				}
				catch (Exception exception)
				{
					Debug.LogWarning("[玄鉴][家族传承] 重建栏目失败，已降级显示：" + exception);
					XuanJianVNext_RebuildFamilyInheritanceFallbackSection(list[i], array[i].Title, builtinResource);
				}
			}
		}
		XjNativeGenealogyTemplateProvider.ConvergeLayout(content, list, array.Length);
		XuanJianVNext_ApplyFamilyGenealogySafetyInset(content);
	}

	internal static void XuanJianVNext_RebuildBorrowedFamilyInheritanceSection(Transform sectionRoot, XjFamilyInheritanceSection section, Font font, Actor actor)
	{
		if (sectionRoot == null)
		{
			return;
		}
		sectionRoot.name = "bg_" + section.Title;

		bool hasCaiQi = section.CaiQiItems != null && section.CaiQiItems.Length != 0;
		bool hasGongFa = section.GongFaItems != null && section.GongFaItems.Length != 0;
		bool hasFaBao = section.FaBaoItems != null && section.FaBaoItems.Length != 0;
		bool hasLingWu = section.LingWuItems != null && section.LingWuItems.Length != 0;
		bool hasAlchemy = section.AlchemyItems != null && section.AlchemyItems.Length != 0;
		bool hasCraft = section.CraftItems != null && section.CraftItems.Length != 0;
		bool hasMembers = section.MemberItems != null && section.MemberItems.Length != 0;
		bool flag = hasCaiQi || hasGongFa || hasFaBao || hasLingWu || hasAlchemy || hasCraft || hasMembers;
		string[] array = section.Items ?? Array.Empty<string>();
		bool flag2 = array.Length != 0
			&& (array.Length != 1 || !XuanJianVNext_IsFamilyInheritanceEmptyItem(array[0]));
		int iconItemCount = hasMembers ? section.MemberItems.Length : hasGongFa ? section.GongFaItems.Length : hasFaBao ? section.FaBaoItems.Length : hasLingWu ? section.LingWuItems.Length : hasAlchemy ? section.AlchemyItems.Length : hasCraft ? section.CraftItems.Length : section.CaiQiItems.Length;
		int a = (flag ? iconItemCount : ((!flag2) ? 1 : array.Length));
		XjIconShelfMetrics metrics = XjIconShelfRenderer.CalculateMetrics(
			a,
			hasMembers || flag,
			XuanJianVNextFamilyInheritanceTextCellWidth,
			XuanJianVNextFamilyInheritanceIconColumns,
			XuanJianVNextFamilyInheritanceIconCellSize,
			XuanJianVNextFamilyInheritanceRowSpacing);
		if (!XjNativeGenealogySectionRenderer.TryPrepare(sectionRoot, section.Title, font, metrics, out Transform transform))
		{
			return;
		}
		if (hasCaiQi)
		{
			for (int i = 0; i < section.CaiQiItems.Length; i++)
			{
				XuanJianVNext_CreateFamilyCaiQiWarehouseIconCell(transform, section.CaiQiItems[i], font);
			}
			return;
		}
		if (hasMembers)
		{
			for (int i = 0; i < section.MemberItems.Length; i++)
			{
				XuanJianVNext_CreateFamilyMemberAvatarCell(transform, section.MemberItems[i]);
			}
			return;
		}
		if (hasGongFa)
		{
			for (int i = 0; i < section.GongFaItems.Length; i++)
			{
				XuanJianVNext_CreateFamilyGongFaWarehouseIconCell(transform, section.GongFaItems[i], font);
			}
			return;
		}
		if (hasFaBao)
		{
			for (int i = 0; i < section.FaBaoItems.Length; i++)
			{
				try
				{
					XuanJianVNext_CreateFamilyFaBaoWarehouseIconCell(transform, section.FaBaoItems[i], font);
				}
				catch (Exception exception)
				{
					Debug.LogWarning("[玄鉴][家族传承] 法宝格渲染失败，已用文字占位：" + exception);
					XuanJianVNext_CreateFamilyInheritanceTextCell(transform, BuildFaBaoFallbackText(section.FaBaoItems[i]), font);
				}
			}
			return;
		}
		if (hasLingWu)
		{
			for (int i = 0; i < section.LingWuItems.Length; i++)
			{
				XjLingWuUiModel.CreateIconCell(transform, section.LingWuItems[i], font);
			}
			return;
		}
		if (hasAlchemy)
		{
			for (int i = 0; i < section.AlchemyItems.Length; i++)
			{
				XjAlchemyUiModel.CreateIconCell(transform, section.AlchemyItems[i], font, "FamilyAlchemyWarehouseSlot");
			}
			return;
		}
		if (hasCraft)
		{
			for (int i = 0; i < section.CraftItems.Length; i++)
			{
				XjCraftInventoryUiModel.CreateIconCell(transform, section.CraftItems[i], font, "FamilyCraftWarehouseSlot");
			}
			return;
		}
		if (XuanJianVNext_IsFamilyChronicleSection(section.Title))
		{
			return;
		}
		if (!flag2)
		{
			return;
		}
		for (int j = 0; j < array.Length; j++)
		{
			XuanJianVNext_CreateFamilyInheritanceTextCell(transform, array[j], font);
		}
	}

	private static void XuanJianVNext_RebuildFamilyInheritanceFallbackSection(Transform sectionRoot, string title, Font font)
	{
		if (sectionRoot == null)
		{
			return;
		}

		string safeTitle = string.IsNullOrWhiteSpace(title) ? "家族传承" : title.Trim();
		sectionRoot.name = "bg_" + safeTitle;
		XjIconShelfMetrics metrics = XjIconShelfRenderer.CalculateMetrics(
			1,
			false,
			XuanJianVNextFamilyInheritanceTextCellWidth,
			XuanJianVNextFamilyInheritanceIconColumns,
			XuanJianVNextFamilyInheritanceIconCellSize,
			XuanJianVNextFamilyInheritanceRowSpacing);
		if (XjNativeGenealogySectionRenderer.TryPrepare(sectionRoot, safeTitle, font, metrics, out Transform content))
		{
			XuanJianVNext_CreateFamilyInheritanceTextCell(content, "本栏读取异常，已跳过异常数据", font);
		}
	}

	private static void XuanJianVNext_CreateFamilyInheritanceTextCell(Transform parent, string text, Font font)
	{
		if (parent == null)
		{
			return;
		}

		GameObject gameObject = new GameObject("Item", typeof(RectTransform), typeof(Text));
		gameObject.transform.SetParent(parent, worldPositionStays: false);
		Text label = gameObject.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? font;
		label.fontSize = 12;
		label.alignment = TextAnchor.MiddleCenter;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.color = Color.white;
		label.supportRichText = false;
		label.raycastTarget = false;
		label.text = string.IsNullOrWhiteSpace(text) ? "暂无" : text.Trim();
	}

	private static string BuildFaBaoFallbackText(XjFamilyFaBaoDisplayItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.FaBaoName))
		{
			return item.FaBaoName.Trim();
		}
		if (!string.IsNullOrWhiteSpace(item.ClassName))
		{
			return item.ClassName.Trim();
		}
		return "异常器物";
	}

	internal static void XuanJianVNext_CreateFamilyMemberAvatarCell(Transform parent, XjFamilyMemberDisplayItem item)
	{
		UiUnitAvatarElement prefab = XuanJianVNext_GetFamilyAvatarPrefab();
		XjNativeAvatarCellRenderer.CreateAvatarCell(parent, item, XuanJianVNextFamilyInheritanceIconCellSize, prefab);
	}

	internal static UiUnitAvatarElement XuanJianVNext_GetFamilyAvatarPrefab()
	{
		if ((UnityEngine.Object)(object)XuanJianVNextFamilyAvatarPrefab != null)
		{
			return XuanJianVNextFamilyAvatarPrefab;
		}

		UiUnitAvatarElement resourcePrefab = Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement")
			?? Resources.Load<UiUnitAvatarElement>("UnitAvatarElement");
		if ((UnityEngine.Object)(object)resourcePrefab != null)
		{
			XuanJianVNextFamilyAvatarPrefab = resourcePrefab;
			return XuanJianVNextFamilyAvatarPrefab;
		}

		return XuanJianVNextFamilyAvatarPrefab;
	}

	internal static void XuanJianVNext_ApplyFamilyInheritanceTitle(Text text, string title, Font font)
	{
		if (!(text == null))
		{
			text.font = LocalizedTextManager.current_font ?? font;
			text.fontSize = 10;
			text.alignment = TextAnchor.MiddleCenter;
			text.horizontalOverflow = HorizontalWrapMode.Overflow;
			text.verticalOverflow = VerticalWrapMode.Overflow;
			text.color = Color.white;
			text.supportRichText = false;
			text.raycastTarget = false;
			text.text = title ?? string.Empty;
		}
	}

	internal static bool XuanJianVNext_IsDescendantOf(Transform candidate, Transform ancestor)
	{
		if (candidate == null || ancestor == null)
		{
			return false;
		}
		Transform transform = candidate;
		while (transform != null)
		{
			if (transform == ancestor)
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	internal static bool XuanJianVNext_IsFamilyChronicleSection(string title)
	{
		if (!string.IsNullOrWhiteSpace(title))
		{
			return title.IndexOf("纪事", StringComparison.Ordinal) >= 0;
		}
		return false;
	}

	private static bool XuanJianVNext_IsFamilyInheritanceEmptyItem(string value)
	{
		return string.IsNullOrWhiteSpace(value)
			|| value.Trim().StartsWith("暂无", StringComparison.Ordinal);
	}

	internal static bool XuanJianVNext_IsRealmSection(string title)
	{
		return !string.IsNullOrWhiteSpace(title)
			&& (title.IndexOf("胎息", StringComparison.Ordinal) >= 0
				|| title.IndexOf("炼气", StringComparison.Ordinal) >= 0
				|| title.IndexOf("\u7ec3\u6c14", StringComparison.Ordinal) >= 0
				|| title.IndexOf("筑基", StringComparison.Ordinal) >= 0
				|| title.IndexOf("紫府", StringComparison.Ordinal) >= 0
				|| title.IndexOf("金丹", StringComparison.Ordinal) >= 0
				|| title.IndexOf("凡人", StringComparison.Ordinal) >= 0);
	}

	internal static void XuanJianVNext_ConfigureFamilyChronicleOpenButton(GameObject target, string title, Actor actor)
	{
	}

}
