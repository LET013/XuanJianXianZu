using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Data.HighRealm;

internal enum XjShenTongTier : byte
{
	Upper = 1,
	Lower = 2
}

/// <summary>
/// 神通的唯一运行期元数据。旧神通表仍作为启动时的录入材料，但从注册完成后，
/// 道途池、上下位、功法映射与审计均只读取本注册表，避免多份字典长期漂移。
/// </summary>
internal sealed class XjShenTongDefinition
{
	internal XjShenTongDefinition(
		string id,
		string daoTuId,
		XjShenTongTier tier,
		string[] compatiblePaths,
		string requiredRealmId,
		int gongFaGrade,
		string[] tags,
		string canonSource)
	{
		Id = Normalize(id);
		Name = Id;
		DaoTuId = Normalize(daoTuId);
		Tier = tier;
		CompatiblePaths = compatiblePaths ?? Array.Empty<string>();
		RequiredRealmId = requiredRealmId ?? string.Empty;
		GongFaGrade = Math.Clamp(gongFaGrade, 1, 6);
		Tags = tags ?? Array.Empty<string>();
		CanonSource = canonSource ?? string.Empty;
	}

	internal string Id { get; }
	internal string Name { get; }
	internal string DaoTuId { get; }
	internal XjShenTongTier Tier { get; }
	internal string[] CompatiblePaths { get; }
	internal string RequiredRealmId { get; }
	internal int GongFaGrade { get; }
	internal string[] Tags { get; }
	internal string CanonSource { get; }

	private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}

internal sealed class XjShenTongRegistry
{
	private readonly Dictionary<string, XjShenTongDefinition> _byId;
	private readonly Dictionary<string, string[]> _upperByDaoTu;
	private readonly Dictionary<string, string[]> _lowerByDaoTu;
	private readonly string[] _issues;

	private XjShenTongRegistry(
		Dictionary<string, XjShenTongDefinition> byId,
		Dictionary<string, string[]> upperByDaoTu,
		Dictionary<string, string[]> lowerByDaoTu,
		string[] issues)
	{
		_byId = byId;
		_upperByDaoTu = upperByDaoTu;
		_lowerByDaoTu = lowerByDaoTu;
		_issues = issues;
	}

	internal static XjShenTongRegistry Build(
		IReadOnlyDictionary<string, string[]> upper,
		IReadOnlyDictionary<string, string[]> lower)
	{
		Dictionary<string, XjShenTongDefinition> byId = new Dictionary<string, XjShenTongDefinition>(StringComparer.Ordinal);
		Dictionary<string, string[]> upperPools = CopyPools(upper);
		Dictionary<string, string[]> lowerPools = CopyPools(lower);
		List<string> issues = new List<string>();
		RegisterPools(byId, upperPools, XjShenTongTier.Upper, issues);
		RegisterPools(byId, lowerPools, XjShenTongTier.Lower, issues);
		return new XjShenTongRegistry(byId, upperPools, lowerPools, issues.ToArray());
	}

	internal bool TryGet(string id, out XjShenTongDefinition definition)
	{
		return _byId.TryGetValue(Normalize(id), out definition);
	}

	internal bool TryResolveOwner(string id, out string daoTu, out XjShenTongTier tier)
	{
		daoTu = string.Empty;
		tier = default;
		if (!TryGet(id, out XjShenTongDefinition definition)) return false;
		daoTu = definition.DaoTuId;
		tier = definition.Tier;
		return !string.IsNullOrWhiteSpace(daoTu);
	}

	internal string[] GetPool(string daoTu, XjShenTongTier tier)
	{
		Dictionary<string, string[]> source = tier == XjShenTongTier.Upper ? _upperByDaoTu : _lowerByDaoTu;
		return source.TryGetValue(Normalize(daoTu), out string[] values) ? values : Array.Empty<string>();
	}

	internal IReadOnlyCollection<XjShenTongDefinition> ReadDefinitions() => _byId.Values;

	internal string[] GetValidationIssues()
	{
		return _issues.Length == 0 ? Array.Empty<string>() : (string[])_issues.Clone();
	}

	private static Dictionary<string, string[]> CopyPools(IReadOnlyDictionary<string, string[]> source)
	{
		Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
		if (source == null) return result;
		foreach (KeyValuePair<string, string[]> pair in source)
		{
			string daoTu = Normalize(pair.Key);
			if (string.IsNullOrWhiteSpace(daoTu)) continue;
			List<string> values = new List<string>();
			HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; pair.Value != null && i < pair.Value.Length; i++)
			{
				string id = Normalize(pair.Value[i]);
				if (!string.IsNullOrWhiteSpace(id) && seen.Add(id)) values.Add(id);
			}
			result[daoTu] = values.ToArray();
		}
		return result;
	}

	private static void RegisterPools(
		Dictionary<string, XjShenTongDefinition> byId,
		Dictionary<string, string[]> pools,
		XjShenTongTier tier,
		List<string> issues)
	{
		foreach (KeyValuePair<string, string[]> pair in pools)
		{
			for (int i = 0; i < pair.Value.Length; i++)
			{
				string id = pair.Value[i];
				if (byId.TryGetValue(id, out XjShenTongDefinition existing))
				{
					issues.Add(id + "重复登记：" + existing.DaoTuId + existing.Tier + " / " + pair.Key + tier);
					continue;
				}
				bool isLongGengSupplement = string.Equals(pair.Key, "长庚", StringComparison.Ordinal)
					&& !string.Equals(id, "意堪身", StringComparison.Ordinal);
				string[] tags = isLongGengSupplement
					? new[] { tier == XjShenTongTier.Upper ? "上位" : "下位", "道统补录", pair.Key }
					: new[] { tier == XjShenTongTier.Upper ? "上位" : "下位", pair.Key };
				byId[id] = new XjShenTongDefinition(
					id,
					pair.Key,
					tier,
					new[] { XjCultivationPathIds.ZiFuJinDan },
					XjRealmIds.ZiFu,
					5,
					tags,
					isLongGengSupplement ? "道统补录" : "有效神通表");
			}
		}
	}

	private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
}
