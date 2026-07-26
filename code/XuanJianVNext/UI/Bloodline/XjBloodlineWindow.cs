using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Family;

namespace XuanJianVNext.UI.Bloodline;

internal sealed class XjBloodlineFamilyItem
{
	internal long FamilyStableId;
	internal int ConfirmedCount;
	internal int PendingCount;
	internal string SampleName = string.Empty;
	internal string CityName = string.Empty;
	internal string ClanName = string.Empty;
	internal bool IsMigrationBranch;
	internal long SourceFamilyId;
	internal string SourceFamilyName = string.Empty;
	internal long OriginCityId;
	internal string OriginCityName = string.Empty;
	internal readonly List<string> Members = new List<string>();
	internal readonly List<string> Pending = new List<string>();
	internal readonly List<string> Marriages = new List<string>();
	internal int GongFaCount;
	internal int QiuJinFaCount;
	internal int CaiQiCount;
	internal int CaiQiFaCount;
	internal int FaBaoCount;
	internal int HighestRealmScore;
	internal int RecentChronicleYear;
	internal int CultivatorCount;
	internal int RealmMemberScore;
	internal int FamilyScore;
	internal string HighestRealm = string.Empty;
	internal string DominantDaoTu = string.Empty;
	internal string BloodlineQuality = string.Empty;
	internal readonly Dictionary<string, int> DaoTuCounts = new Dictionary<string, int>(StringComparer.Ordinal);
	internal readonly List<string> MemberCards = new List<string>();
	internal readonly List<string> Chronicle = new List<string>();
}

internal static class XjBloodlineReadModel
{
	private const int MaxFamilies = 60;
	private const int MaxRowsPerFamily = 8;

	internal static IReadOnlyList<XjBloodlineFamilyItem> Build()
	{
		Dictionary<long, XjBloodlineFamilyItem> items = new Dictionary<long, XjBloodlineFamilyItem>();
		IReadOnlyList<XjFamilyLedgerAggregate> aggregates = XjFamilyMemberLedger.ReadAggregateSnapshot();
		Dictionary<long, string> governingCitiesByFamily = BuildGoverningCityNamesByFamily();
		Dictionary<long, string> familyNames = new Dictionary<long, string>();
		for (int i = 0; i < aggregates.Count; i++)
		{
			XjFamilyLedgerAggregate aggregate = aggregates[i];
			if (aggregate.FamilyStableId <= 0L) continue;
			familyNames[aggregate.FamilyStableId] = XjFamilyDisplayNameResolver.Resolve(aggregate.FamilyStableId);
			XjBloodlineFamilyItem item = GetOrCreate(items, aggregate.FamilyStableId);
			item.ConfirmedCount = aggregate.TotalCount;
			item.SampleName = aggregate.Representative;
			item.ClanName = aggregate.DisplayName;
			item.CultivatorCount = aggregate.CultivatorCount;
			item.HighestRealm = aggregate.HighestRealm;
			item.HighestRealmScore = aggregate.HighestRealmOrder;
			item.RealmMemberScore = aggregate.JinDanCount * GetRealmFamilyPoints(5)
				+ aggregate.ZiFuCount * GetRealmFamilyPoints(4)
				+ Math.Max(0, aggregate.CultivatorCount - aggregate.JinDanCount - aggregate.ZiFuCount) * GetRealmFamilyPoints(2);
			if (governingCitiesByFamily.TryGetValue(aggregate.FamilyStableId, out string cityName)) item.CityName = cityName;
			if (string.IsNullOrWhiteSpace(item.SampleName))
			{
				item.SampleName = XjFamilyDisplayNameResolver.Resolve(aggregate.FamilyStableId);
			}
			if (string.IsNullOrWhiteSpace(item.ClanName)) item.ClanName = XjFamilyDisplayNameResolver.Resolve(aggregate.FamilyStableId);
		}

		foreach (XjBloodlineFamilyItem item in items.Values)
		{
			PopulateBranchDetails(item, familyNames);
			PopulateFamilyDetails(item);
		}

		List<XjBloodlineFamilyItem> result = new List<XjBloodlineFamilyItem>(items.Values);
		result.Sort((left, right) =>
		{
			int byCount = right.ConfirmedCount.CompareTo(left.ConfirmedCount);
			return byCount != 0 ? byCount : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});

		return result.Count <= MaxFamilies ? result : result.GetRange(0, MaxFamilies);
	}

