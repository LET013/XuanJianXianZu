using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// Low-frequency cross-index invariant reconciliation. This lane never scans the
/// whole WorldBox population: cultivator/actor reconciliation piggybacks on the
/// existing eight-bucket annual audit, while this owner only walks the much smaller
/// Sect-authority and runtime-family member snapshots once per reconciliation period.
///
/// Repairs are deliberately one-way and conservative: stale dead/orphan authority
/// records can be removed, live actor references can be re-registered, actor/city
/// projection mirrors can be queued for regeneration, and high-realm army exclusion
/// can be reasserted. It never creates actors, sects, cities or cultivation state.
/// </summary>
internal static class XjRuntimeInvariantLane
{
	private const int ReconcileIntervalYears = 8;
	private static IReadOnlyList<long> _memberActorIds = Array.Empty<long>();
	private static IReadOnlyList<long> _familyActorIds = Array.Empty<long>();
	private static int _cursor;
	private static int _familyCursor;
	private static int _scheduledYear;
	private static int _lastCompletedYear;
	private static int _issuesThisPass;
	private static int _repairsThisPass;
	private static int _lastIssueCount;
	private static int _lastRepairCount;
	private static int _lookupMissesThisPass;
	private static int _lastLookupMissCount;

	internal static bool HasPending => _cursor < (_memberActorIds?.Count ?? 0)
		|| _familyCursor < (_familyActorIds?.Count ?? 0);
	internal static int PendingCount => Math.Max(0, (_memberActorIds?.Count ?? 0) - _cursor)
		+ Math.Max(0, (_familyActorIds?.Count ?? 0) - _familyCursor);
	internal static int LastIssueCount => _lastIssueCount;
	internal static int LastRepairCount => _lastRepairCount;
	internal static int LastCompletedYear => _lastCompletedYear;

	internal static void Schedule(int currentYear)
	{
		if (currentYear <= 0 || XjWorldBootstrapLane.HasPending) return;
		if (HasPending)
		{
			if (currentYear > _scheduledYear) _scheduledYear = currentYear;
			return;
		}
		if (_lastCompletedYear > 0 && currentYear - _lastCompletedYear < ReconcileIntervalYears) return;

		_memberActorIds = XjSectAuthorityStore.GetMemberActorIdsSnapshot();
		_familyActorIds = XjFamilyMemberIndex.Shared.GetRuntimeActorIdsSnapshot();
		_cursor = 0;
		_familyCursor = 0;
		_scheduledYear = currentYear;
		_issuesThisPass = 0;
		_repairsThisPass = 0;
		_lookupMissesThisPass = 0;

		// World lookup already owns a bounded reconcile lane. The invariant pass only
		// requests that owner to verify same-count object replacement; it never walks
		// native city/kingdom collections here.
		XjWorldLookupIndex.RequestSanityRefresh();
		if ((_memberActorIds?.Count ?? 0) == 0 && (_familyActorIds?.Count ?? 0) == 0) CompletePass();
	}

	internal static void Tick(XjCooperativeBudget budget)
	{
		if (budget == null || budget.ShouldYield || !HasPending || XjWorldBootstrapLane.HasPending) return;
		while (_cursor < _memberActorIds.Count && budget.TryTake())
		{
			long actorId = _memberActorIds[_cursor++];
			AuditMember(actorId);
		}
		while (_cursor >= _memberActorIds.Count
			&& _familyCursor < _familyActorIds.Count
			&& budget.TryTake())
		{
			long actorId = _familyActorIds[_familyCursor++];
			AuditFamilyRuntimeMember(actorId);
		}
		if (_cursor >= _memberActorIds.Count && _familyCursor >= _familyActorIds.Count) CompletePass();
	}

	internal static void Clear()
	{
		_memberActorIds = Array.Empty<long>();
		_familyActorIds = Array.Empty<long>();
		_cursor = 0;
		_familyCursor = 0;
		_scheduledYear = 0;
		_lastCompletedYear = 0;
		_issuesThisPass = 0;
		_repairsThisPass = 0;
		_lastIssueCount = 0;
		_lastRepairCount = 0;
		_lookupMissesThisPass = 0;
		_lastLookupMissCount = 0;
	}

	private static void AuditMember(long actorId)
	{
		if (actorId <= 0L) return;
		if (!XjSectAuthorityStore.TryGetMember(actorId, out XuanJianVNext.Data.Sect.XjSectMemberArchiveRecord member)
			|| member == null || member.SectId <= 0L)
		{
			_issuesThisPass++;
			return;
		}

		if (!XjSectRepository.TryGetBySectId(member.SectId, out XuanJianVNext.Data.Sect.XjSectArchiveRecord sect)
			|| sect == null)
		{
			_issuesThisPass++;
			if (XjSectCommands.RemoveUnavailableMember(actorId, _scheduledYear)) _repairsThisPass++;
			return;
		}

		bool resolvedActor = XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null;
		if (!resolvedActor)
		{
			// A lookup miss is not a death certificate. 0.9.9.8 used to remove the
			// Sect member and then clear all actor runtime state here, so a temporary
			// WorldBox replacement/loading window could erase identity needed by CaiQi.
			_issuesThisPass++;
			_lookupMissesThisPass++;
			return;
		}
		if (!actor.isAlive())
		{
			_issuesThisPass++;
			if (XjSectCommands.RemoveUnavailableMember(actorId, _scheduledYear)) _repairsThisPass++;
			XjScheduler.ForgetUnavailableActor(actorId);
			return;
		}

		XjActorRegistry.Register(actor, out _);
		XjCultivatorCache.CheckAndUpdate(actor);

		long mirroredSectId = XjSectProjection.ReadActorMirrorSectId(actor);
		if (mirroredSectId != member.SectId)
		{
			_issuesThisPass++;
			XjSectAuthorityStore.MarkProjectionDirty(member.SectId);
			_repairsThisPass++;
		}
	}

	private static void AuditFamilyRuntimeMember(long actorId)
	{
		if (actorId <= 0L) return;
		bool resolvedActor = XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null;
		if (!resolvedActor)
		{
			// Keep live-family membership intact on a transient lookup miss. This member
			// may carry the persisted bloodline DaoTu used by CaiQi site preference.
			_issuesThisPass++;
			_lookupMissesThisPass++;
			return;
		}
		if (actor.isAlive()) return;

		_issuesThisPass++;
		XjFamilyMemberIndex.Shared.ForgetRuntimeActor(actorId);
		XjScheduler.ForgetUnavailableActor(actorId);
		_repairsThisPass++;
	}

	private static void CompletePass()
	{
		_lastIssueCount = _issuesThisPass;
		_lastRepairCount = _repairsThisPass;
		_lastLookupMissCount = _lookupMissesThisPass;
		_lastCompletedYear = Math.Max(_lastCompletedYear, _scheduledYear);
		XjPerformanceTelemetry.ObserveQueue("invariantIssues", _lastIssueCount);
		XjPerformanceTelemetry.ObserveQueue("invariantRepairs", _lastRepairCount);
		XjPerformanceTelemetry.ObserveQueue("invariantLookupMisses", _lastLookupMissCount);
		_memberActorIds = Array.Empty<long>();
		_familyActorIds = Array.Empty<long>();
		_cursor = 0;
		_familyCursor = 0;
		_scheduledYear = 0;
		_issuesThisPass = 0;
		_repairsThisPass = 0;
		_lookupMissesThisPass = 0;
	}
}
