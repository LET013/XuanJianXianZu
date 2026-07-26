using System;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Data.HighRealm;

internal enum XjDomainSkillKind
{
	None = 0,
	XuanYinCanYi = 1,
	MingDiGuanYuan = 2,
	ZhiYangXuanLei = 3,
	ZaiZheHui = 4,
	ZhuLiaoHui = 5,
	GuiLiuChu = 6,
	TianXiaHan = 7,
	JiaoLuoYuan = 8,
	YangShengZhu = 9,
	ChiDuanZu = 10,
	WeiCongXian = 11,
	RuZhongZhuo = 12,
	BingHongXia = 13,
	QingQiYunJing = 14,
	BuKongJie = 15,
	LuoChaHai = 16,
}

internal readonly struct XjDomainSkillDefinition
{
	internal XjDomainSkillDefinition(
		XjDomainSkillKind kind,
		string displayName,
		string[] shenTongIds,
		bool followCaster,
		bool largeDomain,
		float ziFuPulseDamageMultiplier,
		float jinDanPulseDamageMultiplier,
		float ziFuHealRatio,
		float jinDanHealRatio)
	{
		Kind = kind;
		DisplayName = displayName ?? string.Empty;
		ShenTongIds = shenTongIds ?? Array.Empty<string>();
		FollowCaster = followCaster;
		LargeDomain = largeDomain;
		ZiFuPulseDamageMultiplier = Math.Max(0f, ziFuPulseDamageMultiplier);
		JinDanPulseDamageMultiplier = Math.Max(0f, jinDanPulseDamageMultiplier);
		ZiFuHealRatio = Math.Max(0f, ziFuHealRatio);
		JinDanHealRatio = Math.Max(0f, jinDanHealRatio);
	}

	internal XjDomainSkillKind Kind { get; }
	internal string DisplayName { get; }
	internal string[] ShenTongIds { get; }
	internal bool FollowCaster { get; }
	internal bool LargeDomain { get; }
	internal float ZiFuPulseDamageMultiplier { get; }
	internal float JinDanPulseDamageMultiplier { get; }
	internal float ZiFuHealRatio { get; }
	internal float JinDanHealRatio { get; }

	internal int ResolveRadius(int realmTier)
	{
		bool jinDan = realmTier >= XjRealmSuppression.TierJinDan;
		if (LargeDomain) return jinDan ? 10 : 8;
		return jinDan ? 8 : 6;
	}

	internal float ResolveDurationSeconds(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan ? 10f : 8f;
	}

	internal float ResolveCooldownSeconds(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan ? 35f : 45f;
	}

	internal int ResolveMaxTargets(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan ? 20 : 12;
	}

	internal float ResolvePulseDamageMultiplier(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan
			? JinDanPulseDamageMultiplier
			: ZiFuPulseDamageMultiplier;
	}

	internal float ResolveHealRatio(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan ? JinDanHealRatio : ZiFuHealRatio;
	}
}

internal static class XjDomainSkillCatalog
{
	private static readonly XjDomainSkillDefinition[] Definitions =
	{
		new XjDomainSkillDefinition(
			XjDomainSkillKind.XuanYinCanYi,
			"玄阴参疑境",
			new[] { "不二舆", "参疑室" },
			false,
			false,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.MingDiGuanYuan,
			"明帝观元",
			new[] { "帝观元" },
			true,
			false,
			0.18f,
			0.22f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ZhiYangXuanLei,
			"至阳玄雷域",
			new[] { "至阳嘘" },
			true,
			false,
			0.28f,
			0.35f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ZaiZheHui,
			"再折毁域",
			new[] { "再折毁" },
			true,
			false,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ZhuLiaoHui,
			"诸蓼会",
			new[] { "诸蓼会" },
			false,
			false,
			0.10f,
			0.12f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.GuiLiuChu,
			"归流处",
			new[] { "归流处" },
			false,
			true,
			0.24f,
			0.30f,
			0.010f,
			0.015f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.TianXiaHan,
			"天下熯",
			new[] { "燔旧室", "天下熯" },
			false,
			true,
			0.26f,
			0.34f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.JiaoLuoYuan,
			"狡落原",
			new[] { "狡落原" },
			false,
			false,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.YangShengZhu,
			"养生主",
			new[] { "养生主" },
			true,
			false,
			0f,
			0f,
			0.015f,
			0.020f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ChiDuanZu,
			"残阳大漠",
			new[] { "赤断镞" },
			false,
			true,
			0.20f,
			0.26f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.WeiCongXian,
			"位从险",
			new[] { "位从险" },
			false,
			false,
			0.16f,
			0.21f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.RuZhongZhuo,
			"天下覆",
			new[] { "如重浊", "天下覆" },
			false,
			true,
			0.18f,
			0.24f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.BingHongXia,
			"秉灴夏",
			new[] { "秉灴夏" },
			true,
			false,
			0.10f,
			0.14f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.QingQiYunJing,
			"清炁云境",
			new[] { "空应散" },
			false,
			false,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.BuKongJie,
			"不空劫",
			new[] { "不空劫" },
			false,
			true,
			0.24f,
			0.31f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.LuoChaHai,
			"罗剎海",
			new[] { "罗剎海", "罗刹海" },
			true,
			true,
			0f,
			0f,
			0.012f,
			0.018f),
	};

	internal static bool TryResolve(Actor actor, out XjDomainSkillDefinition definition)
	{
		definition = default;
		if (actor?.data == null) return false;

		string[] shenTongIds = XjXianJiAccessor.ReadRawIds(actor);
		for (int i = 0; i < shenTongIds.Length; i++)
		{
			string learnedId = (shenTongIds[i] ?? string.Empty).Trim();
			if (learnedId.Length == 0) continue;
			for (int d = 0; d < Definitions.Length; d++)
			{
				string[] aliases = Definitions[d].ShenTongIds;
				for (int a = 0; a < aliases.Length; a++)
				{
					if (!string.Equals(learnedId, aliases[a], StringComparison.Ordinal)) continue;
					definition = Definitions[d];
					return true;
				}
			}
		}
		return false;
	}
}
