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
{
		private static string BuildGongFaSummary(Actor actor, in XjGongFaState state)
		{
			if (!state.Found)
			{
				// 人物右栏只展示角色正在修炼的真实主功法。乾坤袋与家族仓库
				// 代表“持有/可借”，不得反向伪装成当前功法。
				return "暂无功法";
			}

			string daoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? "未定道途" : state.DaoTu.Trim();
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaSource, out string sourceText);
			string source = FormatGongFaSource(sourceText);
			string grade5Failure = BuildFailureSuffix(
				actor,
				XjActorDataKeys.XjGongFaGrade5PromotionFailureCount,
				XjActorDataKeys.XjGongFaGrade5PromotionLastYear,
				XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason,
				"升五未成");
			return state.Name + "（" + FormatGongFaGrade(state.Grade) + " - " + daoTu
				+ source + grade5Failure + "）";
		}

		private static string FormatGongFaSource(string sourceText)
		{
			string source = (sourceText ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(source))
			{
				return string.Empty;
			}

			string display = source switch
			{
				"FamilyBorrow" => "家族借法",
				"DengMingShiSync" => "登名石复载",
				"ManualGrant" => "天授功法",
				"InitialGrant" => "初授功法",
				"SectWarehouse" => "宗门传法",
				_ => ContainsAsciiCodeToken(source) ? string.Empty : source
			};
			return string.IsNullOrWhiteSpace(display) ? string.Empty : " - 来源：" + display;
		}

		private static bool ContainsAsciiCodeToken(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				char c = value[i];
				if ((c >= 'A' && c <= 'Z') || c == '_' || c == '.')
				{
					return true;
				}
			}
			return false;
		}

		private static bool TryBuildFamilyBorrowGongFaSummary(Actor actor, out string summary)
		{
			summary = string.Empty;
			if (actor?.data == null
				|| !XjFamilyIdentityIndex.TryGetByActorId(((BaseSystemData)actor.data).id, out XjFamilyIdentityRecord family)
				|| !family.Found
				|| !string.Equals(family.ReasonCode, XjFamilyIdentityReasons.Confirmed, StringComparison.Ordinal)
				|| family.RootActorId <= 0L)
			{
				return false;
			}

			XjActorCultivationSnapshot snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
			string daoTu = NormalizeDaoTuForDisplay(snapshot.DaoTu);
			int maxGrade = XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot);
			if (string.IsNullOrWhiteSpace(daoTu) || maxGrade <= 0)
			{
				return false;
			}

			IReadOnlyList<XjFamilyGongFaWarehouseEntry> entries =
				XjFamilyWarehouseReadModel.Shared.ReadFamilyGongFaEntries(family.RootActorId, XjFamilyGongFaWarehouse.SourceTypeGongFa);
			XjFamilyGongFaWarehouseEntry selected = default;
			for (int i = 0; i < entries.Count; i++)
			{
				XjFamilyGongFaWarehouseEntry candidate = entries[i];
				if (!candidate.Found
					|| string.IsNullOrWhiteSpace(candidate.GongFaName)
					|| candidate.Grade <= 0
					|| candidate.Grade > maxGrade
					|| !string.Equals(NormalizeDaoTuForDisplay(candidate.DaoTu), daoTu, StringComparison.Ordinal))
				{
					continue;
				}

				if (!selected.Found
					|| candidate.Grade > selected.Grade
					|| (candidate.Grade == selected.Grade
						&& string.CompareOrdinal(candidate.GongFaName, selected.GongFaName) < 0))
				{
					selected = candidate;
				}
			}

			if (!selected.Found)
			{
				return false;
			}

			summary = selected.GongFaName.Trim() + "（" + FormatGongFaGrade(selected.Grade) + " - " + daoTu + " - 来源：家族借法）";
			return true;
		}

		private static string BuildGongFaBonusSummary(Actor actor, in XjGongFaState state)
		{
			int grade = state.Found ? state.Grade : 0;
			if (grade <= 0)
			{
				return string.Empty;
			}

			float cultivation = XjGongFaBonusRules.GetCultivationMultiplier(actor, grade);
			float attribute = XjGongFaBonusRules.GetAttributeMultiplier(actor, grade);
			float bloodlineOriginLegacy = XjGongFaBonusRules.GetBloodlineOriginDaoTuLegacyMultiplier(actor);
			float epoch = XjGongFaBonusRules.GetEpochCultivationMultiplier(actor);
			System.Text.StringBuilder builder = new System.Text.StringBuilder(96);
			builder.Append("\n    对境界修炼速度：").Append(cultivation.ToString("F2", CultureInfo.InvariantCulture)).Append("倍");
			builder.Append("\n    对属性加成：").Append(attribute.ToString("F2", CultureInfo.InvariantCulture)).Append("倍");
			if (bloodlineOriginLegacy > 1f)
			{
				builder.Append("\n    血脉原生道途余荫：").Append(bloodlineOriginLegacy.ToString("F2", CultureInfo.InvariantCulture)).Append("倍");
			}
			if (epoch > 1f)
			{
				builder.Append("\n    纪元契合：修炼速度").Append(epoch.ToString("F2", CultureInfo.InvariantCulture)).Append("倍");
			}
			return builder.ToString();
		}

		private static string BuildZiFuYuYinSummary(Actor actor)
		{
			float bloodlineOriginLegacy = XjGongFaBonusRules.GetBloodlineOriginDaoTuLegacyMultiplier(actor);
			return bloodlineOriginLegacy > 1f
				? "修炼速度" + bloodlineOriginLegacy.ToString("F2", CultureInfo.InvariantCulture) + "倍"
				: "无";
		}

		private static string BuildFailureSuffix(
			Actor actor,
			string countKey,
			string yearKey,
			string reasonKey,
			string label)
		{
			XjActorAccessor.TryGetInt(actor, countKey, out int failureCount);
			if (failureCount <= 0)
			{
				return string.Empty;
			}

			XjActorAccessor.TryGetInt(actor, yearKey, out int lastYear);
			XjActorAccessor.TryGetString(actor, reasonKey, out string reason);
			string yearText = lastYear > 0 ? " - " + lastYear.ToString(CultureInfo.InvariantCulture) + "年" : string.Empty;
			string reasonText = string.IsNullOrWhiteSpace(reason) ? string.Empty : " - " + reason.Trim();
			return " - " + label + failureCount.ToString(CultureInfo.InvariantCulture) + "次" + yearText + reasonText;
		}

		private static string FormatGongFaGrade(int grade)
		{
			return XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade);
		}
}
