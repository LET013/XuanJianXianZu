using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly struct XjActorRelationsReadModel
{
    internal readonly string FamilySummary;
    internal readonly string BloodlineSummary;
    internal readonly string FatherStatusSummary;
    internal readonly string ZongMenSummary;

    internal XjActorRelationsReadModel(
        string familySummary,
        string bloodlineSummary,
        string fatherStatusSummary,
        string zongMenSummary)
    {
        FamilySummary = familySummary ?? string.Empty;
        BloodlineSummary = bloodlineSummary ?? string.Empty;
        FatherStatusSummary = fatherStatusSummary ?? string.Empty;
        ZongMenSummary = zongMenSummary ?? string.Empty;
    }
}

internal readonly struct XjActorEquipmentReadModel
{
    internal readonly string FaBaoSummary;
    internal readonly string QianKunDaiSummary;

    internal XjActorEquipmentReadModel(string faBaoSummary, string qianKunDaiSummary)
    {
        FaBaoSummary = faBaoSummary ?? string.Empty;
        QianKunDaiSummary = qianKunDaiSummary ?? string.Empty;
    }
}

internal readonly struct XjActorCraftReadModel
{
    internal readonly string WeaponArt;
    internal readonly string ZiJinSword;
    internal readonly string SwordCombat;
    internal readonly string Alchemy;
    internal readonly string Artifact;
    internal readonly string Talisman;
    internal readonly string Formation;

    internal XjActorCraftReadModel(
        string weaponArt,
        string ziJinSword,
        string swordCombat,
        string alchemy,
        string artifact,
        string talisman,
        string formation)
    {
        WeaponArt = weaponArt ?? string.Empty;
        ZiJinSword = ziJinSword ?? string.Empty;
        SwordCombat = swordCombat ?? string.Empty;
        Alchemy = alchemy ?? string.Empty;
        Artifact = artifact ?? string.Empty;
        Talisman = talisman ?? string.Empty;
        Formation = formation ?? string.Empty;
    }

    internal bool HasAny => !string.IsNullOrWhiteSpace(WeaponArt)
        || !string.IsNullOrWhiteSpace(ZiJinSword)
        || !string.IsNullOrWhiteSpace(SwordCombat)
        || !string.IsNullOrWhiteSpace(Alchemy)
        || !string.IsNullOrWhiteSpace(Artifact)
        || !string.IsNullOrWhiteSpace(Talisman)
        || !string.IsNullOrWhiteSpace(Formation);
}

/// <summary>
/// 百艺、器艺、家族、宗门、装备与乾坤袋的分域只读模型。
/// 每个领域只在自身Revision变化时重建，主角色面板刷新不再重新扫描全部补充系统。
/// </summary>
internal static class XjActorSupplementReadModelStore
{
    private readonly struct RelationsCacheEntry
    {
        internal readonly int RevisionHash;
        internal readonly XjActorRelationsReadModel Model;

        internal RelationsCacheEntry(int revisionHash, XjActorRelationsReadModel model)
        {
            RevisionHash = revisionHash;
            Model = model;
        }
    }

    private readonly struct EquipmentCacheEntry
    {
        internal readonly int RevisionHash;
        internal readonly XjActorEquipmentReadModel Model;

        internal EquipmentCacheEntry(int revisionHash, XjActorEquipmentReadModel model)
        {
            RevisionHash = revisionHash;
            Model = model;
        }
    }

    private readonly struct CraftCacheEntry
    {
        internal readonly int RevisionHash;
        internal readonly XjActorCraftReadModel Model;

        internal CraftCacheEntry(int revisionHash, XjActorCraftReadModel model)
        {
            RevisionHash = revisionHash;
            Model = model;
        }
    }

    private const int CacheSoftLimit = 1024;
    private static readonly Dictionary<long, RelationsCacheEntry> RelationsCache = new Dictionary<long, RelationsCacheEntry>();
    private static readonly Dictionary<long, EquipmentCacheEntry> EquipmentCache = new Dictionary<long, EquipmentCacheEntry>();
    private static readonly Dictionary<long, CraftCacheEntry> CraftCache = new Dictionary<long, CraftCacheEntry>();

