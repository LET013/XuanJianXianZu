using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Aptitude;

/// <summary>
/// 资质只描述修炼承载力；需要“被某个玩法选中”的稀有资格统一从资质、道慧、命数
/// 的既有先天快照派生，不创建第二套天赋特质，也不增加年度人口扫描。
/// 释修不使用本策略中的道慧组合，仍只读取释修自己的命数规则。
/// </summary>
internal static class XjTalentOpportunityRules
{
	internal const float DaoTaiGradeSevenMinimumHuiGuang = 80f;
	internal const float Rank4FuQiMinimumHuiGuang = 66f;
	internal const float Rank4FuQiMinimumMingShu = 66f;
	internal const float Rank4FuQiMinimumCombined = 140f;
	internal const float NamelessSwordMinimumHuiGuang = 70f;
	internal const float SwordIntentMinimumHuiGuang = 60f;
	internal const int FamilyProdigyMinimumScore = 158;

	internal static float ReadHuiGuang(Actor actor)
	{
		if (actor?.data == null) return 0f;
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value)
			? Math.Clamp(value, 0f, XjDaoHuiPolicy.Maximum)
			: 0f;
	}

	internal static float ReadMingShu(Actor actor)
	{
		if (actor?.data == null) return 0f;
		XjMingShuState.Normalize(actor);
		// “天资机会”只读取先天命数，避免角色后天积命后反向改写出生时的资质语义。
		if (XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShuCongenital, out float congenital)
			&& congenital > 0f)
		{
			return Math.Clamp(congenital, 0f, XjMingShuState.MaximumCongenital);
		}
		// 只为极旧档保留总命数回退；Normalize 后的新档都会走先天字段。
		return XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float legacyTotal)
			? Math.Clamp(legacyTotal, 0f, XjMingShuState.MaximumCongenital)
			: 0f;
	}

	/// <summary>
	/// 通用“天骄机会分”：先天命数 + 道慧 + 极轻的资质修正。资质不再独占事件资格，
	/// 但高资质仍因基础曲线更高而天然更容易成为候选。
	/// </summary>
	internal static int ResolveOpportunityScore(Actor actor, int aptitude)
	{
		if (actor?.data == null || aptitude < 1 || aptitude > 6) return 0;
		float score = ReadMingShu(actor) + ReadHuiGuang(actor) + aptitude * 2f;
		return Math.Max(0, (int)Math.Round(score));
	}


	/// <summary>
	/// 家族长期培养使用“出生时的稳定天赋分”：先天命数 + 资质对应的道慧曲线值 + 轻量资质修正。
	/// 不读取洞天、点化、丹法等后天增长后的道慧，避免一个已被培养多年的人因为后天收益
	/// 反过来改变“谁天赋更好”的比较基准。
	/// </summary>
	internal static int ResolveFamilySupportTalentScore(Actor actor, int aptitude)
	{
		if (actor?.data == null || aptitude < 1 || aptitude > 6
			|| !XjDaoHuiPolicy.TryGetAptitudeRange(aptitude, out int huiGuangMin, out int huiGuangMax))
		{
			return 0;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		float curveHuiGuang = XjDeterministicHash.RollRange(actorId, aptitude, 709, huiGuangMin, huiGuangMax);
		float score = ReadMingShu(actor) + curveHuiGuang + aptitude * 2f;
		return Math.Max(0, (int)Math.Round(score));
	}

	internal static int ResolveLuoXiaRecruitBasisPoints(Actor actor, int aptitude, bool isXueSurname)
	{
		if (actor?.data == null || aptitude < 4 || aptitude > 6) return 0;
		int score = ResolveOpportunityScore(actor, aptitude);
		if (isXueSurname)
		{
			// 薛氏余脉是落霞最稳定的传承来源：高天资者通常能被山门看中，但仍保留少量落选。
			return Math.Clamp(3000 + Math.Max(0, score - 115) * 70, 3000, 9000);
		}
		// 落霞山显世后必须真实形成道统人口。普通四至六档从约3%起，
		// 命数与道慧越高越易入门，上限18%；只在五岁事务判一次，不增加世界扫描。
		return Math.Clamp(300 + Math.Max(0, score - 125) * 20, 300, 1800);
	}

	internal static bool IsExceptionalRank4FuQiTalent(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			|| aptitude != 4) return false;
		float huiGuang = ReadHuiGuang(actor);
		float mingShu = ReadMingShu(actor);
		return huiGuang >= Rank4FuQiMinimumHuiGuang
			&& mingShu >= Rank4FuQiMinimumMingShu
			&& huiGuang + mingShu >= Rank4FuQiMinimumCombined;
	}

	internal static bool CanEnterNamelessSwordPath(Actor actor, int aptitude)
	{
		if (actor?.data == null || aptitude < 4 || aptitude > 6) return false;
		float huiGuang = ReadHuiGuang(actor);
		if (huiGuang < NamelessSwordMinimumHuiGuang) return false;
		return aptitude >= 5 || IsExceptionalRank4FuQiTalent(actor);
	}

	internal static bool CanComprehendSwordIntent(Actor actor)
	{
		// 剑意不再把 XjZz4/5/6 当作许可证。剑艺熟练度由器艺系统自身负责，
		// 此处只约束悟性；天然高资质仍因道慧曲线更高而更容易满足。
		return actor?.data != null && ReadHuiGuang(actor) >= SwordIntentMinimumHuiGuang;
	}

	internal static bool CanBorrowDaoTaiGradeSeven(Actor actor)
	{
		return actor?.data != null && ReadHuiGuang(actor) >= DaoTaiGradeSevenMinimumHuiGuang;
	}

	internal static bool IsFamilyProdigy(Actor actor, out int score)
	{
		score = 0;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
			|| aptitude < 4 || aptitude > 6) return false;
		score = ResolveFamilySupportTalentScore(actor, aptitude);
		return score >= FamilyProdigyMinimumScore;
	}
}
