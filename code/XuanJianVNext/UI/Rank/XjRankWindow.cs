using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Foundation;
using XuanJianVNext.Systems.Rank;

namespace XuanJianVNext.UI.Rank;

internal static partial class XjRankWindow
{
	// Legacy identifiers are retained only to clean up existing in-session controls.
	private const string TransparentCloseGlyphName = "XuanJianRankTransparentCloseGlyph";
	private const string CustomCloseButtonName = "XuanJianRankCustomCloseButton";
	// 0.9.8.3截图中的500×310版本整体偏小，标题、表头、侧栏与底栏之间也过于拥挤。
	// 这里按约1.2倍放大整窗，并同步放大三栏、列表行高与筛选图标，不做单纯Transform缩放。
	private const float WindowWidth = 600f;
	private const float WindowHeight = 372f;
	private const float LeftPanelWidth = 110f;
	private const float RightPanelWidth = 110f;
	private const float CenterWidth = 360f;
	private const float ListHeight = 216f;
	private const float CardHeight = 35f;
	private const int RankEntryTextSize = XjUiTheme.RankBodySize - 2;
	private const int CardOverscanRows = 2;
	private const int SideGridCellSize = 26;
	private const float CloseButtonRepairIntervalSeconds = 1.0f;

	private static readonly string[] DaoTuOptions = BuildDaoTuOptions();

	private static ScrollWindow window;
	private static RectTransform contentRect;
	private static RectTransform scrollViewRect;
	private static ScrollRect rankScroll;
	private static ScrollRect sortPanelScroll;
	private static Transform contentTransform;
	private static Text emptyText;
	private static Text countText;
	private static Text rankCaptionText;
	private static Text rankSummaryText;
	private static Text primaryMetricHeaderText;
	private static InputField searchInput;
	private static GameObject cardPrefab;
	private static int currentRealmFilter;
	private static int currentDaoTuFilter;
	private static string searchQuery = string.Empty;
	private static readonly List<XjRankSortKey> activeSortKeys = new List<XjRankSortKey>();
	private static readonly List<XjRankItem> actorList = new List<XjRankItem>();
	// cardInstances 只记录已经实际 Instantiate 的小型池；滚动时只重绑约一屏卡片。
	private static readonly List<GameObject> cardInstances = new List<GameObject>();
	private static readonly Stack<GameObject> recycledCards = new Stack<GameObject>();
	private static readonly Dictionary<int, GameObject> cardByIndex = new Dictionary<int, GameObject>();
	private static readonly List<int> cardIndexScratch = new List<int>(32);
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
	private static bool needRefresh;
	private static bool rebuildingList;
	private static bool filterChoicesInitialized;
	private static float nextEmptyRankRecoveryAt;
	// 排行榜卡片和原生头像都可能在一个 UI 点击内收到两次 pointer/click 回调。
	// 只合并极短时间内对同一人物的重复请求，不接管原生的窗口历史或选中状态。
	private static long lastOpenedActorId;
	private static float lastOpenedActorAt;
	private const float RepeatedActorOpenCooldownSeconds = 0.15f;

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

	private static void TryStopRankScroll(string context)
	{
		if (rankScroll == null) return;
		try
		{
			rankScroll.StopMovement();
		}
		catch (Exception exception)
		{
			XjExceptionDiagnostics.Report("UI/Rank/StopMovement:" + context, exception);
		}
	}

	internal static void OpenActorFromRank(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		float now = Time.unscaledTime;
		if (actorId == lastOpenedActorId
			&& now - lastOpenedActorAt < RepeatedActorOpenCooldownSeconds)
		{
			return;
		}

		lastOpenedActorId = actorId;
		lastOpenedActorAt = now;
		TryStopRankScroll("open-actor");
		Tooltip.hideTooltip();
		ActionLibrary.openUnitWindow(actor);
	}

	private static bool IsRightClickClosingFrame()
	{
		try
		{
			return Input.GetMouseButton(1) || Input.GetMouseButtonDown(1) || Input.GetMouseButtonUp(1);
		}
		catch (Exception exception)
		{
			XjExceptionDiagnostics.Report("UI/Rank/RightClickState", exception);
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
			HidePreexistingWindowChrome(((Component)window).transform);
			RemoveCloseButtonBackground();
			if (ShouldRefreshFilterChoicesOnOpen()) RefreshFilterChoices();
			// 已选筛选/排序的控件在用户实际改动时即时刷新；窗口重开时它们仍然存在，
			// 不再每次 Destroy + Instantiate 并强制整张 Canvas 重排。
			needRefresh = true;
			UpdateVisibleCards();
		};
		updater.OnClose = () =>
		{
			// 人物窗打开时不保留一批仍绑定 Actor 的 UiUnitAvatarElement。
			// 0.6.3 在窗口关闭时直接销毁排行榜卡片；恢复这一生命周期边界，
			// 卡池只服务于排行榜仍打开时的滚动复用。
			DisposeCardPool();
		};
		updater.OnFrame = () =>
		{
			// 地图刚载入时修士索引可能晚于排行榜窗口一两帧完成回填。
			// 只在“完全无筛选且总榜为空”时低频自愈，避免用户必须先点一个道途筛选才能看到角色。
			if (actorList.Count != 0 || !IsUnfilteredRankView()) return;
			if (XuanJianVNext.Core.XjCultivatorCache.Count <= 0) return;
			float now = Time.unscaledTime;
			if (now < nextEmptyRankRecoveryAt) return;
			nextEmptyRankRecoveryAt = now + 1.25f;
			XjRankReadModel.InvalidateCache();
			RefreshCurrentList(true);
		};
		return true;
	}


