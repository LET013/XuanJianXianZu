using System;
using XuanJianVNext.Architecture.Presentation;
using XuanJianVNext.Core;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Map;
using XuanJianVNext.UI.Shi;

namespace XuanJianVNext.UI;

/// <summary>Binds UI implementations to the presentation port at the composition edge.</summary>
internal static class XjUiPresentationBridge
{
	private static bool _registered;

	internal static void Register()
	{
		if (_registered)
		{
			return;
		}

		XjPresentationHooks.Configure(
			EnsureWorldPresentation,
			ResetWorldPresentation,
			XjSectMapLayerSystem.MarkDirty,
			XjActorInfoPanelRenderer.Invalidate,
			RemoveActorInfoProjection,
			ClearActorInfoProjectionCache,
			XjNativeHoverTooltip.RegisterPassthrough,
			XjFpsOverlay.SetEnabled,
			TryShowWorldTip);
		_registered = true;
	}

	private static void EnsureWorldPresentation()
	{
		XjSectMapLayerSystem.EnsureForCurrentWorld();
		XjZhantanlinPlacementUi.RefreshPlacementButtonState();
	}

	private static void ResetWorldPresentation()
	{
		XjSectMapLayerSystem.ResetForWorldUnload();
		XjZhantanlinPlacementUi.RefreshPlacementButtonState(forceEnabled: true);
	}

	private static void RemoveActorInfoProjection(long actorId)
	{
		XjActorSupplementReadModelStore.RemoveActor(actorId);
		XjActorInfoReadModel.RemoveCachedActor(actorId);
	}

	private static void ClearActorInfoProjectionCache()
	{
		XjActorSupplementReadModelStore.Clear();
		XjActorInfoReadModel.ClearReadModelCache();
	}
	private static bool TryShowWorldTip(string text, bool pause, string position, float duration, string color)
	{
		try
		{
			// WorldTip 会先把输入当本地化 key 查询。玄鉴公告正文是动态文本，
			// 先注册 passthrough，避免正文被记成 missing text。
			XjNativeHoverTooltip.RegisterPassthrough(text);
			WorldTip.showNow(text, pause, position, duration, color);
			return true;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjUiPresentationBridge.ShowWorldTip", ex);
			return false;
		}
	}

}
