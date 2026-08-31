using System;
using System.Collections.Generic;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.History;

internal static class XjWorldHistoryStore
{
	private const int MaxEntries = 8000;
	private const int TrimBatchSize = 512;
	private static readonly List<XjWorldHistoryArchiveRecord> Entries = new List<XjWorldHistoryArchiveRecord>();
	private static readonly HashSet<long> EventIds = new HashSet<long>();
	private static readonly HashSet<string> EventSignatures = new HashSet<string>(StringComparer.Ordinal);
	// 同一条事件可能经由角色公告与领域记录两条路径抵达历史；这里按年份和原文去重。
	private static readonly HashSet<string> EventBodySignatures = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<long, List<long>> EventIdsByActor = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, List<long>> EventIdsByFamily = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, List<long>> EventIdsBySect = new Dictionary<long, List<long>>();
	private static readonly HashSet<long> TrimRemovalIds = new HashSet<long>();
	private static readonly List<XjWorldHistoryArchiveRecord> TrimSurvivors = new List<XjWorldHistoryArchiveRecord>(MaxEntries);
	private static long _sequence;
	private static int _suppressedPruneCursor = -1;

	internal static int Count => Entries.Count;
	internal static int Capacity => MaxEntries;

	internal static bool RecordActorEvent(Actor actor, string text, string iconId)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(text)) return false;
		string cleanedText = XjDisplayNameSanitizer.Clean(text, "玄鉴事件");
		long actorId = ((BaseSystemData)actor.data).id;
		int year = ResolveCurrentYear();
		long familyId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId);
		long sectId = ResolveActorSectId(actor);
		long cityId = actor.city?.data?.id ?? 0L;
		int x = 0;
		int y = 0;
		bool hasLocation = false;
		try
		{
			x = (int)Math.Round(actor.current_position.x);
			y = (int)Math.Round(actor.current_position.y);
			hasLocation = true;
		}
		catch (System.Exception xjCaught53) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/History/XjWorldHistoryStore.cs:53", xjCaught53); }

		string normalizedIconId = XjEventIconCatalog.NormalizeIconId(iconId);
		string category = ResolveCategory(normalizedIconId, cleanedText, XjWorldHistoryCategory.World);
		int importance = ResolveImportance(normalizedIconId, cleanedText, category);
		return Add(new XjWorldHistoryArchiveRecord
		{
			Year = year,
			EventType = ResolveEventType(cleanedText, category),
			Category = category,
			Title = cleanedText,
			Body = cleanedText,
			IconId = ResolveHistoryIcon(normalizedIconId, category),
			Importance = importance,
			IsProtected = importance >= 4,
			ActorId = actorId,
			ActorName = XjStringHelper.ActorName(actor, "未名修士"),
			SectId = sectId,
			SectNameSnapshot = ResolveSectName(sectId),
			FamilyId = familyId,
			FamilyNameSnapshot = ResolveFamilyName(familyId),
			CityId = cityId,
			CityNameSnapshot = ResolveCityName(actor.city),
			VisibilityFlags = ResolveVisibilityFlags(importance, actorId, familyId, sectId, category),
			Result = ResolveResult(cleanedText),
			LocationX = x,
			LocationY = y,
			HasLocation = hasLocation
		});
	}

	internal static bool RecordWorldEvent(string text, string iconId)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;
		string cleanedText = XjDisplayNameSanitizer.Clean(text, "玄鉴事件");
		string normalizedIconId = XjEventIconCatalog.NormalizeIconId(iconId);
		string category = ResolveCategory(normalizedIconId, cleanedText, XjWorldHistoryCategory.World);
		int importance = ResolveImportance(normalizedIconId, cleanedText, category);
		return Add(new XjWorldHistoryArchiveRecord
		{
			Year = ResolveCurrentYear(),
			EventType = ResolveEventType(cleanedText, category),
			Category = category,
			Title = cleanedText,
			Body = cleanedText,
			IconId = ResolveHistoryIcon(normalizedIconId, category),
			Importance = importance,
			IsProtected = importance >= 4,
			VisibilityFlags = (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			Result = ResolveResult(cleanedText)
		});
	}

	internal static void RecordDomainEvent(
		string category,
		string title,
		string body,
		int importance,
		bool isProtected = false,
		long actorId = 0L,
		string actorName = "",
		long sectId = 0L,
		long familyId = 0L,
		long cityId = 0L,
		int year = -1,
		int locationX = int.MinValue,
		int locationY = int.MinValue,
		string iconIdOverride = null,
		string eventType = null,
		long relatedActorId = 0L,
		string relatedActorName = "",
		long relatedFamilyId = 0L,
		long relatedSectId = 0L,
		int visibilityFlags = 0,
		string result = null,
		long causeEventId = 0L,
		bool mirrorToWorldLog = true)
	{
		string normalizedTitle = XjDisplayNameSanitizer.Clean(title, string.Empty);
		string normalizedBody = XjDisplayNameSanitizer.Clean(body, string.Empty);
		if (normalizedTitle.Length == 0 && normalizedBody.Length == 0) return;
		if (normalizedBody.Length == 0) normalizedBody = normalizedTitle;
		if (normalizedTitle.Length == 0) normalizedTitle = normalizedBody;
		string iconId = string.IsNullOrWhiteSpace(iconIdOverride)
			? XjEventIconCatalog.ResolveFromWorldHistoryText(normalizedTitle + " " + normalizedBody)
			: XjEventIconCatalog.NormalizeIconId(iconIdOverride);
		string normalizedCategory = ResolveCategory(iconId, normalizedTitle + " " + normalizedBody, category);
		int normalizedImportance = ResolveImportance(iconId, normalizedTitle + " " + normalizedBody, normalizedCategory, importance);
		int resolvedVisibility = visibilityFlags != 0
			? visibilityFlags
			: ResolveVisibilityFlags(normalizedImportance, actorId, familyId, sectId, normalizedCategory);
		bool added = Add(new XjWorldHistoryArchiveRecord
		{
			Year = year >= 0 ? year : ResolveCurrentYear(),
			EventType = string.IsNullOrWhiteSpace(eventType) ? ResolveEventType(normalizedTitle + " " + normalizedBody, normalizedCategory) : eventType.Trim(),
			Category = normalizedCategory,
			Title = normalizedTitle,
			Body = normalizedBody,
			IconId = ResolveHistoryIcon(iconId, normalizedCategory),
			Importance = normalizedImportance,
			IsProtected = isProtected || normalizedImportance >= 4,
			ActorId = Math.Max(0L, actorId),
			ActorName = (actorName ?? string.Empty).Trim(),
			RelatedActorId = Math.Max(0L, relatedActorId),
			RelatedActorName = (relatedActorName ?? string.Empty).Trim(),
			SectId = Math.Max(0L, sectId),
			SectNameSnapshot = ResolveSectName(sectId),
			RelatedSectId = Math.Max(0L, relatedSectId),
			RelatedSectNameSnapshot = ResolveSectName(relatedSectId),
			FamilyId = Math.Max(0L, familyId),
			FamilyNameSnapshot = ResolveFamilyName(familyId),
			RelatedFamilyId = Math.Max(0L, relatedFamilyId),
			RelatedFamilyNameSnapshot = ResolveFamilyName(relatedFamilyId),
			CityId = Math.Max(0L, cityId),
			VisibilityFlags = resolvedVisibility,
			Result = string.IsNullOrWhiteSpace(result) ? ResolveResult(normalizedTitle + " " + normalizedBody) : result.Trim(),
			CauseEventId = Math.Max(0L, causeEventId),
			LocationX = locationX >= 0 ? locationX : 0,
			LocationY = locationY >= 0 ? locationY : 0,
			HasLocation = locationX >= 0 && locationY >= 0
		});
		if (added && mirrorToWorldLog && ShouldMirrorDomainEventToWorldLog(normalizedCategory, normalizedTitle, normalizedBody, iconId))
		{
			XjWorldHistoryRegistry.AddDomainEventLogOnly(normalizedBody, iconId);
		}
	}

	internal static void RecordChronicleEvent(
		long familyId,
		long actorId,
		string eventType,
		string title,
		string body,
		int importance,
		bool isProtected,
		int year,
		string source,
		string actorRealmSnapshot)
	{
		string normalizedTitle = XjDisplayNameSanitizer.Clean(title, string.Empty);
		string normalizedBody = XjDisplayNameSanitizer.Clean(body, string.Empty);
		if (normalizedTitle.Length == 0 && normalizedBody.Length == 0) return;
		if (normalizedBody.Length == 0) normalizedBody = normalizedTitle;
		if (normalizedTitle.Length == 0) normalizedTitle = normalizedBody;

		string actorName = string.Empty;
		long sectId = 0L;
		long cityId = 0L;
		int x = 0;
		int y = 0;
		bool hasLocation = false;
		if (actorId > 0L && XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null)
		{
			actorName = XjStringHelper.ActorName(actor, "未名修士");
			sectId = ResolveActorSectId(actor);
			cityId = actor.city?.data?.id ?? 0L;
			try
			{
				x = (int)Math.Round(actor.current_position.x);
				y = (int)Math.Round(actor.current_position.y);
				hasLocation = true;
			}
			catch (System.Exception xjCaught215) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/History/XjWorldHistoryStore.cs:215", xjCaught215); }
		}

		string iconId = XjEventIconCatalog.ResolveFromWorldHistoryText(normalizedTitle + " " + normalizedBody + " " + eventType);
		string normalizedCategory = ResolveChronicleCategory(eventType, source, actorRealmSnapshot);
		int normalizedImportance = Math.Clamp(importance, 1, 5);
		Add(new XjWorldHistoryArchiveRecord
		{
			Year = year >= 0 ? year : ResolveCurrentYear(),
			EventType = string.IsNullOrWhiteSpace(eventType) ? ResolveEventType(normalizedTitle + " " + normalizedBody, normalizedCategory) : eventType.Trim(),
			Category = normalizedCategory,
			Title = normalizedTitle,
			Body = normalizedBody,
			IconId = ResolveHistoryIcon(iconId, normalizedCategory),
			Importance = normalizedImportance,
			IsProtected = isProtected || importance >= 4,
			VisibilityFlags = ResolveVisibilityFlags(normalizedImportance, actorId, familyId, sectId, normalizedCategory),
			Result = ResolveResult(normalizedTitle + " " + normalizedBody),
			ActorId = Math.Max(0L, actorId),
			ActorName = actorName,
			SectId = Math.Max(0L, sectId),
			SectNameSnapshot = ResolveSectName(sectId),
			FamilyId = Math.Max(0L, familyId),
			FamilyNameSnapshot = ResolveFamilyName(familyId),
			CityId = Math.Max(0L, cityId),
			LocationX = x,
			LocationY = y,
			HasLocation = hasLocation
		});
	}

	/// <summary>
	/// 只读取历史表尾部指定年份的记录。年度成章使用此入口，避免每年复制整段历史或做全表 LINQ。
	/// 年度归约器按年份顺序追加事件，因此通常只需从尾部扫几十条；遇到未来年记录时跳过，
	/// 一旦越过目标年即可停止。
	/// </summary>
	internal static IReadOnlyList<XjWorldHistoryArchiveRecord> ReadTailForYear(int year, int maximumEntries = 128)
	{
		year = Math.Max(0, year);
		int cap = Math.Max(1, maximumEntries);
		if (Entries.Count == 0) return Array.Empty<XjWorldHistoryArchiveRecord>();
		List<XjWorldHistoryArchiveRecord> result = new List<XjWorldHistoryArchiveRecord>(Math.Min(cap, 32));
		for (int i = Entries.Count - 1; i >= 0 && result.Count < cap; i--)
		{
			XjWorldHistoryArchiveRecord item = Entries[i];
			if (item == null) continue;
			if (item.Year > year) continue;
			if (item.Year < year) break;
			result.Add(Clone(item));
		}
		result.Reverse();
		return result;
	}

	internal static IReadOnlyList<XjWorldHistoryArchiveRecord> ReadSnapshot(int maximumEntries = 0)
	{
		int count = maximumEntries > 0 ? Math.Min(Entries.Count, maximumEntries) : Entries.Count;
		if (count == 0) return Array.Empty<XjWorldHistoryArchiveRecord>();
		int start = Entries.Count - count;
		List<XjWorldHistoryArchiveRecord> result = new List<XjWorldHistoryArchiveRecord>(count);
		for (int i = start; i < Entries.Count; i++) result.Add(Clone(Entries[i]));
		return result;
	}

	/// <summary>
	/// 百年世谱按指定年份直接从常驻历史表筛选证据，只克隆命中的记录。
	/// 旧实现先克隆最近8000条再筛选，补卷既浪费分配，也会让早期世纪被
	/// “最近N条”窗口截掉。这里保持一次O(8000)冷路径扫描，但最多只复制
	/// maximumEntries条本世纪证据。
	/// </summary>
	internal static IReadOnlyList<XjWorldHistoryArchiveRecord> ReadSnapshotForYears(int startYear, int endYear, int maximumEntries = 0)
	{
		startYear = Math.Max(0, startYear);
		endYear = Math.Max(startYear, endYear);
		if (Entries.Count == 0) return Array.Empty<XjWorldHistoryArchiveRecord>();
		int cap = maximumEntries > 0 ? maximumEntries : int.MaxValue;
		List<XjWorldHistoryArchiveRecord> result = new List<XjWorldHistoryArchiveRecord>(Math.Min(cap, 256));
		for (int i = Entries.Count - 1; i >= 0 && result.Count < cap; i--)
		{
			XjWorldHistoryArchiveRecord item = Entries[i];
			if (item == null || item.Year < startYear || item.Year > endYear) continue;
			result.Add(Clone(item));
		}
		result.Reverse();
		return result;
	}

	private static long ResolveActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	internal static void ExportArchiveRecords(List<XjWorldHistoryArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < Entries.Count; i++) target.Add(Clone(Entries[i]));
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjWorldHistoryArchiveRecord> source)
	{
		Entries.Clear();
		EventIds.Clear();
		EventSignatures.Clear();
		EventBodySignatures.Clear();
		ClearIndexes();
		_sequence = 0L;
		if (source == null) return;
		bool filteredSuppressedRecords = false;
		bool normalizedLegacyImportance = false;
		int start = Math.Max(0, source.Count - MaxEntries);
		for (int i = start; i < source.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = source[i];
			if (item == null) continue;
			XjWorldHistoryArchiveRecord copy = Clone(item);
			int importedImportance = Math.Clamp(copy.Importance, 1, 5);
			bool importedProtected = copy.IsProtected;
			Normalize(copy);
			if (copy.Importance != importedImportance || copy.IsProtected != importedProtected)
				normalizedLegacyImportance = true;
			if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(copy.Category, copy.EventType, copy.Title, copy.Body))
			{
				filteredSuppressedRecords = true;
				continue;
			}
			string signature = BuildEventSignature(copy);
			string bodySignature = BuildEventBodySignature(copy);
			if (EventSignatures.Contains(signature) || EventBodySignatures.Contains(bodySignature)) continue;
			EventSignatures.Add(signature);
			EventBodySignatures.Add(bodySignature);
			if (copy.EventId <= 0L || !EventIds.Add(copy.EventId))
			{
				copy.EventId = BuildEventId(copy);
				EventIds.Add(copy.EventId);
			}
			Entries.Add(copy);
			IndexRecord(copy);
			_sequence = Math.Max(_sequence, Entries.Count);
		}
		if (PruneLegacyDuplicateProjections() > 0)
		{
			filteredSuppressedRecords = true;
		}
		if (filteredSuppressedRecords || normalizedLegacyImportance)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		XjCodexSnapshotPublisher.MarkDirty();
	}

	internal static int CountLongShuBirthRecords()
	{
		HashSet<long> actorIds = new HashSet<long>();
		int anonymous = 0;
		for (int i = 0; i < Entries.Count; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null) continue;
			string text = (record.Title ?? string.Empty) + " " + (record.Body ?? string.Empty);
			if (!string.Equals(record.EventType, "LongShuBirth", StringComparison.Ordinal)
				&& !(text.IndexOf("出海", StringComparison.Ordinal) >= 0
					&& text.IndexOf("深海龙渊现世", StringComparison.Ordinal) >= 0)) continue;
			if (record.ActorId > 0L) actorIds.Add(record.ActorId);
			else anonymous++;
		}
		return actorIds.Count + anonymous;
	}

	internal static void MarkCenturyProcessed(int startYear, int endYear, IReadOnlyList<long> importantEventIds)
	{
		if (startYear < 0 || endYear < startYear || Entries.Count == 0) return;
		HashSet<long> important = importantEventIds == null
			? new HashSet<long>()
			: new HashSet<long>(importantEventIds);
		bool changed = false;
		for (int i = 0; i < Entries.Count; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || record.Year < startYear || record.Year > endYear
				|| (record.VisibilityFlags & (int)XjHistoryVisibility.CenturyCandidate) == 0) continue;
			string nextStatus;
			if (important.Contains(record.EventId)) nextStatus = XjHistoryCenturyStatus.Referenced;
			else if (record.IsProtected || record.Importance >= 4) nextStatus = XjHistoryCenturyStatus.Permanent;
			else nextStatus = XjHistoryCenturyStatus.Compressed;
			if (!string.Equals(record.CenturyStatus, nextStatus, StringComparison.Ordinal))
			{
				record.CenturyStatus = nextStatus;
				changed = true;
			}
			// 史册压缩只改变百年世谱引用状态，不再把已收录的天下纪事从九类筛选中隐藏。
		}
		if (!changed) return;
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	internal static int ClearLowImportance()
	{
		return ClearByFilter(XjWorldHistoryCategory.All, 2, true);
	}

	internal static int CountClearableByFilter(string category, int maxImportance, bool keepProtected)
	{
		string normalizedCategory = NormalizeCategory(category);
		int cappedImportance = Math.Clamp(maxImportance, 1, 5);
		int count = 0;
		for (int i = 0; i < Entries.Count; i++)
		{
			if (CanClearRecord(Entries[i], normalizedCategory, cappedImportance, keepProtected)) count++;
		}
		return count;
	}

	internal static int ClearByFilter(string category, int maxImportance, bool keepProtected)
	{
		string normalizedCategory = NormalizeCategory(category);
		int cappedImportance = Math.Clamp(maxImportance, 1, 5);
		int removed = 0;
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (!CanClearRecord(record, normalizedCategory, cappedImportance, keepProtected)) continue;
			EventIds.Remove(record.EventId);
			EventSignatures.Remove(BuildEventSignature(record));
			EventBodySignatures.Remove(BuildEventBodySignature(record));
			UnindexRecord(record);
			Entries.RemoveAt(i);
			removed++;
		}
		if (removed > 0)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.World);
		}
		return removed;
	}

	private static bool CanClearRecord(XjWorldHistoryArchiveRecord record, string normalizedCategory, int cappedImportance, bool keepProtected)
	{
		if (record == null) return false;
		if (keepProtected && record.IsProtected) return false;
		string recordCategory = NormalizeCategory(record.Category);
		if (!string.Equals(normalizedCategory, XjWorldHistoryCategory.All, StringComparison.Ordinal)
			&& !string.Equals(recordCategory, normalizedCategory, StringComparison.Ordinal)) return false;
		return Math.Clamp(record.Importance, 1, 5) <= cappedImportance;
	}

	internal static int PruneSuppressedRecords()
	{
		return PruneSuppressedRecords(int.MaxValue);
	}

	/// <summary>
	/// Bounded retention audit used by the annual memory lane. It visits at most
	/// inspectBudget entries and keeps a cursor between runs, so long worlds do
	/// not repeatedly scan all history in one frame. Null records and records
	/// rejected by the current retention policy are safe to remove because they
	/// are not valid, user-visible history.
	/// </summary>
	internal static int PruneSuppressedRecords(int inspectBudget)
	{
		if (Entries.Count == 0 || inspectBudget <= 0)
		{
			_suppressedPruneCursor = -1;
			return 0;
		}

		int targetChecks = Math.Min(inspectBudget, Entries.Count);
		if (_suppressedPruneCursor < 0 || _suppressedPruneCursor >= Entries.Count)
		{
			_suppressedPruneCursor = Entries.Count - 1;
		}

		int removed = 0;
		for (int inspected = 0; inspected < targetChecks && Entries.Count > 0; inspected++)
		{
			if (_suppressedPruneCursor < 0 || _suppressedPruneCursor >= Entries.Count)
			{
				_suppressedPruneCursor = Entries.Count - 1;
			}

			int index = _suppressedPruneCursor;
			XjWorldHistoryArchiveRecord record = Entries[index];
			bool shouldRemove = record == null
				|| !XjHistoryRetentionPolicy.ShouldKeepWorldRecord(
					record.Category,
					record.EventType,
					record.Title,
					record.Body);
			if (shouldRemove)
			{
				if (record != null)
				{
					EventIds.Remove(record.EventId);
					EventSignatures.Remove(BuildEventSignature(record));
					EventBodySignatures.Remove(BuildEventBodySignature(record));
					UnindexRecord(record);
				}
				Entries.RemoveAt(index);
				removed++;
			}
			_suppressedPruneCursor--;
		}

		if (removed > 0)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.World);
		}
		return removed;
	}

	internal static int PruneSuppressedCraftRecords()
	{
		return PruneSuppressedRecords();
	}

	internal static int PruneRecordsBeforeYear(int minimumYear)
	{
		minimumYear = Math.Max(0, minimumYear);
		if (minimumYear <= 0 || Entries.Count == 0)
		{
			return 0;
		}

		int removed = 0;
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || record.Year <= 0 || record.Year >= minimumYear)
			{
				continue;
			}
			EventIds.Remove(record.EventId);
			EventSignatures.Remove(BuildEventSignature(record));
			EventBodySignatures.Remove(BuildEventBodySignature(record));
			UnindexRecord(record);
			Entries.RemoveAt(i);
			removed++;
		}

		if (removed > 0)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.World);
		}
		return removed;
	}

	internal static void CompactMemory()
	{
		Entries.TrimExcess();
		foreach (List<long> ids in EventIdsByActor.Values) ids?.TrimExcess();
		foreach (List<long> ids in EventIdsByFamily.Values) ids?.TrimExcess();
		foreach (List<long> ids in EventIdsBySect.Values) ids?.TrimExcess();
	}

	internal static void Clear()
	{
		Entries.Clear();
		EventIds.Clear();
		EventSignatures.Clear();
		EventBodySignatures.Clear();
		ClearIndexes();
		_sequence = 0L;
		_suppressedPruneCursor = -1;
		XjCodexSnapshotPublisher.MarkDirty();
	}

	internal static bool EnsureWorldVisibleForAnnouncement(string text, string iconId = null)
	{
		string cleaned = XjDisplayNameSanitizer.Clean(text, string.Empty);
		if (cleaned.Length == 0) return false;
		int currentYear = ResolveCurrentYear();
		string normalizedIcon = XjEventIconCatalog.NormalizeIconId(iconId);
		string requestedCategory = ResolveCategory(normalizedIcon, cleaned, XjWorldHistoryCategory.World);
		int inspected = 0;
		for (int i = Entries.Count - 1; i >= 0 && inspected < 128; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || record.Year != currentYear) continue;
			inspected++;
			bool exact = string.Equals(record.Body, cleaned, StringComparison.Ordinal)
				|| string.Equals(record.Title, cleaned, StringComparison.Ordinal);
			bool compatibleCategory = string.Equals(record.Category, requestedCategory, StringComparison.Ordinal)
				|| string.Equals(record.Category, XjWorldHistoryCategory.World, StringComparison.Ordinal)
				|| string.Equals(requestedCategory, XjWorldHistoryCategory.World, StringComparison.Ordinal);
			int textMatchRank = exact ? 3 : ResolveEventTextMatchRank(cleaned, record.Body, loose: true);
			if (!compatibleCategory || textMatchRank <= 0 || textMatchRank == 1 && inspected > 8) continue;
			if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body)) return false;
			int required = (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate);
			if ((record.VisibilityFlags & required) == required) return true;
			record.VisibilityFlags |= required;
			if (string.IsNullOrWhiteSpace(record.IconId))
			{
				string category = ResolveCategory(normalizedIcon, cleaned, record.Category);
				record.IconId = ResolveHistoryIcon(iconId, category);
			}
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.World);
			return true;
		}
		return false;
	}

	internal static string ResolveEventCategory(string iconId, string text, string requestedCategory = XjWorldHistoryCategory.World)
	{
		return ResolveCategory(XjEventIconCatalog.NormalizeIconId(iconId), text, requestedCategory);
	}

	private static bool Add(XjWorldHistoryArchiveRecord record)
	{
		if (record == null || string.IsNullOrWhiteSpace(record.Body)) return false;
		Normalize(record);
		if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body)) return false;
		string signature = BuildEventSignature(record);
		string bodySignature = BuildEventBodySignature(record);
		if (EventSignatures.Contains(signature) || EventBodySignatures.Contains(bodySignature)) return false;
		if (TryMergeIncomingProjection(record, out bool incomingBecameCanonical)) return incomingBecameCanonical;
		EventSignatures.Add(signature);
		EventBodySignatures.Add(bodySignature);
		record.EventId = BuildEventId(record);
		if (!EventIds.Add(record.EventId))
		{
			EventSignatures.Remove(signature);
			EventBodySignatures.Remove(bodySignature);
			return false;
		}
		Entries.Add(record);
		IndexRecord(record);
		Trim();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty();
		return true;
	}

	private static bool ShouldMirrorDomainEventToWorldLog(string category, string title, string body, string iconId)
	{
		string text = string.IsNullOrWhiteSpace(body) ? title : body;
		return XjHistoryRetentionPolicy.ShouldKeepWorldRecord(category, string.Empty, title, body)
			&& XjBroadcastSystem.ShouldShowAnnouncement(text, iconId);
	}


	private static bool HasAny(string text, params string[] needles)
	{
		if (string.IsNullOrWhiteSpace(text) || needles == null) return false;
		for (int i = 0; i < needles.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(needles[i])
				&& text.IndexOf(needles[i], StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static long BuildEventId(XjWorldHistoryArchiveRecord record)
	{
		long seed = ++_sequence;
		string salt = record.Year + "|" + record.ActorId + "|" + record.IconId + "|" + record.Body + "|" + seed;
		long id = XjDeterministicHash.PositiveHash(seed, salt);
		if (id <= 0L) id = seed;
		while (EventIds.Contains(id)) id = id == long.MaxValue ? 1L : id + 1L;
		return id;
	}

	private static void Trim()
	{
		if (Entries.Count <= MaxEntries) return;

		// The old strict-cap loop scanned up to 8k records and shifted the List once
		// for every new history event after saturation. Long-running saves therefore
		// paid O(n) retention cost continuously. Compact a batch in one linear rebuild
		// and keep a hysteresis window before the next trim. Low-importance unprotected
		// history is still discarded first, then other unprotected history, and only
		// finally protected history when absolutely necessary.
		int targetCount = Math.Max(0, MaxEntries - TrimBatchSize);
		int removeNeeded = Math.Max(Entries.Count - targetCount, 1);
		TrimRemovalIds.Clear();

		CollectTrimCandidates(removeNeeded, lowImportanceOnly: true, unprotectedOnly: true);
		CollectTrimCandidates(removeNeeded, lowImportanceOnly: false, unprotectedOnly: true);
		CollectTrimCandidates(removeNeeded, lowImportanceOnly: false, unprotectedOnly: false);

		if (TrimRemovalIds.Count == 0) return;
		TrimSurvivors.Clear();
		for (int i = 0; i < Entries.Count; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record != null && TrimRemovalIds.Contains(record.EventId))
			{
				EventIds.Remove(record.EventId);
				EventSignatures.Remove(BuildEventSignature(record));
				EventBodySignatures.Remove(BuildEventBodySignature(record));
				UnindexRecord(record);
				continue;
			}
			TrimSurvivors.Add(record);
		}

		Entries.Clear();
		Entries.AddRange(TrimSurvivors);
		TrimSurvivors.Clear();
		TrimRemovalIds.Clear();
	}

	private static void CollectTrimCandidates(int removeNeeded, bool lowImportanceOnly, bool unprotectedOnly)
	{
		if (TrimRemovalIds.Count >= removeNeeded) return;
		for (int i = 0; i < Entries.Count && TrimRemovalIds.Count < removeNeeded; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null) continue;
			if (TrimRemovalIds.Contains(record.EventId)) continue;
			if (unprotectedOnly && record.IsProtected) continue;
			if (lowImportanceOnly && record.Importance > 2) continue;
			TrimRemovalIds.Add(record.EventId);
		}
	}

	private static string NormalizeCategory(string category)
	{
		string value = (category ?? string.Empty).Trim();
		if (string.Equals(value, "家族", StringComparison.Ordinal)) return XjWorldHistoryCategory.Family;
		if (string.Equals(value, "资源", StringComparison.Ordinal) || string.Equals(value, "传承", StringComparison.Ordinal)) return XjWorldHistoryCategory.Inheritance;
		if (string.Equals(value, "百艺", StringComparison.Ordinal) || string.Equals(value, "四艺", StringComparison.Ordinal)) return XjWorldHistoryCategory.Craft;
		if (string.Equals(value, "高境", StringComparison.Ordinal) || string.Equals(value, "修行", StringComparison.Ordinal)) return XjWorldHistoryCategory.Cultivation;
		if (string.Equals(value, "洞天", StringComparison.Ordinal) || string.Equals(value, "机缘", StringComparison.Ordinal)) return XjWorldHistoryCategory.Opportunity;
		if (string.Equals(value, "阴司", StringComparison.Ordinal) || string.Equals(value, "生死", StringComparison.Ordinal)) return XjWorldHistoryCategory.LifeAndDeath;
		if (string.Equals(value, "恶行", StringComparison.Ordinal) || string.Equals(value, "恩怨", StringComparison.Ordinal)) return XjWorldHistoryCategory.Vendetta;
		for (int i = 0; i < XjWorldHistoryCategory.Ordered.Length; i++)
		{
			string known = XjWorldHistoryCategory.Ordered[i];
			if (!string.Equals(known, XjWorldHistoryCategory.All, StringComparison.Ordinal)
				&& string.Equals(value, known, StringComparison.Ordinal)) return known;
		}
		return XjWorldHistoryCategory.World;
	}

	private static string ResolveHistoryIcon(string iconId, string category)
	{
		string normalizedIcon = XjEventIconCatalog.NormalizeIconId(iconId);
		return string.IsNullOrWhiteSpace(normalizedIcon)
			? XjEventIconCatalog.ResolveCategoryIconId(NormalizeCategory(category))
			: normalizedIcon;
	}

	private static string ResolveCategory(string iconId, string text, string requestedCategory)
	{
		string id = XjEventIconCatalog.NormalizeIconId(iconId);
		string normalizedRequested = NormalizeCategory(requestedCategory);
		// 百艺领域事件必须优先采用生产者给出的类别。角色名常带“炼气/筑基”等
		// 境界后缀，若继续按整段文本猜测，会把护身符等产出误归到修行。
		if (string.Equals(normalizedRequested, XjWorldHistoryCategory.Craft, StringComparison.Ordinal))
		{
			return XjWorldHistoryCategory.Craft;
		}
		if (HasAny(text, "陨落", "坐化", "身陨", "死亡", "殒命", "绝嗣")) return XjWorldHistoryCategory.LifeAndDeath;
		if (HasAny(text, "阴司", "幽冥府君", "阴司使者", "道胎", "追索", "金性妖邪")) return XjWorldHistoryCategory.LifeAndDeath;
		if (HasAny(text, "截留", "夺法", "夺灵物", "打压", "算计", "欠缴", "压榨", "血祭", "人丹", "送去给丹师", "恶名")) return XjWorldHistoryCategory.Evil;
		if (HasAny(text, "开宗", "宗门", "山门", "宗主", "峰主", "山峰", "压服", "宣战", "灭宗", "另立", "重立", "护宗大阵", "宗门大阵")) return XjWorldHistoryCategory.Sect;
		if (HasAny(text, "洞天", "福地", "秘境", "玄韬", "勘定太虚", "布置玄韬", "滋养空间", "稳定入口")) return XjWorldHistoryCategory.SecretRealm;
		if (HasAny(text, "炼丹", "丹药", "丹方", "炸炉", "炼器", "法器", "灵宝", "法宝", "符箓", "符师", "阵法", "阵师", "大阵", "四艺")) return XjWorldHistoryCategory.Craft;
		if (HasAny(text, "入库", "归入", "纳气仓库", "功法", "求金法", "采气法", "药材", "灵物", "金性遗留", "宗门底蕴", "家族仓库")) return XjWorldHistoryCategory.Resource;
		if (HasAny(text,
			"金丹", "神丹", "结璘", "紫府", "真人", "真君", "羽士", "黄冠", "空证",
			"筑基", "炼气", "突破", "求金", "求证", "果位", "正位", "余位", "闰位",
			"神妙圆满", "转世", "登名石")) return XjWorldHistoryCategory.Cultivation;
		if (!string.Equals(normalizedRequested, XjWorldHistoryCategory.World, StringComparison.Ordinal)) return normalizedRequested;
		if (string.Equals(id, XjEventIconCatalog.ZongMenCreation, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.ZongMenChongTu, StringComparison.Ordinal)) return XjWorldHistoryCategory.Sect;
		if (string.Equals(id, XjEventIconCatalog.DongTianOpen, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.DongTianClose, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.DongTianDeath, StringComparison.Ordinal)) return XjWorldHistoryCategory.SecretRealm;
		if (string.Equals(id, XjEventIconCatalog.YinSiAppear, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.YinSiLeave, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinDanDemon, StringComparison.Ordinal)) return XjWorldHistoryCategory.YinSi;
		if (string.Equals(id, XjEventIconCatalog.ZiFuUpgrade, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinDanUpgrade, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinDanFail, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.Jielin, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.HighRealmDeath, StringComparison.Ordinal)) return XjWorldHistoryCategory.HighRealm;
		if (string.Equals(id, XjEventIconCatalog.LianDan, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.DanFangAcquire, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.FaBaoCreation, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.HighDanYao, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.ZhaLu, StringComparison.Ordinal)) return XjWorldHistoryCategory.Craft;
		if (string.Equals(id, XjEventIconCatalog.GongFaAcquire, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.QiuJinFaAcquire, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.LingWuAppear, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinXingLegacy, StringComparison.Ordinal)) return XjWorldHistoryCategory.Family;
		return XjWorldHistoryCategory.World;
	}

	private static int ResolveImportance(string iconId, string text, string category, int requestedImportance = 0)
	{
		string id = XjEventIconCatalog.NormalizeIconId(iconId);
		int resolved = 0;
		if (HasAny(text,
			"证得金丹", "成就金丹", "服气真君", "金性妖邪", "阴司降临", "道胎垂眸",
			"开宗", "灭宗", "宗门宣战", "高境压服", "权柄之争",
			"正位承继", "夺取正位", "果位正位", "奉本道正统", "道正", "权柄融成", "正式失柄", "永久失落",
			"郁仪仙入位", "结璘仙入位", "特殊仙身入位")) resolved = 5;
		else if (HasAny(text,
			"晋升紫府", "求金失败", "洞天现世", "洞天争夺", "重立山门", "另立新宗", "人丹", "夺法", "截留", "算计",
			"位序扩充", "余位开辟", "闰位开辟", "神通易象", "神通退显", "神通随柄",
			"权柄裂解", "权柄归还", "权柄易位", "权柄归身", "权柄离身", "权柄显化", "权柄潜藏")) resolved = 4;
		else if (HasAny(text, "灵宝", "法宝", "护宗大阵", "宗门大阵", "真君讲道", "真人开坛", "金性遗留")) resolved = 3;
		if (string.Equals(id, XjEventIconCatalog.JinDanUpgrade, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinDanDemon, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.YinSiAppear, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.ZongMenCreation, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.ZongMenChongTu, StringComparison.Ordinal)) resolved = Math.Max(resolved, 5);
		if (string.Equals(id, XjEventIconCatalog.Jielin, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.DongTianOpen, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinDanFail, StringComparison.Ordinal)) resolved = Math.Max(resolved, 4);
		if (string.Equals(id, XjEventIconCatalog.ZiFuUpgrade, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.HighRealmDeath, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.FaBaoCreation, StringComparison.Ordinal)
			|| string.Equals(id, XjEventIconCatalog.JinXingLegacy, StringComparison.Ordinal)) resolved = Math.Max(resolved, 3);
		if (resolved == 0 && !string.IsNullOrEmpty(id)) resolved = 2;
		if (resolved == 0) resolved = 1;
		if (requestedImportance > 0) resolved = Math.Max(resolved, Math.Clamp(requestedImportance, 1, 5));
		if (string.Equals(category, XjWorldHistoryCategory.Evil, StringComparison.Ordinal)) resolved = Math.Max(resolved, 3);
		if (string.Equals(category, XjWorldHistoryCategory.Resource, StringComparison.Ordinal)
			&& resolved > 2
			&& !HasAny(text, "求金法", "金性遗留", "重宝", "灵物")) resolved = 2;
		return Math.Clamp(resolved, 1, 5);
	}

	private static string ResolveChronicleCategory(string eventType, string source, string actorRealmSnapshot)
	{
		string text = ((eventType ?? string.Empty) + "|" + (source ?? string.Empty) + "|" + (actorRealmSnapshot ?? string.Empty)).ToLowerInvariant();
		if (text.Contains("zongmen") || text.Contains("sect")) return XjWorldHistoryCategory.Sect;
		if (text.Contains("dongtian") || text.Contains("secretrealm")) return XjWorldHistoryCategory.SecretRealm;
		if (text.Contains("yinsi") || text.Contains("jinxingyaoxie")) return XjWorldHistoryCategory.YinSi;
		if (text.Contains("alchemy") || text.Contains("liandan") || text.Contains("talisman") || text.Contains("formation") || text.Contains("craft")) return XjWorldHistoryCategory.Craft;
		if (text.Contains("jindan") || text.Contains("jielin") || text.Contains("zifu")
			|| text.Contains("fuqi") || text.Contains("zhenren") || text.Contains("zhenjun")
			|| text.Contains("kongzheng") || text.Contains("breakthrough") || text.Contains("death")) return XjWorldHistoryCategory.HighRealm;
		if (text.Contains("gongfa") || text.Contains("technique") || text.Contains("caiqi") || text.Contains("fabao") || text.Contains("lingwu") || text.Contains("family")) return XjWorldHistoryCategory.Family;
		return XjWorldHistoryCategory.Family;
	}

	internal static IReadOnlyList<long> ReadEventIdsForActor(long actorId) => ReadIndexedIds(EventIdsByActor, actorId);
	internal static IReadOnlyList<long> ReadEventIdsForFamily(long familyId) => ReadIndexedIds(EventIdsByFamily, familyId);
	internal static IReadOnlyList<long> ReadEventIdsForSect(long sectId) => ReadIndexedIds(EventIdsBySect, sectId);

	private static IReadOnlyList<long> ReadIndexedIds(Dictionary<long, List<long>> index, long key)
	{
		if (key <= 0L || !index.TryGetValue(key, out List<long> ids) || ids.Count == 0) return Array.Empty<long>();
		return new List<long>(ids);
	}

	private static void IndexRecord(XjWorldHistoryArchiveRecord record)
	{
		if (record == null || record.EventId <= 0L) return;
		AddIndex(EventIdsByActor, record.ActorId, record.EventId);
		AddIndex(EventIdsByActor, record.RelatedActorId, record.EventId);
		AddIndex(EventIdsByFamily, record.FamilyId, record.EventId);
		AddIndex(EventIdsByFamily, record.RelatedFamilyId, record.EventId);
		AddIndex(EventIdsBySect, record.SectId, record.EventId);
		AddIndex(EventIdsBySect, record.RelatedSectId, record.EventId);
	}

	private static void UnindexRecord(XjWorldHistoryArchiveRecord record)
	{
		if (record == null || record.EventId <= 0L) return;
		RemoveIndex(EventIdsByActor, record.ActorId, record.EventId);
		RemoveIndex(EventIdsByActor, record.RelatedActorId, record.EventId);
		RemoveIndex(EventIdsByFamily, record.FamilyId, record.EventId);
		RemoveIndex(EventIdsByFamily, record.RelatedFamilyId, record.EventId);
		RemoveIndex(EventIdsBySect, record.SectId, record.EventId);
		RemoveIndex(EventIdsBySect, record.RelatedSectId, record.EventId);
	}

	private static void AddIndex(Dictionary<long, List<long>> index, long key, long eventId)
	{
		if (key <= 0L || eventId <= 0L) return;
		if (!index.TryGetValue(key, out List<long> ids))
		{
			ids = new List<long>();
			index[key] = ids;
		}
		if (ids.Count == 0 || ids[ids.Count - 1] != eventId) ids.Add(eventId);
	}

	private static void RemoveIndex(Dictionary<long, List<long>> index, long key, long eventId)
	{
		if (key <= 0L || eventId <= 0L || !index.TryGetValue(key, out List<long> ids)) return;
		ids.Remove(eventId);
		if (ids.Count == 0) index.Remove(key);
	}

	private static void ClearIndexes()
	{
		EventIdsByActor.Clear();
		EventIdsByFamily.Clear();
		EventIdsBySect.Clear();
	}

	private static int ResolveVisibilityFlags(int importance, long actorId, long familyId, long sectId, string category)
	{
		XjHistoryVisibility flags = XjHistoryVisibility.None;
		if (actorId > 0L) flags |= XjHistoryVisibility.Personal;
		if (familyId > 0L) flags |= XjHistoryVisibility.Family;
		if (sectId > 0L) flags |= XjHistoryVisibility.Sect;
		if (importance >= 2 || (actorId <= 0L && familyId <= 0L && sectId <= 0L)
			|| string.Equals(category, XjWorldHistoryCategory.World, StringComparison.Ordinal)) flags |= XjHistoryVisibility.World;
		if (importance >= 3 || familyId > 0L || sectId > 0L) flags |= XjHistoryVisibility.CenturyCandidate;
		return (int)flags;
	}

	private static string ResolveEventType(string text, string category)
	{
		string value = text ?? string.Empty;
		if (HasAny(value, "陨落", "坐化", "身陨", "死亡", "绝嗣")) return "Death";
		if (HasAny(value, "突破", "晋升", "证得", "成就", "筑基", "紫府", "真人", "金丹", "真君", "羽士", "结璘", "空证")) return "Breakthrough";
		if (HasAny(value, "继任", "宗主", "家主", "峰主", "开宗")) return "Office";
		if (HasAny(value, "重宝", "功法", "求金法", "法宝", "灵宝", "入库", "传承")) return "Inheritance";
		if (HasAny(value, "宣战", "灭宗", "斗法", "血仇", "寻仇", "棋子", "棋局")) return "Conflict";
		if (HasAny(value, "洞天", "福地", "奇遇")) return "Opportunity";
		if (string.Equals(category, XjWorldHistoryCategory.Craft, StringComparison.Ordinal)) return "Craft";
		if (string.Equals(category, XjWorldHistoryCategory.Family, StringComparison.Ordinal)) return "Family";
		if (string.Equals(category, XjWorldHistoryCategory.Sect, StringComparison.Ordinal)) return "Sect";
		return "Event";
	}

	private static string ResolveResult(string text)
	{
		string value = text ?? string.Empty;
		if (HasAny(value, "失败", "未成", "受挫", "被破")) return XjHistoryResult.Failure;
		if (HasAny(value, "陨落", "坐化", "身陨", "死亡", "绝嗣")) return XjHistoryResult.Death;
		if (HasAny(value, "易主", "转入", "归入", "交付", "授予")) return XjHistoryResult.Transfer;
		if (HasAny(value, "成功", "有成", "证得", "成就", "晋升", "建成", "复振")) return XjHistoryResult.Success;
		return XjHistoryResult.Change;
	}

	private static string ResolveActorName(long actorId)
	{
		if (actorId <= 0L) return string.Empty;
		try
		{
			return XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null
				? XjStringHelper.ActorName(actor, "未名修士")
				: string.Empty;
		}
		catch { return string.Empty; }
	}

	private static string ResolveFamilyName(long familyId)
	{
		if (familyId <= 0L) return string.Empty;
		try { return XjFamilyDisplayNameResolver.Resolve(familyId) ?? string.Empty; } catch { return string.Empty; }
	}

	private static string ResolveSectName(long sectId)
	{
		if (sectId <= 0L) return string.Empty;
		try
		{
			return XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord record) && record != null
				? record.Name ?? string.Empty
				: string.Empty;
		}
		catch { return string.Empty; }
	}

	private static string ResolveCityName(City city)
	{
		try { return city?.data == null ? string.Empty : city.data.name ?? string.Empty; } catch { return string.Empty; }
	}

	private static int ResolveCurrentYear()
	{
		try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
		catch { return 0; }
	}

	private static void Normalize(XjWorldHistoryArchiveRecord record)
	{
		int importedSchemaVersion = record.SchemaVersion;
		record.SchemaVersion = XjWorldHistorySchema.CurrentVersion;
		record.Year = Math.Max(0, record.Year);
		record.Category = NormalizeCategory(record.Category);
		record.Title = record.Title?.Trim() ?? string.Empty;
		record.Body = record.Body?.Trim() ?? string.Empty;
		if (string.IsNullOrWhiteSpace(record.Title)) record.Title = record.Body;
		record.IconId = XjEventIconCatalog.NormalizeIconId(record.IconId);
		record.Importance = Math.Clamp(record.Importance, 1, 5);
		record.EventType = string.IsNullOrWhiteSpace(record.EventType) ? ResolveEventType(record.Title + " " + record.Body, record.Category) : record.EventType.Trim();
		// 旧档史实也按当前位格表抬升下限：不会降低原有重要度，只修正
		// “道正1星、位序扩充2星、神通随柄3星”等历史遗留。
		record.Importance = ResolveImportance(
			record.IconId, record.Title + " " + record.Body, record.Category, record.Importance);
		record.IsProtected = record.IsProtected || record.Importance >= 4;
		record.ActorName = record.ActorName?.Trim() ?? string.Empty;
		record.RelatedActorName = record.RelatedActorName?.Trim() ?? string.Empty;
		record.SectNameSnapshot = NormalizeArchivedSectName(record.SectNameSnapshot);
		record.RelatedSectNameSnapshot = NormalizeArchivedSectName(record.RelatedSectNameSnapshot);
		record.FamilyNameSnapshot = record.FamilyNameSnapshot?.Trim() ?? string.Empty;
		record.RelatedFamilyNameSnapshot = record.RelatedFamilyNameSnapshot?.Trim() ?? string.Empty;
		record.CityNameSnapshot = record.CityNameSnapshot?.Trim() ?? string.Empty;
		if (record.ActorId > 0L && string.IsNullOrWhiteSpace(record.ActorName)) record.ActorName = ResolveActorName(record.ActorId);
		if (record.RelatedActorId > 0L && string.IsNullOrWhiteSpace(record.RelatedActorName)) record.RelatedActorName = ResolveActorName(record.RelatedActorId);
		if (record.SectId > 0L && string.IsNullOrWhiteSpace(record.SectNameSnapshot)) record.SectNameSnapshot = ResolveSectName(record.SectId);
		if (record.RelatedSectId > 0L && string.IsNullOrWhiteSpace(record.RelatedSectNameSnapshot)) record.RelatedSectNameSnapshot = ResolveSectName(record.RelatedSectId);
		if (record.FamilyId > 0L && string.IsNullOrWhiteSpace(record.FamilyNameSnapshot)) record.FamilyNameSnapshot = ResolveFamilyName(record.FamilyId);
		if (record.RelatedFamilyId > 0L && string.IsNullOrWhiteSpace(record.RelatedFamilyNameSnapshot)) record.RelatedFamilyNameSnapshot = ResolveFamilyName(record.RelatedFamilyId);
		XjHistoryRetentionPolicy.NormalizeRecordScope(record);
		if (record.VisibilityFlags == 0)
		{
			record.VisibilityFlags = ResolveVisibilityFlags(record.Importance, record.ActorId, record.FamilyId, record.SectId, record.Category);
		}
		else if (importedSchemaVersion > 0 && importedSchemaVersion < 3
			&& XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body))
		{
			// 0.9.5迁移：旧史册压缩曾错误移除天下可见位。恢复真实低频事件的九类筛选可见性。
			int restored = ResolveVisibilityFlags(record.Importance, record.ActorId, record.FamilyId, record.SectId, record.Category);
			record.VisibilityFlags |= restored & (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate);
		}
		if (record.ActorId > 0L || record.RelatedActorId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Personal;
		if (record.FamilyId > 0L || record.RelatedFamilyId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Family;
		if (record.SectId > 0L || record.RelatedSectId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Sect;
		if (IsCaiQiFaHistoryRecord(record) || IsGradeFourGongFaHistoryRecord(record))
		{
			record.VisibilityFlags &= ~(int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect);
		}
		if (string.IsNullOrWhiteSpace(record.Result)) record.Result = ResolveResult(record.Title + " " + record.Body);
		if (string.IsNullOrWhiteSpace(record.CenturyStatus)) record.CenturyStatus = record.IsProtected ? XjHistoryCenturyStatus.Permanent : XjHistoryCenturyStatus.Pending;
	}

	private static bool IsCaiQiFaHistoryRecord(XjWorldHistoryArchiveRecord record)
	{
		if (record == null) return false;
		string text = (record.EventType ?? string.Empty) + " "
			+ (record.Title ?? string.Empty) + " "
			+ (record.Body ?? string.Empty);
		return text.IndexOf("CaiQiFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("采气法", StringComparison.Ordinal) >= 0;
	}

	private static bool IsGradeFourGongFaHistoryRecord(XjWorldHistoryArchiveRecord record)
	{
		if (record == null) return false;
		string type = record.EventType ?? string.Empty;
		string text = type + " " + (record.Title ?? string.Empty) + " " + (record.Body ?? string.Empty);
		bool isGongFa = type.IndexOf("GongFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| type.IndexOf("TechniqueRecovered", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("功法", StringComparison.Ordinal) >= 0;
		return isGongFa && (text.IndexOf("一品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("1品", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("二品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("2品", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("三品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("3品", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("四品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("4品", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("五品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("5品", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	private static string NormalizeArchivedSectName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0) return string.Empty;
		if (text.StartsWith("宗门#", StringComparison.Ordinal)
			|| text.StartsWith("Sect#", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("sect_", StringComparison.OrdinalIgnoreCase)) return string.Empty;
		return XjDisplayNameSanitizer.Clean(text, string.Empty);
	}


	private static bool TryMergeIncomingProjection(
		XjWorldHistoryArchiveRecord incoming,
		out bool incomingBecameCanonical)
	{
		incomingBecameCanonical = false;
		if (incoming == null || Entries.Count == 0) return false;
		bool incomingProjection = IsLegacyProjectionRecord(incoming);
		int inspected = 0;
		for (int i = Entries.Count - 1; i >= 0 && inspected < 128; i--)
		{
			XjWorldHistoryArchiveRecord existing = Entries[i];
			if (existing == null || existing.Year != incoming.Year) continue;
			inspected++;
			bool existingProjection = IsLegacyProjectionRecord(existing);
			bool projectionPair = incomingProjection != existingProjection;
			string incomingTextKey = NormalizeEventTextForSemanticDedup(incoming.Body);
			string existingTextKey = NormalizeEventTextForSemanticDedup(existing.Body);
			bool exactProjectionText = incomingTextKey.Length > 0
				&& string.Equals(incomingTextKey, existingTextKey, StringComparison.Ordinal);
			bool mirrorPair = (IsAnnouncementMirror(incoming) || IsAnnouncementMirror(existing)) && exactProjectionText;
			bool actorWorldProjectionPair = incomingProjection && existingProjection && exactProjectionText
				&& (HasNoSubjectIdentity(incoming) || HasNoSubjectIdentity(existing));
			if (!projectionPair && !mirrorPair && !actorWorldProjectionPair) continue;
			if (!AreLikelySameEventRecords(incoming, existing)) continue;
			int matchRank = ResolveRecordTextMatchRank(incoming, existing);
			if (matchRank <= 0 || matchRank == 1 && inspected > 8) continue;

			int incomingScore = ResolveRecordRichness(incoming);
			int existingScore = ResolveRecordRichness(existing);
			if (incomingScore > existingScore)
			{
				ReplaceRecordWithRicherProjection(existing, incoming);
				incomingBecameCanonical = true;
			}
			else
			{
				MergeDuplicateMetadata(existing, incoming);
			}
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty();
			return true;
		}
		return false;
	}

	private static void ReplaceRecordWithRicherProjection(
		XjWorldHistoryArchiveRecord target,
		XjWorldHistoryArchiveRecord incoming)
	{
		if (target == null || incoming == null) return;
		long eventId = target.EventId;
		string centuryStatus = target.CenturyStatus;
		XjWorldHistoryArchiveRecord previous = Clone(target);
		EventSignatures.Remove(BuildEventSignature(target));
		EventBodySignatures.Remove(BuildEventBodySignature(target));
		UnindexRecord(target);

		target.SchemaVersion = incoming.SchemaVersion;
		target.Year = incoming.Year;
		target.EventType = incoming.EventType;
		target.Category = incoming.Category;
		target.Title = incoming.Title;
		target.Body = incoming.Body;
		target.IconId = incoming.IconId;
		target.Importance = incoming.Importance;
		target.IsProtected = incoming.IsProtected;
		target.VisibilityFlags = incoming.VisibilityFlags;
		target.Result = incoming.Result;
		target.CauseEventId = incoming.CauseEventId;
		target.ActorId = incoming.ActorId;
		target.ActorName = incoming.ActorName;
		target.RelatedActorId = incoming.RelatedActorId;
		target.RelatedActorName = incoming.RelatedActorName;
		target.SectId = incoming.SectId;
		target.SectNameSnapshot = incoming.SectNameSnapshot;
		target.RelatedSectId = incoming.RelatedSectId;
		target.RelatedSectNameSnapshot = incoming.RelatedSectNameSnapshot;
		target.FamilyId = incoming.FamilyId;
		target.FamilyNameSnapshot = incoming.FamilyNameSnapshot;
		target.RelatedFamilyId = incoming.RelatedFamilyId;
		target.RelatedFamilyNameSnapshot = incoming.RelatedFamilyNameSnapshot;
		target.CityId = incoming.CityId;
		target.CityNameSnapshot = incoming.CityNameSnapshot;
		target.LocationX = incoming.LocationX;
		target.LocationY = incoming.LocationY;
		target.HasLocation = incoming.HasLocation;
		target.EventId = eventId;
		target.CenturyStatus = string.IsNullOrWhiteSpace(centuryStatus)
			? incoming.CenturyStatus
			: centuryStatus;
		MergeDuplicateMetadata(target, previous);
		Normalize(target);
		EventSignatures.Add(BuildEventSignature(target));
		EventBodySignatures.Add(BuildEventBodySignature(target));
		IndexRecord(target);
	}

	private static int PruneLegacyDuplicateProjections()
	{
		if (Entries.Count < 2) return 0;
		Dictionary<int, List<XjWorldHistoryArchiveRecord>> byYear = new Dictionary<int, List<XjWorldHistoryArchiveRecord>>();
		for (int i = 0; i < Entries.Count; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null) continue;
			if (!byYear.TryGetValue(record.Year, out List<XjWorldHistoryArchiveRecord> list))
			{
				list = new List<XjWorldHistoryArchiveRecord>();
				byYear[record.Year] = list;
			}
			list.Add(record);
		}

		HashSet<long> removeIds = new HashSet<long>();
		foreach (List<XjWorldHistoryArchiveRecord> yearRecords in byYear.Values)
		{
			for (int i = 0; i < yearRecords.Count; i++)
			{
				XjWorldHistoryArchiveRecord projection = yearRecords[i];
				if (projection == null || !IsLegacyProjectionRecord(projection)) continue;
				XjWorldHistoryArchiveRecord best = null;
				int bestScore = int.MinValue;
				for (int j = 0; j < yearRecords.Count; j++)
				{
					if (i == j) continue;
					XjWorldHistoryArchiveRecord candidate = yearRecords[j];
					if (candidate == null || removeIds.Contains(candidate.EventId)) continue;
					if (IsLegacyProjectionRecord(candidate))
					{
						bool mirrorCanMerge = IsAnnouncementMirror(projection) && !IsAnnouncementMirror(candidate);
						bool exactActorWorldPair = (HasNoSubjectIdentity(projection) || HasNoSubjectIdentity(candidate))
							&& string.Equals(
								NormalizeEventTextForSemanticDedup(projection.Body),
								NormalizeEventTextForSemanticDedup(candidate.Body),
								StringComparison.Ordinal);
						if (!mirrorCanMerge && !exactActorWorldPair) continue;
					}
					if (!AreLikelySameEventRecords(projection, candidate)) continue;
					int matchRank = ResolveRecordTextMatchRank(projection, candidate);
					int distance = Math.Abs(i - j);
					if (matchRank <= 0 || matchRank == 1 && distance > 8) continue;
					int score = matchRank * 100000 - distance * 100 + ResolveRecordRichness(candidate);
					if (score <= bestScore) continue;
					best = candidate;
					bestScore = score;
				}
				if (best == null || ResolveRecordRichness(best) < ResolveRecordRichness(projection)) continue;
				MergeDuplicateMetadata(best, projection);
				removeIds.Add(projection.EventId);
			}
		}

		if (removeIds.Count == 0) return 0;
		int removed = 0;
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || !removeIds.Contains(record.EventId)) continue;
			Entries.RemoveAt(i);
			removed++;
		}
		RebuildDerivedIndexes();
		return removed;
	}

	private static bool IsLegacyProjectionRecord(XjWorldHistoryArchiveRecord record)
	{
		if (record == null) return false;
		if (IsAnnouncementMirror(record)) return true;
		if (!string.Equals(record.Title, record.Body, StringComparison.Ordinal)) return false;
		string type = record.EventType ?? string.Empty;
		return string.Equals(type, "Event", StringComparison.Ordinal)
			|| string.Equals(type, "Death", StringComparison.Ordinal)
			|| string.Equals(type, "Breakthrough", StringComparison.Ordinal)
			|| string.Equals(type, "Office", StringComparison.Ordinal)
			|| string.Equals(type, "Inheritance", StringComparison.Ordinal)
			|| string.Equals(type, "Conflict", StringComparison.Ordinal)
			|| string.Equals(type, "Opportunity", StringComparison.Ordinal)
			|| string.Equals(type, "Craft", StringComparison.Ordinal)
			|| string.Equals(type, "Family", StringComparison.Ordinal)
			|| string.Equals(type, "Sect", StringComparison.Ordinal);
	}

	private static bool HasNoSubjectIdentity(XjWorldHistoryArchiveRecord record)
	{
		return record == null
			|| record.ActorId <= 0L
				&& record.RelatedActorId <= 0L
				&& record.FamilyId <= 0L
				&& record.RelatedFamilyId <= 0L
				&& record.SectId <= 0L
				&& record.RelatedSectId <= 0L
				&& record.CityId <= 0L;
	}

	private static bool IsAnnouncementMirror(XjWorldHistoryArchiveRecord record)
	{
		return record != null
			&& !string.IsNullOrWhiteSpace(record.EventType)
			&& record.EventType.StartsWith("AnnouncementMirror:", StringComparison.Ordinal);
	}

	private static bool AreLikelySameEventRecords(XjWorldHistoryArchiveRecord left, XjWorldHistoryArchiveRecord right)
	{
		if (left == null || right == null || left.Year != right.Year) return false;
		if (left.ActorId > 0L && right.ActorId > 0L && left.ActorId != right.ActorId) return false;
		if (left.RelatedActorId > 0L && right.RelatedActorId > 0L && left.RelatedActorId != right.RelatedActorId) return false;
		if (left.FamilyId > 0L && right.FamilyId > 0L && left.FamilyId != right.FamilyId) return false;
		if (left.SectId > 0L && right.SectId > 0L && left.SectId != right.SectId) return false;
		if (left.CityId > 0L && right.CityId > 0L && left.CityId != right.CityId) return false;
		bool categoryCompatible = string.Equals(left.Category, right.Category, StringComparison.Ordinal)
			|| string.Equals(left.Category, XjWorldHistoryCategory.World, StringComparison.Ordinal)
			|| string.Equals(right.Category, XjWorldHistoryCategory.World, StringComparison.Ordinal);
		if (!categoryCompatible) return false;
		// 只有公告镜像允许较宽松的短文/长文匹配；普通角色投影与结构化史实
		// 使用更高阈值，避免同一角色同年发生两次相似事件时被误合并。
		bool loose = IsAnnouncementMirror(left) || IsAnnouncementMirror(right);
		return AreLikelySameEventTexts(left.Body, right.Body, loose)
			|| AreLikelySameEventTexts(left.Body, right.Title, loose)
			|| AreLikelySameEventTexts(left.Title, right.Body, loose);
	}

	private static int ResolveRecordTextMatchRank(
		XjWorldHistoryArchiveRecord left,
		XjWorldHistoryArchiveRecord right)
	{
		if (left == null || right == null) return 0;
		bool loose = IsAnnouncementMirror(left) || IsAnnouncementMirror(right);
		int rank = ResolveEventTextMatchRank(left.Body, right.Body, loose);
		rank = Math.Max(rank, ResolveEventTextMatchRank(left.Body, right.Title, loose));
		rank = Math.Max(rank, ResolveEventTextMatchRank(left.Title, right.Body, loose));
		return rank;
	}

	private static bool AreLikelySameEventTexts(string left, string right, bool loose)
	{
		return ResolveEventTextMatchRank(left, right, loose) > 0;
	}

	private static int ResolveEventTextMatchRank(string left, string right, bool loose)
	{
		string a = NormalizeEventTextForSemanticDedup(left);
		string b = NormalizeEventTextForSemanticDedup(right);
		if (a.Length == 0 || b.Length == 0) return 0;
		if (string.Equals(a, b, StringComparison.Ordinal)) return 3;
		string shorter = a.Length <= b.Length ? a : b;
		string longer = a.Length <= b.Length ? b : a;
		if (shorter.Length >= 10 && longer.IndexOf(shorter, StringComparison.Ordinal) >= 0) return 2;
		if (!loose || shorter.Length < 8) return 0;
		int total = 0;
		int matched = 0;
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < shorter.Length - 1; i++)
		{
			string pair = shorter.Substring(i, 2);
			if (!seen.Add(pair)) continue;
			total++;
			if (longer.IndexOf(pair, StringComparison.Ordinal) >= 0) matched++;
		}
		if (total == 0) return 0;
		double ratio = matched / (double)total;
		return ratio >= 0.72d ? 1 : 0;
	}

	private static string NormalizeEventTextForSemanticDedup(string text)
	{
		string value = XjDisplayNameSanitizer.Clean(text, string.Empty).Trim();
		while (value.StartsWith("【", StringComparison.Ordinal))
		{
			int end = value.IndexOf('】');
			if (end <= 0 || end > 24) break;
			value = value.Substring(end + 1).TrimStart();
		}
		if (value.StartsWith("玄鉴历", StringComparison.Ordinal))
		{
			int yearEnd = value.IndexOf('年');
			if (yearEnd > 2 && yearEnd < 16) value = value.Substring(yearEnd + 1).TrimStart('，', ',', ' ');
		}
		StringBuilder builder = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsWhiteSpace(c) || "，。；：、,.!！?？‘’“”\"'（）()《》「」【】[]—-".IndexOf(c) >= 0) continue;
			builder.Append(c);
		}
		return builder.ToString();
	}

	private static int ResolveRecordRichness(XjWorldHistoryArchiveRecord record)
	{
		if (record == null) return int.MinValue;
		int score = 0;
		if (!IsLegacyProjectionRecord(record)) score += 20;
		if (record.ActorId > 0L) score += 6;
		if (record.RelatedActorId > 0L) score += 3;
		if (record.FamilyId > 0L) score += 4;
		if (record.SectId > 0L) score += 4;
		if (record.CityId > 0L) score += 2;
		if (!string.Equals(record.Title, record.Body, StringComparison.Ordinal)) score += 4;
		if (!string.Equals(record.Category, XjWorldHistoryCategory.World, StringComparison.Ordinal)) score += 2;
		if (record.IsProtected) score += 2;
		score += Math.Clamp(record.Importance, 1, 5);
		return score;
	}

	private static void MergeDuplicateMetadata(XjWorldHistoryArchiveRecord target, XjWorldHistoryArchiveRecord source)
	{
		if (target == null || source == null) return;
		target.VisibilityFlags |= source.VisibilityFlags;
		target.Importance = Math.Max(target.Importance, source.Importance);
		target.IsProtected |= source.IsProtected;
		if (target.ActorId <= 0L && source.ActorId > 0L) target.ActorId = source.ActorId;
		if (target.RelatedActorId <= 0L && source.RelatedActorId > 0L) target.RelatedActorId = source.RelatedActorId;
		if (target.FamilyId <= 0L && source.FamilyId > 0L) target.FamilyId = source.FamilyId;
		if (target.RelatedFamilyId <= 0L && source.RelatedFamilyId > 0L) target.RelatedFamilyId = source.RelatedFamilyId;
		if (target.SectId <= 0L && source.SectId > 0L) target.SectId = source.SectId;
		if (target.RelatedSectId <= 0L && source.RelatedSectId > 0L) target.RelatedSectId = source.RelatedSectId;
		if (target.CityId <= 0L && source.CityId > 0L) target.CityId = source.CityId;
		if (string.IsNullOrWhiteSpace(target.ActorName)) target.ActorName = source.ActorName ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.RelatedActorName)) target.RelatedActorName = source.RelatedActorName ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.FamilyNameSnapshot)) target.FamilyNameSnapshot = source.FamilyNameSnapshot ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.RelatedFamilyNameSnapshot)) target.RelatedFamilyNameSnapshot = source.RelatedFamilyNameSnapshot ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.SectNameSnapshot)) target.SectNameSnapshot = source.SectNameSnapshot ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.RelatedSectNameSnapshot)) target.RelatedSectNameSnapshot = source.RelatedSectNameSnapshot ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.CityNameSnapshot)) target.CityNameSnapshot = source.CityNameSnapshot ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.IconId)) target.IconId = source.IconId ?? string.Empty;
		if (string.IsNullOrWhiteSpace(target.Result)) target.Result = source.Result ?? string.Empty;
		if (target.CauseEventId <= 0L && source.CauseEventId > 0L) target.CauseEventId = source.CauseEventId;
		if (!target.HasLocation && source.HasLocation)
		{
			target.LocationX = source.LocationX;
			target.LocationY = source.LocationY;
			target.HasLocation = true;
		}
	}

	private static void RebuildDerivedIndexes()
	{
		EventIds.Clear();
		EventSignatures.Clear();
		EventBodySignatures.Clear();
		ClearIndexes();
		_sequence = 0L;
		for (int i = 0; i < Entries.Count; i++)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null) continue;
			Normalize(record);
			EventSignatures.Add(BuildEventSignature(record));
			EventBodySignatures.Add(BuildEventBodySignature(record));
			if (record.EventId <= 0L || !EventIds.Add(record.EventId))
			{
				record.EventId = BuildEventId(record);
				EventIds.Add(record.EventId);
			}
			IndexRecord(record);
			_sequence = Math.Max(_sequence, i + 1L);
		}
	}

	private static string BuildEventSignature(XjWorldHistoryArchiveRecord record)
	{
		return record.Year
			+ "|"
			+ record.EventType
			+ "|"
			+ record.Category
			+ "|"
			+ record.ActorId
			+ "|"
			+ record.RelatedActorId
			+ "|"
			+ record.SectId
			+ "|"
			+ record.FamilyId
			+ "|"
			+ record.CityId
			+ "|"
			+ record.Title
			+ "|"
			+ record.Body;
	}

	private static string BuildEventBodySignature(XjWorldHistoryArchiveRecord record)
	{
		return record.Year + "|" + NormalizeEventBodyForDedup(record.Body)
			+ "|" + record.ActorId + "|" + record.RelatedActorId
			+ "|" + record.FamilyId + "|" + record.SectId;
	}

	private static string NormalizeEventBodyForDedup(string text)
	{
		string value = XjDisplayNameSanitizer.Clean(text, string.Empty);
		if (value.Length == 0) return string.Empty;
		StringBuilder builder = new StringBuilder(value.Length);
		for (int i = 0; i < value.Length; i++)
		{
			char c = value[i];
			if (char.IsWhiteSpace(c) || "，。；：、,.!！?？‘’“”\"'".IndexOf(c) >= 0) continue;
			builder.Append(c);
		}
		return builder.ToString();
	}

	private static XjWorldHistoryArchiveRecord Clone(XjWorldHistoryArchiveRecord source)
	{
		return new XjWorldHistoryArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			EventId = source.EventId,
			Year = source.Year,
			EventType = source.EventType ?? string.Empty,
			Category = source.Category ?? XjWorldHistoryCategory.World,
			Title = source.Title ?? string.Empty,
			Body = source.Body ?? string.Empty,
			IconId = source.IconId ?? string.Empty,
			Importance = source.Importance,
			IsProtected = source.IsProtected,
			VisibilityFlags = source.VisibilityFlags,
			Result = source.Result ?? string.Empty,
			CauseEventId = source.CauseEventId,
			CenturyStatus = source.CenturyStatus ?? XjHistoryCenturyStatus.Pending,
			ActorId = source.ActorId,
			ActorName = source.ActorName ?? string.Empty,
			RelatedActorId = source.RelatedActorId,
			RelatedActorName = source.RelatedActorName ?? string.Empty,
			SectId = source.SectId,
			SectNameSnapshot = source.SectNameSnapshot ?? string.Empty,
			RelatedSectId = source.RelatedSectId,
			RelatedSectNameSnapshot = source.RelatedSectNameSnapshot ?? string.Empty,
			FamilyId = source.FamilyId,
			FamilyNameSnapshot = source.FamilyNameSnapshot ?? string.Empty,
			RelatedFamilyId = source.RelatedFamilyId,
			RelatedFamilyNameSnapshot = source.RelatedFamilyNameSnapshot ?? string.Empty,
			CityId = source.CityId,
			CityNameSnapshot = source.CityNameSnapshot ?? string.Empty,
			LocationX = source.LocationX,
			LocationY = source.LocationY,
			HasLocation = source.HasLocation
		};
	}
}
