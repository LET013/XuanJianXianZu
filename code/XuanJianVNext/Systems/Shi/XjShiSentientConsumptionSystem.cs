using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修“度化/摄生”结果事务。度化等同于真实杀死非修士；0.9.11.2起杀生度化仅属于今释；每名今释
/// 每年最多处理三具真实肉身，展示/纪事人数按一具记十人。七相再按欲、忿、法、业、空性等
/// 追加专属摄生收益；没有逐帧寻人状态机。
/// </summary>
internal static class XjShiSentientConsumptionSystem
{
	internal const int CurrentDuhuaRuleVersion = 5;
	internal const int AnnualActualVictimLimit = 3;
	internal const int NarrativePeoplePerActualVictim = 10;

	internal static void EnsureDuhuaRule(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, out int version);
		if (version >= CurrentDuhuaRuleVersion) return;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiConvertedCount, out int convertedCount);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, out int annualCount);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, out int ledgerYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, out string ledgerKeys);
		if (version < 2)
		{
			// v1 及以前记录的是“传法转修人数”，与真实死亡度化不是同一统计。
			convertedCount = 0;
			annualCount = 0;
			ledgerYear = 0;
			ledgerKeys = string.Empty;
		}
		else if (version == 2)
		{
			// v2 的数字是一具真实肉身=1；v3 起只把展示/纪事人数放大十倍，
			// 机械收益与真实死亡数仍按一具肉身计算。
			convertedCount = ScaleNarrativeCount(convertedCount);
			annualCount = ScaleNarrativeCount(annualCount);
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (version < 4 && string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			// v4 起古释永久退出杀生度化/七相摄生统计，旧档中由旧规则产生的相关计数不再继承。
			convertedCount = 0;
			annualCount = 0;
			ledgerYear = 0;
			ledgerKeys = string.Empty;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionCount, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionLedgerYear, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSentientConsumptionLedgerKeys, string.Empty);
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiConvertedCount, convertedCount);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, Math.Max(0, ledgerYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, ledgerKeys ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, annualCount);
		// 规则迁移时统一清掉旧版本跨年债务；当前自动度化由人口闸门重新按年发放，
		// 七相只调整收益，不再决定今释是否具备度化行为。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDebt, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoLastScheduledYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAutoDoneInDueYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaRuleVersion, CurrentDuhuaRuleVersion);
	}

	internal static bool TryRecordDuhuaKill(Actor killer, long targetId, string targetName,
		int year, bool automated)
	{
		if (killer?.data == null || !killer.isAlive() || targetId <= 0L || year <= 0
			|| !XjCultivationPathRules.IsShi(killer)
			|| !XjShiState.TryBuildSnapshot(killer, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return false;
		EnsureDuhuaRule(killer);
		// 人口闸门关闭时，普通杀生仍按原生战斗处理，但不进入“度化”计数/收益/三书。
		// 自动车道会在真实死亡前刷新闸门，并在跨过3000下界后立即关闸；这里读取
		// 同一持久状态，保证玩家/AI亲手击杀也不能在关闸状态继续刷度化记录。
		if (!XjShiDomainState.IsDuhuaPopulationGateActive) return false;
		// 主动击杀与后台年度度化共享同一个真实人数上限，任何度化入口都不能绕过三人/年。
		if (GetDuhuaActualCountThisYear(killer, year) >= AnnualActualVictimLimit) return false;
		// 自动年度度化由债务车道保证目标唯一；主动击杀继续用年度ledger去重。
		if (!automated && !TryDuhuaOnce(killer, targetId, year)) return false;
		XjActorAccessor.TryGetInt(killer, XjActorDataKeys.ShiConvertedCount, out int converted);
		int nextCount = SaturatingAdd(converted, NarrativePeoplePerActualVictim);
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiConvertedCount, nextCount);
		XjActorAccessor.TryGetInt(killer, XjActorDataKeys.ShiDuhuaAnnualCount, out int annualCount);
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiDuhuaAnnualCount,
			SaturatingAdd(annualCount, NarrativePeoplePerActualVictim));
		if (automated)
		{
			// 年度度化同样是真实“吃人/杀人”。七相按各自倾向结算摄生收益，
			// 但不为批量目标逐条写公告/历史，避免年度日志和UI洪峰。
			TryApplyAutomatedOrdinaryConsumption(killer, snapshot, targetId, targetName, year);
			return true;
		}

		// 主动击杀非修士同样计为度化，并给予一份轻量命数/修持/承载收益。
		XjShiMingShuSystem.TryGrantEvent(killer, year, "duhua_kill:" + targetId, 1f, "conversion");
		XjShiState.GrantPracticeEvent(killer, 30f);
		XjShiDomainState.AddContribution(killer, 1, year);
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiLastExpansionYear, year);
		string safeTargetName = string.IsNullOrWhiteSpace(targetName) ? "一名众生" : targetName;
		XjThreeBookWriter.RecordShiDuhuaKill(killer, targetId, safeTargetName, year);
		int actualCount = nextCount / NarrativePeoplePerActualVictim;
		if (actualCount <= 3 || actualCount % 25 == 0)
		{
			XjWorldHistoryStore.RecordActorEvent(killer,
				"亲手度化" + safeTargetName + "，取其一分众生意向以养自身命数。",
				XjShiCatalog.GetRealmTraitId(snapshot.Realm));
		}
		return true;
	}

	internal static int GetDuhuaCountThisYear(Actor actor, int year)
	{
		if (actor?.data == null || year <= 0 || !XjCultivationPathRules.IsShi(actor)) return 0;
		EnsureDuhuaRule(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, out int ledgerYear);
		if (ledgerYear != year)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, year);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
			return 0;
		}
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, out int count)
			? Math.Max(0, count) : 0;
	}

	internal static int GetDuhuaActualCountThisYear(Actor actor, int year)
	{
		int narrative = GetDuhuaCountThisYear(actor, year);
		if (narrative <= 0) return 0;
		return Math.Max(0, narrative / NarrativePeoplePerActualVictim);
	}

	internal static int NormalizeInheritedDuhuaCount(int previousRuleVersion, int previousCount)
	{
		// v3起该字段已经按当前展示口径保存；v4只拆分古今释语义，
		// 因此今释转世应原值继承，不再重复换算。
		if (previousRuleVersion >= 3) return Math.Max(0, previousCount);
		if (previousRuleVersion == 2) return ScaleNarrativeCount(previousCount);
		return 0;
	}

	internal static void OnAnnualDuhuaBatch(Actor teacher, int annualYear, int killed,
		string firstTargetName, string lastTargetName)
	{
		if (teacher?.data == null || annualYear <= 0 || killed <= 0
			|| !XjCultivationPathRules.IsShi(teacher)
			|| !XjShiState.TryBuildSnapshot(teacher, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return;
		float fateAward = Math.Max(1f, (float)Math.Ceiling(killed / 5f));
		XjShiMingShuSystem.TryGrantEvent(teacher, annualYear,
			"annual_duhua:" + annualYear, fateAward, "conversion");
		XjShiState.GrantPracticeEvent(teacher, killed * 20f);
		XjShiDomainState.AddContribution(teacher, killed, annualYear);
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiLastExpansionYear, annualYear);
		int representedPeople = ScaleNarrativeCount(killed);
		XjThreeBookWriter.RecordShiDuhuaBatch(teacher, annualYear, representedPeople,
			firstTargetName, lastTargetName);
	}

	internal static void OnConfirmedKill(Actor victimActor, in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.LastAttackerId <= 0L || snapshot.LastAttackerId == snapshot.ActorId
			|| !XjActorRegistry.ResolveKnownOrWorld(snapshot.LastAttackerId, out Actor killer)
			|| killer?.data == null || !killer.isAlive() || !XjCultivationPathRules.IsShi(killer)
			|| !XjShiState.TryBuildSnapshot(killer, out XjShiSnapshot killerSnapshot)
			|| XjShiCatalog.GetRank(killerSnapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return;
		if (!IsCivilized(victimActor)) return;
		// 古释可以正常参与原生战斗，但杀人永远不解释为度化，也不结算七相摄生。
		if (!string.Equals(killerSnapshot.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return;

		bool victimCultivator = IsCultivator(victimActor, snapshot);
		int year = Math.Max(1, snapshot.Year);
		string targetName = string.IsNullOrWhiteSpace(snapshot.Name) ? "一名众生" : snapshot.Name;
		bool ordinaryDuhuaRecorded = true;
		if (!victimCultivator)
		{
			ordinaryDuhuaRecorded = TryRecordDuhuaKill(
				killer, snapshot.ActorId, targetName, year, automated: false);
		}

		XjActorAccessor.TryGetString(killer, XjActorDataKeys.ShiLineageId, out string lineageId);
		bool highVictim = victimCultivator && ResolveVictimTier(victimActor, snapshot) >= XjRealmSuppression.TierZhuJi;
		string action;
		float mingShu;
		float practice;
		int contribution;
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal))
		{
			action = "摄取了"; mingShu = highVictim ? 8f : 3f; practice = highVictim ? 480f : 160f; contribution = highVictim ? 8 : 3;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal))
		{
			action = "吞纳了"; mingShu = highVictim ? 10f : 4f; practice = highVictim ? 600f : 220f; contribution = highVictim ? 10 : 4;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal) && victimCultivator)
		{
			action = "食取了"; mingShu = highVictim ? 9f : 4f; practice = highVictim ? 520f : 200f; contribution = highVictim ? 12 : 5;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) && victimCultivator)
		{
			action = "收摄了"; mingShu = highVictim ? 7f : 3f; practice = highVictim ? 420f : 160f; contribution = highVictim ? 7 : 3;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) && highVictim)
		{
			action = "归空了"; mingShu = 12f; practice = 700f; contribution = 12;
		}
		else return;

		// 普通众生只有在本次死亡已经通过“人口闸门 + 每人三具/年 + 去重”
		// 的真实度化事务后，才允许继续结算七相摄生。旧实现忽略上面的返回值，
		// 导致大欲/忿怒在关闸或个人额度耗尽后仍能靠普通战斗无限刷摄生收益。
		// 修士之间的真实战斗则仍走各法脉自己的高价值摄生分支。
		if (!victimCultivator && !ordinaryDuhuaRecorded) return;
		if (!TryConsumeOnce(killer, snapshot.ActorId, year)) return;
		XjShiMingShuSystem.TryGrantEvent(killer, year, "seven_aspect_consume:" + snapshot.ActorId,
			mingShu, "battle");
		XjShiState.GrantPracticeEvent(killer, practice);
		XjShiDomainState.AddContribution(killer, contribution, year);
		XjActorAccessor.TryGetInt(killer, XjActorDataKeys.ShiSentientConsumptionCount, out int count);
		count = Math.Max(0, count) + 1;
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiSentientConsumptionCount, count);
		string substance = ResolveSubstance(lineageId);
		XjWorldHistoryStore.RecordActorEvent(killer,
			action + targetName + "的" + substance + "，以养" + XjShiCatalog.GetLineageDisplay(lineageId)
				+ "之相与所系释土。", XjShiCatalog.GetRealmTraitId(killerSnapshot.Realm));
		bool announce = highVictim || count <= 3 || count % 10 == 0;
		if (announce)
		{
			XjShiAnnouncementSystem.OnSentientConsumption(killer, action, targetName,
				major: highVictim || count % 10 == 0);
		}
	}

	private static void TryApplyAutomatedOrdinaryConsumption(Actor killer, in XjShiSnapshot snapshot,
		long targetId, string targetName, int year)
	{
		XjActorAccessor.TryGetString(killer, XjActorDataKeys.ShiLineageId, out string lineageId);
		float mingShu;
		float practice;
		int contribution;
		string eventType = "conversion";
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal))
		{
			mingShu = 3f; practice = 160f; contribution = 3; eventType = "battle";
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal))
		{
			mingShu = 4f; practice = 220f; contribution = 4; eventType = "battle";
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal))
		{
			mingShu = 4f; practice = 180f; contribution = 5;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal))
		{
			mingShu = 2f; practice = 120f; contribution = 2;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal))
		{
			mingShu = 2f; practice = 110f; contribution = 2;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal))
		{
			mingShu = 1f; practice = 90f; contribution = 1;
		}
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal))
		{
			mingShu = 1f; practice = 80f; contribution = 1;
		}
		else
		{
			mingShu = 1f; practice = 100f; contribution = 1;
		}
		if (!TryConsumeOnce(killer, targetId, year)) return;
		XjShiMingShuSystem.TryGrantEvent(killer, year, "seven_aspect_consume:" + targetId,
			mingShu, eventType);
		XjShiState.GrantPracticeEvent(killer, practice);
		XjShiDomainState.AddContribution(killer, contribution, year);
		XjActorAccessor.TryGetInt(killer, XjActorDataKeys.ShiSentientConsumptionCount, out int count);
		XjActorAccessor.SetInt(killer, XjActorDataKeys.ShiSentientConsumptionCount, Math.Max(0, count) + 1);
		_ = snapshot;
		_ = targetName;
	}

	internal static void OnSuccessfulPreaching(Actor teacher, Actor target, int annualYear, bool important)
	{
		if (teacher?.data == null || target?.data == null || annualYear <= 0
			|| !XjCultivationPathRules.IsShi(teacher)) return;
		XjActorAccessor.TryGetString(teacher, XjActorDataKeys.ShiLineageId, out string lineageId);
		string action;
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) action = "摄受了";
		else if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) action = "善引了";
		else return;
		long targetId = ((BaseSystemData)target.data).id;
		if (!TryConsumeOnce(teacher, targetId, annualYear)) return;
		XjShiMingShuSystem.TryGrantEvent(teacher, annualYear, "seven_aspect_preach:" + targetId,
			important ? 6f : 2f, "conversion");
		XjShiState.GrantPracticeEvent(teacher, important ? 300f : 100f);
		XjShiDomainState.AddContribution(teacher, important ? 6 : 2, annualYear);
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.ShiSentientConsumptionCount, out int count);
		int nextCount = Math.Max(0, count) + 1;
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.ShiSentientConsumptionCount, nextCount);
		XjWorldHistoryStore.RecordActorEvent(teacher,
			action + target.getName() + "一分善愿，使其归于" + XjShiCatalog.GetLineageDisplay(lineageId) + "法脉。",
			XjShiTraitIds.DharmaMaster);
		if (important || nextCount <= 3 || nextCount % 10 == 0)
			XjShiAnnouncementSystem.OnSentientConsumption(teacher, action, target.getName(),
				important || nextCount % 10 == 0);
	}

	private static bool TryDuhuaOnce(Actor actor, long targetId, int year)
	{
		if (actor?.data == null || targetId <= 0L) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, out int ledgerYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, out string ledger);
		if (ledgerYear != year)
		{
			ledger = string.Empty;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaLedgerYear, year);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDuhuaAnnualCount, 0);
		}
		string token = "|" + targetId.ToString(CultureInfo.InvariantCulture) + "|";
		ledger ??= string.Empty;
		if (ledger.Contains(token, StringComparison.Ordinal) || ledger.Length + token.Length > 4096) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDuhuaLedgerKeys, ledger + token);
		return true;
	}

	private static bool TryConsumeOnce(Actor actor, long targetId, int year)
	{
		if (actor?.data == null || targetId <= 0L) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiSentientConsumptionLedgerYear, out int ledgerYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSentientConsumptionLedgerKeys, out string ledger);
		if (ledgerYear != year)
		{
			ledger = string.Empty;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiSentientConsumptionLedgerYear, year);
		}
		string token = "|" + targetId.ToString(CultureInfo.InvariantCulture) + "|";
		ledger ??= string.Empty;
		if (ledger.Contains(token, StringComparison.Ordinal)) return false;
		// 台账饱和后停止本年度新增奖励，不能清空后让同一目标重复结算。
		if (ledger.Length + token.Length > 4096) return false;
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSentientConsumptionLedgerKeys, ledger + token);
		return true;
	}

	private static bool IsCivilized(Actor actor)
	{
		try { return actor?.asset != null && actor.asset.civ; }
		catch { return false; }
	}

	private static bool IsCultivator(Actor actor, in XjDeathSnapshot snapshot)
	{
		return XjCultivationPathRules.TryGetPath(actor, out _)
			|| !string.IsNullOrWhiteSpace(snapshot.RealmId)
			|| !string.IsNullOrWhiteSpace(snapshot.DaoTu)
			|| !string.IsNullOrWhiteSpace(snapshot.GongFaName);
	}

	private static int ResolveVictimTier(Actor actor, in XjDeathSnapshot snapshot)
	{
		int tier = actor?.data == null ? 0 : XjRealmSuppression.GetRealmTier(actor);
		if (tier > 0) return tier;
		string realm = XjRealmHelper.NormalizeId(snapshot.RealmId);
		if (string.Equals(realm, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return XjRealmSuppression.TierJinDan;
		if (string.Equals(realm, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return XjRealmSuppression.TierZiFu;
		if (string.Equals(realm, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.HuangGuan, StringComparison.Ordinal)) return XjRealmSuppression.TierZhuJi;
		return 0;
	}

	private static int ScaleNarrativeCount(int value)
	{
		if (value <= 0) return 0;
		long scaled = (long)value * NarrativePeoplePerActualVictim;
		return scaled >= int.MaxValue ? int.MaxValue : (int)scaled;
	}

	private static int SaturatingAdd(int value, int increment)
	{
		long next = (long)Math.Max(0, value) + Math.Max(0, increment);
		return next >= int.MaxValue ? int.MaxValue : (int)next;
	}

	private static string ResolveSubstance(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return "欲念";
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) return "忿火";
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return "法痕";
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return "业力";
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return "空性";
		return "众生意向";
	}
}
