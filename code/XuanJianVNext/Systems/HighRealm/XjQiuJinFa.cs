using XuanJianVNext.Core;
using System;
using UnityEngine;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.LongShu;

namespace XuanJianVNext.Systems.HighRealm;

internal readonly struct XjQiuJinFaState
{
	internal static XjQiuJinFaState Empty { get; } = new XjQiuJinFaState(
		false,
		string.Empty,
		string.Empty,
		0,
		string.Empty,
		false,
		0,
		"Empty",
		string.Empty);

	internal readonly bool Found;
	internal readonly string Name;
	internal readonly string SourceGongFaName;
	internal readonly int SourceGongFaGrade;
	internal readonly string SourceDaoTu;
	internal readonly bool Ready;
	internal readonly int LastYear;
	internal readonly string ReasonCode;
	internal readonly string BoundAuthority;

	internal XjQiuJinFaState(
		bool found,
		string name,
		string sourceGongFaName,
		int sourceGongFaGrade,
		string sourceDaoTu,
		bool ready,
		int lastYear,
		string reasonCode,
		string boundAuthority = "")
	{
		Found = found;
		Name = name ?? string.Empty;
		SourceGongFaName = sourceGongFaName ?? string.Empty;
		SourceGongFaGrade = sourceGongFaGrade;
		SourceDaoTu = sourceDaoTu ?? string.Empty;
		Ready = ready;
		LastYear = lastYear < 0 ? 0 : lastYear;
		ReasonCode = reasonCode ?? string.Empty;
		BoundAuthority = boundAuthority ?? string.Empty;
	}
}


internal static class XjQiuJinFaAccessor
{
	internal static XjQiuJinFaState BuildState(Actor actor)
	{
		if (actor?.data == null)
		{
			return new XjQiuJinFaState(false, string.Empty, string.Empty, 0, string.Empty, false, 0, "ActorInvalid");
		}

		if (!XjSafeCore.IsAliveActor(actor))
		{
			return new XjQiuJinFaState(false, string.Empty, string.Empty, 0, string.Empty, false, 0, "ActorInvalidOrDead");
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaName, out string name);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, out string sourceDaoTu);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority, out string boundAuthority);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaOrigin, out string origin);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaReady, out int ready);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, out int lastYear);

		bool found = !string.IsNullOrWhiteSpace(name) && ready == 1;
		return new XjQiuJinFaState(
			found,
			name,
			string.Empty,
			0,
			sourceDaoTu,
			ready == 1,
			lastYear,
			found ? (string.IsNullOrWhiteSpace(origin) ? "Ok" : origin.Trim()) : "NoQiuJinFa",
			boundAuthority);
	}

	internal static void WriteState(Actor actor, in XjQiuJinFaState state)
	{
		if (!XjSafeCore.IsAliveActor(actor) || string.IsNullOrWhiteSpace(state.Name))
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaName, state.Name);
		// 求金法是独立传承，不与任何一本五品/六品功法建立外键。
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaName, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, state.SourceDaoTu);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority, state.BoundAuthority);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaOrigin, state.ReasonCode);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaReady, state.Ready ? 1 : 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, state.LastYear);
	}

	internal static void SetLastYear(Actor actor, int lastYear)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, lastYear < 0 ? 0 : lastYear);
	}

	internal static void Clear(Actor actor, string reason = "")
	{
		if (actor?.data == null)
		{
			return;
		}

		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaName, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaName, string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaSourceGongFaGrade, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaSourceDaoTu, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaBoundAuthority, string.Empty);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaOrigin, reason ?? string.Empty);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaReady, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaLastYear, 0);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, string.Empty);
	}
}


internal static class XjQiuJinFaNameLibrary
{
	private static readonly string[] QiuJinFaCommonDoubleWords =
	{
		"叩关", "证性", "求真", "合位", "铸性", "归一", "炼神", "定命", "承位", "照性", "凝真", "返本",
		"化一", "证玄", "守一", "契金", "问道", "叩玄", "推真", "演性", "合真", "证位", "升玄", "抱一",
		"通微", "归元", "凝性", "定真", "参同", "复命", "观化", "会真", "返照", "洞玄", "清微", "含真"
	};

