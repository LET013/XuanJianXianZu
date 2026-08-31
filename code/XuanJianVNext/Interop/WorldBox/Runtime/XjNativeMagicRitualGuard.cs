using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace XuanJianVNext.Systems.Runtime;

/// <summary>
/// 玄鉴世界禁用原生外交“魔法仪式”。
/// 版本间相关类型名曾有变化，因此不硬引用单一游戏类型：加载时只在
/// Assembly-CSharp 中发现名称明确包含 ritual/rite 的外交或魔法执行方法，
/// 并用 Harmony 阻断；同时把已注册的对应外交资产设为不可用、权重归零。
/// </summary>
internal static class XjNativeMagicRitualGuard
{
    private static readonly string[] RitualTokens =
    {
        "ritual", "magicrite", "magic_rite", "magicalrite", "magical_rite",
        "magicritual", "magic_ritual", "magic ritual", "魔法仪式"
    };
    private static readonly string[] ExecutionTokens = { "cast", "execute", "perform", "start", "launch", "apply", "process", "run", "try", "use", "create" };
    private static readonly List<object> DisabledAssets = new List<object>(16);
    private static bool _initialized;
    private static int _discoveryPasses;

    // Asset registration is a mod-load concern, not a permanent annual gameplay system.
    // Init performs pass 1; at most two later annual registration rechecks are allowed.
    internal static bool NeedsRegistrationRecheck => _discoveryPasses < 3;

    internal static void Init(Harmony harmony)
    {
        if (_initialized) return;
        _initialized = true;
        PatchExecutionMethods(harmony);
        EnsureDisabled(true);
    }

    internal static void EnsureDisabled(bool force = false)
    {
        if (!force && _discoveryPasses >= 3) return;

        // 原生资产通常只在模组启动期分批注册。允许固定三次发现窗口，
        // 之后永久退出年度控制面；执行方法的 Harmony 门禁继续生效。
        if (_discoveryPasses < 3)
        {
            DiscoverAndDisableAssets();
        }
        for (int i = DisabledAssets.Count - 1; i >= 0; i--)
        {
            object asset = DisabledAssets[i];
            if (asset == null)
            {
                DisabledAssets.RemoveAt(i);
                continue;
            }
            DisableAsset(asset);
        }
    }

    internal static void Clear()
    {
        // AssetManager/Harmony 都是模组生命周期对象，不随 world switch 重建。
        // 不清空发现结果，避免每换一次世界又做三轮程序集/资产扫描。
    }

