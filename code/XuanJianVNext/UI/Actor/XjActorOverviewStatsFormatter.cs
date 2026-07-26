using System;
using System.Collections.Generic;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.UI.ActorInfo;

internal sealed class XjActorOverviewDeferredRefresh : MonoBehaviour
{
	private UnitWindow _window;
	private int _remainingPasses;
	private int _nextFrame;

	internal void Schedule(UnitWindow window)
	{
		_window = window;
		_remainingPasses = 2;
		_nextFrame = Time.frameCount + 1;
		enabled = true;
	}

	private void LateUpdate()
	{
		if (!enabled || Time.frameCount < _nextFrame)
		{
			return;
		}
		if (_window == null || _window.actor?.data == null)
		{
			_window = null;
			_remainingPasses = 0;
			enabled = false;
			return;
		}

		XjActorOverviewStatsFormatter.Refresh(_window, forceLayout: true);
		_remainingPasses--;
		if (_remainingPasses <= 0)
		{
			_window = null;
			enabled = false;
			return;
		}
		_nextFrame = Time.frameCount + 1;
	}
}

internal static class XjActorOverviewStatsFormatter
{
	private const int RefreshCadenceFrames = 20;
	private static long _lastActorId;
	private static int _lastRefreshFrame = -9999;
	private static int _lastWorldYear = -1;

	private static readonly OverviewStatIcon[] Icons =
	{
		new OverviewStatIcon("ZhenYuan", "真元", "ZhenYuan", "ZhenYuan.png", "ZhenQi", "ZhenQi.png"),
		new OverviewStatIcon("MingShu", "命数", "MingShu", "MingShu.png", "XueQi", "XueQi.png"),
		new OverviewStatIcon("HuiGuang", "慧光", "HuiGuang", "HuiGuang.png", "HuiGuang", "HuiGuang.png"),
		new OverviewStatIcon("XjArmorPen", "减穿", true),
		new OverviewStatIcon("XjTrueDamage", "真伤", true),
		new OverviewStatIcon("XjAccuracy", "命中", true),
		new OverviewStatIcon("XjCrit", "暴击", true),
		new OverviewStatIcon("XjAttackSpeed", "攻速", true),
		new OverviewStatIcon("XjSameRealmDamage", "同境", true),
		new OverviewStatIcon("XjShieldBreak", "破盾", true),
		new OverviewStatIcon("XjLifesteal", "吸血", true),
		new OverviewStatIcon("XjDamageReduction", "减伤", true),
		new OverviewStatIcon("XjHealthShield", "护盾", true),
		new OverviewStatIcon("XjDodge", "闪避", true),
		new OverviewStatIcon("XjCritTakenReduction", "抗暴", true),
		new OverviewStatIcon("XjHealback", "回血", true),
		new OverviewStatIcon("XjBreakthrough", "破境", true)
	};

	internal static void RequestDeferredRefresh(UnitWindow window)
	{
		if (window == null)
		{
			return;
		}
		try
		{
			GameObject host = ((Component)window).gameObject;
			XjActorOverviewDeferredRefresh deferred = host.GetComponent<XjActorOverviewDeferredRefresh>();
			if (deferred == null)
			{
				deferred = host.AddComponent<XjActorOverviewDeferredRefresh>();
			}
			deferred.Schedule(window);
		}
		catch
		{
		}
	}

