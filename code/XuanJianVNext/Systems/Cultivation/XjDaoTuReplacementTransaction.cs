using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 道途唯一写入口。权威字段、修法专属数据和可见特质作为一次事务提交：
/// 新道途成功后覆盖旧道途；任一前置校验失败则保留旧状态。
/// </summary>
internal static class XjDaoTuReplacementTransaction
{
	private static readonly HashSet<long> ActiveActors = new HashSet<long>();

	internal static bool ReplaceDaoTu(Actor actor, string daoTu, bool syncVisibleTraits)
	{
		if (actor?.data == null)
		{
			return false;
		}

		// 果位钟爱是跨转世的道途硬锁。任何家族定路、随机定路、手动换道
		// 或旧档回填都只能重新投影到钟爱果位对应的原道途。
		bool songXuanLocked = XjSongXuanEasterEggSystem.IsSongXuan(actor);
		bool zhangYanLocked = XjZhangYanEasterEggSystem.IsZhangYan(actor);
		bool imperialLocked = !songXuanLocked && !zhangYanLocked && XjXianGuoSystem.IsDiMingYang(actor);
		bool favoredLocked = false;
		string requested;
		if (songXuanLocked || zhangYanLocked)
		{
			// 支持者彩蛋的“上仪”是人物事实，不允许家学、果位钟爱、手动换道或
			// 旧档回填把它改写成其他道途。它只是一条O(1)写入口硬锁，不增加检测。
			requested = songXuanLocked ? XjSongXuanEasterEggSystem.DaoTu : XjZhangYanEasterEggSystem.DaoTu;
		}
		else if (imperialLocked)
		{
			requested = XjXianGuoSystem.MingYangDaoTu;
		}
		else
		{
			requested = XjGuoWeiFavoredDaoTuLock.ResolveRequestedDaoTu(actor, daoTu, out favoredLocked);
			if (!favoredLocked
				&& !XjManualTraitEditContext.IsActive
				&& TryResolveManualOverride(actor, out string manualOverride))
			{
				// A player editor choice is an explicit admin override. Ordinary family/CaiQi/
				// breakthrough assignment may not silently replace it. Return false when the
				// caller requested a different path so downstream code cannot continue as if
				// its own DaoTu had been committed (for example, reconciling GongFa to JueYin
				// while the actor actually remains TaiYang). Imperial/favored hard locks are
				// resolved above and intentionally still outrank this editor override.
				if (!string.Equals((requested ?? string.Empty).Trim(), manualOverride, StringComparison.Ordinal))
				{
					ProjectAuthoritativeTrait(actor, syncVisibleTraits);
					return false;
				}
				requested = manualOverride;
			}
		}
		if (requested.Length == 0
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(requested, out _))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !ActiveActors.Add(actorId))
		{
			return false;
		}

