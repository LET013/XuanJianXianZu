using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.ZongMen;

internal static partial class XjZongMenCityData
{
	#region 山峰管理

	internal static List<(int id, string name)> GetPeaks(City city)
	{
		List<(int id, string name)> result = new List<(int id, string name)>();
		if (city?.data == null) return result;
		List<int> peakIds = ReadPeakIds(city);
		peakIds.Sort();
		for (int i = 0; i < peakIds.Count; i++) result.Add((peakIds[i], GetPeakName(city, peakIds[i])));
		return result;
	}

	internal static List<int> GetRegularPeakIds(City city)
	{
		List<int> result = new List<int>();
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (peakIds[i] >= FirstRegularPeakId) result.Add(peakIds[i]);
		}
		result.Sort();
		return result;
	}

	internal static string GetPeakName(City city, int peakId)
	{
		if (city?.data == null) return "无名峰";
		city.data.get(KeyPeakNamePrefix + peakId, out string name, string.Empty);
		if (!string.IsNullOrWhiteSpace(name)) return name.Trim();
		return peakId switch
		{
			MainPeakId => "主峰",
			SupremePeakId => ResolveDongTianPeakName(city),
			_ => "第" + peakId + "峰"
		};
	}

	internal static string GetDongTianPeakName(City city)
	{
		return GetPeakName(city, SupremePeakId);
	}

	internal static Actor GetPeakFengZhu(City city, int peakId)
	{
		if (!TryReadActorId(city, KeyPeakFengZhuPrefix + peakId, out long actorId)) return null;
		Actor actor = ResolveActor(actorId);
		return actor?.data != null && actor.isAlive() ? actor : null;
	}

	internal static void SetPeakFengZhu(City city, int peakId, Actor actor)
	{
		XjZongMenMembershipWriter.AssignPeakMaster(city, peakId, actor, GetCurrentYearOrZero(), "SetPeakFengZhu");
	}

	internal static void ClearPeakFengZhu(City city, int peakId)
	{
		XjZongMenMembershipWriter.ClearPeakMaster(city, peakId, GetCurrentYearOrZero(), "ClearPeakFengZhu");
	}

	internal static List<Actor> GetPeakDisciples(City city, int peakId)
	{
		List<Actor> result = new List<Actor>();
		if (city?.data == null) return result;
		List<long> ids = ReadIdList(city, KeyPeakDisciplePrefix + peakId);
		for (int i = 0; i < ids.Count; i++)
		{
			Actor actor = ResolveActor(ids[i]);
			if (actor?.data != null && actor.isAlive()) result.Add(actor);
		}
		return result;
	}

	internal static void AddPeakDisciple(City city, int peakId, Actor actor)
	{
		XjZongMenMembershipWriter.AssignDisciple(city, peakId, actor, GetCurrentYearOrZero(), "AddPeakDisciple");
	}

	internal static void RemoveActorFromAllPeaks(City city, long actorId)
	{
		if (city?.data == null || actorId <= 0L) return;
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			List<long> disciples = ReadIdList(city, KeyPeakDisciplePrefix + peakId);
			bool changed = false;
			for (int j = disciples.Count - 1; j >= 0; j--)
			{
				if (disciples[j] != actorId) continue;
				disciples.RemoveAt(j);
				changed = true;
			}
			if (changed) WriteIdList(city, KeyPeakDisciplePrefix + peakId, disciples);

			List<long> inner = ReadIdList(city, KeyPeakInnerPrefix + peakId);
			changed = false;
			for (int j = inner.Count - 1; j >= 0; j--)
			{
				if (inner[j] != actorId) continue;
				inner.RemoveAt(j);
				changed = true;
			}
			if (changed) WriteIdList(city, KeyPeakInnerPrefix + peakId, inner);
		}

		Actor actor = ResolveActor(actorId);
		if (actor?.data != null) XjZongMenMembershipWriter.ReconcileActorMirror(city, actor, GetCurrentYearOrZero(), "RemovedFromAllPeaks");
	}

	internal static int GetPeakDiscipleCount(City city, int peakId)
	{
		return city?.data == null ? 0 : ReadIdList(city, KeyPeakDisciplePrefix + peakId).Count;
	}

	#endregion

	#region 山峰创建与治理

	internal static void EnsureMainPeak(City city)
	{
		if (city?.data == null) return;
		List<int> peakIds = ReadPeakIds(city);
		if (!peakIds.Contains(MainPeakId))
		{
			peakIds.Insert(0, MainPeakId);
			WritePeakIds(city, peakIds);
		}
		city.data.get(KeyPeakNamePrefix + MainPeakId, out string mainPeakName, string.Empty);
		if (string.IsNullOrWhiteSpace(mainPeakName)) city.data.set(KeyPeakNamePrefix + MainPeakId, "主峰");
		city.data.get(KeyPeakTypePrefix + MainPeakId, out string mainPeakType, string.Empty);
		if (!string.Equals(mainPeakType, "MainPeak", StringComparison.Ordinal)) city.data.set(KeyPeakTypePrefix + MainPeakId, "MainPeak");
	}

	internal static int CreatePeak(City city, string name, string type)
	{
		if (city?.data == null) return -1;
		EnsureMainPeak(city);
		List<int> peakIds = ReadPeakIds(city);
		if (peakIds.Count >= MaxPeakCount || GetRegularPeakIds(city).Count >= MaxRegularPeakCount) return -1;

		int newId = FirstRegularPeakId;
		while (peakIds.Contains(newId)) newId++;
		peakIds.Add(newId);
		peakIds.Sort();
		WritePeakIds(city, peakIds);
		city.data.set(KeyPeakNamePrefix + newId, NormalizePeakName(name));
		city.data.set(KeyPeakTypePrefix + newId, string.IsNullOrWhiteSpace(type) ? "CultivatorPeak" : type);
		city.data.set(KeyPeakFengZhuPrefix + newId, string.Empty);
		WriteIdList(city, KeyPeakDisciplePrefix + newId, new List<long>());
		WriteIdList(city, KeyPeakInnerPrefix + newId, new List<long>());
		return newId;
	}

	internal static bool RemovePeak(City city, int peakId)
	{
		if (city?.data == null || peakId < FirstRegularPeakId) return false;
		List<int> peakIds = ReadPeakIds(city);
		if (!peakIds.Remove(peakId)) return false;
		WritePeakIds(city, peakIds);
		city.data.set(KeyPeakNamePrefix + peakId, string.Empty);
		city.data.set(KeyPeakTypePrefix + peakId, string.Empty);
		city.data.set(KeyPeakFengZhuPrefix + peakId, string.Empty);
		WriteIdList(city, KeyPeakDisciplePrefix + peakId, new List<long>());
		WriteIdList(city, KeyPeakInnerPrefix + peakId, new List<long>());
		return true;
	}

	internal static int FindVacantRegularPeak(City city)
	{
		List<int> regularPeakIds = GetRegularPeakIds(city);
		for (int i = 0; i < regularPeakIds.Count; i++)
		{
			if (!TryReadActorId(city, KeyPeakFengZhuPrefix + regularPeakIds[i], out _)) return regularPeakIds[i];
		}
		return -1;
	}

	internal static int CreateDynamicPeakForFounder(City city, Actor actor, int currentYear)
	{
		return AssignOrCreatePeakForFounder(city, actor, currentYear, out _);
	}

	internal static int AssignOrCreatePeakForFounder(City city, Actor actor, int currentYear, out bool created)
	{
		created = false;
		if (city?.data == null || actor?.data == null || !IsEligiblePeakFounder(city, actor)) return -1;

		int vacantPeakId = FindVacantRegularPeak(city);
		if (vacantPeakId >= FirstRegularPeakId)
		{
			return XjZongMenMembershipWriter.AssignPeakMaster(city, vacantPeakId, actor, currentYear, "FillVacantPeakMaster")
				? vacantPeakId
				: -1;
		}

		if (!CanCreateRegularPeak(city)) return -1;
		string peakName = GeneratePeakName(city);
		int peakId = CreatePeak(city, peakName, "CultivatorPeak");
		if (peakId < 0) return -1;
		if (!XjZongMenMembershipWriter.AssignPeakMaster(city, peakId, actor, currentYear, "DynamicPeakFounder"))
		{
			RemovePeak(city, peakId);
			return -1;
		}
		created = true;
		return peakId;
	}

	private static bool CanCreateRegularPeak(City city)
	{
		List<int> peakIds = ReadPeakIds(city);
		List<int> regularPeakIds = GetRegularPeakIds(city);
		if (peakIds.Count >= MaxPeakCount || regularPeakIds.Count >= MaxRegularPeakCount) return false;
		if (FindVacantRegularPeak(city) >= FirstRegularPeakId) return false;

		long zongZhuId = GetZongZhuId(city);
		HashSet<long> elders = new HashSet<long>(ReadIdList(city, KeySupremeElders));
		int availableForRegularRoles = 0;
		List<long> memberIds = ReadIdList(city, KeyMemberIds);
		for (int i = 0; i < memberIds.Count; i++)
		{
			if (memberIds[i] == zongZhuId || elders.Contains(memberIds[i])) continue;
			Actor member = ResolveActor(memberIds[i]);
			if (member?.data != null && member.isAlive() && IsCultivator(member)) availableForRegularRoles++;
		}

		// 新峰必须同时有峰主和至少五名弟子；有宗主时主峰也保留一名门人。
		int targetRegularPeakCount = regularPeakIds.Count + 1;
		int requiredMembers = targetRegularPeakCount * (MinDisciplesPerPeak + 1) + (zongZhuId > 0L ? 1 : 0);
		return availableForRegularRoles >= requiredMembers;
	}

	internal static bool NormalizeStoredPeakNames(City city)
	{
		if (city?.data == null) return false;
		bool changed = false;
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			int peakId = peakIds[i];
			string nextName;
			if (peakId == MainPeakId) nextName = "主峰";
			else if (peakId == SupremePeakId) nextName = ResolveDongTianPeakName(city);
			else nextName = NormalizePeakName(GetPeakName(city, peakId));
			city.data.get(KeyPeakNamePrefix + peakId, out string currentName, string.Empty);
			if (string.Equals(currentName ?? string.Empty, nextName, StringComparison.Ordinal)) continue;
			city.data.set(KeyPeakNamePrefix + peakId, nextName);
			changed = true;
		}
		return changed;
	}

	internal static bool EnsureDongTianPeak(City city)
	{
		if (city?.data == null) return false;
		EnsureMainPeak(city);

		List<int> peakIds = ReadPeakIds(city);
		bool alreadyExisted = peakIds.Contains(SupremePeakId);
		for (int i = peakIds.Count - 1; i >= 0; i--)
		{
			int peakId = peakIds[i];
			if (peakId == SupremePeakId) continue;
			city.data.get(KeyPeakTypePrefix + peakId, out string legacyType, string.Empty);
			if (!string.Equals(legacyType, "DongTian", StringComparison.Ordinal)) continue;

			// 旧存档可能把洞天写在普通峰 ID 上。洞天老祖由独立列表保存，
			// 因此只需移除错误峰位并迁移到固定的 1 号洞天位。
			alreadyExisted = true;
			RemovePeak(city, peakId);
		}

		peakIds = ReadPeakIds(city);
		while (!peakIds.Contains(SupremePeakId) && peakIds.Count >= MaxPeakCount)
		{
			List<int> regular = GetRegularPeakIds(city);
			if (regular.Count == 0) break;
			RemovePeak(city, regular[regular.Count - 1]);
			peakIds = ReadPeakIds(city);
		}
		if (!peakIds.Contains(SupremePeakId))
		{
			peakIds.Add(SupremePeakId);
			peakIds.Sort();
			WritePeakIds(city, peakIds);
		}
		city.data.set(KeyPeakNamePrefix + SupremePeakId, ResolveDongTianPeakName(city));
		city.data.set(KeyPeakTypePrefix + SupremePeakId, "DongTian");
		city.data.set(KeyPeakFengZhuPrefix + SupremePeakId, string.Empty);
		WriteIdList(city, KeyPeakDisciplePrefix + SupremePeakId, new List<long>());
		WriteIdList(city, KeyPeakInnerPrefix + SupremePeakId, new List<long>());
		return !alreadyExisted;
	}

	private static string ResolveDongTianPeakName(City city)
	{
		string zongMenName = GetZongMenName(city);
		if (string.IsNullOrWhiteSpace(zongMenName)) return "宗门洞天";
		string name = zongMenName.Trim();
		if (name.EndsWith("洞天", StringComparison.Ordinal)) return name;
		string[] suffixes = { "宗门", "道宫", "仙宫", "玄宫", "宗", "门", "观", "阁", "府", "宫", "院", "寺" };
		for (int i = 0; i < suffixes.Length; i++)
		{
			if (!name.EndsWith(suffixes[i], StringComparison.Ordinal) || name.Length <= suffixes[i].Length) continue;
			name = name.Substring(0, name.Length - suffixes[i].Length);
			break;
		}
		return (string.IsNullOrWhiteSpace(name) ? "宗门" : name) + "洞天";
	}

	internal static bool HasDongTianPeak(City city)
	{
		if (city?.data == null) return false;
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			city.data.get(KeyPeakTypePrefix + peakIds[i], out string type, string.Empty);
			if (peakIds[i] == SupremePeakId || string.Equals(type, "DongTian", StringComparison.Ordinal)) return true;
		}
		return false;
	}

	internal static bool IsEligiblePeakFounder(City city, Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || !IsMember(city, actor) || HasActorAnyPeak(city, actor)) return false;
		int realmLevel = GetRealmLevel(actor);
		if (realmLevel >= JinDanRealmLevel) return false;
		if (realmLevel >= ZiFuRealmLevel) return true;
		if (realmLevel != 3) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int xianJiCount);
		return XjDaoXingStageRules.IsZhuJiLateOrHigher(realmId, zhenYuan, xianJiCount);
	}

	internal static bool IsEligiblePeakFounder(City city, in XjZongMenMemberSnapshot member)
	{
		if (!member.IsValidMember || member.RealmLevel >= JinDanRealmLevel || HasActorAnyPeak(city, member.ActorId)) return false;
		return member.RealmLevel >= ZiFuRealmLevel || (member.RealmLevel == 3 && member.IsFoundationLateOrHigher);
	}

	internal static bool HasActorAnyPeak(City city, Actor actor)
	{
		return HasActorAnyPeak(city, GetActorId(actor));
	}

	internal static bool HasActorAnyPeak(City city, long actorId)
	{
		if (actorId <= 0L) return false;
		List<int> peakIds = ReadPeakIds(city);
		for (int i = 0; i < peakIds.Count; i++)
		{
			if (TryReadActorId(city, KeyPeakFengZhuPrefix + peakIds[i], out long fengZhuId) && fengZhuId == actorId) return true;
		}
		return false;
	}

	internal static string NormalizePeakName(string name)
	{
		string value = string.IsNullOrWhiteSpace(name) ? "无名峰" : name.Trim();
		if (!value.EndsWith("峰", StringComparison.Ordinal)) value += "峰";
		return value.Length <= 3 ? value : value.Substring(0, 2) + "峰";
	}

	#endregion
}
