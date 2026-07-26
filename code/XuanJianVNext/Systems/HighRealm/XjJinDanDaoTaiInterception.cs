using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 阴阳两道途的道胎截杀。它是成丹成功后的独立天劫，不与一般求金失败、
/// 金性妖邪或阴司链混用。
/// </summary>
internal static class XjJinDanDaoTaiInterception
{
	private const int InterceptionChancePercent = 5;

	internal static bool TryResolve(Actor actor, string daoTu, int currentYear)
	{
		if (actor?.data == null || !IsSolarOrLunar(daoTu) || !XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		// 道主之资通过全部修炼概率判定；道胎截杀同样属于求金后的随机判定。
		if (actor.hasTrait("ChuShen8"))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || XjDeterministicHash.PositiveIndex(actorId + currentYear, "jindan_daotai_interception", 100) >= InterceptionChancePercent)
		{
			return false;
		}

		string actorName = actor.getName() ?? "此人";
		string daoTaiName = string.Equals(daoTu, "太阳", StringComparison.Ordinal) ? "日中道胎" : "太阴道胎";
		string announcement = "【道胎垂眸】" + daoTaiName + "垂眸金门，万象骤寂。"
			+ actorName + "方欲丹成，神魂便被天外一线清辉照彻，肉身与道基俱散。";

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "DaoTaiInterception");
		bool died = XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)5, true);
		if (!died)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
			return false;
		}

		XjBroadcastSystem.BroadcastSLevelActorEvent(
			actor,
			announcement,
			announcement,
			"#80D8FF",
			10f,
			XjEventIconCatalog.JinDanFail);
		return true;
	}

	private static bool IsSolarOrLunar(string daoTu)
	{
		return string.Equals(daoTu, "太阳", StringComparison.Ordinal)
			|| string.Equals(daoTu, "太阴", StringComparison.Ordinal);
	}
}
