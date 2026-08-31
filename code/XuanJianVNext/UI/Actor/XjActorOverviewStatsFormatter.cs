using System;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.ActorInfo;

/// <summary>
/// UnitWindow 概览中的玄鉴属性桥。
///
/// 结构策略：
/// - 核心三值只在原生 i_kills 所在最后一行静态插入一次：先天命数、道慧、i_kills、真元；
/// - 不修改原生 Horizontal/Grid Layout 参数，不持续 SetSiblingIndex，不强制重建布局；
/// - 战斗派生值继续使用玄鉴自有独立行，避免挤占原生概览；
/// - 数值绑定遵循成熟模组的“结构一次、窗口启用后重新绑定数据”模式。UnitWindow 每次重新启用后
///   由 XjActorOverviewStatsRebindDriver 延迟一帧执行 setIconValue，避免原生后续初始化把数值吃掉。
/// </summary>
internal static class XjActorOverviewStatsFormatter
{
    private const string LegacyCoreRowName = "XjOverviewCoreStatsRow";
    private const string CombatRow1Name = "XjOverviewCombatStatsRow1";
    private const string CombatRow2Name = "XjOverviewCombatStatsRow2";
    private const string CombatRow3Name = "XjOverviewCombatStatsRow3";

    private static readonly OverviewStatIcon MingShuIcon =
        new OverviewStatIcon("MingShu", "先天命数", "MingShu", "MingShu.png", "XueQi", "XueQi.png");
    private static readonly OverviewStatIcon HuiGuangIcon =
        new OverviewStatIcon("HuiGuang", "道慧", "HuiGuang", "HuiGuang.png");
    private static readonly OverviewStatIcon ZhenYuanIcon =
        new OverviewStatIcon("ZhenYuan", "真元", "ZhenYuan", "ZhenYuan.png", "ZhenQi", "ZhenQi.png");

    private static readonly OverviewStatIcon[] CombatIcons1 =
    {
        new OverviewStatIcon("XjArmorPen", "减穿", true),
        new OverviewStatIcon("XjTrueDamage", "真伤", true),
        new OverviewStatIcon("XjAccuracy", "命中", true),
        new OverviewStatIcon("XjCrit", "暴击", true),
        new OverviewStatIcon("XjAttackSpeed", "攻速", true)
    };

    private static readonly OverviewStatIcon[] CombatIcons2 =
    {
        new OverviewStatIcon("XjSameRealmDamage", "同境", true),
        new OverviewStatIcon("XjShieldBreak", "破盾", true),
        new OverviewStatIcon("XjLifesteal", "吸血", true),
        new OverviewStatIcon("XjDamageReduction", "减伤", true),
        new OverviewStatIcon("XjHealthShield", "护盾", true)
    };

    private static readonly OverviewStatIcon[] CombatIcons3 =
    {
        new OverviewStatIcon("XjDodge", "闪避", true),
        new OverviewStatIcon("XjCritTakenReduction", "抗暴", true),
        new OverviewStatIcon("XjHealback", "回血", true),
        new OverviewStatIcon("XjBreakthrough", "破境", true)
    };

    private static readonly StableRowDefinition[] CombatRows =
    {
        new StableRowDefinition(CombatRow1Name, CombatIcons1),
        new StableRowDefinition(CombatRow2Name, CombatIcons2),
        new StableRowDefinition(CombatRow3Name, CombatIcons3)
    };

    internal static bool HasStableSurface(UnitWindow window)
    {
        Transform content = GetStatsContent(window);
        Transform nativeKillsRow = FindNativeKillsRow(content);
        if (nativeKillsRow == null)
        {
            return false;
        }

        Transform kills = nativeKillsRow.Find("i_kills");
        Transform mingShu = nativeKillsRow.Find(MingShuIcon.Id);
        Transform huiGuang = nativeKillsRow.Find(HuiGuangIcon.Id);
        Transform zhenYuan = nativeKillsRow.Find(ZhenYuanIcon.Id);
        if (!IsHealthyCoreIcon(mingShu) || !IsHealthyCoreIcon(huiGuang) || !IsHealthyCoreIcon(zhenYuan) || kills == null)
        {
            return false;
        }

        return mingShu.GetSiblingIndex() < huiGuang.GetSiblingIndex()
            && huiGuang.GetSiblingIndex() < kills.GetSiblingIndex()
            && kills.GetSiblingIndex() < zhenYuan.GetSiblingIndex();
    }

