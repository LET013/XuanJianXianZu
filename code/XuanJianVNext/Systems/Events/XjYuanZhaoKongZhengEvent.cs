using System;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// 渊照空证者的隐世人物锚点。道尊不再作为地图活动 Actor 参与 AI、寿尽或战斗，
/// 而是在空证后永久藏身水月照真洞天；后续世界事件可用 PresenceId 稳定引用“此人”，
/// 需要洞天实体位置时再通过 DongTianRecordId / DongTianId 查询当前显化记录。
/// </summary>
internal readonly struct XjYuanZhaoFounderPresence
{
	internal readonly bool Found;
	internal readonly string PresenceId;
	internal readonly string Name;
	internal readonly string DaoTu;
	internal readonly string DongTianId;
	internal readonly string DongTianRecordId;
	internal readonly string DongTianName;
	internal readonly int HiddenSinceYear;
	internal readonly bool DongTianManifested;

	internal XjYuanZhaoFounderPresence(
		bool found,
		string presenceId,
		string name,
		string daoTu,
		string dongTianId,
		string dongTianRecordId,
		string dongTianName,
		int hiddenSinceYear,
		bool dongTianManifested)
	{
		Found = found;
		PresenceId = presenceId ?? string.Empty;
		Name = name ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		DongTianId = dongTianId ?? string.Empty;
		DongTianRecordId = dongTianRecordId ?? string.Empty;
		DongTianName = dongTianName ?? string.Empty;
		HiddenSinceYear = Math.Max(0, hiddenSinceYear);
		DongTianManifested = dongTianManifested;
	}
}

/// <summary>
/// “渊照空证”的世界级一次性节点。触发时点以本世界第一次固化的纪年起点为准：
/// 玄鉴历第500年。该基准写入事件档案后永不随读档重算。
/// 触发前太阴、坎水正果隐藏封锁；真正执行空证事务后两果释放，新生渊照正果落入天地位序并可正常求证。
/// 空证者不再远遁天外，而是当场隐入水月照真洞天，作为永久隐世人物锚点保留；
/// 后续事件可稳定引用该人物。水月照真洞天同时是渊照先天之气唯一来源：五百年节点当年若没有
/// 合法尘世锚点，只延后“洞天门户显化”，不改变道尊已藏身其中这一事实；以后按年度O(1)重试。
/// </summary>
internal static class XjYuanZhaoKongZhengEvent
{
	internal const int DelayYears = 500;
	internal const string FounderName = "玄鉴渊照洞真道尊";
	internal const string FounderPresenceId = "yuanzhao_founder_xuanjian_dongzhen";
	internal const string DaoTu = "渊照";
	internal const string SourceTaiYin = "太阴";
	internal const string SourceKanShui = "坎水";
	internal const string LegacyDongTianName = "水月照真洞天";

	private static XjYuanZhaoKongZhengEventArchiveData _state = new XjYuanZhaoKongZhengEventArchiveData();

	internal static bool IsTriggered => _state?.Triggered ?? false;
	internal static int BaseWorldYear => Math.Max(0, _state?.BaseWorldYear ?? 0);
	internal static int ScheduledTriggerYear => Math.Max(0, _state?.ScheduledTriggerYear ?? 0);
	internal static bool IsLegacyDongTianReady => _state?.LegacyDongTianCreated ?? false;
	internal static int TriggeredYear => Math.Max(0, _state?.TriggeredYear ?? 0);
	internal static int FounderLastAudienceInviteYear => Math.Max(0, _state?.LastAudienceInviteYear ?? 0);
	internal static int FounderLastProjectionYear => Math.Max(0, _state?.LastProjectionYear ?? 0);
	internal static int FounderLastAuthorityInterventionYear => Math.Max(0, _state?.LastAuthorityInterventionYear ?? 0);
	internal static int FounderNextCredentialYear => Math.Max(0, _state?.NextCredentialYear ?? 0);
	internal static int FounderLastCredentialYear => Math.Max(0, _state?.LastCredentialYear ?? 0);
	internal static int FounderTotalCredentialIssued => Math.Max(0, _state?.TotalCredentialIssued ?? 0);
	internal static int FounderTotalCredentialResolved => Math.Max(0, _state?.TotalCredentialResolved ?? 0);

