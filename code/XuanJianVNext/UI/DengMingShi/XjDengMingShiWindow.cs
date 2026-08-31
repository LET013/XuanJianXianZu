using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.DengMingShi;

internal static class XjDengMingShiWindow
{
	private const string WindowId = "XuanJianDengMingShiWindow";
	private const float WindowWidth = 280f;
	private const float WindowHeight = 370f;

	private static ScrollWindow _window;
	private static Text _selectedText;
	private static Text _statusText;
	private static RectTransform _recordsContent;
	private static string _statusMessage = string.Empty;
	private static string _pendingDeleteId;

	internal static void Show()
	{
		XjDengMingShiManager.Init();
		bool createdNow = _window == null;
		EnsureWindow();
		Refresh();
		XjWindowOpenGuard.Show(_window, WindowId, createdNow);
	}

	internal static void Refresh()
	{
		if (_window == null) return;

		XjDengMingShiCommands.TryGetSelectedActor(out Actor selected);
		if (_selectedText != null)
		{
			_selectedText.text = selected?.data != null
				? "当前选中：" + (selected.getName() ?? "未名").Trim()
				: "当前选中：暂无可保存角色";
		}
		if (_statusText != null) _statusText.text = _statusMessage;

		for (int i = _recordsContent.childCount - 1; i >= 0; i--)
		{
			Object.Destroy(_recordsContent.GetChild(i).gameObject);
		}

		List<KeyValuePair<string, XjDengMingShiManager.SavedActorPacket>> saved = XjDengMingShiManager.GetSavedActors()
			.OrderByDescending(pair => pair.Value?.SaveTime ?? string.Empty, System.StringComparer.Ordinal)
			.ThenBy(pair => pair.Key, System.StringComparer.Ordinal)
			.ToList();
		if (saved.Count == 0)
		{
			CreateRecordText(_recordsContent, "暂无登名石记录", 70f);
			return;
		}

		for (int i = 0; i < saved.Count; i++) CreateRecordRow(_recordsContent, saved[i].Key, saved[i].Value, i + 1);
		LayoutRebuilder.ForceRebuildLayoutImmediate(_recordsContent);
	}

	private static void EnsureWindow()
	{
		if (_window != null) return;
		XjDengMingShiPlaceMode.EnsurePowerRegistered();
		_window = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.dengmingshi.overview", "ui/Icons/items/DengMin");
		SetupWindow();
	}

	private static void SetupWindow()
	{
		Transform bg = _window.transform.Find("Background");
		if (bg == null) return;
		bg.GetComponent<RectTransform>().sizeDelta = new Vector2(WindowWidth, WindowHeight);

		_selectedText = CreateText(bg, "Selected", new Vector2(-18f, 128f), new Vector2(185f, 18f), 8, TextAnchor.MiddleLeft);
		Button saveBtn = CreateButton(bg, "SaveBtn", "保存", new Vector2(92f, 128f), new Vector2(38f, 20f));
		saveBtn.onClick.AddListener(() =>
		{
			XjDengMingShiCommandResult result = XjDengMingShiCommands.SaveSelectedActor();
			_statusMessage = result.Success ? "已保存，可跨世界读取" : (string.IsNullOrWhiteSpace(result.Message) ? "保存失败" : result.Message);
			Refresh();
		});

		_statusText = CreateText(bg, "Status", new Vector2(0f, 108f), new Vector2(220f, 18f), 7, TextAnchor.MiddleCenter);
		CreateScrollView(bg);
	}

