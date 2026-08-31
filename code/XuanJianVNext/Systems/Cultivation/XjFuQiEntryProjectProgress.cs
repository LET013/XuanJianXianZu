using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Aptitude;

namespace XuanJianVNext.Systems.Cultivation;

/// <summary>
/// 服气入门长期项目的公共进度与年限策略。各道途 Handler 只提供自己的
/// 年限区间与确定性盐值，避免六套相同公式各自漂移。
/// </summary>
internal static class XjFuQiEntryProjectProgress
{
    internal static void Update(Actor actor, int currentYear, int completeYear)
    {
        if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.FuQiCoreProjectStartYear, out int startYear)
            || startYear <= 0 || completeYear <= startYear) return;
        float ratio = Math.Clamp((currentYear - startYear) / (float)(completeYear - startYear), 0f, 1f);
        XjActorAccessor.SetInt(actor, XjActorDataKeys.FuQiCoreProgress, (int)Math.Round(ratio * 10000f));
    }

    internal static int ResolveEntryYears(
        Actor actor,
        int aptitude,
        int aptitude6Min,
        int aptitude6Max,
        int aptitude5Min,
        int aptitude5Max,
        int aptitude4Min,
        int aptitude4Max,
        string deterministicSalt)
    {
        int min;
        int max;
        if (aptitude >= 6) { min = aptitude6Min; max = aptitude6Max; }
        else if (aptitude == 5) { min = aptitude5Min; max = aptitude5Max; }
        else { min = aptitude4Min; max = aptitude4Max; }

        XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
        float quality = XjDaoHuiPolicy.Normalize01(huiGuang);
        int target = max - (int)Math.Round((max - min) * quality);
        long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
        int jitter = XjDeterministicHash.PositiveIndex(actorId + aptitude, deterministicSalt ?? string.Empty, 5) - 2;
        return Math.Clamp(target + jitter, min, max);
    }
}
