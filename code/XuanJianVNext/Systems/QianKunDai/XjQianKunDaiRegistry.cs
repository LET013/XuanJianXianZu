using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.QianKunDai;

internal static class XjQianKunDaiRegistry
{
	internal const int DefaultCapacity = 6;
	internal const string CategoryCaiQi = "CaiQi";
	internal const string CategoryGongFa = "GongFa";
	internal const string CategoryAlchemyMaterial = "AlchemyMaterial";
	internal const string CategoryAlchemyPill = "AlchemyPill";
	internal const string CategoryAlchemyRecipe = "AlchemyRecipe";
	internal const string CategoryTalismanMaterial = "TalismanMaterial";
	internal const string CategoryTalismanItem = "TalismanItem";
	internal const string CategoryFormationMaterial = "FormationMaterial";

	private static readonly Dictionary<long, XjQianKunDaiState> entriesByActorId = new Dictionary<long, XjQianKunDaiState>();

	internal static void AddOrUpdate(XjQianKunDaiState state)
	{
		if (!state.Found || state.ActorId <= 0L)
		{
			return;
		}

		long actorId = state.ActorId;

		// 按 diff 标记：仅当数据实际变化时才触发存档标记
		if (entriesByActorId.TryGetValue(actorId, out XjQianKunDaiState existing))
		{
			if (AreStatesEqual(in existing, in state))
			{
				return;
			}
		}

		entriesByActorId[actorId] = state;
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// 比较两个乾坤袋状态是否一致（避免无效 MarkChanged）
	/// </summary>
	private static bool AreStatesEqual(in XjQianKunDaiState left, in XjQianKunDaiState right)
	{
		return string.Equals(left.ActorName, right.ActorName, System.StringComparison.Ordinal)
			&& string.Equals(left.CaiQiSummary, right.CaiQiSummary, System.StringComparison.Ordinal)
			&& string.Equals(left.FaBaoSummary, right.FaBaoSummary, System.StringComparison.Ordinal)
			&& string.Equals(left.ResourceSummary, right.ResourceSummary, System.StringComparison.Ordinal)
			&& string.Equals(left.Summary, right.Summary, System.StringComparison.Ordinal)
			&& left.UpdatedYear == right.UpdatedYear
			&& left.Capacity == right.Capacity
			&& AreItemsEqual(left.Items, right.Items);
	}

	private static bool AreItemsEqual(IReadOnlyList<XjQianKunDaiItem> left, IReadOnlyList<XjQianKunDaiItem> right)
	{
		left ??= Array.Empty<XjQianKunDaiItem>();
		right ??= Array.Empty<XjQianKunDaiItem>();
		if (left.Count != right.Count) return false;
		for (int i = 0; i < left.Count; i++)
		{
			XjQianKunDaiItem a = left[i];
			XjQianKunDaiItem b = right[i];
			if (!string.Equals(a.ItemId, b.ItemId, StringComparison.Ordinal)
				|| !string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
				|| !string.Equals(a.Category, b.Category, StringComparison.Ordinal)
				|| !string.Equals(a.Source, b.Source, StringComparison.Ordinal)
				|| !string.Equals(a.DaoTu, b.DaoTu, StringComparison.Ordinal)
				|| a.Count != b.Count
				|| a.AcquiredYear != b.AcquiredYear)
				return false;
		}
		return true;
	}

	internal static bool TrySetItemCount(
		long actorId,
		string actorName,
		string itemId,
		string displayName,
		string category,
		string source,
		string daoTu,
		int count,
		int year)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(itemId) || string.IsNullOrWhiteSpace(category))
			return false;

		TryGet(actorId, out XjQianKunDaiState current);
		List<XjQianKunDaiItem> items = new List<XjQianKunDaiItem>(current.Items ?? Array.Empty<XjQianKunDaiItem>());
		int index = FindItemIndex(items, itemId, category);
		if (count <= 0)
		{
			if (index < 0) return false;
			items.RemoveAt(index);
		}
		else if (index >= 0)
		{
			XjQianKunDaiItem old = items[index];
			items[index] = new XjQianKunDaiItem(itemId, displayName, category, source, daoTu, count, old.AcquiredYear > 0 ? old.AcquiredYear : year);
		}
		else
		{
			int capacity = current.Found ? current.Capacity : DefaultCapacity;
			if (string.Equals(category, CategoryCaiQi, StringComparison.Ordinal)
				&& CountCategoryItems(items, CategoryCaiQi) >= capacity)
			{
				return false;
			}
			items.Add(new XjQianKunDaiItem(itemId, displayName, category, source, daoTu, count, year));
		}

