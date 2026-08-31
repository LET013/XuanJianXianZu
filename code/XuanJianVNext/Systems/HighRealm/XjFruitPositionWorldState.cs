using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Shi;

using XuanJianVNext.Systems.LongShu;
namespace XuanJianVNext.Systems.HighRealm;

internal sealed class XjFruitPositionWorldArchiveData
{
	public int LastUpdatedYear { get; set; }
	public List<XjDaoMomentumArchiveRecord> DaoMomentum { get; set; } = new List<XjDaoMomentumArchiveRecord>();
	public List<XjDerivedPositionArchiveRecord> Positions { get; set; } = new List<XjDerivedPositionArchiveRecord>();
	public List<XjDaoTaiPositionBindingArchiveRecord> DaoTaiBindings { get; set; } = new List<XjDaoTaiPositionBindingArchiveRecord>();
	public XjTaiYinHiddenFruitArchiveData TaiYinHiddenFruit { get; set; } = new XjTaiYinHiddenFruitArchiveData();
	public XjYinYangRarePhenomenaArchiveData YinYangRarePhenomena { get; set; } = new XjYinYangRarePhenomenaArchiveData();
}

internal sealed class XjDaoMomentumArchiveRecord
{
	public string DaoTu { get; set; } = string.Empty;
	public int Momentum { get; set; }
	public int LastYear { get; set; }
}

internal sealed class XjDerivedPositionArchiveRecord
{
	public string PositionId { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string PositionType { get; set; } = string.Empty;
	public int SlotIndex { get; set; }
	public string PrimaryAuthority { get; set; } = string.Empty;
	public string SecondaryAuthority { get; set; } = string.Empty;
	public string ExternalDaoTu { get; set; } = string.Empty;
	public string DerivedAuthority { get; set; } = string.Empty;
	public string SecondaryDerivedAuthority { get; set; } = string.Empty;
	public string JinXingName { get; set; } = string.Empty;
	public long FounderActorId { get; set; }
	public string FounderName { get; set; } = string.Empty;
	public int FoundedYear { get; set; }
	public long LastHolderActorId { get; set; }
	public string LastHolderDisplay { get; set; } = string.Empty;
	public int LastHolderDaoHui { get; set; }
	public int LastHeldYear { get; set; }
	// 果名改易者与上任持位者分开保存。仙鉴只展示上任持位者，
	// 命名字段只用于防止同一任持有者反复改名与保存改名沿革。
	public long NamingActorId { get; set; }
	public string NamingActorDisplay { get; set; } = string.Empty;
	public int NamingActorDaoHui { get; set; }
	public int NamingYear { get; set; }
	// 同一持位者隐伏满百年后，后世修士以真实求果试探方可令此位显主。
	// 只记录最近一次已揭示的持位者，继任者需重新在位百年后才能再次触发。
	public long RevealedHolderActorId { get; set; }
	public long RevealedByActorId { get; set; }
	public int RevealedYear { get; set; }
	public bool LegacyBeyondCapacity { get; set; }
}

internal sealed class XjDaoTaiPositionBindingArchiveRecord
{
	public long ActorId { get; set; }
	public string PrimaryPositionId { get; set; } = string.Empty;
	public string SecondaryPositionId { get; set; } = string.Empty;
	public string SecondaryKind { get; set; } = string.Empty;
	public int BoundYear { get; set; }
}

internal readonly struct XjFruitPositionCapacity
{
	internal readonly int Residual;
	internal readonly int Intercalary;

	internal XjFruitPositionCapacity(int residual, int intercalary)
	{
		Residual = Math.Max(0, Math.Min(XjGuoWeiQuanBingRules.YuWeiSlotCount, residual));
		Intercalary = Math.Max(0, Math.Min(XjGuoWeiQuanBingRules.RunWeiSlotCount, intercalary));
	}
}

/// <summary>
/// 果位世界只保存聚合值、位置定义与稳定 actor id。余闰位置首次证成时
/// 才建立，随后由后世继承；道势下降只阻止补位，不抹去在世持有者或历史。
/// </summary>
internal readonly struct XjFruitPositionCapacityRule
{
	internal readonly int MinMomentum;
	internal readonly int MaxMomentum;
	internal readonly int Residual;
	internal readonly int Intercalary;

	internal XjFruitPositionCapacityRule(int minMomentum, int maxMomentum, int residual, int intercalary)
	{
		MinMomentum = Math.Max(0, minMomentum);
		MaxMomentum = Math.Max(MinMomentum, maxMomentum);
		Residual = Math.Max(0, residual);
		Intercalary = Math.Max(0, intercalary);
	}

	internal string RangeLabel => MinMomentum + "—" + MaxMomentum;
}

internal static class XjFruitPositionWorldState
{
	private const int DefaultMomentumBeforeFirstCensus = 70;
	private static readonly XjFruitPositionCapacityRule[] CapacityRules =
	{
		new XjFruitPositionCapacityRule(0, 34, 1, 1),
		new XjFruitPositionCapacityRule(35, 49, 1, 1),
		new XjFruitPositionCapacityRule(50, 64, 2, 1),
		new XjFruitPositionCapacityRule(65, 74, 3, 3),
		new XjFruitPositionCapacityRule(75, 84, 4, 5),
		new XjFruitPositionCapacityRule(85, 94, 5, 7),
		new XjFruitPositionCapacityRule(95, 100, 6, 9)
	};
	private static readonly Dictionary<string, int> MomentumByDaoTu = new Dictionary<string, int>(StringComparer.Ordinal);
	private static readonly Dictionary<string, XjDerivedPositionArchiveRecord> PositionsById = new Dictionary<string, XjDerivedPositionArchiveRecord>(StringComparer.Ordinal);
	private static readonly Dictionary<long, XjDaoTaiPositionBindingArchiveRecord> DaoTaiBindingsByActorId = new Dictionary<long, XjDaoTaiPositionBindingArchiveRecord>();
	private static int _lastUpdatedYear;
	private static int _bindingRevision;

