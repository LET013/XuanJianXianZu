using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 异道神通只能以明确的误导事件进入角色履历。查询严格限制在角色附近，
/// 找不到真实施术者时以“来历不明的修士”记录，不为此扫描全世界角色。
/// </summary>
internal static class XjOtherShenTongDeceptionEventService
{
	private const int SearchRadius = 24;
	private const int MaxCandidates = 64;
	private const int MaxResults = 12;

	internal static void Record(
		Actor victim, int currentYear, string ownerDaoTu, string shenTongName, string source)
	{
		if (!XjSafeCore.IsAliveActor(victim) || currentYear <= 0) return;
		Actor deceiver = ResolveDeceiver(victim, ownerDaoTu, shenTongName, currentYear);
		long victimId = GetActorId(victim);
		string method = XjDeterministicHash.PositiveIndex(
			victimId + currentYear + XjDeterministicHash.StableHash(shenTongName ?? string.Empty),
			"other_shentong_deception_method", 2) == 0 ? "蛊惑" : "欺骗";
		bool highRealmVictim = XjRealmSuppression.GetRealmTier(victim) >= XjRealmSuppression.TierZiFu;
		string deceiverName = deceiver == null
			? (highRealmVictim ? "一名来历不明的紫府真人" : "一名来历不明的修士")
			: XjStringHelper.ActorNameWithoutRealmSuffix(deceiver, "无名修士");

		XjChronicleWriter.RecordOtherShenTongMisled(
			victim, currentYear, ownerDaoTu, shenTongName, deceiverName, method);
		XjThreeBookWriter.RecordOtherShenTongDeception(
			victim, deceiver, currentYear, ownerDaoTu, shenTongName, method, source);
		XjSemanticDiagnostics.RecordEvent("other_shentong_deception", method);
	}

	private static Actor ResolveDeceiver(
		Actor victim, string ownerDaoTu, string shenTongName, int currentYear)
	{
		WorldTile tile;
		try { tile = ((BaseSimObject)victim).current_tile; } catch (System.Exception xjCaught47_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjOtherShenTongDeceptionEventService.cs:47", xjCaught47_1);
			 tile = null; }
		if (tile == null || string.IsNullOrWhiteSpace(ownerDaoTu)) return null;
		long victimId = GetActorId(victim);
		bool requireHighRealmGuide = XjRealmSuppression.GetRealmTier(victim) >= XjRealmSuppression.TierZiFu;
		IReadOnlyList<Actor> candidates = XjLocalActorQuery.Collect(
			tile, SearchRadius, MaxCandidates, MaxResults, candidate =>
			{
				if (!XjSafeCore.IsAliveActor(candidate) || GetActorId(candidate) == victimId) return false;
				// 紫府层次的异途神通不可能由低境界修士完成引导。
				// 真人、真君通过统一境界投影自然计入紫府、金丹层次。
				if (requireHighRealmGuide
					&& XjRealmSuppression.GetRealmTier(candidate) < XjRealmSuppression.TierZiFu) return false;
				if (!XjActorAccessor.TryGetString(candidate, XjActorDataKeys.DaoTu, out string candidateDaoTu)) return false;
				return string.Equals((candidateDaoTu ?? string.Empty).Trim(), ownerDaoTu.Trim(), StringComparison.Ordinal);
			});
		if (candidates == null || candidates.Count == 0) return null;

		// 优先选取真正掌握该神通者；附近只有同道途修士时，仍可视为其
		// 以残篇、伪法或误导性讲解诱使受害者误入异道。
		List<Actor> exactHolders = new List<Actor>();
		for (int i = 0; i < candidates.Count; i++)
		{
			XjXianJiState state = XjXianJiAccessor.BuildState(candidates[i]);
			if (Contains(state.Ids, shenTongName)) exactHolders.Add(candidates[i]);
		}
		IReadOnlyList<Actor> selection = exactHolders.Count > 0 ? exactHolders : candidates;
		int index = XjDeterministicHash.PositiveIndex(
			victimId + currentYear, ownerDaoTu + "|other_shentong_deceiver", selection.Count);
		return selection[index];
	}

	private static bool Contains(string[] ids, string id)
	{
		if (ids == null || string.IsNullOrWhiteSpace(id)) return false;
		for (int i = 0; i < ids.Length; i++)
		{
			if (string.Equals((ids[i] ?? string.Empty).Trim(), id.Trim(), StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch (System.Exception xjCaught91_2) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjOtherShenTongDeceptionEventService.cs:91", xjCaught91_2);
			 return 0L; }
	}
}
