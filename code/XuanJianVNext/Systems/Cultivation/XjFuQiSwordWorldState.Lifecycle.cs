using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.Cultivation;

internal static partial class XjFuQiSwordWorldState
{
	internal static bool TryClaimVacantPosition(Actor actor, int currentYear, out int previousVacantSinceYear)
	{
		previousVacantSinceYear = _state.VacantSinceYear;
		if (!_state.Established || _state.CurrentHolderActorId > 0L || actor?.data == null || currentYear <= 0)
			return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		CloseAllActiveHistory(previousVacantSinceYear > 0 ? previousVacantSinceYear : currentYear);
		_state.CurrentHolderActorId = actorId;
		_state.CurrentHolderAcquiredYear = currentYear;
		_state.VacantSinceYear = 0;
		_state.HolderHistory ??= new List<XjFuQiSwordPositionHolderArchiveData>();
		_state.HolderHistory.Add(new XjFuQiSwordPositionHolderArchiveData
		{
			ActorId = actorId,
			ActorName = SafeActorName(actor),
			AcquiredYear = currentYear,
			IsFounder = false
		});
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static void RollbackClaim(long actorId, int currentYear, int previousVacantSinceYear)
	{
		if (!_state.Established
			|| _state.CurrentHolderActorId != actorId
			|| _state.CurrentHolderAcquiredYear != currentYear)
		{
			return;
		}
		for (int i = _state.HolderHistory.Count - 1; i >= 0; i--)
		{
			XjFuQiSwordPositionHolderArchiveData entry = _state.HolderHistory[i];
			if (entry != null && !entry.IsFounder && entry.ActorId == actorId
				&& entry.AcquiredYear == currentYear && entry.ReleasedYear <= 0)
			{
				_state.HolderHistory.RemoveAt(i);
				break;
			}
		}
		_state.CurrentHolderActorId = 0L;
		_state.CurrentHolderAcquiredYear = 0;
		_state.VacantSinceYear = previousVacantSinceYear > 0 ? previousVacantSinceYear : currentYear;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void OnActorUnavailable(long actorId, int currentYear)
	{
		if (!_state.Established || actorId <= 0L || _state.CurrentHolderActorId != actorId) return;
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null && actor.isAlive())
		{
			return;
		}
		ReleaseCurrentHolder(actorId, currentYear, ResolveHistoryActorName(actorId), true);
	}

	/// <summary>
	/// 冷启动角色扫描完成后执行一次权威校准，保证世界中至多一名长庚果位持有者。
	/// 当前存档指向的合法角色优先，其次取历任记录中的未卸位者，最后按角色ID稳定裁决。
	/// </summary>
	internal static void ReconcileAfterBootstrap(IReadOnlyList<Actor> actors, int currentYear)
	{
		if (!_state.Established) return;
		int year = ResolveCurrentYear(currentYear);
		List<Actor> liveHolders = new List<Actor>();
		if (actors != null)
		{
			for (int i = 0; i < actors.Count; i++)
			{
				Actor actor = actors[i];
				if (IsLongGengZhenJun(actor)) liveHolders.Add(actor);
			}
		}

		Actor winner = FindActorById(liveHolders, _state.CurrentHolderActorId)
			?? FindActorById(liveHolders, FindRecordedActiveHolderId())
			?? PickLowestActorId(liveHolders);

		if (winner == null)
		{
			if (_state.CurrentHolderActorId > 0L)
				ReleaseCurrentHolder(
					_state.CurrentHolderActorId,
					year,
					ResolveHistoryActorName(_state.CurrentHolderActorId),
					false);
			else if (_state.VacantSinceYear <= 0)
				_state.VacantSinceYear = year;
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		long winnerId = ((BaseSystemData)winner.data).id;
		if (_state.CurrentHolderActorId != winnerId)
		{
			if (_state.CurrentHolderActorId > 0L) CloseActiveHistory(_state.CurrentHolderActorId, year);
			_state.CurrentHolderActorId = winnerId;
			_state.CurrentHolderAcquiredYear = ResolveRecordedAcquiredYear(winnerId, year);
			_state.VacantSinceYear = 0;
			EnsureActiveHistory(winner, _state.CurrentHolderAcquiredYear);
		}
		else
		{
			_state.VacantSinceYear = 0;
			if (_state.CurrentHolderAcquiredYear <= 0)
				_state.CurrentHolderAcquiredYear = ResolveRecordedAcquiredYear(winnerId, year);
			EnsureActiveHistory(winner, _state.CurrentHolderAcquiredYear);
		}
		XuanJianVNext.Systems.HighRealm.XjJinDanImmortalityRegistry.EnsureActivated(winner, year);

		for (int i = 0; i < liveHolders.Count; i++)
		{
			Actor duplicate = liveHolders[i];
			long duplicateId = ((BaseSystemData)duplicate.data).id;
			if (duplicateId != winnerId) DemoteDuplicateHolder(duplicate, year);
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void ReconcileRestoredActor(Actor actor, long sourceActorId, int currentYear)
	{
		if (!_state.Established || actor?.data == null || !IsLongGengZhenJun(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int year = ResolveCurrentYear(currentYear);

		if (_state.CurrentHolderActorId > 0L)
		{
			if (_state.CurrentHolderActorId != actorId) DemoteDuplicateHolder(actor, year);
			return;
		}

		if (sourceActorId > 0L && HasHistoryForActor(sourceActorId))
		{
			_state.CurrentHolderActorId = actorId;
			_state.CurrentHolderAcquiredYear = year;
			_state.VacantSinceYear = 0;
			EnsureActiveHistory(actor, year);
			XuanJianVNext.Systems.HighRealm.XjJinDanImmortalityRegistry.EnsureActivated(actor, year);
			XjWorldArchiveSystem.MarkChanged();
			return;
		}

		// 不是历任果位持有者的登名石副本不能凭境界字段复制世界唯一果位。
		DemoteDuplicateHolder(actor, year);
	}

	internal static IReadOnlyList<XjFuQiSwordPositionHolderArchiveData> ReadHolderHistory()
	{
		List<XjFuQiSwordPositionHolderArchiveData> copy = new List<XjFuQiSwordPositionHolderArchiveData>();
		if (_state.HolderHistory == null) return copy;
		for (int i = 0; i < _state.HolderHistory.Count; i++)
		{
			if (_state.HolderHistory[i] != null) copy.Add(_state.HolderHistory[i].Clone());
		}
		return copy;
	}

	private static void NormalizeEstablishedState()
	{
		if (!_state.Established) return;
		_state.DaoName = EstablishedDaoName;
		_state.PositionRank = EstablishedPositionRank;
		_state.HolderHistory ??= new List<XjFuQiSwordPositionHolderArchiveData>();
		if (_state.EstablishedYear <= 0) _state.EstablishedYear = ResolveCurrentYear(1);

		if (_state.CurrentHolderActorId > 0L)
		{
			if (_state.CurrentHolderAcquiredYear <= 0)
				_state.CurrentHolderAcquiredYear = _state.EstablishedYear;
			_state.VacantSinceYear = 0;
			if (FindHistory(_state.CurrentHolderActorId, true) == null)
			{
				_state.HolderHistory.Add(new XjFuQiSwordPositionHolderArchiveData
				{
					ActorId = _state.CurrentHolderActorId,
					ActorName = ResolveHistoryActorName(_state.CurrentHolderActorId),
					AcquiredYear = _state.CurrentHolderAcquiredYear,
					IsFounder = _state.CurrentHolderActorId == _state.FounderActorId
				});
			}
		}
		else if (_state.VacantSinceYear <= 0)
		{
			// 0.9.4.13旧档只保存“当前持有者=0”，没有死亡年份；
			// 迁移时以当前存档年份为最可靠的空悬下界，不能误写成开道年份。
			_state.VacantSinceYear = ResolveCurrentYear(_state.EstablishedYear);
		}

		if (_state.FounderActorId > 0L && !HasHistoryForActor(_state.FounderActorId))
		{
			_state.HolderHistory.Insert(0, new XjFuQiSwordPositionHolderArchiveData
			{
				ActorId = _state.FounderActorId,
				ActorName = string.IsNullOrWhiteSpace(_state.FounderName) ? "无名羽士" : _state.FounderName.Trim(),
				AcquiredYear = _state.EstablishedYear,
				ReleasedYear = _state.CurrentHolderActorId == _state.FounderActorId
					? 0
					: Math.Max(_state.EstablishedYear, _state.VacantSinceYear),
				IsFounder = true
			});
		}
	}

	private static void ReleaseCurrentHolder(long actorId, int currentYear, string actorName, bool publish)
	{
		if (_state.CurrentHolderActorId != actorId) return;
		int year = ResolveCurrentYear(currentYear);
		CloseActiveHistory(actorId, year);
		_state.CurrentHolderActorId = 0L;
		_state.CurrentHolderAcquiredYear = 0;
		_state.VacantSinceYear = year;
		XjWorldArchiveSystem.MarkChanged();
		if (publish) PublishVacancy(actorName);
	}

	private static void PublishVacancy(string actorName)
	{
		string name = string.IsNullOrWhiteSpace(actorName) ? "前任持位者" : actorName.Trim();
		string body = name + "已离世或退出现世，长庚道途仍存，天地果位暂告空悬。"
			+ "后来服气真人可继续温养金性，求证承位。";
		XjBroadcastSystem.BroadcastBLevelWorldEvent(
			"【长庚果位空悬】" + body,
			XjEventIconCatalog.JinDanUpgrade,
			XjAnnouncementCategory.HighRealm);
	}

	private static bool IsLongGengZhenJun(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsFuQiYangXing(actor))
			return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
			return false;
		return XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition core)
			&& string.Equals(core.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal);
	}

	private static void DemoteDuplicateHolder(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true))
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 1);
		int year = ResolveCurrentYear(currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, year);
		XuanJianVNext.Systems.HighRealm.XjJinDanImmortalityRegistry.RemoveForRealmLoss(actor);
		XuanJianVNext.Systems.DongTian.XjSecretRealmRegistry.OnActorLostZhenJunStatus(
			((BaseSystemData)actor.data).id,
			year);
	}

	private static void EnsureActiveHistory(Actor actor, int acquiredYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		XjFuQiSwordPositionHolderArchiveData existing = FindHistory(actorId, true);
		if (existing != null)
		{
			existing.ActorName = SafeActorName(actor);
			return;
		}
		_state.HolderHistory ??= new List<XjFuQiSwordPositionHolderArchiveData>();
		_state.HolderHistory.Add(new XjFuQiSwordPositionHolderArchiveData
		{
			ActorId = actorId,
			ActorName = SafeActorName(actor),
			AcquiredYear = Math.Max(1, acquiredYear),
			IsFounder = actorId == _state.FounderActorId
		});
	}

	private static void CloseAllActiveHistory(int releasedYear)
	{
		if (_state.HolderHistory == null) return;
		for (int i = 0; i < _state.HolderHistory.Count; i++)
		{
			XjFuQiSwordPositionHolderArchiveData entry = _state.HolderHistory[i];
			if (entry != null && entry.ReleasedYear <= 0)
				entry.ReleasedYear = Math.Max(entry.AcquiredYear, releasedYear);
		}
	}

	private static void CloseActiveHistory(long actorId, int releasedYear)
	{
		XjFuQiSwordPositionHolderArchiveData entry = FindHistory(actorId, true);
		if (entry != null) entry.ReleasedYear = Math.Max(entry.AcquiredYear, releasedYear);
	}

	private static XjFuQiSwordPositionHolderArchiveData FindHistory(long actorId, bool activeOnly)
	{
		if (_state.HolderHistory == null || actorId <= 0L) return null;
		for (int i = _state.HolderHistory.Count - 1; i >= 0; i--)
		{
			XjFuQiSwordPositionHolderArchiveData entry = _state.HolderHistory[i];
			if (entry != null && entry.ActorId == actorId && (!activeOnly || entry.ReleasedYear <= 0))
				return entry;
		}
		return null;
	}

	private static bool HasHistoryForActor(long actorId)
	{
		return FindHistory(actorId, false) != null;
	}

	private static long FindRecordedActiveHolderId()
	{
		if (_state.HolderHistory == null) return 0L;
		for (int i = _state.HolderHistory.Count - 1; i >= 0; i--)
		{
			XjFuQiSwordPositionHolderArchiveData entry = _state.HolderHistory[i];
			if (entry != null && entry.ActorId > 0L && entry.ReleasedYear <= 0) return entry.ActorId;
		}
		return 0L;
	}

	private static int ResolveRecordedAcquiredYear(long actorId, int fallbackYear)
	{
		XjFuQiSwordPositionHolderArchiveData entry = FindHistory(actorId, false);
		return entry != null && entry.AcquiredYear > 0
			? entry.AcquiredYear
			: Math.Max(1, fallbackYear);
	}

	private static string ResolveHistoryActorName(long actorId)
	{
		XjFuQiSwordPositionHolderArchiveData entry = FindHistory(actorId, false);
		if (entry != null && !string.IsNullOrWhiteSpace(entry.ActorName)) return entry.ActorName.Trim();
		if (actorId == _state.FounderActorId && !string.IsNullOrWhiteSpace(_state.FounderName))
			return _state.FounderName.Trim();
		return "前任持位者";
	}

	private static Actor FindActorById(IReadOnlyList<Actor> actors, long actorId)
	{
		if (actors == null || actorId <= 0L) return null;
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data != null && ((BaseSystemData)actor.data).id == actorId) return actor;
		}
		return null;
	}

	private static Actor PickLowestActorId(IReadOnlyList<Actor> actors)
	{
		Actor best = null;
		long bestId = long.MaxValue;
		if (actors == null) return null;
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data == null) continue;
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || actorId >= bestId) continue;
			best = actor;
			bestId = actorId;
		}
		return best;
	}

	private static string SafeActorName(Actor actor)
	{
		string actorName = actor?.getName() ?? string.Empty;
		return string.IsNullOrWhiteSpace(actorName) ? "无名羽士" : actorName.Trim();
	}

	private static int ResolveCurrentYear(int currentYear)
	{
		return Math.Max(
			1,
			Math.Max(currentYear, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)));
	}
}
