using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.Rank;

/// <summary>
/// 排行榜玄鉴数据的统一鉴定快照。
/// 战力恢复 0.5.4 的“道行阶段权重 ×（攻击+生命）”主公式，
/// 禁止再用境界固定十万分制造胎息与高境界数值重叠。
/// </summary>
internal readonly struct XjRankMetricSnapshot
{
	internal readonly string RealmId;
	internal readonly string DaoTu;
	internal readonly int RealmOrder;
	internal readonly int Aptitude;
	internal readonly float ZhenYuan;
	internal readonly float MingShu;
	internal readonly float HuiGuang;
	internal readonly int XianJiCount;
	internal readonly int JinDanYiXiang;
	internal readonly string RealmDisplayOverride;
	internal readonly int StageOrderOverride;
	internal readonly float Power;

	internal XjRankMetricSnapshot(
		string realmId,
		string daoTu,
		int realmOrder,
		int aptitude,
		float zhenYuan,
		float mingShu,
		float huiGuang,
		int xianJiCount,
		int jinDanYiXiang,
		string realmDisplayOverride,
		int stageOrderOverride,
		float power)
	{
		RealmId = realmId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		RealmOrder = Math.Max(0, realmOrder);
		Aptitude = Math.Max(0, aptitude);
		ZhenYuan = Math.Max(0f, zhenYuan);
		MingShu = Math.Max(0f, mingShu);
		HuiGuang = Math.Max(0f, huiGuang);
		XianJiCount = Math.Max(0, xianJiCount);
		JinDanYiXiang = Math.Max(0, jinDanYiXiang);
		RealmDisplayOverride = realmDisplayOverride ?? string.Empty;
		StageOrderOverride = Math.Max(0, stageOrderOverride);
		Power = Math.Max(0f, power);
	}
}

internal static class XjRankMetrics
{
	// 道胎/世尊在实际战斗中对金丹级存在完整大境界压制：低一大境界的普通攻击直接归零。
	// 排行榜必须反映这一事实，不能只给金丹巅峰两倍权重后又被装备/血量堆叠反超。
	// 这里使用金丹巅峰八倍的道胎权重；同一道胎层内部仍由真实攻击与生命区分。
	private const int DaoTaiRankWeight = 103680;
	internal static XjRankMetricSnapshot Build(Actor actor)
	{
		if (actor?.data == null)
		{
			return default;
		}

		XjActorCultivationSnapshot cultivation = XjActorCultivationSnapshotBuilder.Build(actor);
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.IsNullOrWhiteSpace(realmId))
		{
			realmId = XjRealmHelper.NormalizeId(cultivation.RealmId);
		}

