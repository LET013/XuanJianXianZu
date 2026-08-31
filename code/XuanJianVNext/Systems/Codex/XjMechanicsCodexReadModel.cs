using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.Codex;

internal sealed class XjMechanicsGuideSnapshot
{
    internal int WorldYear;
    internal int DaoTuCount;
    internal int ActiveFruit;
    internal int ActiveResidual;
    internal int ActiveIntercalary;
    internal int ActiveAuthorityHolders;
    internal int ShiDharmaFormCount;
    internal int ShiWorldHonoredCount;
    internal string AuthorityWarStatus = string.Empty;
    internal IReadOnlyList<string> ZiFuRealmLines = Array.Empty<string>();
    internal IReadOnlyList<string> FuQiRealmLines = Array.Empty<string>();
}

/// <summary>
/// 玄鉴百科的只读模型。百科正文只展示稳定规则，不再混入当前天下实况。
/// 固定、可规划的需求（如真元需求）应明确展示；随机概率、抽签权重与隐藏随机阈值不直接公开。
/// 旧的实况字段暂保留在 DTO 上以减少 RC 阶段结构迁移风险，但 Read 不再填充或读取实时世界状态。
/// </summary>
internal static class XjMechanicsCodexReadModel
{
    private static XjMechanicsGuideSnapshot _cached;

    internal static XjMechanicsGuideSnapshot Read(int worldYear)
    {
        // 玄鉴百科已经从“规则说明 + 当前天下实况”彻底拆成纯规则页。
        // 这里故意不再读取果位权柄、释修人数或权柄之争状态，避免打开百科
        // 触发一套与阅读规则无关的实时快照构造；worldYear 仅保留签名兼容。
        if (_cached != null) return _cached;
        _cached = new XjMechanicsGuideSnapshot
        {
            WorldYear = 0,
            ZiFuRealmLines = BuildZiFuRealmLines(),
            FuQiRealmLines = BuildFuQiRealmLines()
        };
        return _cached;
    }

    internal static void Clear()
    {
        _cached = null;
    }

    internal static string FormatRealmRule(string realmId)
    {
        if (!XjRealmRules.TryGet(realmId, out XjRealmRule rule)) return string.Empty;
        List<string> requirements = new List<string>(4);
        requirements.Add("真元需求：" + FormatRequiredZhenYuan(rule.RequiredZhenYuan));
        if (rule.RequiresCaiQi) requirements.Add("需采气");
        if (rule.RequiresFiveXianJi) requirements.Add("需五门仙基/神通");
        if (rule.RequiresQiuJinFa) requirements.Add("需求金法");
        if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            || string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
        {
            requirements.Add("需一部六品主功法与四部五品神通功法");
        }
        return rule.DisplayName + "：" + string.Join(" · ", requirements);
    }

    internal static string FormatDaoTaiRule(string routeName)
    {
        string prefix = string.IsNullOrWhiteSpace(routeName) ? "道胎" : routeName.Trim() + "道胎";
        string realmId = !string.IsNullOrWhiteSpace(routeName)
            && routeName.IndexOf("服气", StringComparison.Ordinal) >= 0
            ? XjRealmIds.FuQiDaoTai
            : XjRealmIds.DaoTai;
        string zhenYuan = XjRealmRules.TryGet(realmId, out XjRealmRule rule)
            ? FormatRequiredZhenYuan(rule.RequiredZhenYuan)
            : "未载";
        return prefix + "：真元需求：" + zhenYuan
            + " · 需高境修至巅峰并积累足够天地功绩"
            + " · 当前持有真实果位/余位/闰位"
            + " · 道慧与果位格局满足尝试资格"
            + " · 所持本道权柄越完整，突破越有利"
            + " · 随机突破概率与隐藏权重不在指南中公开";
    }

