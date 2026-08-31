using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.WeaponArt;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 长庚果位建立后，紫府金丹道对剑意作出的五种紫金解释。
/// 〖意堪身〗沿用原著名；其余四项为模组补足的三字剑道神通。
/// 所有角色都必须自行完成整套推演，
/// 不从家族、宗门或已有长庚修士处直接借得半成品神通。
/// </summary>
internal static class XjZiJinSwordDaoCatalog
{
	internal const string DaoTu = "长庚";
	internal const int FullMask = (1 << 5) - 1;
	internal static readonly string[] RequiredShenTongIds =
	{
		"意堪身",
		"照寒锋",
		"斩青冥",
		"剑归元",
		"破天门"
	};

	internal static bool IsLongGeng(string daoTu)
	{
		return string.Equals((daoTu ?? string.Empty).Trim(), DaoTu, StringComparison.Ordinal);
	}

	internal static bool HasExactShenTongSet(in XjXianJiState state)
	{
		if (!state.Found || state.Count != RequiredShenTongIds.Length || state.Ids == null)
		{
			return false;
		}
		for (int requiredIndex = 0; requiredIndex < RequiredShenTongIds.Length; requiredIndex++)
		{
			bool found = false;
			for (int actorIndex = 0; actorIndex < state.Ids.Length; actorIndex++)
			{
				if (string.Equals(state.Ids[actorIndex], RequiredShenTongIds[requiredIndex], StringComparison.Ordinal))
				{
					found = true;
					break;
				}
			}
			if (!found) return false;
		}
		return true;
	}

	internal static string JoinRequiredIds() => string.Join("|", RequiredShenTongIds);

	internal static string GetNameByOrdinal(int ordinal)
	{
		return ordinal >= 1 && ordinal <= RequiredShenTongIds.Length
			? RequiredShenTongIds[ordinal - 1]
			: string.Empty;
	}
}

internal static class XjZiJinSwordDaoSystem
{
	private static readonly int[] ProjectMinimumYears = { 12, 18, 24, 32, 40 };
	private static readonly int[] ProjectMaximumYears = { 22, 30, 40, 50, 65 };

	private const int DecisionNone = 0;
	private const int DecisionActive = 1;
	private const int DecisionDeclined = 2;

