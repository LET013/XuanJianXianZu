using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>世家纪事：只围绕血脉延续、族议、功过、资源与家门兴衰展开。</summary>
internal sealed class FamilyNarrativeAdapter : IXjHistoryNarrativeAdapter
{
	internal static FamilyNarrativeAdapter Shared { get; } = new FamilyNarrativeAdapter();

	public XjHistoryVisibility Visibility => XjHistoryVisibility.Family;

	private FamilyNarrativeAdapter() { }

	public bool CanWrite(XjHistoryEvent historyEvent, long subjectId)
	{
		if (historyEvent == null || !historyEvent.HasVisibility(Visibility) || !historyEvent.MatchesFamily(subjectId) || !XjHistoryNarrativeTemplateEngine.CanWritePerspective(historyEvent.Source, Visibility)) return false;
		string familyName = historyEvent.FamilyId == subjectId || subjectId <= 0L
			? historyEvent.FamilyName
			: historyEvent.RelatedFamilyName;
		return XjHistoryNamePolicy.IsChineseFamilyName(familyName);
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

