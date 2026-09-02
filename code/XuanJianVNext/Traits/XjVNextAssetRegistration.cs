using System;
using System.Collections.Generic;
using strings;
using UnityEngine;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Traits;

internal static class XjVNextAssetRegistration
{
    private const string DefaultIcon = "trait/XjRealm1";
    private static bool _initialized;
    private static LocalizedTextManager _runtimeLocalizationManager;
    private static bool _runtimeLocalizationRegistered;
    private static bool _independentDaoEditorEstablished;
    private const string XjFuQiHuangGuan = "XjRealm11";
    private const string XjFuQiZhenRen = "XjRealm12";
    private const string XjFuQiZhenJunYuShi = "XjRealm13";
    private const string XjShiSeed = "XjShiSeed";
    private const string XjShiSengLv = "XjRealm21";
    private const string XjShiFaShi = "XjRealm22";
    private const string XjShiLianMin = "XjRealm23";
    private const string XjShiMoHe = "XjRealm24";
    private const string XjShiFaXiang = "XjRealm25";
    private const string XjShiShiZun = "XjRealm26";
    private const string XjShiWorldHonoredPosture = "XjShiWorldHonoredPosture";
    private const string XjGuShiDaoTrait = "XjGuShiDao";
    private const string XjJinShiDaoTrait = "XjJinShiDao";
    private const string XjLongGengDaoTongTrait = "XjLongGengDaoTong";
    private const string XjYuanZhaoDaoTongTrait = "XjYuanZhaoDaoTong";
    private const string XjHongXiaDaoTongTrait = "XjHongXiaDaoTong";
    private const string XjLuoXiaShanTrait = "XjLuoXiaShan";
    private const string XjYuanZhaoTraitIconPath = "trait/YuanZhao";
    // 虹霞图标独立使用 HongXia；落霞山门人直接复用落霞山洞天主图，避免维护第三套美术。
    private const string XjHongXiaTraitIconPath = "trait/HongXia";
    private const string XjLuoXiaShanTraitIconPath = "trait/LuoXiaShan";
    private const string XjIndependentDaoGroup = "XjIndependentDao";
    private const string XjInternalHiddenGroup = "XjInternalHidden";

    private const string SwordIntentSimulatorName = "照剑天心";
    private const string SwordIntentSimulatorInfo = "陆江仙以天鉴照彻剑心，赋予后立即为角色点化一己剑意并为其配剑；天下最多借此补录十六道剑意。";
    private const string FaBaoDengXianSimulatorName = "法宝登仙";
    private const string FaBaoDengXianSimulatorInfo = "仅可用于道胎。陆江仙点化其本命法宝，使现有金丹本命法宝同器登仙，器名、本命与道途不改。";
    private const string LuoXiaInquirySimulatorName = "问道落霞";
    private const string LuoXiaInquirySimulatorInfo = "仅在落霞山显世后可用。全世界最多三名角色可成功入门；成功者成为落霞山门人，并强制改承虹霞或戊土道途。释修、帝统与果位钟爱转世不可改纳，失败不消耗名额。";
    internal static readonly IReadOnlyList<XjVNextTraitAssetInfo> TraitAssets = BuildTraitAssets();

    internal static bool IsSystemManagedXuanJianTrait(string traitId)
    {
        if (string.IsNullOrWhiteSpace(traitId)) return false;

        for (int i = 0; i < TraitAssets.Count; i++)
        {
            XjVNextTraitAssetInfo trait = TraitAssets[i];
            if (string.Equals(trait.Id, traitId, StringComparison.Ordinal))
            {
                return IsManualGrantBlocked(trait);
            }
        }

        return false;
    }

