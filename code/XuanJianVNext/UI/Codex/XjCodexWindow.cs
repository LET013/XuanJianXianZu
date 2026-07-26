using System;
using System.Collections.Generic;
using System.Reflection;
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
		"玄鉴四艺", "真君名录", "奇遇洞天", "天下风险", "玄鉴史册", "百年世谱"
	};
	private static readonly string[] FamilyRealmFilters = { "全部", "余脉", "炼气", "筑基", "紫府", "金丹" };
	private static readonly string[] FamilyChronicleFilters = { "全部", "破境", "传承", "身故", "族仇", "斗法", "族事" };
	private static readonly string[] SectResourceViews = { "功法", "求金法", "重宝", "气", "丹药", "炼器", "符箓", "阵法" };
	private static readonly string[] FamilyWarehouseViews = { "功法", "求金法", "重宝", "气", "药材", "丹药", "炼器", "符箓" };
	private static readonly string[] CityFilters = { "全部", "宗门城", "有阵法", "治理争议", "有望族" };
	private static readonly string[] CraftProfessionFilters = { "全部", "炼丹师", "炼器师", "符箓师", "阵法师", "任务中" };
	private static readonly string[] CenturyAnnalsCategories = { "总览", "家族兴衰", "宗门变化", "代表人物", "境界统计", "道途纪要", "传承器物" };
	private static readonly string[] ConflictSeverityFilters = { "全部", "危急", "高", "中", "低" };
	private static readonly string[] ConflictKindFilters = { "全部", "宗门关系", "宗门治理", "世家", "城镇", "大阵", "洞天", "阴司" };
	private static readonly string[] CenturyDaoFilters = { "全部", "三阳", "三阴", "三雷", "金德", "木德", "水德", "火德", "土德", "十二炁" };

	private static XjCodexWindow _instance;
	private static GameObject _overlayBlocker;
	private static Texture2D _overlayTexture;
	private static Texture2D _whiteTexture;
	private static GUIStyle _largeLabel;
	private static GUIStyle _largeButton;
	private static GUIStyle _largeWindow;
	private static GUIStyle _largeBox;
	private static GUIStyle _largeTextField;
	private static GUIStyle _largeTextArea;
	private static readonly Dictionary<string, Sprite> _spriteCache = new Dictionary<string, Sprite>(StringComparer.Ordinal);

	private bool _visible;
	private bool _pauseCaptured;
	private float _savedTimeScale = 1f;
	private bool _savedConfigPaused;
	private bool _debugTabUnlocked;
	private int _currentTab;
	private string _familyRealmFilter = "全部";
	private string _familyChronicleFilter = "全部";
	private long _familyChronicleDetailFamilyId;
	private long _familyWarehouseDetailFamilyId;
	private long _familyCultivatorDetailFamilyId;
	private string _familyWarehouseDetailView = "功法";
	private string _familySearchText = string.Empty;
	private string _cityFilter = "全部";
	private string _craftProfessionFilter = "全部";
	private string _craftCatalogView = string.Empty;
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
	private Vector2 _scrollPosition;
	private Vector2 _familyListScrollPosition;
	private long _sectEnrollActorId;
	private long _sectEnrollSectId;
	private string _conflictSeverityFilter = "全部";
	private string _conflictKindFilter = "全部";
	private string _centuryDaoFilter = "全部";
	private Rect _windowRect = new Rect(60f, 60f, 1600f, 1230f);
	private string _historyCategory = XjWorldHistoryCategory.All;
	private string _historyClearCategory = XjWorldHistoryCategory.All;
	private int _historyClearMaxImportance = 2;
	private bool _historyClearKeepProtected = true;
	private bool _historyImportantOnly;
	private bool _historyTraceableOnly;
	private bool _historyClearConfirm;
	private int _historySuppressUntilVersion = -1;
	private int _centuryAnnalsSelectedCenturyId;
	private string _centuryAnnalsCategory = "总览";
	private string _historyMessage = string.Empty;
	private string _focusMessage = string.Empty;
	private long _renamingSectId;
	private string _renamingSectName = string.Empty;
	private string _sectRenameMessage = string.Empty;
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
	private static readonly BindingFlags FocusBindingFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

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
		_instance._scrollPosition = Vector2.zero;
		_instance._sectResourceDetailSectId = 0L;
		_instance._sectResourceDetailView = string.Empty;
		_instance._sectPeakDetailSectId = 0L;
		_instance._formationSectFilter = 0L;
		_instance._secretRealmSectFilter = 0L;
		_instance._sectPeakSelectedPeakId = int.MinValue;
		_instance._renamingSectId = 0L;
		_instance._renamingSectName = string.Empty;
		_instance._sectRenameMessage = string.Empty;
		_instance._familyChronicleDetailFamilyId = 0L;
		_instance._familyWarehouseDetailFamilyId = 0L;
		_instance._familyCultivatorDetailFamilyId = 0L;
		_instance._sectEnrollActorId = 0L;
		_instance._sectEnrollSectId = 0L;
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
		if (Input.GetKey(KeyCode.LeftShift) && Input.GetKeyDown(KeyCode.D))
		{
			_debugTabUnlocked = !_debugTabUnlocked;
			_visible = true;
			_currentTab = _debugTabUnlocked ? TabNames.Length : 0;
			ApplyCodexPause();
			SyncOverlay();
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
			_windowRect = GUI.Window(WindowId, _windowRect, DrawWindow, "玄鉴仙族 · 玄鉴仙鉴");
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
			DrawTabRow();
			GUILayout.Space(9f);
			XjCodexSnapshot snapshot = XjCodexSnapshotPublisher.Read();
			bool fixedFamilyLayout = _currentTab == 3
				&& _familyCultivatorDetailFamilyId <= 0L
				&& _familyWarehouseDetailFamilyId <= 0L
				&& _familyChronicleDetailFamilyId <= 0L;
			bool fixedThreeBookLayout = _currentTab == 11
				&& !string.Equals(_historyBookView, "天下纪事", StringComparison.Ordinal);
			bool fixedPageLayout = fixedFamilyLayout || fixedThreeBookLayout;
			if (!fixedPageLayout) _scrollPosition = GUILayout.BeginScrollView(_scrollPosition);
			if (snapshot == null || snapshot.Version <= 0)
			{
				DrawWaitingPage();
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
					case 13: DrawDiagnosticsPage(snapshot); break;
				}
			}
			DrawFocusMessage();
			if (!fixedPageLayout) GUILayout.EndScrollView();
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

