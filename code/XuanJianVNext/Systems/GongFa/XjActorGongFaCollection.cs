using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.GongFa;

/// <summary>
/// 角色真实持有的功法集合。旧版只保存一部主功法，其余功法由仙基临时推导，
/// 会造成乾坤袋、人物栏、求金与读档结果不一致。本集合以 Actor 自定义数据持久化，
/// 最多保存五部功法，并保持一部功法至多映射一门仙基/神通。
/// </summary>
internal static class XjActorGongFaCollection
{
	internal const int CurrentVersion = 2;
	internal const int MaximumRecords = XjXianJiState.MaxCount;
	private const string SourceMigration = "旧档迁移";

	[JsonObject(MemberSerialization.OptIn)]
	private sealed class Store
	{
		[JsonProperty("v")]
		public int Version { get; set; }

		[JsonProperty("r")]
		public List<RecordDto> Records { get; set; } = new List<RecordDto>();
	}

	[JsonObject(MemberSerialization.OptIn)]
	private sealed class RecordDto
	{
		[JsonProperty("id")]
		public string StableId { get; set; } = string.Empty;

		[JsonProperty("n")]
		public string Name { get; set; } = string.Empty;

		[JsonProperty("g")]
		public int Grade { get; set; }

		[JsonProperty("d")]
		public string DaoTu { get; set; } = string.Empty;

		[JsonProperty("x")]
		public string MappedXianJi { get; set; } = string.Empty;

		[JsonProperty("s")]
		public string Source { get; set; } = string.Empty;

		[JsonProperty("p")]
		public bool IsPrimary { get; set; }

		[JsonProperty("y")]
		public int AcquiredYear { get; set; }
	}

	internal readonly struct Record
	{
		internal readonly string StableId;
		internal readonly string Name;
		internal readonly int Grade;
		internal readonly string DaoTu;
		internal readonly string MappedXianJi;
		internal readonly string Source;
		internal readonly bool IsPrimary;
		internal readonly int AcquiredYear;

		internal Record(string stableId, string name, int grade, string daoTu, string mappedXianJi, string source, bool isPrimary, int acquiredYear)
		{
			StableId = stableId ?? string.Empty;
			Name = name ?? string.Empty;
			Grade = grade;
			DaoTu = daoTu ?? string.Empty;
			MappedXianJi = mappedXianJi ?? string.Empty;
			Source = source ?? string.Empty;
			IsPrimary = isPrimary;
			AcquiredYear = Math.Max(0, acquiredYear);
		}
	}

