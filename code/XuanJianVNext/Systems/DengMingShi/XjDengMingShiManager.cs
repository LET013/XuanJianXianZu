using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Newtonsoft.Json;
using XuanJianVNext.Core;
using XuanJianVNext.Interop.WorldBox;
using XuanJianVNext.Interop.WorldBox.DengMingShi;
using XuanJianVNext.Data.DengMingShi;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Runtime;

namespace XuanJianVNext.Systems.DengMingShi;

/// <summary>0.5.4 登名石管理器 1:1 复刻 — 独立JSON文件持久化, 完整ActorData深拷贝</summary>
internal static class XjDengMingShiManager
{

	public class SavedActorPacket
	{
		public string ActorId { get; set; }
		public string Name { get; set; }
		public string AssetId { get; set; }
		public string SaveTime { get; set; }
		public ActorData ActorData;
		// ActorData 在不同 WorldBox 版本中不会稳定序列化所有自定义字段。
		// 关键高境状态使用独立、向后兼容的明文快照兜底。
		public HighRealmSnapshot HighRealm { get; set; }
	}

	public sealed class HighRealmSnapshot
	{
		public int SchemaVersion { get; set; } = 3;
		public string CultivationPath { get; set; } = string.Empty;
		public string HighRealmPayloadKind { get; set; } = string.Empty;
		public string HighRealmDaoStateJson { get; set; } = string.Empty;
		public XjDengMingShiFuQiSnapshot FuQiState { get; set; } = new XjDengMingShiFuQiSnapshot();
		public string RealmId { get; set; } = string.Empty;
		public string DaoTu { get; set; } = string.Empty;
		public int XjZz { get; set; }
		public int GongFaCollectionVersion { get; set; }
		public string GongFaCollectionJson { get; set; } = string.Empty;
		public string XianJiIds { get; set; } = string.Empty;
		public int XianJiLastYear { get; set; }
		public string QiuJinFaName { get; set; } = string.Empty;
		public string QiuJinFaSourceGongFaName { get; set; } = string.Empty;
		public int QiuJinFaSourceGongFaGrade { get; set; }
		public string QiuJinFaSourceDaoTu { get; set; } = string.Empty;
		public int QiuJinFaLastYear { get; set; }
		public string QiuJinFaBoundAuthority { get; set; } = string.Empty;
		public string JinXing { get; set; } = string.Empty;
		public string GuoWei { get; set; } = string.Empty;
		public int JinDanSuccessYear { get; set; }
		public int JinDanYiXiang { get; set; }
		public string ShenDanGuoWei { get; set; } = string.Empty;
		public long ShenDanAnchorActorId { get; set; }
		public string ShenDanAnchorName { get; set; } = string.Empty;
		public int ShenDanYear { get; set; }
		public string LocalQuanBing { get; set; } = string.Empty;
		public string SeizedQuanBing { get; set; } = string.Empty;
		public string SeizedQuanBingSources { get; set; } = string.Empty;
		public string ForeignQuanBing { get; set; } = string.Empty;
		public string WithdrawnToDongTian { get; set; } = string.Empty;
		public string GuoWeiZhongAi { get; set; } = string.Empty;
		public string PendingExternalZhengWeiDaoTu { get; set; } = string.Empty;
		public int LockUntilYear { get; set; }
		public bool IntegrationRetreatActive { get; set; }
		public int IntegrationRetreatEndYear { get; set; }
		public string QuanBingSummary { get; set; } = string.Empty;
		public string FaBaoId { get; set; } = string.Empty;
		public string FaBaoName { get; set; } = string.Empty;
		public string FaBaoDaoTu { get; set; } = string.Empty;
		public string FaBaoClass { get; set; } = string.Empty;
		public string FaBaoKind { get; set; } = string.Empty;
		public string FaBaoRole { get; set; } = string.Empty;
		public string FaBaoAffixes { get; set; } = string.Empty;
		public string FaBaoDescription { get; set; } = string.Empty;
		public string FaBaoSource { get; set; } = string.Empty;
		public int FaBaoYear { get; set; }
	}

	private static Dictionary<string, SavedActorPacket> _savedActors = new();
	private static readonly string SavePath = Path.Combine(Application.persistentDataPath, "xuanjian_dengmingshi_saved_actors.json");
	internal const int SavedActorLimit = 49;
	private const int RespawnAge = 18;
	private const string AgeYearProcessedKey = "xuanjian.age_year_processed";
	private static string _selectedActorId;
	private static bool _initialized;

	private static readonly JsonSerializerSettings JsonSettings = new()
	{
		ContractResolver = new XjNativeActorDataContractResolver(),
		NullValueHandling = NullValueHandling.Ignore
	};

	// ── 初始化 ──
	public static void Init()
	{
		if (_initialized) return;
		_initialized = true;
		LoadFromFile();
	}

