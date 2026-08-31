namespace XuanJianVNext.Data.Sect;

/// <summary>
/// 宗门成员职阶的权威枚举。角色字段和城镇列表只允许作为单向显示镜像，
/// 不得参与正常业务判断。
/// </summary>
internal static class XjSectMemberRole
{
	internal const string Member = "member";
	internal const string Disciple = "disciple";
	internal const string PeakMaster = "fengzhu";
	internal const string SupremeElder = "supreme_elder";
	internal const string Sovereign = "zongzhu";

	internal static string Normalize(string role)
	{
		string value = (role ?? string.Empty).Trim();
		return value switch
		{
			Sovereign or "宗主" => Sovereign,
			PeakMaster or "峰主" => PeakMaster,
			SupremeElder or "太上长老" or "老祖" or "洞天修士" or "洞天驻修" => SupremeElder,
			Disciple or "弟子" => Disciple,
			_ => Member
		};
	}

	internal static int Priority(string role)
	{
		return Normalize(role) switch
		{
			Sovereign => 5,
			PeakMaster => 4,
			SupremeElder => 3,
			Disciple => 2,
			_ => 1
		};
	}

	internal static string RankDisplay(string role)
	{
		return Normalize(role) switch
		{
			Sovereign => "宗主",
			PeakMaster => "峰主",
			SupremeElder => "洞天修士",
			Disciple => "弟子",
			_ => "门人"
		};
	}
}

/// <summary>
/// ActorId -> SectId 的唯一持久权威记录。PeakId 与 Role 一并保存，
/// 避免成员名单、峰脉名单和角色镜像分别成为第二数据源。
/// </summary>
internal sealed class XjSectMemberArchiveRecord
{
	public int SchemaVersion { get; set; } = XjSectDomainSchema.CurrentVersion;
	public long ActorId { get; set; }
	public long SectId { get; set; }
	public int JoinYear { get; set; }
	public int PeakId { get; set; }
	public string Role { get; set; } = XjSectMemberRole.Member;
	public int LastUpdatedYear { get; set; }
}
