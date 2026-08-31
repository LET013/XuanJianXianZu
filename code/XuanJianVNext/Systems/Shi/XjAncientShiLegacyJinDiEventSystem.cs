using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 隐世古释遗金地的极低频世界事件。
/// 事件只复用年度世界车道与已有高境/释修索引，不逐帧扫描单位；
/// 金地无论被何道统发现都不转移所有权，今释也不能藉此把古释金地并入自己的释土。
/// </summary>
internal static class XjAncientShiLegacyJinDiEventSystem
{
	// 以下均为内部节奏常量，不写入玄鉴百科或普通 UI。
	private const int CheckIntervalYears = 25;
	private const int MinimumLegacyAgeYears = 80;
	private const int MinimumSameDomainEventGapYears = 360;
	private const int ResponseAwakeningMinimumLegacyAgeYears = 500;
	private const int LegacyManifestYears = 45;
	private const int DharmaArchiveManifestYears = 90;
	private const int MaximumNearbyDiscoveryDistance = 180;

	private enum DiscovererKind
	{
		None = 0,
		AncientShi = 1,
		ModernShi = 2,
		ZiJin = 3,
		FuQi = 4
	}

	private readonly struct DiscovererCandidate
	{
		internal readonly Actor Actor;
		internal readonly DiscovererKind Kind;
		internal readonly long Score;

		internal DiscovererCandidate(Actor actor, DiscovererKind kind, long score)
		{
			Actor = actor;
			Kind = kind;
			Score = score;
		}
	}

	internal static void TickYear(int annualYear)
	{
		int year = Math.Max(1, annualYear);
		if (year % CheckIntervalYears != 0) return;

		IReadOnlyList<XjShiDomainRecord> snapshot = XjShiDomainState.ReadSnapshot(year);
		List<XjShiDomainRecord> eligible = new List<XjShiDomainRecord>();
		for (int i = 0; i < snapshot.Count; i++)
		{
			XjShiDomainRecord domain = snapshot[i];
			if (!IsEligibleHiddenLegacy(domain, year)) continue;
			eligible.Add(domain);
		}
		if (eligible.Count <= 0) return;

		// 全世界同一次检测最多触发一处遗地，避免古释死亡较多的长档在同年成批弹事件。
		int pick = XjDeterministicHash.PositiveIndex(year + eligible.Count,
			"ancient_shi_legacy_jindi_pick_v1", eligible.Count);
		XjShiDomainRecord selected = eligible[pick];
		int eventBasis = Math.Min(1000, 250 + eligible.Count * 50);
		int eventRoll = XjDeterministicHash.PositiveIndex(
			XjDeterministicHash.StableHash(selected.DomainId) + year,
			"ancient_shi_legacy_jindi_event_v1", 10000);
		if (eventRoll >= eventBasis) return;

		string eventId = ResolveEventId(selected, year);
		DiscovererCandidate discoverer = FindDiscoverer(selected, year);
		long discovererId = discoverer.Actor?.data != null
			? ((BaseSystemData)discoverer.Actor.data).id : 0L;
		bool awakened = string.Equals(eventId, XjAncientShiLegacyEventIds.ResponseBodyAwakening, StringComparison.Ordinal);
		int manifestYears = string.Equals(eventId, XjAncientShiLegacyEventIds.DharmaArchive, StringComparison.Ordinal)
			? DharmaArchiveManifestYears : LegacyManifestYears;
		if (!XjShiDomainState.ApplyAncientLegacyEventState(selected.DomainId, year, eventId,
			manifestYears, awakened, discovererId)) return;

		string rewardText = ApplyDiscovererReward(discoverer, selected, eventId, year);
		RecordEvent(selected, eventId, discoverer.Actor, discoverer.Kind, rewardText, year);
	}

