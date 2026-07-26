using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.HighRealm;

internal static class XjXuanJianShenTongSpecials
{
	internal const string YiDuiYingTraitId = "XjYiDuiYing";
	internal const string JieLinZhangTraitId = "XjJieLinZhang";
	internal const string YeTianMenTraitId = "XjYeTianMen";

	private const string TaiYinDaoTu = "太阴";
	private const string MingYangDaoTu = "明阳";
	private const string YiDuiYingXianJi = "仪对影";
	private const float YiDuiYingCultivationSpeedMultiplier = 1.2f;
	private const int YiDuiYingNameSchemaCurrent = 1;
	private const string ChineseFamilyNameKey = "chinese_family_name";
	private const string JieLinZhangXianJi = "结璘章";
	private const string YeTianMenXianJi = "谒天门";
	private const float YeTianMenCooldownSeconds = 30f;
	private const int YiDuiYingInitialAge = 18;
	private const string AgeYearProcessedKey = "xuanjian.age_year_processed";

	private static readonly string[] YiDuiYingSingleGivenNames =
	{
		"衡", "宁", "昭", "玄", "清", "澄", "渊", "晏",
		"修", "远", "和", "明", "景", "川", "岚", "舟",
		"微", "真", "素", "行", "安", "言", "知", "临"
	};

	private static readonly string[] YiDuiYingDoubleGivenNames =
	{
		"清和", "景行", "怀瑾", "知白", "明夷", "元晦", "玄度", "云归",
		"守一", "澄心", "清越", "昭宁", "静川", "临渊", "望舒", "含章",
		"若虚", "知微", "安澜", "长宁", "清晏", "明远", "砚秋", "修远",
		"云岫", "青衡", "疏桐", "怀真", "允中", "行简", "知玄", "明澈",
		"景澄", "云舟", "清川", "昭衡", "怀素", "知止", "玄清", "景明",
		"云深", "清宁", "昭远", "明川", "守玄", "知行", "含光", "怀远",
		"景和", "清徽", "玄晏", "云衡", "昭清", "明修", "知远", "静和",
		"清源", "景玄", "怀清", "云昭", "玄宁", "知衡", "明和", "清远"
	};

	private static readonly string[] CompoundChineseSurnames =
	{
		"欧阳", "司马", "上官", "诸葛", "东方", "皇甫", "尉迟", "公孙",
		"慕容", "令狐", "司徒", "司空", "夏侯", "南宫", "长孙", "宇文",
		"独孤", "轩辕", "钟离", "端木", "申屠", "公羊", "公冶", "东郭",
		"南门", "西门", "百里", "呼延", "拓跋", "完颜"
	};

	private static readonly string[] RealmNameSuffixes =
	{
		"结璘仙", "金丹", "紫府", "筑基", "炼气", "胎息"
	};

	private static readonly string[] HighRealmTitleSuffixes =
	{
		"大真人", "真人", "玄君", "神君", "真君", "飞君", "龙君", "大龙王", "龙王"
	};

	internal static bool IsJieLinZhangTrait(string traitId)
	{
		return string.Equals(traitId, JieLinZhangTraitId, StringComparison.Ordinal);
	}

