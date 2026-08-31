using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 0.9.8.7 原著具名神通的视觉桥。
///
/// 只负责“这个已结算技能应该怎样被看见”，不保存玩法状态、不扫描世界、
/// 不复制伤害。所有帧仍由 XjCombatEffectPool 缓存并复用原生 fx_slash 容器。
/// </summary>
internal static class XjCanonicalShenTongVisualSystem
{
    internal const string InnerWorldPath = "effects/Skills/Canon0986/InnerWorld";
    internal const string ThunderFormationPath = "effects/Skills/Canon0986/ThunderFormation";
    internal const string PurpleLightningPath = "effects/Skills/Canon0986/PurpleLightning";
    internal const string FireFormationPath = "effects/Skills/Canon0986/FireFormation";
    internal const string SpearRainPath = "effects/Skills/Canon0986/SpearRain";
    internal const string GoldenCitadelSealPath = "effects/Skills/Canon0986/GoldenCitadelSeal";
    internal const string FlameLancePath = "effects/Skills/Canon0986/FlameLance";
    internal const string WaterFormationPath = "effects/Skills/Canon0986/WaterFormation";
    internal const string WaterSlashPath = "effects/Skills/Canon0986/WaterSlash";
    internal const string WindFormationPath = "effects/Skills/Canon0986/WindFormation";
    internal const string StreamsToWindPath = "effects/Skills/Canon0986/StreamsToWind";
    internal const string WoodArcanaPath = "effects/Skills/Canon0986/WoodArcana";
    internal const string AzureLotusPath = "effects/Skills/Canon0987/Azure_Lotus_Restoration";
    internal const string EarthFormationPath = "effects/Skills/Canon0987/Earth_Formation";
    internal const string EarthSpearPath = "effects/Skills/Canon0987/Earth_Spear_Art";
    internal const string IceDetonationPath = "effects/Skills/Shared/IceDetonation";

    // 纯视觉被动提示只按正在战斗的角色建立小表；不写存档、不参与伤害与施法CD。
    private static readonly Dictionary<long, float> PassiveVisualReadyAt = new Dictionary<long, float>(64);

    internal static bool TryPlayPassiveCombatCue(Actor caster)
    {
        if (caster?.data == null) return false;
        long actorId = GetActorId(caster);
        if (actorId <= 0L) return false;
        float now = Time.time;
        if (PassiveVisualReadyAt.TryGetValue(actorId, out float readyAt) && now < readyAt) return false;

        string[] learned = XjXianJiAccessor.ReadRawIds(caster);
        bool played = false;
        // 这些条目在 Excel 中主要是身法、气象、神职或隐匿，不额外编造伤害。
        if (ContainsAny(learned, "神布序", "听醒辰", "听曜辰"))
        {
            played = PlayAtCaster(caster, ThunderFormationPath, 0.11f, 0.075f);
        }
        else if (ContainsAny(learned, "飓鬼阴", "魇鬼阴", "枭逐狸"))
        {
            played = PlayAtCaster(caster, InnerWorldPath, 0.11f, 0.075f)
                | PlayAtCaster(caster, StreamsToWindPath, 0.10f, 0.065f);
        }
        else if (ContainsAny(learned, "应帝王"))
        {
            // 原著只给出“霸道恢宏、诡异莫测”的气象，不额外附加伤害/控制。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.11f, 0.075f);
        }
        else if (ContainsAny(learned, "白玉盘"))
        {
            // 皎洁玉盘高悬，仅做低频显化；不把太阴属性继续扩写成额外战斗规则。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.13f, 0.075f);
        }
        else if (ContainsAny(learned, "千百身", "箝恨口"))
        {
            // 身化滚滚黑煞、聚散无形。这里仅给身法态提示，治疗仍由罗剎海既有领域结算。
            played = PlayAtCaster(caster, InnerWorldPath, 0.105f, 0.075f);
        }
        else if (ContainsAny(learned, "议八辟"))
        {
            // 原著有宫阙万千、臣属贵重的意象；只做护身显化，不凭空附加免控。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.10f, 0.075f);
        }
        else if (ContainsAny(learned, "抱石眠"))
        {
            // 真炁抱石眠：生机绵长、肌骨还真。只做低频身命显化，不虚构主动攻击。
            played = PlayAtCaster(caster, AzureLotusPath, 0.10f, 0.085f);
        }
        else if (ContainsAny(learned, "好功箓", "瑞气云"))
        {
            // 瑞炁以观运、知祸福为主；这里只给淡金瑞气提示。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.075f, 0.085f);
        }
        else if (ContainsAny(learned, "冠灵旒"))
        {
            // 华炁当前原著只明确与司天神布序互补，故不编造伤害。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.085f, 0.08f);
        }
        else if (ContainsAny(learned, "入清听"))
        {
            // 寒炁自动警醒恶念：冷白气息一闪即可，不建立额外监听扫描。
            played = PlayAtCaster(caster, IceDetonationPath, 0.075f, 0.07f);
        }
        else if (ContainsAny(learned, "浥铅华", "混铅华", "制飬宜", "制养宜", "秘白汞", "秘白录", "候神殊", "侯神殊", "金书序"))
        {
            // 全丹的铅汞、养玄与物性变化以银白/金铁流光显化；复杂神尸机制不由视觉层伪造。
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.075f, 0.085f)
                | (!XjCombatEffectPool.IsHighLoad() && PlayAtCaster(caster, WaterSlashPath, 0.075f, 0.065f));
        }
        else if (ContainsAny(learned, "降魂闻"))
        {
            played = PlayAtCaster(caster, InnerWorldPath, 0.095f, 0.08f);
        }
        else if (ContainsAny(learned, "伏青山", "青宣岳", "上岩神"))
        {
            played = ContainsAny(learned, "伏青山")
                ? PlayAtCaster(caster, EarthFormationPath, 0.13f, 0.08f)
                : PlayAtCaster(caster, AzureLotusPath, 0.085f, 0.085f);
        }
        else if (ContainsAny(learned, "致缉熙"))
        {
            played = PlayAtCaster(caster, GoldenCitadelSealPath, 0.085f, 0.08f);
        }
        else if (ContainsAny(learned, "炁临宇", "炁引池", "浮云身"))
        {
            played = PlayAtCaster(caster, WindFormationPath, 0.12f, 0.075f)
                | PlayAtCaster(caster, StreamsToWindPath, 0.10f, 0.065f);
        }

