using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.XianGuo;

/// <summary>
/// 仙国法自己的朝廷权威层。
///
/// 借鉴“春秋/朝廷类模组”的关键不是复制官职文本，而是复制架构边界：
/// - 官位有独立定义；
/// - 任官记录属于王朝存档，是权威状态；
/// - WorldBox Kingdom/City 只提供疆土、人口与地理事实，不再把原生城主当“仙朝百官”；
/// - 人物身上的持玄字段只是官位投影，可随任免无损撤销。
///
/// 这样帝明阳不会再因为原生城主换人、忠诚脚本或叛乱AI抖动而让整套持玄体系失真。
/// </summary>
internal static class XjXianGuoCourtSystem
{
	private const int AppointmentIntervalYears = 5;
	// 仙国法可以把官员托举到高境，但高境玄位属于“国朝名额”而不是每官一份。
	// 紫府及仙国假金丹共用同一组高玄名额；假金丹额外至多一席。
	private const int BorrowedHighRealmBasePotential = 5200;
	private const int BorrowedHighRealmBaseCities = 3;
	private const int BorrowedHighRealmBasePopulation = 600;
	private const int BorrowedHighRealmSecondPotential = 6800;
	private const int BorrowedHighRealmThirdPotential = 8200;
	private const int BorrowedHighRealmSecondCities = 5;
	private const int BorrowedHighRealmThirdCities = 9;
	private const int BorrowedHighRealmSecondPopulation = 1200;
	private const int BorrowedHighRealmThirdPopulation = 2500;

	private sealed class OfficeDefinition
	{
		internal readonly string Id;
		internal readonly string Name;
		internal readonly int Rank;
		internal readonly int BorrowSteps;
		internal readonly bool Local;
		internal readonly bool CanBorrowFakeJinDan;

		internal OfficeDefinition(string id, string name, int rank, int borrowSteps, bool local, bool canBorrowFakeJinDan)
		{
			Id = id;
			Name = name;
			Rank = rank;
			BorrowSteps = borrowSteps;
			Local = local;
			CanBorrowFakeJinDan = canBorrowFakeJinDan;
		}
	}

	private readonly struct Candidate
	{
		internal readonly Actor Actor;
		internal readonly long ActorId;
		internal readonly long CityId;
		internal readonly long Score;

		internal Candidate(Actor actor, long actorId, long cityId, long score)
		{
			Actor = actor;
			ActorId = actorId;
			CityId = cityId;
			Score = score;
		}
	}

	private static readonly OfficeDefinition[] CentralDefinitions =
	{
		new OfficeDefinition("taizai", "太宰", 1, 2, false, true),
		new OfficeDefinition("sixuan", "司玄", 1, 2, false, true),
		new OfficeDefinition("sibing", "司兵", 2, 1, false, true),
		new OfficeDefinition("silv", "司律", 2, 1, false, true),
		new OfficeDefinition("simin", "司民", 3, 1, false, false),
		new OfficeDefinition("situ", "司土", 3, 1, false, false)
	};

	private static readonly OfficeDefinition LocalDefinition =
		new OfficeDefinition("chixuanshi", "持玄使", 4, 1, true, false);

	private static readonly List<XjXianGuoCourtOfficeRecord> Records = new List<XjXianGuoCourtOfficeRecord>();
	private static readonly Dictionary<string, XjXianGuoCourtOfficeRecord> ByKey =
		new Dictionary<string, XjXianGuoCourtOfficeRecord>(StringComparer.Ordinal);
	private static readonly Dictionary<long, XjXianGuoCourtOfficeRecord> ActiveByActorId =
		new Dictionary<long, XjXianGuoCourtOfficeRecord>();
	private static readonly List<Candidate> CandidateScratch = new List<Candidate>(128);
	private static readonly HashSet<long> UsedActorIds = new HashSet<long>();
	private static readonly HashSet<long> ActiveCityIds = new HashSet<long>();

