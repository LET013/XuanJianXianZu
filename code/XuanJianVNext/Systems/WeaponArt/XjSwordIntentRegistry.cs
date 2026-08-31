using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Codex;

namespace XuanJianVNext.Systems.WeaponArt;

/// <summary>
/// 天下剑意的事件驱动注册表。只有新剑意诞生时写入，不进行年度全世界扫描。
/// </summary>
internal static class XjSwordIntentRegistry
{
	private static readonly Dictionary<string, XjSwordIntentArchiveRecord> ById =
		new Dictionary<string, XjSwordIntentArchiveRecord>(StringComparer.Ordinal);
	private static readonly Dictionary<string, string> IdByName =
		new Dictionary<string, string>(StringComparer.Ordinal);
	private static XjSwordIntentArchiveRecord[] Snapshot = Array.Empty<XjSwordIntentArchiveRecord>();
	private static bool SnapshotDirty = true;

	internal static int Count => ById.Count;

	internal static bool Register(Actor actor, int currentYear, string intentName)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(intentName)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		string id = BuildIntentId(actorId);
		string name = intentName.Trim();
		if (IsNameClaimedByOther(actorId, name)) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string displayRealm = XjRealmHelper.GetDisplayName(realmId);
		if (string.IsNullOrWhiteSpace(displayRealm)) displayRealm = "未入境";
		string path = XjCultivationPathRules.IsFuQiYangXing(actor) ? "服气养性" : "紫府金丹道";
		string type = ResolveIntentType(actorId, name);
		XjSwordIntentArchiveRecord record = new XjSwordIntentArchiveRecord
		{
			IntentId = id,
			IntentName = name,
			CreatorActorId = actorId,
			CreatorName = ResolveCreatorName(actor),
			CreatedYear = Math.Max(0, currentYear),
			CreatorPath = path,
			CreatorRealm = displayRealm,
			IntentType = type,
			Description = BuildDescription(type)
		};

