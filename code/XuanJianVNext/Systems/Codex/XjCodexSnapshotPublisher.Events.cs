using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Doctrine;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Doctrine;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
private static List<XjCodexJinDanItem> BuildJinDan(
		IReadOnlyList<XjJinDanImmortalityArchiveRecord> records,
		IReadOnlyDictionary<long, string> familyNames,
		IReadOnlyDictionary<long, XjYinSiMissionArchiveRecord> activeMissionByTarget,
		IReadOnlyDictionary<long, XjSecretRealmArchiveRecord> secretRealmBySittingActor,
		int currentYear)
	{
		Dictionary<long, XjCodexJinDanItem> byActor = new Dictionary<long, XjCodexJinDanItem>();
		for (int i = 0; i < records.Count; i++)
		{
			XjJinDanImmortalityArchiveRecord record = records[i];
			if (record == null || record.ActorId <= 0L) continue;
			if (record.IsAlive)
			{
				if (!XjScheduler.ResolveActor(record.ActorId, out Actor actor)) continue;
				TryAddJinDanItem(byActor, record.ActorId, actor, record.ActivatedYear, record, familyNames, activeMissionByTarget, secretRealmBySittingActor);
				continue;
			}
			TryAddArchivedJinDanItem(byActor, record, familyNames);
		}

		IReadOnlyList<long> cachedJinDanIds = XjCultivatorCache.GetZhenJunOrHigherIds();
		for (int i = 0; i < cachedJinDanIds.Count; i++)
		{
			long actorId = cachedJinDanIds[i];
			if (byActor.ContainsKey(actorId) || !XjScheduler.ResolveActor(actorId, out Actor actor)) continue;
			TryAddJinDanItem(byActor, actorId, actor, 0, null, familyNames, activeMissionByTarget, secretRealmBySittingActor);
		}

		IReadOnlyList<XjGuoWeiRegistryEntry> guoWeiEntries = XjGuoWeiRegistry.ReadAllEntries();
		for (int i = 0; i < guoWeiEntries.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = guoWeiEntries[i];
			if (!entry.Found || !entry.IsActive || entry.ActorId <= 0L || byActor.ContainsKey(entry.ActorId)) continue;
			if (!XjScheduler.ResolveActor(entry.ActorId, out Actor actor)) continue;
			TryAddJinDanItem(byActor, entry.ActorId, actor, entry.Year, null, familyNames, activeMissionByTarget, secretRealmBySittingActor);
		}

		// 释修摩诃／法相／世尊直接读取年度高境索引并入同一修士名录快照，
		// UI只消费快照，不另起分卷，也不做世界扫描。
		IReadOnlyList<long> shiHighRealmIds = XjShiWorldRegistry.GetLiveHighRealmActorIds(Math.Max(1, currentYear));
		for (int i = 0; i < shiHighRealmIds.Count; i++)
		{
			long actorId = shiHighRealmIds[i];
			if (actorId <= 0L || byActor.ContainsKey(actorId) || !XjScheduler.ResolveActor(actorId, out Actor actor)) continue;
			TryAddShiCultivatorItem(byActor, actorId, actor, Math.Max(1, currentYear), familyNames);
		}

		// 释修转世以及没有进入金丹史录的今释摩诃旧身，也要能进入人物链。
		// 这里只遍历持久化转世账本（通常远小于角色总量），不扫描世界Actor。
		IReadOnlyList<XjReincarnationRecord> reincarnations = XjReincarnation.ReadAllEntries();
		for (int i = 0; i < reincarnations.Count; i++)
		{
			XjReincarnationRecord reincarnation = reincarnations[i];
			if (!reincarnation.Found || reincarnation.ActorId <= 0L || byActor.ContainsKey(reincarnation.ActorId)
				|| !string.Equals(reincarnation.Mode, "ShiReincarnation", StringComparison.Ordinal)) continue;
			TryAddShiReincarnationArchiveItem(byActor, reincarnation, familyNames);
		}

		List<XjCodexJinDanItem> result = new List<XjCodexJinDanItem>(byActor.Values);
		result.Sort((left, right) =>
		{
			int historical = left.IsHistorical.CompareTo(right.IsHistorical);
			if (historical != 0) return historical;
			if (left.IsHistorical)
			{
				int deathYear = right.DeathYear.CompareTo(left.DeathYear);
				if (deathYear != 0) return deathYear;
			}
			int age = right.Age.CompareTo(left.Age);
			return age != 0 ? age : left.ActorId.CompareTo(right.ActorId);
		});
		return result;
	}

private static bool TryAddJinDanItem(
		IDictionary<long, XjCodexJinDanItem> byActor,
		long actorId,
		Actor actor,
		int activatedYear,
		XjJinDanImmortalityArchiveRecord record,
		IReadOnlyDictionary<long, string> familyNames,
		IReadOnlyDictionary<long, XjYinSiMissionArchiveRecord> activeMissionByTarget,
		IReadOnlyDictionary<long, XjSecretRealmArchiveRecord> secretRealmBySittingActor)
	{
		if (actorId <= 0L || actor?.data == null || !actor.isAlive() || XjGuZunRegistry.IsManifestationActor(actor)) return false;
		XjSectRepository.TryGetByActor(actor, out XjSectArchiveRecord sect);
		long familyId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId);
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		realmId = XjRealmHelper.NormalizeId(realmId);
		bool isDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(actor)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		bool isJieLin = XjXuanJianShenTongSpecials.IsJieLinXian(actor);
		bool isYuYi = XjXuanJianShenTongSpecials.IsYuYiXian(actor);
		// 空证真君不只包含青宣。长庚初祖以养青冥空证开道，同样属于空证真君；
		// 后续承继长庚果位/余位者不自动继承“空证”身份。
		bool isLongGengKongZheng = XjFuQiSwordWorldState.IsFounderActor(actorId);
		bool isKongZheng = XjQingXuanKongZhengSystem.HasCompletedKongZheng(actor)
			|| isLongGengKongZheng;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string persistedJinXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, out int jinDanSuccessYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinDanSuccessYear, out int fuQiSuccessYear);
		int resolvedActivatedYear = activatedYear > 0
			? activatedYear
			: Math.Max(Math.Max(jinDan.SuccessYear, shenDan.Year), Math.Max(jinDanSuccessYear, fuQiSuccessYear));
		string achievement = isDaoTai
			? "道胎"
			: isKongZheng
			? "空证真君"
			: isYuYi
				? "郁仪仙"
				: isJieLin
				? "结璘仙"
				: shenDan.Found
					? "神丹真君"
					: isFuQi
						? "真君羽士"
						: "金丹真君";
		string actualGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(jinDan.GuoWei);
		bool specialImmortalHasPosition = (isYuYi || isJieLin)
			&& !string.IsNullOrWhiteSpace(jinDan.GuoWei)
			&& (jinDan.GuoWei.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				|| jinDan.GuoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				|| jinDan.GuoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal));
		string guoWei = specialImmortalHasPosition
			? actualGuoWei
			: isYuYi
				? "太阳果位垂照"
				: isJieLin
					? "太阴果位垂照"
				: shenDan.Found
					? XjGuoWeiCalculator.GetDisplayGuoWeiName(shenDan.GuoWei)
					: actualGuoWei;
		XjYinSiMissionArchiveRecord activeMission = null;
		if (!isDaoTai)
		{
			activeMissionByTarget.TryGetValue(actorId, out activeMission);
		}
		secretRealmBySittingActor.TryGetValue(actorId, out XjSecretRealmArchiveRecord shelterRealm);
		XjDaoTaiPresenceArchiveRecord daoTaiPresence = null;
		if (isDaoTai)
		{
			XjDaoTaiPresenceArchive.TryGetRecord(actorId, out daoTaiPresence);
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
		XjGuoWeiQuanBingState authorityState = default;
		bool hasAuthorityState = XjGuoWeiQuanBingRegistry.TryGet(actorId, out authorityState);
		int authorityCount = 0;
		if (!XjHighRealmAggregateStore.TryGetAuthorityCount(actorId, out authorityCount) && hasAuthorityState)
		{
			authorityCount = CountAuthorityValues(authorityState.LocalQuanBing)
				+ CountAuthorityValues(authorityState.SeizedQuanBing);
		}
		XjHighRealmAggregateStore.TryGetImageSummary(actorId, out string imageSummary);
		string mutationSummary = BuildMutationSummary(actorId);
		bool isReincarnatedBody = XjReincarnation.TryGetReincarnationSourceActorId(actorId, out long reincarnationSourceActorId);
		long identityActorId = actorId;
		if (isReincarnatedBody && XjReincarnation.TryResolveReincarnationRootActorId(actorId, out long reincarnationRootActorId))
		{
			identityActorId = reincarnationRootActorId;
		}
		string reincarnationSourceName = string.Empty;
		int reincarnationYear = 0;
		if (isReincarnatedBody && XjReincarnation.TryGetRecord(reincarnationSourceActorId, out XjReincarnationRecord sourceReincarnation))
		{
			reincarnationSourceName = sourceReincarnation.ActorName;
			reincarnationYear = sourceReincarnation.AppliedYear;
		}

		byActor[identityActorId] = new XjCodexJinDanItem
		{
			ActorId = identityActorId,
			FocusActorId = actorId,
			Name = SafeActorName(actor),
			Age = (int)Math.Floor(Math.Max(0f, actor.getAge())),
			ActivatedYear = resolvedActivatedYear,
			Realm = XjRealmHelper.GetDisplayName(realmId),
			CultivationPath = isFuQi ? "服气养性" : "紫府金丹",
			Achievement = achievement,
			DaoTu = daoTu ?? string.Empty,
			JinXing = string.IsNullOrWhiteSpace(persistedJinXing) ? jinDan.JinXing : persistedJinXing.Trim(),
			GuoWei = guoWei ?? string.Empty,
			IsDaoTai = isDaoTai,
			IsKongZheng = isKongZheng,
			SectId = sect?.SectId ?? 0L,
			SectName = sect?.Name ?? string.Empty,
			FamilyId = familyId,
			FamilyName = familyNames.TryGetValue(familyId, out string familyName) ? familyName : string.Empty,
			YinSiExposure = isDaoTai ? 0f : record?.YinSiExposure ?? 0f,
			YinSiKnown = !isDaoTai && (record?.YinSiKnown ?? false),
			YinSiState = isDaoTai ? "不入阴司" : record?.YinSiState ?? XjJinDanYinSiState.Hidden,
			YinSiReason = isDaoTai ? "道胎不入阴司追索。" : string.IsNullOrWhiteSpace(record?.LastExposureReason) ? "暂无" : record.LastExposureReason.Trim(),
			PursuitCount = isDaoTai ? 0 : record?.PursuitCount ?? 0,
			LastKnownYear = isDaoTai ? 0 : record?.LastKnownYear ?? 0,
			ActiveMissionId = activeMission?.MissionId ?? 0L,
			MissionStage = activeMission?.Stage ?? string.Empty,
			NextPursuitYear = activeMission?.NextActionYear ?? 0,
			SecretRealmName = shelterRealm?.DisplayName ?? string.Empty,
			Sheltered = shelterRealm != null,
			DaoTaiPresenceStatus = daoTaiPresence?.Status ?? string.Empty,
			DaoTaiNextReturnYear = daoTaiPresence?.NextReturnYear ?? 0,
			AuthorityCount = authorityCount,
			LocalAuthoritySummary = hasAuthorityState ? authorityState.LocalQuanBing ?? string.Empty : string.Empty,
			SeizedAuthoritySummary = hasAuthorityState ? authorityState.SeizedQuanBing ?? string.Empty : string.Empty,
			PendingAuthoritySummary = hasAuthorityState ? authorityState.ForeignQuanBing ?? string.Empty : string.Empty,
			AuthorityLifecycleStatus = hasAuthorityState ? authorityState.LifecycleStatus ?? string.Empty : string.Empty,
			IntegrationRetreatActive = hasAuthorityState && authorityState.IntegrationRetreatActive,
			IntegrationRetreatEndYear = hasAuthorityState ? authorityState.IntegrationRetreatEndYear : 0,
			GuoWeiImageSummary = imageSummary ?? string.Empty,
			ShenTongMutationSummary = mutationSummary,
			DaoXing = Math.Max(0, daoXing),
			XiuChi = Math.Max(0, xiuChi),
			IsReincarnationContinuation = isReincarnatedBody,
			ReincarnationSourceActorId = isReincarnatedBody ? reincarnationSourceActorId : 0L,
			ReincarnationSourceName = reincarnationSourceName ?? string.Empty,
			ReincarnationYear = Math.Max(0, reincarnationYear),
			LifeStatus = isReincarnatedBody ? "转世" : ResolveLiveJinDanStatus(actor, isDaoTai, daoTaiPresence?.Status),
			DeathYear = 0,
			DeathReason = string.Empty,
			IsHistorical = false,
			CanFocusActor = true
		};
		return true;
	}

