using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Localization;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// 帝明阳/仙国法的权威状态层。
///
/// 边界原则：
/// 1) 仙朝档案与独立朝廷才是法统/官位权威；WorldBox Kingdom只提供现实疆土、人口、战争与地图归属；
/// 2) 原生King只作为兼容投影：仙朝成立后，原生王位抖动不得反向销毁帝明阳法统，年度事务负责低频自愈；
/// 3) 国势年度汇总只枚举城市快照并读取 units.Count，绝不遍历全国人口；
/// 4) 仙国假金丹只属于百官制度性持玄；帝明阳本人永远按真实 RealmId/真实战力结算。
/// </summary>
internal static class XjXianGuoSystem
{
	internal static int ActiveDynastyCount => ActiveByKingdomId.Count;
	internal static bool IsActiveDynastyKingdom(long kingdomId)
	{
		return kingdomId > 0L
			&& ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord record)
			&& record != null && record.Active;
	}
	internal const string MingYangDaoTu = "明阳";
	internal const string DiMingYangCoreAuthority = "尊卑法统";
	internal const float NativeCaptureSpeedMultiplier = 0.40f;
	internal const int FakeJinDanMinimumCities = 3;
	internal const int FakeJinDanMinimumPopulation = 600;
	internal const int FakeJinDanMinimumPotential = 6200;
	internal const int FakeJinDanMinimumFortune = 5700;
	internal const int FakeJinDanMinimumDynastyAge = 50;
	internal const int FakeJinDanMinimumGrade = 520;
	internal const int FakeJinDanMaximumGrade = 548;
	// 百官仙国法投影使用独立 trait ID：外观/名称仍显示对应境界，但这些 ID
	// 不进入 XjRealmHelper 的真实境界识别，从根上避免假金丹被误当成正统金丹。
	// 仅用于清理0.9.9.8早期存档遗留；新版本不再注册/显示三枚仙国专属境界特质。
	internal const string InstitutionalZhuJiTraitId = "XjXianGuoZhuJi";
	internal const string InstitutionalZiFuTraitId = "XjXianGuoZiFu";
	internal const string InstitutionalFakeJinDanTraitId = "XjXianGuoFakeJinDan";
	internal const string HeavyMinisterTraitId = "XjXianGuoHeavyMinister";

	// 仙国法的捷径口径：官位先借一国之命，使“持玄命数”跨过大境门槛，
	// 再把对应境界以八成底盘显化。这里的国命永远不写进人物真命。
	internal const int PatronageFateZhuJiThreshold = 1000;
	internal const int PatronageFateZiFuThreshold = 10000;
	internal const int PatronageFateJinDanThreshold = 100000;

	private const int FoundingRetryYears = 10;
	private const int FoundingMinimumCityPopulation = 50;
	private const int SuccessionGraceYears = 12;
	private const int PoliticalPlanRetryYears = 8;
	private const int PoliticalPlanTimeoutYears = 80;
	private const int PoliticalPlanBaseProgressNeeded = 100;
	private const int FakeJinDanExitPotential = 5900;
	private const int FakeJinDanExitFortune = 5400;
	private const int FakeJinDanExitPopulation = 520;
	private const int ImperialRestorationIntervalYears = 3;
	private const int ImperialRestorationChancePercent = 20;

	private sealed class NationAggregate
	{
		internal int CityCount;
		internal int Population;
	}

	private static readonly List<XjXianGuoRecord> Records = new List<XjXianGuoRecord>();
	private static readonly Dictionary<long, XjXianGuoRecord> ByDynastyId = new Dictionary<long, XjXianGuoRecord>();
	private static readonly Dictionary<long, XjXianGuoRecord> ActiveBySovereignId = new Dictionary<long, XjXianGuoRecord>();
	private static readonly Dictionary<long, XjXianGuoRecord> ActiveByKingdomId = new Dictionary<long, XjXianGuoRecord>();
	private static readonly List<XjXianGuoPoliticalPlan> PoliticalPlans = new List<XjXianGuoPoliticalPlan>();
	private static readonly Dictionary<long, XjXianGuoPoliticalPlan> ActivePoliticalPlanByActorId = new Dictionary<long, XjXianGuoPoliticalPlan>();
	private static readonly Dictionary<long, NationAggregate> AnnualNationAggregates = new Dictionary<long, NationAggregate>();
	private static readonly List<XjXianGuoRecord> PendingSuccessionScratch = new List<XjXianGuoRecord>(8);
	private static readonly Queue<long> NativeProjectionRepairQueue = new Queue<long>();
	private static readonly HashSet<long> QueuedNativeProjectionRepairIds = new HashSet<long>();
	private static int _nationAggregateYear = -1;
	private static int _annualWorldPopulation;
	private static long _nextDynastyId = 1L;
	private static long _nextPoliticalPlanId = 1L;
	private static long _activeDiMingYangActorId;
	private static long _restoringDiMingYangActorId;
	private static bool _voluntaryAbdicationInProgress;
	private static long _voluntaryAbdicationKingdomId;
	private static long _voluntaryAbdicationSuccessorActorId;

	internal static bool HasAnnualInterest(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		// 仙国法百官必须保留低频年度校验：一旦离开所属国朝/法统，借来的境界
		// 立即在该维护边界撤销，恢复本人真实 RealmId。
		if (HasInstitutionalPatronageMarker(actor)) return true;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L && ActiveBySovereignId.ContainsKey(actorId)) return true;
		if (actorId > 0L && TryGetActivePoliticalPlan(actorId, out _)) return true;
		if (IsDiMingYang(actor)) return true;
		// 普通明阳不再因为“预判帝明阳”进入年度副队列。只有现实中已经
		// 坐上王位的明阳才需要一次年度兜底，以覆盖旧档加载时没有触发 setKing 的情况。
		return IsMingYang(actor) && !HasAbdicatedImperialIdentity(actor)
			&& (IsNativeSovereign(actor) || IsMingYangPoliticalCandidate(actor));
	}

	internal static bool HasWorldPoliticalWork
	{
		get
		{
			foreach (XjXianGuoRecord record in ActiveByKingdomId.Values)
			{
				if (record != null && record.Active && record.SuccessionPending) return true;
			}
			return false;
		}
	}

	internal static bool ShouldReserveForXianGuo(Actor actor)
	{
		return actor?.data != null && actor.isAlive() && IsDiMingYang(actor);
	}

	internal static bool IsDiMingYang(Actor actor)
	{
		// 帝明阳在在位期间属于唯一人物道统身份。失位/旧国覆亡仍按既有规则
		// 尝试复帝；唯一例外是“证真金后主动禅让”，此时旧帝明确退回普通明阳，
		// 帝明阳唯一席位转给被选中的明阳继承人。
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.DiMingYang, out int value)
			&& value > 0;
	}

	internal static string ResolveDaoTuDisplay(Actor actor, string fallback)
	{
		return IsDiMingYang(actor) ? "帝明阳" : (fallback ?? string.Empty).Trim();
	}

	/// <summary>
	/// 帝明阳登基后的王号。只取现实国家名的核心国号，不把“国/王国/帝国/天朝”重复带入人名。
	/// 金丹以前姓名投影为“X王·本名-境界”；真正证金后改用帝君尊号。
	/// </summary>
	internal static string ResolveRoyalTitle(Actor actor)
	{
		string raw = string.Empty;
		if (actor?.data != null && IsNativeSovereign(actor))
		{
			raw = actor.kingdom?.data?.name ?? string.Empty;
		}
		if (string.IsNullOrWhiteSpace(raw) && actor?.data != null && TryGetLatestDynastyForSovereign(((BaseSystemData)actor.data).id, out XjXianGuoRecord latest))
		{
			raw = latest.DynastyName;
		}
		if (string.IsNullOrWhiteSpace(raw)) raw = actor?.kingdom?.data?.name ?? string.Empty;
		string stem = XjNativeMetaNameSinicizer.ResolveCanonicalKingdomStem(raw);
		if (stem.Length == 0) stem = "明";
		return stem + "王";
	}

	/// <summary>
	/// 仙国反哺并非另一套境界：只把国朝之盛化为帝明阳本人的资质上托。
	/// 反哺只升不降，最高仍受 XjZz6 硬上限约束。
	/// </summary>
	private static void ApplyImperialAptitudeUplift(Actor sovereign, XjXianGuoRecord record)
	{
		if (sovereign?.data == null || record == null || !record.Active || !IsDiMingYang(sovereign)) return;
		int effective = Math.Min(record.NationalPotential, record.NationalFortune);
		int floor = effective >= 8200 ? 6 : effective >= 6200 ? 5 : effective >= 4000 ? 4 : 0;
		if (floor <= 0) return;
		XjActorAccessor.TryGetInt(sovereign, XjActorDataKeys.XjZz, out int current);
		current = Math.Clamp(current, 0, 6);
		if (current >= floor) return;

		XjActorAccessor.SetInt(sovereign, XjActorDataKeys.XjZz, floor);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(sovereign, floor);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(sovereign, floor);
		XjVisibleTraitSync.SyncAptitudeTrait(sovereign, floor);
		XjCultivatorCache.CheckAndUpdate(sovereign);
		XjCombatHotPathCache.Refresh(sovereign);
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// 读取仙国法对帝明阳真实破境的额外助力。强弱取国势与国运中较低者，
	/// 防止“有疆无运”或“空有旧运”单边刷满。这里返回额外成功率，不改基础规则。
	/// </summary>
	internal static float ResolveBreakthroughSuccessBonus(Actor actor, string targetRealmId)
	{
		if (actor?.data == null || !IsDiMingYang(actor) || !TryGetActiveSummary(actor, out XjXianGuoSummary summary)) return 0f;
		return ResolveBreakthroughSuccessBonus(in summary, targetRealmId);
	}

	internal static void ObserveDaoTu(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null || !string.Equals((daoTu ?? string.Empty).Trim(), MingYangDaoTu, StringComparison.Ordinal)) return;
		// 已经在位的国王后来定入明阳，同样应立即触发“求帝而明阳”的帝统变化。
		if (IsNativeSovereign(actor))
		{
			TryAwakenDiMingYangFromKingship(actor, actor.kingdom, Math.Max(1, currentYear), out _);
		}
	}

	internal static void TickAnnualActor(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive()) return;
		int year = Math.Max(1, annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		if (HasInstitutionalPatronageMarker(actor))
		{
			ReconcileInstitutionalPatronage(actor, year, syncVisibleTraits: true);
		}

		// 唯一帝明阳的席位属于人物，而不是某一朝某一国。失位与法统断绝只会
		// 结束当朝仙国记录，不会释放帝明阳身份；因此复帝期间同样阻止第二位
		// 明阳因登基再生帝明阳。
		if (IsDiMingYang(actor))
		{
			if (_activeDiMingYangActorId <= 0L) _activeDiMingYangActorId = actorId;
			else if (_activeDiMingYangActorId != actorId)
			{
				// 只用于旧档多帝修复；正常游戏不会把已经成立的帝明阳主动降回明阳。
				if (TryGetActiveRecord(actorId, out XjXianGuoRecord duplicateRecord))
					EndDynasty(actor, duplicateRecord, year, "帝明阳位已有主");
				ClearDiMingYang(actor);
				return;
			}

			ReconcileImperialShenDanConflict(actor);
			EnsureImperialDaoTuIdentity(actor);
			EnsureImperialNonIntercalaryXianJi(actor, year);
			XjDiMingYangCityLordUpliftSystem.EnsureBaseline(actor);
			// 证得真实金丹后的帝明阳若已有合格明阳直系继承人，则主动禅位。
			// 年度兜底同时覆盖“证金当年尚无明阳后嗣，后续后嗣才成明阳”的情况。
			if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan
				&& TryVoluntaryAbdicationAfterRealJinDan(actor, year, out _))
			{
				return;
			}

			if (!IsNativeSovereign(actor))
			{
				// 仙朝成立后，王朝档案才是法统权威。原生King被叛乱AI/第三方模组
				// 短暂改写只视作兼容投影损坏，由年度Refresh低频修回；不能再反向
				// 让帝明阳“抛弃国家”并整朝退场。只有旧国实体真正覆亡才结束法统。
				if (TryGetActiveRecord(actorId, out XjXianGuoRecord institutionalRecord)
					&& institutionalRecord != null && institutionalRecord.Active)
				{
					RefreshDynasty(actor, institutionalRecord, year);
					return;
				}
				TryRestoreImperialKingship(actor, year);
				return;
			}
		}

		// 主动禅让是一次不可逆的政治退场。旧帝仍保留普通明阳修行身份，但不再
		// 参与“谋国/谋逆/复帝”链；旧档若残留政治计划，在这里一次性静默收束。
		if (!IsDiMingYang(actor) && HasAbdicatedImperialIdentity(actor))
		{
			if (TryGetActivePoliticalPlan(actorId, out XjXianGuoPoliticalPlan retiredPlan))
			{
				FailPoliticalPlan(retiredPlan, year, "已禅帝统，退居不争");
			}
			return;
		}

		// 普通明阳只要现实中已经成为国王，就在这一刻转为唯一帝明阳。
		if (!IsDiMingYang(actor) && IsMingYang(actor) && IsNativeSovereign(actor))
		{
			TryAwakenDiMingYangFromKingship(actor, actor.kingdom, year, out _);
		}

		if (!IsDiMingYang(actor) && TryGetActivePoliticalPlan(actorId, out XjXianGuoPoliticalPlan activePlan))
		{
			if (!IsMingYang(actor))
			{
				FailPoliticalPlan(activePlan, year, "已失明阳根性，谋国之机自散");
				return;
			}
			if (!IsMingYangPoliticalRealmEligible(actor))
			{
				FailPoliticalPlan(activePlan, year, "尚止胎息，未入修行，不得以明阳之名发动谋国");
				return;
			}
			AdvancePoliticalPlan(actor, activePlan, year, out _);
			return;
		}
		if (!IsDiMingYang(actor) && IsMingYangPoliticalCandidate(actor))
		{
			TryStartMingYangPoliticalPlan(actor, year);
			return;
		}
		if (!IsDiMingYang(actor)) return;

		if (!TryGetActiveRecord(actorId, out XjXianGuoRecord record))
		{
			TryEstablishXianGuo(actor, year, out record);
		}
		if (record == null || !record.Active) return;
		RefreshDynasty(actor, record, year);
	}

	internal static void OnActorDeath(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L) return;
		int year = Math.Max(1, snapshot.Year);
		if (_activeDiMingYangActorId == snapshot.ActorId) _activeDiMingYangActorId = 0L;
		if (ActivePoliticalPlanByActorId.TryGetValue(snapshot.ActorId, out XjXianGuoPoliticalPlan deadPlan)
			&& deadPlan != null && deadPlan.Active)
		{
			FailPoliticalPlan(deadPlan, year, "谋主身死，谋划自然中止");
		}
		if (!TryGetActiveRecord(snapshot.ActorId, out XjXianGuoRecord record)) return;

		long previousSovereignId = record.SovereignActorId;
		record.PreviousSovereignActorId = previousSovereignId;
		record.PreviousSovereignName = string.IsNullOrWhiteSpace(record.SovereignName) ? snapshot.Name : record.SovereignName;
		record.LastKillerActorId = snapshot.LastAttackerId;
		record.LastKillerName = snapshot.LastAttackerName ?? string.Empty;
		record.SuccessionPending = true;
		record.SuccessionStartedYear = year;
		record.LastPoliticalEventYear = year;
		record.Status = "国主身死，王统待定";
		record.FakeJinDanActive = false;
		record.BorrowedCombatGrade = 0;
		record.SovereignActorId = 0L;
		record.SovereignName = string.Empty;
		ActiveBySovereignId.Remove(previousSovereignId);

		Actor killer = null;
		bool childKilledParent = snapshot.LastAttackerId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(snapshot.LastAttackerId, out killer)
			&& killer?.data != null
			&& IsChildOf(killer, previousSovereignId);
		if (childKilledParent)
		{
			record.FuZiXiangSha = Math.Clamp(record.FuZiXiangSha + 55, 0, 100);
			record.MouNi = Math.Clamp(record.MouNi + 25, 0, 100);
			record.ZiYan = Math.Clamp(record.ZiYan + 25, 0, 100);
			record.JunChen = Math.Max(0, record.JunChen - 30);
			record.NationalFortune = Math.Max(0, (int)Math.Round(record.NationalFortune * 0.78d));
		}

		if (snapshot.LastAttackerId > 0L
			&& ActivePoliticalPlanByActorId.TryGetValue(snapshot.LastAttackerId, out XjXianGuoPoliticalPlan plan)
			&& plan != null && plan.Active && plan.TargetSovereignActorId == previousSovereignId)
		{
			plan.ViolentClaim = true;
			plan.TargetIsParent = plan.TargetIsParent || childKilledParent;
			plan.Progress = Math.Max(plan.Progress, plan.ProgressNeeded);
			plan.LastProgressYear = year;
			plan.Status = "violent_claim";
		}

		XjWorldArchiveSystem.MarkChanged();
		string body = "【仙国王统】" + record.PreviousSovereignName + "身死，" + record.DynastyName
			+ "暂入王统待定；诸城百官静候新君，仙国法统亦随王位而悬。";
		if (childKilledParent)
		{
			body = "【父子相杀】" + (snapshot.LastAttackerName ?? "其子") + "弑" + record.PreviousSovereignName
				+ "，帝明阳父子相杀、谋逆之象骤盛；王位归属仍待天下争定。";
		}
		RecordPoliticalHistory(childKilledParent ? "父子相杀" : "仙国王统待定", body, year, snapshot.LastAttackerId, snapshot.LastAttackerName, previousSovereignId, record.PreviousSovereignName);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: childKilledParent ? "#D98A67" : "#D9B36C");
	}

	/// <summary>
	/// 仙国法百官是否留有制度性承命标记。0.9.8.27 起，即使人物真境已经高于
	/// 当前官位可借境界，只要仍在仙朝任官，国命本身仍然存在；因此不能再用
	/// “借境Tier>=筑基”作为是否持玄的唯一标记。
	/// </summary>
	internal static bool HasInstitutionalPatronageMarker(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XianGuoPatronageDynastyId, out long dynastyId)
			&& dynastyId > 0L;
	}

	internal static int ResolveTrueMingShu(Actor actor)
	{
		if (actor?.data == null) return 0;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuAcquired, out float acquired);
		float total = Math.Max(0f, (float)Math.Floor(congenital) + (float)Math.Floor(acquired));
		// 兼容尚未拆分先天/后天命数的旧档：只读旧总命数作真命回退，绝不把国命写回。
		if (total <= 0f && XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float legacyTotal))
			total = Math.Max(0f, (float)Math.Floor(legacyTotal));
		return total >= int.MaxValue ? int.MaxValue : (int)total;
	}

	internal static int ResolveInstitutionalTierFromEffectiveFate(int effectiveFate)
	{
		effectiveFate = Math.Max(0, effectiveFate);
		if (effectiveFate >= PatronageFateJinDanThreshold) return XjRealmSuppression.TierJinDan;
		if (effectiveFate >= PatronageFateZiFuThreshold) return XjRealmSuppression.TierZiFu;
		if (effectiveFate >= PatronageFateZhuJiThreshold) return XjRealmSuppression.TierZhuJi;
		return XjRealmSuppression.TierLianQi;
	}

	internal static int ResolveFateThresholdForTier(int tier)
	{
		return tier switch
		{
			XjRealmSuppression.TierJinDan => PatronageFateJinDanThreshold,
			XjRealmSuppression.TierZiFu => PatronageFateZiFuThreshold,
			XjRealmSuppression.TierZhuJi => PatronageFateZhuJiThreshold,
			_ => 0
		};
	}

	private static bool TryResolveActiveInstitutionalMandate(
		Actor actor,
		out XjXianGuoRecord record,
		out XjXianGuoCourtOfficeRecord office,
		out int trueFate,
		out int nationalFate,
		out int effectiveFate)
	{
		record = null;
		office = null;
		trueFate = 0;
		nationalFate = 0;
		effectiveFate = 0;
		if (actor?.data == null || !actor.isAlive() || IsDiMingYang(actor)) return false;
		if (!XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XianGuoPatronageDynastyId, out long dynastyId) || dynastyId <= 0L) return false;
		if (!XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XianGuoPatronageKingdomId, out long kingdomId) || kingdomId <= 0L) return false;
		if (!ActiveByKingdomId.TryGetValue(kingdomId, out record)
			|| record == null || !record.Active || record.DynastyId != dynastyId) return false;
		// 官身承命必须仍系于本朝。人物真实地理归属一旦离朝，国命立即失去承载。
		if (actor.kingdom?.data?.id != kingdomId) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjXianGuoCourtSystem.TryGetOfficer(actorId, out office)
			|| office == null || !office.Active || office.DynastyId != dynastyId || office.KingdomId != kingdomId) return false;
		trueFate = ResolveTrueMingShu(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageNationalFate, out nationalFate);
		nationalFate = Math.Max(0, nationalFate);
		long combined = (long)trueFate + nationalFate;
		effectiveFate = combined >= int.MaxValue ? int.MaxValue : (int)combined;
		return true;
	}

	private static bool TryResolveActiveInstitutionalPatronage(
		Actor actor,
		out XjXianGuoRecord record,
		out int tier,
		out int grade)
	{
		record = null;
		tier = 0;
		grade = 0;
		if (!TryResolveActiveInstitutionalMandate(actor, out record, out _, out _, out _, out _)) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, out tier)
			|| tier < XjRealmSuppression.TierZhuJi
			|| tier > XjRealmSuppression.TierJinDan) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, out grade);
		return true;
	}

	internal static bool TryGetCourtMandateSummary(
		Actor actor,
		out int trueFate,
		out int nationalFate,
		out int effectiveFate,
		out bool heavyMinister)
	{
		trueFate = 0;
		nationalFate = 0;
		effectiveFate = 0;
		heavyMinister = false;
		if (!TryResolveActiveInstitutionalMandate(actor, out _, out XjXianGuoCourtOfficeRecord office,
			out trueFate, out nationalFate, out effectiveFate)) return false;
		heavyMinister = XjXianGuoCourtSystem.IsHeavyMinisterOffice(office);
		return true;
	}

	internal static bool TryGetInstitutionalProjectionTier(Actor actor, out int tier)
	{
		tier = 0;
		if (!TryResolveActiveInstitutionalPatronage(actor, out _, out int projectedTier, out _)) return false;
		tier = projectedTier;
		return true;
	}

	internal static bool HasActiveInstitutionalCultivation(Actor actor)
	{
		return TryResolveActiveInstitutionalPatronage(actor, out _, out _, out _);
	}

	/// <summary>
	/// 排行榜只展示仙国官身当前实际借到的持玄层级，不把借境写回真实 RealmId。
	/// 帝明阳与未借境官员继续显示自身真实境界。
	/// </summary>
	internal static bool TryGetCourtRankRealmDisplay(Actor actor, out string display)
	{
		display = string.Empty;
		if (!TryResolveActiveInstitutionalPatronage(actor, out _, out int tier, out _)) return false;
		display = tier switch
		{
			XjRealmSuppression.TierJinDan => "仙国假金丹",
			XjRealmSuppression.TierZiFu => "持玄紫府",
			XjRealmSuppression.TierZhuJi => "持玄筑基",
			_ => string.Empty
		};
		return !string.IsNullOrWhiteSpace(display);
	}

	/// <summary>帝明阳真实大境界，只读取本体境界，不读取百官/自身持玄投影。</summary>
	internal static int ResolveImperialSovereignTier(Actor sovereign)
	{
		if (sovereign?.data == null || !sovereign.isAlive()) return XjRealmSuppression.TierNone;
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(sovereign, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return XjRealmSuppression.TierDaoTai;
		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		return XjRealmSuppression.GetRealmTierFromIdForRuntime(realmId);
	}


	internal static int ResolveInstitutionalEffectiveTier(Actor actor, int realTier)
	{
		int effective = Math.Max(XjRealmSuppression.TierNone, realTier);
		if (TryGetInstitutionalProjectionTier(actor, out int projectedTier) && projectedTier > effective)
		{
			effective = projectedTier;
		}
		return effective;
	}

	/// <summary>
	/// 仙国法借境是朝廷官位的派生结果，不能通过手动添加可见境界特质凭空获得。
	/// 这里仅兼容旧调用：若人物本就由朝廷权威得到相同投影则视作成功，否则拒绝。
	/// </summary>
	internal static bool TryGrantInstitutionalProjection(Actor actor, int targetTier, int currentYear, string source)
	{
		_ = currentYear;
		_ = source;
		if (actor?.data == null || !actor.isAlive()) return false;
		return TryGetInstitutionalProjectionTier(actor, out int authoritativeTier)
			&& authoritativeTier == targetTier;
	}

	/// <summary>
	/// 朝廷持玄的权威写口：官位先授“国命”，再由本命+国命得到持玄命数，
	/// 只有持玄命数跨过门槛时才显化借境。即使人物真境已高、无需借境，
	/// 官身与国命仍然保留；卸任/离朝/法统终结才一并归还。
	/// </summary>
	internal static void ApplyCourtInstitutionalProjection(
		Actor actor,
		XjXianGuoRecord record,
		int nationalFate,
		int targetTier,
		int currentYear,
		string officeName)
	{
		_ = currentYear;
		_ = officeName;
		if (actor?.data == null || !actor.isAlive() || record == null || !record.Active) return;
		// 帝明阳是国朝权柄的授予者，不是承受官命的【国之重臣】。旧档若曾把
		// 帝君错误塞进中枢官位，这里直接清除百官承命投影，避免“帝君给自己授官”。
		if (IsDiMingYang(actor))
		{
			ClearInstitutionalProjection(actor, syncVisibleTraits: true);
			return;
		}
		if (!(XjCultivationPathRules.IsZiFuJinDan(actor) || XjCultivationPathRules.IsFuQiYangXing(actor))) return;

		nationalFate = Math.Max(0, nationalFate);
		int realTier = XjRealmSuppression.GetRealmTier(actor);
		if (targetTier <= realTier || targetTier < XjRealmSuppression.TierZhuJi) targetTier = XjRealmSuppression.TierNone;
		else targetTier = Math.Clamp(targetTier, XjRealmSuppression.TierZhuJi, XjRealmSuppression.TierJinDan);
		int grade = targetTier >= XjRealmSuppression.TierJinDan
			? Math.Clamp(ResolveCourtBorrowedCombatGrade(record), FakeJinDanMinimumGrade, FakeJinDanMaximumGrade)
			: 0;

		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XianGuoPatronageDynastyId, out long currentDynastyId);
		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XianGuoPatronageKingdomId, out long currentKingdomId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, out int currentTier);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, out int currentGrade);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageNationalFate, out int currentNationalFate);
		if (currentDynastyId == record.DynastyId && currentKingdomId == record.KingdomId
			&& currentTier == targetTier && currentGrade == grade && currentNationalFate == nationalFate)
		{
			return;
		}

		XjActorAccessor.SetLong(actor, XjActorDataKeys.XianGuoPatronageDynastyId, record.DynastyId);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XianGuoPatronageKingdomId, record.KingdomId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageNationalFate, nationalFate);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, targetTier);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, grade);
		// 持玄官需要进入运行期属性索引，才能在原生 updateStats 中获得借境底盘；
		// 这也让普通原生人口不再为仙国制度支付每次属性重建的查询成本。
		XjRuntimeActorInterestIndex.Observe(actor);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		actor.setStatsDirty();
		XjCombatHotPathCache.Refresh(actor);
	}

	internal const float InstitutionalBorrowedStatScalar = 0.80f;

	/// <summary>
	/// 百官“持玄借境”的权威属性投影。所借境界不是一枚空标签：在一次真实的
	/// Actor.updateStats 重建中，以目标境界标准面板的八成建立全套基础数值下限。
	/// 这里只建立 floor，不覆盖人物本来已经更高的属性；功法、法宝、纪元、小境界等
	/// 后续乘区继续正常作用。因此一个炼气借紫府不会仍拿炼气身板，也不会因为
	/// 同时保留真实境界 trait 而把“炼气 + 0.8紫府”错误叠成超过八成的底盘。
	/// </summary>
	internal static void ApplyInstitutionalBorrowedRealmStatFloor(Actor actor)
	{
		if (actor?.data == null || actor.stats == null || !actor.isAlive()) return;
		if (!TryResolveActiveInstitutionalPatronage(actor, out _, out int projectedTier, out _)) return;
		int realTier = XjRealmSuppression.GetRealmTier(actor);
		if (projectedTier <= realTier) return;

		switch (projectedTier)
		{
			case XjRealmSuppression.TierZhuJi:
				ApplyBorrowedRealmBaseline(actor, lifespan: 280f, resist: 128f, warfare: 50f, damage: 1000f,
					mass: 140f, health: 10000f, speed: 30f, area: 4f, targets: 12f, accuracy: 60f,
					multiplierSpeed: 1.2f, stamina: 400f, range: 6f, attackSpeed: 6f, scale: 0.08f,
					multiplierHealth: 2f, multiplierDamage: 2f);
				break;
			case XjRealmSuppression.TierZiFu:
				ApplyBorrowedRealmBaseline(actor, lifespan: 500f, resist: 256f, warfare: 100f, damage: 10000f,
					mass: 180f, health: 100000f, speed: 40f, area: 5f, targets: 16f, accuracy: 80f,
					multiplierSpeed: 1.5f, stamina: 500f, range: 8f, attackSpeed: 8f, scale: 0.1f,
					multiplierHealth: 3f, multiplierDamage: 3f);
				break;
			case XjRealmSuppression.TierJinDan:
				// 正统金丹标准寿限取凡俗80 + 金丹基础3000；假金丹仍只借八成数值，
				// 不获得真实金丹道行、果位、金性或证金事务。
				ApplyBorrowedRealmBaseline(actor, lifespan: 3080f, resist: 512f, warfare: 200f, damage: 100000f,
					mass: 360f, health: 1000000f, speed: 80f, area: 30f, targets: 96f, accuracy: 160f,
					multiplierSpeed: 2f, stamina: 1000f, range: 48f, attackSpeed: 10f, scale: 0.1f,
					multiplierHealth: 5f, multiplierDamage: 5f);
				break;
		}
	}

	private static void ApplyBorrowedRealmBaseline(
		Actor actor, float lifespan, float resist, float warfare, float damage, float mass, float health,
		float speed, float area, float targets, float accuracy, float multiplierSpeed, float stamina,
		float range, float attackSpeed, float scale, float multiplierHealth, float multiplierDamage)
	{
		float k = InstitutionalBorrowedStatScalar;
		RaiseBorrowedStatFloor(actor, "lifespan", lifespan * k);
		RaiseBorrowedStatFloor(actor, "Resist", resist * k);
		RaiseBorrowedStatFloor(actor, "warfare", warfare * k);
		RaiseBorrowedStatFloor(actor, "damage", damage * k);
		RaiseBorrowedStatFloor(actor, "mass", mass * k);
		RaiseBorrowedStatFloor(actor, "health", health * k);
		RaiseBorrowedStatFloor(actor, "speed", speed * k);
		RaiseBorrowedStatFloor(actor, "area_of_effect", area * k);
		RaiseBorrowedStatFloor(actor, "targets", targets * k);
		RaiseBorrowedStatFloor(actor, "accuracy", accuracy * k);
		RaiseBorrowedStatFloor(actor, "multiplier_speed", multiplierSpeed * k);
		RaiseBorrowedStatFloor(actor, "stamina", stamina * k);
		RaiseBorrowedStatFloor(actor, "range", range * k);
		RaiseBorrowedStatFloor(actor, "attack_speed", attackSpeed * k);
		RaiseBorrowedStatFloor(actor, "scale", scale * k);
		RaiseBorrowedStatFloor(actor, "multiplier_health", multiplierHealth * k);
		RaiseBorrowedStatFloor(actor, "multiplier_damage", multiplierDamage * k);
		// 旧逻辑中借境提高最终 Resist 会同步提高抗击退；现在把同一结果
		// 投影到玄鉴受力缓存，并由原生 addForce 边界消费，避免只剩“少僵直但仍被震飞”。
		XjKnockbackGuard.ProjectFinalResistToForceGuard(actor);
	}

	private static void RaiseBorrowedStatFloor(Actor actor, string statId, float floor)
	{
		if (actor?.stats == null || string.IsNullOrWhiteSpace(statId) || floor <= 0f) return;
		float current = 0f;
		try { current = actor.stats[statId]; } catch { current = 0f; }
		if (current < floor) actor.stats[statId] = floor;
	}

	internal static void ClearCourtInstitutionalProjection(Actor actor)
	{
		ClearInstitutionalProjection(actor, syncVisibleTraits: true);
	}

	internal static void ReconcileInstitutionalPatronage(Actor actor, int currentYear, bool syncVisibleTraits)
	{
		_ = currentYear;
		if (!HasInstitutionalPatronageMarker(actor)) return;
		if (!TryResolveActiveInstitutionalMandate(actor, out XjXianGuoRecord record, out _, out _, out _, out _))
		{
			ClearInstitutionalProjection(actor, syncVisibleTraits);
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, out int tier);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, out int storedGrade);
		int grade = tier >= XjRealmSuppression.TierJinDan
			? Math.Clamp(ResolveCourtBorrowedCombatGrade(record), FakeJinDanMinimumGrade, FakeJinDanMaximumGrade)
			: 0;
		if (storedGrade != grade)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, grade);
			XjCombatHotPathCache.Refresh(actor);
		}
		if (syncVisibleTraits) XjVisibleTraitSync.SyncCultivationTraits(actor);
	}

	internal static void OnActorNativeKingdomChanged(Actor actor)
	{
		if (!HasInstitutionalPatronageMarker(actor)) return;
		ReconcileInstitutionalPatronage(actor, XjYearTracker.CurrentYear, syncVisibleTraits: true);
	}

	private static void ClearInstitutionalProjection(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XianGuoPatronageDynastyId, 0L);
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XianGuoPatronageKingdomId, 0L);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoPatronageNationalFate, 0);
		XjRuntimeActorInterestIndex.Observe(actor);
		if (syncVisibleTraits) XjVisibleTraitSync.SyncCultivationTraits(actor);
		actor.setStatsDirty();
		XjCombatHotPathCache.Refresh(actor);
	}

	/// <summary>
	/// 【国之重臣】只是仙朝中枢高官的身份象征，不携带静态战斗数值。
	/// 真正力量来自官位所承国命；人物离开中枢官位后该身份立即撤去。
	/// </summary>
	internal static void SyncCourtIdentityTrait(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		bool shouldHave = actorId > 0L
			&& !IsDiMingYang(actor)
			&& XjXianGuoCourtSystem.TryGetOfficer(actorId, out XjXianGuoCourtOfficeRecord office)
			&& office != null && office.Active && XjXianGuoCourtSystem.IsHeavyMinisterOffice(office);
		bool has = actor.hasTrait(HeavyMinisterTraitId);
		if (shouldHave == has) return;
		if (shouldHave)
		{
			if (AssetManager.traits?.get(HeavyMinisterTraitId) != null) actor.addTrait(HeavyMinisterTraitId, false);
		}
		else
		{
			actor.removeTrait(HeavyMinisterTraitId);
		}
	}

	/// <summary>
	/// Returns only the borrowed combat grade. It never changes the real cultivation tier.
	/// </summary>
	internal static bool TryGetBorrowedCombatGrade(Actor actor, out int grade)
	{
		grade = 0;
		if (actor?.data == null || !actor.isAlive()) return false;

		// 百官仙国假金丹：真实 RealmId 可低于紫府，假丹底盘由独立投影 trait 提供；
		// 品秩完全复用原先 520~548 的仙国假金丹模型。
		if (TryResolveActiveInstitutionalPatronage(actor, out XjXianGuoRecord patronageRecord, out int patronageTier, out int patronageGrade)
			&& patronageTier >= XjRealmSuppression.TierJinDan)
		{
			int realTier = XjRealmSuppression.GetRealmTier(actor);
			if (realTier >= XjRealmSuppression.TierJinDan) return false;
			grade = patronageGrade > 0 ? patronageGrade : ResolveCourtBorrowedCombatGrade(patronageRecord);
			grade = Math.Clamp(grade, FakeJinDanMinimumGrade, FakeJinDanMaximumGrade);
			return grade > 0;
		}

		// 帝明阳本人不再存在任何“仙国假金丹”战斗位格。只有百官制度性持玄
		// 可以从上面的 patronage 分支获得假丹；帝君永远使用真实境界与真实战力。
		return false;
	}

	internal static int ResolveEffectiveCombatTier(Actor actor, int realTier)
	{
		int effective = ResolveInstitutionalEffectiveTier(actor, realTier);
		if (effective < XjRealmSuppression.TierJinDan && TryGetBorrowedCombatGrade(actor, out _))
		{
			effective = XjRealmSuppression.TierJinDan;
		}
		return effective;
	}

	internal static float GetBorrowedOutgoingDamageMultiplier(Actor actor)
	{
		if (!TryGetBorrowedCombatGrade(actor, out int grade)) return 1f;
		float progress = Math.Clamp((grade - FakeJinDanMinimumGrade) / (float)Math.Max(1, FakeJinDanMaximumGrade - FakeJinDanMinimumGrade), 0f, 1f);
		return 1.55f + progress * 0.28f;
	}

	internal static float GetBorrowedIncomingDamageMultiplier(Actor actor)
	{
		if (!TryGetBorrowedCombatGrade(actor, out int grade)) return 1f;
		float progress = Math.Clamp((grade - FakeJinDanMinimumGrade) / (float)Math.Max(1, FakeJinDanMaximumGrade - FakeJinDanMinimumGrade), 0f, 1f);
		return 0.68f - progress * 0.08f;
	}

	internal static bool TryGetActiveSummary(Actor actor, out XjXianGuoSummary summary)
	{
		summary = default;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!TryGetActiveRecord(actorId, out XjXianGuoRecord record) || record == null) return false;
		summary = BuildSummary(record);
		return true;
	}

	internal static bool TryGetActiveSummaryByKingdomId(long kingdomId, out XjXianGuoSummary summary)
	{
		summary = default;
		if (kingdomId <= 0L || !ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord record)
			|| record == null || !record.Active) return false;
		summary = BuildSummary(record);
		return true;
	}

	private static XjXianGuoSummary BuildSummary(XjXianGuoRecord record)
	{
		return new XjXianGuoSummary(
			record.DynastyId, record.DynastyName, record.KingdomId, record.CapitalCityId,
			record.SovereignActorId, record.SovereignName, record.FoundedYear, record.StableYears,
			record.DynastyGeneration, record.Status, record.SuccessionPending, record.SuccessionStartedYear,
			record.CityCount, record.Population, record.NationalPotential, record.NationalFortune,
			record.CourtFakeJinDanActive, record.CourtBorrowedCombatGrade, record.TianGuang, record.ZiYan,
			record.JunChen, record.DiHuang, record.FuZiXiangSha, record.MouNi);
	}

	/// <summary>
	/// 玄鉴界面使用的当前仙国法统快照。只枚举稀疏的 ActiveByKingdomId，
	/// 不扫描国家、城市或人口；调用方可以安全地在百科/舆图打开时读取。
	/// </summary>
	internal static IReadOnlyList<XjXianGuoSummary> ReadActiveSummaries()
	{
		// 仙鉴打开时再做一次极窄的王统追赶：只处理“现实国王已经是帝明阳”
		// 的待定档案，避免载入阶段索引尚未就绪时把旧档的王统待定一直显示到来年。
		TryResolveImportedPendingSovereignty();
		if (ActiveByKingdomId.Count == 0) return Array.Empty<XjXianGuoSummary>();
		List<XjXianGuoSummary> result = new List<XjXianGuoSummary>(ActiveByKingdomId.Count);
		foreach (XjXianGuoRecord record in ActiveByKingdomId.Values)
		{
			if (record == null || !record.Active) continue;
			result.Add(BuildSummary(record));
		}
		result.Sort((left, right) => left.DynastyId.CompareTo(right.DynastyId));
		return result;
	}

	/// <summary>
	/// 只在仙国法统页打开时读取仙朝官位权威档案，展示当前百官的真实境界、
	/// 玄秩与持玄投影。官位不再取自原生城主，因此这里不扫描城内居民。
	/// </summary>
	internal static IReadOnlyList<XjXianGuoOfficialSummary> ReadCurrentOfficialSummaries(long kingdomId)
	{
		if (kingdomId <= 0L || !ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord active)
			|| active == null || !active.Active) return Array.Empty<XjXianGuoOfficialSummary>();

		IReadOnlyList<XjXianGuoCourtOfficeRecord> offices = XjXianGuoCourtSystem.ReadActiveOffices(active.DynastyId);
		if (offices == null || offices.Count == 0) return Array.Empty<XjXianGuoOfficialSummary>();
		List<XjXianGuoOfficialSummary> result = new List<XjXianGuoOfficialSummary>(offices.Count);
		for (int i = 0; i < offices.Count; i++)
		{
			XjXianGuoCourtOfficeRecord office = offices[i];
			if (office == null || !office.Active) continue;

			string actorName = string.IsNullOrWhiteSpace(office.ActorName) ? "虚位待补" : office.ActorName.Trim();
			string realRealm = "未任";
			string projection = office.ActorId > 0L ? "在任未借境" : "虚位";
			int grade = 0;
			int trueFate = 0;
			int nationalFate = 0;
			int effectiveFate = 0;
			bool heavyMinister = XjXianGuoCourtSystem.IsHeavyMinisterOffice(office);
			if (office.ActorId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(office.ActorId, out Actor actor)
				&& actor?.data != null && actor.isAlive())
			{
				realRealm = XjRealmHelper.GetDisplayName(
					XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
				if (string.IsNullOrWhiteSpace(realRealm)) realRealm = "凡俗";
				if (TryGetCourtMandateSummary(actor, out trueFate, out nationalFate, out effectiveFate, out bool resolvedHeavy))
					heavyMinister = resolvedHeavy;
				else
				{
					trueFate = ResolveTrueMingShu(actor);
					effectiveFate = trueFate;
				}
				if (TryResolveActiveInstitutionalPatronage(actor, out XjXianGuoRecord patronage,
					out int projectedTier, out int projectedGrade) && ReferenceEquals(patronage, active))
				{
					projection = projectedTier switch
					{
						XjRealmSuppression.TierZhuJi => "持玄筑基",
						XjRealmSuppression.TierZiFu => "持玄紫府",
						XjRealmSuppression.TierJinDan => "仙国假金丹",
						_ => "在任未借境"
					};
					grade = projectedTier >= XjRealmSuppression.TierJinDan ? projectedGrade : 0;
				}
			}
			result.Add(new XjXianGuoOfficialSummary(
				office.CityId, office.CityName, office.ActorId, actorName, realRealm, projection, grade,
				office.OfficeName, XjXianGuoCourtSystem.GetRankDisplay(office.Rank), office.Rank,
				trueFate, nationalFate, effectiveFate, heavyMinister));
		}
		return result;
	}


	internal static float ResolveBreakthroughSuccessBonus(in XjXianGuoSummary summary, string targetRealmId)
	{
		int effective = Math.Min(summary.NationalPotential, summary.NationalFortune);
		float strength = Math.Clamp(effective / 10000f, 0f, 1f);
		float maximum = targetRealmId switch
		{
			var id when string.Equals(id, XjRealmIds.LianQi, StringComparison.Ordinal) => 0.20f,
			var id when string.Equals(id, XjRealmIds.ZhuJi, StringComparison.Ordinal) => 0.12f,
			var id when string.Equals(id, XjRealmIds.ZiFu, StringComparison.Ordinal) => 0.06f,
			var id when string.Equals(id, XjRealmIds.JinDan, StringComparison.Ordinal) => 0.02f,
			_ => 0f
		};
		return Math.Max(0f, maximum * strength);
	}


	/// <summary>
	/// 人物玄鉴照录中的仙国法专栏。帝明阳、谋国中的明阳，以及真正持有
	/// 百官借玄投影的人都能看到自身与哪一朝法统相连；普通无关人物保持空白。
	/// </summary>
	internal static string BuildActorStatusSummary(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return string.Empty;

		if (TryGetActiveRecord(actorId, out XjXianGuoRecord sovereignRecord)
			&& sovereignRecord != null && sovereignRecord.Active)
		{
			XjXianGuoSummary summary = BuildSummary(sovereignRecord);
			int effective = Math.Min(summary.NationalPotential, summary.NationalFortune);
			string status = string.IsNullOrWhiteSpace(summary.Status) ? "仙国行法" : summary.Status.Trim();
			return "身份：帝明阳　" + summary.DynastyName + "　" + status
				+ "\n国玄：国势 " + summary.NationalPotential + " / 国运 " + summary.NationalFortune + " / 有效 " + effective
				+ "\n国朝：" + summary.CityCount + "城　" + summary.Population + "众　立朝" + Math.Max(0, XjYearTracker.CurrentYear - summary.FoundedYear) + "年"
				+ "\n六象：天光" + summary.TianGuang + "　紫焰" + summary.ZiYan + "　君臣" + summary.JunChen + "　帝皇" + summary.DiHuang
				+ ((summary.FuZiXiangSha > 0 || summary.MouNi > 0)
					? "\n政象：父子相杀" + summary.FuZiXiangSha + "　谋逆" + summary.MouNi : string.Empty);
		}

		if (TryResolveActiveInstitutionalMandate(actor, out XjXianGuoRecord patronageRecord,
			out XjXianGuoCourtOfficeRecord office, out int trueFate, out int nationalFate, out int effectiveFate))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageRealmTier, out int tier);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoPatronageGrade, out int grade);
			string borrowed = tier switch
			{
				XjRealmSuppression.TierZhuJi => "持玄筑基",
				XjRealmSuppression.TierZiFu => "持玄紫府",
				XjRealmSuppression.TierJinDan => "仙国假金丹",
				_ => "未借境"
			};
			string gradeText = tier >= XjRealmSuppression.TierJinDan && grade > 0 ? "　品秩" + grade : string.Empty;
			bool heavy = XjXianGuoCourtSystem.IsHeavyMinisterOffice(office);
			string identity = heavy ? "国之重臣" : "仙朝持玄官";
			return "身份：" + identity + "　系于" + patronageRecord.DynastyName
				+ "\n官职：" + office.OfficeName + "　" + XjXianGuoCourtSystem.GetRankDisplay(office.Rank)
				+ "\n本命：" + trueFate + "　承国之命：+" + nationalFate + "　持玄命数：" + effectiveFate
				+ "\n仙国道行：" + borrowed + gradeText + (tier > 0 ? "　八成境力" : string.Empty)
				+ "\n持玄：以官身承帝统国命而走捷径；国命不入本人真命，不参与正统破境，官去则命归、朝亡则法散。";
		}

		if (XjXianGuoCourtSystem.TryGetOfficer(actorId, out XjXianGuoCourtOfficeRecord activeOffice)
			&& activeOffice != null && activeOffice.Active
			&& ActiveByKingdomId.TryGetValue(activeOffice.KingdomId, out XjXianGuoRecord officeDynasty)
			&& officeDynasty != null && officeDynasty.Active && officeDynasty.DynastyId == activeOffice.DynastyId)
		{
			string identity = XjXianGuoCourtSystem.IsHeavyMinisterOffice(activeOffice) ? "国之重臣" : "仙朝持玄官";
			return "身份：" + identity + "　系于" + officeDynasty.DynastyName
				+ "\n官职：" + activeOffice.OfficeName + "　" + XjXianGuoCourtSystem.GetRankDisplay(activeOffice.Rank)
				+ "\n承命：官位已定，国命尚待本朝结算。";
		}

		if (IsDiMingYang(actor))
		{
			return "身份：帝明阳　帝统待复\n复帝：当前未系有效仙国法统；人物道统仍在，按三年一判尝试重登旧国或在旧国覆亡后另寻帝位。";
		}

		if (HasAbdicatedImperialIdentity(actor))
		{
			return "身份：前帝明阳　已禅帝统\n政态：退居不争；保留普通明阳修行身份，不再参与明阳谋国、谋逆与复帝判定。";
		}

		if (TryGetActivePoliticalPlan(actorId, out XjXianGuoPoliticalPlan plan) && plan != null && plan.Active)
		{
			return "身份：明阳谋国　" + (string.IsNullOrWhiteSpace(plan.TargetKingdomName) ? "未定国朝" : plan.TargetKingdomName)
				+ "\n谋划：" + Math.Clamp(plan.Progress, 0, Math.Max(1, plan.ProgressNeeded)) + "/" + Math.Max(1, plan.ProgressNeeded)
				+ "　起于" + XjChronology.FormatYear(plan.StartYear);
		}

		return string.Empty;
	}


	private static bool IsMingYang(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals((daoTu ?? string.Empty).Trim(), MingYangDaoTu, StringComparison.Ordinal);
	}

	internal static bool IsMingYangCandidate(Actor actor) => IsMingYang(actor);

	private static bool HasShenDanIdentity(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjShenDanAccessor.BuildState(actor).Found) return true;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		return string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ShenDan, StringComparison.Ordinal);
	}

	private static bool IsImperialCultivationEligible(Actor actor)
	{
		return actor?.data != null
			&& (XjCultivationPathRules.IsZiFuJinDan(actor) || XjCultivationPathRules.IsFuQiYangXing(actor));
	}

	private static bool IsNativeSovereign(Actor actor)
	{
		return actor?.data != null && actor.kingdom?.data != null
			&& XjNativeKingdomSovereignReadBridge.TryResolveSovereign(actor.kingdom, out Actor sovereign)
			&& SameActor(sovereign, actor);
	}

	private static bool IsMingYangPoliticalRealmEligible(Actor actor)
	{
		// 胎息只是入道前置，不属于能够主持一城、发动明阳谋国的“修士”层级。
		// 从炼气/黄冠以上才允许进入玄鉴的政治计划；原生世界自行立王仍按原生事实验收。
		return actor?.data != null
			&& XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierLianQi;
	}

	private static bool IsMingYangPoliticalCandidate(Actor actor)
	{
		return actor?.data != null && IsMingYang(actor) && !HasAbdicatedImperialIdentity(actor)
			&& IsMingYangPoliticalRealmEligible(actor)
			&& actor.kingdom?.data != null && actor.city?.data != null && !actor.city.isCapitalCity()
			&& XjWorldBoxKingdomBridge.IsNativeCityLeaderForActor(actor.city, actor);
	}

	internal static bool CanCrownAsDiMingYang(Actor actor, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive()) { reason = "角色无效"; return false; }
		if (IsDiMingYang(actor)) { reason = "已经是帝明阳"; return false; }
		if (HasAbdicatedImperialIdentity(actor)) { reason = "已禅帝统，不再复帝"; return false; }
		if (!IsMingYang(actor)) { reason = "仅明阳修士可求帝统"; return false; }
		if (!IsImperialCultivationEligible(actor)) { reason = "仅紫府金丹或服气养性明阳可求帝统"; return false; }
		if (HasShenDanIdentity(actor)) { reason = "神丹旁法不承帝明阳"; return false; }
		if (!IsDiMingYangBirthGateOpen()) { reason = "尊卑法统已失"; return false; }
		long actorId = ((BaseSystemData)actor.data).id;
		if (_activeDiMingYangActorId > 0L && _activeDiMingYangActorId != actorId
			&& XjActorRegistry.ResolveKnownOrWorld(_activeDiMingYangActorId, out Actor current)
			&& current?.data != null && current.isAlive() && IsDiMingYang(current))
		{
			reason = "当世已有帝明阳";
			return false;
		}
		if (actor.kingdom?.data == null) { reason = "角色当前没有所属国家"; return false; }
		return true;
	}

	internal static bool TryAwakenDiMingYangFromKingship(Actor actor, Kingdom kingdom, int currentYear, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !actor.isAlive() || kingdom?.data == null) { reason = "王位事实无效"; return false; }
		bool alreadyImperial = IsDiMingYang(actor);
		if (!alreadyImperial)
		{
			if (HasAbdicatedImperialIdentity(actor)) { reason = "已禅帝统，不再复帝"; return false; }
			if (!IsMingYang(actor)) { reason = "并非明阳修士"; return false; }
			if (!IsImperialCultivationEligible(actor)) { reason = "并非紫府金丹或服气养性明阳"; return false; }
			if (HasShenDanIdentity(actor)) { reason = "神丹旁法不承帝明阳"; return false; }
		}
		if (!XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor sovereign) || !SameActor(sovereign, actor))
		{
			reason = "尚未真正登临王位";
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		int year = Math.Max(1, currentYear);

		if (!alreadyImperial)
		{
			if (!IsDiMingYangBirthGateOpen()) { reason = "尊卑法统已失"; return false; }
			if (_activeDiMingYangActorId > 0L && _activeDiMingYangActorId != actorId
				&& XjActorRegistry.ResolveKnownOrWorld(_activeDiMingYangActorId, out Actor current)
				&& current?.data != null && current.isAlive() && IsDiMingYang(current))
			{
				reason = "当世已有帝明阳";
				return false;
			}

			XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangEvaluated, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYang, 1);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangAwakenedYear, year);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, 0);
			_activeDiMingYangActorId = actorId;
			// 帝君是授命者而非承命百官。若此人登基前曾任中枢官，登帝统当刻就
			// 清掉旧国命投影与【国之重臣】身份，不等待下一次朝廷年度对账。
			if (HasInstitutionalPatronageMarker(actor)) ClearInstitutionalProjection(actor, syncVisibleTraits: true);
			SyncCourtIdentityTrait(actor);
			// 登帝统即永绝托果旁法；这里只清本人既有法门，不触碰其他角色神丹资格。
			XjShenDanMethodSystem.Clear(actor);
			EnsureImperialDaoTuIdentity(actor);
			EnsureImperialNonIntercalaryXianJi(actor, year);
			XjDiMingYangCityLordUpliftSystem.EnsureBaseline(actor);
			XjWorldArchiveSystem.MarkChanged();

			string currentRealm = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			XjRealmTitleApplyService.EnsureTitleForRealm(actor, currentRealm, MingYangDaoTu);
			string name = XjStringHelper.ActorName(actor, "未名帝明阳修士");
			string body = "【帝明阳】" + name + "以明阳之身登临人主，尊卑法统应其帝位而易，自此转自身道统为帝明阳。";
			RecordPoliticalHistory("帝明阳", body, year, actorId, name, 0L, string.Empty);
			XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: "#F0B66E", iconId: XjEventIconCatalog.HistoryWorld);
		}
		else
		{
			_activeDiMingYangActorId = actorId;
			XjDiMingYangCityLordUpliftSystem.EnsureBaseline(actor);
		}

		EnsureXianGuoForCrownedDiMingYang(actor, kingdom, year);
		return true;
	}

	internal static void OnNativeSovereignChanged(Kingdom kingdom, Actor sovereign)
	{
		if (kingdom?.data == null) return;
		// Postfix 只观察原生事务真正落地后的当前国主。传入参数只是 setKing 的请求值，
		// 第三方补丁/原生校正都有可能改变最终结果，仙朝绝不把“请求值”当权威。
		if (!XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor actualSovereign)
			|| actualSovereign?.data == null || !actualSovereign.isAlive()) return;
		sovereign = actualSovereign;
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		long kingdomId = kingdom.data.id;
		long actorId = ((BaseSystemData)sovereign.data).id;
		// 主动禅让使用完整的原生 setKing 事务。该事务的 Postfix 会同步回到这里，
		// 但此刻仙朝档案仍由外层禅让事务负责原子转移，不能把它误判成第三方抢位。
		if (_voluntaryAbdicationInProgress
			&& kingdomId == _voluntaryAbdicationKingdomId
			&& actorId == _voluntaryAbdicationSuccessorActorId)
		{
			return;
		}

		// 王位已经在现实国朝中落定时，不应继续等年度车道把“王统待定”挂在UI上。
		// 直接以最终国王事实推进一次继承判定；若新君尚不具承统资格，再保持待定。
		if (kingdomId > 0L && ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord pendingRecord)
			&& pendingRecord != null && pendingRecord.Active && pendingRecord.SuccessionPending)
		{
			ResolvePendingSuccession(pendingRecord, year);
			return;
		}

		if (kingdomId > 0L && ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord oldRecord)
			&& oldRecord != null && oldRecord.Active && oldRecord.SovereignActorId != actorId
			&& !oldRecord.SuccessionPending)
		{
			Actor oldActor = null;
			if (oldRecord.SovereignActorId > 0L) XjActorRegistry.ResolveKnownOrWorld(oldRecord.SovereignActorId, out oldActor);
			if (oldActor?.data != null && oldActor.isAlive() && IsDiMingYang(oldActor))
			{
				// WorldBox 的换王事务已经完整结束；这里只标记并排入后台修复，绝不
				// 在 setKing 的 Postfix 里递归 setKing。
				oldRecord.NativeSovereignMismatchYears = Math.Max(1, oldRecord.NativeSovereignMismatchYears + 1);
				oldRecord.Status = "仙朝行法（王统映照待正）";
				EnqueueNativeProjectionRepair(oldRecord);
				XjWorldArchiveSystem.MarkChanged();
				return;
			}
			EndDynasty(oldActor, oldRecord, year, "王统实际失其帝君");
		}

		if (IsDiMingYang(sovereign)) EnsureXianGuoForCrownedDiMingYang(sovereign, kingdom, year);
		else if (IsMingYang(sovereign)) TryAwakenDiMingYangFromKingship(sovereign, kingdom, year, out _);
	}

	private static void EnsureXianGuoForCrownedDiMingYang(Actor actor, Kingdom kingdom, int year)
	{
		if (actor?.data == null || kingdom?.data == null || !IsDiMingYang(actor)) return;
		// 旧国主刚死后的王统待定必须由继承事务决定“承统还是改朝”。此时新君
		// 虽已因现实王位转成帝明阳，但不能抢先新建一份仙国档案覆盖旧朝。
		if (ActiveByKingdomId.TryGetValue(kingdom.data.id, out XjXianGuoRecord pending)
			&& pending != null && pending.Active && pending.SuccessionPending) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!TryGetActiveRecord(actorId, out XjXianGuoRecord record))
		{
			string source = _restoringDiMingYangActorId == actorId ? "imperial_restoration" : "crowned_mingyang";
			record = CreateRecord(actor, kingdom, actor.city, year, source);
		}
		if (record != null && record.Active) RefreshDynasty(actor, record, year);
	}

	private static void ClearDiMingYang(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYang, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangEvaluated, 0);
		if (_activeDiMingYangActorId == actorId) _activeDiMingYangActorId = 0L;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool HasAbdicatedImperialIdentity(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.DiMingYangAbdicated, out int value)
			&& value > 0;
	}

	/// <summary>
	/// 帝明阳只在真实证得金丹后考虑禅让。继承人限定为同国、存活、直系子嗣中的
	/// 明阳修士，并复用现行帝明阳资格约束（修炼路径合法、非神丹、未曾退帝）。
	/// 优先读取家族运行索引；只有分家/婚配导致家族表未覆盖现存子女时，才按
	/// current_children_count 触发一次 XjActorRegistry 缓存快照补查，不扫描 WorldBox 世界。
	/// </summary>
	internal static bool TryVoluntaryAbdicationAfterRealJinDan(Actor emperor, int currentYear, out string reason)
	{
		reason = string.Empty;
		if (emperor?.data == null || !emperor.isAlive() || !IsDiMingYang(emperor))
		{
			reason = "并非在位帝明阳";
			return false;
		}
		if (XjRealmSuppression.GetRealmTier(emperor) < XjRealmSuppression.TierJinDan)
		{
			reason = "尚未证得真实金丹";
			return false;
		}
		if (!IsNativeSovereign(emperor) || emperor.kingdom?.data == null)
		{
			reason = "当前并非现实国主";
			return false;
		}

		long emperorId = ((BaseSystemData)emperor.data).id;
		if (!TryGetActiveRecord(emperorId, out XjXianGuoRecord record)
			|| record == null || !record.Active || record.KingdomId != emperor.kingdom.data.id)
		{
			reason = "未系有效仙国法统";
			return false;
		}
		if (!TrySelectVoluntaryImperialHeir(emperor, out Actor successor))
		{
			reason = "暂无可承统的明阳直系继承人";
			return false;
		}

		long kingdomId = emperor.kingdom.data.id;
		long successorId = ((BaseSystemData)successor.data).id;
		_voluntaryAbdicationInProgress = true;
		_voluntaryAbdicationKingdomId = kingdomId;
		_voluntaryAbdicationSuccessorActorId = successorId;
		bool nativeTransferred;
		string nativeReason;
		try
		{
			nativeTransferred = XjNativeKingdomSovereignWriteBridge.TrySetExistingKingdomSovereign(successor, out nativeReason);
		}
		finally
		{
			_voluntaryAbdicationInProgress = false;
			_voluntaryAbdicationKingdomId = 0L;
			_voluntaryAbdicationSuccessorActorId = 0L;
		}

		if (!nativeTransferred)
		{
			reason = string.IsNullOrWhiteSpace(nativeReason) ? "原生王位禅让未落地" : nativeReason;
			return false;
		}
		if (!XjNativeKingdomSovereignReadBridge.TryResolveSovereign(successor.kingdom, out Actor nativeSovereign)
			|| !SameActor(nativeSovereign, successor))
		{
			reason = "原生王位事实未指向继承人";
			return false;
		}

		CompleteVoluntaryImperialAbdication(emperor, successor, record, Math.Max(1, currentYear));
		return true;
	}

	private static bool TrySelectVoluntaryImperialHeir(Actor emperor, out Actor successor)
	{
		successor = null;
		if (emperor?.data == null || emperor.kingdom?.data == null) return false;
		long emperorId = ((BaseSystemData)emperor.data).id;
		long kingdomId = emperor.kingdom.data.id;
		if (emperorId <= 0L || kingdomId <= 0L) return false;

		List<Actor> directChildren = new List<Actor>();
		HashSet<long> seenChildIds = new HashSet<long>();
		if (XjFamilyMemberIndex.Shared.TryGetRecord(emperorId, out XjFamilyIdentity identity)
			&& identity.Found && identity.FamilyStableIdValue > 0L)
		{
			IEnumerable<Actor> familyMembers = XjFamilyMemberIndex.Shared.GetFamilyMembers(identity.FamilyStableIdValue);
			if (familyMembers != null)
			{
				foreach (Actor candidate in familyMembers)
				{
					TryCollectVoluntaryImperialChild(candidate, emperorId, seenChildIds, directChildren);
				}
			}
		}

		// 直系子女可能已经婚配、分家，从当前帝君 FamilyStableId 中消失。这里沿用
		// 血脉系统的低频兜底：只有家族索引数量小于原生现存子女数时才遍历一次
		// 已缓存的角色注册表。Snapshot 本身不访问 World.world.units，不形成世界扫描。
		int expectedLivingChildren = Math.Max(0, emperor.current_children_count);
		if (directChildren.Count < expectedLivingChildren)
		{
			IReadOnlyList<Actor> knownActors = XjActorRegistry.Snapshot();
			if (knownActors != null)
			{
				for (int i = 0; i < knownActors.Count; i++)
				{
					TryCollectVoluntaryImperialChild(knownActors[i], emperorId, seenChildIds, directChildren);
				}
			}
		}

		int bestTier = int.MinValue;
		int bestAptitude = int.MinValue;
		float bestAge = float.MinValue;
		long bestId = long.MaxValue;
		for (int i = 0; i < directChildren.Count; i++)
		{
			Actor candidate = directChildren[i];
			if (candidate?.data == null || !candidate.isAlive()) continue;
			long candidateId = ((BaseSystemData)candidate.data).id;
			if (candidateId <= 0L || candidateId == emperorId || candidate.kingdom?.data?.id != kingdomId) continue;
			if (!IsMingYang(candidate) || IsDiMingYang(candidate) || HasAbdicatedImperialIdentity(candidate)) continue;
			if (!IsImperialCultivationEligible(candidate) || HasShenDanIdentity(candidate)) continue;
			// native_plot 已经交给 WorldBox 执行叛乱；即使随后把该角色设为国王，
			// 原生 Plot 也会在下一拍继续把其旧城分出去。禅让只交给未进入这条
			// 分裂事务的直系后嗣，确保“承位”而非“先夺国、后补档”。
			if (TryGetActivePoliticalPlan(candidateId, out XjXianGuoPoliticalPlan candidatePlan)
				&& string.Equals(candidatePlan.Status, "native_plot", StringComparison.Ordinal)) continue;

			int tier = XjRealmSuppression.GetRealmTier(candidate);
			XjActorAccessor.TryGetInt(candidate, XjActorDataKeys.XjZz, out int aptitude);
			float age = Math.Max(0f, candidate.getAge());
			bool better = successor == null
				|| tier > bestTier
				|| (tier == bestTier && aptitude > bestAptitude)
				|| (tier == bestTier && aptitude == bestAptitude && age > bestAge)
				|| (tier == bestTier && aptitude == bestAptitude && Math.Abs(age - bestAge) < 0.001f && candidateId < bestId);
			if (!better) continue;
			successor = candidate;
			bestTier = tier;
			bestAptitude = aptitude;
			bestAge = age;
			bestId = candidateId;
		}

		return successor != null;
	}

	private static void TryCollectVoluntaryImperialChild(
		Actor candidate, long emperorId, HashSet<long> seenChildIds, List<Actor> result)
	{
		if (candidate?.data == null || !candidate.isAlive() || emperorId <= 0L) return;
		long candidateId = ((BaseSystemData)candidate.data).id;
		if (candidateId <= 0L || candidateId == emperorId || !IsChildOf(candidate, emperorId)) return;
		if (!seenChildIds.Add(candidateId)) return;
		result.Add(candidate);
	}

	private static void CompleteVoluntaryImperialAbdication(
		Actor emperor, Actor successor, XjXianGuoRecord record, int year)
	{
		if (emperor?.data == null || successor?.data == null || record == null || !record.Active) return;
		long emperorId = ((BaseSystemData)emperor.data).id;
		long successorId = ((BaseSystemData)successor.data).id;
		if (emperorId <= 0L || successorId <= 0L || emperorId == successorId) return;

		string formerImperialName = XjStringHelper.ActorName(emperor, "未名帝明阳修士");
		record.PreviousSovereignActorId = emperorId;
		record.PreviousSovereignName = formerImperialName;
		ActiveBySovereignId.Remove(emperorId);
		SetBorrowedState(emperor, record, false, 0);
		XjActorAccessor.SetLong(emperor, XjActorDataKeys.XianGuoDynastyId, 0L);
		ClearDiMingYang(emperor);
		XjActorAccessor.SetInt(emperor, XjActorDataKeys.DiMingYangAbdicated, 1);
		XjActorAccessor.SetInt(emperor, XjActorDataKeys.DiMingYangNextKingAttemptYear, 0);
		if (TryGetActivePoliticalPlan(emperorId, out XjXianGuoPoliticalPlan staleFormerEmperorPlan))
		{
			FailPoliticalPlan(staleFormerEmperorPlan, year, "已禅帝统，退居不争");
		}

		// 旧帝回到普通明阳后必须强制重建真实境界尊号，不能把“帝君”残留给退帝；
		// 若已经是道胎，还要先恢复上一高境尊号再投影道胎，避免出现空尊号/双尊号。
		string formerRealm = XjRealmHelper.GetUnifiedId(emperor, XjRealmHelper.GetTraitSnapshotForRouter);
		XjRealmTitleApplyService.RebuildAfterDiMingYangAbdication(emperor, formerRealm, MingYangDaoTu);

		XjActorAccessor.SetInt(successor, XjActorDataKeys.DiMingYangEvaluated, 1);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.DiMingYang, 1);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.DiMingYangAwakenedYear, year);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.DiMingYangNextKingAttemptYear, 0);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.DiMingYangAbdicated, 0);
		_activeDiMingYangActorId = successorId;
		if (HasInstitutionalPatronageMarker(successor)) ClearInstitutionalProjection(successor, syncVisibleTraits: true);
		SyncCourtIdentityTrait(successor);
		XjShenDanMethodSystem.Clear(successor);
		EnsureImperialDaoTuIdentity(successor);
		EnsureImperialNonIntercalaryXianJi(successor, year);
		XjDiMingYangCityLordUpliftSystem.EnsureBaseline(successor);
		if (TryGetActivePoliticalPlan(successorId, out XjXianGuoPoliticalPlan plan))
		{
			CompletePoliticalPlan(plan, year, "承禅登帝");
		}
		string successorRealm = XjRealmHelper.GetUnifiedId(successor, XjRealmHelper.GetTraitSnapshotForRouter);
		XjRealmTitleApplyService.EnsureTitleForRealm(successor, successorRealm, MingYangDaoTu);

		record.SovereignActorId = successorId;
		record.SovereignName = XjStringHelper.ActorName(successor, "未名帝明阳修士");
		record.SuccessionPending = false;
		record.SuccessionStartedYear = 0;
		record.NativeSovereignMismatchYears = 0;
		record.DynastyGeneration = Math.Max(1, record.DynastyGeneration + 1);
		record.LastPoliticalEventYear = year;
		record.Status = "帝明阳禅让";
		record.LastAnnualYear = 0;
		ActiveBySovereignId[successorId] = record;
		XjActorAccessor.SetLong(successor, XjActorDataKeys.XianGuoDynastyId, record.DynastyId);
		SetBorrowedState(successor, record, false, 0);
		RefreshDynasty(successor, record, year);
		XjWorldArchiveSystem.MarkChanged();

		string retiredName = XjStringHelper.ActorName(emperor, "未名帝明阳修士");
		string successorName = XjStringHelper.ActorName(successor, "未名帝明阳修士");
		string body = "【帝明阳禅让】" + formerImperialName + "证得真金，遂退帝统而复为明阳；"
			+ successorName + "以明阳后嗣承其王位与仙国法统，新帝明阳由此诞生。";
		RecordPoliticalHistory("帝明阳禅让", body, year, successorId, successorName, emperorId, retiredName);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: "#F0B66E", iconId: XjEventIconCatalog.HistoryWorld);
	}

	private static bool TryRestoreImperialKingship(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive() || !IsDiMingYang(actor) || IsNativeSovereign(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, out int nextYear);
		if (nextYear <= 0)
		{
			nextYear = Math.Max(1, year + ImperialRestorationIntervalYears);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, nextYear);
			XjWorldArchiveSystem.MarkChanged();
			return false;
		}
		if (year < nextYear) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, year + ImperialRestorationIntervalYears);
		XjWorldArchiveSystem.MarkChanged();
		if (actor.kingdom?.data == null || !actor.kingdom.isCiv()) return false;

		// 旧朝仍真实存在时，复帝只能争回旧朝，不得因为叛乱/迁城把actor.kingdom改了
		// 就跑去别国自封，造成“帝明阳抛弃国家”。只有旧国确实亡国后才允许另寻国统。
		if (TryGetLatestDynastyForSovereign(actorId, out XjXianGuoRecord latestDynasty)
			&& latestDynasty != null && latestDynasty.KingdomId > 0L
			&& XjWorldLookupIndex.TryResolveKingdom(latestDynasty.KingdomId, out Kingdom survivingOldKingdom)
			&& survivingOldKingdom?.data != null && survivingOldKingdom.isCiv()
			&& actor.kingdom.data.id != latestDynasty.KingdomId)
		{
			return false;
		}

		if (XjDeterministicHash.PositiveIndex(actorId + (long)year * 31L, "dimingyang.imperial_restoration", 100)
			>= ImperialRestorationChancePercent) return false;

		string kingdomName = actor.kingdom.data.name ?? "旧国";
		_restoringDiMingYangActorId = actorId;
		bool restored;
		try
		{
			restored = XjNativeKingdomSovereignWriteBridge.TrySetExistingKingdomSovereign(actor, out _)
				&& IsNativeSovereign(actor);
		}
		finally
		{
			_restoringDiMingYangActorId = 0L;
		}
		if (!restored) return false;

		EnsureXianGuoForCrownedDiMingYang(actor, actor.kingdom, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, year + ImperialRestorationIntervalYears);
		string name = XjStringHelper.ActorName(actor, "未名帝明阳修士");
		string body = "【帝统复归】" + name + "帝统未绝，蛰伏既久，今复践" + kingdomName
			+ "人主之位，百官再奉其统；天光复正，国朝众玄重新归于帝躬。";
		RecordPoliticalHistory("帝统复归", body, year, actorId, name, 0L, string.Empty);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: "#F0B66E", iconId: XjEventIconCatalog.HistoryWorld);
		return true;
	}

	private static bool TryGetLatestDynastyForSovereign(long actorId, out XjXianGuoRecord record)
	{
		record = null;
		if (actorId <= 0L) return false;
		for (int i = Records.Count - 1; i >= 0; i--)
		{
			XjXianGuoRecord candidate = Records[i];
			if (candidate == null) continue;
			if (candidate.SovereignActorId == actorId || candidate.FounderActorId == actorId || candidate.PreviousSovereignActorId == actorId)
			{
				record = candidate;
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// RC11.28 兼容修复：RC11.27 曾短暂允许“帝明阳→神丹”。若旧测试档已经形成
	/// 这种非法组合，先撤销神丹挂靠并退回本修法的真人级，再由正常证金/求真君流程
	/// 重新竞争明阳果位或余位。这里绝不凭空赠送一个金丹席位。
	/// </summary>
	private static void ReconcileImperialShenDanConflict(Actor actor)
	{
		if (actor?.data == null || !IsDiMingYang(actor)) return;
		XjShenDanMethodSystem.Clear(actor);
		if (!HasShenDanIdentity(actor)) return;

		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		XjShenDanAccessor.ClearSuccess(actor);
		XjJinDanAccessor.ClearSuccess(actor);
		if (fuQi)
		{
			XjFuQiStateTransitions.TrySetRealm(actor, XjRealmIds.FuQiZhenRen, false, true);
		}
		else
		{
			XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, false);
		}
		XjVisibleTraitSync.SyncCultivationTraits(actor);
	}

	/// <summary>
	/// 帝明阳的紫金仙基从成立之刻起便只允许本道上位/下位结构，绝不再引入外道仙基。
	/// 旧档或登基前已经混入的外道仙基会在帝统成立时原位改映为明阳本路仙基，使
	/// 五仙基最终只能落向果位或余位，而不会出现“结构已锁死只能证闰”的帝明阳。
	/// </summary>
	private static void EnsureImperialDaoTuIdentity(Actor actor)
	{
		if (actor?.data == null || !IsDiMingYang(actor)) return;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !string.Equals((daoTu ?? string.Empty).Trim(), MingYangDaoTu, StringComparison.Ordinal))
		{
			// 修复旧档/兼容入口把帝明阳权威 DaoTu 改成瑞炁等异道的情况。
			// 紫金与服气各走自己的权威元数据写口；二者自0.9.8.23起都带帝明阳硬锁。
			if (XjCultivationPathRules.IsFuQiYangXing(actor))
			{
				XjFuQiStateTransitions.TrySetDaoTuMetadataOnly(actor, MingYangDaoTu, syncVisibleTraits: true);
			}
			else
			{
				XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, MingYangDaoTu, syncVisibleTraits: true);
			}
		}
	}

	internal static void EnsureImperialNonIntercalaryXianJi(Actor actor, int year)
	{
		if (actor?.data == null || !IsDiMingYang(actor) || !XjCultivationPathRules.IsZiFuJinDan(actor)) return;
		EnsureImperialDaoTuIdentity(actor);
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Ids == null || state.Count <= 0) return;
		bool preferLower = XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(MingYangDaoTu);
		string[] primaryPool = preferLower ? XjXianJiCatalog.GetLowerPool(MingYangDaoTu) : XjXianJiCatalog.GetUpperPool(MingYangDaoTu);
		string[] fallbackPool = preferLower ? XjXianJiCatalog.GetUpperPool(MingYangDaoTu) : XjXianJiCatalog.GetLowerPool(MingYangDaoTu);
		for (int i = 0; i < state.Ids.Length; i++)
		{
			string oldId = state.Ids[i];
			XjXianJiPoolKind oldPoolKind = XjXianJiCatalog.GetPoolKind(MingYangDaoTu, oldId);
			if (oldPoolKind == XjXianJiPoolKind.Native || oldPoolKind == XjXianJiPoolKind.Lower) continue;
			string replacement = PickImperialXianJiReplacement(actor, state.Ids, primaryPool, oldId, year, i);
			if (string.IsNullOrWhiteSpace(replacement)) replacement = PickImperialXianJiReplacement(actor, state.Ids, fallbackPool, oldId, year, i + 17);
			if (string.IsNullOrWhiteSpace(replacement)) continue;
			if (XjXianJiAccessor.TryReplace(actor, oldId, replacement, Math.Max(1, year), "帝明阳帝统归基"))
			{
				state = XjXianJiAccessor.BuildState(actor);
			}
		}
	}

	private static string PickImperialXianJiReplacement(Actor actor, string[] existingIds, string[] pool, string oldId, int year, int saltOffset)
	{
		if (actor?.data == null || pool == null || pool.Length == 0) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		int start = XjDeterministicHash.PositiveIndex(actorId + year + saltOffset * 97L, "dimingyang.xianji.route", pool.Length);
		for (int offset = 0; offset < pool.Length; offset++)
		{
			string candidate = pool[(start + offset) % pool.Length];
			if (string.IsNullOrWhiteSpace(candidate) || string.Equals(candidate, oldId, StringComparison.Ordinal)) continue;
			bool exists = false;
			for (int i = 0; i < existingIds.Length; i++)
			{
				if (string.Equals(existingIds[i], candidate, StringComparison.Ordinal)) { exists = true; break; }
			}
			if (!exists) return candidate;
		}
		return string.Empty;
	}

	internal static bool IsDiMingYangBirthGateOpen()
	{
		return XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(MingYangDaoTu, DiMingYangCoreAuthority);
	}

	private static void TryEstablishXianGuo(Actor actor, int year, out XjXianGuoRecord record)
	{
		record = null;
		if (actor?.data == null || !actor.isAlive() || !IsDiMingYang(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (_activeDiMingYangActorId > 0L && _activeDiMingYangActorId != actorId) return;
		_activeDiMingYangActorId = actorId;
		if (actor.kingdom?.data == null || !IsNativeSovereign(actor)) return;
		if (ActiveByKingdomId.TryGetValue(actor.kingdom.data.id, out XjXianGuoRecord pending)
			&& pending != null && pending.Active && pending.SuccessionPending) return;
		// 仙国法本身从帝明阳真正登基的当年即成立；紫府/真人门槛只属于后续
		// “众玄归一、假丹成型”，不能再阻断国势国运与法统档案。
		record = CreateRecord(actor, actor.kingdom, actor.city, Math.Max(1, year), "crowned_mingyang");
	}

	private static void TryStartMingYangPoliticalPlan(Actor actor, int year)
	{
		if (HasAbdicatedImperialIdentity(actor) || !IsMingYangPoliticalCandidate(actor) || actor.kingdom?.data == null) return;
		Kingdom targetKingdom = actor.kingdom;
		if (CountKingdomCities(targetKingdom) <= 1) return;
		XjNativeKingdomSovereignReadBridge.TryResolveSovereign(targetKingdom, out Actor targetSovereign);
		StartPoliticalPlan(actor, targetKingdom, targetSovereign, actor.city, year);
	}

	private static XjXianGuoPoliticalPlan StartPoliticalPlan(
		Actor actor, Kingdom targetKingdom, Actor targetSovereign, City originCity, int year)
	{
		if (actor?.data == null || HasAbdicatedImperialIdentity(actor)
			|| targetKingdom?.data == null || originCity?.data == null) return null;
		long actorId = ((BaseSystemData)actor.data).id;
		if (TryGetActivePoliticalPlan(actorId, out XjXianGuoPoliticalPlan existing)) return existing;
		long targetSovereignId = targetSovereign?.data == null ? 0L : ((BaseSystemData)targetSovereign.data).id;
		bool targetIsParent = targetSovereignId > 0L && IsChildOf(actor, targetSovereignId);
		XjXianGuoPoliticalPlan plan = new XjXianGuoPoliticalPlan
		{
			PlanId = NextPoliticalPlanId(actorId, targetKingdom.data.id, year),
			ActorId = actorId,
			ActorName = XjStringHelper.ActorName(actor, "未名帝明阳修士"),
			PlanType = targetIsParent ? "父子争统" : "谋逆自立",
			TargetKingdomId = targetKingdom.data.id,
			TargetKingdomName = targetKingdom.data.name ?? string.Empty,
			TargetSovereignActorId = targetSovereignId,
			TargetSovereignName = XjStringHelper.ActorName(targetSovereign, "未名帝明阳修士"),
			OriginCityId = originCity.data.id,
			StartYear = Math.Max(1, year),
			LastProgressYear = Math.Max(1, year),
			LastActionYear = 0,
			Progress = targetIsParent ? 20 : 0,
			ProgressNeeded = PoliticalPlanBaseProgressNeeded,
			Status = "plotting",
			TargetIsParent = targetIsParent,
			Active = true
		};
		PoliticalPlans.Add(plan);
		ActivePoliticalPlanByActorId[actorId] = plan;
		XjWorldArchiveSystem.MarkChanged();
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			"【明阳·谋国】" + plan.ActorName + "据" + (originCity.data.name ?? "一城")
			+ "而观国势，已暗起" + plan.PlanType + "之谋；谋划成熟前不会直接改写王位或疆土。",
			iconId: XjEventIconCatalog.HistoryWorld);
		return plan;
	}

	private static void AdvancePoliticalPlan(
		Actor actor, XjXianGuoPoliticalPlan plan, int year, out XjXianGuoRecord record)
	{
		record = null;
		if (plan == null || !plan.Active || actor?.data == null || !actor.isAlive())
		{
			FailPoliticalPlan(plan, year, "谋主已失");
			return;
		}
		if (HasAbdicatedImperialIdentity(actor))
		{
			FailPoliticalPlan(plan, year, "已禅帝统，退居不争");
			return;
		}

		// 原生政治若已经把谋主推成某国国王，玄鉴只验收这个现实结果。
		if (actor.kingdom?.data != null
			&& XjNativeKingdomSovereignReadBridge.TryResolveSovereign(actor.kingdom, out Actor currentSovereign)
			&& SameActor(currentSovereign, actor))
		{
			bool seizedTarget = actor.kingdom.data.id == plan.TargetKingdomId;
			string outcome = seizedTarget ? "夺取王位" : "谋逆自立";
			CompletePoliticalPlan(plan, year, outcome);
			string outcomeBody = "【帝明阳·" + outcome + "】" + plan.ActorName
				+ (seizedTarget ? "已登旧国之主位" : "已自旧国裂土而成新国之主")
				+ "；玄鉴至此只验收现实结果，再据此建立仙国法统。";
			RecordPoliticalHistory(outcome, outcomeBody, year, plan.ActorId, plan.ActorName,
				plan.TargetSovereignActorId, plan.TargetSovereignName);
			XjBroadcastSystem.ShowRecordedWorldTipCritical(outcomeBody, color: "#D98A67", iconId: XjEventIconCatalog.HistoryWorld);
			TryAwakenDiMingYangFromKingship(actor, actor.kingdom, year, out _);
			TryGetActiveRecord(((BaseSystemData)actor.data).id, out record);
			if (record != null)
			{
				record.MouNi = Math.Clamp(record.MouNi + (seizedTarget ? 45 : 35), 0, 100);
				if (plan.ViolentClaim && plan.TargetIsParent)
					record.FuZiXiangSha = Math.Clamp(record.FuZiXiangSha + 45, 0, 100);
				record.ZiYan = Math.Clamp(record.ZiYan + 20, 0, 100);
				record.JunChen = Math.Max(0, record.JunChen - 12);
				record.NationalFortune = Math.Max(0, (int)Math.Round(record.NationalFortune * 0.88d));
				XjWorldArchiveSystem.MarkChanged();
			}
			return;
		}

		if (year - plan.StartYear > PoliticalPlanTimeoutYears)
		{
			FailPoliticalPlan(plan, year, "谋划久滞未成");
			return;
		}

		if (string.Equals(plan.Status, "native_plot", StringComparison.Ordinal))
		{
			// 原生Plot可能被取消或因世界条件消散。等待一段时间后允许再次进入
			// “plotting”，但绝不绕过原生Plot直接建国。
			if (plan.LastActionYear > 0 && year - plan.LastActionYear >= 20)
			{
				plan.Status = "plotting";
				plan.Progress = Math.Max(80, plan.Progress);
				plan.LastActionYear = year;
				XjWorldArchiveSystem.MarkChanged();
			}
			return;
		}

		if (actor.city?.data == null || actor.kingdom?.data == null
			|| actor.kingdom.data.id != plan.TargetKingdomId
			|| !XjWorldBoxKingdomBridge.IsNativeCityLeaderForActor(actor.city, actor)
			|| actor.city.isCapitalCity())
		{
			return;
		}

		if (plan.LastProgressYear >= year) return;
		int elapsed = Math.Max(1, year - plan.LastProgressYear);
		int tier = XjRealmSuppression.GetRealmTier(actor);
		int cityPopulation = Math.Max(0, actor.city.units?.Count ?? 0);
		int gainPerYear = 6 + (tier >= XjRealmSuppression.TierJinDan ? 5 : 2)
			+ Math.Min(6, cityPopulation / 100) + (plan.TargetIsParent ? 2 : 0);
		if (plan.ViolentClaim) gainPerYear += 6;
		plan.Progress = Math.Min(plan.ProgressNeeded, plan.Progress + gainPerYear * elapsed);
		plan.LastProgressYear = year;
		XjWorldArchiveSystem.MarkChanged();

		if (plan.Progress < plan.ProgressNeeded) return;
		if (plan.LastActionYear > 0 && year - plan.LastActionYear < PoliticalPlanRetryYears) return;
		plan.LastActionYear = year;
		if (!XjNativeKingdomPlotBridge.TryStartRebellion(actor)) return;

		plan.Status = "native_plot";
		XjWorldArchiveSystem.MarkChanged();
		string rebellionBody = "【明阳·谋逆】" + plan.ActorName + "多年谋划已熟，遂举旗起事；"
			+ "自此新国既立，战事与胜负皆归天下大势自行演变。";
		RecordPoliticalHistory("帝明阳谋逆", rebellionBody, year, plan.ActorId, plan.ActorName,
			plan.TargetSovereignActorId, plan.TargetSovereignName);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(rebellionBody, color: "#D98A67");
	}



	internal static void TickAnnualWorld(int annualYear)
	{
		int year = Math.Max(1, annualYear);
		// 政治计划的进度由谋主自己的年度角色车道推进；世界车道只负责
		// “国主死亡后由谁真实继位”这类跨角色事实，避免扫描全部国家与人口。
		if (ActiveByKingdomId.Count == 0) return;
		PendingSuccessionScratch.Clear();
		foreach (KeyValuePair<long, XjXianGuoRecord> pair in ActiveByKingdomId)
		{
			XjXianGuoRecord value = pair.Value;
			if (value != null && value.Active && value.SuccessionPending) PendingSuccessionScratch.Add(value);
		}

		for (int i = 0; i < PendingSuccessionScratch.Count; i++)
		{
			ResolvePendingSuccession(PendingSuccessionScratch[i], year);
		}
		PendingSuccessionScratch.Clear();
	}

	private static void ResolvePendingSuccession(XjXianGuoRecord record, int year)
	{
		if (record == null || !record.Active || !record.SuccessionPending) return;
		if (record.KingdomId <= 0L || !XjWorldLookupIndex.TryResolveKingdom(record.KingdomId, out Kingdom kingdom) || kingdom?.data == null)
		{
			EndDynasty(null, record, year, "国朝已亡，仙国法统随之断绝");
			return;
		}

		if (!XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor successor)
			|| successor?.data == null || !successor.isAlive())
		{
			if (year - record.SuccessionStartedYear >= SuccessionGraceYears)
			{
				EndDynasty(null, record, year, "王位久悬，帝明阳法统失其人主");
			}
			return;
		}

		long successorId = ((BaseSystemData)successor.data).id;
		if (successorId <= 0L) return;
		// 某些复活/死亡回滚/第三方王位事务会先触发死亡快照，再让原帝君仍然
		// 作为现实国王存活。旧逻辑遇到“现王 == 前帝君”直接return，导致王统待定
		// 永久悬挂。只要同一帝明阳仍活着且确实坐在本朝王位上，就恢复原王统。
		if (successorId == record.PreviousSovereignActorId)
		{
			if (successor.isAlive() && IsDiMingYang(successor)) RestorePendingSovereign(record, successor, year);
			return;
		}
		if (IsMingYang(successor) && !IsDiMingYang(successor))
			TryAwakenDiMingYangFromKingship(successor, kingdom, year, out _);
		if (!IsDiMingYang(successor))
		{
			if (year - record.SuccessionStartedYear >= SuccessionGraceYears)
			{
				EndDynasty(null, record, year, "新君非帝明阳，仙国借玄无人承接");
			}
			return;
		}

		bool child = IsChildOf(successor, record.PreviousSovereignActorId);
		bool violent = successorId == record.LastKillerActorId;
		if (child && !violent)
		{
			TransferDynastySovereign(record, successor, year);
			return;
		}

		CreateSuccessorDynasty(record, successor, kingdom, year, violent, child);
	}

	private static void RestorePendingSovereign(XjXianGuoRecord record, Actor sovereign, int year)
	{
		if (record == null || sovereign?.data == null || !sovereign.isAlive() || !IsDiMingYang(sovereign)) return;
		long sovereignId = ((BaseSystemData)sovereign.data).id;
		if (sovereignId <= 0L) return;
		record.SovereignActorId = sovereignId;
		record.SovereignName = XjStringHelper.ActorName(sovereign, "未名帝明阳修士");
		record.SuccessionPending = false;
		record.SuccessionStartedYear = 0;
		record.NativeSovereignMismatchYears = 0;
		record.LastPoliticalEventYear = Math.Max(record.LastPoliticalEventYear, year);
		record.LastAnnualYear = 0;
		record.Status = "仙国行法";
		_activeDiMingYangActorId = sovereignId;
		ActiveBySovereignId[sovereignId] = record;
		XjActorAccessor.SetLong(sovereign, XjActorDataKeys.XianGuoDynastyId, record.DynastyId);
		RefreshDynasty(sovereign, record, year);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void TransferDynastySovereign(XjXianGuoRecord record, Actor successor, int year)
	{
		if (record == null || successor?.data == null) return;
		long successorId = ((BaseSystemData)successor.data).id;
		if (record.PreviousSovereignActorId > 0L
			&& record.PreviousSovereignActorId != successorId
			&& XjActorRegistry.ResolveKnownOrWorld(record.PreviousSovereignActorId, out Actor previousActor)
			&& previousActor?.data != null)
		{
			SetBorrowedState(previousActor, record, false, 0);
			XjActorAccessor.SetLong(previousActor, XjActorDataKeys.XianGuoDynastyId, 0L);
		}
		record.SovereignActorId = successorId;
		record.SovereignName = XjStringHelper.ActorName(successor, "未名帝明阳修士");
		record.SuccessionPending = false;
		record.SuccessionStartedYear = 0;
		record.NativeSovereignMismatchYears = 0;
		record.DynastyGeneration = Math.Max(1, record.DynastyGeneration + 1);
		record.LastPoliticalEventYear = year;
		record.Status = "帝明阳承统";
		record.NationalFortune = Math.Max(0, (int)Math.Round(record.NationalFortune * 0.94d));
		record.JunChen = Math.Max(0, record.JunChen - 8);
		record.LastAnnualYear = 0;
		ActiveBySovereignId[successorId] = record;
		XjActorAccessor.SetLong(successor, XjActorDataKeys.XianGuoDynastyId, record.DynastyId);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.XianGuoBorrowedTier, 0);
		XjActorAccessor.SetInt(successor, XjActorDataKeys.XianGuoBorrowedGrade, 0);
		RefreshDynasty(successor, record, year);
		XjWorldArchiveSystem.MarkChanged();

		string body = "【仙国承统】" + record.SovereignName + "承" + record.PreviousSovereignName
			+ "之位，" + record.DynastyName + "法统未改；国运稍损，而君臣秩序渐复。";
		RecordPoliticalHistory("仙国承统", body, year, successorId, record.SovereignName,
			record.PreviousSovereignActorId, record.PreviousSovereignName);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: "#D9B36C");
	}

	private static void CreateSuccessorDynasty(
		XjXianGuoRecord predecessor, Actor successor, Kingdom kingdom, int year, bool violent, bool child)
	{
		if (predecessor == null || successor?.data == null || kingdom?.data == null) return;
		int inheritedPotential = predecessor.NationalPotential;
		int inheritedFortune = predecessor.NationalFortune;
		int inheritedTianGuang = predecessor.TianGuang;
		int inheritedZiYan = predecessor.ZiYan;
		int inheritedJunChen = predecessor.JunChen;
		int inheritedDiHuang = predecessor.DiHuang;
		int inheritedFuZi = predecessor.FuZiXiangSha;
		int inheritedMouNi = predecessor.MouNi;
		long predecessorId = predecessor.DynastyId;
		string predecessorName = predecessor.DynastyName;
		long oldSovereignId = predecessor.PreviousSovereignActorId;
		string oldSovereignName = predecessor.PreviousSovereignName;

		EndDynasty(null, predecessor, year, violent ? "谋逆夺位，旧朝法统已易" : "异支入主，旧朝法统已易");
		XjXianGuoRecord next = CreateRecord(successor, kingdom, successor.city, year, "dynastic_change");
		if (next == null) return;

		next.PredecessorDynastyId = predecessorId;
		next.DynastyGeneration = 1;
		next.NationalPotential = Math.Max(next.NationalPotential, (int)Math.Round(inheritedPotential * 0.70d));
		next.NationalFortune = Math.Min(next.NationalPotential,
			Math.Max(next.NationalFortune, (int)Math.Round(inheritedFortune * (violent ? 0.55d : 0.65d))));
		next.TianGuang = Math.Clamp((int)Math.Round(inheritedTianGuang * 0.65d), 0, 100);
		next.ZiYan = Math.Clamp(Math.Max(next.ZiYan, inheritedZiYan + (violent ? 25 : 12)), 0, 100);
		next.JunChen = Math.Clamp((int)Math.Round(inheritedJunChen * (violent ? 0.42d : 0.58d)), 0, 100);
		next.DiHuang = Math.Clamp((int)Math.Round(inheritedDiHuang * 0.70d), 0, 100);
		next.FuZiXiangSha = Math.Clamp(inheritedFuZi + (violent && child ? 50 : 0), 0, 100);
		next.MouNi = Math.Clamp(inheritedMouNi + (violent ? 55 : 35), 0, 100);
		next.Status = violent ? "谋逆改朝" : "改朝换代";
		next.LastPoliticalEventYear = year;
		SetBorrowedState(successor, next, false, 0);
		XjWorldArchiveSystem.MarkChanged();

		string successorName = XjStringHelper.ActorName(successor, "未名帝明阳修士");
		string body = "【改朝换代】" + successorName + (violent ? "以谋逆夺位" : "入主旧国")
			+ "，" + predecessorName + "法统遂终，新朝承其部分国势而重定君臣帝皇之象。";
		if (violent && child)
		{
			body = "【父子相杀·改朝】" + successorName + "弑" + oldSovereignName
				+ "而夺王位，旧朝倾覆；悖逆既成现实国统，新朝以紫焰与帝皇之象重新收束天下。";
		}
		RecordPoliticalHistory(violent && child ? "父子相杀·改朝" : "改朝换代", body, year,
			((BaseSystemData)successor.data).id, successorName, oldSovereignId, oldSovereignName);
		XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: violent ? "#D98A67" : "#D9B36C");
	}

	private static bool TryGetActivePoliticalPlan(long actorId, out XjXianGuoPoliticalPlan plan)
	{
		plan = null;
		if (actorId <= 0L) return false;
		return ActivePoliticalPlanByActorId.TryGetValue(actorId, out plan)
			&& plan != null && plan.Active;
	}

	private static long NextPoliticalPlanId(long actorId, long kingdomId, int year)
	{
		long candidate = XjDeterministicHash.PositiveHash(actorId ^ kingdomId ^ year, "xianguo.political_plan.v1");
		if (candidate <= 0L || PoliticalPlans.Exists(item => item != null && item.PlanId == candidate))
		{
			candidate = Math.Max(1L, _nextPoliticalPlanId);
			while (PoliticalPlans.Exists(item => item != null && item.PlanId == candidate)) candidate++;
		}
		_nextPoliticalPlanId = Math.Max(_nextPoliticalPlanId, candidate + 1L);
		return candidate;
	}

	private static void CompletePoliticalPlan(XjXianGuoPoliticalPlan plan, int year, string outcome)
	{
		if (plan == null) return;
		plan.Active = false;
		plan.Status = "completed";
		plan.Outcome = string.IsNullOrWhiteSpace(outcome) ? "谋划已成" : outcome.Trim();
		plan.CompletedYear = Math.Max(plan.StartYear, year);
		if (ActivePoliticalPlanByActorId.TryGetValue(plan.ActorId, out XjXianGuoPoliticalPlan current)
			&& ReferenceEquals(current, plan)) ActivePoliticalPlanByActorId.Remove(plan.ActorId);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void FailPoliticalPlan(XjXianGuoPoliticalPlan plan, int year, string reason)
	{
		if (plan == null) return;
		plan.Active = false;
		plan.Status = "failed";
		plan.Outcome = string.IsNullOrWhiteSpace(reason) ? "谋划已散" : reason.Trim();
		plan.CompletedYear = Math.Max(plan.StartYear, year);
		if (ActivePoliticalPlanByActorId.TryGetValue(plan.ActorId, out XjXianGuoPoliticalPlan current)
			&& ReferenceEquals(current, plan)) ActivePoliticalPlanByActorId.Remove(plan.ActorId);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static int CountKingdomCities(Kingdom kingdom)
	{
		if (kingdom?.data == null) return 0;
		long kingdomId = kingdom.data.id;
		int count = 0;
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < cities.Count; i++)
		{
			if (cities[i]?.kingdom?.data?.id == kingdomId) count++;
		}
		return count;
	}

	private static bool IsChildOf(Actor actor, long parentId)
	{
		if (actor?.data == null || parentId <= 0L) return false;
		return actor.data.parent_id_1 == parentId || actor.data.parent_id_2 == parentId;
	}

	private static void RecordPoliticalHistory(
		string title, string body, int year, long actorId, string actorName, long relatedActorId, string relatedActorName)
	{
		XjWorldHistoryStore.RecordDomainEvent(
			XuanJianVNext.Data.History.XjWorldHistoryCategory.World,
			title,
			body,
			5,
			isProtected: true,
			actorId: actorId,
			actorName: actorName ?? string.Empty,
			year: Math.Max(1, year),
			relatedActorId: relatedActorId,
			relatedActorName: relatedActorName ?? string.Empty,
			iconIdOverride: XjEventIconCatalog.HistoryWorld);
	}

	private static XjXianGuoRecord CreateRecord(Actor sovereign, Kingdom kingdom, City capital, int year, string source)
	{
		if (sovereign?.data == null || kingdom?.data == null) return null;
		long actorId = ((BaseSystemData)sovereign.data).id;
		long kingdomId = kingdom.data.id;
		if (actorId <= 0L || kingdomId <= 0L) return null;

		if (ActiveBySovereignId.TryGetValue(actorId, out XjXianGuoRecord existing) && existing != null)
		{
			return existing;
		}
		if (ActiveByKingdomId.TryGetValue(kingdomId, out XjXianGuoRecord existingKingdom)
			&& existingKingdom != null && existingKingdom.Active)
		{
			if (existingKingdom.SovereignActorId == actorId) return existingKingdom;
			long oldSovereignId = existingKingdom.SovereignActorId;
			if (oldSovereignId > 0L && IsChildOf(sovereign, oldSovereignId))
			{
				existingKingdom.PreviousSovereignActorId = oldSovereignId;
				existingKingdom.PreviousSovereignName = existingKingdom.SovereignName;
				ActiveBySovereignId.Remove(oldSovereignId);
				TransferDynastySovereign(existingKingdom, sovereign, Math.Max(1, year));
				return existingKingdom;
			}
			Actor oldSovereign = null;
			if (oldSovereignId > 0L) XjActorRegistry.ResolveKnownOrWorld(oldSovereignId, out oldSovereign);
			EndDynasty(oldSovereign, existingKingdom, Math.Max(1, year), "王位已易，旧朝法统退场");
			source = "dynastic_change";
		}

		long dynastyId = NextDynastyId(actorId, kingdomId, year);
		string dynastyName = string.Equals(source, "dynastic_change", StringComparison.Ordinal)
			? BuildDynastyName(sovereign, capital)
			: string.IsNullOrWhiteSpace(kingdom.data.name)
				? BuildDynastyName(sovereign, capital)
				: XjNativeMetaNameSinicizer.ResolveCanonicalKingdomName(kingdom.data.name);
		XjXianGuoRecord record = new XjXianGuoRecord
		{
			DynastyId = dynastyId,
			DynastyName = dynastyName,
			FounderActorId = actorId,
			SovereignActorId = actorId,
			SovereignName = XjStringHelper.ActorName(sovereign, "未名帝明阳修士"),
			KingdomId = kingdomId,
			CapitalCityId = capital?.data?.id ?? 0L,
			FoundedYear = Math.Max(1, year),
			LastAnnualYear = 0,
			Active = true,
			Status = "仙国初立",
			FoundingSource = source ?? string.Empty
		};
		Records.Add(record);
		ByDynastyId[dynastyId] = record;
		ActiveBySovereignId[actorId] = record;
		ActiveByKingdomId[kingdomId] = record;
		XjActorAccessor.SetLong(sovereign, XjActorDataKeys.XianGuoDynastyId, dynastyId);
		XjActorAccessor.SetInt(sovereign, XjActorDataKeys.XianGuoBorrowedTier, 0);
		XjActorAccessor.SetInt(sovereign, XjActorDataKeys.XianGuoBorrowedGrade, 0);
		XjWorldArchiveSystem.MarkChanged();

		RefreshDynasty(sovereign, record, Math.Max(1, year));
		if (!string.Equals(source, "dynastic_change", StringComparison.Ordinal)
			&& !string.Equals(source, "imperial_restoration", StringComparison.Ordinal))
		{
			string body = "【仙国肇基】" + record.SovereignName + "以帝明阳统摄" + record.DynastyName
				+ "，自此以国为玄、以众为基，开仙国法。";
			RecordPoliticalHistory("仙国肇基", body, Math.Max(1, year), actorId, record.SovereignName, 0L, string.Empty);
			XjBroadcastSystem.ShowRecordedWorldTipCritical(body, color: "#D9B36C", iconId: XjEventIconCatalog.HistoryWorld);
		}
		return record;
	}

	private static void RefreshDynasty(Actor sovereign, XjXianGuoRecord record, int year)
	{
		if (record == null || !record.Active || sovereign?.data == null) return;
		if (record.LastAnnualYear == year) return;

		// 帝君是仙国法统的授命源头，绝不承受百官国命。旧档若留下重臣/持玄
		// 标记，在王朝年度结算入口先行清退，避免人物页继续出现【国之重臣】。
		if (HasInstitutionalPatronageMarker(sovereign))
			ClearInstitutionalProjection(sovereign, syncVisibleTraits: true);
		SyncCourtIdentityTrait(sovereign);

		long actorId = ((BaseSystemData)sovereign.data).id;
		if (record.KingdomId <= 0L
			|| !XjWorldLookupIndex.TryResolveKingdom(record.KingdomId, out Kingdom kingdom)
			|| kingdom?.data == null || !kingdom.isCiv())
		{
			EndDynasty(sovereign, record, year, "国朝实体已亡，仙国法统随之断绝");
			return;
		}

		bool stillInKingdom = sovereign.kingdom?.data?.id == record.KingdomId;
		bool nativeSovereignKnown = XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor nativeSovereign);
		bool nativeSovereignMatches = nativeSovereignKnown && SameActor(nativeSovereign, sovereign);
		bool nativeProjectionHealthy = stillInKingdom && nativeSovereignMatches;
		if (!nativeProjectionHealthy)
		{
			// 年度事务只发现不一致并排队；真正的原生政治写回在独立后台车道完成。
			EnqueueNativeProjectionRepair(record);
		}
		if (nativeProjectionHealthy)
		{
			record.NativeSovereignMismatchYears = 0;
		}
		else
		{
			record.NativeSovereignMismatchYears = Math.Max(1, record.NativeSovereignMismatchYears + 1);
			// 这里不能return：疆土、国势、朝廷、持玄都属于仙朝档案，原生King
			// 投影一时修不回也不应让整个制度停摆。
		}

		// 百官不再依赖原生城主/忠诚脚本。WorldBox只提供疆土人口与王位事实，
		// 仙朝官位、玄秩和持玄全部由独立朝廷档案维护。

		NationAggregate aggregate = ResolveAnnualNationAggregate(record.KingdomId, year);
		int previousCities = record.CityCount;
		int previousPopulation = record.Population;
		record.CityCount = aggregate?.CityCount ?? 0;
		record.Population = aggregate?.Population ?? 0;
		record.NationalPotential = CalculateNationalPotential(sovereign, record.Population, record.CityCount, _annualWorldPopulation);

		if (record.LastAnnualYear <= 0)
		{
			record.NationalFortune = Math.Min(record.NationalPotential, Math.Max(1800, (int)Math.Round(record.NationalPotential * 0.52d)));
			record.StableYears = 1;
			UpdateImperialImages(record, 0, 0);
		}
		else
		{
			int cityDelta = record.CityCount - previousCities;
			int populationDelta = record.Population - previousPopulation;
			int delta = 60;
			if (cityDelta > 0) delta += Math.Min(900, cityDelta * 260);
			else if (cityDelta < 0) delta -= Math.Min(2400, -cityDelta * 700);
			if (populationDelta > 0) delta += Math.Min(300, populationDelta / 3);
			else if (populationDelta < 0) delta -= Math.Min(900, (-populationDelta) / 2);
			if (cityDelta >= 0 && populationDelta >= -Math.Max(20, previousPopulation / 20))
			{
				record.StableYears = Math.Min(500, record.StableYears + 1);
				delta += Math.Min(100, record.StableYears / 4);
			}
			else
			{
				record.StableYears = Math.Max(0, record.StableYears - 5);
			}
			if (record.NationalFortune < record.NationalPotential)
			{
				delta += Math.Max(0, (record.NationalPotential - record.NationalFortune) / 40);
			}
			record.NationalFortune = Math.Clamp(record.NationalFortune + delta, 0, record.NationalPotential);
			UpdateImperialImages(record, cityDelta, populationDelta);
		}

		ApplyImperialAptitudeUplift(sovereign, record);
		string currentRealmForName = XjRealmHelper.GetUnifiedId(sovereign, XjRealmHelper.GetTraitSnapshotForRouter);
		XjRealmTitleApplyService.EnsureTitleForRealm(sovereign, currentRealmForName, MingYangDaoTu);
		// 王号会在证得真金时改成帝君尊号；仙国档案同步跟随当下真实称号，
		// 后续返真、王统与史册公告不再继续使用旧的“X王”名号。
		record.SovereignName = XjStringHelper.ActorName(sovereign, "未名帝明阳修士");

		int sovereignRealTier = XjRealmSuppression.GetRealmTier(sovereign);
		// 旧档可能还残留帝明阳本人“假金丹”的 borrowed 字段；在仙朝年度入口
		// 无条件迁移清零。百官假金丹继续使用 CourtFakeJinDanActive 独立结算。
		SetBorrowedState(sovereign, record, false, 0);
		bool courtFakeJinDan = IsCourtFakeJinDanReady(record, year, sovereignRealTier);
		record.CourtFakeJinDanActive = courtFakeJinDan;
		record.CourtBorrowedCombatGrade = courtFakeJinDan ? ResolveBorrowedCombatGrade(record) : 0;
		XjXianGuoCourtSystem.Reconcile(record, sovereign, year);
		record.DiHuang = Math.Clamp(record.NationalPotential / 100 + (courtFakeJinDan ? 8 : 0), 0, 100);
		record.Status = !nativeProjectionHealthy
			? "仙朝行法（王统映照待正）"
			: sovereignRealTier >= XjRealmSuppression.TierJinDan ? "仙国行法（帝君临朝）" : "仙国行法";
		record.LastAnnualYear = year;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool HasPendingNativeProjectionRepairs => NativeProjectionRepairQueue.Count > 0;

	private static void EnqueueNativeProjectionRepair(XjXianGuoRecord record)
	{
		if (record == null || !record.Active || record.DynastyId <= 0L) return;
		if (!QueuedNativeProjectionRepairIds.Add(record.DynastyId)) return;
		NativeProjectionRepairQueue.Enqueue(record.DynastyId);
	}

	/// <summary>
	/// 一次只消费一个兼容王位写回。失败不立即重排，下一次年度校验仍不一致时再入队，
	/// 避免和原生政治AI互相抢写形成高频抖动。
	/// </summary>
	internal static void TickNativeProjectionRepair()
	{
		if (NativeProjectionRepairQueue.Count == 0) return;
		long dynastyId = NativeProjectionRepairQueue.Dequeue();
		QueuedNativeProjectionRepairIds.Remove(dynastyId);
		if (dynastyId <= 0L || !ByDynastyId.TryGetValue(dynastyId, out XjXianGuoRecord record)
			|| record == null || !record.Active || record.SovereignActorId <= 0L || record.KingdomId <= 0L) return;
		if (!XjActorRegistry.ResolveKnownOrWorld(record.SovereignActorId, out Actor sovereign)
			|| sovereign?.data == null || !sovereign.isAlive() || !IsDiMingYang(sovereign)) return;
		if (!XjWorldLookupIndex.TryResolveKingdom(record.KingdomId, out Kingdom kingdom)
			|| kingdom?.data == null || !kingdom.isCiv()) return;

		bool stillInKingdom = sovereign.kingdom?.data?.id == record.KingdomId;
		bool nativeMatches = XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor current)
			&& SameActor(current, sovereign);
		if (stillInKingdom && nativeMatches)
		{
			record.NativeSovereignMismatchYears = 0;
			return;
		}

		if (TryRepairInstitutionalSovereignProjection(sovereign, record, kingdom))
		{
			record.NativeSovereignMismatchYears = 0;
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	/// <summary>
	/// 仙朝法统向WorldBox政治层写回一个“兼容国王投影”。此写回不是王统权威，
	/// 只让原生战争/城市AI继续认得帝君。失败只记状态，不销毁仙朝档案。
	/// </summary>
	private static bool TryRepairInstitutionalSovereignProjection(Actor sovereign, XjXianGuoRecord record, Kingdom expectedKingdom)
	{
		if (sovereign?.data == null || !sovereign.isAlive() || record == null || !record.Active
			|| expectedKingdom?.data == null || expectedKingdom.data.id != record.KingdomId) return false;
		// 原生所属国漂移时绝不能拿仙朝档案强行把人从另一座城/另一国家拖回来。
		// 仅当“角色当前城市本身仍属于旧朝”时，才把 kingdom 指针视为可安全修复的
		// 原生半状态；这与春秋的安全修复原则一致：从仍然有效的 native city 事实
		// 推导 kingdom，而不是反过来改城、改居民或改寻路。
		if (sovereign.kingdom?.data?.id != record.KingdomId)
		{
			City currentCity = sovereign.city;
			bool cityStillDomestic = currentCity?.data != null
				&& currentCity.kingdom?.data?.id == record.KingdomId;
			if (!cityStillDomestic) return false;
			XjWorldBoxKingdomBridge.TryRepairActorKingdomFromCurrentCity(sovereign, expectedKingdom);
			if (sovereign.kingdom?.data?.id != record.KingdomId) return false;
		}
		bool repaired = XjNativeKingdomSovereignWriteBridge.TrySetExistingKingdomSovereign(sovereign, out _);
		return repaired && IsNativeSovereign(sovereign);
	}

	private static void UpdateImperialImages(XjXianGuoRecord record, int cityDelta, int populationDelta)
	{
		if (record == null) return;
		record.TianGuang = Math.Clamp(record.NationalFortune / 100, 0, 100);
		record.JunChen = Math.Clamp(record.CityCount * 14 + record.StableYears / 2, 0, 100);
		record.DiHuang = Math.Clamp(record.NationalPotential / 100 + (record.CourtFakeJinDanActive ? 8 : 0), 0, 100);
		int ziYanDelta = cityDelta > 0 ? cityDelta * 8 : populationDelta > 100 ? 2 : -1;
		record.ZiYan = Math.Clamp(record.ZiYan + ziYanDelta, 0, 100);
		// “父子相杀/谋逆”保留为政治事件入口，不靠年度随机虚构。只有后续真实弑亲、夺位事件才能写入。
		record.FuZiXiangSha = Math.Clamp(record.FuZiXiangSha, 0, 100);
		record.MouNi = Math.Clamp(record.MouNi, 0, 100);
	}


	private static bool IsCourtFakeJinDanReady(XjXianGuoRecord record, int year, int sovereignRealTier)
	{
		if (record == null || !record.Active || sovereignRealTier < XjRealmSuppression.TierZiFu
			|| record.CityCount < FakeJinDanMinimumCities
			|| record.Population < FakeJinDanMinimumPopulation
			|| record.NationalPotential < FakeJinDanMinimumPotential
			|| record.NationalFortune < FakeJinDanMinimumFortune
			|| year - record.FoundedYear < FakeJinDanMinimumDynastyAge) return false;
		// 帝君本人证得真金以后，百官的“众玄归一”不会反而消失；这本就是
		// 王朝制度位格，而不是皇帝本人假丹状态的附属字段。
		return true;
	}

	private static int ResolveCourtBorrowedCombatGrade(XjXianGuoRecord record)
	{
		if (record == null || !record.CourtFakeJinDanActive) return 0;
		if (record.CourtBorrowedCombatGrade >= FakeJinDanMinimumGrade)
			return Math.Clamp(record.CourtBorrowedCombatGrade, FakeJinDanMinimumGrade, FakeJinDanMaximumGrade);
		return ResolveBorrowedCombatGrade(record);
	}

	private static int ResolveBorrowedCombatGrade(XjXianGuoRecord record)
	{
		int effective = Math.Min(record.NationalPotential, record.NationalFortune);
		int extra = Math.Clamp((effective - FakeJinDanMinimumFortune) / 75, 0, FakeJinDanMaximumGrade - FakeJinDanMinimumGrade);
		return FakeJinDanMinimumGrade + extra;
	}

	private static int CalculateNationalPotential(Actor sovereign, int population, int cityCount, int worldPopulation)
	{
		int safePopulation = Math.Max(0, population);
		int safeCities = Math.Max(0, cityCount);
		int safeWorldPopulation = Math.Max(safePopulation, worldPopulation);

		// 适配玄鉴当前约3000~5000总人口的实机上限：人口本身采用快速边际递减，
		// 同时加入“占全世界人口份额”而非死盯万人规模。这样800~1200人的强国
		// 在三五千人口世界已足以称雄，而单城堆人口或大量空城都不能直接刷满国势。
		double populationScore = 3000d * (1d - Math.Exp(-safePopulation / 650d));
		int cityScore = Math.Min(3000, safeCities * 650);
		double share = safeWorldPopulation > 0 ? safePopulation / (double)safeWorldPopulation : 0d;
		int worldShareScore = Math.Min(1800, (int)Math.Round(1800d * Math.Sqrt(Math.Clamp(share, 0d, 1d))));
		int realTier = XjRealmSuppression.GetRealmTier(sovereign);
		int sovereignFoundation = realTier >= XjRealmSuppression.TierJinDan ? 800 : 500;
		return Math.Clamp((int)Math.Round(900d + populationScore + cityScore + worldShareScore + sovereignFoundation), 0, 10000);
	}

	private static NationAggregate ResolveAnnualNationAggregate(long kingdomId, int year)
	{
		EnsureAnnualNationSnapshot(year);
		return kingdomId > 0L && AnnualNationAggregates.TryGetValue(kingdomId, out NationAggregate aggregate)
			? aggregate
			: null;
	}

	private static void EnsureAnnualNationSnapshot(int year)
	{
		if (_nationAggregateYear == year) return;
		AnnualNationAggregates.Clear();
		_annualWorldPopulation = 0;
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < cities.Count; i++)
		{
			City city = cities[i];
			long kingdomId = city?.kingdom?.data?.id ?? 0L;
			if (kingdomId <= 0L) continue;
			if (!AnnualNationAggregates.TryGetValue(kingdomId, out NationAggregate aggregate))
			{
				aggregate = new NationAggregate();
				AnnualNationAggregates.Add(kingdomId, aggregate);
			}
			int cityPopulation = Math.Max(0, city.units?.Count ?? 0);
			aggregate.CityCount++;
			aggregate.Population += cityPopulation;
			_annualWorldPopulation += cityPopulation;
		}
		_nationAggregateYear = year;
	}


	private static void SetBorrowedState(Actor actor, XjXianGuoRecord record, bool active, int grade)
	{
		if (record == null) return;
		int normalizedGrade = active ? Math.Clamp(grade, FakeJinDanMinimumGrade, FakeJinDanMaximumGrade) : 0;
		bool changed = record.FakeJinDanActive != active || record.BorrowedCombatGrade != normalizedGrade;
		record.FakeJinDanActive = active;
		record.BorrowedCombatGrade = normalizedGrade;
		if (actor?.data != null)
		{
			int tier = active ? XjRealmSuppression.TierJinDan : 0;
			bool actorChanged = !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoBorrowedTier, out int oldTier)
				|| oldTier != tier
				|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XianGuoBorrowedGrade, out int oldGrade)
				|| oldGrade != normalizedGrade;
			if (actorChanged)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoBorrowedTier, tier);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XianGuoBorrowedGrade, normalizedGrade);
			}
			if (changed || actorChanged) XjCombatHotPathCache.Refresh(actor);
		}
	}

	private static void EndDynasty(Actor actor, XjXianGuoRecord record, int year, string reason)
	{
		if (record == null) return;
		record.Active = false;
		record.EndedYear = Math.Max(record.FoundedYear, year);
		record.Status = string.IsNullOrWhiteSpace(reason) ? "仙国法统已断" : reason.Trim();
		record.CourtFakeJinDanActive = false;
		record.CourtBorrowedCombatGrade = 0;
		SetBorrowedState(actor, record, false, 0);
		ActiveBySovereignId.Remove(record.SovereignActorId);
		if (record.KingdomId > 0L
			&& ActiveByKingdomId.TryGetValue(record.KingdomId, out XjXianGuoRecord current)
			&& ReferenceEquals(current, record))
		{
			ActiveByKingdomId.Remove(record.KingdomId);
		}
		// 法统一断，仙朝官位整体退役，所有在任官员的持玄投影即时撤销。
		XjXianGuoCourtSystem.EndDynasty(record.DynastyId, Math.Max(1, year));
		if (actor?.data != null)
		{
			XjActorAccessor.SetLong(actor, XjActorDataKeys.XianGuoDynastyId, 0L);
			if (IsDiMingYang(actor))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.DiMingYangNextKingAttemptYear, Math.Max(year + ImperialRestorationIntervalYears, 1));
			}
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	private static bool TryGetActiveRecord(long actorId, out XjXianGuoRecord record)
	{
		record = null;
		if (actorId <= 0L) return false;
		return ActiveBySovereignId.TryGetValue(actorId, out record)
			&& record != null
			&& record.Active;
	}

	private static long NextDynastyId(long actorId, long kingdomId, int year)
	{
		long candidate = XjDeterministicHash.PositiveHash(actorId ^ kingdomId ^ year, "xianguo.dynasty.v1");
		if (candidate <= 0L || ByDynastyId.ContainsKey(candidate))
		{
			candidate = Math.Max(1L, _nextDynastyId);
			while (ByDynastyId.ContainsKey(candidate)) candidate++;
		}
		_nextDynastyId = Math.Max(_nextDynastyId, candidate + 1L);
		return candidate;
	}

	private static string BuildDynastyName(Actor actor, City capital)
	{
		string cityName = capital?.data?.name;
		if (!string.IsNullOrWhiteSpace(cityName))
		{
			string stem = cityName.Trim();
			if (stem.EndsWith("城", StringComparison.Ordinal) && stem.Length > 1) stem = stem.Substring(0, stem.Length - 1);
			return XjNativeMetaNameSinicizer.ResolveCanonicalKingdomName(stem);
		}
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		string[] names = { "昭", "宸", "重光", "承天", "明庭", "曜" };
		return XjNativeMetaNameSinicizer.ResolveCanonicalKingdomName(
			names[XjDeterministicHash.PositiveIndex(actorId, "xianguo.name", names.Length)]);
	}

	private static bool SameActor(Actor left, Actor right)
	{
		if (left?.data == null || right?.data == null) return false;
		return ((BaseSystemData)left.data).id == ((BaseSystemData)right.data).id;
	}

	internal static void PruneHistoricalState(int currentYear, bool hardCompact)
	{
		if (currentYear <= 0) return;
		bool changed = false;

		// Closed political plans are already summarized into world history/century
		// chronicles. Keep a generous recent window for diagnostics, but do not let
		// centuries of failed lord schemes inflate every archive snapshot forever.
		int planCutoff = currentYear - (hardCompact ? 80 : 160);
		for (int i = PoliticalPlans.Count - 1; i >= 0; i--)
		{
			XjXianGuoPoliticalPlan plan = PoliticalPlans[i];
			if (plan == null)
			{
				PoliticalPlans.RemoveAt(i);
				changed = true;
				continue;
			}
			if (plan.Active) continue;
			int closedYear = plan.CompletedYear > 0 ? plan.CompletedYear : Math.Max(plan.LastActionYear, plan.LastProgressYear);
			if (closedYear > 0 && closedYear < planCutoff)
			{
				PoliticalPlans.RemoveAt(i);
				changed = true;
			}
		}

		if (hardCompact && Records.Count > 128)
		{
			int dynastyCutoff = currentYear - 1000;
			for (int i = 0; i < Records.Count && Records.Count > 128;)
			{
				XjXianGuoRecord record = Records[i];
				if (record == null || (!record.Active && record.EndedYear > 0 && record.EndedYear < dynastyCutoff))
				{
					if (record != null) ByDynastyId.Remove(record.DynastyId);
					Records.RemoveAt(i);
					changed = true;
					continue;
				}
				i++;
			}
		}

		if (changed) XjWorldArchiveSystem.MarkChanged();
	}

	internal static XjXianGuoArchiveData ExportState()
	{
		XjXianGuoArchiveData state = new XjXianGuoArchiveData
		{
			NextDynastyId = _nextDynastyId,
			NextPoliticalPlanId = _nextPoliticalPlanId,
			ActiveDiMingYangActorId = _activeDiMingYangActorId
		};
		for (int i = 0; i < Records.Count; i++)
		{
			if (Records[i] != null) state.Records.Add(Records[i].Clone());
		}
		for (int i = 0; i < PoliticalPlans.Count; i++)
		{
			if (PoliticalPlans[i] != null) state.PoliticalPlans.Add(PoliticalPlans[i].Clone());
		}
		state.CourtOffices = XjXianGuoCourtSystem.Export();
		return state;
	}

	internal static void ImportState(XjXianGuoArchiveData state)
	{
		ClearRuntime();
		if (state == null) return;
		_nextDynastyId = Math.Max(1L, state.NextDynastyId);
		_nextPoliticalPlanId = Math.Max(1L, state.NextPoliticalPlanId);
		_activeDiMingYangActorId = Math.Max(0L, state.ActiveDiMingYangActorId);
		if (state.Records != null)
		{
			for (int i = 0; i < state.Records.Count; i++)
			{
				XjXianGuoRecord source = state.Records[i];
				if (source == null || source.DynastyId <= 0L) continue;
				XjXianGuoRecord record = source.Clone();
				Records.Add(record);
				ByDynastyId[record.DynastyId] = record;
				_nextDynastyId = Math.Max(_nextDynastyId, record.DynastyId + 1L);
				if (!record.Active) continue;

				// 老档若同一原生国家意外留下多份Active记录，只保留更晚的一份，
				// 其余转为退役，避免一个Kingdom同时提供两份假丹法统。
				if (record.KingdomId > 0L
					&& ActiveByKingdomId.TryGetValue(record.KingdomId, out XjXianGuoRecord collision)
					&& collision != null && collision.Active)
				{
					XjXianGuoRecord keep = record.FoundedYear >= collision.FoundedYear ? record : collision;
					XjXianGuoRecord retire = ReferenceEquals(keep, record) ? collision : record;
					retire.Active = false;
					retire.FakeJinDanActive = false;
					retire.BorrowedCombatGrade = 0;
					retire.CourtFakeJinDanActive = false;
					retire.CourtBorrowedCombatGrade = 0;
					retire.Status = "旧档法统冲突已退役";
					ActiveByKingdomId[record.KingdomId] = keep;
				}
				else if (record.KingdomId > 0L)
				{
					ActiveByKingdomId[record.KingdomId] = record;
				}
			}
		}

		XjXianGuoRecord primaryImperial = null;
		foreach (XjXianGuoRecord record in Records)
		{
			if (record == null || !record.Active || record.SovereignActorId <= 0L) continue;
			if (record.KingdomId > 0L && ActiveByKingdomId.TryGetValue(record.KingdomId, out XjXianGuoRecord active)
				&& !ReferenceEquals(active, record)) continue;

			if (primaryImperial == null && (_activeDiMingYangActorId <= 0L || _activeDiMingYangActorId == record.SovereignActorId))
			{
				primaryImperial = record;
				ActiveBySovereignId[record.SovereignActorId] = record;
				if (_activeDiMingYangActorId <= 0L) _activeDiMingYangActorId = record.SovereignActorId;
				continue;
			}

			// RC11.22：旧档曾允许多名帝明阳并存。新口径世界同时只容一位，
			// 导入时即退役其余仙国法统，避免打开UI到第一次年度结算之间出现短暂多帝。
			record.Active = false;
			record.FakeJinDanActive = false;
			record.BorrowedCombatGrade = 0;
			record.Status = "旧档多帝已归一";
			if (record.KingdomId > 0L && ActiveByKingdomId.TryGetValue(record.KingdomId, out XjXianGuoRecord mapped)
				&& ReferenceEquals(mapped, record)) ActiveByKingdomId.Remove(record.KingdomId);
			if (XjActorRegistry.ResolveKnownOrWorld(record.SovereignActorId, out Actor duplicateActor) && duplicateActor?.data != null)
			{
				XjActorAccessor.SetLong(duplicateActor, XjActorDataKeys.XianGuoDynastyId, 0L);
				ClearDiMingYang(duplicateActor);
			}
		}

		if (state.PoliticalPlans != null)
		{
			for (int i = 0; i < state.PoliticalPlans.Count; i++)
			{
				XjXianGuoPoliticalPlan source = state.PoliticalPlans[i];
				if (source == null || source.PlanId <= 0L || source.ActorId <= 0L) continue;
				XjXianGuoPoliticalPlan plan = source.Clone();
				PoliticalPlans.Add(plan);
				_nextPoliticalPlanId = Math.Max(_nextPoliticalPlanId, plan.PlanId + 1L);
				if (!plan.Active) continue;
				if (!ActivePoliticalPlanByActorId.TryGetValue(plan.ActorId, out XjXianGuoPoliticalPlan existing)
					|| existing == null || plan.StartYear >= existing.StartYear)
				{
					if (existing != null) existing.Active = false;
					ActivePoliticalPlanByActorId[plan.ActorId] = plan;
				}
				else plan.Active = false;
			}
		}
		XjXianGuoCourtSystem.Import(state.CourtOffices);
		TryResolveImportedPendingSovereignty();
	}

	/// <summary>
	/// 旧档可能在“国主死亡快照”与王位实际回填之间保存，导致载入后现实国王已经
	/// 是帝明阳，档案却仍挂着“王统待定”。仅在国家和现任国王都能从当前世界
	/// 权威对象解析，且现任本身就是帝明阳时即时补一次继承事务；解析不到任何一环
	/// 就保持原状，绝不在载入阶段凭空结束王朝。
	/// </summary>
	private static void TryResolveImportedPendingSovereignty()
	{
		if (ActiveByKingdomId.Count == 0) return;
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		PendingSuccessionScratch.Clear();
		foreach (XjXianGuoRecord record in ActiveByKingdomId.Values)
		{
			if (record == null || !record.Active || !record.SuccessionPending || record.KingdomId <= 0L) continue;
			if (!XjWorldLookupIndex.TryResolveKingdom(record.KingdomId, out Kingdom kingdom) || kingdom?.data == null) continue;
			if (!XjNativeKingdomSovereignReadBridge.TryResolveSovereign(kingdom, out Actor sovereign)
				|| sovereign?.data == null || !sovereign.isAlive() || !IsDiMingYang(sovereign)) continue;
			PendingSuccessionScratch.Add(record);
		}
		for (int i = 0; i < PendingSuccessionScratch.Count; i++)
		{
			ResolvePendingSuccession(PendingSuccessionScratch[i], year);
		}
		PendingSuccessionScratch.Clear();
	}

	internal static void ClearRuntime()
	{
		Records.Clear();
		ByDynastyId.Clear();
		ActiveBySovereignId.Clear();
		ActiveByKingdomId.Clear();
		PoliticalPlans.Clear();
		ActivePoliticalPlanByActorId.Clear();
		AnnualNationAggregates.Clear();
		NativeProjectionRepairQueue.Clear();
		QueuedNativeProjectionRepairIds.Clear();
		_nationAggregateYear = -1;
		_annualWorldPopulation = 0;
		_nextDynastyId = 1L;
		_nextPoliticalPlanId = 1L;
		_activeDiMingYangActorId = 0L;
		_restoringDiMingYangActorId = 0L;
		XjXianGuoCourtSystem.Clear();
		XjNativeKingdomSovereignReadBridge.Clear();
	}
}

