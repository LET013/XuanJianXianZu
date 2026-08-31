using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjReincarnationRecord
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string RaceKey;
	internal readonly long FamilyStableId;
	internal readonly string RealmId;
	internal readonly string DaoTu;
	internal readonly string GongFaName;
	internal readonly int GongFaGrade;
	internal readonly int GongFaStage;
	internal readonly float GongFaProgress;
	internal readonly string JinXing;
	internal readonly string GuoWei;
	internal readonly int JinDanYiXiang;
	internal readonly int DeathYear;
	internal readonly string Mode;
	internal readonly string GuoWeiZhongAi;
	internal readonly string FuQiPayload;
	internal readonly long TargetActorId;
	internal readonly string TargetActorName;
	internal readonly int AppliedYear;
	internal readonly string Status;

	internal XjReincarnationRecord(
		bool found,
		long actorId,
		string actorName,
		string raceKey,
		long familyStableId,
		string realmId,
		string daoTu,
		string gongFaName,
		int gongFaGrade,
		int gongFaStage,
		float gongFaProgress,
		string jinXing,
		string guoWei,
		int jinDanYiXiang,
		int deathYear,
		string mode,
		string guoWeiZhongAi,
		string fuQiPayload,
		long targetActorId,
		string targetActorName,
		int appliedYear,
		string status)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		RaceKey = raceKey ?? string.Empty;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		RealmId = realmId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		GongFaName = gongFaName ?? string.Empty;
		GongFaGrade = gongFaGrade < 0 ? 0 : gongFaGrade;
		// 字段仅为旧档结构兼容；功法阶段/进度已从规则中删除。
		_ = gongFaStage;
		_ = gongFaProgress;
		GongFaStage = 0;
		GongFaProgress = 0f;
		JinXing = jinXing ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		JinDanYiXiang = jinDanYiXiang < 0 ? 0 : jinDanYiXiang;
		DeathYear = deathYear < 0 ? 0 : deathYear;
		Mode = mode ?? string.Empty;
		GuoWeiZhongAi = guoWeiZhongAi ?? string.Empty;
		FuQiPayload = fuQiPayload ?? string.Empty;
		TargetActorId = targetActorId < 0L ? 0L : targetActorId;
		TargetActorName = targetActorName ?? string.Empty;
		AppliedYear = appliedYear < 0 ? 0 : appliedYear;
		Status = status ?? string.Empty;
	}
}

/// <summary>
/// 金性转世专用的紧凑继承载荷。普通金丹、紫府转世仍使用原有字段，
/// 只有服气真人求证失败触发的转世记录才写入此结构。
/// </summary>
internal sealed class XjFuQiReincarnationPayload
{
	public int TrueSpiritRemaining { get; set; }
	public int BreakthroughBonusPercent { get; set; }
	public int CumulativeFailureCount { get; set; }
	public float SourceHuiGuang { get; set; }
	public string InheritedJinXing { get; set; } = string.Empty;
	public string FuQiLineageId { get; set; } = string.Empty;
	public string FuQiDaoTuRootId { get; set; } = string.Empty;
	public string FuQiCoreType { get; set; } = string.Empty;
	public string FuQiCoreId { get; set; } = string.Empty;
	public int FuQiCoreProgress { get; set; }
	public string FuQiShenMiaoId { get; set; } = string.Empty;
	public int FuQiSwordQi { get; set; }
	public string FuQiStudiedIntentIds { get; set; } = string.Empty;
	public int FuQiYangQingMingCompletedYear { get; set; }
	public int FuQiShenMiaoPerfectionYear { get; set; }
	public string GongFaCollectionJson { get; set; } = string.Empty;
	public int GongFaCollectionVersion { get; set; }
	public string CraftTraitId { get; set; } = string.Empty;
	public int CraftAlchemyRank { get; set; }
	public int CraftRankSchema { get; set; }
	public int ArtifactRefinerRank { get; set; }
	public int ArtifactTrainingSuccess { get; set; }
	public int ArtifactFaQiSuccess { get; set; }
	public int ArtifactLingBaoSuccess { get; set; }
	public int ArtifactFaBaoSuccess { get; set; }
	public int TalismanProficiency { get; set; }
	public int TalismanRank { get; set; }
	public int FormationProficiency { get; set; }
	public int FormationRank { get; set; }
	public int AlchemyTotalProficiency { get; set; }
	public int AlchemySuccessCount { get; set; }
	public int AlchemyFailureCount { get; set; }
	public int AlchemyMajorAccidentCount { get; set; }
	public int AlchemyLastYear { get; set; }
}

internal static class XjReincarnation
{
	private const string StatusPending = "Pending";
	private const string StatusApplied = "Applied";
	private const string ModeJinDan = "JinDan";
	private const string ModeGuoWeiZhongAi = "GuoWeiZhongAi";
	private const string ModeZiFuJinXing = "ZiFuJinXing";
	private const string ModeFamilyBorrowJinXing = "FamilyBorrowJinXing";
	private const string ModeFuQiJinXing = "FuQiJinXing";
	private const string ModeShiReincarnation = "ShiReincarnation";
	private const int JinDanEarlyReincarnationChanceBasis = 1000;
	private const int JinDanMiddleReincarnationChanceBasis = 3000;
	private const int JinDanLateReincarnationChanceBasis = 5000;
	private const int JinDanPeakReincarnationChanceBasis = 7000;

	private static readonly Dictionary<long, XjReincarnationRecord> recordsByActorId = new Dictionary<long, XjReincarnationRecord>();
	// 已应用转世的 target->source 反向索引。它只随转世记录写入/载入维护，
	// 用于需要判定“同一转世链人物”的少量规则（如水月照真一真灵仅一次），
	// 避免每次从全部历史转世记录中反扫。
	private static readonly Dictionary<long, long> sourceActorIdByTargetActorId = new Dictionary<long, long>();
	// 只索引仍待归返的释修记录，避免年度处理随历史转世档案总量线性增长。
	private static readonly HashSet<long> pendingShiActorIds = new HashSet<long>();
	// 今释摩诃及以上在等待肉身重塑期间仍占“同一真灵原位”的资格。
	// 旧实现只统计当前活着的摩诃，旧身死后立即释放108位之一，新摩诃可以
	// 在20~60年等待期内把位置占满，导致前世每年重塑一具新身、承位失败、
	// 删除新身，再永久保持 Pending。该集合只索引待归返高位真灵，不扫角色。
	private static readonly HashSet<long> pendingModernMoHeReservationActorIds = new HashSet<long>();
	// 所有仍待归返的今释真灵都预留一个普通在世承载位。该预留只用于阻止
	// 后来产生的新今释抢占旧身死亡后释放的位置；真正重塑时仍走专属回归弹性。
	private static readonly HashSet<long> pendingModernShiReturnReservationActorIds = new HashSet<long>();
	private static readonly List<long> pendingShiIdScratch = new List<long>(64);
	private static readonly Queue<long> dueShiReturnQueue = new Queue<long>();
	private static readonly HashSet<long> queuedDueShiReturnIds = new HashSet<long>();
	private static bool _processingShiReturns;

