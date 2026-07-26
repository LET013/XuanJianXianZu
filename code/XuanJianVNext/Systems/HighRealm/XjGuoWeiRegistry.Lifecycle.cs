using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjGuoWeiRegistry
{	
		private static bool RemoveOtherActiveClaimsForActor(long actorId, string keepKey)
		{
			if (actorId <= 0L || activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}
	
			List<string> removeKeys = null;
			foreach (KeyValuePair<string, XjGuoWeiRegistryEntry> pair in activeEntriesByGuoWei)
			{
				if (pair.Value.ActorId != actorId || string.Equals(pair.Key, keepKey, StringComparison.Ordinal))
				{
					continue;
				}
	
				removeKeys ??= new List<string>();
				removeKeys.Add(pair.Key);
			}
	
			if (removeKeys == null)
			{
				return false;
			}
	
			for (int i = 0; i < removeKeys.Count; i++)
			{
				activeEntriesByGuoWei.Remove(removeKeys[i]);
			}
			return true;
		}

		internal static bool ReconcileLiveActorReadOnly(Actor actor)
		{
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return false;
			}
	
			XjJinDanState state = XjJinDanAccessor.BuildState(actor);
			if (!state.Found || string.IsNullOrWhiteSpace(state.GuoWei))
			{
				return false;
			}
	
			long actorId = ((BaseSystemData)actor.data).id;
			if (actorId <= 0L)
			{
				return false;
			}
	
			string key = NormalizeKey(state.GuoWei);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
				&& occupied.Found
				&& occupied.ActorId > 0L
				&& occupied.ActorId != actorId)
			{
				return false;
			}
	
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			if (!IsQingXuanTypeAllowed(daoTu, ResolveTypeFromName(state.GuoWei)))
			{
				return false;
			}
			XjGuoWeiRegistryEntry entry = new XjGuoWeiRegistryEntry(
				true,
				actorId,
				actor.getName(),
				ResolveFamilyName(actor),
				Normalize(daoTu),
				Normalize(state.JinXing),
				Normalize(state.GuoWei),
				state.SuccessYear,
				StatusActive,
				0,
				string.Empty);
	
			activeEntriesByGuoWei[key] = entry;
			if (!historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry historical)
				|| !historical.Found
				|| historical.IsActive)
			{
				historyEntriesByActorId[actorId] = entry;
			}
			return true;
		}

		internal static void ReconcileLiveActor(Actor actor)
		{
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}
	
			XjJinDanState state = XjJinDanAccessor.BuildState(actor);
			if (!state.Found)
			{
				return;
			}
	
			long actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			if (TryClaim(actor, daoTu, state.JinXing, state.GuoWei, state.SuccessYear))
			{
				return;
			}
	
			string preferredType = ResolveTypeFromName(state.GuoWei);
			if (!TryResolveAvailableGuoWei(
					daoTu,
					preferredType,
					actorId,
					actorId + Math.Max(0, state.SuccessYear),
					true,
					out _,
					out string replacement))
			{
				return;
			}
	
			if (!TryClaim(actor, daoTu, state.JinXing, replacement, state.SuccessYear))
			{
				return;
			}
	
			XjJinDanAccessor.WriteSuccess(actor, state.JinXing, replacement, state.SuccessYear);
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, daoTu, replacement, state.SuccessYear);
		}

		internal static void ReleaseForActor(long actorId, string guoWei)
		{
			if (actorId <= 0L || string.IsNullOrWhiteSpace(guoWei))
			{
				return;
			}
	
			bool changed = false;
			string key = NormalizeKey(guoWei);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry entry)
				&& entry.ActorId == actorId)
			{
				activeEntriesByGuoWei.Remove(key);
				changed = true;
			}
	
			if (historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry historical)
				&& historical.IsActive
				&& string.Equals(NormalizeKey(historical.GuoWei), key, StringComparison.Ordinal))
			{
				historyEntriesByActorId.Remove(actorId);
				changed = true;
			}
	
			if (changed)
			{
				Touch(protectedCommit: false);
			}
		}

		internal static void ReleaseFromSnapshot(XjDeathSnapshot snapshot)
		{
			if (!snapshot.Found || snapshot.ActorId <= 0L)
			{
				return;
			}
	
			bool changed = RemoveAllActiveClaimsForActor(snapshot.ActorId);
			if (!historyEntriesByActorId.TryGetValue(snapshot.ActorId, out XjGuoWeiRegistryEntry entry))
			{
				entry = new XjGuoWeiRegistryEntry(
					true,
					snapshot.ActorId,
					snapshot.Name,
					string.Empty,
					snapshot.DaoTu,
					snapshot.JinXing,
					snapshot.GuoWei,
					0,
					StatusActive,
					0,
					string.Empty);
			}
	
			string guoWei = string.IsNullOrWhiteSpace(entry.GuoWei) ? snapshot.GuoWei : entry.GuoWei;
			if (string.IsNullOrWhiteSpace(guoWei))
			{
				if (changed)
				{
					Touch(protectedCommit: true);
				}
				return;
			}
	
			XjGuoWeiRegistryEntry released = new XjGuoWeiRegistryEntry(
				true,
				snapshot.ActorId,
				string.IsNullOrWhiteSpace(entry.ActorName) ? snapshot.Name : entry.ActorName,
				entry.FamilyName,
				string.IsNullOrWhiteSpace(entry.DaoTu) ? snapshot.DaoTu : entry.DaoTu,
				string.IsNullOrWhiteSpace(entry.JinXing) ? snapshot.JinXing : entry.JinXing,
				guoWei,
				entry.Year,
				StatusDeceased,
				snapshot.Year,
				EndReasonDeath);
	
			if (!historyEntriesByActorId.TryGetValue(snapshot.ActorId, out XjGuoWeiRegistryEntry old)
				|| !EntriesEqual(old, released))
			{
				historyEntriesByActorId[snapshot.ActorId] = released;
				changed = true;
			}
	
			if (changed)
			{
				Touch(protectedCommit: true);
			}
		}

		private static bool RemoveAllActiveClaimsForActor(long actorId)
		{
			if (actorId <= 0L || activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}
	
			List<string> keys = null;
			foreach (KeyValuePair<string, XjGuoWeiRegistryEntry> pair in activeEntriesByGuoWei)
			{
				if (pair.Value.ActorId != actorId)
				{
					continue;
				}
	
				keys ??= new List<string>();
				keys.Add(pair.Key);
			}
	
			if (keys == null)
			{
				return false;
			}
	
			for (int i = 0; i < keys.Count; i++)
			{
				activeEntriesByGuoWei.Remove(keys[i]);
			}
			return true;
		}
}

