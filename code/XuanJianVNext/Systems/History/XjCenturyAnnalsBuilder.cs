using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.History;

internal static class XjCenturyAnnalsBuilder
{
	private const int MaxHistoryEvidencePerCentury = 2048;
	private const int MaxLedgerEvidencePerCentury = 2048;

	internal static bool HasPendingCompletedCentury(int currentYear)
	{
		return ResolveNextPendingCompletedCenturyId(currentYear) > 0;
	}

	internal static int ResolveNextPendingCompletedCenturyId(int currentYear)
	{
		if (currentYear <= 0)
		{
			return 0;
		}
		XjCenturyAnnalsStore.TryEnsureBaseWorldYear(currentYear, out int baseWorldYear);
		if (baseWorldYear <= 0 || currentYear < baseWorldYear)
		{
			return 0;
		}

		int elapsedYears = currentYear - baseWorldYear + 1;
		int latestCompletedCenturyId = elapsedYears / XjCenturyAnnalsSchema.YearsPerCentury;
		int centuryId = ResolveEarliestRetainedCenturyId(latestCompletedCenturyId);
		while (centuryId <= latestCompletedCenturyId && XjCenturyAnnalsStore.HasCentury(centuryId))
		{
			centuryId++;
		}
		return centuryId > 0 && centuryId <= latestCompletedCenturyId ? centuryId : 0;
	}

	private static int ResolveEarliestRetainedCenturyId(int latestCompletedCenturyId)
	{
		return Math.Max(1, latestCompletedCenturyId - XjCenturyAnnalsSchema.MaxCenturyRecords + 1);
	}

	/// <summary>
	/// 读档后的纯历史缺卷可以直接从既有世界史/世谱账本补写，不需要重新扫描当前人口。
	/// 只认已经至少过去一整年的卷，当前刚结束的世纪仍交给统一年度检测快照生成，
	/// 防止把“今天的人口状态”倒灌到历史，也避免bootstrap触发全修士扫描。
	/// </summary>
	internal static bool HasPendingHistoricalBackfill(int currentYear)
	{
		int centuryId = ResolveNextPendingCompletedCenturyId(currentYear);
		if (centuryId <= 0) return false;
		XjCenturyAnnalsStore.TryEnsureBaseWorldYear(currentYear, out int baseWorldYear);
		if (baseWorldYear <= 0) return false;
		int endYear = baseWorldYear + centuryId * XjCenturyAnnalsSchema.YearsPerCentury - 1;
		return currentYear > endYear + 1;
	}

	internal static bool TryBuildOneHistoricalBackfill(int currentYear)
	{
		return HasPendingHistoricalBackfill(currentYear) && TryBuildAndStore(currentYear);
	}

	internal static bool TryBuildAndStore(int currentYear)
	{
		if (currentYear <= 0)
		{
			return false;
		}
		XjCenturyAnnalsStore.TryEnsureBaseWorldYear(currentYear, out int baseWorldYear);
		if (baseWorldYear <= 0 || currentYear < baseWorldYear)
		{
			return false;
		}

		int elapsedYears = currentYear - baseWorldYear + 1;
		int latestCompletedCenturyId = elapsedYears / XjCenturyAnnalsSchema.YearsPerCentury;
		int centuryId = ResolveEarliestRetainedCenturyId(latestCompletedCenturyId);
		while (centuryId <= latestCompletedCenturyId && XjCenturyAnnalsStore.HasCentury(centuryId))
		{
			centuryId++;
		}
		if (centuryId <= 0 || centuryId > latestCompletedCenturyId)
		{
			return false;
		}

		int startYear = baseWorldYear + (centuryId - 1) * XjCenturyAnnalsSchema.YearsPerCentury;
		int endYear = startYear + XjCenturyAnnalsSchema.YearsPerCentury - 1;
		int generatedYear = currentYear;
		bool isBackfill = currentYear > endYear + 1;
		// 世谱按目标世纪直接读取证据，而不是拿“最近N条”再筛。这样800年旧档补
		// 201—300年卷时，早期家族/宗门/器物不会被最近320条账本挤掉。
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger =
			XjCenturyAnnalsStore.ReadLedgerSnapshotForCentury(centuryId, MaxLedgerEvidencePerCentury);
		IReadOnlyList<XjWorldHistoryArchiveRecord> historyEvidence =
			XjWorldHistoryStore.ReadSnapshotForYears(startYear, endYear, MaxHistoryEvidencePerCentury);
		List<long> importantEventIds = ReadImportantWorldEventIds(historyEvidence, startYear, endYear);
		List<XjCenturyEventRecord> notableEvents = BuildNotableEvents(historyEvidence, ledger, startYear, endYear);
		List<XjCenturySummaryItemRecord> artifactSummaries = BuildArtifactSummaries(ledger, centuryId);
		List<XjCenturyFamilyStateRecord> familyStates = new List<XjCenturyFamilyStateRecord>();
		List<XjCenturyFamilyStageStateRecord> nextStageStates = new List<XjCenturyFamilyStageStateRecord>();
		List<XjCenturyActorHighlightRecord> representativeActors = new List<XjCenturyActorHighlightRecord>();
		List<XjCenturySummaryItemRecord> sectSummaries = new List<XjCenturySummaryItemRecord>();
		List<XjCenturySummaryItemRecord> realmStatistics = new List<XjCenturySummaryItemRecord>();
		List<XjCenturySummaryItemRecord> daoSummaries = new List<XjCenturySummaryItemRecord>();

		if (!isBackfill)
		{
			// 正常百年卷只构建一次当前世界投影；旧档补录不重复扫描家族与宗门，
			// 避免在高年份旧档中为几十个缺失卷次反复执行 O(家族+宗门) 聚合。
			XjCenturyAnnalsStateRecord previousState = XjCenturyAnnalsStore.ReadStateSnapshot();
			IReadOnlyList<XjCenturyAnnalsArchiveRecord> previousRecords = XjCenturyAnnalsStore.ReadSnapshot(1);
			XjCenturyAnnalsArchiveRecord previousRecord = previousRecords.Count > 0 ? previousRecords[0] : null;
			Dictionary<long, XjCenturyFamilyStageStateRecord> previousStages = BuildPreviousStageMap(previousState);
			IReadOnlyList<XjSectFamilySeatArchiveRecord> familySeats = XjSectRepository.ReadAllFamilySeats();
			IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
			IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
			Dictionary<long, XjSectFamilySeatArchiveRecord> seatsByFamily = BuildSeatMap(familySeats);
			Dictionary<long, int> governingCityCountByFamily = BuildGovernanceCounts(governance);
			Dictionary<long, string> sectNames = BuildSectNames(sects);
			Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> ledgerByFamily = BuildLedgerByFamily(ledger, centuryId);
			Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> ledgerBySect = BuildLedgerBySect(ledger, centuryId);
			IReadOnlyList<XjFamilyLedgerAggregate> familyAggregates = XjFamilyMemberLedger.ReadAggregateSnapshot();

			familyStates = BuildFamilyStates(
				familyAggregates,
				previousStages,
				seatsByFamily,
				governingCityCountByFamily,
				sectNames,
				ledgerByFamily,
				startYear,
				endYear,
				out nextStageStates,
				out representativeActors);

			sectSummaries = BuildSectSummaries(sects, familySeats, sectNames, ledgerBySect);
			if (!XjAnnualWorldDetectionStore.TryRead(currentYear, out realmStatistics, out daoSummaries))
			{
				// 百年卷只能消费统一检测车道已经完成的快照。这里绝不回退到
				// 同步全修士扫描；年度归约器会保持在 CenturyDetection 阶段，
				// 等待分帧聚合完成后再重试构建。
				return false;
			}
			realmStatistics ??= new List<XjCenturySummaryItemRecord>();
			daoSummaries ??= new List<XjCenturySummaryItemRecord>();
			ApplyDaoTransitions(daoSummaries, previousRecord?.DaoSummaries);
		}
		else
		{
			// 补卷不能伪造当年人口快照，但可以从仍保存的世界史与世谱账本中还原
			// “确实发生过什么”。因此补录家族/宗门/境界/道途均标为史料证见，
			// 不把当前世界状态倒灌到数百年前。
			familyStates = BuildBackfillFamilyStates(historyEvidence, ledger, startYear, endYear);
			sectSummaries = BuildBackfillSectSummaries(historyEvidence, ledger);
			realmStatistics = BuildBackfillRealmEvidence(historyEvidence, ledger);
			daoSummaries = BuildBackfillDaoEvidence(ledger);
		}

		// 代表人物首先来自本卷真实事件，不再以当前境界或必须达到紫府/金丹作为门槛。
		representativeActors = BuildVolumeRepresentativeActors(startYear, endYear, representativeActors, historyEvidence);

		XjCenturyAnnalsArchiveRecord record = new XjCenturyAnnalsArchiveRecord
		{
			CenturyId = centuryId,
			StartYear = startYear,
			EndYear = endYear,
			GeneratedYear = generatedYear,
			IsCompleteCycle = !isBackfill,
			Title = "第" + centuryId.ToString(CultureInfo.InvariantCulture) + "世谱",
			WorldSummary = isBackfill
				? BuildBackfillWorldSummary(centuryId, notableEvents, familyStates, sectSummaries, representativeActors, realmStatistics, daoSummaries, artifactSummaries)
				: BuildWorldSummary(centuryId, familyStates, sectSummaries, representativeActors, realmStatistics, daoSummaries, artifactSummaries),
			ImportantEventIds = importantEventIds,
			NotableEvents = notableEvents,
			FamilyStates = familyStates,
			SectSummaries = sectSummaries,
			RealmStatistics = realmStatistics,
			DaoSummaries = daoSummaries,
			ArtifactSummaries = artifactSummaries,
			RepresentativeActors = representativeActors
		};

		bool added = XjCenturyAnnalsStore.TryStoreRecord(record);
		// 同一卷被重复调用时，不能再次推进家族兴衰计数、志业或扶持阶段。
		// 只有新卷真实入库后才提交对应阶段状态。
		if (added)
		{
			XjWorldHistoryStore.MarkCenturyProcessed(startYear, endYear, importantEventIds);
			if (!isBackfill) XjCenturyAnnalsStore.UpdateFamilyStageStates(nextStageStates);
		}
		return added;
	}

