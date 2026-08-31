using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.UI.ZhenJun;

internal sealed class XjZhenJunLuItem
{
	internal string Name, DaoTu, JinXing, GuoWei, Status, Family, ReincarnationName;
	internal long ActorId;
	internal int Year;
}

internal static class XjZhenJunLuReadModel
{
	internal static List<XjZhenJunLuItem> Build()
	{
		var list = new List<XjZhenJunLuItem>();
		Dictionary<long, XjReincarnationRecord> reincarnationsBySource = BuildReincarnationIndex();
		var entries = XjGuoWeiRegistry.ReadAllEntries();
		foreach (var e in entries)
		{
			if (!e.Found) continue;
			string family = e.FamilyName ?? string.Empty;
			string status = e.IsActive ? "在世" : "已逝";
			string actorName = e.ActorName;
			string reincarnationName = string.Empty;
			Actor actor = ResolveLiveActor(e.ActorId);
			if (XuanJianVNext.Core.XjSafeCore.IsAliveActor(actor))
			{
				status = "在世";
				string liveName = actor.getName();
				actorName = string.IsNullOrWhiteSpace(liveName) ? e.ActorName : liveName;
				if (actor.clan?.data != null)
				{
					family = ((BaseSystemData)actor.clan.data).name ?? family;
				}
			}
			else if (reincarnationsBySource.TryGetValue(e.ActorId, out XjReincarnationRecord reincarnation)
				&& (reincarnation.TargetActorId > 0L
					|| string.Equals(reincarnation.Status, "Applied", System.StringComparison.Ordinal)))
			{
				status = "转世";
				reincarnationName = reincarnation.TargetActorName ?? string.Empty;
				if (string.IsNullOrWhiteSpace(reincarnationName)
					&& reincarnation.TargetActorId > 0L
					&& ResolveLiveActor(reincarnation.TargetActorId) is Actor target)
				{
					reincarnationName = target.getName() ?? string.Empty;
				}
			}
			if (string.IsNullOrWhiteSpace(family)
				&& XuanJianVNext.Systems.Family.XjFamilyReadModel.Shared.TryGetConfirmedIdentity(e.ActorId, out var identity)
				&& identity.Found
				&& identity.FamilyStableIdValue > 0L)
			{
				family = XuanJianVNext.Systems.Family.XjFamilyDisplayNameResolver.Resolve(identity.FamilyStableIdValue);
			}
			list.Add(new XjZhenJunLuItem
			{
				ActorId = e.ActorId,
				Name = actorName,
				DaoTu = e.DaoTu,
				JinXing = e.JinXing,
				GuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(e.GuoWei),
				Year = e.Year,
				Status = status,
				Family = family,
				ReincarnationName = reincarnationName
			});
		}
		list.Sort((a, b) =>
		{
			int leftYear = a.Year <= 0 ? int.MaxValue : a.Year;
			int rightYear = b.Year <= 0 ? int.MaxValue : b.Year;
			int byYear = leftYear.CompareTo(rightYear);
			if (byYear != 0) return byYear;
			int name = string.Compare(a.Name, b.Name, System.StringComparison.Ordinal);
			return name != 0 ? name : a.ActorId.CompareTo(b.ActorId);
		});
		return list;
	}

	private static Dictionary<long, XjReincarnationRecord> BuildReincarnationIndex()
	{
		var bySource = new Dictionary<long, XjReincarnationRecord>();
		IReadOnlyList<XjReincarnationRecord> records = XjReincarnation.ReadAllEntries();
		for (int i = 0; i < records.Count; i++)
		{
			XjReincarnationRecord record = records[i];
			if (record.Found && record.ActorId > 0L) bySource[record.ActorId] = record;
		}
		return bySource;
	}

	private static Actor ResolveLiveActor(long actorId)
	{
		return XuanJianVNext.Core.XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& XuanJianVNext.Core.XjSafeCore.IsAliveActor(actor)
			? actor
			: null;
	}
}

