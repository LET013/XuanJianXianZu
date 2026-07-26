using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
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
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{
	internal static XjActorInfoReadModel BuildForActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return Empty();
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string projectedRealm = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		string expectedRealmDisplay = XjXuanJianShenTongSpecials.IsJieLinXian(actor)
			? "结璘仙" : XjRealmHelper.GetDisplayName(projectedRealm);
		bool projectionStale = !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string storedRealmDisplay)
			|| !string.Equals((storedRealmDisplay ?? string.Empty).Trim(), expectedRealmDisplay, StringComparison.Ordinal);
		if (projectionStale) XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		string actorName = actor.getName() ?? string.Empty;
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjJinDanState jinDanState = XjJinDanAccessor.BuildState(actor);
		XjShenDanState shenDanState = XjShenDanAccessor.BuildState(actor);
		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		XjGongFaState gongFaState = XjGongFaAccessor.BuildState(actor);
		XjGuoWeiQuanBingRegistry.TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState quanBingState);
		int jinDanYiXiang = GetJinDanYiXiang(actor);
		string realmDisplay = GetRealmDisplay(snapshot.RealmId, snapshot.ZhenYuan, xianJiState.Count, jinDanYiXiang);
		if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
		{
			realmDisplay = "结璘仙";
		}
		else if (shenDanState.Found)
		{
			realmDisplay = "神丹（挂靠金丹-" + (string.IsNullOrWhiteSpace(shenDanState.AnchorName) ? "未明" : shenDanState.AnchorName.Trim()) + "）";
		}
		string daoTu = ResolveDisplayDaoTu(snapshot.DaoTu, gongFaState, jinDanState, quanBingState);
		int xjZz = snapshot.XjZz < 0 ? 0 : snapshot.XjZz;
		float mingShuCongenital = ReadIntegerFloat(actor, XjActorDataKeys.MingShuCongenital, snapshot.MingShu);
		float mingShuAcquired = ReadIntegerFloat(actor, XjActorDataKeys.MingShuAcquired, 0f);

		return new XjActorInfoReadModel(
			true,
			actorId,
			actorName,
			snapshot.RealmId,
			realmDisplay,
			daoTu,
			XjDaoTuCounter.GetDisplayText(daoTu),
			xjZz,
			GetAptitudeDisplay(xjZz),
			GetAptitudeOverlayDisplay(snapshot.XjZzOverlayMask),
			GetChuShenDisplay(actor),
			snapshot.ZhenYuan,
			snapshot.MingShu,
			mingShuCongenital,
			mingShuAcquired,
			BuildMingShuBonusSummary(snapshot.RealmId, mingShuCongenital, mingShuAcquired),
			snapshot.HuiGuang,
			BuildGongFaSummary(actor, gongFaState),
			BuildGongFaBonusSummary(actor, gongFaState),
			BuildCaiQiSummary(actor, XjCaiQiActorAccessor.BuildSnapshot(actor)),
			BuildCaiQiFaSummary(XjCaiQiFaAccessor.BuildState(actor)),
			BuildXianJiSummary(actor, snapshot.DaoTu, xianJiState),
			BuildQiuJinFaSummary(actor, XjQiuJinFaAccessor.BuildState(actor)),
			BuildJinDanSummary(actor, jinDanState, shenDanState, daoTu),
			jinDanYiXiang,
			BuildJinDanDaoSpellSummary(actor, daoTu, jinDanState),
			string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
				? BuildQuanBingSummary(quanBingState)
				: string.Empty,
			string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
				? BuildGuoWeiZhongAiSummary(quanBingState)
				: string.Empty,
			BuildReincarnationSummary(actor),
			BuildFormerLifeSummary(actor),
			BuildTitleSummary(actor),
			BuildZiFuYuYinSummary(actor),
			BuildEraBonusSummary(daoTu),
			BuildBreakthroughSummary(actor),
			BuildFamilySummary(actorId),
			BuildBloodlineSummary(actorId),
			BuildFatherStatusSummary(actorId),
			BuildZongMenSummary(actor, XjZongMenAccessor.BuildIdentity(actor)),
			BuildFaBaoSummary(XjFaBaoReadModel.BuildForActor(actor)),
			BuildQianKunDaiSummary(actorId));
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
			"暂无金丹",
			0,
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
