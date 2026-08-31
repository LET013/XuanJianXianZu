using System.Collections.Generic;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.History.Books;

namespace XuanJianVNext.Systems.Sect;

internal static class XjSectKnowledgeWriter
{
	private const int MaxHandledKeys = 4096;
	private static readonly HashSet<string> handledKeys = new HashSet<string>();
	private static readonly Queue<string> handledKeyOrder = new Queue<string>();

	internal static void Handle(in XjFamilyDomainEvent domainEvent)
	{
		if (!domainEvent.Found || domainEvent.ActorId <= 0L || domainEvent.ZongMenId <= 0L)
		{
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiCompleted)
		{
			HandleCaiQi(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeCaiQiFaObtained)
		{
			HandleCaiQiFa(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaObtained
			|| domainEvent.EventType == XjFamilyDomainEvent.TypeGongFaPromoted)
		{
			HandleGongFa(domainEvent);
			return;
		}

		if (domainEvent.EventType == XjFamilyDomainEvent.TypeQiuJinFaComprehended)
		{
			HandleQiuJinFa(domainEvent);
		}
	}

	internal static void Clear()
	{
		handledKeys.Clear();
		handledKeyOrder.Clear();
	}

	private static void HandleCaiQi(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.CaiQiResourceId)
			|| domainEvent.CaiQiAmount <= 0
			|| !TryMarkHandled(domainEvent, domainEvent.CaiQiResourceId, 0))
		{
			return;
		}

		if (!XjSectCaiQiWarehouse.TryAddCaiQiResource(
			domainEvent.ZongMenId,
			domainEvent.ZongMenName,
			domainEvent.CaiQiResourceId,
			domainEvent.CaiQiAmount,
			domainEvent.ActorId,
			domainEvent.ActorName,
			domainEvent.Source,
			domainEvent.Year))
		{
			return;
		}
	}

	private static void HandleCaiQiFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.CaiQiFaName)
			|| string.IsNullOrWhiteSpace(domainEvent.DaoTu)
			|| !TryMarkHandled(domainEvent, domainEvent.CaiQiFaName + "|" + domainEvent.DaoTu, 0, false))
		{
			return;
		}

		if (!XjSectCaiQiWarehouse.TryAddCaiQiFa(
			domainEvent.ZongMenId,
			domainEvent.ZongMenName,
			domainEvent.CaiQiFaName,
			domainEvent.DaoTu,
			domainEvent.CaiQiFaSourcePlace,
			domainEvent.ActorId,
			domainEvent.ActorName,
			domainEvent.Year))
		{
			return;
		}
		// 保留宗门采气法库与底层历史事实，但不创建宗门纪事。
		RecordResourceHistory(domainEvent, "采气法入宗", domainEvent.CaiQiFaName + "归入" + domainEvent.ZongMenName + "采气法库，道途：" + Empty(domainEvent.DaoTu, "未明") + "。", 3, "CaiQiFaStored");
	}

	private static void HandleGongFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.GongFaName)
			|| domainEvent.GongFaGrade < 5
			|| domainEvent.GongFaGrade > XjDaoTaiGongFaService.DaoTaiGrade
			|| !TryMarkHandled(domainEvent, domainEvent.GongFaName + "|" + domainEvent.MappedXianJi, domainEvent.GongFaGrade, false))
		{
			return;
		}

		if (!XjSectGongFaPavilion.TryAddGongFa(
			domainEvent.ZongMenId,
			domainEvent.ZongMenName,
			domainEvent.ActorId,
			domainEvent.ActorName,
			domainEvent.GongFaName,
			domainEvent.GongFaGrade,
			domainEvent.DaoTu,
			domainEvent.Source,
			domainEvent.MappedXianJi,
			domainEvent.Year))
		{
			return;
		}
		if (domainEvent.GongFaGrade >= 6)
		{
			RecordResourceHistory(domainEvent, "功法入阁", domainEvent.GongFaName + "入" + domainEvent.ZongMenName + "功法图录，品阶：" + XjGongFaGradeText.Format(domainEvent.GongFaGrade) + "。", 4, "GongFaStored");
			XjThreeBookWriter.RecordSectInheritance(
				domainEvent.ZongMenId,
				domainEvent.ZongMenName,
				domainEvent.ActorId,
				domainEvent.ActorName,
				domainEvent.GongFaName,
				XjGongFaGradeText.Format(domainEvent.GongFaGrade) + "功法",
				domainEvent.Year);
		}
	}

	private static void HandleQiuJinFa(in XjFamilyDomainEvent domainEvent)
	{
		if (string.IsNullOrWhiteSpace(domainEvent.QiuJinFaName)
			|| !TryMarkHandled(domainEvent, domainEvent.QiuJinFaName, domainEvent.GongFaGrade, false))
		{
			return;
		}

		if (!XjSectGongFaPavilion.TryAddQiuJinFa(
			domainEvent.ZongMenId,
			domainEvent.ZongMenName,
			domainEvent.ActorId,
			domainEvent.ActorName,
			domainEvent.QiuJinFaName,
			domainEvent.GongFaGrade,
			domainEvent.DaoTu,
			domainEvent.Source,
			domainEvent.BoundAuthority,
			domainEvent.Year))
		{
			return;
		}
		RecordResourceHistory(domainEvent, "求金法入阁", domainEvent.QiuJinFaName + "入" + domainEvent.ZongMenName + "求金法库，权柄：" + Empty(domainEvent.BoundAuthority, "未明") + "。", 4, "QiuJinFaStored");
		XjThreeBookWriter.RecordSectInheritance(
			domainEvent.ZongMenId,
			domainEvent.ZongMenName,
			domainEvent.ActorId,
			domainEvent.ActorName,
			domainEvent.QiuJinFaName,
			"求金法",
			domainEvent.Year);
	}

	private static void RecordResourceHistory(in XjFamilyDomainEvent domainEvent, string title, string body, int importance, string eventType, string iconId = null)
	{
		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.Inheritance,
			title,
			body,
			importance,
			actorId: domainEvent.ActorId,
			actorName: domainEvent.ActorName,
			sectId: domainEvent.ZongMenId,
			familyId: domainEvent.FamilyStableId,
			year: domainEvent.Year,
			iconIdOverride: iconId,
			eventType: eventType,
			visibilityFlags: (int)(XjHistoryVisibility.Personal | XjHistoryVisibility.Family | XjHistoryVisibility.Sect | XjHistoryVisibility.CenturyCandidate));
	}


	private static string Empty(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();

	private static string ResolveCaiQiDisplayName(string resourceId)
	{
		if (XjCaiQiCatalog.TryGetDisplayNameByResourceId(resourceId, out string displayName)
			&& !string.IsNullOrWhiteSpace(displayName))
		{
			return displayName.Trim();
		}
		return string.Equals(resourceId, "zaqi", System.StringComparison.Ordinal) ? "杂气" : "未名先天之气";
	}

	private static bool TryMarkHandled(in XjFamilyDomainEvent domainEvent, string name, int grade, bool includeYear = true)
	{
		string key = domainEvent.ZongMenId
			+ "|"
			+ domainEvent.ActorId
			+ "|"
			+ domainEvent.EventType
			+ "|"
			+ (name ?? string.Empty).Trim()
			+ "|"
			+ grade
			+ (includeYear ? "|" + domainEvent.Year : string.Empty);
		if (!handledKeys.Add(key))
		{
			return false;
		}

		handledKeyOrder.Enqueue(key);
		while (handledKeyOrder.Count > MaxHandledKeys)
		{
			string expired = handledKeyOrder.Dequeue();
			handledKeys.Remove(expired);
		}
		return true;
	}
}
