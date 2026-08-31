using System;
using System.Collections.Generic;
using NeoModLoader.General;
using NeoModLoader.General.UI.Window;
using NeoModLoader.General.UI.Window.Layout;
using NeoModLoader.General.UI.Window.Utils.Extensions;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Family;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.ZongMen;

// 玄门/0.5.4 style ShiLiXiangQing window. UI is read-only; data still comes from VNext zongmen models.
internal class XjZongMenDetailWindow : AutoLayoutWindow<XjZongMenDetailWindow>
{
	private const string WindowId = "XuanJianShiLiXiangQing";
	private const float ContentWidth = 220f;
	private const float OverviewWidth = 200f;
	private const float PeakCardWidth = 70f;
	private const float PeakCardHeight = 90f;
	private const float PeakSpacing = 5f;
	private const int PeaksPerRow = 3;
	private const int IconSize = 36;
	private const int IconsPerRow = 4;
	private const float ZongZhuCardWidth = 200f;
	private const float ZongZhuCardHeight = 75f;
	private const float FengZhuCardWidth = 70f;
	private const float FengZhuCardHeight = 95f;
	private const float LaoZuCardWidth = 35f;
	private const float LaoZuCardHeight = 65f;
	private const float MenRenCardWidth = 30f;
	private const float MenRenCardHeight = 55f;
	private static long currentCityId;
	private static City currentCity => currentCityId > 0L
		&& XjWorldLookupIndex.TryResolveCity(currentCityId, out City city)
		? city
		: null;
	private static bool created;
	private static XjZongMenDetailWindow instance;
	private int activeTab;
	private readonly bool[] pavilionGradeCollapsed = new bool[7];
	private GameObject tabBarPanel;
	private Image tabDetail;
	private Image tabRoles;
	private Image tabPavilion;

	protected override void Init()
	{
		instance = this;
		layout.spacing = 5;
		layout.padding = new RectOffset(5, 5, 5, 5);
		CreateTabBar();
		if (ScrollWindowComponent?.titleText != null)
		{
			ScrollWindowComponent.titleText.gameObject.SetActive(false);
		}
	}

	public override void OnNormalEnable()
	{
		base.OnNormalEnable();
		RefreshContent();
	}

	internal static void Show(City city)
	{
		if (city?.data == null || !XjSectCityData.HasZongMen(city))
		{
			return;
		}
		currentCityId = city.data.id;
		EnsureCreated();
		ScrollWindow.showWindow(WindowId);
	}

	private static void EnsureCreated()
	{
		if (created)
		{
			return;
		}
		instance = CreateWindow(WindowId, "window_shilixiangqing_title");
		created = true;
	}

	private void RefreshContent()
	{
		ClearContent();
		if (currentCity?.data == null)
		{
			return;
		}
		UpdateTabButtonStates();
		XjZongMenUiReadModel model = XjZongMenUiReadModel.BuildForCity(currentCity);
		if (activeTab == 0)
		{
			CreateOverviewSection(model);
			CreatePeakCardsSection();
		}
		else if (activeTab == 1)
		{
			CreateRolesSection();
		}
		else
		{
			CreatePavilionSection(model);
		}
	}

	private void ClearContent()
	{
		for (int i = transform.childCount - 1; i >= 0; i--)
		{
			UnityEngine.Object.Destroy(transform.GetChild(i).gameObject);
		}
	}

