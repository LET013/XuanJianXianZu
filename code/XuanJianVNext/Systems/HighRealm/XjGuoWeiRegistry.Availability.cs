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
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.YaoShu;

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

			if (IsPermanentlyLockedGuoWei(Normalize(guoWei))
				|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(guoWei)
				|| XjYaoShuGreatSageSystem.IsExternalPositionOccupied(guoWei))
			{
				return false;
			}

			string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			if (XjTaiYinHiddenFruitSystem.IsHiddenForActor(normalizedGuoWei, actorId, out _)) return false;
			// 道胎兼持的次位由独立双位账本占用，禁止普通 TryClaim 路径再次认领；
			// 否则 Registry 的“一人一活动果位”会把道胎主果反向挤掉。
			if (XjFruitPositionWorldState.IsDaoTaiSecondaryOccupied(normalizedGuoWei)) return false;
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

		internal static bool TryResolveConflictingHolder(string guoWei, long actorId, out long holderActorId, out string source)
		{
			holderActorId = 0L;
			source = string.Empty;
			if (string.IsNullOrWhiteSpace(guoWei) || actorId <= 0L) return false;

			string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			string key = NormalizeKey(normalized);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry occupied)
				&& occupied.Found
				&& occupied.IsActive
				&& occupied.ActorId > 0L)
			{
				// 普通果位注册表仍是主位的最终权威。如果当前角色就是注册表持有人，
				// 另一名道胎的副位绑定才是应被 Rehydration/Tick 释放的冲突项；
				// 不能反过来把真实主位持有人迁去余/闰位。
				if (occupied.ActorId == actorId) return false;
				holderActorId = occupied.ActorId;
				source = "GuoWeiRegistry";
				return true;
			}

			if (XjFruitPositionWorldState.TryGetDaoTaiSecondaryHolder(normalized, out long daoTaiHolderId, out _)
				&& daoTaiHolderId > 0L
				&& daoTaiHolderId != actorId)
			{
				holderActorId = daoTaiHolderId;
				source = "DaoTaiSecondary";
				return true;
			}

			return false;
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
					|| !TryGetStrictActiveEntryByActorId(candidate.ActorId, out XjGuoWeiRegistryEntry strictCandidate)
					|| !string.Equals(strictCandidate.GuoWei, candidate.GuoWei, StringComparison.Ordinal)
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
				string type = XjGuoWeiCalculator.NormalizePositionType(types[typeIndex]);
				int slotCount = ResolveSlotCount(normalizedDaoTu, type);
				if (slotCount <= 0)
				{
					continue;
				}
				sawValidType = true;
				if (!IsDaoTuTypeAllowed(normalizedDaoTu, type))
				{
					sawRuleBlocked = true;
					continue;
				}
				if (!XjLongShuSystem.CanClaimHeShuiFruitPosition(actorId, normalizedDaoTu, type))
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
					// 释土锁位只有在求位者亲身进入旃檀林、道慧足够并触发极低概率盗位时
					// 才从永久封锁转为可承继的释意污染位。其他锁位仍按隐藏处理。
					if (XjShiFruitPositionLockSystem.IsLocked(candidate))
					{
						if (!XjScheduler.ResolveActor(actorId, out Actor thief)
							|| !XjShiFruitPositionLockSystem.TryStealFromShiTu(thief, candidate, out _))
						{
							sawHidden = true;
							continue;
						}
					}
					if (IsPermanentlyLockedGuoWei(normalizedDaoTu, type, candidate)
						|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(candidate)
						|| XjYaoShuGreatSageSystem.IsExternalPositionOccupied(candidate))
					{
						sawHidden = true;
						continue;
					}
					if (XjTaiYinHiddenFruitSystem.IsHiddenForActor(candidate, actorId, out _))
					{
						sawHidden = true;
						continue;
					}
					if (XjScheduler.ResolveActor(actorId, out Actor candidateActor)
						&& !XjFruitPositionWorldState.CanAttemptPosition(candidateActor, normalizedDaoTu, type, candidate, out _))
					{
						sawRuleBlocked = true;
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
			string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			string key = NormalizeKey(normalizedGuoWei);
			if (activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry current)
				&& current.Found
				&& current.ActorId == actorId)
			{
				return true;
			}
			if (!IsDaoTuTypeAllowed(daoTu, type)) return false;
			if (!XjLongShuSystem.CanClaimHeShuiFruitPosition(actorId, daoTu, type)) return false;
			if (IsPermanentlyLockedGuoWei(Normalize(daoTu), XjGuoWeiCalculator.NormalizePositionType(type), normalizedGuoWei)
				|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(normalizedGuoWei)
				|| XjYaoShuGreatSageSystem.IsExternalPositionOccupied(normalizedGuoWei)) return false;
			if (XjScheduler.ResolveActor(actorId, out Actor actor)
				&& !XjFruitPositionWorldState.CanAttemptPosition(actor, daoTu, type, normalizedGuoWei, out _)) return false;
			if (!IsAvailableForActor(normalizedGuoWei, actorId)) return false;
			return !TryFindActiveTypeConflict(type, daoTu, actorId, out _, out _);
		}

		private static bool IsDaoTuTypeAllowed(string daoTu, string type)
		{
			string normalizedDaoTu = Normalize(daoTu);
			// 旧时间轴遗留渊照修士不强制降境，但在真正空证且水月照真气源落地前，
			// 连余/闰位也不得提前生成；否则正位虽被时代封锁，fallback仍可能让旧个体
			// 以余位/闰位先一步把尚未出生的道途写进金丹世界史。
			if (string.Equals(normalizedDaoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
				&& !XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(normalizedDaoTu))
			{
				return false;
			}
			if (XjZiJinSwordDaoCatalog.IsLongGeng(normalizedDaoTu))
			{
				// 初代服气开道仍持正位，后续可生动态余位，但长庚永不生闰位。
				return XjFuQiSwordWorldState.IsEstablished
					&& !string.Equals(
						XjGuoWeiCalculator.NormalizePositionType(type),
						XjGuoWeiCalculator.RunWei,
						StringComparison.Ordinal);
			}
			_ = type;
			return true;
		}

		internal static bool IsHiddenYinSiZhengWei(string daoTu, string type, string guoWei)
		{
			// 兼容旧调用名；实际同时处理阴司封锁与斩养之劫封锁。
			return IsPermanentlyLockedGuoWei(daoTu, type, guoWei);
		}

		internal static bool IsHiddenYinSiZhengWei(string guoWei)
		{
			return IsPermanentlyLockedGuoWei(guoWei);
		}

		internal static bool IsPermanentlyLockedGuoWei(string daoTu, string type, string guoWei)
		{
			if (XjShiFruitPositionLockSystem.IsLocked(guoWei)
				|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(guoWei)) return true;
			string normalizedDaoTu = Normalize(daoTu);
			string normalizedType = XjGuoWeiCalculator.NormalizePositionType(type);
			string normalizedGuoWei = Normalize(guoWei);
			// 渊照空证的五百年节点属于“时代封锁”，并非旧有阴司/斩养永久锁：
			// 自本局仙鉴起录年起至玄鉴历第五百年之前封太阴、坎水正果；只有空证事件真实执行后
			// 才释放太阴、坎水，并同时开放新生渊照正果。统一入口避免年度同年顺序提前解封。
			if (XjYuanZhaoFruitSealPolicy.IsSealed(normalizedDaoTu, normalizedType, XjYearTracker.CurrentYear))
			{
				return true;
			}
			if (string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
				&& PermanentlyLockedZhengWeiDaoTus.Contains(normalizedDaoTu))
			{
				return true;
			}
			return string.Equals(normalizedGuoWei, Normalize(ZhanYangLockedZheQiYuWei), StringComparison.Ordinal);
		}

		internal static bool IsPermanentlyLockedGuoWei(string guoWei)
		{
			if (XjShiFruitPositionLockSystem.IsLocked(guoWei)
				|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(guoWei)) return true;
			string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			if (string.IsNullOrWhiteSpace(normalized))
			{
				return false;
			}
			if (string.Equals(normalized, Normalize(ZhanYangLockedZheQiYuWei), StringComparison.Ordinal))
			{
				return true;
			}
			if (!normalized.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				return false;
			}
			string normalizedType = XjGuoWeiCalculator.ZhengWei;
			string normalizedDaoTu = normalized.Substring(0, normalized.Length - XjGuoWeiCalculator.ZhengWei.Length);
			if (XjYuanZhaoFruitSealPolicy.IsSealed(normalizedDaoTu, normalizedType, XjYearTracker.CurrentYear))
			{
				return true;
			}
			foreach (string daoTu in PermanentlyLockedZhengWeiDaoTus)
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
			// “果位在世”必须有仍存活、仍真实承载该正位的角色。旧实现只看
			// activeEntriesByGuoWei，死亡清理与同年求金之间存在一帧窗口，
			// 会让郁仪/结璘误读到已经死亡的正位。复用严格活锚查询一次性封口。
			return TryFindActiveAnchor(daoTu, XjGuoWeiCalculator.ZhengWei, out _);
		}

		private static string[] BuildSearchTypes(string preferredType, bool allowLowerFallback)
		{
			string normalized = XjGuoWeiCalculator.NormalizePositionType(preferredType);
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

		private static int ResolveSlotCount(string daoTu, string type)
		{
			return XjFruitPositionWorldState.ResolveSlotCount(daoTu, XjGuoWeiCalculator.NormalizePositionType(type));
		}

		internal static string ResolveTypeFromName(string guoWei)
		{
			string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
			if (normalized.EndsWith(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) return XjGuoWeiCalculator.ZhengWei;
			if (normalized.EndsWith(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) return XjGuoWeiCalculator.YuWei;
			if (normalized.EndsWith(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return XjGuoWeiCalculator.RunWei;
			return XjGuoWeiCalculator.YuWei;
		}
}