internal sealed class XjXianGuoArchiveData
{
	public long NextDynastyId { get; set; } = 1L;
	public long NextPoliticalPlanId { get; set; } = 1L;
	public long ActiveDiMingYangActorId { get; set; }
	public List<XjXianGuoRecord> Records { get; set; } = new List<XjXianGuoRecord>();
	public List<XjXianGuoPoliticalPlan> PoliticalPlans { get; set; } = new List<XjXianGuoPoliticalPlan>();
	public List<XjXianGuoCourtOfficeRecord> CourtOffices { get; set; } = new List<XjXianGuoCourtOfficeRecord>();
}

internal sealed class XjXianGuoPoliticalPlan
{
	public long PlanId { get; set; }
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public string PlanType { get; set; } = string.Empty;
	public long TargetKingdomId { get; set; }
	public string TargetKingdomName { get; set; } = string.Empty;
	public long TargetSovereignActorId { get; set; }
	public string TargetSovereignName { get; set; } = string.Empty;
	public long OriginCityId { get; set; }
	public int StartYear { get; set; }
	public int LastProgressYear { get; set; }
	public int LastActionYear { get; set; }
	public int CompletedYear { get; set; }
	public int Progress { get; set; }
	public int ProgressNeeded { get; set; } = 100;
	public string Status { get; set; } = string.Empty;
	public string Outcome { get; set; } = string.Empty;
	public bool TargetIsParent { get; set; }
	public bool ViolentClaim { get; set; }
	public bool Active { get; set; }