		WriteInventoryState(current, actorId, actorName, items, year);
		return true;
	}

	internal static bool TryAddItemCount(
		long actorId,
		string actorName,
		string itemId,
		string displayName,
		string category,
		string source,
		string daoTu,
		int count,
		int year)
	{
		if (count <= 0)
		{
			return false;
		}

		TryGet(actorId, out XjQianKunDaiState current);
		IReadOnlyList<XjQianKunDaiItem> currentItems = current.Items ?? Array.Empty<XjQianKunDaiItem>();
		for (int i = 0; i < currentItems.Count; i++)
		{
			XjQianKunDaiItem item = currentItems[i];
			if (!string.Equals(item.ItemId, itemId, StringComparison.Ordinal)
				|| !string.Equals(item.Category, category, StringComparison.Ordinal))
			{
				continue;
			}

			return TrySetItemCount(
				actorId,
				actorName,
				itemId,
				string.IsNullOrWhiteSpace(displayName) ? item.DisplayName : displayName,
				category,
				string.IsNullOrWhiteSpace(source) ? item.Source : source,
				string.IsNullOrWhiteSpace(daoTu) ? item.DaoTu : daoTu,
				item.Count + count,
				year);
		}

		return TrySetItemCount(actorId, actorName, itemId, displayName, category, source, daoTu, count, year);
	}

	internal static bool TryGetItemCount(long actorId, string itemId, string category, out int count)
	{
		count = 0;
		if (actorId <= 0L
			|| string.IsNullOrWhiteSpace(itemId)
			|| string.IsNullOrWhiteSpace(category)
			|| !TryGet(actorId, out XjQianKunDaiState state))
		{
			return false;
		}

		IReadOnlyList<XjQianKunDaiItem> items = state.Items ?? Array.Empty<XjQianKunDaiItem>();
		for (int i = 0; i < items.Count; i++)
		{
			XjQianKunDaiItem item = items[i];
			if (string.Equals(item.ItemId, itemId, StringComparison.Ordinal)
				&& string.Equals(item.Category, category, StringComparison.Ordinal))
			{
				count = item.Count;
				return count > 0;
			}
		}

		return false;
	}

	internal static bool TryConsumeItem(long actorId, string itemId, string category, int count)
	{
		if (count <= 0 || !TryGet(actorId, out XjQianKunDaiState state))
			return false;

		List<XjQianKunDaiItem> items = new List<XjQianKunDaiItem>(state.Items ?? Array.Empty<XjQianKunDaiItem>());
		int index = FindItemIndex(items, itemId, category);
		if (index < 0 || items[index].Count < count)
			return false;

		XjQianKunDaiItem item = items[index];
		int remaining = item.Count - count;
		if (remaining <= 0)
			items.RemoveAt(index);
		else
			items[index] = new XjQianKunDaiItem(item.ItemId, item.DisplayName, item.Category, item.Source, item.DaoTu, remaining, item.AcquiredYear);

		WriteInventoryState(state, state.ActorId, state.ActorName, items, state.UpdatedYear);
		return true;
	}

	internal static bool RemoveInventory(long actorId)
	{
		if (actorId <= 0L || !entriesByActorId.Remove(actorId))
			return false;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool RemoveCategoryItems(long actorId, string category)
	{
		if (actorId <= 0L
			|| string.IsNullOrWhiteSpace(category)
			|| !TryGet(actorId, out XjQianKunDaiState state))
		{
			return false;
		}

		List<XjQianKunDaiItem> items = new List<XjQianKunDaiItem>(state.Items ?? Array.Empty<XjQianKunDaiItem>());
		int removed = items.RemoveAll(item => string.Equals(item.Category, category, StringComparison.Ordinal));
		if (removed <= 0)
		{
			return false;
		}

		WriteInventoryState(state, state.ActorId, state.ActorName, items, state.UpdatedYear);
		return true;
	}

	private static int FindItemIndex(IReadOnlyList<XjQianKunDaiItem> items, string itemId, string category)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (string.Equals(items[i].ItemId, itemId, StringComparison.Ordinal)
				&& string.Equals(items[i].Category, category, StringComparison.Ordinal))
				return i;
		}
		return -1;
	}

	private static int CountCategoryItems(IReadOnlyList<XjQianKunDaiItem> items, string category)
	{
		int count = 0;
		for (int i = 0; i < items.Count; i++)
		{
			if (string.Equals(items[i].Category, category, StringComparison.Ordinal))
			{
				count++;
			}
		}

		return count;
	}

	private static void WriteInventoryState(XjQianKunDaiState current, long actorId, string actorName, IReadOnlyList<XjQianKunDaiItem> items, int year)
	{
		AddOrUpdate(new XjQianKunDaiState(
			true,
			actorId,
			string.IsNullOrWhiteSpace(actorName) ? current.ActorName : actorName,
			current.CaiQiSummary,
			current.FaBaoSummary,
			current.ResourceSummary,
			year > 0 ? year : current.UpdatedYear,
			current.Summary,
			current.Found ? current.Capacity : DefaultCapacity,
			items));
	}

	internal static bool TryGet(long actorId, out XjQianKunDaiState state)
	{
		if (actorId > 0L && entriesByActorId.TryGetValue(actorId, out state))
		{
			return state.Found;
		}

		state = default;
		return false;
	}

	internal static string GetSummary(long actorId)
	{
		if (!TryGet(actorId, out XjQianKunDaiState state))
		{
			return "暂无乾坤袋记录";
		}

		string year = state.UpdatedYear > 0 ? " - " + state.UpdatedYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
		string summary = BuildInventorySummary(state);
		return summary + year;
	}

	private static string BuildInventorySummary(in XjQianKunDaiState state)
	{
		if (state.Items == null || state.Items.Count == 0)
		{
			return string.IsNullOrWhiteSpace(state.Summary) ? "乾坤袋" : state.Summary.Trim();
		}

		List<string> parts = new List<string>(state.Items.Count);
		for (int i = 0; i < state.Items.Count; i++)
		{
			XjQianKunDaiItem item = state.Items[i];
			if (item.Count <= 0 || string.IsNullOrWhiteSpace(item.DisplayName))
			{
				continue;
			}
			if (!IsSupportedInventoryCategory(item.Category))
			{
				continue;
			}

			string category = FormatCategory(item.Category);
			string count = item.Count > 1 ? "×" + item.Count.ToString(CultureInfo.InvariantCulture) : string.Empty;
			parts.Add(category + "：" + item.DisplayName.Trim() + count);
		}

		return parts.Count == 0 ? "乾坤袋" : string.Join("\n", parts);
	}

	private static string FormatCategory(string category)
	{
		if (string.Equals(category, CategoryGongFa, StringComparison.Ordinal))
		{
			return "功法";
		}

		if (string.Equals(category, CategoryCaiQi, StringComparison.Ordinal))
		{
			return "纳气";
		}

		if (string.Equals(category, CategoryAlchemyMaterial, StringComparison.Ordinal))
		{
			return "药材";
		}

		if (string.Equals(category, CategoryAlchemyPill, StringComparison.Ordinal))
		{
			return "丹药";
		}

		if (string.Equals(category, CategoryAlchemyRecipe, StringComparison.Ordinal))
		{
			return "丹方";
		}

		if (string.Equals(category, CategoryTalismanMaterial, StringComparison.Ordinal))
		{
			return "符材";
		}

		if (string.Equals(category, CategoryTalismanItem, StringComparison.Ordinal))
		{
			return "符箓";
		}

		if (string.Equals(category, CategoryFormationMaterial, StringComparison.Ordinal))
		{
			return "阵材";
		}

		return "物品";
	}

	internal static bool IsSupportedInventoryCategory(string category)
	{
		return string.Equals(category, CategoryGongFa, StringComparison.Ordinal)
			|| string.Equals(category, CategoryCaiQi, StringComparison.Ordinal)
			|| string.Equals(category, CategoryAlchemyMaterial, StringComparison.Ordinal)
			|| string.Equals(category, CategoryAlchemyPill, StringComparison.Ordinal)
			|| string.Equals(category, CategoryAlchemyRecipe, StringComparison.Ordinal)
			|| string.Equals(category, CategoryTalismanMaterial, StringComparison.Ordinal)
			|| string.Equals(category, CategoryTalismanItem, StringComparison.Ordinal)
			|| string.Equals(category, CategoryFormationMaterial, StringComparison.Ordinal);
	}

	internal static IReadOnlyList<XjQianKunDaiState> ReadAllEntries()
	{
		if (entriesByActorId.Count == 0)
		{
			return Array.Empty<XjQianKunDaiState>();
		}

		List<XjQianKunDaiState> entries = new List<XjQianKunDaiState>(entriesByActorId.Values);
		entries.Sort((left, right) =>
		{
			int year = left.UpdatedYear.CompareTo(right.UpdatedYear);
			return year != 0 ? year : left.ActorId.CompareTo(right.ActorId);
		});
		return entries;
	}

	internal static void ExportArchiveRecords(List<XjQianKunDaiArchiveData> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjQianKunDaiState state in entriesByActorId.Values)
		{
			if (!state.Found || state.ActorId <= 0L)
			{
				continue;
			}

			records.Add(new XjQianKunDaiArchiveData
			{
				ActorId = state.ActorId,
				ActorName = state.ActorName,
				CaiQiSummary = state.CaiQiSummary,
				FaBaoSummary = state.FaBaoSummary,
				ResourceSummary = state.ResourceSummary,
				UpdatedYear = state.UpdatedYear,
				Summary = state.Summary,
				Capacity = state.Capacity,
				Items = ExportItems(state.Items)
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjQianKunDaiArchiveData> records)
	{
		entriesByActorId.Clear();
		if (records == null)
		{
			return;
		}

		foreach (XjQianKunDaiArchiveData record in records)
		{
			if (record == null || record.ActorId <= 0L)
			{
				continue;
			}

			entriesByActorId[record.ActorId] = new XjQianKunDaiState(
				true,
				record.ActorId,
				record.ActorName,
				record.CaiQiSummary,
				record.FaBaoSummary,
				record.ResourceSummary,
				record.UpdatedYear,
				record.Summary,
				record.Capacity,
				ImportItems(record.Items));
		}
	}

	private static List<XjQianKunDaiItemArchiveData> ExportItems(IReadOnlyList<XjQianKunDaiItem> items)
	{
		List<XjQianKunDaiItemArchiveData> result = new List<XjQianKunDaiItemArchiveData>();
		if (items == null) return result;
		for (int i = 0; i < items.Count; i++)
		{
			XjQianKunDaiItem item = items[i];
			if (item.Count <= 0 || string.IsNullOrWhiteSpace(item.ItemId)) continue;
			result.Add(new XjQianKunDaiItemArchiveData
			{
				ItemId = item.ItemId,
				DisplayName = item.DisplayName,
				Category = item.Category,
				Source = item.Source,
				DaoTu = item.DaoTu,
				Count = item.Count,
				AcquiredYear = item.AcquiredYear
			});
		}
		return result;
	}

	private static IReadOnlyList<XjQianKunDaiItem> ImportItems(IEnumerable<XjQianKunDaiItemArchiveData> items)
	{
		if (items == null) return Array.Empty<XjQianKunDaiItem>();
		List<XjQianKunDaiItem> result = new List<XjQianKunDaiItem>();
		foreach (XjQianKunDaiItemArchiveData item in items)
		{
			if (item == null || item.Count <= 0 || string.IsNullOrWhiteSpace(item.ItemId)) continue;
			if (string.Equals(item.Category, "FaBao", StringComparison.OrdinalIgnoreCase)) continue;
			if (!IsSupportedInventoryCategory(item.Category)) continue;
			result.Add(new XjQianKunDaiItem(item.ItemId, item.DisplayName, item.Category, item.Source, item.DaoTu, item.Count, item.AcquiredYear));
		}
		return result;
	}

	internal static void Clear()
	{
		entriesByActorId.Clear();
	}
}
