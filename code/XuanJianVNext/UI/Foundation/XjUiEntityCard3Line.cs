namespace XuanJianVNext.UI.Foundation;

/// <summary>
/// 人物榜单统一三行模型：姓名、核心数值、道途与境界。
/// </summary>
internal readonly struct XjUiEntityCard3Line
{
    internal XjUiEntityCard3Line(string name, string primaryValue, string routeAndRealm)
    {
        Name = XjUiSafeText.Player(name, "未名修士");
        PrimaryValue = XjUiSafeText.Player(primaryValue, "—");
        RouteAndRealm = XjUiSafeText.Player(routeAndRealm, "未入道");
    }

    internal string Name { get; }
    internal string PrimaryValue { get; }
    internal string RouteAndRealm { get; }
}
