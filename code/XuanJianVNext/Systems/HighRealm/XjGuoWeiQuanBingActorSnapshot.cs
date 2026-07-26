using System;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 金丹权柄的角色原生存档镜像。
/// 世界档案仍是完整账本；此镜像只为活体金丹提供第二持久来源，
/// 防止读档阶段运行表尚未恢复或档案活动态被错误降级时，角色信息与权柄运行态丢失。
/// </summary>
internal static class XjGuoWeiQuanBingActorSnapshot
{
	private const int CurrentSchema = 1;

	internal static void WriteActive(Actor actor, in XjGuoWeiQuanBingState state)
	{
		if (actor?.data == null || !state.Found || state.ActorId <= 0L)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId != state.ActorId)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingSnapshotSchema, CurrentSchema);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingDaoTu, state.DaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingGuoWei, state.GuoWei);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingLocal, state.LocalQuanBing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSeized, state.SeizedQuanBing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSeizedSources, state.SeizedQuanBingSources);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingForeign, state.ForeignQuanBing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingWithdrawnToDongTian, state.WithdrawnToDongTian);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, state.GuoWeiZhongAi);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingPendingExternalZhengWeiDaoTu, state.PendingExternalZhengWeiDaoTu);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingLockUntilYear, Math.Max(0, state.LockUntilYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatActive, state.IntegrationRetreatActive ? 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatEndYear, Math.Max(0, state.IntegrationRetreatEndYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSummary, state.Summary);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingAcquiredYear, Math.Max(0, state.AcquiredYear));
	}

	internal static bool TryReadActive(Actor actor, out XjGuoWeiQuanBingState state)
	{
		state = default;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingSnapshotSchema, out int schema)
			|| schema <= 0)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		ReadString(actor, XjActorDataKeys.XjQuanBingDaoTu, out string daoTu);
		ReadString(actor, XjActorDataKeys.XjQuanBingGuoWei, out string guoWei);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			ReadString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}
		if (string.IsNullOrWhiteSpace(guoWei))
		{
			ReadString(actor, XjActorDataKeys.XjJinDanGuoWei, out guoWei);
		}

		ReadString(actor, XjActorDataKeys.XjQuanBingLocal, out string local);
		ReadString(actor, XjActorDataKeys.XjQuanBingSeized, out string seized);
		ReadString(actor, XjActorDataKeys.XjQuanBingSeizedSources, out string seizedSources);
		ReadString(actor, XjActorDataKeys.XjQuanBingForeign, out string foreign);
		ReadString(actor, XjActorDataKeys.XjQuanBingWithdrawnToDongTian, out string withdrawn);
		ReadString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, out string favored);
		ReadString(actor, XjActorDataKeys.XjQuanBingPendingExternalZhengWeiDaoTu, out string pendingDaoTu);
		ReadString(actor, XjActorDataKeys.XjQuanBingSummary, out string summary);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingLockUntilYear, out int lockUntilYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatActive, out int retreatActive);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatEndYear, out int retreatEndYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQuanBingAcquiredYear, out int acquiredYear);
		if (acquiredYear <= 0)
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out acquiredYear);
		}

		state = new XjGuoWeiQuanBingState(
			true,
			actorId,
			actor.getName(),
			daoTu,
			guoWei,
			local,
			seized,
			foreign,
			withdrawn,
			favored,
			pendingDaoTu,
			Math.Max(0, lockUntilYear),
			retreatActive > 0,
			Math.Max(0, retreatEndYear),
			string.IsNullOrWhiteSpace(summary) ? "角色存档恢复权柄" : summary,
			"Active",
			Math.Max(0, acquiredYear),
			0,
			string.Empty,
			seizedSources);
		return true;
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingSnapshotSchema, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingGuoWei, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingLocal, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSeized, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSeizedSources, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingForeign, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingWithdrawnToDongTian, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingPendingExternalZhengWeiDaoTu, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingLockUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatActive, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingIntegrationRetreatEndYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQuanBingSummary, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingAcquiredYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQuanBingWarPeakObserved, 0);
	}

	private static void ReadString(Actor actor, string key, out string value)
	{
		if (!XjActorAccessor.TryGetString(actor, key, out value))
		{
			value = string.Empty;
		}
	}
}