private static bool TryAddShiCultivatorItem(
		IDictionary<long, XjCodexJinDanItem> byActor,
		long actorId,
		Actor actor,
		int currentYear,
		IReadOnlyDictionary<long, string> familyNames)
	{
		if (actorId <= 0L || actor?.data == null || !actor.isAlive()
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shi)
			|| XjShiCatalog.GetRank(shi.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return false;

		XjSectRepository.TryGetByActor(actor, out XjSectArchiveRecord sect);
		long familyId = 0L;
		XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId);
		string tradition = XjShiCatalog.GetTraditionDisplay(shi.Tradition);
		string realm = XjShiCatalog.GetRealmDisplay(shi.Realm);
		bool reincarnated = XjReincarnation.TryGetReincarnationSourceActorId(actorId, out long sourceActorId);
		long identityActorId = actorId;
		if (reincarnated && XjReincarnation.TryResolveReincarnationRootActorId(actorId, out long reincarnationRootActorId))
		{
			identityActorId = reincarnationRootActorId;
		}
		string sourceName = string.Empty;
		int reincarnationYear = 0;
		if (reincarnated && XjReincarnation.TryGetRecord(sourceActorId, out XjReincarnationRecord sourceRecord))
		{
			sourceName = sourceRecord.ActorName;
			reincarnationYear = sourceRecord.AppliedYear > 0 ? sourceRecord.AppliedYear : sourceRecord.DeathYear;
		}

		byActor[identityActorId] = new XjCodexJinDanItem
		{
			ActorId = identityActorId,
			FocusActorId = actorId,
			Name = SafeActorName(actor),
			Age = (int)Math.Floor(Math.Max(0f, actor.getAge())),
			ActivatedYear = Math.Max(0, shi.RealmEnteredYear),
			Realm = realm,
			CultivationPath = tradition,
			Achievement = realm,
			DaoTu = tradition,
			JinXing = string.Empty,
			GuoWei = string.Empty,
			IsShi = true,
			ShiTradition = tradition,
			ShiRealm = realm,
			ShiPractice = (int)Math.Floor(Math.Max(0f, shi.Practice)),
			ShiCurrentLife = Math.Max(1, shi.CurrentLife),
			ShiCompletedLives = Math.Max(0, shi.CompletedLives),
			SectId = sect?.SectId ?? 0L,
			SectName = sect?.Name ?? string.Empty,
			FamilyId = familyId,
			FamilyName = familyNames.TryGetValue(familyId, out string familyName) ? familyName : string.Empty,
			YinSiState = "不入阴司",
			YinSiReason = "释修不入阴司追索。",
			IsReincarnationContinuation = reincarnated,
			ReincarnationSourceActorId = reincarnated ? sourceActorId : 0L,
			ReincarnationSourceName = sourceName ?? string.Empty,
			ReincarnationYear = Math.Max(0, reincarnationYear),
			LifeStatus = reincarnated ? "转世" : XjClosedCultivationGuard.IsInClosedCultivation(actor) ? "闭关" : "存世",
			DeathYear = 0,
			DeathReason = string.Empty,
			IsHistorical = false,
			CanFocusActor = true
		};
		return true;
	}

