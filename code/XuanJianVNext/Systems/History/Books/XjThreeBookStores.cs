using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.History.Books;

internal static class XjPersonalBiographyStore
{
	// 三书是玩家阅读用的史册，不是无限期事件数据库。事实去重仍由 source ledger
	// 负责；显示卷只保留最近和关键转折，避免长档人物过程纪事反过来压垮模拟。
	private static readonly XjThreeBookStoreCore Store = new XjThreeBookStoreCore(10000, "personal", 4, 1);
	internal static int Count => Store.Count;
	internal static int Capacity => Store.Capacity;
	internal static bool Record(XjThreeBookArchiveRecord record) => Store.Record(record);
	internal static bool ContainsSourceFact(string sourceFactId) => Store.ContainsSourceFact(sourceFactId);
	internal static bool TryGetBySourceFact(string sourceFactId, out XjThreeBookArchiveRecord record) => Store.TryGetBySourceFact(sourceFactId, out record);
	internal static int CountEvents(long subjectId, string eventType, int maximum = int.MaxValue) => Store.CountEvents(subjectId, eventType, maximum);
	internal static IReadOnlyList<XjThreeBookArchiveRecord> ReadSnapshot(int maximumEntries = 0) => Store.ReadSnapshot(maximumEntries);
	internal static void ExportArchiveRecords(List<XjThreeBookArchiveRecord> target) => Store.Export(target);
	internal static void ExportSourceLedger(List<XjThreeBookSourceFactRecord> target) => Store.ExportLedger(target);
	internal static void ImportArchiveRecords(IReadOnlyList<XjThreeBookArchiveRecord> source, IReadOnlyList<XjThreeBookSourceFactRecord> ledger = null) => Store.Import(source, ledger);
	internal static XjThreeBookStoreMetrics ReadMetrics() => Store.ReadMetrics();
	internal static bool Validate(out string message) => Store.Validate(out message);
	internal static void Clear() => Store.Clear();
	internal static void ReleaseSnapshotCache() => Store.ReleaseSnapshotCache();
	internal static void CompactMemory(bool rebuildStorage) => Store.CompactMemory(rebuildStorage);
}

internal static class XjFamilyChronicleBookStore
{
	private static readonly XjThreeBookStoreCore Store = new XjThreeBookStoreCore(6000, "family", 3, 1);
	internal static int Count => Store.Count;
	internal static int Capacity => Store.Capacity;
	internal static bool Record(XjThreeBookArchiveRecord record) => Store.Record(record);
	internal static bool ContainsSourceFact(string sourceFactId) => Store.ContainsSourceFact(sourceFactId);
	internal static bool TryGetBySourceFact(string sourceFactId, out XjThreeBookArchiveRecord record) => Store.TryGetBySourceFact(sourceFactId, out record);
	internal static int CountEvents(long subjectId, string eventType, int maximum = int.MaxValue) => Store.CountEvents(subjectId, eventType, maximum);
	internal static IReadOnlyList<XjThreeBookArchiveRecord> ReadSnapshot(int maximumEntries = 0) => Store.ReadSnapshot(maximumEntries);
	internal static void ExportArchiveRecords(List<XjThreeBookArchiveRecord> target) => Store.Export(target);
	internal static void ExportSourceLedger(List<XjThreeBookSourceFactRecord> target) => Store.ExportLedger(target);
	internal static void ImportArchiveRecords(IReadOnlyList<XjThreeBookArchiveRecord> source, IReadOnlyList<XjThreeBookSourceFactRecord> ledger = null) => Store.Import(source, ledger);
	internal static XjThreeBookStoreMetrics ReadMetrics() => Store.ReadMetrics();
	internal static bool Validate(out string message) => Store.Validate(out message);
	internal static void Clear() => Store.Clear();
	internal static void ReleaseSnapshotCache() => Store.ReleaseSnapshotCache();
	internal static void CompactMemory(bool rebuildStorage) => Store.CompactMemory(rebuildStorage);
}

internal static class XjSectChronicleStore
{
	private static readonly XjThreeBookStoreCore Store = new XjThreeBookStoreCore(4000, "sect", 3, 1);
	internal static int Count => Store.Count;
	internal static int Capacity => Store.Capacity;
	internal static bool Record(XjThreeBookArchiveRecord record) => Store.Record(record);
	internal static bool ContainsSourceFact(string sourceFactId) => Store.ContainsSourceFact(sourceFactId);
	internal static bool TryGetBySourceFact(string sourceFactId, out XjThreeBookArchiveRecord record) => Store.TryGetBySourceFact(sourceFactId, out record);
	internal static int CountEvents(long subjectId, string eventType, int maximum = int.MaxValue) => Store.CountEvents(subjectId, eventType, maximum);
	internal static IReadOnlyList<XjThreeBookArchiveRecord> ReadSnapshot(int maximumEntries = 0) => Store.ReadSnapshot(maximumEntries);
	internal static void ExportArchiveRecords(List<XjThreeBookArchiveRecord> target) => Store.Export(target);
	internal static void ExportSourceLedger(List<XjThreeBookSourceFactRecord> target) => Store.ExportLedger(target);
	internal static void ImportArchiveRecords(IReadOnlyList<XjThreeBookArchiveRecord> source, IReadOnlyList<XjThreeBookSourceFactRecord> ledger = null) => Store.Import(source, ledger);
	internal static XjThreeBookStoreMetrics ReadMetrics() => Store.ReadMetrics();
	internal static bool Validate(out string message) => Store.Validate(out message);
	internal static void Clear() => Store.Clear();
	internal static void ReleaseSnapshotCache() => Store.ReleaseSnapshotCache();
	internal static void CompactMemory(bool rebuildStorage) => Store.CompactMemory(rebuildStorage);
}

internal sealed class XjThreeBookStoreMetrics
{
	internal string Name = string.Empty;
	internal int Count;
	internal int Capacity;
	internal int LedgerCount;
	internal int LedgerCapacity;
	internal int EventTypeCount;
	internal int ExpectedEventTypeCount;
	internal string EventTypes = string.Empty;
	internal string MissingEventTypes = string.Empty;
	internal int LastEventYear;
	internal long Attempts;
	internal long Accepted;
	internal long Invalid;
	internal long DuplicateSource;
	internal long DuplicateSignature;
	internal long Trimmed;
	internal long Imported;
	internal long LedgerPruned;
	internal long SoftOverflow;
}

