using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.FaBao;

namespace XuanJianVNext.Systems.Warehouse;

internal readonly struct XjFamilyFaBaoWarehouseEntry
{
	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly long SectId;
	internal readonly string SectName;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string FaBaoId;
	internal readonly string FaBaoName;
	internal readonly string DaoTu;
	internal readonly string ClassName;
	internal readonly string Source;
	internal readonly int Year;

	internal XjFamilyFaBaoWarehouseEntry(
		bool found,
		long familyStableId,
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
		: this(
			found,
			familyStableId,
			0L,
			string.Empty,
			actorId,
			actorName,
			faBaoId,
			faBaoName,
			daoTu,
			className,
			source,
			year)
	{
	}

	internal XjFamilyFaBaoWarehouseEntry(
		bool found,
		long familyStableId,
		long sectId,
		string sectName,
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		Found = found;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		SectId = sectId < 0L ? 0L : sectId;
		SectName = sectName ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoName = faBaoName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
		Source = source ?? string.Empty;
		Year = year < 0 ? 0 : year;
	}
}

internal static class XjFamilyFaBaoWarehouse
{
	internal const string SourceTypeJinDan = "JinDan";
	internal const string SourceTypeLiveCraft = "LiveCraft";
	internal const string SourceTypeDeathSnapshot = "DeathSnapshot";
	internal const string SourceTypeLostDiscovery = "LostDiscovery";
	internal const string SourceTypeFamilyExtinction = "FamilyExtinction";
	internal const string SourceTypeSectExtinction = "SectExtinction";
	internal const string SourceTypeSectContribution = "SectContribution";

	private static readonly Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>> entriesByFamilyId = new Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>>();
	private static readonly HashSet<string> entryKeys = new HashSet<string>();
	private static readonly Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>> entriesBySectId = new Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>>();
	private static readonly HashSet<string> sectEntryKeys = new HashSet<string>();
	private static readonly List<XjFamilyFaBaoWarehouseEntry> lostEntries = new List<XjFamilyFaBaoWarehouseEntry>();
	private static readonly HashSet<string> lostEntryKeys = new HashSet<string>();
	private static readonly HashSet<string> mutatedLostEntryKeysSinceImport = new HashSet<string>(System.StringComparer.Ordinal);
	// 保存保护层默认会把缺失的永久器物从基线档案补回。所有真实所有权转移/散佚必须显式标记，
	// 否则家族或宗门条目会在保存时被复活，造成一物两份。
	private static readonly HashSet<string> mutatedOwnedEntryKeysSinceImport = new HashSet<string>(System.StringComparer.Ordinal);
	private static readonly List<long> extinctFamilyScratch = new List<long>();
	private static readonly List<long> extinctSectScratch = new List<long>();
	private static readonly List<long> emptyFamilyScratch = new List<long>();
	private static readonly List<long> emptySectScratch = new List<long>();
	private const string LegacyFamilyBorrowSource = "家族借用";
	private const string FamilyBorrowSourcePrefix = "家族借用:";

	internal static bool HasLostEntries => lostEntries.Count > 0;

	internal static bool HasLostEntriesAtOrBeforeYear(int year)
	{
		if (year <= 0) return false;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year <= year) return true;
		}
		return false;
	}
	internal static bool HasFamilyEntries => entryKeys.Count > 0;
	internal static bool HasSectEntries => sectEntryKeys.Count > 0;

	internal static bool AddFaBaoToFamily(
		long actorId,
		string actorName,
		long familyStableId,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| familyStableId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		// 活着的本命主器绝不进入家族器库。此约束放在最终写入口，
		// 避免未来新增炼器、迁移或兼容入口再次绕过上层保护。
		if (IsLiveActorEquipmentRecord(actorId, faBaoId, daoTu, source)) return false;

		string key = familyStableId + "|" + faBaoId.Trim() + "|" + daoTu.Trim();
		if (!entryKeys.Add(key))
		{
			return TryUpdateFamilyEntry(
				familyStableId,
				key,
				new XjFamilyFaBaoWarehouseEntry(
					true,
					familyStableId,
					actorId,
					actorName,
					faBaoId.Trim(),
					faBaoName.Trim(),
					daoTu.Trim(),
					className,
					source,
					year));
		}

		if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries))
		{
			entries = new List<XjFamilyFaBaoWarehouseEntry>();
			entriesByFamilyId[familyStableId] = entries;
		}

		entries.Add(new XjFamilyFaBaoWarehouseEntry(
			true,
			familyStableId,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			source,
			year));
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool AddFaBaoToSect(
		long actorId,
		string actorName,
		long sectId,
		string sectName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| sectId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		// 宗门器库同样不得夺取活修士正在绑定的本命主器。
		if (IsLiveActorEquipmentRecord(actorId, faBaoId, daoTu, source)) return false;

		string key = sectId + "|" + faBaoId.Trim() + "|" + daoTu.Trim();
		XjFamilyFaBaoWarehouseEntry replacement = new XjFamilyFaBaoWarehouseEntry(
			true,
			0L,
			sectId,
			sectName,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			source,
			year);
		if (!sectEntryKeys.Add(key))
		{
			return TryUpdateSectEntry(sectId, key, replacement);
		}

		if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries))
		{
			entries = new List<XjFamilyFaBaoWarehouseEntry>();
			entriesBySectId[sectId] = entries;
		}

		entries.Add(replacement);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	private static bool TryUpdateFamilyEntry(long familyStableId, string key, XjFamilyFaBaoWarehouseEntry replacement)
	{
		if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null
			|| entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry existing = entries[i];
			string existingKey = existing.FamilyStableId + "|" + existing.FaBaoId.Trim() + "|" + existing.DaoTu.Trim();
			if (!string.Equals(existingKey, key, System.StringComparison.Ordinal))
			{
				continue;
			}

			if (string.Equals(existing.FaBaoName, replacement.FaBaoName, System.StringComparison.Ordinal)
				&& string.Equals(existing.ClassName, replacement.ClassName, System.StringComparison.Ordinal)
				&& string.Equals(existing.Source, replacement.Source, System.StringComparison.Ordinal)
				&& existing.Year == replacement.Year
				&& existing.ActorId == replacement.ActorId)
			{
				return false;
			}

			entries[i] = replacement;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			return true;
		}

		return false;
	}

	private static bool TryUpdateSectEntry(long sectId, string key, XjFamilyFaBaoWarehouseEntry replacement)
	{
		if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null
			|| entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry existing = entries[i];
			string existingKey = existing.SectId + "|" + existing.FaBaoId.Trim() + "|" + existing.DaoTu.Trim();
			if (!string.Equals(existingKey, key, System.StringComparison.Ordinal))
			{
				continue;
			}

			if (string.Equals(existing.FaBaoName, replacement.FaBaoName, System.StringComparison.Ordinal)
				&& string.Equals(existing.ClassName, replacement.ClassName, System.StringComparison.Ordinal)
				&& string.Equals(existing.Source, replacement.Source, System.StringComparison.Ordinal)
				&& string.Equals(existing.SectName, replacement.SectName, System.StringComparison.Ordinal)
				&& existing.Year == replacement.Year
				&& existing.ActorId == replacement.ActorId)
			{
				return false;
			}

			entries[i] = replacement;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			return true;
		}

		return false;
	}


	internal static bool IsFamilyBorrowSource(string source)
	{
		string key = (source ?? string.Empty).Trim();
		return string.Equals(key, LegacyFamilyBorrowSource, StringComparison.Ordinal)
			|| key.StartsWith(FamilyBorrowSourcePrefix, StringComparison.Ordinal);
	}

	internal static bool TryResolveBorrowOriginFamilyId(string source, long fallbackFamilyStableId, out long familyStableId)
	{
		familyStableId = 0L;
		string key = (source ?? string.Empty).Trim();
		if (key.StartsWith(FamilyBorrowSourcePrefix, StringComparison.Ordinal))
		{
			string value = key.Substring(FamilyBorrowSourcePrefix.Length);
			if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out long parsed)
				&& parsed > 0L)
			{
				familyStableId = parsed;
				return true;
			}
		}

		if (string.Equals(key, LegacyFamilyBorrowSource, StringComparison.Ordinal) && fallbackFamilyStableId > 0L)
		{
			familyStableId = fallbackFamilyStableId;
			return true;
		}
		return false;
	}

	internal static bool TryCheckoutFamilyFaBao(
		long familyStableId,
		string faBaoId,
		string daoTu,
		out XjFamilyFaBaoWarehouseEntry checkedOut)
	{
		checkedOut = default;
		if (familyStableId <= 0L || string.IsNullOrWhiteSpace(faBaoId) || string.IsNullOrWhiteSpace(daoTu)
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null || entries.Count == 0)
		{
			return false;
		}

		string normalizedId = faBaoId.Trim();
		string normalizedDaoTu = daoTu.Trim();
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!entry.Found
				|| IsLiveActorEquipmentEntry(in entry)
				|| !string.Equals((entry.FaBaoId ?? string.Empty).Trim(), normalizedId, StringComparison.Ordinal)
				|| !string.Equals((entry.DaoTu ?? string.Empty).Trim(), normalizedDaoTu, StringComparison.Ordinal))
			{
				continue;
			}

			checkedOut = entry;
			MarkFamilyEntryRemoved(familyStableId, entry);
			entries.RemoveAt(i);
			if (entries.Count == 0) entriesByFamilyId.Remove(familyStableId);
			MarkWarehouseOwnershipChanged();
			return true;
		}
		return false;
	}

	internal static bool RecordLostFaBao(
		long actorId,
		string actorName,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string className,
		string source,
		int year)
	{
		if (actorId <= 0L
			|| string.IsNullOrWhiteSpace(faBaoId)
			|| string.IsNullOrWhiteSpace(faBaoName)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string key = BuildLostKey(actorId, faBaoId, daoTu);
		if (!lostEntryKeys.Add(key))
		{
			return false;
		}

		lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
			true,
			0L,
			actorId,
			actorName,
			faBaoId.Trim(),
			faBaoName.Trim(),
			daoTu.Trim(),
			className,
			string.IsNullOrWhiteSpace(source) ? "LostOnDeath" : source,
			year));
		mutatedLostEntryKeysSinceImport.Add(key);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadFamilyEntriesView(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		// 瞬时只读投影，避免家族详情为每个家族复制仓库列表；调用方不得持有或修改。
		return entries;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadFamilyEntries(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		List<XjFamilyFaBaoWarehouseEntry> visible = new List<XjFamilyFaBaoWarehouseEntry>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!IsLiveActorEquipmentEntry(in entry)) visible.Add(entry);
		}
		return visible.Count == 0 ? System.Array.Empty<XjFamilyFaBaoWarehouseEntry>() : visible;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadSectEntriesView(long sectId)
	{
		if (sectId <= 0L
			|| !entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		// 瞬时只读投影，避免宗门列表为每个宗门复制器库。调用方不得持有或修改。
		return entries;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadSectEntries(long sectId)
	{
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> entries = ReadSectEntriesView(sectId);
		if (entries.Count == 0) return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		List<XjFamilyFaBaoWarehouseEntry> visible = new List<XjFamilyFaBaoWarehouseEntry>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!IsLiveActorEquipmentEntry(in entry)) visible.Add(entry);
		}
		return visible.Count == 0 ? System.Array.Empty<XjFamilyFaBaoWarehouseEntry>() : visible;
	}

	/// <summary>
	/// 活着的修士本命器物始终属于人物自身，不是家族/宗门器库资产。
	/// 旧版本曾把刚生成的主器同时写入器库，后续又可能被器库借用、转宗或去重，
	/// 从而造成“人物有本命记录但装备消失”的幽灵所有权。此入口只清理与当前
	/// 本命器完全一致的旧错误条目；真正的余器、死后遗宝和家族借用不受影响。
	/// </summary>
	internal static int RemoveLiveBoundPrimaryEntries(long actorId, string faBaoId, string daoTu, string source = "")
	{
		if (IsFamilyBorrowSource(source)) return 0;
		if (actorId <= 0L || string.IsNullOrWhiteSpace(faBaoId) || string.IsNullOrWhiteSpace(daoTu)) return 0;
		string normalizedId = faBaoId.Trim();
		string normalizedDaoTu = daoTu.Trim();
		int removed = 0;
		emptyFamilyScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = pair.Value;
			if (entries == null || entries.Count == 0) continue;
			for (int i = entries.Count - 1; i >= 0; i--)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (entry.ActorId != actorId
					|| !string.Equals((entry.FaBaoId ?? string.Empty).Trim(), normalizedId, StringComparison.Ordinal)
					|| !string.Equals((entry.DaoTu ?? string.Empty).Trim(), normalizedDaoTu, StringComparison.Ordinal)) continue;
				MarkFamilyEntryRemoved(pair.Key, entry);
				entries.RemoveAt(i);
				removed++;
			}
			if (entries.Count == 0) emptyFamilyScratch.Add(pair.Key);
		}
		for (int i = 0; i < emptyFamilyScratch.Count; i++) entriesByFamilyId.Remove(emptyFamilyScratch[i]);
		emptyFamilyScratch.Clear();

		emptySectScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = pair.Value;
			if (entries == null || entries.Count == 0) continue;
			for (int i = entries.Count - 1; i >= 0; i--)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (entry.ActorId != actorId
					|| !string.Equals((entry.FaBaoId ?? string.Empty).Trim(), normalizedId, StringComparison.Ordinal)
					|| !string.Equals((entry.DaoTu ?? string.Empty).Trim(), normalizedDaoTu, StringComparison.Ordinal)) continue;
				MarkSectEntryRemoved(pair.Key, entry);
				entries.RemoveAt(i);
				removed++;
			}
			if (entries.Count == 0) emptySectScratch.Add(pair.Key);
		}
		for (int i = 0; i < emptySectScratch.Count; i++) entriesBySectId.Remove(emptySectScratch[i]);
		emptySectScratch.Clear();
		if (removed > 0) MarkWarehouseOwnershipChanged();
		return removed;
	}

	/// <summary>
	/// 旧档一次性/年度低压对账：只遍历“已经在器库里的条目”，不扫描全世界人物。
	/// 若条目的原主人仍活着且该器物就是其当前本命主器，则恢复其人物装备并从器库剔除。
	/// </summary>
	internal static int ReconcileLiveBoundPrimaryTreasures(int currentYear)
	{
		// 保留旧入口名用于年度调度兼容；0.9.9重做后语义收口为：
		// “任何仍由活角色实际持有/装备的玄鉴器物，都不能同时存在于家族/宗门器库”。
		if (entriesByFamilyId.Count == 0 && entriesBySectId.Count == 0) return 0;
		int removed = 0;
		emptyFamilyScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = pair.Value;
			if (entries == null || entries.Count == 0) continue;
			for (int i = entries.Count - 1; i >= 0; i--)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!TryResolveLiveActorEquipment(in entry, out Actor owner, out bool isAuthoritativePrimary)) continue;
				// 若旧档只有本命权威记录而装备槽已经丢失，先恢复人物装备再清仓库幽灵条目。
				if (isAuthoritativePrimary) XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(owner);
				MarkFamilyEntryRemoved(pair.Key, entry);
				entries.RemoveAt(i);
				removed++;
			}
			if (entries.Count == 0) emptyFamilyScratch.Add(pair.Key);
		}
		for (int i = 0; i < emptyFamilyScratch.Count; i++) entriesByFamilyId.Remove(emptyFamilyScratch[i]);
		emptyFamilyScratch.Clear();

		emptySectScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = pair.Value;
			if (entries == null || entries.Count == 0) continue;
			for (int i = entries.Count - 1; i >= 0; i--)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!TryResolveLiveActorEquipment(in entry, out Actor owner, out bool isAuthoritativePrimary)) continue;
				if (isAuthoritativePrimary) XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(owner);
				MarkSectEntryRemoved(pair.Key, entry);
				entries.RemoveAt(i);
				removed++;
			}
			if (entries.Count == 0) emptySectScratch.Add(pair.Key);
		}
		for (int i = 0; i < emptySectScratch.Count; i++) entriesBySectId.Remove(emptySectScratch[i]);
		emptySectScratch.Clear();
		if (removed > 0) MarkWarehouseOwnershipChanged();
		return removed;
	}

	/// <summary>
	/// 活角色持有优先级高于一切器库收藏。既包括本命主器权威记录，也包括武器/护甲/饰品等
	/// 任意装备槽中的灵宝、法宝、仙器。家族借器在借出期间同样视为“人物实际持有”，
	/// 因而从可借/可展示器库投影中剔除；角色死亡后死亡归档再把实体写回器库。
	/// </summary>
	internal static bool IsLiveActorEquipmentEntry(in XjFamilyFaBaoWarehouseEntry entry)
	{
		return TryResolveLiveActorEquipment(in entry, out _, out _);
	}

	// 兼容旧调用名；始终服从“活角色全部装备优先”不变量。
	internal static bool IsLiveBoundPrimaryEntry(in XjFamilyFaBaoWarehouseEntry entry)
	{
		return IsLiveActorEquipmentEntry(in entry);
	}

	private static bool IsLiveActorEquipmentRecord(long actorId, string faBaoId, string daoTu, string source)
	{
		if (actorId <= 0L || string.IsNullOrWhiteSpace(faBaoId) || string.IsNullOrWhiteSpace(daoTu)) return false;
		XjFamilyFaBaoWarehouseEntry probe = new XjFamilyFaBaoWarehouseEntry(
			true, 0L, actorId, string.Empty, faBaoId.Trim(), string.Empty, daoTu.Trim(), string.Empty, source, 0);
		return TryResolveLiveActorEquipment(in probe, out _, out _);
	}

	private static bool TryResolveLiveActorEquipment(
		in XjFamilyFaBaoWarehouseEntry entry,
		out Actor owner,
		out bool isAuthoritativePrimary)
	{
		owner = null;
		isAuthoritativePrimary = false;
		if (!entry.Found || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.FaBaoId) || string.IsNullOrWhiteSpace(entry.DaoTu)) return false;
		if (!XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out owner) || owner?.data == null || !owner.isAlive())
		{
			owner = null;
			return false;
		}

		string targetId = (entry.FaBaoId ?? string.Empty).Trim();
		string targetDaoTu = (entry.DaoTu ?? string.Empty).Trim();
		XjFaBaoState primary = XjFaBaoAccessor.BuildState(owner);
		if (primary.Found
			&& !IsFamilyBorrowSource(primary.Source)
			&& string.Equals((primary.Id ?? string.Empty).Trim(), targetId, StringComparison.Ordinal)
			&& string.Equals((primary.DaoTu ?? string.Empty).Trim(), targetDaoTu, StringComparison.Ordinal))
		{
			isAuthoritativePrimary = true;
			return true;
		}

		if (owner.equipment == null) return false;
		foreach (ActorEquipmentSlot slot in owner.equipment)
		{
			Item item = slot?.getItem();
			if (item?.data == null || !XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState equipped)) continue;
			if (string.Equals((equipped.Id ?? string.Empty).Trim(), targetId, StringComparison.Ordinal)
				&& string.Equals((equipped.DaoTu ?? string.Empty).Trim(), targetDaoTu, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static void MarkFamilyEntryRemoved(long familyStableId, in XjFamilyFaBaoWarehouseEntry entry)
	{
		entryKeys.Remove(familyStableId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
		mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(familyStableId, 0L, entry.FaBaoId, entry.DaoTu));
	}

	private static void MarkSectEntryRemoved(long sectId, in XjFamilyFaBaoWarehouseEntry entry)
	{
		sectEntryKeys.Remove(sectId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
		mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(0L, sectId, entry.FaBaoId, entry.DaoTu));
	}

	private static void MarkWarehouseOwnershipChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource
			| XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	internal static int CountFamilyEntries(long familyStableId)
	{
		if (familyStableId <= 0L || !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries) || entries == null) return 0;
		int count = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!IsLiveActorEquipmentEntry(in entry)) count++;
		}
		return count;
	}

	internal static int CountSectEntries(long sectId)
	{
		if (sectId <= 0L || !entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries) || entries == null) return 0;
		int count = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = entries[i];
			if (!IsLiveActorEquipmentEntry(in entry)) count++;
		}
		return count;
	}

	/// <summary>
	/// 将家族余器中的最低阶一件真实转入宗门共库。家族至少保留一件器物，
	/// 且宗门仅在尚无自有重宝时执行；家族扣除后写入宗门，写入失败立即回滚。
	/// </summary>
	internal static bool TryContributeSurplusFamilyTreasureToSect(
		long familyStableId,
		long sectId,
		string sectName,
		int currentYear,
		out string treasureName)
	{
		treasureName = string.Empty;
		if (familyStableId <= 0L || sectId <= 0L || CountSectEntries(sectId) > 0
			|| !entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
			|| entries == null || entries.Count < 2)
		{
			return false;
		}

		int candidateIndex = -1;
		int candidateRank = int.MaxValue;
		int validEntryCount = 0;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry candidate = entries[i];
			if (IsLiveActorEquipmentEntry(in candidate)) continue;
			if (!candidate.Found || candidate.ActorId <= 0L || string.IsNullOrWhiteSpace(candidate.FaBaoId)
				|| string.IsNullOrWhiteSpace(candidate.FaBaoName) || string.IsNullOrWhiteSpace(candidate.DaoTu)) continue;
			validEntryCount++;
			int rank = ResolveArtifactRank(candidate.ClassName);
			if (candidateIndex < 0 || rank < candidateRank
				|| rank == candidateRank && candidate.Year < entries[candidateIndex].Year
				|| rank == candidateRank && candidate.Year == entries[candidateIndex].Year
					&& string.CompareOrdinal(candidate.FaBaoName, entries[candidateIndex].FaBaoName) > 0)
			{
				candidateIndex = i;
				candidateRank = rank;
			}
		}
		if (candidateIndex < 0 || validEntryCount < 2) return false;

		XjFamilyFaBaoWarehouseEntry selected = entries[candidateIndex];
		string familyKey = familyStableId + "|" + selected.FaBaoId.Trim() + "|" + selected.DaoTu.Trim();
		string familyOwnerKey = BuildOwnedKey(familyStableId, 0L, selected.FaBaoId, selected.DaoTu);
		bool familyOwnerWasAlreadyMutated = mutatedOwnedEntryKeysSinceImport.Contains(familyOwnerKey);
		mutatedOwnedEntryKeysSinceImport.Add(familyOwnerKey);
		entries.RemoveAt(candidateIndex);
		entryKeys.Remove(familyKey);
		bool added = AddFaBaoToSect(
			selected.ActorId,
			selected.ActorName,
			sectId,
			sectName,
			selected.FaBaoId,
			selected.FaBaoName,
			selected.DaoTu,
			selected.ClassName,
			SourceTypeSectContribution,
			Math.Max(0, currentYear));
		if (!added)
		{
			entries.Insert(Math.Min(candidateIndex, entries.Count), selected);
			entryKeys.Add(familyKey);
			if (!familyOwnerWasAlreadyMutated) mutatedOwnedEntryKeysSinceImport.Remove(familyOwnerKey);
			return false;
		}

		treasureName = selected.FaBaoName.Trim();
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource
			| XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
		return true;
	}

	private static int ResolveArtifactRank(string className)
	{
		if (XjFaBaoCatalog.IsXianQi(className)) return 5;
		if (XjFaBaoCatalog.IsJinDanFaBao(className)) return 4;
		if (XjFaBaoCatalog.IsZiFuLingBao(className)) return 3;
		if (XjFaBaoCatalog.IsZhuJiFaQi(className)) return 2;
		return 1;
	}

	/// <summary>
	/// 家族覆灭后，器库中的真实法器、灵宝与法宝回到既有遗失池，继续由修士低概率拾得。
	/// 只遍历实际拥有器物的家族键，不扫描全部角色或全部家族。
	/// </summary>
	internal static int ReconcileExtinctFamilyTreasures(int currentYear)
	{
		if (entriesByFamilyId.Count == 0) return 0;
		extinctFamilyScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesByFamilyId)
		{
			if (pair.Key <= 0L || pair.Value == null || pair.Value.Count == 0) continue;
			if (XjFamilyMemberLedger.TryGetAggregate(pair.Key, out XjFamilyLedgerAggregate aggregate))
			{
				if (aggregate.AliveCount <= 0) extinctFamilyScratch.Add(pair.Key);
			}
			else if (XjCenturyAnnalsStore.TryGetFamilyStageState(pair.Key, out XjCenturyFamilyStageStateRecord state)
				&& state.WasExtinct)
			{
				// 旧档若家族账本尚未恢复，不把“暂时缺少聚合”误判为覆灭；仅接受该家族 O(1) 阶段索引中的覆灭证据。
				extinctFamilyScratch.Add(pair.Key);
			}
		}

		int movedTotal = 0;
		bool storageChanged = false;
		for (int familyIndex = 0; familyIndex < extinctFamilyScratch.Count; familyIndex++)
		{
			long familyStableId = extinctFamilyScratch[familyIndex];
			if (!entriesByFamilyId.TryGetValue(familyStableId, out List<XjFamilyFaBaoWarehouseEntry> entries)
				|| entries == null || entries.Count == 0)
			{
				storageChanged |= entriesByFamilyId.Remove(familyStableId);
				continue;
			}

			int movedForFamily = 0;
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				// 即使旧档条目损坏，也必须先移除原所有权键，避免失效家族留下幽灵去重键。
				entryKeys.Remove(familyStableId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
				mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(familyStableId, 0L, entry.FaBaoId, entry.DaoTu));
				if (!entry.Found || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName) || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;

				string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
				if (!lostEntryKeys.Add(lostKey)) continue;
				lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					0L,
					entry.ActorId,
					entry.ActorName,
					entry.FaBaoId,
					entry.FaBaoName,
					entry.DaoTu,
					entry.ClassName,
					SourceTypeFamilyExtinction,
					Math.Max(0, currentYear)));
				mutatedLostEntryKeysSinceImport.Add(lostKey);
				movedForFamily++;
			}

			storageChanged |= entriesByFamilyId.Remove(familyStableId);
			if (movedForFamily <= 0) continue;
			movedTotal += movedForFamily;
			string familyName = XjFamilyDisplayNameResolver.Resolve(familyStableId);
			if (string.IsNullOrWhiteSpace(familyName)) familyName = "一支旧族";
			string summary = familyName + "覆灭，器库中" + movedForFamily + "件重宝散佚于世，后世修士仍可能寻得。";
			XjCenturyAnnalsStore.ObserveFamilyEvent(
				"FamilyTreasuresLostOnExtinction",
				Math.Max(1, currentYear),
				familyStableId,
				3,
				summary);
			XjWorldHistoryStore.RecordDomainEvent(
				"家族",
				"重宝散佚",
				summary,
				3,
				familyId: familyStableId,
				year: Math.Max(0, currentYear));
		}

		extinctFamilyScratch.Clear();
		if (!storageChanged) return 0;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource
			| XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
		return movedTotal;
	}

	/// <summary>
	/// 宗门自有器物在灭宗后同样进入既有遗失池，防止新补充的宗门器库形成悬空资产。
	/// 只遍历真正持有宗门器物的键，不建立额外宗门扫描。
	/// </summary>
	internal static int ReconcileExtinctSectTreasures(int currentYear)
	{
		if (entriesBySectId.Count == 0) return 0;
		extinctSectScratch.Clear();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> pair in entriesBySectId)
		{
			if (pair.Key <= 0L || pair.Value == null || pair.Value.Count == 0) continue;
			if (!XjSectRepository.TryGetBySectId(pair.Key, out XjSectArchiveRecord sect)
				|| sect == null || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal))
			{
				extinctSectScratch.Add(pair.Key);
			}
		}

		int movedTotal = 0;
		bool storageChanged = false;
		for (int sectIndex = 0; sectIndex < extinctSectScratch.Count; sectIndex++)
		{
			long sectId = extinctSectScratch[sectIndex];
			if (!entriesBySectId.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> entries)
				|| entries == null || entries.Count == 0)
			{
				storageChanged |= entriesBySectId.Remove(sectId);
				continue;
			}

			int movedForSect = 0;
			string sectName = string.Empty;
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (string.IsNullOrWhiteSpace(sectName) && !string.IsNullOrWhiteSpace(entry.SectName)) sectName = entry.SectName.Trim();
				// 灭宗清库时同步清理所有原宗门键，损坏条目也不能继续占用去重集合。
				sectEntryKeys.Remove(sectId + "|" + (entry.FaBaoId ?? string.Empty).Trim() + "|" + (entry.DaoTu ?? string.Empty).Trim());
				mutatedOwnedEntryKeysSinceImport.Add(BuildOwnedKey(0L, sectId, entry.FaBaoId, entry.DaoTu));
				if (!entry.Found || entry.ActorId <= 0L || string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName) || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;
				string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
				if (!lostEntryKeys.Add(lostKey)) continue;
				lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					0L,
					entry.ActorId,
					entry.ActorName,
					entry.FaBaoId,
					entry.FaBaoName,
					entry.DaoTu,
					entry.ClassName,
					SourceTypeSectExtinction,
					Math.Max(0, currentYear)));
				mutatedLostEntryKeysSinceImport.Add(lostKey);
				movedForSect++;
			}
			storageChanged |= entriesBySectId.Remove(sectId);
			if (movedForSect <= 0) continue;
			movedTotal += movedForSect;
			if (string.IsNullOrWhiteSpace(sectName)) sectName = "一座旧宗";
			string summary = sectName + "覆灭，宗门器库中" + movedForSect + "件重宝散佚于世。";
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectTreasuresLostOnExtinction",
				Math.Max(1, currentYear),
				sectId,
				sectName,
				3,
				summary);
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				"宗门重宝散佚",
				summary,
				3,
				sectId: sectId,
				year: Math.Max(0, currentYear));
		}

		extinctSectScratch.Clear();
		if (!storageChanged) return 0;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(
			XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SectResource | XjCodexDirtyFlags.History
			| XjCodexDirtyFlags.CenturyAnnals);
		return movedTotal;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadAllEntries()
	{
		if (entriesByFamilyId.Count == 0 && entriesBySectId.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoWarehouseEntry>();
		}

		List<XjFamilyFaBaoWarehouseEntry> result = new List<XjFamilyFaBaoWarehouseEntry>();
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> familyEntries in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = familyEntries.Value;
			if (entries == null || entries.Count == 0)
			{
				continue;
			}

			result.AddRange(entries);
		}
		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> sectEntries in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = sectEntries.Value;
			if (entries == null || entries.Count == 0)
			{
				continue;
			}

			result.AddRange(entries);
		}

		result.Sort((left, right) =>
		{
			int byYear = left.Year.CompareTo(right.Year);
			if (byYear != 0)
			{
				return byYear;
			}

			int name = string.Compare(left.FaBaoName, right.FaBaoName, System.StringComparison.Ordinal);
			if (name != 0) return name;
			int id = string.Compare(left.FaBaoId, right.FaBaoId, System.StringComparison.Ordinal);
			if (id != 0) return id;
			int family = left.FamilyStableId.CompareTo(right.FamilyStableId);
			return family != 0 ? family : left.SectId.CompareTo(right.SectId);
		});
		return result;
	}

	internal static IReadOnlyList<XjFamilyFaBaoWarehouseEntry> ReadLostEntries()
	{
		return lostEntries.Count == 0
			? System.Array.Empty<XjFamilyFaBaoWarehouseEntry>()
			: new List<XjFamilyFaBaoWarehouseEntry>(lostEntries);
	}

	internal static bool TryDiscoverLostFaBao(Actor finder, int year)
	{
		if (finder?.data == null || lostEntries.Count == 0)
		{
			return false;
		}

		long finderId = ((BaseSystemData)finder.data).id;
		if (finderId <= 0L
			|| !XjFamilyMemberIndex.Shared.TryGetRecord(finderId, out XjFamilyIdentity identity)
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L
			|| XjFamilyMemberIndex.Shared.IsActorPending(finderId))
		{
			return false;
		}

		int index = PickLostEntryIndex(finderId, year);
		if (index < 0 || index >= lostEntries.Count)
		{
			return false;
		}

		XjFamilyFaBaoWarehouseEntry entry = lostEntries[index];
		string lostKey = BuildLostKey(entry.ActorId, entry.FaBaoId, entry.DaoTu);
		bool lostKeyWasAlreadyMutated = mutatedLostEntryKeysSinceImport.Contains(lostKey);
		lostEntries.RemoveAt(index);
		lostEntryKeys.Remove(lostKey);
		mutatedLostEntryKeysSinceImport.Add(lostKey);

		bool added = AddFaBaoToFamily(
			finderId,
			finder.getName(),
			identity.FamilyStableIdValue,
			entry.FaBaoId,
			entry.FaBaoName,
			entry.DaoTu,
			entry.ClassName,
			SourceTypeLostDiscovery,
			year);
		if (!added)
		{
			// 发现者写入失败时必须把同一件真实器物放回原位置；否则一次重复键、
			// 存档中途状态或家族写入异常都会让遗失池中的重宝永久消失。
			lostEntries.Insert(Math.Min(index, lostEntries.Count), entry);
			lostEntryKeys.Add(lostKey);
			if (!lostKeyWasAlreadyMutated) mutatedLostEntryKeysSinceImport.Remove(lostKey);
			return false;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.FaBaoObtained(
			finder,
			entry.FaBaoId,
			entry.FaBaoName,
			entry.DaoTu,
			SourceTypeLostDiscovery,
			entry.ClassName));
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool WasLostEntryMutatedSinceImport(long actorId, string faBaoId, string daoTu)
	{
		return mutatedLostEntryKeysSinceImport.Contains(BuildLostKey(actorId, faBaoId, daoTu));
	}

	internal static bool WasOwnedEntryMutatedSinceImport(
		long familyStableId,
		long sectId,
		string faBaoId,
		string daoTu)
	{
		return mutatedOwnedEntryKeysSinceImport.Contains(BuildOwnedKey(familyStableId, sectId, faBaoId, daoTu));
	}

	internal static void Clear()
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
		entriesBySectId.Clear();
		sectEntryKeys.Clear();
		lostEntries.Clear();
		lostEntryKeys.Clear();
		mutatedLostEntryKeysSinceImport.Clear();
		mutatedOwnedEntryKeysSinceImport.Clear();
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveFaBaoRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> familyEntry in entriesByFamilyId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = familyEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!entry.Found
					|| entry.FamilyStableId <= 0L
					|| entry.ActorId <= 0L
					|| string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName))
				{
					continue;
				}

				records.Add(new XjWorldArchiveFaBaoRecord
				{
					FamilyStableId = entry.FamilyStableId,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					FaBaoId = entry.FaBaoId,
					FaBaoName = entry.FaBaoName,
					DaoTu = entry.DaoTu,
					ClassName = entry.ClassName,
					Source = entry.Source,
					Year = entry.Year
				});
			}
		}

		foreach (KeyValuePair<long, List<XjFamilyFaBaoWarehouseEntry>> sectEntry in entriesBySectId)
		{
			List<XjFamilyFaBaoWarehouseEntry> entries = sectEntry.Value;
			if (entries == null)
			{
				continue;
			}

			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyFaBaoWarehouseEntry entry = entries[i];
				if (!entry.Found
					|| entry.SectId <= 0L
					|| entry.ActorId <= 0L
					|| string.IsNullOrWhiteSpace(entry.FaBaoId)
					|| string.IsNullOrWhiteSpace(entry.FaBaoName))
				{
					continue;
				}

				records.Add(new XjWorldArchiveFaBaoRecord
				{
					FamilyStableId = 0L,
					SectId = entry.SectId,
					SectName = entry.SectName,
					ActorId = entry.ActorId,
					ActorName = entry.ActorName,
					FaBaoId = entry.FaBaoId,
					FaBaoName = entry.FaBaoName,
					DaoTu = entry.DaoTu,
					ClassName = entry.ClassName,
					Source = entry.Source,
					Year = entry.Year
				});
			}
		}
	}

	internal static void ExportLostArchiveRecords(List<XjWorldArchiveFaBaoRecord> records)
	{
		if (records == null)
		{
			return;
		}

		for (int i = 0; i < lostEntries.Count; i++)
		{
			XjFamilyFaBaoWarehouseEntry entry = lostEntries[i];
			if (!entry.Found
				|| entry.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(entry.FaBaoId)
				|| string.IsNullOrWhiteSpace(entry.FaBaoName))
			{
				continue;
			}

			records.Add(new XjWorldArchiveFaBaoRecord
			{
				FamilyStableId = 0L,
				ActorId = entry.ActorId,
				ActorName = entry.ActorName,
				FaBaoId = entry.FaBaoId,
				FaBaoName = entry.FaBaoName,
				DaoTu = entry.DaoTu,
				ClassName = entry.ClassName,
				Source = entry.Source,
				Year = entry.Year
			});
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveFaBaoRecord> records)
	{
		entriesByFamilyId.Clear();
		entryKeys.Clear();
		entriesBySectId.Clear();
		sectEntryKeys.Clear();
		mutatedOwnedEntryKeysSinceImport.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = records[i];
			if (record == null
				|| (record.FamilyStableId <= 0L && record.SectId <= 0L)
				|| record.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(record.FaBaoId)
				|| string.IsNullOrWhiteSpace(record.FaBaoName)
				|| string.IsNullOrWhiteSpace(record.DaoTu))
			{
				continue;
			}

			if (record.FamilyStableId > 0L)
			{
				string familyKey = record.FamilyStableId + "|" + record.FaBaoId.Trim() + "|" + record.DaoTu.Trim();
				if (!entryKeys.Add(familyKey))
				{
					continue;
				}

				if (!entriesByFamilyId.TryGetValue(record.FamilyStableId, out List<XjFamilyFaBaoWarehouseEntry> familyEntries))
				{
					familyEntries = new List<XjFamilyFaBaoWarehouseEntry>();
					entriesByFamilyId[record.FamilyStableId] = familyEntries;
				}

				familyEntries.Add(new XjFamilyFaBaoWarehouseEntry(
					true,
					record.FamilyStableId,
					record.ActorId,
					record.ActorName,
					record.FaBaoId.Trim(),
					record.FaBaoName.Trim(),
					record.DaoTu.Trim(),
					record.ClassName,
					record.Source,
					record.Year));
				continue;
			}

			string sectKey = record.SectId + "|" + record.FaBaoId.Trim() + "|" + record.DaoTu.Trim();
			if (!sectEntryKeys.Add(sectKey))
			{
				continue;
			}

			if (!entriesBySectId.TryGetValue(record.SectId, out List<XjFamilyFaBaoWarehouseEntry> sectEntries))
			{
				sectEntries = new List<XjFamilyFaBaoWarehouseEntry>();
				entriesBySectId[record.SectId] = sectEntries;
			}

			sectEntries.Add(new XjFamilyFaBaoWarehouseEntry(
				true,
				0L,
				record.SectId,
				record.SectName,
				record.ActorId,
				record.ActorName,
				record.FaBaoId.Trim(),
				record.FaBaoName.Trim(),
				record.DaoTu.Trim(),
				record.ClassName,
				record.Source,
				record.Year));
		}
	}

	internal static void ImportLostArchiveRecords(IReadOnlyList<XjWorldArchiveFaBaoRecord> records)
	{
		lostEntries.Clear();
		lostEntryKeys.Clear();
		mutatedLostEntryKeysSinceImport.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = records[i];
			if (record == null
				|| record.ActorId <= 0L
				|| string.IsNullOrWhiteSpace(record.FaBaoId)
				|| string.IsNullOrWhiteSpace(record.FaBaoName)
				|| string.IsNullOrWhiteSpace(record.DaoTu))
			{
				continue;
			}

			string key = BuildLostKey(record.ActorId, record.FaBaoId, record.DaoTu);
			if (!lostEntryKeys.Add(key))
			{
				continue;
			}

			lostEntries.Add(new XjFamilyFaBaoWarehouseEntry(
				true,
				0L,
				record.ActorId,
				record.ActorName,
				record.FaBaoId.Trim(),
				record.FaBaoName.Trim(),
				record.DaoTu.Trim(),
				record.ClassName,
				record.Source,
			record.Year));
		}
	}

	private static int PickLostEntryIndex(long actorId, int year)
	{
		if (lostEntries.Count == 0 || year <= 0)
		{
			return -1;
		}

		int eligibleCount = 0;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year <= year) eligibleCount++;
		}
		if (eligibleCount <= 0) return -1;

		long mixed = actorId * 1103515245L + year * 12345L;
		int seed = (int)System.Math.Abs(mixed % 2147483647L);
		int eligibleIndex = seed % eligibleCount;
		for (int i = 0; i < lostEntries.Count; i++)
		{
			if (lostEntries[i].Year > year) continue;
			if (eligibleIndex-- == 0) return i;
		}
		return -1;
	}

	private static string BuildOwnedKey(long familyStableId, long sectId, string faBaoId, string daoTu)
	{
		string owner = familyStableId > 0L
			? "family:" + familyStableId
			: "sect:" + Math.Max(0L, sectId);
		return owner + "|" + (faBaoId ?? string.Empty).Trim() + "|" + (daoTu ?? string.Empty).Trim();
	}

	private static string BuildLostKey(long actorId, string faBaoId, string daoTu)
	{
		return actorId + "|" + (faBaoId ?? string.Empty).Trim() + "|" + (daoTu ?? string.Empty).Trim();
	}
}