	private void CreateTabBar()
	{
		Transform parent = transform.parent?.transform?.parent;
		if (parent == null)
		{
			return;
		}
		tabBarPanel = new GameObject("TabBarPanel");
		tabBarPanel.transform.SetParent(parent, false);
		RectTransform rect = tabBarPanel.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0f, 0.5f);
		rect.anchorMax = new Vector2(0f, 0.5f);
		rect.pivot = new Vector2(1f, 0.5f);
		rect.anchoredPosition = new Vector2(-2f, 0f);
		rect.sizeDelta = new Vector2(30f, 102f);
		VerticalLayoutGroup group = tabBarPanel.AddComponent<VerticalLayoutGroup>();
		group.childAlignment = TextAnchor.MiddleCenter;
		group.spacing = 4f;
		group.padding = new RectOffset(2, 2, 4, 4);
		group.childForceExpandWidth = false;
		group.childForceExpandHeight = false;
		tabDetail = CreateTabButton(tabBarPanel.transform, "ui/icons/iconCulture", 0, "详情", "查看宗门概况与山峰。");
		tabRoles = CreateTabButton(tabBarPanel.transform, "ui/icons/iconOptions", 1, "职介", "查看宗主、峰主、洞天驻修与门人。");
		tabPavilion = CreateTabButton(tabBarPanel.transform, "ui/GongFaGe", 2, "功法阁", "查看宗门四至六品功法、求金法、采气法与纳气。");
	}

	private Image CreateTabButton(Transform parent, string iconPath, int tabIndex, string title, string description)
	{
		GameObject buttonObject = new GameObject("TabBtn_" + tabIndex);
		buttonObject.transform.SetParent(parent, false);
		RectTransform rect = buttonObject.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(28f, 28f);
		LayoutElement layoutElement = buttonObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = 28f;
		layoutElement.preferredHeight = 28f;
		Image background = buttonObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/special/button");
		background.type = Image.Type.Sliced;
		Button button = buttonObject.AddComponent<Button>();
		button.targetGraphic = background;
		button.onClick.AddListener(delegate
		{
			if (activeTab == tabIndex)
			{
				return;
			}
			activeTab = tabIndex;
			RefreshContent();
		});
		GameObject iconObject = new GameObject("Icon");
		iconObject.transform.SetParent(buttonObject.transform, false);
		RectTransform iconRect = iconObject.AddComponent<RectTransform>();
		iconRect.anchorMin = Vector2.zero;
		iconRect.anchorMax = Vector2.one;
		iconRect.offsetMin = new Vector2(3f, 3f);
		iconRect.offsetMax = new Vector2(-3f, -3f);
		Image icon = iconObject.AddComponent<Image>();
		icon.sprite = SpriteTextureLoader.getSprite(iconPath) ?? SpriteTextureLoader.getSprite("ui/icons/iconBook");
		icon.preserveAspect = true;
		TipButton tip = buttonObject.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, title, description, string.Empty);
		return background;
	}

	private void UpdateTabButtonStates()
	{
		SetTabColor(tabDetail, activeTab == 0);
		SetTabColor(tabRoles, activeTab == 1);
		SetTabColor(tabPavilion, activeTab == 2);
	}

	private static void SetTabColor(Image image, bool active)
	{
		if (image != null)
		{
			image.color = active ? new Color(0.4f, 0.6f, 0.8f) : new Color(0.25f, 0.25f, 0.25f);
		}
	}

	private void CreateOverviewSection(in XjZongMenUiReadModel model)
	{
		AutoVertLayoutGroup container = this.BeginVertGroup(new Vector2(ContentWidth, 122f), TextAnchor.UpperCenter, 0f, new RectOffset(10, 10, 5, 5));
		Image background = container.gameObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/ShiLiXQxx") ?? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		background.type = Image.Type.Simple;
		CreateTextElement(container.transform, "综合情况", 10, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black, new Vector2(OverviewWidth, 18f));
		CreateInfoRow(container, "创立于:", FormatYear(model.Identity.JoinYear));
		CreateInfoRow(container, "宗门年龄:", FormatAge(model.Identity.JoinYear));
		CreateInfoRow(container, "创建者:", string.IsNullOrWhiteSpace(XjSectCityData.GetFounderName(currentCity)) ? "-" : XjSectCityData.GetFounderName(currentCity));
		CreateInfoRow(container, "当代宗主:", XjSectCityData.GetZongZhu(currentCity)?.getName() ?? "-");
		CreateInfoRow(container, "宗门规模:", GetScaleName());
		CreateInfoRow(container, "修士数量:", XjSectCityData.GetStoredMemberCount(currentCity).ToString());
		CreateInfoRow(container, "山峰数量:", XjSectCityData.GetPeaks(currentCity).Count.ToString());
	}

	private void CreateInfoRow(AutoVertLayoutGroup parent, string label, string value)
	{
		AutoHoriLayoutGroup row = parent.BeginHoriGroup(new Vector2(OverviewWidth, 12f), TextAnchor.MiddleLeft, 2f, new RectOffset(25, 10, 0, 0));
		CreateTextElement(row.transform, label, 6, FontStyle.Normal, TextAnchor.MiddleLeft, Color.black, new Vector2(55f, 12f));
		CreateTextElement(row.transform, value, 6, FontStyle.Normal, TextAnchor.MiddleLeft, Color.black, new Vector2(120f, 12f));
	}

	private void CreatePeakCardsSection()
	{
		List<(int id, string name)> peaks = XjSectCityData.GetPeaks(currentCity);
		if (peaks.Count == 0)
		{
			CreateCenteredText("暂无山峰", new Color(0.5f, 0.5f, 0.5f), ContentWidth, 22f);
			return;
		}
		for (int start = 0; start < peaks.Count; start += PeaksPerRow)
		{
			AutoHoriLayoutGroup row = this.BeginHoriGroup(new Vector2(ContentWidth, PeakCardHeight), TextAnchor.MiddleCenter, PeakSpacing, new RectOffset(0, 0, 0, 0));
			for (int i = start; i < peaks.Count && i < start + PeaksPerRow; i++)
			{
				CreatePeakCard(row, peaks[i].id, peaks[i].name);
			}
		}
	}

	private void CreatePeakCard(AutoHoriLayoutGroup parent, int peakId, string title)
	{
		AutoVertLayoutGroup card = parent.BeginVertGroup(new Vector2(PeakCardWidth, PeakCardHeight), TextAnchor.UpperCenter, 3f, new RectOffset(5, 5, 5, 5));
		Actor representative;
		string vacantText;
		string populationText;
		if (peakId == XjSectCityData.SupremePeakId)
		{
			List<Actor> elders = XjSectCityData.GetSupremeElders(currentCity);
			representative = elders.Count > 0 ? elders[0] : null;
			vacantText = "暂无驻修";
			populationText = "驻修:" + elders.Count;
		}
		else if (peakId == XjSectCityData.MainPeakId)
		{
			representative = XjSectCityData.GetZongZhu(currentCity);
			vacantText = "暂无宗主";
			populationText = "门人:" + XjSectCityData.GetPeakDiscipleCount(currentCity, peakId);
		}
		else
		{
			representative = XjSectCityData.GetPeakFengZhu(currentCity, peakId);
			vacantText = "暂无峰主";
			populationText = "门人:" + XjSectCityData.GetPeakDiscipleCount(currentCity, peakId);
		}

		GameObject backgroundObject = new GameObject("PeakBg");
		backgroundObject.transform.SetParent(card.transform, false);
		RectTransform bgRect = backgroundObject.AddComponent<RectTransform>();
		bgRect.anchorMin = Vector2.zero;
		bgRect.anchorMax = Vector2.one;
		bgRect.offsetMin = new Vector2(0f, -8f);
		bgRect.offsetMax = new Vector2(0f, 28f);
		LayoutElement bgLayout = backgroundObject.AddComponent<LayoutElement>();
		bgLayout.ignoreLayout = true;
		Image background = backgroundObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/ShiLifeng") ?? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		background.type = Image.Type.Simple;
		backgroundObject.transform.SetAsFirstSibling();
		CreateTextElement(card.transform, title, 8, FontStyle.Bold, TextAnchor.MiddleCenter, Color.black, new Vector2(60f, 14f));
		CreateAvatar(card.transform, representative, 28f, 0.55f);
		CreateTextElement(card.transform, representative?.getName() ?? vacantText, 6, FontStyle.Normal, TextAnchor.MiddleCenter, Color.black, new Vector2(60f, 13f));
		CreateTextElement(card.transform, populationText, 6, FontStyle.Normal, TextAnchor.MiddleCenter, Color.black, new Vector2(60f, 13f));
		Button button = card.gameObject.AddComponent<Button>();
		button.targetGraphic = background;
		button.onClick.AddListener(delegate
		{
			if (representative != null && representative.isAlive()) ActionLibrary.openUnitWindow(representative);
		});
	}

	private void CreateRolesSection()
	{
		CreateZongZhuSection();
		CreateRoleGridSection("洞天", XjSectCityData.GetSupremeElders(currentCity), 6, "ui/SLbiaoqian04", LaoZuCardWidth, LaoZuCardHeight, "ui/ZhiJie05", 0.5f);
		List<Actor> masters = new List<Actor>();
		List<(int id, string name)> peaks = XjSectCityData.GetPeaks(currentCity);
		for (int i = 0; i < peaks.Count; i++)
		{
			Actor master = XjSectCityData.GetPeakFengZhu(currentCity, peaks[i].id);
			if (master != null && master != XjSectCityData.GetZongZhu(currentCity))
			{
				masters.Add(master);
			}
		}
		CreateRoleGridSection("峰主", masters, 3, "ui/SLbiaoqian03", FengZhuCardWidth, FengZhuCardHeight, "ui/ZhiJie04", 0.6f);
		CreateRoleGridSection("门人", XjSectCityData.CollectOrdinaryMembers(currentCity), 7, "ui/SLbiaoqian00", MenRenCardWidth, MenRenCardHeight, "ui/ZhiJie01", 0.52f);
	}

	private void CreateZongZhuSection()
	{
		Actor actor = XjSectCityData.GetZongZhu(currentCity);
		AutoVertLayoutGroup section = this.BeginVertGroup(new Vector2(ZongZhuCardWidth, ZongZhuCardHeight), TextAnchor.UpperCenter, 0f, new RectOffset(0, 0, 0, 0));
		Image background = section.gameObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite("ui/ZhiJie00");
		background.type = Image.Type.Sliced;
		if (actor == null)
		{
			CreateCenteredText(section.transform, "空缺", Color.gray, 170f, 40f);
			return;
		}
		CreateTitleBadge(section.transform, "宗主");
		AutoHoriLayoutGroup row = section.BeginHoriGroup(new Vector2(175f, 55f), TextAnchor.MiddleCenter, 0f, new RectOffset(5, 5, 0, 0));
		AutoVertLayoutGroup left = row.BeginVertGroup(new Vector2(55f, 50f), TextAnchor.MiddleLeft, 2f, new RectOffset(0, 0, 0, 0));
		CreateTextElement(left.transform, actor.getName(), 6, FontStyle.Bold, TextAnchor.MiddleLeft, new Color(0.6f, 0.7f, 0.95f), new Vector2(55f, 14f));
		CreateTextElement(left.transform, FormatActorRealm(actor), 6, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.7f, 0.6f, 0.9f), new Vector2(55f, 14f));
		CreateTextElement(left.transform, "门人:" + XjSectCityData.GetStoredMemberCount(currentCity), 6, FontStyle.Normal, TextAnchor.MiddleLeft, new Color(0.7f, 0.75f, 0.85f), new Vector2(55f, 14f));
		CreateAvatar(row.transform, actor, 30f, 0.6f);
		AutoVertLayoutGroup right = row.BeginVertGroup(new Vector2(55f, 50f), TextAnchor.MiddleRight, 2f, new RectOffset(0, 0, 0, 0));
		CreateTextElement(right.transform, "山峰:" + XjSectCityData.GetPeaks(currentCity).Count, 6, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.6f, 0.5f, 0.9f), new Vector2(55f, 14f));
		CreateTextElement(right.transform, "驻修:" + XjSectCityData.GetSupremeElderCount(currentCity), 6, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.7f, 0.75f, 0.85f), new Vector2(55f, 14f));
		string founder = XjSectCityData.GetFounderName(currentCity);
		CreateTextElement(right.transform, string.IsNullOrWhiteSpace(founder) ? "-" : founder, 6, FontStyle.Normal, TextAnchor.MiddleRight, new Color(0.55f, 0.65f, 0.9f), new Vector2(55f, 14f));
	}

	private void CreateRoleGridSection(string title, List<Actor> actors, int perRow, string titleSprite, float cardWidth, float cardHeight, string cardSprite, float avatarScale)
	{
		AutoVertLayoutGroup section = this.BeginVertGroup(default, TextAnchor.UpperCenter, 3f, new RectOffset(0, 0, 0, 0));
		CreateCollapsibleTitle(section, title, false, titleSprite);
		if (actors == null || actors.Count == 0)
		{
			CreateCenteredText(section.transform, "暂无", Color.gray, OverviewWidth, 18f);
			return;
		}
		for (int start = 0; start < actors.Count; start += perRow)
		{
			AutoHoriLayoutGroup row = section.BeginHoriGroup(default, TextAnchor.UpperCenter, 3f, new RectOffset(0, 0, 0, 0));
			for (int i = start; i < actors.Count && i < start + perRow; i++)
			{
				CreateRoleCard(row.transform, actors[i], cardWidth, cardHeight, cardSprite, avatarScale);
			}
		}
	}

	private void CreatePavilionSection(XjZongMenUiReadModel model)
	{
		for (int grade = 5; grade >= 4; grade--)
		{
			CreateGongFaGradeSection(grade, model);
		}
		CreateIconSection("求金法", model.QiuJinFaItems.Count, i => model.QiuJinFaItems[i].Name, i => FormatQiuJinFaTip(model.QiuJinFaItems[i]), _ => "item/gongfa/QiuJinFa", "暂无求金法");
		CreateIconSection("采气法", model.CaiQiFaItems.Count, i => model.CaiQiFaItems[i].Name, i => FormatCaiQiFaTip(model.CaiQiFaItems[i]), _ => "item/gongfa/CaiQiFa", "暂无采气法");
		CreateCaiQiIconSection(model);
	}

	private void CreateCaiQiIconSection(XjZongMenUiReadModel model)
	{
		AutoVertLayoutGroup section = this.BeginVertGroup(default, TextAnchor.UpperCenter, 3f, new RectOffset(0, 0, 0, 0));
		CreateCollapsibleTitle(section, "纳气", false, "ui/SLbiaoqian03");
		if (model.CaiQiItems.Count <= 0)
		{
			CreateCenteredText(section.transform, "暂无纳气", Color.gray, OverviewWidth, 18f);
			return;
		}

		for (int start = 0; start < model.CaiQiItems.Count; start += IconsPerRow)
		{
			int rowCount = Math.Min(IconsPerRow, model.CaiQiItems.Count - start);
			float spacing = rowCount > 1 ? Math.Min(4f, (210f - rowCount * IconSize) / (rowCount - 1)) : 0f;
			AutoHoriLayoutGroup row = section.BeginHoriGroup(new Vector2(ContentWidth, IconSize + 4f), TextAnchor.MiddleCenter, spacing, new RectOffset(5, 5, 2, 2));
			ConfigureIconRow(row);
			for (int i = start; i < start + rowCount; i++)
			{
				XjZongMenUiCaiQiItem item = model.CaiQiItems[i];
				CreateIconSlot(
					row.transform,
					XjFamilyInheritanceTabPatches.XuanJianVNext_TryLoadFamilyCaiQiResourceSprite(item.ResourceId),
					item.ResourceName,
					FormatCaiQiTip(item));
			}
		}
	}

	private void CreateGongFaGradeSection(int grade, XjZongMenUiReadModel model)
	{
		AutoVertLayoutGroup section = this.BeginVertGroup(default, TextAnchor.UpperCenter, 3f, new RectOffset(0, 0, 0, 0));
		int collapsedIndex = grade - 1;
		CreateCollapsibleTitle(section, FormatGongFaGrade(grade), pavilionGradeCollapsed[collapsedIndex], "ui/SLbiaoqian03", delegate
		{
			pavilionGradeCollapsed[collapsedIndex] = !pavilionGradeCollapsed[collapsedIndex];
			RefreshContent();
		});
		if (pavilionGradeCollapsed[collapsedIndex])
		{
			return;
		}

		List<XjZongMenUiGongFaItem> filtered = new List<XjZongMenUiGongFaItem>();
		for (int i = 0; i < model.GongFaItems.Count; i++)
		{
			if (model.GongFaItems[i].Grade == grade)
			{
				filtered.Add(model.GongFaItems[i]);
			}
		}
		if (filtered.Count == 0)
		{
			CreateCenteredText(section.transform, "暂无", Color.gray, OverviewWidth, 18f);
			return;
		}
		for (int start = 0; start < filtered.Count; start += IconsPerRow)
		{
			int count = Math.Min(IconsPerRow, filtered.Count - start);
			float spacing = count > 1 ? Math.Min(4f, (210f - count * IconSize) / (count - 1)) : 0f;
			AutoHoriLayoutGroup row = section.BeginHoriGroup(new Vector2(ContentWidth, IconSize + 4f), TextAnchor.MiddleCenter, spacing, new RectOffset(5, 5, 2, 2));
			ConfigureIconRow(row);
			for (int i = start; i < start + count; i++)
			{
				XjZongMenUiGongFaItem item = filtered[i];
				CreateIconSlot(row.transform, ResolveGongFaIconPath(item.Grade), item.Name, FormatGongFaTip(item));
			}
		}
	}

	private void CreateIconSection(string title, int count, Func<int, string> titleAt, Func<int, string> detailsAt, Func<int, string> iconPathAt, string emptyText)
	{
		AutoVertLayoutGroup section = this.BeginVertGroup(default, TextAnchor.UpperCenter, 3f, new RectOffset(0, 0, 0, 0));
		CreateCollapsibleTitle(section, title, false, "ui/SLbiaoqian03");
		if (count <= 0)
		{
			CreateCenteredText(section.transform, emptyText, Color.gray, OverviewWidth, 18f);
			return;
		}
		for (int start = 0; start < count; start += IconsPerRow)
		{
			int rowCount = Math.Min(IconsPerRow, count - start);
			float spacing = rowCount > 1 ? Math.Min(4f, (210f - rowCount * IconSize) / (rowCount - 1)) : 0f;
			AutoHoriLayoutGroup row = section.BeginHoriGroup(new Vector2(ContentWidth, IconSize + 4f), TextAnchor.MiddleCenter, spacing, new RectOffset(5, 5, 2, 2));
			ConfigureIconRow(row);
			for (int i = start; i < start + rowCount; i++)
			{
				CreateIconSlot(row.transform, iconPathAt == null ? string.Empty : iconPathAt(i), titleAt(i), detailsAt(i));
			}
		}
	}

	private static void ConfigureIconRow(AutoHoriLayoutGroup row)
	{
	}

	private void CreateIconSlot(Transform parent, string iconPath, string title, string details)
	{
		CreateIconSlot(parent, SpriteTextureLoader.getSprite(iconPath), title, details);
	}

	private void CreateIconSlot(Transform parent, Sprite sprite, string title, string details)
	{
		XjIconShelfRenderer.CreateIconSlot(
			parent,
			"ZongMenIconSlot",
			sprite ?? SpriteTextureLoader.getSprite("ui/icons/iconBook"),
			string.IsNullOrWhiteSpace(title) ? "藏" : title.Trim(),
			1,
			string.IsNullOrWhiteSpace(title) ? "宗门藏品" : title.Trim(),
			details ?? string.Empty,
			string.Empty,
			LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf"),
			IconSize,
			IconSize - 4f);
	}

	private void CreateCollapsibleTitle(AutoVertLayoutGroup parent, string title, bool isCollapsed, string spritePath, Action onToggle = null)
	{
		GameObject titleObject = new GameObject("CollapsibleTitle");
		titleObject.transform.SetParent(parent.transform, false);
		RectTransform titleRect = titleObject.AddComponent<RectTransform>();
		titleRect.sizeDelta = new Vector2(ContentWidth, 22f);
		LayoutElement layoutElement = titleObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = ContentWidth;
		layoutElement.preferredHeight = 22f;
		Image background = titleObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite(spritePath) ?? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		background.type = Image.Type.Sliced;
		if (onToggle != null)
		{
			Button button = titleObject.AddComponent<Button>();
			button.targetGraphic = background;
			button.onClick.AddListener(delegate
			{
				onToggle();
			});
		}
		string indicator = onToggle == null ? string.Empty : (isCollapsed ? "▶" : "▼");
		CreateFullSizeText(titleObject.transform, indicator + "【" + title + "】", 11, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.8f, 0.5f));
	}

	private void CreateRoleCard(Transform parent, Actor actor, float width, float height, string spritePath, float avatarScale)
	{
		GameObject cardObject = new GameObject("ZhiJieCard");
		cardObject.transform.SetParent(parent, false);
		RectTransform rect = cardObject.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(width, height);
		LayoutElement layoutElement = cardObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = width;
		layoutElement.preferredHeight = height;
		Image background = cardObject.AddComponent<Image>();
		background.sprite = SpriteTextureLoader.getSprite(spritePath) ?? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		background.type = Image.Type.Sliced;

		float avatarSize = Math.Max(24f, Math.Min(40f, width - 2f));
		GameObject avatarHolder = new GameObject("AvatarContainer");
		avatarHolder.transform.SetParent(cardObject.transform, false);
		RectTransform avatarRect = avatarHolder.AddComponent<RectTransform>();
		avatarRect.anchorMin = new Vector2(0.5f, 1f);
		avatarRect.anchorMax = new Vector2(0.5f, 1f);
		avatarRect.pivot = new Vector2(0.5f, 1f);
		avatarRect.anchoredPosition = new Vector2(0f, -4f);
		avatarRect.sizeDelta = new Vector2(avatarSize, avatarSize);
		CreateAvatar(avatarHolder.transform, actor, avatarSize, avatarScale);

		CreateAnchoredText(cardObject.transform, actor?.getName() ?? "-", 6, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f), new Vector2(0f, -avatarSize - 6f), new Vector2(width - 4f, 14f));
		CreateAnchoredText(cardObject.transform, FormatActorRealm(actor), 6, FontStyle.Normal, TextAnchor.MiddleCenter, new Color(0.7f, 0.9f, 0.7f), new Vector2(0f, -avatarSize - 18f), new Vector2(width - 4f, 12f));

		Button button = cardObject.AddComponent<Button>();
		button.targetGraphic = background;
		button.onClick.AddListener(delegate
		{
			if (actor != null && actor.isAlive())
			{
				ActionLibrary.openUnitWindow(actor);
			}
		});
		TipButton tip = cardObject.AddComponent<TipButton>();
		XjNativeHoverTooltip.Ensure(tip, actor?.getName() ?? "未知", FormatActorRealm(actor), string.Empty);
	}

	private void CreateFullSizeText(Transform parent, string text, int fontSize, FontStyle fontStyle, TextAnchor anchor, Color color)
	{
		GameObject textObject = new GameObject("Text");
		textObject.transform.SetParent(parent, false);
		RectTransform rect = textObject.AddComponent<RectTransform>();
		rect.anchorMin = Vector2.zero;
		rect.anchorMax = Vector2.one;
		rect.offsetMin = Vector2.zero;
		rect.offsetMax = Vector2.zero;
		Text label = textObject.AddComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = fontSize;
		label.fontStyle = fontStyle;
		label.alignment = anchor;
		label.color = color;
		label.text = text ?? string.Empty;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
	}

	private void CreateAnchoredText(Transform parent, string text, int fontSize, FontStyle fontStyle, TextAnchor anchor, Color color, Vector2 anchoredPosition, Vector2 size)
	{
		GameObject textObject = new GameObject("Text");
		textObject.transform.SetParent(parent, false);
		RectTransform rect = textObject.AddComponent<RectTransform>();
		rect.anchorMin = new Vector2(0.5f, 1f);
		rect.anchorMax = new Vector2(0.5f, 1f);
		rect.pivot = new Vector2(0.5f, 1f);
		rect.anchoredPosition = anchoredPosition;
		rect.sizeDelta = size;
		Text label = textObject.AddComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = fontSize;
		label.fontStyle = fontStyle;
		label.alignment = anchor;
		label.color = color;
		label.text = text ?? string.Empty;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
	}

	private AutoVertLayoutGroup CreateNativeFrame(string title, float height)
	{
		AutoVertLayoutGroup section = this.BeginVertGroup(new Vector2(ContentWidth, height), TextAnchor.UpperCenter, 3f, new RectOffset(12, 12, 7, 7));
		CreateTextElement(section.transform, title, 10, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.8f, 0.5f), new Vector2(OverviewWidth, 20f));
		return section;
	}

	private void CreateActorSmallSlot(Transform parent, Actor actor)
	{
		GameObject slot = new GameObject("ActorSlot");
		slot.transform.SetParent(parent, false);
		RectTransform rect = slot.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(28f, 32f);
		LayoutElement layoutElement = slot.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = 28f;
		layoutElement.preferredHeight = 32f;
		CreateAvatar(slot.transform, actor, 28f, 0.56f);
		TipButton tip = slot.AddComponent<TipButton>();
		string realm = FormatActorRealm(actor);
		XjNativeHoverTooltip.Ensure(
			tip,
			actor?.getName() ?? "未知",
			string.IsNullOrWhiteSpace(realm) ? "宗门成员" : realm,
			string.Empty);
	}

	private void CreateAvatar(Transform parent, Actor actor, float size, float scale)
	{
		GameObject holder = new GameObject("Avatar");
		holder.transform.SetParent(parent, false);
		RectTransform rect = holder.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(size, size);
		LayoutElement layoutElement = holder.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = size;
		layoutElement.preferredHeight = size;
		UiUnitAvatarElement prefab = Resources.Load<UiUnitAvatarElement>("ui/UnitAvatarElement");
		if (prefab == null || actor == null)
		{
			Image fallback = holder.AddComponent<Image>();
			fallback.sprite = SpriteTextureLoader.getSprite("ui/icons/iconCitizen");
			fallback.preserveAspect = true;
			return;
		}
		UiUnitAvatarElement avatar = UnityEngine.Object.Instantiate(prefab, holder.transform, false);
		RectTransform avatarRect = avatar.GetComponent<RectTransform>();
		avatarRect.anchorMin = new Vector2(0.5f, 0.5f);
		avatarRect.anchorMax = new Vector2(0.5f, 0.5f);
		avatarRect.pivot = new Vector2(0.5f, 0.5f);
		avatarRect.anchoredPosition = Vector2.zero;
		avatarRect.localScale = new Vector3(scale, scale, scale);
		avatar.show_banner_kingdom = false;
		avatar.show_banner_clan = false;
		avatar.show(actor);
		XjNativeAvatarInteractionSafety.DisableNativeInteraction(avatar.gameObject);
	}

	private void CreateTitleBadge(Transform parent, string title)
	{
		GameObject badge = new GameObject("TitleBadge");
		badge.transform.SetParent(parent, false);
		RectTransform rect = badge.AddComponent<RectTransform>();
		rect.sizeDelta = new Vector2(58f, 14f);
		LayoutElement layoutElement = badge.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = 58f;
		layoutElement.preferredHeight = 14f;
		Image image = badge.AddComponent<Image>();
		image.sprite = SpriteTextureLoader.getSprite("ui/ZongZhuxx") ?? SpriteTextureLoader.getSprite("ui/special/windowInnerSliced");
		image.type = Image.Type.Sliced;
		CreateTextElement(badge.transform, title, 9, FontStyle.Bold, TextAnchor.MiddleCenter, new Color(0.9f, 0.8f, 0.5f), new Vector2(58f, 14f));
	}

	private void CreateCenteredText(string text, Color color, float width, float height)
	{
		CreateCenteredText(transform, text, color, width, height);
	}

	private void CreateCenteredText(Transform parent, string text, Color color, float width, float height)
	{
		CreateTextElement(parent, text, 9, FontStyle.Normal, TextAnchor.MiddleCenter, color, new Vector2(width, height));
	}

	private void CreateTextElement(Transform parent, string text, int fontSize, FontStyle fontStyle, TextAnchor anchor, Color color, Vector2 size)
	{
		GameObject textObject = new GameObject("Text");
		textObject.transform.SetParent(parent, false);
		RectTransform rect = textObject.AddComponent<RectTransform>();
		rect.sizeDelta = size;
		LayoutElement layoutElement = textObject.AddComponent<LayoutElement>();
		layoutElement.preferredWidth = size.x;
		layoutElement.preferredHeight = size.y;
		Text label = textObject.AddComponent<Text>();
		label.font = LocalizedTextManager.current_font ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
		label.fontSize = fontSize;
		label.fontStyle = fontStyle;
		label.alignment = anchor;
		label.color = color;
		label.text = text ?? string.Empty;
		label.horizontalOverflow = HorizontalWrapMode.Wrap;
		label.verticalOverflow = VerticalWrapMode.Overflow;
	}

	private static string GetScaleName()
	{
		int count = XjSectCityData.GetStoredMemberCount(currentCity);
		if (count >= 100) return "巨宗";
		if (count >= 50) return "大宗";
		if (count >= 20) return "中宗";
		if (count > 0) return "小宗";
		return "-";
	}

	private static string FormatYear(int year)
	{
		return year > 0 ? XjChronology.FormatYear(year) : "-";
	}

	private static string FormatAge(int creationYear)
	{
		int year = World.world?.map_stats?.year ?? -1;
		if (year < 0 || creationYear <= 0)
		{
			return "-";
		}
		return Math.Max(0, year - creationYear) + "年";
	}

	private static string ResolveGongFaIconPath(int grade)
	{
		return grade switch
		{
			6 => "item/gongfa/LiuPinGongFa",
			4 => "item/gongfa/SiPinGongFa",
			5 => "item/gongfa/WuPinGongFa",
			_ => "item/gongfa/DiPinGongFa"
		};
	}

	private static string FormatGongFaGrade(int grade)
	{
		return XuanJianVNext.Data.GongFa.XjGongFaGradeText.Format(grade);
	}

	private static string FormatGongFaTip(in XjZongMenUiGongFaItem item)
	{
		string mapped = string.IsNullOrWhiteSpace(item.MappedXianJi)
			? string.Empty
			: "\n映射神通：" + item.MappedXianJi.Trim();
		return item.Grade + "品 · " + XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未载道途") + mapped;
	}

	private static string FormatQiuJinFaTip(in XjZongMenUiQiuJinFaItem item)
	{
		string source = string.IsNullOrWhiteSpace(item.SourceGongFaName) ? "来源功法未载" : item.SourceGongFaName;
		return source + " · " + XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未载道途");
	}

	private static string FormatCaiQiTip(in XjZongMenUiCaiQiItem item)
	{
		return XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未载道途")
			+ " x"
			+ item.Amount
			+ (string.IsNullOrWhiteSpace(item.SourcePlace) ? string.Empty : " · " + item.SourcePlace);
	}

	private static string FormatCaiQiFaTip(in XjZongMenUiCaiQiFaItem item)
	{
		return XjDisplayNameSanitizer.GameTerm(item.DaoTu, "未载道途")
			+ (string.IsNullOrWhiteSpace(item.SourcePlace) ? string.Empty : " · " + item.SourcePlace);
	}

	private static string FormatActorRealm(Actor actor)
	{
		int realm = XjSectCityData.GetRealmLevel(actor);
		return realm switch
		{
			5 => "金丹",
			4 => "紫府",
			3 => "筑基",
			2 => "炼气",
			1 => "胎息",
			_ => "凡俗"
		};
	}
}
