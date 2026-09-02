using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.YaoShu;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 道胎功绩只记录可被世界观察到的高境行为，不做全世界逐年扫描。
/// 功绩达到门槛只是取得“尝试道胎”的资格，实际突破仍由道胎晋升系统裁定。
/// </summary>
internal static class XjDaoTaiMeritSystem
{
	internal const int RequiredMerit = 1000;
	internal const int PeakAuthorityBonusPerCoreAuthorityPercent = 2;
	internal const int PeakAuthorityBonusPerDerivedAuthorityPercent = 1;
	internal const int PeakAuthorityBonusMaxPercent = 20;
	internal const int FirstHighRealmMerit = 300;
	internal const int FirstZhengWeiMerit = 200;
	internal const int OpenDaoMerit = 200;
	internal const int HighRealmProofMerit = 120;
	internal const int ZhengWeiHoldingMerit = 120;
	internal const int YuWeiHoldingMerit = 80;
	internal const int RunWeiHoldingMerit = 60;
	internal const int ZhenRenKillMerit = 40;
	internal const int CrossDaoTuHighRealmKillMerit = 80;
	internal const int DaoTaiKillMerit = 160;
	internal const int ZhengWeiKillBonus = 40;
	internal const int LongShuKillMerit = 80;
	internal const int GreatSageKillMerit = 120;
	internal const int DefeatYinSiMerit = 100;

	private static readonly HashSet<string> FirstProvenDaoTus = new HashSet<string>(StringComparer.Ordinal);
	private static readonly HashSet<string> FirstZhengWeiDaoTus = new HashSet<string>(StringComparer.Ordinal);