	private static readonly string[] QiuJinFaSuffixWords =
	{
		"法", "诀", "经", "章", "书", "录", "典", "要"
	};

	private static readonly string[] QiuJinFaJinDeDoubleWords =
	{
		"白帝", "西极", "太白", "庚辛", "金庭", "锐精", "兑泽", "肃杀", "斩玄", "锋藏", "铸真", "金阙"
	};

	private static readonly string[] QiuJinFaMuDeDoubleWords =
	{
		"青帝", "东华", "句芒", "建木", "长生", "荣枯", "乙木", "苍灵", "青阳", "青华", "春元", "扶桑"
	};

	private static readonly string[] QiuJinFaShuiDeDoubleWords =
	{
		"玄冥", "天一", "坎宫", "沧渊", "四海", "壬癸", "归流", "溟波", "水府", "重渊", "净涟", "寒泉"
	};

	private static readonly string[] QiuJinFaHuoDeDoubleWords =
	{
		"赤帝", "南离", "祝融", "朱明", "炎天", "丹景", "离宫", "焕赫", "朱光", "烈曜", "炽阳", "明焰"
	};

	private static readonly string[] QiuJinFaTuDeDoubleWords =
	{
		"黄庭", "后土", "坤厚", "中宫", "镇岳", "戊己", "承天", "载物", "坤舆", "厚载", "山岳", "玄垣"
	};

	private static readonly string[] QiuJinFaSuDeDoubleWords =
	{
		"青宣", "素德", "宣真", "青律", "太素", "清衡", "正仪", "玄章", "素元", "青简", "宣玄", "素霞"
	};

	private static readonly string[] QiuJinFaSanYinDoubleWords =
	{
		"广寒", "太阴", "少阴", "厥阴", "幽月", "玄阴", "寒蟾", "月府", "凝霜", "素魄", "阴华", "清辉", "湖月", "夜光", "太素", "玄珠", "幽荧", "冰轮", "桂魄", "望舒"
	};

	private static readonly string[] QiuJinFaSanYangDoubleWords =
	{
		"太阳", "少阳", "明阳", "扶桑", "金乌", "纯阳", "东君", "曜灵", "炎光", "昊景", "阳华", "日轮"
	};

	private static readonly string[] QiuJinFaSanLeiDoubleWords =
	{
		"玄雷", "霄雷", "元雷", "玉枢", "九霄", "震霆", "天鼓", "劫雷", "雷罚", "电策", "云霆", "威灵"
	};

	private static readonly string[] QiuJinFaShiErQiDoubleWords =
	{
		"清虚", "紫清", "真元", "邃冥", "寒华", "晞明", "瑞应", "煞轮", "华藏", "谪仙", "上仪", "下仪"
	};

	internal static string GenerateName(string daoTu, string sourceGongFaName, long seed)
	{
		return GenerateQiuJinFaName(daoTu, sourceGongFaName, seed);
	}

	internal static string GenerateQiuJinFaName(string daoTu, string sourceGongFaName, long seed)
	{
		string normalizedDaoTu = Normalize(daoTu);
		string source = Normalize(sourceGongFaName);
		string[] specificPool = ResolveQiuJinFaSpecificWordPool(normalizedDaoTu);
		if (specificPool.Length == 0)
		{
			return string.Empty;
		}

		string first = PickDeterministicQiuJinFaWord(specificPool, normalizedDaoTu, source, seed, 0);
		string second = PickDistinctQiuJinFaWord(QiuJinFaCommonDoubleWords, first, normalizedDaoTu, source, seed, 1);

		string suffix = PickDeterministicQiuJinFaWord(QiuJinFaSuffixWords, normalizedDaoTu, source, seed, 3);
		return NormalizeQiuJinFaName(first + second + suffix);
	}

