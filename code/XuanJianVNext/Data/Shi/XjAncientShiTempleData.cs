using System;
using System.Collections.Generic;

namespace XuanJianVNext.Data.Shi;

internal static class XjAncientShiVowIds
{
	internal const string ProtectLife = "ancient_vow_protect_life";
	internal const string RelieveSuffering = "ancient_vow_relieve_suffering";
	internal const string IlluminateMind = "ancient_vow_illuminate_mind";
	internal const string GuardPurity = "ancient_vow_guard_purity";
	internal const string CarryLamp = "ancient_vow_carry_lamp";
	internal const string ShelterWorld = "ancient_vow_shelter_world";

	internal static readonly string[] All =
	{
		ProtectLife,
		RelieveSuffering,
		IlluminateMind,
		GuardPurity,
		CarryLamp,
		ShelterWorld
	};

	internal static bool IsKnown(string value)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		for (int i = 0; i < All.Length; i++)
			if (string.Equals(All[i], value, StringComparison.Ordinal)) return true;
		return false;
	}
}

internal readonly struct XjAncientShiVowDefinition
{
	internal readonly string Id;
	internal readonly string Name;
	internal readonly string ShortText;
	internal readonly string FullText;
	internal readonly string TempleTitle;

	internal XjAncientShiVowDefinition(string id, string name, string shortText, string fullText, string templeTitle)
	{
		Id = id ?? string.Empty;
		Name = name ?? string.Empty;
		ShortText = shortText ?? string.Empty;
		FullText = fullText ?? string.Empty;
		TempleTitle = templeTitle ?? string.Empty;
	}
}

internal static class XjAncientShiVowCatalog
{
	private static readonly XjAncientShiVowDefinition[] Definitions =
	{
		new XjAncientShiVowDefinition(XjAncientShiVowIds.ProtectLife, "护生宏愿", "护生全性", "愿所行之处少一分妄杀，使有缘众生得全性命、各安其性。", "护生"),
		new XjAncientShiVowDefinition(XjAncientShiVowIds.RelieveSuffering, "济厄宏愿", "见厄则济", "愿见苦厄不袖手，能以己力解一分困顿，便不使其沉沦。", "济厄"),
		new XjAncientShiVowDefinition(XjAncientShiVowIds.IlluminateMind, "明心宏愿", "明心见性", "愿有缘者得见本心，不以强力夺其本性，不以言辞强纳门墙。", "明心"),
		new XjAncientShiVowDefinition(XjAncientShiVowIds.GuardPurity, "守净宏愿", "守净无夺", "愿守一念清净，自证己身，不借众生性命强成自家修持。", "清净"),
		new XjAncientShiVowDefinition(XjAncientShiVowIds.CarryLamp, "传灯宏愿", "持灯不绝", "愿法意有所承而不强求师徒，使后来有缘者仍能见一盏灯火。", "传灯"),
		new XjAncientShiVowDefinition(XjAncientShiVowIds.ShelterWorld, "护世宏愿", "应身护世", "愿所证应身不为世间之害，力所能及时庇一方、镇一厄。", "护世")
	};

	internal static bool TryGet(string id, out XjAncientShiVowDefinition definition)
	{
		for (int i = 0; i < Definitions.Length; i++)
		{
			if (!string.Equals(Definitions[i].Id, id, StringComparison.Ordinal)) continue;
			definition = Definitions[i];
			return true;
		}
		definition = default;
		return false;
	}

	internal static string GetDisplay(string id)
	{
		return TryGet(id, out XjAncientShiVowDefinition definition)
			? definition.Name + "：" + definition.FullText
			: "守本性、立大愿";
	}

	internal static string GetShortDisplay(string id)
	{
		return TryGet(id, out XjAncientShiVowDefinition definition)
			? definition.Name + " · " + definition.ShortText
			: "本愿未载";
	}

	internal static string GetTempleTitle(string id)
	{
		return TryGet(id, out XjAncientShiVowDefinition definition)
			? definition.TempleTitle
			: "清净";
	}
}

internal sealed class XjAncientShiTempleRecord
{
	public long TempleId;
	public string Name = string.Empty;
	public long CityId;
	public string CityName = string.Empty;
	public int FoundedYear;
	public long FounderActorId;
	public string FounderName = string.Empty;
	public long AbbotActorId;
	public string AbbotName = string.Empty;
	public string PrincipalVowId = string.Empty;
	public int LivingMemberCount;
	public int VowFoundation;
	public int DharmaArchive;
	public int ResponseLegacy;
	public int LastActiveYear;
	public int LastRefreshYear;
	// 仅用于寺中遗经自悟的低频探测节流；不是师承、度化或人口配额。
	public int LastRecruitmentProbeYear;
	// 古释证毕/陨落后，自证金地可留给同寺后来者悟道；这里只登记稳定域ID，
	// 不把死者金地转成某个活人的私产。
	public List<string> LegacyJinDiDomainIds = new List<string>();
	public List<long> MemberActorIds = new List<long>();
}

internal sealed class XjAncientShiTempleWorldArchiveData
{
	public int SchemaVersion = 3;
	public long NextTempleId = 1L;
	public List<XjAncientShiTempleRecord> Temples = new List<XjAncientShiTempleRecord>();
}
