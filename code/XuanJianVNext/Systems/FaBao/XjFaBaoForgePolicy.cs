using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.FaBao;

/// <summary>
/// 法器/灵宝/法宝炼制统一门槛、三年周期、持有上限与概率。
/// 同一角色的武器和五个防具槽共用一次炼制机会，杜绝同年多链重复炼器。
/// </summary>
internal static class XjFaBaoForgePolicy
{
	internal const int ZhuJiFaQiChancePercent = 65;
	internal const int ZiFuLingWuChancePercent = 88;
	internal const int PersonalZiFuLingBaoChancePercent = 40;
	internal const int PersonalZiFuLingBaoAttemptIntervalYears = 1;
	internal const int JinDanZhengWeiChancePercent = 100;
	internal const int JieLinChancePercent = 90;
	internal const int JinDanRunWeiChancePercent = 80;
	internal const int JinDanYuWeiChancePercent = 70;
	internal const int AttemptIntervalYears = 2;
	internal const int ZhuJiMaxManagedItems = 2;
	internal const int ZiFuMaxManagedItems = 3;
	internal const int JinDanMaxManagedItems = 6;
	internal const int WarehouseAttemptIntervalYears = 12;
	internal const int ZhuJiWarehouseLifetimeLimit = 3;
	internal const int ZiFuWarehouseLifetimeLimit = 2;
	internal const int JinDanWarehouseLifetimeLimit = 1;

	private static readonly EquipmentType[] ManagedEquipmentTypes =
	{
		EquipmentType.Helmet,
		EquipmentType.Armor,
		EquipmentType.Boots,
		EquipmentType.Ring,
		EquipmentType.Amulet
	};


