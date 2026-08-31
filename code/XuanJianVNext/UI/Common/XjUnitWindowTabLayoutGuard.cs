namespace XuanJianVNext.UI.Common;

/// <summary>
/// UnitWindow Tab 布局边界。
///
/// 0.9.9.8 之前这里会再次写入所有 WindowMetaTab 的 localScale / sizeDelta /
/// anchoredPosition；但原生 DragOrderContainer / DragOrderElement 也持续拥有同一批
/// RectTransform。两个写者同时控制坐标会造成 Tab、图标与 ScrollWindow 动画反复争夺。
///
/// 现在保留兼容入口但不再修改原生 Tab 几何。自定义 Tab 只负责：首次创建、注册到
/// tabs._tabs、设置 sibling 顺序；最终位置与拖拽完全交还 WorldBox 原生容器。
/// </summary>
internal static class XjUnitWindowTabLayoutGuard
{
	internal static void Apply(ScrollWindow scrollWindow)
	{
		// Intentionally no-op: native tab container is the single layout writer.
	}

	internal static void ApplyNow(ScrollWindow scrollWindow)
	{
		// Kept for binary/source compatibility with older call sites.
	}
}
