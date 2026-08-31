using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiSpawner
{
	private const int RespawnAge = 18;
	private const string AgeYearProcessedKey = "xuanjian.age_year_processed";

	/// <summary>PlaceMode 调用 — 直接传入 tile 执行放置。</summary>
	internal static XjDengMingShiCommandResult TryPlaceRecordInternal(string recordId, WorldTile tile)
	{
		if (!XjDengMingShiRegistry.TryGet(recordId, out XjDengMingShiRecord record))
			return new XjDengMingShiCommandResult(false, "未找到记录。");

		return TryCreateActorFromRecord(record, tile, out _)
			? new XjDengMingShiCommandResult(true, "已放置。")
			: new XjDengMingShiCommandResult(false, "放置失败。");
	}

	internal static XjDengMingShiCommandResult TryPlaceRecord(string recordId)
	{
		if (!XjDengMingShiRegistry.TryGet(recordId, out XjDengMingShiRecord record))
		{
			return new XjDengMingShiCommandResult(false, "未找到记录。");
		}

		WorldTile tile = TryGetTargetTile();
		if (tile == null)
		{
			return new XjDengMingShiCommandResult(false, "请先在地图上选择放置位置。");
		}

		return TryCreateActorFromRecord(record, tile, out Actor created)
			? new XjDengMingShiCommandResult(true, "已放置：" + SafeActorName(created, record.ActorName))
			: new XjDengMingShiCommandResult(false, "放置失败。");
	}

	internal static bool TryCreateActorFromRecord(XjDengMingShiRecord record, WorldTile targetTile, out Actor actor)
	{
		actor = null;
		ActorManager actorManager = TryGetActorManager();
		if (record == null || targetTile == null || actorManager == null)
		{
			return false;
		}

		string raceId = Normalize(record.RaceId);
		if (string.IsNullOrWhiteSpace(raceId) || AssetManager.actor_library?.get(raceId) == null)
		{
			return false;
		}

		try
		{
			// Use the native live-spawn path. loadObject is intended for save deserialization
			// and can leave a newly placed actor detached from simulation containers.
			actor = actorManager.spawnNewUnit(raceId, targetTile, false, false, 0f, null, false, true);
			if (actor?.data == null)
			{
				return false;
			}
			ResetPlacedActorAge(actor);
			XjActorStateWriteGateway.SetNativeName(
				actor,
				string.IsNullOrWhiteSpace(record.ActorName) ? "登名者" : record.ActorName.Trim(),
				customName: true);
			if (!XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				actor = null;
				return false;
			}

			RestoreVisibleTraits(actor, record.TraitIds);
			if (!RestoreVNextState(actor, record))
			{
				XjExceptionDiagnostics.Report("DengMingShi.RecordRestore",
					new InvalidOperationException("登名石记录恢复未闭合，已撤销本次生成。"));
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				actor = null;
				return false;
			}
			ResetPlacedActorAge(actor);
			actor.clearOldPath();
			actor.stopMovement();
			actor.clearTraitCache();
			actor.setStatsDirty();
			XjDengMingShiPostPlacement.Reconcile(actor);
			XjFuQiSwordWorldState.ReconcileRestoredActor(actor, record.SourceActorId, GetCurrentYear());
			XjDengMingShiRegistry.MarkPlaced(record.RecordId, GetCurrentYear());
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("DengMingShi.TryCreateActorFromRecord", ex);
			if (actor != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			actor = null;
			return false;
		}
	}

	private static void RestoreVisibleTraits(Actor actor, IReadOnlyList<string> traitIds)
	{
		if (actor?.data == null || traitIds == null)
		{
			return;
		}

		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			for (int i = 0; i < traitIds.Count; i++)
			{
				string traitId = Normalize(traitIds[i]);
				if (string.IsNullOrWhiteSpace(traitId) || AssetManager.traits?.get(traitId) == null || actor.hasTrait(traitId))
				{
					continue;
				}

				actor.addTrait(traitId, false);
			}
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	private static bool RestoreVNextState(Actor actor, XjDengMingShiRecord record)
	{
		if (actor?.data == null || record == null) return false;

		string realmId = ResolveRealmId(record);
		if (!XjCultivationPathRules.IsKnownPath(record.CultivationPath)
			&& string.IsNullOrWhiteSpace(realmId))
		{
			// 普通登名记录没有玄鉴修炼身份；不得用显示境界/默认分支把其
			// 反向解释成紫府金丹修士。
			return true;
		}
		string path = XjDengMingShiCultivationRestore.ResolvePath(
			actor,
			record.CultivationPath,
			realmId,
			record.GongFaName,
			record.FuQiState);
		if (!XjDengMingShiCultivationRestore.RestoreIdentity(
			actor,
			path,
			record.DaoTu,
			realmId,
			record.XjZz,
			record.FuQiState))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return false;
		}

		if (string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
		{
			RestoreZiJinProgression(actor, record);
			if (record.SchemaVersion >= 4)
			{
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, Math.Max(0f, record.ZhenYuan));
			}
		}

		string payloadKind = XjDengMingShiCultivationRestore.ResolvePayloadKind(
			record.HighRealmPayloadKind,
			realmId,
			record.JinDan,
			record.GuoWei,
			record.ShenDanGuoWei,
			record.ShenDanAnchorActorId,
			record.ShenDanYear);
		if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadShenDan, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreShenDan(
				actor,
				path,
				record.DaoTu,
				record.ShenDanGuoWei,
				record.ShenDanAnchorActorId,
				record.ShenDanAnchorName,
				record.ShenDanYear)) return false;
		}
		else if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadDaoTaiCarrier, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreDaoTaiPositionCarrier(
				actor, record.DaoTu, record.JinDan, record.GuoWei, record.JinDanSuccessYear,
				out string resolvedGuoWei)) return false;
			int successYear = record.JinDanSuccessYear > 0 ? record.JinDanSuccessYear : GetCurrentYear();
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(
				actor, string.IsNullOrWhiteSpace(record.DaoTu) ? "未定" : record.DaoTu, resolvedGuoWei, successYear);
		}
		else if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadJinDan, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreJinDan(
				actor, record.DaoTu, record.JinDan, record.GuoWei, record.JinDanSuccessYear,
				out string resolvedGuoWei)) return false;
			int successYear = record.JinDanSuccessYear > 0 ? record.JinDanSuccessYear : GetCurrentYear();
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(
				actor, string.IsNullOrWhiteSpace(record.DaoTu) ? "未定" : record.DaoTu, resolvedGuoWei, successYear);
		}

		// 0.9.6.5：紫府阶段的托果法门（神丹）与求位意向同样属于登名石载荷，
		// 不能只在已恢复金丹时导入。高境重复导入是幂等的，并且不增加道势。
		if (!string.IsNullOrWhiteSpace(record.HighRealmDaoStateJson))
		{
			int daoStateYear = Math.Max(1, record.JinDanSuccessYear > 0 ? record.JinDanSuccessYear : GetCurrentYear());
			if (!XjHighRealmDaoStateService.ImportActorStateJson(actor, record.HighRealmDaoStateJson, daoStateYear))
				XjHighRealmDaoStateService.EnsureRestoredState(actor, daoStateYear);
			if (XjJinDanAccessor.BuildPositionCarrierState(actor).Found)
				XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, daoStateYear);
		}

		if (!string.IsNullOrWhiteSpace(record.FaBaoId) && !string.IsNullOrWhiteSpace(record.FaBaoName))
		{
			XjFaBaoAccessor.WriteState(actor, new XjFaBaoState(
				true,
				record.FaBaoId,
				record.FaBaoName,
				record.FaBaoDaoTu,
				record.FaBaoClass,
				record.FaBaoSource,
				record.FaBaoYear,
				"DengMingShi"));
		}

		XjVisibleTraitSync.SyncCultivationTraits(actor);
		return true;
	}

	private static void RestoreZiJinProgression(Actor actor, XjDengMingShiRecord record)
	{
		if (record.GongFaCollectionVersion > 0 || !string.IsNullOrWhiteSpace(record.XianJiIds))
		{
			XjXianJiAccessor.RestoreSnapshot(actor, record.XianJiIds, record.XianJiLastYear);
		}
		if (!string.IsNullOrWhiteSpace(record.GongFaName) && record.GongFaGrade > 0)
		{
			XjGongFaAccessor.WriteState(actor, new XjGongFaState(
				true,
				record.GongFaName,
				record.GongFaGrade,
				0,
				0f,
				string.IsNullOrWhiteSpace(record.GongFaDaoTu) ? record.DaoTu : record.GongFaDaoTu,
				record.GongFaGrade > XjGongFaAccessor.MaxActiveGrade,
				"DengMingShi"));
		}
		if (!string.IsNullOrWhiteSpace(record.GongFaCollectionJson))
		{
			XjActorGongFaCollection.TryRestoreSerialized(
				actor,
				record.GongFaCollectionVersion,
				record.GongFaCollectionJson,
				"DengMingShiRegistry");
		}
		else
		{
			XjActorGongFaCollection.ReconcileWithActor(actor, "DengMingShiLegacyRecord");
		}
		if (!string.IsNullOrWhiteSpace(record.CaiQiFaName) && !string.IsNullOrWhiteSpace(record.CaiQiFaDaoTu))
		{
			XjCaiQiFaAccessor.WriteState(actor, new XjCaiQiFaState(
				true,
				record.CaiQiFaName,
				record.CaiQiFaDaoTu,
				record.CaiQiFaSourcePlace,
				record.CaiQiFaSourceYear,
				"DengMingShi"));
		}
		if (!string.IsNullOrWhiteSpace(record.QiuJinFa))
		{
			string boundAuthority = record.QiuJinFaBoundAuthority;
			if (string.IsNullOrWhiteSpace(boundAuthority))
			{
				boundAuthority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
					record.QiuJinFaSourceDaoTu,
					record.QiuJinFa,
					record.QiuJinFaSourceGongFaName);
			}
			XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
				true,
				record.QiuJinFa,
				record.QiuJinFaSourceGongFaName,
				record.QiuJinFaSourceGongFaGrade,
				record.QiuJinFaSourceDaoTu,
				true,
				record.QiuJinFaLastYear,
				"DengMingShi",
				boundAuthority));
		}
	}

	private static void ResetPlacedActorAge(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try
		{
			if (World.world != null)
			{
				actor.data.created_time = World.world.getCurWorldTime();
			}
		}
		catch (System.Exception xjCaught282) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiSpawner.cs:282", xjCaught282); }

		actor.data.age_overgrowth = RespawnAge;
		TrySetNumericMember(actor.data, "age", RespawnAge);
		TrySetNumericMember(actor.data, "death_time", 0f);
		TrySetNumericMember(actor.data, "time_to_die", 0f);
		TrySetBooleanMember(actor.data, "dead", false);
		TrySetBooleanMember(actor.data, "is_dead", false);
		TrySetBooleanMember(actor.data, "removed", false);
		XjActorStateWriteGateway.SetExternalInt(
			actor,
			AgeYearProcessedKey,
			RespawnAge - 1,
			XjActorStateDomain.Progression | XjActorStateDomain.Identity);
	}

	private static void TrySetNumericMember(object target, string memberName, float value)
	{
		XjNativeReflectionInterop.TryWriteNumericMember(target, memberName, value);
	}

	private static void TrySetBooleanMember(object target, string memberName, bool value)
	{
		XjNativeReflectionInterop.TryWriteBooleanMember(target, memberName, value);
	}

	private static WorldTile TryGetTargetTile()
	{
		try
		{
			return World.world?.getMouseTilePos();
		}
		catch
		{
			return null;
		}
	}

	private static ActorManager TryGetActorManager()
	{
		try
		{
			MapBox world = World.world;
			return world == null ? null : world.units;
		}
		catch
		{
			return null;
		}
	}

	private static string ResolveRealmId(XjDengMingShiRecord record)
	{
		// 0.9.9 起 RealmId 是唯一权威境界。Realm 只是展示文本，禁止在
		// 登名重塑时从“金丹/紫府/真君”等文字反推业务状态。
		return Normalize(record?.RealmId);
	}

	private static int GetCurrentYear()
	{
		try
		{
			return World.world == null ? 0 : (int)Math.Floor(Math.Max(0.0, World.world.getCurWorldTime()));
		}
		catch
		{
			return 0;
		}
	}

	private static string SafeActorName(Actor actor, string defaultName)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(defaultName) ? "登名者" : defaultName.Trim()) : name.Trim();
		}
		catch
		{
			return string.IsNullOrWhiteSpace(defaultName) ? "登名者" : defaultName.Trim();
		}
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
