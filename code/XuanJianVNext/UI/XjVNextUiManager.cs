using System;
using NeoModLoader.General;
using NeoModLoader.General.UI.Tab;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.UI.DaoTu;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Codex;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.DengMingShi;
using XuanJianVNext.UI.GuoWei;
using XuanJianVNext.UI.Rank;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.UI.ShenBing;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.Systems.Map;

namespace XuanJianVNext.UI;

public static class XjVNextUiManager
{
	private static bool inited;

	private static PowersTab tab;

	public static void Init()
	{
		if (inited)
		{
			return;
		}

		try
		{
			inited = true;
			Sprite tabIcon = TrySprite("ui/XuanJian.png", "ui/icons/iconTab.png", "ui/icons/iconBook.png");
			tab = TabManager.CreateTab("xuanjian_mod_tab", "玄鉴仙族", "玄鉴仙族模组功能入口", tabIcon, "hotkey_tip_tab_other");
			if ((UnityEngine.Object)(object)tab != (UnityEngine.Object)null)
			{
				AddFeatureButtons();
			}
		}
		catch (Exception ex)
		{
			inited = false;
			Debug.LogError("[玄鉴][UI] 功能入口初始化失败：" + ex);
		}
	}

	private static void AddFeatureButtons()
	{
		if ((UnityEngine.Object)(object)tab == (UnityEngine.Object)null)
		{
			return;
		}

		RegisterFeatureButtonTexts();
		PowersTabExtension.SetLayout(tab, new System.Collections.Generic.List<string> { "tab" });
		AddButton("xuanjian.mod_settings", ShowModSettingsWindow, "ui/icons/iconOptions", "ui/icons/iconoptions", "ui/icons/iconJianBook");
		AddButton("xuanjian.codex", () => XjCodexWindow.Show(), "ui/Icons/items/XuanJianCodex", "ui/icons/iconJianBook", "ui/icons/iconBook", "ui/XuanJian.png");
		AddButton("xuanjian.rank", XjVNextFeatureWindows.ShowRankWindow, "ui/Icons/items/PaiHangBang", "ui/icons/iconJianBook");
		AddButton("xuanjian.daotu_counter", ShowDaoTuCounterWindow, "ui/Icons/items/DaoTuKeZhi", "ui/icons/iconTab", "ui/icons/iconBook");
		AddButton("xuanjian.guowei_quanbing", XjVNextFeatureWindows.ShowGuoWeiQuanBingWindow, "ui/Icons/items/GuoWeiQuanBing", "ui/icons/iconBook");
		AddButton("xuanjian.dengmingshi", XjVNextFeatureWindows.ShowDengMingShiWindow, "ui/Icons/items/DengMin", "ui/icons/iconBook");
		AddButton("xuanjian.shenbing", XjVNextFeatureWindows.ShowShenBingWindow, "ui/Icons/items/FaBaoZongLan", "ui/icons/iconBook");
		XjSectMapLayerSystem.EnsurePowerRegistered();
		AddButton("xuanjian.sect_map_layer", XjSectMapLayerSystem.ToggleFromUi, "ui/Icons/event/ZongMenChongTu", "ui/icons/iconWorldInfo");
	}

	private static void RegisterFeatureButtonTexts()
	{
		string[] ids =
		{
			"xuanjian.mod_settings",
			"xuanjian.codex",
			"xuanjian.rank",
			"xuanjian.daotu_counter",
			"xuanjian.guowei_quanbing",
			"xuanjian.dengmingshi",
			"xuanjian.shenbing",
			"xuanjian.sect_map_layer"
		};
		for (int i = 0; i < ids.Length; i++)
		{
			GetButtonText(ids[i], out string title, out string description);
			XjNativeHoverTooltip.RegisterPassthrough(title, description);
		}
	}

	private static void ShowDaoTuCounterWindow()
	{
		XjDaoTuCounterWindow.ShowWindow();
	}

	private static void ShowModSettingsWindow()
	{
		if (XuanJianVNext.XuanJianMod.I == null)
		{
			return;
		}

		NeoModLoader.ui.ModConfigureWindow.ShowWindow(XuanJianVNext.XuanJianMod.I.GetConfig());
		SanitizeConfigWindowText();
	}

