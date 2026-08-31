using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Events;

internal sealed class XjHongXiaLuoXiaArchiveData
{
    public int BaseWorldYear;
    public int ScheduledTriggerYear;
    public bool Triggered;
    public int TriggeredYear;
    public bool LuoXiaShanManifested;
    public bool MingYangIntent;
    public int TotalDisciples;
    // 陆江仙“问道落霞”的成功收徒次数。失败、无效角色和重复使用均不消耗名额。
    public int ManualInquiryDisciples;
    public long MingYangTargetActorId;
    public string MingYangTargetName = string.Empty;
    public string MingYangTargetGuoWei = string.Empty;
    public int MingYangPressure;
    public int MingYangResistance;
    public int MingYangContestCycles;
    public long MingYangTargetRootActorId;
    public int LastMingYangPressureYear;
    public int LastMingYangResistanceYear;
    // 兼容曾经“三轮自动夺柄”版本的存档；新版本不再写入此字段。
    public int MingYangAuthoritySeizedYear;
}

/// <summary>
/// 落霞山／虹霞空证世界事实。薛畋不生成地图 Actor，也不注册为城市宗门；
/// 只以背景高位存在占据虹霞正果与戊土第一闰位。三百年后，只在五岁资质落定时从 XjZz4~6 中
/// 依据先天命数、道慧与资质综合择徒；既有门人直系后辈另有续脉优先，且新收门人以虹霞为主、戊土旧法为辅。首次定路只读取已落定的师承偏好。落霞山地图实体使用永久山门资源，
/// 图片可后补但资源名固定；本类保存“已经显世”的稳定事实，不创建假城市或假宗门。
/// </summary>
internal static class XjHongXiaLuoXiaEvent
{
    internal const string ModuleId = "world.hongxia-luoxia";
    internal const int DelayYears = 300;
    internal const string FounderName = "薛畋";
    internal const string FactionName = "落霞山";
    internal const string DaoTu = "虹霞";
    internal const string SourceWuTu = "戊土";
    internal const string HongXiaJinXing = "虹霞独照性";
    internal const string WuTuRunJinXing = "戊土霞山性";
    internal const string FactionTraitId = "XjLuoXiaShan";
    private const string MingYangDaoTu = "明阳";
    private const int MingYangPressureIntervalYears = 30;
    private const int MingYangPressureCap = 6;
    private const int MingYangResistanceCap = 6;

    private static XjHongXiaLuoXiaArchiveData _state = new XjHongXiaLuoXiaArchiveData();

    internal static bool IsTriggered => _state?.Triggered ?? false;
    internal static bool IsLuoXiaShanManifested => _state?.LuoXiaShanManifested ?? false;
    internal static bool HasMingYangIntent => _state?.MingYangIntent ?? false;
    internal static int BaseWorldYear => Math.Max(0, _state?.BaseWorldYear ?? 0);
    internal static int ScheduledTriggerYear => Math.Max(0, _state?.ScheduledTriggerYear ?? 0);
    internal static int TriggeredYear => Math.Max(0, _state?.TriggeredYear ?? 0);
    internal static int TotalDisciples => Math.Max(0, _state?.TotalDisciples ?? 0);

    internal static string HongXiaZhengWeiId => XjGuoWeiCalculator.BuildGuoWeiSlotName(DaoTu, XjGuoWeiCalculator.ZhengWei, 1);
    internal static string WuTuReservedRunWeiId => XjGuoWeiCalculator.BuildGuoWeiSlotName(SourceWuTu, XjGuoWeiCalculator.RunWei, 1);