		int aptitude = Math.Max(0, cultivation.XjZz);
		int xianJiCount = XjXianJiAccessor.BuildState(actor).Count;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int jinDanYiXiang);
		string realmDisplayOverride = string.Empty;
		int stageOrderOverride = 0;
		float progressionValue = cultivation.ZhenYuan;
		string daoTuDisplay = cultivation.DaoTu;

		if (XjCultivationPathRules.IsShi(actor)
			&& XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shiSnapshot))
		{
			// 排行榜只接收等效层级与实际释修名称，不把等效 RealmId 写回角色。
			realmId = XjShiPowerRules.GetEquivalentRealmId(actor);
			// “古释/今释”属于修法身份，不应污染境界列。排行榜境界只显示
			// 僧侣、法师、怜愍、摩诃、法相、世尊等真实释修境界。
			realmDisplayOverride = XjShiCatalog.GetRealmDisplay(shiSnapshot.Realm);
			stageOrderOverride = XjShiPowerRules.GetEquivalentStageOrder(actor);
			// 真元榜必须保持为0；释修修持只在释修照录中展示，不能借用真元栏。
			progressionValue = 0f;
			daoTuDisplay = XjShiCatalog.GetTraditionDisplay(shiSnapshot.Tradition);
			xianJiCount = 0;
			jinDanYiXiang = 0;
		}
		else if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			// 真人不拥有紫金仙基，因此排行榜不能继续把所有真人固定塞在“紫府中段”。
			// 直接读取与战斗/属性同源的性命合炼档位，使境界排序、战力权重、人物实际面板一致。
			stageOrderOverride = XjRealmStageStatMultiplierService.ResolveZhenRenEquivalentStageOrder(actor);
			realmDisplayOverride = XjRealmStageStatMultiplierService.ResolveZhenRenStageDisplay(actor);
			xianJiCount = 0;
		}
		else if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			// 黄冠现行战斗层级固定对齐筑基初期(16)，排行榜也必须使用同一层级，
			// 不能再固定按筑基中期权重把服气角色虚抬一档。
			stageOrderOverride = 1;
		}

		float power = CalculatePower(
			actor, realmId, progressionValue, xianJiCount, jinDanYiXiang, stageOrderOverride);

		return new XjRankMetricSnapshot(
			realmId,
			daoTuDisplay,
			XjRealmHelper.GetOrder(realmId),
			aptitude,
			progressionValue,
			cultivation.MingShu,
			cultivation.HuiGuang,
			xianJiCount,
			jinDanYiXiang,
			realmDisplayOverride,
			stageOrderOverride,
			power);
	}

	internal static string ResolveRealmDisplay(in XjRankMetricSnapshot metrics)
	{
		if (!string.IsNullOrWhiteSpace(metrics.RealmDisplayOverride)) return metrics.RealmDisplayOverride;
		string stage = XjDaoXingStageRules.FormatDisplay(
			metrics.RealmId,
			metrics.ZhenYuan,
			metrics.XianJiCount,
			metrics.JinDanYiXiang);
		if (!string.IsNullOrWhiteSpace(stage)) return stage;
		string realm = XjRealmHelper.GetDisplayName(metrics.RealmId);
		return string.IsNullOrWhiteSpace(realm) ? "未入道" : realm;
	}

	internal static float ResolveRealmSortValue(in XjRankMetricSnapshot metrics)
	{
		// 跨修法统一比较：黄冠固定对齐筑基初期；真人读取性命合炼的真实等效阶段；
		// 真君羽士继续按前中后巅与金丹一一对应。StageOrderOverride 由各独立修法
		// 在快照构建时写入，避免排行榜再用固定中段值掩盖真实修持差异。
		int stageOrder = metrics.StageOrderOverride > 0
			? metrics.StageOrderOverride
			: ResolveEquivalentStageOrder(metrics.RealmId, ResolveRealmDisplay(in metrics));
		return metrics.RealmOrder * 100f + stageOrder;
	}

	private static int ResolveEquivalentStageOrder(string realmId, string stage)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return 1;
		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 1;
		return ResolveStageOrder(stage);
	}

	private static int ResolveStageOrder(string stage)
	{
		if (string.IsNullOrWhiteSpace(stage)) return 0;
		return stage switch
		{
			"玄景轮" => 1, "承明轮" => 2, "周行轮" => 3, "青元轮" => 4, "玉京轮" => 5, "灵初轮" => 6,
			"炼气一层" or "练气一层" => 1, "炼气二层" or "练气二层" => 2,
			"炼气三层" or "练气三层" => 3, "炼气四层" or "练气四层" => 4,
			"炼气五层" or "练气五层" => 5, "炼气六层" or "练气六层" => 6,
			"炼气七层" or "练气七层" => 7, "炼气八层" or "练气八层" => 8,
			"炼气九层" or "练气九层" => 9,
			"筑基初期" => 1, "筑基中期" => 2, "筑基后期" => 3,
			"紫府初期" => 1, "紫府中期" => 2, "参紫门槛" => 3, "紫府后期" => 4, "紫府巅峰" => 5,
			"真人初期" => 1, "真人中期" => 2, "真人后期" => 4, "真人巅峰" => 5,
			"金丹初期" => 1, "金丹中期" => 2, "金丹后期" => 3, "金丹巅峰" => 4,
			"真君羽士初期" => 1, "真君羽士中期" => 2, "真君羽士后期" => 3, "真君羽士巅峰" => 4,
			"神丹" => 1,
			_ => 0
		};
	}

	internal static float CalculatePower(
		Actor actor,
		string realmId,
		float zhenYuan,
		int xianJiCount,
		int jinDanYiXiang,
		int stageOrderOverride = 0)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return 0f;
		}

		string stageName = XjDaoXingStageRules.FormatDisplay(
			realmId,
			Math.Max(0f, zhenYuan),
			Math.Max(0, xianJiCount),
			Math.Max(0, jinDanYiXiang));
		int realmWeight = stageOrderOverride > 0
			? ResolveLegacyRealmWeightByOrder(realmId, stageOrderOverride)
			: ResolveLegacyRealmWeight(realmId, stageName);
		if (realmWeight <= 0)
		{
			return 0f;
		}
		// 金丹同级排行与战斗共享同一品秩来源，避免旧法相24~27直接套用
		// 正统金丹阶段权重。真实攻击/生命仍参与最终评分，不把品秩做成死榜。
		realmWeight = XjHighRealmCombatGrade.ResolveUnifiedRankWeight(actor, realmWeight);

		float attack = Math.Max(0f, XjSafeCore.GetStatSafe(actor, "damage", 0f));
		// 排行榜使用原生实际最大生命作为生命侧战力。actor.stats["health"] 在带有
		// multiplier_health 的高境角色上可能只是基础统计值；道胎尤其会因此被严重低估。
		float healthStat = Math.Max(0f, XjSafeCore.GetStatSafe(actor, "health", 0f));
		float health = Math.Max(healthStat, XjSafeCore.GetMaxHealthSafe(actor, healthStat));
		double power = (double)realmWeight * (attack + health);
		if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
		{
			power += (double)Math.Max(0, jinDanYiXiang) * 100000000d;
		}

		// 金丹内部仍保持7200→9600→10800→12960；道胎/世尊则按跨大境界绝对压制
		// 使用独立高位权重，避免“金丹属性堆高后显示战力反超世尊”的假象。
		// 排行榜战力是可读的相对评分，不直接展示底层超大生命/攻击原值。
		// 统一缩放一万倍只改变显示量级，不改变角色之间的排序关系。
		power /= 10000d;
		if (double.IsNaN(power) || power <= 0d)
		{
			return 0f;
		}
		return power >= float.MaxValue ? float.MaxValue : (float)power;
	}

	private static int ResolveLegacyRealmWeightByOrder(string realmId, int stageOrder)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		int stage = Math.Max(1, stageOrder);
		if (string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return Math.Min(6, stage) * 10;
		}
		if (string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return 80 + (Math.Min(9, stage) - 1) * 20;
		}
		if (string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return stage <= 1 ? 360 : stage == 2 ? 540 : 720;
		}
		if (string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return stage switch { 1 => 1200, 2 => 1800, 3 => 2100, 4 => 2700, _ => 3600 };
		}
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return stage <= 1 ? 360 : stage == 2 ? 540 : 720;
		}
		if (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return stage switch { 1 => 7200, 2 => 9600, 3 => 10800, _ => 12960 };
		}
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return DaoTaiRankWeight;
		return ResolveLegacyRealmWeight(realmId, string.Empty);
	}

	private static int ResolveLegacyRealmWeight(string realmId, string stageName)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		// 这里只保留旧快照/异常数据的保守兜底。正常服气角色会在 Build() 中写入
		// StageOrderOverride：黄冠按筑基初期，真人按性命合炼动态映射紫府阶段。
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return 360;
		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return 1200;
		// 道胎没有前中后阶段名，旧逻辑会落入 switch 默认值 1。两条修法的道胎以及
		// 世尊统一进入道胎高位权重，并与金丹级保持明确的大境界战力带。
		if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return DaoTaiRankWeight;
		if (!string.IsNullOrWhiteSpace(stageName)
			&& stageName.IndexOf("神丹", StringComparison.Ordinal) >= 0)
		{
			return 6480;
		}

		return stageName switch
		{
			"未入道" => 1,
			"玄景轮" => 10,
			"承明轮" => 20,
			"周行轮" => 30,
			"青元轮" => 40,
			"玉京轮" => 50,
			"灵初轮" => 60,
			"炼气一层" or "练气一层" => 80,
			"炼气二层" or "练气二层" => 100,
			"炼气三层" or "练气三层" => 120,
			"炼气四层" or "练气四层" => 140,
			"炼气五层" or "练气五层" => 160,
			"炼气六层" or "练气六层" => 180,
			"炼气七层" or "练气七层" => 200,
			"炼气八层" or "练气八层" => 220,
			"炼气九层" or "练气九层" => 240,
			"筑基初期" => 360,
			"筑基中期" => 540,
			"筑基后期" => 720,
			"紫府初期" => 1200,
			"紫府中期" => 1800,
			"参紫门槛" => 2100,
			"紫府后期" => 2700,
			"紫府巅峰" => 3600,
			"金丹初期" => 7200,
			"金丹中期" => 9600,
			"金丹后期" => 10800,
			"金丹巅峰" => 12960,
			"真君羽士初期" => 7200,
			"真君羽士中期" => 9600,
			"真君羽士后期" => 10800,
			"真君羽士巅峰" => 12960,
			_ => 1
		};
	}
}
