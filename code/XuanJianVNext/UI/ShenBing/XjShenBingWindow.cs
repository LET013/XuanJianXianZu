using System.Collections.Generic;
using System;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.ShenBing;

internal static class XjShenBingWindow
{
	private const string WindowId = "XuanJianShenBing";
	private const float WindowWidth = 290f;
	private const float WindowHeight = 350f;
	private const float CardWidth = 244f;
	private const float CardHeight = 54f;

	private static bool _initialized;
	private static ScrollWindow _windowInstance;
	private static Dropdown _filterDropdown;
	private static RectTransform _contentRect;
	private static Transform _contentTransform;
	private static Text _emptyText;
	private static Text _countText;
	private static readonly List<GameObject> _cardInstances = new List<GameObject>();
	private static List<XjFaBaoRankEntry> _entries = new List<XjFaBaoRankEntry>();
	private static string _currentFilter = string.Empty;
	private static readonly string[] FilterOptions = { "全部器物", "攻击类", "防御类", "附属类", "筑基法器", "紫府灵宝", "金丹法宝", "仙器" };

	public static void Init()
	{
		if (_initialized) return;
		_windowInstance = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.fabao.overview", "ui/Icons/items/FaBaoZongLan");
		SetupWindowContent();
		_initialized = true;
	}

	public static void ShowWindow()
	{
		bool createdNow = !_initialized;
		if (!_initialized) Init();
		_currentFilter = string.Empty;
		if (_filterDropdown != null) _filterDropdown.SetValueWithoutNotify(0);
		RefreshList();
		XjWindowOpenGuard.Show(_windowInstance, WindowId, createdNow);
	}

	private static void SetupWindowContent()
	{
		Transform bg = _windowInstance.transform.Find("Background");
		if (bg == null) return;
		RectTransform bgRect = bg.GetComponent<RectTransform>();
		if (bgRect != null) bgRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		HideDefaultWindowTitle();
		CreateTopBar(bg);
		CreateScrollView(bg);
		CreateCountText(bg);
		CreateEmptyText(bg);
	}

