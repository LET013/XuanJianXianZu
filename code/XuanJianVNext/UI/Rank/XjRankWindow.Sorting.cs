using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Systems.Rank;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Rank;

internal static partial class XjRankWindow
{
	private static void SortActorItems(List<XjRankItem> list)
	{
		list.Sort((left, right) =>
		{
			if (activeSortKeys.Count == 0)
			{
				int powerResult = NormalizeRankSortValue(right.Power).CompareTo(NormalizeRankSortValue(left.Power));
				if (powerResult != 0) return powerResult;
			}
			else
			{
				foreach (XjRankSortKey key in activeSortKeys)
				{
					float vl = NormalizeRankSortValue(key.Def.GetValue(left.Actor));
					float vr = NormalizeRankSortValue(key.Def.GetValue(right.Actor));
					int result = (key.Ascending ? 1 : -1) * vl.CompareTo(vr);
					if (result != 0) return result;
				}
			}

			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			return name != 0 ? name : StableRankActorId(left).CompareTo(StableRankActorId(right));
		});
	}

	private static long StableRankActorId(XjRankItem item)
	{
		return item.Actor?.data == null ? long.MaxValue : ((BaseSystemData)item.Actor.data).id;
	}

	private static float NormalizeRankSortValue(float value)
	{
		if (float.IsNaN(value)) return 0f;
		if (float.IsPositiveInfinity(value)) return float.MaxValue;
		if (float.IsNegativeInfinity(value)) return float.MinValue;
		return value;
	}

	private static double NormalizeRankSortValue(double value)
	{
		if (double.IsNaN(value)) return 0d;
		if (double.IsPositiveInfinity(value)) return double.MaxValue;
		if (double.IsNegativeInfinity(value)) return double.MinValue;
		return value;
	}

	private static void CreateAvailableSortButtons()
	{
		ClearObjectList(sortButtons);
		if (availableSortContainer == null) return;

		for (int i = 0; i < XjRankSortSystem.SortKeyDefs.Count; i++)
		{
			sortButtons.Add(CreateSortIconButton(XjRankSortSystem.SortKeyDefs[i], availableSortContainer, null));
		}
		RefreshDynamicGridLayout(availableSortContainer);
	}

	private static GameObject CreateSortIconButton(XjRankSortKeyDef definition, Transform parent, XjRankSortKey selectedKey)
	{
		Sprite icon = GetSafeSprite(definition.IconPath);
		Image image = CreateImage("Sort_" + definition.Id, parent, icon, Color.white, new Vector2(SideGridCellSize, SideGridCellSize));
		image.preserveAspect = true;
		Button button = image.gameObject.AddComponent<Button>();
		button.targetGraphic = image;
		button.transition = Selectable.Transition.None;
		if (selectedKey == null)
		{
			button.onClick.AddListener(() =>
			{
				if (activeSortKeys.Exists(k => string.Equals(k.Def.Id, definition.Id, StringComparison.Ordinal))) return;
				if (definition.MatchesActor != null)
				{
					// 古释、今释、服气、紫金都是道统筛选排序，同一时刻只能选其一。
					// 否则多个道统取交集会让榜单错误地变为空表。
					activeSortKeys.RemoveAll(k => k?.Def?.MatchesActor != null);
				}
				activeSortKeys.Add(new XjRankSortKey(definition));
				RefreshSelectedSortButtons();
				RefreshCurrentList(false);
			});
		}
		else
		{
			button.onClick.AddListener(() =>
			{
				selectedKey.Toggle();
				RefreshSelectedSortButtons();
				RefreshCurrentList(false);
			});
			Image arrow = CreateImage("Arrow", image.transform, GetSafeSprite(selectedKey.Ascending ? "ui/icons/iconArrowUP" : "ui/icons/iconArrowDOWN"), Color.white, new Vector2(11, 11));
			arrow.raycastTarget = false;
			SetRect(arrow.gameObject, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(-1, -1), new Vector2(11, 11));
			XjRankRightClickHandler rightClick = image.gameObject.AddComponent<XjRankRightClickHandler>();
			rightClick.OnRightClick = () =>
			{
				activeSortKeys.Remove(selectedKey);
				RefreshSelectedSortButtons();
				RefreshCurrentList(false);
			};
		}

		XjRankTooltipTrigger tip = image.gameObject.AddComponent<XjRankTooltipTrigger>();
		tip.TooltipText = selectedKey == null ? definition.Name : definition.Name + (selectedKey.Ascending ? "（升序）" : "（降序）");
		tip.TooltipDescription = selectedKey == null ? "左键加入排序" : "左键切换升降序，右键移除";
		XjNativeHoverTooltip.RegisterPassthrough(tip.TooltipText, tip.TooltipDescription);
		return image.gameObject;
	}

	private static void RefreshSelectedSortButtons()
	{
		ClearObjectList(selectedSortButtons);
		if (selectedSortContainer == null) return;
		for (int i = 0; i < activeSortKeys.Count; i++)
		{
			selectedSortButtons.Add(CreateSortIconButton(activeSortKeys[i].Def, selectedSortContainer, activeSortKeys[i]));
		}
		// Keep one empty icon row after clearing keys. Collapsing this grid shifts the
		// available sort grid and search field, which makes the right panel jump.
		RefreshDynamicGridLayout(selectedSortContainer, 1);
		RebuildRankSideLayout(selectedSortContainer);
		RefreshPrimaryMetricHeader();
	}

	private static void ClearSortKeys()
	{
		activeSortKeys.Clear();
		RefreshSelectedSortButtons();
		RefreshCurrentList(false);
	}

	private static int GetRealmScore(string realmId)
	{
		return XjRealmHelper.GetOrder(realmId);
	}
}
