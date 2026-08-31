using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;

using XuanJianVNext.Architecture.Presentation;
namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{
		// This is an annual whole-city audit. Keep the working sets resident so
		// long-running worlds do not allocate a new grouping per sect each year.
		private static readonly Dictionary<long, List<long>> RuntimeCityIdsBySectId = new Dictionary<long, List<long>>();
		private static readonly List<XjSectArchiveRecord> RuntimeSectBuffer = new List<XjSectArchiveRecord>();
		private static readonly List<long> RuntimeScratchSectIds = new List<long>();
		private static readonly List<long> GovernanceRemovalBuffer = new List<long>();
		private static readonly List<(long SectId, long FamilyId)> FamilySeatRemovalBuffer = new List<(long SectId, long FamilyId)>();
		private static readonly Dictionary<long, int> SuccessorVoteBuffer = new Dictionary<long, int>();
		private static readonly List<long> EmptyCityIdBuffer = new List<long>(0);
		private static readonly List<long> SectMemberMirrorBuffer = new List<long>();
		private static readonly HashSet<long> SectMemberTransferSeen = new HashSet<long>();
		private static readonly List<long> FamilySeatTransferBuffer = new List<long>();

		internal static void ReconcileRuntimeTerritory(int currentYear)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || BySectId.Count == 0) return;
			IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
			// 即使世界当前没有城市，也继续结算旧宗门，确保不会保留无城宗门运行态。
			ResetRuntimeCityBuffers();
			bool changed = false;
			for (int cityIndex = 0; cityIndex < cities.Count; cityIndex++)
			{
				City city = cities[cityIndex];
				if (city?.data == null) continue;
				// Native population ownership is repaired at the transfer boundary.
				// Territory reconciliation must only maintain CityId -> SectId data;
				// repairing every city's residents here turns a SectId audit into a
				// costly world-wide population pass.
				long cityId = city.data.id;
				long explicitSectId = ResolveExplicitSectIdForCity(city, cityId);
				if (explicitSectId <= 0L) continue;
				if (CanRuntimeAcceptCity(RuntimeCityIdsBySectId, explicitSectId, cityId))
				{
					AddCityId(RuntimeCityIdsBySectId, explicitSectId, cityId);
				}
				else
				{
					ReleaseCitySectBinding(city, cityId, explicitSectId);
					changed = true;
				}
			}
	
			RuntimeSectBuffer.Clear();
			foreach (XjSectArchiveRecord sect in BySectId.Values) RuntimeSectBuffer.Add(sect);
			for (int i = 0; i < RuntimeSectBuffer.Count; i++)
			{
				XjSectArchiveRecord record = RuntimeSectBuffer[i];
				if (record == null || record.SectId <= 0L) continue;
				// 0.9.8.8：0门人不是“衰败中的宗门”，而是已经灭门。五年领地
				// 对账本就会遍历现存宗门，顺手在这里做第二道硬清理，确保旧档里
				// 因治理队列饥饿遗留下来的空门不必再等到逐宗 Prosperity 队列。
				// 同一创宗年留出事务完成窗口，避免创建尚未完成时的瞬时0人被误杀。
				if (currentYear > Math.Max(0, record.FoundingYear)
					&& XjSectAuthorityStore.CountMembers(record.SectId) == 0)
				{
					TryExtinguishSectByDecline(record.SectId, currentYear, "门人断绝，山门无人承继");
					changed = true;
					continue;
				}
				if (EnsureFoundingFormationInvariant(record, currentYear))
				{
					changed = true;
				}
				if (RefreshFormationPower(record))
				{
					changed = true;
				}
				if (!RuntimeCityIdsBySectId.TryGetValue(record.SectId, out List<long> currentCityIds)) currentCityIds = EmptyCityIdBuffer;
				currentCityIds.Sort();
				bool hasTerritory = currentCityIds.Count > 0;
				string previousStatus = record.Status ?? XjSectStatus.SectRegime;
				string nextStatus = ResolveTerritoryStatus(record, currentCityIds.Count);
				if (string.Equals(nextStatus, XjSectStatus.Extinct, StringComparison.Ordinal))
				{
					TransferDefeatedSectWarehouse(record, currentYear);
					RecordTerritoryStatusEvent(record, previousStatus, nextStatus, currentYear);
					RetireSectRecord(record, currentYear, "NoNativeCities");
					changed = true;
					continue;
				}
				bool territoryChangedForRecord = false;
				if (!SequenceEqual(record.CityIds, currentCityIds))
				{
					ReplaceCityIds(record, currentCityIds);
					territoryChangedForRecord = true;
					changed = true;
				}
				if (hasTerritory && !record.CityIds.Contains(record.CapitalCityId))
				{
					record.CapitalCityId = currentCityIds[0];
					territoryChangedForRecord = true;
					changed = true;
				}
				if (!string.Equals(previousStatus, nextStatus, StringComparison.Ordinal))
				{
					record.Status = nextStatus;
					territoryChangedForRecord = true;
					changed = true;
					RecordTerritoryStatusEvent(record, previousStatus, nextStatus, currentYear);
				}
				if (hasTerritory && territoryChangedForRecord)
				{
					XjCityFamilyGovernanceSystem.InitializeForSect(null, record, record.FounderFamilyId, currentYear);
				}
			}
	
			PruneRuntimeCityBuffers();

			foreach (KeyValuePair<long, XjCityFamilyGovernanceArchiveRecord> pair in GovernanceByCityId)
			{
				XjCityFamilyGovernanceArchiveRecord governance = pair.Value;
				if (governance == null || governance.SectId <= 0L) continue;
				if (!BySectId.TryGetValue(governance.SectId, out XjSectArchiveRecord sect)
					|| sect.CityIds == null
					|| !sect.CityIds.Contains(governance.CityId))
				{
					governance.SectId = 0L;
					governance.State = XjCityFamilyGovernanceState.Prominent;
					governance.ChallengerFamilyId = 0L;
					governance.ChallengeStartYear = 0;
					governance.ChallengeConsecutiveYears = 0;
					changed = true;
				}
			}
	
			if (!changed) return;
			XjSectAuthorityStore.RebuildCityAuthorityIndex();
			for (int i = 0; i < RuntimeSectBuffer.Count; i++) if (RuntimeSectBuffer[i]?.SectId > 0L) XjSectAuthorityStore.MarkProjectionDirty(RuntimeSectBuffer[i].SectId);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.City | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Conflict | XjCodexDirtyFlags.History);
			XjPresentationHooks.MarkSectMapDirty();
		}

		private static void ResetRuntimeCityBuffers()
		{
			foreach (List<long> ids in RuntimeCityIdsBySectId.Values) ids.Clear();
		}

		private static void PruneRuntimeCityBuffers()
		{
			RuntimeScratchSectIds.Clear();
			foreach (KeyValuePair<long, List<long>> pair in RuntimeCityIdsBySectId)
			{
				if (!BySectId.ContainsKey(pair.Key)) RuntimeScratchSectIds.Add(pair.Key);
			}
			for (int i = 0; i < RuntimeScratchSectIds.Count; i++) RuntimeCityIdsBySectId.Remove(RuntimeScratchSectIds[i]);
		}

		private static void ReplaceCityIds(XjSectArchiveRecord record, List<long> source)
		{
			if (record == null) return;
			record.CityIds ??= new List<long>();
			record.CityIds.Clear();
			if (source != null && source.Count > 0) record.CityIds.AddRange(source);
		}

		private static long ResolveExplicitSectIdForCity(City city, long cityId)
		{
			long citySectId = XjSectCityData.GetZongMenId(city);
			if (citySectId > 0L && BySectId.ContainsKey(citySectId)) return citySectId;
			if (cityId > 0L
				&& GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance)
				&& governance?.SectId > 0L
				&& BySectId.ContainsKey(governance.SectId))
			{
				return governance.SectId;
			}
			return 0L;
		}

		private static void AddCityId(Dictionary<long, List<long>> map, long sectId, long cityId)
		{
			if (map == null || sectId <= 0L || cityId <= 0L) return;
			if (!map.TryGetValue(sectId, out List<long> ids))
			{
				ids = new List<long>();
				map[sectId] = ids;
			}
			if (ids.Contains(cityId)) return;
			ids.Add(cityId);
		}

		private static bool CanRuntimeAcceptCity(Dictionary<long, List<long>> map, long sectId, long cityId)
		{
			if (map == null || sectId <= 0L || cityId <= 0L) return false;
			if (BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)
				&& record?.CityIds != null
				&& record.CityIds.Contains(cityId))
			{
				return true;
			}
			if (!map.TryGetValue(sectId, out List<long> ids) || ids == null) return true;
			return ids.Contains(cityId) || ids.Count < MaxSectCityCount;
		}

		private static void ReleaseCitySectBinding(City city, long cityId, long sectId)
		{
			if (city?.data != null && sectId > 0L && XjSectCityData.GetZongMenId(city) == sectId)
			{
				XjSectCityData.Clear(city);
			}

			if (cityId <= 0L
				|| !GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance)
				|| governance == null
				|| governance.SectId != sectId)
			{
				return;
			}

			governance.SectId = 0L;
			governance.State = XjCityFamilyGovernanceState.Prominent;
			governance.ChallengerFamilyId = 0L;
			governance.ChallengeStartYear = 0;
			governance.ChallengeConsecutiveYears = 0;
		}

		internal static bool TryDefeatSectByFormationBreak(long defeatedSectId, long victorSectId, int currentYear, string reason)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled
				|| defeatedSectId <= 0L
				|| victorSectId <= 0L
				|| defeatedSectId == victorSectId
				|| !BySectId.TryGetValue(defeatedSectId, out XjSectArchiveRecord defeated)
				|| defeated == null
				|| !BySectId.TryGetValue(victorSectId, out XjSectArchiveRecord victor)
				|| victor == null)
			{
				return false;
			}

			List<long> cityIds = defeated.CityIds == null ? new List<long>(0) : new List<long>(defeated.CityIds);
			CollectDefeatedSectMemberIds(defeated, cityIds);
			for (int i = 0; i < cityIds.Count; i++)
			{
				long cityId = cityIds[i];
				if (cityId <= 0L) continue;
				City city = ResolveCityById(cityId);
				if (city?.data != null)
				{
					XjSectCityData.RebindSectMirror(city, victor.SectId, victor.Name);
				}
				if (victor.CityIds == null) victor.CityIds = new List<long>();
				if (!victor.CityIds.Contains(cityId)) victor.CityIds.Add(cityId);
				if (GovernanceByCityId.TryGetValue(cityId, out XjCityFamilyGovernanceArchiveRecord governance) && governance != null)
				{
					governance.SectId = victor.SectId;
					governance.State = XjCityFamilyGovernanceState.Stable;
					governance.LastChallengeYear = Math.Max(governance.LastChallengeYear, currentYear);
					if (governance.GoverningFamilyId > 0L)
					{
						EnsureFamilySeat(victor.SectId, governance.GoverningFamilyId, currentYear);
						if (victor.FamilyIds == null) victor.FamilyIds = new List<long>();
						if (!victor.FamilyIds.Contains(governance.GoverningFamilyId)) victor.FamilyIds.Add(governance.GoverningFamilyId);
					}
				}
			}
			TransferDefeatedFamilySeats(victor, defeated, currentYear);
			victor.CityIds?.Sort();
			victor.FamilyIds?.Sort();
			if (victor.CityIds != null && victor.CityIds.Count > 0 && !victor.CityIds.Contains(victor.CapitalCityId))
			{
				victor.CapitalCityId = victor.CityIds[0];
			}
			RefreshFormationPower(victor);

			XjSectWarSpoils spoils = XjSectWarSpoilsSystem.ClaimAll(victor, defeated, currentYear);
			int absorbedMembers = AbsorbDefeatedSectMembers(victor, currentYear);
			string victorName = string.IsNullOrWhiteSpace(victor.Name) ? "胜宗" : victor.Name.Trim();
			string defeatedName = string.IsNullOrWhiteSpace(defeated.Name) ? "败宗" : defeated.Name.Trim();
			string summary = string.IsNullOrWhiteSpace(reason) ? "宗门大阵已破" : reason.Trim();
			string body = "【破阵灭宗】" + defeatedName + summary + "，山门底蕴尽归" + victorName + "。";
			if (absorbedMembers > 0)
			{
				body += "接收弟子" + absorbedMembers.ToString(CultureInfo.InvariantCulture) + "人。";
			}
			if (spoils.HasAny)
			{
				body += "收缴功法" + spoils.GongFaEntries + "卷、求金法" + spoils.QiuJinFaEntries + "卷、"
					+ "先天之气" + spoils.CaiQiAmount + "份、丹药" + spoils.PillQuantity + "枚、"
					+ "符箓" + spoils.TalismanQuantity + "张、丹器符阵等物资" + spoils.CraftResourceQuantity + "份。";
			}
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				victorName + "破灭" + defeatedName,
				body,
				5,
				true,
				sectId: victor.SectId,
				cityId: victor.CapitalCityId,
				year: currentYear);
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				body, XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Sect, duration: 10f, color: "#D94C4C", delayFrames: 1);

			RecordTerritoryStatusEvent(defeated, defeated.Status ?? XjSectStatus.SectRegime, XjSectStatus.Extinct, currentYear);
			RetireSectRecord(defeated, currentYear, "FormationBrokenBySectWar");
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.City | XjCodexDirtyFlags.Family | XjCodexDirtyFlags.Formation | XjCodexDirtyFlags.Conflict | XjCodexDirtyFlags.History);
			XjPresentationHooks.MarkSectMapDirty();
			return true;
		}

		private static void CollectDefeatedSectMemberIds(XjSectArchiveRecord defeated, List<long> cityIds)
		{
			SectMemberMirrorBuffer.Clear();
			SectMemberTransferSeen.Clear();
			if (defeated == null || defeated.SectId <= 0L) return;
			AddDefeatedSectMemberId(defeated.SovereignActorId);
			if (defeated.Peaks != null)
			{
				for (int i = 0; i < defeated.Peaks.Count; i++)
				{
					AddDefeatedSectMemberId(defeated.Peaks[i]?.PeakMasterActorId ?? 0L);
				}
			}

			IReadOnlyList<long> indexedIds = XjSectAuthorityStore.GetActorIdsForSect(defeated.SectId);
			for (int i = 0; i < indexedIds.Count; i++) AddDefeatedSectMemberId(indexedIds[i]);
			if (cityIds == null) return;
			for (int i = 0; i < cityIds.Count; i++)
			{
				City city = ResolveCityById(cityIds[i]);
				if (city?.data == null
					|| !XjSectCityData.HasZongMen(city)
					|| XjSectCityData.GetZongMenId(city) != defeated.SectId)
				{
					continue;
				}
				List<long> memberIds = XjSectCityData.GetMemberIds(city);
				for (int j = 0; j < memberIds.Count; j++) AddDefeatedSectMemberId(memberIds[j]);
			}
		}

		private static void AddDefeatedSectMemberId(long actorId)
		{
			if (actorId <= 0L || !SectMemberTransferSeen.Add(actorId)) return;
			SectMemberMirrorBuffer.Add(actorId);
		}

		private static int AbsorbDefeatedSectMembers(XjSectArchiveRecord victor, int currentYear)
		{
			if (victor == null || victor.SectId <= 0L || !TryResolvePrimarySectCity(victor, out City victorCity))
			{
				SectMemberMirrorBuffer.Clear();
				SectMemberTransferSeen.Clear();
				return 0;
			}

			int absorbed = 0;
			for (int i = 0; i < SectMemberMirrorBuffer.Count; i++)
			{
				if (!XjScheduler.ResolveActor(SectMemberMirrorBuffer[i], out Actor actor)
					|| actor?.data == null
					|| !actor.isAlive())
				{
					continue;
				}
				long previousSectId = ReadActorSectId(actor);
				bool added = XjSectMembershipService.EnsureMember(victorCity, actor, currentYear, "SectDestroyedAbsorbMember");
				if (added || previousSectId != victor.SectId)
				{
					if (XjSectCityData.IsMember(victorCity, actor))
					{
						absorbed++;
					}
				}
				XjSectCultivatorCityIndex.Observe(actor);
			}
			SectMemberMirrorBuffer.Clear();
			SectMemberTransferSeen.Clear();
			XjSectCityData.AssignRolesFromScratch(victorCity, currentYear);
			ReconcilePeaks(victor.SectId, currentYear);
			return absorbed;
		}

		private static bool TryResolvePrimarySectCity(XjSectArchiveRecord record, out City city)
		{
			return XjSectOwnership.TryResolvePrimaryCity(record, out city);
		}

		private static void TransferDefeatedFamilySeats(XjSectArchiveRecord victor, XjSectArchiveRecord defeated, int currentYear)
		{
			if (victor == null || defeated == null || victor.SectId <= 0L || defeated.SectId <= 0L) return;
			if (victor.FamilyIds == null) victor.FamilyIds = new List<long>();
			FamilySeatTransferBuffer.Clear();
			if (defeated.FamilyIds != null)
			{
				for (int i = 0; i < defeated.FamilyIds.Count; i++)
				{
					long familyId = defeated.FamilyIds[i];
					if (familyId > 0L && !FamilySeatTransferBuffer.Contains(familyId)) FamilySeatTransferBuffer.Add(familyId);
				}
			}
			foreach (KeyValuePair<(long SectId, long FamilyId), XjSectFamilySeatArchiveRecord> pair in FamilySeats)
			{
				if (pair.Key.SectId != defeated.SectId || pair.Key.FamilyId <= 0L) continue;
				if (!FamilySeatTransferBuffer.Contains(pair.Key.FamilyId)) FamilySeatTransferBuffer.Add(pair.Key.FamilyId);
			}
			for (int i = 0; i < FamilySeatTransferBuffer.Count; i++)
			{
				long familyId = FamilySeatTransferBuffer[i];
				EnsureFamilySeat(victor.SectId, familyId, currentYear);
				if (!victor.FamilyIds.Contains(familyId)) victor.FamilyIds.Add(familyId);
			}
			FamilySeatTransferBuffer.Clear();
		}

		private static void TransferDefeatedSectWarehouse(XjSectArchiveRecord defeated, int currentYear)
		{
			if (defeated?.SectId <= 0L || defeated.CityIds == null || defeated.CityIds.Count == 0) return;
			SuccessorVoteBuffer.Clear();
			for (int i = 0; i < defeated.CityIds.Count; i++)
			{
				City city = ResolveCityById(defeated.CityIds[i]);
				long citySectId = ResolveExplicitSectIdForCity(city, defeated.CityIds[i]);
				if (citySectId > 0L && citySectId != defeated.SectId)
				{
					SuccessorVoteBuffer.TryGetValue(citySectId, out int explicitVotes);
					SuccessorVoteBuffer[citySectId] = explicitVotes + 1;
					continue;
				}

			}

			long successorId = 0L;
			int bestVotes = 0;
			foreach (KeyValuePair<long, int> vote in SuccessorVoteBuffer)
			{
				if (vote.Value > bestVotes || (vote.Value == bestVotes && (successorId == 0L || vote.Key < successorId)))
				{
					successorId = vote.Key;
					bestVotes = vote.Value;
				}
			}
			if (successorId <= 0L || !BySectId.TryGetValue(successorId, out XjSectArchiveRecord victor) || victor == null) return;

			XjSectWarSpoils spoils = XjSectWarSpoilsSystem.ClaimAll(victor, defeated, currentYear);
			if (!spoils.HasAny) return;
			string victorName = string.IsNullOrWhiteSpace(victor.Name) ? "胜宗" : victor.Name.Trim();
			string defeatedName = string.IsNullOrWhiteSpace(defeated.Name) ? "败宗" : defeated.Name.Trim();
			string body = "【灭宗收缴】" + victorName + "收缴" + defeatedName + "宗门底蕴。"
				+ "功法" + spoils.GongFaEntries + "卷、求金法" + spoils.QiuJinFaEntries + "卷、"
				+ "先天之气" + spoils.CaiQiAmount + "份、丹药" + spoils.PillQuantity + "枚、"
				+ "符箓" + spoils.TalismanQuantity + "张、丹器符阵等物资" + spoils.CraftResourceQuantity + "份。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				victorName + "收缴" + defeatedName + "底蕴",
				body,
				5,
				true,
				sectId: victor.SectId,
				cityId: victor.CapitalCityId,
				year: currentYear);
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				body, XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Sect, duration: 8f, color: "#C99652", delayFrames: 1);
		}

	
		private static string ResolveTerritoryStatus(XjSectArchiveRecord record, int territoryCityCount)
		{
			string status = record?.Status ?? XjSectStatus.SectRegime;
			if (string.Equals(status, XjSectStatus.Extinct, StringComparison.Ordinal)) return XjSectStatus.Extinct;
			// 1.0 冻结规则：有效宗门必须至少拥有一座真实宗门城市。
			// 不再保留无城宗门运行态；旧档 LandlessSect 在年度治理时也会直接退场。
			if (territoryCityCount <= 0)
			{
				return XjSectStatus.Extinct;
			}
			return string.Equals(status, XjSectStatus.LandlessSect, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(status)
				? XjSectStatus.SectRegime
				: status;
		}

		private static void RetireSectRecord(XjSectArchiveRecord record, int currentYear, string reason)
		{
			if (record == null || record.SectId <= 0L) return;
			long sectId = record.SectId;
			ClearActorMirrorsForSect(sectId);
			ClearCityZongMenForSect(record);
			GovernanceRemovalBuffer.Clear();
			foreach (KeyValuePair<long, XjCityFamilyGovernanceArchiveRecord> pair in GovernanceByCityId)
			{
				if (pair.Value != null && pair.Value.SectId == sectId) GovernanceRemovalBuffer.Add(pair.Key);
			}
			for (int i = 0; i < GovernanceRemovalBuffer.Count; i++)
			{
				if (!GovernanceByCityId.TryGetValue(GovernanceRemovalBuffer[i], out XjCityFamilyGovernanceArchiveRecord governance) || governance == null) continue;
				governance.SectId = 0L;
				governance.State = XjCityFamilyGovernanceState.Prominent;
				governance.ChallengerFamilyId = 0L;
				governance.ChallengeStartYear = 0;
				governance.ChallengeConsecutiveYears = 0;
			}
			FamilySeatRemovalBuffer.Clear();
			foreach (KeyValuePair<(long SectId, long FamilyId), XjSectFamilySeatArchiveRecord> pair in FamilySeats)
			{
				if (pair.Key.SectId == sectId) FamilySeatRemovalBuffer.Add(pair.Key);
			}
			for (int i = 0; i < FamilySeatRemovalBuffer.Count; i++) FamilySeats.Remove(FamilySeatRemovalBuffer[i]);
			XjSectFormationRegistry.RemoveForSect(sectId);
			XjSectLectureSystem.ForgetSect(sectId);
			ForgetSovereignRuntimeState(sectId);
			XjSectAuthorityStore.UnregisterSect(sectId);
			BySectId.Remove(sectId);
			RuntimeCityIdsBySectId.Remove(sectId);
		}

		private static void ClearActorMirrorsForSect(long sectId)
		{
			if (sectId <= 0L) return;
			// 成员索引由角色回调维护，灭宗不再为清理镜像扫描整个修士缓存。
			IReadOnlyList<long> indexedIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
			SectMemberMirrorBuffer.Clear();
			for (int i = 0; i < indexedIds.Count; i++) SectMemberMirrorBuffer.Add(indexedIds[i]);
			for (int i = 0; i < SectMemberMirrorBuffer.Count; i++)
			{
				if (!XjScheduler.ResolveActor(SectMemberMirrorBuffer[i], out Actor actor) || actor?.data == null) continue;
				if (ReadActorSectId(actor) == sectId) XjSectProjection.ClearActor(actor);
			}
			SectMemberMirrorBuffer.Clear();
		}

		private static void ClearCityZongMenForSect(XjSectArchiveRecord record)
		{
			if (record == null || record.SectId <= 0L || record.CityIds == null) return;
			// The retiring record already owns its city ids. Resolve those ids instead
			// of scanning every city in a large world to clear the mirror state.
			for (int i = 0; i < record.CityIds.Count; i++)
			{
				City city = ResolveCityById(record.CityIds[i]);
				if (city?.data == null || !XjSectCityData.HasZongMen(city) || XjSectCityData.GetZongMenId(city) != record.SectId) continue;
				XjSectCityData.Clear(city);
			}
		}


		private static void RecordTerritoryStatusEvent(XjSectArchiveRecord record, string previousStatus, string nextStatus, int currentYear)
		{
			if (record == null || currentYear <= 0) return;
			if (string.Equals(nextStatus, XjSectStatus.LandlessSect, StringComparison.Ordinal))
			{
				XjWorldHistoryStore.RecordDomainEvent(
					XjWorldHistoryCategory.Sect,
					(record.Name ?? "某宗") + "失地",
					"原有山门或分支已失，宗门暂成失地之宗，保留宗脉档案与宗主传承。",
					4,
					sectId: record.SectId,
					cityId: record.CapitalCityId,
					year: currentYear);
				return;
			}
			if (string.Equals(nextStatus, XjSectStatus.Extinct, StringComparison.Ordinal))
			{
				XjThreeBookWriter.RecordSectExtinct(record.SectId, record.Name, currentYear, FormatSectStatusForHistory(previousStatus));
				XjWorldHistoryStore.RecordDomainEvent(
					XjWorldHistoryCategory.Sect,
					(record.Name ?? "某宗") + "灭宗",
					"宗门已无任何有效山门或分支城市，依一城宗门规则立即灭宗，不保留无城运行态。",
					5,
					sectId: record.SectId,
					cityId: record.CapitalCityId,
					year: currentYear);
				return;
			}
			if (string.Equals(previousStatus, XjSectStatus.LandlessSect, StringComparison.Ordinal))
			{
				XjWorldHistoryStore.RecordDomainEvent(
					XjWorldHistoryCategory.Sect,
					(record.Name ?? "某宗") + "重立山门",
					"宗门重新拥有山门或分支，恢复宗门治理。",
					4,
					sectId: record.SectId,
					cityId: record.CapitalCityId,
					year: currentYear);
			}
		}
}

