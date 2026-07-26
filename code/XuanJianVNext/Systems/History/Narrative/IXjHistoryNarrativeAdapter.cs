using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>
/// 一卷史册只通过自己的适配器读取统一事件底本。
/// 适配器同时负责收录边界与叙述视角，禁止UI直接把世界历史原文当作分卷正文。
/// </summary>
internal interface IXjHistoryNarrativeAdapter
{
	XjHistoryVisibility Visibility { get; }

	bool CanWrite(XjHistoryEvent historyEvent, long subjectId);

	XjHistoryNarrativeEntry Compose(XjHistoryEvent historyEvent, long subjectId, string subjectName);
}
