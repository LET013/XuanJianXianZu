using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修人物关系的年度派生索引。承载地本体由XjShiDomainState保存，本类只派生
/// 摩诃—怜愍关系、已用位次和候选座主，不再以角色ShiJinDiId建立世界事实。
/// </summary>
internal static class XjShiWorldRegistry
{
	private readonly struct MoHeNode
	{
		internal readonly long ActorId;
		internal readonly string Tradition;
		internal readonly string LineageId;
		internal readonly string DomainId;
		internal readonly int Capacity;
		internal readonly int UsedSeats;
		internal readonly int CompletedLives;

		internal MoHeNode(long actorId, string tradition, string lineageId, string domainId,
			int capacity, int usedSeats, int completedLives)
		{
			ActorId = actorId;
			Tradition = tradition ?? string.Empty;
			LineageId = lineageId ?? string.Empty;
			DomainId = domainId ?? string.Empty;
			Capacity = Math.Max(0, capacity);
			UsedSeats = Math.Max(0, usedSeats);
			CompletedLives = Math.Max(0, completedLives);
		}
	}

	private static readonly Dictionary<long, int> SeatCountByPatron = new Dictionary<long, int>();
	private static readonly Dictionary<long, List<long>> DependentsByPatron = new Dictionary<long, List<long>>();
	private static readonly Dictionary<long, long> PatronByDependent = new Dictionary<long, long>();
	private static readonly List<MoHeNode> MoHeNodes = new List<MoHeNode>();
	private static readonly List<long> LiveHighRealmActorIds = new List<long>();
	private static readonly Dictionary<string, int> LiveRealmCounts =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> LiveLineageRealmCounts =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> LiveTraditionCounts =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, int> LiveTraditionRealmCounts =
		new Dictionary<string, int>(StringComparer.Ordinal);
	private static int _builtYear;
	private static bool _dirty = true;

	internal static void EnsureYear(int annualYear)
	{
		int year = Math.Max(1, annualYear);
		// 手动赋予境界、法脉或金地后会把注册表标脏；同一年打开仙录时必须
		// 立即强制对账一次，不能继续沿用年度初的旧快照。
		XjShiDomainState.ReconcileFromActors(year, force: _dirty);
		if (!_dirty && _builtYear == year) return;
		Rebuild(year);
	}

	internal static void Invalidate()
	{
		_dirty = true;
		XjShiDomainState.Invalidate();
	}

	internal static void Clear()
	{
		_builtYear = 0;
		_dirty = true;
		SeatCountByPatron.Clear();
		DependentsByPatron.Clear();
		PatronByDependent.Clear();
		MoHeNodes.Clear();
		LiveHighRealmActorIds.Clear();
		LiveRealmCounts.Clear();
		LiveLineageRealmCounts.Clear();
		LiveTraditionCounts.Clear();
		LiveTraditionRealmCounts.Clear();
	}

	internal static int GetLiveRealmCount(string realmId, int annualYear)
	{
		EnsureYear(annualYear);
		return !string.IsNullOrWhiteSpace(realmId)
			&& LiveRealmCounts.TryGetValue(realmId, out int count) ? count : 0;
	}

	internal static int GetLiveLineageRealmCount(string lineageId, string realmId, int annualYear)
	{
		EnsureYear(annualYear);
		string key = BuildLineageRealmKey(lineageId, realmId);
		return key.Length > 0 && LiveLineageRealmCounts.TryGetValue(key, out int count) ? count : 0;
	}

	internal static int GetLiveTraditionCount(string traditionId, int annualYear)
	{
		EnsureYear(annualYear);
		return !string.IsNullOrWhiteSpace(traditionId)
			&& LiveTraditionCounts.TryGetValue(traditionId, out int count) ? count : 0;
	}

	internal static int GetLiveTraditionRealmCount(string traditionId, string realmId, int annualYear)
	{
		EnsureYear(annualYear);
		string key = BuildTraditionRealmKey(traditionId, realmId);
		return key.Length > 0 && LiveTraditionRealmCounts.TryGetValue(key, out int count) ? count : 0;
	}