	internal static int LastUpdatedYear => _lastUpdatedYear;
	internal static int BindingRevision => _bindingRevision;

	internal static int GetMomentum(string daoTu)
	{
		string normalized = Normalize(daoTu);
		return normalized.Length > 0 && MomentumByDaoTu.TryGetValue(normalized, out int value)
			? Math.Max(0, Math.Min(100, value))
			: DefaultMomentumBeforeFirstCensus;
	}

	internal static XjFruitPositionCapacity GetCapacity(string daoTu)
	{
		return ResolveCapacityFromMomentum(daoTu, GetMomentum(daoTu));
	}

	internal static IReadOnlyList<XjFruitPositionCapacityRule> ReadCapacityRules() => CapacityRules;

	internal static string FormatCapacityRuleSummary()
	{
		List<string> parts = new List<string>(CapacityRules.Length);
		for (int i = 0; i < CapacityRules.Length; i++)
		{
			XjFruitPositionCapacityRule rule = CapacityRules[i];
			parts.Add("道势" + rule.RangeLabel + "：余" + rule.Residual + "、闰" + rule.Intercalary);
		}
		return string.Join("；", parts) + "。长庚闰位恒为0。";
	}

	private static XjFruitPositionCapacity ResolveCapacityFromMomentum(string daoTu, int momentum)
	{
		int normalizedMomentum = Math.Max(0, Math.Min(100, momentum));
		XjFruitPositionCapacityRule selected = CapacityRules[CapacityRules.Length - 1];
		for (int i = 0; i < CapacityRules.Length; i++)
		{
			if (normalizedMomentum < CapacityRules[i].MinMomentum
				|| normalizedMomentum > CapacityRules[i].MaxMomentum) continue;
			selected = CapacityRules[i];
			break;
		}
		int residual = Math.Max(XjGuoWeiQuanBingRules.MinimumYuWeiSlotCount, selected.Residual);
		int intercalary = Math.Max(XjGuoWeiQuanBingRules.MinimumRunWeiSlotCount, selected.Intercalary);
		XjFruitPositionCapacity capacity = new XjFruitPositionCapacity(residual, intercalary);
		return XjZiJinSwordDaoCatalog.IsLongGeng(daoTu)
			? new XjFruitPositionCapacity(capacity.Residual, 0)
			: capacity;
	}

	internal static int ResolveSlotCount(string daoTu, string positionType)
	{
		if (string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) return 1;
		if (XjZiJinSwordDaoCatalog.IsLongGeng(daoTu)
			&& string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return 0;
		XjFruitPositionCapacity capacity = GetCapacity(daoTu);
		if (string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)) return capacity.Residual;
		if (string.Equals(positionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)) return capacity.Intercalary;
		return 0;
	}

	internal static bool CanAttemptPosition(Actor actor, string daoTu, string positionType, out string reason)
	{
		return CanAttemptPosition(actor, daoTu, positionType, string.Empty, out reason);
	}

	internal static bool CanAttemptPosition(
		Actor actor,
		string daoTu,
		string positionType,
		string positionId,
		out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null)
		{
			reason = "角色无效";
			return false;
		}

