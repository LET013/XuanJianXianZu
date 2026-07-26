using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.ZongMen;

// VNext port of the 0.5.4 CityWindow tab prefab. It only bridges UI.
public sealed class XjSimpleWindowTab : APrefab<XjSimpleWindowTab>
{
	public WindowMetaTab Tab { get; private set; }

	protected override void Init()
	{
		if (Initialized)
		{
			return;
		}

		base.Init();
		Tab = GetComponent<WindowMetaTab>();
	}

	public void Setup(string nameKey, ScrollWindow window, Sprite sprite, UnityAction<WindowMetaTab> action)
	{
		if (window?.tabs?._tabs == null)
		{
			return;
		}

		name = nameKey;
		Tab = GetComponent<WindowMetaTab>();
		Tab.name = nameKey;
		Tab._canvas_group = Tab.GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
		window.tabs._tabs.Add(Tab);

		string title = ResolveTabTitle(nameKey);
		XjNativeHoverTooltip.Ensure(Tab._tip_button, title, title, string.Empty);
		transform.SetParent(window.tabs.transform, false);
		Tab.toggleActive(true);

		Transform iconTransform = Tab.transform.Find("Icon");
		Image icon = iconTransform?.GetComponent<Image>();
		if (icon != null)
		{
			icon.sprite = sprite ?? SpriteTextureLoader.getSprite("ui/icons/iconCulture");
		}

		if (action != null)
		{
			Tab.tab_action.AddListener(action);
		}

		SetSize(new Vector2(22f, 22f));
		Tab.transform.localScale = Vector3.one;
	}

	private static void _init()
	{
		GameObject tabObject = new GameObject("AdditionalTab", typeof(Button), typeof(Image), typeof(TipButton));
		TipButton tip = tabObject.GetComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, "玄鉴", "玄鉴", string.Empty);

		tabObject.AddComponent<WindowMetaTab>();
		tabObject.AddComponent<ScrollableButton>();
		tabObject.AddComponent<DragOrderElement>();
		tabObject.AddComponent<ButtonSfx>();

		GameObject holder = new GameObject("XjVNextZongMenTabPrefabHolder");
		Object.DontDestroyOnLoad(holder);
		holder.SetActive(false);
		tabObject.transform.SetParent(holder.transform, false);

		GameObject iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image));
		iconObject.transform.SetParent(tabObject.transform, false);
		RectTransform rect = iconObject.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		Prefab = tabObject.AddComponent<XjSimpleWindowTab>();
	}

	private static string ResolveTabTitle(string nameKey)
	{
		if (string.IsNullOrWhiteSpace(nameKey))
		{
			return "玄鉴";
		}

		if (nameKey.IndexOf("shili", System.StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "宗门";
		}

		if (nameKey.IndexOf("qian", System.StringComparison.OrdinalIgnoreCase) >= 0
			|| nameKey.IndexOf("qiankun", System.StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "乾坤袋";
		}

		return nameKey.Trim();
	}
}
