using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
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
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectLectureSystem
{
	private const int ZhenRenCooldownYears = 20;
	private const int ZhenJunCooldownYears = 40;
	private const int MaxAttendees = 24;
	private const int MaxArchiveRecords = 240;
	private static readonly List<XjSectLectureArchiveRecord> Records = new List<XjSectLectureArchiveRecord>();
	private static readonly Dictionary<long, int> LastLectureYearBySect = new Dictionary<long, int>();
	// 讲道由年度调度串行执行。候选者与家族排序只用于当次选席，复用
	// 容器即可避免每场讲道按宗门规模反复制造短命集合。
	private static readonly List<LectureCandidate> CandidateBuffer = new List<LectureCandidate>();
	private static readonly HashSet<long> CandidateActorIds = new HashSet<long>();
	private static readonly Dictionary<long, LectureCandidate> BestCandidateByFamily = new Dictionary<long, LectureCandidate>();
	private static readonly Dictionary<long, float> VoiceByFamily = new Dictionary<long, float>();
	private static readonly List<long> FamilyOrderBuffer = new List<long>();
	private static readonly HashSet<long> SelectedAttendeeIds = new HashSet<long>();

	internal static void TickYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return;
		IReadOnlyList<XjSectArchiveRecord> sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect == null || sect.SectId <= 0L || string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) continue;
			if (LastLectureYearBySect.TryGetValue(sect.SectId, out int lastYear) && currentYear - lastYear < ZhenRenCooldownYears) continue;
			if (currentYear % 5 != XjDeterministicHash.PositiveIndex(sect.SectId, "sect.lecture.offset", 5)) continue;
			TryHoldLecture(sect, currentYear);
		}
	}

	private static bool TryHoldLecture(XjSectArchiveRecord sect, int currentYear)
	{
		Actor lecturer = null;
		bool trueLordLecture = false;
		XjSecretRealmArchiveRecord realm = null;
		if (XjSecretRealmRegistry.TryGetBySectId(sect.SectId, out realm) && realm.SittingJinDanActorId > 0L
			&& XjScheduler.ResolveActor(realm.SittingJinDanActorId, out Actor jinDan)
			&& IsAtLeast(jinDan, XjRealmIds.JinDan) && ReadActorSectId(jinDan) == sect.SectId)
		{
			lecturer = jinDan;
			trueLordLecture = true;
		}
		else if (sect.SovereignActorId > 0L
			&& XjScheduler.ResolveActor(sect.SovereignActorId, out Actor sovereign)
			&& IsAtLeast(sovereign, XjRealmIds.ZiFu) && ReadActorSectId(sovereign) == sect.SectId)
		{
			lecturer = sovereign;
		}
		else if (sect.FounderActorId > 0L
			&& XjScheduler.ResolveActor(sect.FounderActorId, out Actor founder)
			&& IsAtLeast(founder, XjRealmIds.ZiFu) && ReadActorSectId(founder) == sect.SectId)
		{
			lecturer = founder;
		}
		if (lecturer?.data == null || !lecturer.isAlive()) return false;
		int cooldown = trueLordLecture ? ZhenJunCooldownYears : ZhenRenCooldownYears;
		if (LastLectureYearBySect.TryGetValue(sect.SectId, out int last) && currentYear - last < cooldown) return false;

		XjActorAccessor.TryGetString(lecturer, XjActorDataKeys.DaoTu, out string lecturerDaoTu);
		bool insideRealm = realm != null && realm.EntranceOpen && (string.Equals(realm.Stage, XjSecretRealmStage.Fudi, StringComparison.Ordinal)
			|| string.Equals(realm.Stage, XjSecretRealmStage.Dongtian, StringComparison.Ordinal));
		int attendeeLimit = ResolveAttendeeLimit(realm, insideRealm);
		List<long> attendees = SelectAttendees(sect, lecturer, lecturerDaoTu, attendeeLimit);
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
			bool sameDaoTu = !string.IsNullOrWhiteSpace(lecturerDaoTu) && string.Equals(snapshot.DaoTu, lecturerDaoTu, StringComparison.Ordinal);
			float lectureGain = trueLordLecture ? (sameDaoTu ? 90f : 45f) : (sameDaoTu ? 35f : 20f);
			float proposed = XjCultivationGrowthRules.ApplyRealmCap(snapshot, snapshot.ZhenYuan + lectureGain);
			float nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, proposed);
			if (nextZhenYuan > snapshot.ZhenYuan) XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
			float huiChance = trueLordLecture ? (sameDaoTu ? 0.20f : 0.08f) : (sameDaoTu ? 0.08f : 0.03f);
			float huiGain = XjDeterministicHash.Roll01(attendeeId, currentYear, "lecture", "huiguang") < huiChance ? 1f : 0f;
			if (huiGain > 0f) XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, Math.Min(200f, Math.Max(0f, huiGuang) + huiGain));
			if (trueLordLecture && sameDaoTu && XjRealmHelper.IsRealm(snapshot.RealmId, XjRealmIds.ZiFu))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLectureAidYear, currentYear);
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus, 0.10f);
			}
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
		string lectureSummary = trueLordLecture
			? sectName + "择日开讲大道，" + lecturerName + "于" + lecturePlace + "登坛。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名修士依次列席，听其演说真君层次的道途与关窍。"
			: sectName + "开坛讲法，" + lecturerName + "于" + lecturePlace + "演说采气、筑基与自身所悟。门中" + attendees.Count.ToString(CultureInfo.InvariantCulture) + "名弟子列席听讲，各自带着所得回峰参悟。";
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
		LastLectureYearBySect[sect.SectId] = currentYear;
		XjSectContributionSystem.RecordGeneralContribution(lecturer, trueLordLecture ? 20f : 8f, currentYear, trueLordLecture ? "真君讲道" : "真人开坛");
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

	private static List<long> SelectAttendees(XjSectArchiveRecord sect, Actor lecturer, string lecturerDaoTu, int attendeeLimit)
	{
		attendeeLimit = Math.Clamp(attendeeLimit, 1, 64);
		CandidateBuffer.Clear();
		CandidateActorIds.Clear();
		BestCandidateByFamily.Clear();
		VoiceByFamily.Clear();
		FamilyOrderBuffer.Clear();
		SelectedAttendeeIds.Clear();
		long lecturerId = ((BaseSystemData)lecturer.data).id;
		IReadOnlyList<long> actorIds = XjZongMenCultivatorCityIndex.GetActorIdsForSect(sect.SectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			TryAddLectureCandidate(actorIds[i], lecturerId, sect.SectId, lecturerDaoTu, CandidateActorIds, CandidateBuffer);
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
		// 剩余名额按同道途、境界、慧光与稳定ActorId竞争；高话语权通过更多高质候选自然获得额外名额。
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
		HashSet<long> seen,
		List<LectureCandidate> candidates)
	{
		if (actorId <= 0L || actorId == lecturerId || !seen.Add(actorId)
			|| !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()
			|| ReadActorSectId(actor) != sectId) return;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId);
		candidates.Add(new LectureCandidate(actorId, familyId, XjRealmHelper.GetOrder(realmId), NormalizeSortValue(huiGuang),
			!string.IsNullOrWhiteSpace(lecturerDaoTu) && string.Equals(daoTu, lecturerDaoTu, StringComparison.Ordinal)));
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
		List<XjSectLectureArchiveRecord> result = new List<XjSectLectureArchiveRecord>(Records.Count);
		for (int i = 0; i < Records.Count; i++) result.Add(Clone(Records[i]));
		result.Sort((a, b) => a.Year != b.Year ? b.Year.CompareTo(a.Year) : a.LectureId.CompareTo(b.LectureId));
		return result;
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
			if (!LastLectureYearBySect.TryGetValue(copy.SectId, out int year) || copy.Year > year) LastLectureYearBySect[copy.SectId] = copy.Year;
		}
		if (Records.Count > MaxArchiveRecords) Records.RemoveRange(0, Records.Count - MaxArchiveRecords);
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.SecretRealm);
	}

	internal static void Clear()
	{
		Records.Clear();
		LastLectureYearBySect.Clear();
		CandidateBuffer.Clear();
		CandidateActorIds.Clear();
		BestCandidateByFamily.Clear();
		VoiceByFamily.Clear();
		FamilyOrderBuffer.Clear();
		SelectedAttendeeIds.Clear();
	}

	// 宗门灭亡后不应继续占用讲道冷却槽。该状态只是运行期去重，
	// 历史已经由 Records 和世界史独立保存。
	internal static void ForgetSect(long sectId)
	{
		if (sectId > 0L)
		{
			LastLectureYearBySect.Remove(sectId);
		}
	}

	private static long ReadActorSectId(Actor actor)
	{
		return XjSectRepository.ResolveActorSectId(actor);
	}

	private static bool IsAtLeast(Actor actor, string realmId)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		string actual = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjRealmHelper.GetOrder(actual) >= XjRealmHelper.GetOrder(realmId);
	}

	private static long BuildLectureId(long sectId, int currentYear, bool trueLord)
	{
		long id = XjDeterministicHash.PositiveHash(sectId, "xuanjian.sect.lecture.v1|" + currentYear.ToString(CultureInfo.InvariantCulture) + "|" + (trueLord ? "jd" : "zf"));
		return id <= 0L ? sectId + currentYear : id;
	}

	private static string SafeActorName(Actor actor)
	{
		try { return actor?.getName() ?? "未名真人"; } catch { return "未名真人"; }
	}

	private static void MarkChanged()
	{
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
