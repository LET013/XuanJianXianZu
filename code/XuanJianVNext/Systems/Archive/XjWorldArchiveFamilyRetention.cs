using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Archive;

/// <summary>
/// 保存前把上一次成功读入的家族历史与当前运行时快照合并。
/// 家族纪事、死亡记录、成员账本、身份索引和永久功法知识只增不减；
/// 重宝仓库在未发生显式增减时防丢，发生消费时则以运行时精确数量为准。
/// 运行时重建缺项不得覆盖已经落盘的历史事实。
/// </summary>
internal static class XjWorldArchiveFamilyRetention
{
	internal static void CaptureProtectedBaseline(
		XjWorldArchiveData source,
		XjWorldArchiveData target)
	{
		if (source == null || target == null) return;
		// Archive section exporters use replace-on-write DTO collections. Holding the
		// last successfully written protected collections by reference is therefore a
		// safe copy-on-write baseline: the next Family/Warehouse export installs new
		// lists while these old ones remain immutable retention input.
		target.FamilyChronicles = source.FamilyChronicles;
		target.FamilyDeathRecords = source.FamilyDeathRecords;
		target.FamilyMemberLedger = source.FamilyMemberLedger;
		target.FamilyIdentityIndex = source.FamilyIdentityIndex;
		target.FamilyGongFaWarehouse = source.FamilyGongFaWarehouse;
		target.FamilyQiuJinFaWarehouse = source.FamilyQiuJinFaWarehouse;
		target.FamilyCaiQiWarehouse = source.FamilyCaiQiWarehouse;
		target.FamilyFaBaoWarehouse = source.FamilyFaBaoWarehouse;
		target.FamilyLingWuWarehouse = source.FamilyLingWuWarehouse;
		target.LostFaBaoRecords = source.LostFaBaoRecords;
	}

	internal static XjWorldArchiveData MergeProtectedFamilyData(
		XjWorldArchiveData baseline,
		XjWorldArchiveData current)
	{
		XjWorldArchiveData target = current ?? new XjWorldArchiveData();
		if (baseline == null)
		{
			EnsureLists(target);
			return target;
		}

		EnsureLists(baseline);
		EnsureLists(target);
		MergeChronicles(baseline.FamilyChronicles, target.FamilyChronicles);
		MergeDeaths(baseline.FamilyDeathRecords, target.FamilyDeathRecords);
		MergeMemberLedger(baseline.FamilyMemberLedger, target.FamilyMemberLedger);
		MergeIdentityIndex(baseline.FamilyIdentityIndex, target.FamilyIdentityIndex);
		MergePermanentGongFa(baseline.FamilyGongFaWarehouse, target.FamilyGongFaWarehouse);
		MergePermanentGongFa(baseline.FamilyQiuJinFaWarehouse, target.FamilyQiuJinFaWarehouse);
		MergeCaiQi(baseline.FamilyCaiQiWarehouse, target.FamilyCaiQiWarehouse);
		MergePermanentFaBao(baseline.FamilyFaBaoWarehouse, target.FamilyFaBaoWarehouse);
		MergeLingWu(baseline.FamilyLingWuWarehouse, target.FamilyLingWuWarehouse);
		MergeLostFaBao(baseline.LostFaBaoRecords, target.LostFaBaoRecords);
		// 三书独立Store由主归档/备份归档整体回退保护，不能在这里做“只增不减”合并。
		// 否则容量裁剪掉的旧条目、已经补写完成的待定家族事实会在下一次保存时被旧快照复活。
		// 当前运行时导出的三书记录、事实账本与待补队列均视为权威状态。
		return target;
	}

