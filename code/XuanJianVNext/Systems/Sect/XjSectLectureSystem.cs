using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectLectureSystem
{
	private const int ZhenRenCooldownYears = 10;
	private const int ZhenJunCooldownYears = 50;
	private const int MaxAttendees = 24;
	private const int MaxArchiveRecords = 240;
	private static readonly List<XjSectLectureArchiveRecord> Records = new List<XjSectLectureArchiveRecord>();
	private static long _recordRevision;
	private static long _cachedReadRevision = -1L;
	private static IReadOnlyList<XjSectLectureArchiveRecord> _cachedReadAll = Array.Empty<XjSectLectureArchiveRecord>();
	private static readonly Dictionary<long, int> LastZhenRenLectureYearBySect = new Dictionary<long, int>();
	private static readonly Dictionary<long, int> LastZhenJunLectureYearBySect = new Dictionary<long, int>();
	// 讲道由年度调度串行执行。候选者与家族排序只用于当次选席，复用
	// 容器即可避免每场讲道按宗门规模反复制造短命集合。
	private static readonly List<LectureCandidate> CandidateBuffer = new List<LectureCandidate>();
	private static readonly HashSet<long> CandidateActorIds = new HashSet<long>();
	private static readonly Dictionary<long, LectureCandidate> BestCandidateByFamily = new Dictionary<long, LectureCandidate>();
	private static readonly Dictionary<long, float> VoiceByFamily = new Dictionary<long, float>();
	private static readonly List<long> FamilyOrderBuffer = new List<long>();
	private static readonly HashSet<long> SelectedAttendeeIds = new HashSet<long>();
	private static IReadOnlyList<XjSectArchiveRecord> PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
	private static int _pendingAnnualYear;
	private static int _pendingAnnualIndex;

	internal static bool HasPendingForYear(int currentYear)
	{
		return currentYear > 0
			&& _pendingAnnualYear == currentYear
			&& _pendingAnnualIndex < PendingAnnualSects.Count;
	}

	internal static bool BeginYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return false;
		if (HasPendingForYear(currentYear)) return true;
		ClearPendingAnnualWork();
		PendingAnnualSects = XjSectRepository.ReadAllSects();
		_pendingAnnualYear = currentYear;
		_pendingAnnualIndex = 0;
		if (PendingAnnualSects.Count > 0) return true;
		ClearPendingAnnualWork();
		return false;
	}

	/// <summary>
	/// Continue at sect boundaries. One sect may still be an atomic lecture because
	/// candidate ranking and reward application must commit together, but a year with
	/// many sects can no longer monopolize one rendered frame.
	/// </summary>
	internal static bool TickPending(int itemBudget, double timeBudgetMs)
	{
		if (_pendingAnnualYear <= 0 || _pendingAnnualIndex >= PendingAnnualSects.Count)
		{
			ClearPendingAnnualWork();
			return true;
		}

		XjCooperativeBudget budget = new XjCooperativeBudget(
			Math.Max(1, itemBudget),
			timeBudgetMs,
			XjRuntimeFramePriority.Background);
		while (_pendingAnnualIndex < PendingAnnualSects.Count && budget.TryTake())
		{
			XjSectArchiveRecord sect = PendingAnnualSects[_pendingAnnualIndex++];
			ProcessAnnualSect(sect, _pendingAnnualYear);
		}

		if (_pendingAnnualIndex < PendingAnnualSects.Count) return false;
		ClearPendingAnnualWork();
		return true;
	}

	internal static void TickYear(int currentYear)
	{
		if (!BeginYear(currentYear)) return;
		while (_pendingAnnualIndex < PendingAnnualSects.Count)
		{
			XjSectArchiveRecord sect = PendingAnnualSects[_pendingAnnualIndex++];
			ProcessAnnualSect(sect, currentYear);
		}
		ClearPendingAnnualWork();
	}

	private static void ProcessAnnualSect(XjSectArchiveRecord sect, int currentYear)
	{
		if (sect == null || sect.SectId <= 0L || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) return;
		// 世界门禁已经把讲法宗门列表降到十年一次。真人与真君分开记冷却：
		// 50年节点优先真君讲道，其他10年节点仍可由真人主持，不让高境存在反向压掉日常讲法。
		TryHoldLecture(sect, currentYear);
	}

	internal static void ClearPendingAnnualWork()
	{
		PendingAnnualSects = Array.Empty<XjSectArchiveRecord>();
		_pendingAnnualYear = 0;
		_pendingAnnualIndex = 0;
	}

	private static bool TryHoldLecture(XjSectArchiveRecord sect, int currentYear)
	{
		XjSecretRealmArchiveRecord realm = null;
		XjSecretRealmRegistry.TryGetBySectId(sect.SectId, out realm);

		bool zhenJunDue = !LastZhenJunLectureYearBySect.TryGetValue(sect.SectId, out int lastZhenJun)
			|| currentYear - lastZhenJun >= ZhenJunCooldownYears;
		bool zhenRenDue = !LastZhenRenLectureYearBySect.TryGetValue(sect.SectId, out int lastZhenRen)
			|| currentYear - lastZhenRen >= ZhenRenCooldownYears;
		Actor lecturer = null;
		bool trueLordLecture = false;

		// 同一个十年节点最多举行一场：真君五十年节点优先，未到真君节点则尝试真人讲法。
		if (zhenJunDue)
		{
			lecturer = ResolveLecturer(sect, realm, trueLord: true);
			trueLordLecture = lecturer != null;
		}
		if (lecturer == null && zhenRenDue)
		{
			lecturer = ResolveLecturer(sect, realm, trueLord: false);
			trueLordLecture = false;
		}
		if (lecturer?.data == null || !lecturer.isAlive()) return false;

		bool lecturerFuQi = XjCultivationPathRules.IsFuQiYangXing(lecturer);
		XjActorAccessor.TryGetString(lecturer, XjActorDataKeys.DaoTu, out string lecturerDaoTu);
		string lecturerFuQiRoot = lecturerFuQi ? ResolveFuQiTraditionRoot(lecturer, lecturerDaoTu) : string.Empty;
		bool insideRealm = realm != null && realm.EntranceOpen && (string.Equals(realm.Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)
			|| string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal));
		int attendeeLimit = ResolveAttendeeLimit(realm, insideRealm);
		List<long> attendees = SelectAttendees(sect, lecturer, lecturerDaoTu, lecturerFuQiRoot, lecturerFuQi, attendeeLimit);
		// 无人可听讲时不得生成“0人听讲”事件，也不得占用讲法冷却或给予主持者贡献。
		if (attendees == null || attendees.Count == 0) return false;

		// 候选名单来自增量宗门索引，但角色仍可能在同一年度阶段中死亡、离宗或失效。
		// 原地压实名单，确保公告、历史和存档中的人数只统计真正完成结算者。
		int validAttendeeCount = 0;
		for (int i = 0; i < attendees.Count; i++)
		{
			long attendeeId = attendees[i];
			if (!XjScheduler.ResolveActor(attendeeId, out Actor actor) || actor?.data == null || !actor.isAlive()
				|| ReadActorSectId(actor) != sect.SectId) continue;
			attendees[validAttendeeCount++] = attendeeId;
			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			bool sameTradition = IsSameLectureTradition(actor, lecturerFuQi, lecturerDaoTu, lecturerFuQiRoot);
			ApplyLectureBenefit(actor, in snapshot, attendeeId, currentYear, lecturerFuQi, trueLordLecture, sameTradition);
		}
		if (validAttendeeCount <= 0) return false;
		if (validAttendeeCount < attendees.Count) attendees.RemoveRange(validAttendeeCount, attendees.Count - validAttendeeCount);

		if (trueLordLecture && !insideRealm)
		{
			XjYinSiExposurePursuitSystem.RecordExternalJinDanAction(lecturer, 1f, "在外界举行真君讲道", currentYear);
		}
		long lectureId = BuildLectureId(sect.SectId, currentYear, trueLordLecture);
		string lecturerName = SafeActorName(lecturer);
		string sectName = string.IsNullOrWhiteSpace(sect.Name) ? "某宗" : sect.Name.Trim();
		string lecturePlace = ResolveLecturePlace(realm, insideRealm);
		string lectureSummary;
		if (lecturerFuQi)
		{
			lectureSummary = trueLordLecture
				? sectName + "择日开讲性命大道，" + lecturerName + "于" + lecturePlace + "登坛。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名修士依次列席，听其演说真君羽士层次的神妙、金性与性命关窍。"
				: sectName + "开坛讲法，" + lecturerName + "于" + lecturePlace + "演说神妙归身与服气养性之理。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名弟子列席听讲，各自带着所得回峰参悟。";
		}
		else
		{
			lectureSummary = trueLordLecture
				? sectName + "择日开讲大道，" + lecturerName + "于" + lecturePlace + "登坛。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名修士依次列席，听其演说金丹层次的道途与关窍。"
				: sectName + "开坛讲法，" + lecturerName + "于" + lecturePlace + "演说采气、筑基与自身所悟。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名弟子列席听讲，各自带着所得回峰参悟。";
		}
		XjSectLectureArchiveRecord record = new XjSectLectureArchiveRecord
		{
			LectureId = lectureId,
			SectId = sect.SectId,
			LectureType = trueLordLecture ? XjSectLectureType.ZhenJun : XjSectLectureType.ZhenRen,
			LecturerActorId = ((BaseSystemData)lecturer.data).id,
			LecturerName = lecturerName,
			Year = currentYear,
			HeldInsideSecretRealm = insideRealm,
			SecretRealmId = insideRealm ? realm.RealmId : 0L,
			AttendeeActorIds = attendees,
			AttendeeCount = attendees.Count,
			Summary = lectureSummary
		};
		Records.Add(record);
		if (Records.Count > MaxArchiveRecords) Records.RemoveRange(0, Records.Count - MaxArchiveRecords);
		if (trueLordLecture) LastZhenJunLectureYearBySect[sect.SectId] = currentYear;
		else LastZhenRenLectureYearBySect[sect.SectId] = currentYear;
		XjSectContributionSystem.RecordGeneralContribution(lecturer, trueLordLecture ? 32f : 10f, currentYear, trueLordLecture ? "真君讲道" : "真人开坛");
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(record.LecturerActorId, out long lecturerFamilyId);
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.Sect, trueLordLecture ? "真君讲道" : "真人开坛",
			record.Summary, trueLordLecture ? 4 : 3, actorId: record.LecturerActorId, actorName: lecturerName,
			sectId: sect.SectId, familyId: lecturerFamilyId, cityId: sect.CapitalCityId, year: currentYear,
			eventType: trueLordLecture ? "SectLecture:ZhenJun" : "SectLecture:ZhenRen",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate));
		XjThreeBookWriter.RecordSectLecture(
			sect.SectId,
			sectName,
			record.LecturerActorId,
			lecturerName,
			currentYear,
			attendees.Count,
			trueLordLecture);
		MarkChanged();
		return true;
	}

	private static Actor ResolveLecturer(XjSectArchiveRecord sect, XjSecretRealmArchiveRecord realm, bool trueLord)
	{
		if (sect == null || sect.SectId <= 0L) return null;
		if (trueLord && realm != null && realm.SittingJinDanActorId > 0L
			&& XjScheduler.ResolveActor(realm.SittingJinDanActorId, out Actor sitting)
			&& IsExactLectureRealm(sitting, true) && ReadActorSectId(sitting) == sect.SectId)
		{
			return sitting;
		}

		if (sect.SovereignActorId > 0L
			&& XjScheduler.ResolveActor(sect.SovereignActorId, out Actor sovereign)
			&& IsExactLectureRealm(sovereign, trueLord) && ReadActorSectId(sovereign) == sect.SectId)
		{
			return sovereign;
		}
		if (sect.FounderActorId > 0L
			&& XjScheduler.ResolveActor(sect.FounderActorId, out Actor founder)
			&& IsExactLectureRealm(founder, trueLord) && ReadActorSectId(founder) == sect.SectId)
		{
			return founder;
		}

		// 领导层境界不匹配时才从既有 SectId -> ActorId 增量索引中找一名合适门人；
		// 十年级低频发生，不建立第二份讲师缓存，也不扫描世界人口。
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		Actor best = null;
		long bestId = long.MaxValue;
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L || actorId >= bestId
				|| !XjScheduler.ResolveActor(actorId, out Actor candidate)
				|| !IsExactLectureRealm(candidate, trueLord)
				|| ReadActorSectId(candidate) != sect.SectId) continue;
			best = candidate;
			bestId = actorId;
		}
		return best;
	}

	private static bool IsExactLectureRealm(Actor actor, bool trueLord)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string actual = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return trueLord
				? string.Equals(actual, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				: string.Equals(actual, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal);
		}
		if (!XjCultivationPathRules.IsZiFuJinDan(actor)) return false;
		return trueLord
			? string.Equals(actual, XjRealmIds.JinDan, StringComparison.Ordinal) || string.Equals(actual, XjRealmIds.ShenDan, StringComparison.Ordinal)
			: string.Equals(actual, XjRealmIds.ZiFu, StringComparison.Ordinal);
	}

	private static int ResolveAttendeeLimit(XjSecretRealmArchiveRecord realm, bool insideRealm)
	{
		if (!insideRealm || realm == null || realm.Capacity <= 0) return MaxAttendees;
		return Math.Clamp(realm.Capacity, 4, 48);
	}

	private static string ResolveLecturePlace(XjSecretRealmArchiveRecord realm, bool insideRealm)
	{
		if (!insideRealm || realm == null) return "宗门";
		if (string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal)) return "宗门洞天";
		if (string.Equals(realm.Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)) return "宗门福地";
		return "宗门秘境";
	}

	private static List<long> SelectAttendees(
		XjSectArchiveRecord sect,
		Actor lecturer,
		string lecturerDaoTu,
		string lecturerFuQiRoot,
		bool lecturerFuQi,
		int attendeeLimit)
	{
		attendeeLimit = Math.Clamp(attendeeLimit, 1, 64);
		CandidateBuffer.Clear();
		CandidateActorIds.Clear();
		BestCandidateByFamily.Clear();
		VoiceByFamily.Clear();
		FamilyOrderBuffer.Clear();
		SelectedAttendeeIds.Clear();
		long lecturerId = ((BaseSystemData)lecturer.data).id;
		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sect.SectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			TryAddLectureCandidate(actorIds[i], lecturerId, sect.SectId, lecturerDaoTu, lecturerFuQiRoot, lecturerFuQi, CandidateActorIds, CandidateBuffer);
		}
		// SectId -> 修士索引由角色观察器增量维护，避免讲道时重建
		// CityId -> 修士 的临时映射，再按宗门城镇二次查询。
		CandidateBuffer.Sort(CompareCandidates);
		for (int i = 0; i < CandidateBuffer.Count; i++)
		{
			LectureCandidate candidate = CandidateBuffer[i];
			long familyKey = candidate.FamilyId > 0L ? candidate.FamilyId : -candidate.ActorId;
			if (!BestCandidateByFamily.ContainsKey(familyKey)) BestCandidateByFamily.Add(familyKey, candidate);
			if (familyKey > 0L && XjSectRepository.TryGetFamilySeat(sect.SectId, familyKey, out XjSectFamilySeatArchiveRecord seat))
			{
				VoiceByFamily[familyKey] = seat.VoiceScore;
			}
		}
		foreach (long familyId in BestCandidateByFamily.Keys) FamilyOrderBuffer.Add(familyId);
		FamilyOrderBuffer.Sort((a, b) =>
		{
			float av = a > 0L && VoiceByFamily.TryGetValue(a, out float va) ? va : 0f;
			float bv = b > 0L && VoiceByFamily.TryGetValue(b, out float vb) ? vb : 0f;
			int weakFirst = av.CompareTo(bv);
			return weakFirst != 0 ? weakFirst : a.CompareTo(b);
		});

		List<long> result = new List<long>(Math.Min(attendeeLimit, CandidateBuffer.Count));
		// 每个正式/在地家族先获得一个最低保障名额，弱族优先。
		for (int i = 0; i < FamilyOrderBuffer.Count && result.Count < attendeeLimit; i++)
		{
			LectureCandidate candidate = BestCandidateByFamily[FamilyOrderBuffer[i]];
			result.Add(candidate.ActorId);
			SelectedAttendeeIds.Add(candidate.ActorId);
		}
		// 剩余名额按同道途、境界、道慧与稳定ActorId竞争；高话语权通过更多高质候选自然获得额外名额。
		for (int i = 0; i < CandidateBuffer.Count && result.Count < attendeeLimit; i++)
		{
			if (SelectedAttendeeIds.Add(CandidateBuffer[i].ActorId)) result.Add(CandidateBuffer[i].ActorId);
		}
		SelectedAttendeeIds.Clear();
		return result;
	}

	private static void TryAddLectureCandidate(
		long actorId,
		long lecturerId,
		long sectId,
		string lecturerDaoTu,
		string lecturerFuQiRoot,
		bool lecturerFuQi,
		HashSet<long> seen,
		List<LectureCandidate> candidates)
	{
		if (actorId <= 0L || actorId == lecturerId || !seen.Add(actorId)
			|| !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()
			|| ReadActorSectId(actor) != sectId) return;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId);
		bool sameTradition = IsSameLectureTradition(actor, lecturerFuQi, lecturerDaoTu, lecturerFuQiRoot);
		candidates.Add(new LectureCandidate(actorId, familyId, XjRealmHelper.GetOrder(realmId), NormalizeSortValue(huiGuang), sameTradition));
	}

	private static int CompareCandidates(LectureCandidate a, LectureCandidate b)
	{
		int same = b.SameDaoTu.CompareTo(a.SameDaoTu);
		if (same != 0) return same;
		int realm = b.RealmOrder.CompareTo(a.RealmOrder);
		if (realm != 0) return realm;
		int hui = b.HuiGuang.CompareTo(a.HuiGuang);
		return hui != 0 ? hui : a.ActorId.CompareTo(b.ActorId);
	}

	private static float NormalizeSortValue(float value)
	{
		return float.IsNaN(value) || float.IsInfinity(value) ? 0f : Math.Max(0f, value);
	}

	private readonly struct LectureCandidate
	{
		internal LectureCandidate(long actorId, long familyId, int realmOrder, float huiGuang, bool sameDaoTu)
		{
			ActorId = actorId; FamilyId = familyId; RealmOrder = realmOrder; HuiGuang = huiGuang; SameDaoTu = sameDaoTu;
		}
		internal long ActorId { get; }
		internal long FamilyId { get; }
		internal int RealmOrder { get; }
		internal float HuiGuang { get; }
		internal bool SameDaoTu { get; }
	}


	internal static IReadOnlyList<XjSectLectureArchiveRecord> ReadAll()
	{
		if (_cachedReadRevision == _recordRevision) return _cachedReadAll;
		if (Records.Count == 0)
		{
			_cachedReadAll = Array.Empty<XjSectLectureArchiveRecord>();
			_cachedReadRevision = _recordRevision;
			return _cachedReadAll;
		}

		List<XjSectLectureArchiveRecord> result = new List<XjSectLectureArchiveRecord>(Records.Count);
		for (int i = 0; i < Records.Count; i++) result.Add(Clone(Records[i]));
		result.Sort((a, b) => a.Year != b.Year ? b.Year.CompareTo(a.Year) : a.LectureId.CompareTo(b.LectureId));
		_cachedReadAll = result;
		_cachedReadRevision = _recordRevision;
		return _cachedReadAll;
	}

	internal static void ExportArchiveRecords(List<XjSectLectureArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		for (int i = 0; i < Records.Count; i++) target.Add(Clone(Records[i]));
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjSectLectureArchiveRecord> source)
	{
		Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjSectLectureArchiveRecord record = source[i];
			if (record == null || record.LectureId <= 0L || record.SectId <= 0L) continue;
			XjSectLectureArchiveRecord copy = Clone(record);
			Records.Add(copy);
			Dictionary<long, int> lastBySect = string.Equals(copy.LectureType, XjSectLectureType.ZhenJun, StringComparison.Ordinal)
				? LastZhenJunLectureYearBySect
				: LastZhenRenLectureYearBySect;
			if (!lastBySect.TryGetValue(copy.SectId, out int year) || copy.Year > year) lastBySect[copy.SectId] = copy.Year;
		}
		if (Records.Count > MaxArchiveRecords) Records.RemoveRange(0, Records.Count - MaxArchiveRecords);
		TouchRecordRevision();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SecretRealm);
	}

	internal static void Clear()
	{
		ClearPendingAnnualWork();
		Records.Clear();
		LastZhenRenLectureYearBySect.Clear();
		LastZhenJunLectureYearBySect.Clear();
		CandidateBuffer.Clear();
		CandidateActorIds.Clear();
		BestCandidateByFamily.Clear();
		VoiceByFamily.Clear();
		FamilyOrderBuffer.Clear();
		SelectedAttendeeIds.Clear();
		TouchRecordRevision();
		_cachedReadAll = Array.Empty<XjSectLectureArchiveRecord>();
		_cachedReadRevision = _recordRevision;
	}

	// 宗门灭亡后不应继续占用讲道冷却槽。该状态只是运行期去重，
	// 历史已经由 Records 和世界史独立保存。
	internal static void ForgetSect(long sectId)
	{
		if (sectId > 0L)
		{
			LastZhenRenLectureYearBySect.Remove(sectId);
			LastZhenJunLectureYearBySect.Remove(sectId);
		}
	}

	private static long ReadActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	private static bool IsSameLectureTradition(Actor actor, bool lecturerFuQi, string lecturerDaoTu, string lecturerFuQiRoot)
	{
		if (actor?.data == null) return false;
		if (lecturerFuQi)
		{
			return XjCultivationPathRules.IsFuQiYangXing(actor)
				&& !string.IsNullOrWhiteSpace(lecturerFuQiRoot)
				&& string.Equals(ResolveFuQiTraditionRoot(actor, string.Empty), lecturerFuQiRoot, StringComparison.Ordinal);
		}
		return XjCultivationPathRules.IsZiFuJinDan(actor)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& !string.IsNullOrWhiteSpace(lecturerDaoTu)
			&& string.Equals(daoTu, lecturerDaoTu, StringComparison.Ordinal);
	}

	private static string ResolveFuQiTraditionRoot(Actor actor, string knownDaoTu)
	{
		if (actor?.data == null) return string.Empty;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiDaoTuRootId, out string storedRoot)
			&& XjDaoTuCatalog.TryResolveRootId(storedRoot, out string normalizedStored))
		{
			return normalizedStored;
		}
		string daoTu = knownDaoTu;
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}
		if (XjDaoTuCatalog.TryResolveRootId(daoTu, out string resolvedRoot)) return resolvedRoot;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			&& string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
		{
			return XjDaoTuRootIds.LongGeng;
		}
		return string.Empty;
	}

	private static void ApplyLectureBenefit(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		long attendeeId,
		int currentYear,
		bool lecturerFuQi,
		bool trueLordLecture,
		bool sameTradition)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		float huiChance = trueLordLecture ? (sameTradition ? 0.30f : 0.12f) : (sameTradition ? 0.10f : 0.04f);
		if (XjDeterministicHash.Roll01(attendeeId, currentYear, "lecture", "huiguang") < huiChance)
		{
			float huiGain = trueLordLecture ? 2f : 1f;
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Add(huiGuang, huiGain, XjDaoHuiPolicy.OrdinaryGrowthCeiling));
		}

		bool attendeeFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool attendeeZiJin = XjCultivationPathRules.IsZiFuJinDan(actor);
		bool samePath = lecturerFuQi ? attendeeFuQi : attendeeZiJin;
		// 跨体系听讲只能增长通用道慧，不能把服气讲法解释成真元，
		// 也不能把紫金讲法直接推进服气神妙。
		if (!samePath) return;

		if (attendeeFuQi)
		{
			if (sameTradition) ApplyFuQiLectureProgress(actor, snapshot.RealmId, currentYear, trueLordLecture ? 5 : 2);
			return;
		}
		if (!attendeeZiJin) return;

		float lectureGain = trueLordLecture ? (sameTradition ? 150f : 75f) : (sameTradition ? 45f : 25f);
		float proposed = XjCultivationGrowthRules.ApplyRealmCap(snapshot, snapshot.ZhenYuan + lectureGain);
		float nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, proposed);
		if (nextZhenYuan > snapshot.ZhenYuan) XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
		if (trueLordLecture && sameTradition && XjRealmHelper.IsRealm(snapshot.RealmId, XjRealmIds.ZiFu))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLectureAidYear, currentYear);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus, 0.15f);
		}
	}

	private static void ApplyFuQiLectureProgress(Actor actor, string realmId, int currentYear, int years)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			string rootId = ResolveFuQiTraditionRoot(actor, string.Empty);
			if (string.Equals(rootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiSwordQi, out int swordQi);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiSwordQi, Math.Min(XjFuQiSwordDaoSystem.SwordQiTarget, swordQi + years));
				return;
			}
			if (XjFuQiCoreCatalog.TryGetByRootId(rootId, out XjFuQiCoreDefinition definition)
				&& definition.GameplayImplemented)
			{
				if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.GenericDaoQi, StringComparison.Ordinal))
				{
					XjFuQiGenericDaoQiHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
				else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.NatalTalisman, StringComparison.Ordinal))
				{
					XjFuQiNatalTalismanHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
				else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QingXuan, StringComparison.Ordinal))
				{
					XjFuQiQingXuanHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
				else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.HengZhu, StringComparison.Ordinal))
				{
					XjFuQiHengZhuHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
				else if (string.Equals(definition.HandlerId, XjFuQiHandlerIds.QuanDan, StringComparison.Ordinal))
				{
					XjFuQiQuanDanHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
				else if (XjFuQiBingGuCoreHandler.CanHandle(in definition))
				{
					XjFuQiBingGuCoreHandler.ApplyLectureAid(actor, currentYear, years, in definition);
				}
			}
			return;
		}
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			ShortenProject(actor, XjActorDataKeys.FuQiBodyProjectCompleteYear, currentYear, years);
			return;
		}
		if (!string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfected) && perfected > 0)
		{
			if (XjFuQiBalancePolicy.CanAcceleratePostFailureNurture(actor, currentYear))
			{
				ShortenProject(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, currentYear, years);
			}
		}
		else
		{
			ShortenProject(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, currentYear, years);
		}
	}

	private static void ShortenProject(Actor actor, string key, int currentYear, int years)
	{
		if (years <= 0 || !XjActorAccessor.TryGetInt(actor, key, out int completeYear) || completeYear <= currentYear) return;
		XjActorAccessor.SetInt(actor, key, Math.Max(currentYear + 1, completeYear - years));
	}

	private static long BuildLectureId(long sectId, int currentYear, bool trueLord)
	{
		long id = XjDeterministicHash.PositiveHash(sectId, "xuanjian.sect.lecture.v1|" + currentYear.ToString(CultureInfo.InvariantCulture) + "|" + (trueLord ? "jd" : "zf"));
		return id <= 0L ? sectId + currentYear : id;
	}

	private static string SafeActorName(Actor actor)
	{
		try { return actor?.getName() ?? "未名真人"; } catch (System.Exception xjCaught518_1) {
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Sect/XjSectLectureSystem.cs:518", xjCaught518_1);
			 return "未名真人"; }
	}

	private static void TouchRecordRevision()
	{
		_recordRevision = _recordRevision == long.MaxValue ? 1L : _recordRevision + 1L;
	}

	private static void MarkChanged()
	{
		TouchRecordRevision();
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SecretRealm | XjCodexDirtyFlags.History);
	}

	private static XjSectLectureArchiveRecord Clone(XjSectLectureArchiveRecord source)
	{
		return new XjSectLectureArchiveRecord
		{
			SchemaVersion = source.SchemaVersion, LectureId = source.LectureId, SectId = source.SectId,
			LectureType = source.LectureType ?? XjSectLectureType.ZhenRen, LecturerActorId = source.LecturerActorId,
			LecturerName = source.LecturerName ?? string.Empty, Year = source.Year,
			HeldInsideSecretRealm = source.HeldInsideSecretRealm, SecretRealmId = source.SecretRealmId,
			AttendeeActorIds = source.AttendeeActorIds == null ? new List<long>() : new List<long>(source.AttendeeActorIds),
			AttendeeCount = source.AttendeeCount, Summary = source.Summary ?? string.Empty
		};
	}
}
