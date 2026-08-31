using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using XuanJianVNext.Core;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Cultivation;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.Systems.Codex;

internal static partial class XjCodexSnapshotPublisher
{
	private const int MaxDisplayedGongFaPerSect = 32;
	private const int MaxDisplayedQiuJinFaPerSect = 24;
	private const int MaxDisplayedCaiQiFaPerSect = 24;
	private const int MaxDisplayedArtifactsPerSect = 36;
	private const int MaxDisplayedPillBatchesPerSect = 36;
	private const int MaxDisplayedTalismanBatchesPerSect = 36;
	private const int MaxDisplayedFormationMaterialsPerSect = 18;

	private static List<XjCodexSectResourceItem> BuildSectResources(
		IReadOnlyList<XjSectArchiveRecord> records,
		IReadOnlyDictionary<long, string> sectNames,
		IReadOnlyList<XjSectFamilySeatArchiveRecord> seats,
		IReadOnlyList<XjTalismanBatchArchiveRecord> talismanBatches)
	{
		List<XjCodexSectResourceItem> result = new List<XjCodexSectResourceItem>();
		Dictionary<long, List<XjTalismanBatchArchiveRecord>> talismanBySect = new Dictionary<long, List<XjTalismanBatchArchiveRecord>>();
		for (int t = 0; t < talismanBatches.Count; t++)
		{
			XjTalismanBatchArchiveRecord batch = talismanBatches[t];
			if (batch == null
				|| batch.OwnerScope != XjCraftOwnerScope.Sect
				|| batch.OwnerId <= 0L
				|| batch.Quantity <= 0
				|| string.IsNullOrWhiteSpace(batch.TalismanId))
			{
				continue;
			}
			if (!talismanBySect.TryGetValue(batch.OwnerId, out List<XjTalismanBatchArchiveRecord> list))
			{
				list = new List<XjTalismanBatchArchiveRecord>();
				talismanBySect[batch.OwnerId] = list;
			}
			list.Add(batch);
		}
		IReadOnlyList<XjCraftResourceArchiveRecord> craftResources = XjCraftDomainRegistry.ReadResources();
		Dictionary<long, List<XjCraftResourceArchiveRecord>> formationMaterialsBySect = new Dictionary<long, List<XjCraftResourceArchiveRecord>>();
		for (int r = 0; r < craftResources.Count; r++)
		{
			XjCraftResourceArchiveRecord resource = craftResources[r];
			if (resource == null
				|| resource.OwnerScope != XjCraftOwnerScope.Sect
				|| resource.OwnerId <= 0L
				|| resource.Quantity <= 0
				|| !IsFormationMaterialResource(resource.ResourceId))
			{
				continue;
			}
			if (!formationMaterialsBySect.TryGetValue(resource.OwnerId, out List<XjCraftResourceArchiveRecord> list))
			{
				list = new List<XjCraftResourceArchiveRecord>();
				formationMaterialsBySect[resource.OwnerId] = list;
			}
			list.Add(resource);
		}
		Dictionary<long, long> sectByFamily = new Dictionary<long, long>();
		for (int s = 0; s < seats.Count; s++)
		{
			XjSectFamilySeatArchiveRecord seat = seats[s];
			if (seat?.SectId > 0L && seat.FamilyId > 0L) sectByFamily[seat.FamilyId] = seat.SectId;
		}
		Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>> faBaoBySect = new Dictionary<long, List<XjFamilyFaBaoWarehouseEntry>>();
		IReadOnlyList<XjFamilyFaBaoWarehouseEntry> faBaoEntries = XjFamilyFaBaoWarehouse.ReadAllEntries();
		for (int f = 0; f < faBaoEntries.Count; f++)
		{
			XjFamilyFaBaoWarehouseEntry entry = faBaoEntries[f];
			if (!entry.Found) continue;
			long sectId = entry.SectId;
			if (sectId <= 0L
				&& (entry.FamilyStableId <= 0L || !sectByFamily.TryGetValue(entry.FamilyStableId, out sectId))) continue;
			if (!faBaoBySect.TryGetValue(sectId, out List<XjFamilyFaBaoWarehouseEntry> list))
			{
				list = new List<XjFamilyFaBaoWarehouseEntry>();
				faBaoBySect[sectId] = list;
			}
			list.Add(entry);
		}
		for (int i = 0; i < records.Count; i++)
		{
			XjSectArchiveRecord sect = records[i];
			if (sect == null || sect.SectId <= 0L) continue;
			string sectName = sectNames.TryGetValue(sect.SectId, out string knownName) ? knownName : sect.Name ?? string.Empty;

			IReadOnlyList<XjSectGongFaPavilionEntry> gongFa = XjSectGongFaPavilion.ReadGongFaEntries(sect.SectId);
			int displayedGongFa = 0;
			for (int g = 0; g < gongFa.Count; g++)
			{
				XjSectGongFaPavilionEntry entry = gongFa[g];
				if (!entry.Found || string.IsNullOrWhiteSpace(entry.Name)) continue;
				if (displayedGongFa >= MaxDisplayedGongFaPerSect) continue;
				displayedGongFa++;
				result.Add(new XjCodexSectResourceItem
				{
					SectId = sect.SectId,
					SectName = sectName,
					ResourceKind = "功法",
					Name = entry.Name,
					Grade = entry.Grade,
					DaoTu = entry.DaoTu,
					Amount = 1,
					Source = EmptyResourceSource(entry.SourceType, entry.MappedXianJi),
					SourceActorName = entry.ActorName,
					Year = entry.Year,
					Detail = XjFuQiCoreCatalog.IsKnownMethodName(entry.Name)
						? "服气本命：" + EmptyText(entry.DaoTu, "本命核心")
						: "映照仙基：" + EmptyText(entry.MappedXianJi, "未明")
				});
			}
			AddOverflowResource(result, sect, sectName, "功法", gongFa.Count - displayedGongFa);

			IReadOnlyList<XjSectGongFaPavilionEntry> qiuJinFa = XjSectGongFaPavilion.ReadQiuJinFaEntries(sect.SectId);
			int displayedQiuJinFa = 0;
			for (int q = 0; q < qiuJinFa.Count; q++)
			{
				XjSectGongFaPavilionEntry entry = qiuJinFa[q];
				if (!entry.Found || string.IsNullOrWhiteSpace(entry.Name)) continue;
				if (displayedQiuJinFa >= MaxDisplayedQiuJinFaPerSect) continue;
				displayedQiuJinFa++;
				result.Add(new XjCodexSectResourceItem
				{
					SectId = sect.SectId,
					SectName = sectName,
					ResourceKind = "求金法",
					Name = entry.Name,
					Grade = entry.Grade,
					DaoTu = entry.DaoTu,
					Amount = 1,
					Source = "不绑定具体功法",
					SourceActorName = entry.ActorName,
					Year = entry.Year,
					Detail = "求金之法"
				});
			}
			AddOverflowResource(result, sect, sectName, "求金法", qiuJinFa.Count - displayedQiuJinFa);

			IReadOnlyList<XjSectCaiQiWarehouseEntry> caiQi = XjSectCaiQiWarehouse.ReadCaiQiResources(sect.SectId);
			for (int c = 0; c < caiQi.Count; c++)
			{
				XjSectCaiQiWarehouseEntry entry = caiQi[c];
				if (!entry.Found || entry.Amount <= 0) continue;
				result.Add(new XjCodexSectResourceItem
				{
					SectId = sect.SectId,
					SectName = sectName,
					ResourceKind = "纳气",
					Name = ResolveCaiQiDisplayName(entry.ResourceId, entry.ResourceName),
					DaoTu = entry.DaoTu,
					Amount = entry.Amount,
					Source = entry.SourcePlace,
					SourceActorName = entry.ActorName,
					Year = entry.Year,
					Detail = "纳气库存"
				});
			}

			IReadOnlyList<XjSectCaiQiWarehouseEntry> caiQiFa = XjSectCaiQiWarehouse.ReadCaiQiFaResources(sect.SectId);
			int displayedCaiQiFa = 0;
			for (int f = 0; f < caiQiFa.Count; f++)
			{
				XjSectCaiQiWarehouseEntry entry = caiQiFa[f];
				if (!entry.Found || string.IsNullOrWhiteSpace(entry.ResourceName)) continue;
				if (displayedCaiQiFa >= MaxDisplayedCaiQiFaPerSect) continue;
				displayedCaiQiFa++;
				result.Add(new XjCodexSectResourceItem
				{
					SectId = sect.SectId,
					SectName = sectName,
					ResourceKind = "采气法",
					Name = entry.ResourceName,
					DaoTu = entry.DaoTu,
					Amount = 1,
					Source = entry.SourcePlace,
					SourceActorName = entry.ActorName,
					Year = entry.Year,
					Detail = "采气道途：" + EmptyText(entry.DaoTu, "未定")
				});
			}
			AddOverflowResource(result, sect, sectName, "采气法", caiQiFa.Count - displayedCaiQiFa);

			if (faBaoBySect.TryGetValue(sect.SectId, out List<XjFamilyFaBaoWarehouseEntry> sectFaBao))
			{
				int limit = Math.Min(sectFaBao.Count, MaxDisplayedArtifactsPerSect);
				for (int a = 0; a < limit; a++)
				{
					XjFamilyFaBaoWarehouseEntry entry = sectFaBao[a];
					result.Add(new XjCodexSectResourceItem
					{
						SectId = sect.SectId,
						SectName = sectName,
						ResourceKind = "炼器",
						Name = entry.FaBaoName,
						DaoTu = entry.DaoTu,
						Amount = 1,
						Source = entry.SectId > 0L ? "宗门器库" : "门下家族器库",
						SourceActorName = entry.ActorName,
						Year = entry.Year,
						Detail = EmptyText(entry.ClassName, "器物")
					});
				}
				AddOverflowResource(result, sect, sectName, "炼器", sectFaBao.Count - limit);
			}

			XjAlchemyInventorySnapshot alchemy = XjAlchemyInventoryRegistry.ReadSnapshot(new XjAlchemyOwnerKey(XjAlchemyOwnerScope.ZongMen, sect.SectId));
			int displayedPills = 0;
			for (int p = 0; p < alchemy.PillBatches.Count; p++)
			{
				XjAlchemyPillBatchState batch = alchemy.PillBatches[p];
				if (batch.Quantity <= 0 || string.IsNullOrWhiteSpace(batch.PillId)) continue;
				if (displayedPills >= MaxDisplayedPillBatchesPerSect) continue;
				displayedPills++;
				result.Add(new XjCodexSectResourceItem
				{
					SectId = sect.SectId,
					SectName = sectName,
					ResourceKind = "丹药",
					Name = XjAlchemyText.PillName(batch.PillId),
					Amount = batch.Quantity,
					Source = "宗门丹房",
					SourceActorName = ResolveActorName(batch.CrafterActorId),
					Year = batch.CraftedYear,
					Detail = "药效：" + XjAlchemyText.PillShortDescription(batch.PillId)
						+ "；" + ResolvePillRealmDisplay(batch.PillId)
				});
			}
			AddOverflowResource(result, sect, sectName, "丹药", alchemy.PillBatches.Count - displayedPills);

			if (!talismanBySect.TryGetValue(sect.SectId, out List<XjTalismanBatchArchiveRecord> sectTalismans))
			{
				sectTalismans = null;
			}
			if (sectTalismans != null)
			{
				int displayedTalismans = 0;
				for (int t = 0; t < sectTalismans.Count; t++)
				{
					XjTalismanBatchArchiveRecord batch = sectTalismans[t];
					if (displayedTalismans >= MaxDisplayedTalismanBatchesPerSect) continue;
					displayedTalismans++;
					string name = XjTalismanCatalog.TryGet(batch.TalismanId, out XjTalismanDefinition definition)
						? definition.DisplayName
						: batch.TalismanId;
					result.Add(new XjCodexSectResourceItem
					{
						SectId = sect.SectId,
						SectName = sectName,
						ResourceKind = "符箓",
						Name = name,
						Amount = batch.Quantity,
						Source = "宗门符库",
						SourceActorName = ResolveActorName(batch.CrafterActorId),
						Year = batch.CraftedYear,
						Detail = "效果：" + XjTalismanCatalog.EffectSummary(batch.TalismanId)
							+ "；" + ResolveTalismanRealmDisplay(definition)
					});
				}
				AddOverflowResource(result, sect, sectName, "符箓", sectTalismans.Count - displayedTalismans);
			}
			if (formationMaterialsBySect.TryGetValue(sect.SectId, out List<XjCraftResourceArchiveRecord> sectFormationMaterials))
			{
				int displayedMaterials = 0;
				for (int m = 0; m < sectFormationMaterials.Count; m++)
				{
					XjCraftResourceArchiveRecord resource = sectFormationMaterials[m];
					if (displayedMaterials >= MaxDisplayedFormationMaterialsPerSect) continue;
					displayedMaterials++;
					ResolveFormationMaterialDisplay(resource.ResourceId, out string name, out string detail);
					result.Add(new XjCodexSectResourceItem
					{
						SectId = sect.SectId,
						SectName = sectName,
						ResourceKind = "阵法素材",
						Name = name,
						Amount = resource.Quantity,
						Source = "宗门阵库",
						Year = resource.UpdatedYear,
						Detail = detail
					});
				}
				AddOverflowResource(result, sect, sectName, "阵法素材", sectFormationMaterials.Count - displayedMaterials);
			}
		}
		result.Sort((left, right) =>
		{
			int sect = string.Compare(left.SectName, right.SectName, StringComparison.Ordinal);
			if (sect != 0) return sect;
			int kind = string.Compare(left.ResourceKind, right.ResourceKind, StringComparison.Ordinal);
			if (kind != 0) return kind;
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			if (name != 0) return name;
			int grade = right.Grade.CompareTo(left.Grade);
			if (grade != 0) return grade;
			int source = string.Compare(left.Source, right.Source, StringComparison.Ordinal);
			if (source != 0) return source;
			int sourceActor = string.Compare(left.SourceActorName, right.SourceActorName, StringComparison.Ordinal);
			if (sourceActor != 0) return sourceActor;
			int year = left.Year.CompareTo(right.Year);
			if (year != 0) return year;
			int detail = string.Compare(left.Detail, right.Detail, StringComparison.Ordinal);
			return detail != 0 ? detail : left.SectId.CompareTo(right.SectId);
		});
		return result;
	}

private static void ApplySectResourceSummary(IReadOnlyList<XjCodexSectItem> sects, IReadOnlyList<XjCodexSectResourceItem> resources)
	{
		if (sects == null || resources == null || sects.Count == 0 || resources.Count == 0) return;
		Dictionary<long, XjCodexSectItem> bySect = new Dictionary<long, XjCodexSectItem>();
		for (int i = 0; i < sects.Count; i++) if (sects[i]?.SectId > 0L) bySect[sects[i].SectId] = sects[i];
		for (int i = 0; i < resources.Count; i++)
		{
			XjCodexSectResourceItem resource = resources[i];
			if (resource == null || !bySect.TryGetValue(resource.SectId, out XjCodexSectItem sect)) continue;
			if (resource.ResourceKind == "功法") sect.GongFaCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "求金法") sect.QiuJinFaCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "纳气") sect.CaiQiCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "采气法") sect.CaiQiFaCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "丹药") sect.PillStockCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "炼器") sect.ArtifactStockCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "符箓") sect.TalismanStockCount += Math.Max(1, resource.Amount);
			else if (resource.ResourceKind == "阵法素材") sect.FormationStockCount += Math.Max(1, resource.Amount);
		}
		for (int i = 0; i < sects.Count; i++)
		{
			XjCodexSectItem sect = sects[i];
			if (sect == null) continue;
			sect.CraftStockCount = sect.PillStockCount + sect.ArtifactStockCount + sect.TalismanStockCount + sect.FormationStockCount;
			sect.ResourceSummary = "功法" + sect.GongFaCount + " - 求金法" + sect.QiuJinFaCount
				+ " - 纳气" + sect.CaiQiCount + " - 采气法" + sect.CaiQiFaCount
				+ " - 丹药" + sect.PillStockCount + " - 炼器" + sect.ArtifactStockCount
				+ " - 符箓" + sect.TalismanStockCount + " - 阵法" + sect.FormationStockCount;
		}
	}

	private static void AddOverflowResource(List<XjCodexSectResourceItem> result, XjSectArchiveRecord sect, string sectName, string kind, int overflow)
	{
		if (result == null || sect == null || overflow <= 0) return;
		result.Add(new XjCodexSectResourceItem
		{
			SectId = sect.SectId,
			SectName = sectName,
			ResourceKind = kind,
			Name = "另有" + overflow.ToString(CultureInfo.InvariantCulture) + "项未展开",
			Amount = overflow,
			Source = "宗门底蕴汇总",
			Detail = "为降低长档刷新压力，本页仅展开前列条目。"
		});
	}

	private static bool IsFormationMaterialResource(string resourceId)
	{
		return resourceId == XjCraftCollaborationSystem.FormationFlagLow
			|| resourceId == XjCraftCollaborationSystem.FormationFlagHigh
			|| resourceId == XjCraftCollaborationSystem.FormationPlateLow
			|| resourceId == XjCraftCollaborationSystem.FormationPlateHigh
			|| resourceId == XjCraftCollaborationSystem.FormationRune
			|| resourceId == XjCraftCollaborationSystem.SpiritMaterial;
	}

	private static void ResolveFormationMaterialDisplay(string resourceId, out string name, out string detail)
	{
		switch (resourceId)
		{
			case XjCraftCollaborationSystem.FormationFlagLow:
				name = "下品阵旗";
				detail = "低阶阵法与阵脚修补材料";
				return;
			case XjCraftCollaborationSystem.FormationFlagHigh:
				name = "上品阵旗";
				detail = "宗门大阵主脉材料";
				return;
			case XjCraftCollaborationSystem.FormationPlateLow:
				name = "下品阵盘";
				detail = "基础阵法工程材料";
				return;
			case XjCraftCollaborationSystem.FormationPlateHigh:
				name = "上品阵盘";
				detail = "大阵枢纽材料";
				return;
			case XjCraftCollaborationSystem.FormationRune:
				name = "阵纹";
				detail = "阵图、秘境工程与大阵铭刻材料";
				return;
			case XjCraftCollaborationSystem.SpiritMaterial:
				name = "良材";
				detail = "宗门工程与阵法修复材料";
				return;
			default:
				name = "未名阵材";
				detail = "宗门阵库材料";
				return;
		}
	}

	private static string ResolveCraftRankDisplay(Actor actor, string traitId)
	{
		int rank = string.Equals(traitId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal)
			? XjCraftProficiencySystem.GetAlchemyRank(actor)
			: string.Equals(traitId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal)
				? XjCraftProficiencySystem.GetArtifactRank(actor)
				: string.Equals(traitId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal)
					? XjCraftProficiencySystem.GetTalismanRank(actor)
					: string.Equals(traitId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)
						? XjCraftProficiencySystem.GetFormationRank(actor)
						: XjCraftProficiencySystem.RankNone;
		return XjCraftProficiencySystem.GetRankTierDisplayName(rank);
	}

	private static List<XjCodexCraftItem> BuildCraftActors(IReadOnlyList<XjCraftTaskArchiveRecord> tasks)
	{
		Dictionary<long, XjCraftTaskArchiveRecord> openTaskByActor = new Dictionary<long, XjCraftTaskArchiveRecord>();
		for (int i = 0; i < tasks.Count; i++)
		{
			XjCraftTaskArchiveRecord task = tasks[i];
			if (task?.IsOpen == true && task.ActorId > 0L) openTaskByActor[task.ActorId] = task;
		}
		IReadOnlyList<XjCraftActorIndexEntry> entries = XjCraftActorIndex.ReadAll();
		List<XjCodexCraftItem> result = new List<XjCodexCraftItem>(entries.Count);
		for (int i = 0; i < entries.Count; i++)
		{
			XjCraftActorIndexEntry entry = entries[i];
			if (!XjScheduler.ResolveActor(entry.ActorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
			XjSectRepository.TryGetByActor(actor, out XjSectArchiveRecord sect);
			openTaskByActor.TryGetValue(entry.ActorId, out XjCraftTaskArchiveRecord task);
			result.Add(new XjCodexCraftItem
			{
				ActorId = entry.ActorId,
				ActorName = SafeActorName(actor),
				ProfessionId = entry.TraitId,
				ProfessionName = ResolveProfessionName(entry.TraitId),
				CraftRank = ResolveCraftRankDisplay(actor, entry.TraitId),
				Realm = XjRealmHelper.GetDisplayName(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter)),
				CityName = actor.city?.data == null ? string.Empty : SafeDataName(actor.city.data, string.Empty),
				SectName = sect?.Name ?? string.Empty,
				TaskKind = task?.TaskKind ?? string.Empty,
				TaskStatus = task?.Status ?? string.Empty,
				TaskBlueprint = task?.BlueprintId ?? string.Empty,
				TaskDueYear = task?.DueYear ?? 0
			});
		}
		result.Sort((left, right) =>
		{
			int profession = string.Compare(left.ProfessionName, right.ProfessionName, StringComparison.Ordinal);
			if (profession != 0) return profession;
			int actorName = string.Compare(left.ActorName, right.ActorName, StringComparison.Ordinal);
			return actorName != 0 ? actorName : left.ActorId.CompareTo(right.ActorId);
		});
		return result;
	}

	private static List<XjCodexCraftProfessionSummaryItem> BuildCraftProfessionSnapshotSummaries(
		IReadOnlyList<XjCodexCraftItem> actors)
	{
		Dictionary<string, XjCodexCraftProfessionSummaryItem> byProfession =
			new Dictionary<string, XjCodexCraftProfessionSummaryItem>(StringComparer.Ordinal);
		if (actors != null)
		{
			for (int i = 0; i < actors.Count; i++)
			{
				XjCodexCraftItem item = actors[i];
				if (item == null) continue;
				string professionId = item.ProfessionId ?? string.Empty;
				string professionName = string.IsNullOrWhiteSpace(item.ProfessionName) ? "未定百艺" : item.ProfessionName.Trim();
				string key = professionId.Length > 0 ? professionId : professionName;
				if (!byProfession.TryGetValue(key, out XjCodexCraftProfessionSummaryItem summary))
				{
					summary = new XjCodexCraftProfessionSummaryItem
					{
						ProfessionId = professionId,
						ProfessionName = professionName
					};
					byProfession[key] = summary;
				}
				summary.Total++;
				if (!string.IsNullOrWhiteSpace(item.TaskKind)) summary.Busy++;
				if (ResolveCraftRealmRank(item.Realm) > ResolveCraftRealmRank(summary.TopRealm)) summary.TopRealm = item.Realm;
			}
		}

		List<XjCodexCraftProfessionSummaryItem> result = new List<XjCodexCraftProfessionSummaryItem>(byProfession.Values);
		result.Sort((left, right) => CraftProfessionOrder(left?.ProfessionId).CompareTo(CraftProfessionOrder(right?.ProfessionId)));
		return result;
	}

	private static void TrimCraftActorsFairly(List<XjCodexCraftItem> actors, int maximum)
	{
		if (actors == null || maximum <= 0)
		{
			actors?.Clear();
			return;
		}
		if (actors.Count <= maximum) return;

		Dictionary<string, List<XjCodexCraftItem>> groups =
			new Dictionary<string, List<XjCodexCraftItem>>(StringComparer.Ordinal);
		List<string> keys = new List<string>();
		for (int i = 0; i < actors.Count; i++)
		{
			XjCodexCraftItem item = actors[i];
			if (item == null) continue;
			string key = string.IsNullOrWhiteSpace(item.ProfessionId) ? (item.ProfessionName ?? string.Empty) : item.ProfessionId;
			if (!groups.TryGetValue(key, out List<XjCodexCraftItem> group))
			{
				group = new List<XjCodexCraftItem>();
				groups[key] = group;
				keys.Add(key);
			}
			group.Add(item);
		}
		keys.Sort((left, right) => CraftProfessionOrder(left).CompareTo(CraftProfessionOrder(right)));

		Dictionary<string, int> cursors = new Dictionary<string, int>(StringComparer.Ordinal);
		List<XjCodexCraftItem> selected = new List<XjCodexCraftItem>(maximum);
		bool added;
		do
		{
			added = false;
			for (int i = 0; i < keys.Count && selected.Count < maximum; i++)
			{
				string key = keys[i];
				int cursor = cursors.TryGetValue(key, out int value) ? value : 0;
				List<XjCodexCraftItem> group = groups[key];
				if (cursor >= group.Count) continue;
				selected.Add(group[cursor]);
				cursors[key] = cursor + 1;
				added = true;
			}
		}
		while (added && selected.Count < maximum);

		actors.Clear();
		actors.AddRange(selected);
	}

	private static int CraftProfessionOrder(string professionId)
	{
		if (string.Equals(professionId, XjCraftTraitRules.AlchemyTraitId, StringComparison.Ordinal)) return 0;
		if (string.Equals(professionId, XjCraftTraitRules.ArtifactRefiningTraitId, StringComparison.Ordinal)) return 1;
		if (string.Equals(professionId, XjCraftTraitRules.TalismanTraitId, StringComparison.Ordinal)) return 2;
		if (string.Equals(professionId, XjCraftTraitRules.FormationTraitId, StringComparison.Ordinal)) return 3;
		return 10;
	}

	private static int ResolveCraftRealmRank(string realm)
	{
		if (string.IsNullOrWhiteSpace(realm)) return 0;
		if (realm.IndexOf("道胎", StringComparison.Ordinal) >= 0) return 6;
		if (realm.IndexOf("金丹", StringComparison.Ordinal) >= 0
			|| realm.IndexOf("神丹", StringComparison.Ordinal) >= 0
			|| realm.IndexOf("真君", StringComparison.Ordinal) >= 0) return 5;
		if (realm.IndexOf("紫府", StringComparison.Ordinal) >= 0
			|| realm.IndexOf("真人", StringComparison.Ordinal) >= 0) return 4;
		if (realm.IndexOf("筑基", StringComparison.Ordinal) >= 0
			|| realm.IndexOf("黄冠", StringComparison.Ordinal) >= 0) return 3;
		if (realm.IndexOf("炼气", StringComparison.Ordinal) >= 0) return 2;
		if (realm.IndexOf("胎息", StringComparison.Ordinal) >= 0) return 1;
		return 0;
	}
}
