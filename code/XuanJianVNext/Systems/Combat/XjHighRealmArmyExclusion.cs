using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Retired compatibility shell. High-realm cultivators may participate in the native
/// military exactly as WorldBox decides. Sect-war/high-realm combat remains a
/// XuanJian gameplay layer, but it no longer vetoes recruitment or demobilizes actors.
/// </summary>
internal static class XjHighRealmArmyExclusion
{
    internal static bool HasPending => false;
    internal static bool ShouldRejectNativeWarriorRecruitment(Actor actor) => false;
    internal static void EnqueueIfHighRealm(Actor actor) { }
    internal static void FlushBeforeSave() { }
    internal static void TickQueue(int budget) { }
    internal static void TickQueue(XjCooperativeBudget budget) { }
    internal static void Clear() { }
}
