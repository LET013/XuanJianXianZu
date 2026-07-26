using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectGovernanceRuntimeLane
{		private static void TrySoftSplitSect(long sectId, int currentYear)
		{
			if (!XjRuntimeSettings.AllowSectRebellionEnabled) return;
			if (currentYear <= 0 || !XjSectRepository.TryGetBySectId(sectId, out XjSectArchiveRecord sect) || sect == null) return;
			if (string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)
				|| string.Equals(sect.Status, XjSectStatus.LandlessSect, StringComparison.Ordinal)
				|| sect.CityIds == null || sect.CityIds.Count < SoftSplitMinimumParentCities)
			{
				return;
			}
	
			City capital = ResolveCity(sect.CapitalCityId);
			if (capital?.data == null) return;
			List<long> remoteCities = SelectRemoteCityCluster(sect, capital);
			if (remoteCities.Count < SoftSplitMountainGateCityCount) return;
			if (!TrySelectSoftSplitFounder(sect, remoteCities, out Actor founder, out long founderFamilyId)) return;
			if (founder?.city?.data == null) return;
			long founderCityId = founder.city.data.id;
			if (!remoteCities.Contains(founderCityId)) remoteCities.Insert(0, founderCityId);
			if (remoteCities.Count > SoftSplitMountainGateCityCount) remoteCities.RemoveRange(SoftSplitMountainGateCityCount, remoteCities.Count - SoftSplitMountainGateCityCount);
			if (remoteCities.Count < SoftSplitMountainGateCityCount) return;
	
			if (!XjSectRepository.TryCreateSoftSplit(sect, founder.city, founder, founderFamilyId, remoteCities, currentYear, out XjSectArchiveRecord created)
				|| created == null)
			{
				return;
			}
	
			MigrateSoftSplitMembers(created, remoteCities, currentYear);
		}

		private static List<long> SelectRemoteCityCluster(XjSectArchiveRecord sect, City capital)
		{
			List<long> selected = new List<long>();
			if (sect?.CityIds == null || capital?.data == null) return selected;
			long capitalId = capital.data.id;
			Dictionary<long, float> distances = new Dictionary<long, float>();
			bool hasCapitalPosition = TryExtractMapPosition(capital, 0, out Vector3 capitalPosition);
			for (int i = 0; i < sect.CityIds.Count; i++)
			{
				long cityId = sect.CityIds[i];
				if (cityId <= 0L || cityId == capitalId) continue;
				City city = ResolveCity(cityId);
				if (city?.data == null) continue;
				float distance = Math.Abs(cityId - capitalId);
				if (hasCapitalPosition && TryExtractMapPosition(city, 0, out Vector3 position))
				{
					distance = (position - capitalPosition).sqrMagnitude;
				}
				if (distance < SoftSplitDistanceThresholdSquared && hasCapitalPosition) continue;
				distances[cityId] = distance;
			}
			List<long> candidates = new List<long>(distances.Keys);
			candidates.Sort((left, right) =>
			{
				int cmp = distances[right].CompareTo(distances[left]);
				return cmp != 0 ? cmp : left.CompareTo(right);
			});
			int take = SoftSplitMountainGateCityCount;
			for (int i = 0; i < candidates.Count && selected.Count < take; i++) selected.Add(candidates[i]);
			return selected;
		}

		private static bool TrySelectSoftSplitFounder(XjSectArchiveRecord sect, IReadOnlyList<long> remoteCities, out Actor founder, out long familyId)
		{
			founder = null;
			familyId = 0L;
			if (sect == null || remoteCities == null || remoteCities.Count == 0) return false;
			HashSet<long> citySet = new HashSet<long>(remoteCities);
			int bestOrder = -1;
			float bestVoice = -1f;
			long bestActorId = long.MaxValue;
			Dictionary<long, float> voiceByFamily = BuildVoiceByFamily(sect.SectId);
			Dictionary<City, List<long>> index = XjZongMenCultivatorCityIndex.GetCityIndex();
			foreach (KeyValuePair<City, List<long>> pair in index)
			{
				if (pair.Key?.data == null || pair.Value == null || !citySet.Contains(pair.Key.data.id)) continue;
				for (int i = 0; i < pair.Value.Count; i++)
				{
					long actorId = pair.Value[i];
					if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()
						|| ReadActorSectId(actor) != sect.SectId || !IsAtLeastZiFu(actor)) continue;
					string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
					int order = XjRealmHelper.GetOrder(realmId);
					XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long candidateFamilyId);
					float voice = candidateFamilyId > 0L && voiceByFamily.TryGetValue(candidateFamilyId, out float value) ? value : 0f;
					if (founder == null || order > bestOrder
						|| (order == bestOrder && voice > bestVoice)
						|| (order == bestOrder && Math.Abs(voice - bestVoice) < 0.001f && actorId < bestActorId))
					{
						founder = actor;
						familyId = candidateFamilyId;
						bestOrder = order;
						bestVoice = voice;
						bestActorId = actorId;
					}
				}
			}
			return founder?.data != null && familyId > 0L;
		}

		private static void MigrateSoftSplitMembers(XjSectArchiveRecord sect, IReadOnlyList<long> cityIds, int currentYear)
		{
			if (sect == null || cityIds == null || !XjZongMenCityData.TryResolveZongMenCity(sect.SectId, out City zongMenCity)
				|| zongMenCity?.data == null)
			{
				return;
			}
			HashSet<long> citySet = new HashSet<long>(cityIds);
			Dictionary<City, List<long>> index = XjZongMenCultivatorCityIndex.GetCityIndex();
			foreach (KeyValuePair<City, List<long>> pair in index)
			{
				if (pair.Key?.data == null || pair.Value == null || !citySet.Contains(pair.Key.data.id)) continue;
				for (int i = 0; i < pair.Value.Count; i++)
				{
					if (!XjScheduler.ResolveActor(pair.Value[i], out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
					XjZongMenMembershipWriter.EnsureMember(zongMenCity, actor, currentYear, "SoftSplitMember");
				}
			}
			// 峰主与弟子归属由 XjSectRepository.Peaks 在宗门治理队列统一处理。
			// 这里仅迁入成员，禁止旧城镇层重新创建另一套峰位。
		}
}

