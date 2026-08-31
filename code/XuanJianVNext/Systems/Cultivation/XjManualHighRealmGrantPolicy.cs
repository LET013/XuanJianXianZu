using System;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 玄鉴可见境界允许从特质编辑器手动补录，并由统一协调器补全权威状态。
/// 仅仙国制度投影仍属于内部状态，不能手动塞入角色。
/// </summary>
internal static class XjManualHighRealmGrantPolicy
{
	internal static bool IsProtectedHighRealmTrait(string traitId)
	{
		string id = (traitId ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(id)) return false;

		return string.Equals(id, "XjXianGuoZhuJi", StringComparison.Ordinal)
			|| string.Equals(id, "XjXianGuoZiFu", StringComparison.Ordinal)
			|| string.Equals(id, "XjXianGuoFakeJinDan", StringComparison.Ordinal)
			|| string.Equals(id, "XjRealm6", StringComparison.Ordinal)
			|| string.Equals(id, "XjRealm7", StringComparison.Ordinal)
			|| string.Equals(id, "XjRealm14", StringComparison.Ordinal)
			|| string.Equals(id, "XjRealm26", StringComparison.Ordinal);
	}

	internal static bool IsManualGrantForbiddenTrait(string traitId)
	{
		string id = (traitId ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(id)) return false;

		return IsProtectedHighRealmTrait(id)
			|| string.Equals(id, "XjZiFuDescendant", StringComparison.Ordinal)
			|| string.Equals(id, "XjJinDanDescendant", StringComparison.Ordinal)
			|| string.Equals(id, "XjDaoTaiDescendant", StringComparison.Ordinal)
			|| string.Equals(id, "XjZiFuFamily", StringComparison.Ordinal)
			|| string.Equals(id, "XjJinDanFamily", StringComparison.Ordinal)
			|| string.Equals(id, "XjDaoTaiFamily", StringComparison.Ordinal);
	}

	internal static bool IsManualRealmRecordAllowed(string shiRealm)
	{
		return !string.IsNullOrWhiteSpace(shiRealm);
	}
}
