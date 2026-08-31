using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox.Combat;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 石弹/弹跳石块屏蔽 Guard
/// 语义：防止原版 bowling_ball 技能石块杀死修士
/// 
/// 对应 0.5.4 的石弹屏蔽逻辑
/// 
/// 玄门化原则：
/// - O(1) 检测，无遍历
/// - 无外部持久化依赖
/// </summary>
internal static class XjProjectileGuard
{
	private const int CullCadenceFrames = 30;
	private const int CullBudget = 64;
	private static int _lastCullFrame = -CullCadenceFrames;
	private static int _cullCursor = -1;
	private static readonly Dictionary<string, bool> ProjectileAssetValidity = new Dictionary<string, bool>(StringComparer.Ordinal);
	private static readonly Dictionary<Type, bool> BlockedTypeCache = new Dictionary<Type, bool>();
	private static readonly Dictionary<string, bool> BlockedAssetIdCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    // 需要屏蔽的投射物名称特征（不区分大小写）
    private static readonly string[] BlockedProjectileNamePrefixes = new[]
    {
        // 只保留 0.5.4 明确需要隔离的 bowling_ball 链。
        // 禁止再用 rock/stone/boulder/ball 这类泛词，否则 fireball、石系法术等合法攻击会被全部清零。
        "bowling"
    };

    /// <summary>
    /// 解析伤害载体背后的真实施法者。WorldBox 的投射物本身通常没有 a，
    /// 直接读取 pAttacker.a 会把全部远程攻击误判成环境伤害。
    /// </summary>
    internal static Actor ResolveAttackSourceActor(BaseSimObject source)
    {
        if (source == null)
        {
            return null;
        }

        try
        {
            // Actor 本身就是最权威的攻击来源。不能只依赖 BaseSimObject.a：
            // 某些 WorldBox/NML 包装路径下，直接传入的 Actor 其 a 属性可能为空，
            // 旧逻辑会把近战与部分法术误判成环境伤害，从而绕过境界压制。
            if (source is Actor sourceActor)
            {
                return sourceActor;
            }

            Actor direct = source.a;
            if (direct != null)
            {
                return direct;
            }

            if (TryAsProjectile(source, out Projectile projectile))
            {
                BaseSimObject initiator = XjNativeProjectileInterop.ResolveInitiator(projectile);
                if (initiator is Actor initiatorActor)
                {
                    return initiatorActor;
                }
                return initiator?.a;
            }
        }
        catch (System.Exception xjCaught76) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjProjectileGuard.cs:76", xjCaught76); }

        return null;
    }

    /// <summary>
    /// 判断该来源是否属于战斗投递链。即使施法者已经死亡，延迟投射物也不能
    /// 被误套用环境伤害的单次伤害上限。
    /// </summary>
    internal static bool IsCombatDeliverySource(BaseSimObject source)
    {
        if (source == null)
        {
            return false;
        }

        try
        {
            return source is Actor || source.a != null || TryAsProjectile(source, out _);
        }
        catch
        {
            return source is Actor || TryAsProjectile(source, out _);
        }
    }

    /// <summary>
    /// 判断投射物是否需要被屏蔽（对修士造成伤害的弹跳石块等）
    /// </summary>
    internal static bool IsBlockedProjectile(BaseSimObject attacker)
    {
        if (attacker == null)
            return false;

        try
        {
            Type type = attacker.GetType();
            if (!BlockedTypeCache.TryGetValue(type, out bool blockedType))
            {
                blockedType = ContainsBlockedToken(type.Name);
                BlockedTypeCache[type] = blockedType;
            }
            if (blockedType)
                return true;

            // 额外检查：asset ID 是否包含特征。ID 集合稳定，按字符串缓存。
            string assetId = TryAsProjectile(attacker, out Projectile projectile)
                ? projectile.asset?.id
                : attacker.a?.asset?.id;
            if (!string.IsNullOrWhiteSpace(assetId))
            {
                if (!BlockedAssetIdCache.TryGetValue(assetId, out bool blockedAsset))
                {
                    blockedAsset = ContainsBlockedToken(assetId);
                    BlockedAssetIdCache[assetId] = blockedAsset;
                }
                return blockedAsset;
            }
        }
        catch (System.Exception xjCaught137) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjProjectileGuard.cs:137", xjCaught137); }

        return false;
    }

    /// <summary>
    /// 检查是否需要屏蔽该次伤害
    /// 规则：如果攻击者是屏蔽列表中的投射物，且防御者是修士，则伤害为0
    /// </summary>
    internal static bool ShouldBlock(ref float damage, BaseSimObject pAttacker, Actor defender)
    {
        if (damage <= 0f || defender == null || pAttacker == null)
            return false;

        // 只对修士生效
        if (!XjRealmSuppression.IsCultivator(defender))
            return false;

        // 检查是否来自屏蔽投射物
        if (!IsBlockedProjectile(pAttacker))
            return false;

        // 屏蔽伤害
        damage = 0f;
        return true;
    }

    internal static bool IsValidProjectileAssetId(string projectileId)
    {
        if (string.IsNullOrWhiteSpace(projectileId))
        {
            return false;
        }

        if (ProjectileAssetValidity.TryGetValue(projectileId, out bool cached))
        {
            return cached;
        }

        bool valid;
        try
        {
            valid = AssetManager.projectiles != null && AssetManager.projectiles.has(projectileId);
        }
        catch
        {
            valid = false;
        }
        // 只缓存成功结果。模组投射物可能晚于首轮战斗检查注册；
        // 缓存 false 会让该投射物在整个存档会话中永久失效。
        if (valid)
        {
            ProjectileAssetValidity[projectileId] = true;
        }
        return valid;
    }

