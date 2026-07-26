using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 炼气、筑基内部道行阶段的瓶颈事件。
/// 只在跨越关键阶段阈值时介入，境界突破仍由 XjBreakthroughRules 负责。
/// 已突破的瓶颈以位掩码持久化，读档后不会重复触发。
/// </summary>
internal static class XjBottleneckEventSystem
{
	private const string LianQiMiddle = "LianQiMiddle";
	private const string LianQiLate = "LianQiLate";
	private const string ZhuJiMiddle = "ZhuJiMiddle";
	private const string ZhuJiLate = "ZhuJiLate";

	internal static float ApplyGrowthGate(Actor actor, in XjActorCultivationSnapshot snapshot, float proposedZhenYuan)
	{
		if (actor?.data == null || proposedZhenYuan <= snapshot.ZhenYuan)
		{
			return proposedZhenYuan;
		}

		float stored = ReadStoredZhenYuan(actor);
		if (HasBottleneckBypass(actor))
		{
			ClearActiveBottleneck(actor, "Bypass:SpecialAptitude");
			WriteStoredZhenYuan(actor, 0f);
			// 特殊资质只绕过瓶颈判定，不能绕过当前境界真元上限。
			// 旧档残留的蓄积真元也必须先按当前境界封顶，禁止跨境界释放。
			return XjCultivationGrowthRules.ApplyRealmCap(snapshot, proposedZhenYuan + stored);
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckResolvedMask, out int resolvedMask);
		if (!TryResolvePendingBottleneck(
				snapshot.RealmId, snapshot.ZhenYuan, proposedZhenYuan, resolvedMask,
				out BottleneckDefinition definition))
		{
			// 兼容旧存档：若瓶颈状态已经清除但仍残留蓄积真元，在下一次正向修炼时返还。
			if (stored <= 0f) return XjCultivationGrowthRules.ApplyRealmCap(snapshot, proposedZhenYuan);
			WriteStoredZhenYuan(actor, 0f);
			return XjCultivationGrowthRules.ApplyRealmCap(snapshot, proposedZhenYuan + stored);
		}

		int currentYear = GetCurrentYear();
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBottleneckActiveId, out string activeId);
		float gateValue = definition.Threshold - 1f;
		float incomingGain = Math.Max(0f, proposedZhenYuan - snapshot.ZhenYuan);
		if (!string.Equals(activeId, definition.Id, StringComparison.Ordinal))
		{
			StartBottleneck(actor, in definition, currentYear);
			WriteStoredZhenYuan(actor, stored + Math.Max(0f, proposedZhenYuan - gateValue));
			return gateValue;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, out int nextAttemptYear);
		if (currentYear <= 0 || currentYear < Math.Max(1, nextAttemptYear))
		{
			WriteStoredZhenYuan(actor, stored + incomingGain);
			return gateValue;
		}

