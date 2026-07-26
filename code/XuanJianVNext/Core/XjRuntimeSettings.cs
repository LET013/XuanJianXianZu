using System;
using System.Collections;
using System.Globalization;
using System.Reflection;
using NeoModLoader.api;

namespace XuanJianVNext.Core;

internal readonly struct XjYearRange
{
	internal readonly int Min;
	internal readonly int Max;

	internal XjYearRange(int min, int max)
	{
		Min = Math.Max(0, Math.Min(min, max));
		Max = Math.Max(Min, Math.Max(min, max));
	}

	internal int Roll(System.Random random)
	{
		if (Min >= Max || random == null)
		{
			return Min;
		}

		long span = (long)Max - Min + 1L;
		return (int)(Min + (long)Math.Floor(random.NextDouble() * span));
	}
}

internal static class XjRuntimeSettings
{
	internal const string ReincarnationModeZiFuJinXing = "ZiFuJinXing";

	private static readonly System.Random SharedRandom = new System.Random();
	private static bool autoCollectZhuJi;
	private static bool autoCollectZiFu = true;
	private static bool autoCollectJinDan = true;
	private static bool autoCollectTianShouDaoMai = true;
	private static bool autoCollectZiFuReincarnation = true;
	private static bool autoCollectJinDanReincarnation = true;
	private static bool autoCollectFaBaoOwner = true;
	private static bool autoCollectSwordImmortal;
	private static bool broadcastTreasureMilestone = true;
	private static bool broadcastBottleneck = true;
	private static bool broadcastGongFaWrite = true;
	private static bool broadcastDongTian = true;
	private static bool broadcastYinSi = true;
	private static bool broadcastSect = true;
	private static bool broadcastDeath = true;
	private static bool broadcastLingWu = true;
	private static bool broadcastHighRealm = true;
	private static bool broadcastFamilyInheritance = true;
	private static bool broadcastHighRealmInfluence = true;
	private static bool cultivationEnabled = true;
	private static bool showFpsOverlay;
	private static bool allowSectRebellion;
	private static bool spawnJinXingYaoXie = true;
	private static bool spawnLongShu = true;
	private static XjYearRange qiYuDongTianSpawnYears = new XjYearRange(300, 500);
	private static int jinDanDongTianCultivateYears = 500;
	private static int jinDanPostPeaceYears = 10;
	private static int aptitudeGrantChancePercent = 40;
	private static int qiuJinFaChancePermille = 10;
	private static int revision;

	internal static bool AutoCollectZhuJiEnabled => autoCollectZhuJi;
	internal static bool AutoCollectZiFuEnabled => autoCollectZiFu;
	internal static bool AutoCollectJinDanEnabled => autoCollectJinDan;
	internal static bool AutoCollectTianShouDaoMaiEnabled => autoCollectTianShouDaoMai;
	internal static bool AutoCollectFaBaoOwnerEnabled => autoCollectFaBaoOwner;
	internal static bool AutoCollectSwordImmortalEnabled => autoCollectSwordImmortal;
	internal static bool BroadcastTreasureMilestoneEnabled => broadcastTreasureMilestone;
	// 旧接口保留为兼容别名，不再对应独立配置开关。
	internal static bool BroadcastZiFuEnabled => broadcastHighRealm;
	internal static bool BroadcastYaoCaiEnabled => false;
	internal static bool BroadcastLianDanEnabled => broadcastTreasureMilestone;
	internal static bool BroadcastLianQiArtifactEnabled => broadcastTreasureMilestone;
	internal static bool BroadcastBottleneckEnabled => broadcastBottleneck;
	internal static bool BroadcastTalismanEnabled => false;
	internal static bool BroadcastFormationEnabled => false;
	internal static bool BroadcastGongFaWriteEnabled => broadcastGongFaWrite;
	internal static bool BroadcastDongTianEnabled => broadcastDongTian;
	internal static bool BroadcastSecretRealmTrainingEnabled => broadcastDongTian;
	internal static bool BroadcastYinSiEnabled => broadcastYinSi;
	internal static bool BroadcastSectEnabled => broadcastSect;
	internal static bool BroadcastDeathEnabled => broadcastDeath;
	internal static bool BroadcastLingWuEnabled => broadcastLingWu;
	internal static bool BroadcastHighRealmEnabled => broadcastHighRealm;
	internal static bool BroadcastFamilyInheritanceEnabled => broadcastFamilyInheritance;
	internal static bool BroadcastHighRealmInfluenceEnabled => broadcastHighRealmInfluence;
	internal static bool CultivationEnabled => cultivationEnabled;
	internal static bool ShowFpsOverlayEnabled => showFpsOverlay;
	internal static bool AllowSectRebellionEnabled => allowSectRebellion;
	internal static bool SpawnJinXingYaoXieEnabled => spawnJinXingYaoXie;
	internal static bool SpawnLongShuEnabled => spawnLongShu;
	internal static float AptitudeGrantChanceCap => aptitudeGrantChancePercent / 100f;
	internal static int AptitudeGrantChancePercent => aptitudeGrantChancePercent;
	internal static float QiuJinFaChanceCap => qiuJinFaChancePermille / 1000f;
	internal static int Revision => revision;