		try
		{
			if (!XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(requested, actorId, out string normalized)
				|| string.IsNullOrWhiteSpace(normalized)
				|| (favoredLocked ? !ValidateLockedPath(actor, normalized) : !ValidateGate(actor, normalized)))
			{
				ProjectAuthoritativeTrait(actor, syncVisibleTraits);
				return false;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string previousDaoTu);
			if (string.Equals((previousDaoTu ?? string.Empty).Trim(), normalized, StringComparison.Ordinal))
			{
				ProjectAuthoritativeTrait(actor, syncVisibleTraits);
				Refresh(actor);
				return true;
			}

			bool success;
			if (XjCultivationPathRules.IsFuQiYangXing(actor))
			{
				success = ReplaceFuQiDaoTu(actor, normalized, previousDaoTu);
			}
			else
			{
				success = ReplaceZiJinDaoTu(actor, normalized, previousDaoTu);
			}
			if (!success)
			{
				ProjectAuthoritativeTrait(actor, syncVisibleTraits);
				Refresh(actor);
				return false;
			}

			ProjectAuthoritativeTrait(actor, syncVisibleTraits);
			Refresh(actor);
			ReleaseFavoredReincarnationLockOnDaoTuSwitch(actor, normalized);
			XjSemanticDiagnostics.RecordDaoTuAssignment(actor, previousDaoTu, normalized, "ReplaceDaoTu");
			return true;
		}
		finally
		{
			ActiveActors.Remove(actorId);
		}
	}

	private static bool TryResolveManualOverride(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTuManualOverride, out string raw)
			|| string.IsNullOrWhiteSpace(raw))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(raw.Trim(), actorId, out string normalized)
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return false;
		}

		daoTu = normalized;
		return true;
	}

	private static void ReleaseFavoredReincarnationLockOnDaoTuSwitch(Actor actor, string newDaoTu)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(newDaoTu)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState pending)
			|| !pending.Found
			|| !string.Equals(pending.LifecycleStatus, "PendingReincarnatedZhengWei", StringComparison.Ordinal))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationDaoTu, out string oldDaoTu);
		if (string.IsNullOrWhiteSpace(oldDaoTu))
		{
			oldDaoTu = string.IsNullOrWhiteSpace(pending.PendingExternalZhengWeiDaoTu)
				? pending.DaoTu
				: pending.PendingExternalZhengWeiDaoTu;
		}
		if (string.IsNullOrWhiteSpace(oldDaoTu)) return;

		string normalizedOld = oldDaoTu.Trim();
		if (XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(normalizedOld, actorId, out string canonicalOld)
			&& !string.IsNullOrWhiteSpace(canonicalOld))
		{
			normalizedOld = canonicalOld;
		}
		if (string.Equals(normalizedOld, newDaoTu.Trim(), StringComparison.Ordinal)) return;

		int currentYear = XjAnnualExecutionContext.ResolveYear(actor);
		if (XjGuoWeiQuanBingRegistry.TryReleaseReincarnatedZhengWeiLock(
			normalizedOld, actorId, currentYear, out _))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationFavoredDaoTuSwitched, 1);
		}
	}


	private static bool ReplaceZiJinDaoTu(Actor actor, string normalized, string previousDaoTu)
	{
		XjXianJiState oldXianJi = XjXianJiAccessor.BuildState(actor);
		if (!TryBuildZiJinXianJiReplacement(actor, normalized, in oldXianJi, out string[] replacementIds))
		{
			return false;
		}
		if (!XjActorGongFaCollection.TryExportSerialized(actor, out int oldGongFaVersion, out string oldGongFaJson))
		{
			return false;
		}

		ZiJinTransactionSnapshot snapshot = ZiJinTransactionSnapshot.Capture(
			actor,
			previousDaoTu,
			in oldXianJi,
			oldGongFaVersion,
			oldGongFaJson);

		if (!XjCultivationStateTransitions.TrySetZiJinDaoTuCore(actor, normalized, syncVisibleTraits: false))
		{
			snapshot.Restore(actor);
			return false;
		}

		if (replacementIds.Length == 0)
		{
			if (XjActorGongFaCollection.IsConsistentWithDaoTu(actor, normalized))
			{
				return true;
			}
			snapshot.Restore(actor);
			return false;
		}

		int currentYear = XjAnnualExecutionContext.ResolveYear(actor);
		if (XjActorGongFaCollection.ReplaceAllForManualDaoTu(
				actor,
				normalized,
				replacementIds,
				"道途唯一替换")
			&& XjXianJiAccessor.RestoreSnapshot(actor, string.Join("|", replacementIds), currentYear)
			&& XjActorGongFaCollection.IsConsistentWithDaoTu(actor, normalized))
		{
			return true;
		}

		snapshot.Restore(actor);
		return false;
	}

	private static bool TryBuildZiJinXianJiReplacement(
		Actor actor,
		string daoTu,
		in XjXianJiState existing,
		out string[] replacementIds)
	{
		replacementIds = Array.Empty<string>();
		if (!existing.Found || existing.Count <= 0)
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int currentYear = XjAnnualExecutionContext.ResolveYear(actor);
		List<string> replacements = new List<string>(existing.Count);
		for (int ordinal = 1; ordinal <= existing.Count; ordinal++)
		{
			string[] selected = replacements.ToArray();
			string pickedId;
			bool picked = actor.hasTrait("ChuShen8")
				? XjXianJiCatalog.TryPickDaoZhuForProgression(
					daoTu, ordinal, actorId + currentYear * 131L, selected,
					XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(daoTu), out pickedId)
				: ordinal <= 3
					? XjXianJiCatalog.TryPickUpperForProgression(
						daoTu,
						ordinal,
						actorId + currentYear * 131L,
						selected,
						out pickedId)
					: XjXianJiCatalog.TryPickForProgression(
					daoTu,
					ordinal,
					actorId + currentYear * 131L,
					selected,
					XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(daoTu),
					!XjLongShuSystem.IsLongShu(actor),
					out pickedId);
			if (!picked || string.IsNullOrWhiteSpace(pickedId))
			{
				return false;
			}
			replacements.Add(pickedId.Trim());
		}

		replacementIds = replacements.ToArray();
		return true;
	}

	private sealed class ZiJinTransactionSnapshot
	{
		private static readonly string[] StringKeys =
		{
			XjActorDataKeys.CultivationPath,
			XjActorDataKeys.DaoTu,
			XjActorDataKeys.XjQiuJinFaName,
			XjActorDataKeys.XjQiuJinFaSourceGongFaName,
			XjActorDataKeys.XjQiuJinFaSourceDaoTu,
			XjActorDataKeys.XjQiuJinFaBoundAuthority,
			XjActorDataKeys.XjQiuJinFaOrigin,
			XjActorDataKeys.XjQiuJinFaLastFailureReason
		};

		private static readonly string[] IntKeys =
		{
			XjActorDataKeys.XjQiuJinFaSourceGongFaGrade,
			XjActorDataKeys.XjQiuJinFaReady,
			XjActorDataKeys.XjQiuJinFaLastYear,
			XjActorDataKeys.XjQiuJinFaLastExecutionYear,
			XjActorDataKeys.XjQiuJinFaEligibilityYear,
			XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear,
			XjActorDataKeys.XjQiuJinFaFailureCount
		};

		private readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, int> _ints = new Dictionary<string, int>(StringComparer.Ordinal);
		private XjXianJiState _xianJi;
		private int _gongFaVersion;
		private string _gongFaJson = string.Empty;
		private bool _hadLegacyGongFa;

		internal static ZiJinTransactionSnapshot Capture(
			Actor actor,
			string previousDaoTu,
			in XjXianJiState oldXianJi,
			int oldGongFaVersion,
			string oldGongFaJson)
		{
			ZiJinTransactionSnapshot snapshot = new ZiJinTransactionSnapshot
			{
				_xianJi = oldXianJi,
				_gongFaVersion = oldGongFaVersion,
				_gongFaJson = oldGongFaJson ?? string.Empty,
				_hadLegacyGongFa = XjGongFaAccessor.BuildState(actor).Found
			};
			for (int i = 0; i < StringKeys.Length; i++)
			{
				XjActorAccessor.TryGetString(actor, StringKeys[i], out string value);
				snapshot._strings[StringKeys[i]] = value ?? string.Empty;
			}
			snapshot._strings[XjActorDataKeys.DaoTu] = previousDaoTu ?? string.Empty;
			for (int i = 0; i < IntKeys.Length; i++)
			{
				XjActorAccessor.TryGetInt(actor, IntKeys[i], out int value);
				snapshot._ints[IntKeys[i]] = value;
			}
			return snapshot;
		}

		internal void Restore(Actor actor)
		{
			foreach (KeyValuePair<string, string> pair in _strings)
			{
				XjActorAccessor.SetString(actor, pair.Key, pair.Value ?? string.Empty);
			}
			XjXianJiAccessor.RestoreSnapshot(
				actor,
				string.Join("|", _xianJi.Ids ?? Array.Empty<string>()),
				_xianJi.LastYear);
			XjActorGongFaCollection.TryRestoreSerialized(
				actor,
				_gongFaVersion,
				_gongFaJson,
				"道途唯一替换事务回滚");
			if (!_hadLegacyGongFa)
			{
				XjGongFaAccessor.Clear(actor);
			}
			foreach (KeyValuePair<string, int> pair in _ints)
			{
				XjActorAccessor.SetInt(actor, pair.Key, pair.Value);
			}
			// 功法集合恢复可能重建求金绑定；最后再恢复原始求金字符串，保证完整回滚。
			foreach (KeyValuePair<string, string> pair in _strings)
			{
				if (!string.Equals(pair.Key, XjActorDataKeys.CultivationPath, StringComparison.Ordinal)
					&& !string.Equals(pair.Key, XjActorDataKeys.DaoTu, StringComparison.Ordinal))
				{
					XjActorAccessor.SetString(actor, pair.Key, pair.Value ?? string.Empty);
				}
			}
			XjXianJiAccessor.Forget(actor);
		}
	}

	private static bool ReplaceFuQiDaoTu(Actor actor, string normalized, string previousDaoTu)
	{
		if (!XjDaoTuCatalog.TryResolve(normalized, out XjDaoTuDefinition definition)
			|| !definition.SupportsFuQi
			|| !XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition rootCore)
			|| !rootCore.GameplayImplemented)
		{
			return false;
		}

		// 所有校验、功法集合序列化和服气状态快照均先于写入。只有全部成功后，
		// 才允许替换权威道途，避免失败时残留“新道途＋旧核心／旧功法”的半状态。
		FuQiTransactionSnapshot snapshot = FuQiTransactionSnapshot.Capture(actor, previousDaoTu);
		if (!XjActorGongFaCollection.TryExportSerialized(actor, out int oldGongFaVersion, out string oldGongFaJson))
		{
			return false;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, normalized);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId,
			string.Equals(definition.RootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal)
				? XjFuQiLineageIds.Sword
				: string.Empty);
		XjFuQiSensingSystem.MarkPathEntrySuccess(actor, definition.RootId);
		XjCultivationPathTransitions.ResetFuQiProgress(actor);
		XjFuQiCoreRouter.InitializeActorCore(actor, definition.RootId);
		XjFuQiCoreDefinition specialized = XjFuQiCoreCatalog.SpecializeForDaoTu(in rootCore, normalized);
		if (XjFuQiMethodRules.EnsureRealGongFaEntity(
			actor,
			in specialized,
			XjAnnualExecutionContext.ResolveYear(actor)))
		{
			return true;
		}

		// 防御性回滚：ActorData、感气结果、核心项目与功法集合必须同时恢复。
		snapshot.Restore(actor);
		XjActorGongFaCollection.TryRestoreSerialized(
			actor,
			oldGongFaVersion,
			oldGongFaJson,
			"服气道途替换事务回滚");
		return false;
	}

	private sealed class FuQiTransactionSnapshot
	{
		private static readonly string[] StringKeys =
		{
			XjActorDataKeys.DaoTu,
			XjActorDataKeys.FuQiLineageId,
			XjActorDataKeys.FuQiDaoTuRootId,
			XjActorDataKeys.FuQiCoreType,
			XjActorDataKeys.FuQiCoreId,
			XjActorDataKeys.FuQiSensedQiId,
			XjActorDataKeys.FuQiStudiedIntentIds,
			XjActorDataKeys.FuQiCurrentIntentId,
			XjActorDataKeys.FuQiShenMiaoId
		};

		private static readonly string[] IntKeys =
		{
			XjActorDataKeys.FuQiCoreProgress,
			XjActorDataKeys.FuQiCoreLastAnnualYear,
			XjActorDataKeys.FuQiEraLastAnnualYear,
			XjActorDataKeys.FuQiCoreProjectStartYear,
			XjActorDataKeys.FuQiCoreProjectCompleteYear,
			XjActorDataKeys.FuQiSenseYear,
			XjActorDataKeys.FuQiSenseResult,
		XjActorDataKeys.FuQiHuangGuanEnteredYear,
		XjActorDataKeys.FuQiZhenRenEnteredYear,
			XjActorDataKeys.FuQiRank4ZhenJunEligible,
			XjActorDataKeys.FuQiSwordQi,
			XjActorDataKeys.FuQiIntentStudyStartYear,
			XjActorDataKeys.FuQiIntentStudyCompleteYear,
			XjActorDataKeys.FuQiYangQingMingCompletedYear,
			XjActorDataKeys.FuQiSwordLastAnnualYear,
			XjActorDataKeys.FuQiBodyProjectStartYear,
			XjActorDataKeys.FuQiBodyProjectCompleteYear,
			XjActorDataKeys.FuQiPerfectionProjectStartYear,
			XjActorDataKeys.FuQiPerfectionProjectCompleteYear,
			XjActorDataKeys.FuQiShenMiaoPerfectionYear,
			XjActorDataKeys.FuQiRank4EligibilityChecked,
			XjActorDataKeys.FuQiJinXingReady,
			XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
			XjActorDataKeys.FuQiInjuryUntilYear,
			XjActorDataKeys.FuQiJinDanLastAttemptYear,
			XjActorDataKeys.FuQiJinDanNextAttemptYear,
			XjActorDataKeys.FuQiJinDanFailureCount,
			XjActorDataKeys.FuQiJinDanSuccessYear
		};

		private readonly Dictionary<string, string> _stringValues = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, int> _intValues = new Dictionary<string, int>(StringComparer.Ordinal);

		internal static FuQiTransactionSnapshot Capture(Actor actor, string previousDaoTu)
		{
			FuQiTransactionSnapshot snapshot = new FuQiTransactionSnapshot();
			for (int i = 0; i < StringKeys.Length; i++)
			{
				XjActorAccessor.TryGetString(actor, StringKeys[i], out string value);
				snapshot._stringValues[StringKeys[i]] = value ?? string.Empty;
			}
			// DaoTu可能来自未写入过ActorData的旧档，显式保存调用方读取到的旧值。
			snapshot._stringValues[XjActorDataKeys.DaoTu] = previousDaoTu ?? string.Empty;
			for (int i = 0; i < IntKeys.Length; i++)
			{
				XjActorAccessor.TryGetInt(actor, IntKeys[i], out int value);
				snapshot._intValues[IntKeys[i]] = value;
			}
			return snapshot;
		}

		internal void Restore(Actor actor)
		{
			foreach (KeyValuePair<string, string> pair in _stringValues)
			{
				XjActorAccessor.SetString(actor, pair.Key, pair.Value ?? string.Empty);
			}
			foreach (KeyValuePair<string, int> pair in _intValues)
			{
				XjActorAccessor.SetInt(actor, pair.Key, pair.Value);
			}
		}
	}


	private static bool ValidateLockedPath(Actor actor, string daoTu)
	{
		// 钟爱转世承继的是既成正位契合，不重新接受青宣空证或长庚开道
		// 等“首次入道”门槛；但渊照是旧时间轴迁移的特殊例外：物理遗泽未落地前
		// 只保留真灵钟爱记录，不允许凭旧档钟爱把现世修法提前生成。
		if (string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(XjDaoTuRootIds.YuanZhao)) return false;
		return XjCultivationPathRules.IsFuQiYangXing(actor)
			? XjDaoTuCatalog.SupportsFuQi(daoTu)
			: XjDaoTuCatalog.SupportsZiJin(daoTu);
	}

	private static bool ValidateGate(Actor actor, string daoTu)
	{
		if (string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(XjDaoTuRootIds.YuanZhao))
		{
			return false;
		}
		if (string.Equals(daoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			&& !XjQingXuanKongZhengSystem.CanEnterQingXuan(actor))
		{
			return false;
		}
		if (string.Equals(daoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjZiJinSwordDaoSystem.CanEnterLongGengDaoTu(actor))
		{
			return false;
		}
		return XjCultivationPathRules.IsFuQiYangXing(actor)
			? XjDaoTuCatalog.SupportsFuQi(daoTu)
			: XjDaoTuCatalog.SupportsZiJin(daoTu);
	}

	private static void ProjectAuthoritativeTrait(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string authoritative);
		if (syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return;
		}

		// 冷路径不应凭空显示尚未开放的道途，但仍必须删除非权威的重复道途特质。
		string targetTraitId = XjDaoTuVisibleTraitCatalog.TryResolveTraitId(authoritative, out string resolvedTrait)
			? resolvedTrait
			: string.Empty;
		string[] all = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		XjCultivationStateTransitions.EnterVisibleTraitSync();
		try
		{
			for (int i = 0; i < all.Length; i++)
			{
				string traitId = all[i];
				if (!string.IsNullOrWhiteSpace(traitId)
					&& !string.Equals(traitId, targetTraitId, StringComparison.Ordinal)
					&& actor.hasTrait(traitId))
				{
					actor.removeTrait(traitId);
				}
			}
		}
		finally
		{
			XjCultivationStateTransitions.ExitVisibleTraitSync();
		}
	}

	private static void Refresh(Actor actor)
	{
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		XjCombatHotPathCache.Refresh(actor);
		XjRuntimeActorInterestIndex.Observe(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
	}
}
