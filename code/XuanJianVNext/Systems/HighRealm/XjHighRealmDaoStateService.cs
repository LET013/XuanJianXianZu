using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjHighRealmDoctrineSnapshot
{
	internal readonly bool Found;
	internal readonly string PositionType;
	internal readonly string SourceDaoTu;
	internal readonly string ManifestDaoTu;
	internal readonly string RunFormula;
	internal readonly string Doctrine;
	internal readonly string DaoTitle;
	internal readonly string JinXing;
	internal readonly string AuthorityScope;
	internal readonly string ProofCityName;
	internal readonly string LegacyDoctrine;
	internal readonly int DaoProgress;
	internal readonly int PositionImage;
	internal readonly string RespectPractice;

	internal XjHighRealmDoctrineSnapshot(
		bool found, string positionType, string sourceDaoTu, string manifestDaoTu,
		string runFormula, string doctrine, string daoTitle, string jinXing,
		string authorityScope, string proofCityName, string legacyDoctrine,
		int daoProgress, int positionImage, string respectPractice)
	{
		Found = found;
		PositionType = positionType ?? string.Empty;
		SourceDaoTu = sourceDaoTu ?? string.Empty;
		ManifestDaoTu = manifestDaoTu ?? string.Empty;
		RunFormula = runFormula ?? string.Empty;
		Doctrine = doctrine ?? string.Empty;
		DaoTitle = daoTitle ?? string.Empty;
		JinXing = jinXing ?? string.Empty;
		AuthorityScope = authorityScope ?? string.Empty;
		ProofCityName = proofCityName ?? string.Empty;
		LegacyDoctrine = legacyDoctrine ?? string.Empty;
		DaoProgress = Math.Max(0, daoProgress);
		PositionImage = Math.Max(0, positionImage);
		RespectPractice = respectPractice ?? string.Empty;
	}
}

/// <summary>
/// 将旧版单一“果位意象”拆成道行/修持、求位意向与天地意象三层，
/// 并为闰位保存根道、显道、闰法、宣法、权辖与证道城镇道基。
/// </summary>
internal static class XjHighRealmDaoStateService
{
	internal const int CurrentSchemaVersion = 2;
	private const string CityFoundationSchemaKey = "xuanjian.vnext.city.dao_foundation.schema";
	private const string CityFoundationSourceKey = "xuanjian.vnext.city.dao_foundation.source";
	private const string CityFoundationManifestKey = "xuanjian.vnext.city.dao_foundation.manifest";
	private const string CityFoundationDoctrineKey = "xuanjian.vnext.city.dao_foundation.doctrine";
	private const string CityFoundationLegacyKey = "xuanjian.vnext.city.dao_foundation.legacy";
	private const string CityFoundationFounderKey = "xuanjian.vnext.city.dao_foundation.founder";
	private const string CityFoundationYearKey = "xuanjian.vnext.city.dao_foundation.year";

	private static readonly string[] ArchiveStringKeys =
	{
		XjActorDataKeys.XjGuoWeiIntentionType, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu,
		XjActorDataKeys.XjJinDanSourceDaoTu, XjActorDataKeys.XjJinDanManifestDaoTu,
		XjActorDataKeys.XjJinDanRunFormula, XjActorDataKeys.XjJinDanRunDoctrine,
		XjActorDataKeys.XjJinDanDaoTitle, XjActorDataKeys.XjJinDanAuthorityScope,
		XjActorDataKeys.XjJinDanProofCityName, XjActorDataKeys.XjJinDanLegacyDoctrine,
		XjActorDataKeys.XjJinXingRootDaoTu, XjActorDataKeys.XjJinXingManifestDaoTu,
		XjActorDataKeys.XjJinXingPositionType, XjActorDataKeys.XjJinXingTitle,
		XjActorDataKeys.XjJinXingTrueName, XjActorDataKeys.XjJinXingHistory,
		XjActorDataKeys.XjRespectPracticeType, XjActorDataKeys.XjRespectPracticeStage,
		XjActorDataKeys.XjRespectPracticeTargetDaoTu, XjActorDataKeys.XjLineageRevivalStage,
		XjActorDataKeys.XjLineageRevivalDirection, XjActorDataKeys.XjShenDanMethodName,
		XjActorDataKeys.XjShenDanMethodTargetDaoTu, XjActorDataKeys.XjShenDanMethodTargetPosition,
		XjActorDataKeys.XjShenDanMethodSource,
		XjActorDataKeys.XjShenTongAuthoritySnapshot, XjActorDataKeys.XjShenTongMutationHistory,
		XjActorDataKeys.XjShenTongMutationBindings, XjActorDataKeys.XjGuoWeiImageState,
		XjActorDataKeys.XjGuoWeiImageHistory
	};

	private static readonly string[] ArchiveIntKeys =
	{
		XjActorDataKeys.XjHighRealmDaoSchema, XjActorDataKeys.XjJinDanDaoXing,
		XjActorDataKeys.XjZhenJunXiuChi, XjActorDataKeys.XjGuoWeiImage,
		XjActorDataKeys.XjJinXingPurity, XjActorDataKeys.XjJinXingScars,
		XjActorDataKeys.XjRespectPracticeProgress, XjActorDataKeys.XjLineageRevivalProgress,
		XjActorDataKeys.XjLineageRevivalLastYear, XjActorDataKeys.XjLineageRevivalRetreatLastYear,
		XjActorDataKeys.XjHighRealmLastAnnualYear,
		XjActorDataKeys.XjHighRealmRetreatLastYear, XjActorDataKeys.XjShenDanMethodReady,
		XjActorDataKeys.XjShenDanMethodYear, XjActorDataKeys.XjShenDanMethodEvaluatedYear,
		XjActorDataKeys.XjShenTongDifficultyPermille, XjActorDataKeys.XjShenTongMutationLastYear,
		XjActorDataKeys.XjShenTongMutationRecoverySchema, XjActorDataKeys.XjShenTongActualMutationLastYear
	};

	private static readonly string[] ArchiveLongKeys = { XjActorDataKeys.XjJinDanProofCityId };

	internal static string BuildPromotionJinXing(
		Actor actor,
		string sourceDaoTu,
		string manifestDaoTu,
		string positionType,
		string baseJinXing)
	{
		string type = XjGuoWeiCalculator.NormalizePositionType(positionType);
		string source = Normalize(sourceDaoTu);
		string manifest = Normalize(manifestDaoTu);
		string baseName = Normalize(baseJinXing);
		if (!string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return baseName;
		string title = ResolveDaoTitle(actor);
		string trueName = ResolveJinXingTrueName(baseName, manifest, actor);
		return ShortDao(source) + "闰" + title + trueName + "性";
	}

	internal static string ResolvePromotionDaoTitle(Actor actor) => ResolveDaoTitle(actor);

	/// <summary>
	/// 读取证道时固化的根道/显道。神通在成位后可能因权柄得失发生变化，
	/// 因此已经形成的合法双字段身份不能被后续神通形态反向改写。
	/// </summary>
	internal static bool TryReadPersistedIntercalaryIdentity(
		Actor actor,
		out string sourceDaoTu,
		out string manifestDaoTu)
	{
		sourceDaoTu = string.Empty;
		manifestDaoTu = string.Empty;
		if (actor?.data == null) return false;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string storedSource);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string storedManifest);
		sourceDaoTu = Normalize(storedSource);
		manifestDaoTu = Normalize(storedManifest);
		if (sourceDaoTu.Length > 0
			&& manifestDaoTu.Length > 0
			&& !string.Equals(sourceDaoTu, manifestDaoTu, StringComparison.Ordinal))
		{
			return true;
		}