	private static void PopulateBranchDetails(XjBloodlineFamilyItem item, IReadOnlyDictionary<long, string> familyNames)
	{
		if (item == null || item.FamilyStableId <= 0L) return;
		item.IsMigrationBranch = XjFamilyMemberLedger.IsMigrationBranchFamily(item.FamilyStableId);
		if (XjFamilyMemberLedger.TryGetBranchSourceFamilyId(item.FamilyStableId, out long sourceFamilyId))
		{
			item.SourceFamilyId = sourceFamilyId;
			item.SourceFamilyName = familyNames != null && familyNames.TryGetValue(sourceFamilyId, out string sourceName)
				? sourceName
				: XjFamilyDisplayNameResolver.Resolve(sourceFamilyId);
		}
		if (XjFamilyMemberLedger.TryGetFamilyOriginCityId(item.FamilyStableId, out long originCityId))
		{
			item.OriginCityId = originCityId;
			item.OriginCityName = ResolveCityName(originCityId);
		}
	}

	private static Dictionary<long, string> BuildGoverningCityNamesByFamily()
	{
		Dictionary<long, string> cityNames = new Dictionary<long, string>();
		IReadOnlyList<City> citySnapshot = XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < citySnapshot.Count; i++)
		{
			City city = citySnapshot[i];
			if (city?.data == null || city.data.id <= 0L) continue;
			cityNames[city.data.id] = ResolveCityName(city);
		}

		Dictionary<long, List<string>> namesByFamily = new Dictionary<long, List<string>>();
		IReadOnlyList<XjCityFamilyGovernanceArchiveRecord> governance = XjSectRepository.ReadAllGovernance();
		for (int i = 0; i < governance.Count; i++)
		{
			XjCityFamilyGovernanceArchiveRecord record = governance[i];
			if (record == null || record.GoverningFamilyId <= 0L) continue;
			if (!namesByFamily.TryGetValue(record.GoverningFamilyId, out List<string> names))
			{
				names = new List<string>();
				namesByFamily.Add(record.GoverningFamilyId, names);
			}
			names.Add(cityNames.TryGetValue(record.CityId, out string cityName)
				? cityName
				: "城镇" + record.CityId.ToString(CultureInfo.InvariantCulture));
		}

