using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjDaoTaiPresenceStatus
{
	internal const string ClosedCultivation = "闭关不出";
	internal const string BeyondWorld = "远遁天外";
	internal const string LegacyBeyondWorld = "天外行走";
	internal const string Traveling = "游历";
	internal const string Manifested = "返世显化";
}

internal sealed class XjDaoTaiPresenceWorldArchiveData
{
	public List<XjDaoTaiPresenceArchiveRecord> Records { get; set; } = new List<XjDaoTaiPresenceArchiveRecord>();
}

internal sealed class XjDaoTaiPresenceArchiveRecord
{
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string RealmId { get; set; } = string.Empty;
	public string GuoWei { get; set; } = string.Empty;
	public string PositionKind { get; set; } = string.Empty;
	public string Status { get; set; } = XjDaoTaiPresenceStatus.ClosedCultivation;
	public int AscendedYear { get; set; }
	public int LastStatusYear { get; set; }
	public int NextReturnYear { get; set; }
	public bool BodyArchived { get; set; }
	// 远遁天外时原生收藏星必须一并从尘世UI消失，但返世后要恢复玩家原先的收藏选择。
	public bool FavoriteWasSetBeforeBeyondWorld { get; set; }
	public bool FavoriteSuppressedByBeyondWorld { get; set; }
}

internal static class XjDaoTaiPresenceArchive
{
	private static readonly Dictionary<long, XjDaoTaiPresenceArchiveRecord> RecordsByActorId =
		new Dictionary<long, XjDaoTaiPresenceArchiveRecord>();
	// “远遁天外”与金丹闭关的根本区别：角色仍保留持久身份与档案引用，
	// 但尘世中的 Unity 表现体会被真正关闭，返世时再恢复。只记录本系统
	// 实际隐藏过的角色，避免误把建筑闭关/原生睡眠等其它隐藏状态强行打开。
	private static readonly HashSet<long> BeyondWorldHiddenActorIds = new HashSet<long>();
	private static readonly List<XjDaoTaiPresenceArchiveRecord> WorldTickBuffer = new List<XjDaoTaiPresenceArchiveRecord>(16);
	private static int lastWorldPresenceTickYear;

	internal static int Count => RecordsByActorId.Count;

	internal static bool IsRegisteredDaoTai(long actorId)
	{
		return actorId > 0L && RecordsByActorId.ContainsKey(actorId);
	}

