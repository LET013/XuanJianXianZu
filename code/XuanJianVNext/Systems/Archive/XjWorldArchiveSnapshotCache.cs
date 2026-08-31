using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;

namespace XuanJianVNext.Systems.Archive;

[Flags]
internal enum XjWorldArchiveSection
{
	None = 0,
	Runtime = 1 << 0,
	Family = 1 << 1,
	Warehouses = 1 << 2,
	Sect = 1 << 3,
	HighRealm = 1 << 4,
	Cultivation = 1 << 5,
	Realms = 1 << 6,
	Alchemy = 1 << 7,
	Craft = 1 << 8,
	History = 1 << 9,
	Events = 1 << 10,
	Modules = 1 << 11,
	All = (1 << 12) - 1
}

/// <summary>
/// Revision-aware world archive materialization cache.
///
/// Archive writes are still synchronous at the WorldBox save boundary, but the
/// expensive registry -> DTO projection is no longer forced to happen as one
/// monolithic pass. Dirty sections are rebuilt opportunistically on quiet frames.
/// A save snapshot only completes the sections that are still dirty and then
/// performs the unavoidable retention merge + JSON encode.
///
/// Existing parameterless MarkChanged callsites are classified by compiler-provided
/// caller file path. Unknown callers intentionally fall back to All so this cache
/// cannot silently trade persistence correctness for speed while migration is
/// incomplete.
/// </summary>
internal static class XjWorldArchiveSnapshotCache
{
	private static readonly XjWorldArchiveSection[] OrderedSectionArray =
	{
		XjWorldArchiveSection.Runtime,
		XjWorldArchiveSection.Family,
		XjWorldArchiveSection.Warehouses,
		XjWorldArchiveSection.Sect,
		XjWorldArchiveSection.HighRealm,
		XjWorldArchiveSection.Cultivation,
		XjWorldArchiveSection.Realms,
		XjWorldArchiveSection.Alchemy,
		XjWorldArchiveSection.Craft,
		XjWorldArchiveSection.History,
		XjWorldArchiveSection.Events,
		XjWorldArchiveSection.Modules
	};

	private static readonly Dictionary<string, XjWorldArchiveSection> CallerSectionCache =
		new Dictionary<string, XjWorldArchiveSection>(StringComparer.Ordinal);

	private static XjWorldArchiveData _snapshot = new XjWorldArchiveData();
	private static XjWorldArchiveSection _dirtySections = XjWorldArchiveSection.All;
	private static readonly long[] SectionRevisions = new long[OrderedSectionArray.Length];
	private static long _mutationRevision;
	private static XjWorldArchiveSection _changedSectionsSinceCommit = XjWorldArchiveSection.All;
	private static int _cursor;

	internal static IReadOnlyList<XjWorldArchiveSection> OrderedSections => OrderedSectionArray;
	internal static bool HasDirtySections => _dirtySections != XjWorldArchiveSection.None;
	internal static int DirtySectionCount => CountBits(_dirtySections);
	internal static long StableRevision => _dirtySections == XjWorldArchiveSection.None ? _mutationRevision : -1L;
	internal static XjWorldArchiveSection ChangedSectionsSinceCommit => _changedSectionsSinceCommit;

	internal static void MarkDirty(XjWorldArchiveSection sections)
	{
		sections &= XjWorldArchiveSection.All;
		if (sections == XjWorldArchiveSection.None) return;

		long revision = ++_mutationRevision;
		_dirtySections |= sections;
		_changedSectionsSinceCommit |= sections;
		for (int index = 0; index < OrderedSectionArray.Length; index++)
		{
			if ((sections & OrderedSectionArray[index]) != 0)
			{
				SectionRevisions[index] = revision;
			}
		}
	}

	internal static XjWorldArchiveSection DirtySectionsForCaller(string callerFilePath)
	{
		return ResolveSectionsForCaller(callerFilePath);
	}

