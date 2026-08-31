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
			"突破", "晋升", "黄冠", "紫府", "真人", "金丹", "真君", "羽士", "结璘", "神丹", "空证",
			"道胎", "世尊", "摩诃", "法相", "真灵", "转世", "归返", "归墟",
			"求证", "求金", "阻道", "瓶颈", "果位", "正位", "余位", "闰位", "神妙圆满",
			"开宗", "宗门", "山门", "宗主", "峰主", "宣战", "灭宗", "衰亡", "大比", "讲法", "讲道",
			"家族", "族议", "扶持", "上修", "人丹", "明阳局", "帝明阳", "改朝", "承统", "谋逆", "棋局", "徒作嫁衣",
			"洞天", "福地", "秘境", "机缘", "灵物", "金性", "金地", "三十二天", "传承", "功法", "采气法", "求金法",
			"延寿丹", "灵宝", "法宝", "仙器", "陨落", "坐化", "身陨", "寿尽", "阴司", "龙王", "龙君", "出海",
			"恩怨", "血仇", "寻仇", "斗法", "关系", "亲密", "友好", "敌对", "天下", "纪元");
	}

	private static int ResolveImportance(string category, string text)
	{
		if (HasAny(text,
			"成就金丹", "证得金丹", "服气真君", "空证", "神丹", "道胎", "世尊", "灭宗", "宗门衰亡", "人丹", "阻道", "阴司", "真灵俱灭", "龙王陨落",
			"帝明阳", "改朝换代", "帝统复归", "权柄之争", "正位承继", "夺取正位", "奉本道正统", "道正", "权柄融成", "正式失柄")) return 5;
		if (HasAny(text,
			"晋升紫府", "服气真人", "摩诃", "法相", "转世", "真灵归返", "龙君", "出海", "归墟", "开宗", "洞天现世", "宗门宣战", "高境陨落",
			"位序扩充", "余位开辟", "闰位开辟", "神通易象", "神通退显", "神通随柄",
			"权柄裂解", "权柄归还", "权柄易位", "权柄归身", "权柄离身")) return 4;
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
