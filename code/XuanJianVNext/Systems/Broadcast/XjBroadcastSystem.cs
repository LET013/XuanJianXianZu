using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Broadcast;

internal enum XjAnnouncementCategory
{
	Auto = 0,
	FamilyInheritance = 1,
	HighRealmInfluence = 2,
	GongFaWrite = 3,
	Sect = 4
}

internal static class XjBroadcastSystem
{
	internal static bool HasPendingAnnouncements => PendingTips.Count > 0;
	private const float BroadcastGlobalMinInterval = 0.18f;
	private const float BroadcastSameTextCooldown = 1.2f;
	private const float WorldTipGlobalMinInterval = 0.3f;
	private const float WorldTipSameTextCooldown = 6f;
	private const int RateCacheMax = 512;
	private const int PendingMax = 128;
	private const int ProcessBudget = 12;

	private readonly struct PendingTip
	{
		internal readonly string Text;
		internal readonly bool Pause;
		internal readonly string Position;
		internal readonly float Duration;
		internal readonly string Color;
		internal readonly int EarliestFrame;
		internal readonly XjAnnouncementCategory Category;
		internal readonly string IconId;

		internal PendingTip(string text, bool pause, string position, float duration, string color, int earliestFrame, XjAnnouncementCategory category, string iconId)
		{
			Text = text ?? string.Empty;
			Pause = pause;
			Position = string.IsNullOrWhiteSpace(position) ? "top" : position;
			Duration = duration <= 0f ? 10f : duration;
			Color = string.IsNullOrWhiteSpace(color) ? "#F3961F" : color;
			EarliestFrame = earliestFrame;
			Category = category;
			IconId = iconId ?? string.Empty;
		}
	}

	private static readonly Dictionary<string, float> BroadcastLastByText = new Dictionary<string, float>(StringComparer.Ordinal);
	private static readonly Dictionary<string, float> WorldTipLastByText = new Dictionary<string, float>(StringComparer.Ordinal);
	private static readonly List<string> CleanupKeys = new List<string>(64);
	private static readonly Queue<PendingTip> PendingTips = new Queue<PendingTip>(16);
	private static readonly HashSet<string> PendingTipTexts = new HashSet<string>(StringComparer.Ordinal);
	private static float _lastBroadcastTime = -9999f;
	private static float _lastWorldTipTime = -9999f;