		string normalizedType = XjGuoWeiCalculator.NormalizePositionType(positionType);
		if (XjZiJinSwordDaoCatalog.IsLongGeng(daoTu)
			&& string.Equals(normalizedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
		{
			reason = "长庚不生闰位";
			return false;
		}
		int daoHui = (int)XjDaoHuiPolicy.Read(actor);
		if (string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			// 果位是本道唯一核心席位，不能再出现“果位零门槛，余闰反而要高道慧创证”的倒挂。
			// 正常求果至少需要 85 道慧；释意污染只会在此基础上继续抬高要求。
			int contaminatedThreshold =
				XjShiFruitPositionLockSystem.ResolveContaminatedZhengWeiThreshold(positionId);
			int fruitThreshold = Math.Max((int)XjDaoHuiPolicy.FruitPositionThreshold, contaminatedThreshold);
			if (daoHui < fruitThreshold)
			{
				reason = contaminatedThreshold > (int)XjDaoHuiPolicy.FruitPositionThreshold
					? "释意沾染，果位原有意向剧变，道慧不足以镇定"
					: "果位为本道正受，道慧不足以承果";
				return false;
			}
			return true;
		}

		bool positionAlreadyExists = !string.IsNullOrWhiteSpace(positionId)
			&& TryGetPosition(positionId, out _);
		int threshold = positionAlreadyExists
			? (int)XjDaoHuiPolicy.StableInheritanceThreshold
			: string.Equals(normalizedType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal)
				? (int)XjDaoHuiPolicy.DeriveResidualThreshold
				: (int)XjDaoHuiPolicy.OpenIntercalaryThreshold;
		threshold = Math.Min((int)XjDaoHuiPolicy.Maximum, threshold
			+ XjShiFruitPositionLockSystem.ResolveAttemptDaoHuiPenalty(positionId, normalizedType));

		// 正位道途开闰、收位道途发余，只在首次创证时承受五现的额外阻力。
		// 已经真实存在的位置可由后人按更低的稳定继承门槛承接。
		// 这里的所有余/闰门槛都低于 85 道慧的本道果位，明确维持“派生位易于正果”的位序难度。
		if (!positionAlreadyExists && XjFiveManifestationCatalog.IsDifficultPosition(daoTu, normalizedType))
		{
			// 五现的“正不喜闰、收不喜余”继续体现在更高道慧门槛，
			// 但道势只负责扩充席位数量，不能再把余/闰创证本身彻底锁死。
			threshold = (int)XjDaoHuiPolicy.DifficultPositionThreshold;
		}
		if (daoHui < threshold)
		{
			reason = XjShiFruitPositionLockSystem.IsContaminated(positionId)
				? "释意沾染，位序意向大变，道慧不足以承继"
				: positionAlreadyExists ? "道慧不足以稳定承继" : "道慧不足以创证";
			return false;
		}
		return true;
	}

	internal static void UpdateFromAnnualSummaries(IEnumerable<XjCenturySummaryItemRecord> summaries, int year)
	{
		if (summaries == null || year <= 0) return;
		bool changed = false;
		foreach (XjCenturySummaryItemRecord item in summaries)
		{
			if (item == null || string.IsNullOrWhiteSpace(item.Key)) continue;
			string daoTu = Normalize(item.Key);
			if (daoTu.Length == 0) continue;
			int score = Math.Max(0, item.Score);
			int calculated = Math.Max(0, Math.Min(100, (int)Math.Round(Math.Sqrt(score) * 5.0)));
			if (score > 0) calculated = Math.Max(20, calculated);
			if (string.Equals(daoTu, XjLongShuSystem.HeShuiDaoTuId, StringComparison.Ordinal))
			{
				// 龙属不进入普通四族道途人口统计，但其血脉与行水本身会真实推动合水。
				// 因此在统一年度道势快照上追加一个O(龙属数)的合水修正，而不是另开扫描。
				calculated = XjLongShuSystem.ResolveHeShuiMomentumTarget(calculated);
			}
			if (!MomentumByDaoTu.TryGetValue(daoTu, out int current))
			{
				// 第一次人口普查必须真实写入，否则未见过的道途会一直沿用默认70，
				// 动态余闰容量无法反映实际兴衰。初次建档不补发容量公告。
				MomentumByDaoTu[daoTu] = calculated;
				changed = true;
				continue;
			}
			XjFruitPositionCapacity oldCapacity = ResolveCapacityFromMomentum(daoTu, current);
			// 三年一份快照时仍采用缓变，避免容量在阈值附近频繁开关。
			int next = Math.Max(0, Math.Min(100, (current * 3 + calculated) / 4));
			if (next != current)
			{
				MomentumByDaoTu[daoTu] = next;
				changed = true;
				XjFruitPositionCapacity newCapacity = ResolveCapacityFromMomentum(daoTu, next);
				if (newCapacity.Residual > oldCapacity.Residual || newCapacity.Intercalary > oldCapacity.Intercalary)
				{
					string text = "【位序扩充】" + daoTu + "道势渐盛，余位可开至" + newCapacity.Residual
						+ "席，闰位可开至" + newCapacity.Intercalary + "席。";
					XjBroadcastSystem.BroadcastBLevelWorldEvent(
						text,
						XjEventIconCatalog.HistoryWorld,
						XjAnnouncementCategory.AuthorityPosition);
				}
			}
		}
		_lastUpdatedYear = Math.Max(_lastUpdatedYear, year);
		RefreshLegacyCapacityFlags();
		if (changed)
		{
			XjWorldArchiveSystem.MarkChanged();
		}
	}

	internal static XjDerivedPositionArchiveRecord EnsurePosition(
		Actor actor,
		string daoTu,
		string positionType,
		string positionId,
		string jinXing,
		string externalDaoTu,
		int year)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		string actorName = actor?.data == null ? string.Empty : actor.getName();
		return EnsurePositionCore(
			daoTu, positionType, positionId, jinXing, externalDaoTu, year,
			actorId, actorName, markArchiveChanged: true);
	}

	/// <summary>
	/// 为不在地图 Actor 生命周期中的外部高位存在建立位序事实。FounderActorId 固定为0，
	/// 但创证者姓名与创证年份仍进入果位世界档案；不会生成任何玩家可操作角色。
	/// </summary>
	internal static XjDerivedPositionArchiveRecord EnsureExternalPosition(
		string founderName,
		string daoTu,
		string positionType,
		string positionId,
		string jinXing,
		string externalDaoTu,
		int year,
		bool announceOpening = true)
	{
		XjDerivedPositionArchiveRecord record = EnsurePositionCore(
			daoTu, positionType, positionId, jinXing, externalDaoTu, year,
			0L, founderName, markArchiveChanged: true, broadcastOpening: announceOpening);
		if (record != null)
		{
			string display = (founderName ?? string.Empty).Trim();
			if (record.LastHolderActorId != 0L
				|| !string.Equals(record.LastHolderDisplay ?? string.Empty, display, StringComparison.Ordinal)
				|| record.LastHeldYear != Math.Max(0, year))
			{
				record.LastHolderActorId = 0L;
				record.LastHolderDisplay = display;
				record.LastHeldYear = Math.Max(0, year);
				_bindingRevision++;
				XjWorldArchiveSystem.MarkChanged();
			}
		}
		return record;
	}

