using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.UI.Rank;

namespace XuanJianVNext.Patches;

/// <summary>
/// 妖属大圣收口补丁：只补 WorldBox 原生边界无法在领域代码中稳定表达的兼容行为。
/// 不增加常驻世界扫描；大圣/妖属相关判断均为 O(1) 或只在低频化生事务中执行。
/// </summary>
internal partial class XjVNextPatches
{
    private const float YaoShuGreatSageAttributeMultiplier = 1.5f;
    private const float YaoShuGreatSageMinimumMingShu = 160f;
    private const float YaoShuGreatSageMinimumDaoHui = 88f;
    private const int YaoShuGreatSageManifestationStartYear = 300;

    private static bool _yaoShuSapientCultivationUnlocked;
    private static Type _yaoShuPauseProbeType;
    private static MemberInfo _yaoShuPauseProbeMember;
    private static MethodInfo _yaoShuRepairSubspeciesMethod;
    private static MethodInfo _yaoShuNormalizeSubspeciesMethod;
    private static MethodInfo _yaoShuBeginAttackAnimationMethod;
    private static FieldInfo _yaoShuGreatSageDefinitionsField;
    private static FieldInfo _yaoShuGreatSageStatesField;

    private static readonly string[] YaoShuPauseMemberNames =
    {
        "isPaused", "is_paused", "paused", "_paused",
        "simulationPaused", "simulation_paused", "isSimulationPaused", "is_simulation_paused",
        "worldPaused", "world_paused", "gamePaused", "game_paused", "isGamePaused", "is_game_paused"
    };

    // ---------------------------------------------------------------------
    // 火德召唤鸟：WorldBox 暂停不一定等价于 Unity timeScale=0。
    // 包装整个召唤协程以及其嵌套 IEnumerator；暂停期间不推进 MoveNext，
    // 因而坐标、攻击、逐帧动画都保持原位，恢复后再继续。
    // ---------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(XjHuoDeFireBirdSummonSystem), "RunSummon")]
    private static void XuanJianVNext_HuoDeFireBird_RunSummon_PauseAware_Postfix(ref IEnumerator __result)
    {
        if (__result != null)
        {
            __result = YaoShuPauseAwareCoroutine(__result);
        }
    }

    private static IEnumerator YaoShuPauseAwareCoroutine(IEnumerator inner)
    {
        if (inner == null) yield break;
        while (true)
        {
            while (YaoShuIsSimulationPaused())
            {
                yield return null;
            }

            if (!inner.MoveNext()) yield break;
            object current = inner.Current;
            if (current is IEnumerator nested)
            {
                current = YaoShuPauseAwareCoroutine(nested);
            }
            yield return current;
        }
    }

    private static bool YaoShuIsSimulationPaused()
    {
        if (XjRuntimePauseGate.BlocksSimulation) return true;
        if (Time.timeScale <= 0.0001f) return true;

        object world = World.world;
        if (world == null) return false;
        try
        {
            Type worldType = world.GetType();
            if (_yaoShuPauseProbeType != worldType)
            {
                _yaoShuPauseProbeType = worldType;
                _yaoShuPauseProbeMember = YaoShuResolvePauseMember(worldType);
            }

            switch (_yaoShuPauseProbeMember)
            {
                case PropertyInfo property when property.PropertyType == typeof(bool):
                    return (bool)property.GetValue(world, null);
                case FieldInfo field when field.FieldType == typeof(bool):
                    return (bool)field.GetValue(world);
                case MethodInfo method when method.ReturnType == typeof(bool) && method.GetParameters().Length == 0:
                    return (bool)method.Invoke(world, null);
            }
        }
        catch
        {
            // 兼容探针失败只退回 timeScale/Codex gate，绝不影响召唤物生命周期。
        }
        return false;
    }

    private static MemberInfo YaoShuResolvePauseMember(Type worldType)
    {
        if (worldType == null) return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        for (int i = 0; i < YaoShuPauseMemberNames.Length; i++)
        {
            string name = YaoShuPauseMemberNames[i];
            PropertyInfo property = worldType.GetProperty(name, flags);
            if (property?.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0) return property;
            FieldInfo field = worldType.GetField(name, flags);
            if (field?.FieldType == typeof(bool)) return field;
            MethodInfo method = worldType.GetMethod(name, flags, null, Type.EmptyTypes, null);
            if (method?.ReturnType == typeof(bool)) return method;
        }
        return null;
    }

