using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气法门使用真实功法实体，但始终只有一部本命功法。黄冠、真人、真君羽士
/// 只提升这一部功法的品级，不生成采气法、仙基、神通、求金法或四部副功法。
/// </summary>
internal static class XjFuQiMethodRules
{
	private const int FullVerificationIntervalYears = 25;
	private static readonly Dictionary<long, string> VerifiedRuntimeSignatures = new Dictionary<long, string>();

	internal static bool EnsureRealGongFaEntity(
		Actor actor,
		in XjFuQiCoreDefinition definition,
		int currentYear)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string realm = XjRealmHelper.NormalizeId(realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			if (!string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
			{
				return false;
			}
			daoTu = "无名剑道";
		}

		int desiredGrade = string.Equals(realm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.ShenDan, StringComparison.Ordinal)
			? 6
			: string.Equals(realm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			? 5
			: string.Equals(realm, XjRealmIds.HuangGuan, StringComparison.Ordinal) ? 4 : 1;
		string name = ResolveMethodName(in definition);
		if (string.IsNullOrWhiteSpace(name)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		string observedSignature = BuildRuntimeSignature(actor, realm, daoTu, desiredGrade, name);
		bool periodicFullVerification = currentYear > 0
			&& PositiveModulo(actorId, FullVerificationIntervalYears)
				== PositiveModulo(currentYear, FullVerificationIntervalYears);
		if (!periodicFullVerification
			&& actorId > 0L
			&& VerifiedRuntimeSignatures.TryGetValue(actorId, out string verified)
			&& string.Equals(verified, observedSignature, StringComparison.Ordinal))
		{
			return true;
		}

		ReconcileExclusiveZiJinData(actor);
		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		var records = XjActorGongFaCollection.ReadRecords(actor);
		bool valid = current.Found
			&& current.Grade == desiredGrade
			&& string.Equals((current.Name ?? string.Empty).Trim(), name, StringComparison.Ordinal)
			&& string.Equals((current.DaoTu ?? string.Empty).Trim(), daoTu, StringComparison.Ordinal)
			&& records.Count == 1
			&& records[0].IsPrimary
			&& string.IsNullOrWhiteSpace(records[0].MappedXianJi);
		if (valid)
		{
			RememberVerified(actor, actorId, realm, daoTu, desiredGrade, name);
			return true;
		}

		XjGongFaAccessor.Clear(actor);
		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true, name, desiredGrade, 0, 0f, daoTu, false, "FuQiMethodEntity"));
		XjGongFaAccessor.WriteSource(actor, "服气本命法");
		XjGongFaState repaired = XjGongFaAccessor.BuildState(actor);
		var repairedRecords = XjActorGongFaCollection.ReadRecords(actor);
		bool repairedValid = repaired.Found
			&& repaired.Grade == desiredGrade
			&& string.Equals((repaired.Name ?? string.Empty).Trim(), name, StringComparison.Ordinal)
			&& string.Equals((repaired.DaoTu ?? string.Empty).Trim(), daoTu, StringComparison.Ordinal)
			&& repairedRecords.Count == 1
			&& repairedRecords[0].IsPrimary
			&& string.IsNullOrWhiteSpace(repairedRecords[0].MappedXianJi);
		if (repairedValid) RememberVerified(actor, actorId, realm, daoTu, desiredGrade, name);
		else if (actorId > 0L) VerifiedRuntimeSignatures.Remove(actorId);
		return repairedValid;
	}

	internal static void ForgetRuntimeCache(long actorId)
	{
		if (actorId > 0L) VerifiedRuntimeSignatures.Remove(actorId);
	}

	internal static void ClearRuntimeCache()
	{
		VerifiedRuntimeSignatures.Clear();
	}

	private static void RememberVerified(Actor actor, long actorId, string realm, string daoTu, int desiredGrade, string name)
	{
		if (actorId <= 0L) return;
		VerifiedRuntimeSignatures[actorId] = BuildRuntimeSignature(actor, realm, daoTu, desiredGrade, name);
	}