    internal static int GetRelationsRevisionHash(long actorId, in XjActorRevisionToken token)
    {
        if (actorId <= 0L) return token.RelationsHash;
        long familyStableId = 0L;
        if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity familyIdentity))
        {
            familyStableId = familyIdentity.FamilyStableIdValue;
        }
        XjSectAuthorityStore.TryGetSectId(actorId, out long sectId);
        unchecked
        {
            int revisionHash = token.RelationsHash;
            revisionHash = revisionHash * 31 + XjRelationEntityRevisionStore.GetFamilyRevision(familyStableId);
            revisionHash = revisionHash * 31 + XjRelationEntityRevisionStore.GetSectRevision(sectId);
            return revisionHash;
        }
    }

    internal static XjActorRelationsReadModel GetRelations(Actor actor, long actorId, in XjActorRevisionToken token)
    {
        if (actor?.data == null || actorId <= 0L) return default;
        int revisionHash = GetRelationsRevisionHash(actorId, in token);
        if (RelationsCache.TryGetValue(actorId, out RelationsCacheEntry cached)
            && cached.RevisionHash == revisionHash)
        {
            return cached.Model;
        }

        XjActorRelationsReadModel model = XjActorInfoReadModel.BuildRelationsReadModel(actor, actorId);
        TrimIfNeeded(RelationsCache);
        RelationsCache[actorId] = new RelationsCacheEntry(revisionHash, model);
        return model;
    }

    internal static XjActorEquipmentReadModel GetEquipment(Actor actor, long actorId, in XjActorRevisionToken token)
    {
        if (actor?.data == null || actorId <= 0L) return default;
        int revisionHash = token.EquipmentHash;
        if (EquipmentCache.TryGetValue(actorId, out EquipmentCacheEntry cached)
            && cached.RevisionHash == revisionHash)
        {
            return cached.Model;
        }

        XjActorEquipmentReadModel model = XjActorInfoReadModel.BuildEquipmentReadModel(actor, actorId);
        TrimIfNeeded(EquipmentCache);
        EquipmentCache[actorId] = new EquipmentCacheEntry(revisionHash, model);
        return model;
    }

    internal static XjActorCraftReadModel GetCraft(Actor actor, long actorId, in XjActorRevisionToken token)
    {
        if (actor?.data == null || actorId <= 0L) return default;
        int revisionHash = token.CraftHash;
        if (CraftCache.TryGetValue(actorId, out CraftCacheEntry cached)
            && cached.RevisionHash == revisionHash)
        {
            return cached.Model;
        }

        XjActorCraftReadModel model = new XjActorCraftReadModel(
            XjWeaponArtSystem.BuildDisplaySummary(actor),
            XjZiJinSwordDaoSystem.BuildDisplaySummary(actor),
            XjSwordDaoCombatSystem.BuildDisplaySummary(actor),
            SimplifyCraftSummary(XjCraftProficiencySystem.BuildAlchemyProgressSummary(actor)),
            SimplifyCraftSummary(XjCraftProficiencySystem.BuildArtifactProgressSummary(actor)),
            SimplifyCraftSummary(XjCraftProficiencySystem.BuildTalismanProgressSummary(actor)),
            SimplifyCraftSummary(XjCraftProficiencySystem.BuildFormationProgressSummary(actor)));
        TrimIfNeeded(CraftCache);
        CraftCache[actorId] = new CraftCacheEntry(revisionHash, model);
        return model;
    }

    internal static void RemoveActor(long actorId)
    {
        if (actorId <= 0L) return;
        RelationsCache.Remove(actorId);
        EquipmentCache.Remove(actorId);
        CraftCache.Remove(actorId);
    }

    internal static void Clear()
    {
        RelationsCache.Clear();
        EquipmentCache.Clear();
        CraftCache.Clear();
    }

    private static string SimplifyCraftSummary(string summary)
    {
        if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
        int detailStart = summary.IndexOf('（');
        return (detailStart > 0 ? summary.Substring(0, detailStart) : summary).Trim();
    }

    private static void TrimIfNeeded<T>(Dictionary<long, T> cache)
    {
        if (cache != null && cache.Count >= CacheSoftLimit) cache.Clear();
    }
}
