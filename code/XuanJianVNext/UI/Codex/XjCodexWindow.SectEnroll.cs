using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Broadcast;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.UI.ActorInfo;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private const string PlayerEnrollReason = "PlayerEnrollFromActorPanel";

	internal static void ShowSectEnrollment(long actorId)
	{
		if (actorId <= 0L) return;
		Show(1);
		if (_instance == null) return;
		_instance._sectEnrollActorId = actorId;
		_instance._sectEnrollSectId = 0L;
		_instance._focusMessage = string.Empty;
		_instance._scrollPosition = Vector2.zero;
		XjCodexSnapshotPublisher.RequestRefresh();
	}

	private void DrawSectEnrollmentWizard(XjCodexSnapshot snapshot)
	{
		DrawPageHeader("收入宗门", "当前角色身份已锁定；依次选择宗门与山峰，不依赖地图选择状态。");
		if (!XjScheduler.ResolveActor(_sectEnrollActorId, out Actor actor) || actor?.data == null || !actor.isAlive())
		{
			DrawEmptyCard("该角色已失效，无法收入宗门。", "#FF8877");
			if (GUILayout.Button("返回宗门谱系", GUILayout.Width(150f), GUILayout.Height(34f))) CancelSectEnrollment();
			return;
		}

		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		bool eligible = XjCultivationEligibility.HasCultivationAptitudeTrait(actor)
			&& !XjLongShuSystem.IsLongShu(actor)
			&& !XjYinSiTraitLifecycle.IsYinSi(actor);
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#6FAE9D");
		DrawOrnamentDivider("#6FAE9D", "玄 鉴 收 录");
		GUILayout.BeginHorizontal();
		DrawMiniStat("角色", SafeActorName(actor), "#9CD7FF", GUILayout.Width(220f));
		DrawMiniStat("当前归属", identity.Found ? identity.ZongMenName + "·" + Empty(identity.PeakName, "主峰") : "未入宗门", identity.Found ? "#FFD37A" : "#888888", GUILayout.Width(320f));
		DrawMiniStat("判定", eligible ? "可以收入" : "不符合入宗条件", eligible ? "#A7E08A" : "#FF8877", GUILayout.Width(180f));
		GUILayout.FlexibleSpace();
		if (GUILayout.Button("返回宗门谱系", GUILayout.Width(125f), GUILayout.Height(32f))) CancelSectEnrollment();
		GUILayout.EndHorizontal();
		GUILayout.EndVertical();
		if (!eligible)
		{
			DrawEmptyCard("只有具备修炼资质、非龙属且非阴司化身的存活角色可以收入宗门。", "#FFAA66");
			return;
		}
		if (snapshot?.Sects == null || snapshot.Sects.Count == 0)
		{
			DrawEmptyCard("天下尚无宗门可选。", "#777777");
			return;
		}

		if (_sectEnrollSectId <= 0L)
		{
			GUILayout.Space(8f);
			DrawSectionTitle("第一步：选择宗门", "#FFD37A");
			for (int i = 0; i < snapshot.Sects.Count; i += 3)
			{
				GUILayout.BeginHorizontal();
				for (int j = 0; j < 3 && i + j < snapshot.Sects.Count; j++)
				{
					XjCodexSectItem sect = snapshot.Sects[i + j];
					if (sect == null) continue;
					string sectColor = sect.JinDanCount > 0 ? "#FFD37A" : sect.ZiFuCount > 0 ? "#B7A7FF" : "#9CD7FF";
					GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(450f), GUILayout.Height(116f));
					DrawCardStripe(sectColor);
					GUILayout.Label("<b><size=20><color=" + sectColor + ">◇ " + Rich(sect.Name) + "</color></size></b>");
					GUILayout.Label("山门 " + Rich(ClampInlineText(Empty(sect.CapitalCityName, "未定"), 24)) + "　·　诸峰 " + sect.PeakCount + "　·　修士 " + sect.CultivatorCount);
					GUILayout.Label("<color=grey>宗主 " + Rich(ClampInlineText(Empty(sect.SovereignName, "待继任"), 24)) + "　·　紫府 " + sect.ZiFuCount + "　·　真君 " + sect.JinDanCount + "</color>");
					Color old = GUI.backgroundColor;
					GUI.backgroundColor = new Color(0.35f, 0.31f, 0.18f);
					if (GUILayout.Button("◇ 选择此宗 ◇", GUILayout.Height(29f))) _sectEnrollSectId = sect.SectId;
					GUI.backgroundColor = old;
					GUILayout.EndVertical();
				}
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
				GUILayout.Space(6f);
			}
			return;
		}

		XjCodexSectItem selectedSect = FindSectById(snapshot.Sects, _sectEnrollSectId);
		if (selectedSect == null)
		{
			_sectEnrollSectId = 0L;
			DrawEmptyCard("所选宗门已经失效，请重新选择。", "#FFAA66");
			return;
		}

		GUILayout.Space(8f);
		DrawSectionTitle("第二步：选择山峰 · " + selectedSect.Name, "#B7A7FF");
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("重新选择宗门", GUILayout.Width(145f), GUILayout.Height(32f)))
		{
			_sectEnrollSectId = 0L;
			return;
		}
		GUILayout.Label("选择后将通过唯一成员写入口完成入宗或转宗。", GUILayout.ExpandWidth(true));
		GUILayout.EndHorizontal();

		if (selectedSect.Peaks == null || selectedSect.Peaks.Count == 0)
		{
			DrawEnrollPeakButton(actor, selectedSect, 0, "主峰");
			return;
		}
		for (int i = 0; i < selectedSect.Peaks.Count; i += 4)
		{
			GUILayout.BeginHorizontal();
			for (int j = 0; j < 4 && i + j < selectedSect.Peaks.Count; j++)
			{
				XjCodexSectPeakItem peak = selectedSect.Peaks[i + j];
				if (peak == null) continue;
				DrawEnrollPeakButton(actor, selectedSect, peak.PeakId, Empty(peak.PeakName, peak.PeakId == 0 ? "主峰" : "未名峰"));
			}
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			GUILayout.Space(6f);
		}
	}

	private void DrawEnrollPeakButton(Actor actor, XjCodexSectItem sect, int peakId, string peakName)
	{
		XjCodexSectPeakItem peak = FindPeakById(sect.Peaks, peakId);
		string peakColor = peak != null && peak.JinDanCount > 0 ? "#FFD37A" : peak != null && peak.ZiFuCount > 0 ? "#B7A7FF" : "#A7E08A";
		GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330f), GUILayout.Height(104f));
		DrawCardStripe(peakColor);
		GUILayout.Label("<b><color=" + peakColor + ">◇ " + Rich(ClampInlineText(peakName, 20)) + "</color></b>");
		GUILayout.Label(peak == null ? "主峰席位" : "峰主 " + Rich(ClampInlineText(Empty(peak.PeakMasterName, "未任命"), 16)) + "　·　成员 " + peak.MemberCount);
		GUILayout.Label("<color=grey>收入后立即写入宗门、峰位与角色镜像。</color>");
		if (GUILayout.Button("收入此峰", GUILayout.Height(28f)))
		{
			bool ok = TryEnrollActorIntoSect(actor, sect, peakId, out string message);
			_focusMessage = ok ? string.Empty : "<color=#FF8877>" + message + "</color>";
			if (ok)
			{
				_selectedSectId = sect.SectId;
				_sectEnrollActorId = 0L;
				_sectEnrollSectId = 0L;
				XjActorInfoPanelRenderer.Invalidate();
				XjCodexSnapshotPublisher.RequestRefresh();
			}
		}
		GUILayout.EndVertical();
	}

	private void CancelSectEnrollment()
	{
		_sectEnrollActorId = 0L;
		_sectEnrollSectId = 0L;
		_scrollPosition = Vector2.zero;
	}

	private bool TryEnrollActorIntoSect(Actor actor, XjCodexSectItem sect, int peakId, out string message)
	{
		message = "收入宗门失败。";
		if (actor?.data == null || !actor.isAlive()) { message = "该角色已经失效。"; return false; }
		if (sect == null || sect.SectId <= 0L) { message = "宗门记录无效。"; return false; }
		if (!XjCultivationEligibility.HasCultivationAptitudeTrait(actor) || XjLongShuSystem.IsLongShu(actor) || XjYinSiTraitLifecycle.IsYinSi(actor))
		{ message = "该角色不符合入宗条件。"; return false; }
		if (!XjZongMenCityData.TryResolveZongMenCity(sect.SectId, out City city) || city?.data == null)
		{ message = "未找到该宗门山门。"; return false; }

		int targetPeakId = ResolveEnrollPeakId(city, peakId);
		string actorName = SafeActorName(actor);
		string sectName = string.IsNullOrWhiteSpace(sect.Name) ? XjZongMenCityData.GetZongMenName(city) : sect.Name.Trim();
		string peakName = XjZongMenCityData.GetPeakName(city, targetPeakId);
		int currentYear = XjZongMenCityData.GetCurrentYearOrZero();
		XjZongMenIdentitySnapshot identity = XjZongMenAccessor.BuildIdentity(actor);
		if (identity.Found && identity.ZongMenId == sect.SectId && identity.PeakId == targetPeakId)
		{
			// 重复点击视为幂等成功：关闭收入流程并刷新照录，不留下常驻错误提示。
			XjZongMenCultivatorCityIndex.Observe(actor);
			XjActorInfoPanelRenderer.Invalidate();
			XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.World | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family);
			message = actorName + "已在" + sectName + "·" + peakName + "。";
			return true;
		}

		bool changed = XjZongMenMembershipWriter.AssignDisciple(city, targetPeakId, actor, currentYear, PlayerEnrollReason);
		if (!changed && !XjZongMenCityData.IsMember(city, actor))
		{ message = "收入失败：该角色不符合入宗条件。"; return false; }

		XjZongMenCultivatorCityIndex.Observe(actor);
		XjActorInfoPanelRenderer.Invalidate();
		XjCodexSnapshotPublisher.MarkDirty(XjCodexDirtyFlags.World | XjCodexDirtyFlags.Sect | XjCodexDirtyFlags.Family);
		XjZongMenIdentitySnapshot assigned = XjZongMenAccessor.BuildIdentity(actor);
		if (!assigned.Found || assigned.ZongMenId != sect.SectId)
		{
			message = "收入失败：宗门身份未能同步。";
			return false;
		}
		string assignedPeakName = string.IsNullOrWhiteSpace(assigned.PeakName) ? peakName : assigned.PeakName.Trim();
		message = actorName + "已收入" + sectName + "·" + assignedPeakName + "。";
		TryShowSectEnrollTip(message);
		return true;
	}

	private static int ResolveEnrollPeakId(City city, int requestedPeakId)
	{
		if (city?.data == null || requestedPeakId == int.MinValue) return XjZongMenCityData.MainPeakId;
		return XjZongMenCityData.ReadPeakIds(city).Contains(requestedPeakId) ? requestedPeakId : XjZongMenCityData.MainPeakId;
	}

	private static string SafeActorName(Actor actor)
	{
		string name = actor?.getName();
		return string.IsNullOrWhiteSpace(name) ? "未名角色" : name.Trim();
	}

	private static void TryShowSectEnrollTip(string message)
	{
		try { XjBroadcastSystem.ShowWorldTip(message, false, "top", 5f, "#FFD37A"); }
		catch { }
	}
}