internal sealed class XjThreeBookStoreCore
{
	private static readonly string[] PersonalExpectedEventTypes =
	{
		XjThreeBookEventTypes.PersonalBirth, XjThreeBookEventTypes.PersonalAptitude, XjThreeBookEventTypes.PersonalHalfYaoBloodline, XjThreeBookEventTypes.PersonalCultivationQualified,
		XjThreeBookEventTypes.PersonalRealmBreakthrough, XjThreeBookEventTypes.PersonalDongTianJourney, XjThreeBookEventTypes.PersonalDongTianDeath,
		XjThreeBookEventTypes.PersonalGongFaObtained, XjThreeBookEventTypes.PersonalQiuJinFa, XjThreeBookEventTypes.PersonalFaBaoObtained,
		XjThreeBookEventTypes.PersonalMentorAccepted, XjThreeBookEventTypes.PersonalStudentAccepted, XjThreeBookEventTypes.PersonalSectFounded,
		XjThreeBookEventTypes.PersonalWeaponArt, XjThreeBookEventTypes.PersonalSectTournament, XjThreeBookEventTypes.PersonalDeath,
		XjThreeBookEventTypes.PersonalAcquaintance, XjThreeBookEventTypes.PersonalCloseFriend, XjThreeBookEventTypes.PersonalDaoCompanion,
		XjThreeBookEventTypes.PersonalRareCraft, XjThreeBookEventTypes.PersonalShenTongComprehended,
		XjThreeBookEventTypes.PersonalJieLinSucceeded, XjThreeBookEventTypes.PersonalYuYiSucceeded, XjThreeBookEventTypes.PersonalJinDanReincarnation,
		XjThreeBookEventTypes.PersonalJinDanDaoObstructed, XjThreeBookEventTypes.PersonalJinDanDaoObstructor,
		XjThreeBookEventTypes.PersonalCraftAbility, XjThreeBookEventTypes.PersonalAlchemyRecipe,
		XjThreeBookEventTypes.PersonalLingWuObtained,
		XjThreeBookEventTypes.PersonalFuQiCultivationPhase, XjThreeBookEventTypes.PersonalFuQiIntentStudy,
		XjThreeBookEventTypes.PersonalFuQiHuangGuan,
		XjThreeBookEventTypes.PersonalFuQiZhenRen, XjThreeBookEventTypes.PersonalFuQiShenMiaoPerfected,
		XjThreeBookEventTypes.PersonalFuQiJinDanFailure, XjThreeBookEventTypes.PersonalFuQiZhenJunYuShi,
		XjThreeBookEventTypes.PersonalFuQiLongGengSuccession, XjThreeBookEventTypes.PersonalFuQiToZiFu,
		XjThreeBookEventTypes.PersonalShiEntered, XjThreeBookEventTypes.PersonalShiBaseline, XjThreeBookEventTypes.PersonalShiConversion,
		XjThreeBookEventTypes.PersonalShiRealmChanged, XjThreeBookEventTypes.PersonalShiDharmaFormStage,
		XjThreeBookEventTypes.PersonalShiJinDiObtained, XjThreeBookEventTypes.PersonalShiReincarnation,
		XjThreeBookEventTypes.PersonalShiTrueSpiritReturn, XjThreeBookEventTypes.PersonalShiTrueSpiritAnnihilated,
		XjThreeBookEventTypes.PersonalShiWorldHonored, XjThreeBookEventTypes.PersonalShiPreaching,
		XjThreeBookEventTypes.PersonalShiPosition, XjThreeBookEventTypes.PersonalShiHighRealmVictory,
		XjThreeBookEventTypes.PersonalShiJinDiAbsorbed,
		XjThreeBookEventTypes.PersonalShiReturnToVoid
	};
	private static readonly string[] FamilyExpectedEventTypes =
	{
		XjThreeBookEventTypes.FamilyFounded, XjThreeBookEventTypes.FamilyTalentEmerged,
		XjThreeBookEventTypes.FamilyCultivatorEmerged, XjThreeBookEventTypes.FamilyHighRealmEmerged, XjThreeBookEventTypes.FamilySupportSelected,
		XjThreeBookEventTypes.FamilySupportGranted, XjThreeBookEventTypes.FamilyInheritanceAdded, XjThreeBookEventTypes.FamilyTreasureAdded,
		XjThreeBookEventTypes.FamilySectFounded, XjThreeBookEventTypes.FamilyMemberAchievement, XjThreeBookEventTypes.FamilyStageChanged,
		XjThreeBookEventTypes.FamilySurnameChanged,
		XjThreeBookEventTypes.FamilyMemberDeath, XjThreeBookEventTypes.FamilyMemberMerit, XjThreeBookEventTypes.FamilyDiscipline,
		XjThreeBookEventTypes.FamilyMentorshipLegacy, XjThreeBookEventTypes.FamilyRareCraft,
		XjThreeBookEventTypes.FamilyGuZunDissolved, XjThreeBookEventTypes.FamilyShiAchievement,
		XjThreeBookEventTypes.FamilyShiExpansion
	};
	private static readonly string[] SectExpectedEventTypes =
	{
		XjThreeBookEventTypes.SectFounded, XjThreeBookEventTypes.SectEnrollment, XjThreeBookEventTypes.SectLecture,
		XjThreeBookEventTypes.SectTournament, XjThreeBookEventTypes.SectSecretRealmQualification, XjThreeBookEventTypes.SectPeakMasterChanged,
		XjThreeBookEventTypes.SectSovereignChanged, XjThreeBookEventTypes.SectInheritanceAdded, XjThreeBookEventTypes.SectResourceChanged,
		XjThreeBookEventTypes.SectRelationChanged, XjThreeBookEventTypes.SectWarResult, XjThreeBookEventTypes.SectHighRealmEmerged,
		XjThreeBookEventTypes.SectExtinct, XjThreeBookEventTypes.SectFriendlyRelation, XjThreeBookEventTypes.SectAlliance,
		XjThreeBookEventTypes.SectResourceMilestone, XjThreeBookEventTypes.SectRareCraft,
		XjThreeBookEventTypes.SectGuZunDissolved, XjThreeBookEventTypes.SectShiAchievement,
		XjThreeBookEventTypes.SectShiExpansion
	};

