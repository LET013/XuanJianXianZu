namespace XuanJianVNext.Patches;

/// <summary>
/// P0 native-window quarantine.
///
/// UnitWindow/CityWindow/KingdomWindow are fully owned by WorldBox. XuanJian must
/// not patch their open/show/layout lifecycle, clone native children, inject rows,
/// buttons or panels, or schedule deferred writes into a closing native window.
///
/// These compatibility methods remain because older optional UI helpers may call
/// them, but they intentionally perform no native-window mutation. XuanJian data is
/// exposed through XuanJian-owned windows/codex/rank surfaces instead.
/// </summary>
internal partial class XjVNextPatches
{
    internal static void XuanJianVNext_RequestUnitWindowRefresh(
        UnitWindow window,
        bool includeStatsRows,
        bool force = false)
    {
        // Compatibility no-op. Native UnitWindow is a read-only boundary for XuanJian.
    }

    internal static void XuanJianVNext_ExecuteUnitWindowRefresh(
        UnitWindow window,
        bool includeStatsRows)
    {
        // Compatibility no-op.
    }
}
