using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 可选的上修阻道规则。只在求金者已经通过自身成功判定、位序也确实可承证时结算；
/// 不驱动寻路或实际战斗，避免把高境事务重新塞回原生战斗 AI。
/// </summary>
internal static class XjUpperCultivatorDaoObstructionSystem
{
    private const int ObstructionChancePercent = 10;
    private const string EventType = "JinDanDaoObstruction";

    internal static bool TryResolve(Actor target, int currentYear)
    {
        if (!XjRuntimeSettings.UpperCultivatorDaoObstructionEnabled
            || target?.data == null || !target.isAlive()) return false;

        long targetId = ActorId(target);
        if (targetId <= 0L) return false;

        long targetFamilyId = ResolveFamilyId(targetId);
        long targetSectId = XjSectRepository.ResolveActorSectId(target);
        List<long> candidates = BuildEligibleObstructors(target, targetId, targetFamilyId, targetSectId);
        if (candidates.Count == 0) return false;

        if (XjDeterministicHash.PositiveIndex(targetId + Math.Max(1, currentYear),
            "jindan_upper_cultivator_dao_obstruction_roll", 100) >= ObstructionChancePercent)
        {
            return false;
        }

        candidates.Sort();
        int index = XjDeterministicHash.PositiveIndex(targetId * 31L + Math.Max(1, currentYear),
            "jindan_upper_cultivator_dao_obstruction_picker", candidates.Count);
        if (!XjActorRegistry.ResolveKnownOrWorld(candidates[index], out Actor obstructor)
            || !IsTrueLivingJinDan(obstructor)) return false;

        string targetName = SafeName(target);
        string obstructorName = SafeName(obstructor);
        long obstructorId = ActorId(obstructor);
        long obstructorFamilyId = ResolveFamilyId(obstructorId);
        long obstructorSectId = XjSectRepository.ResolveActorSectId(obstructor);

        string history = "【上修阻道】" + targetName + "叩金门将成之际，" + obstructorName
            + "忽以金丹法意横截其道。两人未在尘世正面斗法，金门却于一念之间崩散，"
            + targetName + "道基随之断绝，当场陨落。";

        XjActorAccessor.SetString(target, XjActorDataKeys.XjDeathAnnouncementReason, "上修阻道");
        bool died = XjVanillaDeathGuard.TryExecuteForceDeath(
            target, (AttackType)11, true, XjDeathCause.ScriptedFinality);
        if (!died)
        {
            XjActorAccessor.SetString(target, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
            return false;
        }

        RecordPersonal(targetId, targetName, currentYear,
            XjThreeBookEventTypes.PersonalJinDanDaoObstructed,
            "obstructed", "金门遭截",
            targetName + "求金将成时遭" + obstructorName
                + "出手阻道。高境法意直接截断金门，其未及成丹便道基俱散，陨于此年。",
            targetFamilyId, targetSectId, obstructorId, obstructorName, XjHistoryResult.Death);
        RecordPersonal(obstructorId, obstructorName, currentYear,
            XjThreeBookEventTypes.PersonalJinDanDaoObstructor,
            "obstructor", "出手阻道",
            obstructorName + "察觉" + targetName
                + "叩金门，遂以金丹法意直接阻道。此举未化作尘世斗法，却使对方金门崩散、求金身死。",
            obstructorFamilyId, obstructorSectId, targetId, targetName, XjHistoryResult.Success);

        XjBroadcastSystem.BroadcastSLevelDomainEvent(
            XjWorldHistoryCategory.HighRealm,
            EventType,
            history,
            history,
            actorId: targetId,
            actorName: targetName,
            relatedActorId: obstructorId,
            relatedActorName: obstructorName,
            result: XjHistoryResult.Death,
            year: currentYear,
            color: "#D66E5A",
            duration: 10f,
            iconId: XjEventIconCatalog.JinDanFail,
            announcementCategory: XjAnnouncementCategory.HighRealmInfluence);
        return true;
    }

    private static List<long> BuildEligibleObstructors(Actor target, long targetId, long targetFamilyId, long targetSectId)
    {
        List<long> result = new List<long>();
        IReadOnlyList<long> ids = XjCultivatorCache.GetJinDanIds();
        for (int i = 0; i < ids.Count; i++)
        {
            long actorId = ids[i];
            if (actorId <= 0L || actorId == targetId
                || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
                || !IsTrueLivingJinDan(actor)) continue;

            long familyId = ResolveFamilyId(actorId);
            if (targetFamilyId > 0L && familyId == targetFamilyId) continue;
            long sectId = XjSectRepository.ResolveActorSectId(actor);
            if (targetSectId > 0L && sectId == targetSectId) continue;

			// 扶持与阻道是两条相反的长期意图。同一上修一旦已经把此人列为扶金对象，
			// 或已经立作本道正果承继人，就绝不能在其叩金门时又被随机池抽中亲手阻道。
			// 其他异族异宗上修仍可按原规则出手，因此没有把受扶持者做成全局免阻道目标。
			if (XjUpperCultivatorGoldSupportSystem.IsProtectedFromPatronObstruction(actor, target)) continue;
            result.Add(actorId);
        }
        return result;
    }

    private static bool IsTrueLivingJinDan(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
        string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
        return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
            && XjJinDanAccessor.BuildState(actor).Found
            && !XjShenDanAccessor.BuildState(actor).Found
            && !XjDaoTaiSpellScale.IsDaoTaiActor(actor);
    }

    private static void RecordPersonal(long actorId, string actorName, int year, string eventType,
        string sourceSuffix, string title, string body, long familyId, long sectId,
        long relatedActorId, string relatedActorName, string result)
    {
        string familyName = familyId > 0L ? XjFamilyDisplayNameResolver.Resolve(familyId) : string.Empty;
        string sectName = string.Empty;
        if (sectId > 0L && XjSectRepository.TryGetBySectId(sectId, out var sect) && sect != null)
        {
            sectName = sect.Name ?? string.Empty;
        }
        XjPersonalBiographyStore.Record(new XjThreeBookArchiveRecord
        {
            SourceFactId = "personal|jindan_dao_obstruction|" + sourceSuffix + "|" + actorId + "|" + relatedActorId + "|" + year,
            SubjectId = actorId,
            SubjectNameSnapshot = actorName,
            Year = Math.Max(1, year),
            EventType = eventType,
            Category = XjWorldHistoryCategory.HighRealm,
            Tag = "阻道",
            Title = title,
            Body = body,
            Importance = 5,
            IsProtected = true,
            Result = result,
            ActorId = actorId,
            ActorName = actorName,
            RelatedActorId = relatedActorId,
            RelatedActorName = relatedActorName,
            FamilyId = familyId,
            FamilyNameSnapshot = familyName,
            SectId = sectId,
            SectNameSnapshot = sectName
        });
    }

    private static long ResolveFamilyId(long actorId)
    {
        if (actorId > 0L && XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
            && familyId > 0L) return familyId;
        return XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
            && entry.Found ? Math.Max(0L, entry.FamilyStableId) : 0L;
    }

    private static long ActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }

    private static string SafeName(Actor actor)
    {
        string name = actor?.getName();
        return string.IsNullOrWhiteSpace(name) ? "无名修士" : name.Trim();
    }
}
