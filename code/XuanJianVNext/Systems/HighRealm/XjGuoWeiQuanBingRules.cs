using System;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiQuanBingRules
{
	internal const int QuanBingCountPerDaoTu = 6;
	internal const int ZhengWeiSlotCount = 1;
	internal const int YuWeiSlotCount = 6;
	internal const int RunWeiSlotCount = 9;
	internal const int MinimumYuWeiSlotCount = 1;
	internal const int MinimumRunWeiSlotCount = 1;
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
		return "权柄：每道6权柄；果位1、余位至少1且最多6、闰位至少1且最多9，两种修法共用同一名额池；长庚不生闰位；谪炁、下仪果位归阴司封锁，保木果位与谪炁第一余位因斩养之劫封锁；外道果位封锁500年，合道闭关10年。";
	}
}
