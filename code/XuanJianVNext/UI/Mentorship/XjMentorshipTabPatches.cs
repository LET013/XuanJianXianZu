using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Family;

namespace XuanJianVNext.UI.Mentorship;

internal static class XjMentorshipTabPatches
{
	private const string TabId = "xuanjian_vnext_mentorship_tab";
	private const string RootName = "XjMentorshipTabRoot";
	private const int RefreshCadenceFrames = 40;
	private static long _lastActorId;
	private static int _lastRefreshFrame = -9999;
	private static int _lastWorldYear = -1;

	internal static bool TryAddOrUpdate(UnitWindow window)
	{
		try
		{
			return TryAddOrUpdateCore(window);
		}
		catch (Exception ex)
		{
			ScrollWindow scrollWindow = window?.scroll_window;
			if (scrollWindow != null)
			{
				HideExisting(scrollWindow);
			}
			Debug.LogWarning("[玄鉴][师徒传承] 页面注入失败，已安全降级：" + ex.GetType().Name);
			return false;
		}
	}

	private static bool TryAddOrUpdateCore(UnitWindow window)
	{
		Actor actor = window?.actor;
		ScrollWindow scrollWindow = window?.scroll_window;
		if (actor?.data == null || scrollWindow?.tabs == null || scrollWindow.transform_content == null)
		{
			return false;
		}

		if (!ShouldShow(actor))
		{
			HideExisting(scrollWindow);
			return false;
		}

		WindowMetaTab tab = XjUiSurfaceOwnership.FindSingleTab(scrollWindow, TabId);
		Transform root = XjUiSurfaceOwnership.FindSingleRoot(
			scrollWindow.transform_content,
			RootName,
			TabId,
			nameof(XjMentorshipTabPatches));
		if (root != null && tab != null && !ShouldRefresh(actor))
		{
			ApplyMentorshipTabLabel(tab.gameObject);
			tab.gameObject.SetActive(true);
			return true;
		}
		if (root == null)
		{
			root = XjMentorshipTabView.CreateTabContent(window, scrollWindow.transform_content, actor);
			if (root == null)
			{
				return false;
			}
			XjUiSurfaceOwnership.Claim(root, TabId, nameof(XjMentorshipTabPatches));
			MarkRefreshed(actor);
		}
		else if (ShouldRefresh(actor))
		{
			XjMentorshipTabView.RefreshContent(root, actor);
			MarkRefreshed(actor);
		}

		bool refill = false;
		if (tab == null)
		{
			tab = CloneBaseTab(window);
			if (tab == null)
			{
				root.gameObject.SetActive(false);
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

	private static bool ShouldShow(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		// 师徒关系是宗门社会关系，不应被资质特质完整性拦截。
		// 旧档角色即使资质投影缺失，只要仍有师承记录也应能打开页面。
		return XjZongMenMentorshipSystem.HasRecordedRelation(actor)
			|| XjZongMenMentorshipSystem.TryGetTeacher(actor, out _)
			|| XjZongMenMentorshipSystem.GetLiveStudents(actor).Count > 0;
	}

	private static void HideExisting(ScrollWindow scrollWindow)
	{
		WindowMetaTab tab = XjUiSurfaceOwnership.FindSingleTab(scrollWindow, TabId);
		if (tab != null)
		{
			tab.gameObject.SetActive(false);
		}
		Transform root = XjUiSurfaceOwnership.FindSingleRoot(scrollWindow.transform_content, RootName, TabId, nameof(XjMentorshipTabPatches));
		if (root != null)
		{
			root.gameObject.SetActive(false);
		}
		XjUnitWindowTabLayoutGuard.Apply(scrollWindow);
	}

	private static WindowMetaTab CloneBaseTab(UnitWindow window)
	{
		WindowMetaTab baseTab = XjFamilyInheritanceTabPatches.XuanJianVNext_GetFamilyInheritanceBaseTab(window);
		if (baseTab == null && window?.scroll_window?.tabs?._tabs != null)
		{
			for (int i = 0; i < window.scroll_window.tabs._tabs.Count; i++)
			{
				WindowMetaTab candidate = window.scroll_window.tabs._tabs[i];
				if (candidate != null && candidate.gameObject != null)
				{
					baseTab = candidate;
					break;
				}
			}
		}
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
		ApplyMentorshipTabLabel(clone);
		EnsureMentorshipTooltips(clone);
		StripClonedDragRuntimeComponents(clone);
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

	private static void ApplyMentorshipTabLabel(GameObject root)
	{
		if (root == null) return;
		LocalizedText[] localizedTexts = root.GetComponentsInChildren<LocalizedText>(true);
		for (int i = 0; i < localizedTexts.Length; i++)
		{
			if (localizedTexts[i] != null) localizedTexts[i].enabled = false;
		}

		Text[] labels = root.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < labels.Length; i++)
		{
			Text label = labels[i];
			if (label == null || label.transform.name.IndexOf("Icon", StringComparison.OrdinalIgnoreCase) >= 0) continue;
			if (!string.IsNullOrWhiteSpace(label.text)
				|| label.transform.name.IndexOf("Text", StringComparison.OrdinalIgnoreCase) >= 0
				|| label.transform.name.IndexOf("Label", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				label.text = "师徒传承";
				break;
			}
		}
	}

	private static void EnsureMentorshipTooltips(GameObject root)
	{
		if (root == null)
		{
			return;
		}

		TipButton[] tips = root.GetComponentsInChildren<TipButton>(true);
		for (int i = 0; i < tips.Length; i++)
		{
			XjNativeHoverTooltip.Ensure(tips[i], "师徒传承", "显示这个角色的师承与门下弟子。", string.Empty);
		}
	}

	private static void StripClonedDragRuntimeComponents(GameObject clone)
	{
		if (clone == null)
		{
			return;
		}

		// A native tab that has already entered Start() carries runtime-only drag
		// state (DragOrderElement + Canvas + GraphicRaycaster). Cloning that object
		// copies the Canvas but not DragOrderElement's private runtime references.
		// When the clone starts, DragOrderElement tries to add a second Canvas and
		// remains half-initialized, which makes DragOrderContainer throw every frame.
		// The mentorship tab does not need native drag-reordering, so remove the copied
		// drag component before activation. Remove dependants before Canvas to satisfy
		// Unity's component dependency rules.
		DragOrderElement[] dragElements = clone.GetComponents<DragOrderElement>();
		for (int i = dragElements.Length - 1; i >= 0; i--)
		{
			UnityEngine.Object.DestroyImmediate(dragElements[i]);
		}

		GraphicRaycaster[] raycasters = clone.GetComponents<GraphicRaycaster>();
		for (int i = raycasters.Length - 1; i >= 0; i--)
		{
			UnityEngine.Object.DestroyImmediate(raycasters[i]);
		}

		Canvas[] canvases = clone.GetComponents<Canvas>();
		for (int i = canvases.Length - 1; i >= 0; i--)
		{
			UnityEngine.Object.DestroyImmediate(canvases[i]);
		}

		// Recreate the drag component while the clone is still inactive. This gives
		// DragOrderElement a normal Start() path with no copied Canvas and preserves
		// the native tab reorder contract expected by DragOrderContainer.
		clone.AddComponent<DragOrderElement>();
	}

	private static void Configure(WindowMetaTab tab, UnitWindow window, ScrollWindow scrollWindow)
	{
		if (tab == null || window == null || scrollWindow?.tabs == null)
		{
			return;
		}

		ApplyMentorshipTabLabel(tab.gameObject);
		EnsureMentorshipTooltips(tab.gameObject);

		Transform iconRoot = tab.transform.Find("Icon");
		Image image = iconRoot != null ? iconRoot.GetComponent<Image>() : null;
		if (image != null)
		{
			image.sprite = SpriteTextureLoader.getSprite("ui/Icons/items/ZongMenJiShi")
				?? SpriteTextureLoader.getSprite("ui/icons/iconCulture")
				?? SpriteTextureLoader.getSprite("ui/icons/iconPeople");
		}

		tab.tab_action?.RemoveAllListeners();
		tab.tab_action = new WindowMetaTabEvent();
		((UnityEvent<WindowMetaTab>)(object)tab.tab_action).AddListener((UnityAction<WindowMetaTab>)delegate(WindowMetaTab currentTab)
		{
			Transform root = XjUiSurfaceOwnership.FindSingleRoot(
				scrollWindow.transform_content,
				RootName,
				TabId,
				nameof(XjMentorshipTabPatches));
			if (root == null && currentTab?.tab_elements != null && currentTab.tab_elements.Count > 0)
			{
				root = currentTab.tab_elements[0];
			}
			if (root != null)
			{
				XjMentorshipTabView.RefreshContent(root, window.actor);
			}
			scrollWindow.tabs.showTab(currentTab);
		});
		BindTabButtonClick(tab, scrollWindow);

		if (tab.transform.parent != scrollWindow.tabs.transform)
		{
			tab.transform.SetParent(scrollWindow.tabs.transform, false);
		}
		tab.transform.localScale = Vector3.one;
		PositionNearFamilyTabs(tab.transform, scrollWindow);
	}

	private static void BindTabButtonClick(WindowMetaTab tab, ScrollWindow scrollWindow)
	{
		if (tab == null || scrollWindow?.tabs == null)
		{
			return;
		}

		Button button = tab.GetComponent<Button>() ?? tab.gameObject.AddComponent<Button>();
		Graphic target = tab.GetComponent<Graphic>();
		if (target != null)
		{
			button.targetGraphic = target;
		}
		button.onClick.RemoveAllListeners();
		button.onClick.AddListener(delegate
		{
			if (tab.tab_action != null)
			{
				tab.tab_action.Invoke(tab);
			}
			else
			{
				scrollWindow.tabs.showTab(tab);
			}
		});
	}

	private static void PositionNearFamilyTabs(Transform tabTransform, ScrollWindow scrollWindow)
	{
		Transform tabsRoot = scrollWindow?.tabs?.transform;
		if (tabsRoot == null || tabTransform == null)
		{
			return;
		}

		for (int i = 0; i < tabsRoot.childCount; i++)
		{
			Transform child = tabsRoot.GetChild(i);
			if (child != null
				&& child != tabTransform
				&& child.name.IndexOf("qiankundai", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				tabTransform.SetSiblingIndex(Mathf.Clamp(i + 1, 0, tabsRoot.childCount - 1));
				return;
			}
		}

		for (int i = 0; i < tabsRoot.childCount; i++)
		{
			Transform child = tabsRoot.GetChild(i);
			if (child != null
				&& child != tabTransform
				&& child.name.IndexOf("genealogy", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				tabTransform.SetSiblingIndex(Mathf.Clamp(i, 0, tabsRoot.childCount - 1));
				return;
			}
		}
	}
}

