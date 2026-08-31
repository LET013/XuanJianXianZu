using System;
using System.Collections.Generic;
using XuanJianVNext.Data.Death;
using XuanJianVNext.Data.Rules;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 金丹死亡遗留金性从死亡主归档中拆出，每帧最多处理一条。
/// 避免仓库、纪事、公告与保护存档在同一死亡帧叠加。
/// </summary>
internal static class XjJinDanResidualDeathLane
{
	private const int MaxRecentKeys = 2048;
	private static readonly Queue<XjDeathSnapshot> Pending = new Queue<XjDeathSnapshot>();
	private static readonly HashSet<string> PendingKeys = new HashSet<string>(StringComparer.Ordinal);
	private static readonly Queue<string> RecentOrder = new Queue<string>();
	private static readonly HashSet<string> RecentKeys = new HashSet<string>(StringComparer.Ordinal);

	internal static bool HasPending => Pending.Count > 0;

	internal static void Enqueue(in XjDeathSnapshot snapshot)
	{
		if (!snapshot.Found || snapshot.ActorId <= 0L) return;
		bool eligible = XjCultivationPathRules.IsJinDanEquivalentRealm(snapshot.RealmId)
			|| snapshot.IsJieLinXian
			|| snapshot.IsYuYiXian;
		if (!eligible || snapshot.FamilyStableId <= 0L) return;
		string key = BuildKey(snapshot);
		if (RecentKeys.Contains(key) || !PendingKeys.Add(key)) return;
		Pending.Enqueue(snapshot);
	}

	internal static void Tick(int budget)
	{
		int safeBudget = Math.Max(0, Math.Min(1, budget));
		for (int i = 0; i < safeBudget && Pending.Count > 0; i++)
		{
			XjDeathSnapshot snapshot = Pending.Dequeue();
			string key = BuildKey(snapshot);
			PendingKeys.Remove(key);
			try
			{
				XjJinDanResidualDeathHandler.Handle(snapshot);
			}
			catch (Exception ex)
			{
				UnityEngine.Debug.LogWarning("[玄鉴][金性遗留] 延迟结算失败: " + ex.GetType().Name + ": " + ex.Message);
			}
			finally
			{
				// 同一死亡快照只尝试一次，避免后续重复死亡回调造成双重入库。
				Remember(key);
			}
		}
	}

	internal static void Clear()
	{
		Pending.Clear();
		PendingKeys.Clear();
		RecentOrder.Clear();
		RecentKeys.Clear();
	}

	private static void Remember(string key)
	{
		if (string.IsNullOrEmpty(key) || !RecentKeys.Add(key)) return;
		RecentOrder.Enqueue(key);
		while (RecentOrder.Count > MaxRecentKeys) RecentKeys.Remove(RecentOrder.Dequeue());
	}

	private static string BuildKey(in XjDeathSnapshot snapshot)
	{
		return snapshot.ActorId + "|" + snapshot.Year + "|" + snapshot.JinXing;
	}
}
