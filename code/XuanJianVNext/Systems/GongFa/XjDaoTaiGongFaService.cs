using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.GongFa;

internal static class XjDaoTaiGongFaService
{
	internal const int DaoTaiGrade = 7;
	internal const float BreakthroughChanceBonus = 0.08f;
	internal const float ShenTongChanceBonus = 0.10f;

	internal static bool HasGradeSeven(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade)
			&& grade >= DaoTaiGrade;
	}

	internal static bool CanBorrowGradeSeven(Actor actor)
	{
		// 能走到这里已经由家族传承/宗门经阁调用者证明“有法可借”；
		// 本层只判断角色是否有足够道慧读懂七品法，不再把 XjZz5/6 当玩法许可证。
		return XjTalentOpportunityRules.CanBorrowDaoTaiGradeSeven(actor);
	}

	internal static float ResolveShenTongBonus(Actor actor)
	{
		return HasGradeSeven(actor) ? ShenTongChanceBonus : 0f;
	}

	internal static bool EnsureGradeSeven(Actor actor, int currentYear)
	{
		if (actor?.data == null) return false;
		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		string daoTu = current.Found && !string.IsNullOrWhiteSpace(current.DaoTu)
			? current.DaoTu
			: ResolveActorDaoTu(actor);
		if (string.IsNullOrWhiteSpace(daoTu)) return false;
		if (current.Found && current.Grade >= DaoTaiGrade) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		string name = current.Found && !string.IsNullOrWhiteSpace(current.Name)
			? XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, DaoTaiGrade)
			: XjGongFaNameLibrary.GenerateName(daoTu, DaoTaiGrade, actorId + 7007L);
		if (string.IsNullOrWhiteSpace(name)) return false;

		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true,
			name,
			DaoTaiGrade,
			0,
			0f,
			daoTu,
			true,
			"DaoTaiGradeSeven"));
		XjGongFaAccessor.WriteSource(actor, "道胎功法蜕变");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, Math.Max(0, currentYear));
		XjGongFaProgression.PublishGongFaPromoted(actor, XjGongFaAccessor.BuildState(actor));
		return true;
	}

	private static string ResolveActorDaoTu(Actor actor)
	{
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string gongFaDaoTu)
			&& !string.IsNullOrWhiteSpace(gongFaDaoTu))
		{
			return gongFaDaoTu.Trim();
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& !string.IsNullOrWhiteSpace(daoTu))
		{
			return daoTu.Trim();
		}
		return string.Empty;
	}
}
