using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjRenDanStages
{
	internal const string Prepared = "Prepared";
	internal const string AwaitingDeath = "AwaitingDeath";
	internal const string Resolved = "Resolved";
	internal const string Lost = "Lost";
	internal const string Tainted = "Tainted";
	internal const string Failed = "Failed";
}

internal static class XjRenDanOutcomes
{
	internal const string Success = "Success";
	internal const string Lost = "Lost";
	internal const string Tainted = "Tainted";
}

internal static partial class XjRenDan
{
	private const int RenDanPlanTimeoutYears = 50;
	private const int SmallFamilyCultivatorLimit = 8;
	private const int SmallFamilyAliveLimit = 60;
	private const int FallbackFamilyCultivatorLimit = 24;
	private const int FallbackFamilyAliveLimit = 180;
	private const float PreparationZhenYuanAid = 180f;
	private const float PreparationHuiGuangAid = 3f;
	private const float TaintedBacklashZhenYuan = 800f;

	/// <summary>
	/// 在第4/第5仙基尝试中，紫府从其他家族中暗定一名同道途筑基作为人丹。
	/// 优先选择无高境庇护的小族；若本局没有这种候选，再放宽到无金丹的普通家族。
	/// 此方法只在上修本人的低频仙基尝试中执行，不增加年度全图扫描。
	/// </summary>
	private static bool TryPrepareRenDanPlan(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(snapshot.DaoTu) || IsReincarnation(actor)) return false;

		long actorId = GetActorId(actor);
		int targetCount = state.Count + 1;
		if (actorId <= 0L
			|| targetCount < XjRenDanRules.ZiFuRenDanMinimumTargetCount
			|| targetCount > XjXianJiState.MaxCount
			|| HasRenDanShenTongAcquired(actor))
		{
			return false;
		}

		if (entriesByActorId.TryGetValue(actorId, out XjRenDanState existing) && existing.Found)
		{
			// 一名紫府一旦真正形成一份人丹事件记录，就不再生成第二份。
			// 第4/第5门各自50%只针对“尚未形成计划”的窗口判定。
			return string.Equals(existing.Stage, XjRenDanStages.Prepared, StringComparison.Ordinal)
				|| string.Equals(existing.Stage, XjRenDanStages.AwaitingDeath, StringComparison.Ordinal);
		}

		// 第4门、第5门分别进行一次独立50%判定。盐中包含 targetCount，
		// 因而第4门未触发不会锁死第5门；但一旦形成事件记录，上面的唯一计划约束立即生效。
		int chanceThreshold = (int)Math.Round(XjRenDanRules.ZiFuRenDanChance * 100f);
		string rollSalt = snapshot.DaoTu + "|rendan_window_roll|" + targetCount;
		if (XjDeterministicHash.PositiveIndex(actorId, rollSalt, 100) >= chanceThreshold)
		{
			return false;
		}

		if (!TryResolveRenDanRoute(actor, snapshot.DaoTu, state, targetCount, string.Empty, out string plannedXianJi, out _))
		{
			return false;
		}

		if (!TryFindPreparationCandidate(actor, snapshot.DaoTu, currentYear, targetCount, out Actor victim, out long victimFamilyId))
		{
			return false;
		}

		string outcome = ResolvePlanOutcome(actorId, GetActorId(victim), currentYear, targetCount);
		long rivalActorId = 0L;
		string rivalActorName = string.Empty;
		if (!string.Equals(outcome, XjRenDanOutcomes.Success, StringComparison.Ordinal))
		{
			if (TryResolveUpperRival(actor, snapshot.DaoTu, currentYear, out Actor rival))
			{
				rivalActorId = GetActorId(rival);
				rivalActorName = rival?.getName() ?? string.Empty;
			}
			else
			{
				// 没有真实可解析的上修参与，就不凭空生成“被截”或“掺假”。
				outcome = XjRenDanOutcomes.Success;
			}
		}

		ApplyPreparationAid(victim);
		long victimActorId = GetActorId(victim);
		string actorName = actor.getName() ?? string.Empty;
		string victimName = victim.getName() ?? string.Empty;
		string source = "紫府第" + targetCount + "神通：" + plannedXianJi;
		entriesByActorId[actorId] = new XjRenDanState(
			true,
			actorId,
			actorName,
			currentYear,
			source,
			targetCount,
			victimActorId,
			victimName,
			snapshot.DaoTu,
			XjRenDanRules.FormatRuleSummary(),
			false,
			0,
			XjRenDanStages.Prepared,
			outcome,
			rivalActorId,
			rivalActorName);

