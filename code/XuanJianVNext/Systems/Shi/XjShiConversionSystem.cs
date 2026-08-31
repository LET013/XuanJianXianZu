using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 紫府金丹、服气高修自然投释的等位转修事务。自然投释完整校验师承、命数、
/// 承载地与果位钟爱锁；特质编辑器只允许补录到法相以下，若源境界折算结果已到
/// 法相则直接拒绝。所有真实入口统一清理旧功法、仙基和高境权威；真君原果余闰位
/// 先捕获后系入释土，不再作为普通空位释放，禁止只换图标不换后台。
/// </summary>
internal static class XjShiConversionSystem
{
	private const float ZhuJiMingShuThreshold = 100f;
	private const float ZiFuMingShuThreshold = 300f;
	private const float JinDanMingShuThreshold = 600f;
	// 自然投释不是另一条“随机跳槽”入口。RC11.10起先统一经过修法重择策略：
	// 本路前景尚好时不考虑今释；只有修途受阻或暮年求长生才进入极低频抽签。
	// 单次概率和考虑冷却统一由 XjCultivationPathSwitchPolicy 负责。

	internal static bool CanEnterThroughTeacher(Actor student, Actor teacher, int annualYear)
	{
		if (XjXianGuoSystem.IsDiMingYang(student)) return false;
		return TryBuildTeacherPlan(student, teacher, annualYear, out ConversionPlan plan)
			&& PassNaturalConversionRoll(student, teacher, annualYear, plan.SourceTier);
	}

	/// <summary>
	/// 供今释年度接引器做“候选选择”使用，只判断结构资格，不提前掷自然投释概率。
	/// 这样一名法相每年最多真正尝试一个高境目标，不会因为遍历候选而把低概率放大。
	/// </summary>
	internal static bool CanBuildTeacherConversionPlan(Actor student, Actor teacher, int annualYear, out int sourceTier)
	{
		sourceTier = 0;
		if (XjXianGuoSystem.IsDiMingYang(student)) return false;
		if (!TryBuildTeacherPlan(student, teacher, annualYear, out ConversionPlan plan)) return false;
		sourceTier = plan.SourceTier;
		return true;
	}


	internal static bool TryConvertThroughTeacher(Actor student, Actor teacher, int annualYear, string entrySource)
	{
		if (XjXianGuoSystem.IsDiMingYang(student)) return false;
		if (!TryBuildTeacherPlan(student, teacher, annualYear, out ConversionPlan plan)) return false;
		// 所有已修行角色的自然换修都共享低频考虑闸。一个角色不会因为同城有多个法相
		// 就在同一年或短周期内被重复抽签；金丹/真君的冷却最长。
		if (!TryReserveNaturalConversionAttempt(student, annualYear, plan.SourceTier)) return false;
		if (!PassNaturalConversionRoll(student, teacher, annualYear, plan.SourceTier)) return false;
		long teacherId = ((BaseSystemData)teacher.data).id;
		XjActorAccessor.TryGetString(teacher, XjActorDataKeys.ShiLineageId, out string lineageId);
		return CommitConversion(student, plan, Math.Max(1, annualYear), teacherId, lineageId, string.Empty);
	}

