using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 【上修扶持下修求金登位】长期布局。
///
/// 设计原则：
/// 1. 主持者必须是世界中真实存在的金丹/真君羽士/道胎，不接受仙国借境、神丹挂靠投影或虚构主持者；
/// 2. 紫金道从“未过参紫”的真实紫府中择材，随后经历择材、补基、定制求金法、压阵候金；
/// 3. 只有目标真实求金并成功登位才产生天地功绩。探月失败只形成探月记录/道途认识，不伪造功绩；
/// 4. 所有运行入口复用高境修士年度稀疏队列，不增加逐帧或全人口扫描。
/// </summary>
internal static class XjUpperCultivatorGoldSupportSystem
{
	internal const string PurposeMoonProbe = "MoonProbe";
	internal const string PurposeLineageHeir = "LineageHeir";
	internal const string PurposeStrengthenLineage = "StrengthenLineage";
	internal const string PurposeInterfereOtherDao = "InterfereOtherDao";
	internal const string PurposeOther = "Other";

	private const int CheckIntervalYears = 50;
	private const int StartChancePercent = 35;
	private const int MinimumStageYears = 40;
	private const int MaxProjectYears = 350;
	private const int MaxCandidateChecks = 96;
	private const float PreCanZiZhenYuanCeiling = 90000f;
	private const int InfluenceCap = 5;
	private const int MinimumSupportAptitude = 4;
	private const float SupportMingShuFloorRank4 = 45f;
	private const float SupportMingShuFloorRank5 = 50f;
	private const float SupportMingShuFloorRank6 = 55f;

	internal static bool HasAnnualInterest(Actor patron, int currentYear)
	{
		if (!IsQualifiedPatron(patron) || currentYear <= 0) return false;
		long patronId = ActorId(patron);
		int phase = XjDeterministicHash.PositiveIndex(patronId, "gold_support.phase", CheckIntervalYears);
		return (currentYear + phase) % CheckIntervalYears == 0;
	}

	internal static bool IsActiveInYear(Actor patron, int currentYear)
	{
		return HasAnnualInterest(patron, currentYear);
	}

	internal static void TickPatron(Actor patron, int currentYear)
	{
		if (!IsQualifiedPatron(patron) || currentYear <= 0) return;
		if (XjActorAccessor.TryGetLong(patron, XjActorDataKeys.XjGoldSupportTargetActorId, out long targetId)
			&& targetId > 0L)
		{
			AdvanceExisting(patron, targetId, currentYear);
			return;
		}

		long patronId = ActorId(patron);
		if (XjDeterministicHash.PositiveIndex(patronId + currentYear, "gold_support.start", 100) >= StartChancePercent)
			return;
		if (!TrySelectPurposeAndTarget(patron, currentYear, out string purpose, out Actor target)) return;
		StartProject(patron, target, purpose, currentYear);
	}

	private static void AdvanceExisting(Actor patron, long targetId, int currentYear)
	{
		if (!XjActorRegistry.ResolveKnownOrWorld(targetId, out Actor target)
			|| target?.data == null || !target.isAlive())
		{
			RecordProjectEnd(patron, null, string.Empty, currentYear, "扶金布局中断",
				"原先选定的求金人选已经不在人世，上修此前经营的求金布局就此断线。", false);
			ClearProject(patron, null);
			return;
		}
		if (!TryReadActiveProject(target, out Actor resolvedPatron, out string purpose, out int stage,
			out int startedYear, out int lastAdvanceYear, out string targetDaoTu)
			|| ActorId(resolvedPatron) != ActorId(patron))
		{
			ClearProject(patron, target);
			return;
		}

		// 旧扶持局只清理真正低于上乘根骨的异常人选。上乘根骨（xjzz4）现在是合法
		// 扶金对象：其自身求金仍必败，但完整走到“法成候金”后，可以借主持上修
		// 的定制求金法与压阵指引获得真实成丹窗口，因此不能再按旧规则撤局。
		int targetAptitude = Math.Max(0, XjActorCultivationSnapshotBuilder.Build(target).XjZz);
		if (targetAptitude > 0 && targetAptitude < MinimumSupportAptitude)
		{
			RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局重择",
				SafeName(patron) + "重审" + SafeName(target) + "根骨，认为其尚不足以承接求金指引，遂撤去旧局另择其材。", false);
			ClearProject(patron, target);
			return;
		}
		ApplySupportFoundation(target);

