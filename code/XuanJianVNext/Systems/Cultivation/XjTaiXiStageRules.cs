using System;
using System.Globalization;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjTaiXiStageRules
{
	internal const float EntryZhenYuan = 10f;
	internal const float RealmCapZhenYuan = 300f;
	private static readonly float[] StageThresholds = { 10f, 30f, 60f, 100f, 150f, 210f };
	private static readonly string[] StageNames = { "玄景轮", "承明轮", "周行轮", "青元轮", "玉京轮", "灵初轮" };

	internal static bool HasEnteredTaiXi(float zhenYuan)
	{
		return zhenYuan >= EntryZhenYuan;
	}

	internal static int ResolveStage(float zhenYuan)
	{
		if (!HasEnteredTaiXi(zhenYuan))
		{
			return 0;
		}

		for (int i = StageThresholds.Length - 1; i >= 0; i--)
		{
			if (zhenYuan >= StageThresholds[i])
			{
				return i + 1;
			}
		}

		return 0;
	}

	internal static float ResolveNextStageZhenYuan(float zhenYuan)
	{
		int stage = ResolveStage(zhenYuan);
		if (stage <= 0)
		{
			return EntryZhenYuan;
		}

		if (stage >= StageThresholds.Length)
		{
			return RealmCapZhenYuan;
		}

		return Math.Min(RealmCapZhenYuan, StageThresholds[stage]);
	}

	internal static string FormatRealmDisplay(string realmId, float zhenYuan)
	{
		if (!string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return string.Empty;
		}

		int stage = ResolveStage(zhenYuan);
		if (stage <= 0)
		{
			return "未入道";
		}

		return StageNames[stage - 1];
	}

	internal static string FormatProgress(float zhenYuan)
	{
		if (!HasEnteredTaiXi(zhenYuan))
		{
			return "真元 当前" + ToInteger(zhenYuan) + " · 门槛" + ToInteger(EntryZhenYuan);
		}

		float next = ResolveNextStageZhenYuan(zhenYuan);
		return "真元 当前" + ToInteger(zhenYuan) + " · 门槛" + ToInteger(next);
	}

	private static string ToInteger(float value)
	{
		return ((int)Math.Floor(Math.Max(0f, value))).ToString(CultureInfo.InvariantCulture);
	}
}

internal static class XjDaoXingStageRules
{
	internal static string FormatDisplay(string realmId, float zhenYuan, int xianJiCount, int jinDanYiXiang)
	{
		string normalizedRealm = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(normalizedRealm))
		{
			normalizedRealm = (realmId ?? string.Empty).Trim();
		}
		if (string.Equals(normalizedRealm, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			string prefix = string.Equals(normalizedRealm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				? "真君羽士"
				: "金丹";
			if (jinDanYiXiang >= 6000)
			{
				return prefix + "巅峰";
			}

			if (jinDanYiXiang >= 3000)
			{
				return prefix + "后期";
			}

			if (jinDanYiXiang >= 1000)
			{
				return prefix + "中期";
			}

			return prefix + "初期";
		}
		if (string.Equals(normalizedRealm, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return "神丹";
		}
		if (string.Equals(normalizedRealm, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| string.Equals(normalizedRealm, XjRealmIds.DaoTaiPlaceholder, StringComparison.Ordinal))
		{
			return "道胎";
		}
		if (string.Equals(normalizedRealm, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return "黄冠";
		}
		if (string.Equals(normalizedRealm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return "真人";
		}
		if (string.Equals(normalizedRealm, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			if (xianJiCount >= 5)
			{
				return "紫府巅峰";
			}

			if (xianJiCount >= 4)
			{
				return "紫府后期";
			}

			if (xianJiCount >= 3)
			{
				return zhenYuan >= 90000f ? "参紫门槛" : "紫府中期";
			}

			return "紫府初期";
		}

		if (string.Equals(normalizedRealm, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			if (zhenYuan < 12000f)
			{
				return "筑基初期";
			}

			if (zhenYuan < 24000f)
			{
				return "筑基中期";
			}

			return "筑基后期";
		}

		if (string.Equals(normalizedRealm, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			if (zhenYuan < 400f)
			{
				return "炼气一层";
			}

			if (zhenYuan < 500f)
			{
				return "炼气二层";
			}

			if (zhenYuan < 600f)
			{
				return "炼气三层";
			}

			if (zhenYuan < 700f)
			{
				return "炼气四层";
			}

			if (zhenYuan < 800f)
			{
				return "炼气五层";
			}

			if (zhenYuan < 900f)
			{
				return "炼气六层";
			}

			if (zhenYuan < 1000f)
			{
				return "炼气七层";
			}

			if (zhenYuan < 1100f)
			{
				return "炼气八层";
			}

			return "炼气九层";
		}

		if (string.Equals(normalizedRealm, XjRealmIds.TaiXi, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(normalizedRealm))
		{
			return XjTaiXiStageRules.FormatRealmDisplay(XjRealmIds.TaiXi, zhenYuan);
		}

		return normalizedRealm.Trim();
	}

	internal static bool IsZhuJiLateOrHigher(string realmId, float zhenYuan, int xianJiCount)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return true;
		}

		return string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)
			&& string.Equals(FormatDisplay(realmId, zhenYuan, xianJiCount, 0), "筑基后期", StringComparison.Ordinal);
	}
}
