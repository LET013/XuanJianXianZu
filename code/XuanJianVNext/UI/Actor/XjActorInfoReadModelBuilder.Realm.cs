using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildStageBonusSummary(string realmId, float zhenYuan, int xianJiCount, int jinDanYiXiang)
		{
			if (string.IsNullOrWhiteSpace(realmId))
			{
				return string.Empty;
			}
	
			if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
			{
				int tier = ResolveTaiXiTier(zhenYuan);
				return tier <= 0 ? string.Empty : "真元上限300 - 寿元+" + tier.ToString(CultureInfo.InvariantCulture);
			}
	
			if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
			{
				int tier = ResolveLianQiTier(zhenYuan);
				return tier <= 0 ? string.Empty : "真元上限1200 - 寿元+" + (tier * 10).ToString(CultureInfo.InvariantCulture);
			}
	
			if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
			{
				int tier = ResolveZhuJiTier(zhenYuan);
				int lifespan = tier switch
				{
					1 => 100,
					2 => 120,
					3 => 150,
					_ => 0
				};
				return tier <= 0 ? string.Empty : "真元上限36000 - 寿元+" + lifespan.ToString(CultureInfo.InvariantCulture);
			}
	
			if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
			{
				return "真元上限" + ResolveZiFuCap(xianJiCount).ToString(CultureInfo.InvariantCulture);
			}
			if (string.Equals(realmId, XjRealmIds.HuangGuan, StringComparison.Ordinal))
			{
				return "寿元480";
			}
			if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
			{
				return "寿元980";
			}
	
			if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
			{
				return "真元上限1000000 - 寿元" + ResolveJinDanLifespanText(jinDanYiXiang);
			}
			if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
			{
				return "寿元" + ResolveZhenJunLifespanText(jinDanYiXiang);
			}
			if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
			{
				return "真元上限1000000";
			}
	
			return string.Empty;
		}

		private static string ResolveJinDanLifespanText(int jinDanYiXiang)
		{
			if (jinDanYiXiang >= 6000) return "6000";
			if (jinDanYiXiang >= 3000) return "5000";
			if (jinDanYiXiang >= 1000) return "4000";
			return "3000";
		}

		private static string ResolveZhenJunLifespanText(int jinDanYiXiang)
		{
			if (jinDanYiXiang >= 6000) return "10368";
			if (jinDanYiXiang >= 3000) return "8640";
			if (jinDanYiXiang >= 1000) return "7200";
			return "6000";
		}

		private static string BuildEraBonusSummary(string authoritativeDaoTu, string displayDaoTu)
		{
			string lookupDaoTu = (authoritativeDaoTu ?? string.Empty).Trim();
			if (!XjEraBonusService.TryGetProfileForDaoTu(lookupDaoTu, out XjEraBonusProfile profile))
			{
				lookupDaoTu = (displayDaoTu ?? string.Empty).Trim();
				if (!XjEraBonusService.TryGetProfileForDaoTu(lookupDaoTu, out profile))
				{
					return "<color=#808080>无</color>";
				}
			}
	
			bool active = XjEraBonusService.IsActiveForDaoTu(lookupDaoTu);
			string color = active ? "#FFFFFF" : "#808080";
			string state = active ? "当前生效" : "当前未生效";
			return "<color=" + color + ">"
				+ profile.DisplayName + "（" + state + "）\n    "
				+ profile.EffectText + "</color>";
		}

		private static string BuildMingShuBonusSummary(string realmId, int aptitude, float congenital, float acquired)
		{
			if (!TryGetMajorBreakthroughRule(realmId, out float cap, out float maxBonus, out int realmMultiplier, out string realmName, out string targetRealmName))
			{
				return "当前境界暂无大境界突破加成";
			}
	
			float rawEffective = Math.Max(0f, congenital * realmMultiplier + acquired);
			float effectiveDisplayValue = cap > 0f ? Math.Min(rawEffective, cap) : rawEffective;
			float bonus = cap > 0f ? Math.Min(1f, Math.Max(0f, rawEffective / cap)) * maxBonus : 0f;
			string mingShuChild = aptitude >= XjMingShuChildSystem.MingShuChildMinimumAptitude
				&& congenital >= XjMingShuChildSystem.MingShuChildCongenitalThreshold
				? "\n    命数子"
				: string.Empty;
			return mingShuChild
				+ "\n    " + realmName + "→" + targetRealmName + "突破加成：" + FormatBreakthroughAid(bonus)
				+ "\n    有效命数 " + FormatMingShuDisplay(effectiveDisplayValue) + " · 上限" + FormatMingShuDisplay(cap);
		}

		private static bool TryGetMajorBreakthroughRule(string realmId, out float cap, out float maxBonus, out int realmMultiplier, out string realmName, out string targetRealmName)
		{
			cap = 0f;
			maxBonus = 0f;
			realmMultiplier = 1;
			realmName = "未入道";
			targetRealmName = string.Empty;
			switch (realmId)
			{
				case XjRealmIds.TaiXi:
					cap = 100f;
					maxBonus = 0.9f;
					realmMultiplier = 1;
					realmName = "胎息";
					targetRealmName = "炼气";
					return true;
				case XjRealmIds.LianQi:
					cap = 1000f;
					maxBonus = 0.7f;
					realmMultiplier = 10;
					realmName = "炼气";
					targetRealmName = "筑基";
					return true;
				case XjRealmIds.ZhuJi:
					cap = 10000f;
					maxBonus = 0.5f;
					realmMultiplier = 100;
					realmName = "筑基";
					targetRealmName = "紫府";
					return true;
				case XjRealmIds.ZiFu:
					cap = 100000f;
					maxBonus = 0.3f;
					realmMultiplier = 1000;
					realmName = "紫府";
					targetRealmName = "金丹";
					return true;
				case XjRealmIds.JinDan:
				case XjRealmIds.ZhenJunYuShi:
					cap = 1000000f;
					maxBonus = 0.1f;
					realmMultiplier = 10000;
					realmName = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
						? "真君羽士"
						: "金丹";
					targetRealmName = "道胎";
					return true;
				case XjRealmIds.ShenDan:
					cap = 1000000f;
					maxBonus = 0.1f;
					realmMultiplier = 10000;
					realmName = "神丹";
					targetRealmName = "道胎";
					return true;
				default:
					return false;
			}
		}

		private static string FormatMingShuDisplay(float value)
		{
			float normalized = Math.Max(0f, value);
			if (normalized >= 10000f)
			{
				float wan = normalized / 10000f;
				return wan >= 100f
					? wan.ToString("0", CultureInfo.InvariantCulture) + "万"
					: wan.ToString("0.##", CultureInfo.InvariantCulture) + "万";
			}
	
			return ((int)Math.Floor(normalized)).ToString(CultureInfo.InvariantCulture);
		}

		private static string FormatBreakthroughAid(float value)
		{
			float normalized = Math.Clamp(value, 0f, 1f);
			float percent = normalized * 100f;
			return percent.ToString(percent >= 10f ? "0.#" : "0.##", CultureInfo.InvariantCulture) + "%";
		}

		private static string TryGetCurrentAgeId()
		{
			try
			{
				return World.world?.era_manager?.getCurrentAge()?.id ?? string.Empty;
			}
			catch
			{
				return string.Empty;
			}
		}

		private static string BuildBreakthroughSummary(Actor actor)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmBreakthroughLastResult, out string result))
			{
				return string.Empty;
			}
	
			if (result.StartsWith("Failure:" + XjRealmIds.LianQi, StringComparison.Ordinal))
			{
				return "炼气突破受挫";
			}
	
			if (result.StartsWith("Failure:" + XjRealmIds.ZhuJi, StringComparison.Ordinal))
			{
				return "筑基突破受挫";
			}
	
			return string.Empty;
		}

		private static int ResolveTaiXiTier(float zhenYuan)
		{
			if (zhenYuan >= 240f) return 6;
			if (zhenYuan >= 180f) return 5;
			if (zhenYuan >= 120f) return 4;
			if (zhenYuan >= 60f) return 3;
			if (zhenYuan >= 20f) return 2;
			return zhenYuan >= 10f ? 1 : 0;
		}

		private static int ResolveLianQiTier(float zhenYuan)
		{
			int tier = (int)Math.Floor(Math.Max(0f, zhenYuan) / 120f);
			return Math.Min(9, Math.Max(1, tier));
		}

		private static int ResolveZhuJiTier(float zhenYuan)
		{
			if (zhenYuan >= 24000f) return 3;
			if (zhenYuan >= 12000f) return 2;
			return zhenYuan >= 1200f ? 1 : 0;
		}

		private static int ResolveZiFuCap(int xianJiCount)
		{
			if (xianJiCount <= 0) return 36000;
			if (xianJiCount == 1) return 54000;
			if (xianJiCount == 2) return 72000;
			if (xianJiCount == 3) return 90000;
			if (xianJiCount == 4) return 112000;
			return 129600;
		}

	
		private static string GetRealmDisplay(string realmId, float zhenYuan, int xianJiCount, int jinDanYiXiang)
		{
			return XjDaoXingStageRules.FormatDisplay(realmId, zhenYuan, xianJiCount, jinDanYiXiang);
		}

		private static int GetJinDanYiXiang(Actor actor)
		{
			if (actor?.data == null)
			{
				return 0;
			}
	
			int baseYiXiang = XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang)
				? Math.Max(0, yiXiang)
				: 0;
			if (baseYiXiang <= 0)
			{
				return 0;
			}
	
			return XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, baseYiXiang);
		}
}

