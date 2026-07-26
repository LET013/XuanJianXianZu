using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.LongShu;

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

		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (!jinDan.Found)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		XjCultivatorCache.CheckAndUpdate(actor);
		XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		XjActorRegistry.Register(actor, out _);

		// 正常读档只恢复内存索引；登名石属于新实体复现，允许解决果位
		// 冲突并把替代果位同步回角色。
		if (externalSpawn)
		{
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
		}
		else
		{
			XjGuoWeiRegistry.ReconcileLiveActorReadOnly(actor);
		}
		jinDan = XjJinDanAccessor.BuildState(actor);
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

		EnsureChronicle(actor, jinDan, daoTu);
		ReconcileExistingFaBao(actor);

		if (externalSpawn)
		{
			if (XjLongShuSystem.IsLongShu(actor))
			{
				XjLongShuDongTianSystem.EnsureForJinDan(actor, Math.Max(1, XjYearTracker.CurrentYear));
			}
			else
			{
				// 普通金丹继续走宗门洞天设置，不套用龙属 490/10 年固定周期。
				XjZongMenDongTianLifecycle.TryStartImmediately(actor, Math.Max(1, XjYearTracker.CurrentYear));
			}

			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		return true;
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
			&& XjZongMenCityData.TryFindActorZongMenCity(actor, out City city))
		{
			hasDongTian = XjZongMenCityData.HasDongTianPeak(city);
			zongMenName = XjZongMenCityData.GetZongMenName(city);
			dongTianName = XjZongMenCityData.GetDongTianPeakName(city);
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
		if (XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out long familyId)
			&& familyId > 0L)
		{
			XjFamilyFaBaoWarehouse.AddFaBaoToFamily(
				actorId,
				actor.getName(),
				familyId,
				state.Id,
				state.Name,
				state.DaoTu,
				state.ClassName,
				XjFamilyFaBaoWarehouse.SourceTypeJinDan,
				state.Year);
		}
	}
}
