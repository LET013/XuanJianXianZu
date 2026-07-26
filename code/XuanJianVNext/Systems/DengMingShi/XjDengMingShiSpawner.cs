using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiSpawner
{
	private const int RespawnAge = 18;
	private const string AgeYearProcessedKey = "xuanjian.age_year_processed";

	/// <summary>PlaceMode 调用 — 直接传入 tile 执行放置。</summary>
	internal static XjDengMingShiCommandResult TryPlaceRecordInternal(string recordId, WorldTile tile)
	{
		if (!XjDengMingShiRegistry.TryGet(recordId, out XjDengMingShiRecord record))
			return new XjDengMingShiCommandResult(false, "未找到记录。");

		return TryCreateActorFromRecord(record, tile, out _)
			? new XjDengMingShiCommandResult(true, "已放置。")
			: new XjDengMingShiCommandResult(false, "放置失败。");
	}

	internal static XjDengMingShiCommandResult TryPlaceRecord(string recordId)
	{
		if (!XjDengMingShiRegistry.TryGet(recordId, out XjDengMingShiRecord record))
		{
			return new XjDengMingShiCommandResult(false, "未找到记录。");
		}

		WorldTile tile = TryGetTargetTile();
		if (tile == null)
		{
			return new XjDengMingShiCommandResult(false, "请先在地图上选择放置位置。");
		}

		return TryCreateActorFromRecord(record, tile, out Actor created)
			? new XjDengMingShiCommandResult(true, "已放置：" + SafeActorName(created, record.ActorName))
			: new XjDengMingShiCommandResult(false, "放置失败。");
	}

	internal static bool TryCreateActorFromRecord(XjDengMingShiRecord record, WorldTile targetTile, out Actor actor)
	{
		actor = null;
		ActorManager actorManager = TryGetActorManager();
		if (record == null || targetTile == null || actorManager == null)
		{
			return false;
		}

		string raceId = Normalize(record.RaceId);
		if (string.IsNullOrWhiteSpace(raceId) || AssetManager.actor_library?.get(raceId) == null)
		{
			return false;
		}

		try
		{
			// Use the native live-spawn path. loadObject is intended for save deserialization
			// and can leave a newly placed actor detached from simulation containers.
			actor = actorManager.spawnNewUnit(raceId, targetTile, false, false, 0f, null, false, true);
			if (actor?.data == null)
			{
				return false;
			}
			ResetPlacedActorAge(actor);
			actor.data.name = string.IsNullOrWhiteSpace(record.ActorName) ? "登名者" : record.ActorName.Trim();
			actor.data.custom_name = true;
			if (!XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				actor = null;
				return false;
			}

			RestoreVisibleTraits(actor, record.TraitIds);
			RestoreVNextState(actor, record);
			ResetPlacedActorAge(actor);
			actor.clearOldPath();
			actor.stopMovement();
			actor.clearTraitCache();
			actor.setStatsDirty();
			XjDengMingShiPostPlacement.Reconcile(actor);
			XjDengMingShiRegistry.MarkPlaced(record.RecordId, GetCurrentYear());
			return true;
		}
		catch
		{
			actor = null;
			return false;
		}
	}

	private static void RestoreVisibleTraits(Actor actor, IReadOnlyList<string> traitIds)
	{
		if (actor?.data == null || traitIds == null)
		{
			return;
		}

		for (int i = 0; i < traitIds.Count; i++)
		{
			string traitId = Normalize(traitIds[i]);
			if (string.IsNullOrWhiteSpace(traitId) || AssetManager.traits?.get(traitId) == null || actor.hasTrait(traitId))
			{
				continue;
			}

			actor.addTrait(traitId, false);
		}
	}

	private static void RestoreVNextState(Actor actor, XjDengMingShiRecord record)
	{
		if (actor?.data == null || record == null)
		{
			return;
		}

		string realmId = ResolveRealmId(record);
		// 登名石必须按自然身份链恢复：资质 → 道途 → 境界。
		// 旧顺序在无现成道途的记录上会让 TrySetRealm 静默失败。
		if (record.XjZz > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, record.XjZz);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		}
		if (string.Equals(record.DaoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal))
		{
			// 记录中已存在青宣道途本身就是完成解锁的证据；否则统一门禁会拒绝恢复。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);
		}
		if (!string.IsNullOrWhiteSpace(record.DaoTu))
		{
			XjCultivationStateTransitions.TrySetDaoTu(actor, record.DaoTu, false);
		}
		if (!string.IsNullOrWhiteSpace(realmId))
		{
			XjCultivationStateTransitions.TrySetRealm(actor, realmId, false);
		}

		if (record.GongFaCollectionVersion > 0 || !string.IsNullOrWhiteSpace(record.XianJiIds))
		{
			// Versioned records restore an intentionally empty XianJi set as empty too.
			XjXianJiAccessor.RestoreSnapshot(actor, record.XianJiIds, record.XianJiLastYear);
		}

		if (!string.IsNullOrWhiteSpace(record.GongFaName) && record.GongFaGrade > 0)
		{
			XjGongFaAccessor.WriteState(actor, new XjGongFaState(
				true,
				record.GongFaName,
				record.GongFaGrade,
				0,
				0f,
				string.IsNullOrWhiteSpace(record.GongFaDaoTu) ? record.DaoTu : record.GongFaDaoTu,
				record.GongFaGrade > XjGongFaAccessor.MaxActiveGrade,
				"DengMingShi"));
		}
		if (!string.IsNullOrWhiteSpace(record.GongFaCollectionJson))
		{
			XjActorGongFaCollection.TryRestoreSerialized(
				actor,
				record.GongFaCollectionVersion,
				record.GongFaCollectionJson,
				"DengMingShiRegistry");
		}
		else
		{
			XjActorGongFaCollection.ReconcileWithActor(actor, "DengMingShiLegacyRecord");
		}

		if (!string.IsNullOrWhiteSpace(record.CaiQiFaName) && !string.IsNullOrWhiteSpace(record.CaiQiFaDaoTu))
		{
			XjCaiQiFaAccessor.WriteState(actor, new XjCaiQiFaState(
				true,
				record.CaiQiFaName,
				record.CaiQiFaDaoTu,
				record.CaiQiFaSourcePlace,
				record.CaiQiFaSourceYear,
				"DengMingShi"));
		}

		if (!string.IsNullOrWhiteSpace(record.QiuJinFa))
		{
			string boundAuthority = record.QiuJinFaBoundAuthority;
			if (string.IsNullOrWhiteSpace(boundAuthority))
			{
				boundAuthority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
					record.QiuJinFaSourceDaoTu,
					record.QiuJinFa,
					record.QiuJinFaSourceGongFaName);
			}
			XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
				true,
				record.QiuJinFa,
				record.QiuJinFaSourceGongFaName,
				record.QiuJinFaSourceGongFaGrade,
				record.QiuJinFaSourceDaoTu,
				true,
				record.QiuJinFaLastYear,
				"DengMingShi",
				boundAuthority));
		}

		if (!string.IsNullOrWhiteSpace(record.JinDan) && !string.IsNullOrWhiteSpace(record.GuoWei))
		{
			int successYear = record.JinDanSuccessYear > 0 ? record.JinDanSuccessYear : GetCurrentYear();
			long actorId = ((BaseSystemData)actor.data).id;
			string jinDanDaoTu = string.IsNullOrWhiteSpace(record.DaoTu) ? "未定" : record.DaoTu;
			string guoWei = record.GuoWei;
			if (!XjGuoWeiRegistry.TryClaim(actor, jinDanDaoTu, record.JinDan, guoWei, successYear))
			{
				string preferredType = XjGuoWeiRegistry.ResolveTypeFromName(record.GuoWei);
				if (!XjGuoWeiRegistry.TryResolveAvailableGuoWei(
					jinDanDaoTu,
					preferredType,
					actorId,
					actorId + successYear,
					true,
					out _,
					out guoWei)
					|| !XjGuoWeiRegistry.TryClaim(actor, jinDanDaoTu, record.JinDan, guoWei, successYear))
				{
					guoWei = string.Empty;
				}
			}

			if (!string.IsNullOrWhiteSpace(guoWei))
			{
				XjJinDanAccessor.WriteSuccess(actor, record.JinDan, guoWei, successYear);
				XjGuoWeiQuanBingLifecycle.InitializeOnJinDan(actor, jinDanDaoTu, guoWei, successYear);
			}
		}

		if (!string.IsNullOrWhiteSpace(record.ShenDanGuoWei)
			&& record.ShenDanAnchorActorId > 0L
			&& record.ShenDanYear > 0)
		{
			XjShenDanAccessor.WriteSuccess(
				actor,
				record.ShenDanGuoWei,
				record.ShenDanAnchorActorId,
				record.ShenDanAnchorName,
				record.ShenDanYear);
			XjShenDanRegistry.Register(((BaseSystemData)actor.data).id, record.ShenDanAnchorActorId);
		}

		if (!string.IsNullOrWhiteSpace(record.FaBaoId) && !string.IsNullOrWhiteSpace(record.FaBaoName))
		{
			XjFaBaoAccessor.WriteState(actor, new XjFaBaoState(
				true,
				record.FaBaoId,
				record.FaBaoName,
				record.FaBaoDaoTu,
				record.FaBaoClass,
				record.FaBaoSource,
				record.FaBaoYear,
				"DengMingShi"));
		}

		XjVisibleTraitSync.SyncCultivationTraits(actor);
	}

	private static void ResetPlacedActorAge(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		try
		{
			if (World.world != null)
			{
				actor.data.created_time = World.world.getCurWorldTime();
			}
		}
		catch
		{
		}

		actor.data.age_overgrowth = RespawnAge;
		TrySetNumericMember(actor.data, "age", RespawnAge);
		TrySetNumericMember(actor.data, "death_time", 0f);
		TrySetNumericMember(actor.data, "time_to_die", 0f);
		TrySetBooleanMember(actor.data, "dead", false);
		TrySetBooleanMember(actor.data, "is_dead", false);
		TrySetBooleanMember(actor.data, "removed", false);
		if (actor.data is BaseSystemData bd)
		{
			bd.set(AgeYearProcessedKey, RespawnAge - 1);
		}
	}

	private static void TrySetNumericMember(object target, string memberName, float value)
	{
		if (target == null || string.IsNullOrWhiteSpace(memberName))
		{
			return;
		}

		const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance
			| System.Reflection.BindingFlags.Public
			| System.Reflection.BindingFlags.NonPublic;
		Type type = target.GetType();
		try
		{
			System.Reflection.FieldInfo field = type.GetField(memberName, Flags);
			if (field != null)
			{
				TrySetConvertedValue(field.FieldType, converted => field.SetValue(target, converted), value);
				return;
			}

			System.Reflection.PropertyInfo property = type.GetProperty(memberName, Flags);
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

		const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance
			| System.Reflection.BindingFlags.Public
			| System.Reflection.BindingFlags.NonPublic;
		Type type = target.GetType();
		try
		{
			System.Reflection.FieldInfo field = type.GetField(memberName, Flags);
			if (field != null && field.FieldType == typeof(bool))
			{
				field.SetValue(target, value);
				return;
			}

			System.Reflection.PropertyInfo property = type.GetProperty(memberName, Flags);
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
		else if (valueType == typeof(byte)) setter((byte)Math.Max(byte.MinValue, Math.Min(byte.MaxValue, (int)Math.Round(value))));
	}

	private static WorldTile TryGetTargetTile()
	{
		try
		{
			return World.world?.getMouseTilePos();
		}
		catch
		{
			return null;
		}
	}

	private static ActorManager TryGetActorManager()
	{
		try
		{
			MapBox world = World.world;
			return world == null ? null : world.units;
		}
		catch
		{
			return null;
		}
	}

	private static string ResolveRealmId(XjDengMingShiRecord record)
	{
		string realmId = Normalize(record.RealmId);
		if (!string.IsNullOrEmpty(realmId))
		{
			return realmId;
		}

		string realm = Normalize(record.Realm);
		if (realm == "胎息") return XjRealmIds.TaiXi;
		if (realm == "炼气" || realm == "\u7ec3\u6c14") return XjRealmIds.LianQi;
		if (realm == "筑基") return XjRealmIds.ZhuJi;
		if (realm == "紫府") return XjRealmIds.ZiFu;
		if (realm == "金丹") return XjRealmIds.JinDan;
		return string.Empty;
	}

	private static int GetCurrentYear()
	{
		try
		{
			return World.world == null ? 0 : (int)Math.Floor(Math.Max(0.0, World.world.getCurWorldTime()));
		}
		catch
		{
			return 0;
		}
	}

	private static string SafeActorName(Actor actor, string defaultName)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? (string.IsNullOrWhiteSpace(defaultName) ? "登名者" : defaultName.Trim()) : name.Trim();
		}
		catch
		{
			return string.IsNullOrWhiteSpace(defaultName) ? "登名者" : defaultName.Trim();
		}
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}
}