	internal static void Refresh(UnitWindow window, bool forceLayout = false)
	{
		if (window == null || window.actor?.data == null || !((NanoObject)window.actor).isAlive())
		{
			return;
		}

		long actorId = ((BaseSystemData)window.actor.data).id;
		int frame = Time.frameCount;
		int worldYear = World.world?.map_stats?.year ?? 0;
		bool hasExistingSurface = HasExistingIconSurface(window);
		if (!forceLayout
			&& hasExistingSurface
			&& actorId == _lastActorId
			&& worldYear == _lastWorldYear
			&& frame - _lastRefreshFrame < RefreshCadenceFrames)
		{
			return;
		}

		if (!EnsureIconGroup(window))
		{
			return;
		}

		XjActorCultivationSnapshot cultivation = XjActorCultivationSnapshotBuilder.Build(window.actor);
		SetIconValue(window, "ZhenYuan", ToNonNegativeInteger(cultivation.ZhenYuan));
		SetIconValue(window, "MingShu", ToNonNegativeInteger(cultivation.MingShu));
		SetIconValue(window, "HuiGuang", ToNonNegativeInteger(cultivation.HuiGuang));
		XjFaBaoBonusProfile profile = BuildOverviewBonusProfile(window.actor);
		SetPercentIconValue(window, "XjArmorPen", profile.ArmorPenetration);
		SetPercentIconValue(window, "XjTrueDamage", profile.TrueDamageRatio);
		SetPercentIconValue(window, "XjAccuracy", profile.AccuracyBonus);
		SetPercentIconValue(window, "XjCrit", profile.CritBonus);
		SetPercentIconValue(window, "XjAttackSpeed", profile.AttackSpeedBonus);
		SetPercentIconValue(window, "XjSameRealmDamage", profile.SameRealmDamageBonus);
		SetPercentIconValue(window, "XjShieldBreak", profile.ShieldBreakBonus);
		SetPercentIconValue(window, "XjLifesteal", profile.Lifesteal);
		SetPercentIconValue(window, "XjDamageReduction", profile.DamageReduction);
		SetPercentIconValue(window, "XjHealthShield", profile.HealthShield);
		SetPercentIconValue(window, "XjDodge", profile.DodgeBonus);
		SetPercentIconValue(window, "XjCritTakenReduction", profile.CritTakenReduction);
		SetPercentIconValue(window, "XjHealback", profile.HealbackBonus);
		SetPercentIconValue(window, "XjBreakthrough", profile.BreakthroughChanceBonus);
		ArrangeOverviewRows(window);
		if (forceLayout)
		{
			ForceOverviewLayout(window);
		}
		_lastActorId = actorId;
		_lastWorldYear = worldYear;
		_lastRefreshFrame = frame;
	}

	private static bool HasExistingIconSurface(UnitWindow window)
	{
		Transform content = ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
		Transform targetGroup = FindKillsGroup(content);
		return targetGroup != null
			&& XjUiSurfaceOwnership.FindSingleRoot(
				targetGroup,
				"XjOverviewStatsGroup",
				"overview",
				nameof(XjActorOverviewStatsFormatter)) != null;
	}

	private static bool EnsureIconGroup(UnitWindow window)
	{
		Transform content = ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
		if (content == null)
		{
			return false;
		}

		Transform targetGroup = FindKillsGroup(content);
		if (targetGroup == null)
		{
			return false;
		}

		// Use ownership system to manage surface root
		Transform surfaceRoot = XjUiSurfaceOwnership.FindSingleRoot(
			targetGroup, "XjOverviewStatsGroup", "overview", nameof(XjActorOverviewStatsFormatter));
		RemoveLegacyFaBaoOverviewIcons(targetGroup);
		Transform templateIcon = targetGroup.Find("i_kills");
		if (templateIcon == null && targetGroup.childCount > 0)
		{
			templateIcon = targetGroup.GetChild(0);
		}

		if (templateIcon == null)
		{
			return false;
		}

		for (int i = 0; i < Icons.Length; i++)
		{
			if (targetGroup.Find(Icons[i].Id) != null)
			{
				continue;
			}

			Transform iconTransform = UnityEngine.Object.Instantiate(templateIcon, targetGroup);
			ConfigureIcon(iconTransform, Icons[i]);
		}

		ArrangeKillsRow(content, targetGroup);
		if (surfaceRoot != null)
		{
			return true;
		}

		// Create and claim the surface ownership marker
		GameObject markerObj = new GameObject("XjOverviewStatsGroup");
		markerObj.transform.SetParent(targetGroup, false);
		markerObj.SetActive(false);
		XjUiSurfaceOwnership.Claim(markerObj.transform, "overview", nameof(XjActorOverviewStatsFormatter));
		return true;
	}

