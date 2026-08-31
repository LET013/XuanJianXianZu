using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.Data.HighRealm;

internal static class XjShenTongTypeIds
{
	internal const string Unknown = "unknown";
	internal const string Ming = "命";
	internal const string Shen = "身";
	internal const string Shu = "术";
	internal const string ShenMing = "身命";
	internal const string MuShen = "木身";
}

/// <summary>
/// 独立扩展道途的上位神通定义。并古既有专属名称沿用原目录；
/// 渊照等后世空证道途在这里登记完整五神通池。所有运行补足项都固定归属本道途，
/// 避免高境五神通生成因目录短缺而降格或借入无关道途。
/// </summary>
internal readonly struct XjZiJinUpperShenTongDefinition
{
	internal readonly string DaoTuRootId;
	internal readonly string DaoTuDisplayName;
	internal readonly string ShenTongId;
	internal readonly string DisplayName;
	internal readonly string TypeId;
	internal readonly string[] Aliases;
	internal readonly string ConceptSummary;
	internal readonly bool IsSourceListed;

	internal XjZiJinUpperShenTongDefinition(
		string daoTuRootId,
		string daoTuDisplayName,
		string shenTongId,
		string displayName,
		string typeId,
		string[] aliases,
		string conceptSummary,
		bool isSourceListed)
	{
		DaoTuRootId = daoTuRootId ?? string.Empty;
		DaoTuDisplayName = daoTuDisplayName ?? string.Empty;
		ShenTongId = shenTongId ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		TypeId = typeId ?? XjShenTongTypeIds.Unknown;
		Aliases = aliases ?? Array.Empty<string>();
		ConceptSummary = conceptSummary ?? string.Empty;
		IsSourceListed = isSourceListed;
	}
}

