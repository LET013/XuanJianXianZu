using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Shi;

/// <summary>
/// 释修尊号与法号的唯一投影入口。保留原著已出现称号，同时按佛教、道释合流用语
/// 组合大规模词库。尊号采用运行时唯一占用表；旧档若撞号，后载入者会稳定改取下一候选。
/// </summary>
internal static class XjShiTitleSystem
{
	private readonly struct MoHeTitle
	{
		internal readonly string Honorific;
		internal readonly string DharmaName;
		internal MoHeTitle(string honorific, string dharmaName)
		{
			Honorific = honorific;
			DharmaName = dharmaName;
		}
	}

	private static readonly Dictionary<string, long> TitleOwners = new Dictionary<string, long>(StringComparer.Ordinal);
	private static readonly Dictionary<string, long> DharmaNameOwners = new Dictionary<string, long>(StringComparer.Ordinal);

	private static readonly MoHeTitle[] CanonicalMoHeTitles =
	{
		new MoHeTitle("横舆车蝉摩柯", "法蝉"),
		new MoHeTitle("脱胎见日摩柯", "司擭"),
		new MoHeTitle("熙光攫镜摩柯", "烛熙")
	};

	private static readonly string[] CanonicalDharmaFormTitles =
	{
		"慈悲六道观世相", "天思慈悲广教相", "轮法慈悲道钟相", "大悲善乐莲世相",
		"大悲善乐良玄相", "出并入圣宝尊相", "广土道肴炼狱相", "缘空性起六情相",
		"怒目四魔帝刹显相", "金躯雷音无漏法相", "六焚丹尸无漏法相", "右座玄机有闻法相"
	};

	private static readonly string[] CanonicalWorldHonoredTitles =
	{
		"十方尽明王", "无上正觉明王", "大千遍照明王", "三世圆觉明王",
		"无量法海明王", "无边慧日明王", "无央应化明王", "无等宝尊明王"
	};

	private static readonly string[] MoHeOpening =
	{
		"横舆", "脱胎", "见日", "熙光", "攫镜", "十方", "六道", "三世", "大悲", "慈悲",
		"善乐", "寂照", "金刚", "雷音", "梵音", "明觉", "圆照", "妙严", "宝幢", "天龙",
		"海印", "清净", "琉璃", "菩提", "摩尼", "莲华", "慈航", "慧日", "法轮", "宝树",
		"无量", "无边", "无央", "无等", "不动", "降魔", "护世", "净业", "持戒", "禅定",
		"般若", "三昧", "觉海", "法界", "玄灯", "宝藏", "灵山", "雪域", "香海", "光明"
	};

	private static readonly string[] MoHeClosing =
	{
		"车蝉", "见日", "攫镜", "持灯", "照夜", "开莲", "转轮", "度世", "观心", "渡海",
		"伏魔", "守戒", "明法", "归寂", "宝藏", "莲台", "梵钟", "金身", "法界", "慧炬",
		"妙音", "无漏", "净土", "慈航", "六通", "十地", "三昧", "明王", "降龙", "伏虎",
		"宝筏", "慧舟", "法鼓", "天眼", "神足", "圆觉", "寂灭", "有闻", "无碍", "应真",
		"妙相", "尊胜", "广教", "性起", "观世", "莲世", "玄门", "帝刹", "净行", "觉路"
	};

	private static readonly string[] DharmaHead =
	{
		"慈悲", "大悲", "天思", "轮法", "善乐", "广土", "缘空", "怒目", "金躯", "六焚",
		"右座", "无量", "无边", "无央", "无等", "十方", "三世", "六道", "宝光", "金刚",
		"琉璃", "莲华", "摩尼", "菩提", "清净", "寂照", "妙觉", "圆通", "梵音", "雷音",
		"法界", "海印", "慈航", "慧日", "宝幢", "净业", "不动", "明王", "降魔", "护法",
		"天龙", "夜叉", "香海", "雪山", "灵鹫", "般若", "禅那", "三昧", "正觉", "应真",
		"宝树", "法鼓", "慧炬", "净莲", "无漏", "有闻", "玄机", "六情", "四魔", "帝刹"
	};

