using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;

namespace XuanJianVNext.Systems.ActorSystem;

internal static class XjActorAccessor
{
	private static readonly HashSet<string> AllowedKeys = BuildAllowedKeys();

	internal static bool TryGetString(Actor actor, string key, out string value)
	{
		value = string.Empty;
		if (!CanUse(actor, key))
		{
			return false;
		}

		((BaseSystemData)actor.data).get(key, out value, string.Empty);
		return !string.IsNullOrEmpty(value);
	}

	internal static void SetString(Actor actor, string key, string value)
	{
		if (CanUse(actor, key))
		{
			((BaseSystemData)actor.data).set(key, value ?? string.Empty);
			XjCultivatorCandidateIndex.NotifyActorDataChanged(actor, key);
		}
	}

	internal static bool TryGetFloat(Actor actor, string key, out float value)
	{
		value = 0f;
		if (!CanUse(actor, key))
		{
			return false;
		}

		((BaseSystemData)actor.data).get(key, out value, 0f);
		return true;
	}

	internal static void SetFloat(Actor actor, string key, float value)
	{
		if (CanUse(actor, key))
		{
			((BaseSystemData)actor.data).set(key, value);
			XjCultivatorCandidateIndex.NotifyActorDataChanged(actor, key);
		}
	}

	internal static bool TryGetInt(Actor actor, string key, out int value)
	{
		value = 0;
		if (!CanUse(actor, key))
		{
			return false;
		}

		((BaseSystemData)actor.data).get(key, out value, 0);
		return true;
	}

	internal static void SetInt(Actor actor, string key, int value)
	{
		if (CanUse(actor, key))
		{
			((BaseSystemData)actor.data).set(key, value);
			XjCultivatorCandidateIndex.NotifyActorDataChanged(actor, key);
		}
	}

	internal static bool TryGetLong(Actor actor, string key, out long value)
	{
		value = 0L;
		if (!CanUse(actor, key))
		{
			return false;
		}

		try
		{
			((BaseSystemData)actor.data).get(key, out value, 0L);
			return true;
		}
		catch
		{
			value = 0L;
			return false;
		}
	}

	internal static void SetLong(Actor actor, string key, long value)
	{
		if (CanUse(actor, key))
		{
			((BaseSystemData)actor.data).set(key, value);
			XjCultivatorCandidateIndex.NotifyActorDataChanged(actor, key);
		}
	}

	private static bool CanUse(Actor actor, string key)
	{
		return actor?.data != null && !string.IsNullOrEmpty(key) && AllowedKeys.Contains(key);
	}

	private static HashSet<string> BuildAllowedKeys()
	{
		HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
		FieldInfo[] fields = typeof(XjActorDataKeys).GetFields(BindingFlags.Public | BindingFlags.Static);
		for (int i = 0; i < fields.Length; i++)
		{
			if (fields[i].FieldType == typeof(string)
				&& fields[i].GetValue(null) is string value
				&& !string.IsNullOrEmpty(value))
			{
				keys.Add(value);
			}
		}
		return keys;
	}

