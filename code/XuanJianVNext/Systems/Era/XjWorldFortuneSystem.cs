using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.Era;

/// <summary>
/// “世运”是当世结构的低频归纳，而不是新的境界倍率系统。0.9.9.19 起
/// 玩家配置的奇遇洞天周期是严格玄鉴历节拍，世运不再改写该配置。
/// </summary>
internal static class XjWorldFortuneSystem
{
	internal const string PingShi = "平世";
	internal const string GaoXiuDingSheng = "高修鼎盛";
	internal const string XuanMenShuaiWei = "玄门衰微";
	internal const string XianGuoShengShi = "仙国盛世";
	internal const string ZhuZongBingJie = "诸宗兵劫";
	internal const string DaoTongDuanDai = "道统断代";
	internal const string DongTianPinKai = "洞天频开";
	internal const string YinSiXianShi = "阴司显世";

	private const int EvaluationIntervalYears = 5;
	private const int MinimumStateYears = 15;
	private static int _lastEvaluationYear;
	private static int _sinceYear;
	private static string _current = PingShi;
	private static string _summary = "世运平稳，诸法各循其常。";

	internal static string CurrentId => _current;
	internal static string CurrentDisplay => _current;
	internal static string Summary => _summary;
	internal static int SinceYear => _sinceYear;

	internal static void Clear()
	{
		_lastEvaluationYear = 0;
		_sinceYear = 0;
		_current = PingShi;
		_summary = "世运平稳，诸法各循其常。";
	}

	internal static void TickYear(int currentYear)
	{
		if (!XjWorldSchemaGuard.GameplayEnabled || currentYear <= 0) return;
		if (_lastEvaluationYear > 0 && currentYear - _lastEvaluationYear < EvaluationIntervalYears) return;
		_lastEvaluationYear = currentYear;

		int sectCount = 0;
		var sects = XjSectRepository.ReadAllSects();
		for (int i = 0; i < sects.Count; i++)
		{
			XjSectArchiveRecord sect = sects[i];
			if (sect != null && sect.SectId > 0L && !string.Equals(sect.Status, XjSectStatus.Extinct, StringComparison.Ordinal)) sectCount++;
		}
		int activeWars = XjSectWarSystem.ReadActiveWars().Count;
		int zhenRenOrHigher = XjCultivatorCache.ZhenRenOrHigherCount;
		int zhenJunOrHigher = XjCultivatorCache.GetZhenJunOrHigherIds().Count;
		int cultivators = Math.Max(1, XjCultivatorCache.Count);
		int activeXianGuo = XjXianGuoSystem.ActiveDynastyCount;
		int activeYinSi = XjYinSiExposurePursuitSystem.ActiveMissionCount;
		int openQiYu = XjDongTianRegistry.CountOpenQiYuDongTian(currentYear);
		float transmissionGap = XjSectTransmissionCoverageSystem.LastGlobalGapRatio;

		string candidate = PingShi;
		string summary = "世运平稳，诸法各循其常。";
		// 灾变性世运优先于盛世描述，避免同年既写“盛世”又在大规模兵劫/阴司追索中。
		if (activeYinSi >= 2)
		{
			candidate = YinSiXianShi;
			summary = "阴司追索与现世高境纠缠加深，阴阳边界愈发显眼；相关高境事件更易牵动天下。";
		}
		else if (activeWars >= 2 || (sectCount >= 4 && activeWars >= 1 && zhenJunOrHigher >= 2))
		{
			candidate = ZhuZongBingJie;
			summary = "诸宗争衡已成兵劫之势，山门、洞天与传承更容易卷入宗门冲突。";
		}
		else if (activeXianGuo > 0 && zhenRenOrHigher >= 6)
		{
			candidate = XianGuoShengShi;
			summary = "帝统立而高修相继，一国之势足以影响周边山门与修行格局。";
		}
		else if (sectCount >= 2 && transmissionGap >= 0.50f)
		{
			candidate = DaoTongDuanDai;
			summary = "多宗高阶传承出现真实断层，山门会更主动求法、访秘境、交换或夺取缺失法门。";
		}
		else if (openQiYu >= 2 || (openQiYu >= 1 && zhenRenOrHigher >= 8))
		{
			candidate = DongTianPinKai;
			summary = "当世同时开放的洞天较多，山门更容易组织门人入内探寻；这只是世界态势描述，不改变奇遇洞天固定排期。";
		}
		else if (zhenJunOrHigher >= 3 || zhenRenOrHigher >= Math.Max(10, cultivators / 18))
		{
			candidate = GaoXiuDingSheng;
			summary = "真人真君相继立世，高修活动密集，论道、争位与洞天际遇更为频繁。";
		}
		else if (sectCount >= 2 && zhenRenOrHigher <= Math.Max(2, sectCount / 2))
		{
			candidate = XuanMenShuaiWei;
			summary = "山门尚存而高修稀少，旧法难续、后继乏力，一纪将歇之象渐显。";
		}

		// 世运是由当前世界事实推导的状态，不额外保存第二份权威存档。首次结算（包括读档后
		// 首轮）只建立基线，不补写“平世→某世运”的伪历史；之后持续至少十五年才允许转运入史。
		if (_sinceYear <= 0)
		{
			_current = candidate;
			_summary = summary;
			_sinceYear = currentYear;
			return;
		}
		if (string.Equals(candidate, _current, StringComparison.Ordinal))
		{
			_summary = summary;
			return;
		}
		if (currentYear - _sinceYear < MinimumStateYears) return;

		string previous = _current;
		_current = candidate;
		_summary = summary;
		_sinceYear = currentYear;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.World,
			"世运转为" + candidate,
			"当世由【" + previous + "】转入【" + candidate + "】。" + summary,
			candidate == PingShi ? 2 : 3,
			year: currentYear,
			eventType: "WorldFortuneChanged:" + candidate,
			visibilityFlags: (int)(XjHistoryVisibility.World | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
	}

}
