using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 师徒授法只复用已有宗门功法阁和角色真实功法，不生成卷轴或第二套背包。
/// 无合法功法可传时，留下二十年的参悟助力；服气修士只温养本命核心。
/// </summary>
internal static class XjSectMentorshipTeachingService
{
	private const int BenefitYears = 20;
	private const int GongFaBonusBasisPoints = 600;
	private const int ShenTongBonusBasisPoints = 300;

	internal static bool TryTeach(Actor teacher, Actor student, int currentYear, out string result)
	{
		result = string.Empty;
		if (!XjSafeCore.IsAliveActor(teacher) || !XjSafeCore.IsAliveActor(student) || currentYear <= 0) return false;

		bool borrowed = XjSectGongFaBorrow.TryBorrowForActor(student);
		if (!borrowed) borrowed = TryTransmitLowGradeMethod(teacher, student, currentYear);

		XjActorAccessor.SetInt(student, XjActorDataKeys.XjMentorshipGongFaBonusBasisPoints, GongFaBonusBasisPoints);
		XjActorAccessor.SetInt(student, XjActorDataKeys.XjMentorshipShenTongBonusBasisPoints, ShenTongBonusBasisPoints);
		XjActorAccessor.SetInt(student, XjActorDataKeys.XjMentorshipBonusUntilYear, currentYear + BenefitYears);

		if (XjCultivationPathRules.IsFuQiYangXing(student))
		{
			XjActorAccessor.TryGetInt(student, XjActorDataKeys.FuQiCoreProgress, out int progress);
			XjActorAccessor.SetInt(student, XjActorDataKeys.FuQiCoreProgress, Math.Min(10000, Math.Max(0, progress) + 10));
			result = "本命核心温养";
		}
		else
		{
			result = borrowed ? "授予合法功法" : "留下参悟指点";
		}
		XjSemanticDiagnostics.RecordMentorshipTeaching(result);
		return true;
	}

	internal static float ResolveGongFaBonus(Actor actor, int currentYear)
	{
		if (!TryRead(actor, currentYear, XjActorDataKeys.XjMentorshipGongFaBonusBasisPoints, out int basisPoints)) return 0f;
		return Math.Clamp(Math.Max(0, basisPoints) / 10000f, 0f, 0.10f);
	}

	internal static float ResolveShenTongBonus(Actor actor, int currentYear)
	{
		if (!TryRead(actor, currentYear, XjActorDataKeys.XjMentorshipShenTongBonusBasisPoints, out int basisPoints)) return 0f;
		return Math.Clamp(Math.Max(0, basisPoints) / 10000f, 0f, 0.08f);
	}

	private static bool TryTransmitLowGradeMethod(Actor teacher, Actor student, int currentYear)
	{
		if (!XjCultivationPathRules.IsZiFuJinDan(teacher) || !XjCultivationPathRules.IsZiFuJinDan(student)) return false;
		XjGongFaState source = XjGongFaAccessor.BuildState(teacher);
		XjGongFaState target = XjGongFaAccessor.BuildState(student);
		if (!source.Found || source.Grade <= 0 || source.Grade > 4 || source.Grade <= target.Grade) return false;
		if (!XjActorAccessor.TryGetString(teacher, XjActorDataKeys.DaoTu, out string teacherDaoTu)
			|| !XjActorAccessor.TryGetString(student, XjActorDataKeys.DaoTu, out string studentDaoTu)
			|| !string.Equals((teacherDaoTu ?? string.Empty).Trim(), (studentDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal)
			|| source.Grade > XjGongFaAptitudeRules.GetMaximumAllowedGrade(student)) return false;

		XjGongFaState granted = new XjGongFaState(
			true, source.Name, source.Grade, 0, 0f, studentDaoTu.Trim(), source.Grade > XjGongFaAccessor.MaxActiveGrade, "Mentorship");
		XjGongFaAccessor.WriteState(student, granted);
		XjGongFaAccessor.WriteSource(student, "师门授法");
		XjActorGongFaCollection.UpsertPrimary(student, granted, source: "师门授法");
		XjGongFaProgression.PublishGongFaPromoted(student, granted);
		return true;
	}

	private static bool TryRead(Actor actor, int currentYear, string valueKey, out int basisPoints)
	{
		basisPoints = 0;
		return actor?.data != null
			&& currentYear > 0
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjMentorshipBonusUntilYear, out int untilYear)
			&& untilYear >= currentYear
			&& XjActorAccessor.TryGetInt(actor, valueKey, out basisPoints);
	}
}
