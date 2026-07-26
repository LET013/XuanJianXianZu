using System;
using System.Collections.Generic;
using strings;
using UnityEngine;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Traits;

internal static class XjVNextAssetRegistration
{
    private const string DefaultIcon = "trait/XjRealm1";
    private static bool _initialized;
    private static LocalizedTextManager _runtimeLocalizationManager;
    private static bool _runtimeLocalizationRegistered;

    private const string ShenTongZaoHuaName = "神通造化";
    private const string ShenTongZaoHuaInfo = "将角色现有的其他道途神通，逐一随机改造为其当前道途的上位、下位或相邻神通，并同步调整对应功法；不增加神通数量，赋予后立即生效并自动移除。";

    internal static readonly IReadOnlyList<XjVNextTraitAssetInfo> TraitAssets = BuildTraitAssets();

    internal static readonly IReadOnlyList<XjVNextTraitGroupInfo> TraitGroups = new[]
    {
        new XjVNextTraitGroupInfo("XjRealm", "trait_group_XjRealm", "#8E7CFF"),
        new XjVNextTraitGroupInfo("XjZz", "trait_group_XjZz", "#FFD166"),
        new XjVNextTraitGroupInfo("YinYang", "trait_group_YinYang", "#70D6FF"),
        new XjVNextTraitGroupInfo("SanLei", "trait_group_SanLei", "#A7B8FF"),
        new XjVNextTraitGroupInfo("JinDe", "trait_group_JinDe", "#E8D06A"),
        new XjVNextTraitGroupInfo("MuDe", "trait_group_MuDe", "#75C46B"),
        new XjVNextTraitGroupInfo("ShuiDe", "trait_group_ShuiDe", "#6BB7E8"),
        new XjVNextTraitGroupInfo("HuoDe", "trait_group_HuoDe", "#E8795C"),
        new XjVNextTraitGroupInfo("TuDe", "trait_group_TuDe", "#B89768"),
        new XjVNextTraitGroupInfo("SuDe", "trait_group_SuDe", "#E8E2D0"),
        new XjVNextTraitGroupInfo("ShiErQi", "trait_group_ShiErQi", "#C3A7FF"),
        new XjVNextTraitGroupInfo("ChuShen", "trait_group_ChuShen", "#7BD88F"),
        new XjVNextTraitGroupInfo("ChuShenSpecial", "trait_group_ChuShenSpecial", "#D7A8FF"),
        new XjVNextTraitGroupInfo("XjHighRealmDescendant", "trait_group_XjHighRealmDescendant", "#E6B35A"),
        new XjVNextTraitGroupInfo("JinDan", "trait_group_JinDan", "#E7C565"),
        new XjVNextTraitGroupInfo("XuanJianTraits", "trait_group_XuanJianTraits", "#C7F0FF"),
        new XjVNextTraitGroupInfo("XjCraft", "trait_group_XjCraft", "#D8B26A"),
        new XjVNextTraitGroupInfo("DebugTraits", "trait_group_DebugTraits", "#32CD32")
    };

