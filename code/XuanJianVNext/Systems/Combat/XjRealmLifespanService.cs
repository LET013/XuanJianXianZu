using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 玄鉴权威寿元结算。
///
/// 原生寿元始终是角色未入玄鉴境界时的权威值；半妖、帝/妖意向等身份
/// 只使用原生特质乘区，绝不把该值改写成玄鉴的凡俗基数。
///
/// 只有已具备玄鉴境界或释修寿数的文明角色，才在真实 updateStats 重建中
/// 投影对应境界寿限。龙属和妖属大圣仍完整交给各自的原生 ActorAsset。
/// </summary>
internal static class XjRealmLifespanService
{
	internal const float MortalCivilianLifespanYears = XjZiXuanBorrowedLifespanEvent.MortalLifespanCapYears;

	private const float TaiXiBaseBonus = 10f;
	private const float LianQiBaseBonus = 100f;
	private const float ZhuJiBaseBonus = 200f;
	// 斩养之劫后的凡俗根基为80年；紫府完整标准寿限固定为500年。
	private const float ZiFuBaseBonus = 420f;
	private const float JinDanBaseBonus = 3000f;
	private const float HuangGuanBaseBonus = 400f;
	private const float FuQiZhenRenBaseBonus = 900f;
	private const float ZhenJunYuShiEarlyBonus = 6000f;
	private const float ZhenJunYuShiMiddleBonus = 7200f;
	private const float ZhenJunYuShiLateBonus = 8640f;
	private const float ZhenJunYuShiPeakBonus = 10368f;

	private static readonly HashSet<long> RepairingActorIds = new HashSet<long>();

	internal static void Clear()
	{
		RepairingActorIds.Clear();
	}

	internal static void MarkDirtyWhenStale(Actor actor)
	{
		if (!NeedsRefresh(actor)) return;
		try { actor.setStatsDirty(); } catch (System.Exception xjCaught53) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:53", xjCaught53); }
	}

