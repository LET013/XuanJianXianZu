using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjXianJiCatalog
{
	private const int MinimumRuntimePoolSize = XjXianJiState.MaxCount;

	private readonly struct PoolOwner
	{
		internal readonly string DaoTu;
		internal readonly bool IsUpper;

		internal PoolOwner(string daoTu, bool isUpper)
		{
			DaoTu = daoTu ?? string.Empty;
			IsUpper = isUpper;
		}
	}

	// 旧档异体字与已经废止的神通旧名统一归一；角色数据在正常对账时完成迁移。
	private static readonly Dictionary<string, string> XianJiAliasMap =
		new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ "罗刹海", "罗剎海" },
		// 0.9.7.5：三门旧下位名退出神通池；旧档统一迁移至重写后的下位形。
		{ "天下明", "照昧灯" },
		{ "掩弊服", "藏晦衣" },
		{ "天下革", "削故锋" },
		{ "剑道神通·二", "照寒锋" },
		{ "剑道神通·三", "斩青冥" },
		{ "剑道神通·四", "剑归元" },
		{ "剑道神通·五", "破天门" },
		{ "魇鬼阴", "飓鬼阴" },
		{ "问道锦", "间道锦" },
		{ "满烬煌", "满垠㷐" },
		{ "混铅华", "浥铅华" },
		{ "制养宜", "制飬宜" },
		{ "秘白录", "秘白汞" },
		{ "侯神殊", "候神殊" },
		{ "倏动勃", "僭劻勷" },
		{ "听曜辰", "听醒辰" },
		{ "东利山", "东羽山" },
		{ "西天璟", "西天塬" },
		{ "南悯水", "南惆水" }
	};

	private static readonly Dictionary<string, string[]> DaoTuXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "兑金", new[] { "位从孚", "君兑隅", "金窍心", "不穷锋", "杀收宫" } },
		{ "库金", new[] { "金销洞", "帑梁银", "百宝殿", "藏金地", "铜锈炁" } },
		{ "庚金", new[] { "今去故", "再折毁", "镂金石", "天金冑", "墓门棘" } },
		{ "齐金", new[] { "天齐满", "金戈阵", "肃杀令", "齐天律", "万军令" } },
		{ "逍金", new[] { "逍遥游", "望商锋", "御金行", "无羁绊", "敛锋芒" } },
		// 服气长庚果位建立后，紫府金丹道对剑意的五种重新解释。
		// 除〖意堪身〗外四项均为模组演绎，统一由特殊研究链自行创造。
		{ "长庚", new[] { "意堪身", "照寒锋", "斩青冥", "剑归元", "破天门" } },
		{ "正木", new[] { "木成方", "位从专", "见查语", "背南行", "万古青" } },
		{ "集木", new[] { "祸延生", "隼就栖", "诸蓼会", "凌云木", "妄诞林" } },
		{ "角木", new[] { "余养性", "潇重林", "黎运春", "乙木全", "青龙气" } },
		{ "更木", new[] { "病前春", "枯荣道", "更迭术", "万古新", "新陈变" } },
		{ "保木", new[] { "蕴长生", "万劫不", "古盘根", "森罗相", "栋梁材" } },
		{ "坎水", new[] { "溪上翁", "浩瀚海", "位从险", "长云暗", "恨江去", "入坎窞" } },
		{ "府水", new[] { "朝寒雨", "合黎渊", "宿穷冬", "广浚湖" } },
		{ "合水", new[] { "归流处", "谶在兹", "广准圣", "诸合还", "妖渎河" } },
		{ "牝水", new[] { "往生泉", "佞无晨", "谿谷会", "参玄臟" } },
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
		{ "宣土", new[] { "神用命", "训浚明", "宣威仪", "德广天" } },
		{ "青宣", new[] { "玄羊子", "伏青山", "青宣岳", "上岩神", "观地冥" } },
		{ "太阴", new[] { "湖月秋", "诣太素", "结璘章", "仪对影", "月光府" } },
		{ "少阴", new[] { "香俱沉", "太冲观", "调杼柚", "广寒敕", "蟾宫折" } },
		{ "厥阴", new[] { "不紫衣", "不二舆", "青鸾唳", "归藏演", "厥阴主" } },
		{ "太阳", new[] { "分阳钗", "郁仪文", "耀尘世", "极盛御", "帝冕冠" } },
		{ "少阳", new[] { "邪绝求", "奉东君", "目骋怀", "青阳焕", "残阳泣" } },
		{ "明阳", new[] { "昭澈心", "谒天门", "帝观元", "君蹈危", "赤断镞" } },
		{ "清炁", new[] { "清元风", "益清气", "浮云身", "空应散" } },
		{ "邃炁", new[] { "代行妨", "闇天殃", "逆人伦", "众生苦", "乱天理" } },
		{ "紫炁", new[] { "绕东山", "列紫篇", "坤辰修", "养生主" } },
		{ "真炁", new[] { "抱石眠", "霞羽客", "鹤挂衣", "授长生", "蜕凡尘" } },
		{ "寒炁", new[] { "松上雪", "入清听", "祢水寒", "沆砀满", "寒世天" } },
		{ "晞炁", new[] { "未阕华", "乞代夜", "郁燠苦", "庆垂轩", "议八辟" } },
		{ "瑞炁", new[] { "好功箓", "瑞气云", "祥云现", "福星照", "瑞炁祥" } },
		{ "煞炁", new[] { "不空劫", "罗剎海", "人中屠", "戮天元" } },
		{ "谪炁", new[] { "藏壑舟", "薄虞渊", "忘川歌", "惘乾坤", "盼天赐" } },
		{ "华炁", new[] { "冠灵旒", "十方界", "仪仗全", "光华现", "琉璃履" } },
		{ "上仪", new[] { "明心筵", "致缉熙", "射狩王", "观天地", "无极生" } },
		{ "下仪", new[] { "幽冥身", "轮回殿", "黄泉路", "忘川河", "了前尘" } },
		{ "玄雷", new[] { "靖平敕", "盛阳墟", "至阴墟", "养青冥" } },
		{ "霄雷", new[] { "冬雷声", "春惊蛰", "轻雷落", "震雷枢", "定周天" } },
		{ "元雷", new[] { "主煞仪", "元雷罚", "九霄动", "霄汉清", "玄雷劫" } }
	};

	private static readonly Dictionary<string, string[]> LowDaoTuXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "太阴", new[] { "不胜寒", "再圆阙", "广寒枝", "惊鹊乡", "授玄珠" } },
		{ "少阴", new[] { "霜降微", "幽谷兰", "凝冰魄", "潜渊鱼", "敛华光" } },
		{ "厥阴", new[] { "隐风谷", "藏木叶", "参疑室", "蛰虫沙", "藏晦衣" } },
		{ "太阳", new[] { "扶桑枝", "耀金乌", "赤霞冠", "明光铠", "炽阳砂" } },
		{ "少阳", new[] { "初晖草", "萌春芽", "晨露晞", "微明火", "启明星" } },
		{ "明阳", new[] { "照昧灯", "煌元关", "长明阶", "誓崤身", "顾署舆" } },
		// 渊照余位以本道下位神通补足。
		{ "渊照", new[] { "沉月纹", "照影符", "返景痕", "涵真珠", "无波鉴" } },
		{ "兑金", new[] { "白虎牙", "西极铁", "锋镝鸣", "霜刃寒", "金戈影" } },
		{ "庚金", new[] { "削故锋", "断金玉", "削铁泥", "刚锋气", "砺石髓" } },
		{ "齐金", new[] { "天金砂", "圆满轮", "无缺璧", "纯钧魄", "恒常性" } },
		{ "库金", new[] { "藏宝匣", "积财山", "聚珍釜", "纳川囊", "封存印" } },
		{ "逍金", new[] { "逍遥铁", "自在锋", "无拘刃", "云游剑", "散金尘" } },
		{ "长庚", new[] { "抱剑眠", "洗锋池", "养剑匣", "试剑石", "听剑鸣" } },
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
		{ "太阳", new[] { "分阳钗", "郁仪文" } },
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
		{ "保木", new[] { "神在隰" } },
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
		{ "谪炁", new[] { "藏壑舟", "薄虞渊", "忘川歌", "惘乾坤", "盼天赐" } },
		{ "华炁", new[] { "冠灵旒", "十方界" } },
		{ "上仪", new[] { "明心筵", "致缉熙", "射狩王" } },
		{ "玄雷", new[] { "靖平敕", "至阳嘘", "神宫誓", "律演威", "伐封坛" } },
		{ "霄雷", new[] { "冬雷声", "春惊蛰", "轻雷落", "斡动紫" } },
		{ "元雷", new[] { "主煞仪" } }
	};

	private static readonly Dictionary<string, string[]> DocumentLowerXianJiMap = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		{ "太阴", new[] { "授玄珠", "不胜寒", "再圆阙", "惊鹊乡", "夜光府" } },
		{ "厥阴", new[] { "藏晦衣", "利异臣", "参疑室" } },
		{ "太阳", new[] { "视天统" } },
		{ "明阳", new[] { "照昧灯", "煌元关", "长明阶", "誓崤身", "顾署舆" } },
		{ "兑金", new[] { "请两忘" } },
		{ "庚金", new[] { "削故锋", "金兽羽" } },
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
		{ "元雷", new[] { "脱煞胎" } },
		{ "谪炁", new[] { "谪仙尘", "贬落烟", "流放雾", "罪愆气", "凡尘染" } }
	};

	// 截图表内条目先建立唯一的“道途 + 上/下位”所有权。运行补足项只能
	// 填入未被表内条目占用的位置，不能把已知神通重新塞进其他道途或另一类池。
	private static readonly Dictionary<string, PoolOwner> SourceOwnershipMap = BuildSourceOwnershipMap();
	private static readonly Dictionary<string, string[]> EffectiveUpperXianJiMap = BuildEffectiveUpperMap();
	private static readonly Dictionary<string, string[]> EffectiveLowerXianJiMap = BuildEffectiveLowerMap();
	private static readonly Dictionary<string, PoolOwner> EffectiveOwnershipMap = BuildEffectiveOwnershipMap();
	private static readonly XjShenTongRegistry ShenTongRegistry =
		XjShenTongRegistry.Build(EffectiveUpperXianJiMap, EffectiveLowerXianJiMap);

	// 道途近邻统一由 XjDaoTuRelationCatalog 维护，神通三池不再私有复制关系表。

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
		if (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			id = string.Empty;
			return false;
		}
		if (IsQingXuan(normalizedDaoTu))
		{
			return TryPickQingXuanForProgression(normalizedDaoTu, ordinal, actorId, existingIds, zhengWeiManifested, out id);
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

		// 池是否开放由当前仙基结构决定，家学/权柄偏置只能在“已经开放”的池之间调权，
		// 不能把结构上已经归零的池重新打开。否则正果已有主时 ownWeight=0 仍可能被
		// 家学活力加回本道上位，重新制造五本道直冲果位的倒挂。渊照则继续完全禁用家学加权。
		bool allowOwnByStructure = ownWeight > 0;
		bool allowLowerByStructure = lowerWeight > 0;
		bool allowAdjacentByStructure = adjacentWeight > 0;
		bool allowOtherByStructure = otherWeight > 0;
		if (!IsYuanZhao(normalizedDaoTu))
		{
			XjDaoLineageStateRegistry.ApplyShenTongBias(
				normalizedDaoTu, ordinal, ref ownWeight, ref lowerWeight, ref adjacentWeight, ref otherWeight);
			if (!allowOwnByStructure) ownWeight = 0;
			if (!allowLowerByStructure) lowerWeight = 0;
			if (!allowAdjacentByStructure) adjacentWeight = 0;
			if (!allowOtherByStructure) otherWeight = 0;
		}

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

		// 第4、5门的结构远亲池固定占原始权重，不因三类常规池缺项而被放大。
		// 该池已经过滤掉完全无拓扑外道；抽中后只可能形成有明确道论依据的远闰。
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

		id = ordinal >= 3
			? PickLineageWeightedCandidate(selected, actorId, ordinal, normalizedDaoTu, "xianji_candidate")
			: selected[XjDeterministicHash.PositiveIndex(
				actorId + ordinal,
				normalizedDaoTu + "|xianji_candidate",
				selected.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	private static string PickLineageWeightedCandidate(
		List<string> candidates,
		long actorId,
		int ordinal,
		string sourceDaoTu,
		string salt)
	{
		if (candidates == null || candidates.Count == 0) return string.Empty;
		int totalWeight = 0;
		int[] weights = new int[candidates.Count];
		for (int i = 0; i < candidates.Count; i++)
		{
			string candidate = candidates[i];
			string owner = sourceDaoTu;
			if (TryResolveOwningDaoTu(candidate, out string resolvedOwner)
				&& !string.IsNullOrWhiteSpace(resolvedOwner)) owner = resolvedOwner;
			int weight = Math.Max(1, XjDaoLineageStateRegistry.ResolveShenTongCandidateWeight(
				owner, candidate, out _, out _));
			weights[i] = weight;
			totalWeight += weight;
		}
		if (totalWeight <= 0) return candidates[0];
		int roll = XjDeterministicHash.PositiveIndex(
			actorId + ordinal, sourceDaoTu + "|" + salt + "|lineage", totalWeight);
		for (int i = 0; i < candidates.Count; i++)
		{
			if (roll < weights[i]) return candidates[i];
			roll -= weights[i];
		}
		return candidates[candidates.Count - 1];
	}

	internal static bool TryPickUpperForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			id = string.Empty;
			return false;
		}
		List<string> upper = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
		if (upper.Count == 0)
		{
			id = string.Empty;
			return false;
		}

		id = ordinal >= 3
			? PickLineageWeightedCandidate(upper, actorId, ordinal, normalizedDaoTu, "daozhu_upper_xianji")
			: upper[XjDeterministicHash.PositiveIndex(
				actorId + ordinal,
				normalizedDaoTu + "|daozhu_upper_xianji",
				upper.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	/// <summary>
	/// 道主优先取本道途上位神通；只有上位池已经去重耗尽后，
	/// 才从本道途下位与相邻道途上位池中补取，永不开放其他池。
	/// </summary>
	/// <summary>
	/// 上修为余位继承人定向补基时使用：只从本道途下位池选一门。
	/// </summary>
	internal static bool TryPickLowerForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		List<string> lower = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
		if (lower.Count == 0)
		{
			id = string.Empty;
			return false;
		}
		id = lower[XjDeterministicHash.PositiveIndex(
			actorId + ordinal, normalizedDaoTu + "|patron_lower_xianji", lower.Count)];
		return !string.IsNullOrWhiteSpace(id);
	}

	internal static bool TryPickDaoZhuForProgression(
		string daoTu,
		int ordinal,
		long actorId,
		string[] existingIds,
		bool zhengWeiManifested,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			id = string.Empty;
			return false;
		}
		if (TryPickUpperForProgression(normalizedDaoTu, ordinal, actorId, existingIds, out id))
		{
			return true;
		}
		_ = zhengWeiManifested;
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

	/// <summary>
	/// 道胎之姿的统一授予闸门。只要本道途仍有未掌握的上位神通，
	/// 下位与相邻道途上位就不得提前进入；其他道途神通在任何情况下都拒绝。
	/// </summary>
	internal static bool IsDaoZhuGrantAllowed(string daoTu, string id, string[] existingIds)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
			&& !XjFuQiSwordWorldState.IsEstablished)
		{
			return false;
		}
		XjXianJiPoolKind kind = GetPoolKind(normalizedDaoTu, id);
		if (kind == XjXianJiPoolKind.Other) return false;
		if (kind == XjXianJiPoolKind.Native) return true;
		if (kind != XjXianJiPoolKind.Lower && kind != XjXianJiPoolKind.Adjacent) return false;

		return !TryPickUpperForProgression(
			normalizedDaoTu,
			(existingIds?.Length ?? 0) + 1,
			0L,
			existingIds ?? Array.Empty<string>(),
			out _);
	}

	internal static bool TryResolveOwningDaoTu(string id, out string daoTu)
	{
		return ShenTongRegistry.TryResolveOwner(NormalizeXianJiId(id), out daoTu, out _);
	}

	internal static bool TryGetDefinition(string id, out XjShenTongDefinition definition)
	{
		return ShenTongRegistry.TryGet(NormalizeXianJiId(id), out definition);
	}

	/// <summary>
	/// 从神通自身的唯一注册表反查“五门全上位”的真实所属道途。
	/// 不读取角色当前道途，专门用于纠正旧档中已经被错误改写成闰位显道的金丹。
	/// </summary>
	internal static bool TryResolvePureUpperDaoTu(in XjXianJiState state, out string daoTu)
	{
		daoTu = string.Empty;
		if (state.Count != XjXianJiState.MaxCount
			|| state.Ids == null
			|| state.Ids.Length < XjXianJiState.MaxCount)
		{
			return false;
		}

		string resolved = string.Empty;
		for (int i = 0; i < XjXianJiState.MaxCount; i++)
		{
			if (!TryGetDefinition(state.Ids[i], out XjShenTongDefinition definition)
				|| definition == null
				|| definition.Tier != XjShenTongTier.Upper)
			{
				return false;
			}

			string owner = NormalizeDaoTu(definition.DaoTuId);
			if (string.IsNullOrWhiteSpace(owner))
			{
				return false;
			}
			if (resolved.Length == 0)
			{
				resolved = owner;
			}
			else if (!string.Equals(resolved, owner, StringComparison.Ordinal))
			{
				return false;
			}
		}

		daoTu = resolved;
		return daoTu.Length > 0;
	}

	internal static IReadOnlyCollection<XjShenTongDefinition> ReadDefinitions()
	{
		return ShenTongRegistry.ReadDefinitions();
	}

	internal static XjXianJiPoolKind GetPoolKind(string daoTu, string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| !ShenTongRegistry.TryResolveOwner(NormalizeXianJiId(id), out string ownerDaoTu, out XjShenTongTier tier))
		{
			return XjXianJiPoolKind.Other;
		}

		if (string.Equals(ownerDaoTu, normalizedDaoTu, StringComparison.Ordinal))
		{
			return tier == XjShenTongTier.Upper ? XjXianJiPoolKind.Native : XjXianJiPoolKind.Lower;
		}

		// 闰位外道只能由“对方上位神通”构成。相邻道途的下位神通不再算 Adjacent，
		// 统一视为 Other，从池分类这一层就阻止其进入任何闰位生成/传法/手动补录链路。
		return tier == XjShenTongTier.Upper && IsAdjacentDaoTu(normalizedDaoTu, ownerDaoTu)
			? XjXianJiPoolKind.Adjacent
			: XjXianJiPoolKind.Other;
	}

	/// <summary>
	/// 相邻神通替换辅助：把异道神通改造成当前道途可接受的
	/// 本道途上位、本道途下位或相邻道途上位神通。三类非空池等概率选取，
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
		string normalizedId = NormalizeXianJiId(id);
		if (string.IsNullOrWhiteSpace(normalizedId) || Contains(existingIds, normalizedId))
		{
			return false;
		}

		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (IsQingXuan(normalizedDaoTu))
		{
			if (Contains(MapGetUpper(normalizedDaoTu), normalizedId)) return true;
			// 青宣果位尚未显现时，首位空证者必须走完整五上位神通；
			// 下阳元只在既有果位之后作为余位末基开放。
			return zhengWeiManifested
				&& ordinal >= XjXianJiState.MaxCount
				&& Contains(MapGetLower(normalizedDaoTu), normalizedId);
		}
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
				&& !XjFuQiSwordWorldState.IsEstablished))
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
			|| (adjacentWeight > 0 && Contains(BuildAdjacentPool(normalizedDaoTu, fixedAdjacentDaoTu, existingIds), normalizedId))
			|| (allowOtherPool
				&& otherWeight > 0
				&& Contains(BuildOtherPool(normalizedDaoTu, existingIds), normalizedId));
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
		if (IsYuanZhao(daoTu))
		{
			ResolveYuanZhaoPoolWeights(currentCount, existingIds, zhengWeiManifested,
				out ownWeight, out lowerWeight, out adjacentWeight, out otherWeight, out fixedAdjacentDaoTu);
			return;
		}
		if (currentCount <= 2)
		{
			ownWeight = 1;
			return;
		}

		if (currentCount == 3)
		{
			// 前三门已经固定为本道上位，第四门才是真正的“求位分岔”。
			// 果位空缺时也不再 70% 继续锁死纯本道路线；余位、近邻闰位应当是更常见的派生出口。
			ownWeight = zhengWeiManifested ? 20 : 45;
			lowerWeight = zhengWeiManifested ? 38 : 25;
			adjacentWeight = zhengWeiManifested ? 37 : 25;
			otherWeight = 5;
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
			// 第四门既已落入一个近邻显道，第五门只在“本道上位 / 同一近邻上位”之间收束，
			// 两种结果都保持合法闰位结构，不再横插下位或另一条远道制造死局。
			ownWeight = 35;
			adjacentWeight = 65;
			return;
		}

		AnalyzePoolCounts(daoTu, existingIds, 4, out int ownCount, out int lowerCount, out int adjacentCount, out int otherCount);
		if (otherCount == 0 && adjacentCount == 0 && lowerCount == 0 && ownCount >= 4)
		{
			// 四门纯本道之后，第五门才决定是否真正冲正果。正果已有主时彻底关闭第五门本道池，
			// 强制向余/闰收束；正果空缺时也只保留三成继续冲果，避免“果比余闰更常见”。
			ownWeight = zhengWeiManifested ? 0 : 30;
			lowerWeight = zhengWeiManifested ? 50 : 35;
			adjacentWeight = zhengWeiManifested ? 45 : 30;
			otherWeight = 5;
			return;
		}

		if (otherCount == 0 && adjacentCount == 0 && lowerCount > 0)
		{
			// 第四门既已定为本道下位，第五门只在本道上/下位中收束，稳定形成余位。
			ownWeight = 35;
			lowerWeight = 65;
			return;
		}

		if (otherCount > 0 && adjacentCount == 0 && lowerCount == 0)
		{
			// Other 池在 0.9.9.3 起只包含有明确结构关系的远亲道途。第四门已经选定远亲后，
			// BuildOtherPool 会锁定同一显道，因此第五门只在本道/该远亲之间收束远闰。
			ownWeight = 35;
			otherWeight = 65;
			return;
		}

		if (zhengWeiManifested)
		{
			ownWeight = 0;
			lowerWeight = 50;
			adjacentWeight = 45;
			otherWeight = 5;
			return;
		}

		ownWeight = 30;
		lowerWeight = 35;
		adjacentWeight = 30;
		otherWeight = 5;
	}

	/// <summary>
	/// 渊照专属五神通结构：前三门固定本道上位；后两门只允许本道上位、本道下位、
	/// 太阴/坎水近邻。出现下位后只继续本道上/下位以收束余位；出现近邻后锁定同一
	/// 近邻以收束闰位。正果已有主且前四门全为本道上位时，第五门强制从下位/近邻补足。
	/// </summary>
	private static void ResolveYuanZhaoPoolWeights(
		int currentCount, string[] existingIds, bool zhengWeiManifested,
		out int ownWeight, out int lowerWeight, out int adjacentWeight, out int otherWeight,
		out string fixedAdjacentDaoTu)
	{
		ownWeight = 0;
		lowerWeight = 0;
		adjacentWeight = 0;
		otherWeight = 0;
		fixedAdjacentDaoTu = string.Empty;

		if (currentCount <= 2)
		{
			ownWeight = 1;
			return;
		}
		if (currentCount == 3)
		{
			ownWeight = zhengWeiManifested ? 20 : 45;
			lowerWeight = zhengWeiManifested ? 40 : 27;
			adjacentWeight = zhengWeiManifested ? 40 : 28;
			return;
		}
		if (currentCount != 4) return;

		AnalyzePoolCounts(XjYuanZhaoKongZhengEvent.DaoTu, existingIds, 4,
			out int ownCount, out int lowerCount, out int adjacentCount, out int otherCount);
		if (otherCount > 0) return;

		if (adjacentCount > 0)
		{
			for (int i = 0; existingIds != null && i < existingIds.Length && i < 4; i++)
			{
				if (TryGetAdjacentDaoTuForXianJi(XjYuanZhaoKongZhengEvent.DaoTu, existingIds[i], out string adjacentDaoTu))
				{
					fixedAdjacentDaoTu = adjacentDaoTu;
					break;
				}
			}
			ownWeight = 35;
			adjacentWeight = 65;
			return;
		}
		if (lowerCount > 0)
		{
			ownWeight = 35;
			lowerWeight = 65;
			return;
		}
		if (ownCount >= 4)
		{
			if (zhengWeiManifested)
			{
				lowerWeight = 50;
				adjacentWeight = 50;
			}
			else
			{
				ownWeight = 30;
				lowerWeight = 35;
				adjacentWeight = 35;
			}
		}
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
		// Adjacent 是闰位专用外道池：全局只收相邻道途“上位神通”。
		// 下位神通只服务其本道余位，任何道途都不得再用相邻下位拼成闰位。
		List<string> result = new List<string>();
		if (!string.IsNullOrWhiteSpace(fixedAdjacentDaoTu))
		{
			if (!XjDaoTuRelationCatalog.IsDirectAdjacent(daoTu, fixedAdjacentDaoTu)) return result;
			AddAvailable(result, MapGetUpper(fixedAdjacentDaoTu), existingIds);
			return result;
		}

		IReadOnlyList<string> adjacentDaoTus = XjDaoTuRelationCatalog.GetDirectNeighbors(daoTu);
		for (int i = 0; i < adjacentDaoTus.Count; i++)
		{
			AddAvailable(result, MapGetUpper(adjacentDaoTus[i]), existingIds);
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	private static List<string> BuildOtherPool(string daoTu, string[] existingIds)
	{
		// Other 不再等于“天下任意外道”。普通闰位只能从有明确道论结构的远亲中取神通：
		// 同根远支、十二炁对炁、五德映照。完全无拓扑关系应走真正空证新道玩法，而不是小空证成闰。
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		IReadOnlyList<string> structuredRemote = XjDaoTuRelationCatalog.GetStructuredRemoteNeighbors(normalizedDaoTu);
		HashSet<string> allowedDaoTus = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < structuredRemote.Count; i++)
		{
			string candidate = NormalizeDaoTu(structuredRemote[i]);
			if (candidate.Length == 0
				|| XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(normalizedDaoTu, candidate)) continue;
			allowedDaoTus.Add(candidate);
		}

		// 若第四门已经选中一个结构远亲，第五门锁定同一显道，避免两个远亲混成无门。
		string fixedOwner = string.Empty;
		for (int i = 0; existingIds != null && i < existingIds.Length; i++)
		{
			string existing = NormalizeXianJiId(existingIds[i]);
			if (existing.Length == 0
				|| !ShenTongRegistry.TryResolveOwner(existing, out string owner, out XjShenTongTier tier)
				|| tier != XjShenTongTier.Upper) continue;
			string normalizedOwner = NormalizeDaoTu(owner);
			if (!allowedDaoTus.Contains(normalizedOwner))
			{
				// 旧档若已经混入一个既非本道、也非近邻/结构远亲的上位外道，
				// 不再继续补另一条“可用远亲”伪装成合法闰位；保持无门等待其它修复/转路。
				if (!string.Equals(normalizedOwner, normalizedDaoTu, StringComparison.Ordinal)
					&& !XjDaoTuRelationCatalog.IsDirectAdjacent(normalizedDaoTu, normalizedOwner))
				{
					return new List<string>();
				}
				continue;
			}
			if (fixedOwner.Length == 0) fixedOwner = normalizedOwner;
			else if (!string.Equals(fixedOwner, normalizedOwner, StringComparison.Ordinal))
			{
				// 已经存在两条不同远亲说明旧数据本身不唯一，不再继续补第三门外道。
				return new List<string>();
			}
		}
		if (fixedOwner.Length > 0)
		{
			allowedDaoTus.Clear();
			allowedDaoTus.Add(fixedOwner);
		}

		List<string> result = new List<string>();
		foreach (XjShenTongDefinition definition in ShenTongRegistry.ReadDefinitions())
		{
			string owner = NormalizeDaoTu(definition.DaoTuId);
			if (definition.Tier == XjShenTongTier.Upper
				&& allowedDaoTus.Contains(owner)
				&& (!XjZiJinSwordDaoCatalog.IsLongGeng(owner) || XjFuQiSwordWorldState.IsEstablished)
				&& !Contains(existingIds, definition.Id)
				&& !result.Contains(definition.Id))
			{
				result.Add(definition.Id);
			}
		}
		result.Sort(StringComparer.Ordinal);
		return result;
	}

	/// <summary>
	/// 为手动金丹补录从指定池精确取一门神通。该入口不做权重抽取，
	/// 由调用方明确限定为本道途上位、本道途下位或同一相邻道途“上位”池。
	/// </summary>
	internal static bool TryPickFromPool(
		string daoTu,
		XjXianJiPoolKind poolKind,
		long seed,
		string[] existingIds,
		string preferredAdjacentDaoTu,
		out string id)
	{
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| (string.Equals(normalizedDaoTu, XjZiJinSwordDaoCatalog.DaoTu, StringComparison.Ordinal)
				&& !XjFuQiSwordWorldState.IsEstablished))
		{
			id = string.Empty;
			return false;
		}

		List<string> candidates;
		switch (poolKind)
		{
		case XjXianJiPoolKind.Native:
			candidates = BuildAvailablePool(MapGetUpper(normalizedDaoTu), existingIds);
			break;
		case XjXianJiPoolKind.Lower:
			candidates = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
			break;
		case XjXianJiPoolKind.Adjacent:
			candidates = BuildAdjacentPool(normalizedDaoTu, NormalizeDaoTu(preferredAdjacentDaoTu), existingIds);
			break;
		default:
			candidates = BuildOtherPool(normalizedDaoTu, existingIds);
			break;
		}
		if (candidates.Count == 0)
		{
			id = string.Empty;
			return false;
		}

		candidates.Sort(StringComparer.Ordinal);
		id = candidates[XjDeterministicHash.PositiveIndex(
			seed,
			normalizedDaoTu + "|manual_exact_pool|" + poolKind + "|" + (preferredAdjacentDaoTu ?? string.Empty),
			candidates.Count)];
		return !string.IsNullOrWhiteSpace(id);
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
		return source.Length > 0 && target.Length > 0
			&& XjDaoTuRelationCatalog.IsDirectAdjacent(source, target);
	}

	internal static bool TryGetAdjacentDaoTuForXianJi(string daoTu, string id, out string adjacentDaoTu)
	{
		adjacentDaoTu = string.Empty;
		string normalizedDaoTu = NormalizeDaoTu(daoTu);
		string normalizedId = NormalizeXianJiId(id);
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| string.IsNullOrWhiteSpace(normalizedId)
			|| !ShenTongRegistry.TryResolveOwner(normalizedId, out string ownerDaoTu, out XjShenTongTier tier)
			|| tier != XjShenTongTier.Upper
			|| !IsAdjacentDaoTu(normalizedDaoTu, ownerDaoTu))
		{
			return false;
		}

		adjacentDaoTu = ownerDaoTu;
		return true;
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
		bool zhengWeiManifested,
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
			if (zhengWeiManifested && (existingIds?.Length ?? 0) >= 4)
			{
				// 青宣首位空证仍保持五上位的专属开道史实；但正果已有主后，
				// 第五门必须转入下阳元收束余位，不再让 76% 后继继续撞一个已占正果。
				List<string> lowerForFinal = BuildAvailablePool(MapGetLower(normalizedDaoTu), existingIds);
				if (lowerForFinal.Count > 0)
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

	private static bool IsYuanZhao(string daoTu)
	{
		return string.Equals(NormalizeDaoTu(daoTu), XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal);
	}

	private static string[] MapGetUpper(string key)
	{
		return ShenTongRegistry.GetPool(key, XjShenTongTier.Upper);
	}

	private static string[] MapGetLower(string key)
	{
		return ShenTongRegistry.GetPool(key, XjShenTongTier.Lower);
	}

	internal static string[] GetUpperPool(string daoTu)
	{
		string[] values = MapGetUpper(NormalizeDaoTu(daoTu));
		string[] copy = new string[values.Length];
		Array.Copy(values, copy, values.Length);
		return copy;
	}

	internal static string[] GetLowerPool(string daoTu)
	{
		string[] values = MapGetLower(NormalizeDaoTu(daoTu));
		string[] copy = new string[values.Length];
		Array.Copy(values, copy, values.Length);
		return copy;
	}

	private static Dictionary<string, PoolOwner> BuildSourceOwnershipMap()
	{
		Dictionary<string, PoolOwner> result = new Dictionary<string, PoolOwner>(StringComparer.Ordinal);
		RegisterSourceOwnership(result, DocumentUpperXianJiMap, isUpper: true);
		RegisterSourceOwnership(result, DocumentLowerXianJiMap, isUpper: false);

		string[] bingGuDaoTus = XjZiJinUpperShenTongCatalog.GetDaoTuDisplayNames();
		for (int i = 0; i < bingGuDaoTus.Length; i++)
		{
			string daoTu = bingGuDaoTus[i];
			string[] names = XjZiJinUpperShenTongCatalog.GetSourceListedNamesByDaoTu(daoTu);
			RegisterSourceOwnership(result, daoTu, names, isUpper: true);
		}
		return result;
	}

	private static void RegisterSourceOwnership(
		Dictionary<string, PoolOwner> result,
		Dictionary<string, string[]> source,
		bool isUpper)
	{
		foreach (KeyValuePair<string, string[]> pair in source)
		{
			RegisterSourceOwnership(result, pair.Key, pair.Value, isUpper);
		}
	}

	private static void RegisterSourceOwnership(
		Dictionary<string, PoolOwner> result,
		string daoTu,
		string[] names,
		bool isUpper)
	{
		for (int i = 0; names != null && i < names.Length; i++)
		{
			string name = NormalizeXianJiId(names[i]);
			if (string.IsNullOrWhiteSpace(name)) continue;

			// 表内若出现重复，先登记的上位条目优先；下位表不能反向覆盖。
			if (!result.ContainsKey(name))
			{
				result[name] = new PoolOwner(daoTu, isUpper);
			}
		}
	}

	private static Dictionary<string, string[]> BuildEffectiveUpperMap()
	{
		Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string[]> pair in DaoTuXianJiMap)
		{
			result[pair.Key] = BuildEffectivePool(
				pair.Key,
				MapGet(DocumentUpperXianJiMap, pair.Key),
				pair.Value,
				isUpper: true);
		}

		foreach (KeyValuePair<string, string[]> pair in DocumentUpperXianJiMap)
		{
			if (!result.ContainsKey(pair.Key))
			{
				result[pair.Key] = BuildEffectivePool(
					pair.Key,
					pair.Value,
					Array.Empty<string>(),
					isUpper: true);
			}
		}

		// 并古截图条目与运行补足项分开处理：原表名称先锁定归属，
		// 只有不足五项的分支才从本道途既有补足池中补齐。
		string[] bingGuDaoTus = XjZiJinUpperShenTongCatalog.GetDaoTuDisplayNames();
		for (int i = 0; i < bingGuDaoTus.Length; i++)
		{
			string daoTu = bingGuDaoTus[i];
			string[] documented = XjZiJinUpperShenTongCatalog.GetSourceListedNamesByDaoTu(daoTu);
			string[] fallback = XjZiJinUpperShenTongCatalog.GetSupplementNamesByDaoTu(daoTu);
			result[daoTu] = BuildEffectivePool(daoTu, documented, fallback, isUpper: true);
		}
		return result;
	}

	private static Dictionary<string, string[]> BuildEffectiveLowerMap()
	{
		Dictionary<string, string[]> result = new Dictionary<string, string[]>(StringComparer.Ordinal);
		foreach (KeyValuePair<string, string[]> pair in LowDaoTuXianJiMap)
		{
			result[pair.Key] = BuildEffectivePool(
				pair.Key,
				MapGet(DocumentLowerXianJiMap, pair.Key),
				pair.Value,
				isUpper: false);
		}

		foreach (KeyValuePair<string, string[]> pair in DocumentLowerXianJiMap)
		{
			if (!result.ContainsKey(pair.Key))
			{
				result[pair.Key] = BuildEffectivePool(
					pair.Key,
					pair.Value,
					Array.Empty<string>(),
					isUpper: false);
			}
		}

		string[] bingGuDaoTus = XjZiJinUpperShenTongCatalog.GetDaoTuDisplayNames();
		for (int i = 0; i < bingGuDaoTus.Length; i++)
		{
			string daoTu = bingGuDaoTus[i];
			result[daoTu] = BuildEffectivePool(
				daoTu,
				Array.Empty<string>(),
				XjZiJinUpperShenTongCatalog.GetLowerNamesByDaoTu(daoTu),
				isUpper: false);
		}
		return result;
	}

	private static string[] BuildEffectivePool(
		string daoTu,
		string[] sourceListed,
		string[] fallback,
		bool isUpper)
	{
		List<string> values = new List<string>();
		AddSourceListed(values, daoTu, sourceListed, isUpper);
		int targetCount = Math.Max(MinimumRuntimePoolSize, values.Count);
		AddSupplement(values, daoTu, fallback, isUpper, targetCount);
		return values.ToArray();
	}

	private static void AddSourceListed(
		List<string> result,
		string daoTu,
		string[] values,
		bool isUpper)
	{
		for (int i = 0; values != null && i < values.Length; i++)
		{
			string value = NormalizeXianJiId(values[i]);
			if (string.IsNullOrWhiteSpace(value)
				|| result.Contains(value)
				|| !SourceOwnershipMap.TryGetValue(value, out PoolOwner owner)
				|| !string.Equals(owner.DaoTu, daoTu, StringComparison.Ordinal)
				|| owner.IsUpper != isUpper)
			{
				continue;
			}
			result.Add(value);
		}
	}

	private static void AddSupplement(
		List<string> result,
		string daoTu,
		string[] values,
		bool isUpper,
		int targetCount)
	{
		for (int i = 0; values != null && i < values.Length && result.Count < targetCount; i++)
		{
			string value = NormalizeXianJiId(values[i]);
			if (string.IsNullOrWhiteSpace(value) || result.Contains(value)) continue;

			if (SourceOwnershipMap.TryGetValue(value, out PoolOwner owner)
				&& (!string.Equals(owner.DaoTu, daoTu, StringComparison.Ordinal)
					|| owner.IsUpper != isUpper))
			{
				continue;
			}
			result.Add(value);
		}
	}

	private static Dictionary<string, PoolOwner> BuildEffectiveOwnershipMap()
	{
		Dictionary<string, PoolOwner> result = new Dictionary<string, PoolOwner>(StringComparer.Ordinal);
		RegisterEffectiveOwnership(result, EffectiveUpperXianJiMap, isUpper: true);
		RegisterEffectiveOwnership(result, EffectiveLowerXianJiMap, isUpper: false);
		return result;
	}

	private static void RegisterEffectiveOwnership(
		Dictionary<string, PoolOwner> result,
		Dictionary<string, string[]> pools,
		bool isUpper)
	{
		List<string> daoTus = new List<string>(pools.Keys);
		daoTus.Sort(StringComparer.Ordinal);
		for (int daoTuIndex = 0; daoTuIndex < daoTus.Count; daoTuIndex++)
		{
			string daoTu = daoTus[daoTuIndex];
			string[] names = MapGet(pools, daoTu);
			for (int i = 0; i < names.Length; i++)
			{
				string name = NormalizeXianJiId(names[i]);
				if (string.IsNullOrWhiteSpace(name)) continue;

				// 表内所有权始终优先。补足项若意外重名，则保持先登记的一方，
				// 不再通过字典遍历顺序把同一神通映射到另一道途。
				if (SourceOwnershipMap.TryGetValue(name, out PoolOwner sourceOwner))
				{
					result[name] = sourceOwner;
				}
				else if (!result.ContainsKey(name))
				{
					result[name] = new PoolOwner(daoTu, isUpper);
				}
			}
		}
	}

	internal static string[] GetPoolValidationIssues()
	{
		List<string> issues = new List<string>(ShenTongRegistry.GetValidationIssues());
		ValidatePools(issues, EffectiveUpperXianJiMap, isUpper: true);
		ValidatePools(issues, EffectiveLowerXianJiMap, isUpper: false);
		return issues.ToArray();
	}

	private static void ValidatePools(
		List<string> issues,
		Dictionary<string, string[]> pools,
		bool isUpper)
	{
		foreach (KeyValuePair<string, string[]> pair in pools)
		{
			HashSet<string> local = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; pair.Value != null && i < pair.Value.Length; i++)
			{
				string name = NormalizeXianJiId(pair.Value[i]);
				if (!local.Add(name))
				{
					issues.Add(pair.Key + (isUpper ? "上位" : "下位") + "池重复：" + name);
				}
				if (EffectiveOwnershipMap.TryGetValue(name, out PoolOwner owner)
					&& (!string.Equals(owner.DaoTu, pair.Key, StringComparison.Ordinal)
						|| owner.IsUpper != isUpper))
				{
					issues.Add(name + "错误出现在" + pair.Key + (isUpper ? "上位" : "下位") + "池");
				}
			}
		}
	}

	internal static string NormalizeXianJiId(string id)
	{
		string value = (id ?? string.Empty).Trim();
		return XianJiAliasMap.TryGetValue(value, out string canonical) ? canonical : value;
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
		string normalizedValue = NormalizeXianJiId(value);
		if (pool == null || string.IsNullOrWhiteSpace(normalizedValue))
		{
			return false;
		}

		for (int i = 0; i < pool.Length; i++)
		{
			if (string.Equals(NormalizeXianJiId(pool[i]), normalizedValue, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static bool Contains(List<string> pool, string value)
	{
		if (pool == null) return false;
		string normalizedValue = NormalizeXianJiId(value);
		for (int i = 0; i < pool.Count; i++)
		{
			if (string.Equals(NormalizeXianJiId(pool[i]), normalizedValue, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}
}
