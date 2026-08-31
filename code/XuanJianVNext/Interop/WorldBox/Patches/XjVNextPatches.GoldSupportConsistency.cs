using System;
using HarmonyLib;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Patches;

/// <summary>
/// 扶金布局语义门禁：正果主持者不能再拿自己的已占果位做“探月”目标。
/// 独立于候选权重，既约束新布局，也能在旧档继续运行时修复已启动的错误布局。
/// </summary>
internal partial class XjVNextPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjUpperCultivatorGoldSupportSystem), "TrySelectPurposeAndTarget")]
    private static void SelectionPostfix(Actor patron, ref string purpose, ref Actor selected, ref bool __result)
    {
        if (!__result || selected?.data == null || patron?.data == null
            || !string.Equals(purpose, XjUpperCultivatorGoldSupportSystem.PurposeMoonProbe, StringComparison.Ordinal)) return;
        string targetDaoTu = ReadDaoTu(selected);
        if (targetDaoTu.Length == 0 || !HoldsZhengWei(patron, targetDaoTu)) return;

        // 同道候选本身仍然是合理扶持对象，只把荒谬的“试探自己果位”目的
        // 改成壮大本道；不重新全局选人，不制造额外扫描。
        purpose = XjUpperCultivatorGoldSupportSystem.PurposeStrengthenLineage;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjUpperCultivatorGoldSupportSystem), "AdvanceExisting")]
    private static void ActiveProjectPrefix(Actor patron, long targetId, int currentYear)
    {
        if (patron?.data == null || targetId <= 0L
            || !XjActorRegistry.ResolveKnownOrWorld(targetId, out Actor target)
            || target?.data == null || !target.isAlive()) return;
        if (!XjActorAccessor.TryGetString(target, XjActorDataKeys.XjGoldSupportPurpose, out string purpose)
            || !string.Equals(purpose, XjUpperCultivatorGoldSupportSystem.PurposeMoonProbe, StringComparison.Ordinal)) return;
        if (!XjActorAccessor.TryGetString(target, XjActorDataKeys.XjGoldSupportTargetDaoTu, out string targetDaoTu)
            || string.IsNullOrWhiteSpace(targetDaoTu)) targetDaoTu = ReadDaoTu(target);
        targetDaoTu = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
        if (targetDaoTu.Length == 0 || !HoldsZhengWei(patron, targetDaoTu)) return;

        // 旧档只改“仍在进行的项目”状态，不擦历史纪事。后续原系统会按
        // StrengthenLineage 继续推进，避免重复弹出新的自我探果事件。
        XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportPurpose, XjUpperCultivatorGoldSupportSystem.PurposeStrengthenLineage);
    }

    private static bool HoldsZhengWei(Actor actor, string daoTu)
    {
        if (actor?.data == null) return false;
        XjJinDanState state = XjJinDanAccessor.BuildState(actor);
        if (!state.Found
            || !string.Equals(ReadDaoTu(actor), XjDaoTuRelationCatalog.Normalize(daoTu), StringComparison.Ordinal)) return false;
        return string.Equals(
            XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei),
            XjGuoWeiCalculator.ZhengWei,
            StringComparison.Ordinal);
    }

    private static string ReadDaoTu(Actor actor)
    {
        if (actor?.data == null
            || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)) return string.Empty;
        return XjDaoTuRelationCatalog.Normalize(daoTu);
    }
}
