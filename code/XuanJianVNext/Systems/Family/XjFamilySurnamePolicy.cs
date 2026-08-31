using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 玄鉴父系家族的姓氏统一策略。
/// 原生氏族/婚姻可能在后代生成时改换姓氏；玄鉴家族身份一经确认，
/// 以 FamilyStableId 对应的持久化规范姓氏为唯一权威，并保留角色原有名字、尊号与境界后缀。
/// 技术性归姓纠错与真正的家族改姓严格分离：前者可修正旧档误姓，后者只改写在世族人与后续新生者。
/// </summary>
internal static class XjFamilySurnamePolicy
{
	private const string ChineseFamilyNameKey = "chinese_family_name";
	private const string ShiReincarnationMode = "ShiReincarnation";
	private static readonly HashSet<long> ReconciledLedgerFamilies = new HashSet<long>();
	private static readonly Dictionary<long, string> CanonicalSurnameByFamily = new Dictionary<long, string>();

	private static readonly string[] CompoundSurnames =
	{
		"欧阳", "太史", "端木", "上官", "司马", "东方", "独孤", "南宫", "万俟", "闻人",
		"夏侯", "诸葛", "尉迟", "公羊", "赫连", "澹台", "皇甫", "宗政", "濮阳", "公冶",
		"太叔", "申屠", "公孙", "慕容", "仲孙", "钟离", "长孙", "宇文", "司徒", "鲜于",
		"司空", "闾丘", "子车", "亓官", "司寇", "巫马", "公西", "颛孙", "壤驷", "公良",
		"漆雕", "乐正", "宰父", "谷梁", "拓跋", "夹谷", "轩辕", "令狐", "段干", "百里",
		"呼延", "东郭", "南门", "羊舌", "微生", "梁丘", "左丘", "东门", "西门", "第五"
	};