	private static bool IsEligibleHiddenLegacy(XjShiDomainRecord domain, int year)
	{
		if (domain == null
			|| XjAncientShiTempleSystem.IsTempleLegacyJinDi(domain.DomainId)
			|| !string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| !string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			|| !string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal)
			|| !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)
			|| domain.AncientLegacyResponseAwakened > 0) return false;
		int legacySince = domain.AncientLegacySinceYear > 0 ? domain.AncientLegacySinceYear : domain.CreatedYear;
		if (legacySince <= 0 || year - legacySince < MinimumLegacyAgeYears) return false;
		return domain.AncientLegacyLastEventYear <= 0
			|| year - domain.AncientLegacyLastEventYear >= MinimumSameDomainEventGapYears;
	}

	private static string ResolveEventId(XjShiDomainRecord domain, int year)
	{
		long seed = XjDeterministicHash.StableHash(domain.DomainId) + year + domain.AncientLegacyEventCount * 131L;
		int roll = XjDeterministicHash.PositiveIndex(seed, "ancient_shi_legacy_jindi_kind_v1", 100);
		bool canAwaken = domain.AncientLegacySinceYear > 0
			&& year - domain.AncientLegacySinceYear >= ResponseAwakeningMinimumLegacyAgeYears;
		if (canAwaken && roll >= 85) return XjAncientShiLegacyEventIds.ResponseBodyAwakening;
		if (roll >= 55) return XjAncientShiLegacyEventIds.DharmaArchive;
		return XjAncientShiLegacyEventIds.LegacyManifest;
	}

	private static DiscovererCandidate FindDiscoverer(XjShiDomainRecord domain, int year)
	{
		DiscovererCandidate best = default;
		HashSet<long> visited = new HashSet<long>();
		IReadOnlyList<long> shiIds = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < shiIds.Count; i++)
		{
			long actorId = shiIds[i];
			if (!visited.Add(actorId)
				|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || actor.current_tile == null
				|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shi)
				|| XjShiCatalog.GetRank(shi.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) continue;
			DiscovererKind kind = string.Equals(shi.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				? DiscovererKind.AncientShi
				: string.Equals(shi.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
					? DiscovererKind.ModernShi : DiscovererKind.None;
			if (kind == DiscovererKind.None) continue;
			TryTakeCandidate(domain, year, actor, kind, ref best);
		}

		IReadOnlyList<long> immortalIds = XjCultivatorCache.GetZhenRenOrHigherIds();
		for (int i = 0; i < immortalIds.Count; i++)
		{
			long actorId = immortalIds[i];
			if (!visited.Add(actorId)
				|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || actor.current_tile == null
				|| XjCultivationPathRules.IsShi(actor)) continue;
			DiscovererKind kind = XjCultivationPathRules.IsFuQiYangXing(actor)
				? DiscovererKind.FuQi
				: XjCultivationPathRules.IsZiFuJinDan(actor) ? DiscovererKind.ZiJin : DiscovererKind.None;
			if (kind == DiscovererKind.None) continue;
			TryTakeCandidate(domain, year, actor, kind, ref best);
		}
		return best;
	}

	private static void TryTakeCandidate(XjShiDomainRecord domain, int year, Actor actor,
		DiscovererKind kind, ref DiscovererCandidate best)
	{
		long score = ResolveDiscoveryScore(domain, actor, year);
		if (score == long.MaxValue) return;
		if (best.Actor == null || score < best.Score)
			best = new DiscovererCandidate(actor, kind, score);
	}

	private static long ResolveDiscoveryScore(XjShiDomainRecord domain, Actor actor, int year)
	{
		if (domain == null || actor?.data == null || actor.current_tile == null) return long.MaxValue;
		long actorId = ((BaseSystemData)actor.data).id;
		long distance = 0L;
		if (domain.MapRadius > 0)
		{
			var pos = actor.current_tile.pos;
			long dx = pos.x - domain.MapCenterX;
			long dy = pos.y - domain.MapCenterY;
			distance = dx * dx + dy * dy;
			long maximum = (long)MaximumNearbyDiscoveryDistance * MaximumNearbyDiscoveryDistance;
			if (distance > maximum) return long.MaxValue;
		}
		long jitter = XjDeterministicHash.PositiveIndex(actorId + year + XjDeterministicHash.StableHash(domain.DomainId),
			"ancient_shi_legacy_discoverer_v1", 4096);
		return distance * 4096L + jitter;
	}

	private static string ApplyDiscovererReward(DiscovererCandidate discoverer, XjShiDomainRecord domain,
		string eventId, int year)
	{
		Actor actor = discoverer.Actor;
		if (actor?.data == null || discoverer.Kind == DiscovererKind.None) return string.Empty;
		int grade = string.Equals(eventId, XjAncientShiLegacyEventIds.ResponseBodyAwakening, StringComparison.Ordinal) ? 3
			: string.Equals(eventId, XjAncientShiLegacyEventIds.DharmaArchive, StringComparison.Ordinal) ? 2 : 1;
		string key = "ancient_legacy_jindi:" + domain.DomainId + ":" + year.ToString(CultureInfo.InvariantCulture);

		switch (discoverer.Kind)
		{
			case DiscovererKind.AncientShi:
			{
				float before = XjShiMingShuSystem.GetValue(actor);
				XjShiMingShuSystem.TryGrantEvent(actor, year, key, grade * 2f, "insight");
				XjAncientShiVowSystem.OnQuietBlessing(actor, year);
				float gained = Math.Max(0f, XjShiMingShuSystem.GetValue(actor) - before);
				return gained > 0.0001f ? "释修命数+" + ((int)Math.Floor(gained)).ToString(CultureInfo.InvariantCulture) + "，宏愿有所应"
					: "宏愿有所应";
			}
			case DiscovererKind.ModernShi:
			{
				float before = XjShiMingShuSystem.GetValue(actor);
				XjShiMingShuSystem.TryGrantEvent(actor, year, key, 1f + grade, "insight");
				XjShiDomainState.AddContribution(actor, grade, year);
				float gained = Math.Max(0f, XjShiMingShuSystem.GetValue(actor) - before);
				return gained > 0.0001f ? "释修命数+" + ((int)Math.Floor(gained)).ToString(CultureInfo.InvariantCulture) + "，承载修持得益"
					: "承载修持得益";
			}
			case DiscovererKind.FuQi:
				return ApplyFuQiInsight(actor, grade);
			case DiscovererKind.ZiJin:
				return ApplyZiJinInsight(actor, grade);
			default:
				return string.Empty;
		}
	}

	private static string ApplyFuQiInsight(Actor actor, int grade)
	{
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float before);
		float after = XjDaoHuiPolicy.Add(before, Math.Max(1, grade), XjDaoHuiPolicy.RareGrowthCeiling);
		float delta = (float)Math.Floor(Math.Max(0f, after - before));
		if (delta > 0f)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, after);
			return "道慧+" + ((int)delta).ToString(CultureInfo.InvariantCulture);
		}
		XjMingShuState.AddAcquired(actor, 1f);
		return "命数+1";
	}

	private static string ApplyZiJinInsight(Actor actor, int grade)
	{
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		float before = snapshot.ZhenYuan;
		float realmCap = XjCultivationGrowthRules.ApplyRealmCap(snapshot, float.MaxValue);
		float requested = Math.Max(1f, (float)Math.Floor(realmCap * (0.01f + grade * 0.01f)));
		float after = XjCultivationGrowthRules.ApplyRealmCap(snapshot, before + requested);
		after = XjBottleneckEventSystem.ApplyGrowthGate(actor, in snapshot, after);
		float delta = (float)Math.Floor(Math.Max(0f, after - before));
		if (delta > 0f)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, after);
			return "真元+" + ((int)delta).ToString(CultureInfo.InvariantCulture);
		}
		XjMingShuState.AddAcquired(actor, 1f);
		return "命数+1";
	}

	private static void RecordEvent(XjShiDomainRecord domain, string eventId, Actor discoverer,
		DiscovererKind kind, string rewardText, int year)
	{
		string domainName = XjShiDomainCatalog.GetDomainDisplayName(domain);
		string formerOwner = string.IsNullOrWhiteSpace(domain.AncientLegacyFormerOwnerName)
			? "古释旧主" : domain.AncientLegacyFormerOwnerName.Trim();
		string eventName = XjAncientShiLegacyEventIds.GetDisplay(eventId);
		string discovererName = discoverer?.data != null ? discoverer.getName() : string.Empty;
		string finderText = string.IsNullOrWhiteSpace(discovererName)
			? "，当世尚无人得入其中"
			: "，为" + discovererName + "所感";
		string body = "【" + eventName + "】" + formerOwner + "遗下的" + domainName + ResolveEventBody(eventId) + finderText + "。";
		XjWorldHistoryStore.RecordWorldEvent(body, XjShiTraitIds.Ancient);
		if (discoverer?.data != null)
		{
			string actorBody = BuildDiscovererHistory(kind, domainName, eventId, rewardText);
			XjWorldHistoryStore.RecordActorEvent(discoverer, actorBody, XjShiTraitIds.Ancient);
		}
		XjShiAnnouncementSystem.OnAncientLegacyJinDiEvent(domainName, formerOwner, eventId,
			discovererName, rewardText);
	}

	private static string ResolveEventBody(string eventId)
	{
		if (string.Equals(eventId, XjAncientShiLegacyEventIds.ResponseBodyAwakening, StringComparison.Ordinal))
			return "中旧应身由寂转灵，金地自此常显于世";
		if (string.Equals(eventId, XjAncientShiLegacyEventIds.DharmaArchive, StringComparison.Ordinal))
			return "忽开旧藏，前人修证所留法意与道藏一并显露";
		return "自隐世中短暂显露，旧日应身气机再现";
	}

	private static string BuildDiscovererHistory(DiscovererKind kind, string domainName, string eventId, string rewardText)
	{
		string eventName = XjAncientShiLegacyEventIds.GetDisplay(eventId);
		string action = kind switch
		{
			DiscovererKind.AncientShi => "循同门古法参悟应身遗泽",
			DiscovererKind.ModernShi => "观照古释遗地而不取其权属",
			DiscovererKind.FuQi => "以服气养性之法观古释应身",
			DiscovererKind.ZiJin => "以紫府金丹之法参悟遗地法意",
			_ => "得见遗地"
		};
		return "逢【" + eventName + "】得见" + domainName + "，" + action
			+ (string.IsNullOrWhiteSpace(rewardText) ? "。" : "，" + rewardText + "。");
	}
}
