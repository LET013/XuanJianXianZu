using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// Cooperative annual cursor for sect-war maintenance. The semantic order is
/// intentionally identical to the legacy TickYear path:
/// family vendetta -> conflict generation -> hostility decay -> threshold wars
/// -> active war settlement. Only the traversal boundaries become resumable.
/// </summary>
internal static partial class XjSectWarSystem
{
	private enum AnnualRuntimePhase : byte
	{
		None = 0,
		Vendetta = 1,
		PrepareConflicts = 2,
		Conflicts = 3,
		PrepareDecay = 4,
		Decay = 5,
		PrepareThresholdWars = 6,
		ThresholdWars = 7,
		PrepareActiveWars = 8,
		ActiveWars = 9,
		Complete = 10
	}

	private static readonly List<XjSectHostilityArchiveRecord> AnnualHostilityBuffer = new List<XjSectHostilityArchiveRecord>(MaxHostilityRecords);
	private static readonly List<XjSectWarArchiveRecord> AnnualWarBuffer = new List<XjSectWarArchiveRecord>(MaxActiveWars);
	private static AnnualRuntimePhase _annualRuntimePhase;
	private static int _annualRuntimeYear;
	private static int _annualRuntimeIndex;
	private static int _annualConflictBudget;
	private static int _annualProcessedWars;
	private static bool _annualChanged;
	private static bool _annualHostilityChanged;

	internal static bool HasPendingAnnualWorkForYear(int currentYear)
	{
		return currentYear > 0 && _annualRuntimeYear == currentYear && _annualRuntimePhase != AnnualRuntimePhase.None;
	}

