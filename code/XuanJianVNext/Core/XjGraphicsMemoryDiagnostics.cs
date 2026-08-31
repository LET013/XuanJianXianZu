using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace XuanJianVNext.Core;

/// <summary>
/// Opt-in, low-frequency graphics inventory. Unity's ALLOC_GFX_MAIN value is a
/// native high-water mark and does not identify an owning mod or asset. This
/// probe therefore never runs in a simulation lane: while explicit performance
/// observation is enabled it inventories loaded graphics objects at most once per minute.
/// </summary>
internal static class XjGraphicsMemoryDiagnostics
{
	private const float SampleIntervalSeconds = 60f;
	private const int TopTextureCount = 6;

	private static float _nextSampleAt = -1f;
	private static long _peakKnownTextureBytes;
	private static long _peakKnownRenderTextureBytes;
	private static string _lastSummary = "图形探针尚未采样。";

	internal static string ReadLatestSummary() => _lastSummary;

	internal static void Clear()
	{
		_nextSampleAt = -1f;
		_peakKnownTextureBytes = 0L;
		_peakKnownRenderTextureBytes = 0L;
		_lastSummary = "图形探针尚未采样。";
	}

	internal static void SampleIfDue()
	{
		float now = Time.unscaledTime;
		if (_nextSampleAt >= 0f && now < _nextSampleAt) return;
		_nextSampleAt = now + SampleIntervalSeconds;

		long started = Stopwatch.GetTimestamp();
		try
		{
			Texture2D[] textures = Resources.FindObjectsOfTypeAll<Texture2D>();
			RenderTexture[] renderTextures = Resources.FindObjectsOfTypeAll<RenderTexture>();
			Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
			Renderer[] renderers = Resources.FindObjectsOfTypeAll<Renderer>();
			BaseEffectController[] effectControllers = Resources.FindObjectsOfTypeAll<BaseEffectController>();

			long textureBytes = 0L;
			long renderTextureBytes = 0L;
			long materialBytes = 0L;
			int xjRendererCount = 0;
			int fxSlashActive = 0;
			int fxSlashPool = 0;
			TextureMetric[] topTextures = new TextureMetric[TopTextureCount];

			for (int i = 0; i < textures.Length; i++)
			{
				Texture2D texture = textures[i];
				if (texture == null) continue;
				long bytes = GetRuntimeBytes(texture);
				textureBytes += bytes;
				InsertTopTexture(topTextures, new TextureMetric(texture.name, texture.width, texture.height, bytes));
			}

			for (int i = 0; i < renderTextures.Length; i++)
			{
				RenderTexture texture = renderTextures[i];
				if (texture != null) renderTextureBytes += GetRuntimeBytes(texture);
			}

			for (int i = 0; i < materials.Length; i++)
			{
				if (materials[i] != null) materialBytes += GetRuntimeBytes(materials[i]);
			}

			for (int i = 0; i < renderers.Length; i++)
			{
				Renderer renderer = renderers[i];
				if (renderer == null || renderer.gameObject == null) continue;
				string objectName = renderer.gameObject.name ?? string.Empty;
				if (objectName.StartsWith("xj_", StringComparison.OrdinalIgnoreCase)
					|| objectName.StartsWith("[玄鉴]", StringComparison.Ordinal)) xjRendererCount++;
			}

			for (int i = 0; i < effectControllers.Length; i++)
			{
				BaseEffectController controller = effectControllers[i];
				if (controller == null || controller.asset == null
					|| !string.Equals(controller.asset.id, "fx_slash", StringComparison.Ordinal)) continue;
				fxSlashActive += Math.Max(0, controller.getActiveIndex());
				fxSlashPool += controller._list?.Count ?? 0;
			}

			_peakKnownTextureBytes = Math.Max(_peakKnownTextureBytes, textureBytes);
			_peakKnownRenderTextureBytes = Math.Max(_peakKnownRenderTextureBytes, renderTextureBytes);
			_lastSummary = BuildSummary(
				textures.Length, textureBytes, renderTextures.Length, renderTextureBytes,
				materials.Length, materialBytes, renderers.Length, xjRendererCount,
				fxSlashActive, fxSlashPool, topTextures, Stopwatch.GetTimestamp() - started);
			UnityEngine.Debug.Log(_lastSummary);
		}
		catch (Exception ex)
		{
			_lastSummary = "[XJ_GFX_PROBE][玄鉴] 采样失败: " + ex.GetType().Name;
			UnityEngine.Debug.LogWarning(_lastSummary);
		}
	}

	private static long GetRuntimeBytes(UnityEngine.Object asset)
	{
		try { return Math.Max(0L, Profiler.GetRuntimeMemorySizeLong(asset)); }
		catch { return 0L; }
	}

	private static void InsertTopTexture(TextureMetric[] entries, TextureMetric candidate)
	{
		for (int i = 0; i < entries.Length; i++)
		{
			if (entries[i].Bytes >= candidate.Bytes) continue;
			for (int move = entries.Length - 1; move > i; move--) entries[move] = entries[move - 1];
			entries[i] = candidate;
			return;
		}
	}

	private static string BuildSummary(
		int textureCount, long textureBytes, int renderTextureCount, long renderTextureBytes,
		int materialCount, long materialBytes, int rendererCount, int xjRendererCount,
		int fxSlashActive, int fxSlashPool, TextureMetric[] topTextures, long elapsedTicks)
	{
		StringBuilder builder = new StringBuilder(640);
		builder.Append("[XJ_GFX_PROBE][玄鉴] tex=")
			.Append(textureCount).Append('/').Append(FormatBytes(textureBytes))
			.Append(" peak=").Append(FormatBytes(_peakKnownTextureBytes))
			.Append(" rt=").Append(renderTextureCount).Append('/').Append(FormatBytes(renderTextureBytes))
			.Append(" peak=").Append(FormatBytes(_peakKnownRenderTextureBytes))
			.Append(" mat=").Append(materialCount).Append('/').Append(FormatBytes(materialBytes))
			.Append(" renderer=").Append(rendererCount).Append(" xj=").Append(xjRendererCount)
			.Append(" fx_slash=").Append(fxSlashActive).Append('/').Append(fxSlashPool)
			.Append(" scan=").Append((elapsedTicks * 1000d / Stopwatch.Frequency).ToString("F1")).Append("ms")
			.Append(" topTex=");

		for (int i = 0; i < topTextures.Length; i++)
		{
			TextureMetric texture = topTextures[i];
			if (texture.Bytes <= 0L) continue;
			if (i > 0) builder.Append(" | ");
			builder.Append(string.IsNullOrWhiteSpace(texture.Name) ? "<unnamed>" : texture.Name)
				.Append('(').Append(texture.Width).Append('x').Append(texture.Height)
				.Append(',').Append(FormatBytes(texture.Bytes)).Append(')');
		}
		return builder.ToString();
	}

	private static string FormatBytes(long bytes)
	{
		if (bytes >= 1024L * 1024L * 1024L) return (bytes / (1024d * 1024d * 1024d)).ToString("F2") + "GB";
		return (bytes / (1024d * 1024d)).ToString("F1") + "MB";
	}

	private readonly struct TextureMetric
	{
		internal TextureMetric(string name, int width, int height, long bytes)
		{
			Name = name ?? string.Empty;
			Width = Math.Max(0, width);
			Height = Math.Max(0, height);
			Bytes = Math.Max(0L, bytes);
		}

		internal string Name { get; }
		internal int Width { get; }
		internal int Height { get; }
		internal long Bytes { get; }
	}
}
