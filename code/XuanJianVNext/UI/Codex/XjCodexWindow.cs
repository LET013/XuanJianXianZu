using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Foundation;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

namespace XuanJianVNext.UI.Codex;

/// <summary>
/// Xuanjian codex shell. The visual geometry, backdrop, typography, tab row,
/// card language, scrolling and input blocker deliberately mirror the
/// reference LOTM encyclopaedia while every data page reads Xuanjian's
/// throttled immutable snapshot.
/// </summary>
internal sealed partial class XjCodexWindow : MonoBehaviour
{
	private const int WindowId = 519207;
	private const int MaxRenderedCardsPerPage = 120;
	private const int MaxRenderedFamilyCardsPerPage = 36;
	private const int MaxRenderedCityCardsPerPage = 64;
	private const int MaxRenderedCraftActorsPerPage = 52;
	private const int MaxRenderedHistoryCardsPerPage = 80;
	private const int MaxRenderedFamilyChronicleItems = 96;
	private const int MaxSectResourceLinesPerSect = 10;
	private const int MaxSectResourceDetailItems = 96;
	private const int MaxRenderedSectPeakMembers = 60;
	private static readonly string[] TabNames =
	{
		"天下总览", "宗门谱系", "宗门底蕴", "家族诸脉", "城镇封邑", "宗门大阵", "洞天福地",
		"玄鉴四艺", "高境名录", "奇遇洞天", "天下风险", "玄鉴史册", "百年世谱", "剑意谱",
		"修道大势", "天下舆图", "玄鉴修士榜", "道途总览", "果位权柄", "明阳经略", "玄鉴百科", "道途总览", "释修总览"
	};
	private static readonly int[] WorldVolumeTabIndexes = { 0, 14, 15, 9, 10 };
	private static readonly int[] SectAndFamilyVolumeTabIndexes = { 1, 3, 4, 19 };
	private static readonly int[] CultivationVolumeTabIndexes = { 20, 22, 8, 17, 18, 7, 13 };
	private static readonly int[] ChronicleVolumeTabIndexes = { 11, 12 };
	private static readonly string[] SectArchiveViews = { "山门总览", "山峰门人", "底蕴传承", "大阵洞天", "关系纪事" };
	private static readonly string[] FamilyArchiveViews = { "家族总览", "在世族人", "家族传承", "封邑门位", "家族纪事" };
	private static readonly string[] FamilyRealmFilters = { "全部", "余脉", "炼气", "筑基", "真人", "真君", "释修" };
	private static readonly string[] FamilyChronicleFilters = { "全部", "破境", "传承", "身故", "族仇", "斗法", "族事" };
	private static readonly string[] SectResourceViews = { "功法", "求金法", "重宝", "气", "丹药", "炼器", "符箓", "阵法" };
	private static readonly string[] FamilyWarehouseViews = { "功法", "求金法", "重宝", "气", "药材", "丹药", "炼器", "符箓" };
	private static readonly string[] CityFilters = { "全部", "宗门城", "有阵法", "治理争议", "有望族" };
	private static readonly string[] CraftArchiveViews = { "百艺总览", "丹道", "器道", "符箓", "阵法" };
	private static readonly string[] JinDanPathFilters = { "全部修法", "紫府金丹", "服气养性", "古释", "今释" };
	private static readonly string[] JinDanAchievementFilters = { "全部身份", "金丹真君", "神丹真君", "结璘仙", "郁仪仙", "真君羽士", "空证真君", "道胎", "摩诃", "法相", "世尊" };
	private static readonly string[] JinDanYinSiFilters = { "全部状态", "安稳", "阴司知悉", "追索中" };
	private static readonly string[] MechanicsGuideViews = { "玩法总览", "释修玩法", "仙国法", "明阳局", "长局机缘", "道统异闻", "龙属纪", "果余闰", "权柄神通", "家族世家", "宗门山门", "百艺器物", "年度纪事" };
	private static readonly string[] CenturyAnnalsCategories = { "总览", "本卷大事", "家族兴衰", "宗门变化", "代表人物", "境界统计", "道途纪要", "传承器物" };
	private static readonly string[] ConflictSeverityFilters = { "全部", "危急", "高", "中", "低" };
	private static readonly string[] ConflictKindFilters = { "全部", "道统", "宗门关系", "宗门治理", "世家", "城镇", "大阵", "洞天", "阴司" };
	private static readonly string[] CenturyDaoFilters =
	{
		"全部", "三阳", "三阴", "三雷", "金德", "木德", "水德", "火德", "土德", "十二炁",
		"鸺葵", "上巫", "玉真", "衡祝", "青宣", "全丹", "执孛", "司天", "都卫", "长庚"
	};

	private static XjCodexWindow _instance;
	private static GameObject _overlayBlocker;
	private static Texture2D _overlayTexture;
	private static Texture2D _whiteTexture;
	private static GUIStyle _largeLabel;
	private static GUIStyle _largeButton;
	private static GUIStyle _archiveSelectorButton;
	private static GUIStyle _largeWindow;
	private static GUIStyle _largeBox;
	private static GUIStyle _largeTextField;
	private static GUIStyle _largeTextArea;
	private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

