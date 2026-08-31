using System;
using UnityEngine;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTaiSpellScale
{
	internal const int ZiJinDaoTaiRadius = 120;
	internal const int FuQiDaoTaiRadius = 112;
	private const float DaoTaiDamageMultiplier = 2f;

	internal static bool IsDaoTaiActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			string normalized = XjRealmHelper.NormalizeId(realmId);
			if (!string.IsNullOrWhiteSpace(normalized))
			{
				return string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
					|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
			}
		}

		// Trait fallback is legacy/manual-import compatibility only. Once a valid RealmId
		// exists, a stale DaoTai trait must never grant DaoTai spell scale.
		try
		{
			return actor.hasTrait(XjRealmIds.DaoTai)
				|| actor.hasTrait(XjRealmIds.FuQiDaoTai);
		}
		catch (Exception ex)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiSpellScale.cs:34", ex);
			return false;
		}
	}

	internal static int ResolveTargetRadius(Actor caster, int baseRadius)
	{
		int normalizedBase = Math.Max(0, baseRadius);
		if (!IsDaoTaiActor(caster) || normalizedBase <= 0) return normalizedBase;
		// 兼容旧调用：只做温和放大，不再把任意局部技能强制扩成 84/90 格圆域。
		return Math.Max(normalizedBase, Math.Min(ResolveAbsoluteRadius(caster), Mathf.CeilToInt(normalizedBase * 1.5f)));
	}

	internal static int ResolveTargetRadius(Actor caster, in XjJinDanDaoSpellDefinition definition)
	{
		int baseRadius = Math.Max(0, definition.Radius);
		if (!IsDaoTaiActor(caster) || baseRadius <= 0) return baseRadius;

		int absoluteCap = ResolveAbsoluteRadius(caster);
		string mode = definition.TargetMode ?? string.Empty;
		if (string.Equals(mode, "Self", StringComparison.Ordinal)) return baseRadius;
		if (string.Equals(mode, "Single", StringComparison.Ordinal))
		{
			return Math.Max(baseRadius, Math.Min(absoluteCap, Mathf.CeilToInt(baseRadius * 1.75f)));
		}
		if (string.Equals(mode, "Limited", StringComparison.Ordinal))
		{
			return Math.Max(baseRadius, Math.Min(absoluteCap, Mathf.CeilToInt(baseRadius * 1.4f)));
		}

		// 真正的大范围/天灾型技能仍允许接近道胎位格范围；本身已经大于 112/120 的
		// 技能（如大日类）绝不反向缩小。
		if (baseRadius >= absoluteCap) return baseRadius;
		return Math.Max(baseRadius, Math.Min(absoluteCap, Mathf.CeilToInt(baseRadius * 1.5f)));
	}

	internal static int ResolveTerrainRadius(Actor caster, in XjJinDanDaoSpellDefinition definition)
	{
		int baseRadius = Math.Max(0, definition.TerrainRadius);
		if (!IsDaoTaiActor(caster) || baseRadius <= 0) return baseRadius;

		int absoluteCap = ResolveAbsoluteRadius(caster);
		if (baseRadius >= absoluteCap) return baseRadius;
		// 小型落点地形只扩大一半；中大型天灾地形也按原形同比放大并受道胎上限约束。
		// 通用雷法的 czar_bomba 是独立的高位天灾表现，不通过本函数重复放大。
		// 0.9.8.28 将绝对天灾半径提升到112/120，显著高于0.21金丹约60~85格的主爆区。
		const float scale = 1.5f;
		return Math.Max(baseRadius, Math.Min(absoluteCap, Mathf.CeilToInt(baseRadius * scale)));
	}

	internal static int ResolveAbsoluteRadius(Actor caster)
	{
		if (caster?.data == null) return ZiJinDaoTaiRadius;
		if (XjActorAccessor.TryGetString(caster, XjActorDataKeys.RealmId, out string realmId))
		{
			string normalized = XjRealmHelper.NormalizeId(realmId);
			if (string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal))
				return FuQiDaoTaiRadius;
			if (string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal))
				return ZiJinDaoTaiRadius;
		}
		try
		{
			if (caster.hasTrait(XjRealmIds.FuQiDaoTai)) return FuQiDaoTaiRadius;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjDaoTaiSpellScale.ResolveAbsoluteRadius.TraitFallback", ex);
		}
		return ZiJinDaoTaiRadius;
	}

	internal static float ResolveDamageMultiplier(Actor caster, float baseMultiplier)
	{
		float normalizedBase = Math.Max(0f, baseMultiplier);
		return IsDaoTaiActor(caster)
			? normalizedBase * DaoTaiDamageMultiplier
			: normalizedBase;
	}
}
