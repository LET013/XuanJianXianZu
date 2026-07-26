using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Systems.DengMingShi;

/// <summary>0.5.4 登名石管理器 1:1 复刻 — 独立JSON文件持久化, 完整ActorData深拷贝</summary>
internal static class XjDengMingShiManager
{
	private sealed class NonPublicMemberContractResolver : DefaultContractResolver
	{
		protected override List<MemberInfo> GetSerializableMembers(Type objectType)
		{
			List<MemberInfo> serializableMembers = DeduplicateMembers(base.GetSerializableMembers(objectType));
			HashSet<string> knownNames = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < serializableMembers.Count; i++)
			{
				string memberName = GetSerializedMemberName(serializableMembers[i]);
				if (!string.IsNullOrWhiteSpace(memberName)) knownNames.Add(memberName);
			}

			MemberInfo[] members = objectType.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			for (int i = 0; i < members.Length; i++)
			{
				MemberInfo member = members[i];
				if ((member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
					|| serializableMembers.Contains(member)
					|| member.GetCustomAttribute<JsonIgnoreAttribute>(true) != null)
				{
					continue;
				}
				if (member is PropertyInfo property
					&& (property.GetIndexParameters().Length != 0
						|| (property.GetGetMethod(true) == null && property.GetSetMethod(true) == null)))
				{
					continue;
				}
				string memberName = GetSerializedMemberName(member);
				if (string.IsNullOrWhiteSpace(memberName) || !knownNames.Add(memberName)) continue;
				serializableMembers.Add(member);
			}
			return serializableMembers;
		}

		private static List<MemberInfo> DeduplicateMembers(List<MemberInfo> members)
		{
			List<MemberInfo> result = new List<MemberInfo>();
			HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
			for (int i = 0; i < members.Count; i++)
			{
				string name = GetSerializedMemberName(members[i]);
				if (!string.IsNullOrWhiteSpace(name) && names.Add(name)) result.Add(members[i]);
			}
			return result;
		}

		private static string GetSerializedMemberName(MemberInfo member)
		{
			if (member == null) return string.Empty;
			JsonPropertyAttribute property = member.GetCustomAttribute<JsonPropertyAttribute>(true);
			return string.IsNullOrWhiteSpace(property?.PropertyName) ? member.Name : property.PropertyName;
		}
	}

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
	private const int RespawnAge = 18;
	private const string AgeYearProcessedKey = "xuanjian.age_year_processed";
	private static string _selectedActorId;
	private static bool _initialized;

	private static readonly JsonSerializerSettings JsonSettings = new()
	{
		ContractResolver = new NonPublicMemberContractResolver(),
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
			actor.data.custom_name = true;
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
			RestoreHighRealmSnapshot(actor, packet.HighRealm);
			XjDengMingShiPostPlacement.Reconcile(actor);
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
			try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
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
					try { if (File.Exists(SavePath)) File.Delete(SavePath); } catch { }
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
			catch
			{
			}
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


	private static HighRealmSnapshot CaptureHighRealmSnapshot(Actor actor)
	{
		if (actor?.data == null)
		{
			return null;
		}

		var snapshot = new HighRealmSnapshot();
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu);
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

		XjJinDanState jinDan = XjJinDanAccessor.BuildState(actor);
		if (jinDan.Found)
		{
			snapshot.JinXing = jinDan.JinXing;
			snapshot.GuoWei = jinDan.GuoWei;
			snapshot.JinDanSuccessYear = jinDan.SuccessYear;
		}
		XjShenDanState shenDan = XjShenDanAccessor.BuildState(actor);
		if (shenDan.Found)
		{
			snapshot.ShenDanGuoWei = shenDan.GuoWei;
			snapshot.ShenDanAnchorActorId = shenDan.AnchorActorId;
			snapshot.ShenDanAnchorName = shenDan.AnchorName;
			snapshot.ShenDanYear = shenDan.Year;
		}

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

	private static void RestoreHighRealmSnapshot(Actor actor, HighRealmSnapshot snapshot)
	{
		if (actor?.data == null || snapshot == null)
		{
			return;
		}

		if (snapshot.XjZz > 0)
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZz, snapshot.XjZz);
			XjActorAccessor.SetInt(actor, XjActorDataKeys.XjZzCheckedAge5, 1);
		}
		if (string.Equals(snapshot.DaoTu, XjQingXuanKongZhengSystem.DaoTu, StringComparison.Ordinal))
		{
			XjActorAccessor.SetInt(actor, XjActorDataKeys.QingXuanUnlocked, 1);
		}
		if (!string.IsNullOrWhiteSpace(snapshot.DaoTu))
		{
			XjCultivationStateTransitions.TrySetDaoTu(actor, snapshot.DaoTu, false);
		}
		if (!string.IsNullOrWhiteSpace(snapshot.RealmId))
		{
			XjCultivationStateTransitions.TrySetRealm(actor, snapshot.RealmId, false);
		}
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

