using System;
using System.Collections.Generic;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 妖民的事件驱动人口索引。
/// 只保存稳定 ActorId、原生物种 id 与小整数计数，不保存 Actor 引用，
/// 不拥有年度回调，也不扫描 World.world.units。旧档由现有 Actor 注册/载档
/// 流程逐个 Observe，思路与诡秘之主的 SequencePopulationIndex 一致。
/// </summary>
internal static class XjYaoShuPopulationIndex
{
	private readonly struct Snapshot : IEquatable<Snapshot>
	{
		internal readonly string AssetId;

		internal Snapshot(string assetId)
		{
			AssetId = assetId ?? string.Empty;
		}

		public bool Equals(Snapshot other)
		{
			return string.Equals(AssetId, other.AssetId, StringComparison.Ordinal);
		}
	}

	private static readonly Dictionary<long, Snapshot> ByActorId = new Dictionary<long, Snapshot>(64);
	private static readonly Dictionary<string, int> CountsByAssetId = new Dictionary<string, int>(StringComparer.Ordinal);

	internal static int TrackedActorCount => ByActorId.Count;

	internal static void ResetForNewWorld()
	{
		ByActorId.Clear();
		CountsByAssetId.Clear();
	}

	internal static void Observe(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		if (!TryBuildSnapshot(actor, out Snapshot current))
		{
			Remove(actorId);
			return;
		}

		if (ByActorId.TryGetValue(actorId, out Snapshot previous))
		{
			if (previous.Equals(current)) return;
			ApplyDelta(previous.AssetId, -1, notifyExtinction: true);
		}

		ByActorId[actorId] = current;
		ApplyDelta(current.AssetId, 1, notifyExtinction: false);
	}

	internal static void Remove(long actorId)
	{
		if (actorId <= 0L || !ByActorId.TryGetValue(actorId, out Snapshot previous)) return;
		ByActorId.Remove(actorId);
		ApplyDelta(previous.AssetId, -1, notifyExtinction: true);
	}

	internal static int GetCount(string assetId)
	{
		if (string.IsNullOrWhiteSpace(assetId)) return 0;
		return CountsByAssetId.TryGetValue(assetId.Trim(), out int count) ? Math.Max(0, count) : 0;
	}

	private static bool TryBuildSnapshot(Actor actor, out Snapshot snapshot)
	{
		snapshot = default;
		try
		{
			if (!actor.isAlive() || !XjYaoShuSapientSpecies.IsYaoMin(actor)) return false;
			if (!XjYaoShuSapientSpecies.TryResolveSupportedAssetId(actor, out string assetId)) return false;
			snapshot = new Snapshot(assetId);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static void ApplyDelta(string assetId, int delta, bool notifyExtinction)
	{
		if (string.IsNullOrWhiteSpace(assetId) || delta == 0) return;
		assetId = assetId.Trim();
		CountsByAssetId.TryGetValue(assetId, out int before);
		int after = Math.Max(0, before + delta);
		if (after <= 0) CountsByAssetId.Remove(assetId);
		else CountsByAssetId[assetId] = after;

		if (notifyExtinction && before > 0 && after == 0)
		{
			XjYaoShuSapientSpecies.OnPopulationExtinct(assetId, Math.Max(0, XjYearTracker.CurrentYear));
		}
	}
}
