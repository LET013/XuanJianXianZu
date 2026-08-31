using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门成员、职阶、峰主、宗主与城镇归属的唯一写入口。
/// 任何调用方都不得直接写 actor/city 宗门镜像。
/// </summary>
internal static class XjSectCommands
{
	internal static bool TryResolveSect(City city, out XjSectArchiveRecord sect)
	{
		return XjSectRepository.TryGetByCity(city, out sect) && sect?.SectId > 0L;
	}

	internal static bool EnrollMember(long sectId, Actor actor, int currentYear, string role = XjSectMemberRole.Member, int peakId = 0)
	{
		if (!IsValidActor(actor) || XjCultivationPathRules.IsShi(actor)
			|| !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		long previousSectId = 0L;
		bool previousLeadershipChanged = false;
		bool membershipChanged = true;
		int joinYear = Math.Max(0, currentYear);
		string resolvedRole = XjSectMemberRole.Normalize(role);
		int resolvedPeakId = peakId;
		if (XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord previous))
		{
			membershipChanged = previous.SectId != sectId;
			if (!membershipChanged)
			{
				if (previous.JoinYear > 0) joinYear = previous.JoinYear;
				// “确保已入宗”不得覆盖已经存在的宗主、峰主、洞天驻修或弟子身份。
				if (string.Equals(resolvedRole, XjSectMemberRole.Member, StringComparison.Ordinal)
					&& !string.Equals(previous.Role, XjSectMemberRole.Member, StringComparison.Ordinal))
				{
					resolvedRole = previous.Role;
					resolvedPeakId = previous.PeakId;
				}
			}
			else
			{
				previousSectId = previous.SectId;
				previousLeadershipChanged = ClearAuthorityPositions(previous.SectId, actorId, includeSovereign: true, currentYear);
			}
		}

		// 金丹/真君/道胎不是普通弟子。已有宗主、峰主身份原位保留；
		// 只有没有领导席位的高境修士，才归入宗门洞天。
		if (IsDongTianRealm(actor)
			&& (string.Equals(resolvedRole, XjSectMemberRole.Member, StringComparison.Ordinal)
				|| string.Equals(resolvedRole, XjSectMemberRole.Disciple, StringComparison.Ordinal)))
		{
			resolvedRole = XjSectMemberRole.SupremeElder;
			resolvedPeakId = 1;
		}

		bool changed = XjSectAuthorityStore.UpsertMember(
			actorId,
			sectId,
			joinYear,
			resolvedRole,
			resolvedPeakId,
			currentYear,
			force: true);
		if (previousLeadershipChanged && previousSectId > 0L)
		{
			Commit(previousSectId);
		}
		if (changed)
		{
			Commit(sectId);
			if (membershipChanged)
			{
			}
		}
		XjSectProjection.ProjectActor(actorId);
		return changed;
	}

