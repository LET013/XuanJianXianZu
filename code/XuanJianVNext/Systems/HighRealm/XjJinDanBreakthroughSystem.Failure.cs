using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{		internal static void ReconcileFailureDemonization(Actor actor)
		{
			if (actor?.data == null || XuanJianVNext.Systems.Combat.XjTrueDamageSystem.IsJinXingYaoXie(actor))
			{
				return;
			}
			if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
			{
				XjJinDanAccessor.ClearFailure(actor);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				return;
			}

			XjJinDanState state = XjJinDanAccessor.BuildState(actor);
			if (string.IsNullOrWhiteSpace(state.FailedState))
			{
				return;
			}

			string failedState = state.FailedState.Trim();
			if (string.Equals(failedState, "NoGuoWei", StringComparison.Ordinal)
				|| string.Equals(failedState, "NoGuoWeiDaoTu", StringComparison.Ordinal)
				|| string.Equals(failedState, "QuanBingDeficient", StringComparison.Ordinal)
				|| string.Equals(failedState, "CrossDaoTuSpell", StringComparison.Ordinal))
			{
				// 0.6 旧逻辑在突破尚未触发前写入了永久失败字符串。
				// 这些存档状态没有完成失败结算，必须清除后恢复年度尝试。
				XjJinDanAccessor.ClearFailure(actor);
				return;
			}

			if (string.Equals(failedState, "BreakthroughFailed", StringComparison.Ordinal))
			{
				ResolveTerminalFailure(actor, state.LastAttemptYear, "BreakthroughFailed");
				return;
			}

			if (failedState.StartsWith("ForcedDeath:", StringComparison.Ordinal))
			{
				ResolveForcedDeathFailure(actor, state.LastAttemptYear, failedState.Substring("ForcedDeath:".Length));
				return;
			}

			if (failedState.StartsWith("Terminal:", StringComparison.Ordinal))
			{
				ResolveTerminalFailure(actor, state.LastAttemptYear, failedState.Substring("Terminal:".Length));
				return;
			}

			// 未识别的旧版失败字符串没有可验证的终局结算证据。
			// 不允许其继续把存活紫府永久锁死，清除后恢复年度突破链。
			XjJinDanAccessor.ClearFailure(actor);
		}

		private static void ResolveTerminalFailure(Actor actor, int failureYear, string reason)
		{
			if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
			{
				XjJinDanAccessor.ClearFailure(actor);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				return;
			}
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}

			if (XjRealmSuppression.GetRealmTier(actor) >= XjRealmSuppression.TierJinDan
				|| XjJinDanAccessor.BuildState(actor).Found
				|| XjShenDanAccessor.BuildState(actor).Found)
			{
				XjJinDanAccessor.ClearFailure(actor);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				return;
			}

			int safeYear = Math.Max(1, failureYear);
			string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "BreakthroughFailed" : reason.Trim();
			XjJinDanAccessor.WriteFailure(actor, "Terminal:" + normalizedReason, safeYear);
			if (!IsFourthAptitudeJinDanAttempt(actor) && TryCreateJinXingYaoXieOnFailure(actor, safeYear))
			{
				return;
			}

			int worldYear = GetWorldYear(actor);
			if (worldYear <= 0)
			{
				worldYear = safeYear;
			}
			string failureAnnouncement = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanFailureDeath(actor);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "JinDanFailure");
			bool died = XuanJianVNext.Systems.Combat.XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)5, true, XuanJianVNext.Systems.Death.XjDeathCause.BreakthroughFailure);
			if (died)
			{
				XuanJianVNext.Systems.Chronicle.XjChronicleWriter.RecordJinDanFailureDeath(actor, worldYear);
				XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastSLevelActorEvent(
					actor,
					failureAnnouncement,
					failureAnnouncement,
					"#B84A4A",
					8f,
					XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanFail);
			}
			if (!died && XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				// 若底层死亡调用被外部模组阻断，不保留永久失败锁；下一年重新结算。
				XjJinDanAccessor.ClearFailure(actor);
			}
		}

		private static void ResolveForcedDeathFailure(Actor actor, int failureYear, string reason)
		{
			if (XjDaoTaiPosturePolicy.IsGuaranteedCultivator(actor))
			{
				XjJinDanAccessor.ClearFailure(actor);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				return;
			}
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}

			int safeYear = Math.Max(1, failureYear);
			string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "CrossDaoTuSpell" : reason.Trim();
			// 单独持久化强制死亡类型，避免重载/延迟对账把“其他神通”
			// 错误地重新分流成结璘仙或金性妖邪。
			XjJinDanAccessor.WriteFailure(actor, "ForcedDeath:" + normalizedReason, safeYear);
			int worldYear = GetWorldYear(actor);
			if (worldYear <= 0)
			{
				worldYear = safeYear;
			}
			string failureAnnouncement = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanFailureDeath(actor);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, "JinDanFailure");
			bool died = XuanJianVNext.Systems.Combat.XjVanillaDeathGuard.TryExecuteForceDeath(actor, (AttackType)5, true, XuanJianVNext.Systems.Death.XjDeathCause.BreakthroughFailure);
			if (died)
			{
				XuanJianVNext.Systems.Chronicle.XjChronicleWriter.RecordJinDanFailureDeath(actor, worldYear);
				XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastSLevelActorEvent(
					actor,
					failureAnnouncement,
					failureAnnouncement,
					"#B84A4A",
					8f,
					XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanFail);
			}
			if (!died && XjSafeCore.IsAliveActor(actor))
			{
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjDeathAnnouncementReason, string.Empty);
				XjActorAccessor.SetString(actor, XjActorDataKeys.XjJinDanFailureNarrative, string.Empty);
				XjJinDanAccessor.ClearFailure(actor);
			}
		}

		private static string BuildZhengWeiSchemerFailureNarrative(Actor actor, in XjGuoWeiRegistryEntry schemer)
		{
			string schemerName = ResolveZhengWeiSchemerDisplayName(schemer);
			string actorName = actor?.getName() ?? "此人";
			return "因果位持有者" + schemerName + "暗中算计，" + actorName + "道势失衡，求金失败。";
		}

		private static string ResolveZhengWeiSchemerDisplayName(in XjGuoWeiRegistryEntry schemer)
		{
			string display = string.Empty;
			if (schemer.ActorId > 0L && XjScheduler.ResolveActor(schemer.ActorId, out Actor actor) && actor?.data != null)
			{
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string title);
				XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string baseName);
				display = JoinTitleAndName(title, baseName);
				if (string.IsNullOrWhiteSpace(display))
				{
					display = actor.getName();
				}
			}
			if (string.IsNullOrWhiteSpace(display))
			{
				display = schemer.ActorName;
			}
			display = StripRealmSuffix(display);
			return string.IsNullOrWhiteSpace(display) ? "未名真君" : display;
		}

		private static string JoinTitleAndName(string title, string baseName)
		{
			string safeTitle = StripRealmSuffix(title);
			string safeName = StripRealmSuffix(baseName);
			if (!string.IsNullOrWhiteSpace(safeTitle) && !string.IsNullOrWhiteSpace(safeName))
			{
				return safeTitle + "·" + safeName;
			}
			return !string.IsNullOrWhiteSpace(safeTitle) ? safeTitle : safeName;
		}

		private static string StripRealmSuffix(string value)
		{
			string text = (value ?? string.Empty).Trim();
			if (text.Length == 0)
			{
				return string.Empty;
			}

			string[] suffixes = { "-金丹", "-神丹", "-郁仪仙", "-结璘仙", "-紫府", "-筑基", "-炼气", "-胎息" };
			for (int i = 0; i < suffixes.Length; i++)
			{
				if (text.EndsWith(suffixes[i], StringComparison.Ordinal))
				{
					return text.Substring(0, text.Length - suffixes[i].Length).Trim();
				}
			}
			return text;
		}

		private static bool IsPermanentlyLockedGuoWeiAttempt(string daoTu, string guoWeiType)
		{
			if (XjShiFruitPositionLockSystem.HasLockedPosition(daoTu, guoWeiType)) return true;
			string candidate = XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, guoWeiType, 1);
			return XjGuoWeiRegistry.IsPermanentlyLockedGuoWei(daoTu, guoWeiType, candidate);
		}

		private static void BroadcastPermanentlyLockedGuoWei(Actor actor, string daoTu, string guoWeiType)
		{
			if (!XjSafeCore.IsAliveActor(actor))
			{
				return;
			}

			string actorName = actor.getName() ?? "有修士";
			string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
			string normalizedType = (guoWeiType ?? string.Empty).Trim();
			bool yinSiLock = (string.Equals(normalizedDaoTu, "谪炁", StringComparison.Ordinal)
					|| string.Equals(normalizedDaoTu, "下仪", StringComparison.Ordinal))
				&& string.Equals(normalizedType, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal);
			bool shiTuLock = XjShiFruitPositionLockSystem.HasLockedPosition(normalizedDaoTu, normalizedType);
			string positionName = normalizedDaoTu + (string.IsNullOrWhiteSpace(normalizedType) ? "果位" : normalizedType);
			string text;
			if (XjYuanZhaoFruitSealPolicy.TryBuildAttemptNarrative(
				actorName,
				normalizedDaoTu,
				normalizedType,
				XjYearTracker.CurrentYear,
				out string eraSealNarrative))
			{
				text = eraSealNarrative;
			}
			else text = shiTuLock
				? actorName + "欲证" + positionName + "，却觉此位已随投释真君真灵沉入释土，现世金性无门可落。"
				: yinSiLock
						? actorName + "欲证" + positionName + "，却只觉冥冥中此位已归阴司，金性不得落定。"
						: actorName + "欲证" + positionName + "，却触及斩养之劫遗下的天地封锁，金性顷刻散去。";
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
				actor,
				text,
				text,
				yinSiLock && !shiTuLock
					? XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.YinSiAppear
					: XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.HistoryWorld);
		}

		private static bool IsFourthAptitudeJinDanAttempt(Actor actor)
		{
			return actor?.data != null
				&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
				&& aptitude == 4;
		}

		private static bool TryCreateJinXingYaoXieOnFailure(Actor actor, int failureYear)
		{
			// 龙属本身就是独立异种。求金失败时应按突破失败直接结算死亡，
			// 不进入人族紫府“金性失控化妖邪”的旁支终局。
			if (XjLongShuSystem.IsLongShu(actor)) return false;

			if (actor?.data == null
				|| !XjRuntimeSettings.SpawnJinXingYaoXieEnabled
				|| XuanJianVNext.Systems.Combat.XjTrueDamageSystem.IsJinXingYaoXie(actor)
				|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
				|| !string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
			{
				return false;
			}

			long actorId = GetActorId(actor);
			int safeYear = Math.Max(1, failureYear);
			if (PositiveRoll(actorId, safeYear, "jindan_failure_yaoxie") >= JinDanFailureYaoXieChancePercent)
			{
				return false;
			}

			string originalName = actor.getName() ?? "无名紫府";
			string demonShenTong = ResolveJinXingYaoXieShenTong(actor);
			ApplyJinXingYaoXieName(actor, demonShenTong);
			if (!XuanJianVNext.Systems.Combat.XjTrueDamageSystem.MarkAsJinXingYaoXie(actor))
			{
				return false;
			}

			int worldYear = GetWorldYear(actor);
			if (worldYear <= 0)
			{
				worldYear = safeYear;
			}
			string demonName = actor.getName() ?? ("金性妖邪-" + demonShenTong);
			XuanJianVNext.Systems.Chronicle.XjChronicleWriter.RecordJinDanFailureDemonized(
				actor,
				worldYear,
				originalName,
				demonName);
			string announcement = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanFailureDemonized(
				originalName,
				demonName);
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastSLevelActorEvent(
				actor,
				announcement,
				announcement,
				"#A83E61",
				9f,
			XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanDemon);
			XuanJianVNext.Systems.Combat.XjTrueDamageSystem.ScheduleJinXingYaoXieSuppression(actor);
			return true;
		}

		internal static bool ConvertToJinXingYaoXieForDebugTrait(Actor actor, int currentYear)
		{
			if (actor?.data == null
				|| XuanJianVNext.Systems.Combat.XjTrueDamageSystem.IsJinXingYaoXie(actor))
			{
				return false;
			}

			string originalName = actor.getName() ?? "无名紫府";
			string demonShenTong = ResolveJinXingYaoXieShenTong(actor);
			ApplyJinXingYaoXieName(actor, demonShenTong);
			if (!XuanJianVNext.Systems.Combat.XjTrueDamageSystem.MarkAsJinXingYaoXie(actor))
			{
				return false;
			}

			int safeYear = Math.Max(1, currentYear);
			int worldYear = GetWorldYear(actor);
			if (worldYear <= 0)
			{
				worldYear = safeYear;
			}

			string demonName = actor.getName() ?? ("金性妖邪-" + demonShenTong);
			XuanJianVNext.Systems.Chronicle.XjChronicleWriter.RecordJinDanFailureDemonized(
				actor,
				worldYear,
				originalName,
				demonName);
			string announcement = XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildJinDanFailureDemonized(
				originalName,
				demonName);
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastSLevelActorEvent(
				actor,
				announcement,
				announcement,
				"#A83E61",
				9f,
				XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.JinDanDemon);
			XuanJianVNext.Systems.Combat.XjTrueDamageSystem.ScheduleJinXingYaoXieSuppression(actor);
			return true;
		}

		internal static void ReconcileJinXingYaoXieIdentity(Actor actor)
		{
			if (actor?.data == null
				|| !XuanJianVNext.Systems.Combat.XjTrueDamageSystem.IsJinXingYaoXie(actor))
			{
				return;
			}

			ApplyJinXingYaoXieName(actor, ResolveJinXingYaoXieShenTong(actor));
		}

		private static string ResolveJinXingYaoXieShenTong(Actor actor)
		{
			if (actor?.data == null)
			{
				return "无名神通";
			}

			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.JinXingYaoXieNameShenTong, out string stored)
				&& !string.IsNullOrWhiteSpace(stored))
			{
				string storedDisplay = ResolveShenTongDisplayName(stored);
				if (!string.IsNullOrWhiteSpace(storedDisplay))
				{
					if (!string.Equals(stored.Trim(), storedDisplay, StringComparison.Ordinal))
					{
						XjActorAccessor.SetString(actor, XjActorDataKeys.JinXingYaoXieNameShenTong, storedDisplay);
					}
					return storedDisplay;
				}
			}

			string resolved = string.Empty;
			if (actor.hasTrait(XjXuanJianShenTongSpecials.YiDuiYingTraitId)
				|| (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjYiDuiYingSourceActorId, out int sourceActorId)
					&& sourceActorId > 0))
			{
				resolved = "仪对影";
			}
			else
			{
				XjXianJiState state = XjXianJiAccessor.BuildState(actor);
				if (state.Ids != null)
				{
					for (int i = state.Ids.Length - 1; i >= 0; i--)
					{
						string candidate = (state.Ids[i] ?? string.Empty).Trim();
						if (!string.IsNullOrWhiteSpace(candidate))
						{
							resolved = ResolveShenTongDisplayName(candidate);
							break;
						}
					}
				}
			}

			if (string.IsNullOrWhiteSpace(resolved)
				&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjShenTongIds, out string shenTongIds))
			{
				string[] ids = shenTongIds.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
				for (int i = ids.Length - 1; i >= 0; i--)
				{
					string candidate = (ids[i] ?? string.Empty).Trim();
					if (!string.IsNullOrWhiteSpace(candidate))
					{
						resolved = ResolveShenTongDisplayName(candidate);
						break;
					}
				}
			}

			if (string.IsNullOrWhiteSpace(resolved))
			{
				resolved = "无名神通";
			}
			XjActorAccessor.SetString(actor, XjActorDataKeys.JinXingYaoXieNameShenTong, resolved);
			return resolved;
		}

		private static string ResolveShenTongDisplayName(string raw)
		{
			string value = (raw ?? string.Empty).Trim();
			if (value.Length == 0)
			{
				return string.Empty;
			}

			if (string.Equals(value, "仪对影", StringComparison.Ordinal)
				|| string.Equals(value, XjXuanJianShenTongSpecials.YiDuiYingTraitId, StringComparison.OrdinalIgnoreCase)
				|| value.IndexOf("YiDuiYing", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "仪对影";
			}
			if (string.Equals(value, "结璘章", StringComparison.Ordinal)
				|| string.Equals(value, XjXuanJianShenTongSpecials.LegacyJieLinZhangTraitId, StringComparison.OrdinalIgnoreCase)
				|| value.IndexOf("JieLinZhang", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "结璘章";
			}
			if (string.Equals(value, "郁仪文", StringComparison.Ordinal)
				|| string.Equals(value, XjXuanJianShenTongSpecials.LegacyYuYiWenTraitId, StringComparison.OrdinalIgnoreCase)
				|| value.IndexOf("YuYiWen", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return "郁仪文";
			}

			foreach (XjJinDanDaoSpellDefinition definition in XjJinDanDaoSpellCatalog.All)
			{
				if (string.Equals(value, definition.Id, StringComparison.OrdinalIgnoreCase)
					|| string.Equals(value, definition.DisplayName, StringComparison.Ordinal))
				{
					return definition.DisplayName;
				}
			}

			return LooksLikeInternalShenTongId(value) ? "无名神通" : value;
		}

		private static bool LooksLikeInternalShenTongId(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				char ch = value[i];
				if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z') || ch == '_')
				{
					return true;
				}
			}
			return false;
		}

		private static void ApplyJinXingYaoXieName(Actor actor, string shenTong)
		{
			if (actor?.data == null)
			{
				return;
			}

			string normalized = string.IsNullOrWhiteSpace(shenTong) ? "无名神通" : shenTong.Trim();
			string fullName = "金性妖邪-" + normalized;
			XjActorAccessor.SetString(actor, XjActorDataKeys.JinXingYaoXieNameShenTong, normalized);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, fullName);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, string.Empty);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, string.Empty);
			XjActorStateWriteGateway.SetDisplayName(actor, fullName, customName: true);
		}
}
