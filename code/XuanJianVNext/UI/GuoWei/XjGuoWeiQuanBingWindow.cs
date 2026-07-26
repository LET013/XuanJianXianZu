using System.Collections.Generic;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.GuoWei;

internal sealed class GuoWeiQuanBingRowRefs : MonoBehaviour
{
	public Image background;
	public Text daoTuText, detailsText;
}

internal static class XjGuoWeiQuanBingWindow
{
	private static bool _initialized;
	private static ScrollWindow _window;
	private static RectTransform _contentRect;
	private static Transform _contentTransform;
	private static Text _emptyText, _summaryText;
	private static readonly List<GuoWeiQuanBingRowRefs> _rowRefs = new();
	private const float WindowWidth = 500f, WindowHeight = 380f, RowSpacing = 4f, DetailWrapChars = 26;
	private static ScrollRect _scrollRect;

	public static void Init()
	{
		if (_initialized) return;
		_window = WindowCreator.CreateEmptyWindow("XuanJianGuoWeiQuanBing", "xuanjian.guowei_quanbing.overview", "ui/Icons/items/GuoWeiQuanBing");
		SetupContent();
		_initialized = true;
	}

	public static void Show()
	{
		bool createdNow = !_initialized;
		if (!_initialized) Init();
		Refresh();
		XjWindowOpenGuard.Show(_window, "XuanJianGuoWeiQuanBing", createdNow);
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
		hr.pivot = new Vector2(0.5f, 1f); hr.anchoredPosition = new Vector2(0f, -34f);
		hr.sizeDelta = new Vector2(-16f, 40f);

		CreateHeaderText(h.transform, "道途", new Vector2(14f, 10f), new Vector2(90f, 20f));
		CreateHeaderText(h.transform, "权柄总览", new Vector2(96f, 10f), new Vector2(360f, 20f));
		CreateHeaderText(h.transform, "展示当前持有、历代持有、永久丢失、正位封锁与果位钟爱",
			new Vector2(14f, -8f), new Vector2(430f, 16f), 8, new Color(0.8f, 0.8f, 0.8f, 0.95f));
	}

	private static void CreateHeaderText(Transform p, string text, Vector2 pos, Vector2 size, int fs = 10, Color? c = null)
	{
		var o = new GameObject("H_" + text, typeof(RectTransform), typeof(Text), typeof(Outline));
		o.transform.SetParent(p, false);
		var r = o.GetComponent<RectTransform>();
		r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
		r.anchoredPosition = pos; r.sizeDelta = size;
		var t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fs; t.alignment = TextAnchor.MiddleLeft;
		t.color = c ?? new Color(0.95f, 0.82f, 0.55f); t.text = text;
		var ol = o.GetComponent<Outline>(); ol.effectColor = new Color(0.1f, 0.08f, 0.05f, 0.78f); ol.effectDistance = new Vector2(1f, -1f);
	}

	private static void CreateScrollView(Transform parent)
	{
		var s = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		s.transform.SetParent(parent, false);
		var sr = s.GetComponent<RectTransform>(); sr.anchorMin = Vector2.zero; sr.anchorMax = Vector2.one;
		sr.offsetMin = new Vector2(8f, 28f); sr.offsetMax = new Vector2(-8f, -82f);
		s.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f); s.GetComponent<Mask>().showMaskGraphic = true;
		_scrollRect = s.GetComponent<ScrollRect>();

		var vp = new GameObject("Viewport", typeof(RectTransform)); vp.transform.SetParent(s.transform, false);
		var vr = vp.GetComponent<RectTransform>(); vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one; vr.offsetMin = vr.offsetMax = Vector2.zero;

		var co = new GameObject("Content", typeof(RectTransform)); co.transform.SetParent(vp.transform, false);
		_contentRect = co.GetComponent<RectTransform>();
		_contentRect.anchorMin = new Vector2(0f, 1f); _contentRect.anchorMax = new Vector2(1f, 1f);
		_contentRect.pivot = new Vector2(0.5f, 1f); _contentRect.anchoredPosition = Vector2.zero; _contentRect.sizeDelta = Vector2.zero;
		_contentTransform = co.transform;

