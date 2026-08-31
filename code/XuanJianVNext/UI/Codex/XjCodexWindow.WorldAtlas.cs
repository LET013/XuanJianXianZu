using System;
using System.Collections.Generic;
using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.XianGuo;
using XuanJianVNext.UI.Map;

using XuanJianVNext.Systems.History;
namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private static readonly string[] AtlasViews = { "宗门疆域", "世家封邑", "仙国法统", "大阵洞天" };
	private static readonly Color32[] FamilyAtlasColors =
	{
		new Color32(122, 210, 168, 255), new Color32(115, 184, 220, 255),
		new Color32(218, 181, 111, 255), new Color32(177, 143, 218, 255),
		new Color32(215, 128, 144, 255), new Color32(139, 202, 208, 255),
		new Color32(197, 207, 118, 255), new Color32(198, 155, 113, 255)
	};

	private sealed class AtlasBounds
	{
		internal float MinX = float.MaxValue;
		internal float MinY = float.MaxValue;
		internal float MaxX = float.MinValue;
		internal float MaxY = float.MinValue;
		internal int Count;
	}

	private int _atlasLayoutCacheVersion = -1;
	private readonly Dictionary<long, AtlasBounds> _atlasSectBoundsCache = new Dictionary<long, AtlasBounds>();
	private readonly Dictionary<long, AtlasBounds> _atlasFamilyBoundsCache = new Dictionary<long, AtlasBounds>();

	private void DrawWorldAtlasPage(XjCodexSnapshot snapshot)
	{
		DrawPageHeader("天下舆图", "以当世山河地势为底图，叠录宗门、世家、仙朝、大阵与洞天；舆图只辨疆域与所在，详细制度各归其卷。");
		DrawAtlasViewTabs();
		DrawAtlasOverview(snapshot);
		if (string.Equals(_atlasView, "仙国法统", StringComparison.Ordinal))
		{
			DrawXianGuoAtlasOverview();
		}
		EnsureAtlasSelection(snapshot);

		if (snapshot.MapWidth <= 0 || snapshot.MapHeight <= 0 || CountPositionedCities(snapshot.Cities) == 0)
		{
			DrawEmptyCard("当前世界尚未提供可用的山河坐标。城镇卷宗仍可正常查看。", "#777777");
			return;
		}

		bool stacked = ContentWidth < 1120f;
		if (stacked)
		{
			DrawAtlasCanvas(snapshot, Mathf.Clamp(_windowRect.height - 570f, 330f, 520f));
			GUILayout.Space(8f);
			DrawAtlasDetail(snapshot);
		}
		else
		{
			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical(GUILayout.ExpandWidth(true));
			DrawAtlasCanvas(snapshot, Mathf.Clamp(_windowRect.height - 475f, 430f, 680f));
			GUILayout.EndVertical();
			GUILayout.Space(8f);
			GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(345f));
			DrawAtlasDetail(snapshot);
			GUILayout.EndVertical();
			GUILayout.EndHorizontal();
		}
	}

	private void DrawAtlasViewTabs()
	{
		GUILayout.BeginVertical(GUI.skin.box);
		DrawCardStripe("#6FAE9D");
		int columns = ContentWidth < 820f ? 2 : AtlasViews.Length;
		for (int i = 0; i < AtlasViews.Length; i++)
		{
			if (i % columns == 0) GUILayout.BeginHorizontal();
			string view = AtlasViews[i];
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = string.Equals(_atlasView, view, StringComparison.Ordinal)
				? new Color(0.32f, 0.48f, 0.44f, 1f)
				: new Color(0.23f, 0.23f, 0.23f, 1f);
			if (GUILayout.Button(view, GUILayout.Height(38f)))
			{
				_atlasView = view;
				_atlasSelectedSecretRealmSectId = 0L;
				_atlasSelectedAdventureRecordId = string.Empty;
			}
			GUI.backgroundColor = old;
			if (i % columns == columns - 1 || i == AtlasViews.Length - 1)
			{
				GUILayout.FlexibleSpace();
				GUILayout.EndHorizontal();
			}
		}
		GUILayout.Label("<color=grey>切换卷层只改变宗门、世家、仙朝、洞天与异动标记；仙朝详细法统另入独立卷宗。</color>");
		GUILayout.EndVertical();
	}

	private void DrawAtlasOverview(XjCodexSnapshot snapshot)
	{
		int positioned = CountPositionedCities(snapshot.Cities);
		int governed = 0;
		int protectedCities = 0;
		for (int i = 0; i < snapshot.Cities.Count; i++)
		{
			XjCodexCityItem city = snapshot.Cities[i];
			if (city == null) continue;
			if (city.GoverningFamilyId > 0L) governed++;
			if (city.FormationProtected || city.FormationGrade > 0) protectedCities++;
		}
		GUILayout.BeginHorizontal();
		DrawOverviewPill("可绘城镇", "已定位" + positioned + " · 总" + snapshot.Cities.Count, "#9CD7FF", GUILayout.Width(155f));
		DrawOverviewPill("宗门疆域", snapshot.SectCount.ToString(), "#FFD37A", GUILayout.Width(125f));
		DrawOverviewPill("有主封邑", governed.ToString(), "#A7E08A", GUILayout.Width(125f));
		DrawOverviewPill("大阵护城", protectedCities.ToString(), "#B7A7FF", GUILayout.Width(125f));
		DrawOverviewPill("仙国法统", XjXianGuoSystem.ActiveDynastyCount.ToString(), "#F0CC75", GUILayout.Width(125f));
		DrawOverviewPill("显世洞天", (snapshot.SecretRealms.Count + snapshot.AdventureRealms.Count).ToString(), "#CFC7B2", GUILayout.Width(125f));
		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
		GUILayout.Space(6f);
	}

	private void EnsureAtlasSelection(XjCodexSnapshot snapshot)
	{
		if (FindCityById(snapshot?.Cities, _atlasSelectedCityId) != null) return;
		XjCodexCityItem first = null;
		if (snapshot?.Cities != null)
		{
			for (int i = 0; i < snapshot.Cities.Count; i++)
			{
				XjCodexCityItem city = snapshot.Cities[i];
				if (city == null || city.TileX < 0 || city.TileY < 0) continue;
				first ??= city;
				if (city.IsSectCapital)
				{
					first = city;
					break;
				}
			}
		}
		_atlasSelectedCityId = first?.CityId ?? 0L;
	}

	private void DrawAtlasCanvas(XjCodexSnapshot snapshot, float height)
	{
		Rect rect = GUILayoutUtility.GetRect(320f, height, GUILayout.ExpandWidth(true));
		Color oldColor = GUI.color;
		Color oldBackground = GUI.backgroundColor;
		Texture2D mapTexture = World.world?.world_layer?.texture;
		if (mapTexture != null)
		{
			GUI.color = Color.white;
			GUI.DrawTexture(rect, mapTexture, ScaleMode.StretchToFill, false);
			GUI.color = new Color(0.04f, 0.06f, 0.07f, 0.18f);
			GUI.DrawTexture(rect, _whiteTexture);
		}
		else
		{
			GUI.color = new Color(0.08f, 0.11f, 0.13f, 0.98f);
			GUI.DrawTexture(rect, _whiteTexture);
		}
		GUI.color = oldColor;
		if (string.Equals(_atlasView, "宗门疆域", StringComparison.Ordinal))
		{
			DrawAtlasTerritoryPatches(snapshot, rect, true);
		}
		else if (string.Equals(_atlasView, "世家封邑", StringComparison.Ordinal))
		{
			DrawAtlasTerritoryPatches(snapshot, rect, false);
		}

		XjCodexCityItem hovered = null;
		for (int i = 0; i < snapshot.Cities.Count; i++)
		{
			XjCodexCityItem city = snapshot.Cities[i];
			if (!TryResolveAtlasPoint(snapshot, rect, city?.TileX ?? -1, city?.TileY ?? -1, out Vector2 point)) continue;
			float size = city.IsSectCapital ? 18f : 13f;
			if (city.CityId == _atlasSelectedCityId) size += 6f;
			Rect node = new Rect(point.x - size * 0.5f, point.y - size * 0.5f, size, size);
			Color nodeColor = ResolveAtlasCityColor(city);
			GUI.backgroundColor = nodeColor;
			string marker = string.Equals(_atlasView, "大阵洞天", StringComparison.Ordinal)
				? city.FormationGrade > 0 ? "阵" : "·"
				: city.IsSectCapital ? "◆" : "●";
			if (GUI.Button(node, new GUIContent(marker, BuildAtlasCityTooltip(city))))
			{
				_atlasSelectedCityId = city.CityId;
				_atlasSelectedSecretRealmSectId = 0L;
				_atlasSelectedAdventureRecordId = string.Empty;
			}
			if (node.Contains(Event.current.mousePosition)) hovered = city;
			if (city.IsSectCapital || city.CityId == _atlasSelectedCityId || city.HasLuoXiaShan)
			{
				GUI.color = new Color(0.93f, 0.93f, 0.88f, 0.96f);
				GUI.Label(new Rect(node.xMax + 3f, node.y - 4f, 145f, 24f),
					XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(city.Name, "未名城镇")
					+ (city.HasLuoXiaShan ? " · 落霞山" : string.Empty));
				GUI.color = oldColor;
			}
		}

		if (string.Equals(_atlasView, "大阵洞天", StringComparison.Ordinal))
		{
			DrawAtlasSecretRealmMarkers(snapshot, rect);
			DrawAtlasAdventureMarkers(snapshot, rect);
		}
		GUI.backgroundColor = oldBackground;
		GUI.color = oldColor;
		DrawAtlasBorder(rect);
		DrawAtlasLegend(rect, hovered);
	}

	private static void DrawAtlasBorder(Rect rect)
	{
		Color old = GUI.color;
		GUI.color = new Color(0.72f, 0.76f, 0.82f, 0.74f);
		GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 2f), _whiteTexture);
		GUI.DrawTexture(new Rect(rect.x, rect.yMax - 2f, rect.width, 2f), _whiteTexture);
		GUI.DrawTexture(new Rect(rect.x, rect.y, 2f, rect.height), _whiteTexture);
		GUI.DrawTexture(new Rect(rect.xMax - 2f, rect.y, 2f, rect.height), _whiteTexture);
		GUI.color = old;
	}

	private void DrawAtlasTerritoryPatches(XjCodexSnapshot snapshot, Rect rect, bool bySect)
	{
		EnsureAtlasBoundsCache(snapshot);
		IReadOnlyDictionary<long, AtlasBounds> boundsByOwner = bySect
			? _atlasSectBoundsCache
			: _atlasFamilyBoundsCache;

		Color old = GUI.color;
		foreach (KeyValuePair<long, AtlasBounds> pair in boundsByOwner)
		{
			AtlasBounds bounds = pair.Value;
			if (bounds == null || bounds.Count == 0) continue;
			float pad = bounds.Count > 1 ? 22f : 13f;
			float usableWidth = Mathf.Max(1f, rect.width - 36f);
			float usableHeight = Mathf.Max(1f, rect.height - 36f);
			float minX = rect.x + 18f + bounds.MinX * usableWidth;
			float minY = rect.y + 18f + bounds.MinY * usableHeight;
			float maxX = rect.x + 18f + bounds.MaxX * usableWidth;
			float maxY = rect.y + 18f + bounds.MaxY * usableHeight;
			Rect patch = new Rect(
				minX - pad,
				minY - pad,
				Mathf.Max(pad * 2f, maxX - minX + pad * 2f),
				Mathf.Max(pad * 2f, maxY - minY + pad * 2f));
			Color color = bySect ? XjSectMapLayerSystem.ResolveDisplayColor(pair.Key) : ResolveFamilyAtlasColor(pair.Key);
			GUI.color = new Color(color.r, color.g, color.b, 1f);
			GUI.DrawTexture(patch, _whiteTexture);
		}
		GUI.color = old;
	}

	private void EnsureAtlasBoundsCache(XjCodexSnapshot snapshot)
	{
		if (snapshot != null && _atlasLayoutCacheVersion == snapshot.Version) return;
		_atlasSectBoundsCache.Clear();
		_atlasFamilyBoundsCache.Clear();
		_atlasLayoutCacheVersion = snapshot?.Version ?? -1;
		if (snapshot?.Cities == null) return;
		for (int i = 0; i < snapshot.Cities.Count; i++)
		{
			XjCodexCityItem city = snapshot.Cities[i];
			if (city == null || !TryResolveNormalizedAtlasPoint(snapshot, city.TileX, city.TileY, out Vector2 point)) continue;
			AddAtlasBound(_atlasSectBoundsCache, city.SectId, point);
			AddAtlasBound(_atlasFamilyBoundsCache, city.GoverningFamilyId, point);
		}
	}

	private static void AddAtlasBound(Dictionary<long, AtlasBounds> boundsByOwner, long ownerId, Vector2 point)
	{
		if (boundsByOwner == null || ownerId <= 0L) return;
		if (!boundsByOwner.TryGetValue(ownerId, out AtlasBounds bounds))
		{
			bounds = new AtlasBounds();
			boundsByOwner[ownerId] = bounds;
		}
		bounds.MinX = Mathf.Min(bounds.MinX, point.x);
		bounds.MinY = Mathf.Min(bounds.MinY, point.y);
		bounds.MaxX = Mathf.Max(bounds.MaxX, point.x);
		bounds.MaxY = Mathf.Max(bounds.MaxY, point.y);
		bounds.Count++;
	}

	private void DrawAtlasAdventureMarkers(XjCodexSnapshot snapshot, Rect rect)
	{
		for (int i = 0; i < snapshot.AdventureRealms.Count; i++)
		{
			XjCodexAdventureRealmItem realm = snapshot.AdventureRealms[i];
			if (realm == null || !TryResolveAtlasPoint(snapshot, rect, realm.AnchorTileX, realm.AnchorTileY, out Vector2 point)) continue;
			Rect node = new Rect(point.x - 12f, point.y - 12f, 24f, 24f);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = realm.IsOpen ? new Color(0.70f, 0.55f, 0.90f, 1f) : new Color(0.44f, 0.42f, 0.50f, 1f);
			if (GUI.Button(node, new GUIContent("洞",
				XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(realm.Name, "未名洞天")
				+ " · " + TranslateAdventureState(realm.State))))
			{
				_atlasSelectedAdventureRecordId = realm.RecordId;
				_atlasSelectedSecretRealmSectId = 0L;
				_atlasSelectedCityId = realm.AnchorCityId;
			}
			GUI.backgroundColor = old;
		}
	}

	private void DrawAtlasSecretRealmMarkers(XjCodexSnapshot snapshot, Rect rect)
	{
		for (int i = 0; i < snapshot.SecretRealms.Count; i++)
		{
			XjCodexSecretRealmItem realm = snapshot.SecretRealms[i];
			XjCodexCityItem city = FindCityById(snapshot.Cities, realm?.EntranceCityId ?? 0L);
			if (realm == null || city == null
				|| !TryResolveAtlasPoint(snapshot, rect, city.TileX, city.TileY, out Vector2 point)) continue;
			Rect node = new Rect(point.x + 5f, point.y - 17f, 24f, 24f);
			Color old = GUI.backgroundColor;
			GUI.backgroundColor = new Color(0.86f, 0.70f, 0.32f, 1f);
			if (GUI.Button(node, new GUIContent("境",
				XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(
					Empty(realm.DisplayName, realm.SectName + "秘境"), "未名秘境")
				+ " · " + TranslateSecretRealmStage(realm.Stage))))
			{
				_atlasSelectedSecretRealmSectId = realm.SectId;
				_atlasSelectedAdventureRecordId = string.Empty;
				_atlasSelectedCityId = realm.EntranceCityId;
			}
			GUI.backgroundColor = old;
		}
	}

	private static bool TryResolveAtlasPoint(XjCodexSnapshot snapshot, Rect rect, int tileX, int tileY, out Vector2 point)
	{
		point = default;
		if (!TryResolveNormalizedAtlasPoint(snapshot, tileX, tileY, out Vector2 normalized)) return false;
		point = new Vector2(
			rect.x + 18f + normalized.x * Mathf.Max(1f, rect.width - 36f),
			rect.y + 18f + normalized.y * Mathf.Max(1f, rect.height - 36f));
		return true;
	}

	private static bool TryResolveNormalizedAtlasPoint(XjCodexSnapshot snapshot, int tileX, int tileY, out Vector2 point)
	{
		point = default;
		if (snapshot == null || snapshot.MapWidth <= 0 || snapshot.MapHeight <= 0 || tileX < 0 || tileY < 0) return false;
		float nx = Mathf.Clamp01((tileX + 0.5f) / snapshot.MapWidth);
		float ny = Mathf.Clamp01((tileY + 0.5f) / snapshot.MapHeight);
		point = new Vector2(nx, 1f - ny);
		return true;
	}

	private Color ResolveAtlasCityColor(XjCodexCityItem city)
	{
		if (city == null) return new Color(0.42f, 0.42f, 0.42f, 1f);
		if (string.Equals(_atlasView, "世家封邑", StringComparison.Ordinal))
		{
			return city.GoverningFamilyId > 0L ? ResolveFamilyAtlasColor(city.GoverningFamilyId) : new Color(0.38f, 0.38f, 0.38f, 1f);
		}
		if (string.Equals(_atlasView, "仙国法统", StringComparison.Ordinal))
		{
			return city.KingdomId > 0L && XjXianGuoSystem.TryGetActiveSummaryByKingdomId(city.KingdomId, out _)
				? new Color(0.88f, 0.69f, 0.28f, 1f)
				: new Color(0.30f, 0.31f, 0.32f, 1f);
		}
		if (string.Equals(_atlasView, "大阵洞天", StringComparison.Ordinal))
		{
			return city.FormationGrade > 0 ? new Color(0.66f, 0.50f, 0.88f, 1f) : new Color(0.30f, 0.34f, 0.36f, 1f);
		}
		return city.SectId > 0L ? XjSectMapLayerSystem.ResolveDisplayColor(city.SectId) : new Color(0.38f, 0.38f, 0.38f, 1f);
	}

	private static Color ResolveFamilyAtlasColor(long familyId)
	{
		ulong stable = (ulong)(familyId < 0L ? -familyId : familyId);
		return FamilyAtlasColors[(int)(stable % (ulong)FamilyAtlasColors.Length)];
	}

	private void DrawAtlasLegend(Rect rect, XjCodexCityItem hovered)
	{
		Color old = GUI.color;
		float legendWidth = Mathf.Clamp(rect.width - 16f, 180f, hovered != null ? 560f : 500f);
		float legendHeight = hovered != null ? 48f : 42f;
		Rect panel = new Rect(rect.x + 8f, rect.y + 8f, legendWidth, legendHeight);
		GUI.color = new Color(0.08f, 0.10f, 0.12f, 0.90f);
		GUI.DrawTexture(panel, _whiteTexture);
		GUI.color = Color.white;
		string text = hovered != null
			? BuildAtlasCityTooltip(hovered)
			: string.Equals(_atlasView, "宗门疆域", StringComparison.Ordinal)
				? "◆ 山门　● 属城　同色为同宗疆域"
				: string.Equals(_atlasView, "世家封邑", StringComparison.Ordinal)
					? "● 同色为同一世家封邑　橙色为治理示警"
					: string.Equals(_atlasView, "仙国法统", StringComparison.Ordinal)
						? "● 金：仙朝疆域　　● 灰：朝外城镇"
						: "阵：大阵护城　洞：奇遇洞天　福地随入口城照录";
		GUIStyle legendStyle = new GUIStyle(GUI.skin.label)
		{
			wordWrap = true,
			clipping = TextClipping.Clip
		};
		GUI.Label(new Rect(panel.x + 10f, panel.y + 7f, Mathf.Max(120f, panel.width - 20f), panel.height - 12f), text, legendStyle);
		GUI.color = old;
	}

	private void DrawAtlasDetail(XjCodexSnapshot snapshot)
	{
		XjCodexSecretRealmItem secretRealm = FindSecretRealmBySectId(snapshot?.SecretRealms, _atlasSelectedSecretRealmSectId);
		if (secretRealm != null)
		{
			DrawAtlasSecretRealmDetail(secretRealm);
			return;
		}
		XjCodexAdventureRealmItem adventure = FindAdventureByRecordId(snapshot?.AdventureRealms, _atlasSelectedAdventureRecordId);
		if (adventure != null)
		{
			DrawAtlasAdventureDetail(adventure);
			return;
		}

		XjCodexCityItem city = FindCityById(snapshot?.Cities, _atlasSelectedCityId);
		if (city == null)
		{
			DrawEmptyCard("从舆图选择一座城镇查看卷宗。", "#777777");
			return;
		}
		if (string.Equals(_atlasView, "仙国法统", StringComparison.Ordinal)
			&& city.KingdomId > 0L
			&& XjXianGuoSystem.TryGetActiveSummaryByKingdomId(city.KingdomId, out XjXianGuoSummary selectedXianGuo))
		{
			DrawXianGuoAtlasDetail(city, in selectedXianGuo);
			return;
		}
		if (string.Equals(_atlasView, "仙国法统", StringComparison.Ordinal))
		{
			DrawEmptyCard("所选城镇当前不属于任何帝明阳仙国法统。下方保留其普通城镇卷宗，便于核对国属与治理关系。", "#777777");
		}
		string accent = city.SectId > 0L ? "#FFD37A" : "#9CD7FF";
		DrawCardStripe(accent);
		GUILayout.Label("<b><size=22>" + Rich(city.Name) + "</size></b>");
		GUILayout.Label("<color=grey>山河坐标 " + city.TileX + "，" + city.TileY + " · " + Rich(Empty(city.KingdomName, "无国属")) + "</color>");
		DrawOrnamentDivider(accent, "城 镇 卷 宗");
		GUILayout.Label("<b>宗门</b>　" + Rich(Empty(city.SectName, "普通国家")));
		GUILayout.Label("<b>治理世家</b>　" + Rich(FormatFamilyDisplayName(city.GoverningFamilyName, city.GoverningFamilyId, "尚未确认")));
		GUILayout.Label("<b>治理状态</b>　" + Rich(TranslateGovernanceState(city.GovernanceState)));
		GUILayout.Label("<b>在城修士</b>　" + city.CultivatorCount);
		if (city.HasLuoXiaShan)
		{
			GUILayout.Label("<color=#E8B7D8><b>落霞山</b>　群霞归山，山门霞影显于本城辖域</color>");
			GUILayout.Label("<color=#CFCFCF>山门传承：历代已收录" + XjHongXiaLuoXiaEvent.TotalDisciples + "名门人；虹霞为主传，戊土霞光旧法为旁传。</color>");
		}
		if (city.KingdomId > 0L && XjXianGuoSystem.TryGetActiveSummaryByKingdomId(city.KingdomId, out XjXianGuoSummary xianGuo))
		{
			GUILayout.Space(4f);
			GUILayout.Label("<color=#F0CC75><b>仙国法</b>　" + Rich(xianGuo.DynastyName)
				+ (xianGuo.CourtFakeJinDanActive ? " · 众玄归一" : " · 法统已立") + "</color>");
			GUILayout.Label("<color=#CFCFCF>国势：" + xianGuo.NationalPotential + "</color>");
			GUILayout.Label("<color=#CFCFCF>国运：" + xianGuo.NationalFortune + "</color>");
			GUILayout.Label("<color=#CFCFCF>城土：" + xianGuo.CityCount + "城 / " + xianGuo.Population + "众</color>");
			if (GUILayout.Button("仙国法统", GUILayout.Width(96f), GUILayout.Height(30f)))
			{
				_selectedXianGuoKingdomId = xianGuo.KingdomId;
				BeginContextNavigation(19, "返回天下舆图");
			}
		}
		if (city.ChallengerFamilyId > 0L)
		{
			GUILayout.Label("<color=#FFAA66><b>争位世家</b>　"
				+ Rich(FormatFamilyDisplayName(city.ChallengerFamilyName, city.ChallengerFamilyId, "未名氏"))
				+ "　·　已持续 " + city.ChallengeConsecutiveYears + " 年</color>");
		}
		if (city.FormationGrade > 0)
		{
			DrawFormationSummaryRow("大阵", city.FormationName, city.FormationGrade,
				city.FormationCurrentDurability, city.FormationMaxDurability,
				city.FormationState, city.FormationLeadName, GUILayout.ExpandWidth(true));
		}
		GUILayout.Space(6f);
		GUILayout.BeginHorizontal();
		DrawCityFocusButton("定位城镇", city.CityId, GUILayout.Width(96f), GUILayout.Height(36f));
		if (GUILayout.Button("城镇档案", GUILayout.Width(96f), GUILayout.Height(36f)))
		{
			BeginContextNavigation(4, "返回天下舆图");
			_cityTargetId = city.CityId;
		}
		GUILayout.EndHorizontal();
		GUILayout.BeginHorizontal();
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && city.SectId > 0L;
		if (GUILayout.Button("宗门档案", GUILayout.Width(96f), GUILayout.Height(36f)))
		{
			BeginContextNavigation(1, "返回天下舆图");
			_selectedSectId = city.SectId;
			SelectSectArchiveView("山门总览");
		}
		GUI.enabled = oldEnabled && city.GoverningFamilyId > 0L;
		if (GUILayout.Button("世家档案", GUILayout.Width(96f), GUILayout.Height(36f)))
		{
			BeginContextNavigation(3, "返回天下舆图");
			_selectedFamilyId = city.GoverningFamilyId;
			SelectFamilyArchiveView("封邑门位");
		}
		GUI.enabled = oldEnabled;
		GUILayout.EndHorizontal();
	}

	private void DrawAtlasSecretRealmDetail(XjCodexSecretRealmItem item)
	{
		string stage = TranslateSecretRealmStage(item.Stage);
		string color = string.Equals(stage, "洞天", StringComparison.Ordinal) ? "#FFD37A" : "#B7A7FF";
		DrawCardStripe(color);
		GUILayout.Label("<b><size=22>" + Rich(Empty(item.DisplayName, item.SectName + "秘境")) + "</size></b>");
		DrawTag(Empty(stage, "营造中"), color);
		GUILayout.Label("<b>所属宗门</b>　" + Rich(Empty(item.SectName, "未载")));
		GUILayout.Label("<b>入口</b>　" + Rich(Empty(item.EntranceCityName, "未稳定")));
		GUILayout.Label("<b>稳定度</b>　" + DescribeLoreStrength(item.Stability));
		GUILayout.Label("<b>玄韬完整</b>　" + DescribeLoreStrength(item.XuanTaoIntegrity));
		GUILayout.Label("<b>坐镇真君</b>　" + Rich(Empty(item.SittingJinDanName, "暂无")));
		GUILayout.Label("<color=grey>" + Rich(Empty(item.Summary, "宗门秘境卷宗尚简。")) + "</color>");
		GUILayout.BeginHorizontal();
		DrawCityFocusButton("定位入口", item.EntranceCityId, GUILayout.Width(96f), GUILayout.Height(36f));
		if (GUILayout.Button("大阵洞天档", GUILayout.Width(115f), GUILayout.Height(36f)))
		{
			BeginContextNavigation(1, "返回天下舆图");
			_selectedSectId = item.SectId;
			SelectSectArchiveView("大阵洞天");
		}
		GUILayout.EndHorizontal();
	}

	private void DrawAtlasAdventureDetail(XjCodexAdventureRealmItem item)
	{
		string color = item.IsOpen ? "#B7A7FF" : "#777777";
		DrawCardStripe(color);
		GUILayout.Label("<b><size=22>" + Rich(item.Name) + "</size></b>");
		DrawTag(TranslateAdventureState(item.State), color);
		GUILayout.Label("<b>道途</b>　" + Rich(Empty(item.DaoTuGroup, "未载")));
		GUILayout.Label("<b>属地</b>　" + Rich(Empty(item.CityName, "无主之地")));
		GUILayout.Label("<b>归属</b>　" + Rich(ResolveAdventureClaimName(item)));
		GUILayout.Label("<b>发现者</b>　" + Rich(Empty(item.DiscovererName, "未载")));
		GUILayout.Label("<b>开放</b>　" + (item.IsOpen ? "至" + XjChronology.FormatYear(item.OpenUntilYear) : "当前沉寂"));
		if (!string.IsNullOrWhiteSpace(item.ContestSummary))
		{
			GUILayout.Label("<color=#FFAA66>" + Rich(item.ContestSummary) + "</color>");
		}
		GUILayout.BeginHorizontal();
		bool canFocus = item.AnchorCityId > 0L || item.AnchorTileX >= 0 && item.AnchorTileY >= 0;
		bool oldEnabled = GUI.enabled;
		GUI.enabled = oldEnabled && canFocus;
		if (GUILayout.Button("定位洞天", GUILayout.Width(96f), GUILayout.Height(36f)))
		{
			_focusMessage = TryFocusMapLayerTarget(item.AnchorCityId, item.AnchorTileX, item.AnchorTileY)
				? "已定位到奇遇洞天。"
				: "该奇遇洞天暂时无法定位。";
		}
		GUI.enabled = oldEnabled;
		if (GUILayout.Button("洞天档案", GUILayout.Width(96f), GUILayout.Height(36f)))
		{
			BeginContextNavigation(9, "返回天下舆图");
			_adventureTargetName = item.Name;
			_adventureTargetCityId = item.AnchorCityId;
		}
		GUILayout.EndHorizontal();
	}

	private static int CountPositionedCities(IReadOnlyList<XjCodexCityItem> cities)
	{
		if (cities == null) return 0;
		int count = 0;
		for (int i = 0; i < cities.Count; i++)
		{
			if (cities[i]?.TileX >= 0 && cities[i].TileY >= 0) count++;
		}
		return count;
	}

	private static XjCodexCityItem FindCityById(IReadOnlyList<XjCodexCityItem> cities, long cityId)
	{
		if (cityId <= 0L) return null;
		if (XjWorldEntityViewModelStore.TryGetCityItem(cityId, out XjCodexCityItem indexed)) return indexed;
		if (cities == null) return null;
		for (int i = 0; i < cities.Count; i++)
		{
			XjCodexCityItem city = cities[i];
			if (city != null && city.CityId == cityId) return city;
		}
		return null;
	}

	private static XjCodexAdventureRealmItem FindAdventureByRecordId(
		IReadOnlyList<XjCodexAdventureRealmItem> items,
		string recordId)
	{
		if (string.IsNullOrWhiteSpace(recordId) || items == null) return null;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexAdventureRealmItem item = items[i];
			if (item != null && string.Equals(item.RecordId, recordId, StringComparison.Ordinal)) return item;
		}
		return null;
	}

	private static XjCodexSecretRealmItem FindSecretRealmBySectId(
		IReadOnlyList<XjCodexSecretRealmItem> items,
		long sectId)
	{
		if (sectId <= 0L || items == null) return null;
		for (int i = 0; i < items.Count; i++)
		{
			XjCodexSecretRealmItem item = items[i];
			if (item != null && item.SectId == sectId) return item;
		}
		return null;
	}

	private static string BuildAtlasCityTooltip(XjCodexCityItem city)
	{
		if (city == null) return string.Empty;
		return XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(city.Name, "未名城镇")
			+ " · " + XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(Empty(city.SectName, "普通国家"), "普通国家")
			+ " · " + XuanJianVNext.Core.XjDisplayNameSanitizer.Clean(
				FormatFamilyDisplayName(city.GoverningFamilyName, city.GoverningFamilyId, "望族未定"), "望族未定")
			+ (city.HasLuoXiaShan ? " · 落霞山" : string.Empty);
	}
}
