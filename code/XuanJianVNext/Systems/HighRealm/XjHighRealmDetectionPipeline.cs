using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 统一的高境界（紫府/金丹）年度检测管道。
/// 消除此前 ProcessHighRealm 中 5 个顺序 TickActor 调用的重复 realm 门控，
/// 改为一次性境界判断后按优先级分发，每个子检测受 budget 限制。
/// </summary>
internal static class XjHighRealmDetectionPipeline
{
	internal static void Tick(ref XjHighRealmDetectionContext ctx)
	{
		if (!ctx.IsZiFu && !ctx.IsJinDanLike)
		{
			return;
		}

		Actor actor = ctx.Actor;
		XjActorCultivationSnapshot snapshot = ctx.Snapshot;
		XjActorRevisionToken snapshotRevision = ReadSnapshotRevision(actor);
		int currentYear = ctx.CurrentYear;
		if (ctx.IsJinDanLike)
		{
			RunStage("GuZunHighestSnapshot", actor, () => XjGuZunRegistry.ObserveHighRealm(actor, currentYear));
			// 道行、果位意象、尊修事与道统中兴共用一次高境年度结算，
			// 不依赖后续预算，避免高负载时闭关和道统成长停摆。
			RunStage("HighRealmDaoState", actor, () => XjHighRealmDaoStateService.TickAnnual(actor, currentYear));
		}

		bool isFuQiYangXing = XjCultivationPathRules.IsFuQiYangXing(actor);
		if (ctx.HasBudget
			&& (!isFuQiYangXing
				|| XjXuanJianShenTongSpecials.IsJieLinXian(actor)
				|| XjXuanJianShenTongSpecials.IsYuYiXian(actor)))
		{
			// 普通真君羽士没有仙基/五神通，不进入紫金神通运行器。
			// 服气太阴失败所得的结璘仙仍需在此维持专属特质、名号与名额登记。
			RunStage("ShenTong", actor, () =>
				XjXuanJianShenTongSpecials.TickActor(actor, snapshot, currentYear));
			ctx = ctx.WithIncrement();
		}

		if (ctx.IsZiFu)
		{
			XjZiJinSwordDaoSystem.TryEvaluateResearchChoice(actor, currentYear);
		}
		bool ziJinSwordManaged = ctx.IsZiFu && XjZiJinSwordDaoSystem.ShouldManage(actor);
		if (ctx.IsZiFu && ctx.HasBudget)
		{
			bool stageCompleted;
			if (ziJinSwordManaged)
			{
				stageCompleted = RunStage("ZiJinSwordDao", actor, () => XjZiJinSwordDaoSystem.TickActor(actor, snapshot, currentYear));
			}
			else
			{
				stageCompleted = RunStage("ZiFuProgression", actor, () => XjZiFuProgression.TickActor(actor, snapshot));
			}
			ctx = ctx.WithIncrement();
			RefreshSnapshotIfRevisionChanged(actor, ref snapshot, ref snapshotRevision, force: !stageCompleted);
		}

		// 青宣的后四道仙基必须由玄羊子逐一抬举为神通。该步骤属于同一紫府
		// 年度阶段，不额外占用检测预算，并始终发生在求金法、六品晋升和金丹尝试之前。
		if (ctx.IsZiFu && XjQingXuanKongZhengSystem.IsQingXuanDaoTu(snapshot.DaoTu))
		{
			bool stageCompleted = RunStage("QingXuanUplift", actor, () =>
				XjQingXuanKongZhengSystem.TickZiFuUplift(actor, currentYear));
			RefreshSnapshotIfRevisionChanged(actor, ref snapshot, ref snapshotRevision, force: !stageCompleted);
		}

		// 五道剑道神通必须由角色逐项自行创造。研究未完成前禁止普通仙基、
		// 求金法、六品晋升和金丹尝试继续推进，避免旧道途链与剑道链串线。
		if (ziJinSwordManaged && XjZiJinSwordDaoSystem.IsResearchInProgress(actor))
		{
			return;
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			bool hadQiuJinFa = snapshot.HasQiuJinFa;
			bool stageCompleted = RunStage("QiuJinFa", actor, () => XjQiuJinFaSystem.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
			RefreshSnapshotIfRevisionChanged(actor, ref snapshot, ref snapshotRevision, force: !stageCompleted);
			if (!hadQiuJinFa && snapshot.HasQiuJinFa)
			{
				return;
			}
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			bool stageCompleted = RunStage("Grade6Promotion", actor, () =>
				XjQiuJinBoundGongFaPromotion.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
			RefreshSnapshotIfRevisionChanged(actor, ref snapshot, ref snapshotRevision, force: !stageCompleted);
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			RunStage("JinDanBreakthrough", actor, () =>
				XjJinDanBreakthroughSystem.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
		}

		if (ctx.IsJinDan && ctx.HasBudget)
		{
			RunStage("AuthorityLifecycle", actor, () =>
				XjGuoWeiQuanBingLifecycle.TickActor(actor, currentYear));
			ctx = ctx.WithIncrement();
		}

		if (ctx.IsJinDanLike && ctx.HasBudget)
		{
			RunStage("JinDanHunt", actor, () =>
				XjJinDanHuntSystem.TickAnnual(actor, snapshot, currentYear));
			ctx = ctx.WithIncrement();
		}

		if (ctx.IsJinDan && XjLongShuSystem.IsLongShu(actor) && ctx.HasBudget)
		{
			RunStage("LongShuDongTian", actor, () =>
				XjLongShuDongTianSystem.TickAnnual(actor, currentYear));
			ctx = ctx.WithIncrement();
		}
	}

	/// <summary>
	/// H2: the annual high-realm pipeline owns one short-lived snapshot. Rebuild it only
	/// after a stage actually commits actor state through the central revision store.
	/// This keeps the old semantic refresh boundaries while removing unconditional
	/// Build(actor) calls for the overwhelmingly common no-op annual stages.
	/// </summary>
	private static void RefreshSnapshotIfRevisionChanged(
		Actor actor,
		ref XjActorCultivationSnapshot snapshot,
		ref XjActorRevisionToken snapshotRevision,
		bool force = false)
	{
		XjActorRevisionToken currentRevision = ReadSnapshotRevision(actor);
		if (!force && currentRevision == snapshotRevision)
		{
			return;
		}

		snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		snapshotRevision = currentRevision;
	}

	private static XjActorRevisionToken ReadSnapshotRevision(Actor actor)
	{
		if (actor?.data == null)
		{
			return default;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			? XjActorStateRevisionStore.GetToken(actorId)
			: default;
	}

	private static bool RunStage(string stage, Actor actor, Action action)
	{
		try
		{
			action?.Invoke();
			return true;
		}
		catch (Exception ex)
		{
			// 年度链异常不能退化成每名角色、每年一条 Debug.LogError 的日志风暴。
			// 统一交给限频诊断；业务阶段保持 fail-soft，但机会时钟由各子系统自己回滚。
			XjExceptionDiagnostics.Report("XjHighRealmDetectionPipeline." + stage, ex);
			return false;
		}
	}
}
