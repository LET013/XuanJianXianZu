using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.History;

namespace XuanJianVNext.Systems.LongShu;

/// <summary>
/// 龙属与合水的专属法理耦合。
///
/// 原著口径被收束成三件运行时事实：
/// 1) 合水果位自龙属肇生后只认龙血；
/// 2) 龙属在世本身会令合水道势偏盛，真正修合水的龙属与合水龙君会进一步放大这种影响；
/// 3) 龙属循合水求金时得到血脉/道途同源加成，但仍需真实五神通、功法、位序与成功判定。
///
/// 本系统只遍历 KnownLongShuIds（当前设计最多三名），不扫描世界角色。
/// </summary>
internal static partial class XjLongShuSystem
{
	internal const string HeShuiDaoTuId = "合水";
	private const float HeShuiBaseJinDanBonus = 0.08f;
	private const float HeShuiFruitJinDanBonus = 0.05f;
	private const float HeShuiSiblingJinDanBonus = 0.015f;
	private const float HeShuiStrongMomentumJinDanBonus = 0.02f;
	private const float HeShuiMaximumJinDanBonus = 0.18f;

	private static int _heShuiInfluenceStage;

	internal static int HeShuiInfluenceStage => Math.Max(0, _heShuiInfluenceStage);

	internal static int ResolveHeShuiMomentumTarget(int naturalMomentum)
	{
		int natural = Math.Clamp(naturalMomentum, 0, 100);
		HeShuiInfluenceSnapshot snapshot = BuildHeShuiInfluenceSnapshot();
		if (snapshot.AliveLongShu <= 0) return natural;

		// 龙属现世后，合水不会再像一条完全无人问津的小道那样跌至极低道势；
		// 真正有合水龙属、双龙同应或龙君据果时，道势下限逐层抬高。
		int floor = 35;
		if (snapshot.HeShuiLongShu >= 1) floor = 55;
		if (snapshot.HeShuiLongShu >= 2) floor = 70;
		if (snapshot.HeShuiFruitHolderId > 0L) floor = 90;

		int bonus = snapshot.AliveLongShu * 4
			+ snapshot.HeShuiLongShu * 8
			+ (snapshot.HeShuiLongShu >= 2 ? 6 : 0)
			+ (snapshot.HeShuiFruitHolderId > 0L ? 20 : 0);
		return Math.Clamp(Math.Max(floor, natural + bonus), 0, 100);
	}

	/// <summary>
	/// 龙属循合水求金的固定加成。它抬高成功率和资质成功率上限，但不会绕过五门仙基、
	/// 六品功法、位序可用性、上修阻道或其他真正的求金门槛。
	/// </summary>
	internal static float ResolveHeShuiBreakthroughSuccessBonus(Actor actor, string manifestDaoTu, string positionType)
	{
		if (!IsLongShu(actor)
			|| !string.Equals((manifestDaoTu ?? string.Empty).Trim(), HeShuiDaoTuId, StringComparison.Ordinal))
		{
			return 0f;
		}

		HeShuiInfluenceSnapshot snapshot = BuildHeShuiInfluenceSnapshot();
		float bonus = HeShuiBaseJinDanBonus;
		int siblings = Math.Max(0, snapshot.AliveLongShu - 1);
		bonus += Math.Min(0.03f, siblings * HeShuiSiblingJinDanBonus);
		if (string.Equals(XjGuoWeiCalculator.NormalizePositionType(positionType),
			XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
		{
			bonus += HeShuiFruitJinDanBonus;
		}
		if (XjFruitPositionWorldState.GetMomentum(HeShuiDaoTuId) >= 75)
		{
			bonus += HeShuiStrongMomentumJinDanBonus;
		}
		return Math.Min(HeShuiMaximumJinDanBonus, Math.Max(0f, bonus));
	}

	private static void TickHeShuiInfluence(int currentYear)
	{
		if (currentYear <= 0) return;
		HeShuiInfluenceSnapshot snapshot = BuildHeShuiInfluenceSnapshot();
		int targetStage = ResolveHeShuiInfluenceStage(snapshot);
		if (targetStage <= _heShuiInfluenceStage) return;

		// 龙属同年成群现世时也不把数个世界级公告挤在同一年度；
		// 每年最多推进一层，让“龙脉引水→合水化泽→合水之征→龙君归合”成为真正长期事件链。
		int stage = Math.Min(targetStage, _heShuiInfluenceStage + 1);
		_heShuiInfluenceStage = stage;
		XjWorldArchiveSystem.MarkChanged();
		PublishHeShuiInfluenceEvent(stage, snapshot, currentYear);
		XjWorldArchiveSystem.RequestProtectedCommit();
	}

	private static int ResolveHeShuiInfluenceStage(in HeShuiInfluenceSnapshot snapshot)
	{
		if (snapshot.HeShuiFruitHolderId > 0L) return 4;
		if (snapshot.HeShuiLongShu >= 2) return 3;
		if (snapshot.HeShuiLongShu >= 1) return 2;
		if (snapshot.AliveLongShu >= 1) return 1;
		return 0;
	}

	private static HeShuiInfluenceSnapshot BuildHeShuiInfluenceSnapshot()
	{
		int alive = 0;
		int heShui = 0;
		long leadId = 0L;
		string leadName = string.Empty;
		long fruitHolderId = 0L;
		string fruitHolderName = string.Empty;

		for (int i = KnownLongShuIds.Count - 1; i >= 0; i--)
		{
			long actorId = KnownLongShuIds[i];
			if (!XjScheduler.ResolveActor(actorId, out Actor actor) || !IsAlive(actor) || !IsLongShu(actor))
			{
				continue;
			}

			alive++;
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
			if (!string.Equals((daoTu ?? string.Empty).Trim(), HeShuiDaoTuId, StringComparison.Ordinal))
			{
				continue;
			}

			heShui++;
			if (leadId <= 0L)
			{
				leadId = actorId;
				leadName = SafeLongShuName(actor);
			}

			XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.XjJinDanManifestDaoTu, out string jinDanManifestDaoTu);
			if (jinDan.Found
				&& string.Equals((jinDanManifestDaoTu ?? string.Empty).Trim(), HeShuiDaoTuId, StringComparison.Ordinal)
				&& string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(jinDan.GuoWei),
					XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal))
			{
				fruitHolderId = actorId;
				fruitHolderName = SafeLongShuName(actor);
			}
		}

		return new HeShuiInfluenceSnapshot(alive, heShui, leadId, leadName, fruitHolderId, fruitHolderName);
	}

