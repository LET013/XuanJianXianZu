using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjFuQiToZiFuTargetOption
{
	internal readonly string DaoTu;
	internal readonly string RootId;
	internal readonly bool IsCurrentDaoTu;
	internal readonly bool IsCurrentRoot;

	internal XjFuQiToZiFuTargetOption(string daoTu, string rootId, bool isCurrentDaoTu, bool isCurrentRoot)
	{
		DaoTu = daoTu ?? string.Empty;
		RootId = rootId ?? string.Empty;
		IsCurrentDaoTu = isCurrentDaoTu;
		IsCurrentRoot = isCurrentRoot;
	}
}

internal readonly struct XjFuQiToZiFuResult
{
	internal readonly bool Success;
	internal readonly string Message;
	internal readonly string SourceDaoTu;
	internal readonly string TargetDaoTu;
	internal readonly int CoreProgressBasisPoints;
	internal readonly int GrantedShenTongCount;
	internal readonly string[] GrantedShenTongIds;

	internal XjFuQiToZiFuResult(
		bool success,
		string message,
		string sourceDaoTu,
		string targetDaoTu,
		int coreProgressBasisPoints,
		int grantedShenTongCount,
		string[] grantedShenTongIds)
	{
		Success = success;
		Message = message ?? string.Empty;
		SourceDaoTu = sourceDaoTu ?? string.Empty;
		TargetDaoTu = targetDaoTu ?? string.Empty;
		CoreProgressBasisPoints = Math.Clamp(coreProgressBasisPoints, 0, 10000);
		GrantedShenTongCount = Math.Clamp(grantedShenTongCount, 0, XjXianJiState.MaxCount);
		GrantedShenTongIds = grantedShenTongIds ?? Array.Empty<string>();
	}
}

/// <summary>
/// 服气真人可永久放弃服气养性，单向转入紫府金丹道。转修只承认真人阶段
/// 对本命核心的圆满温养进度；入黄冠前的凝核进度不作为紫府神通数量依据。
/// 整个写入先完成目标道途与神通集合预校验，再一次性切换身份。
/// </summary>
internal static class XjFuQiToZiFuTransitionSystem
{
	internal const float ZiFuMinimumZhenYuan = 36000f;
	private const string ConversionSource = "服气真人转修紫府";

