using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.UI.ActorInfo;

namespace XuanJianVNext.UI.Rank;

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
	internal static IReadOnlyList<XjRankItem> Build(int realmFilter = 0)
	{
		if (realmFilter == 5)
		{
			return BuildJinDanOnly();
		}
		return BuildFromKnownActors();
	}

	private static IReadOnlyList<XjRankItem> BuildFromKnownActors()
	{
		IReadOnlyList<long> cachedIds = XjCultivatorCache.GetAllIds();
		HashSet<long> actorIds = new HashSet<long>(cachedIds);
		IReadOnlyList<Actor> registry = XjActorRegistry.Snapshot();
		for (int i = 0; i < registry.Count; i++)
		{
			Actor known = registry[i];
			if (known?.data == null || !IsValidActor(known)) continue;
			long knownId = ((BaseSystemData)known.data).id;
			if (knownId > 0L && (XjRealmSuppression.GetRealmTier(known) > XjRealmSuppression.TierNone
				|| XjActorAccessor.TryGetInt(known, XjActorDataKeys.XjZz, out int aptitude) && aptitude > 0)) actorIds.Add(knownId);
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

			XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
			XjActorInfoReadModel info = XjActorInfoReadModel.BuildForActor(actor);
			if (!info.Found || string.IsNullOrWhiteSpace(info.RealmId))
			{
				continue;
			}

			XjRankMetricSnapshot metrics = XjRankMetrics.Build(actor);
			string realmDisplay = XjRankMetrics.ResolveRealmDisplay(in metrics);
			if (string.IsNullOrWhiteSpace(realmDisplay)) realmDisplay = info.RealmDisplay;
			string daoTuDisplay = ResolveDaoTuDisplay(actor, info.DaoTu, metrics.RealmId);
			items.Add(new XjRankItem(
				actor,
				SafeActorName(actor),
				metrics.RealmId,
				string.IsNullOrWhiteSpace(realmDisplay) ? "未入道" : realmDisplay,
				daoTuDisplay,
				SafeAge(actor),
				metrics.Aptitude,
				metrics.ZhenYuan,
				info.GongFaSummary,
				info.JinDanSummary,
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

			XjRealmTitleApplyService.EnsureCurrentRealmProjection(actor);
			XjActorInfoReadModel info = XjActorInfoReadModel.BuildForActor(actor);
			XjRankMetricSnapshot metrics = XjRankMetrics.Build(actor);
			string realmDisplay = XjRankMetrics.ResolveRealmDisplay(in metrics);
			if (string.IsNullOrWhiteSpace(realmDisplay)) realmDisplay = info.RealmDisplay;
			string daoTuDisplay = ResolveDaoTuDisplay(actor, info.DaoTu, metrics.RealmId);
			string jinDan = info.JinDanSummary;
			if (!string.IsNullOrWhiteSpace(record.YinSiState))
			{
				jinDan = string.IsNullOrWhiteSpace(jinDan)
					? "阴司：" + record.YinSiState
					: jinDan + " - 阴司：" + record.YinSiState;
			}
			items.Add(new XjRankItem(
				actor,
				string.IsNullOrWhiteSpace(info.ActorName) ? SafeActorName(actor) : info.ActorName,
				metrics.RealmId,
				string.IsNullOrWhiteSpace(realmDisplay) ? "金丹" : realmDisplay,
				daoTuDisplay,
				SafeAge(actor),
				metrics.Aptitude,
				metrics.ZhenYuan,
				info.GongFaSummary,
				jinDan,
				metrics.Power));
		}
		return items;
	}

	private static string ResolveDaoTuDisplay(Actor actor, string daoTu, string realmId)
	{
		if (string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			&& XjCaiQiActorAccessor.IsLianQiByZaQi(actor))
		{
			return "杂气";
		}

		return (daoTu ?? string.Empty).Trim();
	}

	private static bool IsValidActor(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		try
		{
			return XjSafeCore.IsAliveActor(actor);
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

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
		}
		catch
		{
			return "未名修士";
		}
	}
}
