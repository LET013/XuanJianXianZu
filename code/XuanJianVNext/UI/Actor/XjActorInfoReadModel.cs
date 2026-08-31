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
using XuanJianVNext.Data.Rules;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string RealmId;
	internal readonly string RealmDisplay;
	internal readonly string DaoTu;
	internal readonly string DaoTuCounterDisplay;
	internal readonly int XjZz;
	internal readonly string AptitudeDisplay;
	internal readonly string AptitudeOverlayDisplay;
	internal readonly string ChuShenDisplay;
	internal readonly float ZhenYuan;
	internal readonly float MingShu;
	internal readonly float MingShuCongenital;
	internal readonly float MingShuAcquired;
	internal readonly string MingShuBonusSummary;
	internal readonly float HuiGuang;
	internal readonly string GongFaSummary;
	internal readonly string GongFaBonusSummary;
	internal readonly string CaiQiSummary;
	internal readonly string CaiQiFaSummary;
	internal readonly string XianJiSummary;
	internal readonly string QiuJinFaSummary;
	internal readonly string QiuJinIntentSummary;
	internal readonly string JinDanSummary;
	internal readonly int JinDanYiXiang;
	internal readonly int GuoWeiImage;
	internal readonly string HighRealmDoctrineSummary;
	internal readonly string JinDanDaoSpellSummary;
	internal readonly string QuanBingSummary;
	internal readonly string QuanBingProcessSummary;
	internal readonly string GuoWeiZhongAiSummary;
	internal readonly string DaoTaiMeritSummary;
	internal readonly string DaoTaiBindingSummary;
	internal readonly string ReincarnationSummary;
	internal readonly string FormerLifeSummary;
	internal readonly string TitleSummary;
	internal readonly string StageBonusSummary;
	internal readonly string EraBonusSummary;
	internal readonly string BreakthroughSummary;
	internal readonly string FamilySummary;
	internal readonly string BloodlineSummary;
	internal readonly string FatherStatusSummary;
	internal readonly string ZongMenSummary;
	internal readonly string FaBaoSummary;
	internal readonly string QianKunDaiSummary;

	private XjActorInfoReadModel(
		bool found,
		long actorId,
		string actorName,
		string realmId,
		string realmDisplay,
		string daoTu,
		string daoTuCounterDisplay,
		int xjZz,
		string aptitudeDisplay,
		string aptitudeOverlayDisplay,
		string chuShenDisplay,
		float zhenYuan,
		float mingShu,
		float mingShuCongenital,
		float mingShuAcquired,
		string mingShuBonusSummary,
		float huiGuang,
		string gongFaSummary,
		string gongFaBonusSummary,
		string caiQiSummary,
		string caiQiFaSummary,
		string xianJiSummary,
		string qiuJinFaSummary,
		string qiuJinIntentSummary,
		string jinDanSummary,
		int jinDanYiXiang,
		int guoWeiImage,
		string highRealmDoctrineSummary,
		string jinDanDaoSpellSummary,
		string quanBingSummary,
		string quanBingProcessSummary,
		string guoWeiZhongAiSummary,
		string daoTaiMeritSummary,
		string daoTaiBindingSummary,
		string reincarnationSummary,
		string formerLifeSummary,
		string titleSummary,
		string stageBonusSummary,
		string eraBonusSummary,
		string breakthroughSummary,
		string familySummary,
		string bloodlineSummary,
		string fatherStatusSummary,
		string zongMenSummary,
		string faBaoSummary,
		string qianKunDaiSummary)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		RealmId = realmId ?? string.Empty;
		RealmDisplay = realmDisplay ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		DaoTuCounterDisplay = daoTuCounterDisplay ?? string.Empty;
		XjZz = xjZz < 0 ? 0 : xjZz;
		AptitudeDisplay = aptitudeDisplay ?? string.Empty;
		AptitudeOverlayDisplay = aptitudeOverlayDisplay ?? string.Empty;
		ChuShenDisplay = chuShenDisplay ?? string.Empty;
		ZhenYuan = zhenYuan;
		MingShu = mingShu;
		MingShuCongenital = mingShuCongenital;
		MingShuAcquired = mingShuAcquired;
		MingShuBonusSummary = mingShuBonusSummary ?? string.Empty;
		HuiGuang = huiGuang;
		GongFaSummary = gongFaSummary ?? string.Empty;
		GongFaBonusSummary = gongFaBonusSummary ?? string.Empty;
		CaiQiSummary = caiQiSummary ?? string.Empty;
		CaiQiFaSummary = caiQiFaSummary ?? string.Empty;
		XianJiSummary = xianJiSummary ?? string.Empty;
		QiuJinFaSummary = qiuJinFaSummary ?? string.Empty;
		QiuJinIntentSummary = qiuJinIntentSummary ?? string.Empty;
		JinDanSummary = jinDanSummary ?? string.Empty;
		JinDanYiXiang = jinDanYiXiang < 0 ? 0 : jinDanYiXiang;
		GuoWeiImage = guoWeiImage < 0 ? 0 : guoWeiImage;
		HighRealmDoctrineSummary = highRealmDoctrineSummary ?? string.Empty;
		JinDanDaoSpellSummary = jinDanDaoSpellSummary ?? string.Empty;
		QuanBingSummary = quanBingSummary ?? string.Empty;
		QuanBingProcessSummary = quanBingProcessSummary ?? string.Empty;
		GuoWeiZhongAiSummary = guoWeiZhongAiSummary ?? string.Empty;
		DaoTaiMeritSummary = daoTaiMeritSummary ?? string.Empty;
		DaoTaiBindingSummary = daoTaiBindingSummary ?? string.Empty;
		ReincarnationSummary = reincarnationSummary ?? string.Empty;
		FormerLifeSummary = formerLifeSummary ?? string.Empty;
		TitleSummary = titleSummary ?? string.Empty;
		StageBonusSummary = stageBonusSummary ?? string.Empty;
		EraBonusSummary = eraBonusSummary ?? string.Empty;
		BreakthroughSummary = breakthroughSummary ?? string.Empty;
		FamilySummary = familySummary ?? string.Empty;
		BloodlineSummary = bloodlineSummary ?? string.Empty;
		FatherStatusSummary = fatherStatusSummary ?? string.Empty;
		ZongMenSummary = zongMenSummary ?? string.Empty;
		FaBaoSummary = faBaoSummary ?? string.Empty;
		QianKunDaiSummary = qianKunDaiSummary ?? string.Empty;
	}
}
