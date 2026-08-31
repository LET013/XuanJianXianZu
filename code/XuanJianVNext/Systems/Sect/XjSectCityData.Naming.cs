using System.Collections.Generic;
using System;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.History.Books;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.Systems.Sect;

/// <summary>
/// 城市级宗门数据管理（VNext 版）
/// 宗门数据存储在 city.data 中
/// 核心职责：宗门创建、成员管理、山峰管理、角色分配
/// </summary>
internal static partial class XjSectCityData
{
	private static readonly System.Random SharedRandom = new System.Random();
	private static readonly string[] ZongMenNameSuffixes = { "府", "宗", "门", "道", "宫", "阁", "观" };
	private static readonly string[] SanYinZongMenTitles = { "玄阴", "月阴", "素阴", "寒阴", "广寒", "太素" };
	private static readonly string[] SanYangZongMenTitles = { "曦阳", "昭阳", "丹阳", "炎阳", "紫阳", "大日" };
	private static readonly string[] SanLeiZongMenTitles = { "神霄", "紫霄", "玄霄", "青霄", "神雷", "玉枢" };
	private static readonly string[] JinDeZongMenTitles = { "太白", "玄金", "金阙", "素金", "西庚", "流金" };
	private static readonly string[] MuDeZongMenTitles = { "万木", "青木", "长春", "建木", "若木", "青华" };
	private static readonly string[] ShuiDeZongMenTitles = { "沧浪", "黑水", "北溟", "玄水", "沧溟", "寒渊" };
	private static readonly string[] HuoDeZongMenTitles = { "真火", "丹火", "南明", "朱明", "炎离", "离光" };
	private static readonly string[] TuDeZongMenTitles = { "厚土", "坤元", "黄庭", "重岳", "息壤", "坤舆" };
	// 十二炁各支名称严格隔离，不能把别支的道统标记写进宗门名。
	private static readonly string[] QingQiZongMenTitles = { "太清", "清微", "澄霄", "玉清", "清虚", "玄清" };
	private static readonly string[] ZiQiZongMenTitles = { "紫宸", "紫微", "紫霄", "绛霄", "紫极", "玄紫" };
	private static readonly string[] ZhenQiZongMenTitles = { "真元", "纯一", "玄真", "真武", "归真", "元真" };
	private static readonly string[] SuiQiZongMenTitles = { "邃冥", "幽渊", "玄冥", "深渊", "冥河", "邃玄" };
	private static readonly string[] HanQiZongMenTitles = { "玄霜", "凛冬", "寒渊", "冰魄", "松雪", "凝霜" };
	private static readonly string[] XiQiZongMenTitles = { "晞阳", "晨曦", "曦月", "昭晞", "朝霞", "景曦" };
	private static readonly string[] RuiQiZongMenTitles = { "瑞霞", "嘉祥", "灵瑞", "祥云", "瑞应", "庆云" };
	private static readonly string[] ShaQiZongMenTitles = { "煞轮", "厉劫", "玄煞", "血煞", "镇煞", "煞海" };
	private static readonly string[] HuaQiZongMenTitles = { "华藏", "璧光", "华盖", "玉华", "宝华", "华阳" };
	private static readonly string[] ZheQiZongMenTitles = { "谪仙", "孤天", "谪星", "离宫", "天外", "玄谪" };
	private static readonly string[] ShangYiZongMenTitles = { "上仪", "天衡", "太仪", "承天", "天枢", "上清" };
	private static readonly string[] XiaYiZongMenTitles = { "下仪", "幽都", "黄泉", "忘川", "阴都", "幽冥" };
	private static readonly string[] FallbackZongMenTitles = { "太玄", "清微", "玄真", "道玄", "玉清", "守一", "含章", "归元", "洞真", "灵台", "通明", "冲和" };

	#region 宗门创建

	internal static bool TryCreateZongMen(City city, int currentYear, IReadOnlyDictionary<long, List<long>> cityIndex = null)
	{
		if (HasZongMen(city)) return false;
		Actor zongZhu = FindZongMenMasterCandidate(city, cityIndex);
		if (zongZhu == null) return false;

		return TryCreateZongMenWithFounder(city, zongZhu, currentYear, cityIndex);
	}

