using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修可见特质只是权威状态的投影：释统、当前境界，以及真实金地所有权。
/// 座次、轮回世数等关系数据不膨胀为特质。
/// </summary>
internal static class XjShiVisibleTraitSync
{
	internal const string JinDiOwnerTraitId = "ZhanTanLin";
	private const string LegacyJinDiOwnerTraitId = "ZanTanLin";

	internal static readonly string[] RealmTraitIds =
	{
		XjShiTraitIds.Monk,
		XjShiTraitIds.DharmaMaster,
		XjShiTraitIds.LianMin,
		XjShiTraitIds.MoHe,
		XjShiTraitIds.DharmaForm,
		XjShiTraitIds.WorldHonored
	};

	internal static readonly string[] FoundationTraitIds = { XjShiTraitIds.Seed };

	internal static readonly string[] TraditionTraitIds =
	{
		XjShiTraitIds.Ancient,
		XjShiTraitIds.Modern
	};

	internal static readonly string[] JinDiOwnerTraitIds = { JinDiOwnerTraitId, LegacyJinDiOwnerTraitId };

	internal static void Sync(Actor actor)
	{
		if (actor?.data == null || !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot))
		{
			XjVisibleTraitSync.SyncSingleTraitInGroup(actor, string.Empty, FoundationTraitIds);
			XjVisibleTraitSync.SyncSingleTraitInGroup(actor, string.Empty, RealmTraitIds);
			XjVisibleTraitSync.SyncSingleTraitInGroup(actor, string.Empty, TraditionTraitIds);
			XjVisibleTraitSync.SyncSingleTraitInGroup(actor, string.Empty, JinDiOwnerTraitIds);
			return;
		}

		// 释修不获得仙修 RealmId/修炼权限，但其战斗位格已有权威等效映射。
		// 这里只投影与同阶仙修一致的 WorldBox 原生伴生特质，使排行榜与原生属性
		// 计算使用同一组基础战斗收益；外部 addTrait 对释修仍由门禁拒绝。
		XjVisibleTraitSync.SyncShiEquivalentNativeTraits(actor, XjShiPowerRules.GetEquivalentRealmId(actor));
		XjVisibleTraitSync.SyncSingleTraitInGroup(actor, XjShiTraitIds.Seed, FoundationTraitIds);
		XjVisibleTraitSync.SyncSingleTraitInGroup(
			actor,
			XjShiCatalog.GetRealmTraitId(snapshot.Realm),
			RealmTraitIds);
		XjVisibleTraitSync.SyncSingleTraitInGroup(
			actor,
			XjShiCatalog.GetTraditionTraitId(snapshot.Tradition),
			TraditionTraitIds);
		XjVisibleTraitSync.SyncSingleTraitInGroup(
			actor,
			HasOwnedJinDi(actor) ? JinDiOwnerTraitId : string.Empty,
			JinDiOwnerTraitIds);
	}


	/// <summary>
	/// 只检查释修境界可见投影是否与权威 ShiRealm 一致。用于高位年度对账修复旧档，
	/// 正常状态只做少量 hasTrait，不扫描世界、不重建任何列表。
	/// </summary>
	internal static bool EnsureRealmProjection(Actor actor, string shiRealm)
	{
		if (actor?.data == null || !XjShiCatalog.IsKnownRealm(shiRealm)) return false;
		string expected = XjShiCatalog.GetRealmTraitId(shiRealm);
		bool mismatch = string.IsNullOrWhiteSpace(expected) || !actor.hasTrait(expected);
		if (!mismatch)
		{
			for (int i = 0; i < RealmTraitIds.Length; i++)
			{
				string candidate = RealmTraitIds[i];
				if (!string.Equals(candidate, expected, StringComparison.Ordinal) && actor.hasTrait(candidate))
				{
					mismatch = true;
					break;
				}
			}
		}
		if (!mismatch) return false;

		Sync(actor);
		try { actor.setStatsDirty(); } catch { }
		return true;
	}

	internal static bool IsShiTrait(string traitId)
	{
		return string.Equals(traitId, XjShiTraitIds.Seed, StringComparison.Ordinal)
			|| XjShiCatalog.TryResolveRealmByTrait(traitId, out _)
			|| XjShiCatalog.TryResolveTraditionByTrait(traitId, out _);
	}

	private static bool HasOwnedJinDi(Actor actor)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (XjShiDomainState.TryGetOwnedNorthJinDi(actorId, out _)) return true;

		string[] keys = { XjActorDataKeys.ShiJinDiId, XjActorDataKeys.ShiDomainId };
		for (int i = 0; i < keys.Length; i++)
		{
			if (!XjActorAccessor.TryGetString(actor, keys[i], out string domainId)
				|| string.IsNullOrWhiteSpace(domainId)
				|| !XjShiDomainState.TryGet(domainId, out XjShiDomainRecord domain)
				|| domain == null
				|| domain.OwnerActorId != actorId) continue;
			if (domain.IsNorthWorldHonoredFragment > 0
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal)) return true;
		}
		return false;
	}
}
