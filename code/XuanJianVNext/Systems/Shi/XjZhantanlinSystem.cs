using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using ai;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 旃檀林实体释土：核心半径28，外围按幽冥同源思路铺设
/// “光墙—不灭岩浆—光墙”三重实体封界。岩浆层直接沿用原生“不灭岩浆”世界法则的停滞语义，
/// 即使玩家没有开启该世界法则，旃檀林自身的岩浆环也不会冷却或继续流动。
/// 今释入林/归返仍走玄鉴实体转移事务，不创建虚像或第二套航运系统。
/// </summary>
internal static class XjZhantanlinSystem
{
	// 旃檀林核心从37再缩小四分之一，取整为28；外围仍保持幽冥同规格的
	// 3格光墙 -> 3格 lava3 -> 3格光墙。三层必须完整落在地图内。
	internal const int MinimumRadius = 20;
	internal const int DefaultRadius = 28;
	private const int LegacyPreviousRadius = 37;
	private const int LegacyPreviousBoundaryThickness = 9; // schema10 的 3+3+3 封界
	private const int LegacyExpandedRadius = 56;
	private const int LegacyExpandedBoundaryThickness = 6; // schema9 的 2+2+2 旧封界
	internal const int MaximumRadius = DefaultRadius;
	internal const int InnerLightWallThickness = 3;
	internal const int LavaRingThickness = 3;
	internal const int OuterLightWallThickness = 3;
	internal const int BoundaryRingThickness = InnerLightWallThickness + LavaRingThickness + OuterLightWallThickness;
	internal const int OuterBoundaryRadius = DefaultRadius + BoundaryRingThickness;
	// Compatibility aliases for the existing route/territory code. Semantics are now
	// the complete three-band protected boundary, not mountains.
	internal const int MountainRingThickness = BoundaryRingThickness;
	internal const int OuterMountainRadius = OuterBoundaryRadius;
	private const int CurrentTerrainSchema = 11;
	private const string PeacefulTraitId = "peaceful";
	private const int MoHeReturnStayYears = 2;
	private const int MoHeReturnMinIntervalYears = 18;
	private const int MoHeReturnIntervalSpanYears = 25;
	private const int TerritoryRepairIntervalYears = 10;
	private const int MaintenanceProbeBackoffFrames = 24;
	// 原生陆地单位在旃檀林邻海出现“反复跳水”时，只处理紧邻法界的异常水路。
	// 该范围不改变普通世界跨海/航行规则，只用于识别由佛土封界与邻海造成的残留目标。
	private const int ExternalWaterRecoveryPadding = 12;
	private const int ExternalWaterTargetPadding = 3;
	private const int SanctuaryRouteBackoffBaseFrames = 120;
	private const int SanctuaryRouteBackoffMaxFrames = 780;
	private const int SanctuaryRouteSuppressionMaxIds = 12000;

	private static readonly string[] ArcaneDesertBiomeIds =
	{
		// 旃檀佛土复用原生 desert 地形资源作为底层实现；该资源名不得进入玩家文本。
		// 后两项仅保留旧档兼容。
		"biome_desert",
		"biome_arcane_desert",
		"arcane_desert"
	};



	private static TopTileType _cachedNativeLightWall;
	private static TileType _cachedNativePersistentLava;
	private static bool _terrainWarningIssued;
	private static int _lastTerrainIntegrityYear = -1;
	private static int _lastSanctuaryDefenseYear = -1;
	private static int _lastTerritoryRepairYear = -1;
	private static object _lastSanctuaryPopulationArchive;
	private static int _nextMaintenanceProbeUnityFrame;

	// goTo/nutrition/spawn guards can be invoked thousands of times per rendered
	// frame. Resolve the immutable sanctuary geometry at most once per frame instead
	// of hitting the domain dictionary for every actor callback. Placement/repair and
	// world lifecycle explicitly invalidate this cache.
	private static int _geometryCacheUnityFrame = -1;
	private static bool _geometryCachePlaced;
	private static int _geometryCacheCenterX;
	private static int _geometryCacheCenterY;
	private static int _geometryCacheRadius;

	// 净界校验属于单线程主循环事务，复用缓冲区避免年度检查反复分配 List/HashSet。
	// 矿物列表只在放置、读档首次同步或佛土地形重建时使用；正常年度不再扫描全世界建筑。
	private static readonly List<Actor> SanctuaryActorBuffer = new List<Actor>(128);
	private static readonly HashSet<long> SanctuaryActorSeen = new HashSet<long>();
	private static readonly List<Building> SanctuaryMineralBuffer = new List<Building>(64);
	private static readonly HashSet<Building> SanctuaryMineralSeen = new HashSet<Building>();
	// 摩诃周期归林属于世界年控制面事务。年度只遍历真实释修ID快照，把
	// 已到期/状态不一致的少量摩诃排入队列；后台每次只对账一人，避免依赖
	// 单个 Actor 的 updateAge 是否在40x高压下及时轮到。
	private static readonly Queue<long> PeriodicMoHeReturnQueue = new Queue<long>();
	private static readonly HashSet<long> QueuedPeriodicMoHeReturnIds = new HashSet<long>();

	// 旃檀林会在放置瞬间改变一大片原生地形拓扑。旧实现只拦“终点在佛土里”的
	// goTo，无法阻止起点/终点都在外界、但 RegionPathFinder 生成的中间路径穿过
	// 三重实体封界或邻海的路线。这里不另造异步寻路器：只记录已经被原生实际算坏的路线，
	// 并按 Cultiway 的失败退避思路给同一角色一个有界冷却；一旦 AI 选择安全新目标，
	// 立即解除退避，不冻结角色正常行动。状态仅保存 ActorId 与坐标，不缓存 Actor。
	private readonly struct SanctuaryRouteSuppression
	{
		internal SanctuaryRouteSuppression(int untilFrame, int targetX, int targetY, int strikes,
			int originX, int originY)
		{
			UntilFrame = untilFrame;
			TargetX = targetX;
			TargetY = targetY;
			Strikes = strikes;
			OriginX = originX;
			OriginY = originY;
		}

		internal int UntilFrame { get; }
		internal int TargetX { get; }
		internal int TargetY { get; }
		internal int Strikes { get; }
		// 首次发现坏路线时保存最后一块确定安全的陆地坐标。只存坐标，不保留
		// WorldTile/Actor 引用；若下一拍已经落水，可原路退回而不是随机远距传送。
		internal int OriginX { get; }
		internal int OriginY { get; }
	}

	private static readonly ConcurrentDictionary<long, SanctuaryRouteSuppression> SanctuaryRouteSuppressions
		= new ConcurrentDictionary<long, SanctuaryRouteSuppression>();
	private static int SanctuaryWaterRecoveries;

	internal static void OnWorldLoaded()
	{
		InvalidateGeometryCache();
		_lastTerrainIntegrityYear = -1;
		_lastSanctuaryDefenseYear = -1;
		_lastTerritoryRepairYear = -1;
		_lastSanctuaryPopulationArchive = null;
		_nextMaintenanceProbeUnityFrame = 0;
		PeriodicMoHeReturnQueue.Clear();
		QueuedPeriodicMoHeReturnIds.Clear();
		SanctuaryRouteSuppressions.Clear();
		SanctuaryWaterRecoveries = 0;
		// Do not synchronously repair the sanctuary during the load callback.
		// The reset cursors above make HasRuntimeMaintenancePending true and the
		// module-owned background lane performs the same reconciliation under the
		// cumulative rendered-frame budget.
	}

	internal static void NotifyExternalTerrainMutation()
	{
		// 第三方模组可能在 SaveManager.loadWorld 的后置阶段继续改 top tile。
		// 不在这里同步扫描/重铺佛土，只把现有维护游标置脏；后台车道下一次
		// 获得帧预算时会执行完整性检查并只修真正受损的格子。
		if (!IsPlaced) return;
		_lastTerrainIntegrityYear = -1;
		_lastSanctuaryDefenseYear = -1;
		_nextMaintenanceProbeUnityFrame = 0;
	}

	internal static void OnWorldCleared()
	{
		InvalidateGeometryCache();
		XjActorTileTargetBridge.ClearCache();
		_lastTerrainIntegrityYear = -1;
		_lastSanctuaryDefenseYear = -1;
		_lastTerritoryRepairYear = -1;
		_lastSanctuaryPopulationArchive = null;
		_nextMaintenanceProbeUnityFrame = 0;
		SanctuaryActorBuffer.Clear();
		SanctuaryActorSeen.Clear();
		SanctuaryMineralBuffer.Clear();
		SanctuaryMineralSeen.Clear();
		PeriodicMoHeReturnQueue.Clear();
		QueuedPeriodicMoHeReturnIds.Clear();
		SanctuaryRouteSuppressions.Clear();
		SanctuaryWaterRecoveries = 0;
	}

	internal enum PlacementResult : byte
	{
		Success = 0,
		AlreadyExists = 1,
		InvalidWorld = 2,
		TerrainUnavailable = 3,
		DomainRegistrationFailed = 4,
		Failed = 5,
		CoreOutOfBounds = 6
	}

	/// <summary>
	/// 领域放置命令。这里只处理地形、领域状态、边界保护和今释归入；
	/// 神力按钮、提示文本与选中状态全部由 UI 适配层负责。
	/// </summary>
	internal static PlacementResult TryPlaceAtTile(WorldTile tile)
	{
		if (tile == null || World.world == null || World.world.map_stats?.custom_data == null)
			return PlacementResult.InvalidWorld;
		if (IsPlaced) return PlacementResult.AlreadyExists;
		try
		{
			int centerX = tile.x;
			int centerY = tile.y;
			int year = Math.Max(1, XjYearTracker.CurrentYear > 0
				? XjYearTracker.CurrentYear : World.world.map_stats?.year ?? 1);

			// 海陆均可开辟，但完整的佛土与三重封界必须全部落在地图内。
			// 幽冥本身也是完整实体圆环，不依赖世界边缘替它补封口。
			if (!CanFitPhysicalDomain(centerX, centerY))
				return PlacementResult.CoreOutOfBounds;
			if (!PaintDomainTerrain(centerX, centerY))
				return PlacementResult.TerrainUnavailable;
			if (!XjShiDomainState.PlaceZhantanlin(centerX, centerY, DefaultRadius, year))
				return PlacementResult.DomainRegistrationFailed;
			InvalidateGeometryCache();

			XjShiDomainState.MarkZhantanlinTerrainSchema(CurrentTerrainSchema);
			EnsureTerritoryProtectionApplied(year, force: true);
			ClearExternalRoutesIntoSanctuary();
			ProcessPendingEntries(year);
			_lastSanctuaryDefenseYear = year;
			Debug.Log("[玄鉴][旃檀林] 已开辟于 (" + centerX + ", " + centerY + ")，佛土半径 "
				+ DefaultRadius + "，外围3格光墙—3格不灭岩浆—3格光墙三重实体封界至半径 " + OuterBoundaryRadius + "。");
			return PlacementResult.Success;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.TryPlaceAtTile", ex);
			return PlacementResult.Failed;
		}
	}

