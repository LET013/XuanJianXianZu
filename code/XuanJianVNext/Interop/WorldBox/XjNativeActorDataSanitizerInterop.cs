using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Save-compatibility cleanup for legacy JSON token values that leaked into
/// WorldBox ActorData dictionaries. Native field discovery stays at the interop edge.
/// </summary>
internal static class XjNativeActorDataSanitizerInterop
{
    private static readonly Dictionary<Type, FieldInfo[]> DictionaryFieldsByType = new Dictionary<Type, FieldInfo[]>();

    internal static void NormalizeLegacyJsonTokenValues(ActorData data)
    {
        if (data == null) return;
        FieldInfo[] fields = ResolveDictionaryFields(data.GetType());
        for (int i = 0; i < fields.Length; i++)
        {
            IDictionary dictionary;
            try
            {
                dictionary = fields[i].GetValue(data) as IDictionary;
            }
            catch (Exception ex)
            {
                XjExceptionDiagnostics.Report("Interop/WorldBox/ActorDataSanitizer.ReadDictionary", ex);
                continue;
            }
            if (dictionary == null || dictionary.Count == 0) continue;

            List<object> keys = new List<object>();
            foreach (object key in dictionary.Keys) keys.Add(key);
            for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
            {
                object key = keys[keyIndex];
                try
                {
                    object value = dictionary[key];
                    if (value is JValue scalar)
                    {
                        object normalized = ConvertLegacyJsonScalar(scalar);
                        if (normalized == null) dictionary.Remove(key);
                        else dictionary[key] = normalized;
                    }
                    else if (value is JToken)
                    {
                        dictionary.Remove(key);
                    }
                }
                catch (Exception ex)
                {
                    XjExceptionDiagnostics.Report("Interop/WorldBox/ActorDataSanitizer.NormalizeEntry", ex);
                }
            }
        }
    }

    internal static void ClearRuntimeCache()
    {
        DictionaryFieldsByType.Clear();
    }

    private static FieldInfo[] ResolveDictionaryFields(Type dataType)
    {
        if (dataType == null) return Array.Empty<FieldInfo>();
        if (DictionaryFieldsByType.TryGetValue(dataType, out FieldInfo[] cached)) return cached;

        List<FieldInfo> result = new List<FieldInfo>();
        for (Type type = dataType; type != null && type != typeof(object); type = type.BaseType)
        {
            FieldInfo[] fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                if (!field.IsStatic && typeof(IDictionary).IsAssignableFrom(field.FieldType)) result.Add(field);
            }
        }
        cached = result.ToArray();
        DictionaryFieldsByType[dataType] = cached;
        return cached;
    }

    private static object ConvertLegacyJsonScalar(JValue value)
    {
        if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined) return null;
        object raw = value.Value;
        if (raw == null) return null;
        try
        {
            switch (value.Type)
            {
                case JTokenType.Integer:
                    long integer = Convert.ToInt64(raw);
                    return integer >= int.MinValue && integer <= int.MaxValue ? (object)(int)integer : integer;
                case JTokenType.Float:
                    return Convert.ToSingle(raw);
                case JTokenType.Boolean:
                    return Convert.ToBoolean(raw);
                case JTokenType.String:
                case JTokenType.Guid:
                case JTokenType.Uri:
                case JTokenType.TimeSpan:
                case JTokenType.Date:
                    return Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return raw is string || raw is bool || raw is int || raw is long || raw is float ? raw : null;
            }
        }
        catch
        {
            return null;
        }
    }
}
