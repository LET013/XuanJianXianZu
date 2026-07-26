using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.History;

internal static class XjCenturyAnnalsStore
{
	private static readonly List<XjCenturyAnnalsArchiveRecord> Records = new List<XjCenturyAnnalsArchiveRecord>();
	private static readonly Dictionary<int, XjCenturyAnnalsArchiveRecord> RecordsByCenturyId = new Dictionary<int, XjCenturyAnnalsArchiveRecord>();
	private static readonly List<XjCenturyAnnalsLedgerRecord> Ledger = new List<XjCenturyAnnalsLedgerRecord>();
	private static XjCenturyAnnalsStateRecord State = new XjCenturyAnnalsStateRecord();
	private static bool TimelineRepairPending;
	private static int ImportedSchemaVersion;

	internal static int Count => Records.Count;
	internal static int Capacity => XjCenturyAnnalsSchema.MaxCenturyRecords;
	internal static int BaseWorldYear => State?.BaseWorldYear ?? 0;
	internal static int LastCompletedCenturyId => State?.LastCompletedCenturyId ?? 0;

	internal static IReadOnlyList<XjCenturyAnnalsArchiveRecord> ReadSnapshot(int maximumEntries = 0)
	{
		int count = maximumEntries > 0 ? Math.Min(Records.Count, maximumEntries) : Records.Count;
		if (count <= 0) return Array.Empty<XjCenturyAnnalsArchiveRecord>();
		List<XjCenturyAnnalsArchiveRecord> result = new List<XjCenturyAnnalsArchiveRecord>(count);
		for (int i = Records.Count - 1; i >= 0 && result.Count < count; i--)
		{
			result.Add(Clone(Records[i]));
		}
		return result;
	}

	internal static XjCenturyAnnalsStateRecord ReadStateSnapshot()
	{
		return Clone(State);
	}

	/// <summary>
	/// 年度家族扶持只更新既有阶段记录的少数字段。这里返回只读列表视图，
	/// 避免每年克隆最多数千条阶段记录；调用方不得增删或替换列表元素。
	/// </summary>
	internal static IReadOnlyList<XjCenturyFamilyStageStateRecord> ReadFamilyStageStateView()
	{
		State ??= new XjCenturyAnnalsStateRecord();
		return State.FamilyStageStates;
	}

