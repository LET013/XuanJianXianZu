using System;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.UI.ActorInfo;

// 金丹、真君羽士与道胎共用果位高境的金性、果位、权柄展示模板。
// 境界名、尊号和修持字段仍由各自的境界分支负责，避免显示口径混同。
internal static class XjHighRealmDisplayPolicy
{
	internal static bool UsesFruitPositionTemplate(string realmId)
	{
		return string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal);
	}
}
