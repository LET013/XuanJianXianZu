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
		return BuildStateWithoutDaoMigration(actor);
	}

	/// <summary>
	/// 读档安全修复仅允许补“已经可证明存在”的成功年份：角色必须仍处于金丹等效境界，
	/// 且金性、果位字段均已持久存在；只有 XjJinDanSuccessYear 丢失时，才用同一角色
	/// 已持久化的 RealmEnteredYear 恢复。绝不推断/新造金性、果位或道途。
	/// </summary>
	internal static bool TryRepairMissingSuccessYearFromPersistedRealm(Actor actor)
	{
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (!XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
		if (string.IsNullOrWhiteSpace(jinXing) || string.IsNullOrWhiteSpace(guoWei)) return false;

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);
		if (successYear > 0) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmEnteredYear)
			|| realmEnteredYear <= 0) return false;

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, realmEnteredYear);
		return true;
	}

	// 高境道统旧档迁移需要读取原始金丹载荷，不能在读取过程中再次触发迁移。
	internal static XjJinDanState BuildStateWithoutDaoMigration(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjJinDanState(false, string.Empty, string.Empty, string.Empty, 0, 0, "ActorInvalid");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		NormalizeIdentityMetadata(actor, daoTu, ref jinXing, ref guoWei, persist: false);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, out int lastAttemptYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);

		bool found = XjCultivationPathRules.IsJinDanEquivalentRealm(realmId)
			&& !string.IsNullOrWhiteSpace(jinXing)
			&& !string.IsNullOrWhiteSpace(guoWei)
			&& successYear > 0;
		return new XjJinDanState(
			found,
			jinXing,
			guoWei,
			failedState,
			lastAttemptYear,
			successYear,
			found ? "Ok" : "NoJinDan");
	}

	/// <summary>
	/// 旧档金性/果位别名的显式迁移入口。BuildState/BuildPositionCarrierState 保持纯读取；
	/// 只有现有年度维护车道可以调用本方法持久化规范名，避免 UI、战斗或 Codex 读取
	/// 在不知情的情况下反向改写业务状态。
	/// </summary>
	internal static bool ReconcileStoredIdentityAliases(Actor actor)
	{
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		return NormalizeIdentityMetadata(actor, daoTu, ref jinXing, ref guoWei, persist: true);
	}

	/// <summary>
	/// 转世待归位只保留专用 reincarnation/registry 数据，不得在新肉身上伪装成活跃持位者。
	/// 本方法只清角色侧的金丹果位投影，不释放世界果位注册表中的 pending 归属。
	/// </summary>
	internal static bool ClearPendingReincarnationActiveProjection(Actor actor)
	{
		if (actor?.data == null) return false;

		// Pending reincarnation metadata lives in the reincarnation record/QuanBing Registry.
		// A low-realm new body must not carry active JinDan payload from the former body,
		// otherwise combat, forging and small-realm readers can mistake "waiting for the old
		// fruit" for "already holding the fruit". Do not release world registries here.
		bool changed = false;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing)
			&& !string.IsNullOrWhiteSpace(jinXing))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
			changed = true;
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei)
			&& !string.IsNullOrWhiteSpace(guoWei))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
			changed = true;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear)
			&& successYear > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, 0);
			changed = true;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int compatibilityProgress)
			&& compatibilityProgress > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
			changed = true;
		}
		return changed;
	}

	private static bool NormalizeIdentityMetadata(Actor actor, string daoTu, ref string jinXing, ref string guoWei, bool persist)
	{
		if (actor?.data == null) return false;
		string originalJinXing = jinXing ?? string.Empty;
		string originalGuoWei = guoWei ?? string.Empty;
		bool personalizedRunJinXing = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei)
			.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(jinXing)
			&& jinXing.Contains("闰", StringComparison.Ordinal)
			&& jinXing.EndsWith("性", StringComparison.Ordinal);
		bool hasDoctrineJinXing = personalizedRunJinXing
			|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmDaoSchema, out int daoSchema)
				&& daoSchema >= XjHighRealmDaoStateService.CurrentSchemaVersion
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingPositionType, out string storedPositionType)
				&& string.Equals(storedPositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(jinXing));

		long actorId = ((BaseSystemData)actor.data).id;
		if (!hasDoctrineJinXing && XjJinXingCalculator.NeedsDaoTuNormalization(jinXing, daoTu, actorId))
		{
			string normalizedJinXing = XjJinXingCalculator.Calculate(daoTu, actorId);
			if (!string.IsNullOrWhiteSpace(normalizedJinXing)) jinXing = normalizedJinXing;
		}

		string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		if (normalizedGuoWei.Length > 0) guoWei = normalizedGuoWei;

		string normalizedJinXingName = hasDoctrineJinXing
			? (jinXing ?? string.Empty).Trim()
			: XjJinXingNamePolicy.NormalizeLegacyName(jinXing);
		if (string.Equals(daoTu?.Trim(), "兑金", StringComparison.Ordinal)
			&& string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(guoWei), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			normalizedJinXingName = XjJinXingNamePolicy.CanonicalDuiJinFruitName;
		}
		if (!hasDoctrineJinXing)
		{
			normalizedJinXingName = XjFruitPositionWorldState.ResolveJinXingName(guoWei, normalizedJinXingName);
		}
		if (normalizedJinXingName.Length > 0) jinXing = normalizedJinXingName;

		bool jinXingChanged = !string.Equals(originalJinXing, jinXing ?? string.Empty, StringComparison.Ordinal);
		bool guoWeiChanged = !string.Equals(originalGuoWei, guoWei ?? string.Empty, StringComparison.Ordinal);
		if (persist)
		{
			if (jinXingChanged) XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, jinXing ?? string.Empty);
			if (guoWeiChanged) XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, guoWei ?? string.Empty);
		}
		return jinXingChanged || guoWeiChanged;
	}

	/// <summary>
	/// 读取“仍承载原金丹果位账本”的高境状态。BuildState 继续只代表金丹/真君羽士，
	/// 本方法额外允许道胎/服气道胎读取晋升前保留下来的金性、果位与证位年份，
	/// 避免把道胎重新送回金丹业务状态机。
	/// </summary>
	internal static XjJinDanState BuildPositionCarrierState(Actor actor)
	{
		XjJinDanState state = BuildStateWithoutDaoMigration(actor);
		if (state.Found || actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return state;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanFailedState, out string failedState);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, out int lastAttemptYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int successYear);
		if (successYear <= 0
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmEnteredYear, out int realmEnteredYear)
			&& realmEnteredYear > 0)
		{
			successYear = realmEnteredYear;
		}
		jinXing = XjJinXingNamePolicy.NormalizeLegacyName(jinXing);
		guoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		bool found = !string.IsNullOrWhiteSpace(jinXing)
			&& !string.IsNullOrWhiteSpace(guoWei)
			&& successYear > 0;
		return new XjJinDanState(
			found, jinXing, guoWei, failedState, lastAttemptYear, successYear,
			found ? "DaoTaiPositionCarrier" : state.ReasonCode);
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
		string normalizedGuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		string suppliedJinXing = (jinXing ?? string.Empty).Trim();
		bool doctrineRunJinXing = normalizedGuoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& suppliedJinXing.Contains("闰", StringComparison.Ordinal)
			&& suppliedJinXing.EndsWith("性", StringComparison.Ordinal);
		string normalizedJinXing = doctrineRunJinXing
			? suppliedJinXing
			: XjFruitPositionWorldState.ResolveJinXingName(
				normalizedGuoWei,
				XjJinXingNamePolicy.NormalizeLegacyName(jinXing));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, normalizedJinXing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, normalizedGuoWei);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, safeYear);
		// 求金之志只属于紫府大真人。真实成丹后立即清掉，避免回退/编辑境界时读到陈旧决意。
		XjQiuJinIntentSystem.Clear(actor);
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

	/// <summary>
	/// 结璘/郁仪仍借用金性与成功年展示，但并不持有金丹果位。
	/// 这些兼容字段只能由金丹载荷入口写，特殊仙事务不得各自拼一套半状态。
	/// </summary>
	internal static void WriteSpecialXianPayload(Actor actor, string jinXing, int year)
	{
		if (actor?.data == null) return;
		int safeYear = Math.Max(0, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, (jinXing ?? string.Empty).Trim());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, safeYear);
		XjQiuJinIntentSystem.Clear(actor);
	}

	internal static void ClearSpecialXianPayload(Actor actor, bool clearYiXiang = true)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDeferredReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, 0);
		if (clearYiXiang) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
		XjQiuJinIntentSystem.Clear(actor);
	}

	internal static void ClearActivePayloadForShenDanPromotion(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string persistedGuoWei);
		if (actorId > 0L && !string.IsNullOrWhiteSpace(persistedGuoWei))
		{
			XjGuoWeiRegistry.ReleaseActiveForPromotion(actorId, persistedGuoWei.Trim(), Math.Max(1, currentYear));
		}
		if (actorId > 0L)
		{
			XjGuoWeiQuanBingRegistry.RemoveActor(actorId);
			XjJieLinXianRegistry.Release(actorId);
			XjYuYiXianRegistry.Release(actorId);
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
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanPromotionAnnouncementYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXian, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXianYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYuYiXian, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYuYiXianYear, 0);
		XjQiuJinIntentSystem.Clear(actor);
		ReleaseDynamicDaoState(actor, actorId, persistedGuoWei);
		XjHighRealmDaoStateService.Clear(actor);
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
			XjYuYiXianRegistry.Release(actorId);
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
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanPromotionAnnouncementYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXian, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXianYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYuYiXian, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYuYiXianYear, 0);
		XjQiuJinIntentSystem.Clear(actor);
		ReleaseDynamicDaoState(actor, actorId, persistedGuoWei);
		XjHighRealmDaoStateService.Clear(actor);
	}

	private static void ReleaseDynamicDaoState(Actor actor, long actorId, string guoWei)
	{
		if (actor?.data == null || actorId <= 0L) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string daoTu);
		// 清除高境载荷必须释放该角色在所有根道/显道记录中的实际执柄，
		// 不能只按当前面板范围过滤，否则嬗移变后的旧权柄会成为幽灵占位。
		XjDaoLineageStateRegistry.OnHolderReleased(actorId, daoTu, guoWei, string.Empty,
			World.world?.map_stats?.year ?? 0, penalizeVitality: false);
	}

	internal static void ClearFailure(Actor actor)
	{
		if (actor?.data != null)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		}
	}
}