	private static void RemoveLegacyFaBaoOverviewIcons(Transform targetGroup)
	{
		if (targetGroup == null)
		{
			return;
		}

		string[] legacyIds =
		{
			"FaBaoDamage", "FaBaoArmorPen", "FaBaoTrueDamage", "FaBaoAccuracy", "FaBaoCrit",
			"FaBaoAttackSpeed", "FaBaoSameRealmDamage", "FaBaoShieldBreak", "FaBaoLifesteal",
			"FaBaoDamageReduction", "FaBaoHealth", "FaBaoShield", "FaBaoDodge",
			"FaBaoCritTakenReduction", "FaBaoHealback", "FaBaoCultivation", "FaBaoGuoWeiYiXiang",
			"FaBaoMingShu", "FaBaoHuiGuang", "FaBaoLifespan", "FaBaoBreakthrough"
		};
		for (int i = 0; i < legacyIds.Length; i++)
		{
			Transform child = targetGroup.Find(legacyIds[i]);
			if (child != null)
			{
				UnityEngine.Object.Destroy(child.gameObject);
			}
		}
	}

	private static Transform FindKillsGroup(Transform content)
	{
		if (content == null)
		{
			return null;
		}

		for (int i = 0; i < content.childCount; i++)
		{
			Transform child = content.GetChild(i);
			if (child != null && child.Find("i_kills") != null)
			{
				return child;
			}
		}

		return content.childCount > 4 ? content.GetChild(4) : (content.childCount > 0 ? content.GetChild(content.childCount - 1) : null);
	}

	private static void ArrangeKillsRow(Transform content, Transform targetGroup)
	{
		if (content == null || targetGroup == null)
		{
			return;
		}

		Transform[] orderedIcons = BuildOrderedIcons(targetGroup);

		for (int i = 0; i < orderedIcons.Length; i++)
		{
			if (orderedIcons[i] != null)
			{
				orderedIcons[i].SetSiblingIndex(i);
			}
		}

		Transform templateGroup = FindTemplateIconRow(content, targetGroup);
		AlignRowLayout(targetGroup, templateGroup);
		RectTransform targetRect = targetGroup as RectTransform;
		RectTransform templateRect = templateGroup as RectTransform;
		if (targetRect != null && templateRect != null)
		{
			CopyRowRectTransform(targetRect, templateRect);
			EnsureRowHeight(targetRect, templateGroup, orderedIcons.Length);
		}

		RectTransform[] columns = GetTemplateColumns(templateGroup);
		for (int i = 0; i < orderedIcons.Length; i++)
		{
			if (orderedIcons[i] == null)
			{
				continue;
			}

			RectTransform iconRect = orderedIcons[i] as RectTransform;
			if (iconRect == null)
			{
				continue;
			}

			if (columns != null && i < columns.Length && columns[i] != null)
			{
				CopyRectTransform(iconRect, columns[i % columns.Length]);
				ApplyRowOffset(iconRect, templateGroup, i, columns.Length);
				continue;
			}

			ApplyBackupColumn(iconRect, i);
		}
	}

	private static Transform[] BuildOrderedIcons(Transform targetGroup)
	{
		if (targetGroup == null)
		{
			return Array.Empty<Transform>();
		}

		List<Transform> ordered = new List<Transform>(Icons.Length + 1);
		Transform kills = targetGroup.Find("i_kills");
		if (kills != null && kills.gameObject.activeSelf)
		{
			ordered.Add(kills);
		}

		for (int i = 0; i < Icons.Length; i++)
		{
			Transform icon = targetGroup.Find(Icons[i].Id);
			if (icon != null && icon.gameObject.activeSelf)
			{
				ordered.Add(icon);
			}
		}

		return ordered.ToArray();
	}

	private static Transform FindTemplateIconRow(Transform content, Transform targetGroup)
	{
		if (content == null)
		{
			return null;
		}

		for (int i = 0; i < content.childCount; i++)
		{
			Transform child = content.GetChild(i);
			if (child == null || child == targetGroup)
			{
				continue;
			}

			if (GetTemplateColumns(child)?.Length >= 4)
			{
				return child;
			}
		}

		return null;
	}

