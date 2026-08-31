using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 紫府大真人的“求金之志”。
///
/// 旧逻辑在所有求金硬条件已经满足后，仍按年抽取一次 80%/90% 的“是否触发求金”，
/// 会让人物只是因为年度骰子连续未中而无意义地滞留紫府。现在改为人物决意：
/// - 五、六档根骨：条件齐备即志在叩金，当前结算立即进入真实求金；
/// - 四档根骨：默认自知不足而止步紫府，不自行叩门；
/// - 四档被上修扶金选中：培养未成熟时静候扶金，法成候金后重启求金。
///
/// 该状态只在“资质 / 道胎之姿 / 上修扶金阶段”签名变化时持久化更新，
/// 不按年重新抽签；年度管线只读取稳定状态，因此没有新增扫描或概率债务。
/// </summary>
internal static class XjQiuJinIntentSystem
{
    internal const int StateUnset = 0;
    internal const int StateSeekGold = 1;
    internal const int StateHoldZiFu = 2;
    internal const int StateAwaitUpperGuidance = 3;

    internal readonly struct Decision
    {
        internal readonly int State;
        internal readonly string Reason;
        internal readonly bool AllowsAttempt;

        internal Decision(int state, string reason, bool allowsAttempt)
        {
            State = state;
            Reason = reason ?? string.Empty;
            AllowsAttempt = allowsAttempt;
        }
    }

