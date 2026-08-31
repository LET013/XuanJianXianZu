using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using XuanJianVNext.Core;
using XuanJianVNext.Architecture.Persistence;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.Archive;

/// <summary>
/// 世界级玄鉴归档。读取与写入均必须落到 WorldBox 实际参与存档的
/// custom-data 容器，而不是依赖单一版本的 MapBox.data 字段布局。
/// </summary>
internal static class XjWorldArchiveSystem
{
	private const string ArchiveKey = "xuanjian.vnext.archive.v1";
	private const string ArchiveBackupKey = "xuanjian.vnext.archive.v1.backup";
	private const float NormalCommitDelaySeconds = 120f;
	// A world archive contains every family, warehouse and long-lived record.  It
	// must be coalesced aggressively: serialising it on the main thread after
	// every protected event was the largest avoidable allocation spike at 20x+.
	// Save snapshots still force an immediate commit below, so this only changes
	// background persistence latency, never save correctness.
	private const float ProtectedCommitDelaySeconds = 45f;
	private const float MinimumCommitIntervalSeconds = 60f;
	private const float MissingContainerInitializationGraceSeconds = 3f;
	private const float UnresolvedContainerProbeIntervalSeconds = 0.5f;

	private static XjWorldArchiveData protectedRetentionBaseline;
	private static long pendingWriteRevision;
	private static long lastWrittenRevision;
	private static bool urgentCommitRequested;
	private static float firstDirtyAt = -1f;
	private static float protectedRequestedAt = -1f;
	private static float lastCommitAt = -1000f;
	private static bool hasLoadedReadOnly;
	private static int unresolvedContainerAttempts;
	private static float firstUnresolvedContainerAt = -1f;
	private static float nextUnresolvedContainerProbeAt = -1f;

	private static MapBox cachedWorld;
	private static int cachedWorldSeedId = int.MinValue;
	private static bool hasLoggedReadFailure;
	private static bool hasLoggedCommitFailure;
	private static string cachedEncodedArchive = string.Empty;
	private static long cachedEncodedSnapshotRevision = -1L;
	private static bool cachedEncodedArchiveCommitted;

	internal static bool HasPendingWrite => urgentCommitRequested || pendingWriteRevision > lastWrittenRevision;
	internal static bool HasProtectedCommitRequest => urgentCommitRequested;
	internal static bool HasLoadedArchive => hasLoadedReadOnly;
	internal static bool IsBackgroundCommitDue => IsCommitDue(UnityEngine.Time.unscaledTime);
	internal static bool NeedsBackgroundSnapshotPreparation =>
		hasLoadedReadOnly && XjWorldSchemaGuard.GameplayEnabled && XjWorldArchiveSnapshotCache.HasDirtySections;

	internal static void MarkChanged([CallerFilePath] string callerFilePath = "")
	{
		MarkChangedCore(
			XjWorldArchiveSnapshotCache.DirtySectionsForCaller(callerFilePath),
			callerFilePath);
	}

	internal static void MarkChanged(
		XjWorldArchiveSection sections,
		[CallerFilePath] string callerFilePath = "")
	{
		MarkChangedCore(sections, callerFilePath);
	}

