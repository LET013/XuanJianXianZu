using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 阴阳高位异象只在既有高境事件节点读取当前果位状态，不建立年度扫描。
/// 当前只落两条轻量事实：
/// 1) 阴阳一显一隐时，执孛真君可一次性重现“执渡旧法”的二仪相渡之意；
/// 2) 阴阳俱不显时，真炁金丹初成可一次性引出“一缕至真”异象。
/// 太阳为第一显，常态绝不可被太阴主动藏果；只有专门世界事件显式开启短期许可时例外。
/// </summary>
internal sealed class XjYinYangRarePhenomenaArchiveData
{
    public int SchemaVersion { get; set; } = 1;
    public bool ZhiDuEchoAwakened { get; set; }
    public long ZhiDuEchoActorId { get; set; }
    public string ZhiDuEchoActorName { get; set; } = string.Empty;
    public int ZhiDuEchoYear { get; set; }
    public bool FirstTrueBorn { get; set; }
    public long FirstTrueBearerActorId { get; set; }
    public string FirstTrueBearerName { get; set; } = string.Empty;
    public int FirstTrueYear { get; set; }
    public int SolarVeilPermitUntilYear { get; set; }
    public string SolarVeilPermitReason { get; set; } = string.Empty;
}

internal static class XjYinYangRarePhenomenaSystem
{
    internal const string TaiYinDaoTu = "太阴";
    internal const string TaiYangDaoTu = "太阳";
    internal const string ZhiBoDaoTu = "执孛";
    internal const string ZhenQiDaoTu = "真炁";
    internal static readonly string TaiYangZhengWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
        TaiYangDaoTu, XjGuoWeiCalculator.ZhengWei, 1);

    private static XjYinYangRarePhenomenaArchiveData _state = new XjYinYangRarePhenomenaArchiveData();

    internal static bool HasZhiDuEcho => _state?.ZhiDuEchoAwakened ?? false;
    internal static bool HasFirstTrue => _state?.FirstTrueBorn ?? false;
    internal static long FirstTrueBearerActorId => _state?.FirstTrueBearerActorId ?? 0L;

    internal static XjYinYangRarePhenomenaArchiveData ExportState()
    {
        XjYinYangRarePhenomenaArchiveData source = _state ?? new XjYinYangRarePhenomenaArchiveData();
        return new XjYinYangRarePhenomenaArchiveData
        {
            SchemaVersion = 1,
            ZhiDuEchoAwakened = source.ZhiDuEchoAwakened,
            ZhiDuEchoActorId = Math.Max(0L, source.ZhiDuEchoActorId),
            ZhiDuEchoActorName = source.ZhiDuEchoActorName ?? string.Empty,
            ZhiDuEchoYear = Math.Max(0, source.ZhiDuEchoYear),
            FirstTrueBorn = source.FirstTrueBorn,
            FirstTrueBearerActorId = Math.Max(0L, source.FirstTrueBearerActorId),
            FirstTrueBearerName = source.FirstTrueBearerName ?? string.Empty,
            FirstTrueYear = Math.Max(0, source.FirstTrueYear),
            SolarVeilPermitUntilYear = Math.Max(0, source.SolarVeilPermitUntilYear),
            SolarVeilPermitReason = source.SolarVeilPermitReason ?? string.Empty
        };
    }

    internal static void ImportState(XjYinYangRarePhenomenaArchiveData source)
    {
        _state = source == null ? new XjYinYangRarePhenomenaArchiveData() : new XjYinYangRarePhenomenaArchiveData
        {
            SchemaVersion = 1,
            ZhiDuEchoAwakened = source.ZhiDuEchoAwakened,
            ZhiDuEchoActorId = Math.Max(0L, source.ZhiDuEchoActorId),
            ZhiDuEchoActorName = source.ZhiDuEchoActorName ?? string.Empty,
            ZhiDuEchoYear = Math.Max(0, source.ZhiDuEchoYear),
            FirstTrueBorn = source.FirstTrueBorn,
            FirstTrueBearerActorId = Math.Max(0L, source.FirstTrueBearerActorId),
            FirstTrueBearerName = source.FirstTrueBearerName ?? string.Empty,
            FirstTrueYear = Math.Max(0, source.FirstTrueYear),
            SolarVeilPermitUntilYear = Math.Max(0, source.SolarVeilPermitUntilYear),
            SolarVeilPermitReason = source.SolarVeilPermitReason ?? string.Empty
        };
    }

    internal static void Clear()
    {
        _state = new XjYinYangRarePhenomenaArchiveData();
    }

    /// <summary>
    /// 太阳为第一显。任何普通太阴藏果调用均不得将太阳正果列入目标。
    /// 未来若有明确高位事件需要“太阳暂隐”，事件本身必须显式开启许可。
    /// </summary>
    internal static bool CanTaiYinVeilPosition(string positionId, int currentYear, out string reason)
    {
        reason = string.Empty;
        string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
        if (!string.Equals(normalized, TaiYangZhengWei, StringComparison.Ordinal)) return true;
        if (IsSolarVeilEventPermitted(currentYear)) return true;
        reason = "太阳为第一显，非有高位变故不可藏入月翳";
        return false;
    }

    /// <summary>
    /// 只供专门世界事件调用；普通AI、太阴得果与读档维护不得自行开启。
    /// 不创建轮询，到期后查询自然失效。
    /// </summary>
    internal static void GrantSolarVeilEventPermit(int currentYear, int durationYears, string reason)
    {
        currentYear = Math.Max(1, currentYear);
        int untilYear = currentYear + Math.Max(0, durationYears);
        if (_state == null) _state = new XjYinYangRarePhenomenaArchiveData();
        if (untilYear <= _state.SolarVeilPermitUntilYear) return;
        _state.SolarVeilPermitUntilYear = untilYear;
        _state.SolarVeilPermitReason = reason ?? string.Empty;
        Touch();
    }

    internal static bool IsSolarVeilEventPermitted(int currentYear)
    {
        return _state != null
            && _state.SolarVeilPermitUntilYear > 0
            && Math.Max(1, currentYear) <= _state.SolarVeilPermitUntilYear;
    }

    internal static bool IsYinYangImbalanced(int currentYear)
    {
        if (HasArtificialSourceSeal(currentYear)) return false;
        bool yin = IsPubliclyManifestedZhengWei(TaiYinDaoTu, currentYear);
        bool yang = IsPubliclyManifestedZhengWei(TaiYangDaoTu, currentYear);
        return yin != yang;
    }

    internal static bool IsPrimordialOpening(int currentYear)
    {
        if (HasArtificialSourceSeal(currentYear)) return false;
        return !IsPubliclyManifestedZhengWei(TaiYinDaoTu, currentYear)
            && !IsPubliclyManifestedZhengWei(TaiYangDaoTu, currentYear);
    }

    /// <summary>
    /// 接在既有金丹成功链上。一次成功最多做几个O(1)果位查询；
    /// 一缕真仅真炁求金时检查，执渡遗绪仅执孛求金时检查。
    /// </summary>
    internal static void OnJinDanSucceeded(Actor actor, string daoTu, int currentYear)
    {
        if (actor?.data == null) return;
        currentYear = Math.Max(1, currentYear);
        string normalizedDaoTu = XjDaoTuRelationCatalog.Normalize(daoTu);
        if (string.Equals(normalizedDaoTu, ZhiBoDaoTu, StringComparison.Ordinal)
            && !HasZhiDuEcho
            && IsYinYangImbalanced(currentYear))
        {
            AwakenZhiDuEcho(actor, currentYear);
        }
        if (string.Equals(normalizedDaoTu, ZhenQiDaoTu, StringComparison.Ordinal)
            && !HasFirstTrue
            && IsPrimordialOpening(currentYear))
        {
            BirthFirstTrue(actor, currentYear);
        }
    }

    private static bool HasArtificialSourceSeal(int currentYear)
    {
        // 开局渊照所系的太阴正果属于预设时代封锁，不能被误判成自然的“阴不显”。
        return XjYuanZhaoFruitSealPolicy.IsSealed(TaiYinDaoTu, XjGuoWeiCalculator.ZhengWei, Math.Max(1, currentYear));
    }

    private static bool IsPubliclyManifestedZhengWei(string daoTu, int currentYear)
    {
        string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
        if (normalizedDaoTu.Length == 0) return false;
        string positionId = XjGuoWeiCalculator.BuildGuoWeiSlotName(
            normalizedDaoTu, XjGuoWeiCalculator.ZhengWei, 1);
        if (XjYuanZhaoFruitSealPolicy.IsSealed(normalizedDaoTu, XjGuoWeiCalculator.ZhengWei, Math.Max(1, currentYear)))
            return false;
        if (XjTaiYinHiddenFruitSystem.IsPositionVeiled(positionId)) return false;
        if (XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(normalizedDaoTu)) return true;
        if (XjFruitPositionWorldState.TryGetDaoTaiSecondaryHolder(positionId, out long actorId, out _)
            && actorId > 0L
            && XjScheduler.ResolveActor(actorId, out Actor holder)
            && holder?.data != null
            && XjSafeCore.IsAliveActor(holder))
        {
            return true;
        }
        return false;
    }

    private static void AwakenZhiDuEcho(Actor actor, int year)
    {
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        _state.ZhiDuEchoAwakened = true;
        _state.ZhiDuEchoActorId = actorId;
        _state.ZhiDuEchoActorName = actor.getName() ?? string.Empty;
        _state.ZhiDuEchoYear = year;
        Touch();
        string text = "阴阳一偏之际，" + SafeName(actor.getName(), "一位执孛真君")
            + "于求金之后照见二仪相渡之理。旧日执渡开道之意由此再明：执一端而渡其反，使悖厉之势得有回转。";
        RecordRareEvent("二仪相渡", text, year, actorId, actor.getName(), "ZhiDuEcho");
    }

    private static void BirthFirstTrue(Actor actor, int year)
    {
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        _state.FirstTrueBorn = true;
        _state.FirstTrueBearerActorId = actorId;
        _state.FirstTrueBearerName = actor.getName() ?? string.Empty;
        _state.FirstTrueYear = year;
        Touch();
        string text = "二仪俱隐，真炁又有金丹成象。诸炁在一瞬间仿佛退回未分之初，"
            + SafeName(actor.getName(), "此真君") + "身侧遂现一线至真；其来处难名，只称一缕真。";
        RecordRareEvent("一缕至真", text, year, actorId, actor.getName(), "FirstTrue");
    }

    private static string SafeName(string name, string fallback)
    {
        return string.IsNullOrWhiteSpace(name) ? fallback : name.Trim();
    }

    private static void RecordRareEvent(string title, string text, int year, long actorId, string actorName, string eventType)
    {
        XjBroadcastSystem.ShowRecordedWorldTipCritical(text, color: "#C9C1FF");
        XjWorldHistoryStore.RecordDomainEvent(
            XjWorldHistoryCategory.HighRealm,
            title,
            text,
            5,
            true,
            actorId: actorId,
            actorName: actorName ?? string.Empty,
            year: Math.Max(1, year),
            eventType: eventType);
    }

    private static void Touch()
    {
        XjWorldArchiveSystem.MarkModuleChanged("world.fruit-position-domain");
        XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
            XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World
            | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
            | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
    }
}
