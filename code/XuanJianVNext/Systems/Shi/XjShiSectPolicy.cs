using System;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Shi;

/// <summary>释修不属于紫金／服气的仙修宗门体系。入释与旧档对账均通过这一入口解除仙修宗门权威关系；古释寺庙另由轻量道场系统维护。</summary>
internal static class XjShiSectPolicy
{
	internal static bool IsSectForbidden(Actor actor)
	{
		return actor?.data != null && XjCultivationPathRules.IsShi(actor);
	}

	internal static bool EnforceDetached(Actor actor, int currentYear)
	{
		if (!IsSectForbidden(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		bool changed = false;
		if (XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			&& member != null && member.SectId > 0L)
		{
			changed = XjSectCommands.RemoveMember(member.SectId, actorId, Math.Max(0, currentYear));
		}
		// 释修已经脱离仙修宗门，就不应继续携带宗门闭关的运行时无敌/悬浮状态。
		// 这也是摩诃“长期显示闭关、不再外出”的旧状态来源之一。
		if (XjClosedCultivationGuard.IsInClosedCultivation(actor))
		{
			XjClosedCultivationGuard.MarkClosedCultivation(actor, false);
			changed = true;
		}
		// 旧档可能只残留actor镜像而没有权威成员行，仍必须清空UI与原生投影。
		XjSectProjection.ClearActor(actorId);
		return changed;
	}
}