	/// <summary>
	/// 0.9.6.10 及更早的存档只有果位登记账本，没有动态位置文档。
	/// 加载后按既有账本补出位置定义，不扫描角色，也不逐条触发保护提交。
	/// </summary>
	internal static void BackfillImportedPositions(IEnumerable<XjGuoWeiRegistryEntry> entries)
	{
		if (entries == null) return;
		bool changed = false;
		foreach (XjGuoWeiRegistryEntry entry in entries)
		{
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.GuoWei) || string.IsNullOrWhiteSpace(entry.DaoTu)) continue;
			string id = XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei);
			if (id.Length == 0 || PositionsById.ContainsKey(id)) continue;
			XjDerivedPositionArchiveRecord record = EnsurePositionCore(
				entry.DaoTu,
				XjGuoWeiRegistry.ResolveTypeFromName(entry.GuoWei),
				id,
				entry.JinXing,
				string.Empty,
				entry.Year,
				entry.ActorId,
				entry.ActorName,
				markArchiveChanged: false);
			changed |= record != null;
		}
		if (!changed) return;
		RefreshLegacyCapacityFlags();
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static XjDerivedPositionArchiveRecord EnsurePositionCore(
		string daoTu,
		string positionType,
		string positionId,
		string jinXing,
		string externalDaoTu,
		int year,
		long actorId,
		string actorName,
		bool markArchiveChanged,
		bool broadcastOpening = true)
	{
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (id.Length == 0) return null;
		if (!PositionsById.TryGetValue(id, out XjDerivedPositionArchiveRecord record))
		{
			string normalizedType = XjGuoWeiCalculator.NormalizePositionType(positionType);
			int slot = XjGuoWeiCalculator.ResolveSlotIndex(id);
			IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(daoTu);
			int authorityCount = authorities?.Count ?? 0;
			int primaryIndex = authorityCount > 0
				? XjDeterministicHash.PositiveIndex(slot + year, daoTu + "|" + normalizedType + "|primary", authorityCount)
				: 0;
			int secondaryIndex = authorityCount > 1
				? (primaryIndex + 1 + XjDeterministicHash.PositiveIndex(slot, daoTu + "|secondary", authorityCount - 1)) % authorityCount
				: primaryIndex;
			string primary = authorityCount > 0 ? authorities[primaryIndex] : string.Empty;
			string secondary = authorityCount > 1 ? authorities[secondaryIndex] : string.Empty;
			record = new XjDerivedPositionArchiveRecord
			{
				PositionId = id,
				DaoTu = Normalize(daoTu),
				PositionType = normalizedType,
				SlotIndex = Math.Max(1, slot),
				PrimaryAuthority = primary,
				SecondaryAuthority = secondary,
				ExternalDaoTu = Normalize(externalDaoTu),
				DerivedAuthority = XjDerivedAuthorityNameBuilder.Build(
					daoTu, normalizedType, primary, secondary, externalDaoTu, slot, actorId + year),
				SecondaryDerivedAuthority = !string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
					? XjDerivedAuthorityNameBuilder.Build(
						daoTu, normalizedType, secondary, primary, externalDaoTu, slot + 17, actorId + year + 7919L)
					: string.Empty,
				JinXingName = XjJinXingNamePolicy.NormalizeOrBuild(
					daoTu, normalizedType, jinXing, primary, externalDaoTu, slot, actorId + year),
				FounderActorId = Math.Max(0L, actorId),
				FounderName = (actorName ?? string.Empty).Trim(),
				FoundedYear = Math.Max(0, year)
			};
			PositionsById[id] = record;
			RefreshLegacyCapacityFlag(record);
			if (markArchiveChanged)
			{
				XjWorldArchiveSystem.MarkChanged();
				XjWorldArchiveSystem.RequestProtectedCommit();
				if (broadcastOpening
					&& !string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
				{
					string kind = string.Equals(normalizedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal) ? "闰位" : "余位";
					string displayPosition = XjGuoWeiCalculator.BuildGuoWeiSlotDisplayName(
						record.DaoTu, normalizedType, record.SlotIndex);
					string text = "【" + kind + "开辟】" + record.DaoTu + "道途新开"
						+ XjGuoWeiCalculator.GetChineseSlotOrdinal(record.SlotIndex) + "席" + kind
						+ "，其位名为【" + displayPosition + "】。";
					XjBroadcastSystem.BroadcastBLevelWorldEvent(
						text, XjEventIconCatalog.HistoryWorld, XjAnnouncementCategory.AuthorityPosition);
				}
			}
		}
		return record;
	}

	/// <summary>
	/// 撤销RC8及更早绝对1000年逻辑提前创建的渊照正果位置。该调用只发生在
	/// 新时间轴尚未到“起始年+500”且渊照正果按规则不可能被任何角色持有时。
	/// </summary>
	internal static bool RemovePrematureYuanZhaoZhengWeiForTimelineMigration()
	{
		string id = XjGuoWeiCalculator.NormalizeGuoWeiName("渊照" + XjGuoWeiCalculator.ZhengWei);
		if (id.Length == 0 || !PositionsById.Remove(id)) return false;
		_bindingRevision++;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool TryGetPosition(string positionId, out XjDerivedPositionArchiveRecord record)
	{
		return PositionsById.TryGetValue(XjGuoWeiCalculator.NormalizeGuoWeiName(positionId), out record);
	}

	internal static string ResolveJinXingName(string positionId, string fallback)
	{
		if (TryGetPosition(positionId, out XjDerivedPositionArchiveRecord record)
			&& !string.IsNullOrWhiteSpace(record.JinXingName))
		{
			// 自动生成的旧式“太初…道途…太玄性”会阻止新持位者获得新金性名；
			// 仅迁移未被高道慧修士亲自改名的记录，保留玩家/世界史中的命名行为。
			if (record.NamingActorId <= 0L
				&& (XjJinXingCalculator.NeedsDaoTuNormalization(record.JinXingName, record.DaoTu)
					|| XjJinXingNamePolicy.IsLegacyPositionSyntheticName(record.JinXingName, record.DaoTu)))
			{
				long seed = record.FounderActorId > 0L
					? record.FounderActorId
					: XjDeterministicHash.StableHash(record.PositionId ?? string.Empty);
				string migrated = XjJinXingNamePolicy.NormalizeOrBuild(
					record.DaoTu,
					record.PositionType,
					string.Empty,
					record.PrimaryAuthority,
					record.ExternalDaoTu,
					record.SlotIndex,
					seed);
				if (!string.IsNullOrWhiteSpace(migrated))
				{
					record.JinXingName = migrated;
					XjWorldArchiveSystem.MarkChanged();
				}
			}
			return record.JinXingName;
		}
		return XjJinXingNamePolicy.NormalizeLegacyName(fallback);
	}

	internal static bool HasRevealedHolder(string positionId, long holderActorId)
	{
		return holderActorId > 0L
			&& TryGetPosition(positionId, out XjDerivedPositionArchiveRecord record)
			&& record != null
			&& record.RevealedHolderActorId == holderActorId;
	}

	/// <summary>
	/// 记录一次“求果探位而显主”。该提交以果位+当前持位者为唯一键：
	/// 同一持位者只会被揭示一次；果位换主后，新持位者仍需重新在位百年。
	/// </summary>
	internal static bool TryRecordHolderRevealed(
		string positionId,
		long holderActorId,
		long seekerActorId,
		int year)
	{
		if (holderActorId <= 0L
			|| seekerActorId <= 0L
			|| holderActorId == seekerActorId
			|| !TryGetPosition(positionId, out XjDerivedPositionArchiveRecord record)
			|| record == null
			|| record.RevealedHolderActorId == holderActorId)
		{
			return false;
		}

		record.RevealedHolderActorId = holderActorId;
		record.RevealedByActorId = seekerActorId;
		record.RevealedYear = Math.Max(1, year);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static void RecordHolderEnded(string positionId, long actorId, string holderDisplay, int daoHui, int year)
	{
		if (!TryGetPosition(positionId, out XjDerivedPositionArchiveRecord record)) return;
		record.LastHolderActorId = Math.Max(0L, actorId);
		record.LastHolderDisplay = (holderDisplay ?? string.Empty).Trim();
		record.LastHolderDaoHui = (int)XjDaoHuiPolicy.Clamp(daoHui);
		record.LastHeldYear = Math.Max(0, year);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool TryRenamePosition(
		Actor actor,
		string positionId,
		string proposedName,
		bool fruitMeaningChanged,
		int year)
	{
		if (actor?.data == null
			|| !fruitMeaningChanged
			|| string.IsNullOrWhiteSpace(proposedName)
			|| !TryGetPosition(positionId, out XjDerivedPositionArchiveRecord record))
		{
			return false;
		}
		int daoHui = (int)XjDaoHuiPolicy.Read(actor);
		long actorId = ((BaseSystemData)actor.data).id;
		if (daoHui < XjDaoHuiPolicy.PositionRenamingThreshold
			|| daoHui <= record.LastHolderDaoHui
			|| record.NamingActorId == actorId)
		{
			return false;
		}
		record.JinXingName = XjJinXingNamePolicy.NormalizeLegacyName(proposedName);
		record.NamingActorId = actorId;
		record.NamingActorDisplay = actor.getName();
		record.NamingActorDaoHui = daoHui;
		record.NamingYear = Math.Max(0, year);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool TryBindDaoTaiSecondary(
		Actor actor,
		string primaryPositionId,
		string secondaryPositionId,
		int year,
		out string reason)
	{
		reason = string.Empty;
		if (actor?.data == null || !XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			reason = "只有在世道胎可以合持第二处位序";
			return false;
		}

		string normalizedPrimaryId = XjGuoWeiCalculator.NormalizeGuoWeiName(primaryPositionId);
		string normalizedSecondaryId = XjGuoWeiCalculator.NormalizeGuoWeiName(secondaryPositionId);
		if (!TryGetPosition(normalizedPrimaryId, out XjDerivedPositionArchiveRecord primary)
			|| primary == null
			|| !TryGetPosition(normalizedSecondaryId, out XjDerivedPositionArchiveRecord secondary)
			|| secondary == null
			|| !XjDaoTaiDualPositionSystem.IsComplementaryPair(primary, secondary))
		{
			reason = "道胎双位必须由一处果位与一处余位或闰位组成";
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjDaoTaiDualPositionSystem.IsDaoTuCompatibleForDualPosition(primary, secondary))
		{
			reason = "第二位与原持道途之间没有可承接的道论关系";
			return false;
		}

		if (actorId <= 0L
			|| !XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(actorId, out XjGuoWeiRegistryEntry currentEntry)
			|| !string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(currentEntry.GuoWei), normalizedPrimaryId, StringComparison.Ordinal))
		{
			reason = "角色并非原持位序的当前持有者";
			return false;
		}

		// 道势回落后的历史高席位可以继续被在世持有，但空缺后不能被新取得。
		// 因此 LegacyBeyondCapacity 只拦截“本次新得”的余/闰位，不反向抹除既有双位。
		if (XjDaoTaiDualPositionSystem.IsDerived(secondary) && secondary.LegacyBeyondCapacity)
		{
			reason = "该余闰位当前道势不足，暂不可补位";
			return false;
		}
		if (XjTaiYinHiddenFruitSystem.IsHiddenForActor(secondary.PositionId, actorId, out string hiddenReason))
		{
			reason = hiddenReason;
			return false;
		}
		if (XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(secondary.DaoTu, secondary.PositionType, secondary.PositionId))
		{
			reason = "该位序当前不可承继";
			return false;
		}

		if (DaoTaiBindingsByActorId.TryGetValue(actorId, out XjDaoTaiPositionBindingArchiveRecord existing))
		{
			if (existing != null
				&& string.Equals(existing.PrimaryPositionId, normalizedPrimaryId, StringComparison.Ordinal)
				&& string.Equals(existing.SecondaryPositionId, secondary.PositionId, StringComparison.Ordinal))
			{
				XjDaoTaiDualPositionSystem.SyncActorProjection(actor, existing);
				return true;
			}
			reason = "道胎已经合持另一组真实位序";
			return false;
		}
		if (IsDaoTaiSecondaryOccupiedByOther(secondary.PositionId, actorId))
		{
			reason = "该位序已被另一位道胎合持";
			return false;
		}
		if (IsNormallyOccupiedByOther(secondary.PositionId, actorId))
		{
			reason = "该位序已有正常持位者";
			return false;
		}

		// 由果位再取闰位时，跨道执闰仍须有真实权柄、经历或结构证明；
		// 若角色本来就是闰位道胎，则该闰位已经完成合法证成，后补对应果位无需重复证明。
		if (XjDaoTaiDualPositionSystem.IsFruit(primary)
			&& string.Equals(secondary.PositionType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal)
			&& !XjDaoTaiDualPositionSystem.HasRunWeiCarryingProof(actor, primary, secondary))
		{
			reason = "跨道执闰缺少权柄、经历或承载证明";
			return false;
		}

		XjDaoTaiPositionBindingArchiveRecord binding = new XjDaoTaiPositionBindingArchiveRecord
		{
			ActorId = actorId,
			PrimaryPositionId = normalizedPrimaryId,
			SecondaryPositionId = secondary.PositionId,
			SecondaryKind = secondary.PositionType,
			BoundYear = Math.Max(0, year)
		};
		DaoTaiBindingsByActorId[actorId] = binding;
		_bindingRevision++;
		XjDaoTaiDualPositionSystem.SyncActorProjection(actor, binding);
		XjTaiYinHiddenFruitSystem.OnDaoTaiBindingEstablished(actor, binding, Math.Max(1, year));
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool TryGetDaoTaiBinding(long actorId, out XjDaoTaiPositionBindingArchiveRecord record)
	{
		record = null;
		return actorId > 0L && DaoTaiBindingsByActorId.TryGetValue(actorId, out record);
	}

	/// <summary>
	/// Technical actor-id handoff for third-party replacement mods. This is not a death or
	/// succession, so the DaoTai secondary position keeps its original BoundYear and does
	/// not emit a holder-ended archive record.
	/// </summary>
	internal static bool RebindDaoTaiBindingAfterExternalReplacement(long oldActorId, Actor target)
	{
		if (oldActorId <= 0L || target?.data == null || !target.isAlive()) return false;
		long newActorId = ((BaseSystemData)target.data).id;
		if (newActorId <= 0L || newActorId == oldActorId) return false;
		if (!DaoTaiBindingsByActorId.TryGetValue(oldActorId, out XjDaoTaiPositionBindingArchiveRecord binding)
			|| binding == null) return false;
		if (DaoTaiBindingsByActorId.ContainsKey(newActorId))
		{
			return true;
		}

		DaoTaiBindingsByActorId.Remove(oldActorId);
		binding.ActorId = newActorId;
		DaoTaiBindingsByActorId[newActorId] = binding;
		_bindingRevision++;
		XjTaiYinHiddenFruitSystem.OnHolderRebound(oldActorId, newActorId);
		XjDaoTaiDualPositionSystem.SyncActorProjection(target, binding);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool TryGetDaoTaiSecondaryHolder(
		string positionId,
		out long actorId,
		out XjDaoTaiPositionBindingArchiveRecord binding)
	{
		actorId = 0L;
		binding = null;
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (string.IsNullOrWhiteSpace(normalized)) return false;
		foreach (XjDaoTaiPositionBindingArchiveRecord candidate in DaoTaiBindingsByActorId.Values)
		{
			if (candidate == null || candidate.ActorId <= 0L
				|| !string.Equals(candidate.SecondaryPositionId, normalized, StringComparison.Ordinal)) continue;
			actorId = candidate.ActorId;
			binding = candidate;
			return true;
		}
		return false;
	}

	internal static bool IsDaoTaiSecondaryOccupied(string positionId)
	{
		return TryGetDaoTaiSecondaryHolder(positionId, out _, out _);
	}

	internal static bool IsDaoTaiSecondaryOccupiedByOther(string positionId, long actorId)
	{
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (string.IsNullOrWhiteSpace(normalized)) return false;
		foreach (XjDaoTaiPositionBindingArchiveRecord candidate in DaoTaiBindingsByActorId.Values)
		{
			if (candidate == null || candidate.ActorId <= 0L || candidate.ActorId == actorId) continue;
			if (string.Equals(candidate.SecondaryPositionId, normalized, StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static bool IsNormallyOccupiedByOther(string positionId, long actorId)
	{
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(positionId);
		if (string.IsNullOrWhiteSpace(normalized)) return false;
		IReadOnlyList<XjGuoWeiRegistryEntry> active = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < active.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = active[i];
			if (!entry.Found || !entry.IsActive) continue;
			if (!string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei), normalized, StringComparison.Ordinal)) continue;
			return entry.ActorId > 0L && entry.ActorId != actorId;
		}
		return false;
	}

	internal static bool ReleaseDaoTaiBinding(
		long actorId,
		string holderDisplay,
		int daoHui,
		int year,
		string reason)
	{
		if (actorId <= 0L || !DaoTaiBindingsByActorId.TryGetValue(actorId, out XjDaoTaiPositionBindingArchiveRecord binding)
			|| binding == null) return false;

		DaoTaiBindingsByActorId.Remove(actorId);
		_bindingRevision++;
		if (string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(binding.SecondaryPositionId), XjTaiYinHiddenFruitSystem.TaiYinZhengWei, StringComparison.Ordinal))
		{
			XjTaiYinHiddenFruitSystem.OnTaiYinHolderReleased(actorId, binding.SecondaryPositionId, Math.Max(1, year));
		}
		RecordHolderEnded(binding.SecondaryPositionId, actorId, holderDisplay, daoHui, year);
		if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && actor?.data != null)
		{
			XjDaoTaiDualPositionSystem.ClearActorProjection(actor);
		}
		_ = reason;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static IReadOnlyList<XjDaoTaiPositionBindingArchiveRecord> ReadDaoTaiBindingsSnapshot()
	{
		List<XjDaoTaiPositionBindingArchiveRecord> result = new List<XjDaoTaiPositionBindingArchiveRecord>(DaoTaiBindingsByActorId.Count);
		foreach (XjDaoTaiPositionBindingArchiveRecord binding in DaoTaiBindingsByActorId.Values)
		{
			if (binding == null) continue;
			result.Add(new XjDaoTaiPositionBindingArchiveRecord
			{
				ActorId = binding.ActorId,
				PrimaryPositionId = binding.PrimaryPositionId,
				SecondaryPositionId = binding.SecondaryPositionId,
				SecondaryKind = binding.SecondaryKind,
				BoundYear = binding.BoundYear
			});
		}
		result.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		return result;
	}

	internal static IReadOnlyList<XjDerivedPositionArchiveRecord> ReadPositionsSnapshot()
	{
		List<XjDerivedPositionArchiveRecord> result = new List<XjDerivedPositionArchiveRecord>(PositionsById.Count);
		foreach (XjDerivedPositionArchiveRecord record in PositionsById.Values)
		{
			if (record != null) result.Add(Clone(record));
		}
		result.Sort((left, right) =>
		{
			int byYear = right.FoundedYear.CompareTo(left.FoundedYear);
			return byYear != 0 ? byYear : string.Compare(left.PositionId, right.PositionId, StringComparison.Ordinal);
		});
		return result;
	}

	internal static XjFruitPositionWorldArchiveData ExportState()
	{
		XjFruitPositionWorldArchiveData result = new XjFruitPositionWorldArchiveData { LastUpdatedYear = _lastUpdatedYear };
		foreach (KeyValuePair<string, int> pair in MomentumByDaoTu)
		{
			result.DaoMomentum.Add(new XjDaoMomentumArchiveRecord { DaoTu = pair.Key, Momentum = pair.Value, LastYear = _lastUpdatedYear });
		}
		result.DaoMomentum.Sort((left, right) => string.Compare(left.DaoTu, right.DaoTu, StringComparison.Ordinal));
		foreach (XjDerivedPositionArchiveRecord position in PositionsById.Values)
		{
			result.Positions.Add(Clone(position));
		}
		result.Positions.Sort((left, right) => string.Compare(left.PositionId, right.PositionId, StringComparison.Ordinal));
		foreach (XjDaoTaiPositionBindingArchiveRecord binding in DaoTaiBindingsByActorId.Values)
		{
			result.DaoTaiBindings.Add(new XjDaoTaiPositionBindingArchiveRecord
			{
				ActorId = binding.ActorId,
				PrimaryPositionId = binding.PrimaryPositionId,
				SecondaryPositionId = binding.SecondaryPositionId,
				SecondaryKind = binding.SecondaryKind,
				BoundYear = binding.BoundYear
			});
		}
		result.DaoTaiBindings.Sort((left, right) => left.ActorId.CompareTo(right.ActorId));
		result.TaiYinHiddenFruit = XjTaiYinHiddenFruitSystem.ExportState();
		result.YinYangRarePhenomena = XjYinYangRarePhenomenaSystem.ExportState();
		return result;
	}

	internal static void ImportState(XjFruitPositionWorldArchiveData source)
	{
		Clear();
		if (source == null) return;
		_lastUpdatedYear = Math.Max(0, source.LastUpdatedYear);
		if (source.DaoMomentum != null)
		{
			foreach (XjDaoMomentumArchiveRecord record in source.DaoMomentum)
			{
				if (record == null || string.IsNullOrWhiteSpace(record.DaoTu)) continue;
				MomentumByDaoTu[Normalize(record.DaoTu)] = Math.Max(0, Math.Min(100, record.Momentum));
			}
		}
		if (source.Positions != null)
		{
			foreach (XjDerivedPositionArchiveRecord record in source.Positions)
			{
				if (record == null || string.IsNullOrWhiteSpace(record.PositionId)) continue;
				record.PositionId = XjGuoWeiCalculator.NormalizeGuoWeiName(record.PositionId);
				record.PositionType = XjGuoWeiCalculator.NormalizePositionType(record.PositionType);
				record.JinXingName = XjJinXingNamePolicy.NormalizeLegacyName(record.JinXingName);
				if (string.IsNullOrWhiteSpace(record.DerivedAuthority)
					&& !string.Equals(record.PositionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
				{
					record.DerivedAuthority = XjDerivedAuthorityNameBuilder.Build(
						record.DaoTu, record.PositionType, record.PrimaryAuthority, record.SecondaryAuthority,
						record.ExternalDaoTu, record.SlotIndex, record.FounderActorId + record.FoundedYear);
				}
				if (!string.Equals(record.PositionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
					&& string.IsNullOrWhiteSpace(record.SecondaryDerivedAuthority))
				{
					record.SecondaryDerivedAuthority = XjDerivedAuthorityNameBuilder.Build(
						record.DaoTu, record.PositionType, record.SecondaryAuthority, record.PrimaryAuthority,
						record.ExternalDaoTu, record.SlotIndex + 17, record.FounderActorId + record.FoundedYear + 7919L);
				}
				PositionsById[record.PositionId] = record;
				RefreshLegacyCapacityFlag(record);
			}
		}
		if (source.DaoTaiBindings != null)
		{
			HashSet<string> occupiedSecondary = new HashSet<string>(StringComparer.Ordinal);
			foreach (XjDaoTaiPositionBindingArchiveRecord binding in source.DaoTaiBindings)
			{
				if (binding == null || binding.ActorId <= 0L) continue;
				binding.PrimaryPositionId = XjGuoWeiCalculator.NormalizeGuoWeiName(binding.PrimaryPositionId);
				binding.SecondaryPositionId = XjGuoWeiCalculator.NormalizeGuoWeiName(binding.SecondaryPositionId);
				if (!TryGetPosition(binding.PrimaryPositionId, out XjDerivedPositionArchiveRecord primary)
					|| primary == null
					|| !TryGetPosition(binding.SecondaryPositionId, out XjDerivedPositionArchiveRecord secondary)
					|| secondary == null
					|| !XjDaoTaiDualPositionSystem.IsComplementaryPair(primary, secondary)
					|| !XjDaoTaiDualPositionSystem.IsDaoTuCompatibleForDualPosition(primary, secondary)
					|| XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(secondary.DaoTu, secondary.PositionType, secondary.PositionId)
					|| !occupiedSecondary.Add(binding.SecondaryPositionId)) continue;
				// 旧档已成立双位属于既有持位，不因当前道势下滑而被导入阶段抹除。
				binding.SecondaryKind = secondary.PositionType;
				DaoTaiBindingsByActorId[binding.ActorId] = binding;
			}
			if (DaoTaiBindingsByActorId.Count > 0) _bindingRevision++;
		}
		XjTaiYinHiddenFruitSystem.ImportState(source.TaiYinHiddenFruit);
		XjYinYangRarePhenomenaSystem.ImportState(source.YinYangRarePhenomena);
	}

	internal static void Clear()
	{
		_lastUpdatedYear = 0;
		MomentumByDaoTu.Clear();
		PositionsById.Clear();
		if (DaoTaiBindingsByActorId.Count > 0) _bindingRevision++;
		DaoTaiBindingsByActorId.Clear();
		XjTaiYinHiddenFruitSystem.Clear();
	}

	private static void RefreshLegacyCapacityFlags()
	{
		foreach (XjDerivedPositionArchiveRecord record in PositionsById.Values) RefreshLegacyCapacityFlag(record);
	}

	private static void RefreshLegacyCapacityFlag(XjDerivedPositionArchiveRecord record)
	{
		if (record == null) return;
		int capacity = ResolveSlotCount(record.DaoTu, record.PositionType);
		record.LegacyBeyondCapacity = record.SlotIndex > Math.Max(0, capacity);
	}

	private static XjDerivedPositionArchiveRecord Clone(XjDerivedPositionArchiveRecord source)
	{
		return new XjDerivedPositionArchiveRecord
		{
			PositionId = source.PositionId,
			DaoTu = source.DaoTu,
			PositionType = source.PositionType,
			SlotIndex = source.SlotIndex,
			PrimaryAuthority = source.PrimaryAuthority,
			SecondaryAuthority = source.SecondaryAuthority,
			ExternalDaoTu = source.ExternalDaoTu,
			DerivedAuthority = source.DerivedAuthority,
			SecondaryDerivedAuthority = source.SecondaryDerivedAuthority,
			JinXingName = source.JinXingName,
			FounderActorId = source.FounderActorId,
			FounderName = source.FounderName,
			FoundedYear = source.FoundedYear,
			LastHolderActorId = source.LastHolderActorId,
			LastHolderDisplay = source.LastHolderDisplay,
			LastHolderDaoHui = source.LastHolderDaoHui,
			LastHeldYear = source.LastHeldYear,
			NamingActorId = source.NamingActorId,
			NamingActorDisplay = source.NamingActorDisplay,
			NamingActorDaoHui = source.NamingActorDaoHui,
			NamingYear = source.NamingYear,
			RevealedHolderActorId = source.RevealedHolderActorId,
			RevealedByActorId = source.RevealedByActorId,
			RevealedYear = source.RevealedYear,
			LegacyBeyondCapacity = source.LegacyBeyondCapacity
		};
	}

	private static string Normalize(string value) => (value ?? string.Empty).Trim();
}

internal static class XjDerivedAuthorityNameBuilder
{
	private static readonly string[] ResidualVerbs = { "藏真", "养素", "回元", "承命", "返照", "摄神" };
	private static readonly string[] IntercalaryVerbs = { "旁通", "玄置", "转枢", "间行", "借化", "越序", "合机", "反证", "易象" };

	internal static string Build(
		string daoTu,
		string positionType,
		string primaryAuthority,
		string secondaryAuthority,
		string externalDaoTu,
		int slot,
		long seed)
	{
		string source = TrimAuthority(primaryAuthority);
		string auxiliary = TrimAuthority(secondaryAuthority);
		if (string.Equals(positionType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			return "统摄六权";
		}
		if (string.Equals(positionType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
		{
			string verb = ResidualVerbs[XjDeterministicHash.PositiveIndex(seed + slot, daoTu + "|residual-verb", ResidualVerbs.Length)];
			return source + "·" + verb + (auxiliary.Length > 0 ? "（兼" + auxiliary + "）" : string.Empty);
		}
		string runVerb = IntercalaryVerbs[XjDeterministicHash.PositiveIndex(seed + slot, daoTu + "|intercalary-verb", IntercalaryVerbs.Length)];
		string external = string.IsNullOrWhiteSpace(externalDaoTu) ? "外道" : externalDaoTu.Trim();
		return source + "·" + runVerb + external + (auxiliary.Length > 0 ? "（辅" + auxiliary + "）" : string.Empty);
	}

	private static string TrimAuthority(string value)
	{
		string normalized = (value ?? string.Empty).Trim();
		return normalized.Length <= 4 ? normalized : normalized.Substring(0, 4);
	}
}