	internal static int GetLiveDharmaFormOrHigherCount(int annualYear)
	{
		return GetLiveRealmCount(XjShiRealmIds.DharmaForm, annualYear)
			+ GetLiveRealmCount(XjShiRealmIds.WorldHonored, annualYear);
	}

	/// <summary>
	/// 仙鉴修士名录使用的年度缓存。只收摩诃、法相、世尊，不在UI重扫全世界角色。
	/// 返回只读接口，内容在下一次年度重建或显式Invalidate后更新。
	/// </summary>
	internal static IReadOnlyList<long> GetLiveHighRealmActorIds(int annualYear)
	{
		EnsureYear(annualYear);
		return LiveHighRealmActorIds;
	}

	internal static string BuildJinDiId(long actorId) => XjShiDomainState.BuildJinDiId(actorId);

	internal static bool TryResolveActorId(string raw, out long actorId)
	{
		return long.TryParse((raw ?? string.Empty).Trim(), NumberStyles.Integer,
			CultureInfo.InvariantCulture, out actorId) && actorId > 0L;
	}

	internal static bool TryResolveLiveActor(string raw, out Actor actor)
	{
		actor = null;
		return TryResolveActorId(raw, out long actorId)
			&& XjActorRegistry.ResolveKnownOrWorld(actorId, out actor)
			&& actor?.data != null
			&& actor.isAlive();
	}

	internal static bool TryGetActiveJinDiOwner(string jinDiId, out Actor owner)
	{
		owner = null;
		if (!XjShiDomainState.TryGet(jinDiId, out XjShiDomainRecord domain)
			|| !string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| domain.OwnerActorId <= 0L) return false;
		return XjActorRegistry.ResolveKnownOrWorld(domain.OwnerActorId, out owner)
			&& owner?.data != null && owner.isAlive();
	}

	internal static bool IsJinDiAvailableForClaim(string domainId, long claimantActorId)
	{
		return XjShiDomainState.IsDomainAvailableForMoHeClaim(domainId, claimantActorId);
	}

	internal static bool TryFindAvailablePatron(Actor seeker, int annualYear, out Actor patron,
		out string domainId, out int alignment)
	{
		patron = null;
		domainId = string.Empty;
		alignment = 0;
		if (seeker?.data == null || !seeker.isAlive()) return false;
		EnsureYear(annualYear);

		long seekerId = ((BaseSystemData)seeker.data).id;
		XjActorAccessor.TryGetString(seeker, XjActorDataKeys.ShiLineageId, out string seekerLineage);
		XjActorAccessor.TryGetString(seeker, XjActorDataKeys.ShiTradition, out string seekerTradition);
		int bestScore = int.MinValue;
		for (int i = 0; i < MoHeNodes.Count; i++)
		{
			MoHeNode node = MoHeNodes[i];
			if (node.ActorId <= 0L || node.ActorId == seekerId || node.UsedSeats >= node.Capacity) continue;
			if (!XjActorRegistry.ResolveKnownOrWorld(node.ActorId, out Actor candidate)
				|| candidate?.data == null || !candidate.isAlive()) continue;
			if (!XjShiDomainState.TryGet(node.DomainId, out XjShiDomainRecord domain)
				|| !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) continue;

			int candidateAlignment = ResolveAlignment(seekerId, node.ActorId, seekerLineage, node.LineageId);
			int freeSeats = Math.Max(0, node.Capacity - node.UsedSeats);
			int lineageBonus = !string.IsNullOrWhiteSpace(seekerLineage)
				&& string.Equals(seekerLineage, node.LineageId, StringComparison.Ordinal) ? 500 : 0;
			int traditionBonus = !string.IsNullOrWhiteSpace(seekerTradition)
				&& string.Equals(seekerTradition, node.Tradition, StringComparison.Ordinal) ? 800 : 0;
			int score = freeSeats * 1000 + traditionBonus + lineageBonus
				+ candidateAlignment * 10
				- XjDeterministicHash.PositiveIndex(seekerId + node.ActorId, "shi_patron_tie", 100);
			if (score <= bestScore) continue;
			bestScore = score;
			patron = candidate;
			domainId = node.DomainId;
			alignment = candidateAlignment;
		}
		return patron != null;
	}


