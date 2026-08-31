using System;
using XuanJianVNext.Data.FaBao;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoDescriptionFormatter
{
	private const string SourceJinDan = "JinDan";
	private const string SourceZiFuRefine = "ZiFuRefine";
	private const string SourceLingBaoUpgrade = "LingBaoUpgrade";
	private const string SourceJieLinGrant = "JieLinGrant";
	private const string SourceJieLinUpgrade = "JieLinUpgrade";
	private const string SourceDaoTaiXianQi = "DaoTaiXianQi";

	internal static string NormalizeGeneratedDescription(
		Actor actor,
		string name,
		string daoTu,
		string className,
		string kind,
		string role,
		string source,
		string storedDescription)
	{
		if (XjLingZhuangNameLibrary.TryResolveEquipmentTypeFromKind(kind, out _))
		{
			return BuildGeneratedDescription(actor, name, daoTu, className, kind, role, source);
		}

		string normalizedRole = XjFaBaoCatalog.NormalizeRole(kind, role);
		if (string.Equals(normalizedRole, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			|| string.Equals(normalizedRole, XjFaBaoCatalog.RoleSupport, StringComparison.Ordinal)
			|| string.Equals(source, SourceJinDan, StringComparison.Ordinal)
			|| string.Equals(source, SourceZiFuRefine, StringComparison.Ordinal)
			|| string.Equals(source, SourceLingBaoUpgrade, StringComparison.Ordinal)
			|| string.Equals(source, SourceJieLinGrant, StringComparison.Ordinal)
			|| string.Equals(source, SourceJieLinUpgrade, StringComparison.Ordinal)
			|| string.Equals(source, SourceDaoTaiXianQi, StringComparison.Ordinal)
			|| XjFaBaoCatalog.IsXianQi(className))
		{
			return BuildGeneratedDescription(actor, name, daoTu, className, kind, normalizedRole, source);
		}

		return storedDescription ?? string.Empty;
	}

	internal static string BuildGeneratedDescription(
		Actor actor,
		string name,
		string daoTu,
		string className,
		string kind,
		string role,
		string source)
	{
		string dao = string.IsNullOrWhiteSpace(daoTu) ? "玄鉴道炁" : daoTu.Trim();
		string type = string.IsNullOrWhiteSpace(kind) ? "法宝" : kind.Trim();
		if (XjFaBaoCatalog.IsXianQi(className)
			|| string.Equals(source, SourceDaoTaiXianQi, StringComparison.Ordinal))
		{
			return "此器原为修士本命金丹法宝，入道胎后以" + dao
				+ "真意五百年一温养，于千一机缘中蜕尽器骨而生仙机。器名、器魂与本命归属不改，位格升为仙器；"
				+ type + "中仙机自运，威能远胜寻常金丹法宝；仙器最多承载七条词条、单条上限五成，攻击本命仙器原生伤害五万，防御型仙器原生生命二百万。";
		}
		if (XjLingZhuangNameLibrary.TryResolveEquipmentTypeFromKind(type, out EquipmentType equipmentType))
		{
			string slotLabel = XjLingZhuangNameLibrary.ResolveSlotLabel(equipmentType);
			string phrase = XjLingZhuangNameLibrary.ResolveFunctionPhrase(equipmentType);
			return dao + "灵机入" + slotLabel + "，" + phrase + "。";
		}

		string normalizedRole = XjFaBaoCatalog.NormalizeRole(type, role);
		if (string.Equals(normalizedRole, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal))
		{
			return dao + "真意锻入" + type + "中，锋光敛而不露，临敌一发，可摧坚破阵。";
		}

		bool isYuYiOrigin = XuanJianVNext.Systems.HighRealm.XjXuanJianShenTongSpecials.IsYuYiXian(actor);
		bool isJieLinOrigin = string.Equals(source, SourceJieLinGrant, StringComparison.Ordinal)
			|| string.Equals(source, SourceJieLinUpgrade, StringComparison.Ordinal)
			|| (XuanJianVNext.Systems.HighRealm.XjXuanJianShenTongSpecials.IsJieLinXian(actor)
				&& (string.Equals(source, SourceJinDan, StringComparison.Ordinal)
					|| string.Equals(source, SourceLingBaoUpgrade, StringComparison.Ordinal)));
		if (isYuYiOrigin) return dao + "日精凝入" + type + "中，赤景随身流转，映照灵台与命宫。";
		return isJieLinOrigin
			? dao + "月华凝入" + type + "中，清辉随身流转，映照灵台与命宫。"
			: dao + "真意栖于" + type + "中，灵应随身流转，温养道基与神意。";
	}

}
