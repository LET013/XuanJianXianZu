using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Sect;
using XuanJianVNext.UI.Foundation;
using XuanJianVNext.UI.Common;
using XuanJianVNext.Systems.Rank;

namespace XuanJianVNext.UI.Rank;

internal sealed class XjRankCardView : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
	private long actorId;

	internal void Setup(in XjRankItem item, int index, string sortValue)
	{
		Actor actor = item.Actor;
		actorId = actor?.data == null ? 0L : ((BaseSystemData)actor.data).id;
		ApplyBackground(index);
		SetText("RankText", (index + 1).ToString(CultureInfo.InvariantCulture), RankColor(index));
		string displayName = StripRealmSuffix(XjUiSafeText.Player(item.Name, string.Empty));
		string daoTu = XjUiSafeText.GameTerm(item.DaoTu, "未定");
		string realm = XjUiSafeText.GameTerm(item.RealmDisplay, "未入道");
		SetText("NameText", displayName, Color.white);
		SetText("RightText", realm, XjUiTheme.AccentBlue);
		SetText("PowerText", sortValue, new Color(1f, 0.82f, 0.32f));
		SetText("BelongText", ResolveBelonging(actor), XjUiTheme.AccentGreen);
		SetRichText("DetailText", BuildDetailText(item, daoTu));

		UiUnitAvatarElement avatar = GetComponentInChildren<UiUnitAvatarElement>(true);
		if (avatar != null)
		{
			bool valid = actor?.data != null;
			avatar.gameObject.SetActive(valid);
			if (valid)
			{
				avatar.show(actor);
				if (avatar.clanBanner != null) avatar.clanBanner.gameObject.SetActive(false);
				// 排行榜卡片自己拥有点击/悬浮入口。原生头像只承担渲染，
				// 禁止其第二套 TipButton/EventTrigger 再次进入 showActor。
				XjNativeAvatarInteractionSafety.DisableNativeInteraction(avatar.gameObject);
			}
		}

		Button button = GetComponent<Button>();
		if (button != null)
		{
			button.onClick.RemoveAllListeners();
			button.onClick.AddListener(() =>
			{
				// 角色页仍完全由原生 openUnitWindow 打开；这里只把排行榜 ScrollRect
				// 在同一点击帧留下的惯性和重复点击合并掉。否则卡片回收/窗口 OnEnable
				// 会同帧交错，表现为角色页内图标、按钮不断跳动。
				if (TryResolveLiveActor(out Actor current))
				{
					XjRankWindow.OpenActorFromRank(current);
				}
			});
		}
	}

	internal void Recycle()
	{
		Tooltip.hideTooltip();
		actorId = 0L;
		Button button = GetComponent<Button>();
		if (button != null) button.onClick.RemoveAllListeners();
		UiUnitAvatarElement avatar = GetComponentInChildren<UiUnitAvatarElement>(true);
		if (avatar != null) avatar.gameObject.SetActive(false);
	}

	public void OnPointerEnter(PointerEventData eventData)
	{
		// 排行榜卡片已经直接显示姓名、境界、道途与排序值。旧实现把动态正文交给
		// Tooltip.tip_description，原生随后将整句当本地化Key查询，造成大量 missing-text 日志。
		// 这里不再为卡片建立第二套悬浮提示；点击人物仍按稳定路径打开详情。
	}

	private bool TryResolveLiveActor(out Actor current)
	{
		current = null;
		if (actorId > 0L)
		{
			if (!XjActorRegistry.ResolveKnownOrWorld(actorId, out Actor resolved)
				|| resolved?.data == null
				|| !resolved.isAlive())
			{
				// 已经有稳定ID却无法从当前世界重新解析时，缓存Actor就是失效快照，
				// 绝不能退回去继续打开原生人物面板。
				return false;
			}
			current = resolved;
			return true;
		}

		return false;
	}

	public void OnPointerExit(PointerEventData eventData)
	{
		Tooltip.hideTooltip();
	}

	private static string StripRealmSuffix(string value)
	{
		string text = (value ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text)) return string.Empty;
		string[] markers =
		{
			"-道胎", "-真君羽士", "-郁仪仙", "-结璘仙", "-胎息", "-炼气", "-练气", "-筑基", "-黄冠", "-紫府", "-真人", "-金丹", "-神丹"
		};
		for (int i = 0; i < markers.Length; i++)
		{
			int index = text.LastIndexOf(markers[i], System.StringComparison.Ordinal);
			if (index > 0 && index + markers[i].Length == text.Length)
			{
				return text.Substring(0, index).TrimEnd();
			}
		}
		return text;
	}

	private void SetText(string childName, string value, Color color)
	{
		Text text = transform.Find(childName)?.GetComponent<Text>();
		if (text == null) return;
		text.text = value ?? string.Empty;
		text.color = color;
	}

	private void SetRichText(string childName, string value)
	{
		Text text = transform.Find(childName)?.GetComponent<Text>();
		if (text == null) return;
		text.supportRichText = true;
		text.text = value ?? string.Empty;
		text.color = Color.white;
	}

	private void ApplyBackground(int index)
	{
		Image image = GetComponent<Image>();
		if (image == null) return;
		if (index == 0)
		{
			image.color = new Color(0.48f, 0.32f, 0.09f, 0.98f);
			return;
		}
		if (index == 1)
		{
			image.color = new Color(0.31f, 0.38f, 0.33f, 0.98f);
			return;
		}
		if (index == 2)
		{
			image.color = new Color(0.36f, 0.23f, 0.12f, 0.98f);
			return;
		}
		image.color = index % 2 == 0
			? new Color(0.20f, 0.27f, 0.23f, 0.98f)
			: new Color(0.15f, 0.22f, 0.18f, 0.98f);
	}

	private static string BuildDetailText(in XjRankItem item, string daoTu)
	{
		string safeDaoTu = EscapeRichText(daoTu);
		string detail = "<color=" + XjRankDaoTuPalette.ResolveHex(daoTu) + ">" + safeDaoTu + "</color>";
		if (!string.IsNullOrWhiteSpace(item.JinDan))
		{
			string jinDan = EscapeRichText(XjUiSafeText.GameTerm(item.JinDan, string.Empty));
			detail += "　<color=#B9BBAF>" + jinDan + "</color>";
		}
		return detail;
	}

	private static string EscapeRichText(string value)
	{
		return (value ?? string.Empty)
			.Replace("&", "&amp;")
			.Replace("<", "&lt;")
			.Replace(">", "&gt;");
	}

	private static string ResolveBelonging(Actor actor)
	{
		try
		{
			if (XjSectRepository.TryGetByActor(actor, out XuanJianVNext.Data.Sect.XjSectArchiveRecord sect)
				&& !string.IsNullOrWhiteSpace(sect?.Name))
			{
				return XjUiSafeText.Player(sect.Name.Trim(), "无属");
			}
			if (string.Equals(actor?.asset?.id, "longshu", System.StringComparison.Ordinal))
			{
				return "龙属";
			}
			string kingdomName = actor?.kingdom?.data?.name;
			if (!string.IsNullOrWhiteSpace(kingdomName))
			{
				string trimmed = kingdomName.Trim();
				if (string.Equals(trimmed, "dragons", System.StringComparison.OrdinalIgnoreCase))
				{
					return "龙属";
				}
				if (trimmed.StartsWith("nomads_", System.StringComparison.OrdinalIgnoreCase))
				{
					return "无国属";
				}
				return XjUiSafeText.Player(trimmed, "无属");
			}
		}
		catch (System.Exception exception)
		{
			XjExceptionDiagnostics.Report("XjRankCardView.ResolveBelonging", exception);
		}
		return "无属";
	}

	private static Color RankColor(int rank)
	{
		if (rank == 0) return new Color(1f, 0.84f, 0f);
		if (rank == 1) return new Color(0.75f, 0.75f, 0.75f);
		if (rank == 2) return new Color(0.8f, 0.5f, 0.2f);
		return Color.white;
	}

}