	private readonly int _capacity;
	private readonly int _ledgerCapacity;
	private readonly int _minimumEntriesPerSubject;
	private readonly string _salt;
	private const int DisplayTrimBatchSize = 512;
	private const int LedgerPruneBatchSize = 512;
	private int _nextLedgerPruneCount;
	private readonly Dictionary<long, int> _trimSubjectCounts = new Dictionary<long, int>();
	private readonly HashSet<long> _trimRemovalIds = new HashSet<long>();
	private readonly List<XjThreeBookArchiveRecord> _trimSurvivors = new List<XjThreeBookArchiveRecord>();
	private List<XjThreeBookArchiveRecord> _entries = new List<XjThreeBookArchiveRecord>();
	private HashSet<long> _ids = new HashSet<long>();
	private HashSet<long> _sourceFacts = new HashSet<long>();
	private Dictionary<long, XjThreeBookSourceFactRecord> _sourceLedger = new Dictionary<long, XjThreeBookSourceFactRecord>();
	private Queue<long> _sourceLedgerOrder = new Queue<long>();
	private Dictionary<long, XjThreeBookArchiveRecord> _recordsBySourceFact = new Dictionary<long, XjThreeBookArchiveRecord>();
	private Dictionary<string, int> _eventCounts = new Dictionary<string, int>(StringComparer.Ordinal);
	private Dictionary<string, int> _eventTypeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
	private HashSet<string> _signatures = new HashSet<string>(StringComparer.Ordinal);
	private IReadOnlyList<XjThreeBookArchiveRecord> _snapshotCache = Array.Empty<XjThreeBookArchiveRecord>();
	private bool _snapshotDirty = true;
	private long _sequence;
	private long _attempts;
	private long _accepted;
	private long _invalid;
	private long _duplicateSource;
	private long _duplicateSignature;
	private long _trimmed;
	private long _imported;
	private long _ledgerPruned;
	private int _lastEventYear;

	internal XjThreeBookStoreCore(int capacity, string salt, int ledgerMultiplier, int minimumEntriesPerSubject)
	{
		_capacity = Math.Max(100, capacity);
		_ledgerCapacity = Math.Max(_capacity * Math.Max(4, ledgerMultiplier), 1000);
		_minimumEntriesPerSubject = Math.Max(1, minimumEntriesPerSubject);
		_salt = string.IsNullOrWhiteSpace(salt) ? "book" : salt.Trim();
		_nextLedgerPruneCount = _ledgerCapacity + LedgerPruneBatchSize;
	}

	internal int Count => _entries.Count;
	internal int Capacity => _capacity;

	internal bool ContainsSourceFact(string sourceFactId)
	{
		return TryBuildSourceHash(sourceFactId, out long sourceHash) && _sourceFacts.Contains(sourceHash);
	}

	internal bool TryGetBySourceFact(string sourceFactId, out XjThreeBookArchiveRecord record)
	{
		record = null;
		if (!TryBuildSourceHash(sourceFactId, out long sourceHash)) return false;
		if (_recordsBySourceFact.TryGetValue(sourceHash, out XjThreeBookArchiveRecord stored) && stored != null)
		{
			record = Clone(stored);
			return true;
		}
		if (!_sourceLedger.TryGetValue(sourceHash, out XjThreeBookSourceFactRecord ledger) || ledger == null) return false;
		record = new XjThreeBookArchiveRecord
		{
			SourceFactId = sourceFactId.Trim(),
			Year = ledger.Year,
			SubjectId = ledger.SubjectId,
			EventType = ledger.EventType
		};
		return true;
	}

	internal int CountEvents(long subjectId, string eventType, int maximum)
	{
		if (subjectId <= 0L || string.IsNullOrWhiteSpace(eventType) || maximum <= 0) return 0;
		return _eventCounts.TryGetValue(BuildEventCountKey(subjectId, eventType), out int count)
			? Math.Min(count, maximum)
			: 0;
	}

	internal bool Record(XjThreeBookArchiveRecord record)
	{
		_attempts++;
		if (record == null || record.SubjectId <= 0L || string.IsNullOrWhiteSpace(record.Body))
		{
			_invalid++;
			return false;
		}
		Normalize(record);
		if (IsSuppressedEventType(record.EventType))
		{
			_invalid++;
			return false;
		}
		string source = record.SourceFactId;
		long sourceHash = source.Length > 0 ? BuildSourceHash(source) : 0L;
		if (sourceHash > 0L && _sourceFacts.Contains(sourceHash))
		{
			_duplicateSource++;
			return false;
		}
		string signature = BuildSignature(record);
		if (_signatures.Contains(signature))
		{
			_duplicateSignature++;
			if (source.Length > 0) AddConsumedSourceLedger(source, record.Year);
			return false;
		}
		record.SortSequence = ++_sequence;
		record.BookEventId = BuildEventId(record, record.SortSequence);
		if (!_ids.Add(record.BookEventId))
		{
			_invalid++;
			return false;
		}
		_signatures.Add(signature);
		if (source.Length > 0) AddSourceLedger(record);
		else if (IsLifetimeIndexedEvent(record.EventType)) IndexLifetimeEvent(record.SubjectId, record.EventType);
		XjThreeBookArchiveRecord stored = Clone(record);
		_entries.Add(stored);
		IndexDisplayRecord(stored);
		_accepted++;
		_lastEventYear = Math.Max(_lastEventYear, stored.Year);
		InvalidateSnapshot();
		Trim();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
		return true;
	}

