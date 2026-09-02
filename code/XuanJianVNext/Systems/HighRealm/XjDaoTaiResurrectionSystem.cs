using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 道胎的不灭后手。前层守卫负责让普通伤害不能致死；若有模组绕过 Actor.die
/// 直接走到 ActorManager.destroyObject，本系统在销毁前封存道胎载荷，并在原生
/// 销毁事务结束后的下一帧以新 ActorId 重塑真身，再交接全部运行时身份。
/// </summary>
internal static class XjDaoTaiResurrectionSystem
{
	private const int MaxResurrectionsPerTick = 2;
	private const int MaxRetryCount = 180;
	private const float ResurrectionInvincibleSeconds = 20f;

	private sealed class PendingResurrection
	{
		internal long SourceActorId;
		internal string AssetId = string.Empty;
		internal string ActorName = string.Empty;
		internal List<string> SavedTraits = new List<string>();
		internal XjDengMingShiManager.HighRealmSnapshot HighRealm;
		internal int TileX;
		internal int TileY;
		internal int NotBeforeFrame;
		internal int RetryCount;
		internal bool SourceClaimReleased;
	}

	private static readonly Dictionary<long, PendingResurrection> PendingBySourceId =
		new Dictionary<long, PendingResurrection>();
	private static readonly List<long> TickBuffer = new List<long>(8);
	private static bool worldIsClearing;

	internal static void SetWorldClearing(bool value)
	{
		worldIsClearing = value;
		if (value) PendingBySourceId.Clear();
	}

	internal static void Clear()
	{
		PendingBySourceId.Clear();
		TickBuffer.Clear();
		worldIsClearing = false;
	}

	internal static void QueueFinalDestruction(Actor actor)
	{
		try
		{
			if (worldIsClearing || actor?.data == null
				|| XjExternalUnitTransferContext.IsExplicitReplacementRemoval(actor)
				|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval)
				|| !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return;

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L || PendingBySourceId.ContainsKey(actorId)) return;
			XjDengMingShiManager.HighRealmSnapshot snapshot = XjDengMingShiManager.CaptureHighRealmForArchive(actor);
			if (snapshot == null || string.IsNullOrWhiteSpace(snapshot.RealmId)) return;

			WorldTile tile = ((BaseSimObject)actor).current_tile;
			PendingResurrection pending = new PendingResurrection
			{
				SourceActorId = actorId,
				AssetId = XjDengMingShiManager.ResolveActorAssetIdForArchive(actor),
				ActorName = actor.getName() ?? string.Empty,
				HighRealm = snapshot,
				TileX = tile?.pos.x ?? Math.Max(0, MapBox.width / 2),
				TileY = tile?.pos.y ?? Math.Max(0, MapBox.height / 2),
				NotBeforeFrame = Time.frameCount + 1
			};
			if (actor.data.saved_traits != null)
			{
				for (int i = 0; i < actor.data.saved_traits.Count; i++)
				{
					string traitId = (actor.data.saved_traits[i] ?? string.Empty).Trim();
					if (!string.IsNullOrWhiteSpace(traitId)) pending.SavedTraits.Add(traitId);
				}
			}
			PendingBySourceId.Add(actorId, pending);
		}
		catch (Exception exception)
		{
			// 最终销毁钩子必须 fail-soft；坏 Actor 不能借道胎保护把原生销毁队列卡住。
			XjExceptionDiagnostics.Report("DaoTaiResurrection.Capture", exception);
		}
	}

	internal static void Tick()
	{
		if (worldIsClearing || PendingBySourceId.Count == 0 || World.world?.units == null) return;
		TickBuffer.Clear();
		foreach (long actorId in PendingBySourceId.Keys) TickBuffer.Add(actorId);
		int processed = 0;
		for (int i = 0; i < TickBuffer.Count && processed < MaxResurrectionsPerTick; i++)
		{
			if (!PendingBySourceId.TryGetValue(TickBuffer[i], out PendingResurrection pending) || pending == null) continue;
			if (Time.frameCount < pending.NotBeforeFrame) continue;
			processed++;
			TryResolvePending(pending);
		}
	}

	private static void TryResolvePending(PendingResurrection pending)
	{
		if (worldIsClearing || pending == null) return;
		Actor source = World.world?.units?.get(pending.SourceActorId);
		if (source?.data != null && source.isAlive())
		{
			// 原生销毁队列被取消或第三方已经把对象救回，绝不制造第二个道胎。
			PendingBySourceId.Remove(pending.SourceActorId);
			return;
		}

		WorldTile tile = ResolveSpawnTile(pending);
		if (tile == null)
		{
			RetryOrDrop(pending, "NoWorldTile");
			return;
		}

		try
		{
			if (!pending.SourceClaimReleased)
			{
				XjGuoWeiRegistry.ReleaseForActor(pending.SourceActorId, pending.HighRealm.GuoWei);
				XjGuoWeiQuanBingRegistry.RemoveActor(pending.SourceActorId);
				pending.SourceClaimReleased = true;
			}

			if (!XjDengMingShiManager.TrySpawnDaoTaiResurrection(
				tile, pending.AssetId, pending.ActorName, pending.SavedTraits, pending.HighRealm, out Actor reborn))
			{
				RetryOrDrop(pending, "SpawnOrRestoreFailed");
				return;
			}

			XjAvbsTraitPackInterop.RebindAfterDaoTaiResurrection(pending.SourceActorId, reborn);
			XjDengMingShiPostPlacement.Reconcile(reborn);
			RestoreInvulnerableBody(reborn);
			PendingBySourceId.Remove(pending.SourceActorId);
		}
		catch (Exception exception)
		{
			XjExceptionDiagnostics.Report("DaoTaiResurrection.Resolve", exception);
			RetryOrDrop(pending, "Exception");
		}
	}

	private static WorldTile ResolveSpawnTile(PendingResurrection pending)
	{
		if (World.world == null || MapBox.width <= 0 || MapBox.height <= 0) return null;
		int x = Mathf.Clamp(pending.TileX, 0, MapBox.width - 1);
		int y = Mathf.Clamp(pending.TileY, 0, MapBox.height - 1);
		return World.world.GetTileSimple(x, y);
	}

	private static void RestoreInvulnerableBody(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			actor.updateStats();
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
			actor.setHealth(Math.Max(1, Mathf.CeilToInt(maxHealth)));
			((BaseSimObject)actor).addStatusEffect("invincible", ResurrectionInvincibleSeconds, true);
			XjActorAggroBridge.ClearTargets(actor);
			actor.setStatsDirty();
		}
		catch (Exception exception)
		{
			XjExceptionDiagnostics.Report("DaoTaiResurrection.RestoreBody", exception);
		}
	}

	private static void RetryOrDrop(PendingResurrection pending, string reason)
	{
		pending.RetryCount++;
		if (pending.RetryCount > MaxRetryCount)
		{
			PendingBySourceId.Remove(pending.SourceActorId);
			XjExceptionDiagnostics.Report("DaoTaiResurrection.GaveUp." + reason,
				new InvalidOperationException("道胎重塑重试超限: " + pending.SourceActorId));
			return;
		}
		pending.NotBeforeFrame = Time.frameCount + 1;
	}
}
