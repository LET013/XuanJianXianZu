namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	// Native Window Stability:
	// Do not Harmony-patch ScrollWindow.hide/hideAllEvent, WindowHistory.popHistory,
	// DragOrderContainer or DragOrderElement. Ranking right-click behavior is handled by
	// the ranking window's own pointer handlers; native Unit/City/Kingdom transitions
	// must execute WorldBox's close/tween/history transaction without XuanJian interception.
}