private static bool TryAddShiReincarnationArchiveItem(
		IDictionary<long, XjCodexJinDanItem> byActor,
		XjReincarnationRecord reincarnation,
		IReadOnlyDictionary<long, string> familyNames)
	{
		if (!reincarnation.Found || reincarnation.ActorId <= 0L) return false;
		long identityActorId = reincarnation.ActorId;
		if (XjReincarnation.TryResolveReincarnationRootActorId(reincarnation.ActorId, out long rootActorId)) identityActorId = rootActorId;
		if (byActor.ContainsKey(identityActorId)) return false;

		// 释修轮回记录为了保持通用转世结构，RealmId/DaoTu字段本来就是空的；
		// 权威境界、修法、修持和世数都封存在ShiReincarnationPayload里。旧版名录
		// 直接读RealmId会把真实转世误判成普通死亡，这里改为读取轮回载荷。
		XjShiReincarnationPayload payload = null;
		try
		{
			if (!string.IsNullOrWhiteSpace(reincarnation.FuQiPayload))
				payload = JsonConvert.DeserializeObject<XjShiReincarnationPayload>(reincarnation.FuQiPayload);
		}
		catch
		{
			payload = null;
		}
		if (payload == null
			|| XjShiCatalog.GetRank(payload.PreviousRealm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return false;

		string realm = XjShiCatalog.GetRealmDisplay(payload.PreviousRealm);
		long focusActorId = 0L;
		string currentName = string.Empty;
		string currentTradition = XjShiCatalog.GetTraditionDisplay(payload.Tradition);
		string currentRealm = realm;
		int currentPractice = (int)Math.Floor(Math.Max(0f, payload.PreviousPractice));
		int currentLife = Math.Max(1, payload.PreviousCurrentLife);
		int completedLives = Math.Max(0, payload.PreviousCompletedLives);
		int currentAge = 0;
		int reincarnationYear = Math.Max(0, reincarnation.AppliedYear > 0 ? reincarnation.AppliedYear : reincarnation.DeathYear);
		if (XjReincarnation.TryResolveLatestAppliedRecord(identityActorId, out XjReincarnationRecord latest))
		{
			reincarnationYear = Math.Max(reincarnationYear, Math.Max(0, latest.AppliedYear > 0 ? latest.AppliedYear : latest.DeathYear));
			if (latest.TargetActorId > 0L && XjScheduler.ResolveActor(latest.TargetActorId, out Actor target)
				&& target?.data != null && target.isAlive())
			{
				focusActorId = latest.TargetActorId;
				currentName = SafeActorName(target);
				currentAge = (int)Math.Floor(Math.Max(0f, target.getAge()));
				if (XjShiState.TryBuildSnapshot(target, out XjShiSnapshot shi))
				{
					currentTradition = XjShiCatalog.GetTraditionDisplay(shi.Tradition);
					currentRealm = XjShiCatalog.GetRealmDisplay(shi.Realm);
					currentPractice = (int)Math.Floor(Math.Max(0f, shi.Practice));
					currentLife = Math.Max(1, shi.CurrentLife);
					completedLives = Math.Max(0, shi.CompletedLives);
				}
			}
		}
		long familyId = Math.Max(0L, reincarnation.FamilyStableId);
		byActor[identityActorId] = new XjCodexJinDanItem
		{
			ActorId = identityActorId,
			FocusActorId = focusActorId,
			Name = focusActorId > 0L && !string.IsNullOrWhiteSpace(currentName) ? currentName : EmptyCodexName(reincarnation.TargetActorName, reincarnation.ActorName),
			Age = currentAge,
			ActivatedYear = Math.Max(0, reincarnation.DeathYear),
			Realm = currentRealm,
			CultivationPath = currentTradition,
			Achievement = currentRealm,
			DaoTu = currentTradition,
			IsShi = true,
			ShiTradition = currentTradition,
			ShiRealm = currentRealm,
			ShiPractice = currentPractice,
			ShiCurrentLife = currentLife,
			ShiCompletedLives = completedLives,
			FamilyId = familyId,
			FamilyName = familyNames.TryGetValue(familyId, out string familyName) ? familyName : string.Empty,
			YinSiState = "不入阴司",
			YinSiReason = "释修不入阴司追索。",
			IsReincarnationContinuation = true,
			ReincarnationSourceActorId = identityActorId,
			ReincarnationSourceName = reincarnation.ActorName ?? string.Empty,
			ReincarnationYear = reincarnationYear,
			LifeStatus = "转世",
			DeathYear = Math.Max(0, reincarnation.DeathYear),
			DeathReason = "ShiReincarnation",
			IsHistorical = focusActorId <= 0L,
			CanFocusActor = focusActorId > 0L
		};
		return true;
	}

private static string EmptyCodexName(string preferred, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(preferred)) return preferred.Trim();
		if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();
		return "未名转世释修";
	}

private static bool TryAddArchivedJinDanItem(
		IDictionary<long, XjCodexJinDanItem> byActor,
		XjJinDanImmortalityArchiveRecord record,
		IReadOnlyDictionary<long, string> familyNames)
	{
		if (record == null || record.ActorId <= 0L || record.IsAlive) return false;

		long identityActorId = record.ActorId;
		if (XjReincarnation.TryResolveReincarnationRootActorId(record.ActorId, out long rootActorId))
		{
			identityActorId = rootActorId;
		}
		if (byActor.ContainsKey(identityActorId)) return false;

		long familyId = Math.Max(0L, record.FamilyId);
		bool hasReincarnation = XjReincarnation.TryGetRecord(identityActorId, out XjReincarnationRecord reincarnation);
		string lifeStatus = hasReincarnation ? "转世" : string.IsNullOrWhiteSpace(record.LifeStatus) ? "死亡" : record.LifeStatus.Trim();
		long focusActorId = 0L;
		string currentName = string.Empty;
		int displayAge = Math.Max(0, record.Age);
		string displayRealm = XjRealmHelper.GetDisplayName(XjRealmHelper.NormalizeId(record.RealmId));
		string displayPath = record.CultivationPath ?? string.Empty;
		string displayAchievement = record.Achievement ?? string.Empty;
		string displayDaoTu = record.DaoTu ?? string.Empty;
		string displayJinXing = record.JinXing ?? string.Empty;
		string displayGuoWei = record.GuoWei ?? string.Empty;
		bool displayIsDaoTai = record.IsDaoTai;
		bool displayIsShi = false;
		string shiTradition = string.Empty;
		string shiRealm = string.Empty;
		int shiPractice = 0;
		int shiCurrentLife = 0;
		int shiCompletedLives = 0;
		int reincarnationYear = hasReincarnation
			? Math.Max(0, reincarnation.AppliedYear > 0 ? reincarnation.AppliedYear : reincarnation.DeathYear)
			: 0;

		if (hasReincarnation && XjReincarnation.TryResolveLatestAppliedRecord(identityActorId, out XjReincarnationRecord latest))
		{
			reincarnationYear = Math.Max(reincarnationYear, Math.Max(0, latest.AppliedYear > 0 ? latest.AppliedYear : latest.DeathYear));
			if (latest.TargetActorId > 0L && XjScheduler.ResolveActor(latest.TargetActorId, out Actor reincarnatedActor)
				&& reincarnatedActor?.data != null && reincarnatedActor.isAlive())
			{
				focusActorId = latest.TargetActorId;
				currentName = SafeActorName(reincarnatedActor);
				displayAge = (int)Math.Floor(Math.Max(0f, reincarnatedActor.getAge()));

				if (XjShiState.TryBuildSnapshot(reincarnatedActor, out XjShiSnapshot shi))
				{
					displayIsShi = true;
					shiTradition = XjShiCatalog.GetTraditionDisplay(shi.Tradition);
					shiRealm = XjShiCatalog.GetRealmDisplay(shi.Realm);
					shiPractice = (int)Math.Floor(Math.Max(0f, shi.Practice));
					shiCurrentLife = Math.Max(1, shi.CurrentLife);
					shiCompletedLives = Math.Max(0, shi.CompletedLives);
					displayRealm = shiRealm;
					displayPath = shiTradition;
					displayAchievement = shiRealm;
					displayDaoTu = shiTradition;
					displayJinXing = string.Empty;
					displayGuoWei = string.Empty;
					displayIsDaoTai = false;
				}
				else
				{
					string currentRealmId = XjRealmHelper.GetUnifiedId(reincarnatedActor, XjRealmHelper.GetTraitSnapshotForRouter);
					displayRealm = XjRealmHelper.GetDisplayName(currentRealmId);
					bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(reincarnatedActor);
					bool isZiJin = XjCultivationPathRules.IsZiFuJinDan(reincarnatedActor);
					displayPath = isFuQi ? "服气养性" : isZiJin ? "紫府金丹" : "转世重修";
					if (XjActorAccessor.TryGetString(reincarnatedActor, XjActorDataKeys.DaoTu, out string currentDaoTu)
						&& !string.IsNullOrWhiteSpace(currentDaoTu)) displayDaoTu = currentDaoTu.Trim();
					displayIsDaoTai = XjDaoTaiSpellScale.IsDaoTaiActor(reincarnatedActor)
						|| string.Equals(currentRealmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
						|| string.Equals(currentRealmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
					if (displayIsDaoTai) displayAchievement = "道胎";
					else if (XjCultivationPathRules.IsJinDanEquivalentRealm(currentRealmId)) displayAchievement = isFuQi ? "真君羽士" : "金丹真君";
					else displayAchievement = string.IsNullOrWhiteSpace(displayRealm) ? "转世重修" : displayRealm;

					XjJinDanState currentJinDan = XjJinDanAccessor.BuildState(reincarnatedActor);
					XjActorAccessor.TryGetString(reincarnatedActor, XjActorDataKeys.XjJinDanJinXing, out string currentJinXing);
					displayJinXing = string.IsNullOrWhiteSpace(currentJinXing) ? currentJinDan.JinXing : currentJinXing.Trim();
					displayGuoWei = XjGuoWeiCalculator.GetDisplayGuoWeiName(currentJinDan.GuoWei);
				}
			}
		}

		byActor[identityActorId] = new XjCodexJinDanItem
		{
			ActorId = identityActorId,
			FocusActorId = focusActorId,
			Name = focusActorId > 0L && !string.IsNullOrWhiteSpace(currentName)
				? currentName
				: string.IsNullOrWhiteSpace(record.Name) ? "失考修士#" + identityActorId : record.Name.Trim(),
			Age = displayAge,
			ActivatedYear = Math.Max(0, record.ActivatedYear),
			Realm = displayRealm,
			CultivationPath = displayPath,
			Achievement = displayAchievement,
			DaoTu = displayDaoTu,
			JinXing = displayJinXing,
			GuoWei = displayGuoWei,
			IsDaoTai = displayIsDaoTai,
			IsKongZheng = record.IsKongZheng,
			IsShi = displayIsShi,
			ShiTradition = shiTradition,
			ShiRealm = shiRealm,
			ShiPractice = shiPractice,
			ShiCurrentLife = shiCurrentLife,
			ShiCompletedLives = shiCompletedLives,
			SectId = Math.Max(0L, record.SectId),
			SectName = record.SectName ?? string.Empty,
			FamilyId = familyId,
			FamilyName = familyNames.TryGetValue(familyId, out string familyName) ? familyName : string.Empty,
			YinSiExposure = 0f,
			YinSiKnown = false,
			YinSiState = displayIsShi ? "不入阴司" : XjJinDanYinSiState.Dead,
			YinSiReason = displayIsShi ? "释修不入阴司追索。" : "高境史录。",
			PursuitCount = Math.Max(0, record.PursuitCount),
			LastKnownYear = Math.Max(0, record.LastKnownYear),
			ActiveMissionId = 0L,
			MissionStage = string.Empty,
			NextPursuitYear = 0,
			SecretRealmName = string.Empty,
			Sheltered = false,
			DaoTaiPresenceStatus = record.DaoTaiPresenceStatus ?? string.Empty,
			DaoTaiNextReturnYear = Math.Max(0, record.DaoTaiNextReturnYear),
			AuthorityCount = Math.Max(0, record.AuthorityCount),
			LocalAuthoritySummary = record.LocalAuthoritySummary ?? string.Empty,
			SeizedAuthoritySummary = record.SeizedAuthoritySummary ?? string.Empty,
			PendingAuthoritySummary = record.PendingAuthoritySummary ?? string.Empty,
			AuthorityLifecycleStatus = record.AuthorityLifecycleStatus ?? string.Empty,
			IntegrationRetreatActive = false,
			IntegrationRetreatEndYear = Math.Max(0, record.IntegrationRetreatEndYear),
			GuoWeiImageSummary = record.GuoWeiImageSummary ?? string.Empty,
			ShenTongMutationSummary = record.ShenTongMutationSummary ?? string.Empty,
			DaoXing = Math.Max(0, record.DaoXing),
			XiuChi = Math.Max(0, record.XiuChi),
			IsReincarnationContinuation = hasReincarnation,
			ReincarnationSourceActorId = hasReincarnation ? identityActorId : 0L,
			ReincarnationSourceName = hasReincarnation ? (record.Name ?? reincarnation.ActorName ?? string.Empty) : string.Empty,
			ReincarnationYear = reincarnationYear,
			LifeStatus = lifeStatus,
			DeathYear = Math.Max(0, record.DeathYear),
			DeathReason = hasReincarnation ? "Reincarnation" : record.DeathReason ?? string.Empty,
			IsHistorical = focusActorId <= 0L,
			CanFocusActor = focusActorId > 0L
		};
		return true;
	}

private static string ResolveLiveJinDanStatus(Actor actor, bool isDaoTai, string daoTaiPresenceStatus)
	{
		if (isDaoTai)
		{
			string status = (daoTaiPresenceStatus ?? string.Empty).Trim();
			if (string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal)
				|| string.Equals(status, XjDaoTaiPresenceStatus.LegacyBeyondWorld, StringComparison.Ordinal)) return "天外";
			if (string.Equals(status, XjDaoTaiPresenceStatus.ClosedCultivation, StringComparison.Ordinal)) return "闭关";
		}
		return XjClosedCultivationGuard.IsInClosedCultivation(actor) ? "闭关" : "存世";
	}

private static int CountAuthorityValues(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return 0;
		return raw.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries).Length;
	}

private static string BuildMutationSummary(long actorId)
	{
		if (!XjHighRealmAggregateStore.TryGetMutationBindings(actorId, out IReadOnlyList<XjHighRealmMutationBinding> bindings)
			|| bindings == null || bindings.Count == 0) return string.Empty;
		List<string> rows = new List<string>(Math.Min(3, bindings.Count));
		for (int i = 0; i < bindings.Count && rows.Count < 3; i++)
		{
			XjHighRealmMutationBinding binding = bindings[i];
			if (!binding.IsValid) continue;
			rows.Add(binding.Lower + " → " + binding.Upper + "（" + binding.SourceDaoTu + "·" + binding.Authority + "）");
		}
		return string.Join("；", rows);
	}

private static List<XjCodexAdventureRealmItem> BuildAdventureRealms(
		IReadOnlyList<XjDongTianRecord> realms,
		IReadOnlyList<XjAdventureRealmClaimArchiveRecord> claims,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyDictionary<long, string> familyNames,
		IReadOnlyDictionary<long, string> cityNames)
	{
		Dictionary<string, XjAdventureRealmClaimArchiveRecord> claimsById = new Dictionary<string, XjAdventureRealmClaimArchiveRecord>(StringComparer.Ordinal);
		for (int i = 0; i < claims.Count; i++) if (claims[i] != null && !string.IsNullOrWhiteSpace(claims[i].RecordId)) claimsById[claims[i].RecordId] = claims[i];
		List<XjCodexAdventureRealmItem> result = new List<XjCodexAdventureRealmItem>(realms.Count);
		for (int i = 0; i < realms.Count; i++)
		{
			XjDongTianRecord realm = realms[i];
			// 落霞山是虹霞道统的背景上宗与师承锚点，并非可供玩家寻找、
			// 占据或探索的奇遇洞天。它只保留在世界实体与虹霞事件链中，
			// 绝不进入“奇遇洞天”快照、计数或界面。
			if (XjDongTianRules.IsLuoXiaShanDongTian(realm.QiYuDongTianId))
			{
				continue;
			}
			claimsById.TryGetValue(realm.RecordId, out XjAdventureRealmClaimArchiveRecord claim);
			result.Add(new XjCodexAdventureRealmItem
			{
				RecordId = realm.RecordId,
				Name = realm.DisplayName,
				DaoTuGroup = realm.DaoTuGroup,
				State = claim?.State ?? XjAdventureRealmClaimState.Discovered,
				IsOpen = realm.IsOpen,
				OpenUntilYear = realm.OpenUntilYear,
				RemainingExploreCount = realm.RemainingExploreCount,
				UnlimitedExplore = XjDongTianRules.IsYuanZhaoDongTian(realm.QiYuDongTianId),
				AnchorCityId = claim?.AnchorCityId > 0L ? claim.AnchorCityId : realm.AnchorCityId,
				AnchorTileX = claim != null && claim.AnchorTileX >= 0 ? claim.AnchorTileX : realm.AnchorTileX,
				AnchorTileY = claim != null && claim.AnchorTileY >= 0 ? claim.AnchorTileY : realm.AnchorTileY,
				CityName = claim != null && cityNames.TryGetValue(claim.AnchorCityId, out string currentCityName) ? currentCityName : realm.AnchorCityName,
				ClaimSectName = claim != null && sectNames.TryGetValue(claim.ClaimSectId, out string currentSectName) ? currentSectName : claim?.ClaimSectName ?? string.Empty,
				ClaimKingdomName = claim?.ClaimKingdomName ?? string.Empty,
				DiscovererName = claim?.DiscovererName ?? realm.AnchorActorName,
				ContestingSectName = claim != null && sectNames.TryGetValue(claim.ContestingSectId, out string currentContestingSectName) ? currentContestingSectName : claim?.ContestingSectName ?? string.Empty,
				ContestDueYear = claim?.ContestDueYear ?? 0,
				ContestSummary = claim?.ContestSummary ?? string.Empty
			});
		}
		result.Sort((left, right) =>
		{
			int open = right.IsOpen.CompareTo(left.IsOpen);
			if (open != 0) return open;
			int until = right.OpenUntilYear.CompareTo(left.OpenUntilYear);
			return until != 0 ? until : string.Compare(left.RecordId, right.RecordId, StringComparison.Ordinal);
		});
		return result;
	}

private static List<XjCodexSecretRealmItem> BuildSecretRealms(IReadOnlyList<XjSecretRealmArchiveRecord> records, IReadOnlyDictionary<long, string> sectNames)
	{
		List<XjCodexSecretRealmItem> result = new List<XjCodexSecretRealmItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjSecretRealmArchiveRecord record = records[i];
			if (record == null || record.RealmId <= 0L) continue;
			string lead = record.LeadFormationMasterId > 0L && XjScheduler.ResolveActor(record.LeadFormationMasterId, out Actor leadActor) ? SafeActorName(leadActor) : string.Empty;
			string sitting = record.SittingJinDanActorId > 0L && XjScheduler.ResolveActor(record.SittingJinDanActorId, out Actor sittingActor) ? SafeActorName(sittingActor) : string.Empty;
			List<XjCodexSecretRealmJinDanItem> jinDanActors = BuildSecretRealmJinDanActors(record.SectId);
			result.Add(new XjCodexSecretRealmItem
			{
				SectId = record.SectId, SectName = sectNames.TryGetValue(record.SectId, out string sectName) ? sectName : string.Empty,
				RealmId = record.RealmId, DisplayName = record.DisplayName ?? string.Empty, Stage = record.Stage ?? XjSecretRealmStage.None,
				ConstructionMethodKnown = record.ConstructionMethodKnown, ConstructionMethodSource = record.ConstructionMethodSource ?? string.Empty,
				Stability = record.Stability, XuanTaoIntegrity = record.XuanTaoIntegrity, Capacity = record.Capacity,
				EntranceCityId = record.EntranceCityId,
				EntranceCityName = record.EntranceCityName ?? string.Empty, LeadFormationMasterName = lead,
				SittingJinDanActorId = record.SittingJinDanActorId, SittingJinDanName = sitting,
				JinDanCount = jinDanActors.Count, JinDanActors = jinDanActors,
				ActiveTaskId = record.ActiveTaskId, StageDueYear = record.StageDueYear, EntranceOpen = record.EntranceOpen,
				Summary = record.ConstructionMethodKnown ? "玄韬工程已获传承，当前阶段：" + TranslateSecretRealmStageForConflict(record.Stage) : "尚未获得洞天营造之法"
			});
		}
		return result;
	}