	private static void CreateScrollView(Transform parent)
	{
		GameObject so = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		so.transform.SetParent(parent, false);
		RectTransform sr = so.GetComponent<RectTransform>();
		sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0.5f, 0.5f);
		sr.anchoredPosition = new Vector2(0f, -36f); sr.sizeDelta = new Vector2(260f, 268f);
		so.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.22f);
		so.GetComponent<Mask>().showMaskGraphic = true;

		GameObject vp = new GameObject("Viewport", typeof(RectTransform));
		vp.transform.SetParent(so.transform, false);
		RectTransform vr = vp.GetComponent<RectTransform>();
		vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero;

		GameObject co = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		co.transform.SetParent(vp.transform, false);
		_recordsContent = co.GetComponent<RectTransform>();
		_recordsContent.anchorMin = new Vector2(0f, 1f); _recordsContent.anchorMax = new Vector2(1f, 1f);
		_recordsContent.pivot = new Vector2(0.5f, 1f); _recordsContent.anchoredPosition = Vector2.zero;
		_recordsContent.sizeDelta = new Vector2(-12f, 0f);
		VerticalLayoutGroup layout = co.GetComponent<VerticalLayoutGroup>();
		layout.spacing = 3f; layout.padding = new RectOffset(5, 5, 5, 5);
		layout.childAlignment = TextAnchor.UpperCenter; layout.childControlWidth = true; layout.childControlHeight = true;
		layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
		co.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		ScrollRect scroll = so.GetComponent<ScrollRect>();
		scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 28f; scroll.viewport = vr; scroll.content = _recordsContent;
	}

	private static void CreateRecordRow(Transform parent, string recordId, XjDengMingShiManager.SavedActorPacket packet, int index)
	{
		GameObject row = new GameObject("Row", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
		row.transform.SetParent(parent, false);
		row.GetComponent<RectTransform>().sizeDelta = new Vector2(210f, 42f);
		LayoutElement le = row.GetComponent<LayoutElement>();
		le.minWidth = 210f; le.preferredWidth = 210f; le.minHeight = 42f; le.preferredHeight = 42f;
		Image img = row.GetComponent<Image>();
		img.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		img.type = img.sprite == null ? Image.Type.Simple : Image.Type.Sliced;

		Text rank = CreateText(row.transform, "Rank", new Vector2(-91f, 0f), new Vector2(24f, 28f), 10, TextAnchor.MiddleCenter);
		rank.color = index switch { 1 => new Color(1f, 0.84f, 0f), 2 => new Color(0.75f, 0.75f, 0.75f), 3 => new Color(0.8f, 0.5f, 0.2f), _ => new Color(0.85f, 0.85f, 0.85f) };
		rank.text = index.ToString(CultureInfo.InvariantCulture);

		string actorName = string.IsNullOrWhiteSpace(packet?.Name) ? "未名角色" : packet.Name;
		Text name = CreateText(row.transform, "Name", new Vector2(-22f, 9f), new Vector2(112f, 16f), 8, TextAnchor.MiddleLeft);
		name.text = actorName;
		Text time = CreateText(row.transform, "Time", new Vector2(-22f, -9f), new Vector2(112f, 14f), 7, TextAnchor.MiddleLeft);
		time.color = new Color(0.72f, 0.72f, 0.72f); time.text = packet?.SaveTime ?? string.Empty;

		Button place = CreateTintedButton(row.transform, "Place", "放", new Vector2(70f, 0f), new Vector2(20f, 20f), new Color(0.18f, 0.55f, 0.18f, 0.9f));
		place.onClick.AddListener(() =>
		{
			_pendingDeleteId = null;
			_statusMessage = XjDengMingShiPlaceMode.Begin(recordId) ? "请选择地图位置" : "放置模式启动失败";
		});

		Button del = CreateTintedButton(row.transform, "Delete", "删", new Vector2(94f, 0f), new Vector2(20f, 20f), new Color(0.55f, 0.18f, 0.18f, 0.9f));
		del.onClick.AddListener(() =>
		{
			if (_pendingDeleteId == recordId)
			{
				XjDengMingShiManager.RemoveSavedActor(recordId); _pendingDeleteId = null; _statusMessage = "已删除";
			}
			else
			{
				_pendingDeleteId = recordId; _statusMessage = "再次点击「删」确认删除 " + actorName;
			}
			Refresh();
		});

		TipButton tip = row.AddComponent<TipButton>();
		// 保存时间已经直接显示在条目上；Tooltip 不再把时间戳拼进本地化 key。
		// 否则每次保存都会制造一个全新的动态文本键，累计到 passthrough 上限后
		// WorldBox 会反复输出 LocalizedTextManager: missing text。
		XjNativeHoverTooltip.Ensure(
			tip,
			actorName,
			"跨世界完整人物数据；保存时间见条目下方。",
			string.Empty);
	}

	private static Text CreateText(Transform p, string n, Vector2 pos, Vector2 size, int fs, TextAnchor a)
	{
		GameObject o = new GameObject(n, typeof(RectTransform), typeof(Text)); o.transform.SetParent(p, false);
		RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
		r.anchoredPosition = pos; r.sizeDelta = size;
		Text t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fs; t.alignment = a;
		t.color = new Color(0.96f, 0.97f, 1f); t.raycastTarget = false; return t;
	}

	private static Button CreateButton(Transform p, string n, string label, Vector2 pos, Vector2 size)
	{
		GameObject o = new GameObject(n, typeof(RectTransform), typeof(Image), typeof(Button)); o.transform.SetParent(p, false);
		RectTransform r = o.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0.5f);
		r.anchoredPosition = pos; r.sizeDelta = size; o.GetComponent<Image>().color = new Color(0.18f, 0.16f, 0.12f, 0.9f);
		GameObject lbl = new GameObject("Text", typeof(RectTransform), typeof(Text)); lbl.transform.SetParent(o.transform, false);
		RectTransform lr = lbl.GetComponent<RectTransform>(); lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
		Text lt = lbl.GetComponent<Text>(); lt.font = LocalizedTextManager.current_font; lt.fontSize = 9; lt.alignment = TextAnchor.MiddleCenter; lt.text = label;
		return o.GetComponent<Button>();
	}

	private static Button CreateTintedButton(Transform p, string n, string label, Vector2 pos, Vector2 size, Color tint)
	{
		Button b = CreateButton(p, n, label, pos, size); b.GetComponent<Image>().color = tint; return b;
	}

	private static void CreateRecordText(Transform parent, string text, float h)
	{
		GameObject o = new GameObject("RecordText", typeof(RectTransform), typeof(Text), typeof(LayoutElement)); o.transform.SetParent(parent, false);
		o.GetComponent<RectTransform>().sizeDelta = new Vector2(210f, h);
		LayoutElement le = o.GetComponent<LayoutElement>(); le.minHeight = 28f; le.preferredHeight = h;
		Text t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = 10; t.alignment = TextAnchor.UpperLeft;
		t.color = new Color(0.96f, 0.97f, 1f); t.raycastTarget = false; t.text = text;
	}
}
