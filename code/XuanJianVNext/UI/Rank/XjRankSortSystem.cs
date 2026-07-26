using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.UI.Rank;

/// <summary>Thefantasyworld移植: 多排序键系统</summary>
internal sealed class XjRankSortKeyDef
{
	internal string Id, Name, IconPath;
	internal Func<Actor, float> GetValue;
	internal Func<Actor, string> GetDisplay;

	internal XjRankSortKeyDef(string id, string name, string icon,
		Func<Actor, float> val, Func<Actor, string> disp)
	{ Id = id; Name = name; IconPath = icon; GetValue = val; GetDisplay = disp; }
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
	private static readonly Dictionary<Actor, XjRankMetricSnapshot> MetricCache = new();

	internal static readonly List<XjRankSortKeyDef> SortKeyDefs = new();

	internal static void Init()
	{
		if (SortKeyDefs.Count > 0) return;
		void Add(string id, string name, string icon, Func<Actor, float> val, Func<Actor, string> disp)
			=> SortKeyDefs.Add(new XjRankSortKeyDef(id, name, icon, val, disp));

		Add("power", "战力", "ui/icons/iconDamage", a => GetMetrics(a).Power, a => FormatNum(GetMetrics(a).Power, "战力"));
		Add("realm", "境界", "ui/icons/iconLevels",
			a => { XjRankMetricSnapshot metrics = GetMetrics(a); return XjRankMetrics.ResolveRealmSortValue(in metrics); },
			a => GetRealmDisplay(a));
		Add("zhenyuan", "真元", "ZhenYuan", a => GetMetrics(a).ZhenYuan,
			a => FormatNum(GetMetrics(a).ZhenYuan, "真元"));
		Add("mingshu", "命数", "MingShu", a => GetMetrics(a).MingShu,
			a => FormatNum(GetMetrics(a).MingShu, "命数"));
		Add("huiguang", "慧光", "HuiGuang", a => GetMetrics(a).HuiGuang,
			a => FormatNum(GetMetrics(a).HuiGuang, "慧光"));
		Add("aptitude", "资质", "trait/XjZz6", a => GetMetrics(a).Aptitude,
			a => GetAptitudeName(GetMetrics(a).Aptitude) + " 资质");
		Add("age", "年龄", "ui/icons/iconAge", a => a?.data?.getAge() ?? 0, a => $"{a?.data?.getAge() ?? 0} 岁");
	}

	internal static void ClearCache()
	{
		MetricCache.Clear();
	}

	private static XjRankMetricSnapshot GetMetrics(Actor actor)
	{
		if (actor == null)
		{
			return default;
		}

		if (!MetricCache.TryGetValue(actor, out XjRankMetricSnapshot metrics))
		{
			metrics = XjRankMetrics.Build(actor);
			MetricCache[actor] = metrics;
		}
		return metrics;
	}

	private static string GetRealmDisplay(Actor actor)
	{
		XjRankMetricSnapshot metrics = GetMetrics(actor);
		return XjRankMetrics.ResolveRealmDisplay(in metrics);
	}

	internal static string GetAptitudeName(int zz) => zz switch
	{
		1 => "朽木难雕", 2 => "可琢之材", 3 => "璞玉之资", 4 => "上乘根骨",
		5 => "天公垂目", 6 => "天授道脉", 7 => "先天道体", 8 => "经脉堵塞", 9 => "气血衰败",
		_ => "未测资质"
	};

	internal static string FormatNum(float v, string suffix)
	{
		if (v >= 1e8f) return $"{v / 1e8f:F2}亿{suffix}";
		if (v >= 1e4f) return $"{v / 1e4f:F2}万{suffix}";
		return $"{v:F0} {suffix}";
	}
}
