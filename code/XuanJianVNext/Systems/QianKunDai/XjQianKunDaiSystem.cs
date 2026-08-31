using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.QianKunDai;

/// <summary>
/// 乾坤袋状态构建系统
/// 从 actor 已有功法与纳气资源构建乾坤袋真实库存
/// 供 UI 显示和死亡继承使用
/// </summary>
internal static class XjQianKunDaiSystem
{
	internal static void UpdateState(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (XjLongShuSystem.IsExcludedFromInheritance(actor))
		{
			XjQianKunDaiRegistry.RemoveInventory(actorId);
			return;
		}

		int currentYear = System.Math.Max(0, XjYearTracker.CurrentYear);
		string actorName = actor.getName() ?? string.Empty;
		XjQianKunDaiRegistry.TryGet(actorId, out XjQianKunDaiState existing);
		List<XjQianKunDaiItem> items = BuildSyncedInventory(actor, existing.Items, currentYear);

		// 构建各分类摘要
		string caiQiSummary = BuildCaiQiSummary(actor);
		string resourceSummary = BuildResourceSummary(actor);

		if (string.IsNullOrWhiteSpace(caiQiSummary)
			&& string.IsNullOrWhiteSpace(resourceSummary)
			&& items.Count == 0
			&& !existing.Found)
		{
			return;
		}

		string summary = string.Empty;
		bool unchanged = existing.Found
			&& string.Equals(existing.CaiQiSummary, caiQiSummary, StringComparison.Ordinal)
			&& string.Equals(existing.ResourceSummary, resourceSummary, StringComparison.Ordinal)
			&& string.Equals(existing.Summary, summary, StringComparison.Ordinal)
			&& AreItemsEqual(existing.Items, items);

		var state = new XjQianKunDaiState(
			true, actorId, actorName,
			caiQiSummary, string.Empty, resourceSummary,
			unchanged ? existing.UpdatedYear : currentYear, summary,
			existing.Found ? existing.Capacity : XjQianKunDaiRegistry.DefaultCapacity,
			items);

		XjQianKunDaiRegistry.AddOrUpdate(state);
	}

	private static bool AreItemsEqual(IReadOnlyList<XjQianKunDaiItem> left, IReadOnlyList<XjQianKunDaiItem> right)
	{
		left ??= Array.Empty<XjQianKunDaiItem>();
		right ??= Array.Empty<XjQianKunDaiItem>();
		if (left.Count != right.Count)
		{
			return false;
		}

		for (int i = 0; i < left.Count; i++)
		{
			XjQianKunDaiItem a = left[i];
			XjQianKunDaiItem b = right[i];
			if (!string.Equals(a.ItemId, b.ItemId, StringComparison.Ordinal)
				|| !string.Equals(a.DisplayName, b.DisplayName, StringComparison.Ordinal)
				|| !string.Equals(a.Category, b.Category, StringComparison.Ordinal)
				|| !string.Equals(a.Source, b.Source, StringComparison.Ordinal)
				|| !string.Equals(a.DaoTu, b.DaoTu, StringComparison.Ordinal)
				|| a.Count != b.Count
				|| a.AcquiredYear != b.AcquiredYear)
			{
				return false;
			}
		}

		return true;
	}

	private static List<XjQianKunDaiItem> BuildSyncedInventory(
		Actor actor,
		IReadOnlyList<XjQianKunDaiItem> existingItems,
		int currentYear)
	{
		List<XjQianKunDaiItem> items = new List<XjQianKunDaiItem>(existingItems ?? Array.Empty<XjQianKunDaiItem>());
		items.RemoveAll(item => string.Equals(item.Category, XjQianKunDaiRegistry.CategoryGongFa, StringComparison.Ordinal)
			|| string.Equals(item.Category, XjQianKunDaiRegistry.CategoryCaiQi, StringComparison.Ordinal));
		AppendGongFaInventory(actor, items, currentYear);
		AppendCaiQiInventory(actor, items, currentYear);
		return items;
	}

