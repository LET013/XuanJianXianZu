using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.History.Books;

using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Craft;

internal static class XjCraftTraitAcquisitionSystem
{
	private const float MaximumAcquireChancePercent = 50f;
	private const float HuiGuangCurvePeak = XjDaoHuiPolicy.Maximum;
	private const double HuiGuangCurveExponent = 1.65d;
	private const int RollPrecision = 10000;

	internal static bool TryGrantOnRealmBreakthrough(Actor actor, string realmId, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0 || XjCraftTraitRules.HasAnyCraftTrait(actor)
			|| !TryResolveRealmBaseChance(realmId, out float realmBaseChance)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return false;

		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		huiGuang = XjFaBaoBonusService.GetEffectiveHuiGuang(actor, Math.Max(0f, huiGuang));
		float chancePercent = ResolveAcquireChancePercent(realmBaseChance, huiGuang);
		int threshold = (int)Math.Round(chancePercent * 100f);
		bool daoZhu = IsDaoZhu(actor);
		if (!daoZhu && XjDeterministicHash.PositiveIndex(actorId + currentYear, "craft_awaken|" + realmId, RollPrecision) >= threshold) return false;

		string traitId = SelectCraftPath(actorId, realmId, currentYear);
		if (string.IsNullOrWhiteSpace(traitId) || !XjCraftTraitRules.TryGrantExclusive(actor, traitId)) return false;
		XjThreeBookWriter.RecordCraftAbility(actor, ResolveCraftDisplayName(traitId), currentYear);
		return true;
	}

	internal static float ResolveAcquireChancePercent(float realmBaseChance, float huiGuang)
	{
		float baseChance = Math.Max(0f, Math.Min(MaximumAcquireChancePercent, realmBaseChance));
		float normalized = Math.Max(0f, Math.Min(1f, huiGuang / HuiGuangCurvePeak));
		float curve = (float)Math.Pow(normalized, HuiGuangCurveExponent);
		return Math.Max(0f, Math.Min(MaximumAcquireChancePercent, baseChance + (MaximumAcquireChancePercent - baseChance) * curve));
	}

	private static string SelectCraftPath(long actorId, string realmId, int currentYear)
	{
		int totalWeight = 0;
		for (int i = 0; i < XjCraftProfessionCatalog.All.Count; i++) totalWeight += XjCraftProfessionCatalog.All[i].SelectionWeight;
		if (totalWeight <= 0) return string.Empty;

		int roll = XjDeterministicHash.PositiveIndex(actorId, "craft_path|" + realmId + "|" + currentYear, totalWeight);
		int cursor = 0;
		for (int i = 0; i < XjCraftProfessionCatalog.All.Count; i++)
		{
			cursor += XjCraftProfessionCatalog.All[i].SelectionWeight;
			if (roll < cursor) return XjCraftProfessionCatalog.All[i].TraitId;
		}
		return XjCraftProfessionCatalog.All[XjCraftProfessionCatalog.All.Count - 1].TraitId;
	}

	private static bool IsDaoZhu(Actor actor)
	{
		return actor?.data != null && actor.hasTrait("ChuShen8");
	}

	private static string ResolveCraftDisplayName(string traitId)
	{
		return XjCraftProfessionCatalog.TryGet(traitId, out XjCraftProfessionDefinition definition)
			? definition.DisplayName
			: "百艺";
	}

	private static bool TryResolveRealmBaseChance(string realmId, out float chancePercent)
	{
		chancePercent = realmId switch
		{
			XjRealmIds.TaiXi => 10f,
			XjRealmIds.LianQi => 15f,
			XjRealmIds.ZhuJi => 20f,
			XjRealmIds.HuangGuan => 20f,
			XjRealmIds.ZiFu => 30f,
			XjRealmIds.FuQiZhenRen => 30f,
			XjRealmIds.JinDan => 50f,
			XjRealmIds.ZhenJunYuShi => 50f,
			XjRealmIds.ShenDan => 50f,
			_ => 0f
		};
		return chancePercent > 0f;
	}
}
