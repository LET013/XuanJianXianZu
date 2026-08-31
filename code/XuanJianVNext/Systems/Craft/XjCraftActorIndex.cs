using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.Craft;

internal readonly struct XjCraftActorIndexEntry
{
	internal readonly long ActorId;
	internal readonly string TraitId;

	internal XjCraftActorIndexEntry(long actorId, string traitId)
	{
		ActorId = actorId;
		TraitId = traitId ?? string.Empty;
	}
}

/// <summary>
/// Sparse runtime index for the mutually-exclusive four arts. It is updated
/// only when a known actor is observed or a craft trait changes; codex and
/// task routing never scan ordinary population to discover craft actors.
/// </summary>
internal static class XjCraftActorIndex
{
	private static readonly Dictionary<long, string> TraitByActorId = new Dictionary<long, string>();
	private static long _revision;
	private static long _cachedRevision = -1L;
	private static IReadOnlyList<XjCraftActorIndexEntry> _cachedReadAll = Array.Empty<XjCraftActorIndexEntry>();

	internal static int Count => TraitByActorId.Count;

	internal static bool Contains(long actorId)
	{
		return actorId > 0L && TraitByActorId.ContainsKey(actorId);
	}

	internal static void Observe(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		string traitId = XjCraftTraitRules.GetPrimaryTraitId(actor);
		if (string.IsNullOrWhiteSpace(traitId))
		{
			Forget(actorId);
			return;
		}

		if (TraitByActorId.TryGetValue(actorId, out string existing)
			&& string.Equals(existing, traitId, StringComparison.Ordinal)) return;
		TraitByActorId[actorId] = traitId;
		TouchRevision();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Craft);
	}

	internal static void Forget(long actorId)
	{
		if (actorId > 0L && TraitByActorId.Remove(actorId))
		{
			TouchRevision();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Craft);
		}
	}

	internal static IReadOnlyList<XjCraftActorIndexEntry> ReadAll()
	{
		if (_cachedRevision == _revision) return _cachedReadAll;
		if (TraitByActorId.Count == 0)
		{
			_cachedReadAll = Array.Empty<XjCraftActorIndexEntry>();
			_cachedRevision = _revision;
			return _cachedReadAll;
		}

		List<XjCraftActorIndexEntry> result = new List<XjCraftActorIndexEntry>(TraitByActorId.Count);
		foreach (KeyValuePair<long, string> pair in TraitByActorId)
		{
			result.Add(new XjCraftActorIndexEntry(pair.Key, pair.Value));
		}
		result.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		_cachedReadAll = result;
		_cachedRevision = _revision;
		return _cachedReadAll;
	}

	internal static void Clear()
	{
		TraitByActorId.Clear();
		TouchRevision();
		_cachedReadAll = Array.Empty<XjCraftActorIndexEntry>();
		_cachedRevision = _revision;
	}

	private static void TouchRevision()
	{
		_revision = _revision == long.MaxValue ? 1L : _revision + 1L;
	}
}
