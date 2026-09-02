using System;
using System.Collections.Generic;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 权柄引发的神通易象只允许由显式因果规则触发：
/// 角色显位道途 + 权柄来源道途 + 权柄准确全称 + 旧形 + 新形。
/// 不再使用权柄序位、数组下标或哈希兜底，避免无关外道权柄误触具名变化。
/// </summary>
internal static class XjAuthorityShenTongMutationCatalog
{
	internal readonly struct Rule
	{
		internal readonly string ActorDaoTu;
		internal readonly string SourceDaoTu;
		internal readonly string SourceAuthority;
		internal readonly string Lower;
		internal readonly string Upper;
		internal readonly bool SourceListed;
		internal readonly string ProofMethod;

		internal Rule(string actorDaoTu, string sourceDaoTu, string sourceAuthority, string lower, string upper, bool sourceListed, string proofMethod)
		{
			ActorDaoTu = actorDaoTu ?? string.Empty;
			SourceDaoTu = sourceDaoTu ?? string.Empty;
			SourceAuthority = sourceAuthority ?? string.Empty;
			Lower = lower ?? string.Empty;
			Upper = upper ?? string.Empty;
			SourceListed = sourceListed;
			ProofMethod = proofMethod ?? string.Empty;
		}

		internal string BindingKey => SourceDaoTu + "~" + SourceAuthority + "~" + Lower + "~" + Upper;
	}

