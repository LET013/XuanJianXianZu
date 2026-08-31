using System;
using System.Collections.Generic;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectMandateHostilityCandidate
{
	internal XjSectMandateHostilityCandidate(long targetSectId, int hostility, string reason)
	{
		TargetSectId = targetSectId;
		Hostility = hostility;
		Reason = reason ?? string.Empty;
	}

	internal long TargetSectId { get; }
	internal int Hostility { get; }
	internal string Reason { get; }
	internal bool Found => TargetSectId > 0L && Hostility > 0;
}

internal static partial class XjSectGovernanceRuntimeLane
{
	private const int MandateIntervalYears = 20;
	private const float MandateSupplyDebtThreshold = 70f;
	private const float MandatePrivilegeHeatThreshold = 70f;
	private const int MandateVendettaHostilityThreshold = 50;

	private static void BuildHousePoliticsProjectionCache()
	{
		IReadOnlyList<XjCenturyFamilyStageStateRecord> familyStates = XjCenturyAnnalsStore.ReadFamilyStageStateView();
		for (int i = 0; i < familyStates.Count; i++)
		{
			XjCenturyFamilyStageStateRecord state = familyStates[i];
			if (state != null && state.FamilyStableId > 0L) CachedFamilyStageById[state.FamilyStableId] = state;
		}

		IReadOnlyList<XjSectHostilityArchiveRecord> hostilities = XjSectWarSystem.ReadHostilities();
		for (int i = 0; i < hostilities.Count; i++)
		{
			XjSectHostilityArchiveRecord hostility = hostilities[i];
			if (hostility == null || hostility.LeftSectId <= 0L || hostility.RightSectId <= 0L
				|| hostility.Hostility < MandateVendettaHostilityThreshold || !IsBloodFeudReason(hostility.LastReason))
			{
				continue;
			}
			CacheBloodHostility(hostility.LeftSectId, hostility.RightSectId, hostility.Hostility, hostility.LastReason);
			CacheBloodHostility(hostility.RightSectId, hostility.LeftSectId, hostility.Hostility, hostility.LastReason);
		}
	}

	private static void CacheBloodHostility(long sectId, long targetSectId, int hostility, string reason)
	{
		if (sectId <= 0L || targetSectId <= 0L || sectId == targetSectId) return;
		if (!CachedBloodHostilityBySect.TryGetValue(sectId, out XjSectMandateHostilityCandidate current)
			|| hostility > current.Hostility
			|| (hostility == current.Hostility && targetSectId < current.TargetSectId))
		{
			CachedBloodHostilityBySect[sectId] = new XjSectMandateHostilityCandidate(targetSectId, hostility, reason);
		}
	}

	private static bool IsBloodFeudReason(string reason)
	{
		if (string.IsNullOrWhiteSpace(reason)) return false;
		return reason.Contains("血", StringComparison.Ordinal)
			|| reason.Contains("仇", StringComparison.Ordinal)
			|| reason.Contains("斩杀", StringComparison.Ordinal)
			|| reason.Contains("身死", StringComparison.Ordinal)
			|| reason.Contains("灭族", StringComparison.Ordinal);
	}

