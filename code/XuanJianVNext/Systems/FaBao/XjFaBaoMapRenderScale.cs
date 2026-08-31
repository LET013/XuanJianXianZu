using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.FaBao;

/// <summary>
/// 法宝地图持有物图标缩放。法宝贴图为独立像素资源，不能沿用原版装备的世界缩放。
/// </summary>
internal static class XjFaBaoMapRenderScale
{
	private const float FaBaoItemScaleMultiplier = 0.017f;
	/// <summary>
	/// 统一可见角色渲染通道调用。只处理当前槽位，不再自行扫描 visible_units。
	/// </summary>
	internal static void ApplyVisibleActor(Actor actor, Vector3[] itemScales, int index, bool bootstrapPending)
	{
		if (actor?.data == null || itemScales == null || index < 0 || index >= itemScales.Length)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		bool hasFaBao = XjRuntimeActorInterestIndex.HasFaBaoInterest(actorId)
			|| (bootstrapPending && XjFaBaoAccessor.HasState(actor));
		if (!hasFaBao)
		{
			return;
		}

		Vector3 scale = itemScales[index];
		itemScales[index] = new Vector3(
			scale.x * FaBaoItemScaleMultiplier,
			scale.y * FaBaoItemScaleMultiplier,
			scale.z);
	}

}
