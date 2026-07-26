using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.ZongMen;

internal static class XjZongMenMentorshipSystem
{
	private const int MaxStudents = 3;
	private const int RecruitIntervalYears = 10;
	private const int MinimumTeacherRealmOrder = 3;
	private const int MaxStudentsCheckedPerTeacher = 12;
	private const int MaximumStudentRealmOrder = 3;

	internal static void TickCity(City city, IReadOnlyList<long> actorIds, int currentYear)
	{
		if (city?.data == null || actorIds == null || actorIds.Count < 2)
		{
			return;
		}

		List<Actor> actors = new List<Actor>(actorIds.Count);
		for (int i = 0; i < actorIds.Count; i++)
		{
			if (XjScheduler.ResolveActor(actorIds[i], out Actor actor) && IsValidSectMember(actor))
			{
				actors.Add(actor);
			}
		}

		if (actors.Count < 2)
		{
			return;
		}

		actors.Sort(CompareTeacherCandidates);
		for (int i = 0; i < actors.Count; i++)
		{
			Actor teacher = actors[i];
			CleanupTeacherStudents(teacher);
			if (!CanRecruit(teacher, currentYear))
			{
				continue;
			}

			Actor student = FindStudent(teacher, actors);
			if (student != null)
			{
				Recruit(teacher, student, currentYear);
			}
		}
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
		if (!IsValidSectMember(teacher) || ResolveRealmOrder(teacher) < MinimumTeacherRealmOrder)
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

	private static Actor FindStudent(Actor teacher, IReadOnlyList<Actor> actors)
	{
		XjZongMenIdentitySnapshot teacherSect = XjZongMenAccessor.BuildIdentity(teacher);
		int teacherRealmOrder = ResolveRealmOrder(teacher);
		Actor selected = null;
		int selectedScore = int.MinValue;
		int checkedCount = 0;
		for (int i = 0; i < actors.Count && checkedCount < MaxStudentsCheckedPerTeacher; i++)
		{
			Actor candidate = actors[i];
			if (candidate == teacher || !IsValidStudent(teacherSect, teacherRealmOrder, candidate))
			{
				continue;
			}
			checkedCount++;
			int score = ResolveStudentScore(candidate);
			if (selected == null || score > selectedScore)
			{
				selected = candidate;
				selectedScore = score;
			}
		}
		return selected;
	}

	private static bool IsValidStudent(in XjZongMenIdentitySnapshot teacherSect, int teacherRealmOrder, Actor candidate)
	{
		if (!IsValidSectMember(candidate))
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
		XjZongMenIdentitySnapshot studentSect = XjZongMenAccessor.BuildIdentity(candidate);
		if (!studentSect.Found || studentSect.ZongMenId != teacherSect.ZongMenId)
		{
			return false;
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

		XjZongMenIdentitySnapshot teacherSect = XjZongMenAccessor.BuildIdentity(teacher);
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
				&& IsValidSectMember(student)
				&& IsSameSect(teacherSect, student)
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
			|| !IsValidSectMember(teacher)
			|| ResolveRealmOrder(teacher) <= ResolveRealmOrder(student)
			|| !IsSameSect(XjZongMenAccessor.BuildIdentity(student), teacher))
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
		XjZongMenIdentitySnapshot teacherSect = XjZongMenAccessor.BuildIdentity(teacher);
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjScheduler.ResolveActor(ids[i], out Actor student)
				&& IsValidSectMember(student)
				&& ResolveRealmOrder(student) <= MaximumStudentRealmOrder
				&& ResolveRealmOrder(teacher) > ResolveRealmOrder(student)
				&& IsSameSect(teacherSect, student))
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

	private static bool IsValidSectMember(Actor actor)
	{
		return XjSafeCore.IsAliveActor(actor) && XjZongMenAccessor.BuildIdentity(actor).Found;
	}

	private static bool IsSameSect(in XjZongMenIdentitySnapshot sect, Actor actor)
	{
		XjZongMenIdentitySnapshot other = XjZongMenAccessor.BuildIdentity(actor);
		return sect.Found && other.Found && sect.ZongMenId == other.ZongMenId;
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
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return 5;
			if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return 4;
			if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return 3;
			if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return 2;
			if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)) return 1;
		}
		if (actor.hasTrait(XjRealmIds.JinDan)) return 5;
		if (actor.hasTrait(XjRealmIds.ZiFu)) return 4;
		if (actor.hasTrait(XjRealmIds.ZhuJi)) return 3;
		if (actor.hasTrait(XjRealmIds.LianQi)) return 2;
		if (actor.hasTrait(XjRealmIds.TaiXi)) return 1;
		return 0;
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