	private static readonly Rule[] Rules =
	{
		R("上仪", "上仪", "明心之筵", "仪仗风", "明心筵", false, "本道权柄显化（道统补录）"),
		R("上仪", "上仪", "致于缉熙", "威仪气", "致缉熙", false, "本道权柄显化（道统补录）"),
		R("上仪", "上仪", "观天之道", "尊贵烟", "观天地", false, "本道权柄显化（道统补录）"),
		R("上仪", "上仪", "无极而生", "典礼云", "无极生", false, "本道权柄显化（道统补录）"),
		R("上仪", "上仪", "射狩之王", "朝会雾", "脱胎路", false, "本道权柄显化（道统补录）"),
		R("上巫", "上巫", "槐阴通幽", "阴槐枝", "槐荫鬼", false, "本道权柄显化（道统补录）"),
		R("上巫", "上巫", "帝雾应变", "雾隐身", "应帝王", false, "本道权柄显化（道统补录）"),
		R("上巫", "上巫", "血祭夺生", "血祝纹", "饮民血", false, "本道权柄显化（道统补录）"),
		R("上巫", "上巫", "隐名绝察", "避查符", "勿查我", false, "本道权柄显化（道统补录）"),
		R("上巫", "上巫", "地祝承命", "地祷坛", "地巫祝", false, "本道权柄显化（道统补录）"),
		R("下仪", "下仪", "轮回之殿", "卑下尘", "幽冥身", false, "本道权柄显化（道统补录）"),
		R("下仪", "下仪", "黄泉之路", "凡俗气", "轮回殿", false, "本道权柄显化（道统补录）"),
		R("下仪", "下仪", "忘川之河", "微贱烟", "黄泉路", false, "本道权柄显化（道统补录）"),
		R("下仪", "下仪", "了却前尘", "草莽雾", "忘川河", false, "本道权柄显化（道统补录）"),
		R("下仪", "下仪", "阴司敕令", "野逸风", "了前尘", false, "本道权柄显化（道统补录）"),
		R("保木", "保木", "蕴长生道", "守护藤", "神在隰", false, "本道权柄显化（道统补录）"),
		R("保木", "保木", "万劫不坏", "长青柏", "蕴长生", false, "本道权柄显化（道统补录）"),
		R("保木", "保木", "古盘之根", "护根土", "万劫不", false, "本道权柄显化（道统补录）"),
		R("保木", "保木", "森罗之相", "存续种", "古盘根", false, "本道权柄显化（道统补录）"),
		R("保木", "保木", "栋梁之材", "安巢枝", "森罗相", false, "本道权柄显化（道统补录）"),
		R("元雷", "元雷", "主煞之仪", "脱煞胎", "主煞仪", true, "原著具名前后形"),
		R("元雷", "元雷", "元雷之罚", "元磁光", "元雷罚", false, "本道权柄显化（道统补录）"),
		R("元雷", "元雷", "九霄震动", "先天雷", "九霄动", false, "本道权柄显化（道统补录）"),
		R("元雷", "元雷", "霄汉澄清", "混元霹", "霄汉清", false, "本道权柄显化（道统补录）"),
		R("元雷", "元雷", "玄雷之劫", "太初霆", "玄雷劫", false, "本道权柄显化（道统补录）"),
		R("兑金", "兑金", "白色兑相", "西极铁", "君兑隅", false, "本道权柄显化（道统补录）"),
		R("兑金", "兑金", "消残雷劫", "锋镝鸣", "杀收宫", false, "本道权柄显化（道统补录）"),
		R("兑金", "兑金", "多疑喜缺", "请两忘", "位从孚", true, "原著具名前后形"),
		R("兑金", "兑金", "折毁锋芒", "霜刃寒", "不穷锋", false, "本道权柄显化（道统补录）"),
		R("全丹", "全丹", "铅华照性", "铅华露", "混铅华", false, "本道权柄显化（道统补录）"),
		R("全丹", "全丹", "汞养成身", "养宜散", "制养宜", false, "本道权柄显化（道统补录）"),
		R("全丹", "全丹", "候神孕物", "候神丸", "侯神殊", false, "本道权柄显化（道统补录）"),
		R("全丹", "全丹", "金书通变", "金书页", "金书序", false, "本道权柄显化（道统补录）"),
		R("全丹", "全丹", "性命归真", "内炉火", "丹合神", false, "本道权柄显化（道统补录）"),
		R("华炁", "华炁", "冠灵之旒", "华彩云", "冠灵旒", false, "本道权柄显化（道统补录）"),
		R("华炁", "华炁", "众生之念", "光耀气", "十方界", false, "本道权柄显化（道统补录）"),
		R("华炁", "华炁", "仪仗齐全", "锦绣烟", "仪仗全", false, "本道权柄显化（道统补录）"),
		R("华炁", "华炁", "光华显现", "荣华雾", "光华现", false, "本道权柄显化（道统补录）"),
		R("华炁", "华炁", "琉璃之履", "璀璨霭", "琉璃履", false, "本道权柄显化（道统补录）"),
		R("厥阴", "厥阴", "群居乱交", "利异臣", "不二舆", false, "本道权柄显化（道统补录）"),
		R("厥阴", "厥阴", "上修在身", "藏晦衣", "不紫衣", false, "上位形有旧谱可据；下位前形据道统补录"),
		R("厥阴", "厥阴", "下衍百邪", "参疑室", "青鸾唳", false, "本道权柄显化（道统补录）"),
		R("厥阴", "厥阴", "牝水之因", "隐风谷", "归藏演", false, "本道权柄显化（道统补录）"),
		R("厥阴", "厥阴", "青鸾之唳", "藏木叶", "厥阴主", false, "本道权柄显化（道统补录）"),
		R("司天", "司天", "曜辰闻事", "曜辰铃", "听曜辰", false, "本道权柄显化（道统补录）"),
		R("司天", "司天", "星象布序", "布序简", "神布序", false, "本道权柄显化（道统补录）"),
		R("司天", "司天", "斗衡定方", "斗衡筹", "斗衡玄", false, "本道权柄显化（道统补录）"),
		R("司天", "司天", "辰命守数", "南衡表", "司辰命", false, "本道权柄显化（道统补录）"),
		R("司天", "司天", "浑仪列天", "司时漏", "布天仪", false, "本道权柄显化（道统补录）"),
		R("合水", "合水", "归流之处", "雾回阴", "归流处", false, "本道权柄显化（道统补录）"),
		R("合水", "合水", "雾回阴转", "汇流川", "谶在兹", false, "本道权柄显化（道统补录）"),
		R("合水", "合水", "百川汇海", "交融湖", "广准圣", false, "本道权柄显化（道统补录）"),
		R("合水", "合水", "包容之相", "合涧溪", "诸合还", false, "本道权柄显化（道统补录）"),
		R("合水", "合水", "四海承平", "并泉脉", "妖渎河", false, "本道权柄显化（道统补录）"),
		R("坎水", "坎水", "溪上之翁", "沧海潮", "长云暗", false, "本道权柄显化（道统补录）"),
		R("坎水", "坎水", "浩瀚在怀", "泾龙王", "浩瀚海", true, "本道浩瀚权柄显化"),
		R("坎水", "坎水", "据岭中流", "据岭中", "入坎窞", true, "原著具名前后形"),
		R("坎水", "坎水", "长云暗雪", "天一水", "恨江去", false, "本道权柄显化（道统补录）"),
		R("太阳", "太阳", "五道皆明", "视天统", "分阳钗", true, "原著具名前后形"),
		R("太阳", "太阳", "日间显耀", "扶桑枝", "郁仪文", false, "本道权柄显化（道统补录）"),
		R("太阳", "太阳", "诸阳景从", "耀金乌", "耀尘世", false, "本道权柄显化（道统补录）"),
		R("太阳", "太阳", "极盛御宇", "赤霞冠", "极盛御", false, "本道权柄显化（道统补录）"),
		R("太阳", "太阳", "帝冕冠世", "明光铠", "帝冕冠", false, "本道权柄显化（道统补录）"),
		R("太阴", "太阴", "诸阴所宗", "授玄珠", "结璘章", false, "本道权柄显化（道统补录）"),
		R("太阴", "太阴", "道藏之藏", "不胜寒", "仪对影", false, "本道权柄显化（道统补录）"),
		R("太阴", "太阴", "避劫藏真", "再圆阙", "湖月秋", false, "本道权柄显化（道统补录）"),
		R("太阴", "太阴", "月华幽元", "夜光府", "月光府", true, "原著具名前后形"),
		R("太阴", "太阴", "太素之诣", "惊鹊乡", "诣太素", false, "本道权柄显化（道统补录）"),
		R("宝土", "宝土", "高垒之燕", "宝藏岩", "高垒燕", false, "本道权柄显化（道统补录）"),
		R("宝土", "宝土", "藏纳之宫", "灵玉矿", "藏纳宫", false, "本道权柄显化（道统补录）"),
		R("宝土", "宝土", "膏泽之治", "珍稀壤", "膏泽治", false, "本道权柄显化（道统补录）"),
		R("宝土", "宝土", "万众归心", "膏腴田", "梭摩岭", false, "本道权柄显化（道统补录）"),
		R("宝土", "宝土", "万宝敛藏", "沃土膏", "万众心", false, "本道权柄显化（道统补录）"),
		R("宣土", "宣土", "天下归心", "天下心", "朝社参", true, "原著具名前后形"),
		R("宣土", "宣土", "神用其命", "宣化泥", "神用命", false, "本道权柄显化（道统补录）"),
		R("宣土", "宣土", "训浚之明", "社稷坛", "训浚明", false, "本道权柄显化（道统补录）"),
		R("宣土", "宣土", "宣威仪仗", "布德土", "宣威仪", false, "本道权柄显化（道统补录）"),
		R("宣土", "宣土", "德广配天", "教稼穑", "德广天", false, "本道权柄显化（道统补录）"),
		R("寒炁", "寒炁", "松上之雪", "寒冰魄", "入清听", false, "本道权柄显化（道统补录）"),
		R("寒炁", "寒炁", "入清听音", "霜雪精", "松上雪", false, "本道权柄显化（道统补录）"),
		R("寒炁", "寒炁", "祢水之寒", "凛冬风", "祢水寒", false, "本道权柄显化（道统补录）"),
		R("寒炁", "寒炁", "沆砀之满", "冻云霭", "沆砀满", false, "本道权柄显化（道统补录）"),
		R("寒炁", "寒炁", "青霄之女", "冷月霜", "青霄女", false, "本道权柄显化（道统补录）"),
		R("少阳", "少阳", "邪绝求变", "初晖草", "奉东君", false, "本道权柄显化（道统补录）"),
		R("少阳", "少阳", "奉东君命", "萌春芽", "目骋怀", false, "本道权柄显化（道统补录）"),
		R("少阳", "少阳", "青阳焕发", "晨露晞", "邪绝求", false, "本道权柄显化（道统补录）"),
		R("少阳", "少阳", "残阳泣血", "微明火", "青阳焕", false, "本道权柄显化（道统补录）"),
		R("少阳", "少阳", "目骋怀远", "启明星", "残阳泣", false, "本道权柄显化（道统补录）"),
		R("少阴", "少阴", "华池见冰", "霜降微", "太冲观", false, "本道权柄显化（道统补录）"),
		R("少阴", "少阴", "大暄含寒", "幽谷兰", "香俱沉", false, "本道权柄显化（道统补录）"),
		R("少阴", "少阴", "主阴而燥", "凝冰魄", "调杼柚", false, "本道权柄显化（道统补录）"),
		R("少阴", "少阴", "少阴枢机", "潜渊鱼", "广寒敕", false, "本道权柄显化（道统补录）"),
		R("少阴", "少阴", "蟾宫折桂", "敛华光", "蟾宫折", false, "本道权柄显化（道统补录）"),
		R("并火", "并火", "焰中之乌", "双焰合", "乌从欲", false, "本道权柄显化（道统补录）"),
		R("并火", "并火", "乌从欲生", "并蒂火", "心期焚", false, "本道权柄显化（道统补录）"),
		R("并火", "并火", "心期焚灭", "连焚木", "兼险夺", false, "本道权柄显化（道统补录）"),
		R("并火", "并火", "兼险夺命", "共燃薪", "至命除", false, "本道权柄显化（道统补录）"),
		R("并火", "并火", "至命而除", "同炽炭", "焰中乌", false, "本道权柄显化（道统补录）"),
		R("库金", "库金", "启阵法藏", "藏宝匣", "帑梁银", false, "本道权柄显化（道统补录）"),
		R("库金", "库金", "养金精资", "积财山", "金销洞", false, "本道权柄显化（道统补录）"),
		R("库金", "库金", "金销洞开", "聚珍釜", "百宝殿", false, "本道权柄显化（道统补录）"),
		R("库金", "库金", "百宝殿藏", "纳川囊", "藏金地", false, "本道权柄显化（道统补录）"),
		R("库金", "库金", "铜锈之炁", "封存印", "铜锈炁", false, "本道权柄显化（道统补录）"),
		R("庚金", "庚金", "金色庚相", "削故锋", "今去故", false, "上位形有旧谱可据；下位前形据道统补录"),
		R("庚金", "庚金", "发体殁变", "削铁泥", "天金冑", false, "本道权柄显化（道统补录）"),
		R("庚金", "庚金", "不畏火焚", "刚锋气", "墓门棘", false, "本道权柄显化（道统补录）"),
		R("庚金", "庚金", "镂金石坚", "金兽羽", "镂金石", true, "原著具名前后形"),
		R("府水", "府水", "朝寒之雨", "幽重玄", "合黎渊", false, "本道权柄显化（道统补录）"),
		R("府水", "府水", "合黎之渊", "沉羽渊", "宿穷冬", false, "本道权柄显化（道统补录）"),
		R("府水", "府水", "广浚之湖", "养命蛟", "广浚湖", true, "原著具名前后形"),
		R("归土", "归土", "狡兔落原", "归根尘", "狡落原", false, "本道权柄显化（道统补录）"),
		R("归土", "归土", "土归尘埃", "落葬丘", "庥命簋", false, "本道权柄显化（道统补录）"),
		R("归土", "归土", "万物归藏", "返璞壤", "有常主", false, "本道权柄显化（道统补录）"),
		R("归土", "归土", "归土藏形", "息壤土", "养役母", false, "本道权柄显化（道统补录）"),
		R("归土", "归土", "观红尘事", "安息地", "土归尘", false, "本道权柄显化（道统补录）"),
		R("戊土", "戊土", "受天之抚", "戊己壤", "受抚顶", false, "本道权柄显化（道统补录）"),
		R("戊土", "戊土", "仙无漏身", "中央土", "仙无漏", false, "本道权柄显化（道统补录）"),
		R("戊土", "戊土", "戊心之岩", "厚德尘", "戊心岩", false, "本道权柄显化（道统补录）"),
		R("戊土", "戊土", "厚德载物", "承载地", "厚德载", false, "本道权柄显化（道统补录）"),
		R("戊土", "戊土", "中宫定鼎", "黄天厚", "中宫定", false, "本道权柄显化（道统补录）"),
		R("执孛", "执孛", "形神两渡", "化形蜕", "形渡阡", false, "本道权柄显化（道统补录）"),
		R("执孛", "执孛", "阴阳离绝", "离相符", "相离绝", false, "本道权柄显化（道统补录）"),
		R("执孛", "执孛", "王威断气", "倏动影", "倏动勃", false, "本道权柄显化（道统补录）"),
		R("执孛", "执孛", "孛光蜕形", "渡阡步", "孛形遁", false, "本道权柄显化（道统补录）"),
		R("执孛", "执孛", "移相存命", "孛光尘", "移相身", false, "本道权柄显化（道统补录）"),
		R("明阳", "明阳", "男主女辅", "顾署舆", "赤断镞", true, "原著具名前后形"),
		R("明阳", "明阳", "尊卑法统", "长明阶", "帝观元", true, "原著具名前后形"),
		R("明阳", "明阳", "天光明火", "照昧灯", "昭澈心", false, "上位形有旧谱可据；下位前形据道统补录"),
		R("明阳", "明阳", "谒天门开", "煌元关", "谒天门", true, "原著具名前后形"),
		R("明阳", "明阳", "君蹈危机", "誓崤身", "君蹈危", true, "原著具名前后形"),
		R("晞炁", "晞炁", "未阕之华", "焜煌复", "未阕华", true, "原著具名前后形"),
		R("晞炁", "晞炁", "乞代永夜", "掩尘雾", "乞代夜", true, "原著具名前后形"),
		R("晞炁", "晞炁", "郁燠之苦", "朝露晞", "郁燠苦", false, "本道权柄显化（道统补录）"),
		R("晞炁", "晞炁", "庆垂之轩", "晨光熹", "庆垂轩", false, "本道权柄显化（道统补录）"),
		R("晞炁", "晞炁", "议八辟法", "干阳燥", "议八辟", false, "本道权柄显化（道统补录）"),
		R("更木", "更木", "病前之春", "天下易", "病前春", true, "原著具名前后形"),
		R("更木", "更木", "枯荣交迭", "更新叶", "枯荣道", false, "本道权柄显化（道统补录）"),
		R("更木", "更木", "更迭之术", "岁寒松", "更迭术", false, "本道权柄显化（道统补录）"),
		R("更木", "更木", "万古常新", "改节竹", "万古新", false, "本道权柄显化（道统补录）"),
		R("更木", "更木", "新陈之变", "轮替花", "新陈变", false, "本道权柄显化（道统补录）"),
		R("正木", "正木", "甲乙交合", "正直干", "木成方", false, "本道权柄显化（道统补录）"),
		R("正木", "正木", "木成方正", "巽风枝", "位从专", false, "本道权柄显化（道统补录）"),
		R("正木", "正木", "位从专一", "参天木", "见查语", false, "本道权柄显化（道统补录）"),
		R("正木", "正木", "见查之语", "向阳叶", "背南行", false, "本道权柄显化（道统补录）"),
		R("正木", "正木", "万古长青", "规矩林", "万古青", false, "本道权柄显化（道统补录）"),
		R("清炁", "清炁", "诸炁之母", "清虚台", "益清气", false, "本道权柄显化（道统补录）"),
		R("清炁", "清炁", "清元之风", "上善云", "浮云身", false, "本道权柄显化（道统补录）"),
		R("清炁", "清炁", "益养清炁", "空明意", "空应散", false, "本道权柄显化（道统补录）"),
		R("清炁", "清炁", "炁引灵池", "炁引池", "炁临宇", true, "原著具名前后形"),
		R("渌水", "渌水", "清浊自变", "天下覆", "如重浊", true, "原著具名前后形"),
		R("渌水", "渌水", "洞泉之声", "清溪石", "洞泉声", false, "本道权柄显化（道统补录）"),
		R("渌水", "渌水", "清夕之雨", "渌波纹", "清夕雨", false, "本道权柄显化（道统补录）"),
		R("渌水", "渌水", "丑癸藏形", "澄潭影", "丑癸藏", false, "本道权柄显化（道统补录）"),
		R("渌水", "渌水", "洗劫之露", "碧水珠", "洗劫露", false, "本道权柄显化（道统补录）"),
		R("灴火", "灴火", "燔旧之室", "天下熯", "燔旧室", true, "原著具名前后形"),
		R("灴火", "灴火", "白樆之心", "灴炉炭", "白樆心", false, "本道权柄显化（道统补录）"),
		R("灴火", "灴火", "布燥之使", "烘炉火", "布燥使", false, "本道权柄显化（道统补录）"),
		R("灴火", "灴火", "秉灴之夏", "灼野燎", "秉灴夏", false, "本道权柄显化（道统补录）"),
		R("灴火", "灴火", "虚宫之火", "炎夏阳", "虚宫火", false, "本道权柄显化（道统补录）"),
		R("煞炁", "煞炁", "不空之劫", "凶煞风", "罗剎海", false, "本道权柄显化（道统补录）"),
		R("煞炁", "煞炁", "罗刹之海", "戾气云", "人中屠", false, "本道权柄显化（道统补录）"),
		R("煞炁", "煞炁", "千百化身", "千百身", "箝恨口", true, "原著具名前后形"),
		R("煞炁", "煞炁", "人中屠戮", "劫难烟", "戮天元", false, "本道权柄显化（道统补录）"),
		R("牝水", "牝水", "往生之泉", "玄牝泉", "佞无晨", false, "本道权柄显化（道统补录）"),
		R("牝水", "牝水", "佞无晨晦", "母源海", "往生泉", false, "本道权柄显化（道统补录）"),
		R("牝水", "牝水", "谿谷之会", "滋生液", "谿谷会", false, "本道权柄显化（道统补录）"),
		R("牝水", "牝水", "参玄之脏", "化生池", "参玄臟", false, "本道权柄显化（道统补录）"),
		R("牝水", "牝水", "照夭之胎", "胎息潮", "照夭胎", false, "本道权柄显化（道统补录）"),
		R("牡火", "牡火", "牡煞之火", "雄烈焰", "牡煞火", false, "本道权柄显化（道统补录）"),
		R("牡火", "牡火", "韬光避灾", "刚猛燚", "韬无灾", false, "本道权柄显化（道统补录）"),
		R("牡火", "牡火", "高陵之父", "阳刚火", "高陵父", false, "本道权柄显化（道统补录）"),
		R("牡火", "牡火", "受灴之龠", "烈性焰", "受灴龠", false, "本道权柄显化（道统补录）"),
		R("牡火", "牡火", "光明之相", "壮阳辉", "光明相", false, "本道权柄显化（道统补录）"),
		R("玄雷", "玄雷", "靖平之赦", "玉枢印", "至阳嘘", false, "本道权柄显化（道统补录）"),
		R("玄雷", "玄雷", "盛阳之墟", "雷霆符", "神宫誓", false, "本道权柄显化（道统补录）"),
		R("玄雷", "玄雷", "天鸣之策", "劫雷声", "伐封坛", false, "本道权柄显化（道统补录）"),
		R("玄雷", "玄雷", "律演威仪", "天鸣策", "律演威", true, "原著具名前后形"),
		R("玉真", "玉真", "玉身合命", "青玉符", "玉中人", false, "本道权柄显化（道统补录）"),
		R("玉真", "玉真", "青崖照境", "白玉屑", "青玉崖", false, "本道权柄显化（道统补录）"),
		R("玉真", "玉真", "真虚合同", "问道简", "道合真", false, "本道权柄显化（道统补录）"),
		R("玉真", "玉真", "锦障问途", "合真佩", "问道锦", false, "本道权柄显化（道统补录）"),
		R("玉真", "玉真", "白璧承阴", "庭卫印", "白玉盘", false, "本道权柄显化（道统补录）"),
		R("瑞炁", "瑞炁", "好功之箓", "祥云瑞", "好功箓", false, "本道权柄显化（道统补录）"),
		R("瑞炁", "瑞炁", "瑞气祥云", "吉庆烟", "瑞气云", false, "本道权柄显化（道统补录）"),
		R("瑞炁", "瑞炁", "祥云呈现", "福临气", "祥云现", false, "本道权柄显化（道统补录）"),
		R("瑞炁", "瑞炁", "福星高照", "绵长运", "福星照", false, "本道权柄显化（道统补录）"),
		R("瑞炁", "瑞炁", "瑞炁呈祥", "晋阶霞", "瑞炁祥", false, "本道权柄显化（道统补录）"),
		R("真火", "真火", "天兜之火", "三昧真", "雉离行", false, "本道权柄显化（道统补录）"),
		R("真火", "真火", "雉离之行", "纯阳燧", "天兜火", false, "本道权柄显化（道统补录）"),
		R("真火", "真火", "真火之身", "先天火", "治命神", false, "本道权柄显化（道统补录）"),
		R("真火", "真火", "燎原之势", "不灭灯", "真火身", false, "本道权柄显化（道统补录）"),
		R("真火", "真火", "治命之神", "净世焰", "燎原势", false, "本道权柄显化（道统补录）"),
		R("真炁", "真炁", "抱石而眠", "真元息", "抱石眠", false, "本道权柄显化（道统补录）"),
		R("真炁", "真炁", "霞羽之客", "纯阳罡", "霞羽客", false, "本道权柄显化（道统补录）"),
		R("真炁", "真炁", "鹤挂衣冠", "先天一", "鹤挂衣", false, "本道权柄显化（道统补录）"),
		R("真炁", "真炁", "授人长生", "不二气", "授长生", false, "本道权柄显化（道统补录）"),
		R("真炁", "真炁", "蜕凡超尘", "至大刚", "蜕凡尘", false, "本道权柄显化（道统补录）"),
		R("离火", "离火", "位从罗网", "朱雀羽", "位从罗", false, "本道权柄显化（道统补录）"),
		R("离火", "离火", "顺平征伐", "丹景光", "九重擭", false, "本道权柄显化（道统补录）"),
		R("离火", "离火", "大离之书", "望日述", "大离书", true, "原著具名前后形"),
		R("离火", "离火", "九重之擭", "南明焰", "折焚尽", false, "本道权柄显化（道统补录）"),
		R("紫炁", "紫炁", "列紫之篇", "紫霞绡", "列紫篇", false, "本道权柄显化（道统补录）"),
		R("紫炁", "紫炁", "绕东山行", "华盖云", "坤辰修", false, "本道权柄显化（道统补录）"),
		R("紫炁", "紫炁", "道始兆端", "蕴宝瓶", "道始兆", true, "原著具名前后形"),
		R("紫炁", "紫炁", "坤辰之修", "帝星气", "养生主", false, "本道权柄显化（道统补录）"),
		R("艮土", "艮土", "愚赶山岳", "后土符", "愚赶山", false, "本道权柄显化（道统补录）"),
		R("艮土", "艮土", "正源之谷", "镇岳印", "正源谷", false, "本道权柄显化（道统补录）"),
		R("艮土", "艮土", "山岳镇伏", "坤厚石", "山岳镇", false, "本道权柄显化（道统补录）"),
		R("艮土", "艮土", "不动之尊", "中宫尘", "不动尊", false, "本道权柄显化（道统补录）"),
		R("艮土", "艮土", "艮止如山", "不动山", "艮为山", false, "本道权柄显化（道统补录）"),
		R("衡祝", "衡祝", "赤虎辟灾", "祭火种", "殿阳虎", false, "本道权柄显化（道统补录）"),
		R("衡祝", "衡祝", "斛衡量厄", "量灾筹", "斛量灾", false, "本道权柄显化（道统补录）"),
		R("衡祝", "衡祝", "血烬成域", "殿虎纹", "满烬煌", false, "本道权柄显化（道统补录）"),
		R("衡祝", "衡祝", "祭燎分灾", "守阳灯", "祝衡火", false, "本道权柄显化（道统补录）"),
		R("衡祝", "衡祝", "双衡定命", "余烬符", "灾秤命", false, "本道权柄显化（道统补录）"),
		R("角木", "角木", "余阳所钟", "春风枝", "青龙气", false, "本道权柄显化（道统补录）"),
		R("角木", "角木", "黎运逢春", "木凭春", "黎运春", true, "原著具名前后形"),
		R("角木", "角木", "乙木周全", "定元春", "乙木全", true, "原著具名前后形"),
		R("谪炁", "谪炁", "藏壑之舟", "谪仙尘", "藏壑舟", false, "本道权柄显化（道统补录）"),
		R("谪炁", "谪炁", "薄虞之渊", "贬落烟", "薄虞渊", false, "本道权柄显化（道统补录）"),
		R("谪炁", "谪炁", "忘川之歌", "流放雾", "忘川歌", false, "本道权柄显化（道统补录）"),
		R("谪炁", "谪炁", "谪落人间", "罪愆气", "惘乾坤", false, "本道权柄显化（道统补录）"),
		R("谪炁", "谪炁", "幽冥归途", "凡尘染", "盼天赐", false, "本道权柄显化（道统补录）"),
		R("逍金", "逍金", "逍遥无羁", "逍遥铁", "望商锋", false, "本道权柄显化（道统补录）"),
		R("逍金", "逍金", "纳体归匮", "自在锋", "逍遥游", false, "本道权柄显化（道统补录）"),
		R("逍金", "逍金", "御金而行", "无拘刃", "御金行", false, "本道权柄显化（道统补录）"),
		R("逍金", "逍金", "望商之锋", "云游剑", "无羁绊", false, "本道权柄显化（道统补录）"),
		R("逍金", "逍金", "敛芒藏锋", "散金尘", "敛锋芒", false, "本道权柄显化（道统补录）"),
		R("邃炁", "邃炁", "代行妨天", "幽玄雾", "代行妨", false, "本道权柄显化（道统补录）"),
		R("邃炁", "邃炁", "闇天之秧", "深冥霭", "闇天殃", false, "本道权柄显化（道统补录）"),
		R("邃炁", "邃炁", "逆乱人伦", "邃古息", "逆人伦", false, "本道权柄显化（道统补录）"),
		R("邃炁", "邃炁", "众生之苦", "窈冥烟", "众生苦", false, "本道权柄显化（道统补录）"),
		R("邃炁", "邃炁", "乱天之理", "太虚氤", "乱天理", false, "本道权柄显化（道统补录）"),
		R("都卫", "都卫", "降魂蔽灵", "镇魂符", "降魂闻", false, "本道权柄显化（道统补录）"),
		R("都卫", "都卫", "东岳沉虚", "东山石", "东利山", false, "本道权柄显化（道统补录）"),
		R("都卫", "都卫", "西塬绝界", "西璟砂", "西天璟", false, "本道权柄显化（道统补录）"),
		R("都卫", "都卫", "南水化虚", "南水滴", "南悯水", false, "本道权柄显化（道统补录）"),
		R("都卫", "都卫", "北庭封域", "北庭旗", "北漠庭", false, "本道权柄显化（道统补录）"),
		R("长庚", "长庚", "意御形神", "抱剑眠", "意堪身", false, "本道权柄显化（道统补录）"),
		R("长庚", "长庚", "寒锋照夜", "洗锋池", "照寒锋", false, "本道权柄显化（道统补录）"),
		R("长庚", "长庚", "一剑断法", "养剑匣", "斩青冥", false, "本道权柄显化（道统补录）"),
		R("长庚", "长庚", "兵革归元", "试剑石", "剑归元", false, "本道权柄显化（道统补录）"),
		R("长庚", "长庚", "太白开天", "听剑鸣", "破天门", false, "本道权柄显化（道统补录）"),
		R("集木", "集木", "众修云集", "朱丹参", "妄诞林", false, "本道权柄显化（道统补录）"),
		R("集木", "集木", "隼栖木上", "群鸟栖", "祸延生", false, "本道权柄显化（道统补录）"),
		R("集木", "集木", "祸延生发", "聚材林", "隼就栖", false, "本道权柄显化（道统补录）"),
		R("集木", "集木", "朱丹参同", "万木春", "诸蓼会", false, "本道权柄显化（道统补录）"),
		R("集木", "集木", "定元之春", "荟蔚丛", "凌云木", false, "本道权柄显化（道统补录）"),
		R("霄雷", "霄雷", "冬雷之声", "云霄霆", "春惊蛰", false, "本道权柄显化（道统补录）"),
		R("霄雷", "霄雷", "春惊蛰起", "天外雷", "轻雷落", false, "本道权柄显化（道统补录）"),
		R("霄雷", "霄雷", "轻雷之落", "九霄鸣", "震雷枢", false, "本道权柄显化（道统补录）"),
		R("霄雷", "霄雷", "斡动紫气", "玄雷泊", "斡动紫", true, "原著具名前后形"),
		R("青宣", "青宣", "伏元镇脉", "青岩符", "伏青山", false, "本道权柄显化（道统补录）"),
		R("青宣", "青宣", "玄羊司土", "伏山气", "青宣岳", false, "本道权柄显化（道统补录）"),
		R("青宣", "青宣", "岩神承天", "观冥砂", "上岩神", false, "本道权柄显化（道统补录）"),
		R("青宣", "青宣", "观冥察地", "羊脂露", "观地冥", false, "本道权柄显化（道统补录）"),
		R("青宣", "青宣", "下阳敷生", "下阳元", "玄羊子", true, "原著具名前后形"),
		R("鸺葵", "鸺葵", "枭狸分形", "枭羽符", "枭逐狸", false, "本道权柄显化（道统补录）"),
		R("鸺葵", "鸺葵", "风魇化阴", "狸影纸", "魇鬼阴", false, "本道权柄显化（道统补录）"),
		R("鸺葵", "鸺葵", "夜翳无踪", "夜行箓", "枭夜行", false, "本道权柄显化（道统补录）"),
		R("鸺葵", "鸺葵", "分命寄狸", "分命符", "狸分命", false, "本道权柄显化（道统补录）"),
		R("鸺葵", "鸺葵", "羽阴藏真", "藏形翎", "阴羽藏", false, "本道权柄显化（道统补录）"),
		R("齐金", "齐金", "与天齐满", "天金砂", "天齐满", false, "本道权柄显化（道统补录）"),
		R("齐金", "齐金", "得体归元", "圆满轮", "金戈阵", false, "本道权柄显化（道统补录）"),
		R("齐金", "齐金", "金戈肃杀", "无缺璧", "肃杀令", false, "本道权柄显化（道统补录）"),
		R("齐金", "齐金", "齐天律令", "纯钧魄", "齐天律", false, "本道权柄显化（道统补录）"),
		R("齐金", "齐金", "万军俯首", "恒常性", "万军令", false, "本道权柄显化（道统补录）"),
		R("太阴", "少阴", "华池见冰", "夜光府", "月光府", false, "邻道权柄共鸣（道统补录）"),
		R("少阴", "厥阴", "上修在身", "霜降微", "太冲观", false, "邻道权柄共鸣（道统补录）"),
		R("厥阴", "太阴", "避劫藏真", "藏晦衣", "不紫衣", false, "邻道权柄共鸣（道统补录）"),
		R("太阳", "少阳", "邪绝求变", "视天统", "分阳钗", false, "邻道权柄共鸣（道统补录）"),
		R("少阳", "明阳", "尊卑法统", "初晖草", "奉东君", false, "邻道权柄共鸣（道统补录）"),
		R("明阳", "太阳", "诸阳景从", "顾署舆", "赤断镞", false, "邻道权柄共鸣（道统补录）"),
		R("玄雷", "霄雷", "冬雷之声", "天鸣策", "律演威", false, "邻道权柄共鸣（道统补录）"),
		R("霄雷", "元雷", "元雷之罚", "玄雷泊", "斡动紫", false, "邻道权柄共鸣（道统补录）"),
		R("元雷", "玄雷", "天鸣之策", "脱煞胎", "主煞仪", false, "邻道权柄共鸣（道统补录）"),
		R("兑金", "逍金", "逍遥无羁", "请两忘", "位从孚", false, "邻道权柄共鸣（道统补录）"),
		R("逍金", "齐金", "得体归元", "逍遥铁", "望商锋", false, "邻道权柄共鸣（道统补录）"),
		R("齐金", "库金", "金销洞开", "天金砂", "天齐满", false, "邻道权柄共鸣（道统补录）"),
		R("库金", "庚金", "镂金石坚", "藏宝匣", "帑梁银", false, "邻道权柄共鸣（道统补录）"),
		R("庚金", "长庚", "太白开天", "削故锋", "今去故", false, "邻道权柄共鸣（道统补录）"),
		R("长庚", "兑金", "白色兑相", "抱剑眠", "意堪身", false, "邻道权柄共鸣（道统补录）"),
		R("角木", "正木", "甲乙交合", "木凭春", "黎运春", false, "邻道权柄共鸣（道统补录）"),
		R("正木", "集木", "隼栖木上", "正直干", "木成方", false, "邻道权柄共鸣（道统补录）"),
		R("集木", "更木", "更迭之术", "朱丹参", "妄诞林", false, "邻道权柄共鸣（道统补录）"),
		R("更木", "保木", "森罗之相", "天下易", "病前春", false, "邻道权柄共鸣（道统补录）"),
		R("保木", "角木", "青龙之气", "守护藤", "神在隰", false, "邻道权柄共鸣（道统补录）"),
		R("渌水", "合水", "雾回阴转", "天下覆", "如重浊", false, "邻道权柄共鸣（道统补录）"),
		R("合水", "府水", "宿于穷冬", "雾回阴", "归流处", false, "邻道权柄共鸣（道统补录）"),
		R("府水", "牝水", "参玄之脏", "养命蛟", "广浚湖", false, "邻道权柄共鸣（道统补录）"),
		R("牝水", "坎水", "恨江东去", "玄牝泉", "佞无晨", false, "邻道权柄共鸣（道统补录）"),
		R("离火", "灴火", "燔旧之室", "望日述", "大离书", false, "邻道权柄共鸣（道统补录）"),
		R("灴火", "并火", "乌从欲生", "天下熯", "燔旧室", false, "邻道权柄共鸣（道统补录）"),
		R("并火", "真火", "真火之身", "双焰合", "乌从欲", false, "邻道权柄共鸣（道统补录）"),
		R("真火", "牡火", "受灴之龠", "三昧真", "雉离行", false, "邻道权柄共鸣（道统补录）"),
		R("牡火", "离火", "折焚尽烬", "雄烈焰", "牡煞火", false, "邻道权柄共鸣（道统补录）"),
		R("艮土", "戊土", "受天之抚", "后土符", "愚赶山", false, "邻道权柄共鸣（道统补录）"),
		R("戊土", "归土", "土归尘埃", "戊己壤", "受抚顶", false, "邻道权柄共鸣（道统补录）"),
		R("归土", "宝土", "膏泽之治", "归根尘", "狡落原", false, "邻道权柄共鸣（道统补录）"),
		R("宝土", "宣土", "宣威仪仗", "宝藏岩", "高垒燕", false, "邻道权柄共鸣（道统补录）"),
		R("宣土", "艮土", "艮止如山", "天下心", "朝社参", false, "邻道权柄共鸣（道统补录）"),
		R("清炁", "紫炁", "列紫之篇", "炁引池", "炁临宇", false, "邻道权柄共鸣（道统补录）"),
		R("紫炁", "真炁", "霞羽之客", "蕴宝瓶", "道始兆", false, "邻道权柄共鸣（道统补录）"),
		R("真炁", "邃炁", "逆乱人伦", "真元息", "抱石眠", false, "邻道权柄共鸣（道统补录）"),
		R("邃炁", "寒炁", "沆砀之满", "幽玄雾", "代行妨", false, "邻道权柄共鸣（道统补录）"),
		R("寒炁", "晞炁", "议八辟法", "寒冰魄", "入清听", false, "邻道权柄共鸣（道统补录）"),
		R("晞炁", "瑞炁", "好功之箓", "焜煌复", "未阕华", false, "邻道权柄共鸣（道统补录）"),
		R("瑞炁", "煞炁", "罗刹之海", "祥云瑞", "好功箓", false, "邻道权柄共鸣（道统补录）"),
		R("煞炁", "华炁", "仪仗齐全", "千百身", "箝恨口", false, "邻道权柄共鸣（道统补录）"),
		R("华炁", "谪炁", "谪落人间", "华彩云", "冠灵旒", false, "邻道权柄共鸣（道统补录）"),
		R("谪炁", "上仪", "射狩之王", "谪仙尘", "藏壑舟", false, "邻道权柄共鸣（道统补录）"),
		R("上仪", "下仪", "轮回之殿", "仪仗风", "明心筵", false, "邻道权柄共鸣（道统补录）"),
		R("下仪", "清炁", "清元之风", "卑下尘", "幽冥身", false, "邻道权柄共鸣（道统补录）"),
		R("鸺葵", "上巫", "槐阴通幽", "枭羽符", "枭逐狸", false, "邻道权柄共鸣（道统补录）"),
		R("上巫", "玉真", "青崖照境", "阴槐枝", "槐荫鬼", false, "邻道权柄共鸣（道统补录）"),
		R("玉真", "衡祝", "血烬成域", "青玉符", "玉中人", false, "邻道权柄共鸣（道统补录）"),
		R("衡祝", "青宣", "观冥察地", "祭火种", "殿阳虎", false, "邻道权柄共鸣（道统补录）"),
		R("青宣", "全丹", "性命归真", "下阳元", "玄羊子", false, "邻道权柄共鸣（道统补录）"),
		R("全丹", "执孛", "形神两渡", "铅华露", "混铅华", false, "邻道权柄共鸣（道统补录）"),
		R("执孛", "司天", "星象布序", "化形蜕", "形渡阡", false, "邻道权柄共鸣（道统补录）"),
		R("司天", "都卫", "西塬绝界", "曜辰铃", "听曜辰", false, "邻道权柄共鸣（道统补录）"),
		R("都卫", "鸺葵", "分命寄狸", "镇魂符", "降魂闻", false, "邻道权柄共鸣（道统补录）"),
		R("坎水", "府水", "广浚之湖", "泾龙王", "浩瀚海", true, "原著明确：夺府水浩瀚意向而易象"),
	};

