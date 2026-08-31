using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 与附属剑意模组保持传承互斥。此处只识别公开 trait ID，不引用剑意模组程序集。
/// </summary>
internal static class XjJianDaoCompatibility
{
	private static readonly string[] JianDaoTraitIds =
	{
		"JianDao_R01_JianMang",
		"JianDao_R02_JianQi",
		"JianDao_R03_JianYuan",
		"JianDao_R04_JianYi",
		"JianDao_A01_XiuMu",
		"JianDao_A02_WanShi",
		"JianDao_A03_ZiYu",
		"JianDao_A04_JianTongShen"
	};

	internal static bool ShouldAllowCultivation(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (HasXuanJianCultivation(actor))
		{
			ReconcileExistingConflict(actor);
			return true;
		}

		return !HasJianDaoTrait(actor);
	}

	internal static bool ShouldBlockTraitGrant(Actor actor, string traitId)
	{
		if (XjExternalUnitTransferContext.IsTraitTransferActive) return false;
		if (actor?.data == null || string.IsNullOrWhiteSpace(traitId))
		{
			return false;
		}

		if (IsJianDaoTrait(traitId))
		{
			return HasXuanJianCultivation(actor);
		}

		if (!IsXuanJianCultivationTrait(traitId) || !HasJianDaoTrait(actor))
		{
			return false;
		}

		// Visible XuanJian traits are read-only projections and may be replayed by
		// third-party saved-unit/copier tools. Only the native manual trait editor,
		// internal projection sync, or an actor that already has authoritative
		// XuanJian cultivation state may resolve the sword-path conflict.
		if (XjManualTraitEditContext.IsActive
			|| XjCultivationStateTransitions.IsVisibleTraitSyncActive
			|| HasAuthoritativeXuanJianCultivation(actor))
		{
			ReconcileExistingConflict(actor);
		}
		return false;
	}

	private static bool IsJianDaoTrait(string traitId)
	{
		for (int i = 0; i < JianDaoTraitIds.Length; i++)
		{
			if (string.Equals(traitId, JianDaoTraitIds[i], StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasJianDaoTrait(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		for (int i = 0; i < JianDaoTraitIds.Length; i++)
		{
			if (actor.hasTrait(JianDaoTraitIds[i]))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasXuanJianCultivation(Actor actor)
	{
		return HasAuthoritativeXuanJianCultivation(actor);
	}

	private static bool HasAuthoritativeXuanJianCultivation(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjCultivationEligibility.HasCultivationMarkers(actor)
			|| XjCultivationEligibility.HasExplicitCultivationGrant(actor)) return true;
		return XjCultivationPathRules.TryGetPath(actor, out _);
	}

	private static bool IsXuanJianCultivationTrait(string traitId)
	{
		return XjAptitudeTraitLifecycle.IsAptitudeTrait(traitId)
			|| XjIdentityTraitLifecycle.IsIdentityTrait(traitId)
			|| string.Equals(traitId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(traitId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(traitId, "XjLongGengDaoTong", StringComparison.Ordinal)
			|| string.Equals(traitId, "XjLongGengDaoTong", StringComparison.Ordinal);
	}

	private static void ReconcileExistingConflict(Actor actor)
	{
		if (!HasJianDaoTrait(actor))
		{
			return;
		}

		for (int i = 0; i < JianDaoTraitIds.Length; i++)
		{
			string traitId = JianDaoTraitIds[i];
			if (actor.hasTrait(traitId))
			{
				actor.removeTrait(traitId);
			}
		}

		// 令剑意模组不再为已归入玄鉴体系的旧角色重新分配传承。
		XjActorStateWriteGateway.SetExternalFloat(actor, "jiandao.jiandaozhi", 0f, XjActorStateDomain.Craft);
		XjActorStateWriteGateway.SetExternalInt(actor, "jiandao.aptitude", -1, XjActorStateDomain.Craft);
		XjActorStateWriteGateway.SetExternalBool(actor, "jiandao.aptitude_finalized", true, XjActorStateDomain.Craft);
		XjActorStateWriteGateway.SetExternalBool(actor, "jiandao.is_sword_cultivator", false, XjActorStateDomain.Craft);
	}
}