	private bool _visible;
	private bool _pauseCaptured;
	private float _savedTimeScale = 1f;
	private bool _savedConfigPaused;
	private int _currentTab;
	private string _familyRealmFilter = "全部";
	private string _familyChronicleFilter = "全部";
	private long _familyChronicleDetailFamilyId;
	private long _familyWarehouseDetailFamilyId;
	private long _familyCultivatorDetailFamilyId;
	private string _familyWarehouseDetailView = "功法";
	private string _familySearchText = string.Empty;
	private string _cityFilter = "全部";
	private string _craftArchiveView = "百艺总览";
	private bool _craftBusyOnly;
	private string _craftSearchText = string.Empty;
	private int _craftSummaryCacheVersion = -1;
	private readonly Dictionary<string, CraftProfessionSummary> _craftSummaryCache =
		new Dictionary<string, CraftProfessionSummary>(StringComparer.Ordinal);
	private int _craftDisplayCacheVersion = -1;
	private string _craftDisplayCacheKey = string.Empty;
	private readonly List<XjCodexCraftItem> _craftDisplayCache = new List<XjCodexCraftItem>();
	private string _sectResourceView = "功法";
	private long _sectResourceDetailSectId;
	private string _sectResourceDetailView = string.Empty;
	private long _sectPeakDetailSectId;
	private int _sectPeakSelectedPeakId = int.MinValue;
	private long _formationSectFilter;
	private long _secretRealmSectFilter;
	private long _selectedSectId;
	private long _selectedFamilyId;
	private long _jinDanSectFilter;
	private long _jinDanActorFilter;
	private long _selectedJinDanActorId;
	private string _jinDanPathFilter = "全部修法";
	private string _jinDanAchievementFilter = "全部身份";
	private string _jinDanYinSiFilter = "全部状态";
	private string _jinDanSearchText = string.Empty;
	private int _jinDanDisplayCacheVersion = -1;
	private string _jinDanDisplayCacheKey = string.Empty;
	private readonly List<XjCodexJinDanItem> _jinDanDisplayCache = new List<XjCodexJinDanItem>();
	private Vector2 _jinDanListScrollPosition;
	private long _cityTargetId;
	private long _adventureTargetCityId;
	private string _adventureTargetName = string.Empty;
	private string _sectArchiveView = "山门总览";
	private string _familyArchiveView = "家族总览";
	private Vector2 _sidebarScrollPosition;
	private Vector2 _scrollPosition;
	private Vector2 _sectListScrollPosition;
	private Vector2 _sectDetailScrollPosition;
	private Vector2 _familyListScrollPosition;
	private Vector2 _familyDetailScrollPosition;
	private readonly Vector2[] _pageScrollPositions = new Vector2[TabNames.Length + 1];
	private int _returnTab = -1;
	private Vector2 _returnScrollPosition;
	private string _returnLabel = string.Empty;
	private long _sectEnrollActorId;
	private long _sectEnrollSectId;
	private string _conflictSeverityFilter = "全部";
	private string _conflictKindFilter = "全部";
	private string _centuryDaoFilter = "全部";
	private string _mechanicsGuideView = "玩法总览";
	private Rect _windowRect = new Rect(48f, 48f, 1700f, 1240f);
	// 正文可用宽度按窗口内边距、侧栏、卷页间距与纵向滚动条统一扣除。
	// 旧值只减235会高估约40-100px，多个页面据此计算卡片宽度后就会把正文
	// 撑出横向滚动条。这里宁可保守留白，也不再允许卷页横向溢出。
	private float ContentWidth => Mathf.Max(320f, _windowRect.width - 312f);
	private string _historyCategory = XjWorldHistoryCategory.All;
	private string _historyClearCategory = XjWorldHistoryCategory.All;
	private int _historyClearMaxImportance = 2;
	private bool _historyClearKeepProtected = true;
	private bool _historyImportantOnly;
	private bool _historyTraceableOnly;
	private bool _historyClearConfirm;
	private int _centuryAnnalsSelectedCenturyId;
	private string _centuryAnnalsCategory = "总览";
	private string _historyMessage = string.Empty;
	private string _focusMessage = string.Empty;
	private bool _fullSearchOpen;
	private bool _returnToFullSearch;
	private string _fullSearchQuery = string.Empty;
	private string _fullSearchCategory = "全部";
	private string _fullSearchMessage = "输入名称或卷宗关键词后检索。";
	private int _fullSearchSnapshotVersion = -1;
	private string _fullSearchExecutedQuery = string.Empty;
	private string _fullSearchExecutedCategory = "全部";
	private Vector2 _fullSearchScrollPosition;
	private readonly List<CodexSearchResult> _fullSearchResults = new List<CodexSearchResult>();
	private string _atlasView = "宗门疆域";
	private long _atlasSelectedCityId;
	private long _atlasSelectedSecretRealmSectId;
	private string _atlasSelectedAdventureRecordId = string.Empty;
	private long _renamingSectId;
	private string _renamingSectName = string.Empty;
	private string _sectRenameMessage = string.Empty;
	private long _renamingFamilyId;
	private string _renamingFamilySurname = string.Empty;
	private string _familyRenameMessage = string.Empty;
	private string _lastUiError = string.Empty;
	private long _familyWarehouseCacheFamilyId;
	private int _familyWarehouseCacheYear = -1;
	private float _familyWarehouseCacheRealtime;
	private readonly Dictionary<string, List<FamilyResourceDisplayGroup>> _familyWarehouseGroupCache =
		new Dictionary<string, List<FamilyResourceDisplayGroup>>(StringComparer.Ordinal);
	private readonly Dictionary<string, int> _familyWarehouseCountCache =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private int _sectResourceCacheVersion = -1;
	private readonly Dictionary<string, int> _sectResourceCountCache =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private readonly Dictionary<string, List<SectResourceDisplayGroup>> _sectResourceGroupCache =
		new Dictionary<string, List<SectResourceDisplayGroup>>(StringComparer.Ordinal);

