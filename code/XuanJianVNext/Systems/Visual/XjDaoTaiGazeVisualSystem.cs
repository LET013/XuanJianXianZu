using System;
using System.Collections;
using UnityEngine;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.Visual;

internal static class XjDaoTaiGazeVisualSystem
{
	private const string SpritePath = "effects/YinSi/DaoTaiChuiMou";
	private const float NorthLift = 12f;
	private const float FloatDistance = 4f;
	private const float DurationSeconds = 8f;
	private const int RelativeSortingOrder = 240;

	private static Sprite _sprite;
	private static int _generation;
	private static float _lastShownRealtime = -9999f;

	internal static void Clear()
	{
		_generation++;
		_lastShownRealtime = -9999f;
	}

	internal static bool Show(long actorId, int currentYear)
	{
		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == (UnityEngine.Object)null || MapBox.width <= 0 || MapBox.height <= 0) return false;
		if (Time.unscaledTime - _lastShownRealtime < 2f) return false;

		Sprite sprite = LoadSprite();
		if ((UnityEngine.Object)(object)sprite == (UnityEngine.Object)null) return false;

		int x = Math.Max(0, Math.Min(MapBox.width - 1, XjDeterministicHash.PositiveIndex(actorId + currentYear, "xj.daotai.gaze.x", MapBox.width)));
		Vector3 focusPosition = new Vector3(x, MapBox.height - 1, 0f);
		Vector3 displayPosition = new Vector3(x, MapBox.height - 1 + NorthLift, 0f);
		try { world.locatePosition(displayPosition); }
		catch
		{
			try { world.locatePosition(focusPosition); } catch { }
		}

		try
		{
			_lastShownRealtime = Time.unscaledTime;
			((MonoBehaviour)world).StartCoroutine(Run(sprite, displayPosition, ++_generation));
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static IEnumerator Run(Sprite sprite, Vector3 startPosition, int generation)
	{
		GameObject root = new GameObject("xj_daotai_chuimou");
		root.hideFlags = HideFlags.DontSave;
		SpriteRenderer renderer = XjWorldSpriteRenderLayer.AddRenderer(root, RelativeSortingOrder);
		if (renderer == null)
		{
			UnityEngine.Object.Destroy(root);
			yield break;
		}

		renderer.sprite = sprite;
		renderer.color = new Color(1f, 1f, 1f, 0f);
		root.transform.position = startPosition;
		root.transform.localScale = ResolveScale(sprite);

		float elapsed = 0f;
		while (elapsed < DurationSeconds && generation == _generation)
		{
			elapsed += Time.deltaTime;
			float t = Mathf.Clamp01(elapsed / DurationSeconds);
			root.transform.position = startPosition + new Vector3(0f, FloatDistance * t, 0f);
			float alpha = t < 0.16f ? t / 0.16f : t > 0.82f ? (1f - t) / 0.18f : 1f;
			renderer.color = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha));
			yield return null;
		}

		UnityEngine.Object.Destroy(root);
	}

	private static Vector3 ResolveScale(Sprite sprite)
	{
		float spriteWidth = 1f;
		try { spriteWidth = Math.Max(1f, sprite.bounds.size.x); } catch { }
		float desiredWorldWidth = Mathf.Clamp(MapBox.width * 0.38f, 36f, 96f);
		float scale = desiredWorldWidth / spriteWidth;
		return new Vector3(scale, scale, 1f);
	}

	private static Sprite LoadSprite()
	{
		if ((UnityEngine.Object)(object)_sprite != (UnityEngine.Object)null) return _sprite;
		try
		{
			_sprite = SpriteTextureLoader.getSprite(SpritePath)
				?? SpriteTextureLoader.getSprite("GameResources/" + SpritePath)
				?? SpriteTextureLoader.getSprite(SpritePath + ".png")
				?? SpriteTextureLoader.getSprite("GameResources/" + SpritePath + ".png");
		}
		catch
		{
			_sprite = null;
		}
		return _sprite;
	}
}