	internal static string NormalizeQiuJinFaName(string name)
	{
		string text = Normalize(name);
		if (string.IsNullOrWhiteSpace(text))
		{
			return "求金法";
		}

		for (int i = 0; i < QiuJinFaSuffixWords.Length; i++)
		{
			if (text.EndsWith(QiuJinFaSuffixWords[i], StringComparison.Ordinal))
			{
				return text;
			}
		}

		return text + "法";
	}

	private static string[] ResolveQiuJinFaSpecificWordPool(string daoTu)
	{
		if (IsJinDe(daoTu)) return QiuJinFaJinDeDoubleWords;
		if (IsMuDe(daoTu)) return QiuJinFaMuDeDoubleWords;
		if (IsShuiDe(daoTu)) return QiuJinFaShuiDeDoubleWords;
		if (IsHuoDe(daoTu)) return QiuJinFaHuoDeDoubleWords;
		if (IsTuDe(daoTu)) return QiuJinFaTuDeDoubleWords;
		if (IsSuDe(daoTu)) return QiuJinFaSuDeDoubleWords;
		if (IsSanYin(daoTu)) return QiuJinFaSanYinDoubleWords;
		if (IsSanYang(daoTu)) return QiuJinFaSanYangDoubleWords;
		if (IsSanLei(daoTu)) return QiuJinFaSanLeiDoubleWords;
		return IsShiErQi(daoTu) ? QiuJinFaShiErQiDoubleWords : Array.Empty<string>();
	}

	private static string PickDistinctQiuJinFaWord(
		string[] pool,
		string current,
		string daoTu,
		string source,
		long seed,
		int slot)
	{
		if (pool == null || pool.Length == 0)
		{
			return string.Empty;
		}

		long hash = ComputeQiuJinFaStableHash(seed, daoTu + "|" + source + "|" + slot.ToString());
		int start = (int)(hash % pool.Length);
		for (int offset = 0; offset < pool.Length; offset++)
		{
			string candidate = pool[(start + offset) % pool.Length];
			if (!IsRedundantQiuJinBoundary(current, candidate))
			{
				return candidate;
			}
		}

		return pool[start];
	}

	private static bool IsRedundantQiuJinBoundary(string left, string right)
	{
		string first = left ?? string.Empty;
		string second = right ?? string.Empty;
		if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(second))
		{
			return false;
		}

		return string.Equals(first, second, StringComparison.Ordinal)
			|| first.EndsWith(second, StringComparison.Ordinal)
			|| second.StartsWith(first, StringComparison.Ordinal)
			|| first[first.Length - 1] == second[0];
	}

	private static string PickDeterministicQiuJinFaWord(string[] pool, string daoTu, string source, long seed, int slot)
	{
		if (pool == null || pool.Length == 0)
		{
			return string.Empty;
		}

		long hash = ComputeQiuJinFaStableHash(seed, daoTu + "|" + source + "|" + slot.ToString());
		return pool[(int)(hash % pool.Length)];
	}

	private static long ComputeQiuJinFaStableHash(long seed, string salt)
	{
		unchecked
		{
			ulong hash = 14695981039346656037UL;
			hash ^= (ulong)seed;
			hash *= 1099511628211UL;
			string text = salt ?? string.Empty;
			for (int i = 0; i < text.Length; i++)
			{
				hash ^= text[i];
				hash *= 1099511628211UL;
			}

			return (long)(hash & 0x7FFFFFFFFFFFFFFFUL);
		}
	}

	private static string Normalize(string value)
	{
		return (value ?? string.Empty).Trim();
	}

	private static bool IsSanYin(string daoTu) => daoTu == "太阴" || daoTu == "少阴" || daoTu == "厥阴";
	private static bool IsSanYang(string daoTu) => daoTu == "太阳" || daoTu == "少阳" || daoTu == "明阳";
	private static bool IsSanLei(string daoTu) => daoTu == "玄雷" || daoTu == "霄雷" || daoTu == "元雷";
	private static bool IsJinDe(string daoTu) => daoTu == "兑金" || daoTu == "逍金" || daoTu == "齐金" || daoTu == "库金" || daoTu == "庚金";
	private static bool IsMuDe(string daoTu) => daoTu == "角木" || daoTu == "正木" || daoTu == "集木" || daoTu == "更木" || daoTu == "保木";
	private static bool IsShuiDe(string daoTu) => daoTu == "坎水" || daoTu == "渌水" || daoTu == "合水" || daoTu == "府水" || daoTu == "牝水";
	private static bool IsHuoDe(string daoTu) => daoTu == "离火" || daoTu == "灴火" || daoTu == "并火" || daoTu == "真火" || daoTu == "牡火";
	private static bool IsTuDe(string daoTu) => daoTu == "艮土" || daoTu == "戊土" || daoTu == "归土" || daoTu == "宝土" || daoTu == "宣土";
	private static bool IsSuDe(string daoTu) => daoTu == "青宣";
	private static bool IsShiErQi(string daoTu) => daoTu is "清炁" or "紫炁" or "真炁" or "邃炁" or "寒炁" or "晞炁"
		or "瑞炁" or "煞炁" or "华炁" or "谪炁" or "上仪" or "下仪";
}