	internal static bool IsBaseEligible(Actor actor)
	{
		return actor?.data != null
			&& XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.LongGeng)
			&& XjCultivationPathRules.IsZiFuJinDan(actor)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjWeaponArtSystem.TryGetSwordIntent(actor, out _)
			&& !IsCompletedProfile(actor);
	}

	/// <summary>
	/// 长庚显世只提供后世参悟的可能，不强制所有紫府剑修改换原道途。
	/// 每名符合条件者只进行一次稳定选择，选择投入后才暂停原紫府链。
	/// </summary>
	internal static void TryEvaluateResearchChoice(Actor actor, int currentYear)
	{
		if (!IsBaseEligible(actor) || currentYear <= 0) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, out int decision);
		if (decision == DecisionActive || decision == DecisionDeclined) return;

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		float mingShuFactor = XjBreakthroughRules.CalculateMingShuFactor(actor, XjRealmIds.JinDan);
		float chance = Math.Clamp(0.25f
			+ 0.20f * XjDaoHuiPolicy.Normalize01(huiGuang)
			+ 0.15f * mingShuFactor, 0.25f, 0.60f);
		long actorId = ((BaseSystemData)actor.data).id;
		bool active = XjDeterministicHash.Roll01(actorId, currentYear, "zijin_sword_research", "choice") < chance;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, active ? DecisionActive : DecisionDeclined);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecisionYear, currentYear);
		if (active) XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, currentYear);
	}

	internal static bool ShouldManage(Actor actor)
	{
		if (!IsBaseEligible(actor)) return false;
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordResearchDecision, out int decision)
			&& decision == DecisionActive;
	}

	internal static bool IsResearchInProgress(Actor actor)
	{
		return ShouldManage(actor);
	}

	internal static bool CanEnterLongGengDaoTu(Actor actor)
	{
		if (actor?.data == null
			|| !XjDaoTuManifestRegistry.CanManifestZiJin(XjDaoTuRootIds.LongGeng)
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !XjWeaponArtSystem.TryGetSwordIntent(actor, out _))
		{
			return false;
		}
		int mask = ReadMask(actor);
		if (mask != XjZiJinSwordDaoCatalog.FullMask)
		{
			return false;
		}
		return XjZiJinSwordDaoCatalog.HasExactShenTongSet(XjXianJiAccessor.BuildState(actor));
	}

	internal static void TickActor(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (!ShouldManage(actor) || currentYear <= 0) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordLastAnnualYear, out int lastYear)
			&& lastYear == currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordLastAnnualYear, currentYear);

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, out int startedYear);
		if (startedYear <= 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, currentYear);
		}

		int mask = ReadMask(actor);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, out int currentOrdinal);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, out int completeYear);
		if (currentOrdinal > 0 && completeYear > 0)
		{
			if (currentYear < completeYear) return;
			if (currentOrdinal <= XjZiJinSwordDaoCatalog.RequiredShenTongIds.Length)
			{
				int bit = 1 << (currentOrdinal - 1);
				if ((mask & bit) == 0)
				{
					mask |= bit;
					XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordResearchMask, mask);
					// 这里只记录后台推演完成度。五项尚未形成正式仙基前，
					// 不得在列传中宣称角色已经拥有相应神通。
				}
			}
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectStartYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, 0);
		}

		if (mask == XjZiJinSwordDaoCatalog.FullMask)
		{
			TryCommitLongGengDaoTu(actor, currentYear);
			return;
		}

		int nextOrdinal = ResolveNextMissingOrdinal(mask);
		if (nextOrdinal <= 0) return;
		int duration = ResolveProjectYears(actor, nextOrdinal, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, nextOrdinal);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectStartYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, currentYear + duration);
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor)) return string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		bool completed = XjZiJinSwordDaoCatalog.IsLongGeng(daoTu)
			&& XjZiJinSwordDaoCatalog.HasExactShenTongSet(XjXianJiAccessor.BuildState(actor));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordResearchStartedYear, out int startedYear);
		if (!completed && startedYear <= 0) return string.Empty;

		StringBuilder builder = new StringBuilder(192);
		XjWeaponArtSystem.TryGetSwordIntent(actor, out string swordIntent);
		builder.Append("一己剑意：").AppendLine(string.IsNullOrWhiteSpace(swordIntent) ? "未明" : swordIntent.Trim());
		int mask = completed ? XjZiJinSwordDaoCatalog.FullMask : ReadMask(actor);
		int count = CountBits(mask);
		builder.Append("剑道神通：").Append(count.ToString(CultureInfo.InvariantCulture)).Append('/').AppendLine("5");
		if (count > 0)
		{
			List<string> names = new List<string>(5);
			for (int i = 1; i <= 5; i++) if ((mask & (1 << (i - 1))) != 0) names.Add(XjZiJinSwordDaoCatalog.GetNameByOrdinal(i));
			builder.Append("已推演：").AppendLine(string.Join("、", names));
		}
		if (completed)
		{
			builder.AppendLine("道途：长庚");
			builder.Append("求金：仅可求余位");
			return builder.ToString().TrimEnd();
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordCurrentOrdinal, out int ordinal);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordProjectCompleteYear, out int completeYear);
		if (ordinal > 0 && completeYear > 0)
		{
			int currentYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			builder.Append("正在推演：〖").Append(XjZiJinSwordDaoCatalog.GetNameByOrdinal(ordinal)).AppendLine("〗");
			builder.Append("尚需：").Append(Math.Max(0, completeYear - currentYear).ToString(CultureInfo.InvariantCulture)).Append("年");
		}
		else
		{
			builder.Append("状态：等待下一项神通推演");
		}
		return builder.ToString().TrimEnd();
	}

	private static bool IsCompletedProfile(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !XjZiJinSwordDaoCatalog.IsLongGeng(daoTu))
		{
			return false;
		}
		return XjZiJinSwordDaoCatalog.HasExactShenTongSet(XjXianJiAccessor.BuildState(actor));
	}

	private static bool TryCommitLongGengDaoTu(Actor actor, int currentYear)
	{
		if (actor?.data == null || ReadMask(actor) != XjZiJinSwordDaoCatalog.FullMask) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string oldDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string oldXianJiIds);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiLastYear, out int oldXianJiYear);
		QiuJinSnapshot oldQiuJin = QiuJinSnapshot.Capture(actor);
		if (!XjActorGongFaCollection.TryExportSerialized(actor, out int oldGongFaVersion, out string oldGongFaJson))
		{
			return false;
		}

		bool success = XjXianJiAccessor.RestoreSnapshot(actor, XjZiJinSwordDaoCatalog.JoinRequiredIds(), currentYear)
			&& XjActorGongFaCollection.ReplaceAllForManualDaoTu(
				actor,
				XjZiJinSwordDaoCatalog.DaoTu,
				XjZiJinSwordDaoCatalog.RequiredShenTongIds,
				"自创长庚剑道神通")
			&& XjActorGongFaCollection.TryPrepareManualJinDanGrade5Set(
				actor,
				XjZiJinSwordDaoCatalog.DaoTu,
				"长庚五法齐备",
				out _)
			&& XjCultivationStateTransitions.TrySetDaoTu(actor, XjZiJinSwordDaoCatalog.DaoTu, false);
		if (!success)
		{
			XjXianJiAccessor.RestoreSnapshot(actor, oldXianJiIds ?? string.Empty, oldXianJiYear);
			XjActorGongFaCollection.TryRestoreSerialized(actor, oldGongFaVersion, oldGongFaJson, "长庚道途提交回滚");
			XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, oldDaoTu ?? string.Empty, false);
			oldQiuJin.Restore(actor);
			return false;
		}

		XjQiuJinFaAccessor.Clear(actor, "长庚五法齐备后重新推演求金法");
		XjQiuJinFaSystem.ActivateEligibility(actor, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ZiJinSwordCompletedYear, currentYear);
		long actorId = ((BaseSystemData)actor.data).id;
		XjDaoTuManifestRegistry.MarkZiJinManifested(XjDaoTuRootIds.LongGeng, actorId, currentYear);
		for (int i = 0; i < XjZiJinSwordDaoCatalog.RequiredShenTongIds.Length; i++)
		{
			string source = i == 0
				? "以一己剑意为根，将剑意纳入紫府神通"
				: "沿长庚果位显世后的剑理，自行推演创造";
			XjThreeBookWriter.RecordShenTongComprehended(
				actor, XjZiJinSwordDaoCatalog.RequiredShenTongIds[i], currentYear, source);
		}
		XjBroadcastSystem.AnnounceShenTongBatch(
			actor, XjZiJinSwordDaoCatalog.RequiredShenTongIds, "长庚剑理圆满");
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZiJinSwordDaoComplete");
		XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, 5);
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		try
		{
			actor.clearTraitCache();
			actor.setStatsDirty();
			actor.updateStats();
		}
		catch (System.Exception xjCaught304_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjZiJinSwordDaoSystem.cs:304", xjCaught304_1);
			
			XjCombatHotPathCache.Refresh(actor);
		}
		return true;
	}

	private readonly struct QiuJinSnapshot
	{
		private readonly string _name;
		private readonly string _sourceGongFaName;
		private readonly int _sourceGongFaGrade;
		private readonly string _sourceDaoTu;
		private readonly string _boundAuthority;
		private readonly string _origin;
		private readonly int _ready;
		private readonly int _lastYear;
		private readonly int _eligibilityYear;
		private readonly int _lastExecutionYear;
		private readonly int _lastLogicalAttemptYear;
		private readonly int _failureCount;
		private readonly string _lastFailureReason;

		private QiuJinSnapshot(
			string name,
			string sourceGongFaName,
			int sourceGongFaGrade,
			string sourceDaoTu,
			string boundAuthority,
			string origin,
			int ready,
			int lastYear,
			int eligibilityYear,
			int lastExecutionYear,
			int lastLogicalAttemptYear,
			int failureCount,
			string lastFailureReason)
		{
			_name = name ?? string.Empty;
			_sourceGongFaName = sourceGongFaName ?? string.Empty;
			_sourceGongFaGrade = Math.Max(0, sourceGongFaGrade);
			_sourceDaoTu = sourceDaoTu ?? string.Empty;
			_boundAuthority = boundAuthority ?? string.Empty;
			_origin = origin ?? string.Empty;
			_ready = ready > 0 ? 1 : 0;
			_lastYear = Math.Max(0, lastYear);
			_eligibilityYear = Math.Max(0, eligibilityYear);
			_lastExecutionYear = Math.Max(0, lastExecutionYear);
			_lastLogicalAttemptYear = Math.Max(0, lastLogicalAttemptYear);
			_failureCount = Math.Max(0, failureCount);
			_lastFailureReason = lastFailureReason ?? string.Empty;
		}

		internal static QiuJinSnapshot Capture(Actor actor)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaName, out string name);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaName, out string sourceGongFaName);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, out int sourceGongFaGrade);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, out string sourceDaoTu);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority, out string boundAuthority);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaOrigin, out string origin);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaReady, out int ready);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, out int lastYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaEligibilityYear, out int eligibilityYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaLastExecutionYear, out int lastExecutionYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear, out int lastLogicalAttemptYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, out int failureCount);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, out string lastFailureReason);
			return new QiuJinSnapshot(
				name, sourceGongFaName, sourceGongFaGrade, sourceDaoTu, boundAuthority, origin, ready,
				lastYear, eligibilityYear, lastExecutionYear, lastLogicalAttemptYear, failureCount, lastFailureReason);
		}

		internal void Restore(Actor actor)
		{
			if (actor?.data == null) return;
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaName, _name);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaName, _sourceGongFaName);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, _sourceGongFaGrade);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, _sourceDaoTu);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority, _boundAuthority);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaOrigin, _origin);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaReady, _ready);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, _lastYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaEligibilityYear, _eligibilityYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastExecutionYear, _lastExecutionYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear, _lastLogicalAttemptYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, _failureCount);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, _lastFailureReason);
		}
	}

	private static int ResolveNextMissingOrdinal(int mask)
	{
		for (int i = 0; i < XjZiJinSwordDaoCatalog.RequiredShenTongIds.Length; i++)
		{
			if ((mask & (1 << i)) == 0) return i + 1;
		}
		return 0;
	}

	private static int ResolveProjectYears(Actor actor, int ordinal, int currentYear)
	{
		int index = Math.Max(0, Math.Min(ProjectMinimumYears.Length - 1, ordinal - 1));
		int min = ProjectMinimumYears[index];
		int max = ProjectMaximumYears[index];
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		float factor = aptitude >= 6 ? 0.82f : aptitude >= 5 ? 0.91f : 1f;
		factor *= huiGuang >= 100f ? 0.85f : huiGuang >= 70f ? 0.93f : huiGuang >= 40f ? 1f : 1.10f;
		long actorId = ((BaseSystemData)actor.data).id;
		int baseYears = min + XjDeterministicHash.PositiveIndex(actorId + currentYear * 31L + ordinal * 1009L,
			"zijin_sword_shentong_project", Math.Max(1, max - min + 1));
		return Math.Max(8, (int)Math.Ceiling(baseYears * factor));
	}

	private static int ReadMask(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ZiJinSwordResearchMask, out int mask);
		return Math.Max(0, Math.Min(XjZiJinSwordDaoCatalog.FullMask, mask));
	}

	private static int CountBits(int value)
	{
		int count = 0;
		for (int i = 0; i < 5; i++) if ((value & (1 << i)) != 0) count++;
		return count;
	}
}
