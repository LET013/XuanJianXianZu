using System;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.ActorInfo;

namespace XuanJianVNext.Patches;

internal sealed class XjUnitWindowButtonBindingState : MonoBehaviour
{
	internal bool GenderBound;
	internal bool RemoveScarBound;
}

internal partial class XjVNextPatches
{
	internal const string XuanJianGenderToggleName = "XuanJianGenderToggleButton";
	private const string XuanJianRemoveScarButtonName = "XuanJianRemoveDivineScarButton";
	private const string DivineScarTraitId = "scar_of_divinity";

	private static void XuanJianVNext_TryAddGodToolsUnitWindowButtons(UnitWindow unitWindow)
	{
		if (unitWindow?.actor?.data == null) return;
		EnsureGenderToggleButton(unitWindow);
		EnsureRemoveScarButton(unitWindow);
	}

	private static void EnsureGenderToggleButton(UnitWindow unitWindow)
	{
		Transform avatarTransform = unitWindow?._avatar_element?.transform;
		Actor actor = unitWindow?.actor;
		if (avatarTransform == null || actor?.data == null) return;

		GameObject buttonObject = FindOrCreateImageButton(
			avatarTransform,
			XuanJianGenderToggleName,
			new Vector3(17.5f, -16.8f, 0f),
			new Vector2(16f, 16f));
		Image image = buttonObject.GetComponent<Image>();
		Button button = buttonObject.GetComponent<Button>();
		if (image == null || button == null) return;

		XjUnitWindowButtonBindingState binding = buttonObject.GetComponent<XjUnitWindowButtonBindingState>()
			?? buttonObject.AddComponent<XjUnitWindowButtonBindingState>();
		if (!binding.GenderBound)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				Actor current = unitWindow?.actor;
				if (current?.data == null) return;
				current.data.sex = current.data.sex != ActorSex.Male ? ActorSex.Male : ActorSex.Female;
				MarkActorChanged(current, XjActorStateDomain.Identity);
				try { current.clearSprites(); }
				catch (Exception ex) { XjExceptionDiagnostics.Report("UnitWindow.GodTools.Gender.ClearSprites", ex); }
				try { unitWindow?._avatar_element?.show(current); }
				catch (Exception ex) { XjExceptionDiagnostics.Report("UnitWindow.GodTools.Gender.RefreshAvatar", ex); }
				RefreshGenderIcon(buttonObject, current);
			});
			binding.GenderBound = true;
		}

		RefreshGenderIcon(buttonObject, actor);
		HideFallbackText(buttonObject);
		TipButton tip = buttonObject.GetComponent<TipButton>();
		if (tip != null)
		{
			XjNativeHoverTooltip.Ensure(tip, "性别转换", "点击切换角色性别。", string.Empty);
		}
	}

	private static void RefreshGenderIcon(GameObject buttonObject, Actor actor)
	{
		Image image = buttonObject?.GetComponent<Image>();
		if (image == null || actor?.data == null) return;
		if (actor.data.sex == ActorSex.None)
		{
			image.enabled = false;
			return;
		}

		image.enabled = true;
		image.color = Color.white;
		image.sprite = LoadSprite(actor.data.sex == ActorSex.Male ? "gt_windows/male" : "gt_windows/female");
	}

	private static void EnsureRemoveScarButton(UnitWindow unitWindow)
	{
		if (unitWindow == null || unitWindow._icon_favorite == null) return;
		Actor actor = unitWindow.actor;
		Transform buttonParent = unitWindow._icon_favorite.transform.parent?.parent;
		if (buttonParent == null || actor?.data == null) return;

		GameObject buttonObject = FindOrCreateClonedWindowButton(
			buttonParent,
			unitWindow._icon_favorite.transform.parent.gameObject,
			XuanJianRemoveScarButtonName,
			new Vector3(156.8f, -112f, 0f));
		Button button = buttonObject.GetComponent<Button>();
		Image icon = buttonObject.transform.Find("Icon")?.GetComponent<Image>();
		if (button == null) return;

		bool hasScar = actor.hasTrait(DivineScarTraitId);
		if (buttonObject.activeSelf != hasScar) buttonObject.SetActive(hasScar);
		if (icon != null)
		{
			Sprite scarSprite = LoadSprite("ui/icons/iconDivineScar") ?? LoadSprite("ui/icons/iconTrait");
			icon.sprite = scarSprite;
			icon.enabled = scarSprite != null;
			icon.color = hasScar ? Color.white : new Color(1f, 1f, 1f, 0.35f);
		}
		SetFallbackText(buttonObject, "除痕", icon == null || icon.sprite == null);

		XjUnitWindowButtonBindingState binding = buttonObject.GetComponent<XjUnitWindowButtonBindingState>()
			?? buttonObject.AddComponent<XjUnitWindowButtonBindingState>();
		if (!binding.RemoveScarBound)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				Actor current = unitWindow?.actor;
				if (current?.data == null || !current.hasTrait(DivineScarTraitId)) return;
				current.removeTrait(DivineScarTraitId);
				MarkActorChanged(current, XjActorStateDomain.Identity | XjActorStateDomain.Progression | XjActorStateDomain.HighRealm);
				RefreshUnitWindowActorVisual(unitWindow, current);
				EnsureRemoveScarButton(unitWindow);
			});
			binding.RemoveScarBound = true;
		}

		TipButton tip = buttonObject.GetComponent<TipButton>();
		if (tip != null)
		{
			XjNativeHoverTooltip.Ensure(tip, "删除圣痕", "删除角色圣痕。", string.Empty);
		}
	}

	private static GameObject FindOrCreateImageButton(Transform parent, string name, Vector3 localPosition, Vector2 size)
	{
		Transform existing = parent.Find(name);
		GameObject obj = existing != null
			? existing.gameObject
			: new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(TipButton));
		if (existing == null) obj.transform.SetParent(parent, false);

		RectTransform rect = obj.GetComponent<RectTransform>();
		rect.localPosition = localPosition;
		rect.localScale = Vector3.one;
		rect.sizeDelta = size;
		return obj;
	}

	private static void HideFallbackText(GameObject parentObject)
	{
		Transform existing = parentObject?.transform.Find("FallbackText");
		if (existing != null) existing.gameObject.SetActive(false);
	}

	private static GameObject FindOrCreateClonedWindowButton(
		Transform parent,
		GameObject template,
		string name,
		Vector3 localPosition)
	{
		// Historical name kept for source compatibility only. Since Native Window
		// Stability, this method MUST NOT clone a live WorldBox window hierarchy.
		return XjStableUiFactory.FindOrCreateFixedButton(parent, template, name, localPosition);
	}

	private static void SetFallbackText(GameObject parentObject, string textValue, bool visible)
	{
		if (parentObject == null) return;
		Transform existing = parentObject.transform.Find("FallbackText");
		GameObject textObject = existing != null
			? existing.gameObject
			: new GameObject("FallbackText", typeof(RectTransform), typeof(Text));
		if (existing == null) textObject.transform.SetParent(parentObject.transform, false);

		RectTransform rect = textObject.GetComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;

		Text text = textObject.GetComponent<Text>();
		text.text = textValue ?? string.Empty;
		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = parentObject.name == XuanJianGenderToggleName ? 9 : 7;
		text.alignment = TextAnchor.MiddleCenter;
		text.color = new Color(1f, 0.92f, 0.72f, 1f);
		text.raycastTarget = false;
		textObject.SetActive(visible);
	}

	private static Sprite LoadSprite(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return null;
		try { return SpriteTextureLoader.getSprite(path) ?? Resources.Load<Sprite>(path); }
		catch { return Resources.Load<Sprite>(path); }
	}

	private static void RefreshUnitWindowActorVisual(UnitWindow unitWindow, Actor actor)
	{
		try { actor?.clearSprites(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("UnitWindow.GodTools.Refresh.ClearSprites", ex); }
		try { unitWindow?._avatar_element?.show(actor); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("UnitWindow.GodTools.Refresh.Avatar", ex); }
		try
		{
			XjActorInfoPanelRenderer.Invalidate();
			XjActorInfoPanelRenderer.Refresh(unitWindow);
			XjActorOverviewStatsFormatter.Refresh(unitWindow, forceRefresh: true);
		}
		catch (Exception ex) { XjExceptionDiagnostics.Report("UnitWindow.GodTools.Refresh.Panel", ex); }
	}

	private static void MarkActorChanged(Actor actor, XjActorStateDomain domains)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorStateRevisionStore.Mark(actorId, domains);
	}
}
