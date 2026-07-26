using System.Globalization;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.ZongMen;

/// <summary>
/// 宗门身份的唯一只读投影。运行期逻辑、角色信息页和宗门 UI 共同读取本快照，
/// 不再重复构建 State / DisplayItem / UiIdentityItem 三套相同字段。
/// </summary>
internal readonly struct XjZongMenIdentitySnapshot
{
	internal static XjZongMenIdentitySnapshot Empty { get; } = new XjZongMenIdentitySnapshot(
		false,
		0L,
		string.Empty,
		0L,
		string.Empty,
		string.Empty,
		0,
		string.Empty,
		0,
		string.Empty,
		"Empty");

	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly string Rank;
	internal readonly int JoinYear;
	internal readonly string Role;
	internal readonly int PeakId;
	internal readonly string PeakName;
	internal readonly string ReasonCode;

	internal string DisplayText
	{
		get
		{
			if (!Found || ZongMenId <= 0L)
			{
				return string.IsNullOrWhiteSpace(ActorName) ? "暂无宗门" : ActorName + "：暂无宗门";
			}

			string actorPrefix = string.IsNullOrWhiteSpace(ActorName) ? string.Empty : ActorName + "：";
			string rank = string.IsNullOrWhiteSpace(Rank) ? "门人" : Rank;
			string year = JoinYear > 0 ? "，入门：" + JoinYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
			return actorPrefix + ZongMenName + " · " + rank + year;
		}
	}

	internal XjZongMenIdentitySnapshot(
		bool found,
		long actorId,
		string actorName,
		long zongMenId,
		string zongMenName,
		string rank,
		int joinYear,
		string role,
		int peakId,
		string peakName,
		string reasonCode)
	{
		Found = found && zongMenId > 0L;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		Rank = rank ?? string.Empty;
		JoinYear = joinYear < 0 ? 0 : joinYear;
		Role = role ?? string.Empty;
		PeakId = peakId < 0 ? 0 : peakId;
		PeakName = peakName ?? string.Empty;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjZongMenAccessor
{
	internal static XjZongMenIdentitySnapshot BuildIdentity(Actor actor)
	{
		if (actor?.data == null || !XjCultivationEligibility.HasCultivationAptitudeTrait(actor))
		{
			return new XjZongMenIdentitySnapshot(false, 0L, string.Empty, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty, "ActorInvalid");
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string actorName = actor.getName() ?? string.Empty;
		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long zongMenId);
		if (zongMenId <= 0L && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenId, out int legacyId))
		{
			zongMenId = legacyId;
		}
		XjSectArchiveRecord sectRecord = null;
		if (zongMenId <= 0L && XjSectRepository.TryGetByActor(actor, out sectRecord) && sectRecord?.SectId > 0L)
		{
			zongMenId = sectRecord.SectId;
		}
		if (zongMenId <= 0L)
		{
			return new XjZongMenIdentitySnapshot(false, actorId, actorName, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty, "NoZongMen");
		}
		if (sectRecord == null) XjSectRepository.TryGetBySectId(zongMenId, out sectRecord);
		if ((!XjZongMenCityData.TryResolveZongMenCity(zongMenId, out City city) || !XjZongMenCityData.IsMember(city, actor))
			&& sectRecord == null)
		{
			return new XjZongMenIdentitySnapshot(false, actorId, actorName, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty, "StaleZongMen");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenRank, out string rank);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenJoinYear, out int joinYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenRole, out string role);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZongMenPeakId, out int peakId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenPeakName, out string peakName);
		if (sectRecord != null && !string.IsNullOrWhiteSpace(sectRecord.Name)) name = sectRecord.Name.Trim();
		rank = NormalizeRank(rank, role);

		return new XjZongMenIdentitySnapshot(
			true,
			actorId,
			actorName,
			zongMenId,
			name,
			rank,
			joinYear,
			role,
			peakId,
			peakName,
			string.IsNullOrWhiteSpace(name) ? "NameMissing" : "Found");
	}

	private static string NormalizeRank(string rank, string role)
	{
		string value = (rank ?? string.Empty).Trim();
		string roleValue = (role ?? string.Empty).Trim();
		if (roleValue == "宗主" || value.Contains("宗主")) return "宗主";
		if (roleValue == "峰主" || value.Contains("峰主")) return "峰主";
		if (roleValue == "太上长老" || value.Contains("老祖") || value.Contains("太上")) return "老祖";
		if (roleValue == "弟子" || value.Contains("弟子")) return "弟子";
		if (value.Contains("门人")) return "门人";
		return string.IsNullOrWhiteSpace(value) ? "门人" : value;
	}

	internal static XjZongMenIdentitySnapshot BuildForCity(City city)
	{
		long zongMenId = XjZongMenCityData.GetZongMenId(city);
		string name = XjZongMenCityData.GetZongMenName(city);
		return new XjZongMenIdentitySnapshot(
			zongMenId > 0L,
			0L,
			string.Empty,
			zongMenId,
			name,
			"城镇宗门",
			XjZongMenCityData.GetCreationYear(city),
			string.Empty,
			0,
			string.Empty,
			zongMenId > 0L ? "CityFound" : "NoZongMen");
	}

	internal static void WriteIdentity(Actor actor, in XjZongMenIdentitySnapshot identity)
	{
		if (actor?.data == null) return;
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor))
		{
			SetLongIfChanged(actor, XjActorDataKeys.XjZongMenId, 0L);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenName, string.Empty);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRank, string.Empty);
			SetIntIfChanged(actor, XjActorDataKeys.XjZongMenJoinYear, 0);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRole, string.Empty);
			SetIntIfChanged(actor, XjActorDataKeys.XjZongMenPeakId, 0);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenPeakName, string.Empty);
			return;
		}

		if (!identity.Found || identity.ZongMenId <= 0L)
		{
			SetLongIfChanged(actor, XjActorDataKeys.XjZongMenId, 0L);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenName, string.Empty);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRank, string.Empty);
			SetIntIfChanged(actor, XjActorDataKeys.XjZongMenJoinYear, 0);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRole, string.Empty);
			SetIntIfChanged(actor, XjActorDataKeys.XjZongMenPeakId, 0);
			SetStringIfChanged(actor, XjActorDataKeys.XjZongMenPeakName, string.Empty);
			return;
		}

		SetLongIfChanged(actor, XjActorDataKeys.XjZongMenId, identity.ZongMenId);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenName, identity.ZongMenName);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRank, identity.Rank);
		SetIntIfChanged(actor, XjActorDataKeys.XjZongMenJoinYear, identity.JoinYear);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenRole, identity.Role);
		SetIntIfChanged(actor, XjActorDataKeys.XjZongMenPeakId, identity.PeakId);
		SetStringIfChanged(actor, XjActorDataKeys.XjZongMenPeakName, identity.PeakName);
	}

	private static void SetStringIfChanged(Actor actor, string key, string value)
	{
		XjActorAccessor.TryGetString(actor, key, out string current);
		string next = value ?? string.Empty;
		if (!string.Equals(current ?? string.Empty, next, System.StringComparison.Ordinal))
		{
			XjActorAccessor.SetString(actor, key, next);
		}
	}

	private static void SetIntIfChanged(Actor actor, string key, int value)
	{
		XjActorAccessor.TryGetInt(actor, key, out int current);
		if (current != value) XjActorAccessor.SetInt(actor, key, value);
	}

	private static void SetLongIfChanged(Actor actor, string key, long value)
	{
		XjActorAccessor.TryGetLong(actor, key, out long current);
		if (current != value) XjActorAccessor.SetLong(actor, key, value);
	}
}