internal static class XjQiuJinFaSystem
{
	private const int AttemptIntervalYears = 5;

	internal static void TickActor(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| XjTrueDamageSystem.IsJinXingYaoXie(actor)
			|| !XjXianJiAccessor.HasFive(actor))
		{
			return;
		}

		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (qiuJinFa.Found)
		{
			return;
		}

		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || gongFa.Grade != 5)
		{
			return;
		}

		string resolvedDaoTu = string.IsNullOrWhiteSpace(gongFa.DaoTu) ? snapshot.DaoTu : gongFa.DaoTu;
		if (string.IsNullOrWhiteSpace(resolvedDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(resolvedDaoTu.Trim(), out _))
		{
			XjFamilyDaoTuRules.TryEnsureCultivatorDaoTu(actor, out resolvedDaoTu);
			snapshot = XjActorCultivationSnapshotBuilder.Build(actor);
		}
		if (string.IsNullOrWhiteSpace(resolvedDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(resolvedDaoTu.Trim(), out _))
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		// 第五神通完成后必须再经历完整的五年求金法周期。
		// XjQiuJinFaLastYear 尚未写入时，以最后一门神通的获得年份作为基准，
		// 避免同一年度在高境管线中连续完成第五神通并立刻尝试求金法。
		int attemptBaselineYear = qiuJinFa.LastYear;
		if (attemptBaselineYear <= 0)
		{
			XjXianJiState xianJiState = XjXianJiAccessor.BuildState(actor);
			attemptBaselineYear = xianJiState.LastYear;
		}
		if (attemptBaselineYear > 0 && currentYear < attemptBaselineYear + AttemptIntervalYears)
		{
			return;
		}
		if (!HasFiveGrade5GongFa(actor))
		{
			return;
		}

		XjQiuJinFaAccessor.SetLastYear(actor, currentYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, out int existingFailureCount);
		try
		{
			XjFamilyHighGradeTransmission.TryBorrowQiuJinFa(actor, snapshot, gongFa, currentYear);
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][求金法] 家族借法阶段异常 actor=" + GetActorId(actor) + " ex=" + ex.GetType().Name);
		}
		XjQiuJinFaState borrowed = XjQiuJinFaAccessor.BuildState(actor);
		if (borrowed.Found && borrowed.Ready)
		{
			PublishQiuJinFaSuccess(actor, borrowed);
			return;
		}

		if (!ShouldSucceed(actor, snapshot, gongFa, currentYear, existingFailureCount))
		{
			int nextFailureCount = existingFailureCount + 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, nextFailureCount);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, "慧光未契");
			return;
		}

		string daoTu = resolvedDaoTu;
		string name = XjQiuJinFaNameLibrary.GenerateName(daoTu, gongFa.Name, GetActorId(actor));
		if (string.IsNullOrWhiteSpace(name))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, "道途未定");
			return;
		}

		string boundAuthority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(daoTu, name, string.Empty);
		if (string.IsNullOrWhiteSpace(boundAuthority)
			|| !XjGuoWeiQuanBingRegistry.IsAuthorityAvailable(daoTu, boundAuthority))
		{
			int nextFailureCount = existingFailureCount + 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjQiuJinFaFailureCount, nextFailureCount);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, "所契权柄已失");
			return;
		}

		XjQiuJinFaState newQiuJinFa = new XjQiuJinFaState(
			true,
			name,
			string.Empty,
			0,
			daoTu,
			true,
			currentYear,
			"QiuJinFaComprehended",
			boundAuthority);
		XjQiuJinFaAccessor.WriteState(actor, newQiuJinFa);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjQiuJinFaLastFailureReason, string.Empty);
		PublishQiuJinFaSuccess(actor, newQiuJinFa);
	}

	private static bool HasFiveGrade5GongFa(Actor actor)
	{
		// 只统计角色真实持久化的功法集合；仙基快照不再临时生成五品功法。
		return XjActorGongFaCollection.HasFiveRealGrade5GongFa(actor);
	}

	internal static void PublishQiuJinFaSuccess(Actor actor, XjQiuJinFaState qiuJinFa)
	{
		if (actor?.data == null || !qiuJinFa.Found || !qiuJinFa.Ready)
		{
			return;
		}

		// 求金法已经写入角色后，纪事、仓库、乾坤袋和公告都只是派生副作用。
		// 任一派生系统异常都不能把年度管线或玩家进程一起抛断。
		RunQiuJinSideEffect(actor, "FamilyEvent", () =>
			XjFamilyDomainEventRouter.Publish(XjFamilyDomainEvent.QiuJinFaComprehended(
				actor,
				qiuJinFa.Name,
				string.Empty,
				0,
				qiuJinFa.SourceDaoTu,
				string.Empty,
				qiuJinFa.BoundAuthority)));
		RunQiuJinSideEffect(actor, "Warehouse", () =>
			XjGongFaWarehouseReconciler.ReconcileActor(actor, qiuJinFa.LastYear));
		RunQiuJinSideEffect(actor, "QianKunDai", () => XjQianKunDaiSystem.UpdateState(actor));
		if (XjRuntimeSettings.BroadcastGongFaWriteEnabled)
		{
			RunQiuJinSideEffect(actor, "Announcement", () =>
				XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.BroadcastBLevelActorEvent(
					actor,
					XuanJianVNext.Systems.Broadcast.XjAnnouncementText.BuildQiuJinFaComprehension(
						actor,
						qiuJinFa.Name,
						string.Empty,
						qiuJinFa.SourceDaoTu),
					null,
					XuanJianVNext.Systems.Broadcast.XjEventIconCatalog.QiuJinFaAcquire));
		}
	}

	private static void RunQiuJinSideEffect(Actor actor, string stage, Action action)
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][求金法] " + stage + " actor=" + GetActorId(actor) + " ex=" + ex.GetType().Name);
		}
	}

	private static bool ShouldSucceed(Actor actor, in XjActorCultivationSnapshot snapshot, in XjGongFaState gongFa, int currentYear, int failureCount)
	{
		// 道主之资：求金法参悟100%通过
		if (actor != null && actor.hasTrait("ChuShen8"))
			return true;

		// 默认仍是旧版最高1%；配置提高时按原基础、慧光、资质权重等比放大。
		float chanceCap = Math.Clamp(XjRuntimeSettings.QiuJinFaChanceCap, 0.001f, 0.05f);
		float baseChance = chanceCap * 0.1f;
		float huiGuangBonus = HuiGuangCurveChance(snapshot.HuiGuang, 55f, 120f, chanceCap * 0.6f);
		float aptitudeBonus = AptitudeCurveChance(snapshot.XjZz, chanceCap * 0.3f);
		string daoTu = string.IsNullOrWhiteSpace(gongFa.DaoTu) ? snapshot.DaoTu : gongFa.DaoTu;
		float authorityMultiplier = XjAuthorityInfluenceService.GetQiuJinComprehensionMultiplier(daoTu);
		float chance = Math.Min(chanceCap, (baseChance + huiGuangBonus + aptitudeBonus) * authorityMultiplier);
		if (XjLongShuSystem.IsLongShu(actor))
		{
			// 龙属不走家族与宗门借法链，固定为每次年度参悟1%，不随失败次数累加。
			chance = 0.01f;
		}
		return XjDeterministicHash.PositiveIndex(GetActorId(actor) + currentYear + gongFa.Grade, "qiu_jin_fa", 10000)
			< (int)Math.Floor(chance * 10000f);
	}

	private static float HuiGuangCurveChance(float huiGuang, float minimum, float peak, float maximumChance)
	{
		if (huiGuang < minimum || peak <= minimum || maximumChance <= 0f)
		{
			return 0f;
		}

		float t = Math.Min(1f, Math.Max(0f, (huiGuang - minimum) / (peak - minimum)));
		float smooth = t * t * (3f - (2f * t));
		return Math.Min(maximumChance, Math.Max(0f, maximumChance * smooth));
	}

	private static float AptitudeCurveChance(int xjZz, float maximumChance)
	{
		if (xjZz < 1 || xjZz > 6 || maximumChance <= 0f)
		{
			return 0f;
		}

		float t = Math.Min(1f, Math.Max(0f, (xjZz - 1f) / 5f));
		float smooth = t * t * (3f - (2f * t));
		return maximumChance * smooth;
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}

	private static long GetActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}
}