	private static readonly Dictionary<string, List<Rule>> GainRules = BuildGainRules();
	private static readonly Dictionary<string, List<Rule>> LocalLossRules = BuildLocalLossRules();

	/// <summary>
	/// 反查本道上位神通实际由哪一柄本源权柄显化。并古等五神通/六柄结构不能按
	/// 数组下标或哈希猜测；这里直接复用易象目录这一份显式因果事实。
	/// </summary>
	internal static bool TryResolveLocalAuthorityForUpper(string daoTu, string upperShenTong, out string authority)
	{
		authority = string.Empty;
		string actor = Normalize(daoTu);
		string expectedUpper = NormalizeUpperForBinding(upperShenTong, actor);
		if (actor.Length == 0 || expectedUpper.Length == 0 || !LocalLossRules.TryGetValue(actor, out List<Rule> values)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			Rule candidate = values[i];
			string candidateUpper = NormalizeUpperForBinding(candidate.Upper, actor);
			if (!string.Equals(candidateUpper, expectedUpper, StringComparison.Ordinal)) continue;
			authority = candidate.SourceAuthority;
			return !string.IsNullOrWhiteSpace(authority);
		}
		return false;
	}

	internal static bool TryResolveGain(string actorDaoTu, string sourceDaoTu, string authority, in XjXianJiState state, out Rule rule)
	{
		rule = default;
		string actor = Normalize(actorDaoTu);
		string source = Normalize(sourceDaoTu);
		string exactAuthority = NormalizeAuthority(authority);
		if (actor.Length == 0 || source.Length == 0 || exactAuthority.Length == 0 || state.Ids == null || state.Ids.Length == 0) return false;
		if (!GainRules.TryGetValue(BuildGainKey(actor, source), out List<Rule> values)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			Rule candidate = values[i];
			if (!AuthorityEquals(candidate.SourceAuthority, exactAuthority)) continue;
			if (!Has(state, candidate.Lower) || Has(state, candidate.Upper)) continue;
			rule = candidate;
			return true;
		}
		return false;
	}

