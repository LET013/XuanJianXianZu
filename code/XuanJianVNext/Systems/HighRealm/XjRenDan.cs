using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjRenDanRules
{
	internal const int ZiFuRenDanMinimumTargetCount = 4;
	internal const float ZiFuRenDanChance = 0.50f;
	internal const float JinDanPenaltyRate = 0.15f;
	internal const string RenDanShenTongTagKey = "xuanjian.rendan_shentong_acquired";
	internal const string RenDanPollutionTagKey = "xuanjian.rendan_polluted";

	internal static string FormatRuleSummary()
	{
		return "人丹：符合条件的紫府在第4神通与第5神通窗口各有一次独立的对半判定；命中后直接从当前可用仙基池确定续途神通，不再要求事先持有一门未用的五品映射功法。人丹优先选定其他家族、同道途且无高境庇护的小族筑基；若本局没有这类人选，可放宽到无金丹的普通家族。事件可能成功、被旁人截走或遭掺假；成功所得神通会使日后求金明显更为艰难，污染结果会留下隐藏污染标记并使命定求金失败。";
	}
}

internal static partial class XjRenDan
{
	private static readonly Dictionary<long, XjRenDanState> entriesByActorId = new Dictionary<long, XjRenDanState>();

	internal static bool TryGet(long actorId, out XjRenDanState state)
	{
		if (actorId > 0L && entriesByActorId.TryGetValue(actorId, out state))
		{
			return state.Found;
		}

		state = default;
		return false;
	}

	internal static string GetSummary(long actorId)
	{
		return TryGet(actorId, out XjRenDanState state)
			? Format(state)
			: string.Empty;
	}

	internal static bool TryAcquireDuringXianJi(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState state,
		int currentYear)
	{
		return TryPrepareRenDanPlan(actor, snapshot, state, currentYear);
	}


	internal static bool HasPollution(Actor actor)
	{
		if (actor?.data == null) return false;
		int value = 0;
		((BaseSystemData)actor.data).get(XjRenDanRules.RenDanPollutionTagKey, out value, 0);
		return value > 0;
	}

	internal static bool HasRenDanShenTongAcquired(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		int hasTag = 0;
		((BaseSystemData)actor.data).get(XjRenDanRules.RenDanShenTongTagKey, out hasTag, 0);
		if (hasTag > 0)
		{
			return true;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || !entriesByActorId.TryGetValue(actorId, out XjRenDanState state) || !state.Found)
		{
			return false;
		}

		// Prepared 只是暗中布置，尚未获得人丹神通；旧档 Stage 为空时仍由原角色标记判定。
		bool successful = string.IsNullOrWhiteSpace(state.Outcome)
			|| string.Equals(state.Outcome, XjRenDanOutcomes.Success, StringComparison.Ordinal);
		return successful
			&& (string.Equals(state.Stage, XjRenDanStages.AwaitingDeath, StringComparison.Ordinal)
				|| string.Equals(state.Stage, XjRenDanStages.Resolved, StringComparison.Ordinal));
	}

	internal static IReadOnlyList<XjRenDanState> ReadAllEntries()
	{
		if (entriesByActorId.Count == 0)
		{
			return Array.Empty<XjRenDanState>();
		}

		List<XjRenDanState> entries = new List<XjRenDanState>(entriesByActorId.Values);
		entries.Sort((left, right) =>
		{
			int year = left.Year.CompareTo(right.Year);
			return year != 0 ? year : left.ActorId.CompareTo(right.ActorId);
		});
		return entries;
	}

	internal static void ExportArchiveRecords(List<XjRenDanArchiveData> records)
	{
		if (records == null)
		{
			return;
		}

		foreach (XjRenDanState state in entriesByActorId.Values)
		{
			if (!state.Found || state.ActorId <= 0L)
			{
				continue;
			}

			records.Add(new XjRenDanArchiveData
			{
				ActorId = state.ActorId,
				ActorName = state.ActorName,
				Year = state.Year,
				Source = state.Source,
				ShenTongCount = state.ShenTongCount,
				VictimActorId = state.VictimActorId,
				VictimActorName = state.VictimActorName,
				VictimDaoTu = state.VictimDaoTu,
				Summary = state.Summary,
				DeathFinalized = state.DeathFinalized,
				DeathFinalizedYear = state.DeathFinalizedYear,
				Stage = state.Stage,
				Outcome = state.Outcome,
				RivalActorId = state.RivalActorId,
				RivalActorName = state.RivalActorName
			});
		}
	}

	internal static void ImportArchiveRecords(IEnumerable<XjRenDanArchiveData> records)
	{
		entriesByActorId.Clear();
		if (records == null)
		{
			return;
		}

		foreach (XjRenDanArchiveData record in records)
		{
			if (record == null || record.ActorId <= 0L)
			{
				continue;
			}

			entriesByActorId[record.ActorId] = new XjRenDanState(
				true,
				record.ActorId,
				record.ActorName,
				record.Year,
				record.Source,
				record.ShenTongCount,
				record.VictimActorId,
				record.VictimActorName,
				record.VictimDaoTu,
				record.Summary,
				record.DeathFinalized,
				record.DeathFinalizedYear,
				string.IsNullOrWhiteSpace(record.Stage)
					? (record.DeathFinalized ? XjRenDanStages.Resolved : XjRenDanStages.AwaitingDeath)
					: record.Stage,
				string.IsNullOrWhiteSpace(record.Outcome) ? XjRenDanOutcomes.Success : record.Outcome,
				record.RivalActorId,
				record.RivalActorName);
		}
	}