internal static class XjQiuJinBoundGongFaPromotion
{
	internal static void TickActor(Actor actor, XjActorCultivationSnapshot snapshot)
	{
		if (actor?.data == null
			|| XjGongFaAptitudeRules.GetMaximumAllowedGrade(actor, snapshot) < 6
			|| !XjXianJiAccessor.HasFive(actor))
		{
			return;
		}

		int currentYear = GetCurrentYear(actor);
		EnsureFiveMappedGrade5GongFa(actor, currentYear);
		if (!XjActorGongFaCollection.HasFiveRealGrade5GongFa(actor)) return;
		XjGongFaState gongFa = XjGongFaAccessor.BuildState(actor);
		if (!gongFa.Found || gongFa.Grade != 5)
		{
			return;
		}

		// 求金法不再绑定某一本功法。角色自己没有求金法时，在六品晋升
		// 判定现场依次尝试借用家族、宗门求金法。
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		bool borrowedQiuJinFa = false;
		if (!qiuJinFa.Found || !qiuJinFa.Ready)
		{
			borrowedQiuJinFa = XjFamilyHighGradeTransmission.TryBorrowQiuJinFa(actor, snapshot, gongFa, currentYear);
			qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		}
		if (!qiuJinFa.Found || !qiuJinFa.Ready)
		{
			borrowedQiuJinFa = XuanJianVNext.Systems.ZongMen.XjZongMenQiuJinFaBorrow.TryBorrowForActor(actor);
			qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		}
		if (!qiuJinFa.Found || !qiuJinFa.Ready)
		{
			return;
		}
		if (borrowedQiuJinFa)
		{
			XjQiuJinFaSystem.PublishQiuJinFaSuccess(actor, qiuJinFa);
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaHighPromotionLastYear, out int lastAttemptYear);
		if (lastAttemptYear >= currentYear)
		{
			return;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionLastYear, currentYear);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, out int existingFailureCount);
		if (XjFamilyHighGradeTransmission.TryBorrowGrade6(actor, snapshot, gongFa))
		{
			return;
		}

