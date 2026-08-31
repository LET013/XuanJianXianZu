using System;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 渊照空证前后的正果封锁切换。
/// 本世界起始年起至“起始年+500”的空证事务真正执行之前：太阴、坎水两枚正果
/// 被创道者暗中系住，同时渊照正果尚未诞生；空证完成后，太阴、坎水归还天地，
/// 新生渊照正果也直接落入天地位序，三枚正果均可由后世正常求证。
/// 只管理正位的时代封锁；余位、闰位遵循各自正常生成与竞争规则。
/// </summary>
internal static class XjYuanZhaoFruitSealPolicy
{
	internal enum SealStage : byte
	{
		None = 0,
		PreKongZhengSourceFruits = 1,
		PreKongZhengYuanZhaoNotBorn = 2
	}

	internal static bool IsSealed(string daoTu, string positionType, int currentYear)
	{
		return TryResolveSealStage(daoTu, positionType, currentYear, out _);
	}

	internal static bool TryResolveSealStage(string daoTu, string positionType, int currentYear, out SealStage stage)
	{
		stage = SealStage.None;
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string normalizedType = XjGuoWeiCalculator.NormalizePositionType(positionType);
		if (!string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(normalizedDaoTu)) return false;

		int year = Math.Max(1, currentYear);
		// 可用性查询不能初始化世界时间轴；只读取bootstrap已经固化的五百年节点。
		int triggerYear = XjYuanZhaoKongZhengEvent.ScheduledTriggerYear;
		bool beforeScheduledYear = triggerYear > 0 && year < triggerYear;
		bool eventTriggered = XjYuanZhaoKongZhengEvent.IsTriggered;
		// 到达触发年份并不代表空证事务已经执行；同年角色修炼可能先于世界事件。
		if (beforeScheduledYear || !eventTriggered)
		{
			if (string.Equals(normalizedDaoTu, XjYuanZhaoKongZhengEvent.SourceTaiYin, StringComparison.Ordinal)
				|| string.Equals(normalizedDaoTu, XjYuanZhaoKongZhengEvent.SourceKanShui, StringComparison.Ordinal))
			{
				stage = SealStage.PreKongZhengSourceFruits;
				return true;
			}
			// 真正空证之前天地尚无“渊照正果”，旧版异常渊照修士也不能提前证果。
			if (string.Equals(normalizedDaoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal))
			{
				stage = SealStage.PreKongZhengYuanZhaoNotBorn;
				return true;
			}
		}

		// 0.9.8.15：空证之后渊照果位为无主正果，和太阴、坎水一起正常开放。
		return false;
	}

	internal static bool TryBuildAttemptNarrative(string actorName, string daoTu, string positionType, int currentYear, out string text)
	{
		text = string.Empty;
		if (!TryResolveSealStage(daoTu, positionType, currentYear, out SealStage stage)) return false;
		string safeActorName = string.IsNullOrWhiteSpace(actorName) ? "有修士" : actorName.Trim();
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		string positionName = normalizedDaoTu + XjGuoWeiCalculator.ZhengWei;
		if (stage == SealStage.PreKongZhengSourceFruits)
		{
			text = safeActorName + "欲证" + positionName
				+ "，金门将合之际，却觉此位早已有主；其人名讳与道痕尽隐于天机之后，唯知正果不可落身。";
			return true;
		}
		text = safeActorName + "欲证" + positionName
			+ "，却见天地道网中此途尚无正果可承；水月有影而渊鉴未成，只得止步。";
		return true;
	}

	internal static bool IsHiddenSourceFruitOccupancy(string daoTu, int currentYear)
	{
		return TryResolveSealStage(daoTu, XjGuoWeiCalculator.ZhengWei, currentYear, out SealStage stage)
			&& stage == SealStage.PreKongZhengSourceFruits;
	}

	/// <summary>空证前太阴、坎水只公开“正果有人”，不披露持位者与渊照谜底。</summary>
	internal static bool TryBuildVisibleLockSummary(string daoTu, int currentYear, out string summary)
	{
		if (IsHiddenSourceFruitOccupancy(daoTu, currentYear))
		{
			summary = "果位在位·持位者未披露";
			return true;
		}
		summary = string.Empty;
		return false;
	}
}
