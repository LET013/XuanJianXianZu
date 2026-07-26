using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectGovernanceRuntimeLane
{		private static bool TryExtractMapPosition(object source, int depth, out Vector3 position)
		{
			position = default;
			if (source == null || depth > 3) return false;
			if (source is Vector3 vector3)
			{
				position = vector3;
				return true;
			}
			if (source is Vector2 vector2)
			{
				position = new Vector3(vector2.x, vector2.y, 0f);
				return true;
			}
			if (TryReadFloatMember(source, "x", out float x) && TryReadFloatMember(source, "y", out float y))
			{
				position = new Vector3(x, y, 0f);
				return true;
			}
			string[] memberNames =
			{
				"current_position", "position", "world_position", "pos", "posV3",
				"city_center", "center", "main_tile", "tile", "current_tile"
			};
			Type type = source.GetType();
			for (int i = 0; i < memberNames.Length; i++)
			{
				object value = ReadMemberValue(type, source, memberNames[i]);
				if (value != null && !ReferenceEquals(value, source) && TryExtractMapPosition(value, depth + 1, out position)) return true;
			}
			string[] methodNames = { "getTile", "getCenterTile", "getCityCenterTile", "GetCenterTile" };
			for (int i = 0; i < methodNames.Length; i++)
			{
				MethodInfo method = type.GetMethod(methodNames[i], ReflectionFlags, null, Type.EmptyTypes, null);
				if (method == null) continue;
				try
				{
					object value = method.Invoke(source, null);
					if (value != null && !ReferenceEquals(value, source) && TryExtractMapPosition(value, depth + 1, out position)) return true;
				}
				catch { }
			}
			return false;
		}

		private static object ReadMemberValue(Type type, object source, string name)
		{
			try
			{
				FieldInfo field = type.GetField(name, ReflectionFlags);
				if (field != null) return field.GetValue(source);
				PropertyInfo property = type.GetProperty(name, ReflectionFlags);
				if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(source, null);
			}
			catch { }
			return null;
		}

		private static bool TryReadFloatMember(object source, string name, out float value)
		{
			value = 0f;
			if (source == null) return false;
			object raw = ReadMemberValue(source.GetType(), source, name);
			if (raw == null) return false;
			try
			{
				value = Convert.ToSingle(raw);
				return true;
			}
			catch
			{
				return false;
			}
		}
}