	internal static string ResolvePracticeRealmId(Actor actor, string actualRealmId)
	{
		if (actor?.data == null || !XjCraftTraitRules.CanRefineArtifacts(actor)) return string.Empty;

		string liveRealmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		string realmId = string.IsNullOrWhiteSpace(liveRealmId) ? (actualRealmId ?? string.Empty).Trim() : liveRealmId;
		bool isJinDan = string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal);
		bool isZiFu = string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal);
		bool isZhuJi = string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal);
		bool isLow = string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal);
		if (!isJinDan && !isZiFu && !isZhuJi && !isLow) return string.Empty;

		int rank = XjCraftProficiencySystem.GetArtifactRank(actor);
		// 炼器品级决定可处理的器物层次，修士境界仍限制当前肉身能够稳定承载的最高器物。
		if (isJinDan && rank >= XjCraftProficiencySystem.RankJinDan) return XjRealmIds.JinDan;
		if ((isJinDan || isZiFu) && rank >= XjCraftProficiencySystem.RankZiFu) return XjRealmIds.ZiFu;
		if ((isJinDan || isZiFu || isZhuJi) && rank >= XjCraftProficiencySystem.RankZhuJi) return XjRealmIds.ZhuJi;
		return rank >= XjCraftProficiencySystem.RankLianQi ? XjRealmIds.LianQi : string.Empty;
	}


	/// <summary>
	/// 紫府本命灵宝属于“三神通后的个人炼宝”，不是百艺炼器生产。
	/// 不要求炼器师特质或炼器品级，但正式炼制只允许消耗同道途紫府灵物。
	/// 本命灵宝写入原生武器槽，筑基法器仅是前置武器，不计入本命器物。
	/// </summary>
	internal static bool NeedsPersonalZiFuLingBao(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			|| XjXianJiAccessor.BuildState(actor).Count < 3)
		{
			return false;
		}

		XjFaBaoState primary = XjFaBaoAccessor.BuildState(actor);
		return !primary.Found || XjFaBaoCatalog.IsZhuJiFaQi(primary.ClassName);
	}

	internal static bool CanAttemptPersonalZiFuLingBao(Actor actor, int year)
	{
		if (!NeedsPersonalZiFuLingBao(actor) || year <= 0)
		{
			return false;
		}

		return !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZiFuPersonalLingBaoLastAttemptYear, out int lastYear)
			|| lastYear <= 0
			|| year >= lastYear + PersonalZiFuLingBaoAttemptIntervalYears;
	}

	internal static bool TryReservePersonalZiFuLingBaoAttempt(Actor actor, int year)
	{
		if (!CanAttemptPersonalZiFuLingBao(actor, year))
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZiFuPersonalLingBaoLastAttemptYear, year);
		// 本命灵宝与百艺槽位炼器共享当年正式炼制机会，避免同年连续产出两件器物。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoDefenseLastAttemptYear, year);
		return true;
	}

	internal static int ResolveChancePercent(Actor actor, string realmId)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(realmId)
			|| !XjCraftTraitRules.CanRefineArtifacts(actor))
		{
			return 0;
		}

		int craftRank = XjCraftProficiencySystem.GetArtifactRank(actor);
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& craftRank < XjCraftProficiencySystem.RankZiFu) return 0;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& craftRank < XjCraftProficiencySystem.RankJinDan) return 0;

		int baseChance;
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			baseChance = ZhuJiFaQiChancePercent;
		}
		else if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			baseChance = XjXianJiAccessor.BuildState(actor).Count >= 3
				? ZiFuLingWuChancePercent
				: 0;
		}
		else if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			if (XjXuanJianShenTongSpecials.IsJieLinXian(actor))
			{
				baseChance = JieLinChancePercent;
			}
			else
			{
				string guoWei = ReadGuoWei(actor);
				if (string.IsNullOrWhiteSpace(guoWei)) return 0;
				if (guoWei.Contains(XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) baseChance = JinDanZhengWeiChancePercent;
				else if (guoWei.Contains(XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) baseChance = JinDanRunWeiChancePercent;
				else if (guoWei.Contains(XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) baseChance = JinDanYuWeiChancePercent;
				else return 0;
			}
		}
		else
		{
			return 0;
		}

		// 炼器品级负责层次门槛，经验只影响普通装备稀有度与法器/灵宝/法宝数值。
		int chanceCap = string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal) ? 100 : 95;
		return Math.Clamp(baseChance, 0, chanceCap);
	}

	internal static bool CanAttemptScheduled(Actor actor, string realmId, int year)
	{
		return CanAttempt(actor, realmId, year, requireRetreat: true);
	}

	internal static bool CanAttemptPromotion(Actor actor, string realmId, int year)
	{
		// 境界晋升结算发生在境界写入链内，不应再要求角色此刻仍处于睡眠动作。
		return CanAttempt(actor, realmId, year, requireRetreat: false);
	}

	private static bool CanAttempt(Actor actor, string realmId, int year, bool requireRetreat)
	{
		if (actor?.data == null || year <= 0 || ResolveChancePercent(actor, realmId) <= 0)
		{
			return false;
		}

		// 金丹的年度炼器仍只发生在宗门洞天闭关/睡眠状态；晋升当次结算例外。
		if (requireRetreat
			&& string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& !XjZongMenDongTianLifecycle.IsRetreatActive(actor))
		{
			return false;
		}

		return !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, out int lastYear)
			|| lastYear <= 0
			|| year >= lastYear + AttemptIntervalYears;
	}

	internal static bool TryReserveScheduledAttempt(Actor actor, string realmId, int year)
	{
		return TryReserveAttempt(actor, realmId, year, requireRetreat: true);
	}

	internal static bool TryReservePromotionAttempt(Actor actor, string realmId, int year)
	{
		return TryReserveAttempt(actor, realmId, year, requireRetreat: false);
	}

	private static bool TryReserveAttempt(Actor actor, string realmId, int year, bool requireRetreat)
	{
		if (!CanAttempt(actor, realmId, year, requireRetreat))
		{
			return false;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, year);
		// 同步旧键，避免旧版双回调在迁移年内额外再跑一次。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoDefenseLastAttemptYear, year);
		return true;
	}

	internal static bool RollAnnual(Actor actor, string realmId, int year, string channel)
	{
		return RollAnnual(actor, realmId, year, channel, ResolveChancePercent(actor, realmId));
	}

	internal static bool RollAnnual(Actor actor, string realmId, int year, string channel, int chancePercent)
	{
		int chance = Math.Clamp(chancePercent, 0, 100);
		if (chance <= 0 || actor?.data == null || year <= 0)
		{
			return false;
		}
		if (chance >= 100 || actor.hasTrait("ChuShen8")) return true;

		long actorId = ((BaseSystemData)actor.data).id;
		string salt = "fabao_forge|" + (channel ?? string.Empty) + "|" + realmId + "|" + chance;
		return XjDeterministicHash.PositiveIndex(actorId + year, salt, 100) < chance;
	}

	internal static int ResolveManagedItemLimit(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return ZhuJiMaxManagedItems;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return ZiFuMaxManagedItems;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return JinDanMaxManagedItems;
		return 0;
	}

	internal static int CountManagedItems(Actor actor)
	{
		if (actor?.data == null) return 0;
		int count = XjFaBaoAccessor.HasState(actor) ? 1 : 0;
		if (actor.equipment == null) return count;
		for (int i = 0; i < ManagedEquipmentTypes.Length; i++)
		{
			ActorEquipmentSlot slot = actor.equipment.getSlot(ManagedEquipmentTypes[i]);
			Item item = slot?.getItem();
			if (item?.data != null && XjFaBaoEquipmentSync.TryReadFaBaoState(item, out _)) count++;
		}
		return count;
	}

	internal static bool CanCreateNewManagedItem(Actor actor, string realmId)
	{
		int limit = ResolveManagedItemLimit(realmId);
		return limit > 0 && CountManagedItems(actor) < limit;
	}

	/// <summary>
	/// 角色个人器物已满后，允许以很低频率为家族/宗门器库炼制少量余器。
	/// 每个境界档位有终身硬上限，防止长寿炼器师产出数十乃至数百件器物。
	/// </summary>
	internal static bool CanAttemptWarehouseForge(Actor actor, string realmId, int year)
	{
		if (actor?.data == null || year <= 0 || ResolveChancePercent(actor, realmId) <= 0)
		{
			return false;
		}

		int lifetimeLimit = ResolveWarehouseLifetimeLimit(realmId);
		if (lifetimeLimit <= 0 || ReadWarehouseForgeCount(actor, realmId) >= lifetimeLimit)
		{
			return false;
		}

		return !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjFaBaoWarehouseLastForgeYear, out int lastYear)
			|| lastYear <= 0
			|| year >= lastYear + WarehouseAttemptIntervalYears;
	}

	internal static bool TryReserveWarehouseForge(Actor actor, string realmId, int year)
	{
		if (!CanAttemptWarehouseForge(actor, realmId, year))
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoWarehouseLastForgeYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoLastAttemptYear, year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjFaBaoDefenseLastAttemptYear, year);
		return true;
	}

	internal static void RecordWarehouseForgeSuccess(Actor actor, string realmId)
	{
		if (actor?.data == null)
		{
			return;
		}

		string key = ResolveWarehouseCountKey(realmId);
		if (string.IsNullOrWhiteSpace(key))
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, key, out int current);
		XjActorAccessor.SetInt(actor, key, Math.Max(0, current) + 1);
	}

	internal static int ReadWarehouseForgeCount(Actor actor, string realmId)
	{
		string key = ResolveWarehouseCountKey(realmId);
		if (actor?.data == null || string.IsNullOrWhiteSpace(key))
		{
			return 0;
		}

		return XjActorAccessor.TryGetInt(actor, key, out int count) ? Math.Max(0, count) : 0;
	}

	private static int ResolveWarehouseLifetimeLimit(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return ZhuJiWarehouseLifetimeLimit;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return ZiFuWarehouseLifetimeLimit;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return JinDanWarehouseLifetimeLimit;
		return 0;
	}

	private static string ResolveWarehouseCountKey(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return XjActorDataKeys.XjFaBaoWarehouseFaQiCount;
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return XjActorDataKeys.XjFaBaoWarehouseLingBaoCount;
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return XjActorDataKeys.XjFaBaoWarehouseFaBaoCount;
		return string.Empty;
	}

	private static string ReadGuoWei(Actor actor)
	{
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQuanBingGuoWei, out string activeGuoWei)
			&& !string.IsNullOrWhiteSpace(activeGuoWei))
		{
			return activeGuoWei.Trim();
		}

		return XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanGuoWei, out string guoWei)
			? (guoWei ?? string.Empty).Trim()
			: string.Empty;
	}
}
