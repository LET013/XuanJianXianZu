using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace XuanJianVNext.UI.Common;

/// <summary>
/// Consumes pointer, wheel and drag events on inventory cells so they cannot
/// leak through to the world map. Scrolling and dragging are forwarded to the
/// nearest ScrollRect before the event is marked used.
/// </summary>
internal sealed class XjUiInputBlocker : MonoBehaviour,
	IPointerEnterHandler,
	IPointerExitHandler,
	IPointerDownHandler,
	IPointerUpHandler,
	IPointerClickHandler,
	IInitializePotentialDragHandler,
	IBeginDragHandler,
	IDragHandler,
	IEndDragHandler,
	IScrollHandler
{
	private static readonly HashSet<int> ActiveObjects = new HashSet<int>();
	private ScrollRect _scrollRect;
	private bool _hovered;
	private bool _pressed;
	private bool _dragging;

	internal static bool IsInteractionActive => ActiveObjects.Count > 0;

	internal static XjUiInputBlocker Ensure(GameObject target)
	{
		if (target == null) return null;
		CanvasGroup group = target.GetComponent<CanvasGroup>() ?? target.AddComponent<CanvasGroup>();
		group.blocksRaycasts = true;
		group.interactable = true;
		return target.GetComponent<XjUiInputBlocker>() ?? target.AddComponent<XjUiInputBlocker>();
	}

	private void Awake()
	{
		_scrollRect = GetComponentInParent<ScrollRect>();
	}

	private void OnDisable()
	{
		_hovered = false;
		_pressed = false;
		_dragging = false;
		ActiveObjects.Remove(GetInstanceID());
	}

	private void OnDestroy()
	{
		ActiveObjects.Remove(GetInstanceID());
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		_hovered = true;
		UpdateActive();
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		_hovered = false;
		UpdateActive();
	}

	public void OnPointerDown(PointerEventData eventData)
	{
		_pressed = true;
		UpdateActive();
		eventData?.Use();
	}

	public void OnPointerUp(PointerEventData eventData)
	{
		_pressed = false;
		UpdateActive();
		eventData?.Use();
	}

	public void OnPointerClick(PointerEventData eventData)
	{
		eventData?.Use();
	}

	public void OnInitializePotentialDrag(PointerEventData eventData)
	{
		_scrollRect?.OnInitializePotentialDrag(eventData);
		eventData?.Use();
	}

	public void OnBeginDrag(PointerEventData eventData)
	{
		_dragging = true;
		UpdateActive();
		_scrollRect?.OnBeginDrag(eventData);
		eventData?.Use();
	}

	public void OnDrag(PointerEventData eventData)
	{
		_scrollRect?.OnDrag(eventData);
		eventData?.Use();
	}

	public void OnEndDrag(PointerEventData eventData)
	{
		_scrollRect?.OnEndDrag(eventData);
		_dragging = false;
		_pressed = false;
		UpdateActive();
		eventData?.Use();
	}

	public void OnScroll(PointerEventData eventData)
	{
		_scrollRect?.OnScroll(eventData);
		eventData?.Use();
	}

	private void UpdateActive()
	{
		int id = GetInstanceID();
		if (isActiveAndEnabled && (_hovered || _pressed || _dragging)) ActiveObjects.Add(id);
		else ActiveObjects.Remove(id);
	}
}
