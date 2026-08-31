using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Patches;

/// <summary>
/// Narrow bridge for the established UnitWindow affordances:
/// 1) the right-side 玄鉴照录 panel;
/// 2) the R8-24 native-style core StatsIcon bridge;
/// 3) fixed, XuanJian-owned shortcut tools (登名石 / 性别转换 / 去除圣痕).
///
/// This is deliberately NOT the old UnitWindow refresh pipeline. It never touches tabs,
/// DragOrder, WindowHistory, native layout parameters, coroutines or tweens. The StatsIcon
/// bridge may statically add the three core icons around i_kills, but normal refreshes only
/// bind values. showInfo remains the sole Harmony actor/open transaction observer.
/// </summary>
internal sealed class XjUnitWindowSafeOverlayState : MonoBehaviour
{
    private long _lastActorId;
    private int _lastActorRuntimeToken;

    internal bool ShouldCommit(UnitWindow window, Transform shortcutParent)
    {
        Actor actor = window?.actor;
        if (actor?.data == null || shortcutParent == null)
        {
            return false;
        }

        long actorId = ((BaseSystemData)actor.data).id;
        int runtimeToken = RuntimeHelpers.GetHashCode(actor);
        bool dengMingMissing = shortcutParent.Find(XjVNextPatches.XuanJianDengMingShiShortcutName) == null;
        bool genderMissing = window._avatar_element?.transform?.Find("XuanJianGenderToggleButton") == null;
        bool scarToolMissing = actor.hasTrait("scar_of_divinity")
            && shortcutParent.Find("XuanJianRemoveDivineScarButton") == null;
        bool statsSurfaceMissing = !XjActorOverviewStatsFormatter.HasStableSurface(window);
        if (!dengMingMissing && !genderMissing && !scarToolMissing && !statsSurfaceMissing
            && _lastActorId == actorId
            && _lastActorRuntimeToken == runtimeToken)
        {
            return false;
        }

        _lastActorId = actorId;
        _lastActorRuntimeToken = runtimeToken;
        return true;
    }

    private void OnDisable()
    {
        // UnitWindow is reused by WorldBox. Reset only the bind stamp. The panel and
        // fixed button remain children of the window and naturally follow native
        // visibility; no close-time mutation is performed.
        _lastActorId = 0L;
        _lastActorRuntimeToken = 0;
    }
}

internal sealed class XjDengMingShiSafeButtonBinding : MonoBehaviour
{
    internal bool Bound;
}

