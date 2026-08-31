using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
	internal static void RecordSectProsperityChanged(long sectId, string sectName, int year, int previous, int current, string tier, string summary)
	{
		if (sectId <= 0L || year <= 0) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		bool decline = current < previous;
		string body = resolvedSectName + "五年兴衰结算由" + previous + "变为" + current + "，现处“" + SafeName(tier, "未评") + "”。"
			+ SafeName(summary, string.Empty);
		RecordSect(sectId, resolvedSectName, year, "SectProsperityChanged",
			"sect|prosperity|" + sectId + "|" + year,
			decline ? "山门转衰" : "山门转盛",
			decline ? "兴衰有损" : "声势日隆",
			body, decline ? 3 : 2, false,
			result: decline ? XjHistoryResult.Failure : XjHistoryResult.Success);
	}

	internal static void RecordSectDeclineExtinct(long sectId, string sectName, int year, string reason)
	{
		if (sectId <= 0L) return;
		string resolvedSectName = ResolveSectName(sectId, sectName);
		RecordSect(sectId, resolvedSectName, year, XjThreeBookEventTypes.SectExtinct,
			"sect|decline_extinct|" + sectId,
			"宗门覆灭", "兴衰耗尽",
			resolvedSectName + SafeName(reason, "门人凋零，宗脉无继") + "。山门并非失去城池而亡，而是已无人足以承起法统，遂从天下宗谱除名。",
			5, true, result: XjHistoryResult.Failure);
	}
}
