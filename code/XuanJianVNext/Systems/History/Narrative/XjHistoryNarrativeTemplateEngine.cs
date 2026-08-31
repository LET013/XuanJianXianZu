using System;
using System.Collections.Generic;
using System.Text;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Narrative;

/// <summary>
/// 三卷共用的事实提取与模板库。视角选择由 Personal/Family/Sect NarrativeAdapter 负责。
/// 参考 ActorHistory 的做法：每条记录必须写清“谁、做了什么、涉及谁或何处、结果如何”，
/// 不用“又添一桩旧事”“有所牵连”等空泛占位句，也不虚构底本中不存在的动机与结果。
/// </summary>
internal static class XjHistoryNarrativeTemplateEngine
{
	private const string ActorColor = "#F2E4B7";
	private const string FamilyColor = "#A7E08A";
	private const string SectColor = "#FFD37A";
	private const string PlaceColor = "#8EB6D9";
	private const string ItemColor = "#B9D88F";
	private const string RealmColor = "#B7A7FF";
	private const string NumberColor = "#E4A66A";


	internal static bool CanWritePerspective(XjCodexHistoryItem item, XjHistoryVisibility visibility)
	{
		if (item == null) return false;
		string type = item.EventType ?? string.Empty;
		string text = Join(type, item.Title, item.Body);
		if (IsRoutineCraftEvent(item, text)) return false;
		if (!IsDongTianSurvivalEvent(item)
			&& !IsDongTianDeathNarrativeEvent(item)
			&& (EqualsType(type, "CaiQiCompleted")
				|| ContainsAny(text, "采气进度", "已采气", "采气待启", "普通采气", "获得真元", "真元+10", "真元+20")))
		{
			return false;
		}

		if (visibility == XjHistoryVisibility.Personal)
		{
			return EqualsType(type, "Birth")
				|| EqualsType(type, "AptitudeGranted")
				|| EqualsType(type, "FamilyMemberConfirmed")
				|| StartsWithType(type, "BreakthroughSuccess")
				|| EqualsType(type, "BreakthroughBlocked")
				|| IsDongTianSurvivalEvent(item)
				|| IsDongTianDeathNarrativeEvent(item)
				|| StartsWithType(type, "WeaponArtInsight:")
				|| IsRenDanEvent(type)
				|| IsDeathEvent(item, text)
				|| IsSectFoundationEvent(item, text)
				|| IsOfficeEvent(item, text)
				|| IsGongFaEvent(item, text)
				|| IsQiuJinFaEvent(item, text)
				|| IsRetainedCraftEvent(text)
				|| StartsWithType(type, "SecretRealmTournamentWinner:");
		}

		if (visibility == XjHistoryVisibility.Family)
		{
			return EqualsType(type, "Birth")
				|| EqualsType(type, "AptitudeGranted")
				|| EqualsType(type, "FamilyMemberConfirmed")
				|| StartsWithType(type, "BreakthroughSuccess")
				|| IsDeathEvent(item, text)
				|| IsDongTianSurvivalEvent(item)
				|| IsDongTianDeathNarrativeEvent(item)
				|| StartsWithType(type, "WeaponArtInsight:")
				|| IsFamilySupportEvent(item, text)
				|| IsSectFoundationEvent(item, text)
				|| IsOfficeEvent(item, text)
				|| IsGongFaEvent(item, text)
				|| IsQiuJinFaEvent(item, text)
				|| IsCaiQiFaEvent(item, text)
				|| IsRetainedCraftEvent(text)
				|| ContainsAny(text, "族议", "家族", "族中", "本家", "族库", "立功", "责罚", "血仇", "复起");
		}

		if (visibility == XjHistoryVisibility.Sect)
		{
			return IsSectFoundationEvent(item, text)
				|| IsLectureEvent(item, text)
				|| IsSectRelationEvent(item, text)
				|| IsSectGovernanceEvent(item, text)
				|| IsOfficeEvent(item, text)
				|| IsGongFaEvent(item, text)
				|| IsQiuJinFaEvent(item, text)
				|| IsCaiQiFaEvent(item, text)
				|| IsRetainedCraftEvent(text)
				|| StartsWithType(type, "SecretRealmTournament")
				|| (StartsWithType(type, "BreakthroughSuccess") && IsHighRealmText(text))
				|| (IsDeathEvent(item, text) && IsHighRealmText(text))
				|| ContainsAny(text, "山门", "宗门", "诸峰", "峰主", "宗主", "灭宗", "破阵", "大比");
		}

		return false;
	}
	internal static XjHistoryNarrativeEntry ComposePerspective(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId, string subjectName)
	{
		if (item == null) return new XjHistoryNarrativeEntry();
		if (IsRenDanEvent(item.EventType)) return ComposeRenDan(item, visibility, subjectId, subjectName);
		if (StartsWithType(item.EventType, "WeaponArtInsight:")) return ComposeWeaponArt(item, visibility, subjectId, subjectName);
		if (IsDongTianSurvivalEvent(item)) return ComposeDongTianSurvival(item, visibility, subjectId, subjectName);
		if (IsDongTianDeathNarrativeEvent(item)) return ComposeDongTianDeathNarrative(item, visibility, subjectId, subjectName);
		if (StartsWithType(item.EventType, "SecretRealmTournamentWinner:")) return ComposeSecretRealmTournamentWinner(item, visibility, subjectId, subjectName);
		if (StartsWithType(item.EventType, "SecretRealmTournament:")) return ComposeSecretRealmTournament(item, visibility, subjectId, subjectName);
		if (TryComposeNovelizedCommonEvent(item, visibility, subjectId, subjectName, out XjHistoryNarrativeEntry novelized)) return novelized;

		string plainText = visibility switch
		{
			XjHistoryVisibility.Personal => ComposePersonal(item, subjectId, subjectName),
			XjHistoryVisibility.Family => ComposeFamily(item, subjectId, subjectName),
			XjHistoryVisibility.Sect => ComposeSect(item, subjectId, subjectName),
			_ => RewriteRecordedFact(item, visibility, subjectId, subjectName)
		};

		return BuildEntry(item, plainText, BuildConcreteNote(item), ResolveTag(item), ResolveAccent(item));
	}


