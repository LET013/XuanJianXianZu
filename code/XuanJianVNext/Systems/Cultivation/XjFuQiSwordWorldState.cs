using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.Cultivation;

internal static partial class XjFuQiSwordWorldState
{
	internal const string EstablishedDaoName = "长庚";
	internal const string EstablishedPositionRank = "果位";

	private static XjFuQiSwordWorldArchiveData _state = new XjFuQiSwordWorldArchiveData();
	private static bool _establishmentCommitInProgress;

	internal static bool IsEstablished => _state.Established;
	internal static bool IsCombatDoctrineEstablished => _state.Established && !_establishmentCommitInProgress;
	internal static bool HasCurrentHolder => _state.Established && _state.CurrentHolderActorId > 0L;
	internal static bool IsVacant => _state.Established && _state.CurrentHolderActorId <= 0L;
	internal static string DaoName => _state.Established ? (_state.DaoName ?? string.Empty) : string.Empty;
	internal static long FounderActorId => _state.FounderActorId;
	internal static bool IsFounderActor(long actorId)
	{
		if (!_state.Established || actorId <= 0L) return false;
		if (_state.FounderActorId > 0L) return _state.FounderActorId == actorId;
		if (_state.HolderHistory == null) return false;
		for (int i = 0; i < _state.HolderHistory.Count; i++)
		{
			XjFuQiSwordPositionHolderArchiveData record = _state.HolderHistory[i];
			if (record != null && record.IsFounder && record.ActorId == actorId) return true;
		}
		return false;
	}
	internal static long CurrentHolderActorId => _state.CurrentHolderActorId;
	internal static int EstablishedYear => _state.EstablishedYear;
	internal static int VacantSinceYear => _state.VacantSinceYear;

	/// <summary>
	/// 先提交世界唯一果位，再提交角色境界。调用方若后续角色写入失败，必须调用 RollbackEstablishment。
	/// </summary>
	internal static bool TryEstablish(Actor actor, int currentYear)
	{
		if (_state.Established || actor?.data == null || currentYear <= 0) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		string actorName = SafeActorName(actor);
		_state = new XjFuQiSwordWorldArchiveData
		{
			Established = true,
			DaoName = EstablishedDaoName,
			PositionRank = EstablishedPositionRank,
			FounderActorId = actorId,
			FounderName = actorName,
			EstablishedYear = currentYear,
			CurrentHolderActorId = actorId,
			CurrentHolderAcquiredYear = currentYear,
			HolderHistory = new List<XjFuQiSwordPositionHolderArchiveData>
			{
				new XjFuQiSwordPositionHolderArchiveData
				{
					ActorId = actorId,
					ActorName = actorName,
					AcquiredYear = currentYear,
					IsFounder = true
				}
			}
		};
		_establishmentCommitInProgress = true;
		XjDaoTuManifestRegistry.MarkFuQiManifested(XjDaoTuRootIds.LongGeng, actorId, currentYear);
		XjWorldArchiveSystem.MarkChanged();
		return true;
	}

	internal static bool CommitEstablishment(long actorId, int currentYear)
	{
		if (!_establishmentCommitInProgress
			|| !_state.Established
			|| _state.FounderActorId != actorId
			|| _state.EstablishedYear != currentYear)
		{
			return false;
		}
		_establishmentCommitInProgress = false;
		return true;
	}

	internal static void RollbackEstablishment(long actorId, int currentYear)
	{
		if (!_state.Established
			|| _state.FounderActorId != actorId
			|| _state.EstablishedYear != currentYear)
		{
			return;
		}
		_state = new XjFuQiSwordWorldArchiveData();
		_establishmentCommitInProgress = false;
		XjDaoTuManifestRegistry.RollbackFuQiManifestation(XjDaoTuRootIds.LongGeng, actorId, currentYear);
		XjWorldArchiveSystem.MarkChanged();
	}

	internal static void OnActorDied(Actor actor, int currentYear)
	{
		if (!_state.Established || actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L || _state.CurrentHolderActorId != actorId) return;
		ReleaseCurrentHolder(actorId, currentYear, SafeActorName(actor), true);
	}

	internal static void ReconcileCurrentHolder()
	{
		if (!_state.Established || _state.CurrentHolderActorId <= 0L) return;
		// 读档早期角色索引尚未重建，“暂时解析不到”不能解释为持有者已死。
		// 只在明确解析到死亡对象时清理；正常死亡由 OnActorDied 即时处理。
		if (!XjActorRegistry.ResolveKnownOrWorld(_state.CurrentHolderActorId, out Actor actor)
			|| actor?.data == null) return;
		if (actor.isAlive()) return;
		ReleaseCurrentHolder(_state.CurrentHolderActorId, ResolveCurrentYear(0), SafeActorName(actor), false);
	}

	internal static XjFuQiSwordWorldArchiveData ExportState()
	{
		ReconcileCurrentHolder();
		NormalizeEstablishedState();
		return _state.Clone();
	}

	internal static void ImportState(XjFuQiSwordWorldArchiveData source)
	{
		XjFuQiLongGengPositionHandler.ClearRuntimeState();
		XjFuQiSwordWorldArchiveData value = source?.Clone() ?? new XjFuQiSwordWorldArchiveData();
		if (!value.Established)
		{
			_state = new XjFuQiSwordWorldArchiveData();
			_establishmentCommitInProgress = false;
			return;
		}
		_state = value;
		// 归档只可能发生在同步空证事务之外；已入档的建立事实视为完整提交。
		_establishmentCommitInProgress = false;
		NormalizeEstablishedState();
		ReconcileCurrentHolder();
	}

	/// <summary>
	/// 长庚显世后，将仍保存为空道途的养青冥旧角色回填为长庚。
	/// 这是角色身份的权威迁移，不依赖公告文案猜测。
	/// </summary>
	internal static bool EnsureEstablishedDaoIdentity(Actor actor, bool syncVisibleTraits)
	{
		if (!_state.Established || actor?.data == null || !XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			return false;
		}
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.FuQiLineageId, out string lineage)
			|| !string.Equals(lineage, XjFuQiLineageIds.Sword, StringComparison.Ordinal))
		{
			return false;
		}

		string establishedName = string.IsNullOrWhiteSpace(_state.DaoName)
			? EstablishedDaoName
			: _state.DaoName.Trim();
		bool changed = !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string stored)
			|| !string.Equals((stored ?? string.Empty).Trim(), establishedName, StringComparison.Ordinal);
		if (changed)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.DaoTu, establishedName);
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out string origin)
			|| string.IsNullOrWhiteSpace(origin)
			|| string.Equals(origin.Trim(), "未知道途", StringComparison.Ordinal)
			|| string.Equals(origin.Trim(), "无名道途", StringComparison.Ordinal)
			|| string.Equals(origin.Trim(), "无名剑道", StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, establishedName);
			changed = true;
		}

		if (changed && syncVisibleTraits)
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
		}
		return changed;
	}

	internal static void SyncLineageTraits()
	{
		var ids = XjFuQiCandidateIndex.GetActorIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor) && actor?.data != null && actor.isAlive())
			{
				bool identityChanged = EnsureEstablishedDaoIdentity(actor, false);
				if (identityChanged) XjVisibleTraitSync.SyncCultivationTraits(actor);
				else XjVisibleTraitSync.SyncFuQiLineageTrait(actor);
			}
		}
	}

	internal static void Clear()
	{
		_state = new XjFuQiSwordWorldArchiveData();
		_establishmentCommitInProgress = false;
		XjFuQiLongGengPositionHandler.ClearRuntimeState();
	}
}
