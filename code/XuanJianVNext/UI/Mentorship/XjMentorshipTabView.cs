using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.UI.Common;

using XuanJianVNext.Systems.Sect;
namespace XuanJianVNext.UI.Mentorship;

/// <summary>
/// 师徒页沿用原生族谱的两段布局，但不再在每次角色刷新时销毁并重建整棵 UI。
/// 头像槽只在容量增长时创建，之后通过对象池式复用更新角色，处理方式与西幻世界师徒页一致。
/// </summary>
internal static class XjMentorshipTabView
{
	private const int SectionCount = 2;
	internal const float SlotSize = XjIconShelfRenderer.DefaultIconCellSize;
	internal const float AvatarVisualSize = XjIconShelfRenderer.DefaultIconCellSize;
	internal const float AvatarScale = 0.94f;

	internal static Transform CreateTabContent(UnitWindow window, Transform parent, Actor actor)
	{
		if (parent == null) return null;
		if (!XjNativeGenealogyTemplateProvider.TryClone(
			window,
			parent,
			"XjMentorshipTabRoot",
			SectionCount,
			out XjNativeGenealogyClone clone))
		{
			return null;
		}

		for (int i = 0; i < clone.Sections.Count; i++)
		{
			clone.Sections[i].gameObject.SetActive(i < SectionCount);
		}
		SetTitle(clone.Root, clone.Sections);
		clone.Root.gameObject.AddComponent<XjMentorshipTabMarker>();
		XjMentorshipTabController controller = clone.Root.gameObject.AddComponent<XjMentorshipTabController>();
		controller.Initialize(window, clone.Root, clone.Sections, clone.AvatarPrefab);
		controller.Bind(window, actor, force: false);
		return clone.Root;
	}

	internal static void Bind(Transform root, UnitWindow window, Actor actor, bool force)
	{
		if (root == null) return;
		XjMentorshipTabController controller = root.GetComponent<XjMentorshipTabController>();
		if (controller == null) return;
		controller.Bind(window, actor, force);
	}

	internal static string ResolveActorName(Actor actor)
	{
		return string.IsNullOrWhiteSpace(actor?.getName()) ? "无名修士" : actor.getName().Trim();
	}

	internal static string ResolveTooltipSummary(Actor actor)
	{
		string realm = FormatActorRealm(actor);
		string rank = string.Empty;
		try { rank = XjSectIdentityReader.BuildIdentity(actor).Rank; }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Mentorship.ResolveRank", ex); }
		return realm + (string.IsNullOrWhiteSpace(rank) ? string.Empty : " · " + rank);
	}

	internal static string ResolveTooltipDetails(Actor actor)
	{
		try
		{
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			return "道途：" + XjDisplayNameSanitizer.GameTerm(snapshot.DaoTu, "未定");
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Mentorship.ResolveDetails", ex);
			return "点击查看角色信息";
		}
	}

	internal static void OpenActor(long actorId)
	{
		if (actorId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| !XjSafeCore.IsAliveActor(actor)) return;
		try { ActionLibrary.openUnitWindow(actor); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Mentorship.OpenActor", ex); }
	}

	private static string FormatActorRealm(Actor actor)
	{
		int realm = 0;
		try { realm = XjSectCityData.GetRealmLevel(actor); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("Mentorship.ResolveRealm", ex); }
		return realm switch
		{
			5 => "金丹",
			4 => "紫府",
			3 => "筑基",
			2 => "炼气",
			1 => "胎息",
			_ => "凡俗"
		};
	}

	private static void SetTitle(Transform root, IReadOnlyList<Transform> sections)
	{
		if (root == null) return;
		Text[] texts = root.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			Text text = texts[i];
			if (text == null || IsInsideAnySection(text.transform, sections)) continue;
			LocalizedText localized = text.GetComponent<LocalizedText>();
			if (localized != null) localized.enabled = false;
			text.font = LocalizedTextManager.current_font ?? text.font;
			text.text = "师徒传承";
			return;
		}
	}

	private static bool IsInsideAnySection(Transform target, IReadOnlyList<Transform> sections)
	{
		if (target == null || sections == null) return false;
		for (int i = 0; i < sections.Count; i++)
		{
			for (Transform current = target; current != null; current = current.parent)
			{
				if (current == sections[i]) return true;
			}
		}
		return false;
	}
}

internal sealed class XjMentorshipTabController : MonoBehaviour
{
	private readonly List<XjMentorshipAvatarSlot> _teacherSlots = new List<XjMentorshipAvatarSlot>(1);
	private readonly List<XjMentorshipAvatarSlot> _studentSlots = new List<XjMentorshipAvatarSlot>(8);
	private UnitWindow _window;
	private long _actorId;
	private Transform _root;
	private Transform _teacherSection;
	private Transform _studentSection;
	private Transform _teacherGrid;
	private Transform _studentGrid;
	private UiUnitAvatarElement _avatarPrefab;
	private bool _initialized;
	private bool _pending;
	private bool _force;
	private int _executeFrame;
	private int _lastSignature;
	private long _lastRenderedActorId;