private static List<XjCodexSecretRealmJinDanItem> BuildSecretRealmJinDanActors(long sectId)
	{
		List<XjCodexSecretRealmJinDanItem> result = new List<XjCodexSecretRealmJinDanItem>();
		if (sectId <= 0L)
		{
			return result;
		}

		IReadOnlyList<long> actorIds = XjSectAuthorityStore.GetActorIdsForSect(sectId);
		for (int i = 0; i < actorIds.Count; i++)
		{
			long actorId = actorIds[i];
			if (actorId <= 0L || !XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive())
			{
				continue;
			}

			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (!XjHighRealmIdentity.IsZhenJun(realmId))
			{
				continue;
			}

			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			result.Add(new XjCodexSecretRealmJinDanItem
			{
				ActorId = actorId,
				Name = SafeActorName(actor),
				Realm = XjRealmHelper.GetDisplayName(realmId),
				DaoTu = daoTu ?? string.Empty
			});
		}

		result.Sort((left, right) =>
		{
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			return name != 0 ? name : left.ActorId.CompareTo(right.ActorId);
		});
		return result;
	}

private static string FormatSectPeakName(int peakId, string storedName)
	{
		string trimmed = storedName?.Trim() ?? string.Empty;
		if (!string.IsNullOrWhiteSpace(trimmed) && !IsNumericPeakName(trimmed)) return trimmed;
		int index = Math.Clamp(peakId, 1, SnapshotSectPeakNames.Length) - 1;
		return SnapshotSectPeakNames[index];
	}

private static bool IsNumericPeakName(string name)
	{
		if (string.IsNullOrWhiteSpace(name) || !name.StartsWith("第", StringComparison.Ordinal) || !name.EndsWith("峰", StringComparison.Ordinal)) return false;
		string middle = name.Substring(1, name.Length - 2);
		return int.TryParse(middle, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
	}

private static List<XjCodexLectureItem> BuildLectures(IReadOnlyList<XjSectLectureArchiveRecord> records, IReadOnlyDictionary<long, string> sectNames)
	{
		List<XjCodexLectureItem> result = new List<XjCodexLectureItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjSectLectureArchiveRecord record = records[i];
			if (record == null) continue;
			result.Add(new XjCodexLectureItem
			{
				LectureId = record.LectureId, SectId = record.SectId, SectName = sectNames.TryGetValue(record.SectId, out string name) ? name : string.Empty,
				LectureType = record.LectureType ?? string.Empty, LecturerName = record.LecturerName ?? string.Empty, Year = record.Year,
				HeldInsideSecretRealm = record.HeldInsideSecretRealm, AttendeeCount = record.AttendeeCount, Summary = record.Summary ?? string.Empty
			});
		}
		return result;
	}

