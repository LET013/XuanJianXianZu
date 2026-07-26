using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 高境后裔以三代为界：高境修士的直系子女为第一代；其子女与孙辈分别为第二、第三代。
/// 任一后代自身晋升紫府或金丹后，会以其真实境界重新开启一条第一代直系血脉。
/// </summary>
internal static class XjHighRealmDescendantRules
{
	internal const string ZiFuDescendantTraitId = "XjZiFuDescendant";
	internal const string JinDanDescendantTraitId = "XjJinDanDescendant";
	internal const string ZiFuFamilyTraitId = "XjZiFuFamily";
	internal const string JinDanFamilyTraitId = "XjJinDanFamily";
	internal const int ZiFuChildCap = 5;
	internal const int JinDanChildCap = 3;
	private const int MaxGeneration = 3;

	/// <summary>
	/// 旧档迁移：父母已死亡或离开世界时，仍按角色保存的血脉等级重算
	/// 当前版本的资质保底，避免旧版低保底永久残留。
	/// </summary>
	internal static bool ReconcileStoredLineage(Actor actor)
	{
		return ReadLineage(actor, out int rank, out int generation)
			&& ApplyLineage(actor, rank, generation);
	}

	internal static bool RefreshFromParents(Actor child)
	{
		if (!XjSafeCore.IsAliveActor(child) || child.data == null) return false;
		int bestRank = 0;
		int bestGeneration = int.MaxValue;
		ConsiderParent(child.data.parent_id_1, ref bestRank, ref bestGeneration);
		if (child.data.parent_id_2 > 0L && child.data.parent_id_2 != child.data.parent_id_1)
			ConsiderParent(child.data.parent_id_2, ref bestRank, ref bestGeneration);
		if (bestRank <= 0 || bestGeneration > MaxGeneration) return false;
		return ApplyLineage(child, bestRank, bestGeneration);
	}

	internal static bool ApplyFromPromotedParent(Actor child, Actor promotedParent)
	{
		return ApplyFromPromotedParent(child, promotedParent, true);
	}

	internal static bool ApplyFromPromotedParent(Actor child, Actor promotedParent, bool directDescendant)
	{
		if (!XjSafeCore.IsAliveActor(child) || !XjSafeCore.IsAliveActor(promotedParent)) return false;
		long parentId = ((BaseSystemData)promotedParent.data).id;
		if (parentId <= 0L || (child.data.parent_id_1 != parentId && child.data.parent_id_2 != parentId)) return false;
		int promotedRank = ResolveActualRealmRank(promotedParent);
		if (promotedRank <= 0) return false;
		ReadLineage(child, out int currentRank, out int currentGeneration);
		if (currentRank > promotedRank) return false;
		return ApplyLineage(child, promotedRank, directDescendant ? 1 : 2);
	}

	internal static bool HasReachedBirthCap(Actor parent)
	{
		int cap = ResolveBirthCap(parent);
		return cap > 0 && Math.Max(0, parent?.current_children_count ?? 0) >= cap;
	}

	internal static void RecordSuccessfulBirth(Actor parent)
	{
		// 1.1.0-alpha.2起按存活子女数判定；死亡后名额自动恢复，不再累计终身出生数。
	}

	internal static int ResolveBirthCap(Actor parent)
	{
		int rank = ResolveActualRealmRank(parent);
		return rank >= 2 ? JinDanChildCap : rank == 1 ? ZiFuChildCap : 0;
	}

