using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NeoModLoader.api;

namespace XuanJianVNext.Core;

internal readonly struct XjYearRange
{
	internal readonly int Min;
	internal readonly int Max;

	internal XjYearRange(int min, int max)
	{
		Min = Math.Max(0, Math.Min(min, max));
		Max = Math.Max(Min, Math.Max(min, max));
	}

	internal int Roll(System.Random random)
	{
		if (Min >= Max || random == null)
		{
			return Min;
		}

		long span = (long)Max - Min + 1L;
		return (int)(Min + (long)Math.Floor(random.NextDouble() * span));
	}
}

internal static class XjRuntimeSettings
{
	internal const string ReincarnationModeZiFuJinXing = "ZiFuJinXing";

	private static readonly System.Random SharedRandom = new System.Random();
	private static bool autoCollectZhuJi;
	private static bool autoCollectZiFu = true;
	private static bool autoCollectJinDan = true;
	private static bool autoCollectFuQiZhenRen = true;
	private static bool autoCollectFuQiZhenJun = true;
	private static bool autoCollectKongZhengZhenJun = true;
	private static bool autoCollectTianShouDaoMai = true;
	private static bool autoCollectZiFuReincarnation = true;
	private static bool autoCollectJinDanReincarnation = true;
	private static bool autoCollectFaBaoOwner = true;
	private static bool autoCollectSwordImmortal;
	private static bool autoCollectShiMoHe = true;
	private static bool autoCollectShiDharmaForm = true;
	private static bool broadcastTreasureMilestone = true;
	private static bool broadcastShenTong = true;
	private static bool broadcastAuthorityPosition = true;
	private static bool broadcastBottleneck = true;
	private static bool broadcastGongFaWrite = true;
	private static bool broadcastDongTian = true;
	private static bool broadcastYinSi = true;
	private static bool broadcastSect = true;
	private static bool broadcastDeath = true;
	private static bool broadcastLingWu = true;
	private static bool broadcastHighRealm = true;
	private static bool broadcastFamilyInheritance = true;
	private static bool broadcastHighRealmInfluence = true;
	private static bool broadcastShi = true;
	private static bool cultivationEnabled = true;
	private static bool showFpsOverlay;
	private static bool stageZeroObservation = false;
	private static bool performanceObservation;
	private static bool highSpeedEnemySearchBackoff = true;
	private static bool longRunMemoryMaintenance = true;
	private static bool allowSectRebellion;
	private static bool upperCultivatorDaoObstruction;
	private static bool spawnJinXingYaoXie = true;
	private static bool spawnLongShu = true;
	private static int daoTaiBeyondWorldYears = 450;
	private static int daoTaiTravelYears = 30;
	private static XjYearRange qiYuDongTianSpawnYears = new XjYearRange(300, 500);
	private static int jinDanDongTianCultivateYears = 500;
	private static int jinDanPostPeaceYears = 10;
	private static int aptitudeGrantChancePercent = 40;
	private static int qiuJinFaChancePermille = 10;
	private static int swordIntentChancePercent = 1;
	private static int daoTaiMeritGainCurvePercent = 100;
	private static int revision;