	internal static void ObserveLiveDaoTai(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int safeYear = Math.Max(0, currentYear);

		if (RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord existing) && existing != null)
		{
			existing.ActorName = SafeActorName(actor);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string existingDaoTu);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string existingRealm);
			existing.DaoTu = Clean(existingDaoTu);
			existing.RealmId = XjRealmHelper.NormalizeId(existingRealm);
			RefreshConfiguredReturnYear(existing);
			if (existing.BodyArchived)
			{
				if (existing.NextReturnYear > 0 && safeYear >= existing.NextReturnYear)
				{
					ApplyPresenceState(existing, XjDaoTaiPresenceStatus.Manifested, safeYear, false,
						existing.ActorName + "返世显化");
				}
				else
				{
					ApplyRuntimeArchiveState(actor, existing);
				}
			}
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string guoWei = XjDaoTaiMeritSystem.ResolveGuoWei(actor);
		XjDaoTaiMeritSystem.TryResolveDaoTaiPositionKind(guoWei, out string positionKind);
		RecordAscension(actor, daoTu, realmId, guoWei, positionKind, safeYear);
	}

	internal static void RecordAscension(
		Actor actor,
		string daoTu,
		string realmId,
		string guoWei,
		string positionKind,
		int currentYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int safeYear = Math.Max(0, currentYear);
		string status = XjXianGuoSystem.IsDiMingYang(actor)
			? XjDaoTaiPresenceStatus.Manifested
			: ResolveInitialStatus(actorId, safeYear);
		XjDaoTaiPresenceArchiveRecord record = new XjDaoTaiPresenceArchiveRecord
		{
			ActorId = actorId,
			ActorName = SafeActorName(actor),
			DaoTu = Clean(daoTu),
			RealmId = XjRealmHelper.NormalizeId(realmId),
			GuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei),
			PositionKind = Clean(positionKind),
			Status = status,
			AscendedYear = safeYear,
			LastStatusYear = safeYear,
			NextReturnYear = ResolveNextReturnYear(actorId, safeYear, status),
			BodyArchived = !string.Equals(status, XjDaoTaiPresenceStatus.Manifested, StringComparison.Ordinal)
		};
		RecordsByActorId[actorId] = record;
		ApplyRuntimeArchiveState(actor, record);
		XjWorldArchiveSystem.MarkChanged();
		XjFamilyRealmAchievement family = XjFamilyRealmAchievementNarrative.Resolve(actor, realmId);
		string ascensionBody = XjChronology.FormatYear(safeYear) + "，" + record.ActorName + "持"
			+ (string.IsNullOrWhiteSpace(record.GuoWei) ? "果位" : record.GuoWei)
			+ "而成道胎，现状为" + status
			+ XjFamilyRealmAchievementNarrative.BuildEnding(in family, "道胎");
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			XjFamilyRealmAchievementNarrative.BuildHistoryAction(in family, "道胎"),
			ascensionBody,
			5,
			true,
			actorId: actorId,
			actorName: record.ActorName,
			year: safeYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiAscended");
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			ascensionBody, XjAnnouncementCategory.HighRealm, duration: 11f, color: "#E4BE72", delayFrames: 1, iconId: XjEventIconCatalog.JinDanUpgrade);
	}

	internal static bool TickPresence(Actor actor, int currentYear)
	{
		if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return true;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return true;
		ObserveLiveDaoTai(actor, currentYear);
		if (!RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord record) || record == null) return true;

		int safeYear = Math.Max(0, currentYear);
		if (XjXianGuoSystem.IsDiMingYang(actor))
		{
			if (record.BodyArchived || !string.Equals(record.Status, XjDaoTaiPresenceStatus.Manifested, StringComparison.Ordinal))
			{
				ApplyPresenceState(record, XjDaoTaiPresenceStatus.Manifested, safeYear, false, record.ActorName + "帝临不离国");
			}
			record.NextReturnYear = 0;
			record.BodyArchived = false;
			ApplyRuntimeArchiveState(actor, record);
			return true;
		}
		record.ActorName = SafeActorName(actor);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		record.DaoTu = Clean(daoTu);
		record.RealmId = XjRealmHelper.NormalizeId(realmId);
		RefreshConfiguredReturnYear(record);
		if (record.NextReturnYear <= 0) record.NextReturnYear = ResolveNextReturnYear(actorId, safeYear, record.Status);

		if (record.BodyArchived)
		{
			ApplyRuntimeArchiveState(actor, record);
			if (safeYear < record.NextReturnYear) return false;
			ApplyPresenceState(record, XjDaoTaiPresenceStatus.Manifested, safeYear, false,
				record.ActorName + "返世显化");
			return true;
		}

		if (safeYear < record.NextReturnYear) return true;
		string nextStatus = ResolveDepartureStatus(actorId, safeYear);
		ApplyPresenceState(record, nextStatus, safeYear, true, record.ActorName + nextStatus);
		ApplyRuntimeArchiveState(actor, record);
		return false;
	}


	internal static void TickWorld(int currentYear)
	{
		int safeYear = Math.Max(0, currentYear);
		if (safeYear <= 0 || lastWorldPresenceTickYear == safeYear) return;
		lastWorldPresenceTickYear = safeYear;
		if (RecordsByActorId.Count == 0) return;

		// 远遁/游历中的道胎会被年度角色调度主动跳过，因此“返世”不能再依赖本人年度 Tick。
		// 世界年度入口只扫道胎名册（高境小集合），到期即恢复显化，避免永远卡在天外。
		WorldTickBuffer.Clear();
		foreach (XjDaoTaiPresenceArchiveRecord value in RecordsByActorId.Values) WorldTickBuffer.Add(value);
		for (int i = 0; i < WorldTickBuffer.Count; i++)
		{
			XjDaoTaiPresenceArchiveRecord record = WorldTickBuffer[i];
			if (record == null || record.ActorId <= 0L || !record.BodyArchived) continue;
			RefreshConfiguredReturnYear(record);
			if (!XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor actor)
				|| !XjSafeCore.IsAliveActor(actor)
				|| !XjDaoTaiSpellScale.IsDaoTaiActor(actor))
			{
				continue;
			}

			record.ActorName = SafeActorName(actor);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
			record.DaoTu = Clean(daoTu);
			record.RealmId = XjRealmHelper.NormalizeId(realmId);
			if (record.NextReturnYear > 0 && safeYear >= record.NextReturnYear)
			{
				ApplyPresenceState(record, XjDaoTaiPresenceStatus.Manifested, safeYear, false,
					record.ActorName + "返世显化");
			}
			else
			{
				ApplyRuntimeArchiveState(actor, record);
			}
		}
	}

	/// <summary>
	/// 第三方换身不应让道胎名册同时保留旧/新 actorId。目标仍是道胎时迁移当前
	/// 在世/天外状态；若目标已被物种门禁清掉修炼身份，则撤销旧的当前名册记录。
	/// </summary>
	internal static bool ForgetUnavailableActor(long actorId)
	{
		if (actorId <= 0L) return false;
		BeyondWorldHiddenActorIds.Remove(actorId);
		if (!RecordsByActorId.Remove(actorId)) return false;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool RebindActorAfterExternalReplacement(long oldActorId, Actor target, int currentYear)
	{
		if (oldActorId <= 0L || target?.data == null || !RecordsByActorId.TryGetValue(oldActorId, out XjDaoTaiPresenceArchiveRecord record) || record == null)
			return false;
		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId) return false;

		bool wasHidden = BeyondWorldHiddenActorIds.Remove(oldActorId);
		RecordsByActorId.Remove(oldActorId);
		if (!XjDaoTaiSpellScale.IsDaoTaiActor(target))
		{
			XjWorldArchiveSystem.MarkChanged();
			return true;
		}

		record.ActorId = newActorId;
		record.ActorName = SafeActorName(target);
		XjActorAccessor.TryGetString(target, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetString(target, XjActorDataKeys.RealmId, out string realmId);
		record.DaoTu = Clean(daoTu);
		record.RealmId = XjRealmHelper.NormalizeId(realmId);
		record.LastStatusYear = Math.Max(record.LastStatusYear, Math.Max(0, currentYear));
		RecordsByActorId[newActorId] = record;
		if (wasHidden) BeyondWorldHiddenActorIds.Add(newActorId);
		ApplyRuntimeArchiveState(target, record);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool IsBodyArchived(long actorId)
	{
		return actorId > 0L
			&& RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord record)
			&& record != null
			&& record.BodyArchived;
	}

	internal static bool IsBeyondWorld(long actorId)
	{
		return actorId > 0L
			&& RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord record)
			&& record != null
			&& record.BodyArchived
			&& string.Equals(record.Status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal);
	}

	internal static bool TryGetRecord(long actorId, out XjDaoTaiPresenceArchiveRecord record)
	{
		record = null;
		if (actorId <= 0L || !RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord existing))
		{
			return false;
		}
		record = Clone(existing);
		return record != null;
	}

	internal static bool SetBeyondWorld(Actor actor, int currentYear)
	{
		if (XjXianGuoSystem.IsDiMingYang(actor)) return false;
		return SetTravelState(actor, currentYear, XjDaoTaiPresenceStatus.BeyondWorld);
	}

	internal static bool SetTraveling(Actor actor, int currentYear)
	{
		if (XjXianGuoSystem.IsDiMingYang(actor)) return false;
		return SetTravelState(actor, currentYear, XjDaoTaiPresenceStatus.Traveling);
	}

	internal static XjDaoTaiPresenceWorldArchiveData ExportState()
	{
		XjDaoTaiPresenceWorldArchiveData data = new XjDaoTaiPresenceWorldArchiveData();
		foreach (XjDaoTaiPresenceArchiveRecord record in RecordsByActorId.Values)
		{
			if (record != null) data.Records.Add(Clone(record));
		}
		data.Records.Sort((left, right) => right.AscendedYear.CompareTo(left.AscendedYear));
		return data;
	}

	internal static void ImportState(XjDaoTaiPresenceWorldArchiveData data)
	{
		RestoreAllBeyondWorldVisuals();
		RecordsByActorId.Clear();
		lastWorldPresenceTickYear = 0;
		if (data?.Records == null) return;
		for (int i = 0; i < data.Records.Count; i++)
		{
			XjDaoTaiPresenceArchiveRecord record = Normalize(data.Records[i]);
			if (record != null && record.ActorId > 0L)
			{
				RecordsByActorId[record.ActorId] = record;
			}
		}
	}

	internal static void Clear()
	{
		RestoreAllBeyondWorldVisuals();
		RecordsByActorId.Clear();
		lastWorldPresenceTickYear = 0;
	}

	private static string ResolveInitialStatus(long actorId, int year)
	{
		int roll = XjDeterministicHash.PositiveIndex(actorId + year, "daotai_presence_status", 100);
		if (roll < 50) return XjDaoTaiPresenceStatus.ClosedCultivation;
		if (XjRuntimeSettings.DaoTaiBeyondWorldEnabled)
		{
			if (roll < 82) return XjDaoTaiPresenceStatus.BeyondWorld;
		}
		if (XjRuntimeSettings.DaoTaiTravelEnabled)
		{
			int travelLimit = XjRuntimeSettings.DaoTaiBeyondWorldEnabled ? 94 : 82;
			if (roll < travelLimit) return XjDaoTaiPresenceStatus.Traveling;
		}
		return XjDaoTaiPresenceStatus.Manifested;
	}

	private static int ResolveNextReturnYear(long actorId, int year, string status)
	{
		int safeYear = Math.Max(0, year);
		if (string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal))
		{
			return safeYear + XjRuntimeSettings.DaoTaiBeyondWorldYears;
		}
		if (string.Equals(status, XjDaoTaiPresenceStatus.Traveling, StringComparison.Ordinal))
		{
			return safeYear + XjRuntimeSettings.DaoTaiTravelYears;
		}
		if (string.Equals(status, XjDaoTaiPresenceStatus.Manifested, StringComparison.Ordinal))
		{
			return safeYear + 12 + XjDeterministicHash.PositiveIndex(actorId + safeYear, "daotai_manifest_duration", 19);
		}
		return safeYear + 80 + XjDeterministicHash.PositiveIndex(actorId + safeYear, "daotai_closed_return_year", 121);
	}

	private static void RefreshConfiguredReturnYear(XjDaoTaiPresenceArchiveRecord record)
	{
		if (record == null || record.LastStatusYear < 0) return;
		int expected;
		if (string.Equals(record.Status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal))
		{
			expected = Math.Max(0, record.LastStatusYear) + XjRuntimeSettings.DaoTaiBeyondWorldYears;
		}
		else if (string.Equals(record.Status, XjDaoTaiPresenceStatus.Traveling, StringComparison.Ordinal))
		{
			expected = Math.Max(0, record.LastStatusYear) + XjRuntimeSettings.DaoTaiTravelYears;
		}
		else
		{
			return;
		}
		if (record.NextReturnYear == expected) return;
		record.NextReturnYear = expected;
		XjWorldArchiveSystem.MarkChanged();
	}

	private static string ResolveDepartureStatus(long actorId, int year)
	{
		int roll = XjDeterministicHash.PositiveIndex(actorId + year, "daotai_departure_status", 100);
		if (roll < 45) return XjDaoTaiPresenceStatus.ClosedCultivation;
		if (XjRuntimeSettings.DaoTaiBeyondWorldEnabled && roll < 80) return XjDaoTaiPresenceStatus.BeyondWorld;
		if (XjRuntimeSettings.DaoTaiTravelEnabled) return XjDaoTaiPresenceStatus.Traveling;
		return XjDaoTaiPresenceStatus.ClosedCultivation;
	}

	private static void ApplyPresenceState(
		XjDaoTaiPresenceArchiveRecord record,
		string status,
		int currentYear,
		bool bodyArchived,
		string title)
	{
		if (record == null) return;
		string previous = record.Status;
		record.Status = status;
		record.LastStatusYear = Math.Max(0, currentYear);
		record.NextReturnYear = ResolveNextReturnYear(record.ActorId, record.LastStatusYear, status);
		record.BodyArchived = bodyArchived;
		if (XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor liveActor)
			&& XjSafeCore.IsAliveActor(liveActor))
		{
			ApplyRuntimeArchiveState(liveActor, record);
		}
		XjWorldArchiveSystem.MarkChanged();
		bool beyondWorld = bodyArchived && string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal);
		string body = XjChronology.FormatYear(record.LastStatusYear) + "，" + record.ActorName + "道胎行迹由"
			+ previous + "转为" + status
			+ (beyondWorld
				? "，真身自尘世隐去，预定" + XjChronology.FormatYear(record.NextReturnYear) + "返世显化。"
				: bodyArchived
					? "，预定" + XjChronology.FormatYear(record.NextReturnYear) + "返世显化。"
					: "，本次显化预计延续至" + XjChronology.FormatYear(record.NextReturnYear) + "。");
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			string.IsNullOrWhiteSpace(title) ? record.ActorName + status : title,
			body,
			4,
			true,
			actorId: record.ActorId,
			actorName: record.ActorName,
			year: record.LastStatusYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiPresenceChanged");
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			body, XjAnnouncementCategory.HighRealm, duration: 8f, color: "#B7A7FF", delayFrames: 1, iconId: XjEventIconCatalog.JinDanUpgrade);
	}

	private static XjDaoTaiPresenceArchiveRecord Normalize(XjDaoTaiPresenceArchiveRecord source)
	{
		if (source == null) return null;
		string status = Clean(source.Status);
		if (string.Equals(status, XjDaoTaiPresenceStatus.LegacyBeyondWorld, StringComparison.Ordinal))
		{
			status = XjDaoTaiPresenceStatus.BeyondWorld;
		}
		if (!string.Equals(status, XjDaoTaiPresenceStatus.ClosedCultivation, StringComparison.Ordinal)
			&& !string.Equals(status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal)
			&& !string.Equals(status, XjDaoTaiPresenceStatus.Traveling, StringComparison.Ordinal)
			&& !string.Equals(status, XjDaoTaiPresenceStatus.Manifested, StringComparison.Ordinal))
		{
			status = XjDaoTaiPresenceStatus.ClosedCultivation;
		}
		int lastStatusYear = Math.Max(0, source.LastStatusYear);
		if (lastStatusYear <= 0) lastStatusYear = Math.Max(0, source.AscendedYear);
		int nextReturnYear = Math.Max(0, source.NextReturnYear);
		if (nextReturnYear <= 0 && source.ActorId > 0L)
		{
			nextReturnYear = ResolveNextReturnYear(source.ActorId, lastStatusYear, status);
		}
		return new XjDaoTaiPresenceArchiveRecord
		{
			ActorId = source.ActorId,
			ActorName = Clean(source.ActorName),
			DaoTu = Clean(source.DaoTu),
			RealmId = XjRealmHelper.NormalizeId(source.RealmId),
			GuoWei = XjGuoWeiCalculator.NormalizeGuoWeiName(source.GuoWei),
			PositionKind = Clean(source.PositionKind),
			Status = status,
			AscendedYear = Math.Max(0, source.AscendedYear),
			LastStatusYear = lastStatusYear,
			NextReturnYear = nextReturnYear,
			BodyArchived = !string.Equals(status, XjDaoTaiPresenceStatus.Manifested, StringComparison.Ordinal),
			FavoriteWasSetBeforeBeyondWorld = source.FavoriteWasSetBeforeBeyondWorld,
			FavoriteSuppressedByBeyondWorld = source.FavoriteSuppressedByBeyondWorld
		};
	}

	private static XjDaoTaiPresenceArchiveRecord Clone(XjDaoTaiPresenceArchiveRecord source)
	{
		return Normalize(source);
	}

	private static string SafeActorName(Actor actor)
	{
		return XjDisplayNameSanitizer.Clean(actor?.getName() ?? string.Empty, "无名道胎");
	}

	private static bool SetTravelState(Actor actor, int currentYear, string status)
	{
		if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor) || XjXianGuoSystem.IsDiMingYang(actor)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		if (!RecordsByActorId.TryGetValue(actorId, out XjDaoTaiPresenceArchiveRecord record) || record == null)
		{
			ObserveLiveDaoTai(actor, currentYear);
			RecordsByActorId.TryGetValue(actorId, out record);
		}
		if (record == null) return false;
		int safeYear = Math.Max(0, currentYear);
		record.ActorName = SafeActorName(actor);
		record.Status = status;
		record.LastStatusYear = safeYear;
		record.NextReturnYear = ResolveNextReturnYear(actorId, safeYear, status);
		record.BodyArchived = true;
		ApplyRuntimeArchiveState(actor, record);
		XjWorldArchiveSystem.MarkChanged();
		string presenceBody = XjChronology.FormatYear(safeYear) + "，" + record.ActorName + "道胎行迹转为" + status
			+ "，预定" + XjChronology.FormatYear(record.NextReturnYear) + "返世显化。";
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			record.ActorName + status,
			presenceBody,
			4,
			true,
			actorId: actorId,
			actorName: record.ActorName,
			year: safeYear,
			iconIdOverride: XjEventIconCatalog.JinDanUpgrade,
			eventType: "DaoTaiPresenceChanged");
		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			presenceBody, XjAnnouncementCategory.HighRealm, duration: 8f, color: "#B7A7FF", delayFrames: 1, iconId: XjEventIconCatalog.JinDanUpgrade);
		return true;
	}

	private static void ApplyRuntimeArchiveState(Actor actor, XjDaoTaiPresenceArchiveRecord record)
	{
		if (actor?.data == null || record == null) return;
		bool beyondWorld = record.BodyArchived
			&& string.Equals(record.Status, XjDaoTaiPresenceStatus.BeyondWorld, StringComparison.Ordinal);
		ApplyBeyondWorldFavoriteState(actor, record, beyondWorld);
		ApplyBeyondWorldVisualState(actor, beyondWorld);
		if (!record.BodyArchived) return;

		// 所有离世状态都退出尘世行为；但只有“远遁天外”关闭尘世表现体。
		// 因此闭关仍是洞天/建筑中的真实角色，游历仍保留尘世形体，
		// 而远遁是真正从地图上隐去，二者不再共用同一种视觉实现。
		try { actor.cancelAllBeh(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiPresenceArchive.cs:runtime_cancel", ex); }
		XjActorAggroBridge.ClearTargets(actor);
		try { actor.stopMovement(); }
		catch (Exception ex) { XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiPresenceArchive.cs:runtime_stop", ex); }
	}

	private static void ApplyBeyondWorldFavoriteState(Actor actor, XjDaoTaiPresenceArchiveRecord record, bool hidden)
	{
		if (actor?.data == null || record == null) return;
		try
		{
			BaseSystemData data = (BaseSystemData)actor.data;
			if (hidden)
			{
				// 第一次真正离世时记住玩家收藏状态。后续年度维持隐藏时绝不覆盖这份记忆。
				if (!record.FavoriteSuppressedByBeyondWorld)
				{
					record.FavoriteWasSetBeforeBeyondWorld = data.favorite;
					record.FavoriteSuppressedByBeyondWorld = true;
					XjWorldArchiveSystem.MarkChanged();
				}
				if (data.favorite) data.favorite = false;
				return;
			}

			if (!record.FavoriteSuppressedByBeyondWorld) return;
			data.favorite = record.FavoriteWasSetBeforeBeyondWorld;
			record.FavoriteWasSetBeforeBeyondWorld = false;
			record.FavoriteSuppressedByBeyondWorld = false;
			XjWorldArchiveSystem.MarkChanged();
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/HighRealm/XjDaoTaiPresenceArchive.cs:beyond_world_favorite", ex);
		}
	}

	private static void ApplyBeyondWorldVisualState(Actor actor, bool hidden)
	{
		if (actor?.data == null) return;
		long actorId;
		try { actorId = ((BaseSystemData)actor.data).id; }
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjDaoTaiPresenceArchive.ResolveActorId", ex);
			return;
		}
		if (actorId <= 0L) return;

		if (hidden) BeyondWorldHiddenActorIds.Add(actorId);
		else if (!BeyondWorldHiddenActorIds.Remove(actorId)) return;

		// 只切换原生表现层，不调用 removeFromWorld/kill，避免破坏人物编号、家族、宗门与果位引用。
		// 版本差异统一封装在 WorldBox interop，领域层不再自行反射原生对象。
		XjActorPresentationInterop.SetVisible(actor, !hidden);
	}

	private static void RestoreAllBeyondWorldVisuals()
	{
		if (BeyondWorldHiddenActorIds.Count == 0) return;
		long[] actorIds = new long[BeyondWorldHiddenActorIds.Count];
		BeyondWorldHiddenActorIds.CopyTo(actorIds);
		for (int i = 0; i < actorIds.Length; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(actorIds[i], out Actor actor) && actor?.data != null)
			{
				if (RecordsByActorId.TryGetValue(actorIds[i], out XjDaoTaiPresenceArchiveRecord record) && record != null)
					ApplyBeyondWorldFavoriteState(actor, record, false);
				ApplyBeyondWorldVisualState(actor, false);
			}
		}
		BeyondWorldHiddenActorIds.Clear();
		WorldTickBuffer.Clear();
	}

	private static string Clean(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