	private static void AppendCaiQiInventory(Actor actor, List<XjQianKunDaiItem> items, int currentYear)
	{
		XjCaiQiSnapshot caiQi = XjCaiQiActorAccessor.BuildSnapshot(actor);
		string currentResourceId = (caiQi.ResourceId ?? string.Empty).Trim();
		int currentCount = Math.Max(0, caiQi.ResourceCount);

		if (currentCount <= 0 || string.IsNullOrWhiteSpace(currentResourceId))
		{
			return;
		}

		string displayName = "未名先天之气";
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(currentResourceId, out string resolvedName))
			displayName = resolvedName;
		else if (string.Equals(currentResourceId, "zaqi", StringComparison.Ordinal))
			displayName = "杂气";

		items.Add(new XjQianKunDaiItem(
			currentResourceId,
			displayName,
			XjQianKunDaiRegistry.CategoryCaiQi,
			caiQi.SiteName,
			string.Empty,
			currentCount,
			currentYear));
	}

	private static void AppendGongFaInventory(Actor actor, List<XjQianKunDaiItem> items, int currentYear)
	{
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (gongFa.Found && !string.IsNullOrWhiteSpace(gongFa.Name))
		{
			items.Add(new XjQianKunDaiItem(
				"gongfa.current." + NormalizeItemId(gongFa.Name),
				gongFa.Name.Trim() + "（" + FormatGongFaGrade(gongFa.Grade) + "）",
				XjQianKunDaiRegistry.CategoryGongFa,
				"当前功法",
				gongFa.DaoTu,
				1,
				currentYear));
		}

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (!qiuJinFa.Found || !qiuJinFa.Ready || string.IsNullOrWhiteSpace(qiuJinFa.Name))
		{
			return;
		}

		items.Add(new XjQianKunDaiItem(
			"qiujinfa." + NormalizeItemId(qiuJinFa.Name),
			qiuJinFa.Name.Trim() + "（求金法）",
			XjQianKunDaiRegistry.CategoryGongFa,
			string.IsNullOrWhiteSpace(qiuJinFa.SourceGongFaName) ? "求金法" : qiuJinFa.SourceGongFaName,
			qiuJinFa.SourceDaoTu,
			1,
			qiuJinFa.LastYear > 0 ? qiuJinFa.LastYear : currentYear));
	}


	private static string BuildCaiQiSummary(Actor actor)
	{
		XjCaiQiSnapshot snapshot = XjCaiQiActorAccessor.BuildSnapshot(actor);
		if (!snapshot.HasCompletedCaiQi)
		{
			return string.Empty;
		}

		System.Text.StringBuilder builder = new System.Text.StringBuilder();
		if (!string.IsNullOrWhiteSpace(snapshot.ResourceId))
		{
			builder.Append(ResolveNaQiDisplayName(snapshot.ResourceId));
			if (snapshot.ResourceCount > 1)
			{
				builder.Append('×');
				builder.Append(snapshot.ResourceCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
			}
		}

		return builder.ToString();
	}

	private static string ResolveNaQiDisplayName(string resourceId)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string resolvedName))
		{
			return resolvedName;
		}

		return string.Equals(resourceId, "zaqi", StringComparison.Ordinal) ? "杂气" : "未名先天之气";
	}

	private static string BuildResourceSummary(Actor actor)
	{
		List<string> parts = new List<string>(4);

		// 功法
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (gongFa.Found && !string.IsNullOrWhiteSpace(gongFa.Name))
		{
			parts.Add("功法:" + gongFa.Name);
		}

		return parts.Count > 0 ? string.Join("\n", parts) : string.Empty;
	}
	private static string FormatGongFaGrade(int grade)
	{
		int normalizedGrade = grade > XjGongFaDefinition.MaxGrade ? XjGongFaDefinition.MaxGrade : grade;
		return normalizedGrade switch
		{
			1 => "一品",
			2 => "二品",
			3 => "三品",
			4 => "四品",
			5 => "五品",
			6 => "六品",
			_ => normalizedGrade.ToString(CultureInfo.InvariantCulture) + "品"
		};
	}

	private static string NormalizeItemId(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return "unknown";
		}

		return XjDeterministicHash.StableHash(value.Trim()).ToString(CultureInfo.InvariantCulture);
	}
}


