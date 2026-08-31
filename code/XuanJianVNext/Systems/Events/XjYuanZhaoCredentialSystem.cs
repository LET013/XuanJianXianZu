using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Events;

/// <summary>
/// 水月照真的“照真请凭函”系统。
///
/// 这不是道尊亲召，也不是把水月照真改回公共洞天：空证且洞天门户稳定之后，
/// 每隔约一百至二百年，洞中才会流出一封只认一个真灵的请凭函，随机落到
/// 紫府或金丹手中。持函者可以由玩家主动开启一次短小的水月游历；游历只在
/// 因果/神识层结算，不移动角色、不生成道尊实体，也不会给宗门或国朝开放通行权。
///
/// 设计上把“百余年一次的可玩机缘”与 0.9.8.17 的正式【水月传召】严格分层：
/// 前者允许随机修士在外层水月中做选择，后者才是创道者真正主动点名的高位觐见。
/// </summary>
internal static class XjYuanZhaoCredentialSystem
{
	internal const string CredentialName = "照真请凭函";

	private const int CredentialMinIntervalYears = 100;
	private const int CredentialMaxIntervalYears = 200;
	private const int CredentialLifetimeYears = 50;
	private const int NoCandidateRetryYears = 10;
	private const int ExplorationSessionYears = 10;
	private const int NormalExplorationSteps = 3;

	private const int StateNone = 0;
	private const int StateHolding = 1;
	private const int StateExploring = 2;
	private const int StateResolved = 3;
	private const int StateClosed = 4;

	private const string NodeMirror = "mirror_self";
	private const string NodeCorridor = "moon_corridor";
	private const string NodeStele = "nameless_stele";
	private const string NodeLock = "lock_gate";
	private const string NodeSeat = "silent_seat";
	private const string NodeAbyss = "deep_water";
	private const string NodeBoat = "shoreless_boat";
	private const string NodeLamp = "water_lamp";
	private const string NodeRain = "upward_rain";
	private const string NodeTwinMoon = "twin_moon";
	private const string NodeName = "washed_name";
	private const string NodePalace = "submerged_hall";
	private const string NodeSource = "source_echo";
	private const string NodeFoundation = "foundation_shadow";
	private const string NodeFruit = "fruit_shadow";
	private const string NodeQuestion = "founder_question";

	private static readonly string[] GeneralNodes =
	{
		NodeMirror,
		NodeCorridor,
		NodeStele,
		NodeLock,
		NodeSeat,
		NodeAbyss,
		NodeBoat,
		NodeLamp,
		NodeRain,
		NodeTwinMoon,
		NodeName,
		NodePalace
	};

	internal readonly struct XjYuanZhaoExplorationView
	{
		internal readonly bool Found;
		internal readonly int State;
		internal readonly string Title;
		internal readonly string Body;
		internal readonly string Status;
		internal readonly string Choice0;
		internal readonly string Choice1;
		internal readonly string Choice2;
		internal readonly bool CanEndExploration;

		internal XjYuanZhaoExplorationView(
			bool found,
			int state,
			string title,
			string body,
			string status,
			string choice0,
			string choice1,
			string choice2,
			bool canEndExploration)
		{
			Found = found;
			State = state;
			Title = title ?? string.Empty;
			Body = body ?? string.Empty;
			Status = status ?? string.Empty;
			Choice0 = choice0 ?? string.Empty;
			Choice1 = choice1 ?? string.Empty;
			Choice2 = choice2 ?? string.Empty;
			CanEndExploration = canEndExploration;
		}
	}

	internal static void TickYear(int currentYear)
	{
		if (currentYear <= 0
			|| !XjYuanZhaoKongZhengEvent.IsTriggered
			|| !XjYuanZhaoKongZhengEvent.IsLegacyDongTianReady)
		{
			return;
		}

		ReconcileOutstandingCredential(currentYear);
		if (XjYuanZhaoKongZhengEvent.TryGetActiveFounderCredential(out _, out _)) return;
		// 正式【水月传召】优先级高于百余年流出的游历凭函；二者绝不并发。
		if (XjYuanZhaoKongZhengEvent.TryGetPendingFounderAudience(out _, out int audienceUntil, out _)
			&& audienceUntil >= currentYear) return;

		int nextYear = XjYuanZhaoKongZhengEvent.FounderNextCredentialYear;
		if (nextYear <= 0)
		{
			XjYuanZhaoKongZhengEvent.EnsureFounderCredentialSchedule(
				currentYear + RollCredentialInterval(currentYear, XjYuanZhaoKongZhengEvent.FounderTotalCredentialIssued));
			return;
		}
		if (currentYear < nextYear) return;

		if (!TryPickCredentialHolder(currentYear, out Actor holder))
		{
			XjYuanZhaoKongZhengEvent.DelayFounderCredentialSchedule(currentYear + NoCandidateRetryYears);
			return;
		}

		IssueCredential(holder, currentYear);
	}

