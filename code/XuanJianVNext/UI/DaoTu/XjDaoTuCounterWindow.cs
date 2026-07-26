using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.DaoTu;

internal class XjDaoTuCounterUpdater : MonoBehaviour
{
	public Action onWindowOpen;

	public Action onWindowClose;

	private void OnEnable()
	{
		onWindowOpen?.Invoke();
	}

	private void OnDisable()
	{
		onWindowClose?.Invoke();
	}

}

internal static class XjDaoTuCounterWindow
{
	private static bool _initialized;
	private static ScrollWindow _windowInstance;
	private static RectTransform _contentRect;
	private static Transform _contentTransform;
	private static Text _emptyText;
	private static readonly List<GameObject> _rows = new List<GameObject>();

	private const float WindowWidth = 320f;
	private const float WindowHeight = 340f;
	private const float CardHeight = 58f;
	private const float CardSpacing = 2f;
	public static void Init()
	{
		if (_initialized)
		{
			return;
		}
		_windowInstance = WindowCreator.CreateEmptyWindow("XuanJianDaoTuCounter", "xuanjian.daotu.counter.overview", "ui/Icons/items/DaoTuKeZhi");
		SetupWindowContent();
		XjDaoTuCounterUpdater updater = ((Component)_windowInstance).gameObject.AddComponent<XjDaoTuCounterUpdater>();
		updater.onWindowOpen = RefreshList;
		updater.onWindowClose = OnWindowClose;
		_initialized = true;
	}

	public static void ShowWindow()
	{
		bool createdNow = !_initialized;
		if (!_initialized)
		{
			Init();
		}
		if (createdNow)
		{
			RefreshList();
		}
		XjWindowOpenGuard.Show(_windowInstance, "XuanJianDaoTuCounter", createdNow);
	}

	private static void SetupWindowContent()
	{
		if ((Object)(object)_windowInstance == (Object)null)
		{
			return;
		}
		Transform background = ((Component)_windowInstance).transform.Find("Background");
		if ((Object)(object)background == (Object)null)
		{
			return;
		}
		RectTransform bgRect = ((Component)background).GetComponent<RectTransform>();
		if ((Object)(object)bgRect != (Object)null)
		{
			bgRect.sizeDelta = new Vector2(WindowWidth, WindowHeight);
		}
		CreateHeader(background);
		CreateScrollView(background);
		CreateEmptyText(background);
	}

	private static void EnsureContentReady()
	{
		if ((Object)(object)_contentRect == (Object)null || (Object)(object)_contentTransform == (Object)null || (Object)(object)_emptyText == (Object)null)
		{
			SetupWindowContent();
		}
	}

	private static void CreateHeader(Transform parent)
	{
		GameObject header = new GameObject("Header");
		header.transform.SetParent(parent, false);
		RectTransform headerRect = header.AddComponent<RectTransform>();
		headerRect.anchorMin = new Vector2(0f, 1f);
		headerRect.anchorMax = new Vector2(1f, 1f);
		headerRect.pivot = new Vector2(0.5f, 1f);
		headerRect.anchoredPosition = new Vector2(0f, -32f);
		headerRect.sizeDelta = new Vector2(-16f, 28f);

		// \u6807\u9898\uFF08\u9EC4\u8272\u9ED1\u4F53\uFF0C\u5F80\u4E0A\u632A\uFF09
		GameObject titleObj = new GameObject("HeaderTitle");
		titleObj.transform.SetParent(header.transform, false);
		RectTransform titleRect = titleObj.AddComponent<RectTransform>();
		titleRect.anchorMin = new Vector2(0.5f, 0.5f);
		titleRect.anchorMax = new Vector2(0.5f, 0.5f);
		titleRect.pivot = new Vector2(0.5f, 0.5f);
		titleRect.anchoredPosition = new Vector2(0f, 4f);
		titleRect.sizeDelta = new Vector2(260f, 22f);
		Text titleLabel = titleObj.AddComponent<Text>();
		titleLabel.font = LocalizedTextManager.current_font;
		titleLabel.fontSize = 10;
		titleLabel.alignment = TextAnchor.MiddleCenter;
		titleLabel.fontStyle = FontStyle.Bold;
		((Graphic)titleLabel).color = new Color(1f, 0.92f, 0.2f);
		titleLabel.text = "\u9053\u9014\u514B\u5236";
	}

