using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjFamilyHighGradeTransmission
{
	private const float QiuJinBorrowMinimumMingShu = 70f;
	private const float QiuJinBorrowMinimumHuiGuang = 85f;

	internal static void RecordJinDanGongFaSet(
		Actor actor,
		string daoTu,
		in XjGongFaState finalGongFa,
		in XjQiuJinFaState qiuJinFa,
		in XjXianJiState xianJi,
		int currentYear)
	{
		if (actor?.data == null
			|| XjLongShuSystem.IsExcludedFromInheritance(actor)
			|| string.IsNullOrWhiteSpace(daoTu)
			|| !TryGetConfirmedFamily(actor, out long actorId, out long familyId))
		{
			return;
		}

		// 金丹功法传承只读取角色真实持久化的五部功法，禁止再根据仙基
		// 临时生成四部五品功法写入家族仓库。
		IReadOnlyList<XjActorGongFaCollection.Record> records = XjActorGongFaCollection.ReadRecords(actor);
		for (int i = 0; i < records.Count; i++)
		{
			XjActorGongFaCollection.Record record = records[i];
			if (string.IsNullOrWhiteSpace(record.Name)
				|| (record.Grade != 5 && record.Grade != 6)
				|| string.IsNullOrWhiteSpace(record.MappedXianJi))
			{
				continue;
			}

			string boundGongFaName = string.Empty;
			string authority = record.Grade >= 6 ? qiuJinFa.BoundAuthority : string.Empty;
			XjFamilyGongFaWarehouse.AddGongFaToFamily(
				actorId,
				familyId,
				record.Name,
				record.Grade,
				currentYear,
				XjFamilyGongFaWarehouse.SourceTypeGongFa,
				Normalize(record.DaoTu),
				boundGongFaName,
				record.MappedXianJi,
				authority);
		}
	}

	internal static string ResolveMappedXianJi(string daoTu, string gongFaName, int grade, string sourceType)
	{
		// 服气本命法虽然复用功法物品容器，但绝不映射紫府金丹仙基/神通。
		if (XjFuQiCoreCatalog.IsKnownMethodName(gongFaName))
		{
			return string.Empty;
		}
		if ((grade != 5 && grade != 6)
			|| !string.Equals(sourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal)
			|| !XjXianJiCatalog.TryResolveMappedXianJi(daoTu, gongFaName, out string mappedXianJi))
		{
			return string.Empty;
		}

		return mappedXianJi;
	}

	internal static string ResolveBoundAuthority(string daoTu, string knowledgeName, string boundGongFaName)
	{
		_ = boundGongFaName;
		IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(Normalize(daoTu));
		if (authorities.Count == 0 || string.IsNullOrWhiteSpace(knowledgeName))
		{
			return string.Empty;
		}

		long seed = XjDeterministicHash.StableHash(knowledgeName.Trim() + "|" + Normalize(daoTu));
		return authorities[XjDeterministicHash.PositiveIndex(seed, Normalize(daoTu) + "|qiujin_authority", authorities.Count)];
	}

	internal static bool TryResolveFamilyMappedXianJi(
		Actor actor,
		string daoTu,
		int ordinal,
		string[] existingIds,
		bool zhengWeiManifested,
		out string mappedXianJi)
	{
		return TryResolveFamilyMappedGongFa(
			actor,
			daoTu,
			ordinal,
			existingIds,
			zhengWeiManifested,
			out mappedXianJi,
			out _);
	}

	internal static bool TryResolveFamilyMappedGongFa(
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
		string normalizedDaoTu = Normalize(daoTu);
		if (actor?.data == null
			|| ordinal <= 1
			|| string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor) < 5
			|| !TryReadConfirmedFamilyEntries(actor, out IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries))
		{
			return false;
		}

		bool daoZhu = actor.hasTrait("ChuShen8");
		int selectedScore = int.MinValue;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry candidate = entries[i];
			string mapped = Normalize(candidate.MappedXianJi);
			if (!candidate.Found
				|| candidate.Grade != 5
				|| candidate.Grade > XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor)
				|| !string.Equals(candidate.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(mapped)
				|| string.IsNullOrWhiteSpace(candidate.GongFaName)
				|| Contains(existingIds, mapped))
			{
				continue;
			}

			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(normalizedDaoTu, mapped);
			bool legal = daoZhu
				? XjXianJiCatalog.IsDaoZhuGrantAllowed(normalizedDaoTu, mapped, existingIds)
				: XjXianJiCatalog.IsAvailableForProgression(
					normalizedDaoTu, ordinal, existingIds, zhengWeiManifested, mapped);
			if (!legal)
			{
				continue;
			}

			// 直接优先使用自身道途上位神通功法，其次本道下位、相邻道途上位，最后才是其他上位。
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
				gongFaName = candidate.GongFaName.Trim();
			}
		}

		return !string.IsNullOrWhiteSpace(mappedXianJi) && !string.IsNullOrWhiteSpace(gongFaName);
	}

	internal static bool TryBorrowGrade5(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjGongFaState current)
	{
		if (actor?.data == null
			|| !current.Found
			|| current.Grade <= 0
			|| current.Grade >= 5
			|| XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot) < 5
			|| !TryReadConfirmedFamilyEntries(actor, out IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries))
		{
			return false;
		}

		string daoTu = ResolveDaoTu(current.DaoTu, snapshot.DaoTu);
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (!XjActorGongFaCollection.TryGetPrimary(actor, out XjActorGongFaCollection.Record primary)
			|| string.IsNullOrWhiteSpace(primary.MappedXianJi)
			|| !Contains(xianJi.Ids, primary.MappedXianJi))
		{
			return false;
		}
		string requiredMappedXianJi = primary.MappedXianJi.Trim();
		if (XjXianJiCatalog.GetPoolKind(daoTu, requiredMappedXianJi) != XjXianJiPoolKind.Native
			|| !XjXianJiCatalog.TryResolveOwningDaoTu(requiredMappedXianJi, out string mappedDaoTu)
			|| !string.Equals(Normalize(mappedDaoTu), daoTu, StringComparison.Ordinal))
		{
			return false;
		}
		XjFamilyGongFaWarehouseEntry selected = default;
		int selectedScore = int.MinValue;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry candidate = entries[i];
			if (!candidate.Found
				|| candidate.Grade != 5
				|| !string.Equals(candidate.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal)
				|| candidate.Grade > XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot)
				|| !string.Equals(Normalize(candidate.DaoTu), daoTu, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(candidate.MappedXianJi)
				|| !string.Equals(candidate.MappedXianJi.Trim(), requiredMappedXianJi, StringComparison.Ordinal)
				|| XjXianJiCatalog.GetPoolKind(daoTu, candidate.MappedXianJi) != XjXianJiPoolKind.Native)
			{
				continue;
			}

			int candidateScore = 540;
			if (!string.IsNullOrWhiteSpace(current.Name)
				&& string.Equals(candidate.GongFaName, current.Name, StringComparison.Ordinal))
			{
				candidateScore += 20;
			}
			if (candidate.ActorId > 0L)
			{
				candidateScore += 5;
			}

			if (!selected.Found
				|| candidateScore > selectedScore
				|| (candidateScore == selectedScore
					&& string.CompareOrdinal(candidate.GongFaName, selected.GongFaName) < 0))
			{
				selected = candidate;
				selectedScore = candidateScore;
			}
		}

		if (!selected.Found)
		{
			return false;
		}

		XjGongFaState borrowed = new XjGongFaState(
			true,
			selected.GongFaName,
			5,
			0,
			0f,
			daoTu,
			true,
			"FamilyBorrowGrade5");
		if (!XjActorGongFaCollection.TryReplacePrimaryForSameMapping(
			actor,
			borrowed,
			requiredMappedXianJi,
			"家族借法"))
		{
			return false;
		}
		XjGongFaAccessor.WriteSource(actor, "家族借法");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason, string.Empty);
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.GongFaPromoted(actor, borrowed.Name, borrowed.Grade, borrowed.DaoTu, "FamilyBorrowGrade5"));
		return true;
	}

	internal static bool TryBorrowQiuJinFa(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjGongFaState current,
		int currentYear)
	{
		if (actor?.data == null
			|| snapshot.XjZz < 4
			|| !string.Equals(snapshot.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| !current.Found
			|| current.Grade != 5
			|| !XjXianJiAccessor.HasFive(actor)
			|| XjQiuJinFaAccessor.BuildState(actor).Found
			|| !HasBorrowQualification(snapshot, QiuJinBorrowMinimumMingShu, QiuJinBorrowMinimumHuiGuang)
			|| !TryReadConfirmedFamilyEntries(actor, out IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries))
		{
			return false;
		}

		string daoTu = ResolveDaoTu(current.DaoTu, snapshot.DaoTu);
		XjFamilyGongFaWarehouseEntry selected = SelectAuthorityKnowledge(entries, daoTu);
		if (!selected.Found)
		{
			return false;
		}

		XjQiuJinFaState borrowed = new XjQiuJinFaState(
			true,
			selected.GongFaName,
			string.Empty,
			0,
			daoTu,
			true,
			currentYear,
			"FamilyBorrowQiuJinFa",
			selected.BoundAuthority);
		XjQiuJinFaAccessor.WriteState(actor, borrowed);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, string.Empty);
		return true;
	}

	internal static bool TryBorrowGrade6(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjGongFaState current)
	{
		if (actor?.data == null
			|| !current.Found
			|| current.Grade != 5
			|| XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot) < 6
			|| !XjXianJiAccessor.HasFive(actor)
			|| !TryReadConfirmedFamilyEntries(actor, out IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries))
		{
			return false;
		}

		string daoTu = ResolveDaoTu(current.DaoTu, snapshot.DaoTu);
		XjFamilyGongFaWarehouseEntry selected = SelectGrade6GongFa(entries, daoTu, actor);
		if (!selected.Found)
		{
			return false;
		}

		string name = selected.Grade >= 6
			? selected.GongFaName
			: XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, 6);
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		XjGongFaState borrowed = new XjGongFaState(
			true,
			name,
			6,
			0,
			0f,
			daoTu,
			true,
			"FamilyBorrowGrade6");
		if (!XjActorGongFaCollection.PromoteBoundGrade5ToGrade6(
			actor,
			current.Name,
			borrowed.Name,
			daoTu,
			"家族借法"))
		{
			return false;
		}

		XjGongFaAccessor.WriteState(actor, borrowed);
		XjGongFaAccessor.WriteSource(actor, "家族借法");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, string.Empty);
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.GongFaPromoted(actor, borrowed.Name, borrowed.Grade, borrowed.DaoTu, "FamilyBorrowGrade6"));
		return true;
	}

	private static XjFamilyGongFaWarehouseEntry SelectGrade6GongFa(
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries,
		string daoTu,
		Actor actor)
	{
		XjFamilyGongFaWarehouseEntry selected = default;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry candidate = entries[i];
			if (!candidate.Found
				|| !string.Equals(candidate.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal)
				|| candidate.Grade < 6
				|| (candidate.Grade >= XjDaoTaiGongFaService.DaoTaiGrade
					&& !XjDaoTaiGongFaService.CanBorrowGradeSeven(actor))
				|| !string.Equals(Normalize(candidate.DaoTu), daoTu, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(candidate.BoundAuthority)
				|| !DoesDaoTuHoldAuthority(daoTu, candidate.BoundAuthority))
			{
				continue;
			}

			if (!selected.Found
				|| candidate.Grade > selected.Grade
				|| (candidate.Grade == selected.Grade
					&& string.CompareOrdinal(candidate.GongFaName, selected.GongFaName) < 0))
			{
				selected = candidate;
			}
		}
		return selected;
	}

	private static XjFamilyGongFaWarehouseEntry SelectAuthorityKnowledge(
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries,
		string daoTu)
	{
		XjFamilyGongFaWarehouseEntry selected = default;
		int selectedScore = int.MinValue;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry candidate = entries[i];
			bool isQiuJinFa = string.Equals(candidate.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, StringComparison.Ordinal);
			if (!candidate.Found
				|| !isQiuJinFa
				|| !string.Equals(Normalize(candidate.DaoTu), daoTu, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(candidate.BoundAuthority)
				|| !DoesDaoTuHoldAuthority(daoTu, candidate.BoundAuthority))
			{
				continue;
			}

			int candidateScore = 260;
			if (!string.IsNullOrWhiteSpace(candidate.BoundGongFaName))
			{
				candidateScore += 30;
			}
			if (candidate.ActorId > 0L)
			{
				candidateScore += 10;
			}

			if (!selected.Found
				|| candidateScore > selectedScore
				|| (candidateScore == selectedScore
					&& string.CompareOrdinal(candidate.GongFaName, selected.GongFaName) < 0))
			{
				selected = candidate;
				selectedScore = candidateScore;
			}
		}

		return selected;
	}

	private static bool DoesDaoTuHoldAuthority(string daoTu, string authority)
	{
		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(daoTu);
		for (int i = 0; i < catalog.Count; i++)
		{
			if (string.Equals(catalog[i], authority, StringComparison.Ordinal)
				&& !XjGuoWeiQuanBingRegistry.IsAuthorityLost(daoTu, authority))
			{
				return true;
			}
		}

		return false;
	}

	private static bool HasBorrowQualification(
		in XjActorCultivationSnapshot snapshot,
		float minimumMingShu,
		float minimumHuiGuang)
	{
		return Math.Floor(Math.Max(0f, snapshot.MingShu)) >= minimumMingShu
			&& Math.Floor(Math.Max(0f, snapshot.HuiGuang)) >= minimumHuiGuang;
	}

	private static bool TryReadConfirmedFamilyEntries(Actor actor, out IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries)
	{
		entries = Array.Empty<XjFamilyGongFaWarehouseEntry>();
		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return false;
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord family)
			|| !family.Found
			|| !string.Equals(family.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			|| family.RootActorId <= 0L)
		{
			return false;
		}

		entries = XjFamilyWarehouseReadModel.Shared.ReadFamilyGongFaEntries(family.RootActorId)
			?? Array.Empty<XjFamilyGongFaWarehouseEntry>();
		return entries.Count > 0;
	}

	private static bool TryGetConfirmedFamily(Actor actor, out long actorId, out long familyId)
	{
		actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		familyId = 0L;
		if (actorId <= 0L
			|| !XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord family)
			|| !family.Found
			|| !string.Equals(family.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
			|| family.RootActorId <= 0L)
		{
			return false;
		}

		familyId = family.RootActorId;
		return true;
	}


	private static bool Contains(string[] values, string target)
	{
		for (int i = 0; values != null && i < values.Length; i++)
		{
			if (string.Equals(values[i], target, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static string ResolveDaoTu(string primary, string fallback)
	{
		return Normalize(string.IsNullOrWhiteSpace(primary) ? fallback : primary);
	}

	private static string Normalize(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}
}
