using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Broadcast;

internal static class XjWorldHistoryRegistry
{
	private const string GroupPrefix = "xuanjian_history_";
	private const string LogIdPrefix = "xuanjian_history_broadcast";
	private static readonly Dictionary<string, WorldLogAsset> LogAssetsById = new Dictionary<string, WorldLogAsset>(StringComparer.Ordinal);
	private static bool _initialized;

	internal static void Init()
	{
		if (_initialized)
		{
			return;
		}

		try
		{
			for (int i = 0; i < XjWorldHistoryCategory.Ordered.Length; i++)
			{
				string category = XjWorldHistoryCategory.Ordered[i];
				if (string.Equals(category, XjWorldHistoryCategory.All, StringComparison.Ordinal))
				{
					continue;
				}

				RegisterGroup(category);
				GetOrCreateLogAsset(category, string.Empty);
			}
			_initialized = true;
		}
		catch (Exception ex)
		{
			LogAssetsById.Clear();
			Debug.LogError("[玄鉴][历史] 世界历史资产初始化失败：" + ex);
		}
	}

	internal static bool AddActorEvent(Actor actor, string text, string iconId = null)
	{
		if (!_initialized)
		{
			Init();
		}

		if (actor == null || string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		try
		{
			if (!XjWorldHistoryStore.RecordActorEvent(actor, text, iconId))
			{
				return true;
			}
			if (!XjBroadcastSystem.ShouldShowAnnouncement(text, iconId))
			{
				return true;
			}
			WorldLogAsset logAsset = ResolveLogAsset(text, iconId);
			if (logAsset == null)
			{
				return true;
			}

			WorldLogMessage message = new WorldLogMessage(logAsset, text, null, null)
			{
				unit = actor,
				location = ((BaseSimObject)actor).current_position
			};
			WorldLogMessageExtensions.add(message);
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][历史] 写入角色历史失败：" + ex);
			return false;
		}
	}

	internal static bool AddWorldEvent(string text, string iconId = null)
	{
		if (!_initialized)
		{
			Init();
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		try
		{
			if (!XjWorldHistoryStore.RecordWorldEvent(text, iconId))
			{
				return true;
			}
			if (!XjBroadcastSystem.ShouldShowAnnouncement(text, iconId))
			{
				return true;
			}
			WorldLogAsset logAsset = ResolveLogAsset(text, iconId);
			if (logAsset == null)
			{
				return true;
			}

			WorldLogMessageExtensions.add(new WorldLogMessage(logAsset, text, null, null));
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][历史] 写入世界历史失败：" + ex);
			return false;
		}
	}