	internal static bool HasPlayerInteraction(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return false;
		ReconcileActorState(actor, currentYear, allowTimeoutSettlement: true);
		int state = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState);
		if (state == StateHolding)
		{
			return ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear) >= currentYear;
		}
		if (state == StateExploring)
		{
			int started = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationStartedYear);
			return started > 0 && currentYear <= started + ExplorationSessionYears;
		}
		return false;
	}

	internal static bool HasPendingCredentialInteraction(Actor actor, int currentYear)
	{
		return HasPlayerInteraction(actor, currentYear);
	}

	internal static string ResolvePanelButtonLabel(Actor actor, int currentYear)
	{
		if (actor?.data == null) return string.Empty;
		int state = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState);
		if (state == StateExploring && HasPlayerInteraction(actor, currentYear)) return "◇ 续游水月 ◇";
		if (state == StateHolding && HasPlayerInteraction(actor, currentYear)) return "◇ 持函入水月 ◇";
		return string.Empty;
	}

	internal static bool TryGetView(Actor actor, int currentYear, out XjYuanZhaoExplorationView view)
	{
		view = default;
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0) return false;
		ReconcileActorState(actor, currentYear, allowTimeoutSettlement: true);

		int state = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState);
		if (state == StateHolding)
		{
			int issueYear = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialIssueYear);
			int untilYear = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear);
			if (untilYear < currentYear) return false;
			view = new XjYuanZhaoExplorationView(
				true,
				state,
				"〖" + CredentialName + "〗",
				"函面无字，月下方浮一泓水纹。持函人凝神相照，便觉一念被月影牵去，遥遥映入水月照真外层，肉身仍在尘世。\n\n"
				+ "此函认真灵而不认手泽，旁人得之亦如白纸。水中所见多是旧影、道痕与问心之境；能从其中带回多少，只看自身一念所悟。",
				XjChronology.FormatYear(issueYear) + "得函 · 至" + XjChronology.FormatYear(untilYear) + "水纹散尽",
				"映念入水月",
				"收函待月",
				"辞却此缘",
				false);
			return true;
		}

		if (state == StateExploring)
		{
			string node = ResolveCurrentNode(actor);
			BuildNodeView(actor, node, out string title, out string body, out string choice0, out string choice1, out string choice2);
			int insight = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight);
			int depth = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth);
			int restraint = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint);
			int step = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationStep);
			string last = ReadString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult);
			string status = "已历" + Math.Min(NormalExplorationSteps, Math.Max(0, step)) + "/" + NormalExplorationSteps
				+ "重 · 观照" + insight + " · 深涉" + depth + " · 持守" + restraint;
			if (!string.IsNullOrWhiteSpace(last)) body = last.Trim() + "\n\n" + body;
			view = new XjYuanZhaoExplorationView(true, state, title, body, status, choice0, choice1, choice2, true);
			return true;
		}

		if (state == StateResolved)
		{
			string outcome = ReadString(actor, XjActorDataKeys.YuanZhaoExplorationOutcome);
			int resolvedYear = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationResolvedYear);
			view = new XjYuanZhaoExplorationView(
				true,
				state,
				"〖水月游历已结〗",
				string.IsNullOrWhiteSpace(outcome) ? "水纹已合，此次因缘归于旧事。" : outcome,
				resolvedYear > 0 ? XjChronology.FormatYear(resolvedYear) : string.Empty,
				string.Empty,
				string.Empty,
				string.Empty,
				false);
			return true;
		}

		return false;
	}

	internal static bool TryBeginExploration(Actor actor, int currentYear, out string message)
	{
		message = string.Empty;
		if (actor?.data == null || !actor.isAlive() || currentYear <= 0)
		{
			message = "持函人的真灵已不在此世，函上水纹随之寂灭。";
			return false;
		}
		ReconcileActorState(actor, currentYear, allowTimeoutSettlement: true);
		if (ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState) != StateHolding
			|| ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear) < currentYear)
		{
			message = "请凭函上的水纹已经散去。";
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int serial = Math.Max(1, ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialSerial));
		int seed = XjDeterministicHash.PositiveIndex(actorId + (long)currentYear * 104729L + serial * 8191L,
			"yuanzhao_credential_exploration", int.MaxValue - 1) + 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialState, StateExploring);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationStartedYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed, seed);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationStep, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationUsedMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRarePending, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult,
			"函上水纹忽如活物，自指间铺成一面无边静水。肉身仍留原处，只有一念越过洞天门户。前方无人相迎，唯有静水与月影相待。");
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationOutcome, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationResolvedYear, 0);
		message = "请凭函已展开。";
		RecordHistory(actor, currentYear, "YuanZhaoCredentialEntered", XjStringHelper.ActorName(actor, "无名修士") + "持函入水月",
			XjStringHelper.ActorName(actor, "无名修士") + "展〖" + CredentialName + "〗，肉身未离尘世，只以一念循水月倒影进入水月照真外层。", 2);
		return true;
	}

	internal static bool TryDeclineCredential(Actor actor, int currentYear, out string message)
	{
		message = string.Empty;
		if (actor?.data == null || currentYear <= 0) return false;
		if (ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState) != StateHolding)
		{
			message = "当前并无可辞的请凭函。";
			return false;
		}
		long actorId = ((BaseSystemData)actor.data).id;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialState, StateClosed);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear, 0);
		XjYuanZhaoKongZhengEvent.ClearActiveFounderCredential(actorId);
		message = "持函者没有展开水纹，函上的月下水痕渐渐散去，此缘就此收束。";
		RecordHistory(actor, currentYear, "YuanZhaoCredentialDeclined", XjStringHelper.ActorName(actor, "无名修士") + "辞水月凭函",
			XjStringHelper.ActorName(actor, "无名修士") + "未取〖" + CredentialName + "〗所引之缘，任函上月纹自行隐去。", 1);
		return true;
	}

	internal static bool TryChoose(Actor actor, int choiceIndex, int currentYear, out string message)
	{
		message = string.Empty;
		if (choiceIndex < 0 || choiceIndex > 2 || actor?.data == null || !actor.isAlive() || currentYear <= 0)
		{
			message = "此念未能落入水月。";
			return false;
		}
		ReconcileActorState(actor, currentYear, allowTimeoutSettlement: true);
		if (ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState) != StateExploring)
		{
			message = "此次水月游历已经结束。";
			return false;
		}

		string node = ResolveCurrentNode(actor);
		MarkNodeUsed(actor, node);
		ApplyChoice(actor, node, choiceIndex, out string resultText);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult, resultText);

		if (string.Equals(node, NodeQuestion, StringComparison.Ordinal))
		{
			FinishExploration(actor, currentYear, closedByDepth: false, rareAnswered: true, abandoned: false);
			message = resultText;
			return true;
		}

		int nextStep = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationStep) + 1;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationStep, nextStep);

		if (ShouldCloseForOverreach(actor, currentYear, nextStep))
		{
			FinishExploration(actor, currentYear, closedByDepth: true, rareAnswered: false, abandoned: false);
			message = "水月忽然合拢，后路与前路同时消失。";
			return true;
		}

		if (nextStep >= NormalExplorationSteps)
		{
			if (ShouldOpenRareQuestion(actor, currentYear))
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRarePending, 1);
				XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult,
					resultText + "\n\n本以为游历将尽，诸般倒影却忽然同时静止。水上没有人影，只多出了一道不知从何处来的问意。"
				);
				message = "水月未散，另有一问。";
				return true;
			}
			FinishExploration(actor, currentYear, closedByDepth: false, rareAnswered: false, abandoned: false);
			message = "三重水月已尽。";
			return true;
		}

		message = resultText;
		return true;
	}

	internal static bool TryEndExploration(Actor actor, int currentYear, out string message)
	{
		message = string.Empty;
		if (actor?.data == null || currentYear <= 0
			|| ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState) != StateExploring)
		{
			message = "此刻水月已不应念，无缘再收束前路。";
			return false;
		}
		FinishExploration(actor, currentYear, closedByDepth: false, rareAnswered: false, abandoned: true);
		message = "持函者主动收念归身。";
		return true;
	}

	private static void ReconcileOutstandingCredential(int currentYear)
	{
		if (!XjYuanZhaoKongZhengEvent.TryGetActiveFounderCredential(out long actorId, out _)) return;
		if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive())
		{
			XjYuanZhaoKongZhengEvent.ClearActiveFounderCredential(actorId);
			return;
		}
		ReconcileActorState(actor, currentYear, allowTimeoutSettlement: true);
		int state = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState);
		if (state != StateHolding && state != StateExploring)
		{
			XjYuanZhaoKongZhengEvent.ClearActiveFounderCredential(actorId);
		}
	}

	private static void ReconcileActorState(Actor actor, int currentYear, bool allowTimeoutSettlement)
	{
		if (actor?.data == null || currentYear <= 0) return;
		int state = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState);
		long actorId = ((BaseSystemData)actor.data).id;
		if (state == StateHolding)
		{
			int until = ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear);
			if (until > 0 && currentYear > until)
			{
				XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialState, StateClosed);
				XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear, 0);
				XjYuanZhaoKongZhengEvent.ClearActiveFounderCredential(actorId);
			}
			return;
		}
		if (state != StateExploring || !allowTimeoutSettlement) return;
		int started = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationStartedYear);
		if (started > 0 && currentYear > started + ExplorationSessionYears)
		{
			FinishExploration(actor, currentYear, closedByDepth: false, rareAnswered: false, abandoned: true, timedOut: true);
		}
	}

	private static bool TryPickCredentialHolder(int currentYear, out Actor picked)
	{
		picked = null;
		IReadOnlyList<long> source = XjCultivatorCache.GetZiFuOrHigherIds();
		if (source == null || source.Count == 0) return false;

		List<long> eligible = new List<long>(Math.Min(source.Count, 128));
		for (int i = 0; i < source.Count; i++)
		{
			long actorId = source[i];
			if (!XjScheduler.ResolveActor(actorId, out Actor actor) || actor?.data == null || !actor.isAlive()) continue;
			string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
			if (!string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
				&& !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)) continue;
			if (ReadInt(actor, XjActorDataKeys.YuanZhaoCredentialState) != StateNone) continue;
			if (XjYuanZhaoFounderAudienceSystem.HasActiveInvitation(actor, currentYear, out _)) continue;
			eligible.Add(actorId);
		}
		if (eligible.Count == 0) return false;
		eligible.Sort();
		int serial = Math.Max(0, XjYuanZhaoKongZhengEvent.FounderTotalCredentialIssued) + 1;
		int index = XjDeterministicHash.PositiveIndex((long)currentYear * 65537L + serial * 131071L,
			"yuanzhao_credential_holder", eligible.Count);
		return XjScheduler.ResolveActor(eligible[index], out picked) && picked?.data != null && picked.isAlive();
	}

	private static void IssueCredential(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;
		int serial = Math.Max(0, XjYuanZhaoKongZhengEvent.FounderTotalCredentialIssued) + 1;
		int untilYear = currentYear + CredentialLifetimeYears;
		int nextYear = currentYear + RollCredentialInterval(currentYear, serial);

		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialIssueYear, currentYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear, untilYear);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialSerial, serial);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialState, StateHolding);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationStartedYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationStep, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationUsedMask, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRarePending, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationOutcome, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationResolvedYear, 0);
		XjYuanZhaoKongZhengEvent.RecordFounderCredentialIssued(
			actorId, XjStringHelper.ActorName(actor, "无名修士"), currentYear, untilYear, nextYear);

		string actorName = XjStringHelper.ActorName(actor, "无名修士");
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			"【照真请凭函】" + XjChronology.FormatYear(currentYear) + "，一封薄如水影的无字凭函不知由何处流出，最终落到" + actorName
			+ "手中。函上只在月下显出‘水月照真’四字，既不可转授，也不是道尊亲召；持函者可在" + untilYear
			+ "年前展函，以一念进入洞天外层游历。",
			iconId: XjEventIconCatalog.DongTianOpen,
			category: XjAnnouncementCategory.DongTian);
		RecordHistory(actor, currentYear, "YuanZhaoCredentialIssued", actorName + "得照真请凭函",
			"水月照真有一封〖" + CredentialName + "〗流入尘世，最终认主于" + actorName
			+ "。此函只引一念照入水月外层，并非洞门常开，更非创道者亲召。", 3, worldVisible: true);
	}

	private static int RollCredentialInterval(int currentYear, int serial)
	{
		int span = CredentialMaxIntervalYears - CredentialMinIntervalYears + 1;
		return CredentialMinIntervalYears + XjDeterministicHash.PositiveIndex(
			(long)currentYear * 8191L + Math.Max(1, serial) * 524287L,
			"yuanzhao_credential_interval",
			span);
	}

	private static string ResolveCurrentNode(Actor actor)
	{
		if (actor?.data == null) return NodeMirror;
		if (ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRarePending) > 0) return NodeQuestion;
		int step = Math.Max(0, ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationStep));
		int seed = Math.Max(1, ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed));
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		long actorId = ((BaseSystemData)actor.data).id;

		bool sourceDao = string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			|| string.Equals(daoTu, XjYuanZhaoKongZhengEvent.SourceTaiYin, StringComparison.Ordinal)
			|| string.Equals(daoTu, XjYuanZhaoKongZhengEvent.SourceKanShui, StringComparison.Ordinal);
		if (step == 1 && sourceDao
			&& XjDeterministicHash.Roll01(actorId, seed + step, "yuanzhao_credential_source", daoTu) < 0.42f)
		{
			return NodeSource;
		}
		if (step == 2 && string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal)
			&& XjDeterministicHash.Roll01(actorId, seed + step, "yuanzhao_credential_foundation", daoTu) < 0.30f)
		{
			return NodeFoundation;
		}
		if (step == 2 && string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal)
			&& XjDeterministicHash.Roll01(actorId, seed + step, "yuanzhao_credential_fruit", daoTu) < 0.30f)
		{
			return NodeFruit;
		}

		int usedMask = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationUsedMask);
		int start = XjDeterministicHash.PositiveIndex((long)seed + actorId * 17L + step * 97L,
			"yuanzhao_credential_node", GeneralNodes.Length);
		for (int offset = 0; offset < GeneralNodes.Length; offset++)
		{
			int index = (start + offset) % GeneralNodes.Length;
			if ((usedMask & (1 << index)) != 0) continue;
			return GeneralNodes[index];
		}
		return GeneralNodes[start];
	}

	private static void MarkNodeUsed(Actor actor, string node)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(node)) return;
		for (int i = 0; i < GeneralNodes.Length; i++)
		{
			if (!string.Equals(node, GeneralNodes[i], StringComparison.Ordinal)) continue;
			int usedMask = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationUsedMask);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationUsedMask, usedMask | (1 << i));
			return;
		}
	}

	private static void BuildNodeView(
		Actor actor,
		string node,
		out string title,
		out string body,
		out string choice0,
		out string choice1,
		out string choice2)
	{
		title = "水月照真";
		body = string.Empty;
		choice0 = string.Empty;
		choice1 = string.Empty;
		choice2 = string.Empty;
		switch (node)
		{
			case NodeMirror:
				title = "镜水照形";
				body = "前方只有一方平水。水里映出的并非此刻面容，而是数种彼此矛盾的未来：有人持果，有人败亡，也有人从未踏上今日之道。镜水没有解释哪一幅是真的。";
				choice0 = "凝视本相";
				choice1 = "追问未来倒影";
				choice2 = "闭目不取";
				break;
			case NodeCorridor:
				title = "失月回廊";
				body = "一条没有墙壁的长廊悬在静水上，每走一步，天上的月便少一分。走到尽处或许能看见月从何处缺失，也可能只剩一片无光水面。";
				choice0 = "循缺月而行";
				choice1 = "记下月缺次序";
				choice2 = "原路退一重";
				break;
			case NodeStele:
				title = "无字残碑";
				body = "水中央立着半截古碑，明明没有一字，神识扫过却会自行生出不同经句。它们像是某条道曾经存在过的旁枝，也像是你的心念在给空白找答案。";
				choice0 = "以神识拓读";
				choice1 = "只辨一字";
				choice2 = "不触碑文";
				break;
			case NodeLock:
				title = "锁月石门";
				body = "一道石门横在水上，门无墙、锁无钥。锁孔中却映着一轮完整月影。越想推门，越能感觉到门后并不是‘地方’，而是某段尚未许可你知道的道理。";
				choice0 = "叩门三问";
				choice1 = "试推石门";
				choice2 = "绕门观锁";
				break;
			case NodeSeat:
				title = "无声席";
				body = "一张旧席浮在水面，席前无人，席后也无人。坐下时，四周所有声音都消失了，连自己的念头也像隔着一层水。此处似乎不是用来听人讲法的。";
				choice0 = "坐而不问";
				choice1 = "留一念于席";
				choice2 = "起身作揖";
				break;
			case NodeAbyss:
				title = "静渊无底";
				body = "镜水忽然向下塌陷，化作一口看不见底的深渊。渊中没有妖邪，没有宝光，只有你投下去的每一道神识都会从另一个方向返照回来。";
				choice0 = "照入渊底";
				choice1 = "以月影测深";
				choice2 = "止于水面";
				break;
			case NodeBoat:
				title = "泊舟无岸";
				body = "一叶旧舟横在水上，既无来岸，也无去岸。舟中只放着半截湿润竹篙；每一次拨水，远处都会多出一处本不存在的倒影。";
				choice0 = "登舟循影";
				choice1 = "留岸观舟";
				choice2 = "只取一篙水纹";
				break;
			case NodeLamp:
				title = "水上旧灯";
				body = "一盏没有灯芯的古灯浮在镜水上，灯中却映着一粒冷白月光。它照不亮四周，只会使被照之物显出与平日不同的一层轮廓。";
				choice0 = "以神识点灯";
				choice1 = "熄月观暗";
				choice2 = "绕灯一周";
				break;
			case NodeRain:
				title = "逆雨归天";
				body = "无数细雨从水面倒落向天空，每一滴都裹着一段模糊旧景。它们并非你的记忆，却会在经过身侧时让某些早已遗忘的念头自行苏醒。";
				choice0 = "逆雨而上";
				choice1 = "截一滴细观";
				choice2 = "任雨归天";
				break;
			case NodeTwinMoon:
				title = "两月相背";
				body = "天上与水下各有一轮月，两轮月始终背向彼此。你若只看其中一轮，另一轮便更加明亮；若试图同时看清，两者反而一并模糊。";
				choice0 = "只观天月";
				choice1 = "只观水月";
				choice2 = "不分上下";
				break;
			case NodeName:
				title = "洗名之水";
				body = "一串串人名顺水而来，宗门、家族、尊号与本名层层剥落，最后只剩无法辨认的一点神识痕迹。水流没有抹去那些人，只把‘名’与‘人’暂时分开。";
				choice0 = "追一名到尽头";
				choice1 = "只辨名与人之差";
				choice2 = "任诸名流过";
				break;
			case NodePalace:
				title = "沉殿无门";
				body = "水底沉着一重看不清年代的殿宇，檐角、石阶、廊柱俱全，却偏偏没有门。越靠近，越能确认它并不属于水月照真的真正内景，只是一段被留下的形。";
				choice0 = "潜近石阶";
				choice1 = "在水面描其轮廓";
				choice2 = "知非真殿而止";
				break;
			case NodeSource:
				title = "源流旧影";
				body = "水中依次浮出太阴月轮、坎水玄渊与一线后生的渊照道痕。三者并未合成一物，只在极短的一瞬互相映见。你所修之道恰与这段旧源有涉。";
				choice0 = "追索源流";
				choice1 = "只观旧契";
				choice2 = "不借源名";
				break;
			case NodeFoundation:
				title = "仙基倒影";
				body = "镜水深处浮起一座与你紫府仙基极相似的倒影，却总有一两处结构与你真实所修不同。它既不替你改基，也不告诉你哪一种才算‘正’，只把根脚中的取舍放大到无法忽视。";
				choice0 = "照其根脚";
				choice1 = "只辨一处歧差";
				choice2 = "不以倒影改真基";
				break;
			case NodeFruit:
				title = "果影";
				body = "镜水上出现一枚并不存在于手中的果位倒影。它没有替换你如今的位序，只把‘人持果’与‘果持人’两种景象同时摆在面前。水月并不告诉你哪一种更高。";
				choice0 = "照果不照己";
				choice1 = "照己不照果";
				choice2 = "收果影入心";
				break;
			case NodeQuestion:
				title = "水月一问";
				body = "诸般水景同时归于无波。诸景归寂，水上仍不见人影，也无声息可循；只是某一刻，一道问意像本就存在于你的念头里：\n\n‘若所见皆真，何以还需照？’";
				choice0 = "真不自明，照其来处";
				choice1 = "照为证我，不为证物";
				choice2 = "见与不见，本非一物";
				break;
			default:
				title = "水月旧景";
				body = "水纹重叠，前路一时难辨。";
				choice0 = "观";
				choice1 = "行";
				choice2 = "止";
				break;
		}
	}

	private static void ApplyChoice(Actor actor, string node, int choiceIndex, out string resultText)
	{
		int insight = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight);
		int depth = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth);
		int restraint = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint);
		int addInsight = 0;
		int addDepth = 0;
		int addRestraint = 0;
		resultText = "水纹微动，没有留下可供带走的实物。";

		switch (node)
		{
			case NodeMirror:
				if (choiceIndex == 0) { addInsight = 2; addRestraint = 1; resultText = "你不再分辨哪一幅未来更好，只看所有倒影共同保留下来的那一点本相。数息后，其他未来先后碎去。"; }
				else if (choiceIndex == 1) { addInsight = 1; addDepth = 2; resultText = "你追着最远的一幅倒影向前，镜水立刻生出更多分叉。所得更多，却也更难判断哪些只是被你自己逼出来的答案。"; }
				else { addInsight = 1; addRestraint = 2; resultText = "你闭目不取任何未来。再睁眼时，水里只剩此刻的自己，反而比先前清楚。"; }
				break;
			case NodeCorridor:
				if (choiceIndex == 0) { addInsight = 1; addDepth = 2; resultText = "你顺着缺月不断向前，直到月只剩一线。最后一线月光没有熄灭，而是落进了脚下的水。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 1; resultText = "你停下脚步，只记月相每一次缺损的次序。回廊没有尽头，但其中一段变化已经可以被理解。"; }
				else { addRestraint = 2; resultText = "你退回一步，天上反而多出一分月。原来这条路并不以‘向前’作为唯一尺度。"; }
				break;
			case NodeStele:
				if (choiceIndex == 0) { addInsight = 2; addDepth = 1; resultText = "神识所过，无字碑上浮出数句经义，又在你试图牢记时自行抹去。真正留下来的不是文字，而是一段推演的方法。"; }
				else if (choiceIndex == 1) { addInsight = 1; addRestraint = 2; resultText = "你不贪整篇，只从万千自生经句里辨认一个最稳定的字。碑仍无字，那一字却在心中停住了。"; }
				else { addRestraint = 2; resultText = "你没有以自己的念头替空碑补文。片刻后，那些自行生出的经句全部退去，碑后露出一道原本被遮住的水纹。"; }
				break;
			case NodeLock:
				if (choiceIndex == 0) { addInsight = 2; addDepth = 1; resultText = "你没有求门开，只向门问了三次‘为何锁’。第三问落下时，锁孔中的月影换了方向，却仍未开门。"; }
				else if (choiceIndex == 1) { addDepth = 3; resultText = "你以修为试推石门。门纹丝不动，反而是自身神识被推得向后震开；那一刻你确实看见了门后的一线东西，却无法确认自己是否该看。"; }
				else { addInsight = 2; addRestraint = 1; resultText = "你绕门而行，只观锁纹与水中月影如何相合。门没有开，但‘什么才算钥匙’似乎已经不再只有一种答案。"; }
				break;
			case NodeSeat:
				if (choiceIndex == 0) { addInsight = 1; addRestraint = 2; resultText = "你坐下却不问法。许久之后，消失的声音逐一回来，最后才是自己的念头。你因此分清了哪些念头其实并非必要。"; }
				else if (choiceIndex == 1) { addInsight = 2; addDepth = 1; resultText = "你在席前留下一念，不求回答。那一念沉入水中，再返来时已经少去许多自以为重要的枝节。"; }
				else { addRestraint = 3; resultText = "你向空席作揖便起身。没有人受礼，也没有人回礼；可下一重水景出现得比预想更安静。"; }
				break;
			case NodeAbyss:
				if (choiceIndex == 0) { addInsight = 1; addDepth = 3; resultText = "你把神识照向渊底，每深入一层便从另一侧看见一次自己。直到再向下时，返照已经快于念头本身。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 1; resultText = "你不直接探底，只以月影在不同深处的变形测量它。渊仍无底，却出现了可以比较的尺度。"; }
				else { addRestraint = 3; resultText = "你止于水面。深渊没有因此消失，却第一次不再主动把你的神识拖向更深处。"; }
				break;
			case NodeBoat:
				if (choiceIndex == 0) { addInsight = 1; addDepth = 2; resultText = "你登舟拨水，每一篙都把一处倒影拉近。舟始终没有抵岸，却让你看清‘路’未必要有终点才成立。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 1; resultText = "你留在原处看舟自行漂远，才发现它每次远去都回到同一条水纹，只是角度不同。"; }
				else { addInsight = 1; addRestraint = 2; resultText = "你没有登舟，只记下一篙划开的水纹。水纹很快复平，留下的却是起伏之前与之后的差别。"; }
				break;
			case NodeLamp:
				if (choiceIndex == 0) { addInsight = 2; addDepth = 1; resultText = "神识落入古灯，那一点月光骤然明亮。被照见的不是物，而是诸物彼此遮掩的边界。"; }
				else if (choiceIndex == 1) { addDepth = 1; addRestraint = 2; resultText = "你压下灯中月光，让四周归暗。失去照明之后，原本不起眼的水声反而成了唯一可循的线索。"; }
				else { addInsight = 1; addRestraint = 2; resultText = "你绕灯一周，不点也不灭。不同角度下，同一粒月光显出数种轮廓，却没有一种独占真相。"; }
				break;
			case NodeRain:
				if (choiceIndex == 0) { addDepth = 3; addInsight = 1; resultText = "你逆着倒雨而上，旧景从身侧飞快退去。越往高处，景象越陌生，最后只剩一种无法命名的熟悉感。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 1; resultText = "你截住一滴雨。滴中旧景只存一瞬，却足够让你明白：被唤醒的念头未必属于那段景，本就是你自己的痕迹。"; }
				else { addRestraint = 3; resultText = "你没有截取任何旧景，只看万雨归天。少取一物，并不妨碍你看清它们最终都往哪里去。"; }
				break;
			case NodeTwinMoon:
				if (choiceIndex == 0) { addInsight = 1; addDepth = 2; resultText = "你只看天月，水下那轮反而愈发清亮。直到此刻，你才察觉自己所谓‘只看一边’，仍被另一边所定义。"; }
				else if (choiceIndex == 1) { addInsight = 2; addDepth = 1; resultText = "你只看水月。倒影并不比天上的月更虚，只是它必须借水才能显形。"; }
				else { addInsight = 2; addRestraint = 2; resultText = "你索性不分上下。两月短暂重叠成一道极淡圆痕，旋即各归其位。"; }
				break;
			case NodeName:
				if (choiceIndex == 0) { addDepth = 2; addInsight = 1; resultText = "你追着一个名字直到最下游。尊号、姓氏、本名尽去之后，仍有一点不可被文字替代的痕迹留在水里。"; }
				else if (choiceIndex == 1) { addInsight = 3; resultText = "你不追任何具体人物，只辨‘名’与‘人’何时重合、何时分离。许多原本理所当然的称谓忽然显得并不牢靠。"; }
				else { addRestraint = 3; resultText = "你任诸名流过，不试图据名索人。水流清下来时，你反而更明白哪些东西本就不应由名号决定。"; }
				break;
			case NodePalace:
				if (choiceIndex == 0) { addDepth = 2; addInsight = 1; resultText = "你潜到石阶前，没有找到门，只看见层层水纹在殿柱间重复。那并非真正宫阙，更像一段被保存下来的形制。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 1; resultText = "你留在水面，将整座沉殿的轮廓逐段描出。描到最后，少了门反而成了最明确的一处信息。"; }
				else { addRestraint = 3; resultText = "你既知其非真殿，便不因形似而强求进入。水底宫阙仍在，却不再牵动你的去意。"; }
				break;
			case NodeSource:
				if (choiceIndex == 0) { addInsight = 3; addDepth = 1; resultText = "你顺三道旧痕追索其先后，只看见太阴与坎水并非被渊照吞并，而是在某一点上彼此成镜。源流因此清了一线。"; }
				else if (choiceIndex == 1) { addInsight = 2; addRestraint = 2; resultText = "你只观三道之间曾经成立的旧契，不把其中任何一道强称为另一道的根。水中三影随即各归其位。"; }
				else { addRestraint = 3; resultText = "你不借太阴、坎水的名义解释自己所修之道。三道旧影退去后，反而留下了一条最薄、却最属于你自己的痕。"; }
				break;
			case NodeFoundation:
				if (choiceIndex == 0) { addInsight = 2; addDepth = 1; resultText = "你把注意力放在仙基最初承力之处。倒影里的差异没有替你改基，却让一处平日被境界遮住的根脚重新显出来。"; }
				else if (choiceIndex == 1) { addInsight = 3; addRestraint = 1; resultText = "你只辨其中一处歧差，不试图推翻整座仙基。那一点偏移越看越小，最后化作一种可以带回去慢慢参悟的取舍。"; }
				else { addInsight = 1; addRestraint = 3; resultText = "你不让倒影反客为主。仙基之形只是照见之物，真正承道的仍是你已经走过的修行。"; }
				break;
			case NodeFruit:
				if (choiceIndex == 0) { addInsight = 2; addDepth = 2; resultText = "你只照果位，不照持果之人。果影越发清楚，人影却逐渐淡去；直到你意识到这种清楚本身就是一种偏差。"; }
				else if (choiceIndex == 1) { addInsight = 3; addRestraint = 1; resultText = "你移开对果影的目光，只看自己为何要持、为何能持。果影没有消失，却第一次不再占满整面镜水。"; }
				else { addInsight = 1; addDepth = 1; addRestraint = 2; resultText = "你没有伸手取那枚并不存在的果，只把它的倒影收进心中。它随即碎成数条关于位序与人的不同解释。"; }
				break;
			case NodeQuestion:
				if (choiceIndex == 0) { addInsight = 3; addRestraint = 2; resultText = "你答：‘真不自明，故照其来处。’水面不作评判，只将你的回答原样照回。第二次看见同一句话时，其中已有一处与你先前理解不同。"; }
				else if (choiceIndex == 1) { addInsight = 2; addDepth = 2; resultText = "你答：‘照是为证我，不为证物。’问意没有再追问，只让镜中的你与镜外的你短暂错开半步。"; }
				else { addInsight = 1; addRestraint = 3; resultText = "你答：‘见与不见，本非一物。’水月静了许久。没有人说对错，只有那道问意从此不再属于外物。"; }
				break;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight, Math.Min(99, insight + addInsight));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth, Math.Min(99, depth + addDepth));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint, Math.Min(99, restraint + addRestraint));
	}

	private static bool ShouldCloseForOverreach(Actor actor, int currentYear, int step)
	{
		if (actor?.data == null || step <= 0) return false;
		int depth = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth);
		int insight = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight);
		int restraint = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint);
		if (depth < 5 || depth <= insight + restraint) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		int seed = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed);
		float chance = Math.Clamp(0.12f + (depth - insight - restraint) * 0.05f, 0.12f, 0.32f);
		return XjDeterministicHash.Roll01(actorId, seed + currentYear + step, "yuanzhao_credential_overreach", "depth") < chance;
	}

	private static bool ShouldOpenRareQuestion(Actor actor, int currentYear)
	{
		if (actor?.data == null || XjDongTianRegistry.HasYuanZhaoAudienceRecord(actor)) return false;
		int insight = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight);
		int depth = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth);
		int restraint = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint);
		int score = insight + restraint + depth / 2;
		if (score < 8) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		daoTu = (daoTu ?? string.Empty).Trim();
		bool related = string.Equals(daoTu, XjYuanZhaoKongZhengEvent.DaoTu, StringComparison.Ordinal)
			|| string.Equals(daoTu, XjYuanZhaoKongZhengEvent.SourceTaiYin, StringComparison.Ordinal)
			|| string.Equals(daoTu, XjYuanZhaoKongZhengEvent.SourceKanShui, StringComparison.Ordinal);
		string realm = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		float chance = related ? 0.15f : 0.05f;
		if (string.Equals(realm, XjRealmIds.JinDan, StringComparison.Ordinal)) chance += 0.03f;
		long actorId = ((BaseSystemData)actor.data).id;
		int seed = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed);
		return XjDeterministicHash.Roll01(actorId, seed + currentYear, "yuanzhao_credential_question", daoTu) < chance;
	}

	private static void FinishExploration(
		Actor actor,
		int currentYear,
		bool closedByDepth,
		bool rareAnswered,
		bool abandoned,
		bool timedOut = false)
	{
		if (actor?.data == null || currentYear <= 0) return;
		long actorId = ((BaseSystemData)actor.data).id;
		int insight = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationInsight);
		int depth = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationDepth);
		int restraint = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationRestraint);
		int score = insight + restraint + depth / 2;
		int huiGuangGain = 0;
		bool grantComprehension = false;

		if (!timedOut)
		{
			if (score >= 10) { huiGuangGain = 3; grantComprehension = true; }
			else if (score >= 7) { huiGuangGain = 2; grantComprehension = ShouldGrantMidComprehension(actor, currentYear); }
			else if (score >= 4) huiGuangGain = 1;
			if (rareAnswered) huiGuangGain = Math.Min(4, huiGuangGain + 1);
			if (abandoned) huiGuangGain = Math.Min(huiGuangGain, 1);
		}

		if (huiGuangGain > 0)
		{
			XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float before);
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang,
				XjDaoHuiPolicy.Add(before, huiGuangGain, XjDaoHuiPolicy.RareGrowthCeiling));
		}
		string realm = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (grantComprehension && string.Equals(realm, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			XjEventDongTianBonusService.GrantShenTongComprehensionBenefit(actor, currentYear);
		}

		string outcome;
		if (timedOut)
		{
			outcome = "请凭函所引的一念在尘世岁月中渐渐淡去。等持函者再次回神，水月已经闭合；此次游历未能完整收束，也没有额外取走什么。";
		}
		else if (closedByDepth)
		{
			outcome = "持函者涉入过深，水月没有伤其性命，只在某一刻同时收去前路与后路，将那一念送回尘世。此次因缘至此而止。";
		}
		else if (rareAnswered)
		{
			outcome = "三重水月之后，持函者又遇到一次没有来处的‘水月一问’。洞中仍无人现身，那道问意也不足以称作正式传召；回答落定后，诸景尽归无波。";
		}
		else if (abandoned)
		{
			outcome = "持函者没有继续追索后面的水月异景，而是自行收念归身。能带走的只有已经照见的部分，其余因缘不再强求。";
		}
		else if (score >= 10)
		{
			outcome = "三重水月次第合拢。持函者没有得到任何可称作‘道尊赐物’的东西，却把数段彼此矛盾的见闻收成了一点自己的理解。";
		}
		else if (score >= 6)
		{
			outcome = "水月退去，见闻未尽成法，却留下几处可以反复参悟的痕迹。洞天没有为这次游历给出评语。";
		}
		else
		{
			outcome = "水纹散尽。持函者看过几重异景，却没有强行把它们炼成所得；这次因缘更多只是见过，而非获得。";
		}
		if (huiGuangGain > 0) outcome += " 道慧由此增长" + huiGuangGain + "点。";
		if (grantComprehension && string.Equals(realm, XjRealmIds.ZiFu, StringComparison.Ordinal))
			outcome += " 其紫府神通参悟在未来五十年得到一层水月余照。";

		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialState, StateResolved);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoCredentialUntilYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationRarePending, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationOutcome, outcome);
		XjActorAccessor.SetString(actor, XjActorDataKeys.YuanZhaoExplorationLastResult, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.YuanZhaoExplorationResolvedYear, currentYear);
		XjYuanZhaoKongZhengEvent.ClearActiveFounderCredential(actorId);
		XjYuanZhaoKongZhengEvent.RecordFounderCredentialResolved(actorId);

		string actorName = XjStringHelper.ActorName(actor, "无名修士");
		XjBroadcastSystem.BroadcastBLevelActorEvent(
			actor,
			"【水月游历】" + actorName + "循〖" + CredentialName + "〗所引，自水月照真归来。" + outcome,
			iconId: XjEventIconCatalog.DongTianOpen,
			category: XjAnnouncementCategory.DongTian);
		RecordHistory(actor, currentYear, "YuanZhaoCredentialResolved", actorName + "游水月照真",
			outcome, rareAnswered ? 4 : 2);
	}

	private static bool ShouldGrantMidComprehension(Actor actor, int currentYear)
	{
		if (actor?.data == null) return false;
		long actorId = ((BaseSystemData)actor.data).id;
		int seed = ReadInt(actor, XjActorDataKeys.YuanZhaoExplorationSeed);
		return XjDeterministicHash.Roll01(actorId, seed + currentYear, "yuanzhao_credential_comprehension", "mid") < 0.55f;
	}

	private static void RecordHistory(
		Actor actor,
		int currentYear,
		string eventType,
		string title,
		string detail,
		int importance,
		bool worldVisible = false)
	{
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		int x = -1;
		int y = -1;
		long cityId = 0L;
		try
		{
			WorldTile tile = actor == null ? null : ((BaseSimObject)actor).current_tile;
			if (tile != null) { x = tile.pos.x; y = tile.pos.y; }
			if (actor?.city?.data != null) cityId = actor.city.data.id;
		}
		catch { }
		XjHistoryVisibility visibility = XjHistoryVisibility.Personal | XjHistoryVisibility.CenturyCandidate;
		if (worldVisible) visibility |= XjHistoryVisibility.World;
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.SecretRealm,
			title,
			detail,
			importance,
			actorId: actorId,
			actorName: XjStringHelper.ActorName(actor, "无名修士"),
			cityId: cityId,
			year: currentYear,
			locationX: x,
			locationY: y,
			eventType: eventType,
			visibilityFlags: (int)visibility,
			mirrorToWorldLog: false);
	}

	private static int ReadInt(Actor actor, string key)
	{
		return actor?.data != null && XjActorAccessor.TryGetInt(actor, key, out int value) ? value : 0;
	}

	private static string ReadString(Actor actor, string key)
	{
		return actor?.data != null && XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
	}
}
