using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Narrow, cached compatibility access for version-sensitive WorldBox members.
/// Business systems describe the native operation they need; reflection resolution
/// stays at the interop edge and is cached by runtime type/member signature.
/// </summary>
internal static class XjNativeReflectionInterop
{
    private const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private sealed class TypePlan
    {
        internal readonly Dictionary<string, MemberInfo> Members = new Dictionary<string, MemberInfo>(StringComparer.Ordinal);
        internal readonly Dictionary<string, MethodInfo[]> Methods = new Dictionary<string, MethodInfo[]>(StringComparer.Ordinal);
    }

    private static readonly Dictionary<Type, TypePlan> PlansByType = new Dictionary<Type, TypePlan>();

    internal static bool TryInvokeCompatible(object target, string methodName, object[] args, out object result, out string reason)
    {
        result = null;
        reason = string.Empty;
        if (target == null || string.IsNullOrWhiteSpace(methodName)) return false;

        object[] safeArgs = args ?? Array.Empty<object>();
        MethodInfo[] methods = ResolveMethods(target.GetType(), methodName, safeArgs.Length);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            ParameterInfo[] parameters = method.GetParameters();
            if (!ArgumentsMatch(parameters, safeArgs)) continue;
            try
            {
                result = method.Invoke(target, safeArgs);
                return true;
            }
            catch (TargetInvocationException ex)
            {
                reason = ex.InnerException?.GetType().Name ?? ex.GetType().Name;
                return false;
            }
            catch (Exception ex)
            {
                reason = ex.GetType().Name;
                return false;
            }
        }

