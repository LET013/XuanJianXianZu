using System;
using System.Collections.Generic;
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
	ShiFangJie = 17,
	YuanZhaoShuiYueJing = 18,
	QingYuYa = 19,
	ManYinGuang = 20,
	DongYuShan = 21,
	XiTianYuan = 22,
	NanChouShui = 23,
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
		// 0.9.8.28：领域不再停留在“人物脚边十格”。紫府保持原范围，金丹形成
		// 真正战场级压场，道胎再上一个量级；仍远小于一次主动法术的高境余威，
		// 避免12个持续领域每2秒同时扫描百格圆域。
		if (realmTier >= XjRealmSuppression.TierDaoTai) return LargeDomain ? 36 : 26;
		if (realmTier >= XjRealmSuppression.TierJinDan) return LargeDomain ? 22 : 16;
		return LargeDomain ? 8 : 6;
	}

	internal float ResolveDurationSeconds(int realmTier)
	{
		return realmTier >= XjRealmSuppression.TierJinDan ? 10f : 8f;
	}

	internal float ResolveCooldownSeconds(int realmTier)
	{
		// 与真君/金丹主动法术统一：每门30秒独立CD；不同法术之间至少8秒由共享施法门控负责。
		_ = realmTier;
		return 30f;
	}

	internal int ResolveMaxTargets(int realmTier)
	{
		if (realmTier >= XjRealmSuppression.TierDaoTai) return 56;
		if (realmTier >= XjRealmSuppression.TierJinDan) return 32;
		return 12;
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
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ShiFangJie,
			"十方界",
			new[] { "十方界" },
			true,
			true,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.QingYuYa,
			"青玉崖",
			new[] { "青玉崖" },
			false,
			false,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.ManYinGuang,
			"满垠㷐",
			new[] { "满垠㷐", "满烬煌" },
			false,
			true,
			0.20f,
			0.27f,
			0f,
			0f),
		// 0.9.8.7 都卫三方镇域：名称与表现按用户提供的1456章神通表校准。
		// 旧名东利山/西天璟/南悯水保留为别名，避免旧档已学神通失效。
		new XjDomainSkillDefinition(
			XjDomainSkillKind.DongYuShan,
			"东羽山",
			new[] { "东羽山", "东利山" },
			false,
			true,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.XiTianYuan,
			"西天塬",
			new[] { "西天塬", "西天璟" },
			false,
			true,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.NanChouShui,
			"南惆水",
			new[] { "南惆水", "南悯水" },
			false,
			true,
			0f,
			0f,
			0f,
			0f),
		new XjDomainSkillDefinition(
			XjDomainSkillKind.YuanZhaoShuiYueJing,
			"水月照真境",
			new[] { "月沉渊", "照无身", "回澜鉴", "影归真", "一泓寂" },
			true,
			true,
			0.32f,
			0.42f,
			0f,
			0f),
	};

	internal static bool HasAnyDefinition(string[] shenTongIds)
	{
		if (shenTongIds == null || shenTongIds.Length == 0) return false;
		for (int i = 0; i < shenTongIds.Length; i++)
		{
			string learnedId = (shenTongIds[i] ?? string.Empty).Trim();
			if (learnedId.Length == 0) continue;
			for (int d = 0; d < Definitions.Length; d++)
			{
				string[] aliases = Definitions[d].ShenTongIds;
				for (int a = 0; a < aliases.Length; a++)
				{
					if (string.Equals(learnedId, aliases[a], StringComparison.Ordinal)) return true;
				}
			}
		}
		return false;
	}

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

	internal static int CollectDefinitions(Actor actor, List<XjDomainSkillDefinition> buffer)
	{
		if (buffer == null) return 0;
		buffer.Clear();
		if (actor?.data == null) return 0;

		string[] shenTongIds = XjXianJiAccessor.ReadRawIds(actor);
		for (int i = 0; i < shenTongIds.Length; i++)
		{
			string learnedId = (shenTongIds[i] ?? string.Empty).Trim();
			if (learnedId.Length == 0) continue;
			for (int d = 0; d < Definitions.Length; d++)
			{
				XjDomainSkillDefinition candidate = Definitions[d];
				if (ContainsKind(buffer, candidate.Kind)) continue;
				string[] aliases = candidate.ShenTongIds;
				for (int a = 0; a < aliases.Length; a++)
				{
					if (!string.Equals(learnedId, aliases[a], StringComparison.Ordinal)) continue;
					buffer.Add(candidate);
					break;
				}
			}
		}
		return buffer.Count;
	}

	private static bool ContainsKind(List<XjDomainSkillDefinition> buffer, XjDomainSkillKind kind)
	{
		for (int i = 0; i < buffer.Count; i++)
		{
			if (buffer[i].Kind == kind) return true;
		}
		return false;
	}
}
