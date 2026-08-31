using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using System.Collections.Generic;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Runtime;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.GongFa;

internal readonly struct XjGongFaInheritanceRecord
{
	internal readonly bool Found;
	internal readonly string Name;
	internal readonly int Grade;
	internal readonly string DaoTu;
	internal readonly string MappedXianJi;
	internal readonly string Source;

	internal XjGongFaInheritanceRecord(
		bool found,
		string name,
		int grade,
		string daoTu,
		string mappedXianJi,
		string source)
	{
		Found = found;
		Name = name ?? string.Empty;
		Grade = grade < 0 ? 0 : grade;
		DaoTu = daoTu ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		Source = source ?? string.Empty;
	}
}

internal static class XjGongFaInheritanceSnapshot
{
	internal static IReadOnlyList<XjGongFaInheritanceRecord> BuildRecords(Actor actor, int minimumGrade = 4)
	{
		// 0.8.5.9 起，角色功法集合为唯一真实来源。旧版按仙基即时生成的
		// “虚拟五品功法”只在一次性迁移时落盘，读取阶段不再临时推导。
		return XjActorGongFaCollection.BuildInheritanceRecords(actor, minimumGrade);
	}

}

internal static class XjGongFaAccessor
{
	internal const int MaxActiveGrade = 4;

	internal static XjGongFaState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjGongFaState(false, string.Empty, 0, 0, 0f, string.Empty, false, "ActorInvalid");
		}

		if (!XjSafeCore.IsAliveActor(actor))
		{
			return new XjGongFaState(false, string.Empty, 0, 0, 0f, string.Empty, false, "ActorInvalidOrDead");
		}

		if (XjActorGongFaCollection.TryReadStoredPrimary(actor, out XjActorGongFaCollection.Record storedPrimary))
		{
			// 功法参悟进度系统已经废止。集合只保存真实功法、品阶、道途与神通映射；
			// 旧存档阶段/进度字段不再参与读取、显示或年度判定。
			return new XjGongFaState(
				true,
				storedPrimary.Name,
				storedPrimary.Grade,
				0,
				0f,
				storedPrimary.DaoTu,
				storedPrimary.Grade > MaxActiveGrade,
				"CollectionPrimary");
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int grade);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjGongFaDaoTu, out string daoTu);
		if (grade > XjGongFaDefinition.MaxGrade)
		{
			grade = XjGongFaDefinition.MaxGrade;
			name = XjGongFaNameLibrary.NormalizeNameForGrade(name, daoTu, grade);
		}
		if (!XjGongFaDefinition.IsValidGrade(grade))
		{
			return new XjGongFaState(false, string.Empty, 0, 0, 0f, string.Empty, false, "NoGongFa");
		}

		// v4：功法改为年度判定式参悟/借用。旧存档的阶段与进度键只保留兼容，
		// 不再参与运行时规则，也不再向读取模型暴露。
		return new XjGongFaState(
			true,
			name,
			grade,
			0,
			0f,
			daoTu,
			grade > MaxActiveGrade,
			"Ok");
	}

	internal static void WriteState(Actor actor, in XjGongFaState state)
	{
		if (actor?.data == null
			|| !XjSafeCore.IsAliveActor(actor)
			|| !state.Found
			|| !XjGongFaDefinition.IsValidGrade(state.Grade))
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int previousGrade);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaName, state.Name);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade, state.Grade);
		// 清零旧阶段/进度键，避免旧存档残值继续被其他兼容代码误读。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaStage, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjGongFaProgress, 0f);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaDaoTu, state.DaoTu);
		XjActorGongFaCollection.UpsertPrimary(actor, state);
		if (previousGrade != state.Grade)
		{
			XjGongFaAttemptSchedule.OnGradeChanged(
				actor,
				state.Grade,
				XjAnnualExecutionContext.ResolveYear(actor));
		}
	}

	internal static void WriteSource(Actor actor, string source)
	{
		if (XjSafeCore.IsAliveActor(actor))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaSource, source ?? string.Empty);
			XjActorGongFaCollection.UpdatePrimarySource(actor, source);
		}
	}

	internal static void Clear(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaName, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaStage, 0);
		XjActorAccessor.SetFloat(actor, XjActorDataKeys.XjGongFaProgress, 0f);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaSource, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, 0);
		XjGongFaAttemptSchedule.Clear(actor);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionLastYear, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, string.Empty);
		XjActorGongFaCollection.Clear(actor);
	}
}

