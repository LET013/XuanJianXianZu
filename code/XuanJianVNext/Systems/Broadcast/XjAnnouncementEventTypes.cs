namespace XuanJianVNext.Systems.Broadcast;

/// <summary>
/// 需要在天下纪事中保留独立语义的高境S级公告类型。
/// 不依赖公告正文关键词推断，避免“果位封锁”和“余闰夺正”被归入普通生死或权柄事件。
/// </summary>
internal static class XjAnnouncementEventTypes
{
	internal const string GuoWeiZhongAiZhengWeiLocked = "GuoWeiZhongAiZhengWeiLocked";
	internal const string SameDaoTuZhengWeiSuccession = "SameDaoTuZhengWeiSuccession";
	internal const string AdjacentDaoTuZhengWeiSuccession = "AdjacentDaoTuZhengWeiSuccession";
	internal const string GuoWeiOwnerRevealedByProbe = "GuoWeiOwnerRevealedByProbe";
}
