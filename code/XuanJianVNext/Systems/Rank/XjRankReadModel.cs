using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.YaoShu;
using XuanJianVNext.Data.Rules;

using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.Systems.LongShu;
namespace XuanJianVNext.Systems.Rank;

internal readonly struct XjRankItem
{
	internal readonly Actor Actor;
	internal readonly string Name;
	internal readonly string RealmId;
	internal readonly string RealmDisplay;
	internal readonly string DaoTu;
	internal readonly int Age;
	internal readonly int XjZz;
	internal readonly float ZhenYuan;
	internal readonly string GongFa;
	internal readonly string JinDan;
	internal readonly double Power;

	internal XjRankItem(
		Actor actor,
		string name,
		string realmId,
		string realmDisplay,
		string daoTu,
		int age,
		int xjZz,
		float zhenYuan,
		string gongFa,
		string jinDan,
		double power)
	{
		Actor = actor;
		Name = name ?? string.Empty;
		RealmId = realmId ?? string.Empty;
		RealmDisplay = realmDisplay ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Age = age < 0 ? 0 : age;
		XjZz = xjZz < 0 ? 0 : xjZz;
		ZhenYuan = zhenYuan;
		GongFa = gongFa ?? string.Empty;
		JinDan = jinDan ?? string.Empty;
		Power = power < 0d ? 0d : power;
	}
}

internal static class XjRankReadModel
{
	private const double CacheLifetimeMilliseconds = 15000d;
	private static IReadOnlyList<XjRankItem> _cachedAll = Array.Empty<XjRankItem>();
	private static int _cachedCultivatorRevision = -1;
	private static int _cachedGreatSageRevision = -1;
	private static long _cachedTimestamp;
	private static int _cachedObservedYear = -1;


	internal static void InvalidateCache()
	{
		_cachedAll = Array.Empty<XjRankItem>();
		_cachedCultivatorRevision = -1;
		_cachedGreatSageRevision = -1;
		_cachedTimestamp = 0L;
		_cachedObservedYear = -1;
	}

	internal static IReadOnlyList<XjRankItem> Build(int realmFilter = 0)
	{
		// 大圣只维护固定十二个槽位。榜单打开时做一次定额回填，避免刚化生或刚载档时
		// 因年度调度尚未走到而暂时漏出修士候选集；不扫描世界 Actor。
		XjYaoShuGreatSageSystem.EnsureRankMembership();
		// realmFilter 由排行榜 UI 在统一 ReadModel 上做等阶筛选。旧实现把 order=5
		// 特判成“只读金丹不朽注册表”，会把同为五阶的真君羽士/妖属大圣全部排除。
		int cultivatorRevision = XjCultivatorCache.MembershipRevision;
		int greatSageRevision = XjYaoShuGreatSageSystem.RankMembershipRevision;
		int observedYear = Math.Max(0, XjYearTracker.CurrentYear);
		if (_cachedObservedYear >= 0 && observedYear < _cachedObservedYear)
		{
			InvalidateCache();
		}
		// 高倍速下世界年会数秒内跳过多次。排行榜只读快照若把“年份变化”
		// 作为硬失效条件，会周期性遍历全部修士并制造肉眼可见的卡顿。
		// 年龄与排序允许最多15秒显示延迟；成员身份变化（包括人数不变的替换）立即重建。
		if (_cachedCultivatorRevision == cultivatorRevision
			&& _cachedGreatSageRevision == greatSageRevision
			&& _cachedTimestamp > 0L
			&& ElapsedMilliseconds(_cachedTimestamp) <= CacheLifetimeMilliseconds)
		{
			return _cachedAll;
		}
		_cachedAll = BuildFromKnownActors();
		_cachedCultivatorRevision = cultivatorRevision;
		_cachedGreatSageRevision = greatSageRevision;
		_cachedObservedYear = observedYear;
		_cachedTimestamp = Stopwatch.GetTimestamp();
		return _cachedAll;
	}

