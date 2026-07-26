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
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{	
		private const int BranchPeakCapacity = MaxSectPeaks - 1;
		private const int FirstBranchPeakId = XjZongMenCityData.FirstRegularPeakId;
		private const int LastBranchPeakId = FirstBranchPeakId + BranchPeakCapacity - 1;
		private const int PeakFallbackScanIntervalYears = 3;
		private static readonly List<Actor> PeakCandidateBuffer = new List<Actor>();
		private static readonly HashSet<long> PeakCandidateSeen = new HashSet<long>();
		private static readonly Dictionary<long, int> PeakFallbackScanYearBySect = new Dictionary<long, int>();

		internal static bool ReconcilePeaks(long sectId, int currentYear)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled || sectId <= 0L || !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			record.Peaks ??= new List<XjSectPeakArchiveRecord>();
			// 旧版档案以1号作为首座支峰，但城镇层1号长期保留给洞天峰，
			// 两套编号由此永久错位。先一次性把旧档支峰归一到2—13号，
			// 使档案、角色镜像、手动入峰和城镇弟子名单使用同一编号。
			bool changed = NormalizeArchivePeakIds(record);
			changed |= RemoveInvalidPeaks(record, currentYear);
			if (record.Peaks.Count < BranchPeakCapacity)
			{
				List<Actor> candidates = CollectPeakCandidates(record, currentYear);
				for (int i = 0; i < candidates.Count && record.Peaks.Count < BranchPeakCapacity; i++)
				{
					Actor actor = candidates[i];
					if (actor?.data == null || !actor.isAlive()) continue;
					long actorId = ((BaseSystemData)actor.data).id;
					if (actorId <= 0L || HasPeakMaster(record, actorId)) continue;
					int peakId = NextPeakId(record);
					if (peakId <= 0) break;
					XjSectPeakArchiveRecord peak = new XjSectPeakArchiveRecord
					{
						SectId = record.SectId,
						PeakId = peakId,
						PeakName = ResolvePeakName(record, peakId),
						PeakMasterActorId = actorId,
						PeakMasterName = SafeActorName(actor),
						FounderActorId = actorId,
						FoundedYear = Math.Max(0, currentYear),
						LastConfirmedYear = Math.Max(0, currentYear)
					};
					record.Peaks.Add(peak);
					SetActorPeakMirror(actor, peak);
					RecordPeakFoundedEvent(record, peak, actor, currentYear);
					changed = true;
				}
				record.Peaks.Sort((left, right) => left.PeakId.CompareTo(right.PeakId));
				ReleasePeakCandidateScratch();
			}
			// 宗门档案只保存峰位与峰主，弟子名单仍落在城镇持久化层。
			// 每次年度峰位对账后，把档案峰位投影到城镇并执行一次有界重排，
			// 修复旧档“主峰上百人、支峰只有峰主”的双源漂移。
			changed |= ReconcileCityPeakProjection(record, currentYear);
			if (!changed) return false;
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.History);
			return true;
		}


		private static bool NormalizeArchivePeakIds(XjSectArchiveRecord record)
		{
			if (record?.Peaks == null || record.Peaks.Count == 0) return false;
			HashSet<int> seen = new HashSet<int>();
			bool alreadyNormalized = true;
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (peak == null
					|| peak.PeakId < FirstBranchPeakId
					|| peak.PeakId > LastBranchPeakId
					|| !seen.Add(peak.PeakId))
				{
					alreadyNormalized = false;
				}
			}
			if (alreadyNormalized) return false;

			record.Peaks.Sort((left, right) =>
			{
				if (ReferenceEquals(left, right)) return 0;
				if (left == null) return 1;
				if (right == null) return -1;
				int byId = left.PeakId.CompareTo(right.PeakId);
				if (byId != 0) return byId;
				int byYear = left.FoundedYear.CompareTo(right.FoundedYear);
				return byYear != 0 ? byYear : left.PeakMasterActorId.CompareTo(right.PeakMasterActorId);
			});
			int nextId = FirstBranchPeakId;
			for (int i = 0; i < record.Peaks.Count && nextId <= LastBranchPeakId; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (peak == null) continue;
				peak.PeakId = nextId++;
				if (XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor master) && master?.data != null)
				{
					SetActorPeakMirror(master, peak);
				}
			}
			return true;
		}

		private static bool ReconcileCityPeakProjection(XjSectArchiveRecord record, int currentYear)
		{
			if (record == null || record.SectId <= 0L
				|| !XjZongMenCityData.TryResolveZongMenCity(record.SectId, out City city)
				|| city?.data == null
				|| !XjZongMenCityData.HasZongMen(city))
			{
				return false;
			}

			XjZongMenCityData.EnsureMainPeak(city);
			bool changed = false;
			HashSet<int> desiredRegularIds = new HashSet<int>();
			if (record.Peaks != null)
			{
				for (int i = 0; i < record.Peaks.Count; i++)
				{
					XjSectPeakArchiveRecord peak = record.Peaks[i];
					if (peak == null
						|| peak.PeakId < FirstBranchPeakId
						|| peak.PeakId > LastBranchPeakId
						|| peak.PeakMasterActorId <= 0L)
					{
						continue;
					}
					desiredRegularIds.Add(peak.PeakId);
				}
			}

			List<int> currentPeakIds = XjZongMenCityData.ReadPeakIds(city);
			for (int i = currentPeakIds.Count - 1; i >= 0; i--)
			{
				int peakId = currentPeakIds[i];
				if (peakId < XjZongMenCityData.FirstRegularPeakId || desiredRegularIds.Contains(peakId)) continue;
				changed |= XjZongMenCityData.RemovePeak(city, peakId);
			}

			currentPeakIds = XjZongMenCityData.ReadPeakIds(city);
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (peak == null || !desiredRegularIds.Contains(peak.PeakId)) continue;
				if (!currentPeakIds.Contains(peak.PeakId))
				{
					currentPeakIds.Add(peak.PeakId);
					changed = true;
				}

				string expectedName = string.IsNullOrWhiteSpace(peak.PeakName)
					? ResolvePeakName(record, peak.PeakId)
					: peak.PeakName.Trim();
				city.data.get(XjZongMenCityData.KeyPeakNamePrefix + peak.PeakId, out string currentName, string.Empty);
				if (!string.Equals(currentName ?? string.Empty, expectedName, StringComparison.Ordinal))
				{
					city.data.set(XjZongMenCityData.KeyPeakNamePrefix + peak.PeakId, expectedName);
					changed = true;
				}
				city.data.get(XjZongMenCityData.KeyPeakTypePrefix + peak.PeakId, out string currentType, string.Empty);
				if (!string.Equals(currentType, "CultivatorPeak", StringComparison.Ordinal))
				{
					city.data.set(XjZongMenCityData.KeyPeakTypePrefix + peak.PeakId, "CultivatorPeak");
					changed = true;
				}
				XjZongMenCityData.TryReadActorId(
					city,
					XjZongMenCityData.KeyPeakFengZhuPrefix + peak.PeakId,
					out long currentMasterId);
				if (currentMasterId != peak.PeakMasterActorId)
				{
					city.data.set(
						XjZongMenCityData.KeyPeakFengZhuPrefix + peak.PeakId,
						peak.PeakMasterActorId.ToString(CultureInfo.InvariantCulture));
					changed = true;
				}
				if (XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor master) && master?.data != null)
				{
					XjZongMenMembershipWriter.EnsureMember(city, master, currentYear, "ArchivePeakProjection");
					SetActorPeakMirror(master, peak);
				}
			}
			currentPeakIds.Sort();
			XjZongMenCityData.WritePeakIds(city, currentPeakIds);

			long beforeSignature = BuildCityPeakAssignmentSignature(city);
			bool hadMirrorDrift = HasCityPeakMirrorDrift(city, record.SectId);
			XjZongMenCityData.AssignRolesFromScratch(city, currentYear);
			long afterSignature = BuildCityPeakAssignmentSignature(city);
			return changed || hadMirrorDrift || beforeSignature != afterSignature;
		}

		private static long BuildCityPeakAssignmentSignature(City city)
		{
			if (city?.data == null) return 0L;
			long signature = 1469598103934665603L;
			List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
			peakIds.Sort();
			for (int i = 0; i < peakIds.Count; i++)
			{
				int peakId = peakIds[i];
				signature = unchecked((signature ^ peakId) * 1099511628211L);
				XjZongMenCityData.TryReadActorId(
					city,
					XjZongMenCityData.KeyPeakFengZhuPrefix + peakId,
					out long masterId);
				signature = unchecked((signature ^ masterId) * 1099511628211L);
				List<long> disciples = XjZongMenCityData.ReadIdList(
					city,
					XjZongMenCityData.KeyPeakDisciplePrefix + peakId);
				for (int j = 0; j < disciples.Count; j++)
				{
					signature = unchecked((signature ^ disciples[j]) * 1099511628211L);
				}
			}
			return signature;
		}

		private static bool HasCityPeakMirrorDrift(City city, long sectId)
		{
			if (city?.data == null || sectId <= 0L) return false;
			IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(sectId);
			List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
			long sovereignId = XjZongMenCityData.GetZongZhuId(city);
			for (int i = 0; i < actorIds.Count; i++)
			{
				long actorId = actorIds[i];
				if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
				int expectedPeakId = actorId == sovereignId
					? XjZongMenCityData.MainPeakId
					: XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeySupremeElders, actorId)
						? XjZongMenCityData.SupremePeakId
						: XjZongMenCityData.MainPeakId;
				if (expectedPeakId == XjZongMenCityData.MainPeakId && actorId != sovereignId)
				{
					for (int j = 0; j < peakIds.Count; j++)
					{
						int peakId = peakIds[j];
						if (XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId, actorId)
							|| (XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out long masterId)
								&& masterId == actorId))
						{
							expectedPeakId = peakId;
							break;
						}
					}
				}
				int storedPeakId = XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenPeakId, out int value) ? value : 0;
				if (storedPeakId != expectedPeakId) return true;
			}
			return false;
		}

	
		private static bool RemoveInvalidPeaks(XjSectArchiveRecord record, int currentYear)
		{
			bool changed = false;
			HashSet<int> usedPeakIds = new HashSet<int>();
			HashSet<long> usedMasterIds = new HashSet<long>();
			for (int i = record.Peaks.Count - 1; i >= 0; i--)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (peak == null || peak.PeakId < FirstBranchPeakId || peak.PeakId > LastBranchPeakId || !usedPeakIds.Add(peak.PeakId)
					|| peak.PeakMasterActorId <= 0L || !usedMasterIds.Add(peak.PeakMasterActorId)
					|| peak.PeakMasterActorId == record.SovereignActorId
					|| !XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor actor)
					|| actor?.data == null || !actor.isAlive()
					|| ReadActorSectId(actor) != record.SectId
					|| !CanKeepPeak(actor))
				{
					if (peak?.PeakMasterActorId > 0L && XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor oldActor) && oldActor?.data != null)
					{
						ClearActorPeakMirror(oldActor);
					}
					if (peak != null && currentYear > 0)
					{
						XjCenturyAnnalsStore.ObserveSectEvent(
							"SectPeakMasterLost",
							currentYear,
							record.SectId,
							record.Name,
							3,
							(record.Name ?? "某宗") + peak.PeakName + "峰主位失效，原峰主" + (peak.PeakMasterName ?? "未名") + "不再能持峰",
							peak.PeakMasterActorId,
							peak.PeakMasterName);
					}
					record.Peaks.RemoveAt(i);
					changed = true;
					continue;
				}
				string currentName = SafeActorName(actor);
				if (!string.Equals(peak.PeakMasterName, currentName, StringComparison.Ordinal))
				{
					peak.PeakMasterName = currentName;
					changed = true;
				}
				peak.LastConfirmedYear = Math.Max(peak.LastConfirmedYear, currentYear);
				SetActorPeakMirror(actor, peak);
			}
			return changed;
		}

		private static List<Actor> CollectPeakCandidates(XjSectArchiveRecord record, int currentYear)
		{
			List<Actor> result = PeakCandidateBuffer;
			HashSet<long> seen = PeakCandidateSeen;
			result.Clear();
			seen.Clear();
			IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(record.SectId);
			for (int i = 0; i < actorIds.Count; i++)
			{
				TryAddPeakCandidate(record, actorIds[i], seen, result);
			}

			// The city index is intentionally incremental. A newly admitted cultivator
			// can therefore be valid before its next city-observation pass. Only when
			// a sect still lacks peak masters do one bounded cache pass to fill the
			// vacancy; this avoids keeping empty peaks while preserving the hot path.
			int vacancies = Math.Max(0, BranchPeakCapacity - (record.Peaks?.Count ?? 0));
			if (result.Count < vacancies && TryBeginPeakFallbackScan(record.SectId, currentYear))
			{
				IReadOnlyList<long> allCultivatorIds = XjCultivatorCache.GetAllIds();
				for (int i = 0; i < allCultivatorIds.Count; i++)
				{
					TryAddPeakCandidate(record, allCultivatorIds[i], seen, result);
				}
			}
			result.Sort(ComparePeakCandidates);
			return result;
		}

		private static bool TryBeginPeakFallbackScan(long sectId, int currentYear)
		{
			if (sectId <= 0L) return false;
			if (PeakFallbackScanYearBySect.TryGetValue(sectId, out int lastYear)
				&& currentYear - lastYear < PeakFallbackScanIntervalYears)
			{
				return false;
			}

			PeakFallbackScanYearBySect[sectId] = currentYear;
			return true;
		}

		private static void ReleasePeakCandidateScratch()
		{
			PeakCandidateBuffer.Clear();
			PeakCandidateSeen.Clear();
		}

		internal static void ClearPeakRuntimeState()
		{
			ReleasePeakCandidateScratch();
			PeakFallbackScanYearBySect.Clear();
		}

		private static void TryAddPeakCandidate(XjSectArchiveRecord record, long actorId, HashSet<long> seen, List<Actor> result)
		{
			if (record == null || actorId <= 0L || seen == null || result == null || !seen.Add(actorId) || HasPeakMaster(record, actorId))
			{
				return;
			}

			if (actorId == record.SovereignActorId)
			{
				return;
			}
	
			if (!XjScheduler.ResolveActor(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| ReadActorSectId(actor) != record.SectId
				|| !CanKeepPeak(actor))
			{
				return;
			}
	
			// 峰位镜像还会被旧城镇宗门层用于弟子分配，不能据此判定角色已经是峰主。
			// 是否占据峰主位只以当前宗门档案中的 Peaks 为准；任命成功后会覆盖旧镜像。
			result.Add(actor);
		}

		private static int ComparePeakCandidates(Actor left, Actor right)
		{
			long leftId = left?.data == null ? long.MaxValue : ((BaseSystemData)left.data).id;
			long rightId = right?.data == null ? long.MaxValue : ((BaseSystemData)right.data).id;
			int leftRealmOrder = ReadRealmOrder(left);
			int rightRealmOrder = ReadRealmOrder(right);
			int byRealm = rightRealmOrder.CompareTo(leftRealmOrder);
			if (byRealm != 0) return byRealm;
			float leftZhenYuan = NormalizePeakMetric(ReadFloat(left, XjActorDataKeys.ZhenYuan));
			float rightZhenYuan = NormalizePeakMetric(ReadFloat(right, XjActorDataKeys.ZhenYuan));
			int byZhenYuan = rightZhenYuan.CompareTo(leftZhenYuan);
			return byZhenYuan != 0 ? byZhenYuan : leftId.CompareTo(rightId);
		}

		private static bool HasPeakMaster(XjSectArchiveRecord record, long actorId)
		{
			if (record?.Peaks == null || actorId <= 0L) return false;
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				if (record.Peaks[i]?.PeakMasterActorId == actorId) return true;
			}
			return false;
		}

		private static int NextPeakId(XjSectArchiveRecord record)
		{
			for (int id = FirstBranchPeakId; id <= LastBranchPeakId; id++)
			{
				bool used = false;
				for (int i = 0; i < record.Peaks.Count; i++)
				{
					if (record.Peaks[i]?.PeakId == id)
					{
						used = true;
						break;
					}
				}
				if (!used) return id;
			}
			return 0;
		}

		private static string ResolvePeakName(XjSectArchiveRecord record, int peakId)
		{
			int index = Math.Clamp(peakId - FirstBranchPeakId, 0, PeakNames.Length - 1);
			return PeakNames[index];
		}

		private static bool CanKeepPeak(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return false;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int order = XjRealmHelper.GetOrder(realmId);
			int ziFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
			int jinDanOrder = XjRealmHelper.GetOrder(XjRealmIds.JinDan);
			if (order >= ziFuOrder && order < jinDanOrder) return true;
			return order < ziFuOrder && IsStrictZhuJiLate(actor);
		}

		private static bool IsStrictZhuJiLate(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return false;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (!string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal)) return false;
			float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
			if (zhenYuan >= 24000f) return true;
			int xianJiCount = ReadInt(actor, XjActorDataKeys.XjXianJiCount);
			return string.Equals(XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0), "筑基后期", StringComparison.Ordinal);
		}

		private static float NormalizePeakMetric(float value)
		{
			return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
		}

		private static int ReadRealmOrder(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return -1;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			return XjRealmHelper.GetOrder(realmId);
		}

		private static string ResolvePeakFounderRealmDisplay(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return "修行有成";
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
			int xianJiCount = ReadInt(actor, XjActorDataKeys.XjXianJiCount);
			string display = XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0);
			return string.IsNullOrWhiteSpace(display) ? XjRealmHelper.GetDisplayName(realmId) : display;
		}

		private static long ReadActorSectId(Actor actor)
		{
			return ResolveActorSectId(actor);
		}

		private static float ReadFloat(Actor actor, string key)
		{
			return XjActorAccessor.TryGetFloat(actor, key, out float value) ? value : 0f;
		}

		private static int ReadInt(Actor actor, string key)
		{
			return XjActorAccessor.TryGetInt(actor, key, out int value) ? value : 0;
		}

		private static void SetActorPeakMirror(Actor actor, XjSectPeakArchiveRecord peak)
		{
			if (actor?.data == null || peak == null) return;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenPeakId, peak.PeakId);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenPeakName, peak.PeakName ?? string.Empty);
		}

		private static void ClearActorPeakMirror(Actor actor)
		{
			if (actor?.data == null) return;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZongMenPeakId, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenPeakName, string.Empty);
		}

		private static void RecordPeakFoundedEvent(XjSectArchiveRecord record, XjSectPeakArchiveRecord peak, Actor actor, int currentYear)
		{
			if (record == null || peak == null || actor?.data == null || currentYear <= 0) return;
			XuanJianVNext.Systems.Family.XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(peak.PeakMasterActorId, out long peakFamilyId);
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Sect,
				(record.Name ?? "某宗") + "新开" + peak.PeakName,
				SafeActorName(actor) + ResolvePeakFounderRealmDisplay(actor) + "，得宗门许立" + peak.PeakName + "，山门诸峰增至"
					+ Math.Min(MaxSectPeaks, record.Peaks.Count + (record.SovereignActorId > 0L ? 1 : 0)).ToString(CultureInfo.InvariantCulture) + "座。",
				3,
				actorId: peak.PeakMasterActorId,
				actorName: peak.PeakMasterName,
				sectId: record.SectId,
				familyId: peakFamilyId,
				cityId: record.CapitalCityId,
				year: currentYear,
				eventType: "SectPeakFounded",
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate));
			XjThreeBookWriter.RecordSectLeadership(
				record.SectId,
				record.Name,
				peak.PeakMasterActorId,
				peak.PeakMasterName,
				peak.PeakName + "峰主",
				currentYear,
				"sect|peak-master|" + record.SectId + "|" + peak.PeakId + "|" + peak.PeakMasterActorId + "|" + currentYear);
			XjCenturyAnnalsStore.ObserveSectEvent(
				"SectPeakFounded",
				currentYear,
				record.SectId,
				record.Name,
				3,
				(record.Name ?? "某宗") + "新开" + peak.PeakName + "，" + SafeActorName(actor) + "任峰主",
				peak.PeakMasterActorId,
				peak.PeakMasterName);
		}
}

