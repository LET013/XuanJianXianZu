using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.HighRealm;

internal sealed class XjTaiYinHiddenFruitArchiveData
{
	public int SchemaVersion { get; set; } = 1;
	public long VeilHolderActorId { get; set; }
	public string VeiledPositionId { get; set; } = string.Empty;
	public int VeiledYear { get; set; }
	public long LegacyFamilyId { get; set; }
	public string LegacyFamilyName { get; set; } = string.Empty;
	public long LegacySourceActorId { get; set; }
	public string LegacySourceName { get; set; } = string.Empty;
	public int LegacyHiddenYear { get; set; }
}

/// <summary>
/// 太阴藏果只保存一条世界级遮蔽关系：藏其迹，不改其位。
/// 已有果位的真实持有者、权柄与战斗结算完全不变；空果被藏后只是不再向外界求位开放。
/// 太阴持果者死亡时可将太阴正果留作家族幽契，只有同一稳定家族的后人能正常承果。
/// 全部入口均由证位/离位/死亡事件驱动，不增加年度扫描。
/// </summary>
internal static class XjTaiYinHiddenFruitSystem
{
	internal const string TaiYinDaoTu = "太阴";
	internal static readonly string TaiYinZhengWei = XjGuoWeiCalculator.BuildGuoWeiSlotName(TaiYinDaoTu, XjGuoWeiCalculator.ZhengWei, 1);
	private static XjTaiYinHiddenFruitArchiveData _state = new XjTaiYinHiddenFruitArchiveData();
	private static int _revision = 1;

	internal static int Revision => _revision;
	internal static bool HasActiveVeil => _state != null && _state.VeilHolderActorId > 0L && !string.IsNullOrWhiteSpace(_state.VeiledPositionId);
	internal static bool HasFamilyLegacy => _state != null && _state.LegacyFamilyId > 0L && _state.LegacyHiddenYear > 0;

	internal static XjTaiYinHiddenFruitArchiveData ExportState()
	{
		XjTaiYinHiddenFruitArchiveData source = _state ?? new XjTaiYinHiddenFruitArchiveData();
		return new XjTaiYinHiddenFruitArchiveData
		{
			SchemaVersion = 1,
			VeilHolderActorId = Math.Max(0L, source.VeilHolderActorId),
			VeiledPositionId = XjGuoWeiCalculator.NormalizeGuoWeiName(source.VeiledPositionId),
			VeiledYear = Math.Max(0, source.VeiledYear),
			LegacyFamilyId = Math.Max(0L, source.LegacyFamilyId),
			LegacyFamilyName = source.LegacyFamilyName ?? string.Empty,
			LegacySourceActorId = Math.Max(0L, source.LegacySourceActorId),
			LegacySourceName = source.LegacySourceName ?? string.Empty,
			LegacyHiddenYear = Math.Max(0, source.LegacyHiddenYear)
		};
	}

	internal static void ImportState(XjTaiYinHiddenFruitArchiveData source)
	{
		_state = source == null ? new XjTaiYinHiddenFruitArchiveData() : new XjTaiYinHiddenFruitArchiveData
		{
			SchemaVersion = 1,
			VeilHolderActorId = Math.Max(0L, source.VeilHolderActorId),
			VeiledPositionId = XjGuoWeiCalculator.NormalizeGuoWeiName(source.VeiledPositionId),
			VeiledYear = Math.Max(0, source.VeiledYear),
			LegacyFamilyId = Math.Max(0L, source.LegacyFamilyId),
			LegacyFamilyName = source.LegacyFamilyName ?? string.Empty,
			LegacySourceActorId = Math.Max(0L, source.LegacySourceActorId),
			LegacySourceName = source.LegacySourceName ?? string.Empty,
			LegacyHiddenYear = Math.Max(0, source.LegacyHiddenYear)
		};
		Touch(markArchive: false);
	}

	internal static void Clear()
	{
		_state = new XjTaiYinHiddenFruitArchiveData();
		Touch(markArchive: false);
	}

