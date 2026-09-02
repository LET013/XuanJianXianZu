using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.YaoShu;

/// <summary>
/// 大圣化生时偶发的一炁遗泽。候选只从已被本模组事件接触过的角色登记册中固定预算取样，
/// 不接管原生生育、种族或文明，也不新增世界人口扫描。
/// </summary>
internal static class XjYaoShuHalfBloodlineSystem
{
	internal const string TraitId = "XjYaoShuHalfBlood";
	// 0.9.8.31 曾错误地把妖明阳注册为可编辑特质。保留旧 ID 仅用于读档清理，
	// 绝不再注册、添加或由其反推道途。
	private const string LegacyYaoMingYangTraitId = "XjYaoMingYangDaoTu";
	private const float BestowalChancePerManifestation = 0.35f;
	private const int CandidateProbeBudget = 96;
	private const int MaximumRecipientsPerManifestation = 10;
	private const int AptitudeFloor = 4;
	private const int YaoMingYangAptitudeFloor = 5;

	internal static int TryBestowAfterGreatSage(Actor greatSage, string sageSlotId, int currentYear)
	{
		if (greatSage?.data == null || !greatSage.isAlive()) return 0;
		long sageId = ((BaseSystemData)greatSage.data).id;
		if (sageId <= 0L || XjDeterministicHash.Roll01(sageId, currentYear, sageSlotId, "half_yao_bloodline") >= BestowalChancePerManifestation)
		{
			return 0;
		}

		IReadOnlyList<Actor> knownActors = XjActorRegistry.Snapshot();
		if (knownActors == null || knownActors.Count == 0) return 0;
		int start = XjDeterministicHash.RollRange(sageId, currentYear, 912, 0, knownActors.Count - 1);
		int targetCount = XjDeterministicHash.RollRange(sageId, currentYear, 913, 1, MaximumRecipientsPerManifestation);
		int probes = Math.Min(CandidateProbeBudget, knownActors.Count);
		int grantedCount = 0;
		for (int probe = 0; probe < probes; probe++)
		{
			Actor candidate = knownActors[(start + probe) % knownActors.Count];
			if (!IsEligibleRecipient(candidate)) continue;
			if (!Grant(candidate)) continue;
			RecordBestowal(candidate, greatSage, currentYear);
			grantedCount++;
			if (grantedCount >= targetCount) break;
		}
		return grantedCount;
	}

