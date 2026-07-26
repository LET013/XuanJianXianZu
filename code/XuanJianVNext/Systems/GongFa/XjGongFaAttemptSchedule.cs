using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.GongFa;

/// <summary>
/// 功法品级尝试的统一实体检测入口。品级间隔与旧哈希相位均由 XjDetectionGate 持有，
/// 年度角色流水线和功法领域逻辑不再各自维护一套取模判断。
/// </summary>
internal static class XjGongFaAttemptSchedule
{
	internal static bool IsDue(Actor actor, int targetGrade, int currentYear)
	{
		if (targetGrade <= 3) return true;
		if (actor?.data == null || currentYear < 0) return false;

		XjEntityDetectionJob job;
		switch (targetGrade)
		{
			case 4:
				job = XjEntityDetectionJob.GongFaGrade4Attempt;
				break;
			case 5:
				job = XjEntityDetectionJob.GongFaGrade5Attempt;
				break;
			case 6:
				job = XjEntityDetectionJob.GongFaGrade6Attempt;
				break;
			default:
				return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		return XjDetectionGate.IsEntityMaintenanceSlot(job, actorId, currentYear);
	}
}
