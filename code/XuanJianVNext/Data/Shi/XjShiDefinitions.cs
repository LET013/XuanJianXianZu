using System;

namespace XuanJianVNext.Data.Shi;

/// <summary>
/// 释修是与紫府金丹、服气养性并列的第三套修炼体系。
/// 这里的 ID 只描述释修自身事实，绝不写入仙道 RealmId。
/// </summary>
internal static class XjShiTraditionIds
{
	internal const string Ancient = "ancient";
	internal const string Modern = "modern";
}


internal static class XjShiLineageIds
{
	internal const string NorthWorldHonored = "north_world_honored";
	internal const string GreatDesire = "great_desire";
	internal const string Wrath = "wrath";
	internal const string DharmaAdmiration = "dharma_admiration";
	internal const string Discipline = "discipline";
	internal const string GoodJoy = "good_joy";
	internal const string Compassion = "compassion";
	internal const string Emptiness = "emptiness";
	internal const string ModernUnassigned = "modern_unassigned";

	private static readonly string[] ModernLineages =
	{
		GreatDesire, Wrath, DharmaAdmiration, Discipline, GoodJoy, Compassion, Emptiness
	};

	internal static string ResolveDefaultModern(long actorId)
	{
		// 0.9.6.8起，普通今释必须继承师承或释土法脉；ActorId随机只保留旧档兼容，
		// 不再作为新角色的权威来源。
		_ = actorId;
		return ModernUnassigned;
	}

	internal static string ResolveManualModern(long actorId)
	{
		return ModernLineages[XuanJianVNext.Core.XjDeterministicHash.PositiveIndex(
			actorId, "shi_manual_modern_lineage", ModernLineages.Length)];
	}

	internal static bool IsConcreteModern(string lineageId)
	{
		for (int i = 0; i < ModernLineages.Length; i++)
			if (string.Equals(lineageId, ModernLineages[i], StringComparison.Ordinal)) return true;
		return false;
	}

	internal static string ResolveDefault(string tradition, long actorId)
	{
		return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? NorthWorldHonored
			: ResolveDefaultModern(actorId);
	}

	internal static bool IsKnown(string lineageId)
	{
		if (string.Equals(lineageId, NorthWorldHonored, StringComparison.Ordinal)
			|| string.Equals(lineageId, ModernUnassigned, StringComparison.Ordinal)) return true;
		for (int i = 0; i < ModernLineages.Length; i++)
		{
			if (string.Equals(lineageId, ModernLineages[i], StringComparison.Ordinal)) return true;
		}
		return false;
	}
}

internal static class XjShiRealmIds
{
	internal const string Monk = "monk";
	internal const string DharmaMaster = "dharma_master";
	internal const string LianMin = "lianmin";
	internal const string MoHe = "mohe";
	internal const string DharmaForm = "dharma_form";
	internal const string WorldHonored = "world_honored";
}

/// <summary>
/// 萨陲、发慧、金莲均是怜愍境内的位次，不得注册为独立境界。
/// </summary>
internal static class XjShiSeatIds
{
	internal const string SaTuo = "satuo";
	internal const string FaHui = "fahui";
	internal const string JinLian = "jinlian";
}

internal static class XjShiPositionStatusIds
{
	internal const string None = "none";
	internal const string Attached = "attached";
	internal const string ReturnedToShiTu = "returned_to_shitu";
	internal const string Orphaned = "orphaned";
	internal const string SuccessionCandidate = "succession_candidate";
	internal const string ReincarnationReserved = "reincarnation_reserved";
	internal const string Displaced = "displaced";
}

internal static class XjShiJinDiStatusIds
{
	internal const string None = "none";
	internal const string Manifest = "manifest";
	internal const string Hidden = "hidden";
	internal const string WaitingForRebirth = "waiting_for_rebirth";
}

internal static class XjShiRebirthStateIds
{
	internal const string None = "none";
	internal const string Recovering = "recovering";
	internal const string Restored = "restored";
}

