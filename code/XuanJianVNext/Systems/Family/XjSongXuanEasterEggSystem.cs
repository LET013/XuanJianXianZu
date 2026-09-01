using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Family;

internal sealed class XjSongXuanEasterEggArchiveData
{
    public bool Attempted;
    public bool Born;
    public long ActorId;
    public long SongFamilyStableId;
    public int BirthYear;
}

/// <summary>
/// 支持者彩蛋：一局世界只进行一次“宋玄”抽签。第一次确认到宋氏谱系真实新生儿时
/// 立即把 Attempted 写入世界归档；命中则定为宋玄，未命中则本世界永久关闭彩蛋入口。
/// 旧档已有宋玄只恢复人物事实并校正固定道途，不重新抽签。全程复用出生/家族事件，不增加年度人口扫描。
/// </summary>
internal static class XjSongXuanEasterEggSystem
{
    internal const string ModuleId = "family.song-xuan-easter-egg";
    internal const string Name = "宋玄";
    internal const string DaoTu = "玄雷";
    private const string Surname = "宋";
    private const int AppearanceChancePerTenThousand = 3000;

    private static XjSongXuanEasterEggArchiveData _state = new XjSongXuanEasterEggArchiveData();
    private static long _bootstrapInfantCandidateId;

    internal static bool HasAttempted => _state?.Attempted ?? false;
    internal static bool HasBorn => _state?.Born ?? false;