	internal static bool PostActor(Actor actor, string text, string iconId = null)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(text) || !TryAcquireBroadcastSlot(text, ignoreGlobalInterval: false))
		{
			return false;
		}

		return XjWorldHistoryRegistry.AddActorEvent(actor, text, iconId);
	}

	private static bool PostActorCritical(Actor actor, string text, string iconId = null)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(text) || !TryAcquireBroadcastSlot(text, ignoreGlobalInterval: true))
		{
			return false;
		}

		return XjWorldHistoryRegistry.AddActorEvent(actor, text, iconId);
	}

	internal static bool ShowWorldTip(string text, bool pause = false, string position = "top", float duration = 10f, string color = "#F3961F")
	{
		if (string.IsNullOrWhiteSpace(text) || !ShouldShowAnnouncement(text, null) || !TryAcquireWorldTipSlot(text))
		{
			return false;
		}

		return TryShowWorldTip(text, pause, position, duration, color);
	}

	/// <summary>
	/// 显式公告分类入口。家族传承与上修干预不再依赖文本关键词猜测，
	/// 避免“紫府、法宝”等词误受其他公告开关控制。关闭开关只隐藏提示，
	/// 不影响世界状态、百年世谱和历史记录。
	/// </summary>
	internal static bool ShowCategorizedWorldTip(
		string text,
		XjAnnouncementCategory category,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F")
	{
		if (string.IsNullOrWhiteSpace(text)
			|| !IsExplicitCategoryEnabled(category)
			|| !TryAcquireWorldTipSlot(text))
		{
			return false;
		}

		return TryShowWorldTip(text, pause, position, duration, color);
	}

	internal static bool ShowCategorizedWorldTipCritical(
		string text,
		XjAnnouncementCategory category,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		int delayFrames = 0)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| !IsExplicitCategoryEnabled(category)
			|| PendingTips.Count >= PendingMax
			|| PendingTipTexts.Contains(text)
			|| IsSameTextCoolingDown(WorldTipLastByText, text, WorldTipSameTextCooldown))
		{
			return false;
		}

		if (delayFrames <= 0 && Time.unscaledTime - _lastWorldTipTime >= WorldTipGlobalMinInterval)
		{
			RecordRate(WorldTipLastByText, ref _lastWorldTipTime, text, WorldTipSameTextCooldown);
			return TryShowWorldTip(text, pause, position, duration, color);
		}

		int earliestFrame = Time.frameCount + Math.Max(0, delayFrames);
		PendingTips.Enqueue(new PendingTip(text, pause, position, duration, color, earliestFrame, category, null));
		PendingTipTexts.Add(text);
		return true;
	}

	internal static bool ShowWorldTipCritical(string text, bool pause = false, string position = "top", float duration = 10f, string color = "#F3961F", int delayFrames = 0)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| !ShouldShowAnnouncement(text, null)
			|| PendingTips.Count >= PendingMax
			|| PendingTipTexts.Contains(text)
			|| IsSameTextCoolingDown(WorldTipLastByText, text, WorldTipSameTextCooldown))
		{
			return false;
		}

		// S 级关键事件允许在当前帧直接显示；只有与另一条公告撞车时才入队。
		// 这样死亡、成魔、阴司诛邪等一次性事件不会因为后台阶段未轮到而“有历史、无公告”。
		if (delayFrames <= 0 && Time.unscaledTime - _lastWorldTipTime >= WorldTipGlobalMinInterval)
		{
			RecordRate(WorldTipLastByText, ref _lastWorldTipTime, text, WorldTipSameTextCooldown);
			return TryShowWorldTip(text, pause, position, duration, color);
		}

		int earliestFrame = Time.frameCount + Math.Max(0, delayFrames);
		PendingTips.Enqueue(new PendingTip(text, pause, position, duration, color, earliestFrame, XjAnnouncementCategory.Auto, null));
		PendingTipTexts.Add(text);
		return true;
	}

	internal static bool BroadcastActorCriticalWithWorldTip(
		Actor actor,
		string postText,
		string tipText = null,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		int delayFrames = 0,
		string iconId = null)
	{
		bool posted = !string.IsNullOrWhiteSpace(postText) && PostActorCritical(actor, postText, iconId);
		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? postText : tipText;
		if (ShouldShowAnnouncement(resolvedTip, iconId))
		{
			ShowWorldTipCritical(resolvedTip, pause, position, duration, color, delayFrames);
		}
		return posted;
	}

	internal static bool BroadcastBLevelActorEvent(Actor actor, string postText, string tipText = null, string iconId = null)
	{
		bool posted = !string.IsNullOrWhiteSpace(postText) && PostActorCritical(actor, postText, iconId);
		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? postText : tipText;
		if (ShouldSurfaceBLevelAnnouncement(resolvedTip, iconId))
		{
			ShowWorldTipCritical(resolvedTip, delayFrames: 1);
		}
		return posted;
	}

	internal static bool BroadcastSLevelActorEvent(
		Actor actor,
		string postText,
		string tipText = null,
		string color = "#D94C4C",
		float duration = 8f,
		string iconId = null)
	{
		if (string.IsNullOrWhiteSpace(postText))
		{
			return false;
		}

		bool recorded = PostActorCritical(actor, postText, iconId);
		if (!recorded)
		{
			recorded = XjWorldHistoryRegistry.AddWorldEvent(postText, iconId);
		}

		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? postText : tipText;
		if (ShouldShowAnnouncement(resolvedTip, iconId))
		{
			ShowWorldTipCritical(resolvedTip, false, "top", duration, color, delayFrames: 0);
		}
		return recorded;
	}

	internal static bool BroadcastSLevelWorldEvent(
		string historyText,
		string tipText = null,
		string color = "#D94C4C",
		float duration = 8f,
		string iconId = null)
	{
		if (string.IsNullOrWhiteSpace(historyText))
		{
			return false;
		}

		bool recorded = XjWorldHistoryRegistry.AddWorldEvent(historyText, iconId);
		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? historyText : tipText;
		if (ShouldShowAnnouncement(resolvedTip, iconId))
		{
			ShowWorldTipCritical(
				resolvedTip,
				false,
				"top",
				duration,
				color,
				delayFrames: 0);
		}
		return recorded;
	}

	/// <summary>
	/// 写入带明确事件类型的S级天下纪事，并立即进入关键公告队列。
	/// 重要度固定为5且受保护；公告关闭只隐藏WorldTip，不影响史册记录。
	/// </summary>
	internal static bool BroadcastSLevelDomainEvent(
		string category,
		string eventType,
		string historyText,
		string tipText = null,
		long actorId = 0L,
		string actorName = "",
		long relatedActorId = 0L,
		string relatedActorName = "",
		string result = null,
		int year = -1,
		string color = "#D94C4C",
		float duration = 8f,
		string iconId = null)
	{
		if (string.IsNullOrWhiteSpace(historyText) || string.IsNullOrWhiteSpace(eventType))
		{
			return false;
		}

		string normalizedCategory = string.IsNullOrWhiteSpace(category)
			? XjWorldHistoryCategory.World
			: category.Trim();
		string normalizedIconId = string.IsNullOrWhiteSpace(iconId)
			? XjEventIconCatalog.ResolveCategoryIconId(normalizedCategory)
			: XjEventIconCatalog.NormalizeIconId(iconId);

		XjWorldHistoryStore.RecordDomainEvent(
			normalizedCategory,
			historyText,
			historyText,
			importance: 5,
			isProtected: true,
			actorId: actorId,
			actorName: actorName,
			year: year,
			iconIdOverride: normalizedIconId,
			eventType: eventType.Trim(),
			relatedActorId: relatedActorId,
			relatedActorName: relatedActorName,
			result: result,
			mirrorToWorldLog: true);

		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? historyText : tipText;
		if (ShouldShowAnnouncement(resolvedTip, normalizedIconId))
		{
			ShowWorldTipCritical(
				resolvedTip,
				false,
				"top",
				duration,
				color,
				delayFrames: 0);
		}
		return true;
	}

	internal static bool BroadcastBLevelWorldEvent(string text, string iconId = null)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		bool recorded = XjWorldHistoryRegistry.AddWorldEvent(text, iconId);
		if (ShouldSurfaceBLevelAnnouncement(text, iconId))
		{
			ShowWorldTipCritical(text, delayFrames: 1);
		}
		return recorded;
	}

	internal static void TickCriticalAnnouncements()
	{
		int budget = ProcessBudget;
		while (budget-- > 0 && PendingTips.Count > 0)
		{
			PendingTip pending = PendingTips.Peek();
			if (pending.EarliestFrame > Time.frameCount)
			{
				break;
			}

			if (!IsPendingAnnouncementEnabled(pending))
			{
				PendingTips.Dequeue();
				PendingTipTexts.Remove(pending.Text);
				continue;
			}

			if (IsSameTextCoolingDown(WorldTipLastByText, pending.Text, WorldTipSameTextCooldown))
			{
				PendingTips.Dequeue();
				PendingTipTexts.Remove(pending.Text);
				continue;
			}

			// A critical announcement is deferred, not discarded, when another
			// announcement has just consumed the global presentation slot.
			if (Time.unscaledTime - _lastWorldTipTime < WorldTipGlobalMinInterval)
			{
				break;
			}

			PendingTips.Dequeue();
			PendingTipTexts.Remove(pending.Text);
			RecordRate(WorldTipLastByText, ref _lastWorldTipTime, pending.Text, WorldTipSameTextCooldown);
			TryShowWorldTip(pending.Text, pending.Pause, pending.Position, pending.Duration, pending.Color);
		}
	}

	internal static void DiscardPendingCategory(XjAnnouncementCategory category)
	{
		if (PendingTips.Count == 0) return;
		int count = PendingTips.Count;
		List<PendingTip> retained = new List<PendingTip>(count);
		for (int i = 0; i < count; i++)
		{
			PendingTip pending = PendingTips.Dequeue();
			PendingTipTexts.Remove(pending.Text);
			bool matchesCategory = pending.Category == category
				|| category == XjAnnouncementCategory.GongFaWrite
					&& pending.Category == XjAnnouncementCategory.Auto
					&& IsGongFaWriteAnnouncementText(pending.Text, pending.IconId);
			if (!matchesCategory) retained.Add(pending);
		}
		for (int i = 0; i < retained.Count; i++)
		{
			PendingTips.Enqueue(retained[i]);
			PendingTipTexts.Add(retained[i].Text);
		}
	}

	internal static void Clear()
	{
		BroadcastLastByText.Clear();
		WorldTipLastByText.Clear();
		CleanupKeys.Clear();
		PendingTips.Clear();
		PendingTipTexts.Clear();
		_lastBroadcastTime = -9999f;
		_lastWorldTipTime = -9999f;
	}

	private static bool TryShowWorldTip(string text, bool pause, string position, float duration, string color)
	{
		try
		{
			WorldTip.showNow(text, pause, position, duration, color);
			XjAnnouncementHistoryAlignment.OnAnnouncementShown(text);
			return true;
		}
		catch
		{
			Debug.Log("[玄鉴] " + text);
			return false;
		}
	}

	private static bool IsPendingAnnouncementEnabled(in PendingTip pending)
	{
		return pending.Category == XjAnnouncementCategory.Auto
			? ShouldShowAnnouncement(pending.Text, pending.IconId)
			: IsExplicitCategoryEnabled(pending.Category);
	}

	private static bool IsExplicitCategoryEnabled(XjAnnouncementCategory category)
	{
		return category switch
		{
			XjAnnouncementCategory.FamilyInheritance => XjRuntimeSettings.BroadcastFamilyInheritanceEnabled,
			XjAnnouncementCategory.HighRealmInfluence => XjRuntimeSettings.BroadcastHighRealmInfluenceEnabled,
			XjAnnouncementCategory.GongFaWrite => XjRuntimeSettings.BroadcastGongFaWriteEnabled,
			XjAnnouncementCategory.Sect => XjRuntimeSettings.BroadcastSectEnabled,
			_ => true
		};
	}

	private static bool IsGongFaWriteAnnouncementText(string text, string iconId)
	{
		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (IsIcon(icon, XjEventIconCatalog.GongFaAcquire, XjEventIconCatalog.QiuJinFaAcquire)
			|| HasAny(text, "功法写入", "功法入宗", "功法入阁", "功法入库", "功法图录", "上法入谱", "高法归族",
				"求金法入宗", "求金法入阁", "求金法入库", "洞天营造之法入宗"))
		{
			return true;
		}
		return !string.IsNullOrWhiteSpace(text)
			&& text.IndexOf("采气法", StringComparison.Ordinal) >= 0
			&& HasAny(text, "收录", "入库", "入阁", "入宗", "归入", "归族", "写入", "传承", "采气法库", "又添一份");
	}

	internal static bool ShouldShowAnnouncement(string text, string iconId)
	{
		if (HasAny(text, "家主更替", "家族代表", "接过家主印信")) return false;
		if (HasAny(text, "诸房会于族堂", "召开族议", "族议后", "族库扶持", "重点扶持的后辈", "扶持筑基", "继承家业", "族中基业"))
		{
			return XjRuntimeSettings.BroadcastFamilyInheritanceEnabled;
		}
		if (HasAny(text, "上修扶持", "上修定议", "上修干预", "上修力排众议"))
		{
			return XjRuntimeSettings.BroadcastHighRealmInfluenceEnabled;
		}

		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (IsGongFaWriteAnnouncementText(text, icon))
		{
			return XjRuntimeSettings.BroadcastGongFaWriteEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.LianDan, XjEventIconCatalog.HighDanYao, XjEventIconCatalog.DanFangAcquire, XjEventIconCatalog.ZhaLu)
			|| HasAny(text, "炼丹", "丹药", "丹方", "炸炉", "丹成", "开炉"))
		{
			return XjRuntimeSettings.BroadcastTreasureMilestoneEnabled && IsRetainedAlchemyAnnouncement(text, icon);
		}

		if (HasAny(text, "符箓", "制符", "符师", "护身符", "神行符", "破阵符", "破障符", "镇神符", "符纸", "符墨"))
		{
			return false;
		}

		if (HasAny(text, "阵法", "阵纹", "阵师"))
		{
			return false;
		}

		if (IsIcon(icon, XjEventIconCatalog.FaBaoCreation)
			|| HasAny(text, "炼器", "法器", "灵宝", "法宝", "器成", "万宝录"))
		{
			return XjRuntimeSettings.BroadcastTreasureMilestoneEnabled && IsRetainedArtifactAnnouncement(text, icon);
		}

		if (IsIcon(icon, XjEventIconCatalog.YaoCaiAcquire, XjEventIconCatalog.HighYaoCai)
			|| HasAny(text, "药材", "灵药", "上品药材", "采药"))
		{
			return false;
		}

		if (HasAny(text, "瓶颈", "冲关", "破境"))
		{
			return XjRuntimeSettings.BroadcastBottleneckEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.DongTianOpen, XjEventIconCatalog.DongTianClose, XjEventIconCatalog.DongTianDeath)
			|| HasAny(text, "洞天", "秘境显世", "洞门", "显化"))
		{
			return XjRuntimeSettings.BroadcastDongTianEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.YinSiAppear, XjEventIconCatalog.YinSiLeave)
			|| HasAny(text, "阴司", "幽冥府君", "道胎垂眸"))
		{
			return XjRuntimeSettings.BroadcastYinSiEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.ZongMenCreation, XjEventIconCatalog.ZongMenChongTu)
			|| HasAny(text, "宗门", "开宗", "山门", "宣战", "传檄", "另立新宗", "共务", "门禁", "主持", "阶段完成", "都护境", "护境"))
		{
			return XjRuntimeSettings.BroadcastSectEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.HighRealmDeath, XjEventIconCatalog.JinDanFail)
			|| HasAny(text, "魂归天际", "身陨", "寿尽", "求金失败", "冲击紫府未成", "已殁"))
		{
			return XjRuntimeSettings.BroadcastDeathEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.LingWuAppear, XjEventIconCatalog.JinXingLegacy)
			|| HasAny(text, "灵物", "斩龙得灵", "金性遗留"))
		{
			return XjRuntimeSettings.BroadcastLingWuEnabled;
		}

		if (IsIcon(icon, XjEventIconCatalog.ZiFuUpgrade, XjEventIconCatalog.JinDanUpgrade, XjEventIconCatalog.JinDanDemon, XjEventIconCatalog.Jielin, XjEventIconCatalog.RenDan)
			|| HasAny(text, "紫府", "金丹", "神丹", "结璘", "人丹", "权柄之争", "果位"))
		{
			return XjRuntimeSettings.BroadcastHighRealmEnabled;
		}

		return true;
	}

	private static bool IsRetainedAlchemyAnnouncement(string text, string icon)
	{
		return HasAny(text, "延寿丹", "九曜延寿丹");
	}

	private static bool IsRetainedArtifactAnnouncement(string text, string icon)
	{
		return HasAny(text,
			"炼成灵宝",
			"炼成法宝",
			"蜕变为灵宝",
			"蜕变为法宝",
			"赋予灵宝",
			"赋予法宝",
			"紫府灵宝",
			"金丹法宝");
	}

	private static bool ShouldSurfaceBLevelAnnouncement(string text, string iconId)
	{
		if (!ShouldShowAnnouncement(text, iconId))
		{
			return false;
		}

		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		return IsIcon(icon,
			XjEventIconCatalog.ZongMenCreation,
			XjEventIconCatalog.ZongMenChongTu,
			XjEventIconCatalog.DongTianOpen,
			XjEventIconCatalog.DongTianClose,
			XjEventIconCatalog.DongTianDeath,
			XjEventIconCatalog.YinSiAppear,
			XjEventIconCatalog.YinSiLeave,
			XjEventIconCatalog.ZiFuUpgrade,
			XjEventIconCatalog.JinDanUpgrade,
			XjEventIconCatalog.JinDanFail,
			XjEventIconCatalog.Jielin,
			XjEventIconCatalog.HighRealmDeath,
			XjEventIconCatalog.JinDanDemon,
			XjEventIconCatalog.RenDan,
			XjEventIconCatalog.LingWuAppear,
			XjEventIconCatalog.JinXingLegacy,
			XjEventIconCatalog.FaBaoCreation,
			XjEventIconCatalog.HighDanYao);
	}

	private static bool IsIcon(string icon, params string[] candidates)
	{
		if (string.IsNullOrWhiteSpace(icon) || candidates == null) return false;
		for (int i = 0; i < candidates.Length; i++)
		{
			if (string.Equals(icon, XjEventIconCatalog.NormalizeIconId(candidates[i]), StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasAny(string text, params string[] needles)
	{
		if (string.IsNullOrWhiteSpace(text) || needles == null) return false;
		for (int i = 0; i < needles.Length; i++)
		{
			string needle = needles[i];
			if (!string.IsNullOrWhiteSpace(needle) && text.IndexOf(needle, StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool TryAcquireBroadcastSlot(string text, bool ignoreGlobalInterval)
	{
		float now = Time.unscaledTime;
		if ((!ignoreGlobalInterval && now - _lastBroadcastTime < BroadcastGlobalMinInterval)
			|| IsSameTextCoolingDown(BroadcastLastByText, text, BroadcastSameTextCooldown))
		{
			return false;
		}

		RecordRate(BroadcastLastByText, ref _lastBroadcastTime, text, BroadcastSameTextCooldown);
		return true;
	}

	private static bool TryAcquireWorldTipSlot(string text)
	{
		float now = Time.unscaledTime;
		if (now - _lastWorldTipTime < WorldTipGlobalMinInterval
			|| IsSameTextCoolingDown(WorldTipLastByText, text, WorldTipSameTextCooldown))
		{
			return false;
		}

		RecordRate(WorldTipLastByText, ref _lastWorldTipTime, text, WorldTipSameTextCooldown);
		return true;
	}

	private static bool IsSameTextCoolingDown(Dictionary<string, float> cache, string text, float cooldown)
	{
		return cache.TryGetValue(text, out float lastTextTime) && Time.unscaledTime - lastTextTime < cooldown;
	}

	private static void RecordRate(Dictionary<string, float> cache, ref float lastGlobalTime, string text, float sameTextCooldown)
	{
		float now = Time.unscaledTime;
		lastGlobalTime = now;
		cache[text] = now;
		CleanupCache(cache, now, sameTextCooldown);
	}

	private static void CleanupCache(Dictionary<string, float> cache, float now, float sameTextCooldown)
	{
		if (cache.Count <= RateCacheMax)
		{
			return;
		}

		CleanupKeys.Clear();
		float expireTime = now - sameTextCooldown * 2f;
		foreach (KeyValuePair<string, float> entry in cache)
		{
			if (entry.Value < expireTime)
			{
				CleanupKeys.Add(entry.Key);
			}
		}

		for (int i = 0; i < CleanupKeys.Count; i++)
		{
			cache.Remove(CleanupKeys[i]);
		}

		CleanupKeys.Clear();
	}
}