	private static List<XjCenturyFamilyStateRecord> BuildFamilyStates(
		IReadOnlyList<XjFamilyLedgerAggregate> aggregates,
		IReadOnlyDictionary<long, XjCenturyFamilyStageStateRecord> previousStages,
		IReadOnlyDictionary<long, XjSectFamilySeatArchiveRecord> seatsByFamily,
		IReadOnlyDictionary<long, int> governingCityCountByFamily,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyDictionary<long, List<XjCenturyAnnalsLedgerRecord>> ledgerByFamily,
		int startYear,
		int endYear,
		out List<XjCenturyFamilyStageStateRecord> nextStageStates,
		out List<XjCenturyActorHighlightRecord> representativeActors)
	{
		List<XjCenturyFamilyStateRecord> result = new List<XjCenturyFamilyStateRecord>();
		nextStageStates = new List<XjCenturyFamilyStageStateRecord>();
		representativeActors = new List<XjCenturyActorHighlightRecord>();
		HashSet<long> seenFamilyIds = new HashSet<long>();

		for (int i = 0; aggregates != null && i < aggregates.Count; i++)
		{
			XjFamilyLedgerAggregate aggregate = aggregates[i];
			if (aggregate.FamilyStableId <= 0L || !seenFamilyIds.Add(aggregate.FamilyStableId)) continue;
			ledgerByFamily.TryGetValue(aggregate.FamilyStableId, out List<XjCenturyAnnalsLedgerRecord> familyLedger);
			string familyName = ResolveFamilyNameForAnnals(aggregate.FamilyStableId, familyLedger);
			if (!XjHistoryNamePolicy.IsChineseFamilyName(familyName)) continue;
			previousStages.TryGetValue(aggregate.FamilyStableId, out XjCenturyFamilyStageStateRecord previous);
			seatsByFamily.TryGetValue(aggregate.FamilyStableId, out XjSectFamilySeatArchiveRecord seat);
			governingCityCountByFamily.TryGetValue(aggregate.FamilyStableId, out int governingCityCount);
			XjFamilyInheritanceStatus inheritance = ReadFamilyInheritanceStatus(aggregate.FamilyStableId);
			int score = CalculateFamilyScore(aggregate, seat, governingCityCount, familyLedger);
			string stage = ResolveStage(aggregate, previous, score, seat, governingCityCount);
			XjFamilyAspirationResolution aspiration = ResolveFamilyAspiration(aggregate, previous, stage, inheritance, startYear, endYear);
			string previousStage = string.IsNullOrWhiteSpace(previous?.Stage) ? string.Empty : previous.Stage;
			bool extinct = aggregate.AliveCount <= 0 || aggregate.CultivatorCount <= 0 && aggregate.TotalCount > 0;
			int firstRecordedYear = previous?.FirstRecordedYear > 0 ? previous.FirstRecordedYear : startYear;
			long sectId = seat?.SectId ?? 0L;
			string sectName = sectId > 0L && sectNames.TryGetValue(sectId, out string resolvedSectName)
				? resolvedSectName
				: string.Empty;
			XjCenturyFamilyStateRecord item = new XjCenturyFamilyStateRecord
			{
				FamilyStableId = aggregate.FamilyStableId,
				FamilyName = familyName,
				PreviousStage = previousStage,
				CurrentStage = stage,
				FirstRecordedYear = firstRecordedYear,
				IsExtinct = extinct,
				AliveCount = aggregate.AliveCount,
				TotalCount = aggregate.TotalCount,
				CultivatorCount = aggregate.CultivatorCount,
				ZiFuCount = aggregate.ZiFuCount,
				JinDanCount = aggregate.JinDanCount,
				HighestRealm = aggregate.HighestRealm,
				HighestRealmOrder = aggregate.HighestRealmOrder,
				SectId = sectId,
				SectName = sectName,
				VoiceTier = seat?.VoiceTier ?? string.Empty,
				InfluenceScore = score,
				GongFaSummary = inheritance.BuildSummary(),
				GoverningCityCount = governingCityCount,
				CityInfluence = governingCityCount > 0 ? "治理" + governingCityCount.ToString(CultureInfo.InvariantCulture) + "城" : string.Empty,
				RepresentativeActorId = aggregate.RepresentativeActorId,
				RepresentativeActorName = aggregate.Representative,
				ImportantEventIds = BuildFamilyEventIds(familyLedger),
				ImportantActors = BuildFamilyActorHighlights(aggregate, familyName, sectId, sectName, familyLedger),
				StageReason = BuildStageReason(stage, previousStage, aggregate, score, seat, governingCityCount, familyLedger, firstRecordedYear, endYear),
				Aspiration = aspiration.DisplayAspiration,
				AspirationStatus = aspiration.Status,
				AspirationSinceYear = aspiration.SinceYear,
				AspirationSummary = aspiration.Summary,
				SectRelation = ResolveSectRelation(seat, startYear),
				SupportedActorId = previous?.SupportedActorId ?? 0L,
				SupportedActorName = ResolveActorName(previous?.SupportedActorId ?? 0L),
				SupportPurpose = previous?.SupportPurpose ?? string.Empty,
				SupportedSinceYear = previous?.SupportedSinceYear ?? 0,
				PillarActorId = aggregate.RepresentativeActorId,
				PillarActorName = aggregate.Representative,
				PillarRealm = aggregate.HighestRealm,
				FamilyTreasureSummary = XjFamilyHeritageProjection.ResolveClanTreasure(aggregate.FamilyStableId)
			};
			result.Add(item);
			if (!string.IsNullOrWhiteSpace(previousStage) && !string.Equals(previousStage, stage, StringComparison.Ordinal))
			{
				XjThreeBookWriter.RecordFamilyStageChanged(
					aggregate.FamilyStableId,
					familyName,
					previousStage,
					stage,
					endYear,
					item.StageReason);
			}
			nextStageStates.Add(BuildNextStageState(
				aggregate, previous, stage, score, extinct, firstRecordedYear, endYear, aspiration));
			AppendVolumeRepresentativeActors(representativeActors, item);
		}

		// 本卷有真实事件、但卷末已不在当前家族聚合中的家族仍应入谱。
		// 这可覆盖同卷内衰亡或迁移后失去当前聚合的家族，避免“沈氏家族未载”一类遗漏。
		if (ledgerByFamily != null)
		{
			foreach (KeyValuePair<long, List<XjCenturyAnnalsLedgerRecord>> pair in ledgerByFamily)
			{
				long familyId = pair.Key;
				List<XjCenturyAnnalsLedgerRecord> familyLedger = pair.Value;
				if (familyId <= 0L || seenFamilyIds.Contains(familyId) || familyLedger == null || familyLedger.Count == 0) continue;
				string familyName = ResolveFamilyNameForAnnals(familyId, familyLedger);
				if (!XjHistoryNamePolicy.IsChineseFamilyName(familyName)) continue;
				seenFamilyIds.Add(familyId);
				previousStages.TryGetValue(familyId, out XjCenturyFamilyStageStateRecord previous);
				seatsByFamily.TryGetValue(familyId, out XjSectFamilySeatArchiveRecord seat);
				long sectId = seat?.SectId ?? 0L;
				string sectName = sectId > 0L && sectNames.TryGetValue(sectId, out string resolvedSectName)
					? resolvedSectName
					: string.Empty;
				int score = 0;
				int firstEventYear = endYear;
				for (int j = 0; j < familyLedger.Count; j++)
				{
					XjCenturyAnnalsLedgerRecord ledgerItem = familyLedger[j];
					score += Math.Max(0, ledgerItem?.Importance ?? 0) * 4;
					if (ledgerItem != null && ledgerItem.Year > 0) firstEventYear = Math.Min(firstEventYear, ledgerItem.Year);
				}
				int firstRecordedYear = previous?.FirstRecordedYear > 0
					? previous.FirstRecordedYear
					: Math.Max(startYear, firstEventYear);
				XjFamilyLedgerAggregate extinctAggregate = new XjFamilyLedgerAggregate(
					familyId, familyName, 0, 0, 0, 0, 0,
					0, 0, 0, 0,
					string.Empty, 0, string.Empty, 0L);
				XjCenturyFamilyStateRecord item = new XjCenturyFamilyStateRecord
				{
					FamilyStableId = familyId,
					FamilyName = familyName,
					PreviousStage = previous?.Stage ?? string.Empty,
					CurrentStage = XjCenturyFamilyStage.FuMie,
					FirstRecordedYear = firstRecordedYear,
					IsExtinct = true,
					SectId = sectId,
					SectName = sectName,
					VoiceTier = seat?.VoiceTier ?? string.Empty,
					InfluenceScore = score,
					ImportantEventIds = BuildFamilyEventIds(familyLedger),
					ImportantActors = BuildFamilyActorHighlights(extinctAggregate, familyName, sectId, sectName, familyLedger),
					StageReason = "本卷仍有族人事迹可考，卷末已不见存续修士。",
					FamilyTreasureSummary = XjFamilyHeritageProjection.ResolveClanTreasure(familyId)
				};
				result.Add(item);
				if (!string.Equals(previous?.Stage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal))
				{
					XjThreeBookWriter.RecordFamilyStageChanged(
						familyId,
						familyName,
						previous?.Stage ?? string.Empty,
						XjCenturyFamilyStage.FuMie,
						endYear,
						item.StageReason);
				}
				AppendVolumeRepresentativeActors(representativeActors, item);
				nextStageStates.Add(new XjCenturyFamilyStageStateRecord
				{
					FamilyStableId = familyId,
					Stage = XjCenturyFamilyStage.FuMie,
					FirstRecordedYear = firstRecordedYear,
					LastUpdatedYear = endYear,
					LastScore = score,
					WasExtinct = true
				});
			}
		}

		result.Sort((left, right) =>
		{
			int eventful = (right.ImportantEventIds?.Count > 0).CompareTo(left.ImportantEventIds?.Count > 0);
			if (eventful != 0) return eventful;
			int score = right.InfluenceScore.CompareTo(left.InfluenceScore);
			if (score != 0) return score;
			int cultivators = right.CultivatorCount.CompareTo(left.CultivatorCount);
			return cultivators != 0 ? cultivators : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		if (result.Count > XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury)
		{
			result.RemoveRange(XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury);
		}
		representativeActors.Sort((left, right) =>
		{
			int eventWeight = IsVolumeEventRepresentative(right).CompareTo(IsVolumeEventRepresentative(left));
			if (eventWeight != 0) return eventWeight;
			int realm = right.RealmOrder.CompareTo(left.RealmOrder);
			if (realm != 0) return realm;
			long leftId = left.ActorId > 0L ? left.ActorId : long.MaxValue;
			long rightId = right.ActorId > 0L ? right.ActorId : long.MaxValue;
			int actor = leftId.CompareTo(rightId);
			return actor != 0 ? actor : string.Compare(left.ActorName, right.ActorName, StringComparison.Ordinal);
		});
		if (representativeActors.Count > XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury)
		{
			representativeActors.RemoveRange(XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury, representativeActors.Count - XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		}
		return result;
	}

	private static string ResolveStage(
		in XjFamilyLedgerAggregate aggregate,
		XjCenturyFamilyStageStateRecord previous,
		int score,
		XjSectFamilySeatArchiveRecord seat,
		int governingCityCount)
	{
		if (aggregate.AliveCount <= 0 || aggregate.CultivatorCount <= 0 && aggregate.TotalCount > 0)
		{
			return XjCenturyFamilyStage.FuMie;
		}
		if (previous != null
			&& (string.Equals(previous.Stage, XjCenturyFamilyStage.ZhongShuai, StringComparison.Ordinal)
				|| previous.WasExtinct)
			&& (previous.ConsecutiveRecoveryCycles >= 1 || score >= previous.LastScore + 70)
			&& aggregate.CultivatorCount >= 3
			&& (aggregate.ZiFuCount > 0 || aggregate.JinDanCount > 0 || score >= 135))
		{
			return XjCenturyFamilyStage.FuZhen;
		}
		if (aggregate.JinDanCount > 0 || score >= 260)
		{
			return XjCenturyFamilyStage.DingSheng;
		}
		if (aggregate.ZiFuCount > 0 || score >= 135 || governingCityCount > 0 || seat?.VoiceScore >= 80f)
		{
			return XjCenturyFamilyStage.XingSheng;
		}
		if (previous != null
			&& previous.LastScore > 0
			&& (previous.ConsecutiveDeclineCycles >= 1 && score <= previous.LastScore - Math.Max(25, previous.LastScore / 4)
				|| score <= previous.LastScore - Math.Max(70, previous.LastScore / 3))
			&& aggregate.JinDanCount <= 0
			&& aggregate.ZiFuCount <= previous.LastScore / 120)
		{
			return XjCenturyFamilyStage.ZhongShuai;
		}
		return aggregate.CultivatorCount >= 2 || aggregate.TotalCount >= 5
			? XjCenturyFamilyStage.LiZu
			: XjCenturyFamilyStage.CaoChuang;
	}

	private static int CalculateFamilyScore(
		in XjFamilyLedgerAggregate aggregate,
		XjSectFamilySeatArchiveRecord seat,
		int governingCityCount,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> familyLedger)
	{
		float score = aggregate.AliveCount
			+ aggregate.CultivatorCount * 6f
			+ aggregate.ZiFuCount * 35f
			+ aggregate.JinDanCount * 140f
			+ aggregate.HighestRealmOrder * 12f
			+ governingCityCount * 28f;
		if (seat != null)
		{
			score += seat.VoiceScore * 0.35f + seat.ContributionScore * 0.2f + seat.CraftScore * 0.25f;
			score -= Math.Min(80f, Math.Max(0f, seat.SupplyDebt) * 0.25f);
			score += Math.Min(60f, Math.Max(0f, seat.PrivilegeHeat) * 0.2f);
		}
		if (familyLedger != null)
		{
			for (int i = 0; i < familyLedger.Count; i++)
			{
				score += Math.Max(0, familyLedger[i]?.Importance ?? 0) * 4f;
			}
		}
		return Math.Max(0, (int)Math.Round(score));
	}

	private static XjCenturyFamilyStageStateRecord BuildNextStageState(
		in XjFamilyLedgerAggregate aggregate,
		XjCenturyFamilyStageStateRecord previous,
		string stage,
		int score,
		bool extinct,
		int firstRecordedYear,
		int endYear,
		in XjFamilyAspirationResolution aspiration)
	{
		bool declining = previous != null && previous.LastScore > 0 && score < previous.LastScore;
		bool recovering = previous != null && previous.LastScore > 0 && score > previous.LastScore;
		// 家族志向只是资源方向，不拥有“轮换扶持对象”的权限。扶持对象跨百年长期保留，
		// 死亡/离族失效由 XjFamilySupportSystem 在运行期验证并清除。
		bool preserveSupport = previous != null && previous.SupportedActorId > 0L;
		return new XjCenturyFamilyStageStateRecord
		{
			FamilyStableId = aggregate.FamilyStableId,
			Stage = stage,
			// 首次入谱年必须沿用卷首/既有记录，不能误写为本卷末年。
			FirstRecordedYear = previous?.FirstRecordedYear > 0
				? previous.FirstRecordedYear
				: Math.Max(0, firstRecordedYear),
			LastUpdatedYear = endYear,
			LastScore = score,
			ConsecutiveDeclineCycles = declining ? (previous?.ConsecutiveDeclineCycles ?? 0) + 1 : 0,
			ConsecutiveRecoveryCycles = recovering ? (previous?.ConsecutiveRecoveryCycles ?? 0) + 1 : 0,
			WasExtinct = extinct,
			ActiveAspiration = aspiration.NextAspiration,
			AspirationSinceYear = aspiration.NextSinceYear,
			SupportedActorId = preserveSupport ? previous.SupportedActorId : 0L,
			SupportPurpose = preserveSupport ? previous.SupportPurpose : string.Empty,
			SupportedSinceYear = preserveSupport ? previous.SupportedSinceYear : 0,
			LastSupportYear = preserveSupport ? previous.LastSupportYear : 0,
			LastClanLeaderActorId = previous?.LastClanLeaderActorId ?? 0L
		};
	}

	private static string BuildStageReason(
		string stage,
		string previousStage,
		in XjFamilyLedgerAggregate aggregate,
		int score,
		XjSectFamilySeatArchiveRecord seat,
		int governingCityCount,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger,
		int firstRecordedYear,
		int endYear)
	{
		List<string> facts = new List<string>();
		string opening = "至" + XjChronology.FormatYear(endYear) + "，本族";
		if (!string.IsNullOrWhiteSpace(previousStage) && !string.Equals(previousStage, stage, StringComparison.Ordinal))
		{
			opening += string.Equals(previousStage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal)
				? "重入世谱，定为" + stage
				: "由" + previousStage + "转为" + stage;
		}
		else
		{
			opening += "仍处" + stage;
		}
		if (string.Equals(stage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal))
		{
			int years = firstRecordedYear > 0 && endYear >= firstRecordedYear ? endYear - firstRecordedYear + 1 : 0;
			if (years > 0) facts.Add("自入谱起存续" + years.ToString(CultureInfo.InvariantCulture) + "年");
			facts.Add("本卷传承断绝");
		}
		if (aggregate.JinDanCount > 0) facts.Add("有真君" + aggregate.JinDanCount.ToString(CultureInfo.InvariantCulture) + "人坐镇");
		else if (aggregate.ZiFuCount > 0) facts.Add("有真人" + aggregate.ZiFuCount.ToString(CultureInfo.InvariantCulture) + "人支撑门庭");
		else if (aggregate.CultivatorCount > 0) facts.Add("尚有修士" + aggregate.CultivatorCount.ToString(CultureInfo.InvariantCulture) + "人承续");
		if (governingCityCount > 0) facts.Add("治理" + governingCityCount.ToString(CultureInfo.InvariantCulture) + "城");
		if (seat != null && !string.IsNullOrWhiteSpace(seat.VoiceTier)) facts.Add("宗门位次为" + seat.VoiceTier);
		string ledgerDigest = BuildFamilyLedgerDigest(ledger);
		if (!string.IsNullOrWhiteSpace(ledgerDigest)) facts.Add(ledgerDigest);
		facts.Add("卷末影响为" + score.ToString(CultureInfo.InvariantCulture));
		return opening + "；" + string.Join("，", facts) + "。";
	}

	private static string BuildFamilyLedgerDigest(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (ledger == null || ledger.Count == 0)
		{
			return string.Empty;
		}

		int inheritance = 0;
		int highRealm = 0;
		int sectChange = 0;
		int deaths = 0;
		int vendetta = 0;
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord record = ledger[i];
			if (record == null) continue;
			string eventType = record.EventType ?? string.Empty;
			if (IsArtifactOrInheritanceRecord(record)) inheritance++;
			if (string.Equals(eventType, XjFamilyDomainEvent.TypeJinDanSucceeded, StringComparison.Ordinal)
				|| string.Equals(eventType, XjFamilyDomainEvent.TypeShenDanSucceeded, StringComparison.Ordinal)
				|| string.Equals(eventType, XjFamilyDomainEvent.TypeJinDanGift, StringComparison.Ordinal)
				|| string.Equals(eventType, "JieLinDeath", StringComparison.Ordinal)
				|| string.Equals(eventType, "ActorDeath", StringComparison.Ordinal) && record.Importance >= 4)
			{
				highRealm++;
			}
			if (string.Equals(eventType, "ActorDeath", StringComparison.Ordinal)
				|| string.Equals(eventType, "JieLinDeath", StringComparison.Ordinal))
			{
				deaths++;
			}
			if (record.SectId > 0L || eventType.StartsWith("Sect", StringComparison.Ordinal))
			{
				sectChange++;
			}
			if (eventType.StartsWith("FamilyVendetta", StringComparison.Ordinal))
			{
				vendetta++;
			}
		}

		List<string> parts = new List<string>();
		if (inheritance > 0) parts.Add("传承入谱：" + inheritance.ToString(CultureInfo.InvariantCulture));
		if (highRealm > 0) parts.Add("高境变动：" + highRealm.ToString(CultureInfo.InvariantCulture));
		if (sectChange > 0) parts.Add("宗门牵连：" + sectChange.ToString(CultureInfo.InvariantCulture));
		if (deaths > 0) parts.Add("重要陨落：" + deaths.ToString(CultureInfo.InvariantCulture));
		if (vendetta > 0) parts.Add("血债报复：" + vendetta.ToString(CultureInfo.InvariantCulture));
		return parts.Count == 0 ? string.Empty : string.Join("，", parts);
	}

	private static string ResolveFamilyNameForAnnals(long familyId, IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		string current = XjFamilyDisplayNameResolver.Resolve(familyId);
		if (XjHistoryNamePolicy.IsChineseFamilyName(current)) return current.Trim();
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				string snapshot = ledger[i]?.FamilyName;
				if (XjHistoryNamePolicy.IsChineseFamilyName(snapshot)) return snapshot.Trim();
			}
		}
		return string.Empty;
	}

