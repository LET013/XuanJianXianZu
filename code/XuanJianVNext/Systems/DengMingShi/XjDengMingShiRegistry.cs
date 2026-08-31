using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiRegistry
{
	private static readonly Dictionary<string, XjDengMingShiRecord> recordsById =
		new Dictionary<string, XjDengMingShiRecord>(StringComparer.Ordinal);

	internal static bool Add(XjDengMingShiRecord record)
	{
		if (record == null || string.IsNullOrWhiteSpace(record.RecordId))
		{
			return false;
		}

		recordsById[record.RecordId.Trim()] = Clone(record);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool Remove(string recordId)
	{
		string safeId = Normalize(recordId);
		if (string.IsNullOrEmpty(safeId) || !recordsById.Remove(safeId))
		{
			return false;
		}

		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool TryGet(string recordId, out XjDengMingShiRecord record)
	{
		record = null;
		string safeId = Normalize(recordId);
		if (string.IsNullOrEmpty(safeId) || !recordsById.TryGetValue(safeId, out XjDengMingShiRecord found))
		{
			return false;
		}

		record = Clone(found);
		return true;
	}

	internal static bool MarkPlaced(string recordId, int year)
	{
		string safeId = Normalize(recordId);
		if (string.IsNullOrEmpty(safeId) || !recordsById.TryGetValue(safeId, out XjDengMingShiRecord found))
		{
			return false;
		}

		found.PlacedCount = Math.Max(0, found.PlacedCount) + 1;
		found.LastPlacedYear = Math.Max(0, year);
		recordsById[safeId] = Clone(found);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// 检查角色是否已被保存（通过 SourceActorId 匹配）。
	/// </summary>
	internal static bool IsActorSavedBySourceId(long sourceActorId)
	{
		if (sourceActorId <= 0L)
		{
			return false;
		}

		foreach (XjDengMingShiRecord record in recordsById.Values)
		{
			if (record != null && record.SourceActorId == sourceActorId)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// 按 SourceActorId 删除已保存的记录。
	/// </summary>
	internal static bool RemoveBySourceActorId(long sourceActorId)
	{
		if (sourceActorId <= 0L)
		{
			return false;
		}

		string foundId = null;
		foreach (KeyValuePair<string, XjDengMingShiRecord> entry in recordsById)
		{
			if (entry.Value != null && entry.Value.SourceActorId == sourceActorId)
			{
				foundId = entry.Key;
				break;
			}
		}

		if (foundId == null || !recordsById.Remove(foundId))
		{
			return false;
		}

		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static IReadOnlyList<XjDengMingShiRecord> ReadAll()
	{
		if (recordsById.Count == 0)
		{
			return Array.Empty<XjDengMingShiRecord>();
		}

		List<XjDengMingShiRecord> records = new List<XjDengMingShiRecord>(recordsById.Count);
		foreach (XjDengMingShiRecord record in recordsById.Values)
		{
			records.Add(Clone(record));
		}

		records.Sort((left, right) =>
		{
			int byYear = right.SavedYear.CompareTo(left.SavedYear);
			if (byYear != 0)
			{
				return byYear;
			}

			int name = string.Compare(left.ActorName, right.ActorName, StringComparison.Ordinal);
			if (name != 0) return name;
			int actor = left.SourceActorId.CompareTo(right.SourceActorId);
			return actor != 0 ? actor : string.Compare(left.RecordId, right.RecordId, StringComparison.Ordinal);
		});
		return records;
	}

	internal static void ExportArchiveRecords(List<XjDengMingShiArchiveData> archiveRecords)
	{
		if (archiveRecords == null)
		{
			return;
		}

		foreach (XjDengMingShiRecord record in recordsById.Values)
		{
			if (record == null || string.IsNullOrWhiteSpace(record.RecordId))
			{
				continue;
			}

			archiveRecords.Add(ToArchive(record));
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjDengMingShiArchiveData> archiveRecords)
	{
		recordsById.Clear();
		if (archiveRecords == null)
		{
			return;
		}

		foreach (XjDengMingShiArchiveData archiveRecord in archiveRecords)
		{
			XjDengMingShiRecord record = FromArchive(archiveRecord);
			if (record == null || string.IsNullOrWhiteSpace(record.RecordId))
			{
				continue;
			}

			recordsById[record.RecordId] = record;
		}
	}

	internal static void Clear()
	{
		recordsById.Clear();
	}

	private static XjDengMingShiArchiveData ToArchive(XjDengMingShiRecord record)
	{
		return new XjDengMingShiArchiveData
		{
			SchemaVersion = Math.Max(3, record.SchemaVersion),
			CultivationPath = record.CultivationPath,
			HighRealmPayloadKind = record.HighRealmPayloadKind,
			HighRealmDaoStateJson = record.HighRealmDaoStateJson,
			FuQiState = XjDengMingShiFuQiSnapshotCodec.Clone(record.FuQiState),
			RecordId = record.RecordId,
			ActorName = record.ActorName,
			SourceActorId = record.SourceActorId,
			SavedYear = record.SavedYear,
			RaceId = record.RaceId,
			KingdomId = record.KingdomId,
			CultureId = record.CultureId,
			TraitIds = new List<string>(record.TraitIds ?? new List<string>()),
			Realm = record.Realm,
			RealmId = record.RealmId,
			DaoTu = record.DaoTu,
			XjZz = record.XjZz,
			ZhenYuan = Math.Max(0f, record.ZhenYuan),
			GongFaName = record.GongFaName,
			GongFaGrade = record.GongFaGrade,
			GongFaStage = 0,
			GongFaProgress = 0f,
			GongFaDaoTu = record.GongFaDaoTu,
			GongFaCollectionVersion = record.GongFaCollectionVersion,
			GongFaCollectionJson = record.GongFaCollectionJson,
			XianJiIds = record.XianJiIds,
			XianJiLastYear = record.XianJiLastYear,
			CaiQiFaName = record.CaiQiFaName,
			CaiQiFaDaoTu = record.CaiQiFaDaoTu,
			CaiQiFaSourcePlace = record.CaiQiFaSourcePlace,
			CaiQiFaSourceYear = record.CaiQiFaSourceYear,
			QiuJinFa = record.QiuJinFa,
			QiuJinFaSourceGongFaName = record.QiuJinFaSourceGongFaName,
			QiuJinFaSourceGongFaGrade = record.QiuJinFaSourceGongFaGrade,
			QiuJinFaSourceDaoTu = record.QiuJinFaSourceDaoTu,
			QiuJinFaLastYear = record.QiuJinFaLastYear,
			QiuJinFaBoundAuthority = record.QiuJinFaBoundAuthority,
			JinDan = record.JinDan,
			GuoWei = record.GuoWei,
			JinDanSuccessYear = record.JinDanSuccessYear,
			ShenDanGuoWei = record.ShenDanGuoWei,
			ShenDanAnchorActorId = record.ShenDanAnchorActorId,
			ShenDanAnchorName = record.ShenDanAnchorName,
			ShenDanYear = record.ShenDanYear,
			FaBaoSummary = record.FaBaoSummary,
			FaBaoId = record.FaBaoId,
			FaBaoName = record.FaBaoName,
			FaBaoDaoTu = record.FaBaoDaoTu,
			FaBaoClass = record.FaBaoClass,
			FaBaoSource = record.FaBaoSource,
			FaBaoYear = record.FaBaoYear,
			FamilyStableId = record.FamilyStableId,
			FamilyName = record.FamilyName,
			ZongMenId = record.ZongMenId,
			ZongMenName = record.ZongMenName,
			PlacedCount = record.PlacedCount,
			LastPlacedYear = record.LastPlacedYear
		};
	}

	private static XjDengMingShiRecord FromArchive(XjDengMingShiArchiveData archiveRecord)
	{
		if (archiveRecord == null)
		{
			return null;
		}

		return new XjDengMingShiRecord
		{
			SchemaVersion = archiveRecord.SchemaVersion <= 0 ? 1 : archiveRecord.SchemaVersion,
			CultivationPath = archiveRecord.CultivationPath ?? string.Empty,
			HighRealmPayloadKind = archiveRecord.HighRealmPayloadKind ?? string.Empty,
			HighRealmDaoStateJson = archiveRecord.HighRealmDaoStateJson ?? string.Empty,
			FuQiState = XjDengMingShiFuQiSnapshotCodec.Clone(archiveRecord.FuQiState),
			RecordId = Normalize(archiveRecord.RecordId),
			ActorName = archiveRecord.ActorName ?? string.Empty,
			SourceActorId = archiveRecord.SourceActorId,
			SavedYear = Math.Max(0, archiveRecord.SavedYear),
			RaceId = archiveRecord.RaceId ?? string.Empty,
			KingdomId = archiveRecord.KingdomId ?? string.Empty,
			CultureId = archiveRecord.CultureId ?? string.Empty,
			TraitIds = new List<string>(archiveRecord.TraitIds ?? new List<string>()),
			Realm = archiveRecord.Realm ?? string.Empty,
			RealmId = archiveRecord.RealmId ?? string.Empty,
			DaoTu = archiveRecord.DaoTu ?? string.Empty,
			XjZz = Math.Max(0, archiveRecord.XjZz),
			ZhenYuan = Math.Max(0f, archiveRecord.ZhenYuan),
			GongFaName = archiveRecord.GongFaName ?? string.Empty,
			GongFaGrade = Math.Max(0, archiveRecord.GongFaGrade),
			GongFaStage = 0,
			GongFaProgress = 0f,
			GongFaDaoTu = archiveRecord.GongFaDaoTu ?? string.Empty,
			GongFaCollectionVersion = Math.Max(0, archiveRecord.GongFaCollectionVersion),
			GongFaCollectionJson = archiveRecord.GongFaCollectionJson ?? string.Empty,
			XianJiIds = archiveRecord.XianJiIds ?? string.Empty,
			XianJiLastYear = Math.Max(0, archiveRecord.XianJiLastYear),
			CaiQiFaName = archiveRecord.CaiQiFaName ?? string.Empty,
			CaiQiFaDaoTu = archiveRecord.CaiQiFaDaoTu ?? string.Empty,
			CaiQiFaSourcePlace = archiveRecord.CaiQiFaSourcePlace ?? string.Empty,
			CaiQiFaSourceYear = Math.Max(0, archiveRecord.CaiQiFaSourceYear),
			QiuJinFa = archiveRecord.QiuJinFa ?? string.Empty,
			QiuJinFaSourceGongFaName = archiveRecord.QiuJinFaSourceGongFaName ?? string.Empty,
			QiuJinFaSourceGongFaGrade = Math.Max(0, archiveRecord.QiuJinFaSourceGongFaGrade),
			QiuJinFaSourceDaoTu = archiveRecord.QiuJinFaSourceDaoTu ?? string.Empty,
			QiuJinFaLastYear = Math.Max(0, archiveRecord.QiuJinFaLastYear),
			QiuJinFaBoundAuthority = archiveRecord.QiuJinFaBoundAuthority ?? string.Empty,
			JinDan = archiveRecord.JinDan ?? string.Empty,
			GuoWei = archiveRecord.GuoWei ?? string.Empty,
			JinDanSuccessYear = Math.Max(0, archiveRecord.JinDanSuccessYear),
			ShenDanGuoWei = archiveRecord.ShenDanGuoWei ?? string.Empty,
			ShenDanAnchorActorId = Math.Max(0L, archiveRecord.ShenDanAnchorActorId),
			ShenDanAnchorName = archiveRecord.ShenDanAnchorName ?? string.Empty,
			ShenDanYear = Math.Max(0, archiveRecord.ShenDanYear),
			FaBaoSummary = archiveRecord.FaBaoSummary ?? string.Empty,
			FaBaoId = archiveRecord.FaBaoId ?? string.Empty,
			FaBaoName = archiveRecord.FaBaoName ?? string.Empty,
			FaBaoDaoTu = archiveRecord.FaBaoDaoTu ?? string.Empty,
			FaBaoClass = archiveRecord.FaBaoClass ?? string.Empty,
			FaBaoSource = archiveRecord.FaBaoSource ?? string.Empty,
			FaBaoYear = Math.Max(0, archiveRecord.FaBaoYear),
			FamilyStableId = Math.Max(0L, archiveRecord.FamilyStableId),
			FamilyName = archiveRecord.FamilyName ?? string.Empty,
			ZongMenId = Math.Max(0L, archiveRecord.ZongMenId),
			ZongMenName = archiveRecord.ZongMenName ?? string.Empty,
			PlacedCount = Math.Max(0, archiveRecord.PlacedCount),
			LastPlacedYear = Math.Max(0, archiveRecord.LastPlacedYear)
		};
	}

	private static XjDengMingShiRecord Clone(XjDengMingShiRecord record)
	{
		return record == null ? null : FromArchive(ToArchive(record));
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
