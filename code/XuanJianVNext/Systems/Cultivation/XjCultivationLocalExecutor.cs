using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.Cultivation;

internal readonly struct XjCultivationLocalExecuteResult
{
	internal readonly bool ActorValid;
	internal readonly bool ZhenYuanChanged;
	internal readonly bool RealmChanged;
	internal readonly string PreviousRealmId;
	internal readonly string CurrentRealmId;
	internal readonly string TargetRealmId;
	internal readonly string ReasonCode;

	internal XjCultivationLocalExecuteResult(
		bool actorValid,
		bool zhenYuanChanged,
		bool realmChanged,
		string previousRealmId,
		string currentRealmId,
		string targetRealmId,
		string reasonCode)
	{
		ActorValid = actorValid;
		ZhenYuanChanged = zhenYuanChanged;
		RealmChanged = realmChanged;
		PreviousRealmId = previousRealmId ?? string.Empty;
		CurrentRealmId = currentRealmId ?? string.Empty;
		TargetRealmId = targetRealmId ?? string.Empty;
		ReasonCode = reasonCode ?? string.Empty;
	}
}

internal static class XjCultivationLocalExecutor
{
	internal static XjCultivationLocalExecuteResult RunValidatedLocalStep(
		Actor actor,
		float zhenYuanGain,
		in XjActorCultivationSnapshot before)
	{
		if (actor?.data == null)
		{
			return new XjCultivationLocalExecuteResult(false, false, false, string.Empty, string.Empty, string.Empty, "ActorNull");
		}
		if (!XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return new XjCultivationLocalExecuteResult(true, false, false, before.RealmId, before.RealmId, string.Empty, "NotZiFuJinDanPath");
		}
	
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.RealmManualRemoved, out int manualRemoved) && manualRemoved > 0)
			{
				return new XjCultivationLocalExecuteResult(true, false, false, before.RealmId, before.RealmId, string.Empty, "RealmManuallyRemoved");
			}

			if (before.XjZz <= 0)
			{
				return new XjCultivationLocalExecuteResult(true, false, false, before.RealmId, before.RealmId, string.Empty, "NoAptitude");
			}
			bool zhenYuanChanged = false;
	
			float effectiveZhenYuanGain = XjCultivationGrowthRules.CalculateZhenYuanGain(actor, zhenYuanGain);
			effectiveZhenYuanGain *= XjMingShuChildSystem.ResolveCultivationMultiplier(actor);
			effectiveZhenYuanGain *= XjMingShuSchemeSystem.ResolveCultivationMultiplier(actor);
			effectiveZhenYuanGain *= XjUpperCultivatorGoldSupportSystem.ResolveCultivationMultiplier(actor);
			if (Math.Abs(effectiveZhenYuanGain) > 0.001f)
			{
				float nextZhenYuan = XjCultivationGrowthRules.ApplyRealmCap(before, (float)Math.Floor(Math.Max(0f, before.ZhenYuan + effectiveZhenYuanGain)));
				nextZhenYuan = XjBottleneckEventSystem.ApplyGrowthGate(actor, in before, nextZhenYuan);
				XjActorAccessor.SetFloat(actor, XjActorDataKeys.ZhenYuan, nextZhenYuan);
				zhenYuanChanged = true;
			}
	
		return new XjCultivationLocalExecuteResult(true, zhenYuanChanged, false, before.RealmId, before.RealmId, string.Empty, "GrowthOnly");
	}

}
