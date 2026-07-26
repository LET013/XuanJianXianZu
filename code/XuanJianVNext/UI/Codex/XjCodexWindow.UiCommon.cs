using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Alchemy;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.Craft;
using XuanJianVNext.Data.DongTian;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.History;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Chronicle;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Craft;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.UI.Alchemy;
using XuanJianVNext.UI.Chronicle;
using XuanJianVNext.UI.Common;
using XuanJianVNext.UI.Craft;
using XuanJianVNext.UI.LingWu;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{	
		private void DrawWaitingPage()
		{
			DrawPageHeader("玄鉴尚未照定天下", "玄鉴仙鉴正在等待第一份天下卷宗。");
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.Label("世界继续流转片刻后，玄鉴仙鉴将自动显现当前天下。");
			GUILayout.Space(8f);
			if (GUILayout.Button("重新照览", GUILayout.Width(160f), GUILayout.Height(38f)))
			{
				XjCodexSnapshotPublisher.RefreshNow();
				GUI.changed = true;
			}
			GUILayout.EndVertical();
		}

private void DrawCityFocusButton(string label, long cityId, params GUILayoutOption[] options)
		{
			bool oldEnabled = GUI.enabled;
			GUI.enabled = oldEnabled && cityId > 0L;
			if (GUILayout.Button(label, options))
			{
				_focusMessage = TryFocusMapLayerTarget(cityId, -1, -1) ? "已定位到城镇。" : "该城镇暂时无法定位。";
			}
			GUI.enabled = oldEnabled;
		}

private static void DrawFormationSummaryRow(string prefix, string name, int grade, int current, int max, string state, string leadName, params GUILayoutOption[] labelOptions)
		{
			GUILayout.BeginHorizontal();
			if (!string.IsNullOrWhiteSpace(prefix))
			{
				GUILayout.Label(prefix, GUILayout.Width(prefix.Length >= 5 ? 112f : 70f));
			}
			if (grade > 0)
			{
				DrawFormationIcon(ResolveFormationIconPath(grade));
				string stateIconPath = ResolveFormationStateIconPath(state, current);
				if (!string.IsNullOrWhiteSpace(stateIconPath))
				{
					DrawFormationIcon(stateIconPath);
				}
			}
			GUILayout.Label(FormatFormationSummary(name, grade, current, max, state, leadName), labelOptions);
			GUILayout.EndHorizontal();
		}

private static void DrawFormationIcon(string path)
		{
			Sprite sprite = LoadCachedSprite(path);
			if (sprite?.texture == null)
			{
				GUILayout.Space(26f);
				return;
			}
			GUILayout.Label(sprite.texture, GUILayout.Width(24f), GUILayout.Height(24f));
		}

private static void DrawCenteredResourceIcon(string path, float size)
		{
			GUILayout.BeginHorizontal();
			GUILayout.FlexibleSpace();
			DrawResourceIcon(path, size);
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}

private static void DrawResourceIcon(string path, float size)
		{
			Sprite sprite = LoadCachedSprite(path);
			if (sprite?.texture == null)
			{
				GUILayout.Box("?", GUILayout.Width(size), GUILayout.Height(size));
				return;
			}
			GUILayout.Label(sprite.texture, GUILayout.Width(size), GUILayout.Height(size));
		}

private static Sprite LoadCachedSprite(string path)
		{
			if (string.IsNullOrWhiteSpace(path))
			{
				return null;
			}
			if (_spriteCache.TryGetValue(path, out Sprite cached))
			{
				return cached;
			}
			Sprite sprite = XjIconLoader.TryLoadSpritePath(path)
				?? (!path.StartsWith("GameResources/", StringComparison.OrdinalIgnoreCase)
					? XjIconLoader.TryLoadSpritePath("GameResources/" + path)
					: null);
			_spriteCache[path] = sprite;
			return sprite;
		}

private static string ResolveFormationIconPath(int grade)
		{
			if (grade <= 1) return "GameResources/item/Arts/zhenfa/JiaZuXiaoZhen.png";
			if (grade == 2) return "GameResources/item/Arts/zhenfa/ChengZhenShouYuZhen.png";
			return "GameResources/item/Arts/zhenfa/ZongMenDaZhen.png";
		}

private static string ResolveFormationStateIconPath(string state, int current)
		{
			if (current <= 0 || state == XjSectFormationBuildState.Broken) return "GameResources/item/Arts/zhenfa/DaZhenPoSui.png";
			if (state == XjSectFormationBuildState.Damaged || state == XjSectFormationBuildState.Repairing) return "GameResources/item/Arts/zhenfa/DaZhenSunHuai.png";
			return string.Empty;
		}

private static string ResolveFormationDisplayName(int grade)
		{
			if (grade <= 1) return grade <= 0 ? "未建阵法" : "家族小阵";
			if (grade == 2) return "城镇守御阵";
			return "宗门大阵";
		}

private static int CountFormationProtectedCities(IReadOnlyList<XjCodexCityItem> cities)
		{
			int count = 0;
			for (int i = 0; i < cities.Count; i++) if (cities[i] != null && cities[i].FormationProtected) count++;
			return count;
		}

private static int CountSecretRealmStage(IReadOnlyList<XjCodexSecretRealmItem> realms, string stage)
		{
			int count = 0;
			if (realms == null) return 0;
			for (int i = 0; i < realms.Count; i++)
			{
				if (realms[i] != null && string.Equals(realms[i].Stage, stage, StringComparison.Ordinal)) count++;
			}
			return count;
		}

private static int CountSecretRealmUnderConstruction(IReadOnlyList<XjCodexSecretRealmItem> realms)
		{
			int count = 0;
			if (realms == null) return 0;
			for (int i = 0; i < realms.Count; i++)
			{
				XjCodexSecretRealmItem item = realms[i];
				if (item != null && item.ActiveTaskId > 0L) count++;
			}
			return count;
		}

private static int CountOpenSecretRealms(IReadOnlyList<XjCodexSecretRealmItem> realms)
		{
			int count = 0;
			if (realms == null) return 0;
			for (int i = 0; i < realms.Count; i++)
			{
				if (realms[i] != null && realms[i].EntranceOpen) count++;
			}
			return count;
		}

private static int CountSittingSecretRealms(IReadOnlyList<XjCodexSecretRealmItem> realms)
		{
			int count = 0;
			if (realms == null) return 0;
			for (int i = 0; i < realms.Count; i++)
			{
				if (realms[i] != null && !string.IsNullOrWhiteSpace(realms[i].SittingJinDanName)) count++;
			}
			return count;
		}

private static void DrawPageHeader(string title, string subtitle)
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe("#BAC6D9");
			GUILayout.BeginHorizontal();
			GUILayout.Label("<color=#BAC6D9>◈</color>", GUILayout.Width(24f));
			GUILayout.Label("<b><size=23>" + Rich(title) + "</size></b>");
			GUILayout.FlexibleSpace();
			GUILayout.Label("<color=#BAC6D9>◇ 玄鉴照世 ◇</color>", GUILayout.Width(145f));
			GUILayout.EndHorizontal();
			if (!string.IsNullOrEmpty(subtitle)) GUILayout.Label("<color=#A8A8A8>　" + Rich(subtitle) + "</color>");
			DrawOrnamentDivider("#5C6678", string.Empty);
			GUILayout.EndVertical();
		}