	private static void AlignRowLayout(Transform targetGroup, Transform templateGroup)
	{
		if (targetGroup == null)
		{
			return;
		}

		HorizontalLayoutGroup targetHorizontal = targetGroup.GetComponent<HorizontalLayoutGroup>();
		HorizontalLayoutGroup templateHorizontal = templateGroup != null ? templateGroup.GetComponent<HorizontalLayoutGroup>() : null;
		if (targetHorizontal != null)
		{
			if (templateHorizontal != null)
			{
				targetHorizontal.padding = templateHorizontal.padding;
				targetHorizontal.spacing = templateHorizontal.spacing;
				targetHorizontal.childControlWidth = templateHorizontal.childControlWidth;
				targetHorizontal.childControlHeight = templateHorizontal.childControlHeight;
				targetHorizontal.childForceExpandWidth = templateHorizontal.childForceExpandWidth;
				targetHorizontal.childForceExpandHeight = templateHorizontal.childForceExpandHeight;
			}

			targetHorizontal.childAlignment = TextAnchor.MiddleLeft;
		}

		GridLayoutGroup targetGrid = targetGroup.GetComponent<GridLayoutGroup>();
		GridLayoutGroup templateGrid = templateGroup != null ? templateGroup.GetComponent<GridLayoutGroup>() : null;
		if (targetGrid != null)
		{
			if (templateGrid != null)
			{
				targetGrid.cellSize = templateGrid.cellSize;
				targetGrid.spacing = templateGrid.spacing;
				targetGrid.padding = templateGrid.padding;
			}

			targetGrid.startCorner = GridLayoutGroup.Corner.UpperLeft;
			targetGrid.startAxis = GridLayoutGroup.Axis.Horizontal;
			targetGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
			targetGrid.constraintCount = 5;
			targetGrid.childAlignment = TextAnchor.UpperLeft;
		}
	}

	private static void EnsureRowHeight(RectTransform target, Transform templateGroup, int itemCount)
	{
		if (target == null || itemCount <= 0)
		{
			return;
		}

		int columnCount = Math.Min(5, Math.Max(1, GetTemplateColumns(templateGroup)?.Length ?? 5));
		int rowCount = Math.Max(1, (int)Math.Ceiling(itemCount / (double)columnCount));
		if (rowCount <= 1)
		{
			return;
		}

		float rowStep = ResolveRowStep(templateGroup);
		Vector2 size = target.sizeDelta;
		size.y = Math.Max(size.y, rowStep * rowCount);
		target.sizeDelta = size;

		LayoutElement layout = target.GetComponent<LayoutElement>() ?? target.gameObject.AddComponent<LayoutElement>();
		layout.minHeight = Math.Max(layout.minHeight, size.y);
		layout.preferredHeight = Math.Max(layout.preferredHeight, size.y);
	}

	private static void ApplyRowOffset(RectTransform target, Transform templateGroup, int index, int columnCount)
	{
		if (target == null || columnCount <= 0)
		{
			return;
		}

		int row = index / columnCount;
		if (row <= 0)
		{
			return;
		}

		Vector2 position = target.anchoredPosition;
		position.y -= ResolveRowStep(templateGroup) * row;
		target.anchoredPosition = position;
	}

	private static float ResolveRowStep(Transform templateGroup)
	{
		GridLayoutGroup grid = templateGroup != null ? templateGroup.GetComponent<GridLayoutGroup>() : null;
		if (grid != null)
		{
			return Math.Max(1f, grid.cellSize.y + grid.spacing.y);
		}

		RectTransform[] columns = GetTemplateColumns(templateGroup);
		if (columns != null && columns.Length > 0 && columns[0] != null)
		{
			return Math.Max(1f, columns[0].sizeDelta.y + 8f);
		}

		return 34f;
	}

