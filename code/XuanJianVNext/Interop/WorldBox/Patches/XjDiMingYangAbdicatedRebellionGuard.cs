using System;
using System.Reflection;
using HarmonyLib;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Patches;

/// <summary>
/// 主动禅帝的前帝明阳已经明确退出帝统政治竞争。
/// 除了玄鉴自己的谋国入口外，这里进一步拦截 WorldBox 原生 tryStartPlot：
/// 无论是原生 AI 还是第三方政治模组，只要试图以已禅帝者为谋主发动 rebellion，
/// 都直接拒绝；其他 Plot 与其他角色完全不受影响。
/// </summary>
[HarmonyPatch]
internal static class XjDiMingYangAbdicatedRebellionGuard
{
    private static MethodBase TargetMethod()
    {
        try
        {
            FieldInfo worldField = AccessTools.Field(typeof(World), "world");
            Type worldType = worldField?.FieldType;
            if (worldType == null) return null;

            FieldInfo plotsField = AccessTools.Field(worldType, "plots");
            Type plotsType = plotsField?.FieldType;
            if (plotsType == null)
            {
                PropertyInfo plotsProperty = AccessTools.Property(worldType, "plots");
                plotsType = plotsProperty?.PropertyType;
            }
            if (plotsType == null) return null;

            MethodInfo[] methods = plotsType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null || method.Name != "tryStartPlot" || method.ReturnType != typeof(bool)) continue;
                ParameterInfo[] parameters = method.GetParameters();
                bool hasActor = false;
                bool hasPlot = false;
                for (int p = 0; p < parameters.Length; p++)
                {
                    Type parameterType = parameters[p].ParameterType;
                    if (typeof(Actor).IsAssignableFrom(parameterType)) hasActor = true;
                    if (typeof(PlotAsset).IsAssignableFrom(parameterType)) hasPlot = true;
                }
                if (hasActor && hasPlot) return method;
            }
        }
        catch
        {
        }
        return null;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(object[] __args, ref bool __result)
    {
        if (__args == null || __args.Length == 0) return true;

        Actor actor = null;
        PlotAsset plot = null;
        for (int i = 0; i < __args.Length; i++)
        {
            if (actor == null && __args[i] is Actor actorArg) actor = actorArg;
            if (plot == null && __args[i] is PlotAsset plotArg) plot = plotArg;
        }
        if (actor?.data == null || plot == null || !XjXianGuoSystem.HasAbdicatedImperialIdentity(actor)) return true;

        bool isRebellion = false;
        try
        {
            isRebellion = ReferenceEquals(plot, PlotsLibrary.rebellion)
                || string.Equals(plot.id, "rebellion", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            try { isRebellion = string.Equals(plot.id, "rebellion", StringComparison.OrdinalIgnoreCase); }
            catch { isRebellion = false; }
        }
        if (!isRebellion) return true;

        __result = false;
        return false;
    }
}