private static void DrawSectionTitle(string title, string color)
		{
			GUILayout.Space(8f);
			DrawOrnamentDivider(color, title);
			GUILayout.Space(4f);
		}

private static void DrawOrnamentDivider(string colorHex, string label)
		{
			GUILayout.BeginHorizontal();
			Rect left = GUILayoutUtility.GetRect(40f, 2f, GUILayout.ExpandWidth(true));
			DrawSolidRect(left, ParseHexColor(colorHex, Color.gray));
			if (!string.IsNullOrWhiteSpace(label))
			{
				GUILayout.Label("<b><color=" + colorHex + ">◇ " + Rich(label) + " ◇</color></b>", GUILayout.ExpandWidth(false));
			}
			Rect right = GUILayoutUtility.GetRect(40f, 2f, GUILayout.ExpandWidth(true));
			DrawSolidRect(right, ParseHexColor(colorHex, Color.gray));
			GUILayout.EndHorizontal();
		}

private static void DrawMetricBar(string label, int value, int maxValue, string colorHex, string note = "")
		{
			int safeMax = Math.Max(1, maxValue);
			GUILayout.BeginVertical(GUI.skin.box);
			GUILayout.BeginHorizontal();
			GUILayout.Label("<b>" + Rich(label) + "</b>", GUILayout.Width(180f));
			GUILayout.Label("<color=" + colorHex + "><b>" + Math.Max(0, value) + "</b></color>", GUILayout.Width(70f));
			if (!string.IsNullOrWhiteSpace(note)) GUILayout.Label("<color=grey>" + Rich(note) + "</color>");
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
			DrawInlineBar((float)Math.Max(0, value) / safeMax, colorHex, GUILayout.Height(13f), GUILayout.ExpandWidth(true));
			GUILayout.EndVertical();
		}

