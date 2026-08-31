using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace XuanJianVNext.Interop.WorldBox.DengMingShi;

/// <summary>
/// WorldBox ActorData deep-copy serializer contract used by 登名石.
/// Non-public native serialization knowledge stays at the interop boundary.
/// </summary>
internal sealed class XjNativeActorDataContractResolver : DefaultContractResolver
{
    protected override List<MemberInfo> GetSerializableMembers(Type objectType)
    {
        List<MemberInfo> serializableMembers = DeduplicateMembers(base.GetSerializableMembers(objectType));
        HashSet<string> knownNames = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < serializableMembers.Count; i++)
        {
            string memberName = GetSerializedMemberName(serializableMembers[i]);
            if (!string.IsNullOrWhiteSpace(memberName)) knownNames.Add(memberName);
        }

        MemberInfo[] members = objectType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        for (int i = 0; i < members.Length; i++)
        {
            MemberInfo member = members[i];
            if ((member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                || serializableMembers.Contains(member)
                || member.GetCustomAttribute<JsonIgnoreAttribute>(true) != null)
            {
                continue;
            }
            if (member is PropertyInfo property
                && (property.GetIndexParameters().Length != 0
                    || (property.GetGetMethod(true) == null && property.GetSetMethod(true) == null)))
            {
                continue;
            }
            string memberName = GetSerializedMemberName(member);
            if (string.IsNullOrWhiteSpace(memberName) || !knownNames.Add(memberName)) continue;
            serializableMembers.Add(member);
        }
        return serializableMembers;
    }

    private static List<MemberInfo> DeduplicateMembers(List<MemberInfo> members)
    {
        List<MemberInfo> result = new List<MemberInfo>();
        HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < members.Count; i++)
        {
            string name = GetSerializedMemberName(members[i]);
            if (!string.IsNullOrWhiteSpace(name) && names.Add(name)) result.Add(members[i]);
        }
        return result;
    }

    private static string GetSerializedMemberName(MemberInfo member)
    {
        if (member == null) return string.Empty;
        JsonPropertyAttribute property = member.GetCustomAttribute<JsonPropertyAttribute>(true);
        return string.IsNullOrWhiteSpace(property?.PropertyName) ? member.Name : property.PropertyName;
    }
}
