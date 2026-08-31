using System.Collections.Generic;
using XuanJianVNext.Data.Codex;

namespace XuanJianVNext.Systems.Codex;

/// <summary>
/// 世界实体只读索引。它只消费已发布的仙鉴不可变快照，
/// 不扫描 World、角色、家族或宗门；UI 按稳定 id 直接取得快照条目。
///
/// 0.9.9.9 R3：移除无人消费的二次 Family/Sect/City ViewModel 与签名层。
/// Codex 发布是展示行为，不再反向推动家族/宗门 Revision 失效；
/// 关系 Revision 只由真实领域写入口维护。
/// </summary>
internal static class XjWorldEntityViewModelStore
{
    private static readonly Dictionary<long, XjCodexFamilyItem> FamilyItems = new Dictionary<long, XjCodexFamilyItem>();
    private static readonly Dictionary<long, XjCodexSectItem> SectItems = new Dictionary<long, XjCodexSectItem>();
    private static readonly Dictionary<long, XjCodexCityItem> CityItems = new Dictionary<long, XjCodexCityItem>();

    internal static void Publish(XjCodexSnapshot snapshot)
    {
        ClearItems();
        if (snapshot == null) return;

        if (snapshot.Families != null)
        {
            for (int i = 0; i < snapshot.Families.Count; i++)
            {
                XjCodexFamilyItem item = snapshot.Families[i];
                if (item == null || item.FamilyId <= 0L) continue;
                FamilyItems[item.FamilyId] = item;
            }
        }

        if (snapshot.Sects != null)
        {
            for (int i = 0; i < snapshot.Sects.Count; i++)
            {
                XjCodexSectItem item = snapshot.Sects[i];
                if (item == null || item.SectId <= 0L) continue;
                SectItems[item.SectId] = item;
            }
        }

        if (snapshot.Cities != null)
        {
            for (int i = 0; i < snapshot.Cities.Count; i++)
            {
                XjCodexCityItem item = snapshot.Cities[i];
                if (item == null || item.CityId <= 0L) continue;
                CityItems[item.CityId] = item;
            }
        }
    }

    internal static bool TryGetFamilyItem(long familyId, out XjCodexFamilyItem item)
    {
        item = null;
        return familyId > 0L && FamilyItems.TryGetValue(familyId, out item);
    }

    internal static bool TryGetSectItem(long sectId, out XjCodexSectItem item)
    {
        item = null;
        return sectId > 0L && SectItems.TryGetValue(sectId, out item);
    }

    internal static bool TryGetCityItem(long cityId, out XjCodexCityItem item)
    {
        item = null;
        return cityId > 0L && CityItems.TryGetValue(cityId, out item);
    }

    internal static void Clear()
    {
        ClearItems();
    }

    private static void ClearItems()
    {
        FamilyItems.Clear();
        SectItems.Clear();
        CityItems.Clear();
    }
}
