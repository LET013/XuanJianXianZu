namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// WorldBox 地形变更归因桥。
///
/// 角色法术统一走 WorldBox 已公开的 damageWorld(tile, radius, options, byWho) 入口，
/// 把真实施法者传给原生规则层。这样 Gaia/生态保护可以按“是谁在破坏地形”正常裁定；
/// 不再逐格调用无来源 decreaseTile，也不需要在热路径反射查找未知重载。
/// </summary>
internal static class XjAttributedTerrainInterop
{
    internal static bool TryDecreaseTile(
        WorldTile tile,
        TerraformOptions options,
        BaseSimObject source)
    {
        if (tile == null || options == null || source == null) return false;
        try
        {
            // radius=0 只提交当前格；大范围采样/预算仍由 XjJinDanCombatApi 的
            // TerrainEffectTask 负责，避免 damageWorld 在这里再次展开第二层范围。
            MapAction.damageWorld(tile, 0, options, source);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