		_scrollRect.horizontal = false; _scrollRect.vertical = true;
		_scrollRect.movementType = ScrollRect.MovementType.Clamped; _scrollRect.scrollSensitivity = 30f;
		_scrollRect.viewport = vr; _scrollRect.content = _contentRect;
	}

	private static void CreateEmptyText(Transform parent)
	{
		var eo = new GameObject("Empty", typeof(RectTransform), typeof(Text));
		eo.transform.SetParent(parent, false);
		var er = eo.GetComponent<RectTransform>();
		er.anchorMin = er.anchorMax = new Vector2(0.5f, 0.5f); er.pivot = new Vector2(0.5f, 0.5f);
		er.anchoredPosition = new Vector2(0f, -8f); er.sizeDelta = new Vector2(260f, 48f);
		_emptyText = eo.GetComponent<Text>(); _emptyText.font = LocalizedTextManager.current_font; _emptyText.fontSize = 13;
		_emptyText.alignment = TextAnchor.MiddleCenter; _emptyText.color = new Color(0.65f, 0.65f, 0.65f);
		_emptyText.text = "暂无果位权柄记录";

		var so = new GameObject("Summary", typeof(RectTransform), typeof(Text));
		so.transform.SetParent(parent, false);
		var sr = so.GetComponent<RectTransform>();
		sr.anchorMin = new Vector2(0f, 0f); sr.anchorMax = new Vector2(1f, 0f); sr.pivot = new Vector2(0.5f, 0f);
		sr.anchoredPosition = new Vector2(0f, 8f); sr.sizeDelta = new Vector2(-16f, 16f);
		_summaryText = so.GetComponent<Text>(); _summaryText.font = LocalizedTextManager.current_font; _summaryText.fontSize = 9;
		_summaryText.alignment = TextAnchor.MiddleCenter; _summaryText.color = new Color(0.88f, 0.8f, 0.55f, 0.95f);

		CreateNavButton(parent, "ToTop", "到顶", new Vector2(-80f, 12f), () => { if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 1f; });
		CreateNavButton(parent, "ToBottom", "到底", new Vector2(80f, 12f), () => { if (_scrollRect != null) _scrollRect.verticalNormalizedPosition = 0f; });
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
		var views = BuildViews();
		if (views.Count == 0) { SetActiveRows(0); _emptyText.gameObject.SetActive(true); _contentRect.sizeDelta = Vector2.zero; _summaryText.text = "共 0 条道途记录"; return; }
		_emptyText.gameObject.SetActive(false);
		_summaryText.text = "共 " + views.Count + " 条道途记录";
		EnsureRowPool(views.Count);

		float y = -4f;
		for (int i = 0; i < views.Count; i++)
		{
			var v = views[i];
			float rh = CalculateRowHeight(v);
			UpdateRow(_rowRefs[i], i, y, rh, v);
			y -= rh + RowSpacing;
		}
		SetActiveRows(views.Count);
		_contentRect.sizeDelta = new Vector2(0f, Mathf.Max(0f, -y + 2f));
	}

	private sealed class DaoTuOverview { internal string DaoTu; internal List<string> DetailLines; }

	private static List<DaoTuOverview> BuildViews()
	{
		var result = new List<DaoTuOverview>();
		var states = XjGuoWeiQuanBingRegistry.ReadAllEntries();
		var lostRecords = XjGuoWeiQuanBingRegistry.ReadLostAuthorityRecords();
		var daoTus = new HashSet<string>(System.StringComparer.Ordinal);

		for (int i = 0; i < states.Count; i++)
		{
			var state = states[i];
			if (!state.Found) continue;
			if (!string.IsNullOrWhiteSpace(state.DaoTu)) daoTus.Add(state.DaoTu.Trim());
			if (IsActiveState(state) && !string.IsNullOrWhiteSpace(state.PendingExternalZhengWeiDaoTu))
			{
				daoTus.Add(state.PendingExternalZhengWeiDaoTu.Trim());
			}
		}
		for (int i = 0; i < lostRecords.Count; i++)
		{
			if (!string.IsNullOrWhiteSpace(lostRecords[i].SourceDaoTu)) daoTus.Add(lostRecords[i].SourceDaoTu.Trim());
		}

		var orderedDaoTus = new List<string>(daoTus);
		orderedDaoTus.Sort(System.StringComparer.Ordinal);
		for (int d = 0; d < orderedDaoTus.Count; d++)
		{
			string daoTu = orderedDaoTus[d];
			var gained = new List<string>();
			var historical = new List<string>();
			var lost = new List<string>();
			var locks = new List<string>();
			var favored = new List<string>();

			for (int i = 0; i < states.Count; i++)
			{
				var state = states[i];
				if (!state.Found) continue;
				bool sameDaoTu = string.Equals(state.DaoTu?.Trim(), daoTu, System.StringComparison.Ordinal);
				bool active = IsActiveState(state);
				if (sameDaoTu && active)
				{
					string actor = ResolveActorName(state.ActorName);
					string guoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei);
					gained.Add(actor + "·" + guoWei + "：" + FormatAuthoritySnapshot(state, false));
				}
				else if (sameDaoTu)
				{
					historical.Add(FormatHistoricalHolder(state));
				}

				if (active
					&& state.LockUntilYear > 0
					&& (string.Equals(state.PendingExternalZhengWeiDaoTu?.Trim(), daoTu, System.StringComparison.Ordinal)
						|| (sameDaoTu && string.IsNullOrWhiteSpace(state.PendingExternalZhengWeiDaoTu))))
				{
					locks.Add(ResolveActorName(state.ActorName) + "封锁至玄历" + state.LockUntilYear + "年");
				}

				if (sameDaoTu && active && !string.IsNullOrWhiteSpace(state.GuoWeiZhongAi))
				{
					string favoredName = state.GuoWeiZhongAi.Trim();
					if (!favored.Contains(favoredName)) favored.Add(favoredName);
				}
			}

			for (int i = 0; i < lostRecords.Count; i++)
			{
				var record = lostRecords[i];
				if (!string.Equals(record.SourceDaoTu?.Trim(), daoTu, System.StringComparison.Ordinal)) continue;
				string target = string.IsNullOrWhiteSpace(record.TargetDaoTu) ? "去向未明" : record.TargetDaoTu.Trim();
				string year = record.Year > 0 ? "（玄历" + record.Year + "年）" : string.Empty;
				lost.Add(record.Authority + "→" + target + year);
			}

			if (gained.Count == 0 && historical.Count == 0 && lost.Count == 0 && locks.Count == 0 && favored.Count == 0) continue;
			var lines = new List<string>();
			var allAuthorities = XjGuoWeiAuthorityCatalog.Get(daoTu);
			if (allAuthorities.Count > 0) lines.Add("权柄: " + string.Join("、", allAuthorities));
			if (gained.Count > 0) lines.Add("当前持有: " + string.Join("；", gained));
			if (historical.Count > 0) lines.Add("历代持有: " + string.Join("；", historical));
			if (lost.Count > 0) lines.Add("永久丢失: " + string.Join("；", lost));
			if (locks.Count > 0) lines.Add("正位封锁: " + string.Join("；", locks));
			if (favored.Count > 0) lines.Add("当前果位钟爱: " + string.Join("、", favored));
			result.Add(new DaoTuOverview { DaoTu = daoTu, DetailLines = lines });
		}
		return result;
	}

	private static bool IsActiveState(in XjGuoWeiQuanBingState state)
	{
		return state.Found
			&& state.ActorId > 0L
			&& state.ReleasedYear <= 0
			&& (string.Equals(state.LifecycleStatus, "Active", System.StringComparison.Ordinal)
				|| string.Equals(state.LifecycleStatus, "PendingReincarnatedZhengWei", System.StringComparison.Ordinal));
	}

	private static string ResolveActorName(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? "未名真君" : value.Trim();
	}

	private static string FormatHistoricalHolder(in XjGuoWeiQuanBingState state)
	{
		string actor = ResolveActorName(state.ActorName);
		string guoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(state.GuoWei);
		string held = FormatAuthoritySnapshot(state, true);
		string acquired = state.AcquiredYear > 0 ? "玄历" + state.AcquiredYear + "年得位，" : string.Empty;
		string released = state.ReleasedYear > 0 ? "玄历" + state.ReleasedYear + "年" : string.Empty;
		string reason = FormatReleaseReason(state.ReleaseReason);
		string ending = string.IsNullOrWhiteSpace(released) && string.IsNullOrWhiteSpace(reason)
			? "已释放"
			: released + reason;
		return actor + "·" + guoWei + "：" + acquired + held + "；" + ending;
	}

	private static string FormatAuthoritySnapshot(in XjGuoWeiQuanBingState state, bool historical)
	{
		var parts = new List<string>();
		AddAuthorityPart(parts, string.Empty, state.LocalQuanBing);
		AddAuthorityPart(parts, "夺得", state.SeizedQuanBing);
		AddAuthorityPart(parts, "外道", state.ForeignQuanBing);
		if (!string.IsNullOrWhiteSpace(state.GuoWeiZhongAi))
		{
			parts.Add("果位钟爱：" + state.GuoWeiZhongAi.Trim());
		}
		if (parts.Count > 0)
		{
			return historical ? "曾持" + string.Join("；", parts) : string.Join("；", parts);
		}

		if (!string.IsNullOrWhiteSpace(state.Summary)
			&& state.Summary.IndexOf("旧档历史回填", System.StringComparison.Ordinal) >= 0)
		{
			return "早年权柄明细未载";
		}
		return historical ? "权柄未显" : "权柄未显";
	}

	private static string FormatReleaseReason(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return "释放";
		string reason = value.Trim();
		if (string.Equals(reason, "Death", System.StringComparison.Ordinal)) return "身死释放";
		if (string.Equals(reason, "Reassigned", System.StringComparison.Ordinal)) return "果位改易";
		if (string.Equals(reason, "Rollback", System.StringComparison.Ordinal)) return "改易撤销";
		return reason;
	}

	private static void AddAuthorityPart(List<string> parts, string prefix, string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return;
		string normalized = raw.Replace(";", "、").Replace("/", "、").Replace("|", "、").Replace(",", "、");
		parts.Add(string.IsNullOrWhiteSpace(prefix) ? normalized : prefix + "：" + normalized);
	}

	private static float CalculateRowHeight(DaoTuOverview v)
	{
		if (v.DetailLines.Count == 0) return 46f;
		int lines = 0;
		foreach (var l in v.DetailLines)
			lines += Mathf.Max(1, Mathf.CeilToInt((float)(l?.Length ?? 0) / DetailWrapChars));
		return 22f + lines * 16f;
	}

	private static void EnsureRowPool(int need)
	{
		if (need <= _rowRefs.Count || _contentTransform == null) return;
		for (int i = _rowRefs.Count; i < need; i++) { var r = CreateRow(i); if (r != null) _rowRefs.Add(r); }
	}

	private static GuoWeiQuanBingRowRefs CreateRow(int index)
	{
		if (_contentTransform == null) return null;
		var row = new GameObject("Row_" + index, typeof(RectTransform), typeof(Image), typeof(Shadow));
		row.transform.SetParent(_contentTransform, false);
		var rr = row.GetComponent<RectTransform>(); rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
		rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = Vector2.zero; rr.sizeDelta = new Vector2(0f, 46f);
		row.GetComponent<Image>().sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		row.GetComponent<Image>().type = Image.Type.Sliced;
		row.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.16f); row.GetComponent<Shadow>().effectDistance = new Vector2(0f, -1f);

		var rf = row.AddComponent<GuoWeiQuanBingRowRefs>();
		rf.background = row.GetComponent<Image>();
		rf.daoTuText = CreateCell(row.transform, "DaoTu", new Vector2(14f, -10f), new Vector2(90f, 22f), new Color(0.7f, 0.9f, 1f), 10);
		rf.detailsText = CreateCell(row.transform, "Details", new Vector2(96f, -10f), new Vector2(360f, 34f), new Color(1f, 0.9f, 0.62f), 8);
		row.SetActive(false);
		return rf;
	}

	private static void UpdateRow(GuoWeiQuanBingRowRefs rf, int index, float y, float rh, DaoTuOverview v)
	{
		if (rf == null || v == null) return;
		rf.gameObject.name = "Row_" + index; rf.gameObject.SetActive(true);
		rf.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, y);
		rf.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, rh);
		rf.background.color = index % 2 == 0 ? new Color(1f, 1f, 1f, 0.94f) : new Color(0.94f, 0.94f, 0.94f, 0.94f);
		rf.daoTuText.text = v.DaoTu ?? "";
		rf.detailsText.text = string.Join("\n", v.DetailLines);
		rf.detailsText.rectTransform.sizeDelta = new Vector2(360f, Mathf.Max(18f, rh - 12f));
	}

	private static Text CreateCell(Transform p, string n, Vector2 pos, Vector2 size, Color c, int fs)
	{
		var o = new GameObject(n, typeof(RectTransform), typeof(Text), typeof(Shadow));
		o.transform.SetParent(p, false);
		var r = o.GetComponent<RectTransform>(); r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
		r.anchoredPosition = pos; r.sizeDelta = size;
		var t = o.GetComponent<Text>(); t.font = LocalizedTextManager.current_font; t.fontSize = fs; t.alignment = TextAnchor.UpperLeft;
		t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Overflow; t.color = c; t.text = "";
		o.GetComponent<Shadow>().effectColor = new Color(0f, 0f, 0f, 0.25f); o.GetComponent<Shadow>().effectDistance = new Vector2(0f, -1f);
		return t;
	}

	private static void SetActiveRows(int n) { for (int i = 0; i < _rowRefs.Count; i++) if (_rowRefs[i] != null) _rowRefs[i].gameObject.SetActive(i < n); }
}