    internal static void Init()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        RegisterBaseStats();
        RegisterTraitGroups();
        RegisterTraitAssets();
        XjFaBaoEquipmentAssets.Init();
    }

    private static IReadOnlyList<XjVNextTraitAssetInfo> BuildTraitAssets()
    {
        List<XjVNextTraitAssetInfo> traits = new List<XjVNextTraitAssetInfo>();

        // Dodge / Accuracy 是百分比点战斗属性，禁止再使用旧版 50~10000 的“评级值”。
        // 旧值进入统一结算后会被钳制为 100% 闪避，造成低境界永远无法命中高境界。
        // 境界差由 XjRealmSuppression 处理；闪避与命中只来自法宝、纪元和境界战斗档案。
        traits.Add(T("XjRealm1", "XjRealm", "trait/XjRealm1", "境界", 2,
            Stat("lifespan", 10f), Stat("Resist", 1.5f), Stat(S.damage, 20f), Stat("mass", 10f),
            Stat(S.health, 100f), Stat("accuracy", 3f), Stat("multiplier_speed", 0.2f),
            Stat("stamina", 30f)));
        traits.Add(T("XjRealm2", "XjRealm", "trait/XjRealm2", "境界", 2,
            Stat("lifespan", 100f), Stat("Resist", 10f), Stat("warfare", 15f), Stat("mass", 18f),
            Stat(S.damage, 100f), Stat(S.health, 1000f), Stat(S.speed, 12f),
            Stat("targets", 3f), Stat("accuracy", 25f),
            Stat("multiplier_speed", 0.6f), Stat(S.attack_speed, 6f), Stat("stamina", 80f)));
        traits.Add(T("XjRealm3", "XjRealm", "trait/XjRealm3", "境界", 2,
            Stat("lifespan", 200f), Stat("Resist", 128f), Stat("warfare", 50f), Stat(S.damage, 1000f),
            Stat("mass", 140f), Stat(S.health, 10000f), Stat(S.speed, 30f),
            Stat("area_of_effect", 4f), Stat("targets", 12f),
            Stat("accuracy", 60f), Stat("multiplier_speed", 1.2f), Stat("stamina", 400f),
            Stat("range", 6f), Stat(S.attack_speed, 6f), Stat("scale", 0.08f),
            Stat("multiplier_health", 2f), Stat("multiplier_damage", 2f)));
        traits.Add(T("XjRealm4", "XjRealm", "trait/XjRealm4", "境界", 3,
            Stat("lifespan", 400f), Stat("Resist", 256f), Stat("warfare", 100f), Stat(S.damage, 10000f),
            Stat("mass", 180f), Stat(S.health, 100000f), Stat(S.speed, 40f),
            Stat("area_of_effect", 5f), Stat("targets", 16f),
            Stat("accuracy", 80f), Stat("multiplier_speed", 1.5f), Stat("stamina", 500f),
            Stat("range", 8f), Stat(S.attack_speed, 8f), Stat("scale", 0.1f),
            Stat("multiplier_health", 3f), Stat("multiplier_damage", 3f)));
        traits.Add(T("XjRealm5", "XjRealm", "trait/XjRealm5", "境界", 3,
            Stat("Resist", 512f), Stat("warfare", 200f), Stat(S.damage, 100000f),
            Stat("mass", 360f), Stat(S.health, 1000000f), Stat(S.speed, 80f),
            Stat("area_of_effect", 30f), Stat("targets", 96f),
            Stat("accuracy", 160f), Stat("multiplier_speed", 2f), Stat("stamina", 1000f),
            Stat("range", 48f), Stat(S.attack_speed, 10f), Stat("scale", 0.1f),
            Stat("multiplier_health", 5f), Stat("multiplier_damage", 5f)));

        traits.Add(T("XjZz1", "XjZz", "trait/XjZz1", "资质", NoRarity));
        traits.Add(T("XjZz2", "XjZz", "trait/XjZz2", "资质", NoRarity));
        traits.Add(T("XjZz3", "XjZz", "trait/XjZz3", "资质", NoRarity));
        traits.Add(T("XjZz4", "XjZz", "trait/XjZz4", "资质", 2));
        traits.Add(T("XjZz5", "XjZz", "trait/XjZz5", "资质", 2));
        traits.Add(T("XjZz6", "XjZz", "trait/XjZz6", "资质", 3));
        traits.Add(T("XjZz7", "XjZz", "trait/XjZz7", "资质增益", 1));
        traits.Add(T("XjZz8", "XjZz", "trait/XjZz8", "资质伤损", NoRarity));
        traits.Add(T("XjZz9", "XjZz", "trait/XjZz9", "资质伤损", NoRarity));

        traits.Add(T("YinYang1", "YinYang", "trait/YinYang1", "道途", 3));
        traits.Add(T("YinYang2", "YinYang", "trait/YinYang2", "道途", 3));
        traits.Add(T("YinYang3", "YinYang", "trait/YinYang3", "道途", 2));
        traits.Add(T("YinYang4", "YinYang", "trait/YinYang4", "道途", 2));
        traits.Add(T("YinYang5", "YinYang", "trait/YinYang5", "道途", 2));
        traits.Add(T("YinYang6", "YinYang", "trait/YinYang6", "道途", 2));

        AddSeries(traits, "SanLei", "SanLei", 1, 3, "道途", 2);
        AddSeries(traits, "JinDe", "JinDe", 1, 5, "道途", 2);
        AddSeries(traits, "MuDe", "MuDe", 1, 5, "道途", 2);
        AddSeries(traits, "ShuiDe", "ShuiDe", 1, 5, "道途", 2);
        AddSeries(traits, "HuoDe", "HuoDe", 1, 5, "道途", 2);
        AddSeries(traits, "TuDe", "TuDe", 1, 5, "道途", 2);
        traits.Add(T("QingXuan", "SuDe", "trait/QingXuan", "道途", 2));
        AddSeries(traits, "ShiErQi", "ShiErQi", 1, 12, "道途", 2);

        traits.Add(T("ChuShen1", "ChuShen", "trait/ChuShen1", "出身", NoRarity,
            Stat("multiplier_health", 0.01f), Stat("multiplier_damage", 0.01f), Stat("lifespan", 5f)));
        traits.Add(T("ChuShen2", "ChuShen", "trait/ChuShen2", "出身", NoRarity,
            Stat("multiplier_health", 0.05f), Stat("multiplier_damage", 0.05f), Stat("lifespan", 10f)));
        traits.Add(T("ChuShen3", "ChuShen", "trait/ChuShen3", "出身", NoRarity,
            Stat("multiplier_health", 0.1f), Stat("multiplier_damage", 0.1f), Stat("lifespan", 15f)));
        traits.Add(T("ChuShen4", "ChuShen", "trait/ChuShen4", "出身", 2,
            Stat("multiplier_health", 0.15f), Stat("multiplier_damage", 0.15f), Stat("lifespan", 20f)));
        traits.Add(T("ChuShen5", "ChuShen", "trait/ChuShen4", "出身", 2,
            Stat("multiplier_health", 0.15f), Stat("multiplier_damage", 0.15f), Stat("lifespan", 20f)));
        traits.Add(T("ChuShen6", "ChuShenSpecial", "trait/ChuShen5", "出身", 2,
            Stat("multiplier_health", 0.2f), Stat("multiplier_damage", 0.2f), Stat("lifespan", 30f)));
        traits.Add(T("ChuShen7", "ChuShenSpecial", "trait/ChuShen6", "出身", 3,
            Stat("multiplier_health", 0.25f), Stat("multiplier_damage", 0.25f), Stat("lifespan", 50f)));
        traits.Add(T("ChuShen8", "ChuShenSpecial", "trait/ChuShen7", "出身", 3,
            Stat("multiplier_health", 0.3f), Stat("multiplier_damage", 0.3f), Stat("lifespan", 100f)));

        traits.Add(T("XjZiFuDescendant", "XjHighRealmDescendant", "trait/XjZiFuDescendant", "高境后裔", 2,
            Stat("lifespan", 30f), Stat(S.damage, 10f)));
        traits.Add(T("XjJinDanDescendant", "XjHighRealmDescendant", "trait/XjJinDanDescendant", "高境后裔", 3,
            Stat("lifespan", 50f), Stat(S.damage, 20f)));
        traits.Add(T("XjZiFuFamily", "XjHighRealmDescendant", "trait/XjZiFuFamily", "高境家族", 1));
        traits.Add(T("XjJinDanFamily", "XjHighRealmDescendant", "trait/XjJinDanFamily", "高境家族", 2));

		traits.Add(T(XjCraftTraitRules.AlchemyTraitId, "XjCraft", "trait/LianDanShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.ArtifactRefiningTraitId, "XjCraft", "trait/LianQiShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.TalismanTraitId, "XjCraft", "trait/FuLuShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.FormationTraitId, "XjCraft", "trait/ZhenFaShi", "玄鉴百艺", 2));

		traits.Add(T("XjYiDuiYing", "XuanJianTraits", "effects/ShenTong/TaiYin/YiDuiYing", "玄鉴神通", 3));
        traits.Add(T("XjJieLinZhang", "XuanJianTraits", "effects/ShenTong/TaiYin/JieLinZhang", "玄鉴神通", 3,
            Stat("multiplier_health", 0.15f), Stat("multiplier_damage", 0.15f)));

        // DebugTraits 分组 - 陆江仙模拟器
        traits.Add(T("DebugJinDanReincarnation", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanGuoWeiYiXiang", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanQuanBing", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanGuoWei", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugGongFa", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugClearZaQi", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugMingShu", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugHuiGuang", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugShenTongZaoHua", "DebugTraits", "trait/LuJiangXian", "Debug", 3));

        return traits;
    }

    private const int NoRarity = -1;

    private static void AddSeries(List<XjVNextTraitAssetInfo> traits, string groupId, string prefix, int from, int to, string purpose, int rarity, params XjVNextTraitStat[] stats)
    {
        for (int i = from; i <= to; i++)
        {
            string id = prefix + i.ToString();
            traits.Add(T(id, groupId, "trait/" + id, purpose, rarity, stats));
        }
    }

    private static XjVNextTraitAssetInfo T(string id, string groupId, string iconPath, string purpose, int rarity, params XjVNextTraitStat[] stats)
    {
        return new XjVNextTraitAssetInfo(id, groupId, iconPath, purpose, rarity, stats);
    }

    private static XjVNextTraitStat Stat(string id, float value)
    {
        return new XjVNextTraitStat(id, value);
    }

    /// <summary>
    /// NeoModLoader 在部分安装方式下可能先编译新特质、后继续沿用旧版
    /// Locales 缓存，导致编辑器直接暴露 trait_DebugShenTongZaoHua。
    /// 这里在世界运行后的首帧补注册一次，与“清除杂气”的显示口径一致。
    /// </summary>
    internal static void EnsureRuntimeLocalization()
    {
        LocalizedTextManager manager = LocalizedTextManager.instance;
        if (manager == null)
        {
            return;
        }

        if (!ReferenceEquals(_runtimeLocalizationManager, manager))
        {
            _runtimeLocalizationManager = manager;
            _runtimeLocalizationRegistered = false;
        }

        if (_runtimeLocalizationRegistered)
        {
            return;
        }

        try
        {
            AddRuntimeLocale("trait_DebugShenTongZaoHua", ShenTongZaoHuaName);
            AddRuntimeLocale("trait_DebugShenTongZaoHua_info", ShenTongZaoHuaInfo);
            // 兼容不同原生窗口使用的备用键，避免名称或说明再次退回底层ID。
            AddRuntimeLocale("trait_DebugShenTongZaoHua_description", ShenTongZaoHuaInfo);
            AddRuntimeLocale("DebugShenTongZaoHua", ShenTongZaoHuaName);
            AddRuntimeLocale("DebugShenTongZaoHua_description", ShenTongZaoHuaInfo);
            _runtimeLocalizationRegistered = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][神通造化] 运行期汉化补注册失败 ex=" + ex.GetType().Name);
        }
    }

    internal static void ResetRuntimeLocalization()
    {
        _runtimeLocalizationManager = null;
        _runtimeLocalizationRegistered = false;
    }

    private static void AddRuntimeLocale(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            LocalizedTextManager.add(key, value, false, string.Empty, true);
        }
    }

    private static void RegisterTraitGroups()
    {
        for (int i = 0; i < TraitGroups.Count; i++)
        {
            XjVNextTraitGroupInfo item = TraitGroups[i];
            TryAddTraitGroup(item.Id, item.LocaleKey, item.Color);
        }
    }

    private static void RegisterTraitAssets()
    {
        for (int i = 0; i < TraitAssets.Count; i++)
        {
            XjVNextTraitAssetInfo item = TraitAssets[i];
            TryAddTrait(item);
        }
    }

    private static void RegisterBaseStats()
    {
        TryAddBaseStat("ZhenYuan", -999999f, 999999f, false);
        TryAddBaseStat("MingShu", -999999f, 999999f, false);
        TryAddBaseStat("HuiGuang", -999999f, 999999f, false);
        TryAddBaseStat("Resist", 0f, 999999f, false);
        TryAddBaseStat("Dodge", 0f, 99999f, false);
        TryAddBaseStat("Accuracy", 0f, 99999f, false);
    }

    private static void TryAddBaseStat(string id, float normalizeMin, float normalizeMax, bool showAsPercents)
    {
        if (string.IsNullOrWhiteSpace(id) || AssetManager.base_stats_library == null)
        {
            return;
        }

        try
        {
            if (AssetManager.base_stats_library.get(id) != null)
            {
                return;
            }

            BaseStatAsset asset = new BaseStatAsset();
            ((Asset)asset).id = id;
            asset.normalize = true;
            asset.normalize_min = normalizeMin;
            asset.normalize_max = normalizeMax;
            asset.show_as_percents = showAsPercents;
            asset.used_only_for_civs = false;
            ((AssetLibrary<BaseStatAsset>)(object)AssetManager.base_stats_library).add(asset);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][VNextAsset] base stat 注册跳过 id=" + id + " ex=" + ex.GetType().Name);
        }
    }

    private static void TryAddTraitGroup(string id, string localeKey, string color)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        try
        {
            if (AssetManager.trait_groups.get(id) != null)
            {
                return;
            }

            AssetManager.trait_groups.add(new ActorTraitGroupAsset
            {
                id = id,
                name = string.IsNullOrWhiteSpace(localeKey) ? "trait_group_" + id : localeKey,
                color = string.IsNullOrWhiteSpace(color) ? "#FFFFFF" : color
            });
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][VNextAsset] trait group 注册跳过 id=" + id + " ex=" + ex.GetType().Name);
        }
    }

    private static void TryAddTrait(XjVNextTraitAssetInfo item)
    {
        if (string.IsNullOrWhiteSpace(item.Id))
        {
            return;
        }

        ActorTrait trait = null;
        try
        {
            trait = AssetManager.traits.get(item.Id);
        }
        catch
        {
        }

        bool isNew = trait == null;
        if (trait == null)
        {
            trait = new ActorTrait { id = item.Id };
        }

        try
        {
            trait.group_id = string.IsNullOrWhiteSpace(item.GroupId) ? "miscellaneous" : item.GroupId;
            trait.path_icon = string.IsNullOrWhiteSpace(item.IconPath) ? DefaultIcon : item.IconPath;
            trait.needs_to_be_explored = false;
            trait.rate_birth = 0;
            trait.rate_inherit = 0;
            trait.rate_acquire_grow_up = 0;
            bool hiddenFromMetaEditor = string.Equals(item.Id, "XjYiDuiYing", StringComparison.Ordinal)
                || string.Equals(item.Id, "XjJieLinZhang", StringComparison.Ordinal);
            bool hiddenFromManualGrant = hiddenFromMetaEditor
                || string.Equals(item.Id, "QingXuan", StringComparison.Ordinal);
            trait.can_be_given = !hiddenFromManualGrant;
            trait.show_in_meta_editor = !hiddenFromMetaEditor;
            if (item.Rarity >= 0)
            {
                ((BaseTrait<ActorTrait>)(object)trait).rarity = (Rarity)item.Rarity;
            }

            ApplyStats(trait, item.Stats);

            if (string.Equals(item.Id, XjRealmIds.ZiFu, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.JinDan, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.ShenDan, StringComparison.Ordinal))
            {
                // 紫府及以上通过原生攻击事件触发已掌握的领域神通。领域只读
                // 真实仙基/神通集合，不按道途自动赠送，也不新增领域属性。
                if (!XjDomainSkillRuntime.BindCombatTrigger(trait))
                {
                    Debug.LogWarning("[玄鉴][领域神通] 战斗回调绑定失败 trait=" + item.Id);
                }
            }

            if (string.Equals(item.Id, XjRealmIds.JinDan, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.ShenDan, StringComparison.Ordinal))
            {
                // 金丹与神丹都通过原生攻击事件触发道法，避免仅 RealmId 正确但
                // 可见特质不同步时完全失去施法入口。
                if (!XjJinDanDaoSpellRuntime.BindCombatTrigger(trait))
                {
                    Debug.LogWarning("[玄鉴][金丹法术] 战斗回调绑定失败 trait=" + item.Id);
                }
            }

            if (isNew)
            {
                AssetManager.traits.add(trait);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][VNextAsset] trait 注册跳过 id=" + item.Id + " ex=" + ex.GetType().Name);
        }
    }

    private static void ApplyStats(ActorTrait trait, IReadOnlyList<XjVNextTraitStat> stats)
    {
        if (trait == null)
        {
            return;
        }

        trait.base_stats = new BaseStats();
        if (stats == null)
        {
            return;
        }

        for (int i = 0; i < stats.Count; i++)
        {
            XjVNextTraitStat stat = stats[i];
            if (!string.IsNullOrWhiteSpace(stat.Id) && Math.Abs(stat.Value) > float.Epsilon)
            {
                trait.base_stats.set(stat.Id, stat.Value);
            }
        }
    }
}