	internal static bool TryCreateZongMenWithFounder(City city, Actor zongZhu, int currentYear, IReadOnlyDictionary<long, List<long>> cityIndex = null, long sectId = 0L, string nameOverride = null, bool allowExistingIdentity = false)
	{
		if (city?.data == null || zongZhu?.data == null || sectId <= 0L) return false;
		if (XjYinSiTraitLifecycle.IsYinSi(zongZhu) || !CanFoundZongMen(zongZhu, allowExistingIdentity) || zongZhu.city != city) return false;
		if (GetRealmLevel(zongZhu) < ZiFuRealmLevel) return false;
		if (!XjSectRepository.TryGetBySectId(sectId, out XuanJianVNext.Data.Sect.XjSectArchiveRecord sect) || sect == null) return false;
		int year = Math.Max(Math.Max(0, currentYear), Math.Max(Math.Max(0, XjYearTracker.CurrentYear), Math.Max(0, World.world?.map_stats?.year ?? 0)));
		int ziFuEnteredYear = XjCultivationStateTransitions.ReadZiFuEnteredYear(zongZhu);
		if (ziFuEnteredYear > 0) year = Math.Max(year, ziFuEnteredYear);
		XjSectCommands.RefreshTerritoryIndex(sectId);
		InitializeMembers(city, zongZhu, cityIndex, year);
		XjSectMembershipService.AssignZongZhu(city, zongZhu, year, true, "FounderCreated");
		MarkFounderHistory(zongZhu, city, year);
		PublishZongMenFoundation(zongZhu, sect.Name, year);
		XjActorAccessor.TryGetString(zongZhu, XjActorDataKeys.DaoTu, out string foundingDaoTu);
		XjThreeBookWriter.RecordSectFounded(zongZhu, sectId, sect.Name, foundingDaoTu, year);
		return true;
	}

	private static void PublishZongMenFoundation(Actor founder, string zongMenName, int currentYear)
	{
		if (founder?.data == null || string.IsNullOrWhiteSpace(zongMenName))
		{
			return;
		}

		XjActorAccessor.TryGetString(founder, XjActorDataKeys.DaoTu, out string daoTu);
		string historyText = XjAnnouncementText.BuildZongMenFoundation(founder, zongMenName, daoTu);
		string tipText = XjAnnouncementText.BuildZongMenFoundationTip(founder, zongMenName);
		XjBroadcastSystem.BroadcastBLevelActorEvent(founder, historyText, tipText, XjEventIconCatalog.ZongMenCreation);
		XjChronicleWriter.RecordZongMenFounded(founder, currentYear, zongMenName, daoTu);
	}

	private static void InitializeMembers(City city, Actor founder, IReadOnlyDictionary<long, List<long>> cityIndex, int currentYear)
	{
		if (city?.data == null)
		{
			return;
		}

		if (founder?.data != null)
		{
			XjSectMembershipService.EnsureMember(city, founder, currentYear, "FounderMember");
			MarkJoinEvaluated(city, founder);
		}

		if (cityIndex == null || !cityIndex.TryGetValue(city.data.id, out List<long> candidateIds) || candidateIds == null)
		{
			return;
		}

		for (int i = 0; i < candidateIds.Count; i++)
		{
			Actor actor = ResolveActor(candidateIds[i]);
			if (actor?.data == null || !actor.isAlive() || actor.city != city
				|| XjYinSiTraitLifecycle.IsYinSi(actor)
				|| !IsCultivator(actor))
			{
				continue;
			}

			if (founder?.data != null && GetActorId(actor) == GetActorId(founder))
			{
				continue;
			}

			// 初建时未被选中的修士仍可在后续招募周期重新尝试入宗。
			if (SharedRandom.NextDouble() <= 0.5)
			{
				XjSectMembershipService.EnsureMember(city, actor, currentYear, "FoundationInitialMember");
			}
		}
	}

	private static void MarkJoinEvaluated(City city, Actor actor)
	{
		if (city?.data == null || actor?.data == null)
		{
			return;
		}

		long actorId = GetActorId(actor);
		if (actorId <= 0L)
		{
			return;
		}

		List<long> ids = ReadIdList(city, KeyJoinEvaluatedIds);
		if (ids.Contains(actorId))
		{
			return;
		}

		ids.Add(actorId);
		WriteIdList(city, KeyJoinEvaluatedIds, ids);
	}

