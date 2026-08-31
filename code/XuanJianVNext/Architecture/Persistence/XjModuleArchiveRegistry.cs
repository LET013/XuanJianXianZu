using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Architecture.Persistence;

internal sealed class XjModuleArchiveContributor
{
	internal string ModuleId { get; }
	internal int Order { get; }
	internal int CurrentSchemaVersion { get; }
	internal Func<string> ExportPayload { get; }
	internal Action<int, string> ImportPayload { get; }

	internal XjModuleArchiveContributor(
		string moduleId,
		int order,
		int currentSchemaVersion,
		Func<string> exportPayload,
		Action<int, string> importPayload)
	{
		ModuleId = string.IsNullOrWhiteSpace(moduleId)
			? throw new ArgumentException("Module archive id is required.", nameof(moduleId))
			: moduleId.Trim();
		Order = order;
		CurrentSchemaVersion = Math.Max(1, currentSchemaVersion);
		ExportPayload = exportPayload ?? throw new ArgumentNullException(nameof(exportPayload));
		ImportPayload = importPayload ?? throw new ArgumentNullException(nameof(importPayload));
	}
}

/// <summary>
/// Per-feature persistence registry inspired by document-based save systems.
/// Unknown documents are retained verbatim so a temporarily disabled optional
/// feature cannot erase its state on the next save.
/// </summary>
internal static class XjModuleArchiveRegistry
{
	private readonly struct PayloadCacheEntry
	{
		internal readonly long Revision;
		internal readonly string Payload;

		internal PayloadCacheEntry(long revision, string payload)
		{
			Revision = revision;
			Payload = payload ?? string.Empty;
		}
	}

	private const int MaximumDocuments = 128;
	private const int MaximumPayloadCharacters = 1_000_000;
	private static readonly List<XjModuleArchiveContributor> Contributors =
		new List<XjModuleArchiveContributor>();
	private static readonly Dictionary<string, XjModuleArchiveContributor> ById =
		new Dictionary<string, XjModuleArchiveContributor>(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjModuleArchiveDocument> Passthrough =
		new Dictionary<string, XjModuleArchiveDocument>(StringComparer.Ordinal);
	private static readonly HashSet<string> FailedContributorImports =
		new HashSet<string>(StringComparer.Ordinal);
	private static readonly Dictionary<string, PayloadCacheEntry> PayloadCache =
		new Dictionary<string, PayloadCacheEntry>(StringComparer.Ordinal);
	private static readonly Dictionary<string, long> ModulePayloadRevisions =
		new Dictionary<string, long>(StringComparer.Ordinal);
	private static long _payloadMutationRevision = 1L;
	private static long _allPayloadRevision = 1L;
	private static bool _builtInsRegistered;
	private static bool _sealed;

	internal static int ContributorCount => Contributors.Count;
	internal static int PassthroughCount => Passthrough.Count;
	internal static int CachedPayloadCount => PayloadCache.Count;

	internal static void MarkModuleDirty(string moduleId)
	{
		string normalized = (moduleId ?? string.Empty).Trim();
		if (normalized.Length == 0)
		{
			MarkAllPayloadsDirty();
			return;
		}

		long revision = ++_payloadMutationRevision;
		ModulePayloadRevisions[normalized] = revision;
	}

	internal static void MarkDirtyForCaller(string callerFilePath)
	{
		string moduleId = ResolveModuleIdForCaller(callerFilePath);
		if (moduleId.Length == 0)
		{
			// Module section can be dirtied by legacy or third-party contributors that do
			// not have a stable file->module mapping. Fall back to invalidating every
			// module payload so persistence correctness never depends on this optimization.
			MarkAllPayloadsDirty();
			return;
		}
		MarkModuleDirty(moduleId);
	}

	internal static void Register(XjModuleArchiveContributor contributor)
	{
		if (contributor == null) throw new ArgumentNullException(nameof(contributor));
		if (_sealed) throw new InvalidOperationException("Module archive registry is sealed: " + contributor.ModuleId);
		if (ById.ContainsKey(contributor.ModuleId))
		{
			throw new InvalidOperationException("Duplicate module archive id: " + contributor.ModuleId);
		}
		ById.Add(contributor.ModuleId, contributor);
		Contributors.Add(contributor);
	}

	internal static void ExportDocuments(List<XjModuleArchiveDocument> target)
	{
		if (target == null) return;
		EnsureReady();
		target.Clear();

		List<string> passthroughIds = new List<string>(Passthrough.Keys);
		passthroughIds.Sort(StringComparer.Ordinal);
		foreach (string moduleId in passthroughIds)
		{
			if (target.Count >= MaximumDocuments) break;
			target.Add(Passthrough[moduleId].Clone());
		}

		foreach (XjModuleArchiveContributor contributor in Contributors)
		{
			if (FailedContributorImports.Contains(contributor.ModuleId)
				&& Passthrough.ContainsKey(contributor.ModuleId))
			{
				continue;
			}
			try
			{
				string payload = GetOrBuildPayload(contributor);
				if (payload.Length > MaximumPayloadCharacters)
				{
					throw new InvalidOperationException(
						"Module archive payload exceeds limit: " + contributor.ModuleId
						+ " chars=" + payload.Length);
				}
				ReplaceOrAdd(target, new XjModuleArchiveDocument
				{
					ModuleId = contributor.ModuleId,
					SchemaVersion = contributor.CurrentSchemaVersion,
					Payload = payload
				});
			}
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report(
					"Architecture/Persistence/module-export:" + contributor.ModuleId,
					ex);
				// Never publish a Modules section with one known contributor silently
				// missing. Let the outer archive section remain dirty so the previous
				// successful world archive stays authoritative and a later frame can retry.
				throw;
			}
		}
	}

