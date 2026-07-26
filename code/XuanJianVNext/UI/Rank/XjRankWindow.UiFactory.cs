using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Rank;

internal static partial class XjRankWindow
{private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color, Vector2 size)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image));
		obj.transform.SetParent(parent, false);
		Image image = obj.GetComponent<Image>();
		image.sprite = sprite;
		image.color = color;
		if (size != Vector2.zero) SetRect(obj, size: size);
		return image;
	}

private static Sprite GetSafeSprite(string path)
	{
		Sprite sprite = string.IsNullOrWhiteSpace(path) ? null : XjIconLoader.TryLoadSpritePath(path);
		return sprite ?? SpriteTextureLoader.getSprite("ui/icons/iconQuestionMark");
	}

private static Sprite GetTraitIcon(ActorTrait trait)
	{
		if (trait == null)
		{
			return GetSafeSprite("ui/icons/iconQuestionMark");
		}

		Sprite sprite = XjIconLoader.TryLoadSpritePath(trait.path_icon);
		if (sprite == null && !string.IsNullOrWhiteSpace(trait.path_icon))
		{
			sprite = XjIconLoader.TryLoadSpritePath("GameResources/" + trait.path_icon);
		}

		return sprite ?? GetSafeSprite("ui/icons/iconQuestionMark");
	}

private static Sprite GetSafeKingdomBackground(Kingdom kingdom)
	{
		try
		{
			Sprite sprite = kingdom?.getElementBackground();
			if (sprite != null) return sprite;
		}
		catch
		{
		}
		return GetSafeSprite("ui/special/backgroundKingdomElement");
	}

private static Sprite GetSafeKingdomIcon(Kingdom kingdom)
	{
		try
		{
			Sprite sprite = kingdom?.getElementIcon();
			if (sprite != null) return sprite;
		}
		catch
		{
		}
		return GetSafeSprite("ui/icons/iconQuestionMark");
	}

private static Sprite GetAssetIcon(ActorAsset asset)
	{
		if (asset == null) return GetSafeSprite("ui/icons/iconQuestionMark");
		try
		{
			Sprite sprite = asset.getSpriteIcon();
			if (sprite != null) return sprite;
		}
		catch { }
		try
		{
			string path = asset.getIconPath();
			if (!string.IsNullOrWhiteSpace(path))
			{
				Sprite sprite = SpriteTextureLoader.getSprite(path);
				if (sprite != null) return sprite;
			}
		}
		catch { }
		if (!string.IsNullOrWhiteSpace(asset.icon))
		{
			Sprite sprite = SpriteTextureLoader.getSprite("ui/icons/" + asset.icon);
			if (sprite != null) return sprite;
		}
		return GetSafeSprite("ui/icons/iconQuestionMark");
	}

private static string GetAssetDisplayName(ActorAsset asset)
	{
		if (asset == null) return "未知生物";
		try
		{
			string name = asset.getLocalizedName();
			if (!string.IsNullOrWhiteSpace(name) && !LooksLikeRawDisplayKey(name)) return name;
		}
		catch { }
		try
		{
			string name = LM.Get(asset.name_locale);
			if (!string.IsNullOrWhiteSpace(name) && !string.Equals(name, asset.name_locale, StringComparison.Ordinal) && !LooksLikeRawDisplayKey(name)) return name;
		}
		catch { }
		return "未知生物";
	}

private static Button CreateNavButton(Transform parent, string name, string text, Vector2 anchor, Action onClick)
	{
		Button button = CreateButton(parent, name, text, new Vector2(58, 22), onClick);
		SetRect(button.gameObject, anchor, anchor, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58, 22));
		return button;
	}

private static Button CreateButton(Transform parent, string name, string text, Vector2 size, Action onClick, Vector2? pos = null)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
		obj.transform.SetParent(parent, false);
		Image image = obj.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		image.type = Image.Type.Sliced;
		SetRect(obj, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), pos ?? Vector2.zero, size);
		Button button = obj.GetComponent<Button>();
		button.targetGraphic = image;
		button.transition = Selectable.Transition.None;
		button.onClick.AddListener(() => onClick?.Invoke());
		Text label = CreateText("Text", obj.transform, text, 9, Color.white);
		SetRect(label.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(2, 0), offsetMax: new Vector2(-2, 0));
		return button;
	}