	internal static bool AutoCollectZhuJiEnabled => autoCollectZhuJi;
	internal static bool AutoCollectZiFuEnabled => autoCollectZiFu;
	internal static bool AutoCollectJinDanEnabled => autoCollectJinDan;
	internal static bool AutoCollectFuQiZhenRenEnabled => autoCollectFuQiZhenRen;
	internal static bool AutoCollectFuQiZhenJunEnabled => autoCollectFuQiZhenJun;
	internal static bool AutoCollectKongZhengZhenJunEnabled => autoCollectKongZhengZhenJun;
	internal static bool AutoCollectTianShouDaoMaiEnabled => autoCollectTianShouDaoMai;
	internal static bool AutoCollectFaBaoOwnerEnabled => autoCollectFaBaoOwner;
	internal static bool AutoCollectSwordImmortalEnabled => autoCollectSwordImmortal;
	internal static bool AutoCollectShiMoHeEnabled => autoCollectShiMoHe;
	internal static bool AutoCollectShiDharmaFormEnabled => autoCollectShiDharmaForm;
	internal static bool BroadcastTreasureMilestoneEnabled => broadcastTreasureMilestone;
	internal static bool BroadcastShenTongEnabled => broadcastShenTong;
	internal static bool BroadcastAuthorityPositionEnabled => broadcastAuthorityPosition;
	internal static bool BroadcastBottleneckEnabled => broadcastBottleneck;
	internal static bool BroadcastGongFaWriteEnabled => broadcastGongFaWrite;
	internal static bool BroadcastDongTianEnabled => broadcastDongTian;
	internal static bool BroadcastYinSiEnabled => broadcastYinSi;
	internal static bool BroadcastSectEnabled => broadcastSect;
	internal static bool BroadcastDeathEnabled => broadcastDeath;
	internal static bool BroadcastLingWuEnabled => broadcastLingWu;
	internal static bool BroadcastHighRealmEnabled => broadcastHighRealm;
	internal static bool BroadcastFamilyInheritanceEnabled => broadcastFamilyInheritance;
	internal static bool BroadcastHighRealmInfluenceEnabled => broadcastHighRealmInfluence;
	internal static bool BroadcastShiEnabled => broadcastShi;
	internal static bool CultivationEnabled => cultivationEnabled;
	internal static bool ShowFpsOverlayEnabled => showFpsOverlay;
	internal static bool StageZeroObservationEnabled => stageZeroObservation;
	internal static bool PerformanceObservationEnabled => performanceObservation;
	internal static bool HighSpeedEnemySearchBackoffEnabled => highSpeedEnemySearchBackoff;
	internal static bool LongRunMemoryMaintenanceEnabled => longRunMemoryMaintenance;
	internal static bool AllowSectRebellionEnabled => allowSectRebellion;
	internal static bool UpperCultivatorDaoObstructionEnabled => upperCultivatorDaoObstruction;
	internal static bool SpawnJinXingYaoXieEnabled => spawnJinXingYaoXie;
	internal static bool SpawnLongShuEnabled => spawnLongShu;
	internal static bool DaoTaiBeyondWorldEnabled => daoTaiBeyondWorldYears > 0;
	internal static bool DaoTaiTravelEnabled => daoTaiTravelYears > 0;
	internal static int DaoTaiBeyondWorldYears => Math.Max(100, daoTaiBeyondWorldYears);
	internal static int DaoTaiTravelYears => Math.Max(1, daoTaiTravelYears);
	internal static float AptitudeGrantChanceCap => aptitudeGrantChancePercent / 100f;
	internal static float QiuJinFaChanceCap => qiuJinFaChancePermille / 1000f;
	internal static float SwordIntentChanceCap => swordIntentChancePercent / 100f;
	internal static float DaoTaiMeritGainMultiplier
	{
		get
		{
			switch (daoTaiMeritGainCurvePercent)
			{
				case 75: return 0.75f;
				case 80: return 0.82f;
				case 85: return 0.88f;
				case 90: return 0.93f;
				case 95: return 0.97f;
				case 105: return 1.05f;
				default: return 1.00f;
			}
		}
	}
	internal static int Revision => revision;

