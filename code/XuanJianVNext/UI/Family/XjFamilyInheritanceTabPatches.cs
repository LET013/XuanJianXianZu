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

namespace XuanJianVNext.UI.Family;

public static partial class XjFamilyInheritanceTabPatches
{
	private const string XuanJianVNextFamilyInheritanceTabId = "xuanjian_vnext_family_inheritance_tab";

	private const string XuanJianVNextFamilyInheritanceTabRootName = "XjFamilyInheritanceTabRoot";

	// 5境界 + 纳气/功法/求金法/采气法/法宝/灵物 + 药材/丹药/丹方 + 符箓/阵法 + 1纪事。
	private const int XuanJianVNextFamilyInheritanceSectionCount = 17;
	private const float XuanJianVNextFamilyInheritanceTextCellWidth = XjIconShelfRenderer.DefaultContentWidth;
	private const float XuanJianVNextFamilyInheritanceIconCellSize = XjIconShelfRenderer.DefaultIconCellSize;
	private const float XuanJianVNextFamilyInheritanceRowSpacing = XjIconShelfRenderer.DefaultRowSpacing;
	private const int XuanJianVNextFamilyInheritanceIconColumns = XjIconShelfRenderer.DefaultIconColumns;
	private static UiUnitAvatarElement XuanJianVNextFamilyAvatarPrefab;
	internal static bool XuanJianVNext_TryAddOrUpdateFamilyInheritanceTab(UnitWindow window)
	{
		if (window == null)
		{
			return false;
		}
		Actor obj = window.actor;
		ScrollWindow val = ((TabbedWindow)window).scroll_window;
		if (obj?.data == null || (UnityEngine.Object)(object)val?.tabs == null || val.transform_content == null)
		{
			return false;
		}
		WindowMetaTab val2 = XjUiSurfaceOwnership.FindSingleTab(val, XuanJianVNextFamilyInheritanceTabId);
		Transform transform = XjUiSurfaceOwnership.FindSingleRoot(
			val.transform_content,
			XuanJianVNextFamilyInheritanceTabRootName,
			XuanJianVNextFamilyInheritanceTabId,
			nameof(XjFamilyInheritanceTabPatches));
		if (transform == null)
		{
			transform = XuanJianVNext_CreateFamilyInheritanceContent(window);
			if (transform == null)
			{
				return false;
			}
			XjUiSurfaceOwnership.Claim(
				transform,
				XuanJianVNextFamilyInheritanceTabId,
				nameof(XjFamilyInheritanceTabPatches));
		}
		else
		{
			XuanJianVNext_RefreshFamilyInheritanceContent(window, transform);
		}
		if ((UnityEngine.Object)(object)val2 == null)
		{
			val2 = XuanJianVNext_CloneFamilyInheritanceBaseTab(window);
			if ((UnityEngine.Object)(object)val2 == null)
			{
				return false;
			}
			((UnityEngine.Object)(object)val2).name = "xuanjian_vnext_family_inheritance_tab";
			if (val.tabs._tabs != null && !val.tabs._tabs.Contains(val2))
			{
				val.tabs._tabs.Add(val2);
			}
			XuanJianVNext_AssignFamilyInheritanceTabContent(val2, val, transform);
		}
		else
		{
			XuanJianVNext_EnsureFamilyInheritanceSingleContent(val2, val, transform);
		}
		XuanJianVNext_ConfigureFamilyInheritanceTab(val2, window, val);
		((Component)(object)val2).gameObject.SetActive(true);
		XjUnitWindowTabLayoutGuard.Apply(val);
		return true;
	}