	private static bool TryComposeNovelizedCommonEvent(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName,
		out XjHistoryNarrativeEntry entry)
	{
		entry = null;
		string type = item?.EventType ?? string.Empty;
		string sourceText = Join(item?.Title, item?.Body);
		if (StartsWithType(type, "BreakthroughSuccess"))
		{
			entry = ComposeBreakthroughNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsDeathEvent(item, sourceText))
		{
			entry = ComposeDeathNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsSectFoundationEvent(item, sourceText))
		{
			entry = ComposeSectFoundationNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsLectureEvent(item, sourceText))
		{
			entry = ComposeLectureNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsFamilySupportEvent(item, sourceText))
		{
			entry = ComposeFamilySupportNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsSectRelationEvent(item, sourceText))
		{
			entry = ComposeSectRelationNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsSectGovernanceEvent(item, sourceText))
		{
			entry = ComposeSectGovernanceNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsOfficeEvent(item, sourceText))
		{
			entry = ComposeOfficeNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsRetainedCraftEvent(sourceText))
		{
			entry = ComposeRetainedCraftNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsCaiQiFaEvent(item, sourceText))
		{
			entry = ComposeCaiQiFaNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsQiuJinFaEvent(item, sourceText))
		{
			entry = ComposeQiuJinFaNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (IsGongFaEvent(item, sourceText))
		{
			entry = ComposeGongFaNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (ContainsAny(sourceText, "传承", "得授"))
		{
			entry = ComposeInheritanceNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		if (ContainsAny(sourceText, "宣战", "交锋", "斗法", "血仇", "寻仇", "争端", "冲突", "复仇"))
		{
			entry = ComposeConflictNarrative(item, visibility, subjectId, subjectName);
			return true;
		}
		return false;
	}

	private static XjHistoryNarrativeEntry ComposeDongTianSurvival(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body);
		string realmName = ExtractRealmNameFromText(item.Body);
		if (string.IsNullOrWhiteSpace(realmName)) realmName = ExtractRealmNameFromText(sourceText);
		if (string.IsNullOrWhiteSpace(realmName)) realmName = "奇遇洞天";
		string reward = ExtractDongTianSurvivalReward(item.Body, sourceText, realmName);
		string actor = NarrativeActorName(FirstNonEmpty(
			visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty,
			item.ActorName,
			item.RelatedActorName,
			"一名修士"));
		string family = FirstNonEmpty(
			visibility == XjHistoryVisibility.Family ? subjectName : string.Empty,
			item.FamilyName,
			item.RelatedFamilyName,
			"其族");
		string sect = FirstNonEmpty(
			visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty,
			item.SectName,
			item.RelatedSectName,
			"其宗");
		int variant = StableVariant(item, 3);
		string opening = BuildDongTianOpening(realmName, variant);
		string rewardNarrative = BuildDongTianRewardNarrative(reward, actor, visibility, family, sect);
		string text;
		if (visibility == XjHistoryVisibility.Family)
		{
			text = opening + family + "族人" + actor + "也在入洞之列。待洞门再开时，他自其中安然归来。" + rewardNarrative;
		}
		else if (visibility == XjHistoryVisibility.Sect)
		{
			text = opening + sect + "门下" + actor + "循着洞门逸散的灵机入内。后来他重新踏出洞门，仍归山门修行。" + rewardNarrative;
		}
		else
		{
			text = variant switch
			{
				0 => opening + actor + "循着洞门逸散的灵机踏入其中。待洞门再开时，他从尚未散尽的光影里走了出来。" + rewardNarrative,
				1 => opening + actor + "也在入洞之列。后来洞门重新泛起灵光，他自其中安然归返。" + rewardNarrative,
				_ => opening + actor + "没有在洞门之外停留太久，收敛气息便迈步入内。再见天光时，他已从洞中归来。" + rewardNarrative
			};
		}
		return BuildEntry(item, text, string.Empty, "洞天归来", "#A7E08A");
	}

	private static XjHistoryNarrativeEntry ComposeDongTianDeathNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body);
		List<string> bracketed = ExtractDelimitedValues(sourceText, '【', '】');
		string realmName = bracketed.Count > 0 ? bracketed[0] : ExtractRealmNameFromText(sourceText);
		if (string.IsNullOrWhiteSpace(realmName)) realmName = "奇遇洞天";
		string actor = NarrativeActorName(FirstNonEmpty(
			visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty,
			item.ActorName,
			item.RelatedActorName,
			"一名修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string opening = BuildDongTianOpening(realmName, StableVariant(item, 3));
		string text = visibility switch
		{
			XjHistoryVisibility.Family => opening + family + "族人" + actor + "随众入洞，自此却再未归来。洞门闭合之后，族谱上只余其名与这一场未竟的机缘。",
			XjHistoryVisibility.Sect => opening + sect + "门下" + actor + "入内探寻，最终将性命留在洞中。山门此番少了一名弟子，也多了一桩后来者不敢轻忽的旧事。",
			_ => opening + actor + "踏入其中，却没能等到重见天光的那一刻。他的修途止于洞内，姓名也因此与【" + realmName + "】长久相连。"
		};
		return BuildEntry(item, text, string.Empty, "洞天陨落", "#FF8A80");
	}

	private static XjHistoryNarrativeEntry ComposeSecretRealmTournamentWinner(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string[] parts = (item.EventType ?? string.Empty).Split(':');
		string realmLabel = parts.Length > 1 ? parts[1] : "福地";
		int rank = parts.Length > 2 && int.TryParse(parts[2], out int parsedRank) ? parsedRank : 1;
		int total = parts.Length > 3 && int.TryParse(parts[3], out int parsedTotal) ? parsedTotal : 1;
		int huiGuangGain = parts.Length > 4 && int.TryParse(parts[4], out int parsedGain) ? parsedGain : 0;
		string sourceText = Join(item.Title, item.Body);
		List<string> bracketed = ExtractDelimitedValues(sourceText, '【', '】');
		string realmName = bracketed.Count > 0 ? bracketed[0] : realmLabel;
		string actor = NarrativeActorName(FirstNonEmpty(
			visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty,
			item.ActorName,
			"门中弟子"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string result = huiGuangGain > 0
			? "闭关结束后，他的道慧增长了" + huiGuangGain + "，许多从前晦涩的关隘也随之清晰了几分。"
			: "这一次闭关并未立刻带来可见的突破，却仍让他在洞天灵机中沉淀了一番。";
		string companionText = total > 1
			? "，与其余" + (total - 1) + "名胜者共同进入【" + realmName + "】修炼。"
			: "，独自取得进入【" + realmName + "】修炼的名额。";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => sect + "举行三年大比，" + family + "族人" + actor + "力压同辈，列第" + rank + "。最终共有" + total + "名弟子取得【" + realmName + "】名额，他也在其中。" + result,
			XjHistoryVisibility.Sect => sect + "三年大比再开。诸峰弟子依次登场，" + actor + "最终列第" + rank + companionText + result,
			_ => sect + "三年大比开场时，" + actor + "也在台下候名。数轮考校之后，他列第" + rank + "，从门中诸弟子里争得一席，与共计" + total + "名胜者一同进入【" + realmName + "】。" + result
		};
		return BuildEntry(item, text, string.Empty, realmLabel + "大比", "#9CD7FF");
	}

	private static XjHistoryNarrativeEntry ComposeSecretRealmTournament(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string[] parts = (item.EventType ?? string.Empty).Split(':');
		string realmLabel = parts.Length > 1 ? parts[1] : "福地";
		int total = parts.Length > 2 && int.TryParse(parts[2], out int parsedTotal) ? parsedTotal : 1;
		string sect = FirstNonEmpty(subjectName, item.SectName, "某宗");
		string sourceText = Join(item.Title, item.Body);
		List<string> bracketed = ExtractDelimitedValues(sourceText, '【', '】');
		string realmName = bracketed.Count > 0 ? bracketed[0] : realmLabel;
		string qualification = ContainsAny(sourceText, "宗内尚无真君", "尚无金丹／真君坐镇", "尚无金丹/真君坐镇", "真人退席")
			? "宗内尚无真君，便由紫府真人主持考校，不与下修争席，本届只取筑基及以下门人。"
			: ContainsAny(sourceText, "已有真君坐镇", "已有金丹／真君坐镇", "已有金丹/真君坐镇", "真君退席")
				? "宗内已有真君坐镇，诸位金丹不入场，本届由紫府真人及以下门人参加。"
				: string.Empty;
		string text = sect + "为【" + realmName + "】的修持名额举行三年大比。" + qualification
			+ "诸峰门人依次论道试法，数轮考校后择出" + total + "人入内修持。大比散后，入选者前往秘境，其余门人各归峰中继续修行。";
		return BuildEntry(item, text, string.Empty, realmLabel + "大比", "#FFD37A");
	}

	private static XjHistoryNarrativeEntry ComposeBreakthroughNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body);
		string realm = ResolveRealmLabel(sourceText);
		string actor = NarrativeActorName(FirstNonEmpty(visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty, item.ActorName, "一名修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string personal = realm switch
		{
			"黄冠" => actor + "以性命温养本命神妙，终于脱离凡俗，位列黄冠。服气养性之路自此真正立稳。",
			"真人" => actor + "使神妙由外归身，性命与道气相合，终于求到自身之真，晋为真人。",
			"真君羽士" => actor + "以圆满神妙化出金性，求证得天地认可，自真人登为真君羽士。",
			"筑基" => actor + "在修行中跨过凡俗与修士之间的门槛，道基初成，体内气机也自此沉凝下来。",
			"紫府" => actor + "神意内照，紫府洞开。自这一日起，他眼中的天地已与往日不同。",
			"金丹" => actor + "将诸法归于一处，终于丹成。那一刻起，他不再只是山门或世家中的高修，而是真正足以在天下留下姓名的人物。",
			"结璘" => actor + "于金丹之上再进一步，结璘成仙。旧日修途至此翻过一页，前方已是另一重天地。",
			"炼气" => actor + "引气入体，真元初生，终于从胎息迈入炼气之境。",
			_ => RewriteRecordedFact(item, visibility, subjectId, subjectName)
		};
		string text = visibility switch
		{
			XjHistoryVisibility.Family => realm == "金丹"
				? family + "族人" + actor + "丹成。族中诸修虽未必亲见异象，却都明白，自此以后本族已足以被诸宗诸族郑重记上一笔。"
				: realm == "真君羽士"
					? family + "族人" + actor + "以性命神妙求证得天地认可，登为真君羽士。家门自此多出一位足以影响天下格局的上修。"
				: family + "族人" + personal,
			XjHistoryVisibility.Sect => realm == "金丹"
				? sect + "门中" + actor + "证得金丹。山门上下由此多出一位真君，诸峰之间的格局也随之改变。"
				: realm == "真君羽士"
					? sect + "门中" + actor + "以圆满神妙求证，得天地认果，登为真君羽士。诸峰之间的格局也随之改变。"
				: sect + "门中" + personal,
			_ => personal
		};
		return BuildEntry(item, text, string.Empty, string.IsNullOrWhiteSpace(realm) ? "破境" : realm, ResolveAccent(item));
	}

	private static XjHistoryNarrativeEntry ComposeDeathNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body);
		string actor = NarrativeActorName(FirstNonEmpty(visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty, item.ActorName, item.RelatedActorName, "一名修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string manner = ContainsAny(sourceText, "寿终", "寿尽", "正寝") ? "寿数走到了尽头" : ContainsAny(sourceText, "战死", "斗法", "交锋") ? "死于一场争斗" : "身死道消";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + manner + "。族中自此少了一位熟悉的身影，往日与他相关的旧事，也只能由活着的人继续记得。",
			XjHistoryVisibility.Sect => sect + "门下" + actor + manner + "。山门名录自此少去一人，同门再提起他时，也只能说起从前。",
			_ => actor + manner + "。他一生修行至此止步，往后的天下风云，再与此人无关。"
		};
		return BuildEntry(item, text, string.Empty, "身故", "#FF8A80");
	}

	private static XjHistoryNarrativeEntry ComposeSectFoundationNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "开宗之人"));
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, ExtractFirstBracket(Join(item.Title, item.Body)), "一方山门");
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + "择地开山，立下【" + sect + "】。从此家族在血脉传承之外，又多了一座可以安置门人、收藏法脉的山门。",
			XjHistoryVisibility.Sect => actor + "在此立下【" + sect + "】，开峰收徒，定规传法。最初不过数人立于山门之前，往后的诸峰与门人，却都要从这一日算起。",
			_ => actor + "择地开山，立【" + sect + "】。自此除了自身修行与家族兴衰，他还要承起一座山门的门人、峰脉与法统。"
		};
		return BuildEntry(item, text, string.Empty, "开宗", "#FFD37A");
	}

	private static XjHistoryNarrativeEntry ComposeOfficeNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body, item.EventType);
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, item.RelatedActorName, "一名修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string text;
		if (ContainsAny(sourceText, "峰主", "SectPeakFounded", "SectPeakMaster"))
		{
			string peakName = ExtractPeakName(sourceText);
			text = visibility switch
			{
				XjHistoryVisibility.Family => family + "族人" + actor + "受命执掌" + peakName + "。这一席峰主之位，也使本家在山门诸峰之间有了更重的话语。",
				XjHistoryVisibility.Sect => sect + "诸峰重新定序，" + actor + "受命执掌" + peakName + "。自此该峰弟子、传承与日常事务，皆归其裁定。",
				_ => actor + "受命执掌" + peakName + "。从这一日起，他不仅要自修道业，也要为峰中弟子的前程与传承负责。"
			};
			return BuildEntry(item, text, string.Empty, "峰主", "#9CD7FF");
		}
		if (ContainsAny(sourceText, "宗主", "掌门", "SectSovereign"))
		{
			bool contested = ContainsAny(sourceText, "争位", "势均力敌", "胜出");
			text = visibility switch
			{
				XjHistoryVisibility.Family => family + "族人" + actor + (contested ? "自宗主争位中胜出，接过山门权柄。" : "接过宗主印信，主持山门。") + "其名位落定之后，本家在宗门中的分量也随之改变。",
				XjHistoryVisibility.Sect => contested
					? sect + "诸家与诸峰围绕宗主之位各陈人选，最终由" + actor + "胜出。印信交接之后，山门法旨与诸峰调度皆归其裁决。"
					: sect + "重新议定宗主人选，" + actor + "接过山门印信。自此诸峰次序、门下资源与对外决断，皆由其主持。",
				_ => actor + (contested ? "在宗主争位中压过诸位候选，接掌山门。" : "接过宗主印信，开始主持山门。") + "名位既定，他往后的每一次取舍，都将牵动门下诸峰与诸家。"
			};
			return BuildEntry(item, text, string.Empty, "宗主", "#FFD37A");
		}

		string fact = RewriteRecordedFact(item, visibility, subjectId, subjectName);
		text = visibility switch
		{
			XjHistoryVisibility.Family => fact + family + "也因此重新衡量此人在族中的位置。",
			XjHistoryVisibility.Sect => fact + "名位既定，" + sect + "原有的权责次序也随之调整。",
			_ => fact + "从此以后，他肩上的责任已与从前不同。"
		};
		return BuildEntry(item, text, string.Empty, "名位", "#FFD37A");
	}

	private static XjHistoryNarrativeEntry ComposeLectureNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body, item.EventType);
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "门中上修"));
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		bool trueLord = ContainsAny(sourceText, "真君讲道", "真君大道", "SectLecture:ZhenJun");
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + (trueLord ? "开讲真君大道" : "登坛演说修行关窍") + "。族中若有后辈列席，所得不只是一时真元，更是一次亲闻高境之法的机会。",
			XjHistoryVisibility.Sect => RewriteRecordedFact(item, visibility, subjectId, subjectName) + "讲席散后，门中弟子各自回峰闭关，将坛上所闻一点点化入自己的道业。",
			_ => actor + (trueLord ? "在山门择地开讲，演说真君层次的道途与关窍。" : "择吉日登坛，从采气、筑基直讲到自身所悟。") + "台下门人依次列席，他所讲的每一句话，都可能成为某个后辈日后冲关时的一线明悟。"
		};
		return BuildEntry(item, text, string.Empty, trueLord ? "真君讲道" : "开坛讲法", trueLord ? "#FFD37A" : "#B7A7FF");
	}

	private static XjHistoryNarrativeEntry ComposeFamilySupportNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body, item.EventType);
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "族中后辈"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string patron = NarrativeActorName(FirstNonEmpty(item.RelatedActorName, "族中上修"));
		string text;
		if (ContainsAny(sourceText, "上修定议", "FamilyHighRealmOverride"))
		{
			text = visibility == XjHistoryVisibility.Family
				? family + "原已议定扶持人选，却因" + patron + "亲自出面而改弦更张，最终将族库与传承转向" + actor + "。这一场族议，也让诸房看清了高境上修在族中的分量。"
				: patron + "亲自过问族中扶持之议，将原定人选改为" + actor + "。从此数年，族库、长辈指点与传承机会都会更多地向他倾斜。";
			return BuildEntry(item, text, string.Empty, "上修定议", "#B98AD9");
		}
		if (ContainsAny(sourceText, "举后", "SupportedHeirSelected"))
		{
			text = visibility == XjHistoryVisibility.Family
				? family + "诸房会于族堂，反复衡量资质、年岁与当前关隘，最终推举" + actor + "为这一阶段重点扶持的后辈。"
				: actor + "被族中诸房共同推举。自此他的修行不再只是个人之事，身后已有族库与长辈目光相随。";
			return BuildEntry(item, text, string.Empty, "族议举后", "#A7E08A");
		}
		string recordedSupport = RewriteRecordedFact(item, visibility, subjectId, subjectName);
		text = visibility == XjHistoryVisibility.Family
			? recordedSupport + "族中所求并非一时声势，而是让这名后辈真正越过眼前关隘，将来能够反过来撑起家门。"
			: recordedSupport + "从这一日起，他的修行不再只是独自摸索，族库与长辈的目光也一同落到了他的前路上。";
		return BuildEntry(item, text, string.Empty, "家族扶持", "#A7E08A");
	}

	private static XjHistoryNarrativeEntry ComposeSectRelationNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string left = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "一方宗门");
		string right = FirstNonEmpty(item.RelatedSectName, "另一方宗门");
		string sourceText = Join(item.Title, item.Body, item.EventType);
		string relation = ContainsAny(sourceText, "开战", "临界", "敌意深重") ? "敌对" : ContainsAny(sourceText, "嫌隙", "关系恶化") ? "较差" : "关系生变";
		string text = visibility == XjHistoryVisibility.Sect
			? left + "与" + right + "之间的往来转为“" + relation + "”。门下弟子、附属家族与边界城镇自此都需更加谨慎，一次小冲突也可能牵动两座山门。"
			: RewriteRecordedFact(item, visibility, subjectId, subjectName);
		return BuildEntry(item, text, string.Empty, "宗门关系", relation == "敌对" ? "#FF6B6B" : "#FFAA66");
	}

	private static XjHistoryNarrativeEntry ComposeSectGovernanceNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string type = (item?.EventType ?? string.Empty).Trim();
		string fact = RewriteRecordedFact(item, visibility, subjectId, subjectName);
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item?.SectName, "其宗");
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item?.FamilyName, "其族");
		string text;
		string tag;
		string accent;

		if (EqualsType(type, "SectCommonDuty"))
		{
			text = fact + "五年一度的共务看似只是药田、良材与库藏的增减，真正维系的却是诸峰诸家共同承担山门所需的旧例。";
			tag = "山门共务"; accent = "#9CD7FF";
		}
		else if (StartsWithType(type, "SectMandate:"))
		{
			text = visibility switch
			{
				XjHistoryVisibility.Family => fact + family + "也由此看清，宗主的一纸法旨会如何改变族中下一阶段的资源与位置。",
				XjHistoryVisibility.Sect => fact + "法旨传至诸峰之后，执事与各家依次领命，山门往后的数年也随之转向。",
				_ => fact + "这一道法旨既出，他便不能只顾自身修行，还要等着看门下诸峰能否真正将其落实。"
			};
			tag = "宗主法旨"; accent = "#FFD37A";
		}
		else if (EqualsType(type, "SectDominantFamilyChanged"))
		{
			text = visibility == XjHistoryVisibility.Sect
				? fact + "掌宗之势由此易手，诸峰与附属家族虽仍各守旧位，山门中的轻重次序却已重新排过。"
				: fact + family + "从这一日起不再只是门中一席，而真正握住了调动山门资源与名位的权柄。";
			tag = "掌宗易姓"; accent = "#FFD37A";
		}
		else if (EqualsType(type, "SectFamilyPrivilegeIncident"))
		{
			text = fact + "高境修士的威势一旦落到门中分配之上，受损的便不只是一件重宝或一笔功绩，诸家之间原有的平衡也会随之倾斜。";
			tag = "宗门权争"; accent = "#FF9E80";
		}
		else if (EqualsType(type, "SectFamilySupplyPenalty"))
		{
			text = fact + "山门供养并非虚名，欠下的份额最终仍会落回俸给、名额与族中话语之上。";
			tag = "供养追责"; accent = "#FFAA66";
		}
		else if (EqualsType(type, "SectCityGovernanceTransferred"))
		{
			text = fact + "城中田赋、人口与修士往来仍如旧日运转，真正变化的，是往后由哪一家在山门之下承担这座城的兴衰。";
			tag = "城镇改封"; accent = "#A7E08A";
		}
		else
		{
			text = fact + (visibility == XjHistoryVisibility.Sect
				? sect + "的底蕴不是一日筑成，这次入库也只是诸家共同养山门的一笔。"
				: family + "将余器奉入宗门共库，既是供养山门，也是在门中留下本家的分量。");
			tag = "重宝入库"; accent = "#B7A7FF";
		}
		return BuildEntry(item, text, string.Empty, tag, accent);
	}

	private static XjHistoryNarrativeEntry ComposeCaiQiFaNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = FirstNonEmpty(item.Body, item.Title);
		string name = ExtractLeadingTechniqueName(sourceText, "采气法");
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "门中修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string dao = ExtractValueAfter(sourceText, "道途：");
		string daoText = string.IsNullOrWhiteSpace(dao) ? string.Empty : "，依“" + dao.TrimEnd('。') + "”道途另立法目";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + "将《" + name + "》带入" + sect + "采气法库" + daoText + "。本家尚未入道的后辈，也因此多了一条可循的入门路径。",
			XjHistoryVisibility.Sect => sect + "将《" + name + "》收入采气法库" + daoText + "。从此门中后辈感气之后，不必只靠零散口诀摸索第一步。",
			_ => "《" + name + "》经由" + actor + "之手归入" + sect + "采气法库。它并非高深功法，却决定后来者能否真正迈过修行的第一道门槛。"
		};
		return BuildEntry(item, text, string.Empty, "采气法", "#7FD6C2");
	}

	private static XjHistoryNarrativeEntry ComposeGongFaNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = FirstNonEmpty(item.Body, item.Title);
		string name = ExtractLeadingTechniqueName(sourceText, "功法");
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "门中修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string grade = ExtractValueAfter(sourceText, "品阶：").TrimEnd('。');
		string gradeText = string.IsNullOrWhiteSpace(grade) ? string.Empty : grade + " ";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + "使《" + name + "》归入" + sect + "功法图录。自此本家后辈在山门中，又多了一部可以借阅参悟的" + gradeText + "法门。",
			XjHistoryVisibility.Sect => sect + "将《" + name + "》列入功法图录，定为" + (gradeText.Length > 0 ? gradeText.Trim() : "正式传承") + "。经阁中执事校录之后，此法才真正成为可供后人借阅、参悟与传授的山门法脉。",
			_ => actor + "将《" + name + "》带回" + sect + "。经门中核验后，这部" + gradeText + "功法被收入图录，不再只属于他一人的机缘。"
		};
		return BuildEntry(item, text, string.Empty, "功法", "#C7A7FF");
	}

	private static XjHistoryNarrativeEntry ComposeQiuJinFaNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = FirstNonEmpty(item.Body, item.Title);
		string name = ExtractLeadingTechniqueName(sourceText, "求金法");
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "门中上修"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string authority = ExtractValueAfter(sourceText, "权柄：").TrimEnd('。');
		string authorityText = string.IsNullOrWhiteSpace(authority) ? string.Empty : "，所系权柄为“" + authority + "”";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + "使求金法《" + name + "》归入" + sect + "法库" + authorityText + "。这不是寻常功法，而是一条真正通向金丹果位的法门。",
			XjHistoryVisibility.Sect => sect + "收录求金法《" + name + "》" + authorityText + "。自此门中紫府修士求金之时，终于多出一条可以追索、验证的道路。",
			_ => actor + "将求金法《" + name + "》带回" + sect + authorityText + "。这份传承一旦入库，便足以影响后来数代紫府修士的求金选择。"
		};
		return BuildEntry(item, text, string.Empty, "求金法", "#FFD37A");
	}

	private static XjHistoryNarrativeEntry ComposeRetainedCraftNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string sourceText = Join(item.Title, item.Body);
		string actor = NarrativeActorName(FirstNonEmpty(visibility == XjHistoryVisibility.Personal ? subjectName : string.Empty, item.ActorName, "一名匠人"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string itemName = ExtractFirstBookName(sourceText);
		string kind = ContainsAny(sourceText, "延寿丹") ? "延寿丹" : ContainsAny(sourceText, "紫府灵宝") ? "紫府灵宝" : "金丹法宝";
		string named = string.IsNullOrWhiteSpace(itemName) ? kind : kind + "《" + itemName + "》";
		string text = visibility switch
		{
			XjHistoryVisibility.Family => family + "族人" + actor + "炼成" + named + "。炉火或器光散去后，族中也因此知道，本家又出了一位能成重器的人物。",
			XjHistoryVisibility.Sect => sect + "门下" + actor + "炼成" + named + "。此物既成，山门底蕴也比从前更厚了一分。",
			_ => actor + "守炉运火，最终炼成" + named + "。成品落定的那一刻，他此前多年的百艺积累也终于有了可见的结果。"
		};
		return BuildEntry(item, text, string.Empty, kind, ResolveAccent(item));
	}

	private static XjHistoryNarrativeEntry ComposeInheritanceNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string fact = RewriteRecordedFact(item, visibility, subjectId, subjectName);
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, item.RelatedActorName, "一名修士"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string text = visibility switch
		{
			XjHistoryVisibility.Family => fact + "这份传承虽先落在" + actor + "一人身上，却也让" + family + "后辈第一次看见了一条新的修行道路。",
			XjHistoryVisibility.Sect => fact + sect + "门中由此多出一份可供后人参照的法脉。至于能否真正传下去，还要看后来弟子的造化。",
			_ => fact + "自此以后，这部法门不再只是纸上文字，而真正成了他修途的一部分。"
		};
		return BuildEntry(item, text, string.Empty, ResolveTag(item), ResolveAccent(item));
	}

	private static XjHistoryNarrativeEntry ComposeConflictNarrative(
		XjCodexHistoryItem item,
		XjHistoryVisibility visibility,
		long subjectId,
		string subjectName)
	{
		string fact = RewriteRecordedFact(item, visibility, subjectId, subjectName);
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, "一名修士"));
		string other = NarrativeActorName(FirstNonEmpty(item.RelatedActorName, "另一方"));
		string family = FirstNonEmpty(visibility == XjHistoryVisibility.Family ? subjectName : string.Empty, item.FamilyName, "其族");
		string sect = FirstNonEmpty(visibility == XjHistoryVisibility.Sect ? subjectName : string.Empty, item.SectName, "其宗");
		string text = visibility switch
		{
			XjHistoryVisibility.Family => fact + "自此以后，" + family + "与" + other + "一方之间便多了一段难以轻易揭过的旧账。",
			XjHistoryVisibility.Sect => fact + "这场争端没有只停留在" + actor + "一人身上，" + sect + "也因此被卷入其中。",
			_ => fact + actor + "与" + other + "之间的因果，也从这一刻起更深了一层。"
		};
		return BuildEntry(item, text, string.Empty, "争斗", "#FF9E80");
	}

	private static bool IsDongTianSurvivalEvent(XjCodexHistoryItem item)
	{
		string type = item?.EventType ?? string.Empty;
		string text = Join(item?.Title, item?.Body);
		return EqualsType(type, "DongTianSurvived")
			|| StartsWithType(type, "DongTianSurvived:")
			|| ContainsAny(text, "从【", "洞天生还") && ContainsAny(text, "中生还", "安然归来", "安然归返");
	}

	private static bool IsDongTianDeathNarrativeEvent(XjCodexHistoryItem item)
	{
		string type = item?.EventType ?? string.Empty;
		string text = Join(item?.Title, item?.Body);
		return EqualsType(type, "DongTianDeath")
			|| ContainsAny(text, "陨于【", "洞天陨落", "探索洞天时不幸身陨");
	}

	private static string BuildDongTianOpening(string realmName, int variant)
	{
		string name = string.IsNullOrWhiteSpace(realmName) ? "奇遇洞天" : realmName.Trim();
		if (string.Equals(name, "百川归壑", StringComparison.Ordinal)) return "【百川归壑】洞门重开，四方水气尽向其中汇去，远望如百川倒悬。";
		if (string.Equals(name, "月沉幽府", StringComparison.Ordinal)) return "【月沉幽府】现世时，天光微暗，清冷月色沿着地脉沉入洞门。";
		if (string.Equals(name, "焰照离庭", StringComparison.Ordinal)) return "【焰照离庭】洞门开启，赤光映彻四野，灼热灵机沿地脉一寸寸漫开。";
		if (string.Equals(name, "折锋藏宫", StringComparison.Ordinal)) return "【折锋藏宫】显化之时，金铁清鸣自山腹传出，洞门周围锋芒隐现。";
		if (string.Equals(name, "诸木会春", StringComparison.Ordinal)) return "【诸木会春】开启时，草木一夜返青，浓郁生机从洞门中不断涌出。";
		if (string.Equals(name, "玉枢雷音", StringComparison.Ordinal)) return "【玉枢雷音】现世，云中雷声层层滚过，电光在洞门之外明灭不定。";
		if (string.Equals(name, "照世明关", StringComparison.Ordinal)) return "【照世明关】洞门张开，一线明光横照长空，远近阴翳尽被驱散。";
		if (string.Equals(name, "列炁归庭", StringComparison.Ordinal)) return "【列炁归庭】显化，诸般气机在洞门之外交汇，又一一归入其中。";
		if (string.Equals(name, "镇岳坤舆", StringComparison.Ordinal)) return "【镇岳坤舆】开启时，大地微震，沉厚土德灵机自山河深处升起。";
		return variant == 0
			? "【" + name + "】洞门显化，异样灵机自虚空与地脉之间缓缓铺开。"
			: variant == 1
				? "【" + name + "】再度开启，洞门之外的灵光久久不散。"
				: "【" + name + "】现世，附近修士很快便察觉到了那股不同寻常的气机。";
	}

	private static string BuildDongTianRewardNarrative(string reward, string actor, XjHistoryVisibility visibility, string family, string sect)
	{
		string value = (reward ?? string.Empty).Trim();
		if (value.Length == 0 || ContainsAny(value, "未得奖励", "虽无所得", "未获得可安全接入奖励"))
			return "此行虽未带回可见之物，能够从洞中全身而退，本身便已是不易。";
		string[] rewards = value.Split(new[] { '、' }, StringSplitOptions.RemoveEmptyEntries);
		StringBuilder builder = new StringBuilder();
		for (int i = 0; i < rewards.Length; i++)
		{
			string line = BuildSingleDongTianRewardNarrative(rewards[i].Trim(), actor, visibility, family, sect);
			if (!string.IsNullOrWhiteSpace(line)) builder.Append(line);
		}
		return builder.Length > 0 ? builder.ToString() : actor + "自洞中带回【" + value + "】。此番所得，往后自会在他的修途中显出分量。";
	}

	private static string BuildSingleDongTianRewardNarrative(string value, string actor, XjHistoryVisibility visibility, string family, string sect)
	{
		if (value.StartsWith("命数+", StringComparison.Ordinal))
		{
			string amount = value.Substring("命数+".Length).Trim();
			if (visibility == XjHistoryVisibility.Family)
				return "此行之后，" + actor + "的命数增了" + amount + "。" + family + "众人看不见气数如何流转，却能知道这名族人的前路已与从前不同。";
			if (visibility == XjHistoryVisibility.Sect)
				return "归来之后，" + actor + "的命数增了" + amount + "。" + sect + "门中无人能直接看见那份气数，却都明白此番洞天没有白入。";
			return "此行之后，" + actor + "的命数增了" + amount + "。旁人难见气数流转，他身上那份冥冥中的运势却已与入洞前不同。";
		}
		if (value.StartsWith("真元+", StringComparison.Ordinal))
		{
			string amount = value.Substring("真元+".Length).Trim();
			return "洞天所得被" + actor + "炼入丹田，真元由此增长" + amount + "，距离下一重关隘又近了一步。";
		}
		if (value.IndexOf("真元已蓄积于瓶颈", StringComparison.Ordinal) >= 0)
			return "洞天灵机尽数沉入丹田，却被眼前瓶颈拦住。所得未曾散去，只待来日冲关。";
		if (value.StartsWith("丹方-", StringComparison.Ordinal))
		{
			string name = value.Substring("丹方-".Length).Trim();
			return actor + "从洞中带回丹方《" + name + "》。从此这门收药炼丹之法，也真正有了传承。";
		}
		if (ContainsAny(value, "四品功法", "五品功法", "六品功法"))
			return actor + "在洞中得了一部" + value + "。此法自此归入其身，也为往后的修行多开出一条道路。";
		if (value.IndexOf("法宝-", StringComparison.Ordinal) >= 0)
		{
			int split = value.IndexOf("法宝-", StringComparison.Ordinal);
			string dao = split > 0 ? value.Substring(0, split) : string.Empty;
			string name = value.Substring(split + "法宝-".Length).Trim();
			return actor + "自洞中带出" + (dao.Length > 0 ? dao : string.Empty) + "法宝《" + name + "》。器物灵机凝而不散，显然不是寻常所得。";
		}
		if (value.IndexOf("金丹遗留金性", StringComparison.Ordinal) >= 0)
			return actor + "带回一缕金丹遗留金性，并将其送入家族重宝仓库。此物无声无形，分量却远胜寻常灵物。";
		if (value.IndexOf("洞天营造之法", StringComparison.Ordinal) >= 0)
			return actor + "从中悟得洞天营造之法。此后山门若要开辟自身秘境，便已有了一线可循的门径。";
		return actor + "自洞中带回【" + value + "】。此番所得，往后自会在他的修途中显出分量。";
	}

	private static int StableVariant(XjCodexHistoryItem item, int count)
	{
		if (count <= 1) return 0;
		unchecked
		{
			ulong value = (ulong)(item?.SortSequence ?? 0L);
			string text = Join(item?.EventId, item?.EventType, item?.Title, item?.Body);
			for (int i = 0; i < text.Length; i++) value = value * 1099511628211UL ^ text[i];
			return (int)(value % (ulong)count);
		}
	}

	private static List<string> ExtractDelimitedValues(string text, char open, char close)
	{
		List<string> result = new List<string>();
		if (string.IsNullOrWhiteSpace(text)) return result;
		int cursor = 0;
		while (cursor < text.Length)
		{
			int start = text.IndexOf(open, cursor);
			if (start < 0) break;
			int end = text.IndexOf(close, start + 1);
			if (end < 0) break;
			if (end > start + 1) result.Add(text.Substring(start + 1, end - start - 1).Trim());
			cursor = end + 1;
		}
		return result;
	}

	private static string ExtractRealmNameFromText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		int from = text.IndexOf("从", StringComparison.Ordinal);
		if (from >= 0)
		{
			int end = text.IndexOf("中", from + 1, StringComparison.Ordinal);
			if (end > from + 1) return text.Substring(from + 1, end - from - 1).Trim('【', '】', ' ');
		}
		return ExtractFirstBracket(text);
	}

	private static string ExtractRewardFromText(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		int marker = text.IndexOf("，得", StringComparison.Ordinal);
		if (marker < 0) marker = text.IndexOf("并得", StringComparison.Ordinal);
		if (marker < 0) return string.Empty;
		string value = text.Substring(marker + (text[marker] == '，' ? 2 : 2)).Trim();
		return value.TrimEnd('。').Trim('【', '】');
	}

	private static string ExtractDongTianSurvivalReward(string primaryText, string fallbackText, string realmName)
	{
		string text = string.IsNullOrWhiteSpace(primaryText) ? fallbackText ?? string.Empty : primaryText;
		if (ContainsAny(text, "虽无所得", "未得奖励", "未获得可安全接入奖励", "无所得而归", "空手而归")
			|| ContainsAny(fallbackText, "虽无所得", "未得奖励", "未获得可安全接入奖励", "无所得而归", "空手而归"))
		{
			return string.Empty;
		}

		string reward = ExtractRewardFromText(text);
		if (string.IsNullOrWhiteSpace(reward) && !string.Equals(text, fallbackText, StringComparison.Ordinal))
		{
			reward = ExtractRewardFromText(fallbackText);
		}
		if (string.IsNullOrWhiteSpace(reward)) return string.Empty;

		reward = reward.Trim().TrimEnd('。').Trim('【', '】', ' ');
		if (string.Equals(reward, realmName, StringComparison.Ordinal)
			|| ContainsAny(reward, "洞天归来", "洞天生还", "安然归来", "安然归返"))
		{
			return string.Empty;
		}
		return reward;
	}

	private static string ExtractFirstBracket(string text)
	{
		List<string> values = ExtractDelimitedValues(text, '【', '】');
		return values.Count > 0 ? values[0] : string.Empty;
	}

	private static string ExtractFirstBookName(string text)
	{
		List<string> values = ExtractDelimitedValues(text, '《', '》');
		return values.Count > 0 ? values[0] : string.Empty;
	}

	private static string ComposePersonal(XjCodexHistoryItem item, long subjectId, string subjectName)
	{
		bool primary = item.ActorId == subjectId;
		string actor = NarrativeActorName(FirstNonEmpty(subjectName, primary ? item.ActorName : item.RelatedActorName, "未名修士"));
		string family = FirstNonEmpty(primary ? item.FamilyName : item.RelatedFamilyName, item.FamilyName, item.RelatedFamilyName, "其族");
		string type = item.EventType ?? string.Empty;
		string sourceText = Join(item.Title, item.Body);

		if (EqualsType(type, "Birth") || ContainsAny(sourceText, "降生于世", "新丁降世"))
			return actor + "降生于世，其名自此见于" + family + "族谱。";
		if (EqualsType(type, "AptitudeGranted") || ContainsAny(sourceText, "展露天资", "灵光初显"))
			return actor + "展露天资，灵光初显，正式踏入可修行之列。";
		if (EqualsType(type, "FamilyMemberConfirmed") || ContainsAny(sourceText, "确认入籍", "族人归籍"))
			return actor + "归入" + family + "族谱，自此宗族归属有定。";
		if (StartsWithType(type, "BreakthroughSuccess"))
			return RewriteLeadingActor(item, actor, actor, RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName));
		if (EqualsType(type, "BreakthroughBlocked"))
			return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);
		if (StartsWithType(type, "TechniqueRecovered") || EqualsType(type, "GongFaLost") || EqualsType(type, "CaiQiCompleted"))
			return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);
		if (IsDeathEvent(item, sourceText))
			return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);
		if (IsSectFoundationEvent(item, sourceText) || IsOfficeEvent(item, sourceText))
			return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);
		if (IsRetainedCraftEvent(sourceText))
			return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);

		return RewriteRecordedFact(item, XjHistoryVisibility.Personal, subjectId, subjectName);
	}

	private static string ComposeFamily(XjCodexHistoryItem item, long subjectId, string subjectName)
	{
		bool primary = item.FamilyId == subjectId;
		string family = FirstNonEmpty(subjectName, primary ? item.FamilyName : item.RelatedFamilyName, "未名氏");
		string actor = NarrativeActorName(FirstNonEmpty(primary ? item.ActorName : item.RelatedActorName, item.ActorName, item.RelatedActorName, "族中修士"));
		string type = item.EventType ?? string.Empty;
		string sourceText = Join(item.Title, item.Body);

		if (EqualsType(type, "Birth") || ContainsAny(sourceText, "降生于世", "新丁降世"))
			return family + "新添族人" + actor + "，其名列入族谱。";
		if (EqualsType(type, "AptitudeGranted") || ContainsAny(sourceText, "展露天资", "灵光初显"))
			return family + "族人" + actor + "展露天资，灵光初显，族中自此多了一名修行后辈。";
		if (EqualsType(type, "FamilyMemberConfirmed") || ContainsAny(sourceText, "确认入籍", "族人归籍"))
			return family + "确认" + actor + "归籍，将其姓名正式列入本家族谱。";

		string fact = RewriteRecordedFact(item, XjHistoryVisibility.Family, subjectId, subjectName);
		return RewriteLeadingActor(item, actor, "族中" + actor, fact);
	}

	private static string ComposeSect(XjCodexHistoryItem item, long subjectId, string subjectName)
	{
		bool primary = item.SectId == subjectId;
		string sect = FirstNonEmpty(subjectName, primary ? item.SectName : item.RelatedSectName, "未名宗门");
		string actor = NarrativeActorName(FirstNonEmpty(primary ? item.ActorName : item.RelatedActorName, item.ActorName, item.RelatedActorName, "门中修士"));
		string family = FirstNonEmpty(primary ? item.FamilyName : item.RelatedFamilyName, item.FamilyName, item.RelatedFamilyName, "其族");
		string type = item.EventType ?? string.Empty;
		string sourceText = Join(item.Title, item.Body);

		if (EqualsType(type, "FamilyMemberConfirmed") || ContainsAny(sourceText, "确认入籍", "族人归籍"))
			return sect + "门下" + actor + "归籍" + family + "，其家世归属由此确定。";
		if (EqualsType(type, "Birth") || ContainsAny(sourceText, "降生于世", "新丁降世"))
			return actor + "降生于" + family + "；其后此人入" + sect + "门下，故山门旧录追记其始。";
		if (EqualsType(type, "AptitudeGranted") || ContainsAny(sourceText, "展露天资", "灵光初显"))
			return sect + "门下" + actor + "展露天资，灵光初显。";

		string fact = RewriteRecordedFact(item, XjHistoryVisibility.Sect, subjectId, subjectName);
		return RewriteLeadingActor(item, actor, "门中" + actor, fact);
	}

	/// <summary>
	/// 以真实事件正文为底本，只调整叙述视角与标点。若无法识别事件类别，宁可保留具体事实，
	/// 也绝不退回“旧事入谱”“局势生变”一类无信息句。
	/// </summary>
	private static string RewriteRecordedFact(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId, string subjectName)
	{
		string fact = CleanFact(FirstNonEmpty(item.Body, item.Title, string.Empty));
		if (fact.Length == 0) return string.Empty;

		string actorRaw = visibility == XjHistoryVisibility.Personal
			? FirstNonEmpty(subjectName, item.ActorId == subjectId ? item.ActorName : item.RelatedActorName)
			: visibility == XjHistoryVisibility.Family
				? FirstNonEmpty(item.FamilyId == subjectId ? item.ActorName : item.RelatedActorName, item.ActorName, item.RelatedActorName)
				: FirstNonEmpty(item.SectId == subjectId ? item.ActorName : item.RelatedActorName, item.ActorName, item.RelatedActorName);

		if (!string.IsNullOrWhiteSpace(actorRaw))
		{
			string actor = NarrativeActorName(actorRaw);
			if (visibility == XjHistoryVisibility.Personal) fact = RewriteLeadingActor(item, actorRaw, actor, fact);
			else if (visibility == XjHistoryVisibility.Family) fact = RewriteLeadingActor(item, actorRaw, "族中" + actor, fact);
			else if (visibility == XjHistoryVisibility.Sect) fact = RewriteLeadingActor(item, actorRaw, "门中" + actor, fact);
		}

		return EnsureSentence(fact);
	}

	private static string RewriteLeadingActor(XjCodexHistoryItem item, string actorName, string replacement, string fact)
	{
		if (string.IsNullOrWhiteSpace(fact) || string.IsNullOrWhiteSpace(actorName)) return fact;
		string actor = actorName.Trim();
		string cleanActor = StripRealmSuffix(actor);
		if (fact.StartsWith(actor, StringComparison.Ordinal))
			return replacement + fact.Substring(actor.Length);
		if (!string.Equals(cleanActor, actor, StringComparison.Ordinal) && fact.StartsWith(cleanActor, StringComparison.Ordinal))
			return replacement + fact.Substring(cleanActor.Length);
		return fact;
	}

	private static XjHistoryNarrativeEntry ComposeWeaponArt(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId, string subjectName)
	{
		string[] parts = (item.EventType ?? string.Empty).Split(':');
		string kind = parts.Length > 1 ? parts[1].Trim() : "器";
		int rank = parts.Length > 2 && int.TryParse(parts[2], out int parsed) ? parsed : 0;
		string stage = kind + (rank == 1 ? "芒" : rank == 2 ? "气" : rank == 3 ? "元" : rank >= 4 ? "意" : "艺");
		string actor = NarrativeActorName(FirstNonEmpty(item.ActorName, subjectName, "未名修士"));
		string alias = ExtractChineseQuoted(Join(item.Title, item.Body));
		string text;
		if (visibility == XjHistoryVisibility.Family)
		{
			string family = FirstNonEmpty(subjectName, item.FamilyName, "其族");
			text = family + "族中" + actor + "在长期持" + kind + "修行与实战中悟得" + stage + "。";
		}
		else if (visibility == XjHistoryVisibility.Sect)
		{
			string sect = FirstNonEmpty(subjectName, item.SectName, "其宗");
			text = sect + "门下" + actor + "在持" + kind + "斗法之间悟得" + stage + "。";
		}
		else
		{
			text = actor + "在长期持" + kind + "修行与实战中悟得" + stage + "。";
		}
		if (!string.IsNullOrWhiteSpace(alias)) text += "自此有“" + alias + "”之称。";
		string tag = rank >= 4 && string.Equals(kind, "剑", StringComparison.Ordinal) ? "剑意" : "器艺";
		string accent = rank >= 4 ? "#B7A7FF" : rank >= 3 ? "#9CD7FF" : "#A7E08A";
		return BuildEntry(item, text, BuildConcreteNote(item), tag, accent);
	}

	private static string ExtractChineseQuoted(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		int start = text.IndexOf('“');
		if (start < 0) return string.Empty;
		int end = text.IndexOf('”', start + 1);
		return end > start + 1 ? text.Substring(start + 1, end - start - 1).Trim() : string.Empty;
	}

	private static XjHistoryNarrativeEntry ComposeRenDan(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId, string subjectName)
	{
		string eventType = (item.EventType ?? string.Empty).Trim();
		string victim = NarrativeActorName(FirstNonEmpty(item.ActorName, subjectName, "一名下修"));
		string source = NarrativeActorName(FirstNonEmpty(item.RelatedActorName, "一名上修"));
		string victimFamily = FirstNonEmpty(item.FamilyName, "其族");
		string sourceFamily = FirstNonEmpty(item.RelatedFamilyName, "上修之族");
		bool sourceView = IsRenDanSourceView(item, visibility, subjectId);
		string text;
		string tag;
		string accent;

		if (EqualsType(eventType, "RenDanMarked"))
		{
			if (sourceView)
			{
				tag = "暗定人丹";
				text = source + "暗中择定" + victim + "这名同道途筑基下修为人丹之材，以一桩修行机缘加以扶持，预备施展续途妙法。";
			}
			else
			{
				tag = "筑基奇遇";
				text = victim + "忽得来历不明的修行机缘，真元与道慧皆有增益；其人与" + victimFamily + "当时并不知道，这份机缘来自" + source + "的人丹布置。";
			}
			accent = "#B7A7FF";
		}
		else if (EqualsType(eventType, "RenDanResolved"))
		{
			if (sourceView)
			{
				tag = "续途妙法";
				text = source + "将" + victim + "炼作人丹，施展续途妙法，以其残神补全自身神通；" + sourceFamily + "由此得益。";
			}
			else
			{
				tag = "人丹血劫";
				text = victim + "筑基后遭" + source + "收取，最终被炼作人丹；" + victimFamily + "由此折损一名筑基修士。";
			}
			accent = "#FF8A80";
		}
		else if (EqualsType(eventType, "RenDanLost"))
		{
			tag = sourceView ? "预定人丹已失" : "人丹被截";
			text = sourceView
				? source + "暗中培养的" + victim + "在收丹前被另一名上修截走，原定续途之谋就此落空。"
				: victim + "虽早被" + source + "暗中预定，却在收丹前落入另一名上修之手。";
			accent = "#FFD37A";
		}
		else if (EqualsType(eventType, "RenDanTainted"))
		{
			tag = sourceView ? "人丹掺假" : "人丹血劫";
			text = sourceView
				? source + "收取" + victim + "后才发觉人丹已被旁人动过手脚，续途妙法未成，反受其害。"
				: victim + "被卷入人丹之劫，但其根基或道途已被旁人做过手脚，收丹者续途失败。";
			accent = "#FF9E80";
		}
		else if (EqualsType(eventType, "RenDanMismatch"))
		{
			tag = "人丹不合";
			text = victim + "筑基后的道途与原定布置不合，" + source + "只能中止收丹，续途妙法未曾施行。";
			accent = "#FFD37A";
		}
		else if (EqualsType(eventType, "RenDanPlanFailed"))
		{
			tag = "人丹之谋中止";
			text = source + "对" + victim + "的人丹布置未能走到收丹一步，原定续途之谋就此中止。";
			accent = "#8C8C8C";
		}
		else
		{
			tag = "人丹之事";
			text = CleanFact(FirstNonEmpty(item.Body, item.Title, victim + "与" + source + "之间有一桩人丹因果"));
			accent = "#B7A7FF";
		}

		return BuildEntry(item, text, BuildConcreteNote(item), tag, accent);
	}

	private static XjHistoryNarrativeEntry BuildEntry(XjCodexHistoryItem item, string text, string note, string tag, string accent)
	{
		string normalizedText = EnsureSentence(CleanFact(text));
		string normalizedNote = EnsureSentence(CleanFact(note));
		return new XjHistoryNarrativeEntry
		{
			Tag = string.IsNullOrWhiteSpace(tag) ? "纪事" : tag.Trim(),
			Text = ColorizeNarrative(item, normalizedText),
			Note = string.IsNullOrWhiteSpace(normalizedNote) ? string.Empty : ColorizeNarrative(item, normalizedNote),
			Accent = string.IsNullOrWhiteSpace(accent) ? "#CFC7B2" : accent
		};
	}

	private static string BuildConcreteNote(XjCodexHistoryItem item)
	{
		if (item == null) return string.Empty;
		List<string> parts = new List<string>(2);
		if (!string.IsNullOrWhiteSpace(item.Location)) parts.Add("事发地：" + item.Location.Trim());
		string result = (item.Result ?? string.Empty).Trim();
		if (string.Equals(result, XjHistoryResult.Failure, StringComparison.Ordinal)) parts.Add("结果：未成");
		else if (string.Equals(result, XjHistoryResult.Death, StringComparison.Ordinal)) parts.Add("结果：身死");
		else if (string.Equals(result, XjHistoryResult.Transfer, StringComparison.Ordinal)) parts.Add("结果：归属转移");
		return parts.Count == 0 ? string.Empty : string.Join("；", parts);
	}

	private static string ResolveTag(XjCodexHistoryItem item)
	{
		string type = item?.EventType ?? string.Empty;
		string text = Join(type, item?.Title, item?.Body);
		if (EqualsType(type, "Birth") || ContainsAny(text, "降生于世", "新丁降世")) return "降生";
		if (EqualsType(type, "AptitudeGranted") || ContainsAny(text, "展露天资", "灵光初显")) return "天资";
		if (EqualsType(type, "FamilyMemberConfirmed") || ContainsAny(text, "确认入籍", "族人归籍")) return "归籍";
		if (IsCaiQiFaEvent(item, text)) return "采气法";
		if (IsQiuJinFaEvent(item, text)) return "求金法";
		if (IsGongFaEvent(item, text)) return "功法";
		if (StartsWithType(type, "TechniqueRecovered")) return text.IndexOf("求金法", StringComparison.Ordinal) >= 0 ? "求金法" : "传承";
		if (EqualsType(type, "GongFaLost")) return "失传";
		if (EqualsType(type, "CaiQiCompleted")) return "采气";
		if (StartsWithType(type, "BreakthroughSuccess"))
		{
			string realm = ResolveRealmLabel(text);
			return realm.Length > 0 ? realm : "破境";
		}
		if (EqualsType(type, "BreakthroughBlocked")) return "冲关未成";
		if (IsDeathEvent(item, text)) return "身故";
		if (text.IndexOf("延寿丹", StringComparison.Ordinal) >= 0) return "延寿丹";
		if (text.IndexOf("紫府灵宝", StringComparison.Ordinal) >= 0) return "紫府灵宝";
		if (text.IndexOf("金丹法宝", StringComparison.Ordinal) >= 0) return "金丹法宝";
		if (IsSectFoundationEvent(item, text)) return "开宗";
		if (IsLectureEvent(item, text)) return ContainsAny(text, "真君") ? "真君讲道" : "开坛讲法";
		if (IsFamilySupportEvent(item, text)) return "家族扶持";
		if (IsSectRelationEvent(item, text)) return "宗门关系";
		if (IsOfficeEvent(item, text)) return "名位";
		if (ContainsAny(text, "人丹")) return "人丹";
		if (ContainsAny(text, "洞天", "秘境")) return "洞天";
		if (ContainsAny(text, "宣战", "交锋", "斗法", "血仇", "争端", "冲突")) return "争斗";
		if (ContainsAny(text, "功法", "传承", "求金法", "采气法")) return "传承";
		if (ContainsAny(text, "宗门", "山门", "峰主", "宗主", "讲道")) return "山门";
		return string.IsNullOrWhiteSpace(item?.Category) ? "纪事" : item.Category.Trim();
	}

	private static string ResolveAccent(XjCodexHistoryItem item)
	{
		string tag = ResolveTag(item);
		return tag switch
		{
			"身故" => "#FF8A80",
			"金丹" => "#FFD37A",
			"结璘" => "#FFD37A",
			"紫府" => "#B7A7FF",
			"筑基" => "#9CD7FF",
			"破境" => "#9CD7FF",
			"归籍" => "#A7E08A",
			"天资" => "#A7E08A",
			"传承" => "#C7A7FF",
			"求金法" => "#FFD37A",
			"开宗" => "#FFD37A",
			"山门" => "#FFD37A",
			"争斗" => "#FF9E80",
			"延寿丹" => "#A7E08A",
			"紫府灵宝" => "#B7A7FF",
			"金丹法宝" => "#FFD37A",
			_ => "#CFC7B2"
		};
	}

	private static string ColorizeNarrative(XjCodexHistoryItem item, string plain)
	{
		if (string.IsNullOrWhiteSpace(plain)) return string.Empty;
		string result = EscapeRichText(plain);
		List<(string Value, string Color)> tokens = new List<(string, string)>(10);
		AddToken(tokens, NarrativeActorName(item?.ActorName), ActorColor);
		AddToken(tokens, NarrativeActorName(item?.RelatedActorName), ActorColor);
		AddToken(tokens, item?.FamilyName, FamilyColor);
		AddToken(tokens, item?.RelatedFamilyName, FamilyColor);
		AddToken(tokens, item?.SectName, SectColor);
		AddToken(tokens, item?.RelatedSectName, SectColor);
		AddToken(tokens, item?.Location, PlaceColor);
		tokens.Sort((left, right) => right.Value.Length.CompareTo(left.Value.Length));
		for (int i = 0; i < tokens.Count; i++)
		{
			string value = EscapeRichText(tokens[i].Value);
			if (value.Length == 0) continue;
			result = result.Replace(value, "<color=" + tokens[i].Color + ">" + value + "</color>");
		}
		result = ColorDelimited(result, '《', '》', ItemColor);
		result = ColorDelimited(result, '【', '】', RealmColor);
		result = ColorNumbers(result);
		return result;
	}

	private static string ColorDelimited(string text, char open, char close, string color)
	{
		if (string.IsNullOrEmpty(text)) return text;
		StringBuilder builder = new StringBuilder(text.Length + 32);
		int cursor = 0;
		while (cursor < text.Length)
		{
			int start = text.IndexOf(open, cursor);
			if (start < 0)
			{
				builder.Append(text, cursor, text.Length - cursor);
				break;
			}
			int end = text.IndexOf(close, start + 1);
			if (end < 0)
			{
				builder.Append(text, cursor, text.Length - cursor);
				break;
			}
			builder.Append(text, cursor, start - cursor);
			builder.Append("<color=").Append(color).Append('>');
			builder.Append(text, start, end - start + 1);
			builder.Append("</color>");
			cursor = end + 1;
		}
		return builder.ToString();
	}

	private static string ColorNumbers(string text)
	{
		if (string.IsNullOrEmpty(text)) return text;
		StringBuilder builder = new StringBuilder(text.Length + 24);
		bool insideTag = false;
		int i = 0;
		while (i < text.Length)
		{
			char c = text[i];
			if (c == '<') insideTag = true;
			if (!insideTag && char.IsDigit(c))
			{
				int start = i;
				while (i < text.Length && char.IsDigit(text[i])) i++;
				builder.Append("<color=").Append(NumberColor).Append('>');
				builder.Append(text, start, i - start);
				builder.Append("</color>");
				continue;
			}
			builder.Append(c);
			if (c == '>') insideTag = false;
			i++;
		}
		return builder.ToString();
	}

	private static void AddToken(List<(string Value, string Color)> tokens, string value, string color)
	{
		if (tokens == null || string.IsNullOrWhiteSpace(value)) return;
		string clean = value.Trim();
		for (int i = 0; i < tokens.Count; i++) if (string.Equals(tokens[i].Value, clean, StringComparison.Ordinal)) return;
		tokens.Add((clean, color));
	}

	private static string CleanFact(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0) return string.Empty;
		text = text.Replace("「", "《").Replace("」", "》");
		text = text.Replace("。。", "。");
		return text;
	}

	private static string EnsureSentence(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (text.Length == 0) return string.Empty;
		char last = text[text.Length - 1];
		if (last != '。' && last != '！' && last != '？' && last != '；') text += "。";
		return text;
	}

	private static string NarrativeActorName(string value)
	{
		string clean = StripRealmSuffix(value);
		return string.IsNullOrWhiteSpace(clean) ? (value ?? string.Empty).Trim() : clean;
	}

	private static string StripRealmSuffix(string value)
	{
		string text = (value ?? string.Empty).Trim();
		int index = text.LastIndexOf('-');
		if (index <= 0 || index >= text.Length - 1) return text;
		string suffix = text.Substring(index + 1).Trim();
		if (!ContainsAny(suffix, "胎息", "炼气", "筑基", "紫府", "黄冠", "金丹", "神丹", "结璘", "真人", "真君", "玄君", "龙王")) return text;
		return text.Substring(0, index).Trim();
	}


	private static string EscapeRichText(string value)
	{
		return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
	}

	private static string ResolveRealmLabel(string text)
	{
		if (ContainsAny(text, "结璘", "结璘仙")) return "结璘";
		if (ContainsAny(text, "真君羽士")) return "真君羽士";
		if (ContainsAny(text, "金丹")) return "金丹";
		if (ContainsAny(text, "服气真人", "晋为真人", "真人境")) return "真人";
		if (ContainsAny(text, "黄冠")) return "黄冠";
		if (ContainsAny(text, "紫府")) return "紫府";
		if (ContainsAny(text, "筑基", "仙基")) return "筑基";
		if (ContainsAny(text, "炼气")) return "炼气";
		if (ContainsAny(text, "胎息")) return "胎息";
		return string.Empty;
	}

	private static bool IsDeathEvent(XjCodexHistoryItem item, string text)
	{
		string haystack = Join(item?.EventType, text);
		return ContainsAny(haystack, "Death", "ActorDied", "陨落", "寿终", "坐化", "战死", "身死", "身陨", "归寂", "身殒道消");
	}

	private static bool IsLectureEvent(XjCodexHistoryItem item, string text)
	{
		return StartsWithType(item?.EventType, "SectLecture:") || ContainsAny(text, "开坛讲法", "真人开坛", "真君讲道", "讲授真君大道");
	}

	private static bool IsFamilySupportEvent(XjCodexHistoryItem item, string text)
	{
		return StartsWithType(item?.EventType, "FamilySupport")
			|| StartsWithType(item?.EventType, "FamilySupported")
			|| StartsWithType(item?.EventType, "FamilyHighRealm")
			|| ContainsAny(text, "族议扶持", "族议举后", "上修扶持", "上修定议");
	}

	private static bool IsSectRelationEvent(XjCodexHistoryItem item, string text)
	{
		return StartsWithType(item?.EventType, "SectRelationChanged:")
			|| EqualsType(item?.EventType, "SectHostilityConflict")
			|| ContainsAny(text, "宗门敌意", "宗门关系", "嫌隙已成", "敌意深重", "开战临界");
	}

	private static bool IsSectGovernanceEvent(XjCodexHistoryItem item, string text)
	{
		string type = (item?.EventType ?? string.Empty).Trim();
		return EqualsType(type, "SectCommonDuty")
			|| StartsWithType(type, "SectMandate:")
			|| EqualsType(type, "SectTreasuryContribution")
			|| EqualsType(type, "SectDominantFamilyChanged")
			|| EqualsType(type, "SectFamilyPrivilegeIncident")
			|| EqualsType(type, "SectFamilySupplyPenalty")
			|| EqualsType(type, "SectCityGovernanceTransferred")
			|| ContainsAny(text, "宗门共务", "宗主法旨", "掌宗易姓", "供养追责", "宗门重宝入库", "城镇治理权由");
	}

	private static bool IsCaiQiFaEvent(XjCodexHistoryItem item, string text)
	{
		return EqualsType(item?.EventType, "CaiQiFaStored") || ContainsAny(text, "采气法入宗", "采气法库");
	}

	private static bool IsQiuJinFaEvent(XjCodexHistoryItem item, string text)
	{
		return EqualsType(item?.EventType, "QiuJinFaStored") || ContainsAny(text, "求金法入阁", "求金法库");
	}

	private static bool IsGongFaEvent(XjCodexHistoryItem item, string text)
	{
		return EqualsType(item?.EventType, "GongFaStored")
			|| ContainsAny(text, "功法入阁", "功法图录", "功法阁") && !ContainsAny(text, "采气法", "求金法");
	}

	private static string ExtractPeakName(string text)
	{
		if (string.IsNullOrWhiteSpace(text)) return "一峰";
		string[] markers = { "新开", "许立", "受命执掌", "执掌" };
		for (int i = 0; i < markers.Length; i++)
		{
			int index = text.IndexOf(markers[i], StringComparison.Ordinal);
			if (index < 0) continue;
			string value = text.Substring(index + markers[i].Length).TrimStart();
			int stop = value.IndexOfAny(new[] { '，', '。', '；', ' ', '\n', '\r' });
			if (stop >= 0) value = value.Substring(0, stop);
			value = value.Trim('【', '】', '《', '》', '，', '。', ' ');
			if (value.EndsWith("峰主", StringComparison.Ordinal)) value = value.Substring(0, value.Length - 2).Trim();
			if (!string.IsNullOrWhiteSpace(value)) return value;
		}
		return "一峰";
	}

	private static string ExtractLeadingTechniqueName(string text, string kind)
	{
		string book = ExtractFirstBookName(text);
		if (!string.IsNullOrWhiteSpace(book)) return book;
		string source = (text ?? string.Empty).Trim();
		string[] markers = { "归入", "收入", "入阁", "入宗", "入" };
		for (int i = 0; i < markers.Length; i++)
		{
			int index = source.IndexOf(markers[i], StringComparison.Ordinal);
			if (index > 0)
			{
				string value = source.Substring(0, index).Trim('【', '】', '《', '》', ' ', '，', '。');
				int colon = value.LastIndexOf('：');
				if (colon >= 0 && colon < value.Length - 1) value = value.Substring(colon + 1).Trim();
				if (!string.IsNullOrWhiteSpace(value) && !ContainsAny(value, "采气法入宗", "功法入阁", "求金法入阁")) return value;
			}
		}
		return string.IsNullOrWhiteSpace(kind) ? "未名法门" : "未名" + kind;
	}

	private static string ExtractValueAfter(string text, string marker)
	{
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(marker)) return string.Empty;
		int index = text.IndexOf(marker, StringComparison.Ordinal);
		if (index < 0) return string.Empty;
		string value = text.Substring(index + marker.Length).Trim();
		int stop = value.IndexOfAny(new[] { '，', '；', '\n', '\r' });
		if (stop >= 0) value = value.Substring(0, stop);
		return value.Trim();
	}

	private static bool IsSectFoundationEvent(XjCodexHistoryItem item, string text)
	{
		return EqualsType(item?.EventType, "ZongMenFounded") || ContainsAny(text, "开宗立派", "创立【", "开山立派");
	}

	private static bool IsOfficeEvent(XjCodexHistoryItem item, string text)
	{
		return EqualsType(item?.EventType, "Office")
			|| StartsWithType(item?.EventType, "SectSovereign")
			|| StartsWithType(item?.EventType, "SectPeak")
			|| ContainsAny(text, "继任宗主", "成为宗主", "峰主", "掌门", "接掌宗门");
	}

	private static bool IsRetainedCraftEvent(string text)
	{
		return ContainsAny(text, "延寿丹", "紫府灵宝", "金丹法宝", "七品", "上品灵宝", "上品法宝");
	}

	private static bool IsRoutineCraftEvent(XjCodexHistoryItem item, string text)
	{
		string category = item?.Category ?? string.Empty;
		bool craftCategory = string.Equals(category, XjWorldHistoryCategory.Craft, StringComparison.Ordinal)
			|| ContainsAny(text, "炼丹", "制符", "炼器", "阵法", "护身符", "神行符", "破阵符", "阵盘", "阵旗");
		return craftCategory && !IsRetainedCraftEvent(text);
	}

	private static bool IsHighRealmText(string text)
	{
		return ContainsAny(text, "紫府", "金丹", "神丹", "结璘", "真人", "真君", "玄君");
	}

	private static bool IsRenDanEvent(string eventType)
	{
		return !string.IsNullOrWhiteSpace(eventType) && eventType.Trim().StartsWith("RenDan", StringComparison.Ordinal);
	}

	private static bool IsRenDanSourceView(XjCodexHistoryItem item, XjHistoryVisibility visibility, long subjectId)
	{
		if (subjectId <= 0L) return false;
		if (visibility == XjHistoryVisibility.Personal) return item.RelatedActorId == subjectId;
		if (visibility == XjHistoryVisibility.Family) return item.RelatedFamilyId == subjectId;
		if (visibility == XjHistoryVisibility.Sect) return item.RelatedSectId == subjectId;
		return false;
	}

	private static bool EqualsType(string value, string expected)
	{
		return string.Equals((value ?? string.Empty).Trim(), expected, StringComparison.Ordinal);
	}

	private static bool StartsWithType(string value, string prefix)
	{
		return !string.IsNullOrWhiteSpace(value) && value.Trim().StartsWith(prefix, StringComparison.Ordinal);
	}

	private static string FirstNonEmpty(params string[] values)
	{
		if (values == null) return string.Empty;
		for (int i = 0; i < values.Length; i++) if (!string.IsNullOrWhiteSpace(values[i])) return values[i].Trim();
		return string.Empty;
	}

	private static string Join(params string[] values)
	{
		return values == null ? string.Empty : string.Join(" ", values);
	}

	private static bool ContainsAny(string text, params string[] values)
	{
		if (string.IsNullOrWhiteSpace(text) || values == null) return false;
		for (int i = 0; i < values.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(values[i]) && text.IndexOf(values[i], StringComparison.OrdinalIgnoreCase) >= 0) return true;
		}
		return false;
	}
}
