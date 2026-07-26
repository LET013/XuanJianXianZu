using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.EventSystems;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Codex;

namespace XuanJianVNext.Systems.Map;

/// <summary>
/// Cultiway-style native map overlay for sect territory. The overlay reads the
/// existing sect archive and native city zones only; it never owns territory.
/// </summary>
internal static class XjSectMapLayerSystem
{
	internal const string PowerId = "xuanjian_sect_map_layer";
	private static readonly Color32[] SectColors =
	{
		new Color32(74, 231, 255, 238), new Color32(255, 202, 64, 238),
		new Color32(188, 124, 255, 238), new Color32(80, 255, 147, 238),
		new Color32(255, 105, 142, 238), new Color32(111, 154, 255, 238),
		new Color32(255, 132, 63, 238), new Color32(218, 255, 90, 238)
	};
	private static readonly Color32 DimNonSectTileColor = new Color32(6, 8, 12, 64);

	private static bool _enabled;
	private static XjSectMapLayer _layer;
	private static readonly Dictionary<int, long> SectIdByTileId = new Dictionary<int, long>();
	// Power registration touches the option library and localization manager. It is
	// process-wide state, so repeating it once per rendered frame is pure overhead.
	private static bool _powerRegistered;
	private static int _lastHoverTileId = -1;
	private const string PowerTitle = "宗门图层";
	private const string PowerDescription = "为宗门城镇着色，点击国家查看宗门峰脉。";

	internal static void ToggleFromUi()
	{
		Toggle();
	}

	internal static void EnsurePowerRegistered()
	{
		EnsurePowerButton();
	}

	internal static void EnsureForCurrentWorld()
	{
		if (World.world == null || World.world._map_layers == null) return;
		if (!_powerRegistered)
		{
			EnsurePowerButton();
		}
		if ((UnityEngine.Object)_layer != null) return;

		GameObject layerObject = new GameObject("[玄鉴]宗门图层", typeof(XjSectMapLayer), typeof(SpriteRenderer));
		layerObject.transform.SetParent(World.world.transform);
		layerObject.transform.localPosition = Vector3.zero;
		layerObject.transform.localScale = Vector3.one;
		SpriteRenderer renderer = layerObject.GetComponent<SpriteRenderer>();
		if (renderer != null) renderer.sortingOrder = 1;
		_layer = layerObject.GetComponent<XjSectMapLayer>();
		World.world._map_layers.Add(_layer);
		_layer.MarkDirty();
	}

	internal static void MarkDirty()
	{
		_layer?.MarkDirty();
	}

	internal static void ResetForWorldUnload()
	{
		SectIdByTileId.Clear();
		_layer = null;
		_lastHoverTileId = -1;
	}

	private static void EnsurePowerButton()
	{
		if (_powerRegistered && AssetManager.powers?.get(PowerId) != null) return;
		if (AssetManager.powers == null) return;
		XjNativeHoverTooltip.RegisterPassthrough(PowerTitle, PowerDescription);
		EnsurePowerOption();
		GodPower power = AssetManager.powers.get(PowerId);
		if (power == null)
		{
			power = new GodPower
			{
				id = PowerId,
				name = PowerTitle,
				toggle_name = PowerId,
				map_modes_switch = false,
				force_map_mode = MetaType.None,
				unselect_when_window = true,
				ignore_cursor_icon = true,
				can_drag_map = true,
				allow_unit_selection = true
			};
			AssetManager.powers.add(power);
		}
		power.name = PowerTitle;
		power.toggle_name = PowerId;
		power.map_modes_switch = false;
		power.force_map_mode = MetaType.None;
		power.unselect_when_window = true;
		power.ignore_cursor_icon = true;
		power.can_drag_map = true;
		power.allow_unit_selection = true;
		power.toggle_action = _ => Toggle();
		power.click_action = (_, _) => false;
		RegisterPowerLocalization();
		_powerRegistered = true;
	}

