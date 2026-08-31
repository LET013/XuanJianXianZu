using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Aptitude;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.DongTian;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Interop.WorldBox;

/// <summary>
/// Optional compatibility bridge for Avb's Ultra Trait Pack (namespace: avbultratraitspack).
///
/// AVBS implements several traits by creating a fresh Actor and then calling
/// ActorTool.copyUnitToOtherUnit.  XuanJian normally interprets addTrait/removeTrait calls as
/// explicit gameplay edits; doing that while AVBS is only transporting an actor's traits can
/// re-enter cultivation/identity state machines on a half-built actor.  This bridge marks that
/// narrow copy window as a transport transaction, lets explicit AVBS spawns pass domain spawn
/// guards, and hands the finished actor back to the normal XuanJian registration path once.
///
/// There is deliberately no compile-time reference to AVBS.  If the mod is absent, this class
/// performs no patches and has no gameplay effect.
/// </summary>
internal static class XjAvbsTraitPackInterop
{
    private const string AvbsMainTypeName = "avbultratraitspack.Main";
    private const string AvbsDeathActionsTypeName = "avbultratraitspack.TraitDeathActions";
    private const string AvbsSpecialEffectsTypeName = "avbultratraitspack.TraitSpecialEffectActions";
    private const string ActorToolTypeName = "ReflectionUtility.ActorTool";
    private const int ProbeIntervalFrames = 120;
    private const int MaxProbeAttempts = 30;
    private const int MaxPostCopyActorsPerTick = 4;
    private const int MaxReplacementWaitTicks = 120;

    private static readonly Queue<PostCopyEntry> PendingTargets = new();
    private static readonly HashSet<Actor> PendingTargetSet = new();

    private static Harmony? _harmony;
    private static bool _installed;
    private static bool _loggedInstalled;
    private static int _lastProbeFrame = -100000;
    private static int _probeAttempts;


    internal static void Init(Harmony harmony)
    {
        _harmony = harmony;
        TryInstall();
    }

    internal static void Tick()
    {
        XjExternalUnitTransferContext.AdvancePendingReplacementRemovalLeases();

        if (!_installed
            && _harmony != null
            && _probeAttempts < MaxProbeAttempts
            && Time.frameCount - _lastProbeFrame >= ProbeIntervalFrames)
        {
            TryInstall();
        }

        int processed = 0;
        while (processed < MaxPostCopyActorsPerTick && PendingTargets.Count > 0)
        {
            PostCopyEntry entry = PendingTargets.Dequeue();
            Actor actor = entry.Target;
            processed++;

            if (actor?.data == null)
            {
                PendingTargetSet.Remove(actor);
                continue;
            }

            // A replacement copy is queued before AVBS reaches ActionLibrary.removeUnit(source).
            // Do not transfer unique XuanJian registries until the disposable source has actually
            // left the live world. This avoids a transient double-holder window.
            if (!entry.IsIndependentClone && !IsReplacementSourceGone(entry.Replacement))
            {
                if (entry.RetryCount < MaxReplacementWaitTicks)
                {
                    PendingTargets.Enqueue(entry.WithRetry());
                    continue;
                }

                // The third-party operation did not complete its source removal. Treat the new
                // actor as an independent clone instead of ever duplicating GuoWei/DaoTai/FaBao.
                PendingTargetSet.Remove(actor);
                ProcessIndependentClone(actor);
                continue;
            }

            PendingTargetSet.Remove(actor);
            try
            {
                if (entry.IsIndependentClone)
                {
                    ProcessIndependentClone(actor);
                }
                else
                {
                    ProcessReplacementHandoff(actor, entry.Replacement);
                }
            }
            catch (Exception exception)
            {
                XjExceptionDiagnostics.Report("XjAvbsTraitPackInterop.PostCopyRegistration", exception);
            }
        }
    }