	internal static bool TryResolveHeldUpper(string actorDaoTu, string sourceDaoTu, string authority, in XjXianJiState state, out Rule rule)
	{
		rule = default;
		string actor = Normalize(actorDaoTu);
		string source = Normalize(sourceDaoTu);
		string exactAuthority = NormalizeAuthority(authority);
		if (actor.Length == 0 || source.Length == 0 || exactAuthority.Length == 0 || state.Ids == null || state.Ids.Length == 0) return false;
		if (!GainRules.TryGetValue(BuildGainKey(actor, source), out List<Rule> values)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			Rule candidate = values[i];
			if (!AuthorityEquals(candidate.SourceAuthority, exactAuthority)) continue;
			if (Has(state, candidate.Upper) && !Has(state, candidate.Lower))
			{
				rule = candidate;
				return true;
			}
		}
		return false;
	}

	internal static bool TryResolveLocalLoss(string actorDaoTu, string authority, in XjXianJiState state, out Rule rule)
	{
		rule = default;
		string actor = Normalize(actorDaoTu);
		string exactAuthority = NormalizeAuthority(authority);
		if (actor.Length == 0 || exactAuthority.Length == 0 || state.Ids == null || state.Ids.Length == 0) return false;
		if (!LocalLossRules.TryGetValue(actor, out List<Rule> values)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			Rule candidate = values[i];
			if (!AuthorityEquals(candidate.SourceAuthority, exactAuthority)) continue;
			if (Has(state, candidate.Upper) && !Has(state, candidate.Lower))
			{
				rule = candidate;
				return true;
			}
		}
		return false;
	}

