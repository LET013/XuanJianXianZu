namespace XuanJianVNext.UI.Mentorship;

/// <summary>
/// Compatibility shell for the retired native UnitWindow mentorship tab.
/// Mentorship remains available through a fixed shortcut + XuanJian-owned window;
/// no cloned WindowMetaTab, DragOrderElement, Canvas or tabs._tabs mutation is allowed.
/// </summary>
internal static class XjMentorshipTabPatches
{
	internal static bool TryAddOrUpdate(UnitWindow window)
	{
		return false;
	}
}