	private static IReadOnlyList<XjRankItem> BuildFromKnownActors()
	{
		IReadOnlyList<long> cultivatorIds = XjCultivatorCache.GetAllIds();
		IReadOnlyList<long> greatSageIds = XjYaoShuGreatSageSystem.GetRankActorIds();
		HashSet<long> actorIds = new HashSet<long>();
		for (int i = 0; i < cultivatorIds.Count; i++)
		{
			if (cultivatorIds[i] > 0L) actorIds.Add(cultivatorIds[i]);
		}
		for (int i = 0; i < greatSageIds.Count; i++)
		{
			if (greatSageIds[i] > 0L) actorIds.Add(greatSageIds[i]);
		}

		List<XjRankItem> items = new List<XjRankItem>(actorIds.Count);
		foreach (long actorId in actorIds)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor))
			{
				continue;
			}
			if (!IsValidActor(actor))
			{
				continue;
			}

			XjRankMetricSnapshot metrics = XjRankMetrics.Build(actor);
			string realmDisplay = XjHouShenShuSystem.DecorateRealmDisplay(actor, XjRankMetrics.ResolveRealmDisplay(in metrics));
			if (XjXianGuoSystem.TryGetCourtRankRealmDisplay(actor, out string courtRealmDisplay))
			{
				realmDisplay = courtRealmDisplay;
			}
			string daoTuDisplay = ResolveDaoTuDisplay(actor, metrics.DaoTu, metrics.RealmId);
			items.Add(new XjRankItem(
				actor,
				XjStringHelper.ActorName(actor, "未名修士"),
				metrics.RealmId,
				string.IsNullOrWhiteSpace(realmDisplay) ? "未入道" : realmDisplay,
				daoTuDisplay,
				SafeAge(actor),
				metrics.Aptitude,
				metrics.ZhenYuan,
				string.Empty,
				string.Empty,
				metrics.Power));
		}

		return items;
	}

	private static IReadOnlyList<XjRankItem> BuildJinDanOnly()
	{
		IReadOnlyList<XuanJianVNext.Data.HighRealm.XjJinDanImmortalityArchiveRecord> records = XjJinDanImmortalityRegistry.ReadAll();
		List<XjRankItem> items = new List<XjRankItem>(records.Count);
		for (int i = 0; i < records.Count; i++)
		{
			XuanJianVNext.Data.HighRealm.XjJinDanImmortalityArchiveRecord record = records[i];
			if (record == null || !record.IsAlive || !XjActorRegistry.ResolveKnownOrWorld(record.ActorId, out Actor actor) || !IsValidActor(actor))
			{
				continue;
			}

			XjRankMetricSnapshot metrics = XjRankMetrics.Build(actor);
			string realmDisplay = XjHouShenShuSystem.DecorateRealmDisplay(actor, XjRankMetrics.ResolveRealmDisplay(in metrics));
			if (XjXianGuoSystem.TryGetCourtRankRealmDisplay(actor, out string courtRealmDisplay))
			{
				realmDisplay = courtRealmDisplay;
			}
			string daoTuDisplay = ResolveDaoTuDisplay(actor, metrics.DaoTu, metrics.RealmId);
			string jinDan = string.Empty;
			if (!string.IsNullOrWhiteSpace(record.YinSiState))
			{
				jinDan = "阴司：" + XjDisplayNameSanitizer.Clean(record.YinSiState, "未载");
			}
			items.Add(new XjRankItem(
				actor,
				XjStringHelper.ActorName(actor, "未名修士"),
				metrics.RealmId,
				string.IsNullOrWhiteSpace(realmDisplay) ? "金丹" : realmDisplay,
				daoTuDisplay,
				SafeAge(actor),
				metrics.Aptitude,
				metrics.ZhenYuan,
				string.Empty,
				jinDan,
				metrics.Power));
		}
		return items;
	}

	private static double ElapsedMilliseconds(long timestamp)
	{
		return (Stopwatch.GetTimestamp() - timestamp) * 1000d / Stopwatch.Frequency;
	}

	private static string ResolveDaoTuDisplay(Actor actor, string daoTu, string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& XjCaiQiActorAccessor.IsLianQiByZaQi(actor))
		{
			return "杂气";
		}

		// 排行榜展示的是角色当前已经显化出来的道途，而不是证位前的根本道途。
		// 例如“坎水闰渊照”在角色数据中仍保留 DaoTu=坎水 作为根道，
		// 但金丹/道胎位序身份已经固化 SourceDaoTu=坎水、ManifestDaoTu=渊照。
		// 人物详情页一直按 ManifestDaoTu 展示；排行榜此前直接读 metrics.DaoTu，
		// 因而错误回退成坎水。这里与人物详情统一，以合法闰位显道为最高优先级。
		XjJinDanState position = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (position.Found
			&& string.Equals(
				XjGuoWeiRegistry.ResolveTypeFromName(position.GuoWei),
				XjGuoWeiCalculator.RunWei,
				StringComparison.Ordinal))
		{
			XjHighRealmDaoStateService.ResolvePositionIdentity(
				actor, position.GuoWei, out _, out string manifestDaoTu);
			manifestDaoTu = (manifestDaoTu ?? string.Empty).Trim();
			if (manifestDaoTu.Length > 0)
			{
				return XjXianGuoSystem.ResolveDaoTuDisplay(actor, manifestDaoTu);
			}
		}

		return XjXianGuoSystem.ResolveDaoTuDisplay(actor, (daoTu ?? string.Empty).Trim());
	}

	private static bool IsValidActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		try
		{
			// 榜单作为只读出口也执行同一物种硬门槛，避免旧档尚未完成年度清理时
			// 把自定义种族/动物的历史修炼状态继续显示成有效玄鉴修士。
			return XjSafeCore.IsAliveActor(actor)
				&& (XjCultivationEligibility.IsSupportedNativeCultivationSpecies(actor)
					|| XjLongShuSystem.IsLongShu(actor)
					|| XjYaoShuGreatSageSystem.IsGreatSage(actor)
					|| XjYaoShuSapientSpecies.IsYaoMin(actor));
		}
		catch
		{
			return false;
		}
	}

	private static int SafeAge(Actor actor)
	{
		try
		{
			return actor == null ? 0 : (int)Math.Floor(Math.Max(0f, actor.getAge()));
		}
		catch
		{
			return 0;
		}
	}
}
