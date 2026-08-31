using System;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// 仙国政治谋划与 WorldBox 原生 Plot 的唯一写边界。
///
/// 参考《宿命之环》领主谋划的安全结构：玄鉴只保存“谁在谋划、谋划多久、目标是谁”，
/// 真正的分裂/战争仍交给原生 rebellion Plot；这里不直接指定国王、不改城归属、不反射写战争。
/// </summary>
internal static class XjNativeKingdomPlotBridge
{
    internal static bool TryStartRebellion(Actor actor)
    {
        // 已主动禅帝的旧帝明确退出帝统政治竞争。即使旧档残留谋国计划，
        // 或未来有其他玄鉴入口误调此桥，也不得再借原生 rebellion Plot 起事。
        if (actor?.data == null || XjXianGuoSystem.HasAbdicatedImperialIdentity(actor)
            || !actor.isAlive() || actor.city == null || actor.kingdom == null)
        {
            return false;
        }


        try
        {
            if (!XjWorldBoxKingdomBridge.IsNativeCityLeaderForActor(actor.city, actor) || actor.city.isCapitalCity()
                || World.world?.plots == null)
            {
                return false;
            }

            PlotAsset rebellion = null;
            try { rebellion = AssetManager.plots_library?.get("rebellion"); }
            catch { }
            if (rebellion == null)
            {
                try { rebellion = PlotsLibrary.rebellion; }
                catch { rebellion = null; }
            }
            if (rebellion == null) return false;
            return World.world.plots.tryStartPlot(actor, rebellion, true);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjNativeKingdomPlotBridge.TryStartRebellion", ex);
            return false;
        }
    }
}