    private static bool IsManualGrantBlocked(in XjVNextTraitAssetInfo trait)
    {
        // 编辑器只封锁八枚“玄鉴特质”、独立道途、高境后裔，以及世尊/两系道胎。
        // 其他玄鉴可见特质保留原有的手动授予入口，不能被一刀切为系统专属。
        return string.Equals(trait.GroupId, "XuanJianTraits", StringComparison.Ordinal)
            || string.Equals(trait.GroupId, "XjIndependentDao", StringComparison.Ordinal)
            || string.Equals(trait.GroupId, "XjHighRealmDescendant", StringComparison.Ordinal)
            || string.Equals(trait.Id, XjRealmIds.DaoTai, StringComparison.Ordinal)
            || string.Equals(trait.Id, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
            || string.Equals(trait.Id, XjShiShiZun, StringComparison.Ordinal);
    }

    internal static readonly IReadOnlyList<XjVNextTraitGroupInfo> TraitGroups = new[]
    {
        new XjVNextTraitGroupInfo("XjRealm", "trait_group_XjRealm", "#8E7CFF"),
        new XjVNextTraitGroupInfo("XjFuQiYangXing", "trait_group_XjFuQiYangXing", "#9AD6B3"),
        new XjVNextTraitGroupInfo("XjShiFoundation", "trait_group_XjShiFoundation", "#C9B77A"),
        new XjVNextTraitGroupInfo("XjShiRealm", "trait_group_XjShiRealm", "#E8DCA8"),
        new XjVNextTraitGroupInfo("XjShiDao", "trait_group_XjShiDao", "#D9B96E"),
        new XjVNextTraitGroupInfo("XjZz", "trait_group_XjZz", "#FFD166"),
        new XjVNextTraitGroupInfo("YinYang", "trait_group_YinYang", "#70D6FF"),
        new XjVNextTraitGroupInfo("SanLei", "trait_group_SanLei", "#A7B8FF"),
        new XjVNextTraitGroupInfo("JinDe", "trait_group_JinDe", "#E8D06A"),
        new XjVNextTraitGroupInfo("MuDe", "trait_group_MuDe", "#75C46B"),
        new XjVNextTraitGroupInfo("ShuiDe", "trait_group_ShuiDe", "#6BB7E8"),
        new XjVNextTraitGroupInfo("HuoDe", "trait_group_HuoDe", "#E8795C"),
        new XjVNextTraitGroupInfo("TuDe", "trait_group_TuDe", "#B89768"),
        new XjVNextTraitGroupInfo("XjBingGu", "trait_group_XjBingGu", "#8B739A"),
        new XjVNextTraitGroupInfo("ShiErQi", "trait_group_ShiErQi", "#C3A7FF"),
        new XjVNextTraitGroupInfo("XjIndependentDao", "trait_group_XjIndependentDao", "#9FB7FF"),
        new XjVNextTraitGroupInfo("XjInternalHidden", "trait_group_XjInternalHidden", "#6F7785"),
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
        XjForbiddenVanillaTraitPolicy.DisableManualGrant();
        SyncIndependentDaoEditorState();
        XjFaBaoEquipmentAssets.Init();
        XjLongGengFlyingSwordSystem.RegisterProjectileAsset();
    }

    private static IReadOnlyList<XjVNextTraitAssetInfo> BuildTraitAssets()
    {
        List<XjVNextTraitAssetInfo> traits = new List<XjVNextTraitAssetInfo>();

        // Dodge / Accuracy 是百分比点战斗属性，禁止再使用旧版 50~10000 的“评级值”。
        // 旧值进入统一结算后会被钳制为 100% 闪避，造成低境界永远无法命中高境界。
        // 境界差由 XjRealmSuppression 处理；闪避与命中只来自法宝、纪元和境界战斗档案。
        traits.Add(T("XjRealm1", "XjRealm", "trait/XjRealm1", "境界", NoRarity,
            Stat("lifespan", 10f), Stat("Resist", 1.5f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.056f), Stat(S.damage, 20f), Stat("mass", 10f),
            Stat(S.health, 100f), Stat("accuracy", 3f), Stat("multiplier_speed", 0.2f),
            Stat("stamina", 30f)));
        traits.Add(T("XjRealm2", "XjRealm", "trait/XjRealm2", "境界", NoRarity,
            Stat("lifespan", 100f), Stat("Resist", 10f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.14f), Stat("warfare", 15f), Stat("mass", 18f),
            Stat(S.damage, 100f), Stat(S.health, 1000f), Stat(S.speed, 12f),
            Stat("targets", 3f), Stat("accuracy", 25f),
            Stat("multiplier_speed", 0.6f), Stat(S.attack_speed, 6f), Stat("stamina", 80f)));
        traits.Add(T("XjRealm3", "XjRealm", "trait/XjRealm3", "境界", 2,
            Stat("lifespan", 200f), Stat("Resist", 128f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.812f), Stat("warfare", 50f), Stat(S.damage, 1000f),
            Stat("mass", 140f), Stat(S.health, 10000f), Stat(S.speed, 30f),
            Stat("area_of_effect", 4f), Stat("targets", 12f),
            Stat("accuracy", 60f), Stat("multiplier_speed", 1.2f), Stat("stamina", 400f),
            Stat("range", 6f), Stat(S.attack_speed, 6f), Stat("scale", 0.08f),
            Stat("multiplier_health", 2f), Stat("multiplier_damage", 2f)));
        // 紫金紫府与服气真人共用“真人级”模板，金丹与真君羽士共用“真君级”模板；
        // 差异由路线参数与独立寿元/阶段服务表达，不再让两套散落属性表参与境界识别。
        traits.Add(T("XjRealm4", "XjRealm", "trait/XjRealm4", "境界", 2, BuildZhenRenTierStats(fuQi: false)));
        traits.Add(T("XjRealm5", "XjRealm", "trait/XjRealm5", "境界", 3, BuildJinDanTierStats(fuQi: false)));
        // 仙国法只在权威状态中记录持玄借境，不再额外注册“仙国筑基/紫府/假金丹”三枚境界特质。
        // 人物页继续复用紫府金丹道已有的筑基、紫府、金丹境界图标；离朝时再按本人真实境界回落。
        traits.Add(T(XuanJianVNext.Systems.XianGuo.XjXianGuoSystem.HeavyMinisterTraitId, "XuanJianTraits", "trait/guozhizhongchen", "XianGuoOffice", 2));
        traits.Add(T(XjRealmIds.DaoTai, "XjRealm", "trait/XjRealm6", "ClosedRealm", 3, BuildDaoTaiTierStats(fuQi: false)));

        traits.Add(T(XjFuQiHuangGuan, "XjFuQiYangXing", "trait/XjRealm11", "FuQiRealm", NoRarity,
            Stat("lifespan", 400f), Stat("Resist", 115f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.76f), Stat("warfare", 45f), Stat(S.damage, 1000f),
            Stat("mass", 126f), Stat(S.health, 9000f), Stat(S.speed, 27f),
            Stat("area_of_effect", 3.6f), Stat("targets", 11f),
            Stat("accuracy", 54f), Stat("multiplier_speed", 1.08f), Stat("stamina", 360f),
            Stat("range", 5.4f), Stat(S.attack_speed, 5.4f), Stat("scale", 0.072f),
            Stat("multiplier_health", 1.8f), Stat("multiplier_damage", 1.8f)));
        traits.Add(T(XjFuQiZhenRen, "XjFuQiYangXing", "trait/XjRealm12", "FuQiRealm", 2, BuildZhenRenTierStats(fuQi: true)));
        // 真君羽士寿元由 XjRealmLifespanService 按初/中/后/巅动态结算，
        // 不再把固定寿元写进特质属性，避免工具提示显示过期寿命。
        traits.Add(T(XjFuQiZhenJunYuShi, "XjFuQiYangXing", "trait/XjRealm13", "FuQiRealm", 3, BuildJinDanTierStats(fuQi: true)));
        traits.Add(T(XjRealmIds.FuQiDaoTai, "XjFuQiYangXing", "trait/XjRealm14", "ClosedRealm", 3, BuildDaoTaiTierStats(fuQi: true)));

        // 第三套独立修炼体系。释修种子是“修炼方式”基础身份，类似仙修资质
        // 但不表达资质档次；它完全由释修权威状态投影，禁止玩家手动赋予。
        traits.Add(T(XjShiSeed, "XjShiFoundation", "trait/ShiSeed", "ShiFoundation", NoRarity));
        // 境界属性只表现综合战力，释修不会因此取得仙道 RealmId、真元、仙基、求金、果位或法宝权限。
        // 怜愍三座、摩诃金地与高位轮回已接入年度状态机；编辑器只允许补录至摩诃，法相与世尊必须真实证得。
        traits.Add(T(XjShiSengLv, "XjShiRealm", "trait/XjRealm21", "ShiRealm", NoRarity,
            Stat("Resist", 8f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.10f), Stat("warfare", 8f), Stat(S.damage, 60f), Stat("mass", 14f),
            Stat(S.health, 500f), Stat(S.speed, 8f), Stat("accuracy", 12f),
            Stat("multiplier_speed", 0.35f), Stat("stamina", 50f)));
        // 法师按“筑基后期”数值落地：以筑基基础档为基准整体×1.20，
        // 同时在战斗层级中固定视作筑基后期，避免只有文字对齐而数值仍停在初期。
        traits.Add(T(XjShiFaShi, "XjShiRealm", "trait/XjRealm22", "ShiRealm", 2,
            Stat("Resist", 153.6f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 0.9144f), Stat("warfare", 60f), Stat(S.damage, 1200f),
            Stat("mass", 168f), Stat(S.health, 12000f), Stat(S.speed, 36f),
            Stat("area_of_effect", 4.8f), Stat("targets", 14.4f), Stat("accuracy", 72f),
            Stat("multiplier_speed", 1.44f), Stat("stamina", 480f), Stat("range", 7.2f),
            Stat(S.attack_speed, 7.2f), Stat("scale", 0.096f),
            Stat("multiplier_health", 2.4f), Stat("multiplier_damage", 2.4f)));
        // 怜愍为紫府完整基础档×0.85。这里只缩放战斗数值，不复制紫府寿元、
        // 仙基、果位与仙道权限，确保仍是独立释修境界。
        traits.Add(T(XjShiLianMin, "XjShiRealm", "trait/XjRealm23", "ShiRealm", 2,
            Stat("Resist", 217.6f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 85f), Stat(S.damage, 8500f),
            Stat("mass", 153f), Stat(S.health, 85000f), Stat(S.speed, 34f),
            Stat("area_of_effect", 4.25f), Stat("targets", 13.6f), Stat("accuracy", 68f),
            Stat("multiplier_speed", 1.275f), Stat("stamina", 425f), Stat("range", 6.8f),
            Stat(S.attack_speed, 6.8f), Stat("scale", 0.085f),
            Stat("multiplier_health", 2.55f), Stat("multiplier_damage", 2.55f)));
        traits.Add(T(XjShiMoHe, "XjShiRealm", "trait/XjRealm24", "ShiRealm", 2,
            Stat("Resist", 256f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 100f), Stat(S.damage, 10000f),
            Stat("mass", 180f), Stat(S.health, 100000f), Stat(S.speed, 40f),
            Stat("area_of_effect", 5f), Stat("targets", 16f), Stat("accuracy", 80f),
            Stat("multiplier_speed", 1.5f), Stat("stamina", 500f), Stat("range", 8f),
            Stat(S.attack_speed, 8f), Stat("scale", 0.1f),
            Stat("multiplier_health", 3f), Stat("multiplier_damage", 3f)));
        traits.Add(T(XjShiFaXiang, "XjShiRealm", "trait/XjRealm25", "ShiRealm", 3,
            Stat("Resist", 512f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 200f), Stat(S.damage, 100000f),
            Stat("mass", 360f), Stat(S.health, 1000000f), Stat(S.speed, 80f),
            Stat("area_of_effect", 30f), Stat("targets", 96f), Stat("accuracy", 160f),
            Stat("multiplier_speed", 2f), Stat("stamina", 1000f), Stat("range", 48f),
            Stat(S.attack_speed, 10f), Stat("scale", 0.1f),
            Stat("multiplier_health", 5f), Stat("multiplier_damage", 5f)));
        // 世尊在战斗位格上等同道胎，寿元仍由 XjShiPowerRules 动态结算，
        // 因而这里只复用道胎战斗档案，不向可见特质写入固定寿命。
        traits.Add(T(XjShiShiZun, "XjShiRealm", "trait/XjRealm26", "ShiRealm", 3,
            BuildDaoTaiTierStats(fuQi: false)));
        traits.Add(T(XjGuShiDaoTrait, "XjShiDao", "trait/XjGuShi", "ShiTradition", NoRarity));
        traits.Add(T(XjJinShiDaoTrait, "XjShiDao", "trait/XjJinShi", "ShiTradition", NoRarity));
        // 金性妖邪不可逆阴司死籍是内部持久化 tag，不注册为 ActorTrait。
        // show_in_meta_editor=false 只能隐藏编辑器入口，无法阻止已持有特质出现在人物面板；
        // 因此死籍只使用 ActorData + saved_traits 原始标记，绝不进入可见 trait 集合。

        traits.Add(T("XjZz1", "XjZz", "trait/XjZz1", "资质", NoRarity));
        traits.Add(T("XjZz2", "XjZz", "trait/XjZz2", "资质", NoRarity));
        traits.Add(T("XjZz3", "XjZz", "trait/XjZz3", "资质", NoRarity));
        traits.Add(T("XjZz4", "XjZz", "trait/XjZz4", "资质", 2));
        traits.Add(T("XjZz5", "XjZz", "trait/XjZz5", "资质", 2));
        traits.Add(T("XjZz6", "XjZz", "trait/XjZz6", "资质", 3));
        traits.Add(T("XjZz7", "XjZz", "trait/XjZz7", "资质增益", 1));
        traits.Add(T("XjZz8", "XjZz", "trait/XjZz8", "资质伤损", NoRarity));
        traits.Add(T("XjZz9", "XjZz", "trait/XjZz9", "资质伤损", NoRarity));

        traits.Add(T("YinYang1", "YinYang", "trait/YinYang1", "DaoTu", 3));
        traits.Add(T("YinYang2", "YinYang", "trait/YinYang2", "DaoTu", 3));
        traits.Add(T("YinYang3", "YinYang", "trait/YinYang3", "DaoTu", 2));
        traits.Add(T("YinYang4", "YinYang", "trait/YinYang4", "DaoTu", 2));
        traits.Add(T("YinYang5", "YinYang", "trait/YinYang5", "DaoTu", 2));
        traits.Add(T("YinYang6", "YinYang", "trait/YinYang6", "DaoTu", 2));

        AddSeries(traits, "SanLei", "SanLei", 1, 3, "DaoTu", 2);
        AddSeries(traits, "JinDe", "JinDe", 1, 5, "DaoTu", 2);
        AddSeries(traits, "MuDe", "MuDe", 1, 5, "DaoTu", 2);
        AddSeries(traits, "ShuiDe", "ShuiDe", 1, 5, "DaoTu", 2);
        AddSeries(traits, "HuoDe", "HuoDe", 1, 5, "DaoTu", 2);
        AddSeries(traits, "TuDe", "TuDe", 1, 5, "DaoTu", 2);
        // 九条并古道途使用固定的一对一资源入口；BingGu1至BingGu9均随包提供，
        // 不借用五德、十二炁或百艺图标，也不保留旧 QingXuan 入口。
        traits.Add(T("XjXiaoKuiDaoTu", "XjBingGu", "trait/BingGu1", "DaoTu", 2));
        traits.Add(T("XjShangWuDaoTu", "XjBingGu", "trait/BingGu2", "DaoTu", 2));
        traits.Add(T("XjYuZhenDaoTu", "XjBingGu", "trait/BingGu3", "DaoTu", 2));
        traits.Add(T("XjHengZhuDaoTu", "XjBingGu", "trait/BingGu4", "DaoTu", 2));
        traits.Add(T("XjQingXuanDaoTu", "XjBingGu", "trait/BingGu5", "DaoTu", 2));
        traits.Add(T("XjQuanDanDaoTu", "XjBingGu", "trait/BingGu6", "DaoTu", 2));
        traits.Add(T("XjZhiBoDaoTu", "XjBingGu", "trait/BingGu7", "DaoTu", 2));
        traits.Add(T("XjSiTianDaoTu", "XjBingGu", "trait/BingGu8", "DaoTu", 2));
        traits.Add(T("XjDuWeiDaoTu", "XjBingGu", "trait/BingGu9", "DaoTu", 2));
        AddSeries(traits, "ShiErQi", "ShiErQi", 1, 12, "DaoTu", 2);

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
        // 【世尊之姿】是释修专属的高境天命，不进入 ChuShenSpecial 单选投影，
        // 否则释修人物页同步会把它和道胎之姿/其它特殊出身互相删除。
        // 无自然出生/继承率；全世界最多两名在世持有者，由统一 trait 写边界限额。
        traits.Add(T(XjShiWorldHonoredPosture, "XjShiFoundation", "trait/XjRealm26", "ShiFoundation", 3,
            Stat("multiplier_health", 0.3f), Stat("multiplier_damage", 0.3f), Stat("lifespan", 100f)));
        traits.Add(T("XjJinXingReincarnation", "ChuShenSpecial", "trait/JinDanJinXing", "出身", 3,
            Stat("multiplier_health", 0.25f), Stat("multiplier_damage", 0.25f), Stat("lifespan", 50f)));

        traits.Add(T("XjZiFuDescendant", "XjHighRealmDescendant", "trait/XjZiFuDescendant", "高境后裔", 2,
            Stat("lifespan", 10f)));
        traits.Add(T("XjJinDanDescendant", "XjHighRealmDescendant", "trait/XjJinDanDescendant", "高境后裔", 3,
            Stat("lifespan", 20f)));
        traits.Add(T("XjDaoTaiDescendant", "XjHighRealmDescendant", "trait/XjDaoTaiDescendant", "高境后裔", 3,
            Stat("lifespan", 30f), Stat(S.damage, 80f)));
        traits.Add(T("XjZiFuFamily", "XjHighRealmDescendant", "trait/XjZiFuFamily", "高境家族", 1));
        traits.Add(T("XjJinDanFamily", "XjHighRealmDescendant", "trait/XjJinDanFamily", "高境家族", 2));
        traits.Add(T("XjDaoTaiFamily", "XjHighRealmDescendant", "trait/XjDaoTaiFamily", "高境家族", 3,
            Stat("lifespan", 10f), Stat(S.damage, 30f)));

		traits.Add(T(XjCraftTraitRules.AlchemyTraitId, "XjCraft", "trait/LianDanShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.ArtifactRefiningTraitId, "XjCraft", "trait/LianQiShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.TalismanTraitId, "XjCraft", "trait/FuLuShi", "玄鉴百艺", 2));
		traits.Add(T(XjCraftTraitRules.FormationTraitId, "XjCraft", "trait/ZhenFaShi", "玄鉴百艺", 2));

        traits.Add(T(XjLongGengDaoTongTrait, "XjIndependentDao", "trait/ChangGeng", "IndependentDao", 3));
        traits.Add(T(XjYuanZhaoDaoTongTrait, "XjIndependentDao", XjYuanZhaoTraitIconPath, "IndependentDao", 3));
        traits.Add(T(XjHongXiaDaoTongTrait, "XjIndependentDao", XjHongXiaTraitIconPath, "IndependentDao", 3));
        // 落霞山不是尘世宗门：此特质只投影师承/势力身份，不给属性，不参与宗门人口或继承。
        traits.Add(T(XjLuoXiaShanTrait, "XuanJianTraits", XjLuoXiaShanTraitIconPath, "LuoXiaLineage", 3));

		traits.Add(T("XjYiDuiYing", "XuanJianTraits", "effects/ShenTong/TaiYin/YiDuiYing", "XuanJianTrait", 3));
		traits.Add(T("XjYaoShuGreatSage", "XuanJianTraits", "trait/XjYaoShuGreatSage", "XuanJianTrait", 3));
		// 妖民仍是原生单位；此印只开放修炼，并以原生寿元为基准增至四倍。
		traits.Add(T("XjYaoShuYaoMin", "XuanJianTraits", "trait/XjYaoShuYaoMin", "XuanJianTrait", 1,
			Stat("multiplier_lifespan", 3f)));
		traits.Add(T("XjYaoShuHalfBlood", "XuanJianTraits", "trait/XjYaoShuHalfBlood", "XuanJianTrait", 2,
			Stat("multiplier_lifespan", 0.5f)));
        traits.Add(T("XjJieLinXian", "XuanJianTraits", "effects/ShenTong/TaiYin/JieLinZhang", "XuanJianTrait", 3,
            Stat("multiplier_health", 0.15f), Stat("multiplier_damage", 0.15f)));
        traits.Add(T("XjYuYiXian", "XuanJianTraits", "effects/ShenTong/TaiYang/YuYiWen", "XuanJianTrait", 3,
            Stat("multiplier_health", 0.15f), Stat("multiplier_damage", 0.15f)));
        // 金地所有权的可见投影。无属性，只由释修金地权威状态派生。
        traits.Add(T("ZhanTanLin", "XuanJianTraits", "trait/ZhanTanLin", "XuanJianTrait", 3));

        // DebugTraits 分组 - 陆江仙模拟器
        traits.Add(T("DebugJinDanReincarnation", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanGuoWeiYiXiang", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanQuanBing", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugJinDanGuoWei", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugGongFa", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugClearZaQi", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugMingShu", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugHuiGuang", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugSwordIntent", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugFaBaoDengXian", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
        traits.Add(T("DebugLuoXiaInquiry", "DebugTraits", "trait/LuJiangXian", "Debug", 3));
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

    private static XjVNextTraitStat[] BuildZhenRenTierStats(bool fuQi)
    {
        if (!fuQi)
        {
            return new[]
            {
                Stat("lifespan", 400f), Stat("Resist", 256f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 100f), Stat(S.damage, 10000f),
                Stat("mass", 180f), Stat(S.health, 100000f), Stat(S.speed, 40f), Stat("area_of_effect", 5f),
                Stat("targets", 16f), Stat("accuracy", 80f), Stat("multiplier_speed", 1.5f), Stat("stamina", 500f),
                Stat("range", 8f), Stat(S.attack_speed, 8f), Stat("scale", 0.1f),
                Stat("multiplier_health", 3f), Stat("multiplier_damage", 3f)
            };
        }
        return new[]
        {
            Stat("lifespan", 900f), Stat("Resist", 230f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 90f), Stat(S.damage, 10000f),
            Stat("mass", 162f), Stat(S.health, 90000f), Stat(S.speed, 36f), Stat("area_of_effect", 4.5f),
            Stat("targets", 14f), Stat("accuracy", 72f), Stat("multiplier_speed", 1.35f), Stat("stamina", 450f),
            Stat("range", 7.2f), Stat(S.attack_speed, 7.2f), Stat("scale", 0.09f),
            Stat("multiplier_health", 2.7f), Stat("multiplier_damage", 2.7f)
        };
    }

    private static XjVNextTraitStat[] BuildJinDanTierStats(bool fuQi)
    {
        _ = fuQi; // route-specific lifespan/stage modifiers are applied outside the shared combat tier.
        return new[]
        {
            Stat("Resist", 512f), Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f), Stat("warfare", 200f), Stat(S.damage, 100000f),
            Stat("mass", 360f), Stat(S.health, 1000000f), Stat(S.speed, 80f), Stat("area_of_effect", 30f),
            Stat("targets", 96f), Stat("accuracy", 160f), Stat("multiplier_speed", 2f), Stat("stamina", 1000f),
            Stat("range", 48f), Stat(S.attack_speed, 10f), Stat("scale", 0.1f),
            Stat("multiplier_health", 5f), Stat("multiplier_damage", 5f)
        };
    }

    private static XjVNextTraitStat[] BuildDaoTaiTierStats(bool fuQi)
    {
        return fuQi
            ? BuildDaoTaiStats(
                resistMultiplier: 1.875f, warfareMultiplier: 1.6f, damageMultiplier: 4.2f, massMultiplier: 1.3333334f,
                healthMultiplier: 4.5f, speedMultiplier: 1.375f, areaMultiplier: 2.8f, targetMultiplier: 1.375f,
                accuracyMultiplier: 1.5f, speedBonus: 0.8f, staminaMultiplier: 2f, rangeMultiplier: 1.25f,
                attackSpeedMultiplier: 1.3f, scaleMultiplier: 1.2f, healthMultiplierStat: 1.3f, damageMultiplierStat: 1.3f)
            : BuildDaoTaiStats(
                resistMultiplier: 2f, warfareMultiplier: 1.8f, damageMultiplier: 5f, massMultiplier: 1.45f,
                healthMultiplier: 5f, speedMultiplier: 1.25f, areaMultiplier: 3f, targetMultiplier: 1.5f,
                accuracyMultiplier: 1.375f, speedBonus: 0.5f, staminaMultiplier: 1.8f, rangeMultiplier: 1.3333334f,
                attackSpeedMultiplier: 1.2f, scaleMultiplier: 1.2f, healthMultiplierStat: 1.4f, damageMultiplierStat: 1.4f);
    }

    private static XjVNextTraitStat[] BuildDaoTaiStats(
        float resistMultiplier,
        float warfareMultiplier,
        float damageMultiplier,
        float massMultiplier,
        float healthMultiplier,
        float speedMultiplier,
        float areaMultiplier,
        float targetMultiplier,
        float accuracyMultiplier,
        float speedBonus,
        float staminaMultiplier,
        float rangeMultiplier,
        float attackSpeedMultiplier,
        float scaleMultiplier,
        float healthMultiplierStat,
        float damageMultiplierStat)
    {
        return new[]
        {
            Stat("Resist", 512f * resistMultiplier),
            Stat(XjKnockbackGuard.KnockbackResistanceStatId, 1f),
            Stat("warfare", 200f * warfareMultiplier),
            Stat(S.damage, 100000f * damageMultiplier),
            Stat("mass", 360f * massMultiplier),
            Stat(S.health, 1000000f * healthMultiplier),
            Stat(S.speed, 80f * speedMultiplier),
            Stat("area_of_effect", 30f * areaMultiplier),
            Stat("targets", 96f * targetMultiplier),
            Stat("accuracy", 160f * accuracyMultiplier),
            Stat("multiplier_speed", 2f + speedBonus),
            Stat("stamina", 1000f * staminaMultiplier),
            Stat("range", 48f * rangeMultiplier),
            Stat(S.attack_speed, 10f * attackSpeedMultiplier),
            Stat("scale", 0.1f * scaleMultiplier),
            Stat("multiplier_health", 5f * healthMultiplierStat),
            Stat("multiplier_damage", 5f * damageMultiplierStat)
        };
    }

    /// <summary>
    /// NeoModLoader 在部分安装方式下可能先编译新特质、后继续沿用旧版
    /// Locales 缓存，导致编辑器直接暴露底层特质 ID。
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

        // 特质编辑器状态只需在语言/资源管理器重新建立时同步一次。旧版每个
        // 渲染帧都做一次 AssetManager.traits 查找，即使汉化早已完成。
        SyncIndependentDaoEditorState();

        try
        {
            AddRuntimeLocale("trait_DebugSwordIntent", SwordIntentSimulatorName);
            AddRuntimeLocale("trait_DebugSwordIntent_info", SwordIntentSimulatorInfo);
            AddRuntimeLocale("trait_DebugSwordIntent_description", SwordIntentSimulatorInfo);
            AddRuntimeLocale("DebugSwordIntent", SwordIntentSimulatorName);
            AddRuntimeLocale("DebugSwordIntent_description", SwordIntentSimulatorInfo);
            AddRuntimeLocale("trait_DebugFaBaoDengXian", FaBaoDengXianSimulatorName);
            AddRuntimeLocale("trait_DebugFaBaoDengXian_info", FaBaoDengXianSimulatorInfo);
            AddRuntimeLocale("trait_DebugFaBaoDengXian_description", FaBaoDengXianSimulatorInfo);
            AddRuntimeLocale("DebugFaBaoDengXian", FaBaoDengXianSimulatorName);
            AddRuntimeLocale("DebugFaBaoDengXian_description", FaBaoDengXianSimulatorInfo);
            AddRuntimeTraitLocale(
                "DebugLuoXiaInquiry",
                LuoXiaInquirySimulatorName,
                LuoXiaInquirySimulatorInfo,
                "落霞山门人·限三人");
			// 调试特质和纪元公告有调用方按原始 ID 直接取词条；同时登记原始键，
			// 避免 LocalizedTextManager 再把 PascalCase 转成下划线后报 missing text。
			AddRuntimeRawLocale("xuanjian_history_broadcast_cultivation_JiYuanChange", "纪元更易");
            AddRuntimeLocale("trait_group_XjFuQiYangXing", "服气养性道");
            AddRuntimeLocale("trait_group_XjShiFoundation", "释修根基");
            AddRuntimeLocale("trait_group_XjShiRealm", "释修境界");
            AddRuntimeLocale("trait_group_XjShiDao", "释修道途");
            AddRuntimeLocale("trait_group_XuanJianTraits", "玄鉴特质");
            AddRuntimeLocale("trait_group_XjBingGu", "并古道途");
            AddRuntimeLocale("trait_group_XjIndependentDao", "独立道途");
            AddRuntimeLocale("trait_group_XjInternalHidden", "玄鉴秘藏");
            AddRuntimeLocale(XjKnockbackGuard.KnockbackResistanceStatId, "防击退");
            AddRuntimeLocale("statsIcon_" + XjKnockbackGuard.KnockbackResistanceStatId, "防击退");
            AddRuntimeLocale("statDisplay_" + XjKnockbackGuard.KnockbackResistanceStatId, "防击退");
            AddRuntimeTraitLocale(XjShiSeed, "释修种子", "幼年结释缘，心识与诸释法相应，是踏入释修诸法的根由。此印只记其与释法有缘，古释今释、所行法脉与后来境界仍须各自求得。", "幼结释缘");
            AddRuntimeTraitLocale(XjShiWorldHonoredPosture, "世尊之姿", "释门罕世之姿。角色已真实入释后，凡关乎自身修证成败的关隘皆可顺遂而过；金地、三十二天碎片、世数、宏愿、修持、释修命数，以及古今释各自的世尊之位仍须真实具备。此姿当世至多并存二人。", "释门罕世之姿·当世至多二人");
            AddRuntimeTraitLocale(XjShiSengLv, "僧侣", "高命数者可入释持戒，修持只看命数根基，不受修炼资质限制；手动赋予会完整补录释修状态。", "释修初境");
            AddRuntimeTraitLocale(XjShiFaShi, "法师", "正法法师内修法术、明经求缘，修持效率只由命数决定；手动赋予会同步补齐修持与命数。", "释修法师");
            AddRuntimeTraitLocale(XjShiLianMin, "怜愍", "今释依附摩诃而得座，分萨陲、发慧、金莲三位；手动赋予时优先补真实座位，无主则暂记孤位萨陲。", "摩诃座下");
            AddRuntimeTraitLocale(XjShiMoHe, "摩诃", "位、形、念三不退；手动赋予会同步补录修持、命数以及对应应土或金地，转世后仍须重新证回摩诃。", "释修摩诃");
            AddRuntimeTraitLocale(XjShiFaXiang, "法相", "今释必须先掌握一块北世尊金地，以应身碎片稳住位格方可成相，掌地者即为庙主；金地可位于旃檀林内，法相真身仍永镇旃檀林。古释以慧觉天地、自修应身、自证金地成相；此境只能真实证得，禁止手动赋予。", "释修法相");
            AddRuntimeTraitLocale(XjShiShiZun, "世尊", "释修最高境，古释与今释各只有一位；今释须完整驾驭一重三十二天，古释须慧觉天地、功绩圆满而自成三十二重天。此境只能真实证得，禁止手动赋予。", "释修世尊");
            AddRuntimeTraitLocale(XjGuShiDaoTrait, "古释", "古释重渡己、求解脱，循北世尊道；自动诞生仍沿用首批种子与遗经自悟等原有判定。仅特质编辑器手动赋予时，已有其他道途者不能再挂古释。", "古释法脉");
            AddRuntimeTraitLocale(XjJinShiDaoTrait, "今释", "今释重渡人，以应土纳人，循七相法脉；可手动赋予并按完整释修事务转修。", "今释法脉");
            AddRuntimeTraitLocale(XjYuanZhaoDaoTongTrait, "渊照道途", "空证新道，以太阴为照、坎水为渊；以渊纳象，以月留景，见影而索其身，藏真而遗其形。道性主照、渊、潜、返、寂、真。", "空证独立道途");
            AddRuntimeTraitLocale(XjHongXiaDaoTongTrait, "虹霞道途", "霞光一脉别立之道。戊土在地为山、在天为霞，虹霞由此出而又不拘于五德十二炁；立身水火之间，元磁雷霆难动，梭摩血煞难侵，明阳与醒辰亦难尽照。", "落霞山虹霞法脉");
            AddRuntimeTraitLocale(XjLuoXiaShanTrait, "落霞山门人", "落霞山门下所留霞印。此山灵机冠绝，群霞所宗；日月交替时，天下第一缕霞光自山中而出。入门者承虹霞与戊土霞光旧法，重道行、重师承，少以尘世门籍自限。", "群霞所宗，落霞门人");
            AddRuntimeTraitLocale("XjJinXingReincarnation", "金性转世", "服气真人求证失败后，以前世金性护持真灵转生，道途宿慧未泯。", "金性转世");
			AddRuntimeTraitLocale("XjYaoShuGreatSage", "妖属大圣", "受天数垂顾，寄身鳞羽毛角之间；守一道正位，餐炁养形，俟其道胎。", "正位化形·真君羽士");
			AddRuntimeTraitLocale("XjYaoShuYaoMin", "妖属妖民", "蒙大圣一炁点化，横骨初炼，渐知吐纳；虽托兽形，亦可问道。", "横骨初炼·可问仙途");
			AddRuntimeTraitLocale("XjYaoShuHalfBlood", "半妖血脉", "大圣降临，偶有一缕妖炁落入尘寰。此身人妖并生，寿数稍长，根骨自有异禀。", "妖炁入骨·人妖同源");
			// BaseTrait 直接以原始 ID 拼接 locale key，不会调用 Underscore；仅用 add()
			// 会把 XjYaoShu... 改成小写下划线，导致特质编辑器仍报 missing text。
			AddRuntimeRawLocale("trait_XjYaoShuGreatSage", "妖属大圣");
			AddRuntimeRawLocale("trait_XjYaoShuGreatSage_info", "受天数垂顾，寄身鳞羽毛角之间；守一道正位，餐炁养形，俟其道胎。");
			AddRuntimeRawLocale("trait_XjYaoShuGreatSage_info_2", "正位化形·真君羽士");
			AddRuntimeRawLocale("trait_XjYaoShuYaoMin", "妖属妖民");
			AddRuntimeRawLocale("trait_XjYaoShuYaoMin_info", "蒙大圣一炁点化，横骨初炼，渐知吐纳；虽托兽形，亦可问道。");
			AddRuntimeRawLocale("trait_XjYaoShuYaoMin_info_2", "横骨初炼·可问仙途");
			AddRuntimeRawLocale("trait_XjYaoShuHalfBlood", "半妖血脉");
			AddRuntimeRawLocale("trait_XjYaoShuHalfBlood_info", "大圣降临，偶有一缕妖炁落入尘寰。此身人妖并生，寿数稍长，根骨自有异禀。");
			AddRuntimeRawLocale("trait_XjYaoShuHalfBlood_info_2", "妖炁入骨·人妖同源");
			AddRuntimeLocale("xj_yao_shu_great_sage", "妖属大圣");
			AddRuntimeLocale("xj_yao_shu_great_sage_info", "受天数垂顾，寄身鳞羽毛角之间；守一道正位，餐炁养形，俟其道胎。");
			AddRuntimeLocale("xj_yao_shu_great_sage_info_2", "正位化形·真君羽士");
			AddRuntimeLocale("xj_yao_shu_yao_min", "妖属妖民");
			AddRuntimeLocale("xj_yao_shu_yao_min_info", "蒙大圣一炁点化，横骨初炼，渐知吐纳；虽托兽形，亦可问道。");
			AddRuntimeLocale("xj_yao_shu_yao_min_info_2", "横骨初炼·可问仙途");
			AddRuntimeLocale("xj_yao_shu_half_blood", "半妖血脉");
			AddRuntimeLocale("xj_yao_shu_half_blood_info", "大圣降临，偶有一缕妖炁落入尘寰。此身人妖并生，寿数稍长，根骨自有异禀。");
			AddRuntimeLocale("xj_yao_shu_half_blood_info_2", "妖炁入骨·人妖同源");
            AddRuntimeLocale("trait_XjLongGengDaoTong", "长庚道途");
            AddRuntimeTraitLocale("XjJieLinXian", "结璘仙", "太阴果位在世时，太阴修士求证失利者偶可受月华结璘，初成不占位序；积修成熟后可证入余位，仙基另有所指者亦可证闰位。", "结璘仙");
            AddRuntimeTraitLocale("XjYuYiXian", "郁仪仙", "太阳果位在世时，太阳紫府求金失利者偶可受日精郁仪，初成不占位序；积修成熟后可证入余位，仙基另有所指者亦可证闰位。", "郁仪仙");
            AddRuntimeTraitLocale("ZhanTanLin", "金地", "释修真实持有一方金地时显出的庙主身份印记；金地易主或失去后，此印随之消散。", "释修金地");
            AddRuntimeTraitLocale(XjRealmIds.DaoTai, "道胎境", "道胎既成，诸果归身，万象由此结胎。", "真元需求:1000000");
            AddRuntimeTraitLocale(XjRealmIds.FuQiDaoTai, "道胎", "服气养性，羽化道胎，形神俱妙。", "求道胎");
            _runtimeLocalizationRegistered = true;
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][特质汉化] 运行期汉化补注册失败 异常=" + ex.GetType().Name);
        }
    }

    internal static void ResetRuntimeLocalization()
    {
        _runtimeLocalizationManager = null;
        _runtimeLocalizationRegistered = false;
    }

    /// <summary>
    /// 独立道途只保留长庚入口。原生编辑器会缓存特质列表，因此该特质
    /// 永久禁止手动赋予，但仍挂在独立道途分组下供栏目名本地化。
    /// </summary>
    internal static void SyncIndependentDaoEditorState()
    {
        // 原生编辑器会缓存特质列表，运行中互换显隐会使本应由逻辑写入的
        // 独立道途特质泄露为手动赋予入口。
        SetIndependentDaoTraitEditorState(XjLongGengDaoTongTrait, XjIndependentDaoGroup);
        SetIndependentDaoTraitEditorState(XjYuanZhaoDaoTongTrait, XjIndependentDaoGroup);
        SetIndependentDaoTraitEditorState(XjHongXiaDaoTongTrait, XjIndependentDaoGroup);
        _independentDaoEditorEstablished = XjFuQiSwordWorldState.IsEstablished;
    }

    private static void SetIndependentDaoTraitEditorState(string traitId, string groupId)
    {
        ActorTrait trait = null;
        try
        {
            trait = AssetManager.traits?.get(traitId);
        }
        catch (System.Exception xjCaught368) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Traits/XjVNextAssetRegistration.cs:368", xjCaught368); }
        if (trait == null) return;

        trait.group_id = string.IsNullOrWhiteSpace(groupId) ? XjInternalHiddenGroup : groupId;
        trait.show_in_meta_editor = false;
        trait.can_be_given = false;
    }

    private static void AddRuntimeLocale(string key, string value)
    {
        if (!string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(value))
        {
            LocalizedTextManager.add(key, value, false, string.Empty, true);
        }
    }

    private static void AddRuntimeTraitLocale(string traitId, string name, string info, string shortInfo)
    {
        AddRuntimeLocale("trait_" + traitId, name);
        AddRuntimeLocale("trait_" + traitId + "_info", info);
        AddRuntimeLocale("trait_" + traitId + "_info_2", shortInfo);
        AddRuntimeLocale("trait_" + traitId + "_description", info);
        AddRuntimeLocale(traitId, name);
        AddRuntimeLocale(traitId + "_description", info);
    }

    private static void AddRuntimeRawLocale(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value)) return;
        LocalizedTextManager manager = LocalizedTextManager.instance;
        if (manager?._localized_text == null || manager._localized_text_files == null) return;
        manager._localized_text[key] = value;
        manager._localized_text_files[key] = "xuanjian.runtime";
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
        // WorldBox 0.51.2 的 strings.S 不公开 knockback_reduction。玄鉴使用自己的隐藏
        // 0~1 减免值，并只在 Actor.addForce 入口消费，避免依赖不存在的原生常量。
        TryAddBaseStat(XjKnockbackGuard.KnockbackResistanceStatId, 0f, 1f, true);
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
            Debug.LogWarning("[玄鉴][资源注册] base stat 注册跳过 编号=" + id + " 异常=" + ex.GetType().Name);
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
            Debug.LogWarning("[玄鉴][资源注册] trait group 注册跳过 编号=" + id + " 异常=" + ex.GetType().Name);
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
        catch (System.Exception xjCaught495) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Traits/XjVNextAssetRegistration.cs:495", xjCaught495); }