	internal static int ResolveBorrowedPower(string seatId, Actor patron)
	{
		int seatRank = XjShiCatalog.GetSeatRank(seatId);
		if (seatRank <= 0 || patron?.data == null || !patron.isAlive()) return 0;
		XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiDomainId, out string domainId);
		if (!XjShiDomainState.IsManifest(domainId)) return 0;
		XjActorAccessor.TryGetInt(patron, XjActorDataKeys.ShiDomainShockUntilYear, out int shockUntil);
		if (shockUntil >= Math.Max(1, XjYearTracker.CurrentYear)) return 0;
		XjActorAccessor.TryGetInt(patron, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		return Math.Max(1, seatRank
			+ Math.Min(3, Math.Max(0, completedLives) / 2));
	}



	internal static int ResolveSeatCapacity(Actor patron)
	{
		if (patron?.data == null || !patron.isAlive()) return 0;
		XjActorAccessor.TryGetInt(patron, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		XjActorAccessor.TryGetInt(patron, XjActorDataKeys.ShiAlignment, out int alignment);
		int growthBonus = 0;
		if (XjShiDomainState.TryGetForActor(patron, Math.Max(1, XjYearTracker.CurrentYear), out XjShiDomainRecord domain))
		{
			if (!string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) return 0;
			growthBonus = Math.Max(0, domain.Growth) / XjShiCatalog.DomainGrowthPerCapacity;
		}
		int lifeBonus = Math.Max(0, completedLives) / 2;
		int alignmentBonus = alignment >= 85 ? 2 : alignment >= 65 ? 1 : 0;
		return Math.Min(XjShiCatalog.MaximumLianMinCapacityPerMoHe,
			XjShiCatalog.BaseLianMinCapacityPerMoHe + lifeBonus + growthBonus + alignmentBonus);
	}


	internal static bool TryGetSeatUsage(Actor patron, out int usedSeats, out int capacity)
	{
		usedSeats = 0;
		capacity = 0;
		if (patron?.data == null || !patron.isAlive() || !XjCultivationPathRules.IsShi(patron)) return false;
		XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return false;
		EnsureYear(Math.Max(1, XjYearTracker.CurrentYear));
		long patronId = ((BaseSystemData)patron.data).id;
		SeatCountByPatron.TryGetValue(patronId, out usedSeats);
		capacity = ResolveSeatCapacity(patron);
		return capacity > 0;
	}


	internal static bool RegisterOrMoveAttachment(long dependentActorId, long patronActorId, int annualYear)
	{
		if (dependentActorId <= 0L || patronActorId <= 0L || dependentActorId == patronActorId) return false;
		EnsureYear(annualYear);
		if (!XjActorRegistry.ResolveKnownOrWorld(patronActorId, out Actor patron)
			|| patron?.data == null || !patron.isAlive() || !XjCultivationPathRules.IsShi(patron)) return false;
		XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiRealm, out string realm);
		XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiDomainId, out string patronDomain);
		if (XjShiCatalog.GetRank(realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)
			|| !XjShiDomainState.IsManifest(patronDomain)) return false;

		if (PatronByDependent.TryGetValue(dependentActorId, out long oldPatronId)
			&& oldPatronId == patronActorId) return true;
		SeatCountByPatron.TryGetValue(patronActorId, out int currentCount);
		if (currentCount >= ResolveSeatCapacity(patron)) return false;

		if (PatronByDependent.TryGetValue(dependentActorId, out oldPatronId))
		{
			if (DependentsByPatron.TryGetValue(oldPatronId, out List<long> oldList))
			{
				oldList.Remove(dependentActorId);
				if (oldList.Count == 0) DependentsByPatron.Remove(oldPatronId);
			}
			SeatCountByPatron.TryGetValue(oldPatronId, out int oldCount);
			SeatCountByPatron[oldPatronId] = Math.Max(0, oldCount - 1);
			UpdateMoHeUsedSeats(oldPatronId);
		}

