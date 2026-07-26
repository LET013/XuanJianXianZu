using XuanJianVNext.Core;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.ZongMen;

internal static class XjZongMenGongFaBorrow
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


	/// <summary>
	/// 紫府领悟后续神通时只解析宗门功法阁中的同道途五品神通功法。
	/// 此入口不替换主功法；真正写入由仙基/神通链原子化完成。
	/// </summary>
	internal static bool TryResolveMappedGongFa(
		Actor actor,
		string daoTu,
		int ordinal,
		string[] existingIds,
		bool zhengWeiManifested,
		out string mappedXianJi,
		out string gongFaName)
	{
		mappedXianJi = string.Empty;
		gongFaName = string.Empty;
		if (!XjSafeCore.IsAliveActor(actor)
			|| XjLongShuSystem.IsExcludedFromInheritance(actor)
			|| ordinal <= 1
			|| string.IsNullOrWhiteSpace(daoTu)
			|| XjZongMenBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor) < 5)
		{
			return false;
		}

		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			return false;
		}

		string normalizedDaoTu = XjZongMenGongFaBorrowRules.Normalize(daoTu);
		bool daoZhu = actor.hasTrait("ChuShen8");
		bool allowOtherPool = !XjLongShuSystem.IsLongShu(actor);
		int selectedScore = int.MinValue;
		IReadOnlyList<XjZongMenGongFaPavilionEntry> entries =
			XjZongMenGongFaPavilion.ReadGongFaEntries(identity.ZongMenId);
		for (int i = 0; entries != null && i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry entry = entries[i];
			string mapped = XjZongMenGongFaBorrowRules.Normalize(entry.MappedXianJi);
			if (!entry.Found
				|| entry.ZongMenId != identity.ZongMenId
				|| entry.Grade != 5
				|| entry.Grade > XjZongMenBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor)
				|| !string.Equals(entry.SourceType, XjZongMenGongFaPavilion.SourceTypeGongFa, System.StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(entry.Name)
				|| string.IsNullOrWhiteSpace(mapped))
			{
				continue;
			}

			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(normalizedDaoTu, mapped);
			bool legal = daoZhu
				? kind == XjXianJiPoolKind.Native
					|| (zhengWeiManifested
						&& (kind == XjXianJiPoolKind.Lower || kind == XjXianJiPoolKind.Adjacent))
				: XjXianJiCatalog.IsAvailableForProgression(
					normalizedDaoTu, ordinal, existingIds, zhengWeiManifested, allowOtherPool, mapped);
			if (!legal) continue;

			int score = kind switch
			{
				XjXianJiPoolKind.Native => 400,
				XjXianJiPoolKind.Lower => 300,
				XjXianJiPoolKind.Adjacent => 200,
				_ => 100
			};
			if (score > selectedScore
				|| (score == selectedScore
					&& (string.IsNullOrWhiteSpace(mappedXianJi)
						|| string.CompareOrdinal(mapped, mappedXianJi) < 0)))
			{
				selectedScore = score;
				mappedXianJi = mapped;
				gongFaName = entry.Name.Trim();
			}
		}

		return !string.IsNullOrWhiteSpace(mappedXianJi) && !string.IsNullOrWhiteSpace(gongFaName);
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

		int maxBorrowableGrade = XjZongMenBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor);
		if (maxBorrowableGrade < 4)
		{
			return false;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		IReadOnlyList<XjZongMenGongFaPavilionEntry> entries = XjZongMenGongFaPavilion.ReadGongFaEntries(zongMenState.ZongMenId);
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		bool found = false;
		XjZongMenGongFaPavilionEntry selected = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjZongMenGongFaPavilionEntry entry = entries[i];
			if (!XjZongMenGongFaBorrowRules.CanBorrowGongFa(actor, zongMenState, current, entry, daoTu, maxBorrowableGrade))
			{
				continue;
			}

			if (!found || entry.Grade > selected.Grade)
			{
				found = true;
				selected = entry;
			}
		}

		if (!found)
		{
			return false;
		}

		XjGongFaAccessor.WriteState(
			actor,
			new XjGongFaState(
				true,
				selected.Name,
				selected.Grade,
				0,
				0f,
				XjZongMenGongFaBorrowRules.Normalize(selected.DaoTu),
				selected.Grade > XjGongFaAccessor.MaxActiveGrade,
				"ZongMenGongFa"));
		XjGongFaAccessor.WriteSource(actor, "宗门借法");
		XjGongFaProgression.PublishGongFaPromoted(actor, XjGongFaAccessor.BuildState(actor));
		return true;
	}
}

internal static class XjZongMenBorrowAptitudeRules
{
	internal static int GetMaxBorrowableGongFaGrade(Actor actor)
	{
		// 自行参悟、家族借法、宗门借法共用同一资质/境界上限，避免宗门绕过。
		return XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor);
	}

	internal static bool CanBorrowQiuJinFa(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz)
			&& xjZz >= 5;
	}
}

internal static class XjZongMenGongFaBorrowRules
{
	internal static bool CanBorrowGongFa(
		Actor actor,
		in XjZongMenIdentitySnapshot zongMenState,
		in XjGongFaState currentGongFaState,
		in XjZongMenGongFaPavilionEntry candidateEntry,
		string actorDaoTu,
		int maxBorrowableGrade)
	{
		if (actor?.data == null
			|| !zongMenState.Found
			|| zongMenState.ZongMenId <= 0L
			|| string.IsNullOrWhiteSpace(actorDaoTu)
			|| maxBorrowableGrade < 4
			|| !candidateEntry.Found
			|| candidateEntry.ZongMenId != zongMenState.ZongMenId
			|| !string.Equals(candidateEntry.SourceType, XjZongMenGongFaPavilion.SourceTypeGongFa, System.StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(candidateEntry.Name)
			|| string.IsNullOrWhiteSpace(candidateEntry.DaoTu)
			|| candidateEntry.Grade < 4
			|| candidateEntry.Grade > 6
			|| candidateEntry.Grade > maxBorrowableGrade)
		{
			return false;
		}

		string normalizedActorDaoTu = Normalize(actorDaoTu);
		if (!string.Equals(Normalize(candidateEntry.DaoTu), normalizedActorDaoTu, System.StringComparison.Ordinal))
		{
			return false;
		}
		if (candidateEntry.Grade >= 5)
		{
			string mapped = Normalize(candidateEntry.MappedXianJi);
			if (string.IsNullOrWhiteSpace(mapped)
				|| !XjXianJiCatalog.TryResolveOwningDaoTu(mapped, out string mappedDaoTu)
				|| !string.Equals(Normalize(mappedDaoTu), normalizedActorDaoTu, System.StringComparison.Ordinal))
			{
				return false;
			}
		}

		if (!currentGongFaState.Found)
		{
			return true;
		}

		if (!string.Equals(Normalize(currentGongFaState.DaoTu), normalizedActorDaoTu, System.StringComparison.Ordinal))
		{
			return true;
		}

		return currentGongFaState.Grade < candidateEntry.Grade;
	}

	internal static string Normalize(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}
}
