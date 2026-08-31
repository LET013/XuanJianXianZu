using System;
using System.Collections.Generic;
using System.Linq;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.History;
namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 每条道途唯一的运行时道统状态。权柄状态只由具体事件改变：
/// 潜=尚未随位序显化，显=已经显世而无人执掌，执=正由本道果位持有者执掌，
/// 藏=持柄者离位后归入道统潜藏，裂/借=外道夺柄后的待融合两面，
/// 失/易=融合成功后的原道失柄与目标道果位易解，归=融合失败或融合完成前夺柄者身死后归还。
/// “易”属于目标道途的果位位格，不属于最初夺柄者本人；夺柄者享受功绩，后继果位继续承接，除非此柄再被外道夺走。
/// 道势兴衰只改变道统活力，不再凭空制造权柄失落。
/// </summary>
internal readonly struct XjAuthorityStateAuditReport
{
	internal readonly int Total;
	internal readonly int Invalid;
	internal readonly int PendingMismatches;
	internal readonly int LostMismatches;
	internal readonly int HolderMismatches;
	internal readonly int NonRootArtifacts;
	internal readonly int CatalogMismatches;
	internal readonly int PairMismatches;
	internal readonly int DuplicateMismatches;

	internal XjAuthorityStateAuditReport(
		int total,
		int invalid,
		int pendingMismatches,
		int lostMismatches,
		int holderMismatches,
		int nonRootArtifacts,
		int catalogMismatches,
		int pairMismatches,
		int duplicateMismatches)
	{
		Total = Math.Max(0, total);
		Invalid = Math.Max(0, invalid);
		PendingMismatches = Math.Max(0, pendingMismatches);
		LostMismatches = Math.Max(0, lostMismatches);
		HolderMismatches = Math.Max(0, holderMismatches);
		NonRootArtifacts = Math.Max(0, nonRootArtifacts);
		CatalogMismatches = Math.Max(0, catalogMismatches);
		PairMismatches = Math.Max(0, pairMismatches);
		DuplicateMismatches = Math.Max(0, duplicateMismatches);
	}

	internal bool IsClean => Invalid == 0;
	internal string Summary => IsClean
		? "状态机正常（" + Total + "项权柄）"
		: "发现" + Invalid + "项异常：待融" + PendingMismatches
			+ "、失柄" + LostMismatches + "、持有人" + HolderMismatches
			+ "、配对" + PairMismatches + "、根柄目录" + CatalogMismatches
			+ "、重复" + DuplicateMismatches + "、旧派生残留" + NonRootArtifacts;
}

internal static class XjDaoLineageStateRegistry
{
	private static readonly Dictionary<string, XjDaoLineageArchiveRecord> ByDaoTu =
		new Dictionary<string, XjDaoLineageArchiveRecord>(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjDaoProofFoundationArchiveData> FoundationsByKey =
		new Dictionary<string, XjDaoProofFoundationArchiveData>(StringComparer.Ordinal);
	private static readonly List<string> WorldTickDaoTuBuffer = new List<string>(64);
	private static int lastWorldTickYear;
	private static int pendingWorldTickYear;
	private static int pendingWorldTickIndex;
	private static int revision;

	internal static int Revision => revision;

	internal static void Clear()
	{
		ByDaoTu.Clear();
		FoundationsByKey.Clear();
		WorldTickDaoTuBuffer.Clear();
		lastWorldTickYear = 0;
		pendingWorldTickYear = 0;
		pendingWorldTickIndex = 0;
		revision = 0;
	}

	internal static XjDaoLineageWorldArchiveData ExportState()
	{
		XjDaoLineageWorldArchiveData state = new XjDaoLineageWorldArchiveData();
		List<string> keys = new List<string>(ByDaoTu.Keys);
		keys.Sort(StringComparer.Ordinal);
		for (int i = 0; i < keys.Count; i++) state.Lineages.Add(Clone(ByDaoTu[keys[i]]));
		List<string> foundationKeys = new List<string>(FoundationsByKey.Keys);
		foundationKeys.Sort(StringComparer.Ordinal);
		for (int i = 0; i < foundationKeys.Count; i++) state.ProofFoundations.Add(Clone(FoundationsByKey[foundationKeys[i]]));
		return state;
	}

	internal static void ImportState(XjDaoLineageWorldArchiveData state)
	{
		ByDaoTu.Clear();
		FoundationsByKey.Clear();
		ClearPendingWorldTick();
		lastWorldTickYear = 0;
		revision++;
		if (state?.Lineages != null)
		{
			for (int i = 0; i < state.Lineages.Count; i++)
			{
				XjDaoLineageArchiveRecord record = state.Lineages[i];
				string daoTu = Normalize(record?.DaoTu);
				if (daoTu.Length == 0 || ByDaoTu.ContainsKey(daoTu)) continue;
				record.DaoTu = daoTu;
				NormalizeRecord(record);
				ByDaoTu[daoTu] = record;
			}
		}
		if (state?.ProofFoundations != null)
		{
			for (int i = 0; i < state.ProofFoundations.Count; i++)
			{
				XjDaoProofFoundationArchiveData foundation = state.ProofFoundations[i];
				if (foundation == null || foundation.CityId <= 0L) continue;
				FoundationsByKey[BuildFoundationKey(foundation)] = foundation;
			}
		}
	}

	internal static IReadOnlyList<XjDaoLineageArchiveRecord> ReadAllLineages()
	{
		List<XjDaoLineageArchiveRecord> result = new List<XjDaoLineageArchiveRecord>(ByDaoTu.Count);
		foreach (XjDaoLineageArchiveRecord record in ByDaoTu.Values)
		{
			if (record != null) result.Add(Clone(record));
		}
		result.Sort((left, right) => string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal));
		return result;
	}

