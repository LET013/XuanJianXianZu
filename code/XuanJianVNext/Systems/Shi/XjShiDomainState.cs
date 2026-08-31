using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释土、金地与旃檀林的世界级单权威状态。角色只保存自己所依承载地的稳定ID，
/// 所有权、承载关系、显隐与容量均以本注册表为准。旃檀林本体无单一主人，但其内三十八块金地可分别由庙主持有；旧版主持字段只作迁移清零。每年只从修士缓存重建一次，
/// 不扫描World.world.units，也不在帧循环中运行。
/// </summary>
internal static class XjShiDomainState
{
	internal const int CurrentMigrationVersion = 14;
	private static readonly Dictionary<string, XjShiDomainRecord> DomainsById =
		new Dictionary<string, XjShiDomainRecord>(StringComparer.Ordinal);
	private static readonly string[] JinDiNamePrefixes =
	{
		"宝牙", "白马", "莲花", "琉璃", "无垢", "照寂",
		"玄胎", "灵台", "法常", "金刚", "明轮", "净海"
	};

	private static int _lastReconciledYear;
	// 年度释土对账只扫描已索引释修。三个境界桶只保存稳定 ActorId 并跨年复用，
	// 避免 20x/40x 下每个世界年固定制造三只列表，同时绝不长期持有原生 Actor 引用。
	private static readonly List<long> MoHeActorIdScratch = new List<long>();
	private static readonly List<long> DharmaFormActorIdScratch = new List<long>();
	private static readonly List<long> LianMinActorIdScratch = new List<long>();
	private static int _lastNorthWorldTopologyEnsureYear;
	private static bool _duhuaPopulationGateActive;
	private static bool _dirty = true;

	internal static string BuildJinDiId(long ownerActorId)
	{
		return ownerActorId > 0L
			? "shi:domain:jindi:" + ownerActorId.ToString(CultureInfo.InvariantCulture)
			: string.Empty;
	}

	internal static string BuildYingTuId(long ownerActorId, long hostActorId)
	{
		long stable = ownerActorId > 0L ? ownerActorId : hostActorId;
		return stable > 0L
			? "shi:domain:yingtu:" + stable.ToString(CultureInfo.InvariantCulture)
			: string.Empty;
	}

	internal static void Invalidate()
	{
		_dirty = true;
	}

	internal static void Clear()
	{
		DomainsById.Clear();
		MoHeActorIdScratch.Clear();
		DharmaFormActorIdScratch.Clear();
		LianMinActorIdScratch.Clear();
		_lastReconciledYear = 0;
		_lastNorthWorldTopologyEnsureYear = 0;
		_duhuaPopulationGateActive = false;
		_dirty = true;
	}