	internal static int PrepareNextSections(int maximumSections)
	{
		if (maximumSections <= 0 || _dirtySections == XjWorldArchiveSection.None)
		{
			return 0;
		}

		int prepared = 0;
		int visited = 0;
		while (prepared < maximumSections
			&& _dirtySections != XjWorldArchiveSection.None
			&& visited < OrderedSectionArray.Length)
		{
			if (_cursor >= OrderedSectionArray.Length) _cursor = 0;
			XjWorldArchiveSection section = OrderedSectionArray[_cursor++];
			visited++;
			if ((_dirtySections & section) == 0) continue;
			if (!TryPrepareSection(section)) break;
			prepared++;
			visited = 0;
		}
		return prepared;
	}

	internal static XjWorldArchiveData PrepareAllSections()
	{
		int guard = OrderedSectionArray.Length * 2;
		while (_dirtySections != XjWorldArchiveSection.None && guard-- > 0)
		{
			int before = DirtySectionCount;
			PrepareNextSections(Math.Max(1, before));
			if (DirtySectionCount >= before)
			{
				break;
			}
		}
		return _snapshot;
	}

	internal static XjWorldArchiveData GetPreparedSnapshot()
	{
		return _snapshot;
	}

	internal static void SeedLoadedSnapshot(XjWorldArchiveData loaded)
	{
		// Reuse the decoded object as a safe starting snapshot, but force every section
		// through runtime export before it is trusted for a new write. Import paths may
		// normalize or backfill state without issuing persistence mutation events.
		_snapshot = loaded ?? new XjWorldArchiveData();
		_dirtySections = XjWorldArchiveSection.All;
		_changedSectionsSinceCommit = XjWorldArchiveSection.All;
		long revision = ++_mutationRevision;
		for (int index = 0; index < OrderedSectionArray.Length; index++)
		{
			SectionRevisions[index] = revision;
		}
		_cursor = 0;
	}

	internal static void AcknowledgeCommittedSnapshot()
	{
		if (_dirtySections == XjWorldArchiveSection.None)
		{
			_changedSectionsSinceCommit = XjWorldArchiveSection.None;
		}
	}

	internal static void Clear()
	{
		_snapshot = new XjWorldArchiveData();
		_dirtySections = XjWorldArchiveSection.All;
		_changedSectionsSinceCommit = XjWorldArchiveSection.All;
		_mutationRevision = 1L;
		for (int index = 0; index < OrderedSectionArray.Length; index++)
		{
			SectionRevisions[index] = _mutationRevision;
		}
		_cursor = 0;
		CallerSectionCache.Clear();
	}

	private static bool TryPrepareSection(XjWorldArchiveSection section)
	{
		int sectionIndex = FindSectionIndex(section);
		if (sectionIndex < 0) return false;
		long targetRevision = SectionRevisions[sectionIndex];
		long started = XjRuntimeDiagnostics.BeginNamedSample();
		try
		{
			XjWorldArchiveMemory.ExportSection(_snapshot, section);
			// Exporters are expected to be read-only, but several legacy serializers
			// normalize data on access. If such a path emits MarkChanged re-entrantly,
			// never clear the newer dirty revision with an older materialization.
			if (SectionRevisions[sectionIndex] == targetRevision)
			{
				_dirtySections &= ~section;
			}
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("archive-section-export:" + section, ex);
			return false;
		}
		finally
		{
			XjRuntimeDiagnostics.EndNamedSample("archive.section." + section, started);
			XjPerformanceTelemetry.ObserveQueue("archiveDirtySections", DirtySectionCount);
		}
	}