	internal static void ImportDocuments(IEnumerable<XjModuleArchiveDocument> source)
	{
		EnsureReady();
		Passthrough.Clear();
		FailedContributorImports.Clear();
		PayloadCache.Clear();
		MarkAllPayloadsDirty();
		if (source == null) return;

		int accepted = 0;
		HashSet<string> seenModuleIds = new HashSet<string>(StringComparer.Ordinal);
		foreach (XjModuleArchiveDocument raw in source)
		{
			if (accepted >= MaximumDocuments) break;
			if (raw == null || string.IsNullOrWhiteSpace(raw.ModuleId)) continue;
			string moduleId = raw.ModuleId.Trim();
			if (!seenModuleIds.Add(moduleId))
			{
				UnityEngine.Debug.LogWarning("[玄鉴][模块存档] duplicate document ignored: " + moduleId);
				continue;
			}
			string payload = raw.Payload ?? string.Empty;
			if (payload.Length > MaximumPayloadCharacters)
			{
				UnityEngine.Debug.LogWarning(
					"[玄鉴][模块存档] ignored oversized payload: " + moduleId
					+ " chars=" + payload.Length);
				continue;
			}
			accepted++;

			if (!ById.TryGetValue(moduleId, out XjModuleArchiveContributor contributor))
			{
				Passthrough[moduleId] = raw.Clone();
				continue;
			}
			if (raw.SchemaVersion > contributor.CurrentSchemaVersion)
			{
				Passthrough[moduleId] = raw.Clone();
				FailedContributorImports.Add(moduleId);
				UnityEngine.Debug.LogWarning(
					"[玄鉴][模块存档] future schema retained read-only: " + moduleId
					+ " stored=" + raw.SchemaVersion
					+ " current=" + contributor.CurrentSchemaVersion);
				continue;
			}

			try
			{
				contributor.ImportPayload(Math.Max(0, raw.SchemaVersion), payload);
				FailedContributorImports.Remove(moduleId);
			}
			catch (Exception ex)
			{
				// Keep the raw document so a failed migration never destroys it.
				Passthrough[moduleId] = raw.Clone();
				FailedContributorImports.Add(moduleId);
				XjExceptionDiagnostics.Report(
					"Architecture/Persistence/module-import:" + moduleId,
					ex);
			}
		}
	}

