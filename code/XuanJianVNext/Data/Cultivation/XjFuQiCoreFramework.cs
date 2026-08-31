using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Cultivation;

internal static class XjFuQiCoreTypeIds
{
	internal const string DaoQiShenMiao = "dao_qi_shenmiao";
	internal const string NatalTalisman = "natal_talisman";
	internal const string XuanYangZi = "xuan_yang_zi";
	internal const string RitualFire = "ritual_fire";
	internal const string NatalElixir = "natal_elixir";
	internal const string MutableForm = "mutable_form";
	internal const string CelestialOrder = "celestial_order";
	internal const string DirectionalGuard = "directional_guard";
	internal const string SwordIntent = "sword_intent";
	internal const string YuanZhaoMirror = "yuan_zhao_mirror";
}

internal static class XjFuQiHandlerIds
{
	internal const string GenericDaoQi = "generic_dao_qi";
	internal const string NatalTalisman = "natal_talisman";
	internal const string QingXuan = "qing_xuan";
	internal const string HengZhu = "heng_zhu";
	internal const string QuanDan = "quan_dan";
	internal const string ZhiBo = "zhi_bo";
	internal const string SiTian = "si_tian";
	internal const string DuWei = "du_wei";
	internal const string Sword = "sword";
}

internal readonly struct XjFuQiCoreDefinition
{
	internal readonly string DaoTuRootId;
	internal readonly string CoreTypeId;
	internal readonly string CoreId;
	internal readonly string DisplayName;
	internal readonly string MethodName;
	internal readonly string MethodEffect;
	internal readonly string HandlerId;
	internal readonly bool GameplayImplemented;

	internal XjFuQiCoreDefinition(
		string daoTuRootId,
		string coreTypeId,
		string coreId,
		string displayName,
		string methodName,
		string methodEffect,
		string handlerId,
		bool gameplayImplemented)
	{
		DaoTuRootId = daoTuRootId ?? string.Empty;
		CoreTypeId = coreTypeId ?? string.Empty;
		CoreId = coreId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		MethodName = methodName ?? string.Empty;
		MethodEffect = methodEffect ?? string.Empty;
		HandlerId = handlerId ?? string.Empty;
		GameplayImplemented = gameplayImplemented;
	}
}

/// <summary>
/// 服气公共境界只保存性命双修阶段，具体道途通过本命核心路由差异化。
/// 常见九途、并古九途、长庚与渊照均已具备实际入门Handler；黄冠以后统一复用公共性命双修状态机。
/// 执孛、司天、都卫使用同一轻量并古核心Handler，以数据配置区分本命形相、本命天序与本命方镇。
/// </summary>
internal static class XjFuQiCoreCatalog
{
	/// <summary>
	/// 常见道途按具体支脉使用独立功法名，避免所有服气法门都退化为“某某服气养性章”。
	/// 根类模板仅作为未来新增支脉或旧档异常数据的兜底。
	/// </summary>
	private static readonly Dictionary<string, string> SpecializedMethodNames =
		new Dictionary<string, string>(StringComparer.Ordinal)
		{
			["太阴"] = "《广寒含真养命经》",
			["少阴"] = "《幽荧少阴炼性篇》",
			["厥阴"] = "《风木敛生养命录》",
			["太阳"] = "《大日曜真养形经》",
			["少阳"] = "《东华升阳炼性篇》",
			["明阳"] = "《重光昭明养神录》",
			["玄雷"] = "《玄霆洗髓炼性经》",
			["霄雷"] = "《神霄引真服气录》",
			["元雷"] = "《玉枢元雷振命篇》",
			["兑金"] = "《兑泽鸣金养性诀》",
			["逍金"] = "《逍锋游气炼形篇》",
			["齐金"] = "《肃齐敛锋真章》",
			["库金"] = "《白藏库府养命经》",
			["庚金"] = "《太白庚辛炼魄录》",
			["角木"] = "《角宿青华荣命篇》",
			["正木"] = "《正木扶生养性经》",
			["集木"] = "《众木会春服气录》",
			["更木"] = "《荣枯更生炼命诀》",
			["保木"] = "《青华保生含真章》",
			["坎水"] = "《坎宫玄冥归息经》",
			["渌水"] = "《碧渌澄神养性篇》",
			["合水"] = "《百川归壑合真录》",
			["府水"] = "《重渊水府藏精章》",
			["牝水"] = "《玄牝柔水养命诀》",
			["离火"] = "《朱明离宫炼神经》",
			["灴火"] = "《赤灴焕阳养形篇》",
			["并火"] = "《双焰合真炼性录》",
			["真火"] = "《丹景真火明神章》",
			["牡火"] = "《景焰刚阳炼命诀》",
			["艮土"] = "《艮岳止息镇形经》",
			["戊土"] = "《黄庭戊己养性篇》",
			["归土"] = "《归藏返真炼命录》",
			["宝土"] = "《宝壤承真养形章》",
			["宣土"] = "《坤厚宣和固命诀》",
			["清炁"] = "《清虚澄神服气经》",
			["紫炁"] = "《紫宸绛霄养性篇》",
			["真炁"] = "《纯一真元炼命录》",
			["邃炁"] = "《邃冥幽渊藏真章》",
			["寒炁"] = "《沆砀玄冰敛息诀》",
			["晞炁"] = "《晞明晨光养形经》",
			["瑞炁"] = "《嘉祥瑞应含真篇》",
			["煞炁"] = "《肃刑煞轮炼魄录》",
			["华炁"] = "《华藏昭采养神章》",
			["谪炁"] = "《尘外谪仙归命诀》",
			["上仪"] = "《天衡上仪清真经》",
			["下仪"] = "《幽冥下仪藏性篇》"
		};