        bool isNew = trait == null;
        if (trait == null)
        {
            trait = new ActorTrait { id = item.Id };
        }

        try
        {
            trait.group_id = string.IsNullOrWhiteSpace(item.GroupId) ? "miscellaneous" : item.GroupId;
            trait.path_icon = string.IsNullOrWhiteSpace(item.IconPath) ? DefaultIcon : item.IconPath;
			ConfigureYaoTraitLocalization(trait, item.Id);
            trait.needs_to_be_explored = false;
            trait.rate_birth = 0;
            trait.rate_inherit = 0;
            trait.rate_acquire_grow_up = 0;
			bool blockManualGrant = IsManualGrantBlocked(item);
			trait.can_be_given = !blockManualGrant;
			trait.show_in_meta_editor = !blockManualGrant;
            if (item.Rarity >= 0)
            {
                ((BaseTrait<ActorTrait>)(object)trait).rarity = (Rarity)item.Rarity;
            }

            ApplyStats(trait, item.Stats);

            if (string.Equals(item.Id, XjRealmIds.ZiFu, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.JinDan, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.DaoTai, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
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
                || string.Equals(item.Id, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.DaoTai, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
                || string.Equals(item.Id, XjRealmIds.ShenDan, StringComparison.Ordinal))
            {
                // 真君级角色通过原生攻击事件触发道法，避免仅 RealmId 正确但
                // 可见特质不同步时完全失去施法入口。
                if (!XjJinDanDaoSpellRuntime.BindCombatTrigger(trait))
                {
                    Debug.LogWarning("[玄鉴][真君法术] 战斗回调绑定失败 trait=" + item.Id);
                }
            }

            if (isNew)
            {
                AssetManager.traits.add(trait);
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[玄鉴][资源注册] trait 注册跳过 编号=" + item.Id + " 异常=" + ex.GetType().Name);
		}
	}

	private static void ConfigureYaoTraitLocalization(ActorTrait trait, string traitId)
	{
		if (trait == null) return;
		if (string.Equals(traitId, "XjYaoShuGreatSage", StringComparison.Ordinal))
		{
			trait.special_locale_id = "xj_yao_shu_great_sage";
			trait.special_locale_description = "xj_yao_shu_great_sage_info";
			trait.special_locale_description_2 = "xj_yao_shu_great_sage_info_2";
		}
		else if (string.Equals(traitId, "XjYaoShuYaoMin", StringComparison.Ordinal))
		{
			trait.special_locale_id = "xj_yao_shu_yao_min";
			trait.special_locale_description = "xj_yao_shu_yao_min_info";
			trait.special_locale_description_2 = "xj_yao_shu_yao_min_info_2";
		}
		else if (string.Equals(traitId, "XjYaoShuHalfBlood", StringComparison.Ordinal))
		{
			trait.special_locale_id = "xj_yao_shu_half_blood";
			trait.special_locale_description = "xj_yao_shu_half_blood_info";
			trait.special_locale_description_2 = "xj_yao_shu_half_blood_info_2";
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