internal partial class XjVNextPatches
{
    internal const string XuanJianDengMingShiShortcutName = "XuanJianDengMingShiSaveButton";

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UnitWindow), "showInfo")]
    private static void XuanJianVNext_UnitWindow_ShowInfo_SafeOverlay_Postfix(UnitWindow __instance)
    {
        try
        {
            CommitSafeUnitWindowOverlay(__instance);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("UnitWindow.SafeOverlay", ex);
        }
    }

    private static void CommitSafeUnitWindowOverlay(UnitWindow window)
    {
        Actor actor = window?.actor;
        Transform favoriteVisual = window?._icon_favorite?.transform?.parent;
        Transform shortcutParent = favoriteVisual?.parent;
        if (actor?.data == null || favoriteVisual == null || shortcutParent == null)
        {
            return;
        }

        XjUnitWindowSafeOverlayState state = ((Component)window).GetComponent<XjUnitWindowSafeOverlayState>()
            ?? ((Component)window).gameObject.AddComponent<XjUnitWindowSafeOverlayState>();
        if (!state.ShouldCommit(window, shortcutParent))
        {
            return;
        }

        // Restore the established right-side 玄鉴照录 panel. It is XuanJian-owned,
        // absolutely positioned under Background and does not participate in native
        // rows/tabs/layout. Invalidate forces exactly one fresh bind for this open.
        XjActorInfoPanelRenderer.Invalidate();
        XjActorInfoPanelRenderer.Refresh(window);

        // Native-style XuanJian attributes: core stats are inserted around the native kills icon once.
        // This observer only ensures structure and schedules a next-frame value bind so native late initialization
        // cannot erase XuanJian values. No global layout rebuild or recurring refresh is performed.
        XjActorOverviewStatsFormatter.Refresh(window, forceRefresh: false);

        GameObject dengMing = XjStableUiFactory.FindOrCreateFixedButton(
            shortcutParent,
            favoriteVisual.gameObject,
            XuanJianDengMingShiShortcutName,
            new Vector3(116.8f, -112f, 0f));
        ConfigureDengMingShortcut(dengMing, window, actor);

        // P0 quarantine must not remove established stateless player tools. These
        // buttons are XuanJian-owned fixed children: no native tab/StatsRows/DragOrder
        // registration, no live-object cloning, no periodic refresh.
        XuanJianVNext_TryAddGodToolsUnitWindowButtons(window);
    }

    private static void ConfigureDengMingShortcut(GameObject buttonObject, UnitWindow window, Actor actor)
    {
        if (buttonObject == null)
        {
            return;
        }

        SetDengMingShortcutIcon(buttonObject);

        XjDengMingShiSafeButtonBinding binding = buttonObject.GetComponent<XjDengMingShiSafeButtonBinding>()
            ?? buttonObject.AddComponent<XjDengMingShiSafeButtonBinding>();
        Button button = buttonObject.GetComponent<Button>();
        if (button != null && !binding.Bound)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                Actor current = window?.actor;
                if (!XjSafeCore.IsAliveActor(current))
                {
                    return;
                }

                if (XjDengMingShiCommands.IsActorSaved(current))
                {
                    XjDengMingShiCommands.RemoveSavedActor(current);
                }
                else
                {
                    XjDengMingShiCommands.SaveSelectedActor(current);
                }
                RefreshDengMingShortcutVisual(buttonObject, current);
            });
            binding.Bound = true;
        }

        TipButton tip = buttonObject.GetComponent<TipButton>();
        if (tip != null)
        {
            XjNativeHoverTooltip.Ensure(
                tip,
                "登名石",
                "保存或移除当前角色的登名石记录。",
                "亮色表示已登名，灰色表示尚未登名。");
        }

        RefreshDengMingShortcutVisual(buttonObject, actor);
        buttonObject.SetActive(true);
    }

    private const string DengMingIconLayerName = "XuanJianDengMinIcon";
    private static bool _dengMingIconLoadWarned;

    private static void SetDengMingShortcutIcon(GameObject buttonObject)
    {
        Image icon = ResolveDengMingShortcutIcon(buttonObject);
        if (icon == null)
        {
            return;
        }

        // DengMin.png lives in the mod GameResources tree. SpriteTextureLoader is
        // the loader used by 0.9.8 for custom XuanJian UI sprites; Resources.Load
        // can return null here because this is not a Unity Resources asset.
        Sprite sprite = null;
        try
        {
            sprite = SpriteTextureLoader.getSprite("ui/Icons/items/DengMin")
                ?? SpriteTextureLoader.getSprite("ui/Icons/items/DengMin.png");
        }
        catch
        {
        }
        if (sprite == null)
        {
            try { sprite = Resources.Load<Sprite>("ui/Icons/items/DengMin"); }
            catch { }
        }

        Transform legacyIconTransform = buttonObject.transform.Find("Icon");
        Image legacyIcon = legacyIconTransform != null ? legacyIconTransform.GetComponent<Image>() : null;
        if (sprite != null)
        {
            // The safe button factory intentionally copies only harmless visuals
            // from the favourite slot. Hide that copied star image and render the
            // actual DengMin sprite on a dedicated top layer so it cannot be
            // overwritten by the template visual.
            if (legacyIcon != null && legacyIcon != icon)
            {
                legacyIcon.enabled = false;
                legacyIcon.raycastTarget = false;
            }
            icon.sprite = sprite;
            icon.enabled = true;
            icon.transform.SetAsLastSibling();
        }
        else if (!_dengMingIconLoadWarned)
        {
            _dengMingIconLoadWarned = true;
            Debug.LogWarning("[玄鉴][登名石] DengMin sprite 加载失败：ui/Icons/items/DengMin");
        }
        icon.raycastTarget = false;
    }

    private static void RefreshDengMingShortcutVisual(GameObject buttonObject, Actor actor)
    {
        Image icon = ResolveDengMingShortcutIcon(buttonObject);
        if (icon == null)
        {
            return;
        }

        bool saved = XjSafeCore.IsAliveActor(actor) && XjDengMingShiCommands.IsActorSaved(actor);
        icon.color = saved ? Color.white : new Color(0.52f, 0.52f, 0.52f, 0.62f);
    }

    private static Image ResolveDengMingShortcutIcon(GameObject buttonObject)
    {
        if (buttonObject == null)
        {
            return null;
        }

        Transform existing = buttonObject.transform.Find(DengMingIconLayerName);
        if (existing != null)
        {
            return existing.GetComponent<Image>();
        }

        Transform templateIcon = buttonObject.transform.Find("Icon");
        RectTransform templateRect = templateIcon != null ? templateIcon.GetComponent<RectTransform>() : null;
        GameObject iconObject = new GameObject(DengMingIconLayerName, typeof(RectTransform), typeof(Image));
        iconObject.transform.SetParent(buttonObject.transform, false);
        RectTransform rect = iconObject.GetComponent<RectTransform>();
        if (templateRect != null)
        {
            rect.anchorMin = templateRect.anchorMin;
            rect.anchorMax = templateRect.anchorMax;
            rect.pivot = templateRect.pivot;
            rect.anchoredPosition = templateRect.anchoredPosition;
            rect.sizeDelta = templateRect.sizeDelta;
            rect.localScale = Vector3.one;
        }
        else
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
        Image image = iconObject.GetComponent<Image>();
        image.raycastTarget = false;
        return image;
    }
}