	// 兼容旧内部调用：不再承担任何 UI 副作用。
	public static bool CreateAtTile(WorldTile tile, string pPowerID)
	{
		_ = pPowerID;
		return TryPlaceAtTile(tile) == PlacementResult.Success;
	}

	public static bool Place(WorldTile tile, string powerId = null) => CreateAtTile(tile, powerId);

	private static bool PaintDomainTerrain(int centerX, int centerY)
	{
		// 海陆均可作为放置输入，但核心与三重封界必须完整存在，不能截断在世界边缘。
		if (!CanFitPhysicalDomain(centerX, centerY)) return false;
		if (!TryResolveTerrainAssets(out BiomeAsset biome, out TopTileType desertLow,
			out TopTileType lightWall, out TileType lavaType)) return false;
		if (!PaintSanctuaryLand(centerX, centerY, DefaultRadius, biome, desertLow)) return false;
		return PaintBoundaryBands(centerX, centerY, lightWall, lavaType);
	}

	private static bool CanFitPhysicalDomain(int centerX, int centerY)
	{
		if (World.world == null || MapBox.width <= 0 || MapBox.height <= 0) return false;
		return centerX - OuterBoundaryRadius >= 0
			&& centerY - OuterBoundaryRadius >= 0
			&& centerX + OuterBoundaryRadius < MapBox.width
			&& centerY + OuterBoundaryRadius < MapBox.height;
	}

	private static bool TryResolveTerrainAssets(out BiomeAsset biome, out TopTileType desertLow,
		out TopTileType lightWall, out TileType lavaType)
	{
		biome = null;
		desertLow = null;
		lightWall = null;
		lavaType = null;
		if (World.world == null || MapBox.width <= 0 || MapBox.height <= 0
			|| AssetManager.biome_library == null || AssetManager.tiles == null)
			return false;

		for (int i = 0; i < ArcaneDesertBiomeIds.Length && biome == null; i++)
		{
			try { biome = AssetManager.biome_library.get(ArcaneDesertBiomeIds[i]); }
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjZhantanlinSystem.ResolveArcaneDesertBiome", ex);
			}
		}

		if (biome != null)
		{
			try { desertLow = biome.getTileLow(); }
			catch (Exception ex)
			{
				XjExceptionDiagnostics.Report("XjZhantanlinSystem.ResolveArcaneDesertTile", ex);
			}
		}
		lightWall = ResolveNativeLightWallType();
		lavaType = ResolveNativePersistentLavaType();

		if (biome != null && desertLow != null && lightWall != null && lavaType != null)
		{
			_terrainWarningIssued = false;
			return true;
		}

