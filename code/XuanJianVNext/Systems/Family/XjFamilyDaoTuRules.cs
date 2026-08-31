using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// Resolves a stable paternal-family DaoTu only at lifecycle boundaries.
/// It never scans the world and never runs from UI reads.
/// </summary>
internal static class XjFamilyDaoTuRules
{
	private static readonly XjDaoTuVisibleTraitEntry[] RandomAssignableEntries = BuildRandomAssignableEntries();
	internal static bool TryEnsureCultivatorDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !HasCultivationIdentity(actor))
		{
			return false;
		}

		if (TryReadValidForActor(actor, XjActorDataKeys.DaoTu, out string current))
		{
			daoTu = current;
			EnsureOriginAfterSuccessfulAssignment(actor, daoTu);
			return true;
		}

		if (!TryResolvePreferredDaoTu(actor, out daoTu)
			|| !XjCultivationStateTransitions.TrySetDaoTu(actor, daoTu, false)
			|| !TryReadValidForActor(actor, XjActorDataKeys.DaoTu, out string persisted)
			|| !string.Equals(persisted, daoTu, StringComparison.Ordinal))
		{
			daoTu = string.Empty;
			return false;
		}

		EnsureOriginAfterSuccessfulAssignment(actor, daoTu);
		return true;
	}

	/// <summary>
	/// 只解析角色自身或家族已经确立的道途，不创建普通随机结果。
	/// 已显现的并古道途可以作为家学继承，但后代仍要独立判定感气。
	/// </summary>
	internal static bool TryResolveInitialInheritedDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null) return false;

		// 已经正式定下的本人道途不能被家族投影覆盖；血脉来源仍只是初始偏好，
		// 必须先让家中紫府、真人、金丹、真君与道胎形成主传承，避免舍本逐末。
		if (TryReadInitialAssignable(actor, XjActorDataKeys.DaoTu, out daoTu)) return true;

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			return TryReadInitialAssignable(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu);
		}

		if (TryResolveHighRealmFamilyTradition(identity.FamilyStableIdValue, actorId, out daoTu))
		{
			return true;
		}

		if (TryReadInitialAssignable(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu)) return true;

		// 家中尚无紫府/真人以上传承时，才回落到原有的等权家学选择。
		HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
		List<string> candidates = new List<string>();
		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
		{
			if (member?.data == null || ((BaseSystemData)member.data).id == actorId) continue;
			if (!TryReadInitialAssignable(member, XjActorDataKeys.XjBloodlineOriginDaoTu, out string candidate)
				&& !TryReadInitialAssignable(member, XjActorDataKeys.DaoTu, out candidate)) continue;
			candidate = candidate.Trim();
			if (seen.Add(candidate)) candidates.Add(candidate);
		}
		if (candidates.Count == 0) return false;
		candidates.Sort(StringComparer.Ordinal);
		daoTu = candidates[XjDeterministicHash.PositiveIndex(
			identity.FamilyStableIdValue, "family_inherited_daotu_equal_v2", candidates.Count)];
		return !string.IsNullOrWhiteSpace(daoTu);
	}

	private static bool TryResolveYuanZhaoNaturalContact(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null
			|| !XjDaoTuManifestRegistry.IsDiscovered(XjDaoTuRootIds.YuanZhao)
			|| !XjDaoTuManifestRegistry.IsCaiQiUnlocked(XjDaoTuRootIds.YuanZhao))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| XjDeterministicHash.PositiveIndex(actorId, "yuanzhao_natural_contact_v1", 128) != 0)
		{
			return false;
		}

		daoTu = "渊照";
		return true;
	}

	/// <summary>
	/// 家族主传承由高境道途统一解析器决定；若本家已经把本道果余闰全部占满，
	/// 则形成位序垄断，后代首次定路不再发生旁支分流。
	/// </summary>
	private static bool TryResolveHighRealmFamilyTradition(long familyId, long actorId, out string daoTu)
	{
		daoTu = string.Empty;
		if (!XjDaoTuHeritageService.TryResolveFamilyTradition(
			familyId, actorId, out XjDaoTuTraditionSummary tradition)) return false;
		XjDaoTuControlSummary control = XjDaoTuHeritageService.ResolveFamilyControl(familyId, tradition.DaoTu);
		int influencePercent = control.IsMonopoly ? 100 : tradition.InfluencePercent;
		if (XjDeterministicHash.PositiveIndex(
			actorId + familyId, "family_high_realm_tradition_influence_v1", 100) >= influencePercent) return false;
		daoTu = tradition.DaoTu;
		return !string.IsNullOrWhiteSpace(daoTu);
	}

	/// <summary>
	/// 修炼方式尚未确定时，先解析角色能接触到的道途。家族传承决定接触对象，
	/// 不决定角色最终使用服气养性还是紫府金丹。并古九途与普通道途等权进入常规随机池；
	/// 已空证渊照保留独立低频自然接触，虹霞则只由落霞山择徒或既成家学传承进入。
	/// </summary>
	internal static bool TryResolveInitialDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null) return false;

		if (TryResolveInitialInheritedDaoTu(actor, out daoTu)) return true;

		// 渊照是玄鉴历第五百年后才成立的后世空证道途，不进开局普通等权池。
		// 空证成立后，极少数无既定家学的新修士可以自然接触水月照真的道统痕迹；
		// 一旦某家真正修成渊照，后代便优先走上面的家学传承，而不是持续靠随机扩散。
		// 这里是单角色确定性哈希，不扫描洞天、城市或世界角色。
		if (TryResolveYuanZhaoNaturalContact(actor, out daoTu)) return true;
		long actorId = ((BaseSystemData)actor.data).id;
		long stableSeed = actorId;
		if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			stableSeed = identity.FamilyStableIdValue;
		}

		IReadOnlyList<XjDaoTuVisibleTraitEntry> entries = RandomAssignableEntries;
		if (entries.Count == 0) return false;
		daoTu = entries[XjDeterministicHash.PositiveIndex(stableSeed, "family_daotu", entries.Count)].DisplayName;
		return IsRandomAssignable(daoTu);
	}

	/// <summary>
	/// 紫金体系内部继续使用同一份初始道途解析，避免服气感气失败后重新抽到另一条道途。
	/// </summary>
	internal static bool TryResolvePreferredDaoTu(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		return actor?.data != null
			&& XjCultivationPathRules.IsZiFuJinDan(actor)
			&& TryResolveInitialDaoTu(actor, out daoTu);
	}

	internal static void RememberInitialDaoTuOrigin(Actor actor, string daoTu)
	{
		if (actor?.data == null || !IsInitialAssignable(daoTu)) return;
		EnsureOriginAfterSuccessfulAssignment(actor, daoTu.Trim());
	}

	/// <summary>
	/// CaiQi family preference is deliberately independent from the actor's current DaoTu.
	/// Only another confirmed family member's persisted bloodline origin can bias a site.
	/// This prevents the identity chain from deciding its own CaiQi result.
	/// </summary>
	internal static bool TryResolveCaiQiFamilyPreference(Actor actor, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (!XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			return false;
		}

		// 采气候选与首次定路共用同一高境家学结论，避免定路选择本家主道、
		// 采气偏好却被低境旁支重新拉向另一条道途。
		if (TryResolveHighRealmFamilyTradition(identity.FamilyStableIdValue, actorId, out daoTu))
		{
			if (XjBingGuZiJinCompatibility.TryResolveCaiQiProxyDaoTu(actor, daoTu, out string highRealmProxy))
				daoTu = highRealmProxy;
			return true;
		}

		foreach (Actor member in XjFamilyReadModel.Shared.GetFamilyMembers(identity.FamilyStableIdValue))
		{
			if (member?.data == null || ((BaseSystemData)member.data).id == actorId)
			{
				continue;
			}
			if (TryReadInitialAssignable(member, XjActorDataKeys.XjBloodlineOriginDaoTu, out daoTu))
			{
				if (XjBingGuZiJinCompatibility.TryResolveCaiQiProxyDaoTu(actor, daoTu, out string proxyDaoTu))
				{
					daoTu = proxyDaoTu;
				}
				return true;
			}
		}

		// 首名家族修士尚无可继承来源时不制造“虚拟家族偏好”，
		// 直接让采气候选回落到当前城市的普通稳定哈希。
		return false;
	}

	private static void EnsureOriginAfterSuccessfulAssignment(Actor actor, string daoTu)
	{
		if (!IsInitialAssignable(daoTu)) return;
		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out string origin)
			|| !IsInitialAssignable(origin))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, daoTu.Trim());
		}
	}

	private static XjDaoTuVisibleTraitEntry[] BuildRandomAssignableEntries()
	{
		IReadOnlyList<XjDaoTuVisibleTraitEntry> source = XjDaoTuVisibleTraitCatalog.InitialAssignableEntries;
		List<XjDaoTuVisibleTraitEntry> result = new List<XjDaoTuVisibleTraitEntry>(source.Count);
		for (int i = 0; i < source.Count; i++)
		{
			if (IsRandomAssignable(source[i].DisplayName)) result.Add(source[i]);
		}
		return result.ToArray();
	}

	private static bool HasCultivationIdentity(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)
		{
			return true;
		}

		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& XjCultivationStateTransitions.IsKnownRealm(realmId))
		{
			return true;
		}

		return XjGongFaAccessor.BuildState(actor).Found;
	}

	private static bool TryReadValidForActor(Actor actor, string key, out string daoTu)
	{
		daoTu = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, key, out daoTu)
			|| !IsValid(daoTu))
		{
			return false;
		}

		return !string.Equals(daoTu.Trim(), XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal)
			|| XjQingXuanKongZhengSystem.CanEnterQingXuan(actor);
	}

	private static bool TryReadInitialAssignable(Actor actor, string key, out string daoTu)
	{
		daoTu = string.Empty;
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, key, out daoTu)
			&& IsInitialAssignable(daoTu);
	}

	private static bool IsInitialAssignable(string daoTu)
	{
		if (!IsValid(daoTu)
			|| !XjDaoTuCatalog.TryResolve(daoTu.Trim(), out XjDaoTuDefinition definition)) return false;
		if (IsRandomAssignable(daoTu)) return true;
		// 后世空证道途不进入普通随机池；一旦天地已承认，则可以通过真实家学/血脉来源稳定传下去。
		// 渊照还必须等水月照真洞天门户与唯一采气源真实落地，否则家学只是一段历史记忆，
		// 不能把后代送进一个没有采气来源的死路径。
		return definition.IsLaterFounded
			&& XjDaoTuManifestRegistry.CanEnterLaterFoundedPath(definition.RootId);
	}

	private static bool IsRandomAssignable(string daoTu)
	{
		if (!IsValid(daoTu)
			|| !XjDaoTuCatalog.TryResolve(daoTu.Trim(), out XjDaoTuDefinition definition)) return false;
		if (definition.IsCommonAncient) return true;
		return definition.IsBingGu
			&& XjFuQiCoreCatalog.TryGetByRootId(definition.RootId, out XjFuQiCoreDefinition core)
			&& core.GameplayImplemented;
	}

	private static bool IsValid(string daoTu)
	{
		return !string.IsNullOrWhiteSpace(daoTu)
			&& XjDaoTuVisibleTraitCatalog.TryResolveTraitId(daoTu.Trim(), out _);
	}
}