	internal static bool AddDomainEventLogOnly(string text, string iconId = null)
	{
		if (!_initialized)
		{
			Init();
		}

		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}
		try
		{
			WorldLogAsset logAsset = ResolveLogAsset(text, iconId);
			if (logAsset == null)
			{
				return true;
			}

			WorldLogMessageExtensions.add(new WorldLogMessage(logAsset, text, null, null));
			return true;
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][历史] 写入领域历史失败：" + ex);
			return false;
		}
	}

	private static WorldLogAsset ResolveLogAsset(string text, string iconId)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		// The event producer owns semantic classification. Never parse localized
		// announcement text to guess an icon; missing explicit kinds use generic history.
		string resolvedIconId = XjEventIconCatalog.NormalizeIconId(iconId);
		string category = XjWorldHistoryStore.ResolveEventCategory(resolvedIconId, text);
		return GetOrCreateLogAsset(category, string.Empty);
	}

	private static void RegisterGroup(string category)
	{
		string groupId = ResolveGroupId(category);
		if (AssetManager.history_groups.has(groupId))
		{
			HistoryGroupAsset existing = AssetManager.history_groups.get(groupId);
			if (existing != null) existing.icon_path = XjEventIconCatalog.BuildIconPath(XjEventIconCatalog.ResolveCategoryIconId(category));
			RegisterGroupLocalization(groupId, category);
			return;
		}

		HistoryGroupAsset group = new HistoryGroupAsset
		{
			id = groupId,
			icon_path = XjEventIconCatalog.BuildIconPath(XjEventIconCatalog.ResolveCategoryIconId(category))
		};
		AssetManager.history_groups.add(group);
		RegisterGroupLocalization(groupId, category);
	}

	private static WorldLogAsset GetOrCreateLogAsset(string category, string iconId)
	{
		string normalizedCategory = NormalizeCategory(category);
		string categoryKey = ResolveCategoryKey(normalizedCategory);
		string normalizedIconId = XjEventIconCatalog.NormalizeIconId(iconId);
		string assetId = string.IsNullOrEmpty(normalizedIconId)
			? LogIdPrefix + "_" + categoryKey
			: LogIdPrefix + "_" + categoryKey + "_" + normalizedIconId;
		if (LogAssetsById.TryGetValue(assetId, out WorldLogAsset cached))
		{
			return cached;
		}

		try
		{
			string groupId = ResolveGroupId(normalizedCategory);
			WorldLogAsset asset = AssetManager.world_log_library.has(assetId)
				? AssetManager.world_log_library.get(assetId)
				: new WorldLogAsset
				{
					id = assetId,
					group = groupId,
					color = Toolbox.color_log_neutral,
					text_replacer = ReplaceText
				};

			asset.group = groupId;
			asset.path_icon = XjEventIconCatalog.BuildIconPath(string.IsNullOrEmpty(normalizedIconId)
				? XjEventIconCatalog.ResolveCategoryIconId(normalizedCategory)
				: normalizedIconId);
			asset.color = Toolbox.color_log_neutral;
			asset.text_replacer = ReplaceText;
			RegisterGroupLocalization(groupId, normalizedCategory);
			RegisterLogLocalization(assetId, normalizedCategory, normalizedIconId);

			if (!AssetManager.world_log_library.has(assetId))
			{
				AssetManager.world_log_library.add(asset);
			}

			LogAssetsById[assetId] = asset;
			return asset;
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][历史] 创建历史图标资产失败：" + ex);
			return null;
		}
	}

	private static void RegisterGroupLocalization(string groupId, string category)
	{
		string title = ResolveCategoryTitle(category);
		string description = title + "类玄鉴世界历史";
		LocalizedTextManager.add("history_group_" + groupId, title, false, string.Empty, true);
		LocalizedTextManager.add("history_group_" + groupId + "_description", description, false, string.Empty, true);
		LocalizedTextManager.add(groupId, title, false, string.Empty, true);
		LocalizedTextManager.add(groupId + "_description", description, false, string.Empty, true);
	}

	private static void RegisterLogLocalization(string assetId, string category, string iconId)
	{
		string title = ResolveCategoryTitle(category);
		LocalizedTextManager.add(assetId, title, false, string.Empty, true);
		LocalizedTextManager.add(assetId + "_description", title + "类玄鉴公告", false, string.Empty, true);
	}

	private static string NormalizeCategory(string category)
	{
		string value = (category ?? string.Empty).Trim();
		for (int i = 0; i < XjWorldHistoryCategory.Ordered.Length; i++)
		{
			string known = XjWorldHistoryCategory.Ordered[i];
			if (!string.Equals(known, XjWorldHistoryCategory.All, StringComparison.Ordinal)
				&& string.Equals(value, known, StringComparison.Ordinal))
			{
				return known;
			}
		}
		return XjWorldHistoryCategory.World;
	}

	private static string ResolveCategoryTitle(string category)
	{
		string value = NormalizeCategory(category);
		if (string.Equals(value, XjWorldHistoryCategory.Cultivation, StringComparison.Ordinal)) return "修行";
		if (string.Equals(value, XjWorldHistoryCategory.Family, StringComparison.Ordinal)) return "家族";
		if (string.Equals(value, XjWorldHistoryCategory.Sect, StringComparison.Ordinal)) return "宗门";
		if (string.Equals(value, XjWorldHistoryCategory.Inheritance, StringComparison.Ordinal)) return "传承";
		if (string.Equals(value, XjWorldHistoryCategory.Craft, StringComparison.Ordinal)) return "百艺";
		if (string.Equals(value, XjWorldHistoryCategory.Opportunity, StringComparison.Ordinal)) return "机缘";
		if (string.Equals(value, XjWorldHistoryCategory.Vendetta, StringComparison.Ordinal)) return "恩怨";
		if (string.Equals(value, XjWorldHistoryCategory.LifeAndDeath, StringComparison.Ordinal)) return "生死";
		return "天下";
	}

	private static string ResolveGroupId(string category)
	{
		return GroupPrefix + ResolveCategoryKey(category);
	}

	private static string ResolveCategoryKey(string category)
	{
		string value = NormalizeCategory(category);
		if (string.Equals(value, XjWorldHistoryCategory.Cultivation, StringComparison.Ordinal)) return "cultivation";
		if (string.Equals(value, XjWorldHistoryCategory.Family, StringComparison.Ordinal)) return "family";
		if (string.Equals(value, XjWorldHistoryCategory.Sect, StringComparison.Ordinal)) return "sect";
		if (string.Equals(value, XjWorldHistoryCategory.Inheritance, StringComparison.Ordinal)) return "inheritance";
		if (string.Equals(value, XjWorldHistoryCategory.Craft, StringComparison.Ordinal)) return "craft";
		if (string.Equals(value, XjWorldHistoryCategory.Opportunity, StringComparison.Ordinal)) return "opportunity";
		if (string.Equals(value, XjWorldHistoryCategory.Vendetta, StringComparison.Ordinal)) return "vendetta";
		if (string.Equals(value, XjWorldHistoryCategory.LifeAndDeath, StringComparison.Ordinal)) return "life_death";
		return "world";
	}

	private static void ReplaceText(WorldLogMessage message, ref string text)
	{
		text = message?.special1 ?? string.Empty;
	}
}