private static void DrawOverviewPill(string label, string value, string color, params GUILayoutOption[] options)
		{
			GUILayout.BeginVertical(GUI.skin.box, options);
			DrawCardStripe(color);
			GUILayout.Label("<color=grey>" + Rich(label) + "</color>");
			GUILayout.Label("<b><color=" + color + ">" + Rich(value) + "</color></b>");
			GUILayout.EndVertical();
		}

private static void DrawMiniStat(string label, string value, string color, params GUILayoutOption[] options)
		{
			GUILayout.BeginVertical(GUI.skin.box, options);
			DrawCardStripe(color);
			GUILayout.Label("<color=grey>" + Rich(label) + "</color>");
			GUILayout.Label("<b><color=" + color + ">" + Rich(value) + "</color></b>");
			GUILayout.EndVertical();
		}

private static void DrawListLimitNotice(int total, int shown)
		{
			if (total <= shown) return;
			GUILayout.Space(8f);
			GUILayout.Label("<color=grey>本页先列前 "
				+ shown + "-" + total + " 条记录；可切换筛选查看其余条目。</color>");
		}

private static void DrawEmptyCard(string text, string color)
		{
			GUILayout.BeginVertical(GUI.skin.box);
			DrawCardStripe(color);
			GUILayout.Space(2f);
			GUILayout.Label("<color=" + color + ">" + Rich(text) + "</color>");
			GUILayout.Space(2f);
			GUILayout.EndVertical();
		}

private static string ClampInlineText(string value, int maxChars = 96)
		{
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;
			string safe = value.Trim().Replace("\r", string.Empty).Replace("\n", " ");
			return safe.Length <= maxChars ? safe : safe.Substring(0, Math.Max(0, maxChars - 1)) + "…";
		}

private static void DrawCardStripe(string colorHex)
		{
			Rect rect = GUILayoutUtility.GetRect(100f, 6f, GUILayout.ExpandWidth(true));
			DrawSolidRect(rect, ParseHexColor(colorHex, Color.gray));
		}

private static void DrawInlineBar(float normalized, string colorHex, params GUILayoutOption[] options)
		{
			Rect rect = GUILayoutUtility.GetRect(100f, 18f, options);
			DrawSolidRect(rect, new Color(0.08f, 0.075f, 0.065f, 0.95f));
			Color fill = ParseHexColor(colorHex, Color.white);
			Rect inner = new Rect(rect.x + 2f, rect.y + 2f, Mathf.Max(0f, (rect.width - 4f) * Mathf.Clamp01(normalized)), Mathf.Max(0f, rect.height - 4f));
			DrawSolidRect(inner, fill);
		}

private static void DrawSolidRect(Rect rect, Color color)
		{
			if (_whiteTexture == null) return;
			Color old = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(rect, _whiteTexture);
			GUI.color = old;
		}