    internal static void TickYear(int currentYear)
    {
        if (currentYear <= 0) return;
        EnsureTimelineInitialized(currentYear);
        MigrateConfiguredDelayIfNeeded();
        if (currentYear < Math.Max(1, _state.ScheduledTriggerYear)) return;

        if (_state.Triggered)
        {
            EnsurePostTriggerState(currentYear);
            TickMingYangContest(currentYear);
            return;
        }

        _state.Triggered = true;
        _state.TriggeredYear = currentYear;
        _state.LuoXiaShanManifested = true;
        _state.MingYangIntent = true;
        EnsurePostTriggerState(currentYear);
        TickMingYangContest(currentYear);
        MarkChanged();

        string history = "玄鉴历" + XjChronology.ToXuanJianYear(currentYear) + "年，天地霞光先动，落霞山显于世间。薛畋昔于戊土得闰位真君，闭关千年，至今日借戊土空证虹霞，已为虹霞果位道胎；其持虹霞正果而仍执戊土一闰。虹霞一道独立于阴阳五德、十二炁与并古诸道，不克诸道，诸道亦不能以道统克制虹霞。落霞山虽显山门，却不立人间城国、不入寻常宗门籍册；此后只择少数上资质者收入门下，传虹霞或戊土。山中道意又遥指明阳，似有染指之心。";
        string tip = "【落霞为山·虹霞空证】" + FounderName + "昔于戊土得闰位真君，闭关千年而今空证虹霞；落霞山显世，其持虹霞正果、兼执戊土一闰，并有染指明阳之意。";
        XjBroadcastSystem.BroadcastSLevelWorldEvent(
            history,
            tip,
            "#E6B6D8",
            14f,
            XjEventIconCatalog.JinDanUpgrade,
            XjAnnouncementCategory.HighRealm);
    }

    internal static void ReconcileAfterLoad(int currentYear)
    {
        if (currentYear <= 0) return;
        EnsureTimelineInitialized(currentYear);
        MigrateConfiguredDelayIfNeeded();
        int triggerYear = Math.Max(1, _state.ScheduledTriggerYear);
        if (!_state.Triggered && currentYear >= triggerYear)
        {
            // 旧包或高倍速年度 backlog 可能让世界已经越过节点却尚未真正执行显世。
            // 读档时直接按当前真实世界年补触发，避免继续等待历史年度队列追上。
            TickYear(currentYear);
            return;
        }
        if (_state.Triggered)
        {
            int year = Math.Max(currentYear, _state.TriggeredYear);
            EnsurePostTriggerState(year);
            TickMingYangContest(year);
        }
    }

    private static void EnsureTimelineInitialized(int currentYear)
    {
        _state ??= new XjHongXiaLuoXiaArchiveData();
        if (_state.BaseWorldYear > 0 && _state.ScheduledTriggerYear > _state.BaseWorldYear) return;

        int baseYear = XjYuanZhaoKongZhengEvent.BaseWorldYear;
        if (baseYear <= 0)
        {
            XjCenturyAnnalsStore.TryEnsureBaseWorldYear(Math.Max(1, currentYear), out baseYear);
        }
        baseYear = Math.Max(1, baseYear <= 0 ? currentYear : baseYear);
        _state.BaseWorldYear = baseYear;
        _state.ScheduledTriggerYear = XjChronology.ToWorldYear(DelayYears, baseYear);
        MarkChanged();
    }

    private static void MigrateConfiguredDelayIfNeeded()
    {
        if (_state == null || _state.Triggered) return;
        int authoritativeBase = XjChronology.BaseWorldYear;
        if (authoritativeBase > 0 && _state.BaseWorldYear != authoritativeBase)
        {
            _state.BaseWorldYear = authoritativeBase;
        }
        if (_state.BaseWorldYear <= 0) return;
        int configuredTriggerYear = XjChronology.ToWorldYear(DelayYears, _state.BaseWorldYear);
        if (_state.ScheduledTriggerYear == configuredTriggerYear) return;
        // 只重排尚未发生的旧档时间轴；已经显世的落霞山保留既有历史与触发年。
        _state.ScheduledTriggerYear = configuredTriggerYear;
        MarkChanged();
    }

