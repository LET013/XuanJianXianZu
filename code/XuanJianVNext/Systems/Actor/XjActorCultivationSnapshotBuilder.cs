using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.ActorSystem;

internal readonly struct XjActorCultivationSnapshot
{
	internal static XjActorCultivationSnapshot Empty { get; } = new XjActorCultivationSnapshot(
		string.Empty,
		string.Empty,
		0f,
		0f,
		0f,
		0,
		0,
		0,
		false);

	internal readonly string RealmId;
	internal readonly string DaoTu;
	internal readonly float ZhenYuan;
	internal readonly float MingShu;
	internal readonly float HuiGuang;
	internal readonly int XjZz;
	internal readonly int XjZzOverlayMask;
	internal readonly int XianJiCount;
	internal readonly bool HasQiuJinFa;

	internal XjActorCultivationSnapshot(
		string realmId,
		string daoTu,
		float zhenYuan,
		float mingShu,
		float huiGuang,
		int xjZz,
		int xjZzOverlayMask,
		int xianJiCount,
		bool hasQiuJinFa)
	{
		RealmId = realmId ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ZhenYuan = zhenYuan;
		MingShu = mingShu;
		HuiGuang = huiGuang;
		XjZz = xjZz;
		XjZzOverlayMask = xjZzOverlayMask;
		XianJiCount = xianJiCount;
		HasQiuJinFa = hasQiuJinFa;
	}

	internal XjActorCultivationSnapshot WithZhenYuan(float zhenYuan)
	{
		return new XjActorCultivationSnapshot(
			RealmId,
			DaoTu,
			zhenYuan,
			MingShu,
			HuiGuang,
			XjZz,
			XjZzOverlayMask,
			XianJiCount,
			HasQiuJinFa);
	}
}

internal static class XjActorCultivationSnapshotBuilder
{
	internal static XjActorCultivationSnapshot BuildAnnualProgression(Actor actor, int realmTier)
	{
		bool full = realmTier >= XjRealmSuppression.TierZiFu;
		XjStageZeroObservation.RecordAnnualSnapshotBuild(full);
		return BuildInternal(actor, full);
	}

	internal static XjActorCultivationSnapshot Build(Actor actor)
	{
		return BuildInternal(actor, includeHighRealmState: true);
	}

	private static XjActorCultivationSnapshot BuildInternal(Actor actor, bool includeHighRealmState)
	{
		if (actor == null)
		{
			return XjActorCultivationSnapshot.Empty;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.ZhenYuan, out float zhenYuan);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.MingShu, out float mingShu);
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float huiGuang);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int xjZz);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZzOverlayMask, out int xjZzOverlayMask);
		int xianJiCount = 0;
		bool hasQiuJinFa = false;
		if (includeHighRealmState)
		{
			xianJiCount = XjXianJiAccessor.BuildState(actor).Count;
			XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjQiuJinFaReady, out int qiuJinFaReady);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjQiuJinFaName, out string qiuJinFaName);
			hasQiuJinFa = qiuJinFaReady == 1 && !string.IsNullOrWhiteSpace(qiuJinFaName);
		}
		XjFaBaoBonusService.ApplyEffectiveCultivationStats(actor, ref mingShu, ref huiGuang);

		return new XjActorCultivationSnapshot(
			realmId,
			daoTu,
			zhenYuan,
			mingShu,
			huiGuang,
			xjZz,
			xjZzOverlayMask,
			xianJiCount,
			hasQiuJinFa);
	}
}