	internal static bool IsOpen => _instance != null && _instance._visible;

	internal static void Ensure()
	{
		if (_instance != null) return;
		GameObject host = new GameObject("XjCodexWindow");
		DontDestroyOnLoad(host);
		_instance = host.AddComponent<XjCodexWindow>();
	}

internal static void Show(int tab = 0)
	{
		Ensure();
		if (_instance == null) return;
		_instance._visible = true;
		_instance._currentTab = Mathf.Clamp(tab, 0, TabNames.Length - 1);
		_instance._scrollPosition = _instance._pageScrollPositions[_instance._currentTab];
		_instance._renamingSectId = 0L;
		_instance._renamingSectName = string.Empty;
		_instance._sectRenameMessage = string.Empty;
		_instance._renamingFamilyId = 0L;
		_instance._renamingFamilySurname = string.Empty;
		_instance._familyRenameMessage = string.Empty;
		_instance._sectEnrollActorId = 0L;
		_instance._sectEnrollSectId = 0L;
		_instance.ClearContextReturn();
		XjCodexSnapshotPublisher.SetActiveView(_instance._currentTab, fullSearch: false);
		XjCodexSnapshotPublisher.SetWindowOpen(true);
		_instance.ApplyCodexPause();
		_instance.SyncOverlay();
	}

internal static void Toggle()
	{
		Ensure();
		if (_instance == null) return;
		_instance._visible = !_instance._visible;
		if (_instance._visible)
		{
			XjCodexSnapshotPublisher.SetActiveView(_instance._currentTab, _instance._fullSearchOpen);
			XjCodexSnapshotPublisher.SetWindowOpen(true);
			_instance.ApplyCodexPause();
		}
		else
		{
			_instance.CloseWindow();
		}
		_instance.SyncOverlay();
	}

private void Update()
	{
		if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.L)) Toggle();
		if (_visible)
		{
			EnforceCodexPause();
		}
		if (_visible && Input.GetKeyDown(KeyCode.Escape))
		{
			CloseWindow();
		}
	}

private void OnDestroy()
	{
		CloseWindow();
		if (_instance == this) _instance = null;
	}

private void OnGUI()
	{
		if (!_visible || World.world == null) return;
		EnsureTexturesAndStyles();
		CreateOverlayBlocker();
		NormalizeWindowRect();
		DrawBackdrop();

		GUIStyle oldWindow = GUI.skin.window;
		try
		{
			GUI.skin.window = _largeWindow;
			_windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "玄鉴仙鉴");
		}
		catch (Exception ex)
		{
			HandleUiException(ex);
		}
		finally
		{
			GUI.skin.window = oldWindow;
		}
	}