	internal XjXianGuoPoliticalPlan Clone()
	{
		return (XjXianGuoPoliticalPlan)MemberwiseClone();
	}
}

internal sealed class XjXianGuoRecord
{
	public long DynastyId { get; set; }
	public string DynastyName { get; set; } = string.Empty;
	public long FounderActorId { get; set; }
	public long SovereignActorId { get; set; }
	public string SovereignName { get; set; } = string.Empty;
	public long KingdomId { get; set; }
	public long CapitalCityId { get; set; }
	public int FoundedYear { get; set; }
	public int EndedYear { get; set; }
	public int LastAnnualYear { get; set; }
	public int LastCourtAppointmentYear { get; set; }
	public int CityCount { get; set; }
	public int Population { get; set; }
	public int NationalPotential { get; set; }
	public int NationalFortune { get; set; }
	public int StableYears { get; set; }
	public int NativeSovereignMismatchYears { get; set; }
	public bool Active { get; set; }
	public bool FakeJinDanActive { get; set; }
	public int BorrowedCombatGrade { get; set; }
	public bool CourtFakeJinDanActive { get; set; }
	public int CourtBorrowedCombatGrade { get; set; }
	public int TianGuang { get; set; }
	public int ZiYan { get; set; }
	public int JunChen { get; set; }
	public int DiHuang { get; set; }
	public int FuZiXiangSha { get; set; }
	public int MouNi { get; set; }
	public bool SuccessionPending { get; set; }
	public int SuccessionStartedYear { get; set; }
	public long PreviousSovereignActorId { get; set; }
	public string PreviousSovereignName { get; set; } = string.Empty;
	public long LastKillerActorId { get; set; }
	public string LastKillerName { get; set; } = string.Empty;
	public int LastPoliticalEventYear { get; set; }
	public int DynastyGeneration { get; set; } = 1;
	public long PredecessorDynastyId { get; set; }
	public string Status { get; set; } = string.Empty;
	public string FoundingSource { get; set; } = string.Empty;

