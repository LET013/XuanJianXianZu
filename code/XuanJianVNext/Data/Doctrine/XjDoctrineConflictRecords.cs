using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Data.Doctrine;

/// <summary>
/// 四大道统是宏观修行立场，不等于国家、宗门、家族或个人阵营。
/// 关系采用有向矩阵：服气看紫金与紫金看服气可以不是同一个数值。
/// </summary>
internal static class XjDoctrineIds
{
	internal const string ZiJin = "zijin";
	internal const string FuQi = "fuqi";
	internal const string AncientShi = "ancient_shi";
	internal const string ModernShi = "modern_shi";

	internal static readonly string[] Ordered =
	{
		ZiJin,
		FuQi,
		AncientShi,
		ModernShi
	};

	internal static bool IsKnown(string doctrineId)
	{
		return string.Equals(doctrineId, ZiJin, StringComparison.Ordinal)
			|| string.Equals(doctrineId, FuQi, StringComparison.Ordinal)
			|| string.Equals(doctrineId, AncientShi, StringComparison.Ordinal)
			|| string.Equals(doctrineId, ModernShi, StringComparison.Ordinal);
	}
}

internal static class XjDoctrineRules
{
	internal static bool TryResolve(Actor actor, out string doctrineId)
	{
		doctrineId = string.Empty;
		if (actor?.data == null) return false;

		if (XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			doctrineId = XjDoctrineIds.ZiJin;
			return true;
		}
		if (XjCultivationPathRules.IsFuQiYangXing(actor))
		{
			doctrineId = XjDoctrineIds.FuQi;
			return true;
		}
		if (!XjCultivationPathRules.IsShi(actor)) return false;

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition, out string tradition))
			return false;
		if (string.Equals(tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal))
		{
			doctrineId = XjDoctrineIds.AncientShi;
			return true;
		}
		if (string.Equals(tradition, XjShiTraditionIds.Modern, StringComparison.Ordinal))
		{
			doctrineId = XjDoctrineIds.ModernShi;
			return true;
		}
		return false;
	}

	internal static string GetDisplayName(string doctrineId)
	{
		if (string.Equals(doctrineId, XjDoctrineIds.ZiJin, StringComparison.Ordinal)) return "紫金";
		if (string.Equals(doctrineId, XjDoctrineIds.FuQi, StringComparison.Ordinal)) return "服气";
		if (string.Equals(doctrineId, XjDoctrineIds.AncientShi, StringComparison.Ordinal)) return "古释";
		if (string.Equals(doctrineId, XjDoctrineIds.ModernShi, StringComparison.Ordinal)) return "今释";
		return "未知道统";
	}

	/// <summary>
	/// 固有道统态度只作定性立场说明，不直接转换为开局敌意数值。
	/// 真正会驱动主动冲突的“当世积怨”必须由世界内发生的异道事件产生。
	/// </summary>
	internal static string GetInherentStance(string sourceDoctrineId, string targetDoctrineId)
	{
		if (!XjDoctrineIds.IsKnown(sourceDoctrineId)
			|| !XjDoctrineIds.IsKnown(targetDoctrineId)
			|| string.Equals(sourceDoctrineId, targetDoctrineId, StringComparison.Ordinal))
		{
			return "同道";
		}

		if (string.Equals(sourceDoctrineId, XjDoctrineIds.ZiJin, StringComparison.Ordinal))
		{
			if (string.Equals(targetDoctrineId, XjDoctrineIds.FuQi, StringComparison.Ordinal)) return "相轻";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.AncientShi, StringComparison.Ordinal)) return "相安";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ModernShi, StringComparison.Ordinal)) return "戒备";
		}
		else if (string.Equals(sourceDoctrineId, XjDoctrineIds.FuQi, StringComparison.Ordinal))
		{
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ZiJin, StringComparison.Ordinal)) return "轻蔑";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.AncientShi, StringComparison.Ordinal)) return "相安";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ModernShi, StringComparison.Ordinal)) return "戒备";
		}
		else if (string.Equals(sourceDoctrineId, XjDoctrineIds.AncientShi, StringComparison.Ordinal))
		{
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ZiJin, StringComparison.Ordinal)) return "相安";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.FuQi, StringComparison.Ordinal)) return "相安";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ModernShi, StringComparison.Ordinal)) return "道异";
		}
		else if (string.Equals(sourceDoctrineId, XjDoctrineIds.ModernShi, StringComparison.Ordinal))
		{
			if (string.Equals(targetDoctrineId, XjDoctrineIds.ZiJin, StringComparison.Ordinal)) return "戒备";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.FuQi, StringComparison.Ordinal)) return "戒备";
			if (string.Equals(targetDoctrineId, XjDoctrineIds.AncientShi, StringComparison.Ordinal)) return "道异";
		}
		return "相安";
	}

	/// <summary>
	/// 保留字段兼容旧快照结构，但不再提供开局数值。
	/// </summary>
	internal static int GetBaseHostility(string sourceDoctrineId, string targetDoctrineId) => 0;

	internal static string GetStatus(int hostility)
	{
		if (hostility >= 80) return "道争";
		if (hostility >= 60) return "交恶";
		if (hostility >= 40) return "相争";
		if (hostility >= 20) return "相轻";
		return "相安";
	}
}

internal sealed class XjDoctrineConflictArchiveData
{
	public int SchemaVersion { get; set; } = 1;
	public List<XjDoctrineGrievanceArchiveRecord> Relations { get; set; } = new List<XjDoctrineGrievanceArchiveRecord>();
	public List<XjDoctrineConflictEventArchiveRecord> RecentEvents { get; set; } = new List<XjDoctrineConflictEventArchiveRecord>();
}

internal sealed class XjDoctrineGrievanceArchiveRecord
{
	public string SourceDoctrineId { get; set; } = string.Empty;
	public string TargetDoctrineId { get; set; } = string.Empty;
	public int Grievance { get; set; }
	public int LastChangedYear { get; set; }
	public string LastReason { get; set; } = string.Empty;
}

internal sealed class XjDoctrineConflictEventArchiveRecord
{
	public int Year { get; set; }
	public string SourceDoctrineId { get; set; } = string.Empty;
	public string TargetDoctrineId { get; set; } = string.Empty;
	public int Delta { get; set; }
	public string Reason { get; set; } = string.Empty;
}

internal sealed class XjDoctrineRelationSnapshot
{
	internal string SourceDoctrineId = string.Empty;
	internal string SourceDoctrineName = string.Empty;
	internal string TargetDoctrineId = string.Empty;
	internal string TargetDoctrineName = string.Empty;
	internal int BaseHostility;
	internal int Grievance;
	internal int FinalHostility;
	internal string Status = string.Empty;
	internal int LastChangedYear;
	internal string LastReason = string.Empty;
}

internal sealed class XjDoctrineConflictEventSnapshot
{
	internal int Year;
	internal string SourceDoctrineId = string.Empty;
	internal string SourceDoctrineName = string.Empty;
	internal string TargetDoctrineId = string.Empty;
	internal string TargetDoctrineName = string.Empty;
	internal int Delta;
	internal string Reason = string.Empty;
}
