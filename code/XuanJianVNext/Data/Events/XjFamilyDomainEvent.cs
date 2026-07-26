using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Data.Events;

internal readonly struct XjFamilyDomainEvent
{
	internal const string TypeFamilyMemberConfirmed = "FamilyMemberConfirmed";
	internal const string TypeBirth = "Birth";
	internal const string TypeAptitudeGranted = "AptitudeGranted";
	internal const string TypeRealmBreakthrough = "RealmBreakthrough";
	internal const string TypeCaiQiCompleted = "CaiQiCompleted";
	internal const string TypeCaiQiFaObtained = "CaiQiFaObtained";
	internal const string TypeFaBaoObtained = "FaBaoObtained";
	internal const string TypeGongFaObtained = "GongFaObtained";
	internal const string TypeGongFaPromoted = "GongFaPromoted";
	internal const string TypeQiuJinFaComprehended = "QiuJinFaComprehended";
	internal const string TypeJinDanSucceeded = "JinDanSucceeded";
	internal const string TypeShenDanSucceeded = "ShenDanSucceeded";
	internal const string TypeJinDanGift = "JinDanGift";
	internal const string TypeDongTianOpened = "DongTianOpened";
	internal const string TypeDongTianSurvived = "DongTianSurvived";
	internal const string TypeDongTianDeath = "DongTianDeath";
	internal const string TypeDongTianClosed = "DongTianClosed";
	internal const string TypeFaBaoUpgraded = "FaBaoUpgraded";
	internal const string TypeJinXingObtained = "JinXingObtained";

	internal readonly bool Found;
	internal readonly string EventType;
	internal readonly long ActorId;
	internal readonly string ActorName;
	internal readonly long FamilyStableId;
	internal readonly string FamilyKey;
	internal readonly long ZongMenId;
	internal readonly string ZongMenName;
	internal readonly int Year;
	internal readonly string Source;
	internal readonly string RealmId;
	internal readonly string GongFaName;
	internal readonly int GongFaGrade;
	internal readonly string QiuJinFaName;
	internal readonly string CaiQiResourceId;
	internal readonly int CaiQiAmount;
	internal readonly string CaiQiFaName;
	internal readonly string CaiQiFaSourcePlace;
	internal readonly string FaBaoId;
	internal readonly string FaBaoName;
	internal readonly string FaBaoClass;
	internal readonly string DaoTu;
	internal readonly string GuoWei;
	internal readonly string MappedXianJi;
	internal readonly string BoundAuthority;

	internal XjFamilyDomainEvent(
		bool found,
		string eventType,
		long actorId,
		string actorName,
		long familyStableId,
		string familyKey,
		long zongMenId,
		string zongMenName,
		int year,
		string source,
		string realmId,
		string gongFaName,
		int gongFaGrade,
		string qiuJinFaName,
		string caiQiResourceId,
		int caiQiAmount,
		string caiQiFaName,
		string caiQiFaSourcePlace,
		string faBaoId,
		string faBaoName,
		string faBaoClass,
		string daoTu,
		string guoWei,
		string mappedXianJi,
		string boundAuthority)
	{
		Found = found;
		EventType = eventType ?? string.Empty;
		ActorId = actorId < 0L ? 0L : actorId;
		ActorName = actorName ?? string.Empty;
		FamilyStableId = familyStableId < 0L ? 0L : familyStableId;
		FamilyKey = familyKey ?? string.Empty;
		ZongMenId = zongMenId < 0L ? 0L : zongMenId;
		ZongMenName = zongMenName ?? string.Empty;
		Year = year < 0 ? 0 : year;
		Source = source ?? string.Empty;
		RealmId = realmId ?? string.Empty;
		GongFaName = gongFaName ?? string.Empty;
		GongFaGrade = gongFaGrade < 0 ? 0 : gongFaGrade;
		QiuJinFaName = qiuJinFaName ?? string.Empty;
		CaiQiResourceId = caiQiResourceId ?? string.Empty;
		CaiQiAmount = caiQiAmount < 0 ? 0 : caiQiAmount;
		CaiQiFaName = caiQiFaName ?? string.Empty;
		CaiQiFaSourcePlace = caiQiFaSourcePlace ?? string.Empty;
		FaBaoId = faBaoId ?? string.Empty;
		FaBaoName = faBaoName ?? string.Empty;
		FaBaoClass = faBaoClass ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		GuoWei = guoWei ?? string.Empty;
		MappedXianJi = mappedXianJi ?? string.Empty;
		BoundAuthority = boundAuthority ?? string.Empty;
	}

	internal static XjFamilyDomainEvent FamilyMemberConfirmed(Actor actor)
	{
		return Create(actor, TypeFamilyMemberConfirmed, string.Empty, string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty);
	}

	internal static XjFamilyDomainEvent Birth(Actor actor)
	{
		return Create(actor, TypeBirth, "Birth", string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty);
	}

	internal static XjFamilyDomainEvent AptitudeGranted(Actor actor)
	{
		return Create(actor, TypeAptitudeGranted, "Aptitude", string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty);
	}

	internal static XjFamilyDomainEvent RealmBreakthrough(Actor actor, string realmId, string daoTu)
	{
		return Create(actor, TypeRealmBreakthrough, "Realm", realmId, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, string.Empty);
	}

	internal static XjFamilyDomainEvent CaiQiCompleted(Actor actor, string resourceId, int amount)
	{
		return Create(actor, TypeCaiQiCompleted, "CaiQi", string.Empty, string.Empty, 0, string.Empty, resourceId, amount, string.Empty, string.Empty, string.Empty, string.Empty);
	}

	internal static XjFamilyDomainEvent CaiQiFaObtained(Actor actor, string caiQiFaName, string daoTu, string sourcePlace)
	{
		return Create(actor, TypeCaiQiFaObtained, sourcePlace, string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, caiQiFaName, sourcePlace, daoTu, string.Empty);
	}

	internal static XjFamilyDomainEvent GongFaObtained(
		Actor actor,
		string gongFaName,
		int gongFaGrade,
		string daoTu,
		string source,
		string mappedXianJi = "",
		string boundAuthority = "")
	{
		return Create(actor, TypeGongFaObtained, source, string.Empty, gongFaName, gongFaGrade, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, string.Empty, mappedXianJi, boundAuthority);
	}

	internal static XjFamilyDomainEvent FaBaoObtained(
		Actor actor,
		string faBaoId,
		string faBaoName,
		string daoTu,
		string source,
		string faBaoClass)
	{
		return Create(actor, TypeFaBaoObtained, source, string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, faBaoId, faBaoName, faBaoClass, daoTu, string.Empty);
	}

	internal static XjFamilyDomainEvent GongFaPromoted(
		Actor actor,
		string gongFaName,
		int gongFaGrade,
		string daoTu,
		string source,
		string mappedXianJi = "",
		string boundAuthority = "")
	{
		return Create(actor, TypeGongFaPromoted, source, string.Empty, gongFaName, gongFaGrade, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, string.Empty, mappedXianJi, boundAuthority);
	}

	internal static XjFamilyDomainEvent QiuJinFaComprehended(
		Actor actor,
		string qiuJinFaName,
		string sourceGongFaName,
		int sourceGongFaGrade,
		string daoTu,
		string mappedXianJi = "",
		string boundAuthority = "")
	{
		return Create(actor, TypeQiuJinFaComprehended, sourceGongFaName, string.Empty, sourceGongFaName, sourceGongFaGrade, qiuJinFaName, string.Empty, 0, string.Empty, string.Empty, daoTu, string.Empty, mappedXianJi, boundAuthority);
	}

	internal static XjFamilyDomainEvent JinDanSucceeded(Actor actor, string guoWei, string daoTu)
	{
		return Create(actor, TypeJinDanSucceeded, "JinDan", "JinDan", string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, guoWei);
	}

	internal static XjFamilyDomainEvent ShenDanSucceeded(Actor actor, string guoWei, string daoTu, string anchorName)
	{
		return Create(actor, TypeShenDanSucceeded, anchorName, XjRealmIds.ShenDan, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, guoWei);
	}

	internal static XjFamilyDomainEvent JinDanGift(Actor actor, string resourceId, string daoTu)
	{
		return Create(actor, TypeJinDanGift, "JinDanGift", "JinDan", string.Empty, 0, string.Empty, resourceId, 1, string.Empty, string.Empty, daoTu, string.Empty);
	}

	internal static XjFamilyDomainEvent DongTianOpened(Actor actor, string dongTianName)
	{
		return Create(actor, TypeDongTianOpened, dongTianName, string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty, string.Empty);
	}

	internal static XjFamilyDomainEvent DongTianSurvived(Actor actor, string dongTianName, string rewardType, string rewardSummary)
	{
		return Create(
			actor,
			TypeDongTianSurvived,
			dongTianName,
			string.Empty,
			string.Empty,
			0,
			string.Empty,
			string.Empty,
			0,
			rewardType,
			rewardSummary,
			string.Empty,
			string.Empty);
	}

	internal static XjFamilyDomainEvent DongTianDeath(long actorId, string actorName, int year, string dongTianName)
	{
		return new XjFamilyDomainEvent(
			true,
			TypeDongTianDeath,
			actorId,
			actorName,
			0L,
			string.Empty,
			0L,
			string.Empty,
			year,
			dongTianName,
			string.Empty,
			string.Empty,
			0,
			string.Empty,
			string.Empty,
			0,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty);
	}

	internal static XjFamilyDomainEvent DongTianClosed(long actorId, string actorName, int year, string dongTianName)
	{
		return new XjFamilyDomainEvent(
			true,
			TypeDongTianClosed,
			actorId,
			actorName,
			0L,
			string.Empty,
			0L,
			string.Empty,
			year,
			dongTianName,
			string.Empty,
			string.Empty,
			0,
			string.Empty,
			string.Empty,
			0,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty,
			string.Empty);
	}

	internal static XjFamilyDomainEvent FaBaoUpgraded(Actor actor, string newClassName, string faBaoName)
	{
		return Create(actor, TypeFaBaoUpgraded, "FaBaoUpgrade", string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, string.Empty, faBaoName, string.Empty, string.Empty, newClassName);
	}

	internal static XjFamilyDomainEvent JinXingObtained(Actor actor, string jinXingName, string daoTu)
	{
		return Create(actor, TypeJinXingObtained, "JinXing", string.Empty, string.Empty, 0, string.Empty, string.Empty, 0, string.Empty, string.Empty, daoTu, jinXingName);
	}

	internal XjFamilyDomainEvent WithFamily(long familyStableId, string familyKey)
	{
		return new XjFamilyDomainEvent(
			Found,
			EventType,
			ActorId,
			ActorName,
			familyStableId,
			familyKey,
			ZongMenId,
			ZongMenName,
			Year,
			Source,
			RealmId,
			GongFaName,
			GongFaGrade,
			QiuJinFaName,
			CaiQiResourceId,
			CaiQiAmount,
			CaiQiFaName,
			CaiQiFaSourcePlace,
			FaBaoId,
			FaBaoName,
			FaBaoClass,
			DaoTu,
			GuoWei,
			MappedXianJi,
			BoundAuthority);
	}

	private static XjFamilyDomainEvent Create(
		Actor actor,
		string eventType,
		string source,
		string realmId,
		string gongFaName,
		int gongFaGrade,
		string qiuJinFaName,
		string caiQiResourceId,
		int caiQiAmount,
		string caiQiFaName,
		string caiQiFaSourcePlace,
		string daoTu,
		string guoWei)
	{
		return Create(actor, eventType, source, realmId, gongFaName, gongFaGrade, qiuJinFaName, caiQiResourceId, caiQiAmount, caiQiFaName, caiQiFaSourcePlace, daoTu, guoWei, string.Empty, string.Empty);
	}

	private static XjFamilyDomainEvent Create(
		Actor actor,
		string eventType,
		string source,
		string realmId,
		string gongFaName,
		int gongFaGrade,
		string qiuJinFaName,
		string caiQiResourceId,
		int caiQiAmount,
		string caiQiFaName,
		string caiQiFaSourcePlace,
		string daoTu,
		string guoWei,
		string mappedXianJi,
		string boundAuthority)
	{
		return Create(
			actor,
			eventType,
			source,
			realmId,
			gongFaName,
			gongFaGrade,
			qiuJinFaName,
			caiQiResourceId,
			caiQiAmount,
			caiQiFaName,
			caiQiFaSourcePlace,
			string.Empty,
			string.Empty,
			string.Empty,
			daoTu,
			guoWei,
			mappedXianJi,
			boundAuthority);
	}

	private static XjFamilyDomainEvent Create(
		Actor actor,
		string eventType,
		string source,
		string realmId,
		string gongFaName,
		int gongFaGrade,
		string qiuJinFaName,
		string caiQiResourceId,
		int caiQiAmount,
		string caiQiFaName,
		string caiQiFaSourcePlace,
		string faBaoId,
		string faBaoName,
		string faBaoClass,
		string daoTu,
		string guoWei)
	{
		return Create(actor, eventType, source, realmId, gongFaName, gongFaGrade, qiuJinFaName, caiQiResourceId, caiQiAmount, caiQiFaName, caiQiFaSourcePlace, faBaoId, faBaoName, faBaoClass, daoTu, guoWei, string.Empty, string.Empty);
	}

	private static XjFamilyDomainEvent Create(
		Actor actor,
		string eventType,
		string source,
		string realmId,
		string gongFaName,
		int gongFaGrade,
		string qiuJinFaName,
		string caiQiResourceId,
		int caiQiAmount,
		string caiQiFaName,
		string caiQiFaSourcePlace,
		string faBaoId,
		string faBaoName,
		string faBaoClass,
		string daoTu,
		string guoWei,
		string mappedXianJi,
		string boundAuthority)
	{
		if (actor?.data == null)
		{
			return default;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return default;
		}

		XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjZongMenId, out long zongMenId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenName, out string zongMenName);
		if (zongMenId < 0L)
		{
			zongMenId = 0L;
		}

		if (string.IsNullOrWhiteSpace(zongMenName))
		{
			zongMenName = string.Empty;
		}

		return new XjFamilyDomainEvent(
			true,
			eventType,
			actorId,
			actor.getName(),
			0L,
			string.Empty,
			zongMenId,
			zongMenName,
			GetCurrentYear(actor),
			source,
			realmId,
			gongFaName,
			gongFaGrade,
			qiuJinFaName,
			caiQiResourceId,
			caiQiAmount,
			caiQiFaName,
			caiQiFaSourcePlace,
			faBaoId,
			faBaoName,
			faBaoClass,
			daoTu,
			guoWei,
			mappedXianJi,
			boundAuthority);
	}

	private static int GetCurrentYear(Actor actor)
	{
		int trackedYear = XjYearTracker.CurrentYear;
		if (trackedYear > 0)
		{
			return trackedYear;
		}

		int worldYear = World.world?.map_stats?.year ?? 0;
		if (worldYear > 0)
		{
			return worldYear;
		}

		return actor == null ? 0 : (int)System.Math.Floor(System.Math.Max(0f, actor.getAge()));
	}
}
