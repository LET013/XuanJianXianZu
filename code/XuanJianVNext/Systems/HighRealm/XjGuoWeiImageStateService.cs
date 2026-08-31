using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 将“果位意象”从单一进度值投影为可见真景：阶段决定意象展开程度，
/// 本地权柄组成本道真景，夺取/融入的外道权柄形成客象；权柄失落会造成残缺，
/// 客象过多会由杂映走向异化。只在年度、闭关、夺柄与位序变化时刷新，不常驻扫描。
/// </summary>
internal static class XjGuoWeiImageStateService
{
    private readonly struct ImageSnapshot
    {
        internal readonly int Image;
        internal readonly int Stage;
        internal readonly string StageName;
        internal readonly string Integrity;
        internal readonly string Summary;

        internal ImageSnapshot(int image, int stage, string stageName, string integrity, string summary)
        {
            Image = image;
            Stage = stage;
            StageName = stageName ?? string.Empty;
            Integrity = integrity ?? string.Empty;
            Summary = summary ?? string.Empty;
        }
    }

    internal static string Refresh(Actor actor, int currentYear, bool recordHistory)
    {
        if (actor?.data == null) return string.Empty;
        ImageSnapshot next = Build(actor);
        long actorId = ((BaseSystemData)actor.data).id;
        XjHighRealmAggregateStore.ApplyImage(actorId, next.Stage, next.Integrity, next.Summary);
        string previous = ReadString(actor, XjActorDataKeys.XjGuoWeiImageState);
        if (!string.Equals(previous, next.Summary, StringComparison.Ordinal))
        {
            XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiImageState, next.Summary);
            if (recordHistory && !string.IsNullOrWhiteSpace(previous))
            {
                string previousShort = ShortState(previous);
                string nextShort = ShortState(next.Summary);
                string item = !string.Equals(previousShort, nextShort, StringComparison.Ordinal)
                    ? XjChronology.FormatYear(Math.Max(1, currentYear)) + "·" + previousShort + "→" + nextShort
                    : XjChronology.FormatYear(Math.Max(1, currentYear)) + "·真景改易：" + next.Summary;
                AppendHistory(actor, item);
            }
        }
        return next.Summary;
    }

    internal static string BuildSummary(Actor actor)
    {
        if (actor?.data == null) return string.Empty;
        long actorId = ((BaseSystemData)actor.data).id;
        if (XjHighRealmAggregateStore.TryGetImageSummary(actorId, out string cached)) return cached;
        string current = ReadString(actor, XjActorDataKeys.XjGuoWeiImageState);
        // UI 读取不再写业务状态。旧档尚无摘要时只做一次只读投影，正式写回留给年度/夺柄/闭关节点。
        string summary = string.IsNullOrWhiteSpace(current) ? Build(actor).Summary : current;
        // 只写运行态聚合表，不写ActorData，不触发存档脏标记。
        int stage = ReadCachedStage(actor);
        string integrity = ResolveIntegrityFromSummary(summary);
        XjHighRealmAggregateStore.ApplyImage(actorId, stage, integrity, summary);
        return summary;
    }

    internal static float ResolveCultivationMultiplier(Actor actor)
    {
        if (actor?.data == null) return 1f;
        long actorId = ((BaseSystemData)actor.data).id;
        if (!XjHighRealmAggregateStore.TryGetImageState(actorId, out int stage, out string integrity))
        {
            stage = ReadCachedStage(actor);
            integrity = ReadCachedIntegrity(actor);
            XjHighRealmAggregateStore.ApplyImage(actorId, stage, integrity);
        }
        float multiplier = stage switch
        {
            0 => 0.98f,
            1 => 0.99f,
            2 => 1.00f,
            3 => 1.01f,
            4 => 1.02f,
            5 => 1.03f,
            _ => 1.04f
        };
        if (string.Equals(integrity, "残缺", StringComparison.Ordinal)) multiplier -= 0.03f;
        else if (string.Equals(integrity, "异化", StringComparison.Ordinal)) multiplier -= 0.02f;
        else if (string.Equals(integrity, "圆融", StringComparison.Ordinal)) multiplier += 0.02f;
        return Math.Clamp(multiplier, 0.95f, 1.08f);
    }

    /// <summary>
    /// 果位真景只提供小幅独立战斗差，不取代权柄数量乘区。
    /// 每一阶段差+1.5%，最多+8%；残缺/异化会降低自身真景评分。
    /// </summary>
    internal static float ResolveCombatAdvantage(Actor attacker, Actor defender)
    {
        if (attacker?.data == null || defender?.data == null) return 0f;
        int diff = ResolveCombatScore(attacker) - ResolveCombatScore(defender);
        return diff <= 0 ? 0f : Math.Min(0.08f, diff * 0.015f);
    }

    private static int ResolveCombatScore(Actor actor)
    {
        if (actor?.data == null) return 0;
        long actorId = ((BaseSystemData)actor.data).id;
        if (XjHighRealmAggregateStore.TryGetImageScore(actorId, out int score)) return score;

        // 旧档首次战斗仅回填一次；正常年度刷新后不会进入字符串解析。
        int stage = ReadCachedStage(actor);
        string integrity = ReadCachedIntegrity(actor);
        XjHighRealmAggregateStore.ApplyImage(actorId, stage, integrity);
        return XjHighRealmAggregateStore.TryGetImageScore(actorId, out score)
            ? score
            : XjHighRealmAggregateStore.ResolveImageScore(stage, integrity);
    }

    private static int ReadCachedStage(Actor actor)
    {
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
        return ResolveStage(Math.Clamp(image, 0, 10000));
    }

    private static string ReadCachedIntegrity(Actor actor)
    {
        string summary = ReadString(actor, XjActorDataKeys.XjGuoWeiImageState);
        return ResolveIntegrityFromSummary(summary);
    }

    private static string ResolveIntegrityFromSummary(string summary)
    {
        string value = summary ?? string.Empty;
        if (value.Contains("·圆融", StringComparison.Ordinal)) return "圆融";
        if (value.Contains("·残缺", StringComparison.Ordinal)) return "残缺";
        if (value.Contains("·异化", StringComparison.Ordinal)) return "异化";
        if (value.Contains("·杂映", StringComparison.Ordinal)) return "杂映";
        return "未足";
    }

    private static ImageSnapshot Build(Actor actor)
    {
        XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
        image = Math.Clamp(image, 0, 10000);
        int stage = ResolveStage(image);
        string stageName = ResolveStageName(stage);
        string manifest = ResolveManifestDaoTu(actor);

        List<string> native = new List<string>();
        List<string> foreign = new List<string>();
        int lostLocal = 0;
        long actorId = ((BaseSystemData)actor.data).id;
        if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
        {
            if (!XjHighRealmAggregateStore.TryGetAuthoritySets(
                actorId,
                out IReadOnlyList<string> localAuthorities,
                out IReadOnlyList<string> seizedAuthorities,
                out IReadOnlyList<string> foreignAuthorities))
            {
                XjHighRealmAggregateStore.ApplyAuthority(in state);
                XjHighRealmAggregateStore.TryGetAuthoritySets(
                    actorId,
                    out localAuthorities,
                    out seizedAuthorities,
                    out foreignAuthorities);
            }

            for (int i = 0; i < localAuthorities.Count; i++)
            {
                if (XjGuoWeiQuanBingRegistry.IsAuthorityLost(manifest, localAuthorities[i]))
                {
                    lostLocal++;
                    continue;
                }
                AddUnique(native, XjDaoIntentionCatalog.Resolve(manifest, localAuthorities[i]));
            }
            AddForeignIntentions(foreign, manifest, seizedAuthorities, state.PendingExternalZhengWeiDaoTu);
            AddForeignIntentions(foreign, manifest, foreignAuthorities, state.PendingExternalZhengWeiDaoTu);
        }
        else
        {
            XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string scope);
            string[] localAuthorities = Split(scope);
            for (int i = 0; i < localAuthorities.Length; i++)
            {
                if (XjGuoWeiQuanBingRegistry.IsAuthorityLost(manifest, localAuthorities[i]))
                {
                    lostLocal++;
                    continue;
                }
                AddUnique(native, XjDaoIntentionCatalog.Resolve(manifest, localAuthorities[i]));
            }
        }

        string integrity;
        if (lostLocal > 0) integrity = "残缺";
        else if (foreign.Count >= 3) integrity = "异化";
        else if (foreign.Count > 0) integrity = "杂映";
        else if (stage >= 5 && native.Count >= 5) integrity = "圆融";
        else integrity = "未足";

        int displayCount = Math.Max(1, Math.Min(6, stage == 0 ? 1 : stage));
        string nativeDisplay = JoinFirst(native, displayCount);
        string foreignDisplay = JoinFirst(foreign, Math.Min(3, displayCount));
        List<string> parts = new List<string>();
        if (nativeDisplay.Length > 0) parts.Add("本象：" + nativeDisplay);
        if (foreignDisplay.Length > 0) parts.Add("客象：" + foreignDisplay);
        if (XjFruitTransitionCatalog.TryResolveAvailable(actor, out XjFruitTransitionRule transition))
            parts.Add("果位嬗变：" + transition.ResultName);
        if (parts.Count == 0 && manifest.Length > 0)
        {
            IReadOnlyList<string> defaults = XjDaoIntentionCatalog.Get(manifest);
            if (defaults.Count > 0) parts.Add("潜象：" + defaults[0]);
        }
        string summary = stageName + "·" + integrity;
        if (parts.Count > 0) summary += "（" + string.Join("；", parts) + "）";
        return new ImageSnapshot(image, stage, stageName, integrity, summary);
    }

    private static void AddForeignIntentions(
        List<string> target,
        string manifest,
        IReadOnlyList<string> authorities,
        string pendingSource)
    {
        if (authorities == null) return;
        for (int i = 0; i < authorities.Count; i++)
        {
            string source = string.Empty;
            if (XjDaoIntentionCatalog.TryResolveAuthorityOwner(authorities[i], out string owner, out _)) source = owner;
            if (source.Length == 0) source = (pendingSource ?? string.Empty).Trim();
            if (source.Length == 0 || string.Equals(source, manifest, StringComparison.Ordinal)) continue;
            string intention = XjDaoIntentionCatalog.Resolve(source, authorities[i]);
            if (intention.Length == 0) continue;
            AddUnique(target, source + "·" + intention);
        }
    }

    private static int ResolveStage(int image)
    {
        if (image >= 9000) return 6;
        if (image >= 7500) return 5;
        if (image >= 5000) return 4;
        if (image >= 3000) return 3;
        if (image >= 1500) return 2;
        if (image >= 500) return 1;
        return 0;
    }

    private static string ResolveStageName(int stage)
    {
        return stage switch
        {
            1 => "一象初显",
            2 => "二象交映",
            3 => "三象成域",
            4 => "四象成景",
            5 => "五象合真",
            6 => "六象圆满",
            _ => "真景未成"
        };
    }

    private static string ResolveManifestDaoTu(Actor actor)
    {
        XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
        if (string.IsNullOrWhiteSpace(manifest)) XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out manifest);
        if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(manifest, out string display)) manifest = display;
        return (manifest ?? string.Empty).Trim();
    }

    private static string[] Split(string text)
    {
        return (text ?? string.Empty).Split(new[] { '|', '、', ',', '，', ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static void AddUnique(List<string> target, string value)
    {
        string normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > 0 && !target.Contains(normalized)) target.Add(normalized);
    }

    private static string JoinFirst(List<string> values, int maximum)
    {
        if (values == null || values.Count == 0 || maximum <= 0) return string.Empty;
        int count = Math.Min(values.Count, maximum);
        return string.Join("、", values.GetRange(0, count));
    }

    private static string ShortState(string summary)
    {
        string value = (summary ?? string.Empty).Trim();
        int split = value.IndexOf('（');
        return split > 0 ? value.Substring(0, split) : value;
    }

    private static string ReadString(Actor actor, string key)
    {
        return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
    }

    private static void AppendHistory(Actor actor, string item)
    {
        string current = ReadString(actor, XjActorDataKeys.XjGuoWeiImageHistory);
        List<string> entries = new List<string>((current ?? string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries));
        if (!entries.Contains(item)) entries.Add(item);
        while (entries.Count > 6) entries.RemoveAt(0);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiImageHistory, string.Join("|", entries));
    }
}