private static List<XjCodexConflictItem> BuildConflicts(
		IReadOnlyList<XjCodexSectItem> sects,
		IReadOnlyList<XjCodexCityItem> cities,
		IReadOnlyList<XjCodexFormationItem> formations,
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance,
		IReadOnlyList<XjCodexFamilyItem> families,
		IReadOnlyList<XjYinSiMissionArchiveRecord> yinSiMissions,
		IReadOnlyList<XjCodexSecretRealmItem> secretRealms,
		IReadOnlyList<XjCodexAdventureRealmItem> adventureRealms)
	{
		List<XjCodexConflictItem> result = new List<XjCodexConflictItem>();
		IReadOnlyList<XjDoctrineRelationSnapshot> doctrineRelations = XjDoctrineConflictSystem.ReadRelationSnapshot(Math.Max(1, XjYearTracker.CurrentYear));
		for (int i = 0; i < doctrineRelations.Count; i++)
		{
			XjDoctrineRelationSnapshot relation = doctrineRelations[i];
			if (relation == null || relation.FinalHostility < 60) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = relation.FinalHostility >= 90 ? "危急" : relation.FinalHostility >= 80 ? "高" : "中",
				Kind = "道统",
				Title = relation.SourceDoctrineName + "视" + relation.TargetDoctrineName + "已至" + relation.Status,
				Reason = "当世积怨" + relation.Grievance.ToString(CultureInfo.InvariantCulture) + "。"
					+ (string.IsNullOrWhiteSpace(relation.LastReason) ? string.Empty : "近因：" + relation.LastReason + "。"),
				NextStep = relation.FinalHostility >= 80
					? "已入道争，高境与筑基以上修士可能低频主动索敌；同族、同宗与已有目标仍受保护。"
					: "已入交恶，真人级高境可能主动介入；百年无新异道伤亡会逐步消退积怨。",
				Observer = "天下卷／修道大势／四道形势"
			});
		}

		IReadOnlyList<XjSectHostilityArchiveRecord> hostilities = XjSectWarSystem.ReadHostilities();
		for (int i = 0; i < hostilities.Count; i++)
		{
			XjSectHostilityArchiveRecord hostility = hostilities[i];
			if (hostility == null || hostility.Hostility < 50) continue;
			string leftName = "旧宗门";
			string rightName = "旧宗门";
			for (int s = 0; s < sects.Count; s++)
			{
				if (sects[s].SectId == hostility.LeftSectId) leftName = sects[s].Name;
				if (sects[s].SectId == hostility.RightSectId) rightName = sects[s].Name;
			}
			result.Add(new XjCodexConflictItem
			{
				Severity = hostility.Hostility >= 100 ? "危急" : hostility.Hostility >= 80 ? "高" : "中",
				Kind = "宗门关系",
				SectId = hostility.LastAggressorSectId > 0L ? hostility.LastAggressorSectId : hostility.LeftSectId,
				Title = leftName + "与" + rightName + "宗门敌对",
				Reason = "敌对值" + hostility.Hostility.ToString(CultureInfo.InvariantCulture) + "。"
					+ XjSectWarSystem.NormalizeReasonForDisplay(hostility.LastReason, "两宗已有嫌隙。"),
				NextStep = hostility.Hostility >= 100
					? "两宗均有城、高境与可用护宗大阵时，将触发宗门大战。"
					: "家族血债、重要人物死亡和低频宗门冲突会继续推高敌对值。",
				Observer = "山河诸势／宗门谱系／关系纪事"
			});
		}
		IReadOnlyList<XjSectWarArchiveRecord> activeSectWars = XjSectWarSystem.ReadActiveWars();
		for (int i = 0; i < activeSectWars.Count; i++)
		{
			XjSectWarArchiveRecord war = activeSectWars[i];
			if (war == null) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = war.DurationYears >= 8 ? "危急" : "高",
				Kind = "宗门关系",
				SectId = war.AttackerSectId,
				Title = war.AttackerSectName + "与" + war.DefenderSectName + "宗门大战",
				Reason = "战事已持续" + Math.Max(1, war.DurationYears).ToString(CultureInfo.InvariantCulture) + "年。"
					+ XjSectWarSystem.NormalizeReasonForDisplay(war.Reason, "双方正攻伐护宗大阵。"),
				NextStep = "一方破阵则灭宗吞并；同年双阵尽毁则两宗俱存；十年不破或高境力量不足则停战。",
				Observer = "山河诸势／宗门谱系／关系纪事"
			});
		}
		for (int i = 0; i < formations.Count; i++)
		{
			XjCodexFormationItem formation = formations[i];
			if (formation.Grade <= 0) continue;
			float ratio = formation.MaxDurability <= 0 ? 0f : (float)formation.CurrentDurability / formation.MaxDurability;
			if (ratio <= 0f)
			{
				result.Add(new XjCodexConflictItem { Severity = "危急", Kind = "大阵", SectId = formation.SectId, Title = formation.SectName + "护宗大阵已破", Reason = "大阵耐久归零，全部宗门城市已失去最终易主门禁。", NextStep = "需要阵法师与匹配品阶材料建立维修任务。", Observer = "山河诸势／宗门谱系／大阵洞天" });
			}
			else if (ratio < 0.35f)
			{
				result.Add(new XjCodexConflictItem { Severity = "高", Kind = "大阵", SectId = formation.SectId, Title = formation.SectName + "阵基濒危", Reason = "大阵耐久已近崩解，多城同时受压时可能迅速被破。", NextStep = "检查战争、阵法主持和维修材料。", Observer = "山河诸势／宗门谱系／大阵洞天" });
			}
		}
		for (int i = 0; i < cities.Count; i++)
		{
			XjCodexCityItem city = cities[i];
			if (city.SectId > 0L && city.GoverningFamilyId <= 0L)
			{
				result.Add(new XjCodexConflictItem { Severity = "中", Kind = "城镇", SectId = city.SectId, CityId = city.CityId, Title = city.Name + "治理权悬空", Reason = "宗门城市尚未确认稳定治理家族。", NextStep = "在地家族聚合完成后确认治理权。", Observer = "山河诸势／城镇封邑" });
			}
		}
		for (int i = 0; i < governance.Count; i++)
		{
			XjCityFamilyGovernanceArchiveRecord record = governance[i];
			if (record == null || record.ChallengerFamilyId <= 0L || record.ChallengeConsecutiveYears <= 0) continue;
			string cityName = "城镇#" + record.CityId.ToString(CultureInfo.InvariantCulture);
			for (int c = 0; c < cities.Count; c++) if (cities[c].CityId == record.CityId) { cityName = cities[c].Name; break; }
			result.Add(new XjCodexConflictItem
			{
				Severity = record.ChallengeConsecutiveYears >= 7 ? "高" : "中",
				Kind = "城镇",
				SectId = record.SectId, CityId = record.CityId, FamilyId = record.ChallengerFamilyId,
				Title = cityName + "治理权挑战",
				Reason = "挑战家族已连续" + record.ChallengeConsecutiveYears + "年达到在地控制门槛。",
				NextStep = "连续满10年且维持1.35倍优势后将完成改封。",
				Observer = "山河诸势／城镇封邑"
			});
		}
		for (int i = 0; i < families.Count; i++)
		{
			XjCodexFamilyItem family = families[i];
			if (family.PrivilegeHeat >= 55f)
			{
				result.Add(new XjCodexConflictItem
				{
					Severity = family.PrivilegeHeat >= 80f ? "高" : "中", Kind = "世家", SectId = family.SectId, FamilyId = family.FamilyId,
					Title = family.Name + (family.PrivilegeHeat >= 80f ? "权势坐大" : "门第势重"),
					Reason = "宗门话语权与地方治理权集中，权势热度达到" + Math.Round(family.PrivilegeHeat) + "。",
					NextStep = "宗门可通过宗主更替、改封城镇、讲道名额和宗门律令削弱单一家族把持。",
					Observer = "山河诸势／家族诸脉"
				});
			}
			if (family.SupplyDebt < 60f || family.JinDanCount > 0 || family.ZiFuCount > 0) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = family.SupplyDebt >= 85f ? "高" : "中", Kind = "世家", SectId = family.SectId, FamilyId = family.FamilyId,
				Title = family.Name + "供养欠缴",
				Reason = "供养债务达到" + Math.Round(family.SupplyDebt) + "，与其宗门权利不再匹配。",
				NextStep = "完成百艺供给、阵法维修或战争贡献可逐步降低欠缴。",
				Observer = "山河诸势／家族诸脉"
			});
		}
		for (int i = 0; i < yinSiMissions.Count; i++)
		{
			XjYinSiMissionArchiveRecord mission = yinSiMissions[i];
			if (mission == null || string.Equals(mission.Stage, XjYinSiMissionStage.Resolved, StringComparison.Ordinal)
				|| string.Equals(mission.Stage, XjYinSiMissionStage.Cancelled, StringComparison.Ordinal)
				|| string.Equals(mission.Stage, XjYinSiMissionStage.Evaded, StringComparison.Ordinal)) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = string.Equals(mission.Stage, XjYinSiMissionStage.Pursuing, StringComparison.Ordinal) ? "危急" : "高",
				Kind = "阴司",
				ActorId = mission.TargetActorId,
				Title = mission.TargetActorName + "遭阴司追索",
				Reason = "阴司追索已进入" + TranslateYinSiMissionStageForConflict(mission.Stage) + "阶段，目标已被知悉状态不可逆。",
				NextStep = "进入完整玄韬秘境可延缓本轮追索，但不能洗去阴司名录。",
				Observer = "修行道统／修士名录"
			});
		}
		for (int i = 0; i < secretRealms.Count; i++)
		{
			XjCodexSecretRealmItem realm = secretRealms[i];
			if (realm == null || realm.ActiveTaskId <= 0L || realm.StageDueYear <= 0) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = "低", Kind = "洞天", SectId = realm.SectId, Title = realm.SectName + "玄韬工程推进中",
				Reason = TranslateSecretRealmStageForConflict(realm.Stage) + "预计于" + XjChronology.FormatYear(realm.StageDueYear) + "验收。",
				NextStep = "主持阵法师死亡或材料链中断会保留进度并等待接续。", Observer = "山河诸势／宗门谱系／大阵洞天"
			});
		}
		for (int i = 0; i < adventureRealms.Count; i++)
		{
			XjCodexAdventureRealmItem realm = adventureRealms[i];
			if (realm == null || !string.Equals(realm.State, XjAdventureRealmClaimState.Contested, StringComparison.Ordinal)) continue;
			result.Add(new XjCodexConflictItem
			{
				Severity = "中", Kind = "洞天", CityId = realm.AnchorCityId, AdventureRealmName = realm.Name,
				Title = realm.Name + "正在争夺",
				Reason = EmptyConflict(realm.ContestingSectName, "外宗") + "已派实际探索者挑战既有属地主张。",
				NextStep = "只按实际到场高境角色结算剩余探索权，不会改变城市归属。", Observer = "天下卷／奇遇洞天"
			});
		}

		for (int i = 0; i < sects.Count; i++)
		{
			XjCodexSectItem sect = sects[i];
			if (string.IsNullOrWhiteSpace(sect.SovereignName))
			{
				result.Add(new XjCodexConflictItem { Severity = "高", Kind = "宗门治理", SectId = sect.SectId, Title = sect.Name + "宗主位空悬", Reason = "宗门档案未能确认当前宗主仍在世。", NextStep = "触发宗主继承或临时摄宗流程。", Observer = "山河诸势／宗门谱系／山门总览" });
			}
		}
		result.Sort((left, right) =>
		{
			int severity = ConflictSeverityRank(right?.Severity).CompareTo(ConflictSeverityRank(left?.Severity));
			if (severity != 0) return severity;
			int kind = string.Compare(left?.Kind, right?.Kind, StringComparison.Ordinal);
			if (kind != 0) return kind;
			return string.Compare(left?.Title, right?.Title, StringComparison.Ordinal);
		});
		return result;
	}


	private static int ConflictSeverityRank(string severity)
	{
		return severity switch
		{
			"危急" => 4,
			"高" => 3,
			"中" => 2,
			"低" => 1,
			_ => 0
		};
	}

