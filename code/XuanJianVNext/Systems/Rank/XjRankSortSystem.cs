using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Rank;

/// <summary>Thefantasyworld移植: 多排序键系统</summary>
internal sealed class XjRankSortKeyDef
{
	internal string Id, Name, IconPath;
	internal Func<Actor, float> GetValue;
	internal Func<Actor, string> GetDisplay;
	internal Func<Actor, bool> MatchesActor;

	internal XjRankSortKeyDef(string id, string name, string icon,
		Func<Actor, float> val, Func<Actor, string> disp, Func<Actor, bool> matchesActor = null)
	{
		Id = id;
		Name = name;
		IconPath = icon;
		GetValue = val;
		GetDisplay = disp;
		MatchesActor = matchesActor;
	}
}

internal sealed class XjRankSortKey
{
	internal XjRankSortKeyDef Def;
	internal bool Ascending;
	internal XjRankSortKey(XjRankSortKeyDef def) { Def = def; }
	internal int Compare(Actor a, Actor b) =>
		(Ascending ? 1 : -1) * Def.GetValue(a).CompareTo(Def.GetValue(b));
	internal void Toggle() => Ascending = !Ascending;
}

internal static class XjRankSortSystem
{
	private readonly struct MetricCacheEntry
	{
		internal readonly int InstanceToken;
		internal readonly XjRankMetricSnapshot Metrics;

		internal MetricCacheEntry(int instanceToken, XjRankMetricSnapshot metrics)
		{
			InstanceToken = instanceToken;
			Metrics = metrics;
		}
	}

	// Long-lived UI caches keep stable ids and immutable snapshots only. The runtime
	// instance token preserves the old Actor-key semantics when another mod replaces
	// an Actor object while retaining the same actor id.
	private static readonly Dictionary<long, MetricCacheEntry> MetricCache = new();

	internal static readonly List<XjRankSortKeyDef> SortKeyDefs = new();

	internal static void Init()
	{
		if (SortKeyDefs.Count > 0) return;
		void Add(string id, string name, string icon, Func<Actor, float> val, Func<Actor, string> disp, Func<Actor, bool> matchesActor = null)
			=> SortKeyDefs.Add(new XjRankSortKeyDef(id, name, icon, val, disp, matchesActor));

		Add("shi_ancient", "古释", "trait/XjGuShi", GetAncientShiScore, GetRealmDisplay, IsAncientShi);
		Add("shi_modern", "今释", "trait/XjJinShi", GetModernShiScore, GetRealmDisplay, IsModernShi);
		Add("fuqi", "服气", "trait/XjRealm12", GetFuQiScore, GetRealmDisplay, XjCultivationPathRules.IsFuQiYangXing);
		Add("zijin", "紫金", "trait/XjRealm5", GetZiJinScore, GetRealmDisplay, XjCultivationPathRules.IsZiFuJinDan);
		Add("power", "战力", "ui/icons/iconDamage", a => GetMetrics(a).Power, a => FormatNum(GetMetrics(a).Power, string.Empty));
		Add("realm", "境界", "ui/icons/iconLevels",
			a => { XjRankMetricSnapshot metrics = GetMetrics(a); return XjRankMetrics.ResolveRealmSortValue(in metrics); },
			a => GetRealmDisplay(a));
		Add("zhenyuan", "真元", "ZhenYuan", a => GetMetrics(a).ZhenYuan,
			a => FormatNum(GetMetrics(a).ZhenYuan, string.Empty));
		Add("mingshu", "命数", "MingShu", a => GetMetrics(a).MingShu,
			a => FormatNum(GetMetrics(a).MingShu, string.Empty));
		Add("huiguang", "道慧", "HuiGuang", a => GetMetrics(a).HuiGuang,
			a => FormatNum(GetMetrics(a).HuiGuang, string.Empty));
		Add("aptitude", "资质", "trait/XjZz6", a => GetMetrics(a).Aptitude,
			a => GetAptitudeName(GetMetrics(a).Aptitude));
		Add("alchemy", "丹道", "trait/LianDanShi",
			a => XjCraftProficiencySystem.GetRankingScore(a, XjCraftTraitRules.AlchemyTraitId),
			a => XjCraftProficiencySystem.GetRankingDisplay(a, XjCraftTraitRules.AlchemyTraitId));
		Add("artifact", "器道", "trait/LianQiShi",
			a => XjCraftProficiencySystem.GetRankingScore(a, XjCraftTraitRules.ArtifactRefiningTraitId),
			a => XjCraftProficiencySystem.GetRankingDisplay(a, XjCraftTraitRules.ArtifactRefiningTraitId));
		Add("talisman", "符箓", "trait/FuLuShi",
			a => XjCraftProficiencySystem.GetRankingScore(a, XjCraftTraitRules.TalismanTraitId),
			a => XjCraftProficiencySystem.GetRankingDisplay(a, XjCraftTraitRules.TalismanTraitId));
		Add("formation", "阵法", "trait/ZhenFaShi",
			a => XjCraftProficiencySystem.GetRankingScore(a, XjCraftTraitRules.FormationTraitId),
			a => XjCraftProficiencySystem.GetRankingDisplay(a, XjCraftTraitRules.FormationTraitId));
		Add("sword_intent", "剑意", "item/Arts/Equipment/Attack/xj_jian-5",
			GetSwordIntentRankingScore, GetSwordIntentRankingDisplay);
		Add("age", "年龄", "ui/icons/iconAge", a => a?.data?.getAge() ?? 0, a => $"{a?.data?.getAge() ?? 0} 岁");
	}