internal static class XjShiDharmaFormStageIds
{
	internal const string None = "none";
	internal const string OriginalVow = "original_vow";
	internal const string ResponseBody = "response_body";
	internal const string SelfReturned = "self_returned";
	internal const string WorldHonoredPath = "world_honored_path";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, None, StringComparison.Ordinal)
			|| string.Equals(value, OriginalVow, StringComparison.Ordinal)
			|| string.Equals(value, ResponseBody, StringComparison.Ordinal)
			|| string.Equals(value, SelfReturned, StringComparison.Ordinal)
			|| string.Equals(value, WorldHonoredPath, StringComparison.Ordinal);
	}
}


internal static class XjShiDharmaFormCandidateIds
{
	internal const string None = "none";
	internal const string SourceRulesMissing = "source_rules_missing"; // 旧档兼容
	internal const string ManualRecordReady = "manual_record_ready"; // 旧档兼容
	internal const string LiveOwnerBlocks = "live_owner_blocks";
	internal const string Insufficient = "insufficient";
	internal const string Eligible = "eligible";
	internal const string AttemptCooldown = "attempt_cooldown";
	internal const string Owner = "owner";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, None, StringComparison.Ordinal)
			|| string.Equals(value, SourceRulesMissing, StringComparison.Ordinal)
			|| string.Equals(value, ManualRecordReady, StringComparison.Ordinal)
			|| string.Equals(value, LiveOwnerBlocks, StringComparison.Ordinal)
			|| string.Equals(value, Insufficient, StringComparison.Ordinal)
			|| string.Equals(value, Eligible, StringComparison.Ordinal)
			|| string.Equals(value, AttemptCooldown, StringComparison.Ordinal)
			|| string.Equals(value, Owner, StringComparison.Ordinal);
	}
}

internal static class XjShiWorldHonoredReadinessIds
{
	internal const string Locked = "locked";
	internal const string StructuralReady = "structural_ready";
	internal const string OnPath = "on_path";
	internal const string Eligible = "eligible";
	internal const string AttemptCooldown = "attempt_cooldown";
	internal const string Completed = "completed";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, Locked, StringComparison.Ordinal)
			|| string.Equals(value, StructuralReady, StringComparison.Ordinal)
			|| string.Equals(value, OnPath, StringComparison.Ordinal)
			|| string.Equals(value, Eligible, StringComparison.Ordinal)
			|| string.Equals(value, AttemptCooldown, StringComparison.Ordinal)
			|| string.Equals(value, Completed, StringComparison.Ordinal);
	}
}

internal static class XjShiTraitIds
{
	// 释修路线的基础身份投影。它对应仙修的 XjZz 修炼资质入口，但只标记
	// “此人已踏上释修”，不等同于古释/今释道统或任何释修境界。
	internal const string Seed = "XjShiSeed";
	internal const string Monk = "XjRealm21";
	internal const string DharmaMaster = "XjRealm22";
	internal const string LianMin = "XjRealm23";
	internal const string MoHe = "XjRealm24";
	internal const string DharmaForm = "XjRealm25";
	internal const string WorldHonored = "XjRealm26";
	internal const string Ancient = "XjGuShiDao";
	internal const string Modern = "XjJinShiDao";
}

internal static class XjShiSourceIds
{
	internal const string ManualRecord = "manual_record";
	internal const string Master = "master";
	internal const string Scripture = "scripture";
	internal const string Relic = "relic";
	internal const string Temple = "temple";
	internal const string JinDi = "jindi";
	internal const string Reincarnation = "reincarnation";
	internal const string Conversion = "conversion";
}

internal static class XjShiLawIds
{
	// 仅保留旧档迁移标识。0.9.8.5起释修不存在功法或法门槽，读取后立即清空。
	internal const string BasicDiscipline = "shi_basic_discipline";
}

internal static class XjShiPracticeDirectionIds
{
	internal const string Unassigned = "unassigned";
	internal const string WuLiang = XjShiHeavenCatalog.WuLiang;
	internal const string WuBian = XjShiHeavenCatalog.WuBian;
	internal const string WuYang = XjShiHeavenCatalog.WuYang;
	internal const string WuDeng = XjShiHeavenCatalog.WuDeng;

	private static readonly string[] All = { WuLiang, WuBian, WuYang, WuDeng };

