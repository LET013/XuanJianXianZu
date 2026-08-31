using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Version-tolerant access to small native scalar fields used only for presentation.
/// Reflection is resolved once per runtime type and cached at the WorldBox interop edge.
/// </summary>
internal static class XjNativeScalarInterop
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly string[] HappinessLikeNames =
    {
        "happiness", "Happiness", "mood", "Mood", "emotion", "Emotion", "satisfaction", "Satisfaction"
    };
    private static readonly Dictionary<Type, MemberInfo[]> HappinessMembersByType = new Dictionary<Type, MemberInfo[]>();

    internal static bool TryReadHappinessLikeNumber(object target, out float value)
    {
        value = 0f;
        if (target == null) return false;

        Type type = target.GetType();
        if (!HappinessMembersByType.TryGetValue(type, out MemberInfo[] members))
        {
            members = ResolveMembers(type, HappinessLikeNames);
            HappinessMembersByType[type] = members;
        }

        for (int i = 0; i < members.Length; i++)
        {
            try
            {
                object raw = members[i] switch
                {
                    FieldInfo field => field.GetValue(target),
                    PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(target, null),
                    _ => null
                };
                if (TryConvertNumber(raw, out value)) return true;
            }
            catch (Exception ex)
            {
                XjExceptionDiagnostics.Report("XjNativeScalarInterop.ReadHappinessLikeNumber", ex);
            }
        }
        return false;
    }

    internal static void ClearRuntimeCache()
    {
        HappinessMembersByType.Clear();
    }

    private static MemberInfo[] ResolveMembers(Type type, IReadOnlyList<string> names)
    {
        if (type == null || names == null || names.Count == 0) return Array.Empty<MemberInfo>();
        var result = new List<MemberInfo>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            string name = names[i];
            FieldInfo field = type.GetField(name, Flags);
            if (field != null)
            {
                result.Add(field);
                continue;
            }
            PropertyInfo property = type.GetProperty(name, Flags);
            if (property != null && property.GetIndexParameters().Length == 0) result.Add(property);
        }
        return result.Count == 0 ? Array.Empty<MemberInfo>() : result.ToArray();
    }

    private static bool TryConvertNumber(object raw, out float value)
    {
        value = 0f;
        if (raw == null) return false;
        try
        {
            switch (raw)
            {
                case int i:
                    value = i;
                    return true;
                case float f:
                    value = f;
                    return true;
                case double d:
                    value = (float)d;
                    return true;
                case long l:
                    value = l;
                    return true;
                default:
                    return float.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out value);
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjNativeScalarInterop.ConvertNumber", ex);
            return false;
        }
    }
}