	// 0.9.5首版并古法门名称。新版改名后仍需识别这些已进入角色、家族仓库和宗门藏经阁的物品。
	private static readonly HashSet<string> LegacyMethodNames =
		new HashSet<string>(StringComparer.Ordinal)
		{
			"《鸺葵命符养性篇》",
			"《上巫命符养性篇》",
			"《玉真命符养性篇》",
			"《衡祝守火养命篇》",
			"《玄羊子养性篇》",
			"《全丹内炉养性篇》",
			"《执孛化形养性篇》",
			"《司天布序养性篇》",
			"《都卫方镇养性篇》"
		};

	internal static readonly XjFuQiCoreDefinition[] Definitions =
	{
		F(XjDaoTuRootIds.SanYin, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_san_yin", "三阴道气", "《玄阴服气养性篇》", "纳三阴道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.SanYang, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_san_yang", "三阳道气", "《太阳服气养性篇》", "纳三阳道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.SanLei, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_san_lei", "三雷道气", "《玉枢服气养性篇》", "纳三雷道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.JinDe, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_jin_de", "金德道气", "《白藏服气养性章》", "纳金德道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.MuDe, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_mu_de", "木德道气", "《青华服气养性章》", "纳木德道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.ShuiDe, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_shui_de", "水德道气", "《玄冥服气养性章》", "纳水德道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.HuoDe, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_huo_de", "火德道气", "《朱明服气养性章》", "纳火德道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.TuDe, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_tu_de", "土德道气", "《黄庭服气养性章》", "纳土德道气炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.ShiErQi, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_shi_er_qi", "十二炁道气", "《列炁归元养性篇》", "依十二炁次序归元炼命，循本命核心推进感气、求身与性命合炼", XjFuQiHandlerIds.GenericDaoQi, true),

		F(XjDaoTuRootIds.XiaoKui, XjFuQiCoreTypeIds.NatalTalisman, "fuqi_natal_talisman_xiao_kui", "鸺葵本命符箓", "《鸺葵宿符命书》", "结鸺葵本命符箓，以符命积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.NatalTalisman, true),
		F(XjDaoTuRootIds.ShangWu, XjFuQiCoreTypeIds.NatalTalisman, "fuqi_natal_talisman_shang_wu", "上巫本命符箓", "《上巫祝命玄箓》", "结上巫本命符箓，以符命积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.NatalTalisman, true),
		F(XjDaoTuRootIds.YuZhen, XjFuQiCoreTypeIds.NatalTalisman, "fuqi_natal_talisman_yu_zhen", "玉真本命符箓", "《玉真金阙符经》", "结玉真本命符箓，以符命积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.NatalTalisman, true),
		F(XjDaoTuRootIds.HengZhu, XjFuQiCoreTypeIds.RitualFire, "fuqi_core_heng_zhu", "衡祝本命祭火", "《衡祝守燎真章》", "守养本命祭火，以祭火积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.HengZhu, true),
		F(XjDaoTuRootIds.QingXuan, XjFuQiCoreTypeIds.XuanYangZi, "xuan_yang_zi", "玄羊子", "《玄羊含生录》", "孕化玄羊子，以生机积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.QingXuan, true),
		F(XjDaoTuRootIds.QuanDan, XjFuQiCoreTypeIds.NatalElixir, "fuqi_core_quan_dan", "全丹本命丹性", "《全丹九转内炉经》", "以内炉温养本命丹性，以丹性积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.QuanDan, true),
		F(XjDaoTuRootIds.ZhiBo, XjFuQiCoreTypeIds.MutableForm, "fuqi_core_zhi_bo", "执孛本命形相", "《执孛蜕形真录》", "炼成本命形相，以化形积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.ZhiBo, true),
		F(XjDaoTuRootIds.SiTian, XjFuQiCoreTypeIds.CelestialOrder, "fuqi_core_si_tian", "司天本命天序", "《司天布象玄经》", "排布本命天序，以天序积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.SiTian, true),
		F(XjDaoTuRootIds.DuWei, XjFuQiCoreTypeIds.DirectionalGuard, "fuqi_core_du_wei", "都卫本命方镇", "《都卫方镇命图》", "镇守本命方位，以方镇积累推进感气炼命及后续性命合炼", XjFuQiHandlerIds.DuWei, true),

		F(XjDaoTuRootIds.LongGeng, XjFuQiCoreTypeIds.SwordIntent, "yang_qing_ming", "养青冥", "《养青冥》", "观剑养气、积蓄剑气并参悟剑意，继而循服气性命状态机求证", XjFuQiHandlerIds.Sword, true),
		F(XjDaoTuRootIds.YuanZhao, XjFuQiCoreTypeIds.YuanZhaoMirror, "fuqi_core_yuan_zhao", "水月玄鉴", "《水月涵真养性经》", "纳渊照先天之气，于心湖养成一轮水月玄鉴；以映照返见本真，以潜渊涵养性命，循同一核心推进黄冠、真人与真君羽士。", XjFuQiHandlerIds.GenericDaoQi, true),
		F(XjDaoTuRootIds.HongXia, XjFuQiCoreTypeIds.DaoQiShenMiao, "fuqi_core_hong_xia", "虹霞道气", "《虹霞服气养性篇》", "纳霞光入性命，循虹霞道气推进感气、黄冠、真人与真君羽士。", XjFuQiHandlerIds.GenericDaoQi, true)
	};

	private static readonly Dictionary<string, XjFuQiCoreDefinition> DefinitionByRootId =
		BuildDefinitionByRootId();

	internal static bool TryGetByRootId(string rootId, out XjFuQiCoreDefinition definition)
	{
		string normalized = (rootId ?? string.Empty).Trim();
		return DefinitionByRootId.TryGetValue(normalized, out definition);
	}

	/// <summary>
	/// 同时识别当前专属功法名与0.9.5旧档中的根类功法名。
	/// 该判断只用于物品说明和旧档路径推断，不以“养性篇”等泛词误判紫府金丹功法。
	/// </summary>
	internal static bool IsKnownMethodName(string methodName)
	{
		string normalized = (methodName ?? string.Empty).Trim();
		if (normalized.Length == 0) return false;

		for (int i = 0; i < Definitions.Length; i++)
		{
			if (string.Equals(Definitions[i].MethodName, normalized, StringComparison.Ordinal))
			{
				return true;
			}
		}

		foreach (string specializedName in SpecializedMethodNames.Values)
		{
			if (string.Equals(specializedName, normalized, StringComparison.Ordinal))
			{
				return true;
			}
		}
		if (LegacyMethodNames.Contains(normalized)) return true;

		// 兼容首版由兜底模板生成且已经入库的服气法门；只接受明确带有
		// “服气养性”的名称，避免把普通“养性篇/养命篇”错判为服气功法。
		return normalized.Contains("服气养性", StringComparison.Ordinal);
	}

	internal static bool TryResolveByDaoTu(string daoTuOrRoot, out XjFuQiCoreDefinition definition)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId))
		{
			definition = default;
			return false;
		}
		if (!TryGetByRootId(rootId, out XjFuQiCoreDefinition rootDefinition))
		{
			definition = default;
			return false;
		}
		definition = SpecializeForDaoTu(in rootDefinition, daoTuOrRoot);
		return true;
	}

	internal static XjFuQiCoreDefinition SpecializeForDaoTu(
		in XjFuQiCoreDefinition definition,
		string daoTu)
	{
		string normalized = (daoTu ?? string.Empty).Trim();
		if (!string.Equals(definition.CoreTypeId, XjFuQiCoreTypeIds.DaoQiShenMiao, StringComparison.Ordinal)
			|| string.IsNullOrWhiteSpace(normalized)
			|| !XjDaoTuCatalog.TryResolve(normalized, out XjDaoTuDefinition resolved)
			|| !string.Equals(resolved.RootId, definition.DaoTuRootId, StringComparison.Ordinal)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalized, out _))
		{
			return definition;
		}

		return new XjFuQiCoreDefinition(
			definition.DaoTuRootId,
			definition.CoreTypeId,
			definition.CoreId + "_" + normalized,
			normalized + "道气",
			BuildSpecializedMethodName(definition.DaoTuRootId, normalized),
			BuildSpecializedMethodEffect(definition.DaoTuRootId, normalized),
			definition.HandlerId,
			definition.GameplayImplemented);
	}

	private static string BuildSpecializedMethodName(string rootId, string daoTu)
	{
		string stem = (daoTu ?? string.Empty).Trim();
		if (stem.Length == 0) return "《无名养性篇》";
		if (SpecializedMethodNames.TryGetValue(stem, out string methodName))
		{
			return methodName;
		}
		return rootId switch
		{
			XjDaoTuRootIds.SanYin => "《" + stem + "藏阴养命篇》",
			XjDaoTuRootIds.SanYang => "《" + stem + "曜真养形经》",
			XjDaoTuRootIds.SanLei => "《" + stem + "玉枢炼性录》",
			XjDaoTuRootIds.JinDe => "《" + stem + "白藏鸣金诀》",
			XjDaoTuRootIds.MuDe => "《" + stem + "青华荣命篇》",
			XjDaoTuRootIds.ShuiDe => "《" + stem + "玄冥归息录》",
			XjDaoTuRootIds.HuoDe => "《" + stem + "朱明炼神章》",
			XjDaoTuRootIds.TuDe => "《" + stem + "黄庭镇形经》",
			XjDaoTuRootIds.ShiErQi => "《" + stem + "列炁归元篇》",
			_ => "《" + stem + "性命修持篇》"
		};
	}

	private static string BuildSpecializedMethodEffect(string rootId, string daoTu)
	{
		string stem = (daoTu ?? string.Empty).Trim();
		string action = rootId switch
		{
			XjDaoTuRootIds.SanYin => "敛藏阴炁、温养命火",
			XjDaoTuRootIds.SanYang => "昭明阳炁、炼形养神",
			XjDaoTuRootIds.SanLei => "引雷洗髓、振发性灵",
			XjDaoTuRootIds.JinDe => "鸣金锻魄、收束真息",
			XjDaoTuRootIds.MuDe => "荣发生机、滋养性命",
			XjDaoTuRootIds.ShuiDe => "归息藏精、涵养玄命",
			XjDaoTuRootIds.HuoDe => "炼火明神、焕发命机",
			XjDaoTuRootIds.TuDe => "镇形固命、安定真性",
			XjDaoTuRootIds.ShiErQi => "列炁归元、调合性命",
			_ => "服气炼命、性命双修"
		};
		return "以" + stem + "道气为本命根基，" + action + "；一部本命法门贯穿黄冠、真人与真君羽士。";
	}

	private static XjFuQiCoreDefinition F(
		string rootId,
		string coreType,
		string coreId,
		string displayName,
		string methodName,
		string methodEffect,
		string handler,
		bool implemented)
	{
		return new XjFuQiCoreDefinition(
			rootId,
			coreType,
			coreId,
			displayName,
			methodName,
			methodEffect,
			handler,
			implemented);
	}

	private static Dictionary<string, XjFuQiCoreDefinition> BuildDefinitionByRootId()
	{
		Dictionary<string, XjFuQiCoreDefinition> result =
			new Dictionary<string, XjFuQiCoreDefinition>(Definitions.Length, StringComparer.Ordinal);
		for (int i = 0; i < Definitions.Length; i++)
		{
			result[Definitions[i].DaoTuRootId] = Definitions[i];
		}
		return result;
	}
}