	private static RectTransform[] GetTemplateColumns(Transform group)
	{
		if (group == null || group.childCount == 0)
		{
			return null;
		}

		RectTransform[] columns = new RectTransform[Math.Min(5, group.childCount)];
		int count = 0;
		for (int i = 0; i < group.childCount && count < columns.Length; i++)
		{
			RectTransform rect = group.GetChild(i) as RectTransform;
			if (rect != null)
			{
				columns[count++] = rect;
			}
		}

		if (count == columns.Length)
		{
			return columns;
		}

		RectTransform[] trimmed = new RectTransform[count];
		Array.Copy(columns, trimmed, count);
		return trimmed;
	}

	private static void CopyRectTransform(RectTransform target, RectTransform source)
	{
		if (target == null || source == null)
		{
			return;
		}

		target.anchorMin = source.anchorMin;
		target.anchorMax = source.anchorMax;
		target.pivot = source.pivot;
		target.sizeDelta = source.sizeDelta;
		target.anchoredPosition = source.anchoredPosition;
		target.localScale = source.localScale;
	}

	private static void CopyRowRectTransform(RectTransform target, RectTransform source)
	{
		if (target == null || source == null)
		{
			return;
		}

		float rowY = target.anchoredPosition.y;
		CopyRectTransform(target, source);
		Vector2 position = target.anchoredPosition;
		position.y = rowY;
		target.anchoredPosition = position;
	}

	private static void ApplyBackupColumn(RectTransform target, int column)
	{
		if (target == null)
		{
			return;
		}

		Vector2 position = target.anchoredPosition;
		position.x = column * 102f;
		target.anchoredPosition = position;
	}

	private static void ConfigureIcon(Transform iconTransform, OverviewStatIcon icon)
	{
		if (iconTransform == null)
		{
			return;
		}

		EnsureIconLocalization(icon);
		iconTransform.name = icon.Id;
		StatsIcon statsIcon = iconTransform.GetComponent<StatsIcon>();
		if (statsIcon != null)
		{
			statsIcon.name = icon.Id;
			Image iconImage = statsIcon.getIcon();
			if (icon.UseTextIcon)
			{
				ConfigureTextIcon(iconImage, icon.DisplayName);
			}
			else
			{
				Sprite sprite = LoadOverviewSprite(icon.ResourcePaths);
				if (sprite != null && iconImage != null)
				{
					iconImage.sprite = sprite;
				}
			}
		}

		TipButton tip = iconTransform.GetComponent<TipButton>();
		if (tip != null)
		{
			string localized = LM.Get("statsIcon_" + icon.Id);
			tip.textOnClick = string.IsNullOrWhiteSpace(localized) || localized == "statsIcon_" + icon.Id
				? icon.DisplayName
				: localized;
		}
	}

	private static void EnsureIconLocalization(OverviewStatIcon icon)
	{
		if (string.IsNullOrWhiteSpace(icon.Id) || string.IsNullOrWhiteSpace(icon.DisplayName))
		{
			return;
		}

		string key = "statsIcon_" + icon.Id;
		if (LM.Get(key) == key)
		{
			LocalizedTextManager.add(key, icon.DisplayName, false, string.Empty, true);
		}
	}

	private static void ConfigureTextIcon(Image iconImage, string text)
	{
		if (iconImage == null)
		{
			return;
		}

		iconImage.enabled = false;
		Transform existing = iconImage.transform.Find("XjTextIcon");
		Text textComponent = existing != null ? existing.GetComponent<Text>() : null;
		if (textComponent == null)
		{
			GameObject textObject = new GameObject("XjTextIcon");
			textObject.transform.SetParent(iconImage.transform, false);
			RectTransform rect = textObject.AddComponent<RectTransform>();
			rect.anchorMin = Vector2.zero;
			rect.anchorMax = Vector2.one;
			rect.offsetMin = Vector2.zero;
			rect.offsetMax = Vector2.zero;
			textComponent = textObject.AddComponent<Text>();
			textComponent.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
			textComponent.alignment = TextAnchor.MiddleCenter;
			textComponent.fontSize = 7;
			textComponent.color = new Color(1f, 0.61f, 0.11f, 1f);
		}

		textComponent.text = string.IsNullOrWhiteSpace(text) ? "?" : text.Trim();
	}

