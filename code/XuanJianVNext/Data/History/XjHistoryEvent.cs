using System;
using XuanJianVNext.Data.Codex;

namespace XuanJianVNext.Data.History;

/// <summary>
/// 三卷史册共用的统一事件底本。它只保存已经发生的事实，不直接决定任何一卷的写法。
/// Personal/Family/Sect NarrativeAdapter 分别决定事件是否进入本卷，并生成各自叙述。
/// </summary>
internal sealed class XjHistoryEvent
{
	internal XjCodexHistoryItem Source { get; }

	internal string EventId => Source?.EventId ?? string.Empty;
	internal long SortSequence => Source?.SortSequence ?? 0L;
	internal int Year => Source?.Year ?? 0;
	internal string EventType => Source?.EventType ?? string.Empty;
	internal string Category => Source?.Category ?? string.Empty;
	internal string Title => Source?.Title ?? string.Empty;
	internal string Body => Source?.Body ?? string.Empty;
	internal string Result => Source?.Result ?? string.Empty;
	internal int Importance => Source?.Importance ?? 0;
	internal int VisibilityFlags => Source?.VisibilityFlags ?? 0;
	internal long ActorId => Source?.ActorId ?? 0L;
	internal string ActorName => Source?.ActorName ?? string.Empty;
	internal long RelatedActorId => Source?.RelatedActorId ?? 0L;
	internal string RelatedActorName => Source?.RelatedActorName ?? string.Empty;
	internal long FamilyId => Source?.FamilyId ?? 0L;
	internal string FamilyName => Source?.FamilyName ?? string.Empty;
	internal long RelatedFamilyId => Source?.RelatedFamilyId ?? 0L;
	internal string RelatedFamilyName => Source?.RelatedFamilyName ?? string.Empty;
	internal long SectId => Source?.SectId ?? 0L;
	internal string SectName => Source?.SectName ?? string.Empty;
	internal long RelatedSectId => Source?.RelatedSectId ?? 0L;
	internal string RelatedSectName => Source?.RelatedSectName ?? string.Empty;
	internal long CityId => Source?.CityId ?? 0L;
	internal string Location => Source?.Location ?? string.Empty;
	internal bool HasLocation => Source?.HasLocation ?? false;

	private XjHistoryEvent(XjCodexHistoryItem source)
	{
		Source = source;
	}

	internal static XjHistoryEvent From(XjCodexHistoryItem source)
	{
		return source == null ? null : new XjHistoryEvent(source);
	}

	internal bool HasVisibility(XjHistoryVisibility visibility)
	{
		return visibility != XjHistoryVisibility.None
			&& (VisibilityFlags & (int)visibility) != 0;
	}

	internal bool MatchesPersonal(long subjectId)
	{
		return subjectId <= 0L
			? ActorId > 0L || RelatedActorId > 0L
			: ActorId == subjectId || RelatedActorId == subjectId;
	}

	internal bool MatchesFamily(long subjectId)
	{
		return subjectId <= 0L
			? FamilyId > 0L || RelatedFamilyId > 0L
			: FamilyId == subjectId || RelatedFamilyId == subjectId;
	}

	internal bool MatchesSect(long subjectId)
	{
		return subjectId <= 0L
			? SectId > 0L || RelatedSectId > 0L
			: SectId == subjectId || RelatedSectId == subjectId;
	}
}
