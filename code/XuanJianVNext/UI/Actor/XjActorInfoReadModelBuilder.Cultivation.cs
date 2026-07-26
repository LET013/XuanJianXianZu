using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildCaiQiSummary(Actor actor, in XjCaiQiSnapshot snapshot)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
			{
				return string.Empty;
			}
	
			bool isTaiXi = string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal);
			bool isZaQiLianQi = string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
				&& snapshot.LianQiByZaQi;
			if (!isTaiXi && !isZaQiLianQi)
			{
				if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
					&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
					&& string.Equals(daoTu, XjQingXuanKongZhengSystem.SourceDaoTu, StringComparison.Ordinal))
				{
					return "参羊玄气：" + XjQingXuanKongZhengSystem.BuildProgressText(actor);
				}
				return string.Empty;
			}
	
			if (!snapshot.HasCompletedCaiQi)
			{
				if (string.Equals(snapshot.Status, XjCaiQiStatus.Pending, StringComparison.Ordinal))
				{
					string remaining = FormatRelativeCaiQiYears(actor, snapshot.NextCaiQiYear, "后启");
					return string.IsNullOrWhiteSpace(remaining) ? "采气待启" : "采气待启（" + remaining + "）";
				}
	
				if (string.Equals(snapshot.Status, XjCaiQiStatus.Active, StringComparison.Ordinal))
				{
					return "采气进行中";
				}
	
				if (string.Equals(snapshot.Status, XjCaiQiStatus.Cooldown, StringComparison.Ordinal))
				{
					string remaining = FormatRelativeCaiQiYears(actor, snapshot.NextCaiQiYear, "后再试");
					return string.IsNullOrWhiteSpace(remaining) ? "采气冷却" : "采气冷却（" + remaining + "）";
				}
	
				if (string.Equals(snapshot.Status, XjCaiQiStatus.Failure, StringComparison.Ordinal))
				{
					string reason = string.IsNullOrWhiteSpace(snapshot.FailureReason) ? "未知原因" : snapshot.FailureReason.Trim();
					return "采气失败（" + reason + "）";
				}
	
				return "暂无采气";
			}
	
			string resourceText = BuildCaiQiResourceText(actor, snapshot);
	
			if (string.IsNullOrWhiteSpace(resourceText))
			{
				resourceText = string.Equals(snapshot.ResultType, XjCaiQiResultTypes.ZaQi, StringComparison.Ordinal)
					? "杂气*1"
					: "先天气*1";
			}
	
			return "已采气（" + resourceText + "）";
		}

		private static string BuildCaiQiResourceText(Actor actor, in XjCaiQiSnapshot snapshot)
		{
			long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
			if (actorId > 0L && XjQianKunDaiRegistry.TryGet(actorId, out XjQianKunDaiState qianKunDai))
			{
				IReadOnlyList<XjQianKunDaiItem> items = qianKunDai.Items ?? Array.Empty<XjQianKunDaiItem>();
				List<string> parts = new List<string>(items.Count);
				for (int i = 0; i < items.Count; i++)
				{
					XjQianKunDaiItem item = items[i];
					if (item.Count <= 0
						|| !string.Equals(item.Category, XjQianKunDaiRegistry.CategoryCaiQi, StringComparison.Ordinal))
					{
						continue;
					}
	
					string displayName = string.IsNullOrWhiteSpace(item.DisplayName)
						? GetCaiQiResourceDisplay(item.ItemId)
						: XjCaiQiCatalog.EnsureQiSuffix(item.DisplayName.Trim());
					parts.Add(displayName + "*" + item.Count.ToString(CultureInfo.InvariantCulture));
				}
	
				if (parts.Count > 0)
				{
					return string.Join("、", parts);
				}
			}
	
			if (string.IsNullOrWhiteSpace(snapshot.ResourceId))
			{
				return string.Empty;
			}
	
			string resourceName = GetCaiQiResourceDisplay(snapshot.ResourceId);
			if (snapshot.ResourceCount <= 0)
			{
				return resourceName + "*0，已消耗";
			}
	
			return resourceName + "*" + snapshot.ResourceCount.ToString(CultureInfo.InvariantCulture);
		}

		private static string GetCaiQiResourceDisplay(string resourceId)
		{
			if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName))
			{
				return displayName;
			}
	
			if (string.Equals(resourceId, "zaqi", StringComparison.Ordinal))
			{
				return "杂气";
			}
	
			return "未知采气";
		}

		private static string BuildCaiQiFaSummary(in XjCaiQiFaState state)
		{
			if (!state.Found)
			{
				return "暂无采气法";
			}
	
			string place = string.IsNullOrWhiteSpace(state.SourcePlace) ? string.Empty : " - " + state.SourcePlace.Trim();
			string year = state.SourceYear > 0 ? " - " + state.SourceYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
			return state.Name + "（" + state.DaoTu + place + year + "）";
		}

		private static string BuildXianJiSummary(Actor actor, string daoTu, in XjXianJiState state)
		{
			string project = BuildXianJiProjectSummary(actor);
			if (!state.Found || state.Count <= 0)
			{
				return string.IsNullOrWhiteSpace(project) ? "暂无仙基" : project;
			}
	
			string summary = string.Empty;
			if (state.Ids != null && state.Ids.Length > 0)
			{
				string[] displays = new string[state.Ids.Length];
				for (int i = 0; i < state.Ids.Length; i++)
				{
					displays[i] = state.Ids[i] + ResolveXianJiPoolSuffix(daoTu, state.Ids[i]);
				}
				summary = string.Join("、", displays);
			}
			return string.IsNullOrWhiteSpace(project) ? summary : summary + " - " + project;
		}

		private static string ResolveXianJiPoolSuffix(string daoTu, string xianJiId)
		{
			return XjXianJiCatalog.GetPoolKind(daoTu, xianJiId) switch
			{
				XjXianJiPoolKind.Native => "（上位）",
				XjXianJiPoolKind.Lower => "（下位）",
				XjXianJiPoolKind.Adjacent => "（相邻）",
				_ => "（其他）"
			};
		}

		private static string BuildXianJiProjectSummary(Actor actor)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjXianJiProjectId, out string id)
				|| string.IsNullOrWhiteSpace(id))
			{
				return string.Empty;
			}
	
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiProjectCompleteYear, out int completeYear);
			int currentYear = XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
			int remaining = Math.Max(0, completeYear - currentYear);
			return remaining > 0
				? "参悟中：" + id.Trim() + "（还需" + remaining.ToString(CultureInfo.InvariantCulture) + "年）"
				: "参悟中：" + id.Trim() + "（本年可成）";
		}

		private static string BuildQiuJinFaSummary(Actor actor, in XjQiuJinFaState state)
		{
			if (!state.Found)
			{
				XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, out int failureCount);
				if (failureCount > 0)
				{
					return "求金未成";
				}
	
				return "暂无求金法";
			}
	
			List<string> parts = new List<string>(2);
			string source = ResolveQiuJinFaSourceText(actor, state);
			if (!string.IsNullOrWhiteSpace(source))
			{
				parts.Add("来源：" + source);
			}
			if (!string.IsNullOrWhiteSpace(state.BoundAuthority))
			{
				parts.Add("权柄：" + state.BoundAuthority.Trim());
			}
	
			return parts.Count == 0 ? state.Name : state.Name + "（" + string.Join(" - ", parts) + "）";
		}

		private static string ResolveQiuJinFaSourceText(Actor actor, in XjQiuJinFaState state)
		{
			string code = (state.ReasonCode ?? string.Empty).Trim();
			if (string.Equals(code, "FamilyBorrowQiuJinFa", StringComparison.Ordinal))
			{
				return "家族借法";
			}
			if (string.Equals(code, "ZongMenQiuJinFa", StringComparison.Ordinal))
			{
				return "宗门借法";
			}
			if (string.Equals(code, "Ok", StringComparison.Ordinal)
				|| string.Equals(code, "QiuJinFaComprehended", StringComparison.Ordinal))
			{
				return "自行参悟";
			}
			if (string.Equals(code, "ManualRealmEntry", StringComparison.Ordinal)
				|| string.Equals(code, "DengMingShi", StringComparison.Ordinal))
			{
				code = string.Empty;
			}
	
			if (!string.IsNullOrWhiteSpace(code)) return XjDisplayNameSanitizer.EventSource(code);
	
			// 旧档没有来源字段时，不根据“已入库”倒推借法。
			// 自行参悟的求金法同样会入库，倒推会把首位参悟者误标为家族借法。
			return string.Empty;
		}

		private static string FormatRelativeCaiQiYears(Actor actor, int absoluteYear, string suffix)
		{
			if (actor == null || absoluteYear <= 0)
			{
				return string.Empty;
			}
	
			int currentYear = (int)Math.Floor(Math.Max(0f, actor.getAge()));
			int remaining = absoluteYear - currentYear;
			if (remaining <= 0)
			{
				return string.Empty;
			}
	
			if (remaining > 5)
			{
				remaining = 5;
			}
	
			return remaining.ToString(CultureInfo.InvariantCulture) + "年" + (suffix ?? string.Empty);
		}
}

