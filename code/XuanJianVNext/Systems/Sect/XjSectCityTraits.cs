namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 宗门城镇状态框架。当前只提供稳定持久化字段，不主动改变数值或玩法；
/// 后续可分别接入宗门规模、灵脉等级、护山阵势和宗门环境机制。
/// </summary>
internal readonly struct XjSectCityTraitState
{
	internal readonly int ScaleLevel;
	internal readonly int SpiritVeinLevel;
	internal readonly int FormationLevel;
	internal readonly string EnvironmentState;

	internal bool HasConfiguredState => ScaleLevel > 0
		|| SpiritVeinLevel > 0
		|| FormationLevel > 0
		|| !string.IsNullOrWhiteSpace(EnvironmentState);

	internal XjSectCityTraitState(int scaleLevel, int spiritVeinLevel, int formationLevel, string environmentState)
	{
		ScaleLevel = scaleLevel < 0 ? 0 : scaleLevel;
		SpiritVeinLevel = spiritVeinLevel < 0 ? 0 : spiritVeinLevel;
		FormationLevel = formationLevel < 0 ? 0 : formationLevel;
		EnvironmentState = environmentState ?? string.Empty;
	}
}

internal static class XjSectCityTraitAccessor
{
	private const string KeyScaleLevel = "xuanjian.vnext.city.zongmen.trait.scale_level";
	private const string KeySpiritVeinLevel = "xuanjian.vnext.city.zongmen.trait.spirit_vein_level";
	private const string KeyFormationLevel = "xuanjian.vnext.city.zongmen.trait.formation_level";
	private const string KeyEnvironmentState = "xuanjian.vnext.city.zongmen.trait.environment_state";

	internal static XjSectCityTraitState BuildState(City city)
	{
		if (city?.data == null) return default;
		city.data.get(KeyScaleLevel, out int scaleLevel, 0);
		city.data.get(KeySpiritVeinLevel, out int spiritVeinLevel, 0);
		city.data.get(KeyFormationLevel, out int formationLevel, 0);
		city.data.get(KeyEnvironmentState, out string environmentState, string.Empty);
		return new XjSectCityTraitState(scaleLevel, spiritVeinLevel, formationLevel, environmentState);
	}

	internal static void EnsureDefaults(City city)
	{
		if (city?.data == null) return;
		city.data.get(KeyScaleLevel, out int scaleLevel, 0);
		city.data.get(KeySpiritVeinLevel, out int spiritVeinLevel, 0);
		city.data.get(KeyFormationLevel, out int formationLevel, 0);
		if (scaleLevel < 0) city.data.set(KeyScaleLevel, 0);
		if (spiritVeinLevel < 0) city.data.set(KeySpiritVeinLevel, 0);
		if (formationLevel < 0) city.data.set(KeyFormationLevel, 0);
	}

	internal static void WriteState(City city, in XjSectCityTraitState state)
	{
		if (city?.data == null) return;
		city.data.set(KeyScaleLevel, state.ScaleLevel);
		city.data.set(KeySpiritVeinLevel, state.SpiritVeinLevel);
		city.data.set(KeyFormationLevel, state.FormationLevel);
		city.data.set(KeyEnvironmentState, state.EnvironmentState);
	}

	internal static void Clear(City city)
	{
		if (city?.data == null) return;
		city.data.set(KeyScaleLevel, 0);
		city.data.set(KeySpiritVeinLevel, 0);
		city.data.set(KeyFormationLevel, 0);
		city.data.set(KeyEnvironmentState, string.Empty);
	}
}