	private static XjWorldArchiveSection ResolveSectionsForCaller(string callerFilePath)
	{
		string key = callerFilePath ?? string.Empty;
		if (CallerSectionCache.TryGetValue(key, out XjWorldArchiveSection cached))
		{
			return cached;
		}

		string path = key.Replace('\\', '/');
		XjWorldArchiveSection sections;
		if (Contains(path, "/Systems/Alchemy/"))
		{
			sections = XjWorldArchiveSection.Alchemy;
		}
		else if (Contains(path, "/Systems/Craft/"))
		{
			sections = XjWorldArchiveSection.Craft;
		}
		else if (Contains(path, "/Systems/Warehouse/") || Contains(path, "/Systems/ZongMen/"))
		{
			sections = XjWorldArchiveSection.Warehouses | XjWorldArchiveSection.Sect;
		}
		else if (Contains(path, "/Systems/Sect/")
			|| Contains(path, "/Systems/Formation/"))
		{
			sections = XjWorldArchiveSection.Sect;
		}
		else if (Contains(path, "/Systems/History/") || Contains(path, "/Systems/Chronicle/"))
		{
			sections = XjWorldArchiveSection.History | XjWorldArchiveSection.Family;
		}
		else if (Contains(path, "/Systems/DongTian/") || Contains(path, "/Systems/QianKunDai/"))
		{
			sections = XjWorldArchiveSection.Realms;
		}
		// ModuleDocuments are no longer dirtied by every mutation in a broad domain.
		// Existing module contributors were audited to their actual owner files; only
		// those owners invalidate Modules. Unknown/future callers still fall back to the
		// broad safe path below or can call XjWorldArchiveSystem.MarkModuleChanged(id).
		else if (Contains(path, "/Systems/HighRealm/XjFruitPositionWorldState")
			|| Contains(path, "/Systems/HighRealm/XjDaoTaiPresenceArchive")
			|| Contains(path, "/Systems/HighRealm/XjYuYiXianRegistry")
			|| Contains(path, "/Systems/HighRealm/XjDaoLineageStateRegistry"))
		{
			sections = XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Systems/HighRealm/")
			|| Contains(path, "/Systems/DengMingShi/"))
		{
			sections = XjWorldArchiveSection.HighRealm;
		}
		else if (Contains(path, "/Systems/Cultivation/XjFuQiSwordWorldState"))
		{
			sections = XjWorldArchiveSection.Cultivation | XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Systems/Cultivation/")
			|| Contains(path, "/Systems/WeaponArt/")
			|| Contains(path, "/Systems/Combat/XjDaoTuCounterState"))
		{
			sections = XjWorldArchiveSection.Cultivation;
		}
		else if (Contains(path, "/Systems/Family/XjFamilySurnameRegistry"))
		{
			sections = XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Systems/Family/"))
		{
			sections = XjWorldArchiveSection.Family;
		}
		else if (Contains(path, "/Systems/Shi/XjShiDomainState")
			|| Contains(path, "/Systems/Shi/XjShiFruitPositionLockSystem")
			|| Contains(path, "/Systems/Shi/XjAncientShiTempleSystem"))
		{
			sections = XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Systems/Shi/XjShiOpeningPrologueSystem"))
		{
			sections = XjWorldArchiveSection.Events;
		}
		else if (Contains(path, "/Systems/Shi/"))
		{
			sections = XjWorldArchiveSection.Events | XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Systems/Events/") || Contains(path, "/Systems/LongShu/"))
		{
			sections = XjWorldArchiveSection.Events;
		}
		else if (Contains(path, "/Systems/Doctrine/") || Contains(path, "/Systems/XianGuo/"))
		{
			sections = XjWorldArchiveSection.Modules;
		}
		else if (Contains(path, "/Core/XjRuntimeCadence") || Contains(path, "/Systems/Runtime/"))
		{
			sections = XjWorldArchiveSection.Runtime;
		}
		else
		{
			// Safe fallback during migration: an unclassified mutation invalidates all
			// sections, preserving the old full-export correctness boundary.
			sections = XjWorldArchiveSection.All;
		}

		CallerSectionCache[key] = sections;
		return sections;
	}

	private static int FindSectionIndex(XjWorldArchiveSection section)
	{
		for (int index = 0; index < OrderedSectionArray.Length; index++)
		{
			if (OrderedSectionArray[index] == section) return index;
		}
		return -1;
	}

	private static bool Contains(string source, string token)
	{
		return source.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static int CountBits(XjWorldArchiveSection value)
	{
		int bits = (int)value;
		int count = 0;
		while (bits != 0)
		{
			count += bits & 1;
			bits >>= 1;
		}
		return count;
	}
}
