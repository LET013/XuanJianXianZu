using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjShenDanMethodState
{
	internal readonly bool Found;
	internal readonly string Name;
	internal readonly string TargetDaoTu;
	internal readonly string TargetPositionType;
	internal readonly string Source;
	internal readonly int Year;

	internal XjShenDanMethodState(
		bool found,
		string name,
		string targetDaoTu,
		string targetPositionType,
		string source,
		int year)
	{
		Found = found;
		Name = name ?? string.Empty;
		TargetDaoTu = targetDaoTu ?? string.Empty;
		TargetPositionType = targetPositionType ?? string.Empty;
		Source = source ?? string.Empty;
		Year = Math.Max(0, year);
	}
}

/// <summary>
/// 紫金体系参悟神丹“托果法门”的专属入口。神丹身份、果位挂靠与容量注册
/// 由共享注册表统一管理，服气养性通过自身求真君流程进入同一共享神丹结果；
/// 服气不执行本类的求金法/五仙基参悟步骤，但不再被神丹身份逻辑排除。
/// </summary>
internal static class XjShenDanMethodSystem
{
	private const int MinimumDaoHui = 70;

	internal static XjShenDanMethodState BuildState(Actor actor)
	{
		if (actor?.data == null) return default;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanMethodName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanMethodTargetDaoTu, out string targetDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanMethodTargetPosition, out string targetPosition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenDanMethodSource, out string source);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanMethodReady, out int ready);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanMethodYear, out int year);
		bool found = ready == 1
			&& !string.IsNullOrWhiteSpace(name)
			&& !string.IsNullOrWhiteSpace(targetDaoTu)
			&& !string.IsNullOrWhiteSpace(targetPosition);
		return new XjShenDanMethodState(found, name, targetDaoTu, targetPosition, source, year);
	}

	internal static void OnQiuJinFaReady(Actor actor, in XjQiuJinFaState qiuJinFa, int currentYear)
	{
		if (actor?.data == null
			|| XjXianGuoSystem.IsDiMingYang(actor)
			|| !qiuJinFa.Found
			|| !qiuJinFa.Ready
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return;
		}

		int safeYear = Math.Max(1, currentYear > 0 ? currentYear : qiuJinFa.LastYear);
		if (BuildState(actor).Found) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenDanMethodEvaluatedYear, out int evaluatedYear)
			&& evaluatedYear > 0)
		{
			return;
		}

		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		// 求金法可能早于第五门神通圆满；未满足完整求位结构时不能消耗
		// 唯一的托果法门参悟机会。
		if (xianJi.Count < XjXianJiState.MaxCount) return;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string sourceDaoTu);
		if (XjDaoTuVisibleTraitCatalog.TryResolveDaoTuByTraitId(sourceDaoTu, out string visibleDaoTu))
			sourceDaoTu = visibleDaoTu;
		XjHighRealmDaoStateService.UpdateIntentionFromShenTong(actor, sourceDaoTu, xianJi);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionType, out string positionType);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, out string targetDaoTu);
		positionType = XjGuoWeiCalculator.NormalizePositionType(positionType);
		if (string.IsNullOrWhiteSpace(positionType)
			|| string.IsNullOrWhiteSpace(targetDaoTu)
			|| string.Equals(positionType, XjGuoWeiCalculator.NoDoor, StringComparison.Ordinal))
		{
			return;
		}

		int daoHui = Math.Max(0, (int)XjDaoHuiPolicy.Read(actor));
		if (daoHui < MinimumDaoHui || string.IsNullOrWhiteSpace(qiuJinFa.BoundAuthority)) return;

		// 所有前置条件齐备后才锁定本次参悟，防止求金法先成、神通后成的角色
		// 被永久误判为“没有托果法门（神丹）”。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodEvaluatedYear, safeYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		// 顶级道慧者也仅约一成机会；借来的求金法再减半，防止神丹重新泛滥。
		int chanceBasisPoints = Math.Min(1150, 250 + (daoHui - MinimumDaoHui) * 30);
		bool selfComprehended = string.Equals(qiuJinFa.ReasonCode, "QiuJinFaComprehended", StringComparison.Ordinal)
			|| string.Equals(qiuJinFa.ReasonCode, "Ok", StringComparison.Ordinal);
		if (!selfComprehended) chanceBasisPoints = Math.Max(100, chanceBasisPoints / 2);
		int roll = XjDeterministicHash.PositiveIndex(
			actorId + safeYear,
			(qiuJinFa.Name ?? string.Empty) + "|shendan_method|" + targetDaoTu + "|" + positionType,
			10000);
		if (roll >= chanceBasisPoints) return;

		string methodName = BuildMethodName(qiuJinFa.Name, positionType, qiuJinFa.BoundAuthority);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodName, methodName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodTargetDaoTu, targetDaoTu.Trim());
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodTargetPosition, positionType);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodSource,
			selfComprehended ? "自行参悟" : "传承所得");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodReady, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodYear, safeYear);
	}

	internal static bool CanPursue(
		Actor actor,
		string targetDaoTu,
		string targetPositionType,
		in XjQiuJinFaState qiuJinFa,
		int currentYear)
	{
		if (actor?.data == null
			|| XjXianGuoSystem.IsDiMingYang(actor)
			|| XjCultivationPathRules.IsFuQiYangXing(actor)) return false;
		OnQiuJinFaReady(actor, qiuJinFa, currentYear);
		XjShenDanMethodState state = BuildState(actor);
		return state.Found
			&& string.Equals(state.TargetDaoTu.Trim(), (targetDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal)
			&& string.Equals(
				XjGuoWeiCalculator.NormalizePositionType(state.TargetPositionType),
				XjGuoWeiCalculator.NormalizePositionType(targetPositionType),
				StringComparison.Ordinal);
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		XjShenDanMethodState state = BuildState(actor);
		if (!state.Found) return string.Empty;
		string source = string.IsNullOrWhiteSpace(state.Source) ? string.Empty : "，" + state.Source.Trim();
		return state.Name + "（托" + state.TargetDaoTu + state.TargetPositionType + source + "）";
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodTargetDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodTargetPosition, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjShenDanMethodSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjShenDanMethodEvaluatedYear, 0);
	}

	private static string BuildMethodName(string qiuJinFaName, string positionType, string authority)
	{
		string root = string.IsNullOrWhiteSpace(qiuJinFaName) ? "托果法" : qiuJinFaName.Trim();
		string chapter = string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? "托正篇"
			: string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? "寄闰篇" : "附余篇";
		string authorityMark = string.IsNullOrWhiteSpace(authority) ? string.Empty : "·" + authority.Trim();
		return root + "·" + chapter + authorityMark;
	}
}
