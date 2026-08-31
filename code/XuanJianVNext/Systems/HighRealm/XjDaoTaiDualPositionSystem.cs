using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 道胎双位闭环：道胎保留自己证道时的真实位序，并可再合一处互补位序。
/// 合法组合始终是“一果 + 一余/闰”；原位既可以是果，也可以是余/闰。
/// 后得位序使用独立占用账本，避免普通 Registry 的“一人一活动位序”规则互相挤占。
/// </summary>
internal static class XjDaoTaiDualPositionSystem
{
    internal static void TickActor(Actor actor, int currentYear)
    {
        if (actor?.data == null || !XjSafeCore.IsAliveActor(actor)) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        if (!XjDaoTaiSpellScale.IsDaoTaiActor(actor))
        {
            Release(actor, currentYear, "DaoTaiInvalid");
            return;
        }

        if (!TryResolveAnchorPosition(actor, out XjDerivedPositionArchiveRecord primary))
        {
            Release(actor, currentYear, "PrimaryPositionLost");
            return;
        }

        if (XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
            && binding != null)
        {
            if (IsBindingStillValid(actor, primary, binding))
            {
                SyncActorProjection(actor, binding);
                SynchronizeBoundFruitAuthority(actor, binding, currentYear);
                XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
                return;
            }
            Release(actor, currentYear, "BindingInvalid");
        }

        if (!TrySelectComplementaryPosition(actor, primary, Math.Max(1, currentYear), out XjDerivedPositionArchiveRecord secondary))
        {
            ClearActorProjection(actor);
            return;
        }

        if (!XjFruitPositionWorldState.TryBindDaoTaiSecondary(actor, primary.PositionId, secondary.PositionId,
                Math.Max(1, currentYear), out _)) return;

        if (XjFruitPositionWorldState.TryGetDaoTaiBinding(actorId, out XjDaoTaiPositionBindingArchiveRecord established)
            && established != null)
        {
            SyncActorProjection(actor, established);
            SynchronizeBoundFruitAuthority(actor, established, currentYear);
            XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
            string title = GetBindingTitle(established);
            if (TryResolveBindingPair(established, out XjDerivedPositionArchiveRecord fruit,
                    out XjDerivedPositionArchiveRecord derived))
            {
                string text = actor.getName() + "将【" + XjGuoWeiCalculator.GetDisplayGuoWeiName(fruit.PositionId)
                    + "】与【" + XjGuoWeiCalculator.GetDisplayGuoWeiName(derived.PositionId)
                    + "】两处真实位序相合，成【" + title + "】之局。";
                XjWorldHistoryStore.RecordActorEvent(actor, text, XjEventIconCatalog.JinDanUpgrade);
            }
        }
    }

    private static bool TryResolveAnchorPosition(Actor actor, out XjDerivedPositionArchiveRecord primary)
    {
        primary = null;
        if (actor?.data == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;
        XjJinDanState carrier = XjJinDanAccessor.BuildPositionCarrierState(actor);
        if (!carrier.Found) return false;
        string primaryId = XjGuoWeiCalculator.NormalizeGuoWeiName(carrier.GuoWei);
        if (primaryId.Length == 0
            || !XjFruitPositionWorldState.TryGetPosition(primaryId, out primary)
            || primary == null
            || (!IsFruit(primary) && !IsDerived(primary))) return false;
        return XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(actorId, out XjGuoWeiRegistryEntry active)
            && string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(active.GuoWei), primaryId, StringComparison.Ordinal);
    }

