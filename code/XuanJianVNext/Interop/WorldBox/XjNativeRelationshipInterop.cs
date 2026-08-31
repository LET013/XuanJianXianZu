using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// WorldBox/NML版本间关系字段命名并不稳定。先读取原生BaseSystemData常见键，
/// 再用一次性缓存的反射访问器读取Actor/ActorData上的伴侣字段。只读，不改写原生关系。
/// </summary>
internal static class XjNativeRelationshipInterop
{
	private static readonly string[] DataKeys =
	{
		"lover_id", "lover", "spouse_id", "spouse", "partner_id", "partner", "mate_id", "mate"
	};

	private static readonly HashSet<string> RelationshipMemberNames = new HashSet<string>(StringComparer.Ordinal)
	{
		"lover", "loverid", "loveractor", "spouse", "spouseid", "spouseactor",
		"partner", "partnerid", "partneractor", "mate", "mateid", "mateactor",
		"soulmate", "soulmateid", "soulmateactor", "marriagepartner", "marriagepartnerid"
	};

	private static readonly Dictionary<Type, MemberInfo[]> MembersByType = new Dictionary<Type, MemberInfo[]>();
	private static long _attempts;
	private static long _resolvedByDataKey;
	private static long _resolvedByReflection;
	private static long _unresolved;

	internal static long Attempts => _attempts;
	internal static long ResolvedByDataKey => _resolvedByDataKey;
	internal static long ResolvedByReflection => _resolvedByReflection;
	internal static long Unresolved => _unresolved;

	internal static bool TryResolvePartner(Actor actor, out Actor partner)
	{
		partner = null;
		_attempts++;
		if (actor?.data == null)
		{
			_unresolved++;
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		for (int i = 0; i < DataKeys.Length; i++)
		{
			if (!TryReadDataPartner(actor, actorId, DataKeys[i], out partner)) continue;
			_resolvedByDataKey++;
			return true;
		}

		if (TryReadMembers(actor, actorId, out partner) || TryReadMembers(actor.data, actorId, out partner))
		{
			_resolvedByReflection++;
			return true;
		}
		_unresolved++;
		return false;
	}

	internal static void ClearDiagnostics()
	{
		MembersByType.Clear();
		_attempts = 0L;
		_resolvedByDataKey = 0L;
		_resolvedByReflection = 0L;
		_unresolved = 0L;
	}

	private static bool TryReadDataPartner(Actor actor, long actorId, string key, out Actor partner)
	{
		partner = null;
		try
		{
			actor.data.get(key, out long value, 0L);
			if (TryResolveValidPartner(actorId, value, out partner)) return true;
		}
		catch (System.Exception xjCaught80) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Interop/WorldBox/XjNativeRelationshipInterop.cs:80", xjCaught80); }
		try
		{
			actor.data.get(key, out int value, 0);
			if (TryResolveValidPartner(actorId, value, out partner)) return true;
		}
		catch (System.Exception xjCaught88) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Interop/WorldBox/XjNativeRelationshipInterop.cs:88", xjCaught88); }
		try
		{
			actor.data.get(key, out string raw, string.Empty);
			if (long.TryParse(raw, out long value) && TryResolveValidPartner(actorId, value, out partner)) return true;
		}
		catch (System.Exception xjCaught96) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Interop/WorldBox/XjNativeRelationshipInterop.cs:96", xjCaught96); }
		return false;
	}

	private static bool IsSupportedValueType(Type type)
	{
		return type == typeof(Actor) || type == typeof(long) || type == typeof(int)
			|| type == typeof(ulong) || type == typeof(string);
	}

	private static bool TryReadMembers(object source, long actorId, out Actor partner)
	{
		partner = null;
		if (source == null) return false;
		MemberInfo[] members = ResolveMembers(source.GetType());
		for (int i = 0; i < members.Length; i++)
		{
			object value;
			try
			{
				value = members[i] is FieldInfo field ? field.GetValue(source)
					: members[i] is PropertyInfo property && property.GetIndexParameters().Length == 0 ? property.GetValue(source, null)
					: null;
			}
			catch
			{
				continue;
			}
			if (TryResolveValue(actorId, value, out partner)) return true;
		}
		return false;
	}

	private static MemberInfo[] ResolveMembers(Type type)
	{
		if (type == null) return Array.Empty<MemberInfo>();
		if (MembersByType.TryGetValue(type, out MemberInfo[] cached)) return cached;
		List<MemberInfo> result = new List<MemberInfo>();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			FieldInfo[] fields = type.GetFields(flags);
			for (int i = 0; i < fields.Length; i++)
			{
				if (IsRelationshipMember(fields[i].Name) && IsSupportedValueType(fields[i].FieldType)) result.Add(fields[i]);
			}
			PropertyInfo[] properties = type.GetProperties(flags);
			for (int i = 0; i < properties.Length; i++)
			{
				PropertyInfo property = properties[i];
				if (property.CanRead && property.GetIndexParameters().Length == 0
					&& IsRelationshipMember(property.Name) && IsSupportedValueType(property.PropertyType)) result.Add(property);
			}
		}
		catch (System.Exception xjCaught152) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Interop/WorldBox/XjNativeRelationshipInterop.cs:152", xjCaught152); }
		cached = result.ToArray();
		MembersByType[type] = cached;
		return cached;
	}

	private static bool IsRelationshipMember(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return false;
		string normalized = name.Replace("_", string.Empty).ToLowerInvariant();
		return RelationshipMemberNames.Contains(normalized);
	}

	private static bool TryResolveValue(long actorId, object value, out Actor partner)
	{
		partner = null;
		if (value is Actor actor)
		{
			if (actor?.data != null && ((BaseSystemData)actor.data).id != actorId && actor.isAlive())
			{
				partner = actor;
				return true;
			}
			return false;
		}
		if (value is long longId) return TryResolveValidPartner(actorId, longId, out partner);
		if (value is int intId) return TryResolveValidPartner(actorId, intId, out partner);
		if (value is ulong ulongId && ulongId <= long.MaxValue) return TryResolveValidPartner(actorId, (long)ulongId, out partner);
		if (value is string raw && long.TryParse(raw, out long parsed)) return TryResolveValidPartner(actorId, parsed, out partner);
		return false;
	}

	private static bool TryResolveValidPartner(long actorId, long partnerId, out Actor partner)
	{
		partner = null;
		return partnerId > 0L && partnerId != actorId
			&& XjScheduler.ResolveActor(partnerId, out partner)
			&& partner?.data != null && partner.isAlive();
	}
}
