using System;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// Retired compatibility shell. WorldBox is the sole authority for Army, captain,
/// warrior, City.army and ArmyManager lifecycle.
///
/// The former implementation reflected into native Army/City/Actor fields, assumed
/// multiple armies in one city were duplicates, retired Army objects, and mutated
/// army state during save/load/runtime repair. That can create a repair/refill loop
/// when the native game intentionally maintains more than one military group.
///
/// Keep the public/internal surface so older callers compile, but every operation is
/// intentionally non-mutating. If an Army defect remains, diagnose it first; do not
/// reconstruct WorldBox military state from XuanJian.
/// </summary>
internal static class XjNativeArmySaveGuard
{
    internal static void SanitizeBeforeSave() { }
    internal static void ScheduleAfterLoadInspection() { }
    internal static bool HasPendingRuntimeInspection => false;
    internal static void TickRuntimeInspection(int budget) { }
    internal static bool DetachActorFromNativeArmy(Actor actor, string reason) => false;
    internal static void EnqueueTouchedCity(City city) { }
    internal static void RequestRuntimeDuplicateInspection() { }
    internal static void ClearRuntime() { }
    internal static void SanitizeCityArmyReference(City city) { }
    internal static bool RecoverCityArmyReference(City city, Exception exception, string source) => false;
    internal static void SanitizeActorArmyReference(Actor actor) { }
}
