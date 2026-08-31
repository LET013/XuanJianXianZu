using System;
using System.Collections.Generic;
using System.Globalization;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Cultivation;

internal static class XjFuQiToZiFuWindow
{
	private const string WindowId = "XuanJianFuQiToZiFuWindow";
	private const float WindowWidth = 300f;
	private const float WindowHeight = 390f;

	private static ScrollWindow _window;
	private static Text _titleText;
	private static Text _previewText;
	private static Text _statusText;
	private static RectTransform _content;
	private static long _actorId;
	private static string _selectedDaoTu = string.Empty;
	private static string _status = string.Empty;

	internal static void Show(long actorId)
	{
		_actorId = actorId;
		_selectedDaoTu = string.Empty;
		_status = string.Empty;
		bool createdNow = _window == null;
		EnsureWindow();
		Refresh();
		XjWindowOpenGuard.Show(_window, WindowId, createdNow);
	}

	private static void EnsureWindow()
	{
		if (_window != null) return;
		_window = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.fuqi.to_zifu", "ui/Icons/event/HistoryCultivation");
		Transform background = _window.transform.Find("Background");
		if (background == null) return;
		background.GetComponent<RectTransform>().sizeDelta = new Vector2(WindowWidth, WindowHeight);

		_titleText = CreateText(background, "Title", new Vector2(0f, 145f), new Vector2(258f, 34f), 10, TextAnchor.MiddleCenter);
		_previewText = CreateText(background, "Preview", new Vector2(0f, 110f), new Vector2(258f, 36f), 8, TextAnchor.UpperLeft);
		_statusText = CreateText(background, "Status", new Vector2(0f, -142f), new Vector2(250f, 30f), 8, TextAnchor.MiddleCenter);
		CreateTargetScroll(background);

		Button confirm = CreateButton(background, "Confirm", "永久转修", new Vector2(78f, -165f), new Vector2(72f, 24f), new Color(0.44f, 0.18f, 0.08f, 0.96f));
		confirm.onClick.AddListener(ConfirmSelected);
		Button close = CreateButton(background, "Close", "取消", new Vector2(-78f, -165f), new Vector2(62f, 24f), new Color(0.15f, 0.20f, 0.22f, 0.96f));
		close.onClick.AddListener(() => ScrollWindow.showWindow(WindowId));
	}

	private static void Refresh()
	{
		if (_window == null || _content == null) return;
		for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
		if (!TryResolveActor(out Actor actor))
		{
			_titleText.text = "角色不存在";
			_previewText.text = string.Empty;
			_statusText.text = "无法读取目标角色";
			return;
		}

		int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		int progress = XjFuQiToZiFuTransitionSystem.ResolveCoreProgressBasisPoints(actor, year);
		int count = XjFuQiToZiFuTransitionSystem.ResolveGrantedShenTongCount(progress);
		_titleText.text = (actor.getName() ?? "未名真人") + " · 服气转紫府";
		_previewText.text = "本命核心温养：" + DescribeCoreProgress(progress)
			+ "\n转修后可直接获得：" + count.ToString(CultureInfo.InvariantCulture) + "门目标道途神通与对应五品功法";
		_statusText.text = _status;

		IReadOnlyList<XjFuQiToZiFuTargetOption> options = XjFuQiToZiFuTransitionSystem.BuildTargetOptions(actor);
		if (options.Count == 0)
		{
			CreateEmptyRow("当前没有可转修的紫府金丹道途");
			return;
		}
		for (int i = 0; i < options.Count; i++) CreateTargetRow(actor, options[i], i);
		LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
	}

	private static string DescribeCoreProgress(int basisPoints)
	{
		if (basisPoints <= 0) return "未入";
		if (basisPoints < 2500) return "初成";
		if (basisPoints < 5000) return "渐熟";
		if (basisPoints < 7500) return "深厚";
		if (basisPoints < 10000) return "近乎圆满";
		return "圆满";
	}