private void DrawWindow(int id)
	{
		GUIStyle oldLabel = GUI.skin.label;
		GUIStyle oldButton = GUI.skin.button;
		GUIStyle oldBox = GUI.skin.box;
		GUIStyle oldTextField = GUI.skin.textField;
		GUIStyle oldTextArea = GUI.skin.textArea;
		try
		{
			GUI.skin.label = _largeLabel;
			GUI.skin.button = _largeButton;
			GUI.skin.box = _largeBox ?? GUI.skin.box;
			GUI.skin.textField = _largeTextField ?? GUI.skin.textField;
			GUI.skin.textArea = _largeTextArea ?? GUI.skin.textArea;
			GUILayout.Space(18f);
			DrawCodexHeader();
			GUILayout.Space(9f);
			XjCodexSnapshotPublisher.SetActiveView(_currentTab, _fullSearchOpen);
			XjCodexSnapshot snapshot = XjCodexSnapshotPublisher.Read();
			// 宗门谱系与家族诸脉改由正文滚动承载。右栏不再套固定高度滚动框，
			// 避免少量谱系仍被强行撑满窗口、出现大片空白与横向滚动条。
			bool fixedThreeBookLayout = _currentTab == 11
				&& !string.Equals(_historyBookView, "天下纪事", StringComparison.Ordinal)
				&& !IsArchiveStackedLayout();
			// 修士名录只保留左侧名单的局部滚动；人物详情交给仙鉴正文唯一纵向滚动，
			// 避免右栏再套一根细长滚动条造成滚轮焦点争抢。
			bool fixedPageLayout = !_fullSearchOpen && fixedThreeBookLayout;
			GUILayout.BeginHorizontal();
			DrawCodexSidebar();
			GUILayout.Space(8f);
			float bodyWidth = Mathf.Max(320f, _windowRect.width - 286f);
			GUILayout.BeginVertical(GUILayout.Width(bodyWidth), GUILayout.ExpandHeight(true));
			if (!fixedPageLayout)
			{
				// 仙鉴所有正文卷统一只允许纵向滚动。页面布局必须在当前正文宽度内
				// 自适应换列，而不能依赖底部横向滚动条兜底；这也避免鼠标滚轮在
				// 横纵两根滚动条之间抢焦点。
				_scrollPosition.x = 0f;
				_scrollPosition = GUILayout.BeginScrollView(
					_scrollPosition, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandWidth(true));
				_scrollPosition.x = 0f;
			}
			DrawContextReturnBar();
			if (snapshot == null || snapshot.Version <= 0 || snapshot.ViewKey != XjCodexSnapshotPublisher.DesiredViewKey)
			{
				DrawWaitingPage();
			}
			else if (_fullSearchOpen)
			{
				DrawFullSearchPage(snapshot);
			}
			else
			{
				switch (_currentTab)
				{
					case 0: DrawWorldOverview(snapshot); break;
					case 1: DrawSectPage(snapshot); break;
					case 2: DrawSectResourcePage(snapshot); break;
					case 3: DrawFamilyPage(snapshot); break;
					case 4: DrawCityPage(snapshot); break;
					case 5: DrawFormationPage(snapshot); break;
					case 6: DrawSecretRealmPage(snapshot); break;
					case 7: DrawCraftPage(snapshot); break;
					case 8: DrawJinDanPage(snapshot); break;
					case 9: DrawAdventureRealmPage(snapshot); break;
					case 10: DrawConflictPage(snapshot); break;
					case 11: DrawHistoryPage(snapshot); break;
					case 12: DrawCenturyAnnalsPage(snapshot); break;
					case 13: DrawSwordIntentPage(snapshot); break;
					case 14: DrawCultivationClimatePage(snapshot); break;
					case 15: DrawWorldAtlasPage(snapshot); break;
					case 16: DrawWorldOverview(snapshot); break;
					case 17: DrawDaoTuArchivePage(); break;
					case 18: DrawGuoWeiAuthorityPage(); break;
					case 19: DrawXianGuoPage(); break;
					case 20: DrawMechanicsGuidePage(); break;
					case 21: DrawDaoTuArchivePage(); break;
					case 22: DrawShiDomainPage(); break;
				}
			}
			DrawFocusMessage();
			if (!fixedPageLayout) GUILayout.EndScrollView();
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
			GUI.DragWindow();
		}
		finally
		{
			GUI.enabled = true;
			GUI.backgroundColor = Color.white;
			GUI.skin.label = oldLabel;
			GUI.skin.button = oldButton;
			GUI.skin.box = oldBox;
			GUI.skin.textField = oldTextField;
			GUI.skin.textArea = oldTextArea;
		}
	}

