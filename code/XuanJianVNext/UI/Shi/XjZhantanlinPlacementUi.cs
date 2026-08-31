using System;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.Shi;

/// <summary>
/// Presentation adapter for the unique Zhantanlin placement power.
/// The domain system owns placement rules; this class owns GodPower registration,
/// localization, button state, selection and player-facing tips.
/// </summary>
internal static class XjZhantanlinPlacementUi
{
    internal const string PlacePowerId = "KaiPiZhanTanLin";
    private const string PowerTitle = "开辟旃檀林";
    private const string PowerDescription = "在地图上选择位置，显化唯一的旃檀林；海陆均可开辟。佛土半径28，外围依次铺设3格光墙、3格不灭岩浆、3格光墙；佛土与三重封界必须完整落在地图内。";

    private static bool _powerRegistered;
    private static PowerButton _placementButton;

    internal static void EnsurePowerRegistered()
    {
        if (AssetManager.powers == null) return;

        GodPower power = AssetManager.powers.get(PlacePowerId);
        if (power == null)
        {
            power = new GodPower();
            power.id = PlacePowerId;
            power.name = PowerTitle;
            power.click_action = CreateAtTile;
            power.ignore_cursor_icon = true;
            AssetManager.powers.add(power);
        }
        else
        {
            power.id = PlacePowerId;
            power.name = PowerTitle;
            power.click_action = CreateAtTile;
            power.ignore_cursor_icon = true;
        }

        EnsurePowerLocalization();
        _powerRegistered = true;
    }

    internal static PowerButton CreatePlacementButton()
    {
        EnsurePowerRegistered();
        if (_placementButton != null)
        {
            RefreshPlacementButtonState();
            return _placementButton;
        }

        GodPower power = AssetManager.powers?.get(PlacePowerId);
        if (!_powerRegistered || power == null) return null;
        Sprite icon = SpriteTextureLoader.getSprite("trait/ZhanTanLin")
            ?? SpriteTextureLoader.getSprite("trait/XjJinShi")
            ?? SpriteTextureLoader.getSprite("ui/Icons/items/XuanJianCodex");
        PowerButton button = PowerButtonCreator.CreateGodPowerButton(PlacePowerId, icon);
        if (button == null) return null;
        // PowerButtonCreator may derive a snake_case localization key from the power id.
        // Store the final Chinese text directly in the native TipButton as well so neither
        // KaiPiZhanTanLin nor kai_pi_zhan_tan_lin can leak into the player-facing UI.
        XjNativeHoverTooltip.Ensure(button.GetComponent<TipButton>(), PowerTitle, PowerDescription, string.Empty);
        _placementButton = button;
        Button unityButton = button.GetComponent<Button>();
        if (unityButton != null)
        {
            unityButton.onClick.AddListener(() =>
            {
                if (XjZhantanlinSystem.IsPlaced)
                {
                    ShowTip("xuanjian_zhantanlin_already_exists");
                    TryUnselectAll();
                    RefreshPlacementButtonState();
                    return;
                }
                try { PowerButtonSelector.instance?.setPower(button); }
                catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.SelectPower", ex); }
            });
        }
        RefreshPlacementButtonState();
        return button;
    }

    internal static bool BeginPlacement()
    {
        if (XjZhantanlinSystem.IsPlaced)
        {
            ShowTip("xuanjian_zhantanlin_already_exists");
            RefreshPlacementButtonState();
            return false;
        }
        PowerButton placementButton = CreatePlacementButton();
        if (placementButton == null) return false;
        ScrollWindow.hideAllEvent();
        TryUnselectAll();
        try { PowerButtonSelector.instance?.clickPowerButton(placementButton); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.BeginPlacement.Click", ex); }
        try { PowerButtonSelector.instance?.setPower(placementButton); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.BeginPlacement.Set", ex); }
        return true;
    }

    internal static void RefreshPlacementButtonState(bool forceEnabled = false)
    {
        if (_placementButton == null) return;
        try
        {
            Button button = _placementButton.GetComponent<Button>();
            if (button != null) button.interactable = forceEnabled || !XjZhantanlinSystem.IsPlaced;
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.RefreshButton", ex);
        }
    }

    private static bool CreateAtTile(WorldTile tile, string powerId)
    {
        _ = powerId;
        XjZhantanlinSystem.PlacementResult result = XjZhantanlinSystem.TryPlaceAtTile(tile);
        switch (result)
        {
            case XjZhantanlinSystem.PlacementResult.Success:
                RefreshPlacementButtonState();
                TryUnselectAll();
                ShowTip("xuanjian_zhantanlin_created");
                return true;
            case XjZhantanlinSystem.PlacementResult.AlreadyExists:
                RefreshPlacementButtonState();
                TryUnselectAll();
                ShowTip("xuanjian_zhantanlin_already_exists");
                return false;
            case XjZhantanlinSystem.PlacementResult.CoreOutOfBounds:
                ShowTip("xuanjian_zhantanlin_out_of_bounds");
                return false;
            default:
                ShowTip("xuanjian_zhantanlin_failed");
                return false;
        }
    }

    private static void EnsurePowerLocalization()
    {
        // 旃檀林按钮文本全部随 Locales 静态加载。不要在每次 EnsurePowerRegistered
        // 时重复 LocalizedTextManager.add，否则会产生大量 Already exists 噪音。
        if (LocalizedTextManager.instance == null) return;
    }

    private static void TryUnselectAll()
    {
        try { PowerButtonSelector.instance?.unselectAll(); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.Unselect", ex); }
    }

    private static void ShowTip(string key)
    {
        try { WorldTip.showNow(key, true, "top"); }
        catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinPlacementUi.ShowTip", ex); }
    }
}
