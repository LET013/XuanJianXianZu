using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

internal static class XjShiFruitLockStateIds
{
	internal const string LivingBound = "living_bound";
	internal const string PermanentlySealed = "permanently_sealed";
	internal const string StolenContaminated = "stolen_contaminated";

	internal static bool IsKnown(string value)
	{
		return string.Equals(value, LivingBound, StringComparison.Ordinal)
			|| string.Equals(value, PermanentlySealed, StringComparison.Ordinal)
			|| string.Equals(value, StolenContaminated, StringComparison.Ordinal);
	}
}

internal sealed class XjShiFruitPositionLockArchiveData
{
	public List<XjShiFruitPositionLockRecord> Records { get; set; } = new List<XjShiFruitPositionLockRecord>();
}

internal sealed class XjShiFruitPositionLockRecord
{
	public string PositionId { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string PositionType { get; set; } = string.Empty;
	public long SourceActorId { get; set; }
	public string SourceActorName { get; set; } = string.Empty;
	public string ShiDomainId { get; set; } = string.Empty;
	public int BoundYear { get; set; }
	public int SealedYear { get; set; }
	public string State { get; set; } = XjShiFruitLockStateIds.LivingBound;
	public int StolenYear { get; set; }
	public long StolenByActorId { get; set; }
	public string StolenByActorName { get; set; } = string.Empty;
	public string ShiIntentionId { get; set; } = string.Empty;

	internal XjShiFruitPositionLockRecord Clone()
	{
		return new XjShiFruitPositionLockRecord
		{
			PositionId = PositionId,
			DaoTu = DaoTu,
			PositionType = PositionType,
			SourceActorId = SourceActorId,
			SourceActorName = SourceActorName,
			ShiDomainId = ShiDomainId,
			BoundYear = BoundYear,
			SealedYear = SealedYear,
			State = State,
			StolenYear = StolenYear,
			StolenByActorId = StolenByActorId,
			StolenByActorName = StolenByActorName,
			ShiIntentionId = ShiIntentionId
		};
	}
}

internal readonly struct XjShiFruitPositionConversionCapture
{
	internal readonly bool Found;
	internal readonly string PositionId;
	internal readonly string DaoTu;
	internal readonly string PositionType;
	internal readonly long SourceActorId;
	internal readonly string SourceActorName;

	internal XjShiFruitPositionConversionCapture(
		bool found,
		string positionId,
		string daoTu,
		string positionType,
		long sourceActorId,
		string sourceActorName)
	{
		Found = found;
		PositionId = positionId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		PositionType = positionType ?? string.Empty;
		SourceActorId = Math.Max(0L, sourceActorId);
		SourceActorName = sourceActorName ?? string.Empty;
	}
}

/// <summary>
/// 真君投释后，原果位／余位／闰位不再回到现世空位池。投释存续期间先随真灵
/// 绑定释土；该释修第一次确认死亡后永久封于释土。只有身在旃檀林、道慧极高的
/// 非释修于求位时才可能极低概率盗出。盗出的位永久沾染七相释意，承继与修持均更难。
/// </summary>
internal static class XjShiFruitPositionLockSystem
{
	private const int TheftMinimumDaoHui = 95;
	private const int TheftChanceBasisPoints = 15;
	private const float ContaminatedCultivationMultiplier = 0.55f;
	private const int ContaminatedDerivedPositionPenalty = 20;
	private const int ContaminatedZhengWeiThreshold = 95;

	private static readonly Dictionary<string, XjShiFruitPositionLockRecord> RecordsByPosition =
		new Dictionary<string, XjShiFruitPositionLockRecord>(StringComparer.Ordinal);

	private static readonly string[] ModernShiIntentions =
	{
		XjShiLineageIds.GreatDesire,
		XjShiLineageIds.Wrath,
		XjShiLineageIds.DharmaAdmiration,
		XjShiLineageIds.Discipline,
		XjShiLineageIds.GoodJoy,
		XjShiLineageIds.Compassion,
		XjShiLineageIds.Emptiness
	};

	internal static void Clear()
	{
		RecordsByPosition.Clear();
	}