	private static void ConsiderParent(long parentId, ref int bestRank, ref int bestGeneration)
	{
		if (parentId <= 0L) return;
		int rank = 0;
		int generation = int.MaxValue;
		if (XjScheduler.ResolveActor(parentId, out Actor parent) && parent?.data != null)
		{
			int actualRank = ResolveActualRealmRank(parent);
			if (actualRank > 0)
			{
				rank = actualRank;
				generation = 1;
			}
			else if (ReadLineage(parent, out int parentRank, out int parentGeneration) && parentGeneration > 0 && parentGeneration < MaxGeneration)
			{
				rank = parentRank;
				generation = parentGeneration + 1;
			}
		}
		else if (XjFamilyMemberLedger.TryGetByActorId(parentId, out XjFamilyMemberLedgerEntry entry) && entry.Found)
		{
			string realmId = XjFamilyMemberLedger.NormalizeRealmId(entry.RealmId);
			int order = XjFamilyMemberLedger.GetRealmOrder(realmId);
			if (order >= XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.JinDan)) { rank = 2; generation = 1; }
			else if (order >= XjFamilyMemberLedger.GetRealmOrder(XjRealmIds.ZiFu)) { rank = 1; generation = 1; }
		}
		if (rank <= 0 || generation > MaxGeneration) return;
		if (rank > bestRank || (rank == bestRank && generation < bestGeneration))
		{
			bestRank = rank;
			bestGeneration = generation;
		}
	}

	private static int ResolveActualRealmRank(Actor parent)
	{
		if (parent?.data == null) return 0;
		string realmId = XjRealmHelper.GetUnifiedId(parent, XjRealmHelper.GetTraitSnapshotForRouter);
		int order = XjRealmHelper.GetOrder(realmId);
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.JinDan) || parent.hasTrait("XjRealm5")) return 2;
		if (order >= XjRealmHelper.GetOrder(XjRealmIds.ZiFu) || parent.hasTrait("XjRealm4")) return 1;
		return 0;
	}

	private static bool ReadLineage(Actor actor, out int rank, out int generation)
	{
		rank = 0;
		generation = 0;
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmDescendantRank, out rank);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjHighRealmDescendantGeneration, out generation);
		if (rank <= 0)
		{
			if (actor.hasTrait(JinDanDescendantTraitId) || actor.hasTrait(JinDanFamilyTraitId)) rank = 2;
			else if (actor.hasTrait(ZiFuDescendantTraitId) || actor.hasTrait(ZiFuFamilyTraitId)) rank = 1;
		}
		if (generation <= 0)
		{
			if (actor.hasTrait(JinDanDescendantTraitId) || actor.hasTrait(ZiFuDescendantTraitId)) generation = 1;
			else if (actor.hasTrait(JinDanFamilyTraitId) || actor.hasTrait(ZiFuFamilyTraitId)) generation = 2;
		}
		return rank > 0 && generation > 0 && generation <= MaxGeneration;
	}

	private static bool ApplyLineage(Actor child, int rank, int generation)
	{
		if (child?.data == null || rank <= 0 || generation <= 0 || generation > MaxGeneration) return false;
		string target = generation == 1
			? (rank >= 2 ? JinDanDescendantTraitId : ZiFuDescendantTraitId)
			: (rank >= 2 ? JinDanFamilyTraitId : ZiFuFamilyTraitId);
		bool changed = false;
		string[] all = { ZiFuDescendantTraitId, JinDanDescendantTraitId, ZiFuFamilyTraitId, JinDanFamilyTraitId };
		for (int i = 0; i < all.Length; i++)
		{
			if (!string.Equals(all[i], target, StringComparison.Ordinal) && child.hasTrait(all[i]))
			{
				child.removeTrait(all[i]);
				changed = true;
			}
		}
		if (!child.hasTrait(target)) { child.addTrait(target, false); changed = true; }
		XjActorAccessor.SetInt(child, XjActorDataKeys.XjHighRealmDescendantRank, rank);
		XjActorAccessor.SetInt(child, XjActorDataKeys.XjHighRealmDescendantGeneration, generation);
		// 高境血脉的目的不是只给一个显示特质，而是维持长期可晋升的
		// 后备修士。直系金丹/紫府后裔分别保底五档/四档，后两代各降一档。
		int aptitudeFloor = generation == 1 ? (rank >= 2 ? 5 : 4) : (rank >= 2 ? 4 : 3);
		changed |= EnsureAptitudeFloor(child, aptitudeFloor);
		if (changed) child.setStatsDirty();
		return changed;
	}

	private static bool EnsureAptitudeFloor(Actor actor, int floor)
	{
		if (actor?.data == null || floor < 1 || floor > 6) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int current);
		if (current >= floor) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, floor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(actor, floor);
		XjVisibleTraitSync.SyncAptitudeTrait(actor, floor);
		XjCultivatorCache.CheckAndUpdate(actor);
		// 血脉保底可能把原本的凡人直接变为修士，不能等下一次原生
		// updateAge 才接入调度，否则高倍速下会延迟多年甚至永久漏掉。
		XjScheduler.EnsureRuntimeIndexesForActor(actor);
		XjScheduler.EnqueueAnnualActor(actor);
		return true;
	}
}