		RecordPlanPreparedHistory(actor, victim, victimFamilyId, currentYear, plannedXianJi);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// 只由已经存在人丹计划的紫府本人在年度仙基流程中检查一次目标，不轮询天下角色。
	/// 返回 true 表示本年仙基流程被该计划占用，应停止普通仙基结算。
	/// </summary>
	internal static bool TryAdvancePreparedPlan(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !entriesByActorId.TryGetValue(actorId, out XjRenDanState plan)
			|| !plan.Found)
		{
			return false;
		}
		if (TryResolveRenDanFamilyId(actorId, out long sourceFamilyId)
			&& TryResolveRenDanFamilyId(plan.VictimActorId, out long victimFamilyId)
			&& sourceFamilyId > 0L && sourceFamilyId == victimFamilyId)
		{
			CompletePlanWithoutDeath(plan, currentYear, "所选之人与上修同出一族，原定人丹之谋当即作废，族人不受牵连。", XjRenDanStages.Failed);
			return false;
		}
		if (string.Equals(plan.Stage, XjRenDanStages.AwaitingDeath, StringComparison.Ordinal))
		{
			// 死亡队列尚未完成时不允许普通仙基流程从旁路继续推进。
			return true;
		}
		if (!string.Equals(plan.Stage, XjRenDanStages.Prepared, StringComparison.Ordinal))
		{
			return false;
		}

		if (state.Count >= plan.ShenTongCount)
		{
			CompletePlanWithoutDeath(plan, currentYear, "上修已由别途补足神通，原定人丹之谋遂废。", XjRenDanStages.Failed);
			return false;
		}

		if (currentYear > 0 && plan.Year > 0 && currentYear - plan.Year > RenDanPlanTimeoutYears)
		{
			CompletePlanWithoutDeath(plan, currentYear, plan.VictimActorName + "所系人丹计划历五十年仍未完成，暗中布置就此作废。", XjRenDanStages.Lost);
			return false;
		}

		if (!XjScheduler.ResolveActor(plan.VictimActorId, out Actor victim)
			|| victim?.data == null
			|| !((NanoObject)victim).isAlive())
		{
			CompletePlanWithoutDeath(plan, currentYear, plan.VictimActorName + "未及筑基便已身死，预定人丹由此落空。", XjRenDanStages.Lost);
			return false;
		}

