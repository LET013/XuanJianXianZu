using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;

namespace XuanJianVNext.UI.QianKunDai;

internal readonly struct XjQianKunDaiDisplayItem
{
	internal readonly string Category;
	internal readonly string Name;
	internal readonly string ItemId;
	internal readonly string DaoTu;
	internal readonly string Source;
	internal readonly int Count;
	internal readonly int AcquiredYear;
	internal readonly int GongFaGrade;

	internal XjQianKunDaiDisplayItem(string category, string name, string itemId, string daoTu, string source, int count, int acquiredYear, int gongFaGrade)
	{
		Category = category ?? string.Empty;
		Name = name ?? string.Empty;
		ItemId = itemId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Source = source ?? string.Empty;
		Count = count < 0 ? 0 : count;
		AcquiredYear = acquiredYear < 0 ? 0 : acquiredYear;
		GongFaGrade = gongFaGrade < 0 ? 0 : gongFaGrade;
	}
}

internal readonly struct XjQianKunDaiDisplayModel
{
	internal readonly bool Found;
	internal readonly string ActorName;
	internal readonly int Capacity;
	internal readonly int UpdatedYear;
	internal readonly XjQianKunDaiDisplayItem[] Items;

	internal XjQianKunDaiDisplayModel(bool found, string actorName, int capacity, int updatedYear, XjQianKunDaiDisplayItem[] items)
	{
		Found = found;
		ActorName = actorName ?? string.Empty;
		Capacity = capacity < 1 ? 1 : capacity;
		UpdatedYear = updatedYear < 0 ? 0 : updatedYear;
		Items = items ?? System.Array.Empty<XjQianKunDaiDisplayItem>();
	}
}

internal static class XjQianKunDaiTabFormatter
{
	internal const string EmptyDisplayText = "暂无";
	internal const string DisplayCategoryQiuJinFa = "QiuJinFa";
	internal const string DisplayCategoryCaiQiFa = "CaiQiFa";
	private const int MaxDisplayEntries = 30;
	private const int MaxDisplayedGongFaEntries = 5;

