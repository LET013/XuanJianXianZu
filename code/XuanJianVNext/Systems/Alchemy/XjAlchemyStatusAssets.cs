namespace XuanJianVNext.Systems.Alchemy;

/// <summary>
/// 丹药服用的可见动作状态。状态本身不提供永久数值，实际药效仍由丹药消费系统即时结算。
/// </summary>
internal static class XjAlchemyStatusAssets
{
	internal const string PillConsumedStatusId = "XjAlchemyPillConsumed";
	private const float ConsumptionVisualSeconds = 6f;

	internal static void Init()
	{
		if (AssetManager.status == null || AssetManager.status.get(PillConsumedStatusId) != null)
		{
			return;
		}

		StatusAsset status = new StatusAsset
		{
			id = PillConsumedStatusId,
			path_icon = "trait/LianDanShi",
			locale_id = "status_title_XjAlchemyPillConsumed",
			locale_description = "status_desc_XjAlchemyPillConsumed",
			allow_timer_reset = true
		};
		AssetManager.status.add(pAsset: status);
	}

	internal static void ApplyConsumptionStatus(Actor actor)
	{
		if (actor?.data == null || AssetManager.status?.get(PillConsumedStatusId) == null)
		{
			return;
		}

		try
		{
			((BaseSimObject)actor).addStatusEffect(PillConsumedStatusId, ConsumptionVisualSeconds, true);
			actor.setStatsDirty();
		}
		catch
		{
			// 服丹状态只负责表现，不能反向影响实际丹药结算。
		}
	}
}