		if (!DependentsByPatron.TryGetValue(patronActorId, out List<long> dependents))
		{
			dependents = new List<long>();
			DependentsByPatron[patronActorId] = dependents;
		}
		if (!dependents.Contains(dependentActorId)) dependents.Add(dependentActorId);
		dependents.Sort();
		PatronByDependent[dependentActorId] = patronActorId;
		SeatCountByPatron[patronActorId] = currentCount + 1;
		UpdateMoHeUsedSeats(patronActorId);
		_dirty = false;
		return true;
	}



	internal static void ReleaseAttachment(long dependentActorId, int annualYear)
	{
		if (dependentActorId <= 0L) return;
		EnsureYear(annualYear);
		if (!PatronByDependent.TryGetValue(dependentActorId, out long patronId)) return;
		PatronByDependent.Remove(dependentActorId);
		if (DependentsByPatron.TryGetValue(patronId, out List<long> dependents))
		{
			dependents.Remove(dependentActorId);
			if (dependents.Count == 0) DependentsByPatron.Remove(patronId);
		}
		SeatCountByPatron.TryGetValue(patronId, out int count);
		SeatCountByPatron[patronId] = Math.Max(0, count - 1);
		UpdateMoHeUsedSeats(patronId);
	}

	private static void UpdateMoHeUsedSeats(long patronActorId)
	{
		SeatCountByPatron.TryGetValue(patronActorId, out int usedSeats);
		for (int i = 0; i < MoHeNodes.Count; i++)
		{
			MoHeNode node = MoHeNodes[i];
			if (node.ActorId != patronActorId) continue;
			MoHeNodes[i] = new MoHeNode(node.ActorId, node.Tradition, node.LineageId,
				node.DomainId, node.Capacity, usedSeats, node.CompletedLives);
			break;
		}
	}

	internal static IReadOnlyList<long> GetDependentIds(long patronActorId, int annualYear)
	{
		EnsureYear(annualYear);
		return patronActorId > 0L && DependentsByPatron.TryGetValue(patronActorId, out List<long> ids)
			? ids
			: Array.Empty<long>();
	}


	internal static void RebindDependents(long oldPatronActorId, Actor newPatron, int annualYear,
		IReadOnlyList<long> explicitDependentIds = null)
	{
		if (oldPatronActorId <= 0L || newPatron?.data == null || !newPatron.isAlive()
			|| !XjCultivationPathRules.IsShi(newPatron)) return;
		long newPatronId = ((BaseSystemData)newPatron.data).id;
		if (newPatronId <= 0L) return;
		XjActorAccessor.TryGetString(newPatron, XjActorDataKeys.ShiDomainId, out string newDomainId);
		// 年度索引与轮回载荷取并集：前者覆盖正常同帧归返，后者覆盖跨存档/重载后索引尚未重建的情况。
		HashSet<long> idSet = new HashSet<long>(GetDependentIds(oldPatronActorId, annualYear));
		if (explicitDependentIds != null)
		{
			for (int i = 0; i < explicitDependentIds.Count; i++)
			{
				if (explicitDependentIds[i] > 0L) idSet.Add(explicitDependentIds[i]);
			}
		}
		List<long> ids = new List<long>(idSet);
		ids.Sort();
		for (int i = 0; i < ids.Count; i++)
		{
			long dependentId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(dependentId, out Actor dependent)
				|| dependent?.data == null || !dependent.isAlive()
				|| !XjCultivationPathRules.IsShi(dependent)) continue;
			XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiRealm, out string realm);
			if (!string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) continue;

			XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPatronActorId,
				newPatronId.ToString(CultureInfo.InvariantCulture));
			if (RegisterOrMoveAttachment(dependentId, newPatronId, Math.Max(1, annualYear)))
			{
				XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiSeatId, out string seatId);
				XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
				XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowedPower,
					ResolveBorrowedPower(seatId, newPatron));
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus,
					XjShiPositionStatusIds.Attached);
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiDomainId, newDomainId ?? string.Empty);
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiJinDiId, newDomainId ?? string.Empty);
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiJinDiStatus,
					XjShiJinDiStatusIds.Manifest);
			}
			else
			{
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus,
					XjShiPositionStatusIds.ReincarnationReserved);
			}
		}
		_dirty = true;
	}

	internal static bool CanReincarnate(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastAnnualYear, out int lastYear);
		int checkYear = Math.Max(1, Math.Max(lastYear, XjYearTracker.CurrentYear));
		if (XjShiHighRealmSystem.IsTrueSpiritLocked(actor, checkYear)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);

		// 今释自入道即挂靠释土服务器。只要真灵未被抹除，任何今释境界都可登记归返；
		// 旃檀林尚未放置时记录继续等待，不把“地图尚无落点”误判成真灵俱灭。
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			XjShiDomainState.EnsureZhantanlin(checkYear);
			return true;
		}

		// 古释自证自身，不挂靠释土服务器，也不进入今释真灵归返队列。
		return false;
	}

	private static void Rebuild(int annualYear)
	{
		_builtYear = annualYear;
		_dirty = false;
		SeatCountByPatron.Clear();
		DependentsByPatron.Clear();
		PatronByDependent.Clear();
		MoHeNodes.Clear();
		LiveHighRealmActorIds.Clear();
		LiveRealmCounts.Clear();
		LiveLineageRealmCounts.Clear();
		LiveTraditionCounts.Clear();
		LiveTraditionRealmCounts.Clear();

		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string liveLineageId);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string liveTradition);
			Increment(LiveRealmCounts, realm);
			if (XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
				LiveHighRealmActorIds.Add(actorId);
			Increment(LiveLineageRealmCounts, BuildLineageRealmKey(liveLineageId, realm));
			Increment(LiveTraditionCounts, liveTradition);
			Increment(LiveTraditionRealmCounts, BuildTraditionRealmKey(liveTradition, realm));
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPositionStatus, out string positionStatus);
			bool occupiesLianMinSeat = string.Equals(positionStatus, XjShiPositionStatusIds.Attached, StringComparison.Ordinal)
				|| string.Equals(positionStatus, XjShiPositionStatusIds.ReincarnationReserved, StringComparison.Ordinal)
				|| string.Equals(positionStatus, XjShiPositionStatusIds.SuccessionCandidate, StringComparison.Ordinal);
			if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				&& occupiesLianMinSeat
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPatronActorId, out string rawPatron)
				&& TryResolveActorId(rawPatron, out long patronId))
			{
				SeatCountByPatron.TryGetValue(patronId, out int count);
				SeatCountByPatron[patronId] = count + 1;
				if (!DependentsByPatron.TryGetValue(patronId, out List<long> dependents))
				{
					dependents = new List<long>();
					DependentsByPatron[patronId] = dependents;
				}
				dependents.Add(actorId);
				PatronByDependent[actorId] = patronId;
			}
		}

		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			if (!string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
			if (string.IsNullOrWhiteSpace(domainId)
				|| !XjShiDomainState.TryGet(domainId, out XjShiDomainRecord domain)
				|| !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) continue;
			SeatCountByPatron.TryGetValue(actorId, out int usedSeats);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out int completedLives);
			MoHeNodes.Add(new MoHeNode(actorId, tradition, lineageId, domainId,
				ResolveSeatCapacity(actor), usedSeats, completedLives));
		}

		MoHeNodes.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		foreach (List<long> dependents in DependentsByPatron.Values) dependents.Sort();
	}

	private static string BuildLineageRealmKey(string lineageId, string realmId)
	{
		string lineage = (lineageId ?? string.Empty).Trim();
		string realm = (realmId ?? string.Empty).Trim();
		return lineage.Length == 0 || realm.Length == 0 ? string.Empty : lineage + "|" + realm;
	}

	private static string BuildTraditionRealmKey(string traditionId, string realmId)
	{
		string tradition = (traditionId ?? string.Empty).Trim();
		string realm = (realmId ?? string.Empty).Trim();
		return tradition.Length == 0 || realm.Length == 0 ? string.Empty : tradition + "|" + realm;
	}

	private static void Increment(Dictionary<string, int> source, string key)
	{
		if (source == null || string.IsNullOrWhiteSpace(key)) return;
		source.TryGetValue(key, out int count);
		source[key] = count + 1;
	}

	private static int ResolveAlignment(long seekerId, long patronId, string seekerLineage, string patronLineage)
	{
		int value = 45 + XjDeterministicHash.PositiveIndex(seekerId + patronId, "shi_alignment", 41);
		if (!string.IsNullOrWhiteSpace(seekerLineage)
			&& string.Equals(seekerLineage, patronLineage, StringComparison.Ordinal)) value += 15;
		return Math.Clamp(value, 0, 100);
	}
}
