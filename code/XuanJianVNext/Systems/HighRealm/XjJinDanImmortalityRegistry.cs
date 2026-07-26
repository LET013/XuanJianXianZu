using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjJinDanImmortalityRegistry
{
	private static readonly Dictionary<long, XjJinDanImmortalityArchiveRecord> ByActorId = new Dictionary<long, XjJinDanImmortalityArchiveRecord>();

	internal static int Count => ByActorId.Count;

	internal static bool IsNaturalDeathExempt(Actor actor)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjRealmHelper.GetOrder(realmId) < XjRealmHelper.GetOrder(XjRealmIds.JinDan))
		{
			return false;
		}
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		if (shenDan.Found)
		{
			return XjScheduler.ResolveActor(shenDan.AnchorActorId, out Actor anchor)
				&& XjSafeCore.IsAliveActor(anchor);
		}
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (jinDan.Found && !string.IsNullOrWhiteSpace(jinDan.GuoWei)
			&& jinDan.GuoWei.IndexOf(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) >= 0)
		{
			return false;
		}
		return true;
	}

	internal static bool IsPastYinSiAttentionAge(Actor actor)
	{
		if (!IsNaturalDeathExempt(actor) || actor?.data == null)
		{
			return false;
		}
		if (XjShenDanAccessor.BuildState(actor).Found)
		{
			return false;
		}

		float lifespan = XjRealmLifespanService.GetFiniteYinSiAttentionLifespan(actor);
		if (lifespan <= 0f)
		{
			return false;
		}
		return Math.Max(0f, actor.getAge()) > lifespan;
	}

	internal static bool EnsureActivated(Actor actor, int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || actor?.data == null || !IsNaturalDeathExempt(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (ByActorId.TryGetValue(actorId, out XjJinDanImmortalityArchiveRecord existing))
		{
			bool changed = !existing.IsAlive;
			existing.IsAlive = true;
			if (changed) { XjWorldArchiveSystem.MarkChanged(); XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan); }
			return changed;
		}

		int year = Math.Max(0, currentYear);
		ByActorId.Add(actorId, new XjJinDanImmortalityArchiveRecord
		{
			ActorId = actorId,
			ActivatedYear = year,
			LastExposureUpdateYear = year,
			YinSiState = XjJinDanYinSiState.Hidden,
			IsAlive = true
		});
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan);
		return true;
	}

	internal static void MarkDead(Actor actor)
	{
		int currentYear = 0;
		try { currentYear = Math.Max(0, World.world?.map_stats?.year ?? 0); } catch { }
		MarkDead(actor, currentYear);
	}

	internal static void MarkDead(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !ByActorId.TryGetValue(actorId, out XjJinDanImmortalityArchiveRecord record)) return;
		if (!record.IsAlive && string.Equals(record.YinSiState, XjJinDanYinSiState.Dead, StringComparison.Ordinal)) return;
		record.IsAlive = false;
		record.YinSiState = XjJinDanYinSiState.Dead;
		record.LastKnownYear = Math.Max(record.LastKnownYear, Math.Max(0, currentYear));
		XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan | XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
	}

	internal static bool TryGet(long actorId, out XjJinDanImmortalityArchiveRecord record) => ByActorId.TryGetValue(actorId, out record);

	internal static IReadOnlyList<XjJinDanImmortalityArchiveRecord> ReadAll()
	{
		if (ByActorId.Count == 0) return Array.Empty<XjJinDanImmortalityArchiveRecord>();
		List<XjJinDanImmortalityArchiveRecord> result = new List<XjJinDanImmortalityArchiveRecord>(ByActorId.Count);
		foreach (XjJinDanImmortalityArchiveRecord record in ByActorId.Values)
		{
			if (record != null) result.Add(Clone(record));
		}
		result.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		return result;
	}

	internal static void ExportArchiveRecords(List<XjJinDanImmortalityArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		List<long> ids = new List<long>(ByActorId.Keys);
		ids.Sort();
		for (int i = 0; i < ids.Count; i++) target.Add(Clone(ByActorId[ids[i]]));
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjJinDanImmortalityArchiveRecord> source)
	{
		ByActorId.Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord record = source[i];
			if (record == null || record.ActorId <= 0L) continue;
			ByActorId[record.ActorId] = Clone(record);
		}
	}

	internal static void Clear() => ByActorId.Clear();

	private static XjJinDanImmortalityArchiveRecord Clone(XjJinDanImmortalityArchiveRecord source)
	{
		return new XjJinDanImmortalityArchiveRecord
		{
			ActorId = source.ActorId,
			ActivatedYear = source.ActivatedYear,
			YinSiExposure = Math.Max(0f, source.YinSiExposure),
			LastExposureReason = source.LastExposureReason ?? string.Empty,
			LastExposureUpdateYear = source.LastExposureUpdateYear,
			YinSiKnown = source.YinSiKnown,
			YinSiState = string.IsNullOrWhiteSpace(source.YinSiState) ? XjJinDanYinSiState.Hidden : source.YinSiState,
			PursuitCount = Math.Max(0, source.PursuitCount),
			LastKnownYear = source.LastKnownYear,
			IsAlive = source.IsAlive
		};
	}
}
