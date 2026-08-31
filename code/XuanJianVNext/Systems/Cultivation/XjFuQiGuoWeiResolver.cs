using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjFuQiGuoWeiResolution
{
	internal XjFuQiGuoWeiResolution(
		string sourceDaoTu,
		string manifestDaoTu,
		string guoWeiType,
		string guoWei,
		string externalDaoTu)
	{
		SourceDaoTu = sourceDaoTu ?? string.Empty;
		ManifestDaoTu = manifestDaoTu ?? string.Empty;
		GuoWeiType = guoWeiType ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		ExternalDaoTu = externalDaoTu ?? string.Empty;
	}

	internal string SourceDaoTu { get; }
	internal string ManifestDaoTu { get; }
	internal string GuoWeiType { get; }
	internal string GuoWei { get; }
	internal string ExternalDaoTu { get; }
	internal bool Found => !string.IsNullOrWhiteSpace(ManifestDaoTu) && !string.IsNullOrWhiteSpace(GuoWei);
	internal bool ChangesDaoTu => !string.Equals(SourceDaoTu, ManifestDaoTu, StringComparison.Ordinal);
}

/// <summary>
/// 服气养性的果位分流不读取仙基/神通。0.9.9.3 起不再“正果永远优先”：
/// 本道余位与近邻闰位是更常见的派生出口；只有道慧达到正果承载门槛的人，
/// 才会在部分求证中直接冲击本道唯一果位。结构远亲仍可成闰，但门槛低于正果；
/// 完全无拓扑关系的外道不由服气自动猜测。长庚保持专属规则：初代正位由开道流程处理，
/// 后继只生余位，永不生闰位。
/// </summary>
internal static class XjFuQiGuoWeiResolver
{
	internal static bool TryResolve(
		Actor actor,
		string sourceDaoTu,
		long seed,
		bool isLongGeng,
		out XjFuQiGuoWeiResolution resolution)
	{
		resolution = default;
		if (actor?.data == null) return false;
		string source = (sourceDaoTu ?? string.Empty).Trim();
		long actorId = ((BaseSystemData)actor.data).id;
		if (source.Length == 0 || actorId <= 0L) return false;

		if (isLongGeng)
		{
			return TryResolveExact(actorId, source, XjGuoWeiCalculator.YuWei, seed,
				source, string.Empty, out resolution);
		}

		float daoHui = XjDaoHuiPolicy.Read(actor);
		bool canBearFruit = daoHui + 0.001f >= XjDaoHuiPolicy.FruitPositionThreshold;
		int routeRoll = XjDeterministicHash.PositiveIndex(seed, source + "|fuqi_position_route", 100);

		// 帝明阳即使出身服气养性，也不以外道成闰：只在本道果位与本道余位之间求证。
		// 但正果不再天然优先；道慧足够时仅约三成求证会先冲果，其余优先落余。
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			bool fruitFirst = canBearFruit && routeRoll < 30;
			if (fruitFirst
				&& TryResolveExact(actorId, source, XjGuoWeiCalculator.ZhengWei, seed,
					source, string.Empty, out resolution)) return true;
			if (TryResolveExact(actorId, source, XjGuoWeiCalculator.YuWei, seed + 7919L,
				source, string.Empty, out resolution)) return true;
			return !fruitFirst && canBearFruit
				&& TryResolveExact(actorId, source, XjGuoWeiCalculator.ZhengWei, seed + 12289L,
					source, string.Empty, out resolution);
		}

		IReadOnlyList<string> direct = XjDaoTuCatalog.GetFuQiRelatedDaoTus(source);
		bool preferResidual = (routeRoll & 1) == 0;
		bool fruitFirstGeneric = canBearFruit && routeRoll < 20;

		// 极少数真正道慧足以承果者会直接冲击正果；其余先走更常见的余/近邻闰。
		if (fruitFirstGeneric
			&& TryResolveExact(actorId, source, XjGuoWeiCalculator.ZhengWei, seed,
				source, string.Empty, out resolution)) return true;

		if (preferResidual)
		{
			if (TryResolveExact(actorId, source, XjGuoWeiCalculator.YuWei, seed + 7919L,
				source, string.Empty, out resolution)) return true;
			if (TryResolveCandidateTier(actorId, source, direct, seed, "fuqi_run_near", daoHui, out resolution)) return true;
		}
		else
		{
			if (TryResolveCandidateTier(actorId, source, direct, seed, "fuqi_run_near", daoHui, out resolution)) return true;
			if (TryResolveExact(actorId, source, XjGuoWeiCalculator.YuWei, seed + 7919L,
				source, string.Empty, out resolution)) return true;
		}

		// 结构远闰只需略高于近邻闰的道慧，不再接近空证新道。
		if (daoHui + 0.001f >= XjDaoHuiPolicy.StructuredRemoteThreshold)
		{
			IReadOnlyList<string> remote = XjDaoTuRelationCatalog.GetStructuredRemoteNeighbors(source);
			if (TryResolveCandidateTier(actorId, source, remote, seed + 4099L, "fuqi_run_remote", daoHui, out resolution))
			{
				return true;
			}
		}

		// 余闰全部无空席时，才让具备 85 道慧的角色回头尝试本道正果。
		return !fruitFirstGeneric && canBearFruit
			&& TryResolveExact(actorId, source, XjGuoWeiCalculator.ZhengWei, seed + 12289L,
				source, string.Empty, out resolution);
	}

	private static bool TryResolveCandidateTier(
		long actorId,
		string source,
		IReadOnlyList<string> candidates,
		long seed,
		string salt,
		float daoHui,
		out XjFuQiGuoWeiResolution resolution)
	{
		resolution = default;
		if (candidates == null || candidates.Count == 0) return false;

		int start = XjDeterministicHash.PositiveIndex(seed, source + "|" + salt, candidates.Count);
		for (int offset = 0; offset < candidates.Count; offset++)
		{
			string target = XjDaoTuRelationCatalog.Normalize(candidates[(start + offset) % candidates.Count]);
			if (target.Length == 0
				|| string.Equals(target, source, StringComparison.Ordinal)
				|| !XjDaoTuCatalog.SupportsFuQi(target)
				|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(source, target))
			{
				continue;
			}

			float requiredDaoHui = XjGuoWeiCalculator.ResolveIntercalaryDaoHuiThreshold(source, target);
			if (daoHui + 0.001f < requiredDaoHui) continue;
			if (TryResolveExact(actorId, target, XjGuoWeiCalculator.RunWei,
				seed + offset * 1009L + 17L, source, source, out resolution))
			{
				return true;
			}
		}
		return false;
	}

	private static bool TryResolveExact(
		long actorId,
		string claimDaoTu,
		string type,
		long seed,
		string sourceDaoTu,
		string externalDaoTu,
		out XjFuQiGuoWeiResolution resolution)
	{
		resolution = default;
		if (!XjGuoWeiRegistry.TryResolveAvailableGuoWei(
			claimDaoTu,
			type,
			actorId,
			seed,
			false,
			out string resolvedType,
			out string guoWei))
		{
			return false;
		}
		resolution = new XjFuQiGuoWeiResolution(
			sourceDaoTu,
			claimDaoTu,
			resolvedType,
			guoWei,
			externalDaoTu);
		return resolution.Found;
	}
}