	private static string BuildRuntimeSignature(Actor actor, string realm, string daoTu, int desiredGrade, string name)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaCollectionVersion, out int collectionVersion);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaCollectionJson, out string collectionJson);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaName, out string legacyName);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int legacyGrade);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string legacyDaoTu);
		int jsonHash = string.IsNullOrEmpty(collectionJson) ? 0 : StringComparer.Ordinal.GetHashCode(collectionJson);
		return (realm ?? string.Empty) + "|" + (daoTu ?? string.Empty) + "|" + desiredGrade + "|"
			+ (name ?? string.Empty) + "|" + collectionVersion + "|" + jsonHash + "|"
			+ (legacyName ?? string.Empty) + "|" + legacyGrade + "|" + (legacyDaoTu ?? string.Empty);
	}

	private static int PositiveModulo(long value, int modulus)
	{
		if (modulus <= 0) return 0;
		long remainder = value % modulus;
		return (int)(remainder < 0L ? remainder + modulus : remainder);
	}

	internal static void ReconcileExclusiveZiJinData(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return;
		}

		bool hasXianJiResidue =
			HasNonZeroInt(actor, XjActorDataKeys.XjXianJiCount)
			|| HasText(actor, XjActorDataKeys.XjXianJiIds)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiLastYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiLastExecutionYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiClockTargetCount)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiClockEligibilityYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiLastLogicalAttemptYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiFailureCount)
			|| HasText(actor, XjActorDataKeys.XjXianJiProjectId)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiProjectTargetCount)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiProjectCompleteYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiProjectLastProposalYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjXianJiLectureAidYear)
			|| HasNonZeroFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus)
			|| HasText(actor, XjActorDataKeys.XjShenTongIds);
		if (hasXianJiResidue)
		{
			XjXianJiAccessor.RestoreSnapshot(actor, string.Empty, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiFailureCount, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjXianJiProjectId, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiProjectTargetCount, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiProjectCompleteYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiProjectLastProposalYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjXianJiLectureAidYear, 0);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjXianJiLectureAidBonus, 0f);
			XjXianJiOpportunitySchedule.Clear(actor);
		}

		bool hasQiuJinFaResidue =
			HasText(actor, XjActorDataKeys.XjQiuJinFaName)
			|| HasText(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaName)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade)
			|| HasText(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu)
			|| HasText(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority)
			|| HasText(actor, XjActorDataKeys.XjQiuJinFaOrigin)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaReady)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaLastYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaLastExecutionYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaEligibilityYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount)
			|| HasText(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason);
		if (hasQiuJinFaResidue)
		{
			XjQiuJinFaAccessor.Clear(actor);
		}

		if (HasText(actor, XjActorDataKeys.XjCaiQiFaName)
			|| HasText(actor, XjActorDataKeys.XjCaiQiFaDaoTu)
			|| HasText(actor, XjActorDataKeys.XjCaiQiFaSourcePlace)
			|| HasNonZeroInt(actor, XjActorDataKeys.XjCaiQiFaSourceYear))
		{
			XjCaiQiFaAccessor.Clear(actor);
		}
	}

	private static bool HasText(Actor actor, string key)
	{
		return XjActorAccessor.TryGetString(actor, key, out string value)
			&& !string.IsNullOrWhiteSpace(value);
	}

	private static bool HasNonZeroInt(Actor actor, string key)
	{
		return XjActorAccessor.TryGetInt(actor, key, out int value) && value != 0;
	}

	private static bool HasNonZeroFloat(Actor actor, string key)
	{
		return XjActorAccessor.TryGetFloat(actor, key, out float value)
			&& Math.Abs(value) > 0.0001f;
	}

	internal static string BuildDisplaySummary(
		Actor actor,
		in XjFuQiCoreDefinition definition,
		int currentYear,
		string detail)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		StringBuilder builder = new StringBuilder(256);
		builder.Append("法门层次：").AppendLine(ResolveMethodStage(actor, currentYear, in definition));
		builder.Append("本命核心：").AppendLine(definition.DisplayName);

		float eraMultiplier = XjEraBonusService.GetCultivationMultiplier(actor)
			* XjHighRealmDaoStateService.GetProofFoundationCultivationMultiplier(actor);
		if (eraMultiplier > 1f)
		{
			builder.Append("纪元契合：修持速度")
				.Append(eraMultiplier.ToString("0.00", CultureInfo.InvariantCulture))
				.AppendLine("倍");
		}

		float impersonationMultiplier = XjMingShuChildSystem.ResolveCultivationMultiplier(actor);
		if (impersonationMultiplier > 1f)
		{
			builder.Append("因果拟身：修持速度")
				.Append(impersonationMultiplier.ToString("0.00", CultureInfo.InvariantCulture))
				.AppendLine("倍");
		}
		float mingYangSchemeMultiplier = XjMingShuSchemeSystem.ResolveCultivationMultiplier(actor);
		if (mingYangSchemeMultiplier > 1f)
		{
			builder.Append("明阳局势：修持速度")
				.Append(mingYangSchemeMultiplier.ToString("0.00", CultureInfo.InvariantCulture))
				.AppendLine("倍");
		}

		if (!string.IsNullOrWhiteSpace(detail))
		{
			AppendFilteredDetail(builder, detail);
		}
		return builder.ToString().TrimEnd();
	}

	private static void AppendFilteredDetail(StringBuilder builder, string detail)
	{
		string[] lines = detail.Replace("\r", string.Empty)
			.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < lines.Length; i++)
		{
			string line = lines[i].Trim();
			if (line.Length == 0
				|| line.StartsWith("道途：", StringComparison.Ordinal)
				|| line.StartsWith("本命核心：", StringComparison.Ordinal)
				|| line.StartsWith("功法名称：", StringComparison.Ordinal)
				|| line.StartsWith("功法作用：", StringComparison.Ordinal))
			{
				continue;
			}
			builder.AppendLine(line);
		}
	}

	internal static void ApplyAnnualEraAcceleration(
		Actor actor,
		int currentYear,
		string daoTuRootId)
	{
		if (actor?.data == null || currentYear <= 0)
		{
			return;
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiEraLastAnnualYear, out int lastYear)
			&& lastYear == currentYear)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiEraLastAnnualYear, currentYear);

		float multiplier = XjEraBonusService.GetCultivationMultiplier(actor)
			* XjHighRealmDaoStateService.GetProofFoundationCultivationMultiplier(actor)
			* XjMingShuChildSystem.ResolveCultivationMultiplier(actor)
			* XjMingShuSchemeSystem.ResolveCultivationMultiplier(actor);
		int accelerationBasisPoints = (int)Math.Round(Math.Max(0f, multiplier - 1f) * 10000f);
		if (accelerationBasisPoints <= 0)
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (XjDeterministicHash.PositiveIndex(
			actorId + currentYear,
			"fuqi_era_acceleration|" + (daoTuRootId ?? string.Empty),
			10000) >= accelerationBasisPoints)
		{
			return;
		}

		if (!TryResolveActiveProjectKey(actor, currentYear, out string completeYearKey)
			|| !XjActorAccessor.TryGetInt(actor, completeYearKey, out int completeYear)
			|| completeYear <= currentYear)
		{
			return;
		}
		XjActorAccessor.SetInt(actor, completeYearKey, Math.Max(currentYear, completeYear - 1));
	}

	private static bool TryResolveActiveProjectKey(Actor actor, int currentYear, out string key)
	{
		key = string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string realm = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(realm))
		{
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiIntentStudyCompleteYear, out int intentYear)
				&& intentYear > currentYear)
			{
				key = XjActorDataKeys.FuQiIntentStudyCompleteYear;
				return true;
			}
			key = XjActorDataKeys.FuQiCoreProjectCompleteYear;
			return true;
		}
		if (string.Equals(realm, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			key = XjActorDataKeys.FuQiBodyProjectCompleteYear;
			return true;
		}
		if (!string.Equals(realm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return false;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectionYear);
		if (perfectionYear <= 0)
		{
			key = XjActorDataKeys.FuQiPerfectionProjectCompleteYear;
			return true;
		}
		if (XjFuQiBalancePolicy.CanAcceleratePostFailureNurture(actor, currentYear)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int nurtureYear)
			&& nurtureYear > currentYear)
		{
			key = XjActorDataKeys.FuQiJinXingNurtureCompleteYear;
			return true;
		}
		return false;
	}

	private static string ResolveMethodStage(
		Actor actor,
		int currentYear,
		in XjFuQiCoreDefinition definition)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string realm = XjRealmHelper.NormalizeId(realmId);
		if (string.IsNullOrWhiteSpace(realm))
		{
			if (string.Equals(definition.DaoTuRootId, XjDaoTuRootIds.LongGeng, StringComparison.Ordinal))
			{
				return "观剑养气";
			}
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProgress, out int progress);
			return "感气炼命（" + FormatBasisPoints(progress) + "）";
		}
		if (string.Equals(realm, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return "神妙求身" + BuildProjectProgress(
				actor,
				currentYear,
				XjActorDataKeys.FuQiBodyProjectStartYear,
				XjActorDataKeys.FuQiBodyProjectCompleteYear);
		}
		if (string.Equals(realm, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiShenMiaoPerfectionYear, out int perfectionYear);
			if (perfectionYear > 0)
			{
				return "性命圆满";
			}
			return "性命合炼" + BuildProjectProgress(
				actor,
				currentYear,
				XjActorDataKeys.FuQiPerfectionProjectStartYear,
				XjActorDataKeys.FuQiPerfectionProjectCompleteYear);
		}
		if (string.Equals(realm, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
			|| string.Equals(realm, XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return "金性证成";
		}
		return "服气养性";
	}

	private static string BuildProjectProgress(
		Actor actor,
		int currentYear,
		string startYearKey,
		string completeYearKey)
	{
		if (!XjActorAccessor.TryGetInt(actor, startYearKey, out int startYear)
			|| !XjActorAccessor.TryGetInt(actor, completeYearKey, out int completeYear)
			|| startYear <= 0
			|| completeYear <= startYear)
		{
			return string.Empty;
		}
		float ratio = Math.Clamp(
			(currentYear - startYear) / (float)(completeYear - startYear),
			0f,
			1f);
		return ratio < 0.25f ? "（初行）"
			: ratio < 0.55f ? "（渐进）"
			: ratio < 0.85f ? "（渐深）"
			: "（将成）";
	}

	private static string FormatBasisPoints(int value)
	{
		int normalized = Math.Clamp(value, 0, 10000);
		if (normalized <= 0) return "未入";
		if (normalized < 2500) return "初悟";
		if (normalized < 5500) return "渐入";
		if (normalized < 8500) return "大成";
		return "圆满";
	}

	internal static string ResolveMethodName(in XjFuQiCoreDefinition definition)
	{
		return string.IsNullOrWhiteSpace(definition.MethodName)
			? "《服气养性篇》"
			: definition.MethodName;
	}
}
