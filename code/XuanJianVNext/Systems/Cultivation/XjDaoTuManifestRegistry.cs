using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjDaoTuManifestRegistry
{
	private static readonly Dictionary<string, XjDaoTuManifestArchiveData> Records =
		new Dictionary<string, XjDaoTuManifestArchiveData>(StringComparer.Ordinal);

	internal static bool IsDiscovered(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)) return false;
		if (definition.IsCommonAncient) return true;
		return Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData record) && record.Discovered;
	}

	internal static bool IsFuQiManifested(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)) return false;
		if (definition.IsCommonAncient) return true;
		return Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData record) && record.FuQiManifested;
	}

	internal static bool IsZiJinManifested(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)) return false;
		if (definition.IsCommonAncient) return true;
		return Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData record) && record.ZiJinManifested;
	}

	/// <summary>
	/// 采气开放与服气显世、紫金显世分开记录。青宣必须等空证果位真正成立；
	/// 长庚必须等无名剑道开位并命名；渊照则在五百年空证史实成立后，仍须等
	/// 水月照真洞天与唯一先天之气源真实落地，才允许修士进入对应修炼路径。
	/// </summary>
	internal static bool IsCaiQiUnlocked(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)) return false;
		if (string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			return XjFuQiSwordWorldState.IsEstablished
				|| Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData longGengRecord)
					&& longGengRecord.CaiQiUnlocked;
		}
		if (string.Equals(definition.RootId, XjDaoTuRootIds.YuanZhao, StringComparison.Ordinal))
		{
			return Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData yuanZhaoRecord)
				&& yuanZhaoRecord.CaiQiUnlocked;
		}
		if (!string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal)) return true;
		return Records.TryGetValue(definition.RootId, out XjDaoTuManifestArchiveData record)
			&& record.CaiQiUnlocked;
	}


	/// <summary>
	/// 后世新辟道途的统一“可实际入道”门。道途被历史公开并不必然代表修士已有
	/// 物理修炼条件；渊照必须额外等水月照真洞天与唯一先天之气源就绪。
	/// </summary>
	internal static bool CanEnterLaterFoundedPath(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.IsLaterFounded
			|| !IsDiscovered(definition.RootId)) return false;
		return !string.Equals(definition.RootId, XjDaoTuRootIds.YuanZhao, StringComparison.Ordinal)
			|| IsCaiQiUnlocked(definition.RootId);
	}

	internal static bool CanManifestFuQi(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.SupportsFuQi) return false;
		if (definition.IsCommonAncient) return true;
		// 渊照的“道途已被天地承认”和“后世已有可采之气”是两个阶段。
		// 洞天/固定气源尚未落地时，不得用手动补录、家学或感气旁路预生成服气修士。
		if (definition.IsLaterFounded) return CanEnterLaterFoundedPath(definition.RootId);
		return IsDiscovered(definition.RootId);
	}

	internal static bool CanManifestZiJin(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.SupportsZiJin) return false;
		if (definition.IsCommonAncient) return true;
		// 长庚有自己的“无名剑道开位”权威，不依赖普通 discovered 标记。
		if (string.Equals(definition.ZiJinGateId, XjDaoTuGateIds.LongGengEstablished, StringComparison.Ordinal))
		{
			return XjFuQiSwordWorldState.IsEstablished;
		}
		// 其余后世新辟道途统一走实际入道门。对渊照而言，这会额外要求
		// 水月照真洞天与唯一先天之气源已经真实落地。
		if (definition.IsLaterFounded && !CanEnterLaterFoundedPath(definition.RootId)) return false;
		if (string.Equals(definition.ZiJinGateId, XjDaoTuGateIds.FuQiBeforeZiJin, StringComparison.Ordinal))
		{
			return IsFuQiManifested(definition.RootId);
		}
		return IsDiscovered(definition.RootId);
	}

	internal static bool MarkDiscovered(string daoTuOrRoot, long actorId, int currentYear)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| definition.IsCommonAncient || currentYear <= 0) return false;
		XjDaoTuManifestArchiveData record = GetOrCreate(definition.RootId);
		if (record.Discovered) return true;
		record.Discovered = true;
		record.DiscoveredYear = currentYear;
		record.FirstDiscovererActorId = Math.Max(0L, actorId);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool MarkFuQiManifested(string daoTuOrRoot, long actorId, int currentYear)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| !definition.SupportsFuQi || currentYear <= 0) return false;
		if (definition.IsCommonAncient) return true;
		XjDaoTuManifestArchiveData record = GetOrCreate(definition.RootId);
		if (!record.Discovered)
		{
			record.Discovered = true;
			record.DiscoveredYear = currentYear;
			record.FirstDiscovererActorId = Math.Max(0L, actorId);
		}
		if (!record.FuQiManifested)
		{
			record.FuQiManifested = true;
			record.FuQiManifestedYear = currentYear;
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool MarkZiJinManifested(string daoTuOrRoot, long actorId, int currentYear)
	{
		if (!CanManifestZiJin(daoTuOrRoot)
			|| !XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| currentYear <= 0) return false;
		if (definition.IsCommonAncient) return true;
		XjDaoTuManifestArchiveData record = GetOrCreate(definition.RootId);
		if (!record.Discovered)
		{
			record.Discovered = true;
			record.DiscoveredYear = currentYear;
			record.FirstDiscovererActorId = Math.Max(0L, actorId);
		}
		if (!record.ZiJinManifested)
		{
			record.ZiJinManifested = true;
			record.ZiJinManifestedYear = currentYear;
		}
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool MarkCaiQiUnlocked(string daoTuOrRoot, long actorId, int currentYear)
	{
		if (!XjDaoTuCatalog.TryResolve(daoTuOrRoot, out XjDaoTuDefinition definition)
			|| currentYear <= 0
			|| (!string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal)
				&& !string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
				&& !string.Equals(definition.RootId, XjDaoTuRootIds.YuanZhao, StringComparison.Ordinal)))
		{
			return false;
		}

		XjDaoTuManifestArchiveData record = GetOrCreate(definition.RootId);
		if (record.CaiQiUnlocked) return true;
		if (!record.Discovered)
		{
			record.Discovered = true;
			record.DiscoveredYear = currentYear;
			record.FirstDiscovererActorId = Math.Max(0L, actorId);
		}
		record.CaiQiUnlocked = true;
		record.CaiQiUnlockedYear = currentYear;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// 渊照采气必须由水月照真洞天担保。旧档若只有正位/显世记录而洞天缺失，
	/// 只回锁采气字段，不撤销已经公开的渊照道途与五百年空证史实。
	/// </summary>
	internal static bool LockYuanZhaoCaiQiUntilLegacyReady()
	{
		if (!Records.TryGetValue(XjDaoTuRootIds.YuanZhao, out XjDaoTuManifestArchiveData record)
			|| !record.CaiQiUnlocked) return false;
		record.CaiQiUnlocked = false;
		record.CaiQiUnlockedYear = 0;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static void RollbackFuQiManifestation(string daoTuOrRoot, long actorId, int currentYear)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId)
			|| !Records.TryGetValue(rootId, out XjDaoTuManifestArchiveData record)
			|| !record.FuQiManifested
			|| record.FuQiManifestedYear != currentYear
			|| record.FirstDiscovererActorId != actorId
			|| record.ZiJinManifested) return;
		Records.Remove(rootId);
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// RC8及更早把渊照按绝对1000年提前显化的旧档修复。仅由渊照时间轴迁移调用；
	/// 当前世界尚未到新的“起始年+500”节点时，撤去这条后世道途的提前显化。
	/// </summary>
	internal static bool ResetYuanZhaoTimelineForMigration()
	{
		if (!Records.Remove(XjDaoTuRootIds.YuanZhao)) return false;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static void ExportArchiveRecords(List<XjDaoTuManifestArchiveData> target)
	{
		target?.Clear();
		if (target == null) return;
		List<string> keys = new List<string>(Records.Keys);
		keys.Sort(StringComparer.Ordinal);
		for (int i = 0; i < keys.Count; i++) target.Add(Records[keys[i]].Clone());
	}

	internal static void ImportArchiveRecords(List<XjDaoTuManifestArchiveData> source)
	{
		Records.Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjDaoTuManifestArchiveData record = source[i];
			if (record == null
				|| !XjDaoTuCatalog.TryResolve(record.DaoTuRootId, out XjDaoTuDefinition definition)
				|| definition.IsCommonAncient) continue;
			record.DaoTuRootId = definition.RootId;
			Records[definition.RootId] = record.Clone();
		}
	}

	/// <summary>
	/// 0.9.4.20及更早存档没有独立采气解锁字段。只在读档阶段读取已有世界账本，
	/// 一次性回填已经真正成立的空证果位，不扫描角色或地图。
	/// </summary>
	internal static void ReconcileCaiQiUnlocksAfterLoad()
	{
		if (XjFuQiSwordWorldState.IsEstablished)
		{
			MarkCaiQiUnlocked(
				XjDaoTuRootIds.LongGeng,
				XjFuQiSwordWorldState.FounderActorId,
				Math.Max(1, XjFuQiSwordWorldState.EstablishedYear));
		}

		// 青宣旧档可以由已经正式存在的空证正位反推采气开放；渊照绝对不行。
		// 渊照只有“水月照真洞天 + 固定采气源”真实存在时才由事件收口解锁，
		// 否则仅凭渊照正位会制造一个永远找不到采气地点的死锁。
		if (IsCaiQiUnlocked(XjDaoTuRootIds.QingXuan)) return;

		IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadAllEntries();
		for (int i = 0; i < entries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = entries[i];
			if (!entry.Found
				|| string.IsNullOrWhiteSpace(entry.GuoWei)
				|| !entry.GuoWei.Trim().EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				continue;
			}

			string daoTu = (entry.DaoTu ?? string.Empty).Trim();
			if (!string.Equals(daoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)) continue;
			MarkCaiQiUnlocked(XjDaoTuRootIds.QingXuan, entry.ActorId, Math.Max(1, entry.Year));
			break;
		}
	}

	internal static void Clear() => Records.Clear();

	private static XjDaoTuManifestArchiveData GetOrCreate(string rootId)
	{
		if (!Records.TryGetValue(rootId, out XjDaoTuManifestArchiveData record))
		{
			record = new XjDaoTuManifestArchiveData { DaoTuRootId = rootId };
			Records[rootId] = record;
		}
		return record;
	}
}
