using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 权柄之争沿用直接争柄骨架：已经立下道途的真君为求更进一步的天地功绩，
/// 主动掀起持续五十年的权柄之争。参与者循各自道途入局，以生死争夺权柄与功绩；
/// 夺得外道权柄者可退入洞天十年融合，完成后在战争尚未结束时重新参战。
/// </summary>
internal static class XjQuanBingStruggleSystem
{
    internal const string PhaseNone = "None";
    internal const string PhaseOmen = "Omen";
    internal const string PhaseProxy = "Proxy";
    internal const string PhaseFinal = "Final";
    internal const string PhaseCooldown = "Cooldown";

    private const int MinimumParticipants = 2;
    internal const int FinalDurationYears = 50;
    internal const int CooldownYears = 25;
    internal const int PeakYiXiang = 6000;
    private const int EngageRadius = 18;
    internal const float FirstKillMeritMultiplier = 1.25f;
    private const int TargetRefreshIntervalFrames = 90;

    private static readonly List<long> ParticipantActorIds = new List<long>(16);
    private static readonly Dictionary<long, long> TargetActorIdByParticipant = new Dictionary<long, long>();
    private static readonly Dictionary<long, int> TargetRefreshFrameByParticipant = new Dictionary<long, int>();
    private static string _phase = PhaseNone;
    private static int _startYear;
    private static int _endYearExclusive;
    private static int _phaseStartedYear;
    private static int _phaseEndYearExclusive;
    private static int _cooldownEndYear;
    private static int _tension;
    private static int _proxyConflictCount;
    private static long _initiatorActorId;
    private static string _initiatorName = string.Empty;
    private static int _initialParticipantCount;
    private static int _deadParticipantCount;
    private static bool _hasExternalAuthoritySeizure;
    private static int _lastProcessedYear = -1;
    private static IReadOnlyList<long> _pendingAnnualParticipantSource = Array.Empty<long>();
    private static int _pendingAnnualYear;
    private static int _pendingAnnualParticipantIndex;
    private static bool _pendingAnnualFinalize;

    internal static bool IsActive => string.Equals(_phase, PhaseOmen, StringComparison.Ordinal)
        || string.Equals(_phase, PhaseProxy, StringComparison.Ordinal)
        || string.Equals(_phase, PhaseFinal, StringComparison.Ordinal);
    internal static bool IsFinalConflict => string.Equals(_phase, PhaseFinal, StringComparison.Ordinal);
    internal static bool HasPendingCycle => IsActive || string.Equals(_phase, PhaseCooldown, StringComparison.Ordinal);
    internal static string CurrentPhase => _phase;
    internal static int StartYear => _startYear;
    internal static int EndYearExclusive => _endYearExclusive;
    internal static int ParticipantCount => ParticipantActorIds.Count;

    internal static int GetCodexRevisionToken()
    {
        unchecked
        {
            int phase = string.Equals(_phase, PhaseFinal, StringComparison.Ordinal) ? 4
                : string.Equals(_phase, PhaseCooldown, StringComparison.Ordinal) ? 5
                : string.Equals(_phase, PhaseProxy, StringComparison.Ordinal) ? 3
                : string.Equals(_phase, PhaseOmen, StringComparison.Ordinal) ? 2
                : 1;
            return (((phase * 397) ^ _startYear) * 397 ^ _endYearExclusive) * 397
                ^ ParticipantActorIds.Count ^ (_deadParticipantCount << 8);
        }
    }

    internal static string BuildCodexStatus(int currentYear)
    {
        if (IsFinalConflict)
        {
            return "权柄终局进行中 · 诸道高境正在争柄夺势";
        }
        if (string.Equals(_phase, PhaseCooldown, StringComparison.Ordinal))
        {
            return "道争休止期 · 天地权柄暂归沉寂";
        }
        return "当前未开启权柄之争";
    }

    internal static bool HasPendingAnnualWorkForYear(int currentYear)
    {
        return currentYear >= 0
            && _pendingAnnualYear == currentYear
            && (_pendingAnnualParticipantIndex < _pendingAnnualParticipantSource.Count || _pendingAnnualFinalize);
    }