	internal static void XuanJianVNext_RemoveFamilyInheritanceTab(UnitWindow window)
	{
		if (window == null)
		{
			return;
		}

		ScrollWindow scrollWindow = ((TabbedWindow)window).scroll_window;
		if (scrollWindow?.tabs?._tabs != null)
		{
			for (int i = scrollWindow.tabs._tabs.Count - 1; i >= 0; i--)
			{
				WindowMetaTab tab = scrollWindow.tabs._tabs[i];
				if (tab == null)
				{
					scrollWindow.tabs._tabs.RemoveAt(i);
					continue;
				}

				if (!string.Equals(tab.name, XuanJianVNextFamilyInheritanceTabId, StringComparison.Ordinal))
				{
					continue;
				}

				scrollWindow.tabs._tabs.RemoveAt(i);
				UnityEngine.Object.DestroyImmediate(tab.gameObject);
			}
		}

		Transform content = scrollWindow?.transform_content;
		if (content != null)
		{
			for (int i = content.childCount - 1; i >= 0; i--)
			{
				Transform child = content.GetChild(i);
				if (child != null && string.Equals(child.name, XuanJianVNextFamilyInheritanceTabRootName, StringComparison.Ordinal))
				{
					UnityEngine.Object.DestroyImmediate(child.gameObject);
				}
			}
		}

		if (scrollWindow != null)
		{
			XjUnitWindowTabLayoutGuard.Apply(scrollWindow);
		}
	}

	internal static WindowMetaTab XuanJianVNext_FindFamilyInheritanceTab(ScrollWindow scrollWindow)
	{
		return XjUiSurfaceOwnership.FindSingleTab(scrollWindow, XuanJianVNextFamilyInheritanceTabId);
	}

	internal static WindowMetaTab XuanJianVNext_CloneFamilyInheritanceBaseTab(UnitWindow window)
	{
		WindowMetaTab val = XuanJianVNext_GetFamilyInheritanceBaseTab(window);
		if ((UnityEngine.Object)(object)val == null || window == null)
		{
			return null;
		}
		ScrollWindow scrollWindow = ((TabbedWindow)window).scroll_window;
		if ((UnityEngine.Object)(object)scrollWindow?.tabs == null)
		{
			return null;
		}
		GameObject source = ((Component)(object)val).gameObject;
		bool wasActive = source.activeSelf;
		source.SetActive(false);
		GameObject gameObject = UnityEngine.Object.Instantiate(source, ((Component)(object)((TabbedWindow)window).scroll_window.tabs).transform);
		source.SetActive(wasActive);
		gameObject.SetActive(false);
		gameObject.name = "xuanjian_vnext_family_inheritance_tab";
		WindowMetaTab component = gameObject.GetComponent<WindowMetaTab>();
		if ((UnityEngine.Object)(object)component != null)
		{
			component.tab_action?.RemoveAllListeners();
			component.tab_elements = new List<Transform>();
		}
		return component;
	}