	internal static bool IsKnown(string value)
	{
		for (int i = 0; i < All.Length; i++)
			if (string.Equals(value, All[i], StringComparison.Ordinal)) return true;
		return false;
	}

	internal static string ResolveAncientChoice(long actorId)
	{
		return All[XuanJianVNext.Core.XjDeterministicHash.PositiveIndex(
			actorId, "shi_ancient_four_direction_choice", All.Length)];
	}

	internal static string GetDisplay(string value) => XjShiHeavenCatalog.GetCategoryDisplay(value);
	internal static string GetMeaning(string value) => XjShiHeavenCatalog.GetCategoryMeaning(value);
}

/// <summary>
/// 原著只给出结构关系，没有给出绝对修持门槛和席位容量。以下数值均为
/// WorldBox 年度结算所需的集中平衡参数，后续可只改此处而不改状态机。
/// </summary>
internal static class XjShiCatalog
{
	// 释修入门只认普通命数，不读取xjzz或慧光。门槛属于WorldBox平衡参数。
	// 0.9.11.1：自然入释从“常见高命数分流”降为稀有接引，避免释修人口长期压过两条仙道。
	internal const float NaturalEntryMingShuThreshold = 85f;
	internal const float AncientSeedMingShuThreshold = 85f;
	internal const float ManualMonkMingShuFloor = 70f;
	internal const float ManualDharmaMasterMingShuFloor = 100f;
	internal const float ManualLianMinMingShuFloor = 150f;
	internal const float ManualMoHeMingShuFloor = 300f;
	internal const float DharmaMasterPracticeThreshold = 6000f;
	internal const float LianMinPracticeThreshold = 15000f;
	internal const float FaHuiPracticeThreshold = 30000f;
	internal const float JinLianPracticeThreshold = 54000f;
	internal const float MoHePracticeThreshold = 72000f;
	// 今释常规路径仍是法师→怜愍→摩诃，但摩诃不能被“先有摩诃才能出怜愍”锁死。
	// 命数与修持同时达到高位门槛后，法师或怜愍每年拥有一次极低概率的越阶证摩诃机会。
	internal const float MoHeFateLeapPracticeThreshold = 72000f;
	internal const float MoHeFateLeapMingShuThreshold = 300f;
	internal const int MoHeFateLeapBaseChancePerTenThousand = 2;
	internal const int MoHeFateLeapNoLivingMoHeBonusPerTenThousand = 18;
	internal const int MoHeFateLeapMaximumChancePerTenThousand = 250;
	// 今释摩诃稀缺补位：0~3位时用高概率种子避免法师因“无摩诃可挂靠”形成生态死锁；
	// 第4位出现后立刻回到上面的原始高门槛/低概率逻辑。
	internal const int MoHeScarcityThreshold = 3;
	internal const int MoHeScarcity0ChancePerTenThousand = 8000;
	internal const int MoHeScarcity1ChancePerTenThousand = 6000;
	internal const int MoHeScarcity2ChancePerTenThousand = 4000;
	internal const int MoHeScarcity3ChancePerTenThousand = 2500;
	// 少量“宿世命数直证”：不靠修持年限堆概率，只在释修命数首次跨入新档时判定一次。
	// 极高命数可直接显出数世摩诃格位；900以上才存在极小概率九世俱现、直入法相。
	internal const float FateDirectLeapBand1MingShu = 450f;
	internal const float FateDirectLeapBand2MingShu = 600f;
	internal const float FateDirectLeapBand3MingShu = 750f;
	internal const float FateDirectLeapBand4MingShu = 900f;
	internal const float FateDirectLeapBand5MingShu = 975f;
	internal const int FateDirectLeapBand1ChancePerTenThousand = 30;   // 0.30%：二至三世摩诃
	internal const int FateDirectLeapBand2ChancePerTenThousand = 60;   // 0.60%：三至五世摩诃
	internal const int FateDirectLeapBand3ChancePerTenThousand = 100;  // 1.00%：五至七世摩诃
	internal const int FateDirectLeapBand4ChancePerTenThousand = 180;  // 1.80%：七至九世摩诃，含极微法相
	internal const int FateDirectLeapBand5ChancePerTenThousand = 300;  // 3.00%：九世摩诃，含极微法相
	internal const int FateDirectDharmaFormBand4ChancePerTenThousand = 5;  // 0.05%
	internal const int FateDirectDharmaFormBand5ChancePerTenThousand = 50; // 0.50%
	internal const float RebirthLianMinRecoveryThreshold = 6000f;
	internal const float RebirthMoHeRecoveryThreshold = 12000f;
	internal const float RebirthDharmaFormRecoveryThreshold = 36000f;
	// 承载地与位次容量均为年度派生值。原著只确认“广增释土、法相/摩诃修为提升会增位”，
	// 未给出绝对数量；以下为 WorldBox 平衡参数，不作为原著定数展示。
	internal const int BaseDomainMoHeCapacity = 1;
	internal const int MaximumDomainMoHeCapacity = 8;
	internal const int BaseLianMinCapacityPerMoHe = 1;
	internal const int MaximumLianMinCapacityPerMoHe = 12;
	internal const int DomainGrowthPerCapacity = 250;
	internal const int DomainHostVacancyGraceYears = 10;
	internal const int DomainShockYears = 20;
	internal const int SuccessionGraceYearsWhenRebirthQueued = 120;
	internal const int SaTuoOrphanGraceYears = 5;
	internal const float MaximumShiMingShu = 1000f;
	internal const float UnanchoredModernPracticeMultiplier = 0.75f;
	internal const float AncientSelfPracticeMultiplier = 1.00f;
	internal const float MaximumJinDiPracticeMultiplier = 2.25f;
	// 0.9.9.2：今释人口入口放宽后，按位阶压低长期修持效率；古释保持原速。
	internal const float ModernMonkPracticePaceMultiplier = 0.45f;
	internal const float ModernDharmaMasterPracticePaceMultiplier = 0.40f;
	internal const float ModernLianMinPracticePaceMultiplier = 0.35f;
	internal const float ModernMoHePracticePaceMultiplier = 0.30f;
	internal const float ModernDharmaFormPracticePaceMultiplier = 0.25f;
	internal const float ModernWorldHonoredPracticePaceMultiplier = 0.20f;
	internal const int JinDiScaleScorePerFivePercent = 240;
	internal const int ShiMingShuInsightIntervalYears = 10;
	internal const int ShiMingShuAnnualEventCap = 120;
	internal const int JinDiAbsorptionIntervalYears = 10;
	internal const int JinDiAbsorptionGrowth = 150;
	internal const int HeavenFragmentAttemptIntervalYears = 25;
	internal const int TempleMasterBaseChancePerTenThousand = 900;
	internal const int TempleMasterMaximumChancePerTenThousand = 4200;
	internal const int HeavenFragmentBaseChancePerTenThousand = 1000;
	internal const int HeavenFragmentMaximumChancePerTenThousand = 6200;
	// 原著确认高位依赖位次、释土/金地、轮回与旃檀林，但未给出绝对门槛和成功率。
	// 以下为完成 WorldBox 年度闭环所需的集中工程参数，不作为原著定数展示。
	internal const int HighRealmAuditVersion = 6;
	internal const int MoHeVoluntaryReincarnationMinimumYears = 80;
	internal const int TempleMasterMinimumCompletedLives = 6; // 七世开始感地，九世方可证法相
	internal const int DharmaFormManualRecordMinimumLives = 8; // 兼容旧调试入口
	internal const float DharmaFormManualRecordMinimumMingShu = 600f; // 兼容旧调试入口
	internal const int DharmaFormMinimumLives = 8;
	internal const float DharmaFormPracticeThreshold = 160000f;
	internal const float AncientDharmaFormPracticeThreshold = 180000f;
	internal const float DharmaFormMinimumMingShu = 600f;
	// 古释法师自证法相与高位法相系统统一使用独立常量，避免自然入口硬编码80、
	// 高位门禁却要求600造成两套口径。当前平衡值与通用法相门槛一致。
	internal const float AncientDharmaFormMinimumMingShu = 600f;
	internal const int DharmaFormMinimumDomainGrowth = 500;
	internal const int TempleMasterInitialDomainGrowth = 120; // 初得金地只开根基，不再瞬间补满法相门槛
	// 九世今释摩诃取得北世尊金地后，进入独立的“庙主温养”阶段。
	// 这三项只补到法相最低门槛，不继续制造高位滚雪球；按当前门槛，
	// 一个刚得地且底子一般的九世摩诃约40~60年即可进入首次法相尝试。
	internal const float TempleMasterAnnualDharmaFormPracticeGain = 2200f;
	internal const float TempleMasterAnnualDharmaFormMingShuGain = 12f;
	internal const int TempleMasterAnnualDomainGrowthGain = 12;
	// 古释法相五年清静点化既增释修命数，也温养自身自证金地。按1点点化命数
	// 折20点承载增长，使500→750→1250→2000→3000与40/60/90/120年层次周期可自然闭环。
	internal const int AncientDuhuaDomainGrowthPerMingShu = 20;
	internal const int DharmaFormAttemptIntervalYears = 25;
	internal const int DharmaFormBaseChancePerTenThousand = 1800;
	internal const int DharmaFormMaximumChancePerTenThousand = 7800;
	internal const float DharmaFormFailurePracticeRetention = 0.90f;
	internal const int DharmaFormFailureMingShuLoss = 25;
	internal const int DharmaFormFailureShockYears = 20;
	internal const float ResponseBodyPracticeThreshold = 220000f;
	internal const float SelfReturnedPracticeThreshold = 320000f;
	internal const float WorldHonoredPathPracticeThreshold = 460000f;
	internal const int ResponseBodyMinimumYears = 40;
	internal const int SelfReturnedMinimumYears = 60;
	internal const int WorldHonoredPathMinimumYears = 90;
	internal const float ResponseBodyMingShuThreshold = 700f;
	internal const float SelfReturnedMingShuThreshold = 800f;
	internal const float WorldHonoredPathMingShuThreshold = 900f;
	internal const int ResponseBodyDomainGrowthThreshold = 750;
	internal const int SelfReturnedDomainGrowthThreshold = 1250;
	internal const int WorldHonoredPathDomainGrowthThreshold = 2000;
	internal const int DharmaFormStageAttemptIntervalYears = 10;
	internal const int ResponseBodyRiskRelapseThreshold = 10000;
	internal const float WorldHonoredPracticeThreshold = 650000f;
	internal const float WorldHonoredMingShuThreshold = 950f;
	internal const int WorldHonoredDomainGrowthThreshold = 3000;
	internal const int WorldHonoredMinimumPathYears = 120;
	internal const int WorldHonoredAttemptIntervalYears = 100;
	internal const int WorldHonoredBaseChancePerTenThousand = 600;
	internal const int WorldHonoredMaximumChancePerTenThousand = 3000;
	internal const float WorldHonoredFailurePracticeRetention = 0.75f;
	internal const int WorldHonoredFailureMingShuLoss = 50;
	internal const int WorldHonoredFailureDomainGrowthLoss = 250;
	internal const int WorldHonoredFailureTrueSpiritLockYears = 80;
	internal const int YouTanLinDisturbanceLockYears = 60;
	internal const int WorldHonoredDisturbanceLockYears = 120;
	internal const int YouTanLinDisturbanceResponseBodyRisk = 2500;

