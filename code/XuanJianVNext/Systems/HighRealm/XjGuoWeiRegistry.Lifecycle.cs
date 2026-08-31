using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjGuoWeiRegistry
{
		private static bool RemoveOtherActiveClaimsForActor(long actorId, string keepKey)
		{
			if (actorId <= 0L || activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}

			List<string> removeKeys = null;
			foreach (KeyValuePair<string, XjGuoWeiRegistryEntry> pair in activeEntriesByGuoWei)
			{
				if (pair.Value.ActorId != actorId || string.Equals(pair.Key, keepKey, StringComparison.Ordinal))
				{
					continue;
				}

				removeKeys ??= new List<string>();
				removeKeys.Add(pair.Key);
			}

			if (removeKeys == null)
			{
				return false;
			}

			for (int i = 0; i < removeKeys.Count; i++)
			{
				if (activeEntriesByGuoWei.TryGetValue(removeKeys[i], out XjGuoWeiRegistryEntry removed))
				{
					XjTaiYinHiddenFruitSystem.OnTaiYinHolderReleased(actorId, removed.GuoWei, Math.Max(1, XjYearTracker.CurrentYear));
				}
				activeEntriesByGuoWei.Remove(removeKeys[i]);
			}
			return true;
		}

		/// <summary>
		/// 紫金金丹的硬不变量：五门神通若全部属于同一道途上位池，
		/// 该角色只能持有该道途果位，绝不能因旧档、手动恢复或果位拥堵而落成闰位/余位。
		/// </summary>
		internal static bool IsZiJinPureUpperPositionMismatch(
			Actor actor,
			out string pureUpperDaoTu,
			out XjJinDanState jinDanState)
		{
			pureUpperDaoTu = string.Empty;
			jinDanState = XjJinDanState.Empty;
			if (!XjSafeCore.IsAliveActor(actor)
				|| !XjCultivationPathRules.IsZiFuJinDan(actor))
			{
				return false;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
			if (!string.Equals(
				XjRealmHelper.NormalizeId(realmId),
				XjRealmIds.JinDan,
				StringComparison.Ordinal))
			{
				return false;
			}

			jinDanState = XjJinDanAccessor.BuildStateWithoutDaoMigration(actor);
			if (!jinDanState.Found
				|| string.Equals(
					ResolveTypeFromName(jinDanState.GuoWei),
					XjGuoWeiCalculator.ZhengWei,
					StringComparison.Ordinal))
			{
				return false;
			}

			XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
			return XjXianJiCatalog.TryResolvePureUpperDaoTu(xianJi, out pureUpperDaoTu);
		}

		/// <summary>
		/// 一次性纠正旧档中的“五上位却成余/闰”。
		/// 果位空缺时归正确果；果位已有合法持有者时回退紫府等待后续合法求金，
		/// 绝不再保留非法闰位，也不凭空改成神丹。
		/// </summary>
		internal static bool TryRepairZiJinPureUpperPositionInvariant(
			Actor actor,
			int currentYear,
			out string outcome)
		{
			outcome = string.Empty;
			if (!IsZiJinPureUpperPositionMismatch(actor, out string pureDaoTu, out XjJinDanState state))
			{
				return false;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L) return false;
			int safeYear = Math.Max(1, currentYear);
			string fruitPosition = XjGuoWeiCalculator.BuildGuoWeiSlotName(
				pureDaoTu,
				XjGuoWeiCalculator.ZhengWei,
				1);
			string baseJinXing = XjJinXingCalculator.Calculate(pureDaoTu, actorId);
			bool canTakeFruit = !string.IsNullOrWhiteSpace(baseJinXing)
				&& TryResolveAvailableGuoWeiDetailed(
					pureDaoTu,
					XjGuoWeiCalculator.ZhengWei,
					actorId,
					actorId + Math.Max(0, state.SuccessYear),
					false,
					out string resolvedType,
					out string resolvedPosition,
					out _)
				&& string.Equals(resolvedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				&& string.Equals(resolvedPosition, fruitPosition, StringComparison.Ordinal);

			if (canTakeFruit && TryClaim(
				actor,
				pureDaoTu,
				baseJinXing,
				fruitPosition,
				Math.Max(1, state.SuccessYear)))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string previousManifest);
				XjDaoLineageStateRegistry.OnHolderReleased(
					actorId,
					previousManifest,
					state.GuoWei,
					string.Empty,
					safeYear,
					penalizeVitality: false);

				string resolvedJinXing = XjFruitPositionWorldState.ResolveJinXingName(
					fruitPosition,
					baseJinXing);
				XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, pureDaoTu, false);
				XjJinDanAccessor.WriteSuccess(
					actor,
					resolvedJinXing,
					fruitPosition,
					Math.Max(1, state.SuccessYear));
				XjHighRealmDaoStateService.ApplyRespectPositionChange(
					actor,
					XjGuoWeiCalculator.ZhengWei,
					fruitPosition,
					resolvedJinXing,
					safeYear,
					"五上位归正",
					pureDaoTu,
					pureDaoTu);
				XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
				XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(
					actor,
					pureDaoTu,
					fruitPosition,
					Math.Max(1, state.SuccessYear));
				XjVisibleTraitSync.SyncCultivationTraits(actor);
				XjWorldArchiveSystem.MarkChanged();
				XjWorldArchiveSystem.RequestProtectedCommit();
				outcome = "五门上位神通已归入" + pureDaoTu + "果位";
				return true;
			}

			// 旧档非法高境不能继续占据余/闰位。正果已有合法持有者或被天地规则封锁时，
			// 回退到紫府并保留五门神通、功法和真元，等待果位释放后重新依法求金。
			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false))
			{
				outcome = "五上位归正失败：无法回退紫府";
				return false;
			}

			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, pureDaoTu, false);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, "PureUpperFruitUnavailableLegacyRepair");
			XjActorAccessor.SetString(
				actor,
				XjActorDataKeys.XjJinDanFailureNarrative,
				"旧档五门上位神通曾被错误降格为余闰，现已归还紫府，静待本道果位再开。");
			// 旧金丹回退不能因高龄立即自然死亡，给足一次旧档迁移缓冲。
			XjVanillaDeathGuard.MarkRealmPromotionGrace(actor);
			XjActorAccessor.SetInt(
				actor,
				XjActorDataKeys.RealmNaturalDeathGraceUntilYear,
				safeYear + 180);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			outcome = "本道果位已有主，非法余闰已回退紫府等待正果";
			return true;
		}

		private static bool TryRepairIllegalHeShuiFruitHolder(Actor actor, XjJinDanState state, int currentYear)
		{
			if (!state.Found || XjLongShuSystem.IsLongShu(actor)) return false;
			XjHighRealmDaoStateService.ResolvePositionIdentity(
				actor, state.GuoWei, out _, out string manifestDaoTu);
			if (!XjLongShuSystem.IsHeShuiFruitPosition(
				manifestDaoTu, ResolveTypeFromName(state.GuoWei))) return false;

			int safeYear = Math.Max(1, currentYear);
			if (!XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false)) return false;
			XjJinDanAccessor.ClearSuccess(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason,
				"HeShuiFruitReservedForLongShuLegacyRepair");
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative,
				"旧档曾令非龙属误据合水果位，现已释位回退紫府；合水果位自龙属肇生后只认龙血。");
			XjVanillaDeathGuard.MarkRealmPromotionGrace(actor);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.RealmNaturalDeathGraceUntilYear, safeYear + 180);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			return true;
		}

		internal static bool ReconcileLiveActorReadOnly(Actor actor)
		{
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return false;
			}

			// 冷读档阶段先不登记旧档非法“五上位余/闰”，待所有合法果位重建完成后，
			// 由延迟不变量审计统一归正，避免加载顺序让错误角色抢走正果。
			if (IsZiJinPureUpperPositionMismatch(actor, out _, out _))
			{
				return false;
			}

			XjJinDanState state = XjJinDanAccessor.BuildPositionCarrierState(actor);
			if (!state.Found || string.IsNullOrWhiteSpace(state.GuoWei))
			{
				return false;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L)
			{
				return false;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
			XjHighRealmDaoStateService.ResolvePositionIdentity(
				actor, state.GuoWei, out string sourceDaoTu, out string manifestDaoTu);
			if (string.IsNullOrWhiteSpace(manifestDaoTu)) manifestDaoTu = actorDaoTu;
			string guoWeiType = ResolveTypeFromName(state.GuoWei);
			if (!XjLongShuSystem.CanClaimHeShuiFruitPosition(actor, manifestDaoTu, guoWeiType))
			{
				// 冷读档不允许旧档中的非龙属合水果位重新抢占世界注册表。
				return false;
			}
			string effectiveGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(state.GuoWei);
			if (string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				&& !string.Equals(
					XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(effectiveGuoWei),
					manifestDaoTu,
					StringComparison.Ordinal))
			{
				effectiveGuoWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
					manifestDaoTu, guoWeiType, XjGuoWeiCalculator.ResolveSlotIndex(effectiveGuoWei));
			}
			string key = NormalizeKey(effectiveGuoWei);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
				&& occupied.Found
				&& occupied.ActorId > 0L
				&& occupied.ActorId != actorId)
			{
				return false;
			}
			if (!IsDaoTuTypeAllowed(manifestDaoTu, guoWeiType))
			{
				return false;
			}
			if (IsHiddenYinSiZhengWei(manifestDaoTu, guoWeiType, state.GuoWei))
			{
				return false;
			}
			string externalDaoTu = string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? sourceDaoTu
				: string.Empty;
			XjGuoWeiRegistryEntry entry = new XjGuoWeiRegistryEntry(
				true,
				actorId,
				actor.getName(),
				ResolveFamilyName(actor),
				Normalize(manifestDaoTu),
				XjJinXingNamePolicy.NormalizeLegacyName(state.JinXing),
				effectiveGuoWei,
				state.SuccessYear,
				StatusActive,
				0,
				string.Empty);

			activeEntriesByGuoWei[key] = entry;
			XjFruitPositionWorldState.EnsurePosition(
				actor,
				entry.DaoTu,
				guoWeiType,
				entry.GuoWei,
				entry.JinXing,
				externalDaoTu,
				entry.Year);
			if (!historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry historical)
				|| !historical.Found
				|| historical.IsActive)
			{
				historyEntriesByActorId[actorId] = entry;
			}
			return true;
		}

		internal static void ReconcileLiveActor(Actor actor)
		{
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}

			int currentYear = Math.Max(
				1,
				Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0));
			if (TryRepairZiJinPureUpperPositionInvariant(actor, currentYear, out _))
			{
				return;
			}

			XjJinDanState state = XjJinDanAccessor.BuildState(actor);
			if (!state.Found)
			{
				return;
			}
			if (TryRepairIllegalHeShuiFruitHolder(actor, state, currentYear))
			{
				return;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
			if (TryRestoreDisplacedLockedIntercalary(
				actor, state, actorDaoTu, currentYear, out XjJinDanState restoredState, out string restoredDaoTu))
			{
				state = restoredState;
				actorDaoTu = restoredDaoTu;
			}
			if (TryMigrateLegacyIntercalaryIdentity(
				actor, state, actorDaoTu, currentYear, out XjJinDanState migratedState, out string migratedDaoTu))
			{
				state = migratedState;
				actorDaoTu = migratedDaoTu;
			}

			XjHighRealmDaoStateService.ResolvePositionIdentity(
				actor, state.GuoWei, out string sourceDaoTu, out string manifestDaoTu);
			if (string.IsNullOrWhiteSpace(manifestDaoTu)) manifestDaoTu = actorDaoTu;
			string preferredType = ResolveTypeFromName(state.GuoWei);
			string preferredGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(state.GuoWei);
			if (string.Equals(preferredType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				&& !string.Equals(
					XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(preferredGuoWei),
					manifestDaoTu,
					StringComparison.Ordinal))
			{
				preferredGuoWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(
					manifestDaoTu, preferredType, XjGuoWeiCalculator.ResolveSlotIndex(preferredGuoWei));
			}
			string externalDaoTu = string.Equals(preferredType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? sourceDaoTu
				: string.Empty;

			if (TryClaim(actor, manifestDaoTu, state.JinXing, preferredGuoWei, state.SuccessYear, externalDaoTu))
			{
				XjHighRealmDaoStateService.EnsureRestoredState(actor, state.SuccessYear);
				// 闰位的显道可与证道根基不同。果位索引恢复时曾只修复
				// ActorData/果位账本，未同步原生可见特质，遂出现“明阳闰位、
				// 少阳特质”这一数据与人物栏脱节。恢复完成后按权威 DaoTu
				// 重新投影一次，且该操作是幂等的。
				XjVisibleTraitSync.SyncCultivationTraits(actor);
				return;
			}

			// 历史版本曾允许“道胎副位果位”和普通果位恢复链在导入/旁路时互不知晓，
			// 于是同一正果可能残留在两名角色的个人状态里。正果只有一席：一旦确认
			// 当前位已由他人真实占据，旧持位者必须重新按自身条件寻找合法余/闰位，
			// 不能因为“同类型果位无第二席”而把重复果位字段永久留在人物面板。
			bool fruitConflict = string.Equals(preferredType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				&& TryResolveConflictingHolder(preferredGuoWei, actorId, out _, out _);
			if (!TryResolveAvailableGuoWei(
					manifestDaoTu,
					preferredType,
					actorId,
					actorId + Math.Max(0, state.SuccessYear),
					fruitConflict,
					out _,
					out string replacement))
			{
				return;
			}

			if (!TryClaim(actor, manifestDaoTu, state.JinXing, replacement, state.SuccessYear, externalDaoTu))
			{
				return;
			}

			XjActorAccessor.TryGetString(
				actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string previousManifestDaoTu);
			previousManifestDaoTu = Normalize(previousManifestDaoTu);
			if (previousManifestDaoTu.Length == 0)
			{
				previousManifestDaoTu = Normalize(
					XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(state.GuoWei));
			}
			if (previousManifestDaoTu.Length == 0) previousManifestDaoTu = Normalize(actorDaoTu);
			string previousGuoWei = state.GuoWei;
			XjJinDanAccessor.WriteSuccess(actor, state.JinXing, replacement, state.SuccessYear);
			if (string.Equals(preferredType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(sourceDaoTu)
				&& !string.IsNullOrWhiteSpace(manifestDaoTu))
			{
				XjHighRealmDaoStateService.RepairIntercalaryIdentity(
					actor, sourceDaoTu, manifestDaoTu, replacement, state.JinXing,
					state.SuccessYear, currentYear, previousManifestDaoTu, previousGuoWei);
			}
			else
			{
				XjHighRealmDaoStateService.EnsureRestoredState(actor, state.SuccessYear);
			}
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, manifestDaoTu, replacement, state.SuccessYear);
		}


		/// <summary>
		/// 修复两个已知旧旁路造成的“闰位消失”：
		/// 1) Reconcile 依据当前神通把已经固化的闰位重定向到另一显道；
		/// 2) 权柄之争中余/闰位斩落相邻果位后直接跳成果位。
		/// 只在存在最初闰位证道根基、当前显道与该根基冲突且留有对应旧行为痕迹时执行，
		/// 不扫描或重算正常在位者，也不会把合法“闰以变正”的同显道果位拉回闰位。
		/// </summary>
		private static bool TryRestoreDisplacedLockedIntercalary(
			Actor actor,
			in XjJinDanState state,
			string currentDaoTu,
			int currentYear,
			out XjJinDanState restoredState,
			out string restoredDaoTu)
		{
			restoredState = state;
			restoredDaoTu = Normalize(currentDaoTu);
			if (actor?.data == null || !state.Found) return false;

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L
				|| !XjDaoLineageStateRegistry.TryGetInitialIntercalaryFoundation(
					actorId, out XjDaoProofFoundationArchiveData foundation)
				|| foundation == null)
			{
				return false;
			}

			string originalSource = Normalize(foundation.SourceDaoTu);
			string originalManifest = Normalize(foundation.ManifestDaoTu);
			if (originalSource.Length == 0 || originalManifest.Length == 0
				|| string.Equals(originalSource, originalManifest, StringComparison.Ordinal)) return false;

			string currentType = ResolveTypeFromName(state.GuoWei);
			bool currentIsRun = string.Equals(currentType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal);
			bool currentIsZheng = string.Equals(currentType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
			if (!currentIsRun && !currentIsZheng) return false;

			XjHighRealmDaoStateService.ResolvePositionIdentity(
				actor, state.GuoWei, out string currentSource, out string currentManifest);
			currentSource = Normalize(currentSource);
			currentManifest = Normalize(currentManifest);
			if (currentManifest.Length == 0) currentManifest = Normalize(currentDaoTu);
			if (string.Equals(currentManifest, originalManifest, StringComparison.Ordinal)) return false;

			// 只修复已经发生过旧旁路的角色，避免把开发者手动改档或其他特殊实验误判为迁移对象。
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string jinXingHistory);
			bool hasLegacyRedirectMark = currentIsRun
				&& (jinXingHistory ?? string.Empty).Contains("闰位归属校正为", StringComparison.Ordinal);
			bool hasLegacyCrossFruitMark = currentIsZheng
				&& (jinXingHistory ?? string.Empty).Contains("变成果位", StringComparison.Ordinal);
			if (!hasLegacyRedirectMark && !hasLegacyCrossFruitMark) return false;

			string restoreGuoWei = string.Empty;
			IReadOnlyList<XjDerivedPositionArchiveRecord> positions = XjFruitPositionWorldState.ReadPositionsSnapshot();
			for (int i = 0; i < positions.Count; i++)
			{
				XjDerivedPositionArchiveRecord position = positions[i];
				if (position == null
					|| position.FounderActorId != actorId
					|| !string.Equals(Normalize(position.DaoTu), originalManifest, StringComparison.Ordinal)
					|| !string.Equals(
						XjGuoWeiCalculator.NormalizePositionType(position.PositionType),
						XjGuoWeiCalculator.RunWei,
						StringComparison.Ordinal)) continue;
				restoreGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(position.PositionId);
				break;
			}
			if (restoreGuoWei.Length == 0
				&& !TryResolveAvailableGuoWei(
					originalManifest,
					XjGuoWeiCalculator.RunWei,
					actorId,
					actorId + Math.Max(1, foundation.FoundedYear) * 1009L,
					false,
					out _,
					out restoreGuoWei))
			{
				return false;
			}

			string restoredJinXing = Normalize(foundation.JinXing);
			if (restoredJinXing.Length == 0)
			{
				restoredJinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
					actor, originalSource, originalManifest, XjGuoWeiCalculator.RunWei,
					XjJinXingCalculator.Calculate(originalManifest, actorId));
			}
			if (restoredJinXing.Length == 0) return false;

			int successYear = state.SuccessYear > 0 ? state.SuccessYear : Math.Max(1, foundation.FoundedYear);
			bool restoredClaim = TryClaim(
				actor, originalManifest, restoredJinXing, restoreGuoWei, successYear, originalSource);
			if (!restoredClaim)
			{
				// 原槽已被后来者占用时，不覆盖合法现任；改取同一显道下仍可用的闰位槽。
				// 角色的“根道/显道”保持历史锁定，槽号不是身份本身。
				if (!TryResolveAvailableGuoWei(
					originalManifest,
					XjGuoWeiCalculator.RunWei,
					actorId,
					actorId + Math.Max(1, foundation.FoundedYear) * 1009L + 17L,
					false,
					out _,
					out string fallbackGuoWei)
					|| !TryClaim(
						actor, originalManifest, restoredJinXing, fallbackGuoWei, successYear, originalSource))
				{
					return false;
				}
				restoreGuoWei = fallbackGuoWei;
			}

			string previousDaoTu = Normalize(currentDaoTu);
			bool projectionRestored;
			if (XjCultivationPathRules.IsFuQiYangXing(actor))
			{
				projectionRestored = XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, originalManifest, true);
			}
			else
			{
				XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, originalManifest, true);
				projectionRestored = true;
			}
			if (!projectionRestored)
			{
				ReleaseForActor(actorId, restoreGuoWei);
				if (XjCultivationPathRules.IsFuQiYangXing(actor))
					XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, previousDaoTu, true);
				else
					XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, previousDaoTu, true);
				TryClaim(
					actor,
					currentManifest,
					state.JinXing,
					state.GuoWei,
					state.SuccessYear,
					currentIsRun ? currentSource : string.Empty);
				return false;
			}

			string previousGuoWei = state.GuoWei;
			XjJinDanAccessor.WriteSuccess(actor, restoredJinXing, restoreGuoWei, successYear);
			XjHighRealmDaoStateService.RepairIntercalaryIdentity(
				actor,
				originalSource,
				originalManifest,
				restoreGuoWei,
				restoredJinXing,
				successYear,
				Math.Max(successYear, currentYear),
				currentManifest,
				previousGuoWei);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(
				actor, originalManifest, restoreGuoWei, successYear);
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			restoredDaoTu = originalManifest;
			restoredState = XjJinDanAccessor.BuildState(actor);
			return restoredState.Found;
		}

		private static bool TryMigrateLegacyIntercalaryIdentity(
			Actor actor,
			in XjJinDanState state,
			string currentDaoTu,
			int currentYear,
			out XjJinDanState migratedState,
			out string migratedDaoTu)
		{
			migratedState = state;
			migratedDaoTu = Normalize(currentDaoTu);
			if (actor?.data == null
				|| !XjCultivationPathRules.IsZiFuJinDan(actor)
				|| !state.Found
				|| !string.Equals(ResolveTypeFromName(state.GuoWei), XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				return false;
			}

			bool hasPersistedIdentity = XjHighRealmDaoStateService.TryReadPersistedIntercalaryIdentity(
				actor, out string sourceDaoTu, out string targetDaoTu);
			// 已经固化的闰位根道/显道属于历史事实，不能在 Reconcile 时重新计算后改位。
			// 仅旧档缺失双字段时，才从五神通补出一次身份。
			bool mayResolveFromCurrentShenTong = !hasPersistedIdentity;
			if (mayResolveFromCurrentShenTong)
			{
				XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
				if (XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
					actor, xianJi, out string resolvedSourceDaoTu, out string resolvedTargetDaoTu))
				{
					sourceDaoTu = resolvedSourceDaoTu;
					targetDaoTu = resolvedTargetDaoTu;
					hasPersistedIdentity = true;
				}
			}
			if (!hasPersistedIdentity) return false;

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string storedSourceDaoTu);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string storedManifestDaoTu);
			string namedDaoTu = XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(state.GuoWei);
			bool identityAlreadyCorrect = string.Equals(Normalize(currentDaoTu), sourceDaoTu, StringComparison.Ordinal)
				&& string.Equals(Normalize(namedDaoTu), targetDaoTu, StringComparison.Ordinal)
				&& string.Equals(Normalize(storedSourceDaoTu), sourceDaoTu, StringComparison.Ordinal)
				&& string.Equals(Normalize(storedManifestDaoTu), targetDaoTu, StringComparison.Ordinal);
			if (identityAlreadyCorrect)
			{
				migratedDaoTu = sourceDaoTu;
				return false;
			}

			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L) return false;
			int slot = Math.Max(1, XjGuoWeiCalculator.ResolveSlotIndex(state.GuoWei));
			string replacement = XjGuoWeiCalculator.BuildGuoWeiSlotName(
				targetDaoTu, XjGuoWeiCalculator.RunWei, slot);
			string jinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
				actor, sourceDaoTu, targetDaoTu, XjGuoWeiCalculator.RunWei,
				XjJinXingCalculator.Calculate(targetDaoTu, actorId));
			if (string.IsNullOrWhiteSpace(jinXing)) return false;

			if (!TryClaim(actor, targetDaoTu, jinXing, replacement, state.SuccessYear, sourceDaoTu))
			{
				if (!TryResolveAvailableGuoWei(
					targetDaoTu,
					XjGuoWeiCalculator.RunWei,
					actorId,
					actorId + Math.Max(0, state.SuccessYear),
					false,
					out _,
					out replacement)
					|| !TryClaim(actor, targetDaoTu, jinXing, replacement, state.SuccessYear, sourceDaoTu))
				{
					return false;
				}
			}

			string previousManifestDaoTu = Normalize(storedManifestDaoTu);
			if (previousManifestDaoTu.Length == 0) previousManifestDaoTu = Normalize(namedDaoTu);
			if (previousManifestDaoTu.Length == 0) previousManifestDaoTu = Normalize(currentDaoTu);
			string previousGuoWei = state.GuoWei;
			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, sourceDaoTu, true);
			XjJinDanAccessor.WriteSuccess(actor, jinXing, replacement, state.SuccessYear);
			XjHighRealmDaoStateService.RepairIntercalaryIdentity(
				actor, sourceDaoTu, targetDaoTu, replacement, jinXing, state.SuccessYear, currentYear,
				previousManifestDaoTu, previousGuoWei);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, targetDaoTu, replacement, state.SuccessYear);
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
			XjXianJiAccessor.ReconcileRealmLimit(actor);
			migratedDaoTu = sourceDaoTu;
			migratedState = XjJinDanAccessor.BuildState(actor);
			return migratedState.Found;
		}

		internal static void ReleaseForActor(long actorId, string guoWei)
		{
			if (actorId <= 0L || string.IsNullOrWhiteSpace(guoWei))
			{
				return;
			}

			bool changed = false;
			string key = NormalizeKey(guoWei);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry entry)
				&& entry.ActorId == actorId)
			{
				XjTaiYinHiddenFruitSystem.OnTaiYinHolderReleased(actorId, entry.GuoWei, Math.Max(1, XjYearTracker.CurrentYear));
				activeEntriesByGuoWei.Remove(key);
				changed = true;
			}

			if (historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry historical)
				&& historical.IsActive
				&& string.Equals(NormalizeKey(historical.GuoWei), key, StringComparison.Ordinal))
			{
				historyEntriesByActorId.Remove(actorId);
				changed = true;
			}

			if (changed)
			{
				Touch(protectedCommit: false);
			}
		}

		internal static void ReleaseFromSnapshot(XjDeathSnapshot snapshot)
		{
			if (!snapshot.Found || snapshot.ActorId <= 0L)
			{
				return;
			}

			// 主果与道胎次位是两条独立占用账本，死亡必须分别释放。先冻结是否真实持有太阴正果，
			// 以便次位释放后仍能把太阴果位合法托藏给同一家族。
			bool heldTaiYin = string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(snapshot.GuoWei), XjTaiYinHiddenFruitSystem.TaiYinZhengWei, StringComparison.Ordinal);
			if (XjFruitPositionWorldState.TryGetDaoTaiBinding(snapshot.ActorId, out XjDaoTaiPositionBindingArchiveRecord deathBinding)
				&& deathBinding != null
				&& (string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(deathBinding.PrimaryPositionId), XjTaiYinHiddenFruitSystem.TaiYinZhengWei, StringComparison.Ordinal)
					|| string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(deathBinding.SecondaryPositionId), XjTaiYinHiddenFruitSystem.TaiYinZhengWei, StringComparison.Ordinal)))
			{
				heldTaiYin = true;
			}
			XjFruitPositionWorldState.ReleaseDaoTaiBinding(
				snapshot.ActorId, snapshot.Name, ResolveActorDaoHui(snapshot.ActorId), snapshot.Year, "Death");
			bool changed = RemoveAllActiveClaimsForActor(snapshot.ActorId);
			if (!historyEntriesByActorId.TryGetValue(snapshot.ActorId, out XjGuoWeiRegistryEntry entry))
			{
				entry = new XjGuoWeiRegistryEntry(
					true,
					snapshot.ActorId,
					snapshot.Name,
					string.Empty,
					snapshot.DaoTu,
					snapshot.JinXing,
					snapshot.GuoWei,
					0,
					StatusActive,
					0,
					string.Empty);
			}

			string guoWei = string.IsNullOrWhiteSpace(entry.GuoWei) ? snapshot.GuoWei : entry.GuoWei;
			XjTaiYinHiddenFruitSystem.OnTaiYinHolderDeath(snapshot, heldTaiYin, entry.FamilyName, Math.Max(1, snapshot.Year));
			if (string.IsNullOrWhiteSpace(guoWei))
			{
				if (changed)
				{
					Touch(protectedCommit: true);
				}
				return;
			}

			XjFruitPositionWorldState.RecordHolderEnded(
				guoWei,
				snapshot.ActorId,
				BuildPreviousHolderDisplay(snapshot.ActorId, string.IsNullOrWhiteSpace(entry.ActorName) ? snapshot.Name : entry.ActorName),
				ResolveActorDaoHui(snapshot.ActorId),
				snapshot.Year);

			XjGuoWeiRegistryEntry released = new XjGuoWeiRegistryEntry(
				true,
				snapshot.ActorId,
				string.IsNullOrWhiteSpace(entry.ActorName) ? snapshot.Name : entry.ActorName,
				entry.FamilyName,
				string.IsNullOrWhiteSpace(entry.DaoTu) ? snapshot.DaoTu : entry.DaoTu,
				string.IsNullOrWhiteSpace(entry.JinXing) ? snapshot.JinXing : entry.JinXing,
				guoWei,
				entry.Year,
				StatusDeceased,
				snapshot.Year,
				EndReasonDeath);

			if (!historyEntriesByActorId.TryGetValue(snapshot.ActorId, out XjGuoWeiRegistryEntry old)
				|| !EntriesEqual(old, released))
			{
				historyEntriesByActorId[snapshot.ActorId] = released;
				changed = true;
			}

			if (changed)
			{
				Touch(protectedCommit: true);
			}
		}

		private static bool RemoveAllActiveClaimsForActor(long actorId)
		{
			if (actorId <= 0L || activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}

			List<string> keys = null;
			foreach (KeyValuePair<string, XjGuoWeiRegistryEntry> pair in activeEntriesByGuoWei)
			{
				if (pair.Value.ActorId != actorId)
				{
					continue;
				}

				keys ??= new List<string>();
				keys.Add(pair.Key);
			}

			if (keys == null)
			{
				return false;
			}

			for (int i = 0; i < keys.Count; i++)
			{
				if (activeEntriesByGuoWei.TryGetValue(keys[i], out XjGuoWeiRegistryEntry removed))
				{
					XjTaiYinHiddenFruitSystem.OnTaiYinHolderReleased(actorId, removed.GuoWei, Math.Max(1, XjYearTracker.CurrentYear));
				}
				activeEntriesByGuoWei.Remove(keys[i]);
			}
			return true;
		}

		/// <summary>
		/// 清理由历史版本误写给故尊命痕的活动果位。命痕并非当世持位者，
		/// 因此既不保留活动占用，也不把这次清理写成一次真实的离位沿革。
		/// </summary>
		internal static bool RemoveEphemeralClaims(long actorId)
		{
			if (actorId <= 0L) return false;
			bool changed = RemoveAllActiveClaimsForActor(actorId);
			if (historyEntriesByActorId.Remove(actorId)) changed = true;
			if (changed) Touch(protectedCommit: true);
			return changed;
		}

		internal static void ReleaseActiveForPromotion(long actorId, string guoWei, int currentYear)
		{
			if (actorId <= 0L || string.IsNullOrWhiteSpace(guoWei))
			{
				return;
			}

			bool changed = RemoveAllActiveClaimsForActor(actorId);
			if (!historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry historical)
				|| !historical.Found)
			{
				historical = new XjGuoWeiRegistryEntry(
					true,
					actorId,
					string.Empty,
					string.Empty,
					string.Empty,
					string.Empty,
					Normalize(guoWei),
					0,
					StatusActive,
					0,
					string.Empty);
			}

			string releasedGuoWei = string.IsNullOrWhiteSpace(historical.GuoWei)
				? XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei)
				: XjGuoWeiCalculator.NormalizeGuoWeiName(historical.GuoWei);
			XjFruitPositionWorldState.RecordHolderEnded(
				releasedGuoWei,
				actorId,
				BuildPreviousHolderDisplay(actorId, historical.ActorName),
				ResolveActorDaoHui(actorId),
				Math.Max(1, currentYear));
			XjFruitPositionWorldState.ReleaseDaoTaiBinding(
				actorId, historical.ActorName, ResolveActorDaoHui(actorId), Math.Max(1, currentYear), "Promotion");

			XjGuoWeiRegistryEntry released = new XjGuoWeiRegistryEntry(
				true,
				historical.ActorId,
				historical.ActorName,
				historical.FamilyName,
				historical.DaoTu,
				historical.JinXing,
				releasedGuoWei,
				historical.Year,
				StatusReleased,
				Math.Max(1, currentYear),
				"ShenDanPromotion");
			if (!historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry old)
				|| !EntriesEqual(old, released))
			{
				historyEntriesByActorId[actorId] = released;
				changed = true;
			}

			if (changed)
			{
				Touch(protectedCommit: true);
			}
		}
		private static int ResolveActorDaoHui(long actorId)
		{
			return XjScheduler.ResolveActor(actorId, out Actor actor)
				? (int)XjDaoHuiPolicy.Read(actor)
				: 0;
		}

		private static string BuildPreviousHolderDisplay(long actorId, string fallbackName)
		{
			string name = string.IsNullOrWhiteSpace(fallbackName) ? "无名真君" : fallbackName.Trim();
			if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null)
			{
				return name;
			}
			string liveName = actor.getName();
			if (!string.IsNullOrWhiteSpace(liveName)) name = liveName.Trim();
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string title)
				&& !string.IsNullOrWhiteSpace(title)
				&& name.IndexOf(title.Trim(), StringComparison.Ordinal) < 0)
			{
				name = title.Trim() + "·" + name;
			}
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string realm)
				&& !string.IsNullOrWhiteSpace(realm)
				&& name.IndexOf(realm, StringComparison.Ordinal) < 0)
			{
				name += "-" + realm.Trim();
			}
			return name;
		}

}