private void DrawCodexHeader()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#B9C4D9");
		GUILayout.Space(4f);
		GUILayout.BeginHorizontal();
		GUILayout.Label("<b><color=#D6DCE8>玄鉴仙鉴</color></b>", GUILayout.Width(110f));
		GUILayout.Label("<b><color=#F0D58B>" + Rich(GetCurrentVolumeName()) + "</color></b>");
		GUILayout.FlexibleSpace();
		bool hasPendingChanges = XjCodexSnapshotPublisher.Read().Version > 0
			&& !string.Equals(XjCodexSnapshotPublisher.DirtySummary, "无", StringComparison.Ordinal);
		// 重照入口始终占据固定宽度，既避免天下状态变化时头栏左右跳动，
		// 也让玩家随时可以主动核对当前卷页，不再依赖 Dirty 状态才出现按钮。
		GUILayout.Label(hasPendingChanges ? "<color=#C7B98E>天下已有变化</color>" : string.Empty, GUILayout.Width(118f));
		GUI.backgroundColor = new Color(0.40f, 0.36f, 0.24f, 1f);
		if (GUILayout.Button("重新照录", GUILayout.Width(96f), GUILayout.Height(34f)))
		{
			XjCodexSnapshotPublisher.RefreshNow();
			ReleaseCompanionRecordCaches();
		}
		GUI.backgroundColor = _fullSearchOpen
			? new Color(0.38f, 0.48f, 0.55f, 1f)
			: new Color(0.27f, 0.32f, 0.38f, 1f);
		if (GUILayout.Button(_fullSearchOpen ? "返回当前卷" : "全卷检索", GUILayout.Width(112f), GUILayout.Height(34f)))
		{
			ToggleFullSearch();
		}
		GUI.backgroundColor = new Color(0.42f, 0.30f, 0.28f, 1f);
		if (GUILayout.Button("关闭", GUILayout.Width(72f), GUILayout.Height(34f))) CloseWindow();
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
	}

	private void DrawCodexSidebar()
	{
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(205f), GUILayout.ExpandHeight(true));
		DrawCardStripe("#6FAE9D");
		_sidebarScrollPosition = GUILayout.BeginScrollView(
			_sidebarScrollPosition, false, true, GUILayout.Width(197f), GUILayout.ExpandHeight(true));
		DrawSidebarVolume("天下", WorldVolumeTabIndexes, "#AFC7D9");
		DrawSidebarVolume("山河诸势", SectAndFamilyVolumeTabIndexes, "#D6BE86");
		DrawSidebarVolume("修行道统", CultivationVolumeTabIndexes, "#9FC9C0");
		DrawSidebarVolume("春秋史册", ChronicleVolumeTabIndexes, "#8FA9C7");
		GUI.backgroundColor = Color.white;
		GUILayout.EndScrollView();
		GUILayout.EndVertical();
	}

	private void DrawSidebarVolume(string volumeName, IReadOnlyList<int> tabIndexes, string colorHex)
	{
		GUILayout.Space(6f);
		GUILayout.Label("<b><color=" + colorHex + ">" + Rich(volumeName) + "</color></b>");
		for (int i = 0; i < tabIndexes.Count; i++)
		{
			int tabIndex = tabIndexes[i];
			if (tabIndex < 0 || tabIndex >= TabNames.Length) continue;
			GUI.backgroundColor = _currentTab == tabIndex
				? ParseHexColor(colorHex, new Color(0.52f, 0.58f, 0.62f, 1f))
				: new Color(0.23f, 0.23f, 0.25f, 1f);
			if (GUILayout.Button(TabNames[tabIndex], GUILayout.Height(34f))) SelectCodexTab(tabIndex);
		}
		GUI.backgroundColor = Color.white;
	}

	private string GetCurrentVolumeName()
		{
			if (_fullSearchOpen) return "全卷检索 · 搜四卷有名有据之物";
			if (_currentTab >= 0 && _currentTab < TabNames.Length)
			{
				return GetVolumeName(_currentTab) + " · " + TabNames[_currentTab];
			}
			return "天下总览";
		}

		private static string GetVolumeName(int tabIndex)
		{
			if (Array.IndexOf(WorldVolumeTabIndexes, tabIndex) >= 0) return "天下卷";
			if (Array.IndexOf(SectAndFamilyVolumeTabIndexes, tabIndex) >= 0 || tabIndex == 2 || tabIndex == 5 || tabIndex == 6) return "山河诸势";
			if (Array.IndexOf(CultivationVolumeTabIndexes, tabIndex) >= 0) return "修行道统";
			if (Array.IndexOf(ChronicleVolumeTabIndexes, tabIndex) >= 0) return "春秋史册";
			return "玄鉴仙鉴";
		}

		private void SelectCodexTab(int tabIndex)
		{
			if (_fullSearchOpen)
			{
				_fullSearchScrollPosition = _scrollPosition;
				_fullSearchOpen = false;
				_scrollPosition = _currentTab >= 0 && _currentTab < _pageScrollPositions.Length
					? _pageScrollPositions[_currentTab]
					: Vector2.zero;
			}
			if (_currentTab >= 0 && _currentTab < _pageScrollPositions.Length)
			{
				_pageScrollPositions[_currentTab] = _scrollPosition;
			}
			_currentTab = Mathf.Clamp(tabIndex, 0, TabNames.Length - 1);
			_scrollPosition = _pageScrollPositions[_currentTab];
			XjCodexSnapshotPublisher.SetActiveView(_currentTab, fullSearch: false);
			ClearContextReturn();
		}


