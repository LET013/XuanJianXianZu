using System;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.XianGuo;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 所有服气道途共用的性命双修状态机。它只处理黄冠求身、真人圆满、
/// 金性自然化生、求证与失败养伤；如何取得本命核心由各Handler负责。
/// </summary>
internal static class XjFuQiCultivationSystem
{
	private const int MinimumJinDanAttemptIntervalYears = 10;

	internal static void TickActor(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || currentYear <= 0 || !XjCultivationPathRules.IsFuQiYangXing(actor)) return;
		if (string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
		{
			XjFuQiSwordWorldState.EnsureEstablishedDaoIdentity(actor, true);
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			TickHuangGuan(actor, currentYear, definition);
			return;
		}
		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			TickZhenRen(actor, currentYear, definition);
		}
	}

	internal static string BuildDisplaySummary(Actor actor, in XjFuQiCoreDefinition definition, int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor)) return string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalized = XjRealmHelper.NormalizeId(realmId);
		StringBuilder builder = new StringBuilder(256);
		if (!string.IsNullOrWhiteSpace(definition.DisplayName))
		{
			builder.Append("本命核心：").AppendLine(definition.DisplayName);
		}

		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			int trueSpirit = ReadTrueSpiritForDisplay(actor);
			builder.Append("真灵值：").Append(trueSpirit).AppendLine(" · 上限3");
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent, out int reincarnationBonus)
				&& reincarnationBonus > 0)
			{
				builder.Append("金性转世求证余荫：").AppendLine(DescribeDisplayAid(reincarnationBonus));
			}
		}

		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, out int completeYear) && completeYear > 0)
			{
				builder.Append("求身：神妙归身");
				AppendRemainingYears(builder, currentYear, completeYear);
				builder.AppendLine();
			}
		}
		else if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			AppendZhenRenStatus(actor, builder, currentYear, definition);
		}
		else if (string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			builder.Append("道途：").AppendLine(string.IsNullOrWhiteSpace(daoTu) ? "服气养性" : daoTu.Trim());
			if (string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
			{
				builder.AppendLine("道位：长庚果位");
			}
		}
		return builder.ToString().TrimEnd();
	}

	private static void TickHuangGuan(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachZhenRen(aptitude)) return;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, out int completeYear) || completeYear <= 0)
		{
			int duration = ResolveBodyCultivationYears(actor, aptitude, definition.DaoTuRootId);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectStartYear, currentYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, currentYear + duration);
			return;
		}
		if (currentYear < completeYear) return;
		if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true)) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, 0);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.FuQiZhenRen, currentYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.FuQiZhenRen, daoTu);
		if (IsLongGeng(definition)) XjThreeBookWriter.RecordFuQiZhenRen(actor, currentYear);
		else XjThreeBookWriter.RecordFuQiCoreZhenRen(actor, definition.DisplayName, currentYear);
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			XjAnnouncementText.BuildFuQiZhenRenPromotion(actor, daoTu, definition.DisplayName),
			iconId: XjEventIconCatalog.ZiFuUpgrade,
			category: XjAnnouncementCategory.HighRealm);
		XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.FuQiZhenRen, "FuQiZhenRenPromotion");
		// 五品路线在晋升真人当年立即定路，避免圆满数百年后才高龄转紫府。
		if (TryResolveRank5HighRealmRoute(actor, currentYear, aptitude, definition)) return;
		EnsurePerfectionProject(actor, currentYear, aptitude, definition.DaoTuRootId);
	}

	private static void TickZhenRen(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (!XjFuQiAptitudeRules.CanReachZhenRen(aptitude)) return;

		// 旧档中尚未定路的五品真人在下一年度立即补做分流。
		if (TryResolveRank5HighRealmRoute(actor, currentYear, aptitude, definition)) return;

		// 真人不像紫府那样在“获得新神通”的离散事件上自然触发属性重建，
		// 性命合炼是按年份连续推进的。年度真人结算必须检查一次修持档指纹，
		// 只有跨过25/50/75/100%档位时才 setStatsDirty，避免长期停留在初成真人面板。
		XjRealmStageStatMultiplierService.MarkDirtyWhenStale(actor);

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectionYear);
		if (perfectionYear <= 0)
		{
			EnsurePerfectionProject(actor, currentYear, aptitude, definition.DaoTuRootId);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, out int completeYear);
			if (completeYear <= 0 || currentYear < completeYear) return;
			CompleteShenMiaoPerfection(actor, currentYear, aptitude, definition);
		}


		if (IsLongGeng(definition))
		{
			if (!XjFuQiSwordWorldState.HasCurrentHolder)
			{
				XjFuQiLongGengPositionHandler.ProcessYear(currentYear);
				return;
			}
		}

		if (!TryPrepareJinDanCandidate(actor, currentYear, definition, out aptitude)) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanLastAttemptYear, currentYear);
		float chance = ResolveJinDanSuccessChance(actor, aptitude);
		long actorId = ((BaseSystemData)actor.data).id;
		bool recognized = chance > 0f
			&& XjDeterministicHash.Roll01(actorId, currentYear, "fuqi_generic_jindan", definition.DaoTuRootId) < chance;
		if (recognized)
		{
			bool completed = CompleteGenericZhenJun(actor, currentYear, definition);
			// 神丹是共享高境挂靠位，不再按紫金/服气或五品/六品排除。
			// 只要本次已具备求真君资格、目标道途存在可挂靠果位且容量允许，就可成神丹。
			if (!completed) completed = TryCompleteSharedShenDan(actor, currentYear, definition);
			if (completed) return;
		}
		// 五品功法不转结璘仙，避免“继续求真君/转修紫金”之外再分出第三条路线。
		if (aptitude == 6 && XjXuanJianShenTongSpecials.TryResolveJieLinXianOnFuQiFailure(actor, currentYear)) return;
		ResolveJinDanFailure(actor, currentYear, definition);
	}

	private static bool TryResolveRank5HighRealmRoute(
		Actor actor,
		int currentYear,
		int aptitude,
		in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null || aptitude != 5) return false;
		_ = definition;

		// RC11.10：五品真人不再以固定65%概率提前放弃服气。只有真君前景明显偏低，
		// 而转紫金后仍有显著更高的“一求金丹”机会时，人物才可能主动重择道路。
		bool wantsZiJin = XjCultivationPathSwitchPolicy.ShouldRank5FuQiSwitchToZiJin(
			actor, currentYear, aptitude, out _, out _);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, 1);

		if (!wantsZiJin)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 1);
			return false;
		}

		// 只有目标紫金道途当前真实可承接且事务成功，才算人物真正舍弃服气。
		// 若世界条件暂时不允许转修，就继续按本路尝试真君，不能把角色卡死在“想转但转不了”。
		if (!TryAutoConvertRank5ToZiFu(actor))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 1);
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 0);
		return true;
	}

	private static bool TryAutoConvertRank5ToZiFu(Actor actor)
	{
		var options = XjFuQiToZiFuTransitionSystem.BuildTargetOptions(actor);
		if (options == null || options.Count == 0) return false;
		XjFuQiToZiFuTargetOption selected = options[0];
		for (int i = 0; i < options.Count; i++)
		{
			if (options[i].IsCurrentDaoTu) { selected = options[i]; break; }
			if (options[i].IsCurrentRoot) selected = options[i];
		}
		if (!XjFuQiToZiFuTransitionSystem.TryConvert(actor, selected.DaoTu, out XjFuQiToZiFuResult result)
			|| !result.Success) return false;
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			"自度真君之路已窄，而紫金尚存一线求金之机，此身遂舍服气之路，转入" + result.TargetDaoTu + "紫府金丹道。",
			iconId: XjEventIconCatalog.ZiFuUpgrade,
			category: XjAnnouncementCategory.HighRealm);
		return true;
	}

	private static void EnsurePerfectionProject(Actor actor, int currentYear, int aptitude, string rootId)
	{
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, out int existing) && existing > 0) return;
		int duration = ResolvePerfectionYears(actor, aptitude, rootId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, currentYear + duration);
	}

	internal static void EnsureManualPerfectionProject(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		EnsurePerfectionProject(actor, currentYear, aptitude, definition.DaoTuRootId);
	}

	private static void CompleteShenMiaoPerfection(
		Actor actor,
		int currentYear,
		int aptitude,
		in XjFuQiCoreDefinition definition)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, currentYear);
		// 圆满当年进入第五真人修持档；只标脏，交给原生下一次真实 updateStats 重建，
		// 不在年度结算里同步强刷整套 BaseStats。
		XjRealmStageStatMultiplierService.MarkDirtyWhenStale(actor);
		EnsureTrueSpirit(actor);
		if (IsLongGeng(definition)) XjThreeBookWriter.RecordFuQiShenMiaoPerfected(actor, currentYear);
		else XjThreeBookWriter.RecordFuQiCorePerfected(actor, definition.DisplayName, currentYear);
	}

	internal static bool TryPrepareJinDanCandidate(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		out int aptitude)
	{
		aptitude = 0;
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsFuQiYangXing(actor)
			|| !XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition actual)
			|| !string.Equals(actual.DaoTuRootId, definition.DaoTuRootId, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out aptitude)) return false;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectedYear);
		if (perfectedYear <= 0) return false;
		if (!HasZhenJunEligibility(actor, currentYear, aptitude)) return false;

		NormalizeRolledBackRecoveryWindow(actor);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int injuryUntil) && injuryUntil > 0)
		{
			if (injuryUntil > currentYear) return false;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
			XjFuQiInjurySystem.RefreshStats(actor);
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int nurtureComplete) && nurtureComplete > 0)
		{
			if (nurtureComplete > currentYear) return false;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 1);
		}
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingReady, out int ready) || ready <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, out int nextAttemptYear);
		if (nextAttemptYear > currentYear) return false;
		return !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanLastAttemptYear, out int lastAttempt)
			|| lastAttempt != currentYear;
	}

	private static void NormalizeRolledBackRecoveryWindow(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanLastAttemptYear, out int lastAttemptYear)
			|| lastAttemptYear <= 0) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int storedInjuryUntil);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int storedNurtureComplete);
		if (storedInjuryUntil <= 0 && storedNurtureComplete <= 0) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
		failures = Math.Max(1, failures);
		long actorId = ((BaseSystemData)actor.data).id;
		int expectedInjuryUntil = lastAttemptYear
			+ XjFuQiBalancePolicy.ResolveSevereInjuryYears(actorId, lastAttemptYear, failures);
		int expectedNurtureComplete = expectedInjuryUntil
			+ XjFuQiBalancePolicy.ResolveJinXingNurtureYears(actorId, lastAttemptYear, failures);
		bool changed = false;

		if (storedInjuryUntil > expectedInjuryUntil)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, expectedInjuryUntil);
			changed = true;
		}
		if (storedNurtureComplete > expectedNurtureComplete)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, expectedNurtureComplete);
			changed = true;
		}

		int expectedNextAttempt = Math.Max(
			lastAttemptYear + MinimumJinDanAttemptIntervalYears,
			expectedNurtureComplete);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, out int storedNextAttempt)
			&& storedNextAttempt > expectedNextAttempt)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, expectedNextAttempt);
			changed = true;
		}

		if (changed) XjFuQiInjurySystem.RefreshStats(actor);
	}

	internal static float ResolveJinDanSuccessChance(Actor actor, int aptitude)
	{
		float baseChance = XjFuQiBalancePolicy.ResolveHighRealmSuccessChance(actor, aptitude);
		float causalBonus = XjMingShuChildSystem.ResolveBreakthroughSuccessBonus(actor);
		float schemeBonus = XjMingShuSchemeSystem.ResolveBreakthroughSuccessBonus(actor);
		return Math.Min(0.98f, baseChance + causalBonus + schemeBonus);
	}

	internal static void ResolveJinDanFailure(Actor actor, int currentYear, in XjFuQiCoreDefinition definition)
	{
		if (actor?.data == null) return;
		if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
		{
			// 真君容量、登记竞争等结构性条件暂时不满足时仅延期重试，不写失败次数、
			// 不重伤、不转世、更不能以修炼失败杀死道胎之姿。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, Math.Max(1, currentYear + 1));
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			XjFuQiInjurySystem.RefreshStats(actor);
			return;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
		failures = Math.Max(0, failures) + 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, failures);

		XjFuQiFailureProfile profile = XjFuQiBalancePolicy.ResolveFailureProfile(failures);
		float outcome = XjDeterministicHash.Roll01(
			actorId + failures, currentYear, "fuqi_zhenjun_failure_outcome_v2", definition.DaoTuRootId);
		if (outcome < profile.SevereInjuryChance)
		{
			ResolveSevereInjury(actor, actorId, currentYear, definition, failures);
			return;
		}
		if (outcome < profile.ReincarnationUpperBound)
		{
			if (EnsureTrueSpirit(actor) > 0
				&& ResolveJinXingReincarnation(actor, actorId, currentYear, definition, failures)) return;
			// 真灵耗尽或转世记录无法成立时，转世区间直接转为身死道消，
			// 不再回落到低代价重伤分支。
			ResolveTerminalZhenJunFailure(actor, actorId, currentYear, definition);
			return;
		}
		ResolveTerminalZhenJunFailure(actor, actorId, currentYear, definition);
	}

	private static void ResolveSevereInjury(
		Actor actor,
		long actorId,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		int failures)
	{
		int injuryYears = XjFuQiBalancePolicy.ResolveSevereInjuryYears(actorId, currentYear, failures);
		int nurtureYears = XjFuQiBalancePolicy.ResolveJinXingNurtureYears(actorId, currentYear, failures);
		int injuryUntil = currentYear + injuryYears;
		int nurtureComplete = injuryUntil + nurtureYears;
		int nextAttempt = Math.Max(currentYear + MinimumJinDanAttemptIntervalYears, nurtureComplete);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, injuryUntil);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, nurtureComplete);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, nextAttempt);
		XjFuQiInjurySystem.RefreshStats(actor);
		if (IsLongGeng(definition))
		{
			XjThreeBookWriter.RecordFuQiJinDanFailure(actor, currentYear, injuryYears, nurtureYears, failures);
		}
		else
		{
			XjThreeBookWriter.RecordFuQiCoreJinDanFailure(actor, definition.DisplayName, currentYear, injuryYears, nurtureYears, failures);
		}
	}

	private static bool ResolveJinXingReincarnation(
		Actor actor,
		long actorId,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		int failures)
	{
		if (!XjDeathSnapshotBuilder.TryBuild(actor, out XjDeathSnapshot snapshot)) return false;
		int trueSpirit = EnsureTrueSpirit(actor);
		if (trueSpirit <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent, out int existingBonus);
		int gainedBonus = XjFuQiBalancePolicy.ResolveReincarnationBonusGain(actorId, currentYear, failures);
		int nextBonus = Math.Clamp(
			Math.Max(0, existingBonus) + gainedBonus,
			0,
			XjFuQiBalancePolicy.MaxReincarnationBreakthroughBonusPercent);
		int remainingTrueSpirit = Math.Max(0, trueSpirit - 1);
		if (!XjReincarnation.RecordForcedFuQiJinXing(
			actor, in snapshot, remainingTrueSpirit, nextBonus, failures)) return false;

		string actorName = actor.getName();
		string message = actorName + "求证真君羽士失败，前身性命崩散，一缕金性护持真灵转入来世。";
		XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanFail);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "FuQiZhenJunReincarnation");
		bool died = XjVanillaDeathGuard.TryExecuteForceDeath(
			actor, (AttackType)5, true, XjDeathCause.BreakthroughFailure);
		if (died)
		{
			XjBroadcastSystem.ShowRecordedWorldTipCritical(
				message, false, "top", 8f, "#B477D9", iconId: XjEventIconCatalog.JinDanFail);
			return true;
		}

		XjReincarnation.CancelPending(actorId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
		ResolveSevereInjury(actor, actorId, currentYear, definition, failures);
		return true;
	}

	private static void ResolveTerminalZhenJunFailure(
		Actor actor,
		long actorId,
		int currentYear,
		in XjFuQiCoreDefinition definition)
	{
		string actorName = actor.getName();
		string message = actorName + "服气求证真君羽士，金性反噬，性命俱裂，身死道消。";
		XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanFail);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "FuQiZhenJunFailure");
		bool died = XjVanillaDeathGuard.TryExecuteForceDeath(
			actor, (AttackType)5, true, XjDeathCause.BreakthroughFailure);
		if (died)
		{
			XjBroadcastSystem.ShowRecordedWorldTipCritical(
				message, false, "top", 8f, "#B84A4A", iconId: XjEventIconCatalog.JinDanFail);
			return;
		}

		// 外部模组若阻断死亡，不保留半完成死亡态，退回重伤分支。
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
		ResolveSevereInjury(actor, actorId, currentYear, definition, Math.Max(1, failures));
	}

	internal static bool CompleteGenericZhenJun(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		bool publishNarrativeEvents = true)
	{
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string sourceDaoTu);
		if (string.IsNullOrWhiteSpace(sourceDaoTu))
		{
			if (!IsLongGeng(definition) || !XjFuQiSwordWorldState.IsEstablished) return false;
			sourceDaoTu = XjFuQiSwordWorldState.EstablishedDaoName;
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, sourceDaoTu);
		}
		sourceDaoTu = sourceDaoTu.Trim();
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFuQiGuoWeiResolver.TryResolve(
			actor,
			sourceDaoTu,
			actorId + currentYear,
			IsLongGeng(definition),
			out XjFuQiGuoWeiResolution position))
		{
			return false;
		}

		string targetDaoTu = position.ManifestDaoTu;
		string guoWei = position.GuoWei;
		string proofDaoTitle = XjHighRealmDaoStateService.ResolvePromotionDaoTitle(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiInheritedJinXing, out string inheritedJinXing);
		string jinXing = string.IsNullOrWhiteSpace(inheritedJinXing)
			? XjJinXingCalculator.Calculate(targetDaoTu, actorId)
			: inheritedJinXing.Trim();
		jinXing = XjHighRealmDaoStateService.BuildPromotionJinXing(
			actor, position.SourceDaoTu, position.ManifestDaoTu, position.GuoWeiType, jinXing);
		if (string.IsNullOrWhiteSpace(jinXing)
			|| !XjGuoWeiRegistry.TryClaim(
				actor,
				targetDaoTu,
				jinXing,
				guoWei,
				currentYear,
				position.ExternalDaoTu))
		{
			return false;
		}

		bool daoTuChanged = false;
		if (position.ChangesDaoTu)
		{
			if (!XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, targetDaoTu, false))
			{
				XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
				return false;
			}
			daoTuChanged = true;
		}

		if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.ZhenJunYuShi, false, true))
		{
			if (daoTuChanged) XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, sourceDaoTu, false);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return false;
		}
		XjJinDanAccessor.WriteSuccess(actor, jinXing, guoWei, currentYear);
		if (!XjJinDanAccessor.BuildState(actor).Found)
		{
			XjJinDanAccessor.ClearSuccess(actor);
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
			if (daoTuChanged)
			{
				XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, sourceDaoTu, true);
				XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
			}
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return false;
		}

		XjFuQiCoreDefinition promotedDefinition = definition;
		if (XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition resolvedDefinition))
		{
			promotedDefinition = resolvedDefinition;
		}
		if (!XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in promotedDefinition, currentYear))
		{
			XjJinDanAccessor.ClearSuccess(actor);
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
			if (daoTuChanged) XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, sourceDaoTu, true);
			XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
			XjGuoWeiRegistry.ReleaseForActor(actorId, guoWei);
			return false;
		}
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.ZhenJunYuShi, currentYear);
		XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.ZhenJunYuShi, targetDaoTu);
		// 服气判位仍由道途牵连决定；这里只先固化根道/显道，
		// 再让权柄快照读取真实闰法，不接入五神通判位。
		XjHighRealmDaoStateService.InitializeOnPromotion(
			actor, position.SourceDaoTu, position.ManifestDaoTu, position.GuoWeiType,
			guoWei, jinXing, currentYear, true, proofDaoTitle);
		XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, targetDaoTu, guoWei, currentYear);
		FinalizeFuQiHighRealm(actor, currentYear);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		if (publishNarrativeEvents)
		{
			XjThreeBookWriter.RecordFuQiCoreZhenJunYuShi(actor, promotedDefinition.DisplayName, currentYear);
		}
		XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZhenJunYuShi, "FuQiZhenJunPromotion");
		if (publishNarrativeEvents)
		{
			XjActorCultivationSnapshot successSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			XjJinDanBreakthroughSystem.RunJinDanSuccessEventChain(
				actor,
				targetDaoTu,
				jinXing,
				guoWei,
				currentYear,
				in successSnapshot,
				publishPromotionAnnouncement: true,
				eraChangeCauseOverride: XjAnnouncementText.BuildFuQiZhenJunEraChangeCause(
					actor, targetDaoTu, jinXing, guoWei),
				promotionTextOverride: XjAnnouncementText.BuildFuQiZhenJunPromotion(
					actor, targetDaoTu, promotedDefinition.DisplayName, jinXing, guoWei));
		}
		return true;
	}

	/// <summary>
	/// Player/editor manual ZhenJun reconciliation. This is deliberately stricter
	/// than natural breakthrough resolution: it may claim a legal fruit/derived
	/// position, but it must never fall through to ShenDan, injury, reincarnation
	/// or death. If no legal position is available the edit simply stays at
	/// ZhenRen and can be retried after a position becomes available.
	/// </summary>
	internal static bool TryCompleteManualZhenJunStrict(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		bool publishNarrativeEvents = false)
	{
		if (actor?.data == null || currentYear <= 0) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			|| !XjFuQiAptitudeRules.CanAttemptZhenJunYuShi(actor)) return false;
		if (IsLongGeng(definition) && !XjFuQiSwordWorldState.HasCurrentHolder)
		{
			return XjFuQiLongGengPositionHandler.TryCompleteManualZhenJun(actor, currentYear);
		}
		if (IsLongGeng(definition))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, XjFuQiSwordWorldState.EstablishedDaoName);
		}
		return CompleteGenericZhenJun(actor, currentYear, definition, publishNarrativeEvents);
	}

	internal static bool TryCompleteManualHighRealm(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		bool publishNarrativeEvents = true)
	{
		if (actor?.data == null || currentYear <= 0) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			|| !XjFuQiAptitudeRules.CanAttemptZhenJunYuShi(actor)) return false;
		if (IsLongGeng(definition) && !XjFuQiSwordWorldState.HasCurrentHolder)
		{
			return XjFuQiLongGengPositionHandler.TryCompleteManualZhenJun(actor, currentYear);
		}
		if (IsLongGeng(definition))
		{
			// 长庚已命名且果位有主时，后继修士只竞争余位；长庚不生闰位。
			XjActorAccessor.SetString(
				actor,
				XjActorDataKeys.DaoTu,
				XjFuQiSwordWorldState.EstablishedDaoName);
		}
		return CompleteGenericZhenJun(actor, currentYear, definition, publishNarrativeEvents)
			|| TryCompleteSharedShenDan(actor, currentYear, definition, publishNarrativeEvents);
	}

	private static bool TryCompleteSharedShenDan(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition,
		bool publishNarrativeEvents = true)
	{
		if (actor?.data == null
			|| XjXianGuoSystem.IsDiMingYang(actor)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string[] types =
		{
			XjGuoWeiCalculator.ZhengWei,
			XjGuoWeiCalculator.RunWei,
			XjGuoWeiCalculator.YuWei
		};
		XjGuoWeiRegistryEntry anchor = default;
		bool found = false;
		for (int i = 0; i < types.Length; i++)
		{
			if (IsLongGeng(definition)
				&& string.Equals(types[i], XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) continue;
			if (XjGuoWeiRegistry.TryFindActiveAnchor(
				daoTu, types[i],
				candidate => XjShenDanRegistry.CanAttachToAnchor(candidate, actorId, out _, out _),
				out anchor))
			{
				found = true;
				break;
			}
		}
		if (!found || anchor.ActorId <= 0L || anchor.ActorId == actorId) return false;
		if (!XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.ShenDan, false, true)) return false;

		string anchorName = string.IsNullOrWhiteSpace(anchor.ActorName) ? "无名金丹" : anchor.ActorName.Trim();
		XjShenDanAccessor.WriteSuccess(actor, anchor.GuoWei, anchor.ActorId, anchorName, currentYear);
		if (!XjShenDanAccessor.BuildState(actor).Found)
		{
			XjShenDanAccessor.ClearSuccess(actor);
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
			return false;
		}
		if (!XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear))
		{
			XjShenDanAccessor.ClearSuccess(actor);
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
			XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
			return false;
		}
		if (!XjShenDanRegistry.TryRegister(actorId, anchor.ActorId))
		{
			XjShenDanAccessor.ClearSuccess(actor);
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, true, true);
			XjFuQiMethodRules.EnsureRealGongFaEntity(actor, in definition, currentYear);
			return false;
		}
		FinalizeFuQiHighRealm(actor, currentYear);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.ShenDan, currentYear);
		XjRealmTitleApplyService.ApplyOnPromotion(actor, XjRealmIds.ShenDan, daoTu);
		if (publishNarrativeEvents)
		{
			XjThreeBookWriter.RecordFuQiCoreZhenJunYuShi(actor, definition.DisplayName + "（神丹）", currentYear);
			XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				XjAnnouncementText.BuildFuQiShenDanPromotion(
					actor, daoTu, definition.DisplayName, anchorName, anchor.GuoWei),
				iconId: XjEventIconCatalog.JinDanUpgrade,
				category: XjAnnouncementCategory.HighRealm);
		}
		XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ShenDan, "FuQiShenDanPromotion");
		return true;
	}

	private static void FinalizeFuQiHighRealm(Actor actor, int currentYear)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinDanNextAttemptYear, 0);
		XjJinDanImmortalityRegistry.EnsureActivated(actor, currentYear);
	}

	private static bool HasZhenJunEligibility(Actor actor, int currentYear, int aptitude)
	{
		return aptitude >= 4 && aptitude <= 6
			&& XjFuQiAptitudeRules.CanAttemptZhenJunYuShi(actor);
	}

	private static void AppendZhenRenStatus(Actor actor, StringBuilder builder, int currentYear, in XjFuQiCoreDefinition definition)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectionYear);
		if (perfectionYear <= 0)
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, out int completeYear) && completeYear > 0)
			{
				builder.Append("圆满：温养本命神妙");
				AppendRemainingYears(builder, currentYear, completeYear);
				builder.AppendLine();
			}
			return;
		}
		builder.AppendLine("神妙：圆满，性命自然化出金性");
		if (IsLongGeng(definition) && XjFuQiSwordWorldState.HasCurrentHolder)
		{
			builder.AppendLine("求位：长庚果位已有持位者。");
			return;
		}
		if (IsLongGeng(definition))
		{
			builder.AppendLine(XjFuQiSwordWorldState.IsEstablished
				? "求位：长庚果位空悬，可求证继任。"
				: "求位：可向天地求证无名剑道果位。");
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		if (aptitude < 4 || aptitude > 6)
		{
			builder.AppendLine("求证：当前根基不足以承接服气养性高境。");
			return;
		}
		if (aptitude == 4 && !XjFuQiAptitudeRules.CanAttemptZhenJunYuShi(actor))
		{
			builder.AppendLine("求证：四档根基虽已感气成道，但命数与道慧不足以再承真君羽士之关。");
			return;
		}
		if (aptitude == 5
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, out int routeChecked)
			&& routeChecked > 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, out int stayFuQi);
			if (stayFuQi <= 0)
			{
				builder.AppendLine("命途：五品法门已定转入紫府金丹道。");
				return;
			}
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiInjuryUntilYear, out int injuryUntil) && injuryUntil > currentYear)
		{
			builder.Append("状态：性命反震，闭关养伤");
			AppendRemainingYears(builder, currentYear, injuryUntil);
			builder.AppendLine();
			return;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int nurtureComplete) && nurtureComplete > currentYear)
		{
			builder.Append("金性：重新温养中");
			AppendRemainingYears(builder, currentYear, nurtureComplete);
			builder.AppendLine();
			return;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanFailureCount, out int failures);
		if (failures > 0)
		{
			XjFuQiFailureProfile nextFailure = XjFuQiBalancePolicy.ResolveFailureProfile(failures + 1);
			builder.Append("求证代价：已历").Append(failures).Append("次未成，再败身死之险")
				.AppendLine(DescribeDisplayRisk(nextFailure.TerminalDeathChance));
		}
		builder.AppendLine("金性：已成，待求证真君羽士");
	}

	private static string DescribeDisplayAid(int value)
	{
		if (value <= 0) return "无";
		if (value <= 3) return "微薄";
		if (value <= 6) return "渐显";
		if (value <= 10) return "显著";
		return "深厚";
	}

	private static string DescribeDisplayRisk(float value)
	{
		if (value <= 0.05f) return "甚微";
		if (value <= 0.20f) return "渐起";
		if (value <= 0.40f) return "不低";
		if (value <= 0.65f) return "甚重";
		return "近乎必死";
	}

	private static int ReadTrueSpiritForDisplay(Actor actor)
	{
		if (actor?.data == null) return 0;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiTrueSpiritInitialized, out int initialized)
			&& initialized > 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiTrueSpirit, out int stored);
			return Math.Clamp(stored, 0, 3);
		}

		// 黄冠阶段只显示按当前道慧推算的真灵值，不提前写死。真正到真人
		// 圆满、开始求证时才固化，允许数百年修行所得道慧影响最终上限。
		return ResolveTrueSpiritFromHuiGuang(actor);
	}

	private static int EnsureTrueSpirit(Actor actor)
	{
		if (actor?.data == null) return 0;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiTrueSpiritInitialized, out int initialized)
			&& initialized > 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiTrueSpirit, out int stored);
			return Math.Clamp(stored, 0, 3);
		}

		int trueSpirit = ResolveTrueSpiritFromHuiGuang(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiTrueSpiritInitialized, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiTrueSpirit, trueSpirit);
		return trueSpirit;
	}

	private static int ResolveTrueSpiritFromHuiGuang(Actor actor)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		// 服气只允许五、六品：道慧不足80为1点，80—89为2点，90以上为3点。
		// 五品多落在1—2，六品顶段与稀有后天悟道者才稳定达到3。
		float daoHui = XjDaoHuiPolicy.Clamp(huiGuang);
		return daoHui >= XjDaoHuiPolicy.FuQiMaxTrueSpiritThreshold ? 3
			: daoHui >= XjDaoHuiPolicy.StableInheritanceThreshold ? 2 : 1;
	}

	private static int ResolveBodyCultivationYears(Actor actor, int aptitude, string rootId)
	{
		return ResolveLongProjectYears(actor, aptitude, "fuqi_body_years|" + rootId, 220, 275, 180, 225, 150, 185);
	}

	private static int ResolvePerfectionYears(Actor actor, int aptitude, string rootId)
	{
		return ResolveLongProjectYears(actor, aptitude, "fuqi_perfection_years|" + rootId, 370, 460, 300, 380, 240, 300);
	}

	private static int ResolveLongProjectYears(Actor actor, int aptitude, string salt,
		int min4, int max4, int min5, int max5, int min6, int max6)
	{
		int min;
		int max;
		if (aptitude >= 6) { min = min6; max = max6; }
		else if (aptitude == 5) { min = min5; max = max5; }
		else { min = min4; max = max4; }
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		float quality = XjDaoHuiPolicy.Normalize01(huiGuang);
		int target = max - (int)Math.Round((max - min) * quality);
		long actorId = ((BaseSystemData)actor.data).id;
		int jitter = XjDeterministicHash.PositiveIndex(actorId + aptitude, salt, 7) - 3;
		return Math.Clamp(target + jitter, min, max);
	}

	private static void AppendRemainingYears(StringBuilder builder, int currentYear, int completeYear)
	{
		int remaining = Math.Max(0, completeYear - currentYear);
		if (remaining > 0) builder.Append("，尚需").Append(remaining).Append('年');
	}

	private static bool IsLongGeng(in XjFuQiCoreDefinition definition)
	{
		return string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal);
	}
}
