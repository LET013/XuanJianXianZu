using System;
using System.Text;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.WeaponArt;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.WeaponArt;

namespace XuanJianVNext.Systems.Combat;

/// <summary>
/// 剑意、养青冥与紫金长庚五神通的静态战斗投影。
/// 只在 Actor.updateStats 时读取角色既有状态并生成属性，不进入 getHit 热路径，
/// 不扫描附近角色、不维护实时领域，也不创建额外战斗状态机。
/// </summary>
internal static class XjSwordDaoCombatSystem
{
	internal static bool TryGetBonusProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (!XjFuQiSwordWorldState.IsCombatDoctrineEstablished
			|| actor?.data == null || !actor.isAlive()
			|| !XjWeaponArtSystem.TryGetSwordIntent(actor, out string swordIntent))
		{
			return false;
		}

		bool hasSword = HasCompatibleSwordEquipped(actor);
		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool ziJinLongGeng = IsCompletedZiJinLongGeng(actor);
		bool manifestsWithoutSword = CanManifestWithoutSword(actor, fuQi, ziJinLongGeng);
		if (!hasSword && !manifestsWithoutSword)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		string intentType = XjSwordIntentRegistry.ResolveIntentType(actorId, swordIntent);
		profile = BuildIntentProfile(intentType);

