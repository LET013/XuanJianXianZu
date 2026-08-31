using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.Death;

internal enum XjDeathCause : byte
{
	Unknown = 0,
	NaturalOldAge = 1,
	Combat = 2,
	Starvation = 3,
	Fire = 4,
	YinSi = 5,
	BreakthroughFailure = 6,
	ScriptedFinality = 7,
	TechnicalRemoval = 8,
	ShiVoluntaryReincarnation = 9
}

internal readonly struct XjDeathEntryState
{
	internal XjDeathEntryState(XjDeathSnapshot snapshot, XjDeathCause cause, string guZunArchiveId)
	{
		Snapshot = snapshot;
		Cause = cause;
		GuZunArchiveId = guZunArchiveId ?? string.Empty;
	}

	internal XjDeathSnapshot Snapshot { get; }
	internal XjDeathCause Cause { get; }
	internal string GuZunArchiveId { get; }
	internal bool HasSnapshot => Snapshot.ActorId > 0L;
}

/// <summary>
/// 所有真正死亡统一通过此管线仲裁。它不复制死亡后的家族、果位、阴司等结算，
/// 只负责统一死因、免死/环境拦截、死亡前快照与故尊归档的顺序。
/// </summary>
internal static class XjDeathArbitrationPipeline
{
	private static readonly Dictionary<long, XjDeathCause> ForcedCauses = new Dictionary<long, XjDeathCause>();
	private static readonly object Sync = new object();

