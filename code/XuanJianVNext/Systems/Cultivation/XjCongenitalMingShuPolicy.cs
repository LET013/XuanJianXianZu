using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 先天命数的统一出生分布。命数不再由修炼资质决定：低资质人物同样可能
/// 生有极高命数，高资质人物也可能命数平常。只初始化缺失数据，不重掷旧档。
/// </summary>
internal static class XjCongenitalMingShuPolicy
{
	internal static float Roll(long actorId, string actorName)
	{
		int bucket = XjDeterministicHash.PositiveIndex(actorId,
			"congenital_mingshu_bucket|" + (actorName ?? string.Empty), 10000);
		// 高命数仍然稀少，但概率与xjzz完全无关：约10%达到70以上，
		// 其中约0.5%天然落在95—100。
		if (bucket < 6500) return XjDeterministicHash.BuildSeedInteger(actorId, actorName, 1201, 12, 49);
		if (bucket < 9000) return XjDeterministicHash.BuildSeedInteger(actorId, actorName, 1202, 50, 69);
		if (bucket < 9700) return XjDeterministicHash.BuildSeedInteger(actorId, actorName, 1203, 70, 84);
		if (bucket < 9950) return XjDeterministicHash.BuildSeedInteger(actorId, actorName, 1204, 85, 94);
		return XjDeterministicHash.BuildSeedInteger(actorId, actorName, 1205, 95, 100);
	}
}
