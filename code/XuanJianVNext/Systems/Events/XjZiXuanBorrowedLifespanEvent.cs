using System;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using UnityEngine;

namespace XuanJianVNext.Systems.Events;

internal static class XjZiXuanBorrowedLifespanEvent
{
	internal const float MortalLifespanCapYears = 80f;
	private const int LatestOpeningYear = 10;
	private const int InitialDelayFrames = 45;
	private static XjOpeningLifespanEventArchiveData _state = new XjOpeningLifespanEventArchiveData();
	private static bool _pendingAfterLoad;
	private static bool _openingEligibleAtLoad;
	private static int _earliestFrame;

	internal static bool HasPendingAfterLoad => _pendingAfterLoad && !_state.Triggered;

	internal static void ScheduleAfterLoad(int currentYear)
	{
		_pendingAfterLoad = !_state.Triggered;
		_openingEligibleAtLoad = currentYear >= 0 && currentYear <= LatestOpeningYear;
		_earliestFrame = Time.frameCount + InitialDelayFrames;
	}

	internal static void TickAfterLoad()
	{
		if (!_pendingAfterLoad) return;
		if (_state.Triggered)
		{
			_pendingAfterLoad = false;
			return;
		}
		if (!XjWorldArchiveSystem.HasLoadedArchive
			|| !Config.game_loaded
			|| World.world?.map_stats == null
			|| SmoothLoader.isLoading()
			|| Time.frameCount < _earliestFrame) return;

		int currentYear = Math.Max(1, World.world.map_stats.year);
		if (!_openingEligibleAtLoad)
		{
			EstablishLegacyBaseline(currentYear);
			_pendingAfterLoad = false;
			return;
		}

		TriggerOpening(currentYear);
		_pendingAfterLoad = false;
	}

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0 || _state.Triggered) return;
		if (_pendingAfterLoad && _openingEligibleAtLoad)
		{
			TriggerOpening(currentYear);
			_pendingAfterLoad = false;
			return;
		}
		if (currentYear > LatestOpeningYear)
		{
			EstablishLegacyBaseline(currentYear);
			return;
		}
		TriggerOpening(currentYear);
	}

	private static void EstablishLegacyBaseline(int currentYear)
	{
		if (_state.Triggered) return;
		_state.Initialized = true;
		_state.Triggered = true;
		_state.LegacyBaseline = true;
		_state.TriggeredYear = Math.Max(1, currentYear);
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
	}

	private static void TriggerOpening(int currentYear)
	{
		if (_state.Triggered) return;
		_state.Initialized = true;
		_state.Triggered = true;
		_state.LegacyBaseline = false;
		_state.TriggeredYear = Math.Max(1, currentYear);
		XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.Events);
		const string history =
			"觜玄以自身修为、仙道位格与保木果位为押，向天地借取众生寿数。"
			+ "他凭此功绩登临仙君，却暗藏所借寿数，不曾归还，旋即负契远遁天外。"
			+ "天道因此重创，凡人与修士寿数一并折损；其行违天德、逆人德，仙道共斥为魔祖。"
			+ "此劫后世称作“斩养之劫”，保木果位与谪炁第一余位亦随劫封闭，不再授予现世修士。";
		XjBroadcastSystem.BroadcastSLevelDomainEvent(
			XjWorldHistoryCategory.World,
			"ZhanYangCalamity",
			history,
			"【斩养之劫】觜玄借众生寿登临仙君，藏寿负契，远遁天外；天道重创，众生寿数自此折损。",
			result: XjHistoryResult.Success,
			year: Math.Max(1, currentYear),
			color: "#C8A56B",
			duration: 10f,
			iconId: XjEventIconCatalog.HistoryWorld);
	}


	internal static XjOpeningLifespanEventArchiveData ExportState() => new XjOpeningLifespanEventArchiveData
	{
		Initialized = _state.Initialized,
		Triggered = _state.Triggered,
		LegacyBaseline = _state.LegacyBaseline,
		TriggeredYear = _state.TriggeredYear
	};

	internal static void ImportState(XjOpeningLifespanEventArchiveData state)
	{
		_state = state ?? new XjOpeningLifespanEventArchiveData();
		if (_state.Triggered) _pendingAfterLoad = false;
	}

	internal static void ClearRuntimePending()
	{
		_pendingAfterLoad = false;
		_openingEligibleAtLoad = false;
		_earliestFrame = 0;
	}

	internal static void Clear()
	{
		_state = new XjOpeningLifespanEventArchiveData();
		ClearRuntimePending();
	}
}
