using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.DongTian;

/// <summary>
/// 不固定名称、不固定现世年份的轻量事件洞天。世界年度车道只从真人级缓存选择
/// 一名紫府/真人，再从其家族、城市或修士索引中限量选择下修，不扫描全世界单位。
/// </summary>
internal static class XjEventDongTianSystem
{
	private const int MaxRecords = 128;
	private const int MinSpawnIntervalYears = 60;
	private const int SpawnIntervalSpreadYears = 121;
	private const int MinClueYears = 3;
	private const int ClueYearSpread = 5;
	private const int MaxLowerCultivators = 3;
	private const int CandidateBudget = 128;

	private static readonly string[] CanonicalEventDongTianNames =
	{
		"玄霞洞天", "栖霞洞天", "青萍洞天", "白石洞天", "镜湖洞天", "寒松洞天",
		"紫烟洞天", "归云洞天", "玉泉洞天", "丹崖洞天", "星渊洞天", "月窟洞天",
		"苍梧洞天", "流霞洞天", "太素洞天", "金庭洞天", "碧涧洞天", "灵台洞天"
	};

	private static readonly string[] NameOpenings =
	{
		"玄霭", "栖霞", "青萝", "白石", "镜湖", "寒松", "紫烟", "归云", "玉泉", "丹崖",
		"星潭", "月窟", "苍梧", "流霞", "太素", "金庭", "碧涧", "灵台"
	};
	private static readonly string[] NameEndings =
	{
		"遗府", "古洞", "秘藏", "玄宫", "别院", "旧庭", "石室", "洞庐", "真境", "云窟"
	};

