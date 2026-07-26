using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;

namespace XuanJianVNext.UI.ShenBing;

internal readonly struct XjFaBaoRankEntry
{
	internal readonly string FaBaoId;
	internal readonly string Name;
	internal readonly string ClassName;
	internal readonly string DaoTu;
	internal readonly string Kind;
	internal readonly string Role;
	internal readonly string IconPath;
	internal readonly int AffixCount;
	internal readonly int Power;
	internal readonly string OwnerName;
	internal readonly long OwnerId;
	internal readonly bool IsJinDan;
	internal readonly bool IsFaQi;

	internal XjFaBaoRankEntry(string faBaoId, string name, string className, string daoTu, string kind,
		string role, string iconPath, int affixCount, int power, string ownerName, long ownerId)
	{
		FaBaoId = faBaoId ?? string.Empty;
		Name = name ?? string.Empty;
		ClassName = className ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		Kind = kind ?? string.Empty;
		Role = XjFaBaoCatalog.NormalizeRole(Kind, role);
		IconPath = iconPath ?? string.Empty;
		AffixCount = Math.Max(0, affixCount);
		Power = Math.Max(0, power);
		OwnerName = ownerName ?? string.Empty;
		OwnerId = ownerId;
		IsJinDan = XjFaBaoCatalog.IsJinDanFaBao(ClassName);
		IsFaQi = XjFaBaoCatalog.IsZhuJiFaQi(ClassName);
	}
}

internal static class XjShenBingReadModel
{
	internal static List<XjFaBaoRankEntry> BuildRanking(string filter = "")
	{
		List<XjFaBaoRankEntry> result = new();
		HashSet<string> dedup = new(StringComparer.Ordinal);
		IReadOnlyList<Actor> actors = GetKnownActors();
		if (actors == null) return result;

		for (int i = 0; i < actors.Count; i++)
		{
			Actor actor = actors[i];
			if (actor?.data == null || !actor.isAlive()) continue;

			XjFaBaoState primary = XjFaBaoAccessor.BuildState(actor);
			TryAddEntry(result, dedup, actor, primary, filter, string.Empty);
			TryAddEquipmentSlotEntries(result, dedup, actor, filter);
		}

		result.Sort((left, right) =>
		{
			int power = right.Power.CompareTo(left.Power);
			if (power != 0) return power;
			int owner = left.OwnerId.CompareTo(right.OwnerId);
			if (owner != 0) return owner;
			int name = string.Compare(left.Name, right.Name, StringComparison.Ordinal);
			return name != 0 ? name : string.Compare(left.FaBaoId, right.FaBaoId, StringComparison.Ordinal);
		});
		return result;
	}

