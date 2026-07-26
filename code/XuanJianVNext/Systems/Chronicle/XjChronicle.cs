using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Chronicle;

internal static class XjChronicleEventTypes
{
	internal const string Birth = "Birth";
	internal const string AptitudeGranted = "AptitudeGranted";
	internal const string GongFaGenerated = "GongFaGenerated";
	internal const string GongFaLost = "GongFaLost";
	internal const string CaiQiCompleted = "CaiQiCompleted";
	internal const string CaiQiFaObtained = "CaiQiFaObtained";
	internal const string FaBaoObtained = "FaBaoObtained";
	internal const string QiuJinFaComprehended = "QiuJinFaComprehended";
	internal const string JinDanSucceeded = "JinDanSucceeded";
	internal const string ShenDanSucceeded = "ShenDanSucceeded";
	internal const string JieLinSucceeded = "JieLinSucceeded";
	internal const string RenDanRefined = "RenDanRefined";
	internal const string FamilyVendettaCreated = "FamilyVendettaCreated";
	internal const string FamilyVendettaAvenged = "FamilyVendettaAvenged";
	internal const string FamilyVendettaFailed = "FamilyVendettaFailed";
	internal const string FamilyVendettaCounterKilled = "FamilyVendettaCounterKilled";
	internal const string FamilyVendettaClosed = "FamilyVendettaClosed";
	internal const string ActorDied = "ActorDied";
	internal const string FamilyMemberConfirmed = "FamilyMemberConfirmed";
	internal const string BreakthroughBlocked = "BreakthroughBlocked";
	internal const string JinDanFailureDemonized = "JinDanFailureDemonized";
	internal const string JinDanFailureDeath = "JinDanFailureDeath";
	internal const string JinXingYaoXieSuppressed = "JinXingYaoXieSuppressed";
	internal const string YinSiDescended = "YinSiDescended";
	internal const string HighTierSpellTriggered = "HighTierSpellTriggered";
	internal const string ZongMenBackflow = "ZongMenBackflow";
	internal const string ZongMenFounded = "ZongMenFounded";
	internal const string DongTianOpened = "DongTianOpened";
	internal const string DongTianSurvived = "DongTianSurvived";
	internal const string DongTianDeath = "DongTianDeath";
	internal const string DongTianClosed = "DongTianClosed";
	internal const string FaBaoUpgraded = "FaBaoUpgraded";
	internal const string JinXingObtained = "JinXingObtained";
	internal const string LingWuAppeared = "LingWuAppeared";
	internal const string JinDanResidualAppeared = "JinDanResidualAppeared";
	internal const string JinDanResidualAcquired = "JinDanResidualAcquired";
}

internal sealed class XjChronicleEvent
{
	internal static XjChronicleEvent Empty { get; } = new XjChronicleEvent(
		false,
		0L,
		0L,
		string.Empty,
		0,
		string.Empty,
		string.Empty,
		1,
		false,
		false,
		false,
		string.Empty,
		string.Empty,
		string.Empty);

	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly long ActorId;
	internal readonly string EventType;
	internal readonly int Timestamp;
	internal readonly string Title;
	internal readonly string Body;
	internal readonly int Importance;
	internal readonly bool IsProtected;
	internal readonly bool RelatedToFamilyWarehouse;
	internal readonly bool RelatedToHighGradeGongFa;
	internal readonly string ReasonCode;
	internal readonly string Source;
	internal readonly string ActorRealmSnapshot;

