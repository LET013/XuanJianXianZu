using System;
using System.Collections.Generic;

namespace XuanJianVNext.Core;

/// <summary>
/// Small main-thread pool for long-id snapshots that must stay stable across
/// several frames. Buffers retain capacity between annual/century passes and
/// are never exposed after Return.
/// </summary>
internal static class XjReusableLongBufferPool
{
	private const int MaxRetainedBuffers = 4;
	private const int MaxRetainedCapacity = 131072;
	private static readonly Stack<List<long>> Pool = new Stack<List<long>>(MaxRetainedBuffers);

	internal static List<long> Rent(int minimumCapacity, out bool reused, out int capacityBefore)
	{
		minimumCapacity = Math.Max(0, minimumCapacity);
		List<long> buffer;
		if (Pool.Count > 0)
		{
			buffer = Pool.Pop();
			reused = true;
		}
		else
		{
			buffer = new List<long>(minimumCapacity);
			reused = false;
		}

		buffer.Clear();
		capacityBefore = buffer.Capacity;
		if (buffer.Capacity < minimumCapacity)
		{
			buffer.Capacity = minimumCapacity;
		}
		return buffer;
	}

	internal static void Return(List<long> buffer)
	{
		if (buffer == null) return;
		buffer.Clear();
		if (buffer.Capacity > MaxRetainedCapacity || Pool.Count >= MaxRetainedBuffers)
		{
			return;
		}
		Pool.Push(buffer);
	}

	internal static void Clear()
	{
		Pool.Clear();
	}
}
