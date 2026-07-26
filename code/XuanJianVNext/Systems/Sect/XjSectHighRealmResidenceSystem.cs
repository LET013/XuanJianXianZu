using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// A ZiFu or JinDan who still belongs to an extant sect is seated at that
/// sect's mountain gate. Native city migration must not detach the person
/// from the sect while leaving their authority and engineering tasks behind.
/// </summary>
internal static class XjSectHighRealmResidenceSystem
{
	internal static bool AllowNativeCityAssignment(Actor actor, City targetCity)
	{
		// 宗门高于国家且可横跨多国；高境成员的宗门身份不再绑定唯一山门城市。
		return true;
	}

	internal static bool Enforce(Actor actor, int currentYear)
	{
		// 保留调用入口兼容，但不再强制把紫府/金丹迁回山门。
		return false;
	}

	internal static bool IsAtSectGate(Actor actor, long sectId)
	{
		if (actor?.data == null || sectId <= 0L || !XjZongMenCityData.TryResolveZongMenCity(sectId, out City homeCity)) return false;
		return IsSameCity(actor.city, homeCity);
	}

	private static bool RequiresSectResidence(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive() || XjLongShuSystem.IsLongShu(actor)) return false;
		if (XjRealmSuppression.GetRealmTier(actor) < XjRealmSuppression.TierZiFu) return false;
		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		return identity.Found && identity.ZongMenId > 0L;
	}

	private static bool TryResolveHomeCity(Actor actor, out City homeCity)
	{
		homeCity = null;
		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		return identity.Found
			&& identity.ZongMenId > 0L
			&& XjZongMenCityData.TryResolveZongMenCity(identity.ZongMenId, out homeCity)
			&& homeCity?.data != null;
	}

	private static bool IsSameCity(City left, City right)
	{
		if (left?.data == null || right?.data == null) return false;
		return ReferenceEquals(left, right) || left.data.id == right.data.id;
	}
}
