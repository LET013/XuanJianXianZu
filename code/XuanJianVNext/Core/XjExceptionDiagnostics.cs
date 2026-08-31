using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace XuanJianVNext.Core;

/// <summary>
/// 限频异常诊断。部分调用点位于 WorldBox Parallel worker；因此诊断器自身
/// 不能再成为一个新的非并发 Dictionary 故障源。限频状态由单锁保护，时间
/// 使用 Stopwatch，避免 worker 线程读取 Unity Time。
/// </summary>
internal static class XjExceptionDiagnostics
{
    private sealed class Entry
    {
        internal int Year;
        internal int Count;
        internal long NextAllowedTimestamp;
    }

    private const int MaximumKeys = 512;
    private const int MaximumReportsPerYear = 2;
    private const int MinimumIntervalSeconds = 15;
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly Queue<string> Order = new(MaximumKeys);
    private static readonly object Sync = new();
	private static readonly object PersistentLogSync = new();

    internal static void Report(string context, Exception exception)
    {
        if (exception == null) return;
        string key = string.IsNullOrWhiteSpace(context) ? "unknown" : context.Trim();
        int year = Math.Max(0, XjYearTracker.CurrentYear);
        long now = Stopwatch.GetTimestamp();
        bool shouldLog = false;

        lock (Sync)
        {
            if (!Entries.TryGetValue(key, out Entry entry))
            {
                TrimIfNeededCore();
                entry = new Entry { Year = year, Count = 0, NextAllowedTimestamp = 0L };
                Entries[key] = entry;
                Order.Enqueue(key);
            }
            else if (entry.Year != year)
            {
                entry.Year = year;
                entry.Count = 0;
                entry.NextAllowedTimestamp = 0L;
            }

            if (entry.Count < MaximumReportsPerYear && now >= entry.NextAllowedTimestamp)
            {
                entry.Count++;
                entry.NextAllowedTimestamp = now + Stopwatch.Frequency * MinimumIntervalSeconds;
                shouldLog = true;
            }
        }

        if (!shouldLog) return;
        string message = exception.Message ?? string.Empty;
        if (message.Length > 240) message = message.Substring(0, 240) + "…";
        UnityEngine.Debug.LogWarning("[玄鉴][限频异常] " + key
            + " year=" + year
            + " " + exception.GetType().Name
            + (message.Length > 0 ? "：" + message : string.Empty));
		// 崩溃、强制结束或原生并行 AggregateException 未必会留下可见的 NML 控制台。
		// 仅在既有限频已放行时落一行持久诊断；绝不在高频异常路径反复写盘。
		TryAppendPersistentReport(key, year, exception);
    }

    internal static void Clear()
    {
        lock (Sync)
        {
            Entries.Clear();
            Order.Clear();
        }
    }

    private static void TrimIfNeededCore()
    {
        while (Entries.Count >= MaximumKeys && Order.Count > 0)
        {
            string oldest = Order.Dequeue();
            Entries.Remove(oldest);
        }
    }

	private static void TryAppendPersistentReport(string context, int year, Exception exception)
	{
		try
		{
			string persistentRoot = Application.persistentDataPath;
			if (string.IsNullOrWhiteSpace(persistentRoot)) return;
			string directory = Path.Combine(persistentRoot, "XuanJianDiagnostics");
			string path = Path.Combine(directory, "runtime-exceptions.log");
			string stack = exception.StackTrace ?? string.Empty;
			if (stack.Length > 1600) stack = stack.Substring(0, 1600) + "…";
			string line = $"{DateTime.UtcNow:O}\tworldYear={year}\t{context}\t{exception.GetType().FullName}\t{exception.Message}\t{stack.Replace(Environment.NewLine, " | ")}{Environment.NewLine}";
			lock (PersistentLogSync)
			{
				Directory.CreateDirectory(directory);
				File.AppendAllText(path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
		catch
		{
			// 诊断绝不能成为工作线程或退出阶段的新故障源。
		}
	}
}