		if (!ShouldPromoteToGrade6(actor, snapshot, gongFa, currentYear, existingFailureCount))
		{
			int nextFailureCount = existingFailureCount + 1;
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, nextFailureCount);
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, "六品未契");
			return;
		}

		string daoTu = string.IsNullOrWhiteSpace(gongFa.DaoTu) ? snapshot.DaoTu : gongFa.DaoTu;
		string nextName = XjGongFaNameLibrary.NormalizeNameForGrade(gongFa.Name, daoTu, 6);
		if (string.IsNullOrWhiteSpace(nextName))
		{
			nextName = qiuJinFa.Name;
		}

		XjGongFaState promoted = new XjGongFaState(
			true,
			nextName,
			6,
			0,
			0f,
			daoTu,
			true,
			"QiuJinBoundGrade6");
		if (!XjActorGongFaCollection.PromoteBoundGrade5ToGrade6(
			actor,
			string.Empty,
			nextName,
			daoTu,
			"求金法贯通"))
		{
			XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, "五品功法写入失败");
			return;
		}
		XjGongFaAccessor.WriteState(actor, promoted);
		XjGongFaAccessor.WriteSource(actor, "求金法贯通");
		XjActorAccessor.SetInt(actor, XjActorDataKeys.XjGongFaHighPromotionFailureCount, 0);
		XjActorAccessor.SetString(actor, XjActorDataKeys.XjGongFaHighPromotionLastFailureReason, string.Empty);
		XjGongFaProgression.PublishGongFaPromoted(actor, promoted);
	}

	private static bool ShouldPromoteToGrade6(Actor actor, in XjActorCultivationSnapshot snapshot, in XjGongFaState gongFa, int currentYear, int failureCount)
	{
		if (actor != null && actor.hasTrait("ChuShen8")) return true;
		// 求金法已成、五门神通与五部五品功法齐备后，失败只代表暂未贯通，
		// 不再允许无限期卡死：第十次真实尝试必定晋升。
		if (failureCount >= 9) return true;
		float baseChance = HuiGuangCurveChance(snapshot.HuiGuang, 65f, 110f, 0.12f);
		float chance = Math.Min(0.42f, baseChance + Math.Min(0.30f, Math.Max(0, failureCount) * 0.03f));
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		return XjDeterministicHash.PositiveIndex(actorId + currentYear + gongFa.Grade, "grade6_promotion", 10000) < (int)Math.Floor(chance * 10000f);
	}

	private static void EnsureFiveMappedGrade5GongFa(Actor actor, int currentYear)
	{
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (xianJi.Ids == null) return;

		// 先清除旧档中“主功法未映射却占满第五槽”的孤儿记录，再按五门
		// 真实神通逐一补齐；旧顺序会在满槽时永远无法创建最后一部功法。
		XjActorGongFaCollection.ReconcileWithActor(actor, "QiuJinGrade6Preclean");
		for (int i = 0; i < xianJi.Ids.Length; i++)
		{
			if (!string.IsNullOrWhiteSpace(xianJi.Ids[i]))
				XjActorGongFaCollection.EnsureForXianJi(actor, xianJi.Ids[i], currentYear, string.Empty, "求金前功法映射修复");
		}
		XjActorGongFaCollection.ReconcileWithActor(actor, "QiuJinGrade6Preflight");

		// 极早期存档可能已经拥有五门神通，但其中若干映射功法仍停在低品。
		// 只在主功法已到五品、五门神通真实存在时进行一次集合修复。
		if (!XjActorGongFaCollection.HasFiveRealGrade5GongFa(actor))
		{
			XjGongFaState main = XjGongFaAccessor.BuildState(actor);
			string daoTu = string.IsNullOrWhiteSpace(main.DaoTu)
				? (XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string storedDaoTu) ? storedDaoTu : string.Empty)
				: main.DaoTu;
			if (main.Found && main.Grade == 5 && !string.IsNullOrWhiteSpace(daoTu))
			{
				XjActorGongFaCollection.TryPrepareManualJinDanGrade5Set(actor, daoTu, "求金前五品集合修复", out _);
			}
		}
	}

	private static float HuiGuangCurveChance(float huiGuang, float minimum, float peak, float maximumChance)
	{
		if (huiGuang < minimum || peak <= minimum || maximumChance <= 0f)
		{
			return 0f;
		}

		float t = Math.Min(1f, Math.Max(0f, (huiGuang - minimum) / (peak - minimum)));
		float smooth = t * t * (3f - (2f * t));
		return Math.Min(maximumChance, Math.Max(0f, maximumChance * smooth));
	}

	private static int GetCurrentYear(Actor actor)
	{
		return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
	}
}
