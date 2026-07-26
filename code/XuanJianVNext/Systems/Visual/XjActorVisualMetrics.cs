using System;
using System.Reflection;
using UnityEngine;

namespace XuanJianVNext.Systems.Visual;

/// <summary>
/// Resolves a visible actor's approximate rendered size without depending on a
/// specific WorldBox Actor renderer field name. Spell visuals use this only as
/// a scale reference and fall back to a conservative humanoid size.
/// </summary>
internal static class XjActorVisualMetrics
{
	private const float DefaultWorldSize = 1.35f;
	private static readonly string[] RendererMemberNames =
	{
		"sprite_renderer", "spriteRenderer", "current_sprite_renderer", "renderer"
	};

	internal static float ResolveWorldSize(Actor actor)
	{
		if (actor == null)
		{
			return DefaultWorldSize;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		try
		{
			Type type = actor.GetType();
			for (int i = 0; i < RendererMemberNames.Length; i++)
			{
				string name = RendererMemberNames[i];
				object value = type.GetProperty(name, flags)?.GetValue(actor, null)
					?? type.GetField(name, flags)?.GetValue(actor);
				if (value is not SpriteRenderer renderer || renderer.sprite == null)
				{
					continue;
				}

				Vector3 bounds = renderer.bounds.size;
				float size = Mathf.Max(bounds.x, bounds.y);
				if (size > 0.05f)
				{
					return Mathf.Clamp(size, 0.75f, 2.5f);
				}
			}
		}
		catch
		{
		}

		return DefaultWorldSize;
	}
}
