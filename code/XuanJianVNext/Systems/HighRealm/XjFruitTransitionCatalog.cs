using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal enum XjFruitTransitionKind
{
    None = 0,
    Shan = 1,
    Yi = 2,
    Bian = 3
}

internal readonly struct XjFruitTransitionRule
{
    internal readonly string SourceDaoTu;
    internal readonly string TargetDaoTu;
    internal readonly XjFruitTransitionKind Kind;
    internal readonly string RequiredAuthorityDaoTu;
    internal readonly string RequiredAuthority;
    internal readonly string RequiredShenTong;
    internal readonly int MinimumImage;
    internal readonly string ResultName;
    internal readonly string ProofMethod;

    internal XjFruitTransitionRule(
        string sourceDaoTu,
        string targetDaoTu,
        XjFruitTransitionKind kind,
        string requiredAuthorityDaoTu,
        string requiredAuthority,
        string requiredShenTong,
        int minimumImage,
        string resultName,
        string proofMethod)
    {
        SourceDaoTu = sourceDaoTu ?? string.Empty;
        TargetDaoTu = targetDaoTu ?? string.Empty;
        Kind = kind;
        RequiredAuthorityDaoTu = requiredAuthorityDaoTu ?? string.Empty;
        RequiredAuthority = requiredAuthority ?? string.Empty;
        RequiredShenTong = requiredShenTong ?? string.Empty;
        MinimumImage = Math.Max(0, minimumImage);
        ResultName = resultName ?? string.Empty;
        ProofMethod = proofMethod ?? string.Empty;
    }
}

/// <summary>
/// 嬗、移、变均为果位秩序的罕见有向变化，禁止根据五德相邻、权柄相似或
/// 通用关系图自动反推。当前只登记原著事件骨架足够明确的一条“坎水果位嬗景”：
/// 坎水持有府水浩瀚意向，泾龙王已化浩瀚海，且果位意象至少三象成域。
/// 这不是把坎水改成府水，而是坎水果位内部解释发生嬗变。
/// </summary>
internal static class XjFruitTransitionCatalog
{
    private static readonly XjFruitTransitionRule[] Rules =
    {
        new XjFruitTransitionRule(
            "坎水",
            "坎水",
            XjFruitTransitionKind.Shan,
            "府水",
            "广浚之湖",
            "浩瀚海",
            3000,
            "浩瀚海相",
            "原著明确事件骨架：坎水夺府水浩瀚意向，泾龙王化浩瀚海")
    };

    private static readonly Dictionary<string, XjFruitTransitionRule> DirectedRules = BuildRules();

    internal static bool TryGet(string sourceDaoTu, string targetDaoTu, XjFruitTransitionKind kind, out XjFruitTransitionRule rule)
    {
        return DirectedRules.TryGetValue(BuildKey(sourceDaoTu, targetDaoTu, kind), out rule);
    }

    internal static bool CanTransition(string sourceDaoTu, string targetDaoTu, XjFruitTransitionKind kind)
    {
        return TryGet(sourceDaoTu, targetDaoTu, kind, out _);
    }

    internal static bool TryResolveAvailable(Actor actor, out XjFruitTransitionRule rule)
    {
        rule = default;
        if (actor?.data == null) return false;
        string manifest = ResolveManifestDaoTu(actor);
        if (manifest.Length == 0) return false;
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
        XjXianJiState shenTong = XjXianJiAccessor.BuildState(actor);
        if (!shenTong.Found) return false;
        long actorId = ((BaseSystemData)actor.data).id;
        if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out _)) return false;

        for (int i = 0; i < Rules.Length; i++)
        {
            XjFruitTransitionRule candidate = Rules[i];
            if (!string.Equals(candidate.SourceDaoTu, manifest, StringComparison.Ordinal)) continue;
            if (image < candidate.MinimumImage) continue;
            if (!HasShenTong(shenTong, candidate.RequiredShenTong)) continue;
            if (!XjHighRealmAggregateStore.ContainsAuthority(
                actorId,
                candidate.RequiredAuthority,
                includeLocal: false,
                includeSeized: true,
                includeForeign: true)) continue;
            if (!HasRequiredBinding(actor, candidate)) continue;
            // 外道权柄被夺后，来源道途账本本就会标记“失”；只要准确来源绑定仍在此角色账下即视为有效。
            rule = candidate;
            return true;
        }
        return false;
    }

    private static Dictionary<string, XjFruitTransitionRule> BuildRules()
    {
        Dictionary<string, XjFruitTransitionRule> result = new Dictionary<string, XjFruitTransitionRule>(StringComparer.Ordinal);
        for (int i = 0; i < Rules.Length; i++)
        {
            XjFruitTransitionRule rule = Rules[i];
            result[BuildKey(rule.SourceDaoTu, rule.TargetDaoTu, rule.Kind)] = rule;
        }
        return result;
    }

    private static bool HasRequiredBinding(Actor actor, in XjFruitTransitionRule rule)
    {
        return XjShenTongMutationService.HasBinding(
            actor,
            rule.RequiredAuthorityDaoTu,
            rule.RequiredAuthority,
            rule.RequiredShenTong);
    }

    private static bool HasShenTong(in XjXianJiState state, string id)
    {
        string expected = XjXianJiCatalog.NormalizeXianJiId(id);
        for (int i = 0; i < state.Ids.Length; i++)
            if (string.Equals(XjXianJiCatalog.NormalizeXianJiId(state.Ids[i]), expected, StringComparison.Ordinal)) return true;
        return false;
    }

    private static string ResolveManifestDaoTu(Actor actor)
    {
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
        if (string.IsNullOrWhiteSpace(manifest)) XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out manifest);
        if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(manifest, out string display)) manifest = display;
        return (manifest ?? string.Empty).Trim();
    }

    private static string BuildKey(string sourceDaoTu, string targetDaoTu, XjFruitTransitionKind kind)
    {
        return (sourceDaoTu ?? string.Empty).Trim()
            + ">" + (targetDaoTu ?? string.Empty).Trim()
            + ">" + ((int)kind).ToString();
    }
}
