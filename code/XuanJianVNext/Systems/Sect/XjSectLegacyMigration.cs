using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 仅在首次载入旧归档时读取 actor/city 宗门镜像，并一次性导入新的成员权威表。
/// 迁移完成后正常运行不再从镜像反推业务状态。
/// </summary>
internal static class XjSectLegacyMigration
{
	private static bool _active;
	private static int _year;

	internal static bool IsActive => _active;

	internal static void BeginAfterLoad(int currentYear)
	{
		_active = XjSectAuthorityStore.NeedsLegacyMigration;
		_year = Math.Max(0, currentYear);
		if (!_active)
		{
			XjSectProjection.ScheduleAll();
			return;
		}
		ImportSectMirrors();
	}

	internal static void ObserveActor(Actor actor)
	{
		if (!_active || actor?.data == null || !actor.isAlive()) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (XjCultivationPathRules.IsShi(actor))
		{
			if (XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord existing)
				&& existing?.SectId > 0L)
			{
				XjSectCommands.RemoveMember(existing.SectId, actorId, _year);
			}
			if (TryReadLegacySectId(actor, out long legacyShiSectId))
			{
				XjSectCommands.RemoveFromRoles(legacyShiSectId, actorId, _year, includeSovereign: true);
			}
			XjSectProjection.ClearActor(actorId);
			return;
		}
		if (!XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long sectId) || sectId <= 0L)
		{
			if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacySectId) || legacySectId <= 0) return;
			sectId = legacySectId;
		}
		if (!XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenRole, out string role);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenPeakId, out int peakId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenJoinYear, out int joinYear);
		if (sect.SovereignActorId == actorId) role = XjSectMemberRole.Sovereign;
		else if (TryResolvePeakMaster(sect, actorId, out int authoritativePeakId))
		{
			role = XjSectMemberRole.PeakMaster;
			peakId = authoritativePeakId;
		}
		UpsertLegacyMemberIfAllowed(actorId, sectId, joinYear > 0 ? joinYear : _year, role, peakId, _year, force: false);
	}

	internal static void CompleteAfterBootstrap()
	{
		if (_active)
		{
			EnsureAuthorityPositions();
			XjSectAuthorityStore.CompleteLegacyMigration();
			XjWorldArchiveSystem.MarkChanged();
		}
		_active = false;
		_year = 0;
		XjSectProjection.ScheduleAll();
	}

	internal static void Clear()
	{
		_active = false;
		_year = 0;
	}

	private static void ImportSectMirrors()
	{
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect?.SectId <= 0L) continue;
			if (sect.SovereignActorId > 0L) UpsertLegacyMemberIfAllowed(sect.SovereignActorId, sect.SectId, sect.FoundingYear, XjSectMemberRole.Sovereign, 0, _year, force: true);
			if (sect.Peaks != null)
			{
				for (int p = 0; p < sect.Peaks.Count; p++)
				{
					XjSectPeakArchiveRecord peak = sect.Peaks[p];
					if (peak?.PeakMasterActorId > 0L) UpsertLegacyMemberIfAllowed(peak.PeakMasterActorId, sect.SectId, peak.FoundedYear, XjSectMemberRole.PeakMaster, peak.PeakId, _year, force: true);
				}
			}
			if (!XjSectOwnership.TryResolvePrimaryCity(sect, out City city) || city?.data == null) continue;
			XjSectCityData.BackfillFounderHistory(city);
			List<long> memberIds = XjSectCityData.ReadLegacyIdList(city, XjSectCityData.KeyMemberIds);
			for (int m = 0; m < memberIds.Count; m++) UpsertLegacyMemberIfAllowed(memberIds[m], sect.SectId, sect.FoundingYear, XjSectMemberRole.Member, 0, _year, force: false);
			List<long> elders = XjSectCityData.ReadLegacyIdList(city, XjSectCityData.KeySupremeElders);
			for (int e = 0; e < elders.Count; e++) UpsertLegacyMemberIfAllowed(elders[e], sect.SectId, sect.FoundingYear, XjSectMemberRole.SupremeElder, 1, _year, force: false);
			List<int> legacyPeakIds = ReadLegacyPeakIds(city);
			for (int p = 0; p < legacyPeakIds.Count; p++)
			{
				int peakId = legacyPeakIds[p];
				if (peakId >= 2 && XjSectCityData.TryReadLegacyActorId(city, XjSectCityData.KeyPeakFengZhuPrefix + peakId, out long masterId))
				{
					UpsertLegacyMemberIfAllowed(masterId, sect.SectId, sect.FoundingYear, XjSectMemberRole.PeakMaster, peakId, _year, force: false);
				}
				List<long> disciples = XjSectCityData.ReadLegacyIdList(city, XjSectCityData.KeyPeakDisciplePrefix + peakId);
				for (int d = 0; d < disciples.Count; d++) UpsertLegacyMemberIfAllowed(disciples[d], sect.SectId, sect.FoundingYear, XjSectMemberRole.Disciple, peakId, _year, force: false);
			}
		}
	}

	private static bool UpsertLegacyMemberIfAllowed(long actorId, long sectId, int joinYear,
		string role, int peakId, int currentYear, bool force)
	{
		if (actorId <= 0L || sectId <= 0L) return false;
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && actor?.data != null)
		{
			if (XjCultivationPathRules.IsShi(actor)) return false;
			string normalizedRole = XjSectMemberRole.Normalize(role);
			if (normalizedRole == XjSectMemberRole.Member || normalizedRole == XjSectMemberRole.Disciple)
			{
				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				if (XjDaoTaiSpellScale.IsDaoTaiActor(actor) || XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
				{
					role = XjSectMemberRole.SupremeElder;
					peakId = 1;
				}
			}
		}
		return XjSectAuthorityStore.UpsertMember(actorId, sectId, joinYear, role, peakId, currentYear, force);
	}

	private static bool TryReadLegacySectId(Actor actor, out long sectId)
	{
		sectId = 0L;
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out sectId) && sectId > 0L) return true;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacyId) && legacyId > 0)
		{
			sectId = legacyId;
			return true;
		}
		sectId = 0L;
		return false;
	}

	private static List<int> ReadLegacyPeakIds(City city)
	{
		List<int> result = new List<int>();
		if (city?.data == null) return result;
		city.data.get(XjSectCityData.KeyPeakIds, out string raw, string.Empty);
		if (string.IsNullOrWhiteSpace(raw)) return result;
		string[] parts = raw.Split('|');
		for (int i = 0; i < parts.Length; i++) if (int.TryParse(parts[i], out int id) && id >= 0 && !result.Contains(id)) result.Add(id);
		result.Sort();
		return result;
	}

	private static void EnsureAuthorityPositions()
	{
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect?.SectId <= 0L) continue;
			if (IsShiActorId(sect.SovereignActorId))
			{
				long forbiddenId = sect.SovereignActorId;
				sect.SovereignActorId = 0L;
				XjSectAuthorityStore.RemoveMember(forbiddenId, out _);
				XjSectProjection.ClearActor(forbiddenId);
			}
			else if (sect.SovereignActorId > 0L)
			{
				UpsertLegacyMemberIfAllowed(sect.SovereignActorId, sect.SectId, sect.FoundingYear,
					XjSectMemberRole.Sovereign, 0, _year, force: true);
			}
			if (sect.Peaks == null) continue;
			for (int p = 0; p < sect.Peaks.Count; p++)
			{
				XjSectPeakArchiveRecord peak = sect.Peaks[p];
				if (peak?.PeakMasterActorId <= 0L) continue;
				if (IsShiActorId(peak.PeakMasterActorId))
				{
					long forbiddenId = peak.PeakMasterActorId;
					peak.PeakMasterActorId = 0L;
					peak.PeakMasterName = string.Empty;
					peak.LastConfirmedYear = _year;
					XjSectAuthorityStore.RemoveMember(forbiddenId, out _);
					XjSectProjection.ClearActor(forbiddenId);
					continue;
				}
				UpsertLegacyMemberIfAllowed(peak.PeakMasterActorId, sect.SectId, peak.FoundedYear,
					XjSectMemberRole.PeakMaster, peak.PeakId, _year, force: true);
			}
		}
	}

	private static bool IsShiActorId(long actorId)
	{
		return actorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null
			&& XjCultivationPathRules.IsShi(actor);
	}

	private static bool TryResolvePeakMaster(XjSectArchiveRecord sect, long actorId, out int peakId)
	{
		peakId = 0;
		if (sect?.Peaks == null || actorId <= 0L) return false;
		for (int i = 0; i < sect.Peaks.Count; i++)
		{
			if (sect.Peaks[i]?.PeakMasterActorId != actorId) continue;
			peakId = sect.Peaks[i].PeakId;
			return true;
		}
		return false;
	}
}