	internal static bool IsActiveOfficer(long actorId, long dynastyId)
	{
		return actorId > 0L && dynastyId > 0L
			&& ActiveByActorId.TryGetValue(actorId, out XjXianGuoCourtOfficeRecord record)
			&& record != null && record.Active && record.DynastyId == dynastyId;
	}

	internal static bool TryGetOfficer(long actorId, out XjXianGuoCourtOfficeRecord record)
	{
		record = null;
		if (actorId <= 0L || !ActiveByActorId.TryGetValue(actorId, out XjXianGuoCourtOfficeRecord found)
			|| found == null || !found.Active) return false;
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			&& actor?.data != null && XjXianGuoSystem.IsDiMingYang(actor)) return false;
		record = found;
		return true;
	}

	internal static IReadOnlyList<XjXianGuoCourtOfficeRecord> ReadActiveOffices(long dynastyId)
	{
		if (dynastyId <= 0L) return Array.Empty<XjXianGuoCourtOfficeRecord>();
		List<XjXianGuoCourtOfficeRecord> result = new List<XjXianGuoCourtOfficeRecord>();
		for (int i = 0; i < Records.Count; i++)
		{
			XjXianGuoCourtOfficeRecord record = Records[i];
			if (record == null || !record.Active || record.DynastyId != dynastyId) continue;
			if (record.ActorId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor actor)
				&& actor?.data != null && XjXianGuoSystem.IsDiMingYang(actor))
			{
				// 旧档若把帝君塞进官位，读侧立即按“虚席”展示；年度 Reconcile
				// 会正式撤销错误占位并重新补官。
				XjXianGuoCourtOfficeRecord vacancy = record.Clone();
				vacancy.ActorId = 0L;
				vacancy.ActorName = string.Empty;
				result.Add(vacancy);
				continue;
			}
			result.Add(record);
		}
		result.Sort((left, right) =>
		{
			int rankCompare = left.Rank.CompareTo(right.Rank);
			if (rankCompare != 0) return rankCompare;
			int localCompare = left.CityId.CompareTo(right.CityId);
			if (localCompare != 0) return localCompare;
			return string.CompareOrdinal(left.OfficeId, right.OfficeId);
		});
		return result;
	}

	internal static void Reconcile(XjXianGuoRecord dynasty, Actor sovereign, int annualYear)
	{
		if (dynasty == null || !dynasty.Active || dynasty.DynastyId <= 0L || dynasty.KingdomId <= 0L
			|| sovereign?.data == null || !sovereign.isAlive()) return;
		int year = Math.Max(1, annualYear);

		EnsureOfficeRows(dynasty, year);
		ValidateIncumbents(dynasty, sovereign, year);

		bool hasVacancy = HasVacancy(dynasty.DynastyId);
		if (hasVacancy && (dynasty.LastCourtAppointmentYear <= 0
			|| year - dynasty.LastCourtAppointmentYear >= AppointmentIntervalYears))
		{
			FillVacancies(dynasty, sovereign, year);
			dynasty.LastCourtAppointmentYear = year;
			XjWorldArchiveSystem.MarkChanged();
		}

		// 官位是权威，【国之重臣】/持玄使只是身份；真正捷径来自“承国之命”。
		// 一品→四品依次分配基础国命；紫府/假金丹还必须占用王朝级高玄承命席，
		// 因此不会因为官位数量增长就批量生成高境。
		int sovereignTier = XjXianGuoSystem.ResolveImperialSovereignTier(sovereign);
		int borrowedZiFuLimit = ResolveBorrowedZiFuLimitForSovereign(
			dynasty.CityCount, dynasty.Population, dynasty.NationalPotential, dynasty.NationalFortune, sovereignTier);
		int borrowedFakeJinDanLimit = dynasty.CourtFakeJinDanActive
			? ResolveBorrowedFakeJinDanLimitForSovereign(
				dynasty.CityCount, dynasty.Population, dynasty.NationalPotential, dynasty.NationalFortune, sovereignTier)
			: 0;
		// 百官持玄分账：紫府最多九席，假金丹最多三席；帝明阳本人不占群臣名额。
		int borrowedZiFuUsed = 0;
		int borrowedFakeJinDanUsed = 0;
		for (int rank = 1; rank <= 4; rank++)
		{
			for (int i = 0; i < Records.Count; i++)
			{
				XjXianGuoCourtOfficeRecord office = Records[i];
				if (office == null || !office.Active || office.DynastyId != dynasty.DynastyId
					|| office.ActorId <= 0L || office.Rank != rank) continue;
				if (!XjActorRegistry.ResolveKnownOrWorld(office.ActorId, out Actor officer)
					|| !IsEligibleOfficer(officer, dynasty, sovereign))
				{
					Vacate(office, year, clearProjection: true);
					continue;
				}

				OfficeDefinition definition = ResolveDefinition(office);
				if (definition == null) continue;
				int realTier = XjRealmSuppression.GetRealmTier(officer);
				int trueFate = XjXianGuoSystem.ResolveTrueMingShu(officer);
				int nationalFate = ResolveBaseNationalFate(dynasty, definition);

				bool canUseHighSeat = borrowedZiFuUsed < borrowedZiFuLimit
					&& sovereignTier >= XjRealmSuppression.TierZiFu
					&& realTier < XjRealmSuppression.TierZiFu
					&& realTier + Math.Max(0, definition.BorrowSteps) >= XjRealmSuppression.TierZiFu;
				bool canUseFakeSeat = !definition.Local
					&& borrowedFakeJinDanUsed < borrowedFakeJinDanLimit
					&& dynasty.CourtFakeJinDanActive
					&& sovereignTier >= XjRealmSuppression.TierJinDan
					&& definition.CanBorrowFakeJinDan
					&& realTier < XjRealmSuppression.TierJinDan
					&& realTier + Math.Max(0, definition.BorrowSteps) >= XjRealmSuppression.TierJinDan;

				if (canUseFakeSeat)
				{
					nationalFate = Math.Max(nationalFate, ResolveNationalFateNeededForTier(
						trueFate, XjRealmSuppression.TierJinDan, dynasty, definition));
				}
				else if (canUseHighSeat)
				{
					nationalFate = Math.Max(nationalFate, ResolveNationalFateNeededForTier(
						trueFate, XjRealmSuppression.TierZiFu, dynasty, definition));
				}

				long combinedFate = (long)Math.Max(0, trueFate) + Math.Max(0, nationalFate);
				int effectiveFate = combinedFate >= int.MaxValue ? int.MaxValue : (int)combinedFate;
				int fateTier = XjXianGuoSystem.ResolveInstitutionalTierFromEffectiveFate(effectiveFate);
				int targetTier = ResolveProjectionTier(
					officer, definition, fateTier, sovereignTier, canUseHighSeat || canUseFakeSeat, canUseFakeSeat);

				if (targetTier > realTier)
				{
					if (targetTier >= XjRealmSuppression.TierJinDan) borrowedFakeJinDanUsed++;
					else if (targetTier >= XjRealmSuppression.TierZiFu) borrowedZiFuUsed++;
				}

				XjXianGuoSystem.ApplyCourtInstitutionalProjection(
					officer, dynasty, nationalFate, targetTier, year, definition.Name);
				XjXianGuoSystem.SyncCourtIdentityTrait(officer);
				office.LastValidatedYear = year;
			}
		}

	}

	internal static void EndDynasty(long dynastyId, int annualYear)
	{
		if (dynastyId <= 0L) return;
		int year = Math.Max(1, annualYear);
		bool changed = false;
		for (int i = 0; i < Records.Count; i++)
		{
			XjXianGuoCourtOfficeRecord record = Records[i];
			if (record == null || !record.Active || record.DynastyId != dynastyId) continue;
			long previousActorId = record.ActorId;
			// 先撤掉官位权威，再清人物承命投影；否则人物仍会暂时保留旧朝国命与借境。
			if (previousActorId > 0L) ActiveByActorId.Remove(previousActorId);
			if (previousActorId > 0L
				&& XjActorRegistry.ResolveKnownOrWorld(previousActorId, out Actor actor)
				&& actor?.data != null)
			{
				XjXianGuoSystem.ClearCourtInstitutionalProjection(actor);
			}
			record.ActorId = 0L;
			record.ActorName = string.Empty;
			record.Active = false;
			record.EndedYear = year;
			changed = true;
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
	}

	internal static List<XjXianGuoCourtOfficeRecord> Export()
	{
		List<XjXianGuoCourtOfficeRecord> result = new List<XjXianGuoCourtOfficeRecord>(Records.Count);
		for (int i = 0; i < Records.Count; i++)
		{
			if (Records[i] != null) result.Add(Records[i].Clone());
		}
		return result;
	}

	internal static void Import(IEnumerable<XjXianGuoCourtOfficeRecord> records)
	{
		Clear();
		if (records == null) return;
		foreach (XjXianGuoCourtOfficeRecord source in records)
		{
			if (source == null || source.DynastyId <= 0L || string.IsNullOrWhiteSpace(source.OfficeKey)) continue;
			XjXianGuoCourtOfficeRecord record = source.Clone();
			Records.Add(record);
			ByKey[record.OfficeKey] = record;
			if (record.Active && record.ActorId > 0L)
			{
				// 同一人物旧档若意外占两官，只保留品秩更高的一条为当前权威。
				if (!ActiveByActorId.TryGetValue(record.ActorId, out XjXianGuoCourtOfficeRecord existing)
					|| existing == null || record.Rank < existing.Rank)
				{
					if (existing != null)
					{
						existing.ActorId = 0L;
						existing.ActorName = string.Empty;
					}
					ActiveByActorId[record.ActorId] = record;
				}
				else
				{
					record.ActorId = 0L;
					record.ActorName = string.Empty;
				}
			}
		}
	}

	internal static void Clear()
	{
		Records.Clear();
		ByKey.Clear();
		ActiveByActorId.Clear();
		CandidateScratch.Clear();
		UsedActorIds.Clear();
		ActiveCityIds.Clear();
	}

	private static void EnsureOfficeRows(XjXianGuoRecord dynasty, int year)
	{
		bool changed = false;
		for (int i = 0; i < CentralDefinitions.Length; i++)
		{
			OfficeDefinition definition = CentralDefinitions[i];
			string key = BuildOfficeKey(dynasty.DynastyId, definition.Id, 0L);
			if (ByKey.ContainsKey(key)) continue;
			XjXianGuoCourtOfficeRecord record = CreateRecord(dynasty, definition, key, 0L, string.Empty, year);
			Records.Add(record);
			ByKey[key] = record;
			changed = true;
		}

		ActiveCityIds.Clear();
		IReadOnlyList<City> cities = XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < cities.Count; i++)
		{
			City city = cities[i];
			if (city?.data == null || city.kingdom?.data?.id != dynasty.KingdomId) continue;
			long cityId = city.data.id;
			if (cityId <= 0L) continue;
			ActiveCityIds.Add(cityId);
			string key = BuildOfficeKey(dynasty.DynastyId, LocalDefinition.Id, cityId);
			string cityName = string.IsNullOrWhiteSpace(city.data.name) ? "未名城" : city.data.name.Trim();
			if (ByKey.TryGetValue(key, out XjXianGuoCourtOfficeRecord existing))
			{
				existing.CityName = cityName;
				if (!existing.Active)
				{
					existing.Active = true;
					existing.EndedYear = 0;
					existing.LastVacatedYear = year;
					changed = true;
				}
				continue;
			}
			XjXianGuoCourtOfficeRecord record = CreateRecord(
				dynasty, LocalDefinition, key, cityId, cityName, year);
			Records.Add(record);
			ByKey[key] = record;
			changed = true;
		}

		// 城土易手后，本朝地方官位退役；原人物不再因“以前是某城城主”长期持玄。
		for (int i = 0; i < Records.Count; i++)
		{
			XjXianGuoCourtOfficeRecord record = Records[i];
			if (record == null || !record.Active || record.DynastyId != dynasty.DynastyId
				|| !record.IsLocal || record.CityId <= 0L || ActiveCityIds.Contains(record.CityId)) continue;
			long previousActorId = record.ActorId;
			// 城土易手也是一次正式撤官：先撤官位权威，再清角色投影和战斗缓存。
			if (previousActorId > 0L) ActiveByActorId.Remove(previousActorId);
			if (previousActorId > 0L && XjActorRegistry.ResolveKnownOrWorld(previousActorId, out Actor officer)
				&& officer?.data != null)
			{
				XjXianGuoSystem.ClearCourtInstitutionalProjection(officer);
			}
			record.ActorId = 0L;
			record.ActorName = string.Empty;
			record.Active = false;
			record.EndedYear = year;
			changed = true;
		}
		if (changed) XjWorldArchiveSystem.MarkChanged();
	}

	private static XjXianGuoCourtOfficeRecord CreateRecord(
		XjXianGuoRecord dynasty,
		OfficeDefinition definition,
		string key,
		long cityId,
		string cityName,
		int year)
	{
		return new XjXianGuoCourtOfficeRecord
		{
			DynastyId = dynasty.DynastyId,
			KingdomId = dynasty.KingdomId,
			OfficeKey = key,
			OfficeId = definition.Id,
			OfficeName = definition.Name,
			Rank = definition.Rank,
			IsLocal = definition.Local,
			CityId = cityId,
			CityName = cityName ?? string.Empty,
			CreatedYear = year,
			Active = true
		};
	}

	private static void ValidateIncumbents(XjXianGuoRecord dynasty, Actor sovereign, int year)
	{
		for (int i = 0; i < Records.Count; i++)
		{
			XjXianGuoCourtOfficeRecord record = Records[i];
			if (record == null || !record.Active || record.DynastyId != dynasty.DynastyId || record.ActorId <= 0L) continue;
			if (!XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor actor)
				|| !IsEligibleOfficer(actor, dynasty, sovereign))
			{
				Vacate(record, year, clearProjection: actor?.data != null);
				continue;
			}
			record.ActorName = XjStringHelper.ActorName(actor, string.Empty);
			record.LastValidatedYear = year;
		}
	}

	private static bool HasVacancy(long dynastyId)
	{
		for (int i = 0; i < Records.Count; i++)
		{
			XjXianGuoCourtOfficeRecord record = Records[i];
			if (record != null && record.Active && record.DynastyId == dynastyId && record.ActorId <= 0L) return true;
		}
		return false;
	}

	private static void FillVacancies(XjXianGuoRecord dynasty, Actor sovereign, int year)
	{
		BuildCandidatePool(dynasty, sovereign, year);
		if (CandidateScratch.Count == 0) return;

		UsedActorIds.Clear();
		foreach (KeyValuePair<long, XjXianGuoCourtOfficeRecord> pair in ActiveByActorId)
		{
			if (pair.Value != null && pair.Value.Active) UsedActorIds.Add(pair.Key);
		}

		// 先中央后地方，且中央按一品→三品。官员可来自全国修士，不要求恰好是原生城主。
		for (int rank = 1; rank <= 4; rank++)
		{
			for (int i = 0; i < Records.Count; i++)
			{
				XjXianGuoCourtOfficeRecord office = Records[i];
				if (office == null || !office.Active || office.DynastyId != dynasty.DynastyId
					|| office.ActorId > 0L || office.Rank != rank) continue;
				if (!TrySelectCandidate(office, out Candidate selected)) continue;
				Appoint(office, selected, year);
			}
		}
	}

	private static void BuildCandidatePool(XjXianGuoRecord dynasty, Actor sovereign, int year)
	{
		CandidateScratch.Clear();
		long sovereignId = sovereign?.data == null ? 0L : ((BaseSystemData)sovereign.data).id;
		IReadOnlyList<long> ids = XjCultivatorCache.GetAllIds();
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (actorId <= 0L || actorId == sovereignId
				|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| !IsEligibleOfficer(actor, dynasty, sovereign)) continue;

			int tier = XjRealmSuppression.GetRealmTier(actor);
			if (tier < XjRealmSuppression.TierLianQi) continue;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
			bool mingYang = XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
				&& string.Equals((daoTu ?? string.Empty).Trim(), XjXianGuoSystem.MingYangDaoTu, StringComparison.Ordinal);
			long cityId = actor.city?.data?.id ?? 0L;
			long score = (long)tier * 100000L
				+ (long)Math.Floor(Math.Max(0f, mingShu) * 80f)
				+ (mingYang ? 25000L : 0L)
				+ XjDeterministicHash.PositiveIndex(actorId + year * 97L, "xianguo.court.tie", 997);
			CandidateScratch.Add(new Candidate(actor, actorId, cityId, score));
		}
		CandidateScratch.Sort((left, right) =>
		{
			int scoreCompare = right.Score.CompareTo(left.Score);
			return scoreCompare != 0 ? scoreCompare : left.ActorId.CompareTo(right.ActorId);
		});
	}

	private static bool TrySelectCandidate(XjXianGuoCourtOfficeRecord office, out Candidate selected)
	{
		selected = default;
		bool found = false;
		long bestScore = long.MinValue;
		for (int i = 0; i < CandidateScratch.Count; i++)
		{
			Candidate candidate = CandidateScratch[i];
			if (UsedActorIds.Contains(candidate.ActorId)) continue;
			long score = candidate.Score;
			if (office.IsLocal && office.CityId > 0L && candidate.CityId == office.CityId) score += 50000L;
			if (!found || score > bestScore)
			{
				selected = candidate;
				bestScore = score;
				found = true;
			}
		}
		return found;
	}

	private static void Appoint(XjXianGuoCourtOfficeRecord office, in Candidate selected, int year)
	{
		office.ActorId = selected.ActorId;
		office.ActorName = XjStringHelper.ActorName(selected.Actor, string.Empty);
		office.AppointedYear = year;
		office.LastValidatedYear = year;
		ActiveByActorId[selected.ActorId] = office;
		UsedActorIds.Add(selected.ActorId);
		XjWorldArchiveSystem.MarkChanged();
	}

	private static void Vacate(XjXianGuoCourtOfficeRecord office, int year, bool clearProjection)
	{
		if (office == null) return;
		long previousActorId = office.ActorId;
		// 官位是权威，投影和战斗缓存只是派生。撤官必须先撤权威映射，
		// 再清人物投影，保证缓存刷新不会把已经失去的玄秩重新算进去。
		if (previousActorId > 0L) ActiveByActorId.Remove(previousActorId);
		if (clearProjection && previousActorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(previousActorId, out Actor actor)
			&& actor?.data != null)
		{
			XjXianGuoSystem.ClearCourtInstitutionalProjection(actor);
		}
		office.ActorId = 0L;
		office.ActorName = string.Empty;
		office.LastVacatedYear = year;
		XjWorldArchiveSystem.MarkChanged();
	}

	private static bool IsEligibleOfficer(Actor actor, XjXianGuoRecord dynasty, Actor sovereign)
	{
		if (actor?.data == null || !actor.isAlive() || dynasty == null || !dynasty.Active) return false;
		if (sovereign?.data != null
			&& ((BaseSystemData)actor.data).id == ((BaseSystemData)sovereign.data).id) return false;
		if (actor.kingdom?.data?.id != dynasty.KingdomId) return false;
		// 仙朝百官只从两条玄鉴仙修主体系中取士；释修/龙属/阴司不被王朝官位强行改道。
		return XjCultivationPathRules.IsZiFuJinDan(actor) || XjCultivationPathRules.IsFuQiYangXing(actor);
	}

	private static OfficeDefinition ResolveDefinition(XjXianGuoCourtOfficeRecord record)
	{
		if (record == null) return null;
		if (record.IsLocal) return LocalDefinition;
		for (int i = 0; i < CentralDefinitions.Length; i++)
		{
			if (string.Equals(CentralDefinitions[i].Id, record.OfficeId, StringComparison.Ordinal))
				return CentralDefinitions[i];
		}
		return null;
	}

	/// <summary>
	/// 一朝由仙国法“额外托举”出的紫府/假金丹总名额。名额只随真实国势、
	/// 城土与臣民跨过大台阶增长，最多三席，避免官位数量直接等价于高境数量。
	/// </summary>
	internal static int ResolveBorrowedHighRealmLimit(int cityCount, int population, int nationalPotential, int nationalFortune)
	{
		int effective = Math.Min(Math.Max(0, nationalPotential), Math.Max(0, nationalFortune));
		if (effective < BorrowedHighRealmBasePotential
			|| cityCount < BorrowedHighRealmBaseCities
			|| population < BorrowedHighRealmBasePopulation) return 0;
		int limit = 1;
		if (effective >= BorrowedHighRealmSecondPotential
			&& cityCount >= BorrowedHighRealmSecondCities
			&& population >= BorrowedHighRealmSecondPopulation) limit = 2;
		if (effective >= BorrowedHighRealmThirdPotential
			&& cityCount >= BorrowedHighRealmThirdCities
			&& population >= BorrowedHighRealmThirdPopulation) limit = 3;
		return limit;
	}

	internal static int ResolveBorrowedZiFuLimitForSovereign(
		int cityCount, int population, int nationalPotential, int nationalFortune, int sovereignTier)
	{
		if (sovereignTier < XjRealmSuppression.TierZiFu) return 0;
		int nationalStage = ResolveBorrowedHighRealmLimit(cityCount, population, nationalPotential, nationalFortune);
		return Math.Clamp(nationalStage * 3, 0, 9);
	}

	internal static int ResolveBorrowedFakeJinDanLimitForSovereign(
		int cityCount, int population, int nationalPotential, int nationalFortune, int sovereignTier)
	{
		if (sovereignTier < XjRealmSuppression.TierJinDan) return 0;
		int nationalStage = ResolveBorrowedHighRealmLimit(cityCount, population, nationalPotential, nationalFortune);
		return Math.Clamp(nationalStage, 0, 3);
	}

	internal static int ResolveBorrowedHighRealmLimitForSovereign(
		int cityCount, int population, int nationalPotential, int nationalFortune, int sovereignTier)
	{
		return ResolveBorrowedZiFuLimitForSovereign(
			cityCount, population, nationalPotential, nationalFortune, sovereignTier);
	}

	internal static bool IsHeavyMinisterOffice(XjXianGuoCourtOfficeRecord office)
	{
		return office != null && office.Active && !office.IsLocal && office.Rank >= 1 && office.Rank <= 3;
	}

	private static int ResolveBaseNationalFate(XjXianGuoRecord dynasty, OfficeDefinition definition)
	{
		if (dynasty == null || definition == null) return 0;
		int effectiveNational = Math.Clamp(Math.Min(dynasty.NationalPotential, dynasty.NationalFortune), 0, 10000);
		// 地方持玄使只承一城之命，中枢重臣方能大幅摄取帝统国命。
		int grant = definition.Rank switch
		{
			1 => 1600 + (int)Math.Floor(effectiveNational * 0.65f),
			2 => 1300 + (int)Math.Floor(effectiveNational * 0.48f),
			3 => 1000 + (int)Math.Floor(effectiveNational * 0.32f),
			4 => 550 + (int)Math.Floor(effectiveNational * 0.18f),
			_ => 0
		};
		return Math.Max(0, grant);
	}

	private static int ResolveNationalFateNeededForTier(
		int trueFate,
		int targetTier,
		XjXianGuoRecord dynasty,
		OfficeDefinition definition)
	{
		int threshold = XjXianGuoSystem.ResolveFateThresholdForTier(targetTier);
		if (threshold <= 0) return 0;
		int effectiveNational = dynasty == null ? 0 : Math.Clamp(Math.Min(dynasty.NationalPotential, dynasty.NationalFortune), 0, 10000);
		int margin = targetTier >= XjRealmSuppression.TierJinDan
			? 2500 + effectiveNational / 4
			: 500 + effectiveNational / 20 + Math.Max(0, 3 - (definition?.Rank ?? 3)) * 120;
		long required = (long)threshold + Math.Max(0, margin) - Math.Max(0, trueFate);
		if (required <= 0L) return 0;
		return required >= int.MaxValue ? int.MaxValue : (int)required;
	}

	private static int ResolveProjectionTier(
		Actor actor,
		OfficeDefinition definition,
		int fateTier,
		int sovereignTier,
		bool allowHighSeat,
		bool allowFakeSeat)
	{
		if (actor?.data == null || definition == null) return XjRealmSuppression.TierNone;
		int realTier = XjRealmSuppression.GetRealmTier(actor);
		if (realTier < XjRealmSuppression.TierLianQi) return XjRealmSuppression.TierNone;

		int ceiling = Math.Clamp(fateTier, XjRealmSuppression.TierLianQi, XjRealmSuppression.TierJinDan);
		// 官员持玄不能高过帝明阳真实大境界。
		ceiling = Math.Min(ceiling, Math.Max(XjRealmSuppression.TierLianQi, sovereignTier));
		// 没有王朝高玄承命席时，命数即便很高也不能借官位越过紫府天堑；
		// 这只限制“借来的高境”，不妨碍人物凭自己的真命正常修行突破。
		if (!allowHighSeat && ceiling > XjRealmSuppression.TierZhuJi) ceiling = XjRealmSuppression.TierZhuJi;
		if (!allowFakeSeat && ceiling > XjRealmSuppression.TierZiFu) ceiling = XjRealmSuppression.TierZiFu;
		if (definition.Local && ceiling > XjRealmSuppression.TierZhuJi) ceiling = XjRealmSuppression.TierZhuJi;
		if (definition.Rank >= 3 && ceiling > XjRealmSuppression.TierZiFu) ceiling = XjRealmSuppression.TierZiFu;

		int desired = Math.Min(ceiling, realTier + Math.Max(0, definition.BorrowSteps));
		return desired > realTier ? desired : XjRealmSuppression.TierNone;
	}

	private static string BuildOfficeKey(long dynastyId, string officeId, long cityId)
	{
		return dynastyId.ToString() + "|" + officeId + "|" + cityId.ToString();
	}

	internal static string GetRankDisplay(int rank)
	{
		return rank switch
		{
			1 => "玄秩一品",
			2 => "玄秩二品",
			3 => "玄秩三品",
			4 => "玄秩四品",
			_ => "玄秩"
		};
	}
}

internal sealed class XjXianGuoCourtOfficeRecord
{
	public long DynastyId { get; set; }
	public long KingdomId { get; set; }
	public string OfficeKey { get; set; } = string.Empty;
	public string OfficeId { get; set; } = string.Empty;
	public string OfficeName { get; set; } = string.Empty;
	public int Rank { get; set; }
	public bool IsLocal { get; set; }
	public long CityId { get; set; }
	public string CityName { get; set; } = string.Empty;
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public int CreatedYear { get; set; }
	public int AppointedYear { get; set; }
	public int LastValidatedYear { get; set; }
	public int LastVacatedYear { get; set; }
	public int EndedYear { get; set; }
	public bool Active { get; set; }

	internal XjXianGuoCourtOfficeRecord Clone()
	{
		return (XjXianGuoCourtOfficeRecord)MemberwiseClone();
	}
}