	internal static bool TryBeginDeath(Actor actor, AttackType attackType, out XjDeathEntryState state)
	{
		state = new XjDeathEntryState(XjDeathSnapshot.Empty, XjDeathCause.Unknown, string.Empty);
		if (actor?.data == null)
		{
			return true;
		}

		XjDeathCause cause = ResolveCause(actor, attackType);
		long actorId = GetActorId(actor);
		bool irreversibleYaoXieYinSi = cause == XjDeathCause.YinSi
			&& XjTrueDamageSystem.HasIrreversibleYinSiClaimFast(actor);
		// AVBS replacement/morph traits are implemented as action_death handlers. Let ordinary
		// deaths reach AVBS first；但金性妖邪的阴司死籍属于不可逆终局，不能再触发
		// Replicative Immortality / Morph 等第三方死亡替身，否则会形成阴司斩杀→替身→再斩的循环。
		// TechnicalRemoval 同样永不让渡。
		bool yieldToExternalDeathReplacement = cause != XjDeathCause.TechnicalRemoval
			&& !irreversibleYaoXieYinSi
			&& XjExternalUnitTransferContext.HasExternalDeathReplacementTrait(actor);
		// 0.9.9：道胎/世尊第一层防暴毙。普通死亡在结算入口即被阻断；
		// TechnicalRemoval 仍放行，确保清档/换世界不会留下幽灵实体。
		if (!yieldToExternalDeathReplacement
			&& XjDaoTaiSurvivalGuard.ShouldBlockDirectDeath(actor, cause))
		{
			return false;
		}
		// 真人、真君转世在归位前只防可避免的战斗/事故死亡。寿尽、阴司、
		// 突破失败、主动转世与技术移除均是合法终局。
		if (!yieldToExternalDeathReplacement
			&& XjReincarnationSurvivalGuard.ShouldBlockDirectDeath(actor, cause))
		{
			return false;
		}
		// 候神殊只在“自然坐化”这一条原著明确的终局上起神尸。战死分支还依赖
		// 散白羽落大成，条件未落地前绝不把普通战死伪装成神尸复生。
		if (!yieldToExternalDeathReplacement
			&& cause == XjDeathCause.NaturalOldAge
			&& XjHouShenShuSystem.TryConvertNaturalDeathToShenShi(actor))
		{
			return false;
		}
		if (!yieldToExternalDeathReplacement
			&& cause == XjDeathCause.NaturalOldAge
			&& XjVanillaDeathGuard.ShouldBlockDirectNaturalOldAgeDeath(actor, attackType))
		{
			return false;
		}
		// 闭关只隔离常规外来死亡，不能抵消角色主动求道所引发的失败终局，
		// 也不能拦截明确的脚本最终性/主动转世。金性妖邪已写入不可逆阴司死籍时，
		// 即使被第三方转成道胎/释修并塞进闭关，也不能借保护层躲过阴司代码终局。
		bool closedCultivationCanBlock = cause != XjDeathCause.TechnicalRemoval
			&& cause != XjDeathCause.BreakthroughFailure
			&& cause != XjDeathCause.ScriptedFinality
			&& cause != XjDeathCause.ShiVoluntaryReincarnation
			&& !irreversibleYaoXieYinSi;
		if (!yieldToExternalDeathReplacement
			&& closedCultivationCanBlock
			&& XjClosedCultivationGuard.IsActivelyProtected(actor))
		{
			return false;
		}
		if (!yieldToExternalDeathReplacement
			&& XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)
			&& cause != XjDeathCause.TechnicalRemoval
			&& cause != XjDeathCause.ScriptedFinality
			&& !irreversibleYaoXieYinSi)
		{
			return false;
		}
		if (!yieldToExternalDeathReplacement
			&& cause == XjDeathCause.Starvation
			&& XjVanillaDeathGuard.ShouldBlockDirectStarvationDeath(actor, attackType))
		{
			return false;
		}
		if (!yieldToExternalDeathReplacement
			&& cause == XjDeathCause.Fire
			&& XjVanillaDeathGuard.ShouldBlockDirectFireDeath(actor, attackType))
		{
			return false;
		}
		// 0.9.9.3：命数子的三次护命只在其他免费保护均未生效时消费。
		if (!yieldToExternalDeathReplacement
			&& XjMingShuChildSystem.TryConsumeDeathWard(actor, cause))
		{
			return false;
		}
		XjDeathSnapshot snapshot = XjDeathPatchBridge.CaptureBeforeDeath(actor);
		string guZunArchiveId = cause == XjDeathCause.TechnicalRemoval
			? string.Empty
			: XjGuZunRegistry.PrepareBeforeDeath(actor, cause);
		state = new XjDeathEntryState(snapshot, cause, guZunArchiveId);
		return true;
	}

	internal static bool CompleteDeath(Actor actor, in XjDeathEntryState state)
	{
		bool committed = XjDeathPatchBridge.CommitAfterDeath(actor, state.Snapshot, state.Cause);
		bool actuallyDead = actor != null && !XuanJianVNext.Core.XjSafeCore.IsAliveActor(actor);
		if (actuallyDead)
		{
			XjGuZunRegistry.FinalizeDeath(actor, state.GuZunArchiveId, state.Cause);
			if (committed)
			{
				XjDaoTaiMeritSystem.ObserveHighRealmDeath(state.Snapshot, state.Cause);
				if (state.Cause == XjDeathCause.Combat)
				{
					XjSectWarSystem.ObserveCrossSectHighRealmDeath(state.Snapshot);
				}
			}
		}
		else if (!string.IsNullOrWhiteSpace(state.GuZunArchiveId))
		{
			XjGuZunRegistry.CancelPendingDeath(state.GuZunArchiveId);
		}
		XjSemanticDiagnostics.RecordDeath(actor, state.Cause, actuallyDead && committed);
		return committed;
	}

	internal static void AbortDeath(in XjDeathEntryState state)
	{
		if (!string.IsNullOrWhiteSpace(state.GuZunArchiveId))
		{
			XjGuZunRegistry.CancelPendingDeath(state.GuZunArchiveId);
		}
	}

	internal static bool TryHandleNaturalDeath(Actor actor, ref bool result)
	{
		return XjVanillaDeathGuard.TryHandleNaturalDeathCore(actor, ref result);
	}

	internal static bool EnforceMortalCivilianLifespanLimit(Actor actor)
	{
		return XjVanillaDeathGuard.EnforceMortalCivilianLifespanLimitCore(actor);
	}

	internal static bool EnforceHardLifespanLimit(Actor actor)
	{
		return XjVanillaDeathGuard.EnforceHardLifespanLimitCore(actor);
	}

	internal static void PushForcedCause(long actorId, XjDeathCause cause)
	{
		if (actorId <= 0L) return;
		lock (Sync) ForcedCauses[actorId] = cause;
	}

	internal static void PopForcedCause(long actorId)
	{
		if (actorId <= 0L) return;
		lock (Sync) ForcedCauses.Remove(actorId);
	}

	internal static bool IsForcedCause(Actor actor, XjDeathCause cause)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return false;
		lock (Sync)
		{
			return ForcedCauses.TryGetValue(actorId, out XjDeathCause forced) && forced == cause;
		}
	}

	internal static void Clear()
	{
		lock (Sync) ForcedCauses.Clear();
	}

	private static XjDeathCause ResolveCause(Actor actor, AttackType attackType)
	{
		long actorId = GetActorId(actor);
		if (actorId > 0L)
		{
			lock (Sync)
			{
				if (ForcedCauses.TryGetValue(actorId, out XjDeathCause forced)) return forced;
			}
		}

		if (attackType == AttackType.Starvation) return XjDeathCause.Starvation;
		if (attackType == AttackType.Fire) return XjDeathCause.Fire;
		if (attackType == AttackType.Age) return XjDeathCause.NaturalOldAge;
		return XjVanillaDeathGuard.IsEnvironmentalAttackType(attackType)
			? XjDeathCause.ScriptedFinality
			: XjDeathCause.Combat;
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch (System.Exception xjCaught157_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Death/XjDeathArbitrationPipeline.cs:157", xjCaught157_1);
			 return 0L; }
	}

}
