using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门单权威一致性审计。只遍历宗门档案和权威成员索引，不扫描世界角色全表。
/// 安全修复仅允许：重投影镜像、补齐无歧义的宗主/峰主职阶。
/// </summary>
internal static class XjSectAuthorityAudit
{
    internal static void Audit(List<string> issues, bool repairSafeIssues, ref int repairedIssueCount)
    {
        IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
        IReadOnlyList<XjSectFamilySeatArchiveRecord> seats = XjSectRepository.ReadAllFamilySeats();
        HashSet<long> knownSectIds = new();
        Dictionary<long, long> claimedCitySectIds = new();

        for (int i = 0; i < sects.Count; i++)
        {
            XjSectArchiveRecord sect = sects[i];
            if (sect == null || sect.SectId <= 0L)
            {
                issues.Add("宗门档案存在空记录或无效SectId");
                continue;
            }

            if (!knownSectIds.Add(sect.SectId)) issues.Add("宗门SectId重复：" + sect.SectId);
            if (string.IsNullOrWhiteSpace(sect.Name)) issues.Add("宗门" + sect.SectId + "缺少名称");

            AuditCity(sect, sect.CapitalCityId, true, claimedCitySectIds, issues, repairSafeIssues, ref repairedIssueCount);
            if (sect.CityIds != null)
            {
                HashSet<long> localCities = new();
                for (int cityIndex = 0; cityIndex < sect.CityIds.Count; cityIndex++)
                {
                    long cityId = sect.CityIds[cityIndex];
                    if (cityId <= 0L || cityId == sect.CapitalCityId) continue;
                    if (!localCities.Add(cityId))
                    {
                        issues.Add("宗门" + sect.SectId + "重复登记城镇：" + cityId);
                        continue;
                    }
                    AuditCity(sect, cityId, false, claimedCitySectIds, issues, repairSafeIssues, ref repairedIssueCount);
                }
            }

            AuditMembers(sect, issues, repairSafeIssues, ref repairedIssueCount);
            AuditLeadership(sect, issues, repairSafeIssues, ref repairedIssueCount);
        }

        for (int i = 0; i < seats.Count; i++)
        {
            XjSectFamilySeatArchiveRecord seat = seats[i];
            if (seat == null || seat.SectId <= 0L || seat.FamilyId <= 0L)
            {
                issues.Add("宗门家族席位存在无效记录");
                continue;
            }
            if (!knownSectIds.Contains(seat.SectId)) issues.Add("家族席位指向不存在宗门：" + seat.SectId + "/" + seat.FamilyId);
        }
    }

    private static void AuditMembers(XjSectArchiveRecord sect, List<string> issues, bool repairSafeIssues, ref int repairedIssueCount)
    {
        IReadOnlyList<XjSectMemberArchiveRecord> members = XjSectAuthorityStore.ReadMembersForSect(sect.SectId);
        HashSet<long> actorIds = new();
        for (int i = 0; i < members.Count; i++)
        {
            XjSectMemberArchiveRecord member = members[i];
            if (member == null || member.ActorId <= 0L || member.SectId != sect.SectId)
            {
                issues.Add("宗门" + sect.SectId + "存在无效成员记录");
                continue;
            }
            if (!actorIds.Add(member.ActorId)) issues.Add("宗门" + sect.SectId + "成员重复：" + member.ActorId);
            if (!XjScheduler.ResolveActor(member.ActorId, out Actor actor) || actor?.data == null || !actor.isAlive())
            {
                issues.Add("宗门" + sect.SectId + "成员不可解析：" + member.ActorId);
                continue;
            }

            long mirroredSectId = XjSectProjection.ReadActorMirrorSectId(actor);
            if (mirroredSectId == sect.SectId) continue;
            issues.Add("宗门" + sect.SectId + "成员" + member.ActorId + "角色镜像指向：" + mirroredSectId);
            if (repairSafeIssues)
            {
                XjSectProjection.ProjectActor(member.ActorId);
                repairedIssueCount++;
            }
        }
    }

    private static void AuditLeadership(XjSectArchiveRecord sect, List<string> issues, bool repairSafeIssues, ref int repairedIssueCount)
    {
        int year = Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);
        if (sect.SovereignActorId > 0L)
        {
            if (!XjSectAuthorityStore.TryGetMember(sect.SovereignActorId, out XjSectMemberArchiveRecord sovereignMember)
                || sovereignMember.SectId != sect.SectId)
            {
                issues.Add("宗门" + sect.SectId + "宗主不在权威成员表：" + sect.SovereignActorId);
            }
            else if (!string.Equals(XjSectMemberRole.Normalize(sovereignMember.Role), XjSectMemberRole.Sovereign, StringComparison.Ordinal))
            {
                issues.Add("宗门" + sect.SectId + "宗主职阶偏移：" + sect.SovereignActorId);
                if (repairSafeIssues && XjScheduler.ResolveActor(sect.SovereignActorId, out Actor sovereign) && sovereign?.data != null)
                {
                    XjSectCommands.ChangeSovereign(sect.SectId, sovereign, year, founding: false);
                    repairedIssueCount++;
                }
            }
        }

