using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Rank;

namespace XuanJianVNext.Systems.Chronicle;

/// <summary>
/// 人丹血债的低频年度事件结算。
/// 不写寻路、不设置原生攻击目标；每次直接使用双方现有战力判定成功、失败或反杀。
/// </summary>
internal static class XjFamilyVendettaRegistry
{
	private const int MaxPendingVendettas = 2048;
	private const int MaxAnnualRevengeEvents = 5;
	private const int RevengeCooldownYears = 10;
	private const int JinDanRetributionMinDelayYears = 3;
	private const int JinDanRetributionMaxDelayYears = 6;
	private const int UnresolvedActorGraceYears = 50;
	private const int ManipulationHostilityThreshold = 50;
	private const int ManipulationChancePercent = 35;
	private const int VendettaAttackType = 5;
	private const float MinimumVendettaPowerRatio = 0.65f;

	private static readonly List<XjFamilyVendettaArchiveRecord> Pending = new List<XjFamilyVendettaArchiveRecord>();
	private static readonly HashSet<XjFamilyVendettaArchiveRecord> PendingMembership = new HashSet<XjFamilyVendettaArchiveRecord>();
	private static readonly List<XjFamilyVendettaArchiveRecord> CandidateBuffer = new List<XjFamilyVendettaArchiveRecord>();
	private static readonly List<XjFamilyVendettaArchiveRecord> AnnualPendingSnapshot = new List<XjFamilyVendettaArchiveRecord>();

	private enum AnnualPhase : byte
	{
		None = 0,
		CollectCandidates = 1,
		ProcessCandidates = 2
	}

	private static AnnualPhase _annualPhase;
	private static int _annualYear;
	private static int _annualIndex;
	private static int _annualProcessed;
	private static bool _annualChanged;

	internal static int Count => Pending.Count;

	internal static void RecordRenDanVendetta(
		long victimFamilyId,
		long victimActorId,
		string victimName,
		long offenderFamilyId,
		long offenderActorId,
		string offenderName,
		string xianJi,
		int year,
		string detail = "")
	{
		if (victimFamilyId <= 0L || victimActorId <= 0L || offenderActorId <= 0L)
		{
			return;
		}

		string cleanVictim = XjStringHelper.DisplayNameWithoutRealmSuffix(victimName, "族人");
		string cleanOffender = XjStringHelper.DisplayNameWithoutRealmSuffix(offenderName, "外敌");
		string cleanXianJi = string.IsNullOrWhiteSpace(xianJi) ? "神通" : xianJi.Trim();
		for (int i = 0; i < Pending.Count; i++)
		{
			XjFamilyVendettaArchiveRecord existing = Pending[i];
			if (existing != null
				&& existing.VictimFamilyId == victimFamilyId
				&& existing.VictimActorId == victimActorId
				&& existing.OffenderActorId == offenderActorId)
			{
				return;
			}
		}

		if (Pending.Count >= MaxPendingVendettas)
		{
			RemovePendingAt(0);
		}

		AddPending(new XjFamilyVendettaArchiveRecord
		{
			VictimFamilyId = victimFamilyId,
			VictimActorId = victimActorId,
			VictimName = cleanVictim,
			OffenderFamilyId = Math.Max(0L, offenderFamilyId),
			OffenderActorId = offenderActorId,
			OffenderName = cleanOffender,
			XianJi = cleanXianJi,
			CreatedYear = Math.Max(0, year),
			// 普通族人寻仇仍至少等待十年；若本族尚有真正金丹/真君羽士且仇首仍只是紫府，
			// 则另走3~6年的高境“问罪”事件，不受这一普通冷却限制。
			LastAttemptYear = Math.Max(0, year),
			AttemptCount = 0
		});

		// Pending 本身就是家族层真实仇恨债：进入年度寻仇候选、战力门槛与跨宗敌意，
		// 不是只写一句史册文本。受害者家族ID在立人丹计划时已快照，死亡后不会因成员索引清理而漏债。
		string vendettaSummary = string.IsNullOrWhiteSpace(detail)
			? cleanOffender + "将" + cleanVictim + "炼作人丹，以其根基残神续成" + cleanXianJi + "。其家族由此记下人丹血债，后世可循债寻仇。"
			: detail.Trim();
		AppendFamilyChronicle(
			victimFamilyId,
			victimActorId,
			XjChronicleEventTypes.FamilyVendettaCreated,
			year,
			"血仇记名",
			vendettaSummary,
			4,
			"vendetta.rendan",
			offenderActorId);
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			"FamilyVendettaCreated",
			year,
			victimFamilyId,
			4,
			vendettaSummary,
			victimActorId,
			cleanVictim);

