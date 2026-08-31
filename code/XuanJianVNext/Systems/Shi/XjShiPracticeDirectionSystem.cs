using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修四无修持的唯一判定入口。古释入门时自择一途；今释不自由改途，
/// 始终随当前挂靠金地的三十二天类别变化。释修不读取任何功法、法门或功法参悟字段。
/// </summary>
internal static class XjShiPracticeDirectionSystem
{
	private static readonly int[] ZhantanlinFragments = BuildZhantanlinFragments();

	internal static string EnsureForActor(Actor actor, int annualYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor))
			return XjShiPracticeDirectionIds.Unassigned;

		// 0.9.8.4及更早用临时law_ids表示“已入门”，新版迁移后必须清空，
		// 防止角色信息页和旁路再次把释修误显示成有功法。
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiLawIds, string.Empty);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPracticeDirectionId, out string current);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, out string source);
		long actorId = ((BaseSystemData)actor.data).id;
		int year = Math.Max(1, annualYear);

		string resolved;
		string resolvedSource;
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			resolved = XjShiPracticeDirectionIds.IsKnown(current)
				? current : XjShiPracticeDirectionIds.ResolveAncientChoice(actorId);
			resolvedSource = XjShiPracticeDirectionIds.IsKnown(current) && !string.IsNullOrWhiteSpace(source)
				? source : "ancient_self_choice";
		}
		else if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			resolved = ResolveModernDirection(actor, actorId, year, out resolvedSource);
		}
		else
		{
			resolved = XjShiPracticeDirectionIds.Unassigned;
			resolvedSource = string.Empty;
		}

		if (!string.Equals(current, resolved, StringComparison.Ordinal)
			|| !string.Equals(source, resolvedSource, StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionId, resolved);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, resolvedSource);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiPracticeDirectionConfirmedYear, year);
		}
		return resolved;
	}

	internal static float GetPracticeMultiplier(Actor actor, int annualYear)
	{
		string direction = EnsureForActor(actor, annualYear);
		if (actor?.data == null || !XjShiPracticeDirectionIds.IsKnown(direction))
			return XjShiCatalog.UnanchoredModernPracticeMultiplier;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!TryResolveSupportingJinDi(actor, out XjShiDomainRecord domain))
		{
			return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				? XjShiCatalog.AncientSelfPracticeMultiplier
				: XjShiCatalog.UnanchoredModernPracticeMultiplier;
		}

		int score = ResolveScaleScore(domain);
		float bonus = Math.Min(XjShiCatalog.MaximumJinDiPracticeMultiplier - 1f,
			(score / (float)Math.Max(1, XjShiCatalog.JinDiScaleScorePerFivePercent)) * 0.05f);
		return Math.Clamp(1f + bonus, XjShiCatalog.UnanchoredModernPracticeMultiplier,
			XjShiCatalog.MaximumJinDiPracticeMultiplier);
	}

	internal static string GetDisplay(Actor actor, int annualYear)
	{
		string direction = EnsureForActor(actor, annualYear);
		if (!XjShiPracticeDirectionIds.IsKnown(direction)) return "尚未系定金地";
		return XjShiPracticeDirectionIds.GetDisplay(direction) + " · "
			+ XjShiPracticeDirectionIds.GetMeaning(direction);
	}

	internal static string GetSupportDisplay(Actor actor, int annualYear)
	{
		_ = annualYear;
		if (!TryResolveSupportingJinDi(actor, out XjShiDomainRecord domain))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				? "自修自证，不借外在金地" : "尚未系定金地";
		}

		string display = XjShiDomainCatalog.GetDomainDisplayName(domain);
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal))
			return "旃檀林";
		if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
			|| (!string.IsNullOrWhiteSpace(domain.AbsorbedByDomainId)
				&& string.Equals(domain.AbsorbedByDomainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)))
			return display + "（归于旃檀林）";
		return display;
	}

	private static int ResolveScaleScore(XjShiDomainRecord domain)
	{
		if (domain == null) return 0;
		int ownScore = Math.Max(0, domain.Growth)
			+ Math.Max(0, domain.MapRadius) * 10
			+ Math.Max(0, domain.AbsorbedJinDiCount) * 120
			+ Math.Max(0, domain.SourceHeavenFragmentCount) * 120;
		// 金地并入旃檀林或其他应土后，实际承载规模来自所在释土。
		// 取两者较大值，避免把碎片和母土重复相加，也让旃檀林作为最大真土
		// 对挂靠其内金地的今释提供最高档修持承载。
		if (!string.IsNullOrWhiteSpace(domain.AbsorbedByDomainId)
			&& XjShiDomainState.TryGet(domain.AbsorbedByDomainId, out XjShiDomainRecord host)
			&& host != null && !ReferenceEquals(host, domain))
		{
			int hostScore = Math.Max(0, host.Growth)
				+ Math.Max(0, host.MapRadius) * 10
				+ Math.Max(0, host.AbsorbedJinDiCount) * 120
				+ Math.Max(0, host.SourceHeavenFragmentCount) * 120;
			return Math.Max(ownScore, hostScore);
		}
		return ownScore;
	}

	private static string ResolveModernDirection(Actor actor, long actorId, int annualYear, out string source)
	{
		source = string.Empty;
		if (XjShiDomainState.TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord owned)
			&& XjShiPracticeDirectionIds.IsKnown(owned.SourceHeavenCategory))
		{
			source = owned.DomainId;
			return owned.SourceHeavenCategory;
		}

		string[] keys =
		{
			XjActorDataKeys.ShiRebirthAnchorId,
			XjActorDataKeys.ShiJinDiId,
			XjActorDataKeys.ShiDomainId
		};
		for (int i = 0; i < keys.Length; i++)
		{
			if (!XjActorAccessor.TryGetString(actor, keys[i], out string domainId)
				|| string.IsNullOrWhiteSpace(domainId)
				|| !XjShiDomainState.TryGet(domainId, out XjShiDomainRecord domain)) continue;
			if (XjShiPracticeDirectionIds.IsKnown(domain.SourceHeavenCategory))
			{
				source = domain.DomainId;
				return domain.SourceHeavenCategory;
			}
			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal))
			{
				return ResolveZhantanlinFragment(actorId, annualYear, out source);
			}
		}

		// 今释在旃檀林内没有独占金地时，仍须具体挂靠其中一块应身碎片，
		// 只登记修持锚点，不改写金地所有权。
		if (XjZhantanlinSystem.IsPlaced || XjShiDomainState.TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out _))
			return ResolveZhantanlinFragment(actorId, annualYear, out source);
		return XjShiPracticeDirectionIds.Unassigned;
	}

	private static string ResolveZhantanlinFragment(long actorId, int annualYear, out string source)
	{
		source = string.Empty;
		if (ZhantanlinFragments.Length == 0) return XjShiPracticeDirectionIds.Unassigned;
		int index = XjDeterministicHash.PositiveIndex(actorId,
			"shi_zhantanlin_practice_anchor", ZhantanlinFragments.Length);
		int fragmentIndex = ZhantanlinFragments[index];
		source = XjShiHeavenCatalog.BuildFragmentId(fragmentIndex);
		if (XjShiDomainState.TryGet(source, out XjShiDomainRecord fragment)
			&& XjShiPracticeDirectionIds.IsKnown(fragment.SourceHeavenCategory))
			return fragment.SourceHeavenCategory;
		return XjShiHeavenCatalog.GetCategoryId(XjShiHeavenCatalog.GetHeavenIndexForFragment(fragmentIndex));
	}

	private static bool TryResolveSupportingJinDi(Actor actor, out XjShiDomainRecord domain)
	{
		domain = null;
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjShiDomainState.TryGetOwnedNorthJinDi(actorId, out domain)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPracticeDirectionSource, out string source)
			&& !string.IsNullOrWhiteSpace(source) && XjShiDomainState.TryGet(source, out domain)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthAnchorId, out string anchor)
			&& !string.IsNullOrWhiteSpace(anchor) && XjShiDomainState.TryGet(anchor, out domain)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out string jinDiId)
			&& !string.IsNullOrWhiteSpace(jinDiId) && XjShiDomainState.TryGet(jinDiId, out domain)) return true;
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId)
			&& !string.IsNullOrWhiteSpace(domainId) && XjShiDomainState.TryGet(domainId, out domain)) return true;
		return false;
	}

	private static int[] BuildZhantanlinFragments()
	{
		List<int> result = new List<int>(XjShiHeavenCatalog.ZhantanlinFragmentCount);
		for (int i = 0; i < XjShiHeavenCatalog.TotalFragments; i++)
			if (XjShiHeavenCatalog.IsZhantanlinFragment(i)) result.Add(i);
		return result.ToArray();
	}
}
