using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectGovernanceRuntimeLane
{		private static long ReadActorSectId(Actor actor)
		{
			return XjSectRepository.ResolveActorSectId(actor);
		}

		private static bool IsAtLeastZiFu(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return false;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			return XjRealmHelper.GetOrder(realmId) >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu);
		}

		private static bool IsStrictZhuJiLate(Actor actor)
		{
			if (actor?.data == null || !actor.isAlive()) return false;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (!string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhuJi, StringComparison.Ordinal)) return false;
			float zhenYuan = ReadFloat(actor, XjActorDataKeys.ZhenYuan);
			if (zhenYuan >= 24000f) return true;
			int xianJiCount = ReadInt(actor, XjActorDataKeys.XjXianJiCount);
			return string.Equals(XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, 0), "筑基后期", StringComparison.Ordinal);
		}

		private static float ReadFloat(Actor actor, string key)
		{
			return XjActorAccessor.TryGetFloat(actor, key, out float value) ? value : 0f;
		}

		private static int ReadInt(Actor actor, string key)
		{
			return XjActorAccessor.TryGetInt(actor, key, out int value) ? value : 0;
		}

		private static string SafeActorName(Actor actor)
		{
			try
			{
				string name = actor?.getName();
				return string.IsNullOrWhiteSpace(name) ? "未名真人" : name.Trim();
			}
			catch (System.Exception xjCaught61_1) {
				XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Sect/XjSectGovernanceRuntimeLane.Utilities.cs:61", xjCaught61_1);
				 return "未名真人"; }
		}

		private static City ResolveCity(long cityId)
		{
			if (cityId <= 0L) return null;
			return XjWorldLookupIndex.TryResolveCity(cityId, out City city) ? city : null;
		}

		private static long ResolveSectIdForCity(City city)
		{
			if (city?.data == null) return 0L;
			long citySectId = XjSectOwnership.ResolveSectId(city);
			return citySectId > 0L && XjSectRepository.TryGetBySectId(citySectId, out _) ? citySectId : 0L;
		}

		private static int RealmWeight(int order)
		{
			if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)) return 2000;
			if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu)) return 800;
			if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZhuJi)) return 300;
			if (order >= XjRealmHelper.GetOrder(XjRealmIds.LianQi)) return 120;
			return 10;
		}
}