		if (TryResolveCrossSectPair(victimFamilyId, offenderFamilyId, out long victimSectId, out long offenderSectId))
		{
			XjSectWarSystem.AddHostility(
				victimSectId,
				offenderSectId,
				35,
				year,
				"跨宗家族结下人丹血债");
		}

		MarkChanged();
	}

	internal static bool HasPendingAnnualWorkForYear(int currentYear)
	{
		return currentYear > 0 && _annualYear == currentYear && _annualPhase != AnnualPhase.None;
	}

	internal static bool BeginAnnualWork(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || Pending.Count == 0) return false;
		if (HasPendingAnnualWorkForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		AnnualPendingSnapshot.AddRange(Pending);
		_annualYear = currentYear;
		_annualPhase = AnnualPhase.CollectCandidates;
		return true;
	}

	internal static bool TickPendingAnnualWork(
		int itemBudget,
		double timeBudgetMs,
		out bool changed)
	{
		changed = _annualChanged;
		if (_annualYear <= 0 || _annualPhase == AnnualPhase.None) return true;

		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_annualPhase != AnnualPhase.None && !budget.ShouldYield)
		{
			if (_annualPhase == AnnualPhase.CollectCandidates)
			{
				while (_annualIndex < AnnualPendingSnapshot.Count && budget.TryTake())
				{
					XjFamilyVendettaArchiveRecord entry = AnnualPendingSnapshot[_annualIndex++];
					if (entry == null || entry.VictimFamilyId <= 0L || entry.OffenderActorId <= 0L) continue;
					bool normalVendettaDue = _annualYear - Math.Max(entry.CreatedYear, entry.LastAttemptYear) >= RevengeCooldownYears;
					bool highRealmWindowOpen = _annualYear - entry.CreatedYear >= JinDanRetributionMinDelayYears;
					if (normalVendettaDue || highRealmWindowOpen) CandidateBuffer.Add(entry);
				}
				if (_annualIndex < AnnualPendingSnapshot.Count)
				{
					changed = _annualChanged;
					return false;
				}
				CandidateBuffer.Sort(CompareAnnualCandidate);
				_annualIndex = 0;
				_annualPhase = AnnualPhase.ProcessCandidates;
				continue;
			}

			if (_annualPhase == AnnualPhase.ProcessCandidates)
			{
				while (_annualIndex < CandidateBuffer.Count
					&& _annualProcessed < MaxAnnualRevengeEvents
					&& budget.TryTake())
				{
					ProcessAnnualCandidate(CandidateBuffer[_annualIndex++], _annualYear);
				}
				if (_annualIndex < CandidateBuffer.Count && _annualProcessed < MaxAnnualRevengeEvents)
				{
					changed = _annualChanged;
					return false;
				}
				bool finalChanged = _annualChanged;
				if (finalChanged) MarkChanged();
				ClearPendingAnnualWork();
				changed = finalChanged;
				return true;
			}
		}

		changed = _annualChanged;
		return false;
	}

	private static int CompareAnnualCandidate(XjFamilyVendettaArchiveRecord left, XjFamilyVendettaArchiveRecord right)
	{
		int attemptYear = left.LastAttemptYear.CompareTo(right.LastAttemptYear);
		if (attemptYear != 0) return attemptYear;
		int createdYear = left.CreatedYear.CompareTo(right.CreatedYear);
		if (createdYear != 0) return createdYear;
		return left.OffenderActorId.CompareTo(right.OffenderActorId);
	}

	private static void ProcessAnnualCandidate(XjFamilyVendettaArchiveRecord entry, int currentYear)
	{
		if (entry == null || !PendingMembership.Contains(entry)) return;
		if (!TryResolveLivingActor(entry.OffenderActorId, out Actor offender))
		{
			if (currentYear - entry.CreatedYear >= UnresolvedActorGraceYears)
			{
				CloseWithoutRevenge(entry, currentYear);
				RemovePending(entry);
				_annualChanged = true;
			}
			return;
		}

		if (TryResolveJinDanRetribution(entry, offender, currentYear))
		{
			_annualChanged = true;
			_annualProcessed++;
			return;
		}

		if (currentYear - Math.Max(entry.CreatedYear, entry.LastAttemptYear) < RevengeCooldownYears) return;
		if (!TrySelectAvenger(entry, offender, currentYear, out Actor avenger, out Actor manipulator)) return;
		entry.LastAttemptYear = currentYear;
		entry.AttemptCount = Math.Max(0, entry.AttemptCount) + 1;
		ResolveRevenge(entry, avenger, offender, manipulator, currentYear);
		_annualChanged = true;
		_annualProcessed++;
	}

	internal static void ClearPendingAnnualWork()
	{
		AnnualPendingSnapshot.Clear();
		CandidateBuffer.Clear();
		_annualPhase = AnnualPhase.None;
		_annualYear = 0;
		_annualIndex = 0;
		_annualProcessed = 0;
		_annualChanged = false;
	}

	internal static bool TickYear(int currentYear)
	{
		ClearPendingAnnualWork();
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || Pending.Count == 0)
		{
			return false;
		}

		CandidateBuffer.Clear();
		for (int i = 0; i < Pending.Count; i++)
		{
			XjFamilyVendettaArchiveRecord entry = Pending[i];
			if (entry == null
				|| entry.VictimFamilyId <= 0L
				|| entry.OffenderActorId <= 0L)
			{
				continue;
			}

			bool normalVendettaDue = currentYear - Math.Max(entry.CreatedYear, entry.LastAttemptYear) >= RevengeCooldownYears;
			bool highRealmWindowOpen = currentYear - entry.CreatedYear >= JinDanRetributionMinDelayYears;
			if (!normalVendettaDue && !highRealmWindowOpen) continue;
			CandidateBuffer.Add(entry);
		}

		CandidateBuffer.Sort(CompareAnnualCandidate);

		bool changed = false;
		int processed = 0;
		for (int i = 0; i < CandidateBuffer.Count && processed < MaxAnnualRevengeEvents; i++)
		{
			XjFamilyVendettaArchiveRecord entry = CandidateBuffer[i];
			if (!PendingMembership.Contains(entry))
			{
				continue;
			}

			if (!TryResolveLivingActor(entry.OffenderActorId, out Actor offender))
			{
				if (currentYear - entry.CreatedYear >= UnresolvedActorGraceYears)
				{
					CloseWithoutRevenge(entry, currentYear);
					RemovePending(entry);
					changed = true;
				}
				continue;
			}

			// 金丹对紫府不是“再打一场随机战斗”，而是高位问罪。只要受害者本族
			// 尚有真正金丹级上修，延迟数年后直接结算仇首死亡；闭关与地图距离
			// 都不能让人丹血债永久逃过高境报应。
			if (TryResolveJinDanRetribution(entry, offender, currentYear))
			{
				changed = true;
				processed++;
				continue;
			}

			if (currentYear - Math.Max(entry.CreatedYear, entry.LastAttemptYear) < RevengeCooldownYears)
			{
				continue;
			}

			if (!TrySelectAvenger(entry, offender, currentYear, out Actor avenger, out Actor manipulator))
			{
				continue;
			}

			entry.LastAttemptYear = currentYear;
			entry.AttemptCount = Math.Max(0, entry.AttemptCount) + 1;
			ResolveRevenge(entry, avenger, offender, manipulator, currentYear);
			changed = true;
			processed++;
		}

		CandidateBuffer.Clear();
		if (changed)
		{
			MarkChanged();
		}
		return changed;
	}

	internal static void TryResolveByDeathSnapshot(XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L || snapshot.LastAttackerId <= 0L || Pending.Count == 0)
		{
			return;
		}

		if (!XjFamilyReadModel.Shared.TryGetFamilyStableId(snapshot.LastAttackerId, out long attackerFamilyId)
			|| attackerFamilyId <= 0L)
		{
			return;
		}

		bool changed = false;
		for (int i = Pending.Count - 1; i >= 0; i--)
		{
			XjFamilyVendettaArchiveRecord entry = Pending[i];
			if (entry == null || entry.OffenderActorId != snapshot.ActorId || entry.VictimFamilyId != attackerFamilyId)
			{
				continue;
			}

			string killerName = XjStringHelper.DisplayNameWithoutRealmSuffix(snapshot.LastAttackerName, "族人");
			string offenderName = XjStringHelper.DisplayNameWithoutRealmSuffix(
				string.IsNullOrWhiteSpace(entry.OffenderName) ? snapshot.Name : entry.OffenderName,
				"外敌");
			string body = killerName + "斩杀" + offenderName + "，为" + entry.VictimName + "讨还人丹血债。";
			AppendFamilyChronicle(
				entry.VictimFamilyId,
				entry.VictimActorId,
				XjChronicleEventTypes.FamilyVendettaAvenged,
				snapshot.Year,
				"血仇得雪",
				body,
				5,
				"vendetta.avenged",
				entry.OffenderActorId);
			RecordWorldHistory("血仇得雪", body, 5, entry.VictimFamilyId, snapshot.LastAttackerId, killerName, snapshot.Year);
			AddResultHostility(entry, 15, snapshot.Year, "跨宗血仇在实战中得雪");
			RemovePendingAt(i);
			changed = true;
		}

		if (changed)
		{
			MarkChanged();
		}
	}

	internal static void ExportArchiveRecords(List<XjFamilyVendettaArchiveRecord> target)
	{
		if (target == null)
		{
			return;
		}
		target.Clear();
		for (int i = 0; i < Pending.Count; i++)
		{
			XjFamilyVendettaArchiveRecord entry = Normalize(Clone(Pending[i]));
			if (entry != null)
			{
				target.Add(entry);
			}
		}
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjFamilyVendettaArchiveRecord> source)
	{
		ClearPendingAnnualWork();
		ClearPendingRecords();
		if (source == null)
		{
			return;
		}
		for (int i = 0; i < source.Count && Pending.Count < MaxPendingVendettas; i++)
		{
			XjFamilyVendettaArchiveRecord entry = Normalize(Clone(source[i]));
			if (entry == null || entry.VictimFamilyId <= 0L || entry.OffenderActorId <= 0L)
			{
				continue;
			}
			AddPending(entry);
		}
	}

	internal static void Clear()
	{
		ClearPendingAnnualWork();
		ClearPendingRecords();
	}

	private static bool TryResolveJinDanRetribution(
		XjFamilyVendettaArchiveRecord entry,
		Actor offender,
		int currentYear)
	{
		if (entry == null || !IsUsableActor(offender)
			|| XjRealmSuppression.GetRealmTier(offender) != XjRealmSuppression.TierZiFu)
		{
			return false;
		}

		int dueYear = ResolveJinDanRetributionDueYear(entry);
		if (currentYear < dueYear
			|| !TrySelectFamilyJinDanExecutioner(entry.VictimFamilyId, entry.OffenderActorId, out Actor executioner))
		{
			return false;
		}

		long executionerId = XjSectCityData.GetActorId(executioner);
		long offenderId = XjSectCityData.GetActorId(offender);
		if (executionerId <= 0L || offenderId <= 0L || executionerId == offenderId) return false;

		if (!XjVanillaDeathGuard.TryExecuteForceDeath(offender, (AttackType)VendettaAttackType, true))
		{
			return false;
		}

		string executionerName = SafeActorName(executioner, "族中真君");
		string offenderName = SafeActorName(offender, entry.OffenderName);
		string victimName = XjStringHelper.DisplayNameWithoutRealmSuffix(entry.VictimName, "族人");
		string realmName = ResolveExecutionerRealmName(executioner);
		string body = "昔年" + offenderName + "将" + victimName + "炼作人丹，夺其根基续己神通。血债沉了数载，"
			+ executionerName + "所在洞天忽开一线天光；其人不出山门，只以" + realmName + "位格隔世点名。"
			+ offenderName + "一身紫府道基先碎、法身继灭，神魂最后散尽，连遁逃求生的余地也未留下。"
			+ "自此天下皆知：下修之间尚可论胜负，敢以人丹噬其族人，便要等上修亲自来收这笔血账。";
		string announcement = "【金丹问罪·隔世削名】" + body;

		AppendFamilyChronicle(
			entry.VictimFamilyId,
			entry.VictimActorId,
			XjChronicleEventTypes.FamilyVendettaAvenged,
			currentYear,
			"金丹问罪",
			body,
			6,
			"vendetta.jindan_retribution",
			offenderId);
		RecordWorldHistory("金丹问罪", body, 6, entry.VictimFamilyId, executionerId, executionerName, currentYear);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			announcement,
			XjAnnouncementCategory.HighRealm,
			duration: 8.5f,
			color: "#E8C36A",
			delayFrames: 1,
			iconId: XjEventIconCatalog.RenDan);
		AddResultHostility(entry, 30, currentYear, "金丹亲自清算人丹血债");
		RemoveResolvedEntries(entry.VictimFamilyId, offenderId);
		return true;
	}

	private static int ResolveJinDanRetributionDueYear(XjFamilyVendettaArchiveRecord entry)
	{
		int span = Math.Max(1, JinDanRetributionMaxDelayYears - JinDanRetributionMinDelayYears + 1);
		long seed = Math.Max(1L, entry?.VictimActorId ?? 0L)
			+ Math.Max(1L, entry?.OffenderActorId ?? 0L) * 31L
			+ Math.Max(1, entry?.CreatedYear ?? 0) * 131L;
		int delay = JinDanRetributionMinDelayYears
			+ XjDeterministicHash.PositiveIndex(seed, "vendetta.jindan.delay", span);
		long due = (long)Math.Max(0, entry?.CreatedYear ?? 0) + delay;
		return due > int.MaxValue ? int.MaxValue : (int)due;
	}

	private static bool TrySelectFamilyJinDanExecutioner(long familyId, long excludedActorId, out Actor selected)
	{
		selected = null;
		float bestPower = -1f;
		long bestId = long.MaxValue;
		int scanned = 0;
		foreach (Actor actor in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (scanned++ >= 256) break;
			if (!IsUsableActor(actor)) continue;
			long actorId = XjSectCityData.GetActorId(actor);
			if (actorId <= 0L || actorId == excludedActorId || !IsIndependentJinDanOrHigher(actor)) continue;

			float power = XjRankMetrics.Build(actor).Power;
			if (power > bestPower || (Math.Abs(power - bestPower) < 0.001f && actorId < bestId))
			{
				selected = actor;
				bestPower = power;
				bestId = actorId;
			}
		}
		return selected != null;
	}

	private static bool IsIndependentJinDanOrHigher(Actor actor)
	{
		if (actor?.data == null) return false;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)) return false;
		return XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan;
	}

	private static string ResolveExecutionerRealmName(Actor actor)
	{
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return "真君羽士";
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return "道胎";
		return "金丹";
	}

	private static void ResolveRevenge(
		XjFamilyVendettaArchiveRecord entry,
		Actor avenger,
		Actor offender,
		Actor manipulator,
		int currentYear)
	{
		long avengerId = XjSectCityData.GetActorId(avenger);
		long offenderId = XjSectCityData.GetActorId(offender);
		if (avengerId <= 0L || offenderId <= 0L || avengerId == offenderId)
		{
			return;
		}
		string avengerName = SafeActorName(avenger, "族人");
		string offenderName = SafeActorName(offender, entry.OffenderName);
		float avengerPower = Math.Max(1f, XjRankMetrics.Build(avenger).Power);
		float offenderPower = Math.Max(1f, XjRankMetrics.Build(offender).Power);
		ResolveProbabilities(avengerPower, offenderPower, out int success, out int failure, out _);
		float roll = XjDeterministicHash.Roll01(
			avengerId,
			currentYear,
			"FamilyVendetta",
			offenderId.ToString(CultureInfo.InvariantCulture) + "|" + entry.AttemptCount.ToString(CultureInfo.InvariantCulture));
		int result = roll < success / 100f ? 0 : roll < (success + failure) / 100f ? 1 : 2;

		bool manipulated = manipulator?.data != null && manipulator.isAlive();
		string manipulationPrefix = manipulated
			? SafeActorName(manipulator, "上修") + "借族中旧怨授意" + avengerName + "出手，"
			: string.Empty;

		if (manipulated && TryResolveCrossSectPair(entry.VictimFamilyId, entry.OffenderFamilyId, out long sourceSect, out long targetSect))
		{
			XjSectWarSystem.AddHostility(sourceSect, targetSect, 5, currentYear, "上修借家族血债授意门下寻仇");
		}

		if (result == 0 && XjVanillaDeathGuard.TryExecuteForceDeath(offender, (AttackType)VendettaAttackType, true))
		{
			string body = manipulationPrefix + avengerName + "寻得" + offenderName + "，战力占优，一举斩杀，为" + entry.VictimName + "讨还人丹血债。";
			AppendFamilyChronicle(entry.VictimFamilyId, entry.VictimActorId, XjChronicleEventTypes.FamilyVendettaAvenged,
				currentYear, "血仇得雪", body, 5, manipulated ? "vendetta.manipulated.avenged" : "vendetta.event.avenged", offenderId);
			RecordWorldHistory("血仇得雪", body, 5, entry.VictimFamilyId, avengerId, avengerName, currentYear);
			AddResultHostility(entry, 15, currentYear, "跨宗寻仇得手");
			RemoveResolvedEntries(entry.VictimFamilyId, offenderId);
			return;
		}

		if (result == 2 && XjVanillaDeathGuard.TryExecuteForceDeath(avenger, (AttackType)VendettaAttackType, true))
		{
			string body = manipulationPrefix + avengerName + "寻仇不成，反被" + offenderName + "所杀，旧债未雪，又添新恨。";
			AppendFamilyChronicle(entry.VictimFamilyId, entry.VictimActorId, XjChronicleEventTypes.FamilyVendettaCounterKilled,
				currentYear, "寻仇反殒", body, 4, manipulated ? "vendetta.manipulated.counterkill" : "vendetta.counterkill", offenderId);
			RecordWorldHistory("寻仇反殒", body, 4, entry.VictimFamilyId, avengerId, avengerName, currentYear);
			AddResultHostility(entry, 25, currentYear, "跨宗寻仇遭反杀");
			return;
		}

		string failedBody = manipulationPrefix + avengerName + "谋取" + offenderName + "未果，双方均未身死，血债仍待来日。";
		AppendFamilyChronicle(entry.VictimFamilyId, entry.VictimActorId, XjChronicleEventTypes.FamilyVendettaFailed,
			currentYear, "寻仇未果", failedBody, 2, manipulated ? "vendetta.manipulated.failed" : "vendetta.failed", offenderId);
		RecordWorldHistory("寻仇未果", failedBody, 2, entry.VictimFamilyId, avengerId, avengerName, currentYear);
		AddResultHostility(entry, 5, currentYear, "跨宗寻仇未果");
	}

	private static bool TrySelectAvenger(
		XjFamilyVendettaArchiveRecord entry,
		Actor offender,
		int currentYear,
		out Actor avenger,
		out Actor manipulator)
	{
		avenger = null;
		manipulator = null;
		if (entry == null || entry.VictimFamilyId <= 0L || !IsUsableActor(offender))
		{
			return false;
		}

		bool canManipulate = TryResolveCrossSectPair(entry.VictimFamilyId, entry.OffenderFamilyId, out long victimSectId, out long offenderSectId)
			&& XjSectWarSystem.GetHostility(victimSectId, offenderSectId) >= ManipulationHostilityThreshold
			&& XjDeterministicHash.PositiveIndex(
				entry.VictimActorId,
				"vendetta.manipulation|" + entry.OffenderActorId.ToString(CultureInfo.InvariantCulture) + "|" + currentYear.ToString(CultureInfo.InvariantCulture),
				100) < ManipulationChancePercent
			&& TrySelectHighRealmManipulator(victimSectId, out manipulator);

		if (canManipulate && TrySelectLowerRealmProxy(entry.VictimFamilyId, entry.OffenderActorId, offender, out avenger))
		{
			return true;
		}

		manipulator = null;
		return TrySelectStrongestFamilyMember(entry.VictimFamilyId, entry.OffenderActorId, offender, out avenger);
	}

	private static bool TrySelectStrongestFamilyMember(long familyId, long excludedActorId, Actor offender, out Actor selected)
	{
		selected = null;
		float bestPower = -1f;
		long bestId = long.MaxValue;
		int scanned = 0;
		foreach (Actor actor in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (scanned++ >= 256) break;
			if (!IsUsableActor(actor))
			{
				continue;
			}
			long actorId = XjSectCityData.GetActorId(actor);
			if (actorId == excludedActorId || !CanAttemptVendetta(actor, offender))
			{
				continue;
			}
			float power = XjRankMetrics.Build(actor).Power;
			if (power > bestPower || (Math.Abs(power - bestPower) < 0.001f && actorId < bestId))
			{
				selected = actor;
				bestPower = power;
				bestId = actorId;
			}
		}
		return selected != null;
	}

	private static bool TrySelectLowerRealmProxy(long familyId, long excludedActorId, Actor offender, out Actor selected)
	{
		selected = null;
		int bestPreference = int.MinValue;
		float bestPower = -1f;
		long bestId = long.MaxValue;
		int scanned = 0;
		foreach (Actor actor in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (scanned++ >= 256) break;
			if (!IsUsableActor(actor))
			{
				continue;
			}
			long actorId = XjSectCityData.GetActorId(actor);
			if (actorId == excludedActorId || !CanAttemptVendetta(actor, offender))
			{
				continue;
			}
			int tier = XjRealmSuppression.GetRealmTier(actor);
			int preference = tier == XjRealmSuppression.TierZhuJi ? 2
				: tier == XjRealmSuppression.TierLianQi ? 1
				: 0;
			if (preference <= 0)
			{
				continue;
			}
			float power = XjRankMetrics.Build(actor).Power;
			if (preference > bestPreference
				|| (preference == bestPreference && power > bestPower)
				|| (preference == bestPreference && Math.Abs(power - bestPower) < 0.001f && actorId < bestId))
			{
				selected = actor;
				bestPreference = preference;
				bestPower = power;
				bestId = actorId;
			}
		}
		return selected != null;
	}

	private static bool TrySelectHighRealmManipulator(long sectId, out Actor selected)
	{
		selected = null;
		if (sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null)
		{
			return false;
		}

		float bestPower = -1f;
		long bestId = long.MaxValue;
		if (TryResolveLivingActor(sect.SovereignActorId, out Actor sovereign)
			&& XjRealmSuppression.GetRealmTier(sovereign) >= XjRealmSuppression.TierZiFu)
		{
			selected = sovereign;
			bestPower = XjRankMetrics.Build(sovereign).Power;
			bestId = XjSectCityData.GetActorId(sovereign);
		}

		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		if (actorIds == null)
		{
			return selected != null;
		}
		int scanned = 0;
		for (int i = 0; i < actorIds.Count && scanned < 64; i++)
		{
			scanned++;
			if (!TryResolveLivingActor(actorIds[i], out Actor actor)
				|| XjSectRepository.ResolveActorSectId(actor) != sectId
				|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu)
			{
				continue;
			}
			float power = XjRankMetrics.Build(actor).Power;
			long actorId = XjSectCityData.GetActorId(actor);
			if (power > bestPower || (Math.Abs(power - bestPower) < 0.001f && actorId < bestId))
			{
				selected = actor;
				bestPower = power;
				bestId = actorId;
			}
		}
		return selected != null;
	}

	private static bool CanAttemptVendetta(Actor candidate, Actor offender)
	{
		if (!IsUsableActor(candidate) || !IsUsableActor(offender))
		{
			return false;
		}

		int candidateTier = XjRealmSuppression.GetRealmTier(candidate);
		int offenderTier = XjRealmSuppression.GetRealmTier(offender);
		// 低境不得因抽签或上修授意去送死式寻仇。紫府绝不主动挑战
		// 金丹；同境也必须至少具备可实际交锋的战力。
		if (candidateTier < offenderTier)
		{
			return false;
		}

		float candidatePower = Math.Max(1f, XjRankMetrics.Build(candidate).Power);
		float offenderPower = Math.Max(1f, XjRankMetrics.Build(offender).Power);
		return candidatePower >= offenderPower * MinimumVendettaPowerRatio;
	}

	private static void ResolveProbabilities(float avengerPower, float offenderPower, out int success, out int failure, out int counterKill)
	{
		avengerPower = Math.Max(1f, avengerPower);
		offenderPower = Math.Max(1f, offenderPower);
		if (offenderPower >= avengerPower * 2f)
		{
			success = 5;
			failure = 25;
			counterKill = 70;
			return;
		}
		if (avengerPower >= offenderPower * 2f)
		{
			success = 85;
			failure = 10;
			counterKill = 5;
			return;
		}
		float ratio = avengerPower / offenderPower;
		if (ratio >= 0.8f && ratio <= 1.2f)
		{
			success = 45;
			failure = 35;
			counterKill = 20;
			return;
		}
		if (avengerPower > offenderPower)
		{
			success = 70;
			failure = 20;
			counterKill = 10;
			return;
		}
		success = 20;
		failure = 40;
		counterKill = 40;
	}

	private static void RemoveResolvedEntries(long victimFamilyId, long offenderActorId)
	{
		for (int i = Pending.Count - 1; i >= 0; i--)
		{
			XjFamilyVendettaArchiveRecord pending = Pending[i];
			if (pending != null
				&& pending.VictimFamilyId == victimFamilyId
				&& pending.OffenderActorId == offenderActorId)
			{
				RemovePendingAt(i);
			}
		}
	}

	private static void AddPending(XjFamilyVendettaArchiveRecord entry)
	{
		if (entry == null || !PendingMembership.Add(entry)) return;
		Pending.Add(entry);
	}

	private static bool RemovePending(XjFamilyVendettaArchiveRecord entry)
	{
		if (entry == null || !PendingMembership.Remove(entry)) return false;
		return Pending.Remove(entry);
	}

	private static void RemovePendingAt(int index)
	{
		if ((uint)index >= (uint)Pending.Count) return;
		XjFamilyVendettaArchiveRecord entry = Pending[index];
		Pending.RemoveAt(index);
		if (entry != null) PendingMembership.Remove(entry);
	}

	private static void ClearPendingRecords()
	{
		Pending.Clear();
		PendingMembership.Clear();
	}

	private static void CloseWithoutRevenge(XjFamilyVendettaArchiveRecord entry, int currentYear)
	{
		string offenderName = string.IsNullOrWhiteSpace(entry.OffenderName) ? "仇首" : entry.OffenderName.Trim();
		string body = offenderName + "已不在世间，" + entry.VictimName + "之人丹血债未由本族亲手讨还，自此不再发动角色级寻仇。";
		AppendFamilyChronicle(entry.VictimFamilyId, entry.VictimActorId, XjChronicleEventTypes.FamilyVendettaClosed,
			currentYear, "仇首已殁", body, 2, "vendetta.target.gone", entry.OffenderActorId);
	}

	private static void AddResultHostility(XjFamilyVendettaArchiveRecord entry, int amount, int currentYear, string reason)
	{
		if (entry != null
			&& TryResolveCrossSectPair(entry.VictimFamilyId, entry.OffenderFamilyId, out long victimSectId, out long offenderSectId))
		{
			XjSectWarSystem.AddHostility(victimSectId, offenderSectId, amount, currentYear, reason);
		}
	}

	private static bool TryResolveCrossSectPair(long victimFamilyId, long offenderFamilyId, out long victimSectId, out long offenderSectId)
	{
		victimSectId = 0L;
		offenderSectId = 0L;
		return victimFamilyId > 0L
			&& offenderFamilyId > 0L
			&& XjSectRepository.TryResolveFamilySectId(victimFamilyId, out victimSectId)
			&& XjSectRepository.TryResolveFamilySectId(offenderFamilyId, out offenderSectId)
			&& victimSectId > 0L
			&& offenderSectId > 0L
			&& victimSectId != offenderSectId;
	}

	private static bool TryResolveLivingActor(long actorId, out Actor actor)
	{
		actor = null;
		return actorId > 0L
			&& XjScheduler.ResolveActor(actorId, out actor)
			&& IsUsableActor(actor);
	}

	private static bool IsUsableActor(Actor actor)
	{
		return actor?.data != null && actor.isAlive() && XjSectCityData.GetActorId(actor) > 0L;
	}

	private static string SafeActorName(Actor actor, string fallback)
	{
		if (actor?.data == null)
		{
			return string.IsNullOrWhiteSpace(fallback) ? "无名修士" : fallback.Trim();
		}
		try
		{
			return XjStringHelper.DisplayNameWithoutRealmSuffix(actor.getName(), string.IsNullOrWhiteSpace(fallback) ? "无名修士" : fallback.Trim());
		}
		catch
		{
			return string.IsNullOrWhiteSpace(fallback) ? "无名修士" : fallback.Trim();
		}
	}

	private static void AppendFamilyChronicle(
		long familyId,
		long actorId,
		string eventType,
		int year,
		string title,
		string body,
		int importance,
		string source,
		long relatedActorId)
	{
		XjChronicleEvent chronicleEvent = new XjChronicleEvent(
			true,
			familyId,
			actorId,
			eventType,
			year,
			title,
			body,
			importance,
			importance >= 4,
			false,
			false,
			"Ok",
			source);
		string eventKey = familyId.ToString(CultureInfo.InvariantCulture)
			+ "|" + actorId.ToString(CultureInfo.InvariantCulture)
			+ "|" + relatedActorId.ToString(CultureInfo.InvariantCulture)
			+ "|" + eventType
			+ "|" + year.ToString(CultureInfo.InvariantCulture);
		XjFamilyChronicleMemory.Shared.Append(chronicleEvent, eventKey);
	}

	private static void RecordWorldHistory(
		string title,
		string body,
		int importance,
		long familyId,
		long actorId,
		string actorName,
		int year)
	{
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Family,
			title,
			body,
			importance,
			importance >= 4,
			actorId: actorId,
			actorName: actorName,
			familyId: familyId,
			year: year);
		string eventType = (string.Equals(title, "血仇得雪", StringComparison.Ordinal)
			|| string.Equals(title, "金丹问罪", StringComparison.Ordinal)) ? "FamilyVendettaAvenged"
			: string.Equals(title, "寻仇反殒", StringComparison.Ordinal) ? "FamilyVendettaCounterKilled"
			: string.Equals(title, "寻仇未果", StringComparison.Ordinal) ? "FamilyVendettaFailed"
			: "FamilyVendetta";
		XjCenturyAnnalsStore.ObserveFamilyEvent(
			eventType,
			year,
			familyId,
			importance,
			body,
			actorId,
			actorName);
	}

	private static XjFamilyVendettaArchiveRecord Normalize(XjFamilyVendettaArchiveRecord entry)
	{
		if (entry == null)
		{
			return null;
		}
		entry.VictimFamilyId = Math.Max(0L, entry.VictimFamilyId);
		entry.VictimActorId = Math.Max(0L, entry.VictimActorId);
		entry.OffenderFamilyId = Math.Max(0L, entry.OffenderFamilyId);
		entry.OffenderActorId = Math.Max(0L, entry.OffenderActorId);
		entry.VictimName = XjStringHelper.DisplayNameWithoutRealmSuffix(entry.VictimName, "族人");
		entry.OffenderName = XjStringHelper.DisplayNameWithoutRealmSuffix(entry.OffenderName, "外敌");
		entry.XianJi = string.IsNullOrWhiteSpace(entry.XianJi) ? "神通" : entry.XianJi.Trim();
		entry.CreatedYear = Math.Max(0, entry.CreatedYear);
		entry.LastAttemptYear = entry.LastAttemptYear > 0 ? entry.LastAttemptYear : entry.CreatedYear;
		entry.AttemptCount = Math.Max(0, entry.AttemptCount);
		return entry;
	}

	private static XjFamilyVendettaArchiveRecord Clone(XjFamilyVendettaArchiveRecord entry)
	{
		if (entry == null)
		{
			return null;
		}
		return new XjFamilyVendettaArchiveRecord
		{
			VictimFamilyId = entry.VictimFamilyId,
			VictimActorId = entry.VictimActorId,
			VictimName = entry.VictimName ?? string.Empty,
			OffenderFamilyId = entry.OffenderFamilyId,
			OffenderActorId = entry.OffenderActorId,
			OffenderName = entry.OffenderName ?? string.Empty,
			XianJi = entry.XianJi ?? string.Empty,
			CreatedYear = entry.CreatedYear,
			LastAttemptYear = entry.LastAttemptYear,
			AttemptCount = entry.AttemptCount
		};
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Conflict | XjCodexDirtyFlags.History);
	}
}
