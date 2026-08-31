using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.DongTian;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectCityData
{
	#region 角色分配

	internal static void AssignRolesFromScratch(City city, int currentYear)
	{
		XjSectMaintenanceSnapshot snapshot = XjSectMaintenanceSnapshot.Build(city, currentYear);
		AssignRolesFromScratch(city, snapshot, currentYear);
	}

	internal static void AssignRolesFromScratch(City city, in XjSectMaintenanceSnapshot snapshot, int currentYear)
	{
		if (city?.data == null || !HasZongMen(city)) return;
		List<XjSectMemberMaintenanceSnapshot> members = snapshot.CollectValidMembersSorted();
		if (members.Count == 0)
		{
			SetZongZhuId(city, 0L);
			return;
		}

		city.data.set(KeyLastAssignPeriod, currentYear / RecruitIntervalYears);
		EnsureMainPeak(city);
		NormalizeJinDanAndZongZhuRoles(city, members, currentYear);
		TrimAndFillRegularPeaks(city, members, currentYear);

		List<int> regularPeakIds = GetRegularPeakIds(city);

		HashSet<long> fixedRoleIds = CollectFixedRoleIds(city);
		Queue<XjSectMemberMaintenanceSnapshot> queue = new Queue<XjSectMemberMaintenanceSnapshot>();
		for (int i = 0; i < members.Count; i++)
		{
			if (!fixedRoleIds.Contains(members[i].ActorId)) queue.Enqueue(members[i]);
		}

		List<int> distributionPeakIds = new List<int>();
		// 主峰与各支峰共同参与后续轮转；先为每座支峰补足最低弟子数，
		// 再把余下门人均匀分配。旧实现只要存在支峰就排除主峰，
		// 同时档案峰位又没有投影到城镇峰位，最终表现为全员主峰、支峰仅峰主。
		if (GetZongZhuId(city) > 0L) distributionPeakIds.Add(MainPeakId);
		for (int i = 0; i < regularPeakIds.Count; i++) distributionPeakIds.Add(regularPeakIds[i]);
		AssignMinimumRegularPeakDisciples(city, regularPeakIds, queue, currentYear);
		while (queue.Count > 0 && distributionPeakIds.Count > 0)
		{
			for (int i = 0; i < distributionPeakIds.Count && queue.Count > 0; i++)
			{
				XjSectMemberMaintenanceSnapshot member = queue.Dequeue();
				string reason = distributionPeakIds[i] == MainPeakId
					? "AssignMainPeakDisciple"
					: "AssignRegularPeakDisciple";
				XjSectMembershipService.AssignDisciple(city, distributionPeakIds[i], member.Actor, currentYear, reason);
			}
		}

		for (int i = 0; i < members.Count; i++)
		{
			if (members[i].Actor?.data != null)
			{
				XjSectMembershipService.ReconcileActorMirror(city, members[i].Actor, currentYear, "BalancedSectRoles");
			}
		}
	}

	private static void AssignMinimumRegularPeakDisciples(
		City city,
		List<int> regularPeakIds,
		Queue<XjSectMemberMaintenanceSnapshot> queue,
		int currentYear)
	{
		if (city?.data == null || regularPeakIds == null || queue == null || queue.Count == 0) return;
		Queue<XjSectMemberMaintenanceSnapshot> deferred = new Queue<XjSectMemberMaintenanceSnapshot>();
		for (int i = 0; i < regularPeakIds.Count && queue.Count > 0; i++)
		{
			int assigned = 0;
			int attempts = queue.Count;
			while (assigned < MinDisciplesPerPeak && queue.Count > 0 && attempts-- > 0)
			{
				XjSectMemberMaintenanceSnapshot member = queue.Dequeue();
				if (XjSectMembershipService.AssignDisciple(
						city,
						regularPeakIds[i],
						member.Actor,
						currentYear,
						"FillMinimumRegularPeakDisciples"))
				{
					assigned++;
				}
				else
				{
					deferred.Enqueue(member);
				}
			}
		}

		while (deferred.Count > 0)
		{
			queue.Enqueue(deferred.Dequeue());
		}
	}

	private static void NormalizeJinDanAndZongZhuRoles(City city, List<XjSectMemberMaintenanceSnapshot> members, int currentYear)
	{
		long currentZongZhuId = GetZongZhuId(city);
		bool currentZongZhuValid = false;
		for (int i = 0; i < members.Count; i++)
		{
			if (members[i].ActorId != currentZongZhuId || members[i].Actor?.data == null) continue;
			currentZongZhuValid = true;
			break;
		}

		// 境界晋升不是宗门降职理由。金丹、真君乃至道胎只要本来就是宗主，
		// 便继续主持宗门；只有宗主真实失效时才进行继任。
		if (!currentZongZhuValid)
		{
			SetZongZhuId(city, 0L);
			Actor successor = FindBestZongZhuSuccessor(members, currentZongZhuId);
			if (successor?.data != null)
			{
				XjSectMembershipService.AssignZongZhu(city, successor, currentYear, false, "SovereignVacancySuccession");
			}
		}

		bool hasHighRealm = false;
		long zongZhuId = GetZongZhuId(city);
		for (int i = 0; i < members.Count; i++)
		{
			if (!IsDongTianHighRealm(members[i].Actor)) continue;
			hasHighRealm = true;
			if (members[i].ActorId == zongZhuId) continue;
			if (XjSectAuthorityStore.TryGetMember(members[i].ActorId, out XjSectMemberArchiveRecord authority)
				&& string.Equals(XjSectMemberRole.Normalize(authority.Role), XjSectMemberRole.PeakMaster, StringComparison.Ordinal))
			{
				continue;
			}
			XjSectMembershipService.AssignSupremeElder(city, members[i].Actor, currentYear, "HighRealmEnterDongTian");
		}
		if (hasHighRealm) EnsureDongTianPeak(city);
	}

	private static Actor FindBestZongZhuSuccessor(List<XjSectMemberMaintenanceSnapshot> members, long excludedActorId)
	{
		for (int i = 0; i < members.Count; i++)
		{
			XjSectMemberMaintenanceSnapshot member = members[i];
			if (member.ActorId == excludedActorId || member.Actor?.data == null) continue;
			return member.Actor;
		}
		return null;
	}

	private static void TrimAndFillRegularPeaks(City city, List<XjSectMemberMaintenanceSnapshot> members, int currentYear)
	{
		List<int> regularPeakIds = GetRegularPeakIds(city);
		if (regularPeakIds.Count > MaxRegularPeakCount)
		{
			for (int i = regularPeakIds.Count - 1; i >= MaxRegularPeakCount; i--) RemovePeak(city, regularPeakIds[i]);
			regularPeakIds = GetRegularPeakIds(city);
		}

		HashSet<long> seniorIds = new HashSet<long>(ReadIdList(city, KeySupremeElders));
		long zongZhuId = GetZongZhuId(city);
		// 峰位由宗门档案决定，城镇层只负责弟子名单投影。不能再因为
		// 当前弟子不足五人就删除档案中已经存在且有合法峰主的支峰；
		// 小宗门允许支峰暂时人少，后续招募时再由年度分配补齐。

		HashSet<long> usedMasters = new HashSet<long>();
		for (int i = 0; i < regularPeakIds.Count; i++)
		{
			int peakId = regularPeakIds[i];
			if (!TryReadActorId(city, KeyPeakFengZhuPrefix + peakId, out long masterId)) continue;
			bool valid = masterId != zongZhuId && !seniorIds.Contains(masterId) && !usedMasters.Contains(masterId);
			if (valid)
			{
				Actor master = ResolveActor(masterId);
				valid = master?.data != null && master.isAlive() && IsCultivator(master);
			}
			if (valid) usedMasters.Add(masterId);
			else if (XjSectCommands.TryResolveSect(city, out XjSectArchiveRecord sect)) XjSectCommands.ClearPeakMaster(sect.SectId, peakId, currentYear);
		}

		for (int i = 0; i < regularPeakIds.Count; i++)
		{
			int peakId = regularPeakIds[i];
			if (TryReadActorId(city, KeyPeakFengZhuPrefix + peakId, out _)) continue;
			Actor candidate = FindPeakMasterCandidate(city, members, usedMasters);
			if (candidate?.data == null)
			{
				RemovePeak(city, peakId);
				continue;
			}
			XjSectMembershipService.AssignPeakMaster(city, peakId, candidate, currentYear, "FillVacantPeakMaster");
			usedMasters.Add(GetActorId(candidate));
		}
	}

	private static Actor FindPeakMasterCandidate(City city, List<XjSectMemberMaintenanceSnapshot> members, HashSet<long> usedMasters)
	{
		long zongZhuId = GetZongZhuId(city);
		HashSet<long> elders = new HashSet<long>(ReadIdList(city, KeySupremeElders));
		for (int i = 0; i < members.Count; i++)
		{
			XjSectMemberMaintenanceSnapshot member = members[i];
			if (member.ActorId == zongZhuId
				|| elders.Contains(member.ActorId)
				|| usedMasters.Contains(member.ActorId)
				|| IsDongTianHighRealm(member.Actor)
				|| member.Actor?.data == null)
			{
				continue;
			}
			if (member.RealmLevel >= ZiFuRealmLevel || (member.RealmLevel == 3 && member.IsFoundationLateOrHigher)) return member.Actor;
		}
		return null;
	}

	private static bool IsDongTianHighRealm(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive()) return false;
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return true;
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		return XjCultivationPathRules.IsJinDanEquivalentRealm(realmId);
	}

	private static HashSet<long> CollectFixedRoleIds(City city)
	{
		HashSet<long> result = new HashSet<long>();
		long zongZhuId = GetZongZhuId(city);
		if (zongZhuId > 0L) result.Add(zongZhuId);
		List<long> elders = ReadIdList(city, KeySupremeElders);
		for (int i = 0; i < elders.Count; i++) result.Add(elders[i]);
		List<int> regularPeakIds = GetRegularPeakIds(city);
		for (int i = 0; i < regularPeakIds.Count; i++)
		{
			if (TryReadActorId(city, KeyPeakFengZhuPrefix + regularPeakIds[i], out long peakMasterId)) result.Add(peakMasterId);
		}
		return result;
	}

	#endregion

	#region 招募

	private const int MaximumRecruitChecksPerCycle = 256;
	private const int MaximumExternalRecruitCitiesPerCycle = 12;
	internal const int MinimumHealthySectMembers = 18;
	private const int MaximumDesiredSectMembers = 60;

	private readonly struct XjSectRecruitCandidate
	{
		internal readonly long ActorId;
		internal readonly bool IsExternal;

		internal XjSectRecruitCandidate(long actorId, bool isExternal)
		{
			ActorId = actorId;
			IsExternal = isExternal;
		}
	}

	private readonly struct XjSectExternalCityPick
	{
		internal readonly long CityId;
		internal readonly long Score;

		internal XjSectExternalCityPick(long cityId, long score)
		{
			CityId = cityId;
			Score = score;
		}
	}

	/// <summary>
	/// 每个宗门招募周期领地优先，再从已有修士城市索引中确定性轮转最多12座外部城市；
	/// 总计最多检查256名候选，不扫描世界人口。外部普通招募只接纳无宗散修，绝不挖人。
	/// 正常纳新与家族连续性、师徒关系相互独立：后两者可以影响社会关系，但不能
	/// 取代宗门面对天下散修的常规扩纳。
	/// </summary>
	internal static int TryRecruitSectCultivators(City representativeCity, int currentYear, IReadOnlyDictionary<long, List<long>> cityIndex = null)
	{
		if (representativeCity?.data == null || !HasZongMen(representativeCity)) return 0;
		if (!XjSectRepository.TryGetByCity(representativeCity, out XjSectArchiveRecord sect)
			|| sect == null || sect.SectId <= 0L) return 0;

		HashSet<long> existing = new HashSet<long>(XjSectAuthorityStore.GetActorIdsForSect(sect.SectId));
		int startingMemberCount = existing.Count;
		if (startingMemberCount <= 0) return 0; // 真0人宗交由灭门规则，不凭空复活。

		List<XjSectRecruitCandidate> candidates = CollectRecruitmentCandidates(
			representativeCity, sect, cityIndex, currentYear,
			out int territoryCultivatorCount, out int externalCandidateCount);
		if (candidates.Count == 0) return 0;

		// 领地仍决定宗门的本地基础，但天下存在大量无宗散修时，宗门也应有向外择徒的
		// 正常成长空间。外部候选只来自已有稀疏城市索引，最多12城/256人，不扫描世界人口。
		int externalOpportunity = Math.Min(
			MaximumDesiredSectMembers - MinimumHealthySectMembers,
			externalCandidateCount / 3);
		int desiredMembers = Math.Clamp(
			8 + territoryCultivatorCount / 2 + externalOpportunity,
			MinimumHealthySectMembers,
			MaximumDesiredSectMembers);
		int shortage = desiredMembers - startingMemberCount;
		if (shortage <= 0) return 0;
		bool recovery = startingMemberCount < MinimumHealthySectMembers;
		int recruitTarget = Math.Min(recovery ? 12 : 6, shortage);
		if (recruitTarget <= 0) return 0;

		// 旧版终身拒绝账本继续废弃。常规纳新先给非创宗核心家族留出至少一半席位，
		// 再用全体无宗修士补齐；普通招募永远不会从其他宗门挖人。
		representativeCity.data.set(KeyJoinEvaluatedIds, string.Empty);
		int outsiderTarget = Math.Max(1, (recruitTarget + 1) / 2);
		int added = TryRecruitCandidatePass(
			representativeCity, sect, candidates, existing, currentYear, startingMemberCount,
			recovery, outsiderTarget, outsidersOnly: true);
		if (added < recruitTarget)
		{
			added += TryRecruitCandidatePass(
				representativeCity, sect, candidates, existing, currentYear, startingMemberCount + added,
				recovery, recruitTarget - added, outsidersOnly: false);
		}

		if (added > 0)
		{
			AssignRolesFromScratch(representativeCity, currentYear);
			ReconcileArchivePeaksForCity(representativeCity, currentYear);
		}
		return added;
	}

	private static List<XjSectRecruitCandidate> CollectRecruitmentCandidates(
		City representativeCity,
		XjSectArchiveRecord sect,
		IReadOnlyDictionary<long, List<long>> cityIndex,
		int currentYear,
		out int territoryCultivatorCount,
		out int externalCandidateCount)
	{
		territoryCultivatorCount = 0;
		externalCandidateCount = 0;
		List<City> cities = new List<City>();
		HashSet<long> territoryCityIds = new HashSet<long>();
		if (sect?.CityIds != null)
		{
			for (int i = 0; i < sect.CityIds.Count; i++)
			{
				long cityId = sect.CityIds[i];
				if (cityId <= 0L || !territoryCityIds.Add(cityId)
					|| !XjWorldLookupIndex.TryResolveCity(cityId, out City city) || city?.data == null) continue;
				cities.Add(city);
			}
		}
		long representativeId = GetCityId(representativeCity);
		if (representativeCity?.data != null && representativeId > 0L && territoryCityIds.Add(representativeId)) cities.Add(representativeCity);
		if (cities.Count == 0 && representativeCity?.data != null)
		{
			cities.Add(representativeCity);
			if (representativeId > 0L) territoryCityIds.Add(representativeId);
		}
		cities.Sort((left, right) => GetCityId(left).CompareTo(GetCityId(right)));

		List<XjSectRecruitCandidate> result = new List<XjSectRecruitCandidate>(Math.Min(MaximumRecruitChecksPerCycle, Math.Max(24, cities.Count * 24)));
		HashSet<long> seen = new HashSet<long>();
		int cityStart = cities.Count <= 1 ? 0 : XjDeterministicHash.PositiveIndex(
			sect.SectId + Math.Max(1, currentYear), "zongmen_recruit_territory_city", cities.Count);
		for (int cityOffset = 0; cityOffset < cities.Count; cityOffset++)
		{
			City city = cities[(cityStart + cityOffset) % cities.Count];
			if (city?.data == null) continue;
			long indexedCityId = GetCityId(city);
			if (cityIndex != null && indexedCityId > 0L && cityIndex.TryGetValue(indexedCityId, out List<long> indexedIds) && indexedIds != null)
			{
				territoryCultivatorCount += indexedIds.Count;
				AddIndexedRecruitCandidates(result, seen, indexedIds, sect.SectId, indexedCityId, currentYear, isExternal: false);
				if (result.Count >= MaximumRecruitChecksPerCycle) return result;
				continue;
			}

			// 读档极早期索引尚未回填时，只对宗门自己的少量领地城市做一次有界回退。
			if (city.units == null || city.units.Count == 0) continue;
			int localCount = city.units.Count;
			int localStart = localCount <= 1 ? 0 : XjDeterministicHash.PositiveIndex(
				sect.SectId + indexedCityId + currentYear, "zongmen_recruit_territory_fallback", localCount);
			for (int actorOffset = 0; actorOffset < localCount && result.Count < MaximumRecruitChecksPerCycle; actorOffset++)
			{
				Actor actor = city.units[(localStart + actorOffset) % localCount];
				if (actor?.data == null || !IsCultivator(actor)) continue;
				territoryCultivatorCount++;
				long actorId = GetActorId(actor);
				if (actorId > 0L && seen.Add(actorId)) result.Add(new XjSectRecruitCandidate(actorId, false));
			}
		}

		if (result.Count >= MaximumRecruitChecksPerCycle || cityIndex == null) return result;

		// 领地之外只查看“已有修士索引的城市”，按宗门+年份确定性选最多12座。
		// 选择器只保留12个最优分数，空间O(12)，不会为全部城市构造/排序大列表。
		List<XjSectExternalCityPick> externalCities = SelectExternalRecruitCities(sect, territoryCityIds, currentYear);
		for (int i = 0; i < externalCities.Count && result.Count < MaximumRecruitChecksPerCycle; i++)
		{
			long cityId = externalCities[i].CityId;
			if (!cityIndex.TryGetValue(cityId, out List<long> indexedIds) || indexedIds == null || indexedIds.Count == 0) continue;
			int before = result.Count;
			AddIndexedRecruitCandidates(result, seen, indexedIds, sect.SectId, cityId, currentYear, isExternal: true);
			externalCandidateCount += result.Count - before;
		}
		return result;
	}

	private static void AddIndexedRecruitCandidates(
		List<XjSectRecruitCandidate> result,
		HashSet<long> seen,
		IReadOnlyList<long> indexedIds,
		long sectId,
		long cityId,
		int currentYear,
		bool isExternal)
	{
		if (result == null || seen == null || indexedIds == null || indexedIds.Count == 0) return;
		int actorStart = indexedIds.Count <= 1 ? 0 : XjDeterministicHash.PositiveIndex(
			sectId + cityId + currentYear,
			isExternal ? "zongmen_recruit_external_actor" : "zongmen_recruit_territory_actor",
			indexedIds.Count);
		for (int actorOffset = 0; actorOffset < indexedIds.Count && result.Count < MaximumRecruitChecksPerCycle; actorOffset++)
		{
			long actorId = indexedIds[(actorStart + actorOffset) % indexedIds.Count];
			if (actorId > 0L && seen.Add(actorId)) result.Add(new XjSectRecruitCandidate(actorId, isExternal));
		}
	}

	private static List<XjSectExternalCityPick> SelectExternalRecruitCities(
		XjSectArchiveRecord sect,
		HashSet<long> territoryCityIds,
		int currentYear)
	{
		List<XjSectExternalCityPick> selected = new List<XjSectExternalCityPick>(MaximumExternalRecruitCitiesPerCycle);
		if (sect == null || sect.SectId <= 0L || !XjSectCultivatorCityIndex.HasCandidateCities) return selected;
		IReadOnlyCollection<long> candidateCityIds = XjSectCultivatorCityIndex.GetCandidateCityIds();
		foreach (long cityId in candidateCityIds)
		{
			if (cityId <= 0L || (territoryCityIds != null && territoryCityIds.Contains(cityId))) continue;
			long score = XjDeterministicHash.PositiveHash(
				sect.SectId ^ cityId ^ Math.Max(1, currentYear),
				"zongmen_recruit_external_city");
			InsertExternalCityPick(selected, new XjSectExternalCityPick(cityId, score));
		}
		return selected;
	}

	private static void InsertExternalCityPick(List<XjSectExternalCityPick> selected, XjSectExternalCityPick candidate)
	{
		int insert = 0;
		while (insert < selected.Count
			&& (selected[insert].Score < candidate.Score
				|| (selected[insert].Score == candidate.Score && selected[insert].CityId < candidate.CityId))) insert++;
		if (insert >= MaximumExternalRecruitCitiesPerCycle && selected.Count >= MaximumExternalRecruitCitiesPerCycle) return;
		selected.Insert(insert, candidate);
		if (selected.Count > MaximumExternalRecruitCitiesPerCycle) selected.RemoveAt(selected.Count - 1);
	}

	private static int TryRecruitCandidatePass(
		City representativeCity,
		XjSectArchiveRecord sect,
		IReadOnlyList<XjSectRecruitCandidate> candidates,
		HashSet<long> existing,
		int currentYear,
		int memberCountAtPassStart,
		bool recovery,
		int target,
		bool outsidersOnly)
	{
		if (target <= 0 || candidates == null || candidates.Count == 0) return 0;
		int added = 0;
		for (int i = 0; i < candidates.Count && added < target; i++)
		{
			XjSectRecruitCandidate candidate = candidates[i];
			long actorId = candidate.ActorId;
			if (actorId <= 0L || existing.Contains(actorId)) continue;
			Actor actor = ResolveActor(actorId);
			if (!IsRecruitableCultivator(actor, sect, candidate.IsExternal)) continue;
			bool coreFamily = IsCoreSectFamilyCandidate(sect, actorId);
			if (outsidersOnly && coreFamily) continue;

			bool priorityDaoTu = XjSectTransmissionCoverageSystem.IsPriorityRecruitmentDaoTu(sect.SectId, actor);
			double recruitChance;
			if (memberCountAtPassStart < 3)
			{
				recruitChance = candidate.IsExternal ? 0.85d : 1.00d;
			}
			else if (recovery)
			{
				recruitChance = candidate.IsExternal
					? (priorityDaoTu ? 0.75d : 0.60d)
					: (priorityDaoTu ? 0.90d : 0.75d);
			}
			else
			{
				recruitChance = candidate.IsExternal
					? (priorityDaoTu ? 0.50d : 0.30d)
					: (priorityDaoTu ? 0.80d : 0.50d);
			}
			if (!coreFamily) recruitChance = Math.Min(0.95d, recruitChance + 0.10d);
			float roll = XjDeterministicHash.Roll01(
				actorId, currentYear, "sect:" + sect.SectId,
				candidate.IsExternal ? "zongmen_recruit_external" : "zongmen_recruit_territory");
			if (roll > recruitChance) continue;
			string reason = candidate.IsExternal ? "RecruitExternalCultivator" : "RecruitTerritoryCultivator";
			if (!XjSectMembershipService.EnsureMember(representativeCity, actor, currentYear, reason)) continue;
			existing.Add(actorId);
			added++;
		}
		return added;
	}

	private static bool IsRecruitableCultivator(Actor actor, XjSectArchiveRecord sect, bool externalCandidate)
	{
		if (actor?.data == null || !actor.isAlive() || actor.city?.data == null || sect == null || sect.SectId <= 0L) return false;
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return false;
		if (!XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(actor)
			|| XjCultivationPathRules.IsShi(actor) || XjLongShuSystem.IsLongShu(actor)
			|| XjYinSiTraitLifecycle.IsYinSi(actor) || XjXianGuoSystem.IsDiMingYang(actor)) return false;
		if (XjSectAuthorityStore.TryGetSectId(actorId, out long occupiedSectId) && occupiedSectId > 0L) return false;

		if (externalCandidate) return true;
		// 本地候选重新确认当前仍在宗门领地，避免候选收集后迁城造成错误归类。
		long actorCityId = GetCityId(actor.city);
		return actorCityId > 0L
			&& (actorCityId == sect.CapitalCityId || (sect.CityIds != null && sect.CityIds.Contains(actorCityId)));
	}

	private static bool IsCoreSectFamilyCandidate(XjSectArchiveRecord sect, long actorId)
	{
		if (sect == null || actorId <= 0L
			|| !XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			|| familyId <= 0L) return false;
		return (sect.FounderFamilyId > 0L && familyId == sect.FounderFamilyId)
			|| (sect.DominantFamilyId > 0L && familyId == sect.DominantFamilyId);
	}


