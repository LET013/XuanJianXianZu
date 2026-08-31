using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 故尊命痕与重现。系统只读取真君级跨修法缓存，五年一次检查55条道途，
/// 不扫描全世界单位。连续一千年无金丹/真君后，天地才可能令一位受眷故尊重现一次；
/// 重现者只是旧世命痕，不再进入当世修士索引，也绝不会二次复现。
/// </summary>
internal static class XjGuZunRegistry
{
	private const int ReappearanceAbsenceYears = 1000;
	private const int CheckIntervalYears = 5;
	private const int GlobalReappearanceIntervalYears = 100;
	private const int MaxEligiblePerDaoTu = 3;
	private const string ArchiveIdKey = "xuanjian.vnext.guzun.archive_id";
	private const string SourceActorIdKey = "xuanjian.vnext.guzun.source_actor_id";

	private static readonly Dictionary<string, XjGuZunArchiveRecord> ByArchiveId = new Dictionary<string, XjGuZunArchiveRecord>(StringComparer.Ordinal);
	private static readonly Dictionary<long, string> ArchiveIdByCurrentActor = new Dictionary<long, string>();
	private static readonly Dictionary<string, XjDaoTuHighRealmContinuityRecord> Continuity = new Dictionary<string, XjDaoTuHighRealmContinuityRecord>(StringComparer.Ordinal);
	private static readonly HashSet<string> PendingDeathArchiveIds = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, string> PendingDeathDaoTuByArchiveId = new Dictionary<string, string>(StringComparer.Ordinal);
	private static readonly HashSet<string> PresentDaoTuScratch = new HashSet<string>(StringComparer.Ordinal);
	private static int _lastCheckedYear = -1;
	private static int _lastGlobalReappearanceYear;


	internal static void ObserveHighRealm(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || IsShenDan(actor) || IsManifestationActor(actor)) return;
		if (!IsEligibleHighRealm(actor, out string realmId, out string daoTu, out string path)) return;
		ObserveLivingHighRealm(actor, currentYear, realmId, daoTu, path, captureProgressIncrease: false);
		XjDaoTuHighRealmContinuityRecord continuity = GetOrCreateContinuity(daoTu, currentYear);
		if (continuity.LastHighRealmPresentYear != currentYear)
		{
			continuity.LastHighRealmPresentYear = currentYear;
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	internal static string PrepareBeforeDeath(Actor actor, XjDeathCause cause)
	{
		if (actor?.data == null || cause == XjDeathCause.TechnicalRemoval || IsShenDan(actor)) return string.Empty;
		// 命痕显化死亡只回收原故尊档案，不再重新观察/生成一条新的故尊候选。
		if (IsManifestationActor(actor))
		{
			return XjActorAccessor.TryGetString(actor, ArchiveIdKey, out string manifestedArchiveId)
				? manifestedArchiveId?.Trim() ?? string.Empty
				: string.Empty;
		}
		if (!IsEligibleHighRealm(actor, out string realmId, out string daoTu, out string path)) return string.Empty;

		int year = CurrentYear;
		XjGuZunArchiveRecord record = ObserveLivingHighRealm(actor, year, realmId, daoTu, path, captureProgressIncrease: true);
		if (record == null) return string.Empty;

		record.DeathAge = Math.Max(0, Mathf.RoundToInt(actor.getAge()));
		record.DeathCause = cause.ToString();
		TryCaptureTile(actor, out int x, out int y);
		record.DeathTileX = x;
		record.DeathTileY = y;
		// 在真正死亡及果位释放前冻结天地眷爱，避免死亡后清理顺序让
		// 果位钟爱、主果和命数信息已经不可读取。FinalizeDeath 再做一次幂等补算。
		EvaluateHeavenFavor(record, actor);
		PendingDeathArchiveIds.Add(record.ArchiveId);
		PendingDeathDaoTuByArchiveId[record.ArchiveId] = daoTu;
		return record.ArchiveId;
	}

	internal static void FinalizeDeath(Actor actor, string archiveId, XjDeathCause cause)
	{
		if (string.IsNullOrWhiteSpace(archiveId) || !ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record)) return;
		PendingDeathArchiveIds.Remove(archiveId);
		PendingDeathDaoTuByArchiveId.TryGetValue(archiveId, out string deathDaoTu);
		PendingDeathDaoTuByArchiveId.Remove(archiveId);
		if (actor != null && XjSafeCore.IsAliveActor(actor)) return;

		long actorId = record.CurrentActorId;
		if (actorId > 0L) ArchiveIdByCurrentActor.Remove(actorId);
		record.CurrentActorId = 0L;
		record.IsCurrentlyManifested = false;
		record.DeathYear = CurrentYear;
		record.DeathCause = cause.ToString();
		string continuityDaoTu = string.IsNullOrWhiteSpace(deathDaoTu) ? record.DaoTu : deathDaoTu.Trim();
		if (!string.IsNullOrWhiteSpace(continuityDaoTu))
		{
			GetOrCreateContinuity(continuityDaoTu, record.DeathYear).LastHighRealmPresentYear = record.DeathYear;
		}
		EvaluateHeavenFavor(record, actor);
		TrimEligibleCandidates(record.DaoTu);
		if (!record.HeavenFavored) RemoveArchiveRecord(record.ArchiveId);
		else XjSemanticDiagnostics.RecordGuZun("archive", record.DaoTu);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void CancelPendingDeath(string archiveId)
	{
		if (string.IsNullOrWhiteSpace(archiveId)) return;
		PendingDeathArchiveIds.Remove(archiveId);
		PendingDeathDaoTuByArchiveId.Remove(archiveId);
	}

	internal static void TickDecennial(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0 || currentYear % CheckIntervalYears != 0 || _lastCheckedYear >= currentYear) return;
		_lastCheckedYear = currentYear;
		ResolveManifestedSelfDissolutions(currentYear);

		PresentDaoTuScratch.Clear();
		HashSet<string> presentDaoTu = PresentDaoTuScratch;
		IReadOnlyList<long> highRealmIds = XjCultivatorCache.GetZhenJunOrHigherIds();
		for (int i = 0; i < highRealmIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(highRealmIds[i], out Actor actor)
				|| actor?.data == null
				|| IsShenDan(actor)
				|| !IsEligibleHighRealm(actor, out string realmId, out string daoTu, out string path))
			{
				continue;
			}

			presentDaoTu.Add(daoTu);
			ObserveLivingHighRealm(actor, currentYear, realmId, daoTu, path, captureProgressIncrease: false);
			XjDaoTuHighRealmContinuityRecord continuity = GetOrCreateContinuity(daoTu, currentYear);
			continuity.LastHighRealmPresentYear = currentYear;
		}

		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = XjDaoTuVisibleTraitCatalog.Entries;
		for (int i = 0; i < entries.Count; i++)
		{
			string daoTu = entries[i].DisplayName;
			if (string.IsNullOrWhiteSpace(daoTu) || presentDaoTu.Contains(daoTu)) continue;
			GetOrCreateContinuity(daoTu, currentYear);
		}

