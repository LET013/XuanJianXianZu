using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.UI.ZongMen;

internal static class XjZongMenWindow
{
	internal static void ShowForActor(Actor actor)
	{
		if (actor?.city?.data != null && XjSectCityData.HasZongMen(actor.city))
		{
			XjZongMenDetailWindow.Show(actor.city);
		}
	}

	internal static void ShowForCity(City city)
	{
		if (city?.data != null && XjSectCityData.HasZongMen(city))
		{
			XjZongMenDetailWindow.Show(city);
		}
	}
}
