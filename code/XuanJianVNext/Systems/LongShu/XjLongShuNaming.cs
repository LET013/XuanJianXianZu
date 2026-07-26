using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.LongShu;

internal static partial class XjLongShuSystem
{
	private const string LongShuSurname = "东方";
	private const string ChineseFamilyNameKey = "chinese_family_name";
	private const int LongWangXianJiThreshold = 4;

	private static readonly string[] LongShuGivenNames =
	{
		"浮岚", "玄澜", "清溟", "沧汐", "镜海", "灵泽", "霁涛", "云渊",
		"星澜", "寒潮", "渌川", "瀚汐", "素波", "玄潋", "青溟", "琼澜"
	};

	private static readonly string[] GeneralLongJunTitles =
	{
		"沧宁", "玄泽", "清溟", "渊衡", "澜真", "海晏", "泠川", "云津",
		"潮生", "镜渊", "素澜", "玄汐", "溟和", "清渊", "沧仪", "灵津"
	};

	private static readonly string[] KanShuiLongJunTitles = { "寒渊", "玄溟", "北沧", "冥津", "沉川", "朔海" };
	private static readonly string[] LuShuiLongJunTitles = { "澄渌", "清泠", "碧川", "素澜", "镜波", "净泉" };
	private static readonly string[] HeShuiLongJunTitles = { "朝宗", "汇澜", "会川", "归海", "同流", "合渊" };
	private static readonly string[] FuShuiLongJunTitles = { "藏渊", "沧府", "玄宫", "深庭", "幽壑", "府溟" };
	private static readonly string[] PinShuiLongJunTitles = { "含生", "柔渊", "静海", "玄牝", "育澜", "生津" };

	private static string BuildLongShuName(long actorId, int currentYear, int ordinal)
	{
		string title = ResolveLongShuTitle(XjRealmIds.ZiFu, ResolveLongShuDaoTu(actorId), actorId, 0);
		return title + "·" + BuildLongShuBaseName(actorId) + "-紫府";
	}

	internal static bool TryApplyRealmTitle(Actor actor, string realmId, string daoTu)
	{
		if (!IsLongShu(actor) || string.IsNullOrWhiteSpace(realmId))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		string baseName = BuildLongShuBaseName(actorId);
		string title = ResolveLongShuTitle(realmId, daoTu, actorId, XjXianJiAccessor.BuildState(actor).Count);
		string realmDisplay = ResolveRealmDisplay(realmId);
		((BaseSystemData)actor.data).set(ChineseFamilyNameKey, LongShuSurname);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = title + "·" + baseName + "-" + realmDisplay;
		actor.data.custom_name = true;
		return true;
	}

	internal static void RefreshTitleAfterXianJiChange(Actor actor)
	{
		if (!IsLongShu(actor)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu))
		{
			return;
		}

		TryApplyRealmTitle(actor, realmId, daoTu);
	}

	private static string ResolveLongShuTitle(string realmId, string daoTu, long actorId, int xianJiCount)
	{
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return "龙子";
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			string title = XjRealmTitleNameLibrary.GenerateTitle(realmId, daoTu, actorId);
			return xianJiCount >= LongWangXianJiThreshold
				? ReplaceTitleSuffix(title, "真人", "龙王", "沧渊龙王")
				: EnsureTitleSuffix(title, "真人", "沧渊真人");
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return GenerateLongShuJinDanTitle(daoTu, actorId);
		}

		return string.Empty;
	}

	private static string GenerateLongShuJinDanTitle(string daoTu, long actorId)
	{
		string[] pool = ResolveLongJunTitlePool(daoTu);
		int index = XjDeterministicHash.PositiveIndex(actorId, "longshu.jindan_title." + (daoTu ?? string.Empty), pool.Length);
		return pool[index] + "龙君";
	}

	private static string[] ResolveLongJunTitlePool(string daoTu)
	{
		return (daoTu ?? string.Empty).Trim() switch
		{
			"坎水" => KanShuiLongJunTitles,
			"渌水" => LuShuiLongJunTitles,
			"合水" => HeShuiLongJunTitles,
			"府水" => FuShuiLongJunTitles,
			"牝水" => PinShuiLongJunTitles,
			_ => GeneralLongJunTitles
		};
	}

	private static string EnsureTitleSuffix(string title, string suffix, string fallback)
	{
		string normalized = (title ?? string.Empty).Trim();
		return normalized.EndsWith(suffix, StringComparison.Ordinal) ? normalized : fallback;
	}

	private static string ReplaceTitleSuffix(string title, string oldSuffix, string newSuffix, string fallback)
	{
		string normalized = (title ?? string.Empty).Trim();
		return normalized.EndsWith(oldSuffix, StringComparison.Ordinal)
			? normalized.Substring(0, normalized.Length - oldSuffix.Length) + newSuffix
			: fallback;
	}

	private static string BuildLongShuBaseName(long actorId)
	{
		int index = XjDeterministicHash.PositiveIndex(actorId, "longshu.given_name", LongShuGivenNames.Length);
		return LongShuSurname + LongShuGivenNames[index];
	}

	private static string ResolveRealmDisplay(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return "金丹";
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return "紫府";
		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal)) return "筑基";
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)) return "炼气";
		return "胎息";
	}
}
