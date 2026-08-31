using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>
/// 三卷叙事门面：统一事件先转成 XjHistoryEvent，再交给对应适配器。
/// 天下纪事不经此处改写，继续展示事实底本。
/// </summary>
internal static class XjHistoryNarrativeComposer
{
	internal static bool CanWrite(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId)
	{
		IXjHistoryNarrativeAdapter adapter = XjHistoryNarrativeAdapterRegistry.Resolve(visibility);
		if (adapter == null) return false;
		return adapter.CanWrite(XjHistoryEvent.From(item), subjectId);
	}

	internal static XjHistoryNarrativeEntry Compose(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		IXjHistoryNarrativeAdapter adapter = XjHistoryNarrativeAdapterRegistry.Resolve(visibility);
		if (adapter == null) return new XjHistoryNarrativeEntry();
		return adapter.Compose(XjHistoryEvent.From(item), subjectId, subjectName);
	}
}
