using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.FaBao;

internal static class XjFaBaoBonusService
{
	private static readonly ConcurrentDictionary<long, CachedProfile> ProfileCache = new();
	private const string NoFaBaoSignature = "<none>";
	// 法宝加成会被战斗、寿元、突破和面板同时读取。状态写入会主动 Forget，
	// 因此常态依赖事件失效；仅每180帧做一次防御性签名复核，避免第三方/原生旁路修改
	// 长期留下旧值，同时把装备字符串拼接从热路径中移出绝大多数帧。
	private const int ProfileValidationFrames = 180;

	internal static void Forget(long actorId)
	{
		if (actorId > 0L) ProfileCache.TryRemove(actorId, out _);
	}

	internal static void Clear()
	{
		ProfileCache.Clear();
	}

	internal static bool TryGetProfile(Actor actor, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (actor?.data == null)
		{
			return false;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		int validationFrameBucket = UnityEngine.Time.frameCount / ProfileValidationFrames;
		if (actorId > 0L
			&& TryGetCachedProfile(actorId, out CachedProfile cached)
			&& cached.ValidationFrameBucket == validationFrameBucket)
		{
			profile = cached.Profile;
			return cached.Found;
		}

		XjFaBaoState state = XjFaBaoAccessor.HasState(actor)
			? XjFaBaoAccessor.BuildState(actor)
			: XjFaBaoState.Empty;
		string signature = BuildCombinedProfileSignature(actor, in state);
		if (actorId > 0L
			&& TryGetCachedProfile(actorId, out CachedProfile signatureCached)
			&& string.Equals(signatureCached.Signature, signature, StringComparison.Ordinal))
		{
			profile = signatureCached.Profile;
			return signatureCached.Found;
		}

		bool found = false;
		string primaryId = string.Empty;
		if (state.Found)
		{
			primaryId = state.Id ?? string.Empty;
			if (TryBuildProfileFromAffixes(state.Affixes, out XjFaBaoBonusProfile primaryProfile)
				|| XjFaBaoCatalog.TryGetBonusProfileForRole(state.ClassName, state.Role, out primaryProfile))
			{
				profile = primaryProfile;
				found = true;
			}
		}

		if (actor.equipment != null)
		{
			foreach (ActorEquipmentSlot slot in actor.equipment)
			{
				Item item = slot?.getItem();
				if (!XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState itemState))
				{
					continue;
				}

				if (!string.IsNullOrWhiteSpace(primaryId)
					&& string.Equals(primaryId, itemState.Id, StringComparison.Ordinal))
				{
					continue;
				}

				if (TryBuildProfileFromAffixes(itemState.Affixes, out XjFaBaoBonusProfile itemProfile)
					|| XjFaBaoCatalog.TryGetBonusProfileForRole(itemState.ClassName, itemState.Role, out itemProfile))
				{
					profile = found ? MergeProfiles(in profile, in itemProfile) : itemProfile;
					found = true;
				}
			}
		}
		if (XjDaoTaiBindingBonusService.TryGetProfile(actor, out XjFaBaoBonusProfile daoTaiBindingProfile))
		{
			// 道胎双位不是法宝词条，必须作为独立位格收益叠加。旧实现复用 MergeProfiles 的
			// “同类法宝取最高值”规则，导致一件更高词条法宝就能把余/闰双位收益完全吃掉。
			profile = found ? AddIndependentProfile(in profile, in daoTaiBindingProfile) : daoTaiBindingProfile;
			found = true;
		}

		if (actorId > 0L)
		{
			SetCachedProfile(actorId, new CachedProfile(validationFrameBucket, signature, found, profile));
		}
		return found;
	}

	private static bool TryGetCachedProfile(long actorId, out CachedProfile profile)
	{
		return ProfileCache.TryGetValue(actorId, out profile);
	}

	private static void SetCachedProfile(long actorId, CachedProfile profile)
	{
		ProfileCache[actorId] = profile;
	}