	internal XjXianGuoRecord Clone()
	{
		return (XjXianGuoRecord)MemberwiseClone();
	}
}

internal readonly struct XjXianGuoOfficialSummary
{
	internal readonly long CityId;
	internal readonly string CityName;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string RealRealm;
	internal readonly string Projection;
	internal readonly int Grade;
	internal readonly string OfficeName;
	internal readonly string XuanRank;
	internal readonly int Rank;
	internal readonly int TrueFate;
	internal readonly int NationalFate;
	internal readonly int EffectiveFate;
	internal readonly bool HeavyMinister;

	internal XjXianGuoOfficialSummary(long cityId, string cityName, long actorId, string actorName,
		string realRealm, string projection, int grade, string officeName, string xuanRank, int rank,
		int trueFate, int nationalFate, int effectiveFate, bool heavyMinister)
	{
		CityId = cityId;
		CityName = cityName ?? string.Empty;
		ActorId = actorId;
		ActorName = actorName ?? string.Empty;
		RealRealm = realRealm ?? string.Empty;
		Projection = projection ?? string.Empty;
		Grade = grade;
		OfficeName = officeName ?? string.Empty;
		XuanRank = xuanRank ?? string.Empty;
		Rank = rank;
		TrueFate = Math.Max(0, trueFate);
		NationalFate = Math.Max(0, nationalFate);
		EffectiveFate = Math.Max(0, effectiveFate);
		HeavyMinister = heavyMinister;
	}
}