private void DrawTabRow()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#B9C4D9");
		GUILayout.Space(4f);
		GUILayout.BeginHorizontal();
		for (int i = 0; i < TabNames.Length; i++)
		{
			if (i == 2 || i == 5 || i == 6) continue;
			GUI.backgroundColor = _currentTab == i ? new Color(0.72f, 0.76f, 0.84f, 1f) : new Color(0.30f, 0.30f, 0.32f, 1f);
			if (GUILayout.Button(TabNames[i], GUILayout.Height(42f)))
			{
				_currentTab = i;
				_scrollPosition = Vector2.zero;
				_sectResourceDetailSectId = 0L;
				_sectResourceDetailView = string.Empty;
				_sectPeakDetailSectId = 0L;
				_sectPeakSelectedPeakId = int.MinValue;
				_familyChronicleDetailFamilyId = 0L;
				_familyWarehouseDetailFamilyId = 0L;
				_familyCultivatorDetailFamilyId = 0L;
				_formationSectFilter = 0L;
				_secretRealmSectFilter = 0L;
			}
		}
		if (_debugTabUnlocked)
		{
			GUI.backgroundColor = _currentTab == TabNames.Length ? new Color(0.72f, 0.76f, 0.84f, 1f) : new Color(0.30f, 0.30f, 0.32f, 1f);
			if (GUILayout.Button("运行诊断", GUILayout.Height(42f)))
			{
				_currentTab = TabNames.Length;
				_scrollPosition = Vector2.zero;
			}
		}
		GUI.backgroundColor = new Color(0.75f, 0.77f, 0.82f, 1f);
		if (GUILayout.Button("✕", GUILayout.Width(45f), GUILayout.Height(42f)))
		{
			CloseWindow();
		}
		GUI.backgroundColor = Color.white;
		GUILayout.EndHorizontal();
		GUILayout.Space(2f);
		GUILayout.EndVertical();
	}


private void CloseWindow()
	{
		_visible = false;
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
		if (item.JinDanCount > 0) return item.GoverningCityCount > 0 ? "金丹家族治理城镇并主掌宗门资源" : "金丹家族主掌宗门资源";
		if (item.ZiFuCount > 0) return item.GoverningCityCount > 0 ? "紫府家族治理城镇并参掌宗门" : "紫府家族参掌宗门";
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

private static string Rich(string value) => (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
	private static string CanonTagForResource(string view, string name, string kind, string daoTu)
	{
		return string.Empty;
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
		if (state == "SectRegime") return "紫府宗门";
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
			return "金丹宗门";
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








