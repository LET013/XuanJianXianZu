using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.ZongMen;

internal static partial class XjZongMenCityData
{
	#region 角色分配

	internal static void AssignRolesFromScratch(City city, int currentYear)
	{
		XjZongMenMaintenanceSnapshot snapshot = XjZongMenMaintenanceSnapshot.Build(city, currentYear);
		AssignRolesFromScratch(city, snapshot, currentYear);
	}

	internal static void AssignRolesFromScratch(City city, in XjZongMenMaintenanceSnapshot snapshot, int currentYear)
	{
		if (city?.data == null || !HasZongMen(city)) return;
		List<XjZongMenMemberSnapshot> members = snapshot.CollectValidMembersSorted();
		if (members.Count == 0)
		{
			SetZongZhuId(city, 0L);
			return;
		}

		city.data.set(KeyLastAssignPeriod, currentYear / RecruitIntervalYears);
		EnsureMainPeak(city);
		NormalizeJinDanAndZongZhuRoles(city, members, currentYear);
		TrimAndFillRegularPeaks(city, members, currentYear);

		List<int> peakIds = ReadPeakIds(city);
		List<int> regularPeakIds = GetRegularPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			WriteIdList(city, KeyPeakDisciplePrefix + peakIds[i], new List<long>());
			WriteIdList(city, KeyPeakInnerPrefix + peakIds[i], new List<long>());
		}

