using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 出生早期或迁移重建期间家族身份可能暂时 Pending。这里仅暂存世家纪事投影所需事实，
/// 家族确认后再调用三书 Writer；不会重复触发仓库、百年世谱或旧家族纪事。
/// </summary>
internal static class XjThreeBookDeferredFamilyFacts
{
	private const int MaxRecords = 2048;
	private const int RetentionYears = 100;
	private static readonly List<XjThreeBookDeferredFamilyFactRecord> Records = new List<XjThreeBookDeferredFamilyFactRecord>();
	private static readonly HashSet<string> Ids = new HashSet<string>(StringComparer.Ordinal);
	private static long _enqueued;
	private static long _resolved;
	private static long _expired;
	private static long _dropped;
	private static int _yearCursor;

	internal static int Count => Records.Count;
	internal static long EnqueuedCount => _enqueued;
	internal static long ResolvedCount => _resolved;
	internal static long ExpiredCount => _expired;
	internal static long DroppedCount => _dropped;

	internal static void Enqueue(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.ActorId <= 0L || string.IsNullOrWhiteSpace(domainEvent.EventType)
			|| !ShouldDefer(domainEvent.EventType)) return;
		string id = BuildId(domainEvent);
		if (!Ids.Add(id)) return;
		Records.Add(FromDomainEvent(id, domainEvent));
		_enqueued++;
		while (Records.Count > MaxRecords)
		{
			RemoveAt(0);
			_dropped++;
		}
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
	}

	internal static int TryFlushActor(long actorId, int budget = 16)
	{
		if (actorId <= 0L || budget <= 0 || Records.Count == 0) return 0;
		int processed = 0;
		bool changed = false;
		for (int i = 0; i < Records.Count && processed < budget;)
		{
			XjThreeBookDeferredFamilyFactRecord item = Records[i];
			if (item == null || item.ActorId != actorId)
			{
				i++;
				continue;
			}
			item.Attempts++;
			XjFamilyDomainEvent domainEvent = ToDomainEvent(item);
			if (!XjFamilyDomainEventRouter.TryResolveFamily(domainEvent, out XjFamilyDomainEvent resolved))
			{
				i++;
				processed++;
				continue;
			}
			XjThreeBookWriter.RecordFamilyFact(resolved);
			RemoveAt(i);
			_resolved++;
			changed = true;
			processed++;
		}
		if (changed)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
		}
		return processed;
	}

	internal static int TickYear(int currentYear)
	{
		if (Records.Count == 0) return 0;
		int itemBudget = XjRuntimeWorkBudget.StressTier >= XjRuntimeStressTier.Mild ? 32 : 96;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			itemBudget,
			0.18d,
			XjRuntimeFramePriority.Background);
		int processed = 0;
		int inspected = 0;
		int initialCount = Records.Count;
		bool changed = false;
		while (Records.Count > 0 && inspected < initialCount && budget.TryTake())
		{
			if (_yearCursor < 0 || _yearCursor >= Records.Count) _yearCursor = 0;
			int index = _yearCursor;
			XjThreeBookDeferredFamilyFactRecord item = Records[index];
			inspected++;
			processed++;
			if (item == null)
			{
				RemoveAt(index);
				changed = true;
				continue;
			}
			if (currentYear > 0 && item.QueuedYear > 0 && currentYear - item.QueuedYear > RetentionYears)
			{
				RemoveAt(index);
				_expired++;
				changed = true;
				continue;
			}
			item.Attempts++;
			XjFamilyDomainEvent domainEvent = ToDomainEvent(item);
			if (XjFamilyDomainEventRouter.TryResolveFamily(domainEvent, out XjFamilyDomainEvent resolved))
			{
				XjThreeBookWriter.RecordFamilyFact(resolved);
				RemoveAt(index);
				_resolved++;
				changed = true;
				continue;
			}
			_yearCursor++;
		}
		if (changed)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
		}
		XjPerformanceTelemetry.ObserveQueue("threebook-family-pending", Records.Count);
		return processed;
	}

	internal static void ExportArchiveRecords(List<XjThreeBookDeferredFamilyFactRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < Records.Count; i++) target.Add(Clone(Records[i]));
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjThreeBookDeferredFamilyFactRecord> source)
	{
		Records.Clear();
		Ids.Clear();
		_enqueued = 0L;
		_resolved = 0L;
		_expired = 0L;
		_dropped = 0L;
		_yearCursor = 0;
		if (source == null) return;
		int start = Math.Max(0, source.Count - MaxRecords);
		for (int i = start; i < source.Count; i++)
		{
			XjThreeBookDeferredFamilyFactRecord item = source[i];
			if (item == null || item.ActorId <= 0L || string.IsNullOrWhiteSpace(item.EventType)) continue;
			XjThreeBookDeferredFamilyFactRecord copy = Clone(item);
			if (string.IsNullOrWhiteSpace(copy.DeferredId)) copy.DeferredId = BuildId(ToDomainEvent(copy));
			if (!Ids.Add(copy.DeferredId)) continue;
			Records.Add(copy);
		}
	}

	internal static void CompactMemory()
	{
		Records.TrimExcess();
	}

	internal static void Clear()
	{
		Records.Clear();
		Ids.Clear();
		_enqueued = 0L;
		_resolved = 0L;
		_expired = 0L;
		_dropped = 0L;
		_yearCursor = 0;
	}

	private static bool ShouldDefer(string eventType)
	{
		return string.Equals(eventType, XjFamilyDomainEvent.TypeBirth, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeAptitudeGranted, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeRealmBreakthrough, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeJinDanSucceeded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeShenDanSucceeded, StringComparison.Ordinal);
	}

	private static void RemoveAt(int index)
	{
		if ((uint)index >= (uint)Records.Count) return;
		XjThreeBookDeferredFamilyFactRecord item = Records[index];
		if (item != null) Ids.Remove(item.DeferredId);
		Records.RemoveAt(index);
		if (index < _yearCursor) _yearCursor--;
		if (_yearCursor < 0 || _yearCursor >= Records.Count) _yearCursor = 0;
	}

	private static string BuildId(in XjFamilyDomainEvent domainEvent)
	{
		return "family|deferred|" + domainEvent.EventType + "|" + domainEvent.ActorId + "|" + domainEvent.Year
			+ "|" + domainEvent.Source + "|" + domainEvent.RealmId + "|" + domainEvent.GongFaName
			+ "|" + domainEvent.FaBaoId + "|" + domainEvent.CaiQiResourceId;
	}

	private static XjThreeBookDeferredFamilyFactRecord FromDomainEvent(string id, in XjFamilyDomainEvent value)
	{
		return new XjThreeBookDeferredFamilyFactRecord
		{
			DeferredId = id,
			QueuedYear = Math.Max(value.Year, XjYearTracker.CurrentYear),
			Found = value.Found,
			EventType = value.EventType,
			ActorId = value.ActorId,
			ActorName = value.ActorName,
			FamilyStableId = value.FamilyStableId,
			FamilyKey = value.FamilyKey,
			ZongMenId = value.ZongMenId,
			ZongMenName = value.ZongMenName,
			Year = value.Year,
			Source = value.Source,
			RealmId = value.RealmId,
			GongFaName = value.GongFaName,
			GongFaGrade = value.GongFaGrade,
			QiuJinFaName = value.QiuJinFaName,
			CaiQiResourceId = value.CaiQiResourceId,
			CaiQiAmount = value.CaiQiAmount,
			CaiQiFaName = value.CaiQiFaName,
			CaiQiFaSourcePlace = value.CaiQiFaSourcePlace,
			FaBaoId = value.FaBaoId,
			FaBaoName = value.FaBaoName,
			FaBaoClass = value.FaBaoClass,
			DaoTu = value.DaoTu,
			GuoWei = value.GuoWei,
			MappedXianJi = value.MappedXianJi,
			BoundAuthority = value.BoundAuthority
		};
	}

	private static XjFamilyDomainEvent ToDomainEvent(XjThreeBookDeferredFamilyFactRecord value)
	{
		if (value == null) return default;
		return new XjFamilyDomainEvent(
			value.Found, value.EventType, value.ActorId, value.ActorName, value.FamilyStableId, value.FamilyKey,
			value.ZongMenId, value.ZongMenName, value.Year, value.Source, value.RealmId, value.GongFaName,
			value.GongFaGrade, value.QiuJinFaName, value.CaiQiResourceId, value.CaiQiAmount, value.CaiQiFaName,
			value.CaiQiFaSourcePlace, value.FaBaoId, value.FaBaoName, value.FaBaoClass, value.DaoTu, value.GuoWei,
			value.MappedXianJi, value.BoundAuthority);
	}

	private static XjThreeBookDeferredFamilyFactRecord Clone(XjThreeBookDeferredFamilyFactRecord source)
	{
		if (source == null) return null;
		XjThreeBookDeferredFamilyFactRecord copy = FromDomainEvent(source.DeferredId, ToDomainEvent(source));
		copy.QueuedYear = source.QueuedYear;
		copy.Attempts = source.Attempts;
		return copy;
	}
}
