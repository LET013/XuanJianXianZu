using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 高境后裔以三代为界：高境修士的直系子女为第一代；其子女与孙辈分别为第二、第三代。
/// 任一后代自身晋升真人、真君或道胎后，会以其真实高境身份重新开启一条第一代直系血脉。
/// </summary>
internal static class XjHighRealmDescendantRules
{
	internal const string ZiFuDescendantTraitId = "XjZiFuDescendant";
	internal const string JinDanDescendantTraitId = "XjJinDanDescendant";
	internal const string DaoTaiDescendantTraitId = "XjDaoTaiDescendant";
	internal const string ZiFuFamilyTraitId = "XjZiFuFamily";
	internal const string JinDanFamilyTraitId = "XjJinDanFamily";
	internal const string DaoTaiFamilyTraitId = "XjDaoTaiFamily";
	internal const int ZiFuChildCap = 5;
	internal const int JinDanChildCap = 3;
	internal const int DaoTaiChildCap = 3;
	internal const int MingYangZiFuChildCap = 8;
	internal const int MingYangJinDanChildCap = 6;
	private const string MingYangDaoTu = "明阳";
	private const int MaxGeneration = 3;

	/// <summary>
	/// 旧档迁移：父母已死亡或离开世界时，仍按角色保存的血脉等级恢复
	/// 对应三代身份。高境血脉不再覆盖角色已经完成的五岁资质判定。
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
		if (cap <= 0 || parent?.data == null) return false;
		try { return Math.Max(0, parent.current_children_count) >= cap; }
		catch (NullReferenceException) { return false; }
	}

	internal static void RecordSuccessfulBirth(Actor parent)
	{
		// 1.1.0-alpha.2起按存活子女数判定；死亡后名额自动恢复，不再累计终身出生数。
	}

	internal static int ResolveBirthCap(Actor parent)
	{
		// 道胎后裔本身只允许同时存活1名子女；这是血脉平衡限制，不影响道胎本人原有上限。
		if (parent?.data != null && parent.hasTrait(DaoTaiDescendantTraitId)) return 1;
		int rank = ResolveActualRealmRank(parent);
		if (rank == 2 && IsMingYangPath(parent)) return MingYangJinDanChildCap;
		if (rank == 1 && IsMingYangPath(parent)) return MingYangZiFuChildCap;
		return rank >= 3 ? DaoTaiChildCap : rank == 2 ? JinDanChildCap : rank == 1 ? ZiFuChildCap : 0;
	}

	private static bool IsMingYangPath(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			&& string.Equals((daoTu ?? string.Empty).Trim(), MingYangDaoTu, StringComparison.Ordinal);
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
			rank = XjHighRealmIdentity.ResolveBloodlineRank(realmId);
			if (rank > 0) generation = 1;
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
		return XjHighRealmIdentity.ResolveBloodlineRank(realmId);
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
			if (actor.hasTrait(DaoTaiDescendantTraitId) || actor.hasTrait(DaoTaiFamilyTraitId)) rank = 3;
			else if (actor.hasTrait(JinDanDescendantTraitId) || actor.hasTrait(JinDanFamilyTraitId)) rank = 2;
			else if (actor.hasTrait(ZiFuDescendantTraitId) || actor.hasTrait(ZiFuFamilyTraitId)) rank = 1;
		}
		if (generation <= 0)
		{
			if (actor.hasTrait(DaoTaiDescendantTraitId) || actor.hasTrait(JinDanDescendantTraitId) || actor.hasTrait(ZiFuDescendantTraitId)) generation = 1;
			else if (actor.hasTrait(DaoTaiFamilyTraitId) || actor.hasTrait(JinDanFamilyTraitId) || actor.hasTrait(ZiFuFamilyTraitId)) generation = 2;
		}
		return rank > 0 && generation > 0 && generation <= MaxGeneration;
	}

	private static bool ApplyLineage(Actor child, int rank, int generation)
	{
		if (child?.data == null || rank <= 0 || generation <= 0 || generation > MaxGeneration) return false;
		string target = generation == 1
			? (rank >= 3 ? DaoTaiDescendantTraitId : rank == 2 ? JinDanDescendantTraitId : ZiFuDescendantTraitId)
			: (rank >= 3 ? DaoTaiFamilyTraitId : rank == 2 ? JinDanFamilyTraitId : ZiFuFamilyTraitId);
		bool changed = false;
		string[] all = { ZiFuDescendantTraitId, JinDanDescendantTraitId, DaoTaiDescendantTraitId, ZiFuFamilyTraitId, JinDanFamilyTraitId, DaoTaiFamilyTraitId };
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
		// 道胎血脉的资质保障属于后台规则，不写入特质说明：直系至少xjzz4，二/三代必有xjzz。
		if (rank >= 3)
		{
			changed |= XjAptitudeRuleEvaluator.EnsureDaoTaiLineageAptitude(child, generation == 1);
		}
		if (changed) child.setStatsDirty();
		return changed;
	}
}
