using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
		private static float CalculateTriggerChance(Actor actor, in XjActorCultivationSnapshot snapshot)
		{
			if (actor?.data == null)
			{
				return 0f;
			}

			// 0.5.4 语义：金丹年度触发率主要受修炼资质影响。
			// XjZz5/6 才能尝试金丹；道主在调用方单独强制放行。
			return ResolveJinDanAptitudeTriggerChance(snapshot.XjZz);
		}

		private static float CalculateSuccessChance(Actor actor, in XjActorCultivationSnapshot snapshot)
		{
			// 0.5.4 语义：命数决定承载力，资质决定成功率上限。
			// 当前版本保留更严格的五法门槛，避免四本功法时误入求金/成丹链路。
			float maxChance = ResolveJinDanAptitudeSuccessCap(snapshot.XjZz);
			float chance = maxChance * XjBreakthroughRules.CalculateMingShuFactor(actor, XjRealmIds.JinDan);
			if (XjRenDan.HasRenDanShenTongAcquired(actor))
			{
				// 人丹是对突破成功率的固定减值，不是再乘一个模糊系数。
				chance -= XjRenDanRules.JinDanPenaltyRate;
			}
			return Mathf.Clamp01(chance);
		}

		private static float ResolveJinDanAptitudeSuccessCap(int xjZz)
		{
			return xjZz switch
			{
				6 => 0.40f,
				5 => 0.30f,
				_ => 0f
			};
		}

		private static float ResolveJinDanAptitudeTriggerChance(int xjZz)
		{
			return xjZz switch
			{
				6 => 0.90f,
				5 => 0.80f,
				_ => 0f
			};
		}


		private static int PositiveRollBasisPoints(long actorId, int currentYear, string salt)
		{
			unchecked
			{
				ulong hash = 14695981039346656037UL;
				hash ^= (ulong)actorId;
				hash *= 1099511628211UL;
				hash ^= (ulong)Math.Max(0, currentYear);
				hash *= 1099511628211UL;
				string text = salt ?? string.Empty;
				for (int i = 0; i < text.Length; i++)
				{
					hash ^= text[i];
					hash *= 1099511628211UL;
				}

				return (int)((hash & 0x7FFFFFFFFFFFFFFFUL) % 10000UL);
			}
		}

		private static int PositiveRoll(long actorId, int currentYear, string salt)
		{
			unchecked
			{
				ulong hash = 14695981039346656037UL;
				hash ^= (ulong)actorId;
				hash *= 1099511628211UL;
				hash ^= (ulong)Math.Max(0, currentYear);
				hash *= 1099511628211UL;
				string text = salt ?? string.Empty;
				for (int i = 0; i < text.Length; i++)
				{
					hash ^= text[i];
					hash *= 1099511628211UL;
				}

				return (int)((hash & 0x7FFFFFFFFFFFFFFFUL) % 100UL);
			}
		}

		private static long GetActorId(Actor actor)
		{
			return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		}

		private static int GetCurrentYear(Actor actor)
		{
			return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
		}

		private static int GetWorldYear(Actor actor)
		{
			return XuanJianVNext.Systems.Runtime.XjAnnualExecutionContext.ResolveYear(actor);
		}
}

