using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 家族 UI/信息页展示投影。只转换 Ledger 与 Pending 记录，
/// 不改变 MemberIndex → Ledger → IdentityIndex 的身份回退链。
/// </summary>
internal static class XjFamilyDisplayProjection
{
	internal static IReadOnlyList<XjFamilyMemberDisplayItem> BuildMemberItems(
		IReadOnlyList<XjFamilyMemberLedgerEntry> ledgerEntries)
	{
		if (ledgerEntries == null || ledgerEntries.Count == 0)
		{
			return Array.Empty<XjFamilyMemberDisplayItem>();
		}

		List<XjFamilyMemberDisplayItem> items = new List<XjFamilyMemberDisplayItem>(ledgerEntries.Count);
		for (int i = 0; i < ledgerEntries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = ledgerEntries[i];
			if (!entry.Found || !entry.IsAlive)
			{
				continue;
			}

			string name = string.IsNullOrWhiteSpace(entry.Name) ? "未名族人" : entry.Name;
			string realmId = XjFamilyMemberLedger.NormalizeRealmId(entry.RealmId);
			string realmDisplay = string.IsNullOrWhiteSpace(entry.RealmDisplay)
				? XjFamilyMemberLedger.ResolveRealmDisplayName(realmId)
				: entry.RealmDisplay;
			string displayText = name
				+ "（第"
				+ entry.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture)
				+ "代";
			if (!string.IsNullOrWhiteSpace(realmId))
			{
				displayText += " - " + realmDisplay;
			}

			displayText += " - " + XjFamilyNames.FamilyStatusConfirmed + "）";
			items.Add(new XjFamilyMemberDisplayItem(
				true,
				entry.ActorId,
				name,
				entry.Generation,
				realmId,
				XjFamilyNames.FamilyStatusConfirmed,
				"父系血脉",
				displayText));
		}

		items.Sort((left, right) =>
		{
			int display = string.Compare(left.DisplayText, right.DisplayText, StringComparison.Ordinal);
			return display != 0 ? display : left.ActorId.CompareTo(right.ActorId);
		});
		return items.Count == 0 ? Array.Empty<XjFamilyMemberDisplayItem>() : items;
	}

	internal static IReadOnlyList<XjFamilyPendingDisplayItem> BuildPendingItems(
		IEnumerable<XjFamilyPendingRecord> records,
		Func<XjFamilyPendingRecord, bool> include)
	{
		if (records == null)
		{
			return Array.Empty<XjFamilyPendingDisplayItem>();
		}

		List<XjFamilyPendingDisplayItem> items = new List<XjFamilyPendingDisplayItem>();
		foreach (XjFamilyPendingRecord record in records)
		{
			if (!record.Found || (include != null && !include(record)))
			{
				continue;
			}

			items.Add(ToPendingItem(record));
		}

		items.Sort((left, right) =>
		{
			int display = string.Compare(left.DisplayText, right.DisplayText, StringComparison.Ordinal);
			return display != 0 ? display : left.ActorId.CompareTo(right.ActorId);
		});
		return items.Count == 0 ? Array.Empty<XjFamilyPendingDisplayItem>() : items;
	}

	internal static IReadOnlyList<XjFamilyMarriageDisplayItem> BuildMarriageItems()
	{
		return Array.Empty<XjFamilyMarriageDisplayItem>();
	}

	private static XjFamilyPendingDisplayItem ToPendingItem(in XjFamilyPendingRecord record)
	{
		string status = string.Equals(record.Reason, XjFamilyIdentityReasons.FatherMissing, StringComparison.Ordinal)
			? XjFamilyNames.FamilyStatusFatherMissing
			: XjFamilyNames.FamilyStatusPendingFather;
		string actorName = string.IsNullOrWhiteSpace(record.ActorName)
			? "未名族人"
			: record.ActorName.Trim();
		string displayText = actorName + "（" + status + "）";

		return new XjFamilyPendingDisplayItem(
			true,
			record.ActorId,
			actorName,
			record.Reason,
			record.ParentId1,
			record.ParentId2,
			record.FatherActorId,
			displayText);
	}
}
