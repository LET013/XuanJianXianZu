using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Mentorship;

internal static class XjMentorshipTabView
{
	private const int SectionCount = 1;
	private const float StudentSlotSize = 56f;
	private const float StudentAvatarVisualSize = 50f;
	private const float StudentAvatarScale = 0.94f;

	internal static Transform CreateTabContent(UnitWindow window, Transform parent, Actor actor)
	{
		if (parent == null)
		{
			return null;
		}

		Transform root;
		List<Transform> sections;
		if (XjNativeGenealogyTemplateProvider.TryClone(
			window,
			parent,
			"XjMentorshipTabRoot",
			SectionCount,
			out XjNativeGenealogyClone clone))
		{
			root = clone.Root;
			sections = clone.Sections;
		}
		else
		{
			root = CreateFallbackRoot(parent);
			sections = XjNativeGenealogyTemplateProvider.CollectSectionRoots(root, null);
		}

		if (root == null || sections.Count < SectionCount)
		{
			return null;
		}

		for (int i = 0; i < sections.Count; i++)
		{
			sections[i].gameObject.SetActive(i < SectionCount);
		}

		SetTitle(root, sections);
		root.gameObject.AddComponent<XjMentorshipTabMarker>();
		RefreshContent(root, actor);
		return root;
	}

	private static Transform CreateFallbackRoot(Transform parent)
	{
		GameObject rootObject = new GameObject(
			"XjMentorshipTabRoot",
			typeof(RectTransform),
			typeof(LayoutElement),
			typeof(VerticalLayoutGroup));
		rootObject.transform.SetParent(parent, false);
		rootObject.transform.localScale = Vector3.one;

		VerticalLayoutGroup stack = rootObject.GetComponent<VerticalLayoutGroup>();
		stack.childAlignment = TextAnchor.UpperCenter;
		stack.childControlWidth = true;
		stack.childControlHeight = false;
		stack.childForceExpandWidth = false;
		stack.childForceExpandHeight = false;
		stack.spacing = 4f;

		GameObject titleObject = new GameObject("Title", typeof(RectTransform), typeof(LayoutElement), typeof(Text));
		titleObject.transform.SetParent(rootObject.transform, false);
		LayoutElement titleLayout = titleObject.GetComponent<LayoutElement>();
		titleLayout.preferredHeight = 24f;
		Text title = titleObject.GetComponent<Text>();
		title.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		title.fontSize = 10;
		title.alignment = TextAnchor.MiddleCenter;
		title.color = Color.white;
		title.text = "师徒传承";

		GameObject section = new GameObject("bg_students", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
		section.transform.SetParent(rootObject.transform, false);
		section.transform.localScale = Vector3.one;
		Image background = section.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		background.color = new Color(1f, 1f, 1f, 0.04f);
		background.raycastTarget = false;

		rootObject.SetActive(true);
		return rootObject.transform;
	}

	internal static void RefreshContent(Transform root, Actor actor)
	{
		if (root == null)
		{
			return;
		}

		List<Transform> sections = XjNativeGenealogyTemplateProvider.CollectSectionRoots(root, null);
		if (sections.Count < SectionCount)
		{
			return;
		}
		SetTitle(root, sections);

		for (int i = 0; i < sections.Count; i++)
		{
			sections[i].gameObject.SetActive(i < SectionCount);
		}

		Font font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		RenderStudentsSection(sections[0], actor, font);
		XjNativeGenealogyTemplateProvider.ConvergeLayout(root, sections, SectionCount);
	}

	private static void RenderStudentsSection(Transform section, Actor actor, Font font)
	{
		List<Actor> relations = new List<Actor>();
		bool hasTeacher = XjZongMenMentorshipSystem.TryGetTeacher(actor, out Actor teacher);
		if (hasTeacher && XjSafeCore.IsAliveActor(teacher))
		{
			relations.Add(teacher);
		}
		List<Actor> students = XjZongMenMentorshipSystem.GetLiveStudents(actor);
		for (int i = 0; i < students.Count; i++)
		{
			if (students[i] != null && !relations.Contains(students[i]))
			{
				relations.Add(students[i]);
			}
		}

		if (!XjNativeGenealogySectionRenderer.TryPrepare(
			section,
			hasTeacher ? "师承与门下" : "门下弟子",
			font,
			XjIconShelfRenderer.CalculateMetrics(
				relations.Count,
				true,
				iconColumns: XjIconShelfRenderer.DefaultIconColumns,
				iconCellSize: StudentSlotSize,
				rowSpacing: 4f),
			out Transform grid))
		{
			return;
		}

		for (int i = 0; i < relations.Count; i++)
		{
			CreateStudentSlot(grid, relations[i]);
		}
	}

	private static void CreateStudentSlot(Transform parent, Actor actor)
	{
		GameObject slot = new GameObject("XjMentorshipStudentSlot", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(RectMask2D));
		slot.transform.SetParent(parent, false);
		slot.transform.localScale = Vector3.one;

		LayoutElement layout = slot.GetComponent<LayoutElement>();
		layout.minWidth = StudentSlotSize;
		layout.preferredWidth = StudentSlotSize;
		layout.minHeight = StudentSlotSize;
		layout.preferredHeight = StudentSlotSize;
		layout.flexibleWidth = 0f;
		layout.flexibleHeight = 0f;

		Image background = slot.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		background.color = new Color(1f, 1f, 1f, 0.04f);
		background.raycastTarget = true;

		CreateAvatar(slot.transform, actor);

		Button button = slot.AddComponent<Button>();
		button.targetGraphic = background;
		button.onClick.RemoveAllListeners();
		((UnityEvent)(object)button.onClick).AddListener((UnityAction)delegate
		{
			TryFocusActor(actor);
		});

		TipButton tip = slot.GetComponent<TipButton>() ?? slot.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, ResolveActorName(actor), ResolveStudentTooltipSummary(actor), ResolveStudentTooltipDetails(actor));
	}

