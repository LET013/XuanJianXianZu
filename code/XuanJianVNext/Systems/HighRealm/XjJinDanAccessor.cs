using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;
internal static class XjJinDanAccessor
{
	internal static XjJinDanState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjJinDanState(false, string.Empty, string.Empty, string.Empty, 0, 0, "ActorInvalid");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (XjJinXingCalculator.NeedsDaoTuNormalization(jinXing, daoTu))
		{
			long actorId = ((BaseSystemData)actor.data).id;
			string normalizedJinXing = XjJinXingCalculator.Calculate(daoTu, actorId);
			if (!string.IsNullOrWhiteSpace(normalizedJinXing))
			{
				jinXing = normalizedJinXing;
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, normalizedJinXing);
			}
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, out int lastAttemptYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);

		bool found = !string.IsNullOrWhiteSpace(jinXing) && !string.IsNullOrWhiteSpace(guoWei) && successYear > 0;
		return new XjJinDanState(
			found,
			jinXing,
			guoWei,
			failedState,
			lastAttemptYear,
			successYear,
			found ? "Ok" : "NoJinDan");
	}

	internal static void WriteSuccess(Actor actor, string jinXing, string guoWei, int currentYear)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(jinXing)
			|| string.IsNullOrWhiteSpace(guoWei))
		{
			return;
		}

		int safeYear = currentYear < 0 ? 0 : currentYear;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, jinXing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, guoWei);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, safeYear);
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang) || yiXiang <= 0)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang,
				XjDeterministicHash.PositiveIndex(actorId + safeYear, "jindan_yixiang", 300) + 1);
		}
	}

	internal static void WriteFailure(Actor actor, string failedState, int currentYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(failedState))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, failedState);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, currentYear < 0 ? 0 : currentYear);
	}

	internal static void ClearSuccess(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		// Realm rollback writes the target RealmId before clearing the old high-realm
		// payload. BuildState would therefore report NoJinDan and leak the old GuoWei
		// claim. Read the persisted claim directly instead.
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string persistedGuoWei);
		long actorId = ((BaseSystemData)actor.data).id;
		if (!string.IsNullOrWhiteSpace(persistedGuoWei) && actorId > 0L)
		{
			XjGuoWeiRegistry.ReleaseForActor(actorId, persistedGuoWei.Trim());
		}
		if (actorId > 0L)
		{
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjJieLinXianRegistry.Release(actorId);
		}
		XjGuoWeiQuanBingActorSnapshot.Clear(actor);

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessEventSchema, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXian, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXianYear, 0);
	}

	internal static void ClearFailure(Actor actor)
	{
		if (actor?.data != null)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		}
	}
}

