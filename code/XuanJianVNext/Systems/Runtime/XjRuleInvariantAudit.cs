using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;

using XuanJianVNext.Systems.LongShu;
namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 发布前/读档后不变量审计。只读取运行期修士缓存和归档索引，不访问 World.world.units 扫描全体单位。
/// 对能够确定修复的可见特质偏移执行幂等同步；果位重复只在权威持有人明确时迁移旧残留，
/// 其余果位与故尊等高风险冲突仍只报告，不猜测重建。
/// </summary>
internal static class XjRuleInvariantAudit
{
	private static string _lastReport = string.Empty;
	private static string _lastRunReason = string.Empty;
	private static int _lastIssueCount;
	private static int _lastAuditedActorCount;
	private static int _lastRepairCount;
	private static int _lastRunYear;
	private static bool _afterLoadPending;

	internal static int LastIssueCount => _lastIssueCount;
	internal static int LastRepairCount => _lastRepairCount;
	internal static bool HasDeferredAfterLoadAudit => _afterLoadPending;

	internal static void RunAfterLoad()
	{
		// 归档导入发生在角色索引和高境缓存完全重建之前。这里只登记一次，
		// 由年度世界车道在首个高境维护阶段执行，避免“空缓存审计通过”。
		_afterLoadPending = true;
	}

	internal static void TryRunDeferredAfterLoad()
	{
		if (!_afterLoadPending) return;
		_afterLoadPending = false;
		Run("AfterLoad", repairSafeIssues: true, auditAllCultivators: false);
	}

	internal static string RunReleaseAudit()
	{
		Run("ReleaseAudit", repairSafeIssues: false, auditAllCultivators: true);
		return _lastReport;
	}

	internal static void Clear()
	{
		_lastReport = string.Empty;
		_lastRunReason = string.Empty;
		_lastIssueCount = 0;
		_lastAuditedActorCount = 0;
		_lastRepairCount = 0;
		_lastRunYear = 0;
		_afterLoadPending = false;
	}