	internal static bool FinalizeVictimDeath(long victimActorId, long sourceActorId, int currentYear, string victimActorName)
	{
		return FinalizePreparedVictimDeath(victimActorId, sourceActorId, currentYear, victimActorName);
	}

	internal static void Clear()
	{
		entriesByActorId.Clear();
	}

	private static string Format(in XjRenDanState state)
	{
		string year = state.Year > 0 ? XjChronology.FormatYear(state.Year) : "未知年份";
		string source = string.IsNullOrWhiteSpace(state.Source) ? "紫府炼筑基" : state.Source.Trim();
		string victim = state.VictimActorId > 0L
			? " - 被炼者：" + (string.IsNullOrWhiteSpace(state.VictimActorName) ? state.VictimActorId.ToString(CultureInfo.InvariantCulture) : state.VictimActorName.Trim())
			: string.Empty;
		string summary = string.IsNullOrWhiteSpace(state.Summary) ? XjRenDanRules.FormatRuleSummary() : state.Summary.Trim();
		string stage = string.IsNullOrWhiteSpace(state.Stage) ? string.Empty : " - 阶段：" + state.Stage;
		return source + " - " + year + " - 神通数" + state.ShenTongCount.ToString(CultureInfo.InvariantCulture) + victim + stage + " - " + summary;
	}

	private static bool TryFindRecordForVictim(long victimActorId, long sourceActorId, out XjRenDanState state)
	{
		if (sourceActorId > 0L && entriesByActorId.TryGetValue(sourceActorId, out state) && state.VictimActorId == victimActorId)
		{
			return state.Found;
		}

		foreach (XjRenDanState candidate in entriesByActorId.Values)
		{
			if (candidate.Found && candidate.VictimActorId == victimActorId)
			{
				state = candidate;
				return true;
			}
		}

		state = default;
		return false;
	}

	private static string ResolveGainedXianJi(string source)
	{
		if (string.IsNullOrWhiteSpace(source))
		{
			return "神通";
		}

		string trimmed = source.Trim();
		int separator = trimmed.IndexOf('：');
		if (separator >= 0 && separator + 1 < trimmed.Length)
		{
			return trimmed.Substring(separator + 1).Trim();
		}

		return "神通";
	}

	private static bool IsReincarnation(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		int applied = 0;
		((BaseSystemData)actor.data).get("xuanjian.reincarnation_applied", out applied, 0);
		return applied > 0;
	}

	private static bool IsClosedCultivation(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L || !XjGuoWeiQuanBingRegistry.TryGet(actorId, out XjGuoWeiQuanBingState state))
		{
			return false;
		}

		return state.IntegrationRetreatActive;
	}

	private static bool TryMarkVictimDeathPending(Actor victim, long sourceActorId, int currentYear)
	{
		if (victim?.data == null || sourceActorId <= 0L || !((NanoObject)victim).isAlive())
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(victim, XjActorDataKeys.XjRenDanDeathHandled, out int handled) && handled > 0)
		{
			return false;
		}

		if (!TryApplyRenDanMingShuCost(victim))
		{
			return false;
		}

		XjActorAccessor.SetInt(victim, XjActorDataKeys.XjRenDanDeathPending, 1);
		XjActorAccessor.SetInt(victim, XjActorDataKeys.XjRenDanDeathYear, Math.Max(0, currentYear));
		XjActorAccessor.SetInt(victim, XjActorDataKeys.XjRenDanDeathSourceActorId, sourceActorId > int.MaxValue ? int.MaxValue : (int)sourceActorId);
		XjActorAccessor.SetString(victim, XjActorDataKeys.XjRenDanDeathReason, "RenDanRefined");
		return true;
	}

	private static bool TryApplyRenDanMingShuCost(Actor victim)
	{
		if (victim?.data == null)
		{
			return false;
		}

		XjActorAccessor.TryGetFloat(victim, XjActorDataKeys.MingShu, out float total);
		XjActorAccessor.TryGetFloat(victim, XjActorDataKeys.MingShuCongenital, out float congenital);
		XjActorAccessor.TryGetFloat(victim, XjActorDataKeys.MingShuAcquired, out float acquired);

		total = (float)Math.Floor(Math.Max(0f, total));
		congenital = (float)Math.Floor(Math.Max(0f, congenital));
		acquired = (float)Math.Floor(Math.Max(0f, acquired));
		float remaining = Math.Min(1000f, total);
		float acquiredAvailable = Math.Max(0f, acquired);
		float acquiredCost = Math.Min(acquiredAvailable, remaining);
		acquired -= acquiredCost;
		remaining -= acquiredCost;

		if (remaining > 0f)
		{
			float congenitalCost = Math.Min(congenital, remaining);
			congenital -= congenitalCost;
			remaining -= congenitalCost;
		}

		XjMingShuState.Set(victim, congenital, acquired);
		return true;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

}

