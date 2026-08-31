using System;

namespace XuanJianVNext.Systems.Codex;

/// <summary>
/// Transient exact target used when another codex page opens one of the three books.
/// It is never persisted and never participates in gameplay; the snapshot publisher
/// only uses it to seed the requested subject before the navigation frame is drawn.
/// </summary>
internal static class XjThreeBookNavigationTarget
{
	private static long _actorId;
	private static long _familyId;
	private static long _sectId;

	internal static void Set(long actorId, long familyId, long sectId)
	{
		_actorId = Math.Max(0L, actorId);
		_familyId = Math.Max(0L, familyId);
		_sectId = Math.Max(0L, sectId);
	}

	internal static long Read(int subjectType)
	{
		return subjectType == 0 ? _actorId : subjectType == 1 ? _familyId : subjectType == 2 ? _sectId : 0L;
	}

	internal static void Clear()
	{
		_actorId = 0L;
		_familyId = 0L;
		_sectId = 0L;
	}
}