	// ── 保存 ──
	public static bool SaveActor(Actor actor)
	{
		Init();
		if (actor == null || !actor.isAlive()) return false;
		try
		{
			actor.prepareForSave();
			string actorId = GetActorStorageId(actor);
			if (string.IsNullOrWhiteSpace(actorId)) return false;
			if (!_savedActors.ContainsKey(actorId) && _savedActors.Count >= SavedActorLimit)
			{
				Debug.LogWarning("[登名石] 保存失败：登名石最多保存" + SavedActorLimit + "名角色。");
				return false;
			}
			var packet = new SavedActorPacket
			{
				ActorId = actorId,
				Name = actor.name,
				AssetId = ResolveActorAssetId(actor),
				SaveTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
			};
			packet.ActorData = DeepCopy(actor.data);
			packet.HighRealm = CaptureHighRealmSnapshot(actor);
			if (packet.ActorData == null) return false;
			SanitizeSavedActorData(packet.ActorData);
			if (actor.data.saved_traits != null && actor.data.saved_traits.Count > 0)
			{
				packet.ActorData.saved_traits ??= new List<string>();
				packet.ActorData.saved_traits.Clear();
				packet.ActorData.saved_traits.AddRange(actor.data.saved_traits);
			}
			RemoveLegacyHashEntryIfNeeded(actorId, actor);
			_savedActors[actorId] = packet;
			SaveToFile();
			return true;
		}
		catch (Exception e) { Debug.LogError("[登名石] 保存失败: " + e.Message); return false; }
	}

	// ── 放置 ──
	public static bool SelectActorToPlace(string actorId)
	{
		Init();
		if (string.IsNullOrWhiteSpace(actorId) || !_savedActors.ContainsKey(actorId)) return false;
		_selectedActorId = actorId;
		return true;
	}

	public static bool SpawnSavedActor(WorldTile tile, string powerId = null)
	{
		Init();
		if (string.IsNullOrEmpty(_selectedActorId)) return false;
		if (!_savedActors.TryGetValue(_selectedActorId, out var packet)) return false;
		if (tile == null || packet.ActorData == null) return false;

		var savedTraits = packet.ActorData.saved_traits != null ? new List<string>(packet.ActorData.saved_traits) : null;
		var actorData = DeepCopy(packet.ActorData);
		actorData.asset_id = ResolveRespawnAssetId(packet.AssetId, actorData.asset_id);
		SanitizeSavedActorData(actorData);

		actorData.id = World.world.map_stats.getNextId("unit");
		ApplyRespawnAge(actorData);
		actorData.x = tile.pos.x;
		actorData.y = tile.pos.y;
		actorData.cityID = -1;
		actorData.homeBuildingID = -1;
		actorData.transportID = -1;
		ResetNativeIdentityMembers(actorData);
		ResetRuntimeStateForRespawn(actorData);
		PrepareActorDataForRespawn(actorData, packet.Name);

		if (savedTraits != null && savedTraits.Count > 0)
			actorData.saved_traits = savedTraits;

		// A placed record is a new live unit, not a world-save entry. Rehydrating the
		// complete ActorData through loadObject can restore stale container ids and break
		// the native spatial/AI indexes after prolonged simulation.
		Actor actor = World.world.units.spawnNewUnit(actorData.asset_id, tile, false, false, 0f, null, false, true);
		if (actor != null)
		{
			actor.data.cloneCustomDataFrom(actorData);
			ApplyRespawnAge(actor.data);
			ApplyRespawnName(actor.data, packet.Name);
			XjActorStateWriteGateway.SetDisplayName(actor, actor.data.name, customName: true);
		}
		if (actor != null && !XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
		{
			Debug.LogError("[登名石] 放置失败：角色未能建立原生文明归属，已阻止继续恢复玄鉴状态。");
			XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			return false;
		}
		if (actor != null && savedTraits != null && savedTraits.Count > 0)
		{
			RestoreSavedTraits(actor, savedTraits);
			actor.clearTraitCache();
			actor.setStatsDirty();
		}
		if (actor != null)
		{
			FinalizeRespawnedActor(actor, packet.Name);
			if (!RestoreHighRealmSnapshot(actor, packet.HighRealm))
			{
				Debug.LogError("[登名石] 放置失败：高境果位载荷无法原样恢复，已撤销本次生成。");
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				return false;
			}
			XjDengMingShiPostPlacement.Reconcile(actor);
			long.TryParse(packet.ActorId, out long sourceActorId);
			XjFuQiSwordWorldState.ReconcileRestoredActor(
				actor,
				sourceActorId,
				Math.Max(1, Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0)));
			FinalizeRespawnedActor(actor, packet.Name);
			actor.clearOldPath();
			actor.stopMovement();
			_selectedActorId = null;
		}
		return actor != null;
	}

	// ── 删除 ──
	public static bool RemoveSavedActor(string actorId)
	{
		Init();
		if (string.IsNullOrWhiteSpace(actorId) || !_savedActors.Remove(actorId)) return false;
		if (string.Equals(_selectedActorId, actorId, StringComparison.Ordinal)) _selectedActorId = null;
		SaveToFile();
		return true;
	}
	public static void RemoveSavedActor(Actor actor)
	{
		if (actor == null || !actor.isAlive()) return;
		string id = GetActorStorageId(actor);
		if (!string.IsNullOrWhiteSpace(id)) RemoveSavedActor(id);
		string legacyId = GetLegacyHashActorId(actor);
		if (!string.IsNullOrWhiteSpace(legacyId) && !string.Equals(legacyId, id, StringComparison.Ordinal)) RemoveSavedActor(legacyId);
	}

	// ── 查询 ──
	public static bool IsActorSaved(Actor actor)
	{
		Init();
		if (actor == null || !actor.isAlive()) return false;
		string id = GetActorStorageId(actor);
		if (!string.IsNullOrWhiteSpace(id) && _savedActors.ContainsKey(id)) return true;
		string legacyId = GetLegacyHashActorId(actor);
		return !string.IsNullOrWhiteSpace(legacyId) && _savedActors.ContainsKey(legacyId);
	}
	public static Dictionary<string, SavedActorPacket> GetSavedActors()
	{
		Init();
		return new Dictionary<string, SavedActorPacket>(_savedActors);
	}

