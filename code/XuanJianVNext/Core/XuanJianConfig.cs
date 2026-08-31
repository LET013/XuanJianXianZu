using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Presentation;

public static class XuanJianConfig
{
	public static void AutoCollectZhuJiCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectZhuJi);
	public static void AutoCollectZiFuCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectZiFu);
	public static void AutoCollectJinDanCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectJinDan);
	public static void AutoCollectFuQiZhenRenCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectFuQiZhenRen);
	public static void AutoCollectFuQiZhenJunCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectFuQiZhenJun);
	public static void AutoCollectKongZhengZhenJunCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectKongZhengZhenJun);
	public static void AutoCollectTianShouDaoMaiCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectTianShouDaoMai);
	public static void AutoCollectZiFuReincarnationCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectZiFuReincarnation);
	public static void AutoCollectJinDanReincarnationCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectJinDanReincarnation);
	public static void AutoCollectFaBaoOwnerCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectFaBaoOwner);
	public static void AutoCollectSwordImmortalCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectSwordImmortal);
	public static void AutoCollectShiMoHeCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectShiMoHe);
	public static void AutoCollectShiDharmaFormCallBack(bool value) => SetAutoCollect(value, XjRuntimeSettings.SetAutoCollectShiDharmaForm);
	public static void EnableZiFuPromotionAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastZiFu(value);
	public static void EnableYaoCaiAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastYaoCai(value);
	public static void EnableLianDanAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastLianDan(value);
	public static void EnableLianQiArtifactAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastLianQiArtifact(value);
	public static void EnableTreasureMilestoneAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastTreasureMilestone(value);
	public static void EnableShenTongAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastShenTong(value);
	public static void EnableAuthorityPositionAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastAuthorityPosition(value);
	public static void EnableBottleneckAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastBottleneck(value);
	public static void EnableTalismanAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastTalisman(value);
	public static void EnableFormationAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastFormation(value);
	public static void EnableGongFaWriteAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastGongFaWrite(value);
	public static void EnableDongTianAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastDongTian(value);
	public static void EnableSecretRealmTrainingAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastSecretRealmTraining(value);
	public static void EnableYinSiAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastYinSi(value);
	public static void EnableSectAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastSect(value);
	public static void EnableDeathAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastDeath(value);
	public static void EnableLingWuAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastLingWu(value);
	public static void EnableHighRealmAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastHighRealm(value);
	public static void EnableFamilyInheritanceAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastFamilyInheritance(value);
	public static void EnableHighRealmInfluenceAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastHighRealmInfluence(value);
	public static void EnableShiAnnouncementCallBack(bool value) => XjRuntimeSettings.SetBroadcastShi(value);
	public static void EnableCultivationCallBack(bool value) => XjRuntimeSettings.SetCultivationEnabled(value);
	public static void ShowFpsOverlayCallBack(bool value)
	{
		XjRuntimeSettings.SetShowFpsOverlay(value);
		XjPresentationHooks.SetFpsOverlayEnabled(value);
	}
	public static void StageZeroObservationCallBack(bool value) => XjRuntimeSettings.SetStageZeroObservation(value);
	public static void PerformanceObservationCallBack(bool value) => XjRuntimeSettings.SetPerformanceObservation(value);
	public static void HighSpeedEnemySearchBackoffCallBack(bool value) => XjRuntimeSettings.SetHighSpeedEnemySearchBackoff(value);
	public static void EnableLongRunMemoryMaintenanceCallBack(bool value) => XjRuntimeSettings.SetLongRunMemoryMaintenance(value);
	public static void AllowSectRebellionCallBack(bool value) => XjRuntimeSettings.SetAllowSectRebellion(value);
	public static void EnableUpperCultivatorDaoObstructionCallBack(bool value) => XjRuntimeSettings.SetUpperCultivatorDaoObstruction(value);
	public static void EnableYaoXieGenerationCallBack(bool value) => XjRuntimeSettings.SetSpawnJinXingYaoXie(value);
	public static void EnableLongShuGenerationCallBack(bool value) => XjRuntimeSettings.SetSpawnLongShu(value);
	public static void DaoTaiBeyondWorldYearsCallBack(int value) => XjRuntimeSettings.SetDaoTaiBeyondWorldYears(value);
	public static void DaoTaiTravelYearsCallBack(int value) => XjRuntimeSettings.SetDaoTaiTravelYears(value);
	// 旧开关回调仅用于兼容既有本地配置；新版设置页统一使用时长拉条。
	public static void DaoTaiBeyondWorldCallBack(bool value) => XjRuntimeSettings.SetDaoTaiBeyondWorld(value);
	public static void DaoTaiTravelCallBack(bool value) => XjRuntimeSettings.SetDaoTaiTravel(value);
	public static void QiYuDongTianSpawnYearsCallBack(int value) => XjRuntimeSettings.SetQiYuDongTianSpawnYears(value);
	public static void QiYuDongTianSpawnYearsRangeCallBack(string value) => XjRuntimeSettings.SetQiYuDongTianSpawnYears(value);
	public static void JinDanDongTianCultivateYearsCallBack(int value) => XjRuntimeSettings.SetJinDanDongTianCultivateYears(value);
	public static void JinDanPostPeaceYearsCallBack(int value) => XjRuntimeSettings.SetJinDanPostPeaceYears(value);
	public static void AptitudeGrantChancePercentCallBack(int value) => XjRuntimeSettings.SetAptitudeGrantChancePercent(value);
	public static void QiuJinFaChancePermilleCallBack(int value) => XjRuntimeSettings.SetQiuJinFaChancePermille(value);
	public static void SwordIntentChancePercentCallBack(int value) => XjRuntimeSettings.SetSwordIntentChancePercent(value);
	public static void DaoTaiMeritGainCurvePercentCallBack(int value) => XjRuntimeSettings.SetDaoTaiMeritGainCurvePercent(value);
	// 旧配置回调名保留，避免用户本地已有配置在升级瞬间失效。
	public static void JinDanDongTianCultivateYearsRangeCallBack(string value) => XjRuntimeSettings.SetJinDanDongTianCultivateYears(value);
	public static void JinDanPostPeaceYearsRangeCallBack(string value) => XjRuntimeSettings.SetJinDanPostPeaceYears(value);

	private static void SetAutoCollect(bool value, System.Action<bool> setter)
	{
		setter(value);
		if (value)
		{
			XjScheduler.EnqueueKnownActorsForAutoCollectRecheck();
		}
	}
}