	private static List<XjCenturySummaryItemRecord> BuildSectSummaries(
		IReadOnlyList<XjSectArchiveRecord> sects,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyDictionary<long, List<XjCenturyAnnalsLedgerRecord>> ledgerBySect)
	{
		Dictionary<long, int> familyCountBySect = new Dictionary<long, int>();
		Dictionary<long, float> voiceBySect = new Dictionary<long, float>();
		if (seats != null)
		{
			for (int i = 0; i < seats.Count; i++)
			{
				XjSectFamilySeatArchiveRecord seat = seats[i];
				if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L) continue;
				AddCount(familyCountBySect, seat.SectId, 1);
				voiceBySect.TryGetValue(seat.SectId, out float voice);
				voiceBySect[seat.SectId] = voice + Math.Max(0f, seat.VoiceScore);
			}
		}
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>();
		HashSet<long> seenSectIds = new HashSet<long>();
		if (sects != null)
		{
			for (int i = 0; i < sects.Count; i++)
			{
				XjSectArchiveRecord sect = sects[i];
				if (sect == null || sect.SectId <= 0L) continue;
				seenSectIds.Add(sect.SectId);
				familyCountBySect.TryGetValue(sect.SectId, out int familyCount);
				voiceBySect.TryGetValue(sect.SectId, out float voice);
				ledgerBySect.TryGetValue(sect.SectId, out List<XjCenturyAnnalsLedgerRecord> sectLedger);
				int cityCount = sect.CityIds?.Count ?? 0;
				int peakCount = sect.Peaks?.Count ?? 0;
				int ledgerScore = 0;
				if (sectLedger != null)
				{
					for (int j = 0; j < sectLedger.Count; j++) ledgerScore += Math.Max(0, sectLedger[j]?.Importance ?? 0) * 6;
				}
				int score = Math.Max(0, (int)Math.Round(cityCount * 30f + familyCount * 18f + peakCount * 10f + voice * 0.25f + ledgerScore));
				result.Add(new XjCenturySummaryItemRecord
				{
					Key = sect.SectId.ToString(CultureInfo.InvariantCulture),
					Name = sectNames.TryGetValue(sect.SectId, out string name) ? name : sect.Name ?? string.Empty,
					Score = score,
					Trend = ResolveSectTrend(sect, sectLedger),
					Summary = BuildSectSummaryText(cityCount, familyCount, peakCount, sectLedger)
				});
			}
		}
		if (ledgerBySect != null)
		{
			foreach (KeyValuePair<long, List<XjCenturyAnnalsLedgerRecord>> pair in ledgerBySect)
			{
				if (pair.Key <= 0L || seenSectIds.Contains(pair.Key)) continue;
				string name = ResolveSectNameFromLedger(pair.Key, pair.Value);
				result.Add(new XjCenturySummaryItemRecord
				{
					Key = pair.Key.ToString(CultureInfo.InvariantCulture),
					Name = string.IsNullOrWhiteSpace(name) ? "旧宗门" : name,
					Score = CalculateLedgerScore(pair.Value),
					Trend = ResolveSectTrend(null, pair.Value),
					Summary = BuildSectSummaryText(0, 0, 0, pair.Value)
				});
			}
		}
		result.Sort((left, right) =>
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
		if (result.Count > XjCenturyAnnalsSchema.MaxSectSummariesPerCentury)
		{
			result.RemoveRange(XjCenturyAnnalsSchema.MaxSectSummariesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxSectSummariesPerCentury);
		}
		return result;
	}