internal static class XjGongFaAptitudeRules
{
	internal const int MaximumGrade = XjGongFaDefinition.MaxGrade;

	/// <summary>
	/// 资质决定功法上限：XjZz1-2=四品；XjZz3=四品，其中固定40%角色可至五品；
	/// XjZz4及以上可至六品，以允许四档资质在完整五法条件下搏取金丹。
	/// XjZz3仍为四品，其中固定40%角色可至五品；资格按角色ID固定，不会因读档或逐年重抽。
	/// </summary>
	internal static int GetAptitudeGradeCap(Actor actor, int xjZz)
	{
		if (xjZz >= 4) return 6;
		if (xjZz == 3) return HasStableXjZz3Grade5Eligibility(actor) ? 5 : 4;
		return xjZz >= 1 ? 4 : 0;
	}

	/// <summary>
	/// 境界锁：胎息可至二品、炼气可至四品、筑基可至五品、紫府及以上可至六品。
	/// 实际上限取境界与资质两者较低值。
	/// </summary>
	internal static int GetRealmGradeCap(Actor actor)
	{
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor)) return XjDaoTaiGongFaService.DaoTaiGrade;
		return GetRealmGradeCap(XjRealmSuppression.GetRealmTier(actor));
	}

	internal static int GetRealmGradeCap(int realmTier)
	{
		return realmTier switch
		{
			XjRealmSuppression.TierJinDan => 6,
			XjRealmSuppression.TierZiFu => 6,
			XjRealmSuppression.TierZhuJi => 5,
			XjRealmSuppression.TierLianQi => 4,
			XjRealmSuppression.TierTaiXi => 2,
			_ => 1
		};
	}

	internal static int GetMaximumAllowedGrade(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null || snapshot.XjZz <= 0)
		{
			return 0;
		}
		if (XjDaoTaiSpellScale.IsDaoTaiActor(actor))
		{
			return XjDaoTaiGongFaService.DaoTaiGrade;
		}

		return Math.Min(MaximumGrade, Math.Min(
			GetAptitudeGradeCap(actor, snapshot.XjZz),
			GetRealmGradeCap(actor)));
	}

	internal static int GetMaximumAllowedGrade(Actor actor)
	{
		return actor?.data == null
			? 0
			: GetMaximumAllowedGrade(actor, XjActorCultivationSnapshotBuilder.Build(actor));
	}

	internal static bool CanUseGrade(Actor actor, int grade)
	{
		return grade > 0 && grade <= GetMaximumAllowedGrade(actor);
	}

	internal static bool HasStableXjZz3Grade5Eligibility(Actor actor)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDeterministicHash.Roll01(actorId, 0, "XjZz3", "gongfa_grade5_exception") < 0.40f;
	}
}

internal static class XjGongFaProgression
{
	internal static void EnsureEntryGongFa(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| snapshot.XjZz <= 0)
		{
			return;
		}