    private static void ProcessIndependentClone(Actor actor)
    {
        if (actor?.data == null) return;
        try
        {
            // AVBS Duplicator creates an additional actor. Unique/strong XuanJian authority must
            // not be duplicated onto it: it keeps vanilla and AVBS traits and may enter the
            // cultivation chain normally later.
            XjVisibleTraitSync.ClearIndependentCloneCultivationState(actor);
            XjVisibleTraitSync.ClearCultivationDerivedNativeTraits(actor);
            XjFaBaoEquipmentSync.ClearEquippedFaBaoForCultivationReset(actor);
            XjFaBaoAccessor.ClearState(actor);
            XjScheduler.RegisterActor(actor);
            actor.setStatsDirty();
        }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("XjAvbsTraitPackInterop.IndependentClone", exception);
        }
    }

    private static void ProcessReplacementHandoff(Actor target, ReplacementSnapshot snapshot)
    {
        if (target?.data == null) return;
        long newActorId = ((BaseSystemData)target.data).id;
        long oldActorId = snapshot.SourceActorId;
        if (newActorId <= 0L || oldActorId <= 0L || newActorId == oldActorId)
        {
            XjScheduler.RegisterActor(target);
            target.setStatsDirty();
            return;
        }

        // Release only actor-id keyed runtime claims. This is a technical identity handoff, not
        // a XuanJian death: no death rewards, residual drops, reincarnation, succession, or
        // chronicle death events are committed for the disposable AVBS source actor.
        if (!string.IsNullOrWhiteSpace(snapshot.GuoWei))
        {
            XjGuoWeiRegistry.ReleaseForActor(oldActorId, snapshot.GuoWei);
        }
        XjGuoWeiQuanBingRegistry.RemoveActor(oldActorId);
        XjJieLinXianRegistry.Release(oldActorId);
        XjYuYiXianRegistry.Release(oldActorId);

        // Family is a logical identity, not a death record. Move persisted keys first so
        // target registration restores the same family instead of creating a duplicate row.
        XjFamilyMemberLedger.RebindActorAfterExternalReplacement(oldActorId, target);
        XjFamilyIdentityIndex.RebindActorAfterExternalReplacement(oldActorId, newActorId);

        // AVBS replacement is a technical identity handoff. Drop every rebuildable old-id runtime
        // cache now, but preserve logical state (Sect/ShenDan/active craft tasks) for dedicated rebind.
        XjScheduler.PrepareActorRuntimeForTechnicalReplacement(oldActorId);
        XjActorRegistry.Unregister(oldActorId);

        // ActorTool may copy the equipment payload verbatim; rewrite managed item ownership before
        // rehydration so the new actor is not mistaken for a foreign holder of its own equipment.
        XjFaBaoEquipmentSync.RebindEquippedOwnersAfterExternalReplacement(target, oldActorId);

        // Normal registration intentionally runs before high-realm rebuilding. If AVBS morphed a
        // cultivator into an unsupported species (mage/bandit/etc.), the existing XuanJian species
        // gate clears copied cultivation state here instead of preserving an illegal cultivator.
        XjScheduler.RegisterActor(target);
        int handoffYear = Math.Max(1, XjYearTracker.CurrentYear);

        // Persisted "current actor" state must follow the technical identity handoff as well.
        // Completed products/events keep the old creator id as history; only live ownership, open
        // tasks and current high-realm presence are rebound to the replacement actor.
        XjAlchemyRuntimeRegistry.RebindActorAfterExternalReplacement(oldActorId, newActorId, handoffYear);
        XjCraftDomainRegistry.RebindActorAfterExternalReplacement(oldActorId, newActorId, handoffYear);
        XjSectFormationRegistry.RebindLeadActorAfterExternalReplacement(oldActorId, newActorId);
        XjSecretRealmRegistry.RebindActorAfterExternalReplacement(oldActorId, newActorId);
        XjQianKunDaiRegistry.RebindActorAfterExternalReplacement(oldActorId, target);
        XjJinDanImmortalityRegistry.RebindActorAfterExternalReplacement(oldActorId, target, handoffYear);
        XjDaoTaiPresenceArchive.RebindActorAfterExternalReplacement(oldActorId, target, handoffYear);
        if (!XjSectCommands.RebindMemberAfterExternalReplacement(oldActorId, target, handoffYear))
        {
            // Unsupported AVBS morphs intentionally leave XuanJian cultivation. In that case the
            // old technical identity must not keep a live Sect authority row until the next audit.
            XjSectCommands.RemoveUnavailableMember(oldActorId, handoffYear);
        }

        if (snapshot.WasDaoTai)
        {
            if (XjSafeCore.IsAliveActor(target) && XjDaoTaiSpellScale.IsDaoTaiActor(target))
            {
                XjFruitPositionWorldState.RebindDaoTaiBindingAfterExternalReplacement(oldActorId, target);
            }
            else
            {
                XjFruitPositionWorldState.ReleaseDaoTaiBinding(
                    oldActorId, snapshot.SourceName, snapshot.DaoHui,
                    Math.Max(1, XjYearTracker.CurrentYear), "ExternalReplacementUnsupportedTarget");
            }
        }

        // Rehydrate from the copied ActorData without issuing breakthrough rewards. For normal
        // same-species replacement this makes GuoWei/authority point at the new stable Actor id.
        XjHighRealmRehydration.ReconcileActor(target, externalSpawn: false);
        XjJieLinXianRegistry.ReconcileLiveActor(target);
        XjYuYiXianRegistry.ReconcileLiveActor(target);
        XjShenDanRegistry.RebindActorAfterExternalReplacement(oldActorId, target);

        if (snapshot.WasShi && XjCultivationPathRules.IsShi(target))
        {
            XjShiWorldRegistry.RebindDependents(
                oldActorId, target, Math.Max(1, XjYearTracker.CurrentYear));
        }

        target.setStatsDirty();
    }

    private static bool IsReplacementSourceGone(ReplacementSnapshot snapshot)
    {
        Actor source = snapshot.Source;
        if (source == null || source.data == null) return true;
        try
        {
            if (!source.isAlive()) return true;
            if (snapshot.SourceActorId <= 0L || World.world?.units == null) return false;
            Actor live = World.world.units.get(snapshot.SourceActorId);
            return live == null || !ReferenceEquals(live, source) || !live.isAlive();
        }
        catch
        {
            return source.data == null;
        }
    }

    internal static void ClearRuntime()
    {
        PendingTargets.Clear();
        PendingTargetSet.Clear();
        XjExternalUnitTransferContext.Reset();
    }

    private static void TryInstall()
    {
        if (_installed || _harmony == null || _probeAttempts >= MaxProbeAttempts)
        {
            return;
        }

        _lastProbeFrame = Time.frameCount;
        _probeAttempts++;
        if (FindLoadedType(AvbsMainTypeName) == null)
        {
            return;
        }

        Type? deathActionsType = FindLoadedType(AvbsDeathActionsTypeName);
        Type? specialEffectsType = FindLoadedType(AvbsSpecialEffectsTypeName);
        int generationPatched = 0;

        if (deathActionsType != null)
        {
            generationPatched += PatchNamedMethod(deathActionsType, "replicateNow");
            generationPatched += PatchNamedMethod(deathActionsType, "specialMorphIntoMage");
            generationPatched += PatchNamedMethod(deathActionsType, "specialMorphIntoCreature");
        }

        if (specialEffectsType != null)
        {
            generationPatched += PatchNamedMethod(specialEffectsType, "cloneNow");
        }

        if (generationPatched <= 0)
        {
            return;
        }

        int copyPatched = PatchActorCopyMethods();
        _installed = true;

        if (!_loggedInstalled)
        {
            _loggedInstalled = true;
            Debug.Log($"[玄鉴][AVBS兼容] 已启用单位生成兼容：generation={generationPatched} copy={copyPatched}");
            if (copyPatched <= 0)
            {
                Debug.LogWarning("[玄鉴][AVBS兼容] 未找到 ActorTool.copyUnitToOtherUnit；仅启用显式生成放行，特质搬运事务未安装");
            }
        }
    }

    private static int PatchNamedMethod(Type declaringType, string methodName)
    {
        if (_harmony == null)
        {
            return 0;
        }

        MethodInfo? original = AccessTools.Method(declaringType, methodName);
        if (original == null)
        {
            return 0;
        }

        try
        {
            HarmonyMethod prefix = new(AccessTools.Method(typeof(XjAvbsTraitPackInterop), nameof(ExplicitGenerationPrefix)));
            HarmonyMethod finalizer = new(AccessTools.Method(typeof(XjAvbsTraitPackInterop), nameof(ExplicitGenerationFinalizer)));
            _harmony.Patch(original, prefix: prefix, finalizer: finalizer);
            return 1;
        }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("XjAvbsTraitPackInterop.PatchGeneration:" + methodName, exception);
            return 0;
        }
    }

    private static int PatchActorCopyMethods()
    {
        if (_harmony == null)
        {
            return 0;
        }

        MethodInfo prefixMethod = AccessTools.Method(typeof(XjAvbsTraitPackInterop), nameof(ActorCopyPrefix));
        MethodInfo finalizerMethod = AccessTools.Method(typeof(XjAvbsTraitPackInterop), nameof(ActorCopyFinalizer));
        if (prefixMethod == null || finalizerMethod == null)
        {
            return 0;
        }

        Type? actorToolType = FindLoadedType(ActorToolTypeName) ?? FindLoadedTypeBySimpleName("ActorTool");
        if (actorToolType == null)
        {
            return 0;
        }

        int patched = 0;
        MethodInfo[] methods = actorToolType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (!string.Equals(method.Name, "copyUnitToOtherUnit", StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length < 2
                || parameters[0].ParameterType != typeof(Actor)
                || parameters[1].ParameterType != typeof(Actor))
            {
                continue;
            }

            try
            {
                _harmony.Patch(
                    method,
                    prefix: new HarmonyMethod(prefixMethod),
                    finalizer: new HarmonyMethod(finalizerMethod));
                patched++;
            }
            catch (Exception exception)
            {
                XjExceptionDiagnostics.Report("XjAvbsTraitPackInterop.PatchActorCopy", exception);
            }
        }

        return patched;
    }

    private static Type? FindLoadedTypeBySimpleName(string simpleName)
    {
        if (string.IsNullOrWhiteSpace(simpleName)) return null;

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                Type[] types = assemblies[i].GetTypes();
                for (int j = 0; j < types.Length; j++)
                {
                    Type? type = types[j];
                    if (type != null && string.Equals(type.Name, simpleName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }
            catch (ReflectionTypeLoadException exception)
            {
                Type[]? types = exception.Types;
                if (types == null) continue;
                for (int j = 0; j < types.Length; j++)
                {
                    Type? type = types[j];
                    if (type != null && string.Equals(type.Name, simpleName, StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }
            catch
            {
                // Optional compatibility discovery must remain best-effort.
            }
        }

        return null;
    }

    private static Type? FindLoadedType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            try
            {
                Type? type = assemblies[i].GetType(fullName, throwOnError: false, ignoreCase: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
                // Optional-mod detection must never block XuanJian loading because another
                // dynamic assembly cannot be inspected.
            }
        }

        return null;
    }

    private static bool ExplicitGenerationPrefix(
        MethodBase __originalMethod,
        object[] __args,
        ref bool __result,
        out ExplicitGenerationState __state)
    {
        bool isReplacement = !string.Equals(__originalMethod?.Name, "cloneNow", StringComparison.Ordinal);
        Actor? replacementSource = null;
        if (isReplacement && __args != null && __args.Length > 0)
        {
            replacementSource = __args[0] as Actor;
        }

        // 金性妖邪阴司死籍是代码终局。AVBS 的死亡替身/变形不得在这一次
        // XjDeathCause.YinSi 中接管 action_death，否则会把已判死的妖邪重新复制出来。
        if (replacementSource?.data != null
            && XjTrueDamageSystem.HasIrreversibleYinSiClaim(replacementSource)
            && XuanJianVNext.Systems.Death.XjDeathArbitrationPipeline.IsForcedCause(
                replacementSource, XuanJianVNext.Systems.Death.XjDeathCause.YinSi))
        {
            __state = default;
            __result = false;
            return false;
        }

        XjExternalUnitTransferContext.EnterExplicitExternalGeneration(replacementSource);
        __state = new ExplicitGenerationState(true, isReplacement, replacementSource);
        return true;
    }

    private static Exception? ExplicitGenerationFinalizer(
        Exception? __exception,
        bool __result,
        ExplicitGenerationState __state)
    {
        if (__state.Entered)
        {
            bool keepRemovalMarker = __state.IsReplacement
                && __state.ReplacementSource != null
                && __exception == null
                && __result;
            XjExternalUnitTransferContext.ExitExplicitExternalGeneration(keepRemovalMarker);
        }

        return __exception;
    }

    private static void ActorCopyPrefix(object[] __args, out CopyTransferState __state)
    {
        __state = default;
        if (!XjExternalUnitTransferContext.IsExplicitExternalGeneration)
        {
            return;
        }

        Actor? source = __args != null && __args.Length > 0 ? __args[0] as Actor : null;
        Actor? target = __args != null && __args.Length > 1 ? __args[1] as Actor : null;
        bool independentClone = source != null
            && !XjExternalUnitTransferContext.IsExplicitReplacementRemoval(source);
        ReplacementSnapshot replacement = independentClone
            ? default
            : CaptureReplacementSnapshot(source);
        XjExternalUnitTransferContext.EnterTraitTransfer();
        __state = new CopyTransferState(true, target, independentClone, replacement);
    }

    private static Exception? ActorCopyFinalizer(Exception? __exception, CopyTransferState __state)
    {
        if (!__state.Entered)
        {
            return __exception;
        }

        XjExternalUnitTransferContext.ExitTraitTransfer();
        if (__exception == null && __state.Target?.data != null)
        {
            QueuePostCopyRegistration(
                __state.Target, __state.IsIndependentClone, __state.Replacement);
        }

        return __exception;
    }

    private static ReplacementSnapshot CaptureReplacementSnapshot(Actor? source)
    {
        if (source?.data == null) return default;
        try
        {
            long actorId = ((BaseSystemData)source.data).id;
            XjJinDanState positionState = XjJinDanAccessor.BuildPositionCarrierState(source);
            return new ReplacementSnapshot(
                source,
                actorId,
                source.getName() ?? string.Empty,
                positionState.Found ? positionState.GuoWei ?? string.Empty : string.Empty,
                XjDaoTaiSpellScale.IsDaoTaiActor(source),
                (int)XjDaoHuiPolicy.Read(source),
                XjCultivationPathRules.IsShi(source));
        }
        catch (Exception exception)
        {
            XjExceptionDiagnostics.Report("XjAvbsTraitPackInterop.CaptureReplacement", exception);
            long actorId = 0L;
            try { actorId = source?.data == null ? 0L : ((BaseSystemData)source.data).id; } catch { }
            return new ReplacementSnapshot(source, actorId, string.Empty, string.Empty, false, 0, false);
        }
    }

    private static void QueuePostCopyRegistration(
        Actor actor,
        bool isIndependentClone,
        ReplacementSnapshot replacement)
    {
        if (actor?.data == null || !PendingTargetSet.Add(actor))
        {
            return;
        }

        PendingTargets.Enqueue(new PostCopyEntry(actor, isIndependentClone, replacement, 0));
    }


    private readonly struct ExplicitGenerationState
    {
        internal ExplicitGenerationState(bool entered, bool isReplacement, Actor? replacementSource)
        {
            Entered = entered;
            IsReplacement = isReplacement;
            ReplacementSource = replacementSource;
        }

        internal bool Entered { get; }
        internal bool IsReplacement { get; }
        internal Actor? ReplacementSource { get; }
    }

    private readonly struct ReplacementSnapshot
    {
        internal ReplacementSnapshot(
            Actor? source,
            long sourceActorId,
            string sourceName,
            string guoWei,
            bool wasDaoTai,
            int daoHui,
            bool wasShi)
        {
            Source = source;
            SourceActorId = sourceActorId;
            SourceName = sourceName ?? string.Empty;
            GuoWei = guoWei ?? string.Empty;
            WasDaoTai = wasDaoTai;
            DaoHui = daoHui;
            WasShi = wasShi;
        }

        internal Actor? Source { get; }
        internal long SourceActorId { get; }
        internal string SourceName { get; }
        internal string GuoWei { get; }
        internal bool WasDaoTai { get; }
        internal int DaoHui { get; }
        internal bool WasShi { get; }
    }

    private readonly struct CopyTransferState
    {
        internal CopyTransferState(
            bool entered,
            Actor? target,
            bool isIndependentClone,
            ReplacementSnapshot replacement)
        {
            Entered = entered;
            Target = target;
            IsIndependentClone = isIndependentClone;
            Replacement = replacement;
        }

        internal bool Entered { get; }
        internal Actor? Target { get; }
        internal bool IsIndependentClone { get; }
        internal ReplacementSnapshot Replacement { get; }
    }

    private readonly struct PostCopyEntry
    {
        internal PostCopyEntry(
            Actor target,
            bool isIndependentClone,
            ReplacementSnapshot replacement,
            int retryCount)
        {
            Target = target;
            IsIndependentClone = isIndependentClone;
            Replacement = replacement;
            RetryCount = Math.Max(0, retryCount);
        }

        internal Actor Target { get; }
        internal bool IsIndependentClone { get; }
        internal ReplacementSnapshot Replacement { get; }
        internal int RetryCount { get; }

        internal PostCopyEntry WithRetry()
        {
            return new PostCopyEntry(Target, IsIndependentClone, Replacement, RetryCount + 1);
        }
    }

}
