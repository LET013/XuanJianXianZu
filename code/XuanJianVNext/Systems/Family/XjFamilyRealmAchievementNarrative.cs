using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Family;

internal enum XjFamilyRealmAchievementState
{
	First = 0,
	Again = 1,
	Revival = 2
}

internal readonly struct XjFamilyRealmAchievement
{
	internal readonly bool Found;
	internal readonly long FamilyStableId;
	internal readonly string FamilyName;
	internal readonly XjFamilyRealmAchievementState State;
	internal readonly int CurrentHighestOrder;
	internal readonly int HistoricalHighestOrder;

	internal XjFamilyRealmAchievement(
		bool found,
		long familyStableId,
		string familyName,
		XjFamilyRealmAchievementState state,
		int currentHighestOrder,
		int historicalHighestOrder)
	{
		Found = found;
		FamilyStableId = familyStableId > 0L ? familyStableId : 0L;
		FamilyName = familyName ?? string.Empty;
		State = state;
		CurrentHighestOrder = Math.Max(0, currentHighestOrder);
		HistoricalHighestOrder = Math.Max(0, historicalHighestOrder);
	}
}

/// <summary>
/// 家族高境公告的唯一判定源。
/// 首次：历代从未出现该层级；再度：当前仍有该层级或更高者；
/// 重振：历代曾有，但当前已经断层。所有判断均排除本次刚晋升者。
/// </summary>
internal static class XjFamilyRealmAchievementNarrative
{
	internal static XjFamilyRealmAchievement Resolve(Actor actor, string targetRealmId)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		long familyId = ResolveFamilyStableId(actor, actorId);
		return ResolveCore(
			familyId,
			actorId,
			ResolveFallbackFamilyName(actor),
			targetRealmId);
	}

	internal static XjFamilyRealmAchievement Resolve(
		long familyStableId,
		long actorId,
		string actorName,
		string targetRealmId)
	{
		return ResolveCore(
			familyStableId,
			actorId,
			ResolveFallbackFamilyName(actorName),
			targetRealmId);
	}

	private static XjFamilyRealmAchievement ResolveCore(
		long familyId,
		long actorId,
		string fallbackFamilyName,
		string targetRealmId)
	{
		int targetOrder = XjRealmHelper.GetOrder(targetRealmId);
		if (familyId <= 0L || targetOrder <= 0)
		{
			return new XjFamilyRealmAchievement(
				false, 0L, fallbackFamilyName,
				XjFamilyRealmAchievementState.First, 0, 0);
		}

		int currentHighest = XjFamilyMemberLedger.GetCurrentHighestRealmOrderExcluding(familyId, actorId);
		int historicalHighest = XjFamilyMemberLedger.GetHistoricalHighestRealmOrderExcluding(familyId, actorId);
		XjFamilyRealmAchievementState state = currentHighest >= targetOrder
			? XjFamilyRealmAchievementState.Again
			: historicalHighest >= targetOrder
				? XjFamilyRealmAchievementState.Revival
				: XjFamilyRealmAchievementState.First;

		string familyName = XjFamilyDisplayNameResolver.Resolve(familyId);
		if (string.IsNullOrWhiteSpace(familyName) || string.Equals(familyName, "未名氏", StringComparison.Ordinal))
		{
			familyName = fallbackFamilyName;
		}
		return new XjFamilyRealmAchievement(
			true, familyId, familyName, state, currentHighest, historicalHighest);
	}

	internal static string BuildTag(in XjFamilyRealmAchievement achievement, string realmLabel)
	{
		if (!achievement.Found || string.IsNullOrWhiteSpace(achievement.FamilyName)) return string.Empty;
		string label = string.IsNullOrWhiteSpace(realmLabel) ? "高境" : realmLabel.Trim();
		string action = achievement.State switch
		{
			XjFamilyRealmAchievementState.Again => "再度成就",
			XjFamilyRealmAchievementState.Revival => "重振",
			_ => "首次成就"
		};
		return "【" + achievement.FamilyName.Trim() + "·" + action + label + "】";
	}

	internal static string BuildEnding(in XjFamilyRealmAchievement achievement, string realmLabel)
	{
		if (!achievement.Found) return "。";
		string label = string.IsNullOrWhiteSpace(realmLabel) ? "高境" : realmLabel.Trim();
		return achievement.State switch
		{
			XjFamilyRealmAchievementState.Again => "，族中高位仍在，今又添一位" + label + "。",
			XjFamilyRealmAchievementState.Revival => "，昔日" + label + "一脉曾绝，今朝再出" + label + "，重振门楣。",
			_ => BuildFirstEnding(label)
		};
	}

	internal static string BuildShortTitle(in XjFamilyRealmAchievement achievement, string realmLabel)
	{
		string label = string.IsNullOrWhiteSpace(realmLabel) ? "高境" : realmLabel.Trim();
		return achievement.State switch
		{
			XjFamilyRealmAchievementState.Again => "再添" + label,
			XjFamilyRealmAchievementState.Revival => "重振" + label,
			_ => "首出" + label
		};
	}

	internal static string BuildHistoryAction(in XjFamilyRealmAchievement achievement, string realmLabel)
	{
		string label = string.IsNullOrWhiteSpace(realmLabel) ? "高境" : realmLabel.Trim();
		if (!achievement.Found) return "成就" + label;
		return achievement.State switch
		{
			XjFamilyRealmAchievementState.Again => achievement.FamilyName + "再添" + label,
			XjFamilyRealmAchievementState.Revival => achievement.FamilyName + "重振" + label,
			_ => achievement.FamilyName + "首出" + label
		};
	}

	private static string BuildFirstEnding(string realmLabel)
	{
		return realmLabel switch
		{
			"紫府" => "，这是族中首位紫府，自此称制紫府仙族。",
			"真人" => "，这是族中首位真人，自此跻身真人世家。",
			"金丹" => "，这是族中首位金丹，自此跻身金丹世家。",
			"真君" => "，这是族中首位真君，自此跻身真君世家。",
			"道胎" => "，这是族中首位道胎，自此家门跻身天下绝顶高门。",
			_ => "，这是族中首位" + realmLabel + "，自此跻身高位世家。"
		};
	}

	private static long ResolveFamilyStableId(Actor actor, long actorId)
	{
		if (actorId > 0L
			&& XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long resolved)
			&& resolved > 0L)
		{
			return resolved;
		}

		if (actor?.data == null) return 0L;
		try
		{
			BaseSystemData data = (BaseSystemData)actor.data;
			data.get(XjFamilyIdentity.FamilyStableId, out long mirrored, 0L);
			return mirrored > 0L ? mirrored : 0L;
		}
		catch
		{
			return 0L;
		}
	}

	private static string ResolveFallbackFamilyName(Actor actor)
	{
		try
		{
			string nativeName = actor?.clan?.data == null
				? string.Empty
				: ((BaseSystemData)actor.clan.data).name;
			if (IsUsableFamilyName(nativeName)) return nativeName.Trim();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjFamilyRealmAchievementNarrative.ResolveFallbackFamilyName", ex);
		}

		return ResolveFallbackFamilyName(actor?.getName());
	}

	private static string ResolveFallbackFamilyName(string actorName)
	{
		string resolved = XjFamilyDisplayNameResolver.FromActorName(actorName ?? string.Empty);
		return string.IsNullOrWhiteSpace(resolved) ? "未名氏" : resolved;
	}

	private static bool IsUsableFamilyName(string value)
	{
		string name = (value ?? string.Empty).Trim();
		return name.Length > 0
			&& !string.Equals(name, "筑基世家", StringComparison.Ordinal)
			&& !string.Equals(name, "紫府世家", StringComparison.Ordinal)
			&& !string.Equals(name, "金丹世家", StringComparison.Ordinal)
			&& !string.Equals(name, "真人世家", StringComparison.Ordinal)
			&& !string.Equals(name, "真君世家", StringComparison.Ordinal);
	}
}
