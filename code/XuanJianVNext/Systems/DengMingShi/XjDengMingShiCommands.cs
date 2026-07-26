using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ActorInfo;

namespace XuanJianVNext.Systems.DengMingShi;

internal readonly struct XjDengMingShiCommandResult
{
	internal readonly bool Success;
	internal readonly string Message;

	internal XjDengMingShiCommandResult(bool success, string message)
	{
		Success = success;
		Message = message ?? string.Empty;
	}
}

internal static class XjDengMingShiCommands
{
	private static readonly string[] CoreVisibleTraitIds =
	{
		"XjRealm1", "XjRealm2", "XjRealm3", "XjRealm4", "XjRealm5",
		"XjZz1", "XjZz2", "XjZz3", "XjZz4", "XjZz5", "XjZz6", "XjZz7", "XjZz8", "XjZz9",
		"ChuShen1", "ChuShen2", "ChuShen3", "ChuShen4", "ChuShen5", "ChuShen6", "ChuShen7", "ChuShen8"
	};

	internal static XjDengMingShiCommandResult SaveSelectedActor()
	{
		return TryGetSelectedActor(out Actor actor)
			? SaveSelectedActor(actor)
			: new XjDengMingShiCommandResult(false, "未选中可保存的角色。");
	}

	internal static XjDengMingShiCommandResult SaveSelectedActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjDengMingShiCommandResult(false, "未选中可保存的角色。");
		}

		if (!XjDengMingShiManager.SaveActor(actor))
		{
			return new XjDengMingShiCommandResult(false, "保存失败。");
		}

		return new XjDengMingShiCommandResult(true, "已保存：" + (actor.getName() ?? "未名角色"));
	}

	/// <summary>
	/// 按 actor ID 移除已保存的记录（用于 UnitWindow 一键切换）。
	/// </summary>
	internal static XjDengMingShiCommandResult RemoveSavedActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjDengMingShiCommandResult(false, "未选中角色。");
		}

		if (!XjDengMingShiManager.IsActorSaved(actor))
		{
			return new XjDengMingShiCommandResult(false, "该角色尚未保存到登名石。");
		}

		XjDengMingShiManager.RemoveSavedActor(actor);
		return !XjDengMingShiManager.IsActorSaved(actor)
			? new XjDengMingShiCommandResult(true, "已从登名石移除。")
			: new XjDengMingShiCommandResult(false, "移除失败。");
	}

	/// <summary>
	/// 检查角色是否已保存（用于 UnitWindow 按钮状态指示）。
	/// </summary>
	internal static bool IsActorSaved(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return XjDengMingShiManager.IsActorSaved(actor);
	}

	internal static XjDengMingShiCommandResult DeleteRecord(string recordId)
	{
		return XjDengMingShiManager.RemoveSavedActor(recordId)
			? new XjDengMingShiCommandResult(true, "已删除记录。")
			: new XjDengMingShiCommandResult(false, "未找到记录。");
	}

	internal static XjDengMingShiCommandResult TryPlaceRecord(string recordId)
	{
		return XjDengMingShiManager.SelectActorToPlace(recordId)
			? new XjDengMingShiCommandResult(true, "请选择地图位置放置角色。")
			: new XjDengMingShiCommandResult(false, "未找到记录。");
	}

	internal static bool TryGetSelectedActor(out Actor actor)
	{
		actor = null;
		Type selectedMetasType = typeof(SelectedMetas);
		const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
		string[] names =
		{
			"selected_actor",
			"selected_unit",
			"selected_creature",
			"selected_meta_object",
			"selected"
		};

		for (int i = 0; i < names.Length; i++)
		{
			FieldInfo field = selectedMetasType.GetField(names[i], flags);
			if (TryResolveActor(field?.GetValue(null), out actor))
			{
				return true;
			}

			PropertyInfo property = selectedMetasType.GetProperty(names[i], flags);
			if (property != null && property.CanRead && TryResolveActor(property.GetValue(null, null), out actor))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryResolveActor(object value, out Actor actor)
	{
		actor = value as Actor;
		if (actor?.data != null)
		{
			return true;
		}

		if (value == null)
		{
			return false;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		string[] actorMemberNames = { "actor", "unit", "data" };
		for (int i = 0; i < actorMemberNames.Length; i++)
		{
			FieldInfo field = value.GetType().GetField(actorMemberNames[i], flags);
			if (field?.GetValue(value) is Actor fieldActor && fieldActor.data != null)
			{
				actor = fieldActor;
				return true;
			}

			PropertyInfo property = value.GetType().GetProperty(actorMemberNames[i], flags);
			if (property != null && property.CanRead && property.GetValue(value, null) is Actor propertyActor && propertyActor.data != null)
			{
				actor = propertyActor;
				return true;
			}
		}

		return false;
	}

	private static XjDengMingShiRecord BuildRecord(Actor actor)
	{
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorInfoReadModel info = XjActorInfoReadModel.BuildForActor(actor);
		XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		XjActorGongFaCollection.TryExportSerialized(actor, out int gongFaCollectionVersion, out string gongFaCollectionJson);
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		string xianJiIds = xianJi.Ids == null ? string.Empty : string.Join("|", xianJi.Ids);
		XjCaiQiFaState caiQiFa = XjCaiQiFaAccessor.BuildState(actor);
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		XjFaBaoState faBaoState = XjFaBaoAccessor.BuildState(actor);
		XjFaBaoDisplayState faBao = XjFaBaoReadModel.BuildForActor(actor);
		XjZongMenIdentitySnapshot zongMen = XjZongMenAccessor.BuildIdentity(actor);

		long familyStableId = 0L;
		string familyName = string.Empty;
		if (XjFamilyReadModel.Shared.TryGetConfirmedIdentity(actorId, out XjFamilyIdentity identity))
		{
			familyStableId = identity.FamilyStableIdValue;
			familyName = XjFamilyDisplayNameResolver.Resolve(familyStableId);
		}

		return new XjDengMingShiRecord
		{
			RecordId = CreateRecordId(actorId),
			ActorName = SafeActorName(actor),
			SourceActorId = actorId,
			SavedYear = GetCurrentYear(actor),
			RaceId = ReadNestedId(actor, "asset"),
			KingdomId = ReadNestedId(actor, "kingdom"),
			CultureId = ReadNestedId(actor, "culture"),
			TraitIds = ReadVisibleTraits(actor),
			Realm = info.RealmDisplay,
			RealmId = snapshot.RealmId,
			DaoTu = info.DaoTu,
			XjZz = info.XjZz,
			GongFaName = gongFa.Found ? gongFa.Name : string.Empty,
			GongFaGrade = gongFa.Found ? gongFa.Grade : 0,
			// 兼容字段保留用于读取旧档，但新记录固定清零。
			GongFaStage = 0,
			GongFaProgress = 0f,
			GongFaDaoTu = gongFa.Found ? gongFa.DaoTu : string.Empty,
			GongFaCollectionVersion = gongFaCollectionVersion,
			GongFaCollectionJson = gongFaCollectionJson,
			XianJiIds = xianJiIds,
			XianJiLastYear = xianJi.LastYear,
			CaiQiFaName = caiQiFa.Found ? caiQiFa.Name : string.Empty,
			CaiQiFaDaoTu = caiQiFa.Found ? caiQiFa.DaoTu : string.Empty,
			CaiQiFaSourcePlace = caiQiFa.Found ? caiQiFa.SourcePlace : string.Empty,
			CaiQiFaSourceYear = caiQiFa.Found ? caiQiFa.SourceYear : 0,
			QiuJinFa = qiuJinFa.Found ? qiuJinFa.Name : string.Empty,
			QiuJinFaSourceGongFaName = qiuJinFa.Found ? qiuJinFa.SourceGongFaName : string.Empty,
			QiuJinFaSourceGongFaGrade = qiuJinFa.Found ? qiuJinFa.SourceGongFaGrade : 0,
			QiuJinFaSourceDaoTu = qiuJinFa.Found ? qiuJinFa.SourceDaoTu : string.Empty,
			QiuJinFaLastYear = qiuJinFa.Found ? qiuJinFa.LastYear : 0,
			QiuJinFaBoundAuthority = qiuJinFa.Found ? qiuJinFa.BoundAuthority : string.Empty,
			JinDan = jinDan.Found ? jinDan.JinXing : string.Empty,
			GuoWei = jinDan.Found ? jinDan.GuoWei : string.Empty,
			JinDanSuccessYear = jinDan.Found ? jinDan.SuccessYear : 0,
			ShenDanGuoWei = shenDan.Found ? shenDan.GuoWei : string.Empty,
			ShenDanAnchorActorId = shenDan.Found ? shenDan.AnchorActorId : 0L,
			ShenDanAnchorName = shenDan.Found ? shenDan.AnchorName : string.Empty,
			ShenDanYear = shenDan.Found ? shenDan.Year : 0,
			FaBaoSummary = faBao.Found ? faBao.DisplayText : string.Empty,
			FaBaoId = faBaoState.Found ? faBaoState.Id : string.Empty,
			FaBaoName = faBaoState.Found ? faBaoState.Name : string.Empty,
			FaBaoDaoTu = faBaoState.Found ? faBaoState.DaoTu : string.Empty,
			FaBaoClass = faBaoState.Found ? faBaoState.ClassName : string.Empty,
			FaBaoSource = faBaoState.Found ? faBaoState.Source : string.Empty,
			FaBaoYear = faBaoState.Found ? faBaoState.Year : 0,
			FamilyStableId = familyStableId,
			FamilyName = familyName,
			ZongMenId = zongMen.Found ? zongMen.ZongMenId : 0L,
			ZongMenName = zongMen.Found ? zongMen.ZongMenName : string.Empty
		};
	}

	private static string CreateRecordId(long actorId)
	{
		return actorId.ToString(CultureInfo.InvariantCulture)
			+ "-"
			+ DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture);
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名角色" : name.Trim();
		}
		catch
		{
			return "未名角色";
		}
	}

	private static int GetCurrentYear(Actor actor)
	{
		return actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
	}

	private static List<string> ReadVisibleTraits(Actor actor)
	{
		List<string> traits = new List<string>();
		foreach (string traitId in EnumerateVisibleTraitIds())
		{
			if (!string.IsNullOrWhiteSpace(traitId) && actor.hasTrait(traitId))
			{
				traits.Add(traitId);
			}
		}

		return traits;
	}

	private static IEnumerable<string> EnumerateVisibleTraitIds()
	{
		for (int i = 0; i < CoreVisibleTraitIds.Length; i++)
		{
			yield return CoreVisibleTraitIds[i];
		}

		string[] daoTuTraitIds = XjDaoTuVisibleTraitCatalog.AllTraitIds;
		for (int i = 0; i < daoTuTraitIds.Length; i++)
		{
			yield return daoTuTraitIds[i];
		}
	}

	private static string ReadNestedId(object owner, string memberName)
	{
		object member = ReadMember(owner, memberName);
		object id = ReadMember(member, "id") ?? ReadMember(ReadMember(member, "data"), "id");
		return id == null ? string.Empty : Convert.ToString(id, CultureInfo.InvariantCulture) ?? string.Empty;
	}

	private static object ReadMember(object owner, string memberName)
	{
		if (owner == null || string.IsNullOrWhiteSpace(memberName))
		{
			return null;
		}

		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo field = owner.GetType().GetField(memberName, flags);
		if (field != null)
		{
			return field.GetValue(owner);
		}

		PropertyInfo property = owner.GetType().GetProperty(memberName, flags);
		return property != null && property.CanRead ? property.GetValue(owner, null) : null;
	}
}
