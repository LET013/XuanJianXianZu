using System;
using System.Collections.Generic;
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
	private static readonly List<XjWorldHistoryArchiveRecord> Entries = new List<XjWorldHistoryArchiveRecord>();
	private static readonly HashSet<long> EventIds = new HashSet<long>();
	private static readonly HashSet<string> EventSignatures = new HashSet<string>(StringComparer.Ordinal);
	// 同一条事件可能经由角色公告与领域记录两条路径抵达历史；这里按年份和原文去重。
	private static readonly HashSet<string> EventBodySignatures = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<long, List<long>> EventIdsByActor = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, List<long>> EventIdsByFamily = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, List<long>> EventIdsBySect = new Dictionary<long, List<long>>();
	private static long _sequence;

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
		catch { }

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
			ActorName = SafeActorName(actor),
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
			actorName = SafeActorName(actor);
			sectId = ResolveActorSectId(actor);
			cityId = actor.city?.data?.id ?? 0L;
			try
			{
				x = (int)Math.Round(actor.current_position.x);
				y = (int)Math.Round(actor.current_position.y);
				hasLocation = true;
			}
			catch { }
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

	internal static IReadOnlyList<XjWorldHistoryArchiveRecord> ReadSnapshot(int maximumEntries = 0)
	{
		int count = maximumEntries > 0 ? Math.Min(Entries.Count, maximumEntries) : Entries.Count;
		if (count == 0) return Array.Empty<XjWorldHistoryArchiveRecord>();
		int start = Entries.Count - count;
		List<XjWorldHistoryArchiveRecord> result = new List<XjWorldHistoryArchiveRecord>(count);
		for (int i = start; i < Entries.Count; i++) result.Add(Clone(Entries[i]));
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
		int start = Math.Max(0, source.Count - MaxEntries);
		for (int i = start; i < source.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = source[i];
			if (item == null) continue;
			XjWorldHistoryArchiveRecord copy = Clone(item);
			Normalize(copy);
			if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(copy.Category, copy.EventType, copy.Title, copy.Body))
			{
				filteredSuppressedRecords = true;
				continue;
			}
			string signature = BuildEventSignature(copy);
			string bodySignature = BuildEventBodySignature(copy);
			if (!EventSignatures.Add(signature) || !EventBodySignatures.Add(bodySignature)) continue;
			if (copy.EventId <= 0L || !EventIds.Add(copy.EventId))
			{
				copy.EventId = BuildEventId(copy);
				EventIds.Add(copy.EventId);
			}
			Entries.Add(copy);
			IndexRecord(copy);
			_sequence = Math.Max(_sequence, Entries.Count);
		}
		if (filteredSuppressedRecords)
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
		if (Entries.Count == 0) return 0;
		int removed = 0;
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body)) continue;
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
		XjCodexSnapshotPublisher.MarkDirty();
	}

	internal static bool EnsureWorldVisibleForAnnouncement(string text, string iconId = null)
	{
		string cleaned = XjDisplayNameSanitizer.Clean(text, string.Empty);
		if (cleaned.Length == 0) return false;
		int currentYear = ResolveCurrentYear();
		for (int i = Entries.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = Entries[i];
			if (record == null || record.Year != currentYear) continue;
			if (!string.Equals(record.Body, cleaned, StringComparison.Ordinal)
				&& !string.Equals(record.Title, cleaned, StringComparison.Ordinal)) continue;
			if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body)) return false;
			int required = (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate);
			if ((record.VisibilityFlags & required) == required) return true;
			record.VisibilityFlags |= required;
			if (string.IsNullOrWhiteSpace(record.IconId))
			{
				string category = ResolveCategory(XjEventIconCatalog.NormalizeIconId(iconId), cleaned, record.Category);
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
		if (!EventSignatures.Add(signature) || !EventBodySignatures.Add(bodySignature)) return false;
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

	private static bool IsAlchemyCraftEvent(string text, string iconId)
	{
		return string.Equals(iconId, XjEventIconCatalog.LianDan, StringComparison.Ordinal)
			|| string.Equals(iconId, XjEventIconCatalog.HighDanYao, StringComparison.Ordinal)
			|| string.Equals(iconId, XjEventIconCatalog.DanFangAcquire, StringComparison.Ordinal)
			|| string.Equals(iconId, XjEventIconCatalog.ZhaLu, StringComparison.Ordinal)
			|| HasAny(text, "炼丹", "丹药", "丹方", "炸炉", "丹成", "开炉");
	}

	private static bool IsTalismanCraftEvent(string text, string iconId)
	{
		return HasAny(text, "符箓", "制符", "符师", "护身符", "神行符", "破阵符", "破障符", "镇神符", "符纸", "符墨");
	}

	private static bool IsFormationCraftEvent(string text, string iconId)
	{
		return HasAny(text, "阵法", "大阵", "阵纹", "阵师", "护宗大阵", "宗门大阵");
	}

	private static bool IsArtifactCraftEvent(string text, string iconId)
	{
		return string.Equals(iconId, XjEventIconCatalog.FaBaoCreation, StringComparison.Ordinal)
			|| HasAny(text, "炼器", "法器", "灵宝", "法宝", "器成", "万宝录");
	}

	private static bool IsGongFaWriteEvent(string text, string iconId)
	{
		return string.Equals(iconId, XjEventIconCatalog.GongFaAcquire, StringComparison.Ordinal)
			|| string.Equals(iconId, XjEventIconCatalog.QiuJinFaAcquire, StringComparison.Ordinal)
			|| HasAny(text, "功法入宗", "功法入阁", "上法入谱", "高法归族", "求金法入宗", "求金法入阁", "采气法入宗", "采气法入阁", "洞天营造之法入宗");
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
		while (Entries.Count > MaxEntries)
		{
			int removeIndex = FindOldestRemovableIndex();
			if (removeIndex < 0) removeIndex = 0;
			EventIds.Remove(Entries[removeIndex].EventId);
			EventSignatures.Remove(BuildEventSignature(Entries[removeIndex]));
			EventBodySignatures.Remove(BuildEventBodySignature(Entries[removeIndex]));
			UnindexRecord(Entries[removeIndex]);
			Entries.RemoveAt(removeIndex);
		}
	}

	private static int FindOldestRemovableIndex()
	{
		for (int i = 0; i < Entries.Count; i++)
		{
			if (!Entries[i].IsProtected && Entries[i].Importance <= 2) return i;
		}
		for (int i = 0; i < Entries.Count; i++)
		{
			if (!Entries[i].IsProtected) return i;
		}
		return -1;
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
		if (HasAny(text, "金丹", "神丹", "结璘", "紫府", "筑基", "炼气", "突破", "求金", "转世", "登名石")) return XjWorldHistoryCategory.Cultivation;
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
		if (HasAny(text, "证得金丹", "成就金丹", "金性妖邪", "阴司降临", "道胎垂眸", "开宗", "灭宗", "宗门宣战", "高境压服")) resolved = 5;
		else if (HasAny(text, "晋升紫府", "求金失败", "洞天现世", "洞天争夺", "重立山门", "另立新宗", "人丹", "夺法", "截留", "算计")) resolved = 4;
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
		if (text.Contains("jindan") || text.Contains("jielin") || text.Contains("zifu") || text.Contains("breakthrough") || text.Contains("death")) return XjWorldHistoryCategory.HighRealm;
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
		if (HasAny(value, "突破", "晋升", "证得", "成就", "筑基", "紫府", "金丹")) return "Breakthrough";
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
				? SafeActorName(actor)
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

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
		}
		catch { return "未名修士"; }
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
		return isGongFa && (text.IndexOf("四品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("4品", StringComparison.OrdinalIgnoreCase) >= 0);
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
		return record.Year + "|" + (record.Body ?? string.Empty).Trim()
			+ "|" + record.ActorId + "|" + record.RelatedActorId
			+ "|" + record.FamilyId + "|" + record.SectId;
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