		if (!_terrainWarningIssued)
		{
			_terrainWarningIssued = true;
			string missing = biome == null
				? "biome_desert（旃檀佛土地形）"
				: desertLow == null
					? "desert_low（旃檀佛土顶层）"
					: lightWall == null
						? "原生 wall_light 光墙"
						: "原生 lava 岩浆地形";
			Debug.LogWarning("[玄鉴][旃檀林] 地形资源未就绪：" + missing
				+ "。本次不会生成佛土或三重实体封界。");
		}
		return false;
	}

	private static TopTileType ResolveNativeLightWallType()
	{
		if (_cachedNativeLightWall != null) return _cachedNativeLightWall;
		try
		{
			// This exact WorldBox symbol is already used by the pre-existing 0.9.9.2
			// Zhantanlin legacy-terrain code, so prefer the native asset directly instead
			// of guessing field names through reflection.
			TopTileType candidate = TopTileLibrary.wall_light;
			if (candidate != null) _cachedNativeLightWall = candidate;
			return candidate;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ResolveVanillaLightWall", ex);
			return null;
		}
	}

	private static TileType ResolveNativePersistentLavaType()
	{
		if (_cachedNativePersistentLava != null) return _cachedNativePersistentLava;
		try
		{
			// 与玄门幽冥保持一致：固定使用最高级原生 lava3，
			// 不再猜 lava0/lava1 等不同层级，避免地形可达性与凝固行为不一致。
			TileType candidate = TileLibrary.lava3;
			if (candidate != null && candidate.lava) _cachedNativePersistentLava = candidate;
			return _cachedNativePersistentLava;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ResolveVanillaLava3", ex);
			return null;
		}
	}

	private static bool PaintSanctuaryLand(int centerX, int centerY, int radius,
		BiomeAsset biome, TopTileType desertLow)
	{
		if (World.world == null || biome == null || desertLow == null) return false;
		int resolvedRadius = Math.Clamp(radius, MinimumRadius, MaximumRadius);
		int radiusSquared = resolvedRadius * resolvedRadius;
		int expectedTiles = 0;
		int paintedTiles = 0;

		for (int dy = -resolvedRadius; dy <= resolvedRadius; dy++)
		{
			int y = centerY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -resolvedRadius; dx <= resolvedRadius; dx++)
			{
				if (dx * dx + dy * dy > radiusSquared) continue;
				int x = centerX + dx;
				if (x < 0 || x >= MapBox.width) continue;
				expectedTiles++;
				WorldTile tile = World.world.GetTileSimple(x, y);
				if (tile == null) continue;
				MapAction.terraformMain(tile, TileLibrary.soil_low, TerraformLibrary.flash, false);
				TopTileType topTile = biome.getTile(tile) ?? desertLow;
				if (topTile == null) continue;
				tile.setTopTileType(topTile, true);
				paintedTiles++;
			}
		}

		if (expectedTiles > 0 && paintedTiles == expectedTiles) return true;
		Debug.LogWarning("[玄鉴][旃檀林] 佛土铺设不完整：目标 "
			+ expectedTiles + " 格，实际完成 " + paintedTiles + " 格；已中止领域登记与三重封界生成。");
		return false;
	}

	private enum SanctuaryBoundaryBand : byte
	{
		None = 0,
		InnerLightWall = 1,
		Lava = 2,
		OuterLightWall = 3
	}

	private static SanctuaryBoundaryBand ResolveBoundaryBand(int distanceSquared)
	{
		int coreSquared = DefaultRadius * DefaultRadius;
		if (distanceSquared <= coreSquared) return SanctuaryBoundaryBand.None;
		int innerWallOuter = DefaultRadius + InnerLightWallThickness;
		if (distanceSquared <= innerWallOuter * innerWallOuter) return SanctuaryBoundaryBand.InnerLightWall;
		int lavaOuter = innerWallOuter + LavaRingThickness;
		if (distanceSquared <= lavaOuter * lavaOuter) return SanctuaryBoundaryBand.Lava;
		if (distanceSquared <= OuterBoundaryRadius * OuterBoundaryRadius) return SanctuaryBoundaryBand.OuterLightWall;
		return SanctuaryBoundaryBand.None;
	}

	private static bool PaintBoundaryBands(int centerX, int centerY, TopTileType lightWall, TileType lavaType)
	{
		if (World.world == null || lightWall == null || lavaType == null) return false;
		int expectedTiles = 0;
		int paintedTiles = 0;
		for (int dy = -OuterBoundaryRadius; dy <= OuterBoundaryRadius; dy++)
		{
			int y = centerY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -OuterBoundaryRadius; dx <= OuterBoundaryRadius; dx++)
			{
				SanctuaryBoundaryBand band = ResolveBoundaryBand(dx * dx + dy * dy);
				if (band == SanctuaryBoundaryBand.None) continue;
				int x = centerX + dx;
				if (x < 0 || x >= MapBox.width) continue;
				expectedTiles++;
				WorldTile tile = World.world.GetTileSimple(x, y);
				if (tile == null) continue;
				if (PaintBoundaryTile(tile, band, lightWall, lavaType)) paintedTiles++;
			}
		}
		if (expectedTiles > 0 && paintedTiles == expectedTiles) return true;
		Debug.LogWarning("[玄鉴][旃檀林] 三重实体封界铺设不完整：目标 "
			+ expectedTiles + " 格，实际完成 " + paintedTiles + " 格。");
		return false;
	}

	private static bool PaintBoundaryTile(WorldTile tile, SanctuaryBoundaryBand band,
		TopTileType lightWall, TileType lavaType)
	{
		if (tile == null) return false;
		try
		{
			if (band == SanctuaryBoundaryBand.Lava)
			{
				MapAction.terraformMain(tile, lavaType, TerraformLibrary.flash, false);
				return tile.main_type == lavaType || (tile.Type != null && tile.Type.lava);
			}
			MapAction.terraformMain(tile, TileLibrary.soil_low, TerraformLibrary.flash, false);
			tile.setTopTileType(lightWall, true);
			return tile.top_type == lightWall;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.PaintBoundaryTile", ex);
			return false;
		}
	}

	private static bool IsBoundaryTileIntact(WorldTile tile, SanctuaryBoundaryBand band,
		TopTileType lightWall, TileType lavaType)
	{
		if (tile == null || band == SanctuaryBoundaryBand.None) return false;
		if (band == SanctuaryBoundaryBand.Lava)
			return tile.main_type == lavaType && tile.Type != null && tile.Type.lava;
		return tile.main_type == TileLibrary.soil_low && tile.top_type == lightWall;
	}

	private static int RepairBoundaryBands(int centerX, int centerY, TopTileType lightWall, TileType lavaType)
	{
		if (World.world == null || lightWall == null || lavaType == null) return 0;
		int repaired = 0;
		for (int dy = -OuterBoundaryRadius; dy <= OuterBoundaryRadius; dy++)
		{
			int y = centerY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -OuterBoundaryRadius; dx <= OuterBoundaryRadius; dx++)
			{
				SanctuaryBoundaryBand band = ResolveBoundaryBand(dx * dx + dy * dy);
				if (band == SanctuaryBoundaryBand.None) continue;
				int x = centerX + dx;
				if (x < 0 || x >= MapBox.width) continue;
				WorldTile tile = World.world.GetTileSimple(x, y);
				if (IsBoundaryTileIntact(tile, band, lightWall, lavaType)) continue;
				if (PaintBoundaryTile(tile, band, lightWall, lavaType)) repaired++;
			}
		}
		return repaired;
	}


	private static int RestoreRetiredFootprint(int centerX, int centerY, int retiredOuterRadius)
	{
		if (World.world == null || retiredOuterRadius <= OuterBoundaryRadius) return 0;
		int currentOuterSquared = OuterBoundaryRadius * OuterBoundaryRadius;
		int retiredOuterSquared = retiredOuterRadius * retiredOuterRadius;
		int sampleRadius = retiredOuterRadius + 2;
		int restored = 0;

		// 缩小领域时，旧外围若留在海面会变成可步行孤岛并吸引原生移民/寻路。
		// 因此仅恢复“新外围之外、旧外围之内”的退役环带，按每个方向从旧范围外
		// 采样真实环境；新28核心与28~37三重封界完全不受影响。
		for (int dy = -retiredOuterRadius; dy <= retiredOuterRadius; dy++)
		{
			int y = centerY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -retiredOuterRadius; dx <= retiredOuterRadius; dx++)
			{
				int distanceSquared = dx * dx + dy * dy;
				if (distanceSquared <= currentOuterSquared || distanceSquared > retiredOuterSquared) continue;
				int x = centerX + dx;
				if (x < 0 || x >= MapBox.width) continue;

				WorldTile target = World.world.GetTileSimple(x, y);
				if (target == null) continue;
				double length = Math.Sqrt(Math.Max(1, distanceSquared));
				int sx = centerX + (int)Math.Round(dx / length * sampleRadius);
				int sy = centerY + (int)Math.Round(dy / length * sampleRadius);
				WorldTile source = GetTileInsideMap(sx, sy);
				if (source?.main_type == null) continue;
				try
				{
					MapAction.terraformMain(target, source.main_type, TerraformLibrary.flash, false);
					target.setTopTileType(source.top_type, true);
					restored++;
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("XjZhantanlinSystem.RestoreRetiredFootprint", ex);
				}
			}
		}
		return restored;
	}

	private static int RepairSanctuaryLand(int centerX, int centerY, BiomeAsset biome, TopTileType desertLow)
	{
		if (World.world == null || biome == null || desertLow == null) return 0;
		int repaired = 0;
		int radiusSquared = DefaultRadius * DefaultRadius;
		for (int dy = -DefaultRadius; dy <= DefaultRadius; dy++)
		{
			int y = centerY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -DefaultRadius; dx <= DefaultRadius; dx++)
			{
				if (dx * dx + dy * dy > radiusSquared) continue;
				int x = centerX + dx;
				if (x < 0 || x >= MapBox.width) continue;
				WorldTile tile = World.world.GetTileSimple(x, y);
				if (tile == null) continue;
				bool changed = false;
				try
				{
					bool badMain = tile.Type == null || tile.Type.lava || tile.Type.ocean || tile.Type.block
						|| tile.main_type != TileLibrary.soil_low;
					if (badMain)
					{
						MapAction.terraformMain(tile, TileLibrary.soil_low, TerraformLibrary.flash, false);
						changed = true;
					}
					TopTileType expectedTop = biome.getTile(tile) ?? desertLow;
					if (expectedTop != null && tile.top_type != expectedTop)
					{
						tile.setTopTileType(expectedTop, true);
						changed = true;
					}
					if (changed) repaired++;
				}
				catch (Exception ex)
				{
					XjExceptionDiagnostics.Report("XjZhantanlinSystem.RepairSanctuaryLand", ex);
				}
			}
		}
		return repaired;
	}


	private static WorldTile GetTileInsideMap(int x, int y)
	{
		if (World.world == null || x < 0 || x >= MapBox.width || y < 0 || y >= MapBox.height)
			return null;
		return World.world.GetTileSimple(x, y);
	}

	internal static bool HasRuntimeMaintenancePending
	{
		get
		{
			if (!IsPlaced || UnityEngine.Time.frameCount < _nextMaintenanceProbeUnityFrame) return false;
			int year = Math.Max(1, XjYearTracker.CurrentYear > 0
				? XjYearTracker.CurrentYear
				: World.world?.map_stats?.year ?? 1);
			bool archivePopulationPending = _lastSanctuaryPopulationArchive == null
				&& World.world?.map_stats?.custom_data != null;
			bool territoryDue = _lastTerritoryRepairYear < 0
				|| year - _lastTerritoryRepairYear >= TerritoryRepairIntervalYears;
			return archivePopulationPending
				|| year > _lastSanctuaryDefenseYear
				|| year > _lastTerrainIntegrityYear
				|| territoryDue
				|| XjShiDomainState.GetZhantanlinTerrainSchema() < CurrentTerrainSchema;
		}
	}

	internal static void EnsureTerrainApplied()
	{
		if (!XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId,
				out XjShiDomainRecord domain)
			|| domain == null || domain.MapRadius < MinimumRadius) return;

		int year = Math.Max(1, XjYearTracker.CurrentYear > 0
			? XjYearTracker.CurrentYear : World.world?.map_stats?.year ?? 1);
		int storedSchema = XjShiDomainState.GetZhantanlinTerrainSchema();
		bool schemaOutdated = storedSchema < CurrentTerrainSchema;

		// Schema 11 将核心从37再缩小四分之一至28，外围仍为3+3+3实体封界。
		// 新外围恰好到半径37，因此旧schema10的37~46封界必须恢复为周边自然地形；
		// 更早的56半径旧档则继续恢复其更大的退役足迹，不能留下人工孤岛。
		int previousRadius = domain.MapRadius;
		int retiredOuterRadius = 0;
		if (previousRadius >= LegacyExpandedRadius)
			retiredOuterRadius = LegacyExpandedRadius + LegacyExpandedBoundaryThickness;
		else if (storedSchema >= 10 || previousRadius >= LegacyPreviousRadius)
			retiredOuterRadius = LegacyPreviousRadius + LegacyPreviousBoundaryThickness;
		else if (storedSchema == 9)
			retiredOuterRadius = LegacyPreviousRadius + LegacyExpandedBoundaryThickness;

		if (domain.MapRadius != DefaultRadius)
		{
			XjShiDomainState.PlaceZhantanlin(domain.MapCenterX, domain.MapCenterY, DefaultRadius, year);
			domain.MapRadius = DefaultRadius;
			InvalidateGeometryCache();
			schemaOutdated = true;
		}

		SynchronizeLoadedSanctuaryPopulationOnce(year);
		EnsureSanctuaryDefenseApplied(year);
		EnsureTerritoryProtectionApplied(year);
		bool integrityDue = year > _lastTerrainIntegrityYear;
		if (!schemaOutdated && !integrityDue) return;

		if (!CanFitPhysicalDomain(domain.MapCenterX, domain.MapCenterY))
		{
			_nextMaintenanceProbeUnityFrame = UnityEngine.Time.frameCount + MaintenanceProbeBackoffFrames;
			if (!_terrainWarningIssued)
			{
				_terrainWarningIssued = true;
				Debug.LogWarning("[玄鉴][旃檀林] 现有锚点离地图边缘过近，无法完整容纳佛土与三重实体封界；请在更靠内的位置重新开辟。");
			}
			return;
		}

		if (!TryResolveTerrainAssets(out BiomeAsset biome, out TopTileType desertLow,
				out TopTileType lightWall, out TileType lavaType))
		{
			_nextMaintenanceProbeUnityFrame = UnityEngine.Time.frameCount + MaintenanceProbeBackoffFrames;
			return;
		}
		if (schemaOutdated && retiredOuterRadius > OuterBoundaryRadius)
		{
			int restoredLegacyTiles = RestoreRetiredFootprint(domain.MapCenterX, domain.MapCenterY, retiredOuterRadius);
			if (restoredLegacyTiles > 0)
				Debug.Log("[玄鉴][旃檀林] 已将缩界后退役的外围足迹恢复为周边真实地形 " + restoredLegacyTiles + " 格。");
		}
		_nextMaintenanceProbeUnityFrame = 0;
		_lastTerrainIntegrityYear = year;

		// 与幽冥一样，年度维护直接核对整个核心和三层实体环，不再只抽查少数探针。
		// 这能保证第三方地形改动、海水回填或旧档残片不会在法界内部留下可达缺口。
		int repairedCoreTiles = RepairSanctuaryLand(domain.MapCenterX, domain.MapCenterY, biome, desertLow);
		int repairedBoundaryTiles = RepairBoundaryBands(domain.MapCenterX, domain.MapCenterY, lightWall, lavaType);
		if (repairedCoreTiles > 0 || repairedBoundaryTiles > 0)
		{
			ClearExternalRoutesIntoSanctuary();
			RemoveSanctuaryMinerals();
			Debug.Log("[玄鉴][旃檀林] 已修复佛土 " + repairedCoreTiles + " 格、三重封界 "
				+ repairedBoundaryTiles + " 格，并清理与新地形冲突的旧目标。");
		}
		if (schemaOutdated) XjShiDomainState.MarkZhantanlinTerrainSchema(CurrentTerrainSchema);
	}

	private static void SynchronizeLoadedSanctuaryPopulationOnce(int year)
	{
		object currentArchive = World.world?.map_stats?.custom_data;
		if (currentArchive == null || ReferenceEquals(_lastSanctuaryPopulationArchive, currentArchive)) return;
		_lastSanctuaryPopulationArchive = currentArchive;
		AuditSanctuary(year, announceConversions: false, cleanMinerals: true);
		_lastSanctuaryDefenseYear = Math.Max(_lastSanctuaryDefenseYear, year);
	}

	private static void EnsureSanctuaryDefenseApplied(int year)
	{
		if (!IsPlaced || year <= _lastSanctuaryDefenseYear) return;
		_lastSanctuaryDefenseYear = year;
		AuditSanctuary(year, announceConversions: true, cleanMinerals: false);
	}

	private static void AuditSanctuary(int year, bool announceConversions, bool cleanMinerals)
	{
		if (!IsPlaced || World.world == null) return;
		CollectSanctuaryActors();
		int converted = 0;
		for (int i = 0; i < SanctuaryActorBuffer.Count; i++)
		{
			Actor actor = SanctuaryActorBuffer[i];
			SanctuaryIntruderResult outcome = EnforceSanctuaryOccupant(actor, year, announceConversions);
			if (outcome == SanctuaryIntruderResult.Converted) converted++;
		}

		int removedBuildings = cleanMinerals ? RemoveSanctuaryForeignBuildings() : 0;
		if (converted > 0 || removedBuildings > 0)
		{
			Debug.Log("[玄鉴][旃檀林] 净界：摄化 " + converted
				+ "，清退旧有建筑/矿物 " + removedBuildings + "；领域内角色不再执行删除。");
		}
	}

	private static void CollectSanctuaryActors()
	{
		SanctuaryActorBuffer.Clear();
		SanctuaryActorSeen.Clear();
		if (!XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null) return;
		WorldTile center = GetTileInsideMap(domain.MapCenterX, domain.MapCenterY);
		IEnumerable<Actor> nearby = null;
		try { nearby = center == null ? null : Finder.getUnitsFromChunk(center, OuterMountainRadius, 0f, false); }
		catch { nearby = null; }
		try
		{
			if (nearby == null)
			{
				// 领域局部查询不可用时宁可让后台下一轮重试，也绝不回退成全世界人口扫描。
				XjPerformanceTelemetry.ObserveQueue("shi.sanctuary.chunk-query-unavailable", 1);
				_nextMaintenanceProbeUnityFrame = Math.Max(_nextMaintenanceProbeUnityFrame,
					UnityEngine.Time.frameCount + MaintenanceProbeBackoffFrames);
				return;
			}
			foreach (Actor actor in nearby) AddSanctuaryActorCandidate(actor);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.CollectSanctuaryActors", ex);
		}
	}

	private static void AddSanctuaryActorCandidate(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !IsInside(actor)) return;
		long actorId = GetActorId(actor);
		if (actorId > 0L && !SanctuaryActorSeen.Add(actorId)) return;
		SanctuaryActorBuffer.Add(actor);
	}

	private enum SanctuaryIntruderResult
	{
		None,
		Converted
	}

	private static SanctuaryIntruderResult EnforceSanctuaryOccupant(Actor actor, int year, bool announceConversion)
	{
		if (actor?.data == null || !actor.isAlive() || !IsInside(actor)) return SanctuaryIntruderResult.None;
		if (XjCultivationPathRules.IsShi(actor))
		{
			ApplySanctuaryPeace(actor);
			return SanctuaryIntruderResult.None;
		}

		// 旃檀林的规则是“入土摄化/禁止国家化”，不是删除角色。玩家直接
		// 放置的四族角色即使尚未有资质、境界、城市或国家，也允许尝试投释；
		// 巫师、第三方角色或任何转换失败者都保留实体，只施加佛土和平约束。
		bool supportedSapient = false;
		try
		{
			supportedSapient = actor.isSapient()
				&& actor.asset != null
				&& !actor.asset.default_animal
				&& !actor.asset.is_boat;
		}
		catch { supportedSapient = false; }

		if (supportedSapient)
		{
			string formerPath = XjCultivationPathRules.IsZiFuJinDan(actor)
				? "紫府金丹道"
				: XjCultivationPathRules.IsFuQiYangXing(actor)
					? "服气养性道"
					: "世俗";
			XjShiAnnouncementSystem.SuppressNextEntered(actor);
			bool converted = XjShiState.TryForceConvertByZhantanlin(actor, Math.Max(1, year));
			XjShiAnnouncementSystem.CancelSuppressedEntered(actor);
			if (converted && XjCultivationPathRules.IsShi(actor))
			{
				XjCultivatorCache.CheckAndUpdate(actor);
				if (!IsInside(actor)) TryTeleportInside(actor, GetActorId(actor));
				ApplySanctuaryPeace(actor);
				if (announceConversion) XjShiAnnouncementSystem.OnSanctuaryConverted(actor, formerPath);
				return SanctuaryIntruderResult.Converted;
			}
		}

		ApplySanctuaryPeace(actor);
		return SanctuaryIntruderResult.None;
	}


	private static int RemoveSanctuaryForeignBuildings()
	{
		SanctuaryMineralBuffer.Clear();
		SanctuaryMineralSeen.Clear();
		if (World.world == null
			|| !XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null || domain.MapRadius < MinimumRadius) return 0;

		// 这是“幽冥式领域”和普通改地形最大的差别之一：领域落下以后，原城市的
		// 房屋、道路附属建筑、矿物若继续留在三重封界内，原生工作AI仍会把它们当作家/
		// 仓库/生产目标，从封界外成批发起永久不可达的 BehGoToTileTarget。放置、读档
		// 首次同步和完整重建时一次性清除保护圆内全部原生建筑；正常年度不扫描。
		int radius = OuterMountainRadius;
		long radiusSquared = (long)radius * radius;
		for (int dy = -radius; dy <= radius; dy++)
		{
			int y = domain.MapCenterY + dy;
			if (y < 0 || y >= MapBox.height) continue;
			for (int dx = -radius; dx <= radius; dx++)
			{
				if ((long)dx * dx + (long)dy * dy > radiusSquared) continue;
				int x = domain.MapCenterX + dx;
				if (x < 0 || x >= MapBox.width) continue;
				WorldTile tile = World.world.GetTileSimple(x, y);
				Building building = tile?.building;
				if (building == null || !SanctuaryMineralSeen.Add(building)) continue;
				SanctuaryMineralBuffer.Add(building);
			}
		}

		int removed = 0;
		for (int i = 0; i < SanctuaryMineralBuffer.Count; i++)
		{
			if (TryDestroyBuilding(SanctuaryMineralBuffer[i])) removed++;
		}
		SanctuaryMineralBuffer.Clear();
		SanctuaryMineralSeen.Clear();
		return removed;
	}

	// 兼容旧调用名；语义已经扩展为领域内全部原生建筑。
	private static int RemoveSanctuaryMinerals() => RemoveSanctuaryForeignBuildings();

	private static bool TryDestroyBuilding(Building building)
	{
		return XjTerritoryInterop.TryDestroyBuilding(building);
	}


	/// <summary>
	/// 旃檀林不是国家领土。WorldBox 的国家边界最终落在 City.addZone(TileZone)，
	/// 因此在真正写入城市领地以前直接拒绝任何与佛土相交的区块。区块是原生不可
	/// 再细分的战略单位，只要有一格落入旃檀林，就整块保持无主，避免边界颜色与
	/// 国家战争逻辑穿进释土。
	/// </summary>
	internal static bool ShouldBlockTerritoryClaim(TileZone zone)
	{
		if (!IsPlaced || zone == null) return false;
		if (zone.centerTile != null && IsInsideProtectedBoundary(zone.centerTile)) return true;
		WorldTile[] tiles = zone.tiles;
		if (tiles == null) return false;
		for (int i = 0; i < tiles.Length; i++)
		{
			if (IsInsideProtectedBoundary(tiles[i])) return true;
		}
		return false;
	}

	/// <summary>
	/// 旧档或在旃檀林放置前已经被城市占有的区块，需要从原生城市领地中剥离。
	/// 正常运行以后由 City.addZone 前置拦截，年度这里只做一次低频自愈，不扫描人口。
	/// </summary>
	private static void EnsureTerritoryProtectionApplied(int year, bool force = false)
	{
		if (!IsPlaced || World.world?.zone_calculator?.zones == null) return;
		int resolvedYear = Math.Max(1, year);
		if (!force && _lastTerritoryRepairYear >= 0
			&& resolvedYear - _lastTerritoryRepairYear < TerritoryRepairIntervalYears) return;
		_lastTerritoryRepairYear = resolvedYear;

		var zones = World.world.zone_calculator.zones;
		int detached = 0;
		foreach (TileZone zone in zones)
		{
			if (zone == null || zone.city == null || !ShouldBlockTerritoryClaim(zone)) continue;
			if (TryDetachTerritoryZone(zone)) detached++;
		}
		if (detached > 0)
		{
			Debug.Log("[玄鉴][旃檀林] 已从国家战略领地中剥离 " + detached + " 个相交区块。佛土保持无主。");
		}
	}

	private static bool TryDetachTerritoryZone(TileZone zone)
	{
		return XjTerritoryInterop.TryDetachTerritoryZone(zone);
	}

	internal static bool ShouldBlockAnimalSpawn(string actorAssetId, WorldTile tile)
	{
		if (!IsInside(tile) || string.IsNullOrWhiteSpace(actorAssetId)) return false;
		try { return AssetManager.actor_library?.get(actorAssetId)?.default_animal == true; }
		catch { return false; }
	}

	internal static bool ShouldBlockBuildingSpawn(string buildingAssetId, WorldTile tile)
	{
		_ = buildingAssetId;
		return IsInsideProtectedBoundary(tile);
	}

	/// <summary>
	/// 原生聚落创建的权威门禁。角色可以真实存在于旃檀林并投释，但只要创建事务的
	/// 发起者、落点或候选 City 与实体佛土/三重封界相交，就不能创建原生村镇。
	/// 这条规则必须早于 City.addZone：若只拦 addZone，WorldBox 已经造出的 0-zone City
	/// 会立即灭亡，AI 下一轮再次创建，形成“建立/毁灭”公告风暴。
	/// </summary>
	internal static bool ShouldBlockNativeSettlement(Actor founder, WorldTile requestedTile, City city)
	{
		if (!IsPlaced) return false;
		if (IsInsideProtectedBoundary(requestedTile)) return true;

		try
		{
			WorldTile founderTile = founder?.data == null ? null : ((BaseSimObject)founder).current_tile;
			if (IsInsideProtectedBoundary(founderTile)) return true;
		}
		catch { }

		if (city == null) return false;
		bool hasAnyZone = false;
		try
		{
			if (city.zones != null)
			{
				hasAnyZone = city.zones.Count > 0;
				for (int i = 0; i < city.zones.Count; i++)
				{
					TileZone zone = city.zones[i];
					if (zone != null && ShouldBlockTerritoryClaim(zone)) return true;
				}
			}
		}
		catch { }

		// 城市刚创建、首个 addZone 尚未成功时 zones 可能还是空的；仅在这个半状态窗口
		// 用成员实体位置判断。已有正常领地的城市不会因为某个访客进入旃檀林而被误拦。
		if (hasAnyZone) return false;
		try
		{
			if (city.units != null)
			{
				int checks = 0;
				foreach (Actor member in city.units)
				{
					if (checks++ >= 16) break;
					if (member?.data == null) continue;
					WorldTile memberTile = null;
					try { memberTile = ((BaseSimObject)member).current_tile; } catch { memberTile = null; }
					if (IsInsideProtectedBoundary(memberTile)) return true;
				}
			}
		}
		catch { }
		return false;
	}

	internal static bool ShouldBlockNativeKingdomBinding(City city, Kingdom targetKingdom)
	{
		_ = targetKingdom;
		return ShouldBlockNativeSettlement(null, null, city);
	}

	/// <summary>
	/// 旃檀林内可以存在角色，也可以发生投释，但不能以佛土中的角色/城市为起点
	/// 创建原生国家。这个门禁只挂在低频建国事务上，不触碰 Kingdom.setKing 的
	/// 完整换王事务，避免制造原生职业、历史与外交容器的半状态。
	/// </summary>
	internal static bool ShouldBlockKingdomFounding(City city, Actor founder)
	{
		if (!IsPlaced) return false;
		try
		{
			WorldTile founderTile = founder?.data == null ? null : ((BaseSimObject)founder).current_tile;
			if (IsInsideProtectedBoundary(founderTile)) return true;
		}
		catch { }

		// 兼容读档后遗留城市：即使建国发起者恰好已经走到封界外，只要原城市
		// 仍持有任何与旃檀林相交的旧 TileZone，本次建国也必须拒绝；低频自愈
		// 随后会把这些旧区块从城市领地中剥离。
		if (city == null || World.world?.zone_calculator?.zones == null) return false;
		try
		{
			foreach (TileZone zone in World.world.zone_calculator.zones)
			{
				if (zone?.city != city) continue;
				if (ShouldBlockTerritoryClaim(zone)) return true;
			}
		}
		catch { }
		return false;
	}

	// 旧名保留给兼容检查；旃檀林现在拒绝的是全部原生建筑，而不只是矿物。
	internal static bool ShouldBlockMineralSpawn(string buildingAssetId, WorldTile tile)
	{
		return ShouldBlockBuildingSpawn(buildingAssetId, tile);
	}


	internal static bool IsPlaced
	{
		get => TryGetCachedGeometry(out _, out _, out _);
	}

	private static void InvalidateGeometryCache()
	{
		_geometryCacheUnityFrame = -1;
		_geometryCachePlaced = false;
		_geometryCacheCenterX = 0;
		_geometryCacheCenterY = 0;
		_geometryCacheRadius = 0;
	}

	private static bool TryGetCachedGeometry(out int centerX, out int centerY, out int radius)
	{
		int frame = Time.frameCount;
		if (_geometryCacheUnityFrame != frame)
		{
			_geometryCacheUnityFrame = frame;
			_geometryCachePlaced = XjShiDomainState.TryGet(
				XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
				&& domain != null && domain.MapRadius >= MinimumRadius;
			if (_geometryCachePlaced)
			{
				_geometryCacheCenterX = domain.MapCenterX;
				_geometryCacheCenterY = domain.MapCenterY;
				_geometryCacheRadius = domain.MapRadius;
			}
		}

		centerX = _geometryCacheCenterX;
		centerY = _geometryCacheCenterY;
		radius = _geometryCacheRadius;
		return _geometryCachePlaced;
	}

	private static bool IsInsideCached(int tileX, int tileY, int centerX, int centerY, int radius)
	{
		long dx = tileX - centerX;
		long dy = tileY - centerY;
		long r = radius;
		return dx * dx + dy * dy <= r * r;
	}

	internal static bool IsInside(WorldTile tile)
	{
		return tile != null
			&& TryGetCachedGeometry(out int centerX, out int centerY, out int radius)
			&& IsInsideCached(tile.x, tile.y, centerX, centerY, radius);
	}

	internal static bool IsInsideProtectedBoundary(WorldTile tile)
	{
		return tile != null && IsInsideProtectedBoundary(tile.x, tile.y);
	}

	internal static bool IsInsideProtectedBoundary(int tileX, int tileY)
	{
		return TryGetCachedGeometry(out int centerX, out int centerY, out _)
			&& IsInsideCached(tileX, tileY, centerX, centerY, OuterMountainRadius);
	}

	/// <summary>
	/// 旃檀林岩浆环永久采用原生“不灭岩浆”世界法则的停滞语义。
	/// 原生法则是在 WorldBehaviourActionLava.tryToMoveLava 入口直接停止岩浆
	/// 冷却/流动；这里仅把同一语义局部施加于旃檀林的三格 lava3 环，
	/// 不会强行开启玩家全世界的不灭岩浆法则。
	/// </summary>
	internal static bool IsPermanentLavaBoundaryTile(WorldTile tile)
	{
		if (tile?.Type == null || !tile.Type.lava) return false;
		if (!TryGetCachedGeometry(out int centerX, out int centerY, out _)) return false;
		int dx = tile.x - centerX;
		int dy = tile.y - centerY;
		return ResolveBoundaryBand(dx * dx + dy * dy) == SanctuaryBoundaryBand.Lava;
	}

	internal static bool IsInside(Actor actor)
	{
		if (actor?.data == null) return false;
		try { return IsInside(((BaseSimObject)actor).current_tile); }
		catch { return false; }
	}

	internal static bool IsModernShiInside(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor) || !IsInside(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		return string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
	}

	internal static bool IsLockedInside(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		// 今释摩诃可以出林行走，只在周期归林后的短暂停驻期内禁止离开；
		// 其他今释仍以旃檀林为常住实体释土。古释不受此规则约束。
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			int year = ResolveCurrentYear(actor);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear,
				out int returnUntilYear);
			return returnUntilYear >= year;
		}
		return true;
	}

	internal static bool RequiresPlacedConfinement(string tradition, string realm)
	{
		return string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm);
	}

	/// <summary>
	/// 今释只有摩诃及以上的高位肉身必须归入旃檀林。僧侣、法师与怜愍死亡后的
	/// 真灵仍循自身/座主的真实承载地归返；尤其怜愍应回到所挂靠摩诃的释土，
	/// 不能因为旃檀林尚未开辟而把整条低位轮回链永久卡死。
	/// </summary>
	internal static bool RequiresPhysicalReturnToZhantanlin(string tradition, string realm)
	{
		return string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe);
	}

	/// <summary>
	/// 今释肉身始终归入旃檀林；只有释修命数达到摩诃级门槛且真实持有金地者，
	/// 才将自身金地改作真灵与轮回锚点。释修不读取 XjZz 或道慧。
	/// </summary>
	internal static bool ShouldPreferOwnedJinDi(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return false;
		if (XjShiMingShuSystem.GetValue(actor) < XjShiCatalog.MoHeFateLeapMingShuThreshold) return false;
		return XjShiDomainState.TryGetOwnedNorthJinDi(GetActorId(actor), out _);
	}

	internal static string ResolvePreferredAnchor(Actor actor, string fallbackDomainId, int year)
	{
		if (actor?.data == null) return fallbackDomainId ?? string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
			return fallbackDomainId ?? string.Empty;

		XjShiDomainState.EnsureZhantanlin(Math.Max(1, year));
		if (ShouldPreferOwnedJinDi(actor)
			&& XjShiDomainState.TryGetOwnedNorthJinDi(GetActorId(actor), out XjShiDomainRecord owned))
		{
			return owned.DomainId;
		}
		return XjShiDomainCatalog.ZhantanlinDomainId;
	}


	internal static void OnBecameModern(Actor actor, int year)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return;
		int resolvedYear = Math.Max(1, year);
		XjShiSectPolicy.EnforceDetached(actor, resolvedYear);
		XjShiDomainState.EnsureZhantanlin(resolvedYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		int rank = XjShiCatalog.GetRank(realm);
		if (rank < XjShiCatalog.GetRank(XjShiRealmIds.LianMin))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, XjShiDomainCatalog.ZhantanlinDomainId);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, XjShiDomainCatalog.ZhantanlinDomainId);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus,
				IsPlaced ? XjShiJinDiStatusIds.Manifest : XjShiJinDiStatusIds.WaitingForRebirth);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
				XjShiDomainCatalog.ZhantanlinDomainId);
		}
		else
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthAnchorId, out string currentAnchor);
			string preferred = ResolvePreferredAnchor(actor,
				string.IsNullOrWhiteSpace(domainId) ? currentAnchor : domainId, resolvedYear);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, preferred);
		}
		if (!IsPlaced) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, out int firstEntry);
		bool isMoHe = string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal);
		// 所有今释第一次建立身份时立即入林。摩诃完成首次归入后即可外出行走，
		// 不再被每次年度对账强制拉回；后续由独立周期归林调度处理。
		if (firstEntry <= 0)
		{
			if (isMoHe && !IsInside(actor)) RememberMoHeReturnOrigin(actor, overwrite: true);
			if (!IsInside(actor) && !TryTeleportInside(actor, actorIdSalt: GetActorId(actor))) return;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, 1);
			if (isMoHe)
			{
				// 初次归入只短驻当前世界年；下一年度主动送回原来外界落点。
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, resolvedYear);
				ScheduleNextMoHeReturn(actor, resolvedYear);
			}
		}
		else if (!isMoHe && !IsInside(actor))
		{
			TryTeleportInside(actor, actorIdSalt: GetActorId(actor));
		}
		SynchronizeSanctuaryPeace(actor);
	}

	internal static void EnforceActor(Actor actor, int year)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return;
		int resolvedYear = Math.Max(1, year);
		XjShiSectPolicy.EnforceDetached(actor, resolvedYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			// 古释不受旃檀林居住约束，但只要实际身处林中，仍遵守绝对止战。
			SynchronizeSanctuaryPeace(actor);
			return;
		}
		OnBecameModern(actor, resolvedYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
		{
			HandlePeriodicMoHeReturn(actor, resolvedYear);
			SynchronizeSanctuaryPeace(actor);
			return;
		}
		if (IsPlaced && !IsInside(actor)) TryTeleportInside(actor, GetActorId(actor));
		SynchronizeSanctuaryPeace(actor);
	}

	private static bool SegmentTouchesProtectedBoundary(WorldTile start, WorldTile end,
		int centerX, int centerY, int radius)
	{
		if (start == null || end == null) return false;
		double ax = start.x - centerX;
		double ay = start.y - centerY;
		double bx = end.x - centerX;
		double by = end.y - centerY;
		double dx = bx - ax;
		double dy = by - ay;
		double len2 = dx * dx + dy * dy;
		double t = len2 <= 0.0001 ? 0.0 : Math.Max(0.0, Math.Min(1.0, -(ax * dx + ay * dy) / len2));
		double nx = ax + dx * t;
		double ny = ay + dy * t;
		double r = Math.Max(1, radius);
		return nx * nx + ny * ny <= r * r;
	}

	private static bool ShouldRejectDuringSanctuaryBackoff(Actor actor, WorldTile target)
	{
		if (actor?.data == null || target == null) return false;
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !SanctuaryRouteSuppressions.TryGetValue(actorId, out SanctuaryRouteSuppression state)) return false;
		int frame = Time.frameCount;
		if (frame >= state.UntilFrame)
		{
			SanctuaryRouteSuppressions.TryRemove(actorId, out _);
			return false;
		}
		if (XjCultivationPathRules.IsShi(actor))
		{
			SanctuaryRouteSuppressions.TryRemove(actorId, out _);
			return false;
		}

		bool sameTarget = Math.Abs(target.x - state.TargetX) <= 2 && Math.Abs(target.y - state.TargetY) <= 2;
		if (sameTarget || IsInsideProtectedBoundary(target)) return true;
		if (!TryGetCachedGeometry(out int centerX, out int centerY, out _)) return false;
		if (IsAdjacentSanctuaryLiquidTarget(target, centerX, centerY)) return true;
		// 新目标既不在法界，也不是紧邻法界的异常水格，就立即解除退避。
		// 不再用“起点到终点的直线是否穿圆”推测真实寻路，否则会误伤本可绕行的原生路线。
		// AI 已经选到一个与领域无关的新目标，立即解除退避，保持原生行为活跃。
		SanctuaryRouteSuppressions.TryRemove(actorId, out _);
		return false;
	}

	private static void RegisterSanctuaryRouteSuppression(Actor actor, WorldTile target)
	{
		if (actor?.data == null) return;
		long actorId = GetActorId(actor);
		if (actorId <= 0L) return;
		int targetX = target?.x ?? int.MinValue;
		int targetY = target?.y ?? int.MinValue;
		int strikes = 1;
		int originX = int.MinValue;
		int originY = int.MinValue;
		bool hasPrevious = SanctuaryRouteSuppressions.TryGetValue(actorId, out SanctuaryRouteSuppression previous);
		bool samePreviousTarget = hasPrevious
			&& Math.Abs(previous.TargetX - targetX) <= 2 && Math.Abs(previous.TargetY - targetY) <= 2;
		if (hasPrevious)
		{
			// 目标对象可能在 b5/updateFall 之间已被原生清空；无论目标是否仍相同，
			// 都保留首次确认的安全陆地原点，避免落水后因 target=null 丢失返回点。
			originX = previous.OriginX;
			originY = previous.OriginY;
		}
		if (samePreviousTarget) strikes = Math.Min(4, previous.Strikes + 1);
		if (originX == int.MinValue)
		{
			WorldTile current = null;
			try { current = ((BaseSimObject)actor).current_tile; } catch { current = null; }
			if (IsSafeExternalTile(current))
			{
				originX = current.x;
				originY = current.y;
			}
		}

		float timeScale = Config.time_scale_asset?.multiplier ?? Time.timeScale;
		float scale = Mathf.Clamp(timeScale * 0.25f, 1f, 5f);
		if (XjRuntimeWorkBudget.WorldUnitCount >= 4000) scale *= 1.25f;
		else if (XjRuntimeWorkBudget.WorldUnitCount >= 3000) scale *= 1.18f;
		else if (XjRuntimeWorkBudget.WorldUnitCount >= 1000) scale *= 1.10f;
		scale *= XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Mild => 1.10f,
			XjRuntimeStressTier.Severe => 1.20f,
			XjRuntimeStressTier.Critical => 1.30f,
			_ => 1f
		};
		float repeatedRouteScale = 1f + (strikes - 1) * 0.45f;
		int duration = Mathf.Clamp(Mathf.CeilToInt(SanctuaryRouteBackoffBaseFrames * scale * repeatedRouteScale),
			SanctuaryRouteBackoffBaseFrames, SanctuaryRouteBackoffMaxFrames);
		SanctuaryRouteSuppressions[actorId] = new SanctuaryRouteSuppression(
			Time.frameCount + duration, targetX, targetY, strikes, originX, originY);

		if (SanctuaryRouteSuppressions.Count > SanctuaryRouteSuppressionMaxIds)
		{
			// 只在异常大规模失路时整体释放短期退避状态；绝不缓存无限 ActorId。
			SanctuaryRouteSuppressions.Clear();
		}
	}

	/// <summary>
	/// 在原生AI把目标格写入角色之前拒绝“外界普通角色 -> 旃檀林/三重实体封界”的目标。
	/// 过去只在 goTo 阶段 ClearPath，会让 BehGoToTileTarget 保留同一个 tileTarget
	/// 并持续重试；现在从目标赋值入口就切断这类永久寻路。释修仍可写入目标，
	/// 随后的 goTo 会把它解释成一次领域转移。
	/// </summary>
	internal static bool ShouldRejectExternalTileTarget(Actor actor, WorldTile target)
	{
		if (actor?.data == null || target == null
			|| !TryGetCachedGeometry(out int centerX, out int centerY, out int radius)) return false;
		if (ShouldRejectDuringSanctuaryBackoff(actor, target))
		{
			CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
			return true;
		}

		// setTileTarget 是全体 Actor 的高频入口。先只看“目标格是否靠近旃檀林”，
		// 绝大多数世界目标在这里 O(1) 退出，禁止为远处普通小人读取修炼体系、城市、
		// 当前行为或反射 tileTarget。0.9.8.11 的顺序会先 IsShi(actor)，在大人口下
		// 把一次领域隔离修复放大成全世界寻路税。
		bool targetProtected = IsInsideCached(target.x, target.y, centerX, centerY, OuterMountainRadius);
		bool adjacentLiquidCandidate = !targetProtected
			&& IsAdjacentSanctuaryLiquidTarget(target, centerX, centerY);
		if (!targetProtected && !adjacentLiquidCandidate) return false;

		WorldTile currentTile = null;
		try { currentTile = ((BaseSimObject)actor).current_tile; } catch (System.Exception xjCaught099_1) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("0.9.9/Systems/Shi/XjZhantanlinSystem.cs#1", xjCaught099_1); }
		if (currentTile != null && IsInsideCached(currentTile.x, currentTile.y, centerX, centerY, radius)) return false;
		if (adjacentLiquidCandidate && (!IsExternalLandUnit(actor) || HasValidNativeMaritimeTransit(actor))) return false;
		if (XjCultivationPathRules.IsShi(actor)) return false;

		// 关键修复：不是只 ClearPath，而是终止原生行为并把旧 tileTarget 本身置空。
		// 否则 BehGoToTileTarget 会在下一拍继续拿同一个“佛土/近墙水格”重新跳水。
		CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
		XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
		return true;
	}

	/// <summary>
	/// 仅供 RegionPathFinder 异常恢复判断是否与旃檀林隔离拓扑相关。
	/// 不扫描人口，只检查当前格、目标格、既有短路径和两端连线是否穿过保护圆。
	/// </summary>
	internal static bool IsPathIsolationRelevant(Actor actor, WorldTile target)
	{
		if (!TryGetCachedGeometry(out int centerX, out int centerY, out _) || actor?.data == null) return false;
		WorldTile currentTile = null;
		try { currentTile = ((BaseSimObject)actor).current_tile; } catch (System.Exception xjCaught099_2) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("0.9.9/Systems/Shi/XjZhantanlinSystem.cs#2", xjCaught099_2); }
		if (currentTile == null) return false;
		if (IsInsideCached(currentTile.x, currentTile.y, centerX, centerY, OuterMountainRadius)) return true;
		if (target != null && IsInsideCached(target.x, target.y, centerX, centerY, OuterMountainRadius)) return true;
		if (RouteTouchesProtectedBoundary(actor)) return true;
		if (target == null) return false;

		return SegmentTouchesProtectedBoundary(currentTile, target, centerX, centerY, OuterMountainRadius + 2);
	}

	internal static bool TryBlockTravel(Actor actor, WorldTile target, ref ExecuteEvent result)
	{
		if (actor?.data == null || target == null
			|| !TryGetCachedGeometry(out int centerX, out int centerY, out int radius)) return false;
		// 部分原生行为会保留自己的局部目标并直接重复 goTo，而不再次经过
		// setTileTarget。退避必须同时挂在 goTo 前置，否则仍会每拍重新跑
		// RegionPathFinder，Postfix 虽能取消却无法消除“重复算坏路线”的CPU/跳水循环。
		if (ShouldRejectDuringSanctuaryBackoff(actor, target))
		{
			CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
			XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
			result = ExecuteEvent.True;
			return true;
		}

		WorldTile currentTile = null;
		try { currentTile = ((BaseSimObject)actor).current_tile; } catch (System.Exception xjCaught099_3) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("0.9.9/Systems/Shi/XjZhantanlinSystem.cs#3", xjCaught099_3); }
		bool currentInside = currentTile != null
			&& IsInsideCached(currentTile.x, currentTile.y, centerX, centerY, radius);
		bool targetProtected = IsInsideCached(target.x, target.y, centerX, centerY, OuterMountainRadius);

		// goTo 同样是全世界高频入口。只有“人已在法界内”或“目标真正靠近法界”
		// 才进入释修/行为状态判断。远离旃檀林的普通寻路完全不读取 InterestIndex、
		// 修炼体系、城市或原生行为对象。
		bool adjacentLiquid = false;
		if (!currentInside && !targetProtected)
		{
			if (!IsAdjacentSanctuaryLiquidTarget(target, centerX, centerY)) return false;
			if (!IsExternalLandUnit(actor) || HasValidNativeMaritimeTransit(actor)) return false;
			adjacentLiquid = true;
		}

		bool targetInside = targetProtected
			&& IsInsideCached(target.x, target.y, centerX, centerY, radius);
		bool isShi = XjCultivationPathRules.IsShi(actor);

		// 与幽冥的处理边界一致：只有角色真实身处领域，才执行摄化/抹除。
		if (currentInside && !isShi)
		{
			SanctuaryIntruderResult outcome = EnforceSanctuaryOccupant(actor, ResolveCurrentYear(actor), announceConversion: true);
			if (outcome != SanctuaryIntruderResult.None)
			{
				result = ExecuteEvent.True;
				return true;
			}
		}

		// 旃檀林是领域而非原生可步行岛屿。
		if (!currentInside && (targetProtected || adjacentLiquid))
		{
			if (!isShi)
			{
				CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
				XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
			}
			else
			{
				CancelSanctuaryRoute(actor, resetBehaviour: false, clearNativeTileTarget: true);
				if (targetProtected && TryTeleportInside(actor, GetActorId(actor))) ApplySanctuaryPeace(actor);
			}
			result = ExecuteEvent.True;
			return true;
		}

		if (currentInside && isShi) ApplySanctuaryPeace(actor);
		if (currentInside && IsLockedInside(actor) && !targetInside)
		{
			ApplySanctuaryPeace(actor);
			CancelSanctuaryRoute(actor);
			result = ExecuteEvent.True;
			return true;
		}

		if (currentInside && isShi && !targetInside)
		{
			CancelSanctuaryRoute(actor);
			if (!targetProtected && TryTeleportOutside(actor, target)) ReleaseTemporaryPeaceful(actor);
			result = ExecuteEvent.True;
			return true;
		}

		if (currentInside && !targetInside) ReleaseTemporaryPeaceful(actor);
		return false;
	}

	private static bool TryTeleportOutside(Actor actor, WorldTile target)
	{
		if (actor?.data == null || target == null || IsInsideProtectedBoundary(target)) return false;
		try
		{
			// 原生 AI 偶尔会把水面中间格作为跨海路径节点；领域出口绝不接受液体目标。
			if (target.Type != null && (target.Type.ocean || target.Type.liquid || target.Type.lava)) return false;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.TryTeleportOutside.TileType", ex);
			return false;
		}

		// 领域进出恢复为幽冥式“完整实体转移”：终止旧行为后使用原生 spawnOn
		// 重挂地图/Region，再由下一拍AI重新决策。不能继续用低层 setCurrentTilePosition
		// 留下跨领域旧任务与半更新Region状态。
		return XjHighRealmMovement.TryImmediateDomainTransfer(actor, target);
	}

	private static void CancelSanctuaryRoute(Actor actor, bool resetBehaviour = false,
		bool clearNativeTileTarget = false)
	{
		if (actor?.data == null) return;
		try { actor.stopMovement(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinSystem.CancelRoute.Stop", ex); }
		// clearOldPath 是 WorldBox 暴露的路径作废入口；不直接操作 current_path 容器。
		// 这样旃檀林只判定/取消坏路线，不成为第二个 PathFinder 写者。
		try { actor.clearOldPath(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinSystem.CancelRoute.ClearOldPath", ex); }
		if (clearNativeTileTarget) ClearNativeTileTarget(actor);
		if (resetBehaviour)
		{
			try { actor.cancelAllBeh(); }
			catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinSystem.CancelRoute.CancelBeh", ex); }
		}
	}

	private static WorldTile TryGetNativeTileTarget(Actor actor)
	{
		return XjActorTileTargetBridge.Read(actor);
	}

	private static void ClearNativeTileTarget(Actor actor)
	{
		XjActorTileTargetBridge.Clear(actor);
	}

	/// <summary>
	/// 放置/完整重铺地形属于极低频操作，可以一次性清理已经缓存、且会穿向佛土的旧路径。
	/// 正常运行不扫描世界人口；新 goTo 请求由 TryBlockTravel 常数时间拦截。
	/// </summary>
	private static void ClearExternalRoutesIntoSanctuary()
	{
		if (!IsPlaced || World.world == null
			|| !XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null) return;
		WorldTile center = GetTileInsideMap(domain.MapCenterX, domain.MapCenterY);
		IEnumerable<Actor> nearby = null;
		try { nearby = center == null ? null : Finder.getUnitsFromChunk(center, OuterMountainRadius + 24, 0f, false); }
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ClearExternalRoutes.QueryLocal", ex);
		}
		if (nearby == null) return;
		int cleared = 0;
		try
		{
			foreach (Actor actor in nearby)
			{
				if (actor?.data == null || !actor.isAlive() || IsInside(actor)) continue;
				if (!HasProtectedNativeIntent(actor)) continue;
				// 放置/补墙是极低频冷路径，此时通过原生路径作废入口清旧路径，
				// 并清 tileTarget 与旧 Beh；否则旧工作目标仍会在下一拍驱使角色重新跳水。
				CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
				XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
				cleared++;
			}
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ClearExternalRoutes", ex);
		}
		if (cleared > 0)
		{
			Debug.Log("[玄鉴][旃檀林] 地形更新后局部清理了 " + cleared + " 条指向佛土/三重实体封界的旧寻路。");
		}
	}

	private static bool RouteTouchesProtectedBoundary(Actor actor)
	{
		if (actor?.data == null) return false;
		try
		{
			if (actor.current_path == null || actor.current_path.Count == 0) return false;
			for (int i = 0; i < actor.current_path.Count; i++)
			{
				WorldTile pathTile = actor.current_path[i];
				if (pathTile != null && IsInsideProtectedBoundary(pathTile)) return true;
			}
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.CheckExternalRoute", ex);
		}
		return false;
	}


	private static bool HasProtectedNativeIntent(Actor actor)
	{
		if (actor?.data == null || !TryGetCachedGeometry(out int centerX, out int centerY, out _)) return false;
		WorldTile target = TryGetNativeTileTarget(actor);
		if (target != null)
		{
			if (IsInsideCached(target.x, target.y, centerX, centerY, OuterMountainRadius)) return true;
			if (IsExternalLandUnit(actor) && !HasValidNativeMaritimeTransit(actor)
				&& IsAdjacentSanctuaryLiquidTarget(target, centerX, centerY)) return true;
		}
		try
		{
			BaseSimObject attackTarget = actor.attack_target;
			WorldTile attackTile = attackTarget?.current_tile;
			if (attackTile != null
				&& IsInsideCached(attackTile.x, attackTile.y, centerX, centerY, OuterMountainRadius)) return true;
		}
		catch (System.Exception xjCaught099_4) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("0.9.9/Systems/Shi/XjZhantanlinSystem.cs#4", xjCaught099_4); }
		return RouteTouchesProtectedBoundary(actor);
	}

	private static bool IsExternalLandUnit(Actor actor)
	{
		if (actor?.data == null || actor.asset?.is_boat == true || XjCultivationPathRules.IsShi(actor)) return false;
		// 旧实现只看 actor.city。角色被坏路线拖出城市区域、尤其已经落到海格后，
		// 这个运行时引用可能暂时为空，结果恰好在最需要救援时失去“陆地文明单位”
		// 身份。改以 ActorData.cityID 作为稳定归属，live city 只做兼容补充。
		try
		{
			if (actor.data.cityID >= 0L) return true;
		}
		catch { }
		try { return actor.city != null; }
		catch { return false; }
	}

	private static bool HasValidNativeMaritimeTransit(Actor actor)
	{
		if (actor?.data == null) return false;
		if (actor.asset?.is_boat == true) return true;
		if (XjHighRealmMovement.IsManagedFlightActive(actor)) return true;
		// WorldBox 自己用 transportID 表示角色已经进入/绑定运输实体。玄鉴只认这个
		// 原生授权，不创建船、不维护船池，也不修改 FunBoost 对 pParam.boat 的 AStar。
		try { return actor.data.transportID > 0L; }
		catch { return false; }
	}

	private static bool HasActiveSanctuaryRouteSuppression(Actor actor)
	{
		long actorId = GetActorId(actor);
		if (actorId <= 0L || !SanctuaryRouteSuppressions.TryGetValue(actorId, out SanctuaryRouteSuppression state)) return false;
		if (Time.frameCount < state.UntilFrame) return true;
		SanctuaryRouteSuppressions.TryRemove(actorId, out _);
		return false;
	}

	private static bool TryGetSanctuaryRouteOrigin(long actorId, out WorldTile origin)
	{
		origin = null;
		if (actorId <= 0L || !SanctuaryRouteSuppressions.TryGetValue(actorId, out SanctuaryRouteSuppression state)
			|| state.OriginX == int.MinValue || Time.frameCount >= state.UntilFrame) return false;
		try { origin = World.world?.GetTileSimple(state.OriginX, state.OriginY); }
		catch { origin = null; }
		return IsSafeExternalTile(origin);
	}

	private static bool IsLiquidTile(WorldTile tile)
	{
		if (tile == null) return false;
		try { return tile.Type != null && (tile.Type.ocean || tile.Type.liquid || tile.Type.lava); }
		catch { return false; }
	}

	private static bool IsAdjacentSanctuaryLiquidTarget(WorldTile tile, int centerX, int centerY)
	{
		if (tile == null) return false;
		if (!IsLiquidTile(tile)) return false;
		long dx = tile.x - centerX;
		long dy = tile.y - centerY;
		long limit = OuterMountainRadius + ExternalWaterTargetPadding;
		return dx * dx + dy * dy <= limit * limit;
	}

	/// <summary>
	/// 对已经被旧目标拖进旃檀林邻海的非释修文明单位做一次局部恢复。
	/// 这是稀有水格冷路径：陆地单位绝大多数 updateFall 调用只做一次当前地形判断便退出。
	/// </summary>
	internal static bool TryRecoverExternalWaterStranding(Actor actor)
	{
		if (actor?.data == null) return false;
		WorldTile current = null;
		try { current = ((BaseSimObject)actor).current_tile; } catch { current = null; }
		if (current == null || !IsLiquidTile(current)) return false;
		// 只有真的已经落水，才读取旃檀林几何。此前把 TryGetCachedGeometry 放在
		// 液体判定前，会让所有陆地角色的 updateFall 热回调都额外碰一次领域缓存。
		if (!TryGetCachedGeometry(out int centerX, out int centerY, out _)) return false;

		// updateFall 是极热回调：只有已经落在水格后才继续读取文明身份。合法船运
		// 与玄鉴紫府飞行拥有明确授权，不参与“落水救援”。
		if (!IsExternalLandUnit(actor) || HasValidNativeMaritimeTransit(actor)) return false;
		long actorId = GetActorId(actor);
		bool suppressedRoute = HasActiveSanctuaryRouteSuppression(actor);
		long dx = current.x - centerX;
		long dy = current.y - centerY;
		long recoveryLimit = OuterMountainRadius + ExternalWaterRecoveryPadding;
		if (!suppressedRoute && dx * dx + dy * dy > recoveryLimit * recoveryLimit) return false;

		WorldTile failedTarget = TryGetNativeTileTarget(actor);
		// 若此前尚未登记，在清路线前补登记；若已经登记则会保留首次安全陆地原点。
		RegisterSanctuaryRouteSuppression(actor, failedTarget);
		CancelSanctuaryRoute(actor, resetBehaviour: true, clearNativeTileTarget: true);
		XjActorAggroBridge.ClearTargets(actor, stopMovement: false, clearRetaliation: false);
		if (!TryGetSanctuaryRouteOrigin(actorId, out WorldTile safe)
			&& !TryFindSafeOwnedCityLand(actor, current, out safe)) return false;
		bool recovered = XjHighRealmMovement.TryImmediateDomainRecovery(actor, safe);
		if (recovered)
		{
			Interlocked.Increment(ref SanctuaryWaterRecoveries);
			try { actor.makeWait(0.35f); } catch { }
		}
		return recovered;
	}

	internal static string ReadPathIsolationDiagnostic()
	{
		return "sanctuaryPath={waterRecovered:" + Volatile.Read(ref SanctuaryWaterRecoveries)
			+ ",suppressed:" + SanctuaryRouteSuppressions.Count
			+ "}";
	}

	private static bool TryFindSafeOwnedCityLand(Actor actor, WorldTile origin, out WorldTile safe)
	{
		safe = null;
		City city = null;
		try { city = actor?.city; } catch { city = null; }
		if (city == null && actor?.data != null)
		{
			try
			{
				long cityId = actor.data.cityID;
				if (cityId >= 0L) XjWorldLookupIndex.TryResolveCity(cityId, out city);
			}
			catch { city = null; }
		}
		if (city?.zones == null || city.zones.Count == 0) return false;
		long bestDistance = long.MaxValue;
		int checks = Math.Min(16, city.zones.Count);
		for (int i = 0; i < checks; i++)
		{
			WorldTile candidate = city.zones[i]?.centerTile;
			if (!IsSafeExternalTile(candidate)) continue;
			long dx = origin == null ? 0L : candidate.x - origin.x;
			long dy = origin == null ? 0L : candidate.y - origin.y;
			long distance = dx * dx + dy * dy;
			if (distance >= bestDistance) continue;
			bestDistance = distance;
			safe = candidate;
		}
		return safe != null;
	}

	/// <summary>
	/// 世界年级摩诃归林对账。只遍历 XjCultivatorCache 的释修稳定快照，
	/// 不触碰 World.world.units。正常未到期摩诃 O(1) 跳过。
	/// </summary>
	internal static void SchedulePeriodicMoHeReturns(int currentYear)
	{
		int year = Math.Max(1, currentYear);
		if (!IsPlaced) return;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (actorId <= 0L || QueuedPeriodicMoHeReturnIds.Contains(actorId)
				|| !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive()) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				|| !string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) continue;

			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear, out int nextReturnYear);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, out int returnUntilYear);
			bool inside = IsInside(actor);
			bool needsReconcile = nextReturnYear <= 0
				|| year >= nextReturnYear
				|| (inside && returnUntilYear <= 0)
				|| (returnUntilYear > 0 && year > returnUntilYear)
				|| (returnUntilYear >= year && !inside);
			if (!needsReconcile || !QueuedPeriodicMoHeReturnIds.Add(actorId)) continue;
			PeriodicMoHeReturnQueue.Enqueue(actorId);
		}
	}

	internal static bool HasQueuedPeriodicMoHeReturns => PeriodicMoHeReturnQueue.Count > 0;

	internal static bool TickQueuedPeriodicMoHeReturn(int currentYear)
	{
		if (PeriodicMoHeReturnQueue.Count == 0) return false;
		long actorId = PeriodicMoHeReturnQueue.Dequeue();
		QueuedPeriodicMoHeReturnIds.Remove(actorId);
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive()) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| !string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return false;
		EnforceActor(actor, Math.Max(1, currentYear));
		return true;
	}

	private static void HandlePeriodicMoHeReturn(Actor actor, int year)
	{
		if (actor?.data == null || !IsPlaced) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear,
			out int nextReturnYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear,
			out int returnUntilYear);

		// RC9及更早旧档可能已经把摩诃留在林内，却没有任何有效驻留期。
		// 这种状态不再解释成永久常住，年度对账直接尝试送回外界。
		if (returnUntilYear <= 0 && IsInside(actor))
		{
			if (!TryReturnMoHeOutside(actor)) return;
			ClearMoHeReturnOrigin(actor);
			ReleaseTemporaryPeaceful(actor);
		}

		// 归林驻留期结束后主动送回外界，而不是只“解除禁出”后等待 AI 自己产生
		// 一条跨领域路线。优先回到入林前的真实落点，失败时才用佛土外固定范围陆地兜底。
		if (returnUntilYear > 0 && year > returnUntilYear)
		{
			if (IsInside(actor) && !TryReturnMoHeOutside(actor)) return;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, 0);
			ClearMoHeReturnOrigin(actor);
			ReleaseTemporaryPeaceful(actor);
		}

		if (nextReturnYear <= 0)
		{
			ScheduleNextMoHeReturn(actor, year);
			return;
		}
		if (year >= nextReturnYear)
		{
			if (!IsInside(actor))
			{
				RememberMoHeReturnOrigin(actor, overwrite: true);
				if (!TryTeleportInside(actor, GetActorId(actor))) return;
			}
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear,
				year + Math.Max(0, MoHeReturnStayYears - 1));
			ScheduleNextMoHeReturn(actor, year);
			return;
		}
		if (returnUntilYear >= year && !IsInside(actor))
		{
			RememberMoHeReturnOrigin(actor, overwrite: false);
			TryTeleportInside(actor, GetActorId(actor));
		}
	}

	private static void RememberMoHeReturnOrigin(Actor actor, bool overwrite)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginValid, out int valid);
		if (!overwrite && valid > 0) return;
		WorldTile tile = null;
		try { tile = ((BaseSimObject)actor).current_tile; } catch { tile = null; }
		if (!IsSafeExternalTile(tile)) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginX, tile.x);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginY, tile.y);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginValid, 1);
	}

	private static bool TryReturnMoHeOutside(Actor actor)
	{
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginValid, out int valid);
		if (valid > 0
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginX, out int x)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginY, out int y))
		{
			WorldTile origin = null;
			try { origin = World.world?.GetTileSimple(x, y); } catch { origin = null; }
			if (IsSafeExternalTile(origin) && TryTeleportOutside(actor, origin)) return true;
		}

		// 旧档没有保存外界落点时，优先尝试角色原所属城市的有限 zone 中心。
		// 这比在海上扩大半径扫描更稳定，也不会触发任何全世界城市/人口遍历。
		try
		{
			City city = actor.city;
			if (city?.zones != null)
			{
				int zoneChecks = Math.Min(8, city.zones.Count);
				for (int i = 0; i < zoneChecks; i++)
				{
					WorldTile cityTile = city.zones[i]?.centerTile;
					if (IsSafeExternalTile(cityTile) && TryTeleportOutside(actor, cityTile)) return true;
				}
			}
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.TryReturnMoHeOutside.CityFallback", ex);
		}

		return TryGetSafeOutsideTile(GetActorId(actor), out WorldTile fallback)
			&& TryTeleportOutside(actor, fallback);
	}

	private static void ClearMoHeReturnOrigin(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnOriginValid, 0);
	}

	private static bool TryGetSafeOutsideTile(long salt, out WorldTile tile)
	{
		tile = null;
		if (World.world == null
			|| !XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null) return false;
		int startDirection = XjDeterministicHash.PositiveIndex(salt, "zhantanlin_exit_direction", 8);
		for (int radius = OuterMountainRadius + 4; radius <= OuterMountainRadius + 64; radius += 4)
		{
			for (int offset = 0; offset < 8; offset++)
			{
				int direction = (startDirection + offset) % 8;
				int dx = direction == 0 || direction == 1 || direction == 7 ? radius
					: direction >= 3 && direction <= 5 ? -radius : 0;
				int dy = direction >= 1 && direction <= 3 ? radius
					: direction >= 5 && direction <= 7 ? -radius : 0;
				try { tile = World.world.GetTileSimple(domain.MapCenterX + dx, domain.MapCenterY + dy); }
				catch { tile = null; }
				if (IsSafeExternalTile(tile)) return true;
			}
		}
		tile = null;
		return false;
	}

	private static bool IsSafeExternalTile(WorldTile tile)
	{
		return tile != null
			&& !IsInsideProtectedBoundary(tile)
			&& XjHighRealmMovement.IsSafeTeleportDestination(tile);
	}

	private static void ScheduleNextMoHeReturn(Actor actor, int year)
	{
		long actorId = GetActorId(actor);
		int interval = MoHeReturnMinIntervalYears + XjDeterministicHash.PositiveIndex(
			actorId + Math.Max(1, year), "shi_zhantanlin_periodic_return", MoHeReturnIntervalSpanYears);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear,
			Math.Max(1, year) + interval);
	}

	private static int ResolveCurrentYear(Actor actor)
	{
		int tracked = XjYearTracker.CurrentYear;
		if (tracked > 0) return tracked;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastAnnualYear, out int actorYear);
		return Math.Max(1, actorYear > 0 ? actorYear : World.world?.map_stats?.year ?? 1);
	}

	internal static void SynchronizeSanctuaryPeace(Actor actor)
	{
		if (actor?.data == null) return;
		if (IsInside(actor)) ApplySanctuaryPeace(actor);
		else ReleaseTemporaryPeaceful(actor);
	}

	private static void ApplySanctuaryPeace(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			if (!actor.hasTrait(PeacefulTraitId))
			{
				actor.addTrait(PeacefulTraitId, false);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinTempPeaceful, 1);
				// 只在首次进入清净区时中断既有战斗。不要调用 stopMovement：
				// 第三方批处理（尤其 Optime）可能把这次停止保持到后续AI周期。
				actor.cancelAllBeh();
			}
			actor.attackedBy = null;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ApplySanctuaryPeace", ex);
		}
	}

	/// <summary>
	/// 接管其他临时系统留下的和睦。只有角色实际位于旃檀林时才登记，
	/// 角色原生自带的和睦不会经过此入口。
	/// </summary>
	internal static void AdoptTemporaryPeaceful(Actor actor)
	{
		if (actor?.data == null || !IsInside(actor) || !actor.hasTrait(PeacefulTraitId)) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinTempPeaceful, 1);
		try { actor.attackedBy = null; }
		catch (Exception ex) { XjExceptionDiagnostics.Report("XjZhantanlinSystem.AdoptPeaceful.ClearAttacker", ex); }
	}

	private static void ReleaseTemporaryPeaceful(Actor actor)
	{
		if (actor?.data == null) return;
		try
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiZhantanlinTempPeaceful,
				out int temporary);
			if (temporary > 0 && actor.hasTrait(PeacefulTraitId))
			{
				// 若人物正处于闭关，将临时和睦的清理责任交给闭关系统；
				// 否则由旃檀林在离境时移除。
				if (!XjClosedCultivationGuard.TryAdoptTemporaryPeaceful(actor))
					actor.removeTrait(PeacefulTraitId);
			}
			if (temporary > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinTempPeaceful, 0);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ReleaseTemporaryPeaceful", ex);
		}
	}

	/// <summary>旃檀林是绝对止战区；只要攻击任一端位于林内，本次伤害即被截断。</summary>
	internal static bool ShouldBlockCombat(Actor attacker, Actor defender)
	{
		bool attackerInside = IsInside(attacker);
		bool defenderInside = IsInside(defender);
		if (!attackerInside && !defenderInside) return false;
		if (attackerInside) ApplySanctuaryPeace(attacker);
		if (defenderInside) ApplySanctuaryPeace(defender);
		try
		{
			if (attacker != null) attacker.attackedBy = null;
			if (defender != null) defender.attackedBy = null;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjZhantanlinSystem.ShouldBlockCombat.ClearAttackers", ex);
		}
		return true;
	}

	internal static bool TryResolveRebirthTile(string anchorId, string fallbackDomainId, string patronActorId,
		long sourceActorId, out WorldTile tile)
	{
		tile = null;
		bool anchorInsideZhantanlin = XjShiDomainState.TryGet(anchorId, out XjShiDomainRecord anchorDomain)
			&& anchorDomain != null
			&& string.Equals(anchorDomain.AbsorbedByDomainId,
				XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal);
		bool requiresZhantanlin = anchorInsideZhantanlin
			|| string.Equals(anchorId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(anchorId)
				&& string.Equals(fallbackDomainId, XjShiDomainCatalog.ZhantanlinDomainId,
					StringComparison.Ordinal);
		if (requiresZhantanlin && !IsPlaced) return false;
		if (TryGetAnchorTile(anchorId, sourceActorId, out tile)) return true;
		if (TryGetAnchorTile(fallbackDomainId, sourceActorId, out tile)) return true;
		if (XjShiWorldRegistry.TryResolveLiveActor(patronActorId, out Actor patron))
		{
			try { tile = ((BaseSimObject)patron).current_tile; } catch { tile = null; }
			if (tile != null) return true;
		}
		return TryGetSafeTile(sourceActorId, out tile);
	}

	internal static bool TryGetAnchorTile(string anchorId, long salt, out WorldTile tile)
	{
		tile = null;
		if (string.Equals(anchorId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
			return TryGetSafeTile(salt, out tile);
		if (XjShiDomainState.TryGet(anchorId, out XjShiDomainRecord domain) && domain != null)
		{
			if (string.Equals(domain.AbsorbedByDomainId,
				XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
				return TryGetSafeTile(salt + Math.Max(0, domain.SourceHeavenIndex) * 97L
					+ Math.Max(0, domain.SourceHeavenFragmentOrdinal), out tile);
			long holderId = domain.OwnerActorId;
			if (holderId > 0L && XjActorRegistry.ResolveKnownOrWorld(holderId, out Actor holder)
				&& holder?.data != null && holder.isAlive())
			{
				try { tile = ((BaseSimObject)holder).current_tile; } catch { tile = null; }
				if (tile != null) return true;
			}
		}
		return false;
	}

	private static bool TryTeleportInside(Actor actor, long actorIdSalt)
	{
		if (actor?.data == null) return false;
		if (IsInside(actor))
		{
			ApplySanctuaryPeace(actor);
			return true;
		}
		if (!TryGetSafeTile(actorIdSalt, out WorldTile tile)) return false;
		if (!XjHighRealmMovement.TryImmediateDomainTransfer(actor, tile)) return false;
		if (IsInside(actor)) ApplySanctuaryPeace(actor);
		return IsInside(actor);
	}

	private static bool TryGetSafeTile(long salt, out WorldTile tile)
	{
		tile = null;
		if (World.world == null || !XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null || domain.MapRadius < MinimumRadius) return false;
		int spread = Math.Max(1, Math.Min(12, domain.MapRadius / 4));
		for (int i = 0; i < 24; i++)
		{
			int index = XjDeterministicHash.PositiveIndex(salt + i, "zhantanlin_safe_tile", (spread * 2 + 1) * (spread * 2 + 1));
			int dx = index % (spread * 2 + 1) - spread;
			int dy = index / (spread * 2 + 1) - spread;
			try { tile = World.world.GetTileSimple(domain.MapCenterX + dx, domain.MapCenterY + dy); }
			catch { tile = null; }
			if (tile != null && IsInside(tile) && XjHighRealmMovement.IsSafeTeleportDestination(tile)) return true;
		}
		try { tile = World.world.GetTileSimple(domain.MapCenterX, domain.MapCenterY); }
		catch { tile = null; }
		return tile != null && IsInside(tile) && XjHighRealmMovement.IsSafeTeleportDestination(tile);
	}

	private static void ProcessPendingEntries(int year)
	{
		// 开辟完成后先做一次局部净界：原区域内的异类、动物与矿物不能成为
		// 三重封界内的遗留物。随后再接引全世界今释。
		AuditSanctuary(Math.Max(1, year), announceConversions: true, cleanMinerals: true);
		_lastSanctuaryDefenseYear = Math.Max(_lastSanctuaryDefenseYear, year);

		var ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor))
				TryAdmitModernShi(actor, year);
		}

		// 释修索引是正式运行权威；第三方旁路赋特质由角色注册/年度自愈重新入索引。
		// 放置动作绝不为了兼容旁路而同步扫描全世界人口。
	}

	private static void TryAdmitModernShi(Actor actor, int year)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return;
		OnBecameModern(actor, year);
		if (!IsInside(actor)) TryTeleportInside(actor, GetActorId(actor));
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
