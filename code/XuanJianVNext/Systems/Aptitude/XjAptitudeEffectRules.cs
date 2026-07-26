using System;
using System.Globalization;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Aptitude;

internal static class XjAptitudeEffectRules
{
	internal static void EnsureBaseValuesBoundToAptitude(Actor actor, int xjZz)
	{
		if (actor?.data == null || xjZz < 1 || xjZz > 6)
		{
			return;
		}

		if (!TryResolveBaseRange(xjZz, out int mingShuMin, out int mingShuMax, out int huiGuangMin, out int huiGuangMax))
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		float rolledMingShu = XjDeterministicHash.RollRange(actorId, xjZz, 701, mingShuMin, mingShuMax);
		float rolledHuiGuang = XjDeterministicHash.RollRange(actorId, xjZz, 709, huiGuangMin, huiGuangMax);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);

		float nextCongenital = Math.Max((float)Math.Floor(Math.Max(0f, congenital)), rolledMingShu);
		float nextAcquired = (float)Math.Floor(Math.Max(0f, acquired));
		float nextHuiGuang = Math.Max((float)Math.Floor(Math.Max(0f, huiGuang)), rolledHuiGuang);
		XjMingShuState.Set(actor, nextCongenital, nextAcquired);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, nextHuiGuang);
	}

	internal static void ApplyOnAgeFiveResult(Actor actor, in XjAptitudeRollResult result)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (result.Passed)
		{
			EnsureBaseValuesBoundToAptitude(actor, result.XjZz);
			ApplyPrimaryAptitudeEffect(actor, result.XjZz);
		}

	}

	internal static void ApplyPrimaryAptitudeEffect(Actor actor, int xjZz)
	{
		if (xjZz < 1 || xjZz > 6)
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzEffectApplied, out int applied);
		if (applied != 0)
		{
			return;
		}

		if (!TryResolvePrimaryRange(xjZz, out int zhenYuanMin, out int zhenYuanMax, out int mingShuMin, out int mingShuMax))
		{
			return;
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		ApplyDelta(actor, XjDeterministicHash.RollRange(actorId, xjZz, 17, zhenYuanMin, zhenYuanMax), XjDeterministicHash.RollRange(actorId, xjZz, 23, mingShuMin, mingShuMax));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzEffectApplied, 1);
	}

	private static bool TryResolveBaseRange(
		int xjZz,
		out int mingShuMin,
		out int mingShuMax,
		out int huiGuangMin,
		out int huiGuangMax)
	{
		(mingShuMin, mingShuMax, huiGuangMin, huiGuangMax) = xjZz switch
		{
			1 => (18, 32, 16, 34),
			2 => (26, 42, 28, 48),
			3 => (38, 58, 45, 70),
			4 => (54, 76, 65, 90),
			5 => (70, 90, 85, 108),
			6 => (82, 100, 100, 120),
			_ => (0, 0, 0, 0)
		};
		return mingShuMax > 0 && huiGuangMax > 0;
	}

	private static void ApplyDelta(Actor actor, int zhenYuanDelta, int mingShuDelta)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);

		float nextZhenYuan = Math.Max(0f, ToInteger(zhenYuan) + zhenYuanDelta);
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		// 五岁资质初始化同样服从当前境界真元上限，不能在尚未胎息时预存超额真元。
		nextZhenYuan = XjCultivationGrowthRules.ApplyRealmCap(snapshot, nextZhenYuan);
		nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, nextZhenYuan);
		float nextAcquired = Math.Max(0f, ToInteger(acquired) + mingShuDelta);

		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
		XjMingShuState.Set(actor, congenital, nextAcquired);
	}

	private static bool TryResolvePrimaryRange(int xjZz, out int zhenYuanMin, out int zhenYuanMax, out int mingShuMin, out int mingShuMax)
	{
		switch (xjZz)
		{
			case 1:
				zhenYuanMin = 3;
				zhenYuanMax = 8;
				mingShuMin = 1;
				mingShuMax = 2;
				return true;
			case 2:
				zhenYuanMin = 8;
				zhenYuanMax = 18;
				mingShuMin = 2;
				mingShuMax = 4;
				return true;
			case 3:
				zhenYuanMin = 20;
				zhenYuanMax = 36;
				mingShuMin = 4;
				mingShuMax = 8;
				return true;
			case 4:
				zhenYuanMin = 48;
				zhenYuanMax = 68;
				mingShuMin = 6;
				mingShuMax = 12;
				return true;
			case 5:
				zhenYuanMin = 72;
				zhenYuanMax = 96;
				mingShuMin = 8;
				mingShuMax = 16;
				return true;
			case 6:
				zhenYuanMin = 110;
				zhenYuanMax = 140;
				mingShuMin = 10;
				mingShuMax = 20;
				return true;
			default:
				zhenYuanMin = 0;
				zhenYuanMax = 0;
				mingShuMin = 0;
				mingShuMax = 0;
				return false;
		}
	}

	private static float ToInteger(float value)
	{
		return (float)Math.Floor(Math.Max(0f, value));
	}

}
