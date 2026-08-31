using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.DongTian;

/// <summary>
/// 水月照真洞天是玄鉴渊照洞真道尊空证后的永久隐居处，而不是“高境公共副本”。
/// 0.9.8.17 起彻底分成两个层级：
/// 1）洞天门户/采气源可以在地图上有地标，用来承载渊照先天之气；
/// 2）道尊本人永远只有 FounderPresenceId，没有 Actor、Building、Sprite、AI、坐标与可攻击实体。
///
/// 紫府、真人、金丹、真君羽士都不能凭境界、距离、宗门占领或洞天开放状态自行拜访。
/// 真正入洞只认 XjYuanZhaoFounderAudienceSystem 发出的“水月传召”，并通过一次性探索记录完成持久化。
/// </summary>
internal static partial class XjDongTianRegistry
{
	internal static bool RemoveYuanZhaoDongTianForTimelineMigration()
	{
		string recordId = GetYuanZhaoRecordId();
		if (!recordsById.TryGetValue(recordId, out XjDongTianRecord record)) return false;
		XjYuanZhaoCaiQiSource.RemoveForTimelineMigration(record.AnchorCityId);
		if (record.EntitySpawned) XjDongTianEntitySystem.MarkClosed(record.RecordId);
		XjAdventureRealmClaimSystem.RemoveForTimelineMigration(record.RecordId);
		recordsById.Remove(recordId);
		MarkRecordCacheDirty();
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool HasYuanZhaoDongTian()
	{
		return TryReadQiYuDongTianRecord(XjDongTianRules.YuanZhaoDongTianId, out _);
	}

	internal static bool HasYuanZhaoAudienceRecord(Actor actor)
	{
		return HasYuanZhaoAudienceRecord(actor, string.Empty);
	}

	/// <summary>
	/// 水月觐见按“缘由阶段”而不是按人物一刀切。一个真灵可以在真正不同的道途节点再次被召：
	/// 例如早年以渊照后学身份受过一次点法，后来亲证渊照正果，仍可再有一次“持果见创道者”。
	/// 但同一缘由不会因转世或改名重复刷取。reason为空时表示查询是否发生过任意新版觐见。
	/// </summary>
	internal static bool HasYuanZhaoAudienceRecord(Actor actor, string reason)
	{
		if (actor?.data == null
			|| !TryReadQiYuDongTianRecord(XjDongTianRules.YuanZhaoDongTianId, out XjDongTianRecord record)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		// 0.9.8.16及更早的“公共洞天探索”不算真正见过道尊，升级后仍允许得到新版水月传召。
		// 0.9.8.17起按缘由锁定：同一真灵的“后学点法 / 持果照位 / 源道答疑 / 道争问契”各自至多一次。
		if (HasYuanZhaoAudienceActorId(record, actorId, reason)) return true;
		long lineageActorId = actorId;
		for (int depth = 0; depth < 32
			&& XjReincarnation.TryGetReincarnationSourceActorId(lineageActorId, out long sourceActorId); depth++)
		{
			if (sourceActorId <= 0L || sourceActorId == lineageActorId) break;
			if (HasYuanZhaoAudienceActorId(record, sourceActorId, reason)) return true;
			lineageActorId = sourceActorId;
		}
		return false;
	}

	private static bool HasYuanZhaoAudienceActorId(in XjDongTianRecord record, long actorId, string reason)
	{
		if (actorId <= 0L || record.ExplorerRecords == null) return false;
		string normalizedReason = (reason ?? string.Empty).Trim();
		for (int i = 0; i < record.ExplorerRecords.Count; i++)
		{
			XjQiYuDongTianExplorerRecord entry = record.ExplorerRecords[i];
			if (entry.ExplorerActorId != actorId || !entry.Resolved) continue;
			string type = entry.RewardType ?? string.Empty;
			bool anyNewAudience = type.StartsWith("YuanZhaoFounder", StringComparison.Ordinal)
				|| type.StartsWith("YuanZhaoAudience", StringComparison.Ordinal)
				|| string.Equals(type, "YuanZhaoZhengWeiIllumination", StringComparison.Ordinal)
				|| string.Equals(type, "YuanZhaoSourceDaoAnswer", StringComparison.Ordinal)
				|| string.Equals(type, "YuanZhaoAuthorityAnswer", StringComparison.Ordinal);
			if (!anyNewAudience) continue;
			if (normalizedReason.Length == 0) return true;

			if (string.Equals(normalizedReason, XjYuanZhaoFounderAudienceSystem.ReasonHeirZhengWeiAudience, StringComparison.Ordinal))
			{
				if (string.Equals(type, "YuanZhaoZhengWeiIllumination", StringComparison.Ordinal)) return true;
				continue;
			}
			if (string.Equals(normalizedReason, XjYuanZhaoFounderAudienceSystem.ReasonSourceDaoInquiry, StringComparison.Ordinal))
			{
				if (string.Equals(type, "YuanZhaoSourceDaoAnswer", StringComparison.Ordinal)) return true;
				continue;
			}
			if (string.Equals(normalizedReason, XjYuanZhaoFounderAudienceSystem.ReasonAuthorityArbitration, StringComparison.Ordinal))
			{
				if (string.Equals(type, "YuanZhaoAuthorityAnswer", StringComparison.Ordinal)) return true;
				continue;
			}
			if (string.Equals(normalizedReason, XjYuanZhaoFounderAudienceSystem.ReasonHeirAudience, StringComparison.Ordinal))
			{
				if (type.StartsWith("YuanZhaoFounderTeaching", StringComparison.Ordinal)
					|| string.Equals(type, "YuanZhaoAudienceAnswer", StringComparison.Ordinal)) return true;
			}
		}
		return false;
	}

	internal static bool EnsureYuanZhaoDongTian(int currentYear, bool announce)
	{
		if (currentYear <= 0) return false;
		if (TryReadQiYuDongTianRecord(XjDongTianRules.YuanZhaoDongTianId, out XjDongTianRecord existing))
		{
			// 旧档可能还保存“宗门属地/争夺”状态，以及把某个尘世角色记成洞天发现者。
			// 本版统一迁成私域记录：门户仍有地表位置，但没有发现者、主人或公开探索席位。
			XjAdventureRealmClaimSystem.RemoveForTimelineMigration(existing.RecordId);
			XjDongTianRecord normalized = new XjDongTianRecord(
				true, existing.RecordId, XjDongTianRules.YuanZhaoDongTianId, "渊照", "渊照,太阴,坎水", "水月照真",
				existing.CreatedYear, 0, 0, existing.ExploredActorCount, 0, true,
				XjDongTianRules.FormatDeathRateProfileSummary(XjDongTianRules.YuanZhaoDongTianId),
				XjDongTianRules.FormatRewardPoolSummary(XjDongTianRules.YuanZhaoDongTianId),
				"水月照真洞天（渊照）·玄鉴渊照洞真道尊隐居之所。门户只承渊照先天之气与水月异象；无传召者纵临门前，所见亦不过静水月影。",
				0L, string.Empty, existing.AnchorTileX, existing.AnchorTileY, existing.AnchorCityId, existing.AnchorCityName,
				existing.AnchorYear, "YuanZhaoLegacy", existing.LastExploreReserveYear, existing.ExplorerRecords,
				existing.EntityAssetId, existing.EntityBuildingId, existing.EntitySpawned, false);
			recordsById[existing.RecordId] = normalized;
			MarkRecordCacheDirty();
			XjDongTianEntitySystem.Tick(new[] { normalized }, 1);
			bool existingSourceReady = XjYuanZhaoCaiQiSource.Ensure(normalized.AnchorCityId, normalized.RecordId);
			if (existingSourceReady && announce) BroadcastYuanZhaoLegacyManifest(normalized.AnchorCityName);
			return existingSourceReady;
		}
		if (!XjDongTianRules.TryResolveCatalogEntry(XjDongTianRules.YuanZhaoDongTianId, out XjDongTianCatalogEntry entry)) return false;
		if (!TryFindActivityAnchor(currentYear, out XjQiYuDongTianActivityAnchor anchor) || anchor.CityId <= 0L) return false;

		string recordId = GetYuanZhaoRecordId();
		XjDongTianRecord record = new XjDongTianRecord(
			true,
			recordId,
			entry.QiYuDongTianId,
			entry.DaoTuGroup,
			entry.RelatedDaoTuIds,
			entry.DisplayName,
			currentYear,
			0, // 永久存在，不走五年关闭。
			0, // 不存在公共探索席位。
			0,
			0,
			true,
			XjDongTianRules.FormatDeathRateProfileSummary(entry.QiYuDongTianId),
			XjDongTianRules.FormatRewardPoolSummary(entry.QiYuDongTianId),
			"水月照真洞天（渊照）·玄鉴渊照洞真道尊空证后隐居之所。其门不因修为高下而启，唯应水月传召；无缘者所见，不过一泓静水与月下空山。",
			0L, // 门户只是地图锚点，不把任何尘世 Actor 记作“发现者/道尊代理”。
			string.Empty,
			anchor.TileX,
			anchor.TileY,
			anchor.CityId,
			anchor.CityName,
			currentYear,
			"YuanZhaoLegacy",
			0,
			Array.Empty<XjQiYuDongTianExplorerRecord>());

		recordsById[recordId] = record;
		bool sourceReady = XjYuanZhaoCaiQiSource.Ensure(record.AnchorCityId, record.RecordId);
		MarkRecordCacheDirty();
		// 不调用 OnRealmCreated：水月照真永远没有国家/宗门属地、争夺窗口与宗门远征权。
		XjDongTianEntitySystem.Tick(new[] { record }, 1);
		XjWorldArchiveSystem.MarkChanged();

		if (sourceReady && announce) BroadcastYuanZhaoLegacyManifest(anchor.CityName);
		return sourceReady;
	}

	private static string GetYuanZhaoRecordId() => "qiyu_dongtian_" + XjDongTianRules.YuanZhaoDongTianId;

	private static void BroadcastYuanZhaoLegacyManifest(string cityName)
	{
		string location = string.IsNullOrWhiteSpace(cityName) ? "世间" : cityName.Trim();
		XjBroadcastSystem.BroadcastBLevelWorldEvent(
			"【洞天现世】水月照真洞天于" + location + "附近显门。门户虽在尘世，却只为渊照先天之气留一处交界；洞中真身不显，国朝、宗门与高境威势皆不能强叩其门。唯受【水月传召】者，方可循自身倒影越过门户因果而入。",
			XjEventIconCatalog.DongTianOpen,
			XjAnnouncementCategory.DongTian);
	}

	/// <summary>
	/// 水月照真的探索记录不是“冒险结算”，而是一次觐见事务。这里必须先于通用洞天死亡判定执行：
	/// 受邀者不会因为进道尊私域而掷一个随机死亡骰；若邀请失效，只记作缘散，不伤人物。
	/// </summary>
	private static XjQiYuDongTianExplorerRecord ResolveYuanZhaoAudienceRecord(
		XjDongTianRecord record,
		XjQiYuDongTianExplorerRecord explorerRecord,
		int currentYear)
	{
		if (!TryResolveKnownActor(explorerRecord.ExplorerActorId, out Actor actor)
			|| actor?.data == null
			|| !XjSafeCore.IsAliveActor(actor))
		{
			return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, false,
				"YuanZhaoAudienceLost", "水月传召随真灵远去，此次因缘自行散尽");
		}

		if (!XjYuanZhaoFounderAudienceSystem.TryGetAudienceReasonForResolution(actor, currentYear, out string reason))
		{
			return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, false,
				"YuanZhaoAudienceExpired", "水月不再映路，未能入洞；此缘自行散去");
		}

		string rewardSummary = ApplyYuanZhaoAudienceReward(actor, currentYear, reason, out string rewardType, out bool teaching);
		bool rewardApplied = !string.IsNullOrWhiteSpace(rewardSummary);
		if (!rewardApplied)
		{
			rewardSummary = "道尊只答一问，未另赐外物";
		}

		XjYuanZhaoFounderAudienceSystem.MarkAudienceResolved(actor, currentYear, teaching);
		string actorName = SafeActorName(actor);
		string encounterText = BuildYuanZhaoAudienceEncounterText(actorName, reason, rewardSummary);
		string eventTitle = ResolveYuanZhaoAudienceEventTitle(reason, rewardType);
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			eventTitle + encounterText,
			iconId: XjEventIconCatalog.DongTianOpen,
			category: XjAnnouncementCategory.DongTian);
		RecordYuanZhaoAudienceHistory(actor, record, currentYear, reason, encounterText, teaching);

