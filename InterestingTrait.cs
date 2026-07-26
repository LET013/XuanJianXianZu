using HarmonyLib;
using XuanJianVNext.Traits;
using XuanJianVNext.Patches;
using XuanJianVNext.UI;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Interop;
using NeoModLoader.api;
using UnityEngine;
using System;
using System.Linq;
using System.Reflection;

namespace XuanJianVNext
{
    internal class XuanJianMod : BasicMod<XuanJianMod>
    {
        public static string id = "shiyue.worldbox.mod.XuanJian";

        protected override void OnModLoad()
        {
            try
            {
                XjLocalizationRuntimeMarker.MarkLoaded();
                UnityEngine.Debug.Log("Applying Harmony patches...");
                Harmony harmony = new Harmony(id);
                XjNativeMagicRitualGuard.Init(harmony);
                ApplyHarmonyPatchesSafely(harmony, typeof(XjVNextPatches));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjRealmTraitGrantPatches));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjAptitudeTraitPatches));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjIdentityTraitPatches));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjStunGuard));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjKnockbackGuard));
                ApplyHarmonyPatchesSafely(harmony, typeof(XjVNextDebugTraitHandler));
                UnityEngine.Debug.Log("Harmony patches applied successfully.");

                UnityEngine.Debug.Log("Initializing VNext SafeCore...");
                XjSafeCore.Init();
                UnityEngine.Debug.Log("VNext SafeCore initialized.");

				XjRuntimeSettings.LoadFromModConfig(GetConfig());

                UnityEngine.Debug.Log("Registering VNext display assets...");
                XjVNextAssetRegistration.Init();
                XjAlchemyCatalog.Init();
                XjAlchemyStatusAssets.Init();
                XjLongShuSystem.Init();
                XjLongShuDongTianSystem.Init();
                XjWorldHistoryRegistry.Init();
                UnityEngine.Debug.Log("VNext display assets registered.");

                UnityEngine.Debug.Log("Registering VNext eras...");
                XjEraAssetRegistration.Init();
                UnityEngine.Debug.Log("VNext eras registered.");

                XjDongTianEntitySystem.Init();

                UnityEngine.Debug.Log("Initializing UI system...");
                XjVNextUiManager.Init();
                XjFpsOverlay.SetEnabled(XjRuntimeSettings.ShowFpsOverlayEnabled);
                UnityEngine.Debug.Log("UI system initialized.");

                UnityEngine.Debug.Log("VNext runtime and UI initialization completed.");
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"Error during mod loading: {ex}");
            }
        }

        private static void ApplyHarmonyPatchesSafely(Harmony harmony, Type patchType)
        {
            if (harmony == null || patchType == null)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            MethodInfo[] methods = patchType.GetMethods(flags);
            int patchedCount = 0;
            int skippedCount = 0;
            int failedCount = 0;

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                bool isPrefix = method.GetCustomAttributes(typeof(HarmonyPrefix), true).Any();
                bool isPostfix = method.GetCustomAttributes(typeof(HarmonyPostfix), true).Any();
                bool isTranspiler = method.GetCustomAttributes(typeof(HarmonyTranspiler), true).Any();
                bool isFinalizer = method.GetCustomAttributes(typeof(HarmonyFinalizer), true).Any();
                bool hasPatchTarget = method.GetCustomAttributes(typeof(HarmonyPatch), true).Any();

                if ((!isPrefix && !isPostfix && !isTranspiler && !isFinalizer) || !hasPatchTarget)
                {
                    continue;
                }

                try
                {
                    MethodBase original = ResolveOriginalMethod(method);
                    if (original == null)
                    {
                        skippedCount++;
                        UnityEngine.Debug.LogWarning($"[玄鉴][Harmony] 跳过补丁(未找到目标方法): {method.DeclaringType?.FullName}.{method.Name}");
                        continue;
                    }

                    var processor = harmony.CreateProcessor(original);
                    var patchMethod = new HarmonyMethod(method);
                    if (isPrefix)
                    {
                        processor.AddPrefix(patchMethod);
                    }
                    if (isPostfix)
                    {
                        processor.AddPostfix(patchMethod);
                    }
                    if (isTranspiler)
                    {
                        processor.AddTranspiler(patchMethod);
                    }
                    if (isFinalizer)
                    {
                        processor.AddFinalizer(patchMethod);
                    }
                    processor.Patch();
                    patchedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    UnityEngine.Debug.LogError($"[玄鉴][Harmony] 打补丁失败: {method.DeclaringType?.FullName}.{method.Name} | {ex}");
                }
            }

            UnityEngine.Debug.Log($"[玄鉴][Harmony] 补丁完成 patched={patchedCount} skipped={skippedCount} failed={failedCount}");
        }

        private static MethodBase ResolveOriginalMethod(MethodInfo patchMethod)
        {
            MethodBase original = Harmony.GetOriginalMethod(patchMethod);
            if (original != null)
            {
                return original;
            }

            try
            {
                var infoField = typeof(HarmonyPatch).GetField("info", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (infoField == null)
                {
                    return null;
                }

                object[] patchAttrs = patchMethod.GetCustomAttributes(typeof(HarmonyPatch), true);
                for (int i = 0; i < patchAttrs.Length; i++)
                {
                    HarmonyPatch patchAttr = patchAttrs[i] as HarmonyPatch;
                    if (patchAttr == null)
                    {
                        continue;
                    }

                    HarmonyMethod target = infoField.GetValue(patchAttr) as HarmonyMethod;
                    if (target == null)
                    {
                        continue;
                    }

                    Type declaringType = target.declaringType;
                    string methodName = target.methodName;
                    if (declaringType == null || string.IsNullOrEmpty(methodName))
                    {
                        continue;
                    }

                    if (target.argumentTypes != null && target.argumentTypes.Length > 0)
                    {
                        original = AccessTools.Method(declaringType, methodName, target.argumentTypes);
                        if (original != null)
                        {
                            return original;
                        }
                    }

                    original = AccessTools.Method(declaringType, methodName);
                    if (original != null)
                    {
                        return original;
                    }

                    original = declaringType
                        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                        .FirstOrDefault(m => string.Equals(m.Name, methodName, StringComparison.Ordinal));
                    if (original != null)
                    {
                        return original;
                    }
                }
            }
            catch
            {
                return null;
            }

            return null;
        }
    }
}
