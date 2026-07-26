namespace XuanJianVNext.UI.Family;

using System.Collections.Generic;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

internal readonly struct XjFamilyInheritanceSection
{
	internal readonly string Title;
	internal readonly string[] Items;
	internal readonly XjFamilyMemberDisplayItem[] MemberItems;
	internal readonly XjFamilyCaiQiWarehouseUIItem[] CaiQiItems;
	internal readonly XjFamilyGongFaWarehouseUIItem[] GongFaItems;
	internal readonly XjFamilyFaBaoDisplayItem[] FaBaoItems;
	internal readonly XjAlchemyUiItem[] AlchemyItems;
	internal readonly XjCraftInventoryUiItem[] CraftItems;
	internal readonly XjLingWuUiItem[] LingWuItems;

	internal XjFamilyInheritanceSection(string title, string[] items)
	{
		Title = title ?? string.Empty;
		Items = items ?? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText };
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
	}

	internal XjFamilyInheritanceSection(string title, XjFamilyMemberDisplayItem[] memberItems)
	{
		Title = title ?? string.Empty;
		MemberItems = memberItems ?? System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = MemberItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjFamilyCaiQiWarehouseUIItem[] caiQiItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = caiQiItems ?? System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = CaiQiItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjFamilyGongFaWarehouseUIItem[] gongFaItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		GongFaItems = gongFaItems ?? System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = GongFaItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjFamilyFaBaoDisplayItem[] faBaoItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		FaBaoItems = faBaoItems ?? System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = FaBaoItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjAlchemyUiItem[] alchemyItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = alchemyItems ?? System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = AlchemyItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjCraftInventoryUiItem[] craftItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = craftItems ?? System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = System.Array.Empty<XjLingWuUiItem>();
		Items = CraftItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}

	internal XjFamilyInheritanceSection(string title, XjLingWuUiItem[] lingWuItems)
	{
		Title = title ?? string.Empty;
		MemberItems = System.Array.Empty<XjFamilyMemberDisplayItem>();
		CaiQiItems = System.Array.Empty<XjFamilyCaiQiWarehouseUIItem>();
		GongFaItems = System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		FaBaoItems = System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		AlchemyItems = System.Array.Empty<XjAlchemyUiItem>();
		CraftItems = System.Array.Empty<XjCraftInventoryUiItem>();
		LingWuItems = lingWuItems ?? System.Array.Empty<XjLingWuUiItem>();
		Items = LingWuItems.Length == 0
			? new[] { XjFamilyInheritanceTabFormatter.EmptyDisplayText }
			: System.Array.Empty<string>();
	}
}

internal static class XjFamilyInheritanceTabFormatter
{
	private const int ColumnsPerRow = 3;
	internal const string EmptyDisplayText = "暂无";

	// 0.5.4 家族传承按高境至低境展示。
	private static readonly string[] RealmOrder = { "XjRealm5", "XjRealm4", "XjRealm3", "XjRealm2", "XjRealm1" };
	private static readonly string[] RealmDisplayNames = { "金丹", "紫府", "筑基", "炼气", "胎息" };

	internal static string GetTabTitle()
	{
		return XjFamilyNames.FamilyInheritanceTabName;
	}

	internal static string GetCaiQiSectionTitle()
	{
		return XjFamilyNames.FamilyCaiQiWarehouseName + "仓库";
	}

	internal static string FormatForActor(Actor actor)
	{
		return FormatSections(BuildSectionsForActor(actor));
	}