	internal static void LoadFromModConfig(object modConfig)
	{
		if (modConfig == null)
		{
			return;
		}

		SetAutoCollectZhuJi(ReadBool(modConfig, "XuanJian_config_auto_collect_zhuji", autoCollectZhuJi));
		SetAutoCollectZiFu(ReadBool(modConfig, "XuanJian_config_auto_collect_zifu", autoCollectZiFu));
		SetAutoCollectJinDan(ReadBool(modConfig, "XuanJian_config_auto_collect_jindan", autoCollectJinDan));
		SetAutoCollectFuQiZhenRen(ReadBool(modConfig, "XuanJian_config_auto_collect_fuqi_zhenren", autoCollectFuQiZhenRen));
		SetAutoCollectFuQiZhenJun(ReadBool(modConfig, "XuanJian_config_auto_collect_fuqi_zhenjun", autoCollectFuQiZhenJun));
		SetAutoCollectKongZhengZhenJun(ReadBool(modConfig, "XuanJian_config_auto_collect_kongzheng_zhenjun", autoCollectKongZhengZhenJun));
		SetAutoCollectTianShouDaoMai(ReadBool(modConfig, "XuanJian_config_auto_collect_tianshoudaomai", autoCollectTianShouDaoMai));
		SetAutoCollectZiFuReincarnation(ReadBool(modConfig, "XuanJian_config_auto_collect_zifu_reincarnation", autoCollectZiFuReincarnation));
		SetAutoCollectJinDanReincarnation(ReadBool(modConfig, "XuanJian_config_auto_collect_jindan_reincarnation", autoCollectJinDanReincarnation));
		SetAutoCollectFaBaoOwner(ReadBool(modConfig, "XuanJian_config_auto_collect_fabao_owner", autoCollectFaBaoOwner));
		SetAutoCollectSwordImmortal(ReadBool(modConfig, "XuanJian_config_auto_collect_sword_immortal", autoCollectSwordImmortal));
		SetAutoCollectShiMoHe(ReadBool(modConfig, "XuanJian_config_auto_collect_shi_mohe", autoCollectShiMoHe));
		SetAutoCollectShiDharmaForm(ReadBool(modConfig, "XuanJian_config_auto_collect_shi_dharma_form", autoCollectShiDharmaForm));
		SetBroadcastBottleneck(ReadBool(modConfig, "XuanJian_config_enable_bottleneck_announcement", broadcastBottleneck));
		SetBroadcastShenTong(ReadBool(modConfig, "XuanJian_config_enable_shentong_announcement", broadcastShenTong));
		SetBroadcastAuthorityPosition(ReadBool(modConfig, "XuanJian_config_enable_authority_position_announcement", broadcastAuthorityPosition));
		SetBroadcastGongFaWrite(ReadBool(modConfig, "XuanJian_config_enable_gongfa_write_announcement", broadcastGongFaWrite));
		SetBroadcastDongTian(ReadBool(modConfig, "XuanJian_config_enable_dongtian_announcement", broadcastDongTian));
		SetBroadcastYinSi(ReadBool(modConfig, "XuanJian_config_enable_yinsi_announcement", broadcastYinSi));
		SetBroadcastSect(ReadBool(modConfig, "XuanJian_config_enable_sect_announcement", broadcastSect));
		SetBroadcastDeath(ReadBool(modConfig, "XuanJian_config_enable_death_announcement", broadcastDeath));
		SetBroadcastLingWu(ReadBool(modConfig, "XuanJian_config_enable_lingwu_announcement", broadcastLingWu));
		SetBroadcastHighRealm(ReadBool(modConfig, "XuanJian_config_enable_highrealm_announcement", broadcastHighRealm));
		SetBroadcastTreasureMilestone(ReadBool(modConfig, "XuanJian_config_enable_treasure_milestone_announcement", broadcastTreasureMilestone));
		SetBroadcastFamilyInheritance(ReadBool(modConfig, "XuanJian_config_enable_family_inheritance_announcement", broadcastFamilyInheritance));
		SetBroadcastHighRealmInfluence(ReadBool(modConfig, "XuanJian_config_enable_highrealm_influence_announcement", broadcastHighRealmInfluence));
		SetBroadcastShi(ReadBool(modConfig, "XuanJian_config_enable_shi_announcement", broadcastShi));
		SetCultivationEnabled(ReadBool(modConfig, "XuanJian_config_enable_cultivation", cultivationEnabled));
		SetShowFpsOverlay(ReadBool(modConfig, "XuanJian_config_show_fps_overlay", showFpsOverlay));
		SetStageZeroObservation(ReadBool(modConfig, "XuanJian_config_stage0_observation", stageZeroObservation));
		SetPerformanceObservation(ReadBool(modConfig, "XuanJian_config_performance_observation", performanceObservation));
		SetHighSpeedEnemySearchBackoff(ReadBool(modConfig, "XuanJian_config_highspeed_enemy_search_backoff", highSpeedEnemySearchBackoff));
		SetLongRunMemoryMaintenance(ReadBool(modConfig, "XuanJian_config_enable_longrun_memory_maintenance", longRunMemoryMaintenance));
		SetAllowSectRebellion(ReadBool(modConfig, "XuanJian_config_allow_sect_rebellion", allowSectRebellion));
		SetUpperCultivatorDaoObstruction(ReadBool(modConfig, "XuanJian_config_enable_upper_cultivator_dao_obstruction", upperCultivatorDaoObstruction));
		SetSpawnJinXingYaoXie(ReadBool(modConfig, "XuanJian_config_enable_yao_xie_generation", spawnJinXingYaoXie));
		SetSpawnLongShu(ReadBool(modConfig, "XuanJian_config_enable_longshu_generation", spawnLongShu));
		// 新版使用时长拉条；旧开关仍可由历史配置回调触发，但不再作为默认配置源。
		SetDaoTaiBeyondWorldYears(ReadInt(modConfig, "XuanJian_config_daotai_beyond_world_years", daoTaiBeyondWorldYears));
		SetDaoTaiTravelYears(ReadInt(modConfig, "XuanJian_config_daotai_travel_years", daoTaiTravelYears));
		SetQiYuDongTianSpawnYears(ReadInt(modConfig, "XuanJian_config_qiyu_dongtian_spawn_years", 300));
		SetJinDanDongTianCultivateYears(ReadInt(modConfig, "XuanJian_config_jindan_dongtian_cultivate_years", 500));
		SetJinDanPostPeaceYears(ReadInt(modConfig, "XuanJian_config_jindan_post_peace_years", 10));
		SetAptitudeGrantChancePercent(ReadInt(modConfig, "XuanJian_config_aptitude_grant_chance_percent", aptitudeGrantChancePercent));
		SetQiuJinFaChancePermille(ReadInt(modConfig, "XuanJian_config_qiujinfa_chance_permille", qiuJinFaChancePermille));
		SetSwordIntentChancePercent(ReadInt(modConfig, "XuanJian_config_sword_intent_chance_percent", swordIntentChancePercent));
		SetDaoTaiMeritGainCurvePercent(ReadInt(modConfig, "XuanJian_config_daotai_merit_gain_curve_percent", daoTaiMeritGainCurvePercent));
	}