	private static void TryAddEquipmentSlotEntries(
		List<XjFaBaoRankEntry> result,
		HashSet<string> dedup,
		Actor actor,
		string filter)
	{
		if (actor.equipment == null) return;
		foreach (ActorEquipmentSlot slot in actor.equipment)
		{
			Item item = slot?.getItem();
			if (item?.data == null) continue;
			EquipmentAsset asset = item.getAsset();
			string iconPath = asset == null ? string.Empty : ((BaseUnlockableAsset)asset).path_icon ?? string.Empty;
			if (XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState state))
			{
				TryAddEntry(result, dedup, actor, state, filter, iconPath);
				continue;
			}

			// 兼容旧存档中已标记、但缺失完整 state.id 的防御灵宝。
			// 万宝录只做只读补显，不在打开窗口时回写物品。
			if (!TryBuildLegacyEquipmentState(item, asset, actor, out state)) continue;
			TryAddEntry(result, dedup, actor, state, filter, iconPath);
		}
	}

	private static void TryAddEntry(
		List<XjFaBaoRankEntry> result,
		HashSet<string> dedup,
		Actor actor,
		in XjFaBaoState state,
		string filter,
		string iconPath)
	{
		if (!state.Found || string.IsNullOrWhiteSpace(state.Id)) return;
		string role = XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role);
		if (!MatchesFilter(state.ClassName, role, filter)) return;

		long ownerId = ((BaseSystemData)actor.data).id;
		string key = ownerId + "|" + state.Id + "|" + state.Kind + "|" + state.ClassName;
		if (!dedup.Add(key)) return;

		result.Add(new XjFaBaoRankEntry(
			state.Id,
			state.Name,
			state.ClassName,
			state.DaoTu,
			state.Kind,
			role,
			iconPath,
			CountAffixes(state.Affixes),
			XjFaBaoBonusService.GetOverviewPower(state),
			actor.getName() ?? "无名",
			ownerId));
	}

	private static bool TryBuildLegacyEquipmentState(Item item, EquipmentAsset asset, Actor actor, out XjFaBaoState state)
	{
		state = XjFaBaoState.Empty;
		if (item?.data == null || asset == null || actor?.data == null) return false;
		string assetId = ((Asset)asset).id ?? string.Empty;
		if (!XjFaBaoEquipmentAssets.TryResolveKind(assetId, out string kind, out string role)) return false;
		if (!XjFaBaoEquipmentSync.TryReadFaBaoItem(item, out string name, out string className, out string description, out string affixes)) return false;
		XjFaBaoEquipmentSync.TryReadFaBaoItemId(item, out string faBaoId);
		if (string.IsNullOrWhiteSpace(faBaoId))
		{
			faBaoId = "legacy-slot-" + ((BaseSystemData)actor.data).id + "-" + asset.equipment_type;
		}
		state = new XjFaBaoState(
			true, faBaoId, name, string.Empty, className, kind, role, affixes, description,
			"LegacyEquippedItem", 0, "ReadOnlyRecovered");
		return true;
	}

	private static bool MatchesFilter(string className, string role, string filter)
	{
		string value = (filter ?? string.Empty).Trim();
		if (value.Length == 0 || string.Equals(value, "全部器物", StringComparison.Ordinal)
			|| string.Equals(value, "全部法宝", StringComparison.Ordinal)) return true;
		if (string.Equals(value, "筑基法器", StringComparison.Ordinal)
			|| string.Equals(value, "紫府灵宝", StringComparison.Ordinal)
			|| string.Equals(value, "金丹法宝", StringComparison.Ordinal))
			return string.Equals(className, value, StringComparison.Ordinal);
		if (string.Equals(value, "攻击类", StringComparison.Ordinal))
			return string.Equals(role, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal);
		if (string.Equals(value, "防御类", StringComparison.Ordinal))
			return string.Equals(role, XjFaBaoCatalog.RoleDefense, StringComparison.Ordinal);
		if (string.Equals(value, "附属类", StringComparison.Ordinal))
			return string.Equals(role, XjFaBaoCatalog.RoleSupport, StringComparison.Ordinal);
		return true;
	}

	private static int CountAffixes(string affixes)
	{
		return string.IsNullOrWhiteSpace(affixes)
			? 0
			: affixes.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries).Length;
	}

	private static IReadOnlyList<Actor> GetKnownActors()
	{
		try
		{
			if (XuanJianVNext.Systems.Runtime.XjWorldBootstrapLane.HasPending)
			{
				return XuanJianVNext.Core.XjScheduler.GetKnownActorsSnapshot();
			}

			List<long> actorIds = XuanJianVNext.Core.XjRuntimeActorInterestIndex.GetFaBaoActorIdsSnapshot();
			if (actorIds.Count == 0)
			{
				return Array.Empty<Actor>();
			}

			List<Actor> actors = new List<Actor>(actorIds.Count);
			for (int i = 0; i < actorIds.Count; i++)
			{
				if (XuanJianVNext.Core.XjActorRegistry.ResolveKnownOrWorld(actorIds[i], out Actor actor)
					&& actor?.data != null
					&& actor.isAlive())
				{
					actors.Add(actor);
				}
			}
			return actors;
		}
		catch (Exception exception)
		{
			Debug.LogError("[玄鉴] 万宝录读取角色快照失败：" + exception);
			return null;
		}
	}
}