    internal static bool IsAttackProjectileLookupSafe(in AttackData data)
    {
        return !data.is_projectile || IsValidProjectileAssetId(data.projectile_id);
    }

    /// <summary>
    /// attackRangeAction 会直接生成 projectile_id，不以 is_projectile 为前置条件。
    /// 因而此入口必须无条件校验弹道资源，不能复用 applyAttack 的判断。
    /// </summary>
    internal static bool IsRangeActionProjectileSafe(in AttackData data)
    {
        return IsValidProjectileAssetId(data.projectile_id);
    }

    internal static bool IsBrokenRuntimeProjectile(Projectile projectile)
    {
        // 运行期清理只处理确定损坏的对象。施法者死亡、阵营为空、目标暂时失效
        // 都可能是合法的飞行中状态，不能据此删除投射物，否则会出现远程攻击凭空消失。
        if (projectile == null || projectile.asset == null)
        {
            return true;
        }

        // WorldBox 的近邻索敌假定投射物当前格一定属于有效 MapChunk。
        // 越界瞬移、地图缩放或目标在命中前被销毁时，残留投射物可能落到
        // null chunk，并在每帧抛出 EnemyFinderContainer.getData 空引用。
        try
        {
            WorldTile tile = projectile.getCurrentTilePosition();
            return tile == null || tile.chunk == null;
        }
        catch
        {
            return true;
        }
    }

    internal static void RemoveBrokenProjectile(Projectile projectile)
    {
        if (projectile == null)
        {
            return;
        }

        try
        {
            World.world?.projectiles?.removeObject(projectile);
        }
        catch (System.Exception xjCaught244) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjProjectileGuard.cs:244", xjCaught244); }
    }

	internal static void CullBrokenProjectiles(ProjectileManager manager)
    {
		int frame = Time.frameCount;
		if (frame - _lastCullFrame < CullCadenceFrames)
		{
			return;
		}
		_lastCullFrame = frame;

        List<Projectile> projectiles = manager?.list;
        if (projectiles == null || projectiles.Count == 0)
        {
			_cullCursor = -1;
            return;
        }

		if (_cullCursor < 0 || _cullCursor >= projectiles.Count)
		{
			_cullCursor = projectiles.Count - 1;
		}

		int checks = Math.Min(CullBudget, projectiles.Count);
		for (int checkedCount = 0; checkedCount < checks && projectiles.Count > 0; checkedCount++)
        {
			if (_cullCursor < 0 || _cullCursor >= projectiles.Count)
			{
				_cullCursor = projectiles.Count - 1;
			}

			int index = _cullCursor--;
			Projectile projectile = projectiles[index];
            if (!IsBrokenRuntimeProjectile(projectile))
            {
                continue;
            }

            if (projectile == null)
            {
				projectiles.RemoveAt(index);
            }
            else
            {
                RemoveBrokenProjectile(projectile);
            }

			if (_cullCursor >= projectiles.Count)
			{
				_cullCursor = projectiles.Count - 1;
			}
        }
    }

	internal static void ClearRuntimeCache()
	{
		ProjectileAssetValidity.Clear();
		BlockedTypeCache.Clear();
		BlockedAssetIdCache.Clear();
		_lastCullFrame = -CullCadenceFrames;
		_cullCursor = -1;
	}

	/// <summary>
	/// Last-resort recovery for the native EnemyFinder null-reference.  Some
	/// third-party patches wrap Projectile.updateVelocity after our per-projectile
	/// finalizer, so a bad projectile can escape that guard and rethrow every
	/// frame.  This method only runs from the manager-level finalizer after that
	/// exact failure and removes invalid projectiles in one bounded sweep.
	/// </summary>
	internal static void RecoverFromEnemyFinderFailure(ProjectileManager manager)
	{
		List<Projectile> projectiles = manager?.list;
		if (projectiles == null || projectiles.Count == 0)
		{
			return;
		}

		for (int index = projectiles.Count - 1; index >= 0; index--)
		{
			Projectile projectile = projectiles[index];
			if (!IsBrokenRuntimeProjectile(projectile))
			{
				continue;
			}

			if (projectile == null)
			{
				projectiles.RemoveAt(index);
			}
			else
			{
				RemoveBrokenProjectile(projectile);
			}
		}
	}

	internal static bool IsEnemyFinderNullReference(Exception exception)
	{
		if (exception is not NullReferenceException)
		{
			return false;
		}

		string stackTrace = exception.StackTrace ?? string.Empty;
		return stackTrace.IndexOf("EnemyFinder", StringComparison.OrdinalIgnoreCase) >= 0
			|| stackTrace.IndexOf("checkHitOnNearbyUnits", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	private static bool ContainsBlockedToken(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		for (int i = 0; i < BlockedProjectileNamePrefixes.Length; i++)
		{
			if (value.IndexOf(BlockedProjectileNamePrefixes[i], StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool TryAsProjectile(object value, out Projectile projectile)
	{
		projectile = value as Projectile;
		return projectile != null;
	}

}