    private static void EnsurePostTriggerState(int currentYear)
    {
        int year = Math.Max(1, _state.TriggeredYear > 0 ? _state.TriggeredYear : currentYear);
        XjDaoTuManifestRegistry.MarkDiscovered(XjDaoTuRootIds.HongXia, 0L, year);
        XjDaoTuManifestRegistry.MarkCaiQiUnlocked(XjDaoTuRootIds.HongXia, 0L, year);
        // 落霞山使用洞天实体系统只承担“永久高位山门地标”，不进入公共奇遇、争夺与远征。
        // 若当前没有安全地图锚点，年度 O(1) 世界事件会在后续年份继续尝试，不扫描人口。
        XjDongTianRegistry.EnsureLuoXiaShanDongTian(Math.Max(1, currentYear), announce: false);
        XjFruitPositionWorldState.EnsureExternalPosition(
            FounderName,
            DaoTu,
            XjGuoWeiCalculator.ZhengWei,
            HongXiaZhengWeiId,
            HongXiaJinXing,
            string.Empty,
            year);
        int wuTuHeldSinceYear = Math.Max(1, year - 1000);
        XjFruitPositionWorldState.EnsureExternalPosition(
            FounderName,
            SourceWuTu,
            XjGuoWeiCalculator.RunWei,
            WuTuReservedRunWeiId,
            WuTuRunJinXing,
            DaoTu,
            wuTuHeldSinceYear,
            announceOpening: false);
        if (!_state.LuoXiaShanManifested || !_state.MingYangIntent)
        {
            _state.LuoXiaShanManifested = true;
            _state.MingYangIntent = true;
            MarkChanged();
        }
    }

    internal static bool IsExternalPositionOccupied(string guoWei)
    {
        if (string.IsNullOrWhiteSpace(guoWei)) return false;
        string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
        // 薛畋在本局起录前便已是戊土闰位真君，只是直到三百年事件才公开空证虹霞。
        // 因而戊土第一闰从开局即从玩家可用容量中扣除；虹霞正果则在空证显世后才成立。
        if (string.Equals(normalized, XjGuoWeiCalculator.NormalizeGuoWeiName(WuTuReservedRunWeiId), StringComparison.Ordinal))
            return true;
        return IsTriggered
            && string.Equals(normalized, XjGuoWeiCalculator.NormalizeGuoWeiName(HongXiaZhengWeiId), StringComparison.Ordinal);
    }

    internal static bool HasRuntimeState => (_state?.BaseWorldYear ?? 0) > 0
        || (_state?.ScheduledTriggerYear ?? 0) > 0
        || (_state?.Triggered ?? false)
        || (_state?.LuoXiaShanManifested ?? false)
        || (_state?.MingYangIntent ?? false)
        || (_state?.TotalDisciples ?? 0) > 0
        || (_state?.ManualInquiryDisciples ?? 0) > 0
        || (_state?.MingYangTargetActorId ?? 0L) > 0L
        || (_state?.MingYangPressure ?? 0) > 0
        || (_state?.MingYangResistance ?? 0) > 0
        || (_state?.MingYangContestCycles ?? 0) > 0
        || (_state?.MingYangAuthoritySeizedYear ?? 0) > 0;

