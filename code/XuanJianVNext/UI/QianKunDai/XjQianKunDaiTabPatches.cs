using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Family;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.QianKunDai;

internal static class XjQianKunDaiTabPatches
{
	private const string TabId = "xuanjian_vnext_qiankundai_tab";
	private const string RootName = "XjQianKunDaiTabRoot";
	private const int RefreshCadenceFrames = 40;
	private static long _lastActorId;
	private static int _lastRefreshFrame = -9999;
	private static int _lastWorldYear = -1;

	internal static bool TryAddOrUpdate(UnitWindow window)
	{
		Actor actor = window?.actor;
		ScrollWindow scrollWindow = window?.scroll_window;
		if (actor?.data == null || scrollWindow?.tabs == null || scrollWindow.transform_content == null)
		{
			return false;
		}
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor))
		{
			HideExisting(scrollWindow);
			return false;
		}

		WindowMetaTab tab = XjUiSurfaceOwnership.FindSingleTab(scrollWindow, TabId);
		Transform root = XjUiSurfaceOwnership.FindSingleRoot(
			scrollWindow.transform_content,
			RootName,
			TabId,
			nameof(XjQianKunDaiTabPatches));
		if (root != null && tab != null && !ShouldRefresh(actor))
		{
			tab.gameObject.SetActive(true);
			return true;
		}
		if (root == null)
		{
			root = XjQianKunDaiTabView.CreateTabContent(window, scrollWindow.transform_content, actor);
			if (root == null)
			{
				return false;
			}
			XjUiSurfaceOwnership.Claim(root, TabId, nameof(XjQianKunDaiTabPatches));
			MarkRefreshed(actor);
		}
		else if (ShouldRefresh(actor))
		{
			XjQianKunDaiTabView.RefreshContent(root, actor);
			MarkRefreshed(actor);
		}

		bool refill = false;
		if (tab == null)
		{
			tab = CloneBaseTab(window);
			if (tab == null)
			{
				return false;
			}
			refill = true;
		}

		XjFamilyInheritanceTabPatches.XuanJianVNext_AssignFamilyInheritanceTabContent(tab, scrollWindow, root);
		Configure(tab, window, scrollWindow);
		tab.gameObject.SetActive(true);
		if (refill)
		{
			XjFamilyInheritanceTabPatches.XuanJianVNext_RefillFamilyInheritanceTabs(scrollWindow);
		}
		XjUnitWindowTabLayoutGuard.Apply(scrollWindow);
		return true;
	}

	private static bool ShouldRefresh(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int frame = Time.frameCount;
		int worldYear = World.world?.map_stats?.year ?? 0;
		return actorId != _lastActorId
			|| worldYear != _lastWorldYear
			|| frame - _lastRefreshFrame >= RefreshCadenceFrames;
	}

	private static void MarkRefreshed(Actor actor)
	{
		_lastActorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		_lastWorldYear = World.world?.map_stats?.year ?? 0;
		_lastRefreshFrame = Time.frameCount;
	}

	private static void HideExisting(ScrollWindow scrollWindow)
	{
		WindowMetaTab tab = XjUiSurfaceOwnership.FindSingleTab(scrollWindow, TabId);
		if (tab != null)
		{
			tab.gameObject.SetActive(false);
		}
		Transform root = XjUiSurfaceOwnership.FindSingleRoot(scrollWindow.transform_content, RootName, TabId, nameof(XjQianKunDaiTabPatches));
		if (root != null)
		{
			root.gameObject.SetActive(false);
		}
		XjUnitWindowTabLayoutGuard.Apply(scrollWindow);
	}

	private static WindowMetaTab CloneBaseTab(UnitWindow window)
	{
		WindowMetaTab baseTab = XjFamilyInheritanceTabPatches.XuanJianVNext_GetFamilyInheritanceBaseTab(window);
		if (baseTab == null || window?.scroll_window?.tabs == null)
		{
			return null;
		}

		GameObject source = baseTab.gameObject;
		bool wasActive = source.activeSelf;
		source.SetActive(false);
		GameObject clone = UnityEngine.Object.Instantiate(source, window.scroll_window.tabs.transform);
		source.SetActive(wasActive);
		clone.SetActive(false);
		clone.name = TabId;
		WindowMetaTab tab = clone.GetComponent<WindowMetaTab>();
		if (tab != null)
		{
			tab.name = TabId;
			tab.tab_action?.RemoveAllListeners();
			tab.tab_elements = new List<Transform>();
			if (window.scroll_window.tabs._tabs != null && !window.scroll_window.tabs._tabs.Contains(tab))
			{
				window.scroll_window.tabs._tabs.Add(tab);
			}
		}
		return tab;
	}

	private static void Configure(WindowMetaTab tab, UnitWindow window, ScrollWindow scrollWindow)
	{
		if (tab == null || window == null || scrollWindow?.tabs == null)
		{
			return;
		}

		if (tab._tip_button != null)
		{
			XjNativeHoverTooltip.Ensure(tab._tip_button, "乾坤袋", "乾坤袋", string.Empty);
		}

		Transform iconRoot = tab.transform.Find("Icon");
		Image image = iconRoot != null ? iconRoot.GetComponent<Image>() : null;
		if (image != null)
		{
			image.sprite = SpriteTextureLoader.getSprite("ui/QianKunDai")
				?? SpriteTextureLoader.getSprite("ui/QianKunDai.png")
				?? SpriteTextureLoader.getSprite("ui/icons/iconBag")
				?? SpriteTextureLoader.getSprite("ui/icons/iconInventory");
		}

		tab.tab_action?.RemoveAllListeners();
		tab.tab_action = new WindowMetaTabEvent();
		((UnityEvent<WindowMetaTab>)(object)tab.tab_action).AddListener((UnityAction<WindowMetaTab>)delegate(WindowMetaTab currentTab)
		{
			Transform root = scrollWindow.transform_content?.Find(RootName);
			if (root != null)
			{
				XjQianKunDaiTabView.RefreshContent(root, window.actor);
			}
			scrollWindow.tabs.showTab(currentTab);
		});

		if (tab.transform.parent != scrollWindow.tabs.transform)
		{
			tab.transform.SetParent(scrollWindow.tabs.transform, false);
		}
		tab.transform.localScale = Vector3.one;
		PositionBeforeGenealogy(tab.transform, scrollWindow);
	}

	private static void PositionBeforeGenealogy(Transform tabTransform, ScrollWindow scrollWindow)
	{
		Transform tabsRoot = scrollWindow?.tabs?.transform;
		if (tabsRoot == null || tabTransform == null)
		{
			return;
		}
		for (int i = 0; i < tabsRoot.childCount; i++)
		{
			Transform child = tabsRoot.GetChild(i);
			if (child != null && child != tabTransform && child.name.IndexOf("genealogy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				tabTransform.SetSiblingIndex(Mathf.Clamp(i, 0, tabsRoot.childCount - 1));
				return;
			}
		}
	}
}