	private static void SanitizeConfigWindowText()
	{
		try
		{
			Text[] labels = UnityEngine.Object.FindObjectsOfType<Text>();
			for (int i = 0; i < labels.Length; i++)
			{
				Text label = labels[i];
				if ((UnityEngine.Object)(object)label == (UnityEngine.Object)null)
				{
					continue;
				}

				string translated = TranslateConfigText(label.text);
				if (!string.Equals(translated, label.text, StringComparison.Ordinal))
				{
					label.text = translated;
				}
			}
		}
		catch
		{
		}
	}

	private static string TranslateConfigText(string text)
	{
		switch ((text ?? string.Empty).Trim())
		{
			case "XuanJian_config_auto_collect_zhuji": return "筑基境自动收藏";
			case "XuanJian_config_auto_collect_zifu": return "紫府境自动收藏";
			case "XuanJian_config_auto_collect_jindan": return "金丹境自动收藏";
			case "XuanJian_config_auto_collect_tianshoudaomai": return "天授道脉自动收藏";
			case "XuanJian_config_auto_collect_zifu_reincarnation": return "紫府金性转生自动收藏";
			case "XuanJian_config_auto_collect_jindan_reincarnation": return "金丹转生自动收藏";
			case "XuanJian_config_auto_collect_fabao_owner": return "法宝主人自动收藏";
			case "XuanJian_config_auto_collect_sword_immortal": return "剑仙自动收藏";
			case "XuanJian_config_enable_zifu_promotion_announcement": return "【修行】紫府晋升";
			case "XuanJian_config_enable_yaocai_announcement": return "【资源】采药所得";
			case "XuanJian_config_enable_liandan_announcement": return "【百艺】炼丹";
			case "XuanJian_config_enable_lianqi_artifact_announcement": return "【百艺】炼器与重宝";
			case "XuanJian_config_enable_bottleneck_announcement": return "【修行】瓶颈与冲关";
			case "XuanJian_config_enable_talisman_announcement": return "【百艺】符箓";
			case "XuanJian_config_enable_formation_announcement": return "【百艺】阵法";
			case "XuanJian_config_enable_gongfa_write_announcement": return "【传承】功法入库";
			case "XuanJian_config_enable_dongtian_announcement": return "【洞天】显世与归属";
			case "XuanJian_config_enable_secret_realm_training_announcement": return "【洞天】福地试炼";
			case "XuanJian_config_enable_yinsi_announcement": return "【阴司】追索与离去";
			case "XuanJian_config_enable_sect_announcement": return "【宗门】治理与冲突";
			case "XuanJian_config_enable_cultivation": return "允许修炼";
			case "XuanJian_config_allow_sect_rebellion": return "允许宗门叛乱";
			case "XuanJian_config_enable_death_announcement": return "【生死】高境陨落";
			case "XuanJian_config_enable_lingwu_announcement": return "【资源】灵物现世";
			case "XuanJian_config_enable_highrealm_announcement": return "【修行】高境与果位";
			case "XuanJian_config_enable_treasure_milestone_announcement": return "【重宝】延寿丹与法宝";
			case "XuanJian_config_enable_family_inheritance_announcement": return "【家族】族议与传承";
			case "XuanJian_config_enable_highrealm_influence_announcement": return "【家族】上修扶持与干预";
			case "XuanJian_config_qiujinfa_chance_permille": return "求金法参悟概率";
			case "XuanJian_config_enable_yao_xie_generation": return "金性妖邪生成";
			case "XuanJian_config_enable_longshu_generation": return "龙属生成";
			case "XuanJian_config_qiyu_dongtian_spawn_years": return "奇遇洞天出现间隔";
			case "XuanJian_config_jindan_dongtian_cultivate_years": return "金丹洞天闭关时长";
			case "XuanJian_config_jindan_post_peace_years": return "金丹游历期时长";
			case "XuanJian_config_fabao_sparkle_primary_color": return "法宝星光主色";
			case "XuanJian_config_fabao_sparkle_secondary_color": return "法宝星光副色";
			default: return text ?? string.Empty;
		}
	}

