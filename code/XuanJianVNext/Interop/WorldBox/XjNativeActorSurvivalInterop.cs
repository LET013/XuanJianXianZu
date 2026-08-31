namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Narrow compatibility adapter for WorldBox actor-data survival meters.
/// Different game versions/mods expose different member names and numeric types.
/// </summary>
internal static class XjNativeActorSurvivalInterop
{
    private static readonly string[] SurvivalMemberNames =
    {
        "hunger", "food", "nutrition", "satiety", "stomach"
    };

    internal static void RestoreSurvivalMeters(object actorData, float value)
    {
        if (actorData == null) return;
        for (int i = 0; i < SurvivalMemberNames.Length; i++)
        {
            XjNativeReflectionInterop.TryWriteMemberValue(actorData, SurvivalMemberNames[i], value);
        }
    }
}