    internal static bool BeginAnnualWork(int currentYear)
    {
        if (currentYear < 0) return false;
        if (HasPendingAnnualWorkForYear(currentYear)) return true;
        if (_lastProcessedYear >= currentYear) return false;

        ClearPendingAnnualWork();
        ParticipantActorIds.Clear();
        ClearTargetCache();
        _pendingAnnualParticipantSource = XjCultivatorCache.GetZhenJunOrHigherIds();
        _pendingAnnualYear = currentYear;
        _pendingAnnualParticipantIndex = 0;
        _pendingAnnualFinalize = true;
        return true;
    }

    internal static bool TickPendingAnnualWork(int itemBudget = 4, double timeBudgetMs = 0.30d)
    {
        if (_pendingAnnualYear < 0 || (!_pendingAnnualFinalize && _pendingAnnualParticipantIndex >= _pendingAnnualParticipantSource.Count))
        {
            ClearPendingAnnualWork();
            return true;
        }

        XjCooperativeBudget budget = new XjCooperativeBudget(
            Math.Max(1, itemBudget),
            timeBudgetMs,
            XjRuntimeFramePriority.Background);
        while (_pendingAnnualParticipantIndex < _pendingAnnualParticipantSource.Count && budget.TryTake())
        {
            long candidateId = _pendingAnnualParticipantSource[_pendingAnnualParticipantIndex++];
            if (XjScheduler.ResolveActor(candidateId, out Actor actor) && IsParticipant(actor, out long actorId))
            {
                ParticipantActorIds.Add(actorId);
            }
        }
        if (_pendingAnnualParticipantIndex < _pendingAnnualParticipantSource.Count) return false;
        if (_pendingAnnualFinalize)
        {
            if (!budget.TryTake()) return false;
            _pendingAnnualFinalize = false;
            long finalizeSample = XjRuntimeDiagnostics.BeginNamedSample();
            try
            {
                FinalizeAnnualAfterParticipantRefresh(_pendingAnnualYear);
            }
            finally
            {
                XjRuntimeDiagnostics.EndNamedSample("annual.QuanBing.finalize", finalizeSample);
            }
        }
        ClearPendingAnnualWork();
        return true;
    }

    private static void FinalizeAnnualAfterParticipantRefresh(int currentYear)
    {
        _lastProcessedYear = Math.Max(_lastProcessedYear, currentYear);
        if (string.Equals(_phase, PhaseCooldown, StringComparison.Ordinal))
        {
            if (_cooldownEndYear > 0 && currentYear >= _cooldownEndYear) ResetToNone();
            return;
        }

        if (string.Equals(_phase, PhaseOmen, StringComparison.Ordinal)
            || string.Equals(_phase, PhaseProxy, StringComparison.Ordinal))
        {
            BeginFinal(currentYear);
            return;
        }

        if (string.Equals(_phase, PhaseFinal, StringComparison.Ordinal))
        {
            UpdateEffectiveDeathCount();
            ReleaseActiveCombatantsFromOrdinaryRetreat();
            if (ShouldEndFinal(currentYear))
                EnterCooldown(currentYear, "五十年权柄之争终结，诸道余众各归洞天，清点果位与所夺权柄。", true);
            else Touch();
            return;
        }

        if (ParticipantActorIds.Count < MinimumParticipants)
        {
            RefreshPeakObservation(resetOnly: true, out _);
            return;
        }
        RefreshPeakObservation(resetOnly: false, out Actor trigger);
        if (trigger != null) BeginOmen(trigger, currentYear);
    }

    internal static void ClearPendingAnnualWork()
    {
        _pendingAnnualParticipantSource = Array.Empty<long>();
        _pendingAnnualYear = -1;
        _pendingAnnualParticipantIndex = 0;
        _pendingAnnualFinalize = false;
    }

    internal static void TickAnnual(int currentYear)
    {
        if (currentYear < 0 || _lastProcessedYear >= currentYear) return;
        ClearPendingAnnualWork();
        _lastProcessedYear = currentYear;
        RefreshParticipants();
        FinalizeAnnualAfterParticipantRefresh(currentYear);
    }