	internal static void MarkModuleChanged(string moduleId)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		XjModuleArchiveRegistry.MarkModuleDirty(moduleId);
		MarkChangedCore(XjWorldArchiveSection.Modules, callerFilePath: string.Empty, moduleDirtyAlreadyMarked: true);
	}

	private static void MarkChangedCore(
		XjWorldArchiveSection sections,
		string callerFilePath,
		bool moduleDirtyAlreadyMarked = false)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if ((sections & XjWorldArchiveSection.All) == XjWorldArchiveSection.None)
		{
			sections = XjWorldArchiveSection.All;
		}
		if (pendingWriteRevision <= lastWrittenRevision && firstDirtyAt < 0f)
		{
			firstDirtyAt = UnityEngine.Time.unscaledTime;
		}
		pendingWriteRevision++;
		XjWorldArchiveSnapshotCache.MarkDirty(sections);
		if (!moduleDirtyAlreadyMarked && (sections & XjWorldArchiveSection.Modules) != 0)
		{
			XjModuleArchiveRegistry.MarkDirtyForCaller(callerFilePath);
		}
	}

	internal static void RequestProtectedCommit([CallerFilePath] string callerFilePath = "")
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		XjWorldArchiveSection sections = XjWorldArchiveSnapshotCache.DirtySectionsForCaller(callerFilePath);
		XjWorldArchiveSnapshotCache.MarkDirty(sections);
		if ((sections & XjWorldArchiveSection.Modules) != 0)
		{
			XjModuleArchiveRegistry.MarkDirtyForCaller(callerFilePath);
		}
		if (!urgentCommitRequested)
		{
			protectedRequestedAt = UnityEngine.Time.unscaledTime;
		}
		urgentCommitRequested = true;
	}

	internal static void LoadReadOnly()
	{
		LoadReadOnly(allowInitializeEmpty: false);
	}

	/// <summary>
	/// WorldBox 部分版本会在 finishingUpLoading 之后才挂接 map_stats.custom_data。
	/// 普通读取先等待；确认世界已进入可交互状态并经过短暂宽限期后，仍无容器才按
	/// 真正新世界初始化。这样开局逻辑不会一直被 GameplayEnabled 门禁挡到第一次保存。
	/// </summary>
	internal static void TryResolveMissingContainerAfterLoadGracePeriod()
	{
		EnsureWorldIdentity();
		if (hasLoadedReadOnly || firstUnresolvedContainerAt < 0f) return;
		if (!Config.game_loaded
			|| World.world?.map_stats == null
			|| SmoothLoader.isLoading()) return;
		if (UnityEngine.Time.unscaledTime - firstUnresolvedContainerAt < MissingContainerInitializationGraceSeconds) return;
		LoadReadOnly(allowInitializeEmpty: true);
	}

	private static void LoadReadOnly(bool allowInitializeEmpty)
	{
		EnsureWorldIdentity();
		if (hasLoadedReadOnly)
		{
			return;
		}
		float now = UnityEngine.Time.unscaledTime;
		// 自定义数据容器尚未挂接时，XjNativeWorldArchiveInterop 会走版本兼容的原生容器兜底路径。
		// 读档早期若每个渲染帧都探测，既无收益又会制造明显 GC 峰值；正式存档与
		// 宽限期后的新世界初始化使用 allowInitializeEmpty，始终可立即探测。
		if (!allowInitializeEmpty
			&& firstUnresolvedContainerAt >= 0f
			&& now < nextUnresolvedContainerProbeAt)
		{
			return;
		}

		if (!XjNativeWorldArchiveInterop.TryGetDataObject(out object dataObject, createIfMissing: false))
		{
			if (firstUnresolvedContainerAt < 0f)
			{
				firstUnresolvedContainerAt = now;
			}
			// WorldBox 的自定义数据容器在部分版本中会晚于 finishingUpLoading
			// 初始化。普通读取阶段先等待；只有 SaveManager 正式生成世界快照，
			// 或世界已稳定进入可交互状态并越过上面的宽限期，才允许初始化新档。
			unresolvedContainerAttempts++;
			if (!allowInitializeEmpty)
			{
				nextUnresolvedContainerProbeAt = now + UnresolvedContainerProbeIntervalSeconds;
			}
			if (!allowInitializeEmpty
				|| !XjNativeWorldArchiveInterop.TryGetDataObject(out dataObject, createIfMissing: true))
			{
				return;
			}
		}
		unresolvedContainerAttempts = 0;
		firstUnresolvedContainerAt = -1f;
		nextUnresolvedContainerProbeAt = -1f;

		bool primaryRead = XjNativeWorldArchiveInterop.TryReadString(dataObject, ArchiveKey, out string primaryRaw);
		XjWorldArchiveData data = null;
		bool primaryValid = primaryRead
			&& !string.IsNullOrWhiteSpace(primaryRaw)
			&& XjWorldArchiveCodec.TryDecode(primaryRaw, out data);

		// P0 内存边界：主归档可用时绝不读取、更不反序列化备份。
		// 只有主归档缺失或损坏时，才进入一次性备份回退。
		bool backupRead = false;
		bool backupValid = false;
		string backupStored = string.Empty;
		if (!primaryValid)
		{
			backupRead = XjNativeWorldArchiveInterop.TryReadString(dataObject, ArchiveBackupKey, out backupStored);
			if (backupRead && !string.IsNullOrWhiteSpace(backupStored))
			{
				backupValid = XjWorldArchiveCodec.TryDecode(backupStored, out data);
			}
		}

		if (!primaryValid && !backupValid)
		{
			// 两项都明确为空才是新世界。若存在非空但损坏的文本，则继续保持
			// 可重试，并保留日志，避免把坏读结果覆盖成新档。
			if (string.IsNullOrWhiteSpace(primaryRaw) && string.IsNullOrWhiteSpace(backupStored))
			{
				hasLoadedReadOnly = true;
				protectedRetentionBaseline = null;
				XjWorldSchemaGuard.MarkNewWorld();
				XjWorldArchiveSnapshotCache.Clear();
				lastWrittenRevision = pendingWriteRevision;
			}
			else if (!hasLoggedReadFailure)
			{
				hasLoggedReadFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 主归档与备份归档均无法解码，已阻止空档覆盖。\n");
			}
			return;
		}

		if (!XjWorldSchemaGuard.AcceptArchive(data))
		{
			protectedRetentionBaseline = null;
			lastWrittenRevision = pendingWriteRevision;
			hasLoadedReadOnly = true;
			return;
		}

		XjWorldArchiveMemory.ImportToMemory(data);
		// Seed from the decoded archive so an immediate save always has a complete
		// fallback object, then force section refreshes because import/backfill paths
		// are allowed to normalize runtime state without emitting mutation events.
		XjWorldArchiveSnapshotCache.SeedLoadedSnapshot(data);
		// 只保留家族与高境不可逆事实的轻量基线，不再常驻完整归档对象。
		protectedRetentionBaseline = BuildProtectedRetentionBaseline(data);
		lastWrittenRevision = pendingWriteRevision;
		hasLoadedReadOnly = true;
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0) return;

		LoadReadOnly();
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled) return;

		// Projection is cooperative: one background pass materializes at most the
		// requested number of sections. This work is safe at high simulation speed
		// because no JSON encoding or world-container write occurs here.
		if (XjWorldArchiveSnapshotCache.HasDirtySections)
		{
			XjWorldArchiveSnapshotCache.PrepareNextSections(budget);
		}

		if (pendingWriteRevision <= lastWrittenRevision)
		{
			urgentCommitRequested = false;
			firstDirtyAt = -1f;
			protectedRequestedAt = -1f;
			return;
		}

		// Keep the allocation-heavy merge + monolithic JSON encoding in the old
		// low-speed quiet-frame gate. SaveManager can still force it synchronously.
		if (XjWorldArchiveSnapshotCache.HasDirtySections
			|| !IsCommitDue(UnityEngine.Time.unscaledTime)
			|| !XjRuntimeWorkBudget.CanRunExpensiveSynchronousMaintenance)
		{
			return;
		}

		TryCommitPreparedSnapshot(forceComplete: false);
	}

	internal static void Clear()
	{
		pendingWriteRevision = 0L;
		lastWrittenRevision = 0L;
		urgentCommitRequested = false;
		firstDirtyAt = -1f;
		protectedRequestedAt = -1f;
		lastCommitAt = -1000f;
		hasLoadedReadOnly = false;
		protectedRetentionBaseline = null;
		unresolvedContainerAttempts = 0;
		firstUnresolvedContainerAt = -1f;
		nextUnresolvedContainerProbeAt = -1f;
		cachedWorld = null;
		cachedWorldSeedId = int.MinValue;
		XjNativeWorldArchiveInterop.ClearRuntimeCache();
		cachedEncodedArchive = string.Empty;
		cachedEncodedSnapshotRevision = -1L;
		cachedEncodedArchiveCommitted = false;
		ResetDiagnostics();
		XjWorldArchiveSnapshotCache.Clear();
		XjModuleArchiveRegistry.Clear();
		XjWorldSchemaGuard.Clear();
	}

	internal static bool EnsureLoadedForWorldSnapshot()
	{
		// SaveManager 已进入正式世界快照阶段，此时可以确认迟到的加载已结束；
		// 若仍无 custom_data，按新档初始化。这个入口只建立/导入归档权威，
		// 不编码也不提交 JSON，供 bootstrap 在保存前先收束到稳定边界。
		LoadReadOnly(allowInitializeEmpty: true);
		return hasLoadedReadOnly;
	}

	internal static void CommitPendingBeforeWorldSnapshot()
	{
		EnsureLoadedForWorldSnapshot();
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}
		// Save snapshots preserve strong consistency by synchronously completing every
		// still-dirty section. Unknown MarkChanged callers invalidate All, so an
		// unclassified mutation falls back to the former full-export behavior.
		TryCommitPreparedSnapshot(forceComplete: true);
	}

	private static bool IsCommitDue(float now)
	{
		if (!HasPendingWrite || pendingWriteRevision <= lastWrittenRevision)
		{
			return false;
		}

		float minimumInterval = urgentCommitRequested
			? ResolveProtectedCommitIntervalSeconds()
			: ResolveMinimumCommitIntervalSeconds();
		if (now - lastCommitAt < minimumInterval)
		{
			return false;
		}

		if (urgentCommitRequested)
		{
			float requestedAt = protectedRequestedAt >= 0f ? protectedRequestedAt : now;
			return now - requestedAt >= ResolveProtectedCommitIntervalSeconds();
		}

		float dirtyAt = firstDirtyAt >= 0f ? firstDirtyAt : now;
		return now - dirtyAt >= ResolveNormalCommitDelaySeconds();
	}

	private static float ResolveNormalCommitDelaySeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 240f,
			XjRuntimeStressTier.Severe => 180f,
			XjRuntimeStressTier.Mild => 150f,
			_ => NormalCommitDelaySeconds
		};
	}

	private static float ResolveProtectedCommitIntervalSeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 120f,
			XjRuntimeStressTier.Severe => 90f,
			XjRuntimeStressTier.Mild => 60f,
			_ => ProtectedCommitDelaySeconds
		};
	}

	private static float ResolveMinimumCommitIntervalSeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 180f,
			XjRuntimeStressTier.Severe => 120f,
			XjRuntimeStressTier.Mild => 90f,
			_ => MinimumCommitIntervalSeconds
		};
	}

	private static bool TryCommitPreparedSnapshot(bool forceComplete)
	{
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return false;
		}

		try
		{
			// A previously successful commit already lives in WorldBox custom_data. If no
			// archive mutation or forced dirty section has appeared since then, SaveManager
			// can snapshot that exact string without making us merge/encode/write it again.
			if (forceComplete
				&& cachedEncodedArchiveCommitted
				&& !HasPendingWrite
				&& !XjWorldArchiveSnapshotCache.HasDirtySections
				&& cachedEncodedSnapshotRevision == XjWorldArchiveSnapshotCache.StableRevision)
			{
				XjPerformanceTelemetry.ObserveQueue("archiveCommitReuse", 1);
				return true;
			}

			long prepareStarted = XjRuntimeDiagnostics.BeginNamedSample();
			XjWorldArchiveData current = forceComplete
				? XjWorldArchiveSnapshotCache.PrepareAllSections()
				: XjWorldArchiveSnapshotCache.GetPreparedSnapshot();
			XjRuntimeDiagnostics.EndNamedSample("archive.commit.prepareRemaining", prepareStarted);
			if (current == null || XjWorldArchiveSnapshotCache.HasDirtySections)
			{
				return false;
			}

			long stableRevision = XjWorldArchiveSnapshotCache.StableRevision;
			string previousCommittedRawCache = cachedEncodedArchiveCommitted
				? cachedEncodedArchive
				: string.Empty;
			string raw;
			if (stableRevision > 0L
				&& cachedEncodedSnapshotRevision == stableRevision
				&& !string.IsNullOrEmpty(cachedEncodedArchive))
			{
				raw = cachedEncodedArchive;
				XjPerformanceTelemetry.ObserveQueue("archiveEncodeCacheHit", 1);
			}
			else
			{
				XjWorldArchiveSection changedSections = XjWorldArchiveSnapshotCache.ChangedSectionsSinceCommit;
				const XjWorldArchiveSection familyProtectedSections =
					XjWorldArchiveSection.Family | XjWorldArchiveSection.Warehouses;
				const XjWorldArchiveSection highRealmProtectedSections =
					XjWorldArchiveSection.HighRealm | XjWorldArchiveSection.Cultivation | XjWorldArchiveSection.Events;

				if (protectedRetentionBaseline != null && (changedSections & familyProtectedSections) != 0)
				{
					long familyMergeStarted = XjRuntimeDiagnostics.BeginNamedSample();
					current = XjWorldArchiveFamilyRetention.MergeProtectedFamilyData(protectedRetentionBaseline, current);
					XjRuntimeDiagnostics.EndNamedSample("archive.commit.familyRetention", familyMergeStarted);
				}
				if (protectedRetentionBaseline != null && (changedSections & highRealmProtectedSections) != 0)
				{
					long highRealmMergeStarted = XjRuntimeDiagnostics.BeginNamedSample();
					current = XjWorldArchiveHighRealmRetention.MergeProtectedHighRealmData(protectedRetentionBaseline, current);
					XjRuntimeDiagnostics.EndNamedSample("archive.commit.highRealmRetention", highRealmMergeStarted);
				}

				long encodeStarted = XjRuntimeDiagnostics.BeginNamedSample();
				raw = XjWorldArchiveCodec.Encode(current);
				XjRuntimeDiagnostics.EndNamedSample("archive.commit.encode", encodeStarted);
				cachedEncodedArchive = raw ?? string.Empty;
				cachedEncodedSnapshotRevision = stableRevision;
				cachedEncodedArchiveCommitted = false;
			}

			// Noisy mutation notifications are allowed. If materialization proves that the
			// encoded archive is byte-for-byte identical to the last successful write, the
			// current WorldBox custom_data already contains the desired state. Advance the
			// revisions without rewriting either primary or backup.
			if (!string.IsNullOrEmpty(previousCommittedRawCache)
				&& string.Equals(previousCommittedRawCache, raw, StringComparison.Ordinal))
			{
				XjPerformanceTelemetry.ObserveQueue("archiveNoOpCommit", 1);
				CompleteSuccessfulCommit(current);
				return true;
			}

			// Prefer the previous successful encoded payload already retained by the cache.
			// This is the exact immutable string written to custom_data, so reading the same
			// potentially large value back through reflection is unnecessary. After a failed
			// write or first load there is no trusted cache and we fall back to the container.
			long readPreviousStarted = XjRuntimeDiagnostics.BeginNamedSample();
			string previousPrimaryRaw = previousCommittedRawCache;
			if (string.IsNullOrEmpty(previousPrimaryRaw)
				&& XjNativeWorldArchiveInterop.TryGetDataObject(out object dataObject, createIfMissing: true))
			{
				XjNativeWorldArchiveInterop.TryReadString(dataObject, ArchiveKey, out previousPrimaryRaw);
			}
			else if (!string.IsNullOrEmpty(previousPrimaryRaw))
			{
				XjPerformanceTelemetry.ObserveQueue("archivePreviousRawCacheHit", 1);
			}
			XjRuntimeDiagnostics.EndNamedSample("archive.commit.readPrevious", readPreviousStarted);
			bool hadPreviousPrimary = !string.IsNullOrWhiteSpace(previousPrimaryRaw);
			bool previousDiffers = hadPreviousPrimary
				&& !string.Equals(previousPrimaryRaw, raw, StringComparison.Ordinal);
			if (previousDiffers)
			{
				long backupWriteStarted = XjRuntimeDiagnostics.BeginNamedSample();
				bool backupWritten = XjNativeWorldArchiveInterop.TrySetString(ArchiveBackupKey, previousPrimaryRaw);
				XjRuntimeDiagnostics.EndNamedSample("archive.commit.writeBackup", backupWriteStarted);
				if (!backupWritten) return false;
			}

			long primaryWriteStarted = XjRuntimeDiagnostics.BeginNamedSample();
			bool primaryWritten = XjNativeWorldArchiveInterop.TrySetString(ArchiveKey, raw);
			XjRuntimeDiagnostics.EndNamedSample("archive.commit.writePrimary", primaryWriteStarted);
			if (!primaryWritten) return false;

			// 首次保存或上次备份写入失败后，下一次提交仍必须补齐备份，
			// 不能因为主档内容已相同就把“无备份”误判为提交完成。
			if (!hadPreviousPrimary || !previousDiffers)
			{
				long ensureBackupStarted = XjRuntimeDiagnostics.BeginNamedSample();
				string existingBackup = string.Empty;
				bool hasBackup = XjNativeWorldArchiveInterop.TryGetDataObject(out object backupDataObject, createIfMissing: true)
					&& XjNativeWorldArchiveInterop.TryReadString(backupDataObject, ArchiveBackupKey, out existingBackup)
					&& !string.IsNullOrWhiteSpace(existingBackup);
				bool backupEnsured = hasBackup || XjNativeWorldArchiveInterop.TrySetString(ArchiveBackupKey, raw);
				XjRuntimeDiagnostics.EndNamedSample("archive.commit.ensureBackup", ensureBackupStarted);
				if (!backupEnsured) return false;
			}

			CompleteSuccessfulCommit(current);
			return true;
		}
		catch (Exception ex)
		{
			if (!hasLoggedCommitFailure)
			{
				hasLoggedCommitFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 导出或编码完整归档失败，已保留上一次成功档：" + ex);
			}
			return false;
		}
	}

	private static void CompleteSuccessfulCommit(XjWorldArchiveData current)
	{
		long retentionBaselineStarted = XjRuntimeDiagnostics.BeginNamedSample();
		protectedRetentionBaseline = BuildProtectedRetentionBaseline(current);
		XjRuntimeDiagnostics.EndNamedSample("archive.commit.rebuildRetentionBaseline", retentionBaselineStarted);
		XjWorldArchiveSnapshotCache.AcknowledgeCommittedSnapshot();
		cachedEncodedArchiveCommitted = true;
		lastWrittenRevision = pendingWriteRevision;
		urgentCommitRequested = false;
		firstDirtyAt = -1f;
		protectedRequestedAt = -1f;
		lastCommitAt = UnityEngine.Time.unscaledTime;
		hasLoadedReadOnly = true;
	}

	private static XjWorldArchiveData BuildProtectedRetentionBaseline(XjWorldArchiveData source)
	{
		if (source == null) return null;
		// Snapshot sections replace their DTO collections on mutation. The last
		// successfully written protected collections can therefore be retained by
		// reference as a copy-on-write baseline instead of replaying every retention
		// merge into an empty archive after each save. This keeps the same historical
		// safety boundary without a second O(N) indexing pass at the save boundary.
		XjWorldArchiveData compact = new XjWorldArchiveData();
		XjWorldArchiveFamilyRetention.CaptureProtectedBaseline(source, compact);
		XjWorldArchiveHighRealmRetention.CaptureProtectedBaseline(source, compact);
		return compact;
	}

	private static int ResolveWorldYearSafe()
	{
		try
		{
			return Math.Max(0, World.world?.map_stats?.year ?? 0);
		}
		catch (System.Exception xjCaught362_2)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Archive/XjWorldArchiveSystem.cs:362", xjCaught362_2);
			
			return 0;
		}
	}

	private static void EnsureWorldIdentity()
	{
		MapBox world = World.world;
		int seedId;
		try
		{
			seedId = world == null ? int.MinValue : MapBox.current_world_seed_id;
		}
		catch (System.Exception xjCaught545_6)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Archive/XjWorldArchiveSystem.cs:545", xjCaught545_6);
			
			seedId = int.MinValue;
		}

		if (ReferenceEquals(cachedWorld, world) && cachedWorldSeedId == seedId)
		{
			return;
		}

		cachedWorld = world;
		cachedWorldSeedId = seedId;
		hasLoadedReadOnly = false;
		protectedRetentionBaseline = null;
		unresolvedContainerAttempts = 0;
		firstUnresolvedContainerAt = -1f;
		nextUnresolvedContainerProbeAt = -1f;
		lastWrittenRevision = 0L;
		cachedEncodedArchive = string.Empty;
		cachedEncodedSnapshotRevision = -1L;
		cachedEncodedArchiveCommitted = false;
		XjNativeWorldArchiveInterop.ClearRuntimeCache();
		ResetDiagnostics();
	}

	private static void ResetDiagnostics()
	{
		hasLoggedReadFailure = false;
		hasLoggedCommitFailure = false;
	}

}
