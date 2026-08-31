using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjMingYangSchemeSummary
{
	internal readonly long TargetActorId;
	internal readonly string TargetName;
	internal readonly string TargetRealm;
	internal readonly string TargetDaoTu;
	internal readonly string TargetFamilyName;
	internal readonly long PatronActorId;
	internal readonly string PatronName;
	internal readonly string PatronRealm;
	internal readonly string PatronDaoTu;
	internal readonly string PatronFamilyName;
	internal readonly int Stage;
	internal readonly string StageName;
	internal readonly int StartedYear;
	internal readonly int LastAdvanceYear;
	internal readonly int YearsRunning;

	internal XjMingYangSchemeSummary(
		long targetActorId, string targetName, string targetRealm, string targetDaoTu, string targetFamilyName,
		long patronActorId, string patronName, string patronRealm, string patronDaoTu, string patronFamilyName,
		int stage, string stageName, int startedYear, int lastAdvanceYear, int yearsRunning)
	{
		TargetActorId = targetActorId;
		TargetName = targetName ?? string.Empty;
		TargetRealm = targetRealm ?? string.Empty;
		TargetDaoTu = targetDaoTu ?? string.Empty;
		TargetFamilyName = targetFamilyName ?? string.Empty;
		PatronActorId = patronActorId;
		PatronName = patronName ?? string.Empty;
		PatronRealm = patronRealm ?? string.Empty;
		PatronDaoTu = patronDaoTu ?? string.Empty;
		PatronFamilyName = patronFamilyName ?? string.Empty;
		Stage = stage;
		StageName = stageName ?? string.Empty;
		StartedYear = startedYear;
		LastAdvanceYear = lastAdvanceYear > 0 ? lastAdvanceYear : startedYear;
		YearsRunning = Math.Max(0, yearsRunning);
	}
}

/// <summary>
/// 上修围绕命数子做局的低频分阶段事件。首版只落地最具辨识度的“明阳局”：
/// 复用家族稀疏账本与修士候选索引，每五十年一个家族相位，不做世界人口常驻扫描。
/// </summary>
internal static class XjMingShuSchemeSystem
{
	private const string SchemeMingYang = "MingYang";
	private const int CheckIntervalYears = 50;
	private const int StartChancePercent = 35;
	private const int MinimumStageYears = 40;
	private const int FinalTimeoutYears = 250;
	private const int MaxCandidateChecks = 96;

