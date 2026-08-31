using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjXianJiAccessor
{
	private const char Separator = '|';
	private const string YiDuiYingXianJiId = "仪对影";
	private readonly struct CachedState
	{
		internal readonly int Count;
		internal readonly string IdsText;
		internal readonly int LastYear;
		internal readonly int RealmLimit;
		internal readonly XjXianJiState State;

		internal CachedState(int count, string idsText, int lastYear, int realmLimit, XjXianJiState state)
		{
			Count = count;
			IdsText = idsText ?? string.Empty;
			LastYear = lastYear;
			RealmLimit = realmLimit;
			State = state;
		}
	}

	// Actor.updateStats 会进入 WorldBox 的并行属性批次；该缓存既会在并行读路径 BuildState 写入，
	// 也会被主线程的仙基变更/换档清理 Remove/Clear。普通 Dictionary 在这种交错下会直接
	// 损坏内部桶并抛 InvalidOperationException，因此运行期缓存必须使用并发容器。
	private static readonly System.Collections.Concurrent.ConcurrentDictionary<long, CachedState> StateCache = new System.Collections.Concurrent.ConcurrentDictionary<long, CachedState>();

	internal static XjXianJiState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjXianJiState(false, 0, Array.Empty<string>(), 0, "ActorInvalid");
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int count);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiLastYear, out int lastYear);
		idsText = idsText ?? string.Empty;
		long actorId = GetActorId(actor);
		int realmLimit = ResolveRealmLimit(actor);
		if (actorId > 0L
			&& StateCache.TryGetValue(actorId, out CachedState cached)
			&& cached.Count == count
			&& cached.LastYear == lastYear
			&& cached.RealmLimit == realmLimit
			&& string.Equals(cached.IdsText, idsText, StringComparison.Ordinal))
		{
			return cached.State;
		}

		string[] ids = SplitIds(idsText);
		if (ids.Length > realmLimit)
		{
			Array.Resize(ref ids, realmLimit);
		}
		int normalizedCount = Math.Max(0, Math.Min(realmLimit, ids.Length));

		XjXianJiState state = new XjXianJiState(
			true,
			normalizedCount,
			ids,
			lastYear,
			"Ok");
		if (actorId > 0L)
		{
			StateCache[actorId] = new CachedState(count, idsText, lastYear, realmLimit, state);
		}
		return state;
	}

	internal static bool HasFive(Actor actor)
	{
		return BuildState(actor).Count >= XjXianJiState.MaxCount;
	}

	internal static int GetEffectiveShenTongCount(Actor actor)
	{
		if (actor?.data == null) return 0;
		XjXianJiState state = BuildState(actor);
		return BuildShenTongProjection(actor, state.Ids).Length;
	}

	internal static bool Add(Actor actor, string id, int currentYear, string gongFaName = "", string gongFaSource = "仙基参悟")
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		id = XjXianJiCatalog.NormalizeXianJiId(id);
		bool forbiddenYiDuiYing = IsForbiddenYiDuiYingForMirror(actor, id);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string authoritativeDaoTu);
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			authoritativeDaoTu = XjXianGuoSystem.MingYangDaoTu;
		}
		else if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(authoritativeDaoTu, out string visibleDaoTu))
		{
			authoritativeDaoTu = visibleDaoTu;
		}
		if (string.Equals(authoritativeDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			return false;
		}
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			XjXianJiPoolKind imperialPoolKind = XjXianJiCatalog.GetPoolKind(authoritativeDaoTu, id);
			if (imperialPoolKind != XjXianJiPoolKind.Native && imperialPoolKind != XjXianJiPoolKind.Lower)
			{
				// 帝明阳的五仙基从源头只收本道上位/下位，不收相邻道途与其他道途。
				// 所有自然、传法、洞天、人丹与调试入口最终都会经过 Add，故不会
				// 修到第五门以后才出现“仙基结构只能导向闰位”的死路。
				return false;
			}
		}

		XjXianJiState state = BuildState(actor);
		// 全局硬不变量：第1—3门只能是“当前修炼源道途”的上位神通。
		// 过去这里只在随机 picker 中保证，传法/洞天/调试/旧兼容入口可以直接 Add
		// 异道神通，从而制造前三门异道、后两门本道的反向闰位结构。现在在唯一写口封死。
		if (state.Count < 3
			&& XjXianJiCatalog.GetPoolKind(authoritativeDaoTu, id) != XjXianJiPoolKind.Native)
		{
			return false;
		}
		if (forbiddenYiDuiYing)
		{
			// 仪对影本身不能再修出仪对影。自然悟法遇到该候选时直接换成
			// 同道另一门合法上位神通，避免角色因最终闸门拒绝而长期卡住；
			// 明确指定功法/仙基的外部入口则拒绝，不擅自篡改玩家或传承选择。
			if (!string.IsNullOrWhiteSpace(gongFaName)) return false;
			string replacement = PickYiDuiYingMirrorReplacement(actor, authoritativeDaoTu, state.Ids, currentYear);
			if (string.IsNullOrWhiteSpace(replacement)) return false;
			id = replacement;
		}
		int realmLimit = ResolveRealmLimit(actor);
		if (realmLimit <= 0 || state.Count >= realmLimit || Contains(state.Ids, id))
		{
			return false;
		}
		if (actor.hasTrait("ChuShen8"))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			if (!XjXianJiCatalog.IsDaoZhuGrantAllowed(daoTu, id, state.Ids))
			{
				return false;
			}
		}

		// 一部真实功法只能映射一门仙基/神通。先确保功法记录已经持久化，
		// 再写仙基；任何入口（自然、龙属、阴司、手动）都不能绕过该边界。
		if (!XjActorGongFaCollection.EnsureForXianJi(actor, id, currentYear, gongFaName, gongFaSource))
		{
			return false;
		}

		int oldLength = state.Ids.Length;
		string[] nextIds = new string[Math.Min(realmLimit, oldLength + 1)];
		for (int i = 0; i < oldLength && i < nextIds.Length - 1; i++)
		{
			nextIds[i] = state.Ids[i];
		}

		nextIds[nextIds.Length - 1] = id;
		string joined = string.Join(Separator.ToString(), nextIds);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, nextIds.Length);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds,
			string.Join(Separator.ToString(), BuildShenTongProjection(actor, nextIds)));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, currentYear));
		XjXianJiOpportunitySchedule.OnCollectionChanged(actor, nextIds.Length, currentYear);
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "XianJiAdd");
		XjLongShuSystem.RefreshTitleAfterXianJiChange(actor);
		XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, nextIds.Length);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "XianJiSnapshot");
		XjHighRealmDaoStateService.UpdateIntentionFromShenTong(actor, authoritativeDaoTu, BuildState(actor));
		TryEvaluateShenDanMethodAfterXianJiChange(actor, currentYear);

		// 异道神通是紫府后两门参悟中的合法结果，保留其功法映射并记录因果。
		if (XjXianJiCatalog.GetPoolKind(authoritativeDaoTu, id) == XjXianJiPoolKind.Other
			&& XjXianJiCatalog.TryResolveOwningDaoTu(id, out string owningDaoTu))
		{
			XjOtherShenTongDeceptionEventService.Record(
				actor, Math.Max(0, currentYear), owningDaoTu, id, gongFaSource);
		}
		return true;
	}


	/// <summary>
	/// 权柄、道统或旧档迁移引发神通真形改变时，原子替换仙基名称及其功法映射。
	/// 不改变仙基数量，也不重新抽取功法，避免同一角色凭变化额外获得第六门神通。
	/// </summary>
	internal static bool TryReplace(Actor actor, string oldId, string newId, int currentYear, string source)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
		{
			return false;
		}

		oldId = XjXianJiCatalog.NormalizeXianJiId(oldId);
		newId = XjXianJiCatalog.NormalizeXianJiId(newId);
		if (IsForbiddenYiDuiYingForMirror(actor, newId)) return false;
		if (string.Equals(oldId, newId, StringComparison.Ordinal)) return false;

		XjXianJiState state = BuildState(actor);
		int replaceIndex = -1;
		for (int i = 0; i < state.Ids.Length; i++)
		{
			if (string.Equals(state.Ids[i], oldId, StringComparison.Ordinal)) replaceIndex = i;
			if (string.Equals(state.Ids[i], newId, StringComparison.Ordinal)) return false;
		}
		if (replaceIndex < 0) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (XjXianGuoSystem.IsDiMingYang(actor)) daoTu = XjXianGuoSystem.MingYangDaoTu;
		else if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string visibleDaoTu)) daoTu = visibleDaoTu;

		XjXianJiPoolKind replacementKind = XjXianJiCatalog.GetPoolKind(daoTu, newId);
		if (replaceIndex < 3 && replacementKind != XjXianJiPoolKind.Native) return false;
		if (XjXianGuoSystem.IsDiMingYang(actor)
			&& replacementKind != XjXianJiPoolKind.Native
			&& replacementKind != XjXianJiPoolKind.Lower) return false;

		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjActorGongFaCollection.TryRemapXianJiMappings(
				actor,
				daoTu,
				new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal) { [oldId] = newId },
				string.IsNullOrWhiteSpace(source) ? "神通真形变化" : source.Trim(),
				out _))
		{
			return false;
		}

		string[] next = new string[state.Ids.Length];
		Array.Copy(state.Ids, next, state.Ids.Length);
		next[replaceIndex] = newId;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, string.Join(Separator.ToString(), next));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds,
			string.Join(Separator.ToString(), BuildShenTongProjection(actor, next)));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, currentYear));
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, string.IsNullOrWhiteSpace(source) ? "ShenTongMutation" : source.Trim());
		XjLongShuSystem.RefreshTitleAfterXianJiChange(actor);
		XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, next.Length);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ShenTongMutationSnapshot");
		XjHighRealmDaoStateService.UpdateIntentionFromShenTong(actor, daoTu, BuildState(actor));
		return true;
	}


	private static void TryEvaluateShenDanMethodAfterXianJiChange(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjCultivationPathRules.IsFuQiYangXing(actor)) return;
		XjXianJiState state = BuildState(actor);
		if (state.Count < XjXianJiState.MaxCount) return;
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (!qiuJinFa.Found || !qiuJinFa.Ready) return;
		XjShenDanMethodSystem.OnQiuJinFaReady(
			actor, qiuJinFa, Math.Max(1, currentYear > 0 ? currentYear : qiuJinFa.LastYear));
	}


	internal static bool ReconcileRealmLimit(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int storedCount);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenTongIds, out string shenTongIds);
		bool aliasMigrated = TryRepairLegacyAliases(actor, idsText ?? string.Empty, out string normalizedIdsText);
		string[] rawIds = SplitIds(aliasMigrated ? normalizedIdsText : (idsText ?? string.Empty));
		// 闰位的当前 DaoTu 是“修炼根道”，果位显道只存在于高境身份字段。
		// 旧版曾把当前 DaoTu 写成显道，导致下面前三门池归位按显道反向重写神通。
		bool runWeiRootMigrated = TryRepairRunWeiCultivationRoot(actor);
		int realmLimit = ResolveRealmLimit(actor);
		if (realmLimit < rawIds.Length)
		{
			Array.Resize(ref rawIds, realmLimit);
		}

		bool zheQiCanonMigrated = TryRepairZheQiCanonAssignments(actor, rawIds);

		// 第1—3门按既定规则只能是本道途上位，因此只修这三个确定性槽位。
		// 第4、5门允许本道途下位、相邻道途上位和其他道途上位池，绝不做猜测性替换。
		bool poolOwnershipMigrated = TryRepairLegacyFirstThreePoolAssignments(actor, rawIds);
		bool daoZhuPoolMigrated = TryRepairDaoZhuPreferredAssignments(actor, rawIds);
		// 已成闰位旧档统一收成“四根一显”。3+2 以及反向 1+4 都不能继续作为
		// 合法历史形态留在运行态，否则后续读档/功法对账会再次漂移。
		bool runWeiCompositionMigrated = TryRepairRunWeiFourPlusOneAssignments(actor, rawIds);

		string joined = string.Join(Separator.ToString(), rawIds);
		string projectedShenTongIds = string.Join(Separator.ToString(), BuildShenTongProjection(actor, rawIds));
		bool changed = aliasMigrated
			|| runWeiRootMigrated
			|| zheQiCanonMigrated
			|| poolOwnershipMigrated
			|| daoZhuPoolMigrated
			|| runWeiCompositionMigrated
			|| storedCount != rawIds.Length
			|| !string.Equals((idsText ?? string.Empty).Trim(), joined, StringComparison.Ordinal)
			|| !string.Equals((shenTongIds ?? string.Empty).Trim(), projectedShenTongIds, StringComparison.Ordinal);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string intentionDaoTu);
		if (XjXianGuoSystem.IsDiMingYang(actor)) intentionDaoTu = XjXianGuoSystem.MingYangDaoTu;
		else if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(intentionDaoTu, out string intentionDisplayDaoTu))
			intentionDaoTu = intentionDisplayDaoTu;
		XjHighRealmDaoStateService.UpdateIntentionFromShenTong(
			actor, intentionDaoTu, new XjXianJiState(true, rawIds.Length, rawIds, 0, "Reconcile"));
		if (!changed)
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, rawIds.Length);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds, projectedShenTongIds);
		if (storedCount != rawIds.Length)
		{
			XjXianJiOpportunitySchedule.OnCollectionChanged(
				actor,
				rawIds.Length,
				XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
		}
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(
			actor,
			runWeiCompositionMigrated ? "0.9.9.8闰位四根一显迁移"
				: runWeiRootMigrated ? "0.9.9.8闰位根道归位"
				: daoZhuPoolMigrated ? "0.9.6.4道主神通收口迁移"
				: poolOwnershipMigrated ? "0.9.5.3神通池归位迁移"
				: zheQiCanonMigrated ? "0.9.6.5谪炁五上位归位"
				: aliasMigrated ? "0.9.6.6长庚神通更名迁移" : "RealmLimit");
		if (runWeiRootMigrated
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string repairedDaoTu))
		{
			XjVisibleTraitSync.SyncDaoTuTrait(actor, repairedDaoTu);
		}
		return true;
	}


	private static bool TryRepairRunWeiCultivationRoot(Actor actor)
	{
		if (actor?.data == null) return false;
		XjJinDanState carrier = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (!carrier.Found
			|| !string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(carrier.GuoWei),
				XjGuoWeiCalculator.RunWei,
				StringComparison.Ordinal)
			|| !XjHighRealmDaoStateService.TryReadPersistedIntercalaryIdentity(
				actor, out string sourceDaoTu, out _))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
		string current = (currentDaoTu ?? string.Empty).Trim();
		string source = (sourceDaoTu ?? string.Empty).Trim();
		if (source.Length == 0 || string.Equals(current, source, StringComparison.Ordinal)) return false;

		XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, source, false);
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string repaired)
			&& string.Equals((repaired ?? string.Empty).Trim(), source, StringComparison.Ordinal);
	}

	private static bool TryRepairRunWeiFourPlusOneAssignments(Actor actor, string[] ids)
	{
		if (actor?.data == null || ids == null || ids.Length != XjXianJiState.MaxCount) return false;
		XjJinDanState carrier = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (!carrier.Found
			|| !string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(carrier.GuoWei),
				XjGuoWeiCalculator.RunWei,
				StringComparison.Ordinal)
			|| !XjHighRealmDaoStateService.TryReadPersistedIntercalaryIdentity(
				actor, out string sourceDaoTu, out string manifestDaoTu))
		{
			return false;
		}

		string source = (sourceDaoTu ?? string.Empty).Trim();
		string manifest = (manifestDaoTu ?? string.Empty).Trim();
		if (source.Length == 0 || manifest.Length == 0 || string.Equals(source, manifest, StringComparison.Ordinal)) return false;

		System.Collections.Generic.List<string> sourceIds = new System.Collections.Generic.List<string>(4);
		string manifestId = string.Empty;
		System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < ids.Length; i++)
		{
			string id = (ids[i] ?? string.Empty).Trim();
			if (id.Length == 0 || !seen.Add(id)
				|| !XjXianJiCatalog.TryGetDefinition(id, out XjShenTongDefinition definition)
				|| definition == null || definition.Tier != XjShenTongTier.Upper) continue;

			if (XjXianJiCatalog.GetPoolKind(source, id) == XjXianJiPoolKind.Native)
			{
				if (sourceIds.Count < 4) sourceIds.Add(id);
				continue;
			}
			if (manifestId.Length == 0
				&& XjXianJiCatalog.TryResolveOwningDaoTu(id, out string owner)
				&& string.Equals((owner ?? string.Empty).Trim(), manifest, StringComparison.Ordinal))
			{
				manifestId = id;
			}
		}

		long actorId = GetActorId(actor);
		int guard = 0;
		while (sourceIds.Count < 4 && guard++ < 12)
		{
			System.Collections.Generic.List<string> occupied = new System.Collections.Generic.List<string>(sourceIds);
			if (manifestId.Length > 0) occupied.Add(manifestId);
			if (!XjXianJiCatalog.TryPickUpperForProgression(
				source, sourceIds.Count + 1, actorId + guard * 17L, occupied.ToArray(), out string picked)
				|| string.IsNullOrWhiteSpace(picked)) break;
			if (!sourceIds.Contains(picked)) sourceIds.Add(picked);
		}

		if (manifestId.Length == 0)
		{
			System.Collections.Generic.List<string> occupied = new System.Collections.Generic.List<string>(sourceIds);
			guard = 0;
			while (manifestId.Length == 0 && guard++ < 12
				&& XjXianJiCatalog.TryPickUpperForProgression(
					manifest, 1, actorId + guard * 31L, occupied.ToArray(), out string picked))
			{
				if (!string.IsNullOrWhiteSpace(picked) && !occupied.Contains(picked)) manifestId = picked;
			}
		}

		if (sourceIds.Count != 4 || manifestId.Length == 0) return false;
		string[] desired = { sourceIds[0], sourceIds[1], sourceIds[2], sourceIds[3], manifestId };
		bool changed = false;
		for (int i = 0; i < ids.Length; i++)
		{
			if (!string.Equals((ids[i] ?? string.Empty).Trim(), desired[i], StringComparison.Ordinal))
			{
				changed = true;
				break;
			}
		}
		if (!changed) return false;

		System.Collections.Generic.List<string> removed = new System.Collections.Generic.List<string>();
		System.Collections.Generic.List<string> added = new System.Collections.Generic.List<string>();
		for (int i = 0; i < ids.Length; i++)
		{
			string oldId = (ids[i] ?? string.Empty).Trim();
			if (oldId.Length > 0 && !Contains(desired, oldId) && !removed.Contains(oldId)) removed.Add(oldId);
			string newId = desired[i];
			if (newId.Length > 0 && !Contains(ids, newId) && !added.Contains(newId)) added.Add(newId);
		}

		if (removed.Count != added.Count) return false;
		if (removed.Count > 0)
		{
			System.Collections.Generic.Dictionary<string, string> replacements =
				new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
			for (int i = 0; i < removed.Count; i++) replacements[removed[i]] = added[i];
			if (!XjActorGongFaCollection.TryRemapXianJiMappings(
				actor, source, replacements, "0.9.9.8闰位四根一显迁移", out _)) return false;
		}

		Array.Copy(desired, ids, desired.Length);
		return true;
	}

	private static bool TryRepairZheQiCanonAssignments(Actor actor, string[] ids)
	{
		if (actor?.data == null || ids == null || ids.Length == 0
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)) return false;
		daoTu = (daoTu ?? string.Empty).Trim();
		if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string visibleDaoTu)) daoTu = visibleDaoTu;
		if (!string.Equals(daoTu, "谪炁", StringComparison.Ordinal)) return false;

		string[] obsolete = { "谪仙落" };
		string[] canonicalUpper = { "藏壑舟", "薄虞渊", "忘川歌", "惘乾坤", "盼天赐" };
		bool hasObsolete = false;
		for (int i = 0; i < ids.Length; i++)
		{
			for (int j = 0; j < obsolete.Length; j++)
			{
				if (string.Equals(ids[i], obsolete[j], StringComparison.Ordinal)) { hasObsolete = true; break; }
			}
		}
		if (!hasObsolete) return false;

		string[] repaired = new string[ids.Length];
		Array.Copy(ids, repaired, ids.Length);
		System.Collections.Generic.HashSet<string> occupied = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < repaired.Length; i++)
		{
			bool legacy = false;
			for (int j = 0; j < obsolete.Length; j++)
				if (string.Equals(repaired[i], obsolete[j], StringComparison.Ordinal)) { legacy = true; break; }
			if (!legacy && !string.IsNullOrWhiteSpace(repaired[i])) occupied.Add(repaired[i]);
		}

		System.Collections.Generic.Dictionary<string, string> replacements =
			new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
		long actorId = GetActorId(actor);
		for (int i = 0; i < repaired.Length; i++)
		{
			string oldId = repaired[i];
			bool legacy = false;
			for (int j = 0; j < obsolete.Length; j++)
				if (string.Equals(oldId, obsolete[j], StringComparison.Ordinal)) { legacy = true; break; }
			if (!legacy) continue;

			string replacement = string.Empty;
			for (int j = 0; j < canonicalUpper.Length; j++)
			{
				if (!occupied.Contains(canonicalUpper[j])) { replacement = canonicalUpper[j]; break; }
			}
			if (replacement.Length == 0)
			{
				string[] existing = new string[occupied.Count];
				occupied.CopyTo(existing);
				XjXianJiCatalog.TryPickForProgression(
					"谪炁", i + 1, actorId + (i + 1) * 97L, existing, false, out replacement);
			}
			if (string.IsNullOrWhiteSpace(replacement) || occupied.Contains(replacement)) continue;
			repaired[i] = replacement;
			occupied.Add(replacement);
			replacements[oldId] = replacement;
		}
		if (replacements.Count == 0) return false;
		if (!XjActorGongFaCollection.TryRemapXianJiMappings(
			actor, daoTu, replacements, "0.9.6.5谪炁五上位归位", out _)) return false;
		Array.Copy(repaired, ids, ids.Length);
		return true;
	}

	private static bool TryRepairLegacyAliases(Actor actor, string idsText, out string normalizedIdsText)
	{
		normalizedIdsText = idsText ?? string.Empty;
		if (actor?.data == null || string.IsNullOrWhiteSpace(idsText)) return false;

		string[] raw = idsText.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
		if (raw.Length == 0) return false;
		int count = Math.Min(XjXianJiState.MaxCount, raw.Length);
		string[] normalized = new string[count];
		System.Collections.Generic.Dictionary<string, string> replacements =
			new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
		for (int i = 0; i < count; i++)
		{
			string oldId = (raw[i] ?? string.Empty).Trim();
			string newId = XjXianJiCatalog.NormalizeXianJiId(oldId);
			normalized[i] = newId;
			if (!string.IsNullOrWhiteSpace(oldId)
				&& !string.IsNullOrWhiteSpace(newId)
				&& !string.Equals(oldId, newId, StringComparison.Ordinal))
			{
				replacements[oldId] = newId;
			}
		}
		if (replacements.Count == 0) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (XjXianGuoSystem.IsDiMingYang(actor)) daoTu = XjXianGuoSystem.MingYangDaoTu;
		else if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string displayDaoTu)) daoTu = displayDaoTu;
		if (string.IsNullOrWhiteSpace(daoTu)
			|| !XjActorGongFaCollection.TryRemapXianJiMappings(
				actor, daoTu, replacements, "神通旧名归一迁移", out _))
		{
			return false;
		}

		normalizedIdsText = string.Join(Separator.ToString(), normalized);
		return true;
	}

	private static bool TryRepairLegacyFirstThreePoolAssignments(Actor actor, string[] ids)
	{
		if (actor?.data == null || ids == null || ids.Length == 0
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		daoTu = daoTu.Trim();
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			daoTu = XjXianGuoSystem.MingYangDaoTu;
		}
		else if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string displayDaoTu))
		{
			daoTu = displayDaoTu;
		}
		else if (!XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu, out _))
		{
			return false;
		}

		string[] repaired = new string[ids.Length];
		Array.Copy(ids, repaired, ids.Length);
		System.Collections.Generic.Dictionary<string, string> replacements =
			new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
		int fixedSlotCount = Math.Min(3, repaired.Length);
		long actorId = GetActorId(actor);

		for (int i = 0; i < fixedSlotCount; i++)
		{
			string current = (repaired[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(current)
				|| XjXianJiCatalog.GetPoolKind(daoTu, current) == XjXianJiPoolKind.Native)
			{
				continue;
			}

			string[] occupied = BuildMigrationOccupiedIds(repaired, i);
			if (!XjXianJiCatalog.TryPickUpperForProgression(
				daoTu,
				i + 1,
				actorId,
				occupied,
				out string replacement)
				|| string.IsNullOrWhiteSpace(replacement)
				|| Contains(occupied, replacement))
			{
				continue;
			}

			repaired[i] = replacement;
			replacements[current] = replacement;
		}

		if (replacements.Count == 0)
		{
			return false;
		}

		if (!XjActorGongFaCollection.TryRemapXianJiMappings(
				actor,
				daoTu,
				replacements,
				"0.9.5.3神通池归位迁移",
				out _))
		{
			return false;
		}

		Array.Copy(repaired, ids, ids.Length);
		return true;
	}

	private static bool TryRepairDaoZhuPreferredAssignments(Actor actor, string[] ids)
	{
		if (actor?.data == null || ids == null || ids.Length == 0 || !actor.hasTrait("ChuShen8")
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		daoTu = daoTu.Trim();
		if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string displayDaoTu)) daoTu = displayDaoTu;

		long actorId = GetActorId(actor);
		System.Collections.Generic.List<string> preferred = new System.Collections.Generic.List<string>(ids.Length);
		System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < ids.Length; i++)
		{
			string current = (ids[i] ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(current)
				&& XjXianJiCatalog.GetPoolKind(daoTu, current) == XjXianJiPoolKind.Native
				&& seen.Add(current)) preferred.Add(current);
		}

		int guard = 0;
		while (preferred.Count < ids.Length && guard++ < XjXianJiState.MaxCount + 2
			&& XjXianJiCatalog.TryPickUpperForProgression(
				daoTu, preferred.Count + 1, actorId + guard * 17L, preferred.ToArray(), out string upper))
		{
			if (!string.IsNullOrWhiteSpace(upper) && seen.Add(upper)) preferred.Add(upper);
		}

		for (int i = 0; i < ids.Length && preferred.Count < ids.Length; i++)
		{
			string current = (ids[i] ?? string.Empty).Trim();
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(daoTu, current);
			if (!string.IsNullOrWhiteSpace(current)
				&& (kind == XjXianJiPoolKind.Lower || kind == XjXianJiPoolKind.Adjacent)
				&& seen.Add(current)) preferred.Add(current);
		}

		guard = 0;
		while (preferred.Count < ids.Length && guard++ < XjXianJiState.MaxCount + 4
			&& XjXianJiCatalog.TryPickDaoZhuForProgression(
				daoTu, preferred.Count + 1, actorId + guard * 31L, preferred.ToArray(), true, out string fallback))
		{
			if (!string.IsNullOrWhiteSpace(fallback) && seen.Add(fallback)) preferred.Add(fallback);
		}
		if (preferred.Count != ids.Length) return false;

		bool changed = false;
		for (int i = 0; i < ids.Length; i++)
		{
			if (!string.Equals((ids[i] ?? string.Empty).Trim(), preferred[i], StringComparison.Ordinal))
			{
				changed = true;
				break;
			}
		}
		if (!changed) return false;

		System.Collections.Generic.List<string> removed = new System.Collections.Generic.List<string>();
		System.Collections.Generic.List<string> added = new System.Collections.Generic.List<string>();
		for (int i = 0; i < ids.Length; i++)
		{
			string oldId = (ids[i] ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(oldId) && !preferred.Contains(oldId)) removed.Add(oldId);
			if (!string.IsNullOrWhiteSpace(preferred[i]) && !Contains(ids, preferred[i])) added.Add(preferred[i]);
		}
		System.Collections.Generic.Dictionary<string, string> replacements =
			new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
		for (int i = 0; i < removed.Count && i < added.Count; i++) replacements[removed[i]] = added[i];
		if (replacements.Count > 0
			&& !XjActorGongFaCollection.TryRemapXianJiMappings(
				actor, daoTu, replacements, "0.9.6.4道主神通收口迁移", out _))
		{
			return false;
		}

		for (int i = 0; i < ids.Length; i++) ids[i] = preferred[i];
		return true;
	}

	private static string[] BuildMigrationOccupiedIds(string[] ids, int excludedIndex)
	{
		System.Collections.Generic.List<string> occupied = new System.Collections.Generic.List<string>();
		for (int i = 0; ids != null && i < ids.Length; i++)
		{
			if (i == excludedIndex) continue;
			string id = (ids[i] ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(id) && !occupied.Contains(id))
			{
				occupied.Add(id);
			}
		}
		return occupied.ToArray();
	}

	internal static bool RestoreSnapshot(Actor actor, string idsText, int lastYear)
	{
		if (actor?.data == null)
		{
			return false;
		}
		string[] raw = SplitIds(idsText ?? string.Empty);
		int realmLimit = ResolveRealmLimit(actor);
		System.Collections.Generic.List<string> normalized = new System.Collections.Generic.List<string>(Math.Min(realmLimit, raw.Length));
		System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < raw.Length && normalized.Count < realmLimit; i++)
		{
			string id = XjXianJiCatalog.NormalizeXianJiId((raw[i] ?? string.Empty).Trim());
			if (!string.IsNullOrWhiteSpace(id)
				&& !IsForbiddenYiDuiYingForMirror(actor, id)
				&& seen.Add(id))
			{
				normalized.Add(id);
			}
		}
		string joined = string.Join(Separator.ToString(), normalized);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, normalized.Count);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds,
			string.Join(Separator.ToString(), BuildShenTongProjection(actor, normalized.ToArray())));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, lastYear));
		XjXianJiOpportunitySchedule.OnCollectionChanged(
			actor,
			normalized.Count,
			lastYear > 0
				? lastYear
				: XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
		Forget(actor);
		return true;
	}

	/// <summary>
	/// 仪对影角色本身不能再拥有“仪对影”仙基。旧档或旁路写入若留下该仙基，
	/// 优先换成本人当前道途的另一门合法上位/下位神通；没有候选时才移除。
	/// </summary>
	internal static bool RepairForbiddenYiDuiYingForMirror(Actor actor, int currentYear)
	{
		if (actor?.data == null || !IsYiDuiYingMirrorActor(actor)) return false;
		XjXianJiState state = BuildState(actor);
		if (!Contains(state.Ids, YiDuiYingXianJiId)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(daoTu, out string visibleDaoTu)) daoTu = visibleDaoTu;
		string replacement = PickYiDuiYingMirrorReplacement(actor, daoTu, state.Ids, currentYear);
		if (!string.IsNullOrWhiteSpace(replacement)
			&& TryReplace(actor, YiDuiYingXianJiId, replacement, Math.Max(0, currentYear), "仪对影禁递归"))
		{
			return true;
		}

		System.Collections.Generic.List<string> next = new System.Collections.Generic.List<string>(state.Ids.Length);
		for (int i = 0; i < state.Ids.Length; i++)
		{
			string id = (state.Ids[i] ?? string.Empty).Trim();
			if (id.Length > 0 && !string.Equals(id, YiDuiYingXianJiId, StringComparison.Ordinal) && !next.Contains(id)) next.Add(id);
		}
		string joined = string.Join(Separator.ToString(), next);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, next.Count);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds,
			string.Join(Separator.ToString(), BuildShenTongProjection(actor, next.ToArray())));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, currentYear));
		XjXianJiOpportunitySchedule.OnCollectionChanged(actor, next.Count, Math.Max(0, currentYear));
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "YiDuiYingNoRecursion");
		XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, next.Count);
		if (!string.IsNullOrWhiteSpace(daoTu)) XjHighRealmDaoStateService.UpdateIntentionFromShenTong(actor, daoTu, BuildState(actor));
		return true;
	}

	private static string PickYiDuiYingMirrorReplacement(Actor actor, string daoTu, string[] existingIds, int currentYear)
	{
		if (string.IsNullOrWhiteSpace(daoTu)) return string.Empty;
		System.Collections.Generic.List<string> candidates = new System.Collections.Generic.List<string>();
		AddMirrorReplacementCandidates(candidates, XjXianJiCatalog.GetUpperPool(daoTu), existingIds);
		if (candidates.Count == 0) AddMirrorReplacementCandidates(candidates, XjXianJiCatalog.GetLowerPool(daoTu), existingIds);
		if (candidates.Count == 0) return string.Empty;
		candidates.Sort(StringComparer.Ordinal);
		long actorId = GetActorId(actor);
		return candidates[XjDeterministicHash.PositiveIndex(actorId + Math.Max(0, currentYear), "yiduiying_no_recursion|" + daoTu, candidates.Count)];
	}

	private static void AddMirrorReplacementCandidates(System.Collections.Generic.List<string> target, string[] pool, string[] existingIds)
	{
		for (int i = 0; pool != null && i < pool.Length; i++)
		{
			string id = XjXianJiCatalog.NormalizeXianJiId(pool[i]);
			if (string.IsNullOrWhiteSpace(id)
				|| string.Equals(id, YiDuiYingXianJiId, StringComparison.Ordinal)
				|| Contains(existingIds, id)
				|| target.Contains(id)) continue;
			target.Add(id);
		}
	}

	private static bool IsForbiddenYiDuiYingForMirror(Actor actor, string id)
	{
		return IsYiDuiYingMirrorActor(actor)
			&& string.Equals(XjXianJiCatalog.NormalizeXianJiId(id), YiDuiYingXianJiId, StringComparison.Ordinal);
	}

	private static bool IsYiDuiYingMirrorActor(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingSourceActorId, out int sourceActorId)
			&& sourceActorId > 0;
	}

	internal static void RefreshShenTongProjection(Actor actor)
	{
		if (actor?.data == null) return;
		string[] ids = ReadRawIds(actor);
		string projected = string.Join(Separator.ToString(), BuildShenTongProjection(actor, ids));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenTongIds, out string stored);
		if (!string.Equals((stored ?? string.Empty).Trim(), projected, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds, projected);
		}
	}

	private static string[] BuildShenTongProjection(Actor actor, string[] xianJiIds)
	{
		return XjQingXuanKongZhengSystem.BuildRaisedShenTongProjection(actor, xianJiIds ?? Array.Empty<string>());
	}

	internal static string[] ReadRawIds(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText))
		{
			return Array.Empty<string>();
		}
		return SplitIds(idsText ?? string.Empty);
	}

	private static int ResolveRealmLimit(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			// 高境与五门仙基是同一条强事实。境界写入、修法路径补录和缓存失效
			// 存在极短顺序窗口时，不能因为 CultivationPath 尚未同步就把容量判为 0，
			// 否则 ReconcileRealmLimit 会永久清空仙基，并连带删除真实功法集合。
			return XjXianJiState.MaxCount;
		}
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return 1;
		}
		return 0;
	}

	internal static void Forget(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId > 0L)
		{
			StateCache.TryRemove(actorId, out _);
		}
	}

	internal static void ClearRuntimeCache()
	{
		StateCache.Clear();
	}

	private static string[] SplitIds(string idsText)
	{
		if (string.IsNullOrWhiteSpace(idsText))
		{
			return Array.Empty<string>();
		}

		string[] raw = idsText.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
		int count = Math.Min(XjXianJiState.MaxCount, raw.Length);
		string[] result = new string[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = XjXianJiCatalog.NormalizeXianJiId(raw[i]);
		}

		return result;
	}

	private static bool Contains(string[] ids, string id)
	{
		if (ids == null)
		{
			return false;
		}

		for (int i = 0; i < ids.Length; i++)
		{
			if (string.Equals(ids[i], id, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
