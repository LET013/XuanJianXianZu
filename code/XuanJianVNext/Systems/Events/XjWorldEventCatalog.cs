using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// 统一世界事件目录。年度车道只遍历少量已注册定义，不再让每个新事件自行
/// 挂一套世界入口；候选索引、状态和结果仍由事件专属处理器负责。
/// </summary>
internal static class XjWorldEventCatalog
{
	private const string HongXiaMilestoneId = "hongxia_luoxia_300";
	private const string YuanZhaoMilestoneId = "yuanzhao_kongzheng_500";
	private static int _lastFixedMilestoneTickYear;

	private static readonly XjWorldEventDefinition[] Definitions =
	{
		new XjWorldEventDefinition(
			"opening_zixuan_borrowed_lifespan",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"世界开局年份",
			"天下旧事",
			XjZiXuanBorrowedLifespanEvent.TickYear),
		new XjWorldEventDefinition(
			"hongxia_luoxia_300",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"玄鉴历第300年",
			"薛畋空证虹霞、落霞山显世",
			XjHongXiaLuoXiaEvent.TickYear),
		new XjWorldEventDefinition(
			"yuanzhao_kongzheng_500",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"玄鉴历第500年",
			"渊照空证与水月照真隐居",
			XjYuanZhaoKongZhengEvent.TickYear),
		new XjWorldEventDefinition(
			"yuanzhao_founder_audience",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"空证以后，只有真正牵动源流、正果或权柄的高位因缘才可能惊动水月；一次传召之后往往数百年无声。",
			"水月传召、渊照后学觐见与隔水应照",
			XjYuanZhaoFounderAudienceSystem.TickYear),
		new XjWorldEventDefinition(
			"yuanzhao_credential",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"水月照真偶有无字请凭函流入尘世，函认其人而不认持有者。",
			"照真请凭函与水月外层游历",
			XjYuanZhaoCredentialSystem.TickYear),
		new XjWorldEventDefinition(
			"event_dongtian",
			XjWorldEventSchedulePolicy.AnnualDispatcher,
			1,
			"真人级缓存＋限量下修候选",
			"洞天事件史册适配",
			XjEventDongTianSystem.TickYear)
	};

	internal static bool HasOpeningPending => XjZiXuanBorrowedLifespanEvent.HasPendingAfterLoad;

	internal static void ScheduleAfterLoad(int currentYear)
	{
		XjZiXuanBorrowedLifespanEvent.ScheduleAfterLoad(currentYear);
	}

	internal static void TickOpening()
	{
		XjZiXuanBorrowedLifespanEvent.TickAfterLoad();
	}

	internal static void ClearRuntime()
	{
		XjZiXuanBorrowedLifespanEvent.ClearRuntimePending();
		_lastFixedMilestoneTickYear = 0;
	}

	/// <summary>
	/// 虹霞三百年、渊照五百年属于固定世界里程碑，不能被高倍速下的年度逐年追赶队列拖后。
	/// 这里直接使用“已请求的最新世界年”，每个真实世界年最多执行一次；事件自身仍负责持久化与幂等。
	/// </summary>
	internal static void TickFixedMilestones(int liveWorldYear)
	{
		if (liveWorldYear <= 0 || liveWorldYear == _lastFixedMilestoneTickYear) return;
		_lastFixedMilestoneTickYear = liveWorldYear;
		XjHongXiaLuoXiaEvent.TickYear(liveWorldYear);
		XjYuanZhaoKongZhengEvent.TickYear(liveWorldYear);
	}

	internal static void TickAnnual(int currentYear)
	{
		if (currentYear <= 0) return;
		for (int i = 0; i < Definitions.Length; i++)
		{
			XjWorldEventDefinition definition = Definitions[i];
			if (definition.SchedulePolicy != XjWorldEventSchedulePolicy.AnnualDispatcher
				|| definition.Resolver == null
				|| string.Equals(definition.Id, HongXiaMilestoneId, StringComparison.Ordinal)
				|| string.Equals(definition.Id, YuanZhaoMilestoneId, StringComparison.Ordinal)
				|| (definition.ConfigGate != null && !definition.ConfigGate())) continue;
			definition.Resolver(currentYear);
		}
	}

	internal static IReadOnlyList<XjWorldEventDefinition> ReadDefinitions() => Definitions;
}