private static Dropdown CreateDropdown(string name, Transform parent, string[] options, Action<int> onChange)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown));
		obj.transform.SetParent(parent, false);
		Image bg = obj.GetComponent<Image>();
		bg.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		bg.type = Image.Type.Sliced;
		Dropdown dropdown = obj.GetComponent<Dropdown>();
		for (int i = 0; i < options.Length; i++)
		{
			dropdown.options.Add(new Dropdown.OptionData(options[i]));
		}
		dropdown.onValueChanged.AddListener(index => onChange(index));
		Text label = CreateText("Label", obj.transform, string.Empty, 10, Color.white);
		SetRect(label.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-20, 0));
		dropdown.captionText = label;
		GameObject template = CreateDropdownTemplate(obj.transform, bg.sprite);
		dropdown.template = template.GetComponent<RectTransform>();
		dropdown.itemText = template.transform.Find("Viewport/Content/Item/Item Label")?.GetComponent<Text>();
		template.SetActive(false);
		dropdown.RefreshShownValue();
		return dropdown;
	}

private static GameObject CreateDropdownTemplate(Transform parent, Sprite sprite)
	{
		GameObject template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		template.transform.SetParent(parent, false);
		SetRect(template, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 120));
		Image bg = template.GetComponent<Image>();
		bg.sprite = sprite;
		bg.type = Image.Type.Sliced;
		template.GetComponent<Mask>().showMaskGraphic = true;
		GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
		viewport.transform.SetParent(template.transform, false);
		RectTransform viewportRect = SetRect(viewport, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);
		GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		content.transform.SetParent(viewport.transform, false);
		RectTransform contentRectLocal = SetRect(content, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1));
		content.GetComponent<VerticalLayoutGroup>().childControlHeight = true;
		content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		GameObject item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement));
		item.transform.SetParent(content.transform, false);
		SetRect(item, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), size: new Vector2(0, 20));
		item.GetComponent<LayoutElement>().preferredHeight = 20;
		Text itemText = CreateText("Item Label", item.transform, string.Empty, 9, Color.white);
		SetRect(itemText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-5, 0));
		ScrollRect scroll = template.GetComponent<ScrollRect>();
		scroll.content = contentRectLocal;
		scroll.viewport = viewportRect;
		scroll.horizontal = false;
		return template;
	}

private static GameObject CreateScrollView(Transform parent, string name, Vector2 offsetMin, Vector2 offsetMax)
	{
		GameObject scrollObj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(ScrollRect));
		scrollObj.transform.SetParent(parent, false);
		SetRect(scrollObj, Vector2.zero, Vector2.one, offsetMin: offsetMin, offsetMax: offsetMax);
		scrollObj.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);
		ScrollRect scroll = scrollObj.GetComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 20f;

		GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
		viewport.transform.SetParent(scrollObj.transform, false);
		RectTransform viewportRect = SetRect(viewport, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);
		viewport.GetComponent<Image>().color = Color.white;
		viewport.GetComponent<Mask>().showMaskGraphic = false;

		GameObject content = new GameObject("Content", typeof(RectTransform), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
		content.transform.SetParent(viewport.transform, false);
		RectTransform contentRectLocal = SetRect(content, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), size: Vector2.zero);
		ContentSizeFitter fitter = content.GetComponent<ContentSizeFitter>();
		fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
		layout.childControlHeight = false;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = true;
		layout.spacing = 5f;
		layout.padding = new RectOffset(0, 0, 5, 5);
		scroll.viewport = viewportRect;
		scroll.content = contentRectLocal;
		return scrollObj;
	}

private static void CreateSectionTitle(Transform parent, string text, Vector2 pos)
	{
		Text title = CreateText("Title", parent, text, 10, new Color(1f, 0.84f, 0f));
		SetRect(title.gameObject, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), pos, new Vector2(0, 16));
	}

private static Text CreateText(string name, Transform parent, string text, int fontSize, Color color)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text));
		obj.transform.SetParent(parent, false);
		Text label = obj.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = fontSize;
		label.alignment = TextAnchor.MiddleCenter;
		label.color = color;
		label.text = text ?? string.Empty;
		label.horizontalOverflow = HorizontalWrapMode.Overflow;
		label.verticalOverflow = VerticalWrapMode.Truncate;
		return label;
	}

private static RectTransform SetRect(GameObject obj, Vector2? anchorMin = null, Vector2? anchorMax = null, Vector2? pivot = null, Vector2? pos = null, Vector2? size = null, Vector2? offsetMin = null, Vector2? offsetMax = null)
	{
		RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
		if (anchorMin.HasValue) rect.anchorMin = anchorMin.Value;
		if (anchorMax.HasValue) rect.anchorMax = anchorMax.Value;
		if (pivot.HasValue) rect.pivot = pivot.Value;
		if (pos.HasValue) rect.anchoredPosition = pos.Value;
		if (size.HasValue) rect.sizeDelta = size.Value;
		if (offsetMin.HasValue) rect.offsetMin = offsetMin.Value;
		if (offsetMax.HasValue) rect.offsetMax = offsetMax.Value;
		return rect;
	}

private static void SetButtonColor(Button button, Color color)
	{
		Image image = button == null ? null : button.GetComponent<Image>();
		if (image != null)
		{
			image.color = color;
		}
	}
}




