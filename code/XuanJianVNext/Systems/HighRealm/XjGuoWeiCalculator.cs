using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiCalculator
{
	// 旧代码曾用“正位”指代一道唯一主位，现统一显示为“果位”。
	// LegacyZhengWei 仅用于旧档迁移；五现中的正位由 XjFiveManifestationKind 表达。
	internal const string ZhengWei = "果位";
	internal const string LegacyZhengWei = "正位";
	internal const string RunWei = "闰位";
	internal const string YuWei = "余位";
	internal const string NoDoor = "无门";

	internal static string Calculate(string daoTu, in XjXianJiState xianJiState)
	{
		// 无角色上下文只承认普通近邻闰位；远亲/空证必须由真实道慧证明。
		return CalculateCore(daoTu, xianJiState, 0f);
	}

	internal static string Calculate(Actor actor, string daoTu, in XjXianJiState xianJiState)
	{
		float daoHui = actor?.data != null ? XjDaoHuiPolicy.Read(actor) : 0f;
		return CalculateCore(daoTu, xianJiState, daoHui);
	}

	private static string CalculateCore(string daoTu, in XjXianJiState xianJiState, float daoHui)
	{
		if (xianJiState.Count < XjXianJiState.MaxCount || xianJiState.Ids == null)
		{
			return NoDoor;
		}

		if (XjZiJinSwordDaoCatalog.IsLongGeng(daoTu))
		{
			return XjFuQiSwordWorldState.IsEstablished
				&& XjZiJinSwordDaoCatalog.HasExactShenTongSet(xianJiState)
				? YuWei
				: NoDoor;
		}

		int nativeCount = 0;
		int lowerCount = 0;
		int adjacentCount = 0;
		int otherCount = 0;
		for (int i = 0; i < xianJiState.Ids.Length && i < XjXianJiState.MaxCount; i++)
		{
			switch (XjXianJiCatalog.GetPoolKind(daoTu, xianJiState.Ids[i]))
			{
			case XjXianJiPoolKind.Native:
				nativeCount++;
				break;
			case XjXianJiPoolKind.Lower:
				lowerCount++;
				break;
			case XjXianJiPoolKind.Adjacent:
				adjacentCount++;
				break;
			default:
				otherCount++;
				break;
			}
		}

		// 五门全为本道途上位神通时，只能证明本道途果位；果位被占也不能降格成闰位。
		if (nativeCount == XjXianJiState.MaxCount && lowerCount == 0 && adjacentCount == 0 && otherCount == 0)
		{
			return ZhengWei;
		}

		if (string.Equals(NormalizeDaoTu(daoTu), "青宣", StringComparison.Ordinal))
		{
			if (nativeCount >= 4 && lowerCount == 1 && adjacentCount == 0 && otherCount == 0) return YuWei;
			return NoDoor;
		}

		// 闰位五门必须全部是上位神通，并严格保持“四根一显”：
		// 四门本道上位作为根道，只允许一门外道上位指向唯一显道。
		// 3+2 会让显道反过来重写根道，历史上正是“厥阴闰太阴却变成四门太阴”的来源。
		// 道论近邻由位序层统一按普通闰位门槛处理；同根远亲/十二炁对炁/五德映照
		// 只追加较小的结构远闰门槛。完全无拓扑关系不再以 95 道慧硬解释为“闰位”——
		// 那已经接近真正空证新道，应回到专属空证玩法，而不是让余闰承担小空证。
		if (nativeCount == 4
			&& lowerCount == 0
			&& TryResolveIntercalaryTargetDaoTu(daoTu, xianJiState, out string intercalaryTarget, out _))
		{
			// 新证道严格执行原著硬边界；目标解析本身仍保留纯结构能力，
			// 便于旧档把已经存在的历史闰位还原为“根道闰显道”而不是误写成别道。
			if (IsForbiddenIntercalaryCrossing(daoTu, intercalaryTarget)) return NoDoor;
			float requiredDaoHui = ResolveIntercalaryDaoHuiThreshold(daoTu, intercalaryTarget);
			return daoHui + 0.001f >= requiredDaoHui ? RunWei : NoDoor;
		}

		if (otherCount > 0 || adjacentCount > 0)
		{
			return NoDoor;
		}

		// 余位由本道上、下位神通共同成型：五门中至少三门为本道上位，
		// 其余一至两门为本道下位。四上位一下一位与三上位两下位都应
		// 明确指向本道余位，不能因为只混入一门下位而落成“岐路未定”。
		if (nativeCount >= 3 && lowerCount >= 1) return YuWei;
		return NoDoor;
	}

	internal static string ResolveManifestDaoTu(string daoTu, in XjXianJiState xianJiState, string guoWeiType)
	{
		if (string.Equals(NormalizePositionType(guoWeiType), RunWei, StringComparison.Ordinal)
			&& TryResolveIntercalaryTargetDaoTu(daoTu, xianJiState, out string targetDaoTu, out _))
		{
			return targetDaoTu;
		}
		return NormalizeDaoTu(daoTu);
	}

	internal static string ResolveIntercalarySourceDaoTu(string daoTu, in XjXianJiState xianJiState)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		return !string.IsNullOrWhiteSpace(normalizedDaoTu)
			&& TryResolveIntercalaryTargetDaoTu(normalizedDaoTu, xianJiState, out _, out _)
			? normalizedDaoTu
			: string.Empty;
	}

	/// <summary>
	/// 从五门神通自身的唯一归属反推出闰位的“根道→显道”。
	/// 不依赖角色当前 DaoTu，专门避免成位、读档或迁移后以显道反推，
	/// 将闰位重新写回原道途。合法结构只允许唯一的“四门本道上位根道”，
	/// 以及一门指向唯一外道显位的上位神通。
	/// </summary>
	internal static bool TryResolveIntercalaryIdentity(
		Actor actor,
		in XjXianJiState xianJiState,
		out string sourceDaoTu,
		out string manifestDaoTu)
	{
		sourceDaoTu = string.Empty;
		manifestDaoTu = string.Empty;
		if (xianJiState.Count < XjXianJiState.MaxCount
			|| xianJiState.Ids == null
			|| xianJiState.Ids.Length < XjXianJiState.MaxCount)
		{
			return false;
		}

		HashSet<string> candidates = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < XjXianJiState.MaxCount; i++)
		{
			if (!XjXianJiCatalog.TryGetDefinition(xianJiState.Ids[i], out XjShenTongDefinition definition)
				|| definition == null
				|| definition.Tier != XjShenTongTier.Upper)
			{
				continue;
			}

			string owner = NormalizeDaoTu(definition.DaoTuId);
			if (owner.Length > 0) candidates.Add(owner);
		}

		foreach (string candidate in candidates)
		{
			// 这里只做“既有五神通结构”的身份还原，不重新判定这条闰路在新规则下
			// 是否还能证成。这样历史上已经存在的牝水+少阳等旧闰位仍可准确显示
			// 为“牝水闰少阳”，但 Calculate 的新证道入口会严格拒绝水德闰三阳/
			// 火德闰三阴。
			if (!TryResolveIntercalaryTargetDaoTu(candidate, xianJiState, out string target, out _))
			{
				continue;
			}

			if (sourceDaoTu.Length > 0
				&& (!string.Equals(sourceDaoTu, candidate, StringComparison.Ordinal)
					|| !string.Equals(manifestDaoTu, target, StringComparison.Ordinal)))
			{
				// 多重根道意味着神通结构本身不唯一，不能任意挑一个闰位归属。
				sourceDaoTu = string.Empty;
				manifestDaoTu = string.Empty;
				return false;
			}

			sourceDaoTu = candidate;
			manifestDaoTu = target;
		}

		return sourceDaoTu.Length > 0
			&& manifestDaoTu.Length > 0
			&& !string.Equals(sourceDaoTu, manifestDaoTu, StringComparison.Ordinal);
	}

	internal static bool TryResolveIntercalaryTargetDaoTu(
		string daoTu,
		in XjXianJiState xianJiState,
		out string targetDaoTu,
		out bool remote)
	{
		targetDaoTu = string.Empty;
		remote = false;
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| xianJiState.Count < XjXianJiState.MaxCount
			|| xianJiState.Ids == null)
		{
			return false;
		}

		int nativeCount = 0;
		int lowerCount = 0;
		int externalCount = 0;
		string resolvedTarget = string.Empty;
		for (int i = 0; i < xianJiState.Ids.Length && i < XjXianJiState.MaxCount; i++)
		{
			string id = xianJiState.Ids[i];
			// 证闰硬规则：五门仙基无一例外都必须登记为上位神通。
			// 这条校验独立于 PoolKind，专门堵住旧档、调试赋予、仓库映射等
			// 绕过正常生成池后把“外道下位”误认成闰位的情况。
			if (!XjXianJiCatalog.TryGetDefinition(id, out XjShenTongDefinition definition)
				|| definition == null
				|| definition.Tier != XjShenTongTier.Upper)
			{
				return false;
			}

			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(normalizedDaoTu, id);
			if (kind == XjXianJiPoolKind.Native)
			{
				nativeCount++;
				continue;
			}
			if (kind == XjXianJiPoolKind.Lower)
			{
				lowerCount++;
				continue;
			}
			if (!XjXianJiCatalog.TryResolveOwningDaoTu(id, out string owner)
				|| string.IsNullOrWhiteSpace(owner)
				|| string.Equals(owner, normalizedDaoTu, StringComparison.Ordinal))
			{
				return false;
			}
			string normalizedOwner = NormalizeDaoTu(owner);
			if (string.IsNullOrWhiteSpace(normalizedOwner)) return false;
			// 所有闰位都只承认外道上位；上方已经对五个槽位统一做 Tier=Upper 硬校验。
			if (resolvedTarget.Length == 0) resolvedTarget = normalizedOwner;
			else if (!string.Equals(resolvedTarget, normalizedOwner, StringComparison.Ordinal)) return false;
			externalCount++;
		}

		if (nativeCount != 4 || lowerCount != 0 || externalCount != 1
			|| string.IsNullOrWhiteSpace(resolvedTarget))
		{
			return false;
		}
		targetDaoTu = resolvedTarget;
		remote = !XjDaoTuRelationCatalog.IsDirectAdjacent(normalizedDaoTu, resolvedTarget);
		return true;
	}

	internal static XjDaoTuRelationKind ResolveIntercalaryRelation(string sourceDaoTu, string targetDaoTu)
	{
		return XjDaoTuRelationCatalog.Resolve(NormalizeDaoTu(sourceDaoTu), NormalizeDaoTu(targetDaoTu));
	}

	internal static float ResolveIntercalaryDaoHuiThreshold(string sourceDaoTu, string targetDaoTu)
	{
		XjDaoTuRelationKind relation = ResolveIntercalaryRelation(sourceDaoTu, targetDaoTu);
		return relation switch
		{
			XjDaoTuRelationKind.DirectAdjacent => 0f,
			XjDaoTuRelationKind.Counterpart => XjDaoHuiPolicy.StructuredRemoteThreshold,
			XjDaoTuRelationKind.SameRootRemote => XjDaoHuiPolicy.StructuredRemoteThreshold,
			XjDaoTuRelationKind.ElementAffinity => XjDaoHuiPolicy.StructuredRemoteThreshold,
			// 无道论拓扑即不属于普通闰位。返回超过道慧上限的哨兵值，
			// 既保留旧档身份解析能力，又禁止新角色靠 95 道慧把任意外道“小空证”成闰。
			_ => XjDaoHuiPolicy.Maximum + 1f
		};
	}

	internal static bool IsForbiddenIntercalaryCrossing(string sourceDaoTu, string targetDaoTu)
	{
		string source = NormalizeDaoTu(sourceDaoTu);
		string target = NormalizeDaoTu(targetDaoTu);

		// 渊照由太阴之“照”与坎水之“渊”空证而来，闰位关系封闭为两条：
		// 渊照↔太阴、渊照↔坎水。即使道慧达到完整空证门槛，也不能远闰第三道。
		if (string.Equals(source, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal))
		{
			return !IsYuanZhaoIntercalaryCounterpart(target);
		}
		if (string.Equals(target, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal))
		{
			return !IsYuanZhaoIntercalaryCounterpart(source);
		}

		// 虹霞是借戊土而空证的独立道途；不像普通五德分支可按拓扑远闰。
		// 只允许虹霞↔戊土，双向封闭，避免旧档/调试神通混入其它道途后被判成闰位。
		if (string.Equals(source, XjHongXiaLuoXiaEvent.DaoTu, StringComparison.Ordinal))
		{
			return !IsHongXiaIntercalaryCounterpart(target);
		}
		if (string.Equals(target, XjHongXiaLuoXiaEvent.DaoTu, StringComparison.Ordinal))
		{
			return !IsHongXiaIntercalaryCounterpart(source);
		}

		// 原著边界：水德不可闰三阳，火德不可闰三阴。
		return IsShuiDe(source) && IsSanYang(target)
			|| IsHuoDe(source) && IsSanYin(target);
	}

	private static bool IsYuanZhaoIntercalaryCounterpart(string daoTu)
	{
		string normalized = NormalizeDaoTu(daoTu);
		return string.Equals(normalized, XjYuanZhaoKongZhengEvent.SourceTaiYin, StringComparison.Ordinal)
			|| string.Equals(normalized, XjYuanZhaoKongZhengEvent.SourceKanShui, StringComparison.Ordinal);
	}

	private static bool IsHongXiaIntercalaryCounterpart(string daoTu)
	{
		return string.Equals(NormalizeDaoTu(daoTu), XjHongXiaLuoXiaEvent.SourceWuTu, StringComparison.Ordinal);
	}

	private static bool IsShuiDe(string daoTu)
	{
		return daoTu is "坎水" or "渌水" or "合水" or "府水" or "牝水";
	}

	private static bool IsHuoDe(string daoTu)
	{
		return daoTu is "离火" or "灴火" or "并火" or "真火" or "牡火";
	}

	private static bool IsSanYang(string daoTu)
	{
		return daoTu is "太阳" or "少阳" or "明阳";
	}

	private static bool IsSanYin(string daoTu)
	{
		return daoTu is "太阴" or "少阴" or "厥阴";
	}

	internal static bool TryResolveRemoteIntercalaryDaoTu(string daoTu, in XjXianJiState xianJiState, out string remoteDaoTu)
	{
		remoteDaoTu = string.Empty;
		if (!TryResolveIntercalaryTargetDaoTu(daoTu, xianJiState, out string target, out bool remote) || !remote)
		{
			return false;
		}
		remoteDaoTu = target;
		return true;
	}

	internal static string GenerateGuoWeiName(string daoTu, string guoWeiType, long seed)
	{
		string normalizedDaoTu = XjDaoTuIntentIdentity.ResolveCore(daoTu);
		string normalizedType = NormalizePositionType(guoWeiType);
		if (string.IsNullOrWhiteSpace(normalizedType) || string.Equals(normalizedType, NoDoor, StringComparison.Ordinal)) return NoDoor;
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)) return NoDoor;
		if (!XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedDaoTu, out _)) return NoDoor;

		int maxSlot = XjFruitPositionWorldState.ResolveSlotCount(normalizedDaoTu, normalizedType);
		if (maxSlot <= 0) return NoDoor;
		int slotIndex = maxSlot <= 1
			? 1
			: XjDeterministicHash.PositiveIndex(seed, normalizedDaoTu + "|" + normalizedType, maxSlot) + 1;
		return BuildGuoWeiSlotName(normalizedDaoTu, normalizedType, slotIndex);
	}

	internal static string BuildGuoWeiSlotName(string daoTu, string guoWeiType, int slotIndex)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedType = NormalizePositionType(guoWeiType);
		if (string.IsNullOrWhiteSpace(normalizedType)) normalizedType = NoDoor;
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)) return NoDoor;
		if (slotIndex <= 1) return normalizedDaoTu + normalizedType;
		return normalizedDaoTu + ToChineseNumber(slotIndex) + normalizedType;
	}

	/// <summary>
	/// 果位公告专用显示名。落盘 ID 仍保持“太阴四余位”等旧格式，
	/// 仅展示为“太阴第四余位”，避免破坏旧档与持有者索引。
	/// </summary>
	internal static string BuildGuoWeiSlotDisplayName(string daoTu, string guoWeiType, int slotIndex)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedType = NormalizePositionType(guoWeiType);
		if (string.IsNullOrWhiteSpace(normalizedType)) normalizedType = NoDoor;
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)) return NoDoor;
		if (slotIndex <= 1) return normalizedDaoTu + normalizedType;
		return normalizedDaoTu + "第" + ToChineseNumber(slotIndex) + normalizedType;
	}

	internal static string GetChineseSlotOrdinal(int slotIndex)
	{
		return "第" + ToChineseNumber(Math.Max(1, slotIndex));
	}

	internal static string NormalizeGuoWeiName(string guoWeiName)
	{
		string normalized = (guoWeiName ?? string.Empty).Trim();
		if (normalized.EndsWith(LegacyZhengWei, StringComparison.Ordinal))
		{
			return normalized.Substring(0, normalized.Length - LegacyZhengWei.Length) + ZhengWei;
		}
		return normalized;
	}

	internal static string NormalizePositionType(string positionType)
	{
		string normalized = (positionType ?? string.Empty).Trim();
		return string.Equals(normalized, LegacyZhengWei, StringComparison.Ordinal) ? ZhengWei : normalized;
	}

	internal static string GetDisplayGuoWeiName(string guoWeiName)
	{
		string normalized = NormalizeGuoWeiName(guoWeiName);
		if (string.IsNullOrWhiteSpace(normalized)) return string.Empty;
		if (normalized.EndsWith(RunWei, StringComparison.Ordinal)) return StripGuoWeiDisplayOrdinal(normalized, RunWei);
		if (normalized.EndsWith(YuWei, StringComparison.Ordinal)) return StripGuoWeiDisplayOrdinal(normalized, YuWei);
		return normalized;
	}

	internal static string ResolveDaoTuFromGuoWeiName(string guoWeiName)
	{
		string value = NormalizeGuoWeiName(guoWeiName);
		string type = value.EndsWith(YuWei, StringComparison.Ordinal)
			? YuWei
			: value.EndsWith(RunWei, StringComparison.Ordinal)
				? RunWei
				: value.EndsWith(ZhengWei, StringComparison.Ordinal) ? ZhengWei : string.Empty;
		if (type.Length == 0) return string.Empty;

		int suffixStart = value.Length - type.Length;
		int ordinalStart = suffixStart;
		while (ordinalStart > 0 && IsGuoWeiOrdinalChar(value[ordinalStart - 1])) ordinalStart--;
		return NormalizeDaoTu(value.Substring(0, ordinalStart));
	}

	internal static int ResolveSlotIndex(string guoWei)
	{
		string value = NormalizeGuoWeiName(guoWei);
		string type = value.EndsWith(YuWei, StringComparison.Ordinal)
			? YuWei
			: value.EndsWith(RunWei, StringComparison.Ordinal)
				? RunWei
				: value.EndsWith(ZhengWei, StringComparison.Ordinal) ? ZhengWei : string.Empty;
		if (type.Length == 0) return 1;
		int suffixStart = value.Length - type.Length;
		int ordinalStart = suffixStart;
		while (ordinalStart > 0 && IsGuoWeiOrdinalChar(value[ordinalStart - 1])) ordinalStart--;
		if (ordinalStart == suffixStart) return 1;
		string ordinal = value.Substring(ordinalStart, suffixStart - ordinalStart);
		return ParseChineseNumber(ordinal);
	}

	internal static string NormalizeDaoTu(string daoTu)
	{
		string normalizedDaoTu = XjDaoTuIntentIdentity.ResolveCore(daoTu);
		return !string.IsNullOrWhiteSpace(normalizedDaoTu) && XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedDaoTu, out _)
			? normalizedDaoTu
			: string.Empty;
	}

	private static string StripGuoWeiDisplayOrdinal(string guoWeiName, string weiType)
	{
		int suffixStart = guoWeiName.Length - weiType.Length;
		int ordinalStart = suffixStart;
		while (ordinalStart > 0 && IsGuoWeiOrdinalChar(guoWeiName[ordinalStart - 1])) ordinalStart--;
		return ordinalStart == suffixStart ? guoWeiName : guoWeiName.Substring(0, ordinalStart) + weiType;
	}

	private static bool IsGuoWeiOrdinalChar(char c)
	{
		return char.IsDigit(c)
			|| c == '零' || c == '一' || c == '二' || c == '三' || c == '四'
			|| c == '五' || c == '六' || c == '七' || c == '八' || c == '九' || c == '十';
	}

	private static string ToChineseNumber(int number)
	{
		return number switch
		{
			2 => "二", 3 => "三", 4 => "四", 5 => "五", 6 => "六",
			7 => "七", 8 => "八", 9 => "九", 10 => "十", _ => number.ToString()
		};
	}

	private static int ParseChineseNumber(string value)
	{
		if (int.TryParse(value, out int number)) return Math.Max(1, number);
		return value switch
		{
			"二" => 2, "三" => 3, "四" => 4, "五" => 5, "六" => 6,
			"七" => 7, "八" => 8, "九" => 9, "十" => 10, _ => 1
		};
	}
}
