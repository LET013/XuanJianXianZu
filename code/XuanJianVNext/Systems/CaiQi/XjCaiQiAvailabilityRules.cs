using System;
using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.CaiQi;

/// <summary>
/// 空证道途的采气硬门控。服气感气、服气修炼与开道求证不经过本规则；
/// 这里只控制采气候选、先天之气结算以及既有资源的突破消耗。
/// </summary>
internal static class XjCaiQiAvailabilityRules
{
	private const string FutureLongGengBranchId = "LongGeng";
	private const string FutureLongGengResourceId = "longgengzhiqi";
	private const string YuanZhaoResourceId = "yuanzhaozhiqi";
	private const string HongXiaResourceId = "hongxiazhiqi";

	internal static bool IsEntryCollectible(in XjCaiQiCatalogEntry entry)
	{
		return IsBranchCollectible(entry.BranchId);
	}

	internal static bool IsBranchCollectible(string branchId)
	{
		string normalized = (branchId ?? string.Empty).Trim();
		if (string.Equals(normalized, XjCaiQiBranchIds.QingXuan, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.QingXuan);
		}
		if (string.Equals(normalized, FutureLongGengBranchId, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.LongGeng);
		}
		if (string.Equals(normalized, XjCaiQiBranchIds.YuanZhao, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.YuanZhao);
		}
		if (string.Equals(normalized, XjCaiQiBranchIds.HongXia, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.HongXia);
		}
		return true;
	}

	internal static bool IsResourceCollectible(string resourceId)
	{
		string normalized = (resourceId ?? string.Empty).Trim();
		if (string.Equals(normalized, "qingxuanzhiqi", StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.QingXuan);
		}
		if (string.Equals(normalized, FutureLongGengResourceId, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.LongGeng);
		}
		if (string.Equals(normalized, YuanZhaoResourceId, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.YuanZhao);
		}
		if (string.Equals(normalized, HongXiaResourceId, StringComparison.Ordinal))
		{
			return XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.HongXia);
		}
		return true;
	}


	private static bool IsProxyOnlyBranch(string branchId)
	{
		string normalized = (branchId ?? string.Empty).Trim();
		// 虹霞复用土德采气地，但只对已经确立虹霞传承的角色做定向采气；
		// 普通土德随机采气不应平白抽出虹霞，避免把后世道统扩散成公共常见分支。
		return string.Equals(normalized, XjCaiQiBranchIds.HongXia, StringComparison.Ordinal);
	}

	internal static bool TryResolveCollectibleBranch(string placeTypeId, int index, out string branchId)
	{
		branchId = string.Empty;
		if (!XjCaiQiCatalog.TryGetBranchesForPlaceType(placeTypeId, out IReadOnlyList<string> branches)
			|| branches.Count == 0)
		{
			return false;
		}

		int start = index % branches.Count;
		if (start < 0) start += branches.Count;
		for (int probe = 0; probe < branches.Count; probe++)
		{
			string candidate = branches[(start + probe) % branches.Count] ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(candidate)
				&& !IsProxyOnlyBranch(candidate)
				&& IsBranchCollectible(candidate))
			{
				branchId = candidate;
				return true;
			}
		}
		return false;
	}
}