internal static class XjZhenJunLuWindow
{
	private sealed class ZhenJunRowRefs : MonoBehaviour
	{
		public Image background, statusBg;
		public Text rankText, nameText, clanText, guoWeiText, yearText, statusText;
	}

	private static bool _initialized;
	private static ScrollWindow _window;
	private static RectTransform _contentRect;
	private static Transform _contentTransform;
	private static Text _emptyText, _summaryText;
	private static readonly List<ZhenJunRowRefs> _rowRefs = new();
	private const float WindowWidth = 500f, WindowHeight = 380f, RowHeight = 56f, RowSpacing = 2f;

	public static void Init()
	{
		if (_initialized) return;
		_window = WindowCreator.CreateEmptyWindow("XuanJianZhenJunLu", "xuanjian.zhenjunlu.overview", "ui/Icons/items/ZhenJunLu");
		SetupContent();
		_initialized = true;
	}

	public static void Show()
	{
		bool createdNow = !_initialized;
		if (!_initialized) Init();
		Refresh();
		XjWindowOpenGuard.Show(_window, "XuanJianZhenJunLu", createdNow);
	}

	private static void SetupContent()
	{
		var bg = _window.transform.Find("Background"); if (bg == null) return;
		bg.GetComponent<RectTransform>().sizeDelta = new Vector2(WindowWidth, WindowHeight);

		CreateHeader(bg);
		CreateScrollView(bg);
		CreateEmptyText(bg);
	}

	private static void CreateHeader(Transform parent)
	{
		var h = new GameObject("Header", typeof(RectTransform)); h.transform.SetParent(parent, false);
		var hr = h.GetComponent<RectTransform>();
		hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
		hr.pivot = new Vector2(0.5f, 1f); hr.anchoredPosition = new Vector2(0f, -70f);
		hr.sizeDelta = new Vector2(-16f, 42f);

		CreateHeaderText(h.transform, "真君名讳", new Vector2(50f, 10f), new Vector2(170f, 20f));
		CreateHeaderText(h.transform, "金丹果位", new Vector2(220f, 10f), new Vector2(110f, 20f));
		CreateHeaderText(h.transform, "成道年份", new Vector2(325f, 10f), new Vector2(70f, 20f), TextAnchor.MiddleCenter);
		CreateHeaderText(h.transform, "存活状态", new Vector2(402f, 10f), new Vector2(70f, 20f), TextAnchor.MiddleCenter);
		CreateHeaderText(h.transform, "按晋升先后排序，实时追踪在世/转世/已逝",
			new Vector2(14f, -8f), new Vector2(460f, 18f), TextAnchor.MiddleLeft, 8, new Color(0.8f, 0.8f, 0.8f, 0.95f));
	}

	private static void CreateHeaderText(Transform p, string text, Vector2 pos, Vector2 size, TextAnchor a = TextAnchor.MiddleLeft, int fs = 10, Color? c = null)
	{
		var o = new GameObject("H_" + text, typeof(RectTransform), typeof(Text), typeof(Outline));
		o.transform.SetParent(p, false);
		var r = o.GetComponent<RectTransform>();
		r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
		r.anchoredPosition = pos; r.sizeDelta = size;
		var t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fs; t.alignment = a;
		t.color = c ?? new Color(0.95f, 0.82f, 0.55f); t.text = text;
		var ol = o.GetComponent<Outline>(); ol.effectColor = new Color(0.1f, 0.08f, 0.05f, 0.78f); ol.effectDistance = new Vector2(1f, -1f);
	}

	private static void CreateScrollView(Transform parent)
	{
		var s = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		s.transform.SetParent(parent, false);
		var sr = s.GetComponent<RectTransform>(); sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
		sr.offsetMin = new Vector2(8f, 28f); sr.offsetMax = new Vector2(-8f, -120f);
		s.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f); s.GetComponent<Mask>().showMaskGraphic = true;

