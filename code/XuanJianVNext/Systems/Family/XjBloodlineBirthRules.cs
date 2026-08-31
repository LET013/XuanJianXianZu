using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Family;

// VNext confirmed father-line bloodline layer; no UI backfill, legacy persistence,
// maternal/surname/spouse inference, or LoadReadOnly writes.
internal readonly struct XjBloodlineBirthResult
{
	internal readonly bool ShouldWrite;
	internal readonly string Quality;
	internal readonly int Concentration;
	internal readonly int Generation;
	internal readonly string OriginDaoTu;
	internal readonly bool IsAncestor;
	internal readonly int ExtraTalentInheritance;
	internal readonly string Source;

	internal XjBloodlineBirthResult(
		bool shouldWrite,
		string quality,
		int concentration,
		int generation,
		string originDaoTu,
		bool isAncestor,
		int extraTalentInheritance,
		string source)
	{
		ShouldWrite = shouldWrite;
		Quality = string.IsNullOrWhiteSpace(quality) ? XjBloodlineBirthRules.QualityChenXi : quality.Trim();
		Concentration = Math.Max(0, Math.Min(100, concentration));
		Generation = Math.Max(0, generation);
		OriginDaoTu = originDaoTu ?? string.Empty;
		IsAncestor = isAncestor;
		ExtraTalentInheritance = Math.Max(0, extraTalentInheritance);
		Source = source ?? string.Empty;
	}
}

internal static class XjBloodlineBirthRules
{
	private const float LianQiDirectChildMinimumCongenitalMingShu = 12f;
	private const float ZhuJiDirectChildMinimumCongenitalMingShu = 25f;
	private const float ZiFuDirectChildMinimumCongenitalMingShu = 30f;
	private const float JinDanDirectChildMinimumCongenitalMingShu = 42f;
	private const float LianQiDirectChildMinimumHuiGuang = 12f;
	private const float ZhuJiDirectChildMinimumHuiGuang = 25f;
	private const float ZiFuDirectChildMinimumHuiGuang = 30f;
	private const float JinDanDirectChildMinimumHuiGuang = 42f;

	internal const string QualityChenXi = "尘息";
	internal const string QualityQianLiu = "潜流";
	internal const string QualityMingMai = "明脉";
	internal const string QualityLingYing = "灵应";
	internal const string QualityGuiYuan = "归源";
	internal const string QualityYanZu = "衍祖";
	internal const string QualityZuXue = "祖血";

	internal const string SourceFounder = "Founder";
	internal const string SourceFatherConfirmed = "FatherConfirmed";
	internal const string SourceAtavism = "Atavism";
	internal const string SourceHighRealm = "HighRealmOverride";
	internal const string SourceUnknownFather = "UnknownFather";
	internal const string SourceAlreadyApplied = "AlreadyApplied";

