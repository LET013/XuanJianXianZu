using XuanJianVNext.Core;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectBorrowRules
{
	internal static bool CanBorrowCaiQiFa(
		Actor actor,
		in XjSectIdentitySnapshot zongMenState,
		in XjCaiQiFaState currentCaiQiFaState,
		in XjSectCaiQiWarehouseEntry candidateEntry,
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

internal static class XjSectCaiQiFaBorrow
{
	internal static bool TryBorrowForActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return false;
		}

		XjSectIdentitySnapshot zongMenState = XjSectIdentityReader.BuildIdentity(actor);
		return TryBorrowForActor(actor, zongMenState);
	}

	internal static bool TryBorrowForActor(Actor actor, in XjSectIdentitySnapshot zongMenState)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !zongMenState.Found
			|| zongMenState.ZongMenId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = XjSectBorrowRules.Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		XjCaiQiFaState current = XjCaiQiFaAccessor.BuildState(actor);
		if (current.Found && string.Equals(XjSectBorrowRules.Normalize(current.DaoTu), daoTu, System.StringComparison.Ordinal))
		{
			return false;
		}

		IReadOnlyList<XjSectCaiQiWarehouseEntry> entries = XjSectCaiQiWarehouse.ReadCaiQiFaResources(zongMenState.ZongMenId);
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjSectCaiQiWarehouseEntry entry = entries[i];
			if (!XjSectBorrowRules.CanBorrowCaiQiFa(actor, zongMenState, current, entry, daoTu))
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
