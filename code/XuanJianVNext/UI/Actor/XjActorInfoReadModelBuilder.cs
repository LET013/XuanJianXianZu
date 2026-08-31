using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{
	private readonly struct FrameCacheEntry
	{
		internal readonly XjActorInfoReadModel Model;
		internal readonly XjActorRevisionToken RevisionToken;
		internal readonly int RelationsRevisionHash;
		internal readonly int WorldYear;

		internal FrameCacheEntry(
			XjActorInfoReadModel model,
			in XjActorRevisionToken revisionToken,
			int relationsRevisionHash,
			int worldYear)
		{
			Model = model;
			RevisionToken = revisionToken;
			RelationsRevisionHash = relationsRevisionHash;
			WorldYear = worldYear;
		}
	}

	private const int ReadModelCacheSoftLimit = 1024;
	private static readonly Dictionary<long, FrameCacheEntry> FrameCache = new Dictionary<long, FrameCacheEntry>();

	internal static XjActorInfoReadModel BuildForActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return Empty();
		long actorId = ((BaseSystemData)actor.data).id;
		int worldYear = XjYearTracker.CurrentYear;
		XjActorRevisionToken revisionToken = XjActorStateRevisionStore.GetToken(actorId);
		int relationsRevisionHash = XjActorSupplementReadModelStore.GetRelationsRevisionHash(actorId, in revisionToken);
		if (FrameCache.TryGetValue(actorId, out FrameCacheEntry cached)
			&& cached.RevisionToken == revisionToken
			&& cached.RelationsRevisionHash == relationsRevisionHash
			&& cached.WorldYear == worldYear)
		{
			return cached.Model;
		}

		XjActorInfoReadModel model = BuildForActorCore(actor, actorId, in revisionToken);
		// 只读构建可能水合高境运行态；按构建后的分角色令牌记账，避免下一次无意义重建。
		XjActorRevisionToken committedToken = XjActorStateRevisionStore.GetToken(actorId);
		int committedRelationsHash = XjActorSupplementReadModelStore.GetRelationsRevisionHash(actorId, in committedToken);
		if (FrameCache.Count >= ReadModelCacheSoftLimit) FrameCache.Clear();
		FrameCache[actorId] = new FrameCacheEntry(model, in committedToken, committedRelationsHash, worldYear);
		return model;
	}

	internal static void RemoveCachedActor(long actorId)
	{
		if (actorId > 0L) FrameCache.Remove(actorId);
	}

	internal static void ClearReadModelCache()
	{
		FrameCache.Clear();
	}

	private static XjActorInfoReadModel BuildForActorCore(Actor actor, long actorId, in XjActorRevisionToken revisionToken)
	{
		// UI读取严格只读：境界名称投影由晋升/读档业务入口维护，窗口不得反向写角色状态。
		string actorName = actor.getName() ?? string.Empty;
		// UI读取也是旧档自愈入口：先天命数超过100时即时把溢出迁往后天。
		XjMingShuState.Normalize(actor);
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjJinDanState jinDanState = XjJinDanAccessor.BuildState(actor);
		XjShenDanState shenDanState = XjShenDanAccessor.BuildState(actor);
		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		XjGongFaState gongFaState = XjGongFaAccessor.BuildState(actor);
		bool isDaoTaiRealm = string.Equals(snapshot.RealmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(snapshot.RealmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
		XjGuoWeiQuanBingRegistry.TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState quanBingState);
		if (isDaoTaiRealm && !quanBingState.Found)
		{
			XjGuoWeiQuanBingRegistry.TryGet(actorId, out quanBingState);
		}
		jinDanState = BuildDaoTaiDisplayJinDanState(actor, jinDanState, isDaoTaiRealm);
		int jinDanYiXiang = GetJinDanYiXiang(actor);
		string realmDisplay = GetRealmDisplay(snapshot.RealmId, snapshot.ZhenYuan, xianJiState.Count, jinDanYiXiang);
		if (XjXuanJianShenTongSpecials.IsYuYiXian(actor))
		{
			realmDisplay = "郁仪仙";
		}
		else if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
		{
			realmDisplay = "结璘仙";
		}
		else if (shenDanState.Found)
		{
			// 神丹本身没有果位；挂靠关系只在“道途”区展示一次，
			// 避免把根锚点果位误读成神丹自己的位序。
			realmDisplay = "神丹";
		}
		if (XjXianGuoSystem.TryGetBorrowedCombatGrade(actor, out _))
		{
			realmDisplay = "仙国假金丹（本境：" + realmDisplay + "）";
		}
		else if (XjXianGuoSystem.TryGetInstitutionalProjectionTier(actor, out int institutionalTier)
			&& institutionalTier > XjRealmSuppression.GetRealmTier(actor))
		{
			string borrowedRealmName = institutionalTier switch
			{
				XjRealmSuppression.TierZhuJi => "筑基",
				XjRealmSuppression.TierZiFu => "紫府",
				_ => string.Empty
			};
			if (!string.IsNullOrWhiteSpace(borrowedRealmName))
			{
				realmDisplay = "持玄" + borrowedRealmName + "（本境：" + realmDisplay + "）";
			}
		}
		realmDisplay = XjHouShenShuSystem.DecorateRealmDisplay(actor, realmDisplay);
		string daoTu = ResolveDisplayDaoTu(actor, snapshot.DaoTu, gongFaState, jinDanState, quanBingState);
		int xjZz = snapshot.XjZz < 0 ? 0 : snapshot.XjZz;
		float mingShuCongenital = ReadIntegerFloat(actor, XjActorDataKeys.MingShuCongenital, snapshot.MingShu);
		float mingShuAcquired = ReadIntegerFloat(actor, XjActorDataKeys.MingShuAcquired, 0f);
		float displayedMingShu = Math.Max(0f, mingShuCongenital + mingShuAcquired);
		bool shouldShowGuoWeiQuanBing = XjHighRealmDisplayPolicy.UsesFruitPositionTemplate(snapshot.RealmId);

		XjHighRealmDoctrineSnapshot doctrineState = shouldShowGuoWeiQuanBing
			? XjHighRealmDaoStateService.BuildSnapshot(actor)
			: default;
		string guoWeiImageSummary = shouldShowGuoWeiQuanBing
			? XjGuoWeiImageStateService.BuildSummary(actor)
			: string.Empty;
		string doctrineSummary = shouldShowGuoWeiQuanBing
			? XjHighRealmDaoStateService.BuildCodexSummary(actor, in doctrineState, guoWeiImageSummary)
			: string.Empty;
		XjActorRelationsReadModel relations = XjActorSupplementReadModelStore.GetRelations(actor, actorId, in revisionToken);
		XjActorEquipmentReadModel equipment = XjActorSupplementReadModelStore.GetEquipment(actor, actorId, in revisionToken);

		return new XjActorInfoReadModel(
			true,
			actorId,
			actorName,
			snapshot.RealmId,
			realmDisplay,
			daoTu,
			XjDaoTuCounter.GetDisplayText(ResolveMechanicsDaoTu(daoTu)),
			xjZz,
			GetAptitudeDisplay(xjZz),
			GetAptitudeOverlayDisplay(snapshot.XjZzOverlayMask),
			GetChuShenDisplay(actor),
			snapshot.ZhenYuan,
			displayedMingShu,
			mingShuCongenital,
			mingShuAcquired,
			BuildMingShuBonusSummary(snapshot.RealmId, xjZz, mingShuCongenital, mingShuAcquired),
			snapshot.HuiGuang,
			BuildGongFaSummary(actor, gongFaState),
			BuildGongFaBonusSummary(actor, gongFaState),
			BuildCaiQiSummary(actor, XjCaiQiActorAccessor.BuildSnapshot(actor)),
			BuildCaiQiFaSummary(XjCaiQiFaAccessor.BuildState(actor)),
			BuildXianJiSummary(actor, snapshot.DaoTu, xianJiState),
			BuildQiuJinFaSummary(actor, XjQiuJinFaAccessor.BuildState(actor)),
			XjQiuJinIntentSystem.BuildDisplaySummary(actor, snapshot),
			BuildJinDanSummary(actor, jinDanState, shenDanState, daoTu),
			jinDanYiXiang,
			doctrineState.PositionImage,
			doctrineSummary,
			BuildJinDanDaoSpellSummary(actor, daoTu, snapshot.RealmId, jinDanState),
			shouldShowGuoWeiQuanBing
				? BuildQuanBingSummary(quanBingState)
				: string.Empty,
			shouldShowGuoWeiQuanBing
				? BuildQuanBingProcessSummary(quanBingState)
				: string.Empty,
			shouldShowGuoWeiQuanBing
				? BuildGuoWeiZhongAiSummary(quanBingState)
				: string.Empty,
			XjHighRealmIdentity.IsZhenJun(snapshot.RealmId)
				? XjDaoTaiMeritSystem.BuildDisplaySummary(actor)
				: string.Empty,
			isDaoTaiRealm ? BuildDaoTaiBindingSummary(actorId) : string.Empty,
			BuildReincarnationSummary(actor),
			BuildFormerLifeSummary(actor),
			BuildTitleSummary(actor),
			BuildZiFuYuYinSummary(actor),
			BuildEraBonusSummary(snapshot.DaoTu, daoTu),
			BuildBreakthroughSummary(actor),
			relations.FamilySummary,
			relations.BloodlineSummary,
			relations.FatherStatusSummary,
			relations.ZongMenSummary,
			equipment.FaBaoSummary,
			equipment.QianKunDaiSummary);
	}

	private static XjActorInfoReadModel Empty()
	{
		return new XjActorInfoReadModel(
			false,
			0L,
			string.Empty,
			string.Empty,
			"暂无",
			string.Empty,
			string.Empty,
			0,
			"未定",
			"无",
			"未定出身",
			0f,
			0f,
			0f,
			0f,
			"当前境界暂无大境界突破加成",
			0f,
			"暂无功法",
			string.Empty,
			"暂无采气",
			"暂无采气法",
			"暂无仙基",
			"暂无求金法",
			string.Empty,
			"暂无金丹",
			0,
			0,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			"暂无尊号",
			string.Empty,
			"无",
			string.Empty,
			"暂无家族",
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty);
	}
}
