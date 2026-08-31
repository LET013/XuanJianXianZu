using System;
using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History;

/// <summary>
/// 统一天下纪事与三类史册的收录边界。新增的洞天、大比、器意等人物事件优先保留；
/// 四艺日常生产与已删除公告统一剔除，仅保留延寿丹、紫府灵宝与金丹法宝。
/// </summary>
internal static class XjHistoryRetentionPolicy
{
	internal static bool ShouldKeepWorldRecord(string category, string eventType, string title, string body)
	{
		if (IsForbiddenAdministrativeRecord(eventType, title, body)) return false;
		if (IsExplicitNarrativeEvent(eventType, title, body)) return true;
		if (IsSuppressedLegacyOrRoutineRecord(category, eventType, title, body)) return false;
		if (!IsCraftRecord(category, eventType, title, body)) return true;
		return IsRetainedCraftMilestone(eventType, title, body);
	}

	internal static bool ShouldKeepChronicleRecord(string eventType, string title, string body, string source)
	{
		if (IsForbiddenAdministrativeRecord(eventType, title, body)) return false;
		if (IsExplicitNarrativeEvent(eventType, title, body)) return true;
		if (IsSuppressedLegacyOrRoutineRecord(string.Empty, eventType, title, body)) return false;
		if (!IsCraftChronicle(eventType, title, body, source)) return true;
		return IsRetainedCraftMilestone(eventType, title, body);
	}

	internal static bool IsRetainedCraftMilestone(string eventType, string title, string body)
	{
		string text = Join(eventType, title, body);
		return HasAny(text,
			"九曜延寿丹", "延寿丹",
			"紫府灵宝", "金丹法宝")
			|| HasAny(eventType, "LongevityPill", "ZiFuLingBao", "JinDanFaBao");
	}