		return BuildResolvedExplorerRecord(explorerRecord, currentYear, false, rewardApplied, rewardType, rewardSummary);
	}

	private static string ApplyYuanZhaoAudienceReward(
		Actor actor,
		int currentYear,
		string reason,
		out string rewardType,
		out bool teaching)
	{
		rewardType = "YuanZhaoAudienceAnswer";
		teaching = false;
		if (actor?.data == null) return string.Empty;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		bool ownYuanZhao = string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal);

		// 承正果后来见创道者：不送第二个果、不塞权柄，只“照见自己所持之位”。
		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirZhengWeiAudience, StringComparison.Ordinal)
			&& ownYuanZhao)
		{
			teaching = true;
			rewardType = "YuanZhaoZhengWeiIllumination";
			string daoHui = ApplyHuiGuangReward(actor, 5f);
			return (string.IsNullOrWhiteSpace(daoHui) ? string.Empty : daoHui + "；")
				+ "水月反照正果与自身道统之间的一处缺隙，此后仍须本人自行补全";
		}

		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirAudience, StringComparison.Ordinal)
			&& ownYuanZhao)
		{
			teaching = true;
			long actorId = ((BaseSystemData)actor.data).id;
			if (XjCultivationPathRules.IsZiFuJinDan(actor)
				&& XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
			{
				// 真正“授法”是低频关键点拨，不是洞天掉落池。只有渊照紫府在缺神通时才可能
				// 得一门直接传法；否则只得到参悟方向。
				int shenTongCount = XjXianJiAccessor.GetEffectiveShenTongCount(actor);
				if (shenTongCount < 5
					&& XjDeterministicHash.Roll01(actorId, currentYear, "yuanzhao_founder_teaching", daoTu) < 0.35f
					&& XjZiFuProgression.TryGrantEventDongTianShenTong(actor, currentYear, out string shenTongId))
				{
					rewardType = "YuanZhaoFounderTeachingShenTong";
					return "道尊隔水点去一处错解，直接悟得渊照神通“" + shenTongId + "”";
				}
				XjEventDongTianBonusService.GrantShenTongComprehensionBenefit(actor, currentYear);
				rewardType = "YuanZhaoFounderTeachingInsight";
				return "道尊只改一句法义，得五十年神通参悟助力";
			}

			if (XjCultivationPathRules.IsFuQiYangXing(actor)
				&& XjCultivationPathRules.IsZhenRenEquivalentRealm(realmId))
			{
				if (TryShortenFuQiProject(actor, currentYear, out int years, out string projectName))
				{
					rewardType = "YuanZhaoFounderTeachingFuQi";
					return "道尊指出" + projectName + "中一处反复自证的歧路，进程提前" + years + "年";
				}
				string daoHui = ApplyHuiGuangReward(actor, 5f);
				rewardType = "YuanZhaoFounderTeachingFuQiInsight";
				return string.IsNullOrWhiteSpace(daoHui) ? "得一段服气返照法义" : daoHui + "；得一段服气返照法义";
			}

			// 金丹/真君已经不是靠“送资源”成长的层次。这里只留下方向性点拨。
			if (XjCultivationPathRules.IsJinDanEquivalentRealm(realmId))
			{
				string daoHui = ApplyHuiGuangReward(actor, 3f);
				rewardType = "YuanZhaoFounderTeachingHighRealm";
				return (string.IsNullOrWhiteSpace(daoHui) ? string.Empty : daoHui + "；")
					+ "道尊令其自照一遍神通与果位来路，只点出一处最该先修的缺口";
			}
		}

		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonSourceDaoInquiry, StringComparison.Ordinal))
		{
			// 太阴/坎水是渊照源道，但不是道尊门人。得到的是“答疑”，不是改道统、送神通。
			rewardType = "YuanZhaoSourceDaoAnswer";
			string daoHui = ApplyHuiGuangReward(actor, 2f);
			return (string.IsNullOrWhiteSpace(daoHui) ? string.Empty : daoHui + "；")
				+ "道尊借水中一问反问其本心，使太阴/坎水源流中的疑处自行显形，不授渊照神通";
		}

		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonAuthorityArbitration, StringComparison.Ordinal))
		{
			rewardType = "YuanZhaoAuthorityAnswer";
			return "只照明所争权柄的来路与旧契，不替任何一方裁定胜负";
		}

		return string.Empty;
	}

	private static bool TryShortenFuQiProject(Actor actor, int currentYear, out int shortenedYears, out string projectName)
	{
		shortenedYears = 0;
		projectName = string.Empty;
		if (actor?.data == null || currentYear <= 0) return false;

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, out int nurtureYear)
			&& nurtureYear > currentYear + 1)
		{
			int next = Math.Max(currentYear + 1, nurtureYear - 15);
			shortenedYears = nurtureYear - next;
			if (shortenedYears > 0)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiJinXingNurtureCompleteYear, next);
				projectName = "金性温养";
				return true;
			}
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, out int perfectYear)
			&& perfectYear > currentYear + 1)
		{
			int next = Math.Max(currentYear + 1, perfectYear - 15);
			shortenedYears = perfectYear - next;
			if (shortenedYears > 0)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiPerfectionProjectCompleteYear, next);
				projectName = "神妙圆满";
				return true;
			}
		}

		return false;
	}

	private static string ResolveYuanZhaoAudienceEventTitle(string reason, string rewardType)
	{
		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirZhengWeiAudience, StringComparison.Ordinal))
			return "【照果问道】";
		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonSourceDaoInquiry, StringComparison.Ordinal))
			return "【源道答疑】";
		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonAuthorityArbitration, StringComparison.Ordinal))
			return "【问契照权】";
		if ((rewardType ?? string.Empty).StartsWith("YuanZhaoFounderTeaching", StringComparison.Ordinal))
			return "【道尊授法】";
		return "【入洞见道】";
	}

	private static string BuildYuanZhaoAudienceEncounterText(string actorName, string reason, string rewardSummary)
	{
		string opening = actorName + "循水中倒影入得水月照真。洞中无迎客之人，只有一方静渊、一轮月影与一句从四面八方同时落下的话。";
		string middle = string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirZhengWeiAudience, StringComparison.Ordinal)
			? "其以渊照正果来见，月影先照果、再照人，道尊只问其‘所持者究竟是谁之道’。"
			: string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirAudience, StringComparison.Ordinal)
				? "其以渊照后学之身来见，道尊不问姓名门第，只令其将自以为最明白的一段法重新说一遍。"
				: string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonSourceDaoInquiry, StringComparison.Ordinal)
					? "其因太阴/坎水源流疑处而来，道尊不越俎代庖，只把问题反照回其本道。"
					: "水月只照权柄旧契与来路，不替天地判胜负。";
		return opening + middle + "临去时，水月自合：" + rewardSummary + "。临去时只闻水声，道尊真身始终未显。";
	}

	private static void RecordYuanZhaoAudienceHistory(
		Actor actor,
		in XjDongTianRecord record,
		int currentYear,
		string reason,
		string detail,
		bool teaching)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		string actorName = SafeActorName(actor);
		string eventType = teaching ? "YuanZhaoFounderTeaching" : "YuanZhaoFounderAudience";
		if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonHeirZhengWeiAudience, StringComparison.Ordinal))
			eventType = "YuanZhaoZhengWeiAudience";
		else if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonSourceDaoInquiry, StringComparison.Ordinal))
			eventType = "YuanZhaoSourceDaoInquiry";
		else if (string.Equals(reason, XjYuanZhaoFounderAudienceSystem.ReasonAuthorityArbitration, StringComparison.Ordinal))
			eventType = "YuanZhaoAuthorityArbitrationAudience";

		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			actorName + "入水月照真",
			detail,
			teaching ? 4 : 3,
			actorId: actorId,
			actorName: actorName,
			cityId: record.AnchorCityId,
			year: currentYear,
			locationX: record.AnchorTileX,
			locationY: record.AnchorTileY,
			eventType: eventType,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.CenturyCandidate),
			mirrorToWorldLog: false);
	}
}
