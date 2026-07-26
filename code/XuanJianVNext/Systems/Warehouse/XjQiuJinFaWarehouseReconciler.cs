using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Warehouse;

/// <summary>
/// 将角色乾坤袋中已经存在的求金法回流到其家族、宗门仓库。
/// 该过程只在年度修士管线执行，写入接口本身具有等价项去重，不会逐年重复增殖。
/// </summary>
internal static class XjQiuJinFaWarehouseReconciler
{
	internal static void ReconcileActor(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaReady, out int ready)
			|| ready != 1)
		{
			return;
		}

		XjQiuJinFaState state = XjQiuJinFaAccessor.BuildState(actor);
		if (!state.Found || !state.Ready || string.IsNullOrWhiteSpace(state.Name))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		int grade = 5;
		int year = state.LastYear > 0 ? state.LastYear : Math.Max(0, currentYear);
		string actorName = actor.getName() ?? string.Empty;

		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyStableId)
			&& familyStableId > 0L)
		{
			XjFamilyGongFaWarehouse.AddGongFaToFamily(
				actorId,
				familyStableId,
				state.Name,
				grade,
				year,
				XjFamilyGongFaWarehouse.SourceTypeQiuJinFa,
				state.SourceDaoTu,
				string.Empty,
				string.Empty,
				state.BoundAuthority);
		}

		XjZongMenIdentitySnapshot zongMen = XjZongMenAccessor.BuildIdentity(actor);
		if (zongMen.Found && zongMen.ZongMenId > 0L)
		{
			XjZongMenGongFaPavilion.TryAddQiuJinFa(
				zongMen.ZongMenId,
				zongMen.ZongMenName,
				actorId,
				actorName,
				state.Name,
				grade,
				state.SourceDaoTu,
				string.Empty,
				state.BoundAuthority,
				year);
		}
	}
}
