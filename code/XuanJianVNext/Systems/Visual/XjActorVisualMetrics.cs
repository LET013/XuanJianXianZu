using System;
using UnityEngine;
using XuanJianVNext.Interop.WorldBox;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// Resolves a visible actor's approximate rendered size without depending on a
/// specific WorldBox Actor renderer field name. Spell visuals use this only as
/// a scale reference and fall back to a conservative humanoid size.
/// </summary>
internal static class XjActorVisualMetrics
{
	private const float DefaultWorldSize = 1.35f;


	internal static float ResolveWorldSize(Actor actor)
	{
		if (actor == null)
		{
			return DefaultWorldSize;
		}

		try
		{
			if (XjNativeActorVisualInterop.TryResolveSpriteRenderer(actor, out SpriteRenderer renderer)
				&& renderer?.sprite != null)
			{
				Vector3 bounds = renderer.bounds.size;
				float size = Mathf.Max(bounds.x, bounds.y);
				if (size > 0.05f) return Mathf.Clamp(size, 0.75f, 2.5f);
			}
		}
		catch (System.Exception xjCaught49) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Visual/XjActorVisualMetrics.cs:49", xjCaught49); }

		return DefaultWorldSize;
	}
}
