using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Shi;

namespace XuanJianVNext.Systems.AutoCollect;

internal static class XjAutoCollectSystem
{
	internal static bool TryCollectRealm(Actor actor, string realmId, string source)
	{
		if (actor?.data == null || string.IsNullOrWhiteSpace(realmId))
		{
			return false;
		}

		if (!IsRealmCollectionEnabled(actor, realmId))
		{
			return false;
		}

		return MarkFavorite(actor, source);
	}


	internal static bool TryCollectShiRealm(Actor actor, string shiRealm, string source)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsShi(actor)
			|| string.IsNullOrWhiteSpace(shiRealm)) return false;
		int rank = XjShiCatalog.GetRank(shiRealm);
		if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm))
			return XjRuntimeSettings.AutoCollectShiDharmaFormEnabled && MarkFavorite(actor, source);
		if (rank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe))
			return XjRuntimeSettings.AutoCollectShiMoHeEnabled && MarkFavorite(actor, source);
		return false;
	}

	internal static bool TryCollectTianShouDaoMai(Actor actor, string source)
	{
		if (actor?.data == null || !XjRuntimeSettings.AutoCollectTianShouDaoMaiEnabled)
		{
			return false;
		}

		bool hasTianShou = actor.hasTrait("XjZz6");
		if (!hasTianShou
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude))
		{
			hasTianShou = aptitude == 6;
		}

		return hasTianShou && MarkFavorite(actor, source);
	}

	internal static bool TryCollectReincarnation(Actor actor, string mode)
	{
		if (actor?.data == null || !XjRuntimeSettings.ShouldAutoCollectReincarnation(mode)) return false;

		// 释修转世收藏只认摩诃。僧侣、法师以及它们的死亡恢复不属于
		// “摩诃轮回”这一高境事件，不能因为共用 ShiReincarnation 模式被误收藏。
		if (string.Equals(mode, "ShiReincarnation", StringComparison.Ordinal))
		{
			if (!XjCultivationPathRules.IsShi(actor)
				|| !XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot snapshot)
				|| !string.Equals(snapshot.Realm, XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				return false;
			}
		}

		return MarkFavorite(actor, mode);
	}

	internal static bool TryCollectFaBaoOwner(Actor actor, string source)
	{
		return XjRuntimeSettings.AutoCollectFaBaoOwnerEnabled && MarkFavorite(actor, source);
	}

	internal static bool TryCollectSwordImmortal(Actor actor, string source)
	{
		return XjRuntimeSettings.AutoCollectSwordImmortalEnabled && HasSwordImmortalTitle(actor) && MarkFavorite(actor, source);
	}

	internal static bool TryCollectKongZhengZhenJun(Actor actor, string source)
	{
		if (actor?.data == null
			|| !XjRuntimeSettings.AutoCollectKongZhengZhenJunEnabled
			|| !XjQingXuanKongZhengSystem.HasCompletedKongZheng(actor))
		{
			return false;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		return XjHighRealmIdentity.ResolveClass(realmId) == XjHighRealmClass.ZhenJun
			&& MarkFavorite(actor, source);
	}

	internal static bool HasAnnualInterest(Actor actor, string realmId = "")
	{
		if (actor?.data != null && XjDaoTaiPresenceArchive.IsBeyondWorld(((BaseSystemData)actor.data).id)) return false;
		if (actor?.data == null || ((BaseSystemData)actor.data).favorite)
		{
			return false;
		}

		if (XjCultivationPathRules.IsShi(actor)
			&& XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shiSnapshot))
		{
			int shiRank = XjShiCatalog.GetRank(shiSnapshot.Realm);
			return (shiRank >= XjShiCatalog.GetRank(XjShiRealmIds.DharmaForm)
				&& XjRuntimeSettings.AutoCollectShiDharmaFormEnabled)
				|| (shiRank >= XjShiCatalog.GetRank(XjShiRealmIds.MoHe)
					&& XjRuntimeSettings.AutoCollectShiMoHeEnabled);
		}

		if (string.IsNullOrWhiteSpace(realmId))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out realmId);
		}
		if (!string.IsNullOrWhiteSpace(realmId) && IsRealmCollectionEnabled(actor, realmId))
		{
			return true;
		}

		if (XjRuntimeSettings.AutoCollectKongZhengZhenJunEnabled
			&& XjQingXuanKongZhengSystem.HasCompletedKongZheng(actor)
			&& XjHighRealmIdentity.ResolveClass(realmId) == XjHighRealmClass.ZhenJun)
		{
			return true;
		}

		if (XjRuntimeSettings.AutoCollectTianShouDaoMaiEnabled)
		{
			if (actor.hasTrait("XjZz6"))
			{
				return true;
			}
			if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude)
				&& aptitude == 6)
			{
				return true;
			}
		}

		if (XjRuntimeSettings.AutoCollectFaBaoOwnerEnabled
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoId, out string faBaoId)
			&& !string.IsNullOrWhiteSpace(faBaoId))
		{
			return true;
		}

		if (XjRuntimeSettings.AutoCollectSwordImmortalEnabled && HasSwordImmortalTitle(actor))
		{
			return true;
		}

		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int reincarnationApplied)
			&& reincarnationApplied > 0
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationMode, out string mode)
			&& XjRuntimeSettings.ShouldAutoCollectReincarnation(mode);
	}

	internal static void TickActor(Actor actor, string realmId = "")
	{
		if (actor?.data == null)
		{
			return;
		}
		if (XjDaoTaiPresenceArchive.IsBeyondWorld(((BaseSystemData)actor.data).id)) return;

		if (!IsCollectableActor(actor))
		{
			ClearFavoriteIfInvalid(actor);
			return;
		}

		// Auto-collect is monotonic. Once the actor is already a favourite no
		// feature-specific fields need to be read again in future annual passes.
		if (((BaseSystemData)actor.data).favorite)
		{
			return;
		}

		if (XjCultivationPathRules.IsShi(actor)
			&& XjShiState.TryBuildSnapshot(actor, out XjShiSnapshot shiSnapshot))
		{
			TryCollectShiRealm(actor, shiSnapshot.Realm, "ShiRealmSetting");
			return;
		}

		if (string.IsNullOrWhiteSpace(realmId))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out realmId);
		}
		if (!string.IsNullOrWhiteSpace(realmId))
		{
			TryCollectRealm(actor, realmId, "RealmSetting");
		}
		if (XjRuntimeSettings.AutoCollectKongZhengZhenJunEnabled)
		{
			TryCollectKongZhengZhenJun(actor, "KongZhengZhenJunSetting");
		}
		if (XjRuntimeSettings.AutoCollectTianShouDaoMaiEnabled)
		{
			TryCollectTianShouDaoMai(actor, "TianShouDaoMai");
		}
		if (XjRuntimeSettings.AutoCollectFaBaoOwnerEnabled
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjFaBaoId, out string faBaoId)
			&& !string.IsNullOrWhiteSpace(faBaoId))
		{
			TryCollectFaBaoOwner(actor, "FaBaoOwnerSetting");
		}
		if (XjRuntimeSettings.AutoCollectSwordImmortalEnabled)
		{
			TryCollectSwordImmortal(actor, "SwordImmortalSetting");
		}
		if ((XjRuntimeSettings.ShouldAutoCollectReincarnation("JinDanReincarnation")
				|| XjRuntimeSettings.ShouldAutoCollectReincarnation("FaBaoOwnerReincarnation")
				|| XjRuntimeSettings.ShouldAutoCollectReincarnation("TianShouDaoMaiReincarnation"))
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjReincarnationApplied, out int reincarnationApplied)
			&& reincarnationApplied > 0
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjReincarnationMode, out string mode))
		{
			TryCollectReincarnation(actor, mode);
		}
	}

	private static bool HasSwordImmortalTitle(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjWeaponArtKind, out string kind)
			&& string.Equals(kind?.Trim(), XjWeaponArtKinds.Sword, StringComparison.Ordinal)
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjWeaponArtRank, out int rank)
			&& rank >= XjWeaponArtRanks.Yi;
	}

	private static bool IsRealmCollectionEnabled(Actor actor, string realmId)
	{
		string normalized = XjRealmHelper.NormalizeId(realmId);
		bool isFuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		if (string.Equals(normalized, XjRealmIds.ZhuJi, StringComparison.Ordinal))
		{
			return XjRuntimeSettings.AutoCollectZhuJiEnabled;
		}
		if (string.Equals(normalized, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			return XjRuntimeSettings.AutoCollectZiFuEnabled;
		}
		if (isFuQi && string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return XjRuntimeSettings.AutoCollectFuQiZhenRenEnabled;
		}
		if (isFuQi
			&& (string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal)
				|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal)))
		{
			return XjRuntimeSettings.AutoCollectFuQiZhenJunEnabled;
		}
		return !isFuQi
			&& (string.Equals(normalized, XjRealmIds.JinDan, StringComparison.Ordinal)
				|| string.Equals(normalized, XjRealmIds.ShenDan, StringComparison.Ordinal))
			&& XjRuntimeSettings.AutoCollectJinDanEnabled;
	}

	internal static bool ClearFavoriteIfInvalid(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		if (IsCollectableActor(actor))
		{
			return false;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		if (!data.favorite)
		{
			return false;
		}

		data.favorite = false;
		return true;
	}

	private static bool MarkFavorite(Actor actor, string source)
	{
		if (actor?.data != null && XjDaoTaiPresenceArchive.IsBeyondWorld(((BaseSystemData)actor.data).id)) return false;
		if (actor?.data == null || !IsCollectableActor(actor))
		{
			return false;
		}

		BaseSystemData data = (BaseSystemData)actor.data;
		if (data.favorite)
		{
			return false;
		}

		data.favorite = true;
		_ = source;
		return true;
	}

	private static bool IsCollectableActor(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor))
		{
			return false;
		}

		float health = XjSafeCore.GetHealthSafe(actor, -1f);
		if (health >= 0f)
		{
			return health > 0f;
		}

		return XjSafeCore.GetMaxHealthSafe(actor, 0f) > 0f;
	}
}