	private static void HideDefaultWindowTitle()
	{
		Text[] texts = _windowInstance.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			Text text = texts[i];
			if (text != null && ((string.Equals(text.text, "法宝录", System.StringComparison.Ordinal)
				|| string.Equals(text.text, "万宝录", System.StringComparison.Ordinal))
				|| text.gameObject.name.IndexOf("title", System.StringComparison.OrdinalIgnoreCase) >= 0))
			{
				text.gameObject.SetActive(false);
			}
		}
	}

	private static void CreateTopBar(Transform parent)
	{
		GameObject topBar = new GameObject("TopBar", typeof(RectTransform));
		topBar.transform.SetParent(parent, false);
		RectTransform tr = topBar.GetComponent<RectTransform>();
		tr.anchorMin = tr.anchorMax = tr.pivot = new Vector2(0.5f, 1f);
		tr.anchoredPosition = new Vector2(0f, -18f); tr.sizeDelta = new Vector2(190f, 24f);

		_filterDropdown = CreateDropdown("FilterDropdown", topBar.transform, FilterOptions, index =>
		{
			_currentFilter = index == 0 ? string.Empty : FilterOptions[index];
			RefreshList();
		});
		RectTransform dr = _filterDropdown.GetComponent<RectTransform>();
		dr.anchorMin = dr.anchorMax = dr.pivot = new Vector2(0.5f, 0.5f);
		dr.anchoredPosition = Vector2.zero; dr.sizeDelta = new Vector2(180f, 22f);
	}

	private static void CreateScrollView(Transform parent)
	{
		GameObject sv = new GameObject("ScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		sv.transform.SetParent(parent, false);
		RectTransform sr = sv.GetComponent<RectTransform>();
		sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
		sr.offsetMin = new Vector2(16f, 38f); sr.offsetMax = new Vector2(-16f, -48f);
		ScrollRect scroll = sv.GetComponent<ScrollRect>();
		scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 30f;
		sv.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f); sv.GetComponent<Mask>().showMaskGraphic = true;

		GameObject vp = new GameObject("Viewport", typeof(RectTransform)); vp.transform.SetParent(sv.transform, false);
		RectTransform vr = vp.GetComponent<RectTransform>(); vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = new Vector2(4f, 0f); vr.offsetMax = new Vector2(-4f, 0f);
		GameObject co = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); co.transform.SetParent(vp.transform, false);
		_contentRect = co.GetComponent<RectTransform>();
		_contentRect.anchorMin = new Vector2(0f, 1f); _contentRect.anchorMax = new Vector2(1f, 1f); _contentRect.pivot = new Vector2(0.5f, 1f);
		_contentRect.anchoredPosition = Vector2.zero; _contentRect.sizeDelta = Vector2.zero;
		VerticalLayoutGroup vlg = co.GetComponent<VerticalLayoutGroup>();
		vlg.spacing = 5f; vlg.childAlignment = TextAnchor.UpperCenter; vlg.childControlWidth = true; vlg.childControlHeight = true;
		vlg.childForceExpandHeight = false; vlg.padding = new RectOffset(2, 2, 3, 3);
		co.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		scroll.viewport = vr; scroll.content = _contentRect; _contentTransform = co.transform;
	}

	private static void CreateCountText(Transform parent)
	{
		GameObject co = new GameObject("CountText", typeof(RectTransform), typeof(Text)); co.transform.SetParent(parent, false);
		RectTransform cr = co.GetComponent<RectTransform>(); cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0.5f, 0f);
		cr.anchoredPosition = new Vector2(0f, 18f); cr.sizeDelta = new Vector2(180f, 14f);
		_countText = co.GetComponent<Text>(); _countText.font = LocalizedTextManager.current_font; _countText.fontSize = 8;
		_countText.alignment = TextAnchor.MiddleCenter; _countText.color = new Color(0.72f, 0.72f, 0.72f); _countText.text = "共0件";
	}

	private static void CreateEmptyText(Transform parent)
	{
		GameObject eo = new GameObject("EmptyText", typeof(RectTransform), typeof(Text)); eo.transform.SetParent(parent, false);
		RectTransform er = eo.GetComponent<RectTransform>(); er.anchorMin = er.anchorMax = er.pivot = new Vector2(0.5f, 0.5f);
		er.anchoredPosition = Vector2.zero; er.sizeDelta = new Vector2(210f, 40f);
		_emptyText = eo.GetComponent<Text>(); _emptyText.font = LocalizedTextManager.current_font; _emptyText.fontSize = 11;
		_emptyText.alignment = TextAnchor.MiddleCenter; _emptyText.color = new Color(0.65f, 0.65f, 0.65f); _emptyText.text = "暂无器物数据";
	}

	private static void RefreshList()
	{
		_entries = XjShenBingReadModel.BuildRanking(_currentFilter);
		if (_entries.Count == 0)
		{
			SetActiveCardCount(0); _emptyText.gameObject.SetActive(true); if (_countText != null) _countText.text = "共0件"; return;
		}
		_emptyText.gameObject.SetActive(false); if (_countText != null) _countText.text = "共" + _entries.Count + "件";
		EnsureCardPoolCount(_entries.Count);
		for (int i = 0; i < _entries.Count; i++)
		{
			GameObject cardObj = _cardInstances[i]; cardObj.SetActive(true); UpdateCard(cardObj, _entries[i], i); cardObj.transform.SetAsLastSibling();
		}
		SetActiveCardCount(_entries.Count);
	}

	private static void EnsureCardPoolCount(int required)
	{
		if (required <= _cardInstances.Count || _contentTransform == null) return;
		for (int i = _cardInstances.Count; i < required; i++) _cardInstances.Add(CreateCard());
	}

	private static GameObject CreateCard()
	{
		GameObject obj = new GameObject("FaBaoCard", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button));
		obj.transform.SetParent(_contentTransform, false);
		LayoutElement le = obj.GetComponent<LayoutElement>(); le.minHeight = CardHeight; le.preferredHeight = CardHeight; le.preferredWidth = CardWidth; le.flexibleWidth = 0f;
		obj.GetComponent<RectTransform>().sizeDelta = new Vector2(CardWidth, CardHeight);
		Image bg = obj.GetComponent<Image>(); bg.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement"); bg.type = Image.Type.Sliced;

		PlaceText(CreateText("Rank", obj.transform, 13, TextAnchor.MiddleCenter), new Vector2(3f, 0f), new Vector2(24f, 40f));
		GameObject iconObj = new GameObject("Icon", typeof(RectTransform), typeof(Image)); iconObj.transform.SetParent(obj.transform, false);
		RectTransform iconRt = iconObj.GetComponent<RectTransform>(); iconRt.anchorMin = iconRt.anchorMax = new Vector2(0f, 0.5f); iconRt.pivot = new Vector2(0f, 0.5f);
		iconRt.anchoredPosition = new Vector2(29f, 0f); iconRt.sizeDelta = new Vector2(40f, 40f);

		GameObject typeIconObj = new GameObject("TypeIcon", typeof(RectTransform), typeof(Image)); typeIconObj.transform.SetParent(obj.transform, false);
		RectTransform typeRt = typeIconObj.GetComponent<RectTransform>(); typeRt.anchorMin = typeRt.anchorMax = new Vector2(0f, 0.5f); typeRt.pivot = new Vector2(0f, 0.5f);
		typeRt.anchoredPosition = new Vector2(74f, 12f); typeRt.sizeDelta = new Vector2(16f, 16f);

		PlaceText(CreateText("Name", obj.transform, 10, TextAnchor.MiddleLeft), new Vector2(93f, 12f), new Vector2(142f, 18f));
		PlaceText(CreateText("Class", obj.transform, 9, TextAnchor.MiddleLeft), new Vector2(74f, -13f), new Vector2(72f, 18f));
		PlaceText(CreateText("Owner", obj.transform, 8, TextAnchor.MiddleRight), new Vector2(143f, -13f), new Vector2(92f, 18f));
		obj.SetActive(false); return obj;
	}

	private static void PlaceText(GameObject obj, Vector2 position, Vector2 size)
	{
		RectTransform rt = obj.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f); rt.pivot = new Vector2(0f, 0.5f);
		rt.anchoredPosition = position; rt.sizeDelta = size;
	}

	private static void UpdateCard(GameObject obj, XjFaBaoRankEntry entry, int rank)
	{
		Text rankText = obj.transform.Find("Rank")?.GetComponent<Text>();
		if (rankText != null)
		{
			rankText.text = (rank + 1).ToString(); rankText.color = rank == 0 ? new Color(1f, 0.84f, 0f) : rank == 1 ? new Color(0.75f, 0.75f, 0.75f) : rank == 2 ? new Color(0.8f, 0.5f, 0.2f) : new Color(0.8f, 0.8f, 0.8f);
		}
		Image icon = obj.transform.Find("Icon")?.GetComponent<Image>();
		if (icon != null) { Sprite sp = TryGetFaBaoIcon(entry); icon.sprite = sp; icon.enabled = sp != null; icon.preserveAspect = true; }
		Image typeIcon = obj.transform.Find("TypeIcon")?.GetComponent<Image>();
		if (typeIcon != null)
		{
			typeIcon.sprite = ResolveRoleIcon(entry);
			typeIcon.enabled = typeIcon.sprite != null;
			typeIcon.preserveAspect = true;
			typeIcon.color = Color.white;
		}
		string tierColor = entry.IsXianQi ? "#FFB77A" : entry.IsJinDan ? "#FFD36A" : entry.IsFaQi ? "#69B7FF" : "#C65CFF";
		string tierName = entry.IsXianQi ? "仙器" : entry.IsJinDan ? "金丹" : entry.IsFaQi ? "筑基" : "紫府";
		Text nameText = obj.transform.Find("Name")?.GetComponent<Text>();
		if (nameText != null) nameText.text = Toolbox.coloredString(entry.Name, tierColor);
		Text classText = obj.transform.Find("Class")?.GetComponent<Text>();
		if (classText != null)
		{
			string role = string.IsNullOrWhiteSpace(entry.Role) ? "附属" : entry.Role;
			classText.text = Toolbox.coloredString(tierName + "·" + role, tierColor);
		}
		Text ownerText = obj.transform.Find("Owner")?.GetComponent<Text>();
		if (ownerText != null) { ownerText.text = entry.OwnerName; ownerText.color = new Color(0.82f, 0.82f, 0.78f); }

		Image bg = obj.GetComponent<Image>(); bg.color = new Color(0.78f, 0.82f, 0.80f, rank % 2 == 0 ? 0.34f : 0.28f); bg.raycastTarget = true;
		Button button = obj.GetComponent<Button>(); button.targetGraphic = bg; button.transition = Selectable.Transition.None; button.onClick.RemoveAllListeners();
		long ownerId = entry.OwnerId; button.onClick.AddListener(() => OpenOwner(ownerId));
	}

	private static Sprite ResolveRoleIcon(in XjFaBaoRankEntry entry)
	{
		if (string.Equals(entry.Role, XjFaBaoCatalog.RoleDefense, StringComparison.Ordinal))
		{
			return SpriteTextureLoader.getSprite("ui/icons/iconInventory")
				?? TryGetFaBaoIcon(entry);
		}
		if (string.Equals(entry.Role, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal))
		{
			return SpriteTextureLoader.getSprite("ui/icons/iconDamage")
				?? TryGetFaBaoIcon(entry);
		}
		return SpriteTextureLoader.getSprite("ui/icons/iconStar")
			?? TryGetFaBaoIcon(entry);
	}

	private static void OpenOwner(long ownerId)
	{
		if (ownerId > 0L && XuanJianVNext.Core.XjActorRegistry.ResolveKnownOrWorld(ownerId, out Actor actor) && actor?.data != null && actor.isAlive()) ActionLibrary.openUnitWindow(actor);
	}

	private static Sprite TryGetFaBaoIcon(in XjFaBaoRankEntry entry)
	{
		if (!string.IsNullOrWhiteSpace(entry.IconPath))
		{
			Sprite equippedSprite = XjIconLoader.TryLoadSpritePath(entry.IconPath)
				?? XjIconLoader.TryLoadSpritePath("GameResources/" + entry.IconPath);
			if (equippedSprite != null) return equippedSprite;
		}

		if (!string.IsNullOrWhiteSpace(entry.Kind)
			&& XjFaBaoEquipmentAssets.TryPickIconPath(entry.Kind, entry.OwnerId, entry.FaBaoId, out string iconPath))
		{
			Sprite equipmentSprite = XjIconLoader.TryLoadSpritePath(iconPath)
				?? XjIconLoader.TryLoadSpritePath("GameResources/" + iconPath);
			if (equipmentSprite != null)
			{
				return equipmentSprite;
			}
		}

		string faBaoId = entry.FaBaoId;
		if (!string.IsNullOrWhiteSpace(entry.Kind)
			&& XjFaBaoEquipmentAssets.TryPickAssetId(entry.Kind, entry.OwnerId, entry.FaBaoId, out string assetId))
		{
			faBaoId = assetId;
		}
		if (string.IsNullOrWhiteSpace(faBaoId)) return null;
		string[] paths = { "ui/Icons/items/icon_" + faBaoId, "ui/icons/items/icon_" + faBaoId, "items/icon_" + faBaoId, "fabao/" + faBaoId };
		for (int i = 0; i < paths.Length; i++)
		{
			Sprite sprite = SpriteTextureLoader.getSprite(paths[i]);
			if (sprite != null) return sprite;
		}
		return null;
	}

	private static GameObject CreateText(string name, Transform parent, int fontSize, TextAnchor align)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
		Text t = obj.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fontSize; t.alignment = align; t.color = Color.white; t.raycastTarget = false; return obj;
	}

	private static void SetActiveCardCount(int active)
	{
		for (int i = 0; i < _cardInstances.Count; i++) if (_cardInstances[i] != null) _cardInstances[i].SetActive(i < active);
	}

	private static Dropdown CreateDropdown(string name, Transform parent, string[] options, System.Action<int> onChange)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Dropdown)); obj.transform.SetParent(parent, false);
		Image bg = obj.GetComponent<Image>(); bg.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement"); bg.type = Image.Type.Sliced;
		Dropdown dropdown = obj.GetComponent<Dropdown>(); dropdown.options = new List<Dropdown.OptionData>(options.Select(o => new Dropdown.OptionData(o))); dropdown.onValueChanged.AddListener(i => onChange(i));
		GameObject label = new GameObject("Label", typeof(RectTransform), typeof(Text)); label.transform.SetParent(obj.transform, false);
		RectTransform labelRect = label.GetComponent<RectTransform>(); labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one; labelRect.offsetMin = new Vector2(6f, 0f); labelRect.offsetMax = new Vector2(-6f, 0f);
		Text caption = label.GetComponent<Text>(); caption.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf"); caption.fontSize = 9; caption.alignment = TextAnchor.MiddleCenter; caption.color = Color.white; dropdown.captionText = caption;
		GameObject template = CreateDropdownTemplate(obj.transform, bg.sprite); dropdown.template = template.GetComponent<RectTransform>(); dropdown.itemText = template.transform.Find("Viewport/Content/Item/Item Label")?.GetComponent<Text>();
		template.SetActive(false); dropdown.RefreshShownValue(); return dropdown;
	}

	private static GameObject CreateDropdownTemplate(Transform parent, Sprite sprite)
	{
		GameObject template = new GameObject("Template", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect)); template.transform.SetParent(parent, false);
		RectTransform templateRect = template.GetComponent<RectTransform>(); templateRect.anchorMin = new Vector2(0f, 0f); templateRect.anchorMax = new Vector2(1f, 0f); templateRect.pivot = new Vector2(0.5f, 1f); templateRect.anchoredPosition = Vector2.zero; templateRect.sizeDelta = new Vector2(0f, 76f);
		Image background = template.GetComponent<Image>(); background.sprite = sprite; background.type = Image.Type.Sliced; template.GetComponent<Mask>().showMaskGraphic = true;
		GameObject viewport = new GameObject("Viewport", typeof(RectTransform)); viewport.transform.SetParent(template.transform, false);
		RectTransform viewportRect = viewport.GetComponent<RectTransform>(); viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one; viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;
		GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter)); content.transform.SetParent(viewport.transform, false);
		RectTransform contentRect = content.GetComponent<RectTransform>(); contentRect.anchorMin = new Vector2(0f, 1f); contentRect.anchorMax = new Vector2(1f, 1f); contentRect.pivot = new Vector2(0.5f, 1f); contentRect.anchoredPosition = Vector2.zero; contentRect.sizeDelta = Vector2.zero;
		VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>(); layout.childControlHeight = true; layout.childControlWidth = true; layout.childForceExpandHeight = false; content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		GameObject item = new GameObject("Item", typeof(RectTransform), typeof(Toggle), typeof(LayoutElement)); item.transform.SetParent(content.transform, false);
		RectTransform itemRect = item.GetComponent<RectTransform>(); itemRect.anchorMin = new Vector2(0f, 1f); itemRect.anchorMax = new Vector2(1f, 1f); itemRect.pivot = new Vector2(0.5f, 1f); itemRect.sizeDelta = new Vector2(0f, 19f); item.GetComponent<LayoutElement>().preferredHeight = 19f;
		GameObject itemLabel = new GameObject("Item Label", typeof(RectTransform), typeof(Text)); itemLabel.transform.SetParent(item.transform, false);
		RectTransform itemLabelRect = itemLabel.GetComponent<RectTransform>(); itemLabelRect.anchorMin = Vector2.zero; itemLabelRect.anchorMax = Vector2.one; itemLabelRect.offsetMin = new Vector2(6f, 0f); itemLabelRect.offsetMax = new Vector2(-6f, 0f);
		Text itemText = itemLabel.GetComponent<Text>(); itemText.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf"); itemText.fontSize = 9; itemText.alignment = TextAnchor.MiddleCenter; itemText.color = Color.white;
		ScrollRect scroll = template.GetComponent<ScrollRect>(); scroll.content = contentRect; scroll.viewport = viewportRect; scroll.horizontal = false; scroll.vertical = true; return template;
	}
}