	internal static void ReconcileAfterLoad(int currentYear)
	{
		currentYear = Math.Max(1, currentYear);
		if (HasFamilyLegacy && !HasLivingLegacyFamilyMember(excludeActorId: 0L))
		{
			ClearLegacy(silent: true, currentYear);
		}

		if (!TryResolveCurrentTaiYinHolder(out long holderId))
		{
			if (HasActiveVeil) ClearActiveVeil(silent: true, currentYear);
			return;
		}

		if (HasFamilyLegacy)
		{
			// 已经存在真实太阴持果者时，以权威果位账本为准；若恰为本家后人，相当于读档期间完成承继。
			ClearLegacy(silent: true, currentYear);
		}
		// 旧版本曾允许自动把太阳正果收入月翳；太阳为第一显，若没有专门高位事件许可，
		// 读档时立即释放这一非法遮蔽，再从其他正果中重选。
		if (HasActiveVeil
			&& !XjYinYangRarePhenomenaSystem.CanTaiYinVeilPosition(_state.VeiledPositionId, currentYear, out _))
		{
			ClearActiveVeil(silent: true, currentYear);
		}
		if (_state.VeilHolderActorId != holderId)
		{
			_state.VeilHolderActorId = holderId;
			_state.VeiledPositionId = string.Empty;
			_state.VeiledYear = 0;
			Touch();
		}
		if (string.IsNullOrWhiteSpace(_state.VeiledPositionId)
			&& XjScheduler.ResolveActor(holderId, out Actor holder) && holder?.data != null)
		{
			TryChooseAndVeil(holder, currentYear, announce: false);
		}
	}

