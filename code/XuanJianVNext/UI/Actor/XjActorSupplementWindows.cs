using System;
using System.Collections.Generic;
using System.Text;
using NeoModLoader.General;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Mentorship;
using XuanJianVNext.UI.QianKunDai;

namespace XuanJianVNext.UI.ActorInfo;

/// <summary>
/// Stable UnitWindow extensions. These are fixed buttons built from an empty GameObject;
/// they never join WorldBox's WindowMetaTab/DragOrderContainer. Rich data opens in
/// XuanJian-owned readonly windows, so switching to CityWindow/KingdomWindow leaves the
/// native UnitWindow drag/tween lifecycle untouched.
/// </summary>
internal static class XjActorSupplementWindows
{
	private const string QianKunButtonName = "XuanJianQianKunDaiShortcut";
	private const string MentorshipButtonName = "XuanJianMentorshipShortcut";
	private const string QianKunWindowId = "xuanjian_actor_qiankundai_stable";
	private const string MentorshipWindowId = "xuanjian_actor_mentorship_stable";

	internal static void RefreshShortcuts(UnitWindow window)
	{
		Actor actor = window?.actor;
		Transform favoriteParent = window?._icon_favorite?.transform?.parent;
		Transform parent = favoriteParent?.parent;
		if (actor?.data == null || parent == null || favoriteParent == null)
		{
			return;
		}

		EnsureQianKunShortcut(window, actor, parent, favoriteParent.gameObject);
		EnsureMentorshipShortcut(window, actor, parent, favoriteParent.gameObject);
	}