    private static bool IsBindingStillValid(
        Actor actor,
        XjDerivedPositionArchiveRecord primary,
        XjDaoTaiPositionBindingArchiveRecord binding)
    {
        if (actor?.data == null || primary == null || binding == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (binding.ActorId != actorId
            || !string.Equals(binding.PrimaryPositionId, primary.PositionId, StringComparison.Ordinal)
            || !XjFruitPositionWorldState.TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
            || secondary == null
            || !IsComplementaryPair(primary, secondary)
            || !IsDaoTuCompatibleForDualPosition(primary, secondary)
            || XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(secondary.DaoTu, secondary.PositionType, secondary.PositionId)
            || XjFruitPositionWorldState.IsDaoTaiSecondaryOccupiedByOther(secondary.PositionId, actorId)
            || XjFruitPositionWorldState.IsNormallyOccupiedByOther(secondary.PositionId, actorId))
        {
            return false;
        }

        // 道势回落只阻止空缺席位被重新补入；已经在世的道胎双位不会因此被反向抹除。
        return true;
    }

    private static bool TrySelectComplementaryPosition(
        Actor actor,
        XjDerivedPositionArchiveRecord primary,
        int currentYear,
        out XjDerivedPositionArchiveRecord best)
    {
        best = null;
        if (actor?.data == null || primary == null) return false;
        return IsFruit(primary)
            ? TrySelectDerivedPosition(actor, primary, out best)
            : TrySelectFruitPosition(actor, primary, currentYear, out best);
    }

    private static bool TrySelectFruitPosition(
        Actor actor,
        XjDerivedPositionArchiveRecord primary,
        int currentYear,
        out XjDerivedPositionArchiveRecord fruit)
    {
        fruit = null;
        if (actor?.data == null || primary == null || !IsDerived(primary)) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        string primaryDaoTu = Normalize(primary.DaoTu);
        if (actorId <= 0L || primaryDaoTu.Length == 0) return false;

        // 保留原有“本道正果”懒建档：余/闰成道胎后至少总能检查自己根道的正果。
        // 其它相关道途的正果只读取世界里已经真实存在的位序，不因一次年度检查凭空开果。
        string nativeFruitId = XjGuoWeiCalculator.BuildGuoWeiSlotName(primaryDaoTu, XjGuoWeiCalculator.ZhengWei, 1);
        if (!XjFruitPositionWorldState.TryGetPosition(nativeFruitId, out XjDerivedPositionArchiveRecord nativeFruit)
            || nativeFruit == null)
        {
            XjJinDanState carrier = XjJinDanAccessor.BuildPositionCarrierState(actor);
            XjFruitPositionWorldState.EnsurePosition(
                actor,
                primaryDaoTu,
                XjGuoWeiCalculator.ZhengWei,
                nativeFruitId,
                carrier.JinXing,
                string.Empty,
                Math.Max(1, currentYear));
        }

        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, out string intendedDaoTu);
        intendedDaoTu = Normalize(intendedDaoTu);
        string manifestDaoTu = Normalize(primary.ExternalDaoTu);
        IReadOnlyList<XjDerivedPositionArchiveRecord> positions = XjFruitPositionWorldState.ReadPositionsSnapshot();
        int bestScore = int.MinValue;
        for (int i = 0; i < positions.Count; i++)
        {
            XjDerivedPositionArchiveRecord candidate = positions[i];
            if (candidate == null || !IsFruit(candidate)) continue;
            string candidateDaoTu = Normalize(candidate.DaoTu);
            int relationScore = ResolveDualDaoTuRelationScore(primaryDaoTu, candidateDaoTu);
            if (relationScore < 0
                || XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(candidate.DaoTu, candidate.PositionType, candidate.PositionId)
                || XjFruitPositionWorldState.IsDaoTaiSecondaryOccupiedByOther(candidate.PositionId, actorId)
                || XjFruitPositionWorldState.IsNormallyOccupiedByOther(candidate.PositionId, actorId)) continue;

            bool sameDao = string.Equals(candidateDaoTu, primaryDaoTu, StringComparison.Ordinal);
            bool directAdjacent = !sameDao
                && XjDaoTuRelationCatalog.Resolve(primaryDaoTu, candidateDaoTu) == XjDaoTuRelationKind.DirectAdjacent;
            bool explicitIntent = intendedDaoTu.Length > 0
                && string.Equals(candidateDaoTu, intendedDaoTu, StringComparison.Ordinal);
            bool manifestTarget = manifestDaoTu.Length > 0
                && string.Equals(candidateDaoTu, manifestDaoTu, StringComparison.Ordinal);
            bool evidence = HasAuthorityEvidence(actor, candidate);

            // 道网远亲不是“随机候选池”。只有明确求位意向、原闰位显道或既有权柄经历
            // 才能把 Counterpart / SameRootRemote / ElementAffinity 转成第二果。
            if (!sameDao && !directAdjacent && !explicitIntent && !manifestTarget && !evidence) continue;

            int score = relationScore;
            if (manifestTarget) score += 500;
            if (explicitIntent) score += 350;
            if (evidence) score += 180;
            score += Math.Max(0, 200 - Math.Min(200, candidate.FoundedYear / 50));
            if (fruit == null || score > bestScore
                || (score == bestScore && candidate.FoundedYear < fruit.FoundedYear)
                || (score == bestScore && candidate.FoundedYear == fruit.FoundedYear
                    && string.Compare(candidate.PositionId, fruit.PositionId, StringComparison.Ordinal) < 0))
            {
                fruit = candidate;
                bestScore = score;
            }
        }
        return fruit != null;
    }