	internal static void NotifyFamilyStageStateFieldsChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool TryEnsureBaseWorldYear(int currentYear, out int baseWorldYear)
	{
		State ??= new XjCenturyAnnalsStateRecord();
		// 正常存档的纪年起点一经确定便不可改变。ResolveCenturyId 会在每次
		// 百年账本写入时调用；若仍为它扫描阶段状态、卷宗与账本，会把一次
		// O(1) 年份换算退化成 O(家族+历史)。只有首次建立或旧档迁移才进入重恢复。
		if (!TimelineRepairPending
			&& State.BaseWorldYear > 0
			&& State.SchemaVersion == XjCenturyAnnalsSchema.CurrentVersion)
		{
			baseWorldYear = State.BaseWorldYear;
			return false;
		}

		int referenceYear = ResolveTimelineReferenceYear(currentYear);
		int resolvedBaseWorldYear = ResolvePersistedBaseWorldYear(referenceYear);
		bool changed = false;
		if (State.BaseWorldYear != resolvedBaseWorldYear)
		{
			State.BaseWorldYear = resolvedBaseWorldYear;
			changed = true;
		}

		if (TimelineRepairPending)
		{
			changed |= RepairLegacyTimeline(resolvedBaseWorldYear, referenceYear);
			TimelineRepairPending = false;
		}

		if (State.SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion)
		{
			State.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
			changed = true;
		}

		baseWorldYear = State.BaseWorldYear;
		if (!changed)
		{
			return false;
		}

		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.CenturyAnnals | XjCodexDirtyFlags.World);
		return true;
	}

	private static int ResolvePersistedBaseWorldYear(int referenceYear)
	{
		// Schema 7 以前的卷次可能经历过绝对纪年误迁移或一次错误的基准恢复。
		// 迁移时先综合现有卷、家族首次入谱年和真实生成时点，再回退到旧持久值；
		// 正常 Schema 7 存档则直接沿用本局最初保存的起始年。
		if (TimelineRepairPending && ImportedSchemaVersion < XjCenturyAnnalsSchema.CurrentVersion)
		{
			int recovered = RecoverBaseWorldYearFromRecords(referenceYear);
			if (recovered > 0)
			{
				return recovered;
			}
		}

		if (State.BaseWorldYear > 0)
		{
			return State.BaseWorldYear;
		}

		int inferred = InferBaseWorldYearFromAlignedRecords();
		return inferred > 0 ? inferred : Math.Max(1, referenceYear);
	}

	private static bool ShouldSuppressAbsoluteYearOneEvidence()
	{
		if (ImportedSchemaVersion != 5 || State?.BaseWorldYear != 1)
		{
			return false;
		}
		for (int i = 0; i < Records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = Records[i];
			if (record != null && !record.IsCompleteCycle && !IsSyntheticBackfill(record))
			{
				return true;
			}
		}
		return false;
	}

	private static int RecoverBaseWorldYearFromRecords(int referenceYear)
	{
		Dictionary<int, int> candidates = new Dictionary<int, int>();
		Dictionary<int, int> directCandidates = new Dictionary<int, int>();
		Dictionary<int, int> generatedCandidates = new Dictionary<int, int>();
		bool suppressAbsoluteYearOne = ShouldSuppressAbsoluteYearOneEvidence();

		if (State?.BaseWorldYear > 0 && !(suppressAbsoluteYearOne && State.BaseWorldYear == 1))
		{
			AddBaseCandidate(candidates, State.BaseWorldYear, referenceYear, 2);
		}

		int familyStageEvidence = RecoverBaseWorldYearFromFamilyStages(referenceYear);
		if (familyStageEvidence > 0)
		{
			// 阶段记录以百年卷首初始化；只作为一类强证据，不按家族数量重复加权。
			AddBaseCandidate(candidates, familyStageEvidence, referenceYear, 3);
		}

		List<XjCenturyAnnalsArchiveRecord> evidence = new List<XjCenturyAnnalsArchiveRecord>();
		for (int i = 0; i < Records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = Records[i];
			if (record == null || IsSyntheticBackfill(record)) continue;
			evidence.Add(record);
			if (record.CenturyId > 0 && record.StartYear > 0)
			{
				int candidate = record.StartYear
					- (record.CenturyId - 1) * XjCenturyAnnalsSchema.YearsPerCentury;
				if (!(suppressAbsoluteYearOne && candidate == 1))
				{
					AddBaseCandidate(directCandidates, candidate, referenceYear, 1);
				}
			}
		}
		int direct = SelectBestBaseCandidate(directCandidates);
		if (direct > 0) AddBaseCandidate(candidates, direct, referenceYear, 3);

		evidence.Sort((left, right) =>
		{
			int leftGenerated = left?.GeneratedYear > 0 ? left.GeneratedYear : int.MaxValue;
			int rightGenerated = right?.GeneratedYear > 0 ? right.GeneratedYear : int.MaxValue;
			int generated = leftGenerated.CompareTo(rightGenerated);
			if (generated != 0) return generated;
			int start = (left?.StartYear ?? 0).CompareTo(right?.StartYear ?? 0);
			return start != 0 ? start : (left?.CenturyId ?? 0).CompareTo(right?.CenturyId ?? 0);
		});

		for (int i = 0; i < evidence.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = evidence[i];
			if (record == null || record.GeneratedYear <= 0) continue;
			// 正常卷在本百年最后一年生成：420年开局的第一卷于519年归档。
			// +1 修正此前把正常卷推早一年的偏移；若某卷因读档晚一年补写，
			// 其候选只作为一类证据，不会因卷数多而压倒持久基准与卷首证据。
			int candidate = record.GeneratedYear
				- (i + 1) * XjCenturyAnnalsSchema.YearsPerCentury + 1;
			AddBaseCandidate(generatedCandidates, candidate, referenceYear, 1);
		}
		int generated = SelectBestBaseCandidate(generatedCandidates);
		if (generated > 0) AddBaseCandidate(candidates, generated, referenceYear, 3);
		return SelectBestBaseCandidate(candidates);
	}

	private static int RecoverBaseWorldYearFromFamilyStages(int referenceYear)
	{
		int earliest = 0;
		if (State?.FamilyStageStates != null)
		{
			for (int i = 0; i < State.FamilyStageStates.Count; i++)
			{
				int year = State.FamilyStageStates[i]?.FirstRecordedYear ?? 0;
				if (year > 0 && (referenceYear <= 0 || year <= referenceYear))
				{
					earliest = earliest <= 0 ? year : Math.Min(earliest, year);
				}
			}
		}
		for (int i = 0; i < Records.Count; i++)
		{
			IReadOnlyList<XjCenturyFamilyStateRecord> families = Records[i]?.FamilyStates;
			if (families == null) continue;
			for (int f = 0; f < families.Count; f++)
			{
				int year = families[f]?.FirstRecordedYear ?? 0;
				if (year > 0 && (referenceYear <= 0 || year <= referenceYear))
				{
					earliest = earliest <= 0 ? year : Math.Min(earliest, year);
				}
			}
		}
		return earliest;
	}

	private static int ResolveTimelineReferenceYear(int requestedYear)
	{
		int referenceYear = Math.Max(0, requestedYear);
		int liveWorldYear = World.world?.map_stats?.year ?? 0;
		if (liveWorldYear > 0)
		{
			// 游戏已加载时，当前世界年是迁移的可信上界。旧错误卷、阶段字段或账本
			// 即使带有未来年份，也不能反向把“已完成卷数”推到真实世界年之后。
			return Math.Max(1, Math.Max(referenceYear, liveWorldYear));
		}
		if (State != null)
		{
			if (State.LastGeneratedYear > referenceYear) referenceYear = State.LastGeneratedYear;
			if (State.FamilyStageStates != null)
			{
				for (int i = 0; i < State.FamilyStageStates.Count; i++)
				{
					XjCenturyFamilyStageStateRecord stage = State.FamilyStageStates[i];
					if (stage == null) continue;
					if (stage.LastUpdatedYear > referenceYear) referenceYear = stage.LastUpdatedYear;
					if (stage.FirstRecordedYear > referenceYear) referenceYear = stage.FirstRecordedYear;
				}
			}
		}
		for (int i = 0; i < Records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = Records[i];
			if (record == null) continue;
			if (record.GeneratedYear > referenceYear) referenceYear = record.GeneratedYear;
			if (record.EndYear > referenceYear) referenceYear = record.EndYear;
			if (record.StartYear > referenceYear) referenceYear = record.StartYear;
		}
		for (int i = 0; i < Ledger.Count; i++)
		{
			int year = Ledger[i]?.Year ?? 0;
			if (year > referenceYear) referenceYear = year;
		}
		return Math.Max(1, referenceYear);
	}

	private static int InferBaseWorldYearFromAlignedRecords()
	{
		for (int i = 0; i < Records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = Records[i];
			if (record == null || record.CenturyId <= 0 || record.StartYear <= 0) continue;
			int candidate = record.StartYear
				- (record.CenturyId - 1) * XjCenturyAnnalsSchema.YearsPerCentury;
			if (candidate > 0)
			{
				return candidate;
			}
		}
		return 0;
	}

	private static void AddBaseCandidate(
		Dictionary<int, int> candidates,
		int candidate,
		int referenceYear,
		int weight)
	{
		if (candidates == null || candidate <= 0 || weight <= 0
			|| referenceYear > 0 && candidate > referenceYear)
		{
			return;
		}
		candidates.TryGetValue(candidate, out int count);
		candidates[candidate] = count + weight;
	}

	private static int SelectBestBaseCandidate(Dictionary<int, int> candidates)
	{
		int selected = 0;
		int selectedCount = 0;
		if (candidates == null) return selected;
		foreach (KeyValuePair<int, int> pair in candidates)
		{
			if (pair.Value > selectedCount
				|| pair.Value == selectedCount && (selected <= 0 || pair.Key < selected))
			{
				selected = pair.Key;
				selectedCount = pair.Value;
			}
		}
		return selected;
	}

	private static bool RepairLegacyTimeline(int baseWorldYear, int currentYear)
	{
		if (baseWorldYear <= 0)
		{
			return false;
		}

		bool changed = false;
		int latestCompletedCenturyId = currentYear >= baseWorldYear
			? Math.Max(0, (currentYear - baseWorldYear + 1) / XjCenturyAnnalsSchema.YearsPerCentury)
			: 0;
		Dictionary<int, XjCenturyAnnalsArchiveRecord> repaired = new Dictionary<int, XjCenturyAnnalsArchiveRecord>();
		List<XjCenturyAnnalsArchiveRecord> ordered = new List<XjCenturyAnnalsArchiveRecord>(Records);
		ordered.Sort((left, right) =>
		{
			int generated = (left?.GeneratedYear ?? 0).CompareTo(right?.GeneratedYear ?? 0);
			if (generated != 0) return generated;
			int start = (left?.StartYear ?? 0).CompareTo(right?.StartYear ?? 0);
			return start != 0 ? start : (left?.CenturyId ?? 0).CompareTo(right?.CenturyId ?? 0);
		});

		int realRecordOrdinal = 0;
		for (int i = 0; i < ordered.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = ordered[i];
			if (record == null) continue;
			bool syntheticBackfill = IsSyntheticBackfill(record);
			if (!syntheticBackfill) realRecordOrdinal++;

			// Schema 5 的绝对纪年补卷与当前相对纪年不兼容；舍弃这些空壳卷，
			// 由统一追补入口按正确的游戏起始年重新补齐。
			if (ImportedSchemaVersion == 5 && baseWorldYear > 1 && syntheticBackfill)
			{
				changed = true;
				continue;
			}

			int centuryId = ResolveRelativeCenturyId(record, baseWorldYear, realRecordOrdinal);
			if (centuryId <= 0 || centuryId > latestCompletedCenturyId)
			{
				changed = true;
				continue;
			}

			int expectedStartYear = baseWorldYear
				+ (centuryId - 1) * XjCenturyAnnalsSchema.YearsPerCentury;
			int expectedEndYear = expectedStartYear + XjCenturyAnnalsSchema.YearsPerCentury - 1;
			if (record.CenturyId != centuryId
				|| record.StartYear != expectedStartYear
				|| record.EndYear != expectedEndYear
				|| record.SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion)
			{
				changed = true;
			}
			record.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
			record.CenturyId = centuryId;
			record.StartYear = expectedStartYear;
			record.EndYear = expectedEndYear;
			record.Title = "第" + centuryId + "世谱";
			if (!syntheticBackfill)
			{
				record.IsCompleteCycle = true;
			}

			if (!repaired.TryGetValue(centuryId, out XjCenturyAnnalsArchiveRecord existing)
				|| ScoreRecordEvidence(record) >= ScoreRecordEvidence(existing))
			{
				repaired[centuryId] = record;
			}
		}

		if (repaired.Count != RecordsByCenturyId.Count)
		{
			changed = true;
		}
		RecordsByCenturyId.Clear();
		foreach (KeyValuePair<int, XjCenturyAnnalsArchiveRecord> pair in repaired)
		{
			RecordsByCenturyId[pair.Key] = pair.Value;
		}
		RebuildOrderedRecords();

		for (int i = 0; i < Ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord record = Ledger[i];
			if (record == null) continue;
			int centuryId = ResolveCenturyIdFromBase(record.Year, baseWorldYear);
			if (record.CenturyId != centuryId || record.SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion)
			{
				record.CenturyId = centuryId;
				record.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
				changed = true;
			}
		}

		changed |= RepairFamilyFirstRecordedYearsFromRecords();
		State.BaseWorldYear = baseWorldYear;
		State.GeneratedCenturyIds = new List<int>();
		State.LastCompletedCenturyId = 0;
		State.LastGeneratedYear = 0;
		RepairStateFromRecords();
		return changed;
	}

	private static bool RepairFamilyFirstRecordedYearsFromRecords()
	{
		if (State?.FamilyStageStates == null || State.FamilyStageStates.Count == 0 || Records.Count == 0)
		{
			return false;
		}

		Dictionary<long, int> earliestByFamily = new Dictionary<long, int>();
		for (int i = 0; i < Records.Count; i++)
		{
			IReadOnlyList<XjCenturyFamilyStateRecord> families = Records[i]?.FamilyStates;
			if (families == null) continue;
			for (int f = 0; f < families.Count; f++)
			{
				XjCenturyFamilyStateRecord family = families[f];
				if (family == null || family.FamilyStableId <= 0L || family.FirstRecordedYear <= 0) continue;
				if (!earliestByFamily.TryGetValue(family.FamilyStableId, out int earliest)
					|| family.FirstRecordedYear < earliest)
				{
					earliestByFamily[family.FamilyStableId] = family.FirstRecordedYear;
				}
			}
		}

		bool changed = false;
		for (int i = 0; i < State.FamilyStageStates.Count; i++)
		{
			XjCenturyFamilyStageStateRecord stage = State.FamilyStageStates[i];
			if (stage == null || stage.FamilyStableId <= 0L
				|| !earliestByFamily.TryGetValue(stage.FamilyStableId, out int earliest)
				|| earliest <= 0
				|| stage.FirstRecordedYear > 0 && stage.FirstRecordedYear <= earliest)
			{
				continue;
			}
			stage.FirstRecordedYear = earliest;
			if (stage.LastUpdatedYear < earliest) stage.LastUpdatedYear = earliest;
			changed = true;
		}
		return changed;
	}

	private static int ResolveRelativeCenturyId(
		XjCenturyAnnalsArchiveRecord record,
		int baseWorldYear,
		int realRecordOrdinal)
	{
		if (record == null || baseWorldYear <= 0) return 0;
		if (record.StartYear >= baseWorldYear)
		{
			int delta = record.StartYear - baseWorldYear;
			if (delta % XjCenturyAnnalsSchema.YearsPerCentury == 0)
			{
				return delta / XjCenturyAnnalsSchema.YearsPerCentury + 1;
			}
		}

		if (record.GeneratedYear > baseWorldYear)
		{
			int delta = record.GeneratedYear - baseWorldYear;
			if (delta >= XjCenturyAnnalsSchema.YearsPerCentury
				&& delta % XjCenturyAnnalsSchema.YearsPerCentury == 0)
			{
				return delta / XjCenturyAnnalsSchema.YearsPerCentury;
			}
		}

		if (!IsSyntheticBackfill(record) && realRecordOrdinal > 0)
		{
			return realRecordOrdinal;
		}
		return Math.Max(0, record.CenturyId);
	}

	private static int ResolveCenturyIdFromBase(int year, int baseWorldYear)
	{
		if (year <= 0 || baseWorldYear <= 0 || year < baseWorldYear) return 0;
		return (year - baseWorldYear) / XjCenturyAnnalsSchema.YearsPerCentury + 1;
	}

	private static bool IsSyntheticBackfill(XjCenturyAnnalsArchiveRecord record)
	{
		return record != null
			&& !record.IsCompleteCycle
			&& ((record.WorldSummary?.StartsWith("此卷为旧档补录", StringComparison.Ordinal) ?? false)
				|| (record.WorldSummary?.StartsWith("此卷由旧档补成", StringComparison.Ordinal) ?? false));
	}

	private static int ScoreRecordEvidence(XjCenturyAnnalsArchiveRecord record)
	{
		if (record == null) return int.MinValue;
		int score = record.IsCompleteCycle ? 100000 : 0;
		score += Math.Min(1000, record.FamilyStates?.Count ?? 0) * 100;
		score += Math.Min(100, record.SectSummaries?.Count ?? 0) * 20;
		score += Math.Min(100, record.RepresentativeActors?.Count ?? 0) * 10;
		score += Math.Min(100, record.ImportantEventIds?.Count ?? 0);
		return score;
	}

	internal static IReadOnlyList<XjCenturyAnnalsLedgerRecord> ReadLedgerSnapshot(int maximumEntries = 0)
	{
		int count = maximumEntries > 0 ? Math.Min(Ledger.Count, maximumEntries) : Ledger.Count;
		if (count <= 0) return Array.Empty<XjCenturyAnnalsLedgerRecord>();
		List<XjCenturyAnnalsLedgerRecord> result = new List<XjCenturyAnnalsLedgerRecord>(count);
		for (int i = Ledger.Count - 1; i >= 0 && result.Count < count; i--)
		{
			result.Add(Clone(Ledger[i]));
		}
		return result;
	}

	internal static void ExportArchiveRecords(List<XjCenturyAnnalsArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < Records.Count; i++)
		{
			target.Add(Clone(Records[i]));
		}
	}

	internal static XjCenturyAnnalsStateRecord ExportState()
	{
		return Clone(State);
	}

	internal static void ExportLedgerRecords(List<XjCenturyAnnalsLedgerRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < Ledger.Count; i++)
		{
			target.Add(Clone(Ledger[i]));
		}
	}

	internal static void ImportArchiveRecords(
		IReadOnlyList<XjCenturyAnnalsArchiveRecord> records,
		XjCenturyAnnalsStateRecord state,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		int importedSchemaVersion = ResolveImportedSchemaVersion(records, state, ledger);
		bool requiresRewrite = RequiresMigration(records, state, ledger);
		Clear();
		ImportedSchemaVersion = importedSchemaVersion;
		// 代表人物口径升级只需要重写卷宗，不得触发纪年基准恢复。
		// 仅 Schema 7 以前的旧时间线才进入重恢复。
		TimelineRepairPending = importedSchemaVersion < 7;
		State = Normalize(Clone(state) ?? new XjCenturyAnnalsStateRecord());
		if (records != null)
		{
			int start = Math.Max(0, records.Count - XjCenturyAnnalsSchema.MaxCenturyRecords);
			for (int i = start; i < records.Count; i++)
			{
				XjCenturyAnnalsArchiveRecord copy = Normalize(Clone(records[i]));
				if (copy == null || copy.CenturyId <= 0) continue;
				RecordsByCenturyId[copy.CenturyId] = copy;
			}
			RebuildOrderedRecords();
		}
		if (ledger != null)
		{
			int start = Math.Max(0, ledger.Count - XjCenturyAnnalsSchema.MaxLedgerRecords);
			for (int i = start; i < ledger.Count; i++)
			{
				XjCenturyAnnalsLedgerRecord copy = Normalize(Clone(ledger[i]));
				if (copy != null) Ledger.Add(copy);
			}
		}
		RepairStateFromRecords();
		if (requiresRewrite)
		{
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}

		int currentYear = World.world?.map_stats?.year ?? 0;
		if (currentYear > 0)
		{
			TryEnsureBaseWorldYear(currentYear, out _);
		}
	}

	internal static bool TryStoreRecord(XjCenturyAnnalsArchiveRecord record)
	{
		XjCenturyAnnalsArchiveRecord copy = Normalize(Clone(record));
		if (copy == null || copy.CenturyId <= 0)
		{
			return false;
		}

		// 世谱卷以 CenturyId 为幂等键。正常入口已先检查缺卷；这里仍需拒绝
		// 重复写入，避免同年重复调度用新的世界投影覆盖已归档卷。
		if (RecordsByCenturyId.ContainsKey(copy.CenturyId))
		{
			return false;
		}
		RecordsByCenturyId[copy.CenturyId] = copy;
		RebuildOrderedRecords();
		State ??= new XjCenturyAnnalsStateRecord();
		if (State.BaseWorldYear <= 0)
		{
			int inferredBaseWorldYear = copy.StartYear
				- Math.Max(0, copy.CenturyId - 1) * XjCenturyAnnalsSchema.YearsPerCentury;
			State.BaseWorldYear = inferredBaseWorldYear > 0
				? inferredBaseWorldYear
				: Math.Max(1, copy.StartYear);
		}
		if (!State.GeneratedCenturyIds.Contains(copy.CenturyId))
		{
			State.GeneratedCenturyIds.Add(copy.CenturyId);
			State.GeneratedCenturyIds.Sort();
		}
		State.LastCompletedCenturyId = Math.Max(State.LastCompletedCenturyId, copy.CenturyId);
		State.LastGeneratedYear = Math.Max(State.LastGeneratedYear, copy.GeneratedYear);
		Trim();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.CenturyAnnals | XjCodexDirtyFlags.History | XjCodexDirtyFlags.World);
		return true;
	}

	internal static void ImportFamilyStageStates(IReadOnlyList<XjCenturyFamilyStageStateRecord> states)
	{
		State ??= new XjCenturyAnnalsStateRecord();
		State.FamilyStageStates.Clear();
		if (states == null) return;
		int start = Math.Max(0, states.Count - XjCenturyAnnalsSchema.MaxFamilyStageStates);
		for (int i = start; i < states.Count; i++)
		{
			XjCenturyFamilyStageStateRecord copy = Clone(states[i]);
			if (copy == null || copy.FamilyStableId <= 0L) continue;
			State.FamilyStageStates.Add(copy);
		}
		NormalizeFamilyStageStates(State.FamilyStageStates);
	}

	internal static void UpdateFamilyStageStates(IReadOnlyList<XjCenturyFamilyStageStateRecord> states)
	{
		ImportFamilyStageStates(states);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void ObserveDomainEvent(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.Year <= 0 || domainEvent.FamilyStableId <= 0L)
		{
			return;
		}

		int centuryId = ResolveCenturyId(domainEvent.Year);
		if (centuryId <= 0)
		{
			return;
		}

		int importance = ResolveDomainEventImportance(domainEvent);
		if (importance <= 0)
		{
			return;
		}

		AppendLedgerRecord(new XjCenturyAnnalsLedgerRecord
		{
			CenturyId = centuryId,
			FamilyStableId = domainEvent.FamilyStableId,
			FamilyName = ResolveLedgerFamilyName(domainEvent.FamilyStableId),
			EventType = domainEvent.EventType,
			Year = domainEvent.Year,
			ActorId = domainEvent.ActorId,
			ActorName = domainEvent.ActorName,
			DaoTu = domainEvent.DaoTu,
			Grade = domainEvent.GongFaGrade,
			ItemClass = string.IsNullOrWhiteSpace(domainEvent.FaBaoClass) ? domainEvent.CaiQiFaName : domainEvent.FaBaoClass,
			SectId = domainEvent.ZongMenId,
			SectName = domainEvent.ZongMenName,
			Importance = importance,
			Summary = BuildDomainEventSummary(domainEvent)
		});
	}

	internal static void ObserveDeathSnapshot(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.Year <= 0 || snapshot.FamilyStableId <= 0L)
		{
			return;
		}

		int centuryId = ResolveCenturyId(snapshot.Year);
		if (centuryId <= 0)
		{
			return;
		}

		int importance = ResolveDeathImportance(snapshot);
		if (importance <= 0)
		{
			return;
		}

		AppendLedgerRecord(new XjCenturyAnnalsLedgerRecord
		{
			CenturyId = centuryId,
			FamilyStableId = snapshot.FamilyStableId,
			FamilyName = ResolveLedgerFamilyName(snapshot.FamilyStableId),
			EventType = snapshot.IsJieLinXian ? "JieLinDeath" : "ActorDeath",
			Year = snapshot.Year,
			ActorId = snapshot.ActorId,
			ActorName = snapshot.Name,
			DaoTu = snapshot.DaoTu,
			Grade = snapshot.GongFaGrade,
			ItemClass = snapshot.FaBaoClass,
			Importance = importance,
			Summary = BuildDeathSummary(snapshot)
		});
	}

	internal static void ObserveSectEvent(
		string eventType,
		int year,
		long sectId,
		string sectName,
		int importance,
		string summary,
		long actorId = 0L,
		string actorName = "",
		long familyStableId = 0L,
		string daoTu = "")
	{
		if (year <= 0 || sectId <= 0L || importance <= 0)
		{
			return;
		}

		int centuryId = ResolveCenturyId(year);
		if (centuryId <= 0)
		{
			return;
		}

		AppendLedgerRecord(new XjCenturyAnnalsLedgerRecord
		{
			CenturyId = centuryId,
			FamilyStableId = Math.Max(0L, familyStableId),
			FamilyName = ResolveLedgerFamilyName(familyStableId),
			EventType = eventType ?? string.Empty,
			Year = year,
			ActorId = Math.Max(0L, actorId),
			ActorName = actorName ?? string.Empty,
			DaoTu = daoTu ?? string.Empty,
			SectId = sectId,
			SectName = sectName ?? string.Empty,
			Importance = importance,
			Summary = summary ?? string.Empty
		});
	}

	internal static void ObserveFamilyEvent(
		string eventType,
		int year,
		long familyStableId,
		int importance,
		string summary,
		long actorId = 0L,
		string actorName = "",
		long sectId = 0L,
		string sectName = "")
	{
		if (year <= 0 || familyStableId <= 0L || importance <= 0)
		{
			return;
		}

		int centuryId = ResolveCenturyId(year);
		if (centuryId <= 0)
		{
			return;
		}

		AppendLedgerRecord(new XjCenturyAnnalsLedgerRecord
		{
			CenturyId = centuryId,
			FamilyStableId = familyStableId,
			FamilyName = ResolveLedgerFamilyName(familyStableId),
			EventType = eventType ?? string.Empty,
			Year = year,
			ActorId = Math.Max(0L, actorId),
			ActorName = actorName ?? string.Empty,
			SectId = Math.Max(0L, sectId),
			SectName = sectName ?? string.Empty,
			Importance = importance,
			Summary = summary ?? string.Empty
		});
	}

	internal static void AppendLedgerRecord(XjCenturyAnnalsLedgerRecord record)
	{
		XjCenturyAnnalsLedgerRecord copy = Normalize(Clone(record));
		if (copy == null) return;
		Ledger.Add(copy);
		if (Ledger.Count > XjCenturyAnnalsSchema.MaxLedgerRecords)
		{
			Ledger.RemoveRange(0, Ledger.Count - XjCenturyAnnalsSchema.MaxLedgerRecords);
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool HasCentury(int centuryId)
	{
		return centuryId > 0 && RecordsByCenturyId.ContainsKey(centuryId);
	}

	internal static int ResolveCenturyId(int year)
	{
		if (year <= 0) return 0;
		TryEnsureBaseWorldYear(year, out int baseWorldYear);
		return ResolveCenturyIdFromBase(year, baseWorldYear);
	}

	internal static void Clear()
	{
		Records.Clear();
		RecordsByCenturyId.Clear();
		Ledger.Clear();
		State = new XjCenturyAnnalsStateRecord();
		TimelineRepairPending = false;
		ImportedSchemaVersion = 0;
	}

	private static void RebuildOrderedRecords()
	{
		Records.Clear();
		foreach (XjCenturyAnnalsArchiveRecord record in RecordsByCenturyId.Values)
		{
			Records.Add(record);
		}
		Records.Sort((left, right) => left.CenturyId.CompareTo(right.CenturyId));
	}

	private static void RepairStateFromRecords()
	{
		State ??= new XjCenturyAnnalsStateRecord();
		State.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
		HashSet<int> generated = new HashSet<int>();
		int lastCompletedCenturyId = 0;
		int lastGeneratedYear = 0;
		for (int i = 0; i < Records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = Records[i];
			if (record == null || record.CenturyId <= 0) continue;
			generated.Add(record.CenturyId);
			lastCompletedCenturyId = Math.Max(lastCompletedCenturyId, record.CenturyId);
			lastGeneratedYear = Math.Max(lastGeneratedYear, record.GeneratedYear);
		}
		// 卷次状态是现存归档的派生索引。迁移后必须从真实卷重建，不能继续
		// 合并旧档中的悬空卷号，否则“只有两卷却记作五卷”会长期残留。
		State.GeneratedCenturyIds = new List<int>(generated);
		State.GeneratedCenturyIds.Sort();
		State.LastCompletedCenturyId = lastCompletedCenturyId;
		State.LastGeneratedYear = lastGeneratedYear;
		NormalizeFamilyStageStates(State.FamilyStageStates);
	}

	private static void Trim()
	{
		while (Records.Count > XjCenturyAnnalsSchema.MaxCenturyRecords)
		{
			XjCenturyAnnalsArchiveRecord oldest = Records[0];
			Records.RemoveAt(0);
			if (oldest != null) RecordsByCenturyId.Remove(oldest.CenturyId);
		}
		if (State?.GeneratedCenturyIds != null && State.GeneratedCenturyIds.Count > XjCenturyAnnalsSchema.MaxCenturyRecords)
		{
			State.GeneratedCenturyIds.RemoveRange(0, State.GeneratedCenturyIds.Count - XjCenturyAnnalsSchema.MaxCenturyRecords);
		}
		TrimFamilyStageStates(State?.FamilyStageStates);
	}

	private static void TrimFamilyStageStates(List<XjCenturyFamilyStageStateRecord> states)
	{
		if (states == null || states.Count <= XjCenturyAnnalsSchema.MaxFamilyStageStates) return;
		states.Sort((left, right) =>
		{
			int updated = right.LastUpdatedYear.CompareTo(left.LastUpdatedYear);
			return updated != 0 ? updated : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		states.RemoveRange(XjCenturyAnnalsSchema.MaxFamilyStageStates, states.Count - XjCenturyAnnalsSchema.MaxFamilyStageStates);
	}

	private static int ResolveImportedSchemaVersion(
		IReadOnlyList<XjCenturyAnnalsArchiveRecord> records,
		XjCenturyAnnalsStateRecord state,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (state?.SchemaVersion > 0) return state.SchemaVersion;
		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				if (records[i]?.SchemaVersion > 0) return records[i].SchemaVersion;
			}
		}
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				if (ledger[i]?.SchemaVersion > 0) return ledger[i].SchemaVersion;
			}
		}
		return 0;
	}

	private static bool RequiresMigration(
		IReadOnlyList<XjCenturyAnnalsArchiveRecord> records,
		XjCenturyAnnalsStateRecord state,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (state == null || state.SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion) return true;
		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjCenturyAnnalsArchiveRecord record = records[i];
				if (record == null || record.SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion) return true;
				if (record.FamilyStates == null) return true;
				for (int f = 0; f < record.FamilyStates.Count; f++)
				{
					if (record.FamilyStates[f] == null || record.FamilyStates[f].SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion) return true;
				}
			}
		}
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				if (ledger[i] == null || ledger[i].SchemaVersion != XjCenturyAnnalsSchema.CurrentVersion) return true;
			}
		}
		return false;
	}

	private static void NormalizeFamilyStageStates(List<XjCenturyFamilyStageStateRecord> states)
	{
		if (states == null) return;
		Dictionary<long, XjCenturyFamilyStageStateRecord> latestByFamily = new Dictionary<long, XjCenturyFamilyStageStateRecord>();
		for (int i = 0; i < states.Count; i++)
		{
			XjCenturyFamilyStageStateRecord item = states[i];
			if (item == null || item.FamilyStableId <= 0L) continue;
			item.Stage = item.Stage?.Trim() ?? string.Empty;
			item.FirstRecordedYear = Math.Max(0, item.FirstRecordedYear);
			item.LastUpdatedYear = Math.Max(item.FirstRecordedYear, item.LastUpdatedYear);
			item.ConsecutiveDeclineCycles = Math.Max(0, item.ConsecutiveDeclineCycles);
			item.ConsecutiveRecoveryCycles = Math.Max(0, item.ConsecutiveRecoveryCycles);
			item.ActiveAspiration = NormalizeAspiration(item.ActiveAspiration);
			item.SupportedActorId = Math.Max(0L, item.SupportedActorId);
			item.SupportPurpose = NormalizeSupportPurpose(item.SupportPurpose);
			item.SupportedSinceYear = Math.Max(0, item.SupportedSinceYear);
			item.LastSupportYear = Math.Max(0, item.LastSupportYear);
			item.LastClanLeaderActorId = Math.Max(0L, item.LastClanLeaderActorId);
			if (string.IsNullOrWhiteSpace(item.ActiveAspiration))
			{
				item.AspirationSinceYear = 0;
				item.SupportedActorId = 0L;
				item.SupportPurpose = string.Empty;
				item.SupportedSinceYear = 0;
				item.LastSupportYear = 0;
			}
			else
			{
				item.AspirationSinceYear = Math.Max(0, item.AspirationSinceYear);
				if (item.AspirationSinceYear <= 0)
				{
					item.AspirationSinceYear = item.LastUpdatedYear > 0 ? item.LastUpdatedYear : item.FirstRecordedYear;
				}
				if (item.SupportedActorId <= 0L || string.IsNullOrWhiteSpace(item.SupportPurpose))
				{
					item.SupportedActorId = 0L;
					item.SupportPurpose = string.Empty;
					item.SupportedSinceYear = 0;
				}
				else if (item.SupportedSinceYear <= 0)
				{
					item.SupportedSinceYear = item.LastUpdatedYear > 0 ? item.LastUpdatedYear : item.AspirationSinceYear;
				}
			}
			if (!latestByFamily.TryGetValue(item.FamilyStableId, out XjCenturyFamilyStageStateRecord existing)
				|| item.LastUpdatedYear >= existing.LastUpdatedYear)
			{
				latestByFamily[item.FamilyStableId] = item;
			}
		}
		states.Clear();
		foreach (XjCenturyFamilyStageStateRecord item in latestByFamily.Values) states.Add(item);
		states.Sort((left, right) =>
		{
			int updated = right.LastUpdatedYear.CompareTo(left.LastUpdatedYear);
			return updated != 0 ? updated : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		if (states.Count > XjCenturyAnnalsSchema.MaxFamilyStageStates)
		{
			states.RemoveRange(XjCenturyAnnalsSchema.MaxFamilyStageStates, states.Count - XjCenturyAnnalsSchema.MaxFamilyStageStates);
		}
	}

	private static string NormalizeAspiration(string aspiration)
	{
		string value = aspiration?.Trim() ?? string.Empty;
		if (string.Equals(value, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal)
			|| string.Equals(value, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal)
			|| string.Equals(value, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal)
			|| string.Equals(value, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal))
		{
			return value;
		}
		return string.Empty;
	}


	private static string NormalizeSupportPurpose(string purpose)
	{
		string value = purpose?.Trim() ?? string.Empty;
		if (string.Equals(value, XjCenturyFamilySupportPurpose.FuChiZhuJi, StringComparison.Ordinal)
			|| string.Equals(value, XjCenturyFamilySupportPurpose.FuChiZiFu, StringComparison.Ordinal)
			|| string.Equals(value, XjCenturyFamilySupportPurpose.FuChiQiuJin, StringComparison.Ordinal))
		{
			return value;
		}
		return string.Empty;
	}

	private static XjCenturyAnnalsArchiveRecord Normalize(XjCenturyAnnalsArchiveRecord record)
	{
		if (record == null) return null;
		int importedSchemaVersion = record.SchemaVersion;
		record.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
		record.CenturyId = Math.Max(0, record.CenturyId);
		record.StartYear = Math.Max(0, record.StartYear);
		record.EndYear = Math.Max(record.StartYear, record.EndYear);
		record.GeneratedYear = Math.Max(0, record.GeneratedYear);
		record.Title = record.Title?.Trim() ?? string.Empty;
		record.WorldSummary = record.WorldSummary?.Trim() ?? string.Empty;
		record.ImportantEventIds ??= new List<long>();
		record.FamilyStates ??= new List<XjCenturyFamilyStateRecord>();
		record.SectSummaries ??= new List<XjCenturySummaryItemRecord>();
		record.RealmStatistics ??= new List<XjCenturySummaryItemRecord>();
		record.DaoSummaries ??= new List<XjCenturySummaryItemRecord>();
		record.ArtifactSummaries ??= new List<XjCenturySummaryItemRecord>();
		record.RepresentativeActors ??= new List<XjCenturyActorHighlightRecord>();
		TrimList(record.ImportantEventIds, XjCenturyAnnalsSchema.MaxEventIdsPerRecord);
		NormalizeFamilyStates(record.FamilyStates);
		NormalizeSummaryItems(record.SectSummaries, XjCenturyAnnalsSchema.MaxSectSummariesPerCentury);
		NormalizeRealmStatistics(record.RealmStatistics, XjCenturyAnnalsSchema.MaxRealmSummariesPerCentury);
		NormalizeSummaryItems(record.DaoSummaries, XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury);
		NormalizeArtifactSummaryItems(record.ArtifactSummaries, XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		NormalizeActors(record.RepresentativeActors, XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		if (importedSchemaVersion < 9 || record.RepresentativeActors.Count == 0)
		{
			List<XjCenturyActorHighlightRecord> familyFallback = RebuildRepresentativeActorsFromVolume(record.FamilyStates);
			record.RepresentativeActors = XjCenturyAnnalsBuilder.BuildVolumeRepresentativeActors(
				record.StartYear, record.EndYear, familyFallback);
			NormalizeActors(record.RepresentativeActors, XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		}
		return record;
	}

	private static List<XjCenturyActorHighlightRecord> RebuildRepresentativeActorsFromVolume(
		IReadOnlyList<XjCenturyFamilyStateRecord> familyStates)
	{
		List<XjCenturyActorHighlightRecord> result = new List<XjCenturyActorHighlightRecord>();
		HashSet<long> seenActorIds = new HashSet<long>();
		if (familyStates == null) return result;
		for (int i = 0; i < familyStates.Count && result.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury; i++)
		{
			XjCenturyFamilyStateRecord family = familyStates[i];
			if (family == null) continue;
			if (family.ImportantActors == null) continue;
			for (int j = 0; j < family.ImportantActors.Count && result.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury; j++)
			{
				XjCenturyActorHighlightRecord actor = family.ImportantActors[j];
				if (actor == null || actor.ActorId <= 0L
					|| !XjHistoryNamePolicy.IsChineseActorName(actor.ActorName)
					|| string.IsNullOrWhiteSpace(actor.Reason)
					|| actor.Reason.IndexOf("家族代表", StringComparison.Ordinal) >= 0
					|| !seenActorIds.Add(actor.ActorId)) continue;
				result.Add(Clone(actor));
			}
		}
		return result;
	}

	private static XjCenturyAnnalsStateRecord Normalize(XjCenturyAnnalsStateRecord state)
	{
		state ??= new XjCenturyAnnalsStateRecord();
		state.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
		state.BaseWorldYear = Math.Max(0, state.BaseWorldYear);
		state.LastCompletedCenturyId = Math.Max(0, state.LastCompletedCenturyId);
		state.LastGeneratedYear = Math.Max(0, state.LastGeneratedYear);
		state.GeneratedCenturyIds ??= new List<int>();
		state.FamilyStageStates ??= new List<XjCenturyFamilyStageStateRecord>();
		HashSet<int> generated = new HashSet<int>();
		for (int i = 0; i < state.GeneratedCenturyIds.Count; i++)
		{
			if (state.GeneratedCenturyIds[i] > 0) generated.Add(state.GeneratedCenturyIds[i]);
		}
		state.GeneratedCenturyIds = new List<int>(generated);
		state.GeneratedCenturyIds.Sort();
		if (state.GeneratedCenturyIds.Count > XjCenturyAnnalsSchema.MaxCenturyRecords)
		{
			state.GeneratedCenturyIds.RemoveRange(0, state.GeneratedCenturyIds.Count - XjCenturyAnnalsSchema.MaxCenturyRecords);
		}
		NormalizeFamilyStageStates(state.FamilyStageStates);
		return state;
	}

	private static XjCenturyAnnalsLedgerRecord Normalize(XjCenturyAnnalsLedgerRecord record)
	{
		if (record == null) return null;
		record.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
		record.CenturyId = Math.Max(0, record.CenturyId);
		record.FamilyStableId = Math.Max(0L, record.FamilyStableId);
		record.FamilyName = record.FamilyName?.Trim() ?? string.Empty;
		if (record.FamilyStableId > 0L && !XjHistoryNamePolicy.IsChineseFamilyName(record.FamilyName))
		{
			record.FamilyName = ResolveLedgerFamilyName(record.FamilyStableId);
		}
		record.EventType = record.EventType?.Trim() ?? string.Empty;
		record.Year = Math.Max(0, record.Year);
		record.ActorId = Math.Max(0L, record.ActorId);
		record.ActorName = record.ActorName?.Trim() ?? string.Empty;
		if (record.ActorId > 0L && !XjHistoryNamePolicy.IsChineseActorName(record.ActorName))
		{
			record.ActorId = 0L;
			record.ActorName = string.Empty;
		}
		if (record.FamilyStableId > 0L && !XjHistoryNamePolicy.IsChineseFamilyName(record.FamilyName)
			&& XjHistoryNamePolicy.IsChineseActorName(record.ActorName))
		{
			string fromActor = XjFamilyDisplayNameResolver.FromActorName(record.ActorName);
			if (XjHistoryNamePolicy.IsChineseFamilyName(fromActor)) record.FamilyName = fromActor;
		}
		record.DaoTu = record.DaoTu?.Trim() ?? string.Empty;
		record.Grade = Math.Clamp(record.Grade, 0, 9);
		record.ItemClass = record.ItemClass?.Trim() ?? string.Empty;
		record.SectId = Math.Max(0L, record.SectId);
		record.SectName = record.SectName?.Trim() ?? string.Empty;
		record.Importance = Math.Clamp(record.Importance, 0, 5);
		record.Summary = record.Summary?.Trim() ?? string.Empty;
		if (IsSuppressedLedgerRecord(record.EventType, record.Summary)) return null;
		return record;
	}

	private static bool IsSuppressedLedgerRecord(string eventType, string summary)
	{
		string type = (eventType ?? string.Empty).Trim();
		string text = (summary ?? string.Empty).Trim();
		return type.StartsWith("FamilyClanLeader", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyLeader", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyPatriarch", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyRepresentative", StringComparison.OrdinalIgnoreCase)
			|| text.IndexOf("家主更替", StringComparison.Ordinal) >= 0
			|| text.IndexOf("接过家主印信", StringComparison.Ordinal) >= 0
			|| text.IndexOf("家族代表", StringComparison.Ordinal) >= 0;
	}

	private static string ResolveLedgerFamilyName(long familyStableId)
	{
		if (familyStableId <= 0L) return string.Empty;
		string name = XjFamilyDisplayNameResolver.Resolve(familyStableId);
		return XjHistoryNamePolicy.IsChineseFamilyName(name) ? name.Trim() : string.Empty;
	}

	private static void NormalizeFamilyStates(List<XjCenturyFamilyStateRecord> items)
	{
		if (items == null) return;
		items.RemoveAll(item => item == null || item.FamilyStableId <= 0L);
		for (int i = 0; i < items.Count; i++)
		{
			XjCenturyFamilyStateRecord item = items[i];
			item.SchemaVersion = XjCenturyAnnalsSchema.CurrentVersion;
			item.FamilyName = item.FamilyName?.Trim() ?? string.Empty;
			item.PreviousStage = item.PreviousStage?.Trim() ?? string.Empty;
			item.CurrentStage = item.CurrentStage?.Trim() ?? string.Empty;
			item.HighestRealm = item.HighestRealm?.Trim() ?? string.Empty;
			item.SectName = item.SectName?.Trim() ?? string.Empty;
			item.VoiceTier = item.VoiceTier?.Trim() ?? string.Empty;
			item.CityInfluence = item.CityInfluence?.Trim() ?? string.Empty;
			item.CraftSummary = item.CraftSummary?.Trim() ?? string.Empty;
			item.GongFaSummary = item.GongFaSummary?.Trim() ?? string.Empty;
			item.FaBaoSummary = item.FaBaoSummary?.Trim() ?? string.Empty;
			item.RepresentativeActorName = item.RepresentativeActorName?.Trim() ?? string.Empty;
			item.StageReason = item.StageReason?.Trim() ?? string.Empty;
			item.Aspiration = NormalizeAspiration(item.Aspiration);
			item.AspirationStatus = item.AspirationStatus?.Trim() ?? string.Empty;
			item.AspirationSinceYear = string.IsNullOrWhiteSpace(item.Aspiration) ? 0 : Math.Max(0, item.AspirationSinceYear);
			item.AspirationSummary = item.AspirationSummary?.Trim() ?? string.Empty;
			item.SectRelation = item.SectRelation?.Trim() ?? string.Empty;
			item.SupportedActorId = Math.Max(0L, item.SupportedActorId);
			item.SupportedActorName = item.SupportedActorName?.Trim() ?? string.Empty;
			item.SupportPurpose = NormalizeSupportPurpose(item.SupportPurpose);
			item.SupportedSinceYear = item.SupportedActorId > 0L && !string.IsNullOrWhiteSpace(item.SupportPurpose)
				? Math.Max(0, item.SupportedSinceYear)
				: 0;
			item.PillarActorId = Math.Max(0L, item.PillarActorId);
			item.PillarActorName = item.PillarActorName?.Trim() ?? string.Empty;
			item.PillarRealm = item.PillarRealm?.Trim() ?? string.Empty;
			item.FamilyTreasureSummary = item.FamilyTreasureSummary?.Trim() ?? string.Empty;
			if (item.FamilyTreasureSummary.IndexOf("品功法", StringComparison.Ordinal) >= 0)
			{
				item.FamilyTreasureSummary = string.Empty;
			}
			item.ImportantEventIds ??= new List<long>();
			item.ImportantActors ??= new List<XjCenturyActorHighlightRecord>();
			TrimList(item.ImportantEventIds, XjCenturyAnnalsSchema.MaxEventIdsPerFamily);
			NormalizeActors(item.ImportantActors, XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		}
		items.RemoveAll(item => item == null || !XjHistoryNamePolicy.IsChineseFamilyName(item.FamilyName));
		items.Sort((left, right) =>
		{
			int score = right.InfluenceScore.CompareTo(left.InfluenceScore);
			if (score != 0) return score;
			int cultivators = right.CultivatorCount.CompareTo(left.CultivatorCount);
			return cultivators != 0 ? cultivators : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		if (items.Count > XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury)
		{
			items.RemoveRange(XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury, items.Count - XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury);
		}
	}

	private static void NormalizeArtifactSummaryItems(List<XjCenturySummaryItemRecord> items, int maxCount)
	{
		if (items == null) return;
		items.RemoveAll(item =>
		{
			if (item == null) return true;
			string text = (item.Name ?? string.Empty) + " " + (item.Trend ?? string.Empty) + " " + (item.Summary ?? string.Empty);
			return text.IndexOf("五品功法", StringComparison.Ordinal) >= 0
				|| text.IndexOf("品阶：五品", StringComparison.Ordinal) >= 0
				|| text.IndexOf("品阶:五品", StringComparison.Ordinal) >= 0;
		});
		NormalizeSummaryItems(items, maxCount);
	}

	private static void NormalizeSummaryItems(List<XjCenturySummaryItemRecord> items, int maxCount)
	{
		if (items == null) return;
		items.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.Name));
		for (int i = 0; i < items.Count; i++)
		{
			items[i].Key = items[i].Key?.Trim() ?? string.Empty;
			items[i].Name = items[i].Name?.Trim() ?? string.Empty;
			items[i].Trend = items[i].Trend?.Trim() ?? string.Empty;
			items[i].State = items[i].State?.Trim() ?? string.Empty;
			items[i].Summary = items[i].Summary?.Trim() ?? string.Empty;
		}
		items.Sort((left, right) =>
		{
			int score = right.Score.CompareTo(left.Score);
			if (score != 0) return score;
			int key = string.Compare(left.Key, right.Key, StringComparison.Ordinal);
			if (key != 0) return key;
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			if (name != 0) return name;
			int trend = string.Compare(left.Trend, right.Trend, StringComparison.Ordinal);
			if (trend != 0) return trend;
			int state = string.Compare(left.State, right.State, StringComparison.Ordinal);
			return state != 0 ? state : string.Compare(left.Summary, right.Summary, StringComparison.Ordinal);
		});
		if (items.Count > maxCount) items.RemoveRange(maxCount, items.Count - maxCount);
	}

	private static void NormalizeRealmStatistics(List<XjCenturySummaryItemRecord> items, int maxCount)
	{
		if (items == null) return;
		items.RemoveAll(item => item == null || string.IsNullOrWhiteSpace(item.Name));
		for (int i = 0; i < items.Count; i++)
		{
			items[i].Key = items[i].Key?.Trim() ?? string.Empty;
			items[i].Name = items[i].Name?.Trim() ?? string.Empty;
			items[i].Trend = items[i].Trend?.Trim() ?? string.Empty;
			items[i].State = items[i].State?.Trim() ?? string.Empty;
			items[i].Summary = items[i].Summary?.Trim() ?? string.Empty;
		}
		items.Sort((left, right) =>
		{
			int rank = RealmStatisticRank(left?.Key).CompareTo(RealmStatisticRank(right?.Key));
			if (rank != 0) return rank;
			int key = string.Compare(left?.Key, right?.Key, StringComparison.Ordinal);
			return key != 0 ? key : string.Compare(left?.Name, right?.Name, StringComparison.Ordinal);
		});
		if (items.Count > maxCount) items.RemoveRange(maxCount, items.Count - maxCount);
	}

	private static int RealmStatisticRank(string key)
	{
		return (key ?? string.Empty).Trim().ToLowerInvariant() switch
		{
			"cultivator" => 0,
			"taixi" => 1,
			"lianqi" => 2,
			"zhuji" => 3,
			"zifu" => 4,
			"jindan" => 5,
			"shendan" => 6,
			"jielin" => 7,
			_ => 100
		};
	}

	private static void NormalizeActors(List<XjCenturyActorHighlightRecord> items, int maxCount)
	{
		if (items == null) return;
		items.RemoveAll(item => item == null
			|| item.ActorId <= 0L
			|| !XjHistoryNamePolicy.IsChineseActorName(item.ActorName)
			|| string.IsNullOrWhiteSpace(item.Reason)
			|| item.Reason.IndexOf("家族代表", StringComparison.Ordinal) >= 0);
		for (int i = 0; i < items.Count; i++)
		{
			items[i].ActorName = items[i].ActorName?.Trim() ?? string.Empty;
			items[i].FamilyName = items[i].FamilyName?.Trim() ?? string.Empty;
			items[i].SectName = items[i].SectName?.Trim() ?? string.Empty;
			items[i].Realm = items[i].Realm?.Trim() ?? string.Empty;
			items[i].Reason = items[i].Reason?.Trim() ?? string.Empty;
		}
		items.Sort((left, right) =>
		{
			int realm = right.RealmOrder.CompareTo(left.RealmOrder);
			if (realm != 0) return realm;
			long leftId = left.ActorId > 0L ? left.ActorId : long.MaxValue;
			long rightId = right.ActorId > 0L ? right.ActorId : long.MaxValue;
			int actor = leftId.CompareTo(rightId);
			if (actor != 0) return actor;
			int name = string.Compare(left.ActorName, right.ActorName, StringComparison.Ordinal);
			return name != 0 ? name : string.Compare(left.Reason, right.Reason, StringComparison.Ordinal);
		});
		if (items.Count > maxCount) items.RemoveRange(maxCount, items.Count - maxCount);
	}

	private static int ResolveDomainEventImportance(in XjFamilyDomainEvent domainEvent)
	{
		string eventType = domainEvent.EventType ?? string.Empty;
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeBirth, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeFamilyMemberConfirmed, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeAptitudeGranted, StringComparison.Ordinal))
		{
			return 0;
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianDeath, StringComparison.Ordinal)
			|| (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianOpened, StringComparison.Ordinal)
				|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianClosed, StringComparison.Ordinal))
				&& !IsMeaningfulDongTianName(domainEvent.Source))
		{
			return 0;
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianSurvived, StringComparison.Ordinal))
		{
			return string.IsNullOrWhiteSpace(domainEvent.CaiQiFaSourcePlace) ? 1 : ResolveDongTianRewardImportance(domainEvent.CaiQiFaName, domainEvent.CaiQiFaSourcePlace);
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeJinDanSucceeded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeShenDanSucceeded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianOpened, StringComparison.Ordinal)
				&& IsMeaningfulDongTianName(domainEvent.Source)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianClosed, StringComparison.Ordinal)
				&& IsMeaningfulDongTianName(domainEvent.Source))
		{
			return 5;
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeRealmBreakthrough, StringComparison.Ordinal)
			&& (string.Equals(domainEvent.RealmId, "XjRealm4", StringComparison.Ordinal)
				|| string.Equals(domainEvent.RealmId, "XjRealm5", StringComparison.Ordinal)
				|| (domainEvent.RealmId ?? string.Empty).Contains("紫府")
				|| (domainEvent.RealmId ?? string.Empty).Contains("金丹")))
		{
			return 4;
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoUpgraded, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeJinDanGift, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeJinXingObtained, StringComparison.Ordinal))
		{
			return 3;
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeCaiQiFaObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeCaiQiCompleted, StringComparison.Ordinal))
		{
			return 2;
		}
		return 1;
	}

	private static string BuildDomainEventSummary(in XjFamilyDomainEvent domainEvent)
	{
		string eventType = domainEvent.EventType ?? string.Empty;
		string actorName = string.IsNullOrWhiteSpace(domainEvent.ActorName) ? "未名族人" : domainEvent.ActorName.Trim();
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeShenDanSucceeded, StringComparison.Ordinal))
		{
			string anchorName = string.IsNullOrWhiteSpace(domainEvent.Source) ? "金丹真君" : domainEvent.Source.Trim();
			return actorName + "托果于" + anchorName + "成神丹";
		}
		if (!string.IsNullOrWhiteSpace(domainEvent.GuoWei)) return actorName + "得" + domainEvent.GuoWei;
		if (!string.IsNullOrWhiteSpace(domainEvent.FaBaoName)) return actorName + "得" + domainEvent.FaBaoName;
		if (!string.IsNullOrWhiteSpace(domainEvent.GongFaName)) return BuildGongFaSummary(actorName, domainEvent);
		if (!string.IsNullOrWhiteSpace(domainEvent.QiuJinFaName)) return BuildQiuJinFaSummary(actorName, domainEvent);
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianSurvived, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(domainEvent.CaiQiFaSourcePlace))
		{
			string dongTian = string.IsNullOrWhiteSpace(domainEvent.Source) ? "洞天" : domainEvent.Source.Trim();
			return actorName + "于" + dongTian + "得" + domainEvent.CaiQiFaSourcePlace.Trim();
		}
		if (!string.IsNullOrWhiteSpace(domainEvent.CaiQiFaName)) return actorName + "得" + domainEvent.CaiQiFaName;
		if (!string.IsNullOrWhiteSpace(domainEvent.RealmId)) return actorName + "境界有成";
		if (!string.IsNullOrWhiteSpace(domainEvent.Source) && IsMeaningfulDongTianName(domainEvent.Source)) return actorName + "经历" + domainEvent.Source;
		return actorName + "触发" + eventType;
	}

	private static int ResolveDongTianRewardImportance(string rewardType, string rewardSummary)
	{
		string type = rewardType ?? string.Empty;
		string summary = rewardSummary ?? string.Empty;
		if (type.Contains("FaBao")
			|| type.Contains("JinXing")
			|| type.Contains("SecretRealm")
			|| summary.Contains("法宝")
			|| summary.Contains("金性")
			|| summary.Contains("洞天法"))
		{
			return 4;
		}
		if (type.Contains("GongFa")
			|| type.Contains("AlchemyRecipe")
			|| summary.Contains("功法")
			|| summary.Contains("丹方"))
		{
			return 3;
		}
		return 2;
	}

	private static string BuildGongFaSummary(string actorName, in XjFamilyDomainEvent domainEvent)
	{
		string source = ResolveDomainEventSourceText(domainEvent.Source);
		string grade = FormatGongFaGrade(domainEvent.GongFaGrade);
		string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? string.Empty : "，道途" + domainEvent.DaoTu.Trim();
		string prefix = string.IsNullOrWhiteSpace(source) ? actorName + "得" : actorName + "于" + source + "得";
		return prefix + grade + "功法《" + domainEvent.GongFaName.Trim() + "》" + daoTu;
	}

	private static string BuildQiuJinFaSummary(string actorName, in XjFamilyDomainEvent domainEvent)
	{
		string source = ResolveDomainEventSourceText(domainEvent.Source);
		string daoTu = string.IsNullOrWhiteSpace(domainEvent.DaoTu) ? string.Empty : "，道途" + domainEvent.DaoTu.Trim();
		string prefix = string.IsNullOrWhiteSpace(source) ? actorName + "悟" : actorName + "于" + source + "悟";
		return prefix + "求金法《" + domainEvent.QiuJinFaName.Trim() + "》" + daoTu;
	}

	private static string ResolveDomainEventSourceText(string source)
	{
		string value = (source ?? string.Empty).Trim();
		return XjDisplayNameSanitizer.EventSource(value);
	}

	private static string FormatGongFaGrade(int grade)
	{
		return grade > 0 ? XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade) : string.Empty;
	}

	private static int ResolveDeathImportance(in XjDeathSnapshot snapshot)
	{
		if (snapshot.IsJieLinXian
			|| XjRealmHelper.IsRealm(snapshot.RealmId, "JinDan")
			|| XjRealmHelper.IsRealm(snapshot.RealmId, "ShenDan")
			|| !string.IsNullOrWhiteSpace(snapshot.GuoWei)
			|| !string.IsNullOrWhiteSpace(snapshot.GuoWeiZhongAi))
		{
			return 5;
		}
		if (XjRealmHelper.IsRealm(snapshot.RealmId, "ZiFu"))
		{
			return 4;
		}
		if (!string.IsNullOrWhiteSpace(snapshot.FaBao)
			|| !string.IsNullOrWhiteSpace(snapshot.QiuJinFaName)
			|| snapshot.GongFaGrade >= 5)
		{
			return 3;
		}
		return 0;
	}

	private static string BuildDeathSummary(in XjDeathSnapshot snapshot)
	{
		string actorName = string.IsNullOrWhiteSpace(snapshot.Name) ? "未名修士" : snapshot.Name.Trim();
		string realm = XjRealmHelper.GetDisplayName(snapshot.RealmId);
		List<string> parts = new List<string>();
		if (snapshot.IsJieLinXian) parts.Add("结璘仙陨落");
		else if (!string.IsNullOrWhiteSpace(realm)) parts.Add(realm + "陨落");
		else parts.Add("身故");
		if (!string.IsNullOrWhiteSpace(snapshot.GuoWei)) parts.Add("果位" + snapshot.GuoWei);
		if (!string.IsNullOrWhiteSpace(snapshot.JinXing)) parts.Add("金性" + snapshot.JinXing);
		if (!string.IsNullOrWhiteSpace(snapshot.FaBao)) parts.Add("遗法宝" + snapshot.FaBao);
		if (!string.IsNullOrWhiteSpace(snapshot.QiuJinFaName)) parts.Add("留求金法" + snapshot.QiuJinFaName);
		string reason = XjDisplayNameSanitizer.DeathReason(snapshot.ReasonCode);
		if (!string.IsNullOrWhiteSpace(reason))
		{
			parts.Add(reason);
		}
		return actorName + "：" + string.Join("，", parts);
	}

	private static bool IsMeaningfulDongTianName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		return text.Length > 0
			&& !string.Equals(text, "无", StringComparison.Ordinal)
			&& !string.Equals(text, "暂无", StringComparison.Ordinal)
			&& !text.EndsWith("：无", StringComparison.Ordinal)
			&& !text.EndsWith(":无", StringComparison.Ordinal)
			&& !text.Contains("洞天：无")
			&& !text.Contains("洞天:无");
	}

	private static void TrimList<T>(List<T> items, int maxCount)
	{
		if (items == null || maxCount <= 0 || items.Count <= maxCount) return;
		items.RemoveRange(maxCount, items.Count - maxCount);
	}

	private static XjCenturyAnnalsArchiveRecord Clone(XjCenturyAnnalsArchiveRecord source)
	{
		if (source == null) return null;
		XjCenturyAnnalsArchiveRecord copy = new XjCenturyAnnalsArchiveRecord
		{
			SchemaVersion = source.SchemaVersion,
			CenturyId = source.CenturyId,
			StartYear = source.StartYear,
			EndYear = source.EndYear,
			GeneratedYear = source.GeneratedYear,
			IsCompleteCycle = source.IsCompleteCycle,
			Title = source.Title ?? string.Empty,
			WorldSummary = source.WorldSummary ?? string.Empty,
			ImportantEventIds = source.ImportantEventIds == null ? new List<long>() : new List<long>(source.ImportantEventIds),
			FamilyStates = new List<XjCenturyFamilyStateRecord>(),
			SectSummaries = new List<XjCenturySummaryItemRecord>(),
			RealmStatistics = new List<XjCenturySummaryItemRecord>(),
			DaoSummaries = new List<XjCenturySummaryItemRecord>(),
			ArtifactSummaries = new List<XjCenturySummaryItemRecord>(),
			RepresentativeActors = new List<XjCenturyActorHighlightRecord>()
		};
		if (source.FamilyStates != null) for (int i = 0; i < source.FamilyStates.Count; i++) copy.FamilyStates.Add(Clone(source.FamilyStates[i]));
		if (source.SectSummaries != null) for (int i = 0; i < source.SectSummaries.Count; i++) copy.SectSummaries.Add(Clone(source.SectSummaries[i]));
		if (source.RealmStatistics != null) for (int i = 0; i < source.RealmStatistics.Count; i++) copy.RealmStatistics.Add(Clone(source.RealmStatistics[i]));
		if (source.DaoSummaries != null) for (int i = 0; i < source.DaoSummaries.Count; i++) copy.DaoSummaries.Add(Clone(source.DaoSummaries[i]));
		if (source.ArtifactSummaries != null) for (int i = 0; i < source.ArtifactSummaries.Count; i++) copy.ArtifactSummaries.Add(Clone(source.ArtifactSummaries[i]));
		if (source.RepresentativeActors != null) for (int i = 0; i < source.RepresentativeActors.Count; i++) copy.RepresentativeActors.Add(Clone(source.RepresentativeActors[i]));
		return copy;
	}

	private static XjCenturyFamilyStateRecord Clone(XjCenturyFamilyStateRecord source)
	{
		if (source == null) return null;
		XjCenturyFamilyStateRecord copy = new XjCenturyFamilyStateRecord
		{
			SchemaVersion = source.SchemaVersion,
			FamilyStableId = source.FamilyStableId,
			FamilyName = source.FamilyName ?? string.Empty,
			PreviousStage = source.PreviousStage ?? string.Empty,
			CurrentStage = source.CurrentStage ?? string.Empty,
			FirstRecordedYear = source.FirstRecordedYear,
			IsExtinct = source.IsExtinct,
			AliveCount = source.AliveCount,
			TotalCount = source.TotalCount,
			CultivatorCount = source.CultivatorCount,
			ZiFuCount = source.ZiFuCount,
			JinDanCount = source.JinDanCount,
			HighestRealm = source.HighestRealm ?? string.Empty,
			HighestRealmOrder = source.HighestRealmOrder,
			SectId = source.SectId,
			SectName = source.SectName ?? string.Empty,
			VoiceTier = source.VoiceTier ?? string.Empty,
			InfluenceScore = source.InfluenceScore,
			GoverningCityCount = source.GoverningCityCount,
			CityInfluence = source.CityInfluence ?? string.Empty,
			CraftSummary = source.CraftSummary ?? string.Empty,
			GongFaSummary = source.GongFaSummary ?? string.Empty,
			FaBaoSummary = source.FaBaoSummary ?? string.Empty,
			RepresentativeActorId = source.RepresentativeActorId,
			RepresentativeActorName = source.RepresentativeActorName ?? string.Empty,
			ImportantEventIds = source.ImportantEventIds == null ? new List<long>() : new List<long>(source.ImportantEventIds),
			ImportantActors = new List<XjCenturyActorHighlightRecord>(),
			StageReason = source.StageReason ?? string.Empty,
			Aspiration = source.Aspiration ?? string.Empty,
			AspirationStatus = source.AspirationStatus ?? string.Empty,
			AspirationSinceYear = source.AspirationSinceYear,
			AspirationSummary = source.AspirationSummary ?? string.Empty,
			SectRelation = source.SectRelation ?? string.Empty,
			SupportedActorId = source.SupportedActorId,
			SupportedActorName = source.SupportedActorName ?? string.Empty,
			SupportPurpose = source.SupportPurpose ?? string.Empty,
			SupportedSinceYear = source.SupportedSinceYear,
			PillarActorId = source.PillarActorId,
			PillarActorName = source.PillarActorName ?? string.Empty,
			PillarRealm = source.PillarRealm ?? string.Empty,
			FamilyTreasureSummary = source.FamilyTreasureSummary ?? string.Empty
		};
		if (source.ImportantActors != null) for (int i = 0; i < source.ImportantActors.Count; i++) copy.ImportantActors.Add(Clone(source.ImportantActors[i]));
		return copy;
	}

	private static XjCenturyActorHighlightRecord Clone(XjCenturyActorHighlightRecord source)
	{
		if (source == null) return null;
		return new XjCenturyActorHighlightRecord
		{
			ActorId = source.ActorId,
			ActorName = source.ActorName ?? string.Empty,
			FamilyStableId = source.FamilyStableId,
			FamilyName = source.FamilyName ?? string.Empty,
			SectId = source.SectId,
			SectName = source.SectName ?? string.Empty,
			Realm = source.Realm ?? string.Empty,
			RealmOrder = source.RealmOrder,
			Reason = source.Reason ?? string.Empty
		};
	}

	private static XjCenturySummaryItemRecord Clone(XjCenturySummaryItemRecord source)
	{
		if (source == null) return null;
		return new XjCenturySummaryItemRecord
		{
			Key = source.Key ?? string.Empty,
			Name = source.Name ?? string.Empty,
			Score = source.Score,
			Trend = source.Trend ?? string.Empty,
			State = source.State ?? string.Empty,
			Summary = source.Summary ?? string.Empty
		};
	}

	private static XjCenturyAnnalsStateRecord Clone(XjCenturyAnnalsStateRecord source)
	{
		if (source == null) return new XjCenturyAnnalsStateRecord();
		XjCenturyAnnalsStateRecord copy = new XjCenturyAnnalsStateRecord
		{
			SchemaVersion = source.SchemaVersion,
			BaseWorldYear = source.BaseWorldYear,
			LastCompletedCenturyId = source.LastCompletedCenturyId,
			LastGeneratedYear = source.LastGeneratedYear,
			GeneratedCenturyIds = source.GeneratedCenturyIds == null ? new List<int>() : new List<int>(source.GeneratedCenturyIds),
			FamilyStageStates = new List<XjCenturyFamilyStageStateRecord>()
		};
		if (source.FamilyStageStates != null)
		{
			for (int i = 0; i < source.FamilyStageStates.Count; i++)
			{
				copy.FamilyStageStates.Add(Clone(source.FamilyStageStates[i]));
			}
		}
		return copy;
	}

	private static XjCenturyFamilyStageStateRecord Clone(XjCenturyFamilyStageStateRecord source)
	{
		if (source == null) return null;
		return new XjCenturyFamilyStageStateRecord
		{
			FamilyStableId = source.FamilyStableId,
			Stage = source.Stage ?? string.Empty,
			FirstRecordedYear = source.FirstRecordedYear,
			LastUpdatedYear = source.LastUpdatedYear,
			LastScore = source.LastScore,
			ConsecutiveDeclineCycles = source.ConsecutiveDeclineCycles,
			ConsecutiveRecoveryCycles = source.ConsecutiveRecoveryCycles,
			WasExtinct = source.WasExtinct,
			ActiveAspiration = source.ActiveAspiration ?? string.Empty,
			AspirationSinceYear = source.AspirationSinceYear,
			SupportedActorId = source.SupportedActorId,
			SupportPurpose = source.SupportPurpose ?? string.Empty,
			SupportedSinceYear = source.SupportedSinceYear,
			LastSupportYear = source.LastSupportYear,
			LastClanLeaderActorId = source.LastClanLeaderActorId
		};
	}

	private static XjCenturyAnnalsLedgerRecord Clone(XjCenturyAnnalsLedgerRecord source)
	{
		if (source == null) return null;
		return new XjCenturyAnnalsLedgerRecord
		{
			SchemaVersion = source.SchemaVersion,
			CenturyId = source.CenturyId,
			FamilyStableId = source.FamilyStableId,
			FamilyName = source.FamilyName ?? string.Empty,
			EventType = source.EventType ?? string.Empty,
			Year = source.Year,
			ActorId = source.ActorId,
			ActorName = source.ActorName ?? string.Empty,
			DaoTu = source.DaoTu ?? string.Empty,
			Grade = source.Grade,
			ItemClass = source.ItemClass ?? string.Empty,
			SectId = source.SectId,
			SectName = source.SectName ?? string.Empty,
			Importance = source.Importance,
			Summary = source.Summary ?? string.Empty
		};
	}
}