	internal static XjFamilyInheritanceSection[] BuildSectionsForActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return CreateEmptySections();
		}

		if (!TryResolveFamily(actor, out string familyKey, out long familyId))
		{
			return BuildFocusedOnlySections(actor);
		}

		return BuildFullInheritanceSections(familyKey, familyId, actor);
	}

	private static XjFamilyInheritanceSection[] CreateEmptySections()
	{
		return new[]
		{
			new XjFamilyInheritanceSection("金丹境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("紫府境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("筑基境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("炼气境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("胎息境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("家族纳气仓库", new[] { "暂无先天之气" }),
			new XjFamilyInheritanceSection("家族功法仓库", new[] { "暂无功法" }),
			new XjFamilyInheritanceSection("家族求金法仓库", new[] { "暂无求金法" }),
			new XjFamilyInheritanceSection("家族采气法仓库", new[] { "暂无采气法" }),
			new XjFamilyInheritanceSection("家族器物仓库", new[] { "暂无器物" }),
			new XjFamilyInheritanceSection("家族重宝仓库", new[] { "暂无重宝" }),
			new XjFamilyInheritanceSection("家族药材仓库", new[] { "暂无药材" }),
			new XjFamilyInheritanceSection("家族丹药仓库", new[] { "暂无丹药" }),
			new XjFamilyInheritanceSection("家族丹方库", new[] { "暂无丹方" }),
			new XjFamilyInheritanceSection("家族符箓仓库", new[] { "暂无符箓或符材" }),
			new XjFamilyInheritanceSection("家族阵法材料", new[] { "暂无阵材" }),
		};
	}

	private static XjFamilyInheritanceSection[] BuildFullInheritanceSections(string familyKey, long familyId, Actor focusedActor)
	{
		List<XjFamilyInheritanceSection> sections = new List<XjFamilyInheritanceSection>(17);
		sections.AddRange(BuildRealmGroupedMemberItems(familyId, focusedActor));
		sections.Add(new XjFamilyInheritanceSection("家族纳气仓库", XjFamilyCaiQiWarehouseUI.BuildItems(familyKey)));
		sections.Add(new XjFamilyInheritanceSection("家族功法仓库", XjFamilyGongFaWarehouseUI.BuildItems(familyId, XjFamilyGongFaWarehouse.SourceTypeGongFa)));
		sections.Add(new XjFamilyInheritanceSection("家族求金法仓库", XjFamilyGongFaWarehouseUI.BuildItems(familyId, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa)));
		sections.Add(new XjFamilyInheritanceSection("家族采气法仓库", BuildCaiQiFaWarehouseItems(familyKey)));
		sections.Add(new XjFamilyInheritanceSection("家族器物仓库", BuildFaBaoWarehouseItems(familyId)));
		sections.Add(new XjFamilyInheritanceSection("家族重宝仓库", XjLingWuUiModel.BuildFamilyItems(familyId)));
		XjAlchemyOwnerKey alchemyOwner = new XjAlchemyOwnerKey(XjAlchemyOwnerScope.Family, familyId);
		int currentYear = World.world?.map_stats?.year ?? 0;
		XjAlchemyInventorySnapshot alchemySnapshot = XjAlchemyUiModel.ReadSnapshot(alchemyOwner, currentYear);
		sections.Add(new XjFamilyInheritanceSection("家族药材仓库", XjAlchemyUiModel.BuildMaterials(alchemySnapshot)));
		sections.Add(new XjFamilyInheritanceSection("家族丹药仓库", XjAlchemyUiModel.BuildPills(alchemySnapshot)));
		sections.Add(new XjFamilyInheritanceSection("家族丹方库", XjAlchemyUiModel.BuildRecipes(alchemySnapshot)));
		XjCraftOwnerKey craftOwner = new XjCraftOwnerKey(XjCraftOwnerScope.Family, familyId);
		sections.Add(new XjFamilyInheritanceSection("家族符箓仓库", XjCraftInventoryUiModel.BuildTalismanInventory(craftOwner)));
		sections.Add(new XjFamilyInheritanceSection("家族阵法材料", XjCraftInventoryUiModel.BuildFormationMaterials(craftOwner)));
		return sections.ToArray();
	}

	private static XjFamilyInheritanceSection[] BuildFocusedOnlySections(Actor focusedActor)
	{
		List<XjFamilyInheritanceSection> sections = new List<XjFamilyInheritanceSection>(17);
		XjFamilyInheritanceSection[] realmSections = BuildRealmGroupedMemberItems(0L, focusedActor);
		sections.AddRange(realmSections);
		sections.Add(new XjFamilyInheritanceSection("家族纳气仓库", new[] { "暂无先天之气" }));
		sections.Add(new XjFamilyInheritanceSection("家族功法仓库", new[] { "暂无功法" }));
		sections.Add(new XjFamilyInheritanceSection("家族求金法仓库", new[] { "暂无求金法" }));
		sections.Add(new XjFamilyInheritanceSection("家族采气法仓库", new[] { "暂无采气法" }));
		sections.Add(new XjFamilyInheritanceSection("家族器物仓库", new[] { "暂无器物" }));
		sections.Add(new XjFamilyInheritanceSection("家族重宝仓库", new[] { "暂无重宝" }));
		sections.Add(new XjFamilyInheritanceSection("家族药材仓库", new[] { "暂无药材" }));
		sections.Add(new XjFamilyInheritanceSection("家族丹药仓库", new[] { "暂无丹药" }));
		sections.Add(new XjFamilyInheritanceSection("家族丹方库", new[] { "暂无丹方" }));
		sections.Add(new XjFamilyInheritanceSection("家族符箓仓库", new[] { "暂无符箓或符材" }));
		sections.Add(new XjFamilyInheritanceSection("家族阵法材料", new[] { "暂无阵材" }));
		return sections.ToArray();
	}

	private static XjFamilyInheritanceSection[] BuildRealmGroupedMemberItems(long familyId, Actor focusedActor)
	{
		IReadOnlyList<XjFamilyMemberDisplayItem> indexedItems = familyId > 0L
			? XjFamilyReadModel.Shared.BuildMemberDisplayItems(familyId)
			: System.Array.Empty<XjFamilyMemberDisplayItem>();
		List<XjFamilyMemberDisplayItem> items = new List<XjFamilyMemberDisplayItem>(indexedItems.Count + 1);
		for (int i = 0; i < indexedItems.Count; i++)
		{
			items.Add(indexedItems[i]);
		}

		EnsureFocusedActorMember(items, familyId, focusedActor);

		// 按境界分组
		Dictionary<string, List<XjFamilyMemberDisplayItem>> realmGroups = new Dictionary<string, List<XjFamilyMemberDisplayItem>>();
		for (int r = 0; r < RealmOrder.Length; r++)
		{
			realmGroups[RealmOrder[r]] = new List<XjFamilyMemberDisplayItem>();
		}
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyMemberDisplayItem item = items[i];
			if (!item.Found || item.ActorId <= 0L)
			{
				continue;
			}

			bool hasActor = XjFamilyReadModel.Shared.TryGetActor(item.ActorId, out Actor resolvedActor)
				&& resolvedActor?.data != null;
			if (hasActor && !resolvedActor.isAlive())
			{
				continue;
			}

			string actorRealm = !string.IsNullOrWhiteSpace(item.Realm)
				? item.Realm
				: hasActor ? ResolveFocusedActorRealm(resolvedActor) : string.Empty;
			if (string.IsNullOrWhiteSpace(actorRealm))
			{
				continue;
			}

			string realmGroup = NormalizeRealmGroup(actorRealm);
			if (realmGroups.TryGetValue(realmGroup, out List<XjFamilyMemberDisplayItem> members))
			{
				string name = !string.IsNullOrWhiteSpace(item.Name)
					? item.Name
					: hasActor ? resolvedActor.getName() : "未名族人";
				string displayText = string.IsNullOrWhiteSpace(item.DisplayText)
					? (string.IsNullOrWhiteSpace(name) ? "未名族人" : name.Trim()) + " - " + ResolveRealmDisplayNameForInheritance(actorRealm)
					: item.DisplayText;
				members.Add(new XjFamilyMemberDisplayItem(
					true,
					item.ActorId,
					name,
					item.Generation,
					actorRealm,
					item.Status,
					item.RelationText,
					displayText));
			}
		}

		// 构建分组列表
		List<XjFamilyInheritanceSection> sections = new List<XjFamilyInheritanceSection>();
		for (int r = 0; r < RealmOrder.Length; r++)
		{
			List<XjFamilyMemberDisplayItem> members = realmGroups[RealmOrder[r]];
			sections.Add(new XjFamilyInheritanceSection(
				RealmDisplayNames[r] + "境界",
				members.ToArray()));
		}

		return sections.Count == 0 ? CreateEmptyRealmSections() : sections.ToArray();
	}

	private static void EnsureFocusedActorMember(List<XjFamilyMemberDisplayItem> items, long familyId, Actor focusedActor)
	{
		if (items == null || focusedActor?.data == null)
		{
			return;
		}

		long actorId = ((BaseSystemData)focusedActor.data).id;
		if (actorId <= 0L)
		{
			return;
		}
		XjFamilyIdentity focusedIdentity = XjFamilyIdentity.Empty;
		bool hasFocusedIdentity = XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out focusedIdentity);

		for (int i = 0; i < items.Count; i++)
		{
			if (items[i].ActorId == actorId)
			{
				return;
			}
		}

		if (familyId > 0L
			&& (!hasFocusedIdentity
				|| !focusedIdentity.Found
				|| focusedIdentity.FamilyStableIdValue != familyId))
		{
			return;
		}

		string realm = ResolveFocusedActorRealm(focusedActor);
		string name = focusedActor.getName();
		int generation = 0;
		string status = XjFamilyNames.FamilyStatusConfirmed;
		if (hasFocusedIdentity)
		{
			generation = focusedIdentity.Generation;
			status = XjFamilyNames.FamilyStatusConfirmed;
		}
		string displayText = (string.IsNullOrWhiteSpace(name) ? "未名族人" : name.Trim())
			+ (generation > 0
				? "（第" + generation.ToString(System.Globalization.CultureInfo.InvariantCulture) + "代"
				: "（当前角色");
		if (!string.IsNullOrWhiteSpace(realm))
		{
			displayText += " - " + ResolveRealmDisplayNameForInheritance(realm);
		}

		displayText += " - " + status + "）";
		items.Add(new XjFamilyMemberDisplayItem(
			true,
			actorId,
			string.IsNullOrWhiteSpace(name) ? "未名族人" : name.Trim(),
			generation,
			realm,
			status,
			"父系血脉",
			displayText));
	}

	private static string ResolveFocusedActorRealm(Actor actor)
	{
		return actor?.data == null
			? string.Empty
			: XuanJianVNext.Data.Rules.XjRealmHelper.GetUnifiedId(
				actor,
				XuanJianVNext.Data.Rules.XjRealmHelper.GetTraitSnapshotForRouter);
	}

	private static string ResolveRealmDisplayNameForInheritance(string realm)
	{
		string value = (realm ?? string.Empty).Trim();
		if (string.Equals(value, "XjRealm1", System.StringComparison.Ordinal)
			|| value.IndexOf("胎息", System.StringComparison.Ordinal) >= 0)
		{
			return "胎息";
		}
		if (string.Equals(value, "XjRealm2", System.StringComparison.Ordinal)
			|| value.IndexOf("炼气", System.StringComparison.Ordinal) >= 0
			|| value.IndexOf("\u7ec3\u6c14", System.StringComparison.Ordinal) >= 0)
		{
			return "炼气";
		}
		if (string.Equals(value, "XjRealm3", System.StringComparison.Ordinal)
			|| value.IndexOf("筑基", System.StringComparison.Ordinal) >= 0)
		{
			return "筑基";
		}
		if (string.Equals(value, "XjRealm4", System.StringComparison.Ordinal)
			|| value.IndexOf("紫府", System.StringComparison.Ordinal) >= 0)
		{
			return "紫府";
		}
		if (string.Equals(value, "XjRealm5", System.StringComparison.Ordinal)
			|| value.IndexOf("金丹", System.StringComparison.Ordinal) >= 0)
		{
			return "金丹";
		}

		return "境界未载";
	}

	private static string NormalizeRealmGroup(string realm)
	{
		string value = (realm ?? string.Empty).Trim();
		if (value.Length == 0)
		{
			return string.Empty;
		}

		if (string.Equals(value, "XjRealm1", System.StringComparison.Ordinal)
			|| string.Equals(value, "TaiXi", System.StringComparison.OrdinalIgnoreCase)
			|| value.IndexOf("胎息", System.StringComparison.Ordinal) >= 0)
		{
			return RealmOrder[4];
		}
		if (string.Equals(value, "XjRealm2", System.StringComparison.Ordinal)
			|| string.Equals(value, "LianQi", System.StringComparison.OrdinalIgnoreCase)
			|| value.IndexOf("炼气", System.StringComparison.Ordinal) >= 0
			|| value.IndexOf("\u7ec3\u6c14", System.StringComparison.Ordinal) >= 0)
		{
			return RealmOrder[3];
		}
		if (string.Equals(value, "XjRealm3", System.StringComparison.Ordinal)
			|| string.Equals(value, "ZhuJi", System.StringComparison.OrdinalIgnoreCase)
			|| value.IndexOf("筑基", System.StringComparison.Ordinal) >= 0)
		{
			return RealmOrder[2];
		}
		if (string.Equals(value, "XjRealm4", System.StringComparison.Ordinal)
			|| string.Equals(value, "ZiFu", System.StringComparison.OrdinalIgnoreCase)
			|| value.IndexOf("紫府", System.StringComparison.Ordinal) >= 0
			|| value.IndexOf("参紫", System.StringComparison.Ordinal) >= 0)
		{
			return RealmOrder[1];
		}
		if (string.Equals(value, "XjRealm5", System.StringComparison.Ordinal)
			|| string.Equals(value, "JinDan", System.StringComparison.OrdinalIgnoreCase)
			|| value.IndexOf("金丹", System.StringComparison.Ordinal) >= 0)
		{
			return RealmOrder[0];
		}

		return string.Empty;
	}

	private static XjFamilyInheritanceSection[] CreateEmptyRealmSections()
	{
		return new[]
		{
			new XjFamilyInheritanceSection("金丹境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("紫府境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("筑基境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("炼气境界", new[] { EmptyDisplayText }),
			new XjFamilyInheritanceSection("胎息境界", new[] { EmptyDisplayText }),
		};
	}

	private static string FormatSections(XjFamilyInheritanceSection[] sections)
	{
		System.Text.StringBuilder builder = new System.Text.StringBuilder(128);
		for (int i = 0; i < sections.Length; i++)
		{
			XjFamilyInheritanceSection section = sections[i];
			AppendSection(builder, section.Title, FormatSectionItems(section));
		}

		return builder.ToString();
	}

	private static string FormatSectionItems(in XjFamilyInheritanceSection section)
	{
		if (section.MemberItems != null && section.MemberItems.Length > 0)
		{
			string[] items = new string[section.MemberItems.Length];
			for (int i = 0; i < section.MemberItems.Length; i++)
			{
				items[i] = string.IsNullOrWhiteSpace(section.MemberItems[i].Name)
					? section.MemberItems[i].DisplayText
					: section.MemberItems[i].Name;
			}
			return FormatGridLikeText(items);
		}

		if (section.GongFaItems != null && section.GongFaItems.Length > 0)
		{
			string[] items = new string[section.GongFaItems.Length];
			for (int i = 0; i < section.GongFaItems.Length; i++)
			{
				XjFamilyGongFaWarehouseUIItem item = section.GongFaItems[i];
				string display = item.Name;
				if (item.Grade > 0
					&& !string.Equals(item.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal))
				{
					display += "（" + FormatGongFaGrade(item.Grade) + "）";
				}

				if (item.Count > 1)
				{
					display += "×" + item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
				}

				items[i] = display;
			}

			return FormatGridLikeText(items);
		}

		if (section.CaiQiItems != null && section.CaiQiItems.Length > 0)
		{
			string[] items = new string[section.CaiQiItems.Length];
			for (int i = 0; i < section.CaiQiItems.Length; i++)
			{
				XjFamilyCaiQiWarehouseUIItem item = section.CaiQiItems[i];
				string displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? "未名先天之气" : item.DisplayName;
				items[i] = displayName + "×" + item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}

			return FormatGridLikeText(items);
		}

		if (section.FaBaoItems != null && section.FaBaoItems.Length > 0)
		{
			string[] items = new string[section.FaBaoItems.Length];
			for (int i = 0; i < section.FaBaoItems.Length; i++)
			{
				items[i] = string.IsNullOrWhiteSpace(section.FaBaoItems[i].FaBaoName)
					? "未名法宝"
					: section.FaBaoItems[i].FaBaoName;
			}
			return FormatGridLikeText(items);
		}

		if (section.CraftItems != null && section.CraftItems.Length > 0)
		{
			string[] items = new string[section.CraftItems.Length];
			for (int i = 0; i < section.CraftItems.Length; i++)
			{
				XjCraftInventoryUiItem item = section.CraftItems[i];
				string displayName = string.IsNullOrWhiteSpace(item.DisplayName) ? "未名物品" : item.DisplayName;
				items[i] = displayName + "×" + item.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}
			return FormatGridLikeText(items);
		}

		return FormatGridLikeText(section.Items);
	}

	private static bool TryResolveFamily(Actor actor, out string familyKey, out long familyId)
	{
		familyKey = string.Empty;
		familyId = 0L;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return false;
		}

		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out familyId) && familyId > 0L)
		{
			familyKey = "actor:" + familyId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			return true;
		}

		return false;
	}

	private static string[] BuildGongFaWarehouseItems(long familyId, string sourceType)
	{
		IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries = XjFamilyWarehouseReadModel.Shared.ReadFamilyGongFaEntries(familyId, sourceType);
		if (entries.Count == 0)
		{
			return EmptyItems();
		}

		Dictionary<string, int> countsByDisplayName = new Dictionary<string, int>();
		for (int i = 0; i < entries.Count; i++)
		{
			XjFamilyGongFaWarehouseEntry entry = entries[i];
			if (!entry.Found || string.IsNullOrWhiteSpace(entry.GongFaName))
			{
				continue;
			}

			string displayName = entry.GongFaName;
			if (entry.Grade > 0
				&& !string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal))
			{
				displayName += "（" + FormatGongFaGrade(entry.Grade) + "）";
			}

			if (!string.IsNullOrWhiteSpace(entry.DaoTu))
			{
				displayName += string.Equals(entry.SourceType, XjFamilyGongFaWarehouse.SourceTypeQiuJinFa, System.StringComparison.Ordinal)
					? "·可用道途：" + entry.DaoTu
					: "·" + entry.DaoTu;
			}

			countsByDisplayName.TryGetValue(displayName, out int count);
			countsByDisplayName[displayName] = count + 1;
		}

		if (countsByDisplayName.Count == 0)
		{
			return EmptyItems();
		}

		List<string> displayNames = new List<string>(countsByDisplayName.Keys);
		displayNames.Sort(System.StringComparer.Ordinal);

		List<string> segments = new List<string>(displayNames.Count);
		for (int i = 0; i < displayNames.Count; i++)
		{
			string displayName = displayNames[i];
			int count = countsByDisplayName[displayName];
			segments.Add(displayName + "×" + count.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		return segments.Count == 0 ? EmptyItems() : segments.ToArray();
	}

	private static string FormatGongFaGrade(int grade)
	{
		return XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade);
	}

	private static string[] BuildFamilyMemberItems(long familyId)
	{
		IReadOnlyList<XjFamilyMemberDisplayItem> items = XjFamilyReadModel.Shared.BuildMemberDisplayItems(familyId);
		if (items.Count == 0)
		{
			return EmptyMemberItems();
		}

		List<string> segments = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyMemberDisplayItem item = items[i];
			if (item.Found && !string.IsNullOrWhiteSpace(item.DisplayText))
			{
				segments.Add(item.DisplayText);
			}
		}

		return segments.Count == 0 ? EmptyMemberItems() : segments.ToArray();
	}

	private static string[] BuildFamilyPendingItems(long familyId, long focusedActorId)
	{
		IReadOnlyList<XjFamilyPendingDisplayItem> items = XjFamilyReadModel.Shared.BuildPendingDisplayItems(familyId, focusedActorId);
		if (items.Count == 0)
		{
			return EmptyPendingItems();
		}

		List<string> segments = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyPendingDisplayItem item = items[i];
			if (item.Found && !string.IsNullOrWhiteSpace(item.DisplayText))
			{
				segments.Add(item.DisplayText);
			}
		}

		return segments.Count == 0 ? EmptyPendingItems() : segments.ToArray();
	}

	private static string[] BuildFamilyMarriageItems(long familyId, long focusedActorId)
	{
		IReadOnlyList<XjFamilyMarriageDisplayItem> items = XjFamilyReadModel.Shared.BuildMarriageDisplayItems(familyId, focusedActorId);
		if (items.Count == 0)
		{
			return EmptyMarriageItems();
		}

		List<string> segments = new List<string>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyMarriageDisplayItem item = items[i];
			if (item.Found && !string.IsNullOrWhiteSpace(item.DisplayText))
			{
				segments.Add(item.DisplayText);
			}
		}

		return segments.Count == 0 ? EmptyMarriageItems() : segments.ToArray();
	}

	private static XjFamilyGongFaWarehouseUIItem[] BuildCaiQiFaWarehouseItems(string familyKey)
	{
		IReadOnlyDictionary<string, int> resources = XjFamilyWarehouseReadModel.Shared.ReadFamilyCaiQiFaResources(familyKey);
		if (resources.Count == 0)
		{
			return System.Array.Empty<XjFamilyGongFaWarehouseUIItem>();
		}

		List<string> resourceIds = new List<string>(resources.Keys);
		resourceIds.Sort(System.StringComparer.Ordinal);

		HashSet<string> seenDaoTu = new HashSet<string>(System.StringComparer.Ordinal);
		List<XjFamilyGongFaWarehouseUIItem> segments = new List<XjFamilyGongFaWarehouseUIItem>(resourceIds.Count);
		for (int i = 0; i < resourceIds.Count; i++)
		{
			string resourceId = resourceIds[i];
			int count = resources[resourceId];
			if (count <= 0)
			{
				continue;
			}

			XjFamilyCaiQiWarehouse.ParseCaiQiFaResourceId(resourceId, out string caiQiFaName, out string daoTu);
			string normalizedDaoTu = string.IsNullOrWhiteSpace(daoTu) ? "未知道途" : daoTu.Trim();
			if (!seenDaoTu.Add(normalizedDaoTu))
			{
				continue;
			}

			string displayName = string.IsNullOrWhiteSpace(caiQiFaName) ? "未名采气法" : caiQiFaName;
			segments.Add(new XjFamilyGongFaWarehouseUIItem(
				displayName,
				0,
				count,
				XjFamilyCaiQiWarehouse.ResourceTypeCaiQiFa,
				normalizedDaoTu));
		}

		return segments.Count == 0 ? System.Array.Empty<XjFamilyGongFaWarehouseUIItem>() : segments.ToArray();
	}

	private static XjFamilyFaBaoDisplayItem[] BuildFaBaoWarehouseItems(long familyId)
	{
		IReadOnlyList<XjFamilyFaBaoDisplayItem> items = XjFaBaoReadModel.BuildFamilyItems(familyId);
		if (items.Count == 0)
		{
			return System.Array.Empty<XjFamilyFaBaoDisplayItem>();
		}

		List<XjFamilyFaBaoDisplayItem> segments = new List<XjFamilyFaBaoDisplayItem>(items.Count);
		for (int i = 0; i < items.Count; i++)
		{
			XjFamilyFaBaoDisplayItem item = items[i];
			if (!item.Found || string.IsNullOrWhiteSpace(item.DisplayText))
			{
				continue;
			}

			segments.Add(item);
		}

		return segments.Count == 0 ? System.Array.Empty<XjFamilyFaBaoDisplayItem>() : segments.ToArray();
	}

	private static void AppendSection(System.Text.StringBuilder builder, string title, string value)
	{
		if (builder.Length > 0)
		{
			builder.AppendLine();
			builder.AppendLine();
		}

		builder.AppendLine(title);
		builder.Append(string.IsNullOrWhiteSpace(value) ? EmptyDisplayText : value);
	}

	private static string FormatGridLikeText(string[] items)
	{
		if (items == null || items.Length == 0)
		{
			return EmptyDisplayText;
		}

		if (items.Length <= ColumnsPerRow)
		{
			return string.Join("、", items);
		}

		System.Text.StringBuilder builder = new System.Text.StringBuilder(items.Length * 8);
		for (int i = 0; i < items.Length; i++)
		{
			if (i > 0)
			{
				builder.Append(i % ColumnsPerRow == 0 ? "\n" : "    ");
			}

			builder.Append(items[i]);
		}

		return builder.ToString();
	}

	private static string[] SplitDisplayItems(string display)
	{
		if (string.IsNullOrWhiteSpace(display))
		{
			return EmptyItems();
		}

		string[] rawItems = display.Split('、');
		List<string> items = new List<string>(rawItems.Length);
		for (int i = 0; i < rawItems.Length; i++)
		{
			string item = rawItems[i];
			if (!string.IsNullOrWhiteSpace(item))
			{
				items.Add(item.Trim());
			}
		}

		return items.Count == 0 ? EmptyItems() : items.ToArray();
	}

	private static string[] EmptyItems()
	{
		return new[] { EmptyDisplayText };
	}

	private static string[] EmptyMemberItems()
	{
		return new[] { XjFamilyNames.FamilyNoMembersText };
	}

	private static string[] EmptyPendingItems()
	{
		return EmptyItems();
	}

	private static string[] EmptyMarriageItems()
	{
		return new[] { XjFamilyNames.FamilyNoMarriageText };
	}

	private static string[] EmptyFaBaoItems()
	{
		return new[] { "暂无器物" };
	}
}