	internal static int RuntimeMetricCacheCount => MetricCache.Count;

	internal static void RemoveActor(long actorId)
	{
		if (actorId > 0L) MetricCache.Remove(actorId);
	}

	internal static void ClearCache()
	{
		MetricCache.Clear();
	}

	private static XjRankMetricSnapshot GetMetrics(Actor actor)
	{
		if (actor?.data == null)
		{
			return default;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return XjRankMetrics.Build(actor);
		XjStaleActorIdEviction.Track(actorId);
		int instanceToken = RuntimeHelpers.GetHashCode(actor);
		if (MetricCache.TryGetValue(actorId, out MetricCacheEntry cached)
			&& cached.InstanceToken == instanceToken)
		{
			return cached.Metrics;
		}

		XjRankMetricSnapshot metrics = XjRankMetrics.Build(actor);
		MetricCache[actorId] = new MetricCacheEntry(instanceToken, metrics);
		return metrics;
	}

	private static string GetRealmDisplay(Actor actor)
	{
		XjRankMetricSnapshot metrics = GetMetrics(actor);
		return XjRankMetrics.ResolveRealmDisplay(in metrics);
	}

	private static float GetAncientShiScore(Actor actor) => GetPathRealmScore(actor, IsAncientShi);
	private static float GetModernShiScore(Actor actor) => GetPathRealmScore(actor, IsModernShi);
	private static float GetFuQiScore(Actor actor) => GetPathRealmScore(actor, XjCultivationPathRules.IsFuQiYangXing);
	private static float GetZiJinScore(Actor actor) => GetPathRealmScore(actor, XjCultivationPathRules.IsZiFuJinDan);

	private static float GetPathRealmScore(Actor actor, Func<Actor, bool> matches)
	{
		if (actor == null || matches == null || !matches(actor)) return -1f;
		XjRankMetricSnapshot metrics = GetMetrics(actor);
		return 10000f + XjRankMetrics.ResolveRealmSortValue(in metrics);
	}

	private static bool IsAncientShi(Actor actor) => HasShiTradition(actor, XjShiTraditionIds.Ancient);
	private static bool IsModernShi(Actor actor) => HasShiTradition(actor, XjShiTraditionIds.Modern);

	private static bool HasShiTradition(Actor actor, string tradition)
	{
		return XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			&& string.Equals(snapshot.Tradition, tradition, StringComparison.Ordinal);
	}

	private static float GetSwordIntentRankingScore(Actor actor)
	{
		XjWeaponArtState state = XjWeaponArtSystem.ReadState(actor);
		return state.Found && string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			? state.Rank * 1000f + state.Proficiency
			: 0f;
	}

	private static string GetSwordIntentRankingDisplay(Actor actor)
	{
		XjWeaponArtState state = XjWeaponArtSystem.ReadState(actor);
		if (!state.Found || !string.Equals(state.Kind, XjWeaponArtKinds.Sword, StringComparison.Ordinal))
		{
			return "未修剑道";
		}
		string stage = state.Rank >= XjWeaponArtRanks.Yi && !string.IsNullOrWhiteSpace(state.Alias)
			? state.Alias.Trim()
			: XjWeaponArtKinds.Sword + XjWeaponArtRanks.Suffix(state.Rank);
		return stage + " · 熟练 " + state.Proficiency;
	}

	internal static string GetAptitudeName(int zz) => zz switch
	{
		1 => "朽木难雕", 2 => "可琢之材", 3 => "璞玉之资", 4 => "上乘根骨",
		5 => "天公垂目", 6 => "天授道脉", 7 => "先天道体", 8 => "经脉堵塞", 9 => "气血衰败",
		_ => "未测资质"
	};

	internal static string FormatNum(float v, string suffix)
	{
		string suffixText = string.IsNullOrWhiteSpace(suffix) ? string.Empty : suffix.Trim();
		if (v >= 1e8f) return $"{v / 1e8f:F2}亿{suffixText}";
		if (v >= 1e4f) return $"{v / 1e4f:F2}万{suffixText}";
		return suffixText.Length == 0 ? $"{v:F0}" : $"{v:F0} {suffixText}";
	}
}
