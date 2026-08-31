using XuanJianVNext.Core;
using System.Collections.Generic;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectGongFaBorrow
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
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| ordinal <= 1
			|| string.IsNullOrWhiteSpace(daoTu)
			|| XjSectBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor) < 5)
		{
			return false;
		}

		XjSectIdentitySnapshot identity = XjSectIdentityReader.BuildIdentity(actor);
		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			return false;
		}

		string normalizedDaoTu = XjSectGongFaBorrowRules.Normalize(daoTu);
		bool daoZhu = actor.hasTrait("ChuShen8");
		bool allowOtherPool = false;
		int selectedScore = int.MinValue;
		IReadOnlyList<XjSectGongFaPavilionEntry> entries =
			XjSectGongFaPavilion.ReadGongFaEntries(identity.ZongMenId);
		for (int i = 0; entries != null && i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = entries[i];
			string mapped = XjSectGongFaBorrowRules.Normalize(entry.MappedXianJi);
			if (!entry.Found
				|| entry.ZongMenId != identity.ZongMenId
				|| entry.Grade != 5
				|| entry.Grade > XjSectBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor)
				|| !string.Equals(entry.SourceType, XjSectGongFaPavilion.SourceTypeGongFa, System.StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(entry.Name)
				|| string.IsNullOrWhiteSpace(mapped))
			{
				continue;
			}

			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(normalizedDaoTu, mapped);
			bool legal = daoZhu
				? XjXianJiCatalog.IsDaoZhuGrantAllowed(normalizedDaoTu, mapped, existingIds)
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

	internal static bool TryBorrowForActor(Actor actor, in XjSectIdentitySnapshot zongMenState)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !zongMenState.Found
			|| zongMenState.ZongMenId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = XjSectGongFaBorrowRules.Normalize(daoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		int maxBorrowableGrade = XjSectBorrowAptitudeRules.GetMaxBorrowableGongFaGrade(actor);
		if (maxBorrowableGrade < 4)
		{
			return false;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		IReadOnlyList<XjSectGongFaPavilionEntry> entries = XjSectGongFaPavilion.ReadGongFaEntries(zongMenState.ZongMenId);
		if (entries == null || entries.Count == 0)
		{
			return false;
		}

		bool found = false;
		XjSectGongFaPavilionEntry selected = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjSectGongFaPavilionEntry entry = entries[i];
			if (!XjSectGongFaBorrowRules.CanBorrowGongFa(actor, zongMenState, current, entry, daoTu, maxBorrowableGrade))
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
				XjSectGongFaBorrowRules.Normalize(selected.DaoTu),
				selected.Grade > XjGongFaAccessor.MaxActiveGrade,
				"ZongMenGongFa"));
		XjGongFaAccessor.WriteSource(actor, "宗门借法");
		XjGongFaProgression.PublishGongFaPromoted(actor, XjGongFaAccessor.BuildState(actor));
		return true;
	}
}

internal static class XjSectBorrowAptitudeRules
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
			&& xjZz >= 4;
	}
}

internal static class XjSectGongFaBorrowRules
{
	internal static bool CanBorrowGongFa(
		Actor actor,
		in XjSectIdentitySnapshot zongMenState,
		in XjGongFaState currentGongFaState,
		in XjSectGongFaPavilionEntry candidateEntry,
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
			|| !string.Equals(candidateEntry.SourceType, XjSectGongFaPavilion.SourceTypeGongFa, System.StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(candidateEntry.Name)
			|| XjFuQiCoreCatalog.IsKnownMethodName(candidateEntry.Name)
			|| string.IsNullOrWhiteSpace(candidateEntry.DaoTu)
			|| candidateEntry.Grade < 4
			|| candidateEntry.Grade > XjDaoTaiGongFaService.DaoTaiGrade
			|| (candidateEntry.Grade >= XjDaoTaiGongFaService.DaoTaiGrade
				&& !XjDaoTaiGongFaService.CanBorrowGradeSeven(actor))
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
