using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Family;

namespace XuanJianVNext.UI.Common;

internal static class XjNativeAvatarCellRenderer
{
	internal static void CreateAvatarCell(Transform parent, XjFamilyMemberDisplayItem item, float iconCellSize, UiUnitAvatarElement avatarPrefab)
	{
		if (parent == null || !item.Found || item.ActorId <= 0L
			|| avatarPrefab == null
			|| !XjFamilyReadModel.Shared.TryGetActor(item.ActorId, out Actor actor)
			|| !XjSafeCore.IsAliveActor(actor)) return;

		GameObject wrapper = new GameObject("XjFamilyMemberAvatarCell", typeof(RectTransform), typeof(LayoutElement), typeof(Image), typeof(Button), typeof(TipButton));
		wrapper.transform.SetParent(parent, false);
		wrapper.transform.localScale = Vector3.one;
		RectTransform wrapperRect = wrapper.GetComponent<RectTransform>();
		wrapperRect.sizeDelta = new Vector2(iconCellSize, iconCellSize);
		LayoutElement layout = wrapper.GetComponent<LayoutElement>();
		layout.ignoreLayout = false;
		layout.minWidth = iconCellSize;
		layout.preferredWidth = iconCellSize;
		layout.minHeight = iconCellSize;
		layout.preferredHeight = iconCellSize;
		Image background = wrapper.GetComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		background.type = background.sprite != null ? Image.Type.Sliced : Image.Type.Simple;
		background.color = new Color(1f, 1f, 1f, 0.03f);
		background.raycastTarget = true;

		UiUnitAvatarElement avatar = Object.Instantiate(avatarPrefab, wrapper.transform, false);
		if (avatar == null || !XjNativeAvatarInteractionSafety.TryShow(avatar, actor, iconCellSize, 0.94f))
		{
			Object.Destroy(wrapper);
			return;
		}

		Button button = wrapper.GetComponent<Button>();
		button.targetGraphic = background;
		button.onClick.RemoveAllListeners();
		long actorId = item.ActorId;
		button.onClick.AddListener(delegate
		{
			if (actorId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor current)
				&& XjSafeCore.IsAliveActor(current))
			{
				ActionLibrary.openUnitWindow(current);
			}
		});
		TipButton tip = wrapper.GetComponent<TipButton>();
		string generationText = item.Generation > 0 ? "第" + item.Generation + "代家族成员" : "家族成员";
		XjNativeHoverTooltip.Ensure(tip,
			string.IsNullOrWhiteSpace(item.Name) ? actor.getName() : item.Name,
			generationText,
			string.IsNullOrWhiteSpace(item.DisplayText) ? generationText : item.DisplayText);
	}
}