	private static string ResolveSectNameFromLedger(long sectId, IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				string name = NormalizeArchivedSectName(ledger[i]?.SectName);
				if (!string.IsNullOrWhiteSpace(name)) return name;
			}
		}
		return sectId > 0L ? "旧宗门" : string.Empty;
	}

	private static string NormalizeArchivedSectName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0 || text.StartsWith("宗门#", StringComparison.Ordinal)
			|| text.StartsWith("Sect#", StringComparison.OrdinalIgnoreCase)
			|| text.StartsWith("sect_", StringComparison.OrdinalIgnoreCase)) return string.Empty;
		return XjDisplayNameSanitizer.Clean(text, string.Empty);
	}

	private static int CalculateLedgerScore(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		int score = 0;
		if (ledger == null) return score;
		for (int i = 0; i < ledger.Count; i++) score += Math.Max(0, ledger[i]?.Importance ?? 0) * 8;
		return score;
	}

	private static string ResolveSectTrend(XjSectArchiveRecord sect, IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (sect != null && string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) return "衰亡";
		bool created = false;
		bool changed = false;
		bool conflict = false;
		bool warDanger = false;
		bool warEnded = false;
		bool mutualDestruction = false;
		bool victory = false;
		if (ledger != null)
		{
			for (int i = 0; i < ledger.Count; i++)
			{
				string eventType = ledger[i]?.EventType ?? string.Empty;
				if (string.Equals(eventType, "SectWarVictory", StringComparison.Ordinal)) victory = true;
				else if (string.Equals(eventType, "SectWarMutualDestruction", StringComparison.Ordinal)) mutualDestruction = true;
				else if (string.Equals(eventType, "SectWarEnded", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectWarCancelled", StringComparison.Ordinal)) warEnded = true;
				else if (string.Equals(eventType, "SectWarDefeatTransferPending", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectWarFormationHalf", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectWarFormationQuarter", StringComparison.Ordinal)) warDanger = true;
				else if (string.Equals(eventType, "SectWarStarted", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectWarProgress", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectHostilityConflict", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectMandateVendetta", StringComparison.Ordinal)) conflict = true;
				else if (string.Equals(eventType, "SectCreated", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectSoftSplitCreated", StringComparison.Ordinal)) created = true;
				else if (string.Equals(eventType, "SectExtinct", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectFormationBroken", StringComparison.Ordinal)) warDanger = true;
				else if (string.Equals(eventType, "SectSovereignChanged", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectSovereignContested", StringComparison.Ordinal)
					|| eventType.StartsWith("SectMandate", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectDominantFamilyChanged", StringComparison.Ordinal)
					|| string.Equals(eventType, "SectPeakFounded", StringComparison.Ordinal)) changed = true;
			}
		}
		if (victory) return "灭宗";
		if (mutualDestruction) return "同毁";
		if (warDanger) return "战危";
		if (warEnded) return "罢战";
		if (conflict) return "冲突";
		if (created) return "新立";
		if (changed) return "变动";
		return "存续";
	}

	private static string BuildSectSummaryText(int cityCount, int familyCount, int peakCount, IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		string text = "辖城" + cityCount.ToString(CultureInfo.InvariantCulture)
			+ "，家族席位" + familyCount.ToString(CultureInfo.InvariantCulture)
			+ "，峰脉" + peakCount.ToString(CultureInfo.InvariantCulture);
		if (ledger == null || ledger.Count == 0) return text;
		string digest = BuildSectLedgerDigest(ledger);
		string causalLine = BuildSectCausalLine(ledger);
		if (!string.IsNullOrWhiteSpace(digest)) text += "；本卷" + digest;
		if (!string.IsNullOrWhiteSpace(causalLine)) text += "；因果线：" + causalLine;
		return text;
	}

	private static string BuildSectCausalLine(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (ledger == null || ledger.Count == 0) return string.Empty;
		List<XjCenturyAnnalsLedgerRecord> events = new List<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord record = ledger[i];
			if (record == null || string.IsNullOrWhiteSpace(record.EventType) || string.IsNullOrWhiteSpace(record.Summary)) continue;
			string eventType = record.EventType;
			if (eventType.StartsWith("SectWar", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectHostilityConflict", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectFormationBroken", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectExtinct", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectSovereignContested", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectMandateVendetta", StringComparison.Ordinal))
			{
				events.Add(record);
			}
		}
		if (events.Count == 0) return string.Empty;
		events.Sort((left, right) =>
		{
			int year = left.Year.CompareTo(right.Year);
			if (year != 0) return year;
			int importance = right.Importance.CompareTo(left.Importance);
			if (importance != 0) return importance;
			int sect = left.SectId.CompareTo(right.SectId);
			if (sect != 0) return sect;
			int type = string.Compare(left.EventType, right.EventType, StringComparison.Ordinal);
			if (type != 0) return type;
			int actor = left.ActorId.CompareTo(right.ActorId);
			return actor != 0 ? actor : string.Compare(left.Summary, right.Summary, StringComparison.Ordinal);
		});
		List<string> parts = new List<string>(Math.Min(4, events.Count));
		for (int i = 0; i < events.Count && parts.Count < 4; i++)
		{
			string compact = CompactSectCausalSummary(events[i]);
			if (compact.Length == 0 || parts.Contains(compact)) continue;
			parts.Add(compact);
		}
		if (parts.Count == 0) return string.Empty;
		if (parts.Count == 1) return "因" + parts[0];
		if (parts.Count == 2) return "因" + parts[0] + "，遂至" + parts[1];
		return "因" + parts[0] + "，继而" + string.Join("，再至", parts.GetRange(1, parts.Count - 2)) + "，最终" + parts[parts.Count - 1];
	}

	private static string CompactSectCausalSummary(XjCenturyAnnalsLedgerRecord record)
	{
		if (record == null) return string.Empty;
		string summary = (record.Summary ?? string.Empty).Trim().TrimEnd('。', '；', ';');
		if (summary.Length > 48) summary = summary.Substring(0, 48) + "…";
		return record.Year > 0 ? XjChronology.FormatYear(record.Year) + summary : summary;
	}

	private static string BuildSectLedgerDigest(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		if (ledger == null || ledger.Count == 0)
		{
			return string.Empty;
		}

		int created = 0;
		int extinct = 0;
		int leadership = 0;
		int mandates = 0;
		int peaks = 0;
		int formation = 0;
		int conflicts = 0;
		int wars = 0;
		int victories = 0;
		int mutualDestruction = 0;
		int ceasefires = 0;
		int other = 0;
		for (int i = 0; i < ledger.Count; i++)
		{
			string eventType = ledger[i]?.EventType ?? string.Empty;
			if (string.Equals(eventType, "SectCreated", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectSoftSplitCreated", StringComparison.Ordinal)) created++;
			else if (string.Equals(eventType, "SectExtinct", StringComparison.Ordinal)) extinct++;
			else if (string.Equals(eventType, "SectSovereignChanged", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectSovereignContested", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectDominantFamilyChanged", StringComparison.Ordinal)) leadership++;
			else if (eventType.StartsWith("SectMandate", StringComparison.Ordinal)) mandates++;
			else if (string.Equals(eventType, "SectPeakFounded", StringComparison.Ordinal)) peaks++;
			else if (string.Equals(eventType, "SectFormationBuilt", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectFormationDamaged", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectFormationBroken", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectWarFormationHalf", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectWarFormationQuarter", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectWarDefeatTransferPending", StringComparison.Ordinal)) formation++;
			else if (string.Equals(eventType, "SectHostilityConflict", StringComparison.Ordinal)) conflicts++;
			else if (string.Equals(eventType, "SectWarStarted", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectWarProgress", StringComparison.Ordinal)) wars++;
			else if (string.Equals(eventType, "SectWarVictory", StringComparison.Ordinal)) victories++;
			else if (string.Equals(eventType, "SectWarMutualDestruction", StringComparison.Ordinal)) mutualDestruction++;
			else if (string.Equals(eventType, "SectWarEnded", StringComparison.Ordinal)
				|| string.Equals(eventType, "SectWarCancelled", StringComparison.Ordinal)) ceasefires++;
			else other++;
		}

		List<string> parts = new List<string>();
		if (created > 0) parts.Add("新立" + created.ToString(CultureInfo.InvariantCulture));
		if (extinct > 0) parts.Add("衰亡" + extinct.ToString(CultureInfo.InvariantCulture));
		if (leadership > 0) parts.Add("主脉更替" + leadership.ToString(CultureInfo.InvariantCulture));
		if (mandates > 0) parts.Add("宗主法旨" + mandates.ToString(CultureInfo.InvariantCulture));
		if (peaks > 0) parts.Add("峰脉增设" + peaks.ToString(CultureInfo.InvariantCulture));
		if (formation > 0) parts.Add("大阵变动" + formation.ToString(CultureInfo.InvariantCulture));
		if (conflicts > 0) parts.Add("宗门冲突" + conflicts.ToString(CultureInfo.InvariantCulture));
		if (wars > 0) parts.Add("宗门大战" + wars.ToString(CultureInfo.InvariantCulture));
		if (victories > 0) parts.Add("破阵灭宗" + victories.ToString(CultureInfo.InvariantCulture));
		if (mutualDestruction > 0) parts.Add("两阵同毁" + mutualDestruction.ToString(CultureInfo.InvariantCulture));
		if (ceasefires > 0) parts.Add("久攻罢战" + ceasefires.ToString(CultureInfo.InvariantCulture));
		if (other > 0) parts.Add("要务" + other.ToString(CultureInfo.InvariantCulture));
		return string.Join("，", parts);
	}

	private static List<XjCenturySummaryItemRecord> BuildArtifactSummaries(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger, int centuryId)
	{
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>();
		if (ledger == null) return result;
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord record = ledger[i];
			if (record == null || record.CenturyId != centuryId || record.Importance < 3 || string.IsNullOrWhiteSpace(record.Summary)) continue;
			if (!IsArtifactOrInheritanceRecord(record)) continue;
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = record.Year.ToString(CultureInfo.InvariantCulture) + ":" + record.ActorId.ToString(CultureInfo.InvariantCulture),
				Name = BuildArtifactAnnalsName(record),
				Score = record.Importance,
				Trend = ResolveArtifactTrend(record),
				Summary = BuildArtifactAnnalsSummary(record)
			});
		}
		result.Sort((left, right) =>
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
		if (result.Count > XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury)
		{
			result.RemoveRange(XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury, result.Count - XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury);
		}
		return result;
	}

	private static string BuildArtifactAnnalsName(XjCenturyAnnalsLedgerRecord record)
	{
		string kind = ResolveArtifactKind(record);
		string actor = string.IsNullOrWhiteSpace(record?.ActorName) ? string.Empty : record.ActorName.Trim();
		if (!string.IsNullOrWhiteSpace(actor))
		{
			return actor + " · " + kind;
		}
		return kind;
	}

	private static string ResolveArtifactTrend(XjCenturyAnnalsLedgerRecord record)
	{
		string eventType = record?.EventType ?? string.Empty;
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal)) return "晋品";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)) return "求金";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoUpgraded, StringComparison.Ordinal)) return "升炼";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianSurvived, StringComparison.Ordinal)) return "洞天";
		return "入谱";
	}

	private static string BuildArtifactAnnalsSummary(XjCenturyAnnalsLedgerRecord record)
	{
		if (record == null) return string.Empty;
		List<string> parts = new List<string>();
		if (record.Year > 0) parts.Add(XjChronology.FormatYear(record.Year));
		if (!string.IsNullOrWhiteSpace(record.DaoTu)) parts.Add("道途：" + record.DaoTu.Trim());
		if (record.Grade >= 6) parts.Add("品阶：" + FormatGongFaGrade(record.Grade));
		string reason = ResolveArtifactReason(record);
		if (!string.IsNullOrWhiteSpace(reason)) parts.Add(reason);
		return string.Join("，", parts);
	}

	private static string ResolveArtifactKind(XjCenturyAnnalsLedgerRecord record)
	{
		string eventType = record?.EventType ?? string.Empty;
		string itemClass = record?.ItemClass ?? string.Empty;
		string summary = record?.Summary ?? string.Empty;
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)) return "求金法";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeJinXingObtained, StringComparison.Ordinal) || summary.Contains("金性")) return "金丹金性";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal))
		{
			return "六品功法";
		}
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoUpgraded, StringComparison.Ordinal))
		{
			if (itemClass.Contains("灵宝") || summary.Contains("灵宝")) return "紫府灵宝";
			if (itemClass.Contains("金丹") || summary.Contains("金丹法宝")) return "金丹法宝";
			return "高阶法宝";
		}
		if (summary.Contains("丹方")) return "丹方传承";
		if (summary.Contains("紫府灵物")) return "紫府灵物";
		if (summary.Contains("洞天")) return "洞天收获";
		return "传承器物";
	}

	private static string ResolveArtifactReason(XjCenturyAnnalsLedgerRecord record)
	{
		string eventType = record?.EventType ?? string.Empty;
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianSurvived, StringComparison.Ordinal)) return "洞天有实获";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)) return "可承接金丹路";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal)) return "主功法晋升";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaObtained, StringComparison.Ordinal)) return "高阶功法入族";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoUpgraded, StringComparison.Ordinal)) return "器物升阶";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoObtained, StringComparison.Ordinal)) return "高阶器物入族";
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeJinXingObtained, StringComparison.Ordinal)) return "金丹凭依";
		return "影响传承格局";
	}

	private static string FormatGongFaGrade(int grade)
	{
		return grade switch
		{
			6 => "六品",
			5 => "五品",
			4 => "四品",
			3 => "三品",
			2 => "二品",
			1 => "一品",
			_ => grade.ToString(CultureInfo.InvariantCulture) + "品"
		};
	}

	private static bool IsArtifactOrInheritanceRecord(XjCenturyAnnalsLedgerRecord record)
	{
		string eventType = record.EventType ?? string.Empty;
		string summary = record.Summary ?? string.Empty;
		if (IsFiveGradeTechnique(record)) return false;
		return string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoObtained, StringComparison.Ordinal)
				&& IsRareFaBao(record)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeFaBaoUpgraded, StringComparison.Ordinal)
				&& IsRareFaBao(record)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaObtained, StringComparison.Ordinal)
				&& record.Grade >= 6
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal)
				&& record.Grade >= 6
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)
				&& record.Grade >= 5
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeJinXingObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianSurvived, StringComparison.Ordinal)
				&& IsRareDongTianReward(record)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianOpened, StringComparison.Ordinal)
				&& IsMeaningfulDongTianSummary(summary)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeDongTianClosed, StringComparison.Ordinal)
				&& IsMeaningfulDongTianSummary(summary)
			|| summary.Contains("金性")
			|| summary.Contains("灵宝")
			|| summary.Contains("金丹法宝")
			|| summary.Contains("紫府灵物")
			|| summary.Contains("洞天")
				&& IsMeaningfulDongTianSummary(summary)
				&& IsRareDongTianReward(record)
				&& !summary.Contains("身故")
				&& !summary.Contains("陨落");
	}

	private static bool IsFiveGradeTechnique(XjCenturyAnnalsLedgerRecord record)
	{
		if (record == null) return false;
		string eventType = record.EventType ?? string.Empty;
		if (string.Equals(eventType, XjFamilyDomainEvent.TypeQiuJinFaComprehended, StringComparison.Ordinal)) return false;
		string itemClass = record.ItemClass ?? string.Empty;
		string summary = record.Summary ?? string.Empty;
		bool isTechnique = string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaObtained, StringComparison.Ordinal)
			|| string.Equals(eventType, XjFamilyDomainEvent.TypeGongFaPromoted, StringComparison.Ordinal)
			|| itemClass.IndexOf("GongFa", StringComparison.OrdinalIgnoreCase) >= 0
			|| summary.Contains("功法");
		return isTechnique && (record.Grade == 5 || summary.Contains("五品功法") || summary.Contains("品阶：五品") || summary.Contains("品阶:五品"));
	}

	private static bool IsRareDongTianReward(XjCenturyAnnalsLedgerRecord record)
	{
		if (IsFiveGradeTechnique(record)) return false;
		string itemClass = record?.ItemClass ?? string.Empty;
		string summary = record?.Summary ?? string.Empty;
		return itemClass.Contains("GongFa")
			|| itemClass.Contains("FaBao")
			|| itemClass.Contains("JinXing")
			|| itemClass.Contains("AlchemyRecipe")
			|| itemClass.Contains("SecretRealm")
			|| summary.Contains("功法")
			|| summary.Contains("丹方")
			|| summary.Contains("法宝")
			|| summary.Contains("金性")
			|| summary.Contains("洞天法");
	}

	private static bool IsMeaningfulDongTianSummary(string summary)
	{
		string text = (summary ?? string.Empty).Trim();
		return text.Length > 0
			&& text.Contains("洞天")
			&& !text.Contains("洞天：无")
			&& !text.Contains("洞天:无")
			&& !text.Contains("洞天无")
			&& !text.EndsWith("：无", StringComparison.Ordinal)
			&& !text.EndsWith(":无", StringComparison.Ordinal);
	}

	private static bool IsRareFaBao(XjCenturyAnnalsLedgerRecord record)
	{
		string itemClass = record?.ItemClass ?? string.Empty;
		string summary = record?.Summary ?? string.Empty;
		return itemClass.Contains("灵宝")
			|| itemClass.Contains("金丹")
			|| itemClass.Contains("紫府")
			|| summary.Contains("灵宝")
			|| summary.Contains("金丹法宝")
			|| summary.Contains("紫府灵物");
	}

	private static string BuildWorldSummary(
		int centuryId,
		IReadOnlyList<XjCenturyFamilyStateRecord> families,
		IReadOnlyList<XjCenturySummaryItemRecord> sects,
		IReadOnlyList<XjCenturyActorHighlightRecord> actors,
		IReadOnlyList<XjCenturySummaryItemRecord> realmStatistics,
		IReadOnlyList<XjCenturySummaryItemRecord> daoSummaries,
		IReadOnlyList<XjCenturySummaryItemRecord> artifactSummaries)
	{
		int rising = 0;
		int recovering = 0;
		int peak = 0;
		int declining = 0;
		int extinct = 0;
		for (int i = 0; i < families.Count; i++)
		{
			string stage = families[i].CurrentStage;
			if (stage == XjCenturyFamilyStage.XingSheng) rising++;
			if (stage == XjCenturyFamilyStage.FuZhen) recovering++;
			if (stage == XjCenturyFamilyStage.DingSheng) peak++;
			if (stage == XjCenturyFamilyStage.ZhongShuai) declining++;
			if (stage == XjCenturyFamilyStage.FuMie) extinct++;
		}
		int aspirationCount = CountFamilyAspirations(families);
		int daoTransitionCount = CountDaoTransitions(daoSummaries);
		return "第" + centuryId.ToString(CultureInfo.InvariantCulture) + "百年，入谱家族"
			+ families.Count.ToString(CultureInfo.InvariantCulture)
			+ "支；鼎盛" + peak.ToString(CultureInfo.InvariantCulture)
			+ "，兴盛" + rising.ToString(CultureInfo.InvariantCulture)
			+ "，复振" + recovering.ToString(CultureInfo.InvariantCulture)
			+ "，中衰" + declining.ToString(CultureInfo.InvariantCulture)
			+ "，覆灭" + extinct.ToString(CultureInfo.InvariantCulture)
			+ "；宗门摘要" + sects.Count.ToString(CultureInfo.InvariantCulture)
			+ "条，代表人物" + actors.Count.ToString(CultureInfo.InvariantCulture)
			+ "人；境界统计" + (realmStatistics?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "项，道途纪要" + (daoSummaries?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "项，世家志业" + aspirationCount.ToString(CultureInfo.InvariantCulture)
			+ "项，道统转折" + daoTransitionCount.ToString(CultureInfo.InvariantCulture)
			+ "项，传承器物" + (artifactSummaries?.Count ?? 0).ToString(CultureInfo.InvariantCulture) + "项。";
	}

	private static XjFamilyInheritanceStatus ReadFamilyInheritanceStatus(long familyStableId)
	{
		bool hasFifthGradeGongFa = false;
		bool hasQiuJinFa = false;
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyGongFaWarehouse.ReadFamilyEntries(familyStableId);
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry entry = entries[i];
			if (!entry.Found) continue;
			if (string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, StringComparison.Ordinal))
			{
				hasQiuJinFa = true;
			}
			else if (string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeGongFa, StringComparison.Ordinal) && entry.Grade >= 5)
			{
				hasFifthGradeGongFa = true;
			}
			if (hasFifthGradeGongFa && hasQiuJinFa) break;
		}
		return new XjFamilyInheritanceStatus(hasFifthGradeGongFa, hasQiuJinFa);
	}

	private static XjFamilyAspirationResolution ResolveFamilyAspiration(
		in XjFamilyLedgerAggregate aggregate,
		XjCenturyFamilyStageStateRecord previous,
		string stage,
		in XjFamilyInheritanceStatus inheritance,
		int startYear,
		int endYear)
	{
		string active = previous?.ActiveAspiration?.Trim() ?? string.Empty;
		int sinceYear = previous?.AspirationSinceYear > 0 ? previous.AspirationSinceYear : Math.Max(1, startYear);
		bool extinct = string.Equals(stage, XjCenturyFamilyStage.FuMie, StringComparison.Ordinal);
		if (extinct)
		{
			return active.Length == 0
				? XjFamilyAspirationResolution.Empty
				: new XjFamilyAspirationResolution(active, "中断", sinceYear, "家族覆灭，前代志业随传承中断。", string.Empty, 0);
		}

		if (active.Length > 0)
		{
			if (IsAspirationCompleted(active, aggregate, stage, inheritance))
			{
				string next = SelectAspiration(aggregate, stage, inheritance);
				return new XjFamilyAspirationResolution(
					active,
					"已成",
					sinceYear,
					BuildAspirationCompletionSummary(active, inheritance),
					next,
					next.Length > 0 ? endYear : 0);
			}
			return new XjFamilyAspirationResolution(
				active,
				"未竟",
				sinceYear,
				BuildAspirationPendingSummary(active),
				active,
				sinceYear);
		}

		string selected = SelectAspiration(aggregate, stage, inheritance);
		if (selected.Length == 0) return XjFamilyAspirationResolution.Empty;
		return new XjFamilyAspirationResolution(
			selected,
			"新立",
			endYear,
			BuildAspirationStartSummary(selected),
			selected,
			endYear);
	}

	private static string SelectAspiration(in XjFamilyLedgerAggregate aggregate, string stage, in XjFamilyInheritanceStatus inheritance)
	{
		if (string.Equals(stage, XjCenturyFamilyStage.ZhongShuai, StringComparison.Ordinal))
		{
			return XjCenturyFamilyAspiration.FuZhen;
		}
		if (aggregate.ZiFuCount <= 0
			&& aggregate.JinDanCount <= 0
			&& aggregate.CultivatorCount >= 3
			&& aggregate.HighestRealmOrder >= XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZhuJi))
		{
			return XjCenturyFamilyAspiration.YuFu;
		}
		if (aggregate.ZiFuCount > 0 && aggregate.JinDanCount <= 0)
		{
			// 紫府家族先补足高阶传承，已有五品功法或求金法后才正式立求金志业。
			return inheritance.HasHighGradeInheritance
				? XjCenturyFamilyAspiration.QiuJin
				: XjCenturyFamilyAspiration.QiuFa;
		}
		if (aggregate.CultivatorCount >= 2 && !inheritance.HasHighGradeInheritance)
		{
			return XjCenturyFamilyAspiration.QiuFa;
		}
		return string.Empty;
	}

	private static bool IsAspirationCompleted(
		string aspiration,
		in XjFamilyLedgerAggregate aggregate,
		string stage,
		in XjFamilyInheritanceStatus inheritance)
	{
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal))
		{
			return inheritance.HasHighGradeInheritance;
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal))
		{
			return aggregate.ZiFuCount > 0 || aggregate.JinDanCount > 0;
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal))
		{
			return aggregate.JinDanCount > 0;
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal))
		{
			return string.Equals(stage, XjCenturyFamilyStage.FuZhen, StringComparison.Ordinal);
		}
		return false;
	}

	private static string BuildAspirationStartSummary(string aspiration)
	{
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal)) return "族中传承不足，立志求取五品功法或求金法。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal)) return "真人之前的传承已成，立志培养家族第一位真人；紫府与服气养性两路皆可承志。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal)) return "族中已有真人支柱，立志数代经营，求取第一位真君；金丹与真君羽士皆可承志。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal)) return "家势中衰，后人立志重振门楣。";
		return string.Empty;
	}

	private static string BuildAspirationPendingSummary(string aspiration)
	{
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal)) return "仍未获得五品功法或求金法。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal)) return "族中仍无真人，志业由紫府金丹或服气养性后辈续承。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal)) return "族中仍无真君，登真志业由后辈续承。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal)) return "家势尚未由中衰转为复振。";
		return string.Empty;
	}

	private static string BuildAspirationCompletionSummary(string aspiration, in XjFamilyInheritanceStatus inheritance)
	{
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuFa, StringComparison.Ordinal))
		{
			return inheritance.HasQiuJinFa ? "族中已得求金法，求法志业告成。" : "族中已得五品功法，求法志业告成。";
		}
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.YuFu, StringComparison.Ordinal)) return "族中诞生真人，育真志业告成。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.QiuJin, StringComparison.Ordinal)) return "族中诞生真君，登真志业告成。";
		if (string.Equals(aspiration, XjCenturyFamilyAspiration.FuZhen, StringComparison.Ordinal)) return "家势重振，前代复振志业告成。";
		return string.Empty;
	}

	internal static string ResolveSectRelation(XjSectFamilySeatArchiveRecord seat, int startYear)
	{
		if (seat == null) return string.Empty;
		bool recentIncident = seat.LastPrivilegeIncidentYear >= Math.Max(1, startYear);
		if (seat.PrivilegeHeat >= 80f && seat.PrivilegeIncidentCount > 0) return "强族凌弱";
		if (seat.SupplyDebt >= 80f || recentIncident) return "宗门生隙";
		if (seat.SupplyDebt >= 60f) return "恩债渐重";
		if (seat.SupplyDebt >= 20f) return "尚有供奉";
		if (seat.VoiceScore >= 80f || !string.IsNullOrWhiteSpace(seat.VoiceTier) && !string.Equals(seat.VoiceTier, XjSectVoiceTier.Ordinary, StringComparison.Ordinal))
		{
			return "宗门倚重";
		}
		return "门中平稳";
	}

	private static void ApplyDaoTransitions(
		IReadOnlyList<XjCenturySummaryItemRecord> current,
		IReadOnlyList<XjCenturySummaryItemRecord> previous)
	{
		if (current == null || previous == null || previous.Count == 0) return;
		Dictionary<string, XjCenturySummaryItemRecord> previousByKey = new Dictionary<string, XjCenturySummaryItemRecord>(StringComparer.Ordinal);
		for (int i = 0; i < previous.Count; i++)
		{
			XjCenturySummaryItemRecord item = previous[i];
			if (item == null) continue;
			string key = string.IsNullOrWhiteSpace(item.Key) ? item.Name?.Trim() ?? string.Empty : item.Key.Trim();
			if (key.Length > 0) previousByKey[key] = item;
		}

		for (int i = 0; i < current.Count; i++)
		{
			XjCenturySummaryItemRecord item = current[i];
			if (item == null) continue;
			string key = string.IsNullOrWhiteSpace(item.Key) ? item.Name?.Trim() ?? string.Empty : item.Key.Trim();
			if (key.Length == 0 || !previousByKey.TryGetValue(key, out XjCenturySummaryItemRecord old)) continue;
			string oldState = string.IsNullOrWhiteSpace(old.State) ? old.Trend ?? string.Empty : old.State;
			string currentState = string.IsNullOrWhiteSpace(item.State) ? item.Trend ?? string.Empty : item.State;
			item.State = currentState;
			string transition = string.Empty;
			if (string.Equals(oldState, "断传", StringComparison.Ordinal) && !string.Equals(currentState, "断传", StringComparison.Ordinal))
			{
				transition = "复传";
			}
			else if (!string.Equals(oldState, "断传", StringComparison.Ordinal) && string.Equals(currentState, "断传", StringComparison.Ordinal))
			{
				transition = "断传";
			}
			else if (DaoStateRank(oldState) >= 5 && DaoStateRank(currentState) < DaoStateRank(oldState))
			{
				transition = "衰退";
			}
			else if (DaoStateRank(oldState) <= 4 && DaoStateRank(currentState) >= 5)
			{
				transition = "大兴";
			}
			if (transition.Length == 0) continue;
			item.Trend = transition;
			string prefix = "由" + oldState + "转为" + currentState;
			item.Summary = string.IsNullOrWhiteSpace(item.Summary) ? prefix : prefix + "；" + item.Summary;
		}
	}

	private static int DaoStateRank(string state)
	{
		if (string.Equals(state, "鼎盛", StringComparison.Ordinal)) return 6;
		if (string.Equals(state, "兴盛", StringComparison.Ordinal)) return 5;
		if (string.Equals(state, "复苏", StringComparison.Ordinal)) return 4;
		if (string.Equals(state, "潜隐", StringComparison.Ordinal)) return 2;
		if (string.Equals(state, "断传", StringComparison.Ordinal)) return 1;
		return 0;
	}

	private static string ResolveActorName(long actorId)
	{
		if (actorId <= 0L) return string.Empty;
		if (XjScheduler.ResolveActor(actorId, out Actor actor) && actor?.data != null)
		{
			string liveName = actor.getName();
			if (!string.IsNullOrWhiteSpace(liveName)) return liveName.Trim();
		}
		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found)
		{
			return entry.Name?.Trim() ?? string.Empty;
		}
		return string.Empty;
	}

	private static int CountFamilyAspirations(IReadOnlyList<XjCenturyFamilyStateRecord> families)
	{
		int count = 0;
		if (families == null) return count;
		for (int i = 0; i < families.Count; i++)
		{
			if (!string.IsNullOrWhiteSpace(families[i]?.Aspiration)) count++;
		}
		return count;
	}

	private static int CountDaoTransitions(IReadOnlyList<XjCenturySummaryItemRecord> daoSummaries)
	{
		int count = 0;
		if (daoSummaries == null) return count;
		for (int i = 0; i < daoSummaries.Count; i++)
		{
			string trend = daoSummaries[i]?.Trend ?? string.Empty;
			if (string.Equals(trend, "大兴", StringComparison.Ordinal)
				|| string.Equals(trend, "衰退", StringComparison.Ordinal)
				|| string.Equals(trend, "断传", StringComparison.Ordinal)
				|| string.Equals(trend, "复传", StringComparison.Ordinal))
			{
				count++;
			}
		}
		return count;
	}

	private readonly struct XjFamilyInheritanceStatus
	{
		internal readonly bool HasFifthGradeGongFa;
		internal readonly bool HasQiuJinFa;
		internal bool HasHighGradeInheritance => HasFifthGradeGongFa || HasQiuJinFa;

		internal XjFamilyInheritanceStatus(bool hasFifthGradeGongFa, bool hasQiuJinFa)
		{
			HasFifthGradeGongFa = hasFifthGradeGongFa;
			HasQiuJinFa = hasQiuJinFa;
		}

		internal string BuildSummary()
		{
			if (HasFifthGradeGongFa && HasQiuJinFa) return "五品功法、求金法俱全";
			if (HasQiuJinFa) return "已有求金法";
			if (HasFifthGradeGongFa) return "已有五品功法";
			return "高阶传承未备";
		}
	}

	private readonly struct XjFamilyAspirationResolution
	{
		internal static readonly XjFamilyAspirationResolution Empty = new XjFamilyAspirationResolution(string.Empty, string.Empty, 0, string.Empty, string.Empty, 0);
		internal readonly string DisplayAspiration;
		internal readonly string Status;
		internal readonly int SinceYear;
		internal readonly string Summary;
		internal readonly string NextAspiration;
		internal readonly int NextSinceYear;

		internal XjFamilyAspirationResolution(
			string displayAspiration,
			string status,
			int sinceYear,
			string summary,
			string nextAspiration,
			int nextSinceYear)
		{
			DisplayAspiration = displayAspiration ?? string.Empty;
			Status = status ?? string.Empty;
			SinceYear = Math.Max(0, sinceYear);
			Summary = summary ?? string.Empty;
			NextAspiration = nextAspiration ?? string.Empty;
			NextSinceYear = Math.Max(0, nextSinceYear);
		}
	}

	private static List<long> ReadImportantWorldEventIds(
		IReadOnlyList<XjWorldHistoryArchiveRecord> history,
		int startYear,
		int endYear)
	{
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		List<long> result = new List<long>();
		for (int i = history.Count - 1; i >= 0 && result.Count < XjCenturyAnnalsSchema.MaxEventIdsPerRecord; i--)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item == null || item.EventId <= 0L || item.Year < startYear || item.Year > endYear || item.Importance < 3) continue;
			result.Add(item.EventId);
		}
		return result;
	}

	internal static List<XjCenturyActorHighlightRecord> BuildVolumeRepresentativeActors(
		int startYear,
		int endYear,
		IReadOnlyList<XjCenturyActorHighlightRecord> fallbackActors = null)
	{
		return BuildVolumeRepresentativeActors(
			startYear,
			endYear,
			fallbackActors,
			XjWorldHistoryStore.ReadSnapshotForYears(startYear, endYear, MaxHistoryEvidencePerCentury));
	}

	private static List<XjCenturyActorHighlightRecord> BuildVolumeRepresentativeActors(
		int startYear,
		int endYear,
		IReadOnlyList<XjCenturyActorHighlightRecord> fallbackActors,
		IReadOnlyList<XjWorldHistoryArchiveRecord> history)
	{
		List<XjWorldHistoryArchiveRecord> evidence = new List<XjWorldHistoryArchiveRecord>();
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		for (int i = 0; i < history.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item == null || item.Year < startYear || item.Year > endYear) continue;
			if (item.ActorId <= 0L && item.RelatedActorId <= 0L) continue;
			evidence.Add(item);
		}
		evidence.Sort((left, right) =>
		{
			int importance = right.Importance.CompareTo(left.Importance);
			if (importance != 0) return importance;
			int year = right.Year.CompareTo(left.Year);
			if (year != 0) return year;
			return right.EventId.CompareTo(left.EventId);
		});

		List<XjCenturyActorHighlightRecord> result = new List<XjCenturyActorHighlightRecord>();
		HashSet<long> seen = new HashSet<long>();
		for (int i = 0; i < evidence.Count && result.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury; i++)
		{
			XjWorldHistoryArchiveRecord item = evidence[i];
			AppendHistoryRepresentative(result, seen, item, related: false);
			if (result.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury)
			{
				AppendHistoryRepresentative(result, seen, item, related: true);
			}
		}

		if (fallbackActors != null)
		{
			for (int i = 0; i < fallbackActors.Count && result.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury; i++)
			{
				XjCenturyActorHighlightRecord actor = fallbackActors[i];
				if (actor == null || actor.ActorId <= 0L || !IsVolumeEventRepresentative(actor)
					|| !XjHistoryNamePolicy.IsChineseActorName(actor.ActorName) || !seen.Add(actor.ActorId)) continue;
				result.Add(actor);
			}
		}
		return result;
	}

	private static void AppendHistoryRepresentative(
		List<XjCenturyActorHighlightRecord> target,
		HashSet<long> seen,
		XjWorldHistoryArchiveRecord item,
		bool related)
	{
		if (item == null || !XjHistoryRetentionPolicy.ShouldKeepWorldRecord(item.Category, item.EventType, item.Title, item.Body)) return;
		long actorId = related ? item.RelatedActorId : item.ActorId;
		if (actorId <= 0L || !seen.Add(actorId)) return;
		string actorName = related ? item.RelatedActorName : item.ActorName;
		if (string.IsNullOrWhiteSpace(actorName)) actorName = ResolveActorName(actorId);
		if (!XjHistoryNamePolicy.IsChineseActorName(actorName)) return;
		long familyId = related ? item.RelatedFamilyId : item.FamilyId;
		string familyName = related ? item.RelatedFamilyNameSnapshot : item.FamilyNameSnapshot;
		long sectId = related ? item.RelatedSectId : item.SectId;
		string sectName = NormalizeArchivedSectName(related ? item.RelatedSectNameSnapshot : item.SectNameSnapshot);
		ResolveActorRealmForAnnals(actorId, string.Empty, 0, out string realm, out int realmOrder);
		target.Add(new XjCenturyActorHighlightRecord
		{
			ActorId = actorId,
			ActorName = actorName,
			FamilyStableId = Math.Max(0L, familyId),
			FamilyName = familyName ?? string.Empty,
			SectId = Math.Max(0L, sectId),
			SectName = sectName,
			Realm = realm,
			RealmOrder = realmOrder,
			Reason = BuildHistoryRepresentativeReason(item)
		});
	}

	private static string BuildHistoryRepresentativeReason(XjWorldHistoryArchiveRecord item)
	{
		string text = !string.IsNullOrWhiteSpace(item?.Title) ? item.Title.Trim() : item?.Body?.Trim() ?? string.Empty;
		if (text.Length == 0) return "本卷留下事迹";
		const int maxLength = 36;
		return text.Length <= maxLength ? text : text.Substring(0, maxLength).TrimEnd('，', '；', '。', ' ') + "…";
	}


	internal static bool TryRehydrateArchivedRecord(XjCenturyAnnalsArchiveRecord record)
	{
		if (record == null || record.CenturyId <= 0 || record.StartYear <= 0 || record.EndYear < record.StartYear)
		{
			return false;
		}

		IReadOnlyList<XjWorldHistoryArchiveRecord> history =
			XjWorldHistoryStore.ReadSnapshotForYears(record.StartYear, record.EndYear, MaxHistoryEvidencePerCentury);
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger =
			XjCenturyAnnalsStore.ReadLedgerSnapshotForCentury(record.CenturyId, MaxLedgerEvidencePerCentury);
		bool changed = false;

		if (record.ImportantEventIds == null || record.ImportantEventIds.Count == 0)
		{
			record.ImportantEventIds = ReadImportantWorldEventIds(history, record.StartYear, record.EndYear);
			changed |= record.ImportantEventIds.Count > 0;
		}
		if (record.NotableEvents == null || record.NotableEvents.Count == 0)
		{
			record.NotableEvents = BuildNotableEvents(history, ledger, record.StartYear, record.EndYear);
			changed |= record.NotableEvents.Count > 0;
		}

		if (!record.IsCompleteCycle)
		{
			if (record.FamilyStates == null || record.FamilyStates.Count == 0)
			{
				record.FamilyStates = BuildBackfillFamilyStates(history, ledger, record.StartYear, record.EndYear);
				changed |= record.FamilyStates.Count > 0;
			}
			if (record.SectSummaries == null || record.SectSummaries.Count == 0)
			{
				record.SectSummaries = BuildBackfillSectSummaries(history, ledger);
				changed |= record.SectSummaries.Count > 0;
			}
			if (record.RealmStatistics == null || record.RealmStatistics.Count == 0)
			{
				record.RealmStatistics = BuildBackfillRealmEvidence(history, ledger);
				changed |= record.RealmStatistics.Count > 0;
			}
			if (record.DaoSummaries == null || record.DaoSummaries.Count == 0)
			{
				record.DaoSummaries = BuildBackfillDaoEvidence(ledger);
				changed |= record.DaoSummaries.Count > 0;
			}
			if (record.ArtifactSummaries == null || record.ArtifactSummaries.Count == 0)
			{
				record.ArtifactSummaries = BuildArtifactSummaries(ledger, record.CenturyId);
				changed |= record.ArtifactSummaries.Count > 0;
			}
			if (record.RepresentativeActors == null || record.RepresentativeActors.Count == 0)
			{
				record.RepresentativeActors = BuildVolumeRepresentativeActors(
					record.StartYear, record.EndYear, null, history);
				changed |= record.RepresentativeActors.Count > 0;
			}

			string rebuiltSummary = BuildBackfillWorldSummary(
				record.CenturyId,
				record.NotableEvents ?? new List<XjCenturyEventRecord>(),
				record.FamilyStates ?? new List<XjCenturyFamilyStateRecord>(),
				record.SectSummaries ?? new List<XjCenturySummaryItemRecord>(),
				record.RepresentativeActors ?? new List<XjCenturyActorHighlightRecord>(),
				record.RealmStatistics ?? new List<XjCenturySummaryItemRecord>(),
				record.DaoSummaries ?? new List<XjCenturySummaryItemRecord>(),
				record.ArtifactSummaries ?? new List<XjCenturySummaryItemRecord>());
			if (!string.Equals(record.WorldSummary, rebuiltSummary, StringComparison.Ordinal))
			{
				record.WorldSummary = rebuiltSummary;
				changed = true;
			}
		}
		return changed;
	}

	private static List<XjCenturyEventRecord> BuildNotableEvents(
		IReadOnlyList<XjWorldHistoryArchiveRecord> history,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger,
		int startYear,
		int endYear)
	{
		List<XjCenturyEventRecord> candidates = new List<XjCenturyEventRecord>();
		HashSet<string> signatures = new HashSet<string>(StringComparer.Ordinal);
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		for (int i = 0; i < history.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item == null || item.Year < startYear || item.Year > endYear
				|| item.Importance < 2
				|| !XjHistoryRetentionPolicy.ShouldKeepWorldRecord(item.Category, item.EventType, item.Title, item.Body)) continue;
			string title = CompactAnnalsText(string.IsNullOrWhiteSpace(item.Title) ? item.Body : item.Title, 44);
			string summary = CompactAnnalsText(string.IsNullOrWhiteSpace(item.Body) ? item.Title : item.Body, 120);
			string signature = item.Year.ToString(CultureInfo.InvariantCulture) + "|" + title + "|" + summary;
			if (!signatures.Add(signature)) continue;
			candidates.Add(new XjCenturyEventRecord
			{
				EventId = Math.Max(0L, item.EventId),
				Year = item.Year,
				Category = item.Category ?? string.Empty,
				EventType = item.EventType ?? string.Empty,
				Title = title,
				Summary = summary,
				Importance = Math.Clamp(item.Importance, 1, 5),
				ActorId = Math.Max(0L, item.ActorId),
				ActorName = item.ActorName ?? string.Empty,
				FamilyId = Math.Max(0L, item.FamilyId),
				FamilyName = item.FamilyNameSnapshot ?? string.Empty,
				SectId = Math.Max(0L, item.SectId),
				SectName = NormalizeArchivedSectName(item.SectNameSnapshot)
			});
		}

		ledger ??= Array.Empty<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item == null || item.Year < startYear || item.Year > endYear || item.Importance < 2
				|| string.IsNullOrWhiteSpace(item.Summary)) continue;
			string summary = CompactAnnalsText(item.Summary, 120);
			string title = CompactAnnalsText(ResolveLedgerEventTitle(item), 44);
			string signature = item.Year.ToString(CultureInfo.InvariantCulture) + "|" + title + "|" + summary;
			if (!signatures.Add(signature)) continue;
			candidates.Add(new XjCenturyEventRecord
			{
				EventId = XjDeterministicEventId(item),
				Year = item.Year,
				Category = item.SectId > 0L ? XjWorldHistoryCategory.Sect
					: item.FamilyStableId > 0L ? XjWorldHistoryCategory.Family
					: XjWorldHistoryCategory.Inheritance,
				EventType = item.EventType ?? string.Empty,
				Title = title,
				Summary = summary,
				Importance = Math.Clamp(item.Importance, 1, 5),
				ActorId = Math.Max(0L, item.ActorId),
				ActorName = item.ActorName ?? string.Empty,
				FamilyId = Math.Max(0L, item.FamilyStableId),
				FamilyName = item.FamilyName ?? string.Empty,
				SectId = Math.Max(0L, item.SectId),
				SectName = NormalizeArchivedSectName(item.SectName)
			});
		}

		candidates.Sort((left, right) =>
		{
			int importance = right.Importance.CompareTo(left.Importance);
			if (importance != 0) return importance;
			int year = left.Year.CompareTo(right.Year);
			if (year != 0) return year;
			return left.EventId.CompareTo(right.EventId);
		});
		if (candidates.Count > XjCenturyAnnalsSchema.MaxNotableEventsPerCentury)
		{
			candidates.RemoveRange(XjCenturyAnnalsSchema.MaxNotableEventsPerCentury,
				candidates.Count - XjCenturyAnnalsSchema.MaxNotableEventsPerCentury);
		}
		candidates.Sort((left, right) =>
		{
			int year = left.Year.CompareTo(right.Year);
			if (year != 0) return year;
			int importance = right.Importance.CompareTo(left.Importance);
			return importance != 0 ? importance : left.EventId.CompareTo(right.EventId);
		});
		return candidates;
	}

	private static string ResolveLedgerEventTitle(XjCenturyAnnalsLedgerRecord item)
	{
		if (item == null) return "本卷史事";
		string type = (item.EventType ?? string.Empty).Trim();
		if (type.Length > 0 && !XjHistoryNamePolicy.HasInternalNameMarker(type)
			&& !XjHistoryNamePolicy.ContainsLatinLetter(type)) return type;
		if (!string.IsNullOrWhiteSpace(item.ActorName)) return item.ActorName + "之事";
		if (!string.IsNullOrWhiteSpace(item.FamilyName)) return item.FamilyName + "氏之事";
		if (!string.IsNullOrWhiteSpace(item.SectName)) return item.SectName + "之事";
		return "本卷史事";
	}

	private static string CompactAnnalsText(string value, int maxLength)
	{
		string text = XjDisplayNameSanitizer.Clean(value, string.Empty).Trim();
		if (maxLength <= 0 || text.Length <= maxLength) return text;
		return text.Substring(0, maxLength).TrimEnd('，', '；', '。', ' ') + "…";
	}

	private sealed class BackfillFamilyEvidence
	{
		internal long FamilyId;
		internal string FamilyName = string.Empty;
		internal long SectId;
		internal string SectName = string.Empty;
		internal int FirstYear = int.MaxValue;
		internal int Importance;
		internal readonly List<long> EventIds = new List<long>();
		internal readonly Dictionary<long, string> Actors = new Dictionary<long, string>();
		internal readonly HashSet<long> ZiFuActors = new HashSet<long>();
		internal readonly HashSet<long> JinDanActors = new HashSet<long>();
		internal string HighestRealm = string.Empty;
		internal int HighestRealmOrder;
	}

	private static List<XjCenturyFamilyStateRecord> BuildBackfillFamilyStates(
		IReadOnlyList<XjWorldHistoryArchiveRecord> history,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger,
		int startYear,
		int endYear)
	{
		Dictionary<long, BackfillFamilyEvidence> map = new Dictionary<long, BackfillFamilyEvidence>();
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		for (int i = 0; i < history.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item == null || item.Year < startYear || item.Year > endYear) continue;
			ObserveBackfillFamily(map, item.FamilyId, item.FamilyNameSnapshot, item.ActorId, item.ActorName,
				item.SectId, item.SectNameSnapshot, item.EventId, item.Year, item.Importance, item.Title + " " + item.Body);
			ObserveBackfillFamily(map, item.RelatedFamilyId, item.RelatedFamilyNameSnapshot, item.RelatedActorId, item.RelatedActorName,
				item.RelatedSectId, item.RelatedSectNameSnapshot, item.EventId, item.Year, item.Importance, item.Title + " " + item.Body);
		}
		ledger ??= Array.Empty<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item == null || item.Year < startYear || item.Year > endYear) continue;
			ObserveBackfillFamily(map, item.FamilyStableId, item.FamilyName, item.ActorId, item.ActorName,
				item.SectId, item.SectName, XjDeterministicEventId(item), item.Year, item.Importance, item.Summary);
		}

		List<XjCenturyFamilyStateRecord> result = new List<XjCenturyFamilyStateRecord>();
		foreach (BackfillFamilyEvidence evidence in map.Values)
		{
			if (evidence == null || evidence.FamilyId <= 0L || !XjHistoryNamePolicy.IsChineseFamilyName(evidence.FamilyName)) continue;
			List<long> eventIds = new List<long>();
			for (int i = 0; i < evidence.EventIds.Count && eventIds.Count < XjCenturyAnnalsSchema.MaxEventIdsPerFamily; i++)
			{
				long id = evidence.EventIds[i];
				if (id > 0L && !eventIds.Contains(id)) eventIds.Add(id);
			}
			List<XjCenturyActorHighlightRecord> actors = new List<XjCenturyActorHighlightRecord>();
			foreach (KeyValuePair<long, string> pair in evidence.Actors)
			{
				if (actors.Count >= 4) break;
				if (pair.Key <= 0L || !XjHistoryNamePolicy.IsChineseActorName(pair.Value)) continue;
				actors.Add(new XjCenturyActorHighlightRecord
				{
					ActorId = pair.Key,
					ActorName = pair.Value,
					FamilyStableId = evidence.FamilyId,
					FamilyName = evidence.FamilyName,
					SectId = evidence.SectId,
					SectName = evidence.SectName,
					Realm = evidence.HighestRealm,
					RealmOrder = evidence.HighestRealmOrder,
					Reason = "本卷旧史留名"
				});
			}
			XjCenturyActorHighlightRecord pillar = actors.Count > 0 ? actors[0] : null;
			result.Add(new XjCenturyFamilyStateRecord
			{
				FamilyStableId = evidence.FamilyId,
				FamilyName = evidence.FamilyName,
				CurrentStage = "史录",
				FirstRecordedYear = evidence.FirstYear == int.MaxValue ? startYear : evidence.FirstYear,
				CultivatorCount = evidence.Actors.Count,
				ZiFuCount = evidence.ZiFuActors.Count,
				JinDanCount = evidence.JinDanActors.Count,
				HighestRealm = evidence.HighestRealm,
				HighestRealmOrder = evidence.HighestRealmOrder,
				SectId = evidence.SectId,
				SectName = evidence.SectName,
				VoiceTier = "旧史证见",
				InfluenceScore = evidence.Importance,
				RepresentativeActorId = pillar?.ActorId ?? 0L,
				RepresentativeActorName = pillar?.ActorName ?? string.Empty,
				ImportantEventIds = eventIds,
				ImportantActors = actors,
				StageReason = "旧档补录：本卷留存史料可确证" + evidence.EventIds.Count.ToString(CultureInfo.InvariantCulture)
					+ "件家族相关史事、" + evidence.Actors.Count.ToString(CultureInfo.InvariantCulture)
					+ "名人物留痕；人口与当年完整族势不作伪造。",
				PillarActorId = pillar?.ActorId ?? 0L,
				PillarActorName = pillar?.ActorName ?? string.Empty,
				PillarRealm = evidence.HighestRealm
			});
		}
		result.Sort((left, right) =>
		{
			int importance = right.InfluenceScore.CompareTo(left.InfluenceScore);
			return importance != 0 ? importance : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		if (result.Count > XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury)
			result.RemoveRange(XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxFamilyStatesPerCentury);
		return result;
	}

	private static void ObserveBackfillFamily(
		Dictionary<long, BackfillFamilyEvidence> map,
		long familyId,
		string familyName,
		long actorId,
		string actorName,
		long sectId,
		string sectName,
		long eventId,
		int year,
		int importance,
		string text)
	{
		if (map == null || familyId <= 0L) return;
		string normalizedName = (familyName ?? string.Empty).Trim();
		if (!XjHistoryNamePolicy.IsChineseFamilyName(normalizedName)) return;
		if (!map.TryGetValue(familyId, out BackfillFamilyEvidence evidence))
		{
			evidence = new BackfillFamilyEvidence { FamilyId = familyId, FamilyName = normalizedName };
			map[familyId] = evidence;
		}
		if (evidence.FamilyName.Length == 0) evidence.FamilyName = normalizedName;
		if (sectId > 0L)
		{
			evidence.SectId = sectId;
			evidence.SectName = NormalizeArchivedSectName(sectName);
		}
		evidence.FirstYear = Math.Min(evidence.FirstYear, Math.Max(1, year));
		evidence.Importance += Math.Max(1, importance);
		if (eventId > 0L && !evidence.EventIds.Contains(eventId)) evidence.EventIds.Add(eventId);
		if (actorId > 0L && XjHistoryNamePolicy.IsChineseActorName(actorName)) evidence.Actors[actorId] = actorName.Trim();
		if (TryInferRealmFromText(text, out string realm, out int order))
		{
			if (order > evidence.HighestRealmOrder)
			{
				evidence.HighestRealmOrder = order;
				evidence.HighestRealm = realm;
			}
			if (actorId > 0L && order >= 7) evidence.JinDanActors.Add(actorId);
			else if (actorId > 0L && order >= 6) evidence.ZiFuActors.Add(actorId);
		}
	}

	private sealed class BackfillSectEvidence
	{
		internal long SectId;
		internal string Name = string.Empty;
		internal int Importance;
		internal int EventCount;
		internal readonly List<string> Highlights = new List<string>();
	}

	private static List<XjCenturySummaryItemRecord> BuildBackfillSectSummaries(
		IReadOnlyList<XjWorldHistoryArchiveRecord> history,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		Dictionary<long, BackfillSectEvidence> map = new Dictionary<long, BackfillSectEvidence>();
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		for (int i = 0; i < history.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item == null) continue;
			ObserveBackfillSect(map, item.SectId, item.SectNameSnapshot, item.Importance, item.Title);
			ObserveBackfillSect(map, item.RelatedSectId, item.RelatedSectNameSnapshot, item.Importance, item.Title);
		}
		ledger ??= Array.Empty<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item == null) continue;
			ObserveBackfillSect(map, item.SectId, item.SectName, item.Importance, item.Summary);
		}
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>();
		foreach (BackfillSectEvidence evidence in map.Values)
		{
			if (evidence == null || evidence.SectId <= 0L || !XjHistoryNamePolicy.IsChineseSectName(evidence.Name)) continue;
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = evidence.SectId.ToString(CultureInfo.InvariantCulture),
				Name = evidence.Name,
				Score = evidence.Importance,
				Trend = "史料证见",
				State = "补卷",
				Summary = "留存旧史可确证" + evidence.EventCount.ToString(CultureInfo.InvariantCulture) + "件宗门相关史事"
					+ (evidence.Highlights.Count > 0 ? "；" + string.Join("；", evidence.Highlights) : string.Empty)
			});
		}
		result.Sort((left, right) => right.Score.CompareTo(left.Score));
		if (result.Count > XjCenturyAnnalsSchema.MaxSectSummariesPerCentury)
			result.RemoveRange(XjCenturyAnnalsSchema.MaxSectSummariesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxSectSummariesPerCentury);
		return result;
	}

	private static void ObserveBackfillSect(
		Dictionary<long, BackfillSectEvidence> map,
		long sectId,
		string sectName,
		int importance,
		string highlight)
	{
		if (map == null || sectId <= 0L) return;
		string name = NormalizeArchivedSectName(sectName);
		if (!XjHistoryNamePolicy.IsChineseSectName(name)) return;
		if (!map.TryGetValue(sectId, out BackfillSectEvidence evidence))
		{
			evidence = new BackfillSectEvidence { SectId = sectId, Name = name };
			map[sectId] = evidence;
		}
		evidence.Importance += Math.Max(1, importance);
		evidence.EventCount++;
		string compact = CompactAnnalsText(highlight, 30);
		if (compact.Length > 0 && evidence.Highlights.Count < 3 && !evidence.Highlights.Contains(compact)) evidence.Highlights.Add(compact);
	}

	private static List<XjCenturySummaryItemRecord> BuildBackfillRealmEvidence(
		IReadOnlyList<XjWorldHistoryArchiveRecord> history,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
		Dictionary<string, int> orders = new Dictionary<string, int>(StringComparer.Ordinal);
		history ??= Array.Empty<XjWorldHistoryArchiveRecord>();
		for (int i = 0; i < history.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = history[i];
			if (item != null && TryInferRealmFromText(item.Title + " " + item.Body, out string realm, out int order))
			{
				AddCount(counts, realm, 1);
				orders[realm] = order;
			}
		}
		ledger ??= Array.Empty<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item != null && TryInferRealmFromText(item.Summary, out string realm, out int order))
			{
				AddCount(counts, realm, 1);
				orders[realm] = order;
			}
		}
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>();
		foreach (KeyValuePair<string, int> pair in counts)
		{
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = pair.Key,
				Name = pair.Key,
				Score = pair.Value,
				Trend = "史料出现",
				State = "补卷",
				Summary = "留存史料中有" + pair.Value.ToString(CultureInfo.InvariantCulture)
					+ "条明确涉及此层次；这是史料出现次数，不是当年人口快照。"
			});
		}
		result.Sort((left, right) =>
		{
			orders.TryGetValue(left.Name, out int lo);
			orders.TryGetValue(right.Name, out int ro);
			return ro.CompareTo(lo);
		});
		if (result.Count > XjCenturyAnnalsSchema.MaxRealmSummariesPerCentury)
			result.RemoveRange(XjCenturyAnnalsSchema.MaxRealmSummariesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxRealmSummariesPerCentury);
		return result;
	}

	private static List<XjCenturySummaryItemRecord> BuildBackfillDaoEvidence(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger)
	{
		Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
		Dictionary<string, int> scores = new Dictionary<string, int>(StringComparer.Ordinal);
		ledger ??= Array.Empty<XjCenturyAnnalsLedgerRecord>();
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			string daoTu = XjDisplayNameSanitizer.GameTerm(item?.DaoTu, string.Empty).Trim();
			if (daoTu.Length == 0) continue;
			AddCount(counts, daoTu, 1);
			AddCount(scores, daoTu, Math.Max(1, item.Importance));
		}
		List<XjCenturySummaryItemRecord> result = new List<XjCenturySummaryItemRecord>();
		foreach (KeyValuePair<string, int> pair in counts)
		{
			scores.TryGetValue(pair.Key, out int score);
			result.Add(new XjCenturySummaryItemRecord
			{
				Key = pair.Key,
				Name = pair.Key,
				Score = score,
				Trend = "史料证见",
				State = "补卷",
				Summary = "本卷账本仍可确证" + pair.Value.ToString(CultureInfo.InvariantCulture) + "条与此道途直接相关的记录。"
			});
		}
		result.Sort((left, right) => right.Score.CompareTo(left.Score));
		if (result.Count > XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury)
			result.RemoveRange(XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury, result.Count - XjCenturyAnnalsSchema.MaxDaoSummariesPerCentury);
		return result;
	}

	private static bool TryInferRealmFromText(string value, out string realm, out int order)
	{
		string text = value ?? string.Empty;
		(string Name, int Order)[] terms =
		{
			("世尊", 10), ("道胎", 9), ("法相", 8), ("金丹", 7), ("真君羽士", 7),
			("摩诃", 6), ("紫府", 6), ("真人", 6), ("怜愍", 5), ("筑基", 5),
			("法师", 4), ("炼气", 4), ("僧侣", 3), ("胎息", 3)
		};
		for (int i = 0; i < terms.Length; i++)
		{
			if (text.IndexOf(terms[i].Name, StringComparison.Ordinal) < 0) continue;
			realm = terms[i].Name;
			order = terms[i].Order;
			return true;
		}
		realm = string.Empty;
		order = 0;
		return false;
	}

	private static string BuildBackfillWorldSummary(
		int centuryId,
		IReadOnlyList<XjCenturyEventRecord> events,
		IReadOnlyList<XjCenturyFamilyStateRecord> families,
		IReadOnlyList<XjCenturySummaryItemRecord> sects,
		IReadOnlyList<XjCenturyActorHighlightRecord> actors,
		IReadOnlyList<XjCenturySummaryItemRecord> realms,
		IReadOnlyList<XjCenturySummaryItemRecord> dao,
		IReadOnlyList<XjCenturySummaryItemRecord> artifacts)
	{
		return "第" + centuryId.ToString(CultureInfo.InvariantCulture)
			+ "百年为旧档补卷：现存史料可确证大事" + (events?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "件、家族" + (families?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "支、宗门" + (sects?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "座、留名人物" + (actors?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "人、境界证见" + (realms?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "项、道途记录" + (dao?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "项、传承器物" + (artifacts?.Count ?? 0).ToString(CultureInfo.InvariantCulture)
			+ "项。人口、家族完整规模与当年实时榜单无法从旧档凭空还原，故仅录可确证史事，不以当前世界状态倒灌。";
	}

	private static Dictionary<long, XjCenturyFamilyStageStateRecord> BuildPreviousStageMap(XjCenturyAnnalsStateRecord state)
	{
		Dictionary<long, XjCenturyFamilyStageStateRecord> result = new Dictionary<long, XjCenturyFamilyStageStateRecord>();
		if (state?.FamilyStageStates == null) return result;
		for (int i = 0; i < state.FamilyStageStates.Count; i++)
		{
			XjCenturyFamilyStageStateRecord item = state.FamilyStageStates[i];
			if (item == null || item.FamilyStableId <= 0L) continue;
			result[item.FamilyStableId] = item;
		}
		return result;
	}

	private static Dictionary<long, XjSectFamilySeatArchiveRecord> BuildSeatMap(IReadOnlyList<XjSectFamilySeatArchiveRecord> seats)
	{
		Dictionary<long, XjSectFamilySeatArchiveRecord> result = new Dictionary<long, XjSectFamilySeatArchiveRecord>();
		if (seats == null) return result;
		for (int i = 0; i < seats.Count; i++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[i];
			if (seat == null || seat.FamilyId <= 0L) continue;
			if (!result.TryGetValue(seat.FamilyId, out XjSectFamilySeatArchiveRecord existing) || seat.VoiceScore > existing.VoiceScore)
			{
				result[seat.FamilyId] = seat;
			}
		}
		return result;
	}

	private static Dictionary<long, int> BuildGovernanceCounts(IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance)
	{
		Dictionary<long, int> result = new Dictionary<long, int>();
		if (governance == null) return result;
		for (int i = 0; i < governance.Count; i++)
		{
			XjCityFamilyGovernanceArchiveRecord item = governance[i];
			if (item == null || item.GoverningFamilyId <= 0L) continue;
			AddCount(result, item.GoverningFamilyId, 1);
		}
		return result;
	}

	private static Dictionary<long, string> BuildSectNames(IReadOnlyList<XjSectArchiveRecord> sects)
	{
		Dictionary<long, string> result = new Dictionary<long, string>();
		if (sects == null) return result;
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect == null || sect.SectId <= 0L) continue;
			result[sect.SectId] = sect.Name ?? string.Empty;
		}
		return result;
	}

	private static Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> BuildLedgerByFamily(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger, int centuryId)
	{
		Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> result = new Dictionary<long, List<XjCenturyAnnalsLedgerRecord>>();
		if (ledger == null) return result;
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item == null || item.CenturyId != centuryId || item.FamilyStableId <= 0L) continue;
			if (!result.TryGetValue(item.FamilyStableId, out List<XjCenturyAnnalsLedgerRecord> list))
			{
				list = new List<XjCenturyAnnalsLedgerRecord>();
				result[item.FamilyStableId] = list;
			}
			list.Add(item);
		}
		foreach (List<XjCenturyAnnalsLedgerRecord> list in result.Values)
		{
			list.Sort((left, right) =>
			{
				int importance = right.Importance.CompareTo(left.Importance);
				if (importance != 0) return importance;
				int year = right.Year.CompareTo(left.Year);
				if (year != 0) return year;
				int actor = left.ActorId.CompareTo(right.ActorId);
				if (actor != 0) return actor;
				int type = string.Compare(left.EventType, right.EventType, StringComparison.Ordinal);
				return type != 0 ? type : string.Compare(left.Summary, right.Summary, StringComparison.Ordinal);
			});
		}
		return result;
	}

	private static Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> BuildLedgerBySect(IReadOnlyList<XjCenturyAnnalsLedgerRecord> ledger, int centuryId)
	{
		Dictionary<long, List<XjCenturyAnnalsLedgerRecord>> result = new Dictionary<long, List<XjCenturyAnnalsLedgerRecord>>();
		if (ledger == null) return result;
		for (int i = 0; i < ledger.Count; i++)
		{
			XjCenturyAnnalsLedgerRecord item = ledger[i];
			if (item == null || item.CenturyId != centuryId || item.SectId <= 0L) continue;
			if (!result.TryGetValue(item.SectId, out List<XjCenturyAnnalsLedgerRecord> list))
			{
				list = new List<XjCenturyAnnalsLedgerRecord>();
				result[item.SectId] = list;
			}
			list.Add(item);
		}
		foreach (List<XjCenturyAnnalsLedgerRecord> list in result.Values)
		{
			list.Sort((left, right) =>
			{
				int importance = right.Importance.CompareTo(left.Importance);
				if (importance != 0) return importance;
				int year = right.Year.CompareTo(left.Year);
				if (year != 0) return year;
				int actor = left.ActorId.CompareTo(right.ActorId);
				if (actor != 0) return actor;
				int type = string.Compare(left.EventType, right.EventType, StringComparison.Ordinal);
				return type != 0 ? type : string.Compare(left.Summary, right.Summary, StringComparison.Ordinal);
			});
		}
		return result;
	}

	private static List<long> BuildFamilyEventIds(IReadOnlyList<XjCenturyAnnalsLedgerRecord> familyLedger)
	{
		List<long> result = new List<long>();
		if (familyLedger == null) return result;
		for (int i = 0; i < familyLedger.Count && result.Count < XjCenturyAnnalsSchema.MaxEventIdsPerFamily; i++)
		{
			XjCenturyAnnalsLedgerRecord item = familyLedger[i];
			if (item == null || item.Year <= 0) continue;
			result.Add(XjDeterministicEventId(item));
		}
		return result;
	}

	private static List<XjCenturyActorHighlightRecord> BuildFamilyActorHighlights(
		in XjFamilyLedgerAggregate aggregate,
		string familyName,
		long sectId,
		string sectName,
		IReadOnlyList<XjCenturyAnnalsLedgerRecord> familyLedger)
	{
		List<XjCenturyActorHighlightRecord> result = new List<XjCenturyActorHighlightRecord>();
		HashSet<long> seenActorIds = new HashSet<long>();
		if (familyLedger != null)
		{
			for (int i = 0; i < familyLedger.Count && result.Count < 4; i++)
			{
				XjCenturyAnnalsLedgerRecord ledgerItem = familyLedger[i];
				if (ledgerItem == null || ledgerItem.ActorId <= 0L || !seenActorIds.Add(ledgerItem.ActorId)) continue;
				string actorName = string.IsNullOrWhiteSpace(ledgerItem.ActorName) ? ResolveActorName(ledgerItem.ActorId) : ledgerItem.ActorName.Trim();
				if (!XjHistoryNamePolicy.IsChineseActorName(actorName)) continue;
				ResolveActorRealmForAnnals(ledgerItem.ActorId, aggregate.HighestRealm, aggregate.HighestRealmOrder, out string realm, out int realmOrder);
				string ledgerSectName = NormalizeArchivedSectName(ledgerItem.SectName);
				result.Add(new XjCenturyActorHighlightRecord
				{
					ActorId = ledgerItem.ActorId,
					ActorName = actorName,
					FamilyStableId = aggregate.FamilyStableId,
					FamilyName = familyName,
					SectId = ledgerItem.SectId > 0L ? ledgerItem.SectId : sectId,
					SectName = string.IsNullOrWhiteSpace(ledgerSectName) ? sectName : ledgerSectName,
					Realm = realm,
					RealmOrder = realmOrder,
					Reason = string.IsNullOrWhiteSpace(ledgerItem.Summary) ? "本卷留下事迹" : ledgerItem.Summary
				});
			}
		}

		return result;
	}

	private static void AppendVolumeRepresentativeActors(
		List<XjCenturyActorHighlightRecord> target,
		XjCenturyFamilyStateRecord familyState)
	{
		if (target == null || familyState == null) return;
		IReadOnlyList<XjCenturyActorHighlightRecord> candidates = familyState.ImportantActors;
		if (candidates == null || candidates.Count == 0) return;
		for (int i = 0; i < candidates.Count && target.Count < XjCenturyAnnalsSchema.MaxActorHighlightsPerCentury; i++)
		{
			XjCenturyActorHighlightRecord candidate = candidates[i];
			if (candidate == null || candidate.ActorId <= 0L || ContainsActor(target, candidate.ActorId)) continue;
			target.Add(candidate);
		}
	}

	private static bool ContainsActor(IReadOnlyList<XjCenturyActorHighlightRecord> items, long actorId)
	{
		if (items == null || actorId <= 0L) return false;
		for (int i = 0; i < items.Count; i++) if (items[i]?.ActorId == actorId) return true;
		return false;
	}

	private static bool IsVolumeEventRepresentative(XjCenturyActorHighlightRecord item)
	{
		return item != null
			&& !string.IsNullOrWhiteSpace(item.Reason)
			&& item.Reason.IndexOf("家族代表", StringComparison.Ordinal) < 0;
	}

	private static void ResolveActorRealmForAnnals(long actorId, string fallbackRealm, int fallbackOrder, out string realm, out int realmOrder)
	{
		realm = fallbackRealm ?? string.Empty;
		realmOrder = Math.Max(0, fallbackOrder);
		if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null) return;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string actorRealm) || string.IsNullOrWhiteSpace(actorRealm)) return;
		realm = XjRealmHelper.GetDisplayName(actorRealm);
		realmOrder = XjFamilyMemberLedger.GetRealmOrder(actorRealm);
	}

	private static long XjDeterministicEventId(XjCenturyAnnalsLedgerRecord item)
	{
		unchecked
		{
			long value = 1469598103934665603L;
			value = (value * 1099511628211L) ^ item.CenturyId;
			value = (value * 1099511628211L) ^ item.FamilyStableId;
			value = (value * 1099511628211L) ^ item.ActorId;
			value = (value * 1099511628211L) ^ item.Year;
			return value < 0L ? -value : value;
		}
	}

	private static void AddCount(Dictionary<long, int> map, long key, int count)
	{
		if (key <= 0L || count == 0) return;
		map.TryGetValue(key, out int current);
		map[key] = current + count;
	}

	private static void AddCount(Dictionary<string, int> map, string key, int count)
	{
		if (string.IsNullOrWhiteSpace(key) || count == 0) return;
		map.TryGetValue(key, out int current);
		map[key] = current + count;
	}
}