	internal XjChronicleEvent(
		bool found,
		long familyStableId,
		long actorId,
		string eventType,
		int timestamp,
		string title,
		string body,
		int importance,
		bool isProtected,
		bool relatedToFamilyWarehouse,
		bool relatedToHighGradeGongFa,
		string reasonCode,
		string source = "",
		string actorRealmSnapshot = "")
	{
		Found = found;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		ActorId = actorId < 0L ? 0L : actorId;
		EventType = eventType ?? string.Empty;
		Timestamp = timestamp < 0 ? 0 : timestamp;
		Title = title ?? string.Empty;
		Body = body ?? string.Empty;
		Importance = importance < 1 ? 1 : importance;
		IsProtected = isProtected;
		RelatedToFamilyWarehouse = relatedToFamilyWarehouse;
		RelatedToHighGradeGongFa = relatedToHighGradeGongFa;
		ReasonCode = reasonCode ?? string.Empty;
		Source = source ?? string.Empty;
		ActorRealmSnapshot = actorRealmSnapshot ?? string.Empty;
	}

	internal string Summary
	{
		get
		{
			if (!string.IsNullOrWhiteSpace(Title) && !string.IsNullOrWhiteSpace(Body))
				return Title + "：" + Body;
			if (!string.IsNullOrWhiteSpace(Title))
				return Title;
			return Body;
		}
	}
}


internal sealed class XjFamilyChronicleMemory
{
	internal static XjFamilyChronicleMemory Shared { get; } = new XjFamilyChronicleMemory();

	private readonly Dictionary<long, List<XjChronicleEvent>> eventsByFamilyStableId = new Dictionary<long, List<XjChronicleEvent>>();
	private readonly HashSet<string> eventKeys = new HashSet<string>();
	private readonly Dictionary<XjChronicleEvent, string> archiveEventKeysByEvent = new Dictionary<XjChronicleEvent, string>();

	internal bool Append(XjChronicleEvent chronicleEvent)
	{
		return Append(chronicleEvent, string.Empty);
	}

