using System;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiCultivationRestore
{
	internal const string PayloadNone = "None";
	internal const string PayloadJinDan = "JinDan";
	internal const string PayloadDaoTaiCarrier = "DaoTaiCarrier";
	internal const string PayloadShenDan = "ShenDan";

	internal static string ResolvePath(
		Actor actor,
		string storedPath,
		string realmId,
		string gongFaName,
		XjDengMingShiFuQiSnapshot fuQiState)
	{
		_ = realmId;
		_ = gongFaName;
		_ = fuQiState;
		if (XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _))
		{
			return XjCultivationPathIds.ZiFuJinDan;
		}
		string normalizedStored = (storedPath ?? string.Empty).Trim();
		if (XjCultivationPathRules.IsKnownPath(normalizedStored)) return normalizedStored;
		if (XjCultivationPathRules.TryGetPath(actor, out string livePath)) return livePath;

		// 0.9.9 不再从境界文字、功法名称或旧服气项目猜测修法。缺少权威 Path
		// 就让本次恢复失败，由上层撤销新肉身，避免猜错体系后制造长期半状态。
		return string.Empty;
	}

	internal static bool RestoreIdentity(
		Actor actor,
		string path,
		string daoTu,
		string realmId,
		int aptitude,
		XjDengMingShiFuQiSnapshot fuQiState)
	{
		if (actor?.data == null
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjCultivationPathRules.IsKnownPath(path)) return false;

		// 0.9.9 起登名石不再承担跨版本修法迁移。若新肉身已经带有另一套
		// 权威修法，说明保存载荷/ActorData 已不一致；宁可让本次生成事务失败并
		// 由调用方销毁新肉身，也不能先 ClearAll 再留下半恢复状态。
		if (XjCultivationPathRules.TryGetPath(actor, out string existingPath)
			&& !string.Equals(existingPath, path, StringComparison.Ordinal))
		{
			return false;
		}

		// 释修的完整真灵/金地/位次状态保存在 ActorData 与独立释修归档中，
		// 不能经过紫金/服气的 TrySetInitialPath（该入口会主动拒绝 Shi）。
		// 登名石这里只确认复制出来的释修权威状态完整，不做第二次初始化。
		if (string.Equals(path, XjCultivationPathIds.Shi, StringComparison.Ordinal))
		{
			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
				|| !XjShiCatalog.IsKnownRealm(shiRealm)) return false;
			if (!XjCultivationPathTransitions.SetPathMetadataForAuthorityTransition(actor, XjCultivationPathIds.Shi)) return false;
			XjCultivatorCache.CheckAndUpdate(actor);
			XjRuntimeActorInterestIndex.Observe(actor);
			return true;
		}

		if (aptitude > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, aptitude);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		}

		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out string favoredDaoTu))
		{
			path = XjCultivationPathIds.ZiFuJinDan;
			normalizedDaoTu = favoredDaoTu;
		}
		if (string.Equals(normalizedDaoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);
		}

		bool initialized;
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			XjDengMingShiFuQiSnapshotCodec.TryGetString(fuQiState, XjActorDataKeys.FuQiLineageId, out string lineage);
			XjDengMingShiFuQiSnapshotCodec.TryGetString(fuQiState, XjActorDataKeys.FuQiDaoTuRootId, out string rootId);
			if (string.IsNullOrWhiteSpace(lineage)
				&& string.IsNullOrWhiteSpace(normalizedDaoTu)
				&& string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
			{
				lineage = XjFuQiLineageIds.Sword;
			}
			initialized = XjCultivationPathTransitions.TrySetInitialPath(
				actor,
				path,
				normalizedDaoTu,
				lineage,
				syncVisibleTraits: false);
		}
		else
		{
			initialized = XjCultivationPathTransitions.TrySetInitialPath(
				actor,
				path,
				string.Empty,
				string.Empty,
				syncVisibleTraits: false);
		}
		if (!initialized) return false;

		if (!string.IsNullOrWhiteSpace(normalizedDaoTu)
			&& !XjCultivationStateTransitions.TrySetDaoTu(actor, normalizedDaoTu, false))
		{
			return false;
		}

		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			XjDengMingShiFuQiSnapshotCodec.Restore(actor, fuQiState);
		}

		if (!string.IsNullOrWhiteSpace(realmId)
			&& !XjCultivationStateTransitions.TrySetRealm(actor, realmId, false))
		{
			return false;
		}

		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal))
		{
			// 境界写入会刷新本命功法，但不能覆盖旧档保存的项目进度。
			XjDengMingShiFuQiSnapshotCodec.Restore(actor, fuQiState);
			XjFuQiMethodRules.ReconcileExclusiveZiJinData(actor);
			if (!XjFuQiCoreRouter.TryResolveActorCore(actor, out _)
				&& !string.IsNullOrWhiteSpace(normalizedDaoTu)
				&& XjDaoTuCatalog.TryResolve(normalizedDaoTu, out XjDaoTuDefinition daoTuDefinition))
			{
				XjFuQiCoreRouter.InitializeActorCore(actor, daoTuDefinition.RootId);
				// InitializeActorCore only repairs missing identity; restore saved progress after it.
				XjDengMingShiFuQiSnapshotCodec.Restore(actor, fuQiState);
			}
			if (XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition definition))
			{
				XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, XjAnnualExecutionContext.ResolveYear(actor));
			}
		}
		return true;
	}

	internal static string ResolvePayloadKind(
		string storedKind,
		string realmId,
		string jinXing,
		string guoWei,
		string shenDanGuoWei,
		long shenDanAnchorActorId,
		int shenDanYear)
	{
		string normalized = (storedKind ?? string.Empty).Trim();
		if (string.Equals(normalized, PayloadShenDan, StringComparison.Ordinal)
			|| string.Equals(normalized, PayloadDaoTaiCarrier, StringComparison.Ordinal)
			|| string.Equals(normalized, PayloadJinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, PayloadNone, StringComparison.Ordinal))
		{
			return normalized;
		}
		// 旧档若同时残留两套字段，神丹是最终结果，绝不能先恢复为活动金丹锚点。
		if (string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(shenDanGuoWei) && shenDanAnchorActorId > 0L && shenDanYear > 0))
		{
			return PayloadShenDan;
		}
		if (!string.IsNullOrWhiteSpace(jinXing) && !string.IsNullOrWhiteSpace(guoWei))
		{
			string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
			if (string.Equals(normalizedRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
				|| string.Equals(normalizedRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
			{
				return PayloadDaoTaiCarrier;
			}
			return PayloadJinDan;
		}
		return PayloadNone;
	}

	internal static bool RestoreDaoTaiPositionCarrier(
		Actor actor,
		string daoTu,
		string jinXing,
		string guoWei,
		int successYear,
		out string resolvedGuoWei)
	{
		resolvedGuoWei = string.Empty;
		if (actor?.data == null
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			|| string.IsNullOrWhiteSpace(jinXing)
			|| string.IsNullOrWhiteSpace(guoWei)) return false;

		int safeYear = successYear > 0 ? successYear : Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? "未定" : daoTu.Trim();
		resolvedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		if (string.IsNullOrWhiteSpace(resolvedGuoWei)) return false;

		bool isGuZunManifestation = XjActorAccessor.TryGetInt(
			actor, XjActorDataKeys.GuZunManifestation, out int guZunMarker) && guZunMarker > 0;
		// 登名石放出的真实道胎必须原位恢复，绝不能像普通金丹恢复那样自动改配
		// 其它余/闰位；原果位已被别人占据时直接让上层撤销本次生成。故尊命痕
		// 仅保留历史投影，不占用当世真实果位。
		if (!isGuZunManifestation
			&& !XjGuoWeiRegistry.TryClaim(actor, normalizedDaoTu, jinXing, resolvedGuoWei, safeYear))
		{
			resolvedGuoWei = string.Empty;
			return false;
		}

		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.WriteSuccess(actor, jinXing, resolvedGuoWei, safeYear);
		if (!isGuZunManifestation) XjShenDanRegistry.ReconcilePending();
		XjJinDanState restored = XjJinDanAccessor.BuildPositionCarrierState(actor);
		return restored.Found
			&& string.Equals(restored.GuoWei, resolvedGuoWei, StringComparison.Ordinal);
	}

	internal static bool RestoreJinDan(
		Actor actor,
		string daoTu,
		string jinXing,
		string guoWei,
		int successYear,
		out string resolvedGuoWei)
	{
		resolvedGuoWei = string.Empty;
		if (actor?.data == null
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjCultivationPathRules.IsJinDanEquivalent(actor)
			|| string.IsNullOrWhiteSpace(jinXing)
			|| string.IsNullOrWhiteSpace(guoWei)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int safeYear = successYear > 0 ? successYear : Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? "未定" : daoTu.Trim();
		resolvedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		bool isGuZunManifestation = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.GuZunManifestation, out int guZunMarker) && guZunMarker > 0;
		if (!isGuZunManifestation && !XjGuoWeiRegistry.TryClaim(actor, normalizedDaoTu, jinXing, resolvedGuoWei, safeYear))
		{
			string preferredType = XjGuoWeiRegistry.ResolveTypeFromName(resolvedGuoWei);
			if (!XjGuoWeiRegistry.TryResolveAvailableGuoWei(
				normalizedDaoTu,
				preferredType,
				actorId,
				actorId + safeYear,
				false,
				out _,
				out resolvedGuoWei)
				|| !XjGuoWeiRegistry.TryClaim(actor, normalizedDaoTu, jinXing, resolvedGuoWei, safeYear))
			{
				resolvedGuoWei = string.Empty;
				return false;
			}
		}
		// 故尊命痕保留生前果位投影以恢复法术/显示，但不写入真实果位占用账本。
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.WriteSuccess(actor, jinXing, resolvedGuoWei, safeYear);
		if (!isGuZunManifestation) XjShenDanRegistry.ReconcilePending();
		return true;
	}

	internal static bool RestoreShenDan(
		Actor actor,
		string path,
		string daoTu,
		string guoWei,
		long requestedAnchorActorId,
		string requestedAnchorName,
		int year)
	{
		if (actor?.data == null || !XjCultivationEligibility.CanReceiveXuanJianContent(actor)) return false;
		int safeYear = year > 0 ? year : Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ShenDan, false))
		{
			DowngradeInvalidShenDan(actor, path);
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		string preferredType = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);

		if (requestedAnchorActorId > 0L)
		{
			XjShenDanAccessor.WriteSuccess(actor, guoWei, requestedAnchorActorId, requestedAnchorName, safeYear);
			if (XjShenDanRegistry.TryRegister(actorId, requestedAnchorActorId))
			{
				XjShenDanRegistry.Observe(actor);
				return XjShenDanAccessor.BuildState(actor).Found;
			}
			XjShenDanAccessor.ClearSuccess(actor);
		}

		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(normalizedDaoTu)
			&& !string.IsNullOrWhiteSpace(preferredType)
			&& XjGuoWeiRegistry.TryFindActiveAnchor(
				normalizedDaoTu,
				preferredType,
				candidate => XjShenDanRegistry.CanAttachToAnchor(candidate, actorId, out _, out _),
				out XjGuoWeiRegistryEntry fallback))
		{
			XjShenDanAccessor.WriteSuccess(actor, fallback.GuoWei, fallback.ActorId, fallback.ActorName, safeYear);
			if (XjShenDanRegistry.TryRegister(actorId, fallback.ActorId)) return true;
			XjShenDanAccessor.ClearSuccess(actor);
		}

		DowngradeInvalidShenDan(actor, path);
		return false;
	}

	internal static void DowngradeInvalidShenDan(Actor actor, string path)
	{
		if (actor?.data == null) return;
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.ClearSuccess(actor);
		string fallbackRealm = string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)
			? XjRealmIds.FuQiZhenRen
			: XjRealmIds.ZiFu;
		XjCultivationStateTransitions.TrySetRealm(actor, fallbackRealm, false);
	}
}