	internal static bool TryApplyForConfirmedFamily(Actor actor, in XjFamilyIdentity identity, int currentYear)
	{
		if (actor?.data == null || identity == null || !identity.Found || identity.FamilyStableIdValue <= 0L)
		{
			return false;
		}

		bool descendantChanged = false;
		try
		{
			descendantChanged = XjHighRealmDescendantRules.RefreshFromParents(actor);
		}
		catch (NullReferenceException)
		{
			// 原生父母引用可能恰在读档/死亡迁移窗口内失效。后裔可见特质
			// 属于可重建投影，不得因此阻断角色本人的血脉高境刷新。
		}
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineApplied, out int applied) && applied > 0)
		{
			return descendantChanged;
		}

		XjBloodlineBirthResult result = Calculate(actor, identity, currentYear);
		if (!result.ShouldWrite)
		{
			return descendantChanged;
		}

		WriteBloodlineResult(actor, result, currentYear);
		return true;
	}

	internal static bool TryRefreshForRealmPromotion(Actor actor, int currentYear)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		XjFamilyReadModel readModel = XjFamilyReadModel.Shared;
		XjFamilyIdentity identity = XjFamilyIdentity.Empty;
		bool hasConfirmedIdentity = false;
		try
		{
			hasConfirmedIdentity = readModel != null
				&& readModel.TryGetConfirmedIdentity(actorId, out identity);
		}
		catch (NullReferenceException)
		{
			// 读档/clearWorld 同帧里家族派生索引可能正在换代。血脉刷新不是
			// 突破的权威写入，不允许它因为短暂 ReadModel 空窗破坏突破主链。
			readModel = null;
		}
		if (!hasConfirmedIdentity)
		{
			// 突破可能早于家族 ReadModel 完成世界绑定。仍按原生 parent_id
			// 处理现存直系子女，等账本后续正常建立。
			return RefreshDirectChildrenFromPromotedParent(actor, XjFamilyIdentity.Empty, currentYear);
		}

		string realmQuality = GetBloodlineQualityFromRealm(actor);
		if (string.IsNullOrWhiteSpace(realmQuality))
		{
			return false;
		}

		bool appliedNow = false;
		if (!TryReadAppliedBloodline(actor, out string currentQuality, out int currentConcentration, out int currentGeneration, out string originDaoTu))
		{
			appliedNow = TryApplyForConfirmedFamily(actor, identity, currentYear);
			if (!TryReadAppliedBloodline(actor, out currentQuality, out currentConcentration, out currentGeneration, out originDaoTu))
			{
				return appliedNow;
			}
		}

		if (ShouldApplyRealmPromotionBloodline(realmQuality, currentQuality, currentConcentration, currentGeneration))
		{
			if (string.IsNullOrWhiteSpace(originDaoTu))
			{
				originDaoTu = ReadDaoTu(actor);
			}

			XjBloodlineBirthResult result = Create(true, realmQuality, 100, 1, originDaoTu, true, SourceHighRealm);
			WriteBloodlineResult(actor, result, currentYear);
			appliedNow = true;
		}

		return RefreshDirectChildrenFromPromotedParent(actor, identity, currentYear) || appliedNow;
	}

	internal static XjBloodlineBirthResult Calculate(Actor actor, in XjFamilyIdentity identity, int currentYear)
	{
		if (actor?.data == null || identity == null || !identity.Found || identity.FamilyStableIdValue <= 0L)
		{
			return Default(SourceUnknownFather, false);
		}

		string actorRealmQuality = GetBloodlineQualityFromRealm(actor);
		string actorDaoTu = ReadDaoTu(actor);
		if (identity.Generation <= 1)
		{
			if (string.IsNullOrWhiteSpace(actorRealmQuality))
			{
				return Default(SourceFounder, true);
			}

			return Create(true, actorRealmQuality, 100, 1, actorDaoTu, true, SourceHighRealm);
		}

		if (!XjFamilyResolver.TryResolveInheritanceParentActorId(actor, out long fatherActorId)
			|| fatherActorId <= 0L
			|| !TryResolveActorById(fatherActorId, out Actor father)
			|| father?.data == null
			|| !TryReadAppliedBloodline(father, out string fatherQuality, out int fatherConcentration, out int fatherGeneration, out string fatherOriginDaoTu))
		{
			// Confirmed family identity can arrive before the father's real bloodline record.
			// Wait for the runtime source instead of permanently writing a fabricated empty bloodline.
			return Default(SourceUnknownFather, false);
		}

		if (fatherConcentration <= 0)
		{
			return Default(SourceFatherConfirmed, true);
		}

		string inheritedQuality = NormalizeQuality(fatherQuality);
		int generation = Math.Max(0, fatherGeneration) + 1;
		int concentration = GetConcentrationByGeneration(generation);
		string qualityFromRealm = GetBloodlineQualityFromRealm(actor);
		string quality = concentration <= 0 ? QualityChenXi : inheritedQuality;
		string originDaoTu = string.IsNullOrWhiteSpace(fatherOriginDaoTu) ? actorDaoTu : fatherOriginDaoTu;
		string source = SourceFatherConfirmed;

		if (ShouldAtavism(actor, currentYear, generation))
		{
			quality = inheritedQuality;
			generation = 1;
			concentration = 100;
			source = SourceAtavism;
		}

		if (!string.IsNullOrWhiteSpace(qualityFromRealm)
			&& ShouldOverrideBloodline(inheritedQuality, fatherGeneration, qualityFromRealm))
		{
			quality = qualityFromRealm;
			concentration = 100;
			generation = 1;
			source = SourceHighRealm;
			if (string.IsNullOrWhiteSpace(originDaoTu))
			{
				originDaoTu = actorDaoTu;
			}
		}

		if (concentration <= 0 && !string.Equals(source, SourceHighRealm, StringComparison.Ordinal))
		{
			quality = QualityChenXi;
		}

		return Create(true, quality, concentration, generation, originDaoTu, generation == 1, source);
	}

	internal static bool TryReadAppliedBloodline(
		Actor actor,
		out string quality,
		out int concentration,
		out int generation,
		out string originDaoTu)
	{
		quality = string.Empty;
		concentration = 0;
		generation = 0;
		originDaoTu = string.Empty;
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineApplied, out int applied)
			|| applied <= 0)
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineQuality, out quality);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineConcentration, out concentration);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjBloodlineGeneration, out generation);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, out originDaoTu);
		quality = NormalizeQuality(quality);
		concentration = Math.Max(0, Math.Min(100, concentration));
		generation = Math.Max(0, generation);
		return true;
	}

	private static void WriteBloodlineResult(Actor actor, in XjBloodlineBirthResult result, int currentYear)
	{
		if (actor?.data == null || !result.ShouldWrite)
		{
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineSource, out string previousSource);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjBloodlineQuality, out string previousQuality);
		bool newAtavism = string.Equals(result.Source, SourceAtavism, StringComparison.Ordinal)
			&& (!string.Equals(previousSource, SourceAtavism, StringComparison.Ordinal)
				|| !string.Equals(NormalizeQuality(previousQuality), NormalizeQuality(result.Quality), StringComparison.Ordinal));

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineQuality, result.Quality);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineConcentration, result.Concentration);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineGeneration, result.Generation);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineOriginDaoTu, result.OriginDaoTu);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineIsAncestor, result.IsAncestor ? 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineExtraTalentInheritance, result.ExtraTalentInheritance);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineApplied, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjBloodlineAppliedYear, Math.Max(0, currentYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjBloodlineSource, result.Source);
		XjFamilyBloodlineAggregateCache.InvalidateActor(actor);
		if (newAtavism && currentYear > 0)
		{
			long actorId = ((BaseSystemData)actor.data).id;
			long familyId = 0L;
			XjFamilyReadModel readModel = XjFamilyReadModel.Shared;
			if (readModel != null) readModel.TryGetConfirmedFamilyStableId(actorId, out familyId);
			string actorName;
			try { actorName = actor.getName(); } catch { actorName = string.Empty; }
			if (string.IsNullOrWhiteSpace(actorName)) actorName = "族中后人";
			XjWorldHistoryStore.RecordDomainEvent(
				XjWorldHistoryCategory.Family,
				"祖脉复显",
				actorName + "血脉返祖，久已淡薄的【" + result.Quality + "】一脉重新显现。",
				3,
				actorId: actorId,
				actorName: actorName,
				familyId: familyId,
				year: Math.Max(0, currentYear),
				eventType: "BloodlineAtavism:" + result.Quality,
				visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.CenturyCandidate),
				mirrorToWorldLog: false);
		}
	}

	private static bool RefreshDirectChildrenFromPromotedParent(Actor promotedParent, in XjFamilyIdentity parentIdentity, int currentYear)
	{
		if (!XjSafeCore.IsAliveActor(promotedParent) || promotedParent.data == null)
		{
			return false;
		}

		long parentId = ((BaseSystemData)promotedParent.data).id;
		if (parentId <= 0L) return false;
		XjFamilyReadModel readModel = XjFamilyReadModel.Shared;
		List<Actor> directChildren = new List<Actor>();
		HashSet<long> seenChildIds = new HashSet<long>();
		if (readModel != null && parentIdentity != null && parentIdentity.Found && parentIdentity.FamilyStableIdValue > 0L)
		{
			try
			{
				IEnumerable<Actor> familyMembers = readModel.GetFamilyMembers(parentIdentity.FamilyStableIdValue);
				if (familyMembers != null)
				{
					foreach (Actor child in familyMembers)
					{
						TryCollectDirectChild(child, parentId, seenChildIds, directChildren);
					}
				}
			}
			catch (NullReferenceException)
			{
				// 家族运行索引正在读档重建时退化到 ActorRegistry 的低频兜底。
				readModel = null;
			}
		}

		// 子女可能因婚配或分支已经离开父母当前家族。高境突破是低频事件，
		// 仅在家族成员表未覆盖现存子女时读取一次既有角色索引，不增加年度或逐帧扫描。
		int expectedLivingChildren = SafeReadCurrentChildrenCount(promotedParent);
		if (expectedLivingChildren < 0 || directChildren.Count < expectedLivingChildren)
		{
			IReadOnlyList<Actor> knownActors = XjActorRegistry.Snapshot();
			if (knownActors != null)
			{
				for (int i = 0; i < knownActors.Count; i++)
				{
					TryCollectDirectChild(knownActors[i], parentId, seenChildIds, directChildren);
				}
			}
		}

		// 家族/角色索引可能在读档同帧失效；排序前再次清理空对象。
		directChildren.RemoveAll(child => !XjSafeCore.IsAliveActor(child) || child.data == null);
		directChildren.Sort((left, right) =>
			((BaseSystemData)left.data).id.CompareTo(((BaseSystemData)right.data).id));
		int descendantCap = Math.Max(0, XjHighRealmDescendantRules.ResolveBirthCap(promotedParent));
		bool changed = false;
		for (int childIndex = 0; childIndex < directChildren.Count; childIndex++)
		{
			Actor child = directChildren[childIndex];
			if (!XjSafeCore.IsAliveActor(child) || child.data == null) continue;
			try
			{
				bool receivesDirectDescendantTrait = childIndex < descendantCap;
				changed |= XjHighRealmDescendantRules.ApplyFromPromotedParent(
					child, promotedParent, receivesDirectDescendantTrait);
				long childId = ((BaseSystemData)child.data).id;
				if (childId <= 0L || readModel == null)
				{
					continue;
				}
				XjFamilyIdentity childIdentity;
				try
				{
					if (!readModel.TryGetConfirmedIdentity(childId, out childIdentity)) continue;
				}
				catch (NullReferenceException)
				{
					continue;
				}

				XjBloodlineBirthResult result = Calculate(child, childIdentity, currentYear);
				if (!result.ShouldWrite || !ShouldReplaceAppliedBloodline(child, result))
				{
					continue;
				}

				WriteBloodlineResult(child, result, currentYear);
				changed = true;
			}
			catch (NullReferenceException)
			{
				// WorldBox 可能在同帧销毁/迁移子代的原生亲属对象。单个坏引用
				// 只跳过该子代，不能让整个突破后处理持续刷 NRE。
			}
		}

		return changed;
	}

	private static int SafeReadCurrentChildrenCount(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor) || actor.data == null) return 0;
		try { return Math.Max(0, actor.current_children_count); }
		catch (NullReferenceException) { return -1; }
	}

	private static void TryCollectDirectChild(Actor child, long parentId, HashSet<long> seenChildIds, List<Actor> result)
	{
		if (!XjSafeCore.IsAliveActor(child) || child.data == null || parentId <= 0L) return;
		try
		{
			long childId = ((BaseSystemData)child.data).id;
			if (childId <= 0L || childId == parentId || seenChildIds.Contains(childId)) return;
			if (child.data.parent_id_1 != parentId && child.data.parent_id_2 != parentId) return;
			seenChildIds.Add(childId);
			result.Add(child);
		}
		catch (NullReferenceException)
		{
			// 读档/死亡迁移同帧的半销毁 Actor 不进入血脉刷新。
		}
	}

	private static bool ShouldApplyRealmPromotionBloodline(string realmQuality, string currentQuality, int currentConcentration, int currentGeneration)
	{
		int realmPriority = GetQualityPriority(realmQuality);
		int currentPriority = GetQualityPriority(currentQuality);
		return realmPriority > currentPriority
			|| (realmPriority > 0
				&& realmPriority == currentPriority
				&& (currentGeneration != 1 || currentConcentration < 100));
	}

	private static bool ShouldReplaceAppliedBloodline(Actor actor, in XjBloodlineBirthResult result)
	{
		if (actor?.data == null || !result.ShouldWrite)
		{
			return false;
		}

		if (!TryReadAppliedBloodline(actor, out string currentQuality, out int currentConcentration, out int currentGeneration, out string currentOriginDaoTu))
		{
			return true;
		}

		int nextPriority = GetQualityPriority(result.Quality);
		int currentPriority = GetQualityPriority(currentQuality);
		if (nextPriority != currentPriority)
		{
			return nextPriority > currentPriority;
		}

		if (result.Concentration != currentConcentration)
		{
			return result.Concentration > currentConcentration;
		}

		if (result.Generation > 0 && (currentGeneration <= 0 || result.Generation < currentGeneration))
		{
			return true;
		}

		return string.IsNullOrWhiteSpace(currentOriginDaoTu) && !string.IsNullOrWhiteSpace(result.OriginDaoTu);
	}

	private static XjBloodlineBirthResult Default(string source, bool shouldWrite)
	{
		return Create(shouldWrite, QualityChenXi, 0, 0, string.Empty, false, source);
	}

	private static XjBloodlineBirthResult Create(
		bool shouldWrite,
		string quality,
		int concentration,
		int generation,
		string originDaoTu,
		bool isAncestor,
		string source)
	{
		int extraTalent = GetExtraTalentInheritance(quality, concentration);
		return new XjBloodlineBirthResult(
			shouldWrite,
			quality,
			concentration,
			generation,
			originDaoTu,
			isAncestor,
			extraTalent,
			source);
	}

	private static int GetConcentrationByGeneration(int generation)
	{
		return generation switch
		{
			1 => 100,
			2 => 50,
			3 => 10,
			_ => 0
		};
	}

	private static string GetBloodlineQualityFromRealm(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		if (string.Equals(realmId, XjRealmIds.DaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.FuQiDaoTai, StringComparison.Ordinal)
			|| string.Equals(realmId, XjRealmIds.DaoTaiPlaceholder, StringComparison.Ordinal)) return QualityZuXue;
		return XjHighRealmIdentity.ResolveClass(realmId) switch
		{
			XjHighRealmClass.ZhenJun => QualityYanZu,
			XjHighRealmClass.ZhenRen => QualityGuiYuan,
			_ => string.Empty
		};
	}

	internal static float GetBloodlineBaseValue(Actor actor)
	{
		if (!TryReadAppliedBloodline(actor, out string quality, out int concentration, out _, out _))
		{
			return 0f;
		}

		return NormalizeQuality(quality) switch
		{
			QualityZuXue => concentration >= 100 ? 4f
				: concentration >= 50 ? 2f
				: concentration >= 30 ? 1.2f
				: concentration >= 10 ? 0.4f
				: 0f,
			QualityYanZu => concentration >= 100 ? 2f
				: concentration >= 50 ? 1f
				: concentration >= 30 ? 0.6f
				: concentration >= 10 ? 0.2f
				: 0f,
			QualityGuiYuan => concentration >= 100 ? 1f
				: concentration >= 50 ? 0.5f
				: concentration >= 30 ? 0.3f
				: concentration >= 10 ? 0.1f
				: 0f,
			QualityLingYing => 0.4f,
			QualityMingMai => 0.2f,
			QualityQianLiu => 0.1f,
			_ => 0f
		};
	}

	internal static float GetAptitudeInheritanceChanceBonus(Actor actor)
	{
		return GetEarlyLifeInheritanceBias(actor);
	}

	internal static float GetAptitudeTierWeightBias(Actor actor)
	{
		float inheritanceBias = GetEarlyLifeInheritanceBias(actor);
		GetDirectParentRealmSeedFloors(actor, out float congenitalMingShuFloor, out _);
		float parentRealmBias = Math.Max(0f, Math.Min(1f, congenitalMingShuFloor / 100f));
		return Math.Max(inheritanceBias, parentRealmBias);
	}

	internal static float GetEarlyLifeInheritanceBias(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return 0f;
		}

		// 血脉优势只跟随角色自身的真实父系记录，不再因为族中某一位真人或
		// 真君而扩散给同族全部幼儿。家族整体的传承能力由既有族议、功法库、
		// 丹药与器库承担，不能再变成全族共享的高资质复制器。
		float ownQualityBonus = GetOwnAwakeningQualityBonusForAggregate(actor);
		float selfConcentrationBonus = 0f;
		if (TryReadAppliedBloodline(actor, out _, out int concentration, out _, out _))
		{
			selfConcentrationBonus = Math.Max(0, Math.Min(100, concentration)) / 1000f;
		}

		return Math.Min(0.24f, ownQualityBonus + selfConcentrationBonus);
	}

	internal static void GetDirectParentRealmAptitudeChanceFloors(Actor actor, out float fateChanceFloor, out float aptitudeChanceFloor)
	{
		fateChanceFloor = 0f;
		aptitudeChanceFloor = 0f;
		GetDirectParentRealmSeedFloors(actor, out float congenitalMingShuFloor, out _);
		if (congenitalMingShuFloor >= JinDanDirectChildMinimumCongenitalMingShu)
		{
			fateChanceFloor = 0.82f;
			aptitudeChanceFloor = 0.78f;
			return;
		}

		if (congenitalMingShuFloor >= ZiFuDirectChildMinimumCongenitalMingShu)
		{
			fateChanceFloor = 0.72f;
			aptitudeChanceFloor = 0.68f;
		}
	}

	internal static int GetDirectParentRealmAptitudeTierFloor(Actor actor)
	{
		GetDirectParentRealmSeedFloors(actor, out float congenitalMingShuFloor, out _);
		if (congenitalMingShuFloor >= JinDanDirectChildMinimumCongenitalMingShu) return 5;
		if (congenitalMingShuFloor >= ZiFuDirectChildMinimumCongenitalMingShu) return 4;
		return 0;
	}

	internal static void GetDirectParentRealmSeedFloors(Actor actor, out float congenitalMingShuFloor, out float huiGuangFloor)
	{
		congenitalMingShuFloor = 0f;
		huiGuangFloor = 0f;
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		long parent1 = actor.data.parent_id_1;
		long parent2 = actor.data.parent_id_2;
		ApplyDirectParentRealmSeedFloors(parent1, ref congenitalMingShuFloor, ref huiGuangFloor);
		if (parent2 > 0L && parent2 != parent1)
		{
			ApplyDirectParentRealmSeedFloors(parent2, ref congenitalMingShuFloor, ref huiGuangFloor);
		}
	}

	private static void ApplyDirectParentRealmSeedFloors(long parentId, ref float congenitalMingShuFloor, ref float huiGuangFloor)
	{
		if (parentId <= 0L || !TryResolveActorById(parentId, out Actor parent) || parent?.data == null)
		{
			ApplyDirectParentLedgerRealmSeedFloors(parentId, ref congenitalMingShuFloor, ref huiGuangFloor);
			return;
		}

		XjActorAccessor.TryGetString(parent, XjActorDataKeys.RealmId, out string realmId);
		XjHighRealmClass highRealmClass = XjHighRealmIdentity.ResolveClass(realmId);
		if (highRealmClass == XjHighRealmClass.ZhenJun || parent.hasTrait("XjRealm5"))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, JinDanDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, JinDanDirectChildMinimumHuiGuang);
			return;
		}

		if (highRealmClass == XjHighRealmClass.ZhenRen || parent.hasTrait("XjRealm4"))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, ZiFuDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, ZiFuDirectChildMinimumHuiGuang);
			return;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal) || parent.hasTrait("XjRealm3"))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, ZhuJiDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, ZhuJiDirectChildMinimumHuiGuang);
			return;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal) || parent.hasTrait("XjRealm2"))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, LianQiDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, LianQiDirectChildMinimumHuiGuang);
		}
	}

	private static void ApplyDirectParentLedgerRealmSeedFloors(long parentId, ref float congenitalMingShuFloor, ref float huiGuangFloor)
	{
		if (parentId <= 0L
			|| !XjFamilyMemberLedger.TryGetByActorId(parentId, out XjFamilyMemberLedgerEntry entry)
			|| !entry.Found)
		{
			return;
		}

		string realmId = XjFamilyMemberLedger.NormalizeRealmId(entry.RealmId);
		XjHighRealmClass highRealmClass = XjHighRealmIdentity.ResolveClass(realmId);
		if (highRealmClass == XjHighRealmClass.ZhenJun)
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, JinDanDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, JinDanDirectChildMinimumHuiGuang);
			return;
		}

		if (highRealmClass == XjHighRealmClass.ZhenRen)
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, ZiFuDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, ZiFuDirectChildMinimumHuiGuang);
			return;
		}

		if (string.Equals(realmId, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, ZhuJiDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, ZhuJiDirectChildMinimumHuiGuang);
			return;
		}

		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal))
		{
			congenitalMingShuFloor = Math.Max(congenitalMingShuFloor, LianQiDirectChildMinimumCongenitalMingShu);
			huiGuangFloor = Math.Max(huiGuangFloor, LianQiDirectChildMinimumHuiGuang);
		}
	}

	internal static float GetOwnAwakeningQualityBonusForAggregate(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)
			|| !TryReadAppliedBloodline(actor, out string quality, out _, out _, out _))
		{
			return 0f;
		}

		return NormalizeQuality(quality) switch
		{
			QualityQianLiu => 0.0333f,
			QualityMingMai => 0.0667f,
			QualityLingYing => 0.1f,
			QualityGuiYuan => 0.1333f,
			QualityYanZu => 0.1667f,
			QualityZuXue => 0.2f,
			_ => 0f
		};
	}

	private static bool ShouldOverrideBloodline(string currentQuality, int generation, string newQuality)
	{
		if (string.Equals(newQuality, QualityZuXue, StringComparison.Ordinal))
		{
			return true;
		}

		int currentPriority = GetQualityPriority(currentQuality);
		if (string.Equals(newQuality, QualityYanZu, StringComparison.Ordinal) && generation <= 2)
		{
			return currentPriority <= GetQualityPriority(QualityGuiYuan);
		}

		if (string.Equals(newQuality, QualityGuiYuan, StringComparison.Ordinal))
		{
			return generation switch
			{
				1 => currentPriority <= GetQualityPriority(QualityYanZu),
				2 => currentPriority <= GetQualityPriority(QualityGuiYuan),
				3 => true,
				_ => false
			};
		}

		return false;
	}

	private static int GetExtraTalentInheritance(string quality, int concentration)
	{
		int baseValue = NormalizeQuality(quality) switch
		{
			QualityZuXue => 30,
			QualityYanZu => 25,
			QualityGuiYuan => 20,
			QualityLingYing => 15,
			QualityMingMai => 10,
			QualityQianLiu => 5,
			_ => 0
		};
		return (int)Math.Round(baseValue * Math.Max(0, Math.Min(100, concentration)) / 100f, MidpointRounding.AwayFromZero);
	}

	private static int GetQualityPriority(string quality)
	{
		return NormalizeQuality(quality) switch
		{
			QualityZuXue => 6,
			QualityYanZu => 5,
			QualityGuiYuan => 4,
			QualityLingYing => 3,
			QualityMingMai => 2,
			QualityQianLiu => 1,
			_ => 0
		};
	}

	private static bool ShouldAtavism(Actor actor, int currentYear, int generation)
	{
		if (generation <= 1)
		{
			return false;
		}

		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		unchecked
		{
			ulong hash = 1469598103934665603UL;
			hash = (hash ^ (ulong)Math.Max(0L, actorId)) * 1099511628211UL;
			hash = (hash ^ (ulong)Math.Max(0, currentYear)) * 1099511628211UL;
			hash = (hash ^ (ulong)Math.Max(0, generation)) * 1099511628211UL;
			hash = (hash ^ 0x584A424C4F4F444CUL) * 1099511628211UL;
			return hash % 100 < 5;
		}
	}

	private static string ReadDaoTu(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		return daoTu ?? string.Empty;
	}

	private static string NormalizeQuality(string quality)
	{
		if (string.IsNullOrWhiteSpace(quality))
		{
			return QualityChenXi;
		}

		string value = quality.Trim();
		int separator = value.IndexOf('：');
		if (separator >= 0 && separator + 1 < value.Length)
		{
			value = value.Substring(separator + 1).Trim();
		}

		if (value.EndsWith("血脉", StringComparison.Ordinal))
		{
			value = value.Substring(0, value.Length - 2).Trim();
		}

		return string.IsNullOrWhiteSpace(value) ? QualityChenXi : value;
	}

	private static bool TryResolveActorById(long actorId, out Actor actor)
	{
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out actor);
	}
}