	private static void RegisterPowerLocalization()
	{
		try
		{
			if (LocalizedTextManager.instance == null) return;
			LocalizedTextManager.add(PowerId, PowerTitle, false, string.Empty, true);
			LocalizedTextManager.add(PowerId + "_description", PowerDescription, false, string.Empty, true);
			LocalizedTextManager.add("xuanjian.sect_map_layer", PowerTitle, false, string.Empty, true);
			LocalizedTextManager.add("xuanjian.sect_map_layer_description", PowerDescription, false, string.Empty, true);
			LocalizedTextManager.add(PowerTitle, PowerTitle, false, string.Empty, true);
			LocalizedTextManager.add(PowerTitle + "_description", PowerDescription, false, string.Empty, true);
		}
		catch
		{
		}
	}

	private static void EnsurePowerOption()
	{
		try
		{
			if (AssetManager.options_library != null && AssetManager.options_library.get(PowerId) == null)
			{
				AssetManager.options_library.add(new OptionAsset
				{
					id = PowerId,
					default_int = 0,
					max_value = 0,
					multi_toggle = false,
					type = OptionType.Bool
				});
			}

			if (PlayerConfig.dict != null && !PlayerConfig.dict.ContainsKey(PowerId))
			{
				PlayerConfig.dict.Add(PowerId, new PlayerOptionData(PowerId)
				{
					boolVal = false,
					intVal = 0
				});
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴] 宗门图层开关配置初始化失败: " + ex.Message);
		}
	}

	private static void Toggle()
	{
		_enabled = !_enabled;
		ClearSelectedPowerButton();
		SetOptionState(_enabled);
		EnsureForCurrentWorld();
		_layer?.MarkDirty();
		_lastHoverTileId = -1;
		WorldTip.instance?.startHide();
	}

	private static void ClearSelectedPowerButton()
	{
		try
		{
			PowerButtonSelector.instance?.unselectAll();
		}
		catch
		{
		}
	}

	private static void SetOptionState(bool enabled)
	{
		try
		{
			if (PlayerConfig.dict != null && PlayerConfig.dict.TryGetValue(PowerId, out PlayerOptionData option))
			{
				option.boolVal = enabled;
				option.intVal = enabled ? 1 : 0;
			}
		}
		catch
		{
		}
	}

	private static void HandleSectLayerClick()
	{
		if (!_enabled || World.world == null) return;
		bool clicked = Input.GetMouseButtonDown(0);
		if (clicked && ShouldIgnoreMapClick())
		{
			return;
		}
		WorldTile tile;
		try
		{
			tile = World.world.getMouseTilePos();
		}
		catch
		{
			return;
		}
		if (tile == null) return;

		int tileId = tile.data?.tile_id ?? -1;
		if (!clicked && tileId == _lastHoverTileId) return;
		_lastHoverTileId = tileId;

		if (!TryResolveSectAtTile(tile, out XjSectArchiveRecord tileSect) || tileSect == null || tileSect.SectId <= 0L)
		{
			ClearHoveredSect();
			return;
		}

		if (clicked)
		{
			XjCodexWindow.ShowSectPeaks(tileSect.SectId);
		}
	}

	private static bool ShouldIgnoreMapClick()
	{
		if (XjCodexWindow.IsOpen)
		{
			return true;
		}

		try
		{
			EventSystem eventSystem = EventSystem.current;
			return eventSystem != null && eventSystem.IsPointerOverGameObject();
		}
		catch
		{
			return false;
		}
	}

	private static bool TryResolveSectAtTile(WorldTile tile, out XjSectArchiveRecord sect)
	{
		sect = null;
		int tileId = tile?.data?.tile_id ?? -1;
		return tileId >= 0
			&& SectIdByTileId.TryGetValue(tileId, out long sectId)
			&& XjSectRepository.TryGetBySectId(sectId, out sect)
			&& sect != null
			&& sect.SectId > 0L;
	}