	internal static int GetMerit(Actor actor)
	{
		if (actor?.data == null) return 0;
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiMerit, out int value)
			? Math.Max(0, value)
			: 0;
	}

	internal static void ObserveHighRealmPromotion(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null) return;
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		if ((!string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal)
				&& !string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
			|| string.Equals(normalizedRealm, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return;
		}

		ReconcileHighRealmStatus(actor, currentYear);

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}
		daoTu = daoTu.Trim();

		if (FirstProvenDaoTus.Add(daoTu))
		{
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.HighRealm);
			string proofTitle = string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				? "真君羽士"
				: "金丹真君";
			Award(actor, FirstHighRealmMerit, "成为" + daoTu + "首位证道" + proofTitle, currentYear);
			if (XjZiJinSwordDaoCatalog.IsLongGeng(daoTu)
				|| XjQingXuanKongZhengSystem.IsQingXuanDaoTu(daoTu))
			{
				Award(actor, OpenDaoMerit, "为天地开续" + daoTu + "道途", currentYear);
			}
		}

		string guoWei = ResolveGuoWei(actor);
		if ((guoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& FirstZhengWeiDaoTus.Add(daoTu))
		{
			XjWorldArchiveSystem.MarkChanged(XjWorldArchiveSection.HighRealm);
			Award(actor, FirstZhengWeiMerit, "成为" + daoTu + "首位果位持有者", currentYear);
		}

		RefreshQualification(actor);
	}

	internal static void ObserveHighRealmDeath(in XjDeathSnapshot snapshot, XjDeathCause cause)
	{
		bool victimIsLongShu = (snapshot.RaceKey ?? string.Empty)
			.IndexOf("longshu", StringComparison.OrdinalIgnoreCase) >= 0;
		bool victimIsGreatSage = XjYaoShuGreatSageSystem.IsGreatSageRaceKey(snapshot.RaceKey);
		string victimRealm = XjRealmHelper.NormalizeId(snapshot.RealmId);
		bool victimIsSupportedHighRealm = XjHighRealmIdentity.IsHighRealm(victimRealm);
		if (cause != XjDeathCause.Combat
			|| !snapshot.Found
			|| snapshot.LastAttackerId <= 0L
			|| snapshot.LastAttackerId == snapshot.ActorId
			|| (!victimIsLongShu && !victimIsGreatSage && !victimIsSupportedHighRealm)
			|| !XjActorRegistry.ResolveKnownOrWorld(snapshot.LastAttackerId, out Actor killer)
			|| !XjSafeCore.IsAliveActor(killer)
			|| !(victimIsGreatSage ? IsEligibleGreatSageKiller(killer) : IsEligibleMeritBearer(killer)))
		{
			return;
		}

		if (victimIsLongShu)
		{
			Award(
				killer,
				LongShuKillMerit,
				"斩落龙属" + (string.IsNullOrWhiteSpace(snapshot.Name) ? string.Empty : "“" + snapshot.Name + "”"),
				snapshot.Year);
			return;
		}
		if (victimIsGreatSage)
		{
			Award(killer, GreatSageKillMerit,
				"斩落妖属大圣" + (string.IsNullOrWhiteSpace(snapshot.Name) ? string.Empty : "“" + snapshot.Name + "”"),
				snapshot.Year);
			return;
		}

		XjActorAccessor.TryGetString(killer, XjActorDataKeys.DaoTu, out string killerDaoTu);
		string victimDaoTu = (snapshot.DaoTu ?? string.Empty).Trim();
		killerDaoTu = (killerDaoTu ?? string.Empty).Trim();
		if (killerDaoTu.Length == 0 || victimDaoTu.Length == 0) return;

		bool sameDaoTu = string.Equals(killerDaoTu, victimDaoTu, StringComparison.Ordinal);
		bool warParticipantKill = XjQuanBingStruggleSystem.IsParticipantKill(killer, snapshot.ActorId);
		float firstKillMultiplier = XjQuanBingStruggleSystem.TryConsumeFirstKillMeritMultiplier(killer, snapshot.ActorId);
		if (sameDaoTu && !warParticipantKill)
		{
			// 常态同道相残不记天地功绩；五十年权柄之争期间，所有参与者之间
			// 的有效击杀都按道争结算，首次斩敌另得1.25倍奖励。
			return;
		}

		int amount = string.Equals(victimRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(victimRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
				? DaoTaiKillMerit
				: XjHighRealmIdentity.IsZhenRen(victimRealm) ? ZhenRenKillMerit : CrossDaoTuHighRealmKillMerit;
		if ((snapshot.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			amount += ZhengWeiKillBonus;
		}
		string victimTitle = string.Equals(victimRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(victimRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
				? "道胎"
				: XjHighRealmIdentity.IsZhenRen(victimRealm) ? "真人" : "真君";
		string reason = (warParticipantKill ? (sameDaoTu ? "权柄之争斩落同争" : "权柄之争斩落异途") : "斩落异途") + victimTitle
			+ (string.IsNullOrWhiteSpace(snapshot.Name) ? string.Empty : "“" + snapshot.Name + "”");
		if (firstKillMultiplier > 1f)
		{
			amount = Math.Max(amount + 1, (int)Math.Ceiling(amount * firstKillMultiplier));
			reason += "（首次斩敌功绩×1.25）";
		}
		Award(killer, amount, reason, snapshot.Year);
	}

	internal static void AwardForDefeatingYinSi(long actorId, int currentYear)
	{
		if (actorId > 0L && XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && XjSafeCore.IsAliveActor(actor))
		{
			Award(actor, DefeatYinSiMerit, "连斩本轮阴司追索", currentYear);
		}
	}

	internal static bool CanAttemptDaoTai(Actor actor, out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null)
		{
			reason = "角色无效";
			return false;
		}
		int merit = GetMerit(actor);
		if (merit < RequiredMerit)
		{
			reason = "功绩不足：当前" + merit + " · 门槛" + RequiredMerit;
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		if (!string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			reason = "须为金丹或真君羽士";
			return false;
		}
		if (!IsPeakHighRealmRoute(actor))
		{
			reason = "须至金丹巅峰或真君羽士巅峰";
			return false;
		}

		string guoWei = ResolveGuoWei(actor);
		if (!TryResolveDaoTaiPositionKind(guoWei, out _))
		{
			reason = "须持有一道果位、余位或闰位";
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState authorityState)
			&& authorityState.Found
			&& authorityState.IntegrationRetreatActive)
		{
			reason = "外道权柄尚在合道闭关";
			return false;
		}
		if (XjYinSiExposurePursuitSystem.TryGetActiveMissionForTarget(actorId, out _))
		{
			reason = "仍在阴司追索中";
			return false;
		}

		reason = "已具道胎尝试资格";
		return true;
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		// 沿用RC11.1的权柄分类口径，只调整倍率：核心+2%，衍生+1%，
		// 权柄总加成最多20个百分点，对应成胎总成功率最高30%。
		int merit = GetMerit(actor);
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			// 已成道胎后“门槛/成胎把握”都属于历史过程，不再继续挂在人物面板。
			return "累计" + merit.ToString();
		}

		int authorityBonus = CalculateAuthorityBreakthroughBonusPercent(actor);
		int totalChance = Math.Min(30, 10 + authorityBonus);
		string authorityText = authorityBonus <= 0 ? "权柄未助" : DescribeAuthorityAid(authorityBonus);
		return "当前" + merit.ToString() + " / 门槛" + RequiredMerit
			+ "\n成胎把握：" + DescribeAscensionChance(totalChance) + "　" + authorityText;
	}

	private static string DescribeAscensionChance(int chance)
	{
		if (chance <= 10) return "艰难";
		if (chance <= 15) return "微有转机";
		if (chance <= 20) return "渐有把握";
		if (chance <= 25) return "把握渐厚";
		return "道势大助";
	}

	private static string DescribeAuthorityAid(int bonus)
	{
		if (bonus <= 4) return "权柄微助";
		if (bonus <= 8) return "权柄渐助";
		if (bonus <= 14) return "权柄显助";
		return "权柄厚助";
	}

	internal static int CalculateAuthorityBreakthroughBonusPercent(Actor actor)
	{
		if (actor?.data == null || !IsPeakHighRealmRoute(actor))
		{
			return 0;
		}

		int authorityBonus = CalculateAuthorityBreakthroughBonusCore(actor);
		return authorityBonus <= 0 ? 0 : Math.Min(PeakAuthorityBonusMaxPercent, authorityBonus);
	}

	internal static XjDaoTaiMeritWorldArchiveData ExportState()
	{
		return new XjDaoTaiMeritWorldArchiveData
		{
			FirstProvenDaoTus = new List<string>(FirstProvenDaoTus),
			FirstZhengWeiDaoTus = new List<string>(FirstZhengWeiDaoTus)
		};
	}

	internal static void ImportState(
		XjDaoTaiMeritWorldArchiveData state,
		IReadOnlyList<XjWorldArchiveGuoWeiRecord> historicalGuoWeiRecords)
	{
		FirstProvenDaoTus.Clear();
		FirstZhengWeiDaoTus.Clear();
		if (state?.FirstProvenDaoTus != null)
		{
			for (int i = 0; i < state.FirstProvenDaoTus.Count; i++)
			{
				string value = (state.FirstProvenDaoTus[i] ?? string.Empty).Trim();
				if (value.Length > 0) FirstProvenDaoTus.Add(value);
			}
		}
		if (state?.FirstZhengWeiDaoTus != null)
		{
			for (int i = 0; i < state.FirstZhengWeiDaoTus.Count; i++)
			{
				string value = (state.FirstZhengWeiDaoTus[i] ?? string.Empty).Trim();
				if (value.Length > 0) FirstZhengWeiDaoTus.Add(value);
			}
		}

		// 0.9.6.2及更早的旧档没有完整功绩世界账本。用已有果位历史建立
		// “曾经有人证道”的静默基线，避免升级后下一位高境被误记为首位；
		// 旧人物不会因此追溯发放功绩。
		if (historicalGuoWeiRecords == null) return;
		for (int i = 0; i < historicalGuoWeiRecords.Count; i++)
		{
			XjWorldArchiveGuoWeiRecord record = historicalGuoWeiRecords[i];
			string daoTu = (record?.DaoTu ?? string.Empty).Trim();
			if (daoTu.Length == 0) continue;
			FirstProvenDaoTus.Add(daoTu);
			if ((record.GuoWei ?? string.Empty).Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				FirstZhengWeiDaoTus.Add(daoTu);
			}
		}
	}

	internal static void Clear()
	{
		FirstProvenDaoTus.Clear();
		FirstZhengWeiDaoTus.Clear();
	}

	internal static void ReconcileHighRealmStatus(Actor actor, int currentYear)
	{
		if (actor?.data == null || !IsEligibleMeritBearer(actor)) return;
		int safeYear = Math.Max(1, currentYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiMeritRealmAwarded, out int realmAwarded);
		if (realmAwarded <= 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiMeritRealmAwarded, HighRealmProofMerit);
			Award(actor, HighRealmProofMerit, "证成高境并受天地承认", safeYear);
		}

		string guoWei = ResolveGuoWei(actor);
		if (!TryResolveDaoTaiPositionKind(guoWei, out string positionKind)) return;
		int desired = string.Equals(positionKind, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			? ZhengWeiHoldingMerit
			: string.Equals(positionKind, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				? YuWeiHoldingMerit
				: RunWeiHoldingMerit;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjDaoTaiMeritPositionAwarded, out int alreadyAwarded);
		if (desired <= alreadyAwarded) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiMeritPositionAwarded, desired);
		Award(actor, desired - Math.Max(0, alreadyAwarded), "持有并稳固" + positionKind, safeYear);
	}

	/// <summary>
	/// 供长期高境事件在真实结果发生后结算功绩；只有真实金丹/真君羽士受领。
	/// </summary>
	internal static bool TryAwardExternal(Actor actor, int amount, string reason, int currentYear, out int actualAward)
	{
		actualAward = 0;
		if (actor?.data == null || amount <= 0 || !IsEligibleMeritBearer(actor)) return false;
		int before = GetMerit(actor);
		Award(actor, amount, reason, currentYear);
		actualAward = Math.Max(0, GetMerit(actor) - before);
		return actualAward > 0;
	}

	private static void Award(Actor actor, int amount, string reason, int currentYear)
	{
		if (actor?.data == null || amount <= 0) return;
		float multiplier = XjRuntimeSettings.DaoTaiMeritGainMultiplier;
		int adjustedAmount = Math.Max(1, (int)Math.Round(amount * multiplier, MidpointRounding.AwayFromZero));
		int before = GetMerit(actor);
		int after = Math.Min(999999, before + adjustedAmount);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiMerit, after);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDaoTaiMeritLastReason, reason ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiMeritLastYear, Math.Max(0, currentYear));
		RefreshQualification(actor);
		XjWorldHistoryStore.RecordActorEvent(
			actor,
			"因" + (string.IsNullOrWhiteSpace(reason) ? "高境功行" : reason.Trim()) + "获天地功绩" + adjustedAmount + "，累计" + after + "。",
			XjEventIconCatalog.JinDanUpgrade);
	}

	private static void RefreshQualification(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjDaoTaiMeritQualified, CanAttemptDaoTai(actor, out _) ? 1 : 0);
	}

	private static bool IsEligibleMeritBearer(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			return false;
		}

		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
	}

	private static bool IsEligibleGreatSageKiller(Actor actor)
	{
		if (IsEligibleMeritBearer(actor)) return true;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)) return false;
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static bool IsPeakHighRealmRoute(Actor actor)
	{
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		if (!string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang);
		yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, Math.Max(0, yiXiang));
		return yiXiang >= XjQuanBingStruggleSystem.PeakYiXiang;
	}

	private static int CalculateAuthorityBreakthroughBonusCore(Actor actor)
	{
		if (actor?.data == null) return 0;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
		{
			return 0;
		}

		string guoWei = string.IsNullOrWhiteSpace(state.GuoWei) ? ResolveGuoWei(actor) : state.GuoWei;
		TryResolveDaoTaiPositionKind(guoWei, out string positionKind);

		// 保留RC11.1的既有分类：已夺/已融入的权柄按核心柄；尚在外道借持的权柄
		// 按衍生柄；本道正位所持本柄按核心柄，余位/闰位的派生位柄按衍生柄。
		int coreAuthorityCount = CountAuthorityEntries(state.SeizedQuanBing);
		int derivedAuthorityCount = CountAuthorityEntries(state.ForeignQuanBing);
		int localAuthorityCount = CountAuthorityEntries(state.LocalQuanBing);
		if (string.Equals(positionKind, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			coreAuthorityCount += localAuthorityCount;
		}
		else
		{
			derivedAuthorityCount += localAuthorityCount;
		}

		return coreAuthorityCount * PeakAuthorityBonusPerCoreAuthorityPercent
			+ derivedAuthorityCount * PeakAuthorityBonusPerDerivedAuthorityPercent;
	}

	private static int CountAuthorityEntries(string raw)
	{
		return string.IsNullOrWhiteSpace(raw)
			? 0
			: raw.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Length;
	}

	internal static bool TryResolveDaoTaiPositionKind(string guoWei, out string positionKind)
	{
		positionKind = string.Empty;
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return false;
		}

		if (normalized.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			positionKind = XjGuoWeiCalculator.ZhengWei;
			return true;
		}
		if (normalized.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			positionKind = XjGuoWeiCalculator.YuWei;
			return true;
		}
		if (normalized.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			positionKind = XjGuoWeiCalculator.RunWei;
			return true;
		}
		return false;
	}

	internal static string ResolveGuoWei(Actor actor)
	{
		XjJinDanState state = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (state.Found && !string.IsNullOrWhiteSpace(state.GuoWei)) return state.GuoWei.Trim();
		// 大圣没有金丹载荷，也不能伪写金性；其自身存活的正位由妖属槽位维护。
		return XjYaoShuGreatSageSystem.TryResolveGreatSageFruit(actor, out string fruit) ? fruit : string.Empty;
	}
}