	private static Sprite LoadOverviewSprite(string[] resourcePaths)
	{
		if (resourcePaths == null || resourcePaths.Length == 0)
		{
			return null;
		}

		for (int i = 0; i < resourcePaths.Length; i++)
		{
			string resourcePath = resourcePaths[i];
			if (string.IsNullOrWhiteSpace(resourcePath))
			{
				continue;
			}

			string path = resourcePath.Trim().Replace("\\", "/");
			Sprite sprite = SpriteTextureLoader.getSprite(path)
				?? SpriteTextureLoader.getSprite("GameResources/" + path)
				?? SpriteTextureLoader.getSprite("GameResources/" + path + ".png")
				?? Resources.Load<Sprite>(path)
				?? Resources.Load<Sprite>("GameResources/" + path);
			if (sprite != null)
			{
				return sprite;
			}
		}

		return null;
	}

	private static void SetIconValue(UnitWindow window, string id, int value)
	{
		SetIconVisibility(window, id, true);
		try
		{
			window.setIconValue(id, value, null, string.Empty, false, string.Empty, '/');
		}
		catch (Exception)
		{
		}
	}

	private static void SetPercentIconValue(UnitWindow window, string id, float ratio)
	{
		int value = (int)Math.Round(Math.Max(0f, ratio) * 100f);
		bool visible = value > 0;
		SetIconVisibility(window, id, visible);
		if (!visible)
		{
			return;
		}

		SetIconValue(window, id, value);
	}

	private static void SetIconVisibility(UnitWindow window, string id, bool visible)
	{
		Transform icon = FindOverviewIcon(window, id);
		if (icon != null)
		{
			icon.gameObject.SetActive(visible);
		}
	}

