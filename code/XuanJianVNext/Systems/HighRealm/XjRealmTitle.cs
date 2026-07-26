using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.WeaponArt;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjRealmTitleNameLibrary
{
	private static readonly string[] CommonTitleWords =
	{
		"元", "洞", "长", "上", "素", "灵", "景", "清", "司", "修", "道", "真", "化", "衍", "虚", "极",
		"宸", "枢", "蕴", "玄", "觉", "神", "妙", "恒", "净", "琅", "瑶", "瑾", "瑜", "璇", "珩", "璟"
	};

	private static readonly string[] CommonDoubleTitleWords =
	{
		"太玄", "洞真", "玉清", "上元", "玄都", "青冥", "太微", "元景",
		"妙有", "无极", "高上", "清虚", "玉宸",
		"洞玄", "清微", "含真", "抱一", "冲和", "太初", "太易", "太始",
		"玉京", "寂照", "玄景", "昭明", "道枢", "元和"
	};

	private static readonly string[] JieLinFirstTitleWords =
	{
		"广寒", "湖月", "夜光", "太素", "月府", "素魄", "玄珠", "清辉",
		"幽荧", "冰轮", "桂魄", "蟾宫", "望舒", "玄阴", "寒蟾", "凝霜"
	};

	private static readonly string[] JieLinSecondTitleWords =
	{
		"藏真", "照影", "凝章", "授玄", "清虚", "幽元", "含光", "再圆",
		"秋水", "寂照", "霜华", "月华", "蕴真", "澄辉", "抱月", "守素"
	};

	private static readonly string[] RealmTitleForbiddenWords =
	{
		"胎息", "炼气", "筑基", "紫府", "金丹", "神丹", "结璘"
	};

	private static readonly string[] JinTitleWords =
	{
		"金", "锋", "钧", "鉴", "钺", "铄", "锐", "庚", "白", "肃", "斩", "戈", "铸", "鸣", "绝", "销"
	};

	private static readonly string[] MuTitleWords =
	{
		"木", "桐", "苓", "桑", "槐", "榕", "椿", "苍", "荣", "枯", "华", "芷", "荫", "乙", "森", "桢"
	};

	private static readonly string[] ShuiTitleWords =
	{
		"水", "霈", "霂", "汀", "兰", "泽", "淼", "溟", "霜", "溪", "渡", "涓", "浥", "瀚", "渊", "江", "河"
	};

	private static readonly string[] HuoTitleWords =
	{
		"火", "煌", "翎", "炎", "焱", "烬", "丹", "离", "炽", "焚", "炳", "焕", "耀", "赤", "焰", "曦"
	};

	private static readonly string[] TuTitleWords =
	{
		"土", "岳", "峰", "岭", "壑", "尘", "重", "浊", "坤", "镇", "圭", "壤", "岿", "崇", "岸", "垣"
	};

	private static readonly string[] YinTitleWords =
	{
		"月", "蟾", "桂", "琼", "冰", "寒", "霜", "素", "辉", "魄", "璘", "华", "凝", "寂", "幻", "幽", "烟", "泠", "涵", "弱", "微"
	};

	private static readonly string[] YangTitleWords =
	{
		"日", "阳", "曜", "羲", "炎", "煌", "赤", "乌", "昊", "伏", "炽", "烈", "耀", "辉", "光", "盛", "尊", "焕", "明", "升", "炼", "灼", "乾", "皇", "昭", "威", "浩", "泰", "刚"
	};

	private static readonly string[] QiTitleWords =
	{
		"炁", "气", "岚", "罡", "霞", "风", "云", "冥", "冲", "寥", "穹", "寰", "空", "渺", "幽", "浮"
	};


	private sealed class DaoTuTitleProfile
	{
		internal readonly string[] SingleWords;
		internal readonly string[] DoubleWords;
		internal readonly string[] Markers;

		internal DaoTuTitleProfile(string[] singleWords, string[] doubleWords, string[] markers = null)
		{
			SingleWords = singleWords ?? Array.Empty<string>();
			DoubleWords = doubleWords ?? Array.Empty<string>();
			Markers = markers ?? DoubleWords;
		}
	}


	// 十二炁是十二条独立道途，不共享可直接辨识为其他分支的字词。
	// 旧版把“上仪、下仪”等同置于通用池，导致寒炁等角色会得到错位尊号。
	private static readonly string[] QingQiTitleWords = { "清", "澄", "虚", "泠", "玄", "霁", "微", "渟" };
	private static readonly string[] ZiQiTitleWords = { "紫", "宸", "霄", "绛", "曜", "玄", "霞", "璇" };
	private static readonly string[] ZhenQiTitleWords = { "真", "元", "纯", "太", "玄", "湛", "冲", "凝" };
	private static readonly string[] SuiQiTitleWords = { "邃", "幽", "渊", "冥", "玄", "寂", "深", "潜" };
	private static readonly string[] HanQiTitleWords = { "寒", "霜", "凛", "冰", "雪", "冽", "凝", "沆" };
	private static readonly string[] XiQiTitleWords = { "晞", "曦", "晨", "昕", "晓", "光", "昭", "明" };
	private static readonly string[] RuiQiTitleWords = { "瑞", "嘉", "祥", "麟", "玉", "华", "灵", "庆" };
	private static readonly string[] ShaQiTitleWords = { "煞", "戾", "厉", "刑", "肃", "断", "劫", "峻" };
	private static readonly string[] HuaQiTitleWords = { "华", "章", "文", "采", "璧", "辉", "绮", "昭" };
	private static readonly string[] ZheQiTitleWords = { "谪", "尘", "孤", "客", "天", "外", "玄", "寥" };
	private static readonly string[] ShangYiTitleWords = { "上", "仪", "天", "衡", "清", "升", "玄", "阙" };
	private static readonly string[] XiaYiTitleWords = { "下", "仪", "幽", "冥", "黄", "泉", "忘", "川" };

	private static readonly string[] QingQiDoubleTitleWords = { "清虚", "澄明", "泠然", "玄清", "霁野", "渟云" };
	private static readonly string[] ZiQiDoubleTitleWords = { "紫宸", "绛霄", "紫曜", "玄霞", "璇霄", "宸光" };
	private static readonly string[] ZhenQiDoubleTitleWords = { "真元", "纯一", "太真", "湛然", "玄凝", "冲元" };
	private static readonly string[] SuiQiDoubleTitleWords = { "邃冥", "幽渊", "玄寂", "深潜", "冥府", "渊玄" };
	private static readonly string[] HanQiDoubleTitleWords = { "寒世", "入清", "松上", "祢水", "沆砀", "凛冬", "玄冰", "凝霜" };
	private static readonly string[] XiQiDoubleTitleWords = { "晞明", "晨光", "昕晓", "昭晨", "晞曜", "曦和" };
	private static readonly string[] RuiQiDoubleTitleWords = { "瑞应", "嘉祥", "玉麟", "灵庆", "瑞华", "嘉玉" };
	private static readonly string[] ShaQiDoubleTitleWords = { "煞轮", "肃刑", "厉劫", "峻断", "戾天", "煞威" };
	private static readonly string[] HuaQiDoubleTitleWords = { "华藏", "文璧", "昭采", "绮章", "华辉", "璧光" };
	private static readonly string[] ZheQiDoubleTitleWords = { "谪仙", "孤天", "尘外", "玄客", "天寥", "谪尘" };
	private static readonly string[] ShangYiDoubleTitleWords = { "上仪", "天衡", "清升", "玄阙", "上清", "仪天" };
	private static readonly string[] XiaYiDoubleTitleWords = { "下仪", "幽冥", "黄泉", "忘川", "轮回", "阴司" };

	// 尊号字库按“具体道途”隔离，而不是按三阴、三阳、五德等大组共享。
	// 任何包含兄弟道途专属标识的旧尊号，都会在角色年度同步时重新生成。
	private static readonly Dictionary<string, DaoTuTitleProfile> ExactDaoTuTitleProfiles =
		new Dictionary<string, DaoTuTitleProfile>(StringComparer.Ordinal)
	{
		["太阴"] = Profile(new[] { "月", "桂", "蟾", "魄", "冰", "寒", "素", "霜" }, new[] { "太阴", "广寒", "月府", "望舒", "冰轮", "桂魄", "湖月", "夜光", "素魄", "清辉" }),
		["少阴"] = Profile(new[] { "少", "幽", "荧", "微", "夜", "玄", "阴", "寂" }, new[] { "少阴", "少微", "幽荧", "玄月", "夜阴", "阴华", "幽月", "玄阴" }),
		["厥阴"] = Profile(new[] { "厥", "风", "木", "冥", "幽", "长", "阴", "青" }, new[] { "厥阴", "风木", "冥风", "幽风", "长风", "阴木" }),
		["太阳"] = Profile(new[] { "日", "阳", "曜", "羲", "乌", "昊", "烈", "乾" }, new[] { "太阳", "金乌", "羲和", "曜灵", "日轮", "昊景", "扶桑", "纯阳" }, new[] { "太阳", "金乌", "羲和", "曜灵", "日轮", "昊景", "纯阳" }),
		["少阳"] = Profile(new[] { "少", "阳", "晨", "升", "煦", "暄", "东", "明" }, new[] { "少阳", "少昊", "晨阳", "东阳", "煦日", "暄明", "东君" }),
		["明阳"] = Profile(new[] { "明", "阳", "昭", "光", "朗", "耀", "晖", "景" }, new[] { "明阳", "昭明", "重光", "朗曜", "明景", "阳辉", "重明" }, new[] { "明阳", "昭明", "朗曜", "明景", "阳辉", "重明" }),
		["玄雷"] = Profile(new[] { "玄", "冥", "幽", "雷", "霆", "震", "殛", "肃" }, new[] { "玄雷", "玄霆", "冥雷", "幽霆", "玄震", "雷玄", "震霆", "雷泽" }),
		["霄雷"] = Profile(new[] { "霄", "天", "云", "神", "雷", "霆", "曜", "震" }, new[] { "霄雷", "九霄", "神霄", "霄霆", "云霆", "天霄" }),
		["元雷"] = Profile(new[] { "元", "枢", "雷", "霆", "鼓", "府", "司", "震" }, new[] { "元雷", "玉枢", "元霆", "雷府", "霆司", "天鼓" }),

		["兑金"] = Profile(new[] { "兑", "泽", "西", "悦", "锋", "刃", "白", "金" }, new[] { "兑金", "兑泽", "西兑", "泽锋", "兑刃", "悦金", "西极" }),
		["逍金"] = Profile(new[] { "逍", "游", "凌", "逸", "锋", "刃", "金", "轻" }, new[] { "逍金", "逍锋", "游金", "凌锋", "逸刃", "逍游", "金庭" }),
		["齐金"] = Profile(new[] { "齐", "整", "肃", "正", "钧", "锋", "刃", "金" }, new[] { "齐金", "齐锋", "肃齐", "整刃", "齐钧", "正金", "肃英" }),
		["库金"] = Profile(new[] { "库", "藏", "府", "镇", "金", "锋", "钧", "鉴" }, new[] { "库金", "金库", "藏锋", "库藏", "府金", "镇库", "金阙", "蓐收" }),
		["庚金"] = Profile(new[] { "庚", "辛", "白", "锐", "肃", "金", "锋", "斩" }, new[] { "庚金", "太白", "庚辛", "白藏", "庚锋", "肃金", "白帝" }),

		["角木"] = Profile(new[] { "角", "苍", "青", "荣", "木", "生", "枝", "春" }, new[] { "角木", "苍角", "木角", "角荣", "青角", "角生", "青帝", "东华" }),
		["正木"] = Profile(new[] { "正", "青", "木", "荣", "直", "春", "华", "生" }, new[] { "正木", "正青", "木正", "青正", "正荣", "春正", "青阳", "春元" }),
		["集木"] = Profile(new[] { "集", "林", "众", "森", "会", "木", "青", "荣" }, new[] { "集木", "林集", "众木", "集青", "木会", "森集", "建木", "苍灵" }),
		["更木"] = Profile(new[] { "更", "岁", "枯", "荣", "木", "生", "新", "春" }, new[] { "更木", "荣枯", "木更", "岁木", "更生", "枯荣", "震巽" }),
		["保木"] = Profile(new[] { "保", "护", "守", "长", "木", "生", "青", "荣" }, new[] { "保木", "长生", "护木", "保生", "青护", "木守", "句芒", "扶桑", "青华" }, new[] { "保木", "长生", "护木", "保生", "青护", "木守", "句芒", "青华" }),

		["坎水"] = Profile(new[] { "坎", "玄", "北", "渊", "水", "溟", "泉", "寒" }, new[] { "坎水", "坎宫", "玄坎", "北坎", "坎渊", "水坎", "玄冥", "天一", "壬癸", "北溟" }),
		["渌水"] = Profile(new[] { "渌", "清", "碧", "澄", "水", "波", "泉", "溪" }, new[] { "渌水", "清渌", "渌波", "碧水", "澄渌", "渌泉", "溟波" }),
		["合水"] = Profile(new[] { "合", "川", "流", "归", "水", "汇", "海", "江" }, new[] { "合水", "百川", "归流", "合流", "川合", "众水", "沧渊" }),
		["府水"] = Profile(new[] { "府", "泉", "海", "渊", "玄", "水", "藏", "溟" }, new[] { "府水", "水府", "玄府", "泉府", "府渊", "海府", "重渊" }),
		["牝水"] = Profile(new[] { "牝", "柔", "阴", "泉", "水", "渊", "玄", "波" }, new[] { "牝水", "玄牝", "柔水", "阴泉", "牝渊", "柔波", "寒泉" }),

		["离火"] = Profile(new[] { "离", "明", "朱", "南", "火", "景", "炎", "曜" }, new[] { "离火", "离宫", "南离", "朱明", "离明", "火离", "赤帝", "祝融" }),
		["灴火"] = Profile(new[] { "灴", "赤", "阳", "炎", "火", "光", "明", "炽" }, new[] { "灴火", "灴阳", "赤灴", "灴炎", "灴明", "灴光", "炎天", "赤曜" }),
		["并火"] = Profile(new[] { "并", "双", "合", "炎", "火", "焰", "明", "炽" }, new[] { "并火", "双焰", "并炎", "合焰", "炎并", "并明", "重光" }, new[] { "并火", "双焰", "并炎", "合焰", "炎并", "并明" }),
		["真火"] = Profile(new[] { "真", "纯", "丹", "炎", "火", "焰", "明", "赤" }, new[] { "真火", "真炎", "纯火", "真焰", "真丹", "丹真", "丹景", "阳燧" }),
		["牡火"] = Profile(new[] { "牡", "阳", "烈", "刚", "火", "炎", "炽", "赤" }, new[] { "牡火", "阳火", "牡阳", "烈牡", "牡炎", "刚火", "景焰", "焕赫" }),

		["艮土"] = Profile(new[] { "艮", "山", "止", "岳", "土", "岭", "峰", "镇" }, new[] { "艮土", "艮山", "止岳", "艮岳", "山艮", "止山", "镇岳", "山岳" }),
		["戊土"] = Profile(new[] { "戊", "中", "央", "土", "岳", "坤", "厚", "镇" }, new[] { "戊土", "中央", "戊己", "中土", "戊岳", "土戊", "黄庭", "中宫" }),
		["归土"] = Profile(new[] { "归", "返", "藏", "土", "壤", "岳", "山", "坤" }, new[] { "归土", "归藏", "土归", "返壤", "归岳", "归山", "后土" }),
		["宝土"] = Profile(new[] { "宝", "玉", "珍", "藏", "土", "壤", "岳", "圭" }, new[] { "宝土", "宝壤", "玉土", "藏珍", "宝岳", "珍壤", "厚载", "承天" }),
		["宣土"] = Profile(new[] { "宣", "黄", "坤", "土", "壤", "岳", "章", "垣" }, new[] { "宣土", "土宣", "宣岳", "宣壤", "黄宣", "宣坤", "坤厚", "玄垣" }),

		["清炁"] = Profile(QingQiTitleWords, new[] { "清炁", "清虚", "澄明", "泠然", "玄清", "霁野", "渟云" }),
		["紫炁"] = Profile(ZiQiTitleWords, new[] { "紫炁", "紫宸", "绛霄", "紫曜", "玄霞", "璇霄", "宸光" }),
		["真炁"] = Profile(ZhenQiTitleWords, new[] { "真炁", "真元", "纯一", "太真", "湛然", "玄凝", "冲元" }),
		["邃炁"] = Profile(SuiQiTitleWords, new[] { "邃炁", "邃冥", "幽渊", "玄寂", "深潜", "渊玄", "冥府" }),
		["寒炁"] = Profile(HanQiTitleWords, new[] { "寒炁", "寒世", "沆砀", "凛冬", "玄冰", "凝霜", "入清", "松上", "祢水" }),
		["晞炁"] = Profile(XiQiTitleWords, new[] { "晞炁", "晞明", "晨光", "昕晓", "昭晨", "曦和", "晞曜" }),
		["瑞炁"] = Profile(RuiQiTitleWords, new[] { "瑞炁", "瑞应", "嘉祥", "玉麟", "灵庆", "瑞华", "嘉玉" }),
		["煞炁"] = Profile(ShaQiTitleWords, new[] { "煞炁", "煞轮", "肃刑", "厉劫", "峻断", "戾天", "煞威" }),
		["华炁"] = Profile(HuaQiTitleWords, new[] { "华炁", "华藏", "文璧", "昭采", "绮章", "璧光", "华辉" }),
		["谪炁"] = Profile(ZheQiTitleWords, new[] { "谪炁", "谪仙", "孤天", "尘外", "玄客", "天寥", "谪尘" }),
		["上仪"] = Profile(ShangYiTitleWords, new[] { "上仪", "天衡", "清升", "玄阙", "上清", "仪天" }),
		["下仪"] = Profile(XiaYiTitleWords, new[] { "下仪", "幽冥", "黄泉", "忘川", "轮回", "阴司" }),
		["青宣"] = Profile(new[] { "青", "宣", "素", "律", "衡", "简", "正", "章" }, new[] { "青宣", "素德", "宣真", "青律", "清衡", "正仪", "玄章", "素元", "青简", "宣玄", "素霞" })
	};

	private static readonly string[] ExactDaoTuNames =
	{
		"太阴", "少阴", "厥阴", "太阳", "少阳", "明阳", "玄雷", "霄雷", "元雷",
		"兑金", "逍金", "齐金", "库金", "庚金", "角木", "正木", "集木", "更木", "保木",
		"坎水", "渌水", "合水", "府水", "牝水", "离火", "灴火", "并火", "真火", "牡火",
		"艮土", "戊土", "归土", "宝土", "宣土", "清炁", "紫炁", "真炁", "邃炁", "寒炁",
		"晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪", "青宣"
	};

	// 通用高境词必须避开所有具体道途专属标识，防止通用前缀反向触发跨途修复。
	private static readonly string[] SafeCommonDoubleTitleWords =
	{
		"太玄", "洞真", "玉清", "上元", "玄都", "青冥", "太微", "元景",
		"妙有", "无极", "高上", "玉宸", "洞玄", "含真", "抱一", "冲和",
		"太初", "太易", "太始", "玉京", "寂照", "玄景", "道枢", "元和"
	};

	private static DaoTuTitleProfile Profile(string[] singleWords, string[] doubleWords)
	{
		return new DaoTuTitleProfile(singleWords, doubleWords);
	}

	private static DaoTuTitleProfile Profile(string[] singleWords, string[] doubleWords, string[] markers)
	{
		return new DaoTuTitleProfile(singleWords, doubleWords, markers);
	}

	private static bool TryGetExactTitleProfile(string daoTu, out DaoTuTitleProfile profile)
	{
		return ExactDaoTuTitleProfiles.TryGetValue(daoTu ?? string.Empty, out profile);
	}


	// 只用于修复旧版已生成的跨道途尊号；生成时仍使用各自完整词池。
	private static readonly string[] JinDeTitleMarkers = { "白帝", "西极", "金庭", "太白", "庚辛", "兑泽", "肃英", "金阙", "蓐收", "白藏" };
	private static readonly string[] MuDeTitleMarkers = { "青帝", "东华", "句芒", "建木", "苍灵", "扶桑", "青阳", "荣枯", "春元", "震巽", "青华" };
	private static readonly string[] ShuiDeTitleMarkers = { "玄冥", "天一", "坎宫", "沧渊", "溟波", "寒泉", "重渊", "水府", "壬癸", "北溟" };
	private static readonly string[] HuoDeTitleMarkers = { "赤帝", "南离", "祝融", "朱明", "炎天", "丹景", "离宫", "重光", "景焰", "焕赫", "赤曜", "阳燧" };
	private static readonly string[] TuDeTitleMarkers = { "黄庭", "后土", "坤厚", "中宫", "镇岳", "厚载", "承天", "玄垣", "山岳", "戊己", "中央" };
	private static readonly string[] SuDeTitleMarkers = { "青宣", "素德", "宣真", "青律", "清衡", "正仪", "玄章", "素元", "青简", "宣玄", "素霞" };
	private static readonly string[] SanYinTitleMarkers = { "广寒", "太阴", "少阴", "厥阴", "幽月", "玄阴", "湖月", "夜光", "素魄", "清辉", "幽荧" };
	private static readonly string[] SanYangTitleMarkers = { "太阳", "少阳", "明阳", "扶桑", "金乌", "纯阳", "东君", "曜灵", "昊景", "日轮", "羲和", "重明" };
	private static readonly string[] SanLeiTitleMarkers = { "玄雷", "霄雷", "元雷", "玉枢", "九霄", "震霆", "天鼓", "云霆", "神霄", "雷府", "霆司", "雷泽" };
	private static readonly string[][] ShiErQiTitlePools =
	{
		QingQiDoubleTitleWords, ZiQiDoubleTitleWords, ZhenQiDoubleTitleWords, SuiQiDoubleTitleWords,
		HanQiDoubleTitleWords, XiQiDoubleTitleWords, RuiQiDoubleTitleWords, ShaQiDoubleTitleWords,
		HuaQiDoubleTitleWords, ZheQiDoubleTitleWords, ShangYiDoubleTitleWords, XiaYiDoubleTitleWords
	};
	private static readonly string[][] DaoTuTitleMarkerGroups =
	{
		JinDeTitleMarkers, MuDeTitleMarkers, ShuiDeTitleMarkers, HuoDeTitleMarkers, TuDeTitleMarkers,
		SuDeTitleMarkers, SanYinTitleMarkers, SanYangTitleMarkers, SanLeiTitleMarkers
	};

	private static readonly string[] LeiTitleWords =
	{
		"雷", "霆", "震", "霹", "雳", "掣", "闪", "耀", "鸣", "殛", "霄", "劫", "威", "罚", "镇", "御"
	};

	internal static string GenerateTitle(string realmId, string daoTu, long actorId)
	{
		if (realmId == XuanJianVNext.Data.Rules.XjRealmIds.ZhuJi)
		{
			return string.Empty;
		}

		if (realmId == XuanJianVNext.Data.Rules.XjRealmIds.ZiFu)
		{
			return GenerateZiFuTitle(daoTu, actorId);
		}

		if (realmId == XuanJianVNext.Data.Rules.XjRealmIds.JinDan)
		{
			return GenerateJinDanTitle(daoTu, actorId);
		}
		if (realmId == XuanJianVNext.Data.Rules.XjRealmIds.ShenDan)
		{
			return GenerateShenDanTitle(daoTu, actorId);
		}

		return string.Empty;
	}

	internal static string GenerateZiFuTitle(string daoTu, long actorId)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		string[] singleWords = ResolveSingleTitleWords(normalizedDaoTu);
		for (int attempt = 0; attempt < 12; attempt++)
		{
			string first = Pick(singleWords, actorId + attempt * 17L, normalizedDaoTu + "|zifu|0|" + attempt);
			string second = Pick(CommonTitleWords, actorId + 1L + attempt * 19L, normalizedDaoTu + "|zifu|1|" + attempt);
			if (string.Equals(first, second, StringComparison.Ordinal))
			{
				second = Pick(CommonTitleWords, actorId + 2L + attempt * 23L, normalizedDaoTu + "|zifu|2|" + attempt);
			}
			string candidate = first + second + "真人";
			if (!ContainsRealmTitleWord(candidate) && !HasForeignDaoTuTitleMarker(candidate, normalizedDaoTu))
			{
				return candidate;
			}
		}

		// 所有组合都碰到兄弟道途专属词时，退回明确的本支道途尊号。
		return normalizedDaoTu + "真人";
	}

	internal static string GenerateJinDanTitle(string daoTu, long actorId)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return string.Empty;
		}

		string suffix = PickJinDanSuffix(actorId, normalizedDaoTu);
		string[] daoTuWords = ResolveDoubleTitleWords(normalizedDaoTu);
		for (int attempt = 0; attempt < 16; attempt++)
		{
			string first = Pick(SafeCommonDoubleTitleWords, actorId + 1L + attempt * 29L, normalizedDaoTu + "|jindan|0|" + attempt);
			string second = PickDistinctComponent(SafeCommonDoubleTitleWords, first, actorId + 2L + attempt * 31L, normalizedDaoTu + "|jindan|1|" + attempt);
			string daoTuWord = PickDistinctComponent(daoTuWords, second, actorId + attempt * 37L, normalizedDaoTu + "|jindan|dao|" + attempt);
			string candidate = first + second + daoTuWord + suffix;
			if (!ContainsRealmTitleWord(candidate) && !HasForeignDaoTuTitleMarker(candidate, normalizedDaoTu))
			{
				return candidate;
			}
		}

		return "清微" + normalizedDaoTu + suffix;
	}

	internal static string GenerateShenDanTitle(string daoTu, long actorId)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		string saltBase = string.IsNullOrWhiteSpace(normalizedDaoTu) ? "shendan" : normalizedDaoTu;
		for (int attempt = 0; attempt < 8; attempt++)
		{
			string first = Pick(SafeCommonDoubleTitleWords, actorId + 11L + attempt * 41L, saltBase + "|shendan|0|" + attempt);
			string candidate = first + "侍神";
			if (!ContainsRealmTitleWord(candidate)
				&& (string.IsNullOrWhiteSpace(normalizedDaoTu) || !HasForeignDaoTuTitleMarker(candidate, normalizedDaoTu)))
			{
				return candidate;
			}
		}
		return "道枢侍神";
	}

	internal static string RepairForeignTitle(string daoTu, string title, string fallback)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		string value = (title ?? string.Empty).Trim();
		string replacement = (fallback ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(value)) return replacement;
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)) return value;
		return ContainsRealmTitleWord(value) || HasForeignDaoTuTitleMarker(value, normalizedDaoTu)
			? replacement
			: value;
	}

	internal static string GenerateJieLinTitle(long actorId)
	{
		string first = Pick(JieLinFirstTitleWords, actorId, "taiyin|jielin|0");
		string second = PickDistinctComponent(JieLinSecondTitleWords, first, actorId + 1L, "taiyin|jielin|1");
		string suffix = PickJieLinSuffix(actorId);
		string title = first + second + suffix;
		return ContainsRealmTitleWord(title) ? "广寒清辉" + suffix : title;
	}

	private static string PickJieLinSuffix(long actorId)
	{
		// 结璘仙尊号沿用高境君号体系，仍保持确定性生成。
		long mixedSeed = XjDeterministicHash.StableHash(actorId.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "|taiyin|jielin|suffix|v1");
		int roll = XjDeterministicHash.PositiveIndex(mixedSeed, "jielin_suffix_weight", 100);
		if (roll < 30) return "玄君";
		if (roll < 60) return "神君";
		if (roll < 90) return "真君";
		return "飞君";
	}

	private static string PickJinDanSuffix(long actorId, string daoTu)
	{
		// 加权随机: 30%玄君 + 30%神君 + 30%真君 + 10%飞君
		long mixedSeed = XjDeterministicHash.StableHash(actorId.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "|" + (daoTu ?? string.Empty)
			+ "|jindan|suffix|v2");
		int roll = XjDeterministicHash.PositiveIndex(mixedSeed, "jindan_suffix_weight", 100);
		if (roll < 30) return "玄君";
		if (roll < 60) return "神君";
		if (roll < 90) return "真君";
		return "飞君";
	}

	private static string[] ResolveSingleTitleWords(string daoTu)
	{
		return TryGetExactTitleProfile(daoTu, out DaoTuTitleProfile profile)
			? profile.SingleWords
			: QiTitleWords;
	}

	private static string[] ResolveDoubleTitleWords(string daoTu)
	{
		return TryGetExactTitleProfile(daoTu, out DaoTuTitleProfile profile)
			? profile.DoubleWords
			: SafeCommonDoubleTitleWords;
	}

	private static string[] ResolveShiErQiSingleTitleWords(string daoTu)
	{
		switch (daoTu)
		{
			case "清炁": return QingQiTitleWords;
			case "紫炁": return ZiQiTitleWords;
			case "真炁": return ZhenQiTitleWords;
			case "邃炁": return SuiQiTitleWords;
			case "寒炁": return HanQiTitleWords;
			case "晞炁": return XiQiTitleWords;
			case "瑞炁": return RuiQiTitleWords;
			case "煞炁": return ShaQiTitleWords;
			case "华炁": return HuaQiTitleWords;
			case "谪炁": return ZheQiTitleWords;
			case "上仪": return ShangYiTitleWords;
			case "下仪": return XiaYiTitleWords;
			default: return QiTitleWords;
		}
	}

	private static string[] ResolveShiErQiDoubleTitleWords(string daoTu)
	{
		switch (daoTu)
		{
			case "清炁": return QingQiDoubleTitleWords;
			case "紫炁": return ZiQiDoubleTitleWords;
			case "真炁": return ZhenQiDoubleTitleWords;
			case "邃炁": return SuiQiDoubleTitleWords;
			case "寒炁": return HanQiDoubleTitleWords;
			case "晞炁": return XiQiDoubleTitleWords;
			case "瑞炁": return RuiQiDoubleTitleWords;
			case "煞炁": return ShaQiDoubleTitleWords;
			case "华炁": return HuaQiDoubleTitleWords;
			case "谪炁": return ZheQiDoubleTitleWords;
			case "上仪": return ShangYiDoubleTitleWords;
			case "下仪": return XiaYiDoubleTitleWords;
			default: return CommonDoubleTitleWords;
		}
	}

	internal static bool AuditExactDaoTuTitleProfiles(out string error)
	{
		error = string.Empty;
		if (ExactDaoTuTitleProfiles.Count != ExactDaoTuNames.Length)
		{
			error = "尊号字库数量不一致：应有" + ExactDaoTuNames.Length + "条，实际" + ExactDaoTuTitleProfiles.Count + "条。";
			return false;
		}
		for (int i = 0; i < ExactDaoTuNames.Length; i++)
		{
			string daoTu = ExactDaoTuNames[i];
			if (!ExactDaoTuTitleProfiles.TryGetValue(daoTu, out DaoTuTitleProfile profile)
				|| profile.SingleWords.Length == 0
				|| profile.DoubleWords.Length == 0)
			{
				error = "尊号字库缺少具体道途：" + daoTu;
				return false;
			}
		}
		foreach (KeyValuePair<string, DaoTuTitleProfile> left in ExactDaoTuTitleProfiles)
		{
			foreach (KeyValuePair<string, DaoTuTitleProfile> right in ExactDaoTuTitleProfiles)
			{
				if (string.CompareOrdinal(left.Key, right.Key) >= 0) continue;
				for (int i = 0; i < left.Value.Markers.Length; i++)
				{
					for (int j = 0; j < right.Value.Markers.Length; j++)
					{
						if (string.Equals(left.Value.Markers[i], right.Value.Markers[j], StringComparison.Ordinal))
						{
							error = left.Key + "与" + right.Key + "尊号标识重复：" + left.Value.Markers[i];
							return false;
						}
					}
				}
			}
		}
		return true;
	}

	internal static bool ContainsForbiddenRealmWord(string title)
	{
		return ContainsRealmTitleWord(title);
	}

	internal static bool HasForeignDaoTuTitleMarker(string title, string daoTu)
	{
		string text = title ?? string.Empty;
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		foreach (KeyValuePair<string, DaoTuTitleProfile> pair in ExactDaoTuTitleProfiles)
		{
			if (string.Equals(pair.Key, normalizedDaoTu, StringComparison.Ordinal))
			{
				continue;
			}
			if (ContainsAny(text, pair.Value.Markers))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsOwnDaoTuMarkerGroup(string daoTu, string[] markers)
	{
		return (IsJinDe(daoTu) && object.ReferenceEquals(markers, JinDeTitleMarkers))
			|| (IsMuDe(daoTu) && object.ReferenceEquals(markers, MuDeTitleMarkers))
			|| (IsShuiDe(daoTu) && object.ReferenceEquals(markers, ShuiDeTitleMarkers))
			|| (IsHuoDe(daoTu) && object.ReferenceEquals(markers, HuoDeTitleMarkers))
			|| (IsTuDe(daoTu) && object.ReferenceEquals(markers, TuDeTitleMarkers))
			|| (IsSuDe(daoTu) && object.ReferenceEquals(markers, SuDeTitleMarkers))
			|| (IsSanYin(daoTu) && object.ReferenceEquals(markers, SanYinTitleMarkers))
			|| (IsSanYang(daoTu) && object.ReferenceEquals(markers, SanYangTitleMarkers))
			|| (IsSanLei(daoTu) && object.ReferenceEquals(markers, SanLeiTitleMarkers));
	}

	private static bool ContainsAny(string text, string[] values)
	{
		for (int i = 0; i < values.Length; i++)
		{
			if (text.IndexOf(values[i], StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool ContainsRealmTitleWord(string title)
	{
		string text = title ?? string.Empty;
		for (int i = 0; i < RealmTitleForbiddenWords.Length; i++)
		{
			if (text.IndexOf(RealmTitleForbiddenWords[i], StringComparison.Ordinal) >= 0)
			{
				return true;
			}
		}

		return false;
	}

	private static string NormalizeDaoTu(string daoTu)
	{
		string text = (daoTu ?? string.Empty).Trim();
		return !string.IsNullOrWhiteSpace(text)
			&& XuanJianVNext.Systems.Cultivation.XjDaoTuVisibleTraitCatalog.TryResolveTraitId(text, out _)
				? text
				: string.Empty;
	}

	private static string Pick(string[] values, long actorId, string salt)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}

		return values[XjDeterministicHash.PositiveIndex(actorId, salt, values.Length)];
	}

	private static string PickDistinctComponent(string[] values, string current, long actorId, string salt)
	{
		if (values == null || values.Length == 0)
		{
			return string.Empty;
		}

		int start = XjDeterministicHash.PositiveIndex(actorId, salt, values.Length);
		for (int offset = 0; offset < values.Length; offset++)
		{
			string candidate = values[(start + offset) % values.Length];
			if (!IsRedundantBoundary(current, candidate))
			{
				return candidate;
			}
		}

		return values[start];
	}

	private static bool IsRedundantBoundary(string left, string right)
	{
		string first = left ?? string.Empty;
		string second = right ?? string.Empty;
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
		{
			return false;
		}

		return string.Equals(first, second, StringComparison.Ordinal)
			|| first.EndsWith(second, StringComparison.Ordinal)
			|| second.StartsWith(first, StringComparison.Ordinal)
			|| first[first.Length - 1] == second[0];
	}

	private static string PickDifferent(string[] values, string current, long actorId, string salt)
	{
		string picked = Pick(values, actorId, salt);
		if (values == null || values.Length <= 1 || !string.Equals(picked, current, StringComparison.Ordinal))
		{
			return picked;
		}

		return values[(XjDeterministicHash.PositiveIndex(actorId, salt, values.Length) + 1) % values.Length];
	}

	private static bool IsSanYin(string daoTu) => daoTu == "太阴" || daoTu == "少阴" || daoTu == "厥阴";
	private static bool IsSanYang(string daoTu) => daoTu == "太阳" || daoTu == "少阳" || daoTu == "明阳";
	private static bool IsSanLei(string daoTu) => daoTu == "玄雷" || daoTu == "霄雷" || daoTu == "元雷";
	private static bool IsShiErQi(string daoTu) => daoTu is "清炁" or "紫炁" or "真炁" or "邃炁" or "寒炁" or "晞炁"
		or "瑞炁" or "煞炁" or "华炁" or "谪炁" or "上仪" or "下仪";
	private static bool IsJinDe(string daoTu) => daoTu == "兑金" || daoTu == "逍金" || daoTu == "齐金" || daoTu == "库金" || daoTu == "庚金";
	private static bool IsMuDe(string daoTu) => daoTu == "角木" || daoTu == "正木" || daoTu == "集木" || daoTu == "更木" || daoTu == "保木";
	private static bool IsShuiDe(string daoTu) => daoTu == "坎水" || daoTu == "渌水" || daoTu == "合水" || daoTu == "府水" || daoTu == "牝水";
	private static bool IsHuoDe(string daoTu) => daoTu == "离火" || daoTu == "灴火" || daoTu == "并火" || daoTu == "真火" || daoTu == "牡火";
	private static bool IsTuDe(string daoTu) => daoTu == "艮土" || daoTu == "戊土" || daoTu == "归土" || daoTu == "宝土" || daoTu == "宣土";
	private static bool IsSuDe(string daoTu) => daoTu == "青宣";
}


internal static class XjRealmTitleApplyService
{
	internal static void ApplyOnPromotion(Actor actor, string realmId, string daoTu)
	{
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(realmId)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return;
		}

		if (XjLongShuSystem.TryApplyRealmTitle(actor, realmId, daoTu))
		{
			return;
		}

		string baseName = ResolveBaseName(actor);
		string title = XjRealmTitleNameLibrary.GenerateTitle(realmId, daoTu, GetActorId(actor));
		string realmDisplay = GetRealmDisplay(realmId);
		string fullName = string.IsNullOrWhiteSpace(title)
			? baseName + "-" + realmDisplay
			: title + "·" + baseName + "-" + realmDisplay;

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = fullName;
	}

	internal static void EnsureCurrentRealmProjection(Actor actor)
	{
		if (actor?.data == null) return;
		string storedRealm = XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string stored)
			? XjRealmHelper.NormalizeId(stored) : string.Empty;
		string traitRealm = XjRealmHelper.NormalizeId(XjRealmHelper.GetTraitSnapshotForRouter(actor));
		string realmId = XjRealmHelper.GetOrder(traitRealm) > XjRealmHelper.GetOrder(storedRealm) ? traitRealm : storedRealm;
		if (string.IsNullOrWhiteSpace(realmId)) return;

		// 高阶特质比权威字段更高，说明曾在写境界后中断；补齐一次权威写入。
		if (XjRealmHelper.GetOrder(traitRealm) > XjRealmHelper.GetOrder(storedRealm))
		{
			XjCultivationStateTransitions.TrySetRealm(actor, traitRealm, true);
			realmId = traitRealm;
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			EnsureZiFuTitle(actor, daoTu, XjXianJiAccessor.BuildState(actor).Count);
		}
		else if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJieLinXian, out int jieLin) && jieLin == 1)
				ApplyOnJieLinPromotion(actor);
			else EnsureJinDanTitle(actor, daoTu);
		}
		else if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			EnsureShenDanTitle(actor, daoTu);
		}
		else
		{
			EnsureNoPreZiFuTitle(actor, realmId);
		}
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		XjCultivatorCache.CheckAndUpdate(actor);
	}

	internal static void EnsureNoPreZiFuTitle(Actor actor, string realmId)
	{
		if (actor?.data == null
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)) return;
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (!string.Equals(normalized, XjRealmIds.TaiXi, StringComparison.Ordinal)
			&& !string.Equals(normalized, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& !string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return;
		}

		string baseName = ResolveBaseName(actor);
		string realmDisplay = GetRealmDisplay(normalized);
		if (XjWeaponArtSystem.TryGetLowRealmSwordDisplay(actor, out string weaponArtTitle, out _))
		{
			// 剑意只进入玄鉴照录的器艺字段，绝不拼入人物姓名。
			string titledName = weaponArtTitle + "·" + baseName + "-" + realmDisplay;
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, weaponArtTitle);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
			actor.name = titledName;
			actor.data.custom_name = true;
			return;
		}
		string expected = baseName + "-" + realmDisplay;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string storedRealm);
		if (string.IsNullOrWhiteSpace(storedTitle)
			&& string.Equals(storedRealm?.Trim(), realmDisplay, StringComparison.Ordinal)
			&& string.Equals(actor.getName()?.Trim(), expected, StringComparison.Ordinal))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = expected;
		actor.data.custom_name = true;
	}

	internal static void EnsureZiFuTitle(Actor actor, string daoTu, int xianJiCount)
	{
		if (actor?.data == null
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return;
		}
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string title = (storedTitle ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(title))
		{
			string currentName = actor.getName() ?? string.Empty;
			int separator = currentName.IndexOf('·');
			title = separator > 0 ? currentName.Substring(0, separator).Trim() : string.Empty;
		}

		bool validSuffix = title.EndsWith("真人", StringComparison.Ordinal)
			|| title.EndsWith("大真人", StringComparison.Ordinal);
		if (string.IsNullOrWhiteSpace(title)
			|| !validSuffix
			|| XjRealmTitleNameLibrary.ContainsForbiddenRealmWord(title)
			|| XjRealmTitleNameLibrary.HasForeignDaoTuTitleMarker(title, daoTu))
		{
			ApplyOnPromotion(actor, XjRealmIds.ZiFu, daoTu);
		}
		RefreshZiFuTitleAfterXianJiChange(actor, xianJiCount);
	}

	internal static void RefreshZiFuTitleAfterXianJiChange(Actor actor, int xianJiCount)
	{
		if (actor?.data == null
			|| xianJiCount < 4
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string title = (storedTitle ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(title))
		{
			string currentName = actor.getName() ?? string.Empty;
			int separator = currentName.IndexOf('·');
			title = separator > 0 ? currentName.Substring(0, separator).Trim() : string.Empty;
		}

		if (XjRealmTitleNameLibrary.ContainsForbiddenRealmWord(title)
			|| XjRealmTitleNameLibrary.HasForeignDaoTuTitleMarker(title, daoTu))
		{
			ApplyOnPromotion(actor, XjRealmIds.ZiFu, daoTu);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out storedTitle);
			title = (storedTitle ?? string.Empty).Trim();
		}

		if (title.EndsWith("大真人", StringComparison.Ordinal)
			|| !title.EndsWith("真人", StringComparison.Ordinal))
		{
			return;
		}

		string promotedTitle = title.Substring(0, title.Length - "真人".Length) + "大真人";
		string baseName = ResolveBaseName(actor);
		const string realmDisplay = "紫府";
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, promotedTitle);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = promotedTitle + "·" + baseName + "-" + realmDisplay;
		actor.data.custom_name = true;
	}

	internal static void ApplyOnJieLinPromotion(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		string baseName = ResolveBaseName(actor);
		string title = XjRealmTitleNameLibrary.GenerateJieLinTitle(GetActorId(actor));
		const string realmDisplay = "结璘仙";
		string fullName = string.IsNullOrWhiteSpace(title)
			? baseName + "-" + realmDisplay
			: title + "·" + baseName + "-" + realmDisplay;

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = fullName;
	}

	internal static void EnsureJinDanTitle(Actor actor, string daoTu)
	{
		if (actor?.data == null || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string titleToCheck = (storedTitle ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(titleToCheck))
		{
			string currentName = actor.getName() ?? string.Empty;
			int separator = currentName.IndexOf('·');
			titleToCheck = separator > 0 ? currentName.Substring(0, separator).Trim() : string.Empty;
		}

		if (!string.IsNullOrWhiteSpace(titleToCheck)
			&& HasHighRealmTitleSuffix(titleToCheck)
			&& !XjRealmTitleNameLibrary.ContainsForbiddenRealmWord(titleToCheck)
			&& !XjRealmTitleNameLibrary.HasForeignDaoTuTitleMarker(titleToCheck, daoTu))
		{
			return;
		}

		ApplyOnPromotion(actor, XjRealmIds.JinDan, daoTu);
	}

	internal static void EnsureShenDanTitle(Actor actor, string daoTu)
	{
		if (actor?.data == null || XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string storedRealmDisplay);
		string generated = XjRealmTitleNameLibrary.GenerateTitle(XjRealmIds.ShenDan, daoTu, GetActorId(actor));
		if (string.Equals(storedTitle?.Trim(), generated, StringComparison.Ordinal)
			&& string.Equals(storedRealmDisplay?.Trim(), "神丹", StringComparison.Ordinal))
		{
			return;
		}

		string legacy = (storedTitle ?? string.Empty).Trim();
		bool shouldRepair = string.IsNullOrWhiteSpace(legacy)
			|| !legacy.EndsWith("侍神", StringComparison.Ordinal)
			|| XjRealmTitleNameLibrary.ContainsForbiddenRealmWord(legacy)
			|| XjRealmTitleNameLibrary.HasForeignDaoTuTitleMarker(legacy, daoTu)
			|| !string.Equals(storedRealmDisplay?.Trim(), "神丹", StringComparison.Ordinal);
		if (shouldRepair)
		{
			ApplyOnPromotion(actor, XjRealmIds.ShenDan, daoTu);
		}
	}

	internal static void EnsureJinDanAnchorTitleAtShenDanCapacity(Actor actor, string daoTu)
	{
		if (actor?.data == null
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		string title = (storedTitle ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(title))
		{
			title = XjRealmTitleNameLibrary.GenerateTitle(XjRealmIds.JinDan, daoTu, GetActorId(actor));
		}
		if (string.IsNullOrWhiteSpace(title) || title.EndsWith("神君", StringComparison.Ordinal))
		{
			return;
		}

		string nextTitle = ReplaceHighRealmTitleSuffix(title, "神君");
		if (string.IsNullOrWhiteSpace(nextTitle))
		{
			nextTitle = "清微神君";
		}
		nextTitle = XjRealmTitleNameLibrary.RepairForeignTitle(daoTu, nextTitle, XjRealmTitleNameLibrary.GenerateTitle(XjRealmIds.JinDan, daoTu, GetActorId(actor)));

		string baseName = ResolveBaseName(actor);
		const string realmDisplay = "金丹";
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, nextTitle);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		actor.name = nextTitle + "·" + baseName + "-" + realmDisplay;
		actor.data.custom_name = true;
	}

	internal static void EnsureJieLinTitle(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string storedTitle);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string storedRealmDisplay);
		string generated = XjRealmTitleNameLibrary.GenerateJieLinTitle(GetActorId(actor));
		if (string.Equals(storedTitle?.Trim(), generated, StringComparison.Ordinal)
			&& string.Equals(storedRealmDisplay?.Trim(), "结璘仙", StringComparison.Ordinal))
		{
			return;
		}

		string legacy = (storedTitle ?? string.Empty).Trim();
		bool isLegacyRealmTitle = string.IsNullOrWhiteSpace(legacy)
			|| legacy.EndsWith("真人", StringComparison.Ordinal)
			|| !HasHighRealmTitleSuffix(legacy)
			|| XjRealmTitleNameLibrary.ContainsForbiddenRealmWord(legacy)
			|| !string.Equals(storedRealmDisplay?.Trim(), "结璘仙", StringComparison.Ordinal);
		if (isLegacyRealmTitle)
		{
			ApplyOnJieLinPromotion(actor);
		}
	}

	private static bool HasHighRealmTitleSuffix(string title)
	{
		string value = (title ?? string.Empty).Trim();
		return value.EndsWith("玄君", StringComparison.Ordinal)
			|| value.EndsWith("神君", StringComparison.Ordinal)
			|| value.EndsWith("真君", StringComparison.Ordinal)
			|| value.EndsWith("飞君", StringComparison.Ordinal)
			|| value.EndsWith("侍神", StringComparison.Ordinal);
	}

	private static string ReplaceHighRealmTitleSuffix(string title, string suffix)
	{
		string value = (title ?? string.Empty).Trim();
		string nextSuffix = (suffix ?? string.Empty).Trim();
		if (value.Length == 0 || nextSuffix.Length == 0)
		{
			return string.Empty;
		}

		string[] suffixes = { "玄君", "神君", "真君", "飞君", "侍神" };
		for (int i = 0; i < suffixes.Length; i++)
		{
			string oldSuffix = suffixes[i];
			if (value.EndsWith(oldSuffix, StringComparison.Ordinal))
			{
				return value.Substring(0, value.Length - oldSuffix.Length) + nextSuffix;
			}
		}
		return value + nextSuffix;
	}

	private static string ResolveBaseName(Actor actor)
	{
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string storedBaseName)
			&& !string.IsNullOrWhiteSpace(storedBaseName))
		{
			return storedBaseName.Trim();
		}

		string current = actor.getName();
		string clean = StripVNextTitle(current);
		return string.IsNullOrWhiteSpace(clean) ? current : clean;
	}

	private static string StripVNextTitle(string currentName)
	{
		string text = (currentName ?? string.Empty).Trim();
		if (text.StartsWith("「", StringComparison.Ordinal))
		{
			int titleEnd = text.IndexOf("」·", StringComparison.Ordinal);
			if (titleEnd >= 0 && titleEnd + 2 < text.Length)
			{
				text = text.Substring(titleEnd + 2).Trim();
			}
		}

		int titleSeparator = text.IndexOf('·');
		if (titleSeparator >= 0 && titleSeparator + 1 < text.Length)
		{
			text = text.Substring(titleSeparator + 1).Trim();
		}

		// 旧版可能形成“尊号·姓名-境界-长剑意”。不能只截最后一个
		// 连字符，否则会把“姓名-境界”误存成基础姓名。优先在第一个
		// 已知境界后缀处截断，兼容剑意后缀与重复境界后缀。
		string[] realmMarkers = { "-胎息", "-炼气", "-筑基", "-紫府", "-金丹", "-神丹", "-结璘仙", "－胎息", "－炼气", "－筑基", "－紫府", "－金丹", "－神丹", "－结璘仙" };
		int realmSeparator = -1;
		for (int i = 0; i < realmMarkers.Length; i++)
		{
			int index = text.IndexOf(realmMarkers[i], StringComparison.Ordinal);
			if (index > 0 && (realmSeparator < 0 || index < realmSeparator)) realmSeparator = index;
		}
		if (realmSeparator < 0) realmSeparator = text.LastIndexOf("-", StringComparison.Ordinal);
		if (realmSeparator > 0) text = text.Substring(0, realmSeparator).Trim();

		return text;
	}

	private static string GetRealmDisplay(string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal))
		{
			return "胎息";
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			return "炼气";
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return "筑基";
		}

		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return "紫府";
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return "金丹";
		}
		if (string.Equals(realmId, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return "神丹";
		}

		return realmId ?? string.Empty;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