	internal static void EnsureFreshStats(Actor actor)
	{
		if (!NeedsRefresh(actor) || actor?.data == null) return;

		long actorId;
		try { actorId = ((BaseSystemData)actor.data).id; }
		catch { return; }
		if (actorId <= 0L || !RepairingActorIds.Add(actorId)) return;

		try
		{
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (System.Exception xjCaught70) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:70", xjCaught70); }
		finally { RepairingActorIds.Remove(actorId); }
	}

	/// <summary>
	/// Called only inside the real Actor.updateStats rebuild, immediately before
	/// BaseStats.normalize. Civilized non-cultivators use the mod's fixed eighty-year
	/// baseline; non-civilized assets (including dragons and Great Sages) stay native.
	/// </summary>
	internal static void ApplyRuntimeRealmLifespan(Actor actor)
	{
		if (actor?.data == null || actor.stats == null
			|| !IsCivilizedActor(actor)
			|| XjLongShuSystem.IsLongShu(actor))
		{
			return;
		}

		if (XjCultivationPathRules.IsShi(actor))
		{
			float shiLifespan = XjShiPowerRules.GetLifespanYears(actor);
			actor.stats["lifespan"] = shiLifespan + ResolveManagedTraitLifespanBonus(actor);
			return;
		}

		string realmId = ResolveExactRealmId(actor);
		float realmBonus = GetExpectedRealmBonus(actor, realmId);
		if (realmBonus <= 0f)
		{
			if (IsMortalCivilian(actor)) actor.stats["lifespan"] = MortalCivilianLifespanYears;
			return;
		}

		float managedTraitBonus = ResolveManagedTraitLifespanBonus(actor);
		actor.stats["lifespan"] = ResolveRealmBaselineLifespan(realmId, realmBonus) + managedTraitBonus;
	}

	/// <summary>
	/// Fast ordinary-actor path used from Actor.updateStats. Once runtime interest
	/// indexing has finished, a non-indexed civilized actor cannot carry玄鉴 realm,
	/// Shi, LongShu or managed lifespan state. Avoid parsing those markers on every
	/// native stat rebuild; only interested/bootstrap candidates take the full path.
	/// </summary>
	internal static void ApplyRuntimeRealmLifespanFast(Actor actor, bool hasRuntimeStatInterest, bool bootstrapCandidate)
	{
		if (actor?.stats == null) return;
		if (!hasRuntimeStatInterest && !bootstrapCandidate)
		{
			if (IsMortalCivilian(actor)) actor.stats["lifespan"] = MortalCivilianLifespanYears;
			return;
		}
		ApplyRuntimeRealmLifespan(actor);
	}

	internal static void ClampFinalMortalCivilianLifespanFast(Actor actor, bool hasRuntimeStatInterest, bool bootstrapCandidate)
	{
		if (actor?.stats == null) return;
		if (!hasRuntimeStatInterest && !bootstrapCandidate)
		{
			if (IsMortalCivilian(actor)) actor.stats["lifespan"] = MortalCivilianLifespanYears;
			return;
		}
		ClampFinalMortalCivilianLifespan(actor);
	}

	internal static void ClampFinalMortalCivilianLifespan(Actor actor)
	{
		if (actor?.stats == null || !IsMortalCivilian(actor)) return;

		actor.stats["lifespan"] = MortalCivilianLifespanYears;
	}

	internal static float GetExpectedRealmBonus(Actor actor)
	{
		if (XjCultivationPathRules.IsShi(actor))
		{
			return Math.Max(0f, XjShiPowerRules.GetLifespanYears(actor) - MortalCivilianLifespanYears);
		}
		return GetExpectedRealmBonus(actor, ResolveExactRealmId(actor));
	}

	/// <summary>
	/// 阴司使用的有限寿数必须与角色面板的玄鉴寿元口径一致：
	/// 先结算当前精确境界基础寿限与玄鉴血脉寿元，再乘法宝寿元倍率，
	/// 最后追加丹药固定延寿。不得再单独套用金丹意象档位。
	/// </summary>
	internal static float GetFiniteYinSiAttentionLifespan(Actor actor)
	{
		if (actor?.data == null) return 0f;

		if (XjCultivationPathRules.IsShi(actor))
		{
			float shiBaseline = XjShiPowerRules.GetLifespanYears(actor)
				+ ResolveManagedTraitLifespanBonus(actor);
			return Math.Max(0f, XjFaBaoBonusService.GetEffectiveLifespan(actor, shiBaseline));
		}

		string realmId = ResolveExactRealmId(actor);
		float realmBonus = GetExpectedRealmBonus(actor, realmId);
		if (realmBonus <= 0f) return 0f;

		float finiteBaseline = ResolveRealmBaselineLifespan(realmId, realmBonus)
			+ ResolveManagedTraitLifespanBonus(actor);
		float effective = XjFaBaoBonusService.GetEffectiveLifespan(actor, finiteBaseline);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjAlchemyLifespanBonus, out int alchemyBonus)
			&& alchemyBonus > 0)
		{
			effective += alchemyBonus;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiShenDanLifespanBonus, out int daoTaiShenDanBonus)
			&& daoTaiShenDanBonus > 0)
		{
			effective += daoTaiShenDanBonus;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHouShenShuLifespanBonus, out int houShenShuBonus)
			&& houShenShuBonus > 0)
		{
			effective += houShenShuBonus;
		}
		return Math.Max(0f, effective);
	}

	internal static bool IsMortalCivilian(Actor actor)
	{
		if (actor?.data == null || !IsCivilizedActor(actor) || XjLongShuSystem.IsLongShu(actor))
		{
			return false;
		}

		if (XjCultivationPathRules.IsShi(actor)) return false;

		long actorId;
		try { actorId = ((BaseSystemData)actor.data).id; }
		catch { return false; }

		int cachedTier = XjRealmSuppression.TierNone;
		bool hasCachedTier = actorId > 0L
			&& XjCultivatorCache.TryGetRealmTier(actorId, out cachedTier);
		if (hasCachedTier && cachedTier > XjRealmSuppression.TierNone)
		{
			return false;
		}

		// 境界字段优先于“缓存为0”：手动赋予、登名石恢复和突破提交时
		// 可能先写入权威境界，再在同一事务末尾刷新缓存。
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjRealmHelper.IsKnownTag(realmId))
		{
			return false;
		}

		if (hasCachedTier)
		{
			return true;
		}

		// During bounded post-load bootstrap, only persisted玄鉴 candidates pay the
		// full realm fallback. Ordinary civilians remain an O(1) asset/data check.
		if (XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor))
		{
			return XjRealmSuppression.GetRealmTier(actor) <= XjRealmSuppression.TierNone;
		}

		return true;
	}

	private static bool NeedsRefresh(Actor actor)
	{
		if (actor?.data == null || actor.stats == null
			|| !IsCivilizedActor(actor)
			|| XjLongShuSystem.IsLongShu(actor))
		{
			return false;
		}

		if (XjCultivationPathRules.IsShi(actor))
		{
			float shiExpected = XjShiPowerRules.GetLifespanYears(actor) + ResolveManagedTraitLifespanBonus(actor);
			float shiCurrent = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
			return shiCurrent + 0.01f < shiExpected;
		}

		float current = XjSafeCore.GetStatSafe(actor, "lifespan", 0f);
		string realmId = ResolveExactRealmId(actor);
		float realmBonus = GetExpectedRealmBonus(actor, realmId);
		if (realmBonus <= 0f)
		{
			return IsMortalCivilian(actor)
				&& (Math.Abs(current - MortalCivilianLifespanYears) > 0.01f
					|| float.IsNaN(current) || float.IsInfinity(current));
		}

		float minimumExpected = ResolveRealmBaselineLifespan(realmId, realmBonus)
			+ ResolveManagedTraitLifespanBonus(actor);

		if (current + 0.01f < minimumExpected)
		{
			return true;
		}

		// Cultivators may legitimately exceed the baseline through玄鉴丹药、法宝或
		// 剑道加成，所以不做上界比较。
		return false;
	}

	private static string ResolveExactRealmId(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		if (XjAnnualExecutionContext.TryResolveRealmOverride(actor, out string overrideRealmId))
		{
			return XjRealmHelper.NormalizeId(overrideRealmId);
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			string normalized = XjRealmHelper.NormalizeId(realmId);
			if (normalized.Length > 0) return normalized;
		}

		if (XjWorldBootstrapLane.HasPending
			&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(actor))
		{
			return XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		}
		return string.Empty;
	}

	private static float ResolveRealmBaselineLifespan(string realmId, float realmBonus)
	{
		if (realmBonus <= 0f) return MortalCivilianLifespanYears;
		// 用户给定的真君羽士四档是完整基础寿限，不再额外叠加八十年，
		// 从而初/中/后/巅在无其他加成时严格为 6000/7200/8640/10368。
		return string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			? realmBonus
			: MortalCivilianLifespanYears + realmBonus;
	}

	private static float GetExpectedRealmBonus(Actor actor, string realmId)
	{
		if (IsZiFuJinDanGoldenRealm(realmId))
		{
			return ResolveJinDanLifespanBonus(actor);
		}
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			return ResolveZhenJunYuShiLifespanBonus(actor);
		}
		return GetBaseRealmBonus(realmId);
	}

	private static bool IsZiFuJinDanGoldenRealm(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static float ResolveJinDanLifespanBonus(Actor actor)
	{
		if (actor?.data == null) return JinDanBaseBonus;

		int yiXiang = 0;
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out yiXiang); } catch (System.Exception xjCaught253) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:253", xjCaught253); }
		try { yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, yiXiang); } catch (System.Exception xjCaught254) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:254", xjCaught254); }

		if (yiXiang >= 6000) return 6000f;
		if (yiXiang >= 3000) return 5000f;
		if (yiXiang >= 1000) return 4000f;
		return JinDanBaseBonus;
	}

	private static float ResolveZhenJunYuShiLifespanBonus(Actor actor)
	{
		if (actor?.data == null) return ZhenJunYuShiEarlyBonus;

		int yiXiang = 0;
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out yiXiang); } catch (System.Exception xjCaught267) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:267", xjCaught267); }
		try { yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, yiXiang); } catch (System.Exception xjCaught268) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmLifespanService.cs:268", xjCaught268); }

		if (yiXiang >= 6000) return ZhenJunYuShiPeakBonus;
		if (yiXiang >= 3000) return ZhenJunYuShiLateBonus;
		if (yiXiang >= 1000) return ZhenJunYuShiMiddleBonus;
		return ZhenJunYuShiEarlyBonus;
	}

	private static float ResolveManagedTraitLifespanBonus(Actor actor)
	{
		if (actor?.data == null) return 0f;

		float bonus = 0f;
		if (HasTrait(actor, "ChuShen8")) bonus += 100f;
		else if (HasTrait(actor, "ChuShen7")) bonus += 50f;
		else if (HasTrait(actor, "ChuShen6")) bonus += 30f;
		else if (HasTrait(actor, "ChuShen5")) bonus += 20f;
		else if (HasTrait(actor, "ChuShen4")) bonus += 20f;
		else if (HasTrait(actor, "ChuShen3")) bonus += 15f;
		else if (HasTrait(actor, "ChuShen2")) bonus += 10f;
		else if (HasTrait(actor, "ChuShen1")) bonus += 5f;

		if (HasTrait(actor, "XjDaoTaiDescendant")) bonus += 30f;
		else if (HasTrait(actor, "XjJinDanDescendant")) bonus += 20f;
		else if (HasTrait(actor, "XjZiFuDescendant")) bonus += 10f;
		return bonus;
	}

	private static float ResolveNativeAssetLifespan(Actor actor)
	{
		try
		{
			float value = actor?.asset?.base_stats?["lifespan"] ?? 0f;
			return value > 0f && !float.IsNaN(value) && !float.IsInfinity(value) ? value : 0f;
		}
		catch
		{
			return 0f;
		}
	}

	private static bool IsCivilizedActor(Actor actor)
	{
		try { return actor?.asset != null && actor.asset.civ; }
		catch { return false; }
	}

	private static bool HasTrait(Actor actor, string traitId)
	{
		try
		{
			return actor?.data != null
				&& !string.IsNullOrWhiteSpace(traitId)
				&& actor.hasTrait(traitId);
		}
		catch
		{
			return false;
		}
	}

	private static float GetBaseRealmBonus(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return TaiXiBaseBonus;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return LianQiBaseBonus;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return ZhuJiBaseBonus;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return ZiFuBaseBonus;
		if (IsZiFuJinDanGoldenRealm(realmId)) return JinDanBaseBonus;
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return HuangGuanBaseBonus;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return FuQiZhenRenBaseBonus;
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return ZhenJunYuShiEarlyBonus;
		return 0f;
	}
}
