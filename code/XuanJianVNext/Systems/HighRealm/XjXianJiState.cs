using System;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjXianJiState
{
	internal const int MaxCount = 5;

	internal static XjXianJiState Empty { get; } = new XjXianJiState(
		false,
		0,
		Array.Empty<string>(),
		0,
		"Empty");

	internal readonly bool Found;
	internal readonly int Count;
	internal readonly string[] Ids;
	internal readonly int LastYear;
	internal readonly string ReasonCode;

	internal XjXianJiState(
		bool found,
		int count,
		string[] ids,
		int lastYear,
		string reasonCode)
	{
		Found = found;
		Count = Math.Max(0, Math.Min(MaxCount, count));
		Ids = ids ?? Array.Empty<string>();
		LastYear = Math.Max(0, lastYear);
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal enum XjXianJiPoolKind
{
	Native,
	Lower,
	Adjacent,
	Other
}
