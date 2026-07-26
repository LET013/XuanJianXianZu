using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjGuoWeiRegistryEntry
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly string FamilyName;
	internal readonly string DaoTu;
	internal readonly string JinXing;
	internal readonly string GuoWei;
	internal readonly int Year;
	internal readonly string LifecycleStatus;
	internal readonly int EndedYear;
	internal readonly string EndReason;

	internal bool IsActive => string.Equals(LifecycleStatus, XjGuoWeiRegistry.StatusActive, StringComparison.Ordinal)
		&& EndedYear <= 0;

	internal XjGuoWeiRegistryEntry(
		bool found,
		long actorId,
		string actorName,
		string familyName,
		string daoTu,
		string jinXing,
		string guoWei,
		int year,
		string lifecycleStatus = XjGuoWeiRegistry.StatusActive,
		int endedYear = 0,
		string endReason = "")
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		FamilyName = familyName ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		JinXing = jinXing ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		Year = year < 0 ? 0 : year;
		LifecycleStatus = string.IsNullOrWhiteSpace(lifecycleStatus) ? XjGuoWeiRegistry.StatusActive : lifecycleStatus.Trim();
		EndedYear = endedYear < 0 ? 0 : endedYear;
		EndReason = endReason ?? string.Empty;
	}
}

internal static partial class XjGuoWeiRegistry
{
	internal const string StatusActive = "Active";
	internal const string StatusDeceased = "Deceased";
	internal const string StatusReleased = "Released";
	internal const string EndReasonDeath = "Death";
	internal const string EndReasonRollback = "Rollback";
	internal const string EndReasonReassigned = "Reassigned";
	private const float ZhengWeiSchemerSuppressionMultiplier = 0.55f;
	private const float ZhengWeiSchemerInterferenceChance = 0.40f;
	private static readonly HashSet<string> YinSiHiddenDaoTus = new HashSet<string>(StringComparer.Ordinal)
	{
		"谪炁",
		"下仪"
	};
	private static readonly string[] ZhengWeiSchemerTraitIds =
	{
		"evil",
		"deceitful",
		"deceit",
		"cunning",
		"sly",
		"sneaky"
	};

	// 当前果位占用表只服务运行时判定；死亡后必须释放。
	private static readonly Dictionary<string, XjGuoWeiRegistryEntry> activeEntriesByGuoWei =
		new Dictionary<string, XjGuoWeiRegistryEntry>(StringComparer.Ordinal);

	// 真君历史账本按源角色永久保留；死亡、转世、读档都不能删除。
	private static readonly Dictionary<long, XjGuoWeiRegistryEntry> historyEntriesByActorId =
		new Dictionary<long, XjGuoWeiRegistryEntry>();

	private static int revision;

	internal static int Revision => revision;

	internal static bool Register(Actor actor, string daoTu, string jinXing, string guoWei, int year)
	{
		return TryClaim(actor, daoTu, jinXing, guoWei, year);
	}

	internal static bool TryClaim(Actor actor, string daoTu, string jinXing, string guoWei, int year)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(guoWei))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !IsAvailableForActor(guoWei, actorId))
		{
			return false;
		}

		XjActorRegistry.Register(actor, out _);
		string key = NormalizeKey(guoWei);
		string normalizedDaoTu = Normalize(daoTu);
		string normalizedGuoWei = Normalize(guoWei);
		string guoWeiType = ResolveTypeFromName(normalizedGuoWei);
		if (!IsTypeAvailableForActor(guoWeiType, normalizedDaoTu, actorId, normalizedGuoWei))
		{
			return false;
		}

		bool removedOtherClaims = RemoveOtherActiveClaimsForActor(actorId, key);
		XjGuoWeiRegistryEntry entry = new XjGuoWeiRegistryEntry(
			true,
			actorId,
			actor.getName(),
			ResolveFamilyName(actor),
			normalizedDaoTu,
			Normalize(jinXing),
			normalizedGuoWei,
			year,
			StatusActive,
			0,
			string.Empty);

		bool activeChanged = !activeEntriesByGuoWei.TryGetValue(key, out XjGuoWeiRegistryEntry currentActive)
			|| !EntriesEqual(currentActive, entry);
		bool historyChanged = !historyEntriesByActorId.TryGetValue(actorId, out XjGuoWeiRegistryEntry currentHistory)
			|| !EntriesEqual(currentHistory, entry);

		if (!activeChanged && !historyChanged && !removedOtherClaims)
		{
			return true;
		}

		activeEntriesByGuoWei[key] = entry;
		historyEntriesByActorId[actorId] = entry;
		Touch(protectedCommit: true);
		return true;
	}
	/// <summary>
	/// 读档冷路径只读重建。仅恢复真君录运行时索引，不改 ActorData、
	/// 不重新分配果位，也不把一次加载修复当作新的世界事件。
	/// </summary>
	// 仅用于“境界写入失败”后的预占回滚。该角色尚未真正成就金丹，因此同时撤销刚写入的历史草稿。
	// v5及更早版本会在真君死亡时从果位表删除记录。利用仍在档案中的权柄/转世/死亡快照补回历史账本。
	internal static bool TryGetHistoricalEntry(long actorId, out XjGuoWeiRegistryEntry entry)
	{
		if (actorId > 0L && historyEntriesByActorId.TryGetValue(actorId, out entry))
		{
			return entry.Found;
		}

		entry = default;
		return false;
	}
}







