using System;
using UnityEngine;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjLongevityRacePenalty
{
	private const float ElfZiFuBreakthroughMultiplier = 0.80f;
	private const float ElfJinDanBreakthroughMultiplier = 0.90f;

	internal static float ApplyBreakthroughSuccessPenalty(Actor actor, string targetRealmId, float chance)
	{
		if (!IsElf(actor))
		{
			return Mathf.Clamp01(chance);
		}

		if (string.Equals(targetRealmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return Mathf.Clamp01(chance * ElfZiFuBreakthroughMultiplier);
		}

		if (string.Equals(targetRealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return Mathf.Clamp01(chance * ElfJinDanBreakthroughMultiplier);
		}

		return Mathf.Clamp01(chance);
	}

	internal static float ApplyJinDanBreakthroughPenalty(Actor actor, float chance)
	{
		return IsElf(actor)
			? Mathf.Clamp01(chance * ElfJinDanBreakthroughMultiplier)
			: Mathf.Clamp01(chance);
	}

	internal static bool IsElf(Actor actor)
	{
		string id = actor?.asset?.id;
		return string.Equals(id, "elf", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(id, "unit_elf", StringComparison.OrdinalIgnoreCase);
	}
}