		HashSet<long> fixedRoleIds = CollectFixedRoleIds(city);
		Queue<XjZongMenMemberSnapshot> queue = new Queue<XjZongMenMemberSnapshot>();
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
				XjZongMenMemberSnapshot member = queue.Dequeue();
				string reason = distributionPeakIds[i] == MainPeakId
					? "AssignMainPeakDisciple"
					: "AssignRegularPeakDisciple";
				XjZongMenMembershipWriter.AssignDisciple(city, distributionPeakIds[i], member.Actor, currentYear, reason);
			}
		}

		for (int i = 0; i < members.Count; i++)
		{
			if (members[i].Actor?.data != null)
			{
				XjZongMenMembershipWriter.ReconcileActorMirror(city, members[i].Actor, currentYear, "BalancedSectRoles");
			}
		}
	}

	private static void AssignMinimumRegularPeakDisciples(
		City city,
		List<int> regularPeakIds,
		Queue<XjZongMenMemberSnapshot> queue,
		int currentYear)
	{
		if (city?.data == null || regularPeakIds == null || queue == null || queue.Count == 0) return;
		Queue<XjZongMenMemberSnapshot> deferred = new Queue<XjZongMenMemberSnapshot>();
		for (int i = 0; i < regularPeakIds.Count && queue.Count > 0; i++)
		{
			int assigned = 0;
			int attempts = queue.Count;
			while (assigned < MinDisciplesPerPeak && queue.Count > 0 && attempts-- > 0)
			{
				XjZongMenMemberSnapshot member = queue.Dequeue();
				if (XjZongMenMembershipWriter.AssignDisciple(
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

	private static void NormalizeJinDanAndZongZhuRoles(City city, List<XjZongMenMemberSnapshot> members, int currentYear)
	{
		long currentZongZhuId = GetZongZhuId(city);
		XjZongMenMemberSnapshot currentZongZhu = default;
		bool currentZongZhuValid = false;
		for (int i = 0; i < members.Count; i++)
		{
			if (members[i].ActorId != currentZongZhuId) continue;
			currentZongZhu = members[i];
			currentZongZhuValid = true;
			break;
		}

		bool needsSuccessor = !currentZongZhuValid || currentZongZhu.RealmLevel >= JinDanRealmLevel;
		Actor promotedFormerZongZhu = currentZongZhuValid && currentZongZhu.RealmLevel >= JinDanRealmLevel
			? currentZongZhu.Actor
			: null;
		if (needsSuccessor)
		{
			SetZongZhuId(city, 0L);
			city.data.set(KeyPeakFengZhuPrefix + MainPeakId, string.Empty);
			Actor successor = FindBestZongZhuSuccessor(members, currentZongZhuId);
			if (successor?.data != null)
			{
				XjZongMenMembershipWriter.AssignZongZhu(city, successor, currentYear, false, "JinDanStepDownSuccession");
			}
		}

		bool hasJinDan = false;
		for (int i = 0; i < members.Count; i++)
		{
			if (members[i].RealmLevel < JinDanRealmLevel || members[i].Actor?.data == null) continue;
			hasJinDan = true;
			if (GetZongZhuId(city) == members[i].ActorId) continue;
			XjZongMenMembershipWriter.AssignSupremeElder(city, members[i].Actor, currentYear, "JinDanEnterDongTian");
		}
		if (hasJinDan) EnsureDongTianPeak(city);

		if (promotedFormerZongZhu?.data != null && GetZongZhuId(city) != GetActorId(promotedFormerZongZhu))
		{
			XjZongMenMembershipWriter.AssignSupremeElder(city, promotedFormerZongZhu, currentYear, "FormerZongZhuEnterDongTian");
		}
	}

	private static Actor FindBestZongZhuSuccessor(List<XjZongMenMemberSnapshot> members, long excludedActorId)
	{
		for (int i = 0; i < members.Count; i++)
		{
			XjZongMenMemberSnapshot member = members[i];
			if (member.ActorId == excludedActorId
				|| member.RealmLevel >= JinDanRealmLevel
				|| member.Actor?.data == null)
			{
				continue;
			}
			return member.Actor;
		}
		return null;
	}

	private static void TrimAndFillRegularPeaks(City city, List<XjZongMenMemberSnapshot> members, int currentYear)
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
				valid = master?.data != null && master.isAlive() && IsCultivator(master) && GetRealmLevel(master) < JinDanRealmLevel;
			}
			if (valid) usedMasters.Add(masterId);
			else city.data.set(KeyPeakFengZhuPrefix + peakId, string.Empty);
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
			XjZongMenMembershipWriter.AssignPeakMaster(city, peakId, candidate, currentYear, "FillVacantPeakMaster");
			usedMasters.Add(GetActorId(candidate));
		}
	}

	private static Actor FindPeakMasterCandidate(City city, List<XjZongMenMemberSnapshot> members, HashSet<long> usedMasters)
	{
		long zongZhuId = GetZongZhuId(city);
		HashSet<long> elders = new HashSet<long>(ReadIdList(city, KeySupremeElders));
		for (int i = 0; i < members.Count; i++)
		{
			XjZongMenMemberSnapshot member = members[i];
			if (member.ActorId == zongZhuId
				|| elders.Contains(member.ActorId)
				|| usedMasters.Contains(member.ActorId)
				|| member.RealmLevel >= JinDanRealmLevel
				|| member.Actor?.data == null)
			{
				continue;
			}
			if (member.RealmLevel >= ZiFuRealmLevel || (member.RealmLevel == 3 && member.IsFoundationLateOrHigher)) return member.Actor;
		}
		return null;
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

	internal static void TryRecruitCityCultivators(City city, int currentYear, Dictionary<City, List<long>> cityIndex = null)
	{
		if (city?.data == null || !HasZongMen(city)) return;
		if (cityIndex == null || !cityIndex.TryGetValue(city, out List<long> candidateIds) || candidateIds == null) return;

		HashSet<long> existing = new HashSet<long>(ReadIdList(city, KeyMemberIds));
		List<long> evaluatedIds = ReadIdList(city, KeyJoinEvaluatedIds);
		HashSet<long> evaluated = new HashSet<long>(evaluatedIds);
		bool evaluatedChanged = false;
		int added = 0;

		for (int i = 0; i < candidateIds.Count; i++)
		{
			long actorId = candidateIds[i];
			if (existing.Contains(actorId) || evaluated.Contains(actorId)) continue;
			Actor actor = ResolveActor(actorId);
			if (actor?.data == null || !actor.isAlive() || actor.city != city || !IsCultivator(actor)) continue;

			evaluated.Add(actorId);
			evaluatedIds.Add(actorId);
			evaluatedChanged = true;
			if (SharedRandom.NextDouble() > 0.5) continue;

			if (XjZongMenMembershipWriter.EnsureMember(city, actor, currentYear, "RecruitCityCultivator")) added++;
			// 支峰与弟子分配由新版宗门档案统一维护。旧城镇峰位只保留
			// 兼容读档，不得把新门人写进无峰主的历史支峰。
			existing.Add(actorId);
		}

		if (evaluatedChanged) WriteIdList(city, KeyJoinEvaluatedIds, evaluatedIds);
		if (added > 0)
		{
			AssignRolesFromScratch(city, currentYear);
			ReconcileArchivePeaksForCity(city, currentYear);
		}
	}

	private static int ResolveLeastPopulatedRegularPeak(City city)
	{
		List<int> peakIds = GetRegularPeakIds(city);
		int bestPeak = -1;
		int minCount = int.MaxValue;
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (!TryReadActorId(city, KeyPeakFengZhuPrefix + peakIds[i], out _)) continue;
			int count = GetPeakDiscipleCount(city, peakIds[i]);
			if (count >= minCount) continue;
			minCount = count;
			bestPeak = peakIds[i];
		}
		return bestPeak;
	}

	#endregion

	#region 晋升处理

	internal static bool TryPromoteZiFuInZongMen(Actor actor, City city, int currentYear)
	{
		if (actor?.data == null || city?.data == null || !HasZongMen(city)) return false;
		XjZongMenMembershipWriter.EnsureMember(city, actor, currentYear, "ZiFuPromotionEnsureMember");
		ReconcileArchivePeaksForCity(city, currentYear);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenZiFuPromotion");
		return true;
	}

	internal static int EnsureFamilyMembersInZongMen(City city, long familyId, int currentYear, string reason)
	{
		if (city?.data == null || familyId <= 0L || !HasZongMen(city))
		{
			return 0;
		}

		int added = 0;
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (member?.data == null || !member.isAlive() || !IsCultivator(member) || XjLongShuSystem.IsLongShu(member))
			{
				continue;
			}

			if (XjZongMenMembershipWriter.EnsureMember(city, member, currentYear, reason))
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
		XjZongMenMembershipWriter.EnsureMember(city, actor, currentYear, "JinDanPromotionEnsureMember");

		// 公告只认“本次晋升前无洞天、晋升后新建洞天”的状态跃迁。
		// 不能直接信任维护函数的中间返回值，否则旧存档迁移、峰位归一化
		// 或重复晋升补偿都可能把已有洞天误判成新建并再次弹出提示。
		bool hadDongTianBeforePromotion = HasDongTianPeak(city);
		EnsureDongTianPeak(city);
		bool createdDongTianThisPromotion = !hadDongTianBeforePromotion && HasDongTianPeak(city);

		ReconcileArchivePeaksForCity(city, currentYear);
		XjCultivationSeed.RefreshChuShenForCultivationState(actor);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenJinDanPromotion");
		XjZongMenDongTianLifecycle.TryStartImmediately(actor, currentYear);
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
			&& !XjLongShuSystem.IsLongShu(actor)
			&& XjCultivatorCache.IsCultivator(GetActorId(actor));
	}

	internal static bool TryFindActorZongMenCity(Actor actor, out City zongMenCity)
	{
		zongMenCity = null;
		if (actor?.data == null) return false;

		XjZongMenIdentitySnapshot state = XjZongMenAccessor.BuildIdentity(actor);
		City currentCity = actor.city;
		if (state.Found)
		{
			if (TryResolveZongMenCity(state.ZongMenId, out City storedCity) && IsMember(storedCity, actor))
			{
				zongMenCity = storedCity;
				return true;
			}
		}

		if (currentCity?.data != null && HasZongMen(currentCity) && IsMember(currentCity, actor))
		{
			XjZongMenMembershipWriter.ReconcileActorMirror(currentCity, actor, GetCurrentYearOrZero(), "LegacyMemberMirrorRestore");
			zongMenCity = currentCity;
			return true;
		}
		return false;
	}

	internal static bool HandleZiFuPromotion(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjLongShuSystem.IsLongShu(actor)) return false;

		// 已有宗门身份优先于角色当前所在城市，防止原版迁城把高境修士
		// 从原宗门抽走。只有尚未入宗的紫府，才按当前城市处理。
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
