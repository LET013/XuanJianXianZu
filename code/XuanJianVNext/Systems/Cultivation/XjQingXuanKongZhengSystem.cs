using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.Cultivation;

internal static class XjQingXuanKongZhengSystem
{
	internal const string DaoTu = "青宣";
	internal const string SourceDaoTu = "宣土";
	internal const string FoundationXianJi = "玄羊子";
	private const string GongFaName = "六堰青云要诀";
	private const int QingCanQiTarget = 10;
	private const int QingXuanEntryOdds = 1000;
	private static readonly string[] RequiredKongZhengXianJi =
	{
		"玄羊子",
		"伏青山",
		"青宣岳",
		"上岩神",
		"观地冥"
	};

	internal static bool CanEnterQingXuan(Actor actor)
	{
		if (actor?.data == null)
		{
			return false;
		}

		return XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanUnlocked, out int unlocked)
			&& unlocked > 0;
	}

	internal static bool HasCompletedKongZheng(Actor actor)
	{
		return actor?.data != null
			&& XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanKongZhengCompleted, out int completed)
			&& completed > 0;
	}

	internal static bool HasAnnualInterest(Actor actor, string realmId, string daoTu)
	{
		if (actor?.data == null
			|| !string.Equals(realmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| !string.Equals(daoTu, SourceDaoTu, StringComparison.Ordinal)
			|| CanEnterQingXuan(actor))
		{
			return false;
		}

		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi)
			&& qingCanQi > 0) return true;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi)
			&& chuYangJi > 0) return true;
		if (XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation)
			&& foundation > 0) return true;

		long actorId = ((BaseSystemData)actor.data).id;
		return actorId > 0L
			&& XjDeterministicHash.PositiveIndex(actorId, "qingxuan_entry_once", QingXuanEntryOdds) == 0;
	}

	internal static void TickActor(Actor actor, in XjActorCultivationSnapshot snapshot, int currentYear)
	{
		if (actor?.data == null
			|| currentYear <= 0
			|| !string.Equals(snapshot.RealmId, XjRealmIds.LianQi, StringComparison.Ordinal)
			|| !string.Equals(snapshot.DaoTu, SourceDaoTu, StringComparison.Ordinal))
		{
			return;
		}

		if (CanEnterQingXuan(actor))
		{
			return;
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation);

		if (qingCanQi <= 0
			&& chuYangJi <= 0
			&& foundation <= 0
			&& !RollFirstQingCanQi(actor, currentYear))
		{
			return;
		}

		if (qingCanQi < QingCanQiTarget)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanQingCanQi, Math.Min(QingCanQiTarget, qingCanQi + 1));
			return;
		}

		if (chuYangJi <= 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanChuYangJi, 1);
			return;
		}

		if (foundation <= 0)
		{
			CompleteFoundation(actor, currentYear);
		}
	}

	internal static string BuildProgressText(Actor actor)
	{
		if (actor?.data == null)
		{
			return string.Empty;
		}

		if (CanEnterQingXuan(actor))
		{
			return "玄羊子成基";
		}

		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanQingCanQi, out int qingCanQi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanChuYangJi, out int chuYangJi);
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, out int foundation);
		return "青参之气" + Math.Max(0, Math.Min(QingCanQiTarget, qingCanQi)).ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "/" + QingCanQiTarget.ToString(System.Globalization.CultureInfo.InvariantCulture)
			+ "  褚羊祭" + (chuYangJi > 0 ? "1/1" : "0/1")
			+ "  玄羊子成基" + (foundation > 0 ? "1/1" : "0/1");
	}

	private static bool RollFirstQingCanQi(Actor actor, int currentYear)
	{
		if (actor?.data == null || currentYear <= 0)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		// 青宣只在角色第一次具备宣土炼气资格时判定一次。旧实现把年份放进
		// 随机种子，角色每年都会重新抽取，实际累计概率远高于千分之一。
		return actorId > 0L
			&& XjDeterministicHash.PositiveIndex(actorId, "qingxuan_entry_once", QingXuanEntryOdds) == 0;
	}

	private static void CompleteFoundation(Actor actor, int currentYear)
	{
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanQingCanQi, QingCanQiTarget);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanChuYangJi, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanXuanYangZiFoundation, 1);
		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);

		XjCultivationStateTransitions.TrySetDaoTu(actor, DaoTu, true);
		XjGongFaAccessor.WriteState(actor, new XjGongFaState(
			true,
			GongFaName,
			4,
			0,
			0f,
			DaoTu,
			false,
			"QingXuanFoundation"));
		XjGongFaAccessor.WriteSource(actor, "玄羊子成基");
		XjXianJiAccessor.Add(actor, FoundationXianJi, currentYear);
		XjActorGongFaCollection.ReconcileWithActor(actor, "QingXuanFoundation");
	}

	internal static bool TryCompleteKongZhengOnJinDan(Actor actor, string jinDanDaoTu, int currentYear)
	{
		if (actor?.data == null
			|| HasCompletedKongZheng(actor)
			|| !string.Equals((jinDanDaoTu ?? string.Empty).Trim(), DaoTu, StringComparison.Ordinal)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			|| !string.Equals(realmId, XjRealmIds.JinDan, StringComparison.Ordinal))
		{
			return false;
		}

		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		if (!HasAllKongZhengXianJi(xianJi))
		{
			return false;
		}

		string guoWei = XjGuoWeiCalculator.Calculate(DaoTu, xianJi);
		if (!CanEnterQingXuan(actor)
			|| !string.Equals(guoWei, XjGuoWeiCalculator.ZhengWei, StringComparison.Ordinal)
			|| !string.Equals(XjGuoWeiCalculator.ResolveManifestDaoTu(DaoTu, xianJi, guoWei), DaoTu, StringComparison.Ordinal))
		{
			return false;
		}

		XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanKongZhengCompleted, 1);
		PublishKongZhengEvent(actor, currentYear);
		return true;
	}

	private static void PublishKongZhengEvent(Actor actor, int currentYear)
	{
		if (actor?.data == null)
		{
			return;
		}

		string actorName = actor.getName() ?? "无名修士";
		string historyText = "【空证开天·青宣】" + actorName
			+ "持《六堰青云要诀》，玄羊子、伏青山、青宣岳、上岩神、观地冥五基同举，"
			+ "证成青堰神岳伏元性。第六土自旧制之外开辟，空证一脉由此显世。";
		string tipText = "【空证开天】青宣第六土显世\n" + actorName
			+ "五基合一，证就金丹，青堰神岳伏元性照见山河。";
		XjBroadcastSystem.BroadcastSLevelActorEvent(
			actor,
			historyText,
			tipText,
			"#74E8FF",
			12f,
			XjEventIconCatalog.JinDanUpgrade);
	}

	private static bool HasAllKongZhengXianJi(in XjXianJiState xianJi)
	{
		if (xianJi.Ids == null || xianJi.Ids.Length < RequiredKongZhengXianJi.Length)
		{
			return false;
		}

		for (int requiredIndex = 0; requiredIndex < RequiredKongZhengXianJi.Length; requiredIndex++)
		{
			bool found = false;
			for (int actorIndex = 0; actorIndex < xianJi.Ids.Length; actorIndex++)
			{
				if (string.Equals(xianJi.Ids[actorIndex], RequiredKongZhengXianJi[requiredIndex], StringComparison.Ordinal))
				{
					found = true;
					break;
				}
			}

			if (!found)
			{
				return false;
			}
		}

		return true;
	}
}
