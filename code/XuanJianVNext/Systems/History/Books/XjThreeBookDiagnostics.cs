using System;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.History.Books;

/// <summary>
/// 三书发布前自检。只检查独立Store索引、事实账本与待补家族事实，
/// 默认二十五年一次且仅在异常时写日志，避免正常长档刷屏。
/// </summary>
internal static class XjThreeBookDiagnostics
{
	private const int AuditIntervalYears = 25;
	private static int _lastAuditYear = -1;
	private static string _lastIssue = string.Empty;
	private static string _personalInvariantIssue = string.Empty;
	private static string _familyInvariantIssue = string.Empty;
	private static string _sectInvariantIssue = string.Empty;
	private static bool _afterLoadAuditPending;
	private static int _afterLoadAuditYear;

	internal static XjCodexThreeBookHealth BuildHealth()
	{
		XjThreeBookStoreMetrics personal = XjPersonalBiographyStore.ReadMetrics();
		XjThreeBookStoreMetrics family = XjFamilyChronicleBookStore.ReadMetrics();
		XjThreeBookStoreMetrics sect = XjSectChronicleStore.ReadMetrics();
		bool healthy = string.IsNullOrWhiteSpace(_personalInvariantIssue)
			&& string.IsNullOrWhiteSpace(_familyInvariantIssue)
			&& string.IsNullOrWhiteSpace(_sectInvariantIssue)
			&& personal.SoftOverflow <= 0L && family.SoftOverflow <= 0L && sect.SoftOverflow <= 0L
			&& personal.LedgerCount <= personal.LedgerCapacity
			&& family.LedgerCount <= family.LedgerCapacity
			&& sect.LedgerCount <= sect.LedgerCapacity
			&& XjThreeBookDeferredFamilyFacts.DroppedCount <= 0L;
		string summary = healthy ? "三书接线正常" : FirstNonEmpty(_personalInvariantIssue, _familyInvariantIssue, _sectInvariantIssue,
			personal.SoftOverflow > 0L || family.SoftOverflow > 0L || sect.SoftOverflow > 0L ? "史册出现保护条目软溢出" : string.Empty,
			personal.LedgerCount > personal.LedgerCapacity || family.LedgerCount > family.LedgerCapacity || sect.LedgerCount > sect.LedgerCapacity
				? "三书事实账本出现保护性溢出" : string.Empty,
			XjThreeBookDeferredFamilyFacts.DroppedCount > 0L ? "家族待补事实达到容量并发生丢弃" : string.Empty);
		return new XjCodexThreeBookHealth
		{
			IsHealthy = healthy,
			Summary = summary,
			DeferredFamilyFacts = XjThreeBookDeferredFamilyFacts.Count,
			DeferredResolved = XjThreeBookDeferredFamilyFacts.ResolvedCount,
			DeferredExpired = XjThreeBookDeferredFamilyFacts.ExpiredCount,
			DeferredDropped = XjThreeBookDeferredFamilyFacts.DroppedCount,
			NativePartnerChecks = XjNativeRelationshipInterop.Attempts,
			NativePartnerDataKeyHits = XjNativeRelationshipInterop.ResolvedByDataKey,
			NativePartnerReflectionHits = XjNativeRelationshipInterop.ResolvedByReflection,
			NativePartnerMisses = XjNativeRelationshipInterop.Unresolved,
			Personal = Map("修士", personal),
			Family = Map("世家", family),
			Sect = Map("宗门", sect)
		};
	}

	internal static void AuditAfterLoad(int currentYear)
	{
		// Load/bootstrap is a correctness recovery phase. Do not synchronously walk
		// all three history stores there; reuse the existing bounded annual observer
		// diagnostics stage once normal runtime resumes.
		_afterLoadAuditPending = true;
		_afterLoadAuditYear = Math.Max(0, currentYear);
	}

	internal static void AuditYear(int currentYear)
	{
		if (_afterLoadAuditPending)
		{
			_afterLoadAuditPending = false;
			int auditYear = Math.Max(0, Math.Max(currentYear, _afterLoadAuditYear));
			_afterLoadAuditYear = 0;
			_lastAuditYear = auditYear;
			Audit(auditYear, true);
			return;
		}
		if (currentYear <= 0 || _lastAuditYear >= 0 && currentYear - _lastAuditYear < AuditIntervalYears) return;
		_lastAuditYear = currentYear;
		Audit(currentYear, false);
	}

	internal static void Clear()
	{
		_lastAuditYear = -1;
		_lastIssue = string.Empty;
		_personalInvariantIssue = string.Empty;
		_familyInvariantIssue = string.Empty;
		_sectInvariantIssue = string.Empty;
		_afterLoadAuditPending = false;
		_afterLoadAuditYear = 0;
		XjNativeRelationshipInterop.ClearDiagnostics();
	}

	private static void Audit(int currentYear, bool afterLoad)
	{
		XjPersonalBiographyStore.Validate(out _personalInvariantIssue);
		XjFamilyChronicleBookStore.Validate(out _familyInvariantIssue);
		XjSectChronicleStore.Validate(out _sectInvariantIssue);
		// 自检本身不参与玩法，但应刷新史册诊断卡，保证原生关系命中与待补队列数据可见。
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.History);
		XjCodexThreeBookHealth health = BuildHealth();
		if (health.IsHealthy)
		{
			_lastIssue = string.Empty;
			return;
		}
		string issue = "year=" + currentYear + " " + health.Summary
			+ " personal=" + health.Personal.Count + "/" + health.Personal.Capacity
			+ " family=" + health.Family.Count + "/" + health.Family.Capacity
			+ " sect=" + health.Sect.Count + "/" + health.Sect.Capacity
			+ " deferred=" + health.DeferredFamilyFacts;
		if (!afterLoad && string.Equals(issue, _lastIssue, StringComparison.Ordinal)) return;
		_lastIssue = issue;
		Debug.LogWarning("[玄鉴][三书自检] " + issue);
	}

	private static XjCodexThreeBookStoreHealth Map(string name, XjThreeBookStoreMetrics value)
	{
		return new XjCodexThreeBookStoreHealth
		{
			Name = name,
			Count = value?.Count ?? 0,
			Capacity = value?.Capacity ?? 0,
			LedgerCount = value?.LedgerCount ?? 0,
			LedgerCapacity = value?.LedgerCapacity ?? 0,
			EventTypeCount = value?.EventTypeCount ?? 0,
			ExpectedEventTypeCount = value?.ExpectedEventTypeCount ?? 0,
			EventTypes = value?.EventTypes ?? string.Empty,
			MissingEventTypes = value?.MissingEventTypes ?? string.Empty,
			LastEventYear = value?.LastEventYear ?? 0,
			Attempts = value?.Attempts ?? 0L,
			Accepted = value?.Accepted ?? 0L,
			Invalid = value?.Invalid ?? 0L,
			DuplicateSource = value?.DuplicateSource ?? 0L,
			DuplicateSignature = value?.DuplicateSignature ?? 0L,
			Trimmed = value?.Trimmed ?? 0L,
			Imported = value?.Imported ?? 0L,
			LedgerPruned = value?.LedgerPruned ?? 0L,
			SoftOverflow = value?.SoftOverflow ?? 0L
		};
	}

	private static string FirstNonEmpty(params string[] values)
	{
		if (values != null)
		{
			for (int i = 0; i < values.Length; i++) if (!string.IsNullOrWhiteSpace(values[i])) return values[i].Trim();
		}
		return "三书接线状态异常";
	}
}