	private static void Run(string reason, bool repairSafeIssues, bool auditAllCultivators)
	{
		List<string> issues = new List<string>();
		AuditCultivators(issues, repairSafeIssues, auditAllCultivators, out int auditedActorCount, out int repairCount);
		AuditGuZunRecords(issues);
		AuditShenDanAnchors(issues);
		AuditDaoTuRelations(issues);
		AuditXianJiPools(issues);
		AuditInitialDaoTuPool(issues);
		AuditWorldEventCatalog(issues);
		XjSectAuthorityAudit.Audit(issues, repairSafeIssues, ref repairCount);

		_lastRunReason = reason ?? string.Empty;
		_lastRunYear = Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);
		_lastIssueCount = issues.Count;
		_lastAuditedActorCount = auditedActorCount;
		_lastRepairCount = repairCount;
		StringBuilder report = new StringBuilder(384);
		report.Append("[玄鉴不变量审计]")
			.Append(_lastRunReason)
			.Append("：")
			.Append(issues.Count)
			.Append("项；审计角色")
			.Append(auditedActorCount)
			.Append("人；安全修复")
			.Append(repairCount)
			.Append("项；世界年")
			.Append(_lastRunYear);
		for (int i = 0; i < issues.Count && i < 64; i++) report.Append("\n- ").Append(issues[i]);
		if (issues.Count > 64) report.Append("\n- 其余").Append(issues.Count - 64).Append("项已省略");
		_lastReport = report.ToString();
		if (issues.Count > 0) Debug.LogWarning(_lastReport);
		else Debug.Log(_lastReport);
	}

	private static void AuditCultivators(
		List<string> issues,
		bool repairSafeIssues,
		bool auditAllCultivators,
		out int auditedActorCount,
		out int repairedIssueCount)
	{
		// 读档后只审计真人级以上角色，避免大型存档在首帧同步检查全部修士；
		// 发布审计才覆盖完整修士缓存。两种模式都不访问 World.world.units。
		auditedActorCount = 0;
		repairedIssueCount = 0;
		IReadOnlyList<long> ids = auditAllCultivators
			? XjCultivatorCache.GetAllIds()
			: XjCultivatorCache.GetZhenRenOrHigherIds();
		string[] daoTuTraitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ids[i], out Actor actor) || actor?.data == null) continue;
			auditedActorCount++;
			long actorId = ids[i];

			if (XjCultivationPathRules.IsShi(actor))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string shiTradition);
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm);
				bool invalidAncientMiddle = string.Equals(shiTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
					&& (string.Equals(shiRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
						|| string.Equals(shiRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal));
				if (invalidAncientMiddle && repairSafeIssues)
				{
					XjShiState.EnsureConsistent(actor, Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)));
					repairedIssueCount++;
					XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out shiTradition);
					XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out shiRealm);
					invalidAncientMiddle = string.Equals(shiTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
						&& (string.Equals(shiRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
							|| string.Equals(shiRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal));
				}
				if (invalidAncientMiddle)
					issues.Add("古释角色" + actorId + "非法处于" + XjShiCatalog.GetRealmDisplay(shiRealm) + "境界");
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string authoritativeDaoTu);
			string normalizedAuthorityDaoTu = (authoritativeDaoTu ?? string.Empty).Trim();
			bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
			bool isZiJin = XjCultivationPathRules.IsZiFuJinDan(actor);
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			bool shouldProjectDaoTu = XjVisibleTraitSync.ShouldProjectDaoTuTrait(actor, realmId);
			bool authorityValid = normalizedAuthorityDaoTu.Length > 0
				&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedAuthorityDaoTu, out _);
			string expectedVisibleDaoTu = shouldProjectDaoTu && authorityValid
				? (XjVisibleTraitSync.ResolveVisibleDaoTuTraitProjection(actor, normalizedAuthorityDaoTu) ?? string.Empty).Trim()
				: string.Empty;

			if (normalizedAuthorityDaoTu.Length > 0 && !authorityValid)
			{
				issues.Add("角色" + actorId + "持有非法道途权威字段：" + normalizedAuthorityDaoTu);
			}
			int visibleCount = 0;
			string visibleDaoTu = string.Empty;
			bool traitReadFailed = false;
			for (int traitIndex = 0; traitIndex < daoTuTraitIds.Length; traitIndex++)
			{
				string traitId = daoTuTraitIds[traitIndex];
				try
				{
					if (!actor.hasTrait(traitId)) continue;
					visibleCount++;
					if (visibleDaoTu.Length == 0)
					{
						XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(traitId, out visibleDaoTu);
					}
				}
				catch (Exception exception)
				{
					issues.Add("角色" + actorId + "读取道途特质失败：" + exception.GetType().Name);
					traitReadFailed = true;
					break;
				}
			}

			if (!traitReadFailed)
			{
				bool daoTuMismatch;
				if (!shouldProjectDaoTu)
				{
					daoTuMismatch = visibleCount != 0;
				}
				else if (!authorityValid)
				{
					// 到了应显示道途的阶段却没有合法权威字段，是身份缺失；
					// 审计不能在普通运行期从投影反写，只报告，读档迁移负责恢复。
					daoTuMismatch = true;
				}
				else
				{
					daoTuMismatch = visibleCount != 1
						|| !string.Equals((visibleDaoTu ?? string.Empty).Trim(), expectedVisibleDaoTu, StringComparison.Ordinal);
				}

				if (daoTuMismatch)
				{
					issues.Add("角色" + actorId + "道途权威字段与可见特质不一致（期望"
						+ (shouldProjectDaoTu ? "显示" + expectedVisibleDaoTu : "隐藏") + "，可见" + visibleCount + "项）");
					if (repairSafeIssues && (!shouldProjectDaoTu || authorityValid))
					{
						XjVisibleTraitSync.SyncDaoTuTrait(actor, shouldProjectDaoTu ? authoritativeDaoTu : string.Empty);
						repairedIssueCount++;
					}
				}
			}

			int realmOrder = XjRealmHelper.GetOrder(realmId);
			bool hasEnteredCultivation = realmOrder > 0 || isFuQi || isZiJin;
			if (hasEnteredCultivation && isFuQi == isZiJin)
			{
				issues.Add("角色" + actorId + "修炼路线不是唯一值");
			}

			bool isSharedShenDan = string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
			if (isFuQi && !isSharedShenDan && XjCultivationPathRules.IsZiFuRealm(realmId))
			{
				issues.Add("服气角色" + actorId + "持有紫府金丹道境界：" + XjRealmHelper.GetDisplayName(realmId));
			}
			else if (isZiJin && XjCultivationPathRules.IsFuQiRealm(realmId))
			{
				issues.Add("紫府金丹道角色" + actorId + "持有服气养性境界：" + XjRealmHelper.GetDisplayName(realmId));
			}

			XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
			if (!jinDan.Found
				&& repairSafeIssues
				&& XjJinDanAccessor.TryRepairMissingSuccessYearFromPersistedRealm(actor))
			{
				// 只补回已有事实的时间字段；随后复用冷路径重水合恢复内存注册表。
				XjHighRealmRehydration.ReconcileActor(actor);
				repairedIssueCount++;
				jinDan = XjJinDanAccessor.BuildState(actor);
			}
			XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
			XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
			XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
			string guoWeiType = jinDan.Found
				? XjGuoWeiRegistry.ResolveTypeFromName(jinDan.GuoWei)
				: string.Empty;
			if (jinDan.Found
				&& XjGuoWeiRegistry.TryResolveConflictingHolder(jinDan.GuoWei, actorId, out long conflictingHolderId, out string conflictSource))
			{
				issues.Add("角色" + actorId + "与角色" + conflictingHolderId + "重复持有"
					+ XjGuoWeiCalculator.GetDisplayGuoWeiName(jinDan.GuoWei) + "（" + conflictSource + "）");
				if (repairSafeIssues)
				{
					XjGuoWeiRegistry.ReconcileLiveActor(actor);
					repairedIssueCount++;
					jinDan = XjJinDanAccessor.BuildState(actor);
					guoWeiType = jinDan.Found
						? XjGuoWeiRegistry.ResolveTypeFromName(jinDan.GuoWei)
						: string.Empty;
				}
			}
			string xianJiSourceDaoTu = ResolveXianJiSourceDaoTu(
				normalizedAuthorityDaoTu,
				gongFa,
				qiuJinFa,
				guoWeiType);

			if (XjXianGuoSystem.IsDiMingYang(actor))
			{
				if (!string.Equals(normalizedAuthorityDaoTu, XjXianGuoSystem.MingYangDaoTu, StringComparison.Ordinal))
				{
					issues.Add("帝明阳角色" + actorId + "权威道途漂移为：" + normalizedAuthorityDaoTu);
				}
				xianJiSourceDaoTu = XjXianGuoSystem.MingYangDaoTu;
			}

			if (jinDan.Found
				&& string.Equals(guoWeiType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				&& XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor))
			{
				issues.Add("余位角色" + actorId + "错误获得自然死亡豁免");
			}

			string expectedGongFaDaoTu = string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? xianJiSourceDaoTu
				: normalizedAuthorityDaoTu;
			if (gongFa.Found
				&& !string.IsNullOrWhiteSpace(gongFa.DaoTu)
				&& !string.Equals(gongFa.DaoTu.Trim(), expectedGongFaDaoTu, StringComparison.Ordinal))
			{
				issues.Add("角色" + actorId + "功法道途与修炼源道途不一致：" + gongFa.DaoTu);
			}

			if (isFuQi && xianJi.Found && xianJi.Ids != null && xianJi.Ids.Length > 0)
			{
				issues.Add("服气角色" + actorId + "残留紫府仙基神通实体");
			}
			else if (isZiJin && xianJi.Found && xianJi.Ids != null)
			{
				if (actor.hasTrait("ChuShen8"))
				{
					bool hasFallback = false;
					for (int xianJiAuditIndex = 0; xianJiAuditIndex < xianJi.Ids.Length; xianJiAuditIndex++)
					{
						XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(xianJiSourceDaoTu, xianJi.Ids[xianJiAuditIndex]);
						if (kind == XjXianJiPoolKind.Other)
						{
							issues.Add("道主角色" + actorId + "持有无关道途神通：" + xianJi.Ids[xianJiAuditIndex]);
						}
						if (kind == XjXianJiPoolKind.Lower || kind == XjXianJiPoolKind.Adjacent) hasFallback = true;
					}
					if (hasFallback && XjXianJiCatalog.TryPickUpperForProgression(
						xianJiSourceDaoTu,
						xianJi.Count + 1,
						actorId,
						xianJi.Ids,
						out string unusedUpper))
					{
						issues.Add("道主角色" + actorId + "尚有上位神通" + unusedUpper + "未修，却已进入下位或相邻神通");
					}
				}

				int nativeRequired = Math.Min(3, xianJi.Ids.Length);
				for (int xianJiIndex = 0; xianJiIndex < nativeRequired; xianJiIndex++)
				{
					string id = xianJi.Ids[xianJiIndex];
					if (XjXianJiCatalog.GetPoolKind(xianJiSourceDaoTu, id) != XjXianJiPoolKind.Native)
					{
						issues.Add("角色" + actorId + "第" + (xianJiIndex + 1) + "门上位神通归属错误：" + id);
					}
				}
			}

			bool isZiJinJinDan = isZiJin && string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
			bool isFuQiZhenJun = isFuQi && string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
			if (isZiJinJinDan
				&& repairSafeIssues
				&& XjGuoWeiRegistry.IsZiJinPureUpperPositionMismatch(actor, out _, out _)
				&& XjGuoWeiRegistry.TryRepairZiJinPureUpperPositionInvariant(
					actor,
					Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)),
					out string pureUpperRepairOutcome))
			{
				issues.Add("金丹角色" + actorId + "五上位果位不变量已修复：" + pureUpperRepairOutcome);
				repairedIssueCount++;
				continue;
			}
			if ((isZiJinJinDan || isFuQiZhenJun) && !jinDan.Found)
			{
				issues.Add("角色" + actorId + "已处于"
					+ XjRealmHelper.GetDisplayName(realmId)
					+ "，但缺少完整金性果位状态");
			}

			if (isZiJinJinDan)
			{
				if (!xianJi.Found || xianJi.Count != XjXianJiState.MaxCount)
				{
					issues.Add("金丹角色" + actorId + "不是完整五神通结构");
				}
				else if (jinDan.Found)
				{
					string expectedType = XjGuoWeiCalculator.Calculate(actor, xianJiSourceDaoTu, xianJi);
					// 旧档已经成立的闰位按历史身份继续承认：新拓扑只约束新证位，不倒查抹除旧角色。
					// 仅当旧版五神通仍能解析出同一根道→显道身份时放行，真正的混池/残缺结构仍会报错。
					if (string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
						&& string.Equals(expectedType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal)
						&& XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
							actor, xianJi, out string legacyRunSourceDaoTu, out string legacyManifestDaoTu)
						&& string.Equals(legacyRunSourceDaoTu, xianJiSourceDaoTu, StringComparison.Ordinal)
						&& string.Equals(legacyManifestDaoTu, normalizedAuthorityDaoTu, StringComparison.Ordinal))
					{
						expectedType = XjGuoWeiCalculator.RunWei;
					}

					if (string.Equals(expectedType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
					{
						issues.Add("金丹角色" + actorId + "的五神通不属于当前合法果位结构");
					}
					else if (!string.Equals(expectedType, guoWeiType, StringComparison.Ordinal))
					{
						issues.Add("金丹角色" + actorId + "的神通结构应为"
							+ expectedType
							+ "，实际果位为"
							+ guoWeiType);
					}

					if (string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
					{
						// 闰位身份以证成时固化的根道/显道为第一真值。
						// 只有旧档完全缺失身份字段时，且尚无神通变异，才从五神通反推一次。
						bool hasResolvedIdentity = XjHighRealmDaoStateService.TryReadPersistedIntercalaryIdentity(
							actor, out string runSourceDaoTu, out string manifestDaoTu);
						if (!hasResolvedIdentity && !XjHighRealmDaoStateService.HasRecordedShenTongMutation(actor))
						{
							hasResolvedIdentity = XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
								actor, xianJi, out runSourceDaoTu, out manifestDaoTu);
						}
						if (!hasResolvedIdentity)
						{
							issues.Add("闰位角色" + actorId + "缺少可验证的根道与显道身份");
						}
						else
						{
							XjActorAccessor.TryGetString(
								actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string storedSourceDaoTu);
							XjActorAccessor.TryGetString(
								actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string storedManifestDaoTu);
							storedSourceDaoTu = XjGuoWeiCalculator.NormalizeDaoTu(storedSourceDaoTu);
							storedManifestDaoTu = XjGuoWeiCalculator.NormalizeDaoTu(storedManifestDaoTu);
							bool identityMismatch = !string.Equals(runSourceDaoTu, normalizedAuthorityDaoTu, StringComparison.Ordinal)
								|| !string.Equals(runSourceDaoTu, storedSourceDaoTu, StringComparison.Ordinal)
								|| !string.Equals(manifestDaoTu, storedManifestDaoTu, StringComparison.Ordinal)
								|| !string.Equals(
									XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(jinDan.GuoWei),
									manifestDaoTu,
									StringComparison.Ordinal);
							if (identityMismatch)
							{
								issues.Add("闰位角色" + actorId + "应为" + runSourceDaoTu + "根基→"
									+ manifestDaoTu + "闰位，现有归属未完全一致");
								if (repairSafeIssues)
								{
									XjGuoWeiRegistry.ReconcileLiveActor(actor);
									repairedIssueCount++;
								}
							}
						}
					}
				}
			}

			if (realmOrder >= XjRealmSuppression.TierZiFu
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtTitle, out string swordTitle)
				&& !string.IsNullOrWhiteSpace(swordTitle)
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string mainTitle)
				&& string.Equals((mainTitle ?? string.Empty).Trim(), swordTitle.Trim(), StringComparison.Ordinal))
			{
				issues.Add("高境角色" + actorId + "的剑仙别号覆盖了境界尊号");
				if (repairSafeIssues)
				{
					XjRealmTitleApplyService.EnsureTitleForRealm(actor, realmId, normalizedAuthorityDaoTu);
					repairedIssueCount++;
				}
			}
		}
	}

	private static string ResolveXianJiSourceDaoTu(
		string authoritativeDaoTu,
		in XjGongFaState gongFa,
		in XjQiuJinFaState qiuJinFa,
		string guoWeiType)
	{
		string normalizedAuthority = (authoritativeDaoTu ?? string.Empty).Trim();
		if (!string.Equals(guoWeiType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			return normalizedAuthority;
		}

		string qiuJinSource = (qiuJinFa.SourceDaoTu ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(qiuJinSource))
		{
			return qiuJinSource;
		}

		string gongFaSource = (gongFa.DaoTu ?? string.Empty).Trim();
		return string.IsNullOrWhiteSpace(gongFaSource)
			? normalizedAuthority
			: gongFaSource;
	}

	private static void AuditGuZunRecords(List<string> issues)
	{
		IReadOnlyList<XjGuZunArchiveRecord> records = XjGuZunRegistry.ReadAll();
		HashSet<long> liveActorIds = new HashSet<long>();
		HashSet<string> archiveIds = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < records.Count; i++)
		{
			XjGuZunArchiveRecord record = records[i];
			if (!archiveIds.Add(record.ArchiveId)) issues.Add("故尊档案ID重复：" + record.ArchiveId);
			if (record.IsCurrentlyManifested && record.CurrentActorId > 0L && !liveActorIds.Add(record.CurrentActorId))
				issues.Add("一个存活角色被多个故尊档案绑定：" + record.CurrentActorId);
			if (record.IsCurrentlyManifested && record.CurrentActorId <= 0L)
				issues.Add("故尊档案标记已重现但没有存活角色ID：" + record.ArchiveId);
			else if (record.IsCurrentlyManifested
				&& (!XjScheduler.ResolveActor(record.CurrentActorId, out Actor manifested) || !XjSafeCore.IsAliveActor(manifested)))
				issues.Add("故尊档案绑定了不可解析的存活角色：" + record.ArchiveId + "->" + record.CurrentActorId);
			if (record.HeavenFavored && string.IsNullOrWhiteSpace(record.HighestSnapshotJson))
				issues.Add("受天地眷顾的故尊缺少最高境界快照：" + record.ArchiveId);
		}
	}

	private static void AuditShenDanAnchors(List<string> issues)
	{
		IReadOnlyList<long> ids = XjCultivatorCache.GetZhenJunOrHigherIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjScheduler.ResolveActor(ids[i], out Actor actor) || actor?.data == null) continue;
			XjShenDanState state = XjShenDanAccessor.BuildState(actor);
			if (!state.Found) continue;
			if (state.AnchorActorId <= 0L || !XjScheduler.ResolveActor(state.AnchorActorId, out Actor anchor) || !XjSafeCore.IsAliveActor(anchor))
			{
				issues.Add("神丹" + ids[i] + "缺少合法存活金丹锚点");
				continue;
			}
			if (XjShenDanAccessor.BuildState(anchor).Found)
				issues.Add("神丹" + ids[i] + "错误挂靠在另一个神丹" + state.AnchorActorId + "之下");
			else if (!XjJinDanAccessor.BuildState(anchor).Found)
				issues.Add("神丹" + ids[i] + "的锚点不是活动金丹" + state.AnchorActorId);
		}
	}
	private static void AuditInitialDaoTuPool(List<string> issues)
	{
		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.InitialAssignableEntries;
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> bingGuRoots = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < entries.Count; i++)
		{
			XjDaoTuVisibleTraitEntry entry = entries[i];
			if (!names.Add(entry.DisplayName))
			{
				issues.Add("初始道途池存在重复权重：" + entry.DisplayName);
				continue;
			}
			if (!XjDaoTuCatalog.TryResolve(entry.DisplayName, out XjDaoTuDefinition definition))
			{
				issues.Add("初始道途池存在无法解析项：" + entry.DisplayName);
				continue;
			}
			if (definition.IsBingGu) bingGuRoots.Add(definition.RootId);
			else if (!definition.IsCommonAncient) issues.Add("初始道途池混入后起或不可随机道途：" + entry.DisplayName);
		}

		for (int i = 0; i < XjDaoTuCatalog.Definitions.Length; i++)
		{
			XjDaoTuDefinition definition = XjDaoTuCatalog.Definitions[i];
			if (!definition.IsBingGu
				|| !XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
				|| !core.GameplayImplemented) continue;
			if (!bingGuRoots.Contains(definition.RootId))
			{
				issues.Add("已实现并古道途未进入等权初始池：" + definition.DisplayName);
			}
		}
	}

	private static void AuditDaoTuRelations(List<string> issues)
	{
		IReadOnlyList<string> relationIssues = XjDaoTuRelationCatalog.Validate();
		for (int i = 0; i < relationIssues.Count; i++)
		{
			if (!string.IsNullOrWhiteSpace(relationIssues[i])) issues.Add("道途道网：" + relationIssues[i]);
		}

		// 固定拓扑回归：五德不再同根全邻，十二炁只认环邻，对炁另列远亲。
		if (!XjDaoTuRelationCatalog.IsDirectAdjacent("府水", "坎水")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("府水", "合水"))
		{
			issues.Add("道途道网：水德五现链近邻关系偏移");
		}
		if (!XjDaoTuRelationCatalog.IsDirectAdjacent("清炁", "晞炁")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("清炁", "邃炁")
			|| XjDaoTuRelationCatalog.Resolve("清炁", "邃炁") != XjDaoTuRelationKind.Counterpart)
		{
			issues.Add("道途道网：十二炁六行环/对炁关系偏移");
		}
		if (XjDaoTuRelationCatalog.Resolve("鸺葵", "府水") != XjDaoTuRelationKind.ElementAffinity
			|| XjDaoTuRelationCatalog.Resolve("霄雷", "府水") != XjDaoTuRelationKind.ElementAffinity
			|| XjDaoTuRelationCatalog.Resolve("长庚", "兑金") == XjDaoTuRelationKind.ElementAffinity)
		{
			issues.Add("道途道网：存毁二象的五德映照边界偏移");
		}
		if (!XjDaoTuRelationCatalog.IsDirectAdjacent("太阴", "渊照")
			|| !XjDaoTuRelationCatalog.IsDirectAdjacent("渊照", "坎水")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("渊照", "少阴")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("渊照", "府水"))
		{
			issues.Add("道途道网：渊照必须只以太阴、坎水为直接近邻");
		}
		if (!XjDaoTaiDualPositionSystem.IsDaoTuPairCompatible("太阴", "少阴")
			|| !XjDaoTaiDualPositionSystem.IsDaoTuPairCompatible("府水", "合水")
			|| !XjDaoTaiDualPositionSystem.IsDaoTuPairCompatible("渊照", "坎水")
			|| XjDaoTaiDualPositionSystem.IsDaoTuPairCompatible("太阴", "兑金"))
		{
			issues.Add("道胎双位：第二位未严格服从道途关系表");
		}
		if (XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("渊照", "太阴")
			|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("渊照", "坎水")
			|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("太阴", "渊照")
			|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("坎水", "渊照")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("渊照", "少阴")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("渊照", "府水")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("少阴", "渊照")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("府水", "渊照"))
		{
			issues.Add("果位判定：渊照闰位必须双向只允许太阴、坎水");
		}
		if (!XjDaoTuRelationCatalog.IsDirectAdjacent("虹霞", "戊土")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("虹霞", "艮土")
			|| XjDaoTuRelationCatalog.IsDirectAdjacent("虹霞", "太阴")
			|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("虹霞", "戊土")
			|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("戊土", "虹霞")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("虹霞", "艮土")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("艮土", "虹霞")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("虹霞", "太阴")
			|| !XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing("太阴", "虹霞"))
		{
			issues.Add("果位判定：虹霞闰位必须双向只允许戊土");
		}
		IReadOnlyList<XjFruitPositionCapacityRule> capacityRules = XjFruitPositionWorldState.ReadCapacityRules();
		if (capacityRules == null || capacityRules.Count == 0
			|| capacityRules[0].Residual < XjGuoWeiQuanBingRules.MinimumYuWeiSlotCount
			|| capacityRules[0].Intercalary < XjGuoWeiQuanBingRules.MinimumRunWeiSlotCount)
		{
			issues.Add("果位判定：低道势不得把余位或闰位容量压到零");
		}
	}

	private static void AuditXianJiPools(List<string> issues)
	{
		string[] poolIssues = XjXianJiCatalog.GetPoolValidationIssues();
		for (int i = 0; i < poolIssues.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(poolIssues[i])) issues.Add("神通池：" + poolIssues[i]);
		}

		// 固定回归样例：府水的五现近邻是牝水/坎水。四府水上位加一坎水上位
		// 在无角色道慧上下文时即可显化坎水闰位。
		XjXianJiState intercalarySample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖", "溪上翁" },
			0,
			"InvariantSample");
		string intercalaryType = XjGuoWeiCalculator.Calculate("府水", intercalarySample);
		string intercalaryTarget = XjGuoWeiCalculator.ResolveManifestDaoTu("府水", intercalarySample, intercalaryType);
		if (!string.Equals(intercalaryType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			|| !string.Equals(intercalaryTarget, "坎水", StringComparison.Ordinal))
		{
			issues.Add("果位判定：四府水一坎水上位未正确归入近邻坎水闰位");
		}

		// 全局硬边界：闰位五门必须全部是上位神通。坎水下位“玄冥珠”即使来自府水近邻，
		// 也不得进入 Adjacent 池，更不能与四门府水上位拼成坎水闰位。
		XjXianJiState invalidAdjacentLowerRunSample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖", "玄冥珠" },
			0,
			"InvariantSample");
		if (XjXianJiCatalog.GetPoolKind("府水", "玄冥珠") == XjXianJiPoolKind.Adjacent
			|| !string.Equals(XjGuoWeiCalculator.Calculate("府水", invalidAdjacentLowerRunSample),
				XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal)
			|| XjGuoWeiCalculator.TryResolveIntercalaryTargetDaoTu(
				"府水", invalidAdjacentLowerRunSample, out _, out _))
		{
			issues.Add("果位判定：闰位混入相邻道途下位神通未被全局拒绝");
		}

		// 合水与府水仍同属水德，但隔着坎水/渌水，属于结构远亲：
		// 无角色上下文不得直接成闰，道慧门槛必须落在结构远闰层，但不得高过本道正果门槛。
		XjXianJiState remoteIntercalarySample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖", "妖渎河" },
			0,
			"InvariantSample");
		if (!string.Equals(
				XjGuoWeiCalculator.Calculate("府水", remoteIntercalarySample),
				XjGuoWeiCalculator.NoDoor,
				StringComparison.Ordinal)
			|| !XjGuoWeiCalculator.TryResolveRemoteIntercalaryDaoTu("府水", remoteIntercalarySample, out string remoteTarget)
			|| !string.Equals(remoteTarget, "合水", StringComparison.Ordinal)
			|| Math.Abs(XjGuoWeiCalculator.ResolveIntercalaryDaoHuiThreshold("府水", "合水")
				- XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.StructuredRemoteThreshold) > 0.001f)
		{
			issues.Add("果位判定：府水→合水远亲闰位门槛未正确收紧");
		}
		if (XjGuoWeiCalculator.ResolveIntercalaryDaoHuiThreshold("府水", "兑金")
			<= XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.Maximum)
		{
			issues.Add("果位判定：完全无拓扑外道仍可被普通闰位逻辑小空证");
		}
		if (!(XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.StableInheritanceThreshold
			< XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.DeriveResidualThreshold
			&& XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.DeriveResidualThreshold
			< XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.OpenIntercalaryThreshold
			&& XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.OpenIntercalaryThreshold
			< XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.StructuredRemoteThreshold
			&& XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.StructuredRemoteThreshold
			< XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.DifficultPositionThreshold
			&& XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.DifficultPositionThreshold
			< XuanJianVNext.Systems.Aptitude.XjDaoHuiPolicy.FruitPositionThreshold))
		{
			issues.Add("果位判定：位序道慧难度再次出现余闰高于正果的倒挂");
		}

		// 用户实机回归样例：府水根基混入牝水上位“往生泉”，根道必须保留府水，
		// 但果位名、权柄与注册表归属必须全部落在牝水闰位。
		XjXianJiState pinShuiIntercalarySample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖", "往生泉" },
			0,
			"InvariantSample");
		if (!XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
			null, pinShuiIntercalarySample, out string pinShuiSource, out string pinShuiManifest)
			|| !string.Equals(pinShuiSource, "府水", StringComparison.Ordinal)
			|| !string.Equals(pinShuiManifest, "牝水", StringComparison.Ordinal))
		{
			issues.Add("果位判定：府水根基修牝水神通未正确归入牝水闰位");
		}

		// 用户实机回归样例：逍金四门上位加一门本道下位“逍遥铁”，
		// 必须明确形成逍金余位，不能显示“岐路未定”。
		XjXianJiState xiaoJinResidualSample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "御金行", "敛锋芒", "无羁绊", "逍遥游", "逍遥铁" },
			0,
			"InvariantSample");
		if (!string.Equals(
			XjGuoWeiCalculator.Calculate("逍金", xiaoJinResidualSample),
			XjGuoWeiCalculator.YuWei,
			StringComparison.Ordinal))
		{
			issues.Add("果位判定：逍金四上位一下一位未正确归入逍金余位");
		}

		string[] removedLowerNames = { "天下明", "掩弊服", "天下革" };
		string[] rewrittenLowerNames = { "照昧灯", "藏晦衣", "削故锋" };
		string[] rewrittenDaoTus = { "明阳", "厥阴", "庚金" };
		for (int i = 0; i < removedLowerNames.Length; i++)
		{
			string[] lowerPool = XjXianJiCatalog.GetLowerPool(rewrittenDaoTus[i]);
			if (Array.IndexOf(lowerPool, removedLowerNames[i]) >= 0)
				issues.Add("神通池：旧下位神通仍在运行池中：" + removedLowerNames[i]);
			if (Array.IndexOf(lowerPool, rewrittenLowerNames[i]) < 0)
				issues.Add("神通池：重写下位神通未正确归池：" + rewrittenLowerNames[i]);
		}

		string[] yuanZhaoLowerPool = XjXianJiCatalog.GetLowerPool("渊照");
		string[] expectedYuanZhaoLower = { "沉月纹", "照影符", "返景痕", "涵真珠", "无波鉴" };
		for (int i = 0; i < expectedYuanZhaoLower.Length; i++)
		{
			if (Array.IndexOf(yuanZhaoLowerPool, expectedYuanZhaoLower[i]) < 0)
				issues.Add("神通池：渊照余位缺少本道下位神通：" + expectedYuanZhaoLower[i]);
		}
		XjXianJiState yuanZhaoResidualSample = new XjXianJiState(
			true, XjXianJiState.MaxCount,
			new[] { "月沉渊", "照无身", "回澜鉴", "影归真", "沉月纹" },
			0, "InvariantSample");
		if (!string.Equals(XjGuoWeiCalculator.Calculate("渊照", yuanZhaoResidualSample),
			XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			issues.Add("果位判定：渊照四上位一下一位未正确归入渊照余位");
		}

		XjXianJiState yuanZhaoTaiYinRunSample = new XjXianJiState(
			true, XjXianJiState.MaxCount,
			new[] { "月沉渊", "照无身", "回澜鉴", "影归真", "湖月秋" },
			0, "InvariantSample");
		if (!string.Equals(XjGuoWeiCalculator.Calculate("渊照", yuanZhaoTaiYinRunSample),
			XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			|| !string.Equals(XjGuoWeiCalculator.ResolveManifestDaoTu(
				"渊照", yuanZhaoTaiYinRunSample, XjGuoWeiCalculator.RunWei), "太阴", StringComparison.Ordinal))
		{
			issues.Add("果位判定：渊照混入太阴上位神通未正确归入太阴闰位");
		}

		XjXianJiState yuanZhaoInvalidLowerRunSample = new XjXianJiState(
			true, XjXianJiState.MaxCount,
			new[] { "月沉渊", "照无身", "回澜鉴", "影归真", "不胜寒" },
			0, "InvariantSample");
		if (!string.Equals(XjGuoWeiCalculator.Calculate("渊照", yuanZhaoInvalidLowerRunSample),
			XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			issues.Add("果位判定：渊照不得以太阴下位神通伪成闰位");
		}

		XjXianJiState fruitSample = new XjXianJiState(
			true,
			XjXianJiState.MaxCount,
			new[] { "归流处", "谶在兹", "广准圣", "诸合还", "妖渎河" },
			0,
			"InvariantSample");
		if (!string.Equals(
			XjGuoWeiCalculator.Calculate("合水", fruitSample),
			XjGuoWeiCalculator.ZhengWei,
			StringComparison.Ordinal))
		{
			issues.Add("果位判定：五门本道途上位神通未固定归入果位");
		}

		if (!XjLongShuSystem.IsHeShuiFruitPosition("合水", XjGuoWeiCalculator.ZhengWei)
			|| XjLongShuSystem.IsHeShuiFruitPosition("合水", XjGuoWeiCalculator.YuWei)
			|| XjLongShuSystem.IsHeShuiFruitPosition("坎水", XjGuoWeiCalculator.ZhengWei))
		{
			issues.Add("果位判定：合水果位龙属专属边界发生偏移");
		}
	}

	private static void AuditWorldEventCatalog(List<string> issues)
	{
		IReadOnlyList<XjWorldEventDefinition> definitions = XjWorldEventCatalog.ReadDefinitions();
		HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < definitions.Count; i++)
		{
			XjWorldEventDefinition definition = definitions[i];
			if (definition == null || string.IsNullOrWhiteSpace(definition.Id))
			{
				issues.Add("世界事件目录存在空定义或空ID");
				continue;
			}
			if (!ids.Add(definition.Id)) issues.Add("世界事件目录ID重复：" + definition.Id);
			if (definition.Resolver == null) issues.Add("世界事件缺少处理器：" + definition.Id);
			if (definition.MaximumActiveCount <= 0) issues.Add("世界事件并发上限无效：" + definition.Id);
		}
	}

}
