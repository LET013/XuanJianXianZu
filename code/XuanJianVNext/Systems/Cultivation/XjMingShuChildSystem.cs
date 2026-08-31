using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 命数子复用既有先天/后天命数，不建立第二套“天命值”。
/// “COS”按原著后期的因果拟身思路实现：不是姓名撞梗，也不把角色强改成原著人物，
/// 而是以世界中真实存在过的高境修士作为因果锚，逐步借其“命、性、位”的痕迹拟身。
/// 这里只提供修行/突破层面的因果收益与反噬，不篡改 actor id、真实境界、姓名或血缘。
/// </summary>
internal static class XjMingShuChildSystem
{
	internal const float MingShuChildCongenitalThreshold = 85f;
	internal const int MingShuChildMinimumAptitude = 5;
	internal const int MingShuChildDeathWardMax = 3;
	private const float DeathWardRestoredHealthRatio = 0.40f;
	private const float DeathWardInvincibleSeconds = 8f;
	private const int DeathWardMinimumHealth = 1;
	private const int ImpersonationCheckIntervalYears = 8;
	private const int ImpersonationAdvanceIntervalYears = 8;
	private const int ImpersonationStartChancePercent = 12;
	private const int ImpersonationCompleteHoldYears = 12;
	private const int MaxTemplateChecks = 96;