		XjActorCultivationSnapshot victimSnapshot = XjActorCultivationSnapshotBuilder.Build(victim);
		if (string.Equals(victimSnapshot.RealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(victimSnapshot.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			// 目标尚在成长；上修的下一门神通仍被该计划占住。
			return true;
		}

		if (!string.Equals(victimSnapshot.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			CompletePlanWithoutDeath(plan, currentYear, plan.VictimActorName + "已越过原定层次或改换道途，旧有人丹布置无法再用。", XjRenDanStages.Lost);
			return false;
		}

		if (!string.Equals(victimSnapshot.DaoTu, plan.VictimDaoTu, StringComparison.Ordinal))
		{
			CompletePlanWithoutDeath(plan, currentYear, plan.VictimActorName + "筑基后道途与预定不合，人丹之谋未能相合。", XjRenDanStages.Tainted);
			return false;
		}

		string outcome = string.IsNullOrWhiteSpace(plan.Outcome) ? XjRenDanOutcomes.Success : plan.Outcome;
		if (string.Equals(outcome, XjRenDanOutcomes.Success, StringComparison.Ordinal))
		{
			string gainedXianJi = ResolveGainedXianJi(plan.Source);
			if (!TryResolveRenDanRoute(actor, snapshot.DaoTu, state, plan.ShenTongCount, gainedXianJi, out string verifiedXianJi, out string gongFaName)
				|| !string.Equals(verifiedXianJi, gainedXianJi, StringComparison.Ordinal))
			{
				CompletePlanWithoutDeath(plan, currentYear, "续途之时缺少相合功法，原定人丹未能入法。", XjRenDanStages.Failed);
				return false;
			}

			if (!XjXianJiAccessor.Add(actor, gainedXianJi, currentYear, gongFaName, "人丹续途"))
			{
				CompletePlanWithoutDeath(plan, currentYear, "续途之时仙基位已被占据，原定人丹未能入法。", XjRenDanStages.Failed);
				return false;
			}
			XjActorStateWriteGateway.SetExternalInt(
				actor,
				XjRenDanRules.RenDanShenTongTagKey,
				1,
				XuanJianVNext.Core.XjActorStateDomain.Progression | XuanJianVNext.Core.XjActorStateDomain.HighRealm);
			XjFaBaoAcquisition.TryGrantZiFuLingBaoOnXianJi(actor, snapshot, plan.ShenTongCount, currentYear);
		}
		else if (string.Equals(outcome, XjRenDanOutcomes.Tainted, StringComparison.Ordinal))
		{
			ApplyTaintedBacklash(actor);
		}

		long deathSourceActorId = string.Equals(outcome, XjRenDanOutcomes.Lost, StringComparison.Ordinal)
			&& plan.RivalActorId > 0L
			? plan.RivalActorId
			: actorId;
		if (!TryMarkVictimDeathPending(victim, deathSourceActorId, currentYear))
		{
			CompletePlanWithoutDeath(plan, currentYear, plan.VictimActorName + "已被其他因果先行卷走，人丹计划未能结算。", XjRenDanStages.Lost);
			return false;
		}

		entriesByActorId[actorId] = CopyState(
			plan,
			stage: XjRenDanStages.AwaitingDeath,
			deathFinalized: false,
			deathFinalizedYear: 0);
		XjWorldArchiveSystem.MarkChanged();
		XjRenDanDeathLane.EnqueueActor(victim);
		return true;
	}

	private static bool HasId(string[] ids, string id)
	{
		for (int i = 0; ids != null && i < ids.Length; i++)
		{
			if (string.Equals(ids[i], id, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}
	private static bool TryResolveRenDanRoute(
		Actor actor,
		string daoTu,
		in XjXianJiState state,
		int ordinal,
		string requiredXianJi,
		out string id,
		out string gongFaName)
	{
		id = string.Empty;
		gongFaName = string.Empty;
		if (actor?.data == null || ordinal <= 1 || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string normalizedRequired = (requiredXianJi ?? string.Empty).Trim();
		IReadOnlyList<XjActorGongFaCollection.Record> records = XjActorGongFaCollection.ReadRecords(actor);
		int maximumAllowedGrade = XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor);
		bool zhengWeiManifested = XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(daoTu);
		bool allowUpper = !XjLongShuSystem.IsLongShu(actor);

		// 若计划已锁定仙基，只验证这门仙基仍可进入，并尽量复用现有映射功法。
		// 不允许因为后来多了一本功法就把既有人丹计划改成另一门神通。
		if (normalizedRequired.Length > 0)
		{
			if (XjXianGuoSystem.IsDiMingYang(actor))
			{
				XjXianJiPoolKind lockedKind = XjXianJiCatalog.GetPoolKind(daoTu, normalizedRequired);
				if (lockedKind != XjXianJiPoolKind.Native && lockedKind != XjXianJiPoolKind.Lower) return false;
			}
			if (HasId(state.Ids, normalizedRequired)
				|| !XjXianJiCatalog.IsAvailableForProgression(
					daoTu, ordinal, state.Ids, zhengWeiManifested, allowUpper, normalizedRequired))
			{
				return false;
			}
			for (int i = 0; i < records.Count; i++)
			{
				XjActorGongFaCollection.Record record = records[i];
				if (record.Grade < 5 || record.Grade > maximumAllowedGrade
					|| !string.Equals((record.DaoTu ?? string.Empty).Trim(), daoTu.Trim(), StringComparison.Ordinal)
					|| !string.Equals((record.MappedXianJi ?? string.Empty).Trim(), normalizedRequired, StringComparison.Ordinal)) continue;
				gongFaName = record.Name ?? string.Empty;
				break;
			}
			id = normalizedRequired;
			return true;
		}

		// 兼容旧逻辑：已有合格的五品映射功法时仍优先沿该功法续途。
		for (int i = 0; i < records.Count; i++)
		{
			XjActorGongFaCollection.Record record = records[i];
			string mapped = (record.MappedXianJi ?? string.Empty).Trim();
			if (record.Grade < 5
				|| record.Grade > maximumAllowedGrade
				|| !string.Equals((record.DaoTu ?? string.Empty).Trim(), daoTu.Trim(), StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(mapped)
				|| HasId(state.Ids, mapped)
				|| !XjXianJiCatalog.IsAvailableForProgression(
					daoTu, ordinal, state.Ids, zhengWeiManifested, allowUpper, mapped))
			{
				continue;
			}
			if (XjXianGuoSystem.IsDiMingYang(actor))
			{
				XjXianJiPoolKind mappedKind = XjXianJiCatalog.GetPoolKind(daoTu, mapped);
				if (mappedKind != XjXianJiPoolKind.Native && mappedKind != XjXianJiPoolKind.Lower) continue;
			}
			id = mapped;
			gongFaName = record.Name ?? string.Empty;
			return true;
		}

		// RC11.5：50%命中后不再要求“先拥有一门尚未获得神通的五品映射功法”。
		// 与普通仙基流程保持同一依赖方向：先从当前可用仙基池确定神通；真正续途
		// 成功时由 XjXianJiAccessor.Add 负责建立/补齐对应功法映射。
		long actorId = GetActorId(actor);
		long seed = actorId > 0L ? actorId + ordinal * 7919L : ordinal * 7919L;
		string picked;
		bool pickedRoute = XjXianGuoSystem.IsDiMingYang(actor)
			? XjZiFuProgression.TryPickImperialNonIntercalaryShenTong(daoTu, state, seed, out picked)
			: XjXianJiCatalog.TryPickForProgression(
				daoTu, ordinal, seed, state.Ids, zhengWeiManifested, allowUpper, out picked);
		if (!pickedRoute || string.IsNullOrWhiteSpace(picked))
		{
			return false;
		}
		id = picked.Trim();
		gongFaName = string.Empty;
		return true;
	}
	private static bool TryFindPreparationCandidate(
		Actor source,
		string daoTu,
		int currentYear,
		int targetCount,
		out Actor victim,
		out long victimFamilyId)
	{
		victim = null;
		victimFamilyId = 0L;
		long sourceActorId = GetActorId(source);
		if (!TryResolveRenDanFamilyId(sourceActorId, out long sourceFamilyId) || sourceFamilyId <= 0L)
		{
			return false;
		}
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids.Count == 0) return false;

		Actor fallbackVictim = null;
		long fallbackFamilyId = 0L;
		int start = XjDeterministicHash.PositiveIndex(sourceActorId + currentYear + targetCount, daoTu + "|rendan_lower", ids.Count);
		for (int offset = 0; offset < ids.Count; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == sourceActorId || IsVictimReserved(candidateId)) continue;
			if (!XjScheduler.ResolveActor(candidateId, out Actor candidate)
				|| candidate?.data == null
				|| !((NanoObject)candidate).isAlive()
				|| IsReincarnation(candidate)
				|| IsClosedCultivation(candidate))
			{
				continue;
			}

			XjActorCultivationSnapshot candidateSnapshot = XjActorCultivationSnapshotBuilder.Build(candidate);
			if (!string.Equals(candidateSnapshot.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
				|| !string.Equals(candidateSnapshot.DaoTu, daoTu, StringComparison.Ordinal))
			{
				continue;
			}

			if (!TryResolveRenDanFamilyId(candidateId, out long familyId)
				|| familyId <= 0L
				|| familyId == sourceFamilyId
				|| !XjFamilyMemberLedger.TryGetAggregate(familyId, out XjFamilyLedgerAggregate aggregate)
				|| aggregate.JinDanCount > 0)
			{
				continue;
			}

			// 第一层仍保持原设定：优先没有紫府/金丹庇护的小族。
			if (aggregate.ZiFuCount <= 0
				&& aggregate.CultivatorCount <= SmallFamilyCultivatorLimit
				&& aggregate.AliveCount <= SmallFamilyAliveLimit)
			{
				victim = candidate;
				victimFamilyId = familyId;
				return true;
			}

			// 第二层只在完全找不到小族时使用。允许有紫府庇护，给“护族/截丹”事件
			// 留出现实空间，但仍拒绝已有金丹的大族，避免上修无脑吃同层豪门。
			if (fallbackVictim == null
				&& aggregate.CultivatorCount <= FallbackFamilyCultivatorLimit
				&& aggregate.AliveCount <= FallbackFamilyAliveLimit)
			{
				fallbackVictim = candidate;
				fallbackFamilyId = familyId;
			}
		}

		if (fallbackVictim != null)
		{
			victim = fallbackVictim;
			victimFamilyId = fallbackFamilyId;
			return true;
		}
		return false;
	}
	private static bool TryResolveUpperRival(Actor source, string daoTu, int currentYear, out Actor rival)
	{
		rival = null;
		long sourceId = GetActorId(source);
		if (!TryResolveRenDanFamilyId(sourceId, out long sourceFamilyId) || sourceFamilyId <= 0L)
		{
			return false;
		}
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids.Count == 0) return false;
		int start = XjDeterministicHash.PositiveIndex(sourceId + currentYear, daoTu + "|rendan_rival", ids.Count);
		for (int offset = 0; offset < ids.Count; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == sourceId) continue;
			if (!XjScheduler.ResolveActor(candidateId, out Actor candidate)
				|| candidate?.data == null
				|| !((NanoObject)candidate).isAlive()) continue;
			XjActorCultivationSnapshot candidateSnapshot = XjActorCultivationSnapshotBuilder.Build(candidate);
			if ((!string.Equals(candidateSnapshot.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
					&& !string.Equals(candidateSnapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
				|| !string.Equals(candidateSnapshot.DaoTu, daoTu, StringComparison.Ordinal)) continue;
			if (!TryResolveRenDanFamilyId(candidateId, out long familyId) || familyId <= 0L || familyId == sourceFamilyId) continue;
			rival = candidate;
			return true;
		}
		return false;
	}

	private static bool TryResolveRenDanFamilyId(long actorId, out long familyId)
	{
		if (actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId)
			&& familyId > 0L)
		{
			return true;
		}
		if (actorId > 0L
			&& XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found && entry.FamilyStableId > 0L)
		{
			familyId = entry.FamilyStableId;
			return true;
		}
		familyId = 0L;
		return false;
	}

	private static bool IsVictimReserved(long actorId)
	{
		foreach (XjRenDanState state in entriesByActorId.Values)
		{
			if (!state.Found || state.VictimActorId != actorId) continue;
			if (string.Equals(state.Stage, XjRenDanStages.Prepared, StringComparison.Ordinal)
				|| string.Equals(state.Stage, XjRenDanStages.AwaitingDeath, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static string ResolvePlanOutcome(long sourceActorId, long victimActorId, int currentYear, int targetCount)
	{
		int roll = XjDeterministicHash.PositiveIndex(sourceActorId + victimActorId + currentYear + targetCount, "rendan_outcome", 100);
		if (roll < 70) return XjRenDanOutcomes.Success;
		if (roll < 85) return XjRenDanOutcomes.Lost;
		return XjRenDanOutcomes.Tainted;
	}

	private static void ApplyPreparationAid(Actor victim)
	{
		if (victim?.data == null) return;
		XjActorCultivationSnapshot before = XjActorCultivationSnapshotBuilder.Build(victim);
		float proposedZhenYuan = XjCultivationGrowthRules.ApplyRealmCap(before, before.ZhenYuan + PreparationZhenYuanAid);
		float nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(victim, in before, proposedZhenYuan);
		XjActorAccessor.SetFloat(victim, XjActorDataKeys.ZhenYuan, nextZhenYuan);
		XjActorAccessor.TryGetFloat(victim, XjActorDataKeys.HuiGuang, out float rawHuiGuang);
		XjActorAccessor.SetFloat(victim, XjActorDataKeys.HuiGuang,
			XjDaoHuiPolicy.Add(rawHuiGuang, PreparationHuiGuangAid, XjDaoHuiPolicy.OrdinaryGrowthCeiling));
	}

	private static void ApplyTaintedBacklash(Actor source)
	{
		if (source?.data == null) return;
		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjActorAccessor.SetFloat(source, XjActorDataKeys.ZhenYuan, (float)Math.Floor(Math.Max(0f, zhenYuan - TaintedBacklashZhenYuan)));
		XjActorStateWriteGateway.SetExternalInt(
			source,
			XjRenDanRules.RenDanPollutionTagKey,
			1,
			XjActorStateDomain.Progression | XjActorStateDomain.HighRealm);
	}

	private static void RecordPlanPreparedHistory(Actor source, Actor victim, long victimFamilyId, int year, string plannedXianJi)
	{
		long sourceId = GetActorId(source);
		long victimId = GetActorId(victim);
		TryResolveRenDanFamilyId(sourceId, out long sourceFamilyId);
		long sourceSectId = XjSectOwnership.ResolveSectId(source);
		string sourceName = source?.getName() ?? string.Empty;
		string victimName = victim?.getName() ?? string.Empty;
		TryGetActorLocation(victim, out int x, out int y);
		string body = victimName + "得一桩来历不明的修行扶持，真元与道慧有所增长；实则" + sourceName
			+ "已将这名同道途筑基暗定为与" + plannedXianJi + "相合的人丹之材。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Cultivation,
			"筑基下修受人扶持",
			body,
			3,
			false,
			victimId,
			victimName,
			sourceSectId,
			victimFamilyId,
			victim?.city?.data?.id ?? 0L,
			year,
			x,
			y,
			XjEventIconCatalog.RenDan,
			"RenDanMarked",
			sourceId,
			sourceName,
			sourceFamilyId,
			0L,
			(int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			XjHistoryResult.Change,
			0L,
			false);
	}

	private static void CompletePlanWithoutDeath(XjRenDanState plan, int currentYear, string body, string stage)
	{
		bool mismatch = string.Equals(stage, XjRenDanStages.Tainted, StringComparison.Ordinal);
		RecordOutcomeHistory(
			plan,
			currentYear,
			plan.VictimActorName,
			mismatch ? "RenDanMismatch" : "RenDanPlanFailed",
			mismatch ? "人丹不合" : "人丹之谋中止",
			body,
			3,
			false);
		entriesByActorId[plan.ActorId] = CopyState(
			plan,
			stage: stage,
			deathFinalized: true,
			deathFinalizedYear: currentYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static bool FinalizePreparedVictimDeath(long victimActorId, long sourceActorId, int currentYear, string victimActorName)
	{
		if (victimActorId <= 0L || !TryFindRecordForVictim(victimActorId, sourceActorId, out XjRenDanState state)) return false;
		if (state.DeathFinalized) return true;

		int safeYear = currentYear > 0 ? currentYear : state.Year;
		string targetName = string.IsNullOrWhiteSpace(victimActorName) ? state.VictimActorName : victimActorName.Trim();
		string outcome = string.IsNullOrWhiteSpace(state.Outcome) ? XjRenDanOutcomes.Success : state.Outcome;
		string gainedXianJi = ResolveGainedXianJi(state.Source);
		string eventType;
		string title;
		string body;
		string finalStage;

		if (string.Equals(outcome, XjRenDanOutcomes.Lost, StringComparison.Ordinal))
		{
			eventType = "RenDanLost";
			title = "预定人丹已失";
			string rival = string.IsNullOrWhiteSpace(state.RivalActorName) ? "另一位上修" : state.RivalActorName.Trim();
			body = state.ActorName + "暗中培养的" + targetName + "已成筑基，却在收丹之前被" + rival + "截走，原定续途之谋落空。";
			finalStage = XjRenDanStages.Lost;
		}
		else if (string.Equals(outcome, XjRenDanOutcomes.Tainted, StringComparison.Ordinal))
		{
			eventType = "RenDanTainted";
			title = "人丹掺假";
			string rival = string.IsNullOrWhiteSpace(state.RivalActorName) ? "不明之人" : state.RivalActorName.Trim();
			body = state.ActorName + "收取" + targetName + "施展续途妙法，却发现人丹早被" + rival + "动过手脚；神通未成，反损真元。";
			finalStage = XjRenDanStages.Tainted;
		}
		else
		{
			eventType = "RenDanResolved";
			title = "人丹血劫与续途妙法";
			body = state.ActorName + "待" + targetName + "筑基后将其炼成人丹，施展续途妙法，残神化入" + gainedXianJi + "。";
			finalStage = XjRenDanStages.Resolved;
		}

		RecordOutcomeHistory(state, safeYear, targetName, eventType, title, body, 4, true);
		RecordOutcomeVendetta(state, targetName, safeYear, gainedXianJi, outcome);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			body,
			XjAnnouncementCategory.HighRealmInfluence,
			false,
			"top",
			10f,
			string.Equals(outcome, XjRenDanOutcomes.Success, StringComparison.Ordinal) ? "#B7A7FF" : "#FF9E80");

		entriesByActorId[state.ActorId] = CopyState(
			state,
			stage: finalStage,
			deathFinalized: true,
			deathFinalizedYear: safeYear);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	private static void RecordOutcomeHistory(
		XjRenDanState state,
		int year,
		string victimName,
		string eventType,
		string title,
		string body,
		int importance,
		bool includeWorld)
	{
		TryResolveRenDanFamilyId(state.ActorId, out long sourceFamilyId);
		TryResolveRenDanFamilyId(state.VictimActorId, out long victimFamilyId);
		long sourceSectId = 0L;
		long cityId = 0L;
		int x = int.MinValue;
		int y = int.MinValue;
		if (XjScheduler.ResolveActor(state.ActorId, out Actor source)) sourceSectId = XjSectOwnership.ResolveSectId(source);
		if (XjScheduler.ResolveActor(state.VictimActorId, out Actor victim))
		{
			cityId = victim?.city?.data?.id ?? 0L;
			TryGetActorLocation(victim, out x, out y);
		}
		XjHistoryVisibility visibility = XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate;
		if (includeWorld) visibility |= XjHistoryVisibility.World;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.LifeAndDeath,
			title,
			body,
			importance,
			importance >= 4,
			state.VictimActorId,
			victimName,
			sourceSectId,
			victimFamilyId,
			cityId,
			year,
			x,
			y,
			XjEventIconCatalog.RenDan,
			eventType,
			state.ActorId,
			state.ActorName,
			sourceFamilyId,
			0L,
			(int)visibility,
			string.Equals(eventType, "RenDanResolved", StringComparison.Ordinal) ? XjHistoryResult.Success : XjHistoryResult.Failure);
	}

	private static void RecordOutcomeVendetta(XjRenDanState state, string victimName, int year, string gainedXianJi, string outcome)
	{
		TryResolveRenDanFamilyId(state.VictimActorId, out long victimFamilyId);
		if (victimFamilyId <= 0L) return;

		long offenderActorId = state.ActorId;
		string offenderName = state.ActorName;
		if (string.Equals(outcome, XjRenDanOutcomes.Lost, StringComparison.Ordinal) && state.RivalActorId > 0L)
		{
			offenderActorId = state.RivalActorId;
			offenderName = state.RivalActorName;
		}
		TryResolveRenDanFamilyId(offenderActorId, out long offenderFamilyId);
		string detail = string.Equals(outcome, XjRenDanOutcomes.Tainted, StringComparison.Ordinal)
			? offenderName + "以" + victimName + "试行续途妙法，虽因人丹掺假而未得神通，" + victimName + "仍因此身死。族中记此血仇。"
			: string.Equals(outcome, XjRenDanOutcomes.Lost, StringComparison.Ordinal)
				? offenderName + "截走" + victimName + "作为人丹，原先预定此人的上修亦未能收丹。族中记此血仇。"
				: state.ActorName + "暗中扶持" + victimName + "修至筑基，继而将其炼作人丹，施展续途妙法，残神化入" + gainedXianJi + "。族中记此血仇。";
		XjFamilyVendettaRegistry.RecordRenDanVendetta(
			victimFamilyId,
			state.VictimActorId,
			victimName,
			offenderFamilyId,
			offenderActorId,
			offenderName,
			gainedXianJi,
			year,
			detail);
	}

	private static XjRenDanState CopyState(
		in XjRenDanState state,
		string stage,
		bool deathFinalized,
		int deathFinalizedYear)
	{
		return new XjRenDanState(
			state.Found,
			state.ActorId,
			state.ActorName,
			state.Year,
			state.Source,
			state.ShenTongCount,
			state.VictimActorId,
			state.VictimActorName,
			state.VictimDaoTu,
			state.Summary,
			deathFinalized,
			deathFinalizedYear,
			stage,
			state.Outcome,
			state.RivalActorId,
			state.RivalActorName);
	}

	private static bool TryGetActorLocation(Actor actor, out int x, out int y)
	{
		x = int.MinValue;
		y = int.MinValue;
		if (actor == null) return false;
		try
		{
			x = (int)Math.Round(actor.current_position.x);
			y = (int)Math.Round(actor.current_position.y);
			return x >= 0 && y >= 0;
		}
		catch (System.Exception xjCaught686_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjRenDan.Plans.cs:686", xjCaught686_1);
			
			x = int.MinValue;
			y = int.MinValue;
			return false;
		}
	}
}