	internal static bool TryResolveUniqueSource(string actorDaoTu, string authority, out string sourceDaoTu)
	{
		sourceDaoTu = string.Empty;
		string actor = Normalize(actorDaoTu);
		string exactAuthority = NormalizeAuthority(authority);
		if (actor.Length == 0 || exactAuthority.Length == 0) return false;
		for (int i = 0; i < Rules.Length; i++)
		{
			Rule candidate = Rules[i];
			if (!string.Equals(candidate.ActorDaoTu, actor, StringComparison.Ordinal) || !AuthorityEquals(candidate.SourceAuthority, exactAuthority)) continue;
			if (sourceDaoTu.Length > 0 && !string.Equals(sourceDaoTu, candidate.SourceDaoTu, StringComparison.Ordinal))
			{
				sourceDaoTu = string.Empty;
				return false;
			}
			sourceDaoTu = candidate.SourceDaoTu;
		}
		return sourceDaoTu.Length > 0;
	}

	internal static bool AuthorityEquals(string left, string right) => string.Equals(NormalizeAuthority(left), NormalizeAuthority(right), StringComparison.Ordinal);

	internal static string NormalizeAuthority(string authority)
	{
		string value = (authority ?? string.Empty).Trim().Trim('【', '】', '“', '”', '"');
		int separator = value.IndexOf('·');
		if (separator >= 0 && separator + 1 < value.Length && value.Substring(0, separator).EndsWith("意向", StringComparison.Ordinal)) value = value.Substring(separator + 1).Trim();
		return XjGuoWeiAuthorityCatalog.NormalizeAuthorityNameAny(value);
	}