	internal static XjQianKunDaiDisplayModel BuildDisplayModelForActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjQianKunDaiDisplayModel(false, string.Empty, 1, 0, System.Array.Empty<XjQianKunDaiDisplayItem>());
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return new XjQianKunDaiDisplayModel(false, actor.getName(), 1, 0, System.Array.Empty<XjQianKunDaiDisplayItem>());
		}

		bool hasStoredInventory = XjQianKunDaiRegistry.TryGet(actorId, out XjQianKunDaiState state);
		System.Collections.Generic.IReadOnlyList<XjQianKunDaiItem> stateItems =
			hasStoredInventory ? state.Items ?? System.Array.Empty<XjQianKunDaiItem>() : System.Array.Empty<XjQianKunDaiItem>();
		List<XjQianKunDaiDisplayItem> items = new List<XjQianKunDaiDisplayItem>(stateItems.Count);
		for (int i = 0; i < stateItems.Count && items.Count < MaxDisplayEntries; i++)
		{
			XjQianKunDaiItem item = stateItems[i];
			if (!XjQianKunDaiRegistry.IsSupportedInventoryCategory(item.Category) || item.Count <= 0)
			{
				continue;
			}
			// 功法以 actor 当前快照为唯一显示源，避免持久库存与即时快照同时渲染。
			if (string.Equals(item.Category, XjQianKunDaiRegistry.CategoryGongFa, System.StringComparison.Ordinal))
			{
				continue;
			}

			string name = ResolveDisplayName(item);
			if (HasDisplayItem(items, item.Category, name, item.ItemId))
			{
				continue;
			}
			items.Add(new XjQianKunDaiDisplayItem(
				item.Category,
				name,
				item.ItemId,
				item.DaoTu,
				item.Source,
				item.Count,
				item.AcquiredYear,
				ResolveGongFaGrade(item.DisplayName)));
		}
		AppendActorPracticeItems(actor, items);

		string actorName = hasStoredInventory && !string.IsNullOrWhiteSpace(state.ActorName) ? state.ActorName : actor.getName();
		int capacity = hasStoredInventory ? state.Capacity : XjQianKunDaiRegistry.DefaultCapacity;
		int updatedYear = hasStoredInventory ? state.UpdatedYear : 0;
		return new XjQianKunDaiDisplayModel(true, actorName, capacity, updatedYear, items.ToArray());
	}

	private static void AppendActorPracticeItems(Actor actor, List<XjQianKunDaiDisplayItem> items)
	{
		if (actor?.data == null || items.Count >= MaxDisplayEntries)
		{
			return;
		}

		AppendCanonicalGongFaRecords(actor, items);
		AppendActorCaiQiResource(actor, items);

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (qiuJinFa.Found && !string.IsNullOrWhiteSpace(qiuJinFa.Name))
		{
			items.Add(new XjQianKunDaiDisplayItem(
				DisplayCategoryQiuJinFa,
				qiuJinFa.Name.Trim(),
				"qiujinfa.active",
				qiuJinFa.SourceDaoTu,
				qiuJinFa.SourceGongFaName,
				1,
				qiuJinFa.LastYear,
				qiuJinFa.SourceGongFaGrade));
		}

		if (items.Count >= MaxDisplayEntries)
		{
			return;
		}

		XjCaiQiFaState caiQiFa = XjCaiQiFaAccessor.BuildState(actor);
		if (caiQiFa.Found && !string.IsNullOrWhiteSpace(caiQiFa.Name))
		{
			items.Add(new XjQianKunDaiDisplayItem(
				DisplayCategoryCaiQiFa,
				caiQiFa.Name.Trim(),
				"caiqifa.active",
				caiQiFa.DaoTu,
				caiQiFa.SourcePlace,
				1,
				caiQiFa.SourceYear,
				0));
		}
	}

	private static void AppendCanonicalGongFaRecords(Actor actor, List<XjQianKunDaiDisplayItem> items)
	{
		if (actor?.data == null || items.Count >= MaxDisplayEntries)
		{
			return;
		}

		System.Collections.Generic.IReadOnlyList<XjGongFaInheritanceRecord> records =
			XjGongFaInheritanceSnapshot.BuildRecords(actor, 1);
		for (int i = 0; i < records.Count && items.Count < MaxDisplayEntries; i++)
		{
			XjGongFaInheritanceRecord record = records[i];
			if (!record.Found || string.IsNullOrWhiteSpace(record.Name))
			{
				continue;
			}

			XjQianKunDaiDisplayItem canonical = new XjQianKunDaiDisplayItem(
				XjQianKunDaiRegistry.CategoryGongFa,
				record.Name.Trim(),
				string.IsNullOrWhiteSpace(record.MappedXianJi)
					? "gongfa.inheritance." + i.ToString(System.Globalization.CultureInfo.InvariantCulture)
					: "gongfa.inheritance." + record.MappedXianJi.Trim(),
				record.DaoTu,
				record.MappedXianJi,
				1,
				0,
				record.Grade);
			int existingIndex = FindGongFaDisplayItem(items, record.Name, record.MappedXianJi, record.Grade);
			if (existingIndex >= 0)
			{
				items[existingIndex] = canonical;
			}
			else
			{
				items.Add(canonical);
			}
		}
	}

	private static int FindGongFaDisplayItem(List<XjQianKunDaiDisplayItem> items, string name, string mappedXianJi, int grade)
	{
		string normalized = NormalizeGongFaDisplayName(name);
		string mapped = string.IsNullOrWhiteSpace(mappedXianJi) ? string.Empty : mappedXianJi.Trim();
		for (int i = 0; i < items.Count; i++)
		{
			if (!string.Equals(items[i].Category, XjQianKunDaiRegistry.CategoryGongFa, System.StringComparison.Ordinal))
			{
				continue;
			}
			bool sameMappedXianJi = string.Equals(
				items[i].Source ?? string.Empty,
				mapped,
				System.StringComparison.Ordinal);
			bool sameNameAndGrade = string.Equals(NormalizeGongFaDisplayName(items[i].Name), normalized, System.StringComparison.Ordinal)
				&& items[i].GongFaGrade == grade;
			if (sameNameAndGrade && sameMappedXianJi)
			{
				return i;
			}
		}
		return -1;
	}

	private static void AppendActorCaiQiResource(Actor actor, List<XjQianKunDaiDisplayItem> items)
	{
		if (actor?.data == null || items.Count >= MaxDisplayEntries)
		{
			return;
		}

		XjCaiQiSnapshot snapshot = XjCaiQiActorAccessor.BuildSnapshot(actor);
		if (!snapshot.HasCompletedCaiQi
			|| snapshot.ResourceCount <= 0
			|| XjCaiQiActorAccessor.HasConsumedForBreakthrough(actor)
			|| !XjCaiQiActorAccessor.TryResolveWarehouseResourceId(snapshot, out string resourceId)
			|| string.IsNullOrWhiteSpace(resourceId))
		{
			return;
		}

		for (int i = 0; i < items.Count; i++)
		{
			if (string.Equals(items[i].Category, XjQianKunDaiRegistry.CategoryCaiQi, System.StringComparison.Ordinal)
				&& string.Equals(items[i].ItemId, resourceId, System.StringComparison.Ordinal))
			{
				return;
			}
		}

		string displayName = XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string resolvedName)
			? resolvedName
			: string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "未名先天之气";
		items.Add(new XjQianKunDaiDisplayItem(
			XjQianKunDaiRegistry.CategoryCaiQi,
			displayName,
			resourceId,
			string.Empty,
			string.IsNullOrWhiteSpace(snapshot.SiteName) ? "个人采气" : snapshot.SiteName,
			snapshot.ResourceCount,
			snapshot.LastCaiQiYear,
			0));
	}

	private static string ResolveDisplayName(in XjQianKunDaiItem item)
	{
		if (!string.IsNullOrWhiteSpace(item.DisplayName))
		{
			string storedName = item.DisplayName.Trim();
			return string.Equals(item.Category, XjQianKunDaiRegistry.CategoryCaiQi, System.StringComparison.Ordinal)
				? XjCaiQiCatalog.EnsureQiSuffix(storedName)
				: storedName;
		}
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(item.ItemId, out string displayName))
		{
			return displayName;
		}
		return string.Equals(item.ItemId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "未名库存";
	}

	private static bool HasDisplayItem(List<XjQianKunDaiDisplayItem> items, string category, string name, string itemId)
	{
		for (int i = 0; i < items.Count; i++)
		{
			if (!string.Equals(items[i].Category, category, System.StringComparison.Ordinal))
			{
				continue;
			}
			if (string.Equals(category, XjQianKunDaiRegistry.CategoryGongFa, System.StringComparison.Ordinal)
				&& string.Equals(NormalizeGongFaDisplayName(items[i].Name), NormalizeGongFaDisplayName(name), System.StringComparison.Ordinal)
				&& string.Equals(items[i].ItemId, itemId, System.StringComparison.Ordinal))
			{
				return true;
			}
			if (!string.IsNullOrWhiteSpace(itemId)
				&& string.Equals(items[i].ItemId, itemId, System.StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string NormalizeGongFaDisplayName(string value)
	{
		string result = (value ?? string.Empty).Trim();
		if (result.Length == 0)
		{
			return string.Empty;
		}

		string[] suffixes =
		{
			"（一品）",
			"（二品）",
			"（三品）",
			"（四品）",
			"（五品）",
			"（六品）"
		};
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (result.EndsWith(suffixes[i], System.StringComparison.Ordinal))
			{
				return result.Substring(0, result.Length - suffixes[i].Length).Trim();
			}
		}
		return result;
	}

	private static int ResolveGongFaGrade(string displayName)
	{
		string value = displayName ?? string.Empty;
		if (value.IndexOf("六品", System.StringComparison.Ordinal) >= 0) return 6;
		if (value.IndexOf("五品", System.StringComparison.Ordinal) >= 0) return 5;
		if (value.IndexOf("四品", System.StringComparison.Ordinal) >= 0) return 4;
		if (value.IndexOf("三品", System.StringComparison.Ordinal) >= 0) return 3;
		if (value.IndexOf("二品", System.StringComparison.Ordinal) >= 0) return 2;
		if (value.IndexOf("一品", System.StringComparison.Ordinal) >= 0) return 1;
		return 0;
	}

}