	internal static bool CanConvert(Actor actor, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !XjSafeCore.IsAliveActor(actor))
		{
			reason = "角色已经失效";
			return false;
		}
		if (!XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			reason = "只有服气养性修士可以转修";
			return false;
		}
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			reason = "只有服气真人可以转修紫府";
			return false;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiToZiFuConvertedYear, out int convertedYear)
			&& convertedYear > 0)
		{
			reason = "该角色已经完成过转修";
			return false;
		}
		int currentYear = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int injuryUntilYear)
			&& injuryUntilYear >= currentYear)
		{
			reason = "求证反伤尚未痊愈，不能借转修清除伤势";
			return false;
		}
		if (HasIrreversibleZiJinClaim(actor))
		{
			// TrySetRealm(ZiFu) 会释放金丹果位、权柄与神丹依附运行态。服气真人
			// 正常不可能持有这些身份；若旧档或手动旁路留下了冲突态，先拒绝转修，
			// 避免后续任一写入失败时只能恢复角色字段、却无法原样复建世界唯一权柄。
			reason = "角色存在紫金高境权柄残留，请先通过读档校准后再转修";
			return false;
		}
		return true;
	}

	private static bool HasIrreversibleZiJinClaim(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string jinDanGuoWei)
			&& !string.IsNullOrWhiteSpace(jinDanGuoWei))
		{
			return true;
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanGuoWei, out string shenDanGuoWei)
			&& !string.IsNullOrWhiteSpace(shenDanGuoWei))
		{
			return true;
		}
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjShenDanAnchorActorId, out long anchorActorId)
			&& anchorActorId > 0L)
		{
			return true;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJieLinXian, out int jieLinXian)
			&& jieLinXian > 0)
		{
			return true;
		}
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingSnapshotSchema, out int quanBingSchema)
			&& quanBingSchema > 0;
	}

	internal static IReadOnlyList<XjFuQiToZiFuTargetOption> BuildTargetOptions(Actor actor)
	{
		List<XjFuQiToZiFuTargetOption> result = new List<XjFuQiToZiFuTargetOption>();
		if (!CanConvert(actor, out _)) return result;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string sourceDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out string storedRootId);
		string sourceRootId = string.Empty;
		if (!XjDaoTuCatalog.TryResolveRootId(sourceDaoTu, out sourceRootId)) sourceRootId = (storedRootId ?? string.Empty).Trim();

		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.Entries;
		for (int i = 0; i < entries.Count; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			string daoTu = (entry.DisplayName ?? string.Empty).Trim();
			if (daoTu.Length == 0 || !seen.Add(daoTu)
				|| !XjDaoTuCatalog.TryResolve(daoTu, out XjDaoTuDefinition definition)
				|| !definition.SupportsZiJin
				|| !XjDaoTuManifestRegistry.CanManifestZiJin(definition.RootId))
			{
				continue;
			}
			if (string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
				&& !XjWeaponArtSystem.TryGetSwordIntent(actor, out _))
			{
				continue;
			}
			result.Add(new XjFuQiToZiFuTargetOption(
				daoTu,
				definition.RootId,
				string.Equals(daoTu, (sourceDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal),
				string.Equals(definition.RootId, sourceRootId, StringComparison.Ordinal)));
		}

		result.Sort((left, right) =>
		{
			int c = right.IsCurrentDaoTu.CompareTo(left.IsCurrentDaoTu);
			if (c != 0) return c;
			c = right.IsCurrentRoot.CompareTo(left.IsCurrentRoot);
			if (c != 0) return c;
			bool leftBingGu = XjDaoTuCatalog.IsBingGu(left.DaoTu);
			bool rightBingGu = XjDaoTuCatalog.IsBingGu(right.DaoTu);
			c = leftBingGu.CompareTo(rightBingGu);
			return c != 0 ? c : string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal);
		});
		return result;
	}

	internal static int ResolveCoreProgressBasisPoints(Actor actor, int currentYear)
	{
		if (actor?.data == null) return 0;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectedYear)
			&& perfectedYear > 0)
		{
			return 10000;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, out int startYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, out int completeYear);
		if (startYear <= 0 || completeYear <= startYear)
		{
			// 五品真人在晋升当年即被分流，尚未建立温养项目。此时按既成
			// 真人根基折算为四门神通，而不是退回一门神通重新起步。
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, out int checkedFlag)
				&& checkedFlag > 0
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, out int stayFuQi)
				&& stayFuQi <= 0) return 7500;
			return 0;
		}
		int safeYear = Math.Max(startYear, currentYear);
		long numerator = (long)(safeYear - startYear) * 10000L;
		int progress = (int)(numerator / Math.Max(1, completeYear - startYear));
		return Math.Clamp(progress, 0, 10000);
	}

	internal static int ResolveGrantedShenTongCount(int progressBasisPoints)
	{
		int progress = Math.Clamp(progressBasisPoints, 0, 10000);
		if (progress >= 10000) return 5;
		if (progress >= 7500) return 4;
		if (progress >= 5000) return 3;
		if (progress >= 2500) return 2;
		return 1;
	}

	internal static bool TryPreview(
		Actor actor,
		string targetDaoTu,
		out int progressBasisPoints,
		out string[] shenTongIds,
		out string reason)
	{
		progressBasisPoints = 0;
		shenTongIds = Array.Empty<string>();
		if (!CanConvert(actor, out reason)) return false;
		int year = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		progressBasisPoints = ResolveCoreProgressBasisPoints(actor, year);
		int count = ResolveGrantedShenTongCount(progressBasisPoints);
		return TryBuildTargetShenTongSet(actor, targetDaoTu, count, out shenTongIds, out reason);
	}

	internal static bool TryConvert(Actor actor, string targetDaoTu, out XjFuQiToZiFuResult result)
	{
		result = new XjFuQiToZiFuResult(false, "转修未执行", string.Empty, targetDaoTu, 0, 0, Array.Empty<string>());
		if (!CanConvert(actor, out string reason))
		{
			result = new XjFuQiToZiFuResult(false, reason, string.Empty, targetDaoTu, 0, 0, Array.Empty<string>());
			return false;
		}

		int currentYear = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string sourceDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out string sourceRootId);
		if (string.IsNullOrWhiteSpace(sourceDaoTu)
			&& XjDaoTuCatalog.TryGetByRootId(sourceRootId, out XjDaoTuDefinition sourceDefinition))
		{
			sourceDaoTu = sourceDefinition.DisplayName;
		}
		if (string.Equals((sourceRootId ?? string.Empty).Trim(), XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			// 长庚尚未由首位真君羽士命名时，人物列传不得提前泄露后世道途名。
			sourceDaoTu = "无名剑道";
		}
		int progressBasisPoints = ResolveCoreProgressBasisPoints(actor, currentYear);
		int grantedCount = ResolveGrantedShenTongCount(progressBasisPoints);
		if (!TryBuildTargetShenTongSet(actor, targetDaoTu, grantedCount, out string[] shenTongIds, out reason))
		{
			result = new XjFuQiToZiFuResult(false, reason, sourceDaoTu, targetDaoTu, progressBasisPoints, 0, Array.Empty<string>());
			return false;
		}
		if (!XjDaoTuCatalog.TryResolve(targetDaoTu, out XjDaoTuDefinition targetDefinition))
		{
			result = new XjFuQiToZiFuResult(false, "目标道途无效", sourceDaoTu, targetDaoTu, progressBasisPoints, 0, Array.Empty<string>());
			return false;
		}

		string normalizedTarget = (targetDaoTu ?? string.Empty).Trim();
		TransitionSnapshot snapshot = TransitionSnapshot.Capture(actor);
		try
		{
			if (!XjCultivationPathTransitions.SetPathMetadataForAuthorityTransition(actor, XjCultivationPathIds.ZiFuJinDan))
				throw new InvalidOperationException("紫府金丹修法写入失败");
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, normalizedTarget);
			XjGongFaAccessor.Clear(actor);
			XjXianJiAccessor.RestoreSnapshot(actor, string.Empty, 0);
			ClearZiJinSpecialState(actor);
			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false))
			{
				throw new InvalidOperationException("紫府境界写入失败");
			}

			ConfigureTargetSpecialState(actor, targetDefinition.RootId, shenTongIds, currentYear);
			if (!XjXianJiAccessor.RestoreSnapshot(actor, string.Join("|", shenTongIds), currentYear))
			{
				throw new InvalidOperationException("目标神通集合写入失败");
			}
			if (!XjActorGongFaCollection.ReplaceAllForManualDaoTu(actor, normalizedTarget, shenTongIds, ConversionSource))
			{
				throw new InvalidOperationException("一神通一功法生成失败");
			}
			XjActorGongFaCollection.ReconcileWithActor(actor, ConversionSource);
			if (!ValidateConvertedCultivationSet(actor, normalizedTarget, shenTongIds, out string validationReason))
			{
				throw new InvalidOperationException(validationReason);
			}
			XjQiuJinFaAccessor.Clear(actor, ConversionSource);
			XjQiuJinFaSystem.ResetEligibility(actor);
			if (shenTongIds.Length >= XjXianJiState.MaxCount)
			{
				// 五神通齐备者从转修当年重新起算完整求金法周期；不能继承服气
				// 年代，也不能被上面的清理永久抹掉求金资格。
				XjQiuJinFaSystem.ActivateEligibility(actor, currentYear);
			}
			XjJinDanAccessor.ClearSuccess(actor);
			XjShenDanAccessor.ClearSuccess(actor);

			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float currentZhenYuan);
			if (float.IsNaN(currentZhenYuan) || float.IsInfinity(currentZhenYuan)) currentZhenYuan = 0f;
			float convertedFloor = ResolveConvertedZiFuZhenYuanFloor(shenTongIds.Length);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, Math.Max(convertedFloor, currentZhenYuan));
			ApplyHighAgeConversionGrace(actor, currentYear);

			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiSensedQiId, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSenseResult, XjFuQiSensingSystem.ResultPending);
			XjFuQiCoreRouter.ClearActorCore(actor);
			XjCultivationPathTransitions.ResetFuQiProgress(actor);

			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiToZiFuConvertedYear, currentYear);
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiToZiFuSourceDaoTu, sourceDaoTu ?? string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiToZiFuTargetDaoTu, normalizedTarget);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiToZiFuCoreProgress, progressBasisPoints);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiToZiFuGrantedShenTongCount, shenTongIds.Length);
		}
		catch (Exception ex)
		{
			snapshot.Restore(actor);
			try { RefreshIndexes(actor); } catch (System.Exception xjCaught330) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiToZiFuTransitionSystem.cs:330", xjCaught330); }
			result = new XjFuQiToZiFuResult(
				false,
				"转修失败，状态已回滚：" + ex.Message,
				sourceDaoTu,
				targetDaoTu,
				progressBasisPoints,
				0,
				Array.Empty<string>());
			return false;
		}

		// 自此角色身份、境界、神通、真实功法与服气状态已经原子提交。
		// 世界显现、三书、称号与缓存均为提交后投影，单项失败不得反向回滚
		// 已经成立的角色事实，避免出现历史已写入而角色又恢复服气的分裂状态。
		long actorId = ((BaseSystemData)actor.data).id;
		RunPostCommit("DaoTuManifest", () =>
		{
			if (!XjDaoTuManifestRegistry.MarkZiJinManifested(targetDefinition.RootId, actorId, currentYear))
			{
				throw new InvalidOperationException("紫金道途显现登记失败");
			}
		});
		RunPostCommit("ThreeBookConversion", () => XjThreeBookWriter.RecordFuQiToZiFuConversion(
			actor, sourceDaoTu, normalizedTarget, progressBasisPoints, shenTongIds, currentYear));
		RunPostCommit("ThreeBookShenTong", () =>
		{
			for (int i = 0; i < shenTongIds.Length; i++)
			{
				XjThreeBookWriter.RecordShenTongComprehended(
					actor, shenTongIds[i], currentYear, "服气本命核心转化为紫府神通");
			}
			XjBroadcastSystem.AnnounceShenTongBatch(actor, shenTongIds, "服气本命核心转化为紫府神通");
		});
		if (string.Equals(targetDefinition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal)
			&& shenTongIds.Length == XjXianJiState.MaxCount)
		{
			RunPostCommit("QingXuanFiveShenTongHistory", () =>
				XjThreeBookWriter.RecordQingXuanFiveShenTongReady(actor, currentYear));
		}
		RunPostCommit("InheritanceSnapshot", () => XjGongFaProgression.PublishInheritanceSnapshot(actor, "FuQiToZiFu"));
		RunPostCommit("RealmTitle", () => XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, shenTongIds.Length));
		RunPostCommit("PostRealmWrite", () => XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			XjRealmIds.ZiFu,
			currentYear,
			applyMingShuReward: false,
			refreshBloodline: true,
			syncVisibleTraits: true,
			restoreHealth: true));
		RunPostCommit("IndexRefresh", () => RefreshIndexes(actor));

		result = new XjFuQiToZiFuResult(
			true,
			"转修成功，已获" + shenTongIds.Length + "门神通",
			sourceDaoTu,
			normalizedTarget,
			progressBasisPoints,
			shenTongIds.Length,
			shenTongIds);
		return true;
	}

	private static bool TryBuildTargetShenTongSet(
		Actor actor,
		string targetDaoTu,
		int count,
		out string[] ids,
		out string reason)
	{
		ids = Array.Empty<string>();
		reason = string.Empty;
		string normalized = (targetDaoTu ?? string.Empty).Trim();
		if (normalized.Length == 0
			|| !XjDaoTuCatalog.TryResolve(normalized, out XjDaoTuDefinition definition)
			|| !definition.SupportsZiJin
			|| !XjDaoTuManifestRegistry.CanManifestZiJin(definition.RootId)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			reason = "该道途尚不能以紫府金丹法修炼";
			return false;
		}
		if (string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
			&& !XjWeaponArtSystem.TryGetSwordIntent(actor, out _))
		{
			reason = "转修长庚须先拥有一己剑意";
			return false;
		}

		int required = Math.Clamp(count, 1, XjXianJiState.MaxCount);
		List<string> selected = new List<string>(required);
		if (string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			for (int i = 0; i < required; i++) selected.Add(XjZiJinSwordDaoCatalog.RequiredShenTongIds[i]);
		}
		else if (string.Equals(definition.RootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal))
		{
			string[] qingXuan = { "玄羊子", "伏青山", "青宣岳", "上岩神", "观地冥" };
			for (int i = 0; i < required; i++) selected.Add(qingXuan[i]);
		}
		else if (definition.IsBingGu)
		{
			string[] documented = XjZiJinUpperShenTongCatalog.GetNamesByDaoTu(definition.RootId);
			for (int i = 0; i < documented.Length && selected.Count < required; i++)
			{
				AddUnique(selected, documented[i]);
			}
			for (int ordinal = selected.Count + 1; selected.Count < required; ordinal++)
			{
				AddUnique(selected, definition.DisplayName + "神通·" + ChineseOrdinal(ordinal));
			}
		}
		else
		{
			long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
			long seed = actorId + XjDeterministicHash.StableHash(normalized + "|fuqi_to_zifu");
			for (int ordinal = 1; ordinal <= required; ordinal++)
			{
				string[] existing = selected.ToArray();
				string pickedId;
				bool picked;
				if (actor != null && actor.hasTrait("ChuShen8"))
				{
					picked = XjXianJiCatalog.TryPickDaoZhuForProgression(
						normalized, ordinal, seed, existing,
						XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(normalized), out pickedId);
				}
				else if (ordinal <= 3)
				{
					picked = XjXianJiCatalog.TryPickUpperForProgression(normalized, ordinal, seed, existing, out pickedId);
				}
				else
				{
					picked = XjXianJiCatalog.TryPickForProgression(
						normalized,
						ordinal,
						seed,
						existing,
						XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(normalized),
						!XjLongShuSystem.IsLongShu(actor),
						out pickedId);
				}
				if (!picked || string.IsNullOrWhiteSpace(pickedId) || !AddUnique(selected, pickedId))
				{
					reason = "目标道途无法生成足够的神通";
					return false;
				}
			}
		}

		if (selected.Count != required)
		{
			reason = "目标道途神通集合不完整";
			return false;
		}
		ids = selected.ToArray();
		return true;
	}

	private static bool ValidateConvertedCultivationSet(Actor actor, string targetDaoTu, string[] shenTongIds, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || shenTongIds == null || shenTongIds.Length == 0)
		{
			reason = "转修结果缺少神通";
			return false;
		}
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Count != shenTongIds.Length
			|| XjXianJiAccessor.GetEffectiveShenTongCount(actor) != shenTongIds.Length)
		{
			reason = "神通集合与本命核心转化数量不一致";
			return false;
		}

		HashSet<string> expected = new HashSet<string>(shenTongIds, StringComparer.Ordinal);
		IReadOnlyList<XjActorGongFaCollection.Record> records = XjActorGongFaCollection.ReadRecords(actor);
		if (records.Count != shenTongIds.Length)
		{
			reason = "真实功法数量与神通数量不一致";
			return false;
		}
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < records.Count; i++)
		{
			XjActorGongFaCollection.Record record = records[i];
			if (record.Grade != 5
				|| !string.Equals(record.DaoTu, targetDaoTu, StringComparison.Ordinal)
				|| !expected.Contains(record.MappedXianJi)
				|| !mapped.Add(record.MappedXianJi))
			{
				reason = "一神通一部五品功法映射校验失败";
				return false;
			}
		}
		return mapped.Count == expected.Count;
	}

	private static void ConfigureTargetSpecialState(Actor actor, string rootId, string[] ids, int currentYear)
	{
		if (string.Equals(rootId, XjDaoTuRootIds.QingXuan, StringComparison.Ordinal))
		{
			int mask = 0;
			for (int i = 0; i < ids.Length; i++)
			{
				switch ((ids[i] ?? string.Empty).Trim())
				{
					case "玄羊子": XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, 1); break;
					case "伏青山": mask |= 1 << 1; break;
					case "青宣岳": mask |= 1 << 2; break;
					case "上岩神": mask |= 1 << 3; break;
					case "观地冥": mask |= 1 << 4; break;
				}
			}
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanRaisedShenTongMask, mask);
			if (ids.Length == XjXianJiState.MaxCount)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReady, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReadyYear, currentYear);
			}
		}
		else if (string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			int mask = ids.Length >= XjXianJiState.MaxCount
				? XjZiJinSwordDaoCatalog.FullMask
				: (1 << ids.Length) - 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecisionYear, currentYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, currentYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchMask, mask);
			if (mask == XjZiJinSwordDaoCatalog.FullMask)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCompletedYear, currentYear);
			}
		}
	}

	private static void ClearZiJinSpecialState(Actor actor)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanQingCanQi, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanChuYangJi, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanKongZhengCompleted, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanRaisedShenTongMask, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.QingXuanUpliftTargetId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUpliftCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanFiveShenTongReadyYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecisionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordLastAnnualYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCompletedYear, 0);
	}

	private static float ResolveConvertedZiFuZhenYuanFloor(int shenTongCount)
	{
		return Math.Clamp(shenTongCount, 1, XjXianJiState.MaxCount) switch
		{
			>= 5 => 129600f,
			4 => 112000f,
			3 => 90000f,
			2 => 72000f,
			_ => 54000f
		};
	}

	private static void ApplyHighAgeConversionGrace(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		float lifespan = XjVanillaDeathGuard.GetEffectiveLifespan(actor);
		float age = Math.Max(0f, actor.getAge());
		if (lifespan <= 0f || age < Math.Max(400f, lifespan * 0.75f)) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmNaturalDeathGraceUntilYear, out int existing);
		XjActorAccessor.SetInt(
			actor,
			XjActorDataKeys.RealmNaturalDeathGraceUntilYear,
			Math.Max(existing, currentYear + 180));
	}

	private static void RunPostCommit(string stage, Action action)
	{
		try { action?.Invoke(); }
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError("[玄鉴][服气转紫府] 提交后投影失败 " + stage + "：" + ex.GetType().Name);
		}
	}

	private static void RefreshIndexes(Actor actor)
	{
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
		XjFuQiCandidateIndex.Observe(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (System.Exception xjCaught614) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiToZiFuTransitionSystem.cs:614", xjCaught614); }
	}

	private static bool AddUnique(List<string> target, string value)
	{
		string normalized = (value ?? string.Empty).Trim();
		if (normalized.Length == 0) return false;
		for (int i = 0; i < target.Count; i++)
		{
			if (string.Equals(target[i], normalized, StringComparison.Ordinal)) return false;
		}
		target.Add(normalized);
		return true;
	}

	private static string ChineseOrdinal(int ordinal)
	{
		return ordinal switch { 1 => "一", 2 => "二", 3 => "三", 4 => "四", 5 => "五", _ => ordinal.ToString() };
	}

	private sealed class TransitionSnapshot
	{
		private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, int> _ints = new Dictionary<string, int>(StringComparer.Ordinal);
		private readonly Dictionary<string, float> _floats = new Dictionary<string, float>(StringComparer.Ordinal);
		private readonly Dictionary<string, long> _longs = new Dictionary<string, long>(StringComparer.Ordinal);

		private static readonly string[] StringKeys =
		{
			XjActorDataKeys.CultivationPath, XjActorDataKeys.FuQiLineageId,
			XjActorDataKeys.FuQiDaoTuRootId, XjActorDataKeys.FuQiCoreType, XjActorDataKeys.FuQiCoreId,
			XjActorDataKeys.FuQiSensedQiId, XjActorDataKeys.FuQiStudiedIntentIds,
			XjActorDataKeys.FuQiCurrentIntentId, XjActorDataKeys.FuQiShenMiaoId,
			XjActorDataKeys.DaoTu, XjActorDataKeys.RealmId,
			XjActorDataKeys.XjXianJiIds, XjActorDataKeys.XjShenTongIds,
			XjActorDataKeys.XjGongFaName, XjActorDataKeys.XjGongFaDaoTu, XjActorDataKeys.XjGongFaSource,
			XjActorDataKeys.XjGongFaCollectionJson,
			XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason,
			XjActorDataKeys.XjGongFaHighPromotionLastFailureReason,
			XjActorDataKeys.XjXianJiProjectId,
			XjActorDataKeys.XjQiuJinFaName, XjActorDataKeys.XjQiuJinFaSourceGongFaName,
			XjActorDataKeys.XjQiuJinFaSourceDaoTu, XjActorDataKeys.XjQiuJinFaBoundAuthority,
			XjActorDataKeys.XjQiuJinFaOrigin, XjActorDataKeys.XjQiuJinFaLastFailureReason,
			XjActorDataKeys.XjJinDanJinXing, XjActorDataKeys.XjJinDanGuoWei, XjActorDataKeys.XjJinDanPositionPursuit,
			XjActorDataKeys.XjJinDanFailedState, XjActorDataKeys.XjJinDanFailureNarrative,
			XjActorDataKeys.XjJinDanDeferredReason, XjActorDataKeys.XjQiuJinIntentReason,
			XjActorDataKeys.XjShenDanGuoWei, XjActorDataKeys.XjShenDanAnchorName,
			XjActorDataKeys.XjShenDanDeathAnchorName,
			XjActorDataKeys.XjQuanBingDaoTu, XjActorDataKeys.XjQuanBingGuoWei,
			XjActorDataKeys.XjQuanBingLocal, XjActorDataKeys.XjQuanBingSeized,
			XjActorDataKeys.XjQuanBingSeizedSources, XjActorDataKeys.XjQuanBingForeign,
			XjActorDataKeys.XjQuanBingWithdrawnToDongTian, XjActorDataKeys.XjQuanBingGuoWeiZhongAi,
			XjActorDataKeys.XjQuanBingPendingExternalZhengWeiDaoTu, XjActorDataKeys.XjQuanBingSummary,
			XjActorDataKeys.XjBottleneckActiveId, XjActorDataKeys.XjBottleneckLastResult,
			XjActorDataKeys.QingXuanUpliftTargetId,
			XjActorDataKeys.FuQiToZiFuSourceDaoTu, XjActorDataKeys.FuQiToZiFuTargetDaoTu
		};

		private static readonly string[] IntKeys =
		{
			XjActorDataKeys.RealmEnteredYear, XjActorDataKeys.XjZiFuEnteredYear, XjActorDataKeys.RealmManualRemoved,
			XjActorDataKeys.XjXianJiCount, XjActorDataKeys.XjXianJiLastYear,
			XjActorDataKeys.XjGongFaGrade, XjActorDataKeys.XjGongFaStage,
			XjActorDataKeys.XjGongFaLastProgressionYear, XjActorDataKeys.XjGongFaLastExecutionYear,
			XjActorDataKeys.XjGongFaClockTargetGrade, XjActorDataKeys.XjGongFaClockEligibilityYear,
			XjActorDataKeys.XjGongFaGrade4NextAttemptYear, XjActorDataKeys.XjGongFaGrade5NextAttemptYear,
			XjActorDataKeys.XjGongFaCollectionVersion,
			XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, XjActorDataKeys.XjGongFaGrade5PromotionLastYear,
			XjActorDataKeys.XjGongFaHighPromotionFailureCount, XjActorDataKeys.XjGongFaHighPromotionLastYear,
			XjActorDataKeys.XjXianJiLastExecutionYear, XjActorDataKeys.XjXianJiClockTargetCount,
			XjActorDataKeys.XjXianJiClockEligibilityYear, XjActorDataKeys.XjXianJiLastLogicalAttemptYear,
			XjActorDataKeys.XjXianJiFailureCount, XjActorDataKeys.XjXianJiProjectTargetCount,
			XjActorDataKeys.XjXianJiProjectCompleteYear, XjActorDataKeys.XjXianJiProjectLastProposalYear,
			XjActorDataKeys.XjXianJiLectureAidYear, XjActorDataKeys.XjXianJiLectureAidBonus,
			XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, XjActorDataKeys.XjQiuJinFaReady,
			XjActorDataKeys.XjQiuJinFaLastYear, XjActorDataKeys.XjQiuJinFaLastExecutionYear,
			XjActorDataKeys.XjQiuJinFaEligibilityYear, XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear,
			XjActorDataKeys.XjQiuJinFaFailureCount,
			XjActorDataKeys.XjQiuJinIntentState, XjActorDataKeys.XjQiuJinIntentYear, XjActorDataKeys.XjQiuJinIntentSignature,
			XjActorDataKeys.XjJinDanLastAttemptYear, XjActorDataKeys.XjJinDanSuccessYear,
			XjActorDataKeys.XjJinDanSuccessEventYear, XjActorDataKeys.XjJinDanSuccessEventSchema,
			XjActorDataKeys.XjJinDanPromotionAnnouncementYear, XjActorDataKeys.XjJinDanYiXiang, XjActorDataKeys.XjJieLinXian, XjActorDataKeys.XjJieLinXianYear,
			XjActorDataKeys.XjShenDanYear, XjActorDataKeys.XjShenDanDeathPending,
			XjActorDataKeys.XjShenDanDeathYear, XjActorDataKeys.XjShenDanDeathHandled,
			XjActorDataKeys.XjQuanBingSnapshotSchema, XjActorDataKeys.XjQuanBingLockUntilYear,
			XjActorDataKeys.XjQuanBingIntegrationRetreatActive, XjActorDataKeys.XjQuanBingIntegrationRetreatEndYear,
			XjActorDataKeys.XjQuanBingAcquiredYear, XjActorDataKeys.XjQuanBingWarPeakObserved,
			XjActorDataKeys.XjBottleneckStartedYear, XjActorDataKeys.XjBottleneckNextAttemptYear,
			XjActorDataKeys.XjBottleneckResolvedMask,
			XjActorDataKeys.XjAnnualSecondaryActiveYear, XjActorDataKeys.XjAnnualSecondaryLatestRequestedYear,
			XjActorDataKeys.XjAnnualSecondaryLastCompletedYear,
			XjActorDataKeys.FuQiCoreProgress, XjActorDataKeys.FuQiCoreLastAnnualYear,
			XjActorDataKeys.FuQiEraLastAnnualYear,
			XjActorDataKeys.FuQiCoreProjectStartYear, XjActorDataKeys.FuQiCoreProjectCompleteYear,
			XjActorDataKeys.FuQiSenseYear, XjActorDataKeys.FuQiSenseResult,
			XjActorDataKeys.FuQiRank4ZhenJunEligible, XjActorDataKeys.FuQiSwordQi,
			XjActorDataKeys.FuQiIntentStudyStartYear, XjActorDataKeys.FuQiIntentStudyCompleteYear,
			XjActorDataKeys.FuQiYangQingMingCompletedYear, XjActorDataKeys.FuQiSwordLastAnnualYear,
			XjActorDataKeys.FuQiBodyProjectStartYear, XjActorDataKeys.FuQiBodyProjectCompleteYear,
			XjActorDataKeys.FuQiPerfectionProjectStartYear, XjActorDataKeys.FuQiPerfectionProjectCompleteYear,
			XjActorDataKeys.FuQiShenMiaoPerfectionYear, XjActorDataKeys.FuQiRank4EligibilityChecked,
			XjActorDataKeys.FuQiJinXingReady, XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
			XjActorDataKeys.FuQiInjuryUntilYear, XjActorDataKeys.FuQiJinDanLastAttemptYear,
			XjActorDataKeys.FuQiJinDanNextAttemptYear, XjActorDataKeys.FuQiJinDanFailureCount,
			XjActorDataKeys.FuQiJinDanSuccessYear,
			XjActorDataKeys.QingXuanQingCanQi, XjActorDataKeys.QingXuanChuYangJi,
			XjActorDataKeys.QingXuanXuanYangZiFoundation, XjActorDataKeys.QingXuanUnlocked,
			XjActorDataKeys.QingXuanKongZhengCompleted, XjActorDataKeys.QingXuanRaisedShenTongMask,
			XjActorDataKeys.QingXuanUpliftStartYear, XjActorDataKeys.QingXuanUpliftCompleteYear,
			XjActorDataKeys.QingXuanFiveShenTongReady, XjActorDataKeys.QingXuanFiveShenTongReadyYear,
			XjActorDataKeys.ZiJinSwordResearchDecision, XjActorDataKeys.ZiJinSwordResearchDecisionYear,
			XjActorDataKeys.ZiJinSwordResearchStartedYear, XjActorDataKeys.ZiJinSwordResearchMask,
			XjActorDataKeys.ZiJinSwordCurrentOrdinal, XjActorDataKeys.ZiJinSwordProjectStartYear,
			XjActorDataKeys.ZiJinSwordProjectCompleteYear, XjActorDataKeys.ZiJinSwordLastAnnualYear,
			XjActorDataKeys.ZiJinSwordCompletedYear,
			XjActorDataKeys.FuQiToZiFuConvertedYear, XjActorDataKeys.FuQiToZiFuCoreProgress,
			XjActorDataKeys.FuQiToZiFuGrantedShenTongCount
		};

		private static readonly string[] FloatKeys =
		{
			XjActorDataKeys.ZhenYuan, XjActorDataKeys.XjGongFaProgress,
			XjActorDataKeys.XjBottleneckStoredZhenYuan
		};

		private static readonly string[] LongKeys =
		{
			XjActorDataKeys.XjShenDanAnchorActorId,
			XjActorDataKeys.XjShenDanDeathAnchorActorId
		};

		internal static TransitionSnapshot Capture(Actor actor)
		{
			TransitionSnapshot snapshot = new TransitionSnapshot();
			for (int i = 0; i < StringKeys.Length; i++)
			{
				XjActorAccessor.TryGetString(actor, StringKeys[i], out string value);
				snapshot._strings[StringKeys[i]] = value ?? string.Empty;
			}
			for (int i = 0; i < IntKeys.Length; i++)
			{
				XjActorAccessor.TryGetInt(actor, IntKeys[i], out int value);
				snapshot._ints[IntKeys[i]] = value;
			}
			for (int i = 0; i < FloatKeys.Length; i++)
			{
				XjActorAccessor.TryGetFloat(actor, FloatKeys[i], out float value);
				snapshot._floats[FloatKeys[i]] = value;
			}
			for (int i = 0; i < LongKeys.Length; i++)
			{
				XjActorAccessor.TryGetLong(actor, LongKeys[i], out long value);
				snapshot._longs[LongKeys[i]] = value;
			}
			return snapshot;
		}

		internal void Restore(Actor actor)
		{
			if (actor?.data == null) return;
			foreach (KeyValuePair<string, string> pair in _strings) XjActorAccessor.SetString(actor, pair.Key, pair.Value);
			foreach (KeyValuePair<string, int> pair in _ints) XjActorAccessor.SetInt(actor, pair.Key, pair.Value);
			foreach (KeyValuePair<string, float> pair in _floats) XjActorAccessor.SetFloat(actor, pair.Key, pair.Value);
			foreach (KeyValuePair<string, long> pair in _longs) XjActorAccessor.SetLong(actor, pair.Key, pair.Value);
			// Restore raw collection JSON/version even when the original collection was
			// empty, then rebuild the H1 scalar projection once at this cold rollback boundary.
			XjActorGongFaCollection.RefreshStoredSummary(actor);
			XjXianJiAccessor.Forget(actor);
		}
	}
}
