using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

internal readonly struct XjNativeGenealogyClone
{
	internal readonly Transform Root;
	internal readonly List<Transform> Sections;
	internal readonly UiUnitAvatarElement AvatarPrefab;

	internal XjNativeGenealogyClone(Transform root, List<Transform> sections, UiUnitAvatarElement avatarPrefab)
	{
		Root = root;
		Sections = sections;
		AvatarPrefab = avatarPrefab;
	}
}

internal static class XjNativeGenealogyTemplateProvider
{
	internal static bool TryClone(
		UnitWindow window,
		Transform parent,
		string rootName,
		int sectionCount,
		out XjNativeGenealogyClone result)
	{
		result = default;
		UnitGenealogyElement template = FindTemplate(window);
		if (template == null)
		{
			return false;
		}

		GameObject source = template.gameObject;
		bool wasActive = source.activeSelf;
		source.SetActive(false);
		GameObject clone = parent == null
			? UnityEngine.Object.Instantiate(source)
			: UnityEngine.Object.Instantiate(source, parent);
		source.SetActive(wasActive);

		clone.name = string.IsNullOrWhiteSpace(rootName) ? "XjNativeGenealogyRoot" : rootName;
		clone.transform.localScale = Vector3.one;
		clone.SetActive(false);

		UnitGenealogyElement genealogy = clone.GetComponent<UnitGenealogyElement>();
		UiUnitAvatarElement avatarPrefab = genealogy != null
			? genealogy.prefab_avatar
			: clone.GetComponentInChildren<UiUnitAvatarElement>(true);
		Image sexIcon = genealogy != null ? genealogy._sex_icon : null;
		List<Transform> sections = CollectSectionRoots(clone.transform, genealogy);
		EnsureSectionCount(sections, sectionCount);
		HideDecorations(avatarPrefab, sexIcon);
		DisableLocalizedText(clone.transform);
		XjNativeGenealogyLayout.NormalizeRoot(clone.transform);
		if (genealogy != null)
		{
			UnityEngine.Object.DestroyImmediate(genealogy);
		}

		clone.SetActive(true);
		result = new XjNativeGenealogyClone(clone.transform, sections, avatarPrefab);
		return true;
	}

	internal static UnitGenealogyElement FindTemplate(UnitWindow window)
	{
		if (window == null)
		{
			return null;
		}

		UnitGenealogyElement direct = window.GetComponentInChildren<UnitGenealogyElement>(true);
		if (direct != null)
		{
			return direct;
		}

		Transform content = window.transform.Find("Background/Scroll View/Viewport/Content");
		return content != null ? content.GetComponentInChildren<UnitGenealogyElement>(true) : null;
	}

	internal static List<Transform> CollectSectionRoots(Transform root, UnitGenealogyElement genealogy)
	{
		List<Transform> sections = new List<Transform>(4);
		if (root == null)
		{
			return sections;
		}

		AddSectionRoot(sections, (genealogy != null ? genealogy.transform_grandparents : null) ?? root.Find("Grandparents") ?? root.Find("bg_grandparents"));
		AddSectionRoot(sections, (genealogy != null ? genealogy.transform_parents : null) ?? root.Find("Parents") ?? root.Find("bg_parents"));
		AddSectionRoot(sections, (genealogy != null ? genealogy.transform_siblings : null) ?? root.Find("Siblings") ?? root.Find("bg_siblings"));
		AddSectionRoot(sections, (genealogy != null ? genealogy.transform_children : null) ?? root.Find("Children") ?? root.Find("bg_children"));

		Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
		for (int i = 0; i < descendants.Length; i++)
		{
			Transform candidate = descendants[i];
			if (candidate != null
				&& candidate != root
				&& candidate.name.StartsWith("bg_", StringComparison.OrdinalIgnoreCase)
				&& !sections.Contains(candidate))
			{
				sections.Add(candidate);
			}
		}
		return sections;
	}

	internal static void EnsureSectionCount(List<Transform> sections, int targetCount)
	{
		if (sections == null || sections.Count == 0 || targetCount <= 0)
		{
			return;
		}

		Transform parent = sections[0].parent;
		GameObject template = sections[sections.Count - 1].gameObject;
		int firstIndex = sections[0].GetSiblingIndex();
		while (sections.Count < targetCount)
		{
			Transform clone = UnityEngine.Object.Instantiate(template, parent).transform;
			clone.localScale = Vector3.one;
			sections.Add(clone);
		}

		for (int i = 0; i < sections.Count && i < targetCount; i++)
		{
			sections[i].SetSiblingIndex(Mathf.Min(parent.childCount - 1, firstIndex + i));
		}
	}

	internal static void DisableLocalizedText(Transform root)
	{
		if (root == null)
		{
			return;
		}

		LocalizedText[] localizedTexts = root.GetComponentsInChildren<LocalizedText>(true);
		for (int i = 0; i < localizedTexts.Length; i++)
		{
			if (localizedTexts[i] != null)
			{
				localizedTexts[i].enabled = false;
			}
		}
	}

	internal static void ConvergeLayout(Transform root, IReadOnlyList<Transform> sections, int activeSectionCount)
	{
		if (root == null || sections == null)
		{
			return;
		}

		float rootWidth = XjNativeGenealogyLayout.NormalizeRoot(root);
		VerticalLayoutGroup stack = root.GetComponent<VerticalLayoutGroup>();

		float height = 28f;
		int count = Mathf.Min(Mathf.Max(0, activeSectionCount), sections.Count);
		for (int i = 0; i < count; i++)
		{
			LayoutElement sectionLayout = sections[i] != null ? sections[i].GetComponent<LayoutElement>() : null;
			height += sectionLayout != null && sectionLayout.preferredHeight > 0f
				? sectionLayout.preferredHeight
				: 74f;
		}
		if (stack != null && count > 1)
		{
			height += stack.spacing * (count - 1);
		}

		LayoutElement rootLayout = root.GetComponent<LayoutElement>() ?? root.gameObject.AddComponent<LayoutElement>();
		rootLayout.minWidth = rootWidth;
		rootLayout.preferredWidth = rootWidth;
		rootLayout.flexibleWidth = 0f;
		rootLayout.minHeight = height;
		rootLayout.preferredHeight = height;
		rootLayout.flexibleHeight = 0f;

		RectTransform rootRect = root.GetComponent<RectTransform>();
		if (rootRect != null)
		{
			rootRect.anchorMin = new Vector2(0.5f, 1f);
			rootRect.anchorMax = new Vector2(0.5f, 1f);
			rootRect.pivot = new Vector2(0.5f, 1f);
			rootRect.anchoredPosition = new Vector2(0f, rootRect.anchoredPosition.y);
			rootRect.sizeDelta = new Vector2(rootWidth, height);
		}

		ContentSizeFitter fitter = root.GetComponent<ContentSizeFitter>() ?? root.gameObject.AddComponent<ContentSizeFitter>();
		fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
	}

	private static void AddSectionRoot(List<Transform> sections, Transform candidate)
	{
		if (candidate == null)
		{
			return;
		}

		Transform section = !candidate.name.StartsWith("bg_", StringComparison.OrdinalIgnoreCase) && candidate.parent != null
			? candidate.parent
			: candidate;
		if (section != null && !sections.Contains(section))
		{
			sections.Add(section);
		}
	}

	private static void HideDecorations(UiUnitAvatarElement avatarPrefab, Image sexIcon)
	{
		if (avatarPrefab != null)
		{
			avatarPrefab.gameObject.SetActive(false);
		}
		if (sexIcon != null)
		{
			sexIcon.gameObject.SetActive(false);
		}
	}
}
