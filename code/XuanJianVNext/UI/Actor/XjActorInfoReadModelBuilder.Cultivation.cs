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
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string BuildCaiQiSummary(Actor actor, in XjCaiQiSnapshot snapshot)
		{
			if (actor?.data == null
				|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
			{
				return string.Empty;
			}
	
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			if ((string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
					&& string.Equals(daoTu, XjQingXuanKongZhengSystem.SourceDaoTu, StringComparison.Ordinal))
				|| (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
					&& XjQingXuanKongZhengSystem.IsQingXuanDaoTu(daoTu)))
			{
				string qingXuanProgress = XjQingXuanKongZhengSystem.BuildProgressText(actor);
				if (!string.IsNullOrWhiteSpace(qingXuanProgress))
				{
					return string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
						? "青宣五神通：" + qingXuanProgress
						: "参羊玄气：" + qingXuanProgress;
				}
			}

			bool isTaiXi = string.Equals(realmId, XjRealmIds.TaiXi, StringComparison.Ordinal);
			bool isZaQiLianQi = string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
				&& snapshot.LianQiByZaQi;
			if (!isTaiXi && !isZaQiLianQi)
			{
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
			string year = state.SourceYear > 0 ? " - " + XjChronology.FormatYear(state.SourceYear) : string.Empty;
			string name = XjDisplayNameSanitizer.GameTerm(state.Name, "未名采气法");
			string daoTu = XjDisplayNameSanitizer.GameTerm(state.DaoTu, "未知道途");
			return name + "（" + daoTu + place + year + "）";
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
					string trace = XjDaoLineageStateRegistry.BuildShenTongTrace(daoTu, state.Ids[i]);
					string xianJiName = XjDisplayNameSanitizer.GameTerm(state.Ids[i], "未名神通");
					displays[i] = xianJiName + ResolveXianJiPoolSuffix(daoTu, state.Ids[i])
						+ (string.IsNullOrWhiteSpace(trace) ? string.Empty : "〔" + trace + "〕");
				}
				summary = string.Join("、", displays);
			}
				string intention = BuildGuoWeiIntentionSummary(actor, daoTu, in state);
				if (!string.IsNullOrWhiteSpace(intention))
					summary = string.IsNullOrWhiteSpace(summary) ? intention : summary + " - " + intention;
			return string.IsNullOrWhiteSpace(project) ? summary : summary + " - " + project;
		}


			private static string BuildGuoWeiIntentionSummary(Actor actor, string sourceDaoTu, in XjXianJiState state)
			{
				if (actor?.data == null || state.Count < 3) return string.Empty;
				// 已经形成金丹/真君位格后，果位身份以证道事务固化字段为准；
				// 即便旧档 RealmId 尚未及时同步，也绝不能重新显示或计算一次“求位意向”。
				if (XjJinDanAccessor.BuildStateWithoutDaoMigration(actor).Found) return string.Empty;
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string actorRealmId);
				if (!string.Equals(XjRealmHelper.NormalizeId(actorRealmId), XjRealmIds.ZiFu, StringComparison.Ordinal))
					return string.Empty;
				string source = XjDisplayNameSanitizer.GameTerm(sourceDaoTu, "未知道途");
				string type;
				string targetDaoTu;
				if (state.Count >= XjXianJiState.MaxCount && state.Ids != null)
				{
					// 五门成型后，照录直接读取真正求金所用的权威计算结果。
					// 这样旧档即使尚未等到年度对账，也不会继续显示过期的“岐路未定”。
					type = XjGuoWeiCalculator.Calculate(actor, source, state);
					targetDaoTu = XjGuoWeiCalculator.ResolveManifestDaoTu(source, state, type);
				}
				else
				{
					if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionType, out type)
						|| string.IsNullOrWhiteSpace(type)) return string.Empty;
					XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGuoWeiIntentionTargetDaoTu, out targetDaoTu);
				}
				string normalizedType = XjGuoWeiCalculator.NormalizePositionType(type);
				string target = string.IsNullOrWhiteSpace(targetDaoTu)
					? source
					: XjDisplayNameSanitizer.GameTerm(targetDaoTu, source);
				if (string.Equals(normalizedType, XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
				{
					if (XjGuoWeiCalculator.IsForbiddenIntercalaryCrossing(source, target))
						return "求位意向：岐路未定";
					return "求位意向：借" + source + "闰" + target;
				}
				if (string.Equals(normalizedType, XjGuoWeiCalculator.YuWei, StringComparison.Ordinal))
					return "求位意向：" + target + "余位";
				if (string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
					return "求位意向：" + target + "果位";
				return "求位意向：岐路未定";
			}


		private static string ResolveXianJiPoolSuffix(string daoTu, string xianJiId)
		{
			XjXianJiPoolKind kind = XjXianJiCatalog.GetPoolKind(daoTu, xianJiId);
			if (kind == XjXianJiPoolKind.Other
				&& XjXianJiCatalog.TryResolveOwningDaoTu(xianJiId, out string ownerDaoTu)
				&& !string.IsNullOrWhiteSpace(ownerDaoTu))
			{
				return "（" + XjDisplayNameSanitizer.GameTerm(ownerDaoTu, "异道") + "）";
			}
			return kind switch
			{
				XjXianJiPoolKind.Native => "（上位）",
				XjXianJiPoolKind.Lower => "（下位）",
				XjXianJiPoolKind.Adjacent => "（相邻）",
				_ => string.Empty
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
			string projectName = XjDisplayNameSanitizer.GameTerm(id, "未名神通");
			return remaining > 0
				? "参悟中：" + projectName + "（还需" + remaining.ToString(CultureInfo.InvariantCulture) + "年）"
				: "参悟中：" + projectName + "（本年可成）";
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
	
			// 此栏只展示求金法名称及其来源。权柄、托果法门与金丹位格各有
			// 独立字段，不能把尚未发生或不属于求金法本体的信息塞进这里。
			string name = XjDisplayNameSanitizer.GameTerm(state.Name, "未名求金法");
			string source = ResolveQiuJinFaSourceText(actor, in state);
			return name + "（来源：" + (string.IsNullOrWhiteSpace(source) ? "旧档未载" : source) + "）";
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
			if (code.StartsWith("UpperCultivatorCustomized:", StringComparison.Ordinal))
			{
				return "上修定制";
			}
			// 境界补录与登名石复载都有明确来源，只是不会绑定到某一本功法。
			// 旧版把它们清成空字符串，随后被 UI 误标成“旧档未载”。
			if (string.Equals(code, "ManualRealmEntry", StringComparison.Ordinal))
			{
				return "境界补录";
			}
			if (string.Equals(code, "DengMingShi", StringComparison.Ordinal))
			{
				return "登名石复载";
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
