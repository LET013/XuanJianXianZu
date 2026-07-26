using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.UI.Rank;

/// <summary>
/// 排行榜玄鉴数据的统一鉴定快照。
/// 战力恢复 0.5.4 的“道行阶段权重 ×（攻击+生命）”主公式，
/// 禁止再用境界固定十万分制造胎息与高境界数值重叠。
/// </summary>
internal readonly struct XjRankMetricSnapshot
{
	internal readonly string RealmId;
	internal readonly int RealmOrder;
	internal readonly int Aptitude;
	internal readonly float ZhenYuan;
	internal readonly float MingShu;
	internal readonly float HuiGuang;
	internal readonly int XianJiCount;
	internal readonly int JinDanYiXiang;
	internal readonly float Power;

	internal XjRankMetricSnapshot(
		string realmId,
		int realmOrder,
		int aptitude,
		float zhenYuan,
		float mingShu,
		float huiGuang,
		int xianJiCount,
		int jinDanYiXiang,
		float power)
	{
		RealmId = realmId ?? string.Empty;
		RealmOrder = Math.Max(0, realmOrder);
		Aptitude = Math.Max(0, aptitude);
		ZhenYuan = Math.Max(0f, zhenYuan);
		MingShu = Math.Max(0f, mingShu);
		HuiGuang = Math.Max(0f, huiGuang);
		XianJiCount = Math.Max(0, xianJiCount);
		JinDanYiXiang = Math.Max(0, jinDanYiXiang);
		Power = Math.Max(0f, power);
	}
}

internal static class XjRankMetrics
{
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
		float power = CalculatePower(actor, realmId, cultivation.ZhenYuan, xianJiCount, jinDanYiXiang);

		return new XjRankMetricSnapshot(
			realmId,
			XjRealmHelper.GetOrder(realmId),
			aptitude,
			cultivation.ZhenYuan,
			cultivation.MingShu,
			cultivation.HuiGuang,
			xianJiCount,
			jinDanYiXiang,
			power);
	}

	internal static string ResolveRealmDisplay(in XjRankMetricSnapshot metrics)
	{
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
		// 大境界为主键，小境界为同境界内的次键；不新增角色属性。
		return metrics.RealmOrder * 100f + ResolveStageOrder(ResolveRealmDisplay(in metrics));
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
			"金丹初期" => 1, "金丹中期" => 2, "金丹后期" => 3, "金丹巅峰" => 4,
			"神丹" => 1,
			_ => 0
		};
	}

	internal static float CalculatePower(
		Actor actor,
		string realmId,
		float zhenYuan,
		int xianJiCount,
		int jinDanYiXiang)
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
		int realmWeight = ResolveLegacyRealmWeight(stageName);
		if (realmWeight <= 0)
		{
			return 0f;
		}

		float attack = Math.Max(0f, XjSafeCore.GetStatSafe(actor, "damage", 0f));
		float health = Math.Max(0f, XjSafeCore.GetStatSafe(actor, "health", 0f));
		double power = (double)realmWeight * (attack + health);
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			power += (double)Math.Max(0, jinDanYiXiang) * 100000000d;
		}

		if (double.IsNaN(power) || power <= 0d)
		{
			return 0f;
		}
		return power >= float.MaxValue ? float.MaxValue : (float)power;
	}

	private static int ResolveLegacyRealmWeight(string stageName)
	{
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
			"金丹巅峰" => 129600,
			_ => 1
		};
	}
}