	internal static XjAuthorityStateAuditReport AuditStateMachine()
	{
		int total = 0;
		int invalid = 0;
		int pendingMismatch = 0;
		int lostMismatch = 0;
		int holderMismatch = 0;
		int artifactMismatch = 0;
		int catalogMismatch = 0;
		int pairMismatch = 0;
		int duplicateMismatch = 0;

		IReadOnlyList<XjGuoWeiQuanBingState> states = XjGuoWeiQuanBingRegistry.ReadAllEntries();
		Dictionary<long, XjGuoWeiQuanBingState> activeByActor = new Dictionary<long, XjGuoWeiQuanBingState>();
		for (int i = 0; i < states.Count; i++)
		{
			XjGuoWeiQuanBingState state = states[i];
			if (state.Found && state.ActorId > 0L
				&& string.Equals(state.LifecycleStatus, "Active", StringComparison.Ordinal))
			{
				activeByActor[state.ActorId] = state;
			}
		}

		IReadOnlyList<XjGuoWeiQuanBingLostAuthorityArchiveData> lostRecords =
			XjGuoWeiQuanBingRegistry.ReadLostAuthorityRecords();
		Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData> lostByRoot =
			new Dictionary<string, XjGuoWeiQuanBingLostAuthorityArchiveData>(StringComparer.Ordinal);
		for (int i = 0; i < lostRecords.Count; i++)
		{
			XjGuoWeiQuanBingLostAuthorityArchiveData lost = lostRecords[i];
			if (lost == null) continue;
			lostByRoot[BuildAuditAuthorityKey(lost.SourceDaoTu, lost.Authority)] = lost;
		}

		foreach (XjDaoLineageArchiveRecord record in ByDaoTu.Values)
		{
			if (record?.Authorities == null) continue;
			IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(record.DaoTu);
			HashSet<string> expectedRoots = new HashSet<string>(catalog, StringComparer.Ordinal);
			HashSet<string> seenEntries = new HashSet<string>(StringComparer.Ordinal);
			HashSet<string> seenNativeRoots = new HashSet<string>(StringComparer.Ordinal);

			for (int i = 0; i < record.Authorities.Count; i++)
			{
				XjDaoAuthorityArchiveData authority = record.Authorities[i];
				if (authority == null || string.IsNullOrWhiteSpace(authority.Name)) continue;
				total++;
				bool bad = false;
				bool native = IsNativeRootAuthority(record, authority);
				bool external = IsExternalRootProjection(record, authority);
				string entryKey = Normalize(authority.SourceDaoTu) + "|" + Normalize(authority.Name);
				if (!seenEntries.Add(entryKey))
				{
					duplicateMismatch++;
					bad = true;
				}
				if (native) seenNativeRoots.Add(Normalize(authority.Name));
				if (!native && !external)
				{
					artifactMismatch++;
					bad = true;
				}

				string status = NormalizeStatus(authority.Status);
				if (!IsKnownStatus(status)) bad = true;
				bool nativeOnlyStatus = status == "潜" || status == "显" || status == "执"
					|| status == "藏" || status == "裂" || status == "归" || status == "失";
				bool externalOnlyStatus = status == "借" || status == "易";
				if ((nativeOnlyStatus && !native) || (externalOnlyStatus && !external))
				{
					pairMismatch++;
					bad = true;
				}

				if (status == "执")
				{
					bool validHolder = authority.HolderActorId > 0L
						&& activeByActor.TryGetValue(authority.HolderActorId, out XjGuoWeiQuanBingState nativeHolderState)
						&& string.Equals(Normalize(nativeHolderState.DaoTu), Normalize(record.DaoTu), StringComparison.Ordinal)
						&& string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(nativeHolderState.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
						&& ContainsAuthority(nativeHolderState.LocalQuanBing, authority.Name);
					if (!validHolder)
					{
						holderMismatch++;
						bad = true;
					}
				}
				else if (status == "借")
				{
					bool pending = authority.HolderActorId > 0L
						&& IsBorrowPending(authority.SourceDaoTu, record.DaoTu, authority.Name, authority.HolderActorId);
					XjDaoLineageArchiveRecord sourceRecord = GetExistingRecord(authority.SourceDaoTu);
					XjDaoAuthorityArchiveData sourceRoot = FindNativeAuthority(sourceRecord, authority.Name);
					if (!pending || sourceRoot == null || !string.Equals(sourceRoot.Status, "裂", StringComparison.Ordinal))
					{
						pendingMismatch++;
						bad = true;
					}
				}
				else if (status == "裂")
				{
					if (!TryFindPendingIntegration(record.DaoTu, authority.Name, states, out XjGuoWeiQuanBingState pendingState))
					{
						pendingMismatch++;
						bad = true;
					}
					else
					{
						XjDaoLineageArchiveRecord targetRecord = GetExistingRecord(pendingState.DaoTu);
						XjDaoAuthorityArchiveData targetBorrow = FindExternalAuthority(targetRecord, authority.Name, record.DaoTu);
						if (targetBorrow == null || targetBorrow.HolderActorId != pendingState.ActorId
							|| !string.Equals(targetBorrow.Status, "借", StringComparison.Ordinal))
						{
							pairMismatch++;
							bad = true;
						}
					}
					if (authority.HolderActorId != 0L)
					{
						holderMismatch++;
						bad = true;
					}
				}
				else if (status == "失")
				{
					if (!lostByRoot.TryGetValue(BuildAuditAuthorityKey(record.DaoTu, authority.Name), out XjGuoWeiQuanBingLostAuthorityArchiveData lostRecord)
						|| string.IsNullOrWhiteSpace(lostRecord.TargetDaoTu))
					{
						lostMismatch++;
						bad = true;
					}
					else
					{
						XjDaoLineageArchiveRecord targetRecord = GetExistingRecord(lostRecord.TargetDaoTu);
						XjDaoAuthorityArchiveData targetEasy = FindExternalAuthority(targetRecord, authority.Name, record.DaoTu);
						if (targetEasy == null || !string.Equals(targetEasy.Status, "易", StringComparison.Ordinal))
						{
							pairMismatch++;
							bad = true;
						}
					}
					if (authority.HolderActorId != 0L)
					{
						holderMismatch++;
						bad = true;
					}
				}
				else if (status == "易")
				{
					bool lostPair = lostByRoot.TryGetValue(
						BuildAuditAuthorityKey(authority.SourceDaoTu, authority.Name), out XjGuoWeiQuanBingLostAuthorityArchiveData integratedLostRecord)
						&& string.Equals(Normalize(integratedLostRecord.TargetDaoTu), Normalize(record.DaoTu), StringComparison.Ordinal);
					if (!lostPair)
					{
						lostMismatch++;
						bad = true;
					}
					if (authority.HolderActorId > 0L)
					{
						bool validHolder = activeByActor.TryGetValue(authority.HolderActorId, out XjGuoWeiQuanBingState integratedHolderState)
							&& string.Equals(Normalize(integratedHolderState.DaoTu), Normalize(record.DaoTu), StringComparison.Ordinal)
							&& ContainsAuthority(integratedHolderState.SeizedQuanBing, authority.Name);
						if (!validHolder)
						{
							holderMismatch++;
							bad = true;
						}
					}
				}
				else if (authority.HolderActorId > 0L || !string.IsNullOrWhiteSpace(authority.HolderName))
				{
					holderMismatch++;
					bad = true;
				}
				if (bad) invalid++;
			}

			if (expectedRoots.Count != seenNativeRoots.Count || !expectedRoots.SetEquals(seenNativeRoots))
			{
				catalogMismatch++;
				invalid++;
			}
		}
		return new XjAuthorityStateAuditReport(
			total, invalid, pendingMismatch, lostMismatch, holderMismatch,
			artifactMismatch, catalogMismatch, pairMismatch, duplicateMismatch);
	}

	private static XjDaoLineageArchiveRecord GetExistingRecord(string daoTu)
	{
		string normalized = Normalize(daoTu);
		return normalized.Length > 0 && ByDaoTu.TryGetValue(normalized, out XjDaoLineageArchiveRecord record)
			? record : null;
	}

	private static bool TryFindPendingIntegration(
		string sourceDaoTu,
		string authorityName,
		IReadOnlyList<XjGuoWeiQuanBingState> states,
		out XjGuoWeiQuanBingState pending)
	{
		pending = default;
		if (states == null) return false;
		for (int i = 0; i < states.Count; i++)
		{
			XjGuoWeiQuanBingState state = states[i];
			if (!state.Found || !state.IntegrationRetreatActive
				|| !string.Equals(state.LifecycleStatus, "Active", StringComparison.Ordinal)
				|| !string.Equals(Normalize(state.PendingExternalZhengWeiDaoTu), Normalize(sourceDaoTu), StringComparison.Ordinal)
				|| !ContainsAuthority(state.ForeignQuanBing, authorityName)) continue;
			pending = state;
			return true;
		}
		return false;
	}

	private static string BuildAuditAuthorityKey(string sourceDaoTu, string authorityName)
	{
		return Normalize(sourceDaoTu) + "|" + Normalize(authorityName);
	}

	internal static XjDaoLineageArchiveRecord GetOrCreate(string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (normalized.Length == 0) return null;
		if (ByDaoTu.TryGetValue(normalized, out XjDaoLineageArchiveRecord record)) return record;
		record = new XjDaoLineageArchiveRecord
		{
			DaoTu = normalized,
			CoreRevision = 1,
			Vitality = Math.Max(20, Math.Min(100, XjFruitPositionWorldState.GetMomentum(normalized))),
			Phase = "守成",
			CoreDoctrine = BuildDefaultDoctrine(normalized),
			ShenTongBias = "守本"
		};
		IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(normalized);
		for (int i = 0; i < authorities.Count; i++)
		{
			record.Authorities.Add(new XjDaoAuthorityArchiveData
			{
				Name = authorities[i],
				Status = i < 2 ? "显" : "潜",
				SourceDaoTu = normalized
			});
		}
		ByDaoTu[normalized] = record;
		Touch();
		return record;
	}

	internal static void ApplyShenTongBias(
		string daoTu,
		int ordinal,
		ref int ownWeight,
		ref int lowerWeight,
		ref int adjacentWeight,
		ref int otherWeight)
	{
		if (ordinal < 3) return;
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record == null) return;
		if (record.Vitality >= 75 || string.Equals(record.Phase, "中兴", StringComparison.Ordinal))
		{
			ownWeight += ordinal >= 4 ? 12 : 6;
			lowerWeight = Math.Max(0, lowerWeight - 3);
			otherWeight = Math.Max(0, otherWeight - 2);
		}
		else if (record.Vitality <= 35)
		{
			ownWeight = Math.Max(1, ownWeight - 10);
			lowerWeight += 4;
			adjacentWeight += 5;
			if (ordinal >= 4) otherWeight += 2;
		}
		else if (record.Vitality <= 50)
		{
			ownWeight = Math.Max(1, ownWeight - 4);
			adjacentWeight += 3;
		}

		// 权柄状态会反向塑造后继神通：失、藏、裂使本道难显，归、易使新解释进入主流。
		int obstructed = 0;
		int renewed = 0;
		for (int i = 0; i < record.Authorities.Count; i++)
		{
			string status = record.Authorities[i]?.Status ?? string.Empty;
			if (string.Equals(status, "失", StringComparison.Ordinal)
				|| string.Equals(status, "藏", StringComparison.Ordinal)
				|| string.Equals(status, "裂", StringComparison.Ordinal)) obstructed++;
			else if (string.Equals(status, "归", StringComparison.Ordinal)
				|| string.Equals(status, "易", StringComparison.Ordinal)) renewed++;
		}
		if (obstructed > 0)
		{
			ownWeight = Math.Max(1, ownWeight - Math.Min(8, obstructed * 2));
			adjacentWeight += Math.Min(6, obstructed);
		}
		if (renewed > 0)
		{
			ownWeight += Math.Min(8, renewed * 2);
			otherWeight = Math.Max(0, otherWeight - Math.Min(3, renewed));
		}
	}

	internal static int ResolveShenTongCandidateWeight(
		string ownerDaoTu,
		string shenTongId,
		out string authorityName,
		out string authorityStatus)
	{
		authorityName = string.Empty;
		authorityStatus = string.Empty;
		string owner = Normalize(ownerDaoTu);
		if (owner.Length == 0) return 8;
		IReadOnlyList<string> catalog = XjGuoWeiAuthorityCatalog.Get(owner);
		if (catalog == null || catalog.Count == 0) return 8;

		// 并古、渊照、虹霞等后置道途必须按明确法理承柄。旧实现对神通名做哈希后
		// 随机落到六柄之一，会出现“枭夜行→幽葵宿符”乃至三门神通共用同一柄的
		// 无意义结果。只有尚未登记语义映射的旧道途才保留稳定哈希作为兼容回退。
		string resolvedAuthorityName;
		if (!XjAuthorityLoreCatalog.TryResolveAuthorityForShenTong(owner, shenTongId, out resolvedAuthorityName)
			|| string.IsNullOrWhiteSpace(resolvedAuthorityName)
			|| !catalog.Contains(resolvedAuthorityName))
		{
			int index = XjDeterministicHash.PositiveIndex(
				0L, owner + "|shentong_authority|" + Normalize(shenTongId), catalog.Count);
			resolvedAuthorityName = catalog[index];
		}
		authorityName = resolvedAuthorityName;
		XjDaoLineageArchiveRecord record = GetOrCreate(owner);
		XjDaoAuthorityArchiveData authority = record?.Authorities?.FirstOrDefault(value =>
			value != null && string.Equals(value.Name, resolvedAuthorityName, StringComparison.Ordinal));
		authorityStatus = string.IsNullOrWhiteSpace(authority?.Status) ? "潜" : authority.Status.Trim();
		return ResolveAuthorityStatusWeight(authorityStatus);
	}

	internal static int ResolveAuthorityStatusWeight(string status)
	{
		return (status ?? string.Empty).Trim() switch
		{
			"易" => 15, // 外道权柄已经融入本道，形成稳定新解
			"执" => 12, // 本道果位持有者实际执掌根权柄
			"归" => 11, // 争夺失败后归还原道，法理正在复位
			"显" => 10, // 已显世、可被本道位序承接
			"潜" => 7,  // 尚未随位序显化
			"借" => 7,  // 夺柄者暂借，尚未完成融合
			"藏" => 5,  // 持柄者离位后藏回道统，并非失落
			"裂" => 3,  // 正被外道争夺，原道解释暂时裂开
			"失" => 1,  // 已被外道正式融合，原道不可再用
			_ => 8
		};
	}

	internal static string BuildShenTongTrace(string sourceDaoTu, string shenTongId)
	{
		string owner = Normalize(sourceDaoTu);
		if (XjXianJiCatalog.TryResolveOwningDaoTu(shenTongId, out string resolvedOwner)
			&& !string.IsNullOrWhiteSpace(resolvedOwner)) owner = resolvedOwner.Trim();
		if (owner.Length == 0) return string.Empty;
		ResolveShenTongCandidateWeight(owner, shenTongId, out string authority, out string status);
		if (string.IsNullOrWhiteSpace(authority)) return owner;
		return owner + "·" + authority + "·" + (string.IsNullOrWhiteSpace(status) ? "潜" : status);
	}

	/// <summary>
	/// 每条道统每年最多同步一次。道势以现有果位世界的人口/高境聚合值为底，
	/// 再叠加主位存续与中兴保护；不扫描普通人口，也不另建常驻任务。
	/// </summary>
	internal static bool HasPendingWorldTickForYear(int currentYear)
	{
		return currentYear > 0
			&& pendingWorldTickYear == currentYear
			&& pendingWorldTickIndex < WorldTickDaoTuBuffer.Count;
	}

	internal static bool BeginWorldTick(int currentYear)
	{
		if (currentYear <= 0) return false;
		if (HasPendingWorldTickForYear(currentYear)) return true;
		if (lastWorldTickYear >= currentYear) return false;

		ClearPendingWorldTick();
		// 0.9.7.3 曾短暂把“易”误做成随夺柄者死亡而失坠天地。年度世界维护
		// 先修复这种 TargetDaoTu 为空的错误存档，再进行各道途年度结算。
		// 修复入口本身只遍历稀疏失柄记录；真正可随道途数量增长的年度结算
		// 改为 cursor，以免一个世界年把全部道途连续压在同一渲染帧。
		if (XjGuoWeiQuanBingRegistry.HasUntargetedLostAuthorityRecords())
		{
			RepairWrongPersonalAuthorityLossRecords(currentYear);
		}
		foreach (string daoTu in ByDaoTu.Keys) WorldTickDaoTuBuffer.Add(daoTu);
		pendingWorldTickYear = currentYear;
		pendingWorldTickIndex = 0;
		if (WorldTickDaoTuBuffer.Count > 0) return true;
		lastWorldTickYear = currentYear;
		ClearPendingWorldTick();
		return false;
	}

