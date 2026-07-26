using System;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 0.5.4-compatible MingShu storage: total is always congenital + acquired.
/// Acquired MingShu is a nonnegative integer increment and must not silently
/// become an independent second total.
/// </summary>
internal static class XjMingShuState
{
	internal static float NormalizeInteger(float value)
	{
		if (float.IsNaN(value) || float.IsInfinity(value)) return 0f;
		return (float)Math.Floor(value);
	}

	internal static void AddAcquired(Actor actor, float delta)
	{
		if (actor?.data == null || float.IsNaN(delta) || float.IsInfinity(delta) || Math.Abs(delta) < 0.001f) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		Set(actor, congenital, acquired + delta);
	}

	internal static void Set(Actor actor, float congenital, float acquired)
	{
		if (actor?.data == null) return;
		float normalizedCongenital = Math.Max(0f, NormalizeInteger(congenital));
		float normalizedAcquired = Math.Max(0f, NormalizeInteger(acquired));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuCongenital, normalizedCongenital);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, normalizedAcquired);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShu, Math.Max(0f, normalizedCongenital + normalizedAcquired));
	}

	internal static void Normalize(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		Set(actor, congenital, acquired);
	}
}