		var vp = new GameObject("Viewport", typeof(RectTransform)); vp.transform.SetParent(s.transform, false);
		var vr = vp.GetComponent<RectTransform>(); vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero;

		var co = new GameObject("Content", typeof(RectTransform)); co.transform.SetParent(vp.transform, false);
		_contentRect = co.GetComponent<RectTransform>();
		_contentRect.anchorMin = new Vector2(0f, 1f); _contentRect.anchorMax = new Vector2(1f, 1f);
		_contentRect.pivot = new Vector2(0.5f, 1f); _contentRect.anchoredPosition = Vector2.zero; _contentRect.sizeDelta = Vector2.zero;
		_contentTransform = co.transform;

		var sc = s.GetComponent<ScrollRect>(); sc.horizontal = false; sc.vertical = true;
		sc.movementType = ScrollRect.MovementType.Clamped; sc.scrollSensitivity = 30f;
		sc.viewport = vr; sc.content = _contentRect;
	}

	private static void CreateEmptyText(Transform parent)
	{
		var eo = new GameObject("Empty", typeof(RectTransform), typeof(Text), typeof(Outline));
		eo.transform.SetParent(parent, false);
		var er = eo.GetComponent<RectTransform>();
		er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f); er.pivot = new Vector2(0.5f, 0.5f);
		er.anchoredPosition = new Vector2(0f, -8f); er.sizeDelta = new Vector2(280f, 48f);
		_emptyText = eo.GetComponent<Text>(); _emptyText.font = LocalizedTextManager.current_font; _emptyText.fontSize = 13;
		_emptyText.alignment = TextAnchor.MiddleCenter; _emptyText.color = new Color(0.65f, 0.65f, 0.65f);
		_emptyText.text = "真君隐世，果位失辉";

		var so = new GameObject("Summary", typeof(RectTransform), typeof(Text));
		so.transform.SetParent(parent, false);
		var sr = so.GetComponent<RectTransform>();
		sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
		sr.anchoredPosition = new Vector2(0f, 8f); sr.sizeDelta = new Vector2(-16f, 16f);
		_summaryText = so.GetComponent<Text>(); _summaryText.font = LocalizedTextManager.current_font; _summaryText.fontSize = 9;
		_summaryText.alignment = TextAnchor.MiddleCenter; _summaryText.color = new Color(0.88f, 0.8f, 0.55f, 0.95f);

		CreateNavButton(parent, "ToTop", "到顶", new Vector2(-80f, 12f), () => { if (_window != null) { var scr = _window.transform.Find("Background/Scroll")?.GetComponent<ScrollRect>(); if (scr != null) scr.verticalNormalizedPosition = 1f; } });
		CreateNavButton(parent, "ToBottom", "到底", new Vector2(80f, 12f), () => { if (_window != null) { var scr = _window.transform.Find("Background/Scroll")?.GetComponent<ScrollRect>(); if (scr != null) scr.verticalNormalizedPosition = 0f; } });
	}

	private static void CreateNavButton(Transform p, string n, string text, Vector2 pos, System.Action onClick)
	{
		var b = new GameObject(n, typeof(RectTransform), typeof(Image), typeof(Button));
		b.transform.SetParent(p, false);
		var br = b.GetComponent<RectTransform>(); br.anchorMin = br.anchorMax = new Vector2(0.5f, 0f); br.pivot = new Vector2(0.5f, 0f);
		br.anchoredPosition = pos; br.sizeDelta = new Vector2(60f, 20f);
		b.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		b.GetComponent<Image>().type = Image.Type.Sliced; b.GetComponent<Image>().color = new Color(0.2f, 0.15f, 0.05f, 0.8f);
		var lbl = new GameObject("Text", typeof(RectTransform), typeof(Text)); lbl.transform.SetParent(b.transform, false);
		var lr = lbl.GetComponent<RectTransform>(); lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
		var lt = lbl.GetComponent<Text>(); lt.font = LocalizedTextManager.current_font; lt.fontSize = 9; lt.alignment = TextAnchor.MiddleCenter;
		lt.color = new Color(0.95f, 0.82f, 0.55f); lt.text = text;
		b.GetComponent<Button>().onClick.AddListener(new UnityEngine.Events.UnityAction(onClick));
	}

	private static void Refresh()
	{
		var items = XjZhenJunLuReadModel.Build();
		if (items.Count == 0) { SetActiveRows(0); _emptyText.gameObject.SetActive(true); _contentRect.sizeDelta = Vector2.zero; _summaryText.text = "共0 位真君"; return; }
		_emptyText.gameObject.SetActive(false);
		_summaryText.text = "共" + items.Count + " 位真君";
		EnsureRowPool(items.Count);

		float y = -4f;
		for (int i = 0; i < items.Count; i++)
		{
			UpdateRow(_rowRefs[i], i, y, items[i]);
			y -= RowHeight + RowSpacing;
		}
		SetActiveRows(items.Count);
		_contentRect.sizeDelta = new Vector2(0f, items.Count * (RowHeight + RowSpacing) + 6f);
	}

	private static void EnsureRowPool(int need)
	{
		if (need <= _rowRefs.Count || _contentTransform == null) return;
		for (int i = _rowRefs.Count; i < need; i++) { var r = CreateRow(i); if (r != null) _rowRefs.Add(r); }
	}

	private static ZhenJunRowRefs CreateRow(int index)
	{
		if (_contentTransform == null) return null;
		var row = new GameObject("Row_" + index, typeof(RectTransform), typeof(Image), typeof(Shadow));
		row.transform.SetParent(_contentTransform, false);
		var rr = row.GetComponent<RectTransform>(); rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
		rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = Vector2.zero; rr.sizeDelta = new Vector2(0f, RowHeight);
		row.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		row.GetComponent<Image>().type = Image.Type.Sliced;
		row.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.16f); row.GetComponent<Shadow>().effectDistance = new Vector2(0f, -1f);

		var rf = row.AddComponent<ZhenJunRowRefs>();
		rf.background = row.GetComponent<Image>();
		rf.rankText = CreateCell(row.transform, "Rank", new Vector2(14f, -2f), new Vector2(30f, 20f), new Color(0.95f, 0.82f, 0.55f), 9, TextAnchor.MiddleCenter);
		rf.nameText = CreateCell(row.transform, "Name", new Vector2(50f, -2f), new Vector2(176f, 20f), Color.white, 9, TextAnchor.MiddleLeft);
		rf.clanText = CreateCell(row.transform, "Clan", new Vector2(50f, -22f), new Vector2(176f, 18f), new Color(0.82f, 0.92f, 1f), 8, TextAnchor.MiddleLeft);
		rf.guoWeiText = CreateCell(row.transform, "GuoWei", new Vector2(220f, -2f), new Vector2(110f, 20f), new Color(0.98f, 0.9f, 0.55f), 9, TextAnchor.MiddleLeft);
		rf.yearText = CreateCell(row.transform, "Year", new Vector2(325f, -2f), new Vector2(70f, 20f), new Color(0.74f, 0.88f, 1f), 9, TextAnchor.MiddleCenter);
		CreateStatusBadge(row.transform, rf, new Vector2(402f, -2f), new Vector2(70f, 22f));
		row.SetActive(false);
		return rf;
	}

	private static void UpdateRow(ZhenJunRowRefs rf, int index, float y, XjZhenJunLuItem item)
	{
		if (rf == null || item == null) return;
		rf.gameObject.name = "Row_" + index; rf.gameObject.SetActive(true);
		rf.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
		rf.background.color = index % 2 == 0 ? new Color(1f, 1f, 1f, 0.92f) : new Color(0.92f, 0.92f, 0.92f, 0.92f);
		rf.rankText.text = (index + 1).ToString(CultureInfo.InvariantCulture);
		rf.nameText.text = (item.Name ?? "未名真君");
		string familyText = "氏族：" + (string.IsNullOrWhiteSpace(item.Family) ? "无氏族" : item.Family);
		if (NormalizeStatus(item.Status) == "转世" && !string.IsNullOrWhiteSpace(item.ReincarnationName))
		{
			familyText += "　转世：" + item.ReincarnationName;
		}
		rf.clanText.text = familyText;
		rf.guoWeiText.text = (item.GuoWei ?? "未定果位");
		rf.yearText.text = item.Year > 0 ? XjChronology.FormatYear(item.Year) : "未知";
		rf.statusBg.color = ResolveStatusColor(item.Status);
		rf.statusText.text = NormalizeStatus(item.Status);
	}

	private static Color ResolveStatusColor(string s)
	{
		var n = NormalizeStatus(s);
		if (n == "在世") return new Color(0.28f, 0.68f, 0.34f, 0.95f);
		if (n == "转世") return new Color(0.86f, 0.64f, 0.18f, 0.95f);
		return new Color(0.45f, 0.45f, 0.45f, 0.95f);
	}

	private static string NormalizeStatus(string s)
	{
		if (string.IsNullOrWhiteSpace(s)) return "已逝";
		if (s.Contains("在世") || s.Contains("存世") || s.Contains("登记")) return "在世";
		if (s.Contains("转世") || s.Contains("转")) return "转世";
		if (s.Contains("死亡") || s.Contains("归档")) return "已逝";
		return "已逝";
	}

	private static void CreateStatusBadge(Transform p, ZhenJunRowRefs rf, Vector2 pos, Vector2 size)
	{
		var badge = new GameObject("Badge", typeof(RectTransform), typeof(Image), typeof(Shadow));
		badge.transform.SetParent(p, false);
		var br = badge.GetComponent<RectTransform>(); br.anchorMin = br.anchorMax = new Vector2(0f, 0.5f); br.pivot = new Vector2(0f, 0.5f);
		br.anchoredPosition = pos; br.sizeDelta = size;
		badge.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		badge.GetComponent<Image>().type = Image.Type.Sliced;
		badge.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.2f); badge.GetComponent<Shadow>().effectDistance = new Vector2(0f, -1f);

		var lbl = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(Outline)); lbl.transform.SetParent(badge.transform, false);
		var lr = lbl.GetComponent<RectTransform>(); lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one; lr.offsetMin = lr.offsetMax = Vector2.zero;
		var lt = lbl.GetComponent<Text>(); lt.font = LocalizedTextManager.current_font; lt.fontSize = 8; lt.alignment = TextAnchor.MiddleCenter; lt.color = Color.white; lt.text = "";
		lbl.GetComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.35f); lbl.GetComponent<Outline>().effectDistance = new Vector2(1f, -1f);
		rf.statusBg = badge.GetComponent<Image>(); rf.statusText = lt;
	}

	private static Text CreateCell(Transform p, string n, Vector2 pos, Vector2 size, Color c, int fs, TextAnchor a)
	{
		var o = new GameObject(n, typeof(RectTransform), typeof(Text), typeof(Shadow));
		o.transform.SetParent(p, false);
		var r = o.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
		r.anchoredPosition = pos; r.sizeDelta = size;
		var t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fs; t.alignment = a; t.color = c;
		t.horizontalOverflow = HorizontalWrapMode.Overflow; t.verticalOverflow = VerticalWrapMode.Overflow; t.text = "";
		o.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.25f); o.GetComponent<Shadow>().effectDistance = new Vector2(0f, -1f);
		return t;
	}

	private static void SetActiveRows(int n) { for (int i = 0; i < _rowRefs.Count; i++) if (_rowRefs[i] != null) _rowRefs[i].gameObject.SetActive(i < n); }
}
