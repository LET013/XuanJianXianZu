using NeoModLoader.General.UI.Prefabs;
using UnityEngine;
using UnityEngine.Events;

namespace XuanJianVNext.UI.ZongMen;

/// <summary>
/// Retired compatibility prefab. XuanJian no longer creates or registers
/// WindowMetaTab/DragOrderElement objects inside native Unit/City/Kingdom windows.
/// </summary>
public sealed class XjSimpleWindowTab : APrefab<XjSimpleWindowTab>
{
	public WindowMetaTab Tab => null;

	protected override void Init()
	{
		if (Initialized)
		{
			return;
		}
		base.Init();
	}

	public void Setup(string nameKey, ScrollWindow window, Sprite sprite, UnityAction<WindowMetaTab> action)
	{
		// Compatibility no-op. Callers should use a XuanJian-owned window/fixed
		// shortcut rather than mutate the native tab collection.
		name = string.IsNullOrWhiteSpace(nameKey) ? "玄鉴" : nameKey.Trim();
	}

	private static void _init()
	{
		GameObject holder = new GameObject("XjVNextRetiredSimpleTabPrefabHolder");
		Object.DontDestroyOnLoad(holder);
		holder.SetActive(false);

		GameObject marker = new GameObject("RetiredSimpleWindowTab");
		marker.transform.SetParent(holder.transform, false);
		Prefab = marker.AddComponent<XjSimpleWindowTab>();
	}
}
