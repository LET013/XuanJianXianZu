using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>修士列传：只围绕一名修士的生平、关系、所得与命运变化展开。</summary>
internal sealed class PersonalNarrativeAdapter : IXjHistoryNarrativeAdapter
{
	internal static PersonalNarrativeAdapter Shared { get; } = new PersonalNarrativeAdapter();

	public XjHistoryVisibility Visibility => XjHistoryVisibility.Personal;

	private PersonalNarrativeAdapter() { }

	public bool CanWrite(XjHistoryEvent historyEvent, long subjectId)
	{
		if (historyEvent == null || !historyEvent.HasVisibility(Visibility) || !historyEvent.MatchesPersonal(subjectId) || !XjHistoryNarrativeTemplateEngine.CanWritePerspective(historyEvent.Source, Visibility)) return false;
		string actorName = historyEvent.ActorId == subjectId || subjectId <= 0L
			? historyEvent.ActorName
			: historyEvent.RelatedActorName;
		return XjHistoryNamePolicy.IsChineseActorName(actorName);
	}

	public XjHistoryNarrativeEntry Compose(XjHistoryEvent historyEvent, long subjectId, string subjectName)
	{
		if (!CanWrite(historyEvent, subjectId)) return new XjHistoryNarrativeEntry();
		return XjHistoryNarrativeTemplateEngine.ComposePerspective(
			historyEvent.Source,
			Visibility,
			subjectId,
			subjectName);
	}
}