	internal static void NormalizeRecordScope(XjWorldHistoryArchiveRecord record)
	{
		if (record == null) return;
		string type = (record.EventType ?? string.Empty).Trim();
		string text = Join(type, record.Title, record.Body);
		bool invalidIdentity = false;

		if (record.ActorId > 0L && !XjHistoryNamePolicy.IsChineseActorName(record.ActorName))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.ActorName);
			record.ActorId = 0L;
			record.ActorName = string.Empty;
		}
		if (record.RelatedActorId > 0L && !XjHistoryNamePolicy.IsChineseActorName(record.RelatedActorName))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.RelatedActorName);
			record.RelatedActorId = 0L;
			record.RelatedActorName = string.Empty;
		}
		if (record.FamilyId > 0L && !XjHistoryNamePolicy.IsChineseFamilyName(record.FamilyNameSnapshot))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.FamilyNameSnapshot);
			record.FamilyId = 0L;
			record.FamilyNameSnapshot = string.Empty;
		}
		if (record.RelatedFamilyId > 0L && !XjHistoryNamePolicy.IsChineseFamilyName(record.RelatedFamilyNameSnapshot))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.RelatedFamilyNameSnapshot);
			record.RelatedFamilyId = 0L;
			record.RelatedFamilyNameSnapshot = string.Empty;
		}
		if (record.SectId > 0L && !XjHistoryNamePolicy.IsChineseSectName(record.SectNameSnapshot))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.SectNameSnapshot);
			record.SectId = 0L;
			record.SectNameSnapshot = string.Empty;
		}
		if (record.RelatedSectId > 0L && !XjHistoryNamePolicy.IsChineseSectName(record.RelatedSectNameSnapshot))
		{
			invalidIdentity |= !string.IsNullOrWhiteSpace(record.RelatedSectNameSnapshot);
			record.RelatedSectId = 0L;
			record.RelatedSectNameSnapshot = string.Empty;
		}

		if (invalidIdentity) record.EventType = "SuppressedNonChineseName:" + type;

		record.VisibilityFlags &= ~(int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect);
		if (record.ActorId > 0L || record.RelatedActorId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Personal;
		if (record.FamilyId > 0L || record.RelatedFamilyId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Family;
		if (record.SectId > 0L || record.RelatedSectId > 0L) record.VisibilityFlags |= (int)XjHistoryVisibility.Sect;

		if (!IsTerritoryAdministrationRecord(text)) return;

		// 洞天显世、属地主张落定与关闭属于天下/宗门/家族层面的疆域事件。
		// 发现时的锚点人物并不是事件主角，旧档中的人物锚点统一移除。
		record.ActorId = 0L;
		record.ActorName = string.Empty;
		record.RelatedActorId = 0L;
		record.RelatedActorName = string.Empty;

		bool unownedDiscovery = string.Equals(type, "AdventureRealmTerritoryDiscovered", StringComparison.OrdinalIgnoreCase)
			|| HasAny(text, "现于无主之地", "进入属地主张确认期");
		if (unownedDiscovery)
		{
			record.FamilyId = 0L;
			record.FamilyNameSnapshot = string.Empty;
			record.RelatedFamilyId = 0L;
			record.RelatedFamilyNameSnapshot = string.Empty;
		}

		XjHistoryVisibility visibility = XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate;
		if (record.SectId > 0L || record.RelatedSectId > 0L) visibility |= XjHistoryVisibility.Sect;
		if (!unownedDiscovery && (record.FamilyId > 0L || record.RelatedFamilyId > 0L)) visibility |= XjHistoryVisibility.Family;
		record.VisibilityFlags = (int)visibility;
	}

	private static bool IsForbiddenAdministrativeRecord(string eventType, string title, string body)
	{
		string type = (eventType ?? string.Empty).Trim();
		string text = Join(title, body);
		return type.StartsWith("SuppressedNonChineseName:", StringComparison.OrdinalIgnoreCase)
			|| XjHistoryNamePolicy.HasInternalNameMarker(text)
			|| XjHistoryNamePolicy.ContainsLatinLetter(text)
			|| type.StartsWith("FamilyClanLeader", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyLeader", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyPatriarch", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("FamilyRepresentative", StringComparison.OrdinalIgnoreCase)
			|| HasAny(text, "家主更替", "接过家主印信", "本卷家族代表", "家族代表");
	}

	private static bool IsExplicitNarrativeEvent(string eventType, string title, string body)
	{
		string type = (eventType ?? string.Empty).Trim();
		string text = Join(title, body);
		return type.StartsWith("DongTian", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("AdventureRealm", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("SecretRealmTournament", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("WeaponArt", StringComparison.OrdinalIgnoreCase)
			|| HasAny(text, "宗门大比", "洞天生还", "洞天陨落", "悟得剑意", "悟得刀意", "悟得枪意", "悟得弓意");
	}

	private static bool IsSuppressedLegacyOrRoutineRecord(string category, string eventType, string title, string body)
	{
		if (IsRetainedCraftMilestone(eventType, title, body)) return false;
		string type = (eventType ?? string.Empty).Trim();
		string text = Join(title, body);
		// 旧档曾把扶金的每一步过程都写成显眼历史。机制状态仍由角色字段
		// 保存；“定策”是玩家必须知晓的布局转折，只有补基、演法等过程隐藏。
		if (type.StartsWith("UpperGoldSupport:", StringComparison.OrdinalIgnoreCase)
			&& HasAny(title, "补基授业", "演法定路")) return true;
		// 古释清静点化只保留真实命数/宏愿/金地结果与人物详情累计，不进入玄鉴史册。
		// 同时按旧文案特征过滤既有存档记录，使规则升级后旧档也不会继续展示这类日常点拨。
		if (HasAny(text, "古释点命", "清静照命", "垂清静意照其命痕", "垂一念清静法意照命")) return true;
		if (type.StartsWith("Alchemy", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Talisman", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Formation", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("CraftTask", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Herb", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Medicine", StringComparison.OrdinalIgnoreCase)) return true;
		return HasAny(text,
			"完成一批护身符", "制成护身符", "完成一批神行符", "制成神行符",
			"完成一批破阵符", "制成破阵符", "完成一批破障符", "制成破障符",
			"完成一批镇神符", "制成镇神符", "领悟丹方", "丹方入库",
			"炼成丹药", "炼丹炸炉", "寻得上品药材", "寻得下品药材",
			"筑基法器出世", "筑基法器升阶", "家族阵法建成",
			"宗门大阵建成", "宗门大阵维修", "阵法维修", "四艺任务",
			"炼丹任务", "炼器任务", "符箓任务", "阵法任务");
	}

	private static bool IsCraftRecord(string category, string eventType, string title, string body)
	{
		if (string.Equals(category, XjWorldHistoryCategory.Craft, StringComparison.Ordinal)) return true;
		string type = (eventType ?? string.Empty).Trim();
		return type.StartsWith("Alchemy", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Talisman", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Formation", StringComparison.OrdinalIgnoreCase)
			|| type.StartsWith("Craft", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(type, "FaBaoObtained", StringComparison.Ordinal)
			|| string.Equals(type, "FaBaoUpgraded", StringComparison.Ordinal)
			|| HasAny(Join(title, body), "筑基法器", "紫府灵宝", "金丹法宝");
	}

	private static bool IsCraftChronicle(string eventType, string title, string body, string source)
	{
		string type = (eventType ?? string.Empty).Trim();
		string origin = (source ?? string.Empty).Trim();
		return IsCraftRecord(string.Empty, type, title, body)
			|| origin.StartsWith("alchemy.", StringComparison.OrdinalIgnoreCase)
			|| origin.StartsWith("talisman.", StringComparison.OrdinalIgnoreCase)
			|| origin.StartsWith("formation.", StringComparison.OrdinalIgnoreCase)
			|| origin.StartsWith("craft.", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsTerritoryAdministrationRecord(string text)
	{
		return HasAny(text,
			"AdventureRealmTerritoryDiscovered", "AdventureRealmClaimResolved", "AdventureRealmClosed",
			"属地主张确认期", "属地主张落定",
			"现于无主之地", "结束开放并退出运行索引", "洞门闭合，残余灵机渐散");
	}

	private static string Join(params string[] values)
	{
		return values == null ? string.Empty : string.Join(" ", values);
	}

	private static bool HasAny(string text, params string[] values)
	{
		if (string.IsNullOrWhiteSpace(text) || values == null) return false;
		for (int i = 0; i < values.Length; i++)
		{
			string value = values[i];
			if (!string.IsNullOrWhiteSpace(value)
				&& text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}
}