	internal static bool IsKnownTradition(string tradition)
	{
		return string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			|| string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
	}

	internal static bool IsKnownRealm(string realm)
	{
		return string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
			|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal);
	}

	internal static bool IsKnownSeat(string seatId)
	{
		return string.Equals(seatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal)
			|| string.Equals(seatId, XjShiSeatIds.FaHui, StringComparison.Ordinal)
			|| string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal);
	}

	internal static int GetRank(string realm)
	{
		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal)) return 1;
		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) return 2;
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return 3;
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return 4;
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return 5;
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return 6;
		return 0;
	}

	internal static int GetSeatRank(string seatId)
	{
		if (string.Equals(seatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal)) return 1;
		if (string.Equals(seatId, XjShiSeatIds.FaHui, StringComparison.Ordinal)) return 2;
		if (string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal)) return 3;
		return 0;
	}

	internal static string GetRealmDisplay(string realm)
	{
		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal)) return "僧侣";
		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) return "法师";
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return "怜愍";
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return "摩诃";
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return "法相";
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return "世尊";
		return "未入释门";
	}

	internal static string GetMoHeStageDisplay(int currentLife)
	{
		int life = Math.Max(1, Math.Min(9, currentLife));
		return life switch
		{
			1 => "一世摩诃",
			2 => "二世摩诃",
			3 => "三世摩诃",
			4 => "四世摩诃",
			5 => "五世摩诃",
			6 => "六世摩诃",
			7 => "七世摩诃",
			8 => "八世摩诃",
			_ => "九世摩诃"
		};
	}

	internal static string GetSeatDisplay(string seatId)
	{
		if (string.Equals(seatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal)) return "萨陲座";
		if (string.Equals(seatId, XjShiSeatIds.FaHui, StringComparison.Ordinal)) return "发慧座";
		if (string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal)) return "金莲座";
		return "未得座次";
	}

	internal static string GetSeatNonRegressionDisplay(string seatId)
	{
		if (string.Equals(seatId, XjShiSeatIds.JinLian, StringComparison.Ordinal)) return "形、念不退；法身崩毁可归释土恢复";
		if (string.Equals(seatId, XjShiSeatIds.FaHui, StringComparison.Ordinal)) return "念不退；可退回释土";
		if (string.Equals(seatId, XjShiSeatIds.SaTuo, StringComparison.Ordinal)) return "不退转最低";
		return "无";
	}


	internal static string GetLineageDisplay(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)) return "北世尊道";
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return "大欲";
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) return "忿怒";
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return "慕法";
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return "戒律";
		if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) return "善乐";
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return "慈悲";
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return "空无";
		if (string.Equals(lineageId, XjShiLineageIds.ModernUnassigned, StringComparison.Ordinal)) return "七相未定";
		return "法脉未定";
	}

	internal static string GetTraditionDisplay(string tradition)
	{
		return string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			? "今释"
			: string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				? "古释"
				: "释统未定";
	}

	internal static string GetLineageIdeaDisplay(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)) return "重渡己，求解脱";
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return "无边，先得后失，以求庄严";
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) return "显相，平恶为善，塑成宝相";
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return "求界，释土当世，普渡众生";
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return "苦寒，知悉苦海，遂有舟渡";
		if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) return "明心，喜怒嗔痴，以有求无";
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return "在肚，抚养众生，众生养我";
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return "专心，往生我执，以求解脱";
		return "法脉理念未定";
	}

	internal static string GetPositionStatusDisplay(string status)
	{
		if (string.Equals(status, XjShiPositionStatusIds.Attached, StringComparison.Ordinal)) return "在位";
		if (string.Equals(status, XjShiPositionStatusIds.ReturnedToShiTu, StringComparison.Ordinal)) return "退回释土待位";
		if (string.Equals(status, XjShiPositionStatusIds.Orphaned, StringComparison.Ordinal)) return "座主已失";
		if (string.Equals(status, XjShiPositionStatusIds.SuccessionCandidate, StringComparison.Ordinal)) return "旧档失位（待绝命）";
		if (string.Equals(status, XjShiPositionStatusIds.ReincarnationReserved, StringComparison.Ordinal)) return "座主轮回待归";
		if (string.Equals(status, XjShiPositionStatusIds.Displaced, StringComparison.Ordinal)) return "原位已易主";
		return "无位";
	}

	internal static string GetJinDiStatusDisplay(string status)
	{
		if (string.Equals(status, XjShiJinDiStatusIds.Manifest, StringComparison.Ordinal)) return "显世";
		if (string.Equals(status, XjShiJinDiStatusIds.Hidden, StringComparison.Ordinal)) return "隐世";
		if (string.Equals(status, XjShiJinDiStatusIds.WaitingForRebirth, StringComparison.Ordinal)) return "隐世·待轮回归位";
		return "未证金地";
	}

	internal static string GetDharmaFormStageDisplay(string stage)
	{
		if (string.Equals(stage, XjShiDharmaFormStageIds.OriginalVow, StringComparison.Ordinal)) return "本性立愿";
		if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)) return "相成应身";
		if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)) return "证相归本";
		if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)) return "世尊之路";
		return "未录法相层次";
	}


	internal static string GetDharmaFormCandidateDisplay(string state)
	{
		if (string.Equals(state, XjShiDharmaFormCandidateIds.Owner, StringComparison.Ordinal)) return "已据法相位，成为承载地主人";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.Eligible, StringComparison.Ordinal)) return "法相承位条件已足，等待年度争位";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.AttemptCooldown, StringComparison.Ordinal)) return "本次承位未成，正在温养";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.Insufficient, StringComparison.Ordinal)) return "世数、修持、命数或承载增长不足";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.ManualRecordReady, StringComparison.Ordinal)) return "旧档补录条件已备";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.LiveOwnerBlocks, StringComparison.Ordinal)) return "承载地已有在世法相主人";
		if (string.Equals(state, XjShiDharmaFormCandidateIds.SourceRulesMissing, StringComparison.Ordinal)) return "旧版法相入口已迁移";
		return "未进入法相候位检查";
	}

	internal static string GetWorldHonoredReadinessDisplay(string state)
	{
		if (string.Equals(state, XjShiWorldHonoredReadinessIds.Completed, StringComparison.Ordinal)) return "已证世尊";
		if (string.Equals(state, XjShiWorldHonoredReadinessIds.Eligible, StringComparison.Ordinal)) return "证道条件已足，等待百年证道事务";
		if (string.Equals(state, XjShiWorldHonoredReadinessIds.AttemptCooldown, StringComparison.Ordinal)) return "证道受挫，正在重聚本性与真灵";
		if (string.Equals(state, XjShiWorldHonoredReadinessIds.OnPath, StringComparison.Ordinal)) return "已上世尊之路";
		if (string.Equals(state, XjShiWorldHonoredReadinessIds.StructuralReady, StringComparison.Ordinal)) return "法相结构已备，尚需修持、命数与释土增长";
		return "尚未上世尊之路";
	}

	internal static string GetRealmTraitId(string realm)
	{
		if (string.Equals(realm, XjShiRealmIds.Monk, StringComparison.Ordinal)) return XjShiTraitIds.Monk;
		if (string.Equals(realm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)) return XjShiTraitIds.DharmaMaster;
		if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) return XjShiTraitIds.LianMin;
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)) return XjShiTraitIds.MoHe;
		if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)) return XjShiTraitIds.DharmaForm;
		if (string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal)) return XjShiTraitIds.WorldHonored;
		return string.Empty;
	}

	internal static string GetTraditionTraitId(string tradition)
	{
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return XjShiTraitIds.Ancient;
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return XjShiTraitIds.Modern;
		return string.Empty;
	}

	internal static bool TryResolveRealmByTrait(string traitId, out string realm)
	{
		realm = string.Empty;
		if (string.Equals(traitId, XjShiTraitIds.Monk, StringComparison.Ordinal)) realm = XjShiRealmIds.Monk;
		else if (string.Equals(traitId, XjShiTraitIds.DharmaMaster, StringComparison.Ordinal)) realm = XjShiRealmIds.DharmaMaster;
		else if (string.Equals(traitId, XjShiTraitIds.LianMin, StringComparison.Ordinal)) realm = XjShiRealmIds.LianMin;
		else if (string.Equals(traitId, XjShiTraitIds.MoHe, StringComparison.Ordinal)) realm = XjShiRealmIds.MoHe;
		else if (string.Equals(traitId, XjShiTraitIds.DharmaForm, StringComparison.Ordinal)) realm = XjShiRealmIds.DharmaForm;
		else if (string.Equals(traitId, XjShiTraitIds.WorldHonored, StringComparison.Ordinal)) realm = XjShiRealmIds.WorldHonored;
		return realm.Length > 0;
	}

	internal static bool TryResolveTraditionByTrait(string traitId, out string tradition)
	{
		tradition = string.Empty;
		if (string.Equals(traitId, XjShiTraitIds.Ancient, StringComparison.Ordinal)) tradition = XjShiTraditionIds.Ancient;
		else if (string.Equals(traitId, XjShiTraitIds.Modern, StringComparison.Ordinal)) tradition = XjShiTraditionIds.Modern;
		return tradition.Length > 0;
	}
}