	/// <summary>
	/// 特质编辑器赋予古释／今释时的体系补录。手动干预仍遵守果位钟爱锁；
	/// 若旧境界等位折算将直接进入法相或更高层次，则拒绝补录，必须真实投释/证道。
	/// </summary>
	internal static bool TryConvertManual(Actor actor, string tradition, int annualYear)
	{
		if (XjXianGuoSystem.IsDiMingYang(actor)) return false;
		if (!TryBuildManualPlan(actor, tradition, ignoreFavoredDaoTuLock: false, out ConversionPlan plan)) return false;
		// 手动改挂今/古释也不能把紫府、金丹或真君直接折算成法相。
		// 摩诃仍属于紫府同级补录边界；法相及以上只能走真实高境证道事务。
		if (!XjManualHighRealmGrantPolicy.IsManualRealmRecordAllowed(plan.TargetRealm)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string lineageId = XjShiLineageIds.ResolveDefault(tradition, actorId);
		return CommitConversion(actor, plan, Math.Max(1, annualYear), 0L,
			lineageId, string.Empty);
	}

	/// <summary>
	/// 角色肉身真正踏入旃檀林后的强制摄化。此入口只绕过“果位钟爱不得自然投释”，
	/// 其余境界折算、果位捕获与旧道清理仍走同一转换事务，避免金丹被边界直接抹除。
	/// </summary>
	internal static bool TryConvertByZhantanlin(Actor actor, int annualYear)
	{
		if (XjXianGuoSystem.IsDiMingYang(actor)) return false;
		if (!TryBuildManualPlan(actor, XjShiTraditionIds.Modern, ignoreFavoredDaoTuLock: true,
			out ConversionPlan plan)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string lineageId = XjShiLineageIds.ResolveDefault(XjShiTraditionIds.Modern, actorId);
		return CommitConversion(actor, plan, Math.Max(1, annualYear), 0L, lineageId, string.Empty,
			ignoreFavoredDaoTuLock: true);
	}

	private static bool CommitConversion(Actor actor, in ConversionPlan plan, int annualYear,
		long teacherId, string lineageId, string lawIds, bool ignoreFavoredDaoTuLock = false)
	{
		// 帝明阳既已以明阳正统求帝，生前不再舍国改投他玄；即使是旃檀林
		// 强制摄化或手动补录也不得绕过这一层最终事务门。
		if (XjXianGuoSystem.IsDiMingYang(actor)) return false;
		int year = Math.Max(1, annualYear);
		// 真君投释会清空金丹载荷与现世果位登记，必须在事务开始前捕获原果余闰位。
		XjShiFruitPositionConversionCapture fruitPositionCapture =
			XjShiFruitPositionLockSystem.CaptureBeforeConversion(actor);
		if (!XjShiState.TryEnter(actor, plan.Tradition, year, XjShiSourceIds.Conversion,
			teacherId, lineageId, lawIds, manualOverride: true,
			ignoreFavoredDaoTuLock: ignoreFavoredDaoTuLock)) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionSourceTier, plan.SourceTier);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConversionYear, year);
		ApplyMingShuFloor(actor, plan.MingShuFloor);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPractice, plan.PracticeFloor);
		SetLifeProjection(actor, plan.CurrentLife);

