using System;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 果位钟爱只锁定“已经真正持位”的活跃旧主，不再把转世身的下一世道途
/// 永久钉死。钟爱正位陨落后，原果仍按锁期静待真灵；转世身大概率沿旧道、
/// 小概率另择道途。只有重新证回本途正位后，活跃果位才再次成为道途硬锁。
/// </summary>
internal static class XjGuoWeiFavoredDaoTuLock
{
	private const string FavoredReincarnationMode = "GuoWeiZhongAi";
	private const string PendingReincarnatedZhengWeiStatus = "PendingReincarnatedZhengWei";

	internal static bool TryResolveLockedDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		// 注册表是权威来源。PendingReincarnatedZhengWei 只代表“旧果正在等这个真灵”，
		// 不是说这个新肉身已经重新持果，更不能因此禁止它小概率改修。
		if (XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState registryState)
			&& registryState.Found)
		{
			if (string.Equals(registryState.LifecycleStatus, PendingReincarnatedZhengWeiStatus, StringComparison.Ordinal))
			{
				return false;
			}
			if (!string.IsNullOrWhiteSpace(registryState.GuoWeiZhongAi)
				&& TryNormalize(actorId, registryState.DaoTu, out daoTu))
			{
				return true;
			}
		}

		// 旧角色快照可能把“等待归位”投影成看似活跃的钟爱键。
		// 只要仍带果位钟爱转世来源、且尚未真正成丹，就明确视为 pending，不读这些
		// 旧投影键做道途锁。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationMode, out string reincarnationMode);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationGuoWeiZhongAi, out string reincarnationFavored);
		bool isFavoredReincarnation = string.Equals((reincarnationMode ?? string.Empty).Trim(), FavoredReincarnationMode, StringComparison.Ordinal)
			|| !string.IsNullOrWhiteSpace(reincarnationFavored);
		if (isFavoredReincarnation && !XjJinDanAccessor.BuildPositionCarrierState(actor).Found)
		{
			XjJinDanAccessor.ClearPendingReincarnationActiveProjection(actor);
			XjGuoWeiQuanBingActorSnapshot.Clear(actor);
			return false;
		}

		// 活跃持位者正常都会把钟爱与道途投影到角色数据；只有已经成丹的角色才
		// 接受这份投影，避免旧档 pending 快照把转世身重新钉回前世道途。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, out string activeFavored);
		if (!string.IsNullOrWhiteSpace(activeFavored)
			&& XjJinDanAccessor.BuildState(actor).Found
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQuanBingDaoTu, out string activeDaoTu)
			&& TryNormalize(actorId, activeDaoTu, out daoTu))
		{
			return true;
		}

		// 旧档中角色投影键缺失时，仍允许真正的活跃正位持有者从注册表/正位键恢复。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string storedGuoWei);
		if (!isFavoredReincarnation
			&& XjJinDanAccessor.BuildState(actor).Found
			&& !string.IsNullOrWhiteSpace(storedGuoWei)
			&& storedGuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			&& XjGuoWeiQuanBingRegistry.TryGetHistorical(actorId, out XjGuoWeiQuanBingState historical)
			&& historical.Found
			&& !string.IsNullOrWhiteSpace(historical.GuoWeiZhongAi)
			&& !string.Equals(historical.LifecycleStatus, PendingReincarnatedZhengWeiStatus, StringComparison.Ordinal)
			&& TryNormalize(actorId, historical.DaoTu, out daoTu))
		{
			return true;
		}

		daoTu = string.Empty;
		return false;
	}

	internal static bool ReconcileActor(Actor actor, bool syncVisibleTraits)
	{
		if (!TryResolveLockedDaoTu(actor, out string lockedDaoTu))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
		long actorId = ((BaseSystemData)actor.data).id;
		if (TryNormalize(actorId, currentDaoTu, out string normalizedCurrent)
			&& string.Equals(normalizedCurrent, lockedDaoTu, StringComparison.Ordinal))
		{
			return false;
		}

		return XjCultivationStateTransitions.TrySetDaoTu(actor, lockedDaoTu, syncVisibleTraits);
	}

	internal static string ResolveRequestedDaoTu(Actor actor, string requestedDaoTu, out bool locked)
	{
		locked = TryResolveLockedDaoTu(actor, out string lockedDaoTu);
		return locked ? lockedDaoTu : (requestedDaoTu ?? string.Empty).Trim();
	}

	private static bool TryNormalize(long actorId, string rawDaoTu, out string normalized)
	{
		normalized = string.Empty;
		return !string.IsNullOrWhiteSpace(rawDaoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveCanonicalDisplayName(rawDaoTu.Trim(), actorId, out normalized)
			&& !string.IsNullOrWhiteSpace(normalized)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _);
	}
}
