using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjHighRealmDetectionContext
{
    internal readonly Actor Actor;
    internal readonly XjActorCultivationSnapshot Snapshot;
    internal readonly int CurrentYear;
    internal readonly int Budget;
    internal readonly int Spent;

    internal bool HasBudget => Spent < Budget;

    internal bool IsZiFu => string.Equals(Snapshot.RealmId, XjRealmIds.ZiFu, System.StringComparison.Ordinal);
    internal bool IsJinDan => string.Equals(Snapshot.RealmId, XjRealmIds.JinDan, System.StringComparison.Ordinal);
    internal bool IsShenDan => string.Equals(Snapshot.RealmId, XjRealmIds.ShenDan, System.StringComparison.Ordinal);
    internal bool IsJinDanLike => IsJinDan || IsShenDan;

    internal XjHighRealmDetectionContext(
        Actor actor,
        XjActorCultivationSnapshot snapshot,
        int currentYear,
        int budget,
        int spent = 0)
    {
        Actor = actor;
        Snapshot = snapshot;
        CurrentYear = currentYear < 0 ? 0 : currentYear;
        Budget = budget < 1 ? 1 : budget;
        Spent = spent < 0 ? 0 : spent;
    }

    internal XjHighRealmDetectionContext WithIncrement()
    {
        return new XjHighRealmDetectionContext(Actor, Snapshot, CurrentYear, Budget, Spent + 1);
    }
}