	internal static XjShiFruitPositionLockArchiveData ExportState()
	{
		XjShiFruitPositionLockArchiveData archive = new XjShiFruitPositionLockArchiveData();
		List<string> ids = new List<string>(RecordsByPosition.Keys);
		ids.Sort(StringComparer.Ordinal);
		for (int i = 0; i < ids.Count; i++)
		{
			archive.Records.Add(RecordsByPosition[ids[i]].Clone());
		}
		return archive;
	}

	internal static void ImportState(XjShiFruitPositionLockArchiveData archive)
	{
		RecordsByPosition.Clear();
		if (archive?.Records == null) return;
		for (int i = 0; i < archive.Records.Count; i++)
		{
			XjShiFruitPositionLockRecord record = Normalize(archive.Records[i]);
			if (record == null || RecordsByPosition.ContainsKey(record.PositionId)) continue;
			RecordsByPosition[record.PositionId] = record;
		}
	}

	internal static XjShiFruitPositionConversionCapture CaptureBeforeConversion(Actor actor)
	{
		if (actor?.data == null) return default;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return default;

		XjJinDanState state = XjJinDanAccessor.BuildStateWithoutDaoMigration(actor);
		XjGuoWeiRegistry.TryGetHistoricalEntry(actorId, out XjGuoWeiRegistryEntry history);
		string currentPosition = state.Found && !string.IsNullOrWhiteSpace(state.GuoWei)
			? state.GuoWei
			: history.Found ? history.GuoWei : string.Empty;
		string positionId = XjGuoWeiCalculator.NormalizeGuoWeiName(currentPosition);
		if (string.IsNullOrWhiteSpace(positionId)) return default;

		string positionType = XjGuoWeiRegistry.ResolveTypeFromName(positionId);
		if (string.IsNullOrWhiteSpace(positionType)) return default;
		string daoTu = history.Found ? history.DaoTu : string.Empty;
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out daoTu);
		}
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			daoTu = XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(positionId);
		}
		return new XjShiFruitPositionConversionCapture(
			true,
			positionId,
			NormalizeText(daoTu),
			XjGuoWeiCalculator.NormalizePositionType(positionType),
			actorId,
			actor.getName());
	}

	internal static void CommitConversionBinding(
		Actor actor,
		in XjShiFruitPositionConversionCapture capture,
		int annualYear)
	{
		if (!capture.Found || string.IsNullOrWhiteSpace(capture.PositionId)) return;
		string positionId = XjGuoWeiCalculator.NormalizeGuoWeiName(capture.PositionId);
		if (string.IsNullOrWhiteSpace(positionId)) return;
		RecordsByPosition.TryGetValue(positionId, out XjShiFruitPositionLockRecord existing);

		int year = Math.Max(1, annualYear);
		string domainId = ResolveActorDomain(actor);
		XjShiFruitPositionLockRecord record = existing ?? new XjShiFruitPositionLockRecord();
		record.PositionId = positionId;
		record.DaoTu = NormalizeText(capture.DaoTu);
		record.PositionType = XjGuoWeiCalculator.NormalizePositionType(capture.PositionType);
		record.SourceActorId = capture.SourceActorId;
		record.SourceActorName = capture.SourceActorName;
		record.ShiDomainId = domainId;
		record.BoundYear = record.BoundYear > 0 ? record.BoundYear : year;
		record.SealedYear = 0;
		record.State = XjShiFruitLockStateIds.LivingBound;
		record.StolenYear = 0;
		record.StolenByActorId = 0L;
		record.StolenByActorName = string.Empty;
		RecordsByPosition[positionId] = record;
		MarkChanged();

		if (actor?.data != null)
		{
			string domainDisplay = ResolveDomainDisplay(domainId);
			XjWorldHistoryStore.RecordActorEvent(actor,
				"投释之际，原持" + XjGuoWeiCalculator.GetDisplayGuoWeiName(positionId)
				+ "随真灵沉入" + domainDisplay + "，现世无人可直接承证；此身一死，该位即永封释土。",
				XjEventIconCatalog.HistoryWorld);
		}
	}

	internal static void OnShiConfirmedDeath(Actor actor, long sourceActorId, string domainId, int annualYear)
	{
		if (sourceActorId <= 0L) return;
		int year = Math.Max(1, annualYear);
		bool changed = false;
		bool newlySealed = false;
		foreach (XjShiFruitPositionLockRecord record in RecordsByPosition.Values)
		{
			if (record == null || record.SourceActorId != sourceActorId
				|| string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal)) continue;
			if (!string.Equals(record.State, XjShiFruitLockStateIds.PermanentlySealed, StringComparison.Ordinal))
			{
				record.State = XjShiFruitLockStateIds.PermanentlySealed;
				changed = true;
				newlySealed = true;
			}
			if (record.SealedYear <= 0)
			{
				record.SealedYear = year;
				changed = true;
				newlySealed = true;
			}
			string resolvedDomainId = string.IsNullOrWhiteSpace(domainId) ? string.Empty : domainId.Trim();
			if (resolvedDomainId.Length > 0
				&& !string.Equals(record.ShiDomainId, resolvedDomainId, StringComparison.Ordinal))
			{
				record.ShiDomainId = resolvedDomainId;
				changed = true;
			}
		}

		// 0.9.8.2及更早旧档没有投释锁位文档。真君录历史仍在时，死亡冷路径补出一次。
		if (!changed && XjGuoWeiRegistry.TryGetHistoricalEntry(sourceActorId, out XjGuoWeiRegistryEntry history)
			&& history.Found && !string.IsNullOrWhiteSpace(history.GuoWei))
		{
			string positionId = XjGuoWeiCalculator.NormalizeGuoWeiName(history.GuoWei);
			if (!string.IsNullOrWhiteSpace(positionId)
				&& !RecordsByPosition.ContainsKey(positionId)
				&& !IsActivelyHeldByAnother(positionId, sourceActorId))
			{
				RecordsByPosition[positionId] = new XjShiFruitPositionLockRecord
				{
					PositionId = positionId,
					DaoTu = NormalizeText(history.DaoTu),
					PositionType = XjGuoWeiRegistry.ResolveTypeFromName(positionId),
					SourceActorId = sourceActorId,
					SourceActorName = string.IsNullOrWhiteSpace(history.ActorName)
						? actor?.getName() ?? string.Empty : history.ActorName,
					ShiDomainId = string.IsNullOrWhiteSpace(domainId) ? ResolveActorDomain(actor) : domainId.Trim(),
					BoundYear = Math.Max(1, history.Year),
					SealedYear = year,
					State = XjShiFruitLockStateIds.PermanentlySealed
				};
				changed = true;
				newlySealed = true;
			}
		}

		if (!changed) return;
		MarkChanged();
		if (newlySealed && actor?.data != null)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"肉身既殁，投释前所持果余闰位随真灵永封释土，后世无人可循常法承证。",
				XjEventIconCatalog.HistoryWorld);
		}
	}

	internal static bool IsLocked(string positionId)
	{
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		return id.Length > 0
			&& RecordsByPosition.TryGetValue(id, out XjShiFruitPositionLockRecord record)
			&& record != null
			&& !string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal);
	}

	internal static bool HasLockedPosition(string daoTu, string positionType)
	{
		string normalizedDaoTu = NormalizeText(daoTu);
		string normalizedType = XjGuoWeiCalculator.NormalizePositionType(positionType);
		foreach (XjShiFruitPositionLockRecord record in RecordsByPosition.Values)
		{
			if (record == null || string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal)) continue;
			if (string.Equals(record.DaoTu, normalizedDaoTu, StringComparison.Ordinal)
				&& string.Equals(record.PositionType, normalizedType, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static bool IsContaminated(string positionId)
	{
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		return id.Length > 0
			&& RecordsByPosition.TryGetValue(id, out XjShiFruitPositionLockRecord record)
			&& record != null
			&& string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal);
	}

	internal static bool TryStealFromShiTu(Actor actor, string positionId, out string intentionId)
	{
		intentionId = string.Empty;
		if (actor?.data == null || !actor.isAlive() || XjCultivationPathRules.IsShi(actor)) return false;
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (id.Length == 0 || !RecordsByPosition.TryGetValue(id, out XjShiFruitPositionLockRecord record)
			|| record == null
			|| !string.Equals(record.State, XjShiFruitLockStateIds.PermanentlySealed, StringComparison.Ordinal)) return false;
		if (!XjZhantanlinSystem.IsInside(actor)
			|| XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu
			|| XjDaoHuiPolicy.Read(actor) < TheftMinimumDaoHui) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int year = Math.Max(1, XjYearTracker.CurrentYear);
		int roll = XjDeterministicHash.PositiveIndex(
			actorId + (long)year * 1009L + record.SealedYear,
			id + "|shi_fruit_theft_v1",
			10000);
		if (roll >= TheftChanceBasisPoints) return false;

		int intentionIndex = XjDeterministicHash.PositiveIndex(
			actorId + record.SourceActorId + year,
			id + "|shi_contamination_v1",
			ModernShiIntentions.Length);
		intentionId = ModernShiIntentions[intentionIndex];
		record.State = XjShiFruitLockStateIds.StolenContaminated;
		record.StolenYear = year;
		record.StolenByActorId = actorId;
		record.StolenByActorName = actor.getName();
		record.ShiIntentionId = intentionId;
		MarkChanged();

		string displayPosition = XjGuoWeiCalculator.GetDisplayGuoWeiName(id);
		string displayIntention = XjShiCatalog.GetLineageDisplay(intentionId);
		string text = actor.getName() + "潜入旃檀林，自释土封锁中盗出" + displayPosition
			+ "。此位离土之时已受" + displayIntention + "释意浸染，原有意向剧变，后世承修更难。";
		XjWorldHistoryStore.RecordActorEvent(actor, text, XjEventIconCatalog.HistoryWorld);
		XjBroadcastSystem.BroadcastBLevelActorEvent(actor, text, text, XjEventIconCatalog.HistoryWorld,
			XjAnnouncementCategory.AuthorityPosition);
		return true;
	}

	internal static int ResolveAttemptDaoHuiPenalty(string positionId, string positionType)
	{
		if (!IsContaminated(positionId)) return 0;
		return string.Equals(XjGuoWeiCalculator.NormalizePositionType(positionType),
			XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			? 0
			: ContaminatedDerivedPositionPenalty;
	}

	internal static int ResolveContaminatedZhengWeiThreshold(string positionId)
	{
		return IsContaminated(positionId) ? ContaminatedZhengWeiThreshold : 0;
	}

	internal static float ResolveCultivationMultiplier(Actor actor)
	{
		if (actor?.data == null) return 1f;
		XjJinDanState state = XjJinDanAccessor.BuildPositionCarrierState(actor);
		return state.Found && IsContaminated(state.GuoWei)
			? ContaminatedCultivationMultiplier
			: 1f;
	}

	internal static IReadOnlyList<string> BuildDaoTuStatusSummaries(string daoTu)
	{
		string normalizedDaoTu = NormalizeText(daoTu);
		List<string> result = new List<string>();
		if (normalizedDaoTu.Length == 0) return result;
		List<string> ids = new List<string>(RecordsByPosition.Keys);
		ids.Sort(StringComparer.Ordinal);
		for (int i = 0; i < ids.Count; i++)
		{
			XjShiFruitPositionLockRecord record = RecordsByPosition[ids[i]];
			if (record == null || !string.Equals(record.DaoTu, normalizedDaoTu, StringComparison.Ordinal)) continue;
			string position = XjGuoWeiCalculator.GetDisplayGuoWeiName(record.PositionId);
			if (string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal))
			{
				result.Add(position + "受" + XjShiCatalog.GetLineageDisplay(record.ShiIntentionId) + "释意浸染");
			}
			else
			{
				result.Add(position + "随" + Empty(record.SourceActorName, "投释真君")
					+ "封于" + ResolveDomainDisplay(record.ShiDomainId));
			}
		}
		return result;
	}

	internal static string BuildStatusLine(string positionId)
	{
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (id.Length == 0 || !RecordsByPosition.TryGetValue(id, out XjShiFruitPositionLockRecord record)
			|| record == null) return string.Empty;
		if (string.Equals(record.State, XjShiFruitLockStateIds.StolenContaminated, StringComparison.Ordinal))
		{
			string intention = XjShiCatalog.GetLineageDisplay(record.ShiIntentionId);
			return "释意沾染：" + intention + "；承继门槛提高，修持明显受损，仅余常位过半。";
		}
		string domain = ResolveDomainDisplay(record.ShiDomainId);
		return string.Equals(record.State, XjShiFruitLockStateIds.PermanentlySealed, StringComparison.Ordinal)
			? "释土锁位：随" + Empty(record.SourceActorName, "投释真君") + "真灵永封于" + domain + "，无人可循常法承证。"
			: "释土系位：仍随" + Empty(record.SourceActorName, "投释真君") + "真灵系于" + domain + "，现世不可承证。";
	}

	private static bool IsActivelyHeldByAnother(string positionId, long sourceActorId)
	{
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (normalized.Length == 0) return false;
		IReadOnlyList<XjGuoWeiRegistryEntry> active = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < active.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = active[i];
			if (!entry.Found || !entry.IsActive || entry.ActorId <= 0L || entry.ActorId == sourceActorId) continue;
			if (string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei), normalized, StringComparison.Ordinal))
				return true;
		}
		return false;
	}

	private static XjShiFruitPositionLockRecord Normalize(XjShiFruitPositionLockRecord source)
	{
		if (source == null) return null;
		string positionId = XjGuoWeiCalculator.NormalizeGuoWeiName(source.PositionId);
		if (positionId.Length == 0) return null;
		string positionType = XjGuoWeiCalculator.NormalizePositionType(source.PositionType);
		if (positionType.Length == 0) positionType = XjGuoWeiRegistry.ResolveTypeFromName(positionId);
		string state = XjShiFruitLockStateIds.IsKnown(source.State)
			? source.State : XjShiFruitLockStateIds.LivingBound;
		string intention = XjShiLineageIds.IsKnown(source.ShiIntentionId)
			? source.ShiIntentionId : string.Empty;
		string daoTu = NormalizeText(source.DaoTu);
		if (daoTu.Length == 0) daoTu = XjGuoWeiCalculator.ResolveDaoTuFromGuoWeiName(positionId);
		return new XjShiFruitPositionLockRecord
		{
			PositionId = positionId,
			DaoTu = daoTu,
			PositionType = positionType,
			SourceActorId = Math.Max(0L, source.SourceActorId),
			SourceActorName = source.SourceActorName?.Trim() ?? string.Empty,
			ShiDomainId = source.ShiDomainId?.Trim() ?? string.Empty,
			BoundYear = Math.Max(0, source.BoundYear),
			SealedYear = Math.Max(0, source.SealedYear),
			State = state,
			StolenYear = Math.Max(0, source.StolenYear),
			StolenByActorId = Math.Max(0L, source.StolenByActorId),
			StolenByActorName = source.StolenByActorName?.Trim() ?? string.Empty,
			ShiIntentionId = intention
		};
	}

	private static string ResolveActorDomain(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		if (string.IsNullOrWhiteSpace(domainId))
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out domainId);
		if (string.IsNullOrWhiteSpace(domainId))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
				domainId = XjShiDomainCatalog.ZhantanlinDomainId;
		}
		return domainId?.Trim() ?? string.Empty;
	}

	private static string ResolveDomainDisplay(string domainId)
	{
		if (!string.IsNullOrWhiteSpace(domainId)
			&& XjShiDomainState.TryGet(domainId, out XjShiDomainRecord domain)
			&& domain != null && !string.IsNullOrWhiteSpace(domain.DisplayName))
		{
			return domain.DisplayName.Trim();
		}
		return string.Equals(domainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)
			? "旃檀林" : "释土";
	}

	private static string NormalizeText(string value)
	{
		return (value ?? string.Empty).Trim();
	}

	private static string Empty(string value, string fallback)
	{
		return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}
}