    // ---------------------------------------------------------------------
    // 妖民退场：保留旧档妖民身份识别，但不再投放创始种群，也不再让
    // BabyMaker 子代继承妖民身份。原有存档不会因为删 trait/删字段而炸档。
    // ---------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "SpawnFounderWave")]
    private static bool XuanJianVNext_YaoShu_SpawnFounderWave_Disabled_Prefix(ref int __result)
    {
        __result = 0;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "ObserveNativeBirth")]
    private static bool XuanJianVNext_YaoShu_ObserveNativeBirth_NoYaoMinInheritance_Prefix()
    {
        return false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "TryAwakenForGreatSage")]
    private static void XuanJianVNext_YaoShu_TryAwakenForGreatSage_UnlockCultivation_Postfix()
    {
        _yaoShuSapientCultivationUnlocked = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "TryMaintainForGreatSage")]
    private static void XuanJianVNext_YaoShu_TryMaintainForGreatSage_UnlockCultivation_Postfix()
    {
        _yaoShuSapientCultivationUnlocked = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "ImportPayload")]
    private static void XuanJianVNext_YaoShu_ImportPayload_RestoreCultivationUnlock_Postfix(int __0, string __1)
    {
        if (__0 <= 0 || string.IsNullOrWhiteSpace(__1)) return;
        string[] parts = __1.Split(';');
        for (int i = 0; i < parts.Length; i++)
        {
            string[] fields = (parts[i] ?? string.Empty).Split(',');
            if (fields.Length > 1 && string.Equals(fields[1], "1", StringComparison.Ordinal))
            {
                _yaoShuSapientCultivationUnlocked = true;
                return;
            }
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuSapientSpecies), "ClearRuntime")]
    private static void XuanJianVNext_YaoShu_ClearRuntime_ResetCultivationUnlock_Postfix()
    {
        _yaoShuSapientCultivationUnlocked = false;
    }

    // ---------------------------------------------------------------------
    // 首位大圣显世后，物种门槛从“四族硬白名单”放开到“智慧动物”。
    // 这里只改资格，不把普通动物批量改造成修士；新生智慧动物仍沿用原有
    // 五岁资质窗口，成年个体可经玩家显式授予/既有修炼状态进入同一链路。
    // ---------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanCultivate")]
    private static void XuanJianVNext_YaoShu_CanCultivate_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (__result || !YaoShuCanUseUnlockedAnimalCultivation(__0)) return;
        if (!XjRuntimeSettings.CultivationEnabled || !XjJianDaoCompatibility.ShouldAllowCultivation(__0)) return;
        __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanReceiveXuanJianContent")]
    private static void XuanJianVNext_YaoShu_CanReceiveXuanJianContent_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (!__result && YaoShuCanUseUnlockedAnimalCultivation(__0)) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "IsSupportedNativeCultivationSpecies")]
    private static void XuanJianVNext_YaoShu_IsSupportedCultivationSpecies_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (!__result && YaoShuCanUseUnlockedAnimalCultivation(__0)) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "IsCultivationHardBlocked")]
    private static void XuanJianVNext_YaoShu_IsCultivationHardBlocked_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (__result && YaoShuCanUseUnlockedAnimalCultivation(__0)) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "ShouldClearUnsupportedCultivationState")]
    private static void XuanJianVNext_YaoShu_ShouldClearUnsupported_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (__result && YaoShuCanUseUnlockedAnimalCultivation(__0)) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "RecordManualCultivationGrant")]
    private static void XuanJianVNext_YaoShu_RecordManualCultivationGrant_SapientAnimal_Postfix(Actor __0)
    {
        if (!YaoShuCanUseUnlockedAnimalCultivation(__0)) return;
        XjActorAccessor.SetInt(__0, XjActorDataKeys.ManualCultivationGrant, 1);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanEnterFamilyLedger")]
    private static void XuanJianVNext_YaoShu_CanEnterFamilyLedger_SapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (__result || !YaoShuCanUseUnlockedAnimalCultivation(__0)) return;
        if (__0?.kingdom?.data != null || __0?.city?.data != null) __result = true;
    }

    private static bool YaoShuCanUseUnlockedAnimalCultivation(Actor actor)
    {
        if (!_yaoShuSapientCultivationUnlocked
            || actor?.data == null
            || !actor.isAlive()
            || XjTrueDamageSystem.IsJinXingYaoXie(actor)
            || XjLongShuSystem.IsLongShu(actor)
            || XjYaoShuGreatSageSystem.IsGreatSage(actor))
        {
            return false;
        }

        try
        {
            bool animalLike = actor.isAnimal() || actor.asset?.default_animal == true;
            string assetId = actor.asset?.id ?? actor.data.asset_id ?? string.Empty;
            if (!animalLike && assetId.StartsWith("civ_", StringComparison.OrdinalIgnoreCase))
            {
                string normalized = assetId.Substring(4);
                animalLike = !string.Equals(normalized, "human", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "elf", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "dwarf", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalized, "orc", StringComparison.OrdinalIgnoreCase);
            }
            return animalLike && actor.isSapient();
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------------
    // 大圣资产数值：旧实现把每席 2.4万~5.25万的设计血量强行抬到
    // 4000万基线，再叠真君羽士境界倍率，最终得到约 9.59 亿血量。
    // Init 完成后恢复各席 Definition 的真实底盘，再由统一境界投影乘一次。
    // ---------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "Init")]
    private static void XuanJianVNext_YaoShuGreatSage_Init_CorrectBaseStats_Postfix()
    {
        try
        {
            Array definitions = YaoShuGetGreatSageDefinitions();
            ActorAssetLibrary library = AssetManager.actor_library;
            if (definitions == null || library == null) return;

            for (int i = 0; i < definitions.Length; i++)
            {
                object definition = definitions.GetValue(i);
                string assetId = YaoShuReadField(definition, "ActorAssetId", string.Empty);
                if (string.IsNullOrWhiteSpace(assetId)) continue;
                ActorAsset asset = library.get(assetId);
                if (asset == null) continue;

                float health = YaoShuReadField(definition, "Health", 1f);
                float damage = YaoShuReadField(definition, "Damage", 1f);
                float armor = YaoShuReadField(definition, "Armor", 0f);
                float speed = YaoShuReadField(definition, "Speed", 1f);
                float attackSpeed = YaoShuReadField(definition, "AttackSpeed", 1f);
                float mass = YaoShuReadField(definition, "Mass", 1f);

                asset.base_stats["health"] = Mathf.Max(1f, health) * YaoShuGreatSageAttributeMultiplier;
                asset.base_stats["damage"] = Mathf.Max(1f, damage) * YaoShuGreatSageAttributeMultiplier;
                asset.base_stats["armor"] = Mathf.Max(0f, armor) * YaoShuGreatSageAttributeMultiplier;
                asset.base_stats["speed"] = Mathf.Max(1f, speed) * YaoShuGreatSageAttributeMultiplier;
                asset.base_stats["attack_speed"] = Mathf.Max(1f, attackSpeed) * YaoShuGreatSageAttributeMultiplier;
                asset.base_stats["mass"] = Mathf.Max(1f, mass) * YaoShuGreatSageAttributeMultiplier;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.CorrectBaseStats", ex);
        }
    }

    // 首次显世不再被 50% 掷骰长期空置；身灭后的再化生仍保留原有低频概率。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "RollManifestation")]
    private static bool XuanJianVNext_YaoShuGreatSage_RollManifestation_FirstGuaranteed_Prefix(object __0, ref bool __result)
    {
        if (!YaoShuTryGetManifestationCount(__0, out int count) || count > 0) return true;
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "ScheduleNextAttempt")]
    private static bool XuanJianVNext_YaoShuGreatSage_ScheduleNextAttempt_FirstRetry_Prefix(object __0, int __1, ref int __result)
    {
        if (!YaoShuTryGetManifestationCount(__0, out int count) || count > 0) return true;
        int year = Math.Max(0, __1);
        __result = year < YaoShuGreatSageManifestationStartYear
            ? YaoShuGreatSageManifestationStartYear
            : year + 1;
        return false;
    }

    // 新生大圣若原生 spawn 没有分配可用亚种，不再直接判失败。
    // 从世界中任一正常原生亚种复制数据壳，再复用现有迁移函数改绑到本席 ActorAsset。
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "EnsureGreatSageSubspecies")]
    private static void XuanJianVNext_YaoShuGreatSage_EnsureSubspecies_CreateMissing_Postfix(Actor __0, object __1)
    {
        if (__0?.data == null || __1 == null || YaoShuHasCorrectGreatSageSubspecies(__0, __1)) return;
        try
        {
            Subspecies source = YaoShuFindSubspeciesTemplate(__0);
            if (source?.data == null) return;

            _yaoShuRepairSubspeciesMethod ??= AccessTools.Method(
                typeof(XjYaoShuGreatSageSystem), "TryRepairLegacyCrossAssetSubspecies");
            object repairedObject = _yaoShuRepairSubspeciesMethod?.Invoke(null, new object[] { __0, __1, source });
            if (repairedObject is not Subspecies repaired || repaired.data == null) return;

            _yaoShuNormalizeSubspeciesMethod ??= AccessTools.Method(
                typeof(XjYaoShuGreatSageSystem), "NormalizeGreatSageSubspecies");
            _yaoShuNormalizeSubspeciesMethod?.Invoke(null, new object[] { repaired });
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.Subspecies.CreateMissing", ex);
        }
    }

    private static Subspecies YaoShuFindSubspeciesTemplate(Actor actor)
    {
        try
        {
            if (actor?.subspecies?.data != null && !actor.subspecies.isRekt()) return actor.subspecies;
            IReadOnlyList<Actor> units = World.world?.units?.getSimpleList();
            if (units == null) return null;
            for (int i = 0; i < units.Count; i++)
            {
                Actor candidate = units[i];
                if (ReferenceEquals(candidate, actor)) continue;
                Subspecies subspecies = candidate?.subspecies;
                if (subspecies?.data != null && !subspecies.isRekt()) return subspecies;
            }
        }
        catch
        {
        }
        return null;
    }

    private static bool YaoShuHasCorrectGreatSageSubspecies(Actor actor, object definition)
    {
        try
        {
            Subspecies subspecies = actor?.subspecies;
            if (subspecies?.data == null || subspecies.isRekt()) return false;
            string expectedAssetId = YaoShuReadField(definition, "ActorAssetId", string.Empty);
            return !string.IsNullOrWhiteSpace(expectedAssetId)
                && string.Equals(subspecies.getActorAsset()?.id, expectedAssetId, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    // 每次真实攻击回调都启动 attack 序列；不再由 0.75 秒技能提示冷却决定是否播放。
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "OnGreatSageAttackTarget")]
    private static void XuanJianVNext_YaoShuGreatSage_OnAttack_PlayAttackFrames_Postfix(BaseSimObject __0)
    {
        try
        {
            if (__0 == null || !__0.isActor()) return;
            Actor actor = __0.a;
            if (!XjYaoShuGreatSageSystem.IsGreatSage(actor) || actor?.data == null) return;
            long actorId = ((BaseSystemData)actor.data).id;
            if (actorId <= 0L) return;

            _yaoShuBeginAttackAnimationMethod ??= AccessTools.Method(
                typeof(XjYaoShuGreatSageSystem), "BeginAttackAnimation");
            _yaoShuBeginAttackAnimationMethod?.Invoke(null, new object[] { actorId, Mathf.Max(0f, Time.time) });
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.AttackAnimation", ex);
        }
    }

    // 补齐大圣作为“修士”而不是“生物空壳”的基础权威数据。
    // 已有更高命数/道慧绝不回写降低；只为旧档和新化生缺失值建立下限。
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "EnsureGreatSageIdentity")]
    private static void XuanJianVNext_YaoShuGreatSage_EnsureIdentity_MingShuDaoHui_Postfix(Actor __0)
    {
        if (__0?.data == null || !__0.isAlive()) return;
        try
        {
            XjActorAccessor.TryGetFloat(__0, XjActorDataKeys.MingShuCongenital, out float congenital);
            XjActorAccessor.TryGetFloat(__0, XjActorDataKeys.MingShuAcquired, out float acquired);
            XjActorAccessor.TryGetFloat(__0, XjActorDataKeys.MingShu, out float totalStored);
            float total = Math.Max(totalStored, congenital + acquired);
            if (total < YaoShuGreatSageMinimumMingShu)
            {
                XjMingShuState.Set(__0, 100f, YaoShuGreatSageMinimumMingShu - 100f);
            }
            else
            {
                XjMingShuState.Normalize(__0);
            }

            XjActorAccessor.TryGetFloat(__0, XjActorDataKeys.HuiGuang, out float daoHui);
            if (daoHui < YaoShuGreatSageMinimumDaoHui)
            {
                XjActorAccessor.SetFloat(
                    __0,
                    XjActorDataKeys.HuiGuang,
                    XjDaoHuiPolicy.Clamp(YaoShuGreatSageMinimumDaoHui));
            }

            if (AssetManager.traits?.get("peaceful") != null && !__0.hasTrait("peaceful"))
            {
                __0.addTrait("peaceful", false);
            }

            _yaoShuSapientCultivationUnlocked = true;
            XjCultivatorCache.CheckAndUpdate(__0);
            __0.setStatsDirty();
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.Identity.Seed", ex);
        }
    }

    // 普通非智慧动物不再把大圣当作可主动猎杀目标；若大圣先攻击它，仍允许反击。
    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjActorAggroBridge), "IsValidTarget")]
    private static void XuanJianVNext_YaoShuGreatSage_OrdinaryAnimalAggro_Postfix(
        Actor __0,
        BaseSimObject __1,
        ref bool __result)
    {
        if (!__result || __0?.data == null || __1 == null || !__1.isActor()) return;
        Actor targetActor = __1.a;
        if (!XjYaoShuGreatSageSystem.IsGreatSage(targetActor)
            || XjYaoShuGreatSageSystem.IsGreatSage(__0)) return;

        try
        {
            bool ordinaryAnimal = (__0.isAnimal() || __0.asset?.default_animal == true) && !__0.isSapient();
            if (!ordinaryAnimal) return;
            if (ReferenceEquals(__0.attackedBy, targetActor)) return;
            __result = false;
        }
        catch
        {
        }
    }

    // 排行榜归属以大圣身份优先于其内部野生 kingdom 实现细节。
    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjRankCardView), "ResolveBelonging")]
    private static bool XuanJianVNext_YaoShuGreatSage_RankBelonging_Prefix(Actor __0, ref string __result)
    {
        if (!XjYaoShuGreatSageSystem.IsGreatSage(__0)) return true;
        __result = "妖属大圣";
        return false;
    }

    private static Array YaoShuGetGreatSageDefinitions()
    {
        _yaoShuGreatSageDefinitionsField ??= AccessTools.Field(typeof(XjYaoShuGreatSageSystem), "Definitions");
        return _yaoShuGreatSageDefinitionsField?.GetValue(null) as Array;
    }

    private static bool YaoShuTryGetManifestationCount(object definition, out int count)
    {
        count = 0;
        if (definition == null) return false;
        try
        {
            Array definitions = YaoShuGetGreatSageDefinitions();
            _yaoShuGreatSageStatesField ??= AccessTools.Field(typeof(XjYaoShuGreatSageSystem), "States");
            Array states = _yaoShuGreatSageStatesField?.GetValue(null) as Array;
            if (definitions == null || states == null) return false;

            int length = Math.Min(definitions.Length, states.Length);
            for (int i = 0; i < length; i++)
            {
                if (!ReferenceEquals(definitions.GetValue(i), definition)) continue;
                object state = states.GetValue(i);
                count = YaoShuReadField(state, "ManifestationCount", 0);
                return true;
            }
        }
        catch
        {
        }
        return false;
    }

    private static T YaoShuReadField<T>(object source, string fieldName, T fallback)
    {
        if (source == null || string.IsNullOrWhiteSpace(fieldName)) return fallback;
        try
        {
            FieldInfo field = source.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object value = field?.GetValue(source);
            if (value is T typed) return typed;
            if (value != null) return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
        }
        return fallback;
    }
}
