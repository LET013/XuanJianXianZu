using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Death;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修死亡事务只在确认死亡时运行一次。真灵未灭者登记归返其金地，真灵俱灭者
/// 才释放全部依附并触发怜愍绝命；不进行常驻扫描。
/// </summary>
internal static class XjShiDeathLifecycle
{
	internal static void OnConfirmedDeath(Actor actor, in XjDeathSnapshot deathSnapshot, XjDeathCause cause)
	{
		if (actor?.data == null || !deathSnapshot.Found || deathSnapshot.ActorId <= 0L
			|| !XjCultivationPathRules.IsShi(actor)
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shi))
		{
			return;
		}

		XjShiEntrySystem.InvalidatePresence();
		int year = Math.Max(1, deathSnapshot.Year);
		bool voluntaryMoHeReincarnation = cause == XjDeathCause.ShiVoluntaryReincarnation;
		int realmRank = XjShiCatalog.GetRank(shi.Realm);
		bool isModern = string.Equals(shi.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		// 主动轮回是同一真灵自行舍身，绝不能被旧身上的锁定/死亡标记误判为真灵俱灭。
		bool trueSpiritAnnihilated = !voluntaryMoHeReincarnation && (cause == XjDeathCause.ScriptedFinality
			|| cause == XjDeathCause.TechnicalRemoval
			|| XjShiHighRealmSystem.IsTrueSpiritLocked(actor, year));
		// 正常战斗也要服从境界差：摩诃等同紫府，被完整高一大境界的敌手击杀时，
		// 真灵不再默认百分百逃脱。该判定只在死亡事务执行一次，不进入每击热路径。
		if (!trueSpiritAnnihilated
			&& XjShiTrueSpiritAnnihilationPolicy.ShouldAnnihilateMoHe(
				deathSnapshot, shi, cause, year, out _))
		{
			trueSpiritAnnihilated = true;
		}
		string domainId = string.IsNullOrWhiteSpace(shi.DomainId) ? shi.JinDiId : shi.DomainId;
		if (isModern) domainId = XjZhantanlinSystem.ResolvePreferredAnchor(actor, domainId, year);
		if (!XjShiDomainState.TryGet(domainId, out _))
		{
			if (string.Equals(shi.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
				&& realmRank < XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			{
				// 今释低位、怜愍与摩诃在旧档锚点缺失时，统一回落到优先服务器旃檀林。
				domainId = XjShiDomainState.EnsureZhantanlin(year).DomainId;
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, domainId);
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, domainId);
				XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, domainId);
			}
			else
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
				domainId = XjShiDomainState.EnsureLegacyRebirthDomain(actor, domainId,
					shi.Tradition, lineageId, year);
			}
		}

		// 先结算异脉击杀命数，再处理真灵归返，避免入口改写死者法脉快照。
		XjShiExpansionSystem.OnHighShiKilled(actor, deathSnapshot, shi);
		// 真君投释前的果／余／闰位在第一次确认死亡时永久沉入其释土。
		XjShiFruitPositionLockSystem.OnShiConfirmedDeath(
			actor, deathSnapshot.ActorId, domainId, year);
		bool returnQueued = false;
		if (isModern && !trueSpiritAnnihilated)
		{
			// 主动轮回的载荷在死亡前已经登记；这里不得用“强制归返”覆盖，也不能因一次索引抖动
			// 把直属怜愍判成失去座主。
			returnQueued = voluntaryMoHeReincarnation
				? XjReincarnation.HasPendingShi(deathSnapshot.ActorId)
				: XjReincarnation.RecordForcedShi(actor, deathSnapshot)
					|| XjReincarnation.HasPendingShi(deathSnapshot.ActorId);
			if (voluntaryMoHeReincarnation) returnQueued = true;
		}
		else
		{
			// 古释不依赖释土服务器；真灵俱灭的今释同样不得保留旧归返记录。
			XjReincarnation.CancelPending(deathSnapshot.ActorId);
		}

		string anchorName = XjShiDomainState.TryGet(domainId, out XjShiDomainRecord returnDomain)
			? XjShiDomainCatalog.GetDomainDisplayName(returnDomain) : "所系承载地";
		if (trueSpiritAnnihilated
			&& cause != XjDeathCause.TechnicalRemoval
			&& realmRank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			XjShiAnnouncementSystem.OnHighRealmTrueSpiritAnnihilated(
				deathSnapshot.Name,
				XjShiCatalog.GetRealmDisplay(shi.Realm),
				deathSnapshot.LastAttackerName);
		}
		if (isModern)
		{
			XjShiAnnouncementSystem.OnTrueSpiritResult(deathSnapshot.Name,
				returned: returnQueued && !trueSpiritAnnihilated,
				annihilated: trueSpiritAnnihilated, anchorName: anchorName,
				attackerName: deathSnapshot.LastAttackerName);
			XjThreeBookWriter.RecordShiTrueSpiritResult(actor, year,
				returned: returnQueued && !trueSpiritAnnihilated,
				annihilated: trueSpiritAnnihilated, anchorName: anchorName);
		}
		else if (cause == XjDeathCause.NaturalOldAge)
		{
			// 原著古释寿尽不转世，以“证毕归空”收束其一世修证。
			XjShiAnnouncementSystem.OnAncientReturnToVoid(deathSnapshot.Name);
			XjThreeBookWriter.RecordShiReturnToVoid(actor, year);
		}

		if (realmRank < XjShiCatalog.GetRank(XjShiRealmIds.LianMin))
		{
			XjShiWorldRegistry.Invalidate();
			return;
		}

		// 怜愍自身归返时释放旧肉身的依附索引；新肉身在挂靠金地重塑后再接回同一摩诃。
		if (string.Equals(shi.Realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
		{
			XjShiWorldRegistry.ReleaseAttachment(deathSnapshot.ActorId, year);
			XjShiWorldRegistry.Invalidate();
			return;
		}

		if (realmRank < XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			XjShiWorldRegistry.Invalidate();
			return;
		}

		// 依附关系已有年度索引；复制当前座下列表后再执行释放／绝命，避免摩诃死亡时
		// 重新扫描全部修士，也避免遍历期间修改索引集合。
		List<long> dependentIds = new List<long>(
			XjShiWorldRegistry.GetDependentIds(deathSnapshot.ActorId, year));
		XjShiDomainState.MarkActorDeath(deathSnapshot.ActorId, domainId, returnQueued, year);
		for (int i = 0; i < dependentIds.Count; i++)
		{
			long dependentId = dependentIds[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(dependentId, out Actor dependent)
				|| dependent?.data == null || !dependent.isAlive())
			{
				continue;
			}

			XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowedPower, 0);
			XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
			if (returnQueued && !trueSpiritAnnihilated)
			{
				// 摩诃只是返回所挂靠金地重塑时，直属怜愍不改投、不绝命，只等待同一真灵归返。
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus,
					XjShiPositionStatusIds.ReincarnationReserved);
				XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiJinDiStatus,
					XjShiJinDiStatusIds.WaitingForRebirth);
				XjWorldHistoryStore.RecordActorEvent(dependent,
					"直属摩诃真灵已归返所系承载地，暂收借力，静候同一座主重塑肉身。",
					XjShiTraitIds.LianMin);
				continue;
			}

			// 只有座主真灵俱灭，怜愍才按既定依附规则同时绝命。
			XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiTrueSpiritLocked, 1);
			XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiTrueSpiritLockUntilYear, 0);
			XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiTrueSpiritLockReason,
				"直属摩诃真灵俱灭，所借格位与性命同时断绝");
			XjShiWorldRegistry.ReleaseAttachment(dependentId, year);
			XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus, XjShiPositionStatusIds.Orphaned);
			XjWorldHistoryStore.RecordActorEvent(dependent,
				"直属摩诃真灵俱灭，借来之位与性命一并崩散，绝命于座下。",
				XjShiTraitIds.LianMin);
			XjVanillaDeathGuard.TryExecuteForceDeath(
				dependent, (AttackType)11, true, XjDeathCause.ScriptedFinality);
		}
		XjShiWorldRegistry.Invalidate();
	}


}
