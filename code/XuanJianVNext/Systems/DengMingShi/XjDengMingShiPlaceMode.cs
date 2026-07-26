using NeoModLoader.General;
using UnityEngine;

namespace XuanJianVNext.Systems.DengMingShi;

/// <summary>0.5.4-style GodPower placement mode backed by the cross-save ActorData archive.</summary>
internal static class XjDengMingShiPlaceMode
{
	internal const string PlacePowerId = "xuanjian_dengmingshi_place_actor";

	private static bool _powerRegistered;
	private static PowerButton _powerButton;

	internal static void EnsurePowerRegistered()
	{
		XjDengMingShiManager.Init();
		if (_powerRegistered && _powerButton != null) return;
		if (AssetManager.powers == null) return;

		GodPower power = AssetManager.powers.get(PlacePowerId);
		if (power == null)
		{
			power = new GodPower
			{
				id = PlacePowerId,
				name = PlacePowerId,
				click_action = XjDengMingShiManager.SpawnSavedActor,
				ignore_cursor_icon = true
			};
			AssetManager.powers.add(power);
		}
		else
		{
			power.click_action = XjDengMingShiManager.SpawnSavedActor;
			power.ignore_cursor_icon = true;
		}

		if (_powerButton == null)
		{
			_powerButton = PowerButtonCreator.CreateGodPowerButton(
				PlacePowerId,
				SpriteTextureLoader.getSprite("ui/Icons/items/DengMin"));
			if (_powerButton != null) _powerButton.gameObject.SetActive(false);
		}
		_powerRegistered = _powerButton != null;
	}

	internal static bool Begin(string recordId)
	{
		EnsurePowerRegistered();
		if (_powerButton == null || !XjDengMingShiManager.SelectActorToPlace(recordId)) return false;

		ScrollWindow.hideAllEvent();
		try { PowerButtonSelector.instance?.unselectAll(); } catch { }
		try { PowerButtonSelector.instance?.clickPowerButton(_powerButton); } catch { }
		try { PowerButtonSelector.instance?.setPower(_powerButton); } catch { }
		return true;
	}
}
