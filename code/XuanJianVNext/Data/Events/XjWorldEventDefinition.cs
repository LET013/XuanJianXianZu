using System;

namespace XuanJianVNext.Data.Events;

internal enum XjWorldEventSchedulePolicy : byte
{
	AnnualDispatcher = 1,
	DecennialDispatcher = 2
}

/// <summary>
/// 世界事件的运行期目录项。定义只描述调度、并发上限与历史通道，
/// 事件自身状态仍由各处理器持久化，不把所有事件硬塞进通用奖励模型。
/// </summary>
internal sealed class XjWorldEventDefinition
{
	internal XjWorldEventDefinition(
		string id,
		XjWorldEventSchedulePolicy schedulePolicy,
		int maximumActiveCount,
		string eligibilityProvider,
		string historyAdapter,
		Action<int> resolver,
		Func<bool> configGate = null)
	{
		Id = id ?? string.Empty;
		SchedulePolicy = schedulePolicy;
		MaximumActiveCount = Math.Max(1, maximumActiveCount);
		EligibilityProvider = eligibilityProvider ?? string.Empty;
		HistoryAdapter = historyAdapter ?? string.Empty;
		Resolver = resolver;
		ConfigGate = configGate;
	}

	internal string Id { get; }
	internal XjWorldEventSchedulePolicy SchedulePolicy { get; }
	internal int MaximumActiveCount { get; }
	internal string EligibilityProvider { get; }
	internal string HistoryAdapter { get; }
	internal Action<int> Resolver { get; }
	internal Func<bool> ConfigGate { get; }
}
