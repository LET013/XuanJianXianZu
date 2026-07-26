using System;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiQuanBingRules
{
	internal const int QuanBingCountPerDaoTu = 6;
	internal const int YuWeiSlotCount = 3;
	internal const int RunWeiSlotCount = 6;
	internal const int ExternalZhengWeiLockYears = 500;
	internal const int ExternalAuthorityIntegrationYears = 10;
	internal const float NonAdjacentAuthorityIntegrationSuccessChance = 0.3f;
	internal const float LocalQuanBingSeizeChance = 0.5f;
	internal const float EmptyQuanBingSeizeChance = 0.1f;
	internal const float ExternalZhengWeiAuthoritySeizeChance = 0.5f;
	internal const float SameDaoTuZhengWeiInheritanceChance = 0.8f;
	internal const float PostIntegrationSeizeChanceMultiplier = 0.8f;
	internal const int PendingZhengWeiReincarnationYiXiangCost = 500;

	internal static string FormatRuleSummary()
	{
		return "权柄：每道6权柄；余位3、闰位6；外道正位封锁500年，合道闭关10年。";
	}
}