		if (!RollBreakthrough(actor, snapshot, in definition, currentYear))
		{
			WriteStoredZhenYuan(actor, stored + incomingGain);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, currentYear + 1);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckLastResult, "Failure:" + definition.Id + ":" + currentYear);
			if (ShouldRecordFailure(actor, in definition, currentYear)) PublishFailure(actor, in definition);
			return gateValue;
		}

		resolvedMask |= definition.Mask;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckResolvedMask, resolvedMask);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckActiveId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckLastResult, "Success:" + definition.Id + ":" + currentYear);
		PublishResolved(actor, in definition);

		float releasedZhenYuan = XjCultivationGrowthRules.ApplyRealmCap(snapshot, proposedZhenYuan + stored);
		WriteStoredZhenYuan(actor, 0f);
		// 高额丹力或洞天奖励跨过下一道关隘时，只登记下一瓶颈；全部溢出继续蓄积。
		if (TryResolvePendingBottleneck(
				snapshot.RealmId, definition.Threshold, releasedZhenYuan, resolvedMask,
				out BottleneckDefinition nextDefinition))
		{
			StartBottleneck(actor, in nextDefinition, currentYear);
			float nextGate = nextDefinition.Threshold - 1f;
			WriteStoredZhenYuan(actor, Math.Max(0f, releasedZhenYuan - nextGate));
			return nextGate;
		}

		return releasedZhenYuan;
	}

	internal static string BuildStatusSummary(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBottleneckActiveId, out string activeId)
			|| string.IsNullOrWhiteSpace(activeId)
			|| !TryGetDefinition(activeId, out BottleneckDefinition definition))
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, out int startedYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, out int nextAttemptYear);
		string years = startedYear > 0 ? "，始于" + startedYear + "年" : string.Empty;
		string retry = nextAttemptYear > 0 ? "，下次冲关" + nextAttemptYear + "年" : string.Empty;
		float stored = ReadStoredZhenYuan(actor);
		string reserve = stored > 0.5f ? "，蓄积真元" + (int)Math.Floor(stored) : string.Empty;
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		int currentYear = GetCurrentYear();
		int chanceYear = Math.Max(currentYear, nextAttemptYear);
		float chance = CalculateChance(actor, in snapshot, in definition, chanceYear);
		string chanceLabel = nextAttemptYear > currentYear ? "下次冲关概率" : "当前冲关概率";
		string chanceText = "，" + chanceLabel + (chance * 100f).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "%";
		return definition.DisplayName + chanceText + years + retry + reserve;
	}

	private static void StartBottleneck(Actor actor, in BottleneckDefinition definition, int currentYear)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckActiveId, definition.Id);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, Math.Max(1, currentYear + 1));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckLastResult, "Started:" + definition.Id + ":" + currentYear);

		if (!XjRuntimeSettings.BroadcastBottleneckEnabled)
		{
			return;
		}

		string text = "【修行瓶颈】" + ResolveActorName(actor) + "行至" + definition.DisplayName + "，真元滞涩，需静心冲关。";
		if (definition.IsFoundation)
		{
			XjBroadcastSystem.BroadcastBLevelActorEvent(actor, text, iconId: XjEventIconCatalog.ZiFuUpgrade);
		}
		else
		{
			XjBroadcastSystem.PostActor(actor, text);
		}
	}

	private static void PublishResolved(Actor actor, in BottleneckDefinition definition)
	{
		if (!XjRuntimeSettings.BroadcastBottleneckEnabled)
		{
			return;
		}

		string text = "【瓶颈突破】" + ResolveActorName(actor) + "勘破" + definition.DisplayName + "，道行再进。";
		if (definition.IsFoundation)
		{
			XjBroadcastSystem.BroadcastBLevelActorEvent(actor, text, iconId: XjEventIconCatalog.ZiFuUpgrade);
		}
		else
		{
			XjBroadcastSystem.PostActor(actor, text);
		}
	}

	private static bool ShouldRecordFailure(Actor actor, in BottleneckDefinition definition, int currentYear)
	{
		// 炼气人口数量大，逐年写历史会造成存档膨胀；筑基失败也只每三年留一条记录。
		if (!definition.IsFoundation || currentYear <= 0) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, out int startedYear);
		int elapsed = Math.Max(0, currentYear - startedYear);
		return elapsed > 0 && elapsed % 3 == 0;
	}

	private static void PublishFailure(Actor actor, in BottleneckDefinition definition)
	{
		if (!XjRuntimeSettings.BroadcastBottleneckEnabled)
		{
			return;
		}

		string text = "【冲关未果】" + ResolveActorName(actor)
			+ "冲击" + definition.DisplayName + "未成，需再行温养真元。";
		// 仅按三年节流写入世界历史，不弹出全局提示。
		XjBroadcastSystem.PostActor(actor, text);
	}

	private static bool RollBreakthrough(Actor actor, in XjActorCultivationSnapshot snapshot, in BottleneckDefinition definition, int year)
	{
		if (HasBottleneckBypass(actor))
		{
			return true;
		}

		float chance = CalculateChance(actor, in snapshot, in definition, year);
		long actorId = ((BaseSystemData)actor.data).id;
		return XjDeterministicHash.Roll01(actorId, year, definition.Id, "bottleneck") <= chance;
	}

	private static float CalculateChance(Actor actor, in XjActorCultivationSnapshot snapshot, in BottleneckDefinition definition, int year)
	{
		float aptitudeBonus = Math.Max(0, Math.Min(5, snapshot.XjZz - 1)) * 0.05f;
		// 使用包含器物加成的有效命数，与角色信息和实际修炼快照保持一致。
		float mingShuBonus = Math.Min(0.12f, Math.Max(0f, snapshot.MingShu) / 5000f);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, out int startedYear);
		float persistenceBonus = Math.Min(0.20f, Math.Max(0, year - startedYear) * 0.04f);
		// 三全破境灵丹用于炼气与筑基本境内部关隘，不替代筑基升紫府等大境突破丹。
		float pillBonus = XjAlchemyPillEffectSystem.TryConsumeBottleneckChanceBonus(actor, snapshot.RealmId, year);
		return Math.Min(0.95f, definition.BaseChance + aptitudeBonus + mingShuBonus + persistenceBonus + pillBonus);
	}

	private static bool TryResolvePendingBottleneck(
		string realmId,
		float currentZhenYuan,
		float proposedZhenYuan,
		int resolvedMask,
		out BottleneckDefinition definition)
	{
		definition = default;
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			if (TryGetDefinition(LianQiMiddle, out BottleneckDefinition middle)
				&& (resolvedMask & middle.Mask) == 0
				&& currentZhenYuan < middle.Threshold
				&& proposedZhenYuan >= middle.Threshold)
			{
				definition = middle;
				return true;
			}
			if (TryGetDefinition(LianQiLate, out BottleneckDefinition late)
				&& (resolvedMask & late.Mask) == 0
				&& currentZhenYuan < late.Threshold
				&& proposedZhenYuan >= late.Threshold)
			{
				definition = late;
				return true;
			}
		}
		else if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			if (TryGetDefinition(ZhuJiMiddle, out BottleneckDefinition middle)
				&& (resolvedMask & middle.Mask) == 0
				&& currentZhenYuan < middle.Threshold
				&& proposedZhenYuan >= middle.Threshold)
			{
				definition = middle;
				return true;
			}
			if (TryGetDefinition(ZhuJiLate, out BottleneckDefinition late)
				&& (resolvedMask & late.Mask) == 0
				&& currentZhenYuan < late.Threshold
				&& proposedZhenYuan >= late.Threshold)
			{
				definition = late;
				return true;
			}
		}
		return false;
	}

	private static bool TryGetDefinition(string id, out BottleneckDefinition definition)
	{
		definition = id switch
		{
			LianQiMiddle => new BottleneckDefinition(LianQiMiddle, "炼气中期关隘", 600f, 1 << 0, 0.58f, false),
			LianQiLate => new BottleneckDefinition(LianQiLate, "炼气后期关隘", 900f, 1 << 1, 0.50f, false),
			ZhuJiMiddle => new BottleneckDefinition(ZhuJiMiddle, "筑基中期关隘", 12000f, 1 << 2, 0.40f, true),
			ZhuJiLate => new BottleneckDefinition(ZhuJiLate, "筑基后期关隘", 24000f, 1 << 3, 0.32f, true),
			_ => default
		};
		return !string.IsNullOrWhiteSpace(definition.Id);
	}

	private static int GetCurrentYear()
	{
		try
		{
			return World.world?.map_stats?.year ?? 0;
		}
		catch
		{
			return 0;
		}
	}

	private static bool HasBottleneckBypass(Actor actor)
	{
		try
		{
			return actor != null && (actor.hasTrait("ChuShen7") || actor.hasTrait("ChuShen8"));
		}
		catch
		{
			return false;
		}
	}

	internal static float GetStoredZhenYuan(Actor actor)
	{
		return ReadStoredZhenYuan(actor);
	}

	/// <summary>
	/// 境界真实变化后，旧境界的活动瓶颈与蓄积真元不得带入下一境。
	/// resolved mask 保留，因为炼气、筑基使用互不重叠的位段；只清理当前活动态和储备。
	/// </summary>
	internal static void ResetForRealmTransition(Actor actor, string previousRealmId, string currentRealmId)
	{
		if (actor?.data == null) return;
		string previous = XjRealmHelper.NormalizeId(previousRealmId);
		string current = XjRealmHelper.NormalizeId(currentRealmId);
		if (string.Equals(previous, current, StringComparison.Ordinal)) return;

		ClearActiveBottleneck(actor, "RealmTransition:" + previous + "->" + current);
		WriteStoredZhenYuan(actor, 0f);
	}

	private static float ReadStoredZhenYuan(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.XjBottleneckStoredZhenYuan, out float value)) return 0f;
		return Math.Clamp(value, 0f, 10000000f);
	}

	private static void WriteStoredZhenYuan(Actor actor, float value)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjBottleneckStoredZhenYuan,
			Math.Clamp(value, 0f, 10000000f));
	}

	private static void ClearActiveBottleneck(Actor actor, string reason)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBottleneckActiveId, out string activeId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckActiveId, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBottleneckNextAttemptYear, 0);
		if (!string.IsNullOrWhiteSpace(activeId))
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBottleneckLastResult, (reason ?? "Cleared") + ":" + activeId);
	}

	private static string ResolveActorName(Actor actor)
	{
		string name = null;
		try
		{
			name = actor?.getName();
		}
		catch
		{
		}

		if (string.IsNullOrWhiteSpace(name)
			|| string.Equals(name.Trim(), "NO_NAME", StringComparison.OrdinalIgnoreCase))
		{
			return "无名修士";
		}

		return name.Trim();
	}

	private readonly struct BottleneckDefinition
	{
		internal readonly string Id;
		internal readonly string DisplayName;
		internal readonly float Threshold;
		internal readonly int Mask;
		internal readonly float BaseChance;
		internal readonly bool IsFoundation;

		internal BottleneckDefinition(string id, string displayName, float threshold, int mask, float baseChance, bool isFoundation)
		{
			Id = id ?? string.Empty;
			DisplayName = displayName ?? string.Empty;
			Threshold = threshold;
			Mask = mask;
			BaseChance = baseChance;
			IsFoundation = isFoundation;
		}
	}
}
