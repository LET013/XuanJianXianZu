using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 古释宏愿是个人修证约束，不是今释度化指标。法师起只立一愿，年度低频践愿；
/// 清静点化会推进愿行。古释法相必须先有宏愿并有一定履愿积累。
/// </summary>
internal static class XjAncientShiVowSystem
{
	internal const int FulfilledProgress = 100;
	internal const int DharmaFormMinimumProgress = 60;
	private const int BaseAnnualProgress = 2;
	private const int QuietBlessingProgress = 8;

	internal static void TickActor(Actor actor, in XjShiSnapshot snapshot, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return;

		EnsureDeclared(actor, snapshot, annualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowLastProgressYear, out int lastProgressYear);
		if (lastProgressYear == annualYear) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowLastProgressYear, annualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowProgress, out int progress);
		if (progress >= FulfilledProgress) return;

		int realmBonus = XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm) ? 2 : 0;
		int templeBonus = XjAncientShiTempleSystem.TryGetTempleForActor(actor, out _) ? 1 : 0;
		int next = Math.Clamp(progress + BaseAnnualProgress + realmBonus + templeBonus, 0, FulfilledProgress);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowProgress, next);
		if (progress < FulfilledProgress && next >= FulfilledProgress)
		{
			XjWorldHistoryStore.RecordActorEvent(actor,
				"所发宏愿经年践履，愿行渐合本性，已成一段可持之愿。",
				XjShiCatalog.GetRealmTraitId(snapshot.Realm));
		}
	}

	internal static void OnQuietBlessing(Actor actor, int annualYear)
	{
		if (actor?.data == null || !actor.isAlive() || annualYear <= 0
			|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
			|| !string.Equals(snapshot.Tradition, XjShiTraditionIds.Ancient, StringComparison.Ordinal)
			|| XjShiCatalog.GetRank(snapshot.Realm) < XjShiCatalog.GetRank(XjShiRealmIds.DharmaMaster)) return;
		EnsureDeclared(actor, snapshot, annualYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowProgress, out int progress);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowProgress,
			Math.Clamp(progress + QuietBlessingProgress, 0, FulfilledProgress));
	}

	internal static bool HasDeclared(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiVowId, out string vowId)
			&& XjAncientShiVowIds.IsKnown(vowId);
	}

	internal static bool HasDharmaFormReadiness(Actor actor)
	{
		if (!HasDeclared(actor)) return false;
		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowProgress, out int progress)
			&& progress >= DharmaFormMinimumProgress;
	}

	internal static int GetProgress(Actor actor)
	{
		return actor?.data != null && XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowProgress, out int progress)
			? Math.Clamp(progress, 0, FulfilledProgress)
			: 0;
	}

	internal static string GetDisplay(Actor actor)
	{
		if (actor?.data == null || !XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiVowId, out string vowId))
			return "本愿未载";
		return XjAncientShiVowCatalog.GetDisplay(vowId);
	}

	private static void EnsureDeclared(Actor actor, in XjShiSnapshot snapshot, int annualYear)
	{
		if (actor?.data == null) return;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiVowId, out string current);
		if (XjAncientShiVowIds.IsKnown(current))
		{
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiVowDeclaredYear, out int declaredYear);
			if (declaredYear <= 0) XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowDeclaredYear, annualYear);
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int index = XjDeterministicHash.PositiveIndex(actorId + Math.Max(1, snapshot.RealmEnteredYear) * 17L,
			"ancient_shi_macro_vow_v1", XjAncientShiVowIds.All.Length);
		string vowId = XjAncientShiVowIds.All[index];
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiVowId, vowId);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowDeclaredYear, annualYear);
		int initial = XjShiCatalog.GetRank(snapshot.Realm) >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
			? FulfilledProgress : 0;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowProgress, initial);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.ShiVowLastProgressYear, 0);
		XjWorldHistoryStore.RecordActorEvent(actor,
			"自守本性而发“" + XjAncientShiVowCatalog.GetShortDisplay(vowId) + "”，自此以此愿约束修证。",
			XjShiCatalog.GetRealmTraitId(snapshot.Realm));
	}
}