    private static string FormatRequiredZhenYuan(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return "0";
        double rounded = Math.Round(Math.Max(0d, value));
        return rounded.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    internal static string CapacitySummary => XjFruitPositionWorldState.FormatCapacityRuleSummary();
    internal static IReadOnlyList<XjFruitPositionCapacityRule> CapacityRules => XjFruitPositionWorldState.ReadCapacityRules();
    internal static string AptitudeName(int rank) => XjDisplayNameSanitizer.AptitudeName(rank);
    internal static int RequiredDaoTaiMerit => XjDaoTaiMeritSystem.RequiredMerit;
    internal static int DaoTaiEnlightenmentIntervalYears => XjDaoTaiEnlightenmentSystem.MinimumIntervalYears;
    internal static int DaoTaiEnlightenmentAptitudeCeiling => XjDaoTaiEnlightenmentSystem.MaximumEnlightenedAptitude;
    internal static int AuthorityWarDurationYears => XjQuanBingStruggleSystem.FinalDurationYears;
    internal static int AuthorityIntegrationYears => XjGuoWeiQuanBingRules.ExternalAuthorityIntegrationYears;
    internal static int PeakYiXiang => XjQuanBingStruggleSystem.PeakYiXiang;
    internal static int FruitPositionDaoHui => (int)XjDaoHuiPolicy.FruitPositionThreshold;
    internal static int StableInheritanceDaoHui => (int)XjDaoHuiPolicy.StableInheritanceThreshold;
    internal static int ResidualDaoHui => (int)XjDaoHuiPolicy.DeriveResidualThreshold;
    internal static int IntercalaryDaoHui => (int)XjDaoHuiPolicy.OpenIntercalaryThreshold;
    internal static int RemoteIntercalaryDaoHui => (int)XjDaoHuiPolicy.StructuredRemoteThreshold;
    internal static int DifficultPositionDaoHui => (int)XjDaoHuiPolicy.DifficultPositionThreshold;
    internal static int CompleteEmptyProofDaoHui => (int)XjDaoHuiPolicy.CompleteEmptyProofThreshold;
    internal static int FruitSlotCount => XjGuoWeiQuanBingRules.ZhengWeiSlotCount;
    internal static int MaxResidualSlots => XjGuoWeiQuanBingRules.YuWeiSlotCount;
    internal static int MaxIntercalarySlots => XjGuoWeiQuanBingRules.RunWeiSlotCount;
    internal static int AuthorityCountPerDaoTu => XjGuoWeiQuanBingRules.QuanBingCountPerDaoTu;
    internal static int SpecialXianResidualProgress => XjSpecialXianPositionPromotionSystem.ResidualProgressThreshold;
    internal static int SpecialXianIntercalaryProgress => XjSpecialXianPositionPromotionSystem.IntercalaryProgressThreshold;

    private static IReadOnlyList<string> BuildZiFuRealmLines()
    {
        return new[]
        {
            FormatRealmRule(XjRealmIds.TaiXi),
            FormatRealmRule(XjRealmIds.LianQi),
            FormatRealmRule(XjRealmIds.ZhuJi),
            FormatRealmRule(XjRealmIds.ZiFu),
            FormatRealmRule(XjRealmIds.JinDan),
            FormatRealmRule(XjRealmIds.ShenDan),
            FormatDaoTaiRule("紫府金丹")
        };
    }

    private static IReadOnlyList<string> BuildFuQiRealmLines()
    {
        string rank4Name = AptitudeName(4);
        string rank5Name = AptitudeName(5);
        string rank6Name = AptitudeName(6);
        return new[]
        {
            "黄冠：" + rank5Name + "、" + rank6Name + "为主；少数高先天命数+高道慧的" + rank4Name + "可破格感气",
            "真人：服气本命功法合炼成形 · 真人与紫府同阶",
            "真君羽士：神妙圆满并化出金性 · 四档破格者的求证成功率显著低于五、六档",
            "神丹：具备真君层次资格 · 自身无果位，须托身于已有金丹或真君且锚点容量允许",
            FormatDaoTaiRule("服气养性"),
            "转修紫府金丹：五品真人不会固定分流。只有其真君前景明显偏低，而转入紫金后仍保留更显著的一线求金机会时，才可能舍服气转紫金；受挫后可重新权衡，转修成功后按紫府、五门仙基、六品功法与金丹规则推进"
        };
    }
}
