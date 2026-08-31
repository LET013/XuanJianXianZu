using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 执孛神通「形渡阡」的真实幻身。
///
/// 原著口径只确认“化出有固定修为、灵识自如的幻身”，因此这里实现为一名真正
/// WorldBox Actor：拥有独立 AI、索敌、移动和受击。生成瞬间会把本体当前的
/// 可见/运行时特质做1:1快照，并冻结当时的核心战斗数值；但不复制家族、宗门、
/// 果位所有权、功法推进与社会身份，也不占任何高境位序。本体之后继续突破不会
/// 实时同步，正对应“固定修为”。
/// </summary>
internal static class XjXingDuQianPhantomSystem
{
    private const string CanonicalName = "形渡阡";
    private const int TraitSnapshotSchema = 1;
    // 原著只确认幻身“固定修为、灵识自如”，未给常驻寿命。模组按本轮定稿：
    // 显化后行动3年；自然消散或被杀后温养5年，之后才允许重新显化。
    private const int ManifestDurationYears = 3;
    private const int WarmupYears = 5;

    internal static bool IsPhantom(Actor actor)
    {
        return actor?.data != null
            && XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjXingDuQianSourceActorId, out long sourceId)
            && sourceId > 0L
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXingDuQianInitialized, out int initialized)
            && initialized > 0;
    }

    internal static bool HasPersistedMarker(Actor actor)
    {
        if (actor?.data == null) return false;
        return XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjXingDuQianSourceActorId, out long sourceId)
            && sourceId > 0L;
    }

    internal static bool TryGetFixedRealmTier(Actor actor, out int tier)
    {
        tier = XjRealmSuppression.TierNone;
        return IsPhantom(actor)
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXingDuQianFixedRealmTier, out tier)
            && tier > XjRealmSuppression.TierNone;
    }

    internal static bool TryGetFixedCombatLevel(Actor actor, out int combatLevel)
    {
        combatLevel = 0;
        return IsPhantom(actor)
            && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXingDuQianFixedCombatLevel, out combatLevel)
            && combatLevel > 0;
    }

    internal static void TickSource(
        Actor source,
        in XjActorCultivationSnapshot snapshot,
        in XjXianJiState xianJiState,
        int currentYear)
    {
        if (source?.data == null || !source.isAlive() || IsPhantom(source)) return;

        bool hasXingDuQian = Contains(xianJiState.Ids, CanonicalName);
        if (!hasXingDuQian)
        {
            // 失去神通属于权威状态变化，不把清理误算成“战败温养”；若以后重新学得，可重新起算。
            RemoveExistingPhantom(source, currentYear, beginWarmup: false);
            ClearLifecycle(source);
            return;
        }

        if (XjActorAccessor.TryGetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, out long phantomId)
            && phantomId > 0L)
        {
            if (XjScheduler.ResolveActor(phantomId, out Actor existing)
                && existing?.data != null
                && existing.isAlive()
                && IsPhantom(existing))
            {
                int expireYear = ReadLifecycleYear(source, XjActorDataKeys.XjXingDuQianExpireYear);
                if (expireYear <= 0)
                {
                    // 0.9.8.9旧档的常驻幻身没有寿期字段；载入0.9.8.10后从当前年重新给3年行动期，
                    // 避免旧幻身永久存在，同时不在读档瞬间直接抹掉。
                    int manifestYear = Math.Max(0, currentYear);
                    expireYear = manifestYear + ManifestDurationYears;
                    WriteManifestWindow(source, existing, manifestYear, expireYear);
                }

                if (currentYear >= expireYear)
                {
                    RemoveExistingPhantom(source, currentYear, beginWarmup: true);
                    return;
                }

                EnsureAllegiance(source, existing);
                EnsureTraitSnapshotSchema(source, existing);
                // 旧档载入后幻身可能先于运行索引恢复；只对这一名已知幻身重新观察，
                // 不做世界扫描，确保固定战斗位格与受击/死亡链立即生效。
                XjRuntimeActorInterestIndex.Observe(existing);
                return;
            }
            // 链接存在但幻身已不在世，按“被杀/失去实体”进入完整5年温养，
            // 防止异常注销漏过HandleActorRemoved后下一次年度检查立刻重生。
            XjActorAccessor.SetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, 0L);
            BeginWarmup(source, currentYear);
            return;
        }

        int nextManifestYear = ReadLifecycleYear(source, XjActorDataKeys.XjXingDuQianNextManifestYear);
        if (nextManifestYear > currentYear) return;

        TryCreatePhantom(source, snapshot, currentYear);
    }

    internal static bool ApplyRuntimeStats(Actor phantom)
    {
        if (!IsPhantom(phantom) || phantom.stats == null) return false;

        float health = ReadPositiveFloat(phantom, XjActorDataKeys.XjXingDuQianFixedHealth, 1f);
        float damage = ReadPositiveFloat(phantom, XjActorDataKeys.XjXingDuQianFixedDamage, 1f);
        float speed = ReadPositiveFloat(phantom, XjActorDataKeys.XjXingDuQianFixedSpeed, 1f);
        float attackSpeed = ReadPositiveFloat(phantom, XjActorDataKeys.XjXingDuQianFixedAttackSpeed, 0.1f);
        float range = ReadPositiveFloat(phantom, XjActorDataKeys.XjXingDuQianFixedRange, 1f);

        phantom.stats["health"] = Math.Max(1f, health);
        phantom.stats["damage"] = Math.Max(1f, damage);
        phantom.stats["speed"] = Math.Max(1f, speed);
        phantom.stats["attack_speed"] = Math.Max(0.1f, attackSpeed);
        phantom.stats["range"] = Math.Max(1f, range);
        // 护甲、暴击、闪避等其余面板属性由1:1特质快照和原生 updateStats 继续结算，
        // 不再把幻身额外清零成“缺属性”的空壳。
        return true;
    }

    internal static void HandleActorRemoved(Actor actor)
    {
        if (actor?.data == null) return;
        long actorId = GetActorId(actor);
        if (actorId <= 0L) return;

        if (IsPhantom(actor))
        {
            if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjXingDuQianSourceActorId, out long sourceId)
                && sourceId > 0L
                && XjScheduler.ResolveActor(sourceId, out Actor source)
                && source?.data != null)
            {
                XjActorAccessor.SetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, 0L);
                BeginWarmup(source, CurrentYear());
            }
            return;
        }

        if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjXingDuQianPhantomActorId, out long phantomId)
            && phantomId > 0L
            && XjScheduler.ResolveActor(phantomId, out Actor phantom)
            && phantom?.data != null)
        {
            XjDengMingShiSpawnSafety.TryRemoveInvalidActor(phantom);
        }
        XjActorAccessor.SetLong(actor, XjActorDataKeys.XjXingDuQianPhantomActorId, 0L);
        ClearLifecycle(actor);
    }

    private static void TryCreatePhantom(Actor source, in XjActorCultivationSnapshot snapshot, int currentYear)
    {
        WorldTile origin = TryGetTile(source);
        if (origin == null || World.world?.units == null) return;
        WorldTile tile = ResolveNearbyTile(origin);
        if (tile == null) return;

        int fixedTier = XjRealmSuppression.GetRealmTier(source);
        int fixedCombatLevel = XjRealmSuppression.GetCombatLevel(source);
        if (fixedTier <= XjRealmSuppression.TierNone) return;

        Actor phantom = World.world.units.spawnNewUnit(source.data.asset_id, tile, false, false, 0f, null, false, true);
        if (phantom?.data == null) return;

        try { phantom.setCity(null); } catch { }
        EnsureAllegiance(source, phantom);
        if (phantom.isKingdomCiv() && phantom.kingdom?.data == null)
        {
            XjDengMingShiSpawnSafety.TryRemoveInvalidActor(phantom);
            return;
        }

        long sourceId = GetActorId(source);
        long phantomId = GetActorId(phantom);
        if (sourceId <= 0L || phantomId <= 0L)
        {
            XjDengMingShiSpawnSafety.TryRemoveInvalidActor(phantom);
            return;
        }

        // 幻身不写真实 RealmId/DaoTu/XianJi，因此不会进入突破、果位、宗门与家族流水线。
        // 但显化瞬间的可见/运行时特质必须与本体1:1一致。
        CopyTraitSnapshot(source, phantom);
        XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianTraitSnapshotVersion, TraitSnapshotSchema);
        XjActorAccessor.SetLong(phantom, XjActorDataKeys.XjXingDuQianSourceActorId, sourceId);
        XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianInitialized, 1);
        XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianFixedRealmTier, fixedTier);
        XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianFixedCombatLevel,
            fixedCombatLevel > 0 ? fixedCombatLevel : ResolveFallbackCombatLevel(fixedTier));

        CaptureFixedStats(source, phantom);
        XjActorStateWriteGateway.SetDisplayName(phantom, BuildPhantomName(source), true);
        XjActorAccessor.SetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, phantomId);
        int manifestYear = Math.Max(0, currentYear);
        int expireYear = manifestYear + ManifestDurationYears;
        WriteManifestWindow(source, phantom, manifestYear, expireYear);
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianNextManifestYear, 0);

        XjScheduler.RegisterActor(phantom);
        XjRuntimeActorInterestIndex.Observe(phantom);
        try { phantom.setStatsDirty(); phantom.updateStats(); } catch { }
        try
        {
            float maxHealth = Math.Max(1f, XjSafeCore.GetMaxHealthSafe(phantom, 1f));
            phantom.data.health = Mathf.Max(1, Mathf.RoundToInt(maxHealth));
        }
        catch { }

        XjCanonicalShenTongVisualSystem.TryPlayXingDuQianManifestation(source, phantom);
    }

    private static void CaptureFixedStats(Actor source, Actor phantom)
    {
        float sourceHealth = Math.Max(1f, XjSafeCore.GetMaxHealthSafe(source,
            XjSafeCore.GetStatSafe(source, "health", 1f)));
        float sourceDamage = Math.Max(1f, XjSafeCore.GetStatSafe(source, "damage", 1f));
        float sourceSpeed = Math.Max(1f, XjSafeCore.GetStatSafe(source, "speed", 1f));
        float sourceAttackSpeed = Math.Max(0.1f, XjSafeCore.GetStatSafe(source, "attack_speed", 0.1f));
        float sourceRange = Math.Max(1f, XjSafeCore.GetStatSafe(source, "range", 1f));

        XjActorAccessor.SetFloat(phantom, XjActorDataKeys.XjXingDuQianFixedHealth, sourceHealth);
        XjActorAccessor.SetFloat(phantom, XjActorDataKeys.XjXingDuQianFixedDamage, sourceDamage);
        XjActorAccessor.SetFloat(phantom, XjActorDataKeys.XjXingDuQianFixedSpeed, sourceSpeed);
        XjActorAccessor.SetFloat(phantom, XjActorDataKeys.XjXingDuQianFixedAttackSpeed, sourceAttackSpeed);
        XjActorAccessor.SetFloat(phantom, XjActorDataKeys.XjXingDuQianFixedRange, sourceRange);
    }

    private static void EnsureTraitSnapshotSchema(Actor source, Actor phantom)
    {
        if (source?.data == null || phantom?.data == null) return;
        if (XjActorAccessor.TryGetInt(phantom, XjActorDataKeys.XjXingDuQianTraitSnapshotVersion, out int version)
            && version >= TraitSnapshotSchema) return;

        // 旧存档的形渡阡只保存了空壳与75%核心数值。升级后对已在世的这一具幻身
        // 做一次有界迁移：以当前本体补齐特质快照并改为1:1核心数值，之后继续冻结。
        CopyTraitSnapshot(source, phantom);
        CaptureFixedStats(source, phantom);
        XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianTraitSnapshotVersion, TraitSnapshotSchema);
        try
        {
            phantom.clearTraitCache();
            phantom.setStatsDirty();
            phantom.updateStats();
            ApplyRuntimeStats(phantom);
        }
        catch { }
    }

    private static void CopyTraitSnapshot(Actor source, Actor phantom)
    {
        if (source?.data == null || phantom?.data == null || source.traits == null || phantom.traits == null) return;

        // 不走 addTrait/removeTrait：这些 API 会触发“手动赋予/删除”事务钩子。
        // 幻身复制的是显化快照，不能据此制造第二份修炼后台、果位或道统状态。
        HashSet<string> sourceIds = new HashSet<string>(StringComparer.Ordinal);
        List<ActorTrait> sourceTraits = new List<ActorTrait>();
        foreach (ActorTrait trait in source.traits)
        {
            if (trait == null || string.IsNullOrWhiteSpace(trait.id) || !sourceIds.Add(trait.id)) continue;
            sourceTraits.Add(trait);
        }

        List<ActorTrait> toRemove = new List<ActorTrait>();
        foreach (ActorTrait trait in phantom.traits)
        {
            if (trait == null || string.IsNullOrWhiteSpace(trait.id) || !sourceIds.Contains(trait.id))
            {
                toRemove.Add(trait);
            }
        }
        for (int i = 0; i < toRemove.Count; i++) phantom.traits.Remove(toRemove[i]);

        HashSet<string> phantomIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (ActorTrait trait in phantom.traits)
        {
            if (trait != null && !string.IsNullOrWhiteSpace(trait.id)) phantomIds.Add(trait.id);
        }
        for (int i = 0; i < sourceTraits.Count; i++)
        {
            ActorTrait trait = sourceTraits[i];
            if (phantomIds.Add(trait.id)) phantom.traits.Add(trait);
        }

        phantom.data.saved_traits ??= new List<string>();
        phantom.data.saved_traits.Clear();
        if (source.data.saved_traits != null)
        {
            phantom.data.saved_traits.AddRange(source.data.saved_traits);
        }
        // 运行时已有、但尚未落入 saved_traits 的特质也写入幻身快照，避免读档后缺失。
        for (int i = 0; i < sourceTraits.Count; i++)
        {
            string traitId = sourceTraits[i].id;
            if (!phantom.data.saved_traits.Contains(traitId)) phantom.data.saved_traits.Add(traitId);
        }

        try { phantom.clearTraitCache(); phantom.setStatsDirty(); } catch { }
    }

    private static void RemoveExistingPhantom(Actor source, int currentYear, bool beginWarmup)
    {
        if (source?.data == null) return;
        long phantomId = 0L;
        XjActorAccessor.TryGetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, out phantomId);
        // 先断开源端链接，避免移除回调再次把同一幻身视为仍在役。
        XjActorAccessor.SetLong(source, XjActorDataKeys.XjXingDuQianPhantomActorId, 0L);
        if (phantomId > 0L
            && XjScheduler.ResolveActor(phantomId, out Actor phantom)
            && phantom?.data != null)
        {
            XjDengMingShiSpawnSafety.TryRemoveInvalidActor(phantom);
        }

        if (beginWarmup) BeginWarmup(source, currentYear);
        else ClearLifecycle(source);
    }

    private static void WriteManifestWindow(Actor source, Actor phantom, int manifestYear, int expireYear)
    {
        if (source?.data != null)
        {
            XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianManifestYear, Math.Max(0, manifestYear));
            XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianExpireYear, Math.Max(0, expireYear));
        }
        if (phantom?.data != null)
        {
            XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianManifestYear, Math.Max(0, manifestYear));
            XjActorAccessor.SetInt(phantom, XjActorDataKeys.XjXingDuQianExpireYear, Math.Max(0, expireYear));
        }
    }

    private static void BeginWarmup(Actor source, int currentYear)
    {
        if (source?.data == null) return;
        int next = Math.Max(0, currentYear) + WarmupYears;
        int existing = ReadLifecycleYear(source, XjActorDataKeys.XjXingDuQianNextManifestYear);
        if (existing > next) next = existing;
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianManifestYear, 0);
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianExpireYear, 0);
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianNextManifestYear, next);
    }

    private static void ClearLifecycle(Actor source)
    {
        if (source?.data == null) return;
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianManifestYear, 0);
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianExpireYear, 0);
        XjActorAccessor.SetInt(source, XjActorDataKeys.XjXingDuQianNextManifestYear, 0);
    }

    private static int ReadLifecycleYear(Actor actor, string key)
    {
        return actor?.data != null && XjActorAccessor.TryGetInt(actor, key, out int value)
            ? Math.Max(0, value)
            : 0;
    }

    private static int CurrentYear()
    {
        try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
        catch { return 0; }
    }

    private static void EnsureAllegiance(Actor source, Actor phantom)
    {
        if (source?.data == null || phantom?.data == null) return;
        try
        {
            if (source.kingdom?.data != null && phantom.kingdom != source.kingdom)
                phantom.setKingdom(source.kingdom);
        }
        catch { }
        try { phantom.setCity(null); } catch { }
    }

    private static string BuildPhantomName(Actor source)
    {
        string name = source?.getName();
        if (string.IsNullOrWhiteSpace(name)) name = "无名";
        return "幻身·" + name.Trim();
    }

    private static int ResolveFallbackCombatLevel(int tier)
    {
        return tier switch
        {
            XjRealmSuppression.TierDaoTai => 28,
            XjRealmSuppression.TierJinDan => 24,
            XjRealmSuppression.TierZiFu => 19,
            XjRealmSuppression.TierZhuJi => 16,
            XjRealmSuppression.TierLianQi => 7,
            XjRealmSuppression.TierTaiXi => 1,
            _ => 0
        };
    }

    private static float ReadPositiveFloat(Actor actor, string key, float fallback)
    {
        return XjActorAccessor.TryGetFloat(actor, key, out float value) && value > 0f ? value : fallback;
    }

    private static bool Contains(string[] ids, string value)
    {
        if (ids == null || string.IsNullOrWhiteSpace(value)) return false;
        for (int i = 0; i < ids.Length; i++)
        {
            if (string.Equals((ids[i] ?? string.Empty).Trim(), value, StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static WorldTile ResolveNearbyTile(WorldTile origin)
    {
        if (origin == null) return null;
        int[,] offsets =
        {
            { 2, 0 }, { -2, 0 }, { 0, 2 }, { 0, -2 },
            { 2, 2 }, { -2, 2 }, { 2, -2 }, { -2, -2 },
            { 3, 0 }, { 0, 3 }, { -3, 0 }, { 0, -3 }
        };
        for (int i = 0; i < offsets.GetLength(0); i++)
        {
            WorldTile tile = World.world?.GetTileSimple(origin.x + offsets[i, 0], origin.y + offsets[i, 1]);
            if (tile != null) return tile;
        }
        return origin;
    }

    private static WorldTile TryGetTile(Actor actor)
    {
        try { return actor?.data == null ? null : ((BaseSimObject)actor).current_tile; }
        catch { return null; }
    }

    private static long GetActorId(Actor actor)
    {
        try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
        catch { return 0L; }
    }
}
