using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectWarSystem
{
	private const int MaxHostilityRecords = 128;
	private const int HostilityWarThreshold = 100;
	private const int HostilityDecayIntervalYears = 10;
	private const int HostilityDecayAmount = 5;
	private const int ConflictPairCooldownYears = 10;
	private const int ConflictSectCooldownYears = 10;

	private static readonly Dictionary<(long Left, long Right), XjSectHostilityArchiveRecord> HostilityByPair = new Dictionary<(long, long), XjSectHostilityArchiveRecord>();
	private static readonly List<XjSectHostilityArchiveRecord> HostilityBuffer = new List<XjSectHostilityArchiveRecord>();

	internal static int GetHostility(long leftSectId, long rightSectId)
	{
		return leftSectId > 0L
			&& rightSectId > 0L
			&& leftSectId != rightSectId
			&& HostilityByPair.TryGetValue(ResolvePair(leftSectId, rightSectId), out XjSectHostilityArchiveRecord record)
			&& record != null
			? Math.Clamp(record.Hostility, 0, HostilityWarThreshold)
			: 0;
	}

	internal static IReadOnlyList<XjSectHostilityArchiveRecord> ReadHostilities()
	{
		if (HostilityByPair.Count == 0)
		{
			return Array.Empty<XjSectHostilityArchiveRecord>();
		}

		List<XjSectHostilityArchiveRecord> result = new List<XjSectHostilityArchiveRecord>(HostilityByPair.Count);
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null && record.Hostility > 0)
			{
				result.Add(CloneHostility(record));
			}
		}
		result.Sort((left, right) =>
		{
			int hostility = right.Hostility.CompareTo(left.Hostility);
			if (hostility != 0) return hostility;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});
		return result;
	}

	internal static bool AddHostility(long aggressorSectId, long targetSectId, int amount, int currentYear, string reason)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled
			|| aggressorSectId <= 0L
			|| targetSectId <= 0L
			|| aggressorSectId == targetSectId
			|| amount <= 0
			|| currentYear <= 0)
		{
			return false;
		}

		(long Left, long Right) pair = ResolvePair(aggressorSectId, targetSectId);
		if (!HostilityByPair.TryGetValue(pair, out XjSectHostilityArchiveRecord record) || record == null)
		{
			record = new XjSectHostilityArchiveRecord
			{
				LeftSectId = pair.Left,
				RightSectId = pair.Right
			};
			HostilityByPair[pair] = record;
		}

		int previous = Math.Clamp(record.Hostility, 0, HostilityWarThreshold);
		record.Hostility = Math.Clamp(previous + amount, 0, HostilityWarThreshold);
		record.LastHostilityYear = Math.Max(record.LastHostilityYear, currentYear);
		record.LastAggressorSectId = aggressorSectId;
		record.LastReason = string.IsNullOrWhiteSpace(reason) ? "宗门冲突" : reason.Trim();
		PublishHostilityThreshold(record, previous, currentYear);
		PruneHostilityRecords();
		MarkHostilityChanged();
		return record.Hostility != previous;
	}

	/// <summary>
	/// 只在真实跨宗击杀发生时提高宗门敌对值。读取死亡快照与既有家族席位，
	/// 不扫描世界、不创建追杀任务，也不干预原生国家战争。
	/// </summary>
	internal static void ObserveCrossSectHighRealmDeath(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found
			|| snapshot.Year <= 0
			|| snapshot.ActorId <= 0L
			|| snapshot.LastAttackerId <= 0L
			|| snapshot.FamilyStableId <= 0L
			|| (!XjRealmHelper.IsRealm(snapshot.RealmId, "ZiFu")
				&& !XjRealmHelper.IsRealm(snapshot.RealmId, "JinDan")
				&& !XjRealmHelper.IsRealm(snapshot.RealmId, "ShenDan"))
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(snapshot.LastAttackerId, out long attackerFamilyId)
			|| attackerFamilyId <= 0L
			|| !XjSectRepository.TryResolveFamilySectId(attackerFamilyId, out long attackerSectId)
			|| !XjSectRepository.TryResolveFamilySectId(snapshot.FamilyStableId, out long victimSectId)
			|| attackerSectId <= 0L
			|| victimSectId <= 0L
			|| attackerSectId == victimSectId)
		{
			return;
		}

		string victimName = string.IsNullOrWhiteSpace(snapshot.Name) ? "高境修士" : snapshot.Name.Trim();
		string attackerName = string.IsNullOrWhiteSpace(snapshot.LastAttackerName) ? "敌宗修士" : snapshot.LastAttackerName.Trim();
		AddHostility(
			attackerSectId,
			victimSectId,
			30,
			snapshot.Year,
			attackerName + "斩杀" + victimName + "，两宗高境血债加深");
	}

	internal static void ExportHostilityRecords(List<XjSectHostilityArchiveRecord> target)
	{
		if (target == null)
		{
			return;
		}

		target.Clear();
		HostilityBuffer.Clear();
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null && record.Hostility > 0)
			{
				HostilityBuffer.Add(CloneHostility(record));
			}
		}
		HostilityBuffer.Sort((left, right) =>
		{
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});
		for (int i = 0; i < HostilityBuffer.Count; i++)
		{
			target.Add(HostilityBuffer[i]);
		}
	}

	internal static void ImportHostilityRecords(IReadOnlyList<XjSectHostilityArchiveRecord> source)
	{
		HostilityByPair.Clear();
		HostilityBuffer.Clear();
		if (source == null)
		{
			return;
		}

		for (int i = 0; i < source.Count; i++)
		{
			XjSectHostilityArchiveRecord record = NormalizeHostility(CloneHostility(source[i]));
			if (record == null
				|| record.LeftSectId <= 0L
				|| record.RightSectId <= 0L
				|| record.LeftSectId == record.RightSectId
				|| record.Hostility <= 0)
			{
				continue;
			}
			HostilityByPair[ResolvePair(record.LeftSectId, record.RightSectId)] = record;
		}
		PruneHostilityRecords();
	}

	private static bool TickHostilityAndConflicts(int currentYear)
	{
		if (HostilityByPair.Count == 0)
		{
			return false;
		}

		// 冲突必须先于衰减结算。若先衰减并推进 LastHostilityYear，
		// 刚好满十年的宗门对会被永久推迟，低频冲突将永远无法触发。
		bool changed = false;
		if (currentYear % HostilityDecayIntervalYears == 0)
		{
			changed |= GenerateConflictEvents(currentYear);
		}
		changed |= ApplyHostilityDecay(currentYear);
		changed |= TryStartThresholdWars(currentYear);
		if (changed)
		{
			PruneHostilityRecords();
			MarkHostilityChanged();
		}
		return changed;
	}

	private static bool ApplyHostilityDecay(int currentYear)
	{
		bool changed = false;
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record == null
				|| record.Hostility <= 0
				|| record.Hostility >= HostilityWarThreshold
				|| record.LastHostilityYear <= 0)
			{
				continue;
			}

			int elapsed = currentYear - record.LastHostilityYear;
			if (elapsed < HostilityDecayIntervalYears)
			{
				continue;
			}

			int steps = Math.Max(1, elapsed / HostilityDecayIntervalYears);
			int previous = record.Hostility;
			record.Hostility = Math.Max(0, record.Hostility - steps * HostilityDecayAmount);
			record.LastHostilityYear += steps * HostilityDecayIntervalYears;
			changed |= record.Hostility != previous;
			if (previous > 0 && record.Hostility == 0)
			{
				XjThreeBookWriter.RecordSectFriendlyPair(
					record.LeftSectId,
					ResolveSectName(record.LeftSectId),
					record.RightSectId,
					ResolveSectName(record.RightSectId),
					currentYear,
					"多年未再相争，旧怨随岁月消退");
			}
		}
		return changed;
	}

	private static bool GenerateConflictEvents(int currentYear)
	{
		HostilityBuffer.Clear();
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null
				&& record.Hostility > 0
				&& record.Hostility < HostilityWarThreshold
				&& currentYear - record.LastConflictYear >= ConflictPairCooldownYears
				&& currentYear - record.LastHostilityYear >= ConflictPairCooldownYears)
			{
				HostilityBuffer.Add(record);
			}
		}
		if (HostilityBuffer.Count == 0)
		{
			return false;
		}

		HostilityBuffer.Sort((left, right) =>
		{
			int hostility = right.Hostility.CompareTo(left.Hostility);
			if (hostility != 0) return hostility;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});

		int validSectCount = XjSectRepository.ReadAllSects().Count;
		int budget = Math.Max(1, validSectCount / 8);
		bool changed = false;
		for (int i = 0; i < HostilityBuffer.Count && budget > 0; i++)
		{
			XjSectHostilityArchiveRecord record = HostilityBuffer[i];
			if (!CanGenerateConflict(record, currentYear))
			{
				continue;
			}

			int amount = 5 + XjDeterministicHash.PositiveIndex(
				record.LeftSectId,
				"sect.conflict|" + record.RightSectId.ToString(CultureInfo.InvariantCulture) + "|" + currentYear.ToString(CultureInfo.InvariantCulture),
				8);
			int previous = record.Hostility;
			record.Hostility = Math.Clamp(record.Hostility + amount, 0, HostilityWarThreshold);
			record.LastConflictYear = currentYear;
			record.LastHostilityYear = currentYear;
			record.LastAggressorSectId = ResolveConflictAggressor(record, currentYear);
			record.LastReason = BuildConflictReason(record, currentYear);
			PublishConflictEvent(record, amount, currentYear);
			PublishHostilityThreshold(record, previous, currentYear);
			changed = true;
			budget--;
		}
		return changed;
	}

	private static bool TryStartThresholdWars(int currentYear)
	{
		if (CountActiveWars() >= MaxActiveWars)
		{
			return false;
		}

		HostilityBuffer.Clear();
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null && record.Hostility >= HostilityWarThreshold)
			{
				HostilityBuffer.Add(record);
			}
		}
		HostilityBuffer.Sort((left, right) =>
		{
			int last = left.LastHostilityYear.CompareTo(right.LastHostilityYear);
			if (last != 0) return last;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});

		bool changed = false;
		for (int i = 0; i < HostilityBuffer.Count && CountActiveWars() < MaxActiveWars; i++)
		{
			XjSectHostilityArchiveRecord record = HostilityBuffer[i];
			long attacker = record.LastAggressorSectId == record.RightSectId ? record.RightSectId : record.LeftSectId;
			long defender = attacker == record.LeftSectId ? record.RightSectId : record.LeftSectId;
			if (TryStartWar(attacker, defender, currentYear, record.LastReason))
			{
				record.LastWarYear = currentYear;
				changed = true;
			}
		}
		return changed;
	}

	private static bool CanGenerateConflict(XjSectHostilityArchiveRecord record, int currentYear)
	{
		return record != null
			&& record.LeftSectId > 0L
			&& record.RightSectId > 0L
			&& !HasActiveSect(record.LeftSectId)
			&& !HasActiveSect(record.RightSectId)
			&& !IsPairOnCooldown(record.LeftSectId, record.RightSectId, currentYear)
			&& XjSectRepository.HasValidSectCity(record.LeftSectId)
			&& XjSectRepository.HasValidSectCity(record.RightSectId)
			&& !HasRecentSectConflict(record.LeftSectId, currentYear)
			&& !HasRecentSectConflict(record.RightSectId, currentYear);
	}

	private static bool HasRecentSectConflict(long sectId, int currentYear)
	{
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null
				&& record.LastConflictYear > 0
				&& currentYear - record.LastConflictYear < ConflictSectCooldownYears
				&& (record.LeftSectId == sectId || record.RightSectId == sectId))
			{
				return true;
			}
		}
		return false;
	}

	private static long ResolveConflictAggressor(XjSectHostilityArchiveRecord record, int currentYear)
	{
		if (record.LastAggressorSectId == record.LeftSectId || record.LastAggressorSectId == record.RightSectId)
		{
			return record.LastAggressorSectId;
		}
		return XjDeterministicHash.PositiveIndex(
			record.LeftSectId,
			"sect.conflict.aggressor|" + record.RightSectId.ToString(CultureInfo.InvariantCulture) + "|" + currentYear.ToString(CultureInfo.InvariantCulture),
			2) == 0
			? record.LeftSectId
			: record.RightSectId;
	}

	private static string BuildConflictReason(XjSectHostilityArchiveRecord record, int currentYear)
	{
		string[] reasons =
		{
			"两宗门下因旧怨互相试探，宗门敌意再增。",
			"两宗为弟子与家族之争彼此施压，嫌隙加深。",
			"两宗高层借门下冲突试探对方底线，敌意渐重。",
			"两宗围绕重宝与宗门颜面发生冲突，关系恶化。"
		};
		return reasons[XjDeterministicHash.PositiveIndex(
			record.LeftSectId,
			"sect.conflict.reason|" + record.RightSectId.ToString(CultureInfo.InvariantCulture) + "|" + currentYear.ToString(CultureInfo.InvariantCulture),
			reasons.Length)];
	}

	private static void PublishConflictEvent(XjSectHostilityArchiveRecord record, int amount, int currentYear)
	{
		string leftName = ResolveSectName(record.LeftSectId);
		string rightName = ResolveSectName(record.RightSectId);
		string summary = leftName + "与" + rightName + "发生宗门冲突：" + record.LastReason
			+ "敌对值增加" + amount.ToString(CultureInfo.InvariantCulture)
			+ "，现为" + record.Hostility.ToString(CultureInfo.InvariantCulture) + "。";
		long primarySectId = record.LastAggressorSectId == record.RightSectId ? record.RightSectId : record.LeftSectId;
		long relatedSectId = primarySectId == record.LeftSectId ? record.RightSectId : record.LeftSectId;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			"宗门冲突",
			summary,
			record.Hostility >= 80 ? 4 : 2,
			record.Hostility >= 80,
			sectId: primarySectId,
			relatedSectId: relatedSectId,
			year: currentYear,
			eventType: "SectHostilityConflict",
			visibilityFlags: (int)(XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
		XjCenturyAnnalsStore.ObserveSectEvent(
			"SectHostilityConflict",
			currentYear,
			record.LastAggressorSectId,
			ResolveSectName(record.LastAggressorSectId),
			record.Hostility >= 80 ? 4 : 2,
			summary);
		if (record.Hostility >= 80)
		{
			XjBroadcastSystem.ShowWorldTipCritical(summary, color: "#C96A3D", duration: 7f, delayFrames: 1);
		}
	}

	private static void PublishHostilityThreshold(XjSectHostilityArchiveRecord record, int previous, int currentYear)
	{
		if (record == null || record.Hostility <= previous)
		{
			return;
		}

		int threshold = previous < 100 && record.Hostility >= 100 ? 100
			: previous < 80 && record.Hostility >= 80 ? 80
			: previous < 50 && record.Hostility >= 50 ? 50
			: 0;
		if (threshold == 0)
		{
			return;
		}

		string leftName = ResolveSectName(record.LeftSectId);
		string rightName = ResolveSectName(record.RightSectId);
		string summary = threshold == 100
			? leftName + "与" + rightName + "敌意已至开战临界，若两宗高境与护宗大阵俱全，宗门大战将起。"
			: threshold == 80
				? leftName + "与" + rightName + "敌意深重，门下冲突已逼近宗门大战。"
				: leftName + "与" + rightName + "嫌隙已成，宗门敌对开始显化。";
		long primarySectId = record.LastAggressorSectId == record.RightSectId ? record.RightSectId : record.LeftSectId;
		long relatedSectId = primarySectId == record.LeftSectId ? record.RightSectId : record.LeftSectId;
		string relationLabel = threshold >= 80 ? "敌对" : "较差";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Sect,
			threshold == 100 ? "宗门战临界" : "宗门敌意",
			summary,
			threshold == 100 ? 4 : threshold == 80 ? 3 : 2,
			threshold >= 80,
			sectId: primarySectId,
			relatedSectId: relatedSectId,
			year: currentYear,
			eventType: "SectRelationChanged:" + relationLabel,
			visibilityFlags: (int)(XjHistoryVisibility.Sect | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
		XjThreeBookWriter.RecordSectRelationChanged(
			record.LeftSectId,
			leftName,
			record.RightSectId,
			rightName,
			currentYear,
			relationLabel,
			summary);
		XjThreeBookWriter.RecordSectRelationChanged(
			record.RightSectId,
			rightName,
			record.LeftSectId,
			leftName,
			currentYear,
			relationLabel,
			summary);
		if (threshold >= 80)
		{
			XjBroadcastSystem.ShowWorldTipCritical(summary, color: threshold == 100 ? "#D94C4C" : "#C96A3D", duration: 8f, delayFrames: 1);
		}
	}

	private static void RegisterWarStarted(long attackerSectId, long defenderSectId, int currentYear, string reason)
	{
		(long Left, long Right) pair = ResolvePair(attackerSectId, defenderSectId);
		if (!HostilityByPair.TryGetValue(pair, out XjSectHostilityArchiveRecord record) || record == null)
		{
			record = new XjSectHostilityArchiveRecord
			{
				LeftSectId = pair.Left,
				RightSectId = pair.Right
			};
			HostilityByPair[pair] = record;
		}
		record.Hostility = HostilityWarThreshold;
		record.LastWarYear = currentYear;
		record.LastHostilityYear = currentYear;
		record.LastAggressorSectId = attackerSectId;
		record.LastReason = string.IsNullOrWhiteSpace(reason) ? "宗门敌意达到临界" : reason.Trim();
	}

	private static void RegisterWarEnded(long attackerSectId, long defenderSectId, int currentYear)
	{
		(long Left, long Right) pair = ResolvePair(attackerSectId, defenderSectId);
		if (!HostilityByPair.TryGetValue(pair, out XjSectHostilityArchiveRecord record) || record == null)
		{
			return;
		}

		// 破阵灭宗或异常退场后，不在运行时保留指向已灭宗门的敌对记录。
		if (!XjSectRepository.HasValidSectCity(attackerSectId) || !XjSectRepository.HasValidSectCity(defenderSectId))
		{
			HostilityByPair.Remove(pair);
			return;
		}

		record.LastWarYear = currentYear;
		record.LastHostilityYear = currentYear;
		record.Hostility = Math.Min(record.Hostility, 40);
	}

	private static void RegisterWarResolved(XjSectWarArchiveRecord war, int currentYear, bool mutualDestruction)
	{
		if (war == null
			|| !HostilityByPair.TryGetValue(ResolvePair(war.AttackerSectId, war.DefenderSectId), out XjSectHostilityArchiveRecord record)
			|| record == null)
		{
			return;
		}
		record.LastWarYear = currentYear;
		record.LastHostilityYear = currentYear;
		record.Hostility = mutualDestruction ? 60 : 0;
	}

	private static string ResolveSectName(long sectId)
	{
		return sectId > 0L
			&& XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			&& sect != null
			&& !string.IsNullOrWhiteSpace(sect.Name)
			? sect.Name.Trim()
			: "某宗";
	}

	private static void PruneHostilityRecords()
	{
		HostilityBuffer.Clear();
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record == null
				|| record.Hostility <= 0
				|| !XjSectRepository.TryGetBySectId(record.LeftSectId, out XjSectArchiveRecord left)
				|| left == null
				|| string.Equals(left.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
				|| !XjSectRepository.TryGetBySectId(record.RightSectId, out XjSectArchiveRecord right)
				|| right == null
				|| string.Equals(right.Status, XjSectStatus.Extinct, StringComparison.Ordinal))
			{
				HostilityBuffer.Add(record);
			}
		}
		for (int i = 0; i < HostilityBuffer.Count; i++)
		{
			XjSectHostilityArchiveRecord record = HostilityBuffer[i];
			if (record != null)
			{
				HostilityByPair.Remove(ResolvePair(record.LeftSectId, record.RightSectId));
			}
		}

		if (HostilityByPair.Count <= MaxHostilityRecords)
		{
			return;
		}
		HostilityBuffer.Clear();
		HostilityBuffer.AddRange(HostilityByPair.Values);
		HostilityBuffer.Sort((left, right) =>
		{
			int hostility = left.Hostility.CompareTo(right.Hostility);
			if (hostility != 0) return hostility;
			int year = left.LastHostilityYear.CompareTo(right.LastHostilityYear);
			if (year != 0) return year;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});
		for (int i = 0; HostilityByPair.Count > MaxHostilityRecords && i < HostilityBuffer.Count; i++)
		{
			XjSectHostilityArchiveRecord record = HostilityBuffer[i];
			HostilityByPair.Remove(ResolvePair(record.LeftSectId, record.RightSectId));
		}
	}

	private static void ClearHostilityRuntime()
	{
		HostilityByPair.Clear();
		HostilityBuffer.Clear();
	}

	private static void MarkHostilityChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Conflict | XjCodexDirtyFlags.History);
	}

	private static XjSectHostilityArchiveRecord NormalizeHostility(XjSectHostilityArchiveRecord source)
	{
		if (source == null)
		{
			return null;
		}
		(long Left, long Right) pair = ResolvePair(source.LeftSectId, source.RightSectId);
		source.SchemaVersion = XjSectDomainSchema.CurrentVersion;
		source.LeftSectId = pair.Left;
		source.RightSectId = pair.Right;
		source.Hostility = Math.Clamp(source.Hostility, 0, HostilityWarThreshold);
		source.LastConflictYear = Math.Max(0, source.LastConflictYear);
		source.LastHostilityYear = Math.Max(0, source.LastHostilityYear);
		source.LastWarYear = Math.Max(0, source.LastWarYear);
		source.LastReason = source.LastReason ?? string.Empty;
		return source;
	}

	private static XjSectHostilityArchiveRecord CloneHostility(XjSectHostilityArchiveRecord source)
	{
		if (source == null)
		{
			return null;
		}
		return new XjSectHostilityArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			LeftSectId = source.LeftSectId,
			RightSectId = source.RightSectId,
			Hostility = source.Hostility,
			LastConflictYear = source.LastConflictYear,
			LastHostilityYear = source.LastHostilityYear,
			LastWarYear = source.LastWarYear,
			LastAggressorSectId = source.LastAggressorSectId,
			LastReason = source.LastReason ?? string.Empty
		};
	}
}