		string currentDaoTu = ReadDaoTu(target);
		if (!string.Equals(currentDaoTu, targetDaoTu, StringComparison.Ordinal))
		{
			RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局改弦",
				SafeName(target) + "已经改换道途，原先针对【" + targetDaoTu + "】定下的求金路线失去落点，布局终止。", false);
			ClearProject(patron, target);
			return;
		}

		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(target, XjRealmHelper.GetTraitSnapshotForRouter));
		if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			// 成功金丹会从成功链主动结算；其他偏离（神丹、结璘、转修等）均视为本局未完成登位。
			if (!string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
			{
				RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局失位",
					SafeName(target) + "未沿预定的正统求金路线登位，主持上修未因此获得天地功绩。", false);
				ClearProject(patron, target);
			}
			return;
		}

		XjXianJiState currentXianJiState = XjXianJiAccessor.BuildState(target);
		XjQiuJinFaState currentQiuJinFa = XjQiuJinFaAccessor.BuildState(target);
		if (!CanCompleteGoldRouteUnderSupport(target, targetDaoTu, currentXianJiState)
			|| (!currentQiuJinFa.Ready && !HasAvailableSupportAuthority(targetDaoTu)))
		{
			RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局重择",
				SafeName(target) + "当前已无完整的求金路径，主持上修不再令其空候金门，撤局另择可成之材。", false);
			ClearProject(patron, target);
			return;
		}

		if (startedYear <= 0) startedYear = currentYear;
		if (lastAdvanceYear <= 0) lastAdvanceYear = startedYear;
		if (currentYear - startedYear >= MaxProjectYears)
		{
			RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局散",
				SafeName(target) + "迟迟未能走到求金门前，上修此前投入的扶持渐渐失去时势，本局不记功绩。", false);
			ClearProject(patron, target);
			return;
		}

		if (stage <= 1 && currentYear - lastAdvanceYear >= MinimumStageYears)
		{
			SetStage(target, 2, currentYear);
			AddDaoHui(target, 1f);
			RecordStage(patron, target, purpose, currentYear, "上修扶金·补基授业",
				SafeName(patron) + "正式把" + SafeName(target) + "列入求金培养之选，开始按其根基补法、授业、校正仙基走向。", 3, false);
			stage = 2;
			lastAdvanceYear = currentYear;
		}

		if (stage == 2 && currentYear - lastAdvanceYear >= MinimumStageYears)
		{
			SetStage(target, 3, currentYear);
			AddDaoHui(target, 1f);
			RecordStage(patron, target, purpose, currentYear, "上修扶金·演法定路",
				SafeName(patron) + "开始针对" + SafeName(target) + "的五门仙基、本道位序与求金风险推演专属法门；此后若五门齐备，将优先为其定制求金法。", 4, true);
			stage = 3;
			lastAdvanceYear = currentYear;
		}

		if (stage == 3)
		{
			if (TryProvideCustomizedQiuJinFa(patron, target, purpose, targetDaoTu, currentYear, out bool newlyProvided))
			{
				SetStage(target, 4, currentYear);
				// 法成候金即视为目标本人已经真正掌握这份上修指引；立即固化求金之志，
				// 不把“主持者下一年还活着”当成继续记得这套求金法的条件。
				XjQiuJinIntentSystem.MarkUpperGuidanceMature(target, currentYear);
				if (!newlyProvided)
				{
					RecordStage(patron, target, purpose, currentYear, "上修扶金·法成候金",
						SafeName(target) + "本已有可用求金法，" + SafeName(patron) + "转而据此重定压阵方案，自此只待其真正叩金门。", 4, true);
				}
			}
			else if (!HasAvailableSupportAuthority(targetDaoTu))
			{
				RecordProjectEnd(patron, target, purpose, currentYear, "扶金布局无门",
					SafeName(target) + "五门已备，却因目标道途已无可承的权柄而不能定制求金法；"
						+ "此局到此为止，不把无门之人继续记作候金。", false);
				ClearProject(patron, target);
			}
			return;
		}

		if (stage >= 4)
		{
			// 阶段四不再凭年份自动结算。必须等待 XjJinDan 的真实求金成功/失败事务回调。
			return;
		}
	}

	private static bool TrySelectPurposeAndTarget(Actor patron, int currentYear, out string purpose, out Actor selected)
	{
		purpose = string.Empty;
		selected = null;
		IReadOnlyList<long> ids = XjCultivatorCandidateIndex.GetRealmEnteredIds();
		if (ids == null || ids.Count == 0) return false;
		string patronDaoTu = ReadDaoTu(patron);
		if (patronDaoTu.Length == 0) return false;
		long patronId = ActorId(patron);
		long patronFamilyId = ResolveFamilyId(patron);
		bool patronHoldsZhengWei = HoldsZhengWei(patron, patronDaoTu);

		CandidatePick probe = default;
		CandidatePick heir = default;
		CandidatePick strengthen = default;
		CandidatePick interfere = default;
		CandidatePick other = default;
		int start = XjDeterministicHash.PositiveIndex(patronId + currentYear, "gold_support.candidate_start", ids.Count);
		int checks = Math.Min(ids.Count, MaxCandidateChecks);
		for (int offset = 0; offset < checks; offset++)
		{
			long candidateId = ids[(start + offset) % ids.Count];
			if (candidateId <= 0L || candidateId == patronId
				|| !XjActorRegistry.ResolveKnownOrWorld(candidateId, out Actor actor)
				|| !IsEligibleZiFuCandidate(actor)) continue;
			if (HasLiveGoldSupport(actor)) continue;

			string targetDaoTu = ReadDaoTu(actor);
			if (targetDaoTu.Length == 0) continue;
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			if (snapshot.ZhenYuan >= PreCanZiZhenYuanCeiling) continue;
			int xjzz = Math.Max(0, snapshot.XjZz);
			if (xjzz < MinimumSupportAptitude) continue;
			XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
			// 探月、扶持、干涉的动机可以不同，但都必须先选出一位真正能被
			// 主持者推到金门前的紫府。不能把缺首门神通、或后续神通池已断的
			// 人物写成长期扶金目标，再以“试探”为由让其注定空转。
			if (!CanCompleteGoldRouteUnderSupport(actor, targetDaoTu, xianJiState)
				|| !HasAvailableSupportAuthority(targetDaoTu)) continue;
			int xianJi = xianJiState.Count;
			float score = xjzz * 100f + snapshot.HuiGuang * 3f + snapshot.MingShu + xianJi * 24f;
			long targetFamilyId = ResolveFamilyId(actor);
			bool sameDao = string.Equals(targetDaoTu, patronDaoTu, StringComparison.Ordinal);
			if (!XjGuoWeiRegistry.TryFindActiveAnchor(targetDaoTu, XjGuoWeiCalculator.ZhengWei, out _)) score += 35f;

			// xjzz4-6 均可成为正式扶金对象。五、六档仍因资质评分天然优先；四档只有
			// 在确实更合适、或高档候选不足时才会被选中。四档的价值不来自自身成丹率，
			// 而来自上修完整推进到“法成候金”后为其打开原本不存在的成丹窗口。
			// 探月只用于家族尚未真正接触过的果位。若主持者或候选人的家族
			// 已经有人持有该道正位、闰位或余位，再称“探月”就是重复试探，
			// 应交由继承/壮大/干涉等真实布局处理。
			bool familyAlreadyKnowsTarget = FamilyHoldsActiveDaoPosition(patronFamilyId, targetDaoTu)
				|| FamilyHoldsActiveDaoPosition(targetFamilyId, targetDaoTu);
			if (!familyAlreadyKnowsTarget)
			{
				probe.Consider(actor, score + (!sameDao ? 25f : 0f));
			}
			if (patronHoldsZhengWei && sameDao && snapshot.HuiGuang >= 50f
				&& IsHeirStructureCompatible(actor, targetDaoTu))
				heir.Consider(actor, score + xjzz * 35f + snapshot.HuiGuang * 2f);
			if (sameDao)
				strengthen.Consider(actor, score + 40f);
			else
			{
				XjDaoTuRelationKind relation = XjDaoTuRelationCatalog.Resolve(patronDaoTu, targetDaoTu);
				float relationBonus = relation == XjDaoTuRelationKind.DirectAdjacent ? 50f
					: relation == XjDaoTuRelationKind.Counterpart ? 35f
					: relation == XjDaoTuRelationKind.SameRootRemote ? 25f
					: relation == XjDaoTuRelationKind.ElementAffinity ? 20f : 0f;
				interfere.Consider(actor, score + relationBonus);
			}
			if (patronFamilyId > 0L && targetFamilyId == patronFamilyId)
				other.Consider(actor, score + 30f);
		}

		int roll = XjDeterministicHash.PositiveIndex(patronId + currentYear * 13L, "gold_support.purpose", 100);
		string[] order = roll < 20
			? new[] { PurposeMoonProbe, PurposeLineageHeir, PurposeStrengthenLineage, PurposeInterfereOtherDao, PurposeOther }
			: roll < 40
				? new[] { PurposeLineageHeir, PurposeStrengthenLineage, PurposeInterfereOtherDao, PurposeMoonProbe, PurposeOther }
				: roll < 70
					? new[] { PurposeStrengthenLineage, PurposeLineageHeir, PurposeInterfereOtherDao, PurposeMoonProbe, PurposeOther }
					: roll < 90
						? new[] { PurposeInterfereOtherDao, PurposeStrengthenLineage, PurposeLineageHeir, PurposeMoonProbe, PurposeOther }
						: new[] { PurposeOther, PurposeStrengthenLineage, PurposeLineageHeir, PurposeMoonProbe, PurposeInterfereOtherDao };
		for (int i = 0; i < order.Length; i++)
		{
			CandidatePick pick = order[i] switch
			{
				PurposeMoonProbe => probe,
				PurposeLineageHeir => heir,
				PurposeStrengthenLineage => strengthen,
				PurposeInterfereOtherDao => interfere,
				_ => other
			};
			if (pick.Actor == null) continue;
			purpose = order[i];
			selected = pick.Actor;
			return true;
		}
		return false;
	}

	/// <summary>
	/// 只在高境稀疏候选池内进行的无副作用预检。首门神通必须已经真实存在；
	/// 余下各门则按与实际扶持完全相同的定向选取规则逐门推演。
	/// </summary>
	private static bool CanCompleteGoldRouteUnderSupport(Actor actor, string daoTu, in XjXianJiState state)
	{
		if (actor?.data == null || state.Count <= 0 || string.IsNullOrWhiteSpace(daoTu)) return false;
		if (state.Count >= XjXianJiState.MaxCount) return true;

		List<string> projected = new List<string>(state.Ids ?? Array.Empty<string>());
		long seed = ActorId(actor);
		bool zhengWeiManifested = XjGuoWeiRegistry.TryFindActiveAnchor(
			daoTu, XjGuoWeiCalculator.ZhengWei, out _);
		for (int ordinal = projected.Count + 1; ordinal <= XjXianJiState.MaxCount; ordinal++)
		{
			if (!TryPickGuidedShenTong(
				daoTu, ordinal, seed + ordinal * 4099L, projected.ToArray(), zhengWeiManifested, false,
				out string id)) return false;
			projected.Add(id);
		}
		return true;
	}

	private static bool HasAvailableSupportAuthority(string daoTu)
	{
		IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(daoTu);
		for (int i = 0; i < authorities.Count; i++)
		{
			string authority = authorities[i];
			if (!string.IsNullOrWhiteSpace(authority)
				&& XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(daoTu, authority)) return true;
		}
		return false;
	}

	private static void StartProject(Actor patron, Actor target, string purpose, int currentYear)
	{
		long patronId = ActorId(patron);
		long targetId = ActorId(target);
		string targetDaoTu = ReadDaoTu(target);
		string desiredPosition = string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal)
			? XjGuoWeiCalculator.YuWei
			: string.Empty;
		XjActorAccessor.SetLong(patron, XjActorDataKeys.XjGoldSupportTargetActorId, targetId);
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjGoldSupportPatronActorId, patronId);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportPurpose, purpose);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportStage, 1);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportStartedYear, currentYear);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportLastAdvanceYear, currentYear);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportTargetDaoTu, targetDaoTu);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportDesiredPosition, desiredPosition);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportQiuJinFaProvided, 0);
		ApplySupportFoundation(target);

		string purposeText = PurposeDisplayName(purpose);
		string intent = BuildPurposeIntent(patron, target, purpose, targetDaoTu);
		RecordStage(patron, target, purpose, currentYear, "上修定策·" + purposeText,
			SafeName(patron) + "生出【" + purposeText + "】之意，从尚未过参紫的紫府中选定" + SafeName(target)
			+ "为求金人选。" + intent, 4, true);
	}

	private static string BuildPurposeIntent(Actor patron, Actor target, string purpose, string targetDaoTu)
	{
		if (string.Equals(purpose, PurposeMoonProbe, StringComparison.Ordinal))
			return "此局重在让其真正叩一次【" + targetDaoTu + "】金门，以探果位虚实；对棋子而言并非善意，成败生死都不能取消这次试探。";
		if (string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal))
			return "主持者已握本道正果，欲先把这个天资道行俱佳的后辈推上余位，预作正果承继之人。";
		if (string.Equals(purpose, PurposeStrengthenLineage, StringComparison.Ordinal))
			return "主持者要让本道再添一位真正高境，以壮大道统、扩大自身对本道后继的实际影响。";
		if (string.Equals(purpose, PurposeInterfereOtherDao, StringComparison.Ordinal))
			return "主持者有意扶持此人证入异道，以一名真实登位者为楔，增加自身道统对【" + targetDaoTu + "】的干涉余地。";
		return "此举或出于私恩、家族后手、旧约与临时大局，不另造一套强行解释，只按真实扶持与最终登位结果结算。";
	}

	/// <summary>在正常神通形成周期中插入“上修补基”，不额外创造年度尝试。</summary>
	internal static bool TryResolveGuidedShenTong(
		Actor target,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		out string shenTongId)
	{
		shenTongId = string.Empty;
		if (!TryReadActiveProject(target, out Actor patron, out string purpose, out int stage,
			out _, out _, out string targetDaoTu)
			|| stage < 2 || stage > 4 || state.Count >= XjXianJiState.MaxCount)
			return false;
		int ordinal = state.Count + 1;
		bool zhengWeiManifested = XjGuoWeiRegistry.TryFindActiveAnchor(targetDaoTu, XjGuoWeiCalculator.ZhengWei, out _);

		bool preferYuWei = string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal)
			|| string.Equals(purpose, PurposeStrengthenLineage, StringComparison.Ordinal)
				&& HoldsZhengWei(patron, targetDaoTu);
		return TryPickGuidedShenTong(targetDaoTu, ordinal, ActorId(target) + ActorId(patron), state.Ids,
			zhengWeiManifested, preferYuWei, out shenTongId);
	}

	/// <summary>
	/// 三种扶金动机共用同一条补基链。继承人只是在可证余位时优先安排余位，
	/// 不能据此把探月或干涉对象排除在神通扶持之外。
	/// </summary>
	private static bool TryPickGuidedShenTong(
		string targetDaoTu,
		int ordinal,
		long seed,
		string[] existingIds,
		bool zhengWeiManifested,
		bool preferYuWei,
		out string shenTongId)
	{
		shenTongId = string.Empty;
		if (preferYuWei && ordinal >= XjXianJiState.MaxCount && zhengWeiManifested
			&& XjXianJiCatalog.TryPickLowerForProgression(targetDaoTu, ordinal, seed, existingIds, out string lower)
			&& XjXianJiCatalog.IsAvailableForProgression(targetDaoTu, ordinal, existingIds, true, false, lower))
		{
			shenTongId = lower;
			return true;
		}

		if (XjXianJiCatalog.TryPickUpperForProgression(targetDaoTu, ordinal, seed, existingIds, out string upper)
			&& XjXianJiCatalog.IsAvailableForProgression(targetDaoTu, ordinal, existingIds, zhengWeiManifested, false, upper))
		{
			shenTongId = upper;
			return true;
		}
		return XjXianJiCatalog.TryPickForProgression(
			targetDaoTu, ordinal, seed, existingIds, zhengWeiManifested, true, out shenTongId);
	}

	private static void ApplySupportFoundation(Actor target)
	{
		if (target?.data == null) return;
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(target);
		int aptitude = Math.Max(0, snapshot.XjZz);
		if (aptitude < MinimumSupportAptitude) return;

		// xjzz4 的合法道慧下限本就是 58；这里仅修正旧档/漏初始化，不凭空
		// 把人抬成更高资质。命数则以可追溯的后天命数补至扶金资格线，保留先天命数。
		XjAptitudeEffectRules.EnsureHuiGuangMinimumBoundToAptitude(target, aptitude);
		float minimumMingShu = aptitude >= 6 ? SupportMingShuFloorRank6
			: aptitude == 5 ? SupportMingShuFloorRank5 : SupportMingShuFloorRank4;
		XjActorAccessor.TryGetFloat(target, XjActorDataKeys.MingShu, out float totalMingShu);
		if (totalMingShu < minimumMingShu)
		{
			XjMingShuState.AddAcquired(target, minimumMingShu - Math.Max(0f, totalMingShu));
		}
	}

	private static bool TryProvideCustomizedQiuJinFa(
		Actor patron,
		Actor target,
		string purpose,
		string targetDaoTu,
		int currentYear,
		out bool newlyProvided)
	{
		newlyProvided = false;
		XjQiuJinFaState existing = XjQiuJinFaAccessor.BuildState(target);
		if (existing.Found && existing.Ready) return true;
		if (XjXianJiAccessor.BuildState(target).Count < XjXianJiState.MaxCount) return false;

		long seedBase = ActorId(target) * 17L + ActorId(patron) * 31L + currentYear;
		for (int attempt = 0; attempt < 12; attempt++)
		{
			string name = XjQiuJinFaNameLibrary.GenerateName(targetDaoTu, "上修定制", seedBase + attempt * 7919L);
			if (string.IsNullOrWhiteSpace(name)) continue;
			string boundAuthority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(targetDaoTu, name, string.Empty);
			if (string.IsNullOrWhiteSpace(boundAuthority)
				|| !XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(targetDaoTu, boundAuthority)) continue;
			XjQiuJinFaState state = new XjQiuJinFaState(
				true, name, string.Empty, 0, targetDaoTu, true, currentYear,
				"UpperCultivatorCustomized:" + purpose, boundAuthority);
			XjQiuJinFaAccessor.WriteState(target, state);
			XjQiuJinFaState committed = XjQiuJinFaAccessor.BuildState(target);
			if (!committed.Found || !committed.Ready) continue;
			XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportQiuJinFaProvided, 1);
			XjQiuJinFaSystem.PublishQiuJinFaSuccess(target, committed);
			RecordStage(patron, target, purpose, currentYear, "上修扶金·定制求金法",
				SafeName(patron) + "据" + SafeName(target) + "五门神通与【" + targetDaoTu + "】位序亲自推演，定成《" + committed.Name
				+ "》，并将其所契权柄落到【" + committed.BoundAuthority + "】。自此法已成，只待压阵叩金。", 5, true);
			newlyProvided = true;
			return true;
		}
		return false;
	}

	internal static float ResolveCultivationMultiplier(Actor target)
	{
		if (!TryReadActiveProject(target, out _, out _, out int stage, out _, out _, out _)) return 1f;
		stage = Math.Clamp(stage, 1, 4);
		return stage switch { 1 => 1.12f, 2 => 1.22f, 3 => 1.30f, 4 => 1.35f, _ => 1f };
	}

	/// <summary>返回当前有效扶金指引阶段；0 表示没有有效布局，4 表示已进入法成候金。</summary>
	internal static int ResolveJinDanGuidanceStage(Actor target)
	{
		if (!TryReadActiveProject(target, out _, out _, out int stage, out _, out _, out _)) return 0;
		return Math.Clamp(stage, 1, 4);
	}



	internal static float ResolveJinDanSuccessBonus(Actor target)
	{
		if (!TryReadActiveProject(target, out Actor patron, out string purpose, out int stage, out _, out _, out string targetDaoTu)
			|| stage < 4) return 0f;
		float baseBonus = 0.22f;
		string patronDaoTu = ReadDaoTu(patron);
		int influence = string.Equals(patronDaoTu, targetDaoTu, StringComparison.Ordinal)
			? GetSameDaoInfluence(patron)
			: GetCrossDaoInfluence(patron, targetDaoTu);
		return Math.Min(0.30f, baseBonus + Math.Min(InfluenceCap, Math.Max(0, influence)) * 0.015f);
	}

	internal static void OnJinDanAttemptFailed(Actor target, int currentYear, string reason)
	{
		if (!TryReadActiveProject(target, out Actor patron, out string purpose, out int stage, out _, out _, out string targetDaoTu)
			|| stage < 4) return;
		string resultText;
		if (string.Equals(purpose, PurposeMoonProbe, StringComparison.Ordinal))
		{
			IncrementInt(patron, XjActorDataKeys.XjGoldSupportProbeCount, 1, 9999);
			AddCrossDaoInfluence(patron, targetDaoTu, 1);
			resultText = "这一次叩门已经足够暴露【" + targetDaoTu + "】的部分位势，主持者记下一次探月结果，并提高对此道后续布局的熟悉度；因目标未成功登位，不得天地功绩。";
		}
		else
		{
			resultText = "目标真实求金未成，本局未能把一位高境送上位序，因此无论此前投入多少资源，都不结算天地功绩。";
		}
		RecordProjectEnd(patron, target, purpose, currentYear, "扶金求位·求金未成",
			SafeName(target) + "在上修扶持下真正叩金门，却以【" + NormalizeFailureReason(reason) + "】告终。" + resultText, false);
		ClearProject(patron, target);
	}

	internal static void OnJinDanPromotionSuccess(Actor target, int currentYear)
	{
		if (!TryReadActiveProject(target, out Actor patron, out string purpose, out int stage, out _, out _, out string targetDaoTu)
			|| stage < 4) return;
		XjJinDanState state = XjJinDanAccessor.BuildState(target);
		if (!state.Found) return;
		string positionType = XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei);
		int baseMerit = ResolveSuccessMerit(purpose, positionType);
		bool meritAwarded = XjDaoTaiMeritSystem.TryAwardExternal(patron, baseMerit,
			"扶持" + SafeName(target) + "求金登位（" + PurposeDisplayName(purpose) + "）", currentYear, out int actualMerit);
		IncrementInt(patron, XjActorDataKeys.XjGoldSupportSuccessCount, 1, 9999);

		string influenceText = string.Empty;
		string patronDaoTu = ReadDaoTu(patron);
		if (string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal)
			&& string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			&& string.Equals(patronDaoTu, targetDaoTu, StringComparison.Ordinal))
		{
			AddSameDaoInfluence(patron, 2);
			MarkDesignatedSuccessor(target, patron, targetDaoTu, currentYear);
			influenceText = "本道统影响提高二层，并正式将此余位真君列为正果承继人；待原正果真正空缺，其‘移’修事可直接衔接正位。";
		}
		else if (string.Equals(purpose, PurposeStrengthenLineage, StringComparison.Ordinal))
		{
			AddSameDaoInfluence(patron, 1);
			influenceText = "主持者对本道后继的道统影响提高一层。";
		}
		else if (string.Equals(purpose, PurposeInterfereOtherDao, StringComparison.Ordinal))
		{
			AddCrossDaoInfluence(patron, targetDaoTu, 2);
			influenceText = "主持者对【" + targetDaoTu + "】的干涉影响提高二层，今后再扶持此道求位者会得到更强压阵。";
		}
		else if (string.Equals(purpose, PurposeMoonProbe, StringComparison.Ordinal))
		{
			IncrementInt(patron, XjActorDataKeys.XjGoldSupportProbeCount, 1, 9999);
			AddCrossDaoInfluence(patron, targetDaoTu, 1);
			influenceText = "探月目的亦告完成，主持者记下一次真实位势结果，并提高对此道的布局熟悉度。";
		}

		string meritText = meritAwarded
			? "主持上修因真正扶成一位登位者，获得天地功绩" + actualMerit + "。"
			: "主持者已非需要积攒道胎功绩的金丹/真君位阶，本次不重复结算功绩。";
		string heirDeviation = string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal)
			&& !string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			? "原计划本欲使其先登余位，实际位序有所偏离，因此只按成功扶金结算，不建立正果继承标记。"
			: string.Empty;
		RecordProjectEnd(patron, target, purpose, currentYear, "上修扶金·登位成功",
			SafeName(target) + "在长期培养、补基、定法与压阵之后真实求金成功，登【" + state.GuoWei + "】。"
			+ meritText + influenceText + heirDeviation, true);
		ClearProject(patron, target);
	}

	private static int ResolveSuccessMerit(string purpose, string positionType)
	{
		if (string.Equals(purpose, PurposeLineageHeir, StringComparison.Ordinal))
			return string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal) ? 360 : 180;
		if (string.Equals(purpose, PurposeStrengthenLineage, StringComparison.Ordinal)) return 260;
		if (string.Equals(purpose, PurposeInterfereOtherDao, StringComparison.Ordinal)) return 300;
		if (string.Equals(purpose, PurposeMoonProbe, StringComparison.Ordinal)) return 120;
		return 180;
	}

	internal static bool IsDesignatedSuccessor(Actor actor, string daoTu)
	{
		if (actor?.data == null) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGoldSupportSuccessionDaoTu, out string stored)
			|| !string.Equals(XjDaoTuRelationCatalog.Normalize(stored), XjDaoTuRelationCatalog.Normalize(daoTu), StringComparison.Ordinal)) return false;
		return XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjGoldSupportSuccessionPatronActorId, out long patronId)
			&& patronId > 0L;
	}

	internal static void OnDesignatedSuccessionCompleted(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjGoldSupportSuccessionPatronActorId, 0L);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGoldSupportSuccessionDaoTu, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGoldSupportSuccessionEstablishedYear, 0);
	}

	/// <summary>
	/// 阻道与其他高境事务的语义门禁：主持上修不能亲手截断自己正在经营的扶金目标，
	/// 已经正式立下的正果承继人也仍属于这名上修的既定道统安排。
	/// </summary>
	internal static bool IsProtectedFromPatronObstruction(Actor patron, Actor target)
	{
		if (patron?.data == null || target?.data == null) return false;
		long patronId = ActorId(patron);
		long targetId = ActorId(target);
		if (patronId <= 0L || targetId <= 0L) return false;

		if (XjActorAccessor.TryGetLong(patron, XjActorDataKeys.XjGoldSupportTargetActorId, out long patronTargetId)
			&& patronTargetId == targetId
			&& XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjGoldSupportPatronActorId, out long targetPatronId)
			&& targetPatronId == patronId)
		{
			return true;
		}

		return XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjGoldSupportSuccessionPatronActorId, out long successorPatronId)
			&& successorPatronId == patronId;
	}

	private static void MarkDesignatedSuccessor(Actor target, Actor patron, string daoTu, int currentYear)
	{
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjGoldSupportSuccessionPatronActorId, ActorId(patron));
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportSuccessionDaoTu, daoTu);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportSuccessionEstablishedYear, currentYear);
	}

	internal static int GetSameDaoInfluence(Actor patron)
	{
		if (patron?.data == null) return 0;
		return XjActorAccessor.TryGetInt(patron, XjActorDataKeys.XjGoldSupportSameDaoInfluence, out int value)
			? Math.Clamp(value, 0, InfluenceCap) : 0;
	}

	internal static int GetCrossDaoInfluence(Actor patron, string targetDaoTu)
	{
		if (patron?.data == null || string.IsNullOrWhiteSpace(targetDaoTu)) return 0;
		if (!XjActorAccessor.TryGetString(patron, XjActorDataKeys.XjGoldSupportCrossDaoInfluence, out string raw)
			|| string.IsNullOrWhiteSpace(raw)) return 0;
		string normalized = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
		string[] parts = raw.Split(';');
		for (int i = 0; i < parts.Length; i++)
		{
			int eq = parts[i].IndexOf('=');
			if (eq <= 0) continue;
			string key = XjDaoTuRelationCatalog.Normalize(parts[i].Substring(0, eq));
			if (!string.Equals(key, normalized, StringComparison.Ordinal)) continue;
			if (int.TryParse(parts[i].Substring(eq + 1), out int value)) return Math.Clamp(value, 0, InfluenceCap);
		}
		return 0;
	}

	internal static void AddSameDaoInfluence(Actor patron, int amount)
	{
		if (patron?.data == null || amount <= 0) return;
		XjActorAccessor.SetInt(patron, XjActorDataKeys.XjGoldSupportSameDaoInfluence,
			Math.Min(InfluenceCap, GetSameDaoInfluence(patron) + amount));
	}

	internal static void AddCrossDaoInfluence(Actor patron, string targetDaoTu, int amount)
	{
		if (patron?.data == null || amount <= 0 || string.IsNullOrWhiteSpace(targetDaoTu)) return;
		string normalized = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
		Dictionary<string, int> map = ReadInfluenceMap(patron);
		map.TryGetValue(normalized, out int current);
		map[normalized] = Math.Min(InfluenceCap, Math.Max(0, current) + amount);
		List<string> keys = new List<string>(map.Keys);
		keys.Sort(StringComparer.Ordinal);
		List<string> output = new List<string>(keys.Count);
		for (int i = 0; i < keys.Count; i++) output.Add(keys[i] + "=" + map[keys[i]]);
		XjActorAccessor.SetString(patron, XjActorDataKeys.XjGoldSupportCrossDaoInfluence, string.Join(";", output));
	}

	private static Dictionary<string, int> ReadInfluenceMap(Actor patron)
	{
		Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.Ordinal);
		if (patron?.data == null
			|| !XjActorAccessor.TryGetString(patron, XjActorDataKeys.XjGoldSupportCrossDaoInfluence, out string raw)
			|| string.IsNullOrWhiteSpace(raw)) return result;
		string[] parts = raw.Split(';');
		for (int i = 0; i < parts.Length; i++)
		{
			int eq = parts[i].IndexOf('=');
			if (eq <= 0 || !int.TryParse(parts[i].Substring(eq + 1), out int value)) continue;
			string key = XjDaoTuRelationCatalog.Normalize(parts[i].Substring(0, eq));
			if (key.Length == 0) continue;
			result[key] = Math.Clamp(value, 0, InfluenceCap);
		}
		return result;
	}

	private static bool TryReadActiveProject(
		Actor target,
		out Actor patron,
		out string purpose,
		out int stage,
		out int startedYear,
		out int lastAdvanceYear,
		out string targetDaoTu)
	{
		patron = null;
		purpose = string.Empty;
		stage = 0;
		startedYear = 0;
		lastAdvanceYear = 0;
		targetDaoTu = string.Empty;
		if (target?.data == null || !target.isAlive()
			|| !XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjGoldSupportPatronActorId, out long patronId)
			|| patronId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(patronId, out patron)
			|| !IsQualifiedPatron(patron)
			|| !XjActorAccessor.TryGetLong(patron, XjActorDataKeys.XjGoldSupportTargetActorId, out long patronTargetId)
			|| patronTargetId != ActorId(target)
			|| !XjActorAccessor.TryGetString(target, XjActorDataKeys.XjGoldSupportPurpose, out purpose)
			|| string.IsNullOrWhiteSpace(purpose)
			|| !XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjGoldSupportStage, out stage)
			|| stage <= 0)
		{
			patron = null;
			purpose = string.Empty;
			stage = 0;
			return false;
		}
		XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjGoldSupportStartedYear, out startedYear);
		XjActorAccessor.TryGetInt(target, XjActorDataKeys.XjGoldSupportLastAdvanceYear, out lastAdvanceYear);
		XjActorAccessor.TryGetString(target, XjActorDataKeys.XjGoldSupportTargetDaoTu, out targetDaoTu);
		targetDaoTu = XjDaoTuRelationCatalog.Normalize(targetDaoTu);
		return targetDaoTu.Length > 0;
	}

	private static bool HasLiveGoldSupport(Actor target)
	{
		if (target?.data == null || !XjActorAccessor.TryGetLong(target, XjActorDataKeys.XjGoldSupportPatronActorId, out long patronId)
			|| patronId <= 0L) return false;
		if (XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron) && IsQualifiedPatron(patron)) return true;
		ClearProject(null, target);
		return false;
	}

	private static bool IsHeirStructureCompatible(Actor actor, string daoTu)
	{
		if (actor?.data == null) return false;
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Count >= XjXianJiState.MaxCount) return false;
		string[] upper = XjXianJiCatalog.GetUpperPool(daoTu);
		if (upper == null || upper.Length == 0) return false;
		for (int i = 0; i < state.Ids.Length; i++)
		{
			string id = state.Ids[i];
			bool found = false;
			for (int j = 0; j < upper.Length; j++)
			{
				if (!string.Equals(id, upper[j], StringComparison.Ordinal)) continue;
				found = true;
				break;
			}
			if (!found) return false;
		}
		return true;
	}

	private static bool IsEligibleZiFuCandidate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjXianGuoSystem.IsDiMingYang(actor)
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
		return string.Equals(XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter)), XjRealmIds.ZiFu, StringComparison.Ordinal);
	}

	private static bool IsQualifiedPatron(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		long actorId = ActorId(actor);
		if (actorId <= 0L || XjDaoTaiPresenceArchive.IsBodyArchived(actorId)) return false;
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}

	private static bool HoldsZhengWei(Actor actor, string daoTu)
	{
		if (actor?.data == null) return false;
		XjJinDanState state = XjJinDanAccessor.BuildState(actor);
		if (!state.Found || !string.Equals(ReadDaoTu(actor), XjDaoTuRelationCatalog.Normalize(daoTu), StringComparison.Ordinal)) return false;
		return string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
	}

	private static bool FamilyHoldsActiveDaoPosition(long familyId, string daoTu)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(daoTu)) return false;
		string[] types =
		{
			XjGuoWeiCalculator.ZhengWei,
			XjGuoWeiCalculator.RunWei,
			XjGuoWeiCalculator.YuWei
		};
		for (int index = 0; index < types.Length; index++)
		{
			if (XjGuoWeiRegistry.TryFindActiveAnchor(daoTu, types[index],
				entry => ResolveFamilyId(entry.ActorId) == familyId, out _)) return true;
		}
		return false;
	}

	private static long ResolveFamilyId(long actorId)
	{
		if (actorId <= 0L) return 0L;
		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId) && familyId > 0L) return familyId;
		return XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry entry) && entry.Found
			? Math.Max(0L, entry.FamilyStableId)
			: 0L;
	}

	private static void SetStage(Actor target, int stage, int currentYear)
	{
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportStage, stage);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportLastAdvanceYear, currentYear);
	}

	private static void ClearProject(Actor patron, Actor target)
	{
		if (patron?.data != null) XjActorAccessor.SetLong(patron, XjActorDataKeys.XjGoldSupportTargetActorId, 0L);
		if (target?.data == null) return;
		XjActorAccessor.SetLong(target, XjActorDataKeys.XjGoldSupportPatronActorId, 0L);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportPurpose, string.Empty);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportStage, 0);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportStartedYear, 0);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportLastAdvanceYear, 0);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportTargetDaoTu, string.Empty);
		XjActorAccessor.SetString(target, XjActorDataKeys.XjGoldSupportDesiredPosition, string.Empty);
		XjActorAccessor.SetInt(target, XjActorDataKeys.XjGoldSupportQiuJinFaProvided, 0);
	}

	private static void RecordStage(Actor patron, Actor target, string purpose, int currentYear, string title, string body, int importance, bool announce)
	{
		// 经营过程只写入角色运行状态；史册只收录玩家可据以理解布局的转折。
		// “定策”必须可见，补基、演法等中间推进不挤占天下纪事。
		if (!IsNotableProjectTransition(title)) return;
		RecordProjectEvent(patron, target, purpose, currentYear, title, body, importance, announce, XjHistoryResult.Change);
	}

	private static bool IsNotableProjectTransition(string title)
	{
		return !string.IsNullOrWhiteSpace(title)
			&& (title.StartsWith("上修定策·", StringComparison.Ordinal)
				|| string.Equals(title, "上修扶金·定制求金法", StringComparison.Ordinal)
				|| string.Equals(title, "上修扶金·法成候金", StringComparison.Ordinal));
	}

	private static void RecordProjectEnd(Actor patron, Actor target, string purpose, int currentYear, string title, string body, bool success)
	{
		RecordProjectEvent(patron, target, purpose, currentYear, title, body, success ? 5 : 4, true,
			success ? XjHistoryResult.Success : XjHistoryResult.Failure);
	}

	private static void RecordProjectEvent(
		Actor patron,
		Actor target,
		string purpose,
		int currentYear,
		string title,
		string body,
		int importance,
		bool announce,
		string result)
	{
		long patronId = ActorId(patron);
		long targetId = ActorId(target);
		string patronName = SafeName(patron);
		string targetName = SafeName(target);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			title,
			body,
			importance,
			isProtected: importance >= 5,
			actorId: targetId > 0L ? targetId : patronId,
			actorName: targetId > 0L ? targetName : patronName,
			year: currentYear,
			eventType: "UpperGoldSupport:" + (purpose ?? string.Empty) + ":" + title,
			relatedActorId: patronId,
			relatedActorName: patronName,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			result: result);
		if (patron?.data != null) XjWorldHistoryRegistry.AddActorEvent(patron, body, XjEventIconCatalog.JinDanUpgrade);
		if (target?.data != null) XjWorldHistoryRegistry.AddActorEvent(target, body, XjEventIconCatalog.JinDanUpgrade);
		if (announce)
		{
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				body, XjAnnouncementCategory.HighRealmInfluence,
				duration: importance >= 5 ? 12f : 9f,
				color: importance >= 5 ? "#FFCF70" : "#C8B27A",
				delayFrames: 1,
				iconId: XjEventIconCatalog.JinDanUpgrade);
		}
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History | XjCodexDirtyFlags.CenturyAnnals);
	}

	private static string NormalizeFailureReason(string reason)
	{
		string value = (reason ?? string.Empty).Trim();
		return value switch
		{
			"BreakthroughFailed" => "金门未开",
			"NoGuoWei" => "五门无位可证",
			"PermanentlyLockedGuoWei" => "位序封锁",
			"GuoWeiOccupiedNoShenDanMethod" => "目标位序已有其主",
			"ShenDanCapacityFull" => "托果旁路亦满",
			_ => value.Length == 0 ? "求金失败" : value
		};
	}

	internal static string PurposeDisplayName(string purpose)
	{
		return purpose switch
		{
			PurposeMoonProbe => "探月行动",
			PurposeLineageHeir => "道统继承人",
			PurposeStrengthenLineage => "壮大本道途",
			PurposeInterfereOtherDao => "干涉其他道途",
			_ => "其他·私恩后手"
		};
	}

	private static void AddDaoHui(Actor actor, float delta)
	{
		if (actor?.data == null || delta <= 0f) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang,
			XjDaoHuiPolicy.Add(value, delta, XjDaoHuiPolicy.RareGrowthCeiling));
	}

	private static void IncrementInt(Actor actor, string key, int delta, int cap)
	{
		if (actor?.data == null || delta == 0) return;
		XjActorAccessor.TryGetInt(actor, key, out int value);
		XjActorAccessor.SetInt(actor, key, Math.Clamp(value + delta, 0, Math.Max(0, cap)));
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

	private static string ReadDaoTu(Actor actor)
	{
		if (actor?.data == null || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)) return string.Empty;
		return XjDaoTuRelationCatalog.Normalize(daoTu);
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

	private struct CandidatePick
	{
		internal Actor Actor;
		private float _score;
		private long _actorId;

		internal void Consider(Actor actor, float score)
		{
			long actorId = XjUpperCultivatorGoldSupportSystem.ActorId(actor);
			if (actorId <= 0L) return;
			if (Actor == null || score > _score || Math.Abs(score - _score) < 0.001f && actorId < _actorId)
			{
				Actor = actor;
				_score = score;
				_actorId = actorId;
			}
		}
	}
}