    internal static bool IsSongXuan(Actor actor)
    {
        if (actor?.data == null) return false;
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjSongXuan, out int marker) && marker > 0) return true;
        long actorId = ((BaseSystemData)actor.data).id;
        return actorId > 0L && (_state?.Born ?? false) && _state.ActorId == actorId;
    }

    internal static void ObserveConfirmedFamilyMember(Actor actor, XjFamilyIdentity identity, int currentYear)
    {
        if (actor?.data == null || identity == null || !identity.Found || identity.FamilyStableIdValue <= 0L) return;
        if (!IsTrackedSongFamily(identity.FamilyStableIdValue)) return;

        bool changed = false;
        if ((_state?.SongFamilyStableId ?? 0L) <= 0L)
        {
            _state ??= new XjSongXuanEasterEggArchiveData();
            _state.SongFamilyStableId = identity.FamilyStableIdValue;
            changed = true;
        }

        if (!HasAttempted && IsBirthYear(actor))
        {
            TryAttemptDesignation(actor, identity.FamilyStableIdValue, currentYear);
            return;
        }

        if (changed) MarkChanged();
    }

    /// <summary>
    /// 原生出生事务的事件式入口。普通角色不会在出生当年进入玄鉴家族索引，
    /// 因此宋玄不能依赖五岁时的 AddActorToFamily 才判断“新生儿”。这里仅沿
    /// 已出生孩子的父系权威家族账本做 O(1) 解析，不建立新家族、不扫描人口。
    /// </summary>
    internal static void ObserveNativeBirth(Actor child, Actor parent1, Actor parent2)
    {
        if (HasAttempted || child?.data == null || !child.isAlive()) return;

        long familyId = TryResolveBirthFamilyStableId(child, parent1, parent2);
        if (familyId <= 0L || !IsTrackedSongFamily(familyId)) return;

        _state ??= new XjSongXuanEasterEggArchiveData();
        if (_state.SongFamilyStableId <= 0L)
        {
            _state.SongFamilyStableId = familyId;
            MarkChanged();
        }

        TryAttemptDesignation(child, familyId, ResolveCurrentYear());
    }

    /// <summary>
    /// 冷启动复用已经存在的唯一人口快照。这里只读家族账本/姓氏索引并记录一个候选ID，
    /// 不持有Actor引用，不创建第二次世界扫描。
    /// </summary>
    internal static void ObserveBootstrapActor(Actor actor)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        if (IsSongXuan(actor))
        {
            RecoverExisting(actor, TryResolveFamilyStableId(actorId));
            return;
        }

        long familyId = TryResolveFamilyStableId(actorId);
        if (familyId <= 0L && IsBirthYear(actor))
        {
            familyId = TryResolveLineageFamilyStableId(actor);
        }
        if (familyId <= 0L || !IsTrackedSongFamily(familyId)) return;

        _state ??= new XjSongXuanEasterEggArchiveData();
        if (_state.SongFamilyStableId <= 0L)
        {
            _state.SongFamilyStableId = familyId;
            MarkChanged();
        }

        if (HasAttempted || !IsBirthYear(actor)) return;
        // 确定性取最小ActorId，避免bootstrap遍历顺序影响旧档彩蛋归属。
        if (_bootstrapInfantCandidateId <= 0L || actorId < _bootstrapInfantCandidateId)
        {
            _bootstrapInfantCandidateId = actorId;
        }
    }

    internal static void CompleteBootstrap(int currentYear)
    {
        if (!HasAttempted && _bootstrapInfantCandidateId > 0L
            && XjActorRegistry.ResolveKnownOrWorld(_bootstrapInfantCandidateId, out Actor actor)
            && actor?.data != null)
        {
            long familyId = TryResolveLineageFamilyStableId(actor);
            if (familyId > 0L && IsTrackedSongFamily(familyId))
            {
                TryAttemptDesignation(actor, familyId, currentYear);
            }
        }
        _bootstrapInfantCandidateId = 0L;
    }

    internal static void EnsureAgeFiveDestiny(Actor actor)
    {
        if (!IsSongXuan(actor) || actor?.data == null) return;

        // 五岁不再是“第一次纠正宋玄”的时点，而只是重新确认出生时已经写死的人物事实。
        EnsureIdentityInvariant(actor, syncVisibleTraits: false, repairWrongPath: true);
        XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
        XjVisibleTraitSync.SyncCultivationTraits(actor);
    }

	internal static string EnsureQuZhiTitle(Actor actor, string title)
    {
        if (!IsSongXuan(actor)) return title ?? string.Empty;
        string value = (title ?? string.Empty).Trim();
		if (TryRebuildJunTitle(value, "曲直", out string rebuilt)) return rebuilt;
        if (value.Contains("曲直", StringComparison.Ordinal)) return value;
        return value.Length == 0 ? "曲直" : "曲直" + value;
	}

	private static bool TryRebuildJunTitle(string value, string prefix, out string rebuilt)
	{
		rebuilt = string.Empty;
		if (string.IsNullOrWhiteSpace(value) || value.Length < 4) return false;
		string[] suffixes = { "玄君", "神君", "真君", "元君", "帝君", "飞君" };
		for (int i = 0; i < suffixes.Length; i++)
		{
			string suffix = suffixes[i];
			if (!value.EndsWith(suffix, StringComparison.Ordinal)) continue;
			string stem = value.Substring(0, value.Length - suffix.Length);
			if (stem.StartsWith(prefix, StringComparison.Ordinal)) stem = stem.Substring(prefix.Length);
			if (stem.Length < 4) return false;
			rebuilt = prefix + stem.Substring(0, 4) + suffix;
			return true;
		}
		return false;
	}

	internal static string BuildRealmName(Actor actor, string baseName, string realmDisplay)
	{
		if (!IsSongXuan(actor)) return string.Empty;
		return "曲直" + (string.IsNullOrWhiteSpace(baseName) ? Name : baseName.Trim()) + (realmDisplay ?? string.Empty);
	}

    internal static string ExportPayload()
    {
        XjSongXuanEasterEggArchiveData state = _state ?? new XjSongXuanEasterEggArchiveData();
        // schema v2：Attempted 单独持久化，失败世界也必须永久记住“已经抽过一次”。
        return (state.Attempted ? "1" : "0") + "|"
            + (state.Born ? "1" : "0") + "|"
            + state.ActorId.ToString(CultureInfo.InvariantCulture) + "|"
            + state.SongFamilyStableId.ToString(CultureInfo.InvariantCulture) + "|"
            + state.BirthYear.ToString(CultureInfo.InvariantCulture);
    }

    internal static void ImportPayload(int schemaVersion, string payload)
    {
        _state = new XjSongXuanEasterEggArchiveData();
        if (!string.IsNullOrWhiteSpace(payload))
        {
            string[] parts = payload.Split('|');
            if (schemaVersion >= 2 && parts.Length >= 5)
            {
                _state.Attempted = string.Equals(parts[0], "1", StringComparison.Ordinal);
                _state.Born = string.Equals(parts[1], "1", StringComparison.Ordinal);
                if (long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId))
                    _state.ActorId = Math.Max(0L, actorId);
                if (long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out long familyId))
                    _state.SongFamilyStableId = Math.Max(0L, familyId);
                if (int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int birthYear))
                    _state.BirthYear = Math.Max(0, birthYear);
            }
            else if (parts.Length >= 4)
            {
                // v1 没有 Attempted。已有宋玄说明世界必然已经完成彩蛋；未出生旧档
                // 则允许升级后在第一名新的宋氏新生儿身上进行唯一一次抽签。
                _state.Born = string.Equals(parts[0], "1", StringComparison.Ordinal);
                _state.Attempted = _state.Born;
                if (long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long actorId))
                    _state.ActorId = Math.Max(0L, actorId);
                if (long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long familyId))
                    _state.SongFamilyStableId = Math.Max(0L, familyId);
                if (int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int birthYear))
                    _state.BirthYear = Math.Max(0, birthYear);
            }
        }
        if (_state.ActorId <= 0L) _state.Born = false;
        if (_state.Born) _state.Attempted = true;
        _bootstrapInfantCandidateId = 0L;
    }

    internal static void ClearRuntime()
    {
        _state = new XjSongXuanEasterEggArchiveData();
        _bootstrapInfantCandidateId = 0L;
    }

    private static void TryAttemptDesignation(Actor actor, long familyId, int currentYear)
    {
        if (HasAttempted || actor?.data == null || familyId <= 0L) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        _state ??= new XjSongXuanEasterEggArchiveData();
        _state.Attempted = true;
        _state.SongFamilyStableId = familyId;
        MarkChanged();

        int year = Math.Max(1, currentYear > 0 ? currentYear : ResolveCurrentYear());
        int roll = XjDeterministicHash.PositiveIndex(
            actorId + familyId + (long)year * 17L,
            "song_xuan.world_once",
            10000);
        if (roll >= AppearanceChancePerTenThousand) return;
        Designate(actor, familyId, year);
    }

    private static void Designate(Actor actor, long familyId, int currentYear)
    {
        if (HasBorn || actor?.data == null || familyId <= 0L) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;

        _state ??= new XjSongXuanEasterEggArchiveData();
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjSongXuan, 1);
        XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, Name);
        XjActorStateWriteGateway.SetDisplayName(actor, Name, customName: true);
        // 人物设定硬约束：宋玄必为男性；出生当刻即锁定紫府金丹·玄雷，
        // 不再留给普通感气、释修或随机道途入口一个先写错状态的时间窗。
        actor.data.sex = ActorSex.Male;
        EnsureIdentityInvariant(actor, syncVisibleTraits: false, repairWrongPath: true);
        try
        {
            if (!actor.hasTrait(XjDaoTaiPosturePolicy.TraitId)) actor.addTrait(XjDaoTaiPosturePolicy.TraitId);
        }
        catch (Exception ex)
        {
            XjExceptionDiagnostics.Report("XjSongXuanEasterEggSystem.Designate.DaoTaiPosture", ex);
        }

        _state.Attempted = true;
        _state.Born = true;
        _state.ActorId = actorId;
        _state.SongFamilyStableId = familyId;
        _state.BirthYear = Math.Max(1, currentYear > 0 ? currentYear : ResolveCurrentYear());
        MarkChanged();
    }

    private static void RecoverExisting(Actor actor, long familyId)
    {
        if (actor?.data == null) return;
        long actorId = ((BaseSystemData)actor.data).id;
        if (actorId <= 0L) return;
        _state ??= new XjSongXuanEasterEggArchiveData();
        bool changed = !_state.Attempted || !_state.Born || _state.ActorId != actorId;
        XjActorAccessor.SetInt(actor, XjActorDataKeys.XjSongXuan, 1);
        actor.data.sex = ActorSex.Male;
        EnsureIdentityInvariant(actor, syncVisibleTraits: false, repairWrongPath: true);
        _state.Attempted = true;
        _state.Born = true;
        _state.ActorId = actorId;
        if (familyId > 0L) _state.SongFamilyStableId = familyId;
        if (_state.BirthYear <= 0) _state.BirthYear = Math.Max(1, ResolveCurrentYear() - (int)Math.Floor(Math.Max(0f, actor.getAge())));
        if (changed) MarkChanged();
    }

    private static void EnsureIdentityInvariant(Actor actor, bool syncVisibleTraits, bool repairWrongPath)
    {
        if (actor?.data == null || !IsSongXuan(actor)) return;
        actor.data.sex = ActorSex.Male;

        bool hasPath = XjCultivationPathRules.TryGetPath(actor, out string existingPath);
        if (hasPath && !string.Equals(existingPath, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal) && repairWrongPath)
        {
            // 旧档可能在出生链断裂期间把宋玄送入服气/释修。宋玄身份优先于这份错误修法元数据，
            // 清掉跨修法专属状态后回到紫府金丹主链；境界与人物通用数据由各自权威字段保留。
            XjCultivationPathTransitions.ClearAll(actor);
            hasPath = false;
        }
        if (!hasPath)
        {
            XjCultivationPathTransitions.TrySetInitialPath(
                actor,
                XjCultivationPathIds.ZiFuJinDan,
                DaoTu,
                string.Empty,
                syncVisibleTraits: false);
        }

        // 新生儿尚未建立功法集合，不能走需要“道途+功法同事务一致”的完整换道事务；
        // 先用专门的 metadata-only 恢复入口写死玄雷。五岁资质落定后再建立/对齐真实功法。
        XjCultivationStateTransitions.TrySetDaoTuMetadataOnly(actor, DaoTu, false);
        XjFamilyDaoTuRules.RememberInitialDaoTuOrigin(actor, DaoTu);
        if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)
        {
            XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
            XjGongFaProgression.ReconcileDaoTu(actor, DaoTu, "宋玄玄雷身份不变量");
        }
        if (syncVisibleTraits) XjVisibleTraitSync.SyncCultivationTraits(actor);
    }

    private static long TryResolveFamilyStableId(long actorId)
    {
        if (actorId <= 0L) return 0L;
        if (XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
            && identity != null && identity.Found && identity.FamilyStableIdValue > 0L)
        {
            return identity.FamilyStableIdValue;
        }
        if (XjFamilyMemberLedger.TryGetByActorId(actorId, out XjFamilyMemberLedgerEntry ledger)
            && ledger.Found && ledger.FamilyStableId > 0L)
        {
            return ledger.FamilyStableId;
        }
        return 0L;
    }

    private static long TryResolveBirthFamilyStableId(Actor child, Actor parent1, Actor parent2)
    {
        Actor father = null;
        if (parent1?.data != null && parent1.data.sex == ActorSex.Male) father = parent1;
        else if (parent2?.data != null && parent2.data.sex == ActorSex.Male) father = parent2;

        if (father?.data != null)
        {
            long fatherId = ((BaseSystemData)father.data).id;
            long fatherFamilyId = TryResolveFamilyStableId(fatherId);
            if (fatherFamilyId > 0L) return fatherFamilyId;
        }

        return TryResolveLineageFamilyStableId(child);
    }

    private static long TryResolveLineageFamilyStableId(Actor actor)
    {
        if (actor?.data == null) return 0L;
        long actorId = ((BaseSystemData)actor.data).id;
        long ownFamilyId = TryResolveFamilyStableId(actorId);
        if (ownFamilyId > 0L) return ownFamilyId;

        if (XjFamilyResolver.TryResolveInheritanceParentActorId(actor, out long parentActorId)
            && parentActorId > 0L)
        {
            long parentFamilyId = TryResolveFamilyStableId(parentActorId);
            if (parentFamilyId > 0L) return parentFamilyId;
        }
        return 0L;
    }

    private static bool IsTrackedSongFamily(long familyId)
    {
        if (familyId <= 0L) return false;
        if ((_state?.SongFamilyStableId ?? 0L) == familyId) return true;
        return IsSongFamily(familyId);
    }

    private static bool IsSongFamily(long familyId)
    {
        if (familyId <= 0L) return false;
        string surname = XjFamilySurnameService.ResolveCurrentSurname(familyId);
        return string.Equals(XjFamilySurnameRegistry.NormalizeSurname(surname), Surname, StringComparison.Ordinal);
    }

    private static bool IsBirthYear(Actor actor)
    {
        return actor?.data != null && actor.getAge() <= 1.05f;
    }

    private static int ResolveCurrentYear()
    {
        return Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0));
    }

    private static void MarkChanged()
    {
        XjWorldArchiveSystem.MarkModuleChanged(ModuleId);
    }
}