	private static void CreateHeaderText(Transform parent, string text, Vector2 anchoredPos, Vector2 size)
	{
		GameObject obj = new GameObject("Header_" + text);
		obj.transform.SetParent(parent, false);
		RectTransform rect = obj.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 0.5f);
		rect.anchorMax = new Vector2(0f, 0.5f);
		rect.pivot = new Vector2(0f, 0.5f);
		rect.anchoredPosition = anchoredPos;
		rect.sizeDelta = size;
		Text label = obj.AddComponent<Text>();
		label.font = LocalizedTextManager.current_font;
		label.fontSize = 10;
		label.alignment = (TextAnchor)3;
		((Graphic)label).color = new Color(0.95f, 0.82f, 0.55f);
		label.text = text;
	}

	private static void CreateScrollView(Transform parent)
	{
		GameObject scrollViewObj = new GameObject("ScrollView");
		scrollViewObj.transform.SetParent(parent, false);
		RectTransform scrollRect = scrollViewObj.AddComponent<RectTransform>();
		scrollRect.anchorMin = new Vector2(0f, 0f);
		scrollRect.anchorMax = new Vector2(1f, 1f);
		scrollRect.offsetMin = new Vector2(8f, 12f);
		scrollRect.offsetMax = new Vector2(-8f, -60f);
		ScrollRect scroll = scrollViewObj.AddComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = (MovementType)2;
		scroll.scrollSensitivity = 30f;
		Image scrollBg = scrollViewObj.AddComponent<Image>();
		((Graphic)scrollBg).color = new Color(0f, 0f, 0f, 0.28f);
		Mask mask = scrollViewObj.AddComponent<Mask>();
		mask.showMaskGraphic = true;
		GameObject viewportObj = new GameObject("Viewport");
		viewportObj.transform.SetParent(scrollViewObj.transform, false);
		RectTransform viewportRect = viewportObj.AddComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero;
		viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = Vector2.zero;
		viewportRect.offsetMax = Vector2.zero;
		GameObject contentObj = new GameObject("Content");
		contentObj.transform.SetParent(viewportObj.transform, false);
		_contentRect = contentObj.AddComponent<RectTransform>();
		_contentRect.anchorMin = new Vector2(0f, 1f);
		_contentRect.anchorMax = new Vector2(1f, 1f);
		_contentRect.pivot = new Vector2(0.5f, 1f);
		_contentRect.anchoredPosition = Vector2.zero;
		_contentRect.sizeDelta = Vector2.zero;
		scroll.viewport = viewportRect;
		scroll.content = _contentRect;
		_contentTransform = contentObj.transform;
	}

	private static void CreateEmptyText(Transform parent)
	{
		GameObject emptyObj = new GameObject("EmptyText");
		emptyObj.transform.SetParent(parent, false);
		RectTransform emptyRect = emptyObj.AddComponent<RectTransform>();
		emptyRect.anchorMin = new Vector2(0.5f, 0.5f);
		emptyRect.anchorMax = new Vector2(0.5f, 0.5f);
		emptyRect.pivot = new Vector2(0.5f, 0.5f);
		emptyRect.anchoredPosition = new Vector2(0f, -8f);
		emptyRect.sizeDelta = new Vector2(250f, 48f);
		_emptyText = emptyObj.AddComponent<Text>();
		_emptyText.font = LocalizedTextManager.current_font;
		_emptyText.fontSize = 13;
		_emptyText.alignment = (TextAnchor)4;
		((Graphic)_emptyText).color = new Color(0.65f, 0.65f, 0.65f);
		_emptyText.text = "\u6682\u65E0\u9053\u9014\u6570\u636E";
	}

	private static void RefreshList()
	{
		EnsureContentReady();
		if ((Object)(object)_contentRect == (Object)null || (Object)(object)_contentTransform == (Object)null || (Object)(object)_emptyText == (Object)null)
		{
			return;
		}
		ClearRows();
		List<string> daoTus = BuildOrderedDaoTus();
		if (daoTus.Count == 0)
		{
			_emptyText.text = "暂无道途克制数据";
			((Component)_emptyText).gameObject.SetActive(true);
			_contentRect.sizeDelta = Vector2.zero;
			return;
		}
		((Component)_emptyText).gameObject.SetActive(false);
		float y = -4f;
		for (int i = 0; i < daoTus.Count; i++)
		{
			GameObject row = CreateRow(i, y, daoTus[i]);
			if ((Object)(object)row != (Object)null)
			{
				_rows.Add(row);
			}
			y -= CardHeight + CardSpacing;
		}
		float totalHeight = daoTus.Count * (CardHeight + CardSpacing) + 6f;
		_contentRect.sizeDelta = new Vector2(0f, Mathf.Max(0f, totalHeight));
	}

	private static GameObject CreateRow(int index, float y, string daoTu)
	{
		if ((Object)(object)_contentTransform == (Object)null)
		{
			return null;
		}
		GameObject row = new GameObject("DaoTuCounterRow_" + index);
		row.transform.SetParent(_contentTransform, false);
		RectTransform rect = row.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 1f);
		rect.anchorMax = new Vector2(1f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = new Vector2(0f, y);
		rect.sizeDelta = new Vector2(0f, CardHeight);
		Image bg = row.AddComponent<Image>();
		bg.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		bg.type = (Image.Type)1;
		((Graphic)bg).color = ((index % 2 == 0) ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.92f, 0.92f, 0.92f, 0.92f));
		string display = BuildDisplay(daoTu);
		// 居中显示：文字在框体内居中而非左对齐
	CreateCell(row.transform, "CounterDisplay", display, new Vector2(0f, 0f), new Vector2(0f, 0f), Color.white, 8, TextAnchor.MiddleCenter);
		return row;
	}

	private static void CreateCell(Transform parent, string cellName, string text, Vector2 anchoredPos, Vector2 size, Color color, int fontSize, TextAnchor alignment = (TextAnchor)3)
	{
		GameObject cell = new GameObject(cellName);
		cell.transform.SetParent(parent, false);
		RectTransform rect = cell.AddComponent<RectTransform>();
		if (size.x > 0f || size.y > 0f)
		{
			rect.anchorMin = new Vector2(0f, 0.5f);
			rect.anchorMax = new Vector2(0f, 0.5f);
			rect.pivot = new Vector2(0f, 0.5f);
			rect.anchoredPosition = anchoredPos;
			rect.sizeDelta = size;
		}
		else
		{
			// 拉伸填充父级
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = new Vector2(12f, 4f);
			rect.offsetMax = new Vector2(-12f, -4f);
		}
		Text label = cell.AddComponent<Text>();
		label.font = LocalizedTextManager.current_font;
		label.fontSize = fontSize;
		label.alignment = alignment;
		((Graphic)label).color = color;
		label.supportRichText = true;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.text = text ?? string.Empty;
	}

	private static string BuildDisplay(string daoTu)
	{
		IReadOnlyList<string> counters = XjDaoTuCounter.GetCounterTargets(daoTu);
		IReadOnlyList<string> counteredBy = XjDaoTuCounter.GetCounteredBy(daoTu);
		string counterText = counters.Count == 0 ? "无" : string.Join("、", counters);
		string counteredText = counteredBy.Count == 0 ? "无" : string.Join("、", counteredBy);
		return "<color=#F0D080>" + daoTu + "</color> 克制：" + counterText + "\n被克制：" + counteredText;
	}

	private static List<string> BuildOrderedDaoTus()
	{
		List<string> result = new List<string>();
		HashSet<string> dedup = new HashSet<string>(StringComparer.Ordinal);
		IReadOnlyCollection<string> knownDaoTus = XjDaoTuCounter.GetKnownDaoTus();
		foreach (string daoTu in knownDaoTus)
		{
			if (string.IsNullOrWhiteSpace(daoTu) || !dedup.Add(daoTu))
			{
				continue;
			}
			result.Add(daoTu);
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	private static void ClearRows()
	{
		for (int i = 0; i < _rows.Count; i++)
		{
			if ((Object)(object)_rows[i] != (Object)null)
			{
				Object.Destroy((Object)(object)_rows[i]);
			}
		}
		_rows.Clear();
	}

	private static void OnWindowClose()
	{
		ClearRows();
	}
}