	private static Transform FindOverviewIcon(UnitWindow window, string id)
	{
		if (window == null || string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		Transform content = ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
		Transform targetGroup = FindKillsGroup(content);
		return targetGroup == null ? null : targetGroup.Find(id);
	}

	private static void ArrangeOverviewRows(UnitWindow window)
	{
		if (window == null)
		{
			return;
		}

		Transform content = ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
		Transform targetGroup = FindKillsGroup(content);
		if (content != null && targetGroup != null)
		{
			ArrangeKillsRow(content, targetGroup);
		}
	}

	private static void ForceOverviewLayout(UnitWindow window)
	{
		if (window == null)
		{
			return;
		}
		try
		{
			Transform content = ((Component)window).transform.Find("Background/Scroll View/Viewport/Content/content_more_icons");
			Transform targetGroup = FindKillsGroup(content);
			Canvas.ForceUpdateCanvases();
			ForceRebuild(targetGroup as RectTransform);
			ForceRebuild(content as RectTransform);
			// content_more_icons changes the native ScrollRect Content height. Rebuild
			// its ancestors too; rebuilding only the icon row is why the first open
			// clipped lower rows until another tab caused a full native layout pass.
			Transform ancestor = content?.parent;
			for (int depth = 0; ancestor != null && depth < 3; depth++, ancestor = ancestor.parent)
			{
				ForceRebuild(ancestor as RectTransform);
			}
			Canvas.ForceUpdateCanvases();
		}
		catch
		{
		}
	}


	private static void ForceRebuild(RectTransform rect)
	{
		if (rect == null)
		{
			return;
		}
		LayoutRebuilder.MarkLayoutForRebuild(rect);
		LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
	}

	private static XjFaBaoBonusProfile BuildOverviewBonusProfile(Actor actor)
	{
		float cultivation = 0f;
		float guoWei = 0f;
		float attack = 0f;
		float reduction = 0f;
		float health = 0f;
		float penetration = 0f;
		float shield = 0f;
		float lifesteal = 0f;
		float dodge = 0f;
		float critTaken = 0f;
		float healback = 0f;
		float mingShu = 0f;
		float huiGuang = 0f;
		float lifespan = 0f;
		float accuracy = 0f;
		float crit = 0f;
		float attackSpeed = 0f;
		float sameRealm = 0f;
		float shieldBreak = 0f;
		float breakthrough = 0f;
		float trueDamage = 0f;
		AccumulateProfile(
			XjRealmCombatBonuses.TryGetProfile(actor, out XjFaBaoBonusProfile realmProfile),
			realmProfile,
			ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
			ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
			ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
			ref shieldBreak, ref breakthrough, ref trueDamage);
		AccumulateProfile(
			XjFaBaoBonusService.TryGetProfile(actor, out XjFaBaoBonusProfile faBaoProfile),
			faBaoProfile,
			ref cultivation, ref guoWei, ref attack, ref reduction, ref health, ref penetration,
			ref shield, ref lifesteal, ref dodge, ref critTaken, ref healback, ref mingShu,
			ref huiGuang, ref lifespan, ref accuracy, ref crit, ref attackSpeed, ref sameRealm,
			ref shieldBreak, ref breakthrough, ref trueDamage);
		return new XjFaBaoBonusProfile(
			cultivation, guoWei, attack, reduction, health, penetration, shield, lifesteal,
			dodge, critTaken, healback, mingShu, huiGuang, lifespan, accuracy, crit, attackSpeed,
			sameRealm, shieldBreak, breakthrough, trueDamage);
	}

	private static void AccumulateProfile(
		bool found,
		in XjFaBaoBonusProfile profile,
		ref float cultivation,
		ref float guoWei,
		ref float attack,
		ref float reduction,
		ref float health,
		ref float penetration,
		ref float shield,
		ref float lifesteal,
		ref float dodge,
		ref float critTaken,
		ref float healback,
		ref float mingShu,
		ref float huiGuang,
		ref float lifespan,
		ref float accuracy,
		ref float crit,
		ref float attackSpeed,
		ref float sameRealm,
		ref float shieldBreak,
		ref float breakthrough,
		ref float trueDamage)
	{
		if (!found)
		{
			return;
		}

		cultivation += profile.CultivationSpeedBonus;
		guoWei += profile.GuoWeiYiXiangBonus;
		attack += profile.AttackBonus;
		reduction += profile.DamageReduction;
		health += profile.HealthBonus;
		penetration += profile.ArmorPenetration;
		shield += profile.HealthShield;
		lifesteal += profile.Lifesteal;
		dodge += profile.DodgeBonus;
		critTaken += profile.CritTakenReduction;
		healback += profile.HealbackBonus;
		mingShu += profile.MingShuBonus;
		huiGuang += profile.HuiGuangBonus;
		lifespan += profile.LifespanBonus;
		accuracy += profile.AccuracyBonus;
		crit += profile.CritBonus;
		attackSpeed += profile.AttackSpeedBonus;
		sameRealm += profile.SameRealmDamageBonus;
		shieldBreak += profile.ShieldBreakBonus;
		breakthrough += profile.BreakthroughChanceBonus;
		trueDamage += profile.TrueDamageRatio;
	}

	private static int ToNonNegativeInteger(float value)
	{
		return (int)Math.Floor(Math.Max(0f, value));
	}

	private readonly struct OverviewStatIcon
	{
		internal readonly string Id;
		internal readonly string DisplayName;
		internal readonly string[] ResourcePaths;
		internal readonly bool UseTextIcon;

		internal OverviewStatIcon(string id, string displayName, params string[] resourcePaths)
		{
			Id = id ?? string.Empty;
			DisplayName = displayName ?? string.Empty;
			ResourcePaths = resourcePaths ?? Array.Empty<string>();
			UseTextIcon = false;
		}

		internal OverviewStatIcon(string id, string displayName, bool useTextIcon)
		{
			Id = id ?? string.Empty;
			DisplayName = displayName ?? string.Empty;
			ResourcePaths = Array.Empty<string>();
			UseTextIcon = useTextIcon;
		}
	}
}
