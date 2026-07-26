using System.Collections.Generic;

namespace XuanJianVNext.Data.Sect;

internal static class XjSectLectureType
{
	internal const string ZhenRen = "ZhenRen";
	internal const string ZhenJun = "ZhenJun";
}

internal sealed class XjSectLectureArchiveRecord
{
	public int SchemaVersion { get; set; } = 1;
	public long LectureId { get; set; }
	public long SectId { get; set; }
	public string LectureType { get; set; } = XjSectLectureType.ZhenRen;
	public long LecturerActorId { get; set; }
	public string LecturerName { get; set; } = string.Empty;
	public int Year { get; set; }
	public bool HeldInsideSecretRealm { get; set; }
	public long SecretRealmId { get; set; }
	public List<long> AttendeeActorIds { get; set; } = new List<long>();
	public int AttendeeCount { get; set; }
	public string Summary { get; set; } = string.Empty;
}