	internal static void LoadFromModConfig(object modConfig)
	{
		if (modConfig == null)
		{
			return;
		}

		SetAutoCollectZhuJi(ReadBool(modConfig, "XuanJian_config_auto_collect_zhuji", autoCollectZhuJi));
		SetAutoCollectZiFu(ReadBool(modConfig, "XuanJian_config_auto_collect_zifu", autoCollectZiFu));
		SetAutoCollectJinDan(ReadBool(modConfig, "XuanJian_config_auto_collect_jindan", autoCollectJinDan));
		SetAutoCollectTianShouDaoMai(ReadBool(modConfig, "XuanJian_config_auto_collect_tianshoudaomai", autoCollectTianShouDaoMai));
		SetAutoCollectZiFuReincarnation(ReadBool(modConfig, "XuanJian_config_auto_collect_zifu_reincarnation", autoCollectZiFuReincarnation));
		SetAutoCollectJinDanReincarnation(ReadBool(modConfig, "XuanJian_config_auto_collect_jindan_reincarnation", autoCollectJinDanReincarnation));
		SetAutoCollectFaBaoOwner(ReadBool(modConfig, "XuanJian_config_auto_collect_fabao_owner", autoCollectFaBaoOwner));
		SetAutoCollectSwordImmortal(ReadBool(modConfig, "XuanJian_config_auto_collect_sword_immortal", autoCollectSwordImmortal));
		SetBroadcastBottleneck(ReadBool(modConfig, "XuanJian_config_enable_bottleneck_announcement", broadcastBottleneck));
		SetBroadcastGongFaWrite(ReadBool(modConfig, "XuanJian_config_enable_gongfa_write_announcement", broadcastGongFaWrite));
		SetBroadcastDongTian(ReadBool(modConfig, "XuanJian_config_enable_dongtian_announcement", broadcastDongTian));
		SetBroadcastYinSi(ReadBool(modConfig, "XuanJian_config_enable_yinsi_announcement", broadcastYinSi));
		SetBroadcastSect(ReadBool(modConfig, "XuanJian_config_enable_sect_announcement", broadcastSect));
		SetBroadcastDeath(ReadBool(modConfig, "XuanJian_config_enable_death_announcement", broadcastDeath));
		SetBroadcastLingWu(ReadBool(modConfig, "XuanJian_config_enable_lingwu_announcement", broadcastLingWu));
		SetBroadcastHighRealm(ReadBool(modConfig, "XuanJian_config_enable_highrealm_announcement", broadcastHighRealm));
		SetBroadcastTreasureMilestone(ReadBool(modConfig, "XuanJian_config_enable_treasure_milestone_announcement", broadcastTreasureMilestone));
		SetBroadcastFamilyInheritance(ReadBool(modConfig, "XuanJian_config_enable_family_inheritance_announcement", broadcastFamilyInheritance));
		SetBroadcastHighRealmInfluence(ReadBool(modConfig, "XuanJian_config_enable_highrealm_influence_announcement", broadcastHighRealmInfluence));
		SetCultivationEnabled(ReadBool(modConfig, "XuanJian_config_enable_cultivation", cultivationEnabled));
		SetShowFpsOverlay(ReadBool(modConfig, "XuanJian_config_show_fps_overlay", showFpsOverlay));
		SetAllowSectRebellion(ReadBool(modConfig, "XuanJian_config_allow_sect_rebellion", allowSectRebellion));
		SetSpawnJinXingYaoXie(ReadBool(modConfig, "XuanJian_config_enable_yao_xie_generation", spawnJinXingYaoXie));
		SetSpawnLongShu(ReadBool(modConfig, "XuanJian_config_enable_longshu_generation", spawnLongShu));
		SetQiYuDongTianSpawnYears(ReadInt(modConfig, "XuanJian_config_qiyu_dongtian_spawn_years", 300));
		SetJinDanDongTianCultivateYears(ReadInt(modConfig, "XuanJian_config_jindan_dongtian_cultivate_years", 500));
		SetJinDanPostPeaceYears(ReadInt(modConfig, "XuanJian_config_jindan_post_peace_years", 10));
		SetAptitudeGrantChancePercent(ReadInt(modConfig, "XuanJian_config_aptitude_grant_chance_percent", aptitudeGrantChancePercent));
		SetQiuJinFaChancePermille(ReadInt(modConfig, "XuanJian_config_qiujinfa_chance_permille", qiuJinFaChancePermille));
	}

