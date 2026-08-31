using System;
using System.Globalization;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门身份的唯一只读投影。运行期逻辑、角色信息页和宗门 UI 共同读取本快照，
/// 不再重复构建 State / DisplayItem / UiIdentityItem 三套相同字段。
/// </summary>
internal readonly struct XjSectIdentitySnapshot
{
	internal static XjSectIdentitySnapshot Empty { get; } = new XjSectIdentitySnapshot(
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
			string year = JoinYear > 0 ? "，入门：" + XjChronology.FormatYear(JoinYear) : string.Empty;
			return actorPrefix + ZongMenName + " · " + rank + year;
		}
	}

	internal XjSectIdentitySnapshot(
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

internal static class XjSectIdentityReader
{
	internal static XjSectIdentitySnapshot BuildIdentity(Actor actor)
	{
		if (actor?.data == null
			|| !XjCultivationEligibility.CanReceiveXuanJianContent(actor)
			|| !XjCultivationEligibility.HasCultivationAptitudeTrait(actor))
		{
			return new XjSectIdentitySnapshot(false, 0L, string.Empty, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty, "ActorInvalid");
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string actorName = actor.getName() ?? string.Empty;
		if (actorId <= 0L
			|| !XjSectAuthorityStore.TryGetMember(actorId, out XjSectMemberArchiveRecord member)
			|| member == null
			|| !XjSectRepository.TryGetBySectId(member.SectId, out XjSectArchiveRecord sect)
			|| sect == null)
		{
			return new XjSectIdentitySnapshot(false, actorId, actorName, 0L, string.Empty, string.Empty, 0, string.Empty, 0, string.Empty, "NoZongMen");
		}

		string peakName = XjSectProjection.ResolvePeakName(sect, member.Role, member.PeakId);
		return new XjSectIdentitySnapshot(
			true,
			actorId,
			actorName,
			sect.SectId,
			sect.Name ?? string.Empty,
			XjSectMemberRole.RankDisplay(member.Role),
			member.JoinYear,
			XjSectMemberRole.Normalize(member.Role),
			member.PeakId,
			peakName,
			"Authority");
	}

	internal static XjSectIdentitySnapshot BuildForCity(City city)
	{
		if (city?.data == null
			|| !XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect)
			|| sect?.SectId <= 0L)
		{
			return new XjSectIdentitySnapshot(false, 0L, string.Empty, 0L, string.Empty,
				string.Empty, 0, string.Empty, 0, string.Empty, "NoSect");
		}

		return new XjSectIdentitySnapshot(
			true,
			0L,
			string.Empty,
			sect.SectId,
			sect.Name ?? string.Empty,
			"城镇宗门",
			Math.Max(0, sect.FoundingYear),
			string.Empty,
			0,
			string.Empty,
			"Authority");
	}
}
