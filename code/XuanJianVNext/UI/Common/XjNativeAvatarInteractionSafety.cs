using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XuanJianVNext.Core;

namespace XuanJianVNext.UI.Common;

internal static class XjNativeAvatarInteractionSafety
{
	internal static bool TryShow(UiUnitAvatarElement avatar, Actor actor, float visualSize, float scale = 1f)
	{
		if (avatar == null || !XjSafeCore.IsAliveActor(actor)) return false;
		GameObject avatarObject = avatar.gameObject;
		try
		{
			avatar.show_banner_kingdom = false;
			avatar.show_banner_clan = false;
			avatar.show(actor);
			if (avatar.kingdomBanner != null) avatar.kingdomBanner.gameObject.SetActive(false);
			if (avatar.clanBanner != null) avatar.clanBanner.gameObject.SetActive(false);
			RectTransform rect = avatarObject.GetComponent<RectTransform>();
			if (rect != null)
			{
				rect.anchorMin = new Vector2(0.5f, 0.5f);
				rect.anchorMax = new Vector2(0.5f, 0.5f);
				rect.pivot = new Vector2(0.5f, 0.5f);
				rect.anchoredPosition = Vector2.zero;
				rect.sizeDelta = new Vector2(visualSize, visualSize);
				rect.localScale = new Vector3(scale, scale, scale);
			}
			DisableNativeInteraction(avatarObject);
			return true;
		}
		catch
		{
			if (avatarObject != null) avatarObject.SetActive(false);
			return false;
		}
	}

	internal static void DisableNativeInteraction(GameObject avatarObject)
	{
		if (avatarObject == null) return;
		TipButton[] tips = avatarObject.GetComponentsInChildren<TipButton>(true);
		for (int i = 0; i < tips.Length; i++)
		{
			if (tips[i] != null) tips[i].enabled = false;
		}
		EventTrigger[] triggers = avatarObject.GetComponentsInChildren<EventTrigger>(true);
		for (int i = 0; i < triggers.Length; i++)
		{
			if (triggers[i] == null) continue;
			triggers[i].triggers?.Clear();
			triggers[i].enabled = false;
		}
		Button[] buttons = avatarObject.GetComponentsInChildren<Button>(true);
		for (int i = 0; i < buttons.Length; i++)
		{
			if (buttons[i] == null) continue;
			buttons[i].onClick.RemoveAllListeners();
			buttons[i].enabled = false;
		}
		Graphic[] graphics = avatarObject.GetComponentsInChildren<Graphic>(true);
		for (int i = 0; i < graphics.Length; i++)
		{
			if (graphics[i] != null) graphics[i].raycastTarget = false;
		}
	}
}