		if (!string.IsNullOrWhiteSpace(snapshot.JinXing)
			&& !string.IsNullOrWhiteSpace(snapshot.GuoWei)
			&& snapshot.JinDanSuccessYear > 0)
		{
			XjJinDanAccessor.WriteSuccess(actor, snapshot.JinXing, snapshot.GuoWei, snapshot.JinDanSuccessYear);
			long newActorId = ((BaseSystemData)actor.data).id;
			XjGuoWeiQuanBingRegistry.Record(new XjGuoWeiQuanBingState(
				true,
				newActorId,
				actor.getName(),
				snapshot.DaoTu,
				snapshot.GuoWei,
				snapshot.LocalQuanBing,
				snapshot.SeizedQuanBing,
				snapshot.ForeignQuanBing,
				snapshot.WithdrawnToDongTian,
				snapshot.GuoWeiZhongAi,
				snapshot.PendingExternalZhengWeiDaoTu,
				snapshot.LockUntilYear,
				snapshot.IntegrationRetreatActive,
				snapshot.IntegrationRetreatEndYear,
				string.IsNullOrWhiteSpace(snapshot.QuanBingSummary) ? "登名石高境快照恢复" : snapshot.QuanBingSummary,
				"Active",
				snapshot.JinDanSuccessYear,
				0,
				string.Empty,
				snapshot.SeizedQuanBingSources));
		}
		if (!string.IsNullOrWhiteSpace(snapshot.ShenDanGuoWei)
			&& snapshot.ShenDanAnchorActorId > 0L
			&& snapshot.ShenDanYear > 0)
		{
			XjShenDanAccessor.WriteSuccess(
				actor,
				snapshot.ShenDanGuoWei,
				snapshot.ShenDanAnchorActorId,
				snapshot.ShenDanAnchorName,
				snapshot.ShenDanYear);
			XjShenDanRegistry.Register(((BaseSystemData)actor.data).id, snapshot.ShenDanAnchorActorId);
		}

		if (!string.IsNullOrWhiteSpace(snapshot.FaBaoId)
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
	}

	private static void ApplyRespawnAge(ActorData data)
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
		catch
		{
		}

		data.age_overgrowth = RespawnAge;
		TrySetNumericMember(data, "age", RespawnAge);
		TrySetNumericMember(data, "death_time", 0f);
		TrySetNumericMember(data, "time_to_die", 0f);
		TrySetBooleanMember(data, "dead", false);
		TrySetBooleanMember(data, "is_dead", false);
		TrySetBooleanMember(data, "removed", false);
		if (data is BaseSystemData bd)
		{
			bd.set(AgeYearProcessedKey, RespawnAge - 1);
		}
	}

	private static void ResetRuntimeStateForRespawn(ActorData data)
	{
		if (data is not BaseSystemData bd) return;
		bd.set(AgeYearProcessedKey, RespawnAge - 1);
		bd.set("xuanjian.dongtian_active", false);
		bd.set("xuanjian.dongtian_target_id", 0L);
		bd.set("xuanjian.dongtian_cultivate_start_year", 0);
		bd.set("xuanjian.dongtian_cultivate_end_year", 0);
		bd.set("xuanjian.dongtian_cultivate_years", 0);
		bd.set("xuanjian.dongtian_life_granted", false);
		bd.set("xuanjian.dongtian_visual_suppressed", false);
		bd.set("xuanjian.dongtian_anim_container_suppressed", false);
		bd.set("xuanjian.dongtian_temp_peaceful", false);
		bd.set("xuanjian.dongtian_post_peace_active", false);
		bd.set("xuanjian.dongtian_post_peace_end_year", 0);
		bd.set("xuanjian.dongtian_post_peace_broken_by_attack", false);
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
		if (actor?.data == null) return;
		ApplyRespawnAge(actor.data);
		ApplyRespawnName(actor.data, savedName);
		try
		{
			actor.name = ResolveRespawnName(savedName, actor.name);
			actor.data.custom_name = true;
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
		if (target == null || string.IsNullOrWhiteSpace(memberName)) return;
		Type type = target.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo field = type.GetField(memberName, flags);
		if (field != null)
		{
			TrySetConvertedValue(target, field.FieldType, value, v => field.SetValue(target, v));
			return;
		}
		PropertyInfo property = type.GetProperty(memberName, flags);
		if (property?.CanWrite == true && property.GetIndexParameters().Length == 0)
		{
			TrySetConvertedValue(target, property.PropertyType, value, v => property.SetValue(target, v, null));
		}
	}

	private static void TrySetBooleanMember(object target, string memberName, bool value)
	{
		if (target == null || string.IsNullOrWhiteSpace(memberName)) return;
		Type type = target.GetType();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		FieldInfo field = type.GetField(memberName, flags);
		if (field?.FieldType == typeof(bool))
		{
			field.SetValue(target, value);
			return;
		}
		PropertyInfo property = type.GetProperty(memberName, flags);
		if (property?.CanWrite == true && property.PropertyType == typeof(bool) && property.GetIndexParameters().Length == 0)
		{
			property.SetValue(target, value, null);
		}
	}

	private static void TrySetConvertedValue(object target, Type memberType, float value, Action<object> setter)
	{
		try
		{
			if (memberType == typeof(float)) setter(value);
			else if (memberType == typeof(double)) setter((double)value);
			else if (memberType == typeof(int)) setter((int)Math.Round(value));
			else if (memberType == typeof(long)) setter((long)Math.Round(value));
			else if (memberType == typeof(short)) setter((short)Math.Round(value));
			else if (memberType == typeof(byte)) setter((byte)Mathf.Clamp(Mathf.RoundToInt(value), byte.MinValue, byte.MaxValue));
		}
		catch
		{
		}
	}
}