	private static bool IsVNextKey(string key)
	{
		return key == XjActorDataKeys.ZhenYuan
			|| key == XjActorDataKeys.MingShu
			|| key == XjActorDataKeys.MingShuCongenital
			|| key == XjActorDataKeys.MingShuAcquired
			|| key == XjActorDataKeys.HuiGuang
			|| key == XjActorDataKeys.XjZz
			|| key == XjActorDataKeys.XjZzOverlayMask
			|| key == XjActorDataKeys.XjZzCheckedAge5
			|| key == XjActorDataKeys.XjZzEffectApplied
			|| key == XjActorDataKeys.XjZz9LastPenaltyYear
			|| key == XjActorDataKeys.XjHighRealmChildCount
			|| key == XjActorDataKeys.XjForceGuoWeiZhongAiOnJinDan
			|| key == XjActorDataKeys.ChuShen
			|| key == XjActorDataKeys.ChuShenManualOverride
			|| key == XjActorDataKeys.ChuShenManualRemoved
			|| key == XjActorDataKeys.ChuShenSpecial
			|| key == XjActorDataKeys.ChuShenSpecialManualOverride
			|| key == XjActorDataKeys.ChuShenSpecialManualRemoved
			|| key == XjActorDataKeys.RealmId
			|| key == XjActorDataKeys.RealmManualRemoved
			|| key == XjActorDataKeys.RealmBreakthroughLastAttemptYear
			|| key == XjActorDataKeys.RealmBreakthroughFailureCount
			|| key == XjActorDataKeys.RealmBreakthroughLastResult
			|| key == XjActorDataKeys.RealmBonusAppliedMask
			|| key == XjActorDataKeys.XjZiFuFailureDeathPending
			|| key == XjActorDataKeys.XjZiFuFailureDeathYear
			|| key == XjActorDataKeys.XjZiFuFailureDeathReason
			|| key == XjActorDataKeys.XjZiFuFailureDeathHandled
			|| key == XjActorDataKeys.XjDeathAnnouncementReason
			|| key == XjActorDataKeys.XjRenDanDeathPending
			|| key == XjActorDataKeys.XjRenDanDeathYear
			|| key == XjActorDataKeys.XjRenDanDeathSourceActorId
			|| key == XjActorDataKeys.XjRenDanDeathReason
			|| key == XjActorDataKeys.XjRenDanDeathHandled
			|| key == XjActorDataKeys.XjQiYuDongTianDeathPending
			|| key == XjActorDataKeys.XjQiYuDongTianDeathYear
			|| key == XjActorDataKeys.XjQiYuDongTianDeathRecordId
			|| key == XjActorDataKeys.XjQiYuDongTianDeathReason
			|| key == XjActorDataKeys.XjQiYuDongTianDeathHandled
			|| key == XjActorDataKeys.DaoTu
			|| key == XjActorDataKeys.QingXuanQingCanQi
			|| key == XjActorDataKeys.QingXuanChuYangJi
			|| key == XjActorDataKeys.QingXuanXuanYangZiFoundation
			|| key == XjActorDataKeys.QingXuanUnlocked
			|| key == XjActorDataKeys.QingXuanKongZhengCompleted
			|| key == XjActorDataKeys.XjCombatLevel
			|| key == XjActorDataKeys.XjLongShu
			|| key == XjActorDataKeys.YinSi
			|| key == XjActorDataKeys.JinXingYaoXieBoundYinSiActorId
			|| key == XjActorDataKeys.JinXingYaoXieNameShenTong
			|| key == XjActorDataKeys.YinSiBoundJinXingYaoXieActorId
			|| key == XjActorDataKeys.XjGongFaName
			|| key == XjActorDataKeys.XjGongFaGrade
			|| key == XjActorDataKeys.XjGongFaStage
			|| key == XjActorDataKeys.XjGongFaProgress
			|| key == XjActorDataKeys.XjGongFaDaoTu
			|| key == XjActorDataKeys.XjGongFaSource
			|| key == XjActorDataKeys.XjGongFaClockTargetGrade
			|| key == XjActorDataKeys.XjGongFaClockEligibilityYear
			|| key == XjActorDataKeys.XjGongFaGrade4NextAttemptYear
			|| key == XjActorDataKeys.XjGongFaGrade5NextAttemptYear
			|| key == XjActorDataKeys.XjGongFaCollectionVersion
			|| key == XjActorDataKeys.XjGongFaCollectionJson
			|| key == XjActorDataKeys.XjXianJiCount
			|| key == XjActorDataKeys.XjXianJiIds
			|| key == XjActorDataKeys.XjXianJiLastYear
			|| key == XjActorDataKeys.XjXianJiClockTargetCount
			|| key == XjActorDataKeys.XjXianJiClockEligibilityYear
			|| key == XjActorDataKeys.XjXianJiLastLogicalAttemptYear
			|| key == XjActorDataKeys.XjXianJiFailureCount
			|| key == XjActorDataKeys.XjXianJiProjectId
			|| key == XjActorDataKeys.XjXianJiProjectTargetCount
			|| key == XjActorDataKeys.XjXianJiProjectCompleteYear
			|| key == XjActorDataKeys.XjXianJiProjectLastProposalYear
			|| key == XjActorDataKeys.XjXianJiLectureAidYear
			|| key == XjActorDataKeys.XjXianJiLectureAidBonus
			|| key == XjActorDataKeys.XjShenTongIds
			|| key == XjActorDataKeys.XjQiuJinFaName
			|| key == XjActorDataKeys.XjQiuJinFaOrigin
			|| key == XjActorDataKeys.XjQiuJinFaSourceGongFaName
			|| key == XjActorDataKeys.XjQiuJinFaSourceGongFaGrade
			|| key == XjActorDataKeys.XjQiuJinFaSourceDaoTu
			|| key == XjActorDataKeys.XjQiuJinFaReady
			|| key == XjActorDataKeys.XjQiuJinFaLastYear
			|| key == XjActorDataKeys.XjQiuJinFaEligibilityYear
			|| key == XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear
			|| key == XjActorDataKeys.XjQiuJinFaFailureCount
			|| key == XjActorDataKeys.XjQiuJinFaLastFailureReason
			|| key == XjActorDataKeys.XjGongFaGrade5PromotionFailureCount
			|| key == XjActorDataKeys.XjGongFaGrade5PromotionLastYear
			|| key == XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason
			|| key == XjActorDataKeys.XjGongFaHighPromotionFailureCount
			|| key == XjActorDataKeys.XjGongFaHighPromotionLastYear
			|| key == XjActorDataKeys.XjGongFaHighPromotionLastFailureReason
			|| key == XjActorDataKeys.XjJinDanJinXing
			|| key == XjActorDataKeys.XjJinDanResidualJinXing
			|| key == XjActorDataKeys.XjJinDanResidualJinXingSource
			|| key == XjActorDataKeys.XjJinDanResidualWarehouseMigrated
			|| key == XjActorDataKeys.XjJinDanGuoWei
			|| key == XjActorDataKeys.XjJinDanFailedState
			|| key == XjActorDataKeys.XjJinDanLastAttemptYear
			|| key == XjActorDataKeys.XjJinDanSuccessYear
			|| key == XjActorDataKeys.XjJinDanYiXiang
			|| key == XjActorDataKeys.XjFamilySurname
			|| key == XjActorDataKeys.XjNameBase
			|| key == XjActorDataKeys.XjNameTitle
			|| key == XjActorDataKeys.XjNameRealmDisplay
			|| key == XjActorDataKeys.XjCaiQiFaName
			|| key == XjActorDataKeys.XjCaiQiFaDaoTu
			|| key == XjActorDataKeys.XjCaiQiFaSourcePlace
			|| key == XjActorDataKeys.XjCaiQiFaSourceYear
			|| key == XjActorDataKeys.XjFaBaoId
			|| key == XjActorDataKeys.XjFaBaoName
			|| key == XjActorDataKeys.XjFaBaoDaoTu
			|| key == XjActorDataKeys.XjFaBaoClass
			|| key == XjActorDataKeys.XjFaBaoKind
			|| key == XjActorDataKeys.XjFaBaoRole
			|| key == XjActorDataKeys.XjFaBaoAffixes
			|| key == XjActorDataKeys.XjFaBaoDescription
			|| key == XjActorDataKeys.XjFaBaoSource
			|| key == XjActorDataKeys.XjFaBaoYear
			|| key == XjActorDataKeys.XjZongMenId
			|| key == XjActorDataKeys.XjZongMenName
			|| key == XjActorDataKeys.XjZongMenRank
			|| key == XjActorDataKeys.XjZongMenJoinYear
			|| key == XjActorDataKeys.CaiQiCompleted
			|| key == XjActorDataKeys.CaiQiPlaceTypeId
			|| key == XjActorDataKeys.CaiQiBranchId
			|| key == XjActorDataKeys.CaiQiSiteName
			|| key == XjActorDataKeys.CaiQiResultType
			|| key == XjActorDataKeys.CaiQiStatus
			|| key == XjActorDataKeys.CaiQiFailureReason
			|| key == XjActorDataKeys.CaiQiResourceId
			|| key == XjActorDataKeys.CaiQiResourceCount
			|| key == XjActorDataKeys.CaiQiGatheredCount
			|| key == XjActorDataKeys.CaiQiConsumedForBreakthrough
			|| key == XjActorDataKeys.LianQiByZaQi
			|| key == XjActorDataKeys.LastCaiQiYear
			|| key == XjActorDataKeys.NextCaiQiYear
			|| key == XjActorDataKeys.XjBloodlineQuality
			|| key == XjActorDataKeys.XjBloodlineConcentration
			|| key == XjActorDataKeys.XjBloodlineGeneration
			|| key == XjActorDataKeys.XjBloodlineOriginDaoTu
			|| key == XjActorDataKeys.XjBloodlineIsAncestor
			|| key == XjActorDataKeys.XjBloodlineExtraTalentInheritance
			|| key == XjActorDataKeys.XjBloodlineApplied
			|| key == XjActorDataKeys.XjBloodlineAppliedYear
			|| key == XjActorDataKeys.XjBloodlineSource
			|| key == XjActorDataKeys.XjBloodlineSeedInheritanceApplied
			|| key == XjActorDataKeys.XjReincarnationApplied
			|| key == XjActorDataKeys.XjReincarnationSourceActorId
			|| key == XjActorDataKeys.XjReincarnationSourceName
			|| key == XjActorDataKeys.XjReincarnationSavedYear
			|| key == XjActorDataKeys.XjReincarnationMode
			|| key == XjActorDataKeys.XjReincarnationDaoTu
			|| key == XjActorDataKeys.XjReincarnationGuoWeiZhongAi
			|| key == XjActorDataKeys.XjLastCultivationYear
			|| key == XjActorDataKeys.XjZongMenRole
			|| key == XjActorDataKeys.XjZongMenPeakId
			|| key == XjActorDataKeys.XjZongMenPeakName
			|| key == XjActorDataKeys.XjZongMenDongTianState
			|| key == XjActorDataKeys.XjZongMenDongTianRetreatEndYear
			|| key == XjActorDataKeys.XjZongMenDongTianTravelEndYear
			|| key == XjActorDataKeys.XjZongMenDongTianNextEligibleYear
			|| key == XjActorDataKeys.XjZongMenDongTianLastProcessedYear;
	}
}
