namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 年度世界追赶策略。当前态维护只在最新年执行；固定边界任务只在命中周期时进入。
/// 逐年机会与累计产出由各自领域直接表达，不为未使用的策略预留运行时枚举。
/// </summary>
internal enum XjAnnualCatchUpMode : byte
{
	CollapseToLatest = 0,
	BoundaryOnly = 1
}

internal static class XjAnnualWorkPolicy
{
	internal static bool ShouldRun(XjAnnualCatchUpMode mode, int activeYear, int latestRequestedYear, int intervalYears = 1)
	{
		if (activeYear <= 0) return false;
		return mode switch
		{
			XjAnnualCatchUpMode.CollapseToLatest => activeYear >= latestRequestedYear,
			XjAnnualCatchUpMode.BoundaryOnly => intervalYears > 0 && activeYear % intervalYears == 0,
			_ => false
		};
	}
}
