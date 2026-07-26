using System;
using System.Globalization;

namespace XuanJianVNext.Core
{
    /// <summary>
    ///     确定性哈希工具类，提供与0.5.4语义兼容的FNV-1a哈希实现。
    ///     消除散布在16+处的重复哈希代码。
    ///     所有方法均为线程安全（纯计算，无状态）。
    /// </summary>
    public static class XjDeterministicHash
    {
        // ============================
        // 64位 FNV-1a（用于 PositiveHash / PositiveIndex）
        // 偏移基数: 14695981039346656037UL
        // 质数: 1099511628211UL
        // ============================

        private const ulong Fnv1a64Offset = 14695981039346656037UL;
        private const ulong Fnv1a64Prime = 1099511628211UL;

        /// <summary>
        ///     计算64位FNV-1a确定性哈希，返回正数long。
        /// </summary>
        /// <param name="value">主输入值</param>
        /// <param name="salt">盐值字符串（允许null，自动处理为空串）</param>
        /// <returns>0 到 long.MaxValue 之间的哈希值</returns>
        public static long PositiveHash(long value, string salt)
        {
            unchecked
            {
                ulong hash = Fnv1a64Offset;
                hash ^= (ulong)value;
                hash *= Fnv1a64Prime;

                string text = salt ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= Fnv1a64Prime;
                }

                return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
            }
        }

        /// <summary>
        ///     计算64位FNV-1a确定性哈希并取模，返回 [0, length-1] 范围内的索引。
        /// </summary>
        /// <param name="value">主输入值</param>
        /// <param name="salt">盐值字符串（允许null）</param>
        /// <param name="length">取模长度（必须 &gt; 0，否则返回0）</param>
        /// <returns>非负整数索引</returns>
        public static int PositiveIndex(long value, string salt, int length)
        {
            if (length <= 0)
            {
                return 0;
            }

            return (int)((ulong)PositiveHash(value, salt) % (ulong)length);
        }

        /// <summary>
        ///     对字符串计算稳定哈希（使用 PositiveHash(0, value)）。
        /// </summary>
        public static long StableHash(string value)
        {
            return PositiveHash(0L, value);
        }

        // ============================
        // 32位 FNV-1a（用于 Roll01 / RollRange / BuildSeedInteger）
        // 偏移基数: 2166136261u
        // 质数: 16777619u
        // ============================

        private const uint Fnv1a32Offset = 2166136261u;
        private const uint Fnv1a32Prime = 16777619u;

        /// <summary>
        ///     对输入字符串计算32位FNV-1a原始哈希值。
        /// </summary>
        private static uint ComputeHash32(string text)
        {
            uint hash = Fnv1a32Offset;
            for (int i = 0; i < text.Length; i++)
            {
                hash ^= text[i];
                hash *= Fnv1a32Prime;
            }

            return hash;
        }

        /// <summary>
        ///     突破成功率判定（对应 XjBreakthroughRules.Roll01）。
        ///     输入格式: "{actorId}|{year}|{targetRealmId}|{salt}"
        ///     返回 [0f, 1f] 范围内的浮点数，与原始实现语义一致。
        /// </summary>
        public static float Roll01(long actorId, int year, string targetRealmId, string salt)
        {
            string text = actorId.ToString(CultureInfo.InvariantCulture)
                          + "|" + year.ToString(CultureInfo.InvariantCulture)
                          + "|" + (targetRealmId ?? string.Empty)
                          + "|" + (salt ?? string.Empty);
            uint hash = ComputeHash32(text);
            return (hash % 10000u) / 9999f;
        }

        /// <summary>
        ///     浮点数范围随机（对应 XjCultivationLocalCore.RollRange float重载）。
        ///     输入格式: "{actorId}|{ageYear}|{source}"
        ///     返回 [min, max] 范围内的浮点数。
        /// </summary>
        public static float RollRange(long actorId, int ageYear, int source, float min, float max)
        {
            float safeMin = Math.Min(min, max);
            float safeMax = Math.Max(min, max);
            if (Math.Abs(safeMax - safeMin) < 0.001f)
            {
                return (float)Math.Floor(safeMin);
            }

            string text = actorId.ToString(CultureInfo.InvariantCulture)
                          + "|" + ageYear.ToString(CultureInfo.InvariantCulture)
                          + "|" + source.ToString(CultureInfo.InvariantCulture);
            uint hash = ComputeHash32(text);
            float normalized = (hash % 10000u) / 9999f;
            return (float)Math.Floor(safeMin + (safeMax - safeMin) * normalized);
        }

        /// <summary>
        ///     整数范围随机（对应 XjAptitudeEffectRules.RollRange int重载）。
        ///     输入格式: "{actorId}|{source}|{salt}"（salt为int）
        ///     返回 [min, max] 范围内的整数。
        /// </summary>
        public static int RollRange(long actorId, int source, int salt, int min, int max)
        {
            int safeMin = Math.Min(min, max);
            int safeMax = Math.Max(min, max);
            if (safeMin == safeMax)
            {
                return safeMin;
            }

            string text = actorId.ToString(CultureInfo.InvariantCulture)
                          + "|" + source.ToString(CultureInfo.InvariantCulture)
                          + "|" + salt.ToString(CultureInfo.InvariantCulture);
            uint hash = ComputeHash32(text);
            return safeMin + (int)(hash % (uint)(safeMax - safeMin + 1));
        }

        /// <summary>
        ///     种子整数生成（对应 XjCultivationSeed.BuildSeedInteger）。
        ///     输入格式: "{actorId}|{actorName}|{salt}"
        ///     返回 [minValue, maxValue] 范围内的浮点数（实际为整数，保留float以兼容原始返回类型）。
        /// </summary>
        public static float BuildSeedInteger(long actorId, string actorName, int salt, int minValue, int maxValue)
        {
            string text = actorId.ToString(CultureInfo.InvariantCulture)
                          + "|" + (actorName ?? string.Empty)
                          + "|" + salt.ToString(CultureInfo.InvariantCulture);
            uint hash = ComputeHash32(text);
            float normalized = (hash % 10000u) / 9999f;
            int min = Math.Min(minValue, maxValue);
            int max = Math.Max(minValue, maxValue);
            int value = min + (int)Math.Floor((max - min + 1) * normalized);
            return Math.Min(max, Math.Max(min, value));
        }

        /// <summary>
        ///     混合多int种子的64位PositiveIndex（用于需要组合多个int的场景）。
        ///     使用FNV-1a风格混合多个int值后再计算PositiveIndex。
        /// </summary>
        public static int PositiveIndexFromInts(int seed1, int seed2, string salt, int length)
        {
            unchecked
            {
                ulong hash = Fnv1a64Offset;
                hash ^= (ulong)seed1;
                hash *= Fnv1a64Prime;
                hash ^= (ulong)seed2;
                hash *= Fnv1a64Prime;

                string text = salt ?? string.Empty;
                for (int i = 0; i < text.Length; i++)
                {
                    hash ^= text[i];
                    hash *= Fnv1a64Prime;
                }

                long result = (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
                return length <= 0 ? 0 : (int)((ulong)result % (ulong)length);
            }
        }
    }
}
