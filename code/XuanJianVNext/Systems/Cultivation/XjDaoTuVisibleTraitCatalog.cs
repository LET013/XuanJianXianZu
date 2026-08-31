using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjDaoTuVisibleTraitEntry
{
	internal readonly string TraitId;
	internal readonly string DaoTuGroup;
	internal readonly string DisplayName;
	internal readonly string DaoTuRootId;
	internal readonly string RootGroupId;

	internal XjDaoTuVisibleTraitEntry(string traitId, string daoTuGroup, string displayName, string daoTuRootId, string rootGroupId)
	{
		TraitId = traitId ?? string.Empty;
		DaoTuGroup = daoTuGroup ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		DaoTuRootId = daoTuRootId ?? string.Empty;
		RootGroupId = rootGroupId ?? string.Empty;
	}
}

internal static class XjDaoTuVisibleTraitCatalog
{
	private static readonly XjDaoTuVisibleTraitEntry[] entries = BuildEntries();
	private static readonly XjDaoTuVisibleTraitEntry[] initialAssignableEntries = BuildInitialAssignableEntries();
	private static readonly string[] allTraitIds = BuildAllTraitIds(entries);

	// 并古在界面目录中仍可等到首次真正入道后再显示；但初次定路概率必须与
	// 普通道途一致，因此随机分配使用独立的完整可修池。长庚仍不进入随机池。
	internal static IReadOnlyList<XjDaoTuVisibleTraitEntry> Entries => BuildPublicEntries();
	internal static IReadOnlyList<XjDaoTuVisibleTraitEntry> InitialAssignableEntries => initialAssignableEntries;

	internal static string[] AllTraitIds => allTraitIds;

	internal static bool TryResolveTraitId(string daoTu, out string traitId)
	{
		traitId = string.Empty;
		string normalized = Normalize(daoTu);
		if (string.IsNullOrEmpty(normalized))
		{
			return false;
		}

		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (string.Equals(Normalize(entry.DisplayName), normalized, StringComparison.Ordinal)
				|| string.Equals(Normalize(entry.TraitId), normalized, StringComparison.Ordinal))
			{
				traitId = entry.TraitId;
				return !string.IsNullOrWhiteSpace(traitId);
			}
		}

