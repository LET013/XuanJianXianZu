using System;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Runtime;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LingWu;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.Runtime;

internal enum XjAnnualPipelineStage : byte
{
	Prepare = 0,
	Progression = 1,
	// Stage1 saves may persist this value after core progression. Stage2 treats
	// it as a legacy maintenance marker and migrates it into the maintenance lane.
	Finalize = 2
}

internal enum XjAnnualMaintenanceStage : byte
{
	Identity = 0,
	Ancillary = 1,
	Assets = 2
}

[Flags]
internal enum XjAnnualInterest : ushort
{
	None = 0,
	Progression = 1 << 0,
	Breakthrough = 1 << 1,
	HighRealm = 1 << 2,
	FaBao = 1 << 3,
	LostFaBao = 1 << 4,
	AutoCollect = 1 << 5,
	ZongMen = 1 << 6,
	JinDan = 1 << 7
}

internal readonly struct XjAnnualActorProfile
{
	internal readonly int RealmTier;
	internal readonly string RealmId;
	internal readonly XjAnnualInterest Interest;

	internal XjAnnualActorProfile(int realmTier, string realmId, XjAnnualInterest interest)
	{
		RealmTier = realmTier;
		RealmId = realmId ?? string.Empty;
		Interest = interest;
	}

	internal bool Has(XjAnnualInterest interest) => (Interest & interest) != 0;
}

internal enum XjAnnualActorCommandKind : byte
{
	Core = 0,
	Maintenance = 1,
	Secondary = 2
}

internal readonly struct XjAnnualActorCommand
{
	internal readonly XjAnnualActorCommandKind Kind;
	internal readonly Actor Actor;
	internal readonly int FromYearInclusive;
	internal readonly int AnnualYear;
	internal readonly XjAnnualPipelineStage CoreStage;
	internal readonly XjAnnualMaintenanceStage MaintenanceStage;

	private XjAnnualActorCommand(
		XjAnnualActorCommandKind kind,
		Actor actor,
		int fromYearInclusive,
		int annualYear,
		XjAnnualPipelineStage coreStage,
		XjAnnualMaintenanceStage maintenanceStage)
	{
		Kind = kind;
		Actor = actor;
		FromYearInclusive = Math.Max(1, fromYearInclusive);
		AnnualYear = Math.Max(0, annualYear);
		CoreStage = coreStage;
		MaintenanceStage = maintenanceStage;
	}

	internal static XjAnnualActorCommand Core(Actor actor, int annualYear, XjAnnualPipelineStage stage)
		=> new XjAnnualActorCommand(XjAnnualActorCommandKind.Core, actor, annualYear, annualYear, stage, default);

	internal static XjAnnualActorCommand Maintenance(
		Actor actor,
		int fromYearInclusive,
		int annualYear,
		XjAnnualMaintenanceStage stage)
		=> new XjAnnualActorCommand(XjAnnualActorCommandKind.Maintenance, actor, fromYearInclusive, annualYear, default, stage);

	internal static XjAnnualActorCommand Secondary(Actor actor, int annualYear)
		=> new XjAnnualActorCommand(XjAnnualActorCommandKind.Secondary, actor, annualYear, annualYear, default, default);
}

/// <summary>
/// Per-cultivator orchestration invoked by the scheduler.
/// Domain rules remain in their owning systems.
/// </summary>
internal static class XjSchedulerActorPipeline
{
	private const float ZhenYuanGainPerStep = 1f;
	private static bool _secondaryExtensionsRegistered;
	internal static bool ProcessStage(
		Actor actor,
		int annualYear,
		XjAnnualPipelineStage stage,
		Action<long> enqueueJinDanCombat,
		out XjAnnualPipelineStage nextStage)
	{
		nextStage = stage;
		if (XjXingDuQianPhantomSystem.IsPhantom(actor)) return false;
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| IsBoatLikeActor(actor))
		{
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			if (stage == XjAnnualPipelineStage.Prepare
				&& !XjDaoTaiPresenceArchive.TickPresence(actor, annualYear)) return false;
			if (stage == XjAnnualPipelineStage.Progression
				&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return false;
		}

		int qualifiedYear = XjScheduler.ReadCultivationQualifiedYear(actor);
		if (qualifiedYear > 0 && annualYear < qualifiedYear)
		{
			return false;
		}

		using (XjAnnualExecutionContext.Enter(annualYear))
		{
			XjAnnualActorCommand command = XjAnnualActorCommand.Core(actor, annualYear, stage);
			return ReduceCoreCommand(in command, enqueueJinDanCombat, out nextStage);
		}
	}

	internal static bool ProcessMaintenanceStage(
		Actor actor,
		int fromYearInclusive,
		int annualYear,
		XjAnnualMaintenanceStage stage,
		out XjAnnualMaintenanceStage nextStage)
	{
		nextStage = stage;
		if (XjXingDuQianPhantomSystem.IsPhantom(actor)) return false;
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| IsBoatLikeActor(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor)) return false;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return false;

		int qualifiedYear = XjScheduler.ReadCultivationQualifiedYear(actor);
		int fromYear = Math.Max(Math.Max(1, fromYearInclusive), qualifiedYear > 0 ? qualifiedYear : 1);
		if (annualYear < fromYear) return false;