	internal static bool NeedsActorMigration(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion, out int version);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		return version < CurrentMigrationVersion || !TryGet(domainId, out _);
	}

	internal static XjShiDomainWorldArchiveData ExportState()
	{
		List<string> ids = new List<string>(DomainsById.Keys);
		ids.Sort(StringComparer.Ordinal);
		XjShiDomainWorldArchiveData archive = new XjShiDomainWorldArchiveData
		{
			LastReconciledYear = Math.Max(0, _lastReconciledYear),
			MigrationVersion = CurrentMigrationVersion,
			DuhuaPopulationGateActive = _duhuaPopulationGateActive
		};
		for (int i = 0; i < ids.Count; i++)
		{
			archive.Domains.Add(DomainsById[ids[i]].Clone());
		}
		return archive;
	}

	internal static void ImportState(XjShiDomainWorldArchiveData archive)
	{
		DomainsById.Clear();
		_lastReconciledYear = Math.Max(0, archive?.LastReconciledYear ?? 0);
		_lastNorthWorldTopologyEnsureYear = 0;
		_duhuaPopulationGateActive = archive?.DuhuaPopulationGateActive ?? false;
		if (archive?.Domains != null)
		{
			for (int i = 0; i < archive.Domains.Count; i++)
			{
				XjShiDomainRecord normalized = Normalize(archive.Domains[i]);
				if (normalized == null || DomainsById.ContainsKey(normalized.DomainId)) continue;
				if (string.IsNullOrWhiteSpace(normalized.LegacyMigrationState))
				{
					normalized.LegacyMigrationState = XjShiDomainMigrationIds.ImportedArchive;
				}
				DomainsById[normalized.DomainId] = normalized;
			}
		}
		_dirty = true;
	}

	internal static bool IsDuhuaPopulationGateActive => _duhuaPopulationGateActive;

	internal static void SetDuhuaPopulationGateActive(bool active)
	{
		if (_duhuaPopulationGateActive == active) return;
		_duhuaPopulationGateActive = active;
		MarkChanged();
	}

	internal static bool TryGet(string domainId, out XjShiDomainRecord record)
	{
		record = null;
		string id = NormalizeId(domainId);
		return id.Length > 0 && DomainsById.TryGetValue(id, out record);
	}

	internal static string ResolveDomainDisplayName(string domainId)
	{
		string id = NormalizeId(domainId);
		if (id.Length == 0) return "承载地未载入";
		if (TryGet(id, out XjShiDomainRecord domain) && domain != null)
			return XjShiDomainCatalog.GetDomainDisplayName(domain);
		if (string.Equals(id, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
			return "旃檀林";
		if (XjShiHeavenCatalog.TryParseFragmentId(id, out int fragmentIndex))
			return XjShiHeavenCatalog.GetFragmentDisplayName(fragmentIndex);
		if (id.StartsWith("shi:domain:jindi:", StringComparison.Ordinal)) return "未载入金地";
		if (id.StartsWith("shi:domain:yingtu:", StringComparison.Ordinal)) return "未载入应土";
		return "承载地未载入";
	}

	internal static XjShiDomainRecord EnsureZhantanlin(int annualYear)
	{
		XjShiDomainRecord domain = EnsureDomain(XjShiDomainCatalog.ZhantanlinDomainId,
			XjShiDomainTypeIds.Zhantanlin, XjShiTraditionIds.Modern, string.Empty, Math.Max(1, annualYear));
		bool changed = false;
		if (!string.Equals(domain.DisplayName, "旃檀林", StringComparison.Ordinal))
		{
			domain.DisplayName = "旃檀林";
			changed = true;
		}
		// 旃檀林是今释共有实体释土，没有单一主人或“主持摩诃”。
		if (domain.OwnerActorId != 0L) { domain.OwnerActorId = 0L; changed = true; }
		if (domain.HostMoHeId != 0L) { domain.HostMoHeId = 0L; changed = true; }
		if (domain.MoHePositionCapacity != 108) { domain.MoHePositionCapacity = 108; changed = true; }
		if (domain.LianMinPositionBaseCapacity != 108) { domain.LianMinPositionBaseCapacity = 108; changed = true; }
		// 旃檀林本体只承载今释肉身，不直接提供法相位；法相资格来自其内外金地的独立权属。
		if (domain.DharmaFormPositionCapacity != 0) { domain.DharmaFormPositionCapacity = 0; changed = true; }
		if (domain.AbsorbedJinDiCount != XjShiHeavenCatalog.ZhantanlinFragmentCount)
		{
			domain.AbsorbedJinDiCount = XjShiHeavenCatalog.ZhantanlinFragmentCount;
			changed = true;
		}
		int year = Math.Max(1, annualYear);
		if (_lastNorthWorldTopologyEnsureYear != year)
		{
			changed |= EnsureNorthWorldHonoredFragments(domain, year);
			_lastNorthWorldTopologyEnsureYear = year;
		}
		if (changed) MarkChanged();
		return domain;
	}

	internal static int GetZhantanlinTerrainSchema()
	{
		return TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			&& domain != null ? Math.Max(0, domain.MapTerrainSchema) : 0;
	}

	internal static void MarkZhantanlinTerrainSchema(int schema)
	{
		if (schema <= 0 || !TryGet(XjShiDomainCatalog.ZhantanlinDomainId, out XjShiDomainRecord domain)
			|| domain == null || domain.MapTerrainSchema >= schema) return;
		domain.MapTerrainSchema = schema;
		MarkChanged();
	}

	internal static bool PlaceZhantanlin(int centerX, int centerY, int radius, int annualYear)
	{
		XjShiDomainRecord domain = EnsureZhantanlin(annualYear);
		_ = radius; // 旃檀林自0.9.9.3起固定范围，旧调用参数不再改变区域大小。
		int resolvedRadius = XjZhantanlinSystem.DefaultRadius;
		bool terrainChanged = domain.MapCenterX != centerX || domain.MapCenterY != centerY
			|| domain.MapRadius != resolvedRadius;
		bool changed = terrainChanged
			|| !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal);
		if (terrainChanged) domain.MapTerrainSchema = 0;
		domain.MapCenterX = centerX;
		domain.MapCenterY = centerY;
		domain.MapRadius = resolvedRadius;
		domain.LastPlacedYear = Math.Max(1, annualYear);
		domain.Visibility = XjShiDomainVisibilityIds.Manifest;
		domain.LastManifestYear = Math.Max(1, annualYear);
		domain.DisplayName = "旃檀林";
		domain.Tradition = XjShiTraditionIds.Modern;
		domain.OwnerActorId = 0L;
		domain.HostMoHeId = 0L;
		domain.MoHePositionCapacity = 108;
		domain.LianMinPositionBaseCapacity = 108;
		domain.DharmaFormPositionCapacity = 0;
		domain.AbsorbedJinDiCount = XjShiHeavenCatalog.ZhantanlinFragmentCount;
		if (changed) MarkChanged();
		_dirty = true;
		return true;
	}


	private static bool EnsureNorthWorldHonoredFragments(XjShiDomainRecord zhantanlin, int annualYear)
	{
		if (zhantanlin == null) return false;
		bool changed = false;
		for (int fragmentIndex = 0; fragmentIndex < XjShiHeavenCatalog.TotalFragments; fragmentIndex++)
		{
			int heavenIndex = XjShiHeavenCatalog.GetHeavenIndexForFragment(fragmentIndex);
			string domainId = XjShiHeavenCatalog.BuildFragmentId(fragmentIndex);
			XjShiDomainRecord fragment = EnsureDomain(domainId, XjShiDomainTypeIds.JinDi,
				XjShiTraditionIds.Modern, string.Empty, annualYear);
			string displayName = XjShiHeavenCatalog.GetFragmentDisplayName(fragmentIndex);
			string heavenId = XjShiHeavenCatalog.GetHeavenId(heavenIndex);
			string category = XjShiHeavenCatalog.GetCategoryId(heavenIndex);
			int ordinal = XjShiHeavenCatalog.GetFragmentOrdinal(fragmentIndex);
			int fragmentCount = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			if (!string.Equals(fragment.DisplayName, displayName, StringComparison.Ordinal))
			{ fragment.DisplayName = displayName; changed = true; }
			if (fragment.IsNorthWorldHonoredFragment != 1)
			{ fragment.IsNorthWorldHonoredFragment = 1; changed = true; }
			if (!string.Equals(fragment.SourceHeavenId, heavenId, StringComparison.Ordinal))
			{ fragment.SourceHeavenId = heavenId; changed = true; }
			if (!string.Equals(fragment.SourceHeavenCategory, category, StringComparison.Ordinal))
			{ fragment.SourceHeavenCategory = category; changed = true; }
			if (fragment.SourceHeavenIndex != heavenIndex)
			{ fragment.SourceHeavenIndex = heavenIndex; changed = true; }
			if (fragment.SourceHeavenFragmentOrdinal != ordinal)
			{ fragment.SourceHeavenFragmentOrdinal = ordinal; changed = true; }
			if (fragment.SourceHeavenFragmentCount != fragmentCount)
			{ fragment.SourceHeavenFragmentCount = fragmentCount; changed = true; }

			if (XjShiHeavenCatalog.IsZhantanlinFragment(fragmentIndex))
			{
				// 并入旃檀林只改变碎片所在释土，不抹掉庙主权属。多位法相可分别
				// 持有其中金地而共同支撑旃檀林，只有真灵俱灭才释放主人。
				if (fragment.OwnerActorId <= 0L && fragment.HostMoHeId != 0L)
				{ fragment.HostMoHeId = 0L; changed = true; }
				if (!string.Equals(fragment.AbsorbedByDomainId, zhantanlin.DomainId, StringComparison.Ordinal))
				{ fragment.AbsorbedByDomainId = zhantanlin.DomainId; changed = true; }
				if (fragment.AbsorbedYear <= 0) { fragment.AbsorbedYear = annualYear; changed = true; }
				if (!string.Equals(fragment.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal))
				{ fragment.Visibility = XjShiDomainVisibilityIds.Absorbed; changed = true; }
				fragment.MoHePositionCapacity = 0;
				fragment.LianMinPositionBaseCapacity = 0;
				fragment.LianMinPositionCapacity = 0;
				fragment.DharmaFormPositionCapacity = 1;
			}
			else
			{
				if (!string.IsNullOrWhiteSpace(fragment.AbsorbedByDomainId))
				{ fragment.AbsorbedByDomainId = string.Empty; changed = true; }
				if (fragment.AbsorbedYear != 0) { fragment.AbsorbedYear = 0; changed = true; }
				fragment.MoHePositionCapacity = Math.Max(1, fragment.MoHePositionCapacity);
				fragment.LianMinPositionBaseCapacity = Math.Max(1, fragment.LianMinPositionBaseCapacity);
				fragment.DharmaFormPositionCapacity = 1;
				if (fragment.OwnerActorId <= 0L
					&& !string.Equals(fragment.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal))
				{ fragment.Visibility = XjShiDomainVisibilityIds.Hidden; changed = true; }
			}
		}
		return changed;
	}

	internal static bool TryGetOwnedNorthJinDi(long actorId, out XjShiDomainRecord domain)
	{
		domain = null;
		if (actorId <= 0L) return false;
		foreach (XjShiDomainRecord candidate in DomainsById.Values)
		{
			if (candidate == null || candidate.IsNorthWorldHonoredFragment <= 0
				|| candidate.OwnerActorId != actorId) continue;
			if (domain == null || candidate.SourceHeavenFragmentOrdinal < domain.SourceHeavenFragmentOrdinal
				|| candidate.SourceHeavenFragmentOrdinal == domain.SourceHeavenFragmentOrdinal
					&& string.Compare(candidate.DomainId, domain.DomainId, StringComparison.Ordinal) < 0)
				domain = candidate;
		}
		return domain != null;
	}

	private static bool IsAbsorbedByZhantanlin(XjShiDomainRecord domain)
	{
		return domain != null && domain.IsNorthWorldHonoredFragment > 0
			&& string.Equals(domain.AbsorbedByDomainId,
				XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal);
	}

	internal static bool TransferNorthWorldHonoredOwnership(long sourceActorId, Actor target, int annualYear)
	{
		if (sourceActorId <= 0L || target?.data == null) return false;
		long targetActorId = ((BaseSystemData)target.data).id;
		if (targetActorId <= 0L || targetActorId == sourceActorId) return false;
		bool changed = false;
		XjShiDomainRecord first = null;
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null || domain.IsNorthWorldHonoredFragment <= 0
				|| domain.OwnerActorId != sourceActorId) continue;
			domain.OwnerActorId = targetActorId;
			// 庙主权属转移后同步清除旧版主持字段，当前模型不再另设代掌身份。
			if (domain.HostMoHeId == sourceActorId || domain.HostMoHeId == targetActorId)
				domain.HostMoHeId = 0L;
			if (IsAbsorbedByZhantanlin(domain))
				SetVisibility(domain, XjShiDomainVisibilityIds.Absorbed, Math.Max(1, annualYear));
			else
				SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, Math.Max(1, annualYear));
			if (first == null || domain.SourceHeavenFragmentOrdinal < first.SourceHeavenFragmentOrdinal)
				first = domain;
			changed = true;
		}
		if (!changed) return false;
		RefreshHeavenProjection(target);
		if (first != null)
		{
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(Math.Max(1, annualYear));
			BindActor(target, zhantanlin, isLiangLi: false);
			XjActorAccessor.SetString(target, XjActorDataKeys.ShiRebirthAnchorId,
				XjZhantanlinSystem.ShouldPreferOwnedJinDi(target) ? first.DomainId : zhantanlin.DomainId);
		}
		MarkChanged();
		_dirty = true;
		return true;
	}

	/// <summary>
	/// 释修转世事务失败后的金地回滚。转世期间为了让法相位校验看到“新身是庙主”，
	/// 需要先临时迁移金地；若后续位次/承载提交失败，必须把所有权恢复给旧真灵记录，
	/// 或在旧档本来无金地时释放为无主，绝不能留下指向已删除新肉身的孤儿OwnerActorId。
	/// </summary>
	internal static bool RollbackNorthWorldHonoredOwnership(long failedActorId, long restoreOwnerActorId, int annualYear)
	{
		if (failedActorId <= 0L) return false;
		bool changed = false;
		int year = Math.Max(1, annualYear);
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null || domain.IsNorthWorldHonoredFragment <= 0
				|| domain.OwnerActorId != failedActorId) continue;
			domain.OwnerActorId = Math.Max(0L, restoreOwnerActorId);
			domain.HostMoHeId = 0L;
			if (domain.OwnerActorId > 0L)
			{
				SetVisibility(domain, IsAbsorbedByZhantanlin(domain)
					? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, year);
			}
			else
			{
				SetVisibility(domain, XjShiDomainVisibilityIds.Hidden, year);
			}
			changed = true;
		}
		if (changed)
		{
			MarkChanged();
			_dirty = true;
		}
		return changed;
	}

	internal static bool TryGetHeavenProgress(long actorId, out string heavenId, out int owned, out int required)
	{
		heavenId = string.Empty;
		owned = 0;
		required = 0;
		if (!TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord first)) return false;
		heavenId = first.SourceHeavenId ?? string.Empty;
		required = Math.Max(0, first.SourceHeavenFragmentCount);
		foreach (XjShiDomainRecord candidate in DomainsById.Values)
		{
			if (candidate != null && candidate.IsNorthWorldHonoredFragment > 0
				&& candidate.OwnerActorId == actorId
				&& string.Equals(candidate.SourceHeavenId, heavenId, StringComparison.Ordinal)) owned++;
		}
		return heavenId.Length > 0;
	}

	internal static bool HasReformedHeaven(long actorId, out string heavenId)
	{
		return TryGetHeavenProgress(actorId, out heavenId, out int owned, out int required)
			&& required > 0 && owned >= required;
	}

	internal static void RefreshHeavenProjection(Actor actor)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		bool found = TryGetHeavenProgress(actorId, out string heavenId, out int owned, out int required);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiTempleMaster, found ? 1 : 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiSourceHeavenId, found ? heavenId : string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiOwnedHeavenFragments, found ? owned : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiRequiredHeavenFragments, found ? required : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiReformedHeaven,
			found && required > 0 && owned >= required ? 1 : 0);
		XjShiVisibleTraitSync.Sync(actor);
	}

	internal static bool TryEstablishTempleMasterJinDi(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return false;
		EnsureZhantanlin(annualYear);
		long actorId = ((BaseSystemData)actor.data).id;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.YuanZhaoModernJinDiGrantPending, out int yuanZhaoGrantPending)
			&& yuanZhaoGrantPending > 0
			&& TryForceEstablishTempleMasterJinDi(actor, annualYear, announce: true))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoModernJinDiGrantPending, 0);
			return true;
		}
		if (TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord ownedDomain))
		{
			// 庙主只代表金地所有权。今释肉身仍统一归入旃檀林；只有释修命数深厚者
			// 才将所掌金地改作真灵与轮回锚，不改变肉身所在。
			ownedDomain.HostMoHeId = 0L;
			ownedDomain.DharmaFormPositionCapacity = 1;
			SetVisibility(ownedDomain, IsAbsorbedByZhantanlin(ownedDomain)
				? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, annualYear);
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(annualYear);
			BindActor(actor, zhantanlin, isLiangLi: false);
			string anchorId = XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor)
				? ownedDomain.DomainId : zhantanlin.DomainId;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, anchorId);
			RefreshHeavenProjection(actor);
			return true;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			&& completedLives < XjShiCatalog.TempleMasterMinimumCompletedLives) return false;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastHeavenAttractionYear, out int lastYear);
		if (lastYear > 0 && annualYear - lastYear < XjShiCatalog.HeavenFragmentAttemptIntervalYears) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastHeavenAttractionYear, annualYear);

		List<XjShiDomainRecord> candidates = new List<XjShiDomainRecord>();
		for (int heavenIndex = 0; heavenIndex < XjShiHeavenCatalog.TotalHeavens; heavenIndex++)
		{
			bool anyOwned = false;
			XjShiDomainRecord firstFree = null;
			int first = XjShiHeavenCatalog.GetFirstFragmentIndex(heavenIndex);
			int count = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			for (int offset = 0; offset < count; offset++)
			{
				if (!TryGet(XjShiHeavenCatalog.BuildFragmentId(first + offset), out XjShiDomainRecord fragment)) continue;
				if (fragment.OwnerActorId > 0L) anyOwned = true;
				else if (firstFree == null) firstFree = fragment;
			}
			if (!anyOwned && firstFree != null) candidates.Add(firstFree);
		}
		if (candidates.Count == 0) return false;
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, annualYear);
		int chance = XjShiCatalog.TempleMasterBaseChancePerTenThousand
			+ Math.Max(0, completedLives - XjShiCatalog.TempleMasterMinimumCompletedLives) * 180
			+ (int)Math.Floor(Math.Max(0f, mingShu - XjShiCatalog.DharmaFormMinimumMingShu) * 3f)
			+ (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal) ? 700 : 0);
		chance = Math.Clamp(chance, XjShiCatalog.TempleMasterBaseChancePerTenThousand,
			XjShiCatalog.TempleMasterMaximumChancePerTenThousand);
		if (!XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor))
		{
			int roll = XjDeterministicHash.PositiveIndex(actorId + annualYear,
				"shi_temple_master_first_jindi", 10000);
			if (roll >= chance) return false;
		}
		int selectedIndex = XjDeterministicHash.PositiveIndex(actorId + annualYear * 31L,
			"shi_temple_master_heaven", candidates.Count);
		XjShiDomainRecord selected = candidates[selectedIndex];
		return AssignTempleMasterJinDi(actor, selected, annualYear, announce: true);
	}


	/// <summary>
	/// 为高境投释、洞天遗泽与旧档法相修复补齐合法庙主权属。优先分配旃檀林内的
	/// 无主金地，只有林内三十八地均已有主时才回退到释土外金地。该内部入口不走
	/// 概率，但不向玩家境界编辑器开放，且绝不夺取已有主人金地。
	/// </summary>
	internal static bool TryForceEstablishTempleMasterJinDi(Actor actor, int annualYear, bool announce)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(realm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)) return false;
		int year = Math.Max(1, annualYear);
		EnsureZhantanlin(year);
		long actorId = ((BaseSystemData)actor.data).id;
		if (TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord existing))
		{
			existing.HostMoHeId = 0L;
			existing.DharmaFormPositionCapacity = 1;
			SetVisibility(existing, IsAbsorbedByZhantanlin(existing)
				? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, year);
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(year);
			BindActor(actor, zhantanlin, isLiangLi: false);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
				XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor) ? existing.DomainId : zhantanlin.DomainId);
			RefreshHeavenProjection(actor);
			return true;
		}

		List<XjShiDomainRecord> insideCandidates = new List<XjShiDomainRecord>();
		List<XjShiDomainRecord> outsideCandidates = new List<XjShiDomainRecord>();
		for (int heavenIndex = 0; heavenIndex < XjShiHeavenCatalog.TotalHeavens; heavenIndex++)
		{
			bool anyOwned = false;
			XjShiDomainRecord firstFreeInside = null;
			XjShiDomainRecord firstFreeOutside = null;
			int first = XjShiHeavenCatalog.GetFirstFragmentIndex(heavenIndex);
			int count = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			for (int offset = 0; offset < count; offset++)
			{
				if (!TryGet(XjShiHeavenCatalog.BuildFragmentId(first + offset), out XjShiDomainRecord fragment)) continue;
				if (fragment.OwnerActorId > 0L) { anyOwned = true; break; }
				if (IsAbsorbedByZhantanlin(fragment)) firstFreeInside ??= fragment;
				else firstFreeOutside ??= fragment;
			}
			if (anyOwned) continue;
			if (firstFreeInside != null) insideCandidates.Add(firstFreeInside);
			else if (firstFreeOutside != null) outsideCandidates.Add(firstFreeOutside);
		}
		List<XjShiDomainRecord> candidates = insideCandidates.Count > 0 ? insideCandidates : outsideCandidates;
		if (candidates.Count == 0) return false;
		candidates.Sort((left, right) => string.CompareOrdinal(left?.DomainId, right?.DomainId));
		int selectedIndex = XjDeterministicHash.PositiveIndex(actorId + year * 37L,
			"shi_force_temple_master_jindi", candidates.Count);
		return AssignTempleMasterJinDi(actor, candidates[selectedIndex], year, announce);
	}

	private static bool AssignTempleMasterJinDi(Actor actor, XjShiDomainRecord selected,
		int annualYear, bool announce)
	{
		if (actor?.data == null || selected == null || selected.OwnerActorId > 0L) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;
		selected.OwnerActorId = actorId;
		selected.HostMoHeId = 0L;
		selected.Tradition = XjShiTraditionIds.Modern;
		selected.DharmaFormPositionCapacity = 1;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		selected.LineageId = lineageId ?? string.Empty;
		selected.Growth = Math.Max(selected.Growth, XjShiCatalog.TempleMasterInitialDomainGrowth);
		selected.LastGrowthYear = Math.Max(1, annualYear);
		bool insideZhantanlin = IsAbsorbedByZhantanlin(selected);
		SetVisibility(selected, insideZhantanlin
			? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, annualYear);

		// 今释肉身始终归入旃檀林；庙主金地承担法相根基。仅释修命数深厚者
		// 将其改作真灵与轮回锚，其他今释仍以旃檀林归返。
		XjShiDomainRecord zhantanlin = EnsureZhantanlin(annualYear);
		BindActor(actor, zhantanlin, isLiangLi: false);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
			XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor) ? selected.DomainId : zhantanlin.DomainId);
		RefreshHeavenProjection(actor);
		MarkChanged();
		_dirty = true;
		if (announce)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"感得北世尊应身碎片，掌握" + selected.DisplayName + "，受尊为庙主。",
				XjShiTraitIds.MoHe);
			XjThreeBookWriter.RecordShiJinDiObtained(actor, annualYear, selected.DomainId, selected.DisplayName);
			XjShiAnnouncementSystem.OnTempleMaster(actor, selected.DisplayName);
		}
		return true;
	}

	internal static bool TryGetDharmaFormFoundation(Actor actor, int annualYear,
		out XjShiDomainRecord domain)
	{
		domain = null;
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			EnsureZhantanlin(Math.Max(1, annualYear));
			long actorId = ((BaseSystemData)actor.data).id;
			return TryGetOwnedNorthJinDi(actorId, out domain);
		}
		return TryGetForActor(actor, annualYear, out domain);
	}

	internal static bool IsDharmaFormFoundationStable(XjShiDomainRecord domain)
	{
		if (domain == null) return false;
		if (IsAbsorbedByZhantanlin(domain))
		{
			return XjZhantanlinSystem.IsPlaced
				&& string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal);
		}
		return string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal);
	}

	internal static bool TryAttractSameHeavenFragment(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (!TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord firstOwned))
			return TryEstablishTempleMasterJinDi(actor, annualYear);
		if (HasReformedHeaven(actorId, out _))
		{
			RefreshHeavenProjection(actor);
			return false;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiLastHeavenAttractionYear, out int lastYear);
		if (lastYear > 0 && annualYear - lastYear < XjShiCatalog.HeavenFragmentAttemptIntervalYears) return false;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiLastHeavenAttractionYear, annualYear);
		XjShiDomainRecord target = null;
		foreach (XjShiDomainRecord candidate in DomainsById.Values)
		{
			if (candidate == null || candidate.IsNorthWorldHonoredFragment <= 0
				|| !string.Equals(candidate.SourceHeavenId, firstOwned.SourceHeavenId, StringComparison.Ordinal)
				|| candidate.OwnerActorId > 0L) continue;
			if (target == null || candidate.SourceHeavenFragmentOrdinal < target.SourceHeavenFragmentOrdinal)
				target = candidate;
		}
		if (target == null) return false;
		float mingShu = XjShiMingShuSystem.GetEffectiveValue(actor, annualYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormStage, out string stage);
		int stageBonus = string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal) ? 2200
			: string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal) ? 1500
			: string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal) ? 800 : 0;
		int chance = XjShiCatalog.HeavenFragmentBaseChancePerTenThousand + stageBonus
			+ (int)Math.Floor(Math.Max(0f, mingShu - XjShiCatalog.DharmaFormMinimumMingShu) * 4f)
			+ Math.Max(0, firstOwned.Growth - XjShiCatalog.DharmaFormMinimumDomainGrowth);
		chance = Math.Clamp(chance, XjShiCatalog.HeavenFragmentBaseChancePerTenThousand,
			XjShiCatalog.HeavenFragmentMaximumChancePerTenThousand);
		if (!XjShiWorldHonoredPosturePolicy.IsGuaranteedWorldHonored(actor))
		{
			int roll = XjDeterministicHash.PositiveIndex(actorId + annualYear,
				"shi_same_heaven_fragment|" + firstOwned.SourceHeavenId, 10000);
			if (roll >= chance) return false;
		}
		target.OwnerActorId = actorId;
		target.HostMoHeId = 0L;
		target.Tradition = XjShiTraditionIds.Modern;
		target.DharmaFormPositionCapacity = 1;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		target.LineageId = lineageId ?? string.Empty;
		target.Growth = Math.Max(target.Growth, Math.Max(1, firstOwned.Growth / 2));
		target.LastGrowthYear = annualYear;
		SetVisibility(target, IsAbsorbedByZhantanlin(target)
			? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, annualYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
			XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor)
				? firstOwned.DomainId : XjShiDomainCatalog.ZhantanlinDomainId);
		RefreshHeavenProjection(actor);
		MarkChanged();
		_dirty = true;
		bool completed = HasReformedHeaven(actorId, out string heavenId);
		string eventText = "庙主以同源应身相感，牵引" + target.DisplayName + "归位。";
		if (completed && XjShiHeavenCatalog.TryParseHeavenId(heavenId, out int heavenIndex))
			eventText += "同系列金地至此聚全，重组为" + XjShiHeavenCatalog.GetHeavenDisplayName(heavenIndex) + "。";
		XjWorldHistoryStore.RecordActorEvent(actor, eventText, XjShiTraitIds.DharmaForm);
		string heavenName = completed && XjShiHeavenCatalog.TryParseHeavenId(heavenId, out int completedHeavenIndex)
			? XjShiHeavenCatalog.GetHeavenDisplayName(completedHeavenIndex) : string.Empty;
		XjShiAnnouncementSystem.OnHeavenFragment(actor, target.DisplayName, completed, heavenName);
		return true;
	}

	internal static bool TryGetForActor(Actor actor, int annualYear, out XjShiDomainRecord record)
	{
		record = null;
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		int year = Math.Max(1, annualYear);
		ReconcileFromActors(year);
		if (TryReadActorDomain(actor, out record)) return true;

		// 角色可能在本年度常规对账完成后才入释或更换承载地。只有首次读取
		// 确实未找到且状态已失效时，才允许一次强制收口；正常年度角色循环
		// 不会因此反复扫描全部释修。
		if (_dirty)
		{
			ReconcileFromActors(year, force: true);
			return TryReadActorDomain(actor, out record);
		}
		return false;
	}

	private static bool TryReadActorDomain(Actor actor, out XjShiDomainRecord record)
	{
		record = null;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		if (string.IsNullOrWhiteSpace(domainId))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out domainId);
		}
		return TryGet(domainId, out record);
	}

	internal static void AddContribution(Actor actor, int amount, int annualYear)
	{
		if (actor?.data == null || amount <= 0 || !XjCultivationPathRules.IsShi(actor)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		int scaled = XjShiLineagePolicy.ScaleDomainContribution(lineageId, amount);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiDomainContribution, out float pending);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiDomainContribution, Math.Max(0f, pending) + scaled);
		int year = Math.Max(1, annualYear);
		ReconcileFromActors(year);
		if (!TryResolveContributionDomain(actor, out XjShiDomainRecord domain) && _dirty)
		{
			// 新入释或同年改挂靠只在解析失败时触发一次强制收口；已经拥有合法
			// 承载地的绝大多数年度贡献不再重复执行全释修对账。
			ReconcileFromActors(year, force: true);
			TryResolveContributionDomain(actor, out domain);
		}
		if (domain == null) return;
		ApplyPendingContribution(actor, domain, year);
	}


	private static bool TryResolveContributionDomain(Actor actor, out XjShiDomainRecord domain)
	{
		domain = null;
		if (actor?.data == null) return false;
		ReadIdentity(actor, out string tradition, out string lineageId);

		// 今释“肉身所在”和“法相根基”是两套概念：庙主肉身绑定旃檀林，
		// 但度化/摄生所得承载增长必须优先流入本人所掌北世尊金地。该金地
		// 即使已经并入旃檀林（Absorbed）也仍然拥有独立权属和成长。
		if (TryGetPreferredContributionDomain(actor, tradition, lineageId, out domain)) return true;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPatronActorId, out string rawPatron);
		if (XjShiWorldRegistry.TryResolveLiveActor(rawPatron, out Actor patron)
			&& TryGetPreferredContributionDomain(patron, tradition, lineageId, out domain))
			return true;

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiMasterActorId, out string rawMaster);
		if (XjShiWorldRegistry.TryResolveLiveActor(rawMaster, out Actor master)
			&& TryGetPreferredContributionDomain(master, tradition, lineageId, out domain))
			return true;
		string preferredType = string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			? XjShiDomainTypeIds.YingTu : XjShiDomainTypeIds.JinDi;
		foreach (XjShiDomainRecord candidate in DomainsById.Values)
		{
			if (candidate == null || !string.Equals(candidate.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
				|| !string.Equals(candidate.Tradition, tradition, StringComparison.Ordinal)
				|| !string.Equals(candidate.LineageId, lineageId, StringComparison.Ordinal)) continue;
			bool candidatePreferred = string.Equals(candidate.DomainType, preferredType, StringComparison.Ordinal);
			bool currentPreferred = domain != null
				&& string.Equals(domain.DomainType, preferredType, StringComparison.Ordinal);
			if (domain == null || candidatePreferred && !currentPreferred
				|| candidatePreferred == currentPreferred && (candidate.Growth > domain.Growth
					|| candidate.Growth == domain.Growth
						&& string.Compare(candidate.DomainId, domain.DomainId, StringComparison.Ordinal) < 0))
			{
				domain = candidate;
			}
		}
		return domain != null;
	}

	internal static bool TryAbsorbJinDi(Actor actor, int annualYear, out string absorbedDomainId)
	{
		absorbedDomainId = string.Empty;
		if (actor?.data == null || annualYear <= 0 || !actor.isAlive()
			|| !TryGetForActor(actor, annualYear, out XjShiDomainRecord absorber)
			|| (!string.Equals(absorber.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
				&& !string.Equals(absorber.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			|| !string.Equals(absorber.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		if (absorber.OwnerActorId != actorId) return false;
		XjShiDomainRecord selected = null;
		foreach (XjShiDomainRecord candidate in DomainsById.Values)
		{
			if (candidate == null || candidate.IsNorthWorldHonoredFragment > 0
				|| !string.Equals(candidate.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(candidate.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal)
				|| !string.Equals(candidate.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)
				|| IsLiveShiActor(candidate.OwnerActorId, XjShiRealmIds.MoHe)
				|| IsLiveShiActor(candidate.HostMoHeId, XjShiRealmIds.MoHe)
				|| candidate.OccupiedMoHePositions > 0 || candidate.OccupiedLianMinPositions > 0
				|| candidate.SuccessionCandidateCount > 0) continue;
			if (selected == null || candidate.CreatedYear < selected.CreatedYear
				|| candidate.CreatedYear == selected.CreatedYear
					&& string.Compare(candidate.DomainId, selected.DomainId, StringComparison.Ordinal) < 0)
			{
				selected = candidate;
			}
		}
		if (selected == null) return false;
		selected.OwnerActorId = 0L;
		selected.HostMoHeId = 0L;
		selected.AbsorbedByDomainId = absorber.DomainId;
		selected.AbsorbedYear = Math.Max(1, annualYear);
		selected.LastAbsorptionYear = Math.Max(1, annualYear);
		selected.MoHePositionCapacity = 0;
		selected.LianMinPositionCapacity = 0;
		SetVisibility(selected, XjShiDomainVisibilityIds.Absorbed, annualYear);
		absorber.Growth = Math.Max(0, absorber.Growth + XjShiCatalog.JinDiAbsorptionGrowth);
		absorber.AbsorbedJinDiCount = Math.Max(0, absorber.AbsorbedJinDiCount + 1);
		absorber.LastAbsorptionYear = Math.Max(1, annualYear);
		absorber.LastGrowthYear = Math.Max(absorber.LastGrowthYear, Math.Max(1, annualYear));
		absorbedDomainId = selected.DomainId;
		MarkChanged();
		_dirty = true;
		return true;
	}


	/// <summary>
	/// 只读摩诃位序占用快照。轮回预留只存在于旃檀林108位；法相/世尊活体与其
	/// 死后真灵都不计入摩诃位。供人物详情与审计使用，不改变任何承位结果。
	/// </summary>
	internal static bool TryGetMoHePositionUsage(string domainId, out int occupied, out int reserved, out int capacity)
	{
		occupied = 0;
		reserved = 0;
		capacity = 0;
		if (string.IsNullOrWhiteSpace(domainId) || !TryGet(domainId, out XjShiDomainRecord domain) || domain == null) return false;
		occupied = Math.Max(0, CountLiveRealmInDomain(domain.DomainId, XjShiRealmIds.MoHe));
		capacity = Math.Max(1, ResolveMoHeCapacity(domain));
		if (string.Equals(domain.DomainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
		{
			capacity = 108;
			reserved = Math.Max(0, XjReincarnation.PendingModernMoHePositionReservationCount);
		}
		return true;
	}

	internal static bool IsDomainAvailableForMoHeClaim(string domainId, long claimantActorId)
	{
		if (claimantActorId <= 0L || !TryGet(domainId, out XjShiDomainRecord domain)) return false;
		if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return false;
		if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)
			&& !IsActorBoundToDomain(claimantActorId, domain.DomainId)) return false;
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{
			// 古释金地是自身所证应身。原主人死后可以成为寺中悟道遗泽，但不能被
			// 今释通过普通“占一块无主金地”事务直接改写成自己的摩诃位。
			if (string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				|| string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal))
				return false;
			return domain.OwnerActorId <= 0L || domain.OwnerActorId == claimantActorId
				|| !IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.MoHe);
		}

		int occupied = CountLiveRealmInDomain(domain.DomainId, XjShiRealmIds.MoHe);
		int capacity = Math.Max(1, ResolveMoHeCapacity(domain));
		// 已在该土的普通怜愍或轮回恢复者仍须占用真实摩诃位，不能仅凭“已绑定”绕过容量。
		// 只有角色本身已经是该土摩诃时，重复对账才视为原位保持。
		if (IsLiveActorInDomainAtRealm(claimantActorId, domain.DomainId, XjShiRealmIds.MoHe)) return true;

		// 0.9.8.8：旃檀林108位必须把“正在轮回、尚未重塑肉身”的今释摩诃
		// 也视为已预占。否则旧身死亡即释放名额，20~60年等待期间新摩诃可把
		// 名额吃满，原摩诃随后每年都在 TryClaimMoHePosition 处失败。
		int reserved = string.Equals(domain.DomainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)
			? XjReincarnation.PendingModernMoHePositionReservationCount : 0;
		return occupied + reserved < capacity;
	}

	internal static bool TryClaimMoHePosition(string domainId, long claimantActorId, int annualYear)
	{
		return TryClaimMoHePositionCore(domainId, claimantActorId, annualYear, false, 0L);
	}

	/// <summary>
	/// 同一真灵轮回归返的摩诃原位恢复。新档中普通承位会被 reservation 提前挡住；
	/// 旧档可能已经在 reservation 机制加入前被新摩诃塞满，因此这里对“确有待归返
	/// 记录”的旧摩诃给予一次迁移优先权，允许暂时超出108位。之后普通新晋全部被
	/// 容量门禁阻断，人数会随自然离世逐步回落，而不会让旧人物永久卡在轮回态。
	/// </summary>
	internal static bool TryClaimReservedMoHePosition(
		string domainId, long claimantActorId, long sourceActorId, int annualYear)
	{
		bool hasReservation = sourceActorId > 0L
			&& XjReincarnation.HasPendingModernMoHePositionReservation(sourceActorId)
			&& string.Equals(domainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal);
		return TryClaimMoHePositionCore(domainId, claimantActorId, annualYear, hasReservation, sourceActorId);
	}

	private static bool TryClaimMoHePositionCore(
		string domainId, long claimantActorId, int annualYear, bool reservedReturn, long sourceActorId)
	{
		_ = sourceActorId;
		if (claimantActorId <= 0L || !TryGet(domainId, out XjShiDomainRecord domain)) return false;
		if (!reservedReturn && !IsDomainAvailableForMoHeClaim(domainId, claimantActorId)) return false;
		if (reservedReturn)
		{
			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return false;
			if (!string.Equals(domain.DomainId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal)) return false;
		}
		bool changed = false;
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{
			if (domain.OwnerActorId != claimantActorId)
			{
				domain.OwnerActorId = claimantActorId;
				changed = true;
			}
			if (domain.HostMoHeId != 0L)
			{
				domain.HostMoHeId = 0L;
				changed = true;
			}
		}
		else if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal))
		{
			// 旧档应土不再产生“主持”身份；字段仅为反序列化兼容并在对账时清零。
			if (domain.HostMoHeId != 0L)
			{
				domain.HostMoHeId = 0L;
				changed = true;
			}
		}
		else if (domain.HostMoHeId != 0L)
		{
			domain.HostMoHeId = 0L;
			changed = true;
		}

		if (XjActorRegistry.ResolveKnownOrWorld(claimantActorId, out Actor claimant)
			&& claimant?.data != null && claimant.isAlive())
		{
			BindActor(claimant, domain, isLiangLi: false);
		}
		changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, annualYear);
		// 承位事务调用方随后才把角色境界切换为摩诃。此处若立即重算，会以旧境界
		// 产生一个短暂错误占用数；统一标脏，由境界事务完成后在同年对账中重算。
		if (changed) MarkChanged();
		_dirty = true;
		return true;
	}


	/// <summary>
	/// 法相承位的唯一写入口。自然争位、旧档迁移与陆江仙补录都必须通过此事务，
	/// 统一校验承载地类型、现任主人和旃檀林锚点，避免多套所有权并存。
	/// </summary>
	internal static bool TryClaimDharmaFormPosition(string domainId, long claimantActorId, int annualYear)
	{
		if (claimantActorId <= 0L || !TryGet(domainId, out XjShiDomainRecord domain)
			|| !XjActorRegistry.ResolveKnownOrWorld(claimantActorId, out Actor claimant)
			|| claimant?.data == null || !claimant.isAlive() || !XjCultivationPathRules.IsShi(claimant)) return false;

		XjActorAccessor.TryGetString(claimant, XjActorDataKeys.ShiRealm, out string claimantRealm);
		XjActorAccessor.TryGetString(claimant, XjActorDataKeys.ShiTradition, out string tradition);
		bool ancient = string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
		bool modern = string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
		bool ancientDirectSelfProof = ancient
			&& string.Equals(claimantRealm, XjShiRealmIds.DharmaMaster, StringComparison.Ordinal)
			&& domain.OwnerActorId == claimantActorId
			&& domain.IsNorthWorldHonoredFragment <= 0
			&& (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal));
		if (XjShiCatalog.GetRank(claimantRealm) < XjShiCatalog.GetRank(XjShiRealmIds.MoHe)
			&& !ancientDirectSelfProof) return false;

		if (modern)
		{
			// 今释证法相必须掌握一块北世尊金地。金地可以位于旃檀林内，
			// “已并入旃檀林”只表示所在释土，不影响庙主权属与法相根基。
			bool modernJinDi = domain.IsNorthWorldHonoredFragment > 0
				&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				&& domain.OwnerActorId == claimantActorId;
			if (!modernJinDi || !XjZhantanlinSystem.IsPlaced
				|| !IsDharmaFormFoundationStable(domain)) return false;

			domain.HostMoHeId = 0L;
			domain.DharmaFormPositionCapacity = 1;
			domain.LastDharmaFormClaimYear = Math.Max(1, annualYear);
			domain.LegacyMigrationState = XjShiDomainMigrationIds.None;
			SetVisibility(domain, IsAbsorbedByZhantanlin(domain)
				? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, annualYear);

			// 金地是法相根基；今释肉身和日常承载统一归于旃檀林。仅释修命数深厚者
			// 将所掌金地改作真灵与轮回锚。
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(annualYear);
			BindActor(claimant, zhantanlin, isLiangLi: false);
			XjActorAccessor.SetString(claimant, XjActorDataKeys.ShiRebirthAnchorId,
				XjZhantanlinSystem.ShouldPreferOwnedJinDi(claimant) ? domain.DomainId : zhantanlin.DomainId);
			RefreshHeavenProjection(claimant);
			MarkChanged();
			_dirty = true;
			return true;
		}

		bool ancientDomain = ancient && !string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
			&& (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal));
		if (!ancientDomain) return false;
		if (domain.OwnerActorId > 0L && domain.OwnerActorId != claimantActorId
			&& IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm)) return false;
		domain.OwnerActorId = claimantActorId;
		domain.HostMoHeId = 0L;
		domain.DharmaFormPositionCapacity = 1;
		domain.LastDharmaFormClaimYear = Math.Max(1, annualYear);
		domain.LegacyMigrationState = XjShiDomainMigrationIds.None;
		BindActor(claimant, domain, isLiangLi: false);
		XjActorAccessor.SetString(claimant, XjActorDataKeys.ShiRebirthAnchorId, domain.DomainId);
		SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, annualYear);
		MarkChanged();
		_dirty = true;
		return true;
	}

	internal static bool TryBeginDharmaFormAttempt(string domainId, long candidateActorId, int annualYear)
	{
		if (candidateActorId <= 0L || annualYear <= 0 || !TryGet(domainId, out XjShiDomainRecord domain))
			return false;
		// 每块庙主金地独立记录法相尝试冷却；旃檀林本体不再作为法相根基参与此事务。
		if (domain.LastDharmaFormAttemptYear > 0
			&& annualYear - domain.LastDharmaFormAttemptYear < XjShiCatalog.DharmaFormAttemptIntervalYears) return false;
		domain.LastDharmaFormAttemptYear = annualYear;
		MarkChanged();
		return true;
	}

	internal static bool TryBeginWorldHonoredAttempt(string domainId, long candidateActorId, int annualYear)
	{
		if (candidateActorId <= 0L || annualYear <= 0 || !TryGet(domainId, out XjShiDomainRecord domain))
			return false;
		// 世尊尝试绑定法相自身金地根基；旃檀林本体不承担共享尝试冷却。
		if (domain.LastWorldHonoredAttemptYear > 0
			&& annualYear - domain.LastWorldHonoredAttemptYear < XjShiCatalog.WorldHonoredAttemptIntervalYears)
			return false;
		domain.LastWorldHonoredAttemptYear = annualYear;
		MarkChanged();
		return true;
	}

	internal static bool ApplyAncientLegacyEventState(string domainId, int annualYear, string eventId,
		int manifestYears, bool responseAwakened, long discovererActorId)
	{
		if (!TryGet(domainId, out XjShiDomainRecord domain) || domain == null
			|| !string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| !string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			|| !string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal)
			|| !XjAncientShiLegacyEventIds.IsKnown(eventId)
			|| string.Equals(eventId, XjAncientShiLegacyEventIds.None, StringComparison.Ordinal)) return false;

		int year = Math.Max(1, annualYear);
		bool changed = false;
		if (domain.AncientLegacySinceYear <= 0)
		{ domain.AncientLegacySinceYear = year; changed = true; }
		if (domain.AncientLegacyLastEventYear != year)
		{ domain.AncientLegacyLastEventYear = year; changed = true; }
		if (!string.Equals(domain.AncientLegacyLastEventId, eventId, StringComparison.Ordinal))
		{ domain.AncientLegacyLastEventId = eventId; changed = true; }
		domain.AncientLegacyEventCount = Math.Max(0, domain.AncientLegacyEventCount) + 1;
		changed = true;
		long discoverer = Math.Max(0L, discovererActorId);
		if (domain.AncientLegacyLastDiscovererActorId != discoverer)
		{ domain.AncientLegacyLastDiscovererActorId = discoverer; changed = true; }

		if (responseAwakened)
		{
			if (domain.AncientLegacyResponseAwakened <= 0)
			{ domain.AncientLegacyResponseAwakened = 1; changed = true; }
			if (domain.AncientLegacyResponseAwakenedYear <= 0)
			{ domain.AncientLegacyResponseAwakenedYear = year; changed = true; }
			if (domain.AncientLegacyManifestUntilYear != 0)
			{ domain.AncientLegacyManifestUntilYear = 0; changed = true; }
			changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
		}
		else
		{
			int until = year + Math.Max(1, manifestYears);
			if (domain.AncientLegacyManifestUntilYear < until)
			{ domain.AncientLegacyManifestUntilYear = until; changed = true; }
			changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
		}
		if (changed)
		{
			MarkChanged();
			_dirty = true;
		}
		return changed;
	}

	internal static void AddHighRealmGrowth(string domainId, int amount, int annualYear)
	{
		if (amount <= 0 || !TryGet(domainId, out XjShiDomainRecord domain)) return;
		domain.Growth = Math.Max(0, domain.Growth + amount);
		domain.LastGrowthYear = Math.Max(domain.LastGrowthYear, Math.Max(1, annualYear));
		MarkChanged();
		_dirty = true;
	}

	internal static void ApplyHighRealmSetback(string domainId, int growthLoss, int annualYear)
	{
		if (growthLoss <= 0 || !TryGet(domainId, out XjShiDomainRecord domain)) return;
		domain.Growth = Math.Max(0, domain.Growth - growthLoss);
		domain.LastGrowthYear = Math.Max(domain.LastGrowthYear, Math.Max(1, annualYear));
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
			&& domain.HostMoHeId <= 0L)
			SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, annualYear);
		MarkChanged();
		_dirty = true;
	}

	internal static bool HasLiveDharmaFormOwner(string domainId)
	{
		return TryGet(domainId, out XjShiDomainRecord domain)
			&& !string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal)
			&& domain.OwnerActorId > 0L
			&& IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm);
	}



	internal static void MarkActorDeath(long actorId, string domainId, bool reincarnationQueued, int year)
	{
		if (actorId <= 0L) return;
		bool changed = false;
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null) continue;
			bool hostDied = domain.HostMoHeId == actorId;
			bool ownerDied = domain.OwnerActorId == actorId;
			if (!hostDied && !ownerDied) continue;

			if (hostDied)
			{
				domain.HostMoHeId = 0L;
				changed = true;
			}

			if (ownerDied && domain.IsNorthWorldHonoredFragment > 0)
			{
				bool insideZhantanlin = IsAbsorbedByZhantanlin(domain);
				if (reincarnationQueued)
				{
					// 庙主真灵已返回挂靠金地：保留全部同源碎片。旃檀林内金地仍是
					// “已并入释土”，外部金地才进入不稳定等待。
					changed |= SetVisibility(domain, insideZhantanlin
						? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Unstable, year);
				}
				else
				{
					// 真灵俱灭才释放北世尊应身碎片；旃檀林内碎片只失去庙主，
					// 不会因此从释土中脱落。
					domain.OwnerActorId = 0L;
					domain.HostMoHeId = 0L;
					domain.LineageId = string.Empty;
					domain.LegacyMigrationState = XjShiDomainMigrationIds.None;
					changed |= SetVisibility(domain, insideZhantanlin
						? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Hidden, year);
					changed = true;
				}
				continue;
			}

			if (ownerDied && (string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal)))
			{
				// 法相死亡不删除承载地；保留主人ID作为真灵复归与承接锚点。
				domain.LegacyMigrationState = XjShiDomainMigrationIds.LegacyOwnerMissing;
				changed = true;
			}
			if (ownerDied
				&& domain.IsNorthWorldHonoredFragment <= 0
				&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				&& string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				// 古释不走今释轮回。肉身寿尽或被斩后，自证金地作为“应身遗泽”留下：
				// 有寺庙则登记为同寺后来者的悟道遗泽；无寺庙则仍保留在天地间，
				// 先不稳定、后隐世。绝不直接把所有权送给今释。
				domain.LegacyMigrationState = XjShiDomainMigrationIds.AncientLegacyJinDi;
				domain.HostMoHeId = 0L;
				domain.MoHePositionCapacity = 0;
				domain.LianMinPositionCapacity = 0;
				domain.DharmaFormPositionCapacity = 0;
				if (domain.AncientLegacySinceYear <= 0) domain.AncientLegacySinceYear = Math.Max(1, year);
				if (string.IsNullOrWhiteSpace(domain.AncientLegacyFormerOwnerName)
					&& XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor formerOwner) && formerOwner?.data != null)
					domain.AncientLegacyFormerOwnerName = formerOwner.getName() ?? string.Empty;
				if (XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor originOwner)
					&& originOwner?.data != null && originOwner.current_tile != null)
				{
					var pos = originOwner.current_tile.pos;
					domain.MapCenterX = pos.x;
					domain.MapCenterY = pos.y;
					domain.MapRadius = Math.Max(6, domain.MapRadius);
				}
				XjAncientShiTempleSystem.NotifyAncientJinDiLegacy(actorId, domain.DomainId, year);
				changed = true;
			}

			if (hostDied || ownerDied)
			{
				// 显世不会因一次死亡当年直接坍塌；统一进入权属空缺宽限。
				changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, year);
			}
		}
		if (changed) MarkChanged();
		_dirty = true;
	}

	internal static string EnsureLegacyRebirthDomain(Actor actor, string domainId, string tradition,
		string lineageId, int annualYear)
	{
		if (actor?.data == null) return string.Empty;
		long actorId = ((BaseSystemData)actor.data).id;
		string domainType = string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			? XjShiDomainTypeIds.JinDi
			: XjShiDomainTypeIds.YingTu;
		string resolvedId = NormalizeId(domainId);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& string.Equals(resolvedId, XjShiDomainCatalog.ZhantanlinDomainId, StringComparison.Ordinal))
		{
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(annualYear);
			BindActor(actor, zhantanlin, isLiangLi: false);
			_dirty = true;
			return zhantanlin.DomainId;
		}
		if (resolvedId.Length > 0 && TryGet(resolvedId, out XjShiDomainRecord archived))
		{
			if (string.Equals(archived.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
				domainType = XjShiDomainTypeIds.BaoTu;
			else if (archived.IsNorthWorldHonoredFragment > 0
				&& string.Equals(archived.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
				domainType = XjShiDomainTypeIds.JinDi;
		}
		if (resolvedId.Length == 0)
		{
			resolvedId = string.Equals(domainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				? BuildJinDiId(actorId)
				: BuildYingTuId(0L, actorId);
		}
		bool existed = TryGet(resolvedId, out _);
		XjShiDomainRecord domain = EnsureDomain(resolvedId, domainType, tradition, lineageId, annualYear);
		if (string.Equals(domainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{
			if (domain.OwnerActorId <= 0L) domain.OwnerActorId = actorId;
			if (domain.IsNorthWorldHonoredFragment > 0)
			{
				domain.Tradition = XjShiTraditionIds.Modern;
				domain.LegacyMigrationState = XjShiDomainMigrationIds.None;
				SetVisibility(domain, IsAbsorbedByZhantanlin(domain)
					? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, annualYear);
			}
			else
			{
				if (!existed || string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.None, StringComparison.Ordinal))
					domain.LegacyMigrationState = XjShiDomainMigrationIds.PendingAncientJinDi;
				SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, annualYear);
			}
		}
		else if (string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
		{
			if (!IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm))
				domain.LegacyMigrationState = XjShiDomainMigrationIds.LegacyOwnerMissing;
			if (!IsLiveShiActorAtRealm(domain.HostMoHeId, XjShiRealmIds.MoHe))
				SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, annualYear);
		}
		else
		{
			bool ownerLive = IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm);
			bool hostLive = IsLiveShiActorAtRealm(domain.HostMoHeId, XjShiRealmIds.MoHe);
			if (!ownerLive && string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.None, StringComparison.Ordinal))
				domain.LegacyMigrationState = XjShiDomainMigrationIds.LegacyOwnerMissing;
			// 怜愍转世仍依附一处正常显世应土时，不得把整座应土降为不稳定。
			if (!hostLive) SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, annualYear);
		}
		if (domain.IsNorthWorldHonoredFragment > 0
			&& string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(annualYear);
			BindActor(actor, zhantanlin, isLiangLi: false);
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
				XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor) ? domain.DomainId : zhantanlin.DomainId);
		}
		else
		{
			BindActor(actor, domain, isLiangLi: false);
		}
		RefreshHeavenProjection(actor);
		MarkChanged();
		_dirty = true;
		return domain.DomainId;
	}

	internal static XjShiDomainRecord EnsureModernDharmaFormDomain(
		Actor actor, XjShiDomainRecord sourceDomain, int annualYear)
	{
		if (actor?.data == null) return null;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return null;
		ReadIdentity(actor, out string tradition, out _);
		if (!string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)) return null;
		int year = Math.Max(1, annualYear);
		EnsureZhantanlin(year);

		// 高境投释、洞天遗泽和旧档法相修复必须补齐一块真实北世尊金地；
		// 不再允许以旃檀林整体替代金地门槛，也不伪造个人应土。
		if (!TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord owned)
			&& !TryForceEstablishTempleMasterJinDi(actor, year, announce: false)) return null;
		if (!TryGetOwnedNorthJinDi(actorId, out owned)) return null;
		if (sourceDomain != null)
		{
			int inheritedGrowth = Math.Min(Math.Max(0, sourceDomain.Growth),
				XjShiCatalog.DharmaFormMinimumDomainGrowth);
			if (owned.Growth < inheritedGrowth) owned.Growth = inheritedGrowth;
		}
		owned.HostMoHeId = 0L;
		owned.DharmaFormPositionCapacity = 1;
		SetVisibility(owned, IsAbsorbedByZhantanlin(owned)
			? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, year);
		XjShiDomainRecord zhantanlin = EnsureZhantanlin(year);
		BindActor(actor, zhantanlin, isLiangLi: false);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
			XjZhantanlinSystem.ShouldPreferOwnedJinDi(actor) ? owned.DomainId : zhantanlin.DomainId);
		RefreshHeavenProjection(actor);
		return owned;
	}


	internal static void EnsureAncientSelfProvedJinDi(Actor actor, int annualYear)
	{
		if (actor?.data == null) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		if (!string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		string domainId = BuildJinDiId(actorId);
		bool existed = TryGet(domainId, out XjShiDomainRecord domain);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion, out int migrationVersion);
		bool needsGrowthBaseline = !existed || migrationVersion < CurrentMigrationVersion;
		domain ??= EnsureDomain(domainId, XjShiDomainTypeIds.JinDi,
			XjShiTraditionIds.Ancient, lineageId, annualYear);
		bool changed = !existed;
		if (needsGrowthBaseline && domain.Growth < XjShiCatalog.DharmaFormMinimumDomainGrowth)
		{
			domain.Growth = XjShiCatalog.DharmaFormMinimumDomainGrowth;
			domain.LastGrowthYear = Math.Max(domain.LastGrowthYear, Math.Max(1, annualYear));
			changed = true;
		}
		if (!string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{ domain.DomainType = XjShiDomainTypeIds.JinDi; changed = true; }
		if (!string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{ domain.Tradition = XjShiTraditionIds.Ancient; changed = true; }
		if (!string.Equals(domain.LineageId, lineageId, StringComparison.Ordinal))
		{ domain.LineageId = lineageId; changed = true; }
		if (domain.OwnerActorId != actorId) { domain.OwnerActorId = actorId; changed = true; }
		if (domain.HostMoHeId != 0L) { domain.HostMoHeId = 0L; changed = true; }
		if (!string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.None, StringComparison.Ordinal))
		{ domain.LegacyMigrationState = XjShiDomainMigrationIds.None; changed = true; }
		if (domain.AncientLegacySinceYear != 0) { domain.AncientLegacySinceYear = 0; changed = true; }
		if (domain.AncientLegacyLastEventYear != 0) { domain.AncientLegacyLastEventYear = 0; changed = true; }
		if (domain.AncientLegacyEventCount != 0) { domain.AncientLegacyEventCount = 0; changed = true; }
		if (domain.AncientLegacyManifestUntilYear != 0) { domain.AncientLegacyManifestUntilYear = 0; changed = true; }
		if (domain.AncientLegacyResponseAwakened != 0) { domain.AncientLegacyResponseAwakened = 0; changed = true; }
		if (domain.AncientLegacyResponseAwakenedYear != 0) { domain.AncientLegacyResponseAwakenedYear = 0; changed = true; }
		if (domain.AncientLegacyLastDiscovererActorId != 0L) { domain.AncientLegacyLastDiscovererActorId = 0L; changed = true; }
		if (!string.Equals(domain.AncientLegacyLastEventId, XjAncientShiLegacyEventIds.None, StringComparison.Ordinal))
		{ domain.AncientLegacyLastEventId = XjAncientShiLegacyEventIds.None; changed = true; }
		string actorName = actor.getName() ?? string.Empty;
		if (!string.Equals(domain.AncientLegacyFormerOwnerName, actorName, StringComparison.Ordinal))
		{ domain.AncientLegacyFormerOwnerName = actorName; changed = true; }
		if (actor.current_tile != null)
		{
			var pos = actor.current_tile.pos;
			if (domain.MapCenterX != pos.x || domain.MapCenterY != pos.y)
			{ domain.MapCenterX = pos.x; domain.MapCenterY = pos.y; changed = true; }
			if (domain.MapRadius < 6) { domain.MapRadius = 6; changed = true; }
		}
		changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, annualYear);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string previousDomainId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthAnchorId, out string previousAnchorId);
		if (!string.Equals(previousDomainId, domain.DomainId, StringComparison.Ordinal)
			|| !string.Equals(previousAnchorId, domain.DomainId, StringComparison.Ordinal)) changed = true;
		BindActor(actor, domain, isLiangLi: false);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId, domain.DomainId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinFirstEntry, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinNextReturnYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiZhantanlinReturnUntilYear, 0);
		if (changed)
		{
			MarkChanged();
			_dirty = true;
		}
	}

	internal static void ReconcileFromActors(int annualYear, bool force = false)
	{
		int year = Math.Max(1, annualYear);
		// 常规读入口同一世界年最多执行一次完整释土对账。角色事务若确实需要
		// 立即收口，必须显式 force，并且只有状态已失效时才允许再次执行。
		if (_lastReconciledYear == year && (!force || !_dirty)) return;
		_lastReconciledYear = year;
		_dirty = false;

		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		List<long> moHeActorIds = MoHeActorIdScratch;
		List<long> dharmaFormActorIds = DharmaFormActorIdScratch;
		List<long> lianMinActorIds = LianMinActorIdScratch;
		moHeActorIds.Clear();
		dharmaFormActorIds.Clear();
		lianMinActorIds.Clear();
		bool hasModernDharmaForm = false;
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string middleTradition);
				// 古释不存在怜愍、摩诃。旧档非法状态不进入今释承载对账，交由人物一致性修复。
				if (string.Equals(middleTradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)) continue;
				if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)) lianMinActorIds.Add(actorId);
				else moHeActorIds.Add(actorId);
			}
			else if (string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			{
				dharmaFormActorIds.Add(actorId);
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string highTradition);
				if (string.Equals(highTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
					hasModernDharmaForm = true;
			}
		}
		moHeActorIds.Sort();
		dharmaFormActorIds.Sort();
		lianMinActorIds.Sort();

		bool changed = NormalizeAuthorityLiveness(year);
		if (hasModernDharmaForm)
		{
			XjShiDomainRecord anchor = EnsureZhantanlin(year);
			if (anchor.MapRadius < XjZhantanlinSystem.MinimumRadius)
				changed |= SetVisibility(anchor, XjShiDomainVisibilityIds.Hidden, year);
		}

		// 法相先确立承载关系：古释以内证金地立身；今释必须掌握一块
		// 北世尊金地成为庙主。金地可位于旃檀林内，但肉身仍统一归林。
		for (int i = 0; i < dharmaFormActorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(dharmaFormActorIds[i], out Actor owner)
				|| owner?.data == null || !owner.isAlive()) continue;
			long ownerId = ((BaseSystemData)owner.data).id;
			ReadIdentity(owner, out string tradition, out string lineageId);
			bool ancient = string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal);
			if (!ancient)
			{
				bool ownsJinDi = TryGetOwnedNorthJinDi(ownerId, out XjShiDomainRecord ownedJinDi);
				if (!ownsJinDi)
				{
					// 0.9.9.8以前无金地的今释法相在旧档对账时补齐一块真实金地；
					// 优先取旃檀林内无主碎片，不夺取任何既有庙主权属。
					TryForceEstablishTempleMasterJinDi(owner, year, announce: false);
					ownsJinDi = TryGetOwnedNorthJinDi(ownerId, out ownedJinDi);
				}
				XjShiDomainRecord zhantanlin = EnsureZhantanlin(year);
				if (XjZhantanlinSystem.IsPlaced)
					changed |= SetVisibility(zhantanlin, XjShiDomainVisibilityIds.Manifest, year);
				BindActor(owner, zhantanlin, isLiangLi: false);
				if (ownsJinDi)
				{
					if (ownedJinDi.OwnerActorId != ownerId) { ownedJinDi.OwnerActorId = ownerId; changed = true; }
					if (ownedJinDi.HostMoHeId != 0L) { ownedJinDi.HostMoHeId = 0L; changed = true; }
					if (ownedJinDi.DharmaFormPositionCapacity != 1)
					{ ownedJinDi.DharmaFormPositionCapacity = 1; changed = true; }
					changed |= SetVisibility(ownedJinDi, IsAbsorbedByZhantanlin(ownedJinDi)
						? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, year);
					if (!string.Equals(ownedJinDi.LineageId, lineageId, StringComparison.Ordinal))
					{ ownedJinDi.LineageId = lineageId; changed = true; }
					XjActorAccessor.SetString(owner, XjActorDataKeys.ShiRebirthAnchorId,
						XjZhantanlinSystem.ShouldPreferOwnedJinDi(owner)
							? ownedJinDi.DomainId : zhantanlin.DomainId);
				}
				RefreshHeavenProjection(owner);
				continue;
			}

			// 古释法相仍以自身为金地，不转换成外置宝土，也不与旃檀林建立真身锚。
			EnsureAncientSelfProvedJinDi(owner, year);
			if (TryGetForActorWithoutReconcile(owner, out XjShiDomainRecord ancientDomain))
			{
				if (ancientDomain.DharmaFormPositionCapacity != 1)
				{ ancientDomain.DharmaFormPositionCapacity = 1; changed = true; }
				if (ancientDomain.HostMoHeId != 0L)
				{ ancientDomain.HostMoHeId = 0L; changed = true; }
			}
			RefreshHeavenProjection(owner);

		}

		for (int i = 0; i < moHeActorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(moHeActorIds[i], out Actor moHe)
				|| moHe?.data == null || !moHe.isAlive()) continue;
			long actorId = ((BaseSystemData)moHe.data).id;
			ReadIdentity(moHe, out string tradition, out string lineageId);
			if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
			{
				// 古释没有摩诃层。这里若读到古释摩诃，只把它视作旧档非法状态；
				// 不为其建立任何摩诃承载或金地关系，统一交由 XjShiState.EnsureConsistent 修复为合法古释境界。
				continue;
			}


			// 七世起今释摩诃可极难感得无主金地；得地者为庙主；仅释修命数深厚者改以自身金地为锚。
			TryEstablishTempleMasterJinDi(moHe, year);
			XjShiDomainRecord templeJinDi = null;
			if (TryGetOwnedNorthJinDi(actorId, out templeJinDi))
			{
				if (templeJinDi.HostMoHeId == actorId) { templeJinDi.HostMoHeId = 0L; changed = true; }
				bool insideZhantanlin = IsAbsorbedByZhantanlin(templeJinDi);
				changed |= SetVisibility(templeJinDi, insideZhantanlin
					? XjShiDomainVisibilityIds.Absorbed : XjShiDomainVisibilityIds.Manifest, year);
			}

			// 今释摩诃以旃檀林为归返本土。金地只作为庙主权属与法相根基；
			// 摩诃可在外行走并周期归林，释修命数深厚者可把个人金地作为真灵锚。
			XjShiDomainRecord zhantanlin = EnsureZhantanlin(year);
			if (zhantanlin.HostMoHeId != 0L) { zhantanlin.HostMoHeId = 0L; changed = true; }
			if (XjZhantanlinSystem.IsPlaced)
				changed |= SetVisibility(zhantanlin, XjShiDomainVisibilityIds.Manifest, year);
			BindActor(moHe, zhantanlin, isLiangLi: false);
			XjActorAccessor.SetString(moHe, XjActorDataKeys.ShiRebirthAnchorId,
				templeJinDi != null && XjZhantanlinSystem.ShouldPreferOwnedJinDi(moHe)
					? templeJinDi.DomainId : zhantanlin.DomainId);
			RefreshHeavenProjection(moHe);
		}

		// 当前模型不存在额外“主持摩诃”。所有旧制主持字段统一清零；
		// 法相根基只看古释自身金地或今释庙主金地；旃檀林只负责今释肉身承载。
		for (int i = 0; i < dharmaFormActorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(dharmaFormActorIds[i], out Actor owner)
				|| owner?.data == null || !owner.isAlive()) continue;
			if (!TryGetForActorWithoutReconcile(owner, out XjShiDomainRecord domain)) continue;
			if (domain.HostMoHeId != 0L) { domain.HostMoHeId = 0L; changed = true; }
			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal))
			{
				changed |= SetVisibility(domain, XjZhantanlinSystem.IsPlaced
					? XjShiDomainVisibilityIds.Manifest : XjShiDomainVisibilityIds.Hidden, year);
			}
			else if (domain.OwnerActorId > 0L)
			{
				changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
			}
		}


		// 清除普通摩诃残留的旧制代掌标记。
		for (int i = 0; i < moHeActorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(moHeActorIds[i], out Actor moHe)
				|| moHe?.data == null || !moHe.isAlive()
				|| !TryGetForActorWithoutReconcile(moHe, out XjShiDomainRecord domain)) continue;
			BindActor(moHe, domain, isLiangLi: false);
		}

		for (int i = 0; i < lianMinActorIds.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(lianMinActorIds[i], out Actor dependent)
				|| dependent?.data == null || !dependent.isAlive()) continue;
			XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiPatronActorId, out string rawPatron);
			if (XjShiWorldRegistry.TryResolveActorId(rawPatron, out long patronId)
				&& XjActorRegistry.ResolveKnownOrWorld(patronId, out Actor patron)
				&& patron?.data != null && patron.isAlive()
				&& TryGetForActorWithoutReconcile(patron, out XjShiDomainRecord patronDomain))
			{
				XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiRealm, out string patronRealm);
				if (string.Equals(patronRealm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
				{
					BindActor(dependent, patronDomain, isLiangLi: false);
					continue;
				}

				// 座主从摩诃晋法相/世尊仍是同一真灵，并未死亡。直属怜愍不能因此
				// 被旧迁移逻辑改成归土/孤位，更不能失去借力；只把承载地随座主
				// 的现有法相根基同步过去。新怜愍仍只从在世摩诃中择主。
				if (XjShiCatalog.GetRank(patronRealm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
				{
					BindActor(dependent, patronDomain, isLiangLi: false);
					XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiSeatId, out string inheritedSeatId);
					XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
					XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowedPower,
						XjShiWorldRegistry.ResolveBorrowedPower(inheritedSeatId, patron));
					XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus,
						XjShiPositionStatusIds.Attached);
					continue;
				}
			}

			// 旧档中座主已死但怜愍仍保留原金地ID时，先创建一处隐世的旧制应土，
			// 保住其座位、候证与轮回引用；不凭空补一位法相主人。
			XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiDomainId, out string legacyDomainId);
			if (string.IsNullOrWhiteSpace(legacyDomainId))
				XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiJinDiId, out legacyDomainId);
			legacyDomainId = NormalizeId(legacyDomainId);
			long dependentId = ((BaseSystemData)dependent.data).id;
			if (legacyDomainId.Length == 0) legacyDomainId = BuildYingTuId(0L, dependentId);
			ReadIdentity(dependent, out string tradition, out string lineageId);
			XjShiDomainRecord orphanDomain = EnsureDomain(legacyDomainId, XjShiDomainTypeIds.YingTu,
				tradition, lineageId, year);
			if (string.Equals(orphanDomain.LegacyMigrationState, XjShiDomainMigrationIds.None, StringComparison.Ordinal))
			{
				orphanDomain.LegacyMigrationState = XjShiDomainMigrationIds.LegacyOwnerMissing;
				changed = true;
			}
			changed |= SetVisibility(orphanDomain, XjShiDomainVisibilityIds.Hidden, year);
			BindActor(dependent, orphanDomain, isLiangLi: false);
			XjActorAccessor.SetInt(dependent, XjActorDataKeys.ShiBorrowedPower, 0);
			XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiSeatId, out string orphanSeatId);
			XjActorAccessor.SetString(dependent, XjActorDataKeys.ShiPositionStatus,
				XjShiCatalog.GetSeatRank(orphanSeatId) >= XjShiCatalog.GetSeatRank(XjShiSeatIds.FaHui)
					? XjShiPositionStatusIds.ReturnedToShiTu
					: XjShiPositionStatusIds.Orphaned);
		}

		changed |= RecalculateDerivedState(year);
		if (changed) MarkChanged();
	}


	private static bool NormalizeAuthorityLiveness(int year)
	{
		bool changed = false;
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null) continue;
			if (domain.HostMoHeId != 0L)
			{
				domain.HostMoHeId = 0L;
				changed = true;
			}

			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.Zhantanlin, StringComparison.Ordinal))
			{
				if (domain.OwnerActorId != 0L) { domain.OwnerActorId = 0L; changed = true; }
				changed |= SetVisibility(domain, XjZhantanlinSystem.IsPlaced
					? XjShiDomainVisibilityIds.Manifest : XjShiDomainVisibilityIds.Hidden, year);
				continue;
			}

			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal))
				continue;

			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
				|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			{
				bool ownerLive = IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.MoHe);
				if (!ownerLive
					&& domain.IsNorthWorldHonoredFragment <= 0
					&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
					&& string.Equals(domain.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
				{
					// 旧档中已经死亡的古释可能没有走到新版死亡事务；年度对账在这里
					// 补记为古释遗金地，至少保证不会再被今释普通金地逻辑吞并/占位。
					if (!string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal))
					{
						domain.LegacyMigrationState = XjShiDomainMigrationIds.AncientLegacyJinDi;
						domain.MoHePositionCapacity = 0;
						domain.LianMinPositionCapacity = 0;
						domain.DharmaFormPositionCapacity = 0;
						changed = true;
					}
					if (domain.AncientLegacySinceYear <= 0)
					{
						domain.AncientLegacySinceYear = Math.Max(1, year);
						changed = true;
					}
				}
				if (ownerLive) changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
				else changed |= AdvanceHostVacancy(domain, year);
				continue;
			}

			// 旧应土仅保留为存档迁移壳，不再借主持者维持显世。
			bool ownerLiveYingTu = IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm);
			if (ownerLiveYingTu) changed |= SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
			else changed |= AdvanceHostVacancy(domain, year);
		}
		return changed;
	}



	private static XjShiDomainRecord EnsureDomain(string domainId, string domainType,
		string tradition, string lineageId, int year)
	{
		string id = NormalizeId(domainId);
		if (!DomainsById.TryGetValue(id, out XjShiDomainRecord domain))
		{
			domain = new XjShiDomainRecord
			{
				DomainId = id,
				DisplayName = ResolveDomainDisplayName(id, domainType, lineageId),
				DomainType = domainType,
				Tradition = tradition ?? string.Empty,
				LineageId = lineageId ?? string.Empty,
				Visibility = XjShiDomainVisibilityIds.Hidden,
				MoHePositionCapacity = string.Equals(domainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal) ? 108 : 1,
				LianMinPositionBaseCapacity = string.Equals(domainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal) ? 108 : 1,
				DharmaFormPositionCapacity = string.Equals(domainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)
					? 0
					: string.Equals(domainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
						|| string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal) ? 1 : 0,
				CreatedYear = Math.Max(1, year),
				LegacyMigrationState = XjShiDomainMigrationIds.None
			};
			DomainsById[id] = domain;
			MarkChanged();
		}
		bool normalizedChanged = false;
		if (!XjShiDomainTypeIds.IsKnown(domain.DomainType)
			|| string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal)
				&& string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal))
		{
			domain.DomainType = domainType;
			domain.DharmaFormPositionCapacity = string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal) ? 1 : domain.DharmaFormPositionCapacity;
			normalizedChanged = true;
		}
		if (string.IsNullOrWhiteSpace(domain.Tradition) && !string.IsNullOrWhiteSpace(tradition))
		{
			domain.Tradition = tradition;
			normalizedChanged = true;
		}
		if (string.IsNullOrWhiteSpace(domain.LineageId) && !string.IsNullOrWhiteSpace(lineageId))
		{
			domain.LineageId = lineageId;
			normalizedChanged = true;
		}
		if (string.IsNullOrWhiteSpace(domain.DisplayName))
		{
			domain.DisplayName = ResolveDomainDisplayName(domain.DomainId, domain.DomainType, domain.LineageId);
			normalizedChanged = true;
		}
		if (normalizedChanged) MarkChanged();
		return domain;
	}

	private static bool TryGetPreferredContributionDomain(Actor anchor, string tradition, string lineageId,
		out XjShiDomainRecord domain)
	{
		domain = null;
		if (anchor?.data == null) return false;
		long anchorId = ((BaseSystemData)anchor.data).id;
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& anchorId > 0L
			&& TryGetOwnedNorthJinDi(anchorId, out XjShiDomainRecord owned)
			&& string.Equals(owned.Tradition, tradition, StringComparison.Ordinal)
			&& string.Equals(owned.LineageId, lineageId, StringComparison.Ordinal)
			&& (string.Equals(owned.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
				|| IsAbsorbedByZhantanlin(owned)))
		{
			domain = owned;
			return true;
		}
		if (TryGetForActorWithoutReconcile(anchor, out XjShiDomainRecord current)
			&& string.Equals(current.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
			&& string.Equals(current.Tradition, tradition, StringComparison.Ordinal)
			&& string.Equals(current.LineageId, lineageId, StringComparison.Ordinal))
		{
			domain = current;
			return true;
		}
		return false;
	}

	private static void BindActor(Actor actor, XjShiDomainRecord domain, bool isLiangLi)
	{
		if (actor?.data == null || domain == null) return;
		_ = isLiangLi; // 旧签名兼容；当前模型不再投影主持／量力身份。
		MigrateLegacyContribution(actor);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDomainId, domain.DomainId);
		// 双写旧字段只为0.9.6.7及更早存档、轮回载荷和UI兼容；其不再是权威来源。
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiId, domain.DomainId);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, ToLegacyStatus(domain.Visibility));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiIsMoHeLiangLi, 0);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, out int severedUntil);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed,
			string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
				&& severedUntil < Math.Max(1, _lastReconciledYear) ? 0 : 1);
		ApplyPendingContribution(actor, domain, Math.Max(1, _lastReconciledYear));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion, CurrentMigrationVersion);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			|| string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
				&& XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiRebirthAnchorId,
				XjZhantanlinSystem.ResolvePreferredAnchor(actor, domain.DomainId, Math.Max(1, _lastReconciledYear)));
		}
	}

	private static void MigrateLegacyContribution(Actor actor)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainMigrationVersion, out int version);
		// 贡献人数只在0.9.6.8以前迁移一次；版本3仅增加命数/吞并字段，不能重复追加。
		if (version >= 2) return;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiConvertedCount, out int converted);
		if (converted <= 0) return;
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiDomainContribution, out float pending);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiDomainContribution,
			Math.Max(0f, pending) + Math.Max(0, converted));
	}

	private static void ApplyPendingContribution(Actor actor, XjShiDomainRecord domain, int annualYear)
	{
		if (actor?.data == null || domain == null) return;
		// 显世与可成长彻底拆开：北世尊金地即使已并入旃檀林（Absorbed），
		// 只要仍有合法今释庙主且旃檀林存在，就继续承受本人/法脉的度化贡献。
		// Hidden/Unstable 仍只积存个人贡献，避免无主遗地在幕后无限增长。
		bool manifestGrowth = string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal);
		bool absorbedTempleGrowth = IsAbsorbedByZhantanlin(domain)
			&& string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
			&& string.Equals(domain.Tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal)
			&& domain.OwnerActorId > 0L
			&& IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.MoHe)
			&& XjZhantanlinSystem.IsPlaced;
		if (!manifestGrowth && !absorbedTempleGrowth) return;
		ReadIdentity(actor, out string contributionTradition, out string contributionLineageId);
		if (absorbedTempleGrowth
			&& (!string.Equals(contributionTradition, domain.Tradition, StringComparison.Ordinal)
				|| !string.Equals(contributionLineageId, domain.LineageId, StringComparison.Ordinal))) return;
		if ((string.Equals(domain.DomainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal)
			|| string.Equals(domain.DomainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
			&& (!IsLiveShiActor(domain.OwnerActorId, XjShiRealmIds.DharmaForm)
				|| string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.LegacyOwnerMissing, StringComparison.Ordinal)))
		{
			return;
		}
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiDomainContribution, out float pending);
		int applied = Math.Max(0, (int)Math.Floor(Math.Max(0f, pending)));
		if (applied <= 0) return;
		domain.Growth = Math.Max(0, domain.Growth + applied);
		domain.LastGrowthYear = Math.Max(domain.LastGrowthYear, Math.Max(1, annualYear));
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.ShiDomainContribution, Math.Max(0f, pending - applied));
		MarkChanged();
	}

	private static bool TryGetForActorWithoutReconcile(Actor actor, out XjShiDomainRecord domain)
	{
		domain = null;
		if (actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
		if (TryGet(domainId, out domain)) return true;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiJinDiId, out domainId);
		return TryGet(domainId, out domain);
	}

	private static string ToLegacyStatus(string visibility)
	{
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)) return XjShiJinDiStatusIds.Manifest;
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Unstable, StringComparison.Ordinal)) return XjShiJinDiStatusIds.WaitingForRebirth;
		return XjShiJinDiStatusIds.Hidden;
	}


	private static bool SetVisibility(XjShiDomainRecord domain, string visibility, int year)
	{
		if (domain == null || !XjShiDomainVisibilityIds.IsKnown(visibility)) return false;
		if (string.Equals(domain.Visibility, visibility, StringComparison.Ordinal)) return false;
		string previous = domain.Visibility ?? string.Empty;
		domain.Visibility = visibility;
		if (string.Equals(visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal))
		{
			domain.LastManifestYear = Math.Max(1, year);
			domain.UnstableSinceYear = 0;
		}
		else if (string.Equals(visibility, XjShiDomainVisibilityIds.Unstable, StringComparison.Ordinal))
		{
			if (domain.UnstableSinceYear <= 0) domain.UnstableSinceYear = Math.Max(1, year);
		}
		else if (string.Equals(visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal))
		{
			domain.LastHiddenYear = Math.Max(1, year);
			if (domain.UnstableSinceYear <= 0) domain.UnstableSinceYear = Math.Max(1, year);
		}
		else if (string.Equals(visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal))
		{
			domain.LastHiddenYear = Math.Max(1, year);
			domain.UnstableSinceYear = 0;
		}

		SyncVisibilityToBoundActors(domain, visibility, year,
			applyHiddenConsequence: string.Equals(visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)
				&& !string.Equals(previous, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal));
		return true;
	}




	internal static bool TryDebugToggleVisibility(Actor actor, int annualYear)
	{
		if (actor?.data == null || !TryGetForActor(actor, annualYear, out XjShiDomainRecord domain)
			|| string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return false;
		string target = string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal)
			? XjShiDomainVisibilityIds.Hidden : XjShiDomainVisibilityIds.Manifest;
		bool changed = SetVisibility(domain, target, Math.Max(1, annualYear));
		if (changed) { MarkChanged(); _dirty = true; }
		return changed;
	}

	internal static IReadOnlyList<XjShiDomainRecord> ReadSnapshot()
	{
		int year = Math.Max(1, XjYearTracker.CurrentYear > 0
			? XjYearTracker.CurrentYear : World.world?.map_stats?.year ?? 1);
		return ReadSnapshot(year);
	}

	internal static IReadOnlyList<XjShiDomainRecord> ReadSnapshot(int annualYear)
	{
		// UI读取不是世界重建，但如果手动赋予刚刚修改了释修身份，允许在同年
		// 对已标脏的限定释修索引立即对账一次，保证仙录展示真实现状。
		ReconcileFromActors(Math.Max(1, annualYear), force: _dirty);
		List<XjShiDomainRecord> result = new List<XjShiDomainRecord>();
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain != null) result.Add(domain.Clone());
		}
		result.Sort((left, right) => string.Compare(left.DomainId, right.DomainId, StringComparison.Ordinal));
		return result;
	}

	internal static bool IsManifest(string domainId)
	{
		return TryGet(domainId, out XjShiDomainRecord domain)
			&& string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal);
	}

	internal static bool IsBorrowingActive(Actor dependent, Actor patron, int annualYear)
	{
		if (dependent?.data == null || patron?.data == null || !dependent.isAlive() || !patron.isAlive()) return false;
		XjActorAccessor.TryGetString(dependent, XjActorDataKeys.ShiDomainId, out string dependentDomain);
		XjActorAccessor.TryGetString(patron, XjActorDataKeys.ShiDomainId, out string patronDomain);
		if (string.IsNullOrWhiteSpace(dependentDomain)
			|| !string.Equals(dependentDomain, patronDomain, StringComparison.Ordinal)
			|| !IsManifest(dependentDomain)) return false;
		XjActorAccessor.TryGetInt(dependent, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, out int severedUntil);
		XjActorAccessor.TryGetInt(dependent, XjActorDataKeys.ShiBorrowPowerSuppressed, out int suppressed);
		return !XjShiHighRealmSystem.IsTrueSpiritLocked(dependent, Math.Max(1, annualYear))
			&& suppressed <= 0 && severedUntil < Math.Max(1, annualYear);
	}

	internal static bool TryBeginSuccessionAttempt(string domainId, long candidateActorId, int annualYear)
	{
		if (candidateActorId <= 0L || !TryGet(domainId, out XjShiDomainRecord domain)
			|| domain.LastSuccessionAttemptYear == annualYear) return false;
		long bestId = 0L;
		float bestPractice = float.MinValue;
		int bestAlignment = int.MinValue;
		float bestMingShu = float.MinValue;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			long actorId = ids[i];
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiSeatId, out string seat);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPositionStatus, out string status);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string actorDomain);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiSuccessionEligibleYear, out int eligibleYear);
			if (!string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal)
				|| !string.Equals(seat, XjShiSeatIds.JinLian, StringComparison.Ordinal)
				|| !string.Equals(status, XjShiPositionStatusIds.SuccessionCandidate, StringComparison.Ordinal)
				|| !string.Equals(actorDomain, domain.DomainId, StringComparison.Ordinal)
				|| eligibleYear > annualYear) continue;
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ShiPractice, out float practice);
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiAlignment, out int alignment);
			float mingShu = XjShiMingShuSystem.GetValue(actor);
			bool stronger = bestId <= 0L || practice > bestPractice
				|| Math.Abs(practice - bestPractice) < 0.01f && alignment > bestAlignment
				|| Math.Abs(practice - bestPractice) < 0.01f && alignment == bestAlignment && mingShu > bestMingShu
				|| Math.Abs(practice - bestPractice) < 0.01f && alignment == bestAlignment
					&& Math.Abs(mingShu - bestMingShu) < 0.01f && actorId < bestId;
			if (!stronger) continue;
			bestId = actorId;
			bestPractice = practice;
			bestAlignment = alignment;
			bestMingShu = mingShu;
		}
		if (bestId != candidateActorId) return false;
		domain.LastSuccessionAttemptYear = annualYear;
		MarkChanged();
		return true;
	}

	private static bool AdvanceHostVacancy(XjShiDomainRecord domain, int year)
	{
		if (domain == null) return false;
		if (string.Equals(domain.LegacyMigrationState, XjShiDomainMigrationIds.AncientLegacyJinDi, StringComparison.Ordinal))
		{
			// 古释遗金地可因低频世界事件暂时现世；应身一旦苏醒则持续显世。
			// 这只改变遗地显隐，不恢复旧主人，也不开放今释占有权。
			if (domain.AncientLegacyResponseAwakened > 0)
				return SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
			if (domain.AncientLegacyManifestUntilYear >= year && domain.AncientLegacyManifestUntilYear > 0)
				return SetVisibility(domain, XjShiDomainVisibilityIds.Manifest, year);
			if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)) return false;
			if (domain.AncientLegacyManifestUntilYear > 0 && year > domain.AncientLegacyManifestUntilYear)
			{
				domain.AncientLegacyManifestUntilYear = 0;
				return SetVisibility(domain, XjShiDomainVisibilityIds.Hidden, year);
			}
		}
		if (string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
			|| string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Hidden, StringComparison.Ordinal)) return false;
		if (!string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Unstable, StringComparison.Ordinal))
			return SetVisibility(domain, XjShiDomainVisibilityIds.Unstable, year);
		int since = domain.UnstableSinceYear > 0 ? domain.UnstableSinceYear : year;
		if (year - since >= XjShiCatalog.DomainHostVacancyGraceYears)
			return SetVisibility(domain, XjShiDomainVisibilityIds.Hidden, year);
		return false;
	}

	private static int ResolveMoHeCapacity(XjShiDomainRecord domain)
	{
		if (domain == null
			|| string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return 0;
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)) return 108;
		if (string.Equals(domain.DomainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)) return 1;
		int stageBonus = 0;
		if (domain.OwnerActorId > 0L
			&& XjActorRegistry.ResolveKnownOrWorld(domain.OwnerActorId, out Actor owner)
			&& owner?.data != null)
		{
			XjActorAccessor.TryGetString(owner, XjActorDataKeys.ShiDharmaFormStage, out string stage);
			if (string.Equals(stage, XjShiDharmaFormStageIds.ResponseBody, StringComparison.Ordinal)) stageBonus = 1;
			else if (string.Equals(stage, XjShiDharmaFormStageIds.SelfReturned, StringComparison.Ordinal)) stageBonus = 2;
			else if (string.Equals(stage, XjShiDharmaFormStageIds.WorldHonoredPath, StringComparison.Ordinal)) stageBonus = 3;
		}
		int growthBonus = Math.Max(0, domain.Growth) / XjShiCatalog.DomainGrowthPerCapacity;
		return Math.Min(XjShiCatalog.MaximumDomainMoHeCapacity,
			XjShiCatalog.BaseDomainMoHeCapacity + stageBonus + growthBonus);
	}

	private static int ResolveLianMinCapacityForMoHe(Actor moHe, XjShiDomainRecord domain)
	{
		if (moHe?.data == null || domain == null
			|| string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)) return 0;
		XjActorAccessor.TryGetInt(moHe, XjActorDataKeys.ShiCompletedLives, out int completedLives);
		XjActorAccessor.TryGetInt(moHe, XjActorDataKeys.ShiAlignment, out int alignment);
		int lifeBonus = Math.Max(0, completedLives) / 2;
		int growthBonus = Math.Max(0, domain.Growth) / XjShiCatalog.DomainGrowthPerCapacity;
		int alignmentBonus = alignment >= 85 ? 2 : alignment >= 65 ? 1 : 0;
		return Math.Min(XjShiCatalog.MaximumLianMinCapacityPerMoHe,
			XjShiCatalog.BaseLianMinCapacityPerMoHe + lifeBonus + growthBonus + alignmentBonus);
	}

	private static int CountLiveRealmInDomain(string domainId, string realm)
	{
		int count = 0;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string actorDomain);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string actorRealm);
			if (string.Equals(actorDomain, domainId, StringComparison.Ordinal)
				&& string.Equals(actorRealm, realm, StringComparison.Ordinal)) count++;
		}
		return count;
	}

	private static bool IsActorBoundToDomain(long actorId, string domainId)
	{
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string actorDomain);
		return string.Equals(actorDomain, domainId, StringComparison.Ordinal);
	}

	private static bool IsLiveActorInDomainAtRealm(long actorId, string domainId, string realm)
	{
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string actorDomain);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string actorRealm);
		return string.Equals(actorDomain, domainId, StringComparison.Ordinal)
			&& string.Equals(actorRealm, realm, StringComparison.Ordinal);
	}


	private static bool RecalculateDerivedState(int year)
	{
		Dictionary<string, int[]> previous = new Dictionary<string, int[]>(StringComparer.Ordinal);
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null) continue;
			previous[domain.DomainId] = new[]
			{
				domain.OccupiedMoHePositions,
				domain.OccupiedLianMinPositions,
				domain.SuccessionCandidateCount,
				domain.MoHePositionCapacity,
				domain.LianMinPositionBaseCapacity,
				domain.LianMinPositionCapacity,
				domain.OccupiedDharmaFormPositions,
				domain.DharmaFormCandidateCount,
				domain.DharmaFormPositionCapacity
			};
			domain.OccupiedMoHePositions = 0;
			domain.OccupiedLianMinPositions = 0;
			domain.SuccessionCandidateCount = 0;
			domain.OccupiedDharmaFormPositions = 0;
			domain.DharmaFormCandidateCount = 0;
			domain.MoHePositionCapacity = string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)
				? 108 : ResolveMoHeCapacity(domain);
			domain.LianMinPositionBaseCapacity =
				string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)
					? 108
					: string.Equals(domain.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal)
						? 0 : XjShiCatalog.BaseLianMinCapacityPerMoHe;
			if (string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal))
				domain.DharmaFormPositionCapacity = 0;
			domain.LianMinPositionCapacity = 0;
		}

		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string domainId);
			if (!TryGet(domainId, out XjShiDomainRecord domain)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition);
			long actorId = ((BaseSystemData)actor.data).id;
			XjShiDomainRecord dharmaFoundation = domain;
			bool modern = string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
			if (modern)
			{
				dharmaFoundation = TryGetOwnedNorthJinDi(actorId, out XjShiDomainRecord ownedFoundation)
					? ownedFoundation : null;
			}
			if ((string.Equals(realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal)
				|| string.Equals(realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
				&& dharmaFoundation != null)
			{
				// 今释肉身虽在旃檀林，但法相占位必须记在本人掌握的金地上。
				dharmaFoundation.OccupiedDharmaFormPositions++;
			}
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDharmaFormCandidateState, out string candidateState);
			// 总览中的“候位法相”按实际法相根基统计，不再记到旃檀林本体。
			if (dharmaFoundation != null
				&& (string.Equals(candidateState, XjShiDharmaFormCandidateIds.ManualRecordReady, StringComparison.Ordinal)
					|| string.Equals(candidateState, XjShiDharmaFormCandidateIds.Eligible, StringComparison.Ordinal)
					|| string.Equals(candidateState, XjShiDharmaFormCandidateIds.AttemptCooldown, StringComparison.Ordinal)))
			{
				dharmaFoundation.DharmaFormCandidateCount++;
			}
			if (string.Equals(realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				domain.OccupiedMoHePositions++;
				domain.LianMinPositionCapacity += ResolveLianMinCapacityForMoHe(actor, domain);
			}
			else if (string.Equals(realm, XjShiRealmIds.LianMin, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiPositionStatus, out string status);
				bool occupies = string.Equals(status, XjShiPositionStatusIds.Attached, StringComparison.Ordinal)
					|| string.Equals(status, XjShiPositionStatusIds.ReincarnationReserved, StringComparison.Ordinal)
					|| string.Equals(status, XjShiPositionStatusIds.SuccessionCandidate, StringComparison.Ordinal);
				if (occupies) domain.OccupiedLianMinPositions++;
				if (string.Equals(status, XjShiPositionStatusIds.SuccessionCandidate, StringComparison.Ordinal))
					domain.SuccessionCandidateCount++;
			}
		}

		bool changed = false;
		foreach (XjShiDomainRecord domain in DomainsById.Values)
		{
			if (domain == null) continue;
			if (domain.MoHePositionCapacity < domain.OccupiedMoHePositions)
				domain.MoHePositionCapacity = domain.OccupiedMoHePositions;
			if (domain.LianMinPositionCapacity < domain.OccupiedLianMinPositions)
				domain.LianMinPositionCapacity = domain.OccupiedLianMinPositions;
			if (!string.Equals(domain.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal)
				&& domain.DharmaFormPositionCapacity < domain.OccupiedDharmaFormPositions)
				domain.DharmaFormPositionCapacity = domain.OccupiedDharmaFormPositions;
			if (!previous.TryGetValue(domain.DomainId, out int[] old)
				|| old[0] != domain.OccupiedMoHePositions
				|| old[1] != domain.OccupiedLianMinPositions
				|| old[2] != domain.SuccessionCandidateCount
				|| old[3] != domain.MoHePositionCapacity
				|| old[4] != domain.LianMinPositionBaseCapacity
				|| old[5] != domain.LianMinPositionCapacity
				|| old[6] != domain.OccupiedDharmaFormPositions
				|| old[7] != domain.DharmaFormCandidateCount
				|| old[8] != domain.DharmaFormPositionCapacity)
			{
				changed = true;
			}
		}
		return changed;
	}


	private static void SyncVisibilityToBoundActors(XjShiDomainRecord domain, string visibility, int year,
		bool applyHiddenConsequence)
	{
		if (domain == null) return;
		IReadOnlyList<long> ids = XjCultivatorCache.GetShiIds();
		for (int i = 0; i < ids.Count; i++)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(ids[i], out Actor actor)
				|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) continue;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiDomainId, out string actorDomain);
			if (!string.Equals(actorDomain, domain.DomainId, StringComparison.Ordinal)) continue;
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiJinDiStatus, ToLegacyStatus(visibility));
			if (string.Equals(visibility, XjShiDomainVisibilityIds.Manifest, StringComparison.Ordinal))
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiDomainLinkSeveredUntilYear, out int severedUntil);
				if (severedUntil < year) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 0);
			}
			else
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowPowerSuppressed, 1);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiBorrowedPower, 0);
			}
			if (applyHiddenConsequence) XjShiState.ApplyDomainHiddenConsequence(actor, domain, year);
		}
		if (applyHiddenConsequence) domain.LastVisibilityConsequenceYear = Math.Max(1, year);
	}

	private static void ReadIdentity(Actor actor, out string tradition, out string lineageId)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out tradition);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out lineageId);
		tradition ??= string.Empty;
		lineageId ??= string.Empty;
	}

	private static bool IsLiveShiActor(long actorId, string minimumRealm)
	{
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		return XjShiCatalog.GetRank(realm) >= XjShiCatalog.GetRank(minimumRealm);
	}

	private static bool IsLiveShiActorAtRealm(long actorId, string exactRealm)
	{
		if (actorId <= 0L || !XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor)
			|| actor?.data == null || !actor.isAlive() || !XjCultivationPathRules.IsShi(actor)) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string realm);
		return string.Equals(realm, exactRealm, StringComparison.Ordinal);
	}

	private static XjShiDomainRecord Normalize(XjShiDomainRecord source)
	{
		if (source == null) return null;
		string domainId = NormalizeId(source.DomainId);
		if (domainId.Length == 0 || !XjShiDomainTypeIds.IsKnown(source.DomainType)) return null;
		XjShiDomainRecord result = source.Clone();
		result.DomainId = domainId;
		if (XjShiHeavenCatalog.TryParseFragmentId(result.DomainId, out int fragmentIndex))
		{
			int heavenIndex = XjShiHeavenCatalog.GetHeavenIndexForFragment(fragmentIndex);
			result.IsNorthWorldHonoredFragment = 1;
			result.DomainType = XjShiDomainTypeIds.JinDi;
			result.Tradition = XjShiTraditionIds.Modern;
			result.SourceHeavenId = XjShiHeavenCatalog.GetHeavenId(heavenIndex);
			result.SourceHeavenCategory = XjShiHeavenCatalog.GetCategoryId(heavenIndex);
			result.SourceHeavenIndex = heavenIndex;
			result.SourceHeavenFragmentOrdinal = XjShiHeavenCatalog.GetFragmentOrdinal(fragmentIndex);
			result.SourceHeavenFragmentCount = XjShiHeavenCatalog.GetFragmentCountForHeaven(heavenIndex);
			result.DisplayName = XjShiHeavenCatalog.GetFragmentDisplayName(fragmentIndex);
		}
		else if (string.IsNullOrWhiteSpace(result.DisplayName))
			result.DisplayName = ResolveDomainDisplayName(result.DomainId, result.DomainType, result.LineageId);
		if (!XjShiDomainVisibilityIds.IsKnown(result.Visibility)) result.Visibility = XjShiDomainVisibilityIds.Hidden;
		if (string.Equals(result.Visibility, XjShiDomainVisibilityIds.Absorbed, StringComparison.Ordinal))
		{
			// 普通被吞并金地不再保留主人；北世尊碎片是旃檀林的共同持有权例外，
			// 存档归一化时必须保住庙主、法相根基与真灵锚。
			result.MoHePositionCapacity = 0;
			result.LianMinPositionBaseCapacity = 0;
			result.LianMinPositionCapacity = 0;
			if (result.IsNorthWorldHonoredFragment <= 0)
			{
				result.OwnerActorId = 0L;
				result.HostMoHeId = 0L;
				result.DharmaFormPositionCapacity = 0;
				result.OccupiedDharmaFormPositions = 0;
				result.DharmaFormCandidateCount = 0;
			}
			else
			{
				result.HostMoHeId = 0L;
				result.DharmaFormPositionCapacity = 1;
			}
		}
		if (result.CreatedYear <= 0) result.CreatedYear = 1;
		result.MapRadius = Math.Max(0, result.MapRadius);
		result.MapTerrainSchema = Math.Max(0, result.MapTerrainSchema);
		result.LastPlacedYear = Math.Max(0, result.LastPlacedYear);
		result.AncientLegacySinceYear = Math.Max(0, result.AncientLegacySinceYear);
		result.AncientLegacyLastEventYear = Math.Max(0, result.AncientLegacyLastEventYear);
		result.AncientLegacyEventCount = Math.Max(0, result.AncientLegacyEventCount);
		result.AncientLegacyManifestUntilYear = Math.Max(0, result.AncientLegacyManifestUntilYear);
		result.AncientLegacyResponseAwakened = result.AncientLegacyResponseAwakened > 0 ? 1 : 0;
		result.AncientLegacyResponseAwakenedYear = Math.Max(0, result.AncientLegacyResponseAwakenedYear);
		result.AncientLegacyLastDiscovererActorId = Math.Max(0L, result.AncientLegacyLastDiscovererActorId);
		result.AncientLegacyFormerOwnerName ??= string.Empty;
		if (!XjAncientShiLegacyEventIds.IsKnown(result.AncientLegacyLastEventId))
			result.AncientLegacyLastEventId = XjAncientShiLegacyEventIds.None;
		if (string.Equals(result.DomainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal))
		{
			result.DisplayName = "旃檀林";
			result.MoHePositionCapacity = Math.Max(108, result.MoHePositionCapacity);
			result.LianMinPositionBaseCapacity = Math.Max(108, result.LianMinPositionBaseCapacity);
			result.DharmaFormPositionCapacity = 0;
			result.AbsorbedJinDiCount = XjShiHeavenCatalog.ZhantanlinFragmentCount;
		}
		// 旧版主持字段只作反序列化兼容，读取后无条件清零。
		result.HostMoHeId = 0L;
		return result;
	}

	private static string ResolveDomainDisplayName(string domainId, string domainType, string lineageId)
	{
		if (XjShiHeavenCatalog.TryParseFragmentId(domainId, out int fragmentIndex))
			return XjShiHeavenCatalog.GetFragmentDisplayName(fragmentIndex);
		if (string.Equals(domainType, XjShiDomainTypeIds.YouTanLin, StringComparison.Ordinal))
			return "旃檀林";
		if (string.Equals(domainType, XjShiDomainTypeIds.YingTu, StringComparison.Ordinal))
		{
			string lineage = XjShiCatalog.GetLineageDisplay(lineageId);
			return string.Equals(lineage, "法脉未定", StringComparison.Ordinal)
				|| string.Equals(lineage, "七相未定", StringComparison.Ordinal)
				? "无名应土" : lineage + "应土";
		}
		if (string.Equals(domainType, XjShiDomainTypeIds.JinDi, StringComparison.Ordinal)
			|| string.Equals(domainType, XjShiDomainTypeIds.BaoTu, StringComparison.Ordinal))
		{
			int index = XjDeterministicHash.PositiveIndex(
				XjDeterministicHash.StableHash(domainId ?? string.Empty),
				"shi_jindi_name", JinDiNamePrefixes.Length);
			return JinDiNamePrefixes[index] + "金地";
		}
		return XjShiDomainCatalog.GetTypeDisplay(domainType);
	}

	private static string NormalizeId(string value)
	{
		return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
	}

	private static void MarkChanged()
	{
		XjWorldArchiveSystem.MarkChanged();
	}
}
