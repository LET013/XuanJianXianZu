using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Aptitude;

internal static class XjAptitudeEffectRules
{
	internal static void EnsureBaseValuesBoundToAptitude(Actor actor, int xjZz)
	{
		if (actor?.data == null || xjZz < 1 || xjZz > 6
			|| !TryResolveBaseRange(xjZz, out int mingShuMin, out int mingShuMax,
				out int huiGuangMin, out int huiGuangMax))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		// 修炼资质同时决定先天命数与道慧的先天曲线。两者都只向上补齐，
		// 不覆盖血脉、事件、果位等来源已经给予的更高值。
		float rolledMingShu = XjDeterministicHash.RollRange(actorId, xjZz, 701, mingShuMin, mingShuMax);
		float rolledHuiGuang = XjDeterministicHash.RollRange(actorId, xjZz, 709, huiGuangMin, huiGuangMax);

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		float nextCongenital = Math.Max(Math.Max(0f, (float)Math.Floor(congenital)), rolledMingShu);
		float nextAcquired = Math.Max(0f, (float)Math.Floor(acquired));
		XjMingShuState.Set(actor, nextCongenital, nextAcquired);

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang,
			XjDaoHuiPolicy.Clamp(Math.Max(huiGuang, rolledHuiGuang)));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAptitudeBaseBoundTier, out int boundTier);
		if (boundTier != xjZz)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjAptitudeBaseBoundTier, xjZz);
		}
	}

	/// <summary>
	/// 0.9.9.8 曾将先天命数从资质曲线中错误拆离。旧档借既有的限额 bootstrap
	/// 注册通道一次性补绑，不新增世界人口扫描，也不会每年重复改写。
	/// </summary>
	internal static void EnsureStoredAptitudeBaseCurve(Actor actor)
	{
		if (actor?.data == null || XjCultivationPathRules.IsShi(actor)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz)
			|| xjZz < 1 || xjZz > 6) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAptitudeBaseBoundTier, out int boundTier);
		if (boundTier >= xjZz) return;
		EnsureBaseValuesBoundToAptitude(actor, xjZz);
	}

	internal static void EnsureHuiGuangMinimumBoundToAptitude(Actor actor, int xjZz)
	{
		if (actor?.data == null
			|| !XjDaoHuiPolicy.TryGetAptitudeRange(xjZz, out int huiGuangMin, out _))
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float current);
		if (current < huiGuangMin)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Clamp(huiGuangMin));
		}
	}

	internal static void ApplyOnAgeFiveResult(Actor actor, in XjAptitudeRollResult result)
	{
		if (actor?.data == null) return;
		if (result.Passed)
		{
			EnsureBaseValuesBoundToAptitude(actor, result.XjZz);
			ApplyPrimaryAptitudeEffect(actor, result.XjZz);
		}
	}

	internal static void ApplyPrimaryAptitudeEffect(Actor actor, int xjZz)
	{
		if (actor?.data == null || xjZz < 1 || xjZz > 6) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzEffectApplied, out int applied);
		if (applied != 0) return;
		if (!TryResolvePrimaryRange(xjZz, out int zhenYuanMin, out int zhenYuanMax)) return;

		long actorId = ((BaseSystemData)actor.data).id;
		ApplyZhenYuanDelta(actor, XjDeterministicHash.RollRange(actorId, xjZz, 17, zhenYuanMin, zhenYuanMax));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzEffectApplied, 1);
	}

	private static void ApplyZhenYuanDelta(Actor actor, int zhenYuanDelta)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		float nextZhenYuan = Math.Max(0f, ToInteger(zhenYuan) + zhenYuanDelta);
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		// 一次性资质效果这里只写真元；先天命数与道慧由 EnsureBaseValuesBoundToAptitude 统一绑定。
		nextZhenYuan = XjCultivationGrowthRules.ApplyRealmCap(snapshot, nextZhenYuan);
		nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, nextZhenYuan);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
	}

	private static bool TryResolveBaseRange(
		int xjZz,
		out int mingShuMin,
		out int mingShuMax,
		out int huiGuangMin,
		out int huiGuangMax)
	{
		(mingShuMin, mingShuMax) = xjZz switch
		{
			1 => (18, 32),
			2 => (26, 42),
			3 => (38, 58),
			4 => (54, 76),
			5 => (70, 90),
			6 => (82, 100),
			_ => (0, 0)
		};
		if (mingShuMax <= 0
			|| !XjDaoHuiPolicy.TryGetAptitudeRange(xjZz, out huiGuangMin, out huiGuangMax))
		{
			huiGuangMin = 0;
			huiGuangMax = 0;
			return false;
		}
		return true;
	}

	private static bool TryResolvePrimaryRange(int xjZz, out int zhenYuanMin, out int zhenYuanMax)
	{
		(zhenYuanMin, zhenYuanMax) = xjZz switch
		{
			1 => (3, 8),
			2 => (8, 18),
			3 => (20, 36),
			4 => (48, 68),
			5 => (72, 96),
			6 => (110, 140),
			_ => (0, 0)
		};
		return zhenYuanMax > 0;
	}

	private static float ToInteger(float value)
	{
		return (float)Math.Floor(Math.Max(0f, value));
	}
}
