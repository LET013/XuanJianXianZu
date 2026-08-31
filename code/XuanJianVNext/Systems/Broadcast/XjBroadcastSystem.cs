using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Broadcast;

internal enum XjAnnouncementCategory
{
	Auto = 0,
	FamilyInheritance = 1,
	HighRealmInfluence = 2,
	GongFaWrite = 3,
	Sect = 4,
	HighRealm = 5,
	ShenTong = 6,
	AuthorityPosition = 7,
	LingWu = 8,
	Treasure = 9,
	DongTian = 10,
	Shi = 11,
	System = 12
}

internal static class XjBroadcastSystem
{
	private enum AnnouncementTier
	{
		Routine = 0,
		Major = 1,
		Critical = 2
	}

	private enum AnnouncementHistoryMode
	{
		AlignOrRecord = 0,
		AlreadyRecorded = 1,
		DoNotRecord = 2
	}

	internal static bool HasPendingAnnouncements => PendingTips.Count > 0;
	internal static int PendingAnnouncementCount => PendingTips.Count;
	private const float BroadcastGlobalMinInterval = 0.18f;
	private const float BroadcastSameTextCooldown = 1.2f;
	private const float InteractiveTipGlobalMinInterval = 0.25f;
	private const float AnnouncementGlobalMinInterval = 1.8f;
	private const float WorldTipSameTextCooldown = 12f;
	private const int RateCacheMax = 512;
	private const int PendingMax = 256;
	private const int MaxVisibleAnnouncements = 3;
	private const int RoutinePerWorldYear = 8;
	private const int MajorPerWorldYear = 6;
	private const int RoutinePerCategoryPerWorldYear = 1;
	private const int MajorPerCategoryPerWorldYear = 2;
	private const float RoutineAdmissionWindowSeconds = 10f;
	private const float MajorAdmissionWindowSeconds = 10f;
	private const int RoutineAdmissionWindowMax = 2;
	private const int MajorAdmissionWindowMax = 3;
	private const float NativeWorldLogMinInterval = 0.9f;
	private const float NativeWorldLogWindowSeconds = 10f;
	private const int NativeWorldLogWindowMax = 3;
	private const int NativeWorldLogPerWorldYear = 6;
	private const int NativeWorldLogPerGroupPerWorldYear = 2;

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
		internal readonly AnnouncementTier Tier;
		internal readonly int CreatedYear;
		internal readonly float ExpireAt;
		internal readonly string GroupKey;
		internal readonly AnnouncementHistoryMode HistoryMode;

