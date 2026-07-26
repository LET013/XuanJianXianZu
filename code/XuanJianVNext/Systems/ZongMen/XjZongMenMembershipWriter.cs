using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// 宗门成员与职阶的唯一写入口。city.data 是权威数据源，角色字段只是镜像；
/// 每次迁移同时更新成员表、职阶表、山峰表和角色镜像，避免双源漂移。
/// </summary>
internal static class XjZongMenMembershipWriter
{
	internal const string RoleMember = "member";
	internal const string RoleDisciple = "disciple";
	internal const string RolePeakMaster = "fengzhu";
	internal const string RoleSupremeElder = "supreme_elder";
	internal const string RoleZongZhu = "zongzhu";

	internal static bool EnsureMember(City city, Actor actor, int currentYear, string reason)
	{
		if (city?.data == null || actor?.data == null || !actor.isAlive()
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjYinSiTraitLifecycle.IsYinSi(actor)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;
		if (!XjZongMenCityData.HasZongMen(city)) return false;
		long actorId = XjZongMenCityData.GetActorId(actor);
		if (actorId <= 0L) return false;

		XjZongMenIdentitySnapshot existingIdentity = XjZongMenAccessor.BuildIdentity(actor);
		long zongMenId = XjZongMenCityData.GetZongMenId(city);
		if (existingIdentity.Found && existingIdentity.ZongMenId > 0L && existingIdentity.ZongMenId != zongMenId)
		{
			if (XjZongMenCityData.TryResolveZongMenCity(existingIdentity.ZongMenId, out City oldCity))
			{
				RemoveMember(oldCity, actorId, "TransferToAnotherZongMen");
			}
			else
			{
				XjZongMenAccessor.WriteIdentity(actor, XjZongMenIdentitySnapshot.Empty);
			}
		}

		List<long> members = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyMemberIds);
		bool added = AddUnique(members, actorId);
		if (added) XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyMemberIds, members);
		EnsureBalancedDiscipleAssignment(city, actor);

