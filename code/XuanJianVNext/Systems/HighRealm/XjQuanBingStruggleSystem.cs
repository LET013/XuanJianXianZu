using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 0.5.4 权柄之争迁移版：新晋金丹巅峰触发，至少三名活体金丹参战，
/// 最长持续十年。夺得外道权柄者立即退入洞天合道；期限届满、全部
/// 幸存者退场，或确认夺权且死者超过三分之二时结束。
/// </summary>
internal static class XjQuanBingStruggleSystem
{
	private const int MinimumParticipants = 3;
	private const int DurationYears = 10;
	private const int PeakYiXiang = 6000;
	private const int EngageRadius = 18;

	private static readonly List<long> ParticipantActorIds = new List<long>(16);
	private static bool _enabled;
	private static int _startYear;
	private static int _endYearExclusive;
	private static long _initiatorActorId;
	private static string _initiatorName = string.Empty;
	private static int _initialParticipantCount;
	private static int _deadParticipantCount;
	private static bool _hasExternalAuthoritySeizure;
	private static int _lastProcessedYear = -1;

	internal static bool IsActive => _enabled;

	internal static void TickAnnual(int currentYear)
	{
		if (currentYear < 0 || _lastProcessedYear >= currentYear)
		{
			return;
		}

		_lastProcessedYear = currentYear;
		RefreshParticipants();
		if (_enabled)
		{
			UpdateEffectiveDeathCount();
			if (ShouldEnd(currentYear))
			{
				End();
			}
			else
			{
				ReleaseActiveCombatantsFromOrdinaryRetreat();
				Touch();
			}
			return;
		}

		if (ParticipantActorIds.Count < MinimumParticipants)
		{
			RefreshPeakObservation(resetOnly: true, out _);
			return;
		}

		RefreshPeakObservation(resetOnly: false, out Actor trigger);
		if (trigger != null)
		{
			Begin(trigger, currentYear);
		}
	}

	internal static bool TickCombatActor(Actor actor)
	{
		if (!_enabled || !IsParticipant(actor, out long actorId))
		{
			return false;
		}

		if (!ParticipantActorIds.Contains(actorId))
		{
			ParticipantActorIds.Add(actorId);
		}

		if (IsWithdrawnParticipant(actor))
		{
			ClearCombatIntent(actor);
			return true;
		}

		Actor target = FindNearestTarget(actor);
		if (target == null)
		{
			return true;
		}

		try
		{
			WorldTile actorTile = ((BaseSimObject)actor).current_tile;
			WorldTile targetTile = ((BaseSimObject)target).current_tile;
			if (actorTile == null || targetTile == null)
			{
				return true;
			}

			int distanceSquared = Toolbox.SquaredDistVec2(actorTile.pos, targetTile.pos);
			if (distanceSquared <= EngageRadius * EngageRadius)
			{
				actor.attackedBy = target;
				XjActorAggroBridge.ForceAggro(actor, target);
			}
			else
			{
				actor.setTileTarget(targetTile);
				actor.goTo(targetTile, false, false, false, 0);
			}
		}
		catch
		{
			// 原生行为树版本差异不应中断金丹运行时队列。
		}
		return true;
	}

	internal static void NotifyJinDanDeath(long actorId)
	{
		if (!_enabled || actorId <= 0L)
		{
			return;
		}

		UpdateEffectiveDeathCount();
		Touch();
	}

	internal static void NotifyAuthoritySeized(
		Actor killer,
		string victimDaoTu,
		string authority,
		bool externalAuthority)
	{
		if (killer?.data == null || string.IsNullOrWhiteSpace(authority))
		{
			return;
		}

		string displayName = ResolveAnnouncementName(killer);
		string sourceDaoTu = string.IsNullOrWhiteSpace(victimDaoTu) ? "外道" : victimDaoTu.Trim();
		XjBroadcastSystem.BroadcastSLevelWorldEvent(
			displayName + "夺" + sourceDaoTu + "之“" + authority.Trim() + "”权柄，归诸己身，天地共鉴。",
			color: "#D9822B");

		if (_enabled && externalAuthority)
		{
			_hasExternalAuthoritySeizure = true;
			long actorId = ((BaseSystemData)killer.data).id;
			if (actorId > 0L && !ParticipantActorIds.Contains(actorId))
			{
				ParticipantActorIds.Add(actorId);
			}
			Touch();
		}
	}