        // 没有匹配到本批视觉神通时也做短暂负缓存。否则普通金丹/真君每次平A
        // 都会重复拆解神通数组，视觉系统反而会进入战斗热路径。神通变化后最多30~45秒
        // 自动重新识别，完全不需要扫描角色或写日志。
        PassiveVisualReadyAt[actorId] = now + (played
            ? (XjCombatEffectPool.IsHighLoad() ? 20f : 12f)
            : (XjCombatEffectPool.IsHighLoad() ? 45f : 30f));
        return played;
    }

    internal static bool TryPlayXingDuQianManifestation(Actor source, Actor phantom)
    {
        bool played = false;
        if (source?.data != null) played |= PlayAtCaster(source, InnerWorldPath, 0.11f, 0.075f);
        if (phantom?.data != null)
        {
            played |= PlayAtCaster(phantom, InnerWorldPath, 0.095f, 0.075f);
            if (!XjCombatEffectPool.IsHighLoad())
                played |= PlayAtCaster(phantom, StreamsToWindPath, 0.085f, 0.065f);
        }
        return played;
    }

    internal static bool TryPlayHouShenShuShenShi(Actor actor)
    {
        if (actor?.data == null) return false;
        bool played = PlayAtCaster(actor, GoldenCitadelSealPath, 0.105f, 0.08f);
        if (!XjCombatEffectPool.IsHighLoad())
            played |= PlayAtCaster(actor, WaterSlashPath, 0.085f, 0.065f);
        return played;
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId > 0L) PassiveVisualReadyAt.Remove(actorId);
    }

    internal static void Clear()
    {
        PassiveVisualReadyAt.Clear();
    }

    internal static bool TryPlayDirectSupplemental(
        Actor caster,
        in XjJinDanDaoSpellDefinition definition,
        in XjJinDanDaoSpellTargetContext context)
    {
        WorldTile center = context.CenterTile ?? TryGetTile(caster);
        if (center == null || string.IsNullOrWhiteSpace(definition.Id)) return false;

        try
        {
            switch (definition.Id)
            {
                case "ZiQiDaoShiZhao":
                    return PlayDaoShiZhao(center);
                case "SiTianShenBuXu":
                {
                    bool played = PlayOnFirstTarget(context, PurpleLightningPath, 0.12f, 0.065f);
                    // Excel只明确“冠灵旒与神布序互为弥补”，没有给出可量化战斗倍率；
                    // 因此这里只在两神通同持时增加冠旒/天序相合的视觉，不编造额外伤害。
                    if (ActorHasShenTong(caster, "冠灵旒"))
                        played |= PlayAtCaster(caster, GoldenCitadelSealPath, 0.095f, 0.075f);
                    return played;
                }
                case "XiaoKuiJuGuiYin":
                    return PlayAtCaster(caster, StreamsToWindPath, 0.13f, 0.065f);
                case "YuZhenYuTingJiang":
                    return PlayAtCaster(caster, GoldenCitadelSealPath, 0.09f, 0.075f);
                case "ShangWuYingDiWang":
                    return PlayAtCaster(caster, GoldenCitadelSealPath, 0.11f, 0.075f);
                case "XiQiQiDaiYe":
                    return PlayAtCaster(caster, InnerWorldPath, 0.14f, 0.075f);
                case "HanQiSongShangXue":
                    return PlayAtCaster(caster, IceDetonationPath, 0.12f, 0.07f)
                        | PlayAtCaster(caster, StreamsToWindPath, 0.12f, 0.065f);
                case "ShangYiSheShouWang":
                    return PlayAtCaster(caster, GoldenCitadelSealPath, 0.08f, 0.075f)
                        | PlayOnFirstTarget(context, GoldenCitadelSealPath, 0.07f, 0.075f);
                case "QingXuanFuQingShan":
                    return PlayAtCaster(caster, EarthFormationPath, 0.16f, 0.08f);
                case "ZhiBoJianKuangRang":
                    return PlayAt(center, InnerWorldPath, 0.15f, 0.075f)
                        | PlayAt(center, GoldenCitadelSealPath, 0.11f, 0.075f);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryPlayDomainOpening(
        Actor caster,
        XjDomainSkillKind kind,
        string activeShenTongName,
        WorldTile center,
        int radius)
    {
        if (center == null) return false;
        try
        {
            switch (kind)
            {
                case XjDomainSkillKind.QingQiYunJing:
                    return PlayAt(center, WindFormationPath, 0.24f, 0.075f)
                        | PlayAtCaster(caster, StreamsToWindPath, 0.12f, 0.065f);
                case XjDomainSkillKind.YangShengZhu:
                    // 紫炁之域、明亮光罩与护持气象；只补视觉，不改变既有减伤/治疗语义。
                    return PlayAt(center, GoldenCitadelSealPath, 0.22f, 0.075f)
                        | (!XjCombatEffectPool.IsHighLoad()
                            && PlayAtCaster(caster, PurpleLightningPath, 0.09f, 0.065f));
                case XjDomainSkillKind.BuKongJie:
                    return PlayAt(center, InnerWorldPath, 0.28f, 0.075f);
                case XjDomainSkillKind.LuoChaHai:
                    return PlayAt(center, InnerWorldPath, 0.34f, 0.075f)
                        | (!XjCombatEffectPool.IsHighLoad()
                            && PlayOffset(center, -2.2f, 1.1f, InnerWorldPath, 0.16f, 0.075f));
                case XjDomainSkillKind.QingYuYa:
                    return PlayAt(center, GoldenCitadelSealPath, 0.25f, 0.075f)
                        | PlayAt(center, SpearRainPath, 0.13f, 0.065f);
                case XjDomainSkillKind.ManYinGuang:
                    return PlayAt(center, FireFormationPath, 0.30f, 0.075f)
                        | (!XjCombatEffectPool.IsHighLoad()
                            && PlayAt(center, InnerWorldPath, 0.13f, 0.075f));
                case XjDomainSkillKind.DongYuShan:
                    return PlayAt(center, EarthFormationPath, 0.28f, 0.08f)
                        | (!XjCombatEffectPool.IsHighLoad() && PlayAt(center, EarthSpearPath, 0.12f, 0.07f));
                case XjDomainSkillKind.XiTianYuan:
                    return PlayAt(center, WindFormationPath, 0.28f, 0.075f)
                        | PlayAt(center, EarthFormationPath, 0.18f, 0.08f);
                case XjDomainSkillKind.NanChouShui:
                    return PlayAt(center, WaterFormationPath, 0.30f, 0.075f)
                        | (!XjCombatEffectPool.IsHighLoad() && PlayAt(center, InnerWorldPath, 0.13f, 0.075f));
                case XjDomainSkillKind.YuanZhaoShuiYueJing:
                    return PlayYuanZhaoOpening(activeShenTongName, center);
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool PlayDaoShiZhao(WorldTile center)
    {
        // 原著描述四尊炁神分镇四方：北雷、东木、南火、西白烟坚帛。
        // 这里用四套已有东方/中性素材拼成方位显化，不虚构第五尊或额外伤害。
        bool spawned = false;
        spawned |= PlayOffset(center, 0f, 2.2f, ThunderFormationPath, 0.10f, 0.075f);
        spawned |= PlayOffset(center, 2.2f, 0f, WoodArcanaPath, 0.075f, 0.075f);
        spawned |= PlayOffset(center, 0f, -2.2f, FireFormationPath, 0.10f, 0.075f);
        spawned |= PlayOffset(center, -2.2f, 0f, GoldenCitadelSealPath, 0.10f, 0.075f);
        return spawned;
    }

    private static bool PlayYuanZhaoOpening(string name, WorldTile center)
    {
        switch ((name ?? string.Empty).Trim())
        {
            case "月沉渊":
                return PlayAt(center, WaterFormationPath, 0.30f, 0.075f)
                    | PlayAt(center, InnerWorldPath, 0.14f, 0.075f);
            case "照无身":
                return PlayAt(center, GoldenCitadelSealPath, 0.17f, 0.075f)
                    | PlayAt(center, WaterSlashPath, 0.10f, 0.06f);
            case "回澜鉴":
                return PlayAt(center, WaterFormationPath, 0.23f, 0.075f)
                    | PlayAt(center, WaterSlashPath, 0.13f, 0.06f);
            case "影归真":
                return PlayAt(center, InnerWorldPath, 0.18f, 0.075f)
                    | PlayAt(center, WaterFormationPath, 0.16f, 0.075f);
            case "一泓寂":
                return PlayAt(center, WaterFormationPath, 0.36f, 0.075f);
            default:
                return PlayAt(center, WaterFormationPath, 0.22f, 0.075f);
        }
    }

    private static bool PlayOnFirstTarget(
        in XjJinDanDaoSpellTargetContext context,
        string path,
        float scale,
        float interval)
    {
        if (context.Targets == null) return false;
        for (int i = 0; i < context.Targets.Count; i++)
        {
            Actor target = context.Targets[i];
            WorldTile tile = TryGetTile(target);
            if (tile != null) return PlayAt(tile, path, scale, interval);
        }
        return false;
    }

    private static bool PlayAtCaster(Actor caster, string path, float scale, float interval)
    {
        WorldTile tile = TryGetTile(caster);
        return tile != null && PlayAt(tile, path, scale, interval);
    }

    private static bool PlayAt(WorldTile tile, string path, float scale, float interval)
    {
        if (tile == null || string.IsNullOrWhiteSpace(path)) return false;
        Vector2Int pos = tile.pos;
        return XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
            path,
            pos.x + 0.5f,
            pos.y + 0.5f,
            scale,
            interval);
    }

    private static bool PlayOffset(
        WorldTile tile,
        float dx,
        float dy,
        string path,
        float scale,
        float interval)
    {
        if (tile == null || string.IsNullOrWhiteSpace(path)) return false;
        Vector2Int pos = tile.pos;
        return XjCombatEffectPool.TrySpawnSpriteAnimationSafe(
            path,
            pos.x + 0.5f + dx,
            pos.y + 0.5f + dy,
            scale,
            interval);
    }

    private static bool ActorHasShenTong(Actor actor, params string[] names)
    {
        return actor?.data != null && ContainsAny(XjXianJiAccessor.ReadRawIds(actor), names);
    }

    private static bool ContainsAny(string[] learned, params string[] names)
    {
        if (learned == null || names == null) return false;
        for (int i = 0; i < learned.Length; i++)
        {
            string current = (learned[i] ?? string.Empty).Trim();
            if (current.Length == 0) continue;
            for (int j = 0; j < names.Length; j++)
            {
                if (string.Equals(current, names[j], StringComparison.Ordinal)) return true;
            }
        }
        return false;
    }

    private static long GetActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }

    private static WorldTile TryGetTile(Actor actor)
    {
        try
        {
            return actor?.data == null ? null : ((BaseSimObject)actor).current_tile;
        }
        catch
        {
            return null;
        }
    }
}
