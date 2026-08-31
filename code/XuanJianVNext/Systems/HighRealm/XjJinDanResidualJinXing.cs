using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.HighRealm;

// 金丹遗留金性不再注册为角色特质。这里仅保留奇遇洞天所得金性的
// 隐藏来源标记，用于延续原有真人转世判定，并负责旧存档特质迁移。
internal static class XjJinDanResidualJinXing
{
	internal const string LegacyTraitId = "JinDanJinXing";
	private const string QiYuDongTianSourcePrefix = "QiYuDongTian:";
	internal const string FamilyBorrowSourcePrefix = "FamilyBorrow:";

	internal static bool HasLegacyTrait(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		try
		{
			return actor.hasTrait(LegacyTraitId);
		}
		catch (System.Exception xjCaught32_1)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanResidualJinXing.cs:32", xjCaught32_1);
			
			return false;
		}
	}

	internal static bool HasValidGrant(Actor actor)
	{
		return TryGetValidGrant(actor, out _);
	}

	internal static bool TryGetValidGrant(Actor actor, out string jinXing)
	{
		jinXing = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXing, out string stored)
			|| string.IsNullOrWhiteSpace(stored)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, out string source))
		{
			return false;
		}

		if (IsFamilyBorrowSource(source))
		{
			jinXing = stored.Trim();
			return true;
		}

		if (!IsQiYuDongTianSource(source)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return false;
		}

		jinXing = stored.Trim();
		return true;
	}

	internal static void ReconcileSource(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		// The asset is no longer registered, but an old save can still carry the legacy id.
		if (HasLegacyTrait(actor))
		{
			try
			{
				actor.removeTrait(LegacyTraitId);
			}
			catch (System.Exception xjCaught85_2)
			{
				XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjJinDanResidualJinXing.cs:85", xjCaught85_2);
				
				// Some WorldBox builds cannot resolve a removed trait asset. The hidden
				// provenance keys below remain authoritative, so migration can continue.
			}
		}

		if (!TryGetValidGrant(actor, out string jinXing))
		{
			ClearState(actor);
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, out string validSource);
		// 家族借出的金性已经从重宝仓库扣除，只保留在角色身上的待转世凭据，
		// 绝不能在年度校正时再次回存仓库。
		if (IsFamilyBorrowSource(validSource)) return;

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, out int migrated)
			&& migrated > 0)
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L || !TryResolveFamilyStableId(actorId, out long familyStableId))
		{
			return;
		}

		int year = XjYearTracker.CurrentYear > 0 ? XjYearTracker.CurrentYear : (World.world?.map_stats?.year ?? 0);
		if (XjFamilyLingWuWarehouse.TryAddJinXing(
			familyStableId,
			jinXing,
			1,
			actorId,
			actor.getName(),
			year))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, 1);
		}
	}

	internal static bool TryGrantFromQiYuDongTian(
		Actor actor,
		string jinXing,
		string recordId,
		long familyStableId,
		int currentYear,
		string dongTianName)
	{
		if (actor?.data == null
			|| familyStableId <= 0L
			|| string.IsNullOrWhiteSpace(jinXing)
			|| string.IsNullOrWhiteSpace(recordId)
			|| HasValidGrant(actor)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return false;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L
			|| !XjChronicleWriter.RecordJinDanResidualAcquiredFromDongTian(
				actor,
				currentYear,
				jinXing,
				dongTianName,
				recordId)
			|| !XjFamilyLingWuWarehouse.TryAddJinXing(
				familyStableId,
				jinXing,
				1,
				actorId,
				actor.getName(),
				currentYear))
		{
			return false;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanResidualJinXing, jinXing.Trim());
		XjActorAccessor.SetString(
			actor,
			XjActorDataKeys.XjJinDanResidualJinXingSource,
			QiYuDongTianSourcePrefix + recordId.Trim());
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, 1);
		return true;
	}

	internal static bool TryBorrowForReincarnation(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || HasValidGrant(actor) || XjJinDanImmortalityRegistry.IsNaturalDeathExempt(actor)) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) || aptitude < 4) return false;

		float lifespan = actor.stats == null ? 0f : Math.Max(0f, actor.stats["lifespan"]);
		float age = Math.Max(0f, actor.getAge());
		if (lifespan <= 0f || age < lifespan * 0.95f) return false;

		long actorId = GetActorId(actor);
		if (actorId <= 0L || !TryResolveFamilyStableId(actorId, out long familyStableId)) return false;
		if (!XjFamilyLingWuWarehouse.TryConsumeFirstJinXing(familyStableId, out string jinXing)) return false;

		int year = currentYear > 0 ? currentYear : (XjYearTracker.CurrentYear > 0 ? XjYearTracker.CurrentYear : 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanResidualJinXing, jinXing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource,
			FamilyBorrowSourcePrefix + familyStableId + ":" + year);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, 1);
		XjWorldHistoryRegistry.AddActorEvent(
			actor,
			actor.getName() + "寿元将尽，自家族重宝仓库借得一缕金丹遗留金性，以待转世。",
			XjEventIconCatalog.JinDanUpgrade);
		return true;
	}

	internal static bool IsFamilyBorrowSource(string source)
	{
		return !string.IsNullOrWhiteSpace(source)
			&& source.StartsWith(FamilyBorrowSourcePrefix, StringComparison.Ordinal)
			&& source.Length > FamilyBorrowSourcePrefix.Length;
	}

	private static bool TryResolveFamilyStableId(long actorId, out long familyStableId)
	{
		familyStableId = 0L;
		if (actorId <= 0L)
		{
			return false;
		}

		if (XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out familyStableId) && familyStableId > 0L)
		{
			return true;
		}

		if (XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord identity)
			&& identity.Found
			&& identity.RootActorId > 0L)
		{
			familyStableId = identity.RootActorId;
			return true;
		}

		return false;
	}

	private static bool IsQiYuDongTianSource(string source)
	{
		return !string.IsNullOrWhiteSpace(source)
			&& source.StartsWith(QiYuDongTianSourcePrefix, StringComparison.Ordinal)
			&& source.Length > QiYuDongTianSourcePrefix.Length;
	}

	private static void ClearState(Actor actor)
	{
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanResidualJinXing, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, 0);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}