		if (ById.TryGetValue(id, out XjSwordIntentArchiveRecord previous)
			&& previous != null
			&& string.Equals(previous.IntentName, name, StringComparison.Ordinal))
		{
			return false;
		}
		if (previous != null)
		{
			IdByName.Remove(NormalizeCollisionKey(previous.IntentName));
		}
		ById[id] = record;
		IdByName[NormalizeCollisionKey(name)] = id;
		MarkChanged();
		return true;
	}

	internal static bool TryGet(string intentId, out XjSwordIntentArchiveRecord record)
	{
		record = null;
		if (string.IsNullOrWhiteSpace(intentId)
			|| !ById.TryGetValue(intentId.Trim(), out XjSwordIntentArchiveRecord stored)
			|| stored == null)
		{
			return false;
		}
		record = stored.Clone();
		return true;
	}

	internal static bool TrySelectUnstudied(Actor actor, ISet<string> studiedIds, out XjSwordIntentArchiveRecord selected)
	{
		selected = null;
		if (actor?.data == null || ById.Count == 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		IReadOnlyList<XjSwordIntentArchiveRecord> snapshot = ReadSnapshot();
		List<XjSwordIntentArchiveRecord> candidates = new List<XjSwordIntentArchiveRecord>(snapshot.Count);
		for (int i = 0; i < snapshot.Count; i++)
		{
			XjSwordIntentArchiveRecord item = snapshot[i];
			if (item == null || item.CreatorActorId == actorId
				|| studiedIds != null && studiedIds.Contains(item.IntentId)) continue;
			candidates.Add(item);
		}
		if (candidates.Count == 0) return false;
		int studiedCount = studiedIds?.Count ?? 0;
		int start = XjDeterministicHash.PositiveIndex(
			actorId + studiedCount,
			"fuqi_sword_intent_study_v1",
			candidates.Count);
		selected = candidates[start].Clone();
		return true;
	}

	internal static IReadOnlyList<XjSwordIntentArchiveRecord> ReadSnapshot()
	{
		if (!SnapshotDirty) return Snapshot;
		List<XjSwordIntentArchiveRecord> records = new List<XjSwordIntentArchiveRecord>(ById.Count);
		foreach (XjSwordIntentArchiveRecord record in ById.Values)
		{
			if (record != null) records.Add(record.Clone());
		}
		records.Sort((left, right) =>
		{
			int year = left.CreatedYear.CompareTo(right.CreatedYear);
			if (year != 0) return year;
			return string.Compare(left.IntentId, right.IntentId, StringComparison.Ordinal);
		});
		Snapshot = records.ToArray();
		SnapshotDirty = false;
		return Snapshot;
	}

	internal static void ReleaseSnapshotCache()
	{
		Snapshot = Array.Empty<XjSwordIntentArchiveRecord>();
		SnapshotDirty = ById.Count > 0;
	}

	internal static bool IsNameClaimedByOther(long actorId, string alias)
	{
		string key = NormalizeCollisionKey(alias);
		if (key.Length == 0 || !IdByName.TryGetValue(key, out string id)) return false;
		return !string.Equals(id, BuildIntentId(actorId), StringComparison.Ordinal);
	}

	internal static bool IsCreatorAlive(XjSwordIntentArchiveRecord record)
	{
		return record != null && record.CreatorActorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(record.CreatorActorId, out Actor actor)
			&& actor?.data != null && actor.isAlive();
	}

	internal static void ExportArchiveRecords(List<XjSwordIntentArchiveRecord> target)
	{
		if (target == null) return;
		target.Clear();
		IReadOnlyList<XjSwordIntentArchiveRecord> snapshot = ReadSnapshot();
		for (int i = 0; i < snapshot.Count; i++) target.Add(snapshot[i].Clone());
	}

	internal static void ImportArchiveRecords(IReadOnlyList<XjSwordIntentArchiveRecord> source)
	{
		Clear();
		if (source == null) return;
		for (int i = 0; i < source.Count; i++)
		{
			XjSwordIntentArchiveRecord record = source[i];
			if (record == null || string.IsNullOrWhiteSpace(record.IntentId)
				|| string.IsNullOrWhiteSpace(record.IntentName)) continue;
			XjSwordIntentArchiveRecord copy = record.Clone();
			ById[copy.IntentId] = copy;
			IdByName[NormalizeCollisionKey(copy.IntentName)] = copy.IntentId;
		}
		SnapshotDirty = true;
	}

	internal static void Clear()
	{
		ById.Clear();
		IdByName.Clear();
		Snapshot = Array.Empty<XjSwordIntentArchiveRecord>();
		SnapshotDirty = false;
	}

	private static string BuildIntentId(long actorId)
	{
		return "sword_intent_" + actorId.ToString(CultureInfo.InvariantCulture);
	}

	private static string NormalizeCollisionKey(string value)
	{
		string normalized = (value ?? string.Empty).Trim().Replace(" ", string.Empty);
		if (normalized.EndsWith("剑仙", StringComparison.Ordinal))
		{
			normalized = normalized.Substring(0, normalized.Length - 2) + "剑";
		}
		return normalized;
	}

	private static string ResolveCreatorName(Actor actor)
	{
		string name = actor?.getName() ?? string.Empty;
		return string.IsNullOrWhiteSpace(name) ? "无名剑修" : name.Trim();
	}

	internal static string ResolveIntentType(long actorId, string intentName)
	{
		string[] types = { "断法", "疾锋", "镇守", "藏锋", "绝命", "破军" };
		int index = XjDeterministicHash.PositiveIndex(actorId, "sword_intent_type|" + intentName, types.Length);
		return types[index];
	}

	private static string BuildDescription(string type)
	{
		return type switch
		{
			"断法" => "锋意专于破法斩护，以一点锐意截断对手气机。",
			"疾锋" => "剑出如流光，重在先机、追击与一线破敌。",
			"镇守" => "剑意沉稳如岳，护身守土，擅于迎击反制。",
			"藏锋" => "锋芒内敛，蓄势于无声，出剑之时方见真章。",
			"绝命" => "以险境炼心，越近生死，剑意越发决绝。",
			"破军" => "剑势开阖宏大，擅破阵列与群敌。",
			_ => "一己所悟之剑意，自成一家，不与旁人雷同。"
		};
	}

	private static void MarkChanged()
	{
		SnapshotDirty = true;
		XjWorldArchiveSystem.MarkChanged();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.SwordIntent | XjCodexDirtyFlags.History);
	}
}