	private static Actor FindZongMenMasterCandidate(City city, IReadOnlyDictionary<long, List<long>> cityIndex = null)
	{
		if (city?.data == null) return null;

		if (cityIndex == null || !cityIndex.TryGetValue(city.data.id, out List<long> candidateIds) || candidateIds == null || candidateIds.Count == 0)
		{
			return null;
		}

		if (candidateIds.Count > 0)
		{
			Actor best = null;
			float bestScore = float.MinValue;
			for (int i = 0; i < candidateIds.Count; i++)
			{
				Actor actor = ResolveActor(candidateIds[i]);
				if (actor?.data == null || actor.city != city || !CanFoundZongMen(actor)) continue;
				int realmLevel = GetRealmLevel(actor);
				if (realmLevel < ZiFuRealmLevel) continue;
				float score = realmLevel * 100000f + GetActorId(actor);
				if (score > bestScore)
				{
					bestScore = score;
					best = actor;
				}
			}
			return best;
		}

		return null;
	}


	internal static bool BackfillFounderHistory(City city)
	{
		if (city?.data == null
			|| !TryReadActorId(city, KeyFounderId, out long founderId)
			|| founderId <= 0L)
		{
			return false;
		}

		Actor founder = ResolveActor(founderId);
		if (founder?.data == null)
		{
			return false;
		}

		return MarkFounderHistory(founder, city, GetCreationYear(city));
	}

	private static bool CanFoundZongMen(Actor actor, bool allowExistingIdentity = false)
	{
		if (actor?.data == null
			|| !actor.isAlive()
			|| XjLongShuSystem.IsLongShu(actor)
			|| XjYinSiTraitLifecycle.IsYinSi(actor)
			|| HasFoundedZongMen(actor))
		{
			return false;
		}

		return allowExistingIdentity || !XjSectAuthorityStore.TryGetSectId(GetActorId(actor), out _);
	}