		if (!XjGongFaAccessor.BuildState(actor).Found)
		{
			CreateInitialState(actor, snapshot);
		}
	}

	/// <summary>
	/// 单一年度功法主链。功法不再积累层数/进度；道慧决定本次参悟成功率，
	/// 资质与境界共同决定可达品级。五品用于求金法，六品为金丹门槛。
	/// </summary>
	internal static void TickActor(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| snapshot.XjZz <= 0)
		{
			return;
		}

		XjGongFaState state = XjGongFaAccessor.BuildState(actor);
		if (!state.Found)
		{
			CreateInitialState(actor, snapshot);
			state = XjGongFaAccessor.BuildState(actor);
			if (!state.Found)
			{
				return;
			}
		}
		if (ReconcileStoredGradeCap(actor, state))
		{
			state = XjGongFaAccessor.BuildState(actor);
		}

		if (ReconcileDaoTu(actor, snapshot.DaoTu, "运行期道途校准"))
		{
			state = XjGongFaAccessor.BuildState(actor);
		}
		int maximumAllowedGrade = XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot);
		if (maximumAllowedGrade <= state.Grade)
		{
			return;
		}

		// 紫府及以上每年先检查已确认家族的可用五品功法。只要当前低于五品，
		// 就允许直接承接符合自身仙基与道途的家族功法；无可借项时才继续逐品自行参悟。
		if (maximumAllowedGrade >= 5
			&& state.Grade < 5
			&& XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierZiFu
			&& XjFamilyHighGradeTransmission.TryBorrowGrade5(actor, snapshot, state))
		{
			return;
		}

		int nextGrade = state.Grade + 1;
		if (nextGrade > XjGongFaDefinition.MaxGrade)
		{
			return;
		}
		// 六品只能由求金法绑定链晋升，普通功法参悟链到五品为止。
		if (nextGrade == 6)
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		if (!XjGongFaAttemptSchedule.TryBeginAttempt(actor, nextGrade, currentYear, out int attemptYear))
		{
			return;
		}

		// executionYear 只负责同一世界年去重；attemptYear 是持久化逻辑游标。
		// 高倍速折叠后每年最多消费一笔旧机会，剩余周期继续保留。
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, Math.Max(0, attemptYear));

		bool comprehended = ShouldComprehendByHuiGuang(actor, snapshot.HuiGuang, state.Grade, nextGrade, attemptYear);
		XjStageZeroObservation.RecordGongFaAttempt(nextGrade, comprehended);
		if (!comprehended)
		{
			RecordGrade5Failure(actor, nextGrade, currentYear);
			return;
		}

		PromoteByComprehension(actor, state, snapshot, nextGrade, currentYear);
	}

	/// <summary>
	/// 紫府后续神通周期到点但主功法尚未五品时，改为进行一次直指五品的参悟判定。
	/// 调用方无论成败都会消耗本次三年/五年周期；这里只负责功法结果。
	/// </summary>
	internal static bool TryPromoteMainToGrade5ForShenTong(
		Actor actor,
		in XjActorCultivationSnapshot snapshot,
		int currentYear)
	{
		if (actor?.data == null)
		{
			return false;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (!current.Found || current.Grade >= 5
			|| XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot) < 5)
		{
			return false;
		}

		bool comprehended = ShouldComprehendByHuiGuang(actor, snapshot.HuiGuang, current.Grade, 5, currentYear);
		XjStageZeroObservation.RecordGongFaAttempt(5, comprehended);
		if (!comprehended)
		{
			RecordGrade5Failure(actor, 5, currentYear);
			return false;
		}

		PromoteByComprehension(actor, current, snapshot, 5, currentYear);
		return XjGongFaAccessor.BuildState(actor).Grade >= 5;
	}

	private static bool ReconcileStoredGradeCap(Actor actor, in XjGongFaState current)
	{
		if (actor?.data == null || !current.Found)
		{
			return false;
		}
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade, out int storedGrade)
			|| storedGrade <= XjGongFaDefinition.MaxGrade)
		{
			return false;
		}

		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true,
			current.Name,
			XjGongFaDefinition.MaxGrade,
			0,
			0f,
			current.DaoTu,
			true,
			"GradeCapReconciled"));
		XjGongFaAccessor.WriteSource(actor, "六品上限校准");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, string.Empty);
		return true;
	}

	private static void CreateInitialState(Actor actor, in XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return;
		}
		string daoTu = ResolveDaoTu(string.Empty, snapshot.DaoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		string name = XjGongFaNameLibrary.GenerateName(daoTu, 1, GetActorId(actor));
		if (string.IsNullOrWhiteSpace(name))
		{
			return;
		}

		XjGongFaState created = new XjGongFaState(
			true,
			name,
			1,
			0,
			0f,
			daoTu,
			false,
			"Created");
		XjGongFaAccessor.WriteState(actor, created);
		XjGongFaAccessor.WriteSource(actor, "自行入门");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, Math.Max(0, GetCurrentYear(actor)));
		PublishGongFaObtained(actor, created);
	}

	internal static bool ReconcileDaoTu(Actor actor, string daoTu, string source)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| IsInvalidDaoTu(daoTu))
		{
			return false;
		}

		daoTu = daoTu.Trim();
		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
			if (!current.Found)
		{
			return false;
		}

			// 即使主功法道途已经一致，也必须经过集合边界修复旧档中其余功法
			// 与求金权柄的陈旧元数据。
		return XjActorGongFaCollection.ReconcileDaoTu(
			actor,
			daoTu,
			string.IsNullOrWhiteSpace(source) ? "道途重定" : source);
	}

	/// <summary>
	/// 境界写入后的功法收口。紫府以原筑基仙基为首门神通，并将当前四品主功法
	/// 立即提升为对应五品功法；后续四门神通再通过五年一次的独立领悟链生成。
	/// </summary>
	internal static bool EnsureRealmMinimumGrade(Actor actor, string realmId, string daoTu)
	{
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| IsInvalidDaoTu(daoTu))
		{
			return false;
		}

		bool changed = false;
		if (!XjGongFaAccessor.BuildState(actor).Found)
		{
			CreateInitialState(actor, XjActorCultivationSnapshotBuilder.Build(actor));
			changed = XjGongFaAccessor.BuildState(actor).Found;
		}

		changed |= ReconcileDaoTu(actor, daoTu, "境界道途校准");
		if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjRealmHelper.GetOrder(realmId) < XjRealmHelper.GetOrder(XjRealmIds.ZiFu))
		{
			return changed;
		}

		XjGongFaState current = XjGongFaAccessor.BuildState(actor);
		if (!current.Found || current.Grade >= 5)
		{
			return changed;
		}

		string promotedName = XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, 5);
		if (string.IsNullOrWhiteSpace(promotedName))
		{
			promotedName = XjGongFaNameLibrary.GenerateName(daoTu, 5, GetActorId(actor) + 5005L);
		}
		if (string.IsNullOrWhiteSpace(promotedName))
		{
			return changed;
		}

		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true, promotedName, 5, 0, 0f, daoTu, true, "ZiFuGrade5Promotion"));
		XjGongFaAccessor.WriteSource(actor, "紫府功法蜕变");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaLastProgressionYear, Math.Max(0, GetCurrentYear(actor)));
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (xianJi.Ids != null && xianJi.Ids.Length > 0 && !string.IsNullOrWhiteSpace(xianJi.Ids[0]))
		{
			XjActorGongFaCollection.SetPrimaryMappedXianJi(actor, xianJi.Ids[0]);
		}
		PublishGongFaPromoted(actor, XjGongFaAccessor.BuildState(actor));
		return true;
	}

	private static bool ShouldComprehendByHuiGuang(
		Actor actor,
		float huiGuang,
		int currentGrade,
		int targetGrade,
		int currentYear)
	{
		if (actor != null && actor.hasTrait("ChuShen8"))
		{
			return true;
		}

		float minimum = targetGrade >= 5 ? 45f : 20f;
		float maximumChance = targetGrade switch
		{
			2 => 0.95f,
			3 => 0.80f,
			4 => 0.65f,
			5 => 0.50f,
			6 => 0.42f,
			_ => 0f
		};
		float chance = Math.Min(maximumChance, HuiGuangCurveChance(huiGuang, minimum, 100f, maximumChance)
			+ XjSectMentorshipTeachingService.ResolveGongFaBonus(actor, currentYear));
		return XjDeterministicHash.Roll01(
			GetActorId(actor),
			Math.Max(0, currentYear),
			currentGrade.ToString() + ">" + targetGrade.ToString(),
			"gongfa_unified_comprehension") <= chance;
	}

	private static void PromoteByComprehension(
		Actor actor,
		in XjGongFaState current,
		in XjActorCultivationSnapshot snapshot,
		int targetGrade,
		int currentYear)
	{
		string daoTu = ResolveDaoTu(current.DaoTu, snapshot.DaoTu);
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return;
		}

		string nextName = XjGongFaNameLibrary.NormalizeNameForGrade(current.Name, daoTu, targetGrade);
		if (string.IsNullOrWhiteSpace(nextName))
		{
			nextName = XjGongFaNameLibrary.GenerateName(daoTu, targetGrade, GetActorId(actor) + targetGrade * 1009L);
		}
		if (string.IsNullOrWhiteSpace(nextName))
		{
			return;
		}

		XjGongFaState promoted = new XjGongFaState(
			true,
			nextName,
			targetGrade,
			0,
			0f,
			daoTu,
			targetGrade > XjGongFaAccessor.MaxActiveGrade,
			"UnifiedComprehension");
		XjGongFaAccessor.WriteState(actor, promoted);
		XjGongFaAccessor.WriteSource(actor, "自行参悟");
		if (targetGrade >= 5)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, 0);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastYear, Math.Max(0, currentYear));
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason, string.Empty);
		}
		PublishGongFaPromoted(actor, promoted);
	}

	private static void RecordGrade5Failure(Actor actor, int targetGrade, int currentYear)
	{
		if (targetGrade < 5 || actor?.data == null)
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, out int count);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionFailureCount, count + 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastYear, Math.Max(0, currentYear));
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaGrade5PromotionLastFailureReason, targetGrade == 6 ? "六品未契" : "五品未契");
	}

	private static float HuiGuangCurveChance(float huiGuang, float minimum, float peak, float maximumChance)
	{
		if (huiGuang < minimum || peak <= minimum || maximumChance <= 0f)
		{
			return 0f;
		}

		float t = Math.Min(1f, Math.Max(0f, (huiGuang - minimum) / (peak - minimum)));
		float smooth = t * t * (3f - 2f * t);
		return Math.Min(maximumChance, Math.Max(0f, maximumChance * smooth));
	}

	internal static void PublishGongFaObtained(Actor actor, in XjGongFaState gongFa)
	{
		if (actor?.data == null || !gongFa.Found)
		{
			return;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.GongFaObtained(
			actor,
			gongFa.Name,
			gongFa.Grade,
			gongFa.DaoTu,
			ResolveGongFaEventSource(gongFa)));
		SyncPersonalGongFaStorage(actor);
	}

	internal static void PublishGongFaPromoted(Actor actor, in XjGongFaState gongFa)
	{
		if (actor?.data == null || !gongFa.Found)
		{
			return;
		}

		XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.GongFaPromoted(
			actor,
			gongFa.Name,
			gongFa.Grade,
			gongFa.DaoTu,
			ResolveGongFaEventSource(gongFa)));
		SyncPersonalGongFaStorage(actor);
	}

	private static string ResolveGongFaEventSource(in XjGongFaState gongFa)
	{
		return string.IsNullOrWhiteSpace(gongFa.ReasonCode) ? "PersonalGongFa" : gongFa.ReasonCode.Trim();
	}

	private static void SyncPersonalGongFaStorage(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		XjGongFaWarehouseReconciler.ReconcileActor(actor, currentYear);
		XjQianKunDaiSystem.UpdateState(actor);
	}

	private static string ResolveDaoTu(string currentDaoTu, string snapshotDaoTu)
	{
		string text = string.IsNullOrWhiteSpace(currentDaoTu) ? snapshotDaoTu : currentDaoTu;
		text = (text ?? string.Empty).Trim();
		return IsInvalidDaoTu(text) ? string.Empty : text;
	}

	internal static void PublishInheritanceSnapshot(Actor actor, string source)
	{
		if (actor?.data == null)
		{
			return;
		}

		IReadOnlyList<XjGongFaInheritanceRecord> records = XjGongFaInheritanceSnapshot.BuildRecords(actor);
		for (int i = 0; i < records.Count; i++)
		{
			XjGongFaInheritanceRecord record = records[i];
			if (!record.Found)
			{
				continue;
			}

			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.GongFaObtained(
				actor,
				record.Name,
				record.Grade,
				record.DaoTu,
				string.IsNullOrWhiteSpace(source) ? record.Source : source,
				record.MappedXianJi));
		}
	}

	private static bool IsInvalidDaoTu(string daoTu)
	{
		string text = (daoTu ?? string.Empty).Trim();
		return string.IsNullOrWhiteSpace(text)
			|| string.Equals(text, "基础", StringComparison.Ordinal)
			|| string.Equals(text, "玄门", StringComparison.Ordinal)
			|| string.Equals(text, "无道途", StringComparison.Ordinal);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}
