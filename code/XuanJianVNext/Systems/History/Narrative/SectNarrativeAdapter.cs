using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>宗门纪事：只围绕法统、诸峰、门人、讲席、大比、关系与山门兴衰展开。</summary>
internal sealed class SectNarrativeAdapter : IXjHistoryNarrativeAdapter
{
	internal static SectNarrativeAdapter Shared { get; } = new SectNarrativeAdapter();

	public XjHistoryVisibility Visibility => XjHistoryVisibility.Sect;

	private SectNarrativeAdapter() { }

	public bool CanWrite(XjHistoryEvent historyEvent, long subjectId)
	{
		if (historyEvent == null || !historyEvent.HasVisibility(Visibility) || !historyEvent.MatchesSect(subjectId) || !XjHistoryNarrativeTemplateEngine.CanWritePerspective(historyEvent.Source, Visibility)) return false;
		string sectName = historyEvent.SectId == subjectId || subjectId <= 0L
			? historyEvent.SectName
			: historyEvent.RelatedSectName;
		return XjHistoryNamePolicy.IsChineseSectName(sectName);
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