	private static bool HasFoundedZongMen(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjZongMenFoundedCityId, out string rawCityId)
			|| string.IsNullOrWhiteSpace(rawCityId))
		{
			return false;
		}

		return long.TryParse(rawCityId, System.Globalization.NumberStyles.Integer,
			System.Globalization.CultureInfo.InvariantCulture, out long cityId)
			&& cityId > 0L;
	}

	private static bool MarkFounderHistory(Actor founder, City city, int currentYear)
	{
		if (founder?.data == null || city?.data == null)
		{
			return false;
		}

		long cityId = GetCityId(city);
		if (cityId <= 0L)
		{
			return false;
		}

		bool changed = !HasFoundedZongMen(founder);
		XjActorAccessor.SetString(
			founder,
			XjActorDataKeys.XjZongMenFoundedCityId,
			cityId.ToString(System.Globalization.CultureInfo.InvariantCulture));
		XjActorAccessor.SetInt(founder, XjActorDataKeys.XjZongMenFoundedYear, Math.Max(0, currentYear));
		return changed;
	}

	#endregion

	#region 名称生成

	internal static string GenerateZongMenName(City city, Actor founder)
	{
		string daoTu = ResolveFounderDaoTu(founder);
		string[] titles = ResolveZongMenTitlePool(daoTu);
		long seed = GetCityId(city) ^ GetActorId(founder);
		string suffix = ZongMenNameSuffixes[XjDeterministicHash.PositiveIndex(seed, "zongmen_suffix", ZongMenNameSuffixes.Length)];

		// 宗门唯一性看“前缀”而不是完整名称：玄阴门与玄阴观视为同名宗脉。
		// 从本道途词池按稳定哈希起点轮询，已有前缀会自动让位给下一候选。
		int start = XjDeterministicHash.PositiveIndex(seed, "zongmen_title", titles.Length);
		for (int offset = 0; offset < titles.Length; offset++)
		{
			string title = titles[(start + offset) % titles.Length];
			string candidate = title + suffix;
			if (XjSectRepository.IsSectNameAvailable(candidate)) return candidate;
		}

		// 本道途词池耗尽时再使用通用安全池；仍保持确定性，不做年度/帧级随机重试。
		int fallbackStart = XjDeterministicHash.PositiveIndex(seed, "zongmen_fallback_title", FallbackZongMenTitles.Length);
		for (int offset = 0; offset < FallbackZongMenTitles.Length; offset++)
		{
			string title = FallbackZongMenTitles[(fallbackStart + offset) % FallbackZongMenTitles.Length];
			string candidate = title + suffix;
			if (XjSectRepository.IsSectNameAvailable(candidate)) return candidate;
		}

		return XjSectRepository.BuildUniqueFallbackSectName(seed, suffix);
	}

	private static string ResolveFounderDaoTu(Actor founder)
	{
		if (founder?.data == null)
		{
			return string.Empty;
		}

		XjActorAccessor.TryGetString(founder, XjActorDataKeys.DaoTu, out string daoTu);
		return string.IsNullOrWhiteSpace(daoTu) ? string.Empty : daoTu.Trim();
	}

	private static string[] ResolveZongMenTitlePool(string daoTu)
	{
		string value = (daoTu ?? string.Empty).Trim();
		if (IsDaoTuOneOf(value, "三阴", "太阴", "少阴", "厥阴")) return SanYinZongMenTitles;
		if (IsDaoTuOneOf(value, "三阳", "太阳", "少阳", "明阳")) return SanYangZongMenTitles;
		if (IsDaoTuOneOf(value, "三雷", "天雷", "玄雷", "元雷", "霄雷")) return SanLeiZongMenTitles;
		if (IsDaoTuOneOf(value, "金德", "庚金", "齐金", "库金", "逍金", "兑金", "长庚")) return JinDeZongMenTitles;
		if (IsDaoTuOneOf(value, "木德", "角木", "更木", "保木", "正木", "集木")) return MuDeZongMenTitles;
		if (IsDaoTuOneOf(value, "水德", "坎水", "渌水", "合水", "府水", "牝水")) return ShuiDeZongMenTitles;
		if (IsDaoTuOneOf(value, "火德", "离火", "并火", "真火", "灴火", "牡火")) return HuoDeZongMenTitles;
		if (IsDaoTuOneOf(value, "土德", "艮土", "戊土", "宝土", "归土", "宣土")) return TuDeZongMenTitles;
		if (IsDaoTuOneOf(value, "清炁")) return QingQiZongMenTitles;
		if (IsDaoTuOneOf(value, "紫炁")) return ZiQiZongMenTitles;
		if (IsDaoTuOneOf(value, "真炁")) return ZhenQiZongMenTitles;
		if (IsDaoTuOneOf(value, "邃炁")) return SuiQiZongMenTitles;
		if (IsDaoTuOneOf(value, "寒炁")) return HanQiZongMenTitles;
		if (IsDaoTuOneOf(value, "晞炁")) return XiQiZongMenTitles;
		if (IsDaoTuOneOf(value, "瑞炁")) return RuiQiZongMenTitles;
		if (IsDaoTuOneOf(value, "煞炁")) return ShaQiZongMenTitles;
		if (IsDaoTuOneOf(value, "华炁")) return HuaQiZongMenTitles;
		if (IsDaoTuOneOf(value, "谪炁")) return ZheQiZongMenTitles;
		if (IsDaoTuOneOf(value, "上仪")) return ShangYiZongMenTitles;
		if (IsDaoTuOneOf(value, "下仪")) return XiaYiZongMenTitles;
		if (IsDaoTuOneOf(value, "十二炁")) return FallbackZongMenTitles;
		return FallbackZongMenTitles;
	}

	private static bool IsDaoTuOneOf(string daoTu, params string[] names)
	{
		if (string.IsNullOrWhiteSpace(daoTu))
		{
			return false;
		}

		for (int i = 0; i < names.Length; i++)
		{
			if (string.Equals(daoTu, names[i], StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static string GeneratePeakName(City city)
	{
		string[] words = {
			"玄霜", "景曦", "丹霞", "白虹", "垂云", "霏雾", "曦月", "曜日", "辰星", "霄汉",
			"流火", "霁月", "朔风", "紫渊", "灵墟", "苍梧", "沧溟", "碧落", "空桑", "观澜",
			"濯缨", "玉潢", "青冥", "赤壁", "翠微", "幽谷", "悬河", "磐石", "玉垒", "元乌",
			"太虚", "归藏", "鸿蒙", "玉京", "钧天", "素问", "玄圭", "通明", "守一", "抱朴",
			"含章", "致和", "履中", "守拙", "金阙", "赤炀", "龙渊", "烨衡", "青霄", "凤鸣"
		};
		HashSet<string> existing = new HashSet<string>(StringComparer.Ordinal);
		List<(int id, string name)> peaks = GetPeaks(city);
		for (int i = 0; i < peaks.Count; i++) existing.Add(peaks[i].name);
		int start = SharedRandom.Next(words.Length);
		for (int offset = 0; offset < words.Length; offset++)
		{
			string candidate = words[(start + offset) % words.Length] + "峰";
			if (!existing.Contains(candidate)) return candidate;
		}
		return "玄" + (GetRegularPeakIds(city).Count + 1) + "峰";
	}

	#endregion
}
