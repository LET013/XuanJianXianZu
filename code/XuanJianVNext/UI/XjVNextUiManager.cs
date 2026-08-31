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
using XuanJianVNext.Systems.Shi;
using XuanJianVNext.UI.Map;
using XuanJianVNext.UI.Shi;

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
			RegisterPowerTabLocalization();
			Sprite tabIcon = TrySprite("ui/XuanJian.png", "ui/icons/iconTab.png", "ui/icons/iconBook.png");
			tab = TabManager.CreateTab("xuanjian_mod_tab", "xuanjian_power_tab_title", "xuanjian_power_tab_description", tabIcon);
			if ((UnityEngine.Object)(object)tab != (UnityEngine.Object)null)
			{
				AddFeatureButtons();
			}
		}
		catch (Exception ex)
		{
			inited = false;
			Debug.LogError("[玄鉴][界面] 功能入口初始化失败：" + ex);
		}
	}

	private static void AddFeatureButtons()
	{
		if ((UnityEngine.Object)(object)tab == (UnityEngine.Object)null)
		{
			return;
		}

		TryUiStep("按钮文本", RegisterFeatureButtonTexts);
		// 参考 Cultiway / 宿命之环 / 武极 / 西幻：自定义主功能栏只走 NML 的
		// WrappedPowersTab 标准链。不要再混用 AddButtonToTab 与自定义组，否则 native
		// _power_buttons / wrapper group 的布局状态可能分裂。
		TryUiStep("功能栏布局", () => tab.SetLayout(new System.Collections.Generic.List<string> { "tools" }));
		TryUiStep("玄鉴设置按钮", () => AddButton("xuanjian.mod_settings", ShowModSettingsWindow, "ui/icons/iconOptions", "ui/icons/iconoptions", "ui/icons/iconJianBook"));
		TryUiStep("玄鉴仙鉴按钮", () => AddButton("xuanjian.codex", () => XjCodexWindow.Show(), "ui/Icons/items/XuanJianCodex", "ui/icons/iconJianBook", "ui/icons/iconBook", "ui/XuanJian.png"));
		TryUiStep("玄鉴修士榜按钮", () => AddButton("xuanjian.rank", XjVNextFeatureWindows.ShowRankWindow, "ui/Icons/items/PaiHangBang", "ui/icons/iconJianBook", "ui/icons/iconBook"));
		TryUiStep("登名石按钮", () => AddButton("xuanjian.dengmingshi", XjVNextFeatureWindows.ShowDengMingShiWindow, "ui/Icons/items/DengMin", "ui/icons/iconBook"));
		TryUiStep("旃檀林按钮", () =>
		{
			PowerButton placementButton = XjZhantanlinPlacementUi.CreatePlacementButton();
			if ((UnityEngine.Object)(object)placementButton != (UnityEngine.Object)null)
			{
				tab.AddPowerButton("tools", placementButton);
			}
		});
		TryUiStep("宗门图层按钮", XjSectMapLayerSystem.EnsurePowerRegistered);
		TryUiStep("功能栏最终布局", () => tab.UpdateLayout());
	}

	private static void TryUiStep(string step, Action action)
	{
		try
		{
			action?.Invoke();
		}
		catch (Exception ex)
		{
			Debug.LogError("[玄鉴][界面] " + (step ?? "未知步骤") + "初始化失败，已隔离该功能：" + ex);
		}
	}

	private static void RegisterPowerTabLocalization()
	{
		try
		{
			if (LocalizedTextManager.instance == null) return;
			LocalizedTextManager.add("xuanjian_power_tab_title", "玄鉴仙族", false, string.Empty, true);
			LocalizedTextManager.add("xuanjian_power_tab_description", "玄鉴仙族功能入口", false, string.Empty, true);
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("XjVNextUiManager.RegisterPowerTabLocalization", ex);
		}
	}

	private static void RegisterFeatureButtonTexts()
	{
		string[] ids =
		{
			"xuanjian.mod_settings",
			"xuanjian.codex",
			"xuanjian.rank",
			"xuanjian.dengmingshi",
			"xuanjian.zhantanlin"
		};
		for (int i = 0; i < ids.Length; i++)
		{
			GetButtonText(ids[i], out string title, out string description);
			XjNativeHoverTooltip.RegisterPassthrough(title, description);
		}
	}


	private static void ShowModSettingsWindow()
	{
		if (XuanJianVNext.XuanJianMod.I == null)
		{
			return;
		}

		RegisterShiConfigLocalization();
		NeoModLoader.ui.ModConfigureWindow.ShowWindow(XuanJianVNext.XuanJianMod.I.GetConfig());
		SanitizeConfigWindowText();
	}

	private static void RegisterShiConfigLocalization()
	{
		try
		{
			if (LocalizedTextManager.instance == null) return;
			RegisterConfigLocalization("XuanJian_config_auto_collect_shi_mohe",
				"摩诃自动收藏", "释修晋升摩诃时自动加入收藏夹；转世重塑后保持原收藏状态");
			RegisterConfigLocalization("XuanJian_config_auto_collect_shi_dharma_form",
				"法相与世尊自动收藏", "释修晋升法相或世尊时自动加入收藏夹");
			RegisterConfigLocalization("XuanJian_config_enable_shi_announcement",
				"【释修】入释、位次与释土",
				"入释、摩诃与法相晋升、庙主得地、三十二天重组、真灵归返或俱灭、七相摄生等释修大事发布天下公告；超额事件仍保留在仙鉴与三书");
			RegisterConfigLocalization("XuanJian_config_daotai_merit_gain_curve_percent",
				"功绩比例设置",
				"调节天地功绩的获取幅度。向左降低，向右提高；只影响此后获得的功绩，不改动已经记下的功绩。");
		}
		catch (Exception ex)
		{
			XuanJianVNext.Core.XjExceptionDiagnostics.Report("XjVNextUiManager.RegisterShiConfigLocalization", ex);
		}
	}

	private static void RegisterConfigLocalization(string id, string title, string description)
	{
		LocalizedTextManager.add(id, title, false, string.Empty, true);
		LocalizedTextManager.add(id + " Description", description, false, string.Empty, true);
		LocalizedTextManager.add(id + " description", description, false, string.Empty, true);
		LocalizedTextManager.add(id + "Description", description, false, string.Empty, true);
		LocalizedTextManager.add(id + "_description", description, false, string.Empty, true);
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
		catch (System.Exception xjCaught119) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/XjVNextUiManager.cs:119", xjCaught119); }
	}

	private static string TranslateConfigText(string text)
	{
		switch ((text ?? string.Empty).Trim())
		{
			case "XuanJian_config_auto_collect_zhuji": return "筑基境自动收藏";
			case "XuanJian_config_auto_collect_zifu": return "紫府境自动收藏";
			case "XuanJian_config_auto_collect_jindan": return "金丹境自动收藏";
			case "XuanJian_config_auto_collect_fuqi_zhenren": return "服气养性真人自动收藏";
			case "XuanJian_config_auto_collect_fuqi_zhenjun": return "服气养性真君自动收藏";
			case "XuanJian_config_auto_collect_kongzheng_zhenjun": return "空证真君自动收藏";
			case "XuanJian_config_auto_collect_tianshoudaomai": return "天授道脉自动收藏";
			case "XuanJian_config_auto_collect_zifu_reincarnation": return "紫府金性转生自动收藏";
			case "XuanJian_config_auto_collect_jindan_reincarnation": return "金丹转生自动收藏";
			case "XuanJian_config_auto_collect_fabao_owner": return "法宝主人自动收藏";
			case "XuanJian_config_auto_collect_sword_immortal": return "剑仙自动收藏";
			case "XuanJian_config_auto_collect_shi_mohe": return "摩诃自动收藏";
			case "XuanJian_config_auto_collect_shi_mohe Description": return "释修晋升摩诃时自动加入收藏夹；转世重塑后保持原收藏状态";
			case "XuanJian_config_auto_collect_shi_dharma_form": return "法相与世尊自动收藏";
			case "XuanJian_config_auto_collect_shi_dharma_form Description": return "释修晋升法相或世尊时自动加入收藏夹";
			case "XuanJian_config_enable_zifu_promotion_announcement": return "【修行】紫府晋升";
			case "XuanJian_config_enable_yaocai_announcement": return "【资源】采药所得";
			case "XuanJian_config_enable_liandan_announcement": return "【百艺】炼丹";
			case "XuanJian_config_enable_lianqi_artifact_announcement": return "【百艺】炼器与重宝";
			case "XuanJian_config_enable_bottleneck_announcement": return "【修行】瓶颈与冲关";
			case "XuanJian_config_enable_talisman_announcement": return "【百艺】符箓";
			case "XuanJian_config_enable_formation_announcement": return "【百艺】阵法";
			case "XuanJian_config_enable_gongfa_write_announcement": return "【传承】功法入库";
			case "XuanJian_config_enable_dongtian_announcement": return "【洞天】显世、归属与关闭";
			case "XuanJian_config_enable_secret_realm_training_announcement": return "【洞天】福地试炼";
			case "XuanJian_config_enable_yinsi_announcement": return "【阴司】追索与离去";
			case "XuanJian_config_enable_sect_announcement": return "【宗门】治理与冲突";
			case "XuanJian_config_enable_cultivation": return "允许修炼";
			case "XuanJian_config_show_fps_overlay": return "性能信息显示";
			case "XuanJian_config_performance_observation": return "高负载性能观测";
			case "XuanJian_config_highspeed_enemy_search_backoff": return "高倍速空寻敌退避";
			case "XuanJian_config_stage0_observation": return "年度链路与长局生态观测";
			case "XuanJian_config_enable_longrun_memory_maintenance": return "长期运行内存整理";
			case "XuanJian_config_allow_sect_rebellion": return "允许宗门叛乱";
			case "XuanJian_config_enable_death_announcement": return "【生死】高境陨落";
			case "XuanJian_config_enable_lingwu_announcement": return "【资源】灵物与金性";
			case "XuanJian_config_enable_highrealm_announcement": return "【修行】高境证道与晋升";
			case "XuanJian_config_enable_shi_announcement": return "【释修】入释、位次与释土";
			case "XuanJian_config_enable_shi_announcement Description": return "入释、摩诃与法相晋升、庙主得地、三十二天重组、真灵归返或俱灭、七相摄生等释修大事发布天下公告；超额事件仍保留在仙鉴与三书";
			case "XuanJian_config_enable_shentong_announcement": return "【神通】悟法、易象与退显";
			case "XuanJian_config_enable_authority_position_announcement": return "【果位】权柄与余闰位序";
			case "XuanJian_config_enable_treasure_milestone_announcement": return "【重宝】灵宝、法宝与延寿丹";
			case "XuanJian_config_enable_family_inheritance_announcement": return "【家族】族议与传承";
			case "XuanJian_config_enable_highrealm_influence_announcement": return "【家族】上修扶持与干预";
			case "XuanJian_config_daotai_merit_gain_curve_percent": return "功绩比例设置";
			case "XuanJian_config_daotai_merit_gain_curve_percent Description": return "调节天地功绩的获取幅度。向左降低，向右提高；只影响此后获得的功绩，不改动已经记下的功绩。";
			case "XuanJian_config_daotai_merit_gain_curve_percent description": return "调节天地功绩的获取幅度。向左降低，向右提高；只影响此后获得的功绩，不改动已经记下的功绩。";
			case "XuanJian_config_daotai_merit_gain_curve_percentDescription": return "调节天地功绩的获取幅度。向左降低，向右提高；只影响此后获得的功绩，不改动已经记下的功绩。";
			case "XuanJian_config_daotai_merit_gain_curve_percent_description": return "调节天地功绩的获取幅度。向左降低，向右提高；只影响此后获得的功绩，不改动已经记下的功绩。";
			case "XuanJian_config_aptitude_grant_chance_percent": return "修炼特质获得概率";
			case "XuanJian_config_qiujinfa_chance_permille": return "求金法参悟概率";
			case "XuanJian_config_sword_intent_chance_percent": return "剑意领悟概率（%）";
			case "XuanJian_config_enable_yao_xie_generation": return "金性妖邪生成";
			case "XuanJian_config_enable_longshu_generation": return "龙属生成";
			case "XuanJian_config_qiyu_dongtian_spawn_years": return "奇遇洞天出现间隔";
			case "XuanJian_config_jindan_dongtian_cultivate_years": return "金丹洞天闭关时长";
			case "XuanJian_config_jindan_post_peace_years": return "金丹游历期时长";
			case "XuanJian_config_daotai_beyond_world": return "道胎远遁天外";
			case "XuanJian_config_daotai_travel": return "道胎游历";
			case "XuanJian_config_daotai_beyond_world_years": return "道胎远遁天外时长";
			case "XuanJian_config_daotai_travel_years": return "道胎游历期时长";
			case "XuanJian_config_fabao_sparkle_primary_color": return "法宝星光主色";
			case "XuanJian_config_fabao_sparkle_secondary_color": return "法宝星光副色";
			default: return HideInternalConfigKey(text);
		}
	}

	private static string HideInternalConfigKey(string text)
	{
		string normalized = (text ?? string.Empty).Trim();
		if (normalized.StartsWith("XuanJian_config_", StringComparison.Ordinal))
		{
			return normalized.EndsWith(" Description", StringComparison.Ordinal)
				? "调整玄鉴仙族相关规则。"
				: "玄鉴设置";
		}
		return text ?? string.Empty;
	}

	private static void AddButton(string id, UnityAction action, params string[] iconKeys)
	{
		AddButtonInternal(id, action, true, iconKeys);
	}


	private static void AddButtonInternal(
		string id, UnityAction action, bool clearAfterAction, params string[] iconKeys)
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
				if (clearAfterAction) ClearSelectedPowerButton();
			},
			TrySprite(iconKeys),
			(Transform)null,
			default(Vector2));
		ApplyButtonTooltip(button, id);
		tab.AddPowerButton("tools", button);
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
		catch (System.Exception xjCaught223) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/XjVNextUiManager.cs:223", xjCaught223); }
	}

	private static void GetButtonText(string id, out string title, out string description)
	{
		switch (id ?? string.Empty)
		{
			case "xuanjian.mod_settings":
				title = "玄鉴设置";
				description = "调整玄鉴仙族通知、自动收藏、宗门叛乱与洞天等运行选项。";
				return;
			case "xuanjian.codex":
				title = "玄鉴仙鉴";
				description = "查看天下总览、宗门、家族、城镇、洞天、历史等玄鉴资料。";
				return;
			case "xuanjian.rank":
				title = "玄鉴修士榜";
				description = "按战力、境界、道途、特质和角色名字筛选修士。";
				return;
			case "xuanjian.daotu_counter":
				title = "道途克制";
				description = "查看各道途之间的克制关系与当前已登记道途。";
				return;
			case "xuanjian.guowei_quanbing":
				title = "果位权柄";
				description = "查看果位、权柄和相关道途状态。";
				return;
			case "xuanjian.dengmingshi":
				title = "登名石";
				description = "保存、放出和管理跨世界角色。";
				return;
			case "xuanjian.zhantanlin":
				title = "开辟旃檀林";
				description = "点击后在地图上选择中心，以固定范围开辟旃檀佛土，并在外圈立起光墙。";
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
		catch (System.Exception xjCaught277) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/UI/XjVNextUiManager.cs:277", xjCaught277); }
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
