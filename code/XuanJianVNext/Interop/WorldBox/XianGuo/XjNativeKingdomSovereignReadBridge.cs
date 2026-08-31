using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// Read-only native sovereign probe. Reflection is performed only on annual dynasty maintenance,
/// cached by runtime type, and never used to mutate WorldBox political state.
/// </summary>
internal static class XjNativeKingdomSovereignReadBridge
{
	// 只探测明确“king”语义成员。宁可在某些WorldBox版本退化为成员归属校验，
	// 也不拿泛化 leader/ruler 字段误判王位并终止仙国。
	private static readonly string[] ActorMemberNames = { "king", "king_actor", "_king" };
	private static readonly string[] IdMemberNames = { "king_id", "kingID", "kingId" };
	private static readonly Dictionary<Type, MemberInfo[]> MemberCache = new Dictionary<Type, MemberInfo[]>();

	internal static bool TryResolveSovereign(Kingdom kingdom, out Actor sovereign)
	{
		sovereign = null;
		if (kingdom == null) return false;
		if (TryResolveFromObject(kingdom, out sovereign)) return true;
		if (kingdom.data != null && TryResolveFromObject(kingdom.data, out sovereign)) return true;
		return false;
	}

	private static bool TryResolveFromObject(object source, out Actor sovereign)
	{
		sovereign = null;
		if (source == null) return false;
		MemberInfo[] members = GetMembers(source.GetType());
		for (int i = 0; i < members.Length; i++)
		{
			object value;
			try { value = ReadMember(source, members[i]); }
			catch { continue; }
			if (value is Actor actor && actor?.data != null)
			{
				sovereign = actor;
				return true;
			}
			if (TryConvertLong(value, out long actorId) && actorId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor resolvedActor) && resolvedActor?.data != null)
			{
				sovereign = resolvedActor;
				return true;
			}
		}
		return false;
	}

	private static MemberInfo[] GetMembers(Type type)
	{
		if (type == null) return Array.Empty<MemberInfo>();
		if (MemberCache.TryGetValue(type, out MemberInfo[] cached)) return cached;
		List<MemberInfo> members = new List<MemberInfo>();
		BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		for (int i = 0; i < ActorMemberNames.Length; i++)
		{
			FieldInfo field = type.GetField(ActorMemberNames[i], flags);
			if (field != null) members.Add(field);
			PropertyInfo property = type.GetProperty(ActorMemberNames[i], flags);
			if (property != null && property.GetIndexParameters().Length == 0) members.Add(property);
		}
		for (int i = 0; i < IdMemberNames.Length; i++)
		{
			FieldInfo field = type.GetField(IdMemberNames[i], flags);
			if (field != null) members.Add(field);
			PropertyInfo property = type.GetProperty(IdMemberNames[i], flags);
			if (property != null && property.GetIndexParameters().Length == 0) members.Add(property);
		}
		cached = members.ToArray();
		MemberCache[type] = cached;
		return cached;
	}

	private static object ReadMember(object source, MemberInfo member)
	{
		if (member is FieldInfo field) return field.GetValue(source);
		if (member is PropertyInfo property && property.CanRead) return property.GetValue(source, null);
		return null;
	}

	private static bool TryConvertLong(object value, out long result)
	{
		result = 0L;
		if (value == null) return false;
		try
		{
			result = Convert.ToInt64(value);
			return true;
		}
		catch { return false; }
	}

	internal static void Clear()
	{
		MemberCache.Clear();
	}
}