		using (XjAnnualExecutionContext.Enter(annualYear))
		{
			XjAnnualActorCommand command = XjAnnualActorCommand.Maintenance(actor, fromYear, annualYear, stage);
			return ReduceMaintenanceCommand(in command, out nextStage);
		}
	}

	internal static bool HasSecondaryAnnualInterest(Actor actor, int requestedYear)
	{
		if (XjXingDuQianPhantomSystem.IsPhantom(actor)) return false;
		if (actor?.data == null || !actor.isAlive()) return false;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return false;
		EnsureSecondaryExtensionsRegistered();
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		XjAnnualSecondaryContext context = BuildSecondaryContext(actor, Math.Max(1, requestedYear), realmId, ResolveRealmTier(actor));
		return XjAnnualActorExtensionRegistry.HasQueueInterest(context);
	}

	/// <summary>
	/// 统一维护资格检测。没有任何实体槽到期、也没有资产维护需求的角色，
	/// 不再进入 Identity/Ancillary/Assets 三阶段队列。
	/// </summary>
	internal static bool HasMaintenanceAnnualInterest(
		Actor actor,
		int fromYearInclusive,
		int currentYear)
	{
		if (XjXingDuQianPhantomSystem.IsPhantom(actor)) return false;
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0 || IsBoatLikeActor(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor)) return false;
		if (actorId <= 0L) return false;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return false;
		int realmTier = ResolveRealmTier(actor);
		bool isZiFuPath = XjCultivationPathRules.IsZiFuJinDan(actor);
		bool isSharedImmortalHighRealm = !XjCultivationPathRules.IsShi(actor)
			&& realmTier >= XjRealmSuppression.TierZiFu;
		int fromYear = Math.Max(1, fromYearInclusive);
		if (isSharedImmortalHighRealm)
		{
			// Family/site/sect/fruit reconciliation is current-state maintenance, not an
			// annual chance roll. Realm/position/equipment writes already invalidate the
			// relevant indexes, so a stable 5-year actor slot is sufficient as a repair
			// backstop and further cuts the three-stage high-realm maintenance queue.
			return IsStableActorMaintenanceDueBetween(actorId, fromYear, currentYear, 5);
		}

		if (!XjLongShuSystem.IsLongShu(actor)
			&& (XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.FamilyIdentityRepair, actorId, fromYear, currentYear)
				|| XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.FamilyBranchAndSurname, actorId, fromYear, currentYear)))
		{
			return true;
		}

		if (actor.hasTrait("madness")
			&& (!isZiFuPath || !XjTrueDamageSystem.IsJinXingYaoXie(actor)))
		{
			return true;
		}

		// 宗门招募读取的是“修士当前所在城”增量索引。旧版只对已经入宗的筑基以上
		// 紫金修士做迁城对账，导致大量炼气/筑基散修在初次入索引后搬家，索引长期停留
		// 在旧城，宗门即使领地内有修士也看不到候选人。现在所有可被玄门招募的在世修士
		// 都按稳定ID分片每3年刷新一次城市位置，不增加全图扫描。
		if (realmTier > XjRealmSuppression.TierNone
			&& !XjCultivationPathRules.IsShi(actor)
			&& !XjLongShuSystem.IsLongShu(actor)
			&& !XjYinSiTraitLifecycle.IsYinSi(actor)
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.SectCityObservation, actorId, fromYear, currentYear))
		{
			return true;
		}

		if (realmTier > XjRealmSuppression.TierNone
			&& (XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.ThreeBookSocialObservation, actorId, fromYear, currentYear)
				|| (isZiFuPath
					&& XjDetectionGate.IsEntityMaintenanceDueBetween(
						XjEntityDetectionJob.TalismanDistribution, actorId, fromYear, currentYear))))
		{
			return true;
		}

		XjAnnualActorProfile finalProfile = ResolveFinalizeProfile(actor, fromYear, currentYear);
		return finalProfile.Has(
			XjAnnualInterest.FaBao
			| XjAnnualInterest.AutoCollect
			| XjAnnualInterest.ZongMen
			| XjAnnualInterest.JinDan);
	}

	private static bool IsStableActorMaintenanceDueBetween(
		long actorId,
		int fromYearInclusive,
		int currentYear,
		int intervalYears)
	{
		if (actorId <= 0L || currentYear <= 0 || intervalYears <= 1) return true;
		int fromYear = Math.Max(1, fromYearInclusive);
		if (currentYear < fromYear) return false;
		int offset = XjDeterministicHash.PositiveIndex(actorId, "annual_high_realm_maintenance", intervalYears);
		int remainder = fromYear % intervalYears;
		int delta = offset - remainder;
		if (delta < 0) delta += intervalYears;
		return fromYear + delta <= currentYear;
	}


	/// <summary>
	/// Exact-year secondary gameplay. Unlike coalesced compatibility maintenance,
	/// these systems change proficiency, production or annual chance outcomes and
	/// therefore consume every queued logical year in order.
	/// </summary>
	internal static void ProcessSecondaryAnnual(Actor actor, int annualYear)
	{
		if (XjXingDuQianPhantomSystem.IsPhantom(actor)) return;
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0 || IsBoatLikeActor(actor)) return;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			&& XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return;
		EnsureSecondaryExtensionsRegistered();
		XjAnnualActorCommand command = XjAnnualActorCommand.Secondary(actor, annualYear);
		ReduceSecondaryCommand(in command);
	}

	private static bool ReduceCoreCommand(
		in XjAnnualActorCommand command,
		Action<long> enqueueJinDanCombat,
		out XjAnnualPipelineStage nextStage)
	{
		nextStage = command.CoreStage;
		if (command.Kind != XjAnnualActorCommandKind.Core || command.Actor?.data == null) return false;
		switch (command.CoreStage)
		{
			case XjAnnualPipelineStage.Prepare:
				return ProcessPrepareStage(command.Actor, command.AnnualYear, out nextStage);
			case XjAnnualPipelineStage.Progression:
				return ProcessProgressionStage(command.Actor, command.AnnualYear, enqueueJinDanCombat, out nextStage);
			default:
				return false;
		}
	}

	private static bool ReduceMaintenanceCommand(
		in XjAnnualActorCommand command,
		out XjAnnualMaintenanceStage nextStage)
	{
		nextStage = command.MaintenanceStage;
		if (command.Kind != XjAnnualActorCommandKind.Maintenance || command.Actor?.data == null) return false;
		switch (command.MaintenanceStage)
		{
			case XjAnnualMaintenanceStage.Identity:
				ProcessMaintenanceIdentityStage(command.Actor, command.FromYearInclusive, command.AnnualYear);
				nextStage = XjAnnualMaintenanceStage.Ancillary;
				return true;
			case XjAnnualMaintenanceStage.Ancillary:
				ProcessMaintenanceAncillaryStage(command.Actor, command.FromYearInclusive, command.AnnualYear);
				nextStage = XjAnnualMaintenanceStage.Assets;
				return true;
			case XjAnnualMaintenanceStage.Assets:
				ProcessMaintenanceAssetsStage(command.Actor, command.FromYearInclusive, command.AnnualYear);
				return false;
			default:
				return false;
		}
	}

	private static void ReduceSecondaryCommand(in XjAnnualActorCommand command)
	{
		if (command.Kind != XjAnnualActorCommandKind.Secondary || command.Actor?.data == null) return;
		string realmIdAtYear = ResolveRealmIdAtYear(command.Actor, command.AnnualYear);
		using (XjAnnualExecutionContext.Enter(command.AnnualYear, command.Actor, realmIdAtYear))
		{
			int realmTier = XjAnnualCultivationPathRegistry.TryGetCombatTier(command.Actor, out int pathTier)
				? pathTier
				: XjRealmSuppression.GetRealmTierFromIdForRuntime(realmIdAtYear);
			XjAnnualSecondaryContext context = BuildSecondaryContext(
				command.Actor,
				command.AnnualYear,
				realmIdAtYear,
				realmTier);
			XjAnnualActorExtensionRegistry.ExecuteAll(context, RunSecondaryStep);
		}
	}

	private static XjAnnualSecondaryContext BuildSecondaryContext(
		Actor actor,
		int annualYear,
		string realmId,
		int realmTier)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool isFuQiHighRealm = isFuQi
			&& realmTier >= XjRealmSuppression.TierZiFu;
		bool isShi = XjCultivationPathRules.IsShi(actor);
		return new XjAnnualSecondaryContext(
			actor,
			actorId,
			annualYear,
			realmId,
			realmTier,
			isFuQi,
			isFuQiHighRealm,
			isShi);
	}

	private static bool HasFaBaoSecondaryInterest(XjAnnualSecondaryContext context)
	{
		if (context.BlocksImmortalAssets
			|| context.Actor?.data == null
			|| context.AnnualYear <= 0
			|| context.RealmTier < XjRealmSuppression.TierZhuJi)
		{
			return false;
		}

		if (XjFaBaoForgePolicy.CanAttemptPersonalZiFuLingBao(context.Actor, context.AnnualYear))
		{
			return true;
		}
		if (XjDaoTaiXianQiSystem.IsAttemptDue(context.Actor, context.AnnualYear))
		{
			return true;
		}

		// Ordinary ZhuJi+/ZhenRen actors used to enter exact-year replay only to
		// discover that they were not artifact refiners. Gate by the maintained craft
		// index first, then by the forge interval/retreat policy.
		if (!XjCraftActorIndex.Contains(context.ActorId)) return false;
		string forgeRealmId = XjFaBaoForgePolicy.ResolvePracticeRealmId(context.Actor, context.RealmId);
		return !string.IsNullOrWhiteSpace(forgeRealmId)
			&& XjFaBaoForgePolicy.CanAttemptScheduled(context.Actor, forgeRealmId, context.AnnualYear);
	}

	private static void EnsureSecondaryExtensionsRegistered()
	{
		if (_secondaryExtensionsRegistered) return;

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"LongGengSwordStele",
			10,
			_ => false,
			_ => true,
			context => XjLongGengSwordSteleSystem.TickActor(context.Actor, context.AnnualYear)));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"WeaponArt",
			20,
			context => XjWeaponArtSystem.HasAnnualInterest(context.Actor),
			context => XjWeaponArtSystem.IsActiveInYear(context.Actor, context.AnnualYear),
			context => XjWeaponArtSystem.TickActor(context.Actor, context.AnnualYear)));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"Craft",
			30,
			context => XjCraftActorIndex.Contains(context.ActorId),
			context => XjCraftActorIndex.Contains(context.ActorId)
				&& XjCraftTraitRules.IsActiveInYear(context.Actor, context.AnnualYear),
			context =>
			{
				long craftSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCraft, 31);
				try { XjCraftAnnualRouter.TickActor(context.Actor, context.AnnualYear); }
				finally { XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCraft, craftSample); }
			}));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"FaBaoForge",
			40,
			HasFaBaoSecondaryInterest,
			HasFaBaoSecondaryInterest,
			context =>
			{
				XjFaBaoAcquisition.TryForgeAnnualIfMissing(context.Actor, context.RealmId, context.AnnualYear);
				XjEquipmentForgeConsumer.TryForgeAnnual(context.Actor, context.RealmId, context.AnnualYear);
				XjDaoTaiXianQiSystem.TickActor(context.Actor, context.AnnualYear);
			}));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"LostFaBao",
			50,
			context => !context.BlocksImmortalAssets
				&& context.RealmTier > XjRealmSuppression.TierNone
				&& ShouldAttemptLostFaBaoDiscovery(context.Actor, context.AnnualYear),
			context => !context.BlocksImmortalAssets
				&& context.RealmTier > XjRealmSuppression.TierNone
				&& ShouldAttemptLostFaBaoDiscovery(context.Actor, context.AnnualYear),
			context => ProcessLostFaBaoDiscovery(context.Actor, context.AnnualYear)));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"UpperGoldSupport",
			55,
			context => XjUpperCultivatorGoldSupportSystem.HasAnnualInterest(context.Actor, context.AnnualYear),
			context => XjUpperCultivatorGoldSupportSystem.IsActiveInYear(context.Actor, context.AnnualYear),
			context => XjUpperCultivatorGoldSupportSystem.TickPatron(context.Actor, context.AnnualYear)));

		XjAnnualActorExtensionRegistry.Register(new XjAnnualActorExtensionDescriptor(
			"JinDanGift",
			60,
			context => !context.BlocksImmortalAssets
				&& context.RealmTier >= XjRealmSuppression.TierJinDan
				&& XjJinDanBreakthroughSystem.HasAnnualGiftDue(context.Actor, context.AnnualYear),
			context => !context.BlocksImmortalAssets
				&& context.RealmTier >= XjRealmSuppression.TierJinDan
				&& XjJinDanBreakthroughSystem.HasAnnualGiftDue(context.Actor, context.AnnualYear),
			context => XjJinDanBreakthroughSystem.TickAnnualGift(context.Actor, context.AnnualYear)));

		_secondaryExtensionsRegistered = true;
	}

	private static bool ProcessPrepareStage(Actor actor, int annualYear, out XjAnnualPipelineStage nextStage)
	{
		nextStage = XjAnnualPipelineStage.Progression;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			// 金性妖邪已脱离正常修炼年度逻辑，只维持阴司终局队列。
			// 必须先于寿尽、果位、家族、求金失败修复等所有普通高境维护。
			XjTrueDamageSystem.EnsureJinXingYaoXieCompanion(actor);
			return false;
		}

		// 硬寿限是“当前世界年”的兜底，不是历史年度债务的一部分。
		// 旧实现会在读档或高倍速积压时，对同一批角色连续补跑多年硬寿限，
		// 把原本应分散发生的寿尽压缩到一次队列清算，造成紫府成批死亡。
		int currentWorldYear = Math.Max(0, World.world?.map_stats?.year ?? 0);
		bool isCurrentYearBackstop = currentWorldYear <= 0 || annualYear >= currentWorldYear;
		if (isCurrentYearBackstop && XjVanillaDeathGuard.EnforceHardLifespanLimit(actor))
		{
			return false;
		}
		if (XjYinSiTraitLifecycle.IsYinSi(actor))
		{
			XjYinSiTraitLifecycle.EnsureTransientState(actor);
			return false;
		}

		// 0.9.9.7 及更早存档可能已经有权威 RealmId，却因为内部晋升使用
		// syncVisibleTraits=false 漏掉伴生的 WorldBox 原生特质。只在当前世界年
		// 借现有年度 Actor 管线做幂等补齐，不增加额外世界扫描，也不回放历史年。
		if (isCurrentYearBackstop
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string projectionRealm))
		{
			projectionRealm = XjRealmHelper.NormalizeId(projectionRealm);
			// 旧档字段归一化必须发生在明确的年度维护写入口；普通 Snapshot/BuildState/战斗/UI 读取保持纯读。
			XjDaoHuiPolicy.NormalizeStoredValue(actor);
			XjJinDanAccessor.ReconcileStoredIdentityAliases(actor);
			XjVisibleTraitSync.EnsureRealmNativeTraits(actor, projectionRealm);
			// 龙属总量上限仅三名；顺带修复旧档“已成道胎但姓名仍停在旧后缀”。
			if (XjLongShuSystem.IsLongShu(actor)
				&& (string.Equals(projectionRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
					|| string.Equals(projectionRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string projectionDaoTu);
				XjRealmTitleApplyService.EnsureTitleForRealm(actor, projectionRealm, projectionDaoTu);
			}
		}

		// 果位钟爱转世以真灵契合锁定原道途。年度准备阶段只在发现
		// 旧档或旁路把道途改错时执行一次事务性修复。
		XjGuoWeiFavoredDaoTuLock.ReconcileActor(actor, syncVisibleTraits: false);

		if (XjAnnualCultivationPathRegistry.TryPrepare(actor, annualYear))
		{
			return true;
		}
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			int fuQiRealmTier = ResolveRealmTier(actor);
			// 真人与紫府共享五年一次的本命灵宝素材兜底；服气功法、采气和求金仍不接入。
			if (fuQiRealmTier == XjRealmSuppression.TierZiFu
				&& XjZiFuLingWuOpportunitySystem.IsDue(actor, annualYear))
			{
				XjZiFuLingWuOpportunitySystem.TryGrant(actor, annualYear);
			}
			XjFuQiAnnualRouter.PrepareActor(actor, annualYear);
			return true;
		}

		int realmTier = ResolveRealmTier(actor);
		bool isZiFuOrAbove = realmTier >= XjRealmSuppression.TierZiFu;
		bool isJinDan = realmTier >= XjRealmSuppression.TierJinDan;
		EnsureMissingDaoTuForEnteredCultivator(actor, realmTier);
		XjProgressionCandidateState candidateState =
			XjCultivatorCandidateIndex.GetOrRefreshProgression(actor, annualYear);

		// These are progression-state compatibility repairs. High-realm detection
		// reads them in the immediately following core stage, so they remain here.
		if (XjJinDanResidualJinXing.HasLegacyTrait(actor)
			|| XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, out string residualSource)
				&& !string.IsNullOrWhiteSpace(residualSource))
		{
			XjJinDanResidualJinXing.ReconcileSource(actor);
		}
		if (isZiFuOrAbove
			|| (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState)
				&& !string.IsNullOrWhiteSpace(failedState)))
		{
			XjJinDanBreakthroughSystem.ReconcileFailureDemonization(actor);
		}
		if (isJinDan)
		{
			XjGuoWeiQuanBingRegistry.ReconcileLiveActorReadOnly(actor);
			XjGuoWeiQuanBingRegistry.SyncActiveSnapshotToActor(actor);
		}
		// Stage1 opportunity clocks belong to progression, not compatibility maintenance.
		if (realmTier == XjRealmSuppression.TierZiFu
			&& XjZiFuLingWuOpportunitySystem.IsDue(actor, annualYear))
		{
			XjZiFuLingWuOpportunitySystem.TryGrant(actor, annualYear);
		}

		if (isZiFuOrAbove && IsNearNaturalLifespan(actor))
		{
			// Borrowing needs a stable family id. Only the near-death subset receives
			// this targeted repair on the guaranteed core lane.
			if (!XjLongShuSystem.IsLongShu(actor)
				&& (!XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
					|| !identity.Found
					|| identity.FamilyStableIdValue <= 0L))
			{
				XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
			}
			XjJinDanResidualJinXing.TryBorrowForReincarnation(actor, annualYear);
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int legacyXianJiCount)
			&& legacyXianJiCount > 0)
		{
			XjXianJiAccessor.ReconcileRealmLimit(actor);
		}
		bool caiQiDue = realmTier <= XjRealmSuppression.TierLianQi
			&& candidateState.ShouldProcessCaiQi(annualYear);
		XjStageZeroObservation.RecordCandidateRouting("CaiQi", caiQiDue);
		if (caiQiDue)
		{
			ProcessCaiQiCompletion(actor, annualYear);
			XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
		}
		if (XjAptitudeTraitLifecycle.HasAnnualInterest(actor, annualYear))
		{
			XjAptitudeTraitLifecycle.TickAnnual(actor, annualYear);
		}
		return true;
	}


	private static void EnsureMissingDaoTuForEnteredCultivator(Actor actor, int realmTier)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| (realmTier < XjRealmSuppression.TierLianQi
				&& XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierLianQi))
		{
			return;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& !string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _))
		{
			return;
		}

		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out _);
	}

	private static bool ProcessProgressionStage(
		Actor actor,
		int annualYear,
		Action<long> enqueueJinDanCombat,
		out XjAnnualPipelineStage nextStage)
	{
		nextStage = XjAnnualPipelineStage.Progression;
		// 候神殊神尸“只可存世，不再求道”：保留社会/维护生命周期，但不再增长修为或破境。
		if (XjHouShenShuSystem.IsShenShi(actor)) return false;
		if (XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			XjTrueDamageSystem.EnsureJinXingYaoXieCompanion(actor);
			return false;
		}
		// 命数子的因果拟身走既有年度角色管线，不引入新的全世界扫描。
		XjMingShuChildSystem.TickCausalImpersonation(actor, annualYear);

		// 道胎仍然承载晋升前的金丹果位/权柄账本。外道夺柄后的十年合道
		// 不能因为境界从金丹切到道胎就退出 AuthorityLifecycle，否则会出现
		// “道胎夺柄后直接持有/永不结算融合”的断链。两条修法的道胎统一走
		// 同一权柄融合状态机；这里只处理已存在的权柄账本，不重新跑金丹晋升逻辑。
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			XjGuoWeiQuanBingLifecycle.TickActor(actor, annualYear);
			enqueueJinDanCombat?.Invoke(((BaseSystemData)actor.data).id);
		}

		if (XjAnnualCultivationPathRegistry.TryProgress(actor, annualYear))
		{
			return false;
		}
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			XjFuQiAnnualRouter.TickActor(actor, annualYear);
			XjActorCultivationSnapshot fuQiSnapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			if (XjCultivationPathRules.IsJinDanEquivalentRealm(fuQiSnapshot.RealmId)
				|| string.Equals(fuQiSnapshot.RealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
			{
				if (XjCultivationPathRules.IsJinDanEquivalentRealm(fuQiSnapshot.RealmId))
				{
					XjJinDanState jinDanState = XjJinDanAccessor.BuildState(actor);
					if (jinDanState.Found
						&& !string.IsNullOrWhiteSpace(fuQiSnapshot.DaoTu)
						&& !string.IsNullOrWhiteSpace(jinDanState.JinXing)
						&& !string.IsNullOrWhiteSpace(jinDanState.GuoWei))
					{
						// 对0.9.4.35已存在的真君羽士补跑一次幂等成功链，
						// 从而补齐纪元、洞天、家族事件与法宝；新晋升角色会被成功链标记直接挡回。
						XjJinDanBreakthroughSystem.RunJinDanSuccessEventChain(
							actor,
							fuQiSnapshot.DaoTu,
							jinDanState.JinXing,
							jinDanState.GuoWei,
							annualYear,
							in fuQiSnapshot,
							publishPromotionAnnouncement: false,
							eraChangeCauseOverride: XjAnnouncementText.BuildFuQiZhenJunEraChangeCause(
								actor,
								fuQiSnapshot.DaoTu,
								jinDanState.JinXing,
								jinDanState.GuoWei));
					}
				}
				var fuQiHighRealmContext = new XjHighRealmDetectionContext(
					actor, fuQiSnapshot, annualYear, budget: 6);
				XjHighRealmDetectionPipeline.Tick(ref fuQiHighRealmContext);
				enqueueJinDanCombat?.Invoke(((BaseSystemData)actor.data).id);
			}
			return false;
		}
		XjManualRealmTraitReconciliation.CleanupLegacyPendingManualJinDan(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string liveRealmId);
		if (string.Equals(XjRealmHelper.NormalizeId(liveRealmId), XjRealmIds.JinDan, StringComparison.Ordinal)
			&& (!XjXianJiAccessor.HasFive(actor)
				|| !XjActorGongFaCollection.HasJinDanGongFaSet(actor)
				|| !XjQiuJinFaAccessor.BuildState(actor).Ready)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string repairDaoTu)
			&& !string.IsNullOrWhiteSpace(repairDaoTu))
		{
			// 修复0.9.4.35中手动金丹只留下单神通/单功法的旧档偏移。
			// 已满足五神通、五功法与求金法不变量的正常金丹不会进入此分支。
			XjManualRealmTraitReconciliation.EnsureHighRealmGongFaSet(actor, repairDaoTu);
		}
		XjAnnualActorProfile profile = ResolveProgressionProfile(actor);
		long snapshotSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualSnapshot, 31);
		XjActorCultivationSnapshot annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, profile.RealmTier);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, snapshotSample);
		long growthSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualCultivationGrowth, 31);
		bool didGrow = ProcessCultivationGrowth(actor, annualSnapshot, annualYear);
		XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualCultivationGrowth, growthSample);
		if (didGrow)
		{
			XjProgressionCandidateState candidateState =
				XjCultivatorCandidateIndex.RefreshAfterGrowth(actor, annualYear);
			bool qingXuanChecked = false;
			bool qingXuanDue = candidateState.Has(XjProgressionCandidateFlags.QingXuan)
				&& ShouldProcessQingXuan(actor, annualSnapshot);
			XjStageZeroObservation.RecordCandidateRouting("QingXuan", qingXuanDue);
			if (qingXuanDue)
			{
				qingXuanChecked = true;
				long qingXuanSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualQingXuan, 31);
				XjQingXuanKongZhengSystem.TickActor(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualQingXuan, qingXuanSample);
			}
			if (qingXuanChecked
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string postQingXuanDaoTu)
				&& !string.Equals(postQingXuanDaoTu, annualSnapshot.DaoTu, StringComparison.Ordinal))
			{
				long rebuildSnapshotSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualSnapshot, 31);
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualSnapshot, rebuildSnapshotSample);
			}
			else if (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float postGrowthZhenYuan))
			{
				annualSnapshot = annualSnapshot.WithZhenYuan(postGrowthZhenYuan);
			}

			candidateState = XjCultivatorCandidateIndex.GetOrRefreshProgression(actor, annualYear);
			bool gongFaDue = candidateState.ShouldProcessGongFa(annualYear);
			XjStageZeroObservation.RecordCandidateRouting("GongFa", gongFaDue);
			if (gongFaDue && ShouldProcessGongFa(actor, annualSnapshot, profile.RealmTier, annualYear))
			{
				long gongFaSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualGongFa, 31);
				ProcessGongFa(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualGongFa, gongFaSample);
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				candidateState = XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}

			bool highRealmDue = candidateState.Has(XjProgressionCandidateFlags.HighRealm);
			XjStageZeroObservation.RecordCandidateRouting("HighRealm", highRealmDue);
			if (highRealmDue)
			{
				long highRealmSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualHighRealm, 31);
				ProcessHighRealm(actor, annualSnapshot, annualYear, enqueueJinDanCombat);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualHighRealm, highRealmSample);
				annualSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
				candidateState = XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}

			bool breakthroughDue = candidateState.ShouldProcessBreakthrough(annualYear);
			XjStageZeroObservation.RecordCandidateRouting("Breakthrough", breakthroughDue);
			if (breakthroughDue && ShouldProcessBreakthrough(annualSnapshot))
			{
				long breakthroughSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualBreakthrough, 31);
				ProcessBreakthrough(actor, annualSnapshot, annualYear);
				XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualBreakthrough, breakthroughSample);
				XjCultivatorCandidateIndex.RefreshProgression(actor, annualYear);
			}
		}

		// The scheduler commits the core cursor immediately after this stage. All
		// family/sect/equipment and secondary yearly work is queued independently.
		return false;
	}

	private static XjAnnualActorProfile ResolveProgressionProfile(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjAnnualActorProfile(XjRealmSuppression.TierNone, string.Empty, XjAnnualInterest.None);
		}

		int realmTier = ResolveRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (XjCultivationPathRules.IsShi(actor))
		{
			return new XjAnnualActorProfile(realmTier, string.Empty, XjAnnualInterest.Progression);
		}
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return new XjAnnualActorProfile(realmTier, realmId, XjAnnualInterest.Progression);
		}
		if (!string.IsNullOrWhiteSpace(realmId))
		{
			XjCultivationStateTransitions.EnsureDaoTuForRealm(actor, realmId, true);
		}
		XjAnnualInterest interest = XjAnnualInterest.Progression;
		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			interest |= XjAnnualInterest.HighRealm;
		}
		else
		{
			interest |= XjAnnualInterest.Breakthrough;
		}

		return new XjAnnualActorProfile(realmTier, realmId, interest);
	}

	private static XjAnnualActorProfile ResolveFinalizeProfile(Actor actor, int fromYearInclusive, int currentYear)
	{
		if (actor?.data == null)
		{
			return new XjAnnualActorProfile(XjRealmSuppression.TierNone, string.Empty, XjAnnualInterest.None);
		}

		int realmTier = ResolveRealmTier(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (XjCultivationPathRules.IsShi(actor))
		{
			return new XjAnnualActorProfile(realmTier, string.Empty, XjAnnualInterest.None);
		}
		bool isFuQiHighRealm = XjCultivationPathRules.IsFuQiYangXing(actor)
			&& realmTier >= XjRealmSuppression.TierZiFu;
		if (XjCultivationPathRules.IsFuQiYangXing(actor) && !isFuQiHighRealm)
		{
			return new XjAnnualActorProfile(realmTier, realmId, XjAnnualInterest.None);
		}
		XjAnnualInterest interest = XjAnnualInterest.None;
		long actorId = ((BaseSystemData)actor.data).id;
		bool needsPersonalZiFuLingBao = XjFaBaoForgePolicy.NeedsPersonalZiFuLingBao(actor);
		if (realmTier >= XjRealmSuppression.TierZhuJi
			&& (needsPersonalZiFuLingBao
				|| (IsEquipmentMaintenanceDue(actorId, realmTier, fromYearInclusive, currentYear)
					&& XjFaBaoEquipmentSync.HasAnnualInterest(actor))))
		{
			interest |= XjAnnualInterest.FaBao;
		}
		if (ShouldAttemptLostFaBaoDiscovery(actor, currentYear))
		{
			interest |= XjAnnualInterest.LostFaBao;
		}
		if (XjAutoCollectSystem.HasAnnualInterest(actor, realmId))
		{
			interest |= XjAnnualInterest.AutoCollect;
		}
		if (realmTier >= XjRealmSuppression.TierZiFu
			|| (realmTier >= XjRealmSuppression.TierZhuJi
				&& HasZongMenIdentity(actor)
				&& XjDetectionGate.IsEntityMaintenanceDueBetween(
					XjEntityDetectionJob.SectIdentityRefresh, actorId, fromYearInclusive, currentYear)))
		{
			interest |= XjAnnualInterest.ZongMen;
		}
		if (realmTier >= XjRealmSuppression.TierJinDan)
		{
			interest |= XjAnnualInterest.JinDan;
		}

		return new XjAnnualActorProfile(realmTier, realmId, interest);
	}

	private static bool ShouldProcessQingXuan(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		return XjQingXuanKongZhengSystem.HasAnnualInterest(actor, snapshot.RealmId, snapshot.DaoTu);
	}

	private static bool ShouldProcessGongFa(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		int realmTier,
		int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor) || snapshot.XjZz <= 0)
		{
			return false;
		}

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade)
			|| grade <= 0
			|| grade > XjGongFaDefinition.MaxGrade
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaName, out string name)
			|| string.IsNullOrWhiteSpace(name))
		{
			return true;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string gongFaDaoTu);
		if (!string.IsNullOrWhiteSpace(snapshot.DaoTu)
			&& (string.IsNullOrWhiteSpace(gongFaDaoTu)
				|| !string.Equals((gongFaDaoTu ?? string.Empty).Trim(), snapshot.DaoTu.Trim(), StringComparison.Ordinal)))
		{
			return true;
		}

		int maximumAllowedGrade = Math.Min(
			XjGongFaDefinition.MaxGrade,
			Math.Min(
				XjGongFaAptitudeRules.GetAptitudeGradeCap(actor, snapshot.XjZz),
				XjGongFaAptitudeRules.GetRealmGradeCap(realmTier)));
		if (maximumAllowedGrade <= grade)
		{
			return false;
		}

		int nextGrade = grade + 1;
		if (nextGrade > XjGongFaDefinition.MaxGrade)
		{
			return false;
		}
		if (nextGrade == 6 && !snapshot.HasQiuJinFa)
		{
			return false;
		}

		return XjGongFaAttemptSchedule.IsDue(actor, nextGrade, currentYear);
	}

	private static bool ShouldProcessBreakthrough(in XjActorCultivationSnapshot snapshot)
	{
		return XjCultivationNextRealmResolver.TryGetNextRule(snapshot.RealmId, out XjRealmRule targetRule)
			&& targetRule.IsImplemented
			&& CanWriteBreakthroughRealmId(targetRule.RealmId)
			&& !targetRule.RequiresFiveXianJi
			&& snapshot.ZhenYuan >= targetRule.RequiredZhenYuan;
	}

	private static int ResolveRealmTier(Actor actor)
	{
		if (actor?.data == null)
		{
			return XjRealmSuppression.TierNone;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return XjCultivatorCache.TryGetRealmTier(actorId, out int cachedTier)
			? cachedTier
			: XjRealmSuppression.GetRealmTier(actor);
	}

	private static bool IsEquipmentMaintenanceDue(
		long actorId,
		int realmTier,
		int fromYearInclusive,
		int currentYear)
	{
		if (realmTier >= XjRealmSuppression.TierJinDan) return true;
		XjEntityDetectionJob job = realmTier >= XjRealmSuppression.TierZiFu
			? XjEntityDetectionJob.ZiFuEquipmentMaintenance
			: XjEntityDetectionJob.ZhuJiEquipmentMaintenance;
		return XjDetectionGate.IsEntityMaintenanceDueBetween(job, actorId, fromYearInclusive, currentYear);
	}

	private static bool IsNearNaturalLifespan(Actor actor)
	{
		if (actor?.stats == null)
		{
			return false;
		}

		float lifespan = Math.Max(0f, actor.stats["lifespan"]);
		return lifespan > 0f && Math.Max(0f, actor.getAge()) >= lifespan * 0.95f;
	}

	private static bool HasZongMenIdentity(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor) > 0L;
	}

	private static void RunSecondaryStep(long actorId, int annualYear, string label, Action action)
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			UnityEngine.Debug.LogError(
				"[玄鉴][年度次级车道] actor=" + actorId
				+ " year=" + annualYear
				+ " step=" + (label ?? string.Empty)
				+ " ex=" + ex);
		}
	}

	private static string ResolveRealmIdAtYear(Actor actor, int annualYear)
	{
		if (XjCultivationPathRules.IsShi(actor)) return string.Empty;
		string currentRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (actor?.data == null || annualYear <= 0 || string.IsNullOrWhiteSpace(currentRealmId))
		{
			return currentRealmId ?? string.Empty;
		}

		// Once the requested logical year reaches the latest transition, the live
		// realm is authoritative. Earlier years are reconstructed from persistent
		// threshold timestamps so a lagging secondary lane cannot grant ZiFu/JinDan
		// production before those prerequisites actually existed.
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int currentEnteredYear)
			&& currentEnteredYear > 0
			&& annualYear >= currentEnteredYear)
		{
			return currentRealmId;
		}

		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, out int fuQiZhenJunYear)
				&& fuQiZhenJunYear > 0 && annualYear >= fuQiZhenJunYear)
			{
				return XjRealmIds.ZhenJunYuShi;
			}
			int fuQiZhenRenYear = 0;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiZhenRenEnteredYear, out fuQiZhenRenYear);
			if (fuQiZhenRenYear <= 0)
			{
				// 0.9.6.4 之前没有独立真人入境年份。真人晋升当年会启动神妙圆满项目，
				// 因此旧档可用项目起始年作保守且不超前的历史回填。
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, out fuQiZhenRenYear);
			}
			if (fuQiZhenRenYear > 0 && annualYear >= fuQiZhenRenYear)
			{
				return XjRealmIds.FuQiZhenRen;
			}
			int huangGuanYear = 0;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiHuangGuanEnteredYear, out huangGuanYear);
			if (huangGuanYear <= 0)
			{
				// 本命核心完成年就是黄冠入境年；旧档保留该字段，可直接用于历史重建。
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectCompleteYear, out huangGuanYear);
			}
			if (huangGuanYear > 0 && annualYear >= huangGuanYear)
			{
				return XjRealmIds.HuangGuan;
			}
			return XjRealmIds.LianQi;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanYear, out int shenDanYear)
			&& shenDanYear > 0 && annualYear >= shenDanYear)
		{
			return XjRealmIds.ShenDan;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int jinDanYear)
			&& jinDanYear > 0 && annualYear >= jinDanYear)
		{
			return XjRealmIds.JinDan;
		}
		int ziFuYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(actor);
		if (ziFuYear > 0 && annualYear >= ziFuYear)
		{
			return XjRealmIds.ZiFu;
		}
		int zhuJiYear = XjCultivationStateTransitions.ReadZhuJiEnteredYear(actor);
		if (zhuJiYear > 0 && annualYear >= zhuJiYear)
		{
			return XjRealmIds.ZhuJi;
		}

		// Exact distinction below ZhuJi does not affect secondary production; use
		// LianQi as the safe non-forging historical realm for an already-qualified
		// cultivator whose older transition timestamp is unavailable.
		return XjRealmIds.LianQi;
	}

	private static bool IsBoatLikeActor(Actor actor)
	{
		ActorAsset asset = actor?.asset;
		if (asset == null)
		{
			return false;
		}

		if (asset.is_boat)
		{
			return true;
		}

		string assetId = asset.id ?? string.Empty;
		return assetId.IndexOf("boat", StringComparison.OrdinalIgnoreCase) >= 0
			|| assetId.IndexOf("ship", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void ProcessMaintenanceIdentityStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		int realmTier = ResolveRealmTier(actor);
		bool isZiFuPath = XjCultivationPathRules.IsZiFuJinDan(actor);
		bool isSharedImmortalHighRealm = !XjCultivationPathRules.IsShi(actor)
			&& realmTier >= XjRealmSuppression.TierZiFu;
		bool isZiJinHighRealm = isZiFuPath && isSharedImmortalHighRealm;
		bool isYaoXie = isZiFuPath && XjTrueDamageSystem.IsJinXingYaoXie(actor);

		if (!XjLongShuSystem.IsLongShu(actor))
		{
			if (isSharedImmortalHighRealm)
			{
				bool hasConfirmedFamily = XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
					&& identity.Found
					&& identity.FamilyStableIdValue > 0L;
				if (!hasConfirmedFamily
					|| XjFamilyMemberIndex.Shared.IsActorPending(actorId)
					|| !actor.hasClan())
				{
					XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
				}
			}
			else if (XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.FamilyIdentityRepair, actorId, fromYearInclusive, currentYear)
				&& (!XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out _)
					|| XjFamilyMemberIndex.Shared.IsActorPending(actorId)))
			{
				// Pending is not a terminal state. Re-evaluate on the existing bounded
				// maintenance cadence so children can bind to a father's persisted family
				// identity after the father's live runtime record is evicted by H3.
				XjFamilyMemberIndex.Shared.AddActorToFamily(actor);
			}
		}

		if (isSharedImmortalHighRealm
			|| XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.FamilyBranchAndSurname, actorId, fromYearInclusive, currentYear))
		{
			if (XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity maintenanceIdentity)
				&& maintenanceIdentity.Found)
			{
				XjFamilyMemberIndex.Shared.ReconcileCityBranch(actor, currentYear);
				XjFamilySurnamePolicy.EnsureForConfirmedActor(actor);
			}
		}
		if (!isYaoXie && actor.hasTrait("madness"))
		{
			XjVisibleTraitSync.EnsureCultivatorNoMadness(actor);
		}
		bool sectCityObservationDue = realmTier > XjRealmSuppression.TierNone
			&& !XjCultivationPathRules.IsShi(actor)
			&& !XjLongShuSystem.IsLongShu(actor)
			&& !XjYinSiTraitLifecycle.IsYinSi(actor)
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.SectCityObservation, actorId, fromYearInclusive, currentYear);
		if (isSharedImmortalHighRealm || sectCityObservationDue)
		{
			XjSectCultivatorCityIndex.Observe(actor);
		}
		if (isZiJinHighRealm
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.QiuJinWarehouseReconcile, actorId, fromYearInclusive, currentYear))
		{
			XjQiuJinFaWarehouseReconciler.ReconcileActor(actor, currentYear);
		}
		if (isSharedImmortalHighRealm)
		{
			XjDaoTaiAscensionSystem.TickActor(actor, currentYear);
		}
	}

	private static void ProcessMaintenanceAncillaryStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		XjAnnualActorProfile profile = ResolveProgressionProfile(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjCultivationPathRules.IsZiFuJinDan(actor)
			&& profile.RealmTier > XjRealmSuppression.TierNone
			&& XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.TalismanDistribution, actorId, fromYearInclusive, currentYear))
		{
			long talismanSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualTalisman, 31);
			XjTalismanDistributionSystem.TickActor(actor, currentYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualTalisman, talismanSample);
		}
		if (profile.RealmTier > XjRealmSuppression.TierNone
			&& XjDetectionGate.TryResolveLatestEntityMaintenanceYear(
				XjEntityDetectionJob.ThreeBookSocialObservation,
				actorId,
				fromYearInclusive,
				currentYear,
				out int socialYear))
		{
			long socialSample = XjRuntimeDiagnostics.BeginSample(XjRuntimeHotspot.AnnualThreeBookSocial, 31);
			XjThreeBookSocialObserver.TickActor(actor, socialYear);
			XjRuntimeDiagnostics.EndSample(XjRuntimeHotspot.AnnualThreeBookSocial, socialSample);
		}
	}

	private static void ProcessMaintenanceAssetsStage(Actor actor, int fromYearInclusive, int currentYear)
	{
		XjAnnualActorProfile profile = ResolveFinalizeProfile(actor, fromYearInclusive, currentYear);
		if (profile.Has(XjAnnualInterest.FaBao))
		{
			XjEquipmentForgeConsumer.ReconcileManagedItemLimit(actor, profile.RealmId);
			XjFaBaoEquipmentSync.TryBorrowFamilyFaBao(actor, profile.RealmId, currentYear);
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
		}
		if (profile.Has(XjAnnualInterest.AutoCollect)) XjAutoCollectSystem.TickActor(actor, profile.RealmId);
		if (profile.Has(XjAnnualInterest.ZongMen)) ProcessZongMen(actor, profile.RealmId, fromYearInclusive, currentYear);
		if (profile.Has(XjAnnualInterest.JinDan))
		{
			XjGuoWeiRegistry.ReconcileLiveActor(actor);
		}
	}

	private static bool ShouldAttemptLostFaBaoDiscovery(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		// 失落法宝从“每人每年2%”改为稳定ID错峰的五年一次10%：
		// 五年累计发现概率与旧规则约9.61%接近，同时80%的年份连仓库存在性都不读取。
		int phase = XjDeterministicHash.PositiveIndex(actorId, "lost_fabao_discovery.phase", 5);
		if ((currentYear + phase) % 5 != 0) return false;
		if (!XjFamilyFaBaoWarehouse.HasLostEntriesAtOrBeforeYear(currentYear)) return false;
		return XjDeterministicHash.PositiveIndex(actorId + currentYear, "lost_fabao_discovery", 100) < 10;
	}

	private static void ProcessLostFaBaoDiscovery(Actor actor, int currentYear)
	{
		if (ShouldAttemptLostFaBaoDiscovery(actor, currentYear))
		{
			XjFamilyFaBaoWarehouse.TryDiscoverLostFaBao(actor, currentYear);
		}
	}

	private static bool ProcessCultivationGrowth(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
		float elapsedYears = ZhenYuanGainPerStep;
		int previousYear = 0;
		if (currentYear > 0)
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLastCultivationYear, out int lastYear))
			{
				previousYear = lastYear;
				if (lastYear == currentYear)
				{
					return false;
				}
				if (lastYear > currentYear)
				{
					// 旧档或跨世界恢复可能留下未来年份。修复为当前年前一年，
					// 避免角色永久认为本年已经修炼。
					lastYear = currentYear - 1;
				}
				if (lastYear > 0 && currentYear > lastYear)
				{
					elapsedYears = Math.Max(1, currentYear - lastYear);
				}
			}

			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLastCultivationYear, currentYear);
		}

		XjStageZeroObservation.RecordCultivationGrowth(previousYear, currentYear, elapsedYears);
		// 高倍速年度请求合并到最新年份；遗漏年份只聚合被动真元增长，
		// 功法、瓶颈、丹药和突破仍只在当前年度执行一次，避免补算连跳境界。
		XjCultivationLocalExecutor.RunValidatedLocalStep(actor, elapsedYears, snapshot);
		return true;
	}

	private static void ProcessGongFa(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		XjGongFaProgression.TickActor(actor, snapshot);
		if (!string.Equals(snapshot.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return;
		}
		// 仅针对已进入年度功法处理的筑基修士补旧档缺失的首门仙基；没有额外
		// 世界扫描，也不进入紫府以后的五神通判定。手动授境前版本留下的空状态
		// 会在下一次正常筑基年度处理时自愈。
		if (snapshot.XianJiCount <= 0)
		{
			XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, snapshot.DaoTu, currentYear);
		}

		if (XjDaoXingStageRules.IsZhuJiLateOrHigher(
			snapshot.RealmId,
			snapshot.ZhenYuan,
			snapshot.XianJiCount))
		{
			XjSectCityData.HandleFoundationLatePromotion(actor, currentYear);
		}
	}

	private static void ProcessHighRealm(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear, Action<long> enqueueJinDanCombat)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		var ctx = new XjHighRealmDetectionContext(actor, snapshot, currentYear, budget: 6);
		XjHighRealmDetectionPipeline.Tick(ref ctx);

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string postRealmId);
		if (string.Equals(postRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
				bool successChainHandledZongMen = XjActorAccessor.TryGetInt(
						actor,
						XjActorDataKeys.XjJinDanSuccessEventYear,
						out int successEventYear)
					&& successEventYear == currentYear;
			if (!successChainHandledZongMen)
			{
				XjSectCityData.HandleJinDanPromotion(actor, currentYear);
			}
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.JinDan, "JinDanPromotion");
		}

		if (string.Equals(postRealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(postRealmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			enqueueJinDanCombat?.Invoke(((BaseSystemData)actor.data).id);
		}
	}

	private static void ProcessCaiQiCompletion(Actor actor, int currentYear)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		if (!XjCaiQiActorAccessor.ShouldEnqueueForCaiQi(actor))
		{
			return;
		}

		if (XjCaiQiActorAccessor.GetNextCaiQiYear(actor) <= 0)
		{
			int interval = 3 + (int)(((ulong)((BaseSystemData)actor.data).id + (ulong)(long)currentYear) % 3uL);
			XjCaiQiActorAccessor.SetNextCaiQiYear(actor, currentYear + interval);
			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Pending);
			return;
		}

		if (currentYear < XjCaiQiActorAccessor.GetNextCaiQiYear(actor))
		{
			return;
		}

		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Active);
		if (TryConsumePreferredFamilyQi(actor, out XjCaiQiCatalogEntry familyQiEntry))
		{
			XjCaiQiActorAccessor.MarkCompleted(
				actor,
				XjCaiQiResultTypes.XianTianQi,
				familyQiEntry.PlaceTypeId,
				familyQiEntry.BranchId,
				"家族仓库");
			XjCaiQiFaAcquisition.TryAcquireFromCaiQiResult(
				actor,
				new XjCaiQiResolvedResult(
					true,
					XjCaiQiResultTypes.XianTianQi,
					familyQiEntry.PlaceTypeId,
					familyQiEntry.BranchId,
					"家族仓库",
					"FamilyWarehousePreferredQi"));
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjActorCaiQiCandidate candidate = XjActorCaiQiCandidateResolver.TryResolve(actor);
		if (!candidate.Found)
		{
			if (IsDaoZhuActor(actor))
			{
				RetryCaiQiNextYear(actor, currentYear);
				return;
			}

			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Failure, candidate.ReasonCode);
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjCaiQiResolvedResult result = XjCaiQiResultResolver.Resolve(actor, candidate, currentYear);
		if (!result.Success)
		{
			if (IsDaoZhuActor(actor))
			{
				RetryCaiQiNextYear(actor, currentYear);
				return;
			}

			XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Failure, result.ReasonCode);
			ScheduleNextCaiQi(actor, currentYear);
			return;
		}

		XjCaiQiActorAccessor.MarkCompleted(actor, result.ResultType, result.PlaceTypeId, result.BranchId, result.SiteName);
		XjCaiQiFaAcquisition.TryAcquireFromCaiQiResult(actor, result);
		ScheduleNextCaiQi(actor, currentYear);
	}

	private static bool TryConsumePreferredFamilyQi(Actor actor, out XjCaiQiCatalogEntry entry)
	{
		entry = default;
		if (actor?.data == null
			|| !XjFamilyDaoTuRules.TryResolvePreferredDaoTu(actor, out string preferredDaoTu)
			|| !XjCaiQiCatalog.TryGetEntryByDisplayName(preferredDaoTu, out entry)
			|| !XjCaiQiAvailabilityRules.IsEntryCollectible(in entry)
			|| !XjCaiQiCatalog.TryGetOldResourceIdByBranchId(entry.BranchId, out string resourceId)
			|| string.IsNullOrWhiteSpace(resourceId)
			|| !XjCaiQiAvailabilityRules.IsResourceCollectible(resourceId)
			|| !XjBreakthroughRules.TryResolveFamilyKey(actor, out string familyKey)
			|| !XjFamilyCaiQiWarehouse.TryGetCount(familyKey, resourceId, out int count)
			|| count <= 0)
		{
			return false;
		}

		return XjFamilyCaiQiWarehouse.TryConsume(familyKey, resourceId, 1);
	}

	private static bool IsDaoZhuActor(Actor actor)
	{
		return actor != null && actor.hasTrait("ChuShen8");
	}

	private static void RetryCaiQiNextYear(Actor actor, int currentYear)
	{
		XjCaiQiActorAccessor.SetNextCaiQiYear(actor, currentYear + 1);
		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Pending);
	}

	private static void ScheduleNextCaiQi(Actor actor, int currentYear)
	{
		int interval = 3 + (int)(((ulong)((BaseSystemData)actor.data).id + (ulong)(long)currentYear) % 3uL);
		XjCaiQiActorAccessor.SetLastAndNextCaiQiYear(actor, currentYear, currentYear + interval);
		XjCaiQiActorAccessor.SetStatus(actor, XjCaiQiStatus.Cooldown);
	}

	private static void ProcessBreakthrough(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		if (!XjCultivationNextRealmResolver.TryGetNextRule(snapshot.RealmId, out XjRealmRule targetRule)
			|| !targetRule.IsImplemented
			|| !CanWriteBreakthroughRealmId(targetRule.RealmId)
			|| targetRule.RequiresFiveXianJi
			|| snapshot.ZhenYuan < targetRule.RequiredZhenYuan)
		{
			return;
		}

		bool needsCaiQiSnapshot = targetRule.RequiresCaiQi
			|| string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
		XjCaiQiSnapshot caiQiSnapshot = needsCaiQiSnapshot
			? XjCaiQiActorAccessor.BuildSnapshot(actor)
			: XjCaiQiSnapshot.Empty;
		XjCultivationRuleCheckResult checkResult = XjCultivationRuleValidator.Check(snapshot, targetRule, caiQiSnapshot);
		if (!checkResult.Passed)
		{
			return;
		}

		XjBreakthroughAttemptResult result = XjBreakthroughRules.Resolve(actor, snapshot, caiQiSnapshot, targetRule.RealmId);
		bool promoted = result.CanPromote
			&& XjCultivationStateTransitions.TrySetRealm(actor, targetRule.RealmId, true);
		XjStageZeroObservation.RecordBreakthroughResult(
			targetRule.RealmId,
			promoted ? result.ReasonCode : (result.CanPromote ? "RealmWriteRejected" : result.ReasonCode),
			promoted);
		if (!promoted)
		{
			return;
		}

		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			targetRule.RealmId,
			currentYear,
			syncVisibleTraits: false,
			restoreHealth: false);

		XjActorCultivationSnapshot postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
		string postDaoTu = postSnapshot.DaoTu;
		if (string.IsNullOrWhiteSpace(postDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(postDaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out postDaoTu);
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
			postDaoTu = postSnapshot.DaoTu;
		}
		// 名称、可见境界与排行榜索引属于境界写入的强一致投影，
		// 必须早于血脉、公告、宗门等非关键后处理。
		XjRealmTitleApplyService.ApplyOnPromotion(actor, targetRule.RealmId, postDaoTu);
		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		XjGongFaProgression.EnsureRealmMinimumGrade(actor, targetRule.RealmId, postDaoTu);

		if (string.Equals(targetRule.RealmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			XjGongFaProgression.EnsureEntryGongFa(actor, postSnapshot);
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			string consumedCaiQiResourceId = XjBreakthroughRules.CommitCaiQiForBreakthrough(actor, caiQiSnapshot);
			if (string.Equals(consumedCaiQiResourceId, "zaqi", StringComparison.Ordinal)
				|| (string.IsNullOrWhiteSpace(consumedCaiQiResourceId) && XjCaiQiActorAccessor.IsCaiQiResultZaQi(actor)))
			{
				XjCaiQiActorAccessor.MarkLianQiByZaQi(actor);
			}
			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out postDaoTu)
				|| string.IsNullOrWhiteSpace(postDaoTu)
				|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(postDaoTu.Trim(), out _))
			{
				XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out postDaoTu);
			}
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
			postDaoTu = postSnapshot.DaoTu;
			XjGongFaProgression.EnsureEntryGongFa(actor, postSnapshot);
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			// 旧档或跨版本角色可能在筑基阶段漏写了首门仙基。紫府公告前
			// 只按真实功法映射补写，失败则不发布带“未知”占位的公告。
			XjZiFuProgression.EnsureZhuJiFoundationXianJi(actor, postDaoTu, currentYear);
			postSnapshot = XjActorCultivationSnapshotBuilder.BuildAnnualProgression(actor, ResolveRealmTier(actor));
			postDaoTu = postSnapshot.DaoTu;
		}

		XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(targetRule.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			XjFamilyMemberIndex.Shared.EnsureHighRealmFamily(actor);
		}
		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.RealmBreakthrough(actor, targetRule.RealmId, postDaoTu));
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(
			actor,
			targetRule.RealmId,
			currentYear,
			applyMingShuReward: false,
			refreshBloodline: false);
		if (string.Equals(targetRule.RealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZhuJi, "ZhuJiPromotion");
		}

		if (string.Equals(targetRule.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjSectCityData.HandleZiFuPromotion(actor, currentYear);
			XjLongShuSystem.NotifyZiFuAppeared(actor);
			if (XjRuntimeSettings.BroadcastHighRealmEnabled
				&& XjAnnouncementText.TryBuildZiFuPromotion(actor, out string promotionText))
			{
				XjBroadcastSystem.BroadcastBLevelActorEvent(
					actor,
					promotionText,
					iconId: XjEventIconCatalog.ZiFuUpgrade);
			}
			XjAutoCollectSystem.TryCollectRealm(actor, XjRealmIds.ZiFu, "ZiFuPromotion");
		}
	}

	private static bool CanWriteBreakthroughRealmId(string targetRealmId)
	{
		return string.Equals(targetRealmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal);
	}

	private static void ProcessZongMen(Actor actor, string realmId, int fromYearInclusive, int currentYear)
	{
		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			return;
		}

		bool isZhenRenLike = XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId);
		bool isJinDanLike = XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		bool canBorrowOrDongTian = string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| isZhenRenLike
			|| isJinDanLike;
		if (!canBorrowOrDongTian)
		{
			return;
		}

		XjSectIdentitySnapshot zongMenState = XjSectIdentityReader.BuildIdentity(actor);
		if (!zongMenState.Found || zongMenState.ZongMenId <= 0L)
		{
			if (isJinDanLike)
			{
				XjSectCityData.HandleJinDanPromotion(actor, currentYear);
			}
			else if (isZhenRenLike)
			{
				XjSectCityData.HandleZiFuPromotion(actor, currentYear);
			}
			zongMenState = XjSectIdentityReader.BuildIdentity(actor);
			if (!zongMenState.Found || zongMenState.ZongMenId <= 0L)
			{
				return;
			}
		}

		if (XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			XjSectCaiQiFaBorrow.TryBorrowForActor(actor, zongMenState);
			XjSectGongFaBorrow.TryBorrowForActor(actor, zongMenState);
		}

		if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
		{
			XjSectDongTianLifecycle.TickAnnual(actor, currentYear);
		}

		// 筑基弟子数量通常远高于高阶修士。仓库回流与乾坤袋快照都需要
		// 读取传承/装备，改为错峰校验；入宗和获得新传承时仍由对应事件入口
		// 立即写入。真人、真君保留每年同步，避免宗门核心资产滞后。
		long actorId = ((BaseSystemData)actor.data).id;
		bool needsWarehouseReconcile = !string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| XjDetectionGate.IsEntityMaintenanceDueBetween(
				XjEntityDetectionJob.SectWarehouseReconcile, actorId, fromYearInclusive, currentYear);
		if (needsWarehouseReconcile)
		{
			XjGongFaWarehouseReconciler.ReconcileActor(actor, currentYear);
			XjQianKunDaiSystem.UpdateState(actor);
		}
	}

}