		if (_lastGlobalReappearanceYear > 0 && currentYear - _lastGlobalReappearanceYear < GlobalReappearanceIntervalYears) return;
		TryReappearOne(currentYear, presentDaoTu);
	}

	internal static bool TryGetManifestationSummary(Actor actor, out string summary)
	{
		summary = string.Empty;
		if (actor?.data == null) return false;
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !ArchiveIdByCurrentActor.TryGetValue(actorId, out string archiveId)
			|| !ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record)
			|| record.ReappearanceCount <= 0)
		{
			return false;
		}

		summary = "故尊命痕仅此一现；旧身陨落于" + XjChronology.FormatYear(record.DeathYear);
		return true;
	}

	internal static IReadOnlyList<XjGuZunArchiveRecord> ReadAll()
	{
		List<XjGuZunArchiveRecord> list = new List<XjGuZunArchiveRecord>(ByArchiveId.Count);
		foreach (XjGuZunArchiveRecord record in ByArchiveId.Values) list.Add(Clone(record));
		list.Sort((a, b) => string.CompareOrdinal(a.ArchiveId, b.ArchiveId));
		return list;
	}

	internal static void ExportArchiveRecords(List<XjGuZunArchiveRecord> records, List<XjDaoTuHighRealmContinuityRecord> continuity)
	{
		records?.Clear();
		continuity?.Clear();
		if (records != null)
		{
			foreach (XjGuZunArchiveRecord record in ByArchiveId.Values)
			{
				if (record.IsCurrentlyManifested || record.HeavenFavored) records.Add(Clone(record));
			}
			records.Sort((a, b) => string.CompareOrdinal(a.ArchiveId, b.ArchiveId));
		}
		if (continuity != null)
		{
			foreach (XjDaoTuHighRealmContinuityRecord record in Continuity.Values) continuity.Add(Clone(record));
			continuity.Sort((a, b) => string.CompareOrdinal(a.DaoTu, b.DaoTu));
		}
	}

	internal static void ImportArchiveRecords(
		IReadOnlyList<XjGuZunArchiveRecord> records,
		IReadOnlyList<XjDaoTuHighRealmContinuityRecord> continuity,
		int lastGlobalReappearanceYear)
	{
		Clear();
		_lastGlobalReappearanceYear = Math.Max(0, lastGlobalReappearanceYear);
		if (records != null)
		{
			for (int i = 0; i < records.Count; i++)
			{
				XjGuZunArchiveRecord source = records[i];
				if (source == null || string.IsNullOrWhiteSpace(source.ArchiveId)
					|| source.DeathYear > 0 && !source.HeavenFavored && !source.IsCurrentlyManifested) continue;
				XjGuZunArchiveRecord clone = Clone(source);
				// 旧档候选也必须重新按“果位金丹/果位真君羽士”校验；
				// 余位、闰位、结璘仙与神丹记录在读档时直接淘汰。
				if (!TryNormalizeEligibleArchiveRecord(clone)) continue;
				// CurrentActorId 是运行态绑定，不属于不可逆历史。只有当本次读档
				// 已经能解析到同一存活角色时才保留；否则等待高境缓存重建后由
				// ObserveHighRealm 依据角色身上的 archive_id 重新绑定，防止幽灵故尊。
				if (!clone.IsCurrentlyManifested
					|| clone.CurrentActorId <= 0L
					|| !XjScheduler.ResolveActor(clone.CurrentActorId, out Actor liveActor)
					|| !XjSafeCore.IsAliveActor(liveActor))
				{
					clone.IsCurrentlyManifested = false;
					clone.CurrentActorId = 0L;
				}
				ByArchiveId[clone.ArchiveId] = clone;
				if (clone.IsCurrentlyManifested)
				{
					ArchiveIdByCurrentActor[clone.CurrentActorId] = clone.ArchiveId;
					if (XjScheduler.ResolveActor(clone.CurrentActorId, out Actor manifestedActor) && manifestedActor?.data != null)
					{
						XjActorAccessor.SetInt(manifestedActor, XjActorDataKeys.GuZunManifestation, 1);
						XjCultivatorCache.Remove(clone.CurrentActorId);
						XjJinDanImmortalityRegistry.RemoveForRealmLoss(manifestedActor);
						XjGuoWeiRegistry.RemoveEphemeralClaims(clone.CurrentActorId);
						XjGuoWeiQuanBingRegistry.RemoveActor(clone.CurrentActorId);
					}
				}

			}
		}
		if (continuity != null)
		{
			for (int i = 0; i < continuity.Count; i++)
			{
				XjDaoTuHighRealmContinuityRecord source = continuity[i];
				if (source == null || string.IsNullOrWhiteSpace(source.DaoTu)) continue;
				Continuity[source.DaoTu.Trim()] = Clone(source);
			}
		}
		// 旧档若曾复现过故尊却缺失/损坏连续性字段，以故尊档案本身回填。
		// 一条道途只允许天地复现一位故尊；李言真之后不会再轮到谭思齐等第二位。
		foreach (XjGuZunArchiveRecord record in ByArchiveId.Values)
		{
			if (record == null || record.ReappearanceCount <= 0 || string.IsNullOrWhiteSpace(record.DaoTu)) continue;
			XjDaoTuHighRealmContinuityRecord daoContinuity = GetOrCreateContinuity(record.DaoTu, Math.Max(1, record.LastReappearanceYear));
			daoContinuity.LastReappearanceYear = Math.Max(daoContinuity.LastReappearanceYear, Math.Max(1, record.LastReappearanceYear));
		}
		PurgeLegacyImmortalityPollution();
	}

	internal static int ExportLastGlobalReappearanceYear() => _lastGlobalReappearanceYear;

	internal static void Clear()
	{
		ByArchiveId.Clear();
		ArchiveIdByCurrentActor.Clear();
		Continuity.Clear();
		PendingDeathArchiveIds.Clear();
		PendingDeathDaoTuByArchiveId.Clear();
		_lastCheckedYear = -1;
		_lastGlobalReappearanceYear = 0;
	}

	private static XjGuZunArchiveRecord ObserveLivingHighRealm(
		Actor actor,
		int currentYear,
		string realmId,
		string daoTu,
		string path,
		bool captureProgressIncrease)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return null;
		string archiveId = ResolveArchiveId(actor, actorId);
		bool dirty = false;
		if (!ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record))
		{
			record = new XjGuZunArchiveRecord
			{
				ArchiveId = archiveId,
				SourceActorId = actorId,
				FirstObservedYear = currentYear
			};
			ByArchiveId.Add(archiveId, record);
			dirty = true;
		}

		string actorName = SafeName(actor);
		string assetId = XjDengMingShiManager.ResolveActorAssetIdForArchive(actor);
		if (record.CurrentActorId != actorId) { record.CurrentActorId = actorId; dirty = true; }
		if (!string.Equals(record.ActorName, actorName, StringComparison.Ordinal)) { record.ActorName = actorName; dirty = true; }
		if (!string.Equals(record.AssetId, assetId, StringComparison.Ordinal)) { record.AssetId = assetId; dirty = true; }
		if (!record.IsCurrentlyManifested) { record.IsCurrentlyManifested = true; dirty = true; }
		if (CaptureOriginalIdentity(actor, record)) dirty = true;
		ArchiveIdByCurrentActor[actorId] = archiveId;
		if (!XjActorAccessor.TryGetString(actor, ArchiveIdKey, out string boundArchiveId)
			|| !string.Equals(boundArchiveId, archiveId, StringComparison.Ordinal))
			XjActorAccessor.SetString(actor, ArchiveIdKey, archiveId);
		string sourceActorText = record.SourceActorId.ToString();
		if (!XjActorAccessor.TryGetString(actor, SourceActorIdKey, out string boundSourceActorId)
			|| !string.Equals(boundSourceActorId, sourceActorText, StringComparison.Ordinal))
			XjActorAccessor.SetString(actor, SourceActorIdKey, sourceActorText);

		XjJinDanState currentHighRealmState = XjJinDanAccessor.BuildState(actor);
		string currentGuoWei = currentHighRealmState.GuoWei ?? string.Empty;
		if (!string.Equals(record.HighestGuoWei, currentGuoWei, StringComparison.Ordinal))
		{
			record.HighestGuoWei = currentGuoWei;
			dirty = true;
		}
		if (!record.EligibleZhengWei)
		{
			record.EligibleZhengWei = true;
			dirty = true;
		}
		if (record.SchemaVersion < 3)
		{
			record.SchemaVersion = 3;
			dirty = true;
		}

		int realmOrder = XjRealmHelper.GetOrder(realmId);
		int yiXiang = ReadYiXiang(actor);
		int minorStage = ResolveMinorStage(yiXiang);
		int progress = ResolveProgress(actor, path);
		bool identityChanged = !string.IsNullOrWhiteSpace(record.HighestSnapshotJson)
			&& (!string.Equals(record.DaoTu, daoTu, StringComparison.Ordinal)
				|| !string.Equals(record.CultivationPath, path, StringComparison.Ordinal));
		bool isHigher = identityChanged
			|| realmOrder > record.HighestRealmOrder
			|| realmOrder == record.HighestRealmOrder && minorStage > record.HighestMinorStage
			|| captureProgressIncrease && realmOrder == record.HighestRealmOrder
				&& minorStage == record.HighestMinorStage && progress > record.HighestRealmProgress
			|| string.IsNullOrWhiteSpace(record.HighestSnapshotJson);
		if (isHigher)
		{
			XjDengMingShiManager.HighRealmSnapshot snapshot = XjDengMingShiManager.CaptureHighRealmForArchive(actor);
			if (snapshot != null)
			{
				record.DaoTu = daoTu;
				record.CultivationPath = path;
				record.HighestRealmId = realmId;
				record.HighestRealmOrder = realmOrder;
				record.HighestMinorStage = minorStage;
				record.HighestRealmProgress = progress;
				record.HighestJinDanYiXiang = yiXiang;
				record.HighestSnapshotJson = JsonConvert.SerializeObject(snapshot, Formatting.None);
				record.SavedTraitIds = CaptureNonIdentityTraits(actor);
				record.HighestReachedYear = currentYear;
				dirty = true;
			}
		}
		if (dirty) XjWorldArchiveSystem.MarkChanged();
		return record;
	}

	private static void TryReappearOne(int currentYear, HashSet<string> presentDaoTu)
	{
		XjGuZunArchiveRecord selected = null;
		int selectedAbsence = -1;
		foreach (XjGuZunArchiveRecord record in ByArchiveId.Values)
		{
			if (!CanReappear(record, currentYear, presentDaoTu, out int absenceYears)) continue;
			if (selected == null
				|| absenceYears > selectedAbsence
				|| absenceYears == selectedAbsence && record.HeavenFavorScore > selected.HeavenFavorScore
				|| absenceYears == selectedAbsence && record.HeavenFavorScore == selected.HeavenFavorScore
					&& string.CompareOrdinal(record.ArchiveId, selected.ArchiveId) < 0)
			{
				selected = record;
				selectedAbsence = absenceYears;
			}
		}
		if (selected == null) return;

		WorldTile tile = ResolveReappearanceTile(selected);
		if (tile == null) return;
		XjDengMingShiManager.HighRealmSnapshot snapshot;
		try { snapshot = JsonConvert.DeserializeObject<XjDengMingShiManager.HighRealmSnapshot>(selected.HighestSnapshotJson); }
		catch (System.Exception xjCaught364_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:364", xjCaught364_1);
			 snapshot = null; }
		if (snapshot == null) return;
		int age = ResolveReappearanceAge(selected);
		if (!XjDengMingShiManager.TrySpawnHighRealmArchiveActor(
			tile,
			selected.AssetId,
			selected.ActorName,
			selected.SavedTraitIds,
			snapshot,
			age,
			out Actor actor))
		{
			return;
		}

		long actorId = GetActorId(actor);
		selected.CurrentActorId = actorId;
		selected.IsCurrentlyManifested = true;
		selected.ReappearanceCount++;
		selected.LastReappearanceYear = currentYear;
		selected.LastSelfTruthCheckYear = currentYear;
		ArchiveIdByCurrentActor[actorId] = selected.ArchiveId;
		XjActorAccessor.SetString(actor, ArchiveIdKey, selected.ArchiveId);
		XjActorAccessor.SetString(actor, SourceActorIdKey, selected.SourceActorId.ToString());
		XjActorAccessor.SetInt(actor, XjActorDataKeys.GuZunManifestation, 1);
		// TrySpawnHighRealmArchiveActor 会先恢复境界并可能触发通用高境注册；
		// 命痕标记落下后立即从修士/真君仙鉴索引剔除，避免每次死亡都走完整修士归档。
		XjCultivatorCache.Remove(actorId);
		XjJinDanImmortalityRegistry.RemoveForRealmLoss(actor);
		XjGuoWeiRegistry.RemoveEphemeralClaims(actorId);
		XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
		// 故尊重现不恢复原家族/宗门成员关系：它只是借旧地显化的一缕命痕。
		XjSemanticDiagnostics.RecordGuZun("reappear", selected.DaoTu);
		XjDaoTuHighRealmContinuityRecord continuity = GetOrCreateContinuity(selected.DaoTu, currentYear);
		continuity.LastHighRealmPresentYear = currentYear;
		continuity.LastReappearanceYear = currentYear;
		_lastGlobalReappearanceYear = currentYear;
		XjWorldArchiveSystem.MarkChanged();

		string text = selected.DaoTu + "道途高境断绝千载，天地犹记" + selected.ActorName
			+ "之名。故尊自旧世命痕中重现，仍复昔日最高道行。";
		XjBroadcastSystem.ShowRecordedWorldTipCritical(text, color: "#D9B36C");
		XjWorldHistoryStore.RecordDomainEvent(
			XuanJianVNext.Data.History.XjWorldHistoryCategory.HighRealm,
			"故尊重现",
			text,
			5,
			true,
			actorId: actorId,
			actorName: selected.ActorName,
			year: currentYear);
	}

	private static bool CanReappear(XjGuZunArchiveRecord record, int currentYear, HashSet<string> presentDaoTu, out int absenceYears)
	{
		absenceYears = 0;
		if (record == null || !TryNormalizeEligibleArchiveRecord(record)
			|| record.ReappearanceCount >= 1
			|| !record.HeavenFavored || record.IsCurrentlyManifested || record.DeathYear <= 0
			|| string.IsNullOrWhiteSpace(record.DaoTu) || string.IsNullOrWhiteSpace(record.HighestSnapshotJson)
			|| presentDaoTu.Contains(record.DaoTu))
		{
			return false;
		}
		XjDaoTuHighRealmContinuityRecord continuity = GetOrCreateContinuity(record.DaoTu, currentYear);
		// “故尊复现”是道途断绝后的唯一一次天地回响，不是可循环刷新的复活池。
		if (continuity.LastReappearanceYear > 0) return false;
		absenceYears = Math.Max(0, currentYear - continuity.LastHighRealmPresentYear);
		return absenceYears >= ReappearanceAbsenceYears;
	}

	private static XjDaoTuHighRealmContinuityRecord GetOrCreateContinuity(string daoTu, int currentYear)
	{
		string key = daoTu?.Trim() ?? string.Empty;
		if (!Continuity.TryGetValue(key, out XjDaoTuHighRealmContinuityRecord record))
		{
			record = new XjDaoTuHighRealmContinuityRecord
			{
				DaoTu = key,
				LastHighRealmPresentYear = Math.Max(0, currentYear)
			};
			Continuity[key] = record;
		}
		return record;
	}

	private static void EvaluateHeavenFavor(XjGuZunArchiveRecord record, Actor actor)
	{
		int score = 0;
		if (actor?.data != null && XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu))
		{
			score += Mathf.Clamp(Mathf.RoundToInt(mingShu), 0, 200);
		}
		if (actor?.data != null && XjGuoWeiQuanBingRegistry.TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState authority))
		{
			if (!string.IsNullOrWhiteSpace(authority.GuoWeiZhongAi)) score += 60;
			if (!string.IsNullOrWhiteSpace(authority.GuoWei)
				&& authority.GuoWei.IndexOf(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) >= 0) score += 30;
			if (!string.IsNullOrWhiteSpace(authority.SeizedQuanBing)) score += 10;
		}
		if (record.HighestMinorStage >= 3) score += 20;
		record.HeavenFavorScore = Math.Max(record.HeavenFavorScore, score);
		record.HeavenFavored = record.HeavenFavored || score >= 120;
	}

	private static void TrimEligibleCandidates(string daoTu)
	{
		if (string.IsNullOrWhiteSpace(daoTu)) return;
		List<XjGuZunArchiveRecord> candidates = new List<XjGuZunArchiveRecord>();
		foreach (XjGuZunArchiveRecord record in ByArchiveId.Values)
		{
			if (TryNormalizeEligibleArchiveRecord(record)
				&& record.ReappearanceCount <= 0
				&& record.HeavenFavored && record.DeathYear > 0 && !record.IsCurrentlyManifested
				&& string.Equals(record.DaoTu, daoTu, StringComparison.Ordinal)) candidates.Add(record);
		}
		candidates.Sort((a, b) =>
		{
			int c = b.HeavenFavorScore.CompareTo(a.HeavenFavorScore);
			if (c != 0) return c;
			c = b.HighestRealmOrder.CompareTo(a.HighestRealmOrder);
			if (c != 0) return c;
			return a.DeathYear.CompareTo(b.DeathYear);
		});
		List<string> removeIds = null;
		for (int i = MaxEligiblePerDaoTu; i < candidates.Count; i++)
		{
			candidates[i].HeavenFavored = false;
			if (!candidates[i].IsCurrentlyManifested)
			{
				removeIds ??= new List<string>();
				removeIds.Add(candidates[i].ArchiveId);
			}
		}
		if (removeIds != null)
		{
			for (int i = 0; i < removeIds.Count; i++) RemoveArchiveRecord(removeIds[i]);
		}
	}

	private static void RemoveArchiveRecord(string archiveId)
	{
		if (string.IsNullOrWhiteSpace(archiveId) || !ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record)) return;
		if (record.CurrentActorId > 0L) ArchiveIdByCurrentActor.Remove(record.CurrentActorId);
		PendingDeathArchiveIds.Remove(archiveId);
		PendingDeathDaoTuByArchiveId.Remove(archiveId);
		ByArchiveId.Remove(archiveId);
	}

	private static bool TryNormalizeEligibleArchiveRecord(XjGuZunArchiveRecord record)
	{
		if (record == null || string.IsNullOrWhiteSpace(record.HighestSnapshotJson))
		{
			return false;
		}

		XjDengMingShiManager.HighRealmSnapshot snapshot;
		try
		{
			snapshot = JsonConvert.DeserializeObject<XjDengMingShiManager.HighRealmSnapshot>(record.HighestSnapshotJson);
		}
		catch (System.Exception xjCaught514_2)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:514", xjCaught514_2);
			
			return false;
		}
		if (snapshot == null || !string.IsNullOrWhiteSpace(snapshot.ShenDanGuoWei)
			|| string.Equals(snapshot.HighRealmPayloadKind, XjDengMingShiCultivationRestore.PayloadShenDan, StringComparison.Ordinal))
		{
			return false;
		}

		string realmId = XjRealmHelper.NormalizeId(snapshot.RealmId);
		if (string.IsNullOrWhiteSpace(realmId)) realmId = XjRealmHelper.NormalizeId(record.HighestRealmId);
		string path = string.IsNullOrWhiteSpace(snapshot.CultivationPath)
			? record.CultivationPath ?? string.Empty
			: snapshot.CultivationPath.Trim();
		bool exactJinDan = string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal);
		bool exactFuQiZhenJun = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			&& string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal);
		string guoWei = string.IsNullOrWhiteSpace(snapshot.GuoWei)
			? record.HighestGuoWei ?? string.Empty
			: snapshot.GuoWei.Trim();
		if ((!exactJinDan && !exactFuQiZhenJun)
			|| string.IsNullOrWhiteSpace(guoWei)
			|| guoWei.IndexOf(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) < 0)
		{
			return false;
		}

		record.SchemaVersion = Math.Max(3, record.SchemaVersion);
		record.HighestRealmId = realmId;
		record.CultivationPath = path;
		record.HighestGuoWei = guoWei;
		record.EligibleZhengWei = true;
		return true;
	}

	private static void ResolveManifestedSelfDissolutions(int currentYear)
	{
		List<XjGuZunArchiveRecord> manifested = null;
		foreach (XjGuZunArchiveRecord record in ByArchiveId.Values)
		{
			if (record == null || !record.IsCurrentlyManifested || record.ReappearanceCount <= 0 || record.CurrentActorId <= 0L)
			{
				continue;
			}
			int lastCheckYear = Math.Max(record.LastReappearanceYear, record.LastSelfTruthCheckYear);
			if (currentYear - lastCheckYear < CheckIntervalYears)
			{
				continue;
			}
			manifested ??= new List<XjGuZunArchiveRecord>();
			manifested.Add(record);
		}
		if (manifested == null) return;

		for (int i = 0; i < manifested.Count; i++)
		{
			XjGuZunArchiveRecord record = manifested[i];
			record.LastSelfTruthCheckYear = currentYear;
			XjWorldArchiveSystem.MarkChanged();
			int roll = XjDeterministicHash.PositiveIndex(
				record.CurrentActorId + currentYear,
				"guzun_manifestation_self_truth|" + record.ArchiveId,
				100);
			if (roll >= 40) continue;
			if (!XjScheduler.ResolveActor(record.CurrentActorId, out Actor actor) || !XjSafeCore.IsAliveActor(actor))
			{
				record.IsCurrentlyManifested = false;
				record.CurrentActorId = 0L;
				continue;
			}
			DissolveFalseManifestation(actor, record, currentYear);
		}
	}

	private static void DissolveFalseManifestation(Actor actor, XjGuZunArchiveRecord record, int currentYear)
	{
		long actorId = GetActorId(actor);
		string actorName = SafeName(actor);
		string originalName = string.IsNullOrWhiteSpace(record.ActorName) ? actorName : record.ActorName.Trim();
		string text = actorName + "枯坐内观，终于照见此身只是故尊“" + originalName
			+ "”遗于天地的命痕重现，并非昔日本尊。其人遂主动散去一身道行，形神归还天地。";

		XjBroadcastSystem.ShowRecordedWorldTipCritical(text, color: "#D9B36C");
		XjWorldHistoryStore.RecordDomainEvent(
			XuanJianVNext.Data.History.XjWorldHistoryCategory.HighRealm,
			"故尊辨真散去",
			text,
			5,
			true,
			actorId: actorId,
			actorName: actorName,
			sectId: record.OriginalZongMenId,
			familyId: record.OriginalFamilyId,
			cityId: record.OriginalCityId,
			year: currentYear,
			eventType: "GuZunSelfDissolved",
			result: XuanJianVNext.Data.History.XjHistoryResult.Death);
		XjThreeBookWriter.RecordGuZunSelfDissolution(
			actorId,
			actorName,
			record.OriginalFamilyId,
			record.OriginalFamilyName,
			record.OriginalZongMenId,
			record.OriginalZongMenName,
			currentYear,
			text);

		string archiveId = record.ArchiveId;
		record.HeavenFavored = false;
		record.IsCurrentlyManifested = false;
		record.CurrentActorId = 0L;
		RemoveArchiveRecord(archiveId);
		XjWorldArchiveSystem.MarkChanged();
		XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)5, false, XjDeathCause.TechnicalRemoval);
	}


	internal static bool IsManifestationActor(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.GuZunManifestation, out int markerValue) && markerValue > 0)
		{
			return true;
		}
		long actorId = GetActorId(actor);
		return actorId > 0L
			&& ArchiveIdByCurrentActor.TryGetValue(actorId, out string archiveId)
			&& ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record)
			&& record.IsCurrentlyManifested
			&& record.ReappearanceCount > 0;
	}

	internal static bool IsKnownManifestationActorId(long actorId)
	{
		return actorId > 0L
			&& ArchiveIdByCurrentActor.TryGetValue(actorId, out string archiveId)
			&& ByArchiveId.TryGetValue(archiveId, out XjGuZunArchiveRecord record)
			&& record.IsCurrentlyManifested
			&& record.ReappearanceCount > 0;
	}

	private static void PurgeLegacyImmortalityPollution()
	{
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> immortality = XjJinDanImmortalityRegistry.ReadAll();
		if (immortality == null || immortality.Count == 0 || ByArchiveId.Count == 0) return;
		List<long> removeIds = null;
		for (int i = 0; i < immortality.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord item = immortality[i];
			if (item == null || item.ActorId <= 0L) continue;
			foreach (XjGuZunArchiveRecord guZun in ByArchiveId.Values)
			{
				if (guZun == null || guZun.ReappearanceCount <= 0 || item.ActorId == guZun.SourceActorId) continue;
				// 旧版每次复现可能在真实果位被占后改投同道其他席位，因此不能再用 HighestGuoWei 精确匹配。
				// 只清理由复现产生的“同名 + 同道途”死亡副本；仍存活的普通同名修士不会被这次迁移误删。
				if (item.IsAlive
					|| !string.Equals((item.Name ?? string.Empty).Trim(), (guZun.ActorName ?? string.Empty).Trim(), StringComparison.Ordinal)
					|| !string.Equals((item.DaoTu ?? string.Empty).Trim(), (guZun.DaoTu ?? string.Empty).Trim(), StringComparison.Ordinal))
				{
					continue;
				}
				removeIds ??= new List<long>();
				removeIds.Add(item.ActorId);
				break;
			}
		}
		if (removeIds == null) return;
		for (int i = 0; i < removeIds.Count; i++) XjJinDanImmortalityRegistry.RemoveByActorId(removeIds[i]);
	}

	private static bool IsEligibleHighRealm(Actor actor, out string realmId, out string daoTu, out string path)
	{
		realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		daoTu = string.Empty;
		path = string.Empty;
		if (actor?.data == null || IsShenDan(actor)
			|| XjXuanJianShenTongSpecials.IsJieLinXian(actor)
			|| XjXuanJianShenTongSpecials.IsYuYiXian(actor))
		{
			return false;
		}

		bool isJinDan = string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		bool isFuQiZhenJun = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		if (!isJinDan && !isFuQiZhenJun)
		{
			return false;
		}

		XjCultivationPathRules.TryGetPath(actor, out path);
		path ??= string.Empty;
		if ((isJinDan && !string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
			|| (isFuQiZhenJun && !string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)))
		{
			return false;
		}

		XjJinDanState highRealmState = XjJinDanAccessor.BuildState(actor);
		if (!highRealmState.Found
			|| string.IsNullOrWhiteSpace(highRealmState.GuoWei)
			|| highRealmState.GuoWei.IndexOf(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) < 0)
		{
			return false;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu)
			|| string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		daoTu = daoTu.Trim();
		return true;
	}

	private static bool IsShenDan(Actor actor)
	{
		return actor?.data != null && XjShenDanAccessor.BuildState(actor).Found;
	}

	private static string ResolveArchiveId(Actor actor, long actorId)
	{
		if (XjActorAccessor.TryGetString(actor, ArchiveIdKey, out string existing) && !string.IsNullOrWhiteSpace(existing)) return existing.Trim();
		return "guzun:" + actorId;
	}

	private static int ResolveMinorStage(int yiXiang)
	{
		if (yiXiang >= 6000) return 3;
		if (yiXiang >= 3000) return 2;
		if (yiXiang >= 1000) return 1;
		return 0;
	}

	private static int ResolveProgress(Actor actor, string path)
	{
		if (string.Equals(path, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProgress, out int coreProgress)) return Math.Max(0, coreProgress);
		return ReadYiXiang(actor);
	}

	private static int ReadYiXiang(Actor actor)
	{
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int value) ? Math.Max(0, value) : 0;
	}

	private static int ResolveReappearanceAge(XjGuZunArchiveRecord record)
	{
		bool isFuQiZhenJun = string.Equals(record.HighestRealmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(record.CultivationPath, XjCultivationPathIds.FuQiYangXing, StringComparison.Ordinal);
		int minimum = isFuQiZhenJun ? 600 : 400;
		float finiteLifespan;
		switch (record.HighestMinorStage)
		{
			case 3: finiteLifespan = isFuQiZhenJun ? 10368f : 6080f; break;
			case 2: finiteLifespan = isFuQiZhenJun ? 8640f : 5080f; break;
			case 1: finiteLifespan = isFuQiZhenJun ? 7200f : 4080f; break;
			default: finiteLifespan = isFuQiZhenJun ? 6000f : 3080f; break;
		}
		int safeMaximum = Math.Max(minimum, Mathf.FloorToInt(finiteLifespan * 0.85f));
		return Math.Max(minimum, Math.Min(Math.Max(minimum, record.DeathAge), safeMaximum));
	}

	private static WorldTile ResolveReappearanceTile(XjGuZunArchiveRecord record)
	{
		// 1. 原家族仍有存活成员：优先在家族当前落脚处重现。
		if (record.OriginalFamilyId > 0L)
		{
			int checkedMembers = 0;
			foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(record.OriginalFamilyId))
			{
				if (member?.data == null || !member.isAlive()) continue;
				try
				{
					WorldTile tile = ((BaseSimObject)member).current_tile;
					if (tile != null) return tile;
				}
				catch (System.Exception xjCaught737) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:737", xjCaught737); }
				if (++checkedMembers >= 64) break;
			}
		}

		// 2. 原宗门仍在：回到宗门主城。
		if (record.OriginalZongMenId > 0L
			&& XjSectCityData.TryResolveZongMenCity(record.OriginalZongMenId, out City sectCity)
			&& TryResolveCityTile(sectCity, out WorldTile sectTile))
		{
			return sectTile;
		}

		// 3. 原城市仍在：回到原城市。
		if (record.OriginalCityId > 0L
			&& XjWorldLookupIndex.TryResolveCity(record.OriginalCityId, out City originalCity)
			&& TryResolveCityTile(originalCity, out WorldTile originalTile))
		{
			return originalTile;
		}

		// 4. 原死亡地点仍有效。
		if (record.DeathTileX != 0 || record.DeathTileY != 0)
		{
			try
			{
				WorldTile stored = World.world?.GetTileSimple(record.DeathTileX, record.DeathTileY);
				if (stored != null) return stored;
			}
			catch (System.Exception xjCaught766) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:766", xjCaught766); }
		}

		// 5. 最后才借用现存高境的稳定落脚处。
		IReadOnlyList<long> highRealmIds = XjCultivatorCache.GetZhenJunOrHigherIds();
		for (int i = 0; i < highRealmIds.Count; i++)
		{
			if (!XjScheduler.ResolveActor(highRealmIds[i], out Actor actor) || actor?.data == null) continue;
			try
			{
				WorldTile tile = ((BaseSimObject)actor).current_tile;
				if (tile != null) return tile;
			}
			catch (System.Exception xjCaught779) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:779", xjCaught779); }
		}
		return null;
	}

	private static bool TryResolveCityTile(City city, out WorldTile tile)
	{
		tile = null;
		return city?.data != null && XjNativeMapPositionInterop.TryResolveTile(city, out tile);
	}

	private static bool CaptureOriginalIdentity(Actor actor, XjGuZunArchiveRecord record)
	{
		if (actor?.data == null || record == null) return false;
		bool dirty = false;
		long actorId = GetActorId(actor);
		if (actorId > 0L && XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			&& familyId > 0L)
		{
			string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
			if (record.OriginalFamilyId != familyId) { record.OriginalFamilyId = familyId; dirty = true; }
			if (!string.Equals(record.OriginalFamilyName, familyName, StringComparison.Ordinal))
			{
				record.OriginalFamilyName = familyName ?? string.Empty;
				dirty = true;
			}
		}

		XjSectIdentitySnapshot sect = XjSectIdentityReader.BuildIdentity(actor);
		if (sect.Found && sect.ZongMenId > 0L)
		{
			if (record.OriginalZongMenId != sect.ZongMenId) { record.OriginalZongMenId = sect.ZongMenId; dirty = true; }
			if (!string.Equals(record.OriginalZongMenName, sect.ZongMenName, StringComparison.Ordinal))
			{
				record.OriginalZongMenName = sect.ZongMenName ?? string.Empty;
				dirty = true;
			}
		}

		if (actor.city?.data != null)
		{
			long cityId = actor.city.data.id;
			string cityName = ((BaseSystemData)actor.city.data).name ?? string.Empty;
			if (record.OriginalCityId != cityId) { record.OriginalCityId = cityId; dirty = true; }
			if (!string.Equals(record.OriginalCityName, cityName, StringComparison.Ordinal))
			{
				record.OriginalCityName = cityName;
				dirty = true;
			}
		}
		return dirty;
	}

	private static void RestoreOriginalIdentity(Actor actor, XjGuZunArchiveRecord record, int currentYear)
	{
		if (actor?.data == null || record == null) return;
		if (record.OriginalFamilyId > 0L
			&& XjFamilyReadModel.Shared.GetFamilyMemberIds(record.OriginalFamilyId).Count > 0)
		{
			XjFamilyMemberIndex.Shared.RelinkActorToFamily(actor, record.OriginalFamilyId);
		}
		if (record.OriginalZongMenId > 0L
			&& XjSectCityData.TryResolveZongMenCity(record.OriginalZongMenId, out City sectCity))
		{
			XjSectMembershipService.EnsureMember(sectCity, actor, currentYear, "GuZunReappearance");
		}
	}

	private static List<string> CaptureNonIdentityTraits(Actor actor)
	{
		List<string> result = new List<string>();
		if (actor?.data?.saved_traits == null) return result;
		string[] daoTuTraits = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < actor.data.saved_traits.Count; i++)
		{
			string traitId = actor.data.saved_traits[i];
			if (string.IsNullOrWhiteSpace(traitId)
				|| Array.IndexOf(daoTuTraits, traitId) >= 0
				|| XjRealmHelper.IsKnownTag(traitId)) continue;
			if (!result.Contains(traitId)) result.Add(traitId);
		}
		return result;
	}

	private static void TryCaptureTile(Actor actor, out int x, out int y)
	{
		x = 0; y = 0;
		try
		{
			WorldTile tile = ((BaseSimObject)actor).current_tile;
			if (tile != null) { x = tile.pos.x; y = tile.pos.y; }
		}
		catch (System.Exception xjCaught889) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:889", xjCaught889); }
	}

	private static long GetActorId(Actor actor)
	{
		try { return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id; }
		catch (System.Exception xjCaught895_8) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:895", xjCaught895_8);
			 return 0L; }
	}

	private static string SafeName(Actor actor)
	{
		try { string name = actor?.getName(); return string.IsNullOrWhiteSpace(name) ? "无名故尊" : name.Trim(); }
		catch (System.Exception xjCaught901_9) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjGuZunRegistry.cs:901", xjCaught901_9);
			 return "无名故尊"; }
	}

	private static int CurrentYear => Math.Max(0, World.world?.map_stats?.year ?? XjYearTracker.CurrentYear);

	private static XjGuZunArchiveRecord Clone(XjGuZunArchiveRecord source)
	{
		return new XjGuZunArchiveRecord
		{
			SchemaVersion = Math.Max(3, source.SchemaVersion), ArchiveId = source.ArchiveId ?? string.Empty,
			SourceActorId = source.SourceActorId, CurrentActorId = source.CurrentActorId,
			ActorName = source.ActorName ?? string.Empty, AssetId = source.AssetId ?? string.Empty,
			DaoTu = source.DaoTu ?? string.Empty, CultivationPath = source.CultivationPath ?? string.Empty,
			HighestRealmId = source.HighestRealmId ?? string.Empty, HighestRealmOrder = source.HighestRealmOrder,
			HighestMinorStage = source.HighestMinorStage, HighestRealmProgress = source.HighestRealmProgress,
			HighestJinDanYiXiang = source.HighestJinDanYiXiang, HighestGuoWei = source.HighestGuoWei ?? string.Empty,
			EligibleZhengWei = source.EligibleZhengWei, HighestSnapshotJson = source.HighestSnapshotJson ?? string.Empty,
			SavedTraitIds = source.SavedTraitIds == null ? new List<string>() : new List<string>(source.SavedTraitIds),
			FirstObservedYear = source.FirstObservedYear, HighestReachedYear = source.HighestReachedYear,
			DeathYear = source.DeathYear, DeathAge = source.DeathAge, DeathCause = source.DeathCause ?? string.Empty,
			DeathTileX = source.DeathTileX, DeathTileY = source.DeathTileY,
			OriginalFamilyId = source.OriginalFamilyId, OriginalFamilyName = source.OriginalFamilyName ?? string.Empty,
			OriginalZongMenId = source.OriginalZongMenId, OriginalZongMenName = source.OriginalZongMenName ?? string.Empty,
			OriginalCityId = source.OriginalCityId, OriginalCityName = source.OriginalCityName ?? string.Empty,
			HeavenFavored = source.HeavenFavored, HeavenFavorScore = source.HeavenFavorScore,
			IsCurrentlyManifested = source.IsCurrentlyManifested, ReappearanceCount = source.ReappearanceCount,
			LastReappearanceYear = source.LastReappearanceYear,
			LastSelfTruthCheckYear = source.LastSelfTruthCheckYear
		};
	}

	private static XjDaoTuHighRealmContinuityRecord Clone(XjDaoTuHighRealmContinuityRecord source)
	{
		return new XjDaoTuHighRealmContinuityRecord
		{
			DaoTu = source.DaoTu ?? string.Empty,
			LastHighRealmPresentYear = source.LastHighRealmPresentYear,
			LastReappearanceYear = source.LastReappearanceYear
		};
	}
}