private static List<XjCodexHistoryItem> BuildHistory(
		IReadOnlyList<XjWorldHistoryArchiveRecord> records,
		IReadOnlyDictionary<long, string> cityNames,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyDictionary<long, string> familyNames,
		out List<XjCodexHistorySubjectItem> actorSubjects,
		out List<XjCodexHistorySubjectItem> familySubjects,
		out List<XjCodexHistorySubjectItem> sectSubjects)
	{
		List<XjCodexHistoryItem> result = new List<XjCodexHistoryItem>(records.Count);
		int minimumVisibleYear = Math.Max(0, XjCenturyAnnalsStore.BaseWorldYear);
		for (int i = records.Count - 1; i >= 0; i--)
		{
			XjWorldHistoryArchiveRecord record = records[i];
			if (record == null) continue;
			if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(record.Category, record.EventType, record.Title, record.Body)) continue;
			if (minimumVisibleYear > 0 && record.Year > 0 && record.Year < minimumVisibleYear) continue;
			string sectName = ResolveHistorySubjectName(record.SectId, record.SectNameSnapshot, sectNames, "未名宗门");
			string relatedSectName = ResolveHistorySubjectName(record.RelatedSectId, record.RelatedSectNameSnapshot, sectNames, "未名宗门");
			string familyName = ResolveHistorySubjectName(record.FamilyId, record.FamilyNameSnapshot, familyNames, "未名氏");
			string relatedFamilyName = ResolveHistorySubjectName(record.RelatedFamilyId, record.RelatedFamilyNameSnapshot, familyNames, "未名氏");
			string cityName = cityNames.TryGetValue(record.CityId, out string currentCityName)
				? currentCityName
				: !string.IsNullOrWhiteSpace(record.CityNameSnapshot) ? record.CityNameSnapshot
				: record.HasLocation ? record.LocationX + "," + record.LocationY : string.Empty;
			int displayYear = XjChronology.ToXuanJianYear(record.Year);
			string displayBody = record.Body ?? string.Empty;
			if (displayYear > 0 && displayYear != record.Year)
			{
				displayBody = displayBody.Replace("玄鉴历" + record.Year + "年", "玄鉴历" + displayYear + "年");
			}
			XjCodexHistoryItem item = new XjCodexHistoryItem
			{
				EventId = record.EventId.ToString(CultureInfo.InvariantCulture),
				SortSequence = record.EventId,
				Year = displayYear,
				EventType = record.EventType ?? string.Empty,
				Category = record.Category,
				Title = record.Title,
				Body = displayBody,
				IconId = record.IconId ?? string.Empty,
				Importance = record.Importance,
				IsProtected = record.IsProtected,
				VisibilityFlags = record.VisibilityFlags,
				Result = record.Result ?? string.Empty,
				CauseEventId = record.CauseEventId,
				CenturyStatus = record.CenturyStatus ?? string.Empty,
				ActorId = record.ActorId,
				ActorName = record.ActorName,
				RelatedActorId = record.RelatedActorId,
				RelatedActorName = record.RelatedActorName ?? string.Empty,
				Location = cityName,
				HasLocation = record.HasLocation,
				LocationX = record.LocationX,
				LocationY = record.LocationY,
				SectId = record.SectId,
				SectName = sectName,
				RelatedSectId = record.RelatedSectId,
				RelatedSectName = relatedSectName,
				FamilyId = record.FamilyId,
				FamilyName = familyName,
				RelatedFamilyId = record.RelatedFamilyId,
				RelatedFamilyName = relatedFamilyName,
				CityId = record.CityId
			};
			result.Add(item);
		}
		// 统一史册只在快照发布时排序一次：年份越新越靠前；同年按事件写入序号倒序。
		// 不再依赖存档列表的物理顺序，避免旧档补录或历史迁移把早年事件插到新事之间。
		result.Sort(CompareHistoryNewestFirst);
		// 天下纪事不再派生三书名录。三个史册只读取各自的独立事件存储。
		actorSubjects = new List<XjCodexHistorySubjectItem>();
		familySubjects = new List<XjCodexHistorySubjectItem>();
		sectSubjects = new List<XjCodexHistorySubjectItem>();
		return result;
	}


	private static List<XjCodexHistoryItem> BuildThreeBookHistory(
		IReadOnlyList<XjThreeBookArchiveRecord> records,
		int subjectType,
		IReadOnlyDictionary<long, string> cityNames,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyDictionary<long, string> familyNames,
		out List<XjCodexHistorySubjectItem> subjects)
	{
		List<XjCodexHistoryItem> result = new List<XjCodexHistoryItem>(records?.Count ?? 0);
		Dictionary<long, XjCodexHistorySubjectItem> subjectMap = new Dictionary<long, XjCodexHistorySubjectItem>();
		IReadOnlyDictionary<long, int> personalVisibleStartYears = subjectType == 0 ? BuildVisiblePersonalStartYears(records) : null;
		if (records != null)
		{
			for (int i = records.Count - 1; i >= 0; i--)
			{
				XjThreeBookArchiveRecord record = records[i];
				if (record == null || record.SubjectId <= 0L || string.IsNullOrWhiteSpace(record.Body)) continue;
				if (subjectType == 0 && !IsVisiblePersonalThreeBookRecord(record, personalVisibleStartYears)) continue;
				string sectName = ResolveHistorySubjectName(record.SectId, record.SectNameSnapshot, sectNames, "未名宗门");
				string relatedSectName = ResolveHistorySubjectName(record.RelatedSectId, record.RelatedSectNameSnapshot, sectNames, "未名宗门");
				string familyName = ResolveHistorySubjectName(record.FamilyId, record.FamilyNameSnapshot, familyNames, "未名氏");
				string relatedFamilyName = ResolveHistorySubjectName(record.RelatedFamilyId, record.RelatedFamilyNameSnapshot, familyNames, "未名氏");
				string cityName = cityNames != null && cityNames.TryGetValue(record.CityId, out string liveCity)
					? liveCity
					: !string.IsNullOrWhiteSpace(record.CityNameSnapshot) ? record.CityNameSnapshot
					: record.HasLocation ? record.LocationX + "," + record.LocationY : string.Empty;
				int displayYear = XjChronology.ToXuanJianYear(ResolveThreeBookChronologyYear(record));
				string displayBody = record.Body ?? string.Empty;
				if (displayYear > 0 && displayYear != record.Year && record.Year > 0)
				{
					displayBody = displayBody.Replace("玄鉴历" + record.Year + "年", "玄鉴历" + displayYear + "年");
				}
				XjCodexHistoryItem item = new XjCodexHistoryItem
				{
					EventId = record.BookEventId.ToString(CultureInfo.InvariantCulture),
					SortSequence = record.SortSequence > 0L ? record.SortSequence : record.BookEventId,
					Year = displayYear,
					EventType = record.EventType ?? string.Empty,
					Category = record.Category ?? string.Empty,
					Title = record.Title ?? string.Empty,
					BookTag = record.Tag ?? string.Empty,
					Body = displayBody,
					IconId = record.IconId ?? string.Empty,
					Importance = record.Importance,
					IsProtected = record.IsProtected,
					VisibilityFlags = subjectType == 0 ? (int)XjHistoryVisibility.Personal : subjectType == 1 ? (int)XjHistoryVisibility.Family : (int)XjHistoryVisibility.Sect,
					Result = record.Result ?? string.Empty,
					ActorId = record.ActorId,
					ActorName = record.ActorName ?? string.Empty,
					RelatedActorId = record.RelatedActorId,
					RelatedActorName = record.RelatedActorName ?? string.Empty,
					Location = cityName,
					HasLocation = record.HasLocation,
					LocationX = record.LocationX,
					LocationY = record.LocationY,
					SectId = record.SectId,
					SectName = sectName,
					RelatedSectId = record.RelatedSectId,
					RelatedSectName = relatedSectName,
					FamilyId = record.FamilyId,
					FamilyName = familyName,
					RelatedFamilyId = record.RelatedFamilyId,
					RelatedFamilyName = relatedFamilyName,
					CityId = record.CityId
				};
				result.Add(item);
				AccumulateHistorySubject(subjectMap, record.SubjectId,
					string.IsNullOrWhiteSpace(record.SubjectNameSnapshot) ? "未名" : record.SubjectNameSnapshot,
					item);
			}
		}
		if (subjectType == 0) SeedRequestedPersonalCurrentSummary(result, subjectMap);
		if (subjectType == 1) SeedLiveFamilyChronicleSummaries(result, subjectMap, familyNames);
		result.Sort(CompareHistoryNewestFirst);
		IReadOnlyDictionary<long, string> activeNames = subjectType == 1 ? familyNames : subjectType == 2 ? sectNames : null;
		SeedRequestedHistorySubject(subjectMap, subjectType, activeNames);
		subjects = FinalizeHistorySubjects(subjectMap, subjectType, activeNames);
		return result;
	}



	private static void SeedRequestedPersonalCurrentSummary(
		List<XjCodexHistoryItem> history,
		Dictionary<long, XjCodexHistorySubjectItem> subjects)
	{
		if (history == null || subjects == null) return;
		long actorId = XjThreeBookNavigationTarget.Read(0);
		if (actorId <= 0L || !subjects.TryGetValue(actorId, out XjCodexHistorySubjectItem subject)
			|| subject == null || subject.EventCount > 1
			|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive() || !HasAwakenedCultivationIdentity(actor)) return;
		string name = actor.getName();
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		string realm = XjRealmHelper.GetDisplayName(realmId);
		int shenTongCount = XjXianJiAccessor.BuildState(actor).Count;
		XjCodexHistoryItem item = new XjCodexHistoryItem
		{
			EventId = "personal-live-summary-" + actorId,
			SortSequence = long.MaxValue - actorId,
			Year = Math.Max(1, XjYearTracker.CurrentYear),
			EventType = "PersonalLiveSummary",
			Category = XjWorldHistoryCategory.Cultivation,
			Title = "修途今况",
			BookTag = "今况",
			Body = name + "现为" + (string.IsNullOrWhiteSpace(realm) ? "修士" : realm)
				+ "，已载神通" + shenTongCount + "门。此条为当前状态概览，不替代已经发生的生平旧事。",
			Importance = XjRealmHelper.GetOrder(realmId) >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu) ? 3 : 1,
			VisibilityFlags = (int)XjHistoryVisibility.Personal,
			ActorId = actorId,
			ActorName = name
		};
		history.Add(item);
		AccumulateHistorySubject(subjects, actorId, name, item);
	}

	private static void SeedLiveFamilyChronicleSummaries(
		List<XjCodexHistoryItem> history,
		Dictionary<long, XjCodexHistorySubjectItem> subjects,
		IReadOnlyDictionary<long, string> familyNames)
	{
		if (history == null || subjects == null || familyNames == null) return;
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		foreach (KeyValuePair<long, string> pair in familyNames)
		{
			long familyId = pair.Key;
			if (familyId <= 0L || !subjects.TryGetValue(familyId, out XjCodexHistorySubjectItem subject)
				|| subject == null || subject.EventCount > 1
				|| !XjFamilyMemberLedger.TryGetAggregate(familyId, out XjFamilyLedgerAggregate aggregate)) continue;
			string familyName = string.IsNullOrWhiteSpace(pair.Value) ? aggregate.DisplayName : pair.Value;
			if (IsPlaceholderFamilyName(familyName)) continue;
			XjCodexHistoryItem item = new XjCodexHistoryItem
			{
				EventId = "family-live-summary-" + familyId,
				SortSequence = long.MaxValue - familyId,
				Year = year,
				EventType = "FamilyLiveSummary",
				Category = XjWorldHistoryCategory.Family,
				Title = "家门今况",
				BookTag = "今况",
				Body = familyName + "现有族人" + aggregate.AliveCount + "人，其中修士" + aggregate.CultivatorCount
					+ "人、真人" + aggregate.ZiFuCount + "人、真君" + aggregate.JinDanCount + "人。此条为当前家门概况，不替代已经发生的旧事。",
				Importance = aggregate.JinDanCount > 0 ? 4 : aggregate.ZiFuCount > 0 ? 3 : 1,
				VisibilityFlags = (int)XjHistoryVisibility.Family,
				FamilyId = familyId,
				FamilyName = familyName
			};
			history.Add(item);
			AccumulateHistorySubject(subjects, familyId, familyName, item);
		}
	}

	private static int ResolveThreeBookChronologyYear(XjThreeBookArchiveRecord record)
	{
		if (record == null) return 0;
		int year = Math.Max(0, record.Year);
		bool sectFounded = string.Equals(record.EventType, XjThreeBookEventTypes.PersonalSectFounded, StringComparison.Ordinal)
			|| string.Equals(record.EventType, XjThreeBookEventTypes.FamilySectFounded, StringComparison.Ordinal)
			|| string.Equals(record.EventType, XjThreeBookEventTypes.SectFounded, StringComparison.Ordinal);
		if (!sectFounded || record.ActorId <= 0L
			|| !XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor founder)
			|| founder?.data == null)
		{
			return year;
		}
		int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(founder);
		return ziFuEnteredYear > 0 ? Math.Max(year, ziFuEnteredYear) : year;
	}

	private static IReadOnlyDictionary<long, int> BuildVisiblePersonalStartYears(IReadOnlyList<XjThreeBookArchiveRecord> records)
	{
		Dictionary<long, List<int>> birthYears = new Dictionary<long, List<int>>();
		Dictionary<long, List<int>> qualificationYears = new Dictionary<long, List<int>>();
		HashSet<long> subjectIds = new HashSet<long>();
		if (records == null) return new Dictionary<long, int>();

		for (int i = 0; i < records.Count; i++)
		{
			XjThreeBookArchiveRecord record = records[i];
			if (record == null || record.SubjectId <= 0L || record.Year <= 0) continue;
			subjectIds.Add(record.SubjectId);
			Dictionary<long, List<int>> target = null;
			if (string.Equals(record.EventType, XjThreeBookEventTypes.PersonalBirth, StringComparison.Ordinal)) target = birthYears;
			else if (string.Equals(record.EventType, XjThreeBookEventTypes.PersonalCultivationQualified, StringComparison.Ordinal)
				|| string.Equals(record.EventType, XjThreeBookEventTypes.PersonalShiEntered, StringComparison.Ordinal)
				|| string.Equals(record.EventType, XjThreeBookEventTypes.PersonalShiConversion, StringComparison.Ordinal)
				|| string.Equals(record.EventType, XjThreeBookEventTypes.PersonalShiBaseline, StringComparison.Ordinal))
			{
				// 释修以“入释/投释”作为独立修行资格起点。把它并入可见起点后，
				// 修士列传不仅能显示 PersonalShi 事务，也能保留其降生、交游等
				// 同一人物生平，而不要求伪造紫府金丹体系的资格事件。
				target = qualificationYears;
			}
			if (target == null) continue;
			if (!target.TryGetValue(record.SubjectId, out List<int> years))
			{
				years = new List<int>();
				target[record.SubjectId] = years;
			}
			years.Add(record.Year);
		}

		Dictionary<long, int> visibleStartYears = new Dictionary<long, int>();
		int currentYear = Math.Max(0, XjYearTracker.CurrentYear);
		foreach (long subjectId in subjectIds)
		{
			Actor actor = null;
			bool currentCultivator = XjActorRegistry.ResolveKnownOrWorld(subjectId, out actor)
				&& actor?.data != null && actor.isAlive() && HasAwakenedCultivationIdentity(actor);
			int latestBirth = FindLatestYear(birthYears, subjectId);
			int qualifiedYear = 0;
			if (currentCultivator)
			{
				int persisted = XjScheduler.ReadCultivationQualifiedYear(actor);
				if (persisted > 0 && (currentYear <= 0 || persisted <= currentYear)) qualifiedYear = persisted;
				if (qualifiedYear <= 0) qualifiedYear = FindEarliestYearAtOrAfter(qualificationYears, subjectId, latestBirth);
			}
			else
			{
				qualifiedYear = FindEarliestYearAtOrAfter(qualificationYears, subjectId, latestBirth);
			}
			if (qualifiedYear <= 0) continue;
			int birthYear = FindLatestBirthAtOrBefore(birthYears, subjectId, qualifiedYear);
			if (birthYear <= 0 && currentCultivator && currentYear > 0)
			{
				int inferred = Math.Max(0, currentYear - (int)Math.Floor(Math.Max(0f, actor.getAge())));
				if (inferred > 0 && inferred <= qualifiedYear) birthYear = inferred;
			}
			visibleStartYears[subjectId] = birthYear > 0 ? birthYear : qualifiedYear;
		}
		return visibleStartYears;
	}

	private static int FindLatestYear(IReadOnlyDictionary<long, List<int>> yearsBySubject, long subjectId)
	{
		if (yearsBySubject == null || !yearsBySubject.TryGetValue(subjectId, out List<int> years) || years == null) return 0;
		int best = 0;
		for (int i = 0; i < years.Count; i++) if (years[i] > best) best = years[i];
		return best;
	}

	private static int FindEarliestYearAtOrAfter(IReadOnlyDictionary<long, List<int>> yearsBySubject, long subjectId, int minimumYear)
	{
		if (yearsBySubject == null || !yearsBySubject.TryGetValue(subjectId, out List<int> years) || years == null) return 0;
		int best = 0;
		for (int i = 0; i < years.Count; i++)
		{
			int candidate = years[i];
			if (candidate <= 0 || minimumYear > 0 && candidate < minimumYear) continue;
			if (best <= 0 || candidate < best) best = candidate;
		}
		return best;
	}

	private static int FindLatestBirthAtOrBefore(IReadOnlyDictionary<long, List<int>> birthYears, long subjectId, int year)
	{
		if (birthYears == null || subjectId <= 0L || year <= 0
			|| !birthYears.TryGetValue(subjectId, out List<int> years) || years == null) return 0;
		int best = 0;
		for (int i = 0; i < years.Count; i++)
		{
			int candidate = years[i];
			if (candidate > 0 && candidate <= year && candidate > best) best = candidate;
		}
		return best;
	}

	private static bool HasAwakenedCultivationIdentity(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0) return true;
		return XjRealmSuppression.GetRealmTier(actor) > XjRealmSuppression.TierNone;
	}

	private static bool IsVisiblePersonalThreeBookRecord(XjThreeBookArchiveRecord record, IReadOnlyDictionary<long, int> visibleStartYears)
	{
		if (record == null || record.Year <= 0) return false;
		// 释修拥有独立的入释/投释资格链，不经过紫府金丹与服气体系的
		// PersonalCultivationQualified 入口。旧筛选因此把已经真实写入的
		// PersonalShi* 事件全部过滤掉，表现为“无可写入实录的旧事”。
		// 释修事件本身即是其修行身份成立后的事实，直接进入修士列传。
		if (!string.IsNullOrWhiteSpace(record.EventType)
			&& record.EventType.StartsWith("PersonalShi", StringComparison.Ordinal)) return true;
		if (visibleStartYears == null) return false;
		return visibleStartYears.TryGetValue(record.SubjectId, out int startYear)
			&& startYear > 0
			&& record.Year >= startYear;
	}


	private static int CompareHistoryNewestFirst(XjCodexHistoryItem left, XjCodexHistoryItem right)
	{
		if (ReferenceEquals(left, right)) return 0;
		if (left == null) return 1;
		if (right == null) return -1;
		int year = right.Year.CompareTo(left.Year);
		if (year != 0) return year;
		return right.SortSequence.CompareTo(left.SortSequence);
	}

	private static string ResolveHistorySubjectName(long id, string snapshotName, IReadOnlyDictionary<long, string> currentNames, string fallback)
	{
		if (id <= 0L) return string.Empty;
		if (currentNames != null && currentNames.TryGetValue(id, out string current) && !string.IsNullOrWhiteSpace(current)) return current.Trim();
		return string.IsNullOrWhiteSpace(snapshotName) ? fallback : snapshotName.Trim();
	}


	private static void AccumulateHistorySubject(
		Dictionary<long, XjCodexHistorySubjectItem> subjects,
		long subjectId,
		string subjectName,
		XjCodexHistoryItem history)
	{
		if (subjectId <= 0L || history == null) return;
		if (!subjects.TryGetValue(subjectId, out XjCodexHistorySubjectItem subject))
		{
			subject = new XjCodexHistorySubjectItem
			{
				SubjectId = subjectId,
				Name = string.IsNullOrWhiteSpace(subjectName) ? "未名" : subjectName.Trim()
			};
			subjects[subjectId] = subject;
		}
		else if ((string.IsNullOrWhiteSpace(subject.Name) || string.Equals(subject.Name, "未名", StringComparison.Ordinal))
			&& !string.IsNullOrWhiteSpace(subjectName))
		{
			subject.Name = subjectName.Trim();
		}
		AppendHistorySubjectSearchText(subject, subjectName);
		subject.EventCount++;
		if (history.Year > subject.LatestYear
			|| history.Year == subject.LatestYear && history.SortSequence > subject.LatestSortSequence)
		{
			subject.LatestYear = history.Year;
			subject.LatestSortSequence = history.SortSequence;
			subject.LatestTitle = string.IsNullOrWhiteSpace(history.Title) ? history.Body : history.Title;
		}
		subject.HighestImportance = Math.Max(subject.HighestImportance, history.Importance);
	}

	private static void SeedRequestedHistorySubject(
		Dictionary<long, XjCodexHistorySubjectItem> subjects,
		int subjectType,
		IReadOnlyDictionary<long, string> activeNames)
	{
		if (subjects == null) return;
		long requestedId = XjThreeBookNavigationTarget.Read(subjectType);
		if (requestedId <= 0L || subjects.ContainsKey(requestedId)) return;

		string name = string.Empty;
		bool valid = false;
		if (subjectType == 0)
		{
			valid = XjActorRegistry.ResolveKnownOrWorld(requestedId, out Actor actor)
				&& actor?.data != null
				&& actor.isAlive()
				&& HasAwakenedCultivationIdentity(actor);
			if (valid) name = actor.getName();
		}
		else
		{
			valid = true;
			if (activeNames != null && activeNames.TryGetValue(requestedId, out string currentName)) name = currentName;
			if (string.IsNullOrWhiteSpace(name) && subjectType == 1) name = XjFamilyDisplayNameResolver.Resolve(requestedId);
			if (string.IsNullOrWhiteSpace(name) && subjectType == 2 && XjSectRepository.TryGetBySectId(requestedId, out XjSectArchiveRecord sect) && sect != null)
			{
				name = sect.Name;
			}
		}
		if (!valid) return;
		if (subjectType == 1 && IsPlaceholderFamilyName(name)) return;

		XjCodexHistorySubjectItem subject = new XjCodexHistorySubjectItem
		{
			SubjectId = requestedId,
			Name = string.IsNullOrWhiteSpace(name) ? "未名" : name.Trim(),
			SearchText = string.IsNullOrWhiteSpace(name) ? requestedId.ToString(CultureInfo.InvariantCulture) : name.Trim(),
			EventCount = 0,
			LatestYear = 0,
			LatestSortSequence = 0L,
			LatestTitle = "尚无纪事",
			HighestImportance = 0,
			IsAliveOrActive = subjectType == 0 || activeNames != null && activeNames.ContainsKey(requestedId)
		};
		subjects[requestedId] = subject;
	}

	private static void AppendHistorySubjectSearchText(XjCodexHistorySubjectItem subject, string name)
	{
		if (subject == null || string.IsNullOrWhiteSpace(name)) return;
		string value = name.Trim();
		if (string.IsNullOrWhiteSpace(subject.SearchText))
		{
			subject.SearchText = value;
			return;
		}
		string padded = "|" + subject.SearchText + "|";
		if (padded.IndexOf("|" + value + "|", StringComparison.OrdinalIgnoreCase) < 0)
			subject.SearchText += "|" + value;
	}

	private static List<XjCodexHistorySubjectItem> FinalizeHistorySubjects(
		Dictionary<long, XjCodexHistorySubjectItem> subjects,
		int subjectType,
		IReadOnlyDictionary<long, string> activeNames)
	{
		List<XjCodexHistorySubjectItem> result = new List<XjCodexHistorySubjectItem>(subjects.Count);
		foreach (KeyValuePair<long, XjCodexHistorySubjectItem> pair in subjects)
		{
			XjCodexHistorySubjectItem subject = pair.Value;
			if (subject == null) continue;
			if (subjectType == 0)
			{
				subject.IsAliveOrActive = XjActorRegistry.ResolveKnownOrWorld(subject.SubjectId, out Actor actor)
					&& actor?.data != null && actor.isAlive();
				if (subject.IsAliveOrActive && actor != null)
				{
					string currentName = ResolveActorName(subject.SubjectId);
					if (!string.IsNullOrWhiteSpace(currentName))
					{
						AppendHistorySubjectSearchText(subject, subject.Name);
						subject.Name = currentName;
						AppendHistorySubjectSearchText(subject, currentName);
					}
				}
			}
			else
			{
				string currentName = string.Empty;
				subject.IsAliveOrActive = activeNames != null && activeNames.TryGetValue(subject.SubjectId, out currentName);
				if (subject.IsAliveOrActive && !string.IsNullOrWhiteSpace(currentName))
				{
					AppendHistorySubjectSearchText(subject, subject.Name);
					subject.Name = currentName.Trim();
					AppendHistorySubjectSearchText(subject, subject.Name);
				}
			}
			if (subjectType == 1 && IsPlaceholderFamilyName(subject.Name)) continue;
			result.Add(subject);
		}
		result.Sort((left, right) =>
		{
			int year = right.LatestYear.CompareTo(left.LatestYear);
			if (year != 0) return year;
			int sequence = right.LatestSortSequence.CompareTo(left.LatestSortSequence);
			if (sequence != 0) return sequence;
			int importance = right.HighestImportance.CompareTo(left.HighestImportance);
			if (importance != 0) return importance;
			int active = right.IsAliveOrActive.CompareTo(left.IsAliveOrActive);
			if (active != 0) return active;
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			return name != 0 ? name : left.SubjectId.CompareTo(right.SubjectId);
		});
		return result;
	}

	private static bool IsPlaceholderFamilyName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length != 2 || text[1] != '氏') return false;
		char marker = text[0];
		return marker >= 'A' && marker <= 'Z' || marker >= 'a' && marker <= 'z';
	}