	private static readonly string[] DharmaMiddle =
	{
		"六道", "观世", "广教", "慈悲", "善乐", "入圣", "道肴", "性起", "四魔", "雷音",
		"丹尸", "玄机", "无漏", "有闻", "莲世", "良玄", "宝尊", "炼狱", "六情", "帝刹",
		"十方", "三界", "诸天", "妙法", "法轮", "宝藏", "梵钟", "金莲", "净土", "慧海",
		"慈航", "圆觉", "寂灭", "明心", "渡世", "持戒", "禅定", "般若", "三昧", "应化",
		"庄严", "光明", "无碍", "尊胜", "宝筏", "慧炬", "法鼓", "天眼", "神足", "净业"
	};

	private static readonly string[] DharmaTail =
	{
		"观世", "广教", "道钟", "莲世", "良玄", "宝尊", "炼狱", "六情", "帝刹", "无漏",
		"有闻", "法轮", "明王", "慧海", "金莲", "宝幢", "慈航", "圆觉", "寂照", "梵音",
		"雷音", "妙严", "净业", "三昧", "十地", "六通", "降魔", "护法", "渡世", "宝藏",
		"慧炬", "法鼓", "无碍", "尊胜", "应真", "性海", "香界", "觉城", "宝树", "天门"
	};

	private static readonly string[] DharmaSuffix = { "相", "法相", "显相" };
	private static readonly string[] MoHeSuffix = { "摩柯", "摩诃" };
	// 25 × 20 = 500 个不重复的常用释门法号组合。先使用二字法号；
	// 只有同一世界中在世释修超过五百时，才继续生成三字扩展法号。
	private static readonly string[] DharmaNameHeads =
	{
		"法", "慧", "觉", "明", "净", "空", "圆", "妙", "真", "善",
		"慈", "普", "德", "智", "悟", "道", "戒", "定", "寂", "宏",
		"绍", "传", "性", "印", "源"
	};
	private static readonly string[] DharmaNameTails =
	{
		"安", "远", "海", "云", "光", "照", "心", "行", "严", "恩",
		"济", "成", "观", "航", "莲", "音", "藏", "朗", "诚", "修"
	};
	private static readonly string[] DharmaNameCatalog = BuildDharmaNameCatalog();

	private static readonly string[] CompassionHeads = { "慈悲", "大悲", "慈航", "莲华", "观世", "慧海" };
	private static readonly string[] GoodJoyHeads = { "善乐", "妙乐", "莲华", "宝光", "圆觉", "明心" };
	private static readonly string[] WrathHeads = { "怒目", "不动", "降魔", "金刚", "明王", "帝刹" };
	private static readonly string[] EmptinessHeads = { "缘空", "空无", "寂照", "清净", "无漏", "般若" };
	private static readonly string[] DisciplineHeads = { "持戒", "净业", "无漏", "金躯", "清净", "宝幢" };
	private static readonly string[] DharmaAdmirationHeads = { "轮法", "广土", "法界", "梵音", "雷音", "有闻" };
	private static readonly string[] GreatDesireHeads = { "无边", "宝尊", "广土", "摩尼", "天龙", "六情" };

	internal static void ClearCache()
	{
		TitleOwners.Clear();
		DharmaNameOwners.Clear();
	}

