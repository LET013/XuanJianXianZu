using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.History;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.LingWu;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Death;

internal static class XjDeathPatchBridge
{
	internal static XjDeathSnapshot CaptureBeforeDeath(Actor actor)
	{
		if (actor == null)
		{
			return XjDeathSnapshot.Empty;
		}

		if (XjDeathSnapshotBuilder.TryBuild(actor, out XjDeathSnapshot snapshot))
		{
			return snapshot;
		}

		return XjDeathSnapshot.Empty;
	}

	internal static bool CommitAfterDeath(Actor actor, in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L)
		{
			return false;
		}

		if (XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		XjDeathArchiveQueue.Enqueue(snapshot);
		return true;
	}
}

internal static class XjDeathSnapshotBuilder
{
	internal static bool TryBuild(Actor actor, out XjDeathSnapshot snapshot)
	{
		snapshot = XjDeathSnapshot.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		long familyStableId = 0L;
		if (!XjLongShuSystem.IsExcludedFromInheritance(actor)
			&& !XjFamilyReadModel.Shared.TryGetFamilyStableId(actorId, out familyStableId)
			&& XjFamilyIdentityIndex.TryGetByActorId(actorId, out XjFamilyIdentityRecord record))
		{
			familyStableId = record.RootActorId;
		}

		// 玄鉴归档只服务修士与已确认家族成员。普通世界单位死亡不再
		// 构建整套功法/金丹/法宝/乾坤袋快照。
		bool isCultivator = XjCultivatorCache.IsCultivator(actorId)
			|| XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string knownRealmId)
				&& !string.IsNullOrWhiteSpace(knownRealmId)
			|| XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
				&& aptitude > 0;
		if (!isCultivator && familyStableId <= 0L)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		bool hasCultivationPayload = isCultivator || !string.IsNullOrWhiteSpace(realmId);
		XjGongFaState gongFa = hasCultivationPayload ? XjGongFaAccessor.BuildState(actor) : default;
		XjQiuJinFaState qiuJinFa = hasCultivationPayload ? XjQiuJinFaAccessor.BuildState(actor) : default;
		XjJinDanState jinDan = hasCultivationPayload ? XjJinDanAccessor.BuildState(actor) : default;
		XjShenDanState shenDan = hasCultivationPayload ? XjShenDanAccessor.BuildState(actor) : default;
		XjCaiQiFaState caiQiFa = hasCultivationPayload ? XjCaiQiFaAccessor.BuildState(actor) : default;
		XjFaBaoState faBao = hasCultivationPayload ? XjFaBaoAccessor.BuildState(actor) : default;
		bool isJieLinXian = hasCultivationPayload && XjXuanJianShenTongSpecials.IsJieLinXian(actor);
		string snapshotJinXing = jinDan.Found
			? jinDan.JinXing
			: isJieLinXian && XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanJinXing, out string jieLinJinXing)
				? jieLinJinXing
				: XjJinDanResidualJinXing.TryGetValidGrant(actor, out string residualJinXing)
					? residualJinXing
					: string.Empty;
		string residualJinXingSource = string.Empty;
		if (hasCultivationPayload)
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanResidualJinXingSource, out residualJinXingSource);
		}
		string guoWeiZhongAi = string.Empty;
		if (hasCultivationPayload && XjGuoWeiQuanBingRegistry.TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState liveQuanBing))
		{
			guoWeiZhongAi = liveQuanBing.GuoWeiZhongAi;
		}
		if (hasCultivationPayload && string.IsNullOrWhiteSpace(guoWeiZhongAi))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQuanBingGuoWeiZhongAi, out guoWeiZhongAi);
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, out string deathAnnouncementReason);
		if (string.Equals(deathAnnouncementReason?.Trim(), "YinSiNoDeathArchive", StringComparison.Ordinal)
			|| string.Equals(deathAnnouncementReason?.Trim(), "YinSiMissionEnded", StringComparison.Ordinal)
			|| string.Equals(deathAnnouncementReason?.Trim(), "YinSiMissionCreateFailed", StringComparison.Ordinal))
		{
			return false;
		}
		int jinDanYiXiang = hasCultivationPayload && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int storedYiXiang)
			? storedYiXiang
			: 0;
		// 遗留金性按角色真实金丹道行判定，不计法宝临时加成。
		int jinDanStageIndex = jinDanYiXiang >= 6000 ? 3
			: jinDanYiXiang >= 3000 ? 2
			: jinDanYiXiang >= 1000 ? 1
			: 0;
		int deathRealmTier = hasCultivationPayload ? XjRealmSuppression.GetRealmTier(actor) : XjRealmSuppression.TierNone;
		long lastAttackerId = 0L;
		string lastAttackerName = string.Empty;
		string lastAttackerDaoTu = string.Empty;
		if (hasCultivationPayload || familyStableId > 0L)
		{
			XjCombatTracker.TryGetValidKillerAttribution(
				actor,
				deathRealmTier,
				out lastAttackerId,
				out lastAttackerName,
				out lastAttackerDaoTu);
		}

		snapshot = new XjDeathSnapshot(
			true,
			actorId,
			actor.getName(),
			BuildReincarnationRaceKey(actor),
			familyStableId,
			realmId,
			daoTu,
			gongFa.Found ? gongFa.Name : string.Empty,
			gongFa.Found ? gongFa.Grade : 0,
			0,
			0f,
			qiuJinFa.Found ? qiuJinFa.Name : string.Empty,
			qiuJinFa.Found ? qiuJinFa.SourceGongFaName : string.Empty,
			qiuJinFa.Found ? qiuJinFa.SourceGongFaGrade : 0,
			qiuJinFa.Found ? qiuJinFa.BoundAuthority : string.Empty,
			snapshotJinXing,
			XjJinDanResidualJinXing.IsFamilyBorrowSource(residualJinXingSource)
				? residualJinXingSource
				: jinDan.Found ? string.Empty : residualJinXingSource,
			jinDan.Found ? jinDan.GuoWei : shenDan.Found ? shenDan.GuoWei : string.Empty,
			guoWeiZhongAi,
			jinDanYiXiang,
			jinDanStageIndex,
			isJieLinXian,
			faBao.Found ? faBao.Name : string.Empty,
			faBao.Found ? faBao.Id : string.Empty,
			faBao.Found ? faBao.DaoTu : string.Empty,
			faBao.Found ? faBao.ClassName : string.Empty,
			faBao.Found ? faBao.Source : string.Empty,
			hasCultivationPayload ? XjRenDan.GetSummary(actorId) : string.Empty,
			hasCultivationPayload ? XjGuoWeiQuanBingRegistry.GetSummary(actorId) : string.Empty,
			hasCultivationPayload ? XjDongTianRegistry.GetSummaryForActor(actorId) : string.Empty,
			hasCultivationPayload ? XjQianKunDaiRegistry.GetSummary(actorId) : string.Empty,
			caiQiFa.Found ? caiQiFa.Name : string.Empty,
			caiQiFa.Found ? caiQiFa.DaoTu : string.Empty,
			caiQiFa.Found ? caiQiFa.SourcePlace : string.Empty,
			GetCurrentYear(actor),
			string.IsNullOrWhiteSpace(deathAnnouncementReason) ? "Ok" : deathAnnouncementReason.Trim(),
			lastAttackerId,
			lastAttackerName,
			lastAttackerDaoTu);
		return true;
	}

	private static string BuildReincarnationRaceKey(Actor actor)
	{
		if (actor?.asset == null)
		{
			return string.Empty;
		}

		string assetId = ((Asset)actor.asset).id ?? string.Empty;
		if (string.IsNullOrWhiteSpace(assetId))
		{
			return string.Empty;
		}

		string race = TryGetAssetRaceName(actor);
		if (string.IsNullOrWhiteSpace(race))
		{
			if (assetId.StartsWith("civ_", StringComparison.OrdinalIgnoreCase) && assetId.Length > 4)
			{
				race = assetId.Substring(4);
			}
			else
			{
				int separator = assetId.IndexOf('_');
				race = separator > 0 ? assetId.Substring(0, separator) : assetId;
			}
		}

		return race + "|" + assetId;
	}

	private static string TryGetAssetRaceName(Actor actor)
	{
		try
		{
			return (((object)actor.asset).GetType()
				.GetProperty("race", System.Reflection.BindingFlags.IgnoreCase | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
				?.GetValue(actor.asset))?.ToString() ?? string.Empty;
		}
		catch
		{
			return string.Empty;
		}
	}

	private static int GetCurrentYear(Actor actor)
	{
		int trackedYear = XuanJianVNext.Core.XjYearTracker.CurrentYear;
		if (trackedYear > 0)
		{
			return trackedYear;
		}

		int worldYear = World.world?.map_stats?.year ?? 0;
		if (worldYear > 0)
		{
			return worldYear;
		}

		return actor == null
			? 0
			: (int)System.Math.Floor(System.Math.Max(0f, actor.getAge()));
	}
}

