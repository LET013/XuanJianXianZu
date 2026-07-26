using System;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Broadcast;

/// <summary>
/// 公告与天下纪事的最后一道对齐门禁。事件生产者仍负责写入结构化历史；
/// 公告真正显示时这里只补齐同年记录的天下可见性，或为漏接历史的低频事件补一条底本。
/// UI反馈、普通四艺和高频提示不会进入史册。
/// </summary>
internal static class XjAnnouncementHistoryAlignment
{
	internal static void OnAnnouncementShown(string text, string iconId = null)
	{
		string value = (text ?? string.Empty).Trim();
		if (!ShouldMirror(value)) return;
		if (XjWorldHistoryStore.EnsureWorldVisibleForAnnouncement(value, iconId)) return;

		string category = XjWorldHistoryStore.ResolveEventCategory(iconId, value, XjWorldHistoryCategory.World);
		string eventType = "AnnouncementMirror:" + category;
		if (!XjHistoryRetentionPolicy.ShouldKeepWorldRecord(category, eventType, value, value)) return;
		XjWorldHistoryStore.RecordDomainEvent(
			category,
			BuildTitle(category),
			value,
			ResolveImportance(category, value),
			eventType: eventType,
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			iconIdOverride: iconId,
			mirrorToWorldLog: false);
	}

	private static bool ShouldMirror(string text)
	{
		if (string.IsNullOrWhiteSpace(text)
			|| XjHistoryNamePolicy.HasInternalNameMarker(text)
			|| XjHistoryNamePolicy.ContainsLatinLetter(text)) return false;
		if (HasAny(text,
			"请选择", "请先", "无法", "未找到", "已收纳", "已加入", "重新照览",
			"定位失败", "打开失败", "刷新完成", "界面", "调试", "FPS")) return false;
		if (HasAny(text,
			"炼成丹药", "炼丹炸炉", "完成一批", "制成护身符", "制成神行符",
			"制成破阵符", "制成破障符", "制成镇神符", "寻得上品药材", "寻得下品药材",
			"阵法维修", "宗门大阵维修", "四艺任务")) return false;

		return HasAny(text,
			"突破", "晋升", "紫府", "金丹", "结璘", "神丹", "求金", "瓶颈",
			"开宗", "宗门", "山门", "宗主", "峰主", "宣战", "灭宗", "大比", "讲法", "讲道",
			"家族", "族议", "扶持", "上修", "人丹", "棋局", "徒作嫁衣",
			"洞天", "福地", "秘境", "机缘", "灵物", "金性", "传承", "功法", "采气法", "求金法",
			"延寿丹", "灵宝", "法宝", "陨落", "坐化", "身陨", "寿尽", "阴司", "龙王",
			"恩怨", "血仇", "寻仇", "斗法", "关系", "亲密", "友好", "敌对", "天下", "纪元");
	}

	private static int ResolveImportance(string category, string text)
	{
		if (HasAny(text, "成就金丹", "证得金丹", "神丹", "灭宗", "人丹", "阴司", "龙王陨落")) return 5;
		if (HasAny(text, "晋升紫府", "开宗", "洞天现世", "宗门宣战", "高境陨落")) return 4;
		if (string.Equals(category, XjWorldHistoryCategory.World, StringComparison.Ordinal)) return 3;
		return 2;
	}

	private static string BuildTitle(string category)
	{
		return string.IsNullOrWhiteSpace(category) ? "天下纪事" : category + "纪事";
	}

	private static bool HasAny(string text, params string[] values)
	{
		if (string.IsNullOrWhiteSpace(text) || values == null) return false;
		for (int i = 0; i < values.Length; i++)
		{
			string value = values[i];
			if (!string.IsNullOrWhiteSpace(value)
				&& text.IndexOf(value, StringComparison.Ordinal) >= 0) return true;
		}
		return false;
	}
}