    private static bool TrySelectDerivedPosition(
        Actor actor,
        XjDerivedPositionArchiveRecord primary,
        out XjDerivedPositionArchiveRecord best)
    {
        best = null;
        if (actor?.data == null || primary == null || !IsFruit(primary)) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        string primaryDaoTu = Normalize(primary.DaoTu);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, out string intendedDaoTu);
        intendedDaoTu = Normalize(intendedDaoTu);

        IReadOnlyList<XjDerivedPositionArchiveRecord> positions = XjFruitPositionWorldState.ReadPositionsSnapshot();
        int bestScore = int.MinValue;
        for (int i = 0; i < positions.Count; i++)
        {
            XjDerivedPositionArchiveRecord candidate = positions[i];
            if (candidate == null
                || !IsDerived(candidate)
                || candidate.LegacyBeyondCapacity
                || XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(candidate.DaoTu, candidate.PositionType, candidate.PositionId)
                || XjFruitPositionWorldState.IsDaoTaiSecondaryOccupied(candidate.PositionId)
                || XjFruitPositionWorldState.IsNormallyOccupiedByOther(candidate.PositionId, actorId)) continue;

            string candidateDaoTu = Normalize(candidate.DaoTu);
            int relationScore = ResolveDualDaoTuRelationScore(primaryDaoTu, candidateDaoTu);
            if (relationScore < 0) continue;

            bool sameDao = string.Equals(candidateDaoTu, primaryDaoTu, StringComparison.Ordinal);
            bool directAdjacent = !sameDao
                && XjDaoTuRelationCatalog.Resolve(primaryDaoTu, candidateDaoTu) == XjDaoTuRelationKind.DirectAdjacent;
            bool explicitIntent = intendedDaoTu.Length > 0
                && (string.Equals(candidateDaoTu, intendedDaoTu, StringComparison.Ordinal)
                    || string.Equals(Normalize(candidate.ExternalDaoTu), intendedDaoTu, StringComparison.Ordinal));
            bool structural = string.Equals(Normalize(candidate.ExternalDaoTu), primaryDaoTu, StringComparison.Ordinal)
                || string.Equals(Normalize(primary.ExternalDaoTu), candidateDaoTu, StringComparison.Ordinal);
            bool evidence = HasAuthorityEvidence(actor, candidate);

            // 同道与直接近邻可以自然合位；结构远亲必须再有意向、显道结构或权柄经历。
            // 即便角色曾经碰过某个无关权柄，关系表为 None 时也绝不允许据此越过道网。
            if (!sameDao && !directAdjacent && !explicitIntent && !structural && !evidence) continue;
            if (string.Equals(candidate.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
                && !HasRunWeiCarryingProof(actor, primary, candidate)) continue;

            int score = relationScore;
            if (explicitIntent) score += 500;
            if (structural) score += 350;
            if (evidence) score += 200;
            if (string.Equals(candidate.PositionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) score += 40;
            score += Math.Max(0, 200 - Math.Min(200, candidate.FoundedYear / 50));

            if (best == null || score > bestScore
                || (score == bestScore && candidate.FoundedYear < best.FoundedYear)
                || (score == bestScore && candidate.FoundedYear == best.FoundedYear
                    && string.Compare(candidate.PositionId, best.PositionId, StringComparison.Ordinal) < 0))
            {
                best = candidate;
                bestScore = score;
            }
        }
        return best != null;
    }

    internal static bool IsDaoTuCompatibleForDualPosition(
        XjDerivedPositionArchiveRecord left,
        XjDerivedPositionArchiveRecord right)
    {
        return left != null && right != null
            && IsComplementaryPair(left, right)
            && IsDaoTuPairCompatible(left.DaoTu, right.DaoTu);
    }

    internal static bool IsDaoTuPairCompatible(string sourceDaoTu, string targetDaoTu)
    {
        return ResolveDualDaoTuRelationScore(sourceDaoTu, targetDaoTu) >= 0;
    }

    private static int ResolveDualDaoTuRelationScore(string sourceDaoTu, string targetDaoTu)
    {
        string source = XjDaoTuRelationCatalog.Normalize(sourceDaoTu);
        string target = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
        if (source.Length == 0 || target.Length == 0) return -1;
        if (string.Equals(source, target, StringComparison.Ordinal)) return 1000;

        // 双位不是普通闰位，却仍不得越过已经明确禁止的道论边界。双向检查是因为
        // “主位/副位”取得顺序不应改变两条道能否共同成局。
        if (XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(source, target)
            || XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(target, source)) return -1;

        return XjDaoTuRelationCatalog.Resolve(source, target) switch
        {
            XjDaoTuRelationKind.DirectAdjacent => 800,
            XjDaoTuRelationKind.Counterpart => 600,
            XjDaoTuRelationKind.SameRootRemote => 500,
            XjDaoTuRelationKind.ElementAffinity => 450,
            _ => -1
        };
    }

    internal static bool HasRunWeiCarryingProof(
        Actor actor,
        XjDerivedPositionArchiveRecord primary,
        XjDerivedPositionArchiveRecord secondary)
    {
        if (actor?.data == null || primary == null || secondary == null) return false;
        string primaryDao = Normalize(primary.DaoTu);
        string secondaryDao = Normalize(secondary.DaoTu);
        if (primaryDao.Length == 0 || secondaryDao.Length == 0) return false;
        if (string.Equals(primaryDao, secondaryDao, StringComparison.Ordinal)) return true;
        if (string.Equals(Normalize(secondary.ExternalDaoTu), primaryDao, StringComparison.Ordinal)
            || string.Equals(Normalize(primary.ExternalDaoTu), secondaryDao, StringComparison.Ordinal)) return true;
        return HasAuthorityEvidence(actor, secondary);
    }

    private static bool HasAuthorityEvidence(Actor actor, XjDerivedPositionArchiveRecord secondary)
    {
        if (actor?.data == null || secondary == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        string daoTu = Normalize(secondary.DaoTu);
        string external = Normalize(secondary.ExternalDaoTu);
        string position = XjGuoWeiCalculator.NormalizeGuoWeiName(secondary.PositionId);
        if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
        {
            if (ContainsEvidence(state.SeizedQuanBingSources, daoTu, external, position)
                || ContainsEvidence(state.SeizedQuanBing, daoTu, external, position)
                || ContainsEvidence(state.ForeignQuanBing, daoTu, external, position)
                || string.Equals(Normalize(state.PendingExternalZhengWeiDaoTu), daoTu, StringComparison.Ordinal)
                || string.Equals(Normalize(state.PendingExternalZhengWeiDaoTu), external, StringComparison.Ordinal))
            {
                return true;
            }
        }
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanRunDoctrine, out string doctrine);
        return ContainsEvidence(doctrine, daoTu, external, position);
    }

    private static bool ContainsEvidence(string source, string daoTu, string external, string position)
    {
        string text = source ?? string.Empty;
        if (text.Length == 0) return false;
        return (daoTu.Length > 0 && text.IndexOf(daoTu, StringComparison.Ordinal) >= 0)
            || (external.Length > 0 && text.IndexOf(external, StringComparison.Ordinal) >= 0)
            || (position.Length > 0 && text.IndexOf(position, StringComparison.Ordinal) >= 0);
    }

    internal static bool TryResolveBindingPair(
        XjDaoTaiPositionBindingArchiveRecord binding,
        out XjDerivedPositionArchiveRecord fruit,
        out XjDerivedPositionArchiveRecord derived)
    {
        fruit = null;
        derived = null;
        if (binding == null
            || !XjFruitPositionWorldState.TryGetPosition(binding.PrimaryPositionId, out XjDerivedPositionArchiveRecord primary)
            || primary == null
            || !XjFruitPositionWorldState.TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
            || secondary == null
            || !IsComplementaryPair(primary, secondary)) return false;
        fruit = IsFruit(primary) ? primary : secondary;
        derived = IsDerived(primary) ? primary : secondary;
        return fruit != null && derived != null;
    }

    internal static string GetBindingTitle(XjDaoTaiPositionBindingArchiveRecord binding)
    {
        if (!TryResolveBindingPair(binding, out _, out XjDerivedPositionArchiveRecord derived) || derived == null)
            return "双位并持";
        return string.Equals(derived.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
            ? "持果执闰"
            : "持果兼余";
    }

    internal static bool IsComplementaryPair(XjDerivedPositionArchiveRecord left, XjDerivedPositionArchiveRecord right)
    {
        if (left == null || right == null) return false;
        return IsFruit(left) && IsDerived(right) || IsDerived(left) && IsFruit(right);
    }

    internal static bool IsFruit(XjDerivedPositionArchiveRecord position)
    {
        return position != null
            && string.Equals(position.PositionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
    }

    internal static bool IsDerived(XjDerivedPositionArchiveRecord position)
    {
        return position != null
            && (string.Equals(position.PositionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
                || string.Equals(position.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal));
    }

    private static void SynchronizeBoundFruitAuthority(
        Actor actor,
        XjDaoTaiPositionBindingArchiveRecord binding,
        int currentYear)
    {
        if (actor?.data == null || binding == null
            || !TryResolveBindingPair(binding, out XjDerivedPositionArchiveRecord fruit, out _)
            || fruit == null)
        {
            return;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        IReadOnlyList<string> roots = XjGuoWeiAuthorityCatalog.Get(fruit.DaoTu);
        string scope = roots == null || roots.Count == 0 ? string.Empty : string.Join("|", roots);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
        XjDaoLineageStateRegistry.OnPromotion(
            actorId,
            actor.getName(),
            sourceDaoTu,
            fruit.DaoTu,
            XjGuoWeiCalculator.ZhengWei,
            scope,
            Math.Max(1, currentYear),
            affectVitality: false);
    }

    internal static void SyncActorProjection(Actor actor, XjDaoTaiPositionBindingArchiveRecord binding)
    {
        if (actor?.data == null || binding == null) return;
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryPositionId, binding.SecondaryPositionId ?? string.Empty);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryPositionKind, binding.SecondaryKind ?? string.Empty);
        string authority = string.Empty;
        if (XjFruitPositionWorldState.TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
            && secondary != null)
        {
            authority = BuildPositionAuthorityScope(secondary);
        }
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryAuthorityScope, authority);
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L) XjFaBaoBonusService.Forget(actorId);
        actor.setStatsDirty();
    }

    private static string BuildPositionAuthorityScope(XjDerivedPositionArchiveRecord position)
    {
        if (position == null) return string.Empty;
        if (IsFruit(position))
        {
            IReadOnlyList<string> roots = XjGuoWeiAuthorityCatalog.Get(position.DaoTu);
            return roots == null || roots.Count == 0 ? string.Empty : string.Join("|", roots);
        }
        return MergePipe(position.DerivedAuthority, position.SecondaryDerivedAuthority);
    }

    internal static void ClearActorProjection(Actor actor)
    {
        if (actor?.data == null) return;
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryPositionId, string.Empty);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryPositionKind, string.Empty);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiSecondaryAuthorityScope, string.Empty);
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId > 0L) XjFaBaoBonusService.Forget(actorId);
        actor.setStatsDirty();
    }

    internal static string MergeEffectiveAuthorityScope(Actor actor, string primaryScope)
    {
        if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return primaryScope ?? string.Empty;
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjDaoTaiSecondaryAuthorityScope, out string secondaryScope);
        return MergePipe(primaryScope, secondaryScope);
    }

    private static void Release(Actor actor, int currentYear, string reason)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (!XjFruitPositionWorldState.ReleaseDaoTaiBinding(actorId, actor.getName(), (int)XjDaoHuiPolicy.Read(actor),
                Math.Max(1, currentYear), reason))
        {
            ClearActorProjection(actor);
        }
        // 副位一旦释放，权柄快照也必须立即回落到主位，不能等下一次年度 Tick。
        XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, Math.Max(1, currentYear));
    }

    private static string MergePipe(string left, string right)
    {
        List<string> values = new List<string>();
        AddPipe(values, left);
        AddPipe(values, right);
        return string.Join("|", values);
    }

    private static void AddPipe(List<string> values, string source)
    {
        if (values == null || string.IsNullOrWhiteSpace(source)) return;
        string[] parts = source.Split(new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string value = (parts[i] ?? string.Empty).Trim();
            if (value.Length == 0) continue;
            bool exists = false;
            for (int j = 0; j < values.Count; j++)
            {
                if (!string.Equals(values[j], value, StringComparison.Ordinal)) continue;
                exists = true;
                break;
            }
            if (!exists) values.Add(value);
        }
    }

    private static string Normalize(string value) => (value ?? string.Empty).Trim();
}
