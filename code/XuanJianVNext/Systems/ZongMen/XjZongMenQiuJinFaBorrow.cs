using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.ZongMen;

internal static class XjZongMenQiuJinFaBorrow
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
		daoTu = XjZongMenGongFaBorrowRules.Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjGongFaState currentGongFa = XjGongFaAccessor.BuildState(actor);
		XjQiuJinFaState currentQiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		IReadOnlyList<XjZongMenGongFaPavilionEntry> entries = XjZongMenGongFaPavilion.ReadQiuJinFaEntries(zongMenState.ZongMenId);
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry entry = entries[i];
			if (!XjZongMenQiuJinFaBorrowRules.CanBorrowQiuJinFa(actor, zongMenState, currentGongFa, currentQiuJinFa, xianJi, entry, daoTu, realmId))
			{
				continue;
			}

			string boundAuthority = XjZongMenQiuJinFaBorrowRules.ResolveBoundAuthority(entry, daoTu);
			XjQiuJinFaAccessor.WriteState(
				actor,
				new XjQiuJinFaState(
					true,
					entry.Name,
					string.Empty,
					0,
					daoTu,
					true,
					GetCurrentYear(actor),
					"ZongMenQiuJinFa",
					boundAuthority));
			return true;
		}

		return false;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}

internal static class XjZongMenQiuJinFaBorrowRules
{
	internal static bool CanBorrowQiuJinFa(
		Actor actor,
		in XjZongMenIdentitySnapshot zongMenState,
		in XjGongFaState currentGongFaState,
		in XjQiuJinFaState currentQiuJinFaState,
		in XjXianJiState xianJiState,
		in XjZongMenGongFaPavilionEntry qiuJinFaEntry,
		string actorDaoTu,
		string realmId)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		if (snapshot.XjZz < 5
			|| Math.Floor(Math.Max(0f, snapshot.MingShu)) < 70f
			|| Math.Floor(Math.Max(0f, snapshot.HuiGuang)) < 85f
			|| !zongMenState.Found
			|| zongMenState.ZongMenId <= 0L
			|| string.IsNullOrWhiteSpace(actorDaoTu)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| xianJiState.Count < XjXianJiState.MaxCount
			|| !currentGongFaState.Found
			|| currentGongFaState.Grade != 5
			|| currentQiuJinFaState.Found
			|| !XjZongMenBorrowAptitudeRules.CanBorrowQiuJinFa(actor)
			|| !qiuJinFaEntry.Found
			|| qiuJinFaEntry.ZongMenId != zongMenState.ZongMenId
			|| !string.Equals(qiuJinFaEntry.SourceType, XjZongMenGongFaPavilion.SourceTypeQiuJinFa, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(qiuJinFaEntry.Name)
			|| string.IsNullOrWhiteSpace(qiuJinFaEntry.DaoTu))
		{
			return false;
		}

		string normalizedDaoTu = XjZongMenGongFaBorrowRules.Normalize(actorDaoTu);
		if (!string.Equals(XjZongMenGongFaBorrowRules.Normalize(qiuJinFaEntry.DaoTu), normalizedDaoTu, StringComparison.Ordinal)
			|| !string.Equals(XjZongMenGongFaBorrowRules.Normalize(currentGongFaState.DaoTu), normalizedDaoTu, StringComparison.Ordinal))
		{
			return false;
		}


		string boundAuthority = ResolveBoundAuthority(qiuJinFaEntry, normalizedDaoTu);
		return !string.IsNullOrWhiteSpace(boundAuthority)
			&& XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(normalizedDaoTu, boundAuthority);
	}

	internal static string ResolveBoundAuthority(
		in XjZongMenGongFaPavilionEntry qiuJinFaEntry,
		string normalizedDaoTu)
	{
		if (!string.IsNullOrWhiteSpace(qiuJinFaEntry.MappedXianJi))
		{
			return qiuJinFaEntry.MappedXianJi.Trim();
		}

		return XjFamilyHighGradeTransmission.ResolveBoundAuthority(
			XjZongMenGongFaBorrowRules.Normalize(normalizedDaoTu),
			qiuJinFaEntry.Name,
			qiuJinFaEntry.SourceGongFaName);
	}
}