	internal static bool ShouldCheck(long familyId, in XjFamilyLedgerAggregate aggregate, int currentYear)
	{
		// 明阳局只能由世界中真实存在的金丹/真君/道胎上修主持。这里先用家族最高境界做
		// 低成本粗筛，真正开局前仍会解析 Actor 并检查权威境界，绝不拿紫府或借境投影充数。
		int jinDanOrder = XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan);
		if (familyId <= 0L || currentYear <= 0 || aggregate.CultivatorCount <= 0
			|| aggregate.HighestRealmOrder < jinDanOrder)
		{
			return false;
		}
		// 家族主车道只在5年边界进入全量批次；将50年相位限制为10个五年桶，
		// 既能稳定错峰，也不会出现哈希相位与主车道永不相交。
		int phase = XjDeterministicHash.PositiveIndex(familyId, "family.mingshu_scheme.phase.v2", CheckIntervalYears / 5) * 5;
		return currentYear % CheckIntervalYears == phase;
	}

	internal static bool TryResolve(long sourceFamilyId, IReadOnlyList<XjFamilyMemberLedgerEntry> sourceEntries, int currentYear)
	{
		if (sourceFamilyId <= 0L || currentYear <= 0 || sourceEntries == null || sourceEntries.Count == 0) return false;

		// 已有局只能由原主持人继续。主持人跌出真实高境、转修明阳或成为帝明阳时，局直接断，
		// 不能让族中另一人“接管”从而把一个不存在的上修偷偷补出来。
		Actor activePatron = ResolveActiveSchemePatron(sourceEntries);
		if (activePatron != null)
		{
			if (!IsQualifiedPatron(activePatron))
			{
				XjActorAccessor.TryGetLong(activePatron, XjActorDataKeys.XjMingShuSchemeTargetActorId, out long staleTargetId);
				Actor staleTarget = null;
				if (staleTargetId > 0L) XjActorRegistry.ResolveKnownOrWorld(staleTargetId, out staleTarget);
				RecordInterrupted(sourceFamilyId, activePatron, staleTargetId, currentYear, "做局上修已不再具备真实金丹/真君/道胎位格，棋局失去主持者。", penalizePatron: true);
				ClearScheme(activePatron, staleTarget);
				return true;
			}
			return TryAdvanceExisting(sourceFamilyId, activePatron, currentYear, out bool activeChanged) && activeChanged;
		}

		Actor patron = ResolvePatron(sourceEntries);
		if (patron == null || !HasPatronInvestment(patron)) return false;
		if (XjDeterministicHash.PositiveIndex(
			ActorId(patron) + sourceFamilyId + currentYear,
			"family.mingshu_scheme.start",
			100) >= StartChancePercent)
		{
			return false;
		}

		if (!ResolveMingYangTarget(sourceFamilyId, patron, currentYear, out Actor target, out long targetFamilyId)) return false;
		StartMingYangScheme(sourceFamilyId, targetFamilyId, patron, target, currentYear);
		return true;
	}

	private static bool TryAdvanceExisting(long sourceFamilyId, Actor patron, int currentYear, out bool changed)
	{
		changed = false;
		if (!IsQualifiedPatron(patron)) return false;
		if (!XjActorAccessor.TryGetLong(patron, XjActorDataKeys.XjMingShuSchemeTargetActorId, out long targetId)
			|| targetId <= 0L)
		{
			return false;
		}
		if (!XjActorRegistry.ResolveKnownOrWorld(targetId, out Actor target) || target?.data == null || !target.isAlive())
		{
			RecordInterrupted(sourceFamilyId, patron, targetId, currentYear, "局中命数子已不在人世，明阳局自此中断。", penalizePatron: true);
			ClearScheme(patron, null);
			changed = true;
			return true;
		}

		if (!XjMingShuChildSystem.IsMingShuChild(target))
		{
			RecordInterrupted(sourceFamilyId, patron, targetId, currentYear, "局中之人已不再具备命数子根基，明阳局失去局眼。", penalizePatron: false);
			ClearScheme(patron, target);
			changed = true;
			return true;
		}

		if (!XjActorAccessor.TryGetString(target, XjActorDataKeys.XjMingShuSchemeType, out string schemeType)
			|| !string.Equals(schemeType, SchemeMingYang, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjMingShuSchemePatronActorId, out long patronId)
			|| patronId != ActorId(patron))
		{
			ClearScheme(patron, null);
			changed = true;
			return true;
		}

		XjActorAccessor.TryGetString(target, XjActorDataKeys.DaoTu, out string daoTu);
		if (!string.Equals((daoTu ?? string.Empty).Trim(), "明阳", StringComparison.Ordinal))
		{
			XjMingShuState.AddAcquired(target, 2f);
			XjMingShuState.AddAcquired(patron, -2f);
			AddDaoHui(target, 1f);
			RecordStage(sourceFamilyId, ResolveFamilyId(target), patron, target, currentYear,
				"命数子脱局", "MingShuMingYangSchemeEscapedPath",
				SafeName(target) + "偏离明阳本道，上修原先落下的棋路失去着力之处。其因挣脱既定推演，后天命数增长二点，道慧增长一点；做局者则折损二点后天命数。", 4, true);
			ClearScheme(patron, target);
			changed = true;
			return true;
		}

		XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeStage, out int stage);
		XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeStartedYear, out int startedYear);
		XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, out int lastAdvanceYear);
		if (startedYear <= 0) startedYear = currentYear;
		if (lastAdvanceYear <= 0) lastAdvanceYear = startedYear;
		if (currentYear - lastAdvanceYear < MinimumStageYears) return true;

		long targetFamilyId = ResolveFamilyId(target);
		if (XjXianGuoSystem.IsDiMingYang(target))
		{
			XjMingShuState.AddAcquired(target, 8f);
			XjMingShuState.AddAcquired(patron, 4f);
			AddDaoHui(patron, 1f);
			bool imperialMeritAwarded = XjDaoTaiMeritSystem.TryAwardExternal(
				patron, 520, "明阳局大成·扶成帝明阳", currentYear, out int imperialMerit);
			XjUpperCultivatorGoldSupportSystem.AddCrossDaoInfluence(patron, "明阳", 3);
			string meritText = imperialMeritAwarded
				? "做局上修因真正将一位明阳命数子推至帝统大成，获得天地功绩" + imperialMerit + "，并使自身对明阳道统的干涉影响提高三层。"
				: "主持者已越过需要积攒道胎功绩的金丹/真君阶段，本次不重复结算功绩，但其对明阳道统的干涉影响仍提高三层。";
			RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
				"明阳局大成", "MingShuMingYangSchemeImperialSuccess",
				SafeName(target) + "沿局中诸势登临帝明阳，原本被上修层层推动的明阳之势终于贯通。命数子后天命数增长八点；"
				+ meritText, 5, true);
			ClearScheme(patron, target);
			changed = true;
			return true;
		}

		int realmTier = XjRealmSuppression.GetRealmTier(target);
		if (stage <= 1)
		{
			XjMingShuState.AddAcquired(target, 2f);
			SetStage(target, 2, currentYear);
			ApplyFuQiProjectAcceleration(target, currentYear, 0.10f);
			RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
				"明阳局·引势", "MingShuMingYangSchemeStage2",
				SafeName(target) + "近年屡逢合于明阳的机缘与人事，看似偶然，实则已有上修暗中移势。其后天命数增长二点。", 3, false);
			changed = true;
			return true;
		}
		if (stage == 2)
		{
			XjMingShuState.AddAcquired(target, 2f);
			AddDaoHui(target, 1f);
			SetStage(target, 3, currentYear);
			ApplyFuQiProjectAcceleration(target, currentYear, 0.20f);
			RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
				"明阳局·推局", "MingShuMingYangSchemeStage3",
				SafeName(target) + "已走到局势中央，上修开始借世家、传承与时势将其往更高处推去。其后天命数再增二点，道慧增长一点。", 4, true);
			changed = true;
			return true;
		}

		if (realmTier >= XjRealmSuppression.TierZiFu)
		{
			bool broke = RollBreakScheme(target, patron, sourceFamilyId, currentYear);
			if (broke)
			{
				XjMingShuState.AddAcquired(target, 6f);
				XjMingShuState.AddAcquired(patron, -4f);
				AddDaoHui(target, 2f);
				RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
					"命数子识破明阳局", "MingShuMingYangSchemeBroken",
					SafeName(target) + "踏入真人位阶后终于回看自身一路气数，察觉诸多机缘皆有人为痕迹，遂从上修棋局中脱身。其后天命数增长六点，道慧增长二点；做局上修因气数反冲折损四点后天命数。", 5, true);
			}
			else
			{
				XjMingShuState.AddAcquired(target, 4f);
				XjMingShuState.AddAcquired(patron, 2f);
				bool settledMeritAwarded = XjDaoTaiMeritSystem.TryAwardExternal(
					patron, 240, "明阳局收束·扶成真人", currentYear, out int settledMerit);
				XjUpperCultivatorGoldSupportSystem.AddCrossDaoInfluence(patron, "明阳", 1);
				string meritText = settledMeritAwarded
					? "做局上修因使这场长期布局真正落成，获得天地功绩" + settledMerit + "，对明阳道统的干涉影响提高一层。"
					: "主持者已越过需要积攒道胎功绩的金丹/真君阶段，本次不重复结算功绩，但对明阳道统的干涉影响仍提高一层。";
				RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
					"明阳局收束", "MingShuMingYangSchemeSuccess",
					SafeName(target) + "借层层时势踏入真人位阶，明阳局至此收束。命数子后天命数增长四点；" + meritText, 5, true);
			}
			ClearScheme(patron, target);
			changed = true;
			return true;
		}

		if (currentYear - startedYear >= FinalTimeoutYears)
		{
			XjMingShuState.AddAcquired(target, 1f);
			XjMingShuState.AddAcquired(patron, -1f);
			RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
				"明阳局散", "MingShuMingYangSchemeTimeout",
				SafeName(target) + "多年未能走到上修预想的位置，局势渐散，只余一线气数落在其身，后天命数增长一点；做局上修折损一点后天命数。", 3, false);
			ClearScheme(patron, target);
			changed = true;
			return true;
		}

		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, currentYear);
		changed = true;
		return true;
	}

	private static void StartMingYangScheme(long sourceFamilyId, long targetFamilyId, Actor patron, Actor target, int currentYear)
	{
		// 明阳局是跨越数十年至数百年的高位做局，不能以象征性的两点命数启动。
		// 真君/金丹先压八点后天命数，道胎层次因涉世更深、牵动更广，先压十二点。
		int investment = ResolveInitialFateInvestment(patron);
		XjMingShuState.AddAcquired(patron, -investment);
		long patronId = ActorId(patron);
		long targetId = ActorId(target);
		XjActorAccessor.SetLong(patron, XjActorDataKeys.XjMingShuSchemeTargetActorId, targetId);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjMingShuSchemeType, SchemeMingYang);
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjMingShuSchemePatronActorId, patronId);
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjMingShuSchemePatronFamilyId, sourceFamilyId);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeStage, 1);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeStartedYear, currentYear);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, currentYear);
		ApplyFuQiProjectAcceleration(target, currentYear, 0.05f);
		string investmentText = investment >= 12 ? "十二" : "八";
		RecordStage(sourceFamilyId, targetFamilyId, patron, target, currentYear,
			"上修观命", "MingShuMingYangSchemeStage1",
			SafeName(patron) + "望见" + SafeName(target) + "身上明阳气数异于常人，遂不直接授法，只在暗处落下第一着棋，试图借其命数做一场明阳局，并先以自身" + investmentText + "点后天命数压住局眼。", 4, true);
	}

	private static bool ResolveMingYangTarget(long sourceFamilyId, Actor patron, int currentYear, out Actor selected, out long targetFamilyId)
	{
		selected = null;
		targetFamilyId = 0L;
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids == null || ids.Count == 0) return false;

		long patronId = ActorId(patron);
		int start = XjDeterministicHash.PositiveIndex(
			patronId + sourceFamilyId + currentYear,
			"family.mingshu_scheme.mingyang_target",
			ids.Count);
		float bestMingShu = float.MinValue;
		int bestRealm = -1;
		long bestId = long.MaxValue;

		int checks = Math.Min(ids.Count, MaxCandidateChecks);
		for (int offset = 0; offset < checks; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == patronId
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()
				|| !XjMingShuChildSystem.IsMingShuChild(actor))
			{
				continue;
			}
			long familyId = ResolveFamilyId(actor);
			if (familyId <= 0L || XjXianGuoSystem.IsDiMingYang(actor)) continue;
			if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
				|| !string.Equals((daoTu ?? string.Empty).Trim(), "明阳", StringComparison.Ordinal)) continue;
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjMingShuSchemeType, out string activeScheme)
				&& !string.IsNullOrWhiteSpace(activeScheme))
			{
				if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuSchemePatronActorId, out long activePatronId)
					&& activePatronId > 0L
					&& XjActorRegistry.ResolveKnownOrWorld(activePatronId, out Actor activePatron)
					&& activePatron?.data != null && activePatron.isAlive()) continue;
				ClearScheme(null, actor);
			}

			int realm = XjRealmSuppression.GetRealmTier(actor);
			if (realm < XjRealmSuppression.TierTaiXi || realm > XjRealmSuppression.TierZhuJi) continue;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
			long id = ActorId(actor);
			if (selected == null || mingShu > bestMingShu
				|| Math.Abs(mingShu - bestMingShu) < 0.001f && realm > bestRealm
				|| Math.Abs(mingShu - bestMingShu) < 0.001f && realm == bestRealm && id < bestId)
			{
				selected = actor;
				targetFamilyId = familyId;
				bestMingShu = mingShu;
				bestRealm = realm;
				bestId = id;
			}
		}
		return selected != null && targetFamilyId > 0L;
	}

	private static Actor ResolveActiveSchemePatron(IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
	{
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found || !entry.IsAlive || entry.ActorId <= 0L
				|| !XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()) continue;
			if (XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjMingShuSchemeTargetActorId, out long targetId)
				&& targetId > 0L) return actor;
		}
		return null;
	}

	private static Actor ResolvePatron(IReadOnlyList<XjFamilyMemberLedgerEntry> entries)
	{
		Actor best = null;
		int bestRealm = -1;
		float bestHuiGuang = float.MinValue;
		long bestId = long.MaxValue;
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyMemberLedgerEntry entry = entries[i];
			if (!entry.Found || !entry.IsAlive || entry.ActorId <= 0L
				|| !XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !IsQualifiedPatron(actor)) continue;
			int realm = ResolvePatronRealmScore(actor);
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
			long id = ActorId(actor);
			if (best == null || realm > bestRealm
				|| realm == bestRealm && huiGuang > bestHuiGuang
				|| realm == bestRealm && Math.Abs(huiGuang - bestHuiGuang) < 0.001f && id < bestId)
			{
				best = actor;
				bestRealm = realm;
				bestHuiGuang = huiGuang;
				bestId = id;
			}
		}
		return best;
	}

	private static bool IsQualifiedPatron(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjXianGuoSystem.IsDiMingYang(actor)) return false;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals((daoTu ?? string.Empty).Trim(), "明阳", StringComparison.Ordinal)) return false;

		// 只认权威境界字段。仙国持玄、UI投影或残留特质即便显示成“金丹”，也不能主持明阳局。
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static int ResolvePatronRealmScore(Actor actor)
	{
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)) return 3;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)) return 2;
		return 0;
	}

	private static int ResolveInitialFateInvestment(Actor patron)
	{
		int realmScore = ResolvePatronRealmScore(patron);
		return realmScore >= 3 ? 12 : 8;
	}

	private static bool HasPatronInvestment(Actor patron)
	{
		if (patron?.data == null) return false;
		int required = ResolveInitialFateInvestment(patron);
		return XjActorAccessor.TryGetFloat(patron, XjActorDataKeys.MingShuAcquired, out float acquired)
			&& acquired >= required;
	}

	internal static IReadOnlyList<XjMingYangSchemeSummary> ReadActiveSummaries()
	{
		List<XjMingYangSchemeSummary> result = new List<XjMingYangSchemeSummary>();
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids == null || ids.Count == 0) return result;
		int currentYear = Math.Max(1, XjYearTracker.CurrentYear);
		HashSet<long> seenTargets = new HashSet<long>();
		for (int i = 0; i < ids.Count; i++)
		{
			long targetId = ids[i];
			if (targetId <= 0L || !seenTargets.Add(targetId)
				|| !XjActorRegistry.ResolveKnownOrWorld(targetId, out Actor target)
				|| target?.data == null || !target.isAlive()
				|| !TryGetActiveSchemeStage(target, out int stage)
				|| !XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjMingShuSchemePatronActorId, out long patronId)
				|| patronId <= 0L
				|| !XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron)
				|| !IsQualifiedPatron(patron))
			{
				continue;
			}

			XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeStartedYear, out int startedYear);
			XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, out int lastAdvanceYear);
			if (startedYear <= 0) startedYear = currentYear;
			if (lastAdvanceYear <= 0) lastAdvanceYear = startedYear;
			long targetFamilyId = ResolveFamilyId(target);
			long patronFamilyId = ResolveFamilyId(patron);
			result.Add(new XjMingYangSchemeSummary(
				targetId,
				SafeName(target),
				XjRealmHelper.GetDisplayName(XjRealmHelper.GetUnifiedId(target, XjRealmHelper.GetTraitSnapshotForRouter)),
				ResolveDaoTuDisplay(target),
				XjFamilyDisplayNameResolver.Resolve(targetFamilyId),
				patronId,
				SafeName(patron),
				XjRealmHelper.GetDisplayName(XjRealmHelper.GetUnifiedId(patron, XjRealmHelper.GetTraitSnapshotForRouter)),
				ResolveDaoTuDisplay(patron),
				XjFamilyDisplayNameResolver.Resolve(patronFamilyId),
				stage,
				StageDisplayName(stage),
				startedYear,
				lastAdvanceYear,
				Math.Max(0, currentYear - startedYear)));
		}
		result.Sort((left, right) =>
		{
			int stageCompare = right.Stage.CompareTo(left.Stage);
			if (stageCompare != 0) return stageCompare;
			int yearCompare = left.StartedYear.CompareTo(right.StartedYear);
			if (yearCompare != 0) return yearCompare;
			return left.TargetActorId.CompareTo(right.TargetActorId);
		});
		return result;
	}

	internal static string StageDisplayName(int stage)
	{
		return stage switch
		{
			1 => "识命",
			2 => "引势",
			3 => "推局",
			_ => "未成局"
		};
	}

	/// <summary>
	/// 明阳局不是纯史书事件。局中命数子在识命/引势/推局三个阶段会获得
	/// 可直接进入年度修炼结算的成长倍率；主持者必须仍是当世真实存在的非明阳高境。
	/// </summary>
	internal static float ResolveCultivationMultiplier(Actor target)
	{
		if (!TryGetActiveSchemeStage(target, out int stage)) return 1f;
		return stage switch
		{
			1 => 1.08f,
			2 => 1.18f,
			3 => 1.30f,
			_ => 1f
		};
	}

	/// <summary>
	/// 阶段越深，上修实际投入的人事、资源与推演越多。该加成直接以百分点进入
	/// 紫金/服气破境判定，而不是只增加面板命数。
	/// </summary>
	internal static float ResolveBreakthroughSuccessBonus(Actor target)
	{
		if (!TryGetActiveSchemeStage(target, out int stage)) return 0f;
		return stage switch
		{
			1 => 0.02f,
			2 => 0.05f,
			3 => 0.08f,
			_ => 0f
		};
	}

	private static bool TryGetActiveSchemeStage(Actor target, out int stage)
	{
		stage = 0;
		if (target?.data == null || !target.isAlive()
			|| !XjActorAccessor.TryGetString(target, XjActorDataKeys.XjMingShuSchemeType, out string schemeType)
			|| !string.Equals(schemeType, SchemeMingYang, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjMingShuSchemeStage, out stage)
			|| stage < 1 || stage > 3
			|| !XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjMingShuSchemePatronActorId, out long patronId)
			|| patronId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron)
			|| !IsQualifiedPatron(patron))
		{
			stage = 0;
			return false;
		}

		if (!XjActorAccessor.TryGetLong(patron, XjActorDataKeys.XjMingShuSchemeTargetActorId, out long patronTargetId)
			|| patronTargetId != ActorId(target))
		{
			stage = 0;
			return false;
		}
		return true;
	}

	/// <summary>
	/// 服气养性没有真元条，因此明阳局的“实际推局收益”不能只照搬紫金真元倍率。
	/// 每次阶段推进时，按剩余工期缩短求身/神妙圆满/失败后金性温养工程。
	/// </summary>
	private static void ApplyFuQiProjectAcceleration(Actor target, int currentYear, float remainingFraction)
	{
		if (target?.data == null || currentYear <= 0 || remainingFraction <= 0f
			|| !XjCultivationPathRules.IsFuQiYangXing(target)) return;

		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(target, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			ShortenFuQiProjectByFraction(target, XjActorDataKeys.FuQiBodyProjectCompleteYear, currentYear, remainingFraction);
			return;
		}
		if (!string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return;

		if (XjActorAccessor.TryGetInt(target, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectedYear)
			&& perfectedYear > 0)
		{
			ShortenFuQiProjectByFraction(target, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, currentYear, remainingFraction);
		}
		else
		{
			ShortenFuQiProjectByFraction(target, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, currentYear, remainingFraction);
		}
	}

	private static void ShortenFuQiProjectByFraction(Actor target, string key, int currentYear, float remainingFraction)
	{
		if (!XjActorAccessor.TryGetInt(target, key, out int completeYear) || completeYear <= currentYear + 1) return;
		int remaining = completeYear - currentYear;
		int years = Math.Max(1, (int)Math.Floor(remaining * Math.Clamp(remainingFraction, 0f, 0.75f)));
		XjActorAccessor.SetInt(target, key, Math.Max(currentYear + 1, completeYear - years));
	}


	private static bool RollBreakScheme(Actor target, Actor patron, long sourceFamilyId, int currentYear)
	{
		XjActorAccessor.TryGetFloat(target, XjActorDataKeys.MingShu, out float mingShu);
		XjActorAccessor.TryGetFloat(target, XjActorDataKeys.HuiGuang, out float huiGuang);
		int chance = 15
			+ Math.Max(0, (int)Math.Floor(mingShu - XjMingShuChildSystem.MingShuChildCongenitalThreshold))
			+ Math.Max(0, (int)Math.Floor(huiGuang / 4f));
		chance = Math.Min(75, chance);
		return XjDeterministicHash.PositiveIndex(
			ActorId(target) + ActorId(patron) + sourceFamilyId + currentYear,
			"family.mingshu_scheme.break",
			100) < chance;
	}

	private static void SetStage(Actor target, int stage, int currentYear)
	{
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeStage, stage);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, currentYear);
	}

	private static void ClearScheme(Actor patron, Actor target)
	{
		if (patron?.data != null) XjActorAccessor.SetLong(patron, XjActorDataKeys.XjMingShuSchemeTargetActorId, 0L);
		if (target?.data == null) return;
		XjActorAccessor.SetString(target, XjActorDataKeys.XjMingShuSchemeType, string.Empty);
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjMingShuSchemePatronActorId, 0L);
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjMingShuSchemePatronFamilyId, 0L);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeStage, 0);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeStartedYear, 0);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjMingShuSchemeLastAdvanceYear, 0);
	}

	private static void AddDaoHui(Actor actor, float delta)
	{
		if (actor?.data == null || delta <= 0f) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Add(huiGuang, delta, XjDaoHuiPolicy.RareGrowthCeiling));
	}

	private static void RecordInterrupted(long sourceFamilyId, Actor patron, long targetId, int currentYear, string reason, bool penalizePatron = false)
	{
		if (penalizePatron && patron?.data != null) XjMingShuState.AddAcquired(patron, -2f);
		string patronName = SafeName(patron);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Family,
			"明阳局中断",
			patronName + "先前布下的明阳局未及收束便已断线。" + reason,
			3,
			actorId: ActorId(patron),
			actorName: patronName,
			familyId: sourceFamilyId,
			year: currentYear,
			eventType: "MingShuMingYangSchemeInterrupted",
			relatedActorId: targetId,
			visibilityFlags: (int)(XjHistoryVisibility.Family | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Failure);
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static void RecordStage(
		long sourceFamilyId,
		long targetFamilyId,
		Actor patron,
		Actor target,
		int currentYear,
		string title,
		string eventType,
		string body,
		int importance,
		bool announce)
	{
		long patronId = ActorId(patron);
		long targetId = ActorId(target);
		string patronName = SafeName(patron);
		string targetName = SafeName(target);
		XjCenturyAnnalsStore.ObserveFamilyEvent(eventType, currentYear, sourceFamilyId, importance, body, patronId, patronName);
		if (targetFamilyId > 0L)
		{
			XjCenturyAnnalsStore.ObserveFamilyEvent(eventType + "Target", currentYear, targetFamilyId, importance, body, targetId, targetName);
		}
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Family,
			title,
			body,
			importance,
			isProtected: importance >= 5,
			actorId: targetId,
			actorName: targetName,
			familyId: targetFamilyId,
			year: currentYear,
			eventType: eventType,
			relatedActorId: patronId,
			relatedActorName: patronName,
			relatedFamilyId: sourceFamilyId,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: XjHistoryResult.Change);
		if (announce)
		{
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				body,
				XjAnnouncementCategory.HighRealmInfluence,
				duration: importance >= 5 ? 12f : 9f,
				color: importance >= 5 ? "#FFCF70" : "#B98AD9",
				delayFrames: 1);
		}
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Family | XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	private static long ResolveFamilyId(Actor actor)
	{
		long actorId = ActorId(actor);
		if (actorId <= 0L) return 0L;
		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId) && familyId > 0L) return familyId;
		if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry)
			&& entry.Found && entry.FamilyStableId > 0L) return entry.FamilyStableId;
		return 0L;
	}

	private static string ResolveDaoTuDisplay(Actor actor)
	{
		if (actor?.data == null) return "未定";
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		string display = XjXianGuoSystem.ResolveDaoTuDisplay(actor, (daoTu ?? string.Empty).Trim());
		return string.IsNullOrWhiteSpace(display) ? "未定" : display.Trim();
	}

	private static int RealmOrder(Actor actor)
	{
		return XjFamilyMemberLedger.GetRealmOrder(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
	}

	private static long ActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string SafeName(Actor actor)
	{
		string name = actor?.getName();
		return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
	}
}