internal static class XjZiJinUpperShenTongCatalog
{
	internal static readonly XjZiJinUpperShenTongDefinition[] Definitions =
	{
		// 三巫·鸺葵
		D(XjDaoTuRootIds.XiaoKui, "鸺葵", "xk_xiao_zhu_li", "枭逐狸", XjShenTongTypeIds.Ming, "神通能留下可转法力的分身并隐匿自身"),
		DA(XjDaoTuRootIds.XiaoKui, "鸺葵", "xk_yan_gui_yin", "飓鬼阴", XjShenTongTypeIds.Shen, new[] { "魇鬼阴" }, "喜狂风黑暗、驾风迅疾，并可召出乌黑分身、使自身化为黑气"),
		F(XjDaoTuRootIds.XiaoKui, "鸺葵", "xk_xiao_ye_xing", "枭夜行", XjShenTongTypeIds.Shu, "借夜色与枭影移形匿迹"),
		F(XjDaoTuRootIds.XiaoKui, "鸺葵", "xk_li_fen_ming", "狸分命", XjShenTongTypeIds.Ming, "以狸影承接部分性命与法力"),
		F(XjDaoTuRootIds.XiaoKui, "鸺葵", "xk_yin_yu_cang", "阴羽藏", XjShenTongTypeIds.Shen, "敛气入阴羽，隔绝追索"),

		// 三巫·上巫
		D(XjDaoTuRootIds.ShangWu, "上巫", "sw_huai_yin_gui", "槐荫鬼", XjShenTongTypeIds.Ming, "眼中生出幽暗，偏向隐匿与鬼阴"),
		D(XjDaoTuRootIds.ShangWu, "上巫", "sw_ying_di_wang", "应帝王", XjShenTongTypeIds.Ming, "雾道、诡法与混淆莫测"),
		D(XjDaoTuRootIds.ShangWu, "上巫", "sw_yin_min_xue", "饮民血", XjShenTongTypeIds.Shu, "以玄红法光压制躯体与血肉"),
		D(XjDaoTuRootIds.ShangWu, "上巫", "sw_wu_cha_wo", "勿查我", XjShenTongTypeIds.Shu, "隐去身形气息、避查与隔绝探知"),
		D(XjDaoTuRootIds.ShangWu, "上巫", "sw_di_wu_zhu", "地巫祝", XjShenTongTypeIds.Shen, "以巫祝道法加持自身"),

		// 三巫·玉真
		DA(XjDaoTuRootIds.YuZhen, "玉真", "yz_yu_zhong_ren", "玉中人", XjShenTongTypeIds.ShenMing, new[] { "玉庭将", "庭中卫" }, "玉光环身、身命合一并亲和玉器变化"),
		D(XjDaoTuRootIds.YuZhen, "玉真", "yz_qing_yu_ya", "青玉崖", XjShenTongTypeIds.Shu, "青玉光彩照境、穿透与玉性凝聚"),
		D(XjDaoTuRootIds.YuZhen, "玉真", "yz_dao_he_zhen", "道合真", XjShenTongTypeIds.Shu, "真与虚合、加持器物并统摄法器"),
		DA(XjDaoTuRootIds.YuZhen, "玉真", "yz_wen_dao_jin", "间道锦", XjShenTongTypeIds.Shen, new[] { "问道锦" }, "化青玉崖玉珠为白锦，锦带披风加身并以白锦障目"),
		D(XjDaoTuRootIds.YuZhen, "玉真", "yz_bai_yu_pan", "白玉盘", XjShenTongTypeIds.Unknown, "神通唤出白玉盘，偏向太阴属性"),

		// 二祝·衡祝
		D(XjDaoTuRootIds.HengZhu, "衡祝", "hz_dian_yang_hu", "殿阳虎", XjShenTongTypeIds.Shen, "赤虎玄光、避险脱身、破疫与血色攻伐"),
		D(XjDaoTuRootIds.HengZhu, "衡祝", "hz_hu_liang_zai", "斛量灾", XjShenTongTypeIds.Unknown, "衡祝灾火与巫祝法门"),
		DA(XjDaoTuRootIds.HengZhu, "衡祝", "hz_man_jin_huang", "满垠㷐", XjShenTongTypeIds.Unknown, new[] { "满烬煌" }, "血火幽域、血火烧伤、定身并掩盖玄机"),
		F(XjDaoTuRootIds.HengZhu, "衡祝", "hz_zhu_heng_huo", "祝衡火", XjShenTongTypeIds.Shu, "以祝仪衡定祭火盛衰"),
		F(XjDaoTuRootIds.HengZhu, "衡祝", "hz_zai_cheng_ming", "灾秤命", XjShenTongTypeIds.Ming, "称量灾厄并转入本命祭火"),

		// 二祝·青宣：玄羊子抬举四道仙基成神通的特殊五神通结构。
		D(XjDaoTuRootIds.QingXuan, "青宣", "qx_xuan_yang_zi", "玄羊子", XjShenTongTypeIds.Unknown, "青宣紫金特殊结构的核心神通"),
		D(XjDaoTuRootIds.QingXuan, "青宣", "qx_fu_qing_shan", "伏青山", XjShenTongTypeIds.Unknown, "由玄羊子抬举升格的青宣上位神通"),
		D(XjDaoTuRootIds.QingXuan, "青宣", "qx_qing_xuan_yue", "青宣岳", XjShenTongTypeIds.Unknown, "由玄羊子抬举升格的青宣上位神通"),
		D(XjDaoTuRootIds.QingXuan, "青宣", "qx_shang_yan_shen", "上岩神", XjShenTongTypeIds.Unknown, "由玄羊子抬举升格的青宣上位神通"),
		D(XjDaoTuRootIds.QingXuan, "青宣", "qx_guan_di_ming", "观地冥", XjShenTongTypeIds.Unknown, "由玄羊子抬举升格的青宣上位神通"),

		// 全丹
		DA(XjDaoTuRootIds.QuanDan, "全丹", "qd_hun_qian_hua", "浥铅华", XjShenTongTypeIds.Ming, new[] { "混铅华" }, "望气可见眉心赤团，偏向性命与丹性观察"),
		DA(XjDaoTuRootIds.QuanDan, "全丹", "qd_zhi_yang_yi", "制飬宜", XjShenTongTypeIds.Shu, new[] { "制养宜", "秘白汞", "秘白录" }, "养玄之道与用器之德；秘白汞一脉偏隐匿、法体与伤势恢复"),
		DA(XjDaoTuRootIds.QuanDan, "全丹", "qd_hou_shen_shu", "候神殊", XjShenTongTypeIds.Unknown, new[] { "侯神殊" }, "铅汞神尸、物性孕育、炼化用器、避死延生与多类变化"),
		D(XjDaoTuRootIds.QuanDan, "全丹", "qd_jin_shu_xu", "金书序", XjShenTongTypeIds.Unknown, "神通兼通他道，随情景变化"),
		F(XjDaoTuRootIds.QuanDan, "全丹", "qd_dan_he_shen", "丹合神", XjShenTongTypeIds.ShenMing, "丹性与身命合真，内外圆成"),

		// 执孛（修越）
		D(XjDaoTuRootIds.ZhiBo, "执孛", "zb_xing_du_qian", "形渡阡", XjShenTongTypeIds.Shen, "固定修为与灵识自知的幻身"),
		D(XjDaoTuRootIds.ZhiBo, "执孛", "zb_xiang_li_jue", "相离绝", XjShenTongTypeIds.Unknown, "阴阳交分与中宫阴主"),
		DA(XjDaoTuRootIds.ZhiBo, "执孛", "zb_shu_dong_bo", "僭劻勷", XjShenTongTypeIds.Unknown, new[] { "倏动勃" }, "变动之术，借王威破阵杀敌、分隔四方、斩断气息并可与庚金配合"),
		F(XjDaoTuRootIds.ZhiBo, "执孛", "zb_bo_xing_dun", "孛形遁", XjShenTongTypeIds.Shen, "化本命形相为遁光"),
		F(XjDaoTuRootIds.ZhiBo, "执孛", "zb_yi_xiang_shen", "移相身", XjShenTongTypeIds.ShenMing, "移易形相并保存性命根本"),

		// 司天
		DA(XjDaoTuRootIds.SiTian, "司天", "st_ting_yao_chen", "听醒辰", XjShenTongTypeIds.Ming, new[] { "听曜辰" }, "查言语、听诸事并辅助阵道"),
		D(XjDaoTuRootIds.SiTian, "司天", "st_shen_bu_xu", "神布序", XjShenTongTypeIds.Ming, "观望天象、推衍时序并调度术法"),
		DA(XjDaoTuRootIds.SiTian, "司天", "st_dou_heng_xuan", "斗衡玄", XjShenTongTypeIds.Unknown, new[] { "居南衡" }, "镇守方位、定移安灵与天衡秩序"),
		F(XjDaoTuRootIds.SiTian, "司天", "st_si_chen_ming", "司辰命", XjShenTongTypeIds.Ming, "执掌辰序并护持本命时数"),
		F(XjDaoTuRootIds.SiTian, "司天", "st_bu_tian_yi", "布天仪", XjShenTongTypeIds.Shu, "以天仪排布星序与方位"),

		// 都卫
		D(XjDaoTuRootIds.DuWei, "都卫", "dw_jiang_hun_wen", "降魂闻", XjShenTongTypeIds.Ming, "影身放出黑光，偏向镇魂与遮蔽"),
		DA(XjDaoTuRootIds.DuWei, "都卫", "dw_dong_li_shan", "东羽山", XjShenTongTypeIds.Shu, new[] { "东利山" }, "飞山重白之气下沉，凝滞灵机、固化太虚并可破阵光"),
		DA(XjDaoTuRootIds.DuWei, "都卫", "dw_xi_tian_jing", "西天塬", XjShenTongTypeIds.Shu, new[] { "西天璟" }, "令太虚陡绝，落雪起淡青长风，隔断灵机而不渡太虚"),
		DA(XjDaoTuRootIds.DuWei, "都卫", "dw_nan_min_shui", "南惆水", XjShenTongTypeIds.MuShen, new[] { "南悯水" }, "身化灰色魔水，紫水遮蔽太虚、镇压封锁并具虚化之能"),
		D(XjDaoTuRootIds.DuWei, "都卫", "dw_bei_mo_ting", "北漠庭", XjShenTongTypeIds.Shu, "北方地域与方镇术法，具体效果待原著补足"),

		// 空证·渊照：不以直观见物，而循天地留影返见其真。五门分别承沉月、照真、返景、涵真与无波。
		F(XjDaoTuRootIds.YuanZhao, "渊照", "yz_yue_chen_yuan", "月沉渊", XjShenTongTypeIds.Shu, "使月影沉入一方渊水，扰乱锁敌与外察，使敌难辨真实方位"),
		F(XjDaoTuRootIds.YuanZhao, "渊照", "yz_zhao_wu_shen", "照无身", XjShenTongTypeIds.Ming, "不观其身而观天地留痕，照见真身、破隐匿、假身与替身"),
		F(XjDaoTuRootIds.YuanZhao, "渊照", "yz_hui_lan_jian", "回澜鉴", XjShenTongTypeIds.Shu, "截取近前法痕成景，以较低位格再映一次，不作时间倒流"),
		F(XjDaoTuRootIds.YuanZhao, "渊照", "yz_ying_gui_zhen", "影归真", XjShenTongTypeIds.ShenMing, "以自身留影校正本真，稳住身命、削弱错乱与外力侵扰"),
		F(XjDaoTuRootIds.YuanZhao, "渊照", "yz_yi_hong_ji", "一泓寂", XjShenTongTypeIds.Shu, "水若无波则万象可鉴，以寂静界域压制位移、爆发与连续变法"),

		// 空证·虹霞：五神通均采用原著名目，不再以运行效果临时拼接名称。
		D(XjDaoTuRootIds.HongXia, "虹霞", "hx_chang_xia_wu", "长霞雾", XjShenTongTypeIds.Shen, "凝六彩虹雾，修成后形貌愈显华贵"),
		F(XjDaoTuRootIds.HongXia, "虹霞", "hx_xia_dun", "蝃蝀东", XjShenTongTypeIds.Shen, "取《诗经》“蝃蝀在东，莫敢指之”之意；蝃蝀代指虹霞，身与霞象相应"),
		F(XjDaoTuRootIds.HongXia, "虹霞", "hx_hua_xia", "正漱朝", XjShenTongTypeIds.Shen, "取“餐六气而饮沆瀣兮，漱正阳而含朝霞”之意，纳朝霞入身以养形神"),
		F(XjDaoTuRootIds.HongXia, "虹霞", "hx_luo_qi", "余成绮", XjShenTongTypeIds.Shu, "晚霞散作锦绮：可乱心神、惑视听，亦可散作霞绮以避灾遁难"),
		// 保留旧 Id，确保已有存档的第五门神通仍能被正确读取；名称改为“混含德”。
		// “混”取虹霓为阴阳之精，“德”承含德致清平：失德则虹现为天忌，含德则虹霓不出、贼星不行。
		F(XjDaoTuRootIds.HongXia, "虹霞", "hx_ge_tian", "混含德", XjShenTongTypeIds.Ming, "混阴阳之精，含清平之德；德失则虹霓为天忌，德全则虹霓不出、贼星不行，可息灾异、和阴阳")
	};