	internal static void EnsureForActor(Actor actor)
	{
		if (actor?.data == null || !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)) return;
		long actorId = ((BaseSystemData)actor.data).id;
		string inheritedDharmaName = Read(actor, XjActorDataKeys.ShiDharmaName).Trim();
		int rank = XjShiCatalog.GetRank(snapshot.Realm);
		if (rank < XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
		{
			string lowRealmDharmaName = ResolveUniqueDharmaName(actorId, inheritedDharmaName, string.Empty);
			ReleasePreviousDharmaName(actorId, inheritedDharmaName, lowRealmDharmaName);
			ClearShiProjection(actor, snapshot.Realm, lowRealmDharmaName);
			return;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiLineageId, out string lineageId);
		string inherited = Read(actor, XjActorDataKeys.ShiHonorificTitle).Trim();
		string title;
		if (string.Equals(snapshot.Realm, XjShiRealmIds.WorldHonored, StringComparison.Ordinal))
			title = ResolveUniqueTitle(actorId, inherited, "world", lineageId);
		else if (string.Equals(snapshot.Realm, XjShiRealmIds.DharmaForm, StringComparison.Ordinal))
			title = ResolveUniqueTitle(actorId, inherited, "dharma_form", lineageId);
		else
			title = ResolveUniqueTitle(actorId, inherited, "mohe", lineageId);

		if (!string.IsNullOrWhiteSpace(inherited)
			&& !string.Equals(inherited, title, StringComparison.Ordinal)
			&& TitleOwners.TryGetValue(inherited, out long inheritedOwner)
			&& inheritedOwner == actorId)
		{
			TitleOwners.Remove(inherited);
		}

		string canonicalDharmaName = ResolveCanonicalDharmaName(title);
		string dharmaName = ResolveUniqueDharmaName(actorId, inheritedDharmaName, canonicalDharmaName);
		ReleasePreviousDharmaName(actorId, inheritedDharmaName, dharmaName);
		string baseName = ResolveBaseName(actor);
		string realmDisplay = string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal)
			? XjShiCatalog.GetMoHeStageDisplay(Math.Clamp(Math.Max(snapshot.CurrentLife, snapshot.CompletedLives + 1), 1, 9))
			: XjShiCatalog.GetRealmDisplay(snapshot.Realm);
		string fullName = string.IsNullOrWhiteSpace(title)
			? baseName + "-" + realmDisplay
			: title + "·" + baseName + "-" + realmDisplay;

		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiHonorificTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaName, dharmaName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, title);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realmDisplay);
		if (!string.Equals(actor.getName()?.Trim(), fullName, StringComparison.Ordinal))
			XjActorStateWriteGateway.SetDisplayName(actor, fullName, customName: true);
	}

	private static string ResolveUniqueTitle(long actorId, string inherited, string kind, string lineageId)
	{
		if (IsCompatibleTitle(inherited, kind) && TryReserve(inherited, actorId)) return inherited;
		for (int attempt = 0; attempt < 32768; attempt++)
		{
			string candidate = GenerateTitle(actorId, kind, lineageId, attempt);
			if (TryReserve(candidate, actorId)) return candidate;
		}
		// 极端高人口碰撞时继续加入纯释修法号片段，仍必须先成功占用后返回。
		for (int attempt = 32768; ; attempt++)
		{
			string candidate = GenerateDharmaName(actorId + attempt * 8191L)
				+ GenerateTitle(actorId, kind, lineageId, attempt);
			if (TryReserve(candidate, actorId)) return candidate;
		}
	}

	private static bool TryReserve(string title, long actorId)
	{
		if (string.IsNullOrWhiteSpace(title) || actorId <= 0L) return false;
		if (TitleOwners.TryGetValue(title, out long owner))
		{
			if (owner == actorId) return true;
			// 摩诃等待下一世时只是暂时没有肉身，仍是同一人物。此期间尊号继续
			// 由前世真灵占用，不能因为旧肉身死亡就被其他释修抢走。
			if (XjReincarnation.HasPendingShi(owner)) return false;
			// 非待转世的旧持有者已经死亡或移除时，才允许称号重新流转。
			if (!XjActorRegistry.ResolveKnownOrWorld(owner, out Actor prior)
				|| prior?.data == null || !prior.isAlive())
			{
				TitleOwners[title] = actorId;
				return true;
			}
			return false;
		}
		if (XjReincarnation.IsPendingShiHonorificReserved(title)) return false;
		TitleOwners[title] = actorId;
		return true;
	}

	private static string GenerateTitle(long actorId, string kind, string lineageId, int attempt)
	{
		long seed = actorId + attempt * 104729L;
		if (string.Equals(kind, "world", StringComparison.Ordinal))
		{
			if (attempt < CanonicalWorldHonoredTitles.Length)
				return CanonicalWorldHonoredTitles[XjDeterministicHash.PositiveIndex(seed, "shi_world_canonical", CanonicalWorldHonoredTitles.Length)];
			string head = Pick(DharmaHead, seed, "shi_world_head");
			if (string.Equals(head, "明王", StringComparison.Ordinal))
				head = DharmaHead[(Array.IndexOf(DharmaHead, head) + 13) % DharmaHead.Length];
			string middle = Pick(DharmaMiddle, seed + 17, "shi_world_middle");
			return head + middle + "明王";
		}
		if (string.Equals(kind, "dharma_form", StringComparison.Ordinal))
		{
			if (attempt < CanonicalDharmaFormTitles.Length)
				return CanonicalDharmaFormTitles[XjDeterministicHash.PositiveIndex(seed,
					"shi_dharma_canonical|" + (lineageId ?? string.Empty), CanonicalDharmaFormTitles.Length)];
			string head = ResolveLineageHead(lineageId, seed);
			string middle = Pick(DharmaMiddle, seed + 31, "shi_dharma_middle");
			if (string.Equals(head, middle, StringComparison.Ordinal))
				middle = DharmaMiddle[(Array.IndexOf(DharmaMiddle, middle) + 9) % DharmaMiddle.Length];
			string tail = Pick(DharmaTail, seed + 67, "shi_dharma_tail");
			if (string.Equals(middle, tail, StringComparison.Ordinal))
				tail = DharmaTail[(Array.IndexOf(DharmaTail, tail) + 7) % DharmaTail.Length];
			return head + middle + tail + Pick(DharmaSuffix, seed + 97, "shi_dharma_suffix");
		}

		if (attempt < CanonicalMoHeTitles.Length)
			return CanonicalMoHeTitles[XjDeterministicHash.PositiveIndex(seed, "shi_mohe_canonical", CanonicalMoHeTitles.Length)].Honorific;
		string opening = Pick(MoHeOpening, seed, "shi_mohe_opening");
		string closing = Pick(MoHeClosing, seed + 43, "shi_mohe_closing");
		if (string.Equals(opening, closing, StringComparison.Ordinal))
			closing = MoHeClosing[(Array.IndexOf(MoHeClosing, closing) + 11) % MoHeClosing.Length];
		return opening + closing + Pick(MoHeSuffix, seed + 89, "shi_mohe_suffix");
	}

	private static string ResolveLineageHead(string lineageId, long seed)
	{
		string[] focused;
		if (string.Equals(lineageId, XjShiLineageIds.Compassion, StringComparison.Ordinal)) focused = CompassionHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.GoodJoy, StringComparison.Ordinal)) focused = GoodJoyHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.Wrath, StringComparison.Ordinal)) focused = WrathHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.Emptiness, StringComparison.Ordinal)) focused = EmptinessHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.Discipline, StringComparison.Ordinal)) focused = DisciplineHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.DharmaAdmiration, StringComparison.Ordinal)) focused = DharmaAdmirationHeads;
		else if (string.Equals(lineageId, XjShiLineageIds.GreatDesire, StringComparison.Ordinal)) focused = GreatDesireHeads;
		else focused = DharmaHead;
		return Pick(focused, seed, "shi_dharma_lineage_head|" + (lineageId ?? string.Empty));
	}

	private static bool IsCompatibleTitle(string value, string kind)
	{
		if (string.IsNullOrWhiteSpace(value)) return false;
		if (string.Equals(kind, "world", StringComparison.Ordinal)) return value.EndsWith("明王", StringComparison.Ordinal);
		if (string.Equals(kind, "dharma_form", StringComparison.Ordinal))
			return value.EndsWith("相", StringComparison.Ordinal);
		return value.EndsWith("摩柯", StringComparison.Ordinal) || value.EndsWith("摩诃", StringComparison.Ordinal);
	}

	private static string ResolveCanonicalDharmaName(string title)
	{
		if (string.IsNullOrWhiteSpace(title)) return string.Empty;
		for (int i = 0; i < CanonicalMoHeTitles.Length; i++)
		{
			if (string.Equals(CanonicalMoHeTitles[i].Honorific, title, StringComparison.Ordinal))
				return CanonicalMoHeTitles[i].DharmaName;
		}
		return string.Empty;
	}

	private static string[] BuildDharmaNameCatalog()
	{
		string[] result = new string[DharmaNameHeads.Length * DharmaNameTails.Length];
		int index = 0;
		for (int i = 0; i < DharmaNameHeads.Length; i++)
		{
			for (int j = 0; j < DharmaNameTails.Length; j++)
			{
				result[index++] = DharmaNameHeads[i] + DharmaNameTails[j];
			}
		}
		return result;
	}

	private static string ResolveUniqueDharmaName(long actorId, string inherited, string canonical)
	{
		string preferred = string.IsNullOrWhiteSpace(canonical) ? inherited?.Trim() ?? string.Empty : canonical.Trim();
		if (TryReserveDharmaName(preferred, actorId)) return preferred;

		int start = XjDeterministicHash.PositiveIndex(actorId, "shi_dharma_name_catalog", DharmaNameCatalog.Length);
		for (int attempt = 0; attempt < DharmaNameCatalog.Length; attempt++)
		{
			string candidate = DharmaNameCatalog[(start + attempt) % DharmaNameCatalog.Length];
			if (TryReserveDharmaName(candidate, actorId)) return candidate;
		}

		// 极端情况下扩展为三字法号，仍保持纯释门字词且在世唯一。
		int extensionCapacity = DharmaNameCatalog.Length * DharmaNameTails.Length;
		for (int attempt = 0; attempt < extensionCapacity; attempt++)
		{
			int baseIndex = (start + attempt) % DharmaNameCatalog.Length;
			int tailIndex = (attempt / DharmaNameCatalog.Length
				+ XjDeterministicHash.PositiveIndex(actorId, "shi_dharma_name_extension", DharmaNameTails.Length))
				% DharmaNameTails.Length;
			string candidate = DharmaNameCatalog[baseIndex] + DharmaNameTails[tailIndex];
			if (TryReserveDharmaName(candidate, actorId)) return candidate;
		}

		// 世界中同时在世的释修超过一万时才会落到这里；保留可读前缀并以角色ID兜底。
		string fallback = DharmaNameCatalog[start] + Math.Abs(actorId).ToString();
		DharmaNameOwners[fallback] = actorId;
		return fallback;
	}

	private static void ReleasePreviousDharmaName(long actorId, string previous, string current)
	{
		if (string.IsNullOrWhiteSpace(previous) || string.Equals(previous, current, StringComparison.Ordinal)) return;
		if (DharmaNameOwners.TryGetValue(previous, out long owner) && owner == actorId)
			DharmaNameOwners.Remove(previous);
	}

	private static bool TryReserveDharmaName(string dharmaName, long actorId)
	{
		if (string.IsNullOrWhiteSpace(dharmaName) || actorId <= 0L) return false;
		if (DharmaNameOwners.TryGetValue(dharmaName, out long owner))
		{
			if (owner == actorId) return true;
			if (XjReincarnation.HasPendingShi(owner)) return false;
			if (!XjActorRegistry.ResolveKnownOrWorld(owner, out Actor prior)
				|| prior?.data == null || !prior.isAlive())
			{
				DharmaNameOwners[dharmaName] = actorId;
				return true;
			}
			return false;
		}
		if (XjReincarnation.IsPendingShiDharmaNameReserved(dharmaName)) return false;
		DharmaNameOwners[dharmaName] = actorId;
		return true;
	}

	/// <summary>
	/// 摩诃转世换的是肉身ID，不换人物身份。将前世占用的尊号/法号登记
	/// 原子迁到新肉身，避免恢复阶段被唯一性表误判为“另一个人”。
	/// </summary>
	internal static void TransferReincarnationIdentity(Actor actor, long sourceActorId)
	{
		if (actor?.data == null || sourceActorId <= 0L) return;
		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L) return;

		Actor displacedTitleOwner = null;
		Actor displacedDharmaOwner = null;
		string title = Read(actor, XjActorDataKeys.ShiHonorificTitle).Trim();
		if (!string.IsNullOrWhiteSpace(title))
		{
			if (TitleOwners.TryGetValue(title, out long owner)
				&& owner != sourceActorId && owner != actorId
				&& XjActorRegistry.ResolveKnownOrWorld(owner, out Actor prior)
				&& prior?.data != null && prior.isAlive())
			{
				// 兼容旧版本：若转世等待期间称号已经错误流给了别人，原摩诃的
				// 转世载荷优先。先收回，再让误占者按当前规则生成新的唯一尊号。
				displacedTitleOwner = prior;
			}
			TitleOwners[title] = actorId;
		}

		string dharmaName = Read(actor, XjActorDataKeys.ShiDharmaName).Trim();
		if (!string.IsNullOrWhiteSpace(dharmaName))
		{
			if (DharmaNameOwners.TryGetValue(dharmaName, out long owner)
				&& owner != sourceActorId && owner != actorId
				&& XjActorRegistry.ResolveKnownOrWorld(owner, out Actor prior)
				&& prior?.data != null && prior.isAlive())
			{
				displacedDharmaOwner = prior;
			}
			DharmaNameOwners[dharmaName] = actorId;
		}

		if (displacedTitleOwner?.data != null) EnsureForActor(displacedTitleOwner);
		if (displacedDharmaOwner?.data != null
			&& (displacedTitleOwner?.data == null
				|| ((BaseSystemData)displacedDharmaOwner.data).id != ((BaseSystemData)displacedTitleOwner.data).id))
		{
			EnsureForActor(displacedDharmaOwner);
		}
	}

	private static string GenerateDharmaName(long actorId)
	{
		return DharmaNameCatalog[XjDeterministicHash.PositiveIndex(actorId,
			"shi_dharma_name_catalog_candidate", DharmaNameCatalog.Length)];
	}

	private static string Pick(string[] values, long actorId, string salt)
	{
		return values == null || values.Length == 0 ? string.Empty
			: values[XjDeterministicHash.PositiveIndex(actorId, salt, values.Length)];
	}

	private static void ClearShiProjection(Actor actor, string realmId, string dharmaName)
	{
		if (actor?.data == null) return;
		string baseName = ResolveBaseName(actor);
		string realm = XjShiCatalog.GetRealmDisplay(realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRebirthState, out string rebirthState);
		bool preservePreviousTitle = string.Equals(rebirthState, XjShiRebirthStateIds.Recovering, StringComparison.Ordinal);
		string retainedTitle = preservePreviousTitle
			? Read(actor, XjActorDataKeys.ShiHonorificTitle).Trim()
			: string.Empty;
		if (!preservePreviousTitle)
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.ShiHonorificTitle, string.Empty);
		}
		XjActorAccessor.SetString(actor, XjActorDataKeys.ShiDharmaName, dharmaName ?? string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, baseName);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameTitle, retainedTitle);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameRealmDisplay, realm);
		string titledBaseName = string.IsNullOrWhiteSpace(retainedTitle)
			? baseName : retainedTitle + "·" + baseName;
		string expected = string.IsNullOrWhiteSpace(realm) ? titledBaseName : titledBaseName + "-" + realm;
		if (!string.Equals(actor.getName()?.Trim(), expected, StringComparison.Ordinal))
			XjActorStateWriteGateway.SetDisplayName(actor, expected, customName: true);
	}

	private static string ResolveBaseName(Actor actor)
	{
		string stored = Read(actor, XjActorDataKeys.XjNameBase).Trim();
		if (!string.IsNullOrWhiteSpace(stored)) return stored;
		string current = actor?.getName()?.Trim() ?? string.Empty;
		int dot = current.IndexOf('·');
		if (dot >= 0 && dot + 1 < current.Length) current = current.Substring(dot + 1);
		int dash = current.LastIndexOf('-');
		if (dash > 0) current = current.Substring(0, dash);
		current = current.Trim();
		if (string.IsNullOrWhiteSpace(current)) current = "无名释修";
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjNameBase, current);
		return current;
	}

	private static string Read(Actor actor, string key)
	{
		return XjActorAccessor.TryGetString(actor, key, out string value) ? value ?? string.Empty : string.Empty;
	}
}