	// ── 文件持久化 ──
	private static void SaveToFile()
	{
		string tempPath = SavePath + ".tmp";
		string backupPath = SavePath + ".bak";
		try
		{
			string json = JsonConvert.SerializeObject(_savedActors, Formatting.Indented, JsonSettings);
			File.WriteAllText(tempPath, json);
			if (File.Exists(SavePath))
			{
				File.Copy(SavePath, backupPath, true);
				File.Delete(SavePath);
			}
			File.Move(tempPath, SavePath);
		}
		catch (Exception e)
		{
			Debug.LogError("[登名石] 保存文件失败: " + e.Message);
			try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (System.Exception xjCaught325) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:325", xjCaught325); }
		}
	}

	private static void LoadFromFile()
	{
		string backupPath = SavePath + ".bak";
		bool primaryExists = File.Exists(SavePath);
		string sourcePath = primaryExists ? SavePath : (File.Exists(backupPath) ? backupPath : string.Empty);
		if (sourcePath.Length == 0)
		{
			_savedActors = new();
			return;
		}

		try
		{
			bool changed = LoadDictionaryFromPath(sourcePath);
			if (changed || !string.Equals(sourcePath, SavePath, StringComparison.Ordinal)) SaveToFile();
		}
		catch (Exception e)
		{
			Debug.LogWarning("[登名石] 加载文件失败: " + e.Message);
			if (!string.Equals(sourcePath, backupPath, StringComparison.Ordinal) && File.Exists(backupPath))
			{
				try
				{
					LoadDictionaryFromPath(backupPath);
					// 主文件已经损坏时先移除它，避免 SaveToFile 将坏主文件覆盖正常备份。
					try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch (System.Exception xjCaught354) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:354", xjCaught354); }
					SaveToFile();
					return;
				}
				catch (Exception backupError)
				{
					Debug.LogWarning("[登名石] 备份文件加载失败: " + backupError.Message);
				}
			}
			_savedActors = new();
		}
	}

	private static bool LoadDictionaryFromPath(string path)
	{
		string json = File.ReadAllText(path);
		_savedActors = JsonConvert.DeserializeObject<Dictionary<string, SavedActorPacket>>(json, JsonSettings) ?? new();
		bool changed = false;
		List<string> invalidKeys = null;
		foreach (KeyValuePair<string, SavedActorPacket> pair in _savedActors)
		{
			if (pair.Value?.ActorData == null)
			{
				invalidKeys ??= new List<string>();
				invalidKeys.Add(pair.Key);
				continue;
			}
			changed |= SanitizeSavedActorData(pair.Value.ActorData);
		}
		if (invalidKeys != null)
		{
			for (int i = 0; i < invalidKeys.Count; i++) _savedActors.Remove(invalidKeys[i]);
			changed = true;
		}
		return changed;
	}

	// ── 工具 ──
	private static T DeepCopy<T>(T obj) where T : class
	{
		if (obj == null) return null;
		try
		{
			var json = JsonConvert.SerializeObject(obj, JsonSettings);
			var copy = JsonConvert.DeserializeObject<T>(json, JsonSettings);
			if (copy is ActorData ac && obj is ActorData ao && ao.saved_traits != null && ao.saved_traits.Count > 0)
				ac.saved_traits = new List<string>(ao.saved_traits);
			return copy;
		}
		catch (Exception e) { Debug.LogWarning("[登名石] 深拷贝失败: " + e.Message); return null; }
	}

	private static bool SanitizeSavedActorData(ActorData data)
	{
		if (data == null) return false;
		if (ShouldClearSubspecies(data)) { data.subspecies = -1; return true; }
		return false;
	}

	private static void RestoreSavedTraits(Actor actor, IReadOnlyList<string> savedTraits)
	{
		if (actor?.data == null || savedTraits == null || savedTraits.Count == 0)
		{
			return;
		}

		actor.data.saved_traits = new List<string>(savedTraits.Count);
		for (int i = 0; i < savedTraits.Count; i++)
		{
			string traitId = (savedTraits[i] ?? string.Empty).Trim();
			if (string.IsNullOrWhiteSpace(traitId) || AssetManager.traits?.get(traitId) == null)
			{
				continue;
			}

			actor.data.saved_traits.Add(traitId);
			if (!actor.hasTrait(traitId))
			{
				actor.addTrait(traitId, false);
			}
		}
	}