	internal static WindowMetaTab XuanJianVNext_GetFamilyInheritanceBaseTab(UnitWindow window)
	{
		if ((UnityEngine.Object)(object)window == null)
		{
			return null;
		}
		Transform transform = ((Component)(object)window).transform.Find("Background/Tabs/Genealogy");
		WindowMetaTab val = ((transform != null) ? transform.GetComponent<WindowMetaTab>() : null);
		if ((UnityEngine.Object)(object)val != null)
		{
			return val;
		}
		Transform transform2 = ((Component)(object)window).transform.Find("Background/Tabs");
		if (transform2 == null)
		{
			return null;
		}
		for (int i = 0; i < transform2.childCount; i++)
		{
			Transform child = transform2.GetChild(i);
			if (child != null && child.name.IndexOf("genealogy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				val = child.GetComponent<WindowMetaTab>();
				if ((UnityEngine.Object)(object)val != null)
				{
					return val;
				}
			}
		}
		return null;
	}

	internal static Transform XuanJianVNext_CreateFamilyInheritanceContent(UnitWindow window)
	{
		Transform transform = XuanJianVNext_CreateFamilyInheritanceGenealogyContent(window);
		if (transform != null)
		{
			XuanJianVNext_RefreshFamilyInheritanceContent(window, transform);
			return transform;
		}
		return null;
	}

	internal static Transform XuanJianVNext_CreateFamilyInheritanceGenealogyContent(UnitWindow window)
	{
		if (!XjNativeGenealogyTemplateProvider.TryClone(
			window,
			null,
			XuanJianVNextFamilyInheritanceTabRootName,
			XuanJianVNextFamilyInheritanceSectionCount,
			out XjNativeGenealogyClone clone))
		{
			return null;
		}

		RectTransform contentRect = clone.Root.GetComponent<RectTransform>();
		if (contentRect != null)
		{
			contentRect.anchorMin = new Vector2(0.5f, 1f);
			contentRect.anchorMax = new Vector2(0.5f, 1f);
			contentRect.pivot = new Vector2(0.5f, 1f);
		}
		XuanJianVNext_CacheFamilyAvatarPrefab(clone.Root, clone.AvatarPrefab);
		XuanJianVNext_ConfigureBorrowedFamilyInheritanceSections(clone.Root);
		return clone.Root;
	}

	internal static void XuanJianVNext_ConfigureBorrowedFamilyInheritanceSections(Transform content)
	{
		if (!(content == null))
		{
			List<Transform> list = XjNativeGenealogyTemplateProvider.CollectSectionRoots(content, null);
			XjNativeGenealogyTemplateProvider.EnsureSectionCount(list, XuanJianVNextFamilyInheritanceSectionCount);
			XjNativeGenealogyTemplateProvider.DisableLocalizedText(content);
			for (int i = 0; i < list.Count && i < XuanJianVNextFamilyInheritanceSectionCount; i++)
			{
				list[i].gameObject.SetActive(value: true);
			}
			XuanJianVNext_ReplaceBorrowedFamilyInheritanceHeader(content, list);
		}
	}

	internal static void XuanJianVNext_ReplaceBorrowedFamilyInheritanceHeader(Transform content, List<Transform> sections)
	{
		if (content == null)
		{
			return;
		}
		Text[] componentsInChildren = content.GetComponentsInChildren<Text>(includeInactive: true);
		Text text = null;
		foreach (Text text2 in componentsInChildren)
		{
			if (!(text2 == null) && !XuanJianVNext_IsInsideAnySection(text2.transform, sections) && (text == null || string.Equals(text2.text, "家谱", StringComparison.Ordinal) || string.Equals(text2.gameObject.name, "Title", StringComparison.OrdinalIgnoreCase)))
			{
				text = text2;
			}
		}
		if (text != null)
		{
			XuanJianVNext_ApplyFamilyInheritanceTitle(text, XjFamilyInheritanceTabFormatter.GetTabTitle(), Resources.GetBuiltinResource<Font>("Arial.ttf"));
		}
	}

	internal static bool XuanJianVNext_IsInsideAnySection(Transform transform, List<Transform> sections)
	{
		if (transform == null || sections == null)
		{
			return false;
		}
		for (int i = 0; i < sections.Count; i++)
		{
			if (XuanJianVNext_IsDescendantOf(transform, sections[i]))
			{
				return true;
			}
		}
		return false;
	}



internal static bool XuanJianVNext_EnsureFamilyInheritanceSingleContent(WindowMetaTab tab, ScrollWindow scrollWindow, Transform content)
	{
		if ((UnityEngine.Object)(object)tab == null || scrollWindow?.transform_content == null || content == null)
		{
			return false;
		}
		if (tab.tab_elements != null)
		{
			int num = 0;
			bool flag = false;
			for (int i = 0; i < tab.tab_elements.Count; i++)
			{
				Transform transform = tab.tab_elements[i];
				if (!(transform == null))
				{
					num++;
					if (transform == content)
					{
						flag = true;
					}
				}
			}
			if (flag && num == 1)
			{
				if (content.parent != scrollWindow.transform_content)
				{
					content.SetParent(scrollWindow.transform_content, worldPositionStays: false);
				}
				return false;
			}
		}
		XuanJianVNext_AssignFamilyInheritanceTabContent(tab, scrollWindow, content);
		return true;
	}

	internal static void XuanJianVNext_CacheFamilyAvatarPrefab(Transform content, UiUnitAvatarElement source)
	{
		if (content == null || (UnityEngine.Object)(object)source == null)
		{
			return;
		}

		Transform existing = content.Find("XjFamilyMemberAvatarTemplate");
		UiUnitAvatarElement template = existing == null ? null : existing.GetComponent<UiUnitAvatarElement>();
		if ((UnityEngine.Object)(object)template == null)
		{
			template = UnityEngine.Object.Instantiate(source, content);
			((Component)(object)template).gameObject.name = "XjFamilyMemberAvatarTemplate";
			LayoutElement layout = ((Component)(object)template).gameObject.GetComponent<LayoutElement>()
				?? ((Component)(object)template).gameObject.AddComponent<LayoutElement>();
			layout.ignoreLayout = true;
			((Component)(object)template).gameObject.SetActive(false);
		}

		XuanJianVNextFamilyAvatarPrefab = template;
	}

	internal static void XuanJianVNext_AssignFamilyInheritanceTabContent(WindowMetaTab tab, ScrollWindow scrollWindow, Transform content)
	{
		if (!((UnityEngine.Object)(object)tab == null) && !((UnityEngine.Object)(object)scrollWindow?.tabs == null) && !(scrollWindow.transform_content == null) && !(content == null))
		{
			tab.container = scrollWindow.tabs;
			if (tab.tab_elements == null)
			{
				tab.tab_elements = new List<Transform>();
			}
			else
			{
				tab.tab_elements.Clear();
			}
			content.SetParent(scrollWindow.transform_content, worldPositionStays: false);
			content.gameObject.SetActive(value: true);
			content.localScale = Vector3.one;
			tab.tab_elements.Add(content);
		}
	}

	internal static void XuanJianVNext_ConfigureFamilyInheritanceTab(WindowMetaTab tab, UnitWindow window, ScrollWindow scrollWindow)
	{
		if ((UnityEngine.Object)(object)tab == null || (UnityEngine.Object)(object)window == null || (UnityEngine.Object)(object)scrollWindow?.tabs == null)
		{
			return;
		}
		if ((UnityEngine.Object)(object)tab._tip_button != null)
		{
			XjNativeHoverTooltip.Ensure(tab._tip_button, "家族传承", "家族传承", string.Empty);
		}
		Transform transform = ((Component)(object)tab).transform.Find("Icon");
		if (transform != null)
		{
			Image component = transform.GetComponent<Image>();
			if (component != null)
			{
				component.sprite = SpriteTextureLoader.getSprite("ui/XuanJian") ?? SpriteTextureLoader.getSprite("ui/icons/iconJianBook");
			}
		}
		tab.tab_action?.RemoveAllListeners();
		tab.tab_action = new WindowMetaTabEvent();
		((UnityEvent<WindowMetaTab>)(object)tab.tab_action).AddListener((UnityAction<WindowMetaTab>)delegate(WindowMetaTab currentTab)
		{
			if ((UnityEngine.Object)(object)currentTab != null)
			{
				Transform content = currentTab.tab_elements != null && currentTab.tab_elements.Count > 0
					? currentTab.tab_elements[0]
					: null;
				if (content != null)
				{
					XuanJianVNext_RefreshFamilyInheritanceContent(window, content);
				}
				try
				{
					scrollWindow.tabs.showTab(currentTab);
				}
				catch
				{
				}
			}
		});
		if (((Component)(object)tab).transform.parent != ((Component)(object)scrollWindow.tabs).transform)
		{
			((Component)(object)tab).transform.SetParent(((Component)(object)scrollWindow.tabs).transform, worldPositionStays: false);
		}
		((Component)(object)tab).transform.localScale = Vector3.one;
		XuanJianVNext_PositionFamilyInheritanceTab(((Component)(object)tab).transform, scrollWindow);
	}

	internal static void XuanJianVNext_PositionFamilyInheritanceTab(Transform tabTransform, ScrollWindow scrollWindow)
	{
		Transform transform = ((Component)(object)scrollWindow?.tabs)?.transform;
		if (transform == null || tabTransform == null)
		{
			return;
		}
		int num = -1;
		for (int i = 0; i < transform.childCount; i++)
		{
			Transform child = transform.GetChild(i);
			if (child != null && child.name.IndexOf("genealogy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				num = i;
				break;
			}
		}
		if (num >= 0)
		{
			tabTransform.SetSiblingIndex(Mathf.Clamp(num + 1, 0, transform.childCount - 1));
		}
	}

	internal static void XuanJianVNext_RefillFamilyInheritanceTabs(ScrollWindow scrollWindow)
	{
		if ((UnityEngine.Object)(object)scrollWindow?.tabs == null)
		{
			return;
		}
		try
		{
			scrollWindow.tabs.refillTabsWithContent();
		}
		catch
		{
		}
		XjUnitWindowTabLayoutGuard.Apply(scrollWindow);
	}
}