	internal static void RecordFromSnapshot(XjDeathSnapshot snapshot)
	{
		if (!TryBuildPendingRecord(snapshot, false, out XjReincarnationRecord record) || recordsByActorId.ContainsKey(snapshot.ActorId))
		{
			return;
		}

		recordsByActorId[snapshot.ActorId] = record;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	internal static void RecordForcedJinDanFromSnapshot(XjDeathSnapshot snapshot)
	{
		if (!TryBuildPendingRecord(snapshot, true, out XjReincarnationRecord record) || recordsByActorId.ContainsKey(snapshot.ActorId))
		{
			return;
		}

		recordsByActorId[snapshot.ActorId] = record;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	/// <summary>
	/// 服气真人求证失败时，在原角色真正死亡前登记定向金性转世。死亡归档
	/// 随后看到同一 source actorId 时会保持该高优先级记录，不再另建普通转世。
	/// </summary>
	internal static bool RecordForcedFuQiJinXing(
		Actor source,
		in XjDeathSnapshot snapshot,
		int trueSpiritRemaining,
		int breakthroughBonusPercent,
		int cumulativeFailureCount)
	{
		if (source?.data == null || !snapshot.Found || snapshot.ActorId <= 0L
			|| recordsByActorId.ContainsKey(snapshot.ActorId))
		{
			return false;
		}

		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.HuiGuang, out float huiGuang);
		string inheritedJinXing = snapshot.JinXing ?? string.Empty;
		if (string.IsNullOrWhiteSpace(inheritedJinXing) && !string.IsNullOrWhiteSpace(snapshot.DaoTu))
		{
			inheritedJinXing = XjJinXingCalculator.Calculate(snapshot.DaoTu, snapshot.ActorId);
		}
		XjAlchemyCrafterArchiveData alchemy = XjAlchemyRuntimeRegistry.ReadCrafter(snapshot.ActorId);
		XjFuQiReincarnationPayload payload = new XjFuQiReincarnationPayload
		{
			TrueSpiritRemaining = Math.Clamp(trueSpiritRemaining, 0, 3),
			BreakthroughBonusPercent = Math.Clamp(
				breakthroughBonusPercent, 0, XjFuQiBalancePolicy.MaxReincarnationBreakthroughBonusPercent),
			CumulativeFailureCount = Math.Max(0, cumulativeFailureCount),
			SourceHuiGuang = Math.Max(0f, huiGuang),
			InheritedJinXing = inheritedJinXing,
			FuQiLineageId = ReadString(source, XjActorDataKeys.FuQiLineageId),
			FuQiDaoTuRootId = ReadString(source, XjActorDataKeys.FuQiDaoTuRootId),
			FuQiCoreType = ReadString(source, XjActorDataKeys.FuQiCoreType),
			FuQiCoreId = ReadString(source, XjActorDataKeys.FuQiCoreId),
			FuQiCoreProgress = ReadInt(source, XjActorDataKeys.FuQiCoreProgress),
			FuQiShenMiaoId = ReadString(source, XjActorDataKeys.FuQiShenMiaoId),
			FuQiSwordQi = ReadInt(source, XjActorDataKeys.FuQiSwordQi),
			FuQiStudiedIntentIds = ReadString(source, XjActorDataKeys.FuQiStudiedIntentIds),
			FuQiYangQingMingCompletedYear = ReadInt(source, XjActorDataKeys.FuQiYangQingMingCompletedYear),
			FuQiShenMiaoPerfectionYear = ReadInt(source, XjActorDataKeys.FuQiShenMiaoPerfectionYear),
			GongFaCollectionJson = ReadString(source, XjActorDataKeys.XjGongFaCollectionJson),
			GongFaCollectionVersion = ReadInt(source, XjActorDataKeys.XjGongFaCollectionVersion),
			CraftTraitId = XjCraftTraitRules.GetPrimaryTraitId(source),
			CraftAlchemyRank = ReadInt(source, XjActorDataKeys.XjCraftAlchemyRank),
			CraftRankSchema = ReadInt(source, XjActorDataKeys.XjCraftRankSchema),
			ArtifactRefinerRank = ReadInt(source, XjActorDataKeys.XjArtifactRefinerRank),
			ArtifactTrainingSuccess = ReadInt(source, XjActorDataKeys.XjArtifactTrainingSuccess),
			ArtifactFaQiSuccess = ReadInt(source, XjActorDataKeys.XjArtifactFaQiSuccess),
			ArtifactLingBaoSuccess = ReadInt(source, XjActorDataKeys.XjArtifactLingBaoSuccess),
			ArtifactFaBaoSuccess = ReadInt(source, XjActorDataKeys.XjArtifactFaBaoSuccess),
			TalismanProficiency = ReadInt(source, XjActorDataKeys.XjTalismanProficiency),
			TalismanRank = ReadInt(source, XjActorDataKeys.XjTalismanRank),
			FormationProficiency = ReadInt(source, XjActorDataKeys.XjFormationProficiency),
			FormationRank = ReadInt(source, XjActorDataKeys.XjFormationRank),
			AlchemyTotalProficiency = Math.Max(0, alchemy.TotalProficiency),
			AlchemySuccessCount = Math.Max(0, alchemy.SuccessCount),
			AlchemyFailureCount = Math.Max(0, alchemy.FailureCount),
			AlchemyMajorAccidentCount = Math.Max(0, alchemy.MajorAccidentCount),
			AlchemyLastYear = Math.Max(0, alchemy.LastAlchemyYear)
		};

		string payloadJson;
		try { payloadJson = JsonConvert.SerializeObject(payload, Formatting.None); }
		catch (System.Exception xjCaught250_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjReincarnation.cs:250", xjCaught250_1);
			 return false; }
		recordsByActorId[snapshot.ActorId] = new XjReincarnationRecord(
			true,
			snapshot.ActorId,
			snapshot.Name,
			snapshot.RaceKey,
			snapshot.FamilyStableId,
			snapshot.RealmId,
			snapshot.DaoTu,
			snapshot.GongFaName,
			snapshot.GongFaGrade,
			0,
			0f,
			inheritedJinXing,
			string.Empty,
			0,
			snapshot.Year,
			ModeFuQiJinXing,
			string.Empty,
			payloadJson,
			0L,
			string.Empty,
			0,
			StatusPending);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}


	/// <summary>
	/// 今释死亡且真灵未俱灭时统一登记归返：僧侣、法师归旃檀林，
	/// 怜愍与摩诃归各自真灵挂靠金地；法相以上仍按本人金地保存权位，
	/// 但肉身只能在旃檀林重塑并永久留驻。真灵俱灭由死亡入口直接拦截。
	/// </summary>
	internal static bool RecordForcedShi(Actor source, in XjDeathSnapshot snapshot)
	{
		return RecordShi(source, snapshot, isTrueSpiritReturn: true, yearOverride: 0);
	}

	/// <summary>
	/// 今释摩诃主动转世：先建立非“真灵归返”载荷，再由统一死亡管线确认旧身死亡。
	/// 该路径会推进世数；普通死亡归土仍走RecordForcedShi，不增加世数。
	/// </summary>
	internal static bool RecordVoluntaryShi(Actor source, int currentYear)
	{
		if (source?.data == null || !source.isAlive()
			|| !XjDeathSnapshotBuilder.TryBuild(source, out XjDeathSnapshot snapshot)) return false;
		return RecordShi(source, snapshot, isTrueSpiritReturn: false, yearOverride: Math.Max(1, currentYear));
	}

	private static bool RecordShi(Actor source, in XjDeathSnapshot snapshot,
		bool isTrueSpiritReturn, int yearOverride)
	{
		if (source?.data == null || !snapshot.Found || snapshot.ActorId <= 0L
			|| recordsByActorId.ContainsKey(snapshot.ActorId)
			|| !XjShiState.TryBuildSnapshot(source, out XjShiSnapshot shi)
			|| !string.Equals(shi.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !XjShiWorldRegistry.CanReincarnate(source))
		{
			return false;
		}

		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiLineageId, out string lineageId);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiDharmaFormStage, out string dharmaFormStage);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiVowId, out string vowId);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiMasterActorId, out string masterActorId);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.XjNameBase, out string baseName);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiHonorificTitle, out string honorificTitle);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiDharmaName, out string dharmaName);
		XjActorAccessor.TryGetString(source, XjActorDataKeys.ShiIdentityRootActorId, out string identityRootActorId);
		if (string.IsNullOrWhiteSpace(identityRootActorId))
			identityRootActorId = snapshot.ActorId.ToString(CultureInfo.InvariantCulture);
		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.ShiMingShu, out float shiMingShu);
		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.ShiMingShuPending, out float shiMingShuPending);
		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.MingShu, out float ordinaryMingShu);
		XjActorAccessor.TryGetFloat(source, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiConvertedCount, out int convertedCount);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiDuhuaRuleVersion, out int duhuaRuleVersion);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiSentientConsumptionCount, out int consumptionCount);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiFateDirectLeapBand, out int fateDirectLeapBand);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiConversionSourceTier, out int conversionSourceTier);
		XjActorAccessor.TryGetInt(source, XjActorDataKeys.ShiConversionYear, out int conversionYear);
		bool wasFavorite = false;
		try { wasFavorite = ((BaseSystemData)source.data).favorite; }
		catch { wasFavorite = false; }
		int eventYear = Math.Max(1, yearOverride > 0 ? yearOverride : snapshot.Year);
		string fallbackDomain = string.IsNullOrWhiteSpace(shi.DomainId) ? shi.JinDiId : shi.DomainId;
		string rebirthAnchorId = XjZhantanlinSystem.ResolvePreferredAnchor(source,
			string.IsNullOrWhiteSpace(shi.RebirthAnchorId) ? fallbackDomain : shi.RebirthAnchorId,
			eventYear);
		string assetId = string.Empty;
		try { assetId = ((Asset)source.asset)?.id ?? source.data.asset_id ?? string.Empty; }
		catch { assetId = source.data.asset_id ?? string.Empty; }
		int returnEligibleYear = eventYear + 20 + XjDeterministicHash.PositiveIndex(
			snapshot.ActorId + eventYear, "shi_true_spirit_return_delay", 41);
		List<long> dependentActorIds = new List<long>();
		if (XjShiCatalog.GetRank(shi.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			IReadOnlyList<long> dependents = XjShiWorldRegistry.GetDependentIds(snapshot.ActorId, eventYear);
			for (int i = 0; i < dependents.Count; i++)
			{
				long dependentId = dependents[i];
				if (dependentId > 0L && !dependentActorIds.Contains(dependentId)) dependentActorIds.Add(dependentId);
			}
		}

		XjShiReincarnationPayload payload = new XjShiReincarnationPayload
		{
			Tradition = shi.Tradition,
			PreviousRealm = shi.Realm,
			PreviousSeatId = shi.SeatId,
			PreviousPractice = shi.Practice,
			PreviousShiMingShu = Math.Max(0f, shiMingShu),
			PreviousShiMingShuPending = Math.Max(0f, shiMingShuPending),
			PreviousOrdinaryMingShu = Math.Max(0f, ordinaryMingShu),
			LawIds = string.Empty,
			PracticeDirectionId = ReadString(source, XjActorDataKeys.ShiPracticeDirectionId),
			PracticeDirectionSource = ReadString(source, XjActorDataKeys.ShiPracticeDirectionSource),
			LineageId = lineageId ?? string.Empty,
			PatronActorId = shi.PatronActorId,
			MasterActorId = masterActorId ?? string.Empty,
			DomainId = fallbackDomain,
			JinDiId = shi.JinDiId,
			RebirthAnchorId = rebirthAnchorId,
			PreviousCurrentLife = shi.CurrentLife,
			PreviousCompletedLives = shi.CompletedLives,
			PreviousAlignment = shi.Alignment,
			PreviousConvertedCount = Math.Max(0, convertedCount),
			PreviousDuhuaRuleVersion = Math.Max(0, duhuaRuleVersion),
			PreviousSentientConsumptionCount = Math.Max(0, consumptionCount),
			FateDirectLeapBand = Math.Clamp(fateDirectLeapBand, 0, 5),
			WasFavorite = wasFavorite,
			DharmaFormStage = dharmaFormStage ?? string.Empty,
			VowId = vowId ?? string.Empty,
			BaseName = baseName ?? string.Empty,
			HonorificTitle = honorificTitle ?? string.Empty,
			DharmaName = dharmaName ?? string.Empty,
			IdentityRootActorId = identityRootActorId,
			ConversionSourceTier = Math.Max(0, conversionSourceTier),
			ConversionYear = Math.Max(0, conversionYear),
			SourceHuiGuang = Math.Max(0f, huiGuang),
			ActorAssetId = assetId,
			ReturnEligibleYear = returnEligibleYear,
			IsTrueSpiritReturn = isTrueSpiritReturn,
			DeathYear = eventYear,
			DependentActorIds = dependentActorIds
		};
		string payloadJson;
		try { payloadJson = JsonConvert.SerializeObject(payload, Formatting.None); }
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjReincarnation.RecordShi", ex);
			return false;
		}

		recordsByActorId[snapshot.ActorId] = new XjReincarnationRecord(
			true, snapshot.ActorId, snapshot.Name, snapshot.RaceKey, snapshot.FamilyStableId,
			string.Empty, string.Empty, string.Empty, 0, 0, 0f, string.Empty, string.Empty, 0,
			eventYear, ModeShiReincarnation, string.Empty, payloadJson,
			0L, string.Empty, 0, StatusPending);
		pendingShiActorIds.Add(snapshot.ActorId);
		if (string.Equals(payload.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
			pendingModernShiReturnReservationActorIds.Add(snapshot.ActorId);
		if (RequiresModernMoHePositionReservation(payload))
			pendingModernMoHeReservationActorIds.Add(snapshot.ActorId);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}


	private static bool IsPendingShiRecord(XjReincarnationRecord record)
	{
		return record.Found
			&& record.TargetActorId <= 0L
			&& string.Equals(record.Status, StatusPending, StringComparison.Ordinal)
			&& string.Equals(record.Mode, ModeShiReincarnation, StringComparison.Ordinal);
	}

	internal static int PendingShiCount => pendingShiActorIds.Count;
	internal static int PendingModernShiReturnReservationCount => pendingModernShiReturnReservationActorIds.Count;
	internal static int PendingModernMoHePositionReservationCount => pendingModernMoHeReservationActorIds.Count;

	internal static bool HasPendingModernMoHePositionReservation(long sourceActorId)
	{
		return sourceActorId > 0L && pendingModernMoHeReservationActorIds.Contains(sourceActorId);
	}

	private static bool RequiresModernMoHePositionReservation(XjShiReincarnationPayload payload)
	{
		// 108摩诃位只由“前世仍是摩诃”的真灵保留。法相/世尊活着时本就不占摩诃位，
		// 死后若继续替它们预留一格，会制造“活法相不占、死法相反占位”的幽灵席位。
		return payload != null
			&& string.Equals(payload.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& string.Equals(payload.PreviousRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
	}

	private static void RemovePendingShiIndexes(long sourceActorId)
	{
		if (sourceActorId <= 0L) return;
		pendingShiActorIds.Remove(sourceActorId);
		pendingModernShiReturnReservationActorIds.Remove(sourceActorId);
		pendingModernMoHeReservationActorIds.Remove(sourceActorId);
		queuedDueShiReturnIds.Remove(sourceActorId);
	}

	internal static bool HasPendingShi(long sourceActorId)
	{
		if (sourceActorId <= 0L || !pendingShiActorIds.Contains(sourceActorId)) return false;
		if (recordsByActorId.TryGetValue(sourceActorId, out XjReincarnationRecord record)
			&& IsPendingShiRecord(record)) return true;
		RemovePendingShiIndexes(sourceActorId);
		return false;
	}

	/// <summary>
	/// 待摩诃转世记录中的尊号/法号仍属于同一真灵。即使读档后运行时称号缓存
	/// 尚未由任何在世肉身重建，也不能把这两个身份标识发给别人。
	/// 只在新尊号/法号实际分配时调用，不参与年度扫描。
	/// </summary>
	internal static bool IsPendingShiHonorificReserved(string honorificTitle)
	{
		return IsPendingShiIdentityReserved(honorificTitle, checkHonorific: true);
	}

	internal static bool IsPendingShiDharmaNameReserved(string dharmaName)
	{
		return IsPendingShiIdentityReserved(dharmaName, checkHonorific: false);
	}

	private static bool IsPendingShiIdentityReserved(string value, bool checkHonorific)
	{
		if (string.IsNullOrWhiteSpace(value) || pendingShiActorIds.Count == 0) return false;
		foreach (long sourceId in pendingShiActorIds)
		{
			if (!recordsByActorId.TryGetValue(sourceId, out XjReincarnationRecord record)
				|| !IsPendingShiRecord(record) || string.IsNullOrWhiteSpace(record.FuQiPayload)) continue;
			try
			{
				XjShiReincarnationPayload payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(record.FuQiPayload);
				string reserved = checkHonorific ? payload?.HonorificTitle : payload?.DharmaName;
				if (string.Equals(value, reserved, StringComparison.Ordinal)) return true;
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjReincarnation.PendingShiIdentity", ex);
			}
		}
		return false;
	}

	internal static void CancelPending(long sourceActorId)
	{
		if (sourceActorId <= 0L
			|| !recordsByActorId.TryGetValue(sourceActorId, out XjReincarnationRecord record)
			|| record.TargetActorId > 0L
			|| !string.Equals(record.Status, StatusPending, StringComparison.Ordinal)) return;
		recordsByActorId.Remove(sourceActorId);
		RemovePendingShiIndexes(sourceActorId);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	/// <summary>
	/// 年度控制面只扫描一次仍待归返的释修记录，把已经到期且当前具备物理承载条件的
	/// 真灵放入运行队列。真正的重塑肉身由后台车道逐个执行，避免同一年多名摩诃/法相
	/// 同时归返时把生成、家族恢复、特质同步、列传与存档写脏全部压在一个渲染帧。
	/// </summary>
	internal static void SchedulePendingShiReturns(int currentYear)
	{
		if (currentYear <= 0 || pendingShiActorIds.Count == 0) return;
		pendingShiIdScratch.Clear();
		foreach (long pendingId in pendingShiActorIds) pendingShiIdScratch.Add(pendingId);
		pendingShiIdScratch.Sort();
		for (int pendingIndex = 0; pendingIndex < pendingShiIdScratch.Count; pendingIndex++)
		{
			long sourceId = pendingShiIdScratch[pendingIndex];
			if (!recordsByActorId.TryGetValue(sourceId, out XjReincarnationRecord record)
				|| !IsPendingShiRecord(record))
			{
				RemovePendingShiIndexes(sourceId);
				continue;
			}
			XjShiReincarnationPayload payload;
			try { payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(record.FuQiPayload); }
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjReincarnation.SchedulePendingShiReturns.Payload", ex);
				continue;
			}
			if (payload == null) continue;
			int dueYear = payload.ReturnEligibleYear > 0
				? payload.ReturnEligibleYear
				: Math.Max(1, record.DeathYear) + 20;
			if (currentYear < dueYear) continue;

			bool returnsPhysicallyToZhantanlin =
				XjZhantanlinSystem.RequiresPhysicalReturnToZhantanlin(payload.Tradition, payload.PreviousRealm);
			// 旃檀林尚未显世时不制造一个每帧反复失败的后台任务；下一年度控制面
			// 会重新检查。古释/个人金地归返不受此限制。
			if (returnsPhysicallyToZhantanlin && !XjZhantanlinSystem.IsPlaced) continue;
			if (queuedDueShiReturnIds.Add(sourceId)) dueShiReturnQueue.Enqueue(sourceId);
		}
		pendingShiIdScratch.Clear();
	}

	internal static bool HasQueuedShiReturns => dueShiReturnQueue.Count > 0;

	/// <summary>
	/// 每次最多完成一个真实肉身归返。失败项不在同一帧重试；仍为Pending的记录由下一
	/// 年度重新排队，从而保证错误承载地或第三方生成异常不会形成高速自旋。
	/// </summary>
	internal static bool TickPendingShiReturn(int currentYear)
	{
		if (_processingShiReturns || currentYear <= 0 || World.world?.units == null
			|| dueShiReturnQueue.Count == 0) return false;
		_processingShiReturns = true;
		long sourceId = 0L;
		try
		{
			sourceId = dueShiReturnQueue.Dequeue();
			queuedDueShiReturnIds.Remove(sourceId);
			if (!recordsByActorId.TryGetValue(sourceId, out XjReincarnationRecord record)
				|| !IsPendingShiRecord(record))
			{
				RemovePendingShiIndexes(sourceId);
				return false;
			}

			XjShiReincarnationPayload payload;
			try { payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(record.FuQiPayload); }
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjReincarnation.TickPendingShiReturn.Payload", ex);
				return false;
			}
			if (payload == null) return false;
			int dueYear = payload.ReturnEligibleYear > 0
				? payload.ReturnEligibleYear
				: Math.Max(1, record.DeathYear) + 20;
			if (currentYear < dueYear) return false;

			bool returnsPhysicallyToZhantanlin =
				XjZhantanlinSystem.RequiresPhysicalReturnToZhantanlin(payload.Tradition, payload.PreviousRealm);
			if (returnsPhysicallyToZhantanlin && !XjZhantanlinSystem.IsPlaced) return false;
			string physicalAnchorId = returnsPhysicallyToZhantanlin
				? XjShiDomainCatalog.ZhantanlinDomainId
				: payload.RebirthAnchorId;
			string physicalFallbackId = returnsPhysicallyToZhantanlin
				? XjShiDomainCatalog.ZhantanlinDomainId
				: payload.DomainId;
			int previousRank = XjShiCatalog.GetRank(payload.PreviousRealm);
			if (!returnsPhysicallyToZhantanlin
				&& string.Equals(payload.PreviousRealm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			{
				// 怜愍的真灵挂在座主/金地，不应被旧版“今释一律旃檀林锚”覆盖。
				physicalAnchorId = payload.DomainId;
				physicalFallbackId = payload.DomainId;
			}
			else if (!returnsPhysicallyToZhantanlin
				&& previousRank < XjShiCatalog.GetRank(XjShiRealmIds.LianMin)
				&& !XjZhantanlinSystem.IsPlaced)
			{
				// 僧侣/法师在逻辑旃檀林已建、实体门户尚未放置的旧档中，允许在
				// 安全世界格重塑，再由后续释修事务重新绑定，避免永久Pending。
				physicalAnchorId = string.Empty;
				physicalFallbackId = string.Empty;
			}
			if (!XjZhantanlinSystem.TryResolveRebirthTile(
				physicalAnchorId, physicalFallbackId, payload.PatronActorId, sourceId, out WorldTile tile))
			{
				return false;
			}
			string assetId = ResolveShiReturnAssetId(payload, record.RaceKey);
			if (string.IsNullOrWhiteSpace(assetId)) return false;

			Actor spawned = null;
			try
			{
				spawned = World.world.units.spawnNewUnit(assetId, tile, false, false, 0f, null, false, true);
				if (spawned?.data == null || !XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(spawned))
				{
					XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
					return false;
				}
				spawned.data.age_overgrowth = 18;
				if (!string.IsNullOrWhiteSpace(record.ActorName))
				{
					XjActorStateWriteGateway.SetDisplayName(spawned, record.ActorName, customName: true);
				}
				if (!ApplyShiReincarnationRecord(spawned, record))
				{
					XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
					return false;
				}
				long targetId = ((BaseSystemData)spawned.data).id;
				recordsByActorId[sourceId] = new XjReincarnationRecord(
					record.Found, record.ActorId, record.ActorName, record.RaceKey, record.FamilyStableId,
					record.RealmId, record.DaoTu, record.GongFaName, record.GongFaGrade, 0, 0f,
					record.JinXing, record.GuoWei, record.JinDanYiXiang, record.DeathYear,
					record.Mode, record.GuoWeiZhongAi, record.FuQiPayload,
					targetId, spawned.getName(), currentYear, StatusApplied);
				IndexAppliedReincarnation(sourceId, targetId);
				RemovePendingShiIndexes(sourceId);
				XjScheduler.RegisterActor(spawned);
				XjZhantanlinSystem.EnforceActor(spawned, currentYear);
				XjWorldArchiveSystem.MarkChanged();
				XjWorldArchiveSystem.RequestProtectedCommit();
				return true;
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjReincarnation.TickPendingShiReturn.Spawn", ex);
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(spawned);
				return false;
			}
		}
		finally
		{
			_processingShiReturns = false;
		}
	}

	private static string ResolveShiReturnAssetId(XjShiReincarnationPayload payload, string raceKey)
	{
		if (!string.IsNullOrWhiteSpace(payload?.ActorAssetId)) return payload.ActorAssetId.Trim();
		string value = raceKey ?? string.Empty;
		int separator = value.IndexOf('|');
		return separator >= 0 && separator + 1 < value.Length
			? value.Substring(separator + 1).Trim()
			: value.Trim();
	}

	internal static bool TryApplyToActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		long targetId = ((BaseSystemData)actor.data).id;
		if (targetId <= 0L
			|| XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int applied) && applied > 0)
		{
			return false;
		}

		int age = (int)Math.Floor(Math.Max(0f, actor.getAge()));
		if (age > 5)
		{
			return false;
		}

		string targetRaceKey = BuildReincarnationRaceKey(actor);
		if (string.IsNullOrWhiteSpace(targetRaceKey)
			|| !TryPickPendingRecord(targetRaceKey, out long sourceId, out XjReincarnationRecord pending))
		{
			return false;
		}

		int currentYear = XjYearTracker.CurrentYear > 0 ? XjYearTracker.CurrentYear : age;
		if (!ApplyRecordToActor(actor, pending)) return false;
		recordsByActorId[sourceId] = new XjReincarnationRecord(
			pending.Found,
			pending.ActorId,
			pending.ActorName,
			pending.RaceKey,
			pending.FamilyStableId,
			pending.RealmId,
			pending.DaoTu,
			pending.GongFaName,
			pending.GongFaGrade,
			0,
			0f,
			pending.JinXing,
			pending.GuoWei,
			pending.JinDanYiXiang,
			pending.DeathYear,
			pending.Mode,
			pending.GuoWeiZhongAi,
			pending.FuQiPayload,
			targetId,
			actor.getName(),
			currentYear,
			StatusApplied);
		IndexAppliedReincarnation(sourceId, targetId);
		if (string.Equals(pending.Mode, ModeShiReincarnation, StringComparison.Ordinal))
			RemovePendingShiIndexes(sourceId);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		if (!string.Equals(pending.Mode, ModeFuQiJinXing, StringComparison.Ordinal)
			&& !string.Equals(pending.Mode, ModeShiReincarnation, StringComparison.Ordinal))
		{
			XjThreeBookWriter.RecordJinDanReincarnation(actor, currentYear, pending.ActorName);
		}
		return true;
	}

	internal static void ExportArchiveRecords(List<XjWorldArchiveReincarnationRecord> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjReincarnationRecord record in recordsByActorId.Values)
		{
			if (!record.Found || record.ActorId <= 0L)
			{
				continue;
			}

			records.Add(new XjWorldArchiveReincarnationRecord
			{
				ActorId = record.ActorId,
				ActorName = record.ActorName,
				RaceKey = record.RaceKey,
				FamilyStableId = record.FamilyStableId,
				RealmId = record.RealmId,
				DaoTu = record.DaoTu,
				GongFaName = record.GongFaName,
				GongFaGrade = record.GongFaGrade,
				GongFaStage = 0,
				GongFaProgress = 0f,
				JinXing = record.JinXing,
				GuoWei = record.GuoWei,
				JinDanYiXiang = record.JinDanYiXiang,
				DeathYear = record.DeathYear,
				Mode = record.Mode,
				GuoWeiZhongAi = record.GuoWeiZhongAi,
				FuQiPayload = record.FuQiPayload,
				TargetActorId = record.TargetActorId,
				TargetActorName = record.TargetActorName,
				AppliedYear = record.AppliedYear,
				Status = record.Status
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjWorldArchiveReincarnationRecord> records)
	{
		recordsByActorId.Clear();
		sourceActorIdByTargetActorId.Clear();
		pendingShiActorIds.Clear();
		pendingModernShiReturnReservationActorIds.Clear();
		pendingModernMoHeReservationActorIds.Clear();
		pendingShiIdScratch.Clear();
		dueShiReturnQueue.Clear();
		queuedDueShiReturnIds.Clear();
		_processingShiReturns = false;
		if (records == null)
		{
			return;
		}

		foreach (XjWorldArchiveReincarnationRecord record in records)
		{
			if (record == null || record.ActorId <= 0L)
			{
				continue;
			}

			string importedMode = string.IsNullOrWhiteSpace(record.Mode) ? ModeJinDan : record.Mode;
			string importedStatus = string.IsNullOrWhiteSpace(record.Status) ? StatusPending : record.Status;
			XjReincarnationRecord imported = new XjReincarnationRecord(
				true,
				record.ActorId,
				record.ActorName,
				record.RaceKey,
				record.FamilyStableId,
				record.RealmId,
				record.DaoTu,
				record.GongFaName,
				record.GongFaGrade,
				0,
				0f,
				record.JinXing,
				record.GuoWei,
				record.JinDanYiXiang,
				record.DeathYear,
				importedMode,
				record.GuoWeiZhongAi,
				record.FuQiPayload,
				record.TargetActorId,
				record.TargetActorName,
				record.AppliedYear,
				importedStatus);
			recordsByActorId[record.ActorId] = imported;
			if (imported.TargetActorId > 0L) IndexAppliedReincarnation(imported.ActorId, imported.TargetActorId);
			if (IsPendingShiRecord(imported))
			{
				pendingShiActorIds.Add(record.ActorId);
				try
				{
					XjShiReincarnationPayload payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(imported.FuQiPayload);
					if (payload != null && string.Equals(payload.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
						pendingModernShiReturnReservationActorIds.Add(record.ActorId);
					if (RequiresModernMoHePositionReservation(payload))
						pendingModernMoHeReservationActorIds.Add(record.ActorId);
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("XjReincarnation.ImportShiReservation", ex);
				}
			}
		}
	}

	internal static void Clear()
	{
		recordsByActorId.Clear();
		sourceActorIdByTargetActorId.Clear();
		pendingShiActorIds.Clear();
		pendingModernShiReturnReservationActorIds.Clear();
		pendingModernMoHeReservationActorIds.Clear();
		pendingShiIdScratch.Clear();
		dueShiReturnQueue.Clear();
		queuedDueShiReturnIds.Clear();
		_processingShiReturns = false;
	}

	private static void IndexAppliedReincarnation(long sourceActorId, long targetActorId)
	{
		if (sourceActorId <= 0L || targetActorId <= 0L || sourceActorId == targetActorId) return;
		sourceActorIdByTargetActorId[targetActorId] = sourceActorId;
	}

	/// <summary>
	/// O(1) 查询某具当前肉身的直接前世。调用方可自行沿链回溯；索引由持久化转世记录重建，
	/// 因而不会因为前世 Actor 实体已被 WorldBox 回收而丢失身份连续性。
	/// </summary>
	internal static bool TryGetReincarnationSourceActorId(long targetActorId, out long sourceActorId)
	{
		sourceActorId = 0L;
		return targetActorId > 0L
			&& sourceActorIdByTargetActorId.TryGetValue(targetActorId, out sourceActorId)
			&& sourceActorId > 0L;
	}

	/// <summary>
	/// 沿target->source反向索引追到这一人物转世链最初的旧身。修士名录用最初旧身ID
	/// 作为稳定人物档案键，现世肉身只放在FocusActorId里，避免每转一世就多出一张重复卡。
	/// </summary>
	internal static bool TryResolveReincarnationRootActorId(long actorId, out long rootActorId)
	{
		rootActorId = Math.Max(0L, actorId);
		if (rootActorId <= 0L) return false;
		bool moved = false;
		HashSet<long> visited = null;
		for (int depth = 0; depth < 32; depth++)
		{
			visited ??= new HashSet<long>();
			if (!visited.Add(rootActorId)) break;
			if (!sourceActorIdByTargetActorId.TryGetValue(rootActorId, out long sourceActorId) || sourceActorId <= 0L) break;
			rootActorId = sourceActorId;
			moved = true;
		}
		return moved;
	}

	/// <summary>
	/// O(1)读取指定前世/旧身的持久化转世记录。修士名录用它把“死亡快照”接到
	/// 真正的转世链，而不是依赖DeathReason猜测。
	/// </summary>
	internal static bool TryGetRecord(long sourceActorId, out XjReincarnationRecord record)
	{
		record = default;
		return sourceActorId > 0L
			&& recordsByActorId.TryGetValue(sourceActorId, out record)
			&& record.Found;
	}

	/// <summary>
	/// 沿已应用的source->target链找到最新肉身。链长设置硬上限，既能覆盖摩诃多世
	/// 轮回，也不会因损坏存档形成环而卡死。全程只走字典索引，不扫描历史记录。
	/// </summary>
	internal static bool TryResolveLatestAppliedRecord(long sourceActorId, out XjReincarnationRecord latest)
	{
		latest = default;
		if (!TryGetRecord(sourceActorId, out XjReincarnationRecord current)) return false;
		bool foundApplied = false;
		HashSet<long> visited = null;
		for (int depth = 0; depth < 32; depth++)
		{
			if (current.TargetActorId <= 0L || !string.Equals(current.Status, StatusApplied, StringComparison.Ordinal)) break;
			latest = current;
			foundApplied = true;
			long nextSource = current.TargetActorId;
			visited ??= new HashSet<long>();
			if (!visited.Add(nextSource) || !TryGetRecord(nextSource, out current)) break;
		}
		return foundApplied;
	}

	/// <summary>
	/// 将转世链任意一世的ActorId解析到当前最新已重塑肉身。怜愍只认直属摩诃的
	/// 同一真灵，因此座主换身后必须沿这条稳定链更新挂靠，而不能继续抓着已经死亡
	/// 的旧肉身ID。全程只读转世字典，不做世界扫描。
	/// </summary>
	internal static bool TryResolveLatestLineageActorId(long actorId, out long latestActorId)
	{
		latestActorId = Math.Max(0L, actorId);
		if (latestActorId <= 0L) return false;
		if (TryResolveReincarnationRootActorId(actorId, out long rootActorId) && rootActorId > 0L)
			latestActorId = rootActorId;
		long lineageRoot = latestActorId;
		if (TryResolveLatestAppliedRecord(lineageRoot, out XjReincarnationRecord latest)
			&& latest.TargetActorId > 0L) latestActorId = latest.TargetActorId;
		return latestActorId > 0L;
	}

	internal static IReadOnlyList<XjReincarnationRecord> ReadAllEntries()
	{
		if (recordsByActorId.Count == 0)
		{
			return Array.Empty<XjReincarnationRecord>();
		}

		List<XjReincarnationRecord> entries = new List<XjReincarnationRecord>(recordsByActorId.Values);
		entries.Sort((left, right) =>
		{
			int byYear = left.DeathYear.CompareTo(right.DeathYear);
			if (byYear != 0) return byYear;
			int name = string.Compare(left.ActorName, right.ActorName, StringComparison.Ordinal);
			return name != 0 ? name : left.ActorId.CompareTo(right.ActorId);
		});
		return entries;
	}

	private static bool TryBuildPendingRecord(XjDeathSnapshot snapshot, bool forceJinDan, out XjReincarnationRecord record)
	{
		record = default;
		if (!snapshot.Found || snapshot.ActorId <= 0L)
		{
			return false;
		}

		string mode = ResolveMode(snapshot, out string guoWeiZhongAi);
		if (string.IsNullOrWhiteSpace(mode)
			&& forceJinDan
			&& XjCultivationPathRules.IsJinDanEquivalentRealm(snapshot.RealmId))
		{
			mode = ModeJinDan;
		}
		if (string.IsNullOrWhiteSpace(mode))
		{
			return false;
		}

		record = new XjReincarnationRecord(
			true,
			snapshot.ActorId,
			snapshot.Name,
			snapshot.RaceKey,
			snapshot.FamilyStableId,
			snapshot.RealmId,
			snapshot.DaoTu,
			snapshot.GongFaName,
			snapshot.GongFaGrade,
			0,
			0f,
			snapshot.JinXing,
			snapshot.GuoWei,
			snapshot.JinDanYiXiang,
			snapshot.Year,
			mode,
			guoWeiZhongAi,
			string.Empty,
			0L,
			string.Empty,
			0,
			StatusPending);
		return true;
	}

	private static string ResolveMode(XjDeathSnapshot snapshot, out string guoWeiZhongAi)
	{
		guoWeiZhongAi = snapshot.GuoWeiZhongAi ?? string.Empty;
		if (XjGuoWeiQuanBingRegistry.TryGetHistorical(snapshot.ActorId, out XjGuoWeiQuanBingState state)
			&& !string.IsNullOrWhiteSpace(state.GuoWeiZhongAi))
		{
			guoWeiZhongAi = state.GuoWeiZhongAi;
		}

		if (!string.IsNullOrWhiteSpace(guoWeiZhongAi)
			&& !string.IsNullOrWhiteSpace(snapshot.GuoWei)
			&& snapshot.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return ModeGuoWeiZhongAi;
		}

		if (!string.IsNullOrWhiteSpace(snapshot.JinXing)
			&& XjJinDanResidualJinXing.IsFamilyBorrowSource(snapshot.JinXingSource)
			&& snapshot.FamilyStableId > 0L)
		{
			return ModeFamilyBorrowJinXing;
		}

		if (string.Equals(snapshot.RealmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(snapshot.JinXing)
			&& !string.IsNullOrWhiteSpace(snapshot.JinXingSource)
			&& snapshot.JinXingSource.StartsWith("QiYuDongTian:", StringComparison.Ordinal))
		{
			return ModeZiFuJinXing;
		}

		if (XjCultivationPathRules.IsJinDanEquivalentRealm(snapshot.RealmId)
			&& XjDeterministicHash.PositiveIndex(snapshot.ActorId + snapshot.Year, "jindan_reincarnation", 10000) < GetJinDanReincarnationChanceBasis(snapshot.JinDanYiXiang))
		{
			return ModeJinDan;
		}

		return string.Empty;
	}

	private static int GetJinDanReincarnationChanceBasis(int jinDanYiXiang)
	{
		if (jinDanYiXiang >= 6000)
		{
			return JinDanPeakReincarnationChanceBasis;
		}

		if (jinDanYiXiang >= 3000)
		{
			return JinDanLateReincarnationChanceBasis;
		}

		if (jinDanYiXiang >= 1000)
		{
			return JinDanMiddleReincarnationChanceBasis;
		}

		return JinDanEarlyReincarnationChanceBasis;
	}

	private static bool TryPickPendingRecord(string targetRaceKey, out long sourceId, out XjReincarnationRecord record)
	{
		sourceId = 0L;
		record = default;
		if (string.IsNullOrWhiteSpace(targetRaceKey))
		{
			return false;
		}

		bool found = false;
		foreach (KeyValuePair<long, XjReincarnationRecord> pair in recordsByActorId)
		{
			XjReincarnationRecord candidate = pair.Value;
			if (!candidate.Found
				|| string.Equals(candidate.Mode, ModeShiReincarnation, StringComparison.Ordinal)
				|| candidate.TargetActorId > 0L
				|| !string.Equals(candidate.Status, StatusPending, StringComparison.Ordinal)
				|| !string.Equals(candidate.RaceKey, targetRaceKey, StringComparison.Ordinal))
			{
				continue;
			}

			if (!found || IsBetterPendingRecord(candidate, record))
			{
				found = true;
				sourceId = pair.Key;
				record = candidate;
			}
		}

		return found;
	}

	private static bool IsBetterPendingRecord(XjReincarnationRecord candidate, XjReincarnationRecord current)
	{
		int candidatePriority = GetPendingModePriority(candidate.Mode);
		int currentPriority = GetPendingModePriority(current.Mode);
		if (candidatePriority != currentPriority)
		{
			return candidatePriority > currentPriority;
		}

		int byDeathYear = candidate.DeathYear.CompareTo(current.DeathYear);
		if (byDeathYear != 0)
		{
			return byDeathYear < 0;
		}

		return candidate.ActorId < current.ActorId;
	}

	private static int GetPendingModePriority(string mode)
	{
		if (string.Equals(mode, ModeGuoWeiZhongAi, StringComparison.Ordinal)) return 3;
		if (string.Equals(mode, ModeJinDan, StringComparison.Ordinal)) return 2;
		if (string.Equals(mode, ModeFamilyBorrowJinXing, StringComparison.Ordinal)) return 4;
		if (string.Equals(mode, ModeZiFuJinXing, StringComparison.Ordinal)) return 1;
		if (string.Equals(mode, ModeFuQiJinXing, StringComparison.Ordinal)) return 5;
		if (string.Equals(mode, ModeShiReincarnation, StringComparison.Ordinal)) return 6;
		return 0;
	}

	private static string BuildReincarnationRaceKey(Actor actor)
	{
		if (actor?.asset == null)
		{
			return string.Empty;
		}

		string assetId = ((Asset)actor.asset).id ?? string.Empty;
		if (string.IsNullOrWhiteSpace(assetId))
		{
			return string.Empty;
		}

		string race = TryGetAssetRaceName(actor);
		if (string.IsNullOrWhiteSpace(race))
		{
			if (assetId.StartsWith("civ_", StringComparison.OrdinalIgnoreCase) && assetId.Length > 4)
			{
				race = assetId.Substring(4);
			}
			else
			{
				int separator = assetId.IndexOf('_');
				race = separator > 0 ? assetId.Substring(0, separator) : assetId;
			}
		}

		return race + "|" + assetId;
	}

	private static string TryGetAssetRaceName(Actor actor)
	{
		return XjNativeActorAssetInterop.ReadRaceName(actor);
	}

	private static bool ApplyRecordToActor(Actor actor, XjReincarnationRecord record)
	{
		if (actor?.data == null) return false;
		if (string.Equals(record.Mode, ModeShiReincarnation, StringComparison.Ordinal))
		{
			return ApplyShiReincarnationRecord(actor, record);
		}
		if (string.Equals(record.Mode, ModeFuQiJinXing, StringComparison.Ordinal))
		{
			return ApplyFuQiJinXingRecord(actor, record);
		}
		if (!XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits: false))
		{
			return false;
		}
		bool familyBorrowJinXing = string.Equals(record.Mode, ModeFamilyBorrowJinXing, StringComparison.Ordinal);
		bool ziFuJinXing = familyBorrowJinXing
			|| string.Equals(record.Mode, ModeZiFuJinXing, StringComparison.Ordinal);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationApplied, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceActorId, record.ActorId.ToString(CultureInfo.InvariantCulture));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceName, record.ActorName);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationSavedYear, record.DeathYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationMode, record.Mode);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationDaoTu, record.DaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationGuoWeiZhongAi, record.GuoWeiZhongAi);

		if (!ziFuJinXing && !string.IsNullOrWhiteSpace(record.DaoTu))
		{
			XjCultivationStateTransitions.TrySetDaoTu(actor, record.DaoTu, false);
		}

		if (!ziFuJinXing && !string.IsNullOrWhiteSpace(record.GongFaName) && XjGongFaDefinition.IsValidGrade(record.GongFaGrade))
		{
			XjGongFaAccessor.WriteState(actor, new XjGongFaState(
				true,
				record.GongFaName,
				record.GongFaGrade,
				0,
				0f,
				record.DaoTu,
				record.GongFaGrade > XjGongFaAccessor.MaxActiveGrade,
				"Reincarnation"));
			XjGongFaAccessor.WriteSource(actor, "转世承继");
		}

		// 前世小境界进度保留在转世记录/待归位 Registry 中，不写入新肉身的
		// active XjJinDanYiXiang 兼容投影。新身重新证回高境后再由高境事务恢复。

		if (!ziFuJinXing && !string.IsNullOrWhiteSpace(record.GuoWeiZhongAi))
		{
			XjJinDanAccessor.ClearPendingReincarnationActiveProjection(actor);
			XjGuoWeiQuanBingActorSnapshot.Clear(actor);
			int currentYear = XjYearTracker.CurrentYear > 0 ? XjYearTracker.CurrentYear : record.DeathYear;
			XjGuoWeiQuanBingLifecycle.RecordReincarnatedZhengWeiHeir(
				actor,
				record.DaoTu,
				record.GuoWei,
				record.GuoWeiZhongAi,
				record.JinDanYiXiang,
				currentYear,
				record.ActorName);
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.ChuShenSpecial, ziFuJinXing ? 6 : 7);
		float mingShu = BuildReincarnationMingShu(actor, ziFuJinXing);
		XjMingShuState.Set(actor, mingShu, 0f);

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int existingXjZz) || existingXjZz <= 0)
		{
			long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
			int minimum = ziFuJinXing ? 4 : 5;
			int xjZz = minimum + XjDeterministicHash.PositiveIndex(actorId + record.ActorId, "reincarnation_high_aptitude", 2);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, xjZz);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
			XjAptitudeEffectRules.ApplyOnAgeFiveResult(actor, new XjAptitudeRollResult(true, xjZz, 0, "Reincarnation"));
		}

		long reincarnatedActorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int reincarnatedXjZz);
		if (!XjDaoHuiPolicy.TryGetAptitudeRange(reincarnatedXjZz, out int daoHuiMin, out int daoHuiMax))
		{
			daoHuiMin = ziFuJinXing ? 58 : 75;
			daoHuiMax = ziFuJinXing ? 75 : 90;
		}
		float daoHui = XjDaoHuiPolicy.Clamp(XjDeterministicHash.BuildSeedInteger(
			reincarnatedActorId + record.ActorId,
			actor?.getName(),
			63,
			daoHuiMin,
			daoHuiMax + 1));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, daoHui);

		if (!ziFuJinXing)
		{
			TryGrantReincarnationDaoTuQi(actor, record.DaoTu, currentYear: XjYearTracker.CurrentYear);
		}
		if (familyBorrowJinXing && record.FamilyStableId > 0L)
		{
			XjFamilyMemberIndex.Shared.RelinkActorToFamily(actor, record.FamilyStableId);
			XjWorldArchiveSystem.MarkChanged();
			XjWorldArchiveSystem.RequestProtectedCommit();
		}
		XjAutoCollectSystem.TryCollectReincarnation(actor, record.Mode);
		string title = ziFuJinXing ? "真人转世" : "真君转世";
		string message = actor.getName() + "承" + record.ActorName + "前尘而来，得" + title + "之身。";
		XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanUpgrade);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			message, XjAnnouncementCategory.HighRealm, duration: 8f, color: "#9CD7FF", delayFrames: 1, iconId: XjEventIconCatalog.JinDanUpgrade);
		return true;
	}


	private static bool ApplyShiReincarnationRecord(Actor actor, XjReincarnationRecord record)
	{
		XjShiReincarnationPayload payload;
		try
		{
			payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(record.FuQiPayload);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjReincarnation.ApplyShiReincarnationRecord", ex);
			payload = null;
		}
		if (payload == null || actor?.data == null) return false;
		// 转世/真灵归返不再与普通新入释争用同一硬上限。今释已有真灵可以
		// 使用专属回归弹性，避免旧身已死却因基础容量被后来者填满而长期Pending。
		// 普通新增入口仍只看基础容量，因此这段弹性不会演化成常驻扩人口通道。
		if (!XjCultivationPathRules.IsShi(actor) && !XjShiEntrySystem.HasReincarnationReturnCapacity(payload.Tradition)) return false;
		// 普通旧式转世仍只接受摩诃及以上；真灵归返则允许今释怜愍按原位重塑。
		if (!payload.IsTrueSpiritReturn
			&& XjShiCatalog.GetRank(payload.PreviousRealm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
			return true;

		int currentYear = XjYearTracker.CurrentYear > 0
			? XjYearTracker.CurrentYear
			: Math.Max(1, record.DeathYear);
		// 摩诃转世在释修语义上仍是同一人物。先写身份标记，再进入恢复事务，
		// 避免新肉身刚生成时被家族姓氏统一逻辑改写前世姓名。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationApplied, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceActorId,
			record.ActorId.ToString(CultureInfo.InvariantCulture));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceName, record.ActorName);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationSavedYear, record.DeathYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationMode, ModeShiReincarnation);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationDaoTu, string.Empty);

		if (!XjShiState.ApplyReincarnation(actor, payload, record.ActorId, record.ActorName, currentYear))
		{
			return false;
		}

		// 世界层必须视为同一人物：恢复事务完成后再次锁定前身完整显示名，
		// 防止出生姓氏、家族重连或标题同步在同帧把新肉身改成另一人物。
		if (!string.IsNullOrWhiteSpace(record.ActorName))
		{
			XjActorStateWriteGateway.SetDisplayName(actor, record.ActorName, customName: true);
		}

		// 释修只看命数，不因摩诃转世强行抬升xjzz。命数、修持、法脉、尊号等
		// 已由ApplyReincarnation按前世载荷恢复，此处不再二次覆盖。摩诃是同一人物
		// 换身，不应因新肉身的原生出生关系被挂进另一家族；有旧家族记录时直接续接。
		if (record.FamilyStableId > 0L)
		{
			XjFamilyMemberIndex.Shared.RelinkActorToFamily(actor, record.FamilyStableId);
		}

		// 自动收藏只属于摩诃主动转世。真灵归返是死亡后的肉身重塑，
		// 即使死亡前为摩诃，也不能借用“转世收藏”入口。
		if (!payload.IsTrueSpiritReturn
			&& string.Equals(payload.PreviousRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			XjAutoCollectSystem.TryCollectReincarnation(actor, ModeShiReincarnation);
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out int currentLife);
		string shiReturnMessage = payload.IsTrueSpiritReturn
			? actor.getName() + "真灵归返所挂靠金地，于释土重塑肉身。"
			: actor.getName() + "已入第" + Math.Max(1, currentLife).ToString(CultureInfo.InvariantCulture)
				+ "世；同一真灵续身，姓名、摩诃境界、尊号、法号与法脉承前身不改。";
		XjWorldHistoryRegistry.AddActorEvent(actor, shiReturnMessage, XjEventIconCatalog.ZiFuUpgrade);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			shiReturnMessage, XjAnnouncementCategory.Shi, duration: 8f, color: "#D2B5FF", delayFrames: 1, iconId: XjEventIconCatalog.ZiFuUpgrade);
		return true;
	}

	private static bool ApplyFuQiJinXingRecord(Actor actor, XjReincarnationRecord record)
	{
		XjFuQiReincarnationPayload payload;
		try { payload = JsonConvert.DeserializeObject<XjFuQiReincarnationPayload>(record.FuQiPayload); }
		catch (System.Exception xjCaught775_3) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjReincarnation.cs:775", xjCaught775_3);
			 payload = null; }
		if (payload == null) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int currentYear = XjYearTracker.CurrentYear > 0 ? XjYearTracker.CurrentYear : Math.Max(1, record.DeathYear);
		string lineageId = payload.FuQiLineageId ?? string.Empty;
		bool swordLineage = string.Equals(lineageId, XjFuQiLineageIds.Sword, StringComparison.Ordinal);
		bool namedDaoTuValid = !string.IsNullOrWhiteSpace(record.DaoTu)
			&& XjDaoTuCatalog.TryResolve(record.DaoTu, out XjDaoTuDefinition inheritedDaoTu)
			&& inheritedDaoTu.SupportsFuQi
			&& XjFuQiCoreCatalog.TryGetByRootId(inheritedDaoTu.RootId, out XjFuQiCoreDefinition inheritedCore)
			&& inheritedCore.GameplayImplemented;
		if (!namedDaoTuValid && !swordLineage) return false;

		// 金性转世保底五品，但不再必定生成六品天赋：四成六品、六成五品。
		// 若新生角色已被年龄逻辑预写为紫金道，先清除尚未展开的初始路径，
		// 再按前世道途恢复服气资格，避免转世持续吞并世界最高天赋池。
		int reincarnationAptitude = XjFuQiBalancePolicy.ResolveReincarnationAptitude(
			actorId, record.ActorId, currentYear, lineageId);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzEffectApplied, out int priorAptitudeEffectApplied);
		XjCultivationPathTransitions.ClearAll(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, reincarnationAptitude);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, reincarnationAptitude);
		if (priorAptitudeEffectApplied <= 0)
		{
			XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, reincarnationAptitude);
		}
		else
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzEffectApplied, 1);
		}

		bool pathSet = false;
		if (!string.IsNullOrWhiteSpace(record.DaoTu))
		{
			pathSet = XjCultivationPathTransitions.TrySetInitialPath(
				actor, XjCultivationPathIds.FuQiYangXing, record.DaoTu, lineageId, syncVisibleTraits: false);
		}
		if (!pathSet && string.Equals(lineageId, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
		{
			pathSet = XjCultivationPathTransitions.TrySetInitialPath(
				actor, XjCultivationPathIds.FuQiYangXing, lineageId, syncVisibleTraits: false);
		}
		if (!pathSet) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationApplied, 1);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceActorId, record.ActorId.ToString(CultureInfo.InvariantCulture));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationSourceName, record.ActorName);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjReincarnationSavedYear, record.DeathYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationMode, ModeFuQiJinXing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjReincarnationDaoTu, record.DaoTu);

		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiLineageId, lineageId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiDaoTuRootId, payload.FuQiDaoTuRootId ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreType, payload.FuQiCoreType ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiCoreId, payload.FuQiCoreId ?? string.Empty);
		// 转世继承道统、金性与宿慧，不直接继承真人修为。所有境界项目、
		// 本命核心进度和剑气从零重修，避免幼年角色跳过黄冠、真人阶段。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiShenMiaoId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiStudiedIntentIds, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiYangQingMingCompletedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5HighRealmRouteChecked, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiRank5StayFuQi, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiTrueSpiritInitialized, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiTrueSpirit, Math.Clamp(payload.TrueSpiritRemaining, 0, 3));
		XjActorAccessor.SetInt(
			actor,
			XjActorDataKeys.FuQiReincarnationBreakthroughBonusPercent,
			Math.Clamp(payload.BreakthroughBonusPercent, 0, XjFuQiBalancePolicy.MaxReincarnationBreakthroughBonusPercent));
		XjActorAccessor.SetInt(
			actor,
			XjActorDataKeys.FuQiJinDanFailureCount,
			Math.Max(0, payload.CumulativeFailureCount));
		XjActorAccessor.SetString(actor, XjActorDataKeys.FuQiInheritedJinXing, payload.InheritedJinXing ?? record.JinXing ?? string.Empty);

		if (!string.IsNullOrWhiteSpace(record.GongFaName) && XjGongFaDefinition.IsValidGrade(record.GongFaGrade))
		{
			XjGongFaAccessor.WriteState(actor, new XjGongFaState(
				true, record.GongFaName, record.GongFaGrade, 0, 0f, record.DaoTu,
				record.GongFaGrade > XjGongFaAccessor.MaxActiveGrade, "FuQiJinXingReincarnation"));
			XjGongFaAccessor.WriteSource(actor, "金性转世承继");
		}
		if (!string.IsNullOrWhiteSpace(payload.GongFaCollectionJson))
		{
			XjActorGongFaCollection.TryRestoreSerialized(
				actor,
				Math.Max(1, payload.GongFaCollectionVersion),
				payload.GongFaCollectionJson,
				"FuQiJinXingReincarnation");
		}

		if (XjCraftTraitRules.IsCraftTraitId(payload.CraftTraitId))
		{
			// 前世百艺是定向继承，优先级高于新生角色在年龄链中随机取得的百艺。
			// 先移除可能冲突的当前主百艺，再写入前世技艺并执行独占归一。
			string currentCraftTrait = XjCraftTraitRules.GetPrimaryTraitId(actor);
			if (!string.IsNullOrWhiteSpace(currentCraftTrait)
				&& !string.Equals(currentCraftTrait, payload.CraftTraitId, StringComparison.Ordinal))
			{
				actor.removeTrait(currentCraftTrait);
			}
			if (!actor.hasTrait(payload.CraftTraitId)) actor.addTrait(payload.CraftTraitId, false);
			XjCraftTraitRules.NormalizeExclusive(actor, payload.CraftTraitId);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftActivatedYear, currentYear);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftAlchemyRank, Math.Max(0, payload.CraftAlchemyRank));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjCraftRankSchema, Math.Max(0, payload.CraftRankSchema));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactRefinerRank, Math.Max(0, payload.ArtifactRefinerRank));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactTrainingSuccess, Math.Max(0, payload.ArtifactTrainingSuccess));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactFaQiSuccess, Math.Max(0, payload.ArtifactFaQiSuccess));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactLingBaoSuccess, Math.Max(0, payload.ArtifactLingBaoSuccess));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjArtifactFaBaoSuccess, Math.Max(0, payload.ArtifactFaBaoSuccess));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjTalismanProficiency, Math.Max(0, payload.TalismanProficiency));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjTalismanRank, Math.Max(0, payload.TalismanRank));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFormationProficiency, Math.Max(0, payload.FormationProficiency));
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFormationRank, Math.Max(0, payload.FormationRank));
			if (string.Equals(payload.CraftTraitId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal))
			{
				XjAlchemyRuntimeRegistry.RestoreReincarnatedCrafter(
					actorId, payload.AlchemyTotalProficiency, payload.AlchemySuccessCount,
					payload.AlchemyFailureCount, payload.AlchemyMajorAccidentCount, payload.AlchemyLastYear);
			}
		}

		int huiGuangRaise = 6 + XjDeterministicHash.PositiveIndex(actorId + record.ActorId, "fuqi_reincarnation_huiguang", 7);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float currentHuiGuang);
		float inheritedHuiGuang = XjDaoHuiPolicy.Clamp(Math.Max(currentHuiGuang, payload.SourceHuiGuang + huiGuangRaise));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, inheritedHuiGuang);

		float mingShu = BuildReincarnationMingShu(actor, false);
		XjMingShuState.Set(actor, mingShu, 0f);
		actor.addTrait("XjJinXingReincarnation", false);
		if (record.FamilyStableId > 0L)
		{
			XjFamilyMemberIndex.Shared.RelinkActorToFamily(actor, record.FamilyStableId);
		}
		XjAutoCollectSystem.TryCollectReincarnation(actor, ModeFuQiJinXing);
		string message = actor.getName() + "承" + record.ActorName + "前世金性与真灵转生，仍循"
			+ (string.IsNullOrWhiteSpace(record.DaoTu) ? "服气养性" : record.DaoTu) + "道途。";
		XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanUpgrade);
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			message, XjAnnouncementCategory.HighRealm, duration: 8f, color: "#9CD7FF", delayFrames: 1, iconId: XjEventIconCatalog.JinDanUpgrade);
		return true;
	}

	private static void TryGrantReincarnationDaoTuQi(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu)
			|| !XjCaiQiCatalog.TryGetOldResourceIdByDaoTuName(daoTu, out string resourceId))
		{
			return;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || XjQianKunDaiRegistry.TryGetItemCount(actorId, resourceId, XjQianKunDaiRegistry.CategoryCaiQi, out int count) && count > 0)
		{
			return;
		}
		string displayName = XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string resolved) ? resolved : daoTu + "气";
		XjQianKunDaiRegistry.TryAddItemCount(actorId, actor.getName(), resourceId, displayName,
			XjQianKunDaiRegistry.CategoryCaiQi, "真君转世", daoTu, 1, Math.Max(0, currentYear));
	}

	private static int ReadInt(Actor actor, string key)
	{
		return XjActorAccessor.TryGetInt(actor, key, out int value) ? Math.Max(0, value) : 0;
	}

	private static string ReadString(Actor actor, string key)
	{
		return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
	}

	private static float BuildReincarnationMingShu(Actor actor, bool ziFuJinXing)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int min = ziFuJinXing ? 40 : 140;
		int max = ziFuJinXing ? 81 : 240;
		return XjDeterministicHash.BuildSeedInteger(actorId, actor?.getName(), ziFuJinXing ? 61 : 62, min, max);
	}
}
