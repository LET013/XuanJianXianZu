using System.Collections.Generic;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Warehouse;

internal static class XjFamilyWarehouseWriter
{
	private const int MaxHandledKeys = 4096;
	private static readonly HashSet<string> handledKeys = new HashSet<string>();
	private static readonly Queue<string> handledKeyOrder = new Queue<string>();

	internal static void Handle(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.FamilyStableId <= 0L || domainEvent.ActorId <= 0L)
		{
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiCompleted)
		{
			HandleCaiQi(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiFaObtained)
		{
			HandleCaiQiFa(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeFaBaoObtained)
		{
			// Live-held LingBao/FaBao stays on the actor. Family warehouse receives it only from death snapshot archival.
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaObtained
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaPromoted)
		{
			HandleGongFa(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeQiuJinFaComprehended)
		{
			HandleQiuJinFa(domainEvent);
		}
	}

	internal static void Clear()
	{
		handledKeys.Clear();
		handledKeyOrder.Clear();
	}

	private static void HandleCaiQi(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.FamilyKey)
			|| string.IsNullOrWhiteSpace(domainEvent.CaiQiResourceId)
			|| domainEvent.CaiQiAmount <= 0
			|| !TryMarkHandled(domainEvent, domainEvent.CaiQiResourceId, 0))
		{
			return;
		}

		XjFamilyCaiQiWarehouse.TryAddResource(domainEvent.FamilyKey, domainEvent.CaiQiResourceId, domainEvent.CaiQiAmount);
	}

	private static void HandleCaiQiFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.FamilyKey)
			|| string.IsNullOrWhiteSpace(domainEvent.CaiQiFaName)
			|| string.IsNullOrWhiteSpace(domainEvent.DaoTu)
			|| !TryMarkHandled(domainEvent, domainEvent.CaiQiFaName + "|" + domainEvent.DaoTu, 0))
		{
			return;
		}

		// 采气法正常进入家族仓库，但不写入玄鉴三书。
		XjFamilyCaiQiWarehouse.TryAddCaiQiFa(
			domainEvent.FamilyKey,
			domainEvent.CaiQiFaName,
			domainEvent.DaoTu,
			domainEvent.CaiQiFaSourcePlace,
			domainEvent.Year);
	}

	private static void HandleGongFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.GongFaName)
			|| domainEvent.GongFaGrade < 4
			|| !TryMarkHandled(domainEvent, domainEvent.GongFaName, domainEvent.GongFaGrade))
		{
			return;
		}

		if (XjFamilyGongFaWarehouse.AddGongFaToFamily(
			domainEvent.ActorId,
			domainEvent.FamilyStableId,
			domainEvent.GongFaName,
			domainEvent.GongFaGrade,
			domainEvent.Year,
			XjFamilyGongFaWarehouse.SourceTypeGongFa,
			domainEvent.DaoTu,
			string.Empty,
			domainEvent.MappedXianJi,
			domainEvent.BoundAuthority)
			&& domainEvent.GongFaGrade >= 6)
		{
			XjThreeBookWriter.RecordFamilyInheritance(
				domainEvent.FamilyStableId,
				XjFamilyDisplayNameResolver.Resolve(domainEvent.FamilyStableId),
				domainEvent.ActorId,
				domainEvent.ActorName,
				domainEvent.GongFaName,
				XjGongFaGradeText.Format(domainEvent.GongFaGrade) + "功法",
				domainEvent.Year);
		}
	}

	private static void HandleQiuJinFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.QiuJinFaName)
			|| !TryMarkHandled(domainEvent, domainEvent.QiuJinFaName, domainEvent.GongFaGrade))
		{
			return;
		}

		if (XjFamilyGongFaWarehouse.AddGongFaToFamily(
			domainEvent.ActorId,
			domainEvent.FamilyStableId,
			domainEvent.QiuJinFaName,
			domainEvent.GongFaGrade,
			domainEvent.Year,
			XjFamilyGongFaWarehouse.SourceTypeQiuJinFa,
			domainEvent.DaoTu,
			domainEvent.GongFaName,
			domainEvent.MappedXianJi,
			domainEvent.BoundAuthority))
		{
			XjThreeBookWriter.RecordFamilyInheritance(
				domainEvent.FamilyStableId,
				XjFamilyDisplayNameResolver.Resolve(domainEvent.FamilyStableId),
				domainEvent.ActorId,
				domainEvent.ActorName,
				domainEvent.QiuJinFaName,
				"求金法",
				domainEvent.Year);
		}
	}

	private static bool TryMarkHandled(in XjFamilyDomainEvent domainEvent, string name, int grade)
	{
		string key = domainEvent.FamilyStableId
			+ "|"
			+ domainEvent.ActorId
			+ "|"
			+ domainEvent.EventType
			+ "|"
			+ (name ?? string.Empty).Trim()
			+ "|"
			+ grade
			+ "|"
			+ (domainEvent.DaoTu ?? string.Empty).Trim()
			+ "|"
			+ (domainEvent.MappedXianJi ?? string.Empty).Trim()
			+ "|"
			+ (domainEvent.BoundAuthority ?? string.Empty).Trim()
			+ "|"
			+ domainEvent.Year;
		if (!handledKeys.Add(key))
		{
			return false;
		}

		handledKeyOrder.Enqueue(key);
		while (handledKeyOrder.Count > MaxHandledKeys)
		{
			string expired = handledKeyOrder.Dequeue();
			handledKeys.Remove(expired);
		}
		return true;
	}
}

internal sealed class XjFamilyWarehouseReadModel
{
	internal static XjFamilyWarehouseReadModel Shared { get; } = new XjFamilyWarehouseReadModel();

	internal IReadOnlyList<XjFamilyGongFaWarehouseEntry> ReadFamilyGongFaEntries(long familyId)
	{
		return XjFamilyGongFaWarehouse.ReadFamilyEntries(familyId);
	}

	internal IReadOnlyList<XjFamilyGongFaWarehouseEntry> ReadFamilyGongFaEntries(long familyId, string sourceType)
	{
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyGongFaWarehouse.ReadFamilyEntries(familyId);
		if (entries.Count == 0 || string.IsNullOrWhiteSpace(sourceType))
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseEntry>();
		}

		List<XjFamilyGongFaWarehouseEntry> filtered = new List<XjFamilyGongFaWarehouseEntry>();
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry entry = entries[i];
			if (string.Equals(entry.SourceType, sourceType, System.StringComparison.Ordinal))
			{
				filtered.Add(entry);
			}
		}

		return filtered;
	}

	internal IReadOnlyDictionary<string, int> ReadFamilyCaiQiResources(string familyKey)
	{
		return XjFamilyCaiQiWarehouse.ReadFamilyResources(familyKey, XjFamilyCaiQiWarehouse.ResourceTypeCaiQi);
	}

	internal IReadOnlyDictionary<string, int> ReadFamilyCaiQiFaResources(string familyKey)
	{
		return XjFamilyCaiQiWarehouse.ReadFamilyResources(familyKey, XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa);
	}

	internal IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadFamilyFaBaoEntries(long familyId)
	{
		return XjFamilyFaBaoWarehouse.ReadFamilyEntries(familyId);
	}
}