	private static void PublishHeShuiInfluenceEvent(int stage, in HeShuiInfluenceSnapshot snapshot, int year)
	{
		string title;
		string body;
		long actorId = snapshot.LeadActorId;
		string actorName = snapshot.LeadActorName;

		switch (stage)
		{
			case 4:
				actorId = snapshot.HeShuiFruitHolderId;
				actorName = snapshot.HeShuiFruitHolderName;
				title = "【龙君归合】";
				body = "合水龙君既据果位，龙属旧脉与合位相扣，百川同赴、诸泽相并，合水汇聚之性遂压过寻常水德。自此龙属行水，合势更盛。";
				break;
			case 3:
				title = "【合水之征】";
				body = "两脉龙属同应合水，诸水之间渐染合势，江湖海泽彼此牵引，支流有归一之象。合水本喜汇聚，今得龙脉相助，其征遂见于五水之间。";
				break;
			case 2:
				title = "【合水化泽】";
				body = (string.IsNullOrWhiteSpace(actorName) ? "一脉龙属" : actorName)
					+ "循合水而修，所过江湖水意渐合，诸流相会而成泽。龙属与合水法理相应，本道道势自此受其牵引。";
				break;
			default:
				title = "【龙脉引水】";
				body = "龙属重现沧溟，九子遗脉虽分五水，血脉深处仍与合水相牵。诸水之间先起归流之势，合水道统因此不再全凭人间修士兴衰。";
				break;
		}

		XjWorldHistoryStore.RecordDomainEvent(
			XjWorldHistoryCategory.HighRealm,
			title,
			body,
			importance: stage >= 4 ? 5 : 4,
			isProtected: true,
			actorId: actorId,
			actorName: actorName,
			year: year,
			iconIdOverride: XjEventIconCatalog.LongShu,
			eventType: "LongShuHeShuiInfluence",
			mirrorToWorldLog: false);

		XjBroadcastSystem.ShowRecordedCategorizedWorldTipCritical(
			title + body,
			XjAnnouncementCategory.HighRealmInfluence,
			pause: false,
			position: "top",
			duration: stage >= 4 ? 14f : 11f,
			color: "#58BBD8",
			delayFrames: 1,
			iconId: XjEventIconCatalog.LongShu);
	}

	private static string SafeLongShuName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "无名龙属" : name.Trim();
		}
		catch
		{
			return "无名龙属";
		}
	}

	private readonly struct HeShuiInfluenceSnapshot
	{
		internal readonly int AliveLongShu;
		internal readonly int HeShuiLongShu;
		internal readonly long LeadActorId;
		internal readonly string LeadActorName;
		internal readonly long HeShuiFruitHolderId;
		internal readonly string HeShuiFruitHolderName;

		internal HeShuiInfluenceSnapshot(int aliveLongShu, int heShuiLongShu,
			long leadActorId, string leadActorName, long heShuiFruitHolderId, string heShuiFruitHolderName)
		{
			AliveLongShu = Math.Max(0, aliveLongShu);
			HeShuiLongShu = Math.Max(0, heShuiLongShu);
			LeadActorId = Math.Max(0L, leadActorId);
			LeadActorName = leadActorName ?? string.Empty;
			HeShuiFruitHolderId = Math.Max(0L, heShuiFruitHolderId);
			HeShuiFruitHolderName = heShuiFruitHolderName ?? string.Empty;
		}
	}
}