	/// <summary>
	/// 旧档宗门若尚无自有重宝，只从既有门下家族的余器中迁入一件最低阶器物。
	/// 每宗复用当年已经构建的席位列表，家族至少保留一件真实器物；不复制、不扫描全世界角色。
	/// </summary>
	private static void ReconcileSectTreasury(long sectId, int currentYear)
	{
		if (sectId <= 0L || currentYear <= 0 || XjFamilyFaBaoWarehouse.CountSectEntries(sectId) > 0
			|| !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			|| sect == null || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
			|| !CachedSeatsBySect.TryGetValue(sectId, out List<XjSectFamilySeatArchiveRecord> seats)
			|| seats == null || seats.Count == 0)
		{
			return;
		}

		XjSectFamilySeatArchiveRecord donor = null;
		int donorStock = 1;
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L
				|| string.Equals(seat.State, XjSectFamilySeatState.Suspended, StringComparison.Ordinal))
			{
				continue;
			}
			int stock = XjFamilyFaBaoWarehouse.CountFamilyEntries(seat.FamilyId);
			if (stock < 2) continue;
			if (donor == null || stock > donorStock
				|| stock == donorStock && seat.VoiceScore > donor.VoiceScore
				|| stock == donorStock && Math.Abs(seat.VoiceScore - donor.VoiceScore) < 0.001f && seat.FamilyId < donor.FamilyId)
			{
				donor = seat;
				donorStock = stock;
			}
		}
		if (donor == null || !XjFamilyFaBaoWarehouse.TryContributeSurplusFamilyTreasureToSect(
			donor.FamilyId,
			sectId,
			sect.Name,
			currentYear,
			out string treasureName))
		{
			return;
		}

		string familyName = XjFamilyDisplayNameResolver.Resolve(donor.FamilyId);
		string summary = (string.IsNullOrWhiteSpace(familyName) ? "门下一族" : familyName)
			+ "将余器“" + treasureName + "”奉入" + (string.IsNullOrWhiteSpace(sect.Name) ? "宗门" : sect.Name)
			+ "共库；家族仍保留本族最高阶重宝。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			"宗门重宝入库",
			summary,
			2,
			sectId: sectId,
			familyId: donor.FamilyId,
			cityId: sect.CapitalCityId,
			year: currentYear,
			eventType: "SectTreasuryContribution",
			visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.Sect),
			mirrorToWorldLog: false);
		XjThreeBookWriter.RecordSectResourceAdded(
			sectId,
			sect.Name,
			donor.FamilyId,
			familyName,
			treasureName,
			summary,
			currentYear);
		XjCenturyAnnalsStore.ObserveSectEvent(
			"SectTreasuryContribution",
			currentYear,
			sectId,
			sect.Name,
			2,
			summary,
			familyStableId: donor.FamilyId);
	}

	private static void ResolveHousePolitics(long sectId, int currentYear)
	{
		if (sectId <= 0L || currentYear <= 0 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			|| sect == null || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal))
		{
			return;
		}
		int baseline = Math.Max(sect.FoundingYear, sect.LastMandateYear);
		if (baseline > 0 && currentYear - baseline < MandateIntervalYears) return;
		if (sect.SovereignActorId <= 0L
			|| !XjScheduler.ResolveActor(sect.SovereignActorId, out Actor sovereign)
			|| sovereign?.data == null || !sovereign.isAlive()
			|| XjSectRepository.ResolveActorSectId(sovereign) != sectId)
		{
			return;
		}

		if (TryIssueInheritanceMandate(sect, currentYear, out string inheritanceSummary, out long inheritanceFamilyId, out long actorId, out string actorName))
		{
			CommitMandate(sect, currentYear, "SectMandateInheritance", "赐法扶族", inheritanceSummary, inheritanceFamilyId, actorId, actorName);
			return;
		}
		if (TryIssueSupplyMandate(sect, sovereign, currentYear, out string supplySummary, out long supplyFamilyId))
		{
			CommitMandate(sect, currentYear, "SectMandateSupply", "催缴供奉", supplySummary, supplyFamilyId, 0L, string.Empty);
			return;
		}
		if (TryIssuePrivilegeMandate(sect, sovereign, currentYear, out string privilegeSummary, out long privilegeFamilyId))
		{
			CommitMandate(sect, currentYear, "SectMandatePrivilege", "整饬强族", privilegeSummary, privilegeFamilyId, 0L, string.Empty);
			return;
		}
		if (TryIssueVendettaMandate(sect, currentYear, out string vendettaSummary))
		{
			CommitMandate(sect, currentYear, "SectMandateVendetta", "授意寻仇", vendettaSummary, 0L, 0L, string.Empty);
		}
	}

	private static bool TryIssueInheritanceMandate(
		XjSectArchiveRecord sect,
		int currentYear,
		out string summary,
		out long familyId,
		out long actorId,
		out string actorName)
	{
		summary = string.Empty;
		familyId = 0L;
		actorId = 0L;
		actorName = string.Empty;
		if (sect == null || !CachedSeatsBySect.TryGetValue(sect.SectId, out List<XjSectFamilySeatArchiveRecord> seats)) return false;

		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L
				|| !CachedFamilyStageById.TryGetValue(seat.FamilyId, out XjCenturyFamilyStageStateRecord state)
				|| state == null || state.SupportedActorId <= 0L || string.IsNullOrWhiteSpace(state.SupportPurpose)
				|| !IsMandateSupportStillUrgent(state, seat.FamilyId)
				|| !XjScheduler.ResolveActor(state.SupportedActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| XjSectRepository.ResolveActorSectId(actor) != sect.SectId
				|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(state.SupportedActorId, out long actorFamilyId)
				|| actorFamilyId != seat.FamilyId)
			{
				continue;
			}

			string granted = string.Empty;
			if (string.Equals(state.SupportPurpose, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
			{
				if (XjSectQiuJinFaBorrow.TryBorrowForActor(actor)) granted = "求金法";
				else if (XjSectGongFaBorrow.TryBorrowForActor(actor)) granted = "五品功法";
			}
			else if (string.Equals(state.SupportPurpose, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal))
			{
				if (XjSectGongFaBorrow.TryBorrowForActor(actor)) granted = "合法功法";
			}
			else if (string.Equals(state.SupportPurpose, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal))
			{
				if (XjSectCaiQiFaBorrow.TryBorrowForActor(actor)) granted = "采气法";
				else if (XjSectGongFaBorrow.TryBorrowForActor(actor)) granted = "合法功法";
			}
			if (granted.Length == 0) continue;

			familyId = seat.FamilyId;
			actorId = state.SupportedActorId;
			actorName = SafeActorName(actor);
			summary = (sect.Name ?? "某宗") + "见" + XjFamilyDisplayNameResolver.Resolve(familyId)
				+ "传承有缺，遂从宗门现有传承中赐下" + granted + "，交由族中所举" + actorName + "承接。";
			return true;
		}
		return false;
	}

	private static bool IsMandateSupportStillUrgent(XjCenturyFamilyStageStateRecord state, long familyId)
	{
		if (state == null || familyId <= 0L || string.IsNullOrWhiteSpace(state.ActiveAspiration)) return false;
		bool hasAggregate = CachedFamilyById.TryGetValue(familyId, out XjFamilyLedgerAggregate aggregate);
		string aspiration = state.ActiveAspiration.Trim();
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal))
		{
			// 家族聚合在读档或同年覆灭旁路中可能短暂缺失。缺少真实聚合时不把
			// 默认零值当作“急需扶持”，避免法旨误发资源。
			return hasAggregate && aggregate.ZiFuCount <= 0 && aggregate.JinDanCount <= 0;
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal))
		{
			return hasAggregate && aggregate.JinDanCount <= 0;
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal))
		{
			IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyGongFaWarehouse.ReadFamilyEntriesView(familyId);
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyGongFaWarehouseEntry entry = entries[i];
				if (!entry.Found) continue;
				if (string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, StringComparison.Ordinal)
					|| string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal) && entry.Grade >= 5)
				{
					return false;
				}
			}
		}
		return true;
	}

	private static bool TryIssueSupplyMandate(XjSectArchiveRecord sect, Actor sovereign, int currentYear, out string summary, out long familyId)
	{
		summary = string.Empty;
		familyId = 0L;
		if (sect == null || !CachedSeatsBySect.TryGetValue(sect.SectId, out List<XjSectFamilySeatArchiveRecord> seats)) return false;
		float bestDebt = MandateSupplyDebtThreshold;
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord cached = seats[i];
			if (cached == null || cached.FamilyId <= 0L
				|| !CanSovereignDisciplineFamily(sovereign, cached.FamilyId)
				|| !XjSectRepository.TryGetFamilySeat(sect.SectId, cached.FamilyId, out XjSectFamilySeatArchiveRecord live)
				|| live == null || live.SupplyDebt < bestDebt)
			{
				continue;
			}
			if (live.SupplyDebt > bestDebt || familyId <= 0L || live.FamilyId < familyId)
			{
				bestDebt = live.SupplyDebt;
				familyId = live.FamilyId;
			}
		}
		if (familyId <= 0L || !XjSectRepository.TryApplyMandateSupplyDemand(sect.SectId, familyId, currentYear)) return false;
		summary = (sect.Name ?? "某宗") + "清点共库，见" + XjFamilyDisplayNameResolver.Resolve(familyId)
			+ "供养债务已至" + Math.Round(bestDebt) + "，宗主下旨催缴，并暂削其既有宗门话语。";
		return true;
	}

	private static bool TryIssuePrivilegeMandate(XjSectArchiveRecord sect, Actor sovereign, int currentYear, out string summary, out long familyId)
	{
		summary = string.Empty;
		familyId = 0L;
		if (sect == null || !CachedSeatsBySect.TryGetValue(sect.SectId, out List<XjSectFamilySeatArchiveRecord> seats)) return false;
		float bestHeat = MandatePrivilegeHeatThreshold;
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord cached = seats[i];
			if (cached == null || cached.FamilyId <= 0L
				|| !CanSovereignDisciplineFamily(sovereign, cached.FamilyId)
				|| !XjSectRepository.TryGetFamilySeat(sect.SectId, cached.FamilyId, out XjSectFamilySeatArchiveRecord live)
				|| live == null || live.PrivilegeHeat < bestHeat)
			{
				continue;
			}
			if (live.PrivilegeHeat > bestHeat || familyId <= 0L || live.FamilyId < familyId)
			{
				bestHeat = live.PrivilegeHeat;
				familyId = live.FamilyId;
			}
		}
		if (familyId <= 0L || !XjSectRepository.TryApplyMandatePrivilegeRebuke(sect.SectId, familyId, currentYear)) return false;
		summary = (sect.Name ?? "某宗") + "见" + XjFamilyDisplayNameResolver.Resolve(familyId)
			+ "倚势过甚，宗主遂下旨整饬，收束其话语与取用之权。";
		return true;
	}

	/// <summary>
	/// 宗主只会整饬低于自身境界层次的家族。紫府宗主不会敲打紫府家族，
	/// 金丹宗主不会敲打金丹家族，但可约束紫府及以下家族。
	/// </summary>
	private static bool CanSovereignDisciplineFamily(Actor sovereign, long familyId)
	{
		if (sovereign?.data == null || familyId <= 0L
			|| !CachedFamilyById.TryGetValue(familyId, out XjFamilyLedgerAggregate aggregate))
		{
			return false;
		}

		string realmId = XjRealmHelper.GetUnifiedId(sovereign, XjRealmHelper.GetTraitSnapshotForRouter);
		int sovereignOrder = XjFamilyMemberLedger.GetRealmOrder(realmId);
		return sovereignOrder > 0 && aggregate.HighestRealmOrder < sovereignOrder;
	}

	private static bool TryIssueVendettaMandate(XjSectArchiveRecord sect, int currentYear, out string summary)
	{
		summary = string.Empty;
		if (sect == null || !CachedBloodHostilityBySect.TryGetValue(sect.SectId, out XjSectMandateHostilityCandidate hostility)
			|| !hostility.Found || hostility.Hostility < MandateVendettaHostilityThreshold
			|| !XjSectRepository.TryGetBySectId(hostility.TargetSectId, out XjSectArchiveRecord target)
			|| target == null
			|| !XjSectWarSystem.AddHostility(sect.SectId, hostility.TargetSectId, 10, currentYear,
				"宗主法旨授意寻仇：" + hostility.Reason))
		{
			return false;
		}
		summary = (sect.Name ?? "某宗") + "因" + (string.IsNullOrWhiteSpace(hostility.Reason) ? "两宗旧怨" : hostility.Reason)
			+ "，宗主授意门下对" + (target.Name ?? "敌宗") + "严加戒备，往后相逢自会清算旧账，两宗积怨也因此更深。";
		return true;
	}

	private static void CommitMandate(
		XjSectArchiveRecord sect,
		int currentYear,
		string eventType,
		string mandateName,
		string summary,
		long familyId,
		long actorId,
		string actorName)
	{
		if (sect == null || string.IsNullOrWhiteSpace(summary)
			|| !XjSectRepository.TryRecordMandateYear(sect.SectId, currentYear)) return;
		XjWorldHistoryStore.RecordDomainEvent(
			XuanJianVNext.Data.History.XjWorldHistoryCategory.Sect,
			(sect.Name ?? "某宗") + "宗主法旨·" + mandateName,
			summary,
			3,
			actorId: actorId,
			actorName: actorName,
			sectId: sect.SectId,
			familyId: familyId,
			cityId: sect.CapitalCityId,
			year: currentYear,
			eventType: "SectMandate:" + eventType,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
		XjCenturyAnnalsStore.ObserveSectEvent(
			eventType,
			currentYear,
			sect.SectId,
			sect.Name,
			3,
			summary,
			actorId,
			actorName,
			familyId);
		if (familyId > 0L && (string.Equals(eventType, "SectMandateSupply", StringComparison.Ordinal)
			|| string.Equals(eventType, "SectMandatePrivilege", StringComparison.Ordinal)))
		{
			XjThreeBookWriter.RecordFamilyDiscipline(
				familyId,
				XjFamilyDisplayNameResolver.Resolve(familyId),
				sect.SectId,
				sect.Name,
				currentYear,
				eventType,
				string.Equals(eventType, "SectMandatePrivilege", StringComparison.Ordinal)
					? "倚势过甚，取用与话语越过常例"
					: "供养欠缴，未能按例补足宗门共库",
				string.Equals(eventType, "SectMandatePrivilege", StringComparison.Ordinal));
		}
	}

}
