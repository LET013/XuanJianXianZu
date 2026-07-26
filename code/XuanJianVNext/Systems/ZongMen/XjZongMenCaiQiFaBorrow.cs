using XuanJianVNext.Core;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.ZongMen;

internal static class XjZongMenBorrowRules
{
	internal static bool CanBorrowCaiQiFa(
		Actor actor,
		in XjZongMenIdentitySnapshot zongMenState,
		in XjCaiQiFaState currentCaiQiFaState,
		in XjZongMenCaiQiWarehouseEntry candidateEntry,
		string actorDaoTu)
	{
		if (actor?.data == null
			|| !zongMenState.Found
			|| zongMenState.ZongMenId <= 0L
			|| string.IsNullOrWhiteSpace(actorDaoTu)
			|| !candidateEntry.Found
			|| candidateEntry.ZongMenId != zongMenState.ZongMenId
			|| string.IsNullOrWhiteSpace(candidateEntry.ResourceName)
			|| string.IsNullOrWhiteSpace(candidateEntry.DaoTu))
		{
			return false;
		}

		string normalizedActorDaoTu = Normalize(actorDaoTu);
		if (!string.Equals(Normalize(candidateEntry.DaoTu), normalizedActorDaoTu, System.StringComparison.Ordinal))
		{
			return false;
		}

		return !currentCaiQiFaState.Found
			|| !string.Equals(Normalize(currentCaiQiFaState.DaoTu), normalizedActorDaoTu, System.StringComparison.Ordinal);
	}

	internal static string Normalize(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}
}

internal static class XjZongMenCaiQiFaBorrow
{
	internal static bool TryBorrowForActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return false;
		}

		XjZongMenIdentitySnapshot zongMenState = XjZongMenAccessor.BuildIdentity(actor);
		return TryBorrowForActor(actor, zongMenState);
	}

	internal static bool TryBorrowForActor(Actor actor, in XjZongMenIdentitySnapshot zongMenState)
	{
		if (!zongMenState.Found || zongMenState.ZongMenId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = XjZongMenBorrowRules.Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		XjCaiQiFaState current = XjCaiQiFaAccessor.BuildState(actor);
		if (current.Found && string.Equals(XjZongMenBorrowRules.Normalize(current.DaoTu), daoTu, System.StringComparison.Ordinal))
		{
			return false;
		}

		IReadOnlyList<XjZongMenCaiQiWarehouseEntry> entries = XjZongMenCaiQiWarehouse.ReadCaiQiFaResources(zongMenState.ZongMenId);
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenCaiQiWarehouseEntry entry = entries[i];
			if (!XjZongMenBorrowRules.CanBorrowCaiQiFa(actor, zongMenState, current, entry, daoTu))
			{
				continue;
			}

			XjCaiQiFaAccessor.WriteState(
				actor,
				new XjCaiQiFaState(
					true,
					entry.ResourceName,
					daoTu,
					entry.SourcePlace,
					entry.Year,
					"ZongMenCaiQiFa"));
			return true;
		}

		return false;
	}
}