    internal static bool TickCombatActor(Actor actor)
    {
        if (!IsFinalConflict || !IsParticipant(actor, out long actorId)) return false;
        if (XjAttributedFreezeRegistry.IsFrozen(actor))
        {
            // 仍视为终局参与者，但冻结期间不由自定义权柄追敌逻辑重新写攻击目标/路径。
            return true;
        }
        if (!ParticipantActorIds.Contains(actorId)) ParticipantActorIds.Add(actorId);
        if (IsWithdrawnParticipant(actor))
        {
            ClearCombatIntent(actor);
            return true;
        }
        // 太阴金丹若自身没有主动目标，权柄之争不得替它强制起手。
        // 玩家/正常行为一旦先让其持有攻击目标，本保护立即解除。
        if (XjTaiYinNonAggressionPolicy.IsPassivelyProtected(actor))
        {
            TargetActorIdByParticipant[actorId] = 0L;
            TargetRefreshFrameByParticipant[actorId] = Time.frameCount + TargetRefreshIntervalFrames;
            return true;
        }

        Actor target = ResolveCombatTarget(actor, actorId);
        if (target == null) return true;
        try
        {
            WorldTile actorTile = ((BaseSimObject)actor).current_tile;
            WorldTile targetTile = ((BaseSimObject)target).current_tile;
            if (actorTile == null || targetTile == null) return true;
            int distanceSquared = Toolbox.SquaredDistVec2(actorTile.pos, targetTile.pos);
            if (distanceSquared <= EngageRadius * EngageRadius)
            {
                actor.attackedBy = target;
                XjActorAggroBridge.ForceAggro(actor, target);
            }
            else
            {
                actor.goTo(targetTile, false, false, false, 0);
            }
        }
        catch (System.Exception xjCaught151) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjQuanBingStruggleSystem.cs:151", xjCaught151); }
        return true;
    }

    internal static void NotifyJinDanDeath(long actorId)
    {
        if (!IsActive || actorId <= 0L) return;
        if (IsFinalConflict) UpdateEffectiveDeathCount();
        else _tension = Math.Min(20, _tension + 1);
        Touch();
    }

    /// <summary>
    /// 权柄终局只记录参与者之间的有效击杀；首次击杀奖励由功绩系统在
    /// 本次死亡结算中消费，避免把“功绩加成”错误写成常驻战斗倍率。
    /// </summary>
    internal static void NotifyParticipantKill(Actor killer, long victimActorId)
    {
        if (!IsFinalConflict
            || killer?.data == null
            || victimActorId <= 0L
            || !ParticipantActorIds.Contains(victimActorId)
            || !IsParticipant(killer, out long killerActorId)
            || killerActorId == victimActorId
            || !ParticipantActorIds.Contains(killerActorId))
        {
            return;
        }

        XjActorAccessor.TryGetInt(killer, XjActorDataKeys.XjQuanBingWarKillCount, out int killCount);
        XjActorAccessor.SetInt(
            killer,
            XjActorDataKeys.XjQuanBingWarKillCount,
            Math.Min(99, Math.Max(0, killCount) + 1));
        Touch();
    }

    /// <summary>
    /// 在本轮权柄终局中消费“首次斩敌功绩奖励”。一次终局每名参与者只会
    /// 消费一次，返回值仅用于放大本次斩敌所得功绩，不影响任何战斗数值。
    /// </summary>
    internal static bool IsParticipantKill(Actor actor, long victimActorId)
    {
        return IsFinalConflict
            && victimActorId > 0L
            && ParticipantActorIds.Contains(victimActorId)
            && IsParticipant(actor, out long actorId)
            && actorId != victimActorId
            && ParticipantActorIds.Contains(actorId);
    }

    /// <summary>
    /// 只回答“这个人当前是否真实身在本轮终局”，供水月照真等事件做因缘门槛。
    /// 不把单纯金丹身份或全局 IsActive 当作入局证明。
    /// </summary>
    internal static bool IsCurrentParticipant(Actor actor)
    {
        return IsFinalConflict
            && IsParticipant(actor, out long actorId)
            && ParticipantActorIds.Contains(actorId);
    }

