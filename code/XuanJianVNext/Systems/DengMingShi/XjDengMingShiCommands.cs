using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
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
		return XjNativeSelectionInterop.TryGetSelectedActor(out actor);
	}

}
