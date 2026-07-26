using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{		private static string SafeActorName(Actor actor)
		{
			try
			{
				string name = actor?.getName();
				return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
			}
			catch
			{
				return "未名修士";
			}
		}

		private static string EmptyText(string value, string fallback)
		{
			return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
		}

		private static string SafeDataName(BaseSystemData data, string fallback)
		{
			if (data == null) return fallback;
			try
			{
				return string.IsNullOrWhiteSpace(data.name) ? fallback : data.name.Trim();
			}
			catch
			{
				return fallback;
			}
		}
}