private void CloseWindow()
		{
			_visible = false;
			_sectEnrollActorId = 0L;
			_sectEnrollSectId = 0L;
			_renamingSectId = 0L;
			_renamingSectName = string.Empty;
			_renamingFamilyId = 0L;
			_renamingFamilySurname = string.Empty;
			_familyRenameMessage = string.Empty;
			ReleaseUiCaches();
		XjCodexSnapshotPublisher.SetWindowOpen(false);
		DestroyOverlayBlocker();
		ReleaseCodexPause();
	}

	private void ReleaseUiCaches()
	{
		_familyWarehouseGroupCache.Clear();
		_familyWarehouseCountCache.Clear();
		_familyWarehouseCacheFamilyId = 0L;
		_familyWarehouseCacheYear = -1;
		_familyWarehouseCacheRealtime = 0f;
		_sectResourceGroupCache.Clear();
		_sectResourceCountCache.Clear();
		_sectResourceCacheVersion = -1;
		_jinDanDisplayCache.Clear();
		_jinDanDisplayCacheVersion = -1;
		_jinDanDisplayCacheKey = string.Empty;
		_craftSummaryCache.Clear();
		_craftSummaryCacheVersion = -1;
		_craftDisplayCache.Clear();
		_craftDisplayCacheVersion = -1;
		_craftDisplayCacheKey = string.Empty;
		_fullSearchResults.Clear();
		_fullSearchOpen = false;
		_returnToFullSearch = false;
		_fullSearchQuery = string.Empty;
		_fullSearchCategory = "全部";
		_fullSearchMessage = "输入名称或卷宗关键词后检索。";
		_fullSearchSnapshotVersion = -1;
		_fullSearchExecutedQuery = string.Empty;
		_fullSearchExecutedCategory = "全部";
		_fullSearchScrollPosition = Vector2.zero;
		_swordIntentPage = 0;
		_swordIntentSummaryVersion = -1;
		_swordIntentTypeCount = 0;
		_daoTuOverviewRows.Clear();
		_daoTuOverviewRevision = int.MinValue;
		_daoTuOverviewYear = -1;
		_atlasView = "宗门疆域";
		_atlasSelectedCityId = 0L;
		_atlasSelectedSecretRealmSectId = 0L;
		_atlasSelectedAdventureRecordId = string.Empty;
		_atlasLayoutCacheVersion = -1;
		_atlasSectBoundsCache.Clear();
		_atlasFamilyBoundsCache.Clear();
		ReleaseCompanionRecordCaches();
		_spriteCache.Clear();
	}

private void ApplyCodexPause()
	{
		if (_pauseCaptured)
		{
			EnforceCodexPause();
			return;
		}

		_savedTimeScale = Time.timeScale;
		_savedConfigPaused = Config.paused;
		_pauseCaptured = true;
		XjRuntimePauseGate.SetCodexOpen(true);
		EnforceCodexPause();
	}

private void EnforceCodexPause()
	{
		Config.paused = true;
		Time.timeScale = 0f;
	}

private void ReleaseCodexPause()
	{
		if (!_pauseCaptured)
		{
			return;
		}

		XjRuntimePauseGate.SetCodexOpen(false);
		Config.paused = _savedConfigPaused;
		Time.timeScale = _savedTimeScale < 0f ? 1f : _savedTimeScale;
		_pauseCaptured = false;
	}

private static int Count(IReadOnlyDictionary<string, int> counts, string key) => counts.TryGetValue(key, out int count) ? count : 0;
	private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
	private static string DescribeLoreStrength(int value)
	{
		int normalized = Mathf.Clamp(value, 0, 100);
		if (normalized <= 0) return "未显";
		if (normalized < 20) return "微弱";
		if (normalized < 40) return "初显";
		if (normalized < 60) return "渐成";
		if (normalized < 80) return "稳固";
		if (normalized < 95) return "深厚";
		return "近乎圆满";
	}

	private static string DescribeLoreRatio(float value)
	{
		return DescribeLoreStrength(Mathf.RoundToInt(Mathf.Clamp01(value) * 100f));
	}

	private static string DescribeLoreMultiplier(float value)
	{
		if (value <= 0f) return "停滞";
		if (value < 0.75f) return "缓慢";
		if (value < 0.95f) return "稍缓";
		if (value < 1.10f) return "平稳";
		if (value < 1.35f) return "较快";
		return "迅捷";
	}

	private static string DescribeLoreGate(float value)
	{
		if (value < 0.50f) return "宽松";
		if (value < 0.70f) return "平衡";
		if (value < 0.85f) return "严谨";
		return "严苛";
	}
	private static string FormatFamilyDisplayName(string value, long familyStableId, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(value))
		{
			return value.Trim();
		}
		if (familyStableId > 0L)
		{
			string resolved = XjFamilyDisplayNameResolver.Resolve(familyStableId);
			if (!string.IsNullOrWhiteSpace(resolved))
			{
				return resolved;
			}
		}
		return fallback;
	}

private static string FormatFamilyResponsibility(XjCodexFamilyItem item)
	{
		if (item == null) return "暂无职责";
		if (!string.IsNullOrWhiteSpace(item.Responsibility)) return item.Responsibility.Trim();
		if (item.JinDanCount > 0) return item.GoverningCityCount > 0 ? "真君家族治理城镇并主掌宗门资源" : "真君家族主掌宗门资源";
		if (item.ZiFuCount > 0) return item.GoverningCityCount > 0 ? "真人家族治理城镇并参掌宗门" : "真人家族参掌宗门";
		if (item.SectId > 0L || !string.IsNullOrWhiteSpace(item.SectName)) return "宗门共务";
		return item.GoverningCityCount > 0 ? "城镇望族" : "家族自立";
	}

private static bool IsHighRealmFamily(XjCodexFamilyItem item)
	{
		return item != null && (item.JinDanCount > 0 || item.ZiFuCount > 0);
	}

