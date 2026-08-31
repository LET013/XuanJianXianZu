namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// “道胎之姿”突破语义的单权威入口。它只保护修炼/破境结算：所有随机成败判定
/// 视为通过，修炼流程不得以走火、求金失败、跨道途仙基、污染或额外突破灾劫等理由
/// 直接杀死角色。普通战斗、寿元和其它世界事件仍按各自规则结算。
/// </summary>
internal static class XjDaoTaiPosturePolicy
{
	internal const string TraitId = "ChuShen8";

	internal static bool IsGuaranteedCultivator(Actor actor)
	{
		return actor?.data != null && actor.hasTrait(TraitId);
	}
}