internal readonly struct XjXianGuoSummary
{
	internal readonly long DynastyId;
	internal readonly string DynastyName;
	internal readonly long KingdomId;
	internal readonly long CapitalCityId;
	internal readonly long SovereignActorId;
	internal readonly string SovereignName;
	internal readonly int FoundedYear;
	internal readonly int StableYears;
	internal readonly int DynastyGeneration;
	internal readonly string Status;
	internal readonly bool SuccessionPending;
	internal readonly int SuccessionStartedYear;
	internal readonly int CityCount;
	internal readonly int Population;
	internal readonly int NationalPotential;
	internal readonly int NationalFortune;
	internal readonly bool CourtFakeJinDanActive;
	internal readonly int CourtBorrowedCombatGrade;
	internal readonly int TianGuang;
	internal readonly int ZiYan;
	internal readonly int JunChen;
	internal readonly int DiHuang;
	internal readonly int FuZiXiangSha;
	internal readonly int MouNi;

	internal XjXianGuoSummary(
		long dynastyId, string dynastyName, long kingdomId, long capitalCityId,
		long sovereignActorId, string sovereignName, int foundedYear, int stableYears,
		int dynastyGeneration, string status, bool successionPending, int successionStartedYear,
		int cityCount, int population, int nationalPotential, int nationalFortune, bool courtFakeJinDanActive,
		int courtBorrowedCombatGrade, int tianGuang, int ziYan, int junChen, int diHuang, int fuZiXiangSha, int mouNi)
	{
		DynastyId = dynastyId;
		DynastyName = dynastyName ?? string.Empty;
		KingdomId = kingdomId;
		CapitalCityId = capitalCityId;
		SovereignActorId = sovereignActorId;
		SovereignName = sovereignName ?? string.Empty;
		FoundedYear = foundedYear;
		StableYears = stableYears;
		DynastyGeneration = Math.Max(1, dynastyGeneration);
		Status = status ?? string.Empty;
		SuccessionPending = successionPending;
		SuccessionStartedYear = successionStartedYear;
		CityCount = cityCount;
		Population = population;
		NationalPotential = nationalPotential;
		NationalFortune = nationalFortune;
		CourtFakeJinDanActive = courtFakeJinDanActive;
		CourtBorrowedCombatGrade = courtBorrowedCombatGrade;
		TianGuang = tianGuang;
		ZiYan = ziYan;
		JunChen = junChen;
		DiHuang = diHuang;
		FuZiXiangSha = fuZiXiangSha;
		MouNi = mouNi;
	}
}