internal readonly struct XjVNextTraitAssetInfo
{
    internal XjVNextTraitAssetInfo(string id, string groupId, string iconPath, string purpose, int rarity, IReadOnlyList<XjVNextTraitStat> stats)
    {
        Id = id ?? string.Empty;
        GroupId = groupId ?? string.Empty;
        IconPath = iconPath ?? string.Empty;
        Purpose = purpose ?? string.Empty;
        Rarity = rarity;
        Stats = stats ?? Array.Empty<XjVNextTraitStat>();
    }

    internal string Id { get; }
    internal string GroupId { get; }
    internal string IconPath { get; }
    internal string Purpose { get; }
    internal int Rarity { get; }
    internal IReadOnlyList<XjVNextTraitStat> Stats { get; }
}

internal readonly struct XjVNextTraitGroupInfo
{
    internal XjVNextTraitGroupInfo(string id, string localeKey, string color)
    {
        Id = id ?? string.Empty;
        LocaleKey = localeKey ?? string.Empty;
        Color = color ?? string.Empty;
    }

    internal string Id { get; }
    internal string LocaleKey { get; }
    internal string Color { get; }
}

internal readonly struct XjVNextTraitStat
{
    internal XjVNextTraitStat(string id, float value)
    {
        Id = id ?? string.Empty;
        Value = value;
    }

    internal string Id { get; }
    internal float Value { get; }
}
