using System.Collections.Generic;

namespace XuanJianVNext.Data.DongTian;

internal static class XjEventDongTianState
{
	internal const string ClueGathering = "ClueGathering";
	internal const string Resolved = "Resolved";
}

internal sealed class XjEventDongTianArchiveRecord
{
	public string RecordId { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string State { get; set; } = XjEventDongTianState.ClueGathering;
	public int CreatedYear { get; set; }
	public int ResolveYear { get; set; }
	public int ResolvedYear { get; set; }
	public long UpperActorId { get; set; }
	public string UpperActorName { get; set; } = string.Empty;
	public string UpperRealmId { get; set; } = string.Empty;
	public long AnchorCityId { get; set; }
	public string AnchorCityName { get; set; } = string.Empty;
	public List<long> LowerActorIds { get; set; } = new List<long>();
	public List<string> LowerActorNames { get; set; } = new List<string>();
	public bool UpperLowerLinkSucceeded { get; set; }
	public string RewardType { get; set; } = string.Empty;
	public string OutcomeText { get; set; } = string.Empty;

	internal XjEventDongTianArchiveRecord Clone()
	{
		return new XjEventDongTianArchiveRecord
		{
			RecordId = RecordId ?? string.Empty,
			DisplayName = DisplayName ?? string.Empty,
			State = State ?? XjEventDongTianState.ClueGathering,
			CreatedYear = CreatedYear,
			ResolveYear = ResolveYear,
			ResolvedYear = ResolvedYear,
			UpperActorId = UpperActorId,
			UpperActorName = UpperActorName ?? string.Empty,
			UpperRealmId = UpperRealmId ?? string.Empty,
			AnchorCityId = AnchorCityId,
			AnchorCityName = AnchorCityName ?? string.Empty,
			LowerActorIds = LowerActorIds == null ? new List<long>() : new List<long>(LowerActorIds),
			LowerActorNames = LowerActorNames == null ? new List<string>() : new List<string>(LowerActorNames),
			UpperLowerLinkSucceeded = UpperLowerLinkSucceeded,
			RewardType = RewardType ?? string.Empty,
			OutcomeText = OutcomeText ?? string.Empty
		};
	}
}

internal sealed class XjEventDongTianWorldArchiveData
{
	public int NextSpawnYear { get; set; }
	public int LastProcessedYear { get; set; } = -1;
	public List<XjEventDongTianArchiveRecord> Records { get; set; } = new List<XjEventDongTianArchiveRecord>();

	internal XjEventDongTianWorldArchiveData Clone()
	{
		XjEventDongTianWorldArchiveData clone = new XjEventDongTianWorldArchiveData
		{
			NextSpawnYear = NextSpawnYear,
			LastProcessedYear = LastProcessedYear,
			Records = new List<XjEventDongTianArchiveRecord>()
		};
		if (Records != null)
		{
			for (int i = 0; i < Records.Count; i++)
			{
				if (Records[i] != null) clone.Records.Add(Records[i].Clone());
			}
		}
		return clone;
	}
}