	private static void EnsureLists(XjWorldArchiveData data)
	{
		data.FamilyChronicles ??= new List<XjWorldArchiveChronicleRecord>();
		data.PersonalBiographyRecords ??= new List<XjThreeBookArchiveRecord>();
		data.FamilyChronicleBookRecords ??= new List<XjThreeBookArchiveRecord>();
		data.SectChronicleRecords ??= new List<XjThreeBookArchiveRecord>();
		data.PersonalBiographySourceLedger ??= new List<XjThreeBookSourceFactRecord>();
		data.FamilyChronicleSourceLedger ??= new List<XjThreeBookSourceFactRecord>();
		data.SectChronicleSourceLedger ??= new List<XjThreeBookSourceFactRecord>();
		data.DeferredFamilyChronicleFacts ??= new List<XjThreeBookDeferredFamilyFactRecord>();
		data.FamilyDeathRecords ??= new List<XjWorldArchiveDeathRecord>();
		data.FamilyMemberLedger ??= new List<XjWorldArchiveFamilyMemberRecord>();
		data.FamilyIdentityIndex ??= new List<XjWorldArchiveFamilyIdentityRecord>();
		data.FamilyGongFaWarehouse ??= new List<XjWorldArchiveGongFaRecord>();
		data.FamilyQiuJinFaWarehouse ??= new List<XjWorldArchiveGongFaRecord>();
		data.FamilyCaiQiWarehouse ??= new List<XjWorldArchiveCaiQiRecord>();
		data.FamilyFaBaoWarehouse ??= new List<XjWorldArchiveFaBaoRecord>();
		data.FamilyLingWuWarehouse ??= new List<XjWorldArchiveLingWuRecord>();
		data.LostFaBaoRecords ??= new List<XjWorldArchiveFaBaoRecord>();
	}


	private static void MergeChronicles(
		IReadOnlyList<XjWorldArchiveChronicleRecord> baseline,
		List<XjWorldArchiveChronicleRecord> current)
	{
		Dictionary<string, XjWorldArchiveChronicleRecord> byKey = new Dictionary<string, XjWorldArchiveChronicleRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveChronicleRecord record = current[i];
			if (record != null) byKey[BuildChronicleKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveChronicleRecord historical = baseline[i];
			if (historical == null) continue;
			string key = BuildChronicleKey(historical);
			if (!byKey.TryGetValue(key, out XjWorldArchiveChronicleRecord live))
			{
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}

			if (live.FamilyStableId <= 0L) live.FamilyStableId = historical.FamilyStableId;
			if (live.ActorId <= 0L) live.ActorId = historical.ActorId;
			if (live.Year <= 0) live.Year = historical.Year;
			if (string.IsNullOrWhiteSpace(live.EventKey)) live.EventKey = historical.EventKey;
			if (string.IsNullOrWhiteSpace(live.EventType)) live.EventType = historical.EventType;
			if (string.IsNullOrWhiteSpace(live.Text)) live.Text = historical.Text;
			if (string.IsNullOrWhiteSpace(live.Title)) live.Title = historical.Title;
			if (string.IsNullOrWhiteSpace(live.Body)) live.Body = historical.Body;
			if (string.IsNullOrWhiteSpace(live.Source)) live.Source = historical.Source;
			if (string.IsNullOrWhiteSpace(live.ActorRealmSnapshot)) live.ActorRealmSnapshot = historical.ActorRealmSnapshot;
			live.Importance = Math.Max(live.Importance, historical.Importance);
			live.IsProtected |= historical.IsProtected;
			live.RelatedToFamilyWarehouse |= historical.RelatedToFamilyWarehouse;
			live.RelatedToHighGradeGongFa |= historical.RelatedToHighGradeGongFa;
		}
	}

	private static void MergeDeaths(
		IReadOnlyList<XjWorldArchiveDeathRecord> baseline,
		List<XjWorldArchiveDeathRecord> current)
	{
		Dictionary<string, XjWorldArchiveDeathRecord> byKey = new Dictionary<string, XjWorldArchiveDeathRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveDeathRecord record = current[i];
			if (record != null) byKey[BuildDeathKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveDeathRecord historical = baseline[i];
			if (historical == null) continue;
			string key = BuildDeathKey(historical);
			if (!byKey.TryGetValue(key, out XjWorldArchiveDeathRecord live))
			{
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}

			if (live.FamilyStableId <= 0L) live.FamilyStableId = historical.FamilyStableId;
			if (live.ActorId <= 0L) live.ActorId = historical.ActorId;
			if (live.Year <= 0) live.Year = historical.Year;
			if (string.IsNullOrWhiteSpace(live.Name)) live.Name = historical.Name;
			if (string.IsNullOrWhiteSpace(live.Realm)) live.Realm = historical.Realm;
			if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
			if (string.IsNullOrWhiteSpace(live.GongFaName)) live.GongFaName = historical.GongFaName;
			if (live.GongFaGrade <= 0) live.GongFaGrade = historical.GongFaGrade;
			if (string.IsNullOrWhiteSpace(live.QiuJinFaName)) live.QiuJinFaName = historical.QiuJinFaName;
			if (string.IsNullOrWhiteSpace(live.JinXing)) live.JinXing = historical.JinXing;
			if (string.IsNullOrWhiteSpace(live.GuoWei)) live.GuoWei = historical.GuoWei;
		}
	}