	private static void EnsureQianKunShortcut(UnitWindow window, Actor actor, Transform parent, GameObject visualTemplate)
	{
		GameObject obj = XjStableUiFactory.FindOrCreateFixedButton(
			parent,
			visualTemplate,
			QianKunButtonName,
			new Vector3(196.8f, -112f, 0f));
		if (obj == null) return;

		bool visible = XjCultivationEligibility.HasCultivationAptitudeTrait(actor);
		if (obj.activeSelf != visible) obj.SetActive(visible);
		if (!visible) return;

		SetIcon(obj, "ui/QianKunDai", "ui/icons/iconBag", "ui/icons/iconInventory");
		Button button = obj.GetComponent<Button>();
		XjActorSupplementButtonBinding binding = obj.GetComponent<XjActorSupplementButtonBinding>()
			?? obj.AddComponent<XjActorSupplementButtonBinding>();
		if (button != null && !binding.QianKunBound)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				Actor current = window?.actor;
				if (!XjSafeCore.IsAliveActor(current)) return;
				ShowQianKunDai(((BaseSystemData)current.data).id);
			});
			binding.QianKunBound = true;
		}
		TipButton tip = obj.GetComponent<TipButton>();
		if (tip != null)
		{
			XjNativeHoverTooltip.Ensure(tip, "乾坤袋", "查看角色携带的功法、资源与修行物资。", "独立窗口，不参与原生人物页拖拽Tab。\n");
		}
	}

	private static void EnsureMentorshipShortcut(UnitWindow window, Actor actor, Transform parent, GameObject visualTemplate)
	{
		GameObject obj = XjStableUiFactory.FindOrCreateFixedButton(
			parent,
			visualTemplate,
			MentorshipButtonName,
			new Vector3(236.8f, -112f, 0f));
		if (obj == null) return;

		bool visible = HasMentorship(actor);
		if (obj.activeSelf != visible) obj.SetActive(visible);
		if (!visible) return;

		SetIcon(obj, "ui/Icons/event/ZongMen", "ui/icons/iconCulture", "ui/icons/iconPeople");
		Button button = obj.GetComponent<Button>();
		XjActorSupplementButtonBinding binding = obj.GetComponent<XjActorSupplementButtonBinding>()
			?? obj.AddComponent<XjActorSupplementButtonBinding>();
		if (button != null && !binding.MentorshipBound)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				Actor current = window?.actor;
				if (!XjSafeCore.IsAliveActor(current)) return;
				ShowMentorship(((BaseSystemData)current.data).id);
			});
			binding.MentorshipBound = true;
		}
		TipButton tip = obj.GetComponent<TipButton>();
		if (tip != null)
		{
			XjNativeHoverTooltip.Ensure(tip, "师徒传承", "查看角色师承与门下弟子。", "独立窗口，不参与原生人物页拖拽Tab。\n");
		}
	}

	private static bool HasMentorship(Actor actor)
	{
		if (!XjSafeCore.IsAliveActor(actor)) return false;
		try
		{
			return XjSectMentorshipSystem.HasRecordedRelation(actor)
				|| XjSectMentorshipSystem.TryGetTeacher(actor, out _)
				|| XjSectMentorshipSystem.GetLiveStudents(actor).Count > 0;
		}
		catch (Exception ex)
		{
			XjExceptionDiagnostics.Report("UnitWindow.MentorshipShortcut.Visibility", ex);
			return false;
		}
	}

	private static void ShowQianKunDai(long actorId)
	{
		if (actorId <= 0L) return;
		XjReadonlyTextWindow.Show(
			QianKunWindowId,
			"xuanjian.qiankundai",
			"ui/QianKunDai",
			() => BuildQianKunDaiText(actorId));
	}

	private static void ShowMentorship(long actorId)
	{
		if (actorId <= 0L) return;
		XjReadonlyTextWindow.Show(
			MentorshipWindowId,
			"xuanjian.mentorship",
			"ui/Icons/event/ZongMen",
			() => BuildMentorshipText(actorId));
	}

	private static string BuildQianKunDaiText(long actorId)
	{
		if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) || !XjSafeCore.IsAliveActor(actor))
		{
			return "角色已不在当前世界。";
		}

		XjQianKunDaiDisplayModel model = XjQianKunDaiTabFormatter.BuildDisplayModelForActor(actor);
		StringBuilder sb = new StringBuilder(2048);
		sb.Append("人物：").AppendLine(string.IsNullOrWhiteSpace(model.ActorName) ? actor.getName() : model.ActorName);
		sb.Append("容量：").Append(model.Items.Length).Append('/').AppendLine(model.Capacity.ToString());
		if (model.UpdatedYear > 0) sb.Append("最近整理：").Append(model.UpdatedYear).AppendLine("年");
		sb.AppendLine();
		if (model.Items == null || model.Items.Length == 0)
		{
			sb.Append("暂无随身修行物资。");
			return sb.ToString();
		}

		string lastCategory = null;
		for (int i = 0; i < model.Items.Length; i++)
		{
			XjQianKunDaiDisplayItem item = model.Items[i];
			string category = ResolveCategoryLabel(item.Category);
			if (!string.Equals(lastCategory, category, StringComparison.Ordinal))
			{
				if (lastCategory != null) sb.AppendLine();
				sb.Append("【").Append(category).AppendLine("】");
				lastCategory = category;
			}
			sb.Append("· ").Append(string.IsNullOrWhiteSpace(item.Name) ? "未名物" : item.Name);
			if (item.Count > 1) sb.Append(" ×").Append(item.Count);
			if (item.GongFaGrade > 0) sb.Append(" · ").Append(item.GongFaGrade).Append("品");
			if (!string.IsNullOrWhiteSpace(item.DaoTu)) sb.Append(" · ").Append(item.DaoTu);
			if (!string.IsNullOrWhiteSpace(item.Source)) sb.Append(" · 来源：").Append(item.Source);
			if (item.AcquiredYear > 0) sb.Append(" · ").Append(item.AcquiredYear).Append("年");
			sb.AppendLine();
		}
		return sb.ToString();
	}

	private static string BuildMentorshipText(long actorId)
	{
		if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor actor) || !XjSafeCore.IsAliveActor(actor))
		{
			return "角色已不在当前世界。";
		}

		StringBuilder sb = new StringBuilder(1024);
		sb.Append("人物：").AppendLine(XjMentorshipTabView.ResolveActorName(actor));
		sb.Append("身份：").AppendLine(XjMentorshipTabView.ResolveTooltipSummary(actor));
		sb.AppendLine(XjMentorshipTabView.ResolveTooltipDetails(actor));
		sb.AppendLine();

		if (XjSectMentorshipSystem.TryGetTeacher(actor, out Actor teacher) && XjSafeCore.IsAliveActor(teacher))
		{
			sb.AppendLine("【师承】");
			AppendActorLine(sb, teacher);
		}
		else
		{
			sb.AppendLine("【师承】暂无");
		}

		List<Actor> students = XjSectMentorshipSystem.GetLiveStudents(actor) ?? new List<Actor>();
		students.RemoveAll(student => !XjSafeCore.IsAliveActor(student));
		students.Sort((left, right) => ResolveActorId(left).CompareTo(ResolveActorId(right)));
		sb.AppendLine();
		sb.Append("【门下弟子】").AppendLine(students.Count.ToString());
		if (students.Count == 0)
		{
			sb.AppendLine("暂无");
		}
		else
		{
			for (int i = 0; i < students.Count; i++) AppendActorLine(sb, students[i]);
		}
		return sb.ToString();
	}

	private static void AppendActorLine(StringBuilder sb, Actor actor)
	{
		if (sb == null || !XjSafeCore.IsAliveActor(actor)) return;
		sb.Append("· ").Append(XjMentorshipTabView.ResolveActorName(actor));
		string summary = XjMentorshipTabView.ResolveTooltipSummary(actor);
		if (!string.IsNullOrWhiteSpace(summary)) sb.Append(" · ").Append(summary);
		sb.AppendLine();
	}

	private static long ResolveActorId(Actor actor)
	{
		return actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
	}

	private static string ResolveCategoryLabel(string category)
	{
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryCaiQi, StringComparison.Ordinal)) return "先天之气";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryGongFa, StringComparison.Ordinal)) return "功法";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryAlchemyMaterial, StringComparison.Ordinal)) return "药材";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryAlchemyPill, StringComparison.Ordinal)) return "丹药";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryAlchemyRecipe, StringComparison.Ordinal)) return "丹方";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryTalismanMaterial, StringComparison.Ordinal)) return "符材";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryTalismanItem, StringComparison.Ordinal)) return "符箓";
		if (string.Equals(category, XjQianKunDaiRegistry.CategoryFormationMaterial, StringComparison.Ordinal)) return "阵材";
		if (string.Equals(category, XjQianKunDaiTabFormatter.DisplayCategoryQiuJinFa, StringComparison.Ordinal)) return "求金法";
		if (string.Equals(category, XjQianKunDaiTabFormatter.DisplayCategoryCaiQiFa, StringComparison.Ordinal)) return "采气法";
		return string.IsNullOrWhiteSpace(category) ? "其他" : category;
	}

	private static void SetIcon(GameObject obj, params string[] candidates)
	{
		if (obj == null) return;
		Image image = obj.transform.Find("Icon")?.GetComponent<Image>();
		if (image == null) image = obj.GetComponent<Image>();
		if (image == null) return;
		Sprite sprite = null;
		for (int i = 0; i < candidates.Length && sprite == null; i++)
		{
			string path = candidates[i];
			if (string.IsNullOrWhiteSpace(path)) continue;
			try { sprite = SpriteTextureLoader.getSprite(path) ?? Resources.Load<Sprite>(path); }
			catch { sprite = Resources.Load<Sprite>(path); }
		}
		if (sprite != null) image.sprite = sprite;
		image.color = Color.white;
	}
}

internal sealed class XjActorSupplementButtonBinding : MonoBehaviour
{
	internal bool QianKunBound;
	internal bool MentorshipBound;
}
