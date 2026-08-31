namespace XuanJianVNext.Core;

/// <summary>
/// 世界年份跟踪器 - 缓存当前世界年份，提供年份变化检测
/// 所有年度效果依赖此系统来节流
/// </summary>
internal static class XjYearTracker
{
    /// <summary>
    /// 缓存的世界年份
    /// </summary>
    private static int _currentYear = -1;

    /// <summary>
    /// 当前世界年份（只读）
    /// </summary>
    public static int CurrentYear => _currentYear;

    /// <summary>
    /// 刷新年份缓存（每帧在 ProcessAll 中调用）
    /// </summary>
    public static void Refresh()
    {
        try
        {
            // 尝试从 map_stats 读取年份
            int year = World.world?.map_stats?.year ?? -1;
            _currentYear = year;
        }
        catch
        {
            // World 还没初始化
            _currentYear = -1;
        }
    }

    /// <summary>
    /// 检测年份是否大于上次记录的年份（用于年度效果触发）
    /// </summary>
    /// <summary>
    /// 检测当前年份是否在某年份之后
    /// </summary>
    /// <summary>
    /// 重置跟踪器（世界销毁时调用）
    /// </summary>
    public static void Clear()
    {
        _currentYear = -1;
    }

    /// <summary>
    /// 获取actor的年龄（世界年）
    /// </summary>
}