		internal PendingTip(
			string text,
			bool pause,
			string position,
			float duration,
			string color,
			int earliestFrame,
			XjAnnouncementCategory category,
			string iconId,
			AnnouncementTier tier,
			int createdYear,
			float expireAt,
			string groupKey,
			AnnouncementHistoryMode historyMode)
		{
			Text = text ?? string.Empty;
			Pause = pause;
			Position = string.IsNullOrWhiteSpace(position) ? "top" : position;
			Duration = ClampDuration(duration, tier);
			Color = string.IsNullOrWhiteSpace(color) ? "#F3961F" : color;
			EarliestFrame = earliestFrame;
			Category = category;
			IconId = iconId ?? string.Empty;
			Tier = tier;
			CreatedYear = createdYear;
			ExpireAt = expireAt;
			GroupKey = groupKey ?? string.Empty;
			HistoryMode = historyMode;
		}
	}

	private static readonly Dictionary<string, float> BroadcastLastByText = new Dictionary<string, float>(StringComparer.Ordinal);
	private static readonly Dictionary<string, float> WorldTipLastByText = new Dictionary<string, float>(StringComparer.Ordinal);
	private static readonly List<string> CleanupKeys = new List<string>(64);
	private static readonly List<PendingTip> PendingTips = new List<PendingTip>(24);
	private static readonly HashSet<string> PendingTipTexts = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Queue<float> ActiveAnnouncementExpirations = new Queue<float>(MaxVisibleAnnouncements + 1);
	private static readonly Queue<float> RoutineAdmissionTimes = new Queue<float>(RoutineAdmissionWindowMax + 1);
	private static readonly Queue<float> MajorAdmissionTimes = new Queue<float>(MajorAdmissionWindowMax + 1);
	private static readonly Dictionary<string, int> YearlyCategoryAdmissions = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Queue<float> NativeWorldLogAdmissionTimes = new Queue<float>(NativeWorldLogWindowMax + 1);
	private static readonly Dictionary<string, int> NativeWorldLogYearlyGroups = new Dictionary<string, int>(StringComparer.Ordinal);
	private static float _lastBroadcastTime = -9999f;
	private static float _lastInteractiveTipTime = -9999f;
	private static float _lastAnnouncementTime = -9999f;
	private static int _admissionWorldYear = -1;
	private static int _routineAdmissionsThisYear;
	private static int _majorAdmissionsThisYear;
	private static float _lastNativeWorldLogTime = -9999f;
	private static int _nativeWorldLogYear = -1;
	private static int _nativeWorldLogAdmissionsThisYear;

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
		// 玩家主动操作反馈（收录、入宗等）不进入天下公告年度配额，
		// 但仍保留短间隔和同文冷却，避免同一次点击重复弹出。
		if (string.IsNullOrWhiteSpace(text) || !ShouldShowAnnouncement(text, null) || !TryAcquireInteractiveTipSlot(text))
		{
			return false;
		}

		return TryShowWorldTip(text, pause, position, duration, color, AnnouncementHistoryMode.DoNotRecord, null);
	}

	/// <summary>
	/// 显式公告分类入口。家族传承与上修干预不再依赖文本关键词猜测。
	/// 该入口属于常规天下公告，受统一屏幕容量、年度配额与实时窗口约束。
	/// </summary>
	internal static bool ShowCategorizedWorldTip(
		string text,
		XjAnnouncementCategory category,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		string iconId = null)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames: 1,
			category,
			iconId,
			AnnouncementTier.Routine);
	}

	internal static bool ShowRecordedCategorizedWorldTip(
		string text,
		XjAnnouncementCategory category,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		string iconId = null)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames: 1,
			category,
			iconId,
			AnnouncementTier.Routine,
			AnnouncementHistoryMode.AlreadyRecorded);
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
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames,
			category,
			iconId: null,
			AnnouncementTier.Critical);
	}

	internal static bool ShowRecordedCategorizedWorldTipCritical(
		string text,
		XjAnnouncementCategory category,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		int delayFrames = 0,
		string iconId = null)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames,
			category,
			iconId,
			AnnouncementTier.Critical,
			AnnouncementHistoryMode.AlreadyRecorded);
	}

	internal static bool ShowWorldTipCritical(string text, bool pause = false, string position = "top", float duration = 10f, string color = "#F3961F", int delayFrames = 0)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames,
			XjAnnouncementCategory.Auto,
			iconId: null,
			AnnouncementTier.Critical);
	}

	internal static bool ShowRecordedWorldTip(
		string text,
		bool pause = false,
		string position = "top",
		float duration = 6f,
		string color = "#F3961F",
		string iconId = null)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames: 1,
			XjAnnouncementCategory.Auto,
			iconId,
			AnnouncementTier.Routine,
			AnnouncementHistoryMode.AlreadyRecorded);
	}

	internal static bool ShowRecordedWorldTipCritical(
		string text,
		bool pause = false,
		string position = "top",
		float duration = 10f,
		string color = "#F3961F",
		int delayFrames = 0,
		string iconId = null)
	{
		return TryScheduleAnnouncement(
			text,
			pause,
			position,
			duration,
			color,
			delayFrames,
			XjAnnouncementCategory.Auto,
			iconId,
			AnnouncementTier.Critical,
			AnnouncementHistoryMode.AlreadyRecorded);
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
		TryScheduleAnnouncement(
			resolvedTip,
			pause,
			position,
			duration,
			color,
			delayFrames,
			XjAnnouncementCategory.Auto,
			iconId,
			AnnouncementTier.Critical,
			AnnouncementHistoryMode.AlreadyRecorded);
		return posted;
	}

	internal static bool BroadcastBLevelActorEvent(
		Actor actor,
		string postText,
		string tipText = null,
		string iconId = null,
		XjAnnouncementCategory category = XjAnnouncementCategory.Auto)
	{
		bool posted = !string.IsNullOrWhiteSpace(postText) && PostActorCritical(actor, postText, iconId);
		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? postText : tipText;
		if (category == XjAnnouncementCategory.Auto && ShouldSurfaceBLevelAnnouncement(resolvedTip, iconId))
		{
			TryScheduleAnnouncement(resolvedTip, false, "top", 6f, "#F3961F", 1, category, iconId, AnnouncementTier.Routine, AnnouncementHistoryMode.AlreadyRecorded);
		}
		else if (category != XjAnnouncementCategory.Auto
			&& IsExplicitCategoryEnabled(category)
			&& IsExplicitRoutineSurfaceCategory(category, resolvedTip, iconId))
		{
			TryScheduleAnnouncement(resolvedTip, false, "top", 6f, "#F3961F", 1, category, iconId, AnnouncementTier.Routine, AnnouncementHistoryMode.AlreadyRecorded);
		}
		return posted;
	}

	internal static bool BroadcastSLevelActorEvent(
		Actor actor,
		string postText,
		string tipText = null,
		string color = "#D94C4C",
		float duration = 8f,
		string iconId = null,
		XjAnnouncementCategory announcementCategory = XjAnnouncementCategory.Auto)
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
		if (!IsPersistedFrequentAnnouncement(resolvedTip, iconId) || IsProtectedCriticalAnnouncement(resolvedTip, iconId))
		{
			TryScheduleAnnouncement(resolvedTip, false, "top", duration, color, 0, announcementCategory, iconId, AnnouncementTier.Critical, AnnouncementHistoryMode.AlreadyRecorded);
		}
		return recorded;
	}

	internal static bool BroadcastSLevelWorldEvent(
		string historyText,
		string tipText = null,
		string color = "#D94C4C",
		float duration = 8f,
		string iconId = null,
		XjAnnouncementCategory announcementCategory = XjAnnouncementCategory.Auto)
	{
		if (string.IsNullOrWhiteSpace(historyText))
		{
			return false;
		}

		bool recorded = XjWorldHistoryRegistry.AddWorldEvent(historyText, iconId);
		string resolvedTip = string.IsNullOrWhiteSpace(tipText) ? historyText : tipText;
		if (!IsPersistedFrequentAnnouncement(resolvedTip, iconId) || IsProtectedCriticalAnnouncement(resolvedTip, iconId))
		{
			TryScheduleAnnouncement(resolvedTip, false, "top", duration, color, 0, announcementCategory, iconId, AnnouncementTier.Critical, AnnouncementHistoryMode.AlreadyRecorded);
		}
		return recorded;
	}

	/// <summary>
	/// 写入带明确事件类型的S级天下纪事。WorldTip仍需通过统一公告治理，
	/// 不再因为标记为S级就绕过屏幕容量和过期规则。
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
		string iconId = null,
		XjAnnouncementCategory announcementCategory = XjAnnouncementCategory.Auto)
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
		if (!IsPersistedFrequentAnnouncement(resolvedTip, normalizedIconId) || IsProtectedCriticalAnnouncement(resolvedTip, normalizedIconId))
		{
			TryScheduleAnnouncement(resolvedTip, false, "top", duration, color, 0, announcementCategory, normalizedIconId, AnnouncementTier.Critical, AnnouncementHistoryMode.AlreadyRecorded);
		}
		return true;
	}

	internal static bool BroadcastBLevelWorldEvent(
		string text,
		string iconId = null,
		XjAnnouncementCategory category = XjAnnouncementCategory.Auto)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return false;
		}

		bool recorded = XjWorldHistoryRegistry.AddWorldEvent(text, iconId);
		if (category == XjAnnouncementCategory.Auto && ShouldSurfaceBLevelAnnouncement(text, iconId))
		{
			TryScheduleAnnouncement(text, false, "top", 6f, "#F3961F", 1, category, iconId, AnnouncementTier.Routine, AnnouncementHistoryMode.AlreadyRecorded);
		}
		else if (category != XjAnnouncementCategory.Auto
			&& IsExplicitCategoryEnabled(category)
			&& IsExplicitRoutineSurfaceCategory(category, text, iconId))
		{
			TryScheduleAnnouncement(text, false, "top", 6f, "#F3961F", 1, category, iconId, AnnouncementTier.Routine, AnnouncementHistoryMode.AlreadyRecorded);
		}
		return recorded;
	}

	internal static void TickCriticalAnnouncements()
	{
		PruneActiveAnnouncements();
		PruneExpiredPendingAnnouncements();
		if (PendingTips.Count == 0 || !CanShowAnnouncementNow())
		{
			return;
		}

		int selectedIndex = SelectNextPendingIndex();
		if (selectedIndex < 0)
		{
			return;
		}

		PendingTip pending = PendingTips[selectedIndex];
		if (!IsPendingAnnouncementEnabled(pending)
			|| IsSameTextCoolingDown(WorldTipLastByText, pending.Text, WorldTipSameTextCooldown))
		{
			PendingTips.RemoveAt(selectedIndex);
			PendingTipTexts.Remove(BuildAnnouncementSignature(pending.Text));
			return;
		}

		// Presentation/UI 尚未 Ready 时不再把公告从队列先删掉。半初始化或载入期
		// TryShowWorldTip 失败只会延后重试；真正展示成功后才消费该公告。
		if (ShowScheduledAnnouncement(pending))
		{
			PendingTips.RemoveAt(selectedIndex);
			PendingTipTexts.Remove(BuildAnnouncementSignature(pending.Text));
		}
	}

	internal static void DiscardPendingCategory(XjAnnouncementCategory category)
	{
		for (int i = PendingTips.Count - 1; i >= 0; i--)
		{
			PendingTip pending = PendingTips[i];
			bool matchesCategory = pending.Category == category
				|| pending.Category == XjAnnouncementCategory.Auto
					&& ResolveConfiguredCategory(pending.Text, pending.IconId) == category;
			if (!matchesCategory) continue;
			PendingTipTexts.Remove(BuildAnnouncementSignature(pending.Text));
			PendingTips.RemoveAt(i);
		}
	}

	internal static void Clear()
	{
		BroadcastLastByText.Clear();
		WorldTipLastByText.Clear();
		CleanupKeys.Clear();
		PendingTips.Clear();
		PendingTipTexts.Clear();
		ActiveAnnouncementExpirations.Clear();
		RoutineAdmissionTimes.Clear();
		MajorAdmissionTimes.Clear();
		YearlyCategoryAdmissions.Clear();
		NativeWorldLogAdmissionTimes.Clear();
		NativeWorldLogYearlyGroups.Clear();
		_lastBroadcastTime = -9999f;
		_lastInteractiveTipTime = -9999f;
		_lastAnnouncementTime = -9999f;
		_admissionWorldYear = -1;
		_routineAdmissionsThisYear = 0;
		_majorAdmissionsThisYear = 0;
		_lastNativeWorldLogTime = -9999f;
		_nativeWorldLogYear = -1;
		_nativeWorldLogAdmissionsThisYear = 0;
	}

	private static bool TryScheduleAnnouncement(
		string text,
		bool pause,
		string position,
		float duration,
		string color,
		int delayFrames,
		XjAnnouncementCategory category,
		string iconId,
		AnnouncementTier tier,
		AnnouncementHistoryMode historyMode = AnnouncementHistoryMode.AlignOrRecord)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| !IsAnnouncementEnabled(text, iconId, category, tier)
			|| PendingTipTexts.Contains(BuildAnnouncementSignature(text))
			|| IsSameTextCoolingDown(WorldTipLastByText, text, WorldTipSameTextCooldown))
		{
			return false;
		}

		// 历史上部分生产者把金丹、真君、果位、道胎等关键事件错误走到了 Major/Routine。
		// 保护词命中时在唯一公告边界自动升为 Critical，避免高速世界下被普通限流吞掉。
		if (tier != AnnouncementTier.Critical && IsProtectedCriticalAnnouncement(text, iconId))
		{
			tier = AnnouncementTier.Critical;
		}

		if (tier != AnnouncementTier.Critical && IsPersistedFrequentAnnouncement(text, iconId))
		{
			return false;
		}

		// Popup presentation can lag behind deferred annual semantics at high speed.
		// Preserve the transaction year on the queued tip so an already-recorded
		// 501 event processed at live year 509 can be recognized as stale UI debt.
		int currentYear = XjAnnualExecutionContext.HasActiveYear
			? XjAnnualExecutionContext.CurrentYear
			: ResolveCurrentWorldYear();
		int liveYear = ResolveCurrentWorldYear();
		int maxPresentationAge = ResolvePresentationWorldAgeLimit(tier, historyMode);
		if (maxPresentationAge != int.MaxValue
			&& currentYear > 0
			&& liveYear > currentYear
			&& liveYear - currentYear > maxPresentationAge)
		{
			// The authoritative event/history is already written (or explicitly does
			// not require history). Do not consume UI admission quota for a stale popup.
			return true;
		}

		string groupKey = ResolveAnnouncementGroup(text, iconId, category);
		if (!TryAdmitAnnouncement(tier, groupKey))
		{
			return false;
		}

		float now = Time.unscaledTime;
		float expireAfter = tier switch
		{
			AnnouncementTier.Routine => 8f,
			AnnouncementTier.Major => 18f,
			_ => float.PositiveInfinity
		};
		PendingTip pending = new PendingTip(
			text,
			pause,
			position,
			duration,
			color,
			Time.frameCount + Math.Max(0, delayFrames),
			category,
			iconId,
			tier,
			currentYear,
			now + expireAfter,
			groupKey,
			historyMode);

		if (delayFrames <= 0 && CanShowAnnouncementNow() && ShowScheduledAnnouncement(pending))
		{
			return true;
		}

		if (PendingTips.Count >= PendingMax)
		{
			int removable = FindLowestPriorityPendingIndex();
			if (removable < 0 || PendingTips[removable].Tier >= tier)
			{
				return false;
			}
			PendingTipTexts.Remove(BuildAnnouncementSignature(PendingTips[removable].Text));
			PendingTips.RemoveAt(removable);
		}

		PendingTips.Add(pending);
		PendingTipTexts.Add(BuildAnnouncementSignature(text));
		return true;
	}

	private static bool ShowScheduledAnnouncement(in PendingTip pending)
	{
		if (!CanShowAnnouncementNow())
		{
			return false;
		}

		bool shown = TryShowWorldTip(
			pending.Text,
			pending.Pause,
			pending.Position,
			pending.Duration,
			pending.Color,
			pending.HistoryMode,
			pending.IconId);
		if (shown)
		{
			// Presentation 尚未 Ready 时 TryShowWorldTip 会失败；失败重试不能提前写入
			// 同文案冷却，否则下一帧 Tick 会把仍未显示的关键公告当成重复项删除。
			RecordRate(WorldTipLastByText, ref _lastAnnouncementTime, pending.Text, WorldTipSameTextCooldown);
			ActiveAnnouncementExpirations.Enqueue(Time.unscaledTime + pending.Duration);
		}
		return shown;
	}

	private static bool TryShowWorldTip(
		string text,
		bool pause,
		string position,
		float duration,
		string color,
		AnnouncementHistoryMode historyMode,
		string iconId)
	{
		bool shown = XjPresentationHooks.TryShowWorldTip(text, pause, position, duration, color);
		if (!shown)
		{
			Debug.Log("[玄鉴] " + text);
			return false;
		}
		if (historyMode != AnnouncementHistoryMode.DoNotRecord)
		{
			// “已记录”也做一次同年对账：已有结构化记录只补天下可见性，
			// 若事件生产者漏写史册，则由最后一道对齐门补齐，避免公告与玄鉴历史分叉。
			XjAnnouncementHistoryAlignment.OnAnnouncementShown(text, iconId);
		}
		return true;
	}

	private static bool IsAnnouncementEnabled(
		string text,
		string iconId,
		XjAnnouncementCategory category,
		AnnouncementTier tier)
	{
		if (!ShouldSurfaceTopAnnouncement(text, iconId, category, tier)) return false;
		XjAnnouncementCategory resolved = category == XjAnnouncementCategory.Auto
			? ResolveConfiguredCategory(text, iconId)
			: category;
		return resolved == XjAnnouncementCategory.Auto
			? ShouldShowAnnouncement(text, iconId)
			: IsExplicitCategoryEnabled(resolved);
	}

	/// <summary>
	/// 顶部天下公告只承担“改变人物位格、势力格局或世界机缘”的信息。
	/// 普通成长仍完整写入仙鉴与三书，但不再因为生产者调用了 WorldTip 就抢占屏幕。
	/// </summary>
	private static bool ShouldSurfaceTopAnnouncement(
		string text,
		string iconId,
		XjAnnouncementCategory category,
		AnnouncementTier tier)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;

		// 明确降级为履历/史册的高频事项。
		if (HasAny(text,
			"神通初成", "神通齐成", "神通易象", "神通退显", "旧档易象",
			"冲击紫府未成", "突破紫府失败", "紫府突破失败",
			"清静照命", "一念垂化", "照命无门", "缘来即照",
			"宗门大比", "山门大比", "三年大比",
			"瓶颈", "冲关"))
		{
			return false;
		}

		XjAnnouncementCategory resolved = category == XjAnnouncementCategory.Auto
			? ResolveConfiguredCategory(text, iconId)
			: category;
		switch (resolved)
		{
			case XjAnnouncementCategory.FamilyInheritance:
			case XjAnnouncementCategory.GongFaWrite:
			case XjAnnouncementCategory.ShenTong:
				return false;
			case XjAnnouncementCategory.HighRealmInfluence:
			case XjAnnouncementCategory.LingWu:
			case XjAnnouncementCategory.DongTian:
				return true;
			case XjAnnouncementCategory.Treasure:
				return IsRetainedAlchemyAnnouncement(text, XjEventIconCatalog.NormalizeIconId(iconId))
					|| IsRetainedArtifactAnnouncement(text, XjEventIconCatalog.NormalizeIconId(iconId));
			case XjAnnouncementCategory.Sect:
				return IsImportantSectAnnouncement(text);
			case XjAnnouncementCategory.Shi:
				return IsImportantShiAnnouncement(text);
			case XjAnnouncementCategory.HighRealm:
				return IsImportantHighRealmAnnouncement(text, iconId);
			case XjAnnouncementCategory.AuthorityPosition:
				return tier == AnnouncementTier.Critical || HasAny(text,
					"权柄之争", "夺正", "正位承继", "果位封锁", "果位嬗变", "空证",
					"余位开辟", "闰位开辟", "权柄易位", "权柄裂解");
		}

		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (IsIcon(icon, XjEventIconCatalog.ZiFuUpgrade, XjEventIconCatalog.JinDanUpgrade,
			XjEventIconCatalog.JinDanFail, XjEventIconCatalog.Jielin, XjEventIconCatalog.JinDanDemon,
			XjEventIconCatalog.RenDan, XjEventIconCatalog.HighRealmDeath))
		{
			return IsImportantHighRealmAnnouncement(text, iconId);
		}
		if (IsIcon(icon, XjEventIconCatalog.ZongMenCreation, XjEventIconCatalog.ZongMenChongTu))
		{
			return IsImportantSectAnnouncement(text);
		}

		// 其余未分类事件只有生产者明确标为 Critical 时才占用顶部公告。
		return tier == AnnouncementTier.Critical;
	}

	private static bool IsImportantHighRealmAnnouncement(string text, string iconId)
	{
		if (HasAny(text, "冲击紫府未成", "突破紫府失败", "紫府突破失败")) return false;
		return HasAny(text,
			"证得【", "登临真人", "成就金丹", "金丹真君", "真君羽士", "晋位真君",
			"道胎成就", "成就道胎", "人丹", "空证", "结璘", "郁仪", "神丹",
			"正位承继", "夺正", "正受易位", "以裨继主", "玄置夺君", "道统中兴",
			"求金失败", "金性成魔", "身陨", "陨落", "寿尽", "魂归天际");
	}

	private static bool IsImportantSectAnnouncement(string text)
	{
		if (HasAny(text, "宗门大比", "山门大比", "三年大比", "灭宗收缴", "宗门任务", "阶段完成", "大阵维护"))
			return false;
		return HasAny(text,
			"开山立派", "创立【", "开宗", "另立山门", "另立新宗",
			"宗门宣战", "宣战", "传檄", "破阵灭宗", "宗门衰亡", "宗门覆灭",
			"久攻不下", "两宗同毁", "同归于尽");
	}

	private static bool IsImportantShiAnnouncement(string text)
	{
		return HasAny(text,
			"【金地应身】", "【摩诃不退】", "【应身圆成】", "【法相应土】",
			"【古释世尊】", "【今释世尊】", "【摩诃转世】", "【庙主得地】",
			"【玄天重成】", "【斩灭真灵】", "【真灵俱灭】", "【真灵归土】",
			"【应身苏醒】");
	}

	private static bool CanShowAnnouncementNow()
	{
		PruneActiveAnnouncements();
		return ActiveAnnouncementExpirations.Count < MaxVisibleAnnouncements
			&& Time.unscaledTime - _lastAnnouncementTime >= AnnouncementGlobalMinInterval;
	}

	private static void PruneActiveAnnouncements()
	{
		float now = Time.unscaledTime;
		while (ActiveAnnouncementExpirations.Count > 0 && ActiveAnnouncementExpirations.Peek() <= now)
		{
			ActiveAnnouncementExpirations.Dequeue();
		}
	}

	private static void PruneExpiredPendingAnnouncements()
	{
		int currentYear = ResolveCurrentWorldYear();
		float now = Time.unscaledTime;
		for (int i = PendingTips.Count - 1; i >= 0; i--)
		{
			PendingTip pending = PendingTips[i];
			int maxWorldAge = ResolvePresentationWorldAgeLimit(pending.Tier, pending.HistoryMode);
			bool expiredByTime = now > pending.ExpireAt;
			bool expiredByYear = pending.Tier != AnnouncementTier.Critical
				&& currentYear > 0 && pending.CreatedYear > 0 && currentYear - pending.CreatedYear > maxWorldAge;
			if (!expiredByTime && !expiredByYear && IsPendingAnnouncementEnabled(pending)) continue;
			PendingTipTexts.Remove(BuildAnnouncementSignature(pending.Text));
			PendingTips.RemoveAt(i);
		}
	}

	private static int SelectNextPendingIndex()
	{
		int selected = -1;
		for (int i = 0; i < PendingTips.Count; i++)
		{
			PendingTip candidate = PendingTips[i];
			if (candidate.EarliestFrame > Time.frameCount) continue;
			if (selected < 0)
			{
				selected = i;
				continue;
			}
			PendingTip current = PendingTips[selected];
			if (candidate.Tier > current.Tier
				|| candidate.Tier == current.Tier && candidate.EarliestFrame < current.EarliestFrame)
			{
				selected = i;
			}
		}
		return selected;
	}

	private static int FindLowestPriorityPendingIndex()
	{
		int selected = -1;
		for (int i = 0; i < PendingTips.Count; i++)
		{
			if (selected < 0 || PendingTips[i].Tier < PendingTips[selected].Tier
				|| PendingTips[i].Tier == PendingTips[selected].Tier && PendingTips[i].EarliestFrame > PendingTips[selected].EarliestFrame)
			{
				selected = i;
			}
		}
		return selected;
	}

	private static bool TryAdmitAnnouncement(AnnouncementTier tier, string groupKey)
	{
		if (tier == AnnouncementTier.Critical)
		{
			return true;
		}

		int currentYear = ResolveCurrentWorldYear();
		if (_admissionWorldYear != currentYear)
		{
			_admissionWorldYear = currentYear;
			_routineAdmissionsThisYear = 0;
			_majorAdmissionsThisYear = 0;
			YearlyCategoryAdmissions.Clear();
		}

		float now = Time.unscaledTime;
		Queue<float> rolling = tier == AnnouncementTier.Routine ? RoutineAdmissionTimes : MajorAdmissionTimes;
		float window = tier == AnnouncementTier.Routine ? RoutineAdmissionWindowSeconds : MajorAdmissionWindowSeconds;
		int rollingMax = tier == AnnouncementTier.Routine ? RoutineAdmissionWindowMax : MajorAdmissionWindowMax;
		while (rolling.Count > 0 && now - rolling.Peek() >= window)
		{
			rolling.Dequeue();
		}
		if (rolling.Count >= rollingMax)
		{
			return false;
		}

		int yearlyTotal = tier == AnnouncementTier.Routine ? _routineAdmissionsThisYear : _majorAdmissionsThisYear;
		int yearlyMax = tier == AnnouncementTier.Routine ? RoutinePerWorldYear : MajorPerWorldYear;
		if (currentYear > 0 && yearlyTotal >= yearlyMax)
		{
			return false;
		}

		string categoryKey = ((int)tier).ToString() + "|" + groupKey;
		YearlyCategoryAdmissions.TryGetValue(categoryKey, out int categoryCount);
		int categoryMax = ResolveCategoryYearlyMax(tier, groupKey);
		if (currentYear > 0 && categoryCount >= categoryMax)
		{
			return false;
		}

		rolling.Enqueue(now);
		if (tier == AnnouncementTier.Routine) _routineAdmissionsThisYear++;
		else _majorAdmissionsThisYear++;
		YearlyCategoryAdmissions[categoryKey] = categoryCount + 1;
		return true;
	}

	private static string ResolveAnnouncementGroup(string text, string iconId, XjAnnouncementCategory category)
	{
		XjAnnouncementCategory resolvedCategory = category == XjAnnouncementCategory.Auto
			? ResolveConfiguredCategory(text, iconId)
			: category;
		if (resolvedCategory != XjAnnouncementCategory.Auto) return resolvedCategory.ToString();
		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (IsHighRealmAnnouncement(text, icon)) return "HighRealm";
		if (IsIcon(icon, XjEventIconCatalog.HighRealmDeath, XjEventIconCatalog.JinDanFail) || HasAny(text, "身陨", "陨落", "寿尽", "魂归")) return "Death";
		if (IsIcon(icon, XjEventIconCatalog.ZongMenCreation, XjEventIconCatalog.ZongMenChongTu) || HasAny(text, "宗门", "开宗", "灭宗", "山门")) return "Sect";
		if (IsIcon(icon, XjEventIconCatalog.DongTianOpen, XjEventIconCatalog.DongTianClose, XjEventIconCatalog.DongTianDeath) || HasAny(text, "洞天", "秘境")) return "DongTian";
		if (IsIcon(icon, XjEventIconCatalog.YinSiAppear, XjEventIconCatalog.YinSiLeave) || HasAny(text, "阴司", "幽冥")) return "YinSi";
		if (IsIcon(icon, XjEventIconCatalog.LingWuAppear, XjEventIconCatalog.JinXingLegacy) || HasAny(text, "灵物", "金性")) return "LingWu";
		if (IsIcon(icon, XjEventIconCatalog.FaBaoCreation, XjEventIconCatalog.HighDanYao) || HasAny(text, "灵宝", "法宝", "丹药")) return "Treasure";
		if (IsGongFaWriteAnnouncementText(text, icon)) return "GongFa";
		return "World";
	}

	private static int ResolvePresentationWorldAgeLimit(
		AnnouncementTier tier,
		AnnouncementHistoryMode historyMode)
	{
		// AlignOrRecord still uses successful presentation as its fallback history
		// alignment boundary, so never discard it merely because world years advanced.
		// AlreadyRecorded / DoNotRecord presentation debt is safe to compact.
		if (historyMode == AnnouncementHistoryMode.AlignOrRecord) return int.MaxValue;
		return tier switch
		{
			AnnouncementTier.Routine => 0,
			AnnouncementTier.Major => 1,
			AnnouncementTier.Critical => 2,
			_ => int.MaxValue
		};
	}

	private static int ResolveCurrentWorldYear()
	{
		int tracked = Math.Max(0, XjYearTracker.CurrentYear);
		int live = Math.Max(0, World.world?.map_stats?.year ?? 0);
		return Math.Max(tracked, live);
	}

	private static float ClampDuration(float duration, AnnouncementTier tier)
	{
		float requested = duration <= 0f ? 6f : duration;
		float maximum = tier switch
		{
			AnnouncementTier.Routine => 5.5f,
			AnnouncementTier.Major => 7f,
			_ => 8f
		};
		return requested > maximum ? maximum : requested;
	}

	private static bool IsPendingAnnouncementEnabled(in PendingTip pending)
	{
		XjAnnouncementCategory resolved = pending.Category == XjAnnouncementCategory.Auto
			? ResolveConfiguredCategory(pending.Text, pending.IconId)
			: pending.Category;
		return ShouldSurfaceTopAnnouncement(pending.Text, pending.IconId, pending.Category, pending.Tier)
			&& (resolved == XjAnnouncementCategory.Auto
				? ShouldShowAnnouncement(pending.Text, pending.IconId)
				: IsExplicitCategoryEnabled(resolved));
	}

	private static bool IsExplicitCategoryEnabled(XjAnnouncementCategory category)
	{
		return category switch
		{
			XjAnnouncementCategory.FamilyInheritance => XjRuntimeSettings.BroadcastFamilyInheritanceEnabled,
			XjAnnouncementCategory.HighRealmInfluence => XjRuntimeSettings.BroadcastHighRealmInfluenceEnabled,
			XjAnnouncementCategory.GongFaWrite => XjRuntimeSettings.BroadcastGongFaWriteEnabled,
			XjAnnouncementCategory.Sect => XjRuntimeSettings.BroadcastSectEnabled,
			XjAnnouncementCategory.HighRealm => XjRuntimeSettings.BroadcastHighRealmEnabled,
			XjAnnouncementCategory.ShenTong => XjRuntimeSettings.BroadcastShenTongEnabled,
			XjAnnouncementCategory.AuthorityPosition => XjRuntimeSettings.BroadcastAuthorityPositionEnabled,
			XjAnnouncementCategory.LingWu => XjRuntimeSettings.BroadcastLingWuEnabled,
			XjAnnouncementCategory.Treasure => XjRuntimeSettings.BroadcastTreasureMilestoneEnabled,
			XjAnnouncementCategory.DongTian => XjRuntimeSettings.BroadcastDongTianEnabled,
			XjAnnouncementCategory.Shi => XjRuntimeSettings.BroadcastShiEnabled,
			_ => true
		};
	}

	private static XjAnnouncementCategory ResolveConfiguredCategory(string text, string iconId)
	{
		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (HasAny(text, "神通易象", "神通退显", "旧档易象", "神通初成", "修出神通", "悟出神通", "神通修成"))
			return XjAnnouncementCategory.ShenTong;
		if (HasAny(text,
			"权柄归身", "权柄离身", "权柄易位", "权柄显化", "权柄归显", "权柄失落", "权柄裂解", "权柄潜藏",
			"余位开辟", "闰位开辟", "位序扩充", "余位可开至", "闰位可开至"))
			return XjAnnouncementCategory.AuthorityPosition;
		if (IsIcon(icon, XjEventIconCatalog.LingWuAppear, XjEventIconCatalog.JinXingLegacy)
			|| HasAny(text, "灵物现世", "寻得灵物", "斩龙得灵", "金性遗留", "金性产出", "灵物入库"))
			return XjAnnouncementCategory.LingWu;
		if (IsIcon(icon, XjEventIconCatalog.FaBaoCreation, XjEventIconCatalog.HighDanYao)
			|| IsRetainedAlchemyAnnouncement(text, icon)
			|| IsRetainedArtifactAnnouncement(text, icon))
			return XjAnnouncementCategory.Treasure;
		if (IsIcon(icon, XjEventIconCatalog.DongTianOpen, XjEventIconCatalog.DongTianClose, XjEventIconCatalog.DongTianDeath)
			|| HasAny(text, "洞天显世", "洞天开启", "洞天关闭", "洞门复闭", "此番显世已尽", "洞天归属"))
			return XjAnnouncementCategory.DongTian;
		return XjAnnouncementCategory.Auto;
	}

	private static bool IsExplicitRoutineSurfaceCategory(XjAnnouncementCategory category, string text, string iconId)
	{
		if (!IsExplicitCategoryEnabled(category)) return false;
		return category == XjAnnouncementCategory.ShenTong
			|| category == XjAnnouncementCategory.AuthorityPosition
			|| category == XjAnnouncementCategory.LingWu
			|| category == XjAnnouncementCategory.Treasure
			|| category == XjAnnouncementCategory.DongTian
			|| category == XjAnnouncementCategory.Shi
			|| IsBLevelSurfaceIcon(iconId)
			|| IsProtectedCriticalAnnouncement(text, iconId);
	}

	private static int ResolveCategoryYearlyMax(AnnouncementTier tier, string groupKey)
	{
		if (tier == AnnouncementTier.Critical) return int.MaxValue;
		if (string.Equals(groupKey, XjAnnouncementCategory.ShenTong.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 2 : 3;
		if (string.Equals(groupKey, XjAnnouncementCategory.AuthorityPosition.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 3 : 4;
		if (string.Equals(groupKey, XjAnnouncementCategory.LingWu.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 2 : 3;
		if (string.Equals(groupKey, XjAnnouncementCategory.Treasure.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 2 : 3;
		if (string.Equals(groupKey, XjAnnouncementCategory.DongTian.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 2 : 3;
		if (string.Equals(groupKey, XjAnnouncementCategory.Shi.ToString(), StringComparison.Ordinal)) return tier == AnnouncementTier.Routine ? 3 : 5;
		return tier == AnnouncementTier.Routine ? RoutinePerCategoryPerWorldYear : MajorPerCategoryPerWorldYear;
	}

	internal static bool AnnounceShenTongComprehended(Actor actor, string shenTongName, string source = null)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(shenTongName)) return false;
		string cause = string.IsNullOrWhiteSpace(source) ? "参悟所修功法" : source.Trim();
		string text = "【神通初成】" + actor.getName() + "因" + cause + "，修成神通【" + shenTongName.Trim() + "】。";
		return ShowCategorizedWorldTip(text, XjAnnouncementCategory.ShenTong, duration: 5.5f, color: "#7EA6D8");
	}

	internal static bool AnnounceShenTongBatch(Actor actor, IReadOnlyList<string> shenTongNames, string cause)
	{
		if (actor?.data == null || shenTongNames == null || shenTongNames.Count == 0) return false;
		List<string> names = new List<string>();
		for (int i = 0; i < shenTongNames.Count && names.Count < 5; i++)
		{
			string name = (shenTongNames[i] ?? string.Empty).Trim();
			if (name.Length > 0) names.Add("【" + name + "】");
		}
		if (names.Count == 0) return false;
		string text = "【神通齐成】" + actor.getName() + "因" + (string.IsNullOrWhiteSpace(cause) ? "道法蜕变" : cause.Trim())
			+ "，一并修成" + string.Join("、", names) + "。";
		return ShowCategorizedWorldTipCritical(text, XjAnnouncementCategory.ShenTong, duration: 6.5f, color: "#7EA6D8", delayFrames: 1);
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
		if (HasAny(text,
			"神通初成", "神通齐成", "神通易象", "神通退显", "旧档易象",
			"冲击紫府未成", "突破紫府失败", "紫府突破失败",
			"清静照命", "一念垂化", "照命无门", "缘来即照",
			"宗门大比", "山门大比", "三年大比", "瓶颈", "冲关")) return false;

		XjAnnouncementCategory configured = ResolveConfiguredCategory(text, iconId);
		if (configured != XjAnnouncementCategory.Auto)
		{
			return IsExplicitCategoryEnabled(configured);
		}
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

		if (IsHighRealmAnnouncement(text, icon))
		{
			return XjRuntimeSettings.BroadcastHighRealmEnabled;
		}

		return true;
	}

	/// <summary>
	/// 是否把玄鉴事件继续投影到原生左侧世界日志。仙鉴与三书记录在此之前已经完成，
	/// 因而返回 false 只减少屏幕占用，不会丢失历史。普通年度成果、神通易象、
	/// 权柄归离和资源产出默认不再进入原生日志。
	/// </summary>
	internal static bool ShouldMirrorToNativeWorldLog(string text, string iconId)
	{
		if (string.IsNullOrWhiteSpace(text) || !ShouldShowAnnouncement(text, iconId))
		{
			return false;
		}

		bool protectedCritical = IsProtectedCriticalAnnouncement(text, iconId);
		if (IsPersistedFrequentAnnouncement(text, iconId) && !protectedCritical)
		{
			return false;
		}

		// 没有明确重大语义、也不属于允许展示的B级图标时，只留玄鉴内部史册。
		if (!protectedCritical && !IsBLevelSurfaceIcon(iconId))
		{
			return false;
		}

		float now = Time.unscaledTime;
		if (now - _lastNativeWorldLogTime < NativeWorldLogMinInterval)
		{
			return false;
		}
		while (NativeWorldLogAdmissionTimes.Count > 0
			&& now - NativeWorldLogAdmissionTimes.Peek() >= NativeWorldLogWindowSeconds)
		{
			NativeWorldLogAdmissionTimes.Dequeue();
		}
		if (NativeWorldLogAdmissionTimes.Count >= NativeWorldLogWindowMax)
		{
			return false;
		}

		int currentYear = ResolveCurrentWorldYear();
		if (_nativeWorldLogYear != currentYear)
		{
			_nativeWorldLogYear = currentYear;
			_nativeWorldLogAdmissionsThisYear = 0;
			NativeWorldLogYearlyGroups.Clear();
		}
		if (currentYear > 0 && _nativeWorldLogAdmissionsThisYear >= NativeWorldLogPerWorldYear)
		{
			return false;
		}

		string group = ResolveAnnouncementGroup(text, iconId, XjAnnouncementCategory.Auto);
		NativeWorldLogYearlyGroups.TryGetValue(group, out int groupCount);
		if (currentYear > 0 && groupCount >= NativeWorldLogPerGroupPerWorldYear)
		{
			return false;
		}

		_lastNativeWorldLogTime = now;
		NativeWorldLogAdmissionTimes.Enqueue(now);
		_nativeWorldLogAdmissionsThisYear++;
		NativeWorldLogYearlyGroups[group] = groupCount + 1;
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
		if (!ShouldShowAnnouncement(text, iconId) || IsPersistedFrequentAnnouncement(text, iconId))
		{
			return false;
		}

		XjAnnouncementCategory configured = ResolveConfiguredCategory(text, iconId);
		if (configured != XjAnnouncementCategory.Auto)
		{
			return IsExplicitRoutineSurfaceCategory(configured, text, iconId);
		}
		return IsBLevelSurfaceIcon(iconId);
	}

	/// <summary>
	/// B级事件已经由同一入口写入玄鉴仙鉴或三书时，高频项目只保留左侧历史，
	/// 不再占用右上角WorldTip。证道、陨落、开宗、灭宗与首次洞天显世仍属低频大事。
	/// </summary>
	private static bool IsPersistedFrequentAnnouncement(string text, string iconId)
	{
		if (string.IsNullOrWhiteSpace(text)) return false;

		// 这一层只处理“已持久化且高频”的旧入口；真正的顶部公告白名单由
		// ShouldSurfaceTopAnnouncement 统一裁决，避免生产者各自把普通成长抬成天下大事。
		return HasAny(text,
			"宗门阶段完成", "山门阶段完成", "宗门大阵维护", "护宗大阵维护", "阵法维修",
			"讲法", "讲道", "宗门大比", "山门大比", "完成一批");
	}

	private static bool IsProtectedCriticalAnnouncement(string text, string iconId)
	{
		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		if (IsIcon(icon, XjEventIconCatalog.JinDanUpgrade, XjEventIconCatalog.HighRealmDeath, XjEventIconCatalog.JinDanDemon, XjEventIconCatalog.RenDan))
		{
			return true;
		}

		return HasAny(text,
			"道胎成就", "成就金丹", "金丹真君", "真君羽士", "晋位真君", "证道", "空证",
			"正位承继", "夺正", "果位封锁", "果位嬗变",
			"人丹", "神丹", "结璘", "郁仪", "故尊",
			"权柄之争开启", "权柄之争终结", "道争终局",
			"开宗", "灭宗", "纪元", "传法天尊",
			"身陨", "陨落", "寿尽", "魂归天际");
	}

	private static bool IsBLevelSurfaceIcon(string iconId)
	{
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

	private static bool IsHighRealmAnnouncement(string text, string iconId)
	{
		string icon = XjEventIconCatalog.NormalizeIconId(iconId);
		return IsIcon(icon,
				XjEventIconCatalog.ZiFuUpgrade,
				XjEventIconCatalog.JinDanUpgrade,
				XjEventIconCatalog.JinDanDemon,
				XjEventIconCatalog.Jielin,
				XjEventIconCatalog.RenDan)
			|| HasAny(text,
				"紫府", "真人", "金丹", "真君", "羽士", "神丹", "结璘", "空证",
				"人丹", "权柄之争", "果位", "正位", "余位", "闰位", "神妙圆满");
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

	private static bool TryAcquireInteractiveTipSlot(string text)
	{
		float now = Time.unscaledTime;
		if (now - _lastInteractiveTipTime < InteractiveTipGlobalMinInterval
			|| IsSameTextCoolingDown(WorldTipLastByText, text, WorldTipSameTextCooldown))
		{
			return false;
		}

		RecordRate(WorldTipLastByText, ref _lastInteractiveTipTime, text, WorldTipSameTextCooldown);
		return true;
	}

	private static bool IsSameTextCoolingDown(Dictionary<string, float> cache, string text, float cooldown)
	{
		string signature = BuildAnnouncementSignature(text);
		return signature.Length > 0
			&& cache.TryGetValue(signature, out float lastTextTime)
			&& Time.unscaledTime - lastTextTime < cooldown;
	}

	private static void RecordRate(Dictionary<string, float> cache, ref float lastGlobalTime, string text, float sameTextCooldown)
	{
		float now = Time.unscaledTime;
		lastGlobalTime = now;
		string signature = BuildAnnouncementSignature(text);
		if (signature.Length > 0) cache[signature] = now;
		CleanupCache(cache, now, sameTextCooldown);
	}

	/// <summary>
	/// 公告可能经由领域史、角色史和三书投影多次抵达。去除空白与标点后使用同一语义签名，
	/// 避免同一事件只因换行、书名号或句读差异在历史栏和右上角重复出现。
	/// </summary>
	private static string BuildAnnouncementSignature(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		StringBuilder builder = new StringBuilder(text.Length);
		const string punctuation = "，。；：、,.!！?？‘’“”\"'【】[]（）()《》<>〈〉「」『』—-·";
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (char.IsWhiteSpace(c) || punctuation.IndexOf(c) >= 0) continue;
			builder.Append(c);
		}
		return builder.ToString();
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