    private static void PatchExecutionMethods(Harmony harmony)
    {
        if (harmony == null) return;
        MethodInfo blockVoid = AccessTools.Method(typeof(XjNativeMagicRitualGuard), nameof(BlockVoid));
        MethodInfo blockBool = AccessTools.Method(typeof(XjNativeMagicRitualGuard), nameof(BlockBool));
        HashSet<MethodBase> patched = new HashSet<MethodBase>();

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            string assemblyName = assembly.GetName().Name ?? string.Empty;
            if (assemblyName.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) < 0) continue;

            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types; }
            catch { continue; }
            if (types == null) continue;

            for (int i = 0; i < types.Length; i++)
            {
                Type type = types[i];
                if (type == null || !IsRitualRelatedType(type)) continue;
                MethodInfo[] methods;
                try { methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly); }
                catch { continue; }

                for (int j = 0; j < methods.Length; j++)
                {
                    MethodInfo method = methods[j];
                    if (!IsExecutionMethod(method) || !patched.Add(method)) continue;
                    try
                    {
                        if (method.ReturnType == typeof(void))
                            harmony.Patch(method, prefix: new HarmonyMethod(blockVoid));
                        else if (method.ReturnType == typeof(bool))
                            harmony.Patch(method, prefix: new HarmonyMethod(blockBool));
                    }
                    catch (System.Exception xjCaught109) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Runtime/XjNativeMagicRitualGuard.cs:109", xjCaught109); }
                }
            }
        }
    }

    private static bool IsRitualRelatedType(Type type)
    {
        string name = ((type.FullName ?? type.Name) ?? string.Empty).ToLowerInvariant();
        bool ritual = ContainsAny(name, RitualTokens) || (name.Contains("rite") && name.Contains("magic"));
        bool diplomacyMagic = name.Contains("diplom") && (name.Contains("magic") || name.Contains("spell"));
        return ritual || diplomacyMagic;
    }

    private static bool IsExecutionMethod(MethodInfo method)
    {
        if (method == null || method.IsAbstract || method.ContainsGenericParameters || method.IsSpecialName) return false;
        if (method.ReturnType != typeof(void) && method.ReturnType != typeof(bool)) return false;
        string name = method.Name.ToLowerInvariant();
        return ContainsAny(name, ExecutionTokens) || ContainsAny(name, RitualTokens) || (name.Contains("rite") && name.Contains("magic"));
    }

    private static bool BlockVoid() => false;

    private static bool BlockBool(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static void DiscoverAndDisableAssets()
    {
        Type assetManagerType = AccessTools.TypeByName("AssetManager");
        if (assetManagerType == null) return;
        _discoveryPasses++;
        HashSet<object> seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < DisabledAssets.Count; i++) if (DisabledAssets[i] != null) seen.Add(DisabledAssets[i]);

        FieldInfo[] fields;
        try { fields = assetManagerType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static); }
        catch { return; }
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            string fieldName = (field.Name ?? string.Empty).ToLowerInvariant();
            if (!fieldName.Contains("diplom")
                && !fieldName.Contains("ritual")
                && !fieldName.Contains("rite")
                && !fieldName.Contains("plot")
                && !fieldName.Contains("magic")
                && !fieldName.Contains("action")
                && !fieldName.Contains("power")) continue;
            object container;
            try { container = field.GetValue(null); } catch { continue; }
            ScanContainer(container, seen);
        }
    }

    private static void ScanContainer(object container, HashSet<object> seen)
    {
        if (container == null) return;
        if (container is IEnumerable enumerable && container is not string)
        {
            int count = 0;
            foreach (object item in enumerable)
            {
                if (count++ >= 4096) break;
                InspectCandidate(UnwrapDictionaryEntry(item), seen);
            }
            return;
        }

        Type type = container.GetType();
        foreach (string memberName in new[] { "list", "assets", "dict", "data", "values" })
        {
            object value = ReadMember(container, type, memberName);
            if (value is not IEnumerable nested || value is string) continue;
            int count = 0;
            foreach (object item in nested)
            {
                if (count++ >= 4096) break;
                InspectCandidate(UnwrapDictionaryEntry(item), seen);
            }
        }
    }

    private static object UnwrapDictionaryEntry(object item)
    {
        if (item is DictionaryEntry entry) return entry.Value;
        if (item == null) return null;
        Type type = item.GetType();
        if (type.IsGenericType && type.Name.StartsWith("KeyValuePair", StringComparison.Ordinal))
        {
            try { return type.GetProperty("Value")?.GetValue(item); } catch (System.Exception xjCaught202) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Runtime/XjNativeMagicRitualGuard.cs:202", xjCaught202); }
        }
        return item;
    }

    private static void InspectCandidate(object asset, HashSet<object> seen)
    {
        if (asset == null || seen.Contains(asset)) return;
        string identity = BuildIdentity(asset);
        string lower = identity.ToLowerInvariant();
        bool isRitual = ContainsAny(lower, RitualTokens) || (lower.Contains("rite") && lower.Contains("magic"));
        if (!isRitual) return;
        seen.Add(asset);
        DisabledAssets.Add(asset);
        DisableAsset(asset);
    }

    private static string BuildIdentity(object asset)
    {
        Type type = asset.GetType();
        string text = type.FullName ?? type.Name ?? string.Empty;
        foreach (string name in new[] { "id", "name", "type", "category", "path_icon", "action", "title" })
        {
            object value = ReadMember(asset, type, name);
            if (value != null) text += "|" + value;
        }
        return text;
    }

    private static void DisableAsset(object asset)
    {
        if (asset == null) return;
        Type type = asset.GetType();
        foreach (string name in new[] { "enabled", "active", "allowed", "can_use", "canUse", "can_ai_use", "canAIUse", "usable" })
            TryWriteMember(asset, type, name, false);
        foreach (string name in new[] { "chance", "weight", "rate", "priority", "probability" })
        {
            TryWriteMember(asset, type, name, 0);
            TryWriteMember(asset, type, name, 0f);
        }
    }

    private static object ReadMember(object target, Type type, string name)
    {
        try
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null) return field.GetValue(target);
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property != null && property.CanRead ? property.GetValue(target) : null;
        }
        catch { return null; }
    }

    private static void TryWriteMember(object target, Type type, string name, object value)
    {
        try
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field != null && !field.IsInitOnly && value != null && field.FieldType.IsAssignableFrom(value.GetType()))
            {
                field.SetValue(target, value);
                return;
            }
            PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (property != null && property.CanWrite && value != null && property.PropertyType.IsAssignableFrom(value.GetType()))
                property.SetValue(target, value);
        }
        catch (System.Exception xjCaught270) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Runtime/XjNativeMagicRitualGuard.cs:270", xjCaught270); }
    }

    private static bool ContainsAny(string value, string[] tokens)
    {
        if (string.IsNullOrEmpty(value)) return false;
        for (int i = 0; i < tokens.Length; i++)
            if (value.IndexOf(tokens[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
        public new bool Equals(object x, object y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
