using System;
using UnityEngine;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修版本的开局前情。finishingUpLoading 只负责登记，真正的公告在
/// 世界、归档和原生 UI 均稳定后投递，避免读档黑屏阶段丢失 WorldTip。
/// </summary>
internal static class XjShiOpeningPrologueSystem
{
	private const int InitialDelayFrames = 90;
	private const int RetryDelayFrames = 30;
	private const string EventType = "ShiOpeningPrologue";
	internal const int AncientSeedLimit = 18;
	private const int AncientSeedRuleVersion = 5;
	private const int AncientSeedFirstManifestDelayYears = 50;

	private static XjShiOpeningPrologueArchiveData _state = new XjShiOpeningPrologueArchiveData();
	private static bool _pendingAfterLoad;
	private static int _earliestFrame;

	internal static bool HasPending => _pendingAfterLoad && !_state.Triggered;

	internal static void ScheduleAfterLoad(int currentYear)
	{
		_pendingAfterLoad = !_state.Triggered;
		_earliestFrame = Time.frameCount + InitialDelayFrames;
	}

	internal static void Tick()
	{
		if (!_pendingAfterLoad || _state.Triggered)
		{
			return;
		}

		// 归档可能晚于 finishingUpLoading 才挂到 map_stats.custom_data。
		// 必须等导入完成后再判断 Triggered，防止旧档重复播放。
		if (!XjWorldArchiveSystem.HasLoadedArchive
			|| !Config.game_loaded
			|| World.world?.map_stats == null
			|| SmoothLoader.isLoading()
			|| Time.frameCount < _earliestFrame)
		{
			return;
		}

		int currentYear = Math.Max(0, World.world.map_stats.year);
		const string historyTitle = "北世尊遗世，三十二天碎为六十九金地";
		const string historyBody =
			"北世尊殁后，三十二应身化作三十二天；诸天复碎，遂成六十九块同源金地，散落于诸释土内外。"
			+ "旃檀林收摄其中三十八块，为今释共有之土；今释摩诃历满九世，方可借一块金地成就庙主，并由此求证法相。";

		string tipText = XjZhantanlinSystem.IsPlaced
			? "【旃檀法界·前情提要】\n北世尊殁后，三十二应身化作三十二天；诸天复碎，遂成六十九块金地。旃檀林收摄其中三十八块，为今释共有之土；今释摩诃历满九世，方可借一块金地成就庙主，并由此求证法相。\n旃檀林已显世，今释真身、轮回与庙主证道自此皆有所归。"
			: "【旃檀法界·前情提要】\n北世尊殁后，三十二应身化作三十二天；诸天复碎，遂成六十九块金地。旃檀林将收摄其中三十八块，为今释共有之土；今释摩诃历满九世，方可借一块金地成就庙主，并由此求证法相。\n请打开“玄鉴仙族”功能栏，选择【开辟旃檀林】，在地图上放置今释共有释土。";

		// 史实只记背景，不把操作提示混入天下纪事。
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World,
			historyTitle,
			historyBody,
			importance: 5,
			isProtected: true,
			year: currentYear,
			iconIdOverride: XjEventIconCatalog.HistoryWorld,
			eventType: EventType,
			result: XjHistoryResult.Success,
			mirrorToWorldLog: true);