	internal static bool BeginAnnualWork(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return false;
		if (HasPendingAnnualWorkForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		_annualRuntimeYear = currentYear;
		_annualRuntimePhase = AnnualRuntimePhase.Vendetta;
		return true;
	}

	internal static bool TickPendingAnnualWork(int itemBudget = 3, double timeBudgetMs = 0.34d)
	{
		if (_annualRuntimeYear <= 0 || _annualRuntimePhase == AnnualRuntimePhase.None) return true;
		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);

		while (_annualRuntimePhase != AnnualRuntimePhase.None && !budget.ShouldYield)
		{
			switch (_annualRuntimePhase)
			{
				case AnnualRuntimePhase.Vendetta:
					if (!budget.TryTake()) return false;
					long vendettaSample = XjRuntimeDiagnostics.BeginNamedSample();
					try
					{
						if (!XjFamilyVendettaRegistry.HasPendingAnnualWorkForYear(_annualRuntimeYear))
						{
							XjFamilyVendettaRegistry.BeginAnnualWork(_annualRuntimeYear);
						}
						if (XjFamilyVendettaRegistry.HasPendingAnnualWorkForYear(_annualRuntimeYear))
						{
							bool vendettaComplete = XjFamilyVendettaRegistry.TickPendingAnnualWork(
								itemBudget: 4,
								timeBudgetMs: 0.24d,
								out bool vendettaChanged);
							_annualChanged |= vendettaChanged;
							if (!vendettaComplete) return false;
						}
					}
					finally
					{
						XjRuntimeDiagnostics.EndNamedSample("annual.SectWar.vendetta", vendettaSample);
					}
					_annualRuntimePhase = AnnualRuntimePhase.PrepareConflicts;
					break;

				case AnnualRuntimePhase.PrepareConflicts:
					PrepareAnnualConflictCandidates();
					_annualRuntimePhase = AnnualRuntimePhase.Conflicts;
					break;

				case AnnualRuntimePhase.Conflicts:
					while (_annualRuntimeIndex < AnnualHostilityBuffer.Count
						&& _annualConflictBudget > 0
						&& budget.TryTake())
					{
						XjSectHostilityArchiveRecord record = AnnualHostilityBuffer[_annualRuntimeIndex++];
						if (!IsCurrentAnnualHostilityRecord(record) || !CanGenerateConflict(record, _annualRuntimeYear)) continue;
						int amount = 5 + XjDeterministicHash.PositiveIndex(
							record.LeftSectId,
							"sect.conflict|" + record.RightSectId.ToString(CultureInfo.InvariantCulture) + "|" + _annualRuntimeYear.ToString(CultureInfo.InvariantCulture),
							8);
						int previous = record.Hostility;
						record.Hostility = Math.Clamp(record.Hostility + amount, 0, HostilityWarThreshold);
						record.LastConflictYear = _annualRuntimeYear;
						record.LastHostilityYear = _annualRuntimeYear;
						record.LastAggressorSectId = ResolveConflictAggressor(record, _annualRuntimeYear);
						record.LastReason = BuildConflictReason(record, _annualRuntimeYear);
						PublishConflictEvent(record, amount, _annualRuntimeYear);
						PublishHostilityThreshold(record, previous, _annualRuntimeYear);
						_annualHostilityChanged = true;
						_annualChanged = true;
						_annualConflictBudget--;
					}
					if (_annualRuntimeIndex < AnnualHostilityBuffer.Count && _annualConflictBudget > 0) return false;
					_annualRuntimePhase = AnnualRuntimePhase.PrepareDecay;
					break;

				case AnnualRuntimePhase.PrepareDecay:
					AnnualHostilityBuffer.Clear();
					foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
					{
						if (record != null) AnnualHostilityBuffer.Add(record);
					}
					_annualRuntimeIndex = 0;
					_annualRuntimePhase = AnnualRuntimePhase.Decay;
					break;

				case AnnualRuntimePhase.Decay:
					while (_annualRuntimeIndex < AnnualHostilityBuffer.Count && budget.TryTake())
					{
						XjSectHostilityArchiveRecord record = AnnualHostilityBuffer[_annualRuntimeIndex++];
						if (IsCurrentAnnualHostilityRecord(record) && ApplyAnnualHostilityDecay(record, _annualRuntimeYear))
						{
							_annualHostilityChanged = true;
							_annualChanged = true;
						}
					}
					if (_annualRuntimeIndex < AnnualHostilityBuffer.Count) return false;
					_annualRuntimePhase = AnnualRuntimePhase.PrepareThresholdWars;
					break;

				case AnnualRuntimePhase.PrepareThresholdWars:
					PrepareAnnualThresholdWarCandidates();
					_annualRuntimePhase = AnnualRuntimePhase.ThresholdWars;
					break;

				case AnnualRuntimePhase.ThresholdWars:
					while (_annualRuntimeIndex < AnnualHostilityBuffer.Count
						&& CountActiveWars() < MaxActiveWars
						&& budget.TryTake())
					{
						XjSectHostilityArchiveRecord record = AnnualHostilityBuffer[_annualRuntimeIndex++];
						if (!IsCurrentAnnualHostilityRecord(record)) continue;
						long attacker = record.LastAggressorSectId == record.RightSectId ? record.RightSectId : record.LeftSectId;
						long defender = attacker == record.LeftSectId ? record.RightSectId : record.LeftSectId;
						if (!CanSectInitiateWar(attacker, defender))
						{
							record.Hostility = Math.Min(90, record.Hostility);
							record.LastHostilityYear = _annualRuntimeYear;
							record.LastReason = "积怨虽深，攻方高境不足以承受宗战，暂且按兵不动";
							_annualHostilityChanged = true;
							_annualChanged = true;
							continue;
						}
						if (TryStartWar(attacker, defender, _annualRuntimeYear, record.LastReason))
						{
							record.LastWarYear = _annualRuntimeYear;
							_annualHostilityChanged = true;
							_annualChanged = true;
						}
					}
					if (_annualRuntimeIndex < AnnualHostilityBuffer.Count && CountActiveWars() < MaxActiveWars) return false;
					if (_annualHostilityChanged)
					{
						PruneHostilityRecords();
						MarkHostilityChanged();
					}
					_annualRuntimePhase = AnnualRuntimePhase.PrepareActiveWars;
					break;

				case AnnualRuntimePhase.PrepareActiveWars:
					PrepareAnnualActiveWarCandidates();
					_annualRuntimePhase = AnnualRuntimePhase.ActiveWars;
					break;

				case AnnualRuntimePhase.ActiveWars:
					while (_annualRuntimeIndex < AnnualWarBuffer.Count
						&& _annualProcessedWars < MaxActiveWarsProcessedPerYear
						&& budget.TryTake())
					{
						XjSectWarArchiveRecord war = AnnualWarBuffer[_annualRuntimeIndex++];
						if (!IsCurrentAnnualWarRecord(war)) continue;
						long warSample = XjRuntimeDiagnostics.BeginNamedSample();
						try
						{
							if (ProcessWar(war, _annualRuntimeYear))
							{
								_annualChanged = true;
								_annualProcessedWars++;
							}
						}
						finally
						{
							XjRuntimeDiagnostics.EndNamedSample("annual.SectWar.activeWar", warSample);
						}
					}
					if (_annualRuntimeIndex < AnnualWarBuffer.Count && _annualProcessedWars < MaxActiveWarsProcessedPerYear) return false;
					_annualRuntimePhase = AnnualRuntimePhase.Complete;
					break;

				case AnnualRuntimePhase.Complete:
					if (_annualChanged)
					{
						PruneRecords();
						MarkChanged();
					}
					ClearPendingAnnualWork();
					return true;
			}
		}

		return _annualRuntimePhase == AnnualRuntimePhase.None;
	}