private static List<XjCodexCenturyAnnalsItem> BuildCenturyAnnals(IReadOnlyList<XjCenturyAnnalsArchiveRecord> records)
	{
		List<XjCodexCenturyAnnalsItem> result = new List<XjCodexCenturyAnnalsItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturyAnnalsArchiveRecord record = records[i];
			if (record == null || record.CenturyId <= 0) continue;
			result.Add(new XjCodexCenturyAnnalsItem
			{
				CenturyId = record.CenturyId,
				StartYear = XjChronology.ToXuanJianYear(record.StartYear),
				EndYear = XjChronology.ToXuanJianYear(record.EndYear),
				GeneratedYear = XjChronology.ToXuanJianYear(record.GeneratedYear),
				IsCompleteCycle = record.IsCompleteCycle,
				Title = record.Title ?? string.Empty,
				WorldSummary = record.WorldSummary ?? string.Empty,
				FamilyCount = record.FamilyStates?.Count ?? 0,
				SectCount = record.SectSummaries?.Count ?? 0,
				ActorCount = record.RepresentativeActors?.Count ?? 0,
				Events = BuildCenturyEvents(record.NotableEvents),
				Families = BuildCenturyFamilies(record.FamilyStates),
				Sects = BuildCenturySummaries(record.SectSummaries),
				RealmStatistics = BuildCenturySummaries(record.RealmStatistics),
				DaoSummaries = BuildCenturySummaries(record.DaoSummaries),
				ArtifactSummaries = BuildCenturyArtifactSummaries(record.ArtifactSummaries),
				Actors = BuildCenturyActors(record.RepresentativeActors)
			});
		}
		// 世谱卷目按本局纪年自然顺序排列。存储层仍可用 ReadSnapshot(1) 读取最新卷，
		// 这里只调整玩家可见列表，避免“第二卷在第一卷上方”的卷目混乱。
		result.Sort((left, right) => left.CenturyId.CompareTo(right.CenturyId));
		return result;
	}

