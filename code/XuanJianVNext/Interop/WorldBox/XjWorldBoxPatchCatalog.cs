using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Patches;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// WorldBox/NML补丁的唯一安装边界。领域系统不再自行创建Harmony实例，也不在运行时反复解析目标方法。
/// </summary>
internal static class XjWorldBoxPatchCatalog
{
    private static readonly Type[] PatchTypes =
    {
        typeof(XjVNextPatches),
        // Actor trait 生命周期只允许一个 Harmony 所有者。境界、资质、身份、
        // 阴司、剑道、调试命令等都由统一边界分发，禁止各领域再二次 Patch add/removeTrait。
        typeof(XjNativeTraitMutationPatches),
        typeof(XjHighRealmBirthPatches),
        typeof(XjNativeAiPerformancePatches),
        typeof(XjNativeTargetSafetyPatches),
        typeof(XjStunGuard),
        typeof(XjKnockbackGuard)
    };

    private static readonly RequiredMethod[] RequiredMethods =
    {
        new(typeof(MapBox), "updateSimulation"),
        new(typeof(MapBox), "finishingUpLoading"),
        new(typeof(MapBox), "clearWorld"),
        new(typeof(SaveManager), "currentWorldToSavedMap")
    };

    // 这些是玄鉴真正不可缺的四条运行边界。其余 WorldBox/NML/第三方兼容补丁
    // 都属于 fail-soft：单条失配只关闭对应功能，绝不能再次把整个模块停在
    // “core.localization 已 Ready、部分 Harmony 已安装、runtime/ui 尚未初始化”的半载入状态。
    private static readonly HashSet<string> RequiredPatchMethodNames = new(StringComparer.Ordinal)
    {
        "XuanJianVNext_MapBox_UpdateSimulation_RuntimeCadence_Postfix",
        "XuanJianVNext_MapBox_FinishingUpLoading_RuntimeSettings_Postfix",
        "XuanJianVNext_MapBox_ClearWorld_ClearCache_Postfix",
        "XuanJian_SaveManager_CurrentWorldToSavedMap_CommitArchive_Prefix"
    };

    private static readonly HashSet<string> InstalledRequiredPatchMethodNames = new(StringComparer.Ordinal);

    internal static bool IsInitialized { get; private set; }
    internal static bool RequiredReady { get; private set; }
    internal static int PatchedCount { get; private set; }
    internal static int SkippedCount { get; private set; }
    internal static int FailedCount { get; private set; }
    internal static string Summary { get; private set; } = "尚未安装";

    internal static bool ApplyAll(string harmonyId)
    {
        if (IsInitialized)
        {
            return RequiredReady;
        }

        IsInitialized = true;
        InstalledRequiredPatchMethodNames.Clear();
        RequiredReady = ValidateRequiredCapabilities();
        if (!RequiredReady)
        {
            Summary = "缺少必要的原生运行方法，本轮玄鉴逻辑不再继续载入";
            Debug.LogError("[玄鉴][兼容层] " + Summary);
            return false;
        }

        var harmony = new Harmony(harmonyId);
        ApplyOptionalInitializer("native-magic-ritual", () => XjNativeMagicRitualGuard.Init(harmony));
        ApplyOptionalInitializer("zhantanlin-native-settlement", () => XjZhantanlinNativeSettlementGuard.Init(harmony));
        ApplyOptionalInitializer("avbs-trait-pack", () => XjAvbsTraitPackInterop.Init(harmony));

        for (int i = 0; i < PatchTypes.Length; i++)
        {
            ApplyPatchType(harmony, PatchTypes[i]);
        }

        RequiredReady = RequiredPatchMethodNames.SetEquals(InstalledRequiredPatchMethodNames);
        if (!RequiredReady)
        {
            foreach (string requiredPatch in RequiredPatchMethodNames)
            {
                if (!InstalledRequiredPatchMethodNames.Contains(requiredPatch))
                {
                    Debug.LogError("[玄鉴][兼容层] 缺少核心 Harmony 边界: " + requiredPatch);
                }
            }
        }

        int optionalFailures = Math.Max(0, FailedCount - (RequiredPatchMethodNames.Count - InstalledRequiredPatchMethodNames.Count));
        Summary = $"patched={PatchedCount} skipped={SkippedCount} failed={FailedCount} required={InstalledRequiredPatchMethodNames.Count}/{RequiredPatchMethodNames.Count} optionalFailed={optionalFailures}";
        if (RequiredReady)
        {
            Debug.Log("[玄鉴][兼容层] Harmony补丁安装完成 " + Summary);
        }
        else
        {
            Debug.LogError("[玄鉴][兼容层] 核心 Harmony 边界不完整 " + Summary);
        }
        return RequiredReady;
    }

    private static void ApplyOptionalInitializer(string id, Action initializer)
    {
        try
        {
            initializer?.Invoke();
        }
        catch (Exception ex)
        {
            FailedCount++;
            Debug.LogWarning("[玄鉴][兼容层] 可选兼容初始化失败，已隔离: " + id + " | " + ex);
        }
    }

