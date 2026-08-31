using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{	
		private const int BranchPeakCapacity = MaxSectPeaks - 1;
		private const int FirstBranchPeakId = XjSectPeakIds.FirstRegular;
		private const int LastBranchPeakId = FirstBranchPeakId + BranchPeakCapacity - 1;
		private static readonly List<Actor> PeakCandidateBuffer = new List<Actor>();
		private static readonly HashSet<long> PeakCandidateSeen = new HashSet<long>();

		internal static bool ReconcilePeaks(long sectId, int currentYear)
		{
			if (!XjWorldSchemaGuard.GameplayEnabled
				|| XjSectAuthorityStore.NeedsLegacyMigration
				|| sectId <= 0L
				|| !BySectId.TryGetValue(sectId, out XjSectArchiveRecord record)) return false;
			record.Peaks ??= new List<XjSectPeakArchiveRecord>();
			// 旧版档案以1号作为首座支峰，但1号现统一保留给洞天峰。
			// 归一化只修改宗门权威档案与成员权威记录，角色/城镇字段由投影器稍后单向刷新。
			bool changed = NormalizeArchivePeakIds(record, currentYear);
			changed |= RemoveInvalidPeaks(record, currentYear);
			if (record.Peaks.Count < BranchPeakCapacity)
			{
				List<Actor> candidates = CollectPeakCandidates(record);
				for (int i = 0; i < candidates.Count && record.Peaks.Count < BranchPeakCapacity; i++)
				{
					Actor actor = candidates[i];
					if (actor?.data == null || !actor.isAlive()) continue;
					long actorId = ((BaseSystemData)actor.data).id;
					if (actorId <= 0L || HasPeakMaster(record, actorId)) continue;
					int peakId = NextPeakId(record);
					if (peakId <= 0) break;
					string peakName = ResolvePeakName(record, peakId);
					if (!XjSectCommands.AssignPeakMaster(record.SectId, peakId, actor, currentYear, peakName)) continue;
					XjSectPeakArchiveRecord peak = FindPeak(record, peakId);
					if (peak != null) RecordPeakFoundedEvent(record, peak, actor, currentYear);
					changed = true;
				}
				ReleasePeakCandidateScratch();
			}
			if (!changed) return false;
			XjSectAuthorityStore.MarkProjectionDirty(record.SectId);
			XjWorldArchiveSystem.MarkChanged();
			XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
				XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.History);
			return true;
		}


		private static bool NormalizeArchivePeakIds(XjSectArchiveRecord record, int currentYear)
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

			Dictionary<int, int> firstIdMapping = new Dictionary<int, int>();
			int nextId = FirstBranchPeakId;
			for (int i = 0; i < record.Peaks.Count && nextId <= LastBranchPeakId; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				if (peak == null) continue;
				int previousId = peak.PeakId;
				int normalizedId = nextId++;
				if (!firstIdMapping.ContainsKey(previousId)) firstIdMapping[previousId] = normalizedId;
				peak.SchemaVersion = XjSectDomainSchema.CurrentVersion;
				peak.SectId = record.SectId;
				peak.PeakId = normalizedId;
				peak.LastConfirmedYear = Math.Max(peak.LastConfirmedYear, currentYear);
			}
			record.Peaks.RemoveAll(peak => peak == null || peak.PeakId < FirstBranchPeakId || peak.PeakId > LastBranchPeakId);
			if (record.Peaks.Count > BranchPeakCapacity) record.Peaks.RemoveRange(BranchPeakCapacity, record.Peaks.Count - BranchPeakCapacity);

			IReadOnlyList<long> memberIds = XjSectAuthorityStore.GetActorIdsForSect(record.SectId);
			for (int i = 0; i < memberIds.Count; i++)
			{
				if (!XjSectAuthorityStore.TryGetMember(memberIds[i], out XjSectMemberArchiveRecord member)
					|| member.PeakId < FirstBranchPeakId
					|| !firstIdMapping.TryGetValue(member.PeakId, out int mappedPeakId)
					|| mappedPeakId == member.PeakId) continue;
				if (!XjScheduler.ResolveActor(member.ActorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
				string role = XjSectMemberRole.Normalize(member.Role);
				if (role == XjSectMemberRole.PeakMaster)
				{
					XjSectPeakArchiveRecord mappedPeak = FindPeak(record, mappedPeakId);
					XjSectCommands.AssignPeakMaster(record.SectId, mappedPeakId, actor, currentYear, mappedPeak?.PeakName);
				}
				else if (role == XjSectMemberRole.Disciple)
				{
					XjSectCommands.AssignDisciple(record.SectId, mappedPeakId, actor, currentYear);
				}
			}
			return true;
		}


	
		private static bool RemoveInvalidPeaks(XjSectArchiveRecord record, int currentYear)
		{
			bool changed = record.Peaks.RemoveAll(peak => peak == null) > 0;
			HashSet<int> usedPeakIds = new HashSet<int>();
			HashSet<long> usedMasterIds = new HashSet<long>();
			List<XjSectPeakArchiveRecord> invalid = new List<XjSectPeakArchiveRecord>();
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = record.Peaks[i];
				Actor actor = null;
				bool actorResolved = peak.PeakMasterActorId > 0L
					&& XjScheduler.ResolveActor(peak.PeakMasterActorId, out actor)
					&& actor?.data != null
					&& actor.isAlive();
				bool invalidPeak = peak.PeakId < FirstBranchPeakId
					|| peak.PeakId > LastBranchPeakId
					|| !usedPeakIds.Add(peak.PeakId)
					|| peak.PeakMasterActorId <= 0L
					|| !usedMasterIds.Add(peak.PeakMasterActorId)
					|| peak.PeakMasterActorId == record.SovereignActorId
					|| !actorResolved
					|| ReadActorSectId(actor) != record.SectId
					|| !CanRetainPeak(actor);
				if (invalidPeak)
				{
					invalid.Add(peak);
					continue;
				}

				string currentName = SafeActorName(actor);
				if (!string.Equals(peak.PeakMasterName, currentName, StringComparison.Ordinal))
				{
					peak.PeakMasterName = currentName;
					changed = true;
				}
				peak.LastConfirmedYear = Math.Max(peak.LastConfirmedYear, currentYear);
				if (!XjSectAuthorityStore.TryGetMember(peak.PeakMasterActorId, out XjSectMemberArchiveRecord member)
					|| member.SectId != record.SectId
					|| XjSectMemberRole.Normalize(member.Role) != XjSectMemberRole.PeakMaster
					|| member.PeakId != peak.PeakId)
				{
					changed |= XjSectCommands.AssignPeakMaster(record.SectId, peak.PeakId, actor, currentYear, peak.PeakName);
				}
			}

			for (int i = 0; i < invalid.Count; i++)
			{
				XjSectPeakArchiveRecord peak = invalid[i];
				if (currentYear > 0)
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
				changed |= XjSectCommands.RemovePeak(record.SectId, peak.PeakId, currentYear);
			}
			return changed;
		}

		private static List<Actor> CollectPeakCandidates(XjSectArchiveRecord record)
		{
			List<Actor> result = PeakCandidateBuffer;
			HashSet<long> seen = PeakCandidateSeen;
			result.Clear();
			seen.Clear();
			IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(record.SectId);
			for (int i = 0; i < actorIds.Count; i++)
			{
				TryAddPeakCandidate(record, actorIds[i], seen, result);
			}
			result.Sort(ComparePeakCandidates);
			return result;
		}

		private static void ReleasePeakCandidateScratch()
		{
			PeakCandidateBuffer.Clear();
			PeakCandidateSeen.Clear();
		}

		internal static void ClearPeakRuntimeState()
		{
			ReleasePeakCandidateScratch();
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
				|| !CanFoundPeak(actor))
			{
				return;
			}
	
			// 候选只来自宗门成员权威索引；峰主占位只看宗门档案，不读取角色或城镇镜像。
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

		private static bool CanRetainPeak(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return false;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			int order = XjRealmHelper.GetOrder(realmId);
			int ziFuOrder = XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
			if (order >= ziFuOrder) return true;
			return IsStrictZhuJiLate(actor);
		}

		private static bool CanFoundPeak(Actor actor)
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

		private static XjSectPeakArchiveRecord FindPeak(XjSectArchiveRecord record, int peakId)
		{
			if (record?.Peaks == null || peakId < FirstBranchPeakId) return null;
			for (int i = 0; i < record.Peaks.Count; i++)
			{
				if (record.Peaks[i]?.PeakId == peakId) return record.Peaks[i];
			}
			return null;
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