	private static Dictionary<string, List<Rule>> BuildGainRules()
	{
		Dictionary<string, List<Rule>> result = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
		for (int i = 0; i < Rules.Length; i++)
		{
			Rule rule = Rules[i];
			string key = BuildGainKey(rule.ActorDaoTu, rule.SourceDaoTu);
			if (!result.TryGetValue(key, out List<Rule> values)) { values = new List<Rule>(); result[key] = values; }
			values.Add(rule);
		}
		return result;
	}

	private static Dictionary<string, List<Rule>> BuildLocalLossRules()
	{
		Dictionary<string, List<Rule>> result = new Dictionary<string, List<Rule>>(StringComparer.Ordinal);
		for (int i = 0; i < Rules.Length; i++)
		{
			Rule rule = Rules[i];
			if (!string.Equals(rule.ActorDaoTu, rule.SourceDaoTu, StringComparison.Ordinal)) continue;
			if (!result.TryGetValue(rule.ActorDaoTu, out List<Rule> values)) { values = new List<Rule>(); result[rule.ActorDaoTu] = values; }
			values.Add(rule);
		}
		return result;
	}

	private static string BuildGainKey(string actorDaoTu, string sourceDaoTu) => Normalize(actorDaoTu) + ">" + Normalize(sourceDaoTu);