	internal static void SetAutoCollectZhuJi(bool value) => SetBool(ref autoCollectZhuJi, value);
	internal static void SetAutoCollectZiFu(bool value) => SetBool(ref autoCollectZiFu, value);
	internal static void SetAutoCollectJinDan(bool value) => SetBool(ref autoCollectJinDan, value);
	internal static void SetAutoCollectFuQiZhenRen(bool value) => SetBool(ref autoCollectFuQiZhenRen, value);
	internal static void SetAutoCollectFuQiZhenJun(bool value) => SetBool(ref autoCollectFuQiZhenJun, value);
	internal static void SetAutoCollectKongZhengZhenJun(bool value) => SetBool(ref autoCollectKongZhengZhenJun, value);
	internal static void SetAutoCollectTianShouDaoMai(bool value) => SetBool(ref autoCollectTianShouDaoMai, value);
	internal static void SetAutoCollectZiFuReincarnation(bool value) => SetBool(ref autoCollectZiFuReincarnation, value);
	internal static void SetAutoCollectJinDanReincarnation(bool value) => SetBool(ref autoCollectJinDanReincarnation, value);
	internal static void SetAutoCollectFaBaoOwner(bool value) => SetBool(ref autoCollectFaBaoOwner, value);
	internal static void SetAutoCollectSwordImmortal(bool value) => SetBool(ref autoCollectSwordImmortal, value);
	internal static void SetAutoCollectShiMoHe(bool value) => SetBool(ref autoCollectShiMoHe, value);
	internal static void SetAutoCollectShiDharmaForm(bool value) => SetBool(ref autoCollectShiDharmaForm, value);
	internal static void SetBroadcastTreasureMilestone(bool value)
	{
		SetBool(ref broadcastTreasureMilestone, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Treasure);
		}
	}
	internal static void SetBroadcastShenTong(bool value)
	{
		SetBool(ref broadcastShenTong, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.ShenTong);
		}
	}
	internal static void SetBroadcastAuthorityPosition(bool value)
	{
		SetBool(ref broadcastAuthorityPosition, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.AuthorityPosition);
		}
	}
	internal static void SetBroadcastZiFu(bool value) => SetBroadcastHighRealm(value);
	internal static void SetBroadcastYaoCai(bool value) { }
	internal static void SetBroadcastLianDan(bool value) => SetBroadcastTreasureMilestone(value);
	internal static void SetBroadcastLianQiArtifact(bool value) => SetBroadcastTreasureMilestone(value);
	internal static void SetBroadcastBottleneck(bool value) => SetBool(ref broadcastBottleneck, value);
	internal static void SetBroadcastTalisman(bool value) { }
	internal static void SetBroadcastFormation(bool value) { }
	internal static void SetBroadcastGongFaWrite(bool value)
	{
		SetBool(ref broadcastGongFaWrite, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.GongFaWrite);
		}
	}
	internal static void SetBroadcastDongTian(bool value)
	{
		SetBool(ref broadcastDongTian, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.DongTian);
		}
	}
	internal static void SetBroadcastSecretRealmTraining(bool value) => SetBroadcastDongTian(value);
	internal static void SetBroadcastYinSi(bool value) => SetBool(ref broadcastYinSi, value);
	internal static void SetBroadcastSect(bool value) => SetBool(ref broadcastSect, value);
	internal static void SetBroadcastDeath(bool value) => SetBool(ref broadcastDeath, value);
	internal static void SetBroadcastLingWu(bool value)
	{
		SetBool(ref broadcastLingWu, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.LingWu);
		}
	}
	internal static void SetBroadcastHighRealm(bool value)
	{
		SetBool(ref broadcastHighRealm, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.HighRealm);
		}
	}
	internal static void SetBroadcastFamilyInheritance(bool value)
	{
		SetBool(ref broadcastFamilyInheritance, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.FamilyInheritance);
		}
	}
	internal static void SetBroadcastHighRealmInfluence(bool value) => SetBool(ref broadcastHighRealmInfluence, value);
	internal static void SetBroadcastShi(bool value)
	{
		SetBool(ref broadcastShi, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.Shi);
		}
	}
	internal static void SetCultivationEnabled(bool value) => SetBool(ref cultivationEnabled, value);
	internal static void SetShowFpsOverlay(bool value) => SetBool(ref showFpsOverlay, value);
	internal static void SetPerformanceObservation(bool value)
	{
		bool changed = performanceObservation != value;
		SetBool(ref performanceObservation, value);
		if (!changed) return;

		XjRuntimeDiagnostics.Clear();
		XjSemanticDiagnostics.Clear();
		if (value || !XjPerformanceTelemetry.BenchmarkActive)
		{
			XjPerformanceTelemetry.ResetMeasurementWindow();
		}
	}
	internal static void SetHighSpeedEnemySearchBackoff(bool value) => SetBool(ref highSpeedEnemySearchBackoff, value);
	internal static void SetLongRunMemoryMaintenance(bool value) => SetBool(ref longRunMemoryMaintenance, value);
	internal static void SetStageZeroObservation(bool value)
	{
		SetBool(ref stageZeroObservation, value);
		if (!value)
		{
			XjStageZeroObservation.Clear();
		}
	}
	internal static void SetAllowSectRebellion(bool value) => SetBool(ref allowSectRebellion, value);
	internal static void SetUpperCultivatorDaoObstruction(bool value) => SetBool(ref upperCultivatorDaoObstruction, value);
	internal static void SetSpawnJinXingYaoXie(bool value) => SetBool(ref spawnJinXingYaoXie, value);
	internal static void SetSpawnLongShu(bool value) => SetBool(ref spawnLongShu, value);
	internal static void SetDaoTaiBeyondWorldYears(int value) => SetInt(ref daoTaiBeyondWorldYears, Math.Clamp(value, 100, 1000));
	internal static void SetDaoTaiTravelYears(int value) => SetInt(ref daoTaiTravelYears, Math.Clamp(value, 1, 100));
	// 旧开关只保留兼容语义：关闭时使用最短时长，开启时恢复默认值。
	internal static void SetDaoTaiBeyondWorld(bool value) => SetDaoTaiBeyondWorldYears(value ? 450 : 100);
	internal static void SetDaoTaiTravel(bool value) => SetDaoTaiTravelYears(value ? 30 : 1);
	internal static void SetAptitudeGrantChancePercent(int value) => SetInt(ref aptitudeGrantChancePercent, Math.Clamp(value, 25, 40));
	internal static void SetQiuJinFaChancePermille(int value) => SetInt(ref qiuJinFaChancePermille, Math.Clamp(value, 1, 50));
	internal static void SetSwordIntentChancePercent(int value) => SetInt(ref swordIntentChancePercent, Math.Clamp(value, 1, 10));
	internal static void SetDaoTaiMeritGainCurvePercent(int value)
	{
		int clamped = Math.Clamp(value, 75, 105);
		int snapped = 75 + (int)Math.Round((clamped - 75) / 5d, MidpointRounding.AwayFromZero) * 5;
		SetInt(ref daoTaiMeritGainCurvePercent, Math.Clamp(snapped, 75, 105));
	}

	internal static void SetQiYuDongTianSpawnYears(int value)
	{
		int clamped = Math.Clamp(value, 100, 500);
		SetRange(ref qiYuDongTianSpawnYears, new XjYearRange(clamped, clamped));
	}

	internal static void SetQiYuDongTianSpawnYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(300, 300));
		int midpoint = range.Min + (range.Max - range.Min) / 2;
		SetQiYuDongTianSpawnYears(midpoint);
	}

	internal static void SetJinDanDongTianCultivateYears(int value)
	{
		SetInt(ref jinDanDongTianCultivateYears, Math.Clamp(value, 1, 1000));
	}

	internal static void SetJinDanPostPeaceYears(int value)
	{
		SetInt(ref jinDanPostPeaceYears, Math.Clamp(value, 1, 100));
	}

	// 兼容旧版 TEXT 配置文件；加载到的区间取中值，随后由滑块固化。
	internal static void SetJinDanDongTianCultivateYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(500, 500));
		SetJinDanDongTianCultivateYears(range.Min + (range.Max - range.Min) / 2);
	}

	internal static void SetJinDanPostPeaceYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(10, 10));
		SetJinDanPostPeaceYears(range.Min + (range.Max - range.Min) / 2);
	}

	private static void SetBool(ref bool field, bool value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		revision++;
	}

	private static void SetInt(ref int field, int value)
	{
		if (field == value)
		{
			return;
		}
		field = value;
		revision++;
	}

	private static void SetRange(ref XjYearRange field, XjYearRange value)
	{
		if (field.Min == value.Min && field.Max == value.Max)
		{
			return;
		}

		field = value;
		revision++;
	}

	internal static int GetQiYuDongTianSpawnYears()
	{
		// 滑块语义是严格的玄鉴历固定周期，不再作为随机区间或世运倍率的基数。
		return Math.Clamp(qiYuDongTianSpawnYears.Min, 100, 500);
	}

	internal static int RollQiYuDongTianSpawnOffset()
	{
		// 仅保留旧调用兼容；当前奇遇洞天调度不再使用随机 Roll。
		return GetQiYuDongTianSpawnYears();
	}

	internal static int RollJinDanDongTianCultivateYears()
	{
		return jinDanDongTianCultivateYears;
	}

	internal static int RollJinDanPostPeaceYears()
	{
		return jinDanPostPeaceYears;
	}

	internal static bool ShouldAutoCollectReincarnation(string mode)
	{
		if (string.Equals(mode, "ShiReincarnation", StringComparison.Ordinal))
			return autoCollectShiMoHe;
		if (string.Equals(mode, ReincarnationModeZiFuJinXing, StringComparison.Ordinal))
			return autoCollectZiFuReincarnation;
		return autoCollectJinDanReincarnation;
	}

	private static XjYearRange ParseRange(string raw, XjYearRange fallback)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}

		string normalized = raw.Trim().Replace('－', '-').Replace('—', '-').Replace('~', '-');
		string[] parts = normalized.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 1
			&& int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int single))
		{
			return new XjYearRange(single, single);
		}

		if (parts.Length >= 2
			&& int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min)
			&& int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
		{
			return new XjYearRange(min, max);
		}

		return fallback;
	}

	private static object GetConfigItem(object config, string id)
	{
		if (config == null || string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		if (TryGetConfigItemFromModConfig(config, id, out object directItem))
		{
			return directItem;
		}

		if (TryFindConfigItem(config, id, 0, out object reflectedItem))
		{
			return reflectedItem;
		}

		return null;
	}

	private static bool TryGetConfigItemFromModConfig(object config, string id, out object item)
	{
		item = null;
		if (config is not ModConfig typedConfig)
		{
			return false;
		}

		try
		{
			var items = typedConfig["ConfigItems"];
			if (TryFindConfigItem(items, id, 0, out item))
			{
				return true;
			}
		}
		catch (KeyNotFoundException) { /* ModConfig 缺少可选根键属于正常探测，不记异常。 */ }
		catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjRuntimeSettings.ConfigItemsProbe", ex); }

		string[] sectionNames =
		{
			"基础修炼",
			"自动收藏",
			"公告设置",
			"世界生态",
			"洞天设置",
			"性能维护"
		};
		for (int i = 0; i < sectionNames.Length; i++)
		{
			try
			{
				var sectionItems = typedConfig[sectionNames[i]];
				if (TryFindConfigItem(sectionItems, id, 0, out item))
				{
					return true;
				}
			}
			catch (KeyNotFoundException) { /* 当前配置版本没有该分组时继续探测下一分组。 */ }
			catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjRuntimeSettings.SectionProbe", ex); }
		}

		try
		{
			object direct = typedConfig[id];
			if (direct != null)
			{
				item = direct;
				return true;
			}
		}
		catch (KeyNotFoundException) { /* 没有同名直达项是正常结果。 */ }
		catch (System.Exception ex) { XjExceptionDiagnostics.Report("XjRuntimeSettings.DirectProbe", ex); }

		return false;
	}

	private static bool TryFindConfigItem(object source, string id, int depth, out object item)
	{
		item = null;
		if (source == null || string.IsNullOrWhiteSpace(id) || depth > 4)
		{
			return false;
		}

		if (IsConfigItemId(source, id))
		{
			item = source;
			return true;
		}

		if (source is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (IsIdMatch(entry.Key, id))
				{
					item = entry.Value;
					return true;
				}

				if (TryFindConfigItem(entry.Value, id, depth + 1, out item))
				{
					return true;
				}
			}
		}

		if (source is IEnumerable enumerable && source is not string)
		{
			foreach (object element in enumerable)
			{
				if (TryFindConfigItem(element, id, depth + 1, out item))
				{
					return true;
				}
			}
		}

		if (depth > 0)
		{
			return false;
		}

		Type type = source.GetType();
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		for (int i = 0; i < properties.Length; i++)
		{
			PropertyInfo property = properties[i];
			if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
			{
				continue;
			}

			try
			{
				object value = property.GetMethod.Invoke(source, null);
				if (TryFindConfigItem(value, id, depth + 1, out item))
				{
					return true;
				}
			}
			catch (System.Exception xjCaught490) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:490", xjCaught490); }
		}

		FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
		for (int i = 0; i < fields.Length; i++)
		{
			try
			{
				object value = fields[i].GetValue(source);
				if (TryFindConfigItem(value, id, depth + 1, out item))
				{
					return true;
				}
			}
			catch (System.Exception xjCaught506) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:506", xjCaught506); }
		}

		return false;
	}

	private static bool IsConfigItemId(object source, string id)
	{
		return TryReadMember(source, "Id", out object value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ID", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "id", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "Name", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "name", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "Key", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "key", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ConfigId", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ConfigID", out value) && IsIdMatch(value, id);
	}

	private static bool IsIdMatch(object value, string id)
	{
		return value != null && string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), id, StringComparison.Ordinal);
	}

	private static bool ReadBool(object config, string id, bool fallback)
	{
		object item = GetConfigItem(config, id);
		if (item == null)
		{
			return fallback;
		}

		try
		{
			if (TryReadMember(item, "BoolVal", out object value)
				|| TryReadMember(item, "Value", out value)
				|| TryReadMember(item, "value", out value))
			{
				if (value is bool boolValue)
				{
					return boolValue;
				}

				if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
				{
					return parsed;
				}
			}
		}
		catch (System.Exception xjCaught557) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:557", xjCaught557); }

		return fallback;
	}

	private static int ReadInt(object config, string id, int fallback)
	{
		object item = GetConfigItem(config, id);
		if (item == null)
		{
			return fallback;
		}

		try
		{
			if (TryReadMember(item, "IntVal", out object value)
				|| TryReadMember(item, "Value", out value)
				|| TryReadMember(item, "value", out value))
			{
				if (value is int intValue) return intValue;
				if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
				{
					return parsed;
				}
			}

			// 旧 TEXT 版兼容：区间取中值。
			if (TryReadMember(item, "TextVal", out value))
			{
				XjYearRange range = ParseRange(Convert.ToString(value, CultureInfo.InvariantCulture), new XjYearRange(fallback, fallback));
				return range.Min + (range.Max - range.Min) / 2;
			}
		}
		catch (System.Exception xjCaught592) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:592", xjCaught592); }
		return fallback;
	}
	private static bool TryReadMember(object source, string memberName, out object value)
	{
		value = null;
		if (source == null || string.IsNullOrWhiteSpace(memberName))
		{
			return false;
		}

		Type type = source.GetType();
		PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
		if (property?.GetMethod != null && property.GetIndexParameters().Length == 0)
		{
			try
			{
				value = property.GetMethod.Invoke(source, null);
				return true;
			}
			catch (System.Exception xjCaught640) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:640", xjCaught640); }
		}

		FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
		if (field != null)
		{
			try
			{
				value = field.GetValue(source);
				return true;
			}
			catch (System.Exception xjCaught653) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Core/XjRuntimeSettings.cs:653", xjCaught653); }
		}

		return false;
	}
}