private static IReadOnlyList<XjCodexCenturyEventItem> BuildCenturyEvents(IReadOnlyList<XjCenturyEventRecord> records)
	{
		if (records == null || records.Count == 0) return Array.Empty<XjCodexCenturyEventItem>();
		List<XjCodexCenturyEventItem> result = new List<XjCodexCenturyEventItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturyEventRecord record = records[i];
			if (record == null || record.Year <= 0) continue;
			int displayYear = XjChronology.ToXuanJianYear(record.Year);
			string displaySummary = record.Summary ?? string.Empty;
			if (displayYear > 0 && displayYear != record.Year && displaySummary.Length > 0)
			{
				displaySummary = displaySummary.Replace("玄鉴历" + record.Year + "年", "玄鉴历" + displayYear + "年");
			}
			result.Add(new XjCodexCenturyEventItem
			{
				EventId = record.EventId,
				Year = displayYear,
				Category = record.Category ?? string.Empty,
				EventType = record.EventType ?? string.Empty,
				Title = record.Title ?? string.Empty,
				Summary = displaySummary,
				Importance = record.Importance,
				ActorId = record.ActorId,
				ActorName = record.ActorName ?? string.Empty,
				FamilyId = record.FamilyId,
				FamilyName = record.FamilyName ?? string.Empty,
				SectId = record.SectId,
				SectName = record.SectName ?? string.Empty
			});
		}
		return result;
	}

private static IReadOnlyList<XjCodexCenturyFamilyItem> BuildCenturyFamilies(IReadOnlyList<XjCenturyFamilyStateRecord> records)
	{
		if (records == null || records.Count == 0) return Array.Empty<XjCodexCenturyFamilyItem>();
		List<XjCodexCenturyFamilyItem> result = new List<XjCodexCenturyFamilyItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturyFamilyStateRecord record = records[i];
			if (record == null || record.FamilyStableId <= 0L) continue;
			result.Add(new XjCodexCenturyFamilyItem
			{
				FamilyId = record.FamilyStableId,
				FamilyName = record.FamilyName ?? string.Empty,
				PreviousStage = record.PreviousStage ?? string.Empty,
				CurrentStage = record.CurrentStage ?? string.Empty,
				IsExtinct = record.IsExtinct,
				AliveCount = record.AliveCount,
				CultivatorCount = record.CultivatorCount,
				ZiFuCount = record.ZiFuCount,
				JinDanCount = record.JinDanCount,
				HighestRealm = record.HighestRealm ?? string.Empty,
				SectId = record.SectId,
				SectName = record.SectName ?? string.Empty,
				VoiceTier = record.VoiceTier ?? string.Empty,
				InfluenceScore = record.InfluenceScore,
				GoverningCityCount = record.GoverningCityCount,
				RepresentativeActorName = record.RepresentativeActorName ?? string.Empty,
				StageReason = record.StageReason ?? string.Empty,
				Aspiration = ResolveFamilyAspirationDisplay(record.Aspiration),
				AspirationStatus = record.AspirationStatus ?? string.Empty,
				AspirationSinceYear = XjChronology.ToXuanJianYear(record.AspirationSinceYear),
				AspirationSummary = record.AspirationSummary ?? string.Empty,
				SectRelation = record.SectRelation ?? string.Empty,
				SupportedActorId = record.SupportedActorId,
				SupportedActorName = record.SupportedActorName ?? string.Empty,
				SupportPurpose = record.SupportPurpose ?? string.Empty,
				SupportedSinceYear = XjChronology.ToXuanJianYear(record.SupportedSinceYear),
				PillarActorId = record.PillarActorId,
				PillarActorName = record.PillarActorName ?? string.Empty,
				PillarRealm = record.PillarRealm ?? string.Empty,
				FamilyTreasureSummary = record.FamilyTreasureSummary ?? string.Empty
			});
		}
		return result;
	}

private static IReadOnlyList<XjCodexCenturyActorItem> BuildCenturyActors(IReadOnlyList<XjCenturyActorHighlightRecord> records)
	{
		if (records == null || records.Count == 0) return Array.Empty<XjCodexCenturyActorItem>();
		List<XjCodexCenturyActorItem> result = new List<XjCodexCenturyActorItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturyActorHighlightRecord record = records[i];
			if (record == null) continue;
			result.Add(new XjCodexCenturyActorItem
			{
				ActorId = record.ActorId,
				ActorName = record.ActorName ?? string.Empty,
				FamilyId = record.FamilyStableId,
				FamilyName = record.FamilyName ?? string.Empty,
				SectId = record.SectId,
				SectName = record.SectName ?? string.Empty,
				Realm = record.Realm ?? string.Empty,
				RealmOrder = record.RealmOrder,
				Reason = record.Reason ?? string.Empty
			});
		}
		return result;
	}

private static IReadOnlyList<XjCodexCenturySummaryItem> BuildCenturySummaries(IReadOnlyList<XjCenturySummaryItemRecord> records)
	{
		if (records == null || records.Count == 0) return Array.Empty<XjCodexCenturySummaryItem>();
		List<XjCodexCenturySummaryItem> result = new List<XjCodexCenturySummaryItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturySummaryItemRecord record = records[i];
			if (record == null) continue;
			result.Add(new XjCodexCenturySummaryItem
			{
				Key = record.Key ?? string.Empty,
				Name = record.Name ?? string.Empty,
				Score = record.Score,
				Trend = record.Trend ?? string.Empty,
				Summary = record.Summary ?? string.Empty
			});
		}
		return result;
	}

private static IReadOnlyList<XjCodexCenturySummaryItem> BuildCenturyArtifactSummaries(IReadOnlyList<XjCenturySummaryItemRecord> records)
	{
		if (records == null || records.Count == 0) return Array.Empty<XjCodexCenturySummaryItem>();
		List<XjCodexCenturySummaryItem> result = new List<XjCodexCenturySummaryItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XjCenturySummaryItemRecord record = records[i];
			if (record == null || ShouldHideCenturyArtifactSummary(record)) continue;
			result.Add(new XjCodexCenturySummaryItem
			{
				Key = record.Key ?? string.Empty,
				Name = record.Name ?? string.Empty,
				Score = record.Score,
				Trend = record.Trend ?? string.Empty,
				Summary = record.Summary ?? string.Empty
			});
		}
		return result;
	}

private static bool ShouldHideCenturyArtifactSummary(XjCenturySummaryItemRecord record)
	{
		string text = ((record?.Name ?? string.Empty) + " " + (record?.Summary ?? string.Empty)).Trim();
		return text.Length == 0
			|| text.Contains("洞天：无")
			|| text.Contains("洞天:无")
			|| text.Contains("涉洞天")
			|| text.Contains("身故")
			|| text.Contains("陨落");
	}

private static string TranslateSecretRealmStageForConflict(string stage)
	{
		return XjDisplayNameSanitizer.SecretRealmStage(stage);
	}

private static string TranslateYinSiMissionStageForConflict(string stage)
	{
		if (stage == XjYinSiMissionStage.Scheduled) return "追索已定";
		if (stage == XjYinSiMissionStage.Locating) return "定位追踪";
		if (stage == XjYinSiMissionStage.Manifesting) return "追索临身";
		if (stage == XjYinSiMissionStage.Pursuing) return "追杀";
		if (stage == XjYinSiMissionStage.Evaded) return "暂避";
		return "未知";
	}
}
