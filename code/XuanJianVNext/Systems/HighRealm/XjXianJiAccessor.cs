using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjXianJiAccessor
{
	private const char Separator = '|';
	private readonly struct CachedState
	{
		internal readonly int Count;
		internal readonly string IdsText;
		internal readonly int LastYear;
		internal readonly int RealmLimit;
		internal readonly XjXianJiState State;

		internal CachedState(int count, string idsText, int lastYear, int realmLimit, XjXianJiState state)
		{
			Count = count;
			IdsText = idsText ?? string.Empty;
			LastYear = lastYear;
			RealmLimit = realmLimit;
			State = state;
		}
	}

	private static readonly System.Collections.Generic.Dictionary<long, CachedState> StateCache = new System.Collections.Generic.Dictionary<long, CachedState>();

	internal static XjXianJiState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjXianJiState(false, 0, Array.Empty<string>(), 0, "ActorInvalid");
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int count);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiLastYear, out int lastYear);
		idsText = idsText ?? string.Empty;
		long actorId = GetActorId(actor);
		int realmLimit = ResolveRealmLimit(actor);
		if (actorId > 0L
			&& StateCache.TryGetValue(actorId, out CachedState cached)
			&& cached.Count == count
			&& cached.LastYear == lastYear
			&& cached.RealmLimit == realmLimit
			&& string.Equals(cached.IdsText, idsText, StringComparison.Ordinal))
		{
			return cached.State;
		}

		string[] ids = SplitIds(idsText);
		if (ids.Length > realmLimit)
		{
			Array.Resize(ref ids, realmLimit);
		}
		int normalizedCount = Math.Max(0, Math.Min(realmLimit, ids.Length));

		XjXianJiState state = new XjXianJiState(
			true,
			normalizedCount,
			ids,
			lastYear,
			"Ok");
		if (actorId > 0L)
		{
			StateCache[actorId] = new CachedState(count, idsText, lastYear, realmLimit, state);
		}
		return state;
	}

	internal static bool HasFive(Actor actor)
	{
		return BuildState(actor).Count >= XjXianJiState.MaxCount;
	}

	internal static bool Add(Actor actor, string id, int currentYear, string gongFaName = "", string gongFaSource = "仙基参悟")
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		XjXianJiState state = BuildState(actor);
		int realmLimit = ResolveRealmLimit(actor);
		if (realmLimit <= 0 || state.Count >= realmLimit || Contains(state.Ids, id))
		{
			return false;
		}

		// 一部真实功法只能映射一门仙基/神通。先确保功法记录已经持久化，
		// 再写仙基；任何入口（自然、龙属、阴司、手动）都不能绕过该边界。
		if (!XjActorGongFaCollection.EnsureForXianJi(actor, id, currentYear, gongFaName, gongFaSource))
		{
			return false;
		}

		int oldLength = state.Ids.Length;
		string[] nextIds = new string[Math.Min(realmLimit, oldLength + 1)];
		for (int i = 0; i < oldLength && i < nextIds.Length - 1; i++)
		{
			nextIds[i] = state.Ids[i];
		}

		nextIds[nextIds.Length - 1] = id;
		string joined = string.Join(Separator.ToString(), nextIds);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, nextIds.Length);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds, joined);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, currentYear));
		XjXianJiOpportunitySchedule.OnCollectionChanged(actor, nextIds.Length, currentYear);
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "XianJiAdd");
		XjLongShuSystem.RefreshTitleAfterXianJiChange(actor);
		XjRealmTitleApplyService.RefreshZiFuTitleAfterXianJiChange(actor, nextIds.Length);
		XjGongFaProgression.PublishInheritanceSnapshot(actor, "XianJiSnapshot");
		return true;
	}


	internal static bool ReconcileRealmLimit(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int storedCount);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenTongIds, out string shenTongIds);
		string[] rawIds = SplitIds(idsText ?? string.Empty);
		int realmLimit = ResolveRealmLimit(actor);
		if (realmLimit < rawIds.Length)
		{
			Array.Resize(ref rawIds, realmLimit);
		}

		string joined = string.Join(Separator.ToString(), rawIds);
		bool changed = storedCount != rawIds.Length
			|| !string.Equals((idsText ?? string.Empty).Trim(), joined, StringComparison.Ordinal)
			|| !string.Equals((shenTongIds ?? string.Empty).Trim(), joined, StringComparison.Ordinal);
		if (!changed)
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, rawIds.Length);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds, joined);
		if (storedCount != rawIds.Length)
		{
			XjXianJiOpportunitySchedule.OnCollectionChanged(
				actor,
				rawIds.Length,
				XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
		}
		Forget(actor);
		XjActorGongFaCollection.ReconcileWithActor(actor, "RealmLimit");
		return true;
	}

	internal static bool RestoreSnapshot(Actor actor, string idsText, int lastYear)
	{
		if (actor?.data == null)
		{
			return false;
		}
		string[] raw = SplitIds(idsText ?? string.Empty);
		int realmLimit = ResolveRealmLimit(actor);
		System.Collections.Generic.List<string> normalized = new System.Collections.Generic.List<string>(Math.Min(realmLimit, raw.Length));
		System.Collections.Generic.HashSet<string> seen = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < raw.Length && normalized.Count < realmLimit; i++)
		{
			string id = (raw[i] ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(id) && seen.Add(id))
			{
				normalized.Add(id);
			}
		}
		string joined = string.Join(Separator.ToString(), normalized);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiCount, normalized.Count);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiIds, joined);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenTongIds, joined);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLastYear, Math.Max(0, lastYear));
		XjXianJiOpportunitySchedule.OnCollectionChanged(
			actor,
			normalized.Count,
			lastYear > 0
				? lastYear
				: XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor));
		Forget(actor);
		return true;
	}

	internal static string[] ReadRawIds(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiIds, out string idsText))
		{
			return Array.Empty<string>();
		}
		return SplitIds(idsText ?? string.Empty);
	}

	private static int ResolveRealmLimit(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return 1;
		}
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			// 神丹承接金丹的五门仙基与真实五功法集合；若按 0 处理，
			// 读档或任意集合读取都会把四部副功法永久裁掉。
			return XjXianJiState.MaxCount;
		}
		return 0;
	}

	internal static void Forget(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId > 0L)
		{
			StateCache.Remove(actorId);
		}
	}

	internal static void ClearRuntimeCache()
	{
		StateCache.Clear();
	}

	private static string[] SplitIds(string idsText)
	{
		if (string.IsNullOrWhiteSpace(idsText))
		{
			return Array.Empty<string>();
		}

		string[] raw = idsText.Split(new[] { Separator }, StringSplitOptions.RemoveEmptyEntries);
		int count = Math.Min(XjXianJiState.MaxCount, raw.Length);
		string[] result = new string[count];
		for (int i = 0; i < count; i++)
		{
			result[i] = raw[i].Trim();
		}

		return result;
	}

	private static bool Contains(string[] ids, string id)
	{
		if (ids == null)
		{
			return false;
		}

		for (int i = 0; i < ids.Length; i++)
		{
			if (string.Equals(ids[i], id, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
