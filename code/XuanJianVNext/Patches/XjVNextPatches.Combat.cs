using System;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Talisman;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	private struct XjGetHitState
	{
		internal float BeforeHealth;
		internal float VampirePercent;
		internal bool EnteredRuntimePath;
		internal long SampleStarted;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(MapBox), "applyAttack")]
	public static bool XuanJianVNext_MapBox_ApplyAttack_ProjectileGuard_Prefix(AttackData pData, ref AttackDataResult __result)
	{
		if (XjProjectileGuard.IsAttackProjectileLookupSafe(pData))
		{
			return true;
		}

		__result = AttackDataResult.Miss;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(CombatActionLibrary), "attackRangeAction")]
	public static bool XuanJianVNext_CombatActionLibrary_AttackRangeAction_ProjectileGuard_Prefix(AttackData pData, ref bool __result)
	{
		if (XjProjectileGuard.IsRangeActionProjectileSafe(pData))
		{
			return true;
		}

		__result = false;
		return false;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "hasRangeAttack")]
	public static void XuanJianVNext_Actor_HasRangeAttack_InvalidProjectileFallback_Postfix(
		Actor __instance,
		ref bool __result)
	{
		if (!__result || __instance == null)
		{
			return;
		}

		try
		{
			if (!XjProjectileGuard.IsValidProjectileAssetId(__instance.getWeaponAsset()?.projectile))
			{
				__result = false;
			}
		}
		catch
		{
			__result = false;
		}
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "hasMeleeAttack")]
	public static void XuanJianVNext_Actor_HasMeleeAttack_InvalidProjectileFallback_Postfix(
		Actor __instance,
		ref bool __result)
	{
		if (__result || __instance == null)
		{
			return;
		}

		try
		{
			if (!XjProjectileGuard.IsValidProjectileAssetId(__instance.getWeaponAsset()?.projectile))
			{
				__result = true;
			}
		}
		catch
		{
			__result = true;
		}
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "setArmy")]
	public static void XuanJianVNext_Actor_SetArmy_HighRealmNativeArmyGuard_Postfix(Actor __instance)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}

		// 不能在原生 setArmy 写入前直接取消。原生可能已经创建 Army，
		// Prefix 拦截会留下 captain/city/kingdom 不完整的孤儿军队，保存时 Army.save 空引用。
		XjHighRealmArmyExclusion.DetachNowIfHighRealm(__instance);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), "checkArmyExistence")]
	public static void XuanJianVNext_City_CheckArmyExistence_ArmyReferenceGuard_Prefix(City __instance)
	{
		XjNativeArmySaveGuard.SanitizeCityArmyReference(__instance);
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(City), "checkArmyExistence")]
	public static Exception XuanJianVNext_City_CheckArmyExistence_ArmyReferenceGuard_Finalizer(
		City __instance,
		Exception __exception)
	{
		return XjNativeArmySaveGuard.RecoverCityArmyReference(__instance, __exception, "checkArmyExistence_nullref")
			? null
			: __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(City), "setArmy", new Type[] { typeof(Army) })]
	public static void XuanJianVNext_City_SetArmy_ArmyReferenceGuard_Prefix(City __instance)
	{
		XjNativeArmySaveGuard.SanitizeCityArmyReference(__instance);
	}

	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(City), "setArmy", new Type[] { typeof(Army) })]
	public static Exception XuanJianVNext_City_SetArmy_ArmyReferenceGuard_Finalizer(
		City __instance,
		Exception __exception)
	{
		return XjNativeArmySaveGuard.RecoverCityArmyReference(__instance, __exception, "setArmy_nullref")
			? null
			: __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ProjectileManager), "spawn")]
	public static bool XuanJianVNext_ProjectileManager_Spawn_ProjectileGuard_Prefix(
		BaseSimObject pInitiator,
		BaseSimObject pTargetObject,
		string pAssetID,
		ref Projectile __result)
	{
		// 坐标型/范围型投射物允许 target 为 null；只校验资源 ID。
		// 旧端点校验会把合法的无目标剑影、范围法术全部拦截，表现为远程角色完全打不动。
		if (XjProjectileGuard.IsValidProjectileAssetId(pAssetID))
		{
			return true;
		}

		__result = null;
		return false;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Projectile), "start")]
	public static bool XuanJianVNext_Projectile_Start_ProjectileGuard_Prefix(string pAssetID)
	{
		return XjProjectileGuard.IsValidProjectileAssetId(pAssetID);
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(Projectile), "update", new Type[] { typeof(float) })]
	public static bool XuanJianVNext_Projectile_Update_BrokenAssetGuard_Prefix(Projectile __instance)
	{
		if (!XjProjectileGuard.IsBrokenRuntimeProjectile(__instance))
		{
			return true;
		}

		XjProjectileGuard.RemoveBrokenProjectile(__instance);
		return false;
	}

	// 原生 EnemyFinder 在投射物跨越无效地图块时会从 updateVelocity 抛出
	// 空引用。Prefix 只能预筛当前坐标，无法覆盖本帧内的轨迹变化；这里
	// 只吞掉该已知异常并立即清理投射物，避免同一对象每帧重复写 Player.log。
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Projectile), "updateVelocity", new Type[] { typeof(float) })]
	public static Exception XuanJianVNext_Projectile_UpdateVelocity_NullChunkFinalizer(
		Projectile __instance,
		Exception __exception)
	{
		if (__exception is NullReferenceException)
		{
			XjProjectileGuard.RemoveBrokenProjectile(__instance);
			return null;
		}
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	[HarmonyPatch(typeof(ProjectileManager), "update")]
	public static void XuanJianVNext_ProjectileManager_Update_ProjectileGuard_Prefix(ProjectileManager __instance)
	{
		XjProjectileGuard.CullBrokenProjectiles(__instance);
	}

	// A loader can place another dynamic wrapper outside Projectile.updateVelocity,
	// bypassing that method's finalizer.  Retain a narrow manager-level fallback
	// for the known EnemyFinder null-reference so it cannot repeat hundreds of
	// times and turn a recoverable bad projectile into a process exit.
	[HarmonyFinalizer]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(ProjectileManager), "update")]
	public static Exception XuanJianVNext_ProjectileManager_Update_EnemyFinderFallback_Finalizer(
		ProjectileManager __instance,
		Exception __exception)
	{
		if (XjProjectileGuard.IsEnemyFinderNullReference(__exception))
		{
			XjProjectileGuard.RecoverFromEnemyFinderFailure(__instance);
			return null;
		}
		return __exception;
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "getHit")]
	private static bool XuanJianVNext_Actor_GetHit_UnifiedCombat_Prefix(
		Actor __instance,
		ref float pDamage,
		bool pFlash,
		AttackType pAttackType,
		BaseSimObject pAttacker,
		bool pSkipIfShake,
		bool pMetallicWeapon,
		ref bool pCheckDamageReduction,
		ref XjGetHitState __state)
	{
		__state = default;
		if (!XjSafeCore.IsAliveActor(__instance) || pDamage <= 0f)
		{
			return true;
		}
		Actor attacker = XjProjectileGuard.ResolveAttackSourceActor(pAttacker);
		long defenderId = ((BaseSystemData)__instance.data).id;
		int precheckDefenderTier = XjCultivatorCache.TryGetRealmTier(defenderId, out int cachedPrecheckDefenderTier)
			? cachedPrecheckDefenderTier
			: XjRealmSuppression.GetRealmTier(__instance);
		if (XjVanillaDeathGuard.ShouldBlockStarvationDamage(ref pDamage, __instance, pAttackType)
			|| XjVanillaDeathGuard.ShouldBlockFireDamage(ref pDamage, __instance, pAttackType, precheckDefenderTier))
		{
			return false;
		}
		// 闭关无敌属于权威状态，必须先于“双方均为普通角色”的兴趣索引快退。
		// 否则旧档/漏索引的闭关金丹会直接走原生伤害并被紫府击杀。
		if (XjClosedCultivationGuard.BlockIfClosedCultivation(ref pDamage, __instance, precheckDefenderTier))
		{
			return false;
		}
		// 延迟投射物命中时施法者可能已经死亡；只要攻击者对象与 data 仍存在，
		// 就必须保留其境界、命中、真伤等来源信息，不能降级为环境伤害。
		bool hasExternalAttacker = attacker?.data != null && attacker != __instance;
		long attackerId = hasExternalAttacker ? ((BaseSystemData)attacker.data).id : 0L;
		bool bootstrapPending = XjWorldBootstrapLane.HasPending;
		int repairedDefenderTier = XjRealmSuppression.TierNone;
		int repairedAttackerTier = XjRealmSuppression.TierNone;
		bool defenderKnownOrdinary = XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(defenderId);
		bool defenderInterested = XjRuntimeActorInterestIndex.HasCombatInterest(defenderId)
			|| (!defenderKnownOrdinary
				&& bootstrapPending
				&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(__instance));
		if (!defenderInterested && !defenderKnownOrdinary)
		{
			defenderInterested = XjRuntimeActorInterestIndex.TryRepairCombatInterest(
				__instance,
				out repairedDefenderTier);
		}
		bool attackerKnownOrdinary = hasExternalAttacker
			&& XjRuntimeActorInterestIndex.HasOrdinaryCombatClassification(attackerId);
		bool attackerInterested = hasExternalAttacker
			&& (XjRuntimeActorInterestIndex.HasCombatInterest(attackerId)
				|| (!attackerKnownOrdinary
					&& bootstrapPending
					&& XjRuntimeActorInterestIndex.MayRequireBootstrapInspection(attacker)));
		if (hasExternalAttacker && !attackerInterested && !attackerKnownOrdinary)
		{
			attackerInterested = XjRuntimeActorInterestIndex.TryRepairCombatInterest(
				attacker,
				out repairedAttackerTier);
		}

		// 无玄鉴标记的普通单位直接使用原生战斗。索引缺失的玄鉴角色已在
		// 上方冷分支自愈，不能再因旧档或旁路漏注册而绕过境界压制。
		if (!defenderInterested && !attackerInterested)
		{
			return true;
		}


		int defenderTier = repairedDefenderTier > XjRealmSuppression.TierNone
			? repairedDefenderTier
			: defenderInterested
				? XjCultivatorCache.TryGetRealmTier(defenderId, out int cachedDefenderTier)
					? cachedDefenderTier
					: XjRealmSuppression.GetRealmTier(__instance)
				: XjRealmSuppression.TierNone;
		int attackerTier = repairedAttackerTier > XjRealmSuppression.TierNone
			? repairedAttackerTier
			: attackerInterested
				? XjCultivatorCache.TryGetRealmTier(attackerId, out int cachedAttackerTier)
					? cachedAttackerTier
					: XjRealmSuppression.GetRealmTier(attacker)
				: defenderTier > XjRealmSuppression.TierNone
					? XjRealmSuppression.GetRealmTier(attacker)
					: XjRealmSuppression.TierNone;


		__state.EnteredRuntimePath = true;
		__state.SampleStarted = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.GetHit, 511);
		XjCombatHotProfile defenderProfile = XjCombatHotPathCache.Get(__instance);

		// bowling_ball 隔离按伤害载体本身判断，必须先于攻击者分流。
		// 否则能解析到施法者的投射物会绕过原有屏蔽规则。
		if (XjProjectileGuard.ShouldBlock(ref pDamage, pAttacker, __instance))
		{
			return false;
		}

		if (!hasExternalAttacker)
		{
			// 投射物/战斗载体存在但施法者引用已失效时，不能降级成环境伤害。
			// 否则低境角色射出投射物后死亡、卸载或被清理，命中高境目标时
			// 会绕过攻击者境界判定。无法确认来源境界时，对修士采取安全失败：
			// 该次孤儿战斗载体不造成伤害。真正的火灾、毒、溺水等环境伤害
			// 仍继续走下方环境结算。
			if (defenderTier > XjRealmSuppression.TierNone
				&& XjProjectileGuard.IsCombatDeliverySource(pAttacker))
			{
				pDamage = 0f;
				return false;
			}

			bool useXjDefense = defenderTier > XjRealmSuppression.TierNone
				|| defenderProfile.HasCustomCombatStats
				|| defenderProfile.HasImmortalityProtection;
			if (!useXjDefense)
			{
				return true;
			}

			// 无可解析攻击者的环境伤害与自伤走玄鉴防御层。
			// 可解析到施法者的延迟投射物已在上方按角色攻击处理。
			pCheckDamageReduction = false;
			// 环境伤害只做单次上限控制，不再设置 5% 生命保底。
			// 任何来源在持续造成合法伤害后都必须能够杀死目标。
			XjVanillaDeathGuard.ApplyNonActorDamageCap(ref pDamage, __instance, pAttacker, pAttackType, defenderTier);

			XjCombatResolution environmentResolution = XjCombatDamageResolver.ResolveEnvironmentDamage(
				ref pDamage,
				__instance,
				defenderTier,
				defenderProfile);
			return environmentResolution.ShouldRunOriginal && pDamage > 0f;
		}

		XjCombatHotProfile attackerProfile = XjCombatHotPathCache.Get(attacker);

		bool useXjCombat = attackerTier > XjRealmSuppression.TierNone
			|| defenderTier > XjRealmSuppression.TierNone
			|| attackerProfile.HasCustomCombatStats
			|| defenderProfile.HasCustomCombatStats
			|| attackerProfile.IsJinXingYaoXie
			|| defenderProfile.IsJinXingYaoXie;
		if (!useXjCombat)
		{
			return true;
		}

		// FantasyWorld 的单入口结算与 Guigu 的真伤/压制思路在此收束：
		// 最终伤害先由玄鉴计算，原生 getHit 只负责扣血、死亡和归因。
		pCheckDamageReduction = false;
		__state.VampirePercent = Mathf.Clamp(attackerProfile.Vampire, 0f, 100f);
		if (__state.VampirePercent > 0f)
		{
			__state.BeforeHealth = XjSafeCore.GetHealthSafe(__instance, 0f);
		}

		XjCombatResolution resolution = XjCombatDamageResolver.ResolveActorAttack(
			ref pDamage,
			attacker,
			__instance,
			attackerTier,
			defenderTier,
			attackerProfile,
			defenderProfile);

		if (resolution.Kind == XjCombatResolutionKind.Dodged)
		{
			try
			{
				__instance.startColorEffect(ActorColorEffect.White);
			}
			catch
			{
			}
			return false;
		}

		if (!resolution.ShouldRunOriginal || pDamage <= 0f)
		{
			return false;
		}

		XjTalismanCombatService.ApplyActorDamageTalismans(
			attacker,
			__instance,
			ref pDamage);
		if (pDamage <= 0f) return false;

		XjCombatTracker.RecordAttacker(__instance, attacker, defenderTier, attackerTier);
		bool isJinDanCombat = attackerTier >= XjRealmSuppression.TierJinDan
			|| defenderTier >= XjRealmSuppression.TierJinDan;
		if (isJinDanCombat && XjTrueDamageSystem.TryExecuteYinSiDeathSeal(__instance, attacker))
		{
			return false;
		}

		int currentYear = Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0));
		XjAlchemyPillEffectSystem.TryUseCombatHealingBeforeDamage(__instance, pDamage, currentYear);
		// 天下熯只记录已经通过玄鉴伤害链的本次伤害，用于领域终结脉冲；
		// 该值仅存在于活跃领域实例中，结束或读档即清空。
		XjDomainSkillRuntime.RecordResolvedIncomingDamage(__instance, pDamage);
		return true;
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	[HarmonyPatch(typeof(Actor), "getHit")]
	private static void XuanJianVNext_Actor_GetHit_UnifiedCombat_Postfix(
		Actor __instance,
		float pDamage,
		BaseSimObject pAttacker,
		XjGetHitState __state)
	{
		if (!__state.EnteredRuntimePath)
		{
			return;
		}

		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.GetHit, __state.SampleStarted);

		Actor attacker = XjProjectileGuard.ResolveAttackSourceActor(pAttacker);
		if (!XjSafeCore.IsAliveActor(attacker) || attacker == __instance || __state.VampirePercent <= 0f)
		{
			return;
		}

		// 按目标真实损失生命结算吸血；目标被本击杀死时仍然有效。
		float beforeHealth = __state.BeforeHealth;
		float afterHealth = XjSafeCore.GetHealthSafe(__instance, 0f);
		float damageDone = beforeHealth > afterHealth ? beforeHealth - afterHealth : 0f;
		if (damageDone <= 0f)
		{
			return;
		}

		XjSafeCore.HealActorSafe(attacker, damageDone * __state.VampirePercent / 100f);
	}
}