        if (sect.Peaks == null) return;
        HashSet<int> peakIds = new();
        HashSet<long> peakMasterIds = new();
        for (int i = 0; i < sect.Peaks.Count; i++)
        {
            XjSectPeakArchiveRecord peak = sect.Peaks[i];
            if (peak == null || peak.PeakId < XjSectPeakIds.FirstRegular)
            {
                issues.Add("宗门" + sect.SectId + "存在无效支峰记录");
                continue;
            }
            if (!peakIds.Add(peak.PeakId)) issues.Add("宗门" + sect.SectId + "峰号重复：" + peak.PeakId);
            if (peak.SectId != sect.SectId) issues.Add("宗门" + sect.SectId + "山峰" + peak.PeakId + "反向SectId错误：" + peak.SectId);
            if (peak.PeakMasterActorId <= 0L) continue;
            if (!peakMasterIds.Add(peak.PeakMasterActorId)) issues.Add("宗门" + sect.SectId + "同一峰主占据多峰：" + peak.PeakMasterActorId);
            if (!XjSectAuthorityStore.TryGetMember(peak.PeakMasterActorId, out XjSectMemberArchiveRecord member)
                || member.SectId != sect.SectId)
            {
                issues.Add("宗门" + sect.SectId + "峰主不在权威成员表：" + peak.PeakMasterActorId);
                continue;
            }
            bool roleMatches = string.Equals(XjSectMemberRole.Normalize(member.Role), XjSectMemberRole.PeakMaster, StringComparison.Ordinal)
                && member.PeakId == peak.PeakId;
            if (roleMatches) continue;
            issues.Add("宗门" + sect.SectId + "峰主职阶偏移：" + peak.PeakMasterActorId + "/峰" + peak.PeakId);
            if (repairSafeIssues && XjScheduler.ResolveActor(peak.PeakMasterActorId, out Actor actor) && actor?.data != null)
            {
                XjSectCommands.AssignPeakMaster(sect.SectId, peak.PeakId, actor, year, peak.PeakName);
                repairedIssueCount++;
            }
        }
    }

    private static void AuditCity(
        XjSectArchiveRecord sect,
        long cityId,
        bool isCapital,
        Dictionary<long, long> claimedCitySectIds,
        List<string> issues,
        bool repairSafeIssues,
        ref int repairedIssueCount)
    {
        if (cityId <= 0L)
        {
            if (isCapital) issues.Add("宗门" + sect.SectId + "缺少山门城镇");
            return;
        }
        if (claimedCitySectIds.TryGetValue(cityId, out long claimedSectId) && claimedSectId != sect.SectId)
        {
            issues.Add("城镇" + cityId + "被宗门" + claimedSectId + "与" + sect.SectId + "同时占用");
        }
        else
        {
            claimedCitySectIds[cityId] = sect.SectId;
        }

        if (!XjSectAuthorityStore.TryGetSectIdByCity(cityId, out long indexedSectId) || indexedSectId != sect.SectId)
        {
            issues.Add("城镇" + cityId + "权威索引偏移：" + indexedSectId + "，应为" + sect.SectId);
        }
        if (!XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null)
        {
            issues.Add("宗门" + sect.SectId + "登记不可解析城镇：" + cityId);
            return;
        }

        city.data.get(XjSectCityData.KeySectIdMirror, out string rawSectId, string.Empty);
        city.data.get(XjSectCityData.KeyZongMenName, out string rawName, string.Empty);
        long mirrorId = long.TryParse(rawSectId, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0L;
        bool mismatch = mirrorId != sect.SectId || !string.Equals((rawName ?? string.Empty).Trim(), (sect.Name ?? string.Empty).Trim(), StringComparison.Ordinal);
        if (!mismatch) return;
        issues.Add("宗门" + sect.SectId + "城镇" + cityId + "投影偏移（" + mirrorId + "，" + rawName + "）");
        if (repairSafeIssues)
        {
            XjSectAuthorityStore.MarkProjectionDirty(sect.SectId);
            repairedIssueCount++;
        }
    }

}