	private static bool IsEligibleRecipient(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjYaoShuGreatSageSystem.IsGreatSage(actor)) return false;
		try
		{
			if (actor.hasTrait(TraitId)) return false;
		}
		catch { return false; }
		return IsHuman(actor) || XjYaoShuSapientSpecies.IsYaoMin(actor);
	}

	private static bool IsHuman(Actor actor)
	{
		string assetId = (actor?.asset?.id ?? actor?.data?.asset_id ?? string.Empty).Trim();
		return string.Equals(assetId, "human", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(assetId, "civ_human", StringComparison.OrdinalIgnoreCase);
	}

	private static bool Grant(Actor actor)
	{
		if (AssetManager.traits?.get(TraitId) == null) return false;
		bool granted;
		XjExternalUnitTransferContext.EnterTraitTransfer();
		try
		{
			granted = actor.addTrait(TraitId, false);
		}
		finally
		{
			XjExternalUnitTransferContext.ExitTraitTransfer();
		}
		if (!granted) return false;

		EnsureAptitudeFloor(actor);
		ClearLegacyYaoMingYangTrait(actor);
		if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZhuJi)
		{
			TryAwakenYaoIntentAtZhuJi(actor);
		}
		actor.clearTraitCache();
		actor.setStatsDirty();
		XjActorRegistry.Register(actor, out _);
		return true;
	}

	internal static bool IsYaoIntent(Actor actor)
	{
		if (actor?.data == null || !HasHalfBlood(actor)) return false;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.YaoIntentDaoTu, out string marked)) return false;
		string source = ResolveSourceDaoTu(actor);
		// This method serves display, rank and combat reads. Keep it a pure O(1)
		// marker comparison; mutation happens only in the grant/realm transition.
		return string.Equals(NormalizeDaoTu(marked), source, StringComparison.Ordinal);
	}

	internal static string ResolveDisplayedDaoTu(Actor actor, string fallbackDaoTu)
	{
		string source = NormalizeDaoTu(fallbackDaoTu);
		return IsYaoIntent(actor) ? XjDaoTuIntentIdentity.Compose(XjDaoTuIntentIdentity.YaoPrefix, source) : source;
	}

	internal static void TryAwakenYaoIntentAtZhuJi(Actor actor)
	{
		if (actor?.data == null || !HasHalfBlood(actor)) return;
		string source = ResolveSourceDaoTu(actor);
		if (string.IsNullOrWhiteSpace(source)) return;
		string expected = XjDaoTuIntentIdentity.Compose(XjDaoTuIntentIdentity.YaoPrefix, source);
		if (IsYaoIntent(actor)
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string stored)
			&& string.Equals((stored ?? string.Empty).Trim(), expected, StringComparison.Ordinal))
		{
			return;
		}
		XjActorAccessor.SetString(actor, XjActorDataKeys.YaoIntentDaoTu, source);
		XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, expected);
		XjCultivatorCandidateIndex.RefreshDaoTu(actor);
		EnsureAptitudeFloor(actor);
		ClearLegacyYaoMingYangTrait(actor);
	}

	private static bool HasHalfBlood(Actor actor)
	{
		try { return actor != null && actor.hasTrait(TraitId); }
		catch { return false; }
	}

	private static string ResolveSourceDaoTu(Actor actor)
	{
		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			? XjDaoTuIntentIdentity.ResolveCore(daoTu) : string.Empty;
	}

	private static string NormalizeDaoTu(string value) => (value ?? string.Empty).Trim();

	private static void ClearLegacyYaoMingYangTrait(Actor actor)
	{
		if (actor?.data == null || AssetManager.traits?.get(LegacyYaoMingYangTraitId) == null) return;
		XjExternalUnitTransferContext.EnterTraitTransfer();
		try
		{
			if (actor.hasTrait(LegacyYaoMingYangTraitId)) actor.removeTrait(LegacyYaoMingYangTraitId);
		}
		catch { }
		finally { XjExternalUnitTransferContext.ExitTraitTransfer(); }
	}

	private static void EnsureAptitudeFloor(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int current);
		current = Math.Clamp(current, 0, 6);
		int requiredFloor = string.Equals(ResolveSourceDaoTu(actor), "明阳", StringComparison.Ordinal)
			? YaoMingYangAptitudeFloor : AptitudeFloor;
		if (current < requiredFloor)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, requiredFloor);
			XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, requiredFloor);
			XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(actor, requiredFloor);
		}
		// 资质信息仍由现有修士面板统一显示；半妖血脉自身的说明不暴露资质门槛。
		XjVisibleTraitSync.SyncAptitudeTrait(actor, Math.Max(current, requiredFloor));
		XjCultivatorCache.CheckAndUpdate(actor);
		XjCombatHotPathCache.Refresh(actor);
	}

	private static void RecordBestowal(Actor recipient, Actor greatSage, int currentYear)
	{
		if (recipient?.data == null) return;
		long recipientId = ((BaseSystemData)recipient.data).id;
		if (recipientId <= 0L) return;
		string recipientName = XjStringHelper.ActorName(recipient, "无名修士");
		string sageName = XjStringHelper.ActorName(greatSage, "一尊妖属大圣");
		string body = sageName + "化生之际，一缕妖炁落入" + recipientName + "之身；人妖同源，半妖血脉由此显现。";
		XjThreeBookWriter.RecordHalfYaoBloodline(recipient, sageName, currentYear);
		XjWorldHistoryStore.RecordDomainEvent(XjWorldHistoryCategory.Cultivation, "半妖血脉显现", body,
			importance: 3, isProtected: true, actorId: recipientId, actorName: recipientName,
			year: currentYear, eventType: "YaoShuHalfBloodline", relatedActorId: GetActorId(greatSage),
			relatedActorName: sageName, mirrorToWorldLog: true);
	}

	private static long GetActorId(Actor actor) => actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
}
