using System;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.GongFa;

namespace XuanJianVNext.Systems.Family;

/// <summary>世界一次性的张衍彩蛋；沿原生出生回调判定，不增加人口扫描。</summary>
internal static class XjZhangYanEasterEggSystem
{
	internal const string ModuleId = "family.zhang-yan-easter-egg";
	internal const string Name = "张衍";
	internal const string DaoTu = "并火";
	internal const string DaoHao = "景寂";
	private const string Surname = "张";
	private const int ChancePerTenThousand = 3000;
	private static bool _attempted;
	private static bool _born;
	private static long _actorId;

	internal static bool IsZhangYan(Actor actor)
	{
		if (actor?.data == null) return false;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZhangYan, out int marked) && marked > 0) return true;
		return _born && ((BaseSystemData)actor.data).id == _actorId;
	}

	internal static void ObserveNativeBirth(Actor child, Actor parent1, Actor parent2)
	{
		if (_attempted || child?.data == null || !child.isAlive()) return;
		long familyId = TryResolveBirthFamilyStableId(child, parent1, parent2);
		if (familyId <= 0L
			|| !string.Equals(XjFamilySurnameRegistry.NormalizeSurname(XjFamilySurnameService.ResolveCurrentSurname(familyId)), Surname, StringComparison.Ordinal)) return;
		long actorId = ((BaseSystemData)child.data).id;
		if (actorId <= 0L) return;
		_attempted = true;
		MarkChanged();
		int year = Math.Max(1, World.world?.map_stats?.year ?? 1);
		if (XjDeterministicHash.PositiveIndex(actorId + year * 19L, "zhang_yan.world_once", 10000) >= ChancePerTenThousand) return;
		_born = true;
		_actorId = actorId;
		XjActorAccessor.SetInt(child, XjActorDataKeys.XjZhangYan, 1);
		XjActorAccessor.SetString(child, XjActorDataKeys.XjNameBase, Name);
		XjActorStateWriteGateway.SetDisplayName(child, Name, customName: true);
		child.data.sex = ActorSex.Male;
		EnsureIdentityInvariant(child, false);
		MarkChanged();
	}

	internal static int ResolveAptitude(Actor actor)
	{
		long id = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		return 3 + XjDeterministicHash.PositiveIndex(id, "zhang_yan.aptitude", 2);
	}

	internal static void EnsureAgeFiveDestiny(Actor actor)
	{
		if (!IsZhangYan(actor)) return;
		EnsureIdentityInvariant(actor, false);
		XjGongFaProgression.EnsureEntryGongFa(actor, XjActorCultivationSnapshotBuilder.Build(actor));
		XjVisibleTraitSync.SyncCultivationTraits(actor);
	}

	internal static void EnsureIdentityInvariant(Actor actor, bool syncVisibleTraits)
	{
		if (actor?.data == null || !IsZhangYan(actor)) return;
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, ResolveAptitude(actor));
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		XjCultivationPathTransitions.TryEnsureZiFuJinDan(actor, syncVisibleTraits);
		XjCultivationStateTransitions.RestoreDaoTuMetadataOnly(actor, DaoTu, syncVisibleTraits);
	}

	internal static string EnsureJingJiTitle(Actor actor, string title)
	{
		if (!IsZhangYan(actor)) return title ?? string.Empty;
		string value = (title ?? string.Empty).Trim();
		if (TryRebuildJunTitle(value, DaoHao, out string rebuilt)) return rebuilt;
		return value.Contains(DaoHao, StringComparison.Ordinal) ? value : DaoHao + value;
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

	internal static string ExportPayload() => (_attempted ? "1" : "0") + "|" + (_born ? "1" : "0") + "|" + _actorId.ToString(CultureInfo.InvariantCulture);
	internal static void ImportPayload(int schemaVersion, string payload)
	{
		_attempted = false; _born = false; _actorId = 0L;
		string[] parts = (payload ?? string.Empty).Split('|');
		if (parts.Length < 3) return;
		_attempted = parts[0] == "1"; _born = parts[1] == "1";
		if (long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)) _actorId = Math.Max(0L, value);
		if (_actorId <= 0L) _born = false;
	}
	internal static void ClearRuntime() { _attempted = false; _born = false; _actorId = 0L; }
	private static void MarkChanged() => XjWorldArchiveSystem.MarkModuleChanged(ModuleId);

	private static long TryResolveBirthFamilyStableId(Actor child, Actor parent1, Actor parent2)
	{
		Actor father = parent1?.data != null && parent1.data.sex == ActorSex.Male ? parent1
			: parent2?.data != null && parent2.data.sex == ActorSex.Male ? parent2 : null;
		if (father?.data != null)
		{
			long fatherId = ((BaseSystemData)father.data).id;
			if (XjFamilyMemberLedger.TryGetByActorId(fatherId, out XjFamilyMemberLedgerEntry record) && record.Found && record.FamilyStableId > 0L)
				return record.FamilyStableId;
		}
		if (XjFamilyResolver.TryResolveInheritanceParentActorId(child, out long parentId)
			&& XjFamilyMemberLedger.TryGetByActorId(parentId, out XjFamilyMemberLedgerEntry inherited)
			&& inherited.Found) return inherited.FamilyStableId;
		return 0L;
	}
}
