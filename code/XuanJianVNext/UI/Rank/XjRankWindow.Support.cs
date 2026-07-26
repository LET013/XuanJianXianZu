using System;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Rank;
internal sealed class XjRankWindowUpdater : MonoBehaviour
{
	internal Action OnOpen;
	internal Action OnClose;
	internal Action OnUpdate;

	private void OnEnable()
	{
		OnOpen?.Invoke();
	}

	private void OnDisable()
	{
		OnClose?.Invoke();
	}

	private void Update()
	{
		OnUpdate?.Invoke();
	}

}

internal enum XjRankFilterType
{
	And,
	Or,
	Not
}

internal sealed class XjRankFilterSetting
{
	internal readonly string Id;
	internal readonly string DisplayName;
	internal readonly Sprite Icon;
	internal readonly Sprite InnerIcon;
	internal readonly Color IconColor;
	internal readonly Color InnerColor;
	internal readonly Func<Actor, bool> FilterFunc;
	internal XjRankFilterType Type;
	internal bool ToBeRemoved;

	internal XjRankFilterSetting(
		string id,
		string displayName,
		Sprite icon,
		Sprite innerIcon,
		Color iconColor,
		Color innerColor,
		Func<Actor, bool> filterFunc)
	{
		Id = id ?? string.Empty;
		DisplayName = string.IsNullOrWhiteSpace(displayName) ? "\u672a\u547d\u540d\u7b5b\u9009" : displayName.Trim();
		Icon = icon;
		InnerIcon = innerIcon;
		IconColor = iconColor;
		InnerColor = innerColor;
		FilterFunc = filterFunc;
		Type = XjRankFilterType.And;
		ToBeRemoved = false;
	}

	internal void CycleType()
	{
		Type = (XjRankFilterType)(((int)Type + 1) % 3);
	}

	internal Color GetTypeColor()
	{
		return Type switch
		{
			XjRankFilterType.And => new Color(0.4f, 0.8f, 0.4f),
			XjRankFilterType.Or => new Color(0.4f, 0.6f, 1f),
			XjRankFilterType.Not => new Color(1f, 0.4f, 0.4f),
			_ => Color.white
		};
	}
	internal string GetTypeText()
	{
		return Type switch
		{
			XjRankFilterType.And => "\u4e0e",
			XjRankFilterType.Or => "\u6216",
			XjRankFilterType.Not => "\u975e",
			_ => "?"
		};
	}
}

internal sealed class XjRankTooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	internal string TooltipText;
	internal string TooltipDescription;
	private bool _hovering;

	public void OnPointerEnter(PointerEventData eventData)
	{
		string title = XjNativeHoverTooltip.NormalizeDisplayText(TooltipText);
		string description = XjNativeHoverTooltip.NormalizeDisplayText(TooltipDescription);
		if (_hovering || string.IsNullOrWhiteSpace(title))
		{
			return;
		}

		XjNativeHoverTooltip.RegisterPassthrough(title, description);
		_hovering = true;
		Tooltip.show(gameObject, "tip", new TooltipData
		{
			tip_name = title,
			tip_description = description
		});
	}
	public void OnPointerExit(PointerEventData eventData)
	{
		if (!_hovering)
		{
			return;
		}
		_hovering = false;
		Tooltip.hideTooltip();
	}

	private void OnDisable()
	{
		if (_hovering)
		{
			_hovering = false;
			Tooltip.hideTooltip();
		}
	}
}

internal sealed class XjRankRightClickHandler : MonoBehaviour, IPointerClickHandler
{
	internal Action OnRightClick;
	internal bool BlockRightClick = true;

	public void OnPointerClick(PointerEventData eventData)
	{
		if (eventData.button != PointerEventData.InputButton.Right)
		{
			return;
		}
		OnRightClick?.Invoke();
		if (BlockRightClick)
		{
			eventData.Use();
		}
	}
}

