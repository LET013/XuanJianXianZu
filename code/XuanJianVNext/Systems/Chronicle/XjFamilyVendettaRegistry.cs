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
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Rank;

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
	private const int UnresolvedActorGraceYears = 50;
	private const int ManipulationHostilityThreshold = 50;
	private const int ManipulationChancePercent = 35;
	private const int VendettaAttackType = 5;

	private static readonly List<XjFamilyVendettaArchiveRecord> Pending = new List<XjFamilyVendettaArchiveRecord>();
	private static readonly List<XjFamilyVendettaArchiveRecord> CandidateBuffer = new List<XjFamilyVendettaArchiveRecord>();

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
			Pending.RemoveAt(0);
		}

		Pending.Add(new XjFamilyVendettaArchiveRecord
		{
			VictimFamilyId = victimFamilyId,
			VictimActorId = victimActorId,
			VictimName = cleanVictim,
			OffenderFamilyId = Math.Max(0L, offenderFamilyId),
			OffenderActorId = offenderActorId,
			OffenderName = cleanOffender,
			XianJi = cleanXianJi,
			CreatedYear = Math.Max(0, year),
			// 首次报复至少等待十年，避免人丹事件同年立刻再杀一人。
			LastAttemptYear = Math.Max(0, year),
			AttemptCount = 0
		});

		string vendettaSummary = string.IsNullOrWhiteSpace(detail)
			? cleanOffender + "暗中扶持" + cleanVictim + "修至筑基，继而将其炼作人丹，施展续途妙法，残神化入" + cleanXianJi + "。族中记此血仇，待后来者偿还。"
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

	internal static bool TickYear(int currentYear)
	{
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
				|| entry.OffenderActorId <= 0L
				|| currentYear - Math.Max(entry.CreatedYear, entry.LastAttemptYear) < RevengeCooldownYears)
			{
				continue;
			}
			CandidateBuffer.Add(entry);
		}

		CandidateBuffer.Sort((left, right) =>
		{
			int attemptYear = left.LastAttemptYear.CompareTo(right.LastAttemptYear);
			if (attemptYear != 0) return attemptYear;
			int createdYear = left.CreatedYear.CompareTo(right.CreatedYear);
			if (createdYear != 0) return createdYear;
			return left.OffenderActorId.CompareTo(right.OffenderActorId);
		});

		bool changed = false;
		int processed = 0;
		for (int i = 0; i < CandidateBuffer.Count && processed < MaxAnnualRevengeEvents; i++)
		{
			XjFamilyVendettaArchiveRecord entry = CandidateBuffer[i];
			if (!Pending.Contains(entry))
			{
				continue;
			}

			if (!TryResolveLivingActor(entry.OffenderActorId, out Actor offender))
			{
				if (currentYear - entry.CreatedYear >= UnresolvedActorGraceYears)
				{
					CloseWithoutRevenge(entry, currentYear);
					Pending.Remove(entry);
					changed = true;
				}
				continue;
			}

			if (!TrySelectAvenger(entry, currentYear, out Actor avenger, out Actor manipulator))
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
			Pending.RemoveAt(i);
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
		Pending.Clear();
		CandidateBuffer.Clear();
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
			Pending.Add(entry);
		}
	}

	internal static void Clear()
	{
		Pending.Clear();
		CandidateBuffer.Clear();
	}

	private static void ResolveRevenge(
		XjFamilyVendettaArchiveRecord entry,
		Actor avenger,
		Actor offender,
		Actor manipulator,
		int currentYear)
	{
		long avengerId = XjZongMenCityData.GetActorId(avenger);
		long offenderId = XjZongMenCityData.GetActorId(offender);
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
		int currentYear,
		out Actor avenger,
		out Actor manipulator)
	{
		avenger = null;
		manipulator = null;
		if (entry == null || entry.VictimFamilyId <= 0L)
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

		if (canManipulate && TrySelectLowerRealmProxy(entry.VictimFamilyId, entry.OffenderActorId, out avenger))
		{
			return true;
		}

		manipulator = null;
		return TrySelectStrongestFamilyMember(entry.VictimFamilyId, entry.OffenderActorId, out avenger);
	}

	private static bool TrySelectStrongestFamilyMember(long familyId, long excludedActorId, out Actor selected)
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
			long actorId = XjZongMenCityData.GetActorId(actor);
			if (actorId == excludedActorId)
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

	private static bool TrySelectLowerRealmProxy(long familyId, long excludedActorId, out Actor selected)
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
			long actorId = XjZongMenCityData.GetActorId(actor);
			if (actorId == excludedActorId)
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
			bestId = XjZongMenCityData.GetActorId(sovereign);
		}

		IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(sectId);
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
			long actorId = XjZongMenCityData.GetActorId(actor);
			if (power > bestPower || (Math.Abs(power - bestPower) < 0.001f && actorId < bestId))
			{
				selected = actor;
				bestPower = power;
				bestId = actorId;
			}
		}
		return selected != null;
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
				Pending.RemoveAt(i);
			}
		}
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
		return actor?.data != null && actor.isAlive() && XjZongMenCityData.GetActorId(actor) > 0L;
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
		string eventType = string.Equals(title, "血仇得雪", StringComparison.Ordinal) ? "FamilyVendettaAvenged"
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
