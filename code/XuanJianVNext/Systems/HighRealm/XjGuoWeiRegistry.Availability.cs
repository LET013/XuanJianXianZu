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

internal enum XjGuoWeiAvailabilityReason
{
	Available,
	Occupied,
	RuleBlocked,
	Hidden,
	InvalidDaoTu,
	InvalidActor,
	InvalidType,
	RegistryConflict
}

internal static partial class XjGuoWeiRegistry
{
		internal static bool IsAvailableForActor(string guoWei, long actorId)
		{
			if (string.IsNullOrWhiteSpace(guoWei) || actorId <= 0L)
			{
				return false;
			}

			if (IsHiddenYinSiZhengWei(Normalize(guoWei)))
			{
				return false;
			}

			string normalizedGuoWei = Normalize(guoWei);
			if (string.Equals(ResolveTypeFromName(normalizedGuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				string daoTu = normalizedGuoWei.Substring(0, normalizedGuoWei.Length - XjGuoWeiCalculator.ZhengWei.Length);
				if (XjGuoWeiQuanBingRegistry.IsZhengWeiLockedForActor(daoTu, actorId, XjYearTracker.CurrentYear, out _))
				{
					return false;
				}
			}

			string key = NormalizeKey(guoWei);
			return !activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
				|| !occupied.Found
				|| occupied.ActorId <= 0L
				|| occupied.ActorId == actorId;
		}

		internal static bool TryFindActiveAnchor(string daoTu, string guoWeiType, out XjGuoWeiRegistryEntry entry)
		{
			return TryFindActiveAnchor(daoTu, guoWeiType, null, out entry);
		}

		internal static bool TryFindActiveAnchor(
			string daoTu,
			string guoWeiType,
			Func<XjGuoWeiRegistryEntry, bool> predicate,
			out XjGuoWeiRegistryEntry entry)
		{
			entry = default;
			string normalizedDaoTu = Normalize(daoTu);
			string normalizedType = Normalize(guoWeiType);
			if (string.IsNullOrWhiteSpace(normalizedDaoTu)
				|| string.IsNullOrWhiteSpace(normalizedType)
				|| activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}

			bool found = false;
			XjGuoWeiRegistryEntry best = default;
			foreach (XjGuoWeiRegistryEntry candidate in activeEntriesByGuoWei.Values)
			{
				if (!candidate.Found
					|| !candidate.IsActive
					|| candidate.ActorId <= 0L
					|| !string.Equals(candidate.DaoTu, normalizedDaoTu, StringComparison.Ordinal)
					|| !string.Equals(ResolveTypeFromName(candidate.GuoWei), normalizedType, StringComparison.Ordinal))
				{
					continue;
				}
				if (predicate != null && !predicate(candidate))
				{
					continue;
				}

				if (!found
					|| candidate.Year < best.Year
					|| (candidate.Year == best.Year && candidate.ActorId < best.ActorId))
				{
					found = true;
					best = candidate;
				}
			}

			if (!found)
			{
				return false;
			}

			entry = best;
			return true;
		}
		internal static bool TryResolveAvailableGuoWei(
			string daoTu,
			string preferredType,
			long actorId,
			long seed,
			bool allowLowerFallback,
			out string resolvedType,
			out string guoWei)
		{
			return TryResolveAvailableGuoWeiDetailed(
				daoTu,
				preferredType,
				actorId,
				seed,
				allowLowerFallback,
				out resolvedType,
				out guoWei,
				out _);
		}

		internal static bool TryResolveAvailableGuoWeiDetailed(
			string daoTu,
			string preferredType,
			long actorId,
			long seed,
			bool allowLowerFallback,
			out string resolvedType,
			out string guoWei,
			out XjGuoWeiAvailabilityReason reason)
		{
			resolvedType = string.Empty;
			guoWei = string.Empty;
			string normalizedDaoTu = Normalize(daoTu);
			if (string.IsNullOrWhiteSpace(normalizedDaoTu))
			{
				reason = XjGuoWeiAvailabilityReason.InvalidDaoTu;
				return false;
			}
			if (actorId <= 0L)
			{
				reason = XjGuoWeiAvailabilityReason.InvalidActor;
				return false;
			}

			string[] types = BuildSearchTypes(preferredType, allowLowerFallback);
			bool sawValidType = false;
			bool sawRuleBlocked = false;
			bool sawHidden = false;
			bool sawRegistryConflict = false;
			int eligibleSlotCount = 0;
			int occupiedSlotCount = 0;
			for (int typeIndex = 0; typeIndex < types.Length; typeIndex++)
			{
				string type = Normalize(types[typeIndex]);
				int slotCount = ResolveSlotCount(type);
				if (slotCount <= 0)
				{
					continue;
				}
				sawValidType = true;
				if (!IsQingXuanTypeAllowed(normalizedDaoTu, type))
				{
					sawRuleBlocked = true;
					continue;
				}
				if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
					&& XjGuoWeiQuanBingRegistry.IsZhengWeiLockedForActor(normalizedDaoTu, actorId, XjYearTracker.CurrentYear, out _))
				{
					sawRuleBlocked = true;
					continue;
				}

				int start = slotCount <= 1
					? 1
					: XjDeterministicHash.PositiveIndex(seed + typeIndex * 1009L, normalizedDaoTu + "|" + type, slotCount) + 1;
				for (int offset = 0; offset < slotCount; offset++)
				{
					int slot = ((start - 1 + offset) % slotCount) + 1;
					string candidate = XjGuoWeiCalculator.BuildGuoWeiSlotName(normalizedDaoTu, type, slot);
					if (string.Equals(candidate, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
					{
						sawRegistryConflict = true;
						continue;
					}
					if (IsHiddenYinSiZhengWei(normalizedDaoTu, type, candidate))
					{
						sawHidden = true;
						continue;
					}

					eligibleSlotCount++;
					string key = NormalizeKey(candidate);
					if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
						&& occupied.Found
						&& occupied.IsActive
						&& occupied.ActorId > 0L
						&& occupied.ActorId != actorId)
					{
						occupiedSlotCount++;
						continue;
					}
					if (TryFindActiveTypeConflict(type, normalizedDaoTu, actorId, out _, out _))
					{
						occupiedSlotCount++;
						continue;
					}
					if (!IsAvailableForActor(candidate, actorId))
					{
						sawRegistryConflict = true;
						continue;
					}

					resolvedType = type;
					guoWei = candidate;
					reason = XjGuoWeiAvailabilityReason.Available;
					return true;
				}
			}

			if (eligibleSlotCount > 0 && occupiedSlotCount == eligibleSlotCount)
			{
				reason = XjGuoWeiAvailabilityReason.Occupied;
			}
			else if (sawRegistryConflict)
			{
				reason = XjGuoWeiAvailabilityReason.RegistryConflict;
			}
			else if (sawRuleBlocked)
			{
				reason = XjGuoWeiAvailabilityReason.RuleBlocked;
			}
			else if (sawHidden)
			{
				reason = XjGuoWeiAvailabilityReason.Hidden;
			}
			else
			{
				reason = sawValidType
					? XjGuoWeiAvailabilityReason.RegistryConflict
					: XjGuoWeiAvailabilityReason.InvalidType;
			}
			return false;
		}

		private static bool IsTypeAvailableForActor(string type, string daoTu, long actorId, string guoWei)
		{
			if (!IsQingXuanTypeAllowed(daoTu, type))
			{
				return false;
			}
			if (IsHiddenYinSiZhengWei(Normalize(daoTu), Normalize(type), Normalize(guoWei)))
			{
				return false;
			}
			if (!IsAvailableForActor(guoWei, actorId))
			{
				return false;
			}

			return !TryFindActiveTypeConflict(type, daoTu, actorId, out _, out _);
		}

		private static bool IsQingXuanTypeAllowed(string daoTu, string type)
		{
			if (!string.Equals(Normalize(daoTu), "青宣", StringComparison.Ordinal))
			{
				return true;
			}

			// 青宣为空证：没有闰位；余位必须由已在世的青宣正位开启。
			if (string.Equals(Normalize(type), XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				return false;
			}
			return !string.Equals(Normalize(type), XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				|| HasManifestedZhengWeiDaoTu("青宣");
		}

		internal static bool IsHiddenYinSiZhengWei(string daoTu, string type, string guoWei)
		{
			return YinSiHiddenDaoTus.Contains(Normalize(daoTu))
				&& string.Equals(Normalize(type), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				&& IsHiddenYinSiZhengWei(guoWei);
		}

		internal static bool IsHiddenYinSiZhengWei(string guoWei)
		{
			string normalized = Normalize(guoWei);
			if (string.IsNullOrWhiteSpace(normalized)
				|| !normalized.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				return false;
			}
			foreach (string daoTu in YinSiHiddenDaoTus)
			{
				if (normalized.StartsWith(daoTu, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static bool TryFindActiveTypeConflict(
			string type,
			string daoTu,
			long actorId,
			out string conflictKey,
			out XjGuoWeiRegistryEntry conflict)
		{
			conflictKey = string.Empty;
			conflict = default;
			string normalizedType = Normalize(type);
			string normalizedDaoTu = Normalize(daoTu);
			if (!string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				|| string.IsNullOrWhiteSpace(normalizedDaoTu)
				|| activeEntriesByGuoWei.Count == 0)
			{
				return false;
			}

			foreach (KeyValuePair<string, XjGuoWeiRegistryEntry> pair in activeEntriesByGuoWei)
			{
				XjGuoWeiRegistryEntry entry = pair.Value;
				if (!entry.Found
					|| !entry.IsActive
					|| entry.ActorId <= 0L
					|| entry.ActorId == actorId
					|| !string.Equals(entry.DaoTu, normalizedDaoTu, StringComparison.Ordinal)
					|| !string.Equals(ResolveTypeFromName(entry.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
				{
					continue;
				}

				conflictKey = pair.Key;
				conflict = entry;
				return true;
			}

			return false;
		}


		internal static bool HasManifestedDaoTu(string daoTu)
		{
			string normalized = Normalize(daoTu);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				return false;
			}

			foreach (XjGuoWeiRegistryEntry entry in activeEntriesByGuoWei.Values)
			{
				if (entry.Found
					&& entry.ActorId > 0L
					&& !string.IsNullOrWhiteSpace(entry.GuoWei)
					&& string.Equals(entry.DaoTu, normalized, StringComparison.Ordinal))
				{
					return true;
				}
			}

			return false;
		}

		internal static bool HasManifestedZhengWeiDaoTu(string daoTu)
		{
			string normalized = Normalize(daoTu);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				return false;
			}

			foreach (XjGuoWeiRegistryEntry entry in activeEntriesByGuoWei.Values)
			{
				if (entry.Found
					&& entry.ActorId > 0L
					&& string.Equals(entry.DaoTu, normalized, StringComparison.Ordinal)
					&& entry.GuoWei.IndexOf(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}

			return false;
		}

		private static string[] BuildSearchTypes(string preferredType, bool allowLowerFallback)
		{
			string normalized = Normalize(preferredType);
			if (!allowLowerFallback)
			{
				return new[] { normalized };
			}

			if (string.Equals(normalized, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				return new[] { XjGuoWeiCalculator.ZhengWei, XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei };
			}
			if (string.Equals(normalized, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
			{
				return new[] { XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei };
			}
			if (string.Equals(normalized, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				return new[] { XjGuoWeiCalculator.RunWei, XjGuoWeiCalculator.YuWei };
			}

			return new[] { XjGuoWeiCalculator.YuWei, XjGuoWeiCalculator.RunWei };
		}

		private static int ResolveSlotCount(string type)
		{
			if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				return 1;
			}
			if (string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
			{
				return XjGuoWeiQuanBingRules.YuWeiSlotCount;
			}
			if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				return XjGuoWeiQuanBingRules.RunWeiSlotCount;
			}
			return 0;
		}

		internal static string ResolveTypeFromName(string guoWei)
		{
			string normalized = Normalize(guoWei);
			if (normalized.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				return XjGuoWeiCalculator.ZhengWei;
			}
			if (normalized.EndsWith(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
			{
				return XjGuoWeiCalculator.YuWei;
			}
			if (normalized.EndsWith(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				return XjGuoWeiCalculator.RunWei;
			}
			return XjGuoWeiCalculator.YuWei;
		}
}

