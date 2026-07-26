using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.History.Books;

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
		TargetActorId = targetActorId < 0L ? 0L : targetActorId;
		TargetActorName = targetActorName ?? string.Empty;
		AppliedYear = appliedYear < 0 ? 0 : appliedYear;
		Status = status ?? string.Empty;
	}
}

internal static class XjReincarnation
{
	private const string StatusPending = "Pending";
	private const string StatusApplied = "Applied";
	private const string ModeJinDan = "JinDan";
	private const string ModeGuoWeiZhongAi = "GuoWeiZhongAi";
	private const string ModeZiFuJinXing = "ZiFuJinXing";
	private const string ModeFamilyBorrowJinXing = "FamilyBorrowJinXing";
	private const int JinDanEarlyReincarnationChanceBasis = 1000;
	private const int JinDanMiddleReincarnationChanceBasis = 3000;
	private const int JinDanLateReincarnationChanceBasis = 5000;
	private const int JinDanPeakReincarnationChanceBasis = 7000;

	private static readonly Dictionary<long, XjReincarnationRecord> recordsByActorId = new Dictionary<long, XjReincarnationRecord>();

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
		ApplyRecordToActor(actor, pending);
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
			targetId,
			actor.getName(),
			currentYear,
			StatusApplied);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjThreeBookWriter.RecordJinDanReincarnation(actor, currentYear, pending.ActorName);
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

			recordsByActorId[record.ActorId] = new XjReincarnationRecord(
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
				string.IsNullOrWhiteSpace(record.Mode) ? ModeJinDan : record.Mode,
				record.GuoWeiZhongAi,
				record.TargetActorId,
				record.TargetActorName,
				record.AppliedYear,
				string.IsNullOrWhiteSpace(record.Status) ? StatusPending : record.Status);
		}
	}

	internal static void Clear()
	{
		recordsByActorId.Clear();
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
			&& string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
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

		if (string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal)
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
		try
		{
			return (((object)actor.asset).GetType()
				.GetProperty("race", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
				?.GetValue(actor.asset))?.ToString() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static void ApplyRecordToActor(Actor actor, XjReincarnationRecord record)
	{
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

		if (!ziFuJinXing && !string.IsNullOrWhiteSpace(record.GuoWeiZhongAi))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, record.GuoWei);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, record.GuoWeiZhongAi);
		}

		if (!ziFuJinXing && record.JinDanYiXiang > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, record.JinDanYiXiang);
		}

		if (!ziFuJinXing && !string.IsNullOrWhiteSpace(record.GuoWeiZhongAi))
		{
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
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuCongenital, mingShu);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShuAcquired, 0f);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.MingShu, mingShu);

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
		int huiGuangMin = ziFuJinXing ? 60 : 100;
		int huiGuangMax = ziFuJinXing ? 101 : 161;
		float huiGuang = XjDeterministicHash.BuildSeedInteger(reincarnatedActorId + record.ActorId, actor?.getName(), 63, huiGuangMin, huiGuangMax);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, huiGuang);

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

	private static float BuildReincarnationMingShu(Actor actor, bool ziFuJinXing)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int min = ziFuJinXing ? 40 : 140;
		int max = ziFuJinXing ? 81 : 240;
		return XjDeterministicHash.BuildSeedInteger(actorId, actor?.getName(), ziFuJinXing ? 61 : 62, min, max);
	}
}