	internal static bool TryGetActiveFounderCredential(out long actorId, out int untilYear)
	{
		actorId = Math.Max(0L, _state?.ActiveCredentialActorId ?? 0L);
		untilYear = Math.Max(0, _state?.ActiveCredentialUntilYear ?? 0);
		return actorId > 0L && untilYear > 0;
	}

	internal static void EnsureFounderCredentialSchedule(int nextYear)
	{
		if (nextYear <= 0) return;
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.CredentialSchemaVersion = Math.Max(1, _state.CredentialSchemaVersion);
		if (_state.NextCredentialYear > 0) return;
		_state.NextCredentialYear = nextYear;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void DelayFounderCredentialSchedule(int nextYear)
	{
		if (nextYear <= 0) return;
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.CredentialSchemaVersion = Math.Max(1, _state.CredentialSchemaVersion);
		_state.NextCredentialYear = nextYear;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void RecordFounderCredentialIssued(long actorId, string actorName, int issueYear, int untilYear, int nextYear)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.CredentialSchemaVersion = Math.Max(1, _state.CredentialSchemaVersion);
		_state.LastCredentialYear = Math.Max(0, issueYear);
		_state.ActiveCredentialActorId = Math.Max(0L, actorId);
		_state.ActiveCredentialUntilYear = Math.Max(0, untilYear);
		_state.NextCredentialYear = Math.Max(0, nextYear);
		_state.TotalCredentialIssued = Math.Max(0, _state.TotalCredentialIssued) + 1;
		_state.LastCredentialHolderName = actorName?.Trim() ?? string.Empty;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void ClearActiveFounderCredential(long expectedActorId = 0L)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		if (expectedActorId > 0L && _state.ActiveCredentialActorId > 0L && _state.ActiveCredentialActorId != expectedActorId) return;
		if (_state.ActiveCredentialActorId <= 0L && _state.ActiveCredentialUntilYear <= 0) return;
		_state.ActiveCredentialActorId = 0L;
		_state.ActiveCredentialUntilYear = 0;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void RecordFounderCredentialResolved(long actorId)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.CredentialSchemaVersion = Math.Max(1, _state.CredentialSchemaVersion);
		_state.TotalCredentialResolved = Math.Max(0, _state.TotalCredentialResolved) + 1;
		ClearActiveFounderCredential(actorId);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool TryGetPendingFounderAudience(out long actorId, out int untilYear, out string reason)
	{
		actorId = Math.Max(0L, _state?.PendingAudienceActorId ?? 0L);
		untilYear = Math.Max(0, _state?.PendingAudienceUntilYear ?? 0);
		reason = _state?.PendingAudienceReason ?? string.Empty;
		return actorId > 0L && untilYear > 0 && !string.IsNullOrWhiteSpace(reason);
	}

	internal static void SetPendingFounderAudience(long actorId, int inviteYear, int untilYear, string reason)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.FounderEventSchemaVersion = Math.Max(1, _state.FounderEventSchemaVersion);
		_state.LastAudienceInviteYear = Math.Max(0, inviteYear);
		_state.PendingAudienceActorId = Math.Max(0L, actorId);
		_state.PendingAudienceUntilYear = Math.Max(0, untilYear);
		_state.PendingAudienceReason = reason?.Trim() ?? string.Empty;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void ClearPendingFounderAudience(long expectedActorId = 0L)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		if (expectedActorId > 0L && _state.PendingAudienceActorId > 0L && _state.PendingAudienceActorId != expectedActorId) return;
		if (_state.PendingAudienceActorId <= 0L && _state.PendingAudienceUntilYear <= 0 && string.IsNullOrWhiteSpace(_state.PendingAudienceReason)) return;
		_state.PendingAudienceActorId = 0L;
		_state.PendingAudienceUntilYear = 0;
		_state.PendingAudienceReason = string.Empty;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void RecordFounderAudienceResolved(bool teaching)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		_state.FounderEventSchemaVersion = Math.Max(1, _state.FounderEventSchemaVersion);
		_state.TotalAudienceCount = Math.Max(0, _state.TotalAudienceCount) + 1;
		if (teaching) _state.TotalTeachingCount = Math.Max(0, _state.TotalTeachingCount) + 1;
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static bool TryReserveFounderProjection(int currentYear, int cooldownYears, bool authorityIntervention)
	{
		if (!IsTriggered || currentYear <= 0) return false;
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		int lastYear = authorityIntervention ? Math.Max(0, _state.LastAuthorityInterventionYear) : Math.Max(0, _state.LastProjectionYear);
		if (lastYear > 0 && currentYear < lastYear + Math.Max(1, cooldownYears)) return false;
		_state.FounderEventSchemaVersion = Math.Max(1, _state.FounderEventSchemaVersion);
		_state.LastProjectionYear = currentYear;
		if (authorityIntervention) _state.LastAuthorityInterventionYear = currentYear;
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	/// <summary>
	/// 后续渊照事件的统一人物入口。只要空证已经发生，道尊就视为一直藏身于
	/// 水月照真洞天；洞天尚未在尘世找到门户时 DongTianManifested=false，但人物锚点仍然有效。
	/// </summary>
	internal static bool TryGetFounderPresence(out XjYuanZhaoFounderPresence presence)
	{
		if (!IsTriggered)
		{
			presence = default;
			return false;
		}

		string dongTianId = XjDongTianRules.YuanZhaoDongTianId;
		presence = new XjYuanZhaoFounderPresence(
			true,
			FounderPresenceId,
			FounderName,
			DaoTu,
			dongTianId,
			"qiyu_dongtian_" + dongTianId,
			LegacyDongTianName,
			Math.Max(1, _state.TriggeredYear),
			IsLegacyDongTianReady);
		return true;
	}

	/// <summary>
	/// 兼容旧调用点的纯只读查询。时间轴初始化只允许发生在 bootstrap / 世界事件事务，
	/// UI、果位可用性或其它查询层即使未来重新调用本方法，也绝不能反向写 BaseWorldYear。
	/// 尚未固化时返回0，由调用方按“空证前”处理。
	/// </summary>
	internal static int GetScheduledTriggerYear(int currentYear)
	{
		return ScheduledTriggerYear;
	}

	internal static void ReconcileTimelineAfterLoad(int currentYear)
	{
		if (currentYear <= 0) return;
		EnsureTimelineInitialized(currentYear);
		MigrateConfiguredDelayIfNeeded();
		MigrateLegacyAbsoluteTimelineIfNeeded(currentYear);
		int triggerYear = Math.Max(1, _state.ScheduledTriggerYear);
		if (!_state.Triggered && currentYear >= triggerYear)
		{
			// 固定五百年节点不能因为年度主车道积压而迟到；旧档越过节点时读档即补触发。
			TickYear(currentYear);
			return;
		}
		if (_state.Triggered && currentYear >= triggerYear)
		{
			// 读档后把“事件已发生”派生出的显世、正位、洞天和采气源重新收口。
			// 若旧档从未成功留下洞天，而本次已有合法锚点，这就是第一次真实显化，
			// 允许补发一次B级【洞天现世】。
			EnsurePostTriggerState(currentYear, announceDeferredCave: currentYear > Math.Max(0, _state.TriggeredYear));
		}
	}

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0) return;
		// 年度热路径只初始化/迁移时间轴；读档专用 ReconcileTimelineAfterLoad 会额外做一次
		// 已触发洞天门户自愈，不能每年先调用一次再在下方重复 EnsurePostTriggerState。
		EnsureTimelineInitialized(currentYear);
		MigrateConfiguredDelayIfNeeded();
		MigrateLegacyAbsoluteTimelineIfNeeded(currentYear);

		int triggerYear = Math.Max(1, _state.ScheduledTriggerYear);
		if (currentYear < triggerYear) return;

		if (_state.Triggered)
		{
			// 洞天尚未落地时每年只做一次既有活动锚点O(1)查找；成功后年度热路径
			// 完全停止重试。读档/缓存重建仍由 ReconcileTimelineAfterLoad 单次自愈。
			if (!_state.LegacyDongTianCreated)
			{
				EnsurePostTriggerState(currentYear, announceDeferredCave: true);
			}
			return;
		}

		// 必须先把“空证已真实发生”写入权威事件状态，再切换果位封锁。
		// 这样同一玄鉴年中先执行的角色年度修炼仍只看见旧世界，避免提前解封。
		_state.Triggered = true;
		_state.TriggeredYear = currentYear;
		_state.LegacyBaseline = false;
		EnsurePostTriggerState(currentYear, announceDeferredCave: false);
		bool dongTianReady = _state.LegacyDongTianCreated;
		XjWorldArchiveSystem.MarkChanged();

		const string doctrine = "太阴有形而无定象，坎水无形而纳万象。今以水承月，以月照水，见影而知真，藏真而遗形——天地旧无此说，今始有之。";
		string caveClause = dongTianReady
			? "道尊遂敛尽世迹，藏身‘水月照真洞天’，自此不履尘世，非有因缘不显；洞天深处同时凝成渊照先天之气，以待后世诸修各证所得。"
			: "道尊遂敛尽世迹，隐入尚未向尘世显门的‘水月照真洞天’；洞天本身已成，只是门户暂无所托，待后世有可承其门之地才会显化，届时渊照先天之气亦可为世人所采。";
		string history = "玄鉴历" + XjChronology.ToXuanJianYear(currentYear) + "年，太阴垂象，坎水涵天。自仙鉴起录之年迄今五百载，太阴、坎水二正果皆隐于天机，世人求之只觉有主而不见其名；至此方知，二果皆为"
			+ FounderName + "所系。道尊以太阴之照与坎水之渊互证五百年，于水月相映之间脱出旧理，别开一道。其言曰：‘"
			+ doctrine + "’是日空证成，天地道网自此新增‘渊照’一途，与太阴、坎水相邻而自成其理；太阴、坎水二果同时脱身归天地，自此复开人间。新成渊照正果亦落入天地位序，道尊不占此果，空证后即藏身水月照真洞天；后世渊照之修若道行圆满、自成其法，仍可循此道求证正果。"
			+ caveClause;
		string tip = "【渊照空证】" + FounderName
			+ "以太阴、坎水二果互证五百年，于水月相映间空证‘渊照’；太阴、坎水正果自此复开，新生渊照正果亦归天地、可由后世求证。道尊不占此果，自此藏身水月照真洞天"
			+ (dongTianReady ? "，洞天门户亦于同日显世。" : "；洞天已隐成于水月之间，只待后世门户显化。");
		XjBroadcastSystem.BroadcastSLevelDomainEvent(
			XjWorldHistoryCategory.World,
			"YuanZhaoKongZheng",
			history,
			tip.Trim(),
			result: XjHistoryResult.Success,
			year: currentYear,
			color: "#8FB6D8",
			duration: 12f,
			iconId: XjEventIconCatalog.HistoryWorld);
	}

	private static void EnsureTimelineInitialized(int currentYear)
	{
		_state ??= new XjYuanZhaoKongZhengEventArchiveData();
		if (_state.BaseWorldYear > 0 && _state.ScheduledTriggerYear > _state.BaseWorldYear)
		{
			_state.Initialized = true;
			return;
		}

		XjCenturyAnnalsStore.TryEnsureBaseWorldYear(Math.Max(1, currentYear), out int baseWorldYear);
		baseWorldYear = Math.Max(1, baseWorldYear);
		_state.BaseWorldYear = baseWorldYear;
		_state.ScheduledTriggerYear = XjChronology.ToWorldYear(DelayYears, baseWorldYear);
		_state.Initialized = true;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static void MigrateConfiguredDelayIfNeeded()
	{
		if (_state == null || _state.Triggered) return;
		// 未触发的固定里程碑必须服从玄鉴历唯一基准。旧包曾各自持有 BaseWorldYear，
		// 一旦与百年世谱的起录年分叉，就会在高年份旧地图上提前或延后数千年。
		int authoritativeBase = XjChronology.BaseWorldYear;
		if (authoritativeBase > 0 && _state.BaseWorldYear != authoritativeBase)
		{
			_state.BaseWorldYear = authoritativeBase;
		}
		if (_state.BaseWorldYear <= 0) return;
		int configuredTriggerYear = XjChronology.ToWorldYear(DelayYears, _state.BaseWorldYear);
		if (_state.ScheduledTriggerYear == configuredTriggerYear) return;
		// HF4及更早存档可能已固化旧的1000年/500年口径；只重排尚未发生的时间轴。
		_state.ScheduledTriggerYear = configuredTriggerYear;
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	/// <summary>
	/// RC4~RC8曾以绝对玄历1000年建立渊照。若一个3141年才开始记录的世界在旧版被
	/// 提前补成“已发生”，升级后必须恢复到新的相对里程碑之前的真实状态。若当前已经超过新的
	/// 触发年，则保留既有成果，只把时间轴基准固化，不重复播报。
	/// </summary>
	private static void MigrateLegacyAbsoluteTimelineIfNeeded(int currentYear)
	{
		if (_state.TimelineMigrated) return;
		int triggerYear = Math.Max(1, _state.ScheduledTriggerYear);
		if (currentYear < triggerYear)
		{
			// 新时间轴尚未到点时，渊照及水月照真绝不能预先存在。无论RC8旧事件
			// 状态是否完整，都把可能遗留的洞天/显世/正位/采气索引一起撤回；这些
			// 清理入口均为幂等，不会影响太阴、坎水本身的修炼记录。
			XjDongTianRegistry.RemoveYuanZhaoDongTianForTimelineMigration();
			XjDaoTuManifestRegistry.ResetYuanZhaoTimelineForMigration();
			XjFruitPositionWorldState.RemovePrematureYuanZhaoZhengWeiForTimelineMigration();
			_state.Triggered = false;
			_state.TriggeredYear = 0;
			_state.LegacyBaseline = false;
			_state.DaoTuEstablished = false;
			_state.LegacyDongTianCreated = false;
			_state.LegacyDongTianCreatedYear = 0;
		}
		else if (_state.Triggered && _state.TriggeredYear != triggerYear)
		{
			// 世界已经跨过新节点，不把现有玩法倒回去；只把事件年份归一为本局时间轴。
			_state.TriggeredYear = triggerYear;
			_state.LegacyBaseline = true;
		}
		_state.TimelineMigrated = true;
		XjWorldArchiveSystem.MarkChanged();
	}

	/// <summary>
	/// 空证发生后的幂等收口。渊照“被发现”和“可采气”不是同一个条件：
	/// 道途与正果随空证立即成立，采气必须等唯一水月照真洞天真实存在。
	/// </summary>
	private static void EnsurePostTriggerState(int currentYear, bool announceDeferredCave)
	{
		if (!_state.Triggered) return;
		int safeYear = Math.Max(1, currentYear);
		XjDaoTuManifestRegistry.MarkDiscovered(XjDaoTuRootIds.YuanZhao, 0L, safeYear);
		XjFruitPositionWorldState.EnsurePosition(
			null,
			DaoTu,
			XjGuoWeiCalculator.ZhengWei,
			DaoTu + XjGuoWeiCalculator.ZhengWei,
			XjJinXingCalculator.CanonicalYuanZhaoJinXing,
			string.Empty,
			safeYear);
		_state.DaoTuEstablished = true;

		// “洞天记录存在”还不够：渊照真正可用必须连同唯一固定采气源一起成立。
		// 旧档可能保留洞天却丢了采气索引，因此延迟公告也以权威 readiness 状态判断，
		// 让采气源在后续自愈成功时仍能补发一次【洞天现世】。
		bool shouldAnnounce = announceDeferredCave
			&& !_state.LegacyDongTianCreated
			&& safeYear > Math.Max(0, _state.TriggeredYear);
		bool dongTianReady = XjDongTianRegistry.EnsureYuanZhaoDongTian(safeYear, announce: shouldAnnounce);
		if (dongTianReady)
		{
			// 只有洞天真实存在且固定采气索引已经确保后，后世修士才能自然接触/采渊照之气。
			XjDaoTuManifestRegistry.MarkCaiQiUnlocked(XjDaoTuRootIds.YuanZhao, 0L, safeYear);
			if (!_state.LegacyDongTianCreated || _state.LegacyDongTianCreatedYear <= 0)
			{
				_state.LegacyDongTianCreatedYear = safeYear;
			}
			_state.LegacyDongTianCreated = true;
		}
		else
		{
			// 旧档可能曾因“有渊照正位”被错误反推为采气已开放；在洞天缺失时重新关回去，
			// 防止新角色选中渊照后永久卡在 YuanZhaoLegacySiteMissing。
			XjDaoTuManifestRegistry.LockYuanZhaoCaiQiUntilLegacyReady();
			_state.LegacyDongTianCreated = false;
			_state.LegacyDongTianCreatedYear = 0;
		}
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static XjYuanZhaoKongZhengEventArchiveData ExportState() => new XjYuanZhaoKongZhengEventArchiveData
	{
		Initialized = _state.Initialized,
		BaseWorldYear = _state.BaseWorldYear,
		ScheduledTriggerYear = _state.ScheduledTriggerYear,
		TimelineMigrated = _state.TimelineMigrated,
		Triggered = _state.Triggered,
		LegacyBaseline = _state.LegacyBaseline,
		TriggeredYear = _state.TriggeredYear,
		DaoTuEstablished = _state.DaoTuEstablished,
		LegacyDongTianCreated = _state.LegacyDongTianCreated,
		LegacyDongTianCreatedYear = _state.LegacyDongTianCreatedYear,
		FounderEventSchemaVersion = _state.FounderEventSchemaVersion,
		LastAudienceInviteYear = _state.LastAudienceInviteYear,
		PendingAudienceActorId = _state.PendingAudienceActorId,
		PendingAudienceUntilYear = _state.PendingAudienceUntilYear,
		PendingAudienceReason = _state.PendingAudienceReason,
		TotalAudienceCount = _state.TotalAudienceCount,
		TotalTeachingCount = _state.TotalTeachingCount,
		LastProjectionYear = _state.LastProjectionYear,
		LastAuthorityInterventionYear = _state.LastAuthorityInterventionYear,
		CredentialSchemaVersion = _state.CredentialSchemaVersion,
		NextCredentialYear = _state.NextCredentialYear,
		LastCredentialYear = _state.LastCredentialYear,
		ActiveCredentialActorId = _state.ActiveCredentialActorId,
		ActiveCredentialUntilYear = _state.ActiveCredentialUntilYear,
		TotalCredentialIssued = _state.TotalCredentialIssued,
		TotalCredentialResolved = _state.TotalCredentialResolved,
		LastCredentialHolderName = _state.LastCredentialHolderName
	};

	internal static void ImportState(XjYuanZhaoKongZhengEventArchiveData state)
	{
		_state = state ?? new XjYuanZhaoKongZhengEventArchiveData();
	}

	internal static void Clear() => _state = new XjYuanZhaoKongZhengEventArchiveData();
}