internal static class XjDeathArchiveWriter
{
	internal static void Write(XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L)
		{
			return;
		}

		BroadcastHighRealmDeath(snapshot);
		XjSectWarSystem.ObserveCrossSectHighRealmDeath(snapshot);
		// 先释放死者果位，再结算同道正位承继/外道夺权，避免胜者因
		// 死者仍占用正位而无法完成后续果位迁移。
		XjGuoWeiRegistry.ReleaseFromSnapshot(snapshot);
		XjGuoWeiQuanBingLifecycle.ReleaseFromSnapshot(snapshot);
		XjJieLinXianRegistry.Release(snapshot.ActorId);
		XjReincarnation.RecordFromSnapshot(snapshot);
		XjAlchemyDeathHandler.Handle(snapshot);
		XjLingWuDeathHandler.Handle(snapshot);
		XjJinDanResidualDeathLane.Enqueue(snapshot);
		if (snapshot.FamilyStableId <= 0L)
		{
			WriteLostFaBao(snapshot);
			XjQianKunDaiRegistry.RemoveInventory(snapshot.ActorId);
			return;
		}

		XjFamilyMemberLedger.MarkDead(snapshot);
		XjCenturyAnnalsStore.ObserveDeathSnapshot(snapshot);
		WriteGongFa(snapshot);
		WriteQiuJinFa(snapshot);
		WriteCaiQiFa(snapshot);
		WriteFaBao(snapshot);
		WriteQianKunDai(snapshot);
		XjChronicleWriter.RecordDeathSnapshot(snapshot);
		XjFamilyVendettaRegistry.TryResolveByDeathSnapshot(snapshot);
	}

	private static void BroadcastHighRealmDeath(XjDeathSnapshot snapshot)
	{
		if (!string.Equals(snapshot.ReasonCode, "Ok", StringComparison.Ordinal)
			|| (!XjRealmHelper.IsRealm(snapshot.RealmId, "ZiFu")
				&& !XjRealmHelper.IsRealm(snapshot.RealmId, "JinDan")
				&& !XjRealmHelper.IsRealm(snapshot.RealmId, "ShenDan")))
		{
			return;
		}

		string realm = XjRealmHelper.GetDisplayName(snapshot.RealmId);
		string announcement = XjAnnouncementText.BuildHighRealmDeath(
			snapshot.Name,
			realm,
			snapshot.LastAttackerName);
		XjBroadcastSystem.BroadcastBLevelWorldEvent(announcement, XjEventIconCatalog.HighRealmDeath);
	}

	private static void WriteLostFaBao(XjDeathSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(snapshot.FaBaoId)
			|| string.IsNullOrWhiteSpace(snapshot.FaBao)
			|| string.IsNullOrWhiteSpace(snapshot.FaBaoDaoTu))
		{
			return;
		}

		XjFamilyFaBaoWarehouse.RecordLostFaBao(
			snapshot.ActorId,
			snapshot.Name,
			snapshot.FaBaoId,
			snapshot.FaBao,
			snapshot.FaBaoDaoTu,
			snapshot.FaBaoClass,
			"LostOnDeath",
			snapshot.Year);
	}

	private static void WriteGongFa(XjDeathSnapshot snapshot)
	{
		if (snapshot.GongFaGrade < 1 || string.IsNullOrWhiteSpace(snapshot.GongFaName))
		{
			return;
		}

		XjFamilyGongFaWarehouse.AddGongFaToFamily(
			snapshot.ActorId,
			snapshot.FamilyStableId,
			snapshot.GongFaName,
			snapshot.GongFaGrade,
			snapshot.Year,
			XjFamilyGongFaWarehouse.SourceTypeGongFa,
			snapshot.DaoTu);
	}

	private static void WriteQiuJinFa(XjDeathSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(snapshot.QiuJinFaName))
		{
			return;
		}

		XjFamilyGongFaWarehouse.AddGongFaToFamily(
			snapshot.ActorId,
			snapshot.FamilyStableId,
			snapshot.QiuJinFaName,
			ResolveQiuJinFaSourceGrade(snapshot),
			snapshot.Year,
			XjFamilyGongFaWarehouse.SourceTypeQiuJinFa,
			snapshot.DaoTu,
			snapshot.QiuJinFaSourceGongFaName,
			string.Empty,
			snapshot.QiuJinFaBoundAuthority);
	}

	private static int ResolveQiuJinFaSourceGrade(XjDeathSnapshot snapshot)
	{
		if (snapshot.QiuJinFaSourceGongFaGrade > 0)
		{
			return snapshot.QiuJinFaSourceGongFaGrade;
		}

		return snapshot.GongFaGrade >= 6 ? 5 : System.Math.Max(1, snapshot.GongFaGrade);
	}

	private static void WriteCaiQiFa(XjDeathSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(snapshot.CaiQiFaName) || string.IsNullOrWhiteSpace(snapshot.CaiQiFaDaoTu))
		{
			return;
		}

		XjFamilyCaiQiWarehouse.TryAddCaiQiFa(
			"actor:" + snapshot.FamilyStableId.ToString(System.Globalization.CultureInfo.InvariantCulture),
			snapshot.CaiQiFaName,
			snapshot.CaiQiFaDaoTu,
			snapshot.CaiQiFaSourcePlace,
			snapshot.Year);
	}

	private static void WriteFaBao(XjDeathSnapshot snapshot)
	{
		if (string.IsNullOrWhiteSpace(snapshot.FaBaoId)
			|| string.IsNullOrWhiteSpace(snapshot.FaBao)
			|| string.IsNullOrWhiteSpace(snapshot.FaBaoDaoTu))
		{
			return;
		}

		XjFamilyFaBaoWarehouse.AddFaBaoToFamily(
			snapshot.ActorId,
			snapshot.Name,
			snapshot.FamilyStableId,
			snapshot.FaBaoId,
			snapshot.FaBao,
			snapshot.FaBaoDaoTu,
			snapshot.FaBaoClass,
			XjFamilyFaBaoWarehouse.SourceTypeDeathSnapshot,
			snapshot.Year);
	}

	private static void WriteQianKunDai(XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.FamilyStableId <= 0L || snapshot.ActorId <= 0L)
		{
			return;
		}

		if (XjQianKunDaiRegistry.TryGet(snapshot.ActorId, out XjQianKunDaiState state))
		{
			string familyKey = "actor:" + snapshot.FamilyStableId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			for (int i = 0; i < state.Items.Count; i++)
			{
				XjQianKunDaiItem item = state.Items[i];
				if (item.Count <= 0)
				{
					continue;
				}

				if (string.Equals(item.Category, XjQianKunDaiRegistry.CategoryCaiQi, StringComparison.Ordinal))
				{
					XjFamilyCaiQiWarehouse.TryAdd(familyKey, item.ItemId, item.Count);
				}
				else if (string.Equals(item.Category, XjQianKunDaiRegistry.CategoryGongFa, StringComparison.Ordinal))
				{
					TryWriteQianKunDaiGongFa(snapshot, item);
				}
			}
			XjQianKunDaiRegistry.RemoveInventory(snapshot.ActorId);
		}
	}

	private static void TryWriteQianKunDaiGongFa(XjDeathSnapshot snapshot, in XjQianKunDaiItem item)
	{
		string name = StripTrailingParenthesizedText(item.DisplayName);
		int grade = TryParseGongFaGrade(item.DisplayName, out int parsedGrade) ? parsedGrade : snapshot.GongFaGrade;
		if (string.IsNullOrWhiteSpace(name) || grade <= 0)
		{
			return;
		}

		XjFamilyGongFaWarehouse.AddGongFaToFamily(
			snapshot.ActorId,
			snapshot.FamilyStableId,
			name,
			grade,
			snapshot.Year,
			XjFamilyGongFaWarehouse.SourceTypeGongFa,
			string.IsNullOrWhiteSpace(item.DaoTu) ? snapshot.DaoTu : item.DaoTu);
	}

	private static string StripTrailingParenthesizedText(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return string.Empty;
		}

		string text = value.Trim();
		int index = text.LastIndexOf('（');
		if (index > 0 && text.EndsWith("）", StringComparison.Ordinal))
		{
			return text.Substring(0, index).Trim();
		}

		return text;
	}

	private static bool TryParseGongFaGrade(string displayName, out int grade)
	{
		grade = 0;
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return false;
		}

		if (displayName.Contains("一品")) grade = 1;
		else if (displayName.Contains("二品")) grade = 2;
		else if (displayName.Contains("三品")) grade = 3;
		else if (displayName.Contains("四品")) grade = 4;
			else if (displayName.Contains("六品")) grade = 6;
			else if (displayName.Contains("五品")) grade = 5;
		return grade > 0;
	}
}