	internal void Initialize(UnitWindow window, Transform root, IReadOnlyList<Transform> sections, UiUnitAvatarElement avatarPrefab)
	{
		_window = window;
		_root = root;
		_avatarPrefab = avatarPrefab;
		if (sections == null || sections.Count < 2) return;
		_teacherSection = sections[0];
		_studentSection = sections[1];
		Font font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		XjNativeGenealogySectionRenderer.TryPrepare(
			_teacherSection,
			"师承",
			font,
			XjIconShelfRenderer.CalculateMetrics(0, true, iconCellSize: XjMentorshipTabView.SlotSize),
			out _teacherGrid,
			clearChildren: true);
		XjNativeGenealogySectionRenderer.TryPrepare(
			_studentSection,
			"门下弟子",
			font,
			XjIconShelfRenderer.CalculateMetrics(0, true, iconCellSize: XjMentorshipTabView.SlotSize),
			out _studentGrid,
			clearChildren: true);
		_initialized = _teacherGrid != null && _studentGrid != null;
		if (_initialized) XjNativeGenealogyTemplateProvider.ConvergeLayout(_root, new[] { _teacherSection, _studentSection }, 2);
	}

	internal void Bind(UnitWindow window, Actor actor, bool force)
	{
		if (!_initialized || actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		_window = window ?? _window;
		bool actorChanged = actorId != _actorId;
		_actorId = actorId;
		_pending = true;
		_force |= force || actorChanged;
		_executeFrame = Time.frameCount + 1;
		enabled = true;
	}

	private void OnEnable()
	{
		if (_initialized && _actorId > 0L)
		{
			_pending = true;
			_executeFrame = Time.frameCount + 1;
			enabled = true;
		}
	}

	private void LateUpdate()
	{
		if (!_pending || Time.frameCount < _executeFrame) return;
		if (XjUiInputBlocker.IsInteractionActive)
		{
			_executeFrame = Time.frameCount + 1;
			return;
		}

		_pending = false;
		enabled = false;
		if (!TryResolveBoundActor(out Actor actor) || !IsBindingCurrent(actor)) return;
		try
		{
			Actor teacher = null;
			if (XjSectMentorshipSystem.TryGetTeacher(actor, out Actor foundTeacher)
				&& XjSafeCore.IsAliveActor(foundTeacher))
			{
				teacher = foundTeacher;
			}
			List<Actor> students = XjSectMentorshipSystem.GetLiveStudents(actor) ?? new List<Actor>();
			students.RemoveAll(student => !XjSafeCore.IsAliveActor(student) || student == teacher);
			students.Sort((left, right) => ResolveActorId(left).CompareTo(ResolveActorId(right)));
			if (students.Count > 64) students.RemoveRange(64, students.Count - 64);

			int signature = ComputeSignature(_actorId, teacher, students);
			if (!_force && _lastRenderedActorId == _actorId && _lastSignature == signature) return;
			_force = false;
			if (!IsBindingCurrent(actor)) return;
			Render(teacher, students);
			_lastRenderedActorId = _actorId;
			_lastSignature = signature;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("Mentorship.Refresh", ex);
		}
	}

	private bool TryResolveBoundActor(out Actor actor)
	{
		actor = null;
		return _actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(_actorId, out actor)
			&& XjSafeCore.IsAliveActor(actor);
	}

	private bool IsBindingCurrent(Actor actor)
	{
		if (!_initialized || !gameObject.activeInHierarchy || !XjSafeCore.IsAliveActor(actor)) return false;
		if (_window?.actor?.data == null) return true;
		return ((BaseSystemData)_window.actor.data).id == _actorId;
	}

	private void Render(Actor teacher, List<Actor> students)
	{
		Font font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		XjNativeGenealogySectionRenderer.TryPrepare(
			_teacherSection,
			"师承",
			font,
			XjIconShelfRenderer.CalculateMetrics(teacher == null ? 0 : 1, true, iconCellSize: XjMentorshipTabView.SlotSize),
			out _teacherGrid,
			clearChildren: false);
		XjNativeGenealogySectionRenderer.TryPrepare(
			_studentSection,
			"门下弟子",
			font,
			XjIconShelfRenderer.CalculateMetrics(students.Count, true, iconCellSize: XjMentorshipTabView.SlotSize),
			out _studentGrid,
			clearChildren: false);

		RenderPool(_teacherSlots, _teacherGrid, teacher == null ? Array.Empty<Actor>() : new[] { teacher });
		RenderPool(_studentSlots, _studentGrid, students);
		XjNativeGenealogyTemplateProvider.ConvergeLayout(_root, new[] { _teacherSection, _studentSection }, 2);
		XjNativeHoverTooltip.RepairHierarchy(_root);
	}

	private void RenderPool(List<XjMentorshipAvatarSlot> pool, Transform parent, IReadOnlyList<Actor> actors)
	{
		if (parent == null) return;
		while (pool.Count < actors.Count)
		{
			pool.Add(XjMentorshipAvatarSlot.Create(parent, _avatarPrefab));
		}
		for (int i = 0; i < pool.Count; i++)
		{
			if (i < actors.Count) pool[i].Bind(actors[i]);
			else pool[i].Hide();
		}
	}

	private static long ResolveActorId(Actor actor)
	{
		return actor?.data is BaseSystemData data ? data.id : 0L;
	}

	private static int ComputeSignature(long actorId, Actor teacher, IReadOnlyList<Actor> students)
	{
		unchecked
		{
			int hash = 17;
			hash = hash * 31 + actorId.GetHashCode();
			hash = hash * 31 + ResolveActorId(teacher).GetHashCode();
			for (int i = 0; i < students.Count; i++) hash = hash * 31 + ResolveActorId(students[i]).GetHashCode();
			return hash;
		}
	}
}

internal sealed class XjMentorshipAvatarSlot
{
	private readonly GameObject _root;
	private readonly Image _background;
	private readonly Button _button;
	private readonly TipButton _tip;
	private readonly UiUnitAvatarElement _avatar;
	private readonly Image _fallback;