	internal static bool TryReadStoredPrimary(Actor actor, out Record record)
	{
		record = default;
		if (actor?.data == null)
		{
			return false;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaCollectionVersion, out int version);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaCollectionJson, out string json);
		if (version < CurrentVersion)
		{
			return false;
		}
		Store store = Deserialize(json);
		if (store == null)
		{
			return false;
		}
		NormalizeStore(store);
		RecordDto primary = FindPrimary(store);
		if (!IsValid(primary))
		{
			return false;
		}
		record = ToRecord(primary);
		return true;
	}

	internal static IReadOnlyList<Record> ReadRecords(Actor actor)
	{
		Store store = ReadOrMigrate(actor);
		if (store.Records == null || store.Records.Count == 0)
		{
			return Array.Empty<Record>();
		}

		List<Record> result = new List<Record>(store.Records.Count);
		for (int i = 0; i < store.Records.Count && result.Count < MaximumRecords; i++)
		{
			RecordDto item = store.Records[i];
			if (!IsValid(item))
			{
				continue;
			}
			result.Add(ToRecord(item));
		}
		result.Sort(CompareRecords);
		return result;
	}

	internal static IReadOnlyList<XjGongFaInheritanceRecord> BuildInheritanceRecords(Actor actor, int minimumGrade)
	{
		IReadOnlyList<Record> records = ReadRecords(actor);
		if (records.Count == 0)
		{
			return Array.Empty<XjGongFaInheritanceRecord>();
		}

		List<XjGongFaInheritanceRecord> result = new List<XjGongFaInheritanceRecord>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			Record record = records[i];
			if (record.Grade < minimumGrade)
			{
				continue;
			}
			result.Add(new XjGongFaInheritanceRecord(
				true,
				record.Name,
				record.Grade,
				record.DaoTu,
				record.MappedXianJi,
				record.IsPrimary ? "当前功法" : record.Source));
		}
		return result;
	}

	internal static bool EnsureForXianJi(
		Actor actor,
		string xianJiId,
		int currentYear,
		string preferredName = "",
		string source = "仙基参悟")
	{
		string mapped = Normalize(xianJiId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(mapped))
		{
			return false;
		}

		Store store = ReadOrMigrate(actor);
		NormalizeStore(store);
		for (int i = 0; i < store.Records.Count; i++)
		{
			if (string.Equals(Normalize(store.Records[i].MappedXianJi), mapped, StringComparison.Ordinal))
			{
				return true;
			}
		}

		RecordDto primary = FindPrimary(store);
		XjXianJiState existingXianJi = XjXianJiAccessor.BuildState(actor);
		if (existingXianJi.Count == 0 && primary != null)
		{
			primary.MappedXianJi = mapped;
			primary.StableId = BuildStableId(mapped, true);
			return Write(actor, store);
		}

		if (store.Records.Count >= MaximumRecords)
		{
			return false;
		}

		string daoTu = ResolveDaoTu(actor, primary?.DaoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		string name = Normalize(preferredName);
		if (string.IsNullOrWhiteSpace(name) || ContainsName(store, name))
		{
			name = GenerateUniqueGrade5Name(actor, store, daoTu, mapped);
		}
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}

		bool makePrimary = primary == null;
		RecordDto created = new RecordDto
		{
			StableId = BuildStableId(mapped, makePrimary),
			Name = name,
			Grade = 5,
			DaoTu = daoTu,
			MappedXianJi = mapped,
			Source = string.IsNullOrWhiteSpace(source) ? "仙基参悟" : source.Trim(),
			IsPrimary = makePrimary,
			AcquiredYear = Math.Max(0, currentYear)
		};
		store.Records.Add(created);
		if (!Write(actor, store))
		{
			return false;
		}
		if (makePrimary)
		{
			WriteLegacyPrimary(actor, created);
		}
		return true;
	}

	internal static void UpsertPrimary(Actor actor, in XjGongFaState state, string mappedXianJi = "", string source = "")
	{
		if (actor?.data == null || !state.Found || !XjGongFaDefinition.IsValidGrade(state.Grade))
		{
			return;
		}

		Store store = ReadOrMigrate(actor);
		NormalizeStore(store);
		RecordDto primary = FindPrimary(store);
		if (primary == null)
		{
			primary = new RecordDto { IsPrimary = true };
			store.Records.Insert(0, primary);
		}

		string mapped = Normalize(mappedXianJi);
		if (string.IsNullOrWhiteSpace(mapped))
		{
			mapped = Normalize(primary.MappedXianJi);
		}
		if (string.IsNullOrWhiteSpace(mapped) && state.Grade >= 5)
		{
			XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
			if (XjXianJiCatalog.TryResolveMappedXianJi(state.DaoTu, state.Name, out string resolved)
				&& Contains(xianJi.Ids, resolved))
			{
				mapped = Normalize(resolved);
			}
			else if (xianJi.Ids != null && xianJi.Ids.Length > 0)
			{
				mapped = Normalize(xianJi.Ids[0]);
			}
		}

		primary.StableId = BuildStableId(mapped, true);
		primary.Name = Normalize(state.Name);
		primary.Grade = state.Grade;
		primary.DaoTu = Normalize(state.DaoTu);
		primary.MappedXianJi = mapped;
		primary.Source = string.IsNullOrWhiteSpace(source) ? Normalize(primary.Source) : source.Trim();
		primary.IsPrimary = true;
		for (int i = 0; i < store.Records.Count; i++)
		{
			if (!ReferenceEquals(store.Records[i], primary))
			{
				store.Records[i].IsPrimary = false;
			}
		}
		Write(actor, store);
	}

	internal static void UpdatePrimarySource(Actor actor, string source)
	{
		if (actor?.data == null)
		{
			return;
		}
		Store store = ReadOrMigrate(actor);
		RecordDto primary = FindPrimary(store);
		if (primary == null)
		{
			return;
		}
		primary.Source = Normalize(source);
		Write(actor, store);
	}

	internal static bool SetPrimaryMappedXianJi(Actor actor, string xianJiId)
	{
		string mapped = Normalize(xianJiId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(mapped))
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		RecordDto primary = FindPrimary(store);
		if (primary == null)
		{
			return false;
		}
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (!ReferenceEquals(item, primary)
				&& string.Equals(Normalize(item.MappedXianJi), mapped, StringComparison.Ordinal))
			{
				LogInvariant(actor, "拒绝覆盖已被其他功法占用的仙基映射：" + mapped);
				return false;
			}
		}
		primary.MappedXianJi = mapped;
		primary.StableId = BuildStableId(mapped, true);
		return Write(actor, store);
	}

	internal static bool PromoteBoundGrade5ToGrade6(Actor actor, string sourceName, string promotedName, string daoTu, string source)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(promotedName))
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		NormalizeStore(store);
		RecordDto selected = null;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (item.Grade == 5 && SameName(item.Name, sourceName))
			{
				selected = item;
				break;
			}
		}
		if (selected == null)
		{
			for (int i = 0; i < store.Records.Count; i++)
			{
				RecordDto item = store.Records[i];
				if (item.Grade == 5 && item.IsPrimary)
				{
					selected = item;
					break;
				}
			}
		}
		if (selected == null)
		{
			for (int i = 0; i < store.Records.Count; i++)
			{
				if (store.Records[i].Grade == 5)
				{
					selected = store.Records[i];
					break;
				}
			}
		}
		if (selected == null)
		{
			return false;
		}

		for (int i = 0; i < store.Records.Count; i++)
		{
			store.Records[i].IsPrimary = ReferenceEquals(store.Records[i], selected);
			if (!ReferenceEquals(store.Records[i], selected) && store.Records[i].Grade > 5)
			{
				store.Records[i].Grade = 5;
			}
		}
		selected.Name = Normalize(promotedName);
		selected.Grade = 6;
		selected.DaoTu = Normalize(daoTu);
		selected.Source = string.IsNullOrWhiteSpace(source) ? "求金法贯通" : source.Trim();
		selected.StableId = BuildStableId(selected.MappedXianJi, true);
		if (!Write(actor, store))
		{
			return false;
		}
		WriteLegacyPrimary(actor, selected);
		return true;
	}

	internal static bool HasFiveRealGrade5GongFa(Actor actor)
	{
		IReadOnlyList<Record> records = ReadRecords(actor);
		if (records.Count != MaximumRecords)
		{
			return false;
		}
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < records.Count; i++)
		{
			Record item = records[i];
			if (item.Grade != 5 || string.IsNullOrWhiteSpace(item.MappedXianJi) || !mapped.Add(item.MappedXianJi))
			{
				return false;
			}
		}
		return true;
	}

	internal static bool HasJinDanGongFaSet(Actor actor)
	{
		IReadOnlyList<Record> records = ReadRecords(actor);
		if (records.Count != MaximumRecords)
		{
			return false;
		}
		int grade6 = 0;
		int grade5 = 0;
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < records.Count; i++)
		{
			Record item = records[i];
			if (string.IsNullOrWhiteSpace(item.MappedXianJi) || !mapped.Add(item.MappedXianJi))
			{
				return false;
			}
			if (item.Grade == 6) grade6++;
			else if (item.Grade == 5) grade5++;
			else return false;
		}
		return grade6 == 1 && grade5 == 4;
	}

	internal static bool TryGetByMappedXianJi(Actor actor, string xianJiId, out Record record)
	{
		string mapped = Normalize(xianJiId);
		IReadOnlyList<Record> records = ReadRecords(actor);
		for (int i = 0; i < records.Count; i++)
		{
			if (string.Equals(records[i].MappedXianJi, mapped, StringComparison.Ordinal))
			{
				record = records[i];
				return true;
			}
		}
		record = default;
		return false;
	}

	internal static bool TryGetPrimary(Actor actor, out Record record)
	{
		record = default;
		Store store = ReadOrMigrate(actor);
		RecordDto primary = FindPrimary(store);
		if (!IsValid(primary))
		{
			return false;
		}
		record = ToRecord(primary);
		return true;
	}

	internal static bool IsConsistentWithDaoTu(Actor actor, string daoTu)
	{
		daoTu = Normalize(daoTu);
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		Store store = ReadOrMigrate(actor);
		if (store.Records.Count == 0 || FindPrimary(store) == null)
		{
			return false;
		}
		for (int i = 0; i < store.Records.Count; i++)
		{
			if (!string.Equals(Normalize(store.Records[i].DaoTu), daoTu, StringComparison.Ordinal))
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>
	/// Replaces the current primary technique without changing its XianJi mapping.
	/// Family borrowing must use this atomic boundary instead of writing the legacy
	/// primary first and changing the mapping in a second step.
	/// </summary>
	internal static bool TryReplacePrimaryForSameMapping(
		Actor actor,
		in XjGongFaState replacement,
		string expectedMappedXianJi,
		string source)
	{
		string expected = Normalize(expectedMappedXianJi);
		if (actor?.data == null || !replacement.Found || string.IsNullOrWhiteSpace(expected))
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		RecordDto primary = FindPrimary(store);
		if (primary == null || !string.Equals(Normalize(primary.MappedXianJi), expected, StringComparison.Ordinal))
		{
			return false;
		}
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (!ReferenceEquals(item, primary)
				&& string.Equals(Normalize(item.MappedXianJi), expected, StringComparison.Ordinal))
			{
				LogInvariant(actor, "家族借法映射冲突：" + expected);
				return false;
			}
		}

		primary.Name = Normalize(replacement.Name);
		primary.Grade = replacement.Grade;
		primary.DaoTu = Normalize(replacement.DaoTu);
		primary.Source = string.IsNullOrWhiteSpace(source) ? "家族借法" : source.Trim();
		primary.StableId = BuildStableId(expected, true);
		if (!Write(actor, store))
		{
			return false;
		}
		WriteLegacyPrimary(actor, primary);
		return true;
	}

	/// <summary>
	/// Reconciles the persistent collection after realm rollback, special-role setup,
	/// XianJi recovery and old-save migration. It preserves the primary technique,
	/// removes orphan secondary mappings and materializes missing real techniques.
	/// </summary>
	internal static bool ReconcileWithActor(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		string before = Serialize(store);
		int repairs = ReconcileStoreWithActor(actor, store, createMissing: false);
		if (repairs <= 0)
		{
			return false;
		}
		if (!Write(actor, store))
		{
			return false;
		}
		RecordDto primary = FindPrimary(store);
		if (primary != null)
		{
			WriteLegacyPrimary(actor, primary);
		}
		LogRepair(actor, reason, repairs, before);
		return true;
	}

	internal static bool ReconcileDaoTu(Actor actor, string daoTu, string source)
	{
		daoTu = Normalize(daoTu);
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		if (store.Records.Count == 0)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		Dictionary<string, string> renamed = new Dictionary<string, string>(StringComparer.Ordinal);
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		bool changed = false;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			string oldName = item.Name;
			if (!string.Equals(Normalize(item.DaoTu), daoTu, StringComparison.Ordinal))
			{
				changed = true;
			}
			item.DaoTu = daoTu;
			string candidate = string.Empty;
			long baseSeed = actorId + item.Grade * 1009L + XjDeterministicHash.StableHash(item.MappedXianJi + "|" + item.StableId);
			for (int attempt = 0; attempt < 16; attempt++)
			{
				candidate = XjGongFaNameLibrary.GenerateName(daoTu, item.Grade, baseSeed + attempt * 7919L);
				if (!string.IsNullOrWhiteSpace(candidate) && used.Add(NormalizeName(candidate)))
				{
					break;
				}
				candidate = string.Empty;
			}
			if (!string.IsNullOrWhiteSpace(candidate) && !SameName(candidate, oldName))
			{
				item.Name = candidate.Trim();
				changed = true;
			}
			renamed[NormalizeName(oldName)] = item.Name;
			string nextSource = string.IsNullOrWhiteSpace(source) ? "道途重定" : source.Trim();
			if (!string.Equals(Normalize(item.Source), Normalize(nextSource), StringComparison.Ordinal))
			{
				item.Source = nextSource;
				changed = true;
			}
		}
		if (changed && !Write(actor, store))
		{
			return false;
		}

		if (changed)
		{
			RecordDto primary = FindPrimary(store);
			if (primary != null)
			{
				WriteLegacyPrimary(actor, primary);
			}
		}
		bool qiuJinChanged = ReconcileQiuJinBindingAfterDaoTuChange(actor, daoTu, renamed, store);
		return changed || qiuJinChanged;
	}

	internal static bool ReplaceAllForManualDaoTu(
		Actor actor,
		string daoTu,
		string[] mappedXianJiIds,
		string source)
	{
		daoTu = Normalize(daoTu);
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu) || mappedXianJiIds == null)
		{
			return false;
		}

		List<string> mappings = new List<string>(MaximumRecords);
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < mappedXianJiIds.Length && mappings.Count < MaximumRecords; i++)
		{
			string mapped = Normalize(mappedXianJiIds[i]);
			if (!string.IsNullOrWhiteSpace(mapped) && seen.Add(mapped))
			{
				mappings.Add(mapped);
			}
		}
		if (mappings.Count == 0)
		{
			return ReconcileDaoTu(actor, daoTu, source);
		}

		Store store = ReadOrMigrate(actor);
		NormalizeStore(store);
		RecordDto oldPrimary = FindPrimary(store);
		List<RecordDto> ordered = new List<RecordDto>(MaximumRecords);
		if (oldPrimary != null)
		{
			ordered.Add(oldPrimary);
		}
		for (int i = 0; i < store.Records.Count && ordered.Count < MaximumRecords; i++)
		{
			RecordDto item = store.Records[i];
			if (!ReferenceEquals(item, oldPrimary))
			{
				ordered.Add(item);
			}
		}

		XjGongFaState legacy = XjGongFaAccessor.BuildState(actor);
		int primaryGrade = oldPrimary?.Grade > 0 ? oldPrimary.Grade : (legacy.Found ? legacy.Grade : 5);
		primaryGrade = Math.Max(1, Math.Min(XjGongFaDefinition.MaxGrade, primaryGrade));
		while (ordered.Count < mappings.Count)
		{
			ordered.Add(new RecordDto
			{
				Grade = ordered.Count == 0 ? primaryGrade : Math.Min(5, primaryGrade),
				IsPrimary = ordered.Count == 0,
				AcquiredYear = 0
			});
		}
		if (ordered.Count > mappings.Count)
		{
			ordered.RemoveRange(mappings.Count, ordered.Count - mappings.Count);
		}

		long actorId = ((BaseSystemData)actor.data).id;
		HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < ordered.Count; i++)
		{
			RecordDto item = ordered[i];
			string mapped = mappings[i];
			int grade = item.Grade > 0 ? item.Grade : (i == 0 ? primaryGrade : Math.Min(5, primaryGrade));
			grade = Math.Max(1, Math.Min(XjGongFaDefinition.MaxGrade, grade));
			string name = string.Empty;
			long seed = actorId + grade * 1009L + XjDeterministicHash.StableHash(mapped + "|manual_daotu");
			for (int attempt = 0; attempt < 32; attempt++)
			{
				string generated = XjGongFaNameLibrary.GenerateName(daoTu, grade, seed + attempt * 7919L);
				if (!string.IsNullOrWhiteSpace(generated) && usedNames.Add(NormalizeName(generated)))
				{
					name = generated.Trim();
					break;
				}
			}
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}
			item.Name = name;
			item.Grade = grade;
			item.DaoTu = daoTu;
			item.MappedXianJi = mapped;
			item.Source = string.IsNullOrWhiteSpace(source) ? "手动道途替换" : source.Trim();
			item.IsPrimary = i == 0;
			item.StableId = BuildStableId(mapped, item.IsPrimary);
		}
		store.Records = ordered;
		if (!Write(actor, store))
		{
			return false;
		}
		WriteLegacyPrimary(actor, ordered[0]);
		ReconcileQiuJinBindingAfterDaoTuChange(
			actor,
			daoTu,
			new Dictionary<string, string>(StringComparer.Ordinal),
			store);
		return true;
	}

	/// <summary>
	/// 原子改写指定神通对应的真实功法映射。未被替换的功法名称、品阶、主副顺序
	/// 与获得年份保持不变；只更新被神通造化命中的映射、道途与来源。
	/// </summary>
	internal static bool TryRemapXianJiMappings(
		Actor actor,
		string daoTu,
		IReadOnlyDictionary<string, string> replacements,
		string source,
		out int changedCount)
	{
		changedCount = 0;
		daoTu = Normalize(daoTu);
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu) || replacements == null || replacements.Count == 0)
		{
			return false;
		}

		Dictionary<string, string> normalizedReplacements = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string> pair in replacements)
		{
			string oldMapped = Normalize(pair.Key);
			string newMapped = Normalize(pair.Value);
			if (!string.IsNullOrWhiteSpace(oldMapped)
				&& !string.IsNullOrWhiteSpace(newMapped)
				&& !string.Equals(oldMapped, newMapped, StringComparison.Ordinal))
			{
				normalizedReplacements[oldMapped] = newMapped;
			}
		}
		if (normalizedReplacements.Count == 0)
		{
			return true;
		}

		Store store = ReadOrMigrate(actor);
		NormalizeStore(store);
		HashSet<string> finalMappings = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < store.Records.Count; i++)
		{
			string mapped = Normalize(store.Records[i].MappedXianJi);
			if (normalizedReplacements.TryGetValue(mapped, out string replacement))
			{
				mapped = replacement;
			}
			if (!string.IsNullOrWhiteSpace(mapped) && !finalMappings.Add(mapped))
			{
				LogInvariant(actor, "神通造化映射冲突：" + mapped);
				return false;
			}
		}

		string normalizedSource = string.IsNullOrWhiteSpace(source) ? "陆江仙模拟器·神通造化" : source.Trim();
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			string oldMapped = Normalize(item.MappedXianJi);
			if (!normalizedReplacements.TryGetValue(oldMapped, out string newMapped))
			{
				continue;
			}
			item.MappedXianJi = newMapped;
			item.DaoTu = daoTu;
			item.Source = normalizedSource;
			item.StableId = BuildStableId(newMapped, item.IsPrimary);
			changedCount++;
		}

		if (changedCount == 0)
		{
			return true;
		}
		if (!Write(actor, store))
		{
			changedCount = 0;
			return false;
		}
		RecordDto primary = FindPrimary(store);
		if (primary != null)
		{
			WriteLegacyPrimary(actor, primary);
		}
		return true;
	}

	internal static bool ClearDaoTuMetadata(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		bool collectionChanged = false;
		for (int i = 0; i < store.Records.Count; i++)
		{
			if (!string.IsNullOrWhiteSpace(store.Records[i].DaoTu))
			{
				store.Records[i].DaoTu = string.Empty;
				collectionChanged = true;
			}
		}
		if (collectionChanged && !Write(actor, store))
		{
			return false;
		}
		if (collectionChanged)
		{
			RecordDto primary = FindPrimary(store);
			if (primary != null)
			{
				WriteLegacyPrimary(actor, primary);
			}
		}

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		bool qiuJinChanged = qiuJinFa.Found || qiuJinFa.Ready
			|| !string.IsNullOrWhiteSpace(qiuJinFa.SourceDaoTu)
			|| !string.IsNullOrWhiteSpace(qiuJinFa.BoundAuthority);
		if (qiuJinChanged)
		{
			// 求金法不能脱离道途与真实源功法单独存在。清除道途时
			// 必须完整终止求金链，不能留下 Found=true/DaoTu=empty 的半状态。
			XjQiuJinFaAccessor.Clear(actor, string.IsNullOrWhiteSpace(reason) ? "DaoTuCleared" : reason);
		}
		if (collectionChanged || qiuJinChanged)
		{
			LogRepair(actor, reason, (collectionChanged ? 1 : 0) + (qiuJinChanged ? 1 : 0), string.Empty);
		}
		return collectionChanged || qiuJinChanged;
	}

	internal static bool ReconcileGradeCap(Actor actor, string reason)
	{
		if (actor?.data == null)
		{
			return false;
		}

		int cap = XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor);
		if (cap <= 0)
		{
			return false;
		}

		Store store = ReadOrMigrate(actor);
		HashSet<string> used = new HashSet<string>(StringComparer.Ordinal);
		long actorId = ((BaseSystemData)actor.data).id;
		bool changed = false;
		bool primaryDowngradedToFive = false;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			int targetGrade = Math.Min(item.Grade, cap);
			string candidate = item.Name;
			if (targetGrade != item.Grade)
			{
				if (item.IsPrimary && item.Grade >= 6 && targetGrade == 5)
				{
					primaryDowngradedToFive = true;
				}
				item.Grade = targetGrade;
				candidate = XjGongFaNameLibrary.NormalizeNameForGrade(item.Name, item.DaoTu, targetGrade);
				changed = true;
			}

			string normalizedCandidate = NormalizeName(candidate);
			if (string.IsNullOrWhiteSpace(candidate) || used.Contains(normalizedCandidate))
			{
				candidate = string.Empty;
				long seed = actorId
					+ targetGrade * 1009L
					+ XjDeterministicHash.StableHash(item.MappedXianJi + "|" + item.StableId + "|grade_cap");
				for (int attempt = 0; attempt < 16; attempt++)
				{
					string generated = XjGongFaNameLibrary.GenerateName(item.DaoTu, targetGrade, seed + attempt * 7919L);
					if (!string.IsNullOrWhiteSpace(generated) && used.Add(NormalizeName(generated)))
					{
						candidate = generated.Trim();
						break;
					}
				}
			}
			else
			{
				used.Add(normalizedCandidate);
			}

			if (!string.IsNullOrWhiteSpace(candidate) && !SameName(item.Name, candidate))
			{
				item.Name = candidate.Trim();
				changed = true;
			}
		}

		if (changed)
		{
			if (!Write(actor, store))
			{
				return false;
			}
			RecordDto primary = FindPrimary(store);
			if (primary != null)
			{
				WriteLegacyPrimary(actor, primary);
			}
			LogRepair(actor, reason, 1, string.Empty);
		}

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (qiuJinFa.Found && qiuJinFa.Ready)
		{
			if (cap < 5)
			{
				XjQiuJinFaAccessor.Clear(actor, "RealmGradeCapBelowQiuJin");
				return true;
			}
			if (primaryDowngradedToFive)
			{
				RecordDto primary = FindPrimary(store);
				if (primary != null)
				{
					string authority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
						primary.DaoTu,
						qiuJinFa.Name,
						string.Empty);
					XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
						true,
						qiuJinFa.Name,
						string.Empty,
						0,
						primary.DaoTu,
						true,
						qiuJinFa.LastYear,
						qiuJinFa.ReasonCode,
						authority));
					return true;
				}
			}
		}

		return changed;
	}

	internal static bool TryPrepareManualJinDanGrade5Set(
		Actor actor,
		string daoTu,
		string source,
		out Record sourceRecord)
	{
		sourceRecord = default;
		daoTu = Normalize(daoTu);
		if (actor?.data == null || string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		int manualRepairs = ReconcileStoreWithActor(actor, store, createMissing: true);
		if (manualRepairs > 0)
		{
			if (!Write(actor, store)) return false;
			RecordDto repairedPrimary = FindPrimary(store);
			if (repairedPrimary != null) WriteLegacyPrimary(actor, repairedPrimary);
			LogRepair(actor, "ManualJinDanPrepare", manualRepairs, string.Empty);
		}
		if (store.Records.Count != MaximumRecords)
		{
			return false;
		}
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		long actorId = ((BaseSystemData)actor.data).id;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (string.IsNullOrWhiteSpace(item.MappedXianJi) || !mapped.Add(item.MappedXianJi))
			{
				return false;
			}
			item.Grade = 5;
			item.DaoTu = daoTu;
			item.Source = string.IsNullOrWhiteSpace(source) ? "金丹境补录" : source.Trim();
			string candidate = XjGongFaNameLibrary.NormalizeNameForGrade(item.Name, daoTu, 5);
			if (string.IsNullOrWhiteSpace(candidate) || !names.Add(NormalizeName(candidate)))
			{
				candidate = string.Empty;
				long seed = actorId + XjDeterministicHash.StableHash(item.MappedXianJi + "|manual_jindan_grade5");
				for (int attempt = 0; attempt < 16; attempt++)
				{
					string generated = XjGongFaNameLibrary.GenerateName(daoTu, 5, seed + attempt * 7919L);
					if (!string.IsNullOrWhiteSpace(generated) && names.Add(NormalizeName(generated)))
					{
						candidate = generated.Trim();
						break;
					}
				}
			}
			if (string.IsNullOrWhiteSpace(candidate))
			{
				return false;
			}
			item.Name = candidate;
		}
		if (!Write(actor, store))
		{
			return false;
		}
		RecordDto primary = FindPrimary(store);
		if (primary == null)
		{
			return false;
		}
		WriteLegacyPrimary(actor, primary);
		sourceRecord = ToRecord(primary);
		return true;
	}

	internal static bool TryExportSerialized(Actor actor, out int version, out string json)
	{
		version = 0;
		json = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}
		Store store = ReadOrMigrate(actor);
		json = Serialize(store);
		version = CurrentVersion;
		return !string.IsNullOrWhiteSpace(json);
	}

	internal static bool TryRestoreSerialized(Actor actor, int version, string json, string reason)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(json))
		{
			return false;
		}
		Store store = Deserialize(json);
		if (store == null)
		{
			LogInvariant(actor, "登名石功法集合无法反序列化");
			return false;
		}
		store.Version = Math.Max(version, CurrentVersion);
		int repairs = NormalizeStoreWithRepairCount(store);
		repairs += ReconcileStoreWithActor(actor, store, createMissing: false);
		if (!Write(actor, store))
		{
			return false;
		}
		RecordDto primary = FindPrimary(store);
		if (primary != null)
		{
			WriteLegacyPrimary(actor, primary);
		}
		if (repairs > 0)
		{
			LogRepair(actor, reason, repairs, json);
		}
		return true;
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		Store store = new Store { Version = CurrentVersion, Records = new List<RecordDto>() };
		Write(actor, store);
	}

	private static Store ReadOrMigrate(Actor actor)
	{
		if (actor?.data == null)
		{
			return new Store { Version = CurrentVersion };
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaCollectionVersion, out int version);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaCollectionJson, out string json);
		Store store = Deserialize(json);
		bool migrated = version < CurrentVersion || store == null;
		store ??= new Store();
		string before = migrated ? Serialize(store) : string.Empty;
		if (migrated)
		{
			MigrateLegacy(actor, store);
		}
		int repairs = NormalizeStoreWithRepairCount(store);
		// 只在版本迁移时做一次跨模型对账。稳定读取若每次都扫描仙基与境界，
		// 会把人物栏、年度修炼和家族仓库的普通读取重新变成高人口热点；
		// 特殊生成、境界回退、登名石与道途切换均在各自写入口显式对账。
		if (migrated)
		{
			repairs += ReconcileStoreWithActor(actor, store, createMissing: false);
		}
		store.Version = CurrentVersion;
		if (migrated || repairs > 0)
		{
			if (!Write(actor, store))
			{
				LogInvariant(actor, migrated ? "旧档迁移结果无法持久化" : "读取修复结果无法持久化");
			}
			else if (repairs > 0)
			{
				LogRepair(actor, migrated ? "旧档迁移" : "读取修复", repairs, before);
			}
		}
		return store;
	}

	private static void MigrateLegacy(Actor actor, Store store)
	{
		store.Records ??= new List<RecordDto>();
		if (store.Records.Count > 0)
		{
			return;
		}

		XjGongFaState main = XjGongFaAccessor.BuildState(actor);
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		string primaryMapped = ResolveLegacyPrimaryMappedXianJi(actor, main, xianJi);
		if (main.Found && !string.IsNullOrWhiteSpace(main.Name))
		{
			store.Records.Add(new RecordDto
			{
				StableId = BuildStableId(primaryMapped, true),
				Name = Normalize(main.Name),
				Grade = main.Grade,
				DaoTu = Normalize(main.DaoTu),
				MappedXianJi = primaryMapped,
				Source = SourceMigration,
				IsPrimary = true,
				AcquiredYear = 0
			});
		}

		string daoTu = ResolveDaoTu(actor, main.DaoTu);
		for (int i = 0; xianJi.Ids != null && i < xianJi.Ids.Length && store.Records.Count < MaximumRecords; i++)
		{
			string mapped = Normalize(xianJi.Ids[i]);
			if (string.IsNullOrWhiteSpace(mapped) || string.Equals(mapped, primaryMapped, StringComparison.Ordinal))
			{
				continue;
			}
			string name = GenerateUniqueGrade5Name(actor, store, daoTu, mapped);
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}
			bool makePrimary = FindPrimary(store) == null;
			store.Records.Add(new RecordDto
			{
				StableId = BuildStableId(mapped, makePrimary),
				Name = name,
				Grade = 5,
				DaoTu = daoTu,
				MappedXianJi = mapped,
				Source = SourceMigration,
				IsPrimary = makePrimary,
				AcquiredYear = 0
			});
		}
	}

	private static string ResolveLegacyPrimaryMappedXianJi(Actor actor, in XjGongFaState main, in XjXianJiState xianJi)
	{
		if (xianJi.Ids == null || xianJi.Ids.Length == 0)
		{
			return string.Empty;
		}
		if (main.Found && XjXianJiCatalog.TryResolveMappedXianJi(main.DaoTu, main.Name, out string resolved)
			&& Contains(xianJi.Ids, resolved))
		{
			return Normalize(resolved);
		}
		return Normalize(xianJi.Ids[0]);
	}

	private static void NormalizeStore(Store store)
	{
		NormalizeStoreWithRepairCount(store);
	}

	private static int NormalizeStoreWithRepairCount(Store store)
	{
		int repairs = 0;
		store.Version = CurrentVersion;
		store.Records ??= new List<RecordDto>();
		List<RecordDto> normalized = new List<RecordDto>(MaximumRecords);
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		RecordDto primary = null;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (normalized.Count >= MaximumRecords || !IsValid(item))
			{
				repairs++;
				continue;
			}
			item.Name = Normalize(item.Name);
			item.DaoTu = Normalize(item.DaoTu);
			item.MappedXianJi = Normalize(item.MappedXianJi);
			item.Source = Normalize(item.Source);
			if (!names.Add(NormalizeName(item.Name)))
			{
				repairs++;
				continue;
			}
			if (!string.IsNullOrWhiteSpace(item.MappedXianJi) && !mapped.Add(item.MappedXianJi))
			{
				repairs++;
				continue;
			}
			if (item.IsPrimary && primary == null)
			{
				primary = item;
			}
			else if (item.IsPrimary)
			{
				item.IsPrimary = false;
				repairs++;
			}
			item.StableId = BuildStableId(item.MappedXianJi, item.IsPrimary);
			normalized.Add(item);
		}
		if (primary == null && normalized.Count > 0)
		{
			normalized[0].IsPrimary = true;
			normalized[0].StableId = BuildStableId(normalized[0].MappedXianJi, true);
			repairs++;
		}
		store.Records = normalized;
		return repairs;
	}

	private static int ReconcileStoreWithActor(Actor actor, Store store, bool createMissing)
	{
		if (actor?.data == null || store == null)
		{
			return 0;
		}

		int repairs = 0;
		string[] reconciliationIds = ResolveReconciliationXianJiIds(actor);
		List<string> orderedValid = new List<string>(MaximumRecords);
		HashSet<string> valid = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; reconciliationIds != null && i < reconciliationIds.Length; i++)
		{
			string id = Normalize(reconciliationIds[i]);
			if (!string.IsNullOrWhiteSpace(id) && valid.Add(id))
			{
				orderedValid.Add(id);
			}
		}

		RecordDto primary = FindPrimary(store);
		for (int i = store.Records.Count - 1; i >= 0; i--)
		{
			RecordDto item = store.Records[i];
			string mapped = Normalize(item.MappedXianJi);
			if (string.IsNullOrWhiteSpace(mapped))
			{
				// Only an entry technique may remain unmapped when no XianJi survives.
				// Once the actor still has XianJi, an unmapped old primary is an orphan
				// and must not displace the real technique owning a surviving mapping.
				if (valid.Count > 0 || !ReferenceEquals(item, primary))
				{
					store.Records.RemoveAt(i);
					repairs++;
				}
				continue;
			}
			if (valid.Contains(mapped))
			{
				continue;
			}
			if (valid.Count == 0 && ReferenceEquals(item, primary))
			{
				item.MappedXianJi = string.Empty;
				item.StableId = BuildStableId(string.Empty, true);
				repairs++;
				continue;
			}

			store.Records.RemoveAt(i);
			repairs++;
		}

		primary = FindPrimary(store);
		if (valid.Count == 0)
		{
			for (int i = store.Records.Count - 1; i >= 0; i--)
			{
				if (!ReferenceEquals(store.Records[i], primary))
				{
					store.Records.RemoveAt(i);
					repairs++;
				}
			}
			if (primary != null && !string.IsNullOrWhiteSpace(primary.MappedXianJi))
			{
				primary.MappedXianJi = string.Empty;
				primary.StableId = BuildStableId(string.Empty, true);
				repairs++;
			}
			return repairs;
		}

		primary = null;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (item.IsPrimary && primary == null)
			{
				primary = item;
			}
			else if (item.IsPrimary)
			{
				item.IsPrimary = false;
				repairs++;
			}
		}
		if (primary == null && store.Records.Count > 0)
		{
			for (int validIndex = 0; validIndex < orderedValid.Count && primary == null; validIndex++)
			{
				for (int i = 0; i < store.Records.Count; i++)
				{
					if (string.Equals(Normalize(store.Records[i].MappedXianJi), orderedValid[validIndex], StringComparison.Ordinal))
					{
						primary = store.Records[i];
						break;
					}
				}
			}
			primary ??= store.Records[0];
			primary.IsPrimary = true;
			primary.StableId = BuildStableId(primary.MappedXianJi, true);
			repairs++;
		}

		if (!createMissing)
		{
			return repairs;
		}

		HashSet<string> occupied = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < store.Records.Count; i++)
		{
			string mapped = Normalize(store.Records[i].MappedXianJi);
			if (!string.IsNullOrWhiteSpace(mapped))
			{
				occupied.Add(mapped);
			}
		}

		string daoTu = ResolveDaoTu(actor, primary?.DaoTu);
		for (int i = 0; i < orderedValid.Count && store.Records.Count < MaximumRecords; i++)
		{
			string mapped = orderedValid[i];
			if (occupied.Contains(mapped))
			{
				continue;
			}

			string name = GenerateUniqueGrade5Name(actor, store, daoTu, mapped);
			if (string.IsNullOrWhiteSpace(name))
			{
				continue;
			}
			bool makePrimary = FindPrimary(store) == null;
			RecordDto created = new RecordDto
			{
				StableId = BuildStableId(mapped, makePrimary),
				Name = name,
				Grade = 5,
				DaoTu = daoTu,
				MappedXianJi = mapped,
				Source = SourceMigration,
				IsPrimary = makePrimary,
				AcquiredYear = 0
			};
			store.Records.Add(created);
			if (makePrimary)
			{
				primary = created;
			}
			occupied.Add(mapped);
			repairs++;
		}
		return repairs;
	}

	private static bool ValidateStoreForWrite(Store store, out string reason)
	{
		reason = string.Empty;
		if (store?.Records == null || store.Records.Count > MaximumRecords)
		{
			reason = "记录数量越界";
			return false;
		}
		HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
		HashSet<string> mapped = new HashSet<string>(StringComparer.Ordinal);
		int primaryCount = 0;
		for (int i = 0; i < store.Records.Count; i++)
		{
			RecordDto item = store.Records[i];
			if (!IsValid(item))
			{
				reason = "存在无效功法记录";
				return false;
			}
			item.Name = Normalize(item.Name);
			item.DaoTu = Normalize(item.DaoTu);
			item.MappedXianJi = Normalize(item.MappedXianJi);
			item.Source = Normalize(item.Source);
			if (!names.Add(NormalizeName(item.Name)))
			{
				reason = "功法名称重复：" + item.Name;
				return false;
			}
			if (!string.IsNullOrWhiteSpace(item.MappedXianJi) && !mapped.Add(item.MappedXianJi))
			{
				reason = "仙基映射重复：" + item.MappedXianJi;
				return false;
			}
			if (item.IsPrimary)
			{
				primaryCount++;
			}
		}
		if (store.Records.Count > 0 && primaryCount != 1)
		{
			reason = "主功法数量不是一";
			return false;
		}
		return true;
	}

	private static bool ReconcileQiuJinBindingAfterDaoTuChange(
		Actor actor,
		string daoTu,
		Dictionary<string, string> renamed,
		Store store)
	{
		_ = renamed;
		_ = store;
		XjQiuJinFaState state = XjQiuJinFaAccessor.BuildState(actor);
		if (!state.Found || !state.Ready)
		{
			return false;
		}

		string authority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
			daoTu,
			state.Name,
			string.Empty);
		if (string.IsNullOrWhiteSpace(authority))
		{
			return false;
		}
		bool changed = !string.Equals(Normalize(state.SourceDaoTu), daoTu, StringComparison.Ordinal)
			|| !string.IsNullOrWhiteSpace(state.SourceGongFaName)
			|| state.SourceGongFaGrade != 0
			|| !string.Equals(Normalize(state.BoundAuthority), Normalize(authority), StringComparison.Ordinal);
		if (!changed)
		{
			return false;
		}
		XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
			true,
			state.Name,
			string.Empty,
			0,
			daoTu,
			true,
			state.LastYear,
			state.ReasonCode,
			authority));
		return true;
	}

	private static string[] ResolveReconciliationXianJiIds(Actor actor)
	{
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		if (state.Ids != null && state.Ids.Length > 0)
		{
			return state.Ids;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		bool knownRealm = string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal);
		return knownRealm ? Array.Empty<string>() : XjXianJiAccessor.ReadRawIds(actor);
	}


	private static bool IsValid(RecordDto item)
	{
		return item != null
			&& !string.IsNullOrWhiteSpace(item.Name)
			&& XjGongFaDefinition.IsValidGrade(item.Grade);
	}

	private static RecordDto FindPrimary(Store store)
	{
		if (store?.Records == null)
		{
			return null;
		}
		for (int i = 0; i < store.Records.Count; i++)
		{
			if (store.Records[i].IsPrimary)
			{
				return store.Records[i];
			}
		}
		return store.Records.Count > 0 ? store.Records[0] : null;
	}

	private static string ResolveDaoTu(Actor actor, string preferred)
	{
		string daoTu = Normalize(preferred);
		if (!string.IsNullOrWhiteSpace(daoTu))
		{
			return daoTu;
		}
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		return Normalize(snapshot.DaoTu);
	}

	private static string GenerateUniqueGrade5Name(Actor actor, Store store, string daoTu, string mapped)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		long baseSeed = actorId + XjDeterministicHash.StableHash(mapped);
		for (int attempt = 0; attempt < 12; attempt++)
		{
			string name = XjGongFaNameLibrary.GenerateName(daoTu, 5, baseSeed + (attempt * 7919L));
			if (!string.IsNullOrWhiteSpace(name) && !ContainsName(store, name))
			{
				return name.Trim();
			}
		}
		return string.Empty;
	}

	private static bool ContainsName(Store store, string name)
	{
		string normalized = NormalizeName(name);
		for (int i = 0; store?.Records != null && i < store.Records.Count; i++)
		{
			if (string.Equals(NormalizeName(store.Records[i].Name), normalized, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool Contains(string[] ids, string id)
	{
		string expected = Normalize(id);
		for (int i = 0; ids != null && i < ids.Length; i++)
		{
			if (string.Equals(Normalize(ids[i]), expected, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static bool SameName(string left, string right)
	{
		return string.Equals(NormalizeName(left), NormalizeName(right), StringComparison.Ordinal);
	}

	private static string NormalizeName(string value)
	{
		string result = Normalize(value);
		string[] suffixes = { "（一品）", "（二品）", "（三品）", "（四品）", "（五品）", "（六品）" };
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (result.EndsWith(suffixes[i], StringComparison.Ordinal))
			{
				return result.Substring(0, result.Length - suffixes[i].Length).Trim();
			}
		}
		return result;
	}

	private static string BuildStableId(string mappedXianJi, bool primary)
	{
		string mapped = Normalize(mappedXianJi);
		if (!string.IsNullOrWhiteSpace(mapped))
		{
			return "xianji:" + mapped;
		}
		return primary ? "primary" : "unmapped";
	}

	private static Record ToRecord(RecordDto item)
	{
		return new Record(item.StableId, item.Name, item.Grade, item.DaoTu, item.MappedXianJi, item.Source, item.IsPrimary, item.AcquiredYear);
	}

	private static int CompareRecords(Record left, Record right)
	{
		if (left.IsPrimary != right.IsPrimary)
		{
			return left.IsPrimary ? -1 : 1;
		}
		int grade = right.Grade.CompareTo(left.Grade);
		if (grade != 0)
		{
			return grade;
		}
		int mapped = string.Compare(left.MappedXianJi, right.MappedXianJi, StringComparison.Ordinal);
		if (mapped != 0) return mapped;
		int stable = string.Compare(left.StableId, right.StableId, StringComparison.Ordinal);
		return stable != 0 ? stable : string.Compare(left.Name, right.Name, StringComparison.Ordinal);
	}

	private static string Serialize(Store store)
	{
		try
		{
			return store == null ? string.Empty : JsonConvert.SerializeObject(store, Formatting.None);
		}
		catch
		{
			return string.Empty;
		}
	}

	private static Store Deserialize(string json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return null;
		}
		try
		{
			return JsonConvert.DeserializeObject<Store>(json);
		}
		catch
		{
			return null;
		}
	}

	private static bool Write(Actor actor, Store store)
	{
		if (actor?.data == null || store == null)
		{
			return false;
		}
		store.Version = CurrentVersion;
		if (!ValidateStoreForWrite(store, out string invalidReason))
		{
			LogInvariant(actor, "拒绝写入：" + invalidReason);
			return false;
		}
		try
		{
			string json = JsonConvert.SerializeObject(store, Formatting.None);
			if (string.IsNullOrWhiteSpace(json))
			{
				return false;
			}
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaCollectionJson, json);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaCollectionVersion, CurrentVersion);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaCollectionJson, out string persisted);
			return string.Equals(persisted, json, StringComparison.Ordinal);
		}
		catch
		{
			return false;
		}
	}

	private static void WriteLegacyPrimary(Actor actor, RecordDto primary)
	{
		if (actor?.data == null || primary == null)
		{
			return;
		}
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaName, primary.Name);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade, primary.Grade);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaStage, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjGongFaProgress, 0f);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaDaoTu, primary.DaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaSource, primary.Source);
	}

	private static void LogRepair(Actor actor, string reason, int repairs, string before)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		Debug.LogWarning("[玄鉴][真实功法集合] actor=" + actorId + " reason=" + (reason ?? string.Empty)
			+ " repairs=" + repairs + " previousLength=" + (before?.Length ?? 0));
	}

	private static void LogInvariant(Actor actor, string reason)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		Debug.LogError("[玄鉴][真实功法集合] actor=" + actorId + " invariant=" + (reason ?? string.Empty));
	}

	private static string Normalize(string value)
	{
		return XjStringHelper.Normalize(value);
	}
}