	private static bool ShouldClearSubspecies(ActorData data)
	{
		if (data == null) return false;
		string id = (data.asset_id ?? "").Trim();
		if (id.Length == 0) return false;
		bool isHumanoid = string.Equals(id, "human", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(id, "elf", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(id, "orc", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(id, "dwarf", StringComparison.OrdinalIgnoreCase);
		if (!isHumanoid && id.StartsWith("civ_", StringComparison.OrdinalIgnoreCase))
		{
			string s = id.Substring(4);
			isHumanoid = string.Equals(s, "human", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s, "elf", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s, "orc", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(s, "dwarf", StringComparison.OrdinalIgnoreCase);
		}
		return isHumanoid && data.subspecies >= 0;
	}

	private static string GetActorStorageId(Actor actor)
	{
		if (actor?.data is BaseSystemData d && d.id > 0L) return d.id.ToString();
		return GetLegacyHashActorId(actor);
	}

	private static string GetLegacyHashActorId(Actor actor) => actor?.GetHashCode().ToString() ?? "";

	private static string ResolveActorAssetId(Actor actor)
	{
		if (actor?.asset != null)
		{
			try
			{
				string id = ((Asset)actor.asset).id;
				if (!string.IsNullOrWhiteSpace(id)) return id.Trim();
			}
			catch (System.Exception xjCaught474) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:474", xjCaught474); }
		}

		return (actor?.data?.asset_id ?? string.Empty).Trim();
	}

	private static string ResolveRespawnAssetId(string savedAssetId, string fallbackAssetId)
	{
		string preferred = (savedAssetId ?? string.Empty).Trim();
		if (!string.IsNullOrWhiteSpace(preferred))
		{
			return preferred;
		}

		string fallback = (fallbackAssetId ?? string.Empty).Trim();
		return string.IsNullOrWhiteSpace(fallback) ? "human" : fallback;
	}

	private static void RemoveLegacyHashEntryIfNeeded(string id, Actor actor)
	{
		string legacy = GetLegacyHashActorId(actor);
		if (!string.IsNullOrWhiteSpace(legacy) && !string.Equals(legacy, id, StringComparison.Ordinal))
			_savedActors.Remove(legacy);
	}


	internal static string ResolveActorAssetIdForArchive(Actor actor)
	{
		return ResolveActorAssetId(actor);
	}

	internal static HighRealmSnapshot CaptureHighRealmForArchive(Actor actor)
	{
		return CaptureHighRealmSnapshot(actor);
	}

	internal static bool TrySpawnHighRealmArchiveActor(
		WorldTile tile,
		string assetId,
		string actorName,
		IReadOnlyList<string> savedTraits,
		HighRealmSnapshot snapshot,
		int requestedAge,
		out Actor actor)
	{
		actor = null;
		if (tile == null || snapshot == null || World.world?.units == null) return false;
		string resolvedAssetId = ResolveRespawnAssetId(assetId, assetId);
		if (string.IsNullOrWhiteSpace(resolvedAssetId)) return false;
		try
		{
			actor = World.world.units.spawnNewUnit(resolvedAssetId, tile, false, false, 0f, null, false, true);
			if (actor?.data == null) return false;
			// 高境档案生成目前只用于故尊命痕。先于境界/果位恢复落下标记，
			// 让后续恢复路径知道此身不是当世完整修士，绝不能认领真实果位或进入修士车道。
			XjActorAccessor.SetInt(actor, XjActorDataKeys.GuZunManifestation, 1);
			ApplyRespawnAge(actor.data, requestedAge);
			ApplyRespawnName(actor.data, actorName);
			XjActorStateWriteGateway.SetDisplayName(actor, actor.data.name, customName: true);
			if (!XjDengMingShiSpawnSafety.EnsureNativeCivilizationState(actor))
			{
				XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
				actor = null;
				return false;
			}
			if (savedTraits != null && savedTraits.Count > 0)
			{
				RestoreSavedTraits(actor, new List<string>(savedTraits));
			}
			FinalizeRespawnedActor(actor, actorName, requestedAge);
			// 故尊只恢复修炼成果；旧本命宝物可能已经被家族、宗门或继承者持有，
			// 因此禁止复制法宝快照，后续由现有本命装备系统重新核对或凝聚。
			if (!RestoreHighRealmSnapshot(actor, snapshot, restoreFaBao: false))
			{
				RollbackInvalidHighRealmArchiveSpawn(actor);
				actor = null;
				return false;
			}
			XjDengMingShiPostPlacement.Reconcile(actor);
			FinalizeRespawnedActor(actor, actorName, requestedAge);
			if (!ValidateHighRealmArchiveRestore(actor, snapshot))
			{
				RollbackInvalidHighRealmArchiveSpawn(actor);
				actor = null;
				return false;
			}
			return XjSafeCore.IsAliveActor(actor);
		}
		catch
		{
			if (actor != null) XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
			actor = null;
			return false;
		}
	}

	private static bool ValidateHighRealmArchiveRestore(Actor actor, HighRealmSnapshot snapshot)
	{
		if (!XjSafeCore.IsAliveActor(actor) || snapshot == null) return false;
		string expectedRealm = (snapshot.RealmId ?? string.Empty).Trim();
		string actualRealm = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.IsNullOrWhiteSpace(expectedRealm)
			|| !string.Equals(expectedRealm, actualRealm, StringComparison.Ordinal)) return false;

		XjCultivationPathRules.TryGetPath(actor, out string actualPath);
		string expectedPath = XjDengMingShiCultivationRestore.ResolvePath(
			actor,
			snapshot.CultivationPath,
			snapshot.RealmId,
			string.Empty,
			snapshot.FuQiState);
		if (!string.Equals((expectedPath ?? string.Empty).Trim(), (actualPath ?? string.Empty).Trim(), StringComparison.Ordinal)) return false;

		if (!string.IsNullOrWhiteSpace(snapshot.DaoTu)
			&& (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string actualDaoTu)
				|| !string.Equals(snapshot.DaoTu.Trim(), (actualDaoTu ?? string.Empty).Trim(), StringComparison.Ordinal))) return false;

		if (snapshot.JinDanYiXiang > 0)
		{
			if (!XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int actualYiXiang)
				|| actualYiXiang != snapshot.JinDanYiXiang) return false;
		}

		string payloadKind = XjDengMingShiCultivationRestore.ResolvePayloadKind(
			snapshot.HighRealmPayloadKind,
			snapshot.RealmId,
			snapshot.JinXing,
			snapshot.GuoWei,
			snapshot.ShenDanGuoWei,
			snapshot.ShenDanAnchorActorId,
			snapshot.ShenDanYear);
		if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadJinDan, StringComparison.Ordinal)
			|| string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadDaoTaiCarrier, StringComparison.Ordinal))
		{
			XjJinDanState state = string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadDaoTaiCarrier, StringComparison.Ordinal)
				? XjJinDanAccessor.BuildPositionCarrierState(actor)
				: XjJinDanAccessor.BuildState(actor);
			return state.Found
				&& string.Equals((state.JinXing ?? string.Empty).Trim(), (snapshot.JinXing ?? string.Empty).Trim(), StringComparison.Ordinal)
				&& string.Equals((state.GuoWei ?? string.Empty).Trim(), (snapshot.GuoWei ?? string.Empty).Trim(), StringComparison.Ordinal);
		}
		// 故尊候选不包含神丹。若旧档或非法快照混入，拒绝生成，避免绕过活锚校验。
		return !string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadShenDan, StringComparison.Ordinal);
	}

	private static void RollbackInvalidHighRealmArchiveSpawn(Actor actor)
	{
		if (actor == null) return;
		long actorId = 0L;
		try { if (actor.data != null) actorId = ((BaseSystemData)actor.data).id; } catch (System.Exception xjCaught614) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:614", xjCaught614); }
		try { XjJinDanAccessor.ClearSuccess(actor); } catch (System.Exception xjCaught615) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:615", xjCaught615); }
		if (actorId > 0L)
		{
			try { XjGuoWeiQuanBingRegistry.RemoveActor(actorId); } catch (System.Exception xjCaught618) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:618", xjCaught618); }
			try { XjActorRegistry.Unregister(actorId); } catch (System.Exception xjCaught619) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:619", xjCaught619); }
		}
		XjDengMingShiSpawnSafety.TryRemoveInvalidActor(actor);
	}

	private static HighRealmSnapshot CaptureHighRealmSnapshot(Actor actor)
	{
		if (actor?.data == null)
		{
			return null;
		}

		var snapshot = new HighRealmSnapshot();
		string realmId = XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter);
		if (string.IsNullOrWhiteSpace(realmId))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out realmId);
		}
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
		XjCultivationPathRules.TryGetPath(actor, out string cultivationPath);
		snapshot.SchemaVersion = 4;
		snapshot.CultivationPath = cultivationPath ?? string.Empty;
		snapshot.FuQiState = XjDengMingShiFuQiSnapshotCodec.Capture(actor);
		snapshot.RealmId = realmId ?? string.Empty;
		snapshot.DaoTu = daoTu ?? string.Empty;
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjZz, out int aptitude);
		snapshot.XjZz = Math.Max(0, aptitude);
		XjActorGongFaCollection.TryExportSerialized(actor, out int collectionVersion, out string collectionJson);
		snapshot.GongFaCollectionVersion = collectionVersion;
		snapshot.GongFaCollectionJson = collectionJson;
		XjXianJiState xianJi = XjXianJiAccessor.BuildState(actor);
		snapshot.XianJiIds = xianJi.Ids == null ? string.Empty : string.Join("|", xianJi.Ids);
		snapshot.XianJiLastYear = xianJi.LastYear;
		XjQiuJinFaState qiuJinFa = XjQiuJinFaAccessor.BuildState(actor);
		if (qiuJinFa.Found && qiuJinFa.Ready)
		{
			snapshot.QiuJinFaName = qiuJinFa.Name;
			snapshot.QiuJinFaSourceGongFaName = qiuJinFa.SourceGongFaName;
			snapshot.QiuJinFaSourceGongFaGrade = qiuJinFa.SourceGongFaGrade;
			snapshot.QiuJinFaSourceDaoTu = qiuJinFa.SourceDaoTu;
			snapshot.QiuJinFaLastYear = qiuJinFa.LastYear;
			snapshot.QiuJinFaBoundAuthority = qiuJinFa.BoundAuthority;
		}

		XjJinDanState jinDan = XjJinDanAccessor.BuildPositionCarrierState(actor);
		if (jinDan.Found)
		{
			snapshot.JinXing = jinDan.JinXing;
			snapshot.GuoWei = jinDan.GuoWei;
			snapshot.JinDanSuccessYear = jinDan.SuccessYear;
		}
		XjActorAccessor.TryGetInt(actor, XjActorDataKeys.XjJinDanYiXiang, out int jinDanYiXiang);
		snapshot.JinDanYiXiang = Math.Max(0, jinDanYiXiang);
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		if (shenDan.Found)
		{
			snapshot.ShenDanGuoWei = shenDan.GuoWei;
			snapshot.ShenDanAnchorActorId = shenDan.AnchorActorId;
			snapshot.ShenDanAnchorName = shenDan.AnchorName;
			snapshot.ShenDanYear = shenDan.Year;
		}
		snapshot.HighRealmPayloadKind = shenDan.Found
			? XjDengMingShiCultivationRestore.PayloadShenDan
			: jinDan.Found && XjDaoTaiSpellScale.IsDaoTaiActor(actor)
				? XjDengMingShiCultivationRestore.PayloadDaoTaiCarrier
				: jinDan.Found
					? XjDengMingShiCultivationRestore.PayloadJinDan
					: XjDengMingShiCultivationRestore.PayloadNone;
		snapshot.HighRealmDaoStateJson = XjHighRealmDaoStateService.ExportActorStateJson(actor);

		if (XjGuoWeiQuanBingRegistry.TryGetForLiveDisplay(actor, out XjGuoWeiQuanBingState authority))
		{
			snapshot.LocalQuanBing = authority.LocalQuanBing;
			snapshot.SeizedQuanBing = authority.SeizedQuanBing;
			snapshot.SeizedQuanBingSources = authority.SeizedQuanBingSources;
			snapshot.ForeignQuanBing = authority.ForeignQuanBing;
			snapshot.WithdrawnToDongTian = authority.WithdrawnToDongTian;
			snapshot.GuoWeiZhongAi = authority.GuoWeiZhongAi;
			snapshot.PendingExternalZhengWeiDaoTu = authority.PendingExternalZhengWeiDaoTu;
			snapshot.LockUntilYear = authority.LockUntilYear;
			snapshot.IntegrationRetreatActive = authority.IntegrationRetreatActive;
			snapshot.IntegrationRetreatEndYear = authority.IntegrationRetreatEndYear;
			snapshot.QuanBingSummary = authority.Summary;
		}

		XjFaBaoState faBao = XjFaBaoAccessor.BuildState(actor);
		if (faBao.Found)
		{
			snapshot.FaBaoId = faBao.Id;
			snapshot.FaBaoName = faBao.Name;
			snapshot.FaBaoDaoTu = faBao.DaoTu;
			snapshot.FaBaoClass = faBao.ClassName;
			snapshot.FaBaoKind = faBao.Kind;
			snapshot.FaBaoRole = faBao.Role;
			snapshot.FaBaoAffixes = faBao.Affixes;
			snapshot.FaBaoDescription = faBao.Description;
			snapshot.FaBaoSource = faBao.Source;
			snapshot.FaBaoYear = faBao.Year;
		}
		return snapshot;
	}

	private static bool RestoreHighRealmSnapshot(Actor actor, HighRealmSnapshot snapshot)
	{
		return RestoreHighRealmSnapshot(actor, snapshot, restoreFaBao: true);
	}

	private static bool RestoreHighRealmSnapshot(Actor actor, HighRealmSnapshot snapshot, bool restoreFaBao)
	{
		if (actor?.data == null) return false;
		if (snapshot == null) return true;
		if (!XjCultivationPathRules.IsKnownPath(snapshot.CultivationPath)
			&& string.IsNullOrWhiteSpace(snapshot.RealmId))
		{
			// 普通角色没有玄鉴高境载荷，保持原生放置行为。
			return true;
		}

		string path = XjDengMingShiCultivationRestore.ResolvePath(
			actor,
			snapshot.CultivationPath,
			snapshot.RealmId,
			string.Empty,
			snapshot.FuQiState);
		if (!XjDengMingShiCultivationRestore.RestoreIdentity(
			actor,
			path,
			snapshot.DaoTu,
			snapshot.RealmId,
			snapshot.XjZz,
			snapshot.FuQiState))
		{
			XjVisibleTraitSync.SyncCultivationTraits(actor);
			return false;
		}

		if (string.Equals(path, XjCultivationPathIds.ZiFuJinDan, StringComparison.Ordinal))
		{
			RestoreZiJinSnapshot(actor, snapshot);
		}
		if (snapshot.JinDanYiXiang > 0)
		{
			// 服气/紫金的小境界必须同时恢复真实道行/修持与兼容投影。
			XjHighRealmDaoStateService.RestoreImportedProgressMetadata(
				actor, snapshot.JinDanYiXiang, overwriteExisting: true);
		}

		string payloadKind = XjDengMingShiCultivationRestore.ResolvePayloadKind(
			snapshot.HighRealmPayloadKind,
			snapshot.RealmId,
			snapshot.JinXing,
			snapshot.GuoWei,
			snapshot.ShenDanGuoWei,
			snapshot.ShenDanAnchorActorId,
			snapshot.ShenDanYear);
		if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadShenDan, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreShenDan(
				actor, path, snapshot.DaoTu, snapshot.ShenDanGuoWei,
				snapshot.ShenDanAnchorActorId, snapshot.ShenDanAnchorName, snapshot.ShenDanYear)) return false;
		}
		else if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadDaoTaiCarrier, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreDaoTaiPositionCarrier(
				actor, snapshot.DaoTu, snapshot.JinXing, snapshot.GuoWei, snapshot.JinDanSuccessYear,
				out string resolvedGuoWei)) return false;
			RestorePositionAuthoritySnapshot(actor, snapshot, resolvedGuoWei);
		}
		else if (string.Equals(payloadKind, XjDengMingShiCultivationRestore.PayloadJinDan, StringComparison.Ordinal))
		{
			if (!XjDengMingShiCultivationRestore.RestoreJinDan(
				actor, snapshot.DaoTu, snapshot.JinXing, snapshot.GuoWei, snapshot.JinDanSuccessYear,
				out string resolvedGuoWei)) return false;
			RestorePositionAuthoritySnapshot(actor, snapshot, resolvedGuoWei);
		}

		// 0.9.6.5：紫府阶段的托果法门（神丹）与求位意向同样属于登名石载荷，
		// 不能只在已恢复金丹时导入。高境重复导入是幂等的，并且不增加道势。
		if (!string.IsNullOrWhiteSpace(snapshot.HighRealmDaoStateJson))
		{
			int daoStateYear = Math.Max(1, snapshot.JinDanSuccessYear > 0
				? snapshot.JinDanSuccessYear
				: Math.Max(XjYearTracker.CurrentYear, World.world?.map_stats?.year ?? 0));
			if (!XjHighRealmDaoStateService.ImportActorStateJson(actor, snapshot.HighRealmDaoStateJson, daoStateYear))
				XjHighRealmDaoStateService.EnsureRestoredState(actor, daoStateYear);
			if (XjJinDanAccessor.BuildPositionCarrierState(actor).Found
				&& !(XjActorAccessor.TryGetInt(actor, XjActorDataKeys.GuZunManifestation, out int guZunProgressMarker) && guZunProgressMarker > 0))
				XjGuoWeiQuanBingLifecycle.RefreshProgressAuthorities(actor, daoStateYear);
		}

		if (restoreFaBao
			&& !string.IsNullOrWhiteSpace(snapshot.FaBaoId)
			&& !string.IsNullOrWhiteSpace(snapshot.FaBaoName))
		{
			XjFaBaoAccessor.WriteState(actor, new XjFaBaoState(
				true,
				snapshot.FaBaoId,
				snapshot.FaBaoName,
				snapshot.FaBaoDaoTu,
				snapshot.FaBaoClass,
				snapshot.FaBaoKind,
				snapshot.FaBaoRole,
				snapshot.FaBaoAffixes,
				snapshot.FaBaoDescription,
				snapshot.FaBaoSource,
				snapshot.FaBaoYear,
				"DengMingShiRestore"));
			XjFaBaoEquipmentSync.TryEnsureGeneratedEquipment(actor);
		}
		XjVisibleTraitSync.SyncCultivationTraits(actor);
		return true;
	}

	private static void RestorePositionAuthoritySnapshot(Actor actor, HighRealmSnapshot snapshot, string resolvedGuoWei)
	{
		if (actor?.data == null || snapshot == null || string.IsNullOrWhiteSpace(resolvedGuoWei)) return;
		bool isGuZunManifestation = XjActorAccessor.TryGetInt(
			actor, XjActorDataKeys.GuZunManifestation, out int guZunMarker) && guZunMarker > 0;
		if (isGuZunManifestation) return;
		long newActorId = ((BaseSystemData)actor.data).id;
		XjJinDanState restored = XjJinDanAccessor.BuildPositionCarrierState(actor);
		int restoredYear = restored.Found ? restored.SuccessYear : Math.Max(1, snapshot.JinDanSuccessYear);
		if (snapshot.JinDanYiXiang > 0)
		{
			XjHighRealmDaoStateService.RestoreImportedProgressMetadata(
				actor, snapshot.JinDanYiXiang, overwriteExisting: true);
		}
		XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
			true, newActorId, actor.getName(), snapshot.DaoTu, resolvedGuoWei,
			snapshot.LocalQuanBing, snapshot.SeizedQuanBing, snapshot.ForeignQuanBing,
			snapshot.WithdrawnToDongTian, snapshot.GuoWeiZhongAi, snapshot.PendingExternalZhengWeiDaoTu,
			snapshot.LockUntilYear, snapshot.IntegrationRetreatActive, snapshot.IntegrationRetreatEndYear,
			string.IsNullOrWhiteSpace(snapshot.QuanBingSummary) ? "登名石高境快照恢复" : snapshot.QuanBingSummary,
			"Active", restoredYear, 0, string.Empty, snapshot.SeizedQuanBingSources));
	}

	private static void RestoreZiJinSnapshot(Actor actor, HighRealmSnapshot snapshot)
	{
		if (snapshot.GongFaCollectionVersion > 0 || !string.IsNullOrWhiteSpace(snapshot.XianJiIds))
		{
			XjXianJiAccessor.RestoreSnapshot(actor, snapshot.XianJiIds, snapshot.XianJiLastYear);
		}
		if (!string.IsNullOrWhiteSpace(snapshot.GongFaCollectionJson))
		{
			XjActorGongFaCollection.TryRestoreSerialized(
				actor,
				snapshot.GongFaCollectionVersion,
				snapshot.GongFaCollectionJson,
				"DengMingShiManager");
		}
		else
		{
			XjActorGongFaCollection.ReconcileWithActor(actor, "DengMingShiManagerLegacy");
		}
		if (!string.IsNullOrWhiteSpace(snapshot.QiuJinFaName))
		{
			string boundAuthority = snapshot.QiuJinFaBoundAuthority;
			if (string.IsNullOrWhiteSpace(boundAuthority))
			{
				boundAuthority = XjFamilyHighGradeTransmission.ResolveBoundAuthority(
					snapshot.QiuJinFaSourceDaoTu,
					snapshot.QiuJinFaName,
					snapshot.QiuJinFaSourceGongFaName);
			}
			XjQiuJinFaAccessor.WriteState(actor, new XjQiuJinFaState(
				true,
				snapshot.QiuJinFaName,
				snapshot.QiuJinFaSourceGongFaName,
				snapshot.QiuJinFaSourceGongFaGrade,
				snapshot.QiuJinFaSourceDaoTu,
				true,
				snapshot.QiuJinFaLastYear,
				"DengMingShiManager",
				boundAuthority));
		}
	}

	private static void ApplyRespawnAge(ActorData data)
	{
		ApplyRespawnAge(data, RespawnAge);
	}

	private static void ApplyRespawnAge(ActorData data, int requestedAge)
	{
		if (data == null)
		{
			return;
		}

		try
		{
			if (World.world != null)
			{
				data.created_time = World.world.getCurWorldTime();
			}
		}
		catch (System.Exception xjCaught898) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/DengMingShi/XjDengMingShiManager.cs:898", xjCaught898); }

		int age = Math.Max(1, requestedAge);
		data.age_overgrowth = age;
		TrySetNumericMember(data, "age", age);
		TrySetNumericMember(data, "death_time", 0f);
		TrySetNumericMember(data, "time_to_die", 0f);
		TrySetBooleanMember(data, "dead", false);
		TrySetBooleanMember(data, "is_dead", false);
		TrySetBooleanMember(data, "removed", false);
		XjActorStateWriteGateway.SetDetachedInt(
			data,
			AgeYearProcessedKey,
			age - 1,
			XjActorStateDomain.Progression | XjActorStateDomain.Identity);
	}

	private static void ResetRuntimeStateForRespawn(ActorData data)
	{
		if (data is not BaseSystemData baseData) return;
		using XjActorStateRevisionStore.ReductionScope reduction = XjActorStateRevisionStore.BeginReduction(baseData.id);
		XjActorStateDomain domain = XjActorStateDomain.Progression | XjActorStateDomain.HighRealm | XjActorStateDomain.Identity;
		XjActorStateWriteGateway.SetDetachedInt(data, AgeYearProcessedKey, RespawnAge - 1, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_active", false, domain);
		XjActorStateWriteGateway.SetDetachedLong(data, "xuanjian.dongtian_target_id", 0L, domain);
		XjActorStateWriteGateway.SetDetachedInt(data, "xuanjian.dongtian_cultivate_start_year", 0, domain);
		XjActorStateWriteGateway.SetDetachedInt(data, "xuanjian.dongtian_cultivate_end_year", 0, domain);
		XjActorStateWriteGateway.SetDetachedInt(data, "xuanjian.dongtian_cultivate_years", 0, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_life_granted", false, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_visual_suppressed", false, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_anim_container_suppressed", false, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_temp_peaceful", false, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_post_peace_active", false, domain);
		XjActorStateWriteGateway.SetDetachedInt(data, "xuanjian.dongtian_post_peace_end_year", 0, domain);
		XjActorStateWriteGateway.SetDetachedBool(data, "xuanjian.dongtian_post_peace_broken_by_attack", false, domain);
	}

	private static void PrepareActorDataForRespawn(ActorData data, string savedName)
	{
		if (data == null) return;
		data.clan = -1L;
		data.family = -1L;
		data.health = Math.Max(1, data.health);
		ApplyRespawnAge(data);
		ResetNativeSurvivalMembers(data);
		ApplyRespawnName(data, savedName);
	}

	private static void FinalizeRespawnedActor(Actor actor, string savedName)
	{
		FinalizeRespawnedActor(actor, savedName, RespawnAge);
	}

	private static void FinalizeRespawnedActor(Actor actor, string savedName, int requestedAge)
	{
		if (actor?.data == null) return;
		ApplyRespawnAge(actor.data, requestedAge);
		ApplyRespawnName(actor.data, savedName);
		try
		{
			XjActorStateWriteGateway.SetDisplayName(actor, ResolveRespawnName(savedName, actor.name), customName: true);
			actor.updateStats();
			float maxHealth = XjSafeCore.GetMaxHealthSafe(actor, 0f);
			if (maxHealth > 0f)
			{
				actor.data.health = Mathf.Max(1, Mathf.RoundToInt(maxHealth));
			}
			else
			{
				actor.data.health = Math.Max(1, actor.data.health);
			}
			actor.clearTraitCache();
			actor.setStatsDirty();
		}
		catch
		{
			actor.data.health = Math.Max(1, actor.data.health);
		}
	}

	private static void ApplyRespawnName(ActorData data, string savedName)
	{
		if (data is not BaseSystemData bd) return;
		bd.name = ResolveRespawnName(savedName, bd.name);
		bd.custom_name = true;
	}

	private static string ResolveRespawnName(string savedName, string fallback)
	{
		if (!string.IsNullOrWhiteSpace(savedName)) return savedName.Trim();
		if (!string.IsNullOrWhiteSpace(fallback)) return fallback.Trim();
		return "登名者";
	}

	private static void ResetNativeSurvivalMembers(ActorData data)
	{
		data.health = Math.Max(1, data.health);
		TrySetNumericMember(data, "hunger", 100f);
		TrySetNumericMember(data, "food", 100f);
		TrySetNumericMember(data, "nutrition", 100f);
		TrySetNumericMember(data, "satiety", 100f);
		TrySetNumericMember(data, "stomach", 100f);
		TrySetNumericMember(data, "age", RespawnAge);
		TrySetNumericMember(data, "death_time", 0f);
		TrySetNumericMember(data, "time_to_die", 0f);
		TrySetBooleanMember(data, "dead", false);
		TrySetBooleanMember(data, "is_dead", false);
		TrySetBooleanMember(data, "removed", false);
	}

	private static void ResetNativeIdentityMembers(ActorData data)
	{
		if (data == null) return;
		TrySetNumericMember(data, "kingdomID", -1f);
		TrySetNumericMember(data, "kingdomId", -1f);
		TrySetNumericMember(data, "kingdom_id", -1f);
		TrySetNumericMember(data, "cityID", -1f);
		TrySetNumericMember(data, "cityId", -1f);
		TrySetNumericMember(data, "city_id", -1f);
		TrySetNumericMember(data, "villageID", -1f);
		TrySetNumericMember(data, "villageId", -1f);
		TrySetNumericMember(data, "village_id", -1f);
		TrySetNumericMember(data, "unit_group_id", -1f);
		TrySetNumericMember(data, "group_id", -1f);
	}

	private static void TrySetNumericMember(object target, string memberName, float value)
	{
		XjNativeReflectionInterop.TryWriteNumericMember(target, memberName, value);
	}

	private static void TrySetBooleanMember(object target, string memberName, bool value)
	{
		XjNativeReflectionInterop.TryWriteBooleanMember(target, memberName, value);
	}

}