	private XjMentorshipAvatarSlot(GameObject root, Image background, Button button, TipButton tip, UiUnitAvatarElement avatar, Image fallback)
	{
		_root = root;
		_background = background;
		_button = button;
		_tip = tip;
		_avatar = avatar;
		_fallback = fallback;
	}

	internal static XjMentorshipAvatarSlot Create(Transform parent, UiUnitAvatarElement prefab)
	{
		GameObject root = new GameObject("XjMentorshipAvatarSlot", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(RectMask2D));
		root.transform.SetParent(parent, false);
		root.transform.localScale = Vector3.one;
		LayoutElement layout = root.GetComponent<LayoutElement>();
		layout.minWidth = XjMentorshipTabView.SlotSize;
		layout.preferredWidth = XjMentorshipTabView.SlotSize;
		layout.minHeight = XjMentorshipTabView.SlotSize;
		layout.preferredHeight = XjMentorshipTabView.SlotSize;
		layout.flexibleWidth = 0f;
		layout.flexibleHeight = 0f;

		Image background = root.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		background.color = new Color(1f, 1f, 1f, 0.04f);
		background.raycastTarget = true;

		UiUnitAvatarElement avatar = null;
		Image fallback = null;
		if (prefab != null)
		{
			avatar = UnityEngine.Object.Instantiate(prefab, root.transform, false);
			avatar.gameObject.SetActive(false);
		}
		else
		{
			fallback = new GameObject("Fallback", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
			fallback.transform.SetParent(root.transform, false);
			fallback.sprite = SpriteTextureLoader.getSprite("ui/icons/iconCitizen");
			fallback.preserveAspect = true;
			fallback.raycastTarget = false;
			RectTransform rect = fallback.GetComponent<RectTransform>();
			rect.anchorMin = new Vector2(0.5f, 0.5f);
			rect.anchorMax = new Vector2(0.5f, 0.5f);
			rect.pivot = new Vector2(0.5f, 0.5f);
			rect.anchoredPosition = Vector2.zero;
			rect.sizeDelta = new Vector2(XjMentorshipTabView.AvatarVisualSize, XjMentorshipTabView.AvatarVisualSize);
		}

		Button button = root.AddComponent<Button>();
		button.targetGraphic = background;
		TipButton tip = root.AddComponent<TipButton>();
		return new XjMentorshipAvatarSlot(root, background, button, tip, avatar, fallback);
	}

	internal void Bind(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			Hide();
			return;
		}
		_root.SetActive(true);
		bool shown = _avatar != null && XjNativeAvatarInteractionSafety.TryShow(
			_avatar,
			actor,
			XjMentorshipTabView.AvatarVisualSize,
			XjMentorshipTabView.AvatarScale);
		if (_avatar != null) _avatar.gameObject.SetActive(shown);
		if (_fallback != null) _fallback.gameObject.SetActive(!shown);
		_button.onClick.RemoveAllListeners();
		long capturedActorId = actor.data is BaseSystemData data ? data.id : 0L;
		((UnityEvent)(object)_button.onClick).AddListener((UnityAction)delegate { XjMentorshipTabView.OpenActor(capturedActorId); });
		XjNativeHoverTooltip.Ensure(
			_tip,
			XjMentorshipTabView.ResolveActorName(actor),
			XjMentorshipTabView.ResolveTooltipSummary(actor),
			XjMentorshipTabView.ResolveTooltipDetails(actor));
	}

	internal void Hide()
	{
		_button.onClick.RemoveAllListeners();
		_root.SetActive(false);
	}
}

internal sealed class XjMentorshipTabMarker : MonoBehaviour
{
}