	internal static void ApplyEffectiveCultivationStats(Actor actor, ref float mingShu, ref float huiGuang)
	{
		if (!TryGetProfile(actor, out XjFaBaoBonusProfile profile))
		{
			return;
		}

		if (mingShu > 0f && profile.MingShuBonus > 0f)
		{
			mingShu = (float)Math.Floor(mingShu * (1f + profile.MingShuBonus));
		}

		if (huiGuang > 0f && profile.HuiGuangBonus > 0f)
		{
			huiGuang = (float)Math.Floor(huiGuang * (1f + profile.HuiGuangBonus));
		}
	}

	private static string BuildCombinedProfileSignature(Actor actor, in XjFaBaoState state)
	{
		System.Text.StringBuilder builder = null;
		if (state.Found)
		{
			builder = new System.Text.StringBuilder(256);
			builder.Append(state.Id).Append('|').Append(state.ClassName).Append('|').Append(state.Kind)
				.Append('|').Append(state.Role).Append('|').Append(state.Affixes);
		}

		if (actor?.equipment != null)
		{
			foreach (ActorEquipmentSlot slot in actor.equipment)
			{
				Item item = slot?.getItem();
				if (!XjFaBaoEquipmentSync.TryReadFaBaoState(item, out XjFaBaoState itemState))
				{
					continue;
				}
				if (builder == null)
				{
					builder = new System.Text.StringBuilder(256);
					builder.Append(NoFaBaoSignature);
				}
				builder.Append("||").Append(itemState.Id).Append('|').Append(itemState.ClassName)
					.Append('|').Append(itemState.Role).Append('|').Append(itemState.Affixes);
			}
		}
		long actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		if (actorId > 0L && XjDaoTaiBindingBonusService.TryGetSignature(actorId, out string daoTaiBindingSignature))
		{
			if (builder == null)
			{
				builder = new System.Text.StringBuilder(256);
				builder.Append(NoFaBaoSignature);
			}
			builder.Append("||").Append(daoTaiBindingSignature);
		}

		if (builder == null)
		{
			XjStageZeroObservation.RecordNoFaBaoSignatureFastPath();
			return NoFaBaoSignature;
		}
		return builder.ToString();
	}

	private static XjFaBaoBonusProfile MergeProfiles(
		in XjFaBaoBonusProfile left,
		in XjFaBaoBonusProfile right)
	{
		return new XjFaBaoBonusProfile(
			Math.Max(left.CultivationSpeedBonus, right.CultivationSpeedBonus),
			Math.Max(left.GuoWeiYiXiangBonus, right.GuoWeiYiXiangBonus),
			Math.Max(left.AttackBonus, right.AttackBonus),
			Math.Max(left.DamageReduction, right.DamageReduction),
			Math.Max(left.HealthBonus, right.HealthBonus),
			Math.Max(left.ArmorPenetration, right.ArmorPenetration),
			Math.Max(left.HealthShield, right.HealthShield),
			Math.Max(left.Lifesteal, right.Lifesteal),
			Math.Max(left.DodgeBonus, right.DodgeBonus),
			Math.Max(left.CritTakenReduction, right.CritTakenReduction),
			Math.Max(left.HealbackBonus, right.HealbackBonus),
			Math.Max(left.MingShuBonus, right.MingShuBonus),
			Math.Max(left.HuiGuangBonus, right.HuiGuangBonus),
			Math.Max(left.LifespanBonus, right.LifespanBonus),
			Math.Max(left.AccuracyBonus, right.AccuracyBonus),
			Math.Max(left.CritBonus, right.CritBonus),
			Math.Max(left.AttackSpeedBonus, right.AttackSpeedBonus),
			Math.Max(left.SameRealmDamageBonus, right.SameRealmDamageBonus),
			Math.Max(left.ShieldBreakBonus, right.ShieldBreakBonus),
			Math.Max(left.BreakthroughChanceBonus, right.BreakthroughChanceBonus),
			Math.Max(left.TrueDamageRatio, right.TrueDamageRatio));
	}