#endregion

	#region 晋升处理

	internal static bool TryPromoteZiFuInZongMen(Actor actor, City city, int currentYear)
	{
		if (actor?.data == null || city?.data == null || !HasZongMen(city)) return false;
		XjSectMembershipService.EnsureMember(city, actor, currentYear, "ZiFuPromotionEnsureMember");
		ReconcileArchivePeaksForCity(city, currentYear);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenZiFuPromotion");
		return true;
	}

	internal static int EnsureFamilyMembersInZongMen(
		City city,
		long familyId,
		int currentYear,
		string reason,
		bool preserveExistingSect = false)
	{
		if (city?.data == null || familyId <= 0L || !HasZongMen(city))
		{
			return 0;
		}

		long targetZongMenId = GetZongMenId(city);
		int added = 0;
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (member?.data == null || !member.isAlive() || !IsCultivator(member) || XjLongShuSystem.IsLongShu(member))
			{
				continue;
			}

			if (preserveExistingSect)
			{
				long memberId = GetActorId(member);
				if (XjSectAuthorityStore.TryGetSectId(memberId, out long existingSectId)
					&& existingSectId != targetZongMenId
					&& XjSectOwnership.TryResolvePrimaryCity(existingSectId, out _))
				{
					// Normal founding may gather unaffiliated relatives, but it must not
					// tear established members out of another valid sect and abandon
					// that sect's accumulated property. Explicit soft-split logic may
					// still opt into a full transfer by leaving this flag false.
					continue;
				}
			}

			if (XjSectMembershipService.EnsureMember(city, member, currentYear, reason))
			{
				added++;
			}
		}
		if (added > 0)
		{
			AssignRolesFromScratch(city, currentYear);
			ReconcileArchivePeaksForCity(city, currentYear);
		}
		return added;
	}

	internal static bool TryPromoteFoundationLateInZongMen(Actor actor, City city, int currentYear)
	{
		if (actor?.data == null || city?.data == null || !HasZongMen(city) || !IsMember(city, actor)) return false;
		ReconcileArchivePeaksForCity(city, currentYear);
		return true;
	}

	internal static bool TryPromoteJinDanInZongMen(Actor actor, City city, int currentYear)
	{
		if (actor?.data == null || city?.data == null || XjLongShuSystem.IsLongShu(actor)) return false;
		if (!HasZongMen(city))
		{
			if (TryFindKingdomSectZongMenCity(actor, out City existingSectCity))
			{
				return TryPromoteJinDanInZongMen(actor, existingSectCity, currentYear);
			}

			bool requested = XjNationSectTransitionSystem.RequestFromZiFuPromotion(actor, currentYear);
			if (requested) XjNationSectTransitionSystem.Tick(1);
			return requested;
		}
		XjSectMembershipService.EnsureMember(city, actor, currentYear, "JinDanPromotionEnsureMember");

		// 公告只认“本次晋升前无洞天、晋升后新建洞天”的状态跃迁。
		// 不能直接信任维护函数的中间返回值，否则旧存档迁移、峰位归一化
		// 或重复晋升补偿都可能把已有洞天误判成新建并再次弹出提示。
		bool hadDongTianBeforePromotion = HasDongTianPeak(city);
		EnsureDongTianPeak(city);
		bool createdDongTianThisPromotion = !hadDongTianBeforePromotion && HasDongTianPeak(city);

		ReconcileArchivePeaksForCity(city, currentYear);
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenJinDanPromotion");
		XjSectDongTianLifecycle.TryStartImmediately(actor, currentYear);
		return createdDongTianThisPromotion;
	}

	private static void ReconcileArchivePeaksForCity(City city, int currentYear)
	{
		if (city?.data == null || !XjSectRepository.TryGetByCity(city, out var sect) || sect == null)
		{
			return;
		}

		XjSectRepository.ReconcilePeaks(sect.SectId, currentYear);
	}

	internal static bool IsCultivator(Actor actor)
	{
		return actor?.data != null
			&& actor.isAlive()
			&& XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			&& !XjLongShuSystem.IsLongShu(actor)
			// 释修制度上脱离玄门宗门。旧实现先把释修放进256人候选窗口，
			// 到 EnrollMember 才拒绝，修士密集城市会白白耗尽整轮招募检查。
			&& !XjCultivationPathRules.IsShi(actor)
			&& XjCultivatorCache.IsCultivator(GetActorId(actor));
	}

	internal static bool TryFindActorZongMenCity(Actor actor, out City zongMenCity)
	{
		zongMenCity = null;
		if (actor?.data == null) return false;

		long actorId = GetActorId(actor);
		City currentCity = actor.city;
		if (XjSectAuthorityStore.TryGetSectId(actorId, out long sectId)
			&& XjSectOwnership.TryResolvePrimaryCity(sectId, out City storedCity))
		{
			zongMenCity = storedCity;
			return true;
		}

		if (currentCity?.data != null && HasZongMen(currentCity) && IsMember(currentCity, actor))
		{
			XjSectMembershipService.ReconcileActorMirror(currentCity, actor, GetCurrentYearOrZero(), "LegacyMemberMirrorRestore");
			zongMenCity = currentCity;
			return true;
		}
		return false;
	}

	internal static bool HandleZiFuPromotion(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjLongShuSystem.IsLongShu(actor)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
			&& XjFamilySectContinuity.TryJoinPreferredFamilySect(actor, familyId, currentYear))
		{
			// A newly promoted ZiFu follows the stable sect line already held by
			// the family's other high-realm cultivators. This check deliberately
			// precedes the actor's old disciple identity so one isolated member
			// cannot found a new sect and pull the whole family away again.
			return true;
		}

		// If no other high-realm relative establishes a canonical sect, retain
		// the actor's own valid membership before considering current-city founding.
		if (TryFindActorZongMenCity(actor, out City city))
		{
			return TryPromoteZiFuInZongMen(actor, city, currentYear);
		}

		City currentCity = actor.city;
		if (currentCity?.data != null && HasZongMen(currentCity))
		{
			// 当前城已经有宗门：加入现有宗门并结算职阶，不再调用创宗链，
			// 因而不会重复生成宗门数据或发布“创立宗门”公告。
			return TryPromoteZiFuInZongMen(actor, currentCity, currentYear);
		}

		if (TryFindKingdomSectZongMenCity(actor, out City existingSectCity))
		{
			// 独立宗门层只承认角色、城镇已经写入的宗门归属。
			// 原生国家外壳相同不能再作为入宗依据。
			return TryPromoteZiFuInZongMen(actor, existingSectCity, currentYear);
		}

		if (currentCity?.data == null) return false;
		bool requested = XjNationSectTransitionSystem.RequestFromZiFuPromotion(actor, currentYear);
		if (requested) XjNationSectTransitionSystem.Tick(1);
		return requested;
	}

	internal static bool HandleFoundationLatePromotion(Actor actor, int currentYear)
	{
		return !XjLongShuSystem.IsLongShu(actor)
			&& TryFindActorZongMenCity(actor, out City city)
			&& TryPromoteFoundationLateInZongMen(actor, city, currentYear);
	}

	internal static bool HandleJinDanPromotion(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjLongShuSystem.IsLongShu(actor)) return false;
		if (TryFindActorZongMenCity(actor, out City city)) return TryPromoteJinDanInZongMen(actor, city, currentYear);

		city = actor.city;
		if (city?.data == null) return false;
		if (!HasZongMen(city))
		{
			if (TryFindKingdomSectZongMenCity(actor, out City existingSectCity))
			{
				return TryPromoteJinDanInZongMen(actor, existingSectCity, currentYear);
			}

			bool requested = XjNationSectTransitionSystem.RequestFromZiFuPromotion(actor, currentYear);
			if (requested) XjNationSectTransitionSystem.Tick(1);
			return requested;
		}
		return TryPromoteJinDanInZongMen(actor, city, currentYear);
	}

	private static bool TryFindKingdomSectZongMenCity(Actor actor, out City zongMenCity)
	{
		zongMenCity = null;
		if (actor?.data == null) return false;
		City currentCity = actor.city;
		XjSectArchiveRecord sect = null;
		if (currentCity?.data != null)
		{
			XjSectRepository.TryGetByCity(currentCity, out sect);
		}
		if (sect == null)
		{
			if (!XjSectRepository.TryGetByActor(actor, out sect)) return false;
			if (sect == null) return false;
		}

		if (TryResolveZongMenCity(sect.SectId, out City storedCity))
		{
			zongMenCity = storedCity;
			return true;
		}

		if (!XjWorldLookupIndex.TryResolveCity(sect.CapitalCityId, out City candidate)
			|| candidate?.data == null
			|| !HasZongMen(candidate))
		{
			return false;
		}

		zongMenCity = candidate;
		return true;
	}

	#endregion
}