		Dictionary<long, string> result = new Dictionary<long, string>();
		foreach (KeyValuePair<long, List<string>> pair in namesByFamily)
		{
			pair.Value.Sort(StringComparer.Ordinal);
			result[pair.Key] = string.Join("、", pair.Value);
		}
		return result;
	}

	private static XjBloodlineFamilyItem GetOrCreate(Dictionary<long, XjBloodlineFamilyItem> items, long familyStableId)
	{
		if (!items.TryGetValue(familyStableId, out XjBloodlineFamilyItem item))
		{
			item = new XjBloodlineFamilyItem { FamilyStableId = familyStableId };
			items[familyStableId] = item;
		}

		return item;
	}

	private static void PopulateFamilyDetails(XjBloodlineFamilyItem item)
	{
		IReadOnlyList<XjFamilyMemberDisplayItem> members = XjFamilyReadModel.Shared.BuildMemberDisplayItems(item.FamilyStableId);
		for (int i = 0; i < members.Count; i++)
		{
			if (item.Members.Count < MaxRowsPerFamily && !string.IsNullOrWhiteSpace(members[i].DisplayText))
			{
				item.Members.Add(members[i].DisplayText);
			}

			if (XjScheduler.ResolveActor(members[i].ActorId, out Actor actor)
				&& actor?.data != null)
			{
				string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
				if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
					&& IsDisplayDaoTu(daoTu))
				{
					item.DaoTuCounts.TryGetValue(daoTu.Trim(), out int daoTuCount);
					item.DaoTuCounts[daoTu.Trim()] = daoTuCount + 1;
				}
				int realmScore = GetRealmScore(realmId);
				bool needsInfo = item.MemberCards.Count < MaxRowsPerFamily || realmScore > item.HighestRealmScore;
				XjActorInfoReadModel info = needsInfo ? XjActorInfoReadModel.BuildForActor(actor) : default;
				if (item.MemberCards.Count < MaxRowsPerFamily && info.Found)
				{
					item.MemberCards.Add(BuildMemberCard(members[i], info));
				}
				if (string.IsNullOrWhiteSpace(item.BloodlineQuality))
				{
					item.BloodlineQuality = BuildBloodlineQualityText(members[i].ActorId);
				}

				if (realmScore > 0)
				{
					item.CultivatorCount++;
					item.RealmMemberScore += GetRealmFamilyPoints(realmScore);
				}
				if (realmScore > item.HighestRealmScore)
				{
					item.HighestRealmScore = realmScore;
					item.HighestRealm = info.Found && !string.IsNullOrWhiteSpace(info.RealmDisplay)
						? info.RealmDisplay
						: members[i].Realm;
				}
			}
			else if (item.MemberCards.Count < MaxRowsPerFamily && !string.IsNullOrWhiteSpace(members[i].DisplayText))
			{
				item.MemberCards.Add(members[i].DisplayText);
			}
		}
		item.DominantDaoTu = ResolveDominantDaoTu(item.DaoTuCounts);

		IReadOnlyList<XjFamilyPendingDisplayItem> pending = XjFamilyReadModel.Shared.BuildPendingDisplayItems(item.FamilyStableId, 0L);
		item.PendingCount = pending.Count;
		for (int i = 0; i < pending.Count && item.Pending.Count < MaxRowsPerFamily; i++)
		{
			if (!string.IsNullOrWhiteSpace(pending[i].DisplayText))
			{
				item.Pending.Add(pending[i].DisplayText);
			}
		}

		IReadOnlyList<XjFamilyMarriageDisplayItem> marriages = XjFamilyReadModel.Shared.BuildMarriageDisplayItems(item.FamilyStableId, 0L);
		for (int i = 0; i < marriages.Count && item.Marriages.Count < MaxRowsPerFamily; i++)
		{
			if (!string.IsNullOrWhiteSpace(marriages[i].DisplayText))
			{
				item.Marriages.Add(marriages[i].DisplayText);
			}
		}

		string familyKey = "actor:" + item.FamilyStableId.ToString(CultureInfo.InvariantCulture);
		item.GongFaCount = XjFamilyWarehouseReadModel.Shared.ReadFamilyGongFaEntries(item.FamilyStableId, XjFamilyGongFaWarehouse.SourceTypeGongFa).Count;
		item.QiuJinFaCount = XjFamilyWarehouseReadModel.Shared.ReadFamilyGongFaEntries(item.FamilyStableId, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa).Count;
		item.CaiQiCount = XjFamilyWarehouseReadModel.Shared.ReadFamilyCaiQiResources(familyKey).Count;
		item.CaiQiFaCount = XjFamilyWarehouseReadModel.Shared.ReadFamilyCaiQiFaResources(familyKey).Count;
		item.FaBaoCount = XjFamilyWarehouseReadModel.Shared.ReadFamilyFaBaoEntries(item.FamilyStableId).Count;
		item.FamilyScore = item.GongFaCount * 10
			+ item.QiuJinFaCount * 25
			+ item.FaBaoCount * 30
			+ item.RealmMemberScore;

		string[] chronicle = XjFamilyChronicleDisplayFormatter.BuildItemsForFamily(item.FamilyStableId);
		for (int i = 0; i < chronicle.Length && item.Chronicle.Count < MaxRowsPerFamily; i++)
		{
			if (!string.IsNullOrWhiteSpace(chronicle[i]) && chronicle[i] != XjFamilyChronicleDisplayFormatter.EmptyDisplayText)
			{
				item.Chronicle.Add(chronicle[i]);
				item.RecentChronicleYear = Math.Max(item.RecentChronicleYear, ParseLeadingYear(chronicle[i]));
			}
		}
	}

	private static string BuildMemberCard(in XjFamilyMemberDisplayItem member, in XjActorInfoReadModel info)
	{
		string realm = string.IsNullOrWhiteSpace(info.RealmDisplay) ? member.Realm : info.RealmDisplay;
		string daoTu = string.IsNullOrWhiteSpace(info.DaoTu) ? "未定道途" : info.DaoTu;
		string aptitude = info.XjZz > 0 ? "资质" + info.XjZz.ToString(CultureInfo.InvariantCulture) : "资质未定";
		string gongFa = string.IsNullOrWhiteSpace(info.GongFaSummary) ? "暂无功法" : info.GongFaSummary;
		string jinDan = string.IsNullOrWhiteSpace(info.JinDanSummary) ? "暂无金丹" : info.JinDanSummary;
		return member.Name + "-第" + member.Generation.ToString(CultureInfo.InvariantCulture) + "代-"
			+ realm + "-" + daoTu + "-" + aptitude + "-" + gongFa + "-" + jinDan;
	}

	private static string BuildBloodlineQualityText(long actorId)
	{
		if (!XjFamilyReadModel.Shared.TryGetBloodlineDetails(actorId, out XjBloodlineDisplayState state))
		{
			return string.Empty;
		}

		string generation = state.Generation > 0 ? " - " + state.Generation.ToString(CultureInfo.InvariantCulture) + "代" : string.Empty;
		string origin = string.IsNullOrWhiteSpace(state.OriginDaoTu) ? string.Empty : " - 源道途：" + state.OriginDaoTu.Trim();
		string source = string.IsNullOrWhiteSpace(state.Source) ? string.Empty : " - 来源：" + FormatBloodlineSource(state.Source);
		string talent = state.ExtraTalentInheritance > 0
			? " - 天赋继承+" + state.ExtraTalentInheritance.ToString(CultureInfo.InvariantCulture)
			: string.Empty;
		return "血脉：" + state.Quality + " - " + state.Concentration.ToString(CultureInfo.InvariantCulture)
			+ "%" + generation + origin + source + talent;
	}

	private static string FormatBloodlineSource(string source)
	{
		return source switch
		{
			"Founder" => "始祖",
			"FatherConfirmed" => "父系确认",
			"Atavism" => "返祖",
			"HighRealmOverride" => "境界覆盖",
			"UnknownFather" => "父系不可读",
			_ => source
		};
	}

	private static int ParseLeadingYear(string value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return 0;
		}

		int result = 0;
		for (int i = 0; i < value.Length; i++)
		{
			if (!char.IsDigit(value[i]))
			{
				break;
			}

			result = result * 10 + (value[i] - '0');
		}

		return result;
	}

	private static int GetRealmScore(string realmId)
	{
		return realmId switch
		{
			XjRealmIds.JinDan => 5,
			XjRealmIds.ZiFu => 4,
			XjRealmIds.ZhuJi => 3,
			XjRealmIds.LianQi => 2,
			XjRealmIds.TaiXi => 1,
			_ => 0
		};
	}

	private static bool IsDisplayDaoTu(string daoTu)
	{
		string value = (daoTu ?? string.Empty).Trim();
		return !string.IsNullOrWhiteSpace(value)
			&& !string.Equals(value, "基础", StringComparison.Ordinal)
			&& !string.Equals(value, "玄门", StringComparison.Ordinal)
			&& !string.Equals(value, "无道途", StringComparison.Ordinal);
	}

	private static string ResolveDominantDaoTu(IReadOnlyDictionary<string, int> counts)
	{
		string selected = string.Empty;
		int selectedCount = 0;
		foreach (KeyValuePair<string, int> pair in counts)
		{
			if (pair.Value > selectedCount
				|| (pair.Value == selectedCount && string.CompareOrdinal(pair.Key, selected) < 0))
			{
				selected = pair.Key;
				selectedCount = pair.Value;
			}
		}
		return selected;
	}

	private static int GetRealmFamilyPoints(int realmScore)
	{
		return realmScore switch
		{
			5 => 40,
			4 => 15,
			3 => 5,
			2 => 2,
			1 => 1,
			_ => 0
		};
	}

	private static string ResolveCityName(Actor actor)
	{
		try
		{
			string name = actor?.city?.data == null ? string.Empty : ((BaseSystemData)actor.city.data).name;
			return string.IsNullOrWhiteSpace(name) ? "流散" : name.Trim();
		}
		catch
		{
			return "流散";
		}
	}

	private static string ResolveCityName(City city)
	{
		try
		{
			string name = city?.data == null ? string.Empty : ((BaseSystemData)city.data).name;
			return string.IsNullOrWhiteSpace(name) ? "流散" : name.Trim();
		}
		catch
		{
			return "流散";
		}
	}

	private static string ResolveCityName(long cityId)
	{
		if (cityId <= 0L) return string.Empty;
		return XjWorldLookupIndex.TryResolveCity(cityId, out City city)
			? ResolveCityName(city)
			: "城镇" + cityId.ToString(CultureInfo.InvariantCulture);
	}

	private static string ResolveClanName(Actor actor)
	{
		try
		{
			string name = actor?.clan?.data == null ? string.Empty : ((BaseSystemData)actor.clan.data).name;
			return string.IsNullOrWhiteSpace(name) ? string.Empty : name.Trim();
		}
		catch
		{
			return string.Empty;
		}
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名成员" : name;
		}
		catch
		{
			return "未名成员";
		}
	}
}

