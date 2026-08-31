using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 旃檀林的原生聚落创建前置门禁。
///
/// WorldBox 负责 City 的创建与完整生命周期；玄鉴只表达一条领域规则：
/// 旃檀林保护圆内不得启动原生文明聚落事务。这里不创建第二套建城系统，
/// 也不再扫描 Assembly-CSharp 猜测“哪些方法看起来像建城”。
///
/// 目标表固定到当前支持的 WorldBox 0.51.x 已验证入口。小版本若缺少某个方法，
/// 该入口 fail-soft 跳过并记录，不会扩大扫描范围或误补丁无关 AI。
/// </summary>
internal static class XjZhantanlinNativeSettlementGuard
{
    private const BindingFlags DeclaredFlags = BindingFlags.Public | BindingFlags.NonPublic |
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static bool _initialized;
    private static int _patched;
    private static int _skipped;

    internal static void Init(Harmony harmony)
    {
        if (_initialized || harmony == null) return;
        _initialized = true;

        MethodInfo cityInstancePrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockCityResultInstancePrefix));
        MethodInfo cityStaticPrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockCityResultStaticPrefix));
        MethodInfo boolInstancePrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockBoolResultInstancePrefix));
        MethodInfo boolStaticPrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockBoolResultStaticPrefix));
        MethodInfo voidInstancePrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockVoidInstancePrefix));
        MethodInfo voidStaticPrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockVoidStaticPrefix));
        MethodInfo genericInstancePrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockGenericInstancePrefix));
        MethodInfo genericStaticPrefix = AccessTools.Method(typeof(XjZhantanlinNativeSettlementGuard), nameof(BlockGenericStaticPrefix));
        HashSet<MethodBase> patched = new HashSet<MethodBase>();
        List<string> patchedNames = new List<string>(12);

        PatchExactTarget(harmony, typeof(Actor), "canBuildNewCity", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTarget(harmony, typeof(Actor), "buildCityAndStartCivilization", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);

        PatchExactTarget(harmony, typeof(CityManager), "newCity", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTarget(harmony, typeof(CityManager), "buildNewCity", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTarget(harmony, typeof(CityManager), "tryToCreateCity", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTarget(harmony, typeof(CityManager), "canStartNewCityCivilizationHere", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTarget(harmony, typeof(CityManager), "buildFirstCivilizationCity", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);

        PatchExactTargetByTypeName(harmony, "BehKingCheckNewCityFoundation", "execute", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTargetByTypeName(harmony, "BehKingCheckNewCityFoundation", "startWave", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
        PatchExactTargetByTypeName(harmony, "BehCheckBuildCity", "execute", patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);

        Debug.Log("[玄鉴][旃檀林] 原生聚落精确门禁 patched=" + _patched + " skipped=" + _skipped
            + (patchedNames.Count > 0 ? " targets=" + string.Join(",", patchedNames) : string.Empty));
    }

    private static void PatchExactTargetByTypeName(
        Harmony harmony,
        string typeName,
        string methodName,
        HashSet<MethodBase> patched,
        List<string> patchedNames,
        MethodInfo cityInstancePrefix,
        MethodInfo cityStaticPrefix,
        MethodInfo boolInstancePrefix,
        MethodInfo boolStaticPrefix,
        MethodInfo voidInstancePrefix,
        MethodInfo voidStaticPrefix,
        MethodInfo genericInstancePrefix,
        MethodInfo genericStaticPrefix)
    {
        Type type = AccessTools.TypeByName(typeName);
        if (type == null)
        {
            _skipped++;
            return;
        }

        PatchExactTarget(harmony, type, methodName, patched, patchedNames,
            cityInstancePrefix, cityStaticPrefix, boolInstancePrefix, boolStaticPrefix,
            voidInstancePrefix, voidStaticPrefix, genericInstancePrefix, genericStaticPrefix);
    }

    private static void PatchExactTarget(
        Harmony harmony,
        Type type,
        string methodName,
        HashSet<MethodBase> patched,
        List<string> patchedNames,
        MethodInfo cityInstancePrefix,
        MethodInfo cityStaticPrefix,
        MethodInfo boolInstancePrefix,
        MethodInfo boolStaticPrefix,
        MethodInfo voidInstancePrefix,
        MethodInfo voidStaticPrefix,
        MethodInfo genericInstancePrefix,
        MethodInfo genericStaticPrefix)
    {
        if (harmony == null || type == null || string.IsNullOrWhiteSpace(methodName))
        {
            _skipped++;
            return;
        }

        MethodInfo[] methods;
        try { methods = type.GetMethods(DeclaredFlags); }
        catch (Exception ex)
        {
            _skipped++;
            XjExceptionDiagnostics.Report("ZhantanlinSettlementGuard.Resolve." + type.FullName + "." + methodName, ex);
            return;
        }

        bool found = false;
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method == null
                || method.IsAbstract
                || method.ContainsGenericParameters
                || method.IsSpecialName
                || !string.Equals(method.Name, methodName, StringComparison.Ordinal)
                || !patched.Add(method))
            {
                continue;
            }

            found = true;
            MethodInfo prefix = method.ReturnType == typeof(City)
                ? (method.IsStatic ? cityStaticPrefix : cityInstancePrefix)
                : method.ReturnType == typeof(bool)
                    ? (method.IsStatic ? boolStaticPrefix : boolInstancePrefix)
                    : method.ReturnType == typeof(void)
                        ? (method.IsStatic ? voidStaticPrefix : voidInstancePrefix)
                        : (method.IsStatic ? genericStaticPrefix : genericInstancePrefix);
            if (prefix == null)
            {
                _skipped++;
                continue;
            }

            try
            {
                harmony.Patch(method, prefix: new HarmonyMethod(prefix) { priority = Priority.First });
                _patched++;
                if (patchedNames.Count < 16)
                {
                    patchedNames.Add((method.DeclaringType?.Name ?? "?") + "." + method.Name);
                }
            }
            catch (Exception ex)
            {
                _skipped++;
                XjExceptionDiagnostics.Report("ZhantanlinSettlementGuard.Patch."
                    + (method.DeclaringType?.FullName ?? "unknown") + "." + method.Name, ex);
            }
        }

        if (!found)
        {
            _skipped++;
        }
    }

    private static bool BlockCityResultInstancePrefix(object __instance, object[] __args, ref City __result)
    {
        if (!ShouldBlock(__instance, __args)) return true;
        __result = null;
        return false;
    }

    private static bool BlockCityResultStaticPrefix(object[] __args, ref City __result)
    {
        if (!ShouldBlock(null, __args)) return true;
        __result = null;
        return false;
    }

    private static bool BlockBoolResultInstancePrefix(object __instance, object[] __args, ref bool __result)
    {
        if (!ShouldBlock(__instance, __args)) return true;
        __result = false;
        return false;
    }

    private static bool BlockBoolResultStaticPrefix(object[] __args, ref bool __result)
    {
        if (!ShouldBlock(null, __args)) return true;
        __result = false;
        return false;
    }

    private static bool BlockVoidInstancePrefix(object __instance, object[] __args)
    {
        return !ShouldBlock(__instance, __args);
    }

    private static bool BlockVoidStaticPrefix(object[] __args)
    {
        return !ShouldBlock(null, __args);
    }

    // AI behaviour 的 execute 往往返回 WorldBox 自定义枚举。这里不猜枚举值，
    // 只负责跳过原方法；Harmony 会保留该返回类型的默认值。真正需要的结果是
    // “不执行创建 City 的副作用”，而不是替代原生行为调度。
    private static bool BlockGenericInstancePrefix(object __instance, object[] __args)
    {
        return !ShouldBlock(__instance, __args);
    }

    private static bool BlockGenericStaticPrefix(object[] __args)
    {
        return !ShouldBlock(null, __args);
    }

    private static bool ShouldBlock(object instance, object[] args)
    {
        if (!XjZhantanlinSystem.IsPlaced) return false;

        if (instance is Actor actorInstance && XjZhantanlinSystem.ShouldBlockNativeSettlement(actorInstance, null, null))
            return true;
        if (instance is City cityInstance && XjZhantanlinSystem.ShouldBlockNativeSettlement(null, null, cityInstance))
            return true;

        if (args == null) return false;
        Actor actor = null;
        WorldTile tile = null;
        City city = null;
        for (int i = 0; i < args.Length; i++)
        {
            object value = args[i];
            if (actor == null && value is Actor foundActor) actor = foundActor;
            else if (tile == null && value is WorldTile foundTile) tile = foundTile;
            else if (city == null && value is City foundCity) city = foundCity;
        }
        return XjZhantanlinSystem.ShouldBlockNativeSettlement(actor, tile, city);
    }
}