		ReconcileActorMirror(city, actor, currentYear, string.IsNullOrWhiteSpace(reason) ? "EnsureMember" : reason);
		if (added)
		{
			XjCultivationSeed.RefreshChuShenForCultivationState(actor);
			XjGongFaProgression.PublishInheritanceSnapshot(actor, "ZongMenMemberAdded");
			XjThreeBookWriter.RecordSectEnrollment(
				zongMenId,
				XjZongMenCityData.GetZongMenName(city),
				actor,
				currentYear);
		}
		return added;
	}

	internal static bool RemoveMember(City city, long actorId, string reason)
	{
		if (city?.data == null || actorId <= 0L) return false;
		List<long> members = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyMemberIds);
		bool removed = RemoveAll(members, actorId);
		if (removed) XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyMemberIds, members);
		RemoveFromRoleLists(city, actorId, true);

		Actor actor = XjZongMenCityData.ResolveActor(actorId);
		if (actor?.data != null)
		{
			XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
			if (!identity.Found || identity.ZongMenId == XjZongMenCityData.GetZongMenId(city))
			{
				XjZongMenAccessor.WriteIdentity(actor, XjZongMenIdentitySnapshot.Empty);
			}
		}
		return removed;
	}

	internal static bool AssignDisciple(City city, int peakId, Actor actor, int currentYear, string reason)
	{
		if (city?.data == null || !XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;
		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		if (!peakIds.Contains(peakId)) return false;
		if (peakId != XjZongMenCityData.MainPeakId
			&& !XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out _))
		{
			return false;
		}
		if (!EnsureMember(city, actor, currentYear, reason))
		{
			if (!XjZongMenCityData.IsMember(city, actor)) return false;
		}
		long actorId = XjZongMenCityData.GetActorId(actor);
		if (actorId <= 0L || actorId == XjZongMenCityData.GetZongZhuId(city)) return false;

		RemoveFromRoleLists(city, actorId, false);
		List<long> disciples = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId);
		bool changed = AddUnique(disciples, actorId);
		XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId, disciples);
		ReconcileActorMirror(city, actor, currentYear, reason);
		// 手动入峰后立即刷新宗门修士索引，避免仙鉴快照仍使用旧宗门/旧峰位。
		XjZongMenCultivatorCityIndex.Observe(actor);
		return changed;
	}

	internal static bool AssignPeakMaster(City city, int peakId, Actor actor, int currentYear, string reason)
	{
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;
		if (!EnsureMember(city, actor, currentYear, reason))
		{
			if (!XjZongMenCityData.IsMember(city, actor)) return false;
		}
		long actorId = XjZongMenCityData.GetActorId(actor);
		if (actorId <= 0L) return false;
		long zongZhuId = XjZongMenCityData.GetZongZhuId(city);
		if ((peakId == XjZongMenCityData.MainPeakId && actorId != zongZhuId)
			|| (peakId != XjZongMenCityData.MainPeakId && actorId == zongZhuId))
		{
			return false;
		}
		long existingId = 0L;
		XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out existingId);
		if (existingId > 0L && existingId != actorId)
		{
			city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, string.Empty);
			Actor existing = XjZongMenCityData.ResolveActor(existingId);
			if (existing?.data != null) ReconcileActorMirror(city, existing, currentYear, "PeakMasterReplaced");
		}

		RemoveFromRoleLists(city, actorId, false);
		city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, actorId.ToString(CultureInfo.InvariantCulture));
		ReconcileActorMirror(city, actor, currentYear, reason);
		return existingId != actorId;
	}

	internal static bool ClearPeakMaster(City city, int peakId, int currentYear, string reason)
	{
		if (city?.data == null) return false;
		if (!XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out long actorId))
		{
			city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, string.Empty);
			return false;
		}

		city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, string.Empty);
		Actor actor = XjZongMenCityData.ResolveActor(actorId);
		if (actor?.data != null) ReconcileActorMirror(city, actor, currentYear, reason);
		return true;
	}

	internal static bool AssignSupremeElder(City city, Actor actor, int currentYear, string reason)
	{
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;
		if (!EnsureMember(city, actor, currentYear, reason))
		{
			if (!XjZongMenCityData.IsMember(city, actor)) return false;
		}
		long actorId = XjZongMenCityData.GetActorId(actor);
		if (actorId <= 0L || actorId == XjZongMenCityData.GetZongZhuId(city))
		{
			ReconcileActorMirror(city, actor, currentYear, reason);
			return false;
		}

		RemoveFromRoleLists(city, actorId, false);
		List<long> elders = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders);
		bool changed = AddUnique(elders, actorId);
		XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeySupremeElders, elders);
		ReconcileActorMirror(city, actor, currentYear, reason);
		return changed;
	}

	internal static bool AssignZongZhu(City city, Actor actor, int currentYear, bool founding, string reason)
	{
		if (city?.data == null || actor?.data == null || !XjCultivationEligibility.HasCultivationAptitudeTrait(actor)) return false;
		if (!EnsureMember(city, actor, currentYear, reason))
		{
			if (!XjZongMenCityData.IsMember(city, actor)) return false;
		}

		XjZongMenCityData.EnsureMainPeak(city);
		long actorId = XjZongMenCityData.GetActorId(actor);
		long previousId = XjZongMenCityData.GetZongZhuId(city);
		bool changed = previousId != actorId;
		if (founding)
		{
			XjZongMenCityData.SetGeneration(city, 1);
		}
		else if (changed)
		{
			XjZongMenCityData.SetGeneration(city, Math.Max(1, XjZongMenCityData.GetGeneration(city)) + 1);
		}

		RemoveFromRoleLists(city, actorId, false);
		XjZongMenCityData.SetZongZhuId(city, actorId);
		city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + XjZongMenCityData.MainPeakId, actorId.ToString(CultureInfo.InvariantCulture));
		ReconcileActorMirror(city, actor, currentYear, reason);

		if (changed && previousId > 0L)
		{
			Actor previous = XjZongMenCityData.ResolveActor(previousId);
			if (previous?.data != null) ReconcileActorMirror(city, previous, currentYear, "ZongZhuSucceeded");
		}
		return changed;
	}

	internal static void RemoveFromRoleLists(City city, long actorId, bool includeZongZhu)
	{
		if (city?.data == null || actorId <= 0L) return;
		List<long> elders = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeySupremeElders);
		if (RemoveAll(elders, actorId)) XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeySupremeElders, elders);

		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			if (XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out long peakMasterId)
				&& peakMasterId == actorId)
			{
				city.data.set(XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, string.Empty);
			}

			RemoveActorFromList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId, actorId);
			RemoveActorFromList(city, XjZongMenCityData.KeyPeakInnerPrefix + peakId, actorId);
		}

		if (includeZongZhu && XjZongMenCityData.GetZongZhuId(city) == actorId)
		{
			XjZongMenCityData.SetZongZhuId(city, 0L);
		}
	}

	internal static void ReconcileActorMirror(City city, Actor actor, int currentYear, string reason)
	{
		if (city?.data == null || actor?.data == null) return;
		long actorId = XjZongMenCityData.GetActorId(actor);
		long zongMenId = XjZongMenCityData.GetZongMenId(city);
		if (actorId <= 0L || zongMenId <= 0L) return;
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor))
		{
			RemoveMember(city, actorId, "NonCultivatorPruned");
			XjZongMenAccessor.WriteIdentity(actor, XjZongMenIdentitySnapshot.Empty);
			return;
		}
		if (!XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeyMemberIds, actorId))
		{
			XjZongMenIdentitySnapshot current = XjZongMenAccessor.BuildIdentity(actor);
			if (current.Found && current.ZongMenId == zongMenId)
			{
				XjZongMenAccessor.WriteIdentity(actor, XjZongMenIdentitySnapshot.Empty);
			}
			return;
		}

		EnsureBalancedDiscipleAssignment(city, actor);
		XjZongMenIdentitySnapshot oldIdentity = XjZongMenAccessor.BuildIdentity(actor);
		int joinYear = oldIdentity.Found && oldIdentity.ZongMenId == zongMenId && oldIdentity.JoinYear > 0
			? oldIdentity.JoinYear
			: Math.Max(0, currentYear);
		string zongMenName = XjZongMenCityData.GetZongMenName(city);
		if (XjSectRepository.TryGetBySectId(zongMenId, out var sectRecord)
			&& sectRecord != null
			&& !string.IsNullOrWhiteSpace(sectRecord.Name))
		{
			zongMenName = sectRecord.Name.Trim();
		}
		string role = RoleMember;
		int peakId = 0;
		string peakName = string.Empty;
		string rank = "门人";

		if (TryResolveArchivePeakRole(city, zongMenId, actorId, out string archiveRole, out int archivePeakId, out string archivePeakName, out string archiveRank))
		{
			role = archiveRole;
			peakId = archivePeakId;
			peakName = archivePeakName;
			rank = archiveRank;
		}
		else if (XjZongMenCityData.GetZongZhuId(city) == actorId)
		{
			role = RoleZongZhu;
			peakId = XjZongMenCityData.MainPeakId;
			peakName = XjZongMenCityData.GetPeakName(city, peakId);
			rank = "宗主";
		}
		else if (XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeySupremeElders, actorId))
		{
			role = RoleSupremeElder;
			peakId = XjZongMenCityData.SupremePeakId;
			peakName = XjZongMenCityData.GetDongTianPeakName(city);
			rank = "老祖";
		}
		else if (TryFindPeakMaster(city, actorId, out int masterPeakId))
		{
			role = RolePeakMaster;
			peakId = masterPeakId;
			peakName = XjZongMenCityData.GetPeakName(city, peakId);
			rank = "峰主";
		}
		else if (TryFindDisciplePeak(city, actorId, out int disciplePeakId))
		{
			role = RoleDisciple;
			peakId = disciplePeakId;
			peakName = XjZongMenCityData.GetPeakName(city, peakId);
			rank = "弟子";
		}

		XjZongMenAccessor.WriteIdentity(actor, new XjZongMenIdentitySnapshot(
			true,
			actorId,
			actor.getName() ?? string.Empty,
			zongMenId,
			zongMenName,
			rank,
			joinYear,
			role,
			peakId,
			peakName,
			string.IsNullOrWhiteSpace(reason) ? "Reconciled" : reason));
	}

	/// <summary>
	/// 新版宗门档案是宗主与峰主位的唯一权威。城镇表仅保留成员收录，
	/// 不能再用旧峰位覆盖档案任命，否则会出现无峰主支峰收徒、或峰主
	/// 在角色面板中被降回门人的双源漂移。
	/// </summary>
	private static bool TryResolveArchivePeakRole(
		City city,
		long zongMenId,
		long actorId,
		out string role,
		out int peakId,
		out string peakName,
		out string rank)
	{
		role = RoleDisciple;
		peakId = XjZongMenCityData.MainPeakId;
		peakName = "主峰";
		rank = "弟子";
		if (zongMenId <= 0L || actorId <= 0L
			|| !XjSectRepository.TryGetBySectId(zongMenId, out var sect)
			|| sect == null)
		{
			return false;
		}

		if (sect.SovereignActorId == actorId)
		{
			role = RoleZongZhu;
			peakId = XjZongMenCityData.MainPeakId;
			peakName = "主峰";
			rank = "宗主";
			return true;
		}

		if (sect.Peaks != null)
		{
			for (int i = 0; i < sect.Peaks.Count; i++)
			{
				var peak = sect.Peaks[i];
				if (peak == null || peak.PeakMasterActorId != actorId || peak.PeakId <= 0) continue;
				role = RolePeakMaster;
				peakId = peak.PeakId;
				peakName = string.IsNullOrWhiteSpace(peak.PeakName) ? "第" + peak.PeakId + "峰" : peak.PeakName.Trim();
				rank = "峰主";
				return true;
			}
		}

		// 老祖位仍由城镇职阶表保存；交回调用方解析，不能被档案兜底降成主峰弟子。
		if (XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeySupremeElders, actorId)) return false;

		// 弟子的具体峰位仍以唯一成员写入口写入的城镇峰脉名单为准。
		// 旧实现无条件回写主峰，导致“收入支峰成功”后角色镜像又被覆盖，
		// 仙鉴诸峰快照自然找不到该弟子。
		if (TryFindDisciplePeak(city, actorId, out int disciplePeakId))
		{
			role = RoleDisciple;
			peakId = disciplePeakId;
			peakName = XjZongMenCityData.GetPeakName(city, disciplePeakId);
			rank = "弟子";
			if (sect.Peaks != null)
			{
				for (int i = 0; i < sect.Peaks.Count; i++)
				{
					var peak = sect.Peaks[i];
					if (peak == null || peak.PeakId != disciplePeakId) continue;
					if (!string.IsNullOrWhiteSpace(peak.PeakName))
					{
						peakName = peak.PeakName.Trim();
					}
					break;
				}
			}
			return true;
		}

		// 没有有效支峰席位时才回归主峰，不再覆盖已经落定的合法峰位。
		role = RoleDisciple;
		peakId = XjZongMenCityData.MainPeakId;
		peakName = XjZongMenCityData.GetPeakName(city, peakId);
		rank = "弟子";
		return true;
	}

	private static void EnsureBalancedDiscipleAssignment(City city, Actor actor)
	{
		if (city?.data == null || actor?.data == null) return;
		long actorId = XjZongMenCityData.GetActorId(actor);
		if (actorId <= 0L
			|| XjZongMenCityData.GetZongZhuId(city) == actorId
			|| XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeySupremeElders, actorId)
			|| TryFindPeakMaster(city, actorId, out _)) return;

		bool hadExistingPeak = TryFindDisciplePeak(city, actorId, out int existingPeakId);
		// 已在有峰主的支峰中就保持原位；只有无峰位或仍挤在主峰的普通门人
		// 才参与均衡。这样旧档中的“全员主峰”会在年度镜像校验时逐步修复，
		// 又不会把玩家已经手动收入支峰的弟子反复搬动。
		if (hadExistingPeak && existingPeakId >= XjZongMenCityData.FirstRegularPeakId) return;

		int selectedPeakId = 0;
		int selectedCount = int.MaxValue;
		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			if (peakId < XjZongMenCityData.FirstRegularPeakId
				|| !XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakId, out long peakMasterId)
				|| peakMasterId <= 0L) continue;
			Actor peakMaster = XjZongMenCityData.ResolveActor(peakMasterId);
			if (peakMaster?.data == null || !peakMaster.isAlive()) continue;
			int count = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakId).Count;
			if (count < selectedCount || (count == selectedCount && (selectedPeakId <= 0 || peakId < selectedPeakId)))
			{
				selectedPeakId = peakId;
				selectedCount = count;
			}
		}

		if (selectedPeakId <= 0)
		{
			// 尚无可承载弟子的支峰时，维持或补入主峰作为兜底。
			if (hadExistingPeak) return;
			selectedPeakId = XjZongMenCityData.MainPeakId;
		}

		if (hadExistingPeak && existingPeakId != selectedPeakId)
		{
			RemoveActorFromList(city, XjZongMenCityData.KeyPeakDisciplePrefix + existingPeakId, actorId);
			RemoveActorFromList(city, XjZongMenCityData.KeyPeakInnerPrefix + existingPeakId, actorId);
		}

		List<long> disciples = XjZongMenCityData.ReadIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + selectedPeakId);
		if (AddUnique(disciples, actorId))
		{
			XjZongMenCityData.WriteIdList(city, XjZongMenCityData.KeyPeakDisciplePrefix + selectedPeakId, disciples);
		}
	}

	private static bool TryFindPeakMaster(City city, long actorId, out int peakId)
	{
		peakId = 0;
		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (XjZongMenCityData.TryReadActorId(city, XjZongMenCityData.KeyPeakFengZhuPrefix + peakIds[i], out long value)
				&& value == actorId)
			{
				peakId = peakIds[i];
				return true;
			}
		}
		return false;
	}

	private static bool TryFindDisciplePeak(City city, long actorId, out int peakId)
	{
		peakId = 0;
		List<int> peakIds = XjZongMenCityData.ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeyPeakDisciplePrefix + peakIds[i], actorId)
				|| XjZongMenCityData.ContainsId(city, XjZongMenCityData.KeyPeakInnerPrefix + peakIds[i], actorId))
			{
				peakId = peakIds[i];
				return true;
			}
		}
		return false;
	}

	private static void RemoveActorFromList(City city, string key, long actorId)
	{
		List<long> ids = XjZongMenCityData.ReadIdList(city, key);
		if (RemoveAll(ids, actorId)) XjZongMenCityData.WriteIdList(city, key, ids);
	}

	private static bool AddUnique(List<long> ids, long actorId)
	{
		if (ids == null || actorId <= 0L || ids.Contains(actorId)) return false;
		ids.Add(actorId);
		return true;
	}

	private static bool RemoveAll(List<long> ids, long actorId)
	{
		if (ids == null || actorId <= 0L) return false;
		bool changed = false;
		for (int i = ids.Count - 1; i >= 0; i--)
		{
			if (ids[i] != actorId) continue;
			ids.RemoveAt(i);
			changed = true;
		}
		return changed;
	}
}