	internal static bool EnsureForConfirmedActor(Actor actor)
	{
		if (actor?.data == null
			|| PreserveShiReincarnationIdentity(actor)
			|| XjLongShuSystem.IsExcludedFromInheritance(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L
			|| !XjFamilyMemberIndex.Shared.TryGetRecord(actorId, out XjFamilyIdentity identity)
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L)
		{
			return false;
		}

		string canonicalSurname = ResolveCanonicalSurname(identity.FamilyStableIdValue, actor);
		if (string.IsNullOrWhiteSpace(canonicalSurname))
		{
			return false;
		}

		bool changed = ApplySurname(actor, canonicalSurname);
		if (changed)
		{
			XjFamilyMemberLedger.UpsertConfirmed(actor, identity, "family.surname_reconcile");
		}

		// 每个家族每次载入只扫描一次账本。若家族从未发生正式改姓，可以修复旧档中
		// 已故成员被原生命名污染的误姓；一旦存在真实改姓史，历史姓名即视为史实，
		// 只维护仍在世成员，禁止把改姓前的先祖也洗成新姓。
		if (ReconciledLedgerFamilies.Add(identity.FamilyStableIdValue))
		{
			bool includeHistorical = !XjFamilySurnameRegistry.TryGetState(identity.FamilyStableIdValue, out XjFamilySurnameStateSnapshot surnameState)
				|| !surnameState.HasFormalChange;
			changed |= XjFamilyMemberLedger.ReconcileFamilySurname(identity.FamilyStableIdValue, canonicalSurname, includeHistorical);
		}
		return changed;
	}

	internal static bool EnsureMemberSurname(Actor actor, in XjFamilyIdentity identity)
	{
		if (actor?.data == null
			|| PreserveShiReincarnationIdentity(actor)
			|| !identity.Found
			|| identity.FamilyStableIdValue <= 0L
			|| XjLongShuSystem.IsExcludedFromInheritance(actor)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		string canonicalSurname = ResolveCanonicalSurname(identity.FamilyStableIdValue, actor);
		if (string.IsNullOrWhiteSpace(canonicalSurname))
		{
			return false;
		}

		return ApplySurname(actor, canonicalSurname);
	}

	private static bool PreserveShiReincarnationIdentity(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)) return false;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int applied)
			|| applied <= 0) return false;
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationMode, out string mode);
		return string.Equals(mode, ShiReincarnationMode, StringComparison.Ordinal);
	}

	private static string ResolveCanonicalSurname(long familyStableId, Actor fallbackActor)
	{
		if (familyStableId <= 0L)
		{
			return string.Empty;
		}
		if (CanonicalSurnameByFamily.TryGetValue(familyStableId, out string cachedSurname))
		{
			return cachedSurname;
		}

		// 家族级持久化权威优先于任何角色当前名字。只有正式改姓记录，或明确继承自
		// 已正式改姓的来源主家，才允许保留任意两个汉字的姓氏。普通自动建立阶段
		// 必须按真实单姓/已知复姓解释，避免把“东 + 莘嘉庆”误锁成“东莘”。
		if (XjFamilySurnameRegistry.TryGetCurrentSurname(familyStableId, out string authoritativeSurname))
		{
			string trustedSurname = ResolveTrustedRegistrySurname(familyStableId, authoritativeSurname);
			return CacheCanonicalSurname(familyStableId, trustedSurname);
		}

		// 城市迁徙分家沿用来源主家当时的规范姓氏。分家拥有新的 FamilyStableId，
		// 之后仍可独立正式改姓，不反向影响主家。
		if (TryResolveBranchSourceFamilyId(fallbackActor, familyStableId, out long sourceFamilyId))
		{
			string sourceSurname = ResolveKnownSurname(sourceFamilyId);
			if (!string.IsNullOrWhiteSpace(sourceSurname))
			{
				int year = GetCurrentYear();
				string established = XjFamilySurnameRegistry.EnsureEstablished(familyStableId, sourceSurname, year, sourceFamilyId);
				return CacheCanonicalSurname(familyStableId, established);
			}
		}

		string discovered = ResolveKnownSurname(familyStableId);
		if (!string.IsNullOrWhiteSpace(discovered))
		{
			string established = XjFamilySurnameRegistry.EnsureEstablished(familyStableId, discovered, GetCurrentYear());
			return CacheCanonicalSurname(familyStableId, established);
		}

		// 新家族首次确认时，根角色本身可以建立规范姓氏。非根成员在根角色
		// 尚未可解析时暂缓，避免把婚后姓氏误锁为全族姓氏。
		long fallbackId = fallbackActor?.data == null ? 0L : ((BaseSystemData)fallbackActor.data).id;
		string fallbackSurname = fallbackId == familyStableId ? ReadActorSurnameForInference(fallbackActor) : string.Empty;
		if (string.IsNullOrWhiteSpace(fallbackSurname)) return string.Empty;
		string bootstrapped = XjFamilySurnameRegistry.EnsureEstablished(familyStableId, fallbackSurname, GetCurrentYear());
		return CacheCanonicalSurname(familyStableId, bootstrapped);
	}

	private static string ResolveKnownSurname(long familyStableId)
	{
		if (familyStableId <= 0L) return string.Empty;
		if (XjFamilySurnameRegistry.TryGetCurrentSurname(familyStableId, out string registrySurname))
		{
			return ResolveTrustedRegistrySurname(familyStableId, registrySurname);
		}
		if (CanonicalSurnameByFamily.TryGetValue(familyStableId, out string cached)) return cached;

		Actor rootActor;
		if ((XjFamilyMemberIndex.Shared.TryGetActor(familyStableId, out rootActor)
				|| XjScheduler.ResolveActor(familyStableId, out rootActor))
			&& rootActor?.data != null)
		{
			string rootSurname = ReadActorSurnameForInference(rootActor);
			if (!string.IsNullOrWhiteSpace(rootSurname)) return rootSurname;
		}

		if (XjFamilyMemberLedger.TryGetByActorId(familyStableId, out XjFamilyMemberLedgerEntry rootEntry)
			&& rootEntry.Found)
		{
			string ledgerSurname = ExtractSurname(CleanBaseName(rootEntry.Name));
			if (!string.IsNullOrWhiteSpace(ledgerSurname)) return ledgerSurname;
		}
		return string.Empty;
	}

	private static bool TryResolveBranchSourceFamilyId(Actor actor, long familyStableId, out long sourceFamilyId)
	{
		sourceFamilyId = 0L;
		if (actor?.data != null
			&& XjActorAccessor.TryGetLong(actor, XjActorDataKeys.XjFamilyBranchSourceFamilyId, out long mirroredSource)
			&& mirroredSource > 0L
			&& mirroredSource != familyStableId)
		{
			sourceFamilyId = mirroredSource;
			return true;
		}
		return XjFamilyMemberLedger.TryGetBranchSourceFamilyId(familyStableId, out sourceFamilyId)
			&& sourceFamilyId > 0L
			&& sourceFamilyId != familyStableId;
	}

	private static int GetCurrentYear()
	{
		try { return Math.Max(0, World.world?.map_stats?.year ?? 0); }
		catch { return 0; }
	}

	internal static bool TryGetCanonicalSurname(long familyStableId, out string surname)
	{
		surname = ResolveCanonicalSurname(familyStableId, null);
		return !string.IsNullOrWhiteSpace(surname);
	}

	internal static void SetCanonicalSurname(long familyStableId, string surname)
	{
		string normalized = XjFamilySurnameRegistry.NormalizeSurname(surname);
		if (familyStableId <= 0L || normalized.Length == 0) return;
		CanonicalSurnameByFamily[familyStableId] = normalized;
		ReconciledLedgerFamilies.Add(familyStableId);
	}

	internal static bool ApplyCanonicalSurnameForFamilyChange(Actor actor, string surname)
	{
		return ApplySurname(actor, surname);
	}

	private static string CacheCanonicalSurname(long familyStableId, string surname)
	{
		string normalized = (surname ?? string.Empty).Trim();
		if (familyStableId > 0L && normalized.Length > 0)
		{
			CanonicalSurnameByFamily[familyStableId] = normalized;
		}
		return normalized;
	}

	private static bool ApplySurname(Actor actor, string canonicalSurname)
	{
		string surname = (canonicalSurname ?? string.Empty).Trim();
		if (actor?.data == null
			|| string.IsNullOrWhiteSpace(surname)
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor))
		{
			return false;
		}

		string currentDisplayName = actor.getName() ?? string.Empty;
		string storedBaseName = ResolveStoredBaseName(actor);
		bool useGeneratedNameLayout = !string.IsNullOrWhiteSpace(storedBaseName)
			&& string.Equals(currentDisplayName.Trim(), BuildDisplayName(actor, storedBaseName), StringComparison.Ordinal);
		string baseName = useGeneratedNameLayout
			? storedBaseName
			: CleanBaseName(currentDisplayName);
		if (string.IsNullOrWhiteSpace(baseName))
		{
			baseName = storedBaseName;
		}
		// 旧版姓氏对账会把任意两字姓与固定复姓词典混用，从而反复追加姓氏第二字。
		// 这里只修复明确的历史污染形态：完整姓氏后至少连续重复两次尾字；单次相同保留，
		// 避免误伤“司马马X”一类合法姓名。
		baseName = RepairHistoricalRepeatedSurnameTail(baseName, surname);

		string oldSurname = ReadActorSurname(actor);
		string givenName = baseName;
		bool staleExtendedAutomaticSurname = IsStaleExtendedAutomaticSurname(oldSurname, surname)
			&& baseName.StartsWith(oldSurname, StringComparison.Ordinal);
		if (staleExtendedAutomaticSurname)
		{
			givenName = baseName.Substring(oldSurname.Length);
		}
		else if (baseName.StartsWith(surname, StringComparison.Ordinal))
		{
			givenName = baseName.Substring(surname.Length);
		}
		else if (!string.IsNullOrWhiteSpace(oldSurname) && baseName.StartsWith(oldSurname, StringComparison.Ordinal))
		{
			givenName = baseName.Substring(oldSurname.Length);
		}
		else
		{
			string extracted = ExtractSurname(baseName);
			if (!string.IsNullOrWhiteSpace(extracted) && baseName.StartsWith(extracted, StringComparison.Ordinal))
			{
				givenName = baseName.Substring(extracted.Length);
			}
		}

		string nextBaseName = surname + (givenName ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(nextBaseName))
		{
			nextBaseName = surname;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		data.get(ChineseFamilyNameKey, out string storedChineseSurname, string.Empty);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFamilySurname, out string storedFamilySurname);
		string nextDisplayName = useGeneratedNameLayout
			? BuildDisplayName(actor, nextBaseName)
			: RewriteDisplaySurname(currentDisplayName, surname, oldSurname);
		if (string.IsNullOrWhiteSpace(nextDisplayName))
		{
			nextDisplayName = nextBaseName;
		}
		bool changed = !string.Equals((storedChineseSurname ?? string.Empty).Trim(), surname, StringComparison.Ordinal)
			|| !string.Equals((storedFamilySurname ?? string.Empty).Trim(), surname, StringComparison.Ordinal)
			|| !string.Equals(baseName, nextBaseName, StringComparison.Ordinal)
			|| !string.Equals(currentDisplayName, nextDisplayName, StringComparison.Ordinal);
		if (!changed)
		{
			return false;
		}

		XjActorStateWriteGateway.SetExternalString(actor, ChineseFamilyNameKey, surname, XjActorStateDomain.Family | XjActorStateDomain.Identity);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFamilySurname, surname);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, nextBaseName);
		XjActorStateWriteGateway.SetDisplayName(actor, nextDisplayName, customName: true);
		return true;
	}

	private static string BuildDisplayName(Actor actor, string baseName)
	{
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameTitle, out string title);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameRealmDisplay, out string realmDisplay);
		title = (title ?? string.Empty).Trim();
		realmDisplay = (realmDisplay ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(realmDisplay))
		{
			return title + "·" + baseName + "-" + realmDisplay;
		}
		if (!string.IsNullOrWhiteSpace(title))
		{
			return title + "·" + baseName;
		}
		if (!string.IsNullOrWhiteSpace(realmDisplay))
		{
			return baseName + "-" + realmDisplay;
		}
		return baseName;
	}

	internal static string RewriteDisplaySurname(string displayName, string canonicalSurname)
	{
		return RewriteDisplaySurname(displayName, canonicalSurname, string.Empty);
	}

	internal static string RewriteDisplaySurname(string displayName, string canonicalSurname, string knownOldSurname)
	{
		string text = (displayName ?? string.Empty).Trim();
		string surname = (canonicalSurname ?? string.Empty).Trim();
		if (text.Length == 0 || surname.Length == 0)
		{
			return text;
		}

		int dotIndex = text.IndexOf('·');
		string prefix = dotIndex >= 0 ? text.Substring(0, dotIndex + 1) : string.Empty;
		string body = dotIndex >= 0 && dotIndex + 1 < text.Length ? text.Substring(dotIndex + 1) : text;
		int realmIndex = body.LastIndexOf('-');
		string suffix = realmIndex > 0 ? body.Substring(realmIndex) : string.Empty;
		string baseName = realmIndex > 0 ? body.Substring(0, realmIndex) : body;

		string oldSurname = (knownOldSurname ?? string.Empty).Trim();
		if (IsStaleExtendedAutomaticSurname(oldSurname, surname)
			&& baseName.StartsWith(oldSurname, StringComparison.Ordinal))
		{
			return prefix + surname + baseName.Substring(oldSurname.Length) + suffix;
		}

		if (baseName.StartsWith(surname, StringComparison.Ordinal))
		{
			string repaired = RepairHistoricalRepeatedSurnameTail(baseName, surname);
			return prefix + repaired + suffix;
		}

		if (oldSurname.Length == 0 || !baseName.StartsWith(oldSurname, StringComparison.Ordinal))
		{
			oldSurname = ExtractSurname(baseName);
		}
		string givenName = !string.IsNullOrWhiteSpace(oldSurname) && baseName.StartsWith(oldSurname, StringComparison.Ordinal)
			? baseName.Substring(oldSurname.Length)
			: baseName;
		return prefix + surname + (givenName ?? string.Empty).Trim() + suffix;
	}

	private static string RepairHistoricalRepeatedSurnameTail(string baseName, string canonicalSurname)
	{
		string text = CleanBaseName(baseName);
		string surname = (canonicalSurname ?? string.Empty).Trim();
		if (surname.Length != 2 || text.Length <= surname.Length || !text.StartsWith(surname, StringComparison.Ordinal))
		{
			return text;
		}

		char repeated = surname[surname.Length - 1];
		int index = surname.Length;
		int duplicateCount = 0;
		while (index < text.Length && text[index] == repeated)
		{
			duplicateCount++;
			index++;
		}
		// One matching first character of a given name is ambiguous and may be
		// legitimate (e.g. 司马马X). Two or more consecutive copies are the exact
		// corruption shape produced by the old reconciliation loop.
		if (duplicateCount < 2)
		{
			return text;
		}
		return surname + text.Substring(index);
	}

	private static string ResolveTrustedRegistrySurname(long familyStableId, string registrySurname)
	{
		string raw = (registrySurname ?? string.Empty).Trim();
		if (raw.Length == 0) return string.Empty;

		if (XjFamilySurnameRegistry.TryGetState(familyStableId, out XjFamilySurnameStateSnapshot state)
			&& state.Found)
		{
			// 玩家正式改姓是最高权威，允许一至两个任意汉字。
			if (state.HasFormalChange) return raw;

			// 分家如果直接继承了一个已经正式改姓的主家，也必须保留该复姓。
			if (state.SourceFamilyId > 0L
				&& XjFamilySurnameRegistry.TryGetState(state.SourceFamilyId, out XjFamilySurnameStateSnapshot sourceState)
				&& sourceState.Found
				&& sourceState.HasFormalChange
				&& string.Equals(sourceState.CurrentSurname, raw, StringComparison.Ordinal))
			{
				return raw;
			}
		}

		string inferred = NormalizeInferredSurname(raw);
		if (!string.Equals(inferred, raw, StringComparison.Ordinal) && inferred.Length > 0)
		{
			XjFamilySurnameRegistry.TryRepairAutomaticSurname(familyStableId, inferred);
		}
		return inferred;
	}

	private static string ReadActorSurnameForInference(Actor actor)
	{
		return NormalizeInferredSurname(ReadActorSurname(actor));
	}

	internal static string NormalizeInferredSurname(string value)
	{
		string text = XjFamilySurnameRegistry.NormalizeSurname(value);
		if (text.Length <= 1) return text;
		for (int i = 0; i < CompoundSurnames.Length; i++)
		{
			string compound = CompoundSurnames[i];
			if (text.StartsWith(compound, StringComparison.Ordinal)) return compound;
		}
		// 自动推断只允许“单姓”或词表内真实复姓。任意两个汉字只能来自
		// 玩家正式改姓记录，不能从角色姓名前两个字猜出来。
		return text.Substring(0, 1);
	}

	private static bool IsStaleExtendedAutomaticSurname(string oldSurname, string canonicalSurname)
	{
		string oldValue = (oldSurname ?? string.Empty).Trim();
		string canonical = (canonicalSurname ?? string.Empty).Trim();
		if (canonical.Length != 1 || oldValue.Length != 2 || !oldValue.StartsWith(canonical, StringComparison.Ordinal))
		{
			return false;
		}
		for (int i = 0; i < CompoundSurnames.Length; i++)
		{
			if (string.Equals(oldValue, CompoundSurnames[i], StringComparison.Ordinal)) return false;
		}
		return true;
	}

	internal static void ClearRuntimeCache()
	{
		ReconciledLedgerFamilies.Clear();
		CanonicalSurnameByFamily.Clear();
	}

	private static string ReadPersistedFamilySurname(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFamilySurname, out string surname)
			&& !string.IsNullOrWhiteSpace(surname))
		{
			return surname.Trim();
		}
		return string.Empty;
	}

	private static string ReadActorSurname(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		string persisted = ReadPersistedFamilySurname(actor);
		if (!string.IsNullOrWhiteSpace(persisted))
		{
			return persisted;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		data.get(ChineseFamilyNameKey, out string storedSurname, string.Empty);
		storedSurname = (storedSurname ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(storedSurname))
		{
			return storedSurname;
		}

		return ExtractSurname(ResolveBaseName(actor));
	}

	private static string ResolveBaseName(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string storedBaseName)
			&& !string.IsNullOrWhiteSpace(storedBaseName))
		{
			return CleanBaseName(storedBaseName);
		}
		return CleanBaseName(actor.getName());
	}

	private static string CleanBaseName(string value)
	{
		string text = (value ?? string.Empty).Trim();
		int separator = text.IndexOf('·');
		if (separator >= 0 && separator + 1 < text.Length)
		{
			text = text.Substring(separator + 1).Trim();
		}
		int realmSeparator = text.LastIndexOf('-');
		if (realmSeparator > 0)
		{
			text = text.Substring(0, realmSeparator).Trim();
		}
		return text;
	}

	private static string ExtractSurname(string baseName)
	{
		string text = CleanBaseName(baseName);
		if (string.IsNullOrWhiteSpace(text))
		{
			return string.Empty;
		}
		for (int i = 0; i < CompoundSurnames.Length; i++)
		{
			if (text.StartsWith(CompoundSurnames[i], StringComparison.Ordinal))
			{
				return CompoundSurnames[i];
			}
		}
		return text.Substring(0, 1);
	}

	private static string ResolveStoredBaseName(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjNameBase, out string storedBaseName)
			&& !string.IsNullOrWhiteSpace(storedBaseName)
			? CleanBaseName(storedBaseName)
			: string.Empty;
	}
}
