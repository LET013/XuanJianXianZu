using System;
using System.Reflection;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.DengMingShi;

internal static class XjDengMingShiSpawnSafety
{
	internal static bool EnsureNativeCivilizationState(Actor actor)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return false;
		}
		if (!IsKingdomCiv(actor))
		{
			return true;
		}
		if (actor.kingdom?.data != null)
		{
			return true;
		}

		if (TryAttachCityKingdom(actor))
		{
			return true;
		}

		if (TryBuildNativeCivilization(actor))
		{
			return true;
		}

		return TryCreateFallbackKingdom(actor);
	}

	internal static void TryRemoveInvalidActor(Actor actor)
	{
		if (actor == null)
		{
			return;
		}

		const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		string[] noArgMethods = { "removeFromWorld", "remove", "killHimself", "kill", "destroy", "Destroy" };
		for (int i = 0; i < noArgMethods.Length; i++)
		{
			try
			{
				MethodInfo method = actor.GetType().GetMethod(noArgMethods[i], Flags, null, Type.EmptyTypes, null);
				if (method == null) continue;
				method.Invoke(actor, null);
				return;
			}
			catch
			{
			}
		}

		try
		{
			object manager = World.world?.units;
			MethodInfo method = manager?.GetType().GetMethod("removeObject", Flags, null, new[] { typeof(Actor) }, null);
			if (method != null)
			{
				method.Invoke(manager, new object[] { actor });
			}
		}
		catch
		{
		}
	}

	private static bool IsKingdomCiv(Actor actor)
	{
		try
		{
			return actor.isKingdomCiv();
		}
		catch
		{
			return false;
		}
	}

	private static bool TryAttachCityKingdom(Actor actor)
	{
		try
		{
			Kingdom cityKingdom = actor.city?.kingdom;
			if (cityKingdom?.data == null)
			{
				return false;
			}

			actor.setKingdom(cityKingdom);
			return actor.kingdom?.data != null;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryBuildNativeCivilization(Actor actor)
	{
		try
		{
			if (!actor.canBuildNewCity())
			{
				return false;
			}

			XjNationSectRebellionGuard.BeginControlledFounding();
			try
			{
				actor.buildCityAndStartCivilization();
			}
			finally
			{
				XjNationSectRebellionGuard.EndControlledFounding();
			}

			return actor.kingdom?.data != null;
		}
		catch
		{
			return false;
		}
	}

	private static bool TryCreateFallbackKingdom(Actor actor)
	{
		try
		{
			if (World.world?.kingdoms == null)
			{
				return false;
			}

			XjNationSectRebellionGuard.BeginControlledFounding();
			Kingdom kingdom;
			try
			{
				kingdom = World.world.kingdoms.makeNewCivKingdom(actor, string.Empty, false);
			}
			finally
			{
				XjNationSectRebellionGuard.EndControlledFounding();
			}

			if (kingdom?.data == null)
			{
				return actor.kingdom?.data != null;
			}

			actor.setKingdom(kingdom);
			return actor.kingdom?.data != null;
		}
		catch
		{
			return false;
		}
	}
}