private static string FormatFamilyResourceSeat(XjCodexFamilyItem item)
	{
		if (item == null) return "0";
		if (item.JinDanCount > 0) return item.VoiceScore >= 65f ? "主掌" : "优先";
		if (item.ZiFuCount > 0) return item.VoiceScore >= 45f ? "参掌" : "取用";
		return Mathf.RoundToInt(item.SupplyDebt).ToString();
	}

private static string Truncate(string value, int max)
	{
		if (string.IsNullOrWhiteSpace(value)) return string.Empty;
		string trimmed = value.Trim();
		if (max <= 0 || trimmed.Length <= max) return trimmed;
		return trimmed.Substring(0, max) + "...";
	}

private static bool HasToken(string value, string token)
	{
		return !string.IsNullOrWhiteSpace(value)
			&& !string.IsNullOrWhiteSpace(token)
			&& value.IndexOf(token, StringComparison.Ordinal) >= 0;
	}

private static string Rich(string value)
	{
		return XjUiSafeText.Rich(value, string.Empty);
	}
	private static string ResolveCanonCategory(string view, string kind)
	{
		string value = (kind ?? string.Empty) + "|" + (view ?? string.Empty);
		if (HasToken(value, "洞天") || HasToken(value, "福地")) return XuanJianVNext.Data.Lore.XjCanonCategory.DongTian;
		if (HasToken(value, "果位")) return XuanJianVNext.Data.Lore.XjCanonCategory.GuoWei;
		if (HasToken(value, "权柄")) return XuanJianVNext.Data.Lore.XjCanonCategory.QuanBing;
		if (HasToken(value, "仙基")) return XuanJianVNext.Data.Lore.XjCanonCategory.XianJi;
		if (HasToken(value, "法宝") || HasToken(value, "灵宝") || HasToken(value, "炼器") || HasToken(value, "重宝")) return XuanJianVNext.Data.Lore.XjCanonCategory.FaBao;
		if (HasToken(value, "功法") || HasToken(value, "求金法") || HasToken(value, "采气法")) return XuanJianVNext.Data.Lore.XjCanonCategory.GongFa;
		if (HasToken(value, "气") || HasToken(value, "道途")) return XuanJianVNext.Data.Lore.XjCanonCategory.DaoTu;
		return XuanJianVNext.Data.Lore.XjCanonCategory.GongFa;
	}

	private static string ProfessionColor(string profession) => profession == "炼丹师" ? "#A7E08A" : profession == "炼器师" ? "#FFD37A" : profession == "符箓师" ? "#9CD7FF" : "#B7A7FF";
	private static string SectResourceColor(string kind) => kind == "功法" ? "#B7A7FF" : kind == "求金法" ? "#FFD37A" : kind == "纳气" ? "#A7E08A" : "#9CD7FF";
	private static string SectResourceViewColor(string view) => view == "功法" ? "#B7A7FF" : view == "重宝" ? "#FFD37A" : view == "气" ? "#A7E08A" : view == "药材" ? "#A7E08A" : view == "丹药" ? "#A7E08A" : view == "炼器" ? "#FFD37A" : "#9CD7FF";
	private static string FormatGongFaGrade(int grade) => grade <= 0 ? "未载" : XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade);
	private static string HistoryCategoryColor(string category) => category == XjWorldHistoryCategory.Sect ? "#FFD37A" : category == XjWorldHistoryCategory.Family ? "#A7E08A" : category == XjWorldHistoryCategory.Resource ? "#B7A7FF" : category == XjWorldHistoryCategory.HighRealm ? "#FF8877" : category == XjWorldHistoryCategory.SecretRealm ? "#9CD7FF" : category == XjWorldHistoryCategory.Craft ? "#76C7D5" : category == XjWorldHistoryCategory.YinSi ? "#FF6666" : category == XjWorldHistoryCategory.Evil ? "#FFAA66" : "#CFC7B2";
	private static string ImportanceColor(int importance)
	{
		if (importance <= 0) return "#888888";
		if (importance == 1) return "#A7E08A";
		if (importance == 2) return "#9CD7FF";
		if (importance == 3) return "#B7A7FF";
		if (importance == 4) return "#FFD37A";
		return "#FF7777";
	}
	private static string StateColor(string state) => state == XjAdventureRealmClaimState.Contested ? "#FF8877" : state == XjAdventureRealmClaimState.Guarded ? "#A7E08A" : state == XjAdventureRealmClaimState.ClaimWindow ? "#FFAA66" : "#B7A7FF";
	private static string ResolveAdventureClaimLabel(XjCodexAdventureRealmItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.ClaimSectName)) return "主张宗门";
		if (!string.IsNullOrWhiteSpace(item.ClaimKingdomName)) return "主张国家";
		return "主张势力";
	}

private static string ResolveAdventureClaimName(XjCodexAdventureRealmItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.ClaimSectName)) return item.ClaimSectName.Trim();
		if (!string.IsNullOrWhiteSpace(item.ClaimKingdomName)) return item.ClaimKingdomName.Trim();
		return "无主";
	}