        reason = methodName + "(" + safeArgs.Length + ")";
        return false;
    }

    internal static object ReadMemberValue(object target, string memberName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName)) return null;
        try
        {
            MemberInfo member = ResolveMember(target.GetType(), memberName);
            if (member is FieldInfo field) return field.GetValue(target);
            if (member is PropertyInfo property && property.CanRead && property.GetIndexParameters().Length == 0)
                return property.GetValue(target, null);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/NativeReflection.ReadMemberValue", ex);
        }
        return null;
    }

    internal static bool TryWriteMemberValue(object target, string memberName, object value)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName)) return false;
        try
        {
            MemberInfo member = ResolveMember(target.GetType(), memberName);
            if (member is FieldInfo field && !field.IsInitOnly && TryCoerceValue(value, field.FieldType, out object fieldValue))
            {
                field.SetValue(target, fieldValue);
                return true;
            }
            if (member is PropertyInfo property
                && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && TryCoerceValue(value, property.PropertyType, out object propertyValue))
            {
                property.SetValue(target, propertyValue, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/NativeReflection.TryWriteMemberValue", ex);
        }
        return false;
    }

    internal static bool TryWriteNumericMember(object target, string memberName, float value)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName)) return false;
        try
        {
            MemberInfo member = ResolveMember(target.GetType(), memberName);
            if (member is FieldInfo field && !field.IsInitOnly
                && TryConvertNumeric(value, field.FieldType, out object fieldValue))
            {
                field.SetValue(target, fieldValue);
                return true;
            }
            if (member is PropertyInfo property && property.CanWrite
                && property.GetIndexParameters().Length == 0
                && TryConvertNumeric(value, property.PropertyType, out object propertyValue))
            {
                property.SetValue(target, propertyValue, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/NativeReflection.TryWriteNumericMember", ex);
        }
        return false;
    }

    internal static bool TryWriteBooleanMember(object target, string memberName, bool value)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName)) return false;
        try
        {
            MemberInfo member = ResolveMember(target.GetType(), memberName);
            if (member is FieldInfo field && !field.IsInitOnly && field.FieldType == typeof(bool))
            {
                field.SetValue(target, value);
                return true;
            }
            if (member is PropertyInfo property && property.CanWrite
                && property.GetIndexParameters().Length == 0 && property.PropertyType == typeof(bool))
            {
                property.SetValue(target, value, null);
                return true;
            }
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("Interop/WorldBox/NativeReflection.TryWriteBooleanMember", ex);
        }
        return false;
    }

    internal static bool TryReadLongMember(object target, string memberName, out long value)
    {
        value = 0L;
        object raw = ReadMemberValue(target, memberName);
        if (raw == null) return false;
        try
        {
            value = Convert.ToInt64(raw);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static void ClearRuntimeCache()
    {
        PlansByType.Clear();
    }

    private static MethodInfo[] ResolveMethods(Type type, string methodName, int arity)
    {
        if (type == null) return Array.Empty<MethodInfo>();
        TypePlan plan = GetPlan(type);
        string key = methodName + "#" + arity;
        if (plan.Methods.TryGetValue(key, out MethodInfo[] cached)) return cached;

        MethodInfo[] allMethods = type.GetMethods(InstanceFlags);
        var matches = new List<MethodInfo>();
        for (int i = 0; i < allMethods.Length; i++)
        {
            MethodInfo method = allMethods[i];
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal)) continue;
            if (method.GetParameters().Length != arity) continue;
            matches.Add(method);
        }

        cached = matches.Count == 0 ? Array.Empty<MethodInfo>() : matches.ToArray();
        plan.Methods[key] = cached;
        return cached;
    }

    private static MemberInfo ResolveMember(Type type, string memberName)
    {
        if (type == null || string.IsNullOrWhiteSpace(memberName)) return null;
        TypePlan plan = GetPlan(type);
        if (plan.Members.TryGetValue(memberName, out MemberInfo cached)) return cached;

        MemberInfo member = type.GetField(memberName, InstanceFlags);
        if (member == null)
        {
            PropertyInfo property = type.GetProperty(memberName, InstanceFlags);
            if (property != null && property.GetIndexParameters().Length == 0) member = property;
        }
        plan.Members[memberName] = member;
        return member;
    }

    private static TypePlan GetPlan(Type type)
    {
        if (PlansByType.TryGetValue(type, out TypePlan plan)) return plan;
        plan = new TypePlan();
        PlansByType[type] = plan;
        return plan;
    }

    private static bool ArgumentsMatch(ParameterInfo[] parameters, object[] args)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameterType = parameters[i].ParameterType;
            object arg = args[i];
            if (arg == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null) return false;
                continue;
            }
            if (!parameterType.IsInstanceOfType(arg)) return false;
        }
        return true;
    }

    private static bool TryConvertNumeric(float value, Type targetType, out object converted)
    {
        converted = null;
        if (targetType == null) return false;
        Type effectiveType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (effectiveType == typeof(float)) converted = value;
            else if (effectiveType == typeof(double)) converted = (double)value;
            else if (effectiveType == typeof(int)) converted = (int)Math.Round(value);
            else if (effectiveType == typeof(long)) converted = (long)Math.Round(value);
            else if (effectiveType == typeof(short)) converted = (short)Math.Round(value);
            else if (effectiveType == typeof(byte)) converted = (byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, (int)Math.Round(value)));
            else return false;
            return true;
        }
        catch
        {
            converted = null;
            return false;
        }
    }

    private static bool TryCoerceValue(object value, Type targetType, out object coerced)
    {
        coerced = null;
        if (targetType == null) return false;
        if (value == null)
        {
            if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) == null) return false;
            return true;
        }
        if (targetType.IsInstanceOfType(value))
        {
            coerced = value;
            return true;
        }

        Type underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
        try
        {
            if (underlying == typeof(float))
            {
                coerced = Convert.ToSingle(value);
                return true;
            }
            if (underlying == typeof(double))
            {
                coerced = Convert.ToDouble(value);
                return true;
            }
            if (underlying == typeof(decimal))
            {
                coerced = Convert.ToDecimal(value);
                return true;
            }
            if (underlying == typeof(long))
            {
                coerced = Convert.ToInt64(value);
                return true;
            }
            if (underlying == typeof(int))
            {
                coerced = Convert.ToInt32(value);
                return true;
            }
            if (underlying == typeof(short))
            {
                coerced = Convert.ToInt16(value);
                return true;
            }
            if (underlying == typeof(byte))
            {
                coerced = Convert.ToByte(value);
                return true;
            }
            if (underlying == typeof(uint))
            {
                coerced = Convert.ToUInt32(value);
                return true;
            }
            if (underlying == typeof(ulong))
            {
                coerced = Convert.ToUInt64(value);
                return true;
            }
            if (underlying == typeof(string))
            {
                coerced = Convert.ToString(value);
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }
}