	private static void CreateTargetRow(Actor actor, XjFuQiToZiFuTargetOption option, int index)
	{
		GameObject row = new GameObject("Target_" + index, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
		row.transform.SetParent(_content, false);
		LayoutElement layout = row.GetComponent<LayoutElement>();
		layout.minHeight = 30f; layout.preferredHeight = 30f;
		Image image = row.GetComponent<Image>();
		bool selected = string.Equals(_selectedDaoTu, option.DaoTu, StringComparison.Ordinal);
		image.color = selected ? new Color(0.30f, 0.45f, 0.24f, 0.96f) : new Color(0.09f, 0.16f, 0.17f, 0.92f);

		string suffix = option.IsCurrentDaoTu ? "（原道途）" : option.IsCurrentRoot ? "（同根类）" : string.Empty;
		Text text = CreateStretchText(row.transform, XjDisplayNameSanitizer.GameTerm(option.DaoTu, "未知道途") + suffix, 9, TextAnchor.MiddleLeft);
		text.color = selected ? new Color(1f, 0.94f, 0.66f) : new Color(0.93f, 0.98f, 0.96f);
		Button button = row.GetComponent<Button>();
		button.onClick.AddListener(() =>
		{
			_selectedDaoTu = option.DaoTu;
			if (XjFuQiToZiFuTransitionSystem.TryPreview(actor, option.DaoTu, out int progress, out string[] ids, out string reason))
			{
				_status = "已选「" + option.DaoTu + "」：" + string.Join("、", ids);
			}
			else _status = reason;
			Refresh();
		});
	}

	private static void ConfirmSelected()
	{
		if (string.IsNullOrWhiteSpace(_selectedDaoTu))
		{
			_status = "请先选择目标道途";
			Refresh();
			return;
		}
		if (!TryResolveActor(out Actor actor))
		{
			_status = "角色已经失效";
			Refresh();
			return;
		}
		if (XjFuQiToZiFuTransitionSystem.TryConvert(actor, _selectedDaoTu, out XjFuQiToZiFuResult result))
		{
			_status = result.Message + "：" + string.Join("、", result.GrantedShenTongIds);
			XjActorInfoPanelRenderer.Invalidate();
			// 转修成功后角色已不再是服气真人，直接关闭窗口，避免成功页面
			// 被重新计算为“0%/1门”的无效预览。
			ScrollWindow.showWindow(WindowId);
			return;
		}
		_status = result.Message;
		Refresh();
	}

	private static bool TryResolveActor(out Actor actor)
	{
		actor = null;
		return _actorId > 0L
			&& (XjFamilyReadModel.Shared.TryGetActor(_actorId, out actor)
				|| XjActorRegistry.ResolveKnownOrWorld(_actorId, out actor))
			&& actor?.data != null;
	}

	private static void CreateTargetScroll(Transform parent)
	{
		GameObject scrollObject = new GameObject("Targets", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObject.transform.SetParent(parent, false);
		RectTransform scrollRectTransform = scrollObject.GetComponent<RectTransform>();
		scrollRectTransform.anchorMin = scrollRectTransform.anchorMax = scrollRectTransform.pivot = new Vector2(0.5f, 0.5f);
		scrollRectTransform.anchoredPosition = new Vector2(0f, -15f);
		scrollRectTransform.sizeDelta = new Vector2(270f, 210f);
		scrollObject.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);
		scrollObject.GetComponent<Mask>().showMaskGraphic = true;

		GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
		viewport.transform.SetParent(scrollObject.transform, false);
		RectTransform viewportRect = viewport.GetComponent<RectTransform>();
		viewportRect.anchorMin = Vector2.zero; viewportRect.anchorMax = Vector2.one;
		viewportRect.offsetMin = viewportRect.offsetMax = Vector2.zero;

		GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		content.transform.SetParent(viewport.transform, false);
		_content = content.GetComponent<RectTransform>();
		_content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
		_content.pivot = new Vector2(0.5f, 1f); _content.anchoredPosition = Vector2.zero;
		_content.sizeDelta = new Vector2(-10f, 0f);
		VerticalLayoutGroup group = content.GetComponent<VerticalLayoutGroup>();
		group.padding = new RectOffset(4, 4, 4, 4); group.spacing = 3f;
		group.childControlWidth = true; group.childControlHeight = true;
		group.childForceExpandWidth = true; group.childForceExpandHeight = false;
		content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

		ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
		scroll.horizontal = false; scroll.vertical = true; scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 28f; scroll.viewport = viewportRect; scroll.content = _content;
	}

	private static void CreateEmptyRow(string value)
	{
		GameObject row = new GameObject("Empty", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
		row.transform.SetParent(_content, false);
		LayoutElement layout = row.GetComponent<LayoutElement>(); layout.preferredHeight = 60f;
		Text text = row.GetComponent<Text>(); text.font = LocalizedTextManager.current_font;
		text.fontSize = 9; text.alignment = TextAnchor.MiddleCenter; text.text = value;
		text.color = new Color(0.82f, 0.84f, 0.84f); text.raycastTarget = false;
	}

	private static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size, int fontSize, TextAnchor alignment)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = position; rect.sizeDelta = size;
		Text text = obj.GetComponent<Text>(); text.font = LocalizedTextManager.current_font;
		text.fontSize = fontSize; text.alignment = alignment; text.color = new Color(0.96f, 0.98f, 1f);
		text.raycastTarget = false; return text;
	}

	private static Text CreateStretchText(Transform parent, string value, int fontSize, TextAnchor alignment)
	{
		GameObject obj = new GameObject("Text", typeof(RectTransform), typeof(Text)); obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
		rect.offsetMin = new Vector2(8f, 0f); rect.offsetMax = new Vector2(-8f, 0f);
		Text text = obj.GetComponent<Text>(); text.font = LocalizedTextManager.current_font;
		text.fontSize = fontSize; text.alignment = alignment; text.text = value; text.raycastTarget = false;
		return text;
	}

	private static Button CreateButton(Transform parent, string name, string label, Vector2 position, Vector2 size, Color color)
	{
		GameObject obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button)); obj.transform.SetParent(parent, false);
		RectTransform rect = obj.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = position; rect.sizeDelta = size; obj.GetComponent<Image>().color = color;
		Text text = CreateStretchText(obj.transform, label, 9, TextAnchor.MiddleCenter);
		text.color = new Color(1f, 0.96f, 0.82f);
		return obj.GetComponent<Button>();
	}
}
