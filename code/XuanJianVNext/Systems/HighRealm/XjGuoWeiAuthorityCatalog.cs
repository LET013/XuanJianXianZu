using System;
using System.Collections.Generic;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjGuoWeiAuthorityCatalog
{
	private static readonly Dictionary<string, string[]> entries = new Dictionary<string, string[]>(StringComparer.Ordinal)
	{
		["太阴"] = new[] { "太阴星主", "诸阴所宗", "道藏之藏", "避劫藏真", "月华幽元", "太素之诣" },
		["少阴"] = new[] { "少阴化羽", "华池见冰", "大暄含寒", "主阴而燥", "少阴枢机", "蟾宫折桂" },
		["厥阴"] = new[] { "厥阴生衍", "群居乱交", "上修在身", "下衍百邪", "牝水之因", "青鸾之唳" },
		["太阳"] = new[] { "太阳星主", "五道皆明", "日间显耀", "诸阳景从", "极盛御宇", "帝冕冠世" },
		["少阳"] = new[] { "右东极星", "邪绝求变", "奉东君命", "青阳焕发", "残阳泣血", "目骋怀远" },
		["明阳"] = new[] { "生长交合", "男主女辅", "尊卑法统", "天光明火", "谒天门开", "君蹈危机" },
		["玄雷"] = new[] { "银白雷霆", "靖平之赦", "盛阳之墟", "天鸣之策", "律演威仪", "伐恶封坛" },
		["霄雷"] = new[] { "紫色雷霆", "冬雷之声", "春惊蛰起", "轻雷之落", "斡动紫气", "定周天序" },
		["元雷"] = new[] { "磁光本源", "主煞之仪", "元雷之罚", "九霄震动", "霄汉澄清", "玄雷之劫" },
		["兑金"] = new[] { "太白正位", "白色兑相", "消残雷劫", "多疑喜缺", "折毁锋芒", "杀害之心" },
		["逍金"] = new[] { "蕴位藏养", "逍遥无羁", "纳体归匮", "御金而行", "望商之锋", "敛芒藏锋" },
		["齐金"] = new[] { "收蓄之位", "与天齐满", "得体归元", "金戈肃杀", "齐天律令", "万军俯首" },
		["库金"] = new[] { "藏位秘纳", "启阵法藏", "养金精资", "金销洞开", "百宝殿藏", "铜锈之炁" },
		["庚金"] = new[] { "变革之位", "金色庚相", "发体殁变", "不畏火焚", "镂金石坚", "墓门荆棘" },
		["长庚"] = new[] { "万剑归宗", "意御形神", "寒锋照夜", "一剑断法", "兵革归元", "太白开天" },
		["角木"] = new[] { "生发受藏", "余阳所钟", "潇重之林", "黎运逢春", "乙木周全", "青龙之气" },
		["正木"] = new[] { "岁星正位", "甲乙交合", "木成方正", "位从专一", "见查之语", "万古长青" },
		["集木"] = new[] { "收蓄栖止", "众修云集", "隼栖木上", "祸延生发", "朱丹参同", "定元之春" },
		["更木"] = new[] { "变革破阵", "病前之春", "枯荣交迭", "更迭之术", "万古常新", "新陈之变" },
		["保木"] = new[] { "蕴位藏养", "蕴长生道", "万劫不坏", "古盘之根", "森罗之相", "栋梁之材" },
		["坎水"] = new[] { "辰星正位", "溪上之翁", "浩瀚在怀", "据岭中流", "长云暗雪", "恨江东去" },
		["渌水"] = new[] { "变革符箓", "清浊自变", "洞泉之声", "清夕之雨", "丑癸藏形", "洗劫之露" },
		["合水"] = new[] { "收蓄水脉", "归流之处", "雾回阴转", "百川汇海", "包容之相", "四海承平" },
		["府水"] = new[] { "蕴位黑白", "朝寒之雨", "合黎之渊", "宿于穷冬", "广浚之湖", "养命之蛟" },
		["牝水"] = new[] { "生发疗伤", "佞无晨", "往生泉", "谿谷会", "参玄臟", "照夭胎" },
		["离火"] = new[] { "荧惑正位", "位从罗网", "顺平征伐", "大离之书", "九重之擭", "折焚尽烬" },
		["灴火"] = new[] { "变革白红", "燔旧之室", "白樆之心", "布燥之使", "秉灴之夏", "虚宫之火" },
		["并火"] = new[] { "收蓄灰黑", "焰中之乌", "乌从欲生", "心期焚灭", "兼险夺命", "至命而除" },
		["真火"] = new[] { "蕴位金纤", "天兜之火", "雉离之行", "真火之身", "燎原之势", "治命之神" },
		["牡火"] = new[] { "生发朦胧", "牡煞之火", "韬光避灾", "高陵之父", "受灴之龠", "光明之相" },
		["艮土"] = new[] { "正位防守", "愚赶山岳", "正源之谷", "山岳镇伏", "不动之尊", "艮止如山" },
		["戊土"] = new[] { "藏养山霞", "受天之抚", "仙无漏身", "戊心之岩", "厚德载物", "中宫定鼎" },
		["归土"] = new[] { "收蓄归墟", "狡兔落原", "土归尘埃", "万物归藏", "归土藏形", "观红尘事" },
		["宝土"] = new[] { "生发滋养", "高垒之燕", "藏纳之宫", "膏泽之治", "万众归心", "万宝敛藏" },
		["宣土"] = new[] { "变革金刚", "天下归心", "神用其命", "训浚之明", "宣威仪仗", "德广配天" },
		["清炁"] = new[] { "玄风之本", "诸炁之母", "清元之风", "益养清炁", "浮云之身", "炁引灵池" },
		["紫炁"] = new[] { "紫金道行", "列紫之篇", "绕东山行", "道始兆端", "坤辰之修", "养生之主" },
		["真炁"] = new[] { "修武晶莹", "抱石而眠", "霞羽之客", "鹤挂衣冠", "授人长生", "蜕凡超尘" },
		["邃炁"] = new[] { "玄黄乌焰", "代行妨天", "闇天之秧", "逆乱人伦", "众生之苦", "乱天之理" },
		["寒炁"] = new[] { "三阴生寒", "入清听", "松上雪", "祢水寒", "沆砀满", "青霄女" },
		["晞炁"] = new[] { "三阳驭晞", "未阕之华", "乞代永夜", "郁燠之苦", "庆垂之轩", "议八辟法" },
		["瑞炁"] = new[] { "感应福祸", "好功之箓", "瑞气祥云", "祥云呈现", "福星高照", "瑞炁呈祥" },
		["煞炁"] = new[] { "凶煞降临", "不空之劫", "罗刹之海", "千百化身", "人中屠戮", "戮天之元" },
		["华炁"] = new[] { "五彩宝光", "冠灵之旒", "众生之念", "仪仗齐全", "光华显现", "琉璃之履" },
		["谪炁"] = new[] { "受谪幽冥", "藏壑之舟", "薄虞之渊", "忘川之歌", "谪落人间", "幽冥归途" },
		["上仪"] = new[] { "超凡脱俗", "明心之筵", "致于缉熙", "观天之道", "无极而生", "射狩之王" },
		["下仪"] = new[] { "幽冥之身", "轮回之殿", "黄泉之路", "忘川之河", "了却前尘", "阴司敕令" },
		["鸺葵"] = new[] { "幽葵宿命", "枭狸分形", "风魇化阴", "夜翳无踪", "分命寄狸", "羽阴藏真" },
		["上巫"] = new[] { "巫天受命", "槐阴通幽", "帝雾应变", "血祭夺生", "隐名绝察", "地祝承命" },
		["玉真"] = new[] { "玉德成真", "玉身合命", "青崖照境", "真虚合同", "锦障问途", "白璧承阴" },
		["衡祝"] = new[] { "衡火祝命", "赤虎辟灾", "斛衡量厄", "血烬成域", "祭燎分灾", "双衡定命" },
		["青宣"] = new[] { "青岳宣土", "伏元镇脉", "玄羊司土", "岩神承天", "观冥察地", "下阳敷生" }
		,
		["全丹"] = new[] { "丹炉全性", "铅华照性", "汞养成身", "候神孕物", "金书通变", "性命归真" },
		["执孛"] = new[] { "孛曜主变", "形神两渡", "阴阳离绝", "王威断气", "孛光蜕形", "移相存命" },
		["司天"] = new[] { "司天总序", "曜辰闻事", "星象布序", "斗衡定方", "辰命守数", "浑仪列天" },
		["都卫"] = new[] { "四方都镇", "降魂蔽灵", "东岳沉虚", "西塬绝界", "南水化虚", "北庭封域" },
		["渊照"] = new[] { "渊镜留真", "沉月乱位", "无身照真", "返景映法", "涵影定真", "无波镇界" },
		// 虹霞承戊土空证而显，以“落霞成山”为根柄，其余五柄分别承五门虹霞上位神通。
		["虹霞"] = new[] { "落霞成山", "六彩凝华", "虹霓应身", "朝霞养形", "绮霞散劫", "阴阳含德" }
	};


	// 0.9.9.22 以前，后置道途曾直接拿神通名/临时描述当作六柄稳定键。
	// 现在将其迁移为与阴阳、三雷、五德、十二炁一致的独立权柄名。旧名仍只作为
	// 读档别名存在，所有新写入统一使用新权柄；这保证夺柄、失柄与道统状态不会断档。
	private static readonly Dictionary<string, Dictionary<string, string>> LegacyAliasesByDaoTu =
		new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal)
		{
			["长庚"] = A(("万剑所宗", "万剑归宗"), ("一意独尊", "意御形神"), ("剑气长存", "寒锋照夜"), ("锋断万法", "一剑断法"), ("兵革归位", "兵革归元"), ("太白照世", "太白开天")),
			["鸺葵"] = A(("枭逐狸", "幽葵宿命"), ("魇鬼阴", "枭狸分形"), ("枭夜行", "风魇化阴"), ("狸分命", "夜翳无踪"), ("阴羽藏", "分命寄狸"), ("幽葵宿符", "羽阴藏真")),
			["上巫"] = A(("槐荫鬼", "巫天受命"), ("应帝王", "槐阴通幽"), ("饮民血", "帝雾应变"), ("勿查我", "血祭夺生"), ("地巫祝", "隐名绝察"), ("上巫祝命", "地祝承命")),
			["玉真"] = A(("玉中人", "玉德成真"), ("青玉崖", "玉身合命"), ("道合真", "青崖照境"), ("问道锦", "真虚合同"), ("白玉盘", "锦障问途"), ("玉庭真卫", "白璧承阴")),
			["衡祝"] = A(("殿阳虎", "衡火祝命"), ("斛量灾", "赤虎辟灾"), ("满烬煌", "斛衡量厄"), ("祭火衡命", "血烬成域"), ("祝燎分灾", "祭燎分灾"), ("双衡镇火", "双衡定命")),
			["青宣"] = A(("青宣神岳", "青岳宣土"), ("伏元镇脉", "伏元镇脉"), ("玄羊司土", "玄羊司土"), ("观地冥权", "岩神承天"), ("上岩承天", "观冥察地"), ("下阳敷生", "下阳敷生")),
			["全丹"] = A(("全丹内炉", "丹炉全性"), ("九转丹性", "铅华照性"), ("性命归炉", "汞养成身"), ("候神金书", "候神孕物"), ("混铅制养", "金书通变"), ("全性养真", "性命归真")),
			["执孛"] = A(("执孛化形", "孛曜主变"), ("形神分合", "形神两渡"), ("孛光离绝", "阴阳离绝"), ("断气更相", "王威断气"), ("蜕形真录", "孛光蜕形"), ("倏动分身", "移相存命")),
			["司天"] = A(("司天布序", "司天总序"), ("浑仪列象", "曜辰闻事"), ("曜辰听命", "星象布序"), ("斗衡定历", "斗衡定方"), ("神布天章", "辰命守数"), ("星轨摄命", "浑仪列天")),
			["都卫"] = A(("都卫方镇", "四方都镇"), ("四卫摄山", "降魂蔽灵"), ("降魂镇域", "东岳沉虚"), ("东利西璟", "西塬绝界"), ("南悯北漠", "南水化虚"), ("卫庭护界", "北庭封域")),
			["渊照"] = A(("潜形入坎", "渊镜留真"), ("月沉玄渊", "沉月乱位"), ("渊鉴照真", "无身照真"), ("返景留真", "返景映法"), ("涵真不坠", "涵影定真"), ("一鉴无波", "无波镇界")),
			["虹霞"] = A(("落霞山", "落霞成山"), ("长霞雾", "六彩凝华"), ("蝃蝀东", "虹霓应身"), ("正漱朝", "朝霞养形"), ("余成绮", "绮霞散劫"), ("混含德", "阴阳含德"))
		};

	private static readonly Dictionary<string, string> LegacyAliasesAny = BuildLegacyAliasesAny();

	internal static string NormalizeAuthorityName(string daoTu, string authority)
	{
		string value = (authority ?? string.Empty).Trim();
		string owner = (daoTu ?? string.Empty).Trim();
		if (value.Length == 0) return string.Empty;
		if (owner.Length > 0 && LegacyAliasesByDaoTu.TryGetValue(owner, out Dictionary<string, string> aliases)
			&& aliases.TryGetValue(value, out string migrated)) return migrated;
		return LegacyAliasesAny.TryGetValue(value, out string anyMigrated) ? anyMigrated : value;
	}

	internal static string NormalizeAuthorityNameAny(string authority) => NormalizeAuthorityName(string.Empty, authority);

	internal static string NormalizeAuthoritySet(string raw, string preferredDaoTu = "")
	{
		if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
		string[] parts = raw.Split(new[] { ',', '，', '|', '、' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> result = new List<string>(parts.Length);
		for (int i = 0; i < parts.Length; i++)
		{
			string value = NormalizeAuthorityName(preferredDaoTu, parts[i]);
			if (value.Length > 0 && !result.Contains(value)) result.Add(value);
		}
		return string.Join(",", result);
	}

	internal static string NormalizeAuthoritySourceMap(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
		string[] parts = raw.Split(new[] { ';', '；' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> result = new List<string>(parts.Length);
		for (int i = 0; i < parts.Length; i++)
		{
			string entry = parts[i]?.Trim() ?? string.Empty;
			int colon = entry.IndexOf(':');
			if (colon <= 0) continue;
			string tail = entry.Substring(colon + 1).Trim();
			string source = tail;
			int slash = source.IndexOf('/');
			if (slash >= 0) source = source.Substring(0, slash).Trim();
			string authority = NormalizeAuthorityName(source, entry.Substring(0, colon));
			if (authority.Length == 0 || tail.Length == 0) continue;
			string normalized = authority + ":" + tail;
			if (!result.Contains(normalized)) result.Add(normalized);
		}
		return string.Join(";", result);
	}

	private static Dictionary<string, string> BuildLegacyAliasesAny()
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
		HashSet<string> ambiguous = new HashSet<string>(StringComparer.Ordinal);
		foreach (Dictionary<string, string> aliases in LegacyAliasesByDaoTu.Values)
		{
			foreach (KeyValuePair<string, string> pair in aliases)
			{
				if (string.Equals(pair.Key, pair.Value, StringComparison.Ordinal)) continue;
				if (result.TryGetValue(pair.Key, out string existing) && !string.Equals(existing, pair.Value, StringComparison.Ordinal))
				{
					ambiguous.Add(pair.Key);
					continue;
				}
				result[pair.Key] = pair.Value;
			}
		}
		foreach (string key in ambiguous) result.Remove(key);
		return result;
	}

	private static Dictionary<string, string> A(params (string OldName, string NewName)[] values)
	{
		Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.Ordinal);
		for (int i = 0; i < values.Length; i++) result[values[i].OldName] = values[i].NewName;
		return result;
	}

	internal static IReadOnlyList<string> Get(string daoTu)
	{
		return !string.IsNullOrWhiteSpace(daoTu) && entries.TryGetValue(daoTu.Trim(), out string[] values)
			? values
			: Array.Empty<string>();
	}
	internal static IReadOnlyList<string> GetAllDaoTus()
	{
		List<string> result = new List<string>(entries.Keys);
		result.Sort(StringComparer.Ordinal);
		return result;
	}

}
