using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 沿用0.5.4的命数口径：后天命数只记录真实获得或消耗，
/// 总命数始终由先天命数与后天命数相加得到，并同步旧档键。
/// </summary>
internal static class XjMingShuState
{
	internal const float MaximumCongenital = 100f;
	internal const string LegacyTotalKey = "wulin.xueQiNum";
	internal const string LegacyCongenitalKey = "wulin.xueQiCongenital";
	internal const string LegacyAcquiredKey = "wulin.xueQiAcquired";

	internal static float NormalizeInteger(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
		return (float)Math.Floor(value);
	}

	internal static void AddAcquired(Actor actor, float delta)
	{
		if (actor?.data == null || float.IsNaN(delta) || float.IsInfinity(delta) || Math.Abs(delta) < 0.001f) return;
		ReadComponents(actor, out float congenital, out float acquired);
		Write(actor, congenital, Math.Max(0f, acquired + delta));
	}

	internal static void Set(Actor actor, float congenital, float acquired)
	{
		Write(actor, congenital, acquired);
	}

	internal static void Normalize(Actor actor)
	{
		if (actor?.data == null) return;
		ReadComponents(actor, out float congenital, out float acquired);
		Write(actor, congenital, acquired);
	}

	private static void ReadComponents(Actor actor, out float congenital, out float acquired)
	{
		congenital = 0f;
		acquired = 0f;
		if (actor?.data == null) return;

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out acquired);
		BaseSystemData data = (BaseSystemData)actor.data;
		float legacyCongenital = 0f;
		float legacyAcquired = 0f;
		float legacyTotal = 0f;
		data.get(LegacyCongenitalKey, out legacyCongenital, 0f);
		data.get(LegacyAcquiredKey, out legacyAcquired, 0f);
		data.get(LegacyTotalKey, out legacyTotal, 0f);

		if (congenital <= 0f && legacyCongenital > 0f) congenital = legacyCongenital;
		if (acquired <= 0f && legacyAcquired > 0f) acquired = legacyAcquired;
		if (congenital <= 0f && acquired <= 0f && legacyTotal > 0f) congenital = legacyTotal;
		congenital = Math.Max(0f, NormalizeInteger(congenital));
		acquired = Math.Max(0f, NormalizeInteger(acquired));
	}

	private static void Write(Actor actor, float congenital, float acquired)
	{
		if (actor?.data == null) return;
		float rawCongenital = Math.Max(0f, NormalizeInteger(congenital));
		float normalizedAcquired = Math.Max(0f, NormalizeInteger(acquired));
		// 先天命数是出生底盘，绝不能超过100。转世、龙属或旧档曾写入的
		// 超额部分属于前世/血脉带来的后天积累，迁入后天命数但保留总量。
		float overflow = Math.Max(0f, rawCongenital - MaximumCongenital);
		float normalizedCongenital = Math.Min(MaximumCongenital, rawCongenital);
		normalizedAcquired += overflow;
		float total = normalizedCongenital + normalizedAcquired;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuCongenital, normalizedCongenital);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, normalizedAcquired);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShu, total);
		XjActorStateWriteGateway.SetExternalFloat(actor, LegacyCongenitalKey, normalizedCongenital, XjActorStateDomain.Progression);
		XjActorStateWriteGateway.SetExternalFloat(actor, LegacyAcquiredKey, normalizedAcquired, XjActorStateDomain.Progression);
		XjActorStateWriteGateway.SetExternalFloat(actor, LegacyTotalKey, total, XjActorStateDomain.Progression);
	}
}
