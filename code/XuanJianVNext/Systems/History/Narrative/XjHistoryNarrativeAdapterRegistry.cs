using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>三卷适配器的唯一入口；避免各页面各自判断和重复改写事件。</summary>
internal static class XjHistoryNarrativeAdapterRegistry
{
	internal static IXjHistoryNarrativeAdapter Resolve(XjHistoryVisibility visibility)
	{
		return visibility switch
		{
			XjHistoryVisibility.Personal => PersonalNarrativeAdapter.Shared,
			XjHistoryVisibility.Family => FamilyNarrativeAdapter.Shared,
			XjHistoryVisibility.Sect => SectNarrativeAdapter.Shared,
			_ => null
		};
	}
}