private static void EnsureTexturesAndStyles()
		{
			if (_whiteTexture == null)
			{
				_whiteTexture = new Texture2D(1, 1);
				_whiteTexture.SetPixel(0, 0, Color.white);
				_whiteTexture.Apply();
			}
			if (_largeLabel == null)
			{
				_largeLabel = new GUIStyle(GUI.skin.label)
				{
					fontSize = 18, richText = true, wordWrap = true, clipping = TextClipping.Clip, padding = new RectOffset(2, 2, 2, 2)
				};
				_largeButton = new GUIStyle(GUI.skin.button)
				{
					fontSize = 17, richText = true, wordWrap = true, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(10, 10, 4, 4), margin = new RectOffset(4, 4, 4, 4), clipping = TextClipping.Clip
				};
				_largeWindow = new GUIStyle(GUI.skin.window)
				{
					fontSize = 24, padding = new RectOffset(14, 14, 28, 14)
				};
				_largeBox = new GUIStyle(GUI.skin.box)
				{
					fontSize = 17, richText = true, wordWrap = true, stretchWidth = true, stretchHeight = false, alignment = TextAnchor.UpperLeft, clipping = TextClipping.Clip, padding = new RectOffset(12, 12, 10, 10), margin = new RectOffset(6, 6, 6, 6)
				};
				_largeTextField = new GUIStyle(GUI.skin.textField)
				{
					fontSize = 17, richText = true, wordWrap = false, clipping = TextClipping.Clip, padding = new RectOffset(8, 8, 7, 7), margin = new RectOffset(4, 4, 4, 4)
				};
				_largeTextArea = new GUIStyle(GUI.skin.textArea)
				{
					fontSize = 17, richText = true, wordWrap = true, clipping = TextClipping.Clip, padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset(4, 4, 4, 4)
				};
			}
		}


private void NormalizeWindowRect()
		{
			float maxWidth = Mathf.Max(960f, Screen.width - 80f);
			float maxHeight = Mathf.Max(720f, Screen.height - 80f);
			_windowRect.width = Mathf.Min(1600f, maxWidth);
			_windowRect.height = Mathf.Min(1230f, maxHeight);
			_windowRect.x = Mathf.Clamp(_windowRect.x, 20f, Mathf.Max(20f, Screen.width - _windowRect.width - 20f));
			_windowRect.y = Mathf.Clamp(_windowRect.y, 20f, Mathf.Max(20f, Screen.height - _windowRect.height - 20f));
		}

private void DrawBackdrop()
		{
			GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), _whiteTexture,
				ScaleMode.StretchToFill, true, 0f, new Color(0f, 0f, 0f, 0.72f), 0f, 0f);
			DrawSolidRect(new Rect(_windowRect.x - 5f, _windowRect.y - 5f, _windowRect.width + 10f, _windowRect.height + 10f),
				new Color(0.80f, 0.82f, 0.88f, 0.96f));
			DrawSolidRect(new Rect(_windowRect.x - 2f, _windowRect.y - 2f, _windowRect.width + 4f, _windowRect.height + 4f),
				new Color(0.46f, 0.49f, 0.56f, 0.98f));
			GUI.DrawTexture(_windowRect, _whiteTexture,
				ScaleMode.StretchToFill, true, 0f, new Color(0.035f, 0.032f, 0.028f, 0.98f), 0f, 0f);
			DrawSolidRect(new Rect(_windowRect.x + 12f, _windowRect.y + 12f, _windowRect.width - 24f, 1f),
				new Color(0.86f, 0.87f, 0.91f, 0.45f));
			DrawSolidRect(new Rect(_windowRect.x + 12f, _windowRect.y + _windowRect.height - 13f, _windowRect.width - 24f, 1f),
				new Color(0.38f, 0.40f, 0.45f, 0.55f));
		}


private void SyncOverlay()
		{
			if (_visible) CreateOverlayBlocker();
			else DestroyOverlayBlocker();
		}

private static void CreateOverlayBlocker()
		{
			if (_overlayBlocker != null) return;
			try
			{
				if (_overlayTexture == null)
				{
					_overlayTexture = new Texture2D(1, 1);
					_overlayTexture.SetPixel(0, 0, Color.white);
					_overlayTexture.Apply();
				}
				_overlayBlocker = new GameObject("XjCodexOverlay");
				Canvas canvas = _overlayBlocker.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				canvas.sortingOrder = 29990;
				_overlayBlocker.AddComponent<GraphicRaycaster>();
				Image image = _overlayBlocker.AddComponent<Image>();
				image.sprite = Sprite.Create(_overlayTexture, new Rect(0f, 0f, 1f, 1f), Vector2.zero);
				image.color = new Color(0f, 0f, 0f, 0f);
				image.raycastTarget = true;
				RectTransform transform = _overlayBlocker.GetComponent<RectTransform>();
				transform.anchorMin = Vector2.zero;
				transform.anchorMax = Vector2.one;
				transform.offsetMin = Vector2.zero;
				transform.offsetMax = Vector2.zero;
			}
			catch { }
		}

