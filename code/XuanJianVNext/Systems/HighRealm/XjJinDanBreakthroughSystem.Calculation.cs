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
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;

internal static partial class XjJinDanBreakthroughSystem
{
		private static float CalculateSuccessChance(Actor actor, in XjActorCultivationSnapshot snapshot)
		{
			// 0.5.4 语义：命数决定承载力，资质决定成功率上限。
			// 当前版本保留更严格的五法门槛，避免四本功法时误入求金/成丹链路。
			float maxChance = ResolveJinDanAptitudeSuccessCap(snapshot.XjZz);
			// 四档的15%现在只作为“成熟上修指引已经打开成丹窗口”后的自身根基部分；
			// 没有成熟指引时，主突破链会在进入成功判定前强制归零。
			float chance = snapshot.XjZz == 4
				? maxChance
				: maxChance * XjBreakthroughRules.CalculateMingShuFactor(actor, XjRealmIds.JinDan);
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
				4 => 0.15f,
				_ => 0f
			};
		}






		private static string BuildFourthAptitudeFailureNarrative(Actor actor)
		{
			string actorName = actor?.getName() ?? "此人";
			return actorName + "自知自身根基难以独承金性，本已止步紫府；后得上修指引，补基演法、定制求金之术俱成，遂重立求金之志叩门，终仍因承载不足而败，身死道消。";
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