	internal static XjWorldArchiveQuanBingStruggleState ExportState()
	{
		return new XjWorldArchiveQuanBingStruggleState
		{
			Enabled = _enabled,
			StartYear = _startYear,
			EndYearExclusive = _endYearExclusive,
			InitiatorActorId = _initiatorActorId,
			InitiatorName = _initiatorName,
			InitialParticipantCount = _initialParticipantCount,
			DeadParticipantCount = _deadParticipantCount,
			HasExternalAuthoritySeizure = _hasExternalAuthoritySeizure,
			LastProcessedYear = _lastProcessedYear
		};
	}

	internal static void ImportState(XjWorldArchiveQuanBingStruggleState state)
	{
		XjWorldArchiveQuanBingStruggleState source = state ?? new XjWorldArchiveQuanBingStruggleState();
		_enabled = source.Enabled;
		_startYear = Math.Max(0, source.StartYear);
		_endYearExclusive = Math.Max(0, source.EndYearExclusive);
		_initiatorActorId = Math.Max(0L, source.InitiatorActorId);
		_initiatorName = source.InitiatorName ?? string.Empty;
		_initialParticipantCount = Math.Max(0, source.InitialParticipantCount);
		_deadParticipantCount = Math.Max(0, source.DeadParticipantCount);
		_hasExternalAuthoritySeizure = source.HasExternalAuthoritySeizure;
		_lastProcessedYear = source.LastProcessedYear;
		ParticipantActorIds.Clear();
	}

	internal static void Clear()
	{
		ParticipantActorIds.Clear();
		_enabled = false;
		_startYear = 0;
		_endYearExclusive = 0;
		_initiatorActorId = 0L;
		_initiatorName = string.Empty;
		_initialParticipantCount = 0;
		_deadParticipantCount = 0;
		_hasExternalAuthoritySeizure = false;
		_lastProcessedYear = -1;
	}

	private static void Begin(Actor trigger, int currentYear)
	{
		if (trigger?.data == null || ParticipantActorIds.Count < MinimumParticipants)
		{
			return;
		}

		_enabled = true;
		_startYear = Math.Max(0, currentYear);
		_endYearExclusive = SafeAdd(_startYear, DurationYears);
		_initiatorActorId = ((BaseSystemData)trigger.data).id;
		_initiatorName = trigger.getName() ?? string.Empty;
		_initialParticipantCount = ParticipantActorIds.Count;
		_deadParticipantCount = 0;
		_hasExternalAuthoritySeizure = false;
		ReleaseActiveCombatantsFromOrdinaryRetreat();

		XjBroadcastSystem.BroadcastSLevelWorldEvent(
			ResolveAnnouncementName(trigger) + "已臻金丹巅峰，为求更进一步，今掀权柄之争，欲夺诸道权柄以证大道。",
			color: "#D9822B");
		Touch();
	}

	private static bool ShouldEnd(int currentYear)
	{
		if (!_enabled)
		{
			return false;
		}
		if (_endYearExclusive > 0 && currentYear >= _endYearExclusive)
		{
			return true;
		}
		if (AreAllAliveParticipantsWithdrawn())
		{
			return true;
		}
		return _hasExternalAuthoritySeizure
			&& _initialParticipantCount > 0
			&& _deadParticipantCount * 3 > _initialParticipantCount * 2;
	}

	private static void End()
	{
		if (!_enabled)
		{
			return;
		}

		_enabled = false;
		for (int i = 0; i < ParticipantActorIds.Count; i++)
		{
			if (XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) && actor?.data != null)
			{
				ClearCombatIntent(actor);
			}
		}

