using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修命数的唯一写入口。入释时以角色普通命数作为根基，之后再由具体事件
/// 与低频自悟增长；不折算为真元。按年账本负责去重与封顶。
/// </summary>
internal static class XjShiMingShuSystem
{
	internal static float GetValue(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return 0f;
		EnsureInitialized(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float value);
		return Math.Clamp(value, 0f, XjShiCatalog.MaximumShiMingShu);
	}

	internal static float GetEffectiveValue(Actor actor, int annualYear)
	{
		float own = GetValue(actor);
		if (!XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| string.IsNullOrWhiteSpace(snapshot.PatronActorId)
			|| !XjShiWorldRegistry.TryResolveLiveActor(snapshot.PatronActorId, out Actor patron)
			|| !XjShiDomainState.IsBorrowingActive(actor, patron, annualYear)) return own;
		return Math.Min(XjShiCatalog.MaximumShiMingShu, own + Math.Min(100f, GetValue(patron) * 0.1f));
	}

	internal static void TickAnnual(Actor actor, int annualYear)
	{
		if (actor?.data == null || annualYear <= 0 || !XjCultivationPathRules.IsShi(actor)) return;
		EnsureInitialized(actor);
		EnsureLedgerYear(actor, annualYear);

		// 0.9.6.9及更早把“命数事件”按300点写进修持队列。本版迁移为事件份数，
		// 只执行一次并清空旧字段，避免读档后继续污染修持。
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPendingFateEvents, out float legacyPending);
		if (legacyPending > 0f)
		{
			float migrated = Math.Clamp(legacyPending / 300f, 1f, 20f);
			TryGrantEvent(actor, annualYear, "legacy_fate_migration", migrated, "insight");
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiPendingFateEvents, 0f);
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShuPending, out float pending);
		if (pending > 0f)
		{
			ApplyAward(actor, annualYear, Math.Min(pending, XjShiCatalog.ShiMingShuAnnualEventCap), "pending");
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuPending, 0f);
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiMingShuLastInsightYear, out int lastInsight);
		if (lastInsight > 0 && annualYear - lastInsight < XjShiCatalog.ShiMingShuInsightIntervalYears) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if ((annualYear + Math.Abs(actorId)) % XjShiCatalog.ShiMingShuInsightIntervalYears != 0L) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiMingShuLastInsightYear, annualYear);
		float ownMingShu = GetValue(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		int chance = XjShiLineagePolicy.GetInsightChanceBasis(lineageId)
			+ Math.Min(2600, Math.Max(0, (int)ownMingShu) * 8);
		if (XjDeterministicHash.PositiveIndex(actorId + annualYear, "shi_mingshu_insight_v1", 10000)
			>= Math.Min(6500, chance)) return;
		float award = 1f + XjDeterministicHash.PositiveIndex(actorId + annualYear,
			"shi_mingshu_insight_amount_v1", 3);
		TryGrantEvent(actor, annualYear, "self_insight", award, "insight");
	}

	internal static bool TryGrantEvent(Actor actor, int annualYear, string eventKey, float amount, string eventType)
	{
		if (actor?.data == null || annualYear <= 0 || amount <= 0f
			|| !XjCultivationPathRules.IsShi(actor)) return false;
		EnsureInitialized(actor);
		EnsureLedgerYear(actor, annualYear);
		string normalizedKey = NormalizeLedgerKey(eventKey);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, out string ledger);
		if (normalizedKey.Length > 0 && (ledger ?? string.Empty).Contains("|" + normalizedKey + "|", StringComparison.Ordinal)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		float scaled = XjShiLineagePolicy.ScaleMingShuAward(lineageId, amount, eventType ?? string.Empty);
		if (!ApplyAward(actor, annualYear, scaled, normalizedKey)) return false;
		if (normalizedKey.Length > 0)
		{
			string nextLedger = (ledger ?? string.Empty) + "|" + normalizedKey + "|";
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, nextLedger);
		}
		return true;
	}

	/// <summary>
	/// 古释五年点化是独立的低频因果，不参与普通“年度命数事件封顶”。
	/// 这样可保证一次真实点化会同步增加古释自身释修命数；唯一例外是已经达到释修命数总上限。
	/// 返回本次实际增加值。
	/// </summary>
	internal static float GrantAncientDuhua(Actor actor, int annualYear, long targetId, float amount)
	{
		if (actor?.data == null || annualYear <= 0 || targetId <= 0L || amount <= 0f
			|| !XjCultivationPathRules.IsShi(actor)) return 0f;
		EnsureInitialized(actor);
		EnsureLedgerYear(actor, annualYear);
		string normalizedKey = NormalizeLedgerKey("ancient_duhua:" + targetId + ":" + annualYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, out string ledger);
		ledger ??= string.Empty;
		if (normalizedKey.Length > 0 && ledger.Contains("|" + normalizedKey + "|", StringComparison.Ordinal)) return 0f;

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float current);
		current = Math.Clamp(Math.Max(0f, current), 0f, XjShiCatalog.MaximumShiMingShu);
		float next = Math.Clamp(current + Math.Max(0f, amount), 0f, XjShiCatalog.MaximumShiMingShu);
		float applied = next - current;
		if (applied <= 0.0001f) return 0f;

		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, next);
		if (normalizedKey.Length > 0)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, ledger + "|" + normalizedKey + "|");
		}
		return applied;
	}

	/// <summary>
	/// 九世今释庙主取得北世尊金地后的高位温养。它不是随机“命数事件”，
	/// 不占普通年度事件封顶，也不经法脉事件倍率；唯一写入仍集中在本系统。
	/// 调用方负责按年度去重，本入口只补到法相最低命数门槛。
	/// </summary>
	internal static float GrantTempleMasterFoundation(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f || !XjCultivationPathRules.IsShi(actor)) return 0f;
		EnsureInitialized(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float current);
		current = Math.Clamp(Math.Max(0f, current), 0f, XjShiCatalog.MaximumShiMingShu);
		float cap = Math.Min(XjShiCatalog.MaximumShiMingShu, XjShiCatalog.DharmaFormMinimumMingShu);
		float next = Math.Min(cap, current + Math.Max(0f, amount));
		float applied = next - current;
		if (applied <= 0.0001f) return 0f;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, next);
		return applied;
	}

	internal static void QueueEvent(Actor actor, float amount)
	{
		if (actor?.data == null || amount <= 0f || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShuPending, out float pending);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuPending, Math.Max(0f, pending) + amount);
	}

	internal static void TryGrantHighFateInteraction(Actor actor, Actor other, int annualYear, string relationType)
	{
		if (actor?.data == null || other?.data == null || annualYear <= 0
			|| !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetFloat(other, XjActorDataKeys.MingShu, out float otherMingShu);
		if (otherMingShu < 80f) return;
		long otherId = ((BaseSystemData)other.data).id;
		string relation = string.Equals(relationType, "close_friend", StringComparison.Ordinal)
			? "close_friend" : "acquaintance";
		string token = "|" + relation + ":" + otherId.ToString(CultureInfo.InvariantCulture) + "|";
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiMingShuInteractionKeys, out string interactionLedger);
		interactionLedger ??= string.Empty;
		if (interactionLedger.Contains(token, StringComparison.Ordinal)
			|| interactionLedger.Length + token.Length > 4096) return;
		float award = string.Equals(relation, "close_friend", StringComparison.Ordinal) ? 5f : 3f;
		if (TryGrantEvent(actor, annualYear, "high_fate_interaction:" + relation + ":" + otherId, award, "interaction"))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuInteractionKeys, interactionLedger + token);
		}
	}

	internal static void InitializeFromOrdinaryFate(Actor actor)
	{
		if (actor?.data == null) return;
		XjMingShuState.Normalize(actor);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float ordinary);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float existing);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu,
			Math.Clamp(Math.Max(Math.Max(0f, existing), Math.Max(0f, ordinary)),
				0f, XjShiCatalog.MaximumShiMingShu));
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, 0f);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuPending, 0f);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiMingShuLastInsightYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiMingShuLedgerYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuInteractionKeys, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuAwardedThisYear, 0f);
	}

	private static void EnsureInitialized(Actor actor)
	{
		if (actor?.data == null) return;
		if (!XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float value)
			|| float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
		{
			InitializeFromOrdinaryFate(actor);
		}
	}

	private static void EnsureLedgerYear(Actor actor, int year)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiMingShuLedgerYear, out int ledgerYear);
		if (ledgerYear == year) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiMingShuLedgerYear, year);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiMingShuLedgerKeys, string.Empty);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuAwardedThisYear, 0f);
	}

	private static bool ApplyAward(Actor actor, int annualYear, float amount, string source)
	{
		_ = annualYear;
		_ = source;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShuAwardedThisYear, out float awarded);
		float remaining = Math.Max(0f, XjShiCatalog.ShiMingShuAnnualEventCap - Math.Max(0f, awarded));
		float applied = Math.Min(Math.Max(0f, amount), remaining);
		if (applied <= 0f) return false;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiMingShu, out float current);
		float next = Math.Clamp(Math.Max(0f, current) + applied, 0f, XjShiCatalog.MaximumShiMingShu);
		if (next <= current + 0.0001f) return false;
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShu, next);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiMingShuAwardedThisYear, awarded + (next - current));
		return true;
	}

	private static string NormalizeLedgerKey(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return string.Empty;
		string trimmed = value.Trim().Replace("|", string.Empty, StringComparison.Ordinal);
		return trimmed.Length <= 96 ? trimmed : trimmed.Substring(0, 96);
	}
}