	private static void AddButton(string id, UnityAction action, params string[] iconKeys)
	{
		if ((UnityEngine.Object)(object)tab == (UnityEngine.Object)null)
		{
			return;
		}

		PowerButton button = PowerButtonCreator.CreateSimpleButton(
			id,
			() =>
			{
				ClearSelectedPowerButton();
				action?.Invoke();
				ClearSelectedPowerButton();
			},
			TrySprite(iconKeys),
			(Transform)null,
			default(Vector2));
		ApplyButtonTooltip(button, id);
		PowersTabExtension.AddPowerButton(tab, "tab", button);
	}

	private static void ApplyButtonTooltip(PowerButton button, string id)
	{
		if ((UnityEngine.Object)(object)button == (UnityEngine.Object)null)
		{
			return;
		}

		try
		{
			TipButton tip = button.GetComponent<TipButton>() ?? button.gameObject.AddComponent<TipButton>();
			GetButtonText(id, out string title, out string description);
			XjNativeHoverTooltip.Ensure(tip, title, description, string.Empty);
		}
		catch
		{
		}
	}

	private static void GetButtonText(string id, out string title, out string description)
	{
		switch (id ?? string.Empty)
		{
			case "xuanjian.mod_settings":
				title = "模组设置";
				description = "调整玄鉴仙族通知、自动收藏、宗门叛乱与洞天等运行选项。";
				return;
			case "xuanjian.codex":
				title = "玄鉴仙鉴";
				description = "查看天下总览、宗门、家族、城镇、洞天、历史等玄鉴资料。";
				return;
			case "xuanjian.rank":
				title = "排行榜";
				description = "按战力、境界、道途、特质和角色名字筛选修士。";
				return;
			case "xuanjian.daotu_counter":
				title = "道途刻制";
				description = "查看各道途果位、正闰余位与当前占据情况。";
				return;
			case "xuanjian.guowei_quanbing":
				title = "果位权柄";
				description = "查看果位、权柄和相关道途状态。";
				return;
			case "xuanjian.dengmingshi":
				title = "登名石";
				description = "保存、放出和管理跨世界角色。";
				return;
			case "xuanjian.shenbing":
				title = "万宝录";
				description = "查看法器、灵宝、法宝与器物传承。";
				return;
			case "xuanjian.sect_map_layer":
				title = "宗门图层";
				description = "为宗门城镇着色，点击国家查看宗门峰脉。";
				return;
			default:
				title = string.IsNullOrWhiteSpace(id) ? "玄鉴功能" : id;
				description = "玄鉴仙族功能入口。";
				return;
		}
	}

	private static void ClearSelectedPowerButton()
	{
		try
		{
			PowerButtonSelector.instance?.unselectAll();
		}
		catch
		{
		}
	}

	private static Sprite TrySprite(params string[] keys)
	{
		for (int i = 0; i < keys.Length; i++)
		{
			string key = keys[i];
			if (string.IsNullOrWhiteSpace(key))
			{
				continue;
			}

			Sprite sprite = SpriteTextureLoader.getSprite(key);
			if ((UnityEngine.Object)(object)sprite != (UnityEngine.Object)null)
			{
				return sprite;
			}
		}

		return null;
	}
}

internal static class XjVNextFeatureWindows
{
	internal static void ShowRankWindow()
	{
		XjRankWindow.Show();
	}

	internal static void ShowGuoWeiQuanBingWindow()
	{
		XjGuoWeiQuanBingWindow.Show();
	}

	internal static void ShowDengMingShiWindow()
	{
		XjDengMingShiWindow.Show();
	}

	internal static void ShowShenBingWindow()
	{
		XjShenBingWindow.ShowWindow();
	}

	internal static void ShowZongMenWindow()
	{
		Actor actor = GetSelectedActorOrNull();
		if (actor?.data != null)
		{
			XjZongMenWindow.ShowForActor(actor);
			return;
		}

		City city = SelectedMetas.selected_city;
		if (((CoreSystemObject<CityData>)(object)city)?.data != null)
		{
			XjZongMenWindow.ShowForCity(city);
			return;
		}

		XjZongMenWindow.ShowForActor(null);
	}

	internal static void ShowFamilyChronicleWindow()
	{
		XjCodexWindow.Show(3);
	}

	private static Actor GetSelectedActorOrNull()
	{
		return XjDengMingShiCommands.TryGetSelectedActor(out Actor actor) ? actor : null;
	}
}
