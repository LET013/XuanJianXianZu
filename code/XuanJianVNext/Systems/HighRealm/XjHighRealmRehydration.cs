using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 读档、登名石复现等“角色已经是金丹”的冷路径重建器。
/// 只补注册表、纪事、权柄镜像和已有法宝，不重复发放突破奖励。
/// </summary>
internal static class XjHighRealmRehydration
{
	internal static bool ReconcileActor(Actor actor, bool externalSpawn = false)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		if (shenDan.Found)
		{
			XjCultivatorCache.CheckAndUpdate(actor);
			XjActorRegistry.Register(actor, out _);
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			XjShenDanRegistry.Observe(actor);
			return true;
		}

		bool isDaoTai = IsDaoTai(actor);
		XjJinDanState jinDan = isDaoTai
			? XjJinDanAccessor.BuildPositionCarrierState(actor)
			: XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found)
		{
			return false;
		}

		if (actorId <= 0L)
		{
			return false;
		}

		// 冷读档优先纠正 RC11.5“果位意象调试误写小境界投影”的旧档污染。
		// 真实道行/修持字段未损坏，因此无需猜测或重算角色修炼成果。
		XjHighRealmDaoStateService.EnsureRestoredState(actor, Math.Max(1, XjYearTracker.CurrentYear));

		XjCultivatorCache.CheckAndUpdate(actor);
		XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		XjActorRegistry.Register(actor, out _);

		// 正常读档只恢复内存索引；登名石属于新实体复现，允许解决果位
		// 冲突并把替代果位同步回角色。
		if (externalSpawn && !isDaoTai)
		{
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
		}
		else
		{
			XjGuoWeiRegistry.ReconcileLiveActorReadOnly(actor);
		}
		XjShenDanRegistry.ReconcilePending();
		jinDan = isDaoTai
			? XjJinDanAccessor.BuildPositionCarrierState(actor)
			: XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found)
		{
			// 外部复现或主动重建时，五上位旧档可能因正果已有主而依法回退紫府。
			// 此时不得继续按金丹重建权柄、纪事和法宝。
			XjCultivatorCache.CheckAndUpdate(actor);
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return true;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);

		if (!XjGuoWeiQuanBingRegistry.TryGet(actorId, out _))
		{
			bool restoredReadOnly = XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
			if (!restoredReadOnly && externalSpawn)
			{
				XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, daoTu, jinDan.GuoWei, jinDan.SuccessYear);
			}
		}
		if (externalSpawn)
		{
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
		}

		if (!isDaoTai)
		{
			EnsureChronicle(actor, jinDan, daoTu);
		}
		ReconcileExistingFaBao(actor);
		if (isDaoTai)
		{
			XjDaoTaiDualPositionSystem.TickActor(actor, Math.Max(1, XjYearTracker.CurrentYear));
		}

		if (externalSpawn)
		{
			if (XjLongShuSystem.IsLongShu(actor))
			{
				XjLongShuDongTianSystem.EnsureForJinDan(actor, Math.Max(1, XjYearTracker.CurrentYear));
			}
			else
			{
				// 普通金丹继续走宗门洞天设置，不套用龙属 490/10 年固定周期。
				XjSectDongTianLifecycle.TryStartImmediately(actor, Math.Max(1, XjYearTracker.CurrentYear));
			}

			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		return true;
	}

	private static bool IsDaoTai(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return true;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		realmId = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static void EnsureChronicle(Actor actor, in XjJinDanState jinDan, string daoTu)
	{
		if (XjJinDanBreakthroughSystem.HasJinDanChronicle(actor))
		{
			return;
		}

		int year = jinDan.SuccessYear > 0 ? jinDan.SuccessYear : Math.Max(1, XjYearTracker.CurrentYear);
		bool hasDongTian = false;
		string zongMenName = string.Empty;
		string dongTianName = string.Empty;
		if (!XjLongShuSystem.IsLongShu(actor)
			&& XjSectCityData.TryFindActorZongMenCity(actor, out City city))
		{
			hasDongTian = XjSectCityData.HasDongTianPeak(city);
			zongMenName = XjSectCityData.GetZongMenName(city);
			dongTianName = XjSectCityData.GetDongTianPeakName(city);
		}

		XjChronicleWriter.RecordJinDanSucceeded(
			actor,
			year,
			jinDan.GuoWei,
			jinDan.JinXing,
			daoTu,
			hasDongTian,
			zongMenName,
			dongTianName);
	}

	private static void ReconcileExistingFaBao(Actor actor)
	{
		XjFaBaoState state = XjFaBaoAccessor.BuildState(actor);
		if (!state.Found)
		{
			// ActorData 的自定义字段缺失时，仍允许从已装备物品恢复；这里
			// 不调用金丹成功发放逻辑，因此不会凭空新增第二件法宝。
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
			state = XjFaBaoAccessor.BuildState(actor);
		}
		else
		{
			XjFaBaoAccessor.WriteState(actor, state);
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
		}

		if (!state.Found)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		// 活着的修士本命器只属于人物自身。高境重水合负责恢复装备，并清理旧版本
		// 错误写入家族/宗门器库的同一件主器，不再把它重新登记成仓储资产。
		XjFamilyFaBaoWarehouse.RemoveLiveBoundPrimaryEntries(actorId, state.Id, state.DaoTu, state.Source);
	}
}
