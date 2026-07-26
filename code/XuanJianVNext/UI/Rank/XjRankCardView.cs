using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.UI.Rank;

internal sealed class XjRankCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	private Actor actor;

	internal void Setup(in XjRankItem item, int index, string sortValue)
	{
		actor = item.Actor;
		SetText("RankText", (index + 1).ToString(CultureInfo.InvariantCulture), RankColor(index));
		SetText("NameText", string.IsNullOrWhiteSpace(item.Name) ? "未名修士" : item.Name, Color.white);
		SetText("PowerText", sortValue, new Color(0.4f, 0.8f, 1f));
		SetText("RightText", string.IsNullOrWhiteSpace(item.DaoTu) ? "未定" : item.DaoTu, new Color(1f, 0.84f, 0f));
		SetText("RealmText", string.IsNullOrWhiteSpace(item.RealmDisplay) ? "未入道" : item.RealmDisplay, new Color(1f, 0.6f, 0.2f));

		UiUnitAvatarElement avatar = GetComponentInChildren<UiUnitAvatarElement>(true);
		if (avatar != null)
		{
			bool valid = actor?.data != null;
			avatar.gameObject.SetActive(valid);
			if (valid)
			{
				avatar.show(actor);
				if (avatar.clanBanner != null) avatar.clanBanner.gameObject.SetActive(false);
			}
		}

		Button button = GetComponent<Button>();
		if (button != null)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				if (actor?.data != null && actor.isAlive())
				{
					XjTrueDamageSystem.EnsureJinXingYaoXieNativeSaveStateForSave(actor);
					ActionLibrary.openUnitWindow(actor);
				}
			});
		}
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		if (actor?.data != null && actor.isAlive())
		{
			XjTrueDamageSystem.EnsureJinXingYaoXieNativeSaveStateForSave(actor);
			actor.showTooltip(this);
		}
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		Tooltip.hideTooltip();
	}

	private void SetText(string childName, string value, Color color)
	{
		Text text = transform.Find(childName)?.GetComponent<Text>();
		if (text == null) return;
		text.text = value ?? string.Empty;
		text.color = color;
	}

	private static Color RankColor(int rank)
	{
		if (rank == 0) return new Color(1f, 0.84f, 0f);
		if (rank == 1) return new Color(0.75f, 0.75f, 0.75f);
		if (rank == 2) return new Color(0.8f, 0.5f, 0.2f);
		return Color.white;
	}

}
