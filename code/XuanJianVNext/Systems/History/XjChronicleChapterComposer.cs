using System;
using System.Collections.Generic;
using System.Text;
using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History;

/// <summary>
/// 把同年已真实发生的高重要度事件整理成“章”，不再随机制造第二套剧情。
/// 章回只做叙事编排：优先聚合同宗、同族或同一世界主题的事件，并逐条引用真实标题。
/// 每年最多一章，避免史册膨胀和公告噪音。
/// </summary>
internal static class XjChronicleChapterComposer
{
	private const int MaximumEvidence = 128;
	private static readonly List<XjWorldHistoryArchiveRecord> CandidateBuffer = new List<XjWorldHistoryArchiveRecord>(32);
	private static readonly List<XjWorldHistoryArchiveRecord> SelectedBuffer = new List<XjWorldHistoryArchiveRecord>(4);
	private static int _lastComposedYear;

	internal static void Clear()
	{
		_lastComposedYear = 0;
		CandidateBuffer.Clear();
		SelectedBuffer.Clear();
	}

	internal static void TryComposeYear(int currentYear)
	{
		if (currentYear <= 0 || _lastComposedYear == currentYear) return;
		IReadOnlyList<XjWorldHistoryArchiveRecord> records = XjWorldHistoryStore.ReadTailForYear(currentYear, MaximumEvidence);
		CandidateBuffer.Clear();
		for (int i = 0; i < records.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = records[i];
			if (item == null || item.Year != currentYear || item.Importance < 3) continue;
			if (!string.IsNullOrWhiteSpace(item.EventType) && item.EventType.StartsWith("AutoChapter:", StringComparison.Ordinal))
			{
				_lastComposedYear = currentYear;
				return;
			}
			CandidateBuffer.Add(item);
		}
		if (CandidateBuffer.Count < 2) return;

		long selectedSectId = ResolveBestSharedId(useSect: true);
		long selectedFamilyId = selectedSectId > 0L ? 0L : ResolveBestSharedId(useSect: false);
		SelectedBuffer.Clear();
		for (int i = 0; i < CandidateBuffer.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = CandidateBuffer[i];
			if (selectedSectId > 0L && item.SectId != selectedSectId && item.RelatedSectId != selectedSectId) continue;
			if (selectedFamilyId > 0L && item.FamilyId != selectedFamilyId && item.RelatedFamilyId != selectedFamilyId) continue;
			SelectedBuffer.Add(item);
		}
		if (SelectedBuffer.Count < 2)
		{
			SelectedBuffer.Clear();
			for (int i = 0; i < CandidateBuffer.Count; i++) SelectedBuffer.Add(CandidateBuffer[i]);
		}

		SelectedBuffer.Sort(CompareEvidence);
		if (SelectedBuffer.Count > 4) SelectedBuffer.RemoveRange(4, SelectedBuffer.Count - 4);
		SelectedBuffer.Sort((left, right) => left.EventId.CompareTo(right.EventId));

		string archetype = ResolveArchetype(SelectedBuffer);
		string chapterName = ResolveChapterName(archetype);
		StringBuilder body = new StringBuilder(384);
		body.Append("是岁，");
		for (int i = 0; i < SelectedBuffer.Count; i++)
		{
			if (i > 0) body.Append(i == SelectedBuffer.Count - 1 ? "；其后，" : "；继而，");
			AppendEvidence(body, SelectedBuffer[i]);
		}
		body.Append("。此章只据当年已录之事实合编，不另增因果。");

		int importance = 3;
		for (int i = 0; i < SelectedBuffer.Count; i++) importance = Math.Max(importance, SelectedBuffer[i].Importance);
		XjWorldHistoryArchiveRecord cause = SelectedBuffer[0];
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World,
			"《" + XjChronology.FormatYear(currentYear) + "·" + chapterName + "》",
			body.ToString(),
			Math.Min(5, importance),
			isProtected: importance >= 4,
			sectId: selectedSectId,
			familyId: selectedFamilyId,
			cityId: cause.CityId,
			year: currentYear,
			eventType: "AutoChapter:" + archetype,
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate
				| (selectedSectId > 0L ? XjHistoryVisibility.Sect : 0)
				| (selectedFamilyId > 0L ? XjHistoryVisibility.Family : 0)),
			causeEventId: cause.EventId,
			mirrorToWorldLog: false);
		_lastComposedYear = currentYear;
	}

	private static long ResolveBestSharedId(bool useSect)
	{
		Dictionary<long, int> scores = new Dictionary<long, int>();
		for (int i = 0; i < CandidateBuffer.Count; i++)
		{
			XjWorldHistoryArchiveRecord item = CandidateBuffer[i];
			long id = useSect ? item.SectId : item.FamilyId;
			if (id <= 0L) continue;
			scores.TryGetValue(id, out int score);
			scores[id] = score + Math.Max(1, item.Importance);
		}
		long bestId = 0L;
		int bestScore = 0;
		foreach (KeyValuePair<long, int> pair in scores)
		{
			if (pair.Value > bestScore || pair.Value == bestScore && pair.Key < bestId)
			{
				bestId = pair.Key;
				bestScore = pair.Value;
			}
		}
		return bestScore >= 6 ? bestId : 0L;
	}

	private static int CompareEvidence(XjWorldHistoryArchiveRecord left, XjWorldHistoryArchiveRecord right)
	{
		int cmp = right.Importance.CompareTo(left.Importance);
		return cmp != 0 ? cmp : right.EventId.CompareTo(left.EventId);
	}

	private static void AppendEvidence(StringBuilder builder, XjWorldHistoryArchiveRecord item)
	{
		string actor = string.IsNullOrWhiteSpace(item.ActorName) ? string.Empty : item.ActorName.Trim();
		string title = string.IsNullOrWhiteSpace(item.Title) ? item.Body : item.Title;
		if (!string.IsNullOrWhiteSpace(actor) && !title.Contains(actor, StringComparison.Ordinal)) builder.Append(actor).Append(' ');
		builder.Append(string.IsNullOrWhiteSpace(title) ? "有事见于仙鉴" : title.Trim());
	}

	private static string ResolveArchetype(IReadOnlyList<XjWorldHistoryArchiveRecord> items)
	{
		string text = string.Empty;
		for (int i = 0; i < items.Count; i++) text += " " + items[i].EventType + " " + items[i].Title + " " + items[i].Body;
		if (HasAny(text, "帝明阳", "仙国", "明阳", "复帝", "帝统")) return "XianGuo";
		if (HasAny(text, "阴司", "鬼", "幽冥", "阴世")) return "YinSi";
		if (HasAny(text, "大比", "论道", "试法", "讲法", "讲道")) return "Tournament";
		if (HasAny(text, "洞天", "福地", "秘境", "奇遇")) return "DongTian";
		if (HasAny(text, "果位", "余位", "闰位", "权柄", "证金", "金丹", "真君", "道胎")) return "Position";
		if (HasAny(text, "宣战", "兵劫", "破阵", "灭宗", "攻城", "杀")) return "War";
		if (HasAny(text, "传承", "功法", "求金法", "补撰", "换法", "道统")) return "Inheritance";
		if (HasAny(text, "返祖", "祖血", "家族", "族人")) return "Family";
		if (HasAny(text, "开宗", "宗门", "山门", "宗主", "峰主")) return "Sect";
		return "World";
	}

	private static string ResolveChapterName(string archetype)
	{
		return archetype switch
		{
			"XianGuo" => "明阳临国",
			"YinSi" => "幽明相涉",
			"Tournament" => "诸峰论道",
			"DongTian" => "洞天开合",
			"Position" => "位序有移",
			"War" => "兵锋起处",
			"Inheritance" => "法脉续断",
			"Family" => "族脉复兴",
			"Sect" => "山门兴替",
			_ => "岁事相因"
		};
	}

	private static bool HasAny(string text, params string[] needles)
	{
		for (int i = 0; i < needles.Length; i++) if (text.IndexOf(needles[i], StringComparison.Ordinal) >= 0) return true;
		return false;
	}
}