	private static XjFaBaoBonusProfile AddIndependentProfile(
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

	private readonly struct CachedProfile
	{
		internal readonly int ValidationFrameBucket;
		internal readonly string Signature;
		internal readonly bool Found;
		internal readonly XjFaBaoBonusProfile Profile;

		internal CachedProfile(int validationFrameBucket, string signature, bool found, XjFaBaoBonusProfile profile)
		{
			ValidationFrameBucket = validationFrameBucket;
			Signature = signature ?? string.Empty;
			Found = found;
			Profile = profile;
		}
	}

	internal static float GetCultivationSpeedMultiplier(Actor actor)
	{
		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) && profile.CultivationSpeedBonus > 0f
			? 1f + profile.CultivationSpeedBonus
			: 1f;
	}

	internal static float GetBreakthroughChanceBonus(Actor actor)
	{
		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) ? profile.BreakthroughChanceBonus : 0f;
	}

	internal static int GetEffectiveJinDanYiXiang(Actor actor, int baseYiXiang)
	{
		if (baseYiXiang <= 0)
		{
			return 0;
		}

		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) && profile.GuoWeiYiXiangBonus > 0f
			? (int)Math.Floor(baseYiXiang * (1f + profile.GuoWeiYiXiangBonus))
			: baseYiXiang;
	}

	internal static float GetEffectiveMingShu(Actor actor, float baseMingShu)
	{
		if (baseMingShu <= 0f)
		{
			return 0f;
		}

		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) && profile.MingShuBonus > 0f
			? (float)Math.Floor(baseMingShu * (1f + profile.MingShuBonus))
			: baseMingShu;
	}

	internal static float GetEffectiveHuiGuang(Actor actor, float baseHuiGuang)
	{
		if (baseHuiGuang <= 0f)
		{
			return 0f;
		}

		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) && profile.HuiGuangBonus > 0f
			? (float)Math.Floor(baseHuiGuang * (1f + profile.HuiGuangBonus))
			: baseHuiGuang;
	}

	internal static float GetEffectiveLifespan(Actor actor, float baseLifespan)
	{
		if (baseLifespan <= 0f)
		{
			return 0f;
		}

		return TryGetProfile(actor, out XjFaBaoBonusProfile profile) && profile.LifespanBonus > 0f
			? baseLifespan * (1f + profile.LifespanBonus)
			: baseLifespan;
	}

	internal static string BuildBonusText(string className)
	{
		if (!XjFaBaoCatalog.TryGetBonusProfile(className, out XjFaBaoBonusProfile profile))
		{
			return string.Empty;
		}

		return BuildBonusText(profile);
	}

	private static string BuildBonusText(in XjFaBaoBonusProfile profile)
	{
		List<string> parts = new List<string>(8);
		AddPercent(parts, "修炼速度", profile.CultivationSpeedBonus);
		AddPercent(parts, "果位意象", profile.GuoWeiYiXiangBonus);
		AddPercent(parts, "命数加成", profile.MingShuBonus);
		AddPercent(parts, "道慧加成", profile.HuiGuangBonus);
		AddPercent(parts, "寿命增加", profile.LifespanBonus);
		AddPercent(parts, "攻击", profile.AttackBonus);
		AddPercent(parts, "伤害减免", profile.DamageReduction);
		AddPercent(parts, "血量", profile.HealthBonus);
		AddPercent(parts, "减伤穿透", profile.ArmorPenetration);
		AddPercent(parts, "真伤转化", profile.TrueDamageRatio);
		AddPercent(parts, "血量护盾", profile.HealthShield);
		AddPercent(parts, "吸血", profile.Lifesteal);
		AddPercent(parts, "闪避提升", profile.DodgeBonus);
		AddPercent(parts, "受暴击降低", profile.CritTakenReduction);
		AddPercent(parts, "每秒回血", profile.HealbackBonus);
		AddPercent(parts, "命中提升", profile.AccuracyBonus);
		AddPercent(parts, "暴击提升", profile.CritBonus);
		AddPercent(parts, "攻速提升", profile.AttackSpeedBonus);
		AddPercent(parts, "同境界伤害", profile.SameRealmDamageBonus);
		AddPercent(parts, "破盾", profile.ShieldBreakBonus);
		AddPercent(parts, "突破概率", profile.BreakthroughChanceBonus);
		return string.Join("\n", parts);
	}

	internal static string BuildBonusText(in XjFaBaoState state)
	{
		if (!string.IsNullOrWhiteSpace(state.Affixes))
		{
			return NormalizeAffixDisplay(state.Affixes);
		}

		if (!XjFaBaoCatalog.TryGetBonusProfileForRole(state.ClassName, state.Role, out XjFaBaoBonusProfile profile))
		{
			return string.Empty;
		}

		return BuildBonusText(profile);
	}

	internal static string BuildDescription(in XjFaBaoState state)
	{
		if (!state.Found)
		{
			return string.Empty;
		}

		if (!string.IsNullOrWhiteSpace(state.Description))
		{
			return state.Description.Trim();
		}

		string daoTu = string.IsNullOrWhiteSpace(state.DaoTu) ? "玄鉴" : state.DaoTu.Trim();
		string role = XjFaBaoCatalog.NormalizeRole(state.Kind, state.Role);
		string function = string.Equals(role, XjFaBaoCatalog.RoleAttack, StringComparison.Ordinal)
			? "锋芒内敛，临敌时有破阵摧坚之势"
			: string.Equals(role, XjFaBaoCatalog.RoleDefense, StringComparison.Ordinal)
				? "宝光护体，临危时自有灵机环身"
				: "灵应随身，能温养道基、澄明神意";

		string classText = XjFaBaoCatalog.IsXianQi(state.ClassName)
			? "历经道胎五百年温养，于千一机缘中蜕生仙机，器名器魂与本命归属皆承旧器"
			: XjFaBaoCatalog.IsJinDanFaBao(state.ClassName)
			? "内蕴" + daoTu + "金丹真意，随金丹真炁流转而渐显玄光"
			: XjFaBaoCatalog.IsZhuJiFaQi(state.ClassName)
				? "以" + daoTu + "先天之气炼成，能承载筑基真元而不生灵性"
				: "藏有" + daoTu + "紫府灵机，随道行温养而渐生灵应";
		return state.Name + function + "；" + classText + "。";
	}

	internal static int GetOverviewPower(in XjFaBaoState state)
	{
		if (!state.Found)
		{
			return 0;
		}

		if (!string.IsNullOrWhiteSpace(state.Affixes))
		{
			int max = 0;
			string[] parts = state.Affixes.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < parts.Length; i++)
			{
				string part = parts[i].Trim();
				int plus = part.IndexOf('+');
				int percent = part.IndexOf('%');
				if (plus <= 0 || percent <= plus)
				{
					continue;
				}

				string valueText = part.Substring(plus + 1, percent - plus - 1).Trim();
				if (int.TryParse(valueText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
					&& value > max)
				{
					max = value;
				}
			}

			return max;
		}

		return XjFaBaoCatalog.IsXianQi(state.ClassName) ? 50
			: XjFaBaoCatalog.IsJinDanFaBao(state.ClassName) ? 30
			: XjFaBaoCatalog.IsZhuJiFaQi(state.ClassName) ? 10 : 20;
	}

	internal static int GetAffixPercent(in XjFaBaoState state, string label)
	{
		return GetOverviewPowerForLabels(state.Affixes, label);
	}

	internal static int GetOverviewAttackPower(in XjFaBaoState state)
	{
		return GetOverviewPowerForLabels(
			state.Affixes,
			"伤害提升",
			"减伤穿透",
			"防御穿透",
			"真伤转化",
			"命中提升",
			"暴击提升",
			"攻速提升",
			"同境界伤害",
			"破盾",
			"吸血");
	}

	internal static int GetOverviewDefensePower(in XjFaBaoState state)
	{
		return GetOverviewPowerForLabels(
			state.Affixes,
			"伤害减免",
			"生命提升",
			"护盾提升",
			"闪避提升",
			"受暴击降低");
	}

	internal static int GetOverviewAuxiliaryPower(in XjFaBaoState state)
	{
		return GetOverviewPowerForLabels(
			state.Affixes,
			"每秒回血",
			"修炼速度",
			"果位意象",
			"命数加成",
			"道慧加成",
			"辉光加成", // 旧档兼容：仅保留读取，不再生成旧称。
			"寿命增加",
			"突破概率");
	}

	private static int GetOverviewPowerForLabels(string affixes, params string[] labels)
	{
		if (string.IsNullOrWhiteSpace(affixes) || labels == null || labels.Length == 0)
		{
			return 0;
		}

		int max = 0;
		string[] parts = affixes.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i].Trim();
			int plus = part.IndexOf('+');
			int percent = part.IndexOf('%');
			if (plus <= 0 || percent <= plus)
			{
				continue;
			}

			string label = part.Substring(0, plus).Trim();
			if (!ContainsLabel(labels, label))
			{
				continue;
			}

			string valueText = part.Substring(plus + 1, percent - plus - 1).Trim();
			if (int.TryParse(valueText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value)
				&& value > max)
			{
				max = value;
			}
		}

		return max;
	}

	private static bool ContainsLabel(string[] labels, string label)
	{
		for (int i = 0; i < labels.Length; i++)
		{
			if (string.Equals(labels[i], label, StringComparison.Ordinal))
			{
				return true;
			}
		}

		return false;
	}

	private static void AddPercent(List<string> parts, string label, float value)
	{
		if (parts == null || value <= 0f)
		{
			return;
		}

		parts.Add(label + "+" + FormatPercent(value * 100f) + "%");
	}

	private static string FormatPercent(float percent)
	{
		return percent.ToString(percent < 2f ? "0.0#" : "0.#", System.Globalization.CultureInfo.InvariantCulture);
	}

	private static string NormalizeAffixDisplay(string affixes)
	{
		if (string.IsNullOrWhiteSpace(affixes))
		{
			return string.Empty;
		}

		string[] source = affixes.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
		List<string> normalized = new List<string>(source.Length);
		for (int i = 0; i < source.Length; i++)
		{
			string part = source[i].Trim();
			int plus = part.IndexOf('+');
			int percent = part.IndexOf('%');
			if (plus <= 0 || percent <= plus)
			{
				normalized.Add(part);
				continue;
			}

			string label = part.Substring(0, plus).Trim();
			string valueText = part.Substring(plus + 1, percent - plus - 1).Trim();
			if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
			{
				normalized.Add(part);
				continue;
			}

			if (string.Equals(label, "防御穿透", StringComparison.Ordinal))
			{
				label = "减伤穿透";
			}
			if (string.Equals(label, "每秒回血", StringComparison.Ordinal) && value > 2f)
			{
				value /= 100f;
			}

			normalized.Add(label + "+" + FormatPercent(value) + "%");
		}

		return string.Join("\n", normalized);
	}

	private static bool TryBuildProfileFromAffixes(string affixes, out XjFaBaoBonusProfile profile)
	{
		profile = default;
		if (string.IsNullOrWhiteSpace(affixes))
		{
			return false;
		}

		float cultivation = 0f;
		float guoWei = 0f;
		float mingShu = 0f;
		float huiGuang = 0f;
		float lifespan = 0f;
		float attack = 0f;
		float reduction = 0f;
		float health = 0f;
		float penetration = 0f;
		float shield = 0f;
		float lifesteal = 0f;
		float dodge = 0f;
		float critTakenReduction = 0f;
		float healback = 0f;
		float accuracy = 0f;
		float crit = 0f;
		float attackSpeed = 0f;
		float sameRealm = 0f;
		float shieldBreak = 0f;
		float breakthrough = 0f;
		float trueDamage = 0f;
		string[] parts = affixes.Split(new[] { '/', '-' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < parts.Length; i++)
		{
			string part = parts[i].Trim();
			int plus = part.IndexOf('+');
			int percent = part.IndexOf('%');
			if (plus <= 0 || percent <= plus)
			{
				continue;
			}

			string label = part.Substring(0, plus).Trim();
			string valueText = part.Substring(plus + 1, percent - plus - 1).Trim();
			if (!float.TryParse(valueText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
			{
				continue;
			}

			float ratio = Math.Max(0f, value / 100f);
			if (string.Equals(label, "每秒回血", StringComparison.Ordinal) && value > 2f)
			{
				// 旧档曾把回血与攻击词条共用 10%-30% 区间。运行时按 0.10%-0.30%/秒解释。
				ratio = Math.Max(0f, value / 10000f);
			}
			switch (label)
			{
				case "修炼速度":
					cultivation = Math.Max(cultivation, ratio);
					break;
				case "果位意象":
					guoWei = Math.Max(guoWei, ratio);
					break;
				case "命数加成":
					mingShu = Math.Max(mingShu, ratio);
					break;
				case "\u8f89\u5149\u52a0\u6210":
				case "道慧加成": // 旧档兼容：仅保留读取，不再生成旧称。
					huiGuang = Math.Max(huiGuang, ratio);
					break;
				case "寿命增加":
					lifespan = Math.Max(lifespan, ratio);
					break;
				case "伤害提升":
					attack = Math.Max(attack, ratio);
					break;
				case "伤害减免":
					reduction = Math.Max(reduction, ratio);
					break;
				case "生命提升":
					health = Math.Max(health, ratio);
					break;
				case "防御穿透":
				case "减伤穿透":
					penetration = Math.Max(penetration, ratio);
					break;
				case "真伤转化":
					trueDamage = Math.Max(trueDamage, ratio);
					break;
				case "护盾提升":
					shield = Math.Max(shield, ratio);
					break;
				case "吸血":
					lifesteal = Math.Max(lifesteal, ratio);
					break;
				case "闪避提升":
					dodge = Math.Max(dodge, ratio);
					break;
				case "受暴击降低":
					critTakenReduction = Math.Max(critTakenReduction, ratio);
					break;
				case "每秒回血":
					healback = Math.Max(healback, ratio);
					break;
				case "命中提升":
					accuracy = Math.Max(accuracy, ratio);
					break;
				case "暴击提升":
					crit = Math.Max(crit, ratio);
					break;
				case "攻速提升":
					attackSpeed = Math.Max(attackSpeed, ratio);
					break;
				case "同境界伤害":
					sameRealm = Math.Max(sameRealm, ratio);
					break;
				case "破盾":
					shieldBreak = Math.Max(shieldBreak, ratio);
					break;
				case "突破概率":
					breakthrough = Math.Max(breakthrough, ratio);
					break;
			}
		}

		profile = new XjFaBaoBonusProfile(
			cultivation,
			guoWei,
			attack,
			reduction,
			health,
			penetration,
			shield,
			lifesteal,
			dodge,
			critTakenReduction,
			healback,
			mingShu,
			huiGuang,
			lifespan,
			accuracy,
			crit,
			attackSpeed,
			sameRealm,
			shieldBreak,
			breakthrough,
			trueDamage);
		return cultivation > 0f
			|| guoWei > 0f
			|| mingShu > 0f
			|| huiGuang > 0f
			|| lifespan > 0f
			|| attack > 0f
			|| reduction > 0f
			|| health > 0f
			|| penetration > 0f
			|| shield > 0f
			|| lifesteal > 0f
			|| dodge > 0f
			|| critTakenReduction > 0f
			|| healback > 0f
			|| accuracy > 0f
			|| crit > 0f
			|| attackSpeed > 0f
			|| sameRealm > 0f
			|| shieldBreak > 0f
			|| breakthrough > 0f
			|| trueDamage > 0f;
	}
}