    /// <summary>
    /// showInfo 只负责确保结构存在并提交一次“窗口稳定后绑定”。
    /// forceRefresh 用于玩家主动改动（GodTools 等），可立即绑定一次，同时仍保留下一帧 settle bind。
    /// </summary>
    internal static void Refresh(UnitWindow window, bool forceRefresh = false)
    {
        if (!IsAliveWindowActor(window))
        {
            return;
        }

        if (!EnsureStableSurface(window))
        {
            return;
        }

        if (forceRefresh)
        {
            BindValuesNow(window);
        }

        EnsureRebindDriver(window)?.Schedule(window);
    }

    /// <summary>
    /// 由窗口级一次性 rebind driver 调用。这里允许同值再次 setIconValue：
    /// “数据没变”不能推导“原生 StatsIcon 仍持有上次显示状态”。
    /// </summary>
    internal static void BindValuesNow(UnitWindow window)
    {
        if (!IsAliveWindowActor(window) || !EnsureStableSurface(window))
        {
            return;
        }

        Actor actor = window.actor;
        XjActorCultivationSnapshot cultivation = XjActorCultivationSnapshotBuilder.Build(actor);

        float congenitalMingShu = 0f;
        if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out congenitalMingShu)
            || congenitalMingShu < 0f)
        {
            congenitalMingShu = Math.Min(100f, Math.Max(0f, cultivation.MingShu));
        }

        SetCoreIconValue(window, MingShuIcon.Id, ToNonNegativeInteger(congenitalMingShu));
        SetCoreIconValue(window, HuiGuangIcon.Id, ToNonNegativeInteger(cultivation.HuiGuang));
        SetCoreIconValue(window, ZhenYuanIcon.Id, ToNonNegativeInteger(cultivation.ZhenYuan));

        XjFaBaoBonusProfile profile = BuildOverviewBonusProfile(actor);
        ApplyPercentRow(window, CombatRow1Name, CombatIcons1, new[]
        {
            profile.ArmorPenetration,
            profile.TrueDamageRatio,
            profile.AccuracyBonus,
            profile.CritBonus,
            profile.AttackSpeedBonus
        });
        ApplyPercentRow(window, CombatRow2Name, CombatIcons2, new[]
        {
            profile.SameRealmDamageBonus,
            profile.ShieldBreakBonus,
            profile.Lifesteal,
            profile.DamageReduction,
            profile.HealthShield
        });
        ApplyPercentRow(window, CombatRow3Name, CombatIcons3, new[]
        {
            profile.DodgeBonus,
            profile.CritTakenReduction,
            profile.HealbackBonus,
            profile.BreakthroughChanceBonus
        });
    }

    private static bool IsAliveWindowActor(UnitWindow window)
    {
        return window?.actor?.data != null && ((NanoObject)window.actor).isAlive();
    }

    private static bool EnsureStableSurface(UnitWindow window)
    {
        Transform content = GetStatsContent(window);
        if (content == null)
        {
            return false;
        }

        if (content.GetComponent<StatsIconContainer>() == null)
        {
            content.gameObject.AddComponent<StatsIconContainer>();
        }

        RemoveLegacyDedicatedCoreRow(content);

        Transform nativeKillsRow = FindNativeKillsRow(content);
        Transform kills = nativeKillsRow?.Find("i_kills");
        if (nativeKillsRow == null || kills == null)
        {
            return false;
        }

        RemoveLegacyInlineCombatIcons(nativeKillsRow);
        if (!EnsureCoreIconsInline(nativeKillsRow, kills))
        {
            return false;
        }

        int insertionIndex = nativeKillsRow.GetSiblingIndex() + 1;
        for (int i = 0; i < CombatRows.Length; i++)
        {
            Transform row = content.Find(CombatRows[i].Name);
            if (row != null)
            {
                // 0.9.9.12~0.9.9.16 已经写进存量 UnitWindow 的战斗行也可能继承
                // 原生 UnitStatsElement。打开新包后第一次刷新时就地清掉，不要求窗口重建。
                StripNativeMetaLifecycleRecursive(row);
                continue;
            }

            row = CreateStableRow(content, nativeKillsRow, CombatRows[i], insertionIndex + i);
            if (row == null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool EnsureCoreIconsInline(Transform nativeKillsRow, Transform kills)
    {
        if (nativeKillsRow == null || kills == null)
        {
            return false;
        }

        Transform mingShu = FindOrCreateCoreIcon(nativeKillsRow, kills, MingShuIcon);
        Transform huiGuang = FindOrCreateCoreIcon(nativeKillsRow, kills, HuiGuangIcon);
        Transform zhenYuan = FindOrCreateCoreIcon(nativeKillsRow, kills, ZhenYuanIcon);
        StripNativeMetaLifecycle(mingShu);
        StripNativeMetaLifecycle(huiGuang);
        StripNativeMetaLifecycle(zhenYuan);
        if (mingShu == null || huiGuang == null || zhenYuan == null)
        {
            return false;
        }

        // 核心三值永远显示；仅在结构缺失/顺序损坏时修复一次。普通数值刷新绝不重新排序。
        mingShu.gameObject.SetActive(true);
        huiGuang.gameObject.SetActive(true);
        zhenYuan.gameObject.SetActive(true);

        if (!(mingShu.GetSiblingIndex() < huiGuang.GetSiblingIndex()
            && huiGuang.GetSiblingIndex() < kills.GetSiblingIndex()
            && kills.GetSiblingIndex() < zhenYuan.GetSiblingIndex()))
        {
            // 只围绕 i_kills 做相对插入，不改任何原生 LayoutGroup/RectTransform 参数，
            // 也不改变其他原生兄弟之间的相对顺序。
            mingShu.SetSiblingIndex(kills.GetSiblingIndex());
            huiGuang.SetSiblingIndex(kills.GetSiblingIndex());
            zhenYuan.SetSiblingIndex(Mathf.Min(nativeKillsRow.childCount - 1, kills.GetSiblingIndex() + 1));
        }

        return true;
    }

    private static Transform FindOrCreateCoreIcon(Transform row, Transform template, OverviewStatIcon icon)
    {
        Transform existing = row.Find(icon.Id);
        if (existing != null)
        {
            return existing;
        }

        Transform created = CloneStatsIconWithoutNativeMeta(template, row);
        if (created == null) return null;
        ConfigureIcon(created, icon);
        created.gameObject.SetActive(true);
        return created;
    }

    private static void RemoveLegacyDedicatedCoreRow(Transform content)
    {
        Transform legacy = content?.Find(LegacyCoreRowName);
        if (legacy != null)
        {
            UnityEngine.Object.DestroyImmediate(legacy.gameObject);
        }
    }

    private static void RemoveLegacyInlineCombatIcons(Transform nativeKillsRow)
    {
        if (nativeKillsRow == null)
        {
            return;
        }

        for (int r = 0; r < CombatRows.Length; r++)
        {
            OverviewStatIcon[] icons = CombatRows[r].Icons;
            for (int i = 0; i < icons.Length; i++)
            {
                Transform legacy = nativeKillsRow.Find(icons[i].Id);
                if (legacy != null)
                {
                    UnityEngine.Object.DestroyImmediate(legacy.gameObject);
                }
            }
        }
    }

    private static Transform CreateStableRow(
        Transform content,
        Transform nativeTemplateRow,
        in StableRowDefinition definition,
        int siblingIndex)
    {
        Transform row = CloneRowWithoutNativeMeta(nativeTemplateRow, content);
        if (row == null) return null;
        row.name = definition.Name;
        row.gameObject.SetActive(false);
        row.localScale = Vector3.one;
        row.SetSiblingIndex(Mathf.Clamp(siblingIndex, 0, content.childCount - 1));

        Transform iconTemplate = row.Find("i_kills");
        if (iconTemplate == null)
        {
            UnityEngine.Object.Destroy(row.gameObject);
            return null;
        }

        for (int i = row.childCount - 1; i >= 0; i--)
        {
            Transform child = row.GetChild(i);
            if (child != null && child != iconTemplate)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }

        for (int i = 0; i < definition.Icons.Length; i++)
        {
            Transform iconTransform = CloneStatsIconWithoutNativeMeta(iconTemplate, row);
            if (iconTransform == null) continue;
            ConfigureIcon(iconTransform, definition.Icons[i]);
        }

        UnityEngine.Object.DestroyImmediate(iconTemplate.gameObject);
        return row;
    }

    /// <summary>
    /// 原生 i_kills 属于 UnitStatsElement/WindowMetaElementBase 的生命周期树。直接复制它会把
    /// 未初始化的 UnitStatsElement 一并带入玄鉴图标，并在人物页 OnEnable 时进入原生
    /// showContent 协程，最终于 UnitStatsElement.cs 解引用空元数据。自定义概览只需要
    /// StatsIcon/TipButton，因此所有克隆必须先在 inactive staging 下剥离原生 meta 生命周期，
    /// 再挂回可见层级；不 patch UnitWindow，不逐帧修 UI。
    /// </summary>
    private static Transform CloneStatsIconWithoutNativeMeta(Transform template, Transform parent)
    {
        if (template == null || parent == null) return null;
        GameObject staging = new GameObject("XjStatsCloneStaging");
        staging.SetActive(false);
        Transform created = null;
        try
        {
            created = UnityEngine.Object.Instantiate(template, staging.transform);
            created.gameObject.SetActive(false);
            StripNativeMetaLifecycle(created);
            created.SetParent(parent, false);
            return created;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(staging);
        }
    }

    private static Transform CloneRowWithoutNativeMeta(Transform template, Transform parent)
    {
        if (template == null || parent == null) return null;
        GameObject staging = new GameObject("XjStatsRowCloneStaging");
        staging.SetActive(false);
        Transform created = null;
        try
        {
            created = UnityEngine.Object.Instantiate(template, staging.transform);
            created.gameObject.SetActive(false);
            StripNativeMetaLifecycleRecursive(created);
            created.SetParent(parent, false);
            return created;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(staging);
        }
    }

    private static void StripNativeMetaLifecycleRecursive(Transform root)
    {
        if (root == null) return;
        StripNativeMetaLifecycle(root);
        for (int i = 0; i < root.childCount; i++)
        {
            StripNativeMetaLifecycleRecursive(root.GetChild(i));
        }
    }

    private static void StripNativeMetaLifecycle(Transform target)
    {
        if (target == null) return;
        // UnitStatsElement derives from the native window-meta lifecycle base; removing the concrete
        // component is sufficient and leaves StatsIcon/TipButton intact. GetComponent(string) keeps
        // this bridge tolerant of minor native namespace changes between WorldBox builds.
        Component nativeMeta = target.GetComponent("UnitStatsElement");
        if (nativeMeta != null)
        {
            UnityEngine.Object.DestroyImmediate(nativeMeta);
        }
    }

    private static Transform GetStatsContent(UnitWindow window)
    {
        return window == null
            ? null
            : ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
    }

    private static Transform FindNativeKillsRow(Transform content)
    {
        if (content == null)
        {
            return null;
        }

        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (child == null || IsXuanJianCombatRow(child.name) || string.Equals(child.name, LegacyCoreRowName, StringComparison.Ordinal))
            {
                continue;
            }

            if (child.Find("i_kills") != null)
            {
                return child;
            }
        }

        return null;
    }

    private static bool IsXuanJianCombatRow(string rowName)
    {
        return string.Equals(rowName, CombatRow1Name, StringComparison.Ordinal)
            || string.Equals(rowName, CombatRow2Name, StringComparison.Ordinal)
            || string.Equals(rowName, CombatRow3Name, StringComparison.Ordinal);
    }

    private static bool IsHealthyCoreIcon(Transform icon)
    {
        return icon != null
            && icon.gameObject.activeSelf
            && icon.GetComponent<StatsIcon>() != null
            && icon.GetComponent("UnitStatsElement") == null;
    }

    private static XjActorOverviewStatsRebindDriver EnsureRebindDriver(UnitWindow window)
    {
        if (window == null)
        {
            return null;
        }

        Component host = (Component)window;
        XjActorOverviewStatsRebindDriver driver = host.GetComponent<XjActorOverviewStatsRebindDriver>();
        if (driver == null)
        {
            driver = host.gameObject.AddComponent<XjActorOverviewStatsRebindDriver>();
        }
        driver.Initialize(window);
        return driver;
    }

    private static void ApplyPercentRow(
        UnitWindow window,
        string rowName,
        OverviewStatIcon[] icons,
        float[] ratios)
    {
        Transform content = GetStatsContent(window);
        Transform row = content?.Find(rowName);
        if (row == null || icons == null || ratios == null)
        {
            return;
        }

        bool anyVisible = false;
        int count = Math.Min(icons.Length, ratios.Length);
        for (int i = 0; i < count; i++)
        {
            int value = (int)Math.Round(Math.Max(0f, ratios[i]) * 100f);
            Transform icon = row.Find(icons[i].Id);
            bool visible = value > 0;
            if (icon != null && icon.gameObject.activeSelf != visible)
            {
                icon.gameObject.SetActive(visible);
            }
            if (visible)
            {
                anyVisible = true;
                SetIconValue(window, icons[i].Id, value);
            }
        }

        if (row.gameObject.activeSelf != anyVisible)
        {
            row.gameObject.SetActive(anyVisible);
        }
    }

    private static void ConfigureIcon(Transform iconTransform, OverviewStatIcon icon)
    {
        if (iconTransform == null)
        {
            return;
        }

        EnsureIconLocalization(icon);
        iconTransform.name = icon.Id;
        StatsIcon statsIcon = iconTransform.GetComponent<StatsIcon>();
        if (statsIcon != null)
        {
            statsIcon.name = icon.Id;
            Image iconImage = statsIcon.getIcon();
            if (icon.UseTextIcon)
            {
                ConfigureTextIcon(iconImage, icon.DisplayName);
            }
            else
            {
                Sprite sprite = LoadOverviewSprite(icon.ResourcePaths);
                if (sprite != null && iconImage != null)
                {
                    iconImage.sprite = sprite;
                    iconImage.enabled = true;
                }
            }
        }

        TipButton tip = iconTransform.GetComponent<TipButton>();
        if (tip != null)
        {
            string localized = LM.Get("statsIcon_" + icon.Id);
            string title = string.IsNullOrWhiteSpace(localized) || localized == "statsIcon_" + icon.Id
                ? icon.DisplayName
                : localized;
            XjNativeHoverTooltip.Ensure(tip, title, tip.textOnClickDescription ?? string.Empty, string.Empty);
        }
    }

    private static void EnsureIconLocalization(OverviewStatIcon icon)
    {
        if (string.IsNullOrWhiteSpace(icon.Id) || string.IsNullOrWhiteSpace(icon.DisplayName))
        {
            return;
        }

        string key = "statsIcon_" + icon.Id;
        if (LM.Get(key) == key)
        {
            LocalizedTextManager.add(key, icon.DisplayName, false, string.Empty, true);
        }
    }

    private static void ConfigureTextIcon(Image iconImage, string text)
    {
        if (iconImage == null)
        {
            return;
        }

        iconImage.enabled = false;
        Transform existing = iconImage.transform.Find("XjTextIcon");
        Text textComponent = existing != null ? existing.GetComponent<Text>() : null;
        if (textComponent == null)
        {
            GameObject textObject = new GameObject("XjTextIcon", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(iconImage.transform, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            textComponent = textObject.GetComponent<Text>();
            textComponent.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
            textComponent.alignment = TextAnchor.MiddleCenter;
            textComponent.fontSize = 7;
            textComponent.color = new Color(1f, 0.61f, 0.11f, 1f);
            textComponent.raycastTarget = false;
        }

        textComponent.text = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim();
    }

    private static Sprite LoadOverviewSprite(string[] resourcePaths)
    {
        if (resourcePaths == null || resourcePaths.Length == 0)
        {
            return null;
        }

        for (int i = 0; i < resourcePaths.Length; i++)
        {
            string resourcePath = resourcePaths[i];
            if (string.IsNullOrWhiteSpace(resourcePath))
            {
                continue;
            }

            string path = resourcePath.Trim().Replace("\\", "/");
            Sprite sprite = SpriteTextureLoader.getSprite(path)
                ?? SpriteTextureLoader.getSprite("GameResources/" + path)
                ?? SpriteTextureLoader.getSprite("GameResources/" + path + ".png")
                ?? Resources.Load<Sprite>(path)
                ?? Resources.Load<Sprite>("GameResources/" + path);
            if (sprite != null)
            {
                return sprite;
            }
        }

        return null;
    }

    private static void SetCoreIconValue(UnitWindow window, string id, int value)
    {
        Transform content = GetStatsContent(window);
        Transform row = FindNativeKillsRow(content);
        Transform icon = row?.Find(id);
        if (icon != null && !icon.gameObject.activeSelf)
        {
            icon.gameObject.SetActive(true);
        }
        SetIconValue(window, id, value);
        // 原生 setIconValue/StatsIconContainer 在部分刷新链里可能改 active，核心三值在一次绑定末尾再兜一次。
        if (icon != null && !icon.gameObject.activeSelf)
        {
            icon.gameObject.SetActive(true);
        }
    }

    private static void SetIconValue(UnitWindow window, string id, int value)
    {
        if (window == null || string.IsNullOrWhiteSpace(id))
        {
            return;
        }

        try
        {
            window.setIconValue(id, value, null, string.Empty, false, string.Empty, '/');
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("UnitWindow.StableStatsIcon.SetValue", ex);
        }
    }

    private readonly struct StableRowDefinition
    {
        internal readonly string Name;
        internal readonly OverviewStatIcon[] Icons;

        internal StableRowDefinition(string name, OverviewStatIcon[] icons)
        {
            Name = name ?? string.Empty;
            Icons = icons ?? Array.Empty<OverviewStatIcon>();
        }
    }

    private static XjFaBaoBonusProfile BuildOverviewBonusProfile(Actor actor)
    {
        float cultivation = 0f;
        float guoWei = 0f;
        float attack = 0f;
        float reduction = 0f;
        float health = 0f;
        float penetration = 0f;
        float shield = 0f;
        float lifesteal = 0f;
        float dodge = 0f;
        float critTaken = 0f;
        float healback = 0f;
        float mingShu = 0f;
        float huiGuang = 0f;
        float lifespan = 0f;
        float accuracy = 0f;
        float crit = 0f;
        float attackSpeed = 0f;
        float sameRealm = 0f;
        float shieldBreak = 0f;
        float breakthrough = 0f;
        float trueDamage = 0f;
        XjFaBaoBonusProfile realmProfile = default;
        AccumulateProfile(
            XjCultivationPathRules.IsZiFuJinDan(actor) && XjRealmCombatBonuses.TryGetProfile(actor, out realmProfile),
            realmProfile,
            ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
            ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
            ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
            ref shieldBreak, ref breakthrough, ref trueDamage);
        AccumulateProfile(
            XjFaBaoBonusService.TryGetProfile(actor, out XjFaBaoBonusProfile faBaoProfile),
            faBaoProfile,
            ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
            ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
            ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
            ref shieldBreak, ref breakthrough, ref trueDamage);
        AccumulateProfile(
            XjWeaponArtSystem.TryGetBonusProfile(actor, out XjFaBaoBonusProfile weaponArtProfile),
            weaponArtProfile,
            ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
            ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
            ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
            ref shieldBreak, ref breakthrough, ref trueDamage);
        AccumulateProfile(
            XjSwordDaoCombatSystem.TryGetBonusProfile(actor, out XjFaBaoBonusProfile swordDaoProfile),
            swordDaoProfile,
            ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
            ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
            ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
            ref shieldBreak, ref breakthrough, ref trueDamage);
        return new XjFaBaoBonusProfile(
            cultivation, guoWei, attack, reduction, health, penetration, shield, lifesteal,
            dodge, critTaken, healback, mingShu, huiGuang, lifespan, accuracy, crit, attackSpeed,
            sameRealm, shieldBreak, breakthrough, trueDamage);
    }

    private static void AccumulateProfile(
        bool found,
        in XjFaBaoBonusProfile profile,
        ref float cultivation,
        ref float guoWei,
        ref float attack,
        ref float reduction,
        ref float health,
        ref float penetration,
        ref float shield,
        ref float lifesteal,
        ref float dodge,
        ref float critTaken,
        ref float healback,
        ref float mingShu,
        ref float huiGuang,
        ref float lifespan,
        ref float accuracy,
        ref float crit,
        ref float attackSpeed,
        ref float sameRealm,
        ref float shieldBreak,
        ref float breakthrough,
        ref float trueDamage)
    {
        if (!found)
        {
            return;
        }

        cultivation += profile.CultivationSpeedBonus;
        guoWei += profile.GuoWeiYiXiangBonus;
        attack += profile.AttackBonus;
        reduction += profile.DamageReduction;
        health += profile.HealthBonus;
        penetration += profile.ArmorPenetration;
        shield += profile.HealthShield;
        lifesteal += profile.Lifesteal;
        dodge += profile.DodgeBonus;
        critTaken += profile.CritTakenReduction;
        healback += profile.HealbackBonus;
        mingShu += profile.MingShuBonus;
        huiGuang += profile.HuiGuangBonus;
        lifespan += profile.LifespanBonus;
        accuracy += profile.AccuracyBonus;
        crit += profile.CritBonus;
        attackSpeed += profile.AttackSpeedBonus;
        sameRealm += profile.SameRealmDamageBonus;
        shieldBreak += profile.ShieldBreakBonus;
        breakthrough += profile.BreakthroughChanceBonus;
        trueDamage += profile.TrueDamageRatio;
    }

    private static int ToNonNegativeInteger(float value)
    {
        return (int)Math.Floor(Math.Max(0f, value));
    }

    private readonly struct OverviewStatIcon
    {
        internal readonly string Id;
        internal readonly string DisplayName;
        internal readonly string[] ResourcePaths;
        internal readonly bool UseTextIcon;

        internal OverviewStatIcon(string id, string displayName, params string[] resourcePaths)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ResourcePaths = resourcePaths ?? Array.Empty<string>();
            UseTextIcon = false;
        }

        internal OverviewStatIcon(string id, string displayName, bool useTextIcon)
        {
            Id = id ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            ResourcePaths = Array.Empty<string>();
            UseTextIcon = useTextIcon;
        }
    }
}

/// <summary>
/// 不新增 UnitWindow.OnEnable Harmony。showInfo/主动刷新只调度一次下一帧补绑；
/// 补绑完成后组件立即自停，下一次窗口刷新再由 Schedule 临时启用。
/// 结构和布局都不在 Update 中反复修改，人物页长期打开时没有空转轮询。
/// </summary>
internal sealed class XjActorOverviewStatsRebindDriver : MonoBehaviour
{
    private UnitWindow _window;
    private bool _pending;
    private int _targetFrame;

    internal void Initialize(UnitWindow window)
    {
        _window = window;
    }

    internal void Schedule(UnitWindow window)
    {
        if (window != null)
        {
            _window = window;
        }
        if (_window?.actor?.data == null)
        {
            return;
        }

        int nextFrame = Time.frameCount + 1;
        if (_pending && _targetFrame <= nextFrame)
        {
            return;
        }

        _targetFrame = nextFrame;
        _pending = true;
        if (!enabled)
        {
            enabled = true;
        }
    }

    private void OnEnable()
    {
        if (_window?.actor?.data != null)
        {
            _targetFrame = Time.frameCount + 1;
            _pending = true;
        }
    }

    private void OnDisable()
    {
        _pending = false;
    }

    private void Update()
    {
        if (!_pending || Time.frameCount < _targetFrame)
        {
            return;
        }

        _pending = false;
        try
        {
            XjActorOverviewStatsFormatter.BindValuesNow(_window);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("UnitWindow.StableStatsIcon.DelayedBind", ex);
        }
        finally
        {
            // 延迟补绑完成后关闭组件自身的 Update。下一次 showInfo/主动刷新会由
            // Schedule 重新启用一次，避免 UnitWindow 常驻打开时每帧只做 _pending 分支判断。
            enabled = false;
        }
    }
}