	internal bool Append(XjChronicleEvent chronicleEvent, string eventKey)
	{
		if (IsCaiQiFaChronicle(chronicleEvent) || IsGradeFourGongFaChronicle(chronicleEvent))
		{
			return false;
		}

		if (chronicleEvent == null
			|| !chronicleEvent.Found
			|| chronicleEvent.FamilyStableId <= 0L
			|| chronicleEvent.ActorId <= 0L
			|| string.IsNullOrWhiteSpace(chronicleEvent.EventType)
			|| !XjHistoryRetentionPolicy.ShouldKeepChronicleRecord(chronicleEvent.EventType, chronicleEvent.Title, chronicleEvent.Body, chronicleEvent.Source))
		{
			return false;
		}

		string stableEventKey;
		if (string.IsNullOrWhiteSpace(eventKey))
		{
			stableEventKey = AddUniqueEventKey(BuildArchiveEventKey(chronicleEvent));
		}
		else
		{
			stableEventKey = eventKey.Trim();
			if (!eventKeys.Add(stableEventKey))
			{
				return false;
			}
		}

		if (!eventsByFamilyStableId.TryGetValue(chronicleEvent.FamilyStableId, out List<XjChronicleEvent> events))
		{
			events = new List<XjChronicleEvent>();
			eventsByFamilyStableId[chronicleEvent.FamilyStableId] = events;
		}

		events.Add(chronicleEvent);
		archiveEventKeysByEvent[chronicleEvent] = stableEventKey;
		XjWorldArchiveSystem.MarkChanged();
		if (chronicleEvent.IsProtected)
		{
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		return true;
	}

	private static bool IsCaiQiFaChronicle(XjChronicleEvent chronicleEvent)
	{
		if (chronicleEvent == null) return false;
		string text = (chronicleEvent.EventType ?? string.Empty) + " "
			+ (chronicleEvent.Title ?? string.Empty) + " "
			+ (chronicleEvent.Body ?? string.Empty) + " "
			+ (chronicleEvent.Summary ?? string.Empty);
		return text.IndexOf("CaiQiFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("采气法", StringComparison.Ordinal) >= 0;
	}

	private static bool IsGradeFourGongFaChronicle(XjChronicleEvent chronicleEvent)
	{
		if (chronicleEvent == null) return false;
		string type = chronicleEvent.EventType ?? string.Empty;
		string text = type + " "
			+ (chronicleEvent.Title ?? string.Empty) + " "
			+ (chronicleEvent.Body ?? string.Empty) + " "
			+ (chronicleEvent.Summary ?? string.Empty);
		bool isGongFa = type.IndexOf("GongFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| type.IndexOf("TechniqueRecovered", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("功法", StringComparison.Ordinal) >= 0;
		return isGongFa && (text.IndexOf("四品", StringComparison.Ordinal) >= 0
			|| text.IndexOf("4品", StringComparison.OrdinalIgnoreCase) >= 0);
	}

	internal IReadOnlyList<XjChronicleEvent> ReadFamilyEvents(long familyStableId)
	{
		if (familyStableId <= 0L
			|| !eventsByFamilyStableId.TryGetValue(familyStableId, out List<XjChronicleEvent> events)
			|| events.Count == 0)
		{
			return System.Array.Empty<XjChronicleEvent>();
		}

		return new List<XjChronicleEvent>(events);
	}

	internal void Clear()
	{
		eventsByFamilyStableId.Clear();
		eventKeys.Clear();
		archiveEventKeysByEvent.Clear();
	}

	internal void ExportArchiveRecords(
		List<XjWorldArchiveChronicleRecord> chronicleRecords,
		List<XjWorldArchiveDeathRecord> deathRecords)
	{
		if (chronicleRecords == null || deathRecords == null)
		{
			return;
		}

		foreach (KeyValuePair<long, List<XjChronicleEvent>> familyEntry in eventsByFamilyStableId)
		{
			List<XjChronicleEvent> events = familyEntry.Value;
			if (events == null)
			{
				continue;
			}

			for (int i = 0; i < events.Count; i++)
			{
				XjChronicleEvent chronicleEvent = events[i];
				if (chronicleEvent == null || !chronicleEvent.Found)
				{
					continue;
				}

				chronicleRecords.Add(new XjWorldArchiveChronicleRecord
				{
					EventKey = archiveEventKeysByEvent.TryGetValue(chronicleEvent, out string archiveEventKey)
						? archiveEventKey
						: BuildArchiveEventKey(chronicleEvent),
					FamilyStableId = chronicleEvent.FamilyStableId,
					ActorId = chronicleEvent.ActorId,
					EventType = chronicleEvent.EventType,
					Year = chronicleEvent.Timestamp,
					Text = chronicleEvent.Summary,
					Title = chronicleEvent.Title,
					Body = chronicleEvent.Body,
					Source = chronicleEvent.Source,
					ActorRealmSnapshot = chronicleEvent.ActorRealmSnapshot,
					Importance = chronicleEvent.Importance,
					IsProtected = chronicleEvent.IsProtected,
					RelatedToFamilyWarehouse = chronicleEvent.RelatedToFamilyWarehouse,
					RelatedToHighGradeGongFa = chronicleEvent.RelatedToHighGradeGongFa
				});

				if (chronicleEvent.EventType == XjChronicleEventTypes.ActorDied)
				{
					deathRecords.Add(new XjWorldArchiveDeathRecord
					{
						FamilyStableId = chronicleEvent.FamilyStableId,
						ActorId = chronicleEvent.ActorId,
						Year = chronicleEvent.Timestamp,
						Name = chronicleEvent.Summary
					});
				}
			}
		}
	}

	internal void ImportArchiveRecords(IReadOnlyList<XjWorldArchiveChronicleRecord> records)
	{
		eventsByFamilyStableId.Clear();
		eventKeys.Clear();
		archiveEventKeysByEvent.Clear();
		if (records == null || records.Count == 0)
		{
			return;
		}

		bool filteredSuppressedCraft = false;
		for (int i = 0; i < records.Count; i++)
		{
			XjWorldArchiveChronicleRecord record = records[i];
			if (record == null || record.FamilyStableId <= 0L || record.ActorId <= 0L || string.IsNullOrWhiteSpace(record.EventType))
			{
				continue;
			}

			if (!XjHistoryRetentionPolicy.ShouldKeepChronicleRecord(record.EventType, record.Title, record.Body, record.Source))
			{
				filteredSuppressedCraft = true;
				continue;
			}

			XjChronicleEvent chronicleEvent = new XjChronicleEvent(
				true,
				record.FamilyStableId,
				record.ActorId,
				record.EventType,
				record.Year,
				string.IsNullOrWhiteSpace(record.Title) ? record.Text : record.Title,
				string.IsNullOrWhiteSpace(record.Body) ? record.Text : record.Body,
				record.Importance < 1 ? 1 : record.Importance,
				record.IsProtected,
				record.RelatedToFamilyWarehouse,
				record.RelatedToHighGradeGongFa,
				"Archive",
				record.Source,
				record.ActorRealmSnapshot);

			string eventKey = string.IsNullOrWhiteSpace(record.EventKey)
				? BuildArchiveEventKey(chronicleEvent)
				: record.EventKey.Trim();
			eventKey = AddUniqueEventKey(eventKey);

			if (!eventsByFamilyStableId.TryGetValue(chronicleEvent.FamilyStableId, out List<XjChronicleEvent> events))
			{
				events = new List<XjChronicleEvent>();
				eventsByFamilyStableId[chronicleEvent.FamilyStableId] = events;
			}

			events.Add(chronicleEvent);
			archiveEventKeysByEvent[chronicleEvent] = eventKey;
		}
		if (filteredSuppressedCraft)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
	}

	private string AddUniqueEventKey(string baseKey)
	{
		string candidate = baseKey ?? string.Empty;
		int occurrence = 2;
		while (!eventKeys.Add(candidate))
		{
			candidate = baseKey + "|occurrence|" + occurrence;
			occurrence++;
		}

		return candidate;
	}

	private static string BuildArchiveEventKey(XjChronicleEvent chronicleEvent)
	{
		if (chronicleEvent == null)
		{
			return string.Empty;
		}

		return chronicleEvent.FamilyStableId
			+ "|"
			+ chronicleEvent.ActorId
			+ "|"
			+ chronicleEvent.EventType
			+ "|"
			+ chronicleEvent.Timestamp
			+ "|"
			+ chronicleEvent.Summary;
	}
}


internal sealed class XjChronicleReadModel
{
	internal static XjChronicleReadModel Shared { get; } = new XjChronicleReadModel(XjFamilyChronicleMemory.Shared);

	private readonly XjFamilyChronicleMemory chronicleMemory;

	internal XjChronicleReadModel(XjFamilyChronicleMemory chronicleMemory)
	{
		this.chronicleMemory = chronicleMemory ?? XjFamilyChronicleMemory.Shared;
	}

	internal IReadOnlyList<XjChronicleEvent> ReadFamilyChronicle(long familyStableId)
	{
		return chronicleMemory.ReadFamilyEvents(familyStableId);
	}

	internal IReadOnlyList<XjChronicleEvent> ReadActorChronicle(long familyStableId, long actorId)
	{
		if (familyStableId <= 0L || actorId <= 0L)
		{
			return System.Array.Empty<XjChronicleEvent>();
		}

		IReadOnlyList<XjChronicleEvent> familyEvents = chronicleMemory.ReadFamilyEvents(familyStableId);
		if (familyEvents.Count == 0)
		{
			return System.Array.Empty<XjChronicleEvent>();
		}

		List<XjChronicleEvent> actorEvents = new List<XjChronicleEvent>();
		for (int i = 0; i < familyEvents.Count; i++)
		{
			XjChronicleEvent chronicleEvent = familyEvents[i];
			if (chronicleEvent != null && chronicleEvent.ActorId == actorId)
			{
				actorEvents.Add(chronicleEvent);
			}
		}

		return actorEvents;
	}
}