		// 金性字段是同一证道事务写入的冗余快照，可修复主字段缺失或旧版覆盖。
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingRootDaoTu, out string jinXingSource);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingManifestDaoTu, out string jinXingManifest);
		sourceDaoTu = Normalize(jinXingSource);
		manifestDaoTu = Normalize(jinXingManifest);
		return sourceDaoTu.Length > 0
			&& manifestDaoTu.Length > 0
			&& !string.Equals(sourceDaoTu, manifestDaoTu, StringComparison.Ordinal);
	}

	internal static bool HasRecordedShenTongMutation(Actor actor)
	{
		if (actor?.data == null) return false;
		return (XjActorAccessor.TryGetString(
				actor, XjActorDataKeys.XjShenTongMutationBindings, out string bindings)
				&& !string.IsNullOrWhiteSpace(bindings))
			|| (XjActorAccessor.TryGetString(
				actor, XjActorDataKeys.XjShenTongMutationHistory, out string history)
				&& !string.IsNullOrWhiteSpace(history));
	}

	/// <summary>
	/// 读取高境身份时统一分离“根本道途”和“果位归属道途”。证道时固化字段
	/// 与当前五神通共同校验旧档；发生过权柄神通变化后只信固化身份，绝不把
	/// 后天变化或角色当前 DaoTu 误当成果位归属。
	/// </summary>
	internal static void ResolvePositionIdentity(
		Actor actor,
		string guoWei,
		out string sourceDaoTu,
		out string manifestDaoTu)
	{
		sourceDaoTu = string.Empty;
		manifestDaoTu = string.Empty;
		if (actor?.data == null) return;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string currentDaoTu);
		string type = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		string namedDaoTu = XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(guoWei);
		bool hasPersistedIdentity = TryReadPersistedIntercalaryIdentity(
			actor, out sourceDaoTu, out manifestDaoTu);

		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			// 闰位一旦证成，根道/显道即为位序身份的一部分，之后不得因神通变化、
			// 道网修订或运行期校验重新“闰”去另一果位。只有旧档完全缺少固化身份时，
			// 才允许从当时五神通反推一次用于补档。
			bool mayResolveFromCurrentShenTong = !hasPersistedIdentity;
			if (mayResolveFromCurrentShenTong
				&& XjCultivationPathRules.IsZiFuJinDan(actor)
				&& XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
					actor, XjXianJiAccessor.BuildState(actor), out string resolvedSource, out string resolvedManifest))
			{
				sourceDaoTu = resolvedSource;
				manifestDaoTu = resolvedManifest;
				hasPersistedIdentity = true;
			}
			if (!hasPersistedIdentity)
			{
				manifestDaoTu = Normalize(namedDaoTu);
				if (XjFruitPositionWorldState.TryGetPosition(guoWei, out XjDerivedPositionArchiveRecord position)
					&& position != null)
				{
					sourceDaoTu = Normalize(position.ExternalDaoTu);
				}
			}

			if (manifestDaoTu.Length == 0) manifestDaoTu = Normalize(currentDaoTu);
			if (sourceDaoTu.Length == 0) sourceDaoTu = manifestDaoTu;
			return;
		}

		if (manifestDaoTu.Length == 0) manifestDaoTu = Normalize(currentDaoTu);
		sourceDaoTu = manifestDaoTu;
	}

	/// <summary>
	/// 修复已经写入旧档的错误闰位归属。保留道行、意象、金性纯度、裂痕与历史，
	/// 只重建根道/显道、果位权辖和道统投影，不把迁移当作一次新的证道。
	/// </summary>
	internal static void RepairIntercalaryIdentity(
		Actor actor,
		string sourceDaoTu,
		string manifestDaoTu,
		string guoWei,
		string jinXing,
		int successYear,
		int migrationYear,
		string previousManifestDaoTu,
		string previousGuoWei)
	{
		if (actor?.data == null) return;
		string source = Normalize(sourceDaoTu);
		string manifest = Normalize(manifestDaoTu);
		if (source.Length == 0 || manifest.Length == 0
			|| string.Equals(source, manifest, StringComparison.Ordinal)) return;

		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool isFuQi = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		string progressKey = isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing;
		XjActorAccessor.TryGetInt(actor, progressKey, out int progress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int compatibilityProgress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingPurity, out int purity);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingScars, out int scars);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmLastAnnualYear, out int lastAnnualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmRetreatLastYear, out int lastRetreatYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string history);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanDaoTitle, out string title);
		long actorId = ((BaseSystemData)actor.data).id;
		int safeSuccessYear = Math.Max(1, successYear);
		int safeMigrationYear = Math.Max(safeSuccessYear, migrationYear);

		string previousManifest = Normalize(previousManifestDaoTu);
		if (actorId > 0L
			&& previousManifest.Length > 0
			&& (!string.Equals(previousManifest, manifest, StringComparison.Ordinal)
				|| !string.Equals(
					XjGuoWeiCalculator.NormalizeGuoWeiName(previousGuoWei),
					XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei),
					StringComparison.Ordinal)))
		{
			XjDaoLineageStateRegistry.OnHolderReleased(
				actorId, previousManifest, previousGuoWei, string.Empty, safeMigrationYear, penalizeVitality: false);
		}

		InitializeOnPromotion(
			actor, source, manifest, XjGuoWeiCalculator.RunWei, guoWei, jinXing, safeSuccessYear, isFuQi,
			daoTitleOverride: title, recordFoundation: false, affectLineageVitality: false);

		int preservedProgress = Math.Max(progress, compatibilityProgress);
		if (preservedProgress > 0)
		{
			XjActorAccessor.SetInt(actor, progressKey, preservedProgress);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, preservedProgress);
		}
		if (image > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage, image);
		if (purity > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingPurity, purity);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingScars, Math.Max(0, scars));
		if (lastAnnualYear > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmLastAnnualYear, lastAnnualYear);
		if (lastRetreatYear > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmRetreatLastYear, lastRetreatYear);
		string migrationItem = XjChronology.FormatYear(safeMigrationYear)
			+ "·闰位归属校正为" + manifest;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingHistory,
			MergeJinXingHistory(history, migrationItem));
		XjGuoWeiImageStateService.Refresh(actor, safeMigrationYear, recordHistory: false);
		RefreshRuntimeDoctrine(actor);
	}

	internal static void InitializeOnPromotion(
		Actor actor,
		string sourceDaoTu,
		string manifestDaoTu,
		string positionType,
		string guoWei,
		string jinXing,
		int currentYear,
		bool isFuQi,
		string daoTitleOverride = "",
		bool recordFoundation = true,
		bool affectLineageVitality = true)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
		{
		string source = Normalize(sourceDaoTu);
		string manifest = Normalize(manifestDaoTu);
		if (source.Length == 0) source = manifest;
		if (manifest.Length == 0) manifest = source;
		string type = XjGuoWeiCalculator.NormalizePositionType(positionType);
		if (type.Length == 0) type = XjGuoWeiRegistry.ResolveTypeFromName(guoWei);
		string title = Normalize(daoTitleOverride);
		if (title.Length == 0) title = ResolveDaoTitle(actor);
		string runFormula = string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			? "借" + source + "闰" + manifest : string.Empty;
		string doctrine = ResolveDoctrine(source, manifest, type);
		string legacy = ResolveLegacyDoctrine(source, manifest, type);
		string scope = XjDaoLineageStateRegistry.ResolveInitialAuthorityScope(source, manifest, type, actorId, currentYear);
		int initialProgress = 120 + XjDeterministicHash.PositiveIndex(actorId + currentYear, "highrealm_initial_progress", 181);
		// 果位钟爱转世等旧链会在低境载体上预存兼容道行；正式登位时必须承继，
		// 不能被新制初始值覆盖。普通新晋角色该字段通常为空，不受影响。
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int inheritedProgress)
			&& inheritedProgress > initialProgress)
		{
			initialProgress = Math.Min(12000, inheritedProgress);
		}
		int initialImage = string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal) ? 180
			: string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? 130 : 90;
		string trueName = ResolveJinXingTrueName(jinXing, manifest, actor);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmDaoSchema, CurrentSchemaVersion);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, source);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionType, type);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanRunFormula, runFormula);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanRunDoctrine, doctrine);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanDaoTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, scope);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanLegacyDoctrine, legacy);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingRootDaoTu, source);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingManifestDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingPositionType, type);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingTrueName, trueName);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingPurity, 70 + XjDeterministicHash.PositiveIndex(actorId, "jinxing_purity", 26));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingScars, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingHistory,
			XjChronology.FormatYear(Math.Max(1, currentYear)) + "·" + type + "定性");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage, initialImage);
		if (isFuQi)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, initialProgress);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanDaoXing, 0);
		}
		else
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanDaoXing, initialProgress);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, 0);
		}
		// 兼容现有战力、寿命、小境界和排行，只投影实际道行/修持，不再代表求位方向。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, initialProgress);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmLastAnnualYear, Math.Max(0, currentYear));

		if (recordFoundation)
			RecordProofFoundation(actor, source, manifest, type, doctrine, legacy, jinXing, scope, title, currentYear);
		string heldScope = ResolveHeldAuthorityScope(scope, source, manifest, type, initialProgress);
		XjDaoLineageStateRegistry.OnPromotion(actorId, actor.getName(), source, manifest, type, heldScope,
			currentYear, affectLineageVitality);
		XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: false);
		RefreshRuntimeDoctrine(actor);

		// 闰位证成后，根道仍留在 DaoTu/SourceDaoTu，玩家可见道途必须立刻投影 ManifestDaoTu。
		// 不等待下一次年度维护、窗口刷新或读档审计，避免“坎水闰渊照”仍挂坎水特质。
		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !string.Equals(source, manifest, StringComparison.Ordinal))
		{
			XjVisibleTraitSync.SyncDaoTuTrait(actor, source);
		}
		}
	}

	internal static void UpdateIntentionFromShenTong(Actor actor, string daoTu, in XjXianJiState state)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| state.Ids == null || state.Count < 3) return;
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		// 求位意向只在紫府五神通形成期间推演；成丹后的闰位继续以根道作为
		// 当前修炼 DaoTu，显道只进入果位/金性身份字段。不能再用成丹后的运行态
		// 神通变化反向重算并覆盖证道时固化的根道→显道关系。
		if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)) return;
		string source = Normalize(daoTu);
		int native = 0, lower = 0, external = 0;
		string externalDao = string.Empty;
		bool mixedExternal = false;
		for (int i = 0; i < state.Ids.Length; i++)
		{
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(source, state.Ids[i]);
			if (kind == XjXianJiPoolKind.Native) { native++; continue; }
			if (kind == XjXianJiPoolKind.Lower) { lower++; continue; }
			if (!XjXianJiCatalog.TryResolveOwningDaoTu(state.Ids[i], out string owner)) { mixedExternal = true; continue; }
			owner = Normalize(owner);
			if (externalDao.Length == 0) externalDao = owner;
			else if (!string.Equals(externalDao, owner, StringComparison.Ordinal)) mixedExternal = true;
			external++;
		}
		string type;
		string target = source;
		if (state.Count >= XjXianJiState.MaxCount)
		{
			// 第五门完成后必须与真正求金判定使用同一权威计算器，
			// 防止照录显示“闰位意向”而实际因混入下位/多外道成为无门。
			type = XjGuoWeiCalculator.Calculate(actor, source, state);
			target = XjGuoWeiCalculator.ResolveManifestDaoTu(source, state, type);
		}
		else if (mixedExternal || (external > 0 && lower > 0)) type = "岐路";
		else if (external > 0 && native >= 2 && lower == 0)
		{
			// 求位意向阶段也执行最终求金的同一套硬边界与道途拓扑门槛，
			// 防止低道慧角色先显示一个最终永远不可能成立的闰位意向。
			float requiredDaoHui = XjGuoWeiCalculator.ResolveIntercalaryDaoHuiThreshold(source, externalDao);
			if (XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(source, externalDao)
				|| XjDaoHuiPolicy.Read(actor) + 0.001f < requiredDaoHui)
			{
				type = "岐路";
			}
			else
			{
				type = XjGuoWeiCalculator.RunWei;
				target = externalDao;
			}
		}
		else if (lower > 0 && external == 0) type = XjGuoWeiCalculator.YuWei;
		else type = XjGuoWeiCalculator.ZhengWei;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionType, type);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, target);
	}

	internal static void TickAnnual(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		MigrateLegacy(actor, currentYear);
		ReconcileCompatibilityProgressProjection(actor);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmLastAnnualYear, out int lastYear)
			&& lastYear >= currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmLastAnnualYear, currentYear);
		RunAnnualReduction(actor, currentYear, retreat: false);
	}

	internal static void TickRetreat(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		MigrateLegacy(actor, currentYear);
		ReconcileCompatibilityProgressProjection(actor);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmRetreatLastYear, out int lastYear)
			&& lastYear >= currentYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmRetreatLastYear, currentYear);
		RunAnnualReduction(actor, currentYear, retreat: true);
	}

	/// <summary>
	/// 高境个人年度归约器。年度修持与闭关修持共享同一业务顺序，只以参数决定
	/// 基础增长、果位意象增量和尊修事口径；权柄、神通、真景与UI修订因此只提交一次。
	/// </summary>
	private static void RunAnnualReduction(Actor actor, int currentYear, bool retreat)
	{
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool isFuQi = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		if (!isFuQi && !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) return;

		long actorId = ((BaseSystemData)actor.data).id;
		using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
		{
			int baseGrowth = retreat ? 32 : 12;
			if (!retreat)
			{
				// 高境修持统一读取有效道慧；并火紫府金丹道的“越进境越焚慧”
				// 因此会真实进入年度修持，而不只是停留在人物面板。
				float daoHui = XjDaoHuiPolicy.Read(actor);
				baseGrowth += Math.Max(0, Math.Min(12, (int)daoHui / 10));
			}

			int imageGrowth = 2;
			if (retreat)
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string source);
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
				imageGrowth = string.Equals(Normalize(source), Normalize(manifest), StringComparison.Ordinal) ? 8 : 6;
			}

			XjShenTongMutationService.TickAnnual(actor, currentYear);
			int growth = Math.Max(1, (int)Math.Round(
				(baseGrowth + ResolveJinXingProgressModifier(actor))
				* XjShenTongMutationService.ResolveCultivationMultiplier(actor)
				* XjGuoWeiImageStateService.ResolveCultivationMultiplier(actor)
				* XjShiFruitPositionLockSystem.ResolveCultivationMultiplier(actor)));
			IncreaseProgress(actor, isFuQi, growth, imageGrowth);
			XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, currentYear);
			XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: true);
			RefineJinXing(actor, currentYear, retreat);
			RefreshRuntimeDoctrine(actor);

			XjRespectPracticeSystem.TickAnnual(actor, currentYear, retreat);
			if (!retreat) TryStartLineageRevival(actor, currentYear);
			TickLineageRevival(actor, currentYear, retreat);
			RefreshRuntimeDoctrine(actor);
		}
	}

	internal static XjHighRealmDoctrineSnapshot BuildSnapshot(Actor actor)
	{
		if (actor?.data == null) return default;
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjHighRealmAggregateStore.TryGetDoctrine(actorId, out XjHighRealmDoctrineSnapshot cached))
			return cached;
		XjHighRealmDoctrineSnapshot snapshot = BuildSnapshotReadOnly(actor);
		XjHighRealmAggregateStore.ApplyDoctrine(actorId, in snapshot);
		return snapshot;
	}

	private static XjHighRealmDoctrineSnapshot BuildSnapshotReadOnly(Actor actor)
	{
		if (actor?.data == null) return default;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingPositionType, out string type);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string source);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanRunFormula, out string runFormula);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanRunDoctrine, out string doctrine);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanDaoTitle, out string title);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jinXing);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string scope);
		scope = XjDaoTaiDualPositionSystem.MergeEffectiveAuthorityScope(actor, scope);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanProofCityName, out string cityName);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanLegacyDoctrine, out string legacy);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjRespectPracticeType, out string respectType);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjRespectPracticeStage, out string respectStage);
		if (string.Equals(XjGuoWeiCalculator.NormalizePositionType(type), XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			// 闰位说明永远由“证道根道 -> 果位显道”的双身份现算，不信旧版保存的
			// RunFormula 文本。这样旧档里曾经写反的“借宝闰司天”也会直接显示为
			// 正确的“借司天闰宝土”，且神通变化后仍以固化身份为准。
			XjJinDanState jinDanState = XjJinDanAccessor.BuildStateWithoutDaoMigration(actor);
			ResolvePositionIdentity(actor, jinDanState.GuoWei, out string resolvedSource, out string resolvedManifest);
			if (!string.IsNullOrWhiteSpace(resolvedSource) && !string.IsNullOrWhiteSpace(resolvedManifest))
			{
				source = resolvedSource;
				manifest = resolvedManifest;
				runFormula = "借" + source + "闰" + manifest;
			}
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjRespectPracticeProgress, out int respectProgress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool isFuQi = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		XjActorAccessor.TryGetInt(actor, isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing, out int progress);
		string respect = string.IsNullOrWhiteSpace(respectType) ? string.Empty
			: respectType.Trim() + "·" + Normalize(respectStage) + " · 进度" + Math.Max(0, respectProgress) + " · 满1000";
		return new XjHighRealmDoctrineSnapshot(
			!string.IsNullOrWhiteSpace(type), type, source, manifest, runFormula, doctrine, title, jinXing,
			(scope ?? string.Empty).Replace('|', '、'), cityName, legacy, progress, image, respect);
	}

	private static void RefreshRuntimeDoctrine(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		XjHighRealmDoctrineSnapshot snapshot = BuildSnapshotReadOnly(actor);
		XjHighRealmAggregateStore.ApplyDoctrine(actorId, in snapshot);
	}

	internal static float GetProofFoundationCultivationMultiplier(Actor actor)
	{
		if (actor?.data == null) return 1f;
		City city = actor.city;
		if (city?.data == null && XuanJianVNext.Systems.Sect.XjSectCityData.TryFindActorZongMenCity(actor, out City zongMenCity)) city = zongMenCity;
		if (city?.data == null) return 1f;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = Normalize(daoTu);
		if (daoTu.Length == 0) return 1f;
		return XjDaoLineageStateRegistry.ResolveFoundationMultiplier(city.data.id, daoTu);
	}

	internal static string BuildCodexSummary(Actor actor)
	{
		XjHighRealmDoctrineSnapshot state = BuildSnapshot(actor);
		string imageState = XjGuoWeiImageStateService.BuildSummary(actor);
		return BuildCodexSummary(actor, in state, imageState);
	}

	internal static string BuildCodexSummary(
		Actor actor,
		in XjHighRealmDoctrineSnapshot state,
		string imageState)
	{
		if (!state.Found) return string.Empty;
		List<string> lines = new List<string>();
		string realmId = XjRealmHelper.NormalizeId(
			XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool isFuQi = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		lines.Add((isFuQi ? "真君位格：" : "金丹位格：") + state.ManifestDaoTu + state.PositionType);
		if (!string.IsNullOrWhiteSpace(state.RunFormula)) lines.Add("闰法：" + state.RunFormula);
		if (!string.IsNullOrWhiteSpace(state.SourceDaoTu)) lines.Add("根本道途：" + state.SourceDaoTu);
		if (!string.IsNullOrWhiteSpace(state.ManifestDaoTu)) lines.Add("显位道途：" + state.ManifestDaoTu);
		if (!string.IsNullOrWhiteSpace(state.Doctrine)) lines.Add("证道法理：" + state.Doctrine);
		if (!string.IsNullOrWhiteSpace(state.JinXing)) lines.Add("金性：" + state.JinXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingPurity, out int purity);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingScars, out int scars);
		if (purity > 0) lines.Add("金性状态：纯度" + purity + " · 上限100，裂痕" + Math.Max(0, scars));
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string jinXingHistory)
			&& !string.IsNullOrWhiteSpace(jinXingHistory)) lines.Add("金性沿革：\n  " + FormatJinXingHistory(jinXingHistory));
		if (!string.IsNullOrWhiteSpace(state.AuthorityScope)) lines.Add("权辖：" + state.AuthorityScope);
		if (!string.IsNullOrWhiteSpace(state.ProofCityName)) lines.Add("证道之地：" + state.ProofCityName);
		if (!string.IsNullOrWhiteSpace(state.LegacyDoctrine)) lines.Add("留存道基：" + state.LegacyDoctrine);
		lines.Add("果位意象：" + (string.IsNullOrWhiteSpace(imageState) ? "真景未成" : imageState)
			+ "（意象" + state.PositionImage + " · 上限10000）");
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiImageHistory, out string imageHistory)
			&& !string.IsNullOrWhiteSpace(imageHistory))
			lines.Add("意象沿革：" + imageHistory.Replace('|', '；'));
		if (!string.IsNullOrWhiteSpace(state.RespectPractice)) lines.Add("尊修事：" + state.RespectPractice);
		string lineageSummary = XjDaoLineageStateRegistry.BuildLineageSummary(state.ManifestDaoTu);
		if (!string.IsNullOrWhiteSpace(lineageSummary)) lines.Add("道统：" + lineageSummary);
		string authorityFlow = XjDaoLineageStateRegistry.BuildAuthorityStateSummary(state.ManifestDaoTu);
		if (!string.IsNullOrWhiteSpace(authorityFlow)) lines.Add("权柄流转：" + authorityFlow);
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjShenTongDifficultyPermille, out int shenTongPermille)
			&& shenTongPermille > 0)
		{
			string tendency = shenTongPermille > 1050 ? "顺遂" : shenTongPermille < 950 ? "艰涩" : "平常";
			lines.Add("神通显化：" + tendency + "（" + shenTongPermille + "‰）");
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenTongMutationHistory, out string mutationHistory)
			&& !string.IsNullOrWhiteSpace(mutationHistory))
			lines.Add("神通沿革：" + mutationHistory.Replace('|', '；'));
		return string.Join("\n", lines);
	}

	internal static void ApplyRespectPositionChange(
		Actor actor,
		string newPositionType,
		string newGuoWei,
		string newJinXing,
		int currentYear,
		string respectPractice,
		string sourceDaoTuOverride = "",
		string manifestDaoTuOverride = "")
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string source);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
		if (!string.IsNullOrWhiteSpace(sourceDaoTuOverride)) source = sourceDaoTuOverride.Trim();
		if (!string.IsNullOrWhiteSpace(manifestDaoTuOverride)) manifest = manifestDaoTuOverride.Trim();
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanDaoTitle, out string title);
		string type = XjGuoWeiCalculator.NormalizePositionType(newPositionType);
		if (type.Length == 0) type = XjGuoWeiRegistry.ResolveTypeFromName(newGuoWei);
		string doctrine = ResolveDoctrine(source, manifest, type);
		string legacy = ResolveLegacyDoctrine(source, manifest, type);
		long actorId = ((BaseSystemData)actor.data).id;
		using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
		{
		string scope = XjDaoLineageStateRegistry.ResolveInitialAuthorityScope(source, manifest, type, actorId, currentYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, source);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingRootDaoTu, source);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingManifestDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionType, type);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingPositionType, type);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingManifestDaoTu, manifest);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingTitle, Normalize(title));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanRunFormula,
			string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
				? "借" + source + "闰" + manifest : string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanRunDoctrine, doctrine);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanLegacyDoctrine, legacy);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, scope);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingTrueName,
			ResolveJinXingTrueName(newJinXing, manifest, actor));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string history);
		string item = XjChronology.FormatYear(Math.Max(1, currentYear))
			+ "·" + Normalize(respectPractice) + "成果位";
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingHistory,
			MergeJinXingHistory(history, item));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage, Math.Max(500, image - 1200));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
		string heldScope = ResolveHeldAuthorityScope(scope, source, manifest, type, Math.Max(daoXing, xiuChi));
		XjDaoLineageStateRegistry.OnPromotion(
			actorId, actor.getName(), source, manifest, type, heldScope, currentYear);
		XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: true);
		RefreshRuntimeDoctrine(actor);
		}
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null) return;
		for (int i = 0; i < ArchiveStringKeys.Length; i++) XjActorAccessor.SetString(actor, ArchiveStringKeys[i], string.Empty);
		for (int i = 0; i < ArchiveIntKeys.Length; i++) XjActorAccessor.SetInt(actor, ArchiveIntKeys[i], 0);
		for (int i = 0; i < ArchiveLongKeys.Length; i++) XjActorAccessor.SetLong(actor, ArchiveLongKeys[i], 0L);
		long actorId = ((BaseSystemData)actor.data).id;
		XjHighRealmAggregateStore.RemoveActor(actorId);
	}

	internal static string ExportActorStateJson(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		MigrateLegacy(actor, World.world?.map_stats?.year ?? 0);
		XjHighRealmActorArchiveData state = new XjHighRealmActorArchiveData { SchemaVersion = CurrentSchemaVersion };
		for (int i = 0; i < ArchiveStringKeys.Length; i++)
		{
			if (XjActorAccessor.TryGetString(actor, ArchiveStringKeys[i], out string value)
				&& !string.IsNullOrWhiteSpace(value)) state.StringValues[ArchiveStringKeys[i]] = value;
		}
		for (int i = 0; i < ArchiveIntKeys.Length; i++)
		{
			if (XjActorAccessor.TryGetInt(actor, ArchiveIntKeys[i], out int value)) state.IntValues[ArchiveIntKeys[i]] = value;
		}
		for (int i = 0; i < ArchiveLongKeys.Length; i++)
		{
			if (XjActorAccessor.TryGetLong(actor, ArchiveLongKeys[i], out long value)) state.LongValues[ArchiveLongKeys[i]] = value;
		}
		return JsonConvert.SerializeObject(state, Formatting.None);
	}

	internal static bool ImportActorStateJson(Actor actor, string json, int currentYear)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(json)) return false;
		XjHighRealmActorArchiveData state;
		try { state = JsonConvert.DeserializeObject<XjHighRealmActorArchiveData>(json); }
		catch { return false; }
		if (state == null) return false;
		Clear(actor);
		if (state.StringValues != null)
		{
			for (int i = 0; i < ArchiveStringKeys.Length; i++)
			{
				if (state.StringValues.TryGetValue(ArchiveStringKeys[i], out string value))
					XjActorAccessor.SetString(actor, ArchiveStringKeys[i], value ?? string.Empty);
			}
		}
		if (state.IntValues != null)
		{
			for (int i = 0; i < ArchiveIntKeys.Length; i++)
			{
				if (state.IntValues.TryGetValue(ArchiveIntKeys[i], out int value))
					XjActorAccessor.SetInt(actor, ArchiveIntKeys[i], value);
			}
		}
		if (state.LongValues != null)
		{
			for (int i = 0; i < ArchiveLongKeys.Length; i++)
			{
				if (state.LongValues.TryGetValue(ArchiveLongKeys[i], out long value))
					XjActorAccessor.SetLong(actor, ArchiveLongKeys[i], value);
			}
		}
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjHighRealmDaoSchema, CurrentSchemaVersion);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
		int compatibilityProgress = Math.Max(daoXing, xiuChi);
		if (compatibilityProgress > 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, compatibilityProgress);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanSourceDaoTu, out string source);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string manifest);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingPositionType, out string type);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanAuthorityScope, out string scope);
		long actorId = ((BaseSystemData)actor.data).id;
		using (XjHighRealmAggregateStore.BeginReduction(actorId, currentYear))
		{
		if (!string.IsNullOrWhiteSpace(manifest))
		{
			string heldScope = ResolveHeldAuthorityScope(scope, source, manifest, type, compatibilityProgress);
			XjDaoLineageStateRegistry.OnPromotion(actorId, actor.getName(), source, manifest, type, heldScope,
				Math.Max(1, currentYear), affectVitality: false);
		}
		XjGuoWeiImageStateService.Refresh(actor, currentYear, recordHistory: false);
		RefreshRuntimeDoctrine(actor);
		}
		return true;
	}

	/// <summary>
	/// 明确的手动导入/登名石恢复入口。XjJinDanYiXiang 只是兼容投影，
	/// 真正的小境界进度必须同步写入道行/修持权威字段。普通年度增长不得调用本方法。
	/// </summary>
	internal static bool RestoreImportedProgressMetadata(Actor actor, int progress, bool overwriteExisting)
	{
		if (actor?.data == null || progress <= 0) return false;
		int safeProgress = Math.Min(12000, Math.Max(1, progress));
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		string progressKey = isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing;
		XjActorAccessor.TryGetInt(actor, progressKey, out int existing);
		int next = overwriteExisting ? safeProgress : Math.Max(existing, safeProgress);
		bool changed = existing != next;
		if (changed) XjActorAccessor.SetInt(actor, progressKey, next);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int compatibility);
		if (compatibility != next)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, next);
			changed = true;
		}
		return changed;
	}

	internal static void EnsureRestoredState(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		MigrateLegacy(actor, currentYear);
		ReconcileCompatibilityProgressProjection(actor);
		RefreshRuntimeDoctrine(actor);
	}

	/// <summary>
	/// XjJinDanYiXiang 自0.9.6.5起只允许作为真实道行/修持的小境界兼容投影。
	/// RC11.5 的“果位意象”调试特质曾错误覆盖此字段；真实 XjJinDanDaoXing /
	/// XjZhenJunXiuChi 并未受损，因此重载时直接以真实进度纠正即可，旧档无需回滚。
	/// </summary>
	private static void ReconcileCompatibilityProgressProjection(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanDaoXing, out int daoXing);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhenJunXiuChi, out int xiuChi);
		int realProgress = Math.Max(daoXing, xiuChi);
		if (realProgress <= 0) return;
		realProgress = Math.Min(12000, Math.Max(1, realProgress));
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int compatibilityProgress);
		if (compatibilityProgress != realProgress)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, realProgress);
		}
	}

	private static void IncreaseProgress(Actor actor, bool isFuQi, int progressGrowth, int imageGrowth)
	{
		string progressKey = isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing;
		XjActorAccessor.TryGetInt(actor, progressKey, out int progress);
		progress = Math.Min(12000, Math.Max(1, progress) + Math.Max(0, progressGrowth));
		XjActorAccessor.SetInt(actor, progressKey, progress);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, progress);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage, Math.Min(10000, Math.Max(0, image) + Math.Max(0, imageGrowth)));
	}

	private static string ResolveHeldAuthorityScope(
		string fullScope, string sourceDaoTu, string manifestDaoTu, string positionType, int progress)
	{
		string type = XjGuoWeiCalculator.NormalizePositionType(positionType);
		int count;
		if (string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			count = progress >= 6000 ? 6 : progress >= 3000 ? 5 : progress >= 1000 ? 4 : 3;
		}
		else if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) count = progress >= 4000 ? 3 : 2;
		else if (string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) count = progress >= 6000 ? 2 : 1;
		else return string.Empty;

		List<string> raw = new List<string>();
		string[] parts = (fullScope ?? string.Empty).Split(
			new[] { '|', ',', '，', '、' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string value = Normalize(parts[i]);
			if (value.Length > 0 && !raw.Contains(value)) raw.Add(value);
		}
		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			IReadOnlyList<string> targetCatalog = XjGuoWeiAuthorityCatalog.Get(manifestDaoTu);
			IReadOnlyList<string> sourceCatalog = XjGuoWeiAuthorityCatalog.Get(sourceDaoTu);
			List<string> target = new List<string>();
			List<string> borrowed = new List<string>();
			List<string> crossed = new List<string>();
			List<string> other = new List<string>();
			for (int i = 0; i < raw.Count; i++)
			{
				string value = raw[i];
				if (value.EndsWith("交感", StringComparison.Ordinal)) crossed.Add(value);
				else if (!string.Equals(Normalize(sourceDaoTu), Normalize(manifestDaoTu), StringComparison.Ordinal)
					&& ContainsAuthority(sourceCatalog, value)) borrowed.Add(value);
				else if (ContainsAuthority(targetCatalog, value)) target.Add(value);
				else other.Add(value);
			}
			raw.Clear();
			if (target.Count > 0) raw.Add(target[0]);
			if (borrowed.Count > 0 && !raw.Contains(borrowed[0])) raw.Add(borrowed[0]);
			if (crossed.Count > 0 && !raw.Contains(crossed[0])) raw.Add(crossed[0]);
			for (int i = 1; i < target.Count; i++) if (!raw.Contains(target[i])) raw.Add(target[i]);
			for (int i = 1; i < borrowed.Count; i++) if (!raw.Contains(borrowed[i])) raw.Add(borrowed[i]);
			for (int i = 1; i < crossed.Count; i++) if (!raw.Contains(crossed[i])) raw.Add(crossed[i]);
			for (int i = 0; i < other.Count; i++) if (!raw.Contains(other[i])) raw.Add(other[i]);
		}
		if (raw.Count > count) raw.RemoveRange(count, raw.Count - count);
		return string.Join("|", raw);
	}

	private static bool ContainsAuthority(IReadOnlyList<string> values, string target)
	{
		if (values == null || string.IsNullOrWhiteSpace(target)) return false;
		for (int i = 0; i < values.Count; i++)
		{
			if (string.Equals(values[i], target, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static int ResolveJinXingProgressModifier(Actor actor)
	{
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingPurity, out int purity);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingScars, out int scars);
		return Math.Max(-8, Math.Min(8, (purity - 70) / 5 - Math.Max(0, scars) * 2));
	}

	private static void RefineJinXing(Actor actor, int currentYear, bool retreat)
	{
		if (actor?.data == null || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int cadence = retreat ? 10 : 25;
		if (currentYear % cadence != XjDeterministicHash.PositiveIndex(actorId, retreat ? "jinxing_retreat" : "jinxing_annual", cadence)) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingPurity, out int purity);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinXingScars, out int scars);
		if (retreat && scars > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingScars, scars - 1);
			AppendJinXingHistory(actor, currentYear, "闭关弥合金性裂痕");
			return;
		}
		if (purity < 100)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinXingPurity, purity + 1);
			if (retreat) AppendJinXingHistory(actor, currentYear, "闭关炼真");
		}
	}

	private static void AppendJinXingHistory(Actor actor, int year, string eventName)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinXingHistory, out string history);
		string item = XjChronology.FormatYear(Math.Max(1, year)) + "·" + Normalize(eventName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinXingHistory,
			MergeJinXingHistory(history, item));
	}

	private static string MergeJinXingHistory(string history, string item)
	{
		string joined = string.IsNullOrWhiteSpace(history) ? item : history.Trim() + "；" + item;
		return FormatJinXingHistory(joined).Replace("\n", "；");
	}

	private static string FormatJinXingHistory(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
		string[] parts = raw.Split(new[] { '；', ';', '|', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> ordered = new List<string>(parts.Length);
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < parts.Length; i++)
		{
			string value = parts[i].Trim();
			if (value.Length > 0 && seen.Add(value)) ordered.Add(value);
		}
		ordered.Sort(CompareJinXingHistoryItem);
		return string.Join("\n", ordered);
	}

	private static int CompareJinXingHistoryItem(string left, string right)
	{
		int leftYear = ExtractChronologyYear(left);
		int rightYear = ExtractChronologyYear(right);
		int byYear = leftYear.CompareTo(rightYear);
		return byYear != 0 ? byYear : string.CompareOrdinal(left, right);
	}

	private static int ExtractChronologyYear(string item)
	{
		const string prefix = "玄鉴历";
		int start = (item ?? string.Empty).IndexOf(prefix, StringComparison.Ordinal);
		if (start < 0) return int.MaxValue;
		start += prefix.Length;
		int end = item.IndexOf('年', start);
		if (end <= start) return int.MaxValue;
		return int.TryParse(item.Substring(start, end - start), out int year) ? year : int.MaxValue;
	}

	private static void MigrateLegacy(Actor actor, int currentYear)
	{
		if (actor?.data == null) return;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmDaoSchema, out int schema)
			&& schema >= CurrentSchemaVersion) return;
		XjJinDanState jindan = XjJinDanAccessor.BuildStateWithoutDaoMigration(actor);
		if (!jindan.Found) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int legacyProgress);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		string currentDaoTu = Normalize(daoTu);
		string type = XjGuoWeiRegistry.ResolveTypeFromName(jindan.GuoWei);
		string manifest = Normalize(XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(jindan.GuoWei));
		if (manifest.Length == 0) manifest = currentDaoTu;
		string source = manifest;
		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			bool hasPersistedIdentity = TryReadPersistedIntercalaryIdentity(
				actor, out string persistedSource, out string persistedManifest);
			if (hasPersistedIdentity)
			{
				source = persistedSource;
				manifest = persistedManifest;
			}

			// 旧档只有在根道/显道固化字段缺失时才从五神通补推；
			// 已经写实的闰位身份永远不随当前神通重定向。
			bool mayResolveFromCurrentShenTong = !hasPersistedIdentity;
			if (mayResolveFromCurrentShenTong
				&& XjCultivationPathRules.IsZiFuJinDan(actor)
				&& XjGuoWeiCalculator.TryResolveIntercalaryIdentity(
					actor, XjXianJiAccessor.BuildState(actor), out string resolvedSource, out string resolvedManifest))
			{
				source = resolvedSource;
				manifest = resolvedManifest;
			}
			else if (!hasPersistedIdentity)
			{
				if (XjFruitPositionWorldState.TryGetPosition(jindan.GuoWei, out XjDerivedPositionArchiveRecord position)
					&& position != null && !string.IsNullOrWhiteSpace(position.ExternalDaoTu))
				{
					source = Normalize(position.ExternalDaoTu);
				}
				else if (currentDaoTu.Length > 0 && !string.Equals(currentDaoTu, manifest, StringComparison.Ordinal))
				{
					source = currentDaoTu;
				}
			}
		}
		string realmId = XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter));
		bool isFuQi = string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
		int successYear = jindan.SuccessYear > 0 ? jindan.SuccessYear : Math.Max(1, currentYear);
		InitializeOnPromotion(actor, source, manifest, type, jindan.GuoWei, jindan.JinXing,
			successYear, isFuQi, recordFoundation: false, affectLineageVitality: false);
		if (legacyProgress > 0)
		{
			string progressKey = isFuQi ? XjActorDataKeys.XjZhenJunXiuChi : XjActorDataKeys.XjJinDanDaoXing;
			int preserved = Math.Min(12000, Math.Max(1, legacyProgress));
			XjActorAccessor.SetInt(actor, progressKey, preserved);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, preserved);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGuoWeiImage, out int image);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGuoWeiImage,
				Math.Max(image, Math.Min(10000, preserved / 2)));
		}
	}

	private static void RecordProofFoundation(
		Actor actor, string source, string manifest, string type, string doctrine, string legacy,
		string jinXing, string scope, string title, int year)
	{
		City city = actor?.city;
		if (city?.data == null && XuanJianVNext.Systems.Sect.XjSectCityData.TryFindActorZongMenCity(actor, out City zongMenCity)) city = zongMenCity;
		if (city?.data == null) return;
		long cityId = city.data.id;
		string cityName = string.IsNullOrWhiteSpace(city.data.name) ? "无名城" : city.data.name.Trim();
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetLong(actor, XjActorDataKeys.XjJinDanProofCityId, cityId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanProofCityName, cityName);
		city.data.set(CityFoundationSchemaKey, CurrentSchemaVersion);
		city.data.set(CityFoundationSourceKey, source);
		city.data.set(CityFoundationManifestKey, manifest);
		city.data.set(CityFoundationDoctrineKey, doctrine);
		city.data.set(CityFoundationLegacyKey, legacy);
		city.data.set(CityFoundationFounderKey, actorId.ToString(CultureInfo.InvariantCulture));
		city.data.set(CityFoundationYearKey, Math.Max(0, year));
		XjDaoLineageStateRegistry.RecordFoundation(new XjDaoProofFoundationArchiveData
		{
			CityId = cityId, CityName = cityName, FounderActorId = actorId,
			FounderName = actor.getName() ?? string.Empty, DaoTitle = title,
			SourceDaoTu = source, ManifestDaoTu = manifest, PositionType = type,
			Doctrine = doctrine, LegacyDoctrine = legacy, JinXing = jinXing,
			AuthorityScope = scope, FoundedYear = Math.Max(0, year)
		});
	}

	private static void TryStartLineageRevival(Actor actor, int currentYear)
	{
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string daoTu)) return;
		int daoHui = (int)XjDaoHuiPolicy.Read(actor);
		if (XjDaoLineageStateRegistry.TryAdvanceRevival(actor, daoTu, daoHui, currentYear, out string text)
			&& !string.IsNullOrWhiteSpace(text))
		{
			XjWorldHistoryRegistry.AddActorEvent(actor, text, XjEventIconCatalog.JinDanUpgrade);
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(
				text, XjAnnouncementCategory.HighRealm, duration: 7f, color: "#E4BE72", iconId: XjEventIconCatalog.JinDanUpgrade);
		}
	}

	private static void TickLineageRevival(Actor actor, int currentYear, bool retreat)
	{
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjLineageRevivalStage, out string stage)
			|| string.IsNullOrWhiteSpace(stage) || string.Equals(stage, "已定统", StringComparison.Ordinal)) return;
		string lastYearKey = retreat ? XjActorDataKeys.XjLineageRevivalRetreatLastYear : XjActorDataKeys.XjLineageRevivalLastYear;
		if (XjActorAccessor.TryGetInt(actor, lastYearKey, out int last) && last >= currentYear) return;
		XjActorAccessor.SetInt(actor, lastYearKey, currentYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjLineageRevivalProgress, out int progress);
		progress += retreat ? 18 : 7;
		if (progress < 100)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLineageRevivalProgress, progress);
			return;
		}
		string next = string.Equals(stage.Trim(), "察旧", StringComparison.Ordinal) ? "立图"
			: string.Equals(stage.Trim(), "立图", StringComparison.Ordinal) ? "定统" : "已定统";
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjLineageRevivalStage, next);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjLineageRevivalProgress, 0);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjLineageRevivalDirection, out string direction);
		if (string.Equals(next, "已定统", StringComparison.Ordinal))
		{
			XjDaoLineageStateRegistry.CompleteRevival(actor, daoTu, direction, currentYear);
			string message = "【道统中兴】" + (actor.getName() ?? "无名真君") + "完成" + daoTu + "道统定统，以“" + direction + "”重释核心，后世神通与权柄自此改易。";
			XjWorldHistoryRegistry.AddActorEvent(actor, message, XjEventIconCatalog.JinDanUpgrade);
			XjBroadcastSystem.ShowRecordedCategorizedWorldTip(message, XjAnnouncementCategory.HighRealm, iconId: XjEventIconCatalog.JinDanUpgrade);
		}
	}

	private static string ResolveDaoTitle(Actor actor)
	{
		string title = string.Empty;
		if (actor?.data != null) XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out title);
		title = Normalize(title);
		string[] suffixes = { "真人", "道人", "玄君", "真君", "羽士", "道君", "仙尊", "天尊" };
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (title.EndsWith(suffixes[i], StringComparison.Ordinal)) title = title.Substring(0, title.Length - suffixes[i].Length).Trim();
		}
		int separator = title.IndexOf('·');
		if (separator > 0) title = title.Substring(0, separator).Trim();
		if (title.Length >= 2) return title.Substring(0, 2);
		string name = actor?.getName() ?? string.Empty;
		name = XjStringHelper.DisplayNameWithoutRealmSuffix(name, "玄清");
		return name.Length >= 2 ? name.Substring(0, 2) : "玄清";
	}

	private static string ResolveJinXingTrueName(string jinXing, string manifestDaoTu, Actor actor)
	{
		string text = Normalize(jinXing);
		if (text.EndsWith("性", StringComparison.Ordinal)) text = text.Substring(0, text.Length - 1);
		string manifest = Normalize(manifestDaoTu);
		if (manifest.Length > 0) text = text.Replace(manifest, string.Empty);
		string title = ResolveDaoTitle(actor);
		text = text.Replace(title, string.Empty).Replace("闰", string.Empty);
		if (text.Length >= 2) return text.Substring(text.Length - 2, 2);
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		string[] values = { "云沧", "玄津", "玉澜", "清渊", "天潢", "元泽", "灵湛", "太泓" };
		return values[XjDeterministicHash.PositiveIndex(actorId, manifest + "|jinxing_true_name", values.Length)];
	}

	private static string ResolveDoctrine(string source, string manifest, string type)
	{
		if (!string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			return string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				? "余以辅正，持偏裨而续道脉"
				: "果以统玄，正受一道而辖群象";
		}
		if (string.Equals(source, "渌水", StringComparison.Ordinal) && string.Equals(manifest, "坎水", StringComparison.Ordinal))
			return "闰以变正，渌以润洪";
		if (string.Equals(source, "府水", StringComparison.Ordinal) && string.Equals(manifest, "合水", StringComparison.Ordinal))
			return "闰以易位，府以藏流，合以并脉";
		if (string.Equals(source, "坎水", StringComparison.Ordinal) && string.Equals(manifest, "渌水", StringComparison.Ordinal))
			return "闰以旁通，坎以行险，渌以滋生";
		if (string.Equals(source, "牝水", StringComparison.Ordinal) && string.Equals(manifest, "府水", StringComparison.Ordinal))
			return "闰以显藏，牝以纳幽，府以蓄泽";
		string sourceShort = ShortDao(source);
		string manifestShort = ShortDao(manifest);
		bool sameRoot = source.Length > 0 && manifest.Length > 0
			&& source[source.Length - 1] == manifest[manifest.Length - 1];
		return sameRoot
			? "闰以变正，" + sourceShort + "以通流，" + manifestShort + "以定象"
			: "闰以变正，" + sourceShort + "以借玄，" + manifestShort + "以显位";
	}

	private static string ResolveLegacyDoctrine(string source, string manifest, string type)
	{
		if (string.Equals(type, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			if (string.Equals(source, "渌水", StringComparison.Ordinal) && string.Equals(manifest, "坎水", StringComparison.Ordinal)) return "润变宣法";
			return ShortDao(source) + "变" + ShortDao(manifest) + "宣法";
		}
		return string.Equals(type, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
			? ShortDao(manifest) + "辅正宣法" : ShortDao(manifest) + "正受宣法";
	}

	private static string ShortDao(string daoTu)
	{
		string value = Normalize(daoTu);
		if (value.Length == 2 && "阴阳雷金木水火土炁仪".IndexOf(value[1]) >= 0) return value.Substring(0, 1);
		return value;
	}
	private static string Normalize(string value) => (value ?? string.Empty).Trim();
}
