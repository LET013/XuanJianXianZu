using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.ActorSystem;

internal static class XjActorAccessor
{
	private static readonly HashSet<string> AllowedKeys = BuildAllowedKeys();

	internal static bool TryGetString(Actor actor, string key, out string value)
	{
		value = string.Empty;
		if (!TryGetData(actor, key, out BaseSystemData data)) return false;
		try
		{
			data.get(key, out value, string.Empty);
			return !string.IsNullOrEmpty(value);
		}
		catch
		{
			value = string.Empty;
			return false;
		}
	}

	internal static void SetString(Actor actor, string key, string value)
	{
		if (CanUse(actor, key))
		{
			XjActorStateWriteGateway.SetString(actor, key, value);
		}
	}

	internal static bool TryGetFloat(Actor actor, string key, out float value)
	{
		value = 0f;
		if (!TryGetData(actor, key, out BaseSystemData data)) return false;
		try
		{
			data.get(key, out value, 0f);
			return true;
		}
		catch
		{
			value = 0f;
			return false;
		}
	}

	internal static void SetFloat(Actor actor, string key, float value)
	{
		if (CanUse(actor, key))
		{
			XjActorStateWriteGateway.SetFloat(actor, key, value);
		}
	}

	internal static bool TryGetInt(Actor actor, string key, out int value)
	{
		value = 0;
		if (!TryGetData(actor, key, out BaseSystemData data)) return false;
		try
		{
			data.get(key, out value, 0);
			return true;
		}
		catch
		{
			value = 0;
			return false;
		}
	}

	internal static void SetInt(Actor actor, string key, int value)
	{
		if (CanUse(actor, key))
		{
			XjActorStateWriteGateway.SetInt(actor, key, value);
		}
	}

	internal static bool TryGetLong(Actor actor, string key, out long value)
	{
		value = 0L;
		if (!TryGetData(actor, key, out BaseSystemData data)) return false;
		try
		{
			data.get(key, out value, 0L);
			return true;
		}
		catch
		{
			value = 0L;
			return false;
		}
	}

	internal static void SetLong(Actor actor, string key, long value)
	{
		if (CanUse(actor, key))
		{
			XjActorStateWriteGateway.SetLong(actor, key, value);
		}
	}

	private static bool CanUse(Actor actor, string key)
	{
		return TryGetData(actor, key, out _);
	}

	private static bool TryGetData(Actor actor, string key, out BaseSystemData data)
	{
		data = actor?.data as BaseSystemData;
		return data != null && !string.IsNullOrEmpty(key) && AllowedKeys.Contains(key);
	}

	private static HashSet<string> BuildAllowedKeys()
	{
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		FieldInfo[] fields = typeof(XjActorDataKeys).GetFields(BindingFlags.Public | BindingFlags.Static);
		for (int i = 0; i < fields.Length; i++)
		{
			if (fields[i].FieldType == typeof(string)
				&& fields[i].GetValue(null) is string value
				&& !string.IsNullOrEmpty(value))
			{
				keys.Add(value);
			}
		}
		return keys;
	}

}