	internal static bool RemoveMember(long sectId, long actorId, int currentYear)
	{
		if (sectId <= 0L || actorId <= 0L || !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member) || member.SectId != sectId)
		{
			return false;
		}
		bool leadershipChanged = ClearAuthorityPositions(sectId, actorId, includeSovereign: true, currentYear);
		bool changed = XjSectAuthorityStore.RemoveMember(actorId, out long previousSectId);
		if (changed)
		{
			Commit(previousSectId);
			XjSectProjection.ClearActor(actorId);
		}
		return changed;
	}

	/// <summary>
	/// Lifecycle reconciliation path for an actor that is already dead/unavailable.
	/// It removes current authority state and vacant leadership slots without emitting
	/// a delayed gameplay event. Normal voluntary membership changes still use RemoveMember.
	/// </summary>
	internal static bool RemoveUnavailableMember(long actorId, int currentYear)
	{
		if (actorId <= 0L || !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			|| member == null || member.SectId <= 0L)
		{
			return false;
		}
		long sectId = member.SectId;
		bool leadershipChanged = ClearAuthorityPositions(sectId, actorId, includeSovereign: true, currentYear);
		bool membershipChanged = XjSectAuthorityStore.RemoveMember(actorId, out _);
		if (!leadershipChanged && !membershipChanged) return false;
		Commit(sectId);
		XjSectProjection.ClearActor(actorId);
		return true;
	}

	internal static bool RebindMemberAfterExternalReplacement(long oldActorId, Actor target, int currentYear)
	{
		if (oldActorId <= 0L || !IsValidActor(target) || XjCultivationPathRules.IsShi(target)
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(target)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(target)
			|| !XjSectAuthorityStore.TryGetMember(oldActorId, out XjSectMemberArchiveRecord source)
			|| source == null || source.SectId <= 0L
			|| !XjSectRepository.TryGetBySectId(source.SectId, out XjSectArchiveRecord sect) || sect == null)
		{
			return false;
		}

		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId) return false;
		bool wasSovereign = sect.SovereignActorId == oldActorId;
		List<int> ledPeakIds = null;
		if (sect.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak?.PeakMasterActorId != oldActorId) continue;
				ledPeakIds ??= new List<int>();
				ledPeakIds.Add(peak.PeakId);
			}
		}

		XjSectAuthorityStore.RemoveMember(oldActorId, out _);
		bool changed = XjSectAuthorityStore.UpsertMember(
			newActorId, source.SectId, source.JoinYear, source.Role, source.PeakId, currentYear, force: true);
		if (wasSovereign) sect.SovereignActorId = newActorId;
		if (ledPeakIds != null && sect.Peaks != null)
		{
			string targetName = XjStringHelper.ActorName(target, source.Role);
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak == null || !ledPeakIds.Contains(peak.PeakId)) continue;
				peak.PeakMasterActorId = newActorId;
				peak.PeakMasterName = targetName;
				peak.LastConfirmedYear = Math.Max(0, currentYear);
			}
		}
		Commit(source.SectId);
		XjSectProjection.ProjectActor(newActorId);
		return changed || wasSovereign || (ledPeakIds?.Count ?? 0) > 0;
	}

	internal static bool AssignDisciple(long sectId, int peakId, Actor actor, int currentYear)
	{
		if (!IsValidActor(actor) || XjCultivationPathRules.IsShi(actor)
			|| !IsPeakAvailable(sectId, peakId)) return false;
		if (IsDongTianRealm(actor))
		{
			return AssignSupremeElder(sectId, actor, currentYear);
		}
		long actorId = ((BaseSystemData)actor.data).id;
		EnrollMember(sectId, actor, currentYear);
		if (!XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member) || member.SectId != sectId) return false;
		if (IsSovereign(sectId, actorId)) return false;
		bool leadershipChanged = ClearAuthorityPositions(sectId, actorId, includeSovereign: false, currentYear);
		bool changed = leadershipChanged;
		changed |= XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.Disciple, peakId, currentYear);
		if (changed)
		{
			Commit(sectId);
		}
		XjSectProjection.ProjectActor(actorId);
		return changed;
	}

	internal static bool AssignSupremeElder(long sectId, Actor actor, int currentYear)
	{
		if (!IsValidActor(actor) || XjCultivationPathRules.IsShi(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		bool enrolledChanged = EnrollMember(sectId, actor, currentYear);
		if (!XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member) || member.SectId != sectId) return false;
		// 高境晋升不得反过来把宗主或峰主下放到洞天。
		if (IsSovereign(sectId, actorId)
			|| string.Equals(XjSectMemberRole.Normalize(member.Role), XjSectMemberRole.PeakMaster, StringComparison.Ordinal)) return false;
		bool leadershipChanged = ClearAuthorityPositions(sectId, actorId, includeSovereign: false, currentYear);
		bool changed = enrolledChanged || leadershipChanged;
		changed |= XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.SupremeElder, 1, currentYear);
		if (changed)
		{
			Commit(sectId);
		}
		XjSectProjection.ProjectActor(actorId);
		return changed;
	}

	internal static bool AssignPeakMaster(long sectId, int peakId, Actor actor, int currentYear, string peakName = null)
	{
		if (!IsValidActor(actor) || XjCultivationPathRules.IsShi(actor)
			|| peakId < 2 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		EnrollMember(sectId, actor, currentYear);
		if (!XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member) || member.SectId != sectId || IsSovereign(sectId, actorId)) return false;

		sect.Peaks ??= new List<XjSectPeakArchiveRecord>();
		XjSectPeakArchiveRecord targetPeak = null;
		long replacedActorId = 0L;
		bool changed = false;
		for (int i = sect.Peaks.Count - 1; i >= 0; i--)
		{
			XjSectPeakArchiveRecord item = sect.Peaks[i];
			if (item == null)
			{
				sect.Peaks.RemoveAt(i);
				changed = true;
				continue;
			}
			if (item.PeakId == peakId)
			{
				targetPeak = item;
				replacedActorId = item.PeakMasterActorId;
				continue;
			}
			if (item.PeakMasterActorId != actorId) continue;

			// 峰位属于宗门，不属于峰主。调任时只清空旧峰主，绝不删除旧峰。
			item.PeakMasterActorId = 0L;
			item.PeakMasterName = string.Empty;
			item.LastConfirmedYear = Math.Max(0, currentYear);
			changed = true;
		}

		string nextName = actor.getName() ?? string.Empty;
		string nextPeakName = string.IsNullOrWhiteSpace(peakName) ? ResolvePeakName(sect, peakId) : peakName.Trim();
		if (targetPeak == null)
		{
			targetPeak = new XjSectPeakArchiveRecord
			{
				SchemaVersion = XjSectDomainSchema.CurrentVersion,
				SectId = sectId,
				PeakId = peakId,
				PeakName = nextPeakName,
				PeakMasterActorId = actorId,
				PeakMasterName = nextName,
				FounderActorId = actorId,
				FoundedYear = Math.Max(0, currentYear),
				LastConfirmedYear = Math.Max(0, currentYear)
			};
			sect.Peaks.Add(targetPeak);
			sect.Peaks.Sort((left, right) => (left?.PeakId ?? int.MaxValue).CompareTo(right?.PeakId ?? int.MaxValue));
			changed = true;
		}
		else
		{
			changed |= targetPeak.PeakMasterActorId != actorId
				|| !string.Equals(targetPeak.PeakMasterName, nextName, StringComparison.Ordinal)
				|| !string.Equals(targetPeak.PeakName, nextPeakName, StringComparison.Ordinal);
			targetPeak.SchemaVersion = XjSectDomainSchema.CurrentVersion;
			targetPeak.SectId = sectId;
			targetPeak.PeakMasterActorId = actorId;
			targetPeak.PeakMasterName = nextName;
			targetPeak.PeakName = nextPeakName;
			targetPeak.LastConfirmedYear = Math.Max(0, currentYear);
		}

		if (replacedActorId > 0L && replacedActorId != actorId)
		{
			changed |= SetFallbackRole(sectId, replacedActorId, currentYear);
			XjSectProjection.ProjectActor(replacedActorId);
		}
		changed |= XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.PeakMaster, peakId, currentYear);
		if (changed)
		{
			Commit(sectId);
		}
		XjSectProjection.ProjectActor(actorId);
		return changed;
	}

	internal static bool ClearPeakMaster(long sectId, int peakId, int currentYear)
	{
		if (sectId <= 0L || peakId < 2 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect?.Peaks == null) return false;
		for (int i = sect.Peaks.Count - 1; i >= 0; i--)
		{
			XjSectPeakArchiveRecord peak = sect.Peaks[i];
			if (peak == null || peak.PeakId != peakId) continue;
			long actorId = peak.PeakMasterActorId;
			if (actorId <= 0L) return false;
			peak.PeakMasterActorId = 0L;
			peak.PeakMasterName = string.Empty;
			peak.LastConfirmedYear = Math.Max(0, currentYear);
			SetFallbackRole(sectId, actorId, currentYear);
			Commit(sectId);
			XjSectProjection.ProjectActor(actorId);
			return true;
		}
		return false;
	}

	internal static bool ChangeSovereign(long sectId, Actor actor, int currentYear, bool founding)
	{
		if (!IsValidActor(actor) || XjCultivationPathRules.IsShi(actor)
			|| !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		EnrollMember(sectId, actor, currentYear);
		long previousId = sect.SovereignActorId;
		bool changed = previousId != actorId;
		if (!changed)
		{
			XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.Sovereign, 0, currentYear);
			XjSectProjection.ProjectActor(actorId);
			return false;
		}
		ClearAuthorityPositions(sectId, actorId, includeSovereign: false, currentYear);
		sect.SovereignActorId = actorId;
		sect.SovereignGeneration = founding ? 1 : Math.Max(1, sect.SovereignGeneration) + 1;
		XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.Sovereign, 0, currentYear);
		if (previousId > 0L && previousId != actorId)
		{
			SetFallbackRole(sectId, previousId, currentYear);
			XjSectProjection.ProjectActor(previousId);
		}
		Commit(sectId);
		XjSectProjection.ProjectActor(actorId);
		return true;
	}

	internal static void RemoveFromRoles(long sectId, long actorId, int currentYear, bool includeSovereign)
	{
		if (sectId <= 0L || actorId <= 0L) return;
		bool changed = ClearAuthorityPositions(sectId, actorId, includeSovereign, currentYear);
		changed |= SetFallbackRole(sectId, actorId, currentYear);
		if (changed)
		{
			Commit(sectId);
		}
		XjSectProjection.ProjectActor(actorId);
	}


	internal static bool SetSovereignGeneration(long sectId, int generation)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		int next = Math.Max(1, generation);
		if (sect.SovereignGeneration == next) return false;
		sect.SovereignGeneration = next;
		Commit(sectId);
		return true;
	}

	internal static bool SetLastRecruitYear(long sectId, int year)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		if (sect.LastRecruitYear == year) return false;
		sect.LastRecruitYear = year;
		Commit(sectId);
		return true;
	}

	internal static bool ClearSovereign(long sectId, int currentYear)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null || sect.SovereignActorId <= 0L) return false;
		long previousId = sect.SovereignActorId;
		sect.SovereignActorId = 0L;
		SetFallbackRole(sectId, previousId, currentYear);
		Commit(sectId);
		XjSectProjection.ProjectActor(previousId);
		return true;
	}

	internal static int CreatePeak(long sectId, string name, int currentYear)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return -1;
		sect.Peaks ??= new List<XjSectPeakArchiveRecord>();
		int nextId = 2;
		for (int i = 0; i < sect.Peaks.Count; i++) if (sect.Peaks[i] != null) nextId = Math.Max(nextId, sect.Peaks[i].PeakId + 1);
		sect.Peaks.Add(new XjSectPeakArchiveRecord
		{
			SchemaVersion = XjSectDomainSchema.CurrentVersion,
			SectId = sectId,
			PeakId = nextId,
			PeakName = string.IsNullOrWhiteSpace(name) ? "第" + nextId + "峰" : name.Trim(),
			FoundedYear = Math.Max(0, currentYear),
			LastConfirmedYear = Math.Max(0, currentYear)
		});
		sect.Peaks.Sort((left, right) => (left?.PeakId ?? int.MaxValue).CompareTo(right?.PeakId ?? int.MaxValue));
		Commit(sectId);
		return nextId;
	}

	internal static bool RemovePeak(long sectId, int peakId, int currentYear)
	{
		if (peakId < 2 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect?.Peaks == null) return false;
		bool changed = false;
		HashSet<long> affectedActors = new HashSet<long>();
		for (int i = sect.Peaks.Count - 1; i >= 0; i--)
		{
			XjSectPeakArchiveRecord peak = sect.Peaks[i];
			if (peak == null || peak.PeakId != peakId) continue;
			if (peak.PeakMasterActorId > 0L) affectedActors.Add(peak.PeakMasterActorId);
			sect.Peaks.RemoveAt(i);
			changed = true;
		}
		IReadOnlyList<long> members = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		for (int i = 0; i < members.Count; i++)
		{
			if (XjSectAuthorityStore.TryGetMember(members[i], out XjSectMemberArchiveRecord member) && member.PeakId == peakId)
			{
				affectedActors.Add(member.ActorId);
				changed |= SetFallbackRole(sectId, member.ActorId, currentYear);
			}
		}
		if (changed)
		{
			Commit(sectId);
			foreach (long actorId in affectedActors) XjSectProjection.ProjectActor(actorId);
		}
		return changed;
	}

	internal static bool IsPeakAvailable(long sectId, int peakId)
	{
		if (peakId == 0 || peakId == 1) return true;
		if (sectId <= 0L || peakId < 2 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect?.Peaks == null) return false;
		for (int i = 0; i < sect.Peaks.Count; i++) if (sect.Peaks[i]?.PeakId == peakId) return true;
		return false;
	}

	internal static void RefreshTerritoryIndex(long sectId)
	{
		if (sectId <= 0L || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return;
		XjSectAuthorityStore.RegisterSectCities(sect);
		Commit(sectId);
	}

	private static bool ClearAuthorityPositions(long sectId, long actorId, bool includeSovereign, int currentYear)
	{
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return false;
		bool changed = false;
		if (includeSovereign && sect.SovereignActorId == actorId)
		{
			sect.SovereignActorId = 0L;
			changed = true;
		}
		if (sect.Peaks != null)
		{
			for (int i = sect.Peaks.Count - 1; i >= 0; i--)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[i];
				if (peak?.PeakMasterActorId != actorId) continue;
				peak.PeakMasterActorId = 0L;
				peak.PeakMasterName = string.Empty;
				peak.LastConfirmedYear = Math.Max(0, currentYear);
				changed = true;
			}
		}
		return changed;
	}


	private static bool SetFallbackRole(long sectId, long actorId, int currentYear)
	{
		if (actorId <= 0L) return false;
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& IsValidActor(actor)
			&& IsDongTianRealm(actor))
		{
			return XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.SupremeElder, 1, currentYear);
		}
		return XjSectAuthorityStore.SetMemberRole(actorId, XjSectMemberRole.Member, 0, currentYear);
	}

	private static bool IsDongTianRealm(Actor actor)
	{
		if (!IsValidActor(actor)) return false;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return true;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
	}

	private static bool IsSovereign(long sectId, long actorId)
	{
		return sectId > 0L && actorId > 0L
			&& XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect)
			&& sect?.SovereignActorId == actorId;
	}

	private static bool IsValidActor(Actor actor)
	{
		return actor?.data != null && actor.isAlive() && ((BaseSystemData)actor.data).id > 0L;
	}

	private static string ResolvePeakName(XjSectArchiveRecord sect, int peakId)
	{
		if (sect?.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				if (sect.Peaks[i]?.PeakId == peakId && !string.IsNullOrWhiteSpace(sect.Peaks[i].PeakName)) return sect.Peaks[i].PeakName.Trim();
			}
		}
		return "第" + peakId + "峰";
	}

	private static void Commit(long sectId)
	{
		XjSectAuthorityStore.MarkProjectionDirty(sectId);
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.World | XjCodexDirtyFlags.History);
	}
}
