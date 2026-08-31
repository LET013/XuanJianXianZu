using System;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 将服气状态机已经真实启动的长期修持投影到人物列传。
/// 本观察器只读取当前年度索引角色的既有年份字段，不抽取随机事件、
/// 不遍历额外角色，也不保存新的玩法状态。
/// </summary>
internal static class XjFuQiNarrativeEventObserver
{
	internal static void ObserveAfterAnnualTick(Actor actor, int currentYear)
	{
		if (actor?.data == null
			|| currentYear <= 0
			|| !XjFuQiCoreRouter.TryResolveActorCore(actor, out XjFuQiCoreDefinition definition)
			|| !definition.GameplayImplemented)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(normalized))
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, out int startYear)
				&& startYear == currentYear)
			{
				XjThreeBookWriter.RecordFuQiCultivationPhase(
					actor,
					"core",
					"感气立修",
					"感应本路道气后，开始以性命温养" + SafeCoreName(definition) + "。此时神妙尚未成形，修持才刚刚起步。",
					currentYear);
			}
			return;
		}

		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiBodyProjectStartYear, out int bodyStartYear)
			&& bodyStartYear == currentYear)
		{
			XjThreeBookWriter.RecordFuQiCultivationPhase(
				actor,
				"body",
				"神妙求身",
				"已得本命神妙，开始使" + SafeCoreName(definition) + "由外归身，以性命承其真意。",
				currentYear);
			return;
		}

		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectStartYear, out int perfectionStartYear)
			&& perfectionStartYear == currentYear)
		{
			XjThreeBookWriter.RecordFuQiCultivationPhase(
				actor,
				"perfection",
				"性命合炼",
				"求到自身之真后，继续温养" + SafeCoreName(definition) + "，使神妙、性命与道气渐趋圆满。",
				currentYear);
		}
	}

	private static string SafeCoreName(in XjFuQiCoreDefinition definition)
	{
		return string.IsNullOrWhiteSpace(definition.DisplayName) ? "本命神妙" : definition.DisplayName.Trim();
	}
}