	internal IReadOnlyList<XjThreeBookArchiveRecord> ReadSnapshot(int maximumEntries = 0)
	{
		if (maximumEntries <= 0 || maximumEntries >= _entries.Count)
		{
			if (_snapshotDirty)
			{
				List<XjThreeBookArchiveRecord> all = new List<XjThreeBookArchiveRecord>(_entries.Count);
				for (int i = 0; i < _entries.Count; i++)
				{
					XjThreeBookArchiveRecord item = _entries[i];
					if (item == null || IsSuppressedEventType(item.EventType)) continue;
					all.Add(Clone(item));
				}
				_snapshotCache = all;
				_snapshotDirty = false;
			}
			return _snapshotCache;
		}
		int count = Math.Min(maximumEntries, _entries.Count);
		int start = _entries.Count - count;
		List<XjThreeBookArchiveRecord> result = new List<XjThreeBookArchiveRecord>(count);
		for (int i = start; i < _entries.Count; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || IsSuppressedEventType(item.EventType)) continue;
			result.Add(Clone(item));
		}
		return result;
	}

	internal void Export(List<XjThreeBookArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < _entries.Count; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || IsSuppressedEventType(item.EventType)) continue;
			target.Add(Clone(item));
		}
	}

	internal void ExportLedger(List<XjThreeBookSourceFactRecord> target)
	{
		if (target == null) return;
		target.Clear();
		foreach (long key in _sourceLedgerOrder)
		{
			if (_sourceLedger.TryGetValue(key, out XjThreeBookSourceFactRecord item) && item != null)
				target.Add(CloneLedger(item));
		}
	}

	internal void Import(IReadOnlyList<XjThreeBookArchiveRecord> source, IReadOnlyList<XjThreeBookSourceFactRecord> ledger)
	{
		Reset(false);
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				XjThreeBookSourceFactRecord item = ledger[i];
				if (item == null || item.SourceFactHash <= 0L) continue;
				AddSourceLedger(item);
			}
		}
		if (source != null)
		{
			for (int i = 0; i < source.Count; i++)
			{
				XjThreeBookArchiveRecord item = source[i];
				if (item == null || item.SubjectId <= 0L || string.IsNullOrWhiteSpace(item.Body))
				{
					_invalid++;
					continue;
				}
				XjThreeBookArchiveRecord copy = Clone(item);
				Normalize(copy);
				if (IsSuppressedEventType(copy.EventType))
				{
					_invalid++;
					continue;
				}
				string signature = BuildSignature(copy);
				if (!_signatures.Add(signature))
				{
					_duplicateSignature++;
					if (copy.SourceFactId.Length > 0) AddConsumedSourceLedger(copy.SourceFactId, copy.Year);
					continue;
				}
				if (copy.SortSequence <= 0L) copy.SortSequence = ++_sequence;
				else _sequence = Math.Max(_sequence, copy.SortSequence);
				if (copy.BookEventId <= 0L || _ids.Contains(copy.BookEventId)) copy.BookEventId = BuildEventId(copy, copy.SortSequence);
				_ids.Add(copy.BookEventId);
				if (copy.SourceFactId.Length > 0) AddSourceLedger(copy);
				else if (IsLifetimeIndexedEvent(copy.EventType)) IndexLifetimeEvent(copy.SubjectId, copy.EventType);
				_entries.Add(copy);
				IndexDisplayRecord(copy);
				_imported++;
				_lastEventYear = Math.Max(_lastEventYear, copy.Year);
			}
		}
		Trim();
		// 旧档可能一次导入数万条历史，再被新容量线裁到数千条。若继续沿用导入
		// 时扩张出的 List/HashSet 桶，条目虽然删掉，峰值内存仍会留在当前会话。
		// 导档仅发生在读档/归档恢复，允许在这里一次性收束容器；不要等到多年后的
		// 定期维护才回收这部分常驻压力。
		RebuildStorage();
		InvalidateSnapshot();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
	}

	internal XjThreeBookStoreMetrics ReadMetrics()
	{
		return new XjThreeBookStoreMetrics
		{
			Name = _salt,
			Count = _entries.Count,
			Capacity = _capacity,
			LedgerCount = _sourceLedger.Count,
			LedgerCapacity = _ledgerCapacity,
			EventTypeCount = _eventTypeCounts.Count,
			ExpectedEventTypeCount = GetExpectedEventTypes().Length,
			EventTypes = BuildEventTypeSummary(),
			MissingEventTypes = BuildMissingEventTypeSummary(),
			LastEventYear = _lastEventYear,
			Attempts = _attempts,
			Accepted = _accepted,
			Invalid = _invalid,
			DuplicateSource = _duplicateSource,
			DuplicateSignature = _duplicateSignature,
			Trimmed = _trimmed,
			Imported = _imported,
			LedgerPruned = _ledgerPruned,
			SoftOverflow = Math.Max(0, _entries.Count - _capacity)
		};
	}

	internal bool Validate(out string message)
	{
		if (_entries.Count != _ids.Count || _entries.Count != _signatures.Count)
		{
			message = _salt + " entries/ids/signatures不一致 " + _entries.Count + "/" + _ids.Count + "/" + _signatures.Count;
			return false;
		}
		if (_sourceLedger.Count != _sourceFacts.Count || _sourceLedger.Count != _sourceLedgerOrder.Count)
		{
			message = _salt + " 事实账本索引不一致 " + _sourceLedger.Count + "/" + _sourceFacts.Count + "/" + _sourceLedgerOrder.Count;
			return false;
		}
		int displayedEventCount = 0;
		foreach (int count in _eventTypeCounts.Values) displayedEventCount += count;
		if (displayedEventCount != _entries.Count)
		{
			message = _salt + " 事件型计数不一致 " + displayedEventCount + "/" + _entries.Count;
			return false;
		}
		for (int i = 0; i < _entries.Count; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || item.SubjectId <= 0L || item.BookEventId <= 0L || string.IsNullOrWhiteSpace(item.Body))
			{
				message = _salt + " 存在无效条目 index=" + i;
				return false;
			}
			if (!string.IsNullOrWhiteSpace(item.SourceFactId))
			{
				long sourceHash = BuildSourceHash(item.SourceFactId);
				if (!_sourceFacts.Contains(sourceHash)
					|| !_sourceLedger.TryGetValue(sourceHash, out XjThreeBookSourceFactRecord ledger)
					|| ledger == null)
				{
					message = _salt + " 显示条目缺少事实账本 source=" + item.SourceFactId;
					return false;
				}
				if (ledger.SubjectId > 0L
					&& (ledger.SubjectId != item.SubjectId || !string.Equals(ledger.EventType, item.EventType, StringComparison.Ordinal)))
				{
					message = _salt + " 显示条目与事实账本不一致 source=" + item.SourceFactId;
					return false;
				}
			}
		}
		message = string.Empty;
		return true;
	}

	internal void Clear()
	{
		Reset(true);
	}

	internal void ReleaseSnapshotCache()
	{
		_snapshotCache = Array.Empty<XjThreeBookArchiveRecord>();
		_snapshotDirty = true;
	}

	internal void CompactMemory(bool rebuildStorage)
	{
		Trim();
		PruneSourceLedger();
		ReleaseSnapshotCache();
		if (!rebuildStorage) return;
		RebuildStorage();
	}

	private void RebuildStorage()
	{
		// 长档裁剪后 Dictionary/HashSet/Queue 仍会保留历史峰值桶容量。
		// 定期重建容器，才能真正把已删除项占用的托管数组交还给 GC。
		_entries = new List<XjThreeBookArchiveRecord>(_entries);
		_ids = new HashSet<long>(_ids);
		_sourceFacts = new HashSet<long>(_sourceFacts);
		_sourceLedger = new Dictionary<long, XjThreeBookSourceFactRecord>(_sourceLedger);
		_sourceLedgerOrder = new Queue<long>(_sourceLedgerOrder);
		_recordsBySourceFact = new Dictionary<long, XjThreeBookArchiveRecord>(_recordsBySourceFact);
		_eventCounts = new Dictionary<string, int>(_eventCounts, StringComparer.Ordinal);
		_eventTypeCounts = new Dictionary<string, int>(_eventTypeCounts, StringComparer.Ordinal);
		_signatures = new HashSet<string>(_signatures, StringComparer.Ordinal);
	}

	private void Reset(bool markDirty)
	{
		_entries.Clear();
		_ids.Clear();
		_sourceFacts.Clear();
		_sourceLedger.Clear();
		_sourceLedgerOrder.Clear();
		_recordsBySourceFact.Clear();
		_eventCounts.Clear();
		_eventTypeCounts.Clear();
		_signatures.Clear();
		_snapshotCache = Array.Empty<XjThreeBookArchiveRecord>();
		_snapshotDirty = true;
		_sequence = 0L;
		_attempts = 0L;
		_accepted = 0L;
		_invalid = 0L;
		_duplicateSource = 0L;
		_duplicateSignature = 0L;
		_trimmed = 0L;
		_imported = 0L;
		_ledgerPruned = 0L;
		_lastEventYear = 0;
		_nextLedgerPruneCount = _ledgerCapacity + LedgerPruneBatchSize;
		if (markDirty)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
		}
	}

	private long BuildEventId(XjThreeBookArchiveRecord record, long sequence)
	{
		long seed = Math.Max(1L, sequence);
		long id = XjDeterministicHash.PositiveHash(seed, _salt + "|" + record.SubjectId + "|" + record.Year + "|" + record.EventType + "|" + record.SourceFactId + "|" + seed);
		if (id <= 0L) id = seed;
		while (_ids.Contains(id)) id = id == long.MaxValue ? 1L : id + 1L;
		return id;
	}

	private void Trim()
	{
		if (_entries.Count <= _capacity) return;

		// 长档三书一旦达到 1~1.6 万条，旧实现会在每一条新纪事写入后：
		// 1) 重建 subjectCounts；2) 线性寻找一个可删项；3) List.RemoveAt 搬移上万元素。
		// 这会把“历史越来越长”直接变成持续主线程税。这里改成小幅滞回批裁剪：
		// 一次线性选择最多约 512 条低价值旧记录并整体重建 List，之后约 512 次新增都无需再裁剪。
		int softLimit = _capacity + Math.Max(100, _capacity / 20);
		int targetCount = Math.Max(100, _capacity - DisplayTrimBatchSize);
		int removalGoal = Math.Max(1, _entries.Count - targetCount);

		_trimSubjectCounts.Clear();
		for (int i = 0; i < _entries.Count; i++)
		{
			long subjectId = _entries[i]?.SubjectId ?? 0L;
			if (subjectId <= 0L) continue;
			_trimSubjectCounts.TryGetValue(subjectId, out int count);
			_trimSubjectCounts[subjectId] = count + 1;
		}

		_trimRemovalIds.Clear();
		SelectTrimCandidates(removalGoal, 2);
		SelectTrimCandidates(removalGoal, 5);

		// 保护条目只享有短暂缓冲，绝不能成为无限增长豁免。事实账本仍保留
		// 去重信息；这里回收的是玩家可见的旧过程叙事。
		if (_entries.Count - _trimRemovalIds.Count > _capacity && _entries.Count > softLimit)
			SelectProtectedFallbackCandidates(removalGoal);
		if (_entries.Count - _trimRemovalIds.Count > _capacity)
			SelectAnchorFallbackCandidates(removalGoal);
		if (_entries.Count - _trimRemovalIds.Count > _capacity)
			SelectAbsoluteFallbackCandidates(removalGoal);

		if (_trimRemovalIds.Count == 0) return;

		_trimSurvivors.Clear();
		_trimSurvivors.Capacity = Math.Max(_trimSurvivors.Capacity, _entries.Count - _trimRemovalIds.Count);
		for (int i = 0; i < _entries.Count; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item != null && _trimRemovalIds.Contains(item.BookEventId))
			{
				_ids.Remove(item.BookEventId);
				_signatures.Remove(BuildSignature(item));
				UnindexDisplayRecord(item);
				_trimmed++;
				continue;
			}
			_trimSurvivors.Add(item);
		}

		_entries.Clear();
		_entries.AddRange(_trimSurvivors);
		InvalidateSnapshot();
	}

	private void SelectTrimCandidates(int removalGoal, int maxImportance)
	{
		if (_trimRemovalIds.Count >= removalGoal) return;
		for (int i = 0; i < _entries.Count && _trimRemovalIds.Count < removalGoal; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || _trimRemovalIds.Contains(item.BookEventId)) continue;
			if (!CanRemove(item, _trimSubjectCounts, false, false, maxImportance, _minimumEntriesPerSubject)) continue;
			_trimRemovalIds.Add(item.BookEventId);
			if (_trimSubjectCounts.TryGetValue(item.SubjectId, out int count)) _trimSubjectCounts[item.SubjectId] = Math.Max(0, count - 1);
		}
	}

	private void SelectProtectedFallbackCandidates(int removalGoal)
	{
		if (_trimRemovalIds.Count >= removalGoal) return;
		for (int i = 0; i < _entries.Count && _trimRemovalIds.Count < removalGoal; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || _trimRemovalIds.Contains(item.BookEventId) || IsAnchorEvent(item.EventType)) continue;
			if (!_trimSubjectCounts.TryGetValue(item.SubjectId, out int count) || count <= _minimumEntriesPerSubject) continue;
			_trimRemovalIds.Add(item.BookEventId);
			_trimSubjectCounts[item.SubjectId] = count - 1;
		}
	}

	private void SelectAnchorFallbackCandidates(int removalGoal)
	{
		if (_trimRemovalIds.Count >= removalGoal) return;
		for (int i = 0; i < _entries.Count && _trimRemovalIds.Count < removalGoal; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || _trimRemovalIds.Contains(item.BookEventId)) continue;
			if (!_trimSubjectCounts.TryGetValue(item.SubjectId, out int count) || count <= 1) continue;
			// 当旧档的锚点本身已超过容量时，优先回收同一主体较早的锚点，
			// 留下最后一条记录，使人物、家族、宗门仍有可追溯的史册入口。
			_trimRemovalIds.Add(item.BookEventId);
			_trimSubjectCounts[item.SubjectId] = count - 1;
		}
	}

	private void SelectAbsoluteFallbackCandidates(int removalGoal)
	{
		if (_trimRemovalIds.Count >= removalGoal) return;
		// 最后一层只在世界历史主体数已经超过史册总容量时启用。此时若仍保证
		// “每个主体至少一条”，容量承诺就不可能成立；按时间淘汰最旧显示条目，
		// 但不动 source ledger，既避免旧事被重新生成，也保证存档不会无界增长。
		for (int i = 0; i < _entries.Count && _trimRemovalIds.Count < removalGoal; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item == null || _trimRemovalIds.Contains(item.BookEventId)) continue;
			_trimRemovalIds.Add(item.BookEventId);
		}
	}

	private void AddSourceLedger(XjThreeBookArchiveRecord record)
	{
		bool lifetimeIndexed = IsLifetimeIndexedEvent(record.EventType);
		AddSourceLedger(new XjThreeBookSourceFactRecord
		{
			SourceFactHash = BuildSourceHash(record.SourceFactId),
			Year = lifetimeIndexed ? record.Year : 0,
			SubjectId = lifetimeIndexed ? record.SubjectId : 0L,
			EventType = lifetimeIndexed ? record.EventType : string.Empty
		});
	}

	private void AddConsumedSourceLedger(string sourceFactId, int year)
	{
		AddSourceLedger(new XjThreeBookSourceFactRecord
		{
			SourceFactHash = BuildSourceHash(sourceFactId),
			Year = 0,
			SubjectId = 0L,
			EventType = string.Empty
		});
	}

	private void AddSourceLedger(XjThreeBookSourceFactRecord record)
	{
		if (record == null || record.SourceFactHash <= 0L) return;
		long sourceHash = record.SourceFactHash;
		if (!_sourceFacts.Add(sourceHash)) return;
		XjThreeBookSourceFactRecord copy = CloneLedger(record);
		copy.SourceFactHash = sourceHash;
		copy.EventType = string.IsNullOrWhiteSpace(copy.EventType) ? string.Empty : copy.EventType.Trim();
		copy.Year = Math.Max(0, copy.Year);
		copy.SubjectId = Math.Max(0L, copy.SubjectId);
		_sourceLedger[sourceHash] = copy;
		_sourceLedgerOrder.Enqueue(sourceHash);
		if (copy.SubjectId > 0L && !string.IsNullOrWhiteSpace(copy.EventType)) IndexLifetimeEvent(copy.SubjectId, copy.EventType);
		PruneSourceLedger();
	}

	private void PruneSourceLedger()
	{
		int count = _sourceLedger.Count;
		if (count <= _ledgerCapacity)
		{
			_nextLedgerPruneCount = _ledgerCapacity + LedgerPruneBatchSize;
			return;
		}
		if (count < _nextLedgerPruneCount) return;

		int softLimit = _ledgerCapacity + Math.Max(1000, _ledgerCapacity / 20);
		int targetCount = _ledgerCapacity;
		bool removedAny = false;
		while (_sourceLedger.Count > targetCount)
		{
			// 正常容量内优先保留仍在UI显示的事实和社交生涯元数据。
			if (TryPruneOneLedger(false))
			{
				removedAny = true;
				continue;
			}
			// 若账本全部是受保护社交事实，允许5%软溢出；超过软上限后仍须裁剪最旧的
			// 非显示事实，防止数千年高人口存档中的伴侣/知交元数据无限增长。
			if (_sourceLedger.Count <= softLimit || !TryPruneOneLedger(true)) break;
			removedAny = true;
		}

		// 不再在容量饱和后每新增一条事实就旋转数万项 Queue。正常有可裁项时，
		// 一次回收到容量线并留出512条滞回；若当前全部属于受保护事实，则直接
		// 推迟到软上限以后再探测，避免连续数百条新事实重复做“全队列找不到”。
		if (_sourceLedger.Count <= _ledgerCapacity)
			_nextLedgerPruneCount = _ledgerCapacity + LedgerPruneBatchSize;
		else if (!removedAny && _sourceLedger.Count <= softLimit)
			_nextLedgerPruneCount = softLimit + 1;
		else
			_nextLedgerPruneCount = _sourceLedger.Count + LedgerPruneBatchSize;
	}

	private bool TryPruneOneLedger(bool allowLifetimeIndexed)
	{
		int guard = _sourceLedgerOrder.Count;
		while (guard-- > 0 && _sourceLedgerOrder.Count > 0)
		{
			long sourceHash = _sourceLedgerOrder.Dequeue();
			if (!_sourceLedger.TryGetValue(sourceHash, out XjThreeBookSourceFactRecord item)) continue;
			if (_recordsBySourceFact.ContainsKey(sourceHash) || !allowLifetimeIndexed && IsLifetimeIndexedLedger(item))
			{
				_sourceLedgerOrder.Enqueue(sourceHash);
				continue;
			}
			_sourceLedger.Remove(sourceHash);
			_sourceFacts.Remove(sourceHash);
			_ledgerPruned++;
			UnindexLifetimeEvent(item.SubjectId, item.EventType);
			return true;
		}
		return false;
	}

	private void IndexDisplayRecord(XjThreeBookArchiveRecord record)
	{
		if (record == null) return;
		if (!string.IsNullOrWhiteSpace(record.SourceFactId)) _recordsBySourceFact[BuildSourceHash(record.SourceFactId)] = record;
		IndexDisplayEventType(record.EventType);
	}

	private void UnindexDisplayRecord(XjThreeBookArchiveRecord record)
	{
		if (record == null) return;
		if (!string.IsNullOrWhiteSpace(record.SourceFactId)) _recordsBySourceFact.Remove(BuildSourceHash(record.SourceFactId));
		UnindexDisplayEventType(record.EventType);
	}

	private void IndexLifetimeEvent(long subjectId, string eventType)
	{
		if (subjectId <= 0L || string.IsNullOrWhiteSpace(eventType)) return;
		string key = BuildEventCountKey(subjectId, eventType);
		_eventCounts.TryGetValue(key, out int count);
		_eventCounts[key] = count + 1;
	}

	private void UnindexLifetimeEvent(long subjectId, string eventType)
	{
		if (subjectId <= 0L || string.IsNullOrWhiteSpace(eventType)) return;
		string key = BuildEventCountKey(subjectId, eventType);
		if (!_eventCounts.TryGetValue(key, out int count)) return;
		if (count <= 1) _eventCounts.Remove(key);
		else _eventCounts[key] = count - 1;
	}

	private void IndexDisplayEventType(string eventType)
	{
		if (string.IsNullOrWhiteSpace(eventType)) return;
		string normalized = eventType.Trim();
		_eventTypeCounts.TryGetValue(normalized, out int count);
		_eventTypeCounts[normalized] = count + 1;
	}

	private void UnindexDisplayEventType(string eventType)
	{
		if (string.IsNullOrWhiteSpace(eventType)) return;
		string normalized = eventType.Trim();
		if (!_eventTypeCounts.TryGetValue(normalized, out int count)) return;
		if (count <= 1) _eventTypeCounts.Remove(normalized);
		else _eventTypeCounts[normalized] = count - 1;
	}

	private static bool IsLifetimeIndexedEvent(string eventType)
	{
		return string.Equals(eventType, XjThreeBookEventTypes.PersonalAcquaintance, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.PersonalCloseFriend, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.PersonalDaoCompanion, StringComparison.Ordinal);
	}

	private static bool IsLifetimeIndexedLedger(XjThreeBookSourceFactRecord item)
	{
		return item != null && item.SubjectId > 0L && IsLifetimeIndexedEvent(item.EventType);
	}

	private string BuildEventTypeSummary()
	{
		if (_eventTypeCounts.Count == 0) return "未载";
		List<string> keys = new List<string>(_eventTypeCounts.Keys);
		keys.Sort(StringComparer.Ordinal);
		List<string> parts = new List<string>(keys.Count);
		for (int i = 0; i < keys.Count; i++) parts.Add(keys[i] + "×" + _eventTypeCounts[keys[i]]);
		return string.Join("、", parts);
	}

	private string BuildMissingEventTypeSummary()
	{
		string[] expected = GetExpectedEventTypes();
		List<string> missing = new List<string>();
		for (int i = 0; i < expected.Length; i++) if (!_eventTypeCounts.ContainsKey(expected[i])) missing.Add(expected[i]);
		return missing.Count == 0 ? "无" : string.Join("、", missing);
	}

	private string[] GetExpectedEventTypes()
	{
		return string.Equals(_salt, "personal", StringComparison.Ordinal) ? PersonalExpectedEventTypes
			: string.Equals(_salt, "family", StringComparison.Ordinal) ? FamilyExpectedEventTypes
			: SectExpectedEventTypes;
	}

	private long BuildSourceHash(string sourceFactId)
	{
		if (string.IsNullOrWhiteSpace(sourceFactId)) return 0L;
		long hash = XjDeterministicHash.StableHash(_salt + "|" + sourceFactId.Trim());
		return hash > 0L ? hash : 1L;
	}

	private bool TryBuildSourceHash(string sourceFactId, out long sourceHash)
	{
		sourceHash = BuildSourceHash(sourceFactId);
		return sourceHash > 0L;
	}

	private static string BuildEventCountKey(long subjectId, string eventType)
	{
		return subjectId + "|" + (eventType ?? string.Empty).Trim();
	}

	private int FindRemovableIndex()
	{
		Dictionary<long, int> subjectCounts = new Dictionary<long, int>();
		for (int i = 0; i < _entries.Count; i++)
		{
			long subjectId = _entries[i]?.SubjectId ?? 0L;
			if (subjectId <= 0L) continue;
			subjectCounts.TryGetValue(subjectId, out int count);
			subjectCounts[subjectId] = count + 1;
		}
		// 容量内优先裁剪同一卷中的低价值重复事项。卷首、修炼资格、死亡等锚点，
		// 以及某个主体仅剩的最后一条记录，不在常规裁剪中删除。若全部为保护事项，
		// 允许在5%软上限内暂时溢出，再由硬上限兜底裁剪非锚点。
		for (int i = 0; i < _entries.Count; i++) if (CanRemove(_entries[i], subjectCounts, false, false, 2, _minimumEntriesPerSubject)) return i;
		for (int i = 0; i < _entries.Count; i++) if (CanRemove(_entries[i], subjectCounts, false, false, 5, _minimumEntriesPerSubject)) return i;
		return -1;
	}

	private int FindProtectedFallbackIndex()
	{
		Dictionary<long, int> subjectCounts = new Dictionary<long, int>();
		for (int i = 0; i < _entries.Count; i++)
		{
			long subjectId = _entries[i]?.SubjectId ?? 0L;
			if (subjectId <= 0L) continue;
			subjectCounts.TryGetValue(subjectId, out int count);
			subjectCounts[subjectId] = count + 1;
		}
		for (int i = 0; i < _entries.Count; i++)
		{
			XjThreeBookArchiveRecord item = _entries[i];
			if (item != null && !IsAnchorEvent(item.EventType)
				&& subjectCounts.TryGetValue(item.SubjectId, out int count)
				&& count > _minimumEntriesPerSubject) return i;
		}
		return -1;
	}

	private static bool CanRemove(XjThreeBookArchiveRecord item, IReadOnlyDictionary<long, int> subjectCounts, bool allowProtected, bool allowAnchor, int maxImportance, int minimumEntriesPerSubject)
	{
		if (item == null || !allowProtected && item.IsProtected || item.Importance > maxImportance) return false;
		if (!allowAnchor && IsAnchorEvent(item.EventType)) return false;
		return subjectCounts.TryGetValue(item.SubjectId, out int count) && count > Math.Max(1, minimumEntriesPerSubject);
	}

	private static bool IsSuppressedEventType(string eventType)
	{
		return string.Equals(eventType, XjThreeBookEventTypes.FamilyBloodlineBirth, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.PersonalShiAncientDuhua, StringComparison.Ordinal);
	}

	private static bool IsAnchorEvent(string eventType)
	{
		return string.Equals(eventType, XjThreeBookEventTypes.PersonalBirth, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.PersonalCultivationQualified, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.PersonalDeath, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.FamilyFounded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.SectFounded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjThreeBookEventTypes.SectExtinct, StringComparison.Ordinal);
	}

	private static string BuildSignature(XjThreeBookArchiveRecord record)
	{
		return record.SubjectId + "|" + record.Year + "|" + record.EventType + "|" + record.ActorId + "|" + record.RelatedActorId + "|" + record.Body;
	}

	private void InvalidateSnapshot()
	{
		_snapshotDirty = true;
		_snapshotCache = Array.Empty<XjThreeBookArchiveRecord>();
	}

	private static void Normalize(XjThreeBookArchiveRecord record)
	{
		record.SchemaVersion = XjThreeBookSchema.CurrentVersion;
		record.SourceFactId = (record.SourceFactId ?? string.Empty).Trim();
		record.SubjectNameSnapshot = (record.SubjectNameSnapshot ?? string.Empty).Trim();
		record.Year = Math.Max(0, record.Year);
		record.EventType = string.IsNullOrWhiteSpace(record.EventType) ? "BookEvent" : record.EventType.Trim();
		record.Category = string.IsNullOrWhiteSpace(record.Category) ? XjWorldHistoryCategory.World : record.Category.Trim();
		record.Tag = string.IsNullOrWhiteSpace(record.Tag) ? "纪事" : record.Tag.Trim();
		record.Title = (record.Title ?? string.Empty).Trim();
		record.Body = (record.Body ?? string.Empty).Trim();
		if (record.Title.Length == 0) record.Title = record.Tag;
		record.IconId = (record.IconId ?? string.Empty).Trim();
		record.Importance = Math.Clamp(record.Importance, 1, 5);
		record.Result = (record.Result ?? string.Empty).Trim();
		record.ActorName = (record.ActorName ?? string.Empty).Trim();
		record.RelatedActorName = (record.RelatedActorName ?? string.Empty).Trim();
		record.FamilyNameSnapshot = (record.FamilyNameSnapshot ?? string.Empty).Trim();
		record.RelatedFamilyNameSnapshot = (record.RelatedFamilyNameSnapshot ?? string.Empty).Trim();
		record.SectNameSnapshot = (record.SectNameSnapshot ?? string.Empty).Trim();
		record.RelatedSectNameSnapshot = (record.RelatedSectNameSnapshot ?? string.Empty).Trim();
		record.CityNameSnapshot = (record.CityNameSnapshot ?? string.Empty).Trim();
	}

	private static XjThreeBookSourceFactRecord CloneLedger(XjThreeBookSourceFactRecord source)
	{
		return new XjThreeBookSourceFactRecord
		{
			SourceFactHash = Math.Max(0L, source?.SourceFactHash ?? 0L),
			Year = Math.Max(0, source?.Year ?? 0),
			SubjectId = Math.Max(0L, source?.SubjectId ?? 0L),
			EventType = source?.EventType ?? string.Empty
		};
	}

	private static XjThreeBookArchiveRecord Clone(XjThreeBookArchiveRecord source)
	{
		return new XjThreeBookArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			BookEventId = source.BookEventId,
			SortSequence = source.SortSequence,
			SourceFactId = source.SourceFactId,
			SubjectId = source.SubjectId,
			SubjectNameSnapshot = source.SubjectNameSnapshot,
			Year = source.Year,
			EventType = source.EventType,
			Category = source.Category,
			Tag = source.Tag,
			Title = source.Title,
			Body = source.Body,
			IconId = source.IconId,
			Importance = source.Importance,
			IsProtected = source.IsProtected,
			Result = source.Result,
			ActorId = source.ActorId,
			ActorName = source.ActorName,
			RelatedActorId = source.RelatedActorId,
			RelatedActorName = source.RelatedActorName,
			FamilyId = source.FamilyId,
			FamilyNameSnapshot = source.FamilyNameSnapshot,
			RelatedFamilyId = source.RelatedFamilyId,
			RelatedFamilyNameSnapshot = source.RelatedFamilyNameSnapshot,
			SectId = source.SectId,
			SectNameSnapshot = source.SectNameSnapshot,
			RelatedSectId = source.RelatedSectId,
			RelatedSectNameSnapshot = source.RelatedSectNameSnapshot,
			CityId = source.CityId,
			CityNameSnapshot = source.CityNameSnapshot,
			LocationX = source.LocationX,
			LocationY = source.LocationY,
			HasLocation = source.HasLocation
		};
	}
}
