using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.Family;

/// <summary>
/// 玄鉴父系家族的姓氏统一策略。
/// 原生氏族/婚姻可能在后代生成时改换姓氏；玄鉴家族身份一经确认，
/// 以家族根角色的姓氏为唯一姓氏，并保留角色原有名字、尊号与境界后缀。
/// </summary>
internal static class XjFamilySurnamePolicy
{
	private const string ChineseFamilyNameKey = "chinese_family_name";
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

		// 每个家族每次载入只扫描一次历史账本，连已故成员的谱系显示名也统一。
		if (ReconciledLedgerFamilies.Add(identity.FamilyStableIdValue))
		{
			changed |= XjFamilyMemberLedger.ReconcileFamilySurname(identity.FamilyStableIdValue, canonicalSurname);
		}
		return changed;
	}

	internal static bool EnsureMemberSurname(Actor actor, in XjFamilyIdentity identity)
	{
		if (actor?.data == null
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

		Actor rootActor;
		if ((XjFamilyMemberIndex.Shared.TryGetActor(familyStableId, out rootActor)
				|| XjScheduler.ResolveActor(familyStableId, out rootActor))
			&& rootActor?.data != null)
		{
			string rootSurname = ReadActorSurname(rootActor);
			if (!string.IsNullOrWhiteSpace(rootSurname))
			{
				return CacheCanonicalSurname(familyStableId, rootSurname);
			}
		}

		if (XjFamilyMemberLedger.TryGetByActorId(familyStableId, out XjFamilyMemberLedgerEntry rootEntry)
			&& rootEntry.Found)
		{
			string ledgerSurname = ExtractSurname(CleanBaseName(rootEntry.Name));
			if (!string.IsNullOrWhiteSpace(ledgerSurname))
			{
				return CacheCanonicalSurname(familyStableId, ledgerSurname);
			}
		}

		// 新家族首次确认时，根角色本身可以建立规范姓氏。非根成员在根角色
		// 尚未可解析时暂缓，避免把婚后姓氏误锁为全族姓氏。
		long fallbackId = fallbackActor?.data == null ? 0L : ((BaseSystemData)fallbackActor.data).id;
		string fallbackSurname = fallbackId == familyStableId ? ReadActorSurname(fallbackActor) : string.Empty;
		return string.IsNullOrWhiteSpace(fallbackSurname)
			? string.Empty
			: CacheCanonicalSurname(familyStableId, fallbackSurname);
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

		string baseName = ResolveBaseName(actor);
		string oldSurname = ReadActorSurname(actor);
		string givenName = baseName;
		if (!string.IsNullOrWhiteSpace(oldSurname) && baseName.StartsWith(oldSurname, StringComparison.Ordinal))
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
		string currentDisplayName = actor.getName() ?? string.Empty;
		string nextDisplayName = BuildDisplayName(actor, nextBaseName);
		bool changed = !string.Equals((storedChineseSurname ?? string.Empty).Trim(), surname, StringComparison.Ordinal)
			|| !string.Equals((storedFamilySurname ?? string.Empty).Trim(), surname, StringComparison.Ordinal)
			|| !string.Equals(baseName, nextBaseName, StringComparison.Ordinal)
			|| !string.Equals(currentDisplayName, nextDisplayName, StringComparison.Ordinal);
		if (!changed)
		{
			return false;
		}

		data.set(ChineseFamilyNameKey, surname);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjFamilySurname, surname);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, nextBaseName);
		actor.name = nextDisplayName;
		actor.data.custom_name = true;
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
		string oldSurname = ExtractSurname(baseName);
		string givenName = !string.IsNullOrWhiteSpace(oldSurname) && baseName.StartsWith(oldSurname, StringComparison.Ordinal)
			? baseName.Substring(oldSurname.Length)
			: baseName;
		return prefix + surname + (givenName ?? string.Empty).Trim() + suffix;
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
}
