using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectMentorshipSystem
{
	private const int MaxStudents = 3;
	private const int RecruitIntervalYears = 10;
	private const int MinimumTeacherRealmOrder = 3;
	private const int MaxStudentsCheckedPerTeacher = 12;
	private const int MaximumStudentRealmOrder = 3;

	private sealed class StudentPools
	{
		internal readonly List<Actor> All = new List<Actor>();
		internal readonly Dictionary<string, List<Actor>> ByDaoTu =
			new Dictionary<string, List<Actor>>(StringComparer.Ordinal);
	}

	internal static void TickCity(City city, IReadOnlyList<long> actorIds, int currentYear)
	{
		if (city?.data == null || actorIds == null || actorIds.Count < 2)
		{
			return;
		}

		List<Actor> actors = new List<Actor>(actorIds.Count);
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (XjScheduler.ResolveActor(actorIds[i], out Actor actor)
				&& IsValidMentorshipActor(actor))
			{
				// actorIds 已经来自同一宗门权威成员表；师徒关系不应再被
				// “此刻是否住在山门代表城”二次截断。属城、外出门人仍属本宗。
				actors.Add(actor);
			}
		}

		if (actors.Count < 2)
		{
			return;
		}

		actors.Sort(CompareTeacherCandidates);
		StudentPools pools = BuildStudentPools(actors);
		for (int i = 0; i < actors.Count; i++)
		{
			Actor teacher = actors[i];
			CleanupTeacherStudents(teacher);
			if (CanRecruit(teacher, currentYear))
			{
				Actor student = FindStudent(teacher, pools);
				if (student != null) Recruit(teacher, student, currentYear);
			}
			TryTeachOneStudent(teacher, currentYear);
		}
	}


	private static void TryTeachOneStudent(Actor teacher, int currentYear)
	{
		if (teacher?.data == null || currentYear <= 0) return;
		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.XjZongMenLastTeachingYear, out int lastYear);
		if (lastYear > 0 && currentYear - lastYear < RecruitIntervalYears) return;
		List<Actor> students = GetLiveStudents(teacher);
		if (students.Count == 0) return;
		long teacherId = GetActorId(teacher);
		int index = XjDeterministicHash.PositiveIndex(teacherId + currentYear, "zongmen.mentorship.teaching", students.Count);
		Actor student = students[index];
		if (!XjSectMentorshipTeachingService.TryTeach(teacher, student, currentYear, out _)) return;
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.XjZongMenLastTeachingYear, currentYear);
	}

	internal static bool HasRecordedRelation(Actor actor)
	{
		return actor?.data != null
			&& (HasTeacher(actor) || GetStudentIds(actor).Count > 0);
	}

	internal static string BuildSummary(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		List<string> parts = new List<string>(2);
		if (TryGetTeacher(actor, out Actor teacher))
		{
			parts.Add("师傅：" + SafeName(teacher));
		}

		List<Actor> students = GetLiveStudents(actor);
		if (students.Count > 0)
		{
			int shown = Math.Min(3, students.Count);
			List<string> names = new List<string>(shown);
			for (int i = 0; i < shown; i++) names.Add(SafeName(students[i]));
			string suffix = students.Count > shown ? "等" + students.Count.ToString(CultureInfo.InvariantCulture) + "人" : string.Empty;
			parts.Add("弟子：" + string.Join("、", names) + suffix);
		}

		return parts.Count == 0 ? string.Empty : string.Join(" - ", parts);
	}

	private static bool CanRecruit(Actor teacher, int currentYear)
	{
		if (!IsValidMentorshipActor(teacher) || ResolveRealmOrder(teacher) < MinimumTeacherRealmOrder)
		{
			return false;
		}

		if (GetStudentIds(teacher).Count >= MaxStudents)
		{
			return false;
		}

		XjActorAccessor.TryGetInt(teacher, XjActorDataKeys.XjZongMenLastRecruitYear, out int lastRecruitYear);
		return currentYear <= 0 || lastRecruitYear <= 0 || currentYear - lastRecruitYear >= RecruitIntervalYears;
	}

	private static Actor FindStudent(Actor teacher, StudentPools pools)
	{
		if (teacher?.data == null || pools == null) return null;
		int teacherRealmOrder = ResolveRealmOrder(teacher);
		string teacherDaoTu = ResolveDaoTu(teacher);

		// 师徒关系是独立社会关系，不要求同宗、同家族，也不因任一方退宗/转宗自动解除。
		// 收徒时仍遵循修行传承的自然偏好：本道途 > 直接近邻 > 结构相关道途 > 其他。
		if (teacherDaoTu.Length > 0
			&& pools.ByDaoTu.TryGetValue(teacherDaoTu, out List<Actor> sameDaoTu))
		{
			Actor same = SelectBestStudent(teacher, teacherRealmOrder, sameDaoTu);
			if (same != null) return same;
		}

		Actor direct = SelectBestRelatedStudent(teacher, teacherRealmOrder, teacherDaoTu, pools, directOnly: true);
		if (direct != null) return direct;

		Actor structured = SelectBestRelatedStudent(teacher, teacherRealmOrder, teacherDaoTu, pools, directOnly: false);
		if (structured != null) return structured;

		return SelectBestStudent(teacher, teacherRealmOrder, pools.All);
	}

	private static Actor SelectBestRelatedStudent(
		Actor teacher,
		int teacherRealmOrder,
		string teacherDaoTu,
		StudentPools pools,
		bool directOnly)
	{
		if (teacherDaoTu.Length == 0 || pools == null || pools.ByDaoTu.Count == 0) return null;
		Actor selected = null;
		int selectedScore = int.MinValue;
		foreach (KeyValuePair<string, List<Actor>> pair in pools.ByDaoTu)
		{
			if (string.Equals(pair.Key, teacherDaoTu, StringComparison.Ordinal)) continue;
			XjDaoTuRelationKind relation = XjDaoTuRelationCatalog.Resolve(teacherDaoTu, pair.Key);
			bool matches = directOnly
				? relation == XjDaoTuRelationKind.DirectAdjacent
				: relation == XjDaoTuRelationKind.Counterpart
					|| relation == XjDaoTuRelationKind.SameRootRemote
					|| relation == XjDaoTuRelationKind.ElementAffinity;
			if (!matches) continue;
			Actor candidate = SelectBestStudent(teacher, teacherRealmOrder, pair.Value);
			if (candidate == null) continue;
			int score = ResolveStudentScore(candidate);
			if (selected == null || score > selectedScore
				|| (score == selectedScore && GetActorId(candidate) < GetActorId(selected)))
			{
				selected = candidate;
				selectedScore = score;
			}
		}
		return selected;
	}

	private static Actor SelectBestStudent(
		Actor teacher,
		int teacherRealmOrder,
		IReadOnlyList<Actor> candidates)
	{
		if (candidates == null || candidates.Count == 0) return null;
		Actor selected = null;
		int selectedScore = int.MinValue;
		int checkedCount = 0;
		for (int i = 0; i < candidates.Count && checkedCount < MaxStudentsCheckedPerTeacher; i++)
		{
			Actor candidate = candidates[i];
			if (candidate == teacher || !IsValidStudent(teacherRealmOrder, candidate)) continue;
			checkedCount++;
			int score = ResolveStudentScore(candidate);
			if (selected == null || score > selectedScore
				|| (score == selectedScore && GetActorId(candidate) < GetActorId(selected)))
			{
				selected = candidate;
				selectedScore = score;
			}
		}
		return selected;
	}

	private static StudentPools BuildStudentPools(IReadOnlyList<Actor> actors)
	{
		StudentPools pools = new StudentPools();
		if (actors == null) return pools;
		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data == null) continue;
			pools.All.Add(actor);
			string daoTu = ResolveDaoTu(actor);
			if (daoTu.Length == 0) continue;
			if (!pools.ByDaoTu.TryGetValue(daoTu, out List<Actor> bucket))
			{
				bucket = new List<Actor>();
				pools.ByDaoTu[daoTu] = bucket;
			}
			bucket.Add(actor);
		}
		return pools;
	}

	private static bool IsValidStudent(int teacherRealmOrder, Actor candidate)
	{
		if (!IsValidMentorshipActor(candidate))
		{
			return false;
		}
		if (HasTeacher(candidate))
		{
			if (TryGetTeacher(candidate, out _))
			{
				return false;
			}
			ClearTeacher(candidate);
		}
		int studentRealmOrder = ResolveRealmOrder(candidate);
		return studentRealmOrder > 0 && studentRealmOrder <= MaximumStudentRealmOrder && teacherRealmOrder > studentRealmOrder;
	}

	private static void Recruit(Actor teacher, Actor student, int currentYear)
	{
		if (teacher?.data == null || student?.data == null)
		{
			return;
		}

		long teacherId = ((BaseSystemData)teacher.data).id;
		long studentId = ((BaseSystemData)student.data).id;
		List<long> studentIds = GetStudentIds(teacher);
		if (!studentIds.Contains(studentId))
		{
			studentIds.Add(studentId);
			if (studentIds.Count > MaxStudents)
			{
				studentIds.RemoveRange(MaxStudents, studentIds.Count - MaxStudents);
			}
		}

		XjActorAccessor.SetString(teacher, XjActorDataKeys.XjZongMenStudentIds, JoinIds(studentIds));
		XjActorAccessor.SetInt(teacher, XjActorDataKeys.XjZongMenLastRecruitYear, Math.Max(0, currentYear));
		XjActorAccessor.SetLong(student, XjActorDataKeys.XjZongMenTeacherId, teacherId);
		XjActorAccessor.SetString(student, XjActorDataKeys.XjZongMenTeacherName, SafeName(teacher));
		XjThreeBookWriter.RecordMentorship(teacher, student, currentYear);
	}

	private static void CleanupTeacherStudents(Actor teacher)
	{
		List<long> ids = GetStudentIds(teacher);
		if (ids.Count == 0)
		{
			return;
		}

		bool changed = false;
		long teacherId = GetActorId(teacher);
		int teacherRealmOrder = ResolveRealmOrder(teacher);
		for (int i = ids.Count - 1; i >= 0; i--)
		{
			long studentId = ids[i];
			Actor student = null;
			long recordedTeacherId = 0L;
			bool resolved = XjScheduler.ResolveActor(studentId, out student);
			int studentRealmOrder = resolved ? ResolveRealmOrder(student) : 0;
			bool hasRecordedTeacher = resolved
				&& XjActorAccessor.TryGetLong(student, XjActorDataKeys.XjZongMenTeacherId, out recordedTeacherId);
			bool valid = resolved
				&& IsValidMentorshipActor(student)
				&& studentRealmOrder > 0
				&& studentRealmOrder <= MaximumStudentRealmOrder
				&& teacherRealmOrder > studentRealmOrder
				&& hasRecordedTeacher
				&& recordedTeacherId == teacherId;
			if (valid) continue;
			if (student?.data != null && recordedTeacherId == teacherId) ClearTeacher(student);
			ids.RemoveAt(i);
			changed = true;
		}

		if (changed)
		{
			XjActorAccessor.SetString(teacher, XjActorDataKeys.XjZongMenStudentIds, JoinIds(ids));
		}
	}

	internal static bool TryGetTeacher(Actor student, out Actor teacher)
	{
		teacher = null;
		if (student?.data == null || ResolveRealmOrder(student) > MaximumStudentRealmOrder)
		{
			ClearTeacher(student);
			return false;
		}
		if (!XjActorAccessor.TryGetLong(student, XjActorDataKeys.XjZongMenTeacherId, out long teacherId)
			|| teacherId <= 0L
			|| !XjScheduler.ResolveActor(teacherId, out teacher)
			|| !IsValidMentorshipActor(teacher)
			|| ResolveRealmOrder(teacher) <= ResolveRealmOrder(student))
		{
			ClearTeacher(student);
			teacher = null;
			return false;
		}
		return true;
	}

	private static void ClearTeacher(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjZongMenTeacherId, 0L);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjZongMenTeacherName, string.Empty);
	}

	internal static List<Actor> GetLiveStudents(Actor teacher)
	{
		List<long> ids = GetStudentIds(teacher);
		List<Actor> result = new List<Actor>(ids.Count);
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjScheduler.ResolveActor(ids[i], out Actor student)
				&& IsValidMentorshipActor(student)
				&& ResolveRealmOrder(student) <= MaximumStudentRealmOrder
				&& ResolveRealmOrder(teacher) > ResolveRealmOrder(student))
			{
				result.Add(student);
			}
		}
		return result;
	}

	private static bool HasTeacher(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenTeacherId, out long teacherId)
			&& teacherId > 0L;
	}

	private static List<long> GetStudentIds(Actor teacher)
	{
		List<long> result = new List<long>(MaxStudents);
		if (teacher?.data == null
			|| !XjActorAccessor.TryGetString(teacher, XjActorDataKeys.XjZongMenStudentIds, out string raw)
			|| string.IsNullOrWhiteSpace(raw))
		{
			return result;
		}

		string[] parts = raw.Split(',');
		for (int i = 0; i < parts.Length && result.Count < MaxStudents; i++)
		{
			if (long.TryParse((parts[i] ?? string.Empty).Trim(), out long id) && id > 0L && !result.Contains(id))
			{
				result.Add(id);
			}
		}
		return result;
	}

	private static string JoinIds(IReadOnlyList<long> ids)
	{
		if (ids == null || ids.Count == 0)
		{
			return string.Empty;
		}

		List<string> parts = new List<string>(ids.Count);
		for (int i = 0; i < ids.Count; i++)
		{
			if (ids[i] > 0L) parts.Add(ids[i].ToString(CultureInfo.InvariantCulture));
		}
		return string.Join(",", parts);
	}

	private static bool IsValidMentorshipActor(Actor actor)
	{
		return XjSafeCore.IsAliveActor(actor) && ResolveRealmOrder(actor) > 0;
	}

	private static string ResolveDaoTu(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu))
		{
			return string.Empty;
		}
		return XjDaoTuRelationCatalog.Normalize(daoTu);
	}

	private static int CompareTeacherCandidates(Actor left, Actor right)
	{
		int realm = ResolveRealmOrder(right).CompareTo(ResolveRealmOrder(left));
		if (realm != 0) return realm;
		int aptitude = ResolveAptitude(right).CompareTo(ResolveAptitude(left));
		if (aptitude != 0) return aptitude;
		return GetActorId(left).CompareTo(GetActorId(right));
	}

	private static int ResolveStudentScore(Actor actor)
	{
		return ResolveAptitude(actor) * 100 + ResolveRealmOrder(actor) * 10 - Math.Min(9, Math.Max(0, GetStudentIds(actor).Count));
	}

	private static int ResolveRealmOrder(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjRealmHelper.GetOrder(realmId);
	}

	private static int ResolveAptitude(Actor actor)
	{
		return actor?.data != null && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			? Math.Max(0, aptitude)
			: 0;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string SafeName(Actor actor)
	{
		return string.IsNullOrWhiteSpace(actor?.getName()) ? "无名门人" : actor.getName().Trim();
	}
}