	private static void SetupWindowContent()
	{
		RectTransform windowRect = ((Component)window).GetComponent<RectTransform>();
		if (windowRect != null)
		{
			windowRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}

		Transform background = ((Component)window).transform.Find("Background");
		if (background == null)
		{
			return;
		}

		MakeOwnGraphicsTransparent(((Component)window).transform);
		MakeOwnGraphicsTransparent(background);
		HidePreexistingWindowChrome(((Component)window).transform);
		RectTransform bgRect = background.GetComponent<RectTransform>();
		if (bgRect != null)
		{
			bgRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}

		CreateCustomBackplate(background);
		CreateLeftPanel(background);
		// 让两侧栏先于榜框绘制；右侧排序栏不再覆盖榜单右侧金边。
		CreateRightPanel(background);
		CreateCenterPanel(background);
		RemoveCloseButtonBackground();
	}

	private static void MakeOwnGraphicsTransparent(Transform transform)
	{
		if (transform == null)
		{
			return;
		}

		Graphic[] graphics = transform.GetComponents<Graphic>();
		for (int i = 0; i < graphics.Length; i++)
		{
			Graphic graphic = graphics[i];
			if (graphic == null)
			{
				continue;
			}
			graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 0f);
			graphic.raycastTarget = false;
			try { graphic.canvasRenderer.SetAlpha(0f); } catch (System.Exception xjCaughtRootGraphic) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.cs:MakeOwnGraphicsTransparent", xjCaughtRootGraphic); }
		}
	}

	private static void HidePreexistingWindowChrome(Transform background)
	{
		if (background == null)
		{
			return;
		}

		Graphic[] graphics = background.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			Graphic graphic = graphics[i];
			if (graphic == null || IsRankOwnedTransform(graphic.transform) || IsNativeCloseTransform(graphic.transform))
			{
				continue;
			}
			graphic.raycastTarget = false;
			graphic.color = new Color(graphic.color.r, graphic.color.g, graphic.color.b, 0f);
			try { graphic.canvasRenderer.SetAlpha(0f); } catch (System.Exception xjCaughtChromeGraphic) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/Rank/XjRankWindow.cs:HidePreexistingWindowChrome", xjCaughtChromeGraphic); }
			graphic.enabled = false;
		}
	}

	private static bool IsRankOwnedTransform(Transform transform)
	{
		if (IsNativeCloseTransform(transform))
		{
			return true;
		}

		while (transform != null)
		{
			string name = transform.name ?? string.Empty;
			if (name.StartsWith("XjRank", StringComparison.Ordinal)
				|| string.Equals(name, "LeftPanel", StringComparison.Ordinal)
				|| string.Equals(name, "RightPanel", StringComparison.Ordinal)
				|| string.Equals(name, "RankTitlePanel", StringComparison.Ordinal)
				|| string.Equals(name, "RankTableHeader", StringComparison.Ordinal)
				|| string.Equals(name, "RankScrollView", StringComparison.Ordinal)
				|| string.Equals(name, "BottomBar", StringComparison.Ordinal)
				|| string.Equals(name, CustomCloseButtonName, StringComparison.Ordinal)
				|| string.Equals(name, TransparentCloseGlyphName, StringComparison.Ordinal))
			{
				return true;
			}
			transform = transform.parent;
		}
		return false;
	}

	private static void CreateCustomBackplate(Transform parent)
	{
		GameObject plate = new GameObject("XjRankCustomBackplate", typeof(RectTransform), typeof(Image));
		plate.transform.SetParent(parent, false);
		// 背板严格贴合窗口，不再向四周溢出4px造成截图中白/灰边和框线错位。
		SetRect(plate, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);
		Image image = plate.GetComponent<Image>();
		image.sprite = null;
		image.color = XjUiTheme.Frame;
		image.raycastTarget = false;

		GameObject inner = new GameObject("XjRankInnerSurface", typeof(RectTransform), typeof(Image));
		inner.transform.SetParent(parent, false);
		SetRect(inner, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 5), offsetMax: new Vector2(-5, -5));
		Image innerImage = inner.GetComponent<Image>();
		innerImage.sprite = null;
		innerImage.color = XjUiTheme.SurfaceWindow;
		innerImage.raycastTarget = false;

		CreatePanelAccent(inner.transform, new Color(0.88f, 0.68f, 0.29f, 0.9f), 2f);
	}

	private static void CreatePanelAccent(Transform parent, Color color, float height)
	{
		GameObject accent = new GameObject("XjRankAccent", typeof(RectTransform), typeof(Image));
		accent.transform.SetParent(parent, false);
		// 拉伸线只缩左右各8px，anchoredPosition归零，避免原实现“右移7px再减14px”
		// 导致左右面板顶线肉眼不居中。
		SetRect(accent, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(-16f, height));
		Image image = accent.GetComponent<Image>();
		image.sprite = null;
		image.color = color;
		image.raycastTarget = false;
	}

	private static void CreateLeftPanel(Transform parent)
	{
		GameObject panel = new GameObject("LeftPanel", typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		SetRect(panel, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(LeftPanelWidth, -72));
		Image panelBg = panel.GetComponent<Image>();
		panelBg.sprite = null;
		panelBg.type = Image.Type.Simple;
		panelBg.color = XjUiTheme.SurfacePanel;
		panel.AddComponent<XjRankRightClickHandler>().BlockRightClick = true;
		CreatePanelAccent(panel.transform, new Color(0.82f, 0.65f, 0.29f, 0.85f), 1.5f);
		CreateSectionTitle(panel.transform, "筛选", new Vector2(0, -6));

		GameObject scroll = CreateScrollView(panel.transform, "FilterScroll", new Vector2(6, 36), new Vector2(-6, -31));
		filterScrollContent = scroll.transform.Find("Viewport/Content");
		int sideColumns = ResolveSideGridColumns();
		selectedFiltersContainer = CreateTitledGrid(filterScrollContent, "已选筛选", SideGridCellSize, sideColumns, false);
		kingdomFilterContainer = CreateTitledGrid(filterScrollContent, "国家", SideGridCellSize, sideColumns, true);
		assetFilterContainer = CreateTitledGrid(filterScrollContent, "亚种", SideGridCellSize, sideColumns, true);
		traitFilterContainer = CreateTitledGrid(filterScrollContent, "特质", SideGridCellSize, sideColumns, true);

		Button clear = CreateButton(panel.transform, "ClearFilters", "清空全部", new Vector2(82, 20), ClearAllFilters);
		SetRect(clear.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(82, 20));
		SetButtonColor(clear, new Color(0.22f, 0.08f, 0.06f, 0.9f));
	}


	private static void CreateCenterPanel(Transform parent)
	{
		currentDaoTuFilter = 0;
		currentRealmFilter = 0;
		CreateRankTableFrame(parent);

		GameObject titlePanel = new GameObject("RankTitlePanel", typeof(RectTransform), typeof(Image));
		titlePanel.transform.SetParent(parent, false);
		SetRect(titlePanel, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -17), new Vector2(CenterWidth, 46));
		Image titleBg = titlePanel.GetComponent<Image>();
		titleBg.sprite = null;
		titleBg.color = XjUiTheme.SurfaceDeep;
		CreatePanelAccent(titlePanel.transform, new Color(0.91f, 0.72f, 0.33f, 0.9f), 1.5f);
		rankCaptionText = CreateText("RankCaption", titlePanel.transform, "玄鉴修士榜", 15, XjUiTheme.AccentGold);
		rankCaptionText.fontStyle = FontStyle.Bold;
		SetRect(rankCaptionText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(8, 19), offsetMax: new Vector2(-8, -2));
		rankSummaryText = CreateText("RankSummary", titlePanel.transform, "总计入榜角色：0名", 8, new Color(0.66f, 0.79f, 0.71f));
		rankSummaryText.alignment = TextAnchor.MiddleCenter;
		SetRect(rankSummaryText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(8, 2), offsetMax: new Vector2(-8, -26));

		GameObject scrollObj = new GameObject("RankScrollView", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
		scrollObj.transform.SetParent(parent, false);
		// Keep the table header outside the viewport. The viewport starts immediately below it and
		// extends down to the navigation bar, so changing list height cannot cover the header.
		scrollViewRect = SetRect(scrollObj, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -27), new Vector2(CenterWidth, ListHeight));
		Image scrollBg = scrollObj.GetComponent<Image>();
		scrollBg.sprite = null;
		scrollBg.color = XjUiTheme.SurfaceScroll;
		rankScroll = scrollObj.GetComponent<ScrollRect>();
		rankScroll.horizontal = false;
		rankScroll.vertical = true;
		rankScroll.movementType = ScrollRect.MovementType.Clamped;
		rankScroll.inertia = false;
		rankScroll.scrollSensitivity = 22f;

		GameObject viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
		viewport.transform.SetParent(scrollObj.transform, false);
		RectTransform viewportRect = SetRect(viewport, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);

		GameObject content = new GameObject("Content", typeof(RectTransform));
		content.transform.SetParent(viewport.transform, false);
		contentRect = SetRect(content, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, Vector2.zero);
		contentTransform = content.transform;

		rankScroll.viewport = viewportRect;
		rankScroll.content = contentRect;
		rankScroll.onValueChanged.AddListener(_ =>
		{
			if (!rebuildingList) UpdateVisibleCards();
		});

		// Create after the scroll view so the viewport and its cards can never cover the column labels.
		CreateRankTableHeaderClean(parent);

		GameObject bottom = new GameObject("BottomBar", typeof(RectTransform));
		bottom.transform.SetParent(parent, false);
		SetRect(bottom, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 15), new Vector2(CenterWidth, 20));
		CreateNavButton(bottom.transform, "ToTop", "置顶", new Vector2(0.32f, 0.5f), ToTop);
		CreateNavButton(bottom.transform, "Refresh", "更新", new Vector2(0.50f, 0.5f), RefreshAll);
		CreateNavButton(bottom.transform, "ToBottom", "置底", new Vector2(0.68f, 0.5f), ToBottom);

		countText = CreateText("Count", parent, "总计修士：0", 9, new Color(1f, 0.82f, 0.32f));
		countText.fontStyle = FontStyle.Bold;
		SetRect(countText.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 39), new Vector2(CenterWidth, 17));

		emptyText = CreateText("Empty", parent, "暂无记录", 11, new Color(0.64f, 0.64f, 0.64f));
		SetRect(emptyText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), size: new Vector2(200, 50));
	}

	private static void CreateRankTableFrame(Transform parent)
	{
		GameObject outer = new GameObject("XjRankTableFrame", typeof(RectTransform), typeof(Image));
		outer.transform.SetParent(parent, false);
		SetRect(outer, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(CenterWidth + 8f, WindowHeight - 24f));
		Image outerImage = outer.GetComponent<Image>();
		outerImage.sprite = null;
		outerImage.color = XjUiTheme.FrameGold;
		outerImage.raycastTarget = false;

		GameObject inner = new GameObject("XjRankTableSurface", typeof(RectTransform), typeof(Image));
		inner.transform.SetParent(outer.transform, false);
		SetRect(inner, Vector2.zero, Vector2.one, offsetMin: new Vector2(1.5f, 1.5f), offsetMax: new Vector2(-1.5f, -1.5f));
		Image innerImage = inner.GetComponent<Image>();
		innerImage.sprite = null;
		innerImage.color = XjUiTheme.SurfaceDeep;
		innerImage.raycastTarget = false;
	}


	private static void CreateRankTableHeaderClean(Transform parent)
	{
		GameObject header = new GameObject("RankTableHeader", typeof(RectTransform), typeof(Image));
		header.transform.SetParent(parent, false);
		SetRect(header, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -74), new Vector2(CenterWidth, 27));
		Image bg = header.GetComponent<Image>();
		bg.sprite = null;
		bg.color = new Color(0.29f, 0.21f, 0.075f, 0.99f);
		CreatePanelAccent(header.transform, new Color(0.92f, 0.73f, 0.34f, 0.92f), 1f);
		ResolveCenterColumns(out float rankX, out float rankWidth, out _, out _,
			out float nameX, out float nameWidth, out float realmX, out float realmWidth,
			out float powerX, out float powerWidth, out float belongX, out float belongWidth);
		// 标题按实际文字起点校准：姓名落在角色名第二字，境界与条目文字左缘对齐。
		float nameHeaderX = nameX + RankEntryTextSize;
		float realmHeaderX = realmX + 5f;
		CreateHeaderText(header.transform, "榜", rankX, rankWidth, TextAnchor.MiddleCenter);
		CreateHeaderText(header.transform, "姓名", nameHeaderX, nameWidth - RankEntryTextSize, TextAnchor.MiddleLeft);
		CreateHeaderText(header.transform, "境界", realmHeaderX, realmWidth - 5f, TextAnchor.MiddleLeft);
		primaryMetricHeaderText = CreateHeaderText(header.transform, "战力", powerX, powerWidth, TextAnchor.MiddleRight);
		CreateHeaderText(header.transform, "归属", belongX, belongWidth, TextAnchor.MiddleLeft);
		RefreshPrimaryMetricHeader();
	}

	private static Text CreateHeaderText(Transform parent, string text, float x, float width, TextAnchor alignment)
	{
		Text label = CreateText("Header_" + text, parent, text, 9, new Color(1f, 0.86f, 0.48f));
		label.alignment = alignment;
		label.fontStyle = FontStyle.Bold;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Truncate;
		SetRect(label.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(width, 22f));
		return label;
	}

	private static void RefreshPrimaryMetricHeader()
	{
		if (primaryMetricHeaderText == null) return;
		primaryMetricHeaderText.text = activeSortKeys.Count > 0 && activeSortKeys[0]?.Def != null
			? activeSortKeys[0].Def.Name
			: "战力";
	}

	private static void RebuildRankSideLayout(Transform gridTransform)
	{
		if (gridTransform == null) return;
		Canvas.ForceUpdateCanvases();
		for (Transform current = gridTransform; current != null; current = current.parent)
		{
			if (current is RectTransform rect)
			{
				LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
			}
			if (ReferenceEquals(current, sortScrollContent)) break;
		}
	}

	private static void CreateRightPanel(Transform parent)
	{
		GameObject panel = new GameObject("RightPanel", typeof(RectTransform), typeof(Image));
		panel.transform.SetParent(parent, false);
		SetRect(panel, new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(-8, 0), new Vector2(RightPanelWidth, -72));
		Image panelBg = panel.GetComponent<Image>();
		panelBg.sprite = null;
		panelBg.type = Image.Type.Simple;
		panelBg.color = XjUiTheme.SurfacePanel;
		panel.AddComponent<XjRankRightClickHandler>().BlockRightClick = true;
		CreatePanelAccent(panel.transform, XjUiTheme.AccentMint, 1.5f);
		CreateSectionTitle(panel.transform, "排序", new Vector2(0, -6));

		GameObject scroll = CreateScrollView(panel.transform, "SortScroll", new Vector2(6, 36), new Vector2(-6, -31));
		sortPanelScroll = scroll.GetComponent<ScrollRect>();
		sortScrollContent = scroll.transform.Find("Viewport/Content");
		int sideColumns = ResolveSideGridColumns();
		selectedSortContainer = CreateTitledGrid(sortScrollContent, "当前排序", SideGridCellSize, sideColumns, false);
		availableSortContainer = CreateTitledGrid(sortScrollContent, "可选排序", SideGridCellSize, sideColumns, true);
		CreateSearchSection(sortScrollContent);

		XjRankSortSystem.Init();
		CreateAvailableSortButtons();
		RefreshSelectedSortButtons();

		Button clear = CreateButton(panel.transform, "ClearSort", "清空排序", new Vector2(82, 20), ClearSortKeys);
		SetRect(clear.gameObject, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(82, 20));
		SetButtonColor(clear, new Color(0.22f, 0.08f, 0.06f, 0.9f));
	}


	private static void RefreshAll()
	{
		XjRankReadModel.InvalidateCache();
		filterChoicesInitialized = false;
		RefreshFilterChoices();
		RefreshSelectedFilters();
		RefreshCurrentList(true);
	}

	private static void RefreshCurrentList(bool resetScroll)
	{
		rebuildingList = true;
		try
		{
			XjRankSortSystem.ClearCache();
			ClearCards();
			if (resetScroll && contentRect != null)
			{
				contentRect.anchoredPosition = Vector2.zero;
			}

			actorList.Clear();
			actorList.AddRange(BuildFilteredActorItems(out int admittedTotal));
			SortActorItems(actorList);
			RefreshPrimaryMetricHeader();
			RefreshRankSummary();

			// ScrollRect 在“内容高度小于视口”时会依据 Bounds 自动补偿。旧实现把
			// Content 高度直接设成条目数×行高，筛选后只有几名修士时，短内容会被
			// 推到视口底部；再滚动一次，虚拟化按另一套 scrollY 计算，于是出现
			// “角色明明存在却在很下面、滚一下又消失”。Content 至少铺满整个视口，
			// 让顶部永远是唯一零点。
			Canvas.ForceUpdateCanvases();
			float viewportHeight = ResolveRankViewportHeight();
			float contentHeight = Math.Max(viewportHeight, Math.Max(1, actorList.Count) * CardHeight);
			contentRect.sizeDelta = new Vector2(0f, contentHeight);
			Canvas.ForceUpdateCanvases();
			if (rankScroll != null)
			{
				TryStopRankScroll("refresh-list");
				rankScroll.velocity = Vector2.zero;
			}
			if (contentRect != null)
			{
				float maxScroll = Math.Max(0f, contentHeight - viewportHeight);
				float y = resetScroll ? 0f : Mathf.Clamp(contentRect.anchoredPosition.y, 0f, maxScroll);
				contentRect.anchoredPosition = new Vector2(0f, y);
				if (resetScroll && rankScroll != null) rankScroll.verticalNormalizedPosition = 1f;
			}
			string trimmedSearch = (searchQuery ?? string.Empty).Trim();
			countText.text = "总计修士：" + admittedTotal.ToString(CultureInfo.InvariantCulture)
				+ (actorList.Count == admittedTotal && string.IsNullOrWhiteSpace(trimmedSearch)
					? string.Empty
					: "　当前显示：" + actorList.Count.ToString(CultureInfo.InvariantCulture));
			emptyText.gameObject.SetActive(actorList.Count == 0);

		}
		finally
		{
			// 即使第三方UI/原生对象在刷新期间抛错，也不能永久锁死滚动事件。
			rebuildingList = false;
		}
		CreateInitialVisibleCards();
	}

	private static void RefreshRankSummary()
	{
		if (rankSummaryText == null) return;
		if (actorList.Count <= 0)
		{
			rankSummaryText.text = "总计入榜角色：0名";
			return;
		}

		rankSummaryText.text = "总计入榜角色：" + actorList.Count.ToString(CultureInfo.InvariantCulture) + "名";
	}

	private static bool IsUnfilteredRankView()
	{
		if (currentRealmFilter != 0 || currentDaoTuFilter != 0) return false;
		if (!string.IsNullOrWhiteSpace(searchQuery)) return false;
		for (int i = 0; i < activeFilters.Count; i++)
		{
			if (activeFilters[i] != null && !activeFilters[i].ToBeRemoved) return false;
		}
		for (int i = 0; i < activeSortKeys.Count; i++)
		{
			if (activeSortKeys[i]?.Def?.MatchesActor != null) return false;
		}
		return true;
	}

	private static List<XjRankItem> BuildFilteredActorItems(out int admittedTotal)
	{
		IReadOnlyList<XjRankItem> all = XjRankReadModel.Build(0);
		// 若榜单快照恰好在地图切换/索引回填前生成过空表，15秒只读缓存不能继续把
		// 已经存在的修士挡在总榜外。发现“修士索引有成员但总榜为空”时只重建一次。
		if ((all == null || all.Count == 0) && XuanJianVNext.Core.XjCultivatorCache.Count > 0)
		{
			XjRankReadModel.InvalidateCache();
			all = XjRankReadModel.Build(0);
		}
		admittedTotal = all?.Count ?? 0;
		IReadOnlyList<XjRankItem> source = currentRealmFilter == 0 ? all : XjRankReadModel.Build(currentRealmFilter);
		List<XjRankItem> items = new List<XjRankItem>(source ?? Array.Empty<XjRankItem>());
		string normalizedSearch = NormalizeSearchText(searchQuery);
		for (int i = items.Count - 1; i >= 0; i--)
		{
			if (!PassRealmFilter(items[i]) || !PassDaoTuFilter(items[i]) || !PassSearchFilter(items[i], normalizedSearch))
			{
				items.RemoveAt(i);
			}
		}

		for (int i = items.Count - 1; i >= 0; i--)
		{
			bool keep = true;
			for (int j = 0; j < activeSortKeys.Count; j++)
			{
				Func<Actor, bool> matches = activeSortKeys[j]?.Def?.MatchesActor;
				if (matches != null && !matches(items[i].Actor))
				{
					keep = false;
					break;
				}
			}
			if (!keep) items.RemoveAt(i);
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
		if (index < 0 || index >= actorList.Count || cardByIndex.ContainsKey(index) || cardPrefab == null || contentTransform == null) return;

		GameObject card = AcquireCard();
		if (card == null) return;
		XjRankItem item = actorList[index];
		card.transform.SetParent(contentTransform, false);
		card.SetActive(true);
		RectTransform cardRect = card.GetComponent<RectTransform>();
		if (cardRect != null) cardRect.anchoredPosition = new Vector2(0f, -CardHeight / 2f - index * CardHeight);
		else card.transform.localPosition = new Vector3(0, -CardHeight / 2f - index * CardHeight);
		cardByIndex[index] = card;
		string primarySortDisplay = XjRankSortSystem.FormatNum((float)item.Power, string.Empty);
		if (activeSortKeys.Count > 0 && activeSortKeys[0]?.Def?.GetDisplay != null)
		{
			try { primarySortDisplay = activeSortKeys[0].Def.GetDisplay(item.Actor); }
			catch (System.Exception xjCaughtRankCardDisplay)
			{
				XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjRankWindow.CreateActorCard.GetDisplay", xjCaughtRankCardDisplay);
			}
		}
		card.GetComponent<XjRankCardView>()?.Setup(item, index, primarySortDisplay);
	}

	private static GameObject AcquireCard()
	{
		while (recycledCards.Count > 0)
		{
			GameObject pooled = recycledCards.Pop();
			if (pooled != null) return pooled;
		}
		GameObject created = UnityEngine.Object.Instantiate(cardPrefab, contentTransform);
		created.SetActive(false);
		cardInstances.Add(created);
		return created;
	}

	private static GameObject CreateCardPrefab()
	{
		GameObject card = new GameObject("XjRankCard", typeof(RectTransform), typeof(Image), typeof(Button), typeof(XjRankCardView));
		RectTransform rect = card.GetComponent<RectTransform>();
		// 虚拟行必须锚定 Content 顶部。默认 RectTransform 锚点在中心，旧实现却按
		// “距顶部 index×CardHeight”计算 anchoredPosition，正是少量条目落在视口底部、
		// 滚动后又被遮罩裁掉的根因。
		rect.anchorMin = new Vector2(0.5f, 1f);
		rect.anchorMax = new Vector2(0.5f, 1f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2(ResolveRankContentWidth() - 8f, CardHeight - 1f);
		Image bg = card.GetComponent<Image>();
		bg.sprite = null;
		bg.type = Image.Type.Simple;
		card.GetComponent<Button>().targetGraphic = bg;

		ResolveCenterColumns(out float rankX, out float rankWidth, out float avatarX, out float avatarSize,
			out float nameX, out float nameWidth, out float realmX, out float realmWidth,
			out float powerX, out float powerWidth, out float belongX, out float belongWidth);
		float mainY = CardHeight * 0.18f;
		float mainH = Math.Max(10f, CardHeight * 0.43f);
		float detailY = -CardHeight * 0.25f;
		float detailH = Math.Max(8f, CardHeight * 0.30f);
		CreateCardText("RankText", card.transform, new Vector2(rankX, 0), new Vector2(rankWidth, CardHeight - 3f), XjUiTheme.RankIndexSize, TextAnchor.MiddleCenter, new Color(1f, 0.84f, 0f));
		CreateAvatarElement(card.transform, avatarX, avatarSize);
		// 主列统一字号，尤其不再让“战力”比“境界/归属”大一号。
		CreateCardText("NameText", card.transform, new Vector2(nameX, mainY), new Vector2(nameWidth, mainH), RankEntryTextSize, TextAnchor.MiddleLeft, Color.white, allowBestFit: true);
		CreateCardText("RightText", card.transform, new Vector2(realmX, mainY), new Vector2(realmWidth, mainH), RankEntryTextSize, TextAnchor.MiddleLeft, XjUiTheme.AccentBlue);
		CreateCardText("PowerText", card.transform, new Vector2(powerX, mainY), new Vector2(powerWidth, mainH), RankEntryTextSize, TextAnchor.MiddleRight, new Color(1f, 0.82f, 0.32f));
		CreateCardText("BelongText", card.transform, new Vector2(belongX, mainY), new Vector2(belongWidth, mainH), RankEntryTextSize, TextAnchor.MiddleLeft, XjUiTheme.AccentGreen, allowBestFit: true);
		CreateCardText("DetailText", card.transform, new Vector2(nameX, detailY), new Vector2(nameWidth, detailH), XjUiTheme.RankMetaSize, TextAnchor.MiddleLeft, new Color(0.74f, 0.76f, 0.68f));
		card.SetActive(false);
		return card;
	}

	private static void ResolveCenterColumns(
		out float rankX, out float rankWidth, out float avatarX, out float avatarSize,
		out float nameX, out float nameWidth, out float realmX, out float realmWidth,
		out float powerX, out float powerWidth, out float belongX, out float belongWidth)
	{
		float inner = Math.Max(220f, ResolveRankContentWidth() - 8f);
		float gap = Mathf.Clamp(inner * 0.010f, 2.5f, 4f);
		float nameRealmGap = Mathf.Clamp(gap + 5f, 7.5f, 9f);
		float realmPowerGap = Mathf.Clamp(gap * 0.35f, 1f, 1.5f);
		// 战力保持右对齐，但把整列继续左收，给归属留出更明确的视觉缓冲。
		float powerBelongGap = Mathf.Clamp(gap + 13f, 15.5f, 18f);
		rankX = 5f;
		rankWidth = Mathf.Clamp(inner * 0.082f, 22f, 30f);
		avatarSize = Mathf.Clamp(inner * 0.068f, 20f, 25f);
		avatarX = rankX + rankWidth + gap;
		nameX = avatarX + avatarSize + gap;
		nameWidth = Mathf.Clamp(inner * 0.285f, 72f, 114f);
		// 境界右移，与姓名拉开；战力右对齐到归属列前，并以更大的列间距左收。
		realmX = nameX + nameWidth + nameRealmGap;
		realmWidth = Mathf.Clamp(inner * 0.155f, 44f, 62f);
		powerWidth = Mathf.Clamp(inner * 0.155f, 42f, 64f);
		belongWidth = Mathf.Clamp(inner * 0.160f, 45f, 60f);
		belongX = inner - belongWidth;
		powerX = Mathf.Max(realmX + realmWidth + realmPowerGap, belongX - powerBelongGap - powerWidth);
	}

	private static int ResolveSideGridColumns()
	{
		return LeftPanelWidth < 112f ? 3 : 4;
	}

	private static float ResolveRankContentWidth()
	{
		return scrollViewRect != null && scrollViewRect.rect.width > 120f
			? scrollViewRect.rect.width
			: CenterWidth;
	}

	private static float ResolveRankViewportHeight()
	{
		return scrollViewRect != null && scrollViewRect.rect.height > 1f
			? scrollViewRect.rect.height
			: ListHeight;
	}

	private static void CreateCardText(string name, Transform parent, Vector2 pos, Vector2 size, int fontSize, TextAnchor alignment, Color color, bool allowBestFit = false)
	{
		Text text = CreateText(name, parent, string.Empty, fontSize, color);
		text.alignment = alignment;
		text.supportRichText = false;
		// 0.9.9：境界/战力等同类字段必须保持同字号。只允许姓名、归属这类不可控长文本
		// 在自己的列内缩小，避免每一行四个主列各自best-fit导致视觉大小跳动。
		text.resizeTextForBestFit = allowBestFit;
		if (allowBestFit)
		{
			text.resizeTextMinSize = Math.Max(5, fontSize - 2);
			text.resizeTextMaxSize = fontSize;
		}
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		SetRect(text.gameObject, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), pos, size);
	}

	private static void CreateAvatarElement(Transform parent, float x, float avatarSize)
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
			rect.anchoredPosition = new Vector2(x, 0);
			float scale = Mathf.Clamp(avatarSize / 62f, 0.28f, 0.38f);
			rect.localScale = new Vector3(scale, scale, scale);
			avatar.show_banner_kingdom = true;
			avatar.show_banner_clan = false;
			if (avatar.kingdomBanner != null) avatar.kingdomBanner.gameObject.SetActive(false);
			if (avatar.clanBanner != null) avatar.clanBanner.gameObject.SetActive(false);
			return;
		}

		GameObject avatarObj = new GameObject("AvatarFallback", typeof(RectTransform), typeof(Image));
		avatarObj.transform.SetParent(parent, false);
		SetRect(avatarObj, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(avatarSize, avatarSize));
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
		containerLayout.spacing = 3f;
		containerLayout.padding = new RectOffset(1, 1, 0, 6);
		container.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		GameObject titleBar = new GameObject("TitleBar", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
		titleBar.transform.SetParent(container.transform, false);
		Image titleImage = titleBar.GetComponent<Image>();
		titleImage.sprite = null;
		titleImage.type = Image.Type.Simple;
		titleImage.color = new Color(0.29f, 0.31f, 0.25f, 0.98f);
		SetRect(titleBar, size: new Vector2(0, 20));
		titleBar.GetComponent<LayoutElement>().preferredHeight = 20f;
		Text titleText = CreateText("Title", titleBar.transform, title, 9, new Color(1f, 0.86f, 0.48f));
		titleText.alignment = TextAnchor.MiddleLeft;
		SetRect(titleText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-42, 0));

		GameObject gridContainer = new GameObject("GridContainer", typeof(RectTransform), typeof(Image), typeof(ContentSizeFitter), typeof(VerticalLayoutGroup));
		gridContainer.transform.SetParent(container.transform, false);
		Image gridImage = gridContainer.GetComponent<Image>();
		gridImage.sprite = null;
		gridImage.type = Image.Type.Simple;
		gridImage.color = new Color(0.16f, 0.18f, 0.15f, 0.98f);
		SetRect(gridContainer, size: Vector2.zero);
		gridContainer.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		VerticalLayoutGroup gridContainerLayout = gridContainer.GetComponent<VerticalLayoutGroup>();
		gridContainerLayout.childControlHeight = true;
		gridContainerLayout.childControlWidth = true;
		gridContainerLayout.childForceExpandHeight = false;
			gridContainerLayout.padding = new RectOffset(4, 4, 2, 2);

		GameObject grid = new GameObject("Grid", typeof(RectTransform), typeof(LayoutElement), typeof(ContentSizeFitter), typeof(GridLayoutGroup));
		grid.transform.SetParent(gridContainer.transform, false);
		SetRect(grid, size: Vector2.zero);
		GridLayoutGroup gridLayout = grid.GetComponent<GridLayoutGroup>();
		float adaptiveCell = ResolveInitialSideCellSize(columns, cellSize);
		grid.GetComponent<LayoutElement>().minHeight = adaptiveCell + 5f;
		grid.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		gridLayout.cellSize = new Vector2(adaptiveCell, adaptiveCell);
		gridLayout.spacing = new Vector2(4f, 4f);
		gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
		gridLayout.constraintCount = columns;

		if (collapsible)
		{
			Text toggleText = CreateText("Toggle", titleBar.transform, "[折叠]", 7, new Color(0.72f, 0.74f, 0.68f));
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
			// 筛选项必须默认可见。旧逻辑在创建后立即隐藏容器，导致国家、亚种、特质看似“没有加载”。
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
		titleImage.sprite = null;
		titleImage.type = Image.Type.Simple;
		titleImage.color = new Color(0.17f, 0.19f, 0.15f, 0.82f);
		titleBar.GetComponent<LayoutElement>().preferredHeight = 17f;
		Text titleText = CreateText("Title", titleBar.transform, "角色搜索", 8, new Color(1f, 0.9f, 0.7f));
		titleText.alignment = TextAnchor.MiddleLeft;
		SetRect(titleText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(5, 0), offsetMax: new Vector2(-5, 0));

		GameObject inputObj = new GameObject("ActorSearchInput", typeof(RectTransform), typeof(Image), typeof(InputField), typeof(LayoutElement));
		inputObj.transform.SetParent(container.transform, false);
		Image inputImage = inputObj.GetComponent<Image>();
		inputImage.sprite = SpriteTextureLoader.getSprite("ui/special/darkInputFieldEmpty");
		inputImage.type = Image.Type.Sliced;
		inputImage.color = new Color(0.12f, 0.14f, 0.11f, 0.90f);
		inputObj.GetComponent<LayoutElement>().preferredHeight = 21f;

		Text inputText = CreateText("Text", inputObj.transform, string.Empty, 8, Color.white);
		inputText.alignment = TextAnchor.MiddleLeft;
		inputText.horizontalOverflow = HorizontalWrapMode.Overflow;
		SetRect(inputText.gameObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(8, 0), offsetMax: new Vector2(-8, 0));

		Text placeholder = CreateText("Placeholder", inputObj.transform, "输入角色名字", 8, new Color(0.7f, 0.7f, 0.7f, 0.75f));
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

																																	private static float ResolveInitialSideCellSize(int columns, int requestedCellSize)
	{
		columns = Math.Max(1, columns);
		float available = Math.Max(46f, LeftPanelWidth - 18f);
		float spacing = 4f * Math.Max(0, columns - 1);
		float computed = (available - spacing) / columns;
		return Mathf.Clamp(computed, 16f, Math.Max(16f, requestedCellSize));
	}

	private static void ToTop()
	{
		if (contentRect == null) return;
		if (rankScroll != null)
		{
			TryStopRankScroll("to-top");
			rankScroll.velocity = Vector2.zero;
			rankScroll.verticalNormalizedPosition = 1f;
		}
		contentRect.anchoredPosition = Vector2.zero;
		UpdateVisibleCards();
	}

	private static void ToBottom()
	{
		if (contentRect == null || scrollViewRect == null) return;
		if (rankScroll != null)
		{
			TryStopRankScroll("to-bottom");
			rankScroll.velocity = Vector2.zero;
		}
		float max = Math.Max(0f, contentRect.sizeDelta.y - ResolveRankViewportHeight());
		contentRect.anchoredPosition = new Vector2(0, max);
		if (rankScroll != null) rankScroll.verticalNormalizedPosition = 0f;
		UpdateVisibleCards();
	}

	private static void CreateInitialVisibleCards()
	{
		UpdateVisibleCards();
	}

	private static void UpdateVisibleCards()
	{
		if (rebuildingList) return;
		if (needRefresh)
		{
			needRefresh = false;
			RefreshCurrentList(true);
			return;
		}
		if (actorList.Count == 0 || contentRect == null)
		{
			RecycleVisibleCards();
			return;
		}

		float viewportHeight = ResolveRankViewportHeight();
		float maxScroll = Math.Max(0f, contentRect.sizeDelta.y - viewportHeight);
		float scrollY = Mathf.Clamp(contentRect.anchoredPosition.y, 0f, maxScroll);
		if (Math.Abs(contentRect.anchoredPosition.y - scrollY) > 0.01f)
		{
			contentRect.anchoredPosition = new Vector2(0f, scrollY);
		}
		int first = Math.Max(0, (int)Math.Floor(scrollY / CardHeight) - CardOverscanRows);
		int last = Math.Min(actorList.Count - 1,
			(int)Math.Ceiling((scrollY + viewportHeight) / CardHeight) + CardOverscanRows);

		cardIndexScratch.Clear();
		foreach (KeyValuePair<int, GameObject> pair in cardByIndex)
		{
			if (pair.Key < first || pair.Key > last) cardIndexScratch.Add(pair.Key);
		}
		for (int i = 0; i < cardIndexScratch.Count; i++) RecycleCard(cardIndexScratch[i]);
		cardIndexScratch.Clear();

		for (int i = first; i <= last; i++) CreateActorCard(i);
	}

	private static void RecycleCard(int index)
	{
		if (!cardByIndex.TryGetValue(index, out GameObject card)) return;
		cardByIndex.Remove(index);
		if (card == null) return;
		card.GetComponent<XjRankCardView>()?.Recycle();
		card.SetActive(false);
		recycledCards.Push(card);
	}

	private static void RecycleVisibleCards()
	{
		if (cardByIndex.Count == 0) return;
		cardIndexScratch.Clear();
		foreach (int index in cardByIndex.Keys) cardIndexScratch.Add(index);
		for (int i = 0; i < cardIndexScratch.Count; i++) RecycleCard(cardIndexScratch[i]);
		cardIndexScratch.Clear();
	}

	private static void ClearCards()
	{
		// 排行榜仍打开时的筛选/滚动刷新继续复用小型卡池。
		RecycleVisibleCards();
	}

	private static void DisposeCardPool()
	{
		Tooltip.hideTooltip();
		cardByIndex.Clear();
		cardIndexScratch.Clear();
		recycledCards.Clear();
		for (int i = 0; i < cardInstances.Count; i++)
		{
			GameObject card = cardInstances[i];
			if (card != null) UnityEngine.Object.Destroy(card);
		}
		cardInstances.Clear();
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