    internal static float TryConsumeFirstKillMeritMultiplier(Actor actor, long victimActorId)
    {
        // 死亡功绩在死亡归档队列之前结算，此时击杀数尚未由
        // XjGuoWeiQuanBingLifecycle.NotifyParticipantKill 写入。这里直接校验
        // 击杀者与死者是否都是本轮终局参与者，避免奖励永远因 killCount=0 失效。
        if (!IsParticipantKill(actor, victimActorId)
            || XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingWarMeritBonusUsed, out int used) && used > 0)
        {
            return 1f;
        }

        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarMeritBonusUsed, 1);
        Touch();
        return FirstKillMeritMultiplier;
    }

    internal static void NotifyAuthoritySeized(Actor killer, string victimDaoTu, string authority, bool externalAuthority)
    {
        if (killer?.data == null || string.IsNullOrWhiteSpace(authority)) return;
        // 史实由 XjDaoLineageStateRegistry 以“裂解→融成/归还”的结构化记录
        // 单写入。这里仅推进道争张力，避免再生成一条“已经归己”的短投影，
        // 也避免待融合权柄被误记为正式夺取。
        if (!IsActive) return;
        int currentYear = World.world?.map_stats?.year ?? 0;
        XjYuanZhaoFounderAudienceSystem.OnAuthoritySeized(killer, victimDaoTu, authority, externalAuthority, currentYear);
        _tension = Math.Min(20, _tension + (externalAuthority ? 2 : 1));
        _proxyConflictCount++;
        if (externalAuthority) _hasExternalAuthoritySeizure = true;
        long actorId = ((BaseSystemData)killer.data).id;
        if (actorId > 0L && !ParticipantActorIds.Contains(actorId)) ParticipantActorIds.Add(actorId);
        Touch();
    }

    internal static XjWorldArchiveQuanBingStruggleState ExportState()
    {
        return new XjWorldArchiveQuanBingStruggleState
        {
            Enabled = HasPendingCycle,
            Phase = _phase,
            StartYear = _startYear,
            EndYearExclusive = _endYearExclusive,
            PhaseStartedYear = _phaseStartedYear,
            PhaseEndYearExclusive = _phaseEndYearExclusive,
            CooldownEndYear = _cooldownEndYear,
            Tension = _tension,
            ProxyConflictCount = _proxyConflictCount,
            InitiatorActorId = _initiatorActorId,
            InitiatorName = _initiatorName,
            InitialParticipantCount = _initialParticipantCount,
            DeadParticipantCount = _deadParticipantCount,
            HasExternalAuthoritySeizure = _hasExternalAuthoritySeizure,
            LastProcessedYear = _lastProcessedYear
        };
    }

    internal static void ImportState(XjWorldArchiveQuanBingStruggleState state)
    {
        ClearPendingAnnualWork();
        XjWorldArchiveQuanBingStruggleState source = state ?? new XjWorldArchiveQuanBingStruggleState();
        _phase = NormalizePhase(source.Phase, source.Enabled);
        _startYear = Math.Max(0, source.StartYear);
        _endYearExclusive = Math.Max(0, source.EndYearExclusive);
        _phaseStartedYear = Math.Max(0, source.PhaseStartedYear > 0 ? source.PhaseStartedYear : source.StartYear);
        _phaseEndYearExclusive = Math.Max(0, source.PhaseEndYearExclusive > 0 ? source.PhaseEndYearExclusive : source.EndYearExclusive);
        _cooldownEndYear = Math.Max(0, source.CooldownEndYear);
        _tension = Math.Max(0, source.Tension);
        _proxyConflictCount = Math.Max(0, source.ProxyConflictCount);
        _initiatorActorId = Math.Max(0L, source.InitiatorActorId);
        _initiatorName = source.InitiatorName ?? string.Empty;
        _initialParticipantCount = Math.Max(0, source.InitialParticipantCount);
        _deadParticipantCount = Math.Max(0, source.DeadParticipantCount);
        _hasExternalAuthoritySeizure = source.HasExternalAuthoritySeizure;
        _lastProcessedYear = source.LastProcessedYear;
        if (string.Equals(_phase, PhaseFinal, StringComparison.Ordinal))
        {
            int baseYear = _startYear > 0 ? _startYear : (_phaseStartedYear > 0 ? _phaseStartedYear : Math.Max(0, _lastProcessedYear));
            _startYear = baseYear;
            _phaseStartedYear = baseYear;
            int requiredEnd = SafeAdd(baseYear, FinalDurationYears);
            if (_phaseEndYearExclusive < requiredEnd) _phaseEndYearExclusive = requiredEnd;
            if (_endYearExclusive < requiredEnd) _endYearExclusive = requiredEnd;
        }
        ParticipantActorIds.Clear();
        ClearTargetCache();
    }

    internal static void Clear()
    {
        ClearPendingAnnualWork();
        ParticipantActorIds.Clear();
        ClearTargetCache();
        _phase = PhaseNone;
        _startYear = 0;
        _endYearExclusive = 0;
        _phaseStartedYear = 0;
        _phaseEndYearExclusive = 0;
        _cooldownEndYear = 0;
        _tension = 0;
        _proxyConflictCount = 0;
        _initiatorActorId = 0L;
        _initiatorName = string.Empty;
        _initialParticipantCount = 0;
        _deadParticipantCount = 0;
        _hasExternalAuthoritySeizure = false;
        _lastProcessedYear = -1;
    }

    private static void BeginOmen(Actor trigger, int currentYear)
    {
        if (trigger?.data == null || ParticipantActorIds.Count < MinimumParticipants) return;
        _initiatorActorId = ((BaseSystemData)trigger.data).id;
        _initiatorName = trigger.getName() ?? string.Empty;
        BeginFinal(currentYear);
    }

    private static void BeginFinal(int currentYear)
    {
        if (ParticipantActorIds.Count < MinimumParticipants)
        {
            EnterCooldown(currentYear, "道争未成终局，诸真君各自退去。", false);
            return;
        }
        _phase = PhaseFinal;
        _startYear = currentYear;
        _phaseStartedYear = currentYear;
        _phaseEndYearExclusive = SafeAdd(currentYear, FinalDurationYears);
        _endYearExclusive = _phaseEndYearExclusive;
        _initialParticipantCount = ParticipantActorIds.Count;
        _deadParticipantCount = 0;
        ResetKillRewards();
        ReleaseActiveCombatantsFromOrdinaryRetreat();
        XjBroadcastSystem.BroadcastSLevelWorldEvent(
            (string.IsNullOrWhiteSpace(_initiatorName) ? "诸位真君" : ResolveAnnouncementNameByText(_initiatorName))
            + "为求更进一步的天地功绩，主动掀起五十年真君权柄之争。诸道真君循各自道途入局，以生死争夺权柄与功绩。",
            color: "#C95B3D");
        Touch();
    }

    private static void EnterCooldown(int currentYear, string announcement, bool finalEnded)
    {
        ResetKillRewards();
        for (int i = 0; i < ParticipantActorIds.Count; i++)
        {
            if (XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) && actor?.data != null) ClearCombatIntent(actor);
        }
        _phase = PhaseCooldown;
        _phaseStartedYear = currentYear;
        _phaseEndYearExclusive = 0;
        _cooldownEndYear = SafeAdd(currentYear, CooldownYears);
        _endYearExclusive = _cooldownEndYear;
        ParticipantActorIds.Clear();
        ClearTargetCache();
        if (!string.IsNullOrWhiteSpace(announcement)) XjBroadcastSystem.BroadcastBLevelWorldEvent(announcement);
        if (finalEnded) _tension = 0;
        Touch();
    }

    private static void ResetToNone()
    {
        _phase = PhaseNone;
        _startYear = 0;
        _endYearExclusive = 0;
        _phaseStartedYear = 0;
        _phaseEndYearExclusive = 0;
        _cooldownEndYear = 0;
        _tension = 0;
        _proxyConflictCount = 0;
        _initiatorActorId = 0L;
        _initiatorName = string.Empty;
        _initialParticipantCount = 0;
        _deadParticipantCount = 0;
        _hasExternalAuthoritySeizure = false;
        ResetKillRewards();
        ParticipantActorIds.Clear();
        ClearTargetCache();
        Touch();
    }

    private static bool ShouldEndFinal(int currentYear)
    {
        return IsFinalConflict && _phaseEndYearExclusive > 0 && currentYear >= _phaseEndYearExclusive;
    }

    private static void RefreshParticipants()
    {
        ParticipantActorIds.Clear();
        ClearTargetCache();
        IReadOnlyList<long> ids = XjCultivatorCache.GetZhenJunOrHigherIds();
        for (int i = 0; i < ids.Count; i++)
        {
            if (XjScheduler.ResolveActor(ids[i], out Actor actor) && IsParticipant(actor, out long actorId)) ParticipantActorIds.Add(actorId);
        }
    }

    private static void UpdateEffectiveDeathCount()
    {
        if (!IsFinalConflict || _initialParticipantCount <= 0) return;
        int inferred = Math.Max(0, _initialParticipantCount - ParticipantActorIds.Count);
        if (inferred > _deadParticipantCount) _deadParticipantCount = inferred;
    }

    private static void RefreshPeakObservation(bool resetOnly, out Actor trigger)
    {
        trigger = null;
        for (int i = 0; i < ParticipantActorIds.Count; i++)
        {
            if (!XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) || actor?.data == null) continue;
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
            bool isPeak = yiXiang >= PeakYiXiang;
            XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, out int observed);
            if (!isPeak)
            {
                if (observed != 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, 0);
                continue;
            }
            if (!resetOnly && observed == 0 && trigger == null)
            {
                XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, 1);
                trigger = actor;
            }
        }
    }

    private static void ReleaseActiveCombatantsFromOrdinaryRetreat()
    {
        if (!IsFinalConflict) return;
        for (int i = 0; i < ParticipantActorIds.Count; i++)
        {
            if (!XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) || actor?.data == null) continue;
            if (IsWithdrawnParticipant(actor)) XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
            else
            {
                XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
                ClearCombatIntent(actor);
            }
        }
    }

    internal static bool IsWithdrawnParticipant(Actor actor)
    {
        if (actor?.data == null) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        return XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)
            && (state.IntegrationRetreatActive || !string.IsNullOrWhiteSpace(state.WithdrawnToDongTian));
    }

    private static Actor ResolveCombatTarget(Actor actor, long actorId)
    {
        int frame = Time.frameCount;
        if (TargetActorIdByParticipant.TryGetValue(actorId, out long targetId)
            && TargetRefreshFrameByParticipant.TryGetValue(actorId, out int nextRefreshFrame)
            && frame < nextRefreshFrame)
        {
            if (targetId <= 0L) return null;
            if (XjScheduler.ResolveActor(targetId, out Actor cached)
                && cached?.data != null
                && IsParticipant(cached, out _)
                && !IsWithdrawnParticipant(cached)
                && !XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(actor, cached))
            {
                return cached;
            }
        }

        Actor resolved = FindNearestTarget(actor);
        if (resolved?.data != null)
        {
            TargetActorIdByParticipant[actorId] = ((BaseSystemData)resolved.data).id;
            TargetRefreshFrameByParticipant[actorId] = frame + TargetRefreshIntervalFrames;
        }
        else
        {
            // 终局仅剩一名或其余人暂时撤入洞天时，同样缓存“无目标”，
            // 避免最后一人每个战斗帧都重新遍历参与者。
            TargetActorIdByParticipant[actorId] = 0L;
            TargetRefreshFrameByParticipant[actorId] = frame + TargetRefreshIntervalFrames;
        }
        return resolved;
    }

    private static Actor FindNearestTarget(Actor actor)
    {
        WorldTile actorTile;
        try { actorTile = ((BaseSimObject)actor).current_tile; } catch (System.Exception xjCaught530_2) {
            XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjQuanBingStruggleSystem.cs:530", xjCaught530_2);
             actorTile = null; }
        if (actorTile == null) return null;
        long actorId = ((BaseSystemData)actor.data).id;
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string ownDaoTu);
        bool prioritizeOwnZheng = false;
        if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState ownState))
        {
            string position = ownState.GuoWei ?? string.Empty;
            prioritizeOwnZheng = position.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
                || position.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
        }

        Actor bestOwnZheng = null;
        Actor bestDifferent = null;
        int bestOwnZhengDistance = int.MaxValue;
        int bestDifferentDistance = int.MaxValue;
        for (int i = 0; i < ParticipantActorIds.Count; i++)
        {
            long candidateId = ParticipantActorIds[i];
            if (candidateId == actorId || !XjScheduler.ResolveActor(candidateId, out Actor candidate)
                || !IsParticipant(candidate, out _) || IsWithdrawnParticipant(candidate)
                || XjTaiYinNonAggressionPolicy.ShouldBlockIncomingAttack(actor, candidate)) continue;
            WorldTile targetTile;
            try { targetTile = ((BaseSimObject)candidate).current_tile; } catch (System.Exception xjCaught544_3) {
                XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjQuanBingStruggleSystem.cs:544", xjCaught544_3);
                 targetTile = null; }
            if (targetTile == null) continue;
            int distance = Toolbox.SquaredDistVec2(actorTile.pos, targetTile.pos);
            XjActorAccessor.TryGetString(candidate, XjActorDataKeys.DaoTu, out string targetDaoTu);
            bool sameDao = !string.IsNullOrWhiteSpace(ownDaoTu)
                && string.Equals(ownDaoTu, targetDaoTu, StringComparison.Ordinal);
            if (sameDao && prioritizeOwnZheng
                && XjGuoWeiQuanBingRegistry.TryGet(candidateId, out XjGuoWeiQuanBingState targetState)
                && (targetState.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
            {
                if (distance < bestOwnZhengDistance) { bestOwnZhengDistance = distance; bestOwnZheng = candidate; }
                continue;
            }
            if (!sameDao && distance < bestDifferentDistance)
            {
                bestDifferentDistance = distance;
                bestDifferent = candidate;
            }
        }
        return bestOwnZheng ?? bestDifferent;
    }

    private static bool IsParticipant(Actor actor, out long actorId)
    {
        actorId = 0L;
        if (!XjSafeCore.IsAliveActor(actor) || !XjJinDanAccessor.BuildPositionCarrierState(actor).Found) return false;
        actorId = ((BaseSystemData)actor.data).id;
        return actorId > 0L;
    }


    private static void ClearTargetCache()
    {
        TargetActorIdByParticipant.Clear();
        TargetRefreshFrameByParticipant.Clear();
    }

    private static void ResetKillRewards()
    {
        for (int i = 0; i < ParticipantActorIds.Count; i++)
        {
            if (XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) && actor?.data != null)
            {
                XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarKillCount, 0);
                XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarMeritBonusUsed, 0);
            }
        }
    }

    private static void ClearCombatIntent(Actor actor)
    {
        if (actor?.data == null) return;
        try { actor.attackedBy = null; actor.cancelAllBeh(); actor.stopMovement(); } catch (System.Exception xjCaught581) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjQuanBingStruggleSystem.cs:581", xjCaught581); }
    }


    private static string ResolveAnnouncementNameByText(string name)
    {
        string displayName = name?.Trim() ?? string.Empty;
        int dashIndex = displayName.IndexOf('-');
        if (dashIndex > 0) displayName = displayName.Substring(0, dashIndex).Trim();
        return string.IsNullOrWhiteSpace(displayName) ? "无名真君" : displayName;
    }

    private static string NormalizePhase(string phase, bool legacyEnabled)
    {
        if (string.Equals(phase, PhaseOmen, StringComparison.Ordinal)
            || string.Equals(phase, PhaseProxy, StringComparison.Ordinal)
            || string.Equals(phase, PhaseFinal, StringComparison.Ordinal)) return PhaseFinal;
        if (string.Equals(phase, PhaseCooldown, StringComparison.Ordinal)) return PhaseCooldown;
        return legacyEnabled ? PhaseFinal : PhaseNone;
    }

    private static int SafeAdd(int year, int duration)
    {
        long value = (long)Math.Max(0, year) + Math.Max(0, duration);
        return value > int.MaxValue ? int.MaxValue : (int)value;
    }

    private static void Touch()
    {
        XjWorldArchiveSystem.MarkChanged();
        XjWorldArchiveSystem.RequestProtectedCommit();
    }
}