	private static bool IsCurrentAnnualHostilityRecord(XjSectHostilityArchiveRecord record)
	{
		if (record == null || record.LeftSectId <= 0L || record.RightSectId <= 0L) return false;
		return HostilityByPair.TryGetValue(ResolvePair(record.LeftSectId, record.RightSectId), out XjSectHostilityArchiveRecord current)
			&& object.ReferenceEquals(current, record);
	}

	private static bool IsCurrentAnnualWarRecord(XjSectWarArchiveRecord war)
	{
		return war != null
			&& war.WarId > 0L
			&& RecordsByWarId.TryGetValue(war.WarId, out XjSectWarArchiveRecord current)
			&& object.ReferenceEquals(current, war);
	}

	private static void PrepareAnnualConflictCandidates()
	{
		AnnualHostilityBuffer.Clear();
		_annualRuntimeIndex = 0;
		_annualConflictBudget = 0;
		if (_annualRuntimeYear % HostilityDecayIntervalYears != 0 || HostilityByPair.Count == 0) return;
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null
				&& record.Hostility > 0
				&& record.Hostility < HostilityWarThreshold
				&& _annualRuntimeYear - record.LastConflictYear >= ConflictPairCooldownYears
				&& _annualRuntimeYear - record.LastHostilityYear >= ConflictPairCooldownYears)
			{
				AnnualHostilityBuffer.Add(record);
			}
		}
		AnnualHostilityBuffer.Sort((left, right) =>
		{
			int hostility = right.Hostility.CompareTo(left.Hostility);
			if (hostility != 0) return hostility;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});
		_annualConflictBudget = Math.Max(1, XjSectRepository.CountEstablishedSects() / 8);
	}

	private static bool ApplyAnnualHostilityDecay(XjSectHostilityArchiveRecord record, int currentYear)
	{
		if (record == null
			|| record.Hostility <= 0
			|| record.Hostility >= HostilityWarThreshold
			|| record.LastHostilityYear <= 0)
		{
			return false;
		}
		int elapsed = currentYear - record.LastHostilityYear;
		if (elapsed < HostilityDecayIntervalYears) return false;
		int steps = Math.Max(1, elapsed / HostilityDecayIntervalYears);
		int previous = record.Hostility;
		record.Hostility = Math.Max(0, record.Hostility - steps * HostilityDecayAmount);
		record.LastHostilityYear += steps * HostilityDecayIntervalYears;
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
		return record.Hostility != previous;
	}

	private static void PrepareAnnualThresholdWarCandidates()
	{
		AnnualHostilityBuffer.Clear();
		_annualRuntimeIndex = 0;
		if (CountActiveWars() >= MaxActiveWars) return;
		foreach (XjSectHostilityArchiveRecord record in HostilityByPair.Values)
		{
			if (record != null && record.Hostility >= HostilityWarThreshold) AnnualHostilityBuffer.Add(record);
		}
		AnnualHostilityBuffer.Sort((left, right) =>
		{
			int last = left.LastHostilityYear.CompareTo(right.LastHostilityYear);
			if (last != 0) return last;
			int first = left.LeftSectId.CompareTo(right.LeftSectId);
			return first != 0 ? first : left.RightSectId.CompareTo(right.RightSectId);
		});
	}

	private static void PrepareAnnualActiveWarCandidates()
	{
		AnnualWarBuffer.Clear();
		_annualRuntimeIndex = 0;
		_annualProcessedWars = 0;
		foreach (XjSectWarArchiveRecord record in RecordsByWarId.Values)
		{
			if (record != null && string.Equals(record.Status, XjSectWarStatus.Active, StringComparison.Ordinal))
			{
				AnnualWarBuffer.Add(record);
			}
		}
		AnnualWarBuffer.Sort((left, right) => left.LastProcessedYear != right.LastProcessedYear
			? left.LastProcessedYear.CompareTo(right.LastProcessedYear)
			: left.WarId.CompareTo(right.WarId));
	}

	internal static void ClearPendingAnnualWork()
	{
		XjFamilyVendettaRegistry.ClearPendingAnnualWork();
		AnnualHostilityBuffer.Clear();
		AnnualWarBuffer.Clear();
		_annualRuntimePhase = AnnualRuntimePhase.None;
		_annualRuntimeYear = 0;
		_annualRuntimeIndex = 0;
		_annualConflictBudget = 0;
		_annualProcessedWars = 0;
		_annualChanged = false;
		_annualHostilityChanged = false;
	}
}
