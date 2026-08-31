using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Cultivation;

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
			if (!XjBroadcastSystem.ShouldMirrorToNativeWorldLog(text, iconId))
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
			if (!XjBroadcastSystem.ShouldMirrorToNativeWorldLog(text, iconId))
			{
				return true;
			}
			WorldLogAsset logAsset = ResolveLogAsset(text, iconId);
			if (logAsset == null)
			{
				return true;
			}

			WorldLogMessage message = new WorldLogMessage(logAsset, text, null, null);
			WorldLogMessageExtensions.add(message);
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
			if (!XjBroadcastSystem.ShouldMirrorToNativeWorldLog(text, iconId))
			{
				return true;
			}
			WorldLogAsset logAsset = ResolveLogAsset(text, iconId);
			if (logAsset == null)
			{
				return true;
			}

			WorldLogMessage message = new WorldLogMessage(logAsset, text, null, null);
			WorldLogMessageExtensions.add(message);
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
		return GetOrCreateLogAsset(category, resolvedIconId);
	}

	private static void RegisterGroup(string category)
	{
		string groupId = ResolveGroupId(category);
		if (AssetManager.history_groups.has(groupId))
		{
			HistoryGroupAsset existing = AssetManager.history_groups.get(groupId);
			if (existing != null) existing.icon_path = XjEventIconCatalog.BuildIconPath(XjEventIconCatalog.ResolveCategoryIconId(category));
			return;
		}

		HistoryGroupAsset group = new HistoryGroupAsset
		{
			id = groupId,
			icon_path = XjEventIconCatalog.BuildIconPath(XjEventIconCatalog.ResolveCategoryIconId(category))
		};
		AssetManager.history_groups.add(group);
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
	private static void RegisterLogLocalization(string assetId, string category, string iconId)
	{
		string title = ResolveLogTitle(category, iconId);
		LocalizedTextManager.add(assetId, title, false, string.Empty, true);
		LocalizedTextManager.add(assetId + "_description", title + "玄鉴公告", false, string.Empty, true);
	}

	private static string ResolveLogTitle(string category, string iconId)
	{
		string normalizedIconId = XjEventIconCatalog.NormalizeIconId(iconId);
		if (string.Equals(normalizedIconId, XjEventIconCatalog.LingWuAppear, StringComparison.Ordinal)) return "灵物现世";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.JinXingLegacy, StringComparison.Ordinal)) return "金性遗留";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.GongFaAcquire, StringComparison.Ordinal)) return "功法入库";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.QiuJinFaAcquire, StringComparison.Ordinal)) return "求金法";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.FaBaoCreation, StringComparison.Ordinal)) return "法宝成器";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.DongTianOpen, StringComparison.Ordinal)) return "洞天现世";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.DongTianClose, StringComparison.Ordinal)) return "洞天暂闭";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.DongTianDeath, StringComparison.Ordinal)) return "洞天身陨";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.ZongMenCreation, StringComparison.Ordinal)) return "开宗立道";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.ZongMenChongTu, StringComparison.Ordinal)) return "宗门冲突";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.ZiFuUpgrade, StringComparison.Ordinal)) return "紫府晋升";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.JinDanUpgrade, StringComparison.Ordinal)) return "金丹晋升";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.JinDanFail, StringComparison.Ordinal)) return "求金失败";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.JinDanDemon, StringComparison.Ordinal)) return "金性妖邪";
		if (string.Equals(normalizedIconId, XjEventIconCatalog.HighRealmDeath, StringComparison.Ordinal)) return "高境身陨";
		return ResolveCategoryTitle(category);
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
		// 世界日志格式化属于原生 UI 热路径。任何玄鉴状态读取失败都只能
		// 回退到事件发生时保存的正文，不能让 WorldLogWindow 连续报错。
		string snapshot = message?.special1 ?? string.Empty;
		text = snapshot;
		try
		{
			if (!XjFuQiSwordWorldState.IsEstablished
				|| snapshot.IndexOf("养青冥", StringComparison.Ordinal) < 0) return;

			// 旧档世界日志保存的是事件发生时的文案快照。长庚已经显世后，
			// 养青冥相关旧行应按世界已知事实显示，不继续残留未知道途。
			text = snapshot.Replace("【未知道途】", "【长庚】")
				.Replace("【无名道途】", "【长庚】")
				.Replace("【无名剑道】", "【长庚】");
		}
		catch
		{
			text = snapshot;
		}
	}
}