    /// <summary>
    /// 仅供已经走到金门前的紫府主链调用。upperGuidanceStage 由扶金系统一次读取后传入，
    /// 避免同一年度重复解析扶金项目。
    /// </summary>
    internal static Decision ResolveForEligibleZiFu(
        Actor actor,
        in XjActorCultivationSnapshot snapshot,
        int currentYear,
        int upperGuidanceStage)
    {
        if (actor?.data == null)
        {
            return new Decision(StateUnset, "ActorInvalid", false);
        }

        bool daoTaiPosture = actor.hasTrait("ChuShen8");
        int aptitude = Math.Clamp(snapshot.XjZz, 0, 9);

        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinIntentState, out int storedState);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinIntentSignature, out int storedSignature);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinIntentReason, out string storedReason);

        // 上修已经完整补基、演法并定成求金法以后，这份“成熟指引”属于目标本人已经获得的
        // 修行认知，不再依赖主持者继续活着。扶金项目后来因上修死亡/失格而清除，也不能让
        // 四档大真人重新失忆退回【止步紫府】。只有资质/修法本身发生变化并主动 Clear 时才重置。
        int guidanceStage = ResolveEffectiveGuidanceStage(
            aptitude, daoTaiPosture, upperGuidanceStage, storedState, storedReason);
        int signature = BuildSignature(aptitude, daoTaiPosture, guidanceStage);
        if (storedSignature == signature && IsValidState(storedState))
        {
            return new Decision(storedState, storedReason, storedState == StateSeekGold);
        }

        Decision next = ResolveDecision(aptitude, daoTaiPosture, guidanceStage);
        Persist(actor, next, signature, currentYear);
        return next;
    }

    /// <summary>
    /// 纯读取显示，不在人物面板构建期间反向写角色数据。
    /// </summary>
    internal static string BuildDisplaySummary(Actor actor, in XjActorCultivationSnapshot snapshot)
    {
        if (actor?.data == null
            || !XjCultivationPathRules.IsZiFuJinDan(actor)
            || !string.Equals(XjRealmHelper.NormalizeId(snapshot.RealmId), XjRealmIds.ZiFu, StringComparison.Ordinal))
        {
            return string.Empty;
        }

        if (!XjXianJiAccessor.HasFive(actor))
        {
            // 求金之志只在五门圆满、真正成为紫府大真人后出现。普通紫府不显示“尚未立志”
            // 这一占位栏，避免把尚在积修的人物提前写成已经开始议金。
            return string.Empty;
        }

        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinIntentState, out int state);
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinIntentSignature, out int storedSignature);
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinIntentReason, out string reason);

        // 人物页保持纯读取，但不能盲信旧持久化状态：玩家手动改资质、上修刚选中目标等变化
        // 可能先于下一次年度高境事务发生。用当前权威条件计算签名，不一致时只临时推导显示，
        // 不在 UI 读模型里反向落盘，也不会出现“资质已经变了，面板还显示旧求金之志”。
        bool daoTaiPosture = actor.hasTrait("ChuShen8");
        int aptitude = Math.Clamp(snapshot.XjZz, 0, 9);
        int activeGuidanceStage = aptitude == 4 && !daoTaiPosture
            ? XjUpperCultivatorGoldSupportSystem.ResolveJinDanGuidanceStage(actor)
            : 0;
        int effectiveGuidanceStage = ResolveEffectiveGuidanceStage(
            aptitude, daoTaiPosture, activeGuidanceStage, state, reason);
        int currentSignature = BuildSignature(aptitude, daoTaiPosture, effectiveGuidanceStage);
        if (!IsValidState(state) || storedSignature != currentSignature)
        {
            Decision derived = ResolveDecision(aptitude, daoTaiPosture, effectiveGuidanceStage);
            return FormatDisplay(derived.State, derived.Reason);
        }

        return FormatDisplay(state, reason);
    }

    private static string FormatDisplay(int state, string reason)
    {
        return state switch
        {
            StateSeekGold when string.Equals(reason, "UpperGuidanceMature", StringComparison.Ordinal)
                => "得上修指引——根基短处已有高修补正，求金之志已复，条件齐备即叩金门。",
            StateSeekGold when string.Equals(reason, "DaoTaiPosture", StringComparison.Ordinal)
                => "志在叩金——道胎之姿不疑自身承载，金门条件齐备即行证道。",
            StateSeekGold
                => "志在叩金——自认根基足以承金，金门条件齐备即行证道。",
            StateAwaitUpperGuidance
                => "静候扶金——已得上修青眼，暂不自行叩门，待授业、演法与求金法全部成熟。",
            StateHoldZiFu
                => "止步紫府——自知根基不足以独承金性，不会自行叩金；若得上修完整指引，可重新立志。",
            _ => string.Empty
        };
    }

    /// <summary>
    /// 上修扶金推进到“法成候金”时立即固化成熟指引。这样即便主持者在目标真正叩门前死亡，
    /// 人物已经获得的补基、演法与专属求金认知也不会被项目清理反向抹掉。
    /// </summary>
    internal static void MarkUpperGuidanceMature(Actor actor, int currentYear)
    {
        if (actor?.data == null || !XjXianJiAccessor.HasFive(actor)) return;
        XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
        if (snapshot.XjZz != 4 || actor.hasTrait("ChuShen8")) return;
        int signature = BuildSignature(4, false, 4);
        Persist(actor, new Decision(StateSeekGold, "UpperGuidanceMature", true), signature, currentYear);
    }

    internal static void Clear(Actor actor)
    {
        if (actor?.data == null) return;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentState, StateUnset);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentYear, 0);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinIntentReason, string.Empty);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentSignature, 0);
    }

    private static Decision ResolveDecision(int aptitude, bool daoTaiPosture, int guidanceStage)
    {
        if (daoTaiPosture)
        {
            return new Decision(StateSeekGold, "DaoTaiPosture", true);
        }

        if (aptitude >= 5 && aptitude <= 6)
        {
            return new Decision(StateSeekGold, "AptitudeConfident", true);
        }

        if (aptitude == 4)
        {
            if (guidanceStage >= 4)
            {
                return new Decision(StateSeekGold, "UpperGuidanceMature", true);
            }
            if (guidanceStage > 0)
            {
                return new Decision(StateAwaitUpperGuidance, "UpperGuidanceInProgress", false);
            }
            return new Decision(StateHoldZiFu, "RootInsufficient", false);
        }

        return new Decision(StateHoldZiFu, "RootBelowGoldThreshold", false);
    }

    private static int ResolveEffectiveGuidanceStage(
        int aptitude,
        bool daoTaiPosture,
        int activeGuidanceStage,
        int storedState,
        string storedReason)
    {
        int guidanceStage = Math.Clamp(activeGuidanceStage, 0, 4);
        if (aptitude == 4
            && !daoTaiPosture
            && storedState == StateSeekGold
            && string.Equals(storedReason, "UpperGuidanceMature", StringComparison.Ordinal))
        {
            return 4;
        }
        return guidanceStage;
    }

    private static int BuildSignature(int aptitude, bool daoTaiPosture, int guidanceStage)
    {
        // 小型稳定签名足够覆盖会改变“求金之志”的三项权威条件。
        // 不混入年份，因此同一状态不会在年度管线中反复改写。
        return aptitude * 100 + (daoTaiPosture ? 10 : 0) + guidanceStage;
    }

    private static void Persist(Actor actor, in Decision decision, int signature, int currentYear)
    {
        int safeYear = Math.Max(1, currentYear);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentState, decision.State);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentYear, safeYear);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinIntentReason, decision.Reason);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinIntentSignature, signature);
    }

    private static bool IsValidState(int state)
    {
        return state == StateSeekGold
            || state == StateHoldZiFu
            || state == StateAwaitUpperGuidance;
    }
}