		if (fuQi)
		{
			profile = AddProfiles(profile, BuildFuQiProfile(actor));
		}
		else if (ziJinLongGeng)
		{
			profile = AddProfiles(profile, BuildZiJinLongGengProfile());
		}
		return true;
	}

	internal static string BuildDisplaySummary(Actor actor)
	{
		if (actor?.data == null
			|| !XjWeaponArtSystem.TryGetSwordIntent(actor, out string swordIntent))
		{
			return string.Empty;
		}

		// 长庚尚未真正开道时，普通剑艺只展示剑艺本身，不提前暴露
		// 世界级剑道状态、未证条件或额外战斗加成。
		if (!XjFuQiSwordWorldState.IsCombatDoctrineEstablished) return string.Empty;

		long actorId = ((BaseSystemData)actor.data).id;
		string intentType = XjSwordIntentRegistry.ResolveIntentType(actorId, swordIntent);
		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool ziJinLongGeng = IsCompletedZiJinLongGeng(actor);
		string manifestation = ResolveManifestationText(actor, fuQi, ziJinLongGeng);

		StringBuilder builder = new StringBuilder(128);
		builder.Append("剑理：").AppendLine(string.IsNullOrWhiteSpace(intentType) ? "未定" : intentType);
		builder.Append("显化：").AppendLine(manifestation);
		builder.Append("战斗倾向：").Append(ResolveIntentDescription(intentType));
		if (ziJinLongGeng)
		{
			builder.AppendLine().Append("五神通：意堪身承意，其余四道分别护神、破法、应变与统摄剑势");
		}
		else if (fuQi
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId)
			&& string.Equals(XjRealmHelper.NormalizeId(realmId), XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			builder.AppendLine().Append("道位：天地既认此剑为道，剑意可随性命而发");
		}
		return builder.ToString().TrimEnd();
	}

	internal static bool CanUseSwordArtWithoutWeapon(Actor actor)
	{
		if (!XjFuQiSwordWorldState.IsCombatDoctrineEstablished
			|| actor?.data == null
			|| !XjWeaponArtSystem.TryGetSwordIntent(actor, out _)) return false;
		bool fuQi = XjCultivationPathRules.IsFuQiYangXing(actor);
		bool ziJinLongGeng = IsCompletedZiJinLongGeng(actor);
		return CanManifestWithoutSword(actor, fuQi, ziJinLongGeng);
	}

	internal static bool HasCompatibleSwordEquipped(Actor actor)
	{
		if (actor?.data == null) return false;
		Item weapon = actor.equipment?.getSlot(EquipmentType.Weapon)?.getItem();
		return weapon?.data != null
			&& string.Equals(
				XjWeaponArtSystem.ResolveItemKindForActor(actor, weapon),
				XjWeaponArtKinds.Sword,
				StringComparison.Ordinal);
	}

	private static bool CanManifestWithoutSword(Actor actor, bool fuQi, bool ziJinLongGeng)
	{
		if (ziJinLongGeng) return true; // 〖意堪身〗已经把一己剑意纳入紫府神通。
		if (!fuQi || actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			return false;
		}
		string normalized = XjRealmHelper.NormalizeId(realmId);
		return string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
			|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal);
	}

	private static bool IsCompletedZiJinLongGeng(Actor actor)
	{
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !XjZiJinSwordDaoCatalog.IsLongGeng(daoTu))
		{
			return false;
		}
		return XjZiJinSwordDaoCatalog.HasExactShenTongSet(XjXianJiAccessor.BuildState(actor));
	}

	private static XjFaBaoBonusProfile BuildIntentProfile(string intentType)
	{
		return intentType switch
		{
			"断法" => new XjFaBaoBonusProfile(
				0f, 0f, 0.03f, 0f, 0f, 8f, 0f, 0f,
				accuracyBonus: 3f,
				shieldBreakBonus: 8f),
			"疾锋" => new XjFaBaoBonusProfile(
				0f, 0f, 0.04f, 0f, 0f, 0f, 0f, 0f,
				accuracyBonus: 8f,
				critBonus: 4f,
				attackSpeedBonus: 0.08f),
			"镇守" => new XjFaBaoBonusProfile(
				0f, 0f, 0.02f, 6f, 0.04f, 0f, 6f, 0f,
				critTakenReduction: 4f),
			"藏锋" => new XjFaBaoBonusProfile(
				0f, 0f, 0.04f, 0f, 0f, 0f, 0f, 0f,
				critBonus: 8f,
				sameRealmDamageBonus: 6f),
			"绝命" => new XjFaBaoBonusProfile(
				0f, 0f, 0.06f, 0f, 0f, 0f, 0f, 5f,
				critBonus: 5f),
			"破军" => new XjFaBaoBonusProfile(
				0f, 0f, 0.08f, 0f, 0f, 4f, 0f, 0f,
				sameRealmDamageBonus: 4f,
				shieldBreakBonus: 10f),
			_ => new XjFaBaoBonusProfile(
				0f, 0f, 0.03f, 0f, 0f, 3f, 0f, 0f,
				accuracyBonus: 3f,
				critBonus: 3f)
		};
	}

	private static XjFaBaoBonusProfile BuildFuQiProfile(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			return default;
		}
		string normalized = XjRealmHelper.NormalizeId(realmId);
		if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
		{
			return new XjFaBaoBonusProfile(
				0f, 0f, 0.04f, 0f, 0f, 4f, 0f, 0f,
				accuracyBonus: 4f,
				sameRealmDamageBonus: 4f);
		}
		if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal))
		{
			return new XjFaBaoBonusProfile(
				0f, 0f, 0.08f, 3f, 0f, 7f, 0f, 0f,
				dodgeBonus: 4f,
				accuracyBonus: 7f,
				critBonus: 4f,
				sameRealmDamageBonus: 6f,
				shieldBreakBonus: 4f,
				trueDamageRatio: 2f);
		}
		if (string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
		{
			return new XjFaBaoBonusProfile(
				0f, 0f, 0.12f, 5f, 0f, 10f, 0f, 0f,
				dodgeBonus: 6f,
				accuracyBonus: 10f,
				critBonus: 8f,
				attackSpeedBonus: 0.05f,
				sameRealmDamageBonus: 10f,
				shieldBreakBonus: 8f,
				trueDamageRatio: 4f);
		}
		return default;
	}

	private static XjFaBaoBonusProfile BuildZiJinLongGengProfile()
	{
		return new XjFaBaoBonusProfile(
			0f, 0f, 0.12f, 6f, 0.06f, 12f, 6f, 2f,
			dodgeBonus: 5f,
			critTakenReduction: 4f,
			accuracyBonus: 8f,
			critBonus: 8f,
			attackSpeedBonus: 0.06f,
			sameRealmDamageBonus: 10f,
			shieldBreakBonus: 12f,
			trueDamageRatio: 4f);
	}

	private static XjFaBaoBonusProfile AddProfiles(
		in XjFaBaoBonusProfile left,
		in XjFaBaoBonusProfile right)
	{
		return new XjFaBaoBonusProfile(
			left.CultivationSpeedBonus + right.CultivationSpeedBonus,
			left.GuoWeiYiXiangBonus + right.GuoWeiYiXiangBonus,
			left.AttackBonus + right.AttackBonus,
			left.DamageReduction + right.DamageReduction,
			left.HealthBonus + right.HealthBonus,
			left.ArmorPenetration + right.ArmorPenetration,
			left.HealthShield + right.HealthShield,
			left.Lifesteal + right.Lifesteal,
			left.DodgeBonus + right.DodgeBonus,
			left.CritTakenReduction + right.CritTakenReduction,
			left.HealbackBonus + right.HealbackBonus,
			left.MingShuBonus + right.MingShuBonus,
			left.HuiGuangBonus + right.HuiGuangBonus,
			left.LifespanBonus + right.LifespanBonus,
			left.AccuracyBonus + right.AccuracyBonus,
			left.CritBonus + right.CritBonus,
			left.AttackSpeedBonus + right.AttackSpeedBonus,
			left.SameRealmDamageBonus + right.SameRealmDamageBonus,
			left.ShieldBreakBonus + right.ShieldBreakBonus,
			left.BreakthroughChanceBonus + right.BreakthroughChanceBonus,
			left.TrueDamageRatio + right.TrueDamageRatio);
	}

	private static string ResolveManifestationText(Actor actor, bool fuQi, bool ziJinLongGeng)
	{
		if (ziJinLongGeng) return "〖意堪身〗承载剑意，不拘外剑";
		if (fuQi && actor?.data != null
			&& XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId))
		{
			string normalized = XjRealmHelper.NormalizeId(realmId);
			if (string.Equals(normalized, XjRealmIds.FuQiZhenRen, StringComparison.Ordinal)
				|| string.Equals(normalized, XjRealmIds.ZhenJunYuShi, StringComparison.Ordinal))
			{
				return "神妙归身，不拘外剑";
			}
			if (string.Equals(normalized, XjRealmIds.HuangGuan, StringComparison.Ordinal))
			{
				return HasCompatibleSwordEquipped(actor) ? "借剑承意" : "未持剑，剑意暂敛";
			}
		}
		return HasCompatibleSwordEquipped(actor) ? "持剑而发" : "未持剑，剑意暂敛";
	}

	private static string ResolveIntentDescription(string intentType)
	{
		return intentType switch
		{
			"断法" => "破法斩护，偏重穿透与破盾",
			"疾锋" => "争夺先机，偏重命中、暴击与攻速",
			"镇守" => "以剑护身，偏重减伤、生命与护持",
			"藏锋" => "锋芒内敛，偏重暴击与同境杀伤",
			"绝命" => "临险愈锐，偏重攻击、吸血与暴击",
			"破军" => "剑势开阖，偏重攻击、破盾与同境杀伤",
			_ => "一己剑理随创者性命而异"
		};
	}
}