	internal static bool TickPendingWorldTick(int itemBudget = 4, double timeBudgetMs = 0.28d)
	{
		if (pendingWorldTickYear <= 0 || pendingWorldTickIndex >= WorldTickDaoTuBuffer.Count)
		{
			ClearPendingWorldTick();
			return true;
		}

		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (pendingWorldTickIndex < WorldTickDaoTuBuffer.Count && budget.TryTake())
		{
			string daoTu = WorldTickDaoTuBuffer[pendingWorldTickIndex++];
			long sample = XjRuntimeDiagnostics.BeginNamedSample();
			try
			{
				TickAnnual(daoTu, pendingWorldTickYear);
			}
			finally
			{
				XjRuntimeDiagnostics.EndNamedSample("annual.DaoLineage.lineage", sample);
			}
		}

		if (pendingWorldTickIndex < WorldTickDaoTuBuffer.Count) return false;
		lastWorldTickYear = Math.Max(lastWorldTickYear, pendingWorldTickYear);
		ClearPendingWorldTick();
		return true;
	}

	internal static void TickKnownLineages(int currentYear)
	{
		if (!BeginWorldTick(currentYear)) return;
		while (HasPendingWorldTickForYear(currentYear))
		{
			TickAnnual(WorldTickDaoTuBuffer[pendingWorldTickIndex++], currentYear);
		}
		lastWorldTickYear = Math.Max(lastWorldTickYear, currentYear);
		ClearPendingWorldTick();
	}

	internal static void ClearPendingWorldTick()
	{
		WorldTickDaoTuBuffer.Clear();
		pendingWorldTickYear = 0;
		pendingWorldTickIndex = 0;
	}

	internal static void TickAnnual(string daoTu, int currentYear)
	{
		if (currentYear <= 0) return;
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record == null || record.LastSyncedYear >= currentYear) return;
		record.LastSyncedYear = currentYear;

		int oldVitality = record.Vitality;
		// 道势只影响活力和道统阶段，不再随机制造权柄失落；年度这里只做旧档状态修复。
		bool authorityChanged = RepairLegacyAuthorityStates(record, currentYear);
		if (XjGuoWeiQuanBingRegistry.CountAvailableAuthorities(record.DaoTu) <= 0)
		{
			// 六柄根权都已经融入其他道途果位时，原道自身已经没有完整果位根基。
			record.Vitality = 0;
			record.Phase = "毁尽";
			if (oldVitality != 0 || authorityChanged)
			{
				record.LastChangedYear = Math.Max(record.LastChangedYear, currentYear);
				Touch();
			}
			return;
		}

		int target = XjFruitPositionWorldState.GetMomentum(record.DaoTu);
		bool hasFruit = XjGuoWeiRegistry.TryFindActiveAnchor(
			record.DaoTu, XjGuoWeiCalculator.ZhengWei, out _);
		target += hasFruit ? 4 : -6;
		if (record.RevivalYear > 0 && currentYear - record.RevivalYear <= 120)
			target = Math.Max(target, 70);
		target = Math.Max(0, Math.Min(100, target));

