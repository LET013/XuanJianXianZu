using UnityEngine;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// Fixed native-window shortcut factory. A fixed button is never a native tab and
/// therefore never clones or owns DragOrderElement / Canvas / GraphicRaycaster / Tween
/// runtime state from an already-started WorldBox hierarchy.
/// </summary>
internal static class XjStableUiFactory
{
	internal static GameObject FindOrCreateFixedButton(
		Transform parent,
		GameObject visualTemplate,
		string name,
		Vector3 localPosition)
	{
		if (parent == null || string.IsNullOrWhiteSpace(name)) return null;

		Transform existing = parent.Find(name);
		GameObject obj = existing != null ? existing.gameObject : null;
		if (obj != null && HasUnsafeRuntimeComponents(obj))
		{
			// Legacy in-memory clones from older builds must not be dissected component by
			// component while DragOrderContainer/Tween may still reference them. Retire the
			// whole object atomically (inactive + deferred Destroy) and create a clean root.
			obj.name = name + "_LegacyRetired_" + Time.frameCount;
			obj.SetActive(false);
			Object.Destroy(obj);
			obj = null;
		}

		if (obj == null)
		{
			obj = CreateFixedButton(parent, visualTemplate, name);
		}
		if (obj == null) return null;

		RectTransform rect = obj.GetComponent<RectTransform>();
		if (rect != null)
		{
			rect.localPosition = localPosition;
			rect.localScale = Vector3.one;
		}
		Button button = obj.GetComponent<Button>();
		Image image = obj.GetComponent<Image>();
		if (button != null && image != null) button.targetGraphic = image;
		return obj;
	}

	private static GameObject CreateFixedButton(Transform parent, GameObject template, string name)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
		obj.SetActive(false);
		obj.transform.SetParent(parent, false);

		CopyRect(template != null ? template.GetComponent<RectTransform>() : null, obj.GetComponent<RectTransform>());
		CopyImage(template != null ? template.GetComponent<Image>() : null, obj.GetComponent<Image>());
		CopyButton(template != null ? template.GetComponent<Button>() : null, obj.GetComponent<Button>(), obj.GetComponent<Image>());

		Transform sourceIcon = template != null ? template.transform.Find("Icon") : null;
		if (sourceIcon != null)
		{
			GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
			iconObject.transform.SetParent(obj.transform, false);
			CopyRect(sourceIcon.GetComponent<RectTransform>(), iconObject.GetComponent<RectTransform>());
			CopyImage(sourceIcon.GetComponent<Image>(), iconObject.GetComponent<Image>());
			Image icon = iconObject.GetComponent<Image>();
			if (icon != null) icon.raycastTarget = false;
		}

		obj.SetActive(true);
		return obj;
	}

	private static void CopyRect(RectTransform source, RectTransform target)
	{
		if (target == null) return;
		if (source == null)
		{
			target.sizeDelta = new Vector2(32f, 32f);
			return;
		}
		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.sizeDelta = source.sizeDelta;
		target.anchoredPosition = source.anchoredPosition;
		target.localRotation = source.localRotation;
		target.localScale = Vector3.one;
	}

	private static void CopyImage(Image source, Image target)
	{
		if (target == null) return;
		if (source == null)
		{
			target.color = Color.white;
			return;
		}
		target.sprite = source.sprite;
		target.overrideSprite = source.overrideSprite;
		target.type = source.type;
		target.preserveAspect = source.preserveAspect;
		target.fillCenter = source.fillCenter;
		target.fillMethod = source.fillMethod;
		target.fillAmount = source.fillAmount;
		target.fillClockwise = source.fillClockwise;
		target.fillOrigin = source.fillOrigin;
		target.color = source.color;
		target.material = source.material;
		target.raycastTarget = true;
	}

	private static void CopyButton(Button source, Button target, Graphic targetGraphic)
	{
		if (target == null) return;
		// Animation/SpriteSwap/Navigation can hold references into the native source tree.
		target.transition = Selectable.Transition.ColorTint;
		if (source != null)
		{
			target.colors = source.colors;
			target.interactable = source.interactable;
		}
		target.navigation = new Navigation { mode = Navigation.Mode.None };
		target.targetGraphic = targetGraphic;
	}

	private static bool HasUnsafeRuntimeComponents(GameObject obj)
	{
		if (obj == null) return false;
		return obj.GetComponentInChildren<DragOrderElement>(true) != null
			|| obj.GetComponentInChildren<GraphicRaycaster>(true) != null
			|| obj.GetComponentInChildren<Canvas>(true) != null;
	}
}