	private static bool TryGetSectForMapCity(City city, out XjSectArchiveRecord sect)
	{
		sect = null;
		long cityId = city?.data?.id ?? 0L;
		if (cityId > 0L
			&& XjSectRepository.TryGetGovernance(cityId, out XjCityFamilyGovernanceArchiveRecord governance)
			&& governance?.SectId > 0L
			&& XjSectRepository.TryGetBySectId(governance.SectId, out sect)
			&& IsRenderableSect(sect))
		{
			return true;
		}

		return XjSectRepository.TryGetByCity(city, out sect) && IsRenderableSect(sect);
	}

	private static bool IsRenderableSect(XjSectArchiveRecord sect)
	{
		return sect != null
			&& sect.SectId > 0L
			&& !string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal);
	}

	private static void ClearHoveredSect()
	{
		try
		{
			WorldTip.instance?.startHide();
		}
		catch
		{
		}
	}

	private static Color32 ResolveSectColor(long sectId)
	{
		ulong stable = (ulong)(sectId < 0L ? -sectId : sectId);
		return SectColors[(int)(stable % (ulong)SectColors.Length)];
	}

	private sealed class XjSectMapLayer : MapLayer
	{
		private bool _dirty = true;
		private bool _textureWarned;
		private SpriteRenderer _renderer;

		internal void MarkDirty()
		{
			_dirty = true;
		}

		public override void update(float pElapsed)
		{
			if (!EnsureTextureReady()) return;
			if (_renderer == null) _renderer = GetComponent<SpriteRenderer>();
			if (_renderer == null || pixels == null) return;

			_renderer.enabled = _enabled;
			if (!_enabled)
			{
				return;
			}
			float alpha = 0.45f;
			try
			{
				alpha = MapBox.isRenderMiniMap() && World.world?.zone_calculator != null
					? World.world.zone_calculator.minimap_opacity
					: Mathf.Clamp(ZoneCalculator.getCameraScaleZoom() * 0.3f, 0.18f, 0.68f);
			}
			catch
			{
				alpha = 0.45f;
			}
			_renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp(alpha + 0.2f, 0.42f, 0.88f));

			if (_dirty)
			{
				RebuildPixels();
				// MapLayer does not upload its backing pixel buffer automatically.
				// Without this call the power toggles correctly but the sect colors
				// never reach the overlay texture.
				updatePixels();
				_dirty = false;
			}
			HandleSectLayerClick();
		}

		private bool EnsureTextureReady()
		{
			if (pixels != null) return true;
			if (World.world?.tiles_list == null) return false;
			try
			{
				createTextureNew();
			}
			catch (Exception ex)
			{
				if (!_textureWarned)
				{
					_textureWarned = true;
					Debug.LogWarning("[玄鉴] 宗门图层纹理初始化失败，已跳过本帧：" + ex.Message);
				}
				return false;
			}
			return pixels != null;
		}

		private void RebuildPixels()
		{
			WorldTile[] tiles = World.world?.tiles_list;
			if (tiles == null || pixels == null || pixels.Length != tiles.Length) return;
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = DimNonSectTileColor;
			}
			SectIdByTileId.Clear();
			if (World.world.cities == null) return;

			IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
			for (int cityIndex = 0; cityIndex < cities.Count; cityIndex++)
			{
				City city = cities[cityIndex];
				if (city?.data == null || city.zones == null) continue;
				if (!TryGetSectForMapCity(city, out XjSectArchiveRecord sect) || sect == null) continue;
				Color32 color = ResolveSectColor(sect.SectId);
				for (int zoneIndex = 0; zoneIndex < city.zones.Count; zoneIndex++)
				{
					TileZone zone = city.zones[zoneIndex];
					if (zone?.tiles == null) continue;
					for (int tileIndex = 0; tileIndex < zone.tiles.Length; tileIndex++)
					{
						WorldTile tile = zone.tiles[tileIndex];
						int id = tile?.data?.tile_id ?? -1;
						if (id >= 0 && id < pixels.Length)
						{
							pixels[id] = color;
							SectIdByTileId[id] = sect.SectId;
						}
					}
				}
			}
		}

	}
}