	private static void MergeMemberLedger(
		IReadOnlyList<XjWorldArchiveFamilyMemberRecord> baseline,
		List<XjWorldArchiveFamilyMemberRecord> current)
	{
		Dictionary<long, XjWorldArchiveFamilyMemberRecord> byActor = new Dictionary<long, XjWorldArchiveFamilyMemberRecord>();
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveFamilyMemberRecord record = current[i];
			if (record != null && record.ActorId > 0L) byActor[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveFamilyMemberRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!byActor.TryGetValue(historical.ActorId, out XjWorldArchiveFamilyMemberRecord live))
			{
				current.Add(historical);
				byActor[historical.ActorId] = historical;
				continue;
			}

			bool explicitRelink = XjFamilyMemberLedger.IsExplicitFamilyRelinkSource(live.Source);
			if (!explicitRelink && historical.FamilyStableId > 0L)
			{
				live.FamilyStableId = historical.FamilyStableId;
			}
			if (live.Generation <= 0) live.Generation = historical.Generation;
			if (string.IsNullOrWhiteSpace(live.Name)) live.Name = historical.Name;
			if (string.IsNullOrWhiteSpace(live.RealmId)) live.RealmId = historical.RealmId;
			if (string.IsNullOrWhiteSpace(live.RealmDisplay)) live.RealmDisplay = historical.RealmDisplay;
			if (live.BirthYear <= 0) live.BirthYear = historical.BirthYear;
			if (live.DeathYear <= 0) live.DeathYear = historical.DeathYear;
			if (string.IsNullOrWhiteSpace(live.Source)) live.Source = historical.Source;
		}
	}

	private static void MergeIdentityIndex(
		IReadOnlyList<XjWorldArchiveFamilyIdentityRecord> baseline,
		List<XjWorldArchiveFamilyIdentityRecord> current)
	{
		Dictionary<long, XjWorldArchiveFamilyIdentityRecord> byActor = new Dictionary<long, XjWorldArchiveFamilyIdentityRecord>();
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveFamilyIdentityRecord record = current[i];
			if (record != null && record.ActorId > 0L) byActor[record.ActorId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveFamilyIdentityRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L) continue;
			if (!byActor.TryGetValue(historical.ActorId, out XjWorldArchiveFamilyIdentityRecord live))
			{
				current.Add(historical);
				byActor[historical.ActorId] = historical;
				continue;
			}

			bool explicitRelink = string.Equals(live.ReasonCode, "ReincarnationLink", StringComparison.Ordinal)
				|| string.Equals(live.ReasonCode, XjFamilyIdentityReasons.CityMigrationBranch, StringComparison.Ordinal);
			if (!explicitRelink)
			{
				if (!string.IsNullOrWhiteSpace(historical.FamilyKey)) live.FamilyKey = historical.FamilyKey;
				if (historical.RootActorId > 0L) live.RootActorId = historical.RootActorId;
			}
			if (string.IsNullOrWhiteSpace(live.ReasonCode)) live.ReasonCode = historical.ReasonCode;
			live.IsMale |= historical.IsMale;
		}
	}

	private static void MergePermanentGongFa(
		IReadOnlyList<XjWorldArchiveGongFaRecord> baseline,
		List<XjWorldArchiveGongFaRecord> current)
	{
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveGongFaRecord record = current[i];
			if (record != null) keys.Add(BuildGongFaKey(record));
		}
		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveGongFaRecord historical = baseline[i];
			if (historical == null) continue;
			if (keys.Add(BuildGongFaKey(historical))) current.Add(historical);
		}
	}

	private static void MergeCaiQi(
		IReadOnlyList<XjWorldArchiveCaiQiRecord> baseline,
		List<XjWorldArchiveCaiQiRecord> current)
	{
		Dictionary<string, XjWorldArchiveCaiQiRecord> byKey = new Dictionary<string, XjWorldArchiveCaiQiRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveCaiQiRecord record = current[i];
			if (record != null && !string.IsNullOrWhiteSpace(record.FamilyKey) && !string.IsNullOrWhiteSpace(record.ResourceName))
				byKey[BuildCaiQiKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveCaiQiRecord historical = baseline[i];
			if (historical == null || string.IsNullOrWhiteSpace(historical.FamilyKey) || string.IsNullOrWhiteSpace(historical.ResourceName)) continue;
			string resourceType = NormalizeCaiQiResourceType(historical.ResourceType);
			string key = BuildCaiQiKey(historical);
			bool authoritativeMutation = XjFamilyCaiQiWarehouse.WasMutatedSinceImport(
				historical.FamilyKey, resourceType, historical.ResourceName);
			if (!byKey.TryGetValue(key, out XjWorldArchiveCaiQiRecord live))
			{
				// 本会话已明确消耗到零时，不得从上一版归档复活。
				if (authoritativeMutation) continue;
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}

			if (!authoritativeMutation)
			{
				live.Count = Math.Max(live.Count, historical.Count);
			}
			if (string.IsNullOrWhiteSpace(live.ResourceType)) live.ResourceType = resourceType;
		}
	}

	private static void MergePermanentFaBao(
		IReadOnlyList<XjWorldArchiveFaBaoRecord> baseline,
		List<XjWorldArchiveFaBaoRecord> current)
	{
		Dictionary<string, XjWorldArchiveFaBaoRecord> byKey = new Dictionary<string, XjWorldArchiveFaBaoRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = current[i];
			if (record != null
				&& (record.FamilyStableId > 0L || record.SectId > 0L)
				&& !string.IsNullOrWhiteSpace(record.FaBaoId))
				byKey[BuildFamilyFaBaoKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveFaBaoRecord historical = baseline[i];
			if (historical == null
				|| (historical.FamilyStableId <= 0L && historical.SectId <= 0L)
				|| string.IsNullOrWhiteSpace(historical.FaBaoId)) continue;
			string key = BuildFamilyFaBaoKey(historical);
			bool authoritativeMutation = XjFamilyFaBaoWarehouse.WasOwnedEntryMutatedSinceImport(
				historical.FamilyStableId, historical.SectId, historical.FaBaoId, historical.DaoTu);
			if (!byKey.TryGetValue(key, out XjWorldArchiveFaBaoRecord live))
			{
				// 本次会话已将器物转交宗门或散佚时，缺失即为权威状态，不能由保护层复活旧所有权。
				if (authoritativeMutation) continue;
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}
			FillFaBaoBlanks(live, historical);
		}
	}

	private static void MergeLostFaBao(
		IReadOnlyList<XjWorldArchiveFaBaoRecord> baseline,
		List<XjWorldArchiveFaBaoRecord> current)
	{
		Dictionary<string, XjWorldArchiveFaBaoRecord> byKey = new Dictionary<string, XjWorldArchiveFaBaoRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveFaBaoRecord record = current[i];
			if (record != null && record.ActorId > 0L && !string.IsNullOrWhiteSpace(record.FaBaoId))
				byKey[BuildLostFaBaoKey(record)] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveFaBaoRecord historical = baseline[i];
			if (historical == null || historical.ActorId <= 0L || string.IsNullOrWhiteSpace(historical.FaBaoId)) continue;
			string key = BuildLostFaBaoKey(historical);
			bool authoritativeMutation = XjFamilyFaBaoWarehouse.WasLostEntryMutatedSinceImport(
				historical.ActorId, historical.FaBaoId, historical.DaoTu);
			if (!byKey.TryGetValue(key, out XjWorldArchiveFaBaoRecord live))
			{
				// 已被发现并转入家族仓库的遗失法宝不能在保存时重新回到遗失池。
				if (authoritativeMutation) continue;
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}
			FillFaBaoBlanks(live, historical);
		}
	}

	private static void FillFaBaoBlanks(XjWorldArchiveFaBaoRecord live, XjWorldArchiveFaBaoRecord historical)
	{
		if (live.FamilyStableId <= 0L) live.FamilyStableId = historical.FamilyStableId;
		if (live.SectId <= 0L) live.SectId = historical.SectId;
		if (string.IsNullOrWhiteSpace(live.SectName)) live.SectName = historical.SectName;
		if (live.ActorId <= 0L) live.ActorId = historical.ActorId;
		if (string.IsNullOrWhiteSpace(live.ActorName)) live.ActorName = historical.ActorName;
		if (string.IsNullOrWhiteSpace(live.FaBaoId)) live.FaBaoId = historical.FaBaoId;
		if (string.IsNullOrWhiteSpace(live.FaBaoName)) live.FaBaoName = historical.FaBaoName;
		if (string.IsNullOrWhiteSpace(live.DaoTu)) live.DaoTu = historical.DaoTu;
		if (string.IsNullOrWhiteSpace(live.ClassName)) live.ClassName = historical.ClassName;
		if (string.IsNullOrWhiteSpace(live.Source)) live.Source = historical.Source;
		if (live.Year <= 0) live.Year = historical.Year;
	}

	private static void MergeLingWu(
		IReadOnlyList<XjWorldArchiveLingWuRecord> baseline,
		List<XjWorldArchiveLingWuRecord> current)
	{
		Dictionary<string, XjWorldArchiveLingWuRecord> byKey = new Dictionary<string, XjWorldArchiveLingWuRecord>(StringComparer.Ordinal);
		for (int i = 0; i < current.Count; i++)
		{
			XjWorldArchiveLingWuRecord record = current[i];
			if (record != null && record.FamilyStableId > 0L && !string.IsNullOrWhiteSpace(record.LingWuId))
				byKey[record.FamilyStableId + "|" + record.LingWuId] = record;
		}

		for (int i = 0; i < baseline.Count; i++)
		{
			XjWorldArchiveLingWuRecord historical = baseline[i];
			if (historical == null || historical.FamilyStableId <= 0L || string.IsNullOrWhiteSpace(historical.LingWuId)) continue;
			string key = historical.FamilyStableId + "|" + historical.LingWuId;
			bool authoritativeMutation = XjFamilyLingWuWarehouse.WasMutatedSinceImport(
				historical.FamilyStableId, historical.LingWuId);
			if (!byKey.TryGetValue(key, out XjWorldArchiveLingWuRecord live))
			{
				// 本次会话已把该条目明确消耗到零时，当前列表中不存在正是权威状态。
				if (authoritativeMutation) continue;
				current.Add(historical);
				byKey[key] = historical;
				continue;
			}

			// 有显式增减记录时保留当前精确数量；只有未触碰条目才用旧归档兜底防丢。
			if (!authoritativeMutation)
			{
				live.Count = Math.Max(live.Count, historical.Count);
			}
			if (live.FirstAcquiredYear <= 0 || (historical.FirstAcquiredYear > 0 && historical.FirstAcquiredYear < live.FirstAcquiredYear))
				live.FirstAcquiredYear = historical.FirstAcquiredYear;
			if (historical.LastAcquiredYear > live.LastAcquiredYear)
			{
				live.LastAcquiredYear = historical.LastAcquiredYear;
				live.LastSourceActorId = historical.LastSourceActorId;
				live.LastSourceActorName = historical.LastSourceActorName;
			}
		}
	}


	private static string BuildCaiQiKey(XjWorldArchiveCaiQiRecord record)
	{
		return (record.FamilyKey ?? string.Empty).Trim() + "|"
			+ NormalizeCaiQiResourceType(record.ResourceType) + "|"
			+ (record.ResourceName ?? string.Empty).Trim();
	}

	private static string NormalizeCaiQiResourceType(string resourceType)
	{
		return string.IsNullOrWhiteSpace(resourceType)
			? XjFamilyCaiQiWarehouse.ResourceTypeCaiQi
			: resourceType.Trim();
	}

	private static string BuildFamilyFaBaoKey(XjWorldArchiveFaBaoRecord record)
	{
		string owner = record.FamilyStableId > 0L
			? "family:" + record.FamilyStableId
			: "sect:" + record.SectId;
		return owner + "|" + (record.FaBaoId ?? string.Empty).Trim() + "|" + (record.DaoTu ?? string.Empty).Trim();
	}

	private static string BuildLostFaBaoKey(XjWorldArchiveFaBaoRecord record)
	{
		return record.ActorId + "|" + (record.FaBaoId ?? string.Empty).Trim() + "|" + (record.DaoTu ?? string.Empty).Trim();
	}

	private static string BuildChronicleKey(XjWorldArchiveChronicleRecord record)
	{
		if (!string.IsNullOrWhiteSpace(record.EventKey)) return record.EventKey.Trim();
		return record.FamilyStableId + "|" + record.ActorId + "|" + record.EventType + "|" + record.Year + "|" + record.Title + "|" + record.Body;
	}

	private static string BuildDeathKey(XjWorldArchiveDeathRecord record)
	{
		return record.FamilyStableId + "|" + record.ActorId + "|" + record.Year + "|" + record.Name;
	}

	private static string BuildGongFaKey(XjWorldArchiveGongFaRecord record)
	{
		return record.FamilyStableId + "|" + record.Name + "|" + record.Grade + "|" + record.DaoTu + "|" + record.SourceType;
	}
}