		int next = oldVitality;
		if (target > oldVitality) next += Math.Min(3, target - oldVitality);
		else if (target < oldVitality) next -= Math.Min(2, oldVitality - target);
		record.Vitality = Math.Max(0, Math.Min(100, next));
		record.Phase = ResolvePhase(record.Vitality, record.RevivalYear > 0);
		if (record.Vitality != oldVitality || authorityChanged)
		{
			record.LastChangedYear = Math.Max(record.LastChangedYear, currentYear);
			Touch();
		}
	}

	internal static IReadOnlyList<string> GetUsableAuthorityNames(string daoTu, bool allowDormantFallback = false)
	{
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record?.Authorities == null || record.Authorities.Count == 0)
		{
			return XjGuoWeiAuthorityCatalog.Get(daoTu)
				.Where(value => !XjGuoWeiQuanBingRegistry.IsAuthorityLost(daoTu, value))
				.ToArray();
		}
		List<XjDaoAuthorityArchiveData> ordered = record.Authorities
			.Where(value => value != null && !string.IsNullOrWhiteSpace(value.Name)
				&& IsNativeRootAuthority(record, value)
				&& !string.Equals(value.Status, "失", StringComparison.Ordinal)
				&& !string.Equals(value.Status, "裂", StringComparison.Ordinal)
				&& !string.Equals(value.Status, "借", StringComparison.Ordinal)
				&& (allowDormantFallback || !string.Equals(value.Status, "藏", StringComparison.Ordinal)))
			.OrderBy(value => AuthorityPriority(value.Status))
			.ThenBy(value => value.Name, StringComparer.Ordinal)
			.ToList();
		if (ordered.Count == 0) return Array.Empty<string>();
		return ordered.Select(value => value.Name).ToArray();
	}

	internal static string ResolveInitialAuthorityScope(
		string sourceDaoTu,
		string manifestDaoTu,
		string positionType,
		long actorId,
		int year)
	{
		string source = Normalize(sourceDaoTu);
		string manifest = Normalize(manifestDaoTu);
		IReadOnlyList<string> target = GetUsableAuthorityNames(manifest, allowDormantFallback: true);
		IReadOnlyList<string> root = GetUsableAuthorityNames(source, allowDormantFallback: true);
		List<string> selected = new List<string>();
		int targetCount = string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? 3
			: string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 3 : 1;
		if (target.Count > 0)
		{
			int start = XjDeterministicHash.PositiveIndex(actorId + year, manifest + "|scope", target.Count);
			for (int offset = 0; offset < target.Count && selected.Count < targetCount; offset++)
			{
				string name = target[(start + offset) % target.Count];
				if (!selected.Contains(name)) selected.Add(name);
			}
		}
		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& root.Count > 0)
		{
			string borrowed = root[XjDeterministicHash.PositiveIndex(actorId + 97L + year, source + "|borrowed", root.Count)];
			if (!selected.Contains(borrowed)) selected.Add(borrowed);
			string crossed = BuildCrossAuthority(source, manifest);
			if (!selected.Contains(crossed)) selected.Add(crossed);
		}
		return string.Join("|", selected);
	}

	/// <summary>
	/// 外道夺柄的第一阶段。此时原道只进入“裂”，夺柄者一方只进入“借”；
	/// 只有十年合道成功后才会转成“失/易”。这样权柄失落必然对应一次
	/// 真实的高境击杀与融合，不再由道势或随机年度演化制造。
	/// </summary>
	internal static bool OnAuthoritySeized(
		long actorId,
		string actorName,
		string sourceDaoTu,
		string targetDaoTu,
		string authorityName,
		int year,
		bool decisiveLoss,
		long expectedSourceHolderActorId = 0L)
	{
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		string authority = Normalize(authorityName);
		if (actorId <= 0L || source.Length == 0 || target.Length == 0 || authority.Length == 0
			|| string.Equals(source, target, StringComparison.Ordinal)
			|| !XjGuoWeiAuthorityCatalog.Get(source).Contains(authority)) return false;

		if (decisiveLoss)
		{
			// 天道干涉是唯一允许跳过“裂/借—十年融合”的显式例外。
			// 永久失柄账本也由同一个结算入口在完成全部校验后写入，
			// 避免“账本先失、状态机后失败”的半提交。
			return OnAuthorityIntegrationResolved(actorId, actorName, source, target, authority, year, success: true, directIntervention: true);
		}
		if (XjGuoWeiQuanBingRegistry.IsAuthorityLost(source, authority)) return false;

		XjDaoLineageArchiveRecord sourceRecord = GetOrCreate(source);
		XjDaoLineageArchiveRecord targetRecord = GetOrCreate(target);
		if (sourceRecord == null || targetRecord == null) return false;
		XjDaoAuthorityArchiveData sourceAuthority = FindNativeAuthority(sourceRecord, authority);
		if (sourceAuthority == null
			|| !string.Equals(sourceAuthority.Status, "执", StringComparison.Ordinal)
			|| sourceAuthority.HolderActorId <= 0L
			|| (expectedSourceHolderActorId > 0L && sourceAuthority.HolderActorId != expectedSourceHolderActorId)) return false;
		XjDaoAuthorityArchiveData borrowed = FindExternalAuthority(targetRecord, authority, source);
		if (borrowed != null)
		{
			if (string.Equals(borrowed.Status, "易", StringComparison.Ordinal)) return false;
			if (borrowed.HolderActorId > 0L && borrowed.HolderActorId != actorId) return false;
		}
		else
		{
			borrowed = new XjDaoAuthorityArchiveData { Name = authority, SourceDaoTu = source };
			targetRecord.Authorities.Add(borrowed);
		}

		bool changed = SetAuthority(sourceRecord, sourceAuthority, "裂", 0L,
			string.IsNullOrWhiteSpace(actorName) ? "待外道融合" : "待" + actorName.Trim() + "融合",
			source, year);
		changed |= SetAuthority(targetRecord, borrowed, "借", actorId, actorName, source, year);
		if (!changed) return false;

		sourceRecord.Vitality = Math.Max(0, sourceRecord.Vitality - 2);
		sourceRecord.Phase = ResolvePhase(sourceRecord.Vitality, sourceRecord.RevivalYear > 0);
		targetRecord.Vitality = Math.Min(100, targetRecord.Vitality + 1);
		targetRecord.Phase = ResolvePhase(targetRecord.Vitality, targetRecord.RevivalYear > 0);
		Touch();

		string authorityDisplay = XjDaoIntentionCatalog.FormatAuthority(source, authority);
		string text = "【权柄裂解】" + (string.IsNullOrWhiteSpace(actorName) ? "一位真君" : actorName.Trim())
			+ "斩落持柄者，暂夺" + source + authorityDisplay + "；原道此柄转为‘裂’，夺柄者只得‘借’，须闭关十年合道。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World, "权柄裂解", text, importance: 4,
			actorId: actorId, actorName: actorName, year: year,
			iconIdOverride: XjEventIconCatalog.HistoryWorld,
			eventType: "AuthorityFractured", result: XjHistoryResult.Transfer,
			mirrorToWorldLog: false);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			text, XjAnnouncementCategory.AuthorityPosition, duration: 6.5f,
			color: "#C69B5A", delayFrames: 1, iconId: XjEventIconCatalog.HistoryWorld);
		return true;
	}

	/// <summary>
	/// 世界事件中的外部高位存在夺取权柄。外部存在没有可写入的 Actor，不能伪装成
	/// 一次角色击杀；但仍必须在同一事务中同步原道“失”、目标道“易”及失柄账本，
	/// 以免留下只有账本而没有道统投影的半状态。
	/// </summary>
	internal static bool TryClaimAuthorityForExternalWorldPosition(
		string claimantName,
		string sourceDaoTu,
		string targetDaoTu,
		string authorityName,
		int year,
		string reason)
	{
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		string authority = Normalize(authorityName);
		if (source.Length == 0 || target.Length == 0 || authority.Length == 0
			|| string.Equals(source, target, StringComparison.Ordinal)
			|| XjGuoWeiQuanBingRegistry.IsAuthorityLost(source, authority)
			|| !XjGuoWeiAuthorityCatalog.Get(source).Contains(authority))
		{
			return false;
		}

		XjDaoLineageArchiveRecord sourceRecord = GetOrCreate(source);
		XjDaoLineageArchiveRecord targetRecord = GetOrCreate(target);
		if (sourceRecord == null || targetRecord == null) return false;
		XjDaoAuthorityArchiveData sourceAuthority = FindNativeAuthority(sourceRecord, authority);
		XjDaoAuthorityArchiveData borrowed = FindExternalAuthority(targetRecord, authority, source);
		if (sourceAuthority == null
			|| !string.Equals(sourceAuthority.Status, "执", StringComparison.Ordinal)
			|| borrowed != null)
		{
			return false;
		}

		string holderName = string.IsNullOrWhiteSpace(claimantName) ? "外部高位存在" : claimantName.Trim();
		int safeYear = Math.Max(1, year);
		XjGuoWeiQuanBingRegistry.RecordLostAuthority(
			source,
			authority,
			target,
			safeYear,
			string.IsNullOrWhiteSpace(reason) ? "外部世界果位夺柄" : reason.Trim());
		if (!XjGuoWeiQuanBingRegistry.IsAuthorityLost(source, authority)) return false;

		bool changed = SetAuthority(sourceRecord, sourceAuthority, "失", 0L, holderName, source, safeYear);
		borrowed = new XjDaoAuthorityArchiveData { Name = authority, SourceDaoTu = source };
		targetRecord.Authorities.Add(borrowed);
		changed |= SetAuthority(targetRecord, borrowed, "易", 0L, holderName, source, safeYear);
		if (!changed) return false;

		sourceRecord.Vitality = Math.Max(0, sourceRecord.Vitality - 2);
		sourceRecord.Phase = ResolvePhase(sourceRecord.Vitality, sourceRecord.RevivalYear > 0);
		targetRecord.Vitality = Math.Min(100, targetRecord.Vitality + 2);
		targetRecord.Phase = ResolvePhase(targetRecord.Vitality, targetRecord.RevivalYear > 0);
		Touch();
		return true;
	}

	/// <summary>
	/// 外道权柄融合结算。成功才形成原道“失”和新道“易”；失败或夺柄者
	/// 身死则权柄“归”回原道，并删除目标道途中的临时借柄投影。
	/// </summary>
	internal static bool OnAuthorityIntegrationResolved(
		long actorId,
		string actorName,
		string sourceDaoTu,
		string targetDaoTu,
		string authorityName,
		int year,
		bool success,
		bool directIntervention = false)
	{
		string source = Normalize(sourceDaoTu);
		string target = Normalize(targetDaoTu);
		string authority = Normalize(authorityName);
		if (source.Length == 0 || target.Length == 0 || authority.Length == 0
			|| string.Equals(source, target, StringComparison.Ordinal)
			|| !XjGuoWeiAuthorityCatalog.Get(source).Contains(authority)) return false;

		XjDaoLineageArchiveRecord sourceRecord = GetOrCreate(source);
		XjDaoLineageArchiveRecord targetRecord = GetOrCreate(target);
		if (sourceRecord == null || targetRecord == null) return false;
		XjDaoAuthorityArchiveData sourceAuthority = FindNativeAuthority(sourceRecord, authority);
		if (sourceAuthority == null) return false;
		XjDaoAuthorityArchiveData borrowed = FindExternalAuthority(targetRecord, authority, source);

		if (directIntervention)
		{
			if (!success || XjGuoWeiQuanBingRegistry.IsAuthorityLost(source, authority)) return false;
			// 天道干涉可跳过击杀与十年融合，但不能覆盖一柄正在被持有、
			// 正在融合或已经存在外道投影的权柄。
			if (sourceAuthority.HolderActorId > 0L
				|| string.Equals(sourceAuthority.Status, "裂", StringComparison.Ordinal)
				|| string.Equals(sourceAuthority.Status, "失", StringComparison.Ordinal)
				|| borrowed != null) return false;
		}
		else
		{
			bool sourceFractured = string.Equals(sourceAuthority.Status, "裂", StringComparison.Ordinal);
			bool pairCompatible = borrowed == null
				|| (string.Equals(borrowed.Status, "借", StringComparison.Ordinal)
					&& borrowed.HolderActorId == actorId
					&& string.Equals(Normalize(borrowed.SourceDaoTu), source, StringComparison.Ordinal));
			// 正常结算必须从真实“裂”柄进入。成功时永久失柄账本必须已经由
			// 融合结算写入；允许旧档缺失目标“借”投影，但不允许无裂柄凭空成“失/易”。
			if (!sourceFractured || !pairCompatible) return false;
		}

		// 永久失柄只在完整校验通过后，由状态机唯一入口写入。
		// 这样不存在“lost ledger 已写入、裂/借配对却未成功结算”的半状态。
		if (success)
		{
			XjGuoWeiQuanBingRegistry.RecordLostAuthority(
				source, authority, target, year,
				directIntervention ? "天道干涉·权柄赋予" : "外道权柄合道完成");
			if (!XjGuoWeiQuanBingRegistry.IsAuthorityLost(source, authority)) return false;
		}

		bool changed = SetAuthority(sourceRecord, sourceAuthority, success ? "失" : "归", 0L,
			success ? (string.IsNullOrWhiteSpace(actorName) ? "已为外道所融" : "已为" + actorName.Trim() + "所融") : string.Empty,
			source, year);
		if (success)
		{
			if (borrowed == null)
			{
				borrowed = new XjDaoAuthorityArchiveData { Name = authority, SourceDaoTu = source };
				targetRecord.Authorities.Add(borrowed);
			}
			ResolveActiveFruitHolder(target, out long fruitHolderId, out string fruitHolderName);
			changed |= SetAuthority(targetRecord, borrowed, "易", fruitHolderId, fruitHolderName, source, year);
			targetRecord.Vitality = Math.Min(100, targetRecord.Vitality + 2);
		}
		else if (borrowed != null)
		{
			targetRecord.Authorities.Remove(borrowed);
			targetRecord.LastChangedYear = Math.Max(targetRecord.LastChangedYear, year);
			changed = true;
		}
		if (!changed) return false;

		sourceRecord.Vitality = Math.Max(0, Math.Min(100, sourceRecord.Vitality + (success ? -2 : 2)));
		sourceRecord.Phase = ResolvePhase(sourceRecord.Vitality, sourceRecord.RevivalYear > 0);
		targetRecord.Phase = ResolvePhase(targetRecord.Vitality, targetRecord.RevivalYear > 0);
		Touch();
		if (success) EvaluateRootAuthorityExtinction(source, year, authority, actorName);

		string display = XjDaoIntentionCatalog.FormatAuthority(source, authority);
		string title = success ? "权柄融成" : "权柄归还";
		string text = success
			? "【" + title + "】" + (string.IsNullOrWhiteSpace(actorName) ? "一位真君" : actorName.Trim())
				+ "正式融成" + source + display + "；原道此柄正式‘失’，" + target + "道途得其‘易’解。"
			: "【" + title + "】" + (string.IsNullOrWhiteSpace(actorName) ? "夺柄者" : actorName.Trim())
				+ "合道未成，" + source + display + "自‘裂’复‘归’，" + target + "道途临时‘借’柄消散。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World, title, text, importance: success ? 5 : 4,
			isProtected: success, actorId: actorId, actorName: actorName, year: year,
			iconIdOverride: XjEventIconCatalog.HistoryWorld,
			eventType: success ? "AuthorityIntegrated" : "AuthorityReturned",
			result: XjHistoryResult.Transfer, mirrorToWorldLog: false);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			text, XjAnnouncementCategory.AuthorityPosition, duration: 6.5f,
			color: success ? "#D59B45" : "#8DB6C9", delayFrames: 1,
			iconId: XjEventIconCatalog.HistoryWorld);
		return true;
	}

	internal static void OnPromotion(
		long actorId,
		string actorName,
		string sourceDaoTu,
		string manifestDaoTu,
		string positionType,
		string authorityScope,
		int year,
		bool affectVitality = true)
	{
		string source = Normalize(sourceDaoTu);
		string manifest = Normalize(manifestDaoTu);
		XjDaoLineageArchiveRecord manifestRecord = GetOrCreate(manifest);
		if (manifestRecord == null || actorId <= 0L) return;

		bool changed = false;
		if (affectVitality)
		{
			int gain = string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? 8
				: string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 5 : 3;
			int next = Math.Min(100, manifestRecord.Vitality + gain);
			changed = next != manifestRecord.Vitality;
			manifestRecord.Vitality = next;
			manifestRecord.Phase = ResolvePhase(manifestRecord.Vitality, manifestRecord.RevivalYear > 0);
			manifestRecord.LastChangedYear = Math.Max(manifestRecord.LastChangedYear, year);
		}

		HashSet<string> scopes = new HashSet<string>(
			(authorityScope ?? string.Empty).Split(new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(Normalize).Where(value => value.Length > 0),
			StringComparer.Ordinal);
		List<string> newlyClaimed = SynchronizePositionAuthorityClaims(
			actorId, actorName, manifestRecord, positionType, scopes, year, out bool claimChanged);
		changed |= claimChanged;
		if (!changed) return;
		Touch();

		if (affectVitality && newlyClaimed.Count > 0)
		{
			string holder = string.IsNullOrWhiteSpace(actorName) ? "一位持位者" : actorName.Trim();
			string scopeText = string.Join("、", newlyClaimed.OrderBy(value => value, StringComparer.Ordinal).Take(3));
			string text = "【权柄归身】" + holder + "晋位，" + manifest + "道途" + scopeText + "归于其身。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.World, "权柄归身", text, importance: 4,
				actorId: actorId, actorName: actorName, year: year,
				iconIdOverride: XjEventIconCatalog.HistoryWorld,
				eventType: "AuthorityAssigned", result: XjHistoryResult.Transfer,
				mirrorToWorldLog: false);
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
				text, XjAnnouncementCategory.AuthorityPosition, duration: 5.5f,
				color: "#C69B5A", iconId: XjEventIconCatalog.HistoryWorld);
		}
	}

	private static List<string> SynchronizePositionAuthorityClaims(
		long actorId,
		string actorName,
		XjDaoLineageArchiveRecord manifestRecord,
		string positionType,
		HashSet<string> scopes,
		int currentYear,
		out bool changed)
	{
		changed = false;
		List<string> newlyClaimed = new List<string>();
		if (!string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			// 余位、闰位使用位置档案中的派生权柄。它们不改变六柄根权柄的
			// 潜/显/执状态，也绝不能借普通晋位入口制造“借”或“易”。
			return newlyClaimed;
		}

		HashSet<string> desiredNative = new HashSet<string>(StringComparer.Ordinal);
		IReadOnlyList<string> nativeCatalog = XjGuoWeiAuthorityCatalog.Get(manifestRecord.DaoTu);
		for (int i = 0; i < nativeCatalog.Count; i++)
		{
			if (scopes.Contains(nativeCatalog[i])) desiredNative.Add(nativeCatalog[i]);
		}

		// 只释放该角色此前通过正位实际执掌、而本次已经不在真实持柄快照中的根权柄。
		foreach (XjDaoLineageArchiveRecord record in ByDaoTu.Values)
		{
			if (record?.Authorities == null) continue;
			for (int i = 0; i < record.Authorities.Count; i++)
			{
				XjDaoAuthorityArchiveData authority = record.Authorities[i];
				if (authority == null || authority.HolderActorId != actorId
					|| !string.Equals(authority.Status, "执", StringComparison.Ordinal)
					|| !IsNativeRootAuthority(record, authority)) continue;
				bool keep = string.Equals(record.DaoTu, manifestRecord.DaoTu, StringComparison.Ordinal)
					&& desiredNative.Contains(authority.Name);
				if (keep) continue;
				changed |= SetAuthority(record, authority, "藏", 0L, string.Empty, record.DaoTu, currentYear);
			}
		}

		foreach (string authorityName in desiredNative)
		{
			XjDaoAuthorityArchiveData authority = FindNativeAuthority(manifestRecord, authorityName);
			if (authority == null
				|| string.Equals(authority.Status, "失", StringComparison.Ordinal)
				|| string.Equals(authority.Status, "裂", StringComparison.Ordinal)
				|| string.Equals(authority.Status, "借", StringComparison.Ordinal)
				|| (authority.HolderActorId > 0L && authority.HolderActorId != actorId)) continue;
			bool wasHeld = authority.HolderActorId == actorId && string.Equals(authority.Status, "执", StringComparison.Ordinal);
			if (SetAuthority(manifestRecord, authority, "执", actorId, actorName, manifestRecord.DaoTu, currentYear))
			{
				changed = true;
				if (!wasHeld) newlyClaimed.Add(authority.Name);
			}
		}

		// 已经“易”入本道的外道根权柄属于本道果位位格。只要新的正位果主承位，
		// 就承接该道途此前已经融成的全部外道权柄；它不依赖本轮本地权辖 scope。
		for (int i = 0; i < manifestRecord.Authorities.Count; i++)
		{
			XjDaoAuthorityArchiveData authority = manifestRecord.Authorities[i];
			if (authority == null
				|| !IsExternalRootProjection(manifestRecord, authority)
				|| !string.Equals(authority.Status, "易", StringComparison.Ordinal)
				|| (authority.HolderActorId > 0L && authority.HolderActorId != actorId)) continue;
			bool wasHeld = authority.HolderActorId == actorId;
			if (SetAuthority(manifestRecord, authority, "易", actorId, actorName, authority.SourceDaoTu, currentYear))
			{
				changed = true;
				if (!wasHeld) newlyClaimed.Add(authority.Name);
			}
		}
		return newlyClaimed;
	}

	/// <summary>
	/// 本道余位/闰位斩落自己所归属道途的果位并成功承继时，根权柄从死者“执”精确转交给新果位。
	/// 此入口只允许同一根道、已由死者实际执掌且已进入新果位真实持柄快照的权柄，
	/// 不产生裂/借/失/易，也不能被普通晋位同步代替。
	/// </summary>
	internal static bool OnNativeAuthoritySucceeded(
		long previousHolderActorId,
		long newHolderActorId,
		string newHolderName,
		string daoTu,
		string authorityNames,
		int currentYear)
	{
		string normalizedDaoTu = Normalize(daoTu);
		if (previousHolderActorId <= 0L || newHolderActorId <= 0L
			|| previousHolderActorId == newHolderActorId || normalizedDaoTu.Length == 0) return false;
		XjDaoLineageArchiveRecord record = GetOrCreate(normalizedDaoTu);
		if (record?.Authorities == null) return false;
		HashSet<string> desired = new HashSet<string>(
			(authorityNames ?? string.Empty).Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(Normalize).Where(value => value.Length > 0), StringComparer.Ordinal);
		if (desired.Count == 0) return false;
		bool changed = false;
		for (int i = 0; i < record.Authorities.Count; i++)
		{
			XjDaoAuthorityArchiveData authority = record.Authorities[i];
			if (authority == null || !desired.Contains(authority.Name)
				|| !IsNativeRootAuthority(record, authority)
				|| !string.Equals(authority.Status, "执", StringComparison.Ordinal)
				|| authority.HolderActorId != previousHolderActorId) continue;
			changed |= SetAuthority(record, authority, "执", newHolderActorId, newHolderName, normalizedDaoTu, currentYear);
		}
		if (changed) Touch();
		return changed;
	}

	internal static void OnHolderReleased(
		long actorId,
		string daoTu,
		string guoWei,
		string authorityNames,
		int currentYear,
		bool penalizeVitality)
	{
		if (actorId <= 0L) return;
		HashSet<string> released = new HashSet<string>(
			(authorityNames ?? string.Empty).Split(new[] { ',', '，', '|' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(value => Normalize(value)), StringComparer.Ordinal);
		List<(string Source, string Target, string Authority, string HolderName)> borrowedReturns =
			new List<(string, string, string, string)>();
		bool changed = false;
		foreach (XjDaoLineageArchiveRecord record in ByDaoTu.Values)
		{
			if (record?.Authorities == null) continue;
			for (int i = 0; i < record.Authorities.Count; i++)
			{
				XjDaoAuthorityArchiveData authority = record.Authorities[i];
				if (authority == null || authority.HolderActorId != actorId) continue;
				if (released.Count > 0 && !released.Contains(authority.Name)) continue;

				string source = Normalize(authority.SourceDaoTu);
				if (string.Equals(authority.Status, "借", StringComparison.Ordinal)
					&& source.Length > 0
					&& !string.Equals(source, record.DaoTu, StringComparison.Ordinal))
				{
					borrowedReturns.Add((source, record.DaoTu, authority.Name, authority.HolderName));
					continue;
				}

				if (string.Equals(authority.Status, "易", StringComparison.Ordinal))
				{
					// 已融成的新解释留在目标道统，只失去现任持柄者。
					changed |= SetAuthority(record, authority, "易", 0L, string.Empty, authority.SourceDaoTu, currentYear);
				}
				else
				{
					// 正常离位只处理本道“执”柄；外道“借”柄已在上方进入精确归还队列。
					changed |= SetAuthority(record, authority, "藏", 0L, string.Empty, record.DaoTu, currentYear);
				}
			}
		}

		for (int i = 0; i < borrowedReturns.Count; i++)
		{
			var item = borrowedReturns[i];
			OnAuthorityIntegrationResolved(actorId, item.HolderName, item.Source, item.Target, item.Authority, currentYear, success: false);
			changed = true;
		}

		if (penalizeVitality)
		{
			XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
			if (record != null)
			{
				string type = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
				int loss = string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? 10
					: string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 5 : 3;
				record.Vitality = Math.Max(0, record.Vitality - loss);
				record.Phase = ResolvePhase(record.Vitality, record.RevivalYear > 0);
				record.LastChangedYear = Math.Max(record.LastChangedYear, currentYear);
				changed = true;
			}
		}
		if (changed)
		{
			Touch();
			string holderName = "一位持位者";
			if (XjGuoWeiQuanBingRegistry.TryGetHistorical(actorId, out XjGuoWeiQuanBingState historical)
				&& !string.IsNullOrWhiteSpace(historical.ActorName)) holderName = historical.ActorName;
			string scope = released.Count > 0 ? string.Join("、", released.Take(3)) : "所执诸柄";
			string text = "【权柄离身】" + holderName + "离位，" + daoTu + "道途" + scope + "藏回本途；若有外道借柄，则同时归还原道；已融入本果位的外道权柄留在道途果位中等待后继者承接。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.World,
				"权柄离身",
				text,
				importance: 4,
				actorId: actorId,
				actorName: holderName,
				year: currentYear,
				iconIdOverride: XjEventIconCatalog.HistoryWorld,
				eventType: "AuthorityReleased",
				result: XjHistoryResult.Transfer,
				mirrorToWorldLog: false);
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
				text,
				XjAnnouncementCategory.AuthorityPosition,
				duration: 5.5f,
				color: "#B58B52",
				iconId: XjEventIconCatalog.HistoryWorld);
		}
	}

	private static bool AreAllRootAuthoritiesLost(string daoTu)
	{
		string normalized = Normalize(daoTu);
		IReadOnlyList<string> roots = XjGuoWeiAuthorityCatalog.Get(normalized);
		if (normalized.Length == 0 || roots == null || roots.Count == 0) return false;
		for (int i = 0; i < roots.Count; i++)
		{
			if (!XjGuoWeiQuanBingRegistry.TryGetLostAuthorityRecord(normalized, roots[i], out XjGuoWeiQuanBingLostAuthorityArchiveData lost)
				|| lost == null || string.IsNullOrWhiteSpace(lost.TargetDaoTu)) return false;
		}
		return true;
	}

	private static void EvaluateRootAuthorityExtinction(string daoTu, int currentYear, string triggeringAuthority, string actorName)
	{
		string normalized = Normalize(daoTu);
		if (normalized.Length == 0 || !AreAllRootAuthoritiesLost(normalized)) return;
		XjDaoLineageArchiveRecord record = GetOrCreate(normalized);
		if (record == null) return;
		bool first = !string.Equals(record.Phase, "毁尽", StringComparison.Ordinal);
		record.Vitality = 0;
		record.Phase = "毁尽";
		record.LastChangedYear = Math.Max(record.LastChangedYear, currentYear);
		Touch();
		if (!first) return;

		string text = "【道途毁尽】" + normalized
			+ "六柄根权尽数易入他途果位，自身已无完整果位根基；后世若要重开此道，必须重新夺回或另证其权。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World, "道途毁尽", text, importance: 6, isProtected: true,
			year: currentYear, iconIdOverride: XjEventIconCatalog.HistoryWorld,
			eventType: "DaoLineageExtinguished", result: XjHistoryResult.Failure, mirrorToWorldLog: false);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			text, XjAnnouncementCategory.AuthorityPosition, duration: 9f, color: "#A33F3F",
			delayFrames: 1, iconId: XjEventIconCatalog.HistoryWorld);
	}

	private static void RepairWrongPersonalAuthorityLossRecords(int currentYear)
	{
		IReadOnlyList<XjGuoWeiQuanBingLostAuthorityArchiveData> lost = XjGuoWeiQuanBingRegistry.ReadLostAuthorityRecords();
		for (int i = 0; i < lost.Count; i++)
		{
			XjGuoWeiQuanBingLostAuthorityArchiveData item = lost[i];
			if (item == null || !string.IsNullOrWhiteSpace(item.TargetDaoTu)) continue;
			string source = Normalize(item.SourceDaoTu);
			string authority = Normalize(item.Authority);
			if (source.Length == 0 || authority.Length == 0) continue;

			if (TryRecoverLostAuthorityTargetFromHistory(source, authority, out string target))
			{
				XjGuoWeiQuanBingRegistry.RecordLostAuthority(source, authority, target, currentYear, "旧档修复：恢复为目标道途果位融权");
				XjDaoLineageArchiveRecord targetRecord = GetOrCreate(target);
				XjDaoAuthorityArchiveData projection = FindExternalAuthority(targetRecord, authority, source);
				if (projection == null)
				{
					projection = new XjDaoAuthorityArchiveData { Name = authority, SourceDaoTu = source };
					targetRecord.Authorities.Add(projection);
				}
				ResolveActiveFruitHolder(target, out long holderId, out string holderName);
				SetAuthority(targetRecord, projection, "易", holderId, holderName, source, currentYear);
				Touch();
			}
			else
			{
				// “失坠天地”是上一版错误规则制造的无目标记录；找不到任何真实融入道途时，
				// 直接撤销错误失柄，让该根权回到原道，而不是继续保留幽灵损失。
				XjGuoWeiQuanBingRegistry.ClearLostAuthority(source, authority);
				XjDaoLineageArchiveRecord sourceRecord = GetOrCreate(source);
				XjDaoAuthorityArchiveData native = FindNativeAuthority(sourceRecord, authority);
				if (native != null) SetAuthority(sourceRecord, native, "归", 0L, string.Empty, source, currentYear);
				Touch();
			}
		}
	}

	private static bool TryRecoverLostAuthorityTargetFromHistory(string sourceDaoTu, string authorityName, out string targetDaoTu)
	{
		targetDaoTu = string.Empty;
		int bestYear = -1;
		IReadOnlyList<XjGuoWeiQuanBingState> states = XjGuoWeiQuanBingRegistry.ReadAllEntries();
		for (int i = 0; i < states.Count; i++)
		{
			XjGuoWeiQuanBingState state = states[i];
			if (!state.Found || string.IsNullOrWhiteSpace(state.DaoTu)
				|| !ContainsAuthority(state.SeizedQuanBing, authorityName)
				|| !HasSeizedAuthoritySourceEvidence(state.SeizedQuanBingSources, authorityName, sourceDaoTu)) continue;
			int year = Math.Max(state.AcquiredYear, state.ReleasedYear);
			if (year < bestYear) continue;
			bestYear = year;
			targetDaoTu = Normalize(state.DaoTu);
		}
		return targetDaoTu.Length > 0;
	}

	private static bool HasSeizedAuthoritySourceEvidence(string rawSources, string authorityName, string sourceDaoTu)
	{
		if (string.IsNullOrWhiteSpace(rawSources)) return true;
		string expectedAuthority = Normalize(authorityName);
		string expectedSource = Normalize(sourceDaoTu);
		string[] entries = rawSources.Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < entries.Length; i++)
		{
			string entry = entries[i]?.Trim();
			if (string.IsNullOrWhiteSpace(entry)) continue;
			int colon = entry.IndexOf(':');
			if (colon <= 0) continue;
			string name = Normalize(entry.Substring(0, colon));
			if (!string.Equals(name, expectedAuthority, StringComparison.Ordinal)) continue;
			string source = entry.Substring(colon + 1).Trim();
			int slash = source.IndexOf('/');
			if (slash >= 0) source = source.Substring(0, slash);
			if (string.Equals(Normalize(source), expectedSource, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static void RecordFoundation(XjDaoProofFoundationArchiveData foundation)
	{
		if (foundation == null || foundation.CityId <= 0L) return;
		FoundationsByKey[BuildFoundationKey(foundation)] = Clone(foundation);
		Touch();
	}

	internal static bool TryGetFoundation(long cityId, out XjDaoProofFoundationArchiveData foundation)
	{
		foundation = null;
		if (cityId <= 0L) return false;
		foreach (XjDaoProofFoundationArchiveData candidate in FoundationsByKey.Values)
		{
			if (candidate == null || candidate.CityId != cityId) continue;
			if (foundation == null || candidate.FoundedYear > foundation.FoundedYear) foundation = candidate;
		}
		return foundation != null;
	}

	/// <summary>
	/// 读取某角色最初真实写入的闰位证道根基。该记录在后续尊修、权柄变化和旧档校正时不会覆盖，
	/// 因而可用于修复旧版本把既成闰位错误重定向到其他果位的存档。
	/// </summary>
	internal static bool TryGetInitialIntercalaryFoundation(long actorId, out XjDaoProofFoundationArchiveData foundation)
	{
		foundation = null;
		if (actorId <= 0L) return false;
		foreach (XjDaoProofFoundationArchiveData candidate in FoundationsByKey.Values)
		{
			if (candidate == null
				|| candidate.FounderActorId != actorId
				|| !string.Equals(
					XjGuoWeiCalculator.NormalizePositionType(candidate.PositionType),
					XjGuoWeiCalculator.RunWei,
					StringComparison.Ordinal)) continue;
			if (foundation == null
				|| candidate.FoundedYear < foundation.FoundedYear
				|| (candidate.FoundedYear == foundation.FoundedYear && candidate.CityId < foundation.CityId))
			{
				foundation = candidate;
			}
		}
		if (foundation == null) return false;
		foundation = Clone(foundation);
		return true;
	}

	internal static float ResolveFoundationMultiplier(long cityId, string daoTu)
	{
		string normalized = Normalize(daoTu);
		if (cityId <= 0L || normalized.Length == 0) return 1f;
		float multiplier = 1f;
		foreach (XjDaoProofFoundationArchiveData foundation in FoundationsByKey.Values)
		{
			if (foundation == null || foundation.CityId != cityId) continue;
			bool sourceMatch = string.Equals(normalized, Normalize(foundation.SourceDaoTu), StringComparison.Ordinal);
			bool manifestMatch = string.Equals(normalized, Normalize(foundation.ManifestDaoTu), StringComparison.Ordinal);
			if (sourceMatch && manifestMatch) multiplier = Math.Max(multiplier, 1.08f);
			else if (sourceMatch || manifestMatch) multiplier = Math.Max(multiplier, 1.05f);
		}
		return multiplier;
	}

	internal static string BuildLineageSummary(string daoTu)
	{
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record == null) return string.Empty;
		return record.DaoTu + "道统·" + record.Phase + "（道势" + record.Vitality
			+ " · 上限100，核心第" + record.CoreRevision + "代：" + record.CoreDoctrine + "）";
	}

	internal static string BuildAuthorityStateSummary(string daoTu)
	{
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record?.Authorities == null || record.Authorities.Count == 0) return string.Empty;
		List<string> parts = new List<string>();
		for (int i = 0; i < record.Authorities.Count; i++)
		{
			XjDaoAuthorityArchiveData authority = record.Authorities[i];
			if (authority == null || string.IsNullOrWhiteSpace(authority.Name)) continue;
			parts.Add(authority.Name.Trim() + "·" + (string.IsNullOrWhiteSpace(authority.Status) ? "潜" : authority.Status.Trim()));
			if (parts.Count >= 8) break;
		}
		return string.Join("、", parts);
	}

	internal static bool TryAdvanceRevival(
		Actor actor,
		string daoTu,
		int daoHui,
		int currentYear,
		out string eventText)
	{
		eventText = string.Empty;
		if (actor?.data == null || daoHui < 82 || currentYear <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int positionImage);
		if (positionImage < 1600) return false;
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record == null || (record.Vitality > 35 && record.RevivalActorId <= 0L)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (record.RevivalActorId > 0L && record.RevivalActorId != actorId) return false;
		if (record.RevivalActorId <= 0L)
		{
			int cadenceSlot = XjDeterministicHash.PositiveIndex(actorId, daoTu + "|lineage_reviver_cadence", 5);
			if (currentYear % 5 != cadenceSlot
				|| XjDeterministicHash.PositiveIndex(actorId + currentYear, daoTu + "|lineage_reviver", 100) >= 4) return false;
			record.RevivalActorId = actorId;
			record.RevivalActorName = actor.getName() ?? string.Empty;
			record.RevivalDirection = ResolveRevivalDirection(actorId, currentYear);
			XuanJianVNext.Systems.ActorSystem.XjActorAccessor.SetString(actor, XuanJianVNext.Data.Rules.XjActorDataKeys.XjLineageRevivalStage, "察旧");
			XuanJianVNext.Systems.ActorSystem.XjActorAccessor.SetString(actor, XuanJianVNext.Data.Rules.XjActorDataKeys.XjLineageRevivalDirection, record.RevivalDirection);
			XuanJianVNext.Systems.ActorSystem.XjActorAccessor.SetInt(actor, XuanJianVNext.Data.Rules.XjActorDataKeys.XjLineageRevivalProgress, 0);
			eventText = "【道统察旧】" + record.RevivalActorName + "见" + daoTu + "道统倾颓，开始考索旧法、失权与断脉。";
			Touch();
			return true;
		}
		return false;
	}

	internal static void CompleteRevival(Actor actor, string daoTu, string direction, int currentYear)
	{
		if (actor?.data == null) return;
		XjDaoLineageArchiveRecord record = GetOrCreate(daoTu);
		if (record == null) return;
		record.CoreRevision = Math.Max(1, record.CoreRevision + 1);
		record.Vitality = Math.Min(100, Math.Max(70, record.Vitality + 35));
		record.Phase = "中兴";
		record.ShenTongBias = string.IsNullOrWhiteSpace(direction) ? "更新" : direction.Trim();
		record.CoreDoctrine = BuildRevisedDoctrine(daoTu, record.ShenTongBias, record.CoreRevision);
		record.RevivalYear = currentYear;
		record.LastChangedYear = currentYear;

		// 中兴只恢复道统活力；实际执柄必须继续经过当前角色的真实位序快照。
		// 这样余位、闰位不会因“中兴”旁路直接拿到果位根权柄。
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L && XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state) && state.Found)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string sourceDaoTu);
			OnPromotion(actorId, actor.getName(), sourceDaoTu, state.DaoTu,
				XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei),
				(state.LocalQuanBing ?? string.Empty).Replace(',', '|'), currentYear, affectVitality: false);
		}
		Touch();
	}

	private static bool RepairLegacyAuthorityStates(XjDaoLineageArchiveRecord record, int currentYear)
	{
		if (record?.Authorities == null || record.Authorities.Count == 0 || currentYear <= 0) return false;
		bool changed = false;
		for (int i = record.Authorities.Count - 1; i >= 0; i--)
		{
			XjDaoAuthorityArchiveData authority = record.Authorities[i];
			if (authority == null || string.IsNullOrWhiteSpace(authority.Name))
			{
				record.Authorities.RemoveAt(i);
				changed = true;
				continue;
			}
			string sourceDaoTu = string.IsNullOrWhiteSpace(authority.SourceDaoTu) ? record.DaoTu : authority.SourceDaoTu;
			string normalizedAuthorityName = XjGuoWeiAuthorityCatalog.NormalizeAuthorityName(sourceDaoTu, authority.Name);
			if (!string.Equals(authority.Name, normalizedAuthorityName, StringComparison.Ordinal))
			{
				authority.Name = normalizedAuthorityName;
				changed = true;
			}
			authority.Status = NormalizeStatus(authority.Status);
			bool native = IsNativeRootAuthority(record, authority);
			bool external = IsExternalRootProjection(record, authority);
			if (!native && !external)
			{
				// 清理旧版将余闰派生权柄、交感词直接塞入六柄状态表的残留。
				record.Authorities.RemoveAt(i);
				changed = true;
				continue;
			}

			if (native)
			{
				bool permanentlyLost = XjGuoWeiQuanBingRegistry.IsAuthorityLost(record.DaoTu, authority.Name);
				bool pending = IsAuthorityPendingIntegration(record.DaoTu, authority.Name);
				if (permanentlyLost)
				{
					changed |= SetAuthority(record, authority, "失", 0L, authority.HolderName, record.DaoTu, currentYear);
					continue;
				}
				if (pending)
				{
					changed |= SetAuthority(record, authority, "裂", 0L, authority.HolderName, record.DaoTu, currentYear);
					continue;
				}
				if (authority.HolderActorId > 0L)
				{
					if (IsNativeAuthorityHeldByActiveFruit(record.DaoTu, authority.Name, authority.HolderActorId))
					{
						changed |= SetAuthority(record, authority, "执", authority.HolderActorId, authority.HolderName, record.DaoTu, currentYear);
					}
					else
					{
						// 旧档持有人已经死亡、离位或不再是本道果位时，
						// 根权柄只能藏回本道，不能保留幽灵“执”状态。
						changed |= SetAuthority(record, authority, "藏", 0L, string.Empty, record.DaoTu, currentYear);
					}
					continue;
				}
				if (string.Equals(authority.Status, "裂", StringComparison.Ordinal)
					|| string.Equals(authority.Status, "失", StringComparison.Ordinal)
					|| string.Equals(authority.Status, "借", StringComparison.Ordinal)
					|| string.Equals(authority.Status, "易", StringComparison.Ordinal))
				{
					changed |= SetAuthority(record, authority, "归", 0L, string.Empty, record.DaoTu, currentYear);
				}
				else if (string.Equals(authority.Status, "执", StringComparison.Ordinal))
				{
					changed |= SetAuthority(record, authority, "显", 0L, string.Empty, record.DaoTu, currentYear);
				}
				else if (string.Equals(authority.Status, "归", StringComparison.Ordinal)
					&& authority.LastChangedYear > 0 && authority.LastChangedYear < currentYear)
				{
					// “归”是融合失败后的过渡态；下一年稳定为可承接的“显”。
					changed |= SetAuthority(record, authority, "显", 0L, string.Empty, record.DaoTu, currentYear);
				}
				else if (authority.HolderActorId != 0L || !string.IsNullOrWhiteSpace(authority.HolderName))
				{
					changed |= SetAuthority(record, authority, authority.Status, 0L, string.Empty, record.DaoTu, currentYear);
				}
				continue;
			}

			string source = Normalize(authority.SourceDaoTu);
			bool sourceLostHere = IsAuthorityLostToTarget(source, authority.Name, record.DaoTu);
			bool borrowed = authority.HolderActorId > 0L
				&& IsBorrowPending(source, record.DaoTu, authority.Name, authority.HolderActorId);
			if (sourceLostHere)
			{
				// “易”柄跟随目标道途正果，而不是跟随最初夺柄者个人。
				ResolveActiveFruitHolder(record.DaoTu, out long holderId, out string holderName);
				changed |= SetAuthority(record, authority, "易", holderId, holderName, source, currentYear);
			}
			else if (borrowed)
			{
				changed |= SetAuthority(record, authority, "借", authority.HolderActorId, authority.HolderName, source, currentYear);
			}
			else
			{
				record.Authorities.RemoveAt(i);
				changed = true;
			}
		}
		return changed;
	}

	private static bool IsNativeAuthorityHeldByActiveFruit(string daoTu, string authorityName, long actorId)
	{
		if (actorId <= 0L || !XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state)
			|| !state.Found || !string.Equals(Normalize(state.DaoTu), Normalize(daoTu), StringComparison.Ordinal)
			|| !string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(state.GuoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return false;
		}
		return ContainsAuthority(state.LocalQuanBing, authorityName);
	}


	internal static string BuildIntegratedAuthoritySet(string targetDaoTu)
	{
		XjDaoLineageArchiveRecord record = GetOrCreate(targetDaoTu);
		if (record?.Authorities == null) return string.Empty;
		return string.Join(",", record.Authorities
			.Where(value => value != null && IsExternalRootProjection(record, value)
				&& string.Equals(value.Status, "易", StringComparison.Ordinal)
				&& IsAuthorityLostToTarget(value.SourceDaoTu, value.Name, record.DaoTu))
			.Select(value => value.Name)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(value => value, StringComparer.Ordinal));
	}

	internal static string BuildIntegratedAuthoritySources(string targetDaoTu)
	{
		XjDaoLineageArchiveRecord record = GetOrCreate(targetDaoTu);
		if (record?.Authorities == null) return string.Empty;
		return string.Join(";", record.Authorities
			.Where(value => value != null && IsExternalRootProjection(record, value)
				&& string.Equals(value.Status, "易", StringComparison.Ordinal)
				&& IsAuthorityLostToTarget(value.SourceDaoTu, value.Name, record.DaoTu))
			.Select(value => value.Name + ":" + Normalize(value.SourceDaoTu) + "/果位融权")
			.Distinct(StringComparer.Ordinal)
			.OrderBy(value => value, StringComparer.Ordinal));
	}

	internal static bool OnIntegratedAuthorityReseized(
		long killerActorId,
		string killerName,
		string originalSourceDaoTu,
		string previousTargetDaoTu,
		string newTargetDaoTu,
		string authorityName,
		int currentYear)
	{
		string source = Normalize(originalSourceDaoTu);
		string previous = Normalize(previousTargetDaoTu);
		string next = Normalize(newTargetDaoTu);
		string authority = Normalize(authorityName);
		if (source.Length == 0 || previous.Length == 0 || next.Length == 0 || authority.Length == 0
			|| string.Equals(previous, next, StringComparison.Ordinal)
			|| !XjGuoWeiAuthorityCatalog.Get(source).Contains(authority)) return false;
		if (!XjGuoWeiQuanBingRegistry.TryGetLostAuthorityRecord(source, authority, out XjGuoWeiQuanBingLostAuthorityArchiveData lost)
			|| lost == null || !string.Equals(Normalize(lost.TargetDaoTu), previous, StringComparison.Ordinal)) return false;

		XjDaoLineageArchiveRecord previousRecord = GetOrCreate(previous);
		if (previousRecord == null) return false;
		XjDaoAuthorityArchiveData projection = FindExternalAuthority(previousRecord, authority, source);
		if (projection == null || !string.Equals(projection.Status, "易", StringComparison.Ordinal)) return false;

		// 已经融入他途果位的根权柄仍可继续被夺。若夺柄者恰好就是原道果位，
		// 这不是“再次融入原道形成一个外来易柄”，而是原道真正收回自己的根权柄。
		// 因而必须清掉 lost 账本与旧果位投影，并恢复本道六柄中的原生权柄。
		if (string.Equals(next, source, StringComparison.Ordinal))
		{
			XjDaoLineageArchiveRecord sourceRecord = GetOrCreate(source);
			XjDaoAuthorityArchiveData native = FindNativeAuthority(sourceRecord, authority);
			if (sourceRecord == null || native == null) return false;

			previousRecord.Authorities.Remove(projection);
			previousRecord.LastChangedYear = Math.Max(previousRecord.LastChangedYear, currentYear);
			XjGuoWeiQuanBingRegistry.ClearLostAuthority(source, authority);
			ResolveActiveFruitHolder(source, out long recoveredHolderId, out string recoveredHolderName);
			SetAuthority(
				sourceRecord, native, recoveredHolderId > 0L ? "执" : "归",
				recoveredHolderId, recoveredHolderName, source, currentYear);

			previousRecord.Vitality = Math.Max(0, previousRecord.Vitality - 2);
			sourceRecord.Vitality = Math.Min(100, sourceRecord.Vitality + 2);
			previousRecord.Phase = ResolvePhase(previousRecord.Vitality, previousRecord.RevivalYear > 0);
			sourceRecord.Phase = ResolvePhase(sourceRecord.Vitality, sourceRecord.RevivalYear > 0);
			Touch();

			string recoveredDisplay = XjDaoIntentionCatalog.FormatAuthority(source, authority);
			string recoveredText = "【权柄归道】"
				+ (string.IsNullOrWhiteSpace(killerName) ? "一位真君" : killerName.Trim())
				+ "斩落" + previous + "果位持柄者，将其已融的" + source + recoveredDisplay
				+ "夺回本途，根权柄重新归入" + source + "果位。";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.World, "权柄归道", recoveredText, importance: 5, isProtected: true,
				actorId: killerActorId, actorName: killerName, year: currentYear,
				iconIdOverride: XjEventIconCatalog.HistoryWorld, eventType: "IntegratedAuthorityRecoveredBySource",
				result: XjHistoryResult.Transfer, mirrorToWorldLog: false);
			XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
				recoveredText, XjAnnouncementCategory.AuthorityPosition, duration: 6.5f, color: "#83C7B5",
				delayFrames: 1, iconId: XjEventIconCatalog.HistoryWorld);
			return true;
		}

		XjDaoLineageArchiveRecord nextRecord = GetOrCreate(next);
		if (nextRecord == null) return false;
		previousRecord.Authorities.Remove(projection);
		previousRecord.LastChangedYear = Math.Max(previousRecord.LastChangedYear, currentYear);

		XjDaoAuthorityArchiveData nextProjection = FindExternalAuthority(nextRecord, authority, source);
		if (nextProjection == null)
		{
			nextProjection = new XjDaoAuthorityArchiveData { Name = authority, SourceDaoTu = source };
			nextRecord.Authorities.Add(nextProjection);
		}
		XjGuoWeiQuanBingRegistry.RecordLostAuthority(source, authority, next, currentYear, "已融果位权柄再被外道夺走");
		ResolveActiveFruitHolder(next, out long holderId, out string holderName);
		SetAuthority(nextRecord, nextProjection, "易", holderId, holderName, source, currentYear);
		previousRecord.Vitality = Math.Max(0, previousRecord.Vitality - 2);
		nextRecord.Vitality = Math.Min(100, nextRecord.Vitality + 2);
		previousRecord.Phase = ResolvePhase(previousRecord.Vitality, previousRecord.RevivalYear > 0);
		nextRecord.Phase = ResolvePhase(nextRecord.Vitality, nextRecord.RevivalYear > 0);
		Touch();

		string display = XjDaoIntentionCatalog.FormatAuthority(source, authority);
		string text = "【权柄再夺】" + (string.IsNullOrWhiteSpace(killerName) ? "一位真君" : killerName.Trim())
			+ "斩落" + previous + "果位持柄者，将其已融的" + source + display + "再夺入" + next + "果位。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World, "权柄再夺", text, importance: 5, isProtected: true,
			actorId: killerActorId, actorName: killerName, year: currentYear,
			iconIdOverride: XjEventIconCatalog.HistoryWorld, eventType: "IntegratedAuthorityReseized",
			result: XjHistoryResult.Transfer, mirrorToWorldLog: false);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			text, XjAnnouncementCategory.AuthorityPosition, duration: 6.5f, color: "#D59B45",
			delayFrames: 1, iconId: XjEventIconCatalog.HistoryWorld);
		return true;
	}

	internal static bool TryResolveActiveFruitHolder(string daoTu, out long actorId, out string actorName)
	{
		actorId = 0L;
		actorName = string.Empty;
		string normalized = Normalize(daoTu);
		if (normalized.Length == 0) return false;

		if (XjGuoWeiRegistry.TryFindActiveAnchor(normalized, XjGuoWeiCalculator.ZhengWei, out XjGuoWeiRegistryEntry fruit)
			&& fruit.Found && fruit.IsActive && fruit.ActorId > 0L)
		{
			actorId = fruit.ActorId;
			actorName = fruit.ActorName ?? string.Empty;
			return true;
		}

		// 道胎若以余/闰为本位、后补果位，果位占用保存在独立双位账本，
		// 普通 Registry 不会出现第二条活动位序。这里必须把该真实果位也认作承柄者。
		IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> bindings = XjFruitPositionWorldState.ReadDaoTaiBindingsSnapshot();
		for (int i = 0; i < bindings.Count; i++)
		{
			XjDaoTaiPositionBindingArchiveRecord binding = bindings[i];
			if (binding == null || binding.ActorId <= 0L
				|| !XjDaoTaiDualPositionSystem.TryResolveBindingPair(binding, out XjDerivedPositionArchiveRecord boundFruit, out _)
				|| boundFruit == null
				|| !string.Equals(Normalize(boundFruit.DaoTu), normalized, StringComparison.Ordinal)
				|| !XjActorRegistry.ResolveKnownOrWorld(binding.ActorId, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor)
				|| !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) continue;
			actorId = binding.ActorId;
			actorName = actor.getName() ?? string.Empty;
			return true;
		}
		return false;
	}

	private static void ResolveActiveFruitHolder(string daoTu, out long actorId, out string actorName)
	{
		TryResolveActiveFruitHolder(daoTu, out actorId, out actorName);
	}

	private static bool IsAuthorityLostToTarget(string sourceDaoTu, string authorityName, string targetDaoTu)
	{
		return XjGuoWeiQuanBingRegistry.IsAuthorityLostToTarget(sourceDaoTu, authorityName, targetDaoTu);
	}

	private static bool IsAuthorityPendingIntegration(string sourceDaoTu, string authorityName)
	{
		return XjGuoWeiQuanBingRegistry.HasPendingIntegration(sourceDaoTu, authorityName);
	}

	private static bool IsBorrowPending(
		string sourceDaoTu,
		string targetDaoTu,
		string authorityName,
		long actorId)
	{
		return XjGuoWeiQuanBingRegistry.HasBorrowPending(sourceDaoTu, targetDaoTu, authorityName, actorId);
	}

	private static bool ContainsAuthority(string raw, string authorityName)
	{
		return XjGuoWeiQuanBingRegistry.ContainsAuthorityToken(raw, authorityName);
	}

	private static bool SetAuthority(
		XjDaoLineageArchiveRecord record,
		XjDaoAuthorityArchiveData authority,
		string status,
		long holderActorId,
		string holderName,
		string sourceDaoTu,
		int year)
	{
		if (record == null || authority == null) return false;
		string normalizedStatus = NormalizeStatus(status);
		string normalizedHolder = holderName ?? string.Empty;
		string normalizedSource = Normalize(sourceDaoTu);
		bool changed = !string.Equals(authority.Status, normalizedStatus, StringComparison.Ordinal)
			|| authority.HolderActorId != Math.Max(0L, holderActorId)
			|| !string.Equals(authority.HolderName ?? string.Empty, normalizedHolder, StringComparison.Ordinal)
			|| !string.Equals(Normalize(authority.SourceDaoTu), normalizedSource, StringComparison.Ordinal);
		if (!changed) return false;
		authority.Status = normalizedStatus;
		authority.HolderActorId = Math.Max(0L, holderActorId);
		authority.HolderName = normalizedHolder;
		authority.SourceDaoTu = normalizedSource;
		authority.LastChangedYear = Math.Max(authority.LastChangedYear, year);
		record.LastChangedYear = Math.Max(record.LastChangedYear, year);
		return true;
	}

	private static XjDaoAuthorityArchiveData FindNativeAuthority(XjDaoLineageArchiveRecord record, string name)
	{
		if (record?.Authorities == null || string.IsNullOrWhiteSpace(name)) return null;
		for (int i = 0; i < record.Authorities.Count; i++)
		{
			XjDaoAuthorityArchiveData authority = record.Authorities[i];
			if (authority != null && string.Equals(authority.Name, name, StringComparison.Ordinal)
				&& IsNativeRootAuthority(record, authority)) return authority;
		}
		return null;
	}

	private static XjDaoAuthorityArchiveData FindExternalAuthority(
		XjDaoLineageArchiveRecord record,
		string name,
		string sourceDaoTu)
	{
		if (record?.Authorities == null || string.IsNullOrWhiteSpace(name)) return null;
		string source = Normalize(sourceDaoTu);
		for (int i = 0; i < record.Authorities.Count; i++)
		{
			XjDaoAuthorityArchiveData authority = record.Authorities[i];
			if (authority != null && string.Equals(authority.Name, name, StringComparison.Ordinal)
				&& string.Equals(Normalize(authority.SourceDaoTu), source, StringComparison.Ordinal)
				&& IsExternalRootProjection(record, authority)) return authority;
		}
		return null;
	}

	private static bool IsNativeRootAuthority(XjDaoLineageArchiveRecord record, XjDaoAuthorityArchiveData authority)
	{
		if (record == null || authority == null) return false;
		return string.Equals(Normalize(authority.SourceDaoTu), Normalize(record.DaoTu), StringComparison.Ordinal)
			&& XjGuoWeiAuthorityCatalog.Get(record.DaoTu).Contains(authority.Name);
	}

	private static bool IsExternalRootProjection(XjDaoLineageArchiveRecord record, XjDaoAuthorityArchiveData authority)
	{
		if (record == null || authority == null) return false;
		string source = Normalize(authority.SourceDaoTu);
		return source.Length > 0
			&& !string.Equals(source, Normalize(record.DaoTu), StringComparison.Ordinal)
			&& XjGuoWeiAuthorityCatalog.Get(source).Contains(authority.Name);
	}

	private static bool IsKnownStatus(string status)
	{
		return status == "潜" || status == "显" || status == "执" || status == "藏"
			|| status == "裂" || status == "借" || status == "归" || status == "失" || status == "易";
	}

	private static string NormalizeStatus(string status)
	{
		string value = Normalize(status);
		return IsKnownStatus(value) ? value : "潜";
	}


	private static void NormalizeRecord(XjDaoLineageArchiveRecord record)
	{
		record.CoreRevision = Math.Max(1, record.CoreRevision);
		record.Vitality = Math.Max(0, Math.Min(100, record.Vitality));
		record.Phase = string.IsNullOrWhiteSpace(record.Phase) ? ResolvePhase(record.Vitality, record.RevivalYear > 0) : record.Phase.Trim();
		record.CoreDoctrine = string.IsNullOrWhiteSpace(record.CoreDoctrine) ? BuildDefaultDoctrine(record.DaoTu) : record.CoreDoctrine.Trim();
		record.ShenTongBias = string.IsNullOrWhiteSpace(record.ShenTongBias) ? "守本" : record.ShenTongBias.Trim();
		record.Authorities ??= new List<XjDaoAuthorityArchiveData>();
		IReadOnlyList<string> names = XjGuoWeiAuthorityCatalog.Get(record.DaoTu);
		if (record.Authorities.Count == 0)
		{
			for (int i = 0; i < names.Count; i++) record.Authorities.Add(new XjDaoAuthorityArchiveData { Name = names[i], Status = i < 2 ? "显" : "潜", SourceDaoTu = record.DaoTu });
		}
		else
		{
			for (int i = 0; i < record.Authorities.Count; i++)
			{
				XjDaoAuthorityArchiveData authority = record.Authorities[i];
				if (authority == null) continue;
				string sourceDaoTu = string.IsNullOrWhiteSpace(authority.SourceDaoTu) ? record.DaoTu : authority.SourceDaoTu;
				authority.Name = XjGuoWeiAuthorityCatalog.NormalizeAuthorityName(sourceDaoTu, authority.Name);
				authority.Status = NormalizeStatus(authority.Status);
				if (string.IsNullOrWhiteSpace(authority.SourceDaoTu) && names.Contains(authority.Name))
					authority.SourceDaoTu = record.DaoTu;
			}
		}
	}

	private static int AuthorityPriority(string status)
	{
		if (string.Equals(status, "执", StringComparison.Ordinal)) return 0;
		if (string.Equals(status, "归", StringComparison.Ordinal)) return 1;
		if (string.Equals(status, "易", StringComparison.Ordinal)) return 2;
		if (string.Equals(status, "显", StringComparison.Ordinal)) return 3;
		if (string.Equals(status, "借", StringComparison.Ordinal)) return 4;
		if (string.Equals(status, "裂", StringComparison.Ordinal)) return 5;
		return 6;
	}

	private static string ResolvePhase(int vitality, bool revived)
	{
		if (revived && vitality >= 65) return "中兴";
		if (vitality >= 85) return "鼎盛";
		if (vitality >= 65) return "兴盛";
		if (vitality >= 45) return "守成";
		if (vitality >= 25) return "倾颓";
		return "断续";
	}

	private static string BuildDefaultDoctrine(string daoTu) => Normalize(daoTu) + "守玄宣法";
	private static string BuildRevisedDoctrine(string daoTu, string direction, int revision) => Normalize(daoTu) + direction + "第" + revision + "代宣法";
	private static string ResolveRevivalDirection(long actorId, int year)
	{
		string[] values = { "复古", "更新", "合流", "斩旧" };
		return values[XjDeterministicHash.PositiveIndex(actorId + year, "lineage_revival_direction", values.Length)];
	}
	private static string BuildCrossAuthority(string source, string manifest) => ShortDao(source) + ShortDao(manifest) + "交感";
	private static string BuildFoundationKey(XjDaoProofFoundationArchiveData foundation)
	{
		if (foundation == null) return string.Empty;
		return foundation.CityId + "|" + foundation.FounderActorId + "|" + foundation.FoundedYear
			+ "|" + Normalize(foundation.SourceDaoTu) + "|" + Normalize(foundation.ManifestDaoTu);
	}
	private static string ShortDao(string daoTu)
	{
		string value = Normalize(daoTu);
		if (value.Length == 2 && "阴阳雷金木水火土炁仪".IndexOf(value[1]) >= 0) return value.Substring(0, 1);
		return value;
	}
	private static string Normalize(string value) => (value ?? string.Empty).Trim();
	private static void Touch()
	{
		unchecked { revision++; }
		XjWorldArchiveSystem.MarkChanged();
	}

	private static XjDaoLineageArchiveRecord Clone(XjDaoLineageArchiveRecord source)
	{
		XjDaoLineageArchiveRecord result = new XjDaoLineageArchiveRecord
		{
			DaoTu = source.DaoTu, CoreRevision = source.CoreRevision, Vitality = source.Vitality,
			Phase = source.Phase, CoreDoctrine = source.CoreDoctrine, ShenTongBias = source.ShenTongBias,
			RevivalActorId = source.RevivalActorId, RevivalActorName = source.RevivalActorName,
			RevivalDirection = source.RevivalDirection, RevivalYear = source.RevivalYear,
				LastChangedYear = source.LastChangedYear, LastSyncedYear = source.LastSyncedYear
		};
		if (source.Authorities != null)
		{
			for (int i = 0; i < source.Authorities.Count; i++)
			{
				XjDaoAuthorityArchiveData a = source.Authorities[i];
				if (a == null) continue;
				result.Authorities.Add(new XjDaoAuthorityArchiveData { Name = a.Name, Status = a.Status, HolderActorId = a.HolderActorId, HolderName = a.HolderName, SourceDaoTu = a.SourceDaoTu, LastChangedYear = a.LastChangedYear });
			}
		}
		return result;
	}
	private static XjDaoProofFoundationArchiveData Clone(XjDaoProofFoundationArchiveData source) => new XjDaoProofFoundationArchiveData
	{
		CityId = source.CityId, CityName = source.CityName, FounderActorId = source.FounderActorId,
		FounderName = source.FounderName, DaoTitle = source.DaoTitle, SourceDaoTu = source.SourceDaoTu,
		ManifestDaoTu = source.ManifestDaoTu, PositionType = source.PositionType, Doctrine = source.Doctrine,
		LegacyDoctrine = source.LegacyDoctrine, JinXing = source.JinXing, AuthorityScope = source.AuthorityScope,
		FoundedYear = source.FoundedYear
	};
}