	internal static void Clear()
	{
		Passthrough.Clear();
		FailedContributorImports.Clear();
		PayloadCache.Clear();
		ModulePayloadRevisions.Clear();
		_payloadMutationRevision++;
		_allPayloadRevision = _payloadMutationRevision;
	}

	private static string GetOrBuildPayload(XjModuleArchiveContributor contributor)
	{
		long targetRevision = ResolvePayloadRevision(contributor.ModuleId);
		if (PayloadCache.TryGetValue(contributor.ModuleId, out PayloadCacheEntry cached)
			&& cached.Revision == targetRevision)
		{
			XjPerformanceTelemetry.ObserveQueue("archiveModulePayloadCacheHit", 1);
			return cached.Payload;
		}

		long started = XjRuntimeDiagnostics.BeginNamedSample();
		string payload = contributor.ExportPayload() ?? string.Empty;
		XjRuntimeDiagnostics.EndNamedSample("archive.module." + contributor.ModuleId, started);
		// Exporters are expected to be read-only, but legacy normalization can emit
		// MarkChanged re-entrantly. Cache only if this module stayed at the same
		// revision during export; otherwise the enclosing Modules section remains dirty
		// and the next pass will rebuild the newer payload.
		if (ResolvePayloadRevision(contributor.ModuleId) == targetRevision)
		{
			PayloadCache[contributor.ModuleId] = new PayloadCacheEntry(targetRevision, payload);
		}
		return payload;
	}

	private static long ResolvePayloadRevision(string moduleId)
	{
		long revision = _allPayloadRevision;
		if (ModulePayloadRevisions.TryGetValue(moduleId, out long moduleRevision)
			&& moduleRevision > revision)
		{
			revision = moduleRevision;
		}
		return revision;
	}

	private static void MarkAllPayloadsDirty()
	{
		_allPayloadRevision = ++_payloadMutationRevision;
	}

	private static string ResolveModuleIdForCaller(string callerFilePath)
	{
		string path = (callerFilePath ?? string.Empty).Replace('\\', '/');
		if (Contains(path, "/Systems/Cultivation/XjFuQiSwordWorldState"))
			return "cultivation.fuqi-sword-world";
		if (Contains(path, "/Systems/HighRealm/XjFruitPositionWorldState"))
			return "world.fruit-position-domain";
		if (Contains(path, "/Systems/HighRealm/XjDaoTaiPresenceArchive"))
			return "highrealm.daotai-presence";
		if (Contains(path, "/Systems/HighRealm/XjYuYiXianRegistry"))
			return "highrealm.yuyi-xian";
		if (Contains(path, "/Systems/HighRealm/XjDaoLineageStateRegistry"))
			return "highrealm.dao-lineage";
		if (Contains(path, "/Systems/Shi/XjShiDomainState"))
			return "cultivation.shi-domain-world";
		if (Contains(path, "/Systems/Shi/XjShiFruitPositionLockSystem"))
			return "cultivation.shi-fruit-lock";
		if (Contains(path, "/Systems/Family/XjFamilySurnameRegistry"))
			return "family.surname-authority";
		if (Contains(path, "/Systems/Shi/XjAncientShiTempleSystem"))
			return "cultivation.ancient-shi-temples";
		if (Contains(path, "/Systems/Doctrine/"))
			return "world.doctrine-conflict";
		if (Contains(path, "/Systems/XianGuo/"))
			return "world.xianguo";
		return string.Empty;
	}