	internal static bool IsMingShuChild(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			&& aptitude >= MingShuChildMinimumAptitude
			&& XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital)
			&& congenital >= MingShuChildCongenitalThreshold;
	}

	internal static int GetDeathWardRemaining(Actor actor)
	{
		if (!IsMingShuChild(actor)) return 0;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuDeathWardRemaining, out int remaining))
		{
			return MingShuChildDeathWardMax;
		}
		return Math.Clamp(remaining, 0, MingShuChildDeathWardMax);
	}

	/// <summary>
	/// 命数子的三重护命只拦“本可避开的死劫”。寿尽、阴司、求道失败、脚本终局、
	/// 主动转世与清档销毁仍是合法终局，不能被有限护命次数改写。
	/// </summary>
	internal static bool TryConsumeDeathWard(Actor actor, XjDeathCause cause)
	{
		if (!IsWardableDeathCause(cause) || !IsMingShuChild(actor)) return false;
		int remaining = GetDeathWardRemaining(actor);
		if (remaining <= 0) return false;

		int next = remaining - 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuDeathWardRemaining, next);
		RecoverFromDeathWard(actor);
		RecordDeathWardEvent(actor, next);
		return true;
	}

	internal static bool TryConsumeDeathWardForDirectDestroy(Actor actor, AttackType attackType)
	{
		if (actor?.data == null || !actor.isAlive() || !IsMingShuChild(actor)) return false;
		if (HasForcedUnwardableFinality(actor)) return false;

		XjDeathCause cause;
		if (attackType == AttackType.Age) cause = XjDeathCause.NaturalOldAge;
		else if (attackType == AttackType.Starvation) cause = XjDeathCause.Starvation;
		else if (attackType == AttackType.Fire) cause = XjDeathCause.Fire;
		else cause = XjVanillaDeathGuard.IsEnvironmentalAttackType(attackType)
			? XjDeathCause.ScriptedFinality
			: XjDeathCause.Combat;
		return TryConsumeDeathWard(actor, cause);
	}

	private static bool IsWardableDeathCause(XjDeathCause cause)
	{
		return cause == XjDeathCause.Unknown
			|| cause == XjDeathCause.Combat
			|| cause == XjDeathCause.Starvation
			|| cause == XjDeathCause.Fire;
	}

	private static bool HasForcedUnwardableFinality(Actor actor)
	{
		return XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.NaturalOldAge)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.YinSi)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.BreakthroughFailure)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.ScriptedFinality)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.TechnicalRemoval)
			|| XjDeathArbitrationPipeline.IsForcedCause(actor, XjDeathCause.ShiVoluntaryReincarnation);
	}

	private static void RecoverFromDeathWard(Actor actor)
	{
		if (actor?.data == null) return;
		float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
		int current = Math.Max(DeathWardMinimumHealth, actor.data.health);
		int restored = maxHealth > 0f
			? Mathf.CeilToInt(Mathf.Max(DeathWardMinimumHealth, maxHealth * DeathWardRestoredHealthRatio))
			: current;
		try { actor.setHealth(Math.Max(current, restored)); }
		catch { actor.data.health = Math.Max(current, restored); }
		XjActorAggroBridge.ClearTargets(actor);
		try { ((BaseSimObject)actor).addStatusEffect("invincible", DeathWardInvincibleSeconds, true); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("MingShuDeathWard.AddInvincible", ex); }
		try { actor.setStatsDirty(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("MingShuDeathWard.SetStatsDirty", ex); }
	}

	private static void RecordDeathWardEvent(Actor actor, int remaining)
	{
		if (actor?.data == null) return;
		int currentYear = Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);
		string actorName = SafeName(actor);
		string remainder = remaining > 0
			? "命数护身尚余" + remaining.ToString() + "次。"
			: "三重护命至此已经用尽。";
		string body = actorName + "临死之际先天命数骤然回护，硬生生避过一场死劫。" + remainder;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Opportunity,
			"命数护身",
			body,
			4,
			isProtected: false,
			actorId: ActorId(actor),
			actorName: actorName,
			year: currentYear,
			eventType: "MingShuDeathWard",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Change);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			body,
			XjAnnouncementCategory.HighRealmInfluence,
			duration: 8f,
			color: "#D9C78A",
			delayFrames: 1);
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	/// <summary>
	/// 低频推进“因果拟身”。候选模板必须是本世界真实 Actor，且权威境界为金丹/真君/道胎之一；
	/// 只接受本道途或直接近邻道途，避免凭空借来毫无关系的身份。
	/// </summary>
	internal static bool TickCausalImpersonation(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return false;
		if (!IsMingShuChild(actor))
		{
			if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, out long staleTargetId)
				&& staleTargetId > 0L)
			{
				ClearImpersonation(actor);
			}
			return false;
		}
		long actorId = ActorId(actor);
		if (actorId <= 0L) return false;

		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, out long targetId)
			&& targetId > 0L)
		{
			return AdvanceExistingImpersonation(actor, currentYear, targetId);
		}

		int phase = XjDeterministicHash.PositiveIndex(actorId, "mingshu.causal_impersonation.phase", ImpersonationCheckIntervalYears);
		if ((currentYear + phase) % ImpersonationCheckIntervalYears != 0) return false;
		if (XjDeterministicHash.PositiveIndex(actorId + currentYear, "mingshu.causal_impersonation.start", 100)
			>= ImpersonationStartChancePercent) return false;

		if (!ResolveActualCausalTemplate(actor, currentYear, out Actor template)) return false;
		StartImpersonation(actor, template, currentYear);
		return true;
	}

	internal static float ResolveCultivationMultiplier(Actor actor)
	{
		if (!TryGetActiveImpersonationStage(actor, out int stage)) return 1f;
		return stage switch
		{
			1 => 1.04f,
			2 => 1.08f,
			3 => 1.12f,
			4 => 1.16f,
			_ => 1f
		};
	}

	internal static float ResolveBreakthroughSuccessBonus(Actor actor)
	{
		if (!TryGetActiveImpersonationStage(actor, out int stage)) return 0f;
		return stage switch
		{
			1 => 0.01f,
			2 => 0.02f,
			3 => 0.03f,
			4 => 0.04f,
			_ => 0f
		};
	}

	internal static string BuildStatusSummary(Actor actor)
	{
		if (!IsMingShuChild(actor)) return string.Empty;
		string summary = "命数子";
		int deathWardRemaining = GetDeathWardRemaining(actor);
		summary += "\n    命数护身：尚余" + deathWardRemaining.ToString() + "/" + MingShuChildDeathWardMax.ToString() + "次";
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, out long targetId)
			&& targetId > 0L)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetName, out string targetName);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetDaoTu, out string targetDaoTu);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, out int stage);
			string identity = string.IsNullOrWhiteSpace(targetName) ? "某位上修" : targetName.Trim();
			if (!string.IsNullOrWhiteSpace(targetDaoTu)) identity += "·" + targetDaoTu.Trim();
			summary += "\n    因果拟身：借" + identity + "之因·" + ResolveImpersonationStageDisplay(stage);
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuSchemeType, out string schemeType)
			&& string.Equals(schemeType, "MingYang", StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuSchemeStage, out int stage);
			string patronText = string.Empty;
			if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuSchemePatronActorId, out long patronId)
				&& patronId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron)
				&& patron?.data != null && patron.isAlive())
			{
				string patronRealm = XjRealmHelper.GetDisplayName(XjRealmHelper.GetUnifiedId(patron, XjRealmHelper.GetTraitSnapshotForRouter));
				patronText = " · 主局 " + SafeName(patron) + (string.IsNullOrWhiteSpace(patronRealm) ? string.Empty : "（" + patronRealm + "）");
			}
			summary += "\n    明阳局：" + ResolveMingYangSchemeStageDisplay(stage) + patronText;
		}
		return summary;
	}

	private static bool AdvanceExistingImpersonation(Actor actor, int currentYear, long targetId)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetDaoTu, out string targetDaoTu);
		if (!IsCausallyCompatible(actorDaoTu, targetDaoTu))
		{
			RecordImpersonationEvent(actor, currentYear, "拟因失合", "MingShuCausalImpersonationPathBreak",
				SafeName(actor) + "修行道途已与所借因果失合，先前拟出的身份痕迹自行散去。", 3, false, XjHistoryResult.Failure);
			ClearImpersonation(actor);
			return true;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, out int stage);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuImpersonationLastAdvanceYear, out int lastYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuImpersonationCompleteYear, out int completeYear);
		if (stage >= 4)
		{
			if (completeYear > 0 && currentYear - completeYear >= ImpersonationCompleteHoldYears)
			{
				RecordImpersonationEvent(actor, currentYear, "拟身余因渐散", "MingShuCausalImpersonationFaded",
					SafeName(actor) + "先前假借而来的因果身份已用尽，余痕渐渐退去，只留下真正归于自身的命数与见识。", 2, false, XjHistoryResult.Change);
				ClearImpersonation(actor);
				return true;
			}
			return false;
		}
		if (lastYear <= 0) lastYear = currentYear;
		if (currentYear - lastYear < ImpersonationAdvanceIntervalYears) return false;

		int backlashChance = stage <= 1 ? 8 : stage == 2 ? 12 : 18;
		if (XjDeterministicHash.PositiveIndex(ActorId(actor) + targetId + currentYear,
			"mingshu.causal_impersonation.backlash|" + stage.ToString(), 100) < backlashChance)
		{
			XjMingShuState.AddAcquired(actor, -3f);
			RecordImpersonationEvent(actor, currentYear, "因果反噬", "MingShuCausalImpersonationBacklash",
				SafeName(actor) + "强行贴近他人命数时被旧因反噬，拟出的身份当场崩散，后天命数损失三点。", 4, true, XjHistoryResult.Failure);
			ClearImpersonation(actor);
			return true;
		}

		if (stage <= 1)
		{
			XjMingShuState.AddAcquired(actor, 1f);
			SetImpersonationStage(actor, 2, currentYear);
			RecordImpersonationEvent(actor, currentYear, "因果拟身·合性", "MingShuCausalImpersonationStage2",
				SafeName(actor) + "沿所借因果继续揣摩其性命痕迹，开始让自身修行与那道身份相互贴合，后天命数增长一点。", 3, false, XjHistoryResult.Change);
			return true;
		}
		if (stage == 2)
		{
			XjMingShuState.AddAcquired(actor, 2f);
			AddDaoHui(actor, 1f);
			SetImpersonationStage(actor, 3, currentYear);
			RecordImpersonationEvent(actor, currentYear, "因果拟身·近真", "MingShuCausalImpersonationStage3",
				SafeName(actor) + "所借之命与自身之性愈发相合，外在因果已经能短暂混淆，后天命数增长二点，道慧增长一点。", 4, true, XjHistoryResult.Change);
			return true;
		}

		XjMingShuState.AddAcquired(actor, 4f);
		AddDaoHui(actor, 1f);
		SetImpersonationStage(actor, 4, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationCompleteYear, currentYear);
		RecordImpersonationEvent(actor, currentYear, "因果拟身·假身成", "MingShuCausalImpersonationComplete",
			SafeName(actor) + "终于以自身命数托起了所借身份的一段因果轮廓。此后数年，其修行与破境都能借这道旧因得到助力；后天命数增长四点，道慧增长一点。", 5, true, XjHistoryResult.Success);
		return true;
	}

	private static void StartImpersonation(Actor actor, Actor template, int currentYear)
	{
		long targetId = ActorId(template);
		XjActorAccessor.TryGetString(template, XjActorDataKeys.DaoTu, out string targetDaoTu);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, targetId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetName, SafeName(template));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetDaoTu, (targetDaoTu ?? string.Empty).Trim());
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationStartedYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationLastAdvanceYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationCompleteYear, 0);
		string templateRealm = XjRealmHelper.GetUnifiedId(template, XjRealmHelper.GetTraitSnapshotForRouter);
		string realmDisplay = XjRealmHelper.GetDisplayName(templateRealm);
		RecordImpersonationEvent(actor, currentYear, "假借因果·立影", "MingShuCausalImpersonationStarted",
			SafeName(actor) + "以命数为引，牵住了真实存在的" + SafeName(template)
			+ "（" + realmDisplay + "）身上一缕因果痕迹，开始尝试借其命、性与位拟出一重身份。", 4, true, XjHistoryResult.Change,
			relatedActorId: targetId, relatedActorName: SafeName(template));
	}

	private static bool ResolveActualCausalTemplate(Actor actor, int currentYear, out Actor selected)
	{
		selected = null;
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids == null || ids.Count == 0) return false;
		long actorId = ActorId(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
		if (string.IsNullOrWhiteSpace(actorDaoTu)) return false;
		int start = XjDeterministicHash.PositiveIndex(actorId + currentYear, "mingshu.causal_impersonation.template", ids.Count);
		int checks = Math.Min(ids.Count, MaxTemplateChecks);
		int bestRelation = -1;
		int bestRealm = -1;
		float bestDaoHui = float.MinValue;
		long bestId = long.MaxValue;

		for (int offset = 0; offset < checks; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == actorId
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor candidate)
				|| candidate?.data == null || !candidate.isAlive()
				|| !IsActualHighRealmTemplate(candidate)) continue;
			XjActorAccessor.TryGetString(candidate, XjActorDataKeys.DaoTu, out string candidateDaoTu);
			int relationScore = ResolveRelationScore(actorDaoTu, candidateDaoTu);
			if (relationScore <= 0) continue;
			int realmScore = ResolveTemplateRealmScore(candidate);
			XjActorAccessor.TryGetFloat(candidate, XjActorDataKeys.HuiGuang, out float daoHui);
			if (selected == null || relationScore > bestRelation
				|| relationScore == bestRelation && realmScore > bestRealm
				|| relationScore == bestRelation && realmScore == bestRealm && daoHui > bestDaoHui
				|| relationScore == bestRelation && realmScore == bestRealm && Math.Abs(daoHui - bestDaoHui) < 0.001f && candidateId < bestId)
			{
				selected = candidate;
				bestRelation = relationScore;
				bestRealm = realmScore;
				bestDaoHui = daoHui;
				bestId = candidateId;
			}
		}
		return selected != null;
	}

	private static bool IsActualHighRealmTemplate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static int ResolveTemplateRealmScore(Actor actor)
	{
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 2;
		return 0;
	}

	private static bool IsCausallyCompatible(string sourceDaoTu, string targetDaoTu)
	{
		return ResolveRelationScore(sourceDaoTu, targetDaoTu) > 0;
	}

	private static int ResolveRelationScore(string sourceDaoTu, string targetDaoTu)
	{
		string source = XjDaoTuRelationCatalog.Normalize(sourceDaoTu);
		string target = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
		if (source.Length == 0 || target.Length == 0) return 0;
		if (string.Equals(source, target, StringComparison.Ordinal)) return 4;
		return XjDaoTuRelationCatalog.Resolve(source, target) == XjDaoTuRelationKind.DirectAdjacent ? 3 : 0;
	}

	private static bool TryGetActiveImpersonationStage(Actor actor, out int stage)
	{
		stage = 0;
		if (actor?.data == null || !IsMingShuChild(actor)
			|| !XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, out long targetId)
			|| targetId <= 0L
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, out stage)
			|| stage <= 0) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actorDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetDaoTu, out string targetDaoTu);
		return IsCausallyCompatible(actorDaoTu, targetDaoTu);
	}

	private static void SetImpersonationStage(Actor actor, int stage, int currentYear)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, Math.Clamp(stage, 0, 4));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationLastAdvanceYear, currentYear);
	}

	private static void ClearImpersonation(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjMingShuImpersonationTargetActorId, 0L);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjMingShuImpersonationTargetDaoTu, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationStage, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationLastAdvanceYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjMingShuImpersonationCompleteYear, 0);
	}

	private static string ResolveImpersonationStageDisplay(int stage)
	{
		return stage switch
		{
			1 => "立影",
			2 => "合性",
			3 => "近真",
			4 => "假身已成",
			_ => "未成"
		};
	}

	private static string ResolveMingYangSchemeStageDisplay(int stage)
	{
		return stage switch
		{
			1 => "识命",
			2 => "引势",
			3 => "推局",
			_ => "待收束"
		};
	}

	private static void AddDaoHui(Actor actor, float delta)
	{
		if (actor?.data == null || delta <= 0f) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang,
			XjDaoHuiPolicy.Add(huiGuang, delta, XjDaoHuiPolicy.RareGrowthCeiling));
	}

	private static void RecordImpersonationEvent(
		Actor actor,
		int currentYear,
		string title,
		string eventType,
		string body,
		int importance,
		bool announce,
		string result,
		long relatedActorId = 0L,
		string relatedActorName = "")
	{
		long actorId = ActorId(actor);
		string actorName = SafeName(actor);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Opportunity,
			title,
			body,
			importance,
			isProtected: importance >= 5,
			actorId: actorId,
			actorName: actorName,
			year: currentYear,
			eventType: eventType,
			relatedActorId: relatedActorId,
			relatedActorName: relatedActorName,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: result);
		if (announce)
		{
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				body,
				XjAnnouncementCategory.HighRealmInfluence,
				duration: importance >= 5 ? 11f : 8f,
				color: importance >= 5 ? "#E7D39B" : "#C5B78A",
				delayFrames: 1);
		}
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	private static long ActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string SafeName(Actor actor)
	{
		string name = actor?.getName();
		return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
	}
}