	private static readonly Dictionary<string, string[]> LowerNamesByRootId =
		new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		[XjDaoTuRootIds.XiaoKui] = new[] { "枭羽符", "狸影纸", "夜行箓", "分命符", "藏形翎" },
		[XjDaoTuRootIds.ShangWu] = new[] { "阴槐枝", "雾隐身", "血祝纹", "避查符", "地祷坛" },
		[XjDaoTuRootIds.YuZhen] = new[] { "青玉符", "白玉屑", "问道简", "合真佩", "庭卫印" },
		[XjDaoTuRootIds.HengZhu] = new[] { "祭火种", "量灾筹", "殿虎纹", "守阳灯", "余烬符" },
		[XjDaoTuRootIds.QingXuan] = new[] { "下阳元", "青岩符", "伏山气", "观冥砂", "羊脂露" },
		[XjDaoTuRootIds.QuanDan] = new[] { "铅华露", "养宜散", "候神丸", "金书页", "内炉火" },
		[XjDaoTuRootIds.ZhiBo] = new[] { "化形蜕", "离相符", "倏动影", "渡阡步", "孛光尘" },
		[XjDaoTuRootIds.SiTian] = new[] { "曜辰铃", "布序简", "斗衡筹", "南衡表", "司时漏" },
		[XjDaoTuRootIds.DuWei] = new[] { "镇魂符", "东山石", "西璟砂", "南水滴", "北庭旗" },
		[XjDaoTuRootIds.YuanZhao] = new[] { "沉月纹", "照影符", "返景痕", "涵真珠", "无波鉴" },
		[XjDaoTuRootIds.HongXia] = new[] { "霞雾纹", "虹霓符", "朝霞露", "绮霞帛", "含德印" }
	};

	internal static string[] GetNamesByDaoTu(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId)) return Array.Empty<string>();
		List<string> result = new List<string>();
		AppendNames(result, rootId, sourceListed: true);
		AppendNames(result, rootId, sourceListed: false);
		return result.ToArray();
	}

	internal static string[] GetSourceListedNamesByDaoTu(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId)) return Array.Empty<string>();
		List<string> result = new List<string>();
		AppendNames(result, rootId, sourceListed: true);
		return result.ToArray();
	}

	internal static string[] GetSupplementNamesByDaoTu(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId)) return Array.Empty<string>();
		List<string> result = new List<string>();
		AppendNames(result, rootId, sourceListed: false);
		return result.ToArray();
	}

	private static void AppendNames(List<string> result, string rootId, bool sourceListed)
	{
		for (int i = 0; i < Definitions.Length; i++)
		{
			XjZiJinUpperShenTongDefinition definition = Definitions[i];
			if (definition.IsSourceListed == sourceListed
				&& string.Equals(definition.DaoTuRootId, rootId, StringComparison.Ordinal)
				&& !string.IsNullOrWhiteSpace(definition.DisplayName)
				&& !result.Contains(definition.DisplayName))
			{
				result.Add(definition.DisplayName);
			}
		}
	}

	internal static string[] GetLowerNamesByDaoTu(string daoTuOrRoot)
	{
		if (!XjDaoTuCatalog.TryResolveRootId(daoTuOrRoot, out string rootId)
			|| !LowerNamesByRootId.TryGetValue(rootId, out string[] names))
		{
			return Array.Empty<string>();
		}
		string[] result = new string[names.Length];
		Array.Copy(names, result, names.Length);
		return result;
	}

	internal static string[] GetDaoTuDisplayNames()
	{
		List<string> result = new List<string>();
		for (int i = 0; i < Definitions.Length; i++)
		{
			string name = Definitions[i].DaoTuDisplayName;
			if (name.Length > 0 && !result.Contains(name)) result.Add(name);
		}
		return result.ToArray();
	}

	internal static bool TryGet(string shenTongNameOrId, out XjZiJinUpperShenTongDefinition definition)
	{
		string normalized = (shenTongNameOrId ?? string.Empty).Trim();
		for (int i = 0; i < Definitions.Length; i++)
		{
			XjZiJinUpperShenTongDefinition candidate = Definitions[i];
			if (string.Equals(candidate.ShenTongId, normalized, StringComparison.Ordinal)
				|| string.Equals(candidate.DisplayName, normalized, StringComparison.Ordinal))
			{
				definition = candidate;
				return true;
			}
			for (int aliasIndex = 0; aliasIndex < candidate.Aliases.Length; aliasIndex++)
			{
				if (string.Equals(candidate.Aliases[aliasIndex], normalized, StringComparison.Ordinal))
				{
					definition = candidate;
					return true;
				}
			}
		}
		definition = default;
		return false;
	}

	private static XjZiJinUpperShenTongDefinition D(
		string rootId, string daoTu, string id, string name, string type, string summary)
	{
		return new XjZiJinUpperShenTongDefinition(rootId, daoTu, id, name, type, Array.Empty<string>(), summary, true);
	}


	private static XjZiJinUpperShenTongDefinition F(
		string rootId, string daoTu, string id, string name, string type, string summary)
	{
		return new XjZiJinUpperShenTongDefinition(rootId, daoTu, id, name, type, Array.Empty<string>(), summary, false);
	}

	private static XjZiJinUpperShenTongDefinition DA(
		string rootId, string daoTu, string id, string name, string type, string[] aliases, string summary)
	{
		return new XjZiJinUpperShenTongDefinition(rootId, daoTu, id, name, type, aliases, summary, true);
	}
}