	private static bool Contains(string source, string token)
	{
		return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static void EnsureReady()
	{
		if (!_builtInsRegistered)
		{
			RegisterBuiltIns();
			_builtInsRegistered = true;
		}
		if (_sealed) return;
		Contributors.Sort((left, right) =>
		{
			int order = left.Order.CompareTo(right.Order);
			return order != 0 ? order : string.CompareOrdinal(left.ModuleId, right.ModuleId);
		});
		_sealed = true;
	}

	private static void RegisterBuiltIns()
	{
		// Dual-write one isolated subsystem during the transition. Legacy field data
		// remains supported; the module document wins when present.
		Register(new XjModuleArchiveContributor(
			"cultivation.fuqi-sword-world",
			10,
			1,
			() => JsonConvert.SerializeObject(XjFuQiSwordWorldState.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjFuQiSwordWorldArchiveData state =
					JsonConvert.DeserializeObject<XjFuQiSwordWorldArchiveData>(payload);
				if (state != null) XjFuQiSwordWorldState.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"world.fruit-position-domain",
			20,
			1,
			() => JsonConvert.SerializeObject(XjFruitPositionWorldState.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjFruitPositionWorldArchiveData state =
					JsonConvert.DeserializeObject<XjFruitPositionWorldArchiveData>(payload);
				if (state != null) XjFruitPositionWorldState.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"highrealm.daotai-presence",
			25,
			1,
			() => JsonConvert.SerializeObject(XjDaoTaiPresenceArchive.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjDaoTaiPresenceWorldArchiveData state =
					JsonConvert.DeserializeObject<XjDaoTaiPresenceWorldArchiveData>(payload);
				if (state != null) XjDaoTaiPresenceArchive.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"highrealm.yuyi-xian",
			30,
			1,
			() => JsonConvert.SerializeObject(XjYuYiXianRegistry.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjYuYiXianWorldArchiveData state =
					JsonConvert.DeserializeObject<XjYuYiXianWorldArchiveData>(payload);
				if (state != null) XjYuYiXianRegistry.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"highrealm.dao-lineage",
			35,
			1,
			() => JsonConvert.SerializeObject(XjDaoLineageStateRegistry.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjDaoLineageWorldArchiveData state =
					JsonConvert.DeserializeObject<XjDaoLineageWorldArchiveData>(payload);
				if (state != null) XjDaoLineageStateRegistry.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"cultivation.shi-domain-world",
			40,
			1,
			() => JsonConvert.SerializeObject(XjShiDomainState.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjShiDomainWorldArchiveData state =
					JsonConvert.DeserializeObject<XjShiDomainWorldArchiveData>(payload);
				if (state != null) XjShiDomainState.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"cultivation.shi-fruit-lock",
			45,
			1,
			() => JsonConvert.SerializeObject(XjShiFruitPositionLockSystem.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjShiFruitPositionLockArchiveData state =
					JsonConvert.DeserializeObject<XjShiFruitPositionLockArchiveData>(payload);
				if (state != null) XjShiFruitPositionLockSystem.ImportState(state);
			}));

		Register(new XjModuleArchiveContributor(
			"family.surname-authority",
			50,
			1,
			() => JsonConvert.SerializeObject(XjFamilySurnameRegistry.ExportState(), Formatting.None),
			(_, payload) =>
			{
				if (string.IsNullOrWhiteSpace(payload)) return;
				XjFamilySurnameWorldArchiveData state =
					JsonConvert.DeserializeObject<XjFamilySurnameWorldArchiveData>(payload);
				if (state != null) XjFamilySurnameRegistry.ImportState(state);
			}));

	}

	private static void ReplaceOrAdd(
		List<XjModuleArchiveDocument> target,
		XjModuleArchiveDocument document)
	{
		for (int index = 0; index < target.Count; index++)
		{
			if (string.Equals(target[index]?.ModuleId, document.ModuleId, StringComparison.Ordinal))
			{
				target[index] = document;
				return;
			}
		}
		if (target.Count < MaximumDocuments) target.Add(document);
	}
}