	internal static void SetAutoCollectZhuJi(bool value) => SetBool(ref autoCollectZhuJi, value);
	internal static void SetAutoCollectZiFu(bool value) => SetBool(ref autoCollectZiFu, value);
	internal static void SetAutoCollectJinDan(bool value) => SetBool(ref autoCollectJinDan, value);
	internal static void SetAutoCollectTianShouDaoMai(bool value) => SetBool(ref autoCollectTianShouDaoMai, value);
	internal static void SetAutoCollectZiFuReincarnation(bool value) => SetBool(ref autoCollectZiFuReincarnation, value);
	internal static void SetAutoCollectJinDanReincarnation(bool value) => SetBool(ref autoCollectJinDanReincarnation, value);
	internal static void SetAutoCollectFaBaoOwner(bool value) => SetBool(ref autoCollectFaBaoOwner, value);
	internal static void SetAutoCollectSwordImmortal(bool value) => SetBool(ref autoCollectSwordImmortal, value);
	internal static void SetBroadcastTreasureMilestone(bool value) => SetBool(ref broadcastTreasureMilestone, value);
	internal static void SetBroadcastZiFu(bool value) => SetBroadcastHighRealm(value);
	internal static void SetBroadcastYaoCai(bool value) { }
	internal static void SetBroadcastLianDan(bool value) => SetBroadcastTreasureMilestone(value);
	internal static void SetBroadcastLianQiArtifact(bool value) => SetBroadcastTreasureMilestone(value);
	internal static void SetBroadcastBottleneck(bool value) => SetBool(ref broadcastBottleneck, value);
	internal static void SetBroadcastTalisman(bool value) { }
	internal static void SetBroadcastFormation(bool value) { }
	internal static void SetBroadcastGongFaWrite(bool value)
	{
		SetBool(ref broadcastGongFaWrite, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.GongFaWrite);
		}
	}
	internal static void SetBroadcastDongTian(bool value) => SetBool(ref broadcastDongTian, value);
	internal static void SetBroadcastSecretRealmTraining(bool value) => SetBroadcastDongTian(value);
	internal static void SetBroadcastYinSi(bool value) => SetBool(ref broadcastYinSi, value);
	internal static void SetBroadcastSect(bool value) => SetBool(ref broadcastSect, value);
	internal static void SetBroadcastDeath(bool value) => SetBool(ref broadcastDeath, value);
	internal static void SetBroadcastLingWu(bool value) => SetBool(ref broadcastLingWu, value);
	internal static void SetBroadcastHighRealm(bool value) => SetBool(ref broadcastHighRealm, value);
	internal static void SetBroadcastFamilyInheritance(bool value)
	{
		SetBool(ref broadcastFamilyInheritance, value);
		if (!value)
		{
			XuanJianVNext.Systems.Broadcast.XjBroadcastSystem.DiscardPendingCategory(
				XuanJianVNext.Systems.Broadcast.XjAnnouncementCategory.FamilyInheritance);
		}
	}
	internal static void SetBroadcastHighRealmInfluence(bool value) => SetBool(ref broadcastHighRealmInfluence, value);
	internal static void SetCultivationEnabled(bool value) => SetBool(ref cultivationEnabled, value);
	internal static void SetShowFpsOverlay(bool value) => SetBool(ref showFpsOverlay, value);
	internal static void SetAllowSectRebellion(bool value) => SetBool(ref allowSectRebellion, value);
	internal static void SetSpawnJinXingYaoXie(bool value) => SetBool(ref spawnJinXingYaoXie, value);
	internal static void SetSpawnLongShu(bool value) => SetBool(ref spawnLongShu, value);
	internal static void SetAptitudeGrantChancePercent(int value) => SetInt(ref aptitudeGrantChancePercent, Math.Clamp(value, 25, 40));
	internal static void SetQiuJinFaChancePermille(int value) => SetInt(ref qiuJinFaChancePermille, Math.Clamp(value, 1, 50));

	internal static void SetQiYuDongTianSpawnYears(int value)
	{
		int clamped = Math.Clamp(value, 100, 500);
		SetRange(ref qiYuDongTianSpawnYears, new XjYearRange(clamped, clamped));
	}

	internal static void SetQiYuDongTianSpawnYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(300, 300));
		int midpoint = range.Min + (range.Max - range.Min) / 2;
		SetQiYuDongTianSpawnYears(midpoint);
	}

	internal static void SetJinDanDongTianCultivateYears(int value)
	{
		SetInt(ref jinDanDongTianCultivateYears, Math.Clamp(value, 1, 1000));
	}

	internal static void SetJinDanPostPeaceYears(int value)
	{
		SetInt(ref jinDanPostPeaceYears, Math.Clamp(value, 1, 100));
	}

	// 兼容旧版 TEXT 配置文件；加载到的区间取中值，随后由滑块固化。
	internal static void SetJinDanDongTianCultivateYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(500, 500));
		SetJinDanDongTianCultivateYears(range.Min + (range.Max - range.Min) / 2);
	}

	internal static void SetJinDanPostPeaceYears(string value)
	{
		XjYearRange range = ParseRange(value, new XjYearRange(10, 10));
		SetJinDanPostPeaceYears(range.Min + (range.Max - range.Min) / 2);
	}

	private static void SetBool(ref bool field, bool value)
	{
		if (field == value)
		{
			return;
		}

		field = value;
		revision++;
	}

	private static void SetInt(ref int field, int value)
	{
		if (field == value)
		{
			return;
		}
		field = value;
		revision++;
	}

	private static void SetRange(ref XjYearRange field, XjYearRange value)
	{
		if (field.Min == value.Min && field.Max == value.Max)
		{
			return;
		}

		field = value;
		revision++;
	}

	internal static int RollQiYuDongTianSpawnOffset()
	{
		return qiYuDongTianSpawnYears.Roll(SharedRandom);
	}

	internal static int RollJinDanDongTianCultivateYears()
	{
		return jinDanDongTianCultivateYears;
	}

	internal static int RollJinDanPostPeaceYears()
	{
		return jinDanPostPeaceYears;
	}

	internal static bool ShouldAutoCollectReincarnation(string mode)
	{
		return string.Equals(mode, ReincarnationModeZiFuJinXing, StringComparison.Ordinal)
			? autoCollectZiFuReincarnation
			: autoCollectJinDanReincarnation;
	}

	private static XjYearRange ParseRange(string raw, XjYearRange fallback)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}

		string normalized = raw.Trim().Replace('－', '-').Replace('—', '-').Replace('~', '-');
		string[] parts = normalized.Split(new[] { '-' }, StringSplitOptions.RemoveEmptyEntries);
		if (parts.Length == 1
			&& int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int single))
		{
			return new XjYearRange(single, single);
		}

		if (parts.Length >= 2
			&& int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int min)
			&& int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int max))
		{
			return new XjYearRange(min, max);
		}

		return fallback;
	}

	private static object GetConfigItem(object config, string id)
	{
		if (config == null || string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		if (TryGetConfigItemFromModConfig(config, id, out object directItem))
		{
			return directItem;
		}

		if (TryFindConfigItem(config, id, 0, out object reflectedItem))
		{
			return reflectedItem;
		}

		return null;
	}

	private static bool TryGetConfigItemFromModConfig(object config, string id, out object item)
	{
		item = null;
		if (config is not ModConfig typedConfig)
		{
			return false;
		}

		try
		{
			var items = typedConfig["ConfigItems"];
			if (TryFindConfigItem(items, id, 0, out item))
			{
				return true;
			}
		}
		catch
		{
		}

		try
		{
			object direct = typedConfig[id];
			if (direct != null)
			{
				item = direct;
				return true;
			}
		}
		catch
		{
		}

		return false;
	}

	private static bool TryFindConfigItem(object source, string id, int depth, out object item)
	{
		item = null;
		if (source == null || string.IsNullOrWhiteSpace(id) || depth > 4)
		{
			return false;
		}

		if (IsConfigItemId(source, id))
		{
			item = source;
			return true;
		}

		if (source is IDictionary dictionary)
		{
			foreach (DictionaryEntry entry in dictionary)
			{
				if (IsIdMatch(entry.Key, id))
				{
					item = entry.Value;
					return true;
				}

				if (TryFindConfigItem(entry.Value, id, depth + 1, out item))
				{
					return true;
				}
			}
		}

		if (source is IEnumerable enumerable && source is not string)
		{
			foreach (object element in enumerable)
			{
				if (TryFindConfigItem(element, id, depth + 1, out item))
				{
					return true;
				}
			}
		}

		if (depth > 0)
		{
			return false;
		}

		Type type = source.GetType();
		PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
		for (int i = 0; i < properties.Length; i++)
		{
			PropertyInfo property = properties[i];
			if (property.GetIndexParameters().Length != 0 || property.GetMethod == null)
			{
				continue;
			}

			try
			{
				object value = property.GetMethod.Invoke(source, null);
				if (TryFindConfigItem(value, id, depth + 1, out item))
				{
					return true;
				}
			}
			catch
			{
			}
		}

		FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
		for (int i = 0; i < fields.Length; i++)
		{
			try
			{
				object value = fields[i].GetValue(source);
				if (TryFindConfigItem(value, id, depth + 1, out item))
				{
					return true;
				}
			}
			catch
			{
			}
		}

		return false;
	}

	private static bool IsConfigItemId(object source, string id)
	{
		return TryReadMember(source, "Id", out object value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ID", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "id", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "Name", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "name", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "Key", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "key", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ConfigId", out value) && IsIdMatch(value, id)
			|| TryReadMember(source, "ConfigID", out value) && IsIdMatch(value, id);
	}

	private static bool IsIdMatch(object value, string id)
	{
		return value != null && string.Equals(Convert.ToString(value, CultureInfo.InvariantCulture), id, StringComparison.Ordinal);
	}

	private static bool ReadBool(object config, string id, bool fallback)
	{
		object item = GetConfigItem(config, id);
		if (item == null)
		{
			return fallback;
		}

		try
		{
			if (TryReadMember(item, "BoolVal", out object value)
				|| TryReadMember(item, "Value", out value)
				|| TryReadMember(item, "value", out value))
			{
				if (value is bool boolValue)
				{
					return boolValue;
				}

				if (bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out bool parsed))
				{
					return parsed;
				}
			}
		}
		catch
		{
		}

		return fallback;
	}

	private static int ReadInt(object config, string id, int fallback)
	{
		object item = GetConfigItem(config, id);
		if (item == null)
		{
			return fallback;
		}

		try
		{
			if (TryReadMember(item, "IntVal", out object value)
				|| TryReadMember(item, "Value", out value)
				|| TryReadMember(item, "value", out value))
			{
				if (value is int intValue) return intValue;
				if (int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
				{
					return parsed;
				}
			}

			// 旧 TEXT 版兼容：区间取中值。
			if (TryReadMember(item, "TextVal", out value))
			{
				XjYearRange range = ParseRange(Convert.ToString(value, CultureInfo.InvariantCulture), new XjYearRange(fallback, fallback));
				return range.Min + (range.Max - range.Min) / 2;
			}
		}
		catch
		{
		}
		return fallback;
	}

	private static string ReadText(object config, string id, string fallback)
	{
		object item = GetConfigItem(config, id);
		if (item == null)
		{
			return fallback;
		}

		try
		{
			if (TryReadMember(item, "TextVal", out object value)
				|| TryReadMember(item, "Value", out value)
				|| TryReadMember(item, "value", out value))
			{
				string text = Convert.ToString(value, CultureInfo.InvariantCulture);
				return string.IsNullOrWhiteSpace(text) ? fallback : text;
			}
		}
		catch
		{
		}

		return fallback;
	}

	private static bool TryReadMember(object source, string memberName, out object value)
	{
		value = null;
		if (source == null || string.IsNullOrWhiteSpace(memberName))
		{
			return false;
		}

		Type type = source.GetType();
		PropertyInfo property = type.GetProperty(memberName, BindingFlags.Public | BindingFlags.Instance);
		if (property?.GetMethod != null && property.GetIndexParameters().Length == 0)
		{
			try
			{
				value = property.GetMethod.Invoke(source, null);
				return true;
			}
			catch
			{
			}
		}

		FieldInfo field = type.GetField(memberName, BindingFlags.Public | BindingFlags.Instance);
		if (field != null)
		{
			try
			{
				value = field.GetValue(source);
				return true;
			}
			catch
			{
			}
		}

		return false;
	}
}