	private static string NormalizeUpperForBinding(string value, string expectedDaoTu)
	{
		string normalized = (value ?? string.Empty).Trim();
		if (XjZiJinUpperShenTongCatalog.TryGet(normalized, out XjZiJinUpperShenTongDefinition definition)
			&& string.Equals(definition.DaoTuDisplayName, expectedDaoTu, StringComparison.Ordinal)
			&& !string.IsNullOrWhiteSpace(definition.DisplayName))
		{
			return definition.DisplayName;
		}
		return XjXianJiCatalog.NormalizeXianJiId(normalized);
	}

	private static bool Has(in XjXianJiState state, string id)
	{
		string expected = XjXianJiCatalog.NormalizeXianJiId(id);
		for (int i = 0; i < state.Ids.Length; i++)
			if (string.Equals(XjXianJiCatalog.NormalizeXianJiId(state.Ids[i]), expected, StringComparison.Ordinal)) return true;
		return false;
	}

	private static string Normalize(string text) => (text ?? string.Empty).Trim();

	private static Rule R(string actorDaoTu, string sourceDaoTu, string sourceAuthority, string lower, string upper, bool sourceListed, string proofMethod)
	{
		return new Rule(
			actorDaoTu,
			sourceDaoTu,
			sourceAuthority,
			lower,
			upper,
			sourceListed,
			NormalizeTopologyProofMethod(actorDaoTu, sourceDaoTu, proofMethod));
	}

	private static string NormalizeTopologyProofMethod(string actorDaoTu, string sourceDaoTu, string proofMethod)
	{
		string proof = proofMethod ?? string.Empty;
		if (!proof.StartsWith("邻道权柄共鸣", StringComparison.Ordinal)) return proof;

		return XjDaoTuRelationCatalog.Resolve(actorDaoTu, sourceDaoTu) switch
		{
			XjDaoTuRelationKind.DirectAdjacent => proof,
			XjDaoTuRelationKind.Counterpart => "对炁权柄共鸣（道统补录）",
			XjDaoTuRelationKind.SameRootRemote => "同根远亲权柄异解（道统补录）",
			XjDaoTuRelationKind.ElementAffinity => "五德映照权柄异解（道统补录）",
			_ => "外道权柄异解（道统补录）"
		};
	}
}