internal static class XjBloodlineFormatter
{
	internal static string Format(IReadOnlyList<XjBloodlineFamilyItem> items)
	{
		if (items == null || items.Count == 0)
		{
			return "暂无家族传承记录。";
		}

		StringBuilder builder = new StringBuilder(1024);
		builder.AppendLine("<color=#E7D8A7>血脉-家族传承</color>");
		AppendSortSummary(builder, items);
		for (int i = 0; i < items.Count; i++)
		{
			XjBloodlineFamilyItem item = items[i];
			builder.Append((i + 1).ToString(CultureInfo.InvariantCulture));
			builder.Append(". ");
			builder.Append(string.IsNullOrWhiteSpace(item.ClanName) ? XjFamilyDisplayNameResolver.Resolve(item.FamilyStableId) : item.ClanName);
			builder.Append(" - 已确认 ");
			builder.Append(item.ConfirmedCount.ToString(CultureInfo.InvariantCulture));
			builder.Append(" - 待确认 ");
			builder.Append(item.PendingCount.ToString(CultureInfo.InvariantCulture));
			if (!string.IsNullOrWhiteSpace(item.SampleName))
			{
				builder.Append(" - 代表：");
				builder.Append(item.SampleName);
			}
			if (!string.IsNullOrWhiteSpace(item.HighestRealm))
			{
				builder.Append(" - 最高境界：");
				builder.Append(item.HighestRealm);
			}
			if (!string.IsNullOrWhiteSpace(item.BloodlineQuality))
			{
				builder.Append(" - ");
				builder.Append(item.BloodlineQuality);
			}
			if (item.RecentChronicleYear > 0)
			{
				builder.Append(" - 最近纪事：");
				builder.Append(item.RecentChronicleYear.ToString(CultureInfo.InvariantCulture));
				builder.Append("年");
			}
			builder.AppendLine();

			AppendRows(builder, "主要成员卡片", item.MemberCards);
			AppendRows(builder, "主要成员", item.Members);
			AppendRows(builder, "父系未确认", item.Pending);
			AppendRows(builder, "姻亲关联", item.Marriages);
			AppendRows(builder, "家族纪事", item.Chronicle);
			builder.Append("   仓库摘要：功法 ");
			builder.Append(item.GongFaCount.ToString(CultureInfo.InvariantCulture));
			builder.Append(" - 求金法 ");
			builder.Append(item.QiuJinFaCount.ToString(CultureInfo.InvariantCulture));
			builder.Append(" - 纳气 ");
			builder.Append(item.CaiQiCount.ToString(CultureInfo.InvariantCulture));
			builder.Append(" - 采气法 ");
			builder.Append(item.CaiQiFaCount.ToString(CultureInfo.InvariantCulture));
			builder.Append(" - 法宝 ");
			builder.AppendLine(item.FaBaoCount.ToString(CultureInfo.InvariantCulture));
			builder.AppendLine();
		}

		return builder.ToString().TrimEnd();
	}