    /// <summary>
    /// 落霞与明阳是一场跨转世的长期争夺：落霞会持续压迫明阳正果的同一转世链，
    /// 但明阳持位者可凭帝统、慧光与转世归来积累守势。双方拉锯只写入真实世界史，
    /// 不会由固定计时器直接把【尊卑法统】永久夺走。
    /// </summary>
    private static void TickMingYangContest(int currentYear)
    {
        if (!IsTriggered || currentYear <= 0) return;
        // 已被旧版本自动夺走的权柄不能在这里擅自回滚；新版本不再写入此字段。
        if (_state.MingYangAuthoritySeizedYear > 0) return;

        if (!TryFindActiveMingYangZhengWei(out XjGuoWeiRegistryEntry target)) return;
        long rootActorId = ResolveMingYangLineageRoot(target.ActorId);
        bool sameLineage = _state.MingYangTargetRootActorId > 0L
            && _state.MingYangTargetRootActorId == rootActorId;
        if (!sameLineage)
        {
            _state.MingYangTargetActorId = target.ActorId;
            _state.MingYangTargetRootActorId = rootActorId;
            _state.MingYangTargetName = target.ActorName ?? string.Empty;
            _state.MingYangTargetGuoWei = target.GuoWei ?? string.Empty;
            _state.MingYangPressure = 0;
            _state.MingYangResistance = 0;
            _state.LastMingYangPressureYear = currentYear;
            _state.LastMingYangResistanceYear = currentYear;
            MarkChanged();
            XjBroadcastSystem.BroadcastBLevelWorldEvent(
                "【落霞望明阳】落霞山遥感明阳正果在世，霞意初次落向"
                    + DisplayTargetName(target) + "所持的"
                    + XjGuoWeiCalculator.GetDisplayGuoWeiName(target.GuoWei) + "。",
                XjEventIconCatalog.JinDanUpgrade,
                XjAnnouncementCategory.HighRealm);
            return;
        }

        bool reincarnatedReturn = _state.MingYangTargetActorId != target.ActorId;
        if (reincarnatedReturn || !string.Equals(_state.MingYangTargetGuoWei, target.GuoWei, StringComparison.Ordinal))
        {
            _state.MingYangTargetActorId = target.ActorId;
            _state.MingYangTargetName = target.ActorName ?? string.Empty;
            _state.MingYangTargetGuoWei = target.GuoWei ?? string.Empty;
            _state.MingYangResistance = Math.Min(MingYangResistanceCap, _state.MingYangResistance + 1);
            MarkChanged();
            XjBroadcastSystem.BroadcastBLevelWorldEvent(
                "【明阳归位】" + DisplayTargetName(target) + "承接同一缕明阳旧性再临正果；"
                    + "落霞山此前所布之局未散，明阳一方亦因转世归来而更知守势。",
                XjEventIconCatalog.JinDanUpgrade,
                XjAnnouncementCategory.HighRealm);
        }

        if (currentYear - _state.LastMingYangPressureYear < MingYangPressureIntervalYears) return;
        _state.LastMingYangPressureYear = currentYear;
        int attack = 1 + XjDeterministicHash.PositiveIndex(
            rootActorId + currentYear * 17L, "luoxia_mingyang_pressure_v2", 2);
        int resistance = ResolveMingYangResistance(target, reincarnatedReturn);
        _state.MingYangPressure = Math.Min(MingYangPressureCap, _state.MingYangPressure + attack);
        _state.MingYangResistance = Math.Min(MingYangResistanceCap, _state.MingYangResistance + resistance);
        _state.LastMingYangResistanceYear = currentYear;

        if (_state.MingYangResistance >= _state.MingYangPressure)
        {
            _state.MingYangPressure = Math.Max(0, _state.MingYangPressure - 1);
            _state.MingYangResistance = Math.Max(0, _state.MingYangResistance - 1);
            MarkChanged();
            XjBroadcastSystem.BroadcastBLevelWorldEvent(
                "【明阳守正】落霞山再以虹霞之理遥照明阳，"
                    + DisplayTargetName(target) + "却以现世根基与旧性相应守住帝统。"
                    + "此轮霞意未能压过明阳。",
                XjEventIconCatalog.JinDanUpgrade,
                XjAnnouncementCategory.HighRealm);
            return;
        }

        if (_state.MingYangPressure < MingYangPressureCap)
        {
            MarkChanged();
            XjBroadcastSystem.BroadcastBLevelWorldEvent(
                "【霞照明阳】落霞山以虹霞遮向" + DisplayTargetName(target)
                    + "所执正果，欲借人世起落磨损其帝统。明阳尚未失位，仍可继续积蓄守势。",
                XjEventIconCatalog.JinDanUpgrade,
                XjAnnouncementCategory.HighRealm);
            return;
        }

        _state.MingYangContestCycles = Math.Max(0, _state.MingYangContestCycles) + 1;
        _state.MingYangPressure = 3;
        _state.MingYangResistance = Math.Min(MingYangResistanceCap, _state.MingYangResistance + 1);
        MarkChanged();
        XjBroadcastSystem.BroadcastSLevelWorldEvent(
            "玄鉴历" + XjChronology.ToXuanJianYear(currentYear) + "年，落霞山重压明阳，"
                + DisplayTargetName(target) + "所承旧性几近动摇，却仍未失去【尊卑法统】。"
                + "这一局只在同一转世链上留下更深牵引，后世仍有再起、再守与再争之机。",
            "【虹霞染指明阳】落霞山逼近帝统，明阳守住果位；争夺未决，转世未绝。",
            "#E6B6D8",
            14f,
            XjEventIconCatalog.JinDanUpgrade,
            XjAnnouncementCategory.HighRealm);
    }

    private static long ResolveMingYangLineageRoot(long actorId)
    {
        if (actorId <= 0L) return 0L;
        return XjReincarnation.TryResolveReincarnationRootActorId(actorId, out long rootActorId) && rootActorId > 0L
            ? rootActorId
            : actorId;
    }

