using System;
using UnityEngine;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;

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
		int currentYear = ctx.CurrentYear;

		if (ctx.HasBudget)
		{
			RunStage("ShenTong", actor, () =>
				XjXuanJianShenTongSpecials.TickActor(actor, snapshot, currentYear));
			ctx = ctx.WithIncrement();
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			RunStage("ZiFuProgression", actor, () => XjZiFuProgression.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			bool hadQiuJinFa = XjQiuJinFaAccessor.BuildState(actor).Found;
			RunStage("QiuJinFa", actor, () => XjQiuJinFaSystem.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			if (!hadQiuJinFa && XjQiuJinFaAccessor.BuildState(actor).Found)
			{
				return;
			}
		}

		if (ctx.IsZiFu && ctx.HasBudget)
		{
			RunStage("Grade6Promotion", actor, () =>
				XjQiuJinBoundGongFaPromotion.TickActor(actor, snapshot));
			ctx = ctx.WithIncrement();
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
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

	private static void RunStage(string stage, Actor actor, Action action)
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
			Debug.LogError("[玄鉴][高境年度链] " + stage + " actor=" + actorId + " ex=" + ex.GetType().Name);
		}
	}
}
