using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Patches;

/// <summary>
/// 0.9.11 妖属收口：把大圣的修炼身份、妖民五岁准入、果位映照、百艺隔离与
/// 原生死亡/特质编辑边界放回同一条权威链。这里只做事件型补正，不增加常驻扫描。
/// </summary>
internal partial class XjVNextPatches
{
    private const string YaoShuAge5CheckedKey = "xj_yaoshu_age5_checked";
    private const int YaoShuAge5AwakenPercent = 20;
    private const string YaoShuStableTemplateId = "civ_seal";

    [ThreadStatic]
    private static int _yaoShuManifestationContextDepth;

    private static MethodInfo _yaoShuEnsureYaoMinIdentityMethod;
    private static MethodInfo _yaoShuResolveRealmTraitMethod;
    private static MethodInfo _yaoShuResolveDaoTuTraitMethod;

    private static readonly string[] YaoShuCraftTraitIds =
    {
        "XjAlchemyCrafter",
        "XjArtifactRefiner",
        "XjTalismanCrafter",
        "XjFormationMaster"
    };

    // ------------------------------------------------------------------
    // 妖民：只有五岁判定成功并真正带有妖民身份的智慧动物才进入修炼链。
    // 不恢复创始妖民、不继承、不灭绝补种；资质下限只存在于内部状态。
    // ------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanCultivate")]
    private static void XuanJianVNext_YaoShu_CanCultivate_RequireYaoMin_Postfix(Actor __0, ref bool __result)
    {
        YaoShuRestrictSapientAnimalEligibility(__0, ref __result);
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjCultivationEligibility), "CanReceiveXuanJianContent")]
    private static void XuanJianVNext_YaoShu_CanReceiveContent_RequireYaoMin_Postfix(Actor __0, ref bool __result)
    {
        YaoShuRestrictSapientAnimalEligibility(__0, ref __result);
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjCultivationEligibility), "IsSupportedNativeCultivationSpecies")]
    private static void XuanJianVNext_YaoShu_IsSupportedSpecies_RequireYaoMin_Postfix(Actor __0, ref bool __result)
    {
        YaoShuRestrictSapientAnimalEligibility(__0, ref __result);
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjCultivationEligibility), "IsCultivationHardBlocked")]
    private static void XuanJianVNext_YaoShu_HardBlock_NonYaoMinSapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (YaoShuIsSupportedSapientAnimal(__0) && !XjYaoShuSapientSpecies.IsYaoMin(__0)) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjCultivationEligibility), "ShouldClearUnsupportedCultivationState")]
    private static void XuanJianVNext_YaoShu_ClearUnsupported_NonYaoMinSapientAnimal_Postfix(Actor __0, ref bool __result)
    {
        if (YaoShuIsSupportedSapientAnimal(__0) && !XjYaoShuSapientSpecies.IsYaoMin(__0)) __result = true;
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(Actor), "updateAge")]
    private static void XuanJianVNext_YaoShu_Age5YaoMinAwakening_Postfix(Actor __instance)
    {
        if (!_yaoShuSapientCultivationUnlocked
            || __instance?.data == null
            || !__instance.isAlive()
            || XjYaoShuGreatSageSystem.IsGreatSage(__instance)
            || XjYaoShuSapientSpecies.IsYaoMin(__instance)
            || !YaoShuIsSupportedSapientAnimal(__instance)
            || YaoShuResolveAge(__instance) != 5)
        {
            return;
        }

        if (XjActorAccessor.TryGetInt(__instance, YaoShuAge5CheckedKey, out int checkedValue) && checkedValue > 0) return;
        XjActorAccessor.SetInt(__instance, YaoShuAge5CheckedKey, 1);

        long actorId = ((BaseSystemData)__instance.data).id;
        if (actorId <= 0L || XjDeterministicHash.PositiveIndex(actorId, "yaoshu_yaomin_age5", 100) >= YaoShuAge5AwakenPercent) return;

        try
        {
            _yaoShuEnsureYaoMinIdentityMethod ??= AccessTools.Method(typeof(XjYaoShuSapientSpecies), "EnsureYaoMinIdentity");
            _yaoShuEnsureYaoMinIdentityMethod?.Invoke(null, new object[] { __instance, false });

            XjActorAccessor.TryGetInt(__instance, XjActorDataKeys.XjZz, out int aptitude);
            if (aptitude < 3)
            {
                XjActorAccessor.SetInt(__instance, XjActorDataKeys.XjZz, 3);
                XjActorAccessor.SetInt(__instance, XjActorDataKeys.XjZzCheckedAge5, 1);
                XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(__instance, 3);
                XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(__instance, 3);
            }

            YaoShuRemoveCraftTraits(__instance);
            XjCultivatorCache.CheckAndUpdate(__instance);
            __instance.setStatsDirty();
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuYaoMin.Age5Awakening", ex);
        }
    }

    private static void YaoShuRestrictSapientAnimalEligibility(Actor actor, ref bool result)
    {
        if (!YaoShuIsSupportedSapientAnimal(actor)) return;
        result = XjYaoShuSapientSpecies.IsYaoMin(actor);
    }

    private static bool YaoShuIsSupportedSapientAnimal(Actor actor)
    {
        if (actor?.data == null || !actor.isAlive() || XjYaoShuGreatSageSystem.IsGreatSage(actor)) return false;
        try
        {
            return actor.isSapient() && XjYaoShuSapientSpecies.TryResolveSupportedAssetId(actor, out _);
        }
        catch
        {
            return false;
        }
    }

    private static int YaoShuResolveAge(Actor actor)
    {
        if (actor?.data == null) return -1;
        try
        {
            MethodInfo method = AccessTools.Method(actor.GetType(), "getAge", Type.EmptyTypes);
            if (method?.Invoke(actor, null) is int intAge) return intAge;
            if (method?.Invoke(actor, null) is float floatAge) return Mathf.FloorToInt(floatAge);
        }
        catch
        {
        }
        return -1;
    }

    // ------------------------------------------------------------------
    // 大圣不进入玄鉴百艺。既有旧档残留在身份协调时同步清掉；新授予在原生
    // addTrait 边界直接拒绝，避免百艺系统下一拍又补回来。
    // ------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(string), typeof(bool) })]
    private static bool XuanJianVNext_YaoShu_BlockCraftTrait_String_Prefix(
        Actor __instance,
        [HarmonyArgument("pTraitID")] string traitId,
        ref bool __result)
    {
        if (!YaoShuShouldBlockCraftTrait(__instance, traitId)) return true;
        __result = false;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Actor), "addTrait", new Type[] { typeof(ActorTrait), typeof(bool) })]
    private static bool XuanJianVNext_YaoShu_BlockCraftTrait_Asset_Prefix(
        Actor __instance,
        [HarmonyArgument("pTrait")] ActorTrait trait,
        ref bool __result)
    {
        if (!YaoShuShouldBlockCraftTrait(__instance, trait?.id)) return true;
        __result = false;
        return false;
    }

    private static bool YaoShuShouldBlockCraftTrait(Actor actor, string traitId)
    {
        if (actor?.data == null || string.IsNullOrWhiteSpace(traitId)) return false;
        bool isYaoShu = XjYaoShuGreatSageSystem.IsGreatSage(actor) || XjYaoShuSapientSpecies.IsYaoMin(actor);
        if (!isYaoShu) return false;
        for (int i = 0; i < YaoShuCraftTraitIds.Length; i++)
        {
            if (string.Equals(traitId, YaoShuCraftTraitIds[i], StringComparison.Ordinal)) return true;
        }
        return false;
    }

    private static void YaoShuRemoveCraftTraits(Actor actor)
    {
        if (actor?.data == null) return;
        for (int i = 0; i < YaoShuCraftTraitIds.Length; i++)
        {
            string traitId = YaoShuCraftTraitIds[i];
            try { if (actor.hasTrait(traitId)) actor.removeTrait(traitId); } catch { }
        }
    }

    // ------------------------------------------------------------------
    // 大圣果位是“妖属映照”。剧情封锁不参与化生判定；只有 XjGuoWeiRegistry
    // 中确有存活真实持位者时，才阻止对应大圣显世。
    // ------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TickAnnual")]
    private static void XuanJianVNext_YaoShu_ManifestationContext_Annual_Prefix()
    {
        _yaoShuManifestationContextDepth++;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TickAnnual")]
    private static void XuanJianVNext_YaoShu_ManifestationContext_Annual_Postfix()
    {
        _yaoShuManifestationContextDepth = Math.Max(0, _yaoShuManifestationContextDepth - 1);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryDebugManifestAll")]
    private static void XuanJianVNext_YaoShu_ManifestationContext_Debug_Prefix()
    {
        _yaoShuManifestationContextDepth++;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "TryDebugManifestAll")]
    private static void XuanJianVNext_YaoShu_ManifestationContext_Debug_Postfix()
    {
        _yaoShuManifestationContextDepth = Math.Max(0, _yaoShuManifestationContextDepth - 1);
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjGuoWeiRegistry), "IsPermanentlyLockedGuoWei")]
    private static void XuanJianVNext_YaoShu_IgnoreStoryLockDuringManifestation_Postfix(ref bool __result)
    {
        if (_yaoShuManifestationContextDepth > 0) __result = false;
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "CanAttemptManifestation")]
    private static void XuanJianVNext_YaoShu_OnlyLivingHolderBlocksManifestation_Postfix(object __0, ref bool __result)
    {
        if (!__result || __0 == null) return;
        string fruit = YaoShuReadObjectString(__0, "FruitId");
        if (!string.IsNullOrWhiteSpace(fruit) && YaoShuHasLivingTrueGuoWeiHolder(fruit)) __result = false;
    }

    private static bool YaoShuHasLivingTrueGuoWeiHolder(string fruit)
    {
        IReadOnlyList<XjGuoWeiRegistryEntry> entries = XjGuoWeiRegistry.ReadActiveEntries();
        for (int i = 0; i < entries.Count; i++)
        {
            object boxed = entries[i];
            string guoWei = YaoShuReadObjectString(boxed, "GuoWei");
            if (!string.Equals((guoWei ?? string.Empty).Trim(), fruit.Trim(), StringComparison.Ordinal)) continue;
            long actorId = YaoShuReadObjectLong(boxed, "ActorId");
            if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor holder) || !XjSafeCore.IsAliveActor(holder)) continue;
            if (!XjYaoShuGreatSageSystem.IsGreatSage(holder)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    // 大圣身份：每次身份协调后强制回到真实服气真君羽士权威层；不生成修士尊号，
    // 数值底盘取普通原生修士底盘后再做 1.5x，寿元做 3x。性情按席位固定分流。
    // ------------------------------------------------------------------

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "EnsureGreatSageIdentity")]
    private static void XuanJianVNext_YaoShu_EnsureIdentity_Finalize_Postfix(Actor __0, object __1)
    {
        if (__0?.data == null || __1 == null || !__0.isAlive()) return;
        try
        {
            XjActorAccessor.SetInt(__0, XjActorDataKeys.XjZz, 6);
            XjActorAccessor.SetInt(__0, XjActorDataKeys.XjZzCheckedAge5, 1);
            XjFuQiStateTransitions.TrySetRealm(__0, XjRealmIds.ZhenJunYuShi, syncVisibleTraits: true, clearManualRemoved: false);

            string displayName = YaoShuReadObjectString(__1, "DisplayName");
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                XjActorStateWriteGateway.SetDisplayName(__0, displayName.Trim(), customName: true);
            }

            YaoShuNormalizeGreatSageBaseStats(__0);
            YaoShuApplySeatPersonality(__0, YaoShuReadObjectString(__1, "SlotId"));
            YaoShuRemoveCraftTraits(__0);
            XjCultivatorCache.CheckAndUpdate(__0);
            __0.setStatsDirty();
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.Identity.Finalize", ex);
        }
    }

    private static void YaoShuNormalizeGreatSageBaseStats(Actor actor)
    {
        ActorAsset target = actor?.asset;
        ActorAsset template = AssetManager.actor_library?.get(YaoShuStableTemplateId);
        if (target?.base_stats == null || template?.base_stats == null || ReferenceEquals(target, template)) return;

        YaoShuCopyScaledBaseStat(template, target, "health", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "damage", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "armor", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "speed", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "attack_speed", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "mass", 1.5f);
        YaoShuCopyScaledBaseStat(template, target, "lifespan", 3f);
    }

    private static void YaoShuCopyScaledBaseStat(ActorAsset source, ActorAsset target, string key, float scale)
    {
        try
        {
            float value = source.base_stats[key];
            target.base_stats[key] = Mathf.Max(0f, value * scale);
        }
        catch
        {
        }
    }

    private static void YaoShuApplySeatPersonality(Actor actor, string slotId)
    {
        if (actor?.data == null) return;
        int bucket = XjDeterministicHash.PositiveIndex(((BaseSystemData)actor.data).id, "yaoshu_personality_" + (slotId ?? string.Empty), 3);
        bool peaceful = bucket == 0;
        bool bloodlust = bucket == 1;
        YaoShuSetOptionalTrait(actor, "peaceful", peaceful);
        YaoShuSetOptionalTrait(actor, "bloodlust", bloodlust);
    }

    private static void YaoShuSetOptionalTrait(Actor actor, string traitId, bool wanted)
    {
        if (actor?.data == null || AssetManager.traits?.get(traitId) == null) return;
        try
        {
            bool present = actor.hasTrait(traitId);
            if (wanted && !present) actor.addTrait(traitId, false);
            else if (!wanted && present) actor.removeTrait(traitId);
        }
        catch
        {
        }
    }

    // attack 文件已经存在；真实攻击后额外把第一帧保持一个极短窗口，避免一拍内
    // 被 run/walk 重算盖回去，然后继续原有完整 attack 序列。
    [HarmonyPostfix]
    [HarmonyPriority(-200)]
    [HarmonyPatch(typeof(XjYaoShuGreatSageSystem), "OnGreatSageAttackTarget")]
    private static void XuanJianVNext_YaoShu_AttackFrames_PersistFirstFrame_Postfix(BaseSimObject __0)
    {
        if (__0 == null || !__0.isActor()) return;
        Actor actor = __0.a;
        if (actor?.data == null || !XjYaoShuGreatSageSystem.IsGreatSage(actor)) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        try
        {
            _yaoShuBeginAttackAnimationMethod ??= AccessTools.Method(typeof(XjYaoShuGreatSageSystem), "BeginAttackAnimation");
            _yaoShuBeginAttackAnimationMethod?.Invoke(null, new object[] { actorId, Time.time + 0.12f });
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("YaoShuGreatSage.AttackFrames.Persist", ex);
        }
    }

    // ------------------------------------------------------------------
    // 普通修士死亡归档不能吞大圣：大圣只走自己的【大圣身殒】事务，不产出
    // 修士金性遗留、家族仓库归还等副作用。
    // ------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(XjDeathPatchBridge), "CaptureBeforeDeath")]
    private static bool XuanJianVNext_YaoShuGreatSage_SkipCultivatorDeathArchive_Prefix(Actor __0, ref XjDeathSnapshot __result)
    {
        if (!XjYaoShuGreatSageSystem.IsGreatSage(__0)) return true;
        __result = XjDeathSnapshot.Empty;
        return false;
    }

    // ------------------------------------------------------------------
    // 编辑器特质：只让“境界/道途”进入境界协调器，其他玄鉴特质保留原生授予结果。
    // 真君羽士若第一次协调刚好停在真人，当次事务内再提交一次，消灭二次点击依赖。
    // ------------------------------------------------------------------

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(XjManualRealmTraitReconciliation), "HandleGranted")]
    private static bool XuanJianVNext_ManualTraitGrant_OnlyRealmOrDaoTu_Prefix(Actor __0, string __1)
    {
        if (__0?.data == null || string.IsNullOrWhiteSpace(__1)) return true;
        return YaoShuIsRealmOrDaoTuTrait(__1);
    }

    [HarmonyPostfix]
    [HarmonyPriority(-100)]
    [HarmonyPatch(typeof(XjManualRealmTraitReconciliation), "HandleGranted")]
    private static void XuanJianVNext_ManualZhenJun_AtomicRetry_Postfix(Actor __0, string __1, bool __2, string __3)
    {
        if (__0?.data == null
            || !__2
            || !string.Equals(__1, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
            || XjYaoShuGreatSageSystem.IsGreatSage(__0)) return;

        XjActorAccessor.TryGetString(__0, XjActorDataKeys.RealmId, out string realmId);
        if (string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return;
        XjManualFuQiRealmReconciliation.ReconcileGrant(__0, XjRealmIds.ZhenJunYuShi, __3);
    }

    private static bool YaoShuIsRealmOrDaoTuTrait(string traitId)
    {
        if (XjManualHighRealmGrantPolicy.IsProtectedHighRealmTrait(traitId)) return true;
        try
        {
            _yaoShuResolveRealmTraitMethod ??= AccessTools.Method(typeof(XjManualRealmTraitReconciliation), "TryResolveRealm");
            object[] realmArgs = { traitId, string.Empty, 0 };
            if (_yaoShuResolveRealmTraitMethod?.Invoke(null, realmArgs) is bool realm && realm) return true;

            _yaoShuResolveDaoTuTraitMethod ??= AccessTools.Method(typeof(XjManualRealmTraitReconciliation), "TryResolveDaoTuTrait");
            object[] daoArgs = { traitId, string.Empty };
            if (_yaoShuResolveDaoTuTraitMethod?.Invoke(null, daoArgs) is bool dao && dao) return true;
        }
        catch
        {
            // 识别器异常时宁可放行原协调器，避免把合法境界编辑静默吞掉。
            return true;
        }
        return false;
    }

    private static string YaoShuReadObjectString(object source, string memberName)
    {
        object value = YaoShuReadObjectMember(source, memberName);
        return value?.ToString() ?? string.Empty;
    }

    private static long YaoShuReadObjectLong(object source, string memberName)
    {
        object value = YaoShuReadObjectMember(source, memberName);
        if (value is long longValue) return longValue;
        if (value is int intValue) return intValue;
        return value != null && long.TryParse(value.ToString(), out long parsed) ? parsed : 0L;
    }

    private static object YaoShuReadObjectMember(object source, string memberName)
    {
        if (source == null || string.IsNullOrWhiteSpace(memberName)) return null;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type type = source.GetType();
        try
        {
            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(source, null);
            FieldInfo field = type.GetField(memberName, flags);
            return field?.GetValue(source);
        }
        catch
        {
            return null;
        }
    }
}