		_startYear = 0;
		_endYearExclusive = 0;
		_initiatorActorId = 0L;
		_initiatorName = string.Empty;
		_initialParticipantCount = 0;
		_deadParticipantCount = 0;
		_hasExternalAuthoritySeizure = false;
		ParticipantActorIds.Clear();
		XjBroadcastSystem.BroadcastBLevelWorldEvent("权柄之争尘埃落定，诸金丹各归洞天，闭关参悟新得之权。");
		Touch();
	}

	private static void RefreshParticipants()
	{
		ParticipantActorIds.Clear();
		IReadOnlyList<long> jinDanIds = XjCultivatorCache.GetJinDanIds();
		for (int i = 0; i < jinDanIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(jinDanIds[i], out Actor actor))
			{
				continue;
			}

			if (IsParticipant(actor, out long actorId))
			{
				ParticipantActorIds.Add(actorId);
			}
		}
	}

	private static void UpdateEffectiveDeathCount()
	{
		if (!_enabled || _initialParticipantCount <= 0)
		{
			return;
		}

		int inferred = Math.Max(0, _initialParticipantCount - ParticipantActorIds.Count);
		if (inferred > _deadParticipantCount)
		{
			_deadParticipantCount = inferred;
		}
	}

	private static void RefreshPeakObservation(bool resetOnly, out Actor trigger)
	{
		trigger = null;
		for (int i = 0; i < ParticipantActorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) || actor?.data == null)
			{
				continue;
			}

			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
			bool isPeak = yiXiang >= PeakYiXiang;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, out int observed);
			if (!isPeak)
			{
				if (observed != 0)
				{
					XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, 0);
				}
				continue;
			}

			if (!resetOnly && observed == 0 && trigger == null)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, 1);
				trigger = actor;
			}
		}
	}

	private static void ReleaseActiveCombatantsFromOrdinaryRetreat()
	{
		for (int i = 0; i < ParticipantActorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) || actor?.data == null)
			{
				continue;
			}
			if (IsWithdrawnParticipant(actor))
			{
				XjClosedCultivationGuard.MarkClosedCultivation(actor, true);
				continue;
			}
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			ClearCombatIntent(actor);
		}
	}

	private static bool AreAllAliveParticipantsWithdrawn()
	{
		if (ParticipantActorIds.Count == 0)
		{
			return true;
		}

		for (int i = 0; i < ParticipantActorIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ParticipantActorIds[i], out Actor actor) || actor?.data == null)
			{
				continue;
			}
			if (!IsWithdrawnParticipant(actor))
			{
				return false;
			}
		}
		return true;
	}

	internal static bool IsWithdrawnParticipant(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		return XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)
			&& (state.IntegrationRetreatActive || !string.IsNullOrWhiteSpace(state.WithdrawnToDongTian));
	}

	private static Actor FindNearestTarget(Actor actor)
	{
		WorldTile actorTile;
		try { actorTile = ((BaseSimObject)actor).current_tile; }
		catch { actorTile = null; }
		if (actorTile == null)
		{
			return null;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string ownDaoTu);
		Actor bestDifferent = null;
		Actor bestFallback = null;
		int bestDifferentDistance = int.MaxValue;
		int bestFallbackDistance = int.MaxValue;
		long actorId = ((BaseSystemData)actor.data).id;
		for (int i = 0; i < ParticipantActorIds.Count; i++)
		{
			long candidateId = ParticipantActorIds[i];
			if (candidateId == actorId
				|| !XjScheduler.ResolveActor(candidateId, out Actor candidate)
				|| !IsParticipant(candidate, out _)
				|| IsWithdrawnParticipant(candidate))
			{
				continue;
			}

			WorldTile targetTile;
			try { targetTile = ((BaseSimObject)candidate).current_tile; }
			catch { targetTile = null; }
			if (targetTile == null)
			{
				continue;
			}

			int distance = Toolbox.SquaredDistVec2(actorTile.pos, targetTile.pos);
			XjActorAccessor.TryGetString(candidate, XjActorDataKeys.DaoTu, out string targetDaoTu);
			if (!string.IsNullOrWhiteSpace(ownDaoTu)
				&& !string.Equals(ownDaoTu, targetDaoTu, StringComparison.Ordinal))
			{
				if (distance < bestDifferentDistance)
				{
					bestDifferentDistance = distance;
					bestDifferent = candidate;
				}
			}
			else if (distance < bestFallbackDistance)
			{
				bestFallbackDistance = distance;
				bestFallback = candidate;
			}
		}

		return bestDifferent ?? bestFallback;
	}

	private static bool IsParticipant(Actor actor, out long actorId)
	{
		actorId = 0L;
		if (!XjSafeCore.IsAliveActor(actor) || !XjJinDanAccessor.BuildState(actor).Found)
		{
			return false;
		}
		actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L;
	}

	private static void ClearCombatIntent(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		try
		{
			actor.attackedBy = null;
			actor.cancelAllBeh();
			actor.stopMovement();
		}
		catch
		{
		}
	}

	private static string ResolveAnnouncementName(Actor actor)
	{
		string displayName = actor?.getName()?.Trim() ?? string.Empty;
		int dashIndex = displayName.IndexOf('-');
		if (dashIndex > 0)
		{
			displayName = displayName.Substring(0, dashIndex).Trim();
		}
		return string.IsNullOrWhiteSpace(displayName) ? "无名真君" : displayName;
	}

	private static int SafeAdd(int year, int duration)
	{
		long value = (long)Math.Max(0, year) + Math.Max(0, duration);
		return value > int.MaxValue ? int.MaxValue : (int)value;
	}

	private static void Touch()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}
}