		bool scheduled = XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			tipText,
			XjAnnouncementCategory.System,
			pause: true,
			position: "top",
			duration: 7f,
			color: "#D8BE72",
			delayFrames: 1,
			iconId: XjEventIconCatalog.HistoryWorld);
		if (!scheduled)
		{
			_earliestFrame = Time.frameCount + RetryDelayFrames;
			return;
		}

		_state.Triggered = true;
		_state.TriggeredYear = currentYear;
		_pendingAfterLoad = false;
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// 古释首批十八种子仍是“每个世界固定十八个名额”，但前五十年保持隐世。
	/// 五十年后不再设置人为的逐个年份间隔，由后续角色第一次真正定修行体系时
	/// 自然承接尚未落定的种子名额，因此会随人口与缘法陆续出现，而不是开局集中涌现。
	/// 十八名额用尽后，后世新古释才恢复 TryEnterInitial 的 1%~3% 遗经自悟。
	/// </summary>
	internal static bool TryBeginAncientSeedBootstrap(int currentYear, bool hasLivingAncient)
	{
		_ = hasLivingAncient; // 兼容旧调用；首批十八种子不再被“天下已有古释”截断。
		int year = Math.Max(1, currentYear);
		_state ??= new XjShiOpeningPrologueArchiveData();
		_state.AncientSeedCount = Math.Clamp(_state.AncientSeedCount, 0, AncientSeedLimit);
		if (_state.AncientSeedCount >= AncientSeedLimit)
		{
			_state.AncientSeedBootstrapClosed = true;
			return false;
		}

		if (!_state.AncientSeedBootstrapStarted)
		{
			_state.AncientSeedBootstrapStarted = true;
			_state.AncientSeedBootstrapYear = year;
			_state.AncientSeedBootstrapClosed = false;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		else if (_state.AncientSeedBootstrapClosed)
		{
			// 0.9.8.14以前会因快照耗尽/年份推进提前关窗；只要未满十八就重新开放。
			_state.AncientSeedBootstrapClosed = false;
			XjWorldArchiveSystem.MarkChanged();
		}

		return true;
	}

	internal static bool IsAncientSeedBootstrapOpen(int currentYear)
	{
		_ = currentYear;
		return _state != null
			&& _state.AncientSeedBootstrapStarted
			&& !_state.AncientSeedBootstrapClosed
			&& _state.AncientSeedCount < AncientSeedLimit;
	}

	internal static bool CanManifestAncientSeed(int currentYear)
	{
		int year = Math.Max(1, currentYear);
		if (!IsAncientSeedBootstrapOpen(year)) return false;
		int bootstrapYear = Math.Max(1, _state.AncientSeedBootstrapYear);
		return year - bootstrapYear >= AncientSeedFirstManifestDelayYears;
	}

	/// <summary>
	/// 完成“本次既有人口快照”的扫描。快照扫完并不代表十八种子已经补齐；
	/// 未满十八时只结束冷启动扫描，权威名额窗口继续留给后续新角色消费。
	/// </summary>
	internal static void CompleteAncientSeedBootstrap(int currentYear)
	{
		int year = Math.Max(1, currentYear);
		_state ??= new XjShiOpeningPrologueArchiveData();
		if (!_state.AncientSeedBootstrapStarted)
		{
			_state.AncientSeedBootstrapStarted = true;
			_state.AncientSeedBootstrapYear = year;
		}
		bool shouldClose = _state.AncientSeedCount >= AncientSeedLimit;
		if (_state.AncientSeedBootstrapClosed == shouldClose) return;
		_state.AncientSeedBootstrapClosed = shouldClose;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	internal static void RecordAncientSeedSuccess(int currentYear)
	{
		int year = Math.Max(1, currentYear);
		_state ??= new XjShiOpeningPrologueArchiveData();
		if (!_state.AncientSeedBootstrapStarted)
		{
			_state.AncientSeedBootstrapStarted = true;
			_state.AncientSeedBootstrapYear = year;
		}
		if (_state.AncientSeedCount >= AncientSeedLimit)
		{
			_state.AncientSeedBootstrapClosed = true;
			return;
		}
		_state.AncientSeedBootstrapClosed = false;
		_state.AncientSeedLastManifestYear = year;
		_state.AncientSeedCount = Math.Min(AncientSeedLimit, Math.Max(0, _state.AncientSeedCount) + 1);
		if (_state.AncientSeedCount >= AncientSeedLimit) _state.AncientSeedBootstrapClosed = true;
		XjWorldArchiveSystem.MarkChanged();
		if (_state.AncientSeedBootstrapClosed) XjWorldArchiveSystem.RequestProtectedCommit();
	}

	internal static XjShiOpeningPrologueArchiveData ExportState()
	{
		return new XjShiOpeningPrologueArchiveData
		{
			AncientSeedRuleVersion = AncientSeedRuleVersion,
			Triggered = _state.Triggered,
			TriggeredYear = _state.TriggeredYear,
			AncientSeedBootstrapStarted = _state.AncientSeedBootstrapStarted,
			AncientSeedBootstrapClosed = _state.AncientSeedBootstrapClosed,
			AncientSeedBootstrapYear = _state.AncientSeedBootstrapYear,
			AncientSeedLastManifestYear = _state.AncientSeedLastManifestYear,
			AncientSeedCount = Math.Clamp(_state.AncientSeedCount, 0, AncientSeedLimit)
		};
	}

	internal static void ImportState(XjShiOpeningPrologueArchiveData state)
	{
		_state = state ?? new XjShiOpeningPrologueArchiveData();
		// RuleVersion 5：保留每世界十八枚首批种子，但只保留“前五十年隐世”这一道时间门。
		// 五十年后不再强制相邻种子间隔；旧档已经出现的古释数量原样保留。
		if (_state.AncientSeedRuleVersion < AncientSeedRuleVersion)
		{
			_state.AncientSeedCount = Math.Clamp(_state.AncientSeedCount, 0, AncientSeedLimit);
			if (_state.AncientSeedCount < AncientSeedLimit)
			{
				_state.AncientSeedBootstrapClosed = false;
				if (!_state.AncientSeedBootstrapStarted) _state.AncientSeedBootstrapYear = 0;
			}
			else
			{
				_state.AncientSeedBootstrapStarted = true;
				_state.AncientSeedBootstrapClosed = true;
			}
			_state.AncientSeedRuleVersion = AncientSeedRuleVersion;
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		if (_state.Triggered)
		{
			_pendingAfterLoad = false;
		}
	}

	internal static void Clear()
	{
		_state = new XjShiOpeningPrologueArchiveData();
		_pendingAfterLoad = false;
		_earliestFrame = 0;
	}
}