internal static class XjDeathArchiveQueue
{
	internal static bool HasPending => pendingSnapshots.Count > 0;
	private const int MaxRecentProcessedKeys = 4096;
	private static readonly Queue<XjDeathSnapshot> pendingSnapshots = new Queue<XjDeathSnapshot>();
	private static readonly HashSet<string> pendingKeys = new HashSet<string>();
	private static readonly Queue<string> recentProcessedKeyOrder = new Queue<string>();
	private static readonly HashSet<string> recentProcessedKeys = new HashSet<string>();

	internal static void Enqueue(XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L)
		{
			return;
		}

		string key = BuildKey(snapshot);
		if (pendingKeys.Contains(key) || recentProcessedKeys.Contains(key) || !pendingKeys.Add(key))
		{
			return;
		}

		pendingSnapshots.Enqueue(snapshot);
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		int processed = 0;
		while (processed < budget && pendingSnapshots.Count > 0)
		{
			XjDeathSnapshot snapshot = pendingSnapshots.Dequeue();
			string key = BuildKey(snapshot);
			pendingKeys.Remove(key);
			try
			{
				XjDeathArchiveWriter.Write(snapshot);
				RememberProcessed(key);
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogWarning("[玄鉴][死亡归档] 写入失败: " + ex.GetType().Name + ": " + ex.Message);
			}
			processed++;
		}
	}

	internal static void Clear()
	{
		pendingSnapshots.Clear();
		pendingKeys.Clear();
		recentProcessedKeyOrder.Clear();
		recentProcessedKeys.Clear();
	}

	private static void RememberProcessed(string key)
	{
		if (string.IsNullOrEmpty(key) || !recentProcessedKeys.Add(key))
		{
			return;
		}

		recentProcessedKeyOrder.Enqueue(key);
		while (recentProcessedKeyOrder.Count > MaxRecentProcessedKeys)
		{
			string expired = recentProcessedKeyOrder.Dequeue();
			recentProcessedKeys.Remove(expired);
		}
	}

	private static string BuildKey(XjDeathSnapshot snapshot)
	{
		return snapshot.ActorId
			+ "|"
			+ snapshot.Year
			+ "|"
			+ snapshot.RealmId;
	}
}
