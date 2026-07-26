using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjXianJiCatalog
{
	private static readonly Dictionary<string, string[]> DaoTuXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "兑金", new[] { "位从孚", "君兑隅", "金窍心", "不穷锋", "杀收宫" } },
		{ "库金", new[] { "金销洞", "帑梁银", "百宝殿", "藏金地", "铜锈炁" } },
		{ "庚金", new[] { "今去故", "再折毁", "镂金石", "天金冑", "墓门棘" } },
		{ "齐金", new[] { "天齐满", "金戈阵", "肃杀令", "齐天律", "万军令" } },
		{ "逍金", new[] { "逍遥游", "望商锋", "御金行", "无羁绊", "敛锋芒" } },
		{ "正木", new[] { "木成方", "位从专", "见查语", "背南行", "万古青" } },
		{ "集木", new[] { "祸延生", "隼就栖", "诸蓼会", "凌云木", "妄诞林" } },
		{ "角木", new[] { "余养性", "潇重林", "黎运春", "乙木全", "青龙气" } },
		{ "更木", new[] { "病前春", "枯荣道", "更迭术", "万古新", "新陈变" } },
		{ "保木", new[] { "蕴长生", "万劫不", "古盘根", "森罗相", "栋梁材" } },
		{ "坎水", new[] { "溪上翁", "浩瀚海", "位从险", "长云暗", "恨江去", "入坎窞" } },
		{ "府水", new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖", "养命蛟" } },
		{ "合水", new[] { "归流处", "谶在兹", "广准圣", "诸合还", "妖渎河" } },
		{ "牝水", new[] { "往生泉", "佞无晨", "谿谷会", "蕴宝瓶", "参玄臟" } },
		{ "渌水", new[] { "洞泉声", "清夕雨", "丑癸藏", "如重浊", "洗劫露" } },
		{ "离火", new[] { "位从罗", "顺平征", "大离书", "九重擭", "折焚尽" } },
		{ "真火", new[] { "天兜火", "雉离行", "真火身", "燎原势", "炼乾坤" } },
		{ "并火", new[] { "焰中乌", "乌从欲", "心期焚", "兼险夺", "至命除" } },
		{ "牡火", new[] { "牡煞火", "韬无灾", "高陵父", "受灴龠", "光明相" } },
		{ "灴火", new[] { "燔旧室", "白樆心", "布燥使", "秉灴夏", "虚宫火" } },
		{ "艮土", new[] { "愚赶山", "正源谷", "山岳镇", "不动尊", "艮为山" } },
		{ "戊土", new[] { "受抚顶", "仙无漏", "戊心岩", "厚德载", "中宫定" } },
		{ "归土", new[] { "狡落原", "土归尘", "万物归", "归土藏", "观红尘" } },
		{ "宝土", new[] { "高垒燕", "藏纳宫", "膏泽治", "万众心", "万宝敛" } },
		{ "宣土", new[] { "天下心", "神用命", "训浚明", "宣威仪", "德广天" } },
		{ "青宣", new[] { "玄羊子", "伏青山", "青宣岳", "上岩神", "观地冥" } },
		{ "太阴", new[] { "湖月秋", "诣太素", "结璘章", "仪对影", "月光府" } },
		{ "少阴", new[] { "香俱沉", "太冲观", "调杼柚", "广寒敕", "蟾宫折" } },
		{ "厥阴", new[] { "不紫衣", "不二舆", "青鸾唳", "归藏演", "厥阴主" } },
		{ "太阳", new[] { "分阳钗", "显挂天", "耀尘世", "极盛御", "帝冕冠" } },
		{ "少阳", new[] { "邪绝求", "奉东君", "目骋怀", "青阳焕", "残阳泣" } },
		{ "明阳", new[] { "昭澈心", "谒天门", "帝观元", "君蹈危", "赤断镞" } },
		{ "清炁", new[] { "清元风", "益清气", "浮云身", "炁引池", "空应散" } },
		{ "邃炁", new[] { "代行妨", "闇天殃", "逆人伦", "众生苦", "乱天理" } },
		{ "紫炁", new[] { "绕东山", "蕴宝瓶", "列紫篇", "坤辰修", "养生主" } },
		{ "真炁", new[] { "抱石眠", "霞羽客", "鹤挂衣", "授长生", "蜕凡尘" } },
		{ "寒炁", new[] { "松上雪", "入清听", "祢水寒", "沆砀满", "寒世天" } },
		{ "晞炁", new[] { "未阕华", "乞代夜", "郁燠苦", "庆垂轩", "议八辟" } },
		{ "瑞炁", new[] { "好功箓", "瑞气云", "祥云现", "福星照", "瑞炁祥" } },
		{ "煞炁", new[] { "不空劫", "罗刹海", "千百身", "人中屠", "戮天元" } },
		{ "谪炁", new[] { "藏壑舟", "薄虞渊", "惘乾坤", "谪仙落", "盼天赐" } },
		{ "华炁", new[] { "冠灵旒", "锦绣袍", "仪仗全", "光华现", "琉璃履" } },
		{ "上仪", new[] { "明心筵", "致缉熙", "观天地", "无极生", "脱胎路" } },
		{ "下仪", new[] { "幽冥身", "轮回殿", "黄泉路", "忘川河", "了前尘" } },
		{ "玄雷", new[] { "靖平敕", "盛阳墟", "天鸣策", "至阴墟", "养青冥" } },
		{ "霄雷", new[] { "冬雷声", "春惊蛰", "轻雷落", "震雷枢", "定周天" } },
		{ "元雷", new[] { "主煞仪", "元雷罚", "九霄动", "霄汉清", "玄雷劫" } }
	};

	private static readonly Dictionary<string, string[]> LowDaoTuXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "太阴", new[] { "不胜寒", "再圆阙", "广寒枝", "惊鹊乡", "授玄珠" } },
		{ "少阴", new[] { "霜降微", "幽谷兰", "凝冰魄", "潜渊鱼", "敛华光" } },
		{ "厥阴", new[] { "隐风谷", "藏木叶", "参疑室", "蛰虫沙", "掩弊服" } },
		{ "太阳", new[] { "扶桑枝", "耀金乌", "赤霞冠", "明光铠", "炽阳砂" } },
		{ "少阳", new[] { "初晖草", "萌春芽", "晨露晞", "微明火", "启明星" } },
		{ "明阳", new[] { "天下明", "煌元关", "长明阶", "誓崤身", "顾署舆" } },
		{ "兑金", new[] { "白虎牙", "西极铁", "锋镝鸣", "霜刃寒", "金戈影" } },
		{ "庚金", new[] { "天下革", "断金玉", "削铁泥", "刚锋气", "砺石髓" } },
		{ "齐金", new[] { "天金砂", "圆满轮", "无缺璧", "纯钧魄", "恒常性" } },
		{ "库金", new[] { "藏宝匣", "积财山", "聚珍釜", "纳川囊", "封存印" } },
		{ "逍金", new[] { "逍遥铁", "自在锋", "无拘刃", "云游剑", "散金尘" } },
		{ "角木", new[] { "青龙鳞", "建木芽", "春风枝", "长生叶", "句芒符" } },
		{ "正木", new[] { "正直干", "巽风枝", "参天木", "向阳叶", "规矩林" } },
		{ "集木", new[] { "群鸟栖", "聚材林", "朱丹参", "万木春", "荟蔚丛" } },
		{ "更木", new[] { "更新叶", "岁寒松", "改节竹", "轮替花", "代谢果" } },
		{ "保木", new[] { "守护藤", "长青柏", "护根土", "存续种", "安巢枝" } },
		{ "坎水", new[] { "玄冥珠", "沧海潮", "天一水", "寒泉眼", "归墟浪" } },
		{ "渌水", new[] { "清溪石", "渌波纹", "澄潭影", "碧水珠", "净莲露" } },
		{ "合水", new[] { "汇流川", "交融湖", "合涧溪", "并泉脉", "混元液" } },
		{ "府水", new[] { "幽重玄", "沉羽渊", "浮芥塘", "不渡河", "鸿毛津" } },
		{ "牝水", new[] { "玄牝泉", "母源海", "滋生液", "化生池", "胎息潮" } },
		{ "离火", new[] { "朱雀羽", "祝融火", "丹景光", "南明焰", "离宫灯" } },
		{ "并火", new[] { "双焰合", "并蒂火", "连焚木", "共燃薪", "同炽炭" } },
		{ "真火", new[] { "三昧真", "纯阳燧", "先天火", "不灭灯", "净世焰" } },
		{ "灴火", new[] { "灴炉炭", "烘炉火", "灼野燎", "炎夏阳", "焦土尘" } },
		{ "牡火", new[] { "雄烈焰", "刚猛燚", "阳刚火", "烈性焰", "壮阳辉" } },
		{ "艮土", new[] { "后土符", "镇岳印", "坤厚石", "中宫尘", "不动山" } },
		{ "戊土", new[] { "戊己壤", "中央土", "厚德尘", "承载地", "黄天厚" } },
		{ "宣土", new[] { "宣化泥", "社稷坛", "布德土", "教稼穑", "丰年谷" } },
		{ "归土", new[] { "归根尘", "落葬丘", "返璞壤", "息壤土", "安息地" } },
		{ "宝土", new[] { "宝藏岩", "灵玉矿", "珍稀壤", "膏腴田", "沃土膏" } },
		{ "青宣", new[] { "下阳元" } },
		{ "清炁", new[] { "清虚台", "上善云", "浮云骨", "空明意", "澄心镜" } },
		{ "紫炁", new[] { "紫霞绡", "祥瑞烟", "华盖云", "帝星气", "玄元紫" } },
		{ "真炁", new[] { "真元息", "纯阳罡", "先天一", "不二气", "至大刚" } },
		{ "邃炁", new[] { "幽玄雾", "深冥霭", "邃古息", "窈冥烟", "太虚氤" } },
		{ "寒炁", new[] { "寒冰魄", "霜雪精", "凛冬风", "冻云霭", "冷月霜" } },
		{ "晞炁", new[] { "朝露晞", "晨光熹", "干阳燥", "晞发风", "蒸腾雾" } },
		{ "瑞炁", new[] { "祥云瑞", "吉庆烟", "福临气", "绵长运", "晋阶霞" } },
		{ "煞炁", new[] { "凶煞风", "杀伐雾", "戾气云", "劫难烟", "死兆霭" } },
		{ "谪炁", new[] { "谪仙尘", "贬落烟", "流放雾", "罪愆气", "凡尘染" } },
		{ "华炁", new[] { "华彩云", "光耀气", "锦绣烟", "荣华雾", "璀璨霭" } },
		{ "上仪", new[] { "仪仗风", "威仪气", "尊贵烟", "典礼云", "朝会雾" } },
		{ "下仪", new[] { "卑下尘", "凡俗气", "微贱烟", "草莽雾", "野逸风" } },
		{ "玄雷", new[] { "玉枢印", "雷霆符", "震霆角", "劫雷声", "九霄鼓" } },
		{ "霄雷", new[] { "云霄霆", "天外雷", "青穹电", "九霄鸣", "高空闪" } },
		{ "元雷", new[] { "元磁光", "先天雷", "混元霹", "太初霆", "无极电" } }
	};

	private static readonly Dictionary<string, string[]> DocumentUpperXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "太阴", new[] { "结璘章", "仪对影", "湖月秋", "诣太素" } },
		{ "少阴", new[] { "太冲观", "香俱沉", "调杼柚" } },
		{ "厥阴", new[] { "不紫衣", "不二舆" } },
		{ "太阳", new[] { "分阳钗" } },
		{ "少阳", new[] { "奉东君", "目骋怀", "邪绝求" } },
		{ "明阳", new[] { "昭澈心", "谒天门", "帝观元", "君蹈危", "赤断镞" } },
		{ "兑金", new[] { "金窍心", "位从孚", "君兑隅", "杀收宫", "不穷锋" } },
		{ "库金", new[] { "帑梁银", "金销洞" } },
		{ "庚金", new[] { "今去故", "再折毁", "镂金石", "天金冑", "墓门棘" } },
		{ "齐金", new[] { "天齐满" } },
		{ "逍金", new[] { "望商锋", "逍遥游" } },
		{ "正木", new[] { "木成方", "位从专", "见查语", "背南行" } },
		{ "集木", new[] { "妄诞林", "祸延生", "隼就栖", "诸蓼会", "凌云木" } },
		{ "角木", new[] { "余养性", "潇重林", "黎运春", "乙木全" } },
		{ "更木", new[] { "病前春" } },
		{ "坎水", new[] { "溪上翁", "入坎窞", "浩瀚海", "长云暗", "恨江去", "位从险" } },
		{ "府水", new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖" } },
		{ "合水", new[] { "归流处", "谶在兹", "广准圣", "诸合还", "妖渎河" } },
		{ "牝水", new[] { "佞无晨", "往生泉", "谿谷会", "参玄臟" } },
		{ "渌水", new[] { "如重浊", "洞泉声", "清夕雨", "丑癸藏", "洗劫露" } },
		{ "离火", new[] { "顺平征", "位从罗", "大离书", "九重擭", "折焚尽" } },
		{ "真火", new[] { "雉离行", "天兜火", "治命神" } },
		{ "并火", new[] { "乌从欲", "心期焚", "兼险夺", "至命除", "焰中乌" } },
		{ "牡火", new[] { "牡煞火", "韬无灾", "高陵父", "受灴龠" } },
		{ "灴火", new[] { "燔旧室", "白樆心", "布燥使", "秉灴夏" } },
		{ "艮土", new[] { "愚赶山", "正源谷" } },
		{ "戊土", new[] { "受抚顶", "仙无漏", "戊心岩" } },
		{ "归土", new[] { "狡落原", "庥命簋", "有常主", "养役母" } },
		{ "宝土", new[] { "高垒燕", "藏纳宫", "膏泽治", "梭摩岭" } },
		{ "宣土", new[] { "朝社参", "神用命", "训浚明" } },
		{ "青宣", new[] { "玄羊子", "伏青山", "青宣岳", "上岩神", "观地冥" } },
		{ "清炁", new[] { "清元风", "益清气", "浮云身", "炁临宇", "空应散" } },
		{ "邃炁", new[] { "代行妨", "闇天殃" } },
		{ "紫炁", new[] { "绕东山", "列紫篇", "道始兆", "坤辰修", "养生主" } },
		{ "真炁", new[] { "抱石眠", "霞羽客", "鹤挂衣", "授长生" } },
		{ "寒炁", new[] { "入清听", "松上雪", "祢水寒", "沆砀满" } },
		{ "晞炁", new[] { "未阕华", "乞代夜", "郁燠苦", "庆垂轩", "议八辟" } },
		{ "瑞炁", new[] { "好功箓", "瑞气云" } },
		{ "煞炁", new[] { "不空劫", "罗剎海", "箝恨口" } },
		{ "谪炁", new[] { "藏壑舟", "薄虞渊" } },
		{ "华炁", new[] { "冠灵旒" } },
		{ "玄雷", new[] { "靖平敕", "至阳嘘", "神宫誓", "律演威", "伐封坛" } },
		{ "霄雷", new[] { "冬雷声", "春惊蛰", "轻雷落", "斡动紫" } },
		{ "元雷", new[] { "主煞仪" } }
	};

	private static readonly Dictionary<string, string[]> DocumentLowerXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "太阴", new[] { "授玄珠", "不胜寒", "再圆阙", "惊鹊乡", "夜光府" } },
		{ "厥阴", new[] { "掩弊服", "利异臣", "参疑室" } },
		{ "太阳", new[] { "视天统" } },
		{ "明阳", new[] { "天下明", "煌元关", "长明阶", "誓崤身", "顾署舆" } },
		{ "兑金", new[] { "请两忘", "不穷锋" } },
		{ "庚金", new[] { "天下革", "金兽羽" } },
		{ "集木", new[] { "朱丹参" } },
		{ "角木", new[] { "木凭春", "定元春" } },
		{ "更木", new[] { "天下易" } },
		{ "坎水", new[] { "据岭中", "泾龙王" } },
		{ "府水", new[] { "养命蛟" } },
		{ "合水", new[] { "雾回阴" } },
		{ "渌水", new[] { "天下覆" } },
		{ "离火", new[] { "望日述" } },
		{ "灴火", new[] { "天下熯" } },
		{ "宣土", new[] { "天下心" } },
		{ "青宣", new[] { "下阳元" } },
		{ "清炁", new[] { "炁引池" } },
		{ "紫炁", new[] { "蕴宝瓶" } },
		{ "晞炁", new[] { "焜煌复", "掩尘雾" } },
		{ "煞炁", new[] { "千百身" } },
		{ "玄雷", new[] { "天鸣策" } },
		{ "霄雷", new[] { "玄雷泊" } },
		{ "元雷", new[] { "脱煞胎" } }
	};

	private static readonly Dictionary<string, string[]> EffectiveUpperXianJiMap = BuildEffectiveUpperMap();
	private static readonly Dictionary<string, string[]> EffectiveLowerXianJiMap = BuildEffectiveLowerMap();

	private static readonly Dictionary<string, string[]> AdjacentDaoTuMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "兑金", new[] { "逍金", "齐金", "库金", "庚金" } }, { "逍金", new[] { "兑金", "齐金", "库金", "庚金" } }, { "齐金", new[] { "兑金", "逍金", "库金", "庚金" } }, { "库金", new[] { "兑金", "逍金", "齐金", "庚金" } }, { "庚金", new[] { "兑金", "逍金", "齐金", "库金" } },
		{ "角木", new[] { "正木", "集木", "更木", "保木" } }, { "正木", new[] { "角木", "集木", "更木", "保木" } }, { "集木", new[] { "角木", "正木", "更木", "保木" } }, { "更木", new[] { "角木", "正木", "集木", "保木" } }, { "保木", new[] { "角木", "正木", "集木", "更木" } },
		{ "坎水", new[] { "渌水", "合水", "府水", "牝水" } }, { "渌水", new[] { "坎水", "合水", "府水", "牝水" } }, { "合水", new[] { "坎水", "渌水", "府水", "牝水" } }, { "府水", new[] { "坎水", "渌水", "合水", "牝水" } }, { "牝水", new[] { "坎水", "渌水", "合水", "府水" } },
		{ "离火", new[] { "灴火", "并火", "真火", "牡火" } }, { "灴火", new[] { "离火", "并火", "真火", "牡火" } }, { "并火", new[] { "离火", "灴火", "真火", "牡火" } }, { "真火", new[] { "离火", "灴火", "并火", "牡火" } }, { "牡火", new[] { "离火", "灴火", "并火", "真火" } },
		{ "艮土", new[] { "戊土", "归土", "宝土", "宣土" } }, { "戊土", new[] { "艮土", "归土", "宝土", "宣土" } }, { "归土", new[] { "艮土", "戊土", "宝土", "宣土" } }, { "宝土", new[] { "艮土", "戊土", "归土", "宣土" } }, { "宣土", new[] { "艮土", "戊土", "归土", "宝土" } },
		{ "太阴", new[] { "少阴", "厥阴" } }, { "少阴", new[] { "太阴", "厥阴" } }, { "厥阴", new[] { "太阴", "少阴" } },
		{ "太阳", new[] { "少阳", "明阳" } }, { "少阳", new[] { "太阳", "明阳" } }, { "明阳", new[] { "太阳", "少阳" } },
		{ "清炁", new[] { "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "紫炁", new[] { "清炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "真炁", new[] { "清炁", "紫炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "邃炁", new[] { "清炁", "紫炁", "真炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "寒炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "晞炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "瑞炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "煞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "煞炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "华炁", "谪炁", "上仪", "下仪" } },
		{ "华炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "谪炁", "上仪", "下仪" } },
		{ "谪炁", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "上仪", "下仪" } },
		{ "上仪", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "下仪" } },
		{ "下仪", new[] { "清炁", "紫炁", "真炁", "邃炁", "寒炁", "晞炁", "瑞炁", "煞炁", "华炁", "谪炁", "上仪" } },
		{ "玄雷", new[] { "霄雷", "元雷" } }, { "霄雷", new[] { "玄雷", "元雷" } }, { "元雷", new[] { "玄雷", "霄雷" } }
	};

	internal static bool TryPick(string daoTu, int ordinal, long actorId, out string id)
	{
		return TryPickAvailable(daoTu, ordinal, actorId, Array.Empty<string>(), out id);
	}

	internal static bool TryPickAvailable(string daoTu, int ordinal, long actorId, string[] existingIds, out string id)
	{
		return TryPickForProgression(daoTu, ordinal, actorId, existingIds, false, out id);
	}

	internal static bool TryPickForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		bool zhengWeiManifested,
		out string id)
	{
		return TryPickForProgression(
			daoTu,
			ordinal,
			actorId,
			existingIds,
			zhengWeiManifested,
			true,
			out id);
	}

	internal static bool TryPickForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		bool zhengWeiManifested,
		bool allowOtherPool,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			id = string.Empty;
			return false;
		}
		if (IsQingXuan(normalizedDaoTu))
		{
			return TryPickQingXuanForProgression(normalizedDaoTu, ordinal, actorId, existingIds, out id);
		}

		ResolvePoolWeights(
			normalizedDaoTu,
			ordinal,
			existingIds,
			zhengWeiManifested,
			out int ownWeight,
			out int lowerWeight,
			out int adjacentWeight,
			out int otherWeight,
			out string fixedAdjacentDaoTu);

		List<string> own = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
		List<string> lower = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
		List<string> adjacent = BuildAdjacentPool(normalizedDaoTu, fixedAdjacentDaoTu, existingIds);
		List<string> other = BuildOtherPool(normalizedDaoTu, existingIds);

		int effectiveOwnWeight = own.Count > 0 ? ownWeight : 0;
		int effectiveLowerWeight = lower.Count > 0 ? lowerWeight : 0;
		int effectiveAdjacentWeight = adjacent.Count > 0 ? adjacentWeight : 0;
		bool canPickOther = allowOtherPool && other.Count > 0 && otherWeight > 0;
		int standardTotalWeight = effectiveOwnWeight + effectiveLowerWeight + effectiveAdjacentWeight;
		if (standardTotalWeight <= 0)
		{
			id = string.Empty;
			return false;
		}

		// 第4、5门的“其他池”严格固定为10个百分点，不因正常池缺项而放大。
		// 若上位、下位、相邻三类均无候选，则本轮没有合法目标，不把其他池
		// 临时提升为100%，也不消耗正常的神通判定周期。
		List<string> selected;
		int otherRoll = XjDeterministicHash.PositiveIndex(
			actorId + ordinal, normalizedDaoTu + "|xianji_other_pool", 100);
		if (canPickOther && otherRoll < otherWeight)
		{
			selected = other;
		}
		else
		{
			int roll = XjDeterministicHash.PositiveIndex(
				actorId + ordinal, normalizedDaoTu + "|xianji_standard_pool", standardTotalWeight);
			if (roll < effectiveOwnWeight)
			{
				selected = own;
			}
			else if ((roll -= effectiveOwnWeight) < effectiveLowerWeight)
			{
				selected = lower;
			}
			else
			{
				selected = adjacent;
			}
		}

		id = selected[XjDeterministicHash.PositiveIndex(
			actorId + ordinal,
			normalizedDaoTu + "|xianji_candidate",
			selected.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool TryPickUpperForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		List<string> upper = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
		if (upper.Count == 0)
		{
			id = string.Empty;
			return false;
		}

		id = upper[XjDeterministicHash.PositiveIndex(
			actorId + ordinal,
			normalizedDaoTu + "|daozhu_upper_xianji",
			upper.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	/// <summary>
	/// 道主优先取本道途上位神通；上位池已经去重耗尽且正位已显化时，
	/// 才从本道途下位与相邻道途池中补取，永不开放其他池。
	/// </summary>
	internal static bool TryPickDaoZhuForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		bool zhengWeiManifested,
		out string id)
	{
		if (TryPickUpperForProgression(daoTu, ordinal, actorId, existingIds, out id))
		{
			return true;
		}
		if (!zhengWeiManifested)
		{
			id = string.Empty;
			return false;
		}

		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		List<string> candidates = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
		List<string> adjacent = BuildAdjacentPool(normalizedDaoTu, string.Empty, existingIds);
		for (int i = 0; i < adjacent.Count; i++)
		{
			if (!candidates.Contains(adjacent[i])) candidates.Add(adjacent[i]);
		}
		if (candidates.Count == 0)
		{
			id = string.Empty;
			return false;
		}
		id = candidates[XjDeterministicHash.PositiveIndex(
			actorId + ordinal, normalizedDaoTu + "|daozhu_lower_adjacent", candidates.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool TryResolveOwningDaoTu(string id, out string daoTu)
	{
		daoTu = string.Empty;
		string normalizedId = (id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedId)) return false;

		List<string> keys = new List<string>(EffectiveUpperXianJiMap.Keys);
		keys.Sort(StringComparer.Ordinal);
		for (int i = 0; i < keys.Count; i++)
		{
			if (Contains(MapGetUpper(keys[i]), normalizedId))
			{
				daoTu = keys[i];
				return true;
			}
		}
		keys = new List<string>(EffectiveLowerXianJiMap.Keys);
		keys.Sort(StringComparer.Ordinal);
		for (int i = 0; i < keys.Count; i++)
		{
			if (Contains(MapGetLower(keys[i]), normalizedId))
			{
				daoTu = keys[i];
				return true;
			}
		}
		return false;
	}

	internal static XjXianJiPoolKind GetPoolKind(string daoTu, string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		string normalizedId = (id ?? string.Empty).Trim();
		if (Contains(MapGetUpper(normalizedDaoTu), normalizedId))
		{
			return XjXianJiPoolKind.Native;
		}

		if (Contains(MapGetLower(normalizedDaoTu), normalizedId))
		{
			return XjXianJiPoolKind.Lower;
		}

		if (AdjacentDaoTuMap.TryGetValue(normalizedDaoTu, out string[] adjacentDaoTus))
		{
			for (int i = 0; i < adjacentDaoTus.Length; i++)
			{
				if (Contains(MapGetUpper(adjacentDaoTus[i]), normalizedId)
					|| Contains(MapGetLower(adjacentDaoTus[i]), normalizedId))
				{
					return XjXianJiPoolKind.Adjacent;
				}
			}
		}

		return XjXianJiPoolKind.Other;
	}

	/// <summary>
	/// 陆江仙模拟器“神通造化”专用：把异道神通改造成当前道途可接受的
	/// 本道途上位、本道途下位或相邻道途神通。三类非空池等概率选取，
	/// 再在所选池内稳定随机；existingIds 用于保证同一角色五门神通不重名。
	/// </summary>
	internal static bool TryPickUpperLowerAdjacentReplacement(
		string daoTu,
		long actorId,
		int currentYear,
		int slotIndex,
		string replacedId,
		string[] existingIds,
		out string id)
	{
		id = string.Empty;
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		List<List<string>> categories = new List<List<string>>(3);
		List<string> upper = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
		List<string> lower = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
		List<string> adjacent = BuildAdjacentPool(normalizedDaoTu, string.Empty, existingIds);
		upper.Sort(StringComparer.Ordinal);
		lower.Sort(StringComparer.Ordinal);
		adjacent.Sort(StringComparer.Ordinal);
		if (upper.Count > 0) categories.Add(upper);
		if (lower.Count > 0) categories.Add(lower);
		if (adjacent.Count > 0) categories.Add(adjacent);
		if (categories.Count == 0)
		{
			return false;
		}

		long seed = actorId
			+ Math.Max(0, currentYear) * 4099L
			+ Math.Max(0, slotIndex) * 131L
			+ XjDeterministicHash.StableHash((replacedId ?? string.Empty).Trim());
		List<string> selected = categories[XjDeterministicHash.PositiveIndex(
			seed,
			normalizedDaoTu + "|debug_shentong_zaohua_category",
			categories.Count)];
		id = selected[XjDeterministicHash.PositiveIndex(
			seed + 7919L,
			normalizedDaoTu + "|debug_shentong_zaohua_candidate",
			selected.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool TryResolveMappedXianJi(string daoTu, string gongFaName, out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		string[] pool = MapGetUpper(normalizedDaoTu);
		if (pool.Length == 0 || string.IsNullOrWhiteSpace(gongFaName))
		{
			id = string.Empty;
			return false;
		}

		int index = XjDeterministicHash.PositiveIndex(
			XjDeterministicHash.StableHash(gongFaName.Trim()),
			normalizedDaoTu + "|family_gongfa_mapped_xianji",
			pool.Length);
		id = pool[index];
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool IsAvailableForOrdinal(string daoTu, int ordinal, string id)
	{
		return IsAvailableForProgression(daoTu, ordinal, Array.Empty<string>(), false, id);
	}

	internal static bool IsAvailableForProgression(
		string daoTu,
		int ordinal,
		string[] existingIds,
		bool zhengWeiManifested,
		string id)
	{
		return IsAvailableForProgression(
			daoTu,
			ordinal,
			existingIds,
			zhengWeiManifested,
			true,
			id);
	}

	internal static bool IsAvailableForProgression(
		string daoTu,
		int ordinal,
		string[] existingIds,
		bool zhengWeiManifested,
		bool allowOtherPool,
		string id)
	{
		string normalizedId = (id ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedId) || Contains(existingIds, normalizedId))
		{
			return false;
		}

		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (IsQingXuan(normalizedDaoTu))
		{
			return Contains(MapGetUpper(normalizedDaoTu), normalizedId)
				|| Contains(MapGetLower(normalizedDaoTu), normalizedId);
		}
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			return false;
		}

		ResolvePoolWeights(
			normalizedDaoTu,
			ordinal,
			existingIds,
			zhengWeiManifested,
			out int ownWeight,
			out int lowerWeight,
			out int adjacentWeight,
			out int otherWeight,
			out string fixedAdjacentDaoTu);
		return (ownWeight > 0 && Contains(MapGetUpper(normalizedDaoTu), normalizedId))
			|| (lowerWeight > 0 && Contains(MapGetLower(normalizedDaoTu), normalizedId))
			|| (adjacentWeight > 0 && Contains(BuildAdjacentPool(normalizedDaoTu, fixedAdjacentDaoTu, Array.Empty<string>()), normalizedId))
			|| (allowOtherPool
				&& otherWeight > 0
				&& Contains(BuildOtherPool(normalizedDaoTu, Array.Empty<string>()), normalizedId));
	}

	private static void ResolvePoolWeights(
		string daoTu,
		int ordinal,
		string[] existingIds,
		bool zhengWeiManifested,
		out int ownWeight,
		out int lowerWeight,
		out int adjacentWeight,
		out int otherWeight,
		out string fixedAdjacentDaoTu)
	{
		ownWeight = 0;
		lowerWeight = 0;
		adjacentWeight = 0;
		otherWeight = 0;
		fixedAdjacentDaoTu = string.Empty;
		int currentCount = Math.Max(0, ordinal - 1);
		if (currentCount <= 2)
		{
			ownWeight = 1;
			return;
		}

		if (currentCount == 3)
		{
			ownWeight = zhengWeiManifested ? 50 : 70;
			lowerWeight = zhengWeiManifested ? 20 : 10;
			adjacentWeight = zhengWeiManifested ? 20 : 10;
			otherWeight = 10;
			return;
		}

		if (currentCount != 4)
		{
			return;
		}

		if (existingIds != null
			&& existingIds.Length >= 4
			&& TryGetAdjacentDaoTuForXianJi(daoTu, existingIds[3], out fixedAdjacentDaoTu))
		{
			ownWeight = 15;
			lowerWeight = 20;
			adjacentWeight = 55;
			otherWeight = 10;
			return;
		}

		AnalyzePoolCounts(daoTu, existingIds, 4, out int ownCount, out int lowerCount, out int adjacentCount, out int otherCount);
		if (otherCount == 0 && adjacentCount == 0 && lowerCount == 0 && ownCount >= 4)
		{
			ownWeight = 60;
			lowerWeight = 15;
			adjacentWeight = 15;
			otherWeight = 10;
			return;
		}

		if (otherCount == 0 && adjacentCount == 0 && lowerCount > 0)
		{
			ownWeight = 25;
			lowerWeight = 55;
			adjacentWeight = 10;
			otherWeight = 10;
			return;
		}

		if (zhengWeiManifested)
		{
			ownWeight = 30;
			lowerWeight = 30;
			adjacentWeight = 30;
			otherWeight = 10;
			return;
		}

		ownWeight = 55;
		lowerWeight = 20;
		adjacentWeight = 15;
		otherWeight = 10;
	}

	private static void AnalyzePoolCounts(
		string daoTu,
		string[] existingIds,
		int slotCount,
		out int ownCount,
		out int lowerCount,
		out int adjacentCount,
		out int otherCount)
	{
		ownCount = 0;
		lowerCount = 0;
		adjacentCount = 0;
		otherCount = 0;
		int limit = Math.Min(slotCount, existingIds?.Length ?? 0);
		for (int i = 0; i < limit; i++)
		{
			switch (GetPoolKind(daoTu, existingIds[i]))
			{
				case XjXianJiPoolKind.Native:
					ownCount++;
					break;
				case XjXianJiPoolKind.Lower:
					lowerCount++;
					break;
				case XjXianJiPoolKind.Adjacent:
					adjacentCount++;
					break;
				default:
					otherCount++;
					break;
			}
		}
	}

	private static List<string> BuildAdjacentPool(string daoTu, string fixedAdjacentDaoTu, string[] existingIds)
	{
		List<string> result = new List<string>();
		if (!string.IsNullOrWhiteSpace(fixedAdjacentDaoTu))
		{
			AddAvailable(result, MapGetUpper(fixedAdjacentDaoTu), existingIds);
			AddAvailable(result, MapGetLower(fixedAdjacentDaoTu), existingIds);
			return result;
		}

		if (AdjacentDaoTuMap.TryGetValue(daoTu, out string[] adjacentDaoTus))
		{
			for (int i = 0; i < adjacentDaoTus.Length; i++)
			{
				AddAvailable(result, MapGetUpper(adjacentDaoTus[i]), existingIds);
				AddAvailable(result, MapGetLower(adjacentDaoTus[i]), existingIds);
			}
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	private static List<string> BuildOtherPool(string daoTu, string[] existingIds)
	{
		HashSet<string> excludedDaoTus = new HashSet<string>(StringComparer.Ordinal) { daoTu };
		if (AdjacentDaoTuMap.TryGetValue(daoTu, out string[] adjacentDaoTus))
		{
			for (int i = 0; i < adjacentDaoTus.Length; i++)
			{
				excludedDaoTus.Add(adjacentDaoTus[i]);
			}
		}

		List<string> result = new List<string>();
		foreach (KeyValuePair<string, string[]> pair in DaoTuXianJiMap)
		{
			if (!excludedDaoTus.Contains(pair.Key))
			{
				AddAvailable(result, MapGetUpper(pair.Key), existingIds);
			}
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	private static List<string> BuildAvailablePool(string[] pool, string[] existingIds)
	{
		List<string> result = new List<string>();
		AddAvailable(result, pool, existingIds);
		return result;
	}

	private static void AddAvailable(List<string> result, string[] pool, string[] existingIds)
	{
		for (int i = 0; pool != null && i < pool.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(pool[i])
				&& !Contains(existingIds, pool[i])
				&& !result.Contains(pool[i]))
			{
				result.Add(pool[i]);
			}
		}
	}

	internal static bool IsAdjacentDaoTu(string sourceDaoTu, string targetDaoTu)
	{
		string source = NormalizeDaoTu(sourceDaoTu);
		string target = NormalizeDaoTu(targetDaoTu);
		if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target)
			|| !AdjacentDaoTuMap.TryGetValue(source, out string[] adjacent))
		{
			return false;
		}
		for (int i = 0; i < adjacent.Length; i++)
		{
			if (string.Equals(adjacent[i], target, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool TryGetAdjacentDaoTuForXianJi(string daoTu, string id, out string adjacentDaoTu)
	{
		adjacentDaoTu = string.Empty;
		if (!AdjacentDaoTuMap.TryGetValue(daoTu, out string[] adjacentDaoTus))
		{
			return false;
		}

		for (int i = 0; i < adjacentDaoTus.Length; i++)
		{
			if (Contains(MapGetUpper(adjacentDaoTus[i]), id)
				|| Contains(MapGetLower(adjacentDaoTus[i]), id))
			{
				adjacentDaoTu = adjacentDaoTus[i];
				return true;
			}
		}
		return false;
	}

	private static string[] MapGet(Dictionary<string, string[]> map, string key)
	{
		return map.TryGetValue(key ?? string.Empty, out string[] values) && values != null ? values : Array.Empty<string>();
	}

	private static bool TryPickQingXuanForProgression(
		string normalizedDaoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		out string id)
	{
		List<string> own = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
		if (own.Count > 0)
		{
			if ((existingIds == null || existingIds.Length == 0)
				&& Contains(own.ToArray(), "玄羊子"))
			{
				id = "玄羊子";
				return true;
			}
			if ((existingIds?.Length ?? 0) >= 4)
			{
				List<string> lowerForFinal = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
				if (lowerForFinal.Count > 0
					&& XjDeterministicHash.PositiveIndex(
						actorId + ordinal,
						normalizedDaoTu + "|qingxuan_yuwei_branch",
						100) < 24)
				{
					id = lowerForFinal[0];
					return !string.IsNullOrWhiteSpace(id);
				}
			}
			id = own[XjDeterministicHash.PositiveIndex(
				actorId + ordinal,
				normalizedDaoTu + "|qingxuan_upper",
				own.Count)];
			return !string.IsNullOrWhiteSpace(id);
		}

		List<string> lower = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
		if (lower.Count > 0)
		{
			id = lower[0];
			return !string.IsNullOrWhiteSpace(id);
		}

		id = string.Empty;
		return false;
	}

	private static bool IsQingXuan(string daoTu)
	{
		return string.Equals((daoTu ?? string.Empty).Trim(), "青宣", StringComparison.Ordinal);
	}

	private static string[] MapGetUpper(string key)
	{
		return MapGet(EffectiveUpperXianJiMap, key);
	}

	private static string[] MapGetLower(string key)
	{
		return MapGet(EffectiveLowerXianJiMap, key);
	}

	private static Dictionary<string, string[]> BuildEffectiveUpperMap()
	{
		Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string[]> pair in DaoTuXianJiMap)
		{
			result[pair.Key] = MergePreferred(MapGet(DocumentUpperXianJiMap, pair.Key), pair.Value);
		}

		foreach (KeyValuePair<string, string[]> pair in DocumentUpperXianJiMap)
		{
			if (!result.ContainsKey(pair.Key))
			{
				result[pair.Key] = Deduplicate(pair.Value);
			}
		}
		return result;
	}

	private static Dictionary<string, string[]> BuildEffectiveLowerMap()
	{
		Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string[]> pair in LowDaoTuXianJiMap)
		{
			string[] documentPool = MapGet(DocumentLowerXianJiMap, pair.Key);
			result[pair.Key] = documentPool.Length > 0 ? Deduplicate(documentPool) : Deduplicate(pair.Value);
		}

		foreach (KeyValuePair<string, string[]> pair in DocumentLowerXianJiMap)
		{
			if (!result.ContainsKey(pair.Key))
			{
				result[pair.Key] = Deduplicate(pair.Value);
			}
		}
		return result;
	}

	private static string[] MergePreferred(string[] preferred, string[] fallback)
	{
		List<string> values = new List<string>();
		AddDistinct(values, preferred);
		AddDistinct(values, fallback);
		return values.ToArray();
	}

	private static string[] Deduplicate(string[] values)
	{
		List<string> result = new List<string>();
		AddDistinct(result, values);
		return result.ToArray();
	}

	private static void AddDistinct(List<string> result, string[] values)
	{
		for (int i = 0; values != null && i < values.Length; i++)
		{
			string value = values[i]?.Trim() ?? string.Empty;
			if (!string.IsNullOrWhiteSpace(value) && !result.Contains(value))
			{
				result.Add(value);
			}
		}
	}

	private static string NormalizeDaoTu(string daoTu)
	{
		string text = (daoTu ?? string.Empty).Trim();
		return !string.IsNullOrWhiteSpace(text) && XjDaoTuVisibleTraitCatalog.TryResolveTraitId(text, out _)
			? text
			: string.Empty;
	}

	private static bool Contains(string[] pool, string value)
	{
		if (pool == null || string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		for (int i = 0; i < pool.Length; i++)
		{
			if (string.Equals(pool[i], value, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static bool Contains(List<string> pool, string value)
	{
		return pool != null && pool.Contains(value);
	}
}