		return false;
	}

	internal static bool TryResolveDaoTuByTraitId(string traitId, out string daoTu)
	{
		daoTu = string.Empty;
		string normalized = Normalize(traitId);
		if (string.IsNullOrEmpty(normalized))
		{
			return false;
		}

		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (string.Equals(Normalize(entry.TraitId), normalized, StringComparison.Ordinal))
			{
				daoTu = entry.DisplayName;
				return !string.IsNullOrWhiteSpace(daoTu);
			}
		}

		return false;
	}

	internal static bool TryResolveRootId(string daoTu, out string rootId)
	{
		return XjDaoTuCatalog.TryResolveRootId(daoTu, out rootId);
	}

	/// <summary>
	/// 服气状态机按十八条根类路由，但角色身份与可见特质必须保留到具体分支。
	/// 旧档若只保存了“土德”等根名，按角色稳定种子恢复一个具体分支；同一角色
	/// 在读档、年度推进与界面刷新之间不会重新抽取。
	/// </summary>
	internal static bool TryResolveCanonicalDisplayName(
		string daoTuOrRoot,
		long stableSeed,
		out string displayName)
	{
		displayName = string.Empty;
		string normalized = Normalize(daoTuOrRoot);
		if (normalized.Length == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Length; i++)
		{
			if (string.Equals(Normalize(entries[i].DisplayName), normalized, StringComparison.Ordinal)
				|| string.Equals(Normalize(entries[i].TraitId), normalized, StringComparison.Ordinal))
			{
				displayName = entries[i].DisplayName;
				return !string.IsNullOrWhiteSpace(displayName);
			}
		}

		if (!XjDaoTuCatalog.TryResolveRootId(normalized, out string rootId))
		{
			return false;
		}

		List<XjDaoTuVisibleTraitEntry> candidates = new List<XjDaoTuVisibleTraitEntry>();
		for (int i = 0; i < entries.Length; i++)
		{
			if (string.Equals(entries[i].DaoTuRootId, rootId, StringComparison.Ordinal))
			{
				candidates.Add(entries[i]);
			}
		}
		if (candidates.Count == 0)
		{
			return false;
		}

		int index = candidates.Count == 1
			? 0
			: XjDeterministicHash.PositiveIndex(stableSeed, "fuqi_daotu_branch|" + rootId, candidates.Count);
		displayName = candidates[index].DisplayName;
		return !string.IsNullOrWhiteSpace(displayName);
	}

	private static XjDaoTuVisibleTraitEntry[] BuildEntries()
	{
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>();
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = XjCaiQiCatalog.Entries[i];
			if (string.IsNullOrWhiteSpace(entry.OldTraitId) || !seen.Add(entry.OldTraitId))
			{
				continue;
			}

			XjDaoTuCatalog.TryResolve(entry.DisplayName, out XjDaoTuDefinition definition);
			result.Add(new XjDaoTuVisibleTraitEntry(
				entry.OldTraitId,
				definition.IsBingGu ? "XjBingGu" : ResolveDaoTuGroup(entry.OldTraitId),
				entry.DisplayName,
				definition.RootId,
				definition.GroupId));
		}
		if (seen.Add("XjXiaoKuiDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjXiaoKuiDaoTu", "XjBingGu", "鸺葵", XjDaoTuRootIds.XiaoKui, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjShangWuDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjShangWuDaoTu", "XjBingGu", "上巫", XjDaoTuRootIds.ShangWu, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjYuZhenDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjYuZhenDaoTu", "XjBingGu", "玉真", XjDaoTuRootIds.YuZhen, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjHengZhuDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjHengZhuDaoTu", "XjBingGu", "衡祝", XjDaoTuRootIds.HengZhu, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjQuanDanDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjQuanDanDaoTu", "XjBingGu", "全丹", XjDaoTuRootIds.QuanDan, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjZhiBoDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjZhiBoDaoTu", "XjBingGu", "执孛", XjDaoTuRootIds.ZhiBo, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjSiTianDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjSiTianDaoTu", "XjBingGu", "司天", XjDaoTuRootIds.SiTian, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjDuWeiDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjDuWeiDaoTu", "XjBingGu", "都卫", XjDaoTuRootIds.DuWei, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjQingXuanDaoTu"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjQingXuanDaoTu", "XjBingGu", "青宣", XjDaoTuRootIds.QingXuan, XjDaoTuGroupIds.BingGu));
		}
		if (seen.Add("XjLongGengDaoTong"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjLongGengDaoTong", "JinDe", "长庚", XjDaoTuRootIds.LongGeng, XjDaoTuGroupIds.LaterFounded));
		}
		if (seen.Add("XjYuanZhaoDaoTong"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjYuanZhaoDaoTong", "XjIndependentDao", "渊照", XjDaoTuRootIds.YuanZhao, XjDaoTuGroupIds.LaterFounded));
		}
		if (seen.Add("XjHongXiaDaoTong"))
		{
			result.Add(new XjDaoTuVisibleTraitEntry("XjHongXiaDaoTong", "XjIndependentDao", "虹霞", XjDaoTuRootIds.HongXia, XjDaoTuGroupIds.LaterFounded));
		}
		return result.ToArray();
	}

	private static XjDaoTuVisibleTraitEntry[] BuildInitialAssignableEntries()
	{
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>(entries.Length);
		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (!XjDaoTuCatalog.TryResolve(entry.DisplayName, out XjDaoTuDefinition definition)) continue;
			if (definition.IsCommonAncient)
			{
				result.Add(entry);
				continue;
			}
			if (definition.IsBingGu
				&& XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
				&& core.GameplayImplemented)
			{
				result.Add(entry);
			}
		}
		return result.ToArray();
	}

	private static XjDaoTuVisibleTraitEntry[] BuildPublicEntries()
	{
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>(entries.Length);
		for (int i = 0; i < entries.Length; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (string.Equals(entry.RootGroupId, XjDaoTuGroupIds.BingGu, StringComparison.Ordinal)
				&& !XjDaoTuManifestRegistry.IsDiscovered(entry.DaoTuRootId))
			{
				continue;
			}
			if (string.Equals(entry.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
				&& !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.LongGeng))
			{
				continue;
			}
			if ((string.Equals(entry.DaoTuRootId, XjDaoTuRootIds.YuanZhao, StringComparison.Ordinal)
					|| string.Equals(entry.DaoTuRootId, XjDaoTuRootIds.HongXia, StringComparison.Ordinal))
				&& !XjDaoTuManifestRegistry.IsDiscovered(entry.DaoTuRootId))
			{
				continue;
			}
			result.Add(entry);
		}
		return result.ToArray();
	}

	private static string[] BuildAllTraitIds(XjDaoTuVisibleTraitEntry[] source)
	{
		if (source == null || source.Length == 0)
		{
			return Array.Empty<string>();
		}

		string[] result = new string[source.Length];
		for (int i = 0; i < source.Length; i++)
		{
			result[i] = source[i].TraitId;
		}

		return result;
	}

	private static string ResolveDaoTuGroup(string traitId)
	{
		string id = traitId ?? string.Empty;
		int index = 0;
		while (index < id.Length && !char.IsDigit(id[index]))
		{
			index++;
		}

		return index <= 0 ? id : id.Substring(0, index);
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