    private static int ResolveMingYangResistance(in XjGuoWeiRegistryEntry target, bool reincarnatedReturn)
    {
        int value = reincarnatedReturn ? 1 : 0;
        if (!XjScheduler.ResolveActor(target.ActorId, out Actor actor) || actor?.data == null) return value;
        XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
        if (snapshot.HuiGuang >= 90f) value++;
        if (XjXianGuoSystem.IsDiMingYang(actor)) value += 2;
        return Math.Min(3, value);
    }

    private static bool TryFindActiveMingYangZhengWei(out XjGuoWeiRegistryEntry target)
    {
        target = default;
        IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            XjGuoWeiRegistryEntry entry = entries[i];
            if (!entry.Found || !entry.IsActive || entry.ActorId <= 0L
                || !string.Equals(XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(entry.GuoWei), MingYangDaoTu, StringComparison.Ordinal))
            {
                continue;
            }
            string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei);
            if (!normalizedGuoWei.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) continue;
            target = entry;
            return true;
        }
        return false;
    }

    private static string DisplayTargetName(in XjGuoWeiRegistryEntry target)
    {
        return string.IsNullOrWhiteSpace(target.ActorName) ? "在世明阳真君" : target.ActorName.Trim();
    }

    /// <summary>
    /// 不扫描世界，只在五岁资质落定时判一次落霞山收徒。
    /// XjZz4/5/6 都可成为候选，但最终机会由先天命数、道慧与资质共同派生；
    /// 薛姓承薛畋道胎余脉得到强传承加权。师承与未来道途偏好先落档，首次定路只消费这个结果。
    /// </summary>
    internal static bool TryAcceptAgeFiveDisciple(Actor actor, int aptitude, int currentYear)
    {
        if (!IsTriggered || actor?.data == null || aptitude < 4 || aptitude > 6
            || XjCultivationPathRules.IsShi(actor)) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;

        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LuoXiaShanDisciple, out int lineage)
            && lineage > 0)
        {
            return TryResolveDiscipleDaoTu(actor, out _);
        }

        // 已入落霞门墙者的直系后辈优先续脉。只读取父母两个ID，不扫家族/城市；
        // 四、五、六档分别约七成、九成、必入，并沿父母已有虹霞/戊土师承继续。
        if (TryResolveDirectLuoXiaParentDaoTu(actor, out string inheritedDaoTu))
        {
            int inheritedBasisPoints = aptitude >= 6 ? 10000 : aptitude == 5 ? 9000 : 7000;
            if (XjDeterministicHash.PositiveIndex(actorId + TriggeredYear * 47L,
                "luoxia_shan_direct_lineage_once_v1", 10000) < inheritedBasisPoints)
            {
                RecordDiscipleAccepted(actor, inheritedDaoTu, Math.Max(1, currentYear),
                    "幼时承家中落霞旧缘，又得山门复择，遂续入门墙，承【" + inheritedDaoTu + "】一脉。");
                return true;
            }
        }

        bool isXueSurname = IsXueSurname(actor);
        int basisPoints = XjTalentOpportunityRules.ResolveLuoXiaRecruitBasisPoints(actor, aptitude, isXueSurname);
        if (basisPoints <= 0) return false;
        if (XjDeterministicHash.PositiveIndex(actorId + TriggeredYear * 31L,
            "luoxia_shan_age_five_disciple_once_v3", 10000) >= basisPoints) return false;

        // 落霞山是虹霞正统山门，戊土只是薛畋旧法旁传；自然收徒以四比一偏向虹霞。
        string daoTu = XjDeterministicHash.PositiveIndex(actorId, "luoxia_shan_disciple_daotu_v3", 5) < 4
            ? DaoTu
            : SourceWuTu;
        RecordDiscipleAccepted(actor, daoTu, Math.Max(1, currentYear));
        return true;
    }

    private static bool IsXueSurname(Actor actor)
    {
        if (actor?.data == null) return false;
        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFamilySurname, out string surname)
            && string.Equals((surname ?? string.Empty).Trim(), "薛", StringComparison.Ordinal))
        {
            return true;
        }

        // 五岁资质结算可能早于家族姓氏镜像写入；此时只把角色名首字作为回退，
        // 不改姓、不建立假家族，也不会影响正式家谱的姓氏权威。
        string name = actor.getName() ?? string.Empty;
        return name.TrimStart().StartsWith("薛", StringComparison.Ordinal);
    }

    private static bool TryResolveDirectLuoXiaParentDaoTu(Actor actor, out string daoTu)
    {
        daoTu = string.Empty;
        if (actor?.data == null) return false;

        bool firstFound = TryResolveParentDiscipleDaoTu(actor.data.parent_id_1, out string first);
        bool secondFound = TryResolveParentDiscipleDaoTu(actor.data.parent_id_2, out string second);
        if (!firstFound && !secondFound) return false;
        if (firstFound && !secondFound) { daoTu = first; return true; }
        if (!firstFound && secondFound) { daoTu = second; return true; }
        if (string.Equals(first, second, StringComparison.Ordinal)) { daoTu = first; return true; }

        long actorId = ((BaseSystemData)actor.data).id;
        daoTu = XjDeterministicHash.PositiveIndex(actorId, "luoxia_parent_lineage_pick_v1", 2) == 0 ? first : second;
        return IsDiscipleDaoTu(daoTu);
    }

    private static bool TryResolveParentDiscipleDaoTu(long parentId, out string daoTu)
    {
        daoTu = string.Empty;
        if (parentId <= 0L
            || !XjActorRegistry.ResolveKnownOrWorld(parentId, out Actor parent)
            || parent?.data == null
            || !XjActorAccessor.TryGetInt(parent, XjActorDataKeys.LuoXiaShanDisciple, out int lineage)
            || lineage <= 0
            || !XjActorAccessor.TryGetString(parent, XjActorDataKeys.LuoXiaShanDiscipleDaoTu, out string stored)
            || !IsDiscipleDaoTu(stored))
        {
            return false;
        }
        daoTu = stored.Trim();
        return true;
    }

    internal static bool TryResolveDiscipleDaoTu(Actor actor, out string daoTu)
    {
        daoTu = string.Empty;
        if (actor?.data == null
            || !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LuoXiaShanDisciple, out int lineage)
            || lineage <= 0)
        {
            return false;
        }

        if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.LuoXiaShanDiscipleDaoTu, out string stored)
            && IsDiscipleDaoTu(stored))
        {
            daoTu = stored.Trim();
            return true;
        }

        // 兼容旧版已入门但没有偏好字段的存档；只补写一次稳定结果，不触碰已存在的修炼路径。
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return false;
        daoTu = XjDeterministicHash.PositiveIndex(actorId, "luoxia_shan_disciple_daotu_v3", 5) < 4
            ? DaoTu
            : SourceWuTu;
        XjActorAccessor.SetString(actor, XjActorDataKeys.LuoXiaShanDiscipleDaoTu, daoTu);
        return true;
    }

    /// <summary>
    /// 陆江仙模拟器的显式收徒入口。三次是全世界成功名额，而不是单个角色的冷却；
    /// 未能完成道途事务时不会写师承，也不会扣除名额。
    /// </summary>
    internal static bool TryAcceptManualInquiryDisciple(Actor actor, int currentYear, out string daoTu, out string reason)
    {
        daoTu = string.Empty;
        reason = string.Empty;
        if (!IsTriggered)
        {
            reason = "luoxia_not_manifested";
            return false;
        }
        if (!XjSafeCore.IsAliveActor(actor) || actor?.data == null)
        {
            reason = "actor_not_alive";
            return false;
        }
        if (XjCultivationPathRules.IsShi(actor))
        {
            reason = "shi_path_incompatible";
            return false;
        }
        if (XjXianGuoSystem.IsDiMingYang(actor)
            || XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _))
        {
            // 正果钟爱与帝统是跨转世的硬锁，不能用调试入口制造“记录是虹霞、实际仍是明阳”的假门人。
            reason = "immutable_daotu_lock";
            return false;
        }
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LuoXiaShanDisciple, out int lineage) && lineage > 0)
        {
            reason = "already_luoxia_disciple";
            return false;
        }

        _state ??= new XjHongXiaLuoXiaArchiveData();
        if (_state.ManualInquiryDisciples >= 3)
        {
            reason = "global_inquiry_limit_reached";
            return false;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L)
        {
            reason = "invalid_actor_id";
            return false;
        }
        daoTu = XjDeterministicHash.PositiveIndex(
            actorId + Math.Max(1, currentYear) * 131L + _state.ManualInquiryDisciples,
            "luoxia_shan_manual_inquiry_daotu_v2", 5) < 4
            ? DaoTu
            : SourceWuTu;

        bool switched;
        bool initializedPath = false;
        if (!XjCultivationPathRules.TryGetPath(actor, out _))
        {
            initializedPath = XjCultivationPathTransitions.TrySetInitialPath(
                actor, XjCultivationPathIds.ZiFuJinDan, daoTu, string.Empty, syncVisibleTraits: false);
            switched = initializedPath && XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, true);
            if (switched)
            {
                XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
            }
        }
        else
        {
            switched = XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, true);
        }
        if (!switched)
        {
            // 原本尚未入道者若在后续道途事务被拒绝，回到无修炼路径，避免“没入门却被半初始化”。
            if (initializedPath) XjCultivationPathTransitions.ClearAll(actor);
            daoTu = string.Empty;
            reason = "daotu_transition_rejected";
            return false;
        }

        _state.ManualInquiryDisciples++;
        RecordDiscipleAccepted(
            actor,
            daoTu,
            Math.Max(1, currentYear),
            "蒙落霞山薛畋问道收录，遂入落霞门下，改承【" + daoTu + "】。此缘只记师承，不改城市宗门归属。");
        MarkChanged();
        return true;
    }

    internal static void RecordDiscipleAccepted(Actor actor, string daoTu, int currentYear, string historyText = null)
    {
        if (actor?.data == null || !IsDiscipleDaoTu(daoTu)) return;

        bool alreadyRecorded = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.LuoXiaShanDisciple, out int lineage)
            && lineage > 0;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.LuoXiaShanDisciple, 1);
        XjActorAccessor.SetString(actor, XjActorDataKeys.LuoXiaShanDiscipleDaoTu, daoTu.Trim());
        try
        {
            if (!actor.hasTrait(FactionTraitId) && AssetManager.traits?.get(FactionTraitId) != null)
            {
                actor.addTrait(FactionTraitId, false);
            }
        }
        catch
        {
            // 师承权威字段已经写入；即使某次资源初始化尚未完成，也不能重复计数或改道。
        }

        if (alreadyRecorded) return;
        _state ??= new XjHongXiaLuoXiaArchiveData();
        _state.TotalDisciples = Math.Max(0, _state.TotalDisciples) + 1;
        MarkChanged();
        XjWorldHistoryStore.RecordActorEvent(
            actor,
            string.IsNullOrWhiteSpace(historyText)
                ? "五岁时得落霞山薛畋择入门下，日后首次定路将承【" + daoTu.Trim() + "】。落霞山不籍于人间宗门，此缘只记师承，不改城市宗门归属。"
                : historyText.Trim(),
            XjEventIconCatalog.HistoryWorld);
    }

    private static bool IsDiscipleDaoTu(string daoTu)
    {
        return string.Equals((daoTu ?? string.Empty).Trim(), DaoTu, StringComparison.Ordinal)
            || string.Equals((daoTu ?? string.Empty).Trim(), SourceWuTu, StringComparison.Ordinal);
    }

    internal static string ExportPayload()
    {
        return JsonConvert.SerializeObject(_state ?? new XjHongXiaLuoXiaArchiveData(), Formatting.None);
    }

    internal static void ImportPayload(int schemaVersion, string payload)
    {
        _ = schemaVersion;
        if (string.IsNullOrWhiteSpace(payload))
        {
            _state = new XjHongXiaLuoXiaArchiveData();
            return;
        }
        try
        {
            _state = JsonConvert.DeserializeObject<XjHongXiaLuoXiaArchiveData>(payload)
                ?? new XjHongXiaLuoXiaArchiveData();
        }
        catch
        {
            _state = new XjHongXiaLuoXiaArchiveData();
        }
    }

    internal static void ClearRuntime()
    {
        _state = new XjHongXiaLuoXiaArchiveData();
    }

    private static void MarkChanged()
    {
        XjWorldArchiveSystem.MarkModuleChanged(ModuleId);
    }
}