internal readonly struct XjShiSnapshot
{
	internal readonly bool Found;
	internal readonly string Tradition;
	internal readonly string Realm;
	internal readonly float Practice;
	internal readonly string LawIds;
	internal readonly string EntrySource;
	internal readonly int RealmEnteredYear;
	internal readonly int LastAnnualYear;
	internal readonly int CurrentLife;
	internal readonly int CompletedLives;
	internal readonly string PatronActorId;
	internal readonly string SeatId;
	internal readonly float SeatProgress;
	internal readonly int BorrowedPower;
	internal readonly int Alignment;
	internal readonly string PositionStatus;
	internal readonly string DomainId;
	internal readonly int IsMoHeLiangLi;
	internal readonly string JinDiId;
	internal readonly string JinDiStatus;
	internal readonly string RebirthAnchorId;
	internal readonly string RebirthTargetRealm;
	internal readonly string RebirthTargetSeat;
	internal readonly string RebirthState;
	internal readonly float RebirthRecovery;
	internal readonly int TrueSpiritLocked;
	internal readonly string SuccessionSourceActorId;
	internal readonly int SuccessionEligibleYear;
	internal readonly string DharmaFormStage;
	internal readonly string VowId;
	internal readonly int ResponseBodyRisk;

	internal XjShiSnapshot(
		bool found,
		string tradition,
		string realm,
		float practice,
		string lawIds,
		string entrySource,
		int realmEnteredYear,
		int lastAnnualYear,
		int currentLife,
		int completedLives,
		string patronActorId,
		string seatId,
		float seatProgress,
		int borrowedPower,
		int alignment,
		string positionStatus,
		string domainId,
		int isMoHeLiangLi,
		string jinDiId,
		string jinDiStatus,
		string rebirthAnchorId,
		string rebirthTargetRealm,
		string rebirthTargetSeat,
		string rebirthState,
		float rebirthRecovery,
		int trueSpiritLocked,
		string successionSourceActorId,
		int successionEligibleYear,
		string dharmaFormStage,
		string vowId,
		int responseBodyRisk)
	{
		Found = found;
		Tradition = tradition ?? string.Empty;
		Realm = realm ?? string.Empty;
		Practice = Math.Max(0f, practice);
		LawIds = lawIds ?? string.Empty;
		EntrySource = entrySource ?? string.Empty;
		RealmEnteredYear = Math.Max(0, realmEnteredYear);
		LastAnnualYear = Math.Max(0, lastAnnualYear);
		CurrentLife = Math.Max(1, currentLife);
		CompletedLives = Math.Max(0, completedLives);
		PatronActorId = patronActorId ?? string.Empty;
		SeatId = seatId ?? string.Empty;
		SeatProgress = Math.Max(0f, seatProgress);
		BorrowedPower = Math.Max(0, borrowedPower);
		Alignment = Math.Clamp(alignment, 0, 100);
		PositionStatus = positionStatus ?? string.Empty;
		DomainId = domainId ?? string.Empty;
		IsMoHeLiangLi = Math.Max(0, isMoHeLiangLi);
		JinDiId = jinDiId ?? string.Empty;
		JinDiStatus = jinDiStatus ?? string.Empty;
		RebirthAnchorId = rebirthAnchorId ?? string.Empty;
		RebirthTargetRealm = rebirthTargetRealm ?? string.Empty;
		RebirthTargetSeat = rebirthTargetSeat ?? string.Empty;
		RebirthState = rebirthState ?? string.Empty;
		RebirthRecovery = Math.Max(0f, rebirthRecovery);
		TrueSpiritLocked = Math.Max(0, trueSpiritLocked);
		SuccessionSourceActorId = successionSourceActorId ?? string.Empty;
		SuccessionEligibleYear = Math.Max(0, successionEligibleYear);
		DharmaFormStage = dharmaFormStage ?? string.Empty;
		VowId = vowId ?? string.Empty;
		ResponseBodyRisk = Math.Clamp(responseBodyRisk, 0, 10000);
	}
}