	private static void AppendSortSummary(StringBuilder builder, IReadOnlyList<XjBloodlineFamilyItem> items)
	{
		List<XjBloodlineFamilyItem> byRealm = new List<XjBloodlineFamilyItem>(items);
		byRealm.Sort((left, right) =>
		{
			int realm = right.HighestRealmScore.CompareTo(left.HighestRealmScore);
			if (realm != 0)
			{
				return realm;
			}

			int confirmed = right.ConfirmedCount.CompareTo(left.ConfirmedCount);
			return confirmed != 0 ? confirmed : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		List<XjBloodlineFamilyItem> byChronicle = new List<XjBloodlineFamilyItem>(items);
		byChronicle.Sort((left, right) =>
		{
			int year = right.RecentChronicleYear.CompareTo(left.RecentChronicleYear);
			return year != 0 ? year : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
		List<XjBloodlineFamilyItem> byPending = new List<XjBloodlineFamilyItem>(items);
		byPending.Sort((left, right) =>
		{
			int pending = right.PendingCount.CompareTo(left.PendingCount);
			return pending != 0 ? pending : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});

		AppendFamilyIndex(builder, "最高境界索引", byRealm, item => string.IsNullOrWhiteSpace(item.HighestRealm) ? "暂无境界" : item.HighestRealm);
		AppendFamilyIndex(builder, "最近纪事索引", byChronicle, item => item.RecentChronicleYear > 0 ? item.RecentChronicleYear.ToString(CultureInfo.InvariantCulture) + "年" : "暂无纪事");
		AppendFamilyIndex(builder, "父系待确认索引", byPending, item => item.PendingCount.ToString(CultureInfo.InvariantCulture) + "条");
		builder.AppendLine();
	}

	private static void AppendFamilyIndex(StringBuilder builder, string title, List<XjBloodlineFamilyItem> items, Func<XjBloodlineFamilyItem, string> label)
	{
		builder.Append("【");
		builder.Append(title);
		builder.Append("】");
		int count = Math.Min(5, items.Count);
		for (int i = 0; i < count; i++)
		{
			XjBloodlineFamilyItem item = items[i];
			if (i > 0)
			{
				builder.Append("；");
			}

			builder.Append("家族");
			builder.Append(item.FamilyStableId.ToString(CultureInfo.InvariantCulture));
			builder.Append(" ");
			builder.Append(label(item));
		}
		builder.AppendLine();
	}

	private static void AppendRows(StringBuilder builder, string title, List<string> rows)
	{
		if (rows == null || rows.Count == 0)
		{
			return;
		}

		builder.Append("   ");
		builder.Append(title);
		builder.AppendLine("：");
		for (int i = 0; i < rows.Count; i++)
		{
			builder.Append("     - ");
			builder.AppendLine(rows[i]);
		}
	}
}

internal static class XjBloodlineWindow
{
	private const string WindowId = "XuanJianBloodlineWindow";
	private static ScrollWindow window;
	private static Transform cardRoot;
	private static Text emptyText;
	private static Text summaryText;

	internal static void Show()
	{
		bool createdNow = window == null;
		EnsureWindow();
		if (window == null)
		{
			return;
		}

		Refresh();
		XjWindowOpenGuard.Show(window, WindowId, createdNow);
	}

	private static void EnsureWindow()
	{
		if (window != null)
		{
			return;
		}

		window = WindowCreator.CreateEmptyWindow(WindowId, "xuanjian.bloodline.overview", "ui/Icons/items/XueMaiBang");
		Transform background = window == null ? null : ((Component)window).transform.Find("Background");
		if (background == null)
		{
			return;
		}

		RectTransform backgroundRect = background.GetComponent<RectTransform>();
		if (backgroundRect != null)
		{
			backgroundRect.sizeDelta = new Vector2(250f, 320f);
		}

		CreateCardScroll(background);
		emptyText = CreateText(background, "EmptyText", "暂无血脉记录", 14, TextAnchor.MiddleCenter, new Color(0.65f, 0.65f, 0.65f));
		SetRect(emptyText.gameObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(200f, 44f));
		summaryText = CreateText(background, "SummaryText", string.Empty, 9, TextAnchor.MiddleLeft, new Color(0.78f, 0.82f, 0.86f));
		SetRect(summaryText.gameObject, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(-30f, 18f));
	}

	private static void CreateCardScroll(Transform parent)
	{
		GameObject scrollObject = new GameObject("BloodlineCardScroll", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect));
		scrollObject.transform.SetParent(parent, false);
		SetRect(scrollObject, Vector2.zero, Vector2.one, offsetMin: new Vector2(10f, 28f), offsetMax: new Vector2(-10f, -34f));
		Image background = scrollObject.GetComponent<Image>();
		background.color = new Color(0f, 0f, 0f, 0.20f);
		background.raycastTarget = true;
		scrollObject.GetComponent<Mask>().showMaskGraphic = true;

		GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
		viewport.transform.SetParent(scrollObject.transform, false);
		RectTransform viewportRect = SetRect(viewport, Vector2.zero, Vector2.one, offsetMin: Vector2.zero, offsetMax: Vector2.zero);

		GameObject content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
		content.transform.SetParent(viewport.transform, false);
		RectTransform contentRect = SetRect(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(-10f, 0f));
		VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>();
		layout.padding = new RectOffset(6, 6, 6, 8);
		layout.spacing = 6f;
		layout.childAlignment = TextAnchor.UpperCenter;
		layout.childControlWidth = true;
		layout.childControlHeight = true;
		layout.childForceExpandWidth = true;
		layout.childForceExpandHeight = false;
		content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
		cardRoot = content.transform;

		ScrollRect scroll = scrollObject.GetComponent<ScrollRect>();
		scroll.horizontal = false;
		scroll.vertical = true;
		scroll.movementType = ScrollRect.MovementType.Clamped;
		scroll.scrollSensitivity = 28f;
		scroll.viewport = viewportRect;
		scroll.content = contentRect;
	}

	private static void Refresh()
	{
		if (cardRoot == null)
		{
			return;
		}

		for (int i = cardRoot.childCount - 1; i >= 0; i--)
		{
			UnityEngine.Object.Destroy(cardRoot.GetChild(i).gameObject);
		}

		List<XjBloodlineFamilyItem> families = new List<XjBloodlineFamilyItem>(XjBloodlineReadModel.Build());
		SortFamilies(families);
		emptyText?.gameObject.SetActive(families.Count == 0);
		if (summaryText != null)
		{
			summaryText.text = "家族：" + families.Count.ToString(CultureInfo.InvariantCulture) + " · 按战力排列";
		}
		for (int i = 0; i < families.Count; i++)
		{
			CreateFamilyCard(families[i]);
		}
	}

	private static void SortFamilies(List<XjBloodlineFamilyItem> families)
	{
		families.Sort((left, right) =>
		{
			int score = right.FamilyScore.CompareTo(left.FamilyScore);
			if (score != 0) return score;
			int realm = right.HighestRealmScore.CompareTo(left.HighestRealmScore);
			if (realm != 0) return realm;
			int confirmed = right.ConfirmedCount.CompareTo(left.ConfirmedCount);
			return confirmed != 0 ? confirmed : left.FamilyStableId.CompareTo(right.FamilyStableId);
		});
	}

	private static void CreateFamilyCard(XjBloodlineFamilyItem item)
	{
		GameObject card = new GameObject("BloodlineFamilyCard", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
		card.transform.SetParent(cardRoot, false);
		LayoutElement layout = card.GetComponent<LayoutElement>();
		layout.minHeight = 72f;
		layout.preferredHeight = 72f;
		Image image = card.GetComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/special/backgroundKingdomElement");
		image.type = image.sprite == null ? Image.Type.Simple : Image.Type.Sliced;
		image.color = Color.white;

		string name = FormatClanName(item.ClanName, item.SampleName);
		// 标题行: 氏族名 + 所属城镇
	string titleText = name;
	if (!string.IsNullOrWhiteSpace(item.CityName) && item.CityName != "流散")
		titleText += "  [" + item.CityName + "]";
	if (item.IsMigrationBranch)
		titleText += "  [迁城分家]";
	Text title = CreateText(card.transform, "Title", titleText, 9, TextAnchor.MiddleLeft, new Color(1f, 0.84f, 0f));
	SetRect(title.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(8f, -12f), new Vector2(-20f, 18f));

	// 道途 + 境界 (去掉·，加宽间距)
	Text line1 = CreateText(card.transform, "Line1",
		"道途：" + (string.IsNullOrWhiteSpace(item.DominantDaoTu) ? "未定" : item.DominantDaoTu)
		+ "    最高境界：" + (string.IsNullOrWhiteSpace(item.HighestRealm) ? "未入道" : item.HighestRealm),
		7, TextAnchor.MiddleLeft, Color.white);
	SetRect(line1.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(8f, -34f), new Vector2(-20f, 18f));

	// 战力行
	Text line2 = CreateText(card.transform, "Line2",
		"战力：" + item.FamilyScore.ToString(CultureInfo.InvariantCulture),
		7, TextAnchor.MiddleLeft, new Color(0.75f, 0.84f, 0.92f));
	SetRect(line2.gameObject, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f), new Vector2(8f, -56f), new Vector2(-20f, 18f));

		TipButton tip = card.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, name, BuildFamilyTooltip(item), string.Empty);
	}

	private static string FormatClanName(string clanName, string sampleName)
	{
		string value = string.IsNullOrWhiteSpace(clanName) ? sampleName : clanName;
		value = (value ?? string.Empty).Trim();
		if (value.EndsWith("氏族", StringComparison.Ordinal))
		{
			value = value.Substring(0, value.Length - 1);
		}
		if (value.EndsWith("氏", StringComparison.Ordinal))
		{
			return value;
		}
		return string.IsNullOrWhiteSpace(value) ? "未名氏" : value.Substring(0, 1) + "氏";
	}

	private static string BuildFamilyTooltip(XjBloodlineFamilyItem item)
	{
		return "道途：" + (string.IsNullOrWhiteSpace(item.DominantDaoTu) ? "未定" : item.DominantDaoTu)
			+ "\n谱系：" + BuildBranchTooltipLine(item)
			+ "\n最高境界：" + (string.IsNullOrWhiteSpace(item.HighestRealm) ? "未入道" : item.HighestRealm)
			+ "\n战力：" + item.FamilyScore.ToString(CultureInfo.InvariantCulture)
			+ "\n修士：" + item.CultivatorCount.ToString(CultureInfo.InvariantCulture)
			+ "\n功法：" + item.GongFaCount.ToString(CultureInfo.InvariantCulture)
			+ " 求金法：" + item.QiuJinFaCount.ToString(CultureInfo.InvariantCulture)
			+ " 法宝：" + item.FaBaoCount.ToString(CultureInfo.InvariantCulture);
	}

	private static string BuildBranchTooltipLine(XjBloodlineFamilyItem item)
	{
		if (item == null) return "本家";
		string text = item.IsMigrationBranch ? "迁城分家" : "本家";
		if (item.SourceFamilyId > 0L)
		{
			text += "\n来源主家：" + (string.IsNullOrWhiteSpace(item.SourceFamilyName)
				? XjFamilyDisplayNameResolver.Resolve(item.SourceFamilyId)
				: item.SourceFamilyName);
		}
		if (item.OriginCityId > 0L)
		{
			text += "\n发源城：" + (string.IsNullOrWhiteSpace(item.OriginCityName)
				? "城镇" + item.OriginCityId.ToString(CultureInfo.InvariantCulture)
				: item.OriginCityName);
		}
		text += "\n治理城镇：" + (string.IsNullOrWhiteSpace(item.CityName) ? "暂无" : item.CityName);
		return text;
	}

	private static Text CreateText(Transform parent, string name, string value, int fontSize, TextAnchor alignment, Color color)
	{
		GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
		textObject.transform.SetParent(parent, false);
		Text text = textObject.GetComponent<Text>();
		text.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		text.fontSize = fontSize;
		text.alignment = alignment;
		text.color = color;
		text.horizontalOverflow = HorizontalWrapMode.Wrap;
		text.verticalOverflow = VerticalWrapMode.Truncate;
		text.raycastTarget = false;
		text.text = value ?? string.Empty;
		return text;
	}

	private static RectTransform SetRect(
		GameObject obj,
		Vector2? anchorMin = null,
		Vector2? anchorMax = null,
		Vector2? pivot = null,
		Vector2? pos = null,
		Vector2? size = null,
		Vector2? offsetMin = null,
		Vector2? offsetMax = null)
	{
		RectTransform rect = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
		if (anchorMin.HasValue) rect.anchorMin = anchorMin.Value;
		if (anchorMax.HasValue) rect.anchorMax = anchorMax.Value;
		if (pivot.HasValue) rect.pivot = pivot.Value;
		if (pos.HasValue) rect.anchoredPosition = pos.Value;
		if (size.HasValue) rect.sizeDelta = size.Value;
		if (offsetMin.HasValue) rect.offsetMin = offsetMin.Value;
		if (offsetMax.HasValue) rect.offsetMax = offsetMax.Value;
		return rect;
	}
}