	private static XjEventDongTianWorldArchiveData _state = new XjEventDongTianWorldArchiveData();

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0 || _state.LastProcessedYear >= currentYear) return;
		_state.LastProcessedYear = currentYear;
		ResolveDueRecord(currentYear);
		if (HasActiveRecord()) return;

		if (_state.NextSpawnYear <= 0)
		{
			_state.NextSpawnYear = currentYear + ResolveNextInterval(currentYear, "initial");
			MarkChanged();
			return;
		}
		if (currentYear < _state.NextSpawnYear) return;

		if (!TryCreateRecord(currentYear))
		{
			// 当前没有紫府/真人或没有可用下修时，不在同一年反复重试。
			_state.NextSpawnYear = currentYear + 10;
			MarkChanged();
			return;
		}
		_state.NextSpawnYear = currentYear + ResolveNextInterval(currentYear, "next");
		PruneRecords();
		MarkChanged();
	}

	private static bool TryCreateRecord(int currentYear)
	{
		if (!TrySelectUpperCultivator(currentYear, out Actor upper)) return false;
		List<Actor> lowers = SelectLowerCultivators(upper, currentYear);
		if (lowers.Count == 0) return false;

		long upperId = ((BaseSystemData)upper.data).id;
		string recordId = "event_dongtian_" + currentYear + "_" + upperId;
		string displayName = GenerateName(upperId, currentYear);
		XjEventDongTianArchiveRecord record = new XjEventDongTianArchiveRecord
		{
			RecordId = recordId,
			DisplayName = displayName,
			State = XjEventDongTianState.ClueGathering,
			CreatedYear = currentYear,
			ResolveYear = currentYear + MinClueYears
				+ XjDeterministicHash.PositiveIndex(upperId + currentYear, "event_dongtian_clue_years", ClueYearSpread),
			UpperActorId = upperId,
			UpperActorName = SafeActorName(upper),
			UpperRealmId = XjRealmHelper.GetUnifiedId(upper, XjRealmHelper.GetTraitSnapshotForRouter),
			AnchorCityId = upper.city?.data?.id ?? 0L,
			AnchorCityName = SafeCityName(upper)
		};
		for (int i = 0; i < lowers.Count; i++)
		{
			record.LowerActorIds.Add(((BaseSystemData)lowers[i].data).id);
			record.LowerActorNames.Add(SafeActorName(lowers[i]));
		}
		_state.Records ??= new List<XjEventDongTianArchiveRecord>();
		_state.Records.Add(record);

		string lowerNames = string.Join("、", record.LowerActorNames);
		string body = record.UpperActorName + "察觉天地间一处来历不明的洞天线索，遣"
			+ lowerNames + "循迹访查，待诸般残图与地脉征兆相互勾连后再定门户。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			displayName + "线索初现",
			body,
			3,
			actorId: upperId,
			actorName: record.UpperActorName,
			cityId: record.AnchorCityId,
			year: currentYear,
			eventType: "EventDongTianClueGathering",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
		XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
			"【洞天线索】" + body,
			XjAnnouncementCategory.DongTian,
			duration: 5.5f,
			iconId: XjEventIconCatalog.DongTianOpen);
		return true;
	}

	private static void ResolveDueRecord(int currentYear)
	{
		XjEventDongTianArchiveRecord record = GetActiveRecord();
		if (record == null || currentYear < record.ResolveYear) return;

		if (!XjActorRegistry.ResolveKnownOrWorld(record.UpperActorId, out Actor upper)
			|| !XjSafeCore.IsAliveActor(upper))
		{
			ResolveForLowerCultivators(record, currentYear, "上修已不在世，线索最终为下修所得");
			return;
		}

		List<Actor> lowers = ResolveAliveLowers(record.LowerActorIds);
		if (lowers.Count == 0)
		{
			CloseRecord(record, currentYear, false, "None", "搜寻线索的下修尽数失联，洞天门户重新隐没。", upper);
			return;
		}

		int successBasisPoints = 4200;
		for (int i = 0; i < lowers.Count; i++)
		{
			XjActorAccessor.TryGetFloat(lowers[i], XjActorDataKeys.HuiGuang, out float huiGuang);
			XjActorAccessor.TryGetFloat(lowers[i], XjActorDataKeys.MingShuCongenital, out float congenital);
			XjActorAccessor.TryGetFloat(lowers[i], XjActorDataKeys.MingShuAcquired, out float acquired);
			successBasisPoints += (int)Math.Clamp(huiGuang * 8f + (congenital + acquired) * 3f, 0f, 900f);
			if (ReferenceEquals(lowers[i].city, upper.city) && upper.city != null) successBasisPoints += 250;
		}
		successBasisPoints = Math.Min(8500, successBasisPoints);
		bool success = XjDeterministicHash.PositiveIndex(
			record.UpperActorId + currentYear,
			"event_dongtian_link|" + record.RecordId,
			10000) < successBasisPoints;
		if (!success)
		{
			ResolveForLowerCultivators(record, currentYear, "上修与下修勾连未成，洞天机缘反落于探路者");
			return;
		}

		ApplyUpperReward(record, upper, currentYear);
	}

	private static void ApplyUpperReward(XjEventDongTianArchiveRecord record, Actor upper, int currentYear)
	{
		int rewardRoll = XjDeterministicHash.PositiveIndex(
			record.UpperActorId + currentYear,
			"event_dongtian_upper_reward|" + record.RecordId,
			3);
		string rewardType;
		string rewardText;
		if (rewardRoll == 0 && TryGrantZiFuLingWu(upper, currentYear, out rewardText))
		{
			rewardType = "ZiFuLingWu";
		}
		else if (rewardRoll == 2
			&& XjZiFuProgression.TryGrantEventDongTianShenTong(upper, currentYear, out string shenTongId))
		{
			rewardType = "ShenTongTrade";
			rewardText = "于洞天残主留下的交易中直接换得神通“" + shenTongId + "”";
		}
		else
		{
			rewardType = "ShenTongComprehension";
			XjEventDongTianBonusService.GrantShenTongComprehensionBenefit(upper, currentYear);
			// 真人没有紫府式多神通池，洞天机缘改为加速当前金性温养。
			if (XjCultivationPathRules.IsFuQiYangXing(upper)
				&& XjFuQiBalancePolicy.CanAcceleratePostFailureNurture(upper, currentYear)
				&& XjActorAccessor.TryGetInt(upper, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int completeYear)
				&& completeYear > currentYear)
			{
				XjActorAccessor.SetInt(upper, XjActorDataKeys.FuQiJinXingNurtureCompleteYear,
					Math.Max(currentYear + 1, completeYear - 10));
				rewardText = "洞天道痕助其温养神妙，金性化生进程提前十年";
			}
			else
			{
				rewardText = "得五十年洞天参悟助力，后续神通参悟概率提高";
			}
		}
		CloseRecord(record, currentYear, true, rewardType,
			record.UpperActorName + "凭下修所集线索寻得" + record.DisplayName + "，" + rewardText + "。", upper);
	}

	private static bool TryGrantZiFuLingWu(Actor upper, int currentYear, out string rewardText)
	{
		rewardText = string.Empty;
		if (upper?.data == null
			|| !XjActorAccessor.TryGetString(upper, XjActorDataKeys.DaoTu, out string daoTu)
			|| !XjLingWuCatalog.TryResolveByDaoTu(daoTu, out XjLingWuDef definition)) return false;
		long actorId = ((BaseSystemData)upper.data).id;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L
			|| !XjFamilyLingWuWarehouse.TryAddLingWu(
				familyId, definition, actorId, SafeActorName(upper), currentYear)) return false;
		rewardText = "得紫府灵物“" + definition.Name + "”，已收入家族重宝仓库";
		return true;
	}

	private static void ResolveForLowerCultivators(XjEventDongTianArchiveRecord record, int currentYear, string reason)
	{
		List<Actor> lowers = ResolveAliveLowers(record.LowerActorIds);
		for (int i = 0; i < lowers.Count; i++)
		{
			Actor lower = lowers[i];
			XjEventDongTianBonusService.GrantLowerCultivatorBenefits(lower, currentYear);
			long actorId = ((BaseSystemData)lower.data).id;
			int huiGuangGain = 2 + XjDeterministicHash.PositiveIndex(actorId + currentYear,
				"event_dongtian_lower_huiguang|" + record.RecordId, 4);
			int mingShuGain = 5 + XjDeterministicHash.PositiveIndex(actorId + currentYear,
				"event_dongtian_lower_mingshu|" + record.RecordId, 8);
			XjActorAccessor.TryGetFloat(lower, XjActorDataKeys.HuiGuang, out float huiGuang);
			XjActorAccessor.SetFloat(lower, XjActorDataKeys.HuiGuang, XjDaoHuiPolicy.Add(huiGuang, huiGuangGain, XjDaoHuiPolicy.RareGrowthCeiling));
			XjMingShuState.AddAcquired(lower, mingShuGain);
		}
		string names = lowers.Count == 0 ? "探路下修" : string.Join("、", lowers.ConvertAll(SafeActorName));
		Actor focus = lowers.Count > 0 ? lowers[0] : null;
		CloseRecord(record, currentYear, false, "LowerCultivatorBenefit",
			reason + "；" + names + "各得五十年破境助力，紫府成功率额外提高，并增长道慧与命数。", focus);
	}

	private static void CloseRecord(
		XjEventDongTianArchiveRecord record,
		int currentYear,
		bool success,
		string rewardType,
		string outcome,
		Actor focus)
	{
		record.State = XjEventDongTianState.Resolved;
		record.ResolvedYear = currentYear;
		record.UpperLowerLinkSucceeded = success;
		record.RewardType = rewardType ?? string.Empty;
		record.OutcomeText = outcome ?? string.Empty;
		long actorId = focus?.data == null ? record.UpperActorId : ((BaseSystemData)focus.data).id;
		string actorName = focus?.data == null ? record.UpperActorName : SafeActorName(focus);
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			record.DisplayName + "机缘落定",
			record.OutcomeText,
			4,
			actorId: actorId,
			actorName: actorName,
			cityId: record.AnchorCityId,
			year: currentYear,
			eventType: success ? "EventDongTianUpperReward" : "EventDongTianLowerReward",
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate));
		XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
			"【洞天机缘】" + record.OutcomeText,
			XjAnnouncementCategory.DongTian,
			duration: 5.5f,
			iconId: XjEventIconCatalog.DongTianOpen);
		XjSemanticDiagnostics.RecordEvent("event_dongtian", success ? rewardType : "lower_or_failed:" + rewardType);
		MarkChanged();
	}

	private static bool TrySelectUpperCultivator(int currentYear, out Actor upper)
	{
		upper = null;
		IReadOnlyList<long> ids = XjCultivatorCache.GetZhenRenOrHigherIds();
		if (ids.Count == 0) return false;
		int start = XjDeterministicHash.PositiveIndex(currentYear, "event_dongtian_upper_start", ids.Count);
		int checks = Math.Min(ids.Count, CandidateBudget);
		for (int i = 0; i < checks; i++)
		{
			long id = ids[(start + i) % ids.Count];
			if (!XjActorRegistry.ResolveKnownOrWorld(id, out Actor actor) || !XjSafeCore.IsAliveActor(actor)) continue;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
				|| string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
			{
				upper = actor;
				return true;
			}
		}
		return false;
	}

	private static List<Actor> SelectLowerCultivators(Actor upper, int currentYear)
	{
		List<Actor> result = new List<Actor>(MaxLowerCultivators);
		long upperId = ((BaseSystemData)upper.data).id;
		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(upperId, out long familyId))
		{
			int familyChecks = 0;
			foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
			{
				if (++familyChecks > CandidateBudget) break;
				TryAddLowerCandidate(result, member, upper);
				if (result.Count >= MaxLowerCultivators) return result;
			}
		}

		IReadOnlyList<long> ids = XjCultivatorCache.GetAllIds();
		if (ids.Count == 0) return result;
		int start = XjDeterministicHash.PositiveIndex(upperId + currentYear, "event_dongtian_lower_start", ids.Count);
		int checks = Math.Min(ids.Count, CandidateBudget);
		// 同城候选优先。
		for (int pass = 0; pass < 2 && result.Count < MaxLowerCultivators; pass++)
		{
			for (int i = 0; i < checks && result.Count < MaxLowerCultivators; i++)
			{
				if (!XjActorRegistry.ResolveKnownOrWorld(ids[(start + i) % ids.Count], out Actor actor)) continue;
				if (pass == 0 && !ReferenceEquals(actor?.city, upper.city)) continue;
				TryAddLowerCandidate(result, actor, upper);
			}
		}
		return result;
	}

	private static void TryAddLowerCandidate(List<Actor> result, Actor actor, Actor upper)
	{
		if (!XjSafeCore.IsAliveActor(actor) || ReferenceEquals(actor, upper)) return;
		int tier = XjRealmSuppression.GetRealmTier(actor);
		if (tier <= XjRealmSuppression.TierNone || tier >= XjRealmSuppression.TierZiFu) return;
		for (int i = 0; i < result.Count; i++) if (ReferenceEquals(result[i], actor)) return;
		result.Add(actor);
	}

	private static List<Actor> ResolveAliveLowers(IReadOnlyList<long> ids)
	{
		List<Actor> result = new List<Actor>(MaxLowerCultivators);
		if (ids == null) return result;
		for (int i = 0; i < ids.Count && result.Count < MaxLowerCultivators; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor) && XjSafeCore.IsAliveActor(actor)) result.Add(actor);
		}
		return result;
	}

	private static bool HasActiveRecord() => GetActiveRecord() != null;

	private static XjEventDongTianArchiveRecord GetActiveRecord()
	{
		if (_state?.Records == null) return null;
		for (int i = _state.Records.Count - 1; i >= 0; i--)
		{
			XjEventDongTianArchiveRecord record = _state.Records[i];
			if (record != null && string.Equals(record.State, XjEventDongTianState.ClueGathering, StringComparison.Ordinal)) return record;
		}
		return null;
	}

	private static int ResolveNextInterval(int currentYear, string salt)
	{
		return MinSpawnIntervalYears + XjDeterministicHash.PositiveIndex(currentYear,
			"event_dongtian_interval|" + salt, SpawnIntervalSpreadYears);
	}

	private static string GenerateName(long actorId, int currentYear)
	{
		if (CanonicalEventDongTianNames.Length > 0)
		{
			return CanonicalEventDongTianNames[XjDeterministicHash.PositiveIndex(actorId + currentYear,
				"event_dongtian_canonical_name", CanonicalEventDongTianNames.Length)];
		}

		string opening = NameOpenings[XjDeterministicHash.PositiveIndex(actorId + currentYear,
			"event_dongtian_name_opening", NameOpenings.Length)];
		string ending = NameEndings[XjDeterministicHash.PositiveIndex(actorId + currentYear * 31L,
			"event_dongtian_name_ending", NameEndings.Length)];
		return opening + ending;
	}

	private static void PruneRecords()
	{
		if (_state.Records == null || _state.Records.Count <= MaxRecords) return;
		for (int i = 0; i < _state.Records.Count && _state.Records.Count > MaxRecords;)
		{
			if (_state.Records[i] == null
				|| string.Equals(_state.Records[i].State, XjEventDongTianState.Resolved, StringComparison.Ordinal))
			{
				_state.Records.RemoveAt(i);
			}
			else i++;
		}
	}

	internal static XjEventDongTianWorldArchiveData ExportState()
	{
		return (_state ?? new XjEventDongTianWorldArchiveData()).Clone();
	}

	internal static void ImportState(XjEventDongTianWorldArchiveData state)
	{
		_state = state?.Clone() ?? new XjEventDongTianWorldArchiveData();
		_state.Records ??= new List<XjEventDongTianArchiveRecord>();
		PruneRecords();
	}

	internal static void Clear() => _state = new XjEventDongTianWorldArchiveData();

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static string SafeActorName(Actor actor)
	{
		try { return actor?.getName() ?? "无名修士"; }
		catch { return "无名修士"; }
	}

	private static string SafeCityName(Actor actor)
	{
		try { return actor?.city?.data == null ? string.Empty : ((BaseSystemData)actor.city.data).name ?? string.Empty; }
		catch { return string.Empty; }
	}
}