		bool completed;
		if (string.Equals(plan.TargetRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal))
		{
			completed = XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaMaster, year, manualOverride: true);
		}
		else if (string.Equals(plan.TargetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			completed = PromoteToMoHe(actor, plan, year);
		}
		else
		{
			completed = PromoteToDharmaForm(actor, plan, year);
		}

		if (!completed)
		{
			// TryEnter已经完成道途切换；即便后续承载位写入失败，也不能让原果位泄回现世。
			XjShiFruitPositionLockSystem.CommitConversionBinding(actor, fruitPositionCapture, year);
			return false;
		}
		XjShiFruitPositionLockSystem.CommitConversionBinding(actor, fruitPositionCapture, year);
		if (string.Equals(plan.TargetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			&& XjShiDharmaFormStageIds.IsKnown(plan.DharmaFormStage)
			&& !string.Equals(plan.DharmaFormStage, XjShiDharmaFormStageIds.None, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaFormStage, plan.DharmaFormStage);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDharmaFormStageEnteredYear, year);
			ApplyConvertedDharmaFormStageFloor(actor, plan.DharmaFormStage, year);
			XjRealmSuppression.SyncCombatLevel(actor);
			XjCombatHotPathCache.Refresh(actor);
		}
		XjShiTitleSystem.EnsureForActor(actor);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"舍旧途投释，由" + plan.SourceDisplay + "等位转为"
			+ XjShiCatalog.GetRealmDisplay(plan.TargetRealm) + "，承"
			+ XjShiCatalog.GetLineageDisplay(lineageId) + "法脉。",
			XjShiCatalog.GetRealmTraitId(plan.TargetRealm));
		XjThreeBookWriter.RecordShiConversion(actor, year, plan.SourceDisplay,
			plan.TargetRealm, lineageId);
		return true;
	}

	private static bool PromoteToMoHe(Actor actor, in ConversionPlan plan, int annualYear)
	{
		// 摩诃是今释专属高位；古释没有怜愍、摩诃中间层。
		if (!string.Equals(plan.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		string domainId = XjShiDomainState.EnsureZhantanlin(annualYear).DomainId;
		if (!XjShiDomainState.TryClaimMoHePosition(domainId, actorId, annualYear)) return false;
		if (!XjShiState.TrySetRealm(actor, XjShiRealmIds.MoHe, annualYear, manualOverride: true)) return false;
		XjShiExpansionSystem.OnRealmOrPositionAttained(actor, annualYear, XjShiRealmIds.MoHe,
			string.Empty, selfProvedJinDi: false, becameLiangLi: false);
		return true;
	}

	private static bool PromoteToDharmaForm(Actor actor, in ConversionPlan plan, int annualYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		string domainId = string.Empty;
		bool ancient = string.Equals(plan.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		if (ancient)
		{
			// 古释高境折算直接落到法相，不借用今释摩诃位与九世轮回。先补到法师
			// 只是满足古释“由法师自证法相”的合法承位入口，不生成额外中间纪事。
			if (!XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot ancientSnapshot)) return false;
			if (XjShiCatalog.GetRank(ancientSnapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)
				&& !XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaMaster, annualYear,
					manualOverride: true, updateWorldRegistry: false, emitNarrative: false)) return false;
			XjShiDomainState.EnsureAncientSelfProvedJinDi(actor, annualYear);
			if (!XjShiDomainState.TryGetDharmaFormFoundation(actor, annualYear, out XjShiDomainRecord ancientDomain)
				|| ancientDomain == null) return false;
			domainId = ancientDomain.DomainId;
		}
		else
		{
			XjShiDomainRecord zhantanlin = XjShiDomainState.EnsureZhantanlin(annualYear);
			domainId = zhantanlin.DomainId;
			if (!XjShiDomainState.TryClaimMoHePosition(domainId, actorId, annualYear)) return false;
			if (!XjShiState.TrySetRealm(actor, XjShiRealmIds.MoHe, annualYear, manualOverride: true)) return false;
			XjShiDomainState.TryGet(domainId, out XjShiDomainRecord sourceDomain);
			XjShiDomainRecord claimDomain = XjShiDomainState.EnsureModernDharmaFormDomain(
				actor, sourceDomain, annualYear);
			if (claimDomain == null) return false;
			domainId = claimDomain.DomainId;
		}
		if (string.IsNullOrWhiteSpace(domainId)) return false;
		if (!XjShiDomainState.TryClaimDharmaFormPosition(domainId, actorId, annualYear)) return false;
		if (!XjShiState.TrySetRealm(actor, XjShiRealmIds.DharmaForm, annualYear, manualOverride: true)) return false;
		XjShiHighRealmSystem.EnsureActor(actor, annualYear);
		return true;
	}

	private static bool TryBuildTeacherPlan(Actor student, Actor teacher, int annualYear, out ConversionPlan plan)
	{
		plan = default;
		if (student?.data == null || teacher?.data == null || ReferenceEquals(student, teacher)
			|| !student.isAlive() || !teacher.isAlive()
			|| XjCultivationPathRules.IsShi(student)
			|| XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(student, out _)
			|| !XjCultivationEligibility.CanCultivate(student)
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot teacherSnapshot)) return false;

		int sourceTier = XjRealmSuppression.GetRealmTier(student);
		int teacherRank = XjShiCatalog.GetRank(teacherSnapshot.Realm);
		float mingShu = ReadOrdinaryMingShu(student);
		if (!TryBuildTierPlan(student, sourceTier, teacherSnapshot.Tradition, mingShu,
			out plan)) return false;

		if (sourceTier == XjRealmSuppression.TierZhuJi)
		{
			bool highTarget = string.Equals(plan.TargetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
				|| string.Equals(plan.TargetRealm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal);
			int requiredTeacherRank = highTarget
				? XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
				: XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster);
			if (teacherRank < requiredTeacherRank || mingShu < ZhuJiMingShuThreshold) return false;
			if (string.Equals(plan.TargetRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
				&& string.Equals(teacherSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
			{
				if (!XjShiDomainState.TryGetForActor(teacher, Math.Max(1, annualYear), out XjShiDomainRecord teacherDomain)
					|| !XjShiDomainState.IsDomainAvailableForMoHeClaim(teacherDomain.DomainId,
						((BaseSystemData)student.data).id)) return false;
				plan = plan.WithDomain(teacherDomain.DomainId);
			}
		}
		else if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			if (teacherRank < XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
				|| mingShu < ZiFuMingShuThreshold) return false;
			if (string.Equals(teacherSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
			{
				if (!XjShiDomainState.TryGetForActor(teacher, Math.Max(1, annualYear), out XjShiDomainRecord teacherDomain)
					|| !XjShiDomainState.IsDomainAvailableForMoHeClaim(teacherDomain.DomainId,
						((BaseSystemData)student.data).id)) return false;
				plan = plan.WithDomain(teacherDomain.DomainId);
			}
		}
		else if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			if (teacherRank < XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
				|| mingShu < JinDanMingShuThreshold
				|| !IsTeacherSufficientForJinDanConversion(teacherSnapshot, plan.DharmaFormStage)) return false;
		}
		else return false;

		return HasConvertiblePath(student)
			&& XjCultivationPathSwitchPolicy.CanConsiderNaturalShiConversion(student, sourceTier, annualYear);
	}

	private static bool IsTeacherSufficientForJinDanConversion(in XjShiSnapshot teacherSnapshot, string targetStage)
	{
		if (string.Equals(teacherSnapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return true;
		if (!string.Equals(teacherSnapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return false;
		return ResolveDharmaFormStageIndex(teacherSnapshot.DharmaFormStage)
			>= ResolveDharmaFormStageIndex(targetStage);
	}

	private static int ResolveDharmaFormStageIndex(string stage)
	{
		if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)) return 1;
		if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)) return 2;
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)) return 3;
		return 0;
	}

	private static bool PassNaturalConversionRoll(Actor student, Actor teacher, int annualYear, int sourceTier)
	{
		if (student?.data == null || teacher?.data == null) return false;
		float mingShu = ReadOrdinaryMingShu(student);
		int basisPoints = XjCultivationPathSwitchPolicy.ResolveNaturalShiConversionBasisPoints(
			student, sourceTier, mingShu);
		if (basisPoints <= 0) return false;

		long studentId = ((BaseSystemData)student.data).id;
		long teacherId = ((BaseSystemData)teacher.data).id;
		int year = Math.Max(1, annualYear);
		return XjDeterministicHash.PositiveIndex(studentId + teacherId + year,
			"shi_natural_path_switch_v3", 10000) < basisPoints;
	}

	private static bool TryReserveNaturalConversionAttempt(Actor actor, int annualYear, int sourceTier)
	{
		if (actor?.data == null) return false;
		int year = Math.Max(1, annualYear);
		int cooldown = XjCultivationPathSwitchPolicy.ResolveNaturalShiConversionCooldownYears(sourceTier);
		if (cooldown == int.MaxValue) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiHighRealmConversionLastAttemptYear, out int lastYear)
			&& lastYear > 0 && year - lastYear < cooldown) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiHighRealmConversionLastAttemptYear, year);
		return true;
	}

	private static bool TryBuildManualPlan(Actor actor, string tradition, bool ignoreFavoredDaoTuLock,
		out ConversionPlan plan)
	{
		plan = default;
		if (actor?.data == null || !actor.isAlive() || !XjShiCatalog.IsKnownTradition(tradition)
			|| XjCultivationPathRules.IsShi(actor)
			|| !ignoreFavoredDaoTuLock && XjGuoWeiFavoredDaoTuLock.TryResolveLockedDaoTu(actor, out _)
			|| !XjCultivationEligibility.CanCultivate(actor)) return false;
		int sourceTier = XjRealmSuppression.GetRealmTier(actor);
		if (sourceTier <= XjRealmSuppression.TierLianQi || !HasConvertiblePath(actor)) return false;
		return TryBuildTierPlan(actor, sourceTier, tradition, ReadOrdinaryMingShu(actor), out plan);
	}

	private static bool TryBuildTierPlan(Actor actor, int sourceTier, string tradition,
		float mingShu, out ConversionPlan plan)
	{
		plan = default;
		float sourceProgress = ResolveSourceProgress(actor, sourceTier);
		if (sourceTier == XjRealmSuppression.TierZhuJi)
		{
			// 同样的高根基投释，在今释折算为摩诃；在古释则直接按“法师→法相”折算，
			// 不制造古释怜愍或古释摩诃。
			if (mingShu >= XjShiCatalog.ManualMoHeMingShuFloor && sourceProgress >= 0.85f)
			{
				if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
				{
					float practice = XjShiCatalog.AncientDharmaFormPracticeThreshold + sourceProgress * 3000f;
					plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.DharmaForm,
						XjShiCatalog.DharmaFormManualRecordMinimumMingShu, practice, 1,
						"筑基／黄冠（高命数圆满根基）", string.Empty, XjShiDharmaFormStageIds.OriginalVow);
				}
				else
				{
					int currentLife = mingShu >= 600f ? 2 : 1;
					float practice = XjShiCatalog.MoHePracticeThreshold + sourceProgress * 6000f;
					plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.MoHe,
						XjShiCatalog.ManualMoHeMingShuFloor, practice, currentLife,
						"筑基／黄冠（高命数圆满根基）", string.Empty, XjShiDharmaFormStageIds.None);
				}
				return true;
			}
			float convertedPractice = XjShiCatalog.DharmaMasterPracticeThreshold
				+ sourceProgress * Math.Max(0f,
					XjShiCatalog.LianMinPracticeThreshold - XjShiCatalog.DharmaMasterPracticeThreshold - 1f);
			plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.DharmaMaster,
				XjShiCatalog.ManualDharmaMasterMingShuFloor,
				convertedPractice, 1, "筑基／黄冠", string.Empty, XjShiDharmaFormStageIds.None);
			return true;
		}
		if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				float practiceFloor = XjShiCatalog.AncientDharmaFormPracticeThreshold
					+ sourceProgress * Math.Max(1f, XjShiCatalog.ResponseBodyPracticeThreshold - XjShiCatalog.AncientDharmaFormPracticeThreshold);
				plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.DharmaForm,
					XjShiCatalog.DharmaFormManualRecordMinimumMingShu, practiceFloor,
					1, "紫府／真人", string.Empty, XjShiDharmaFormStageIds.OriginalVow);
				return true;
			}
			int shenTongCount = Math.Max(1, XjXianJiAccessor.GetEffectiveShenTongCount(actor));
			int currentLife = ResolveMoHeLife(shenTongCount, mingShu);
			float practiceFloorModern = XjShiCatalog.MoHePracticeThreshold
				+ Math.Max(0, currentLife - 1) * 6000f + sourceProgress * 5999f;
			plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.MoHe,
				XjShiCatalog.ManualMoHeMingShuFloor, practiceFloorModern,
				currentLife, "紫府／真人", string.Empty, XjShiDharmaFormStageIds.None);
			return true;
		}
		if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			int jinDanStage = ResolveJinDanStage(actor);
			string targetStage = jinDanStage switch
			{
				1 => XjShiDharmaFormStageIds.ResponseBody,
				2 => XjShiDharmaFormStageIds.SelfReturned,
				3 => XjShiDharmaFormStageIds.WorldHonoredPath,
				_ => XjShiDharmaFormStageIds.OriginalVow
			};
			float lower = jinDanStage switch
			{
				1 => XjShiCatalog.ResponseBodyPracticeThreshold,
				2 => XjShiCatalog.SelfReturnedPracticeThreshold,
				3 => XjShiCatalog.WorldHonoredPathPracticeThreshold,
				_ => string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
					? XjShiCatalog.AncientDharmaFormPracticeThreshold : XjShiCatalog.DharmaFormPracticeThreshold
			};
			float upper = jinDanStage switch
			{
				1 => XjShiCatalog.SelfReturnedPracticeThreshold,
				2 => XjShiCatalog.WorldHonoredPathPracticeThreshold,
				3 => XjShiCatalog.WorldHonoredPracticeThreshold,
				_ => XjShiCatalog.ResponseBodyPracticeThreshold
			};
			float practiceFloor = lower + Math.Max(0f, upper - lower) * sourceProgress;
			plan = new ConversionPlan(sourceTier, tradition, XjShiRealmIds.DharmaForm,
				XjShiCatalog.DharmaFormManualRecordMinimumMingShu,
				practiceFloor, string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal) ? 1 : 9,
				"金丹／真君羽士", string.Empty, targetStage);
			return true;
		}
		return false;
	}

	private static float ResolveSourceProgress(Actor actor, int sourceTier)
	{
		if (actor?.data == null) return 0f;
		if (XjCultivationPathRules.TryGetPath(actor, out string path)
			&& string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)
			&& sourceTier <= XjRealmSuppression.TierZiFu)
		{
			int basis = XjFuQiToZiFuTransitionSystem.ResolveCoreProgressBasisPoints(
				actor, Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor)));
			return Math.Clamp(basis / 10000f, 0f, 1f);
		}
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		if (sourceTier == XjRealmSuppression.TierZhuJi)
			return Math.Clamp((zhenYuan - 6000f) / 30000f, 0f, 1f);
		if (sourceTier == XjRealmSuppression.TierZiFu)
		{
			int count = Math.Max(1, XjXianJiAccessor.GetEffectiveShenTongCount(actor));
			float countProgress = Math.Clamp((count - 1) / 4f, 0f, 1f);
			float yuanProgress = Math.Clamp((zhenYuan - 36000f) / 58000f, 0f, 1f);
			return Math.Clamp(countProgress * 0.8f + yuanProgress * 0.2f, 0f, 1f);
		}
		if (sourceTier == XjRealmSuppression.TierJinDan)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
			int stage = ResolveJinDanStage(actor);
			int lower = stage switch { 1 => 1000, 2 => 3000, 3 => 6000, _ => 0 };
			int upper = stage switch { 1 => 3000, 2 => 6000, 3 => 10000, _ => 1000 };
			return Math.Clamp((yiXiang - lower) / (float)Math.Max(1, upper - lower), 0f, 1f);
		}
		return 0f;
	}

	private static int ResolveJinDanStage(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
		if (yiXiang >= 6000) return 3;
		if (yiXiang >= 3000) return 2;
		if (yiXiang >= 1000) return 1;
		return 0;
	}

	/// <summary>
	/// 原著对标：紫府一至四神通分别对应一／二世、三／四世、五／六世、七世摩诃；
	/// 五法俱全继续落在八／九世。每档取高低位由命数决定，不再一律取上限。
	/// </summary>
	private static int ResolveMoHeLife(int shenTongCount, float mingShu)
	{
		bool upper = mingShu >= 400f;
		if (shenTongCount <= 1) return upper ? 2 : 1;
		if (shenTongCount == 2) return upper ? 4 : 3;
		if (shenTongCount == 3) return upper ? 6 : 5;
		if (shenTongCount == 4) return 7;
		return mingShu >= 650f ? 9 : 8;
	}

	private static bool HasConvertiblePath(Actor actor)
	{
		if (!XjCultivationPathRules.TryGetPath(actor, out string path)) return true;
		return string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal)
			|| string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal);
	}

	private static void SetLifeProjection(Actor actor, int currentLife)
	{
		int life = Math.Clamp(currentLife, 1, 9);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCurrentLife, life);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiCompletedLives, Math.Max(0, life - 1));
	}

	private static void ApplyConvertedDharmaFormStageFloor(Actor actor, string stage, int annualYear)
	{
		float mingShuFloor = string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)
			? XjShiCatalog.WorldHonoredPathMingShuThreshold
			: string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)
				? XjShiCatalog.SelfReturnedMingShuThreshold
				: string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)
					? XjShiCatalog.ResponseBodyMingShuThreshold
					: XjShiCatalog.DharmaFormMinimumMingShu;
		ApplyMingShuFloor(actor, mingShuFloor);
		int growthFloor = XjShiHighRealmSystem.ResolveFoundationGrowthFloor(XjShiRealmIds.DharmaForm, stage);
		if (growthFloor > 0
			&& XjShiDomainState.TryGetDharmaFormFoundation(actor, annualYear, out XjShiDomainRecord domain)
			&& domain != null && domain.Growth < growthFloor)
		{
			XjShiDomainState.AddHighRealmGrowth(domain.DomainId, growthFloor - domain.Growth, annualYear);
		}
	}

	private static void ApplyMingShuFloor(Actor actor, float floor)
	{
		XjMingShuState.Normalize(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float ordinary);
		if (ordinary < floor) XjMingShuState.AddAcquired(actor, floor - ordinary);
		XjShiMingShuSystem.InitializeFromOrdinaryFate(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float shi);
		if (shi < floor) XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, floor);
	}

	private static float ReadOrdinaryMingShu(Actor actor)
	{
		if (actor?.data == null) return 0f;
		XjMingShuState.Normalize(actor);
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float value)
			? Math.Max(0f, value) : 0f;
	}

	private readonly struct ConversionPlan
	{
		internal readonly int SourceTier;
		internal readonly string Tradition;
		internal readonly string TargetRealm;
		internal readonly float MingShuFloor;
		internal readonly float PracticeFloor;
		internal readonly int CurrentLife;
		internal readonly string SourceDisplay;
		internal readonly string DomainId;
		internal readonly string DharmaFormStage;

		internal ConversionPlan(int sourceTier, string tradition, string targetRealm,
			float mingShuFloor, float practiceFloor, int currentLife, string sourceDisplay, string domainId,
			string dharmaFormStage)
		{
			SourceTier = sourceTier;
			Tradition = tradition ?? string.Empty;
			TargetRealm = targetRealm ?? string.Empty;
			MingShuFloor = Math.Max(0f, mingShuFloor);
			PracticeFloor = Math.Max(0f, practiceFloor);
			CurrentLife = Math.Max(1, currentLife);
			SourceDisplay = sourceDisplay ?? string.Empty;
			DomainId = domainId ?? string.Empty;
			DharmaFormStage = dharmaFormStage ?? XjShiDharmaFormStageIds.None;
		}

		internal ConversionPlan WithDomain(string domainId)
		{
			return new ConversionPlan(SourceTier, Tradition, TargetRealm, MingShuFloor,
				PracticeFloor, CurrentLife, SourceDisplay, domainId, DharmaFormStage);
		}
	}
}
