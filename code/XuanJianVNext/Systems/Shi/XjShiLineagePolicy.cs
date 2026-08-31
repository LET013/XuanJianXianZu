using System;
using XuanJianVNext.Data.Shi;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 七相与北世尊道的轻量行为权重。这里只改变年度入口的频率、候选预算和结果权重，
/// 不创建独立AI状态机，也不逐帧扫描单位。数值均为游戏化参数，UI只展示理念与倾向。
/// </summary>
internal static class XjShiLineagePolicy
{
	internal static int GetPreachingInterval(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return 3;
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return 4;
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)
			|| string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return 5;
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)
			|| string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) return 7;
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return 8;
		return 6;
	}

	internal static int GetPreachingCandidateBudget(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return 16;
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return 14;
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return 8;
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return 6;
		return 12;
	}

	internal static int ModifyPreachingBasis(string lineageId, int basisPoints)
	{
		int delta = 0;
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) delta = 700;
		else if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) delta = 350;
		else if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) delta = 100;
		else if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) delta = -150;
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) delta = -300;
		return Math.Clamp(basisPoints + delta, 100, 5000);
	}

	internal static int ScaleDomainContribution(string lineageId, int amount)
	{
		if (amount <= 0) return 0;
		float factor = 1f;
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) factor = 1.35f;
		else if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) factor = 1.2f;
		else if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) factor = 1.25f;
		else if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) factor = 0.85f;
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) factor = 0.75f;
		return Math.Max(1, (int)Math.Round(amount * factor));
	}

	internal static float ScaleMingShuAward(string lineageId, float amount, string eventType)
	{
		if (amount <= 0f) return 0f;
		float factor = 1f;
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)
			&& string.Equals(eventType, "battle", StringComparison.Ordinal)) factor = 1.3f;
		else if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)
			&& string.Equals(eventType, "conversion", StringComparison.Ordinal)) factor = 1.25f;
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)
			&& string.Equals(eventType, "insight", StringComparison.Ordinal)) factor = 1.35f;
		else if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)
			&& string.Equals(eventType, "domain", StringComparison.Ordinal)) factor = 1.2f;
		return Math.Max(1f, amount * factor);
	}

	internal static int ModifySeatPromotionChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 300
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? -150
			: string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 150 : 0;
		return Math.Clamp(chance + delta, 1000, 9500);
	}

	internal static int ModifyMoHeFateLeapChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 30
			: string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 20
			: string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal) ? 10
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? -10 : 0;
		return Math.Clamp(chance + delta, 1, XjShiCatalog.MoHeFateLeapMaximumChancePerTenThousand);
	}

	internal static int ModifySuccessionChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 500
			: string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 250
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? -200 : 0;
		return Math.Clamp(chance + delta, 2500, 9500);
	}

	internal static int GetInsightChanceBasis(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return 3200;
		if (string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)) return 2600;
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return 2100;
		return 1800;
	}

	internal static int GetJinDiAbsorptionChanceBasis(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return 6000;
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return 4500;
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return 3500;
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return 2200;
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return 1800;
		return 3000;
	}

	internal static int ModifyDharmaFormChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 450
			: string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal) ? 350
			: string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal) ? 300
			: string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 250
			: string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal) ? 200
			: string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal) ? 150
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? -100 : 0;
		return Math.Clamp(chance + delta, 500, 9500);
	}

	internal static int ModifyDharmaFormStageChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 350
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? 300
			: string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal) ? 200
			: string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal) ? 150
			: string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 100
			: string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal) ? -150 : 0;
		return Math.Clamp(chance + delta, 1000, 9000);
	}

	internal static int ModifyWorldHonoredChance(string lineageId, int chance)
	{
		int delta = string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? 500
			: string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal) ? 400
			: string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? 150
			: string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal) ? 100
			: string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal) ? -100 : 0;
		return Math.Clamp(chance + delta, 200, 5000);
	}

	internal static int ModifyResponseBodyRiskDelta(string lineageId, int delta)
	{
		int adjustment = string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal) ? -100
			: string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal) ? -80
			: string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal) ? -60
			: string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal) ? 120
			: string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal) ? 60 : 0;
		return Math.Clamp(delta + adjustment, -300, 1000);
	}

	internal static string GetBranchFunctionDisplay(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return "扩张、争位与吞并权重提高；击杀众生可摄取欲念";
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) return "异脉战斗命数收益与法相争位提高；击杀众生可吞纳忿火";
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return "释土经营与金地吞并最强；只摄取修士法痕";
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return "扩张较慢，法相层次与证道稳定；只收摄修士业力";
		if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) return "度化众生所得较为温和；以善乐摄受众生意向";
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return "度化众生所得命数与释土增长最高；以慈悲摄受众生意向";
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return "自悟、法相与世尊事务权重最高；只归空筑基以上修士";
		if (string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)) return "自证金地与古释高位闭环";
		return "七相未定，暂不附加分支权重";
	}

	internal static string GetAiTendencyDisplay(string lineageId)
	{
		if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) return "偏向扩张、争位与吞并；法相争位略强，应身风险略高";
		if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) return "异脉交锋所得命数更高；争法相较强，应身最不稳定";
		if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) return "偏向经营释土与金地，吞并能力最强";
		if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) return "度化扩张较克制；法相层次最稳，证世尊略有优势";
		if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) return "度化节奏较缓，主动扩张较少";
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) return "偏向度化众生；释土增长最活跃，法相层次较稳";
		if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) return "偏向自悟与高位求解；法相、世尊事务权重最高";
		if (string.Equals(lineageId, XjShiLineageIds.NorthWorldHonored, StringComparison.Ordinal)) return "偏向渡己、自证金地与个人解脱；古释高位事务独立闭环";
		return "法脉未定，暂不附加专属行为权重";
	}
}