	private static GameObject CreateRow(Transform parent, string name, Font font, string text, Color color)
	{
		GameObject row = new GameObject(name, typeof(RectTransform), typeof(LayoutElement), typeof(Image));
		row.transform.SetParent(parent, false);
		row.transform.localScale = Vector3.one;

		LayoutElement layout = row.GetComponent<LayoutElement>();
		layout.minWidth = XjIconShelfRenderer.DefaultContentWidth;
		layout.preferredWidth = XjIconShelfRenderer.DefaultContentWidth;
		layout.minHeight = 24f;
		layout.preferredHeight = 24f;
		layout.flexibleWidth = 0f;
		layout.flexibleHeight = 0f;

		Image image = row.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		image.type = image.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		image.color = color;
		image.raycastTarget = true;

		GameObject labelObject = new GameObject("Text", typeof(RectTransform), typeof(Text));
		labelObject.transform.SetParent(row.transform, false);
		RectTransform rect = labelObject.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = new Vector2(6f, 1f);
		rect.offsetMax = new Vector2(-6f, -1f);

		Text label = labelObject.GetComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = 9;
		label.resizeTextForBestFit = true;
		label.resizeTextMinSize = 6;
		label.resizeTextMaxSize = 10;
		label.alignment = TextAnchor.MiddleLeft;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
		label.color = Color.white;
		label.raycastTarget = false;
		label.text = text ?? string.Empty;

		return row;
	}

	private static void CreateAvatar(Transform parent, Actor actor)
	{
		GameObject holder = new GameObject("Avatar", typeof(RectTransform), typeof(LayoutElement));
		holder.transform.SetParent(parent, false);
		holder.transform.localScale = Vector3.one;

		RectTransform rect = holder.GetComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 0.5f);
		rect.anchorMax = new Vector2(0.5f, 0.5f);
		rect.pivot = new Vector2(0.5f, 0.5f);
		rect.anchoredPosition = Vector2.zero;
		rect.sizeDelta = new Vector2(StudentAvatarVisualSize, StudentAvatarVisualSize);

		LayoutElement layout = holder.GetComponent<LayoutElement>();
		layout.preferredWidth = StudentAvatarVisualSize;
		layout.preferredHeight = StudentAvatarVisualSize;

		UiUnitAvatarElement prefab = Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement");
		if (prefab == null || !XjSafeCore.IsAliveActor(actor))
		{
			Image fallback = holder.AddComponent<Image>();
			fallback.sprite = SpriteTextureLoader.getSprite("ui/icons/iconCitizen");
			fallback.preserveAspect = true;
			fallback.raycastTarget = false;
			return;
		}

		UiUnitAvatarElement avatar = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
		if (!XjNativeAvatarInteractionSafety.TryShow(avatar, actor, StudentAvatarVisualSize, StudentAvatarScale))
		{
			Image fallback = holder.AddComponent<Image>();
			fallback.sprite = SpriteTextureLoader.getSprite("ui/icons/iconCitizen");
			fallback.preserveAspect = true;
			fallback.raycastTarget = false;
		}
	}

	private static string ResolveActorName(Actor actor)
	{
		return string.IsNullOrWhiteSpace(actor?.getName()) ? "无名弟子" : actor.getName().Trim();
	}

	private static string ResolveStudentTooltipSummary(Actor actor)
	{
		string realm = FormatActorRealm(actor);
		string rank = XjZongMenAccessor.BuildIdentity(actor).Rank;
		return realm + (string.IsNullOrWhiteSpace(rank) ? string.Empty : " · " + rank);
	}

	private static string ResolveStudentTooltipDetails(Actor actor)
	{
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		string daoTu = string.IsNullOrWhiteSpace(snapshot.DaoTu) ? "未定" : snapshot.DaoTu.Trim();
		return "道途：" + daoTu;
	}

	private static string FormatActorRealm(Actor actor)
	{
		int realm = XjZongMenCityData.GetRealmLevel(actor);
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

	private static void TryFocusActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		try
		{
			World.world?.locatePosition(new Vector3(actor.current_position.x, actor.current_position.y, 0f));
		}
		catch
		{
		}
	}

	private static void SetTitle(Transform root, List<Transform> sections)
	{
		Text[] texts = root.GetComponentsInChildren<Text>(true);
		for (int i = 0; i < texts.Length; i++)
		{
			if (texts[i] == null || IsInsideAnySection(texts[i].transform, sections))
			{
				continue;
			}

			LocalizedText localized = texts[i].GetComponent<LocalizedText>();
			if (localized != null) localized.enabled = false;
			texts[i].font = LocalizedTextManager.current_font ?? texts[i].font;
			texts[i].text = "师徒传承";
			return;
		}
	}

	private static bool IsInsideAnySection(Transform target, List<Transform> sections)
	{
		for (int i = 0; i < sections.Count; i++)
		{
			for (Transform current = target; current != null; current = current.parent)
			{
				if (current == sections[i])
				{
					return true;
				}
			}
		}
		return false;
	}
}

internal sealed class XjMentorshipTabMarker : MonoBehaviour
{
}