private static string ResolveAdventureClaimColor(XjCodexAdventureRealmItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.ClaimSectName)) return "#FFD37A";
		if (!string.IsNullOrWhiteSpace(item.ClaimKingdomName)) return "#9CD7FF";
		return "#888888";
	}

private static string TranslateSectStatus(string state)
	{
		if (state == "SectRegime") return "真人宗门";
		if (state == "FudiSect") return "福地宗门";
		if (state == "DongtianSect") return "洞天宗门";
		if (state == "LandlessSect") return "失地宗门";
		if (state == "Extinct") return "断绝";
		return "宗门政体";
	}

private static string TranslateSectStatus(string state, int jinDanCount)
	{
		if (jinDanCount > 0
			&& state != "FudiSect"
			&& state != "DongtianSect"
			&& state != "LandlessSect"
			&& state != "Extinct")
		{
			return "真君宗门";
		}
		return TranslateSectStatus(state);
	}

private static string TranslateGovernanceState(string state)
		=> state == XjCityFamilyGovernanceState.Prominent ? "望族"
			: state == XjCityFamilyGovernanceState.Stable ? "稳定"
			: state == XjCityFamilyGovernanceState.Challenged ? "挑战中"
			: state == XjCityFamilyGovernanceState.Reassigned ? "改封"
			: state == "Contested" ? "争议"
			: state == "Integration" ? "整合期"
			: Empty(state, "未确认");
	private static string TranslateFormationState(string state) => state == XjSectFormationBuildState.Operational ? "运转" : state == XjSectFormationBuildState.Damaged ? "受损" : state == XjSectFormationBuildState.Repairing ? "维修中" : state == XjSectFormationBuildState.Broken ? "已破" : state == XjSectFormationBuildState.Deploying ? "部署中" : state == XjSectFormationBuildState.MaterialCommitted ? "材料已定" : "未建";
	private static string TranslateAdventureState(string state) => state == XjAdventureRealmClaimState.Unclaimed ? "未主" : state == XjAdventureRealmClaimState.Discovered ? "已发现" : state == XjAdventureRealmClaimState.ClaimWindow ? "归属确认" : state == XjAdventureRealmClaimState.Guarded ? "归属已定" : state == XjAdventureRealmClaimState.Contested ? "争夺中" : state == XjAdventureRealmClaimState.Resolved ? "归属已定" : state == XjAdventureRealmClaimState.Closed ? "已关闭" : "未知";
	private static string TranslateSecretRealmStage(string stage)
	{
		return XjDisplayNameSanitizer.SecretRealmStage(stage);
	}

private static string TranslateLectureType(string type) => type == XjSectLectureType.ZhenJun ? "真君讲道" : "真人开坛";

	private static string TranslateYinSiMissionStage(string stage)
	{
		if (stage == XjYinSiMissionStage.Scheduled) return "追索已定";
		if (stage == XjYinSiMissionStage.Locating) return "定位中";
		if (stage == XjYinSiMissionStage.Manifesting) return "追索临身";
		if (stage == XjYinSiMissionStage.Pursuing) return "追索中";
		if (stage == XjYinSiMissionStage.Evaded) return "本轮暂避";
		return "未知";
	}

private static string TranslateYinSiState(string state, bool known)
	{
		if (known && state == XjJinDanYinSiState.Pursuing) return "追索中";
		if (known && state == XjJinDanYinSiState.Locating) return "定位中";
		if (known && state == XjJinDanYinSiState.Evaded) return "暂避追索";
		if (known) return "已被知悉";
		if (state == XjJinDanYinSiState.NearDiscovery) return "阴司将觉";
		if (state == XjJinDanYinSiState.Traced) return "已留痕迹";
		return "隐匿无迹";
	}

private static string FormatFormationSummary(string name, int grade, int current, int max, string state, string leadName)
	{
		if (grade <= 0) return "<color=#888888>未建</color>";
		string color = current <= 0 ? "#FF8877" : "#A7E08A";
		return "<color=" + color + "><b>" + Rich(Empty(name, ResolveFormationDisplayName(grade)))
			+ " · 第" + grade + "阶 · 耐久" + current + "-" + max
			+ " · " + Rich(TranslateFormationState(state))
			+ " · 构造者" + Rich(Empty(leadName, "未任命")) + "</b></color>";
	}

private static string PlainFormationSummary(string name, int grade, int current, int max, string state, string leadName)
	{
		if (grade <= 0) return "未建";
		return Empty(name, ResolveFormationDisplayName(grade))
			+ " · 第" + grade + "阶 · 耐久" + current + "-" + max
			+ " · " + TranslateFormationState(state)
			+ " · 构造者" + Empty(leadName, "未任命");
	}

private static Color ParseHexColor(string hex, Color fallback)
	{
		if (!string.IsNullOrWhiteSpace(hex) && ColorUtility.TryParseHtmlString(hex, out Color color)) return color;
		return fallback;
	}
}
