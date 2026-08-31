using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Shi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 高境小境界的最终属性乘区。仅参与运行期属性重建，不写入特质、说明或角色详情。
/// 寿元由 XjRealmLifespanService 独立结算，绝不进入本乘区。
/// </summary>
internal static class XjRealmStageStatMultiplierService
{
	// 指纹写入角色数据仅用于检测小境界变化并触发一次属性重建；不参与任何界面展示。
	private const int FingerprintSchema = 5;
	private const int FingerprintSchemaBase = FingerprintSchema * 1000;

	internal static void MarkDirtyWhenStale(Actor actor)
	{
		if (actor?.data == null) return;
		Resolve(actor, out int desiredFingerprint, out _);
		int appliedFingerprint = ReadAppliedFingerprint(actor);
		if (desiredFingerprint == appliedFingerprint) return;
		try { actor.setStatsDirty(); } catch (System.Exception xjCaught27) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:27", xjCaught27); }
	}

	internal static bool TryResolveMultiplier(Actor actor, out int fingerprint, out float multiplier)
	{
		Resolve(actor, out fingerprint, out multiplier);
		return multiplier > 1f;
	}

	internal static void MarkApplied(Actor actor, int fingerprint)
	{
		if (actor?.data == null) return;
		try
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjRealmStageStatFingerprint, Math.Max(0, fingerprint));
		}
		catch (System.Exception xjCaught45) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:45", xjCaught45); }
	}

	private static void Resolve(Actor actor, out int fingerprint, out float multiplier)
	{
		fingerprint = FingerprintSchemaBase;
		multiplier = 1f;
		if (actor?.data == null) return;

		// 摩诃的每一世直接复用紫府一至五神通的最终属性乘区，既不复制仙道权限，
		// 又能让“世数=紫府小境界”真正体现在战斗数值上。
		if (XjCultivationPathRules.IsShi(actor))
		{
			bool isModernShi = XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiTradition,
				out string shiTradition)
				&& string.Equals(shiTradition, XjShiTraditionIds.Modern, StringComparison.Ordinal);
			int ownedHeavenFragments = isModernShi ? ResolveModernShiOwnedHeavenFragments(actor) : 0;
			if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.ShiRealm, out string shiRealm)
				&& string.Equals(shiRealm, XuanJianVNext.Data.Shi.XjShiRealmIds.MoHe, StringComparison.Ordinal))
			{
				int life = ResolveMoHeLife(actor);
				int band = life <= 2 ? 1 : life <= 4 ? 2 : life <= 6 ? 3 : life == 7 ? 4 : 5;
				fingerprint += 500 + band;
				multiplier = 1f + (band - 1) * 0.1f;
			}
			if (isModernShi)
			{
				// 0.9.9：今释法相/高位角色按同源金地掌握数强化；第一块为基准，其后每块 +10%。
				int additionalFragments = Math.Max(0, ownedHeavenFragments - 1);
				fingerprint += 600 + ownedHeavenFragments;
				multiplier *= 1f + additionalFragments * 0.1f;
			}
			return;
		}

		string realmId = ResolveExactRealmId(actor);
		if (string.Equals(realmId, XjRealmIds.ZiFu, StringComparison.Ordinal))
		{
			int count = ResolveZiFuShenTongCount(actor);
			int band = Math.Clamp(count, 1, 5);
			fingerprint += 100 + band;
			multiplier = 1f + (band - 1) * 0.1f;
			return;
		}

		if (string.Equals(realmId, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			int band = ResolveZhenRenBand(actor);
			fingerprint += 200 + band;
			// 真人的性命合炼按“紫府一至五神通”的同一最终属性乘区落地：
			// 初成真人=一神通档，圆满=五神通档。服气仍保留自身90%真人级基础模板，
			// 因此这里只补成长曲线，不把服气直接抬成紫金同面板。
			multiplier = 1f + (Math.Clamp(band, 1, 5) - 1) * 0.1f;
			return;
		}

		if (string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			int band = ResolveJinDanBand(actor);
			fingerprint += 300 + band;
			multiplier = ResolveGoldenRealmMultiplier(band);
			return;
		}

		if (string.Equals(realmId, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			int band = ResolveJinDanBand(actor);
			fingerprint += 400 + band;
			multiplier = ResolveGoldenRealmMultiplier(band);
		}
	}

	private static string ResolveExactRealmId(Actor actor)
	{
		if (actor?.data == null) return string.Empty;
		if (XjAnnualExecutionContext.TryResolveRealmOverride(actor, out string overrideRealmId))
		{
			return XjRealmHelper.NormalizeId(overrideRealmId);
		}
		if (XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			return XjRealmHelper.NormalizeId(realmId);
		}
		return string.Empty;
	}

	private static int ResolveZiFuShenTongCount(Actor actor)
	{
		try
		{
			int effective = XjXianJiAccessor.GetEffectiveShenTongCount(actor);
			if (effective > 0) return effective;
		}
		catch (System.Exception xjCaught114) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:114", xjCaught114); }
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjXianJiCount, out int stored))
		{
			return Math.Clamp(stored, 1, 5);
		}
		return 1;
	}

	internal static int ResolveZhenRenBand(Actor actor)
	{
		int currentYear = Math.Max(1, XjAnnualExecutionContext.ResolveYear(actor));
		int progress = 0;
		try { progress = XjFuQiToZiFuTransitionSystem.ResolveCoreProgressBasisPoints(actor, currentYear); }
		catch (System.Exception xjCaught127) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:127", xjCaught127); }

		// 与紫府的一至五神通成长档一一对应。性命合炼圆满必须进入第五档，
		// 不能像旧逻辑那样在75%后永久停在第四档。
		if (progress >= 10000) return 5;
		if (progress >= 7500) return 4;
		if (progress >= 5000) return 3;
		if (progress >= 2500) return 2;
		return 1;
	}

	/// <summary>
	/// 真人修持档映射到紫府的可读阶段。第二档仍属“初期”，与紫府第二神通
	/// 只提高最终属性而不单独改境界名的现行口径一致。
	/// </summary>
	internal static int ResolveZhenRenEquivalentStageOrder(Actor actor)
	{
		return ResolveZhenRenBand(actor) switch
		{
			1 => 1,
			2 => 1,
			3 => 2,
			4 => 4,
			_ => 5
		};
	}

	internal static int ResolveZhenRenEquivalentCombatLevel(Actor actor)
	{
		return ResolveZhenRenBand(actor) switch
		{
			1 => 19,
			2 => 19,
			3 => 20,
			4 => 22,
			_ => 23
		};
	}

	internal static string ResolveZhenRenStageDisplay(Actor actor)
	{
		return ResolveZhenRenBand(actor) switch
		{
			1 or 2 => "真人初期",
			3 => "真人中期",
			4 => "真人后期",
			_ => "真人巅峰"
		};
	}


	private static int ResolveMoHeLife(Actor actor)
	{
		int currentLife = 1;
		int completedLives = 0;
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCurrentLife, out currentLife); }
		catch (System.Exception ex) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjRealmStageStatMultiplierService.ResolveMoHeLife.current", ex); }
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiCompletedLives, out completedLives); }
		catch (System.Exception ex) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjRealmStageStatMultiplierService.ResolveMoHeLife.completed", ex); }
		return Math.Clamp(Math.Max(currentLife, completedLives + 1), 1, 9);
	}

	private static int ResolveModernShiOwnedHeavenFragments(Actor actor)
	{
		if (actor?.data == null) return 0;
		if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ShiOwnedHeavenFragments, out int owned)) return 0;
		return Math.Max(0, owned);
	}

	private static int ResolveJinDanBand(Actor actor)
	{
		int yiXiang = 0;
		try { XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out yiXiang); }
		catch (System.Exception xjCaught139) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:139", xjCaught139); }
		try { yiXiang = XjFaBaoBonusService.GetEffectiveJinDanYiXiang(actor, Math.Max(0, yiXiang)); }
		catch (System.Exception xjCaught141) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Combat/XjRealmStageStatMultiplierService.cs:141", xjCaught141); }

		if (yiXiang >= 6000) return 4;
		if (yiXiang >= 3000) return 3;
		if (yiXiang >= 1000) return 2;
		return 1;
	}

	private static float ResolveGoldenRealmMultiplier(int band)
	{
		return band switch
		{
			2 => 1.2f,
			3 => 1.4f,
			4 => 1.6f,
			_ => 1f
		};
	}

	private static int ReadAppliedFingerprint(Actor actor)
	{
		if (actor?.data == null) return 0;
		try
		{
			((BaseSystemData)actor.data).get(
				XjActorDataKeys.XjRealmStageStatFingerprint,
				out int value,
				0);
			return Math.Max(0, value);
		}
		catch
		{
			return 0;
		}
	}
}