private static void DestroyOverlayBlocker()
		{
			if (_overlayBlocker == null) return;
			try { GameObject.Destroy(_overlayBlocker); } catch { }
			_overlayBlocker = null;
		}

private void HandleUiException(Exception ex)
		{
			_lastUiError = ex.GetType().Name + " - " + ex.Message;
				Debug.LogError("[玄鉴][玄鉴仙鉴] UI绘制异常，已自动关闭并解除遮罩：" + _lastUiError + "\n" + ex);
			_visible = false;
			_currentTab = 0;
			DestroyOverlayBlocker();
				try { WorldTip.showNow("玄鉴仙鉴绘制异常，已自动关闭。请查看 Player.log 中的 [玄鉴][玄鉴仙鉴]。", false, "top", 5f); } catch { }
		}
private static bool TryFocusMapLayerTarget(long cityId, int tileX, int tileY)
	{
		if (World.world == null) return false;
		if (cityId > 0L && TryResolveCityForFocus(cityId, out City city) && TryExtractMapPosition(city, 0, out Vector3 cityPosition))
		{
			try
			{
				World.world.locatePosition(cityPosition);
				return true;
			}
			catch { }
		}
		if (tileX < 0 || tileY < 0) return false;
		try
		{
			World.world.locatePosition(new Vector3(tileX, tileY, 0f));
			return true;
		}
		catch { return false; }
	}

private static bool TryResolveCityForFocus(long cityId, out City city)
	{
		return XjWorldLookupIndex.TryResolveCity(cityId, out city);
	}

private static bool TryExtractMapPosition(object source, int depth, out Vector3 position)
	{
		position = default;
		if (source == null || depth > 3) return false;
		if (source is Vector3 vector3)
		{
			position = vector3;
			return true;
		}
		if (source is Vector2 vector2)
		{
			position = new Vector3(vector2.x, vector2.y, 0f);
			return true;
		}
		if (TryReadFloatMember(source, "x", out float x) && TryReadFloatMember(source, "y", out float y))
		{
			position = new Vector3(x, y, 0f);
			return true;
		}

		string[] memberNames =
		{
			"current_position", "position", "world_position", "pos", "posV3",
			"city_center", "center", "main_tile", "tile", "current_tile"
		};
		Type type = source.GetType();
		for (int i = 0; i < memberNames.Length; i++)
		{
			object value = ReadMemberValue(type, source, memberNames[i]);
			if (value != null && !ReferenceEquals(value, source) && TryExtractMapPosition(value, depth + 1, out position)) return true;
		}

		string[] methodNames = { "getTile", "getCenterTile", "getCityCenterTile", "GetCenterTile" };
		for (int i = 0; i < methodNames.Length; i++)
		{
			MethodInfo method = type.GetMethod(methodNames[i], FocusBindingFlags, null, Type.EmptyTypes, null);
			if (method == null) continue;
			try
			{
				object value = method.Invoke(source, null);
				if (value != null && !ReferenceEquals(value, source) && TryExtractMapPosition(value, depth + 1, out position)) return true;
			}
			catch { }
		}
		return false;
	}

private static object ReadMemberValue(Type type, object source, string name)
	{
		try
		{
			FieldInfo field = type.GetField(name, FocusBindingFlags);
			if (field != null) return field.GetValue(source);
			PropertyInfo property = type.GetProperty(name, FocusBindingFlags);
			if (property != null && property.GetIndexParameters().Length == 0) return property.GetValue(source, null);
		}
		catch { }
		return null;
	}

private static bool TryReadFloatMember(object source, string name, out float value)
	{
		value = 0f;
		if (source == null) return false;
		object raw = ReadMemberValue(source.GetType(), source, name);
		if (raw == null) return false;
		try
		{
			value = Convert.ToSingle(raw);
			return true;
		}
		catch { return false; }
	}

}


