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
{
	private const string TransparentCloseGlyphName = "XuanJianRankTransparentCloseGlyph";
	private const float WindowWidth = 620f;
	private const float WindowHeight = 420f;
	private const float LeftPanelWidth = 170f;
	private const float RightPanelWidth = 170f;
	private const float CenterWidth = 250f;
	private const float ListHeight = 300f;
	private const float CardHeight = 46f;
	private const float CloseButtonRepairIntervalSeconds = 0.25f;

	private static readonly string[] RealmOptions = { "全部", "胎息", "炼气", "筑基", "紫府", "金丹" };
	private static readonly string[] DaoTuOptions = BuildDaoTuOptions();

	private static ScrollWindow window;
	private static RectTransform contentRect;
	private static RectTransform scrollViewRect;
	private static ScrollRect rankScroll;
	private static Transform contentTransform;
	private static Text emptyText;
	private static Text countText;
	private static Dropdown realmDropdown;
	private static Dropdown filterDropdown;
	private static InputField searchInput;
	private static GameObject cardPrefab;
	private static int currentRealmFilter;
	private static int currentDaoTuFilter;
	private static string searchQuery = string.Empty;
	private static readonly List<XjRankSortKey> activeSortKeys = new List<XjRankSortKey>();
	private static readonly List<XjRankItem> actorList = new List<XjRankItem>();
	private static readonly List<GameObject> cardInstances = new List<GameObject>();
	private static readonly Dictionary<int, GameObject> cardByIndex = new Dictionary<int, GameObject>();
	private static readonly List<int> cardIndexBuffer = new List<int>();
	private static readonly List<GameObject> sortButtons = new List<GameObject>();
	private static Transform filterScrollContent;
	private static Transform selectedFiltersContainer;
	private static Transform kingdomFilterContainer;
	private static Transform assetFilterContainer;
	private static Transform traitFilterContainer;
	private static Transform sortScrollContent;
	private static Transform selectedSortContainer;
	private static Transform availableSortContainer;
	private static readonly List<XjRankFilterSetting> activeFilters = new List<XjRankFilterSetting>();
	private static readonly List<GameObject> selectedFilterButtons = new List<GameObject>();
	private static readonly List<GameObject> kingdomFilterButtons = new List<GameObject>();
	private static readonly List<GameObject> assetFilterButtons = new List<GameObject>();
	private static readonly List<GameObject> traitFilterButtons = new List<GameObject>();
	private static readonly List<GameObject> selectedSortButtons = new List<GameObject>();
	private static bool suppressDropdownCallbacks;
	private static bool needRefresh;
	private static bool filterChoicesInitialized;
	private static int lastViewStart = int.MaxValue;
	private static int lastViewEnd = -1;
	private static float nextCloseButtonRepairAt;

	internal static void Show()
	{
		bool createdNow = EnsureWindow();
		if (window == null)
		{
			return;
		}

		if (createdNow)
		{
			RefreshFilterChoices();
			RefreshSelectedFilters();
			RefreshSelectedSortButtons();
			needRefresh = true;
		}
		XjWindowOpenGuard.Show(window, "XuanJianRankWindow", createdNow);
	}

	internal static bool ShouldBlockRightClickClose(ScrollWindow target)
	{
		return target != null
			&& ReferenceEquals(target, window)
			&& IsRightClickClosingFrame();
	}

	internal static bool ShouldBlockGlobalRightClickClose()
	{
		return window != null
			&& ((Component)window).gameObject.activeInHierarchy
			&& IsRightClickClosingFrame();
	}

	private static bool IsRightClickClosingFrame()
	{
		try
		{
			return Input.GetMouseButton(1) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1);
		}
		catch
		{
			return false;
		}
	}

	private static bool EnsureWindow()
	{
		if (window != null)
		{
			return false;
		}

		window = WindowCreator.CreateEmptyWindow("XuanJianRankWindow", "xuanjian.rank.overview", "ui/Icons/items/PaiHangBang");
		if (window == null)
		{
			return false;
		}

		RemoveCloseButtonBackground();
		SetupWindowContent();
		cardPrefab = CreateCardPrefab();
		cardPrefab.transform.SetParent(((Component)window).transform, false);
		XjRankWindowUpdater updater = ((Component)window).gameObject.AddComponent<XjRankWindowUpdater>();
		updater.OnOpen = () =>
		{
			RemoveCloseButtonBackground();
			nextCloseButtonRepairAt = Time.unscaledTime + CloseButtonRepairIntervalSeconds;
			if (ShouldRefreshFilterChoicesOnOpen()) RefreshFilterChoices();
			RefreshSelectedFilters();
			RefreshSelectedSortButtons();
			needRefresh = true;
		};
		updater.OnUpdate = () =>
		{
			// 原生窗口偶尔会在悬停或布局刷新时写回关闭按钮底图。
			// 关闭按钮控件树改为每 0.25 秒修复一次，不再逐帧遍历。
			float now = Time.unscaledTime;
			if (now >= nextCloseButtonRepairAt)
			{
				RemoveCloseButtonBackground();
				nextCloseButtonRepairAt = now + CloseButtonRepairIntervalSeconds;
			}
			UpdateVisibleCards();
		};
		updater.OnClose = () =>
		{
			nextCloseButtonRepairAt = 0f;
			ClearCards();
		};
		return true;
	}

	private static void RemoveCloseButtonBackground()
	{
		if (window == null)
		{
			return;
		}

		Transform closeButton = FindCloseButtonTransform();
		if (closeButton == null)
		{
			return;
		}

		MakeCloseAncestorsTransparent(closeButton);
		Image[] images = closeButton.GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			MakeTransparentButtonImage(images[i], images[i].transform == closeButton);
		}
		RawImage[] rawImages = closeButton.GetComponentsInChildren<RawImage>(true);
		for (int i = 0; i < rawImages.Length; i++)
		{
			MakeTransparentButtonRawImage(rawImages[i], rawImages[i].transform == closeButton);
		}
		HideNativeCloseGraphics(closeButton);
		EnsureTransparentHitArea(closeButton);
		EnsureTransparentCloseGlyph(closeButton);
	}

	private static void MakeCloseAncestorsTransparent(Transform closeButton)
	{
		if (closeButton == null || window == null)
		{
			return;
		}

		Transform root = ((Component)window).transform;
		Transform current = closeButton.parent;
		while (current != null && current != root)
		{
			string name = current.name ?? string.Empty;
			bool closeShell = name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("buttonclose", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0;
			if (closeShell)
			{
				Image[] images = current.GetComponents<Image>();
				for (int i = 0; i < images.Length; i++) MakeTransparentButtonImage(images[i], false);
				RawImage[] rawImages = current.GetComponents<RawImage>();
				for (int i = 0; i < rawImages.Length; i++) MakeTransparentButtonRawImage(rawImages[i], false);
			}
			current = current.parent;
		}
	}

	private static void MakeTransparentButtonImage(Image image, bool keepRaycast)
	{
		if (image == null)
		{
			return;
		}

		image.sprite = null;
		image.color = new Color(1f, 1f, 1f, 0f);
		image.raycastTarget = keepRaycast;
		Button button = image.GetComponent<Button>();
		if (button != null)
		{
			button.targetGraphic = image;
			button.transition = Selectable.Transition.None;
		}
	}

	private static void MakeTransparentButtonRawImage(RawImage image, bool keepRaycast)
	{
		if (image == null)
		{
			return;
		}

		image.texture = null;
		image.color = new Color(1f, 1f, 1f, 0f);
		image.raycastTarget = keepRaycast;
		Button button = image.GetComponent<Button>();
		if (button != null)
		{
			button.targetGraphic = image;
			button.transition = Selectable.Transition.None;
		}
	}

	private static void HideNativeCloseGraphics(Transform closeButton)
	{
		if (closeButton == null)
		{
			return;
		}

		Graphic[] graphics = closeButton.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			Graphic graphic = graphics[i];
			if (graphic == null)
			{
				continue;
			}

			bool isRoot = graphic.transform == closeButton;
			bool isGlyph = string.Equals(graphic.gameObject.name, TransparentCloseGlyphName, StringComparison.Ordinal);
			if (isGlyph)
			{
				continue;
			}

			if (isRoot)
			{
				graphic.enabled = true;
				graphic.color = new Color(1f, 1f, 1f, 0f);
				graphic.raycastTarget = true;
				continue;
			}

			graphic.raycastTarget = false;
			graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 0f);
			try { graphic.canvasRenderer.SetAlpha(0f); } catch { }
			graphic.enabled = false;
		}
	}

	private static void EnsureTransparentHitArea(Transform closeButton)
	{
		Image image = closeButton.GetComponent<Image>();
		if (image == null)
		{
			image = closeButton.gameObject.AddComponent<Image>();
		}
		image.sprite = null;
		image.color = new Color(1f, 1f, 1f, 0f);
		image.raycastTarget = true;
		Button button = closeButton.GetComponent<Button>();
		if (button != null)
		{
			button.targetGraphic = image;
			button.transition = Selectable.Transition.None;
		}
	}

	private static void EnsureTransparentCloseGlyph(Transform closeButton)
	{
		if (closeButton == null)
		{
			return;
		}

		Transform existing = closeButton.Find(TransparentCloseGlyphName);
		Text glyph = existing == null ? null : existing.GetComponent<Text>();
		if (glyph == null)
		{
			GameObject glyphObject = new GameObject(TransparentCloseGlyphName);
			glyphObject.transform.SetParent(closeButton, false);
			RectTransform rect = glyphObject.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			glyph = glyphObject.AddComponent<Text>();
			glyph.raycastTarget = false;
		}

		glyph.text = "×";
		glyph.alignment = TextAnchor.MiddleCenter;
		glyph.fontSize = 26;
		glyph.fontStyle = FontStyle.Bold;
		glyph.color = new Color(0.85f, 0.08f, 0.04f, 1f);
		glyph.enabled = true;
		try { glyph.canvasRenderer.SetAlpha(1f); } catch { }
		if (glyph.font == null)
		{
			glyph.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
		}
	}

	private static Transform FindCloseButtonTransform()
	{
		Transform root = ((Component)window).transform;
		string[] paths =
		{
			"CloseButton",
			"ButtonClose",
			"Close",
			"Background/CloseButton",
			"Background/ButtonClose",
			"Background/Close"
		};
		for (int i = 0; i < paths.Length; i++)
		{
			Transform found = root.Find(paths[i]);
			if (found != null)
			{
				return found;
			}
		}

		Button[] buttons = ((Component)window).GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			Transform current = ((Component)buttons[i]).transform;
			string name = current.name ?? string.Empty;
			if (name.IndexOf("close", StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("x", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return current;
			}
		}
		return null;
	}

	private static bool HasChildImage(Transform transform)
	{
		Image[] images = transform.GetComponentsInChildren<Image>(true);
		for (int i = 0; i < images.Length; i++)
		{
			if (images[i] != null && images[i].transform != transform)
			{
				return true;
			}
		}
		return false;
	}

	private static void SetupWindowContent()
	{
		Transform background = ((Component)window).transform.Find("Background");
		if (background == null)
		{
			return;
		}

		RectTransform bgRect = background.GetComponent<RectTransform>();
		if (bgRect != null)
		{
			bgRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}

		CreateLeftPanel(background);
		CreateCenterPanel(background);
		CreateRightPanel(background);
	}

	private static void CreateLeftPanel(Transform parent)
	{
		GameObject panel = new GameObject("LeftPanel", typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		SetRect(panel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(LeftPanelWidth, -60));
		panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);
		panel.AddComponent<XjRankRightClickHandler>().BlockRightClick = true;
		CreateSectionTitle(panel.transform, "筛选", new Vector2(0, -5));

		GameObject scroll = CreateScrollView(panel.transform, "FilterScroll", new Vector2(5, 30), new Vector2(-5, -25));
		filterScrollContent = scroll.transform.Find("Viewport/Content");
		selectedFiltersContainer = CreateTitledGrid(filterScrollContent, "已选筛选", 25, 5, false);
		kingdomFilterContainer = CreateTitledGrid(filterScrollContent, "国家", 25, 5, true);
		assetFilterContainer = CreateTitledGrid(filterScrollContent, "人种", 25, 5, true);
		traitFilterContainer = CreateTitledGrid(filterScrollContent, "特质", 25, 5, true);

		Button clear = CreateButton(panel.transform, "ClearFilters", "清空全部", new Vector2(68, 18), ClearAllFilters);
		SetRect(clear.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(68, 18));
		SetButtonColor(clear, new Color(0.6f, 0.3f, 0.3f));
	}


	private static void CreateCenterPanel(Transform parent)
	{
		GameObject topBar = new GameObject("TopBar", typeof(RectTransform));
		topBar.transform.SetParent(parent, false);
		SetRect(topBar, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(CenterWidth, 26));
		filterDropdown = CreateDropdown("DaoTuDropdown", topBar.transform, DaoTuOptions, index =>
		{
			if (suppressDropdownCallbacks) return;
			currentDaoTuFilter = index;
			RefreshCurrentList(true);
		});
		SetRect(filterDropdown.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(120, 25));
		realmDropdown = CreateDropdown("RealmDropdown", topBar.transform, RealmOptions, index =>
		{
			if (suppressDropdownCallbacks) return;
			currentRealmFilter = index;
			RefreshCurrentList(true);
		});
		SetRect(realmDropdown.gameObject, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(1, 0.5f), Vector2.zero, new Vector2(120, 25));

		GameObject scrollObj = new GameObject("RankScrollView", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObj.transform.SetParent(parent, false);
		scrollViewRect = SetRect(scrollObj, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -5), new Vector2(CenterWidth, ListHeight));
		Image scrollBg = scrollObj.GetComponent<Image>();
		scrollBg.color = new Color(0f, 0f, 0f, 0.14f);
		scrollObj.GetComponent<Mask>().showMaskGraphic = true;
		rankScroll = scrollObj.GetComponent<ScrollRect>();
		rankScroll.horizontal = false;
		rankScroll.vertical = true;
		rankScroll.movementType = ScrollRect.MovementType.Clamped;
		rankScroll.scrollSensitivity = 30f;

		GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
		viewport.transform.SetParent(scrollObj.transform, false);
		RectTransform viewportRect = SetRect(viewport, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);

		GameObject content = new GameObject("Content", typeof(RectTransform));
		content.transform.SetParent(viewport.transform, false);
		contentRect = SetRect(content, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
		contentTransform = content.transform;

		rankScroll.viewport = viewportRect;
		rankScroll.content = contentRect;

		GameObject bottom = new GameObject("BottomBar", typeof(RectTransform));
		bottom.transform.SetParent(parent, false);
		SetRect(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(CenterWidth, 26));
		CreateNavButton(bottom.transform, "ToTop", "顶", new Vector2(0.20f, 0.5f), ToTop);
		CreateNavButton(bottom.transform, "Refresh", "刷新", new Vector2(0.50f, 0.5f), RefreshAll);
		CreateNavButton(bottom.transform, "ToBottom", "底", new Vector2(0.80f, 0.5f), ToBottom);

		countText = CreateText("Count", parent, "榜单：0", 9, new Color(0.72f, 0.72f, 0.72f));
		SetRect(countText.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(CenterWidth, 16));

		emptyText = CreateText("Empty", parent, "暂无记录", 14, new Color(0.64f, 0.64f, 0.64f));
		SetRect(emptyText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size: new Vector2(200, 50));
	}

	private static void CreateRightPanel(Transform parent)
	{
		GameObject panel = new GameObject("RightPanel", typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		SetRect(panel, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(RightPanelWidth, -60));
		panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.05f);
		panel.AddComponent<XjRankRightClickHandler>().BlockRightClick = true;
		CreateSectionTitle(panel.transform, "排序", new Vector2(0, -5));

		GameObject scroll = CreateScrollView(panel.transform, "SortScroll", new Vector2(5, 30), new Vector2(-5, -25));
		sortScrollContent = scroll.transform.Find("Viewport/Content");
		selectedSortContainer = CreateTitledGrid(sortScrollContent, "当前排序", 25, 5, false);
		availableSortContainer = CreateTitledGrid(sortScrollContent, "可选排序", 25, 5, true);
		CreateSearchSection(sortScrollContent);

		XjRankSortSystem.Init();
		CreateAvailableSortButtons();
		RefreshSelectedSortButtons();

		Button clear = CreateButton(panel.transform, "ClearSort", "清空排序", new Vector2(68, 18), ClearSortKeys);
		SetRect(clear.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 8), new Vector2(68, 18));
		SetButtonColor(clear, new Color(0.6f, 0.3f, 0.3f));
	}


	private static void RefreshAll()
	{
		RefreshFilterChoices();
		RefreshSelectedFilters();
		RefreshCurrentList(true);
	}

	private static void RefreshCurrentList(bool resetScroll)
	{
		XjRankSortSystem.ClearCache();
		ClearCards();
		if (resetScroll && contentRect != null)
		{
			contentRect.anchoredPosition = Vector2.zero;
		}

		actorList.Clear();
		actorList.AddRange(BuildFilteredActorItems());
		SortActorItems(actorList);
		contentRect.sizeDelta = new Vector2(0, Math.Max(1, actorList.Count) * CardHeight);
		lastViewStart = int.MaxValue;
		lastViewEnd = -1;
		string trimmedSearch = (searchQuery ?? string.Empty).Trim();
		countText.text = string.IsNullOrWhiteSpace(trimmedSearch)
			? "共 " + actorList.Count.ToString(CultureInfo.InvariantCulture) + " 人"
			: "搜索：" + trimmedSearch + "  共 " + actorList.Count.ToString(CultureInfo.InvariantCulture) + " 人";
		emptyText.gameObject.SetActive(actorList.Count == 0);
		CreateInitialVisibleCards();

		RefreshSelectedSortButtons();
	}

	private static List<XjRankItem> BuildFilteredActorItems()
	{
		List<XjRankItem> items = new List<XjRankItem>(XjRankReadModel.Build(currentRealmFilter));
		string normalizedSearch = NormalizeSearchText(searchQuery);
		for (int i = items.Count - 1; i >= 0; i--)
		{
			if (!PassRealmFilter(items[i]) || !PassDaoTuFilter(items[i]) || !PassSearchFilter(items[i], normalizedSearch))
			{
				items.RemoveAt(i);
			}
		}

		List<XjRankFilterSetting> andFilters = activeFilters.FindAll(f => !f.ToBeRemoved && f.Type == XjRankFilterType.And);
		List<XjRankFilterSetting> orFilters = activeFilters.FindAll(f => !f.ToBeRemoved && f.Type == XjRankFilterType.Or);
		List<XjRankFilterSetting> notFilters = activeFilters.FindAll(f => !f.ToBeRemoved && f.Type == XjRankFilterType.Not);
		for (int i = items.Count - 1; i >= 0; i--)
		{
			Actor actor = items[i].Actor;
			bool keep = true;
			for (int j = 0; j < andFilters.Count; j++)
			{
				if (!SafeFilter(andFilters[j], actor))
				{
					keep = false;
					break;
				}
			}
			if (keep && orFilters.Count > 0)
			{
				keep = false;
				for (int j = 0; j < orFilters.Count; j++)
				{
					if (SafeFilter(orFilters[j], actor))
					{
						keep = true;
						break;
					}
				}
			}
			if (keep)
			{
				for (int j = 0; j < notFilters.Count; j++)
				{
					if (SafeFilter(notFilters[j], actor))
					{
						keep = false;
						break;
					}
				}
			}
			if (!keep)
			{
				items.RemoveAt(i);
			}
		}
		return items;
	}


	private static bool PassRealmFilter(in XjRankItem item)
	{
		if (currentRealmFilter <= 0)
		{
			return true;
		}

		return GetRealmScore(item.RealmId) == currentRealmFilter;
	}

	private static bool PassDaoTuFilter(in XjRankItem item)
	{
		if (currentDaoTuFilter <= 0 || currentDaoTuFilter >= DaoTuOptions.Length)
		{
			return true;
		}

		return string.Equals((item.DaoTu ?? string.Empty).Trim(), DaoTuOptions[currentDaoTuFilter], StringComparison.Ordinal);
	}

	private static bool PassSearchFilter(in XjRankItem item, string normalizedSearch)
	{
		if (string.IsNullOrWhiteSpace(normalizedSearch))
		{
			return true;
		}

		if (NormalizeSearchText(item.Name).Contains(normalizedSearch))
		{
			return true;
		}
		if (item.Actor?.data != null && NormalizeSearchText(item.Actor.getName()).Contains(normalizedSearch))
		{
			return true;
		}
		return false;
	}

	private static string NormalizeSearchText(string value)
	{
		return (value ?? string.Empty).Trim().Replace(" ", string.Empty).Replace("　", string.Empty);
	}

	private static string[] BuildDaoTuOptions()
	{
		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.Entries;
		string[] result = new string[entries.Count + 2];
		result[0] = "全部道途";
		result[1] = "杂气";
		for (int i = 0; i < entries.Count; i++)
		{
			result[i + 2] = entries[i].DisplayName;
		}
		return result;
	}

		private static void CreateActorCard(int index)
	{
		if (index < 0 || index >= actorList.Count || cardByIndex.ContainsKey(index) || cardPrefab == null || contentTransform == null)
		{
			return;
		}

		XjRankItem item = actorList[index];
		GameObject card = UnityEngine.Object.Instantiate(cardPrefab, contentTransform);
		card.SetActive(true);
		card.transform.localPosition = new Vector3(0, -CardHeight / 2f - index * CardHeight);
		cardInstances.Add(card);
		cardByIndex[index] = card;
		string primarySortDisplay = XjRankSortSystem.FormatNum((float)item.Power, "战力");
		if (activeSortKeys.Count > 0 && activeSortKeys[0]?.Def?.GetDisplay != null)
		{
			try
			{
				primarySortDisplay = activeSortKeys[0].Def.GetDisplay(item.Actor);
			}
			catch
			{
			}
		}
		card.GetComponent<XjRankCardView>()?.Setup(item, index, primarySortDisplay);
	}

	private static GameObject CreateCardPrefab()
	{
		GameObject card = new GameObject("XjRankCard", typeof(RectTransform), typeof(Image), typeof(Button), typeof(XjRankCardView));
		RectTransform rect = card.GetComponent<RectTransform>();
		rect.sizeDelta = new Vector2(240f, 42f);
		Image bg = card.GetComponent<Image>();
		bg.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		bg.type = Image.Type.Sliced;
		card.GetComponent<Button>().targetGraphic = bg;

		CreateCardText("RankText", card.transform, new Vector2(5, 0), new Vector2(30, 34), 13, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0f));
		CreateAvatarElement(card.transform);
		CreateCardText("NameText", card.transform, new Vector2(76, 7), new Vector2(92, 18), 8, TextAnchor.MiddleLeft, Color.white);
		CreateCardText("PowerText", card.transform, new Vector2(76, -8), new Vector2(80, 18), 8, TextAnchor.MiddleLeft, new Color(0.4f, 0.8f, 1f));
		CreateCardText("RightText", card.transform, new Vector2(160, -6), new Vector2(44, 18), 8, TextAnchor.MiddleLeft, new Color(1f, 0.84f, 0f));
		CreateCardText("RealmText", card.transform, new Vector2(204, -6), new Vector2(36, 18), 8, TextAnchor.MiddleLeft, new Color(1f, 0.6f, 0.2f));
		card.SetActive(false);
		return card;
	}

	private static void CreateCardText(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor alignment, Color color)
	{
		Text text = CreateText(name, parent, string.Empty, fontSize, color);
		text.alignment = alignment;
		SetRect(text.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), pos, size);
	}

	private static void CreateAvatarElement(Transform parent)
	{
		UiUnitAvatarElement prefab = Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement");
		if (prefab != null)
		{
			UiUnitAvatarElement avatar = UnityEngine.Object.Instantiate(prefab, parent);
			avatar.name = "AvatarElement";
			RectTransform rect = avatar.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0, 0.5f);
			rect.anchorMax = new Vector2(0, 0.5f);
			rect.pivot = new Vector2(0, 0.5f);
			rect.anchoredPosition = new Vector2(38, 0);
			rect.localScale = new Vector3(0.55f, 0.55f, 0.55f);
			avatar.show_banner_kingdom = true;
			avatar.show_banner_clan = false;
			if (avatar.kingdomBanner != null) avatar.kingdomBanner.gameObject.SetActive(false);
			if (avatar.clanBanner != null) avatar.clanBanner.gameObject.SetActive(false);
			return;
		}

		GameObject avatarObj = new GameObject("AvatarFallback", typeof(RectTransform), typeof(Image));
		avatarObj.transform.SetParent(parent, false);
		SetRect(avatarObj, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(30, 30));
	}

	private static Transform CreateTitledGrid(Transform parent, string title, int cellSize, int columns, bool collapsible)
	{
		GameObject container = new GameObject("Grid_" + title, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		container.transform.SetParent(parent, false);
		SetRect(container, size: Vector2.zero);
		VerticalLayoutGroup containerLayout = container.GetComponent<VerticalLayoutGroup>();
		containerLayout.childControlHeight = true;
		containerLayout.childControlWidth = true;
		containerLayout.childForceExpandHeight = false;
		containerLayout.childForceExpandWidth = true;
		containerLayout.spacing = 2f;
		containerLayout.padding = new RectOffset(0, 0, 0, 5);
		container.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject titleBar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
		titleBar.transform.SetParent(container.transform, false);
		Image titleImage = titleBar.GetComponent<Image>();
		titleImage.sprite = SpriteTextureLoader.getSprite("ui/special/darkInputFieldEmpty");
		titleImage.type = Image.Type.Sliced;
		titleImage.color = new Color(0.25f, 0.25f, 0.25f, 0.9f);
		SetRect(titleBar, size: new Vector2(0, 20));
		titleBar.GetComponent<LayoutElement>().preferredHeight = 20f;
		Text titleText = CreateText("Title", titleBar.transform, title, 10, new Color(1f, 0.9f, 0.7f));
		titleText.alignment = TextAnchor.MiddleLeft;
		SetRect(titleText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-42, 0));

		GameObject gridContainer = new GameObject("GridContainer", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
		gridContainer.transform.SetParent(container.transform, false);
		Image gridImage = gridContainer.GetComponent<Image>();
		gridImage.sprite = SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		gridImage.type = Image.Type.Sliced;
		gridImage.color = new Color(0.15f, 0.15f, 0.15f, 0.8f);
		SetRect(gridContainer, size: Vector2.zero);
		gridContainer.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		VerticalLayoutGroup gridContainerLayout = gridContainer.GetComponent<VerticalLayoutGroup>();
		gridContainerLayout.childControlHeight = true;
		gridContainerLayout.childControlWidth = true;
		gridContainerLayout.childForceExpandHeight = false;
		gridContainerLayout.padding = new RectOffset(3, 3, 3, 3);

		GameObject grid = new GameObject("Grid", typeof(RectTransform), typeof(LayoutElement), typeof(ContentSizeFitter), typeof(GridLayoutGroup));
		grid.transform.SetParent(gridContainer.transform, false);
		SetRect(grid, size: Vector2.zero);
		grid.GetComponent<LayoutElement>().minHeight = cellSize + 6f;
		grid.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		GridLayoutGroup gridLayout = grid.GetComponent<GridLayoutGroup>();
		gridLayout.cellSize = new Vector2(cellSize, cellSize);
		gridLayout.spacing = new Vector2(3f, 3f);
		gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		gridLayout.constraintCount = columns;

		if (collapsible)
		{
			Text toggleText = CreateText("Toggle", titleBar.transform, "[折叠]", 9, new Color(0.7f, 0.7f, 0.7f));
			toggleText.alignment = TextAnchor.MiddleRight;
			SetRect(toggleText.gameObject, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-5, 0), new Vector2(50, 0));
			Button toggle = titleBar.AddComponent<Button>();
			toggle.targetGraphic = titleImage;
			toggle.transition = Selectable.Transition.None;
			toggle.onClick.AddListener(() =>
			{
				bool wasActive = gridContainer.activeSelf;
				gridContainer.SetActive(!wasActive);
				toggleText.text = wasActive ? "[展开]" : "[折叠]";
				Canvas.ForceUpdateCanvases();
				LayoutRebuilder.ForceRebuildLayoutImmediate(container.GetComponent<RectTransform>());
				if (parent != null) LayoutRebuilder.ForceRebuildLayoutImmediate(parent.GetComponent<RectTransform>());
			});
			XjRankTooltipTrigger tip = titleBar.AddComponent<XjRankTooltipTrigger>();
			tip.TooltipText = "点击展开或折叠";
			// 筛选项必须默认可见。旧逻辑在创建后立即隐藏容器，导致国家、人种、特质看似“没有加载”。
			gridContainer.SetActive(true);
		}
		return grid.transform;
	}

	private static void CreateSearchSection(Transform parent)
	{
		GameObject container = new GameObject("ActorSearchSection", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		container.transform.SetParent(parent, false);
		SetRect(container, size: Vector2.zero);
		VerticalLayoutGroup layout = container.GetComponent<VerticalLayoutGroup>();
		layout.childControlHeight = true;
		layout.childControlWidth = true;
		layout.childForceExpandHeight = false;
		layout.childForceExpandWidth = true;
		layout.spacing = 4f;
		layout.padding = new RectOffset(0, 0, 2, 5);
		container.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject titleBar = new GameObject("SearchTitle", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
		titleBar.transform.SetParent(container.transform, false);
		Image titleImage = titleBar.GetComponent<Image>();
		titleImage.sprite = SpriteTextureLoader.getSprite("ui/special/darkInputFieldEmpty");
		titleImage.type = Image.Type.Sliced;
		titleImage.color = new Color(0.25f, 0.25f, 0.25f, 0.75f);
		titleBar.GetComponent<LayoutElement>().preferredHeight = 20f;
		Text titleText = CreateText("Title", titleBar.transform, "角色搜索", 10, new Color(1f, 0.9f, 0.7f));
		titleText.alignment = TextAnchor.MiddleLeft;
		SetRect(titleText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-5, 0));

		GameObject inputObj = new GameObject("ActorSearchInput", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
		inputObj.transform.SetParent(container.transform, false);
		Image inputImage = inputObj.GetComponent<Image>();
		inputImage.sprite = SpriteTextureLoader.getSprite("ui/special/darkInputFieldEmpty");
		inputImage.type = Image.Type.Sliced;
		inputImage.color = new Color(0.05f, 0.05f, 0.05f, 0.85f);
		inputObj.GetComponent<LayoutElement>().preferredHeight = 26f;

		Text inputText = CreateText("Text", inputObj.transform, string.Empty, 10, Color.white);
		inputText.alignment = TextAnchor.MiddleLeft;
		inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
		SetRect(inputText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(8, 0), offsetMax: new Vector2(-8, 0));

		Text placeholder = CreateText("Placeholder", inputObj.transform, "输入角色名字", 10, new Color(0.7f, 0.7f, 0.7f, 0.75f));
		placeholder.alignment = TextAnchor.MiddleLeft;
		SetRect(placeholder.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(8, 0), offsetMax: new Vector2(-8, 0));

		searchInput = inputObj.GetComponent<InputField>();
		searchInput.textComponent = inputText;
		searchInput.placeholder = placeholder;
		searchInput.lineType = InputField.LineType.SingleLine;
		searchInput.characterLimit = 24;
		searchInput.SetTextWithoutNotify(searchQuery ?? string.Empty);
		searchInput.onValueChanged.AddListener(value =>
		{
			searchQuery = value ?? string.Empty;
			RefreshCurrentList(true);
		});
	}

																																	private static void ToTop()
	{
		if (contentRect != null)
		{
			contentRect.anchoredPosition = Vector2.zero;
		}
	}

	private static void ToBottom()
	{
		if (contentRect != null && scrollViewRect != null)
		{
			float max = Math.Max(0f, contentRect.sizeDelta.y - scrollViewRect.rect.height);
			contentRect.anchoredPosition = new Vector2(0, max);
		}
	}

	private static void CreateInitialVisibleCards()
	{
		if (actorList.Count == 0 || scrollViewRect == null)
		{
			return;
		}

		int visible = Mathf.CeilToInt((scrollViewRect.rect.height > 0f ? scrollViewRect.rect.height : ListHeight) / CardHeight) + 2;
		int end = Mathf.Min(visible - 1, actorList.Count - 1);
		for (int i = 0; i <= end; i++) CreateActorCard(i);
		lastViewStart = 0;
		lastViewEnd = end;
	}

	private static void UpdateVisibleCards()
	{
		if (needRefresh)
		{
			needRefresh = false;
			RefreshCurrentList(true);
			return;
		}

		if (actorList.Count == 0 || contentRect == null || scrollViewRect == null || rankScroll == null)
		{
			return;
		}

		float scrollY = contentRect.anchoredPosition.y;
		float viewHeight = scrollViewRect.rect.height > 0f ? scrollViewRect.rect.height : ListHeight;
		int start = Mathf.Max(0, Mathf.FloorToInt(scrollY / CardHeight) - 1);
		int end = Mathf.Min(Mathf.CeilToInt((scrollY + viewHeight) / CardHeight) + 1, actorList.Count - 1);
		if (start == lastViewStart && end == lastViewEnd)
		{
			return;
		}

		cardIndexBuffer.Clear();
		foreach (KeyValuePair<int, GameObject> pair in cardByIndex)
		{
			if (pair.Key < start || pair.Key > end) cardIndexBuffer.Add(pair.Key);
		}
		for (int i = 0; i < cardIndexBuffer.Count; i++)
		{
			int index = cardIndexBuffer[i];
			if (cardByIndex.TryGetValue(index, out GameObject card) && card != null)
			{
				cardInstances.Remove(card);
				UnityEngine.Object.Destroy(card);
			}
			cardByIndex.Remove(index);
		}
		for (int i = start; i <= end; i++) CreateActorCard(i);
		lastViewStart = start;
		lastViewEnd = end;
	}

	private static void ClearCards()
	{
		ClearObjectList(cardInstances);
		cardByIndex.Clear();
		cardIndexBuffer.Clear();
		lastViewStart = int.MaxValue;
		lastViewEnd = -1;
	}

	private static void ClearObjectList(List<GameObject> list)
	{
		for (int i = 0; i < list.Count; i++)
		{
			if (list[i] != null)
			{
				UnityEngine.Object.Destroy(list[i]);
			}
		}
		list.Clear();
	}

		}