    private static bool ValidateRequiredCapabilities()
    {
        bool ready = true;
        for (int i = 0; i < RequiredMethods.Length; i++)
        {
            RequiredMethod required = RequiredMethods[i];
            MethodInfo method = AccessTools.Method(required.DeclaringType, required.MethodName);
            if (method != null)
            {
                continue;
            }

            ready = false;
            Debug.LogError($"[玄鉴][兼容层] 缺少核心原生方法: {required.DeclaringType.FullName}.{required.MethodName}");
        }

        return ready;
    }

    private static void ApplyPatchType(Harmony harmony, Type patchType)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
        MethodInfo[] methods;
        try
        {
            methods = patchType.GetMethods(flags);
        }
        catch (Exception ex)
        {
            FailedCount++;
            Debug.LogWarning($"[玄鉴][兼容层] 无法枚举可选补丁类型，已隔离: {patchType?.FullName} | {ex}");
            return;
        }

        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            try
            {
                // Harmony/第三方版本差异有时会在读取 HarmonyPatch 属性本身时抛异常。
                // 属性探测也必须位于单补丁隔离边界内，不能让一个 optional patch
                // 再次形成“部分 Patch 已安装、runtime/ui 未初始化”的半载入世界。
                bool isPrefix = method.GetCustomAttributes(typeof(HarmonyPrefix), true).Any();
                bool isPostfix = method.GetCustomAttributes(typeof(HarmonyPostfix), true).Any();
                bool isTranspiler = method.GetCustomAttributes(typeof(HarmonyTranspiler), true).Any();
                bool isFinalizer = method.GetCustomAttributes(typeof(HarmonyFinalizer), true).Any();
                bool hasPatchTarget = method.GetCustomAttributes(typeof(HarmonyPatch), true).Any();

                if ((!isPrefix && !isPostfix && !isTranspiler && !isFinalizer) || !hasPatchTarget)
                {
                    continue;
                }

                MethodBase? original = ResolveOriginalMethod(method);
                if (original == null)
                {
                    SkippedCount++;
                    if (RequiredPatchMethodNames.Contains(method.Name)) FailedCount++;
                    Debug.LogWarning($"[玄鉴][兼容层] 跳过补丁(未找到目标方法): {method.DeclaringType?.FullName}.{method.Name}");
                    continue;
                }

                PatchProcessor processor = harmony.CreateProcessor(original);
                HarmonyMethod patchMethod = new(method);
                if (isPrefix) processor.AddPrefix(patchMethod);
                if (isPostfix) processor.AddPostfix(patchMethod);
                if (isTranspiler) processor.AddTranspiler(patchMethod);
                if (isFinalizer) processor.AddFinalizer(patchMethod);
                processor.Patch();
                PatchedCount++;
                if (RequiredPatchMethodNames.Contains(method.Name))
                {
                    InstalledRequiredPatchMethodNames.Add(method.Name);
                }
            }
            catch (Exception ex)
            {
                FailedCount++;
                bool required = RequiredPatchMethodNames.Contains(method.Name);
                string message = $"[玄鉴][兼容层] 打补丁失败: {method.DeclaringType?.FullName}.{method.Name} | {ex}";
                if (required) Debug.LogError(message);
                else Debug.LogWarning(message + "（可选补丁已隔离，玄鉴继续初始化）");
            }
        }
    }

    private static MethodBase? ResolveOriginalMethod(MethodInfo patchMethod)
    {
        MethodBase? original = Harmony.GetOriginalMethod(patchMethod);
        if (original != null)
        {
            return original;
        }

        FieldInfo? infoField = typeof(HarmonyPatch).GetField("info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (infoField == null)
        {
            return null;
        }

        object[] patchAttributes = patchMethod.GetCustomAttributes(typeof(HarmonyPatch), true);
        for (int i = 0; i < patchAttributes.Length; i++)
        {
            if (patchAttributes[i] is not HarmonyPatch patchAttribute)
            {
                continue;
            }

            if (infoField.GetValue(patchAttribute) is not HarmonyMethod target ||
                target.declaringType == null || string.IsNullOrEmpty(target.methodName))
            {
                continue;
            }

            if (target.argumentTypes != null && target.argumentTypes.Length > 0)
            {
                original = AccessTools.Method(target.declaringType, target.methodName, target.argumentTypes);
                if (original != null) return original;
            }

            original = AccessTools.Method(target.declaringType, target.methodName);
            if (original != null) return original;

            original = target.declaringType
                .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .FirstOrDefault(candidate => string.Equals(candidate.Name, target.methodName, StringComparison.Ordinal));
            if (original != null) return original;
        }

        return null;
    }

    private readonly struct RequiredMethod
    {
        internal RequiredMethod(Type declaringType, string methodName)
        {
            DeclaringType = declaringType;
            MethodName = methodName;
        }

        internal Type DeclaringType { get; }
        internal string MethodName { get; }
    }
}