	internal static bool IsJieLinXian(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJieLinXian, out int value)
			&& value > 0;
	}

	internal static float GetCultivationSpeedMultiplier(Actor actor)
	{
		return IsYiDuiYingMirror(actor) || (actor?.hasTrait(YiDuiYingTraitId) ?? false)
			? YiDuiYingCultivationSpeedMultiplier
			: 1f;
	}

	internal static bool ReconcileYiDuiYingMirrorOnLoad(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingSourceActorId, out int sourceActorId)
			|| sourceActorId <= 0)
		{
			return false;
		}

		NormalizeLegacyJsonTokenValues(actor.data);
		EnsureYiDuiYingFreshAge(actor);

		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingInitialized, out int initialized)
			|| initialized <= 0)
		{
			SanitizeYiDuiYingMirror(actor);
		}
		else
		{
			EnsureTrait(actor, YiDuiYingTraitId);
		}

		EnsureYiDuiYingAptitudeFloor(actor);

		int mirrorActorId = SafeActorId(actor);
		Actor source = null;
		if (TryResolveActor(sourceActorId, out source)
			&& source?.data != null
			&& (!XjActorAccessor.TryGetInt(source, XjActorDataKeys.XjYiDuiYingMirrorActorId, out int canonicalMirrorId)
				|| canonicalMirrorId <= 0))
		{
			XjActorAccessor.SetInt(source, XjActorDataKeys.XjYiDuiYingMirrorActorId, mirrorActorId);
		}
		LinkYiDuiYingToSourceFamily(source, actor, sourceActorId);

		EnsureYiDuiYingMirrorName(actor, source);

		// 返回 false：仪对影完成一次性净化后，继续进入正常修炼、家族与年度流水线。
		return false;
	}

	internal static void PrepareYiDuiYingMirrorsForSave()
	{
		IReadOnlyList<Actor> units = XjWorldBootstrapLane.HasPending
			? World.world?.units?.getSimpleList()
			: XjScheduler.GetKnownActorsSnapshot();
		if (units == null)
		{
			return;
		}

		for (int i = 0; i < units.Count; i++)
		{
			Actor actor = units[i];
			if (IsYiDuiYingMirror(actor))
			{
				EnsureYiDuiYingMirrorName(actor);
				NormalizeLegacyJsonTokenValues(actor.data);
			}
		}
	}

	internal static void TickActor(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		if (IsYiDuiYingMirror(actor))
		{
			EnsureYiDuiYingMirrorName(actor);
		}

		if (IsJieLinXian(actor))
		{
			EnsureTrait(actor, JieLinZhangTraitId);
			XjJieLinXianRegistry.ReconcileLiveActor(actor);
			XjRealmTitleApplyService.EnsureJieLinTitle(actor);
		}
		else if (string.Equals(snapshot.RealmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			XjRealmTitleApplyService.EnsureJinDanTitle(actor, snapshot.DaoTu);
		}

		XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
		EnsureUnlockedTraitMarkers(actor, snapshot, xianJiState);
		TryCreateYiDuiYingMirror(actor, snapshot, xianJiState);
		TryCastYeTianMen(actor, snapshot, xianJiState, currentYear);
	}

	internal static bool TryResolveJieLinXianOnJinDanFailure(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		in XjXianJiState xianJiState,
		int currentYear)
	{
		// 结璘仙只接受太阴本位上位、下位与相邻神通；任何“其他”神通
		// 都不能借太阴正位转结璘，求金失败后必须直接死亡。
		if (actor?.data == null
			|| !string.Equals(snapshot.DaoTu, TaiYinDaoTu, StringComparison.Ordinal)
			|| !HasOnlyJieLinCompatibleXianJi(xianJiState)
			|| !XjGuoWeiRegistry.HasManifestedZhengWeiDaoTu(TaiYinDaoTu)
			|| !XjJieLinXianRegistry.HasCapacityFor(actor))
		{
			return false;
		}

		string jinXing = XjJinXingCalculator.Calculate(TaiYinDaoTu, GetActorId(actor));
		if (!XjCultivationStateTransitions.TrySetDaoTu(actor, TaiYinDaoTu, false)
			|| !XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.JinDan, false))
		{
			return false;
		}

		int safeYear = currentYear < 0 ? 0 : currentYear;
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, jinXing);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, safeYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXian, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXianYear, safeYear);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, "结璘仙");
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int yiXiang) || yiXiang <= 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang,
				XjDeterministicHash.PositiveIndex(GetActorId(actor) + safeYear, "jielinxian_yixiang", 300) + 1);
		}

		if (!XjJieLinXianRegistry.TryRegister(actor, safeYear))
		{
			// 容量检查与注册之间理论上不会变化；防御性失败必须完整回滚，
			// 避免留下“紫府境但仍带结璘/金丹字段”的半状态。
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanJinXing, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailedState, string.Empty);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanLastAttemptYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanSuccessYear, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJinDanYiXiang, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXian, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjJieLinXianYear, 0);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, string.Empty);
			XjCultivationStateTransitions.TrySetRealm(actor, XjRealmIds.ZiFu, true);
			return false;
		}

		XjYinSiTraitLifecycle.EnsureRemovedFromJinDan(actor);
		EnsureTrait(actor, JieLinZhangTraitId);
		XjRealmPromotionHelper.ApplyCommonPostRealmWrite(actor, XjRealmIds.JinDan, safeYear);
		XjRealmTitleApplyService.ApplyOnJieLinPromotion(actor);

		int activeCount = XjJieLinXianRegistry.ActiveCount;
		XjChronicleWriter.RecordJieLinSucceeded(actor, safeYear, activeCount);
		XjThreeBookWriter.RecordJieLinSucceeded(actor, safeYear, activeCount);
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			XjAnnouncementText.BuildJieLinPromotion(actor, activeCount));
		XjFaBaoAcquisition.TryGrantOnJieLinSuccess(actor, safeYear);
		return true;
	}

	private static bool HasOnlyJieLinCompatibleXianJi(in XjXianJiState state)
	{
		if (!state.Found || state.Count != XjXianJiState.MaxCount || state.Ids == null)
		{
			return false;
		}

		for (int i = 0; i < state.Ids.Length && i < XjXianJiState.MaxCount; i++)
		{
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(TaiYinDaoTu, state.Ids[i]);
			if (kind == XjXianJiPoolKind.Other)
			{
				return false;
			}
		}
		return true;
	}

	private static void EnsureUnlockedTraitMarkers(Actor actor, in XjActorCultivationSnapshot snapshot, in XjXianJiState xianJiState)
	{
		_ = snapshot;
		_ = xianJiState;

		// 仪对影特质只属于生成出的独立分身。清理旧版本误加在本体上的标记，
		// 但不再因本体换道途或失去仙基而删除、重置已有分身。
		if (!IsYiDuiYingMirror(actor) && actor.hasTrait(YiDuiYingTraitId))
		{
			actor.removeTrait(YiDuiYingTraitId);
		}

		// 谒天门已移除特质依赖，仅由道途+仙基驱动
	}

	private static void TryCreateYiDuiYingMirror(Actor actor, in XjActorCultivationSnapshot snapshot, in XjXianJiState xianJiState)
	{
		if (XjYinSiTraitLifecycle.IsYinSi(actor)
			|| !string.Equals(snapshot.DaoTu, TaiYinDaoTu, StringComparison.Ordinal)
			|| actor.hasTrait(YiDuiYingTraitId)
			|| IsYiDuiYingMirror(actor)
			|| !HasXianJi(xianJiState, YiDuiYingXianJi))
		{
			return;
		}

		// 镜影存活 → 不可使用；镜影死亡 → 触发10秒冷却
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingMirrorActorId, out int mirrorId) && mirrorId > 0)
		{
			if (TryResolveActor(mirrorId, out Actor m) && XjSafeCore.IsAliveActor(m))
				return;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYiDuiYingMirrorActorId, 0);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjYiDuiYingCooldownEndTime, Time.unscaledTime + 10f);
		}

		// 冷却检查
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.XjYiDuiYingCooldownEndTime, out float cooldownEnd);
		if (cooldownEnd > 0f && Time.unscaledTime < cooldownEnd)
			return;

		WorldTile origin = ((BaseSimObject)actor).current_tile;
		if (origin == null || (UnityEngine.Object)(object)World.world == (UnityEngine.Object)null || World.world.units == null)
		{
			return;
		}

		WorldTile tile = ResolveNearbyTile(origin);
		if (tile == null)
		{
			return;
		}

		int actorId = SafeActorId(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYiDuiYingMirrorActorId, 0);

		// A mirror is a new live actor. Do not rebuild it through loadObject because copied
		// save fields can retain stale zone, city and AI container identifiers.
		Actor mirror = World.world.units.spawnNewUnit(actor.data.asset_id, tile, false, false, 0f, null, false, true);
		if (mirror?.data == null)
		{
			return;
		}
		mirror.setCity(null);
		if (mirror.isKingdomCiv() && mirror.kingdom?.data == null)
		{
			XjDengMingShiSpawnSafety.TryRemoveInvalidActor(mirror);
			return;
		}
		SanitizeYiDuiYingMirror(mirror);
		ResetYiDuiYingMirrorAge(mirror);
		CopyYiDuiYingLineageState(actor, mirror);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingSourceActorId, actorId);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingMirrorActorId, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingInitialized, 1);
		EnsureTrait(mirror, YiDuiYingTraitId);
		LinkYiDuiYingToSourceFamily(actor, mirror, actorId);
		EnsureYiDuiYingAptitudeFloor(mirror);
		XjScheduler.RegisterActor(mirror);

		// 仪对影是独立角色：继承本体姓氏，但重新生成名字。
		EnsureYiDuiYingMirrorName(mirror, actor, force: true);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjYiDuiYingMirrorActorId, SafeActorId(mirror));
	}

	private static void TryCastYeTianMen(Actor actor, in XjActorCultivationSnapshot snapshot, in XjXianJiState xianJiState, int currentYear)
	{
		if (!string.Equals(snapshot.DaoTu, MingYangDaoTu, StringComparison.Ordinal)
			|| !HasXianJi(xianJiState, YeTianMenXianJi))
		{
			return;
		}

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.XjYeTianMenLastYear, out float lastCastTime);
		if (lastCastTime > 0f && Time.unscaledTime - lastCastTime < YeTianMenCooldownSeconds)
		{
			return;
		}

		WorldTile tile = ((BaseSimObject)actor).current_tile;
		if (tile == null)
		{
			return;
		}

		if (XjJinDanCombatApi.TryApplyTerrainEffect(tile, "SmashTerrain", 3, 0.8f, "YeTianMen", out _))
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjYeTianMenLastYear, Time.unscaledTime);
		}
	}

	private static bool HasXianJi(in XjXianJiState state, string id)
	{
		if (state.Ids == null || string.IsNullOrWhiteSpace(id))
		{
			return false;
		}

		for (int i = 0; i < state.Ids.Length; i++)
		{
			if (string.Equals(state.Ids[i], id, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static void EnsureTrait(Actor actor, string traitId)
	{
		if (actor?.data != null && !string.IsNullOrWhiteSpace(traitId) && !actor.hasTrait(traitId))
		{
			actor.addTrait(traitId, false);
		}
	}

	private static WorldTile ResolveNearbyTile(WorldTile origin)
	{
		if (origin == null)
		{
			return null;
		}

		int x = origin.x;
		int y = origin.y;
		int[,] offsets =
		{
			{ 2, 0 }, { -2, 0 }, { 0, 2 }, { 0, -2 },
			{ 2, 2 }, { -2, 2 }, { 2, -2 }, { -2, -2 },
			{ 3, 0 }, { 0, 3 }, { -3, 0 }, { 0, -3 }
		};

		for (int i = 0; i < offsets.GetLength(0); i++)
		{
			WorldTile tile = World.world.GetTileSimple(x + offsets[i, 0], y + offsets[i, 1]);
			if (tile != null)
			{
				return tile;
			}
		}

		return origin;
	}

	private static void CopyYiDuiYingLineageState(Actor source, Actor mirror)
	{
		if (source?.data == null || mirror?.data == null)
		{
			return;
		}

		CopyFloatActorKey(source, mirror, XjActorDataKeys.XjBloodlineQuality);
		CopyFloatActorKey(source, mirror, XjActorDataKeys.XjBloodlineConcentration);
		CopyIntActorKey(source, mirror, XjActorDataKeys.XjBloodlineGeneration);
		CopyStringActorKey(source, mirror, XjActorDataKeys.XjBloodlineOriginDaoTu);
		CopyIntActorKey(source, mirror, XjActorDataKeys.XjBloodlineExtraTalentInheritance);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjBloodlineIsAncestor, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjBloodlineApplied, 1);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjBloodlineAppliedYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjBloodlineSource, "YiDuiYingLineage");
		// 新角色只保留氏族血脉底子，不继承本体已经结算过的修炼资源标记。
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjBloodlineSeedInheritanceApplied, 0);
	}

	private static void LinkYiDuiYingToSourceFamily(Actor source, Actor mirror, long sourceActorId)
	{
		if (mirror?.data == null || sourceActorId <= 0L)
		{
			return;
		}

		if (source?.data != null && source.data.clan > 0L)
		{
			// 原生氏族标记只复制归属，不复制年龄、父母或其他角色状态。
			mirror.data.clan = source.data.clan;
		}

		if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(sourceActorId, out long familyStableId)
			&& familyStableId > 0L)
		{
			XjFamilyMemberIndex.Shared.RelinkActorToFamily(mirror, familyStableId);
		}
	}

	private static void CopyIntActorKey(Actor source, Actor target, string key)
	{
		if (XjActorAccessor.TryGetInt(source, key, out int value))
		{
			XjActorAccessor.SetInt(target, key, value);
		}
	}

	private static void CopyFloatActorKey(Actor source, Actor target, string key)
	{
		if (XjActorAccessor.TryGetFloat(source, key, out float value))
		{
			XjActorAccessor.SetFloat(target, key, value);
		}
	}

	private static void CopyStringActorKey(Actor source, Actor target, string key)
	{
		if (XjActorAccessor.TryGetString(source, key, out string value))
		{
			XjActorAccessor.SetString(target, key, value);
		}
	}

	private static void NormalizeLegacyJsonTokenValues(ActorData data)
	{
		if (data == null)
		{
			return;
		}

		for (Type type = data.GetType(); type != null && type != typeof(object); type = type.BaseType)
		{
			FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			for (int i = 0; i < fields.Length; i++)
			{
				FieldInfo field = fields[i];
				if (field.IsStatic || !typeof(IDictionary).IsAssignableFrom(field.FieldType))
				{
					continue;
				}

				IDictionary dictionary;
				try
				{
					dictionary = field.GetValue(data) as IDictionary;
				}
				catch
				{
					continue;
				}

				if (dictionary == null || dictionary.Count == 0)
				{
					continue;
				}

				List<object> keys = new List<object>();
				foreach (object key in dictionary.Keys)
				{
					keys.Add(key);
				}

				for (int keyIndex = 0; keyIndex < keys.Count; keyIndex++)
				{
					object key = keys[keyIndex];
					object value;
					try
					{
						value = dictionary[key];
						if (value is JValue scalar)
						{
							object normalized = ConvertLegacyJsonScalar(scalar);
							if (normalized == null)
							{
								dictionary.Remove(key);
							}
							else
							{
								dictionary[key] = normalized;
							}
						}
						else if (value is JToken)
						{
							dictionary.Remove(key);
						}
					}
					catch
					{
						// 存档前清理不得因为单个未知字段阻断整个保存流程。
					}
				}
			}
		}
	}

	private static object ConvertLegacyJsonScalar(JValue value)
	{
		if (value == null || value.Type == JTokenType.Null || value.Type == JTokenType.Undefined)
		{
			return null;
		}

		object raw = value.Value;
		if (raw == null)
		{
			return null;
		}

		try
		{
			switch (value.Type)
			{
				case JTokenType.Integer:
					long integer = Convert.ToInt64(raw);
					return integer >= int.MinValue && integer <= int.MaxValue ? (object)(int)integer : integer;
				case JTokenType.Float:
					return Convert.ToSingle(raw);
				case JTokenType.Boolean:
					return Convert.ToBoolean(raw);
				case JTokenType.String:
				case JTokenType.Guid:
				case JTokenType.Uri:
				case JTokenType.TimeSpan:
				case JTokenType.Date:
					return Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture);
				default:
					return raw is string || raw is bool || raw is int || raw is long || raw is float
						? raw
						: null;
			}
		}
		catch
		{
			return null;
		}
	}

	private static int SafeActorId(Actor actor)
	{
		if (actor?.data == null)
		{
			return 0;
		}

		long id = ((BaseSystemData)actor.data).id;
		return id <= 0L ? 0 : id > int.MaxValue ? int.MaxValue : (int)id;
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static void EnsureYiDuiYingMirrorName(Actor mirror, Actor knownSource = null, bool force = false)
	{
		if (!IsYiDuiYingMirror(mirror) || mirror?.data == null)
		{
			return;
		}

		if (!force
			&& XjActorAccessor.TryGetInt(mirror, XjActorDataKeys.XjYiDuiYingNameSchema, out int schema)
			&& schema >= YiDuiYingNameSchemaCurrent)
		{
			return;
		}

		XjActorAccessor.TryGetInt(mirror, XjActorDataKeys.XjYiDuiYingSourceActorId, out int sourceActorId);
		Actor source = knownSource;
		if (source?.data == null && sourceActorId > 0)
		{
			TryResolveActor(sourceActorId, out source);
		}

		string sourceBaseName;
		string surname;
		long sourceSeed;
		if (source?.data != null)
		{
			sourceBaseName = ResolveYiDuiYingSourceBaseName(source);
			surname = ResolveYiDuiYingSurname(source, sourceBaseName);
			sourceSeed = GetActorId(source);
		}
		else
		{
			sourceBaseName = ResolveYiDuiYingFallbackBaseName(mirror);
			surname = ResolveYiDuiYingSurname(mirror, sourceBaseName);
			sourceSeed = sourceActorId;
		}

		if (string.IsNullOrWhiteSpace(surname))
		{
			return;
		}

		string baseName = BuildYiDuiYingBaseName(
			surname,
			sourceBaseName,
			GetActorId(mirror),
			sourceSeed);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			return;
		}

		((BaseSystemData)mirror.data).set(ChineseFamilyNameKey, surname);
		mirror.setName(baseName, true);
		mirror.name = baseName;
		mirror.data.custom_name = true;
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjNameTitle, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjNameRealmDisplay, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingNameSchema, YiDuiYingNameSchemaCurrent);
		RestoreYiDuiYingOwnRealmDisplayName(mirror);
	}

	private static void RestoreYiDuiYingOwnRealmDisplayName(Actor mirror)
	{
		if (mirror?.data == null)
		{
			return;
		}

		if (IsJieLinXian(mirror))
		{
			XjRealmTitleApplyService.ApplyOnJieLinPromotion(mirror);
			return;
		}

		if (!XjActorAccessor.TryGetString(mirror, XjActorDataKeys.RealmId, out string realmId)
			|| string.IsNullOrWhiteSpace(realmId))
		{
			return;
		}

		XjActorAccessor.TryGetString(mirror, XjActorDataKeys.DaoTu, out string daoTu);
		XjRealmTitleApplyService.ApplyOnPromotion(mirror, realmId, daoTu);
	}

	private static string ResolveYiDuiYingSourceBaseName(Actor source)
	{
		if (source?.data == null)
		{
			return string.Empty;
		}

		if (XjActorAccessor.TryGetString(source, XjActorDataKeys.XjNameBase, out string storedBaseName)
			&& !string.IsNullOrWhiteSpace(storedBaseName))
		{
			string normalizedStored = NormalizeYiDuiYingBaseName(storedBaseName);
			if (!string.IsNullOrWhiteSpace(normalizedStored))
			{
				return normalizedStored;
			}
		}

		return NormalizeYiDuiYingBaseName(source.getName());
	}

	private static string ResolveYiDuiYingFallbackBaseName(Actor mirror)
	{
		if (mirror?.data == null)
		{
			return string.Empty;
		}

		if (XjActorAccessor.TryGetString(mirror, XjActorDataKeys.XjNameBase, out string storedBaseName)
			&& !string.IsNullOrWhiteSpace(storedBaseName))
		{
			string normalizedStored = NormalizeYiDuiYingBaseName(storedBaseName);
			if (!string.IsNullOrWhiteSpace(normalizedStored))
			{
				return normalizedStored;
			}
		}

		return NormalizeYiDuiYingBaseName(mirror.getName());
	}

	private static string NormalizeYiDuiYingBaseName(string rawName)
	{
		string text = (rawName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}

		if (text.StartsWith("「", StringComparison.Ordinal))
		{
			int bracketTitleEnd = text.IndexOf("」·", StringComparison.Ordinal);
			if (bracketTitleEnd >= 0 && bracketTitleEnd + 2 < text.Length)
			{
				text = text.Substring(bracketTitleEnd + 2).Trim();
			}
		}

		int titleSeparator = text.IndexOf('·');
		if (titleSeparator >= 0 && titleSeparator + 1 < text.Length)
		{
			text = text.Substring(titleSeparator + 1).Trim();
		}

		int realmSeparator = LastNameSeparatorIndex(text);
		if (realmSeparator > 0)
		{
			text = text.Substring(0, realmSeparator).Trim();
		}

		bool removedRealm;
		do
		{
			removedRealm = false;
			for (int i = 0; i < RealmNameSuffixes.Length; i++)
			{
				string suffix = RealmNameSuffixes[i];
				if (text.EndsWith(suffix, StringComparison.Ordinal) && text.Length > suffix.Length)
				{
					text = text.Substring(0, text.Length - suffix.Length).Trim();
					removedRealm = true;
					break;
				}
			}
		}
		while (removedRealm);

		for (int i = 0; i < HighRealmTitleSuffixes.Length; i++)
		{
			string suffix = HighRealmTitleSuffixes[i];
			int titleEnd = text.IndexOf(suffix, StringComparison.Ordinal);
			if (titleEnd >= 0 && titleEnd + suffix.Length < text.Length)
			{
				text = text.Substring(titleEnd + suffix.Length).Trim();
				break;
			}
		}

		return text.Replace(" ", string.Empty).Trim();
	}

	private static int LastNameSeparatorIndex(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return -1;
		}

		int index = text.LastIndexOf("-", StringComparison.Ordinal);
		index = Math.Max(index, text.LastIndexOf('―'));
		index = Math.Max(index, text.LastIndexOf('—'));
		return index;
	}

	private static string ResolveYiDuiYingSurname(Actor actor, string normalizedBaseName)
	{
		if (actor?.data != null)
		{
			((BaseSystemData)actor.data).get(ChineseFamilyNameKey, out string storedSurname, string.Empty);
			storedSurname = (storedSurname ?? string.Empty).Trim();
			if (!string.IsNullOrWhiteSpace(storedSurname))
			{
				return storedSurname;
			}
		}

		string baseName = (normalizedBaseName ?? string.Empty).Trim();
		for (int i = 0; i < CompoundChineseSurnames.Length; i++)
		{
			string compound = CompoundChineseSurnames[i];
			if (baseName.StartsWith(compound, StringComparison.Ordinal))
			{
				return compound;
			}
		}

		return baseName.Length > 0 ? baseName.Substring(0, 1) : string.Empty;
	}

	private static string BuildYiDuiYingBaseName(
		string surname,
		string sourceBaseName,
		long mirrorActorId,
		long sourceActorId)
	{
		string safeSurname = (surname ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(safeSurname))
		{
			return string.Empty;
		}

		string normalizedSource = NormalizeYiDuiYingBaseName(sourceBaseName);
		string sourceGivenName = normalizedSource.StartsWith(safeSurname, StringComparison.Ordinal)
			? normalizedSource.Substring(safeSurname.Length)
			: normalizedSource;
		string[] pool = sourceGivenName.Length == 1
			? YiDuiYingSingleGivenNames
			: YiDuiYingDoubleGivenNames;
		long seed = mirrorActorId + sourceActorId;
		int start = XjDeterministicHash.PositiveIndex(seed, "yiduiying.given_name", pool.Length);
		for (int offset = 0; offset < pool.Length; offset++)
		{
			string candidate = pool[(start + offset) % pool.Length];
			if (!string.Equals(candidate, sourceGivenName, StringComparison.Ordinal))
			{
				return safeSurname + candidate;
			}
		}

		return safeSurname + (sourceGivenName.Length == 1 ? "宁" : "清影");
	}

	private static bool IsYiDuiYingMirror(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingSourceActorId, out int sourceActorId)
			&& sourceActorId > 0;
	}

	private static void EnsureYiDuiYingAptitudeFloor(Actor mirror)
	{
		if (!IsYiDuiYingMirror(mirror) || mirror?.data == null)
		{
			return;
		}

		int ageYear = (int)Math.Floor(Math.Max(0f, mirror.getAge()));
		if (ageYear < 5)
		{
			return;
		}

		XjActorAccessor.TryGetInt(mirror, XjActorDataKeys.XjZz, out int currentAptitude);
		if (currentAptitude >= 4)
		{
			return;
		}

		int aptitude = XjAptitudeRuleEvaluator.ResolveYiDuiYingAptitude(mirror);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZz, aptitude);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjAptitudeEffectRules.EnsureBaseValuesBoundToAptitude(mirror, aptitude);
		XjAptitudeEffectRules.ApplyPrimaryAptitudeEffect(mirror, aptitude);
		XjVisibleTraitSync.SyncAptitudeTrait(mirror, aptitude);
		XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(mirror, out _);
		XjCultivationSeed.RefreshChuShenForCultivationState(mirror);
		XjGongFaProgression.EnsureEntryGongFa(mirror, XjActorCultivationSnapshotBuilder.Build(mirror));
	}

	private static void SanitizeYiDuiYingMirror(Actor mirror)
	{
		if (mirror?.data == null)
		{
			return;
		}

		XjVisibleTraitSync.ClearUnsupportedCultivationState(mirror);
		XjVisibleTraitSync.ClearCultivationDerivedNativeTraits(mirror);
		XjFaBaoEquipmentSync.ClearEquippedFaBaoForCultivationReset(mirror);

		// 核心修炼从零开始。
		XjActorAccessor.SetString(mirror, XjActorDataKeys.RealmId, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.RealmEnteredYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZhuJiEnteredYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZiFuEnteredYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.DaoTu, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZz, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZzOverlayMask, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZzCheckedAge5, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZzEffectApplied, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZz9LastPenaltyYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.ChuShen, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.ChuShenSpecial, 0);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.ZhenYuan, 0f);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.MingShu, 0f);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.MingShuCongenital, 0f);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.MingShuAcquired, 0f);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.HuiGuang, 0f);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.RealmBreakthroughLastAttemptYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.RealmBreakthroughFailureCount, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.RealmBreakthroughLastResult, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.RealmBonusAppliedMask, 0);

		// 功法、仙基、神通、求金法与金丹链全部独立重修。
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaName, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaGrade, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaStage, 0);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.XjGongFaProgress, 0f);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaDaoTu, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaSource, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaCollectionVersion, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaCollectionJson, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaLastExecutionYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaClockTargetGrade, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaClockEligibilityYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaGrade4NextAttemptYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaGrade5NextAttemptYear, 0);

		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaGrade5PromotionLastYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaHighPromotionFailureCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjGongFaHighPromotionLastYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiCount, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjXianJiIds, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiLastYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiLastExecutionYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiClockTargetCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiClockEligibilityYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiLastLogicalAttemptYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiFailureCount, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjXianJiProjectId, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiProjectTargetCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiProjectCompleteYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjXianJiProjectLastProposalYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjShenTongIds, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjQiuJinFaName, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjQiuJinFaSourceGongFaName, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjQiuJinFaSourceDaoTu, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjQiuJinFaBoundAuthority, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaReady, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaLastYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaLastExecutionYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaEligibilityYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaLastLogicalAttemptYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjQiuJinFaFailureCount, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjQiuJinFaLastFailureReason, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjJinDanJinXing, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjJinDanResidualJinXing, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjJinDanResidualJinXingSource, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanResidualWarehouseMigrated, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjJinDanGuoWei, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjJinDanFailedState, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanLastAttemptYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanSuccessYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanSuccessEventYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanSuccessEventSchema, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJinDanYiXiang, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZiFuLingWuNextOpportunityYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZiFuLingWuLastExecutionYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJieLinXian, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjJieLinXianYear, 0);

		// 不复制采气、法宝与宗门身份；家族血缘数据保留，使其进入同一家族账本。
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjCaiQiFaName, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjCaiQiFaDaoTu, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjCaiQiFaSourcePlace, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjCaiQiFaSourceYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.CaiQiCompleted, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiPlaceTypeId, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiBranchId, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiSiteName, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiResultType, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiStatus, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiFailureReason, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.CaiQiResourceId, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.CaiQiResourceCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.CaiQiGatheredCount, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.CaiQiConsumedForBreakthrough, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.LianQiByZaQi, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.LastCaiQiYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.NextCaiQiYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoId, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoName, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoDaoTu, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoClass, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoKind, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoRole, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoAffixes, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoDescription, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjFaBaoSource, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjFaBaoYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjZongMenId, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjZongMenName, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZongMenRank, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZongMenJoinYear, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjZongMenRole, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZongMenPeakId, 0);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjZongMenPeakName, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjZongMenFoundedCityId, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjZongMenFoundedYear, 0);

		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjNameTitle, string.Empty);
		XjActorAccessor.SetString(mirror, XjActorDataKeys.XjNameRealmDisplay, string.Empty);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingMirrorActorId, 0);
		XjActorAccessor.SetFloat(mirror, XjActorDataKeys.XjYiDuiYingCooldownEndTime, 0f);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjLastCultivationYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualActiveYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualLatestRequestedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualStage, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualLastCompletedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualSecondaryActiveYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualSecondaryLatestRequestedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualSecondaryLastCompletedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualMaintenanceFromYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualMaintenanceActiveYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualMaintenanceLatestRequestedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualMaintenanceStage, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjAnnualMaintenanceLastCompletedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjWeaponArtActivatedYear, 0);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjCraftActivatedYear, 0);

		if (mirror.hasTrait(JieLinZhangTraitId)) mirror.removeTrait(JieLinZhangTraitId);
		if (mirror.hasTrait(YeTianMenTraitId)) mirror.removeTrait(YeTianMenTraitId);
		ClearSavedCultivationTraits(mirror.data.saved_traits);
		EnsureTrait(mirror, YiDuiYingTraitId);
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingInitialized, 1);
		mirror.setStatsDirty();
	}

	private static void ResetYiDuiYingMirrorAge(Actor mirror)
	{
		if (mirror?.data == null)
		{
			return;
		}

		try
		{
			if (World.world != null)
			{
				mirror.data.created_time = World.world.getCurWorldTime();
			}
		}
		catch
		{
		}

		mirror.data.age_overgrowth = YiDuiYingInitialAge;
		TrySetNumericMember(mirror.data, "age", YiDuiYingInitialAge);
		try
		{
			float actualAge = mirror.getAge();
			if (!float.IsNaN(actualAge) && !float.IsInfinity(actualAge))
			{
				float correction = YiDuiYingInitialAge - actualAge;
				if (Math.Abs(correction) > 0.01f)
				{
					mirror.data.age_overgrowth = Math.Max(0, mirror.data.age_overgrowth + Mathf.RoundToInt(correction));
					TrySetNumericMember(mirror.data, "age", YiDuiYingInitialAge);
				}
			}
		}
		catch
		{
		}
		TrySetNumericMember(mirror.data, "death_time", 0f);
		TrySetNumericMember(mirror.data, "time_to_die", 0f);
		TrySetBooleanMember(mirror.data, "dead", false);
		TrySetBooleanMember(mirror.data, "is_dead", false);
		TrySetBooleanMember(mirror.data, "removed", false);
		if (mirror.data is BaseSystemData bd)
		{
			bd.set(AgeYearProcessedKey, YiDuiYingInitialAge - 1);
		}
		XjActorAccessor.SetInt(mirror, XjActorDataKeys.XjYiDuiYingFreshAgeInitialized, 1);
	}

	private static void EnsureYiDuiYingFreshAge(Actor mirror)
	{
		if (mirror?.data == null
			|| !IsYiDuiYingMirror(mirror)
			|| XjActorAccessor.TryGetInt(mirror, XjActorDataKeys.XjYiDuiYingFreshAgeInitialized, out int initialized)
				&& initialized > 0)
		{
			return;
		}

		// 旧版仪对影可能沿用了本体年龄。只对没有新生年龄标记的角色修复一次，
		// 之后按普通独立角色自然增长，不会每次读档都重置为十八岁。
		ResetYiDuiYingMirrorAge(mirror);
	}

	private static void TrySetNumericMember(object target, string memberName, float value)
	{
		if (target == null || string.IsNullOrWhiteSpace(memberName))
		{
			return;
		}

		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = target.GetType();
		try
		{
			FieldInfo field = type.GetField(memberName, Flags);
			if (field != null)
			{
				TrySetConvertedValue(field.FieldType, converted => field.SetValue(target, converted), value);
				return;
			}

			PropertyInfo property = type.GetProperty(memberName, Flags);
			if (property?.CanWrite == true)
			{
				TrySetConvertedValue(property.PropertyType, converted => property.SetValue(target, converted, null), value);
			}
		}
		catch
		{
		}
	}

	private static void TrySetBooleanMember(object target, string memberName, bool value)
	{
		if (target == null || string.IsNullOrWhiteSpace(memberName))
		{
			return;
		}

		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		Type type = target.GetType();
		try
		{
			FieldInfo field = type.GetField(memberName, Flags);
			if (field != null && field.FieldType == typeof(bool))
			{
				field.SetValue(target, value);
				return;
			}

			PropertyInfo property = type.GetProperty(memberName, Flags);
			if (property?.CanWrite == true && property.PropertyType == typeof(bool))
			{
				property.SetValue(target, value, null);
			}
		}
		catch
		{
		}
	}

	private static void TrySetConvertedValue(Type valueType, Action<object> setter, float value)
	{
		if (valueType == null || setter == null)
		{
			return;
		}

		if (valueType == typeof(int)) setter((int)Math.Round(value));
		else if (valueType == typeof(long)) setter((long)Math.Round(value));
		else if (valueType == typeof(float)) setter(value);
		else if (valueType == typeof(double)) setter((double)value);
		else if (valueType == typeof(short)) setter((short)Math.Round(value));
		else if (valueType == typeof(byte)) setter((byte)Mathf.Clamp((int)Math.Round(value), byte.MinValue, byte.MaxValue));
	}

	private static void ClearSavedCultivationTraits(List<string> savedTraits)
	{
		if (savedTraits == null)
		{
			return;
		}

		savedTraits.RemoveAll(traitId =>
		{
			if (string.IsNullOrWhiteSpace(traitId))
			{
				return false;
			}

			return traitId.StartsWith("XjRealm", StringComparison.Ordinal)
				|| traitId.StartsWith("XjZz", StringComparison.Ordinal)
				|| traitId.StartsWith("ChuShen", StringComparison.Ordinal)
				|| string.Equals(traitId, YiDuiYingTraitId, StringComparison.Ordinal)
				|| string.Equals(traitId, JieLinZhangTraitId, StringComparison.Ordinal)
				|| string.Equals(traitId, YeTianMenTraitId, StringComparison.Ordinal);
		});
	}

	private static bool TryResolveActor(int actorId, out Actor actor)
	{
		if (actorId <= 0)
		{
			actor = null;
			return false;
		}

		// ResolveKnownOrWorld 先查常驻索引，再按单位 ID 读取世界对象；
		// 既支持新生成后已注册的分身，也兼容旧存档加载顺序。
		return XjActorRegistry.ResolveKnownOrWorld(actorId, out actor);
	}
}
