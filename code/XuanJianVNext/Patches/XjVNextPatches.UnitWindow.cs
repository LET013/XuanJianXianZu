using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.UI.Family;
using XuanJianVNext.UI.Mentorship;
using XuanJianVNext.UI.QianKunDai;
using XuanJianVNext.Systems.DengMingShi;
using XuanJianVNext.UI.ActorInfo;
using XuanJianVNext.UI.Overview;
using XuanJianVNext.Data.CaiQi;
using static XuanJianVNext.UI.Family.XjFamilyInheritanceTabPatches;

namespace XuanJianVNext.Patches;

internal partial class XjVNextPatches
{
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UnitWindow), "OnEnable")]
	public static void XuanJianVNext_UnitWindow_OnEnable_FamilyInheritanceTab_Postfix(UnitWindow __instance)
	{
		try
		{
			XjActorOverviewStatsFormatter.Refresh(__instance, forceLayout: true);
			XjActorOverviewStatsFormatter.RequestDeferredRefresh(__instance);
			XjActorInfoPanelRenderer.Refresh(__instance);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴] UnitWindow UI注入异常(OnEnable): " + ex.Message);
		}
		XuanJianVNext_RemoveFamilyInheritanceTab(__instance);
		XjQianKunDaiTabPatches.TryAddOrUpdate(__instance);
		XjMentorshipTabPatches.TryAddOrUpdate(__instance);
		XuanJianVNext_TryAddDengMingShiSaveButton(__instance);
	}

	private static void XuanJianVNext_TryAddOrUpdateFamilyInheritanceTab_Guard(UnitWindow window)
	{
		if (window?.actor == null || window.actor.data == null)
		{
			XuanJianVNext_TryAddOrUpdateFamilyInheritanceTab(window);
			return;
		}
		if (XuanJianVNext.Systems.LongShu.XjLongShuSystem.IsLongShu(window.actor))
			return;
		XuanJianVNext_TryAddOrUpdateFamilyInheritanceTab(window);
	}

	private static Button _xuanJianDengMingShiSaveButton;
	private static Image _xuanJianDengMingShiButtonIcon;
	private static UnitWindow _xuanJianDengMingShiBoundWindow;
	private const string XuanJianDengMingShiSaveButtonName = "XuanJianDengMingShiSaveButton";

	/// <summary>
	/// 在单位窗口添加登名石快捷按钮（同 0.5.4：位于收藏按钮上方，支持保存/移除切换，颜色状态指示）。
	/// </summary>
	private static void XuanJianVNext_TryAddDengMingShiSaveButton(UnitWindow unitWindow)
	{
		if (unitWindow == null || unitWindow.transform == null
			|| unitWindow._icon_favorite == null)
		{
			return;
		}

		if (_xuanJianDengMingShiSaveButton == null)
		{
			// 首次初始化：克隆收藏按钮的父物体（同 0.5.4）
			Transform favParent = unitWindow._icon_favorite.transform.parent;
			GameObject buttonObj = UnityEngine.Object.Instantiate(
				favParent.gameObject,
				favParent.parent);
			buttonObj.name = XuanJianDengMingShiSaveButtonName;

			RectTransform rect = buttonObj.GetComponent<RectTransform>();
			rect.localPosition = new Vector3(116.8f, -112f);
			rect.localScale = Vector3.one;

			// 设置登名图标
			Transform iconTransform = buttonObj.transform.Find("Icon");
			if (iconTransform != null)
			{
				_xuanJianDengMingShiButtonIcon = iconTransform.GetComponent<Image>();
			}
			Sprite dengMinSprite = Resources.Load<Sprite>("ui/Icons/items/DengMin");
			if (_xuanJianDengMingShiButtonIcon != null && dengMinSprite != null)
			{
				_xuanJianDengMingShiButtonIcon.sprite = dengMinSprite;
			}

			_xuanJianDengMingShiSaveButton = buttonObj.GetComponent<Button>();

			// 设置工具提示
			TipButton tip = buttonObj.GetComponent<TipButton>();
			if (tip != null)
			{
				XjNativeHoverTooltip.Ensure(tip, "登名", "鉴中天地，登名入石", "点击保存/移除角色至登名石。");
			}
		}
		else
		{
			// 后续调用：确保按钮挂在正确的父级下
			Transform targetParent = unitWindow._icon_favorite?.transform?.parent?.parent;
			if (targetParent != null && _xuanJianDengMingShiSaveButton.transform.parent != targetParent)
			{
				_xuanJianDengMingShiSaveButton.transform.SetParent(targetParent, worldPositionStays: false);
				_xuanJianDengMingShiSaveButton.transform.localPosition = new Vector3(116.8f, -112f);
				_xuanJianDengMingShiSaveButton.transform.localScale = Vector3.one;
			}
		}

		// 重新绑定点击事件
		_xuanJianDengMingShiBoundWindow = unitWindow;
		_xuanJianDengMingShiSaveButton.onClick.RemoveAllListeners();
		_xuanJianDengMingShiSaveButton.onClick.AddListener(OnXuanJianDengMingShiButtonClick);

		// 更新按钮状态指示
		RefreshDengMingShiButtonState(unitWindow.actor);
	}

	private static void OnXuanJianDengMingShiButtonClick()
	{
		Actor actor = _xuanJianDengMingShiBoundWindow?.actor;
		if (actor?.data == null) return;

		if (XjDengMingShiCommands.IsActorSaved(actor))
		{
			XjDengMingShiCommands.RemoveSavedActor(actor);
		}
		else
		{
			XjDengMingShiCommands.SaveSelectedActor(actor);
		}

		RefreshDengMingShiButtonState(actor);
	}

	private static void RefreshDengMingShiButtonState(Actor actor)
	{
		if (_xuanJianDengMingShiButtonIcon != null)
		{
			bool isSaved = actor?.data != null && XjDengMingShiCommands.IsActorSaved(actor);
			_xuanJianDengMingShiButtonIcon.color = isSaved
				? Color.white
				: new Color(0.5f, 0.5f, 0.5f, 0.5f);
		}
	}
	[HarmonyPostfix]
	[HarmonyPatch(typeof(UnitWindow), "showInfo")]
	public static void XuanJianVNext_UnitWindow_ShowInfo_FamilyInheritanceTab_Postfix(UnitWindow __instance)
	{
		try
		{
			XjActorOverviewStatsFormatter.Refresh(__instance, forceLayout: true);
			XjActorOverviewStatsFormatter.RequestDeferredRefresh(__instance);
			XjActorInfoPanelRenderer.Refresh(__instance);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴] UnitWindow UI注入异常(showInfo): " + ex.Message);
		}
		XuanJianVNext_RemoveFamilyInheritanceTab(__instance);
		XjQianKunDaiTabPatches.TryAddOrUpdate(__instance);
		XjMentorshipTabPatches.TryAddOrUpdate(__instance);
		XuanJianVNext_TryAddDengMingShiSaveButton(__instance);
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(UnitWindow), "showStatsRows")]
	public static void XuanJianVNext_UnitWindow_ShowStatsRows_FamilyCaiQi_ReadOnly_Postfix(UnitWindow __instance)
	{
		try
		{
			// showStatsRows occurs after native rows are ready: bypass same-frame throttle and rebuild layout.
			XjActorOverviewStatsFormatter.Refresh(__instance, forceLayout: true);
			XjActorOverviewStatsFormatter.RequestDeferredRefresh(__instance);
			XuanJianVNext_TryShowActorZongMenRow(__instance);
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[玄鉴] UnitWindow UI注入异常(showStatsRows): " + ex.Message);
		}
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(UnitStatsElement), "showContent")]
	public static Exception XuanJianVNext_UnitStatsElement_ShowContent_SafeCore_Finalizer(Exception __exception)
	{
		if (__exception is NullReferenceException)
		{
			return null;
		}

		return __exception;
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(CityWindow), "showStatsRows")]
	public static void XuanJianVNext_CityWindow_ShowStatsRows_CaiQiSites_ReadOnly_Postfix(CityWindow __instance)
	{
		if ((UnityEngine.Object)(object)__instance == null)
		{
			return;
		}
		City selected_city = SelectedMetas.selected_city;
		if (((CoreSystemObject<CityData>)(object)selected_city)?.data != null)
		{
			XjCultivatorPopulationSummary cultivators = XjCultivatorPopulationOverview.BuildForCity(selected_city);
			KeyValueField cultivatorRow = XuanJian_ShowCityWindowStatRow054(__instance, "修炼者", cultivators.DisplayValue);
			XuanJian_AttachCultivatorPopulationTooltip(
				cultivatorRow,
				"城中修炼者",
				cultivators.BuildTooltip(XuanJian_GetCityDisplayName(selected_city)));

			string text = XjCaiQiCityDisplayFormatter.FormatCitySites(((BaseSystemData)((CoreSystemObject<CityData>)(object)selected_city).data).id);
			if (!string.IsNullOrWhiteSpace(text))
			{
				KeyValueField row = XuanJian_ShowCityWindowStatRow054(__instance, "采气地", text);
				XuanJian_MoveCityRowAfterOriginSpecies(row);
			}

			if (XjZongMenCityData.HasZongMen(selected_city))
			{
				string zongMenName = XjZongMenCityData.GetZongMenName(selected_city);
				if (!string.IsNullOrWhiteSpace(zongMenName))
				{
					KeyValueField row = XuanJian_ShowCityWindowStatRow054(__instance, "宗门", zongMenName);
					XuanJian_AttachPlainTooltip(row, zongMenName, "该城镇所属宗门。");
				}
			}
			XuanJian_TryShowCitySectOverviewRows(__instance, selected_city);
		}
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(KingdomWindow), "showStatsRows")]
	public static void XuanJianVNext_KingdomWindow_ShowStatsRows_CultivatorPopulation_Postfix(KingdomWindow __instance)
	{
		if ((UnityEngine.Object)(object)__instance == null)
		{
			return;
		}

		Kingdom kingdom = SelectedMetas.selected_kingdom;
		if (kingdom?.data == null)
		{
			return;
		}

		XjCultivatorPopulationSummary cultivators = XjCultivatorPopulationOverview.BuildForKingdom(kingdom);
		KeyValueField row = XuanJian_ShowKingdomWindowStatRow054(__instance, "修炼者", cultivators.DisplayValue);
		XuanJian_AttachCultivatorPopulationTooltip(
			row,
			"国内修炼者",
			cultivators.BuildTooltip(XuanJian_GetKingdomDisplayName(kingdom)));
		XuanJian_TryShowKingdomSectOverviewRows(__instance, kingdom);
	}

	private static string XuanJian_GetCityDisplayName(City city)
	{
		string name = city?.data?.name;
		return string.IsNullOrWhiteSpace(name) ? "未知城镇" : name.Trim();
	}

	private static string XuanJian_GetKingdomDisplayName(Kingdom kingdom)
	{
		string name = kingdom?.data?.name;
		return string.IsNullOrWhiteSpace(name) ? "未知国家" : name.Trim();
	}

	[HarmonyPostfix]
	[HarmonyPatch(typeof(CityWindow), "startShowingWindow")]
	public static void XuanJianVNext_CityWindow_StartShowingWindow_ZongMenTab_ReadOnly_Postfix(CityWindow __instance)
	{
		if (__instance == null || __instance.scroll_window?.tabs?._tabs == null)
		{
			return;
		}

		ScrollWindow scrollWindow = __instance.scroll_window;
		Transform spacer = __instance.tabs?.transform.Find("space (1)");
		if (spacer != null)
		{
			UnityEngine.Object.Destroy(spacer.gameObject);
		}

		WindowMetaTab existing = XjUiSurfaceOwnership.FindSingleTab(scrollWindow, "shili_tab");
		if (existing != null)
		{
			scrollWindow.tabs._tabs.Remove(existing);
			UnityEngine.Object.DestroyImmediate(existing.gameObject);
		}
	}

	[HarmonyPrefix]
	[HarmonyPatch(typeof(IconOutline), "show", new Type[] { typeof(ContainerItemColor) })]
	private static bool XuanJianVNext_IconOutline_Show_NullContainerGuard_Prefix(ContainerItemColor pContainer)
	{
		return pContainer != null;
	}

	[HarmonyFinalizer]
	[HarmonyPatch(typeof(IconOutline), "show", new Type[] { typeof(ContainerItemColor) })]
	private static Exception XuanJianVNext_IconOutline_Show_UiFailureGuard_Finalizer(Exception __exception)
	{
		return __exception is NullReferenceException ? null : __exception;
	}



	private static void XuanJianVNext_TryShowActorZongMenRow(UnitWindow window)
	{
		Actor actor = window?.actor;
		if (actor?.data == null)
		{
			return;
		}

		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		if (!identity.Found || identity.ZongMenId <= 0L || string.IsNullOrWhiteSpace(identity.ZongMenName))
		{
			return;
		}


		KeyValueField row = window.showStatRow(
			"宗门",
			identity.ZongMenName.Trim(),
			null,
			MetaType.None,
			-1L,
			false,
			null,
			null,
			null,
			false);
		if (row == null)
		{
			return;
		}

		TipButton tip = row.gameObject.GetComponent<TipButton>() ?? row.gameObject.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, identity.ZongMenName.Trim(), "角色所属宗门。", string.Empty);
	}


	private static KeyValueField XuanJian_ShowCityWindowStatRow054(CityWindow window, string leftLabel, string rightValue)
	{
		if (_xuanJianCityShowStatRowMethod == null)
		{
			_xuanJianCityShowStatRowMethod = window.GetType()
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.FirstOrDefault(method =>
				{
					ParameterInfo[] parameters = method.GetParameters();
					return method.Name == "showStatRow"
						&& parameters.Length == 10
						&& parameters[0].ParameterType == typeof(string)
						&& parameters[1].ParameterType == typeof(object);
				});
		}

		if (_xuanJianCityShowStatRowMethod == null)
		{
			return null;
		}

		KeyValueField row = _xuanJianCityShowStatRowMethod.Invoke(window, new object[10]
		{
			leftLabel,
			rightValue,
			null,
			MetaType.None,
			-1L,
			false,
			null,
			null,
			null,
			false
		}) as KeyValueField;
		XuanJian_ApplyInjectedCityRowTint(row);
		return row;
	}

	private static void XuanJian_ApplyInjectedCityRowTint(KeyValueField row)
	{
		if (row?.gameObject == null)
		{
			return;
		}

		Image image = row.gameObject.GetComponent<Image>() ?? row.gameObject.AddComponent<Image>();
		int index = row.transform?.GetSiblingIndex() ?? 0;
		image.color = (index & 1) == 0
			? new Color(0.18f, 0.21f, 0.17f, 0.36f)
			: new Color(0.10f, 0.12f, 0.10f, 0.24f);
		image.raycastTarget = true;
	}

	private static KeyValueField XuanJian_ShowKingdomWindowStatRow054(KingdomWindow window, string leftLabel, string rightValue)
	{
		if (_xuanJianKingdomShowStatRowMethod == null)
		{
			_xuanJianKingdomShowStatRowMethod = window.GetType()
				.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
				.FirstOrDefault(method =>
				{
					ParameterInfo[] parameters = method.GetParameters();
					return method.Name == "showStatRow"
						&& parameters.Length == 10
						&& parameters[0].ParameterType == typeof(string)
						&& parameters[1].ParameterType == typeof(object);
				});
		}

		if (_xuanJianKingdomShowStatRowMethod == null)
		{
			return null;
		}

		return _xuanJianKingdomShowStatRowMethod.Invoke(window, new object[10]
		{
			leftLabel,
			rightValue,
			null,
			MetaType.None,
			-1L,
			false,
			null,
			null,
			null,
			false
		}) as KeyValueField;
	}

	private static void XuanJian_AttachCultivatorPopulationTooltip(KeyValueField row, string title, string description)
	{
		if (row == null)
		{
			return;
		}

		TipButton tip = row.gameObject.GetComponent<TipButton>() ?? row.gameObject.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, title, description, string.Empty);
	}

	private static void XuanJian_MoveCityRowAfterOriginSpecies(KeyValueField row)
	{
		Transform rowTransform = row?.transform;
		Transform parent = rowTransform?.parent;
		if (parent == null)
		{
			return;
		}

		for (int i = 0; i < parent.childCount; i++)
		{
			Transform child = parent.GetChild(i);
			if (child == null || child == rowTransform)
			{
				continue;
			}

			string label = child.GetComponent<KeyValueField>()?.name_text?.text?.Trim();
			if (label == "始祖物种" || label == "creature_statistics_species")
			{
				rowTransform.SetSiblingIndex(Math.Min(i + 1, parent.childCount - 1));
				return;
			}
		}
	}

	private static bool XuanJian_TryShowCityWindowStatRow(CityWindow window, string leftLabel, string rightValue)
	{
		return XuanJian_ShowCityWindowStatRow054(window, leftLabel, rightValue) != null;
	}

	private static void XuanJian_TryShowCitySectOverviewRows(CityWindow window, City city)
	{
		if (window == null || city?.data == null || city.kingdom?.data == null)
		{
			return;
		}

		long cityId = ((BaseSystemData)((CoreSystemObject<CityData>)(object)city).data).id;
		XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord sect);
		if (sect != null)
		{
			KeyValueField sectRow = XuanJian_ShowCityWindowStatRow054(window, "宗门政体", BuildSectCompactText(sect));
			XuanJian_AttachSectOverviewTooltip(sectRow, sect, city);
		}

		if (XjSectRepository.TryGetGovernance(cityId, out XjCityFamilyGovernanceArchiveRecord governance) && governance != null)
		{
			bool sectBound = sect != null && governance.SectId == sect.SectId;
			KeyValueField governanceRow = XuanJian_ShowCityWindowStatRow054(window, sectBound ? "治理家族" : "城镇望族", BuildGovernanceCompactText(governance));
			XuanJian_AttachPlainTooltip(governanceRow, sectBound ? "城镇治理" : "城镇望族", BuildGovernanceTooltip(governance));
		}

		if (sect != null)
		{
			KeyValueField formationRow = XuanJian_ShowCityWindowStatRow054(window, "宗门大阵", BuildFormationCompactText(sect.SectId));
			XuanJian_AttachPlainTooltip(formationRow, "宗门大阵", BuildFormationTooltip(sect.SectId));
		}
	}

	private static void XuanJian_TryShowKingdomSectOverviewRows(KingdomWindow window, Kingdom kingdom)
	{
		if (window == null || kingdom?.data == null)
		{
			return;
		}

		KingdomSectSummary summary = BuildKingdomSectSummary(kingdom);
		if (summary == null || summary.SectCount <= 0)
		{
			return;
		}

		KeyValueField row = XuanJian_ShowKingdomWindowStatRow054(window, "辖境宗门", summary.SectCount + "宗-" + summary.CityCount + "城");
		XuanJian_AttachPlainTooltip(row, "辖境宗门", summary.Tooltip);
	}

	private sealed class KingdomSectSummary
	{
		internal int SectCount;
		internal int CityCount;
		internal string Tooltip;
	}

	private static KingdomSectSummary BuildKingdomSectSummary(Kingdom kingdom)
	{
		if (kingdom?.data == null) return null;
		long kingdomId = kingdom.data.id;
		Dictionary<long, int> cityCountBySect = new Dictionary<long, int>();
		IReadOnlyList<City> cities = XuanJianVNext.Core.XjWorldLookupIndex.GetCitySnapshot();
		for (int i = 0; i < cities.Count; i++)
		{
			City city = cities[i];
			if (city?.data == null || city.kingdom?.data?.id != kingdomId) continue;
			if (!XjSectRepository.TryGetByCity(city, out XjSectArchiveRecord citySect) || citySect?.SectId <= 0L) continue;
			cityCountBySect.TryGetValue(citySect.SectId, out int count);
			cityCountBySect[citySect.SectId] = count + 1;
		}

		if (cityCountBySect.Count <= 0) return null;
		List<string> lines = new List<string>();
		int cityCount = 0;
		foreach (KeyValuePair<long, int> pair in cityCountBySect)
		{
			cityCount += pair.Value;
			string sectName = "未知宗门";
			if (XjSectRepository.TryGetBySectId(pair.Key, out XjSectArchiveRecord sect) && sect != null && !string.IsNullOrWhiteSpace(sect.Name))
			{
				sectName = sect.Name.Trim();
			}
			lines.Add(sectName + "：" + pair.Value + "城");
		}

		lines.Sort(StringComparer.Ordinal);
		return new KingdomSectSummary
		{
			SectCount = cityCountBySect.Count,
			CityCount = cityCount,
			Tooltip = "该国家辖境内存在的宗门城镇。国家与宗门已经分离，这里只做城市归属概览。\n" + string.Join("\n", lines)
		};
	}

	private static string BuildSectCompactText(XjSectArchiveRecord sect)
	{
		if (sect == null)
		{
			return "暂无宗门";
		}

		string name = string.IsNullOrWhiteSpace(sect.Name) ? "未命名宗门" : sect.Name.Trim();
		return name + "-" + TranslateSectStatus(sect);
	}

	private static string BuildGovernanceCompactText(XjCityFamilyGovernanceArchiveRecord governance)
	{
		if (governance == null || governance.GoverningFamilyId <= 0L)
		{
			return "尚未确认";
		}

		string text = ResolveFamilyDisplayName(governance.GoverningFamilyId) + "-" + TranslateGovernanceState(governance.State);
		if (governance.ChallengerFamilyId > 0L)
		{
			text += "-受" + ResolveFamilyDisplayName(governance.ChallengerFamilyId) + "挑战"
				+ "(" + Math.Max(0, governance.ChallengeConsecutiveYears) + "/10年)";
		}
		return text;
	}

	private static string BuildGovernanceTooltip(XjCityFamilyGovernanceArchiveRecord governance)
	{
		if (governance == null)
		{
			return "该城镇尚未确认本地望族。";
		}

		string title = governance.SectId > 0L ? "治理家族" : "城镇望族";
		string text = title + "：" + ResolveFamilyDisplayName(governance.GoverningFamilyId)
			+ "\n状态：" + TranslateGovernanceState(governance.State)
			+ "\n确认年份：" + Math.Max(0, governance.ConfirmedYear);
		if (governance.ChallengerFamilyId > 0L)
		{
			string governingName = ResolveFamilyDisplayName(governance.GoverningFamilyId);
			string challengerName = ResolveFamilyDisplayName(governance.ChallengerFamilyId);
			text += "\n挑战家族：" + ResolveFamilyDisplayName(governance.ChallengerFamilyId)
				+ "\n连续挑战：" + Math.Max(0, governance.ChallengeConsecutiveYears) + "/10年"
				+ "\n现任控制分：" + governingName + " " + governance.IncumbentControlScore.ToString("0")
				+ "\n挑战控制分：" + challengerName + " " + governance.ChallengerControlScore.ToString("0");
		}
		return text;
	}

	private static string ResolveFamilyDisplayName(long familyStableId)
	{
		string displayName = XjFamilyDisplayNameResolver.Resolve(familyStableId);
		return string.IsNullOrWhiteSpace(displayName) ? "未名氏" : displayName;
	}

	private static string BuildFormationCompactText(long sectId)
	{
		if (!XjSectFormationRegistry.TryGet(sectId, out XjSectFormationArchiveRecord formation) || formation == null || formation.Grade <= 0)
		{
			return "未建";
		}

		return XjFormationEngineeringSystem.FormatFormationName(formation.Grade)
			+ "-耐久" + Math.Max(0, formation.CurrentDurability) + "-" + Math.Max(0, formation.MaxDurability);
	}

	private static string BuildFormationTooltip(long sectId)
	{
		if (!XjSectFormationRegistry.TryGet(sectId, out XjSectFormationArchiveRecord formation) || formation == null || formation.Grade <= 0)
		{
			return "该宗门尚未建成护宗大阵。";
		}

		return "阵法：" + XjFormationEngineeringSystem.FormatFormationName(formation.Grade)
			+ "\n品阶：第" + formation.Grade + "阶"
			+ "\n状态：" + TranslateFormationState(formation.BuildState)
			+ "\n耐久：" + Math.Max(0, formation.CurrentDurability) + "-" + Math.Max(0, formation.MaxDurability)
			+ "\n构造者：" + ResolveFormationLeadName(formation.LeadFormationMasterId)
			+ "\n占领速度：" + Math.Round(Math.Max(0f, formation.OccupationSpeedMultiplier) * 100f) + "%"
			+ "\n完成门禁：" + Math.Round(Math.Max(0f, formation.CompletionGate) * 100f) + "%";
	}

	private static string ResolveFormationLeadName(long actorId)
	{
		if (actorId <= 0L)
		{
			return "未任命";
		}
		if (XuanJianVNext.Core.XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) && actor?.data != null)
		{
			return SafeActorName(actor);
		}
		return "未任命";
	}

	private static void XuanJian_AttachSectOverviewTooltip(KeyValueField row, XjSectArchiveRecord sect, City focusCity)
	{
		if (row == null || sect == null)
		{
			return;
		}

		string capitalName = ResolveSectCapitalName(sect);
		if (focusCity != null && XjZongMenCityData.HasZongMen(focusCity))
		{
			XuanJian_AttachPlainTooltip(
				row,
				string.IsNullOrWhiteSpace(sect.Name) ? "宗门政体" : sect.Name.Trim(),
				"状态：" + TranslateSectStatus(sect)

				+ "\n创宗：" + Math.Max(0, sect.FoundingYear) + "年"
				+ "\n山门：" + capitalName);
			return;
		}

		XuanJian_AttachPlainTooltip(
			row,
			string.IsNullOrWhiteSpace(sect.Name) ? "宗门政体" : sect.Name.Trim(),
			"状态：" + TranslateSectStatus(sect)
			+ "\n创宗：" + Math.Max(0, sect.FoundingYear) + "年"
			+ "\n山门：" + capitalName);
	}

	private static string ResolveSectCapitalName(XjSectArchiveRecord sect)
	{
		if (sect == null || sect.CapitalCityId <= 0L)
		{
			return "未定";
		}
		City city = FindCityById(sect.CapitalCityId);
		if (city?.data == null)
		{
			return "未定";
		}
		try
		{
			string name = city.data.name;
			return string.IsNullOrWhiteSpace(name) ? "未名城镇" : name.Trim();
		}
		catch
		{
			return "未名城镇";
		}
	}

	private static void XuanJian_AttachPlainTooltip(KeyValueField row, string title, string description)
	{
		if (row == null)
		{
			return;
		}

		TipButton tip = row.gameObject.GetComponent<TipButton>() ?? row.gameObject.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, title, description, string.Empty);
	}

	private static City FindCityById(long cityId)
	{
		return XuanJianVNext.Core.XjWorldLookupIndex.TryResolveCity(cityId, out City city) ? city : null;
	}

	private static string SafeActorName(Actor actor)
	{
		try
		{
			string name = actor?.getName();
			return string.IsNullOrWhiteSpace(name) ? "未名修士" : name.Trim();
		}
		catch
		{
			return "未名修士";
		}
	}

	private static string TranslateSectStatus(string state)
	{
		if (state == XjSectStatus.SectRegime) return "紫府宗门";
		if (state == XjSectStatus.FudiSect) return "福地宗门";
		if (state == XjSectStatus.DongtianSect) return "洞天宗门";
		if (state == XjSectStatus.LandlessSect) return "失地宗门";
		if (state == XjSectStatus.Extinct) return "断绝";
		return "宗门政体";
	}

	private static string TranslateSectStatus(XjSectArchiveRecord sect)
	{
		if (sect != null
			&& HasLivingJinDanInSect(sect)
			&& sect.Status != XjSectStatus.FudiSect
			&& sect.Status != XjSectStatus.DongtianSect
			&& sect.Status != XjSectStatus.LandlessSect
			&& sect.Status != XjSectStatus.Extinct)
		{
			return "金丹宗门";
		}
		return TranslateSectStatus(sect?.Status);
	}

	private static bool HasLivingJinDanInSect(XjSectArchiveRecord sect)
	{
		if (sect?.FamilyIds == null || sect.FamilyIds.Count == 0)
		{
			return false;
		}
		for (int i = 0; i < sect.FamilyIds.Count; i++)
		{
			foreach (XjFamilyMemberLedgerEntry entry in XjFamilyMemberLedger.ReadFamilyAlive(sect.FamilyIds[i]))
			{
				if (XjRealmHelper.GetOrder(entry.RealmId) >= XjRealmHelper.GetOrder(XjRealmIds.JinDan)
					&& XjActorRegistry.ResolveKnownOrWorld(entry.ActorId, out Actor actor)
					&& actor?.data != null
					&& actor.isAlive())
				{
					return true;
				}
			}
		}
		return false;
	}

	private static string TranslateGovernanceState(string state)
	{
		if (state == XjCityFamilyGovernanceState.Prominent) return "望族";
		if (state == XjCityFamilyGovernanceState.Stable) return "稳定";
		if (state == XjCityFamilyGovernanceState.Challenged) return "挑战中";
		if (state == XjCityFamilyGovernanceState.Reassigned) return "改封";
		if (state == "Contested") return "争议";
		if (state == "Integration") return "整合期";
		return "未确认";
	}

	private static string TranslateFormationState(string state)
	{
		if (state == XjSectFormationBuildState.Operational) return "运转";
		if (state == XjSectFormationBuildState.Damaged) return "受损";
		if (state == XjSectFormationBuildState.Repairing) return "维修中";
		if (state == XjSectFormationBuildState.Broken) return "已破";
		if (state == XjSectFormationBuildState.Deploying) return "部署中";
		if (state == XjSectFormationBuildState.MaterialCommitted) return "材料已定";
		return "未建";
	}
}

