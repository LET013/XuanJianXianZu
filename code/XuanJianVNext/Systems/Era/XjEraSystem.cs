using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Data.Era;
using UnityEngine;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.Era;

internal static class XjEraAssetRegistration
{
	internal static void Init()
	{
		if (AssetManager.era_library == null)
		{
			return;
		}

		// 十项玄鉴纪元统一注册；并古纪元同时进入随机纪元池。允许在高境切纪元前再次校准，避免其他初始化顺序覆盖图标字段。
		for (int i = 0; i < XjEraCatalog.All.Count; i++)
		{
			Register(XjEraCatalog.All[i]);
		}
	}

	private static void Register(in XjEraDefinition definition)
	{
		if (string.IsNullOrWhiteSpace(definition.Id))
		{
			return;
		}

		// 不能在同 ID 已存在时直接跳过。旧版本、热重载或其他注册顺序会留下
		// 过期的 path_icon，导致中央大图已是目标纪元而轮盘小图仍显示通用图标。
		// 复用现有资产并校准展示字段，不触碰玩家的纪元启用/禁用列表。
		WorldAgeAsset asset = AssetManager.era_library.get(definition.Id);
		if (asset == null)
		{
			asset = new WorldAgeAsset { id = definition.Id };
			((AssetLibrary<WorldAgeAsset>)(object)AssetManager.era_library).add(asset);
		}

		asset.path_icon = definition.IconPath;
		asset.path_background = definition.BackgroundPath;
		asset.rate = definition.Rate;
		asset.overlay_moon = definition.OverlayMoon;
		asset.overlay_sun = definition.OverlaySun;
		asset.global_unfreeze_world = definition.GlobalUnfreezeWorld;
		asset.temperature_damage_bonus = definition.TemperatureDamageBonus;
		asset.title_color = ParseColor(definition.TitleColorHex);
		asset.light_color = ParseColor(definition.LightColorHex);
	}

	private static Color ParseColor(string colorHex)
	{
		return ColorUtility.TryParseHtmlString(colorHex, out Color color) ? color : Color.white;
	}
}

internal static class XjEraRandomizer
{
	private static string _lastPickedAgeId = string.Empty;

	internal static bool TryPickRandomAgeForUnknown(out WorldAgeAsset pickedAge)
	{
		pickedAge = null;
		List<WorldAgeAsset> pool = BuildPool();
		if (pool.Count == 0)
		{
			return false;
		}

		string blockedAgeId = GetBlockedAgeId();
		List<WorldAgeAsset> filteredPool = new List<WorldAgeAsset>(pool.Count);
		for (int i = 0; i < pool.Count; i++)
		{
			WorldAgeAsset candidate = pool[i];
			if (candidate != null && candidate.id != blockedAgeId)
			{
				filteredPool.Add(candidate);
			}
		}

		List<WorldAgeAsset> finalPool = filteredPool.Count > 0 ? filteredPool : pool;
		pickedAge = finalPool[Random.Range(0, finalPool.Count)];
		if (pickedAge == null)
		{
			return false;
		}

		_lastPickedAgeId = pickedAge.id ?? string.Empty;
		return true;
	}

	internal static void Clear()
	{
		_lastPickedAgeId = string.Empty;
	}

	private static List<WorldAgeAsset> BuildPool()
	{
		List<WorldAgeAsset> pool = new List<WorldAgeAsset>(XjEraCatalog.All.Count);
		for (int i = 0; i < XjEraCatalog.All.Count; i++)
		{
			WorldAgeAsset asset = AssetManager.era_library?.get(XjEraCatalog.All[i].Id);
			if (asset != null)
			{
				pool.Add(asset);
			}
		}

		return pool;
	}

	private static string GetBlockedAgeId()
	{
		string currentAgeId = TryGetCurrentAgeId();
		return XjEraCatalog.Contains(currentAgeId) ? currentAgeId : _lastPickedAgeId;
	}

	private static string TryGetCurrentAgeId()
	{
		try
		{
			return World.world?.era_manager?.getCurrentAge()?.id ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}
}

internal static class XjEraRuntime
{
	private const string UnknownAgeId = "age_unknown";

	internal static bool TrySetCurrentAgeForDaoTu(string daoTuOrGroup, string changeCause)
	{
		// 确保已有同 ID 纪元资产也先完成图标/背景校准。
		XjEraAssetRegistration.Init();

		if (!XjEraCatalog.TryResolveAgeIdForDaoTu(daoTuOrGroup, out string ageId))
		{
			return false;
		}

		try
		{
			WorldAgeManager eraManager = World.world?.era_manager;
			WorldAgeAsset targetAge = AssetManager.era_library?.get(ageId);
			if (eraManager == null || targetAge == null)
			{
				return false;
			}

			if (string.Equals(eraManager.getCurrentAge()?.id, ageId, System.StringComparison.Ordinal))
			{
				return true;
			}

			// 只切换当前纪元，不改写玩家的纪元启用/禁用池。
			// 纪元提示必须携带调用方给出的明确来源，禁止再使用无法区分
			// “金丹晋升”与“洞天显世”的通用文案。
			eraManager.setCurrentAge(targetAge, true);
			bool switched = string.Equals(eraManager.getCurrentAge()?.id, ageId, System.StringComparison.Ordinal);
			if (switched)
			{
				// 纪元切换后重置纪元年份为当前世界年，使纪元时间从0开始计算。
				try
				{
					object currentAge = eraManager.getCurrentAge();
					int worldYear = World.world?.map_stats?.year ?? 0;
					if (currentAge != null && worldYear > 0)
					{
						XjNativeReflectionInterop.TryWriteMemberValue(currentAge, "year", worldYear);
					}
				}
				catch (System.Exception xjCaught184) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Era/XjEraSystem.cs:184", xjCaught184); }

				string safeCause = string.IsNullOrWhiteSpace(changeCause)
					? "【纪元异动】天地道势发生变化"
					: changeCause.Trim();
				string targetTitle = XjEraCatalog.GetDisplayName(ageId);
				XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelWorldEvent(
					safeCause + "，天地纪元转为【" + targetTitle + "】。",
					XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JiYuanChange);
			}
			return switched;
		}
		catch
		{
			return false;
		}
	}

	internal static bool TryReplaceUnknownAge(WorldAgeAsset candidateAge, out WorldAgeAsset replacementAge)
	{
		replacementAge = null;
		if (candidateAge == null || candidateAge.id != UnknownAgeId)
		{
			return false;
		}

		return XjEraRandomizer.TryPickRandomAgeForUnknown(out replacementAge);
	}

	internal static void Clear()
	{
		XjEraRandomizer.Clear();
	}
}
