using System;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.DongTian;

/// <summary>
/// 落霞山借洞天实体系统显化为永久“高位山门地标”，但逻辑上不是普通奇遇洞天。
/// 原著中的落霞山是北方上宗与霞光道统核心，本实现只借现有实体/存档基础设施显示势力，
/// 不给它创建城市、原生宗门、发现者、探索名额、属地争夺或宗门远征，从而保持低维护成本。
/// </summary>
internal static partial class XjDongTianRegistry
{
    internal static bool HasLuoXiaShanDongTian()
    {
        return TryReadQiYuDongTianRecord(XjDongTianRules.LuoXiaShanDongTianId, out _);
    }

    /// <summary>
    /// 落霞山不强绑城市；只在它的实际建筑格已经落入某城辖区时，才向舆图提供该城。
    /// 因此无城显世后，城市日后扩张覆盖此格也会自然显示，无需迁移旧档。
    /// </summary>
    internal static bool TryResolveLuoXiaShanHostCityId(out long cityId)
    {
        cityId = 0L;
        if (!TryReadQiYuDongTianRecord(XjDongTianRules.LuoXiaShanDongTianId, out XjDongTianRecord record))
            return false;

        WorldTile tile = null;
        try
        {
            if (!XjDongTianEntitySystem.TryResolveCurrentTile(record, out tile))
            {
                tile = World.world?.GetTileSimple(record.AnchorTileX, record.AnchorTileY);
            }
            City city = tile?.zone?.city;
            if (city?.data == null || city.data.id <= 0L) return false;
            cityId = city.data.id;
            return true;
        }
        catch
        {
            cityId = 0L;
            return false;
        }
    }

    internal static bool EnsureLuoXiaShanDongTian(int currentYear, bool announce)
    {
        if (currentYear <= 0) return false;
        if (TryReadQiYuDongTianRecord(XjDongTianRules.LuoXiaShanDongTianId, out XjDongTianRecord existing))
        {
            bool normalizedAlready = existing.OpenUntilYear == 0
                && existing.MaxExploreActors == 0
                && existing.RemainingExploreCount == 0
                && existing.IsOpen
                && !existing.EntityClosed
                && existing.EntitySpawned
                && string.Equals(existing.DaoTuGroup, "虹霞", StringComparison.Ordinal)
                && string.Equals(existing.DisplayName, "落霞山", StringComparison.Ordinal)
                && (existing.ExplorerRecords == null || existing.ExplorerRecords.Count == 0);
            if (normalizedAlready)
            {
                // 读档后实体可能被原生清理而存档仍标记为已生成；主动走一次实体巡检以补回地标。
                XjDongTianEntitySystem.Tick(new[] { existing }, 1);
                return true;
            }

            // 防旧档/调试链把普通洞天字段写进来：落霞山始终归零探索与属地状态。
            XjAdventureRealmClaimSystem.RemoveForTimelineMigration(existing.RecordId);
            XjDongTianRecord normalized = new XjDongTianRecord(
                true, existing.RecordId, XjDongTianRules.LuoXiaShanDongTianId, "虹霞", "虹霞,戊土", "落霞山",
                existing.CreatedYear, 0, 0, existing.ExploredActorCount, 0, true,
                "非公共奇遇，不设探索死亡率",
                "非公共奇遇，不设探索奖励",
                BuildLuoXiaShanSummary(),
                0L, string.Empty, existing.AnchorTileX, existing.AnchorTileY, existing.AnchorCityId, existing.AnchorCityName,
                existing.AnchorYear, "HongXiaLuoXia", existing.LastExploreReserveYear, Array.Empty<XjQiYuDongTianExplorerRecord>(),
                existing.EntityAssetId, existing.EntityBuildingId, existing.EntitySpawned, false);
            recordsById[existing.RecordId] = normalized;
            MarkRecordCacheDirty();
            XjDongTianEntitySystem.Tick(new[] { normalized }, 1);
            if (announce) BroadcastLuoXiaShanManifest(normalized.AnchorCityName);
            return true;
        }

        if (!XjDongTianRules.TryResolveCatalogEntry(XjDongTianRules.LuoXiaShanDongTianId, out XjDongTianCatalogEntry entry))
            return false;
        if (!TryFindActivityAnchor(currentYear, out XjQiYuDongTianActivityAnchor anchor))
            return false;

        string recordId = "qiyu_dongtian_" + XjDongTianRules.LuoXiaShanDongTianId;
        XjDongTianRecord record = new XjDongTianRecord(
            true, recordId, entry.QiYuDongTianId, entry.DaoTuGroup, entry.RelatedDaoTuIds, entry.DisplayName,
            currentYear,
            0, // 永久山门，不走五年关闭。
            0, // 无公共探索名额。
            0,
            0,
            true,
            "非公共奇遇，不设探索死亡率",
            "非公共奇遇，不设探索奖励",
            BuildLuoXiaShanSummary(),
            0L, // 不把地图锚点人物记作山门发现者或代理人。
            string.Empty,
            anchor.TileX, anchor.TileY, anchor.CityId, anchor.CityName, currentYear, "HongXiaLuoXia", 0,
            Array.Empty<XjQiYuDongTianExplorerRecord>());

        recordsById[recordId] = record;
        MarkRecordCacheDirty();
        // 刻意不调用 OnRealmCreated：落霞山没有国家/宗门属地、争夺窗口与远征权。
        XjDongTianEntitySystem.Tick(new[] { record }, 1);
        XjWorldArchiveSystem.MarkChanged();
        if (announce) BroadcastLuoXiaShanManifest(anchor.CityName);
        return true;
    }

    private static string BuildLuoXiaShanSummary()
    {
        return "落霞山（虹霞）·群霞所宗，天下灵机至盛。日月交替之际，第一缕霞光自山中而出；山门深闭，只择有缘之人入门，承虹霞与戊土霞光旧法。";
    }

    private static void BroadcastLuoXiaShanManifest(string cityName)
    {
        string location = string.IsNullOrWhiteSpace(cityName) ? "未探索之地" : cityName.Trim();
        XjBroadcastSystem.BroadcastBLevelWorldEvent(
            "【落霞显山】落霞山于" + location + "附近显出山门霞影。群霞自天际归山，尘世行旅却无门可寻；唯山中自择有缘之人，传承虹霞与戊土霞光旧法。",
            XjEventIconCatalog.DongTianOpen,
            XjAnnouncementCategory.DongTian);
    }
}