	internal static bool IsHiddenForActor(string guoWei, long actorId, out string reason)
	{
		reason = string.Empty;
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		if (string.IsNullOrWhiteSpace(normalized) || actorId <= 0L) return false;

		if (HasActiveVeil && string.Equals(normalized, _state.VeiledPositionId, StringComparison.Ordinal))
		{
			// 被藏果位的真实持有人仍可维护自己的权威状态；遮蔽只针对外界求位与窥视。
			if (IsCurrentPositionHolder(normalized, actorId)) return false;
			reason = "果位隐入太阴月翳，道势不可寻";
			return true;
		}

		if (HasFamilyLegacy && string.Equals(normalized, TaiYinZhengWei, StringComparison.Ordinal))
		{
			if (!HasLivingLegacyFamilyMember(excludeActorId: 0L))
			{
				ClearLegacy(silent: true, Math.Max(1, XjYearTracker.CurrentYear));
				return false;
			}
			if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
				&& familyId == _state.LegacyFamilyId)
			{
				return false;
			}
			reason = "太阴正果藏于旧族月契，外人无从照见";
			return true;
		}
		return false;
	}

	internal static bool IsPositionVeiled(string guoWei)
	{
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		return HasActiveVeil && string.Equals(normalized, _state.VeiledPositionId, StringComparison.Ordinal)
			|| HasFamilyLegacy && string.Equals(normalized, TaiYinZhengWei, StringComparison.Ordinal);
	}

	internal static bool TryGetActiveVeiledPosition(out string positionId)
	{
		positionId = HasActiveVeil ? _state.VeiledPositionId : string.Empty;
		return !string.IsNullOrWhiteSpace(positionId);
	}

	internal static bool IsFamilyLegacyHidden => HasFamilyLegacy;

	internal static void OnPositionClaimed(Actor actor, string daoTu, string guoWei, int year)
	{
		if (actor?.data == null) return;
		string normalized = XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei);
		string type = XjGuoWeiRegistry.ResolveTypeFromName(normalized);
		if (!string.Equals(type, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		if (string.Equals(normalized, TaiYinZhengWei, StringComparison.Ordinal))
		{
			OnTaiYinHeld(actor, Math.Max(1, year), announce: true);
			return;
		}

		// 太阴先成而当时没有合法目标时，后续有新的正果真正成立即可补一次藏果；已有目标绝不更换。
		if (!HasActiveVeil && TryResolveCurrentTaiYinHolder(out long holderId)
			&& XjScheduler.ResolveActor(holderId, out Actor holder) && holder?.data != null)
		{
			TryChooseAndVeil(holder, Math.Max(1, year), announce: true);
		}
	}

	internal static void OnDaoTaiBindingEstablished(Actor actor, XjDaoTaiPositionBindingArchiveRecord binding, int year)
	{
		if (actor?.data == null || binding == null) return;
		if (string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(binding.SecondaryPositionId), TaiYinZhengWei, StringComparison.Ordinal)
			|| string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(binding.PrimaryPositionId), TaiYinZhengWei, StringComparison.Ordinal))
		{
			OnTaiYinHeld(actor, Math.Max(1, year), announce: true);
		}
	}

	internal static void OnHolderRebound(long oldActorId, long newActorId)
	{
		if (_state == null || oldActorId <= 0L || newActorId <= 0L || _state.VeilHolderActorId != oldActorId) return;
		_state.VeilHolderActorId = newActorId;
		Touch();
	}

	internal static void OnTaiYinHolderReleased(long actorId, string guoWei, int year)
	{
		if (actorId <= 0L || !string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(guoWei), TaiYinZhengWei, StringComparison.Ordinal)) return;
		if (_state != null && _state.VeilHolderActorId == actorId) ClearActiveVeil(silent: false, Math.Max(1, year));
	}

	internal static void OnTaiYinHolderDeath(in XjDeathSnapshot snapshot, bool heldTaiYin, string familyName, int year)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L || !heldTaiYin) return;
		year = Math.Max(1, year);
		if (_state != null && _state.VeilHolderActorId == snapshot.ActorId) ClearActiveVeil(silent: false, year);
		if (snapshot.FamilyStableId <= 0L || !HasLivingFamilyMembers(snapshot.FamilyStableId, snapshot.ActorId)) return;

		_state.LegacyFamilyId = snapshot.FamilyStableId;
		_state.LegacyFamilyName = string.IsNullOrWhiteSpace(familyName) ? "旧族" : familyName.Trim();
		_state.LegacySourceActorId = snapshot.ActorId;
		_state.LegacySourceName = string.IsNullOrWhiteSpace(snapshot.Name) ? "太阴故主" : snapshot.Name.Trim();
		_state.LegacyHiddenYear = year;
		Touch();
		string text = _state.LegacySourceName + "身陨之前，将太阴正果敛入" + _state.LegacyFamilyName
			+ "月契。月华不显于外，只待同脉后人照见旧果。";
		RecordWorldEvent("月府藏真", text, year, snapshot.ActorId, snapshot.Name, snapshot.FamilyStableId);
	}

	private static void OnTaiYinHeld(Actor actor, int year, bool announce)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		if (HasFamilyLegacy)
		{
			bool sameFamily = XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyId)
				&& familyId == _state.LegacyFamilyId;
			string legacyFamilyName = _state.LegacyFamilyName;
			ClearLegacy(silent: true, year);
			if (sameFamily && announce)
			{
				string text = actor.getName() + "循" + (string.IsNullOrWhiteSpace(legacyFamilyName) ? "家门" : legacyFamilyName)
					+ "旧契照见太阴正果，月翳自开，果位归于同脉。";
				RecordWorldEvent("月契归宗", text, year, actorId, actor.getName(), familyId);
			}
		}
		if (_state.VeilHolderActorId != actorId)
		{
			if (HasActiveVeil) ClearActiveVeil(silent: true, year);
			_state.VeilHolderActorId = actorId;
			Touch();
		}
		if (string.IsNullOrWhiteSpace(_state.VeiledPositionId)) TryChooseAndVeil(actor, year, announce);
	}

	private static bool TryChooseAndVeil(Actor holder, int year, bool announce)
	{
		if (holder?.data == null || HasActiveVeil) return false;
		long holderId = ((BaseSystemData)holder.data).id;
		if (holderId <= 0L) return false;
		List<string> emptyCandidates = new List<string>();
		List<string> occupiedCandidates = new List<string>();
		IReadOnlyList<XjGuoWeiRegistryEntry> active = XjGuoWeiRegistry.ReadActiveEntries();
		HashSet<string> activeIds = new HashSet<string>(StringComparer.Ordinal);
		for (int i = 0; i < active.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = active[i];
			if (entry.Found && entry.IsActive && !string.IsNullOrWhiteSpace(entry.GuoWei))
				activeIds.Add(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei));
		}
		IReadOnlyList<string> daoTus = XjGuoWeiAuthorityCatalog.GetAllDaoTus();
		for (int i = 0; i < daoTus.Count; i++)
		{
			string daoTu = (daoTus[i] ?? string.Empty).Trim();
			if (daoTu.Length == 0 || string.Equals(daoTu, TaiYinDaoTu, StringComparison.Ordinal)
				|| !XjDaoTuManifestRegistry.IsDiscovered(daoTu)) continue;
			string candidate = XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, XjGuoWeiCalculator.ZhengWei, 1);
			if (!XjYinYangRarePhenomenaSystem.CanTaiYinVeilPosition(candidate, year, out _)) continue;
			if (XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(daoTu, XjGuoWeiCalculator.ZhengWei, candidate)
				|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(candidate)) continue;
			if (activeIds.Contains(candidate) || XjFruitPositionWorldState.IsDaoTaiSecondaryOccupied(candidate)) occupiedCandidates.Add(candidate);
			else emptyCandidates.Add(candidate);
		}
		List<string> candidates = emptyCandidates.Count > 0 ? emptyCandidates : occupiedCandidates;
		if (candidates.Count == 0) return false;
		candidates.Sort(StringComparer.Ordinal);
		int index = XjDeterministicHash.PositiveIndex(holderId + year, "taiyin_hidden_fruit", candidates.Count);
		return TryVeilPosition(holder, candidates[index], year, announce, out _);
	}

	internal static bool TryVeilPosition(Actor taiYinHolder, string targetPositionId, int year, bool announce, out string reason)
	{
		reason = string.Empty;
		if (taiYinHolder?.data == null) { reason = "持果者无效"; return false; }
		long actorId = ((BaseSystemData)taiYinHolder.data).id;
		if (!IsTaiYinHolder(actorId)) { reason = "并未持有太阴正果"; return false; }
		if (HasActiveVeil) { reason = "太阴月翳已藏一果"; return false; }
		string target = XjGuoWeiCalculator.NormalizeGuoWeiName(targetPositionId);
		if (string.IsNullOrWhiteSpace(target)
			|| string.Equals(target, TaiYinZhengWei, StringComparison.Ordinal)
			|| !string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(target), XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			reason = "只能藏另一道已显世正果";
			return false;
		}
		if (!XjYinYangRarePhenomenaSystem.CanTaiYinVeilPosition(target, year, out string veilGateReason))
		{
			reason = veilGateReason;
			return false;
		}
		string daoTu = target.Substring(0, target.Length - XjGuoWeiCalculator.ZhengWei.Length);
		if (!XjDaoTuManifestRegistry.IsDiscovered(daoTu)
			|| XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(daoTu, XjGuoWeiCalculator.ZhengWei, target)
			|| XjHongXiaLuoXiaEvent.IsExternalPositionOccupied(target))
		{
			reason = "该果位尚不可藏";
			return false;
		}
		_state.VeilHolderActorId = actorId;
		_state.VeiledPositionId = target;
		_state.VeiledYear = Math.Max(1, year);
		Touch();
		if (announce)
		{
			string text = "太阴幽映垂世，" + XjGuoWeiCalculator.GetDisplayGuoWeiName(target) + "敛入月翳，道势自此难寻。";
			RecordWorldEvent("太阴藏果", text, Math.Max(1, year), actorId, taiYinHolder.getName(), 0L);
		}
		return true;
	}

	private static bool TryResolveCurrentTaiYinHolder(out long actorId)
	{
		actorId = 0L;
		IReadOnlyList<XjGuoWeiRegistryEntry> active = XjGuoWeiRegistry.ReadActiveEntries();
		for (int i = 0; i < active.Count; i++)
		{
			XjGuoWeiRegistryEntry entry = active[i];
			if (!entry.Found || !entry.IsActive || entry.ActorId <= 0L) continue;
			if (!string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei), TaiYinZhengWei, StringComparison.Ordinal)) continue;
			actorId = entry.ActorId;
			return true;
		}
		if (XjFruitPositionWorldState.TryGetDaoTaiSecondaryHolder(TaiYinZhengWei, out long secondaryHolderId, out _)
			&& secondaryHolderId > 0L)
		{
			actorId = secondaryHolderId;
			return true;
		}
		return false;
	}

	private static bool IsTaiYinHolder(long actorId)
	{
		return actorId > 0L && TryResolveCurrentTaiYinHolder(out long holderId) && holderId == actorId;
	}

	private static bool IsCurrentPositionHolder(string positionId, long actorId)
	{
		if (actorId <= 0L) return false;
		if (XjGuoWeiRegistry.TryGetStrictActiveEntryByActorId(actorId, out XjGuoWeiRegistryEntry entry)
			&& string.Equals(XjGuoWeiCalculator.NormalizeGuoWeiName(entry.GuoWei), positionId, StringComparison.Ordinal)) return true;
		return XjFruitPositionWorldState.TryGetDaoTaiSecondaryHolder(positionId, out long secondaryHolderId, out _)
			&& secondaryHolderId == actorId;
	}

	private static bool HasLivingLegacyFamilyMember(long excludeActorId)
	{
		return HasFamilyLegacy && HasLivingFamilyMembers(_state.LegacyFamilyId, excludeActorId);
	}

	private static bool HasLivingFamilyMembers(long familyId, long excludeActorId)
	{
		if (familyId <= 0L) return false;
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(familyId))
		{
			if (member?.data == null || !XjSafeCore.IsAliveActor(member)) continue;
			long memberId = ((BaseSystemData)member.data).id;
			if (memberId > 0L && memberId != excludeActorId) return true;
		}
		return false;
	}

	private static void ClearActiveVeil(bool silent, int year)
	{
		if (!HasActiveVeil)
		{
			_state.VeilHolderActorId = 0L;
			_state.VeiledPositionId = string.Empty;
			_state.VeiledYear = 0;
			return;
		}
		string old = _state.VeiledPositionId;
		_state.VeilHolderActorId = 0L;
		_state.VeiledPositionId = string.Empty;
		_state.VeiledYear = 0;
		Touch();
		if (!silent)
		{
			string text = "月翳散去，" + XjGuoWeiCalculator.GetDisplayGuoWeiName(old) + "重新显于世间。";
			RecordWorldEvent("月翳散去", text, year, 0L, string.Empty, 0L);
		}
	}

	private static void ClearLegacy(bool silent, int year)
	{
		if (!HasFamilyLegacy) return;
		string familyName = _state.LegacyFamilyName;
		_state.LegacyFamilyId = 0L;
		_state.LegacyFamilyName = string.Empty;
		_state.LegacySourceActorId = 0L;
		_state.LegacySourceName = string.Empty;
		_state.LegacyHiddenYear = 0;
		Touch();
		if (!silent)
		{
			string text = (string.IsNullOrWhiteSpace(familyName) ? "旧族" : familyName) + "月契已散，太阴正果重显于世。";
			RecordWorldEvent("月契消散", text, year, 0L, string.Empty, 0L);
		}
	}

	private static void RecordWorldEvent(string title, string text, int year, long actorId, string actorName, long familyId)
	{
		XjBroadcastSystem.ShowRecordedWorldTipCritical(text, color: "#A7A1D8");
		XjWorldHistoryStore.RecordDomainEvent(
			XuanJianVNext.Data.History.XjWorldHistoryCategory.HighRealm,
			title,
			text,
			4,
			true,
			actorId: actorId,
			actorName: actorName ?? string.Empty,
			familyId: familyId,
			year: Math.Max(1, year),
			eventType: "TaiYinHiddenFruit");
	}

	private static void Touch(bool markArchive = true)
	{
		unchecked { _revision++; if (_revision <= 0) _revision = 1; }
		if (markArchive) XjWorldArchiveSystem.MarkChanged();
		XuanJianVNext.Systems.Codex.XjCodexSnapshotPublisher.MarkDirty(
			XuanJianVNext.Data.Codex.XjCodexDirtyFlags.World
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.JinDan
			| XuanJianVNext.Data.Codex.XjCodexDirtyFlags.History);
	}
}
