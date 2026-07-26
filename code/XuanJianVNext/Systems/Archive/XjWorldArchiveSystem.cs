using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Archive;

namespace XuanJianVNext.Systems.Archive;

/// <summary>
/// 世界级玄鉴归档。读取与写入均必须落到 WorldBox 实际参与存档的
/// custom-data 容器，而不是依赖单一版本的 MapBox.data 字段布局。
/// </summary>
internal static class XjWorldArchiveSystem
{
	private const string ArchiveKey = "xuanjian.vnext.archive.v1";
	private const string ArchiveBackupKey = "xuanjian.vnext.archive.v1.backup";
	private const float NormalCommitDelaySeconds = 30f;
	// A world archive contains every family, warehouse and long-lived record.  It
	// must be coalesced aggressively: serialising it on the main thread after
	// every protected event was the largest avoidable allocation spike at 20x+.
	// Save snapshots still force an immediate commit below, so this only changes
	// background persistence latency, never save correctness.
	private const float ProtectedCommitDelaySeconds = 6f;
	private const float MinimumCommitIntervalSeconds = 8f;

	private static readonly string[] DirectFieldCandidates =
	{
		"data", "_data", "world_data", "_world_data", "map_data", "_map_data", "save_data", "_save_data"
	};

	private static readonly string[] DirectPropertyCandidates =
	{
		"data", "world_data", "map_data", "save_data"
	};

	private static readonly string[][] NestedMemberPathCandidates =
	{
		new[] { "map_stats", "custom_data" },
		new[] { "mapStats", "custom_data" },
		new[] { "stats", "custom_data" },
		new[] { "map_stats", "data" },
		new[] { "mapStats", "data" },
		new[] { "stats", "data" }
	};

	private static readonly string[] SetterMethodCandidates =
	{
		"set", "Set", "setString", "SetString", "setValue", "SetValue", "set_value", "SetValueByKey", "add", "Add"
	};

	private static readonly string[] GetterMethodCandidates =
	{
		"get", "Get", "getString", "GetString", "getValue", "GetValue", "get_value", "GetValueByKey"
	};

	private static XjWorldArchiveData loadedArchiveSnapshot;
	private static string lastKnownGoodRaw = string.Empty;
	private static long pendingWriteRevision;
	private static long lastWrittenRevision;
	private static bool urgentCommitRequested;
	private static float firstDirtyAt = -1f;
	private static float protectedRequestedAt = -1f;
	private static float lastCommitAt = -1000f;
	private static bool hasLoadedReadOnly;
	private static int unresolvedContainerAttempts;

	private static MapBox cachedWorld;
	private static int cachedWorldSeedId = int.MinValue;
	private static object cachedWorldData;
	private static Type cachedWorldDataType;
	private static MethodInfo cachedGetStringMethod;
	private static MethodInfo cachedSetStringMethod;
	private static bool hasLoggedReadFailure;
	private static bool hasLoggedWriteFailure;
	private static bool hasLoggedCommitFailure;
	private static bool hasLoggedAccessorFailure;

	internal static bool HasPendingWrite => urgentCommitRequested || pendingWriteRevision > lastWrittenRevision;
	internal static bool HasProtectedCommitRequest => urgentCommitRequested;
	internal static bool HasLoadedArchive => hasLoadedReadOnly;
	internal static bool IsBackgroundCommitDue => IsCommitDue(UnityEngine.Time.unscaledTime);

	internal static void MarkChanged()
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if (pendingWriteRevision <= lastWrittenRevision && firstDirtyAt < 0f)
		{
			firstDirtyAt = UnityEngine.Time.unscaledTime;
		}
		pendingWriteRevision++;
	}

	internal static void RequestProtectedCommit()
	{
		if (!XjWorldSchemaGuard.GameplayEnabled) return;
		if (!urgentCommitRequested)
		{
			protectedRequestedAt = UnityEngine.Time.unscaledTime;
		}
		urgentCommitRequested = true;
	}

	internal static void LoadReadOnly()
	{
		LoadReadOnly(allowInitializeEmpty: false);
	}

	private static void LoadReadOnly(bool allowInitializeEmpty)
	{
		EnsureWorldIdentity();
		if (hasLoadedReadOnly)
		{
			return;
		}

		if (!TryGetWorldDataObject(out object dataObject, createIfMissing: false))
		{
			// WorldBox 的自定义数据容器在部分版本中会晚于 finishingUpLoading
			// 初始化。普通读取阶段只能等待，绝不因超时创建空容器；仅在
			// SaveManager 正式生成世界快照时，才允许为真正的新档初始化。
			unresolvedContainerAttempts++;
			if (!allowInitializeEmpty
				|| !TryGetWorldDataObject(out dataObject, createIfMissing: true))
			{
				return;
			}
		}
		unresolvedContainerAttempts = 0;

		bool primaryRead = TryReadWorldString(dataObject, ArchiveKey, out string primaryRaw);
		bool backupRead = TryReadWorldString(dataObject, ArchiveBackupKey, out string backupRaw);
		if (!primaryRead && !backupRead)
		{
			return;
		}

		XjWorldArchiveData primaryData = null;
		XjWorldArchiveData backupData = null;
		bool primaryValid = !string.IsNullOrWhiteSpace(primaryRaw)
			&& XjWorldArchiveCodec.TryDecode(primaryRaw, out primaryData);
		bool backupValid = !string.IsNullOrWhiteSpace(backupRaw)
			&& XjWorldArchiveCodec.TryDecode(backupRaw, out backupData);
		if (!primaryValid && !backupValid)
		{
			// 两项都明确为空才是新世界。若存在非空但损坏的文本，则继续保持
			// 可重试，并保留日志，避免把坏读结果覆盖成新档。
			if (string.IsNullOrWhiteSpace(primaryRaw) && string.IsNullOrWhiteSpace(backupRaw))
			{
				hasLoadedReadOnly = true;
				loadedArchiveSnapshot = null;
				lastKnownGoodRaw = string.Empty;
				XjWorldSchemaGuard.MarkNewWorld();
				lastWrittenRevision = pendingWriteRevision;
			}
			else if (!hasLoggedReadFailure)
			{
				hasLoggedReadFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 主归档与备份归档均无法解码，已阻止空档覆盖。\n");
			}
			return;
		}

		XjWorldArchiveData data = primaryValid ? primaryData : backupData;
		if (primaryValid && backupValid)
		{
			data = XjWorldArchiveFamilyRetention.MergeProtectedFamilyData(backupData, data);
			data = XjWorldArchiveHighRealmRetention.MergeProtectedHighRealmData(backupData, data);
		}

		if (!XjWorldSchemaGuard.AcceptArchive(data))
		{
			loadedArchiveSnapshot = data;
			lastKnownGoodRaw = primaryValid ? primaryRaw : backupRaw;
			lastWrittenRevision = pendingWriteRevision;
			hasLoadedReadOnly = true;
			return;
		}

		XjWorldArchiveMemory.ImportToMemory(data);
		loadedArchiveSnapshot = data;
		lastKnownGoodRaw = XjWorldArchiveCodec.Encode(data);
		lastWrittenRevision = pendingWriteRevision;
		hasLoadedReadOnly = true;
	}

	internal static void Tick(int budget)
	{
		if (budget <= 0)
		{
			return;
		}

		LoadReadOnly();
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}
		if (pendingWriteRevision <= lastWrittenRevision)
		{
			urgentCommitRequested = false;
			firstDirtyAt = -1f;
			protectedRequestedAt = -1f;
			return;
		}

		if (!IsCommitDue(UnityEngine.Time.unscaledTime))
		{
			return;
		}

		TryCommitCurrentMemory();
	}

	internal static void Clear()
	{
		pendingWriteRevision = 0L;
		lastWrittenRevision = 0L;
		urgentCommitRequested = false;
		firstDirtyAt = -1f;
		protectedRequestedAt = -1f;
		lastCommitAt = -1000f;
		hasLoadedReadOnly = false;
		loadedArchiveSnapshot = null;
		lastKnownGoodRaw = string.Empty;
		unresolvedContainerAttempts = 0;
		cachedWorld = null;
		cachedWorldSeedId = int.MinValue;
		ClearAccessorCache();
		ResetDiagnostics();
		XjWorldSchemaGuard.Clear();
	}

	internal static void CommitPendingBeforeWorldSnapshot()
	{
		// SaveManager 已进入正式世界快照阶段，此时可以确认迟到的加载已结束；
		// 若仍无 custom_data，按新档初始化，避免玩家刚进世界立即保存时漏档。
		LoadReadOnly(allowInitializeEmpty: true);
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return;
		}
		// 游戏存档前始终导出完整内存快照。即使某条重建路径漏标 dirty，
		// 也不能让不完整运行时状态覆盖已保存的家族历史。
		TryCommitCurrentMemory();
	}

	private static bool IsCommitDue(float now)
	{
		if (!HasPendingWrite || pendingWriteRevision <= lastWrittenRevision)
		{
			return false;
		}

		float minimumInterval = urgentCommitRequested
			? ResolveProtectedCommitIntervalSeconds()
			: ResolveMinimumCommitIntervalSeconds();
		if (now - lastCommitAt < minimumInterval)
		{
			return false;
		}

		if (urgentCommitRequested)
		{
			float requestedAt = protectedRequestedAt >= 0f ? protectedRequestedAt : now;
			return now - requestedAt >= ResolveProtectedCommitIntervalSeconds();
		}

		float dirtyAt = firstDirtyAt >= 0f ? firstDirtyAt : now;
		return now - dirtyAt >= ResolveNormalCommitDelaySeconds();
	}

	private static float ResolveNormalCommitDelaySeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 90f,
			XjRuntimeStressTier.Severe => 60f,
			XjRuntimeStressTier.Mild => 45f,
			_ => NormalCommitDelaySeconds
		};
	}

	private static float ResolveProtectedCommitIntervalSeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 30f,
			XjRuntimeStressTier.Severe => 20f,
			XjRuntimeStressTier.Mild => 12f,
			_ => ProtectedCommitDelaySeconds
		};
	}

	private static float ResolveMinimumCommitIntervalSeconds()
	{
		return XjRuntimeWorkBudget.StressTier switch
		{
			XjRuntimeStressTier.Critical => 45f,
			XjRuntimeStressTier.Severe => 30f,
			XjRuntimeStressTier.Mild => 15f,
			_ => MinimumCommitIntervalSeconds
		};
	}

	private static bool TryCommitCurrentMemory()
	{
		if (!hasLoadedReadOnly || !XjWorldSchemaGuard.GameplayEnabled)
		{
			return false;
		}

		try
		{
			XjWorldArchiveData current = XjWorldArchiveMemory.ExportFromMemory();
			current = XjWorldArchiveFamilyRetention.MergeProtectedFamilyData(loadedArchiveSnapshot, current);
			current = XjWorldArchiveHighRealmRetention.MergeProtectedHighRealmData(loadedArchiveSnapshot, current);
			string raw = XjWorldArchiveCodec.Encode(current);

			// 先写备份，再写主档；两次写入都要求回读逐字一致。
			if (!string.IsNullOrWhiteSpace(lastKnownGoodRaw)
				&& !TrySetWorldString(ArchiveBackupKey, lastKnownGoodRaw))
			{
				return false;
			}
			if (!TrySetWorldString(ArchiveKey, raw))
			{
				return false;
			}
			if (string.IsNullOrWhiteSpace(lastKnownGoodRaw)
				&& !TrySetWorldString(ArchiveBackupKey, raw))
			{
				return false;
			}

			loadedArchiveSnapshot = current;
			lastKnownGoodRaw = raw;
			lastWrittenRevision = pendingWriteRevision;
			urgentCommitRequested = false;
			firstDirtyAt = -1f;
			protectedRequestedAt = -1f;
			lastCommitAt = UnityEngine.Time.unscaledTime;
			hasLoadedReadOnly = true;
			return true;
		}
		catch (Exception ex)
		{
			if (!hasLoggedCommitFailure)
			{
				hasLoggedCommitFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 导出或编码完整归档失败，已保留上一次成功档：" + ex);
			}
			return false;
		}
	}

	private static int ResolveWorldYearSafe()
	{
		try
		{
			return Math.Max(0, World.world?.map_stats?.year ?? 0);
		}
		catch
		{
			return 0;
		}
	}

	private static bool TryReadWorldString(object dataObject, string key, out string value)
	{
		value = string.Empty;
		if (dataObject == null || string.IsNullOrWhiteSpace(key))
		{
			return false;
		}

		try
		{
			if (!EnsureDataAccessors(dataObject) || cachedGetStringMethod == null)
			{
				return false;
			}

			object[] args = BuildGetterArguments(cachedGetStringMethod, key.Trim());
			object result = cachedGetStringMethod.Invoke(dataObject, args);
			value = args.Length > 1 ? (args[1] as string ?? string.Empty) : string.Empty;
			// bool=false 表示 key 不存在，但 accessor 本身工作正常。
			return cachedGetStringMethod.ReturnType != typeof(bool)
				|| result is not bool found
				|| found
				|| string.IsNullOrWhiteSpace(value);
		}
		catch (Exception ex)
		{
			if (!hasLoggedReadFailure)
			{
				hasLoggedReadFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 读取世界归档字段失败：" + ex);
			}
			return false;
		}
	}

	private static bool TrySetWorldString(string key, string value)
	{
		if (string.IsNullOrWhiteSpace(key)
			|| !TryGetWorldDataObject(out object dataObject, createIfMissing: true))
		{
			return false;
		}

		try
		{
			if (!EnsureDataAccessors(dataObject) || cachedSetStringMethod == null)
			{
				return false;
			}

			string payload = value ?? string.Empty;
			object[] args = BuildSetterArguments(cachedSetStringMethod, key.Trim(), payload);
			object result = cachedSetStringMethod.Invoke(dataObject, args);
			if (cachedSetStringMethod.ReturnType == typeof(bool) && result is bool written && !written)
			{
				return false;
			}

			// 写入必须回读校验。旧实现只检查 Invoke 成功，实际上可能写入了
			// 不参与世界存档的临时容器，正是纪事读档消失的主要风险。
			return TryReadWorldString(dataObject, key, out string verify)
				&& string.Equals(payload, verify ?? string.Empty, StringComparison.Ordinal);
		}
		catch (Exception ex)
		{
			if (!hasLoggedWriteFailure)
			{
				hasLoggedWriteFailure = true;
				UnityEngine.Debug.LogError("[玄鉴][存档] 写入世界归档字段失败：" + ex);
			}
			return false;
		}
	}

	private static bool TryGetWorldDataObject(out object dataObject, bool createIfMissing)
	{
		EnsureWorldIdentity();
		dataObject = null;
		MapBox world = World.world;
		if ((UnityEngine.Object)(object)world == null)
		{
			return false;
		}

		if (ReferenceEquals(cachedWorld, world) && cachedWorldData != null
			&& EnsureDataAccessors(cachedWorldData))
		{
			dataObject = cachedWorldData;
			return true;
		}

		// 当前 WorldBox/Guigu 的权威世界存档容器。优先使用强类型路径，
		// 反射候选仅保留给旧版或改名版本兜底。
		try
		{
			MapStats mapStats = world.map_stats;
			if (mapStats != null)
			{
				if (mapStats.custom_data == null && createIfMissing)
				{
					mapStats.custom_data = new SaveCustomData();
				}
				if (IsWorldDataObject(mapStats.custom_data))
				{
					CacheWorldData(world, mapStats.custom_data);
					dataObject = cachedWorldData;
					return true;
				}
			}
		}
		catch
		{
		}

		for (int i = 0; i < NestedMemberPathCandidates.Length; i++)
		{
			if (TryResolveNestedPersistentDataObject(
				world,
				NestedMemberPathCandidates[i],
				createIfMissing,
				out object nested))
			{
				CacheWorldData(world, nested);
				dataObject = cachedWorldData;
				return true;
			}
		}

		Type worldType = world.GetType();
		for (int i = 0; i < DirectFieldCandidates.Length; i++)
		{
			FieldInfo field = FindFieldInHierarchy(worldType, DirectFieldCandidates[i]);
			if (field == null)
			{
				continue;
			}

			object candidate = field.GetValue(world);
			if (IsWorldDataObject(candidate))
			{
				CacheWorldData(world, candidate);
				dataObject = cachedWorldData;
				return true;
			}
		}

		for (int i = 0; i < DirectPropertyCandidates.Length; i++)
		{
			PropertyInfo property = FindPropertyInHierarchy(worldType, DirectPropertyCandidates[i]);
			if (property == null || !property.CanRead)
			{
				continue;
			}

			object candidate = property.GetValue(world, null);
			if (IsWorldDataObject(candidate))
			{
				CacheWorldData(world, candidate);
				dataObject = cachedWorldData;
				return true;
			}
		}

		if (!hasLoggedAccessorFailure)
		{
			hasLoggedAccessorFailure = true;
			UnityEngine.Debug.LogWarning("[玄鉴][存档] 尚未找到可读写的世界持久化容器，将在后续帧重试。");
		}
		return false;
	}

	private static void EnsureWorldIdentity()
	{
		MapBox world = World.world;
		int seedId;
		try
		{
			seedId = world == null ? int.MinValue : MapBox.current_world_seed_id;
		}
		catch
		{
			seedId = int.MinValue;
		}

		if (ReferenceEquals(cachedWorld, world) && cachedWorldSeedId == seedId)
		{
			return;
		}

		cachedWorld = world;
		cachedWorldSeedId = seedId;
		hasLoadedReadOnly = false;
		loadedArchiveSnapshot = null;
		lastKnownGoodRaw = string.Empty;
		unresolvedContainerAttempts = 0;
		lastWrittenRevision = 0L;
		ClearAccessorCache(keepWorldReference: true);
		ResetDiagnostics();
	}

	private static void CacheWorldData(MapBox world, object dataObject)
	{
		cachedWorld = world;
		cachedWorldData = dataObject;
		cachedWorldDataType = null;
		cachedGetStringMethod = null;
		cachedSetStringMethod = null;
		EnsureDataAccessors(dataObject);
	}

	private static void ClearAccessorCache(bool keepWorldReference = false)
	{
		cachedWorldData = null;
		cachedWorldDataType = null;
		cachedGetStringMethod = null;
		cachedSetStringMethod = null;
		if (!keepWorldReference)
		{
			cachedWorld = null;
			cachedWorldSeedId = int.MinValue;
		}
	}

	private static void ResetDiagnostics()
	{
		hasLoggedReadFailure = false;
		hasLoggedWriteFailure = false;
		hasLoggedCommitFailure = false;
		hasLoggedAccessorFailure = false;
	}

	private static bool EnsureDataAccessors(object dataObject)
	{
		if (dataObject == null)
		{
			return false;
		}

		Type dataType = dataObject.GetType();
		if (cachedWorldDataType == dataType
			&& cachedGetStringMethod != null
			&& cachedSetStringMethod != null)
		{
			return true;
		}

		cachedWorldDataType = dataType;
		MethodInfo[] methods = dataType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		cachedSetStringMethod = FindSetterMethod(methods);
		cachedGetStringMethod = FindGetterMethod(methods);
		return cachedSetStringMethod != null && cachedGetStringMethod != null;
	}

	private static MethodInfo FindSetterMethod(IReadOnlyList<MethodInfo> methods)
	{
		for (int nameIndex = 0; nameIndex < SetterMethodCandidates.Length; nameIndex++)
		{
			for (int i = 0; i < methods.Count; i++)
			{
				MethodInfo method = methods[i];
				if (!string.Equals(method.Name, SetterMethodCandidates[nameIndex], StringComparison.Ordinal))
				{
					continue;
				}

				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length < 2
					|| parameters[0].ParameterType != typeof(string)
					|| parameters[1].ParameterType != typeof(string)
					|| !TrailingParametersAreOptional(parameters))
				{
					continue;
				}
				return method;
			}
		}
		return null;
	}

	private static MethodInfo FindGetterMethod(IReadOnlyList<MethodInfo> methods)
	{
		for (int nameIndex = 0; nameIndex < GetterMethodCandidates.Length; nameIndex++)
		{
			for (int i = 0; i < methods.Count; i++)
			{
				MethodInfo method = methods[i];
				if (!string.Equals(method.Name, GetterMethodCandidates[nameIndex], StringComparison.Ordinal))
				{
					continue;
				}

				ParameterInfo[] parameters = method.GetParameters();
				if (parameters.Length < 2
					|| parameters[0].ParameterType != typeof(string)
					|| !parameters[1].ParameterType.IsByRef
					|| parameters[1].ParameterType.GetElementType() != typeof(string)
					|| !TrailingParametersAreOptional(parameters))
				{
					continue;
				}
				return method;
			}
		}
		return null;
	}

	private static bool TrailingParametersAreOptional(IReadOnlyList<ParameterInfo> parameters)
	{
		for (int i = 2; i < parameters.Count; i++)
		{
			if (parameters[i].ParameterType.IsByRef
				|| (!parameters[i].IsOptional && !parameters[i].HasDefaultValue))
			{
				return false;
			}
		}
		return true;
	}

	private static object[] BuildSetterArguments(MethodInfo method, string key, string value)
	{
		ParameterInfo[] parameters = method.GetParameters();
		object[] args = new object[parameters.Length];
		args[0] = key;
		args[1] = value;
		for (int i = 2; i < parameters.Length; i++)
		{
			args[i] = ResolveDefaultArgument(parameters[i]);
		}
		return args;
	}

	private static object[] BuildGetterArguments(MethodInfo method, string key)
	{
		ParameterInfo[] parameters = method.GetParameters();
		object[] args = new object[parameters.Length];
		args[0] = key;
		args[1] = string.Empty;
		for (int i = 2; i < parameters.Length; i++)
		{
			args[i] = ResolveDefaultArgument(parameters[i]);
		}
		return args;
	}

	private static object ResolveDefaultArgument(ParameterInfo parameter)
	{
		if (parameter.HasDefaultValue)
		{
			return parameter.DefaultValue;
		}
		if (parameter.IsOptional)
		{
			return Type.Missing;
		}
		Type type = parameter.ParameterType;
		if (type == typeof(string)) return string.Empty;
		if (type == typeof(bool)) return false;
		if (type.IsEnum)
		{
			Array values = Enum.GetValues(type);
			return values.Length > 0 ? values.GetValue(0) : Activator.CreateInstance(type);
		}
		return type.IsValueType ? Activator.CreateInstance(type) : null;
	}

	private static bool IsWorldDataObject(object candidate)
	{
		if (candidate == null)
		{
			return false;
		}
		MethodInfo[] methods = candidate.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return FindSetterMethod(methods) != null && FindGetterMethod(methods) != null;
	}

	private static bool TryResolveNestedPersistentDataObject(
		object root,
		string[] path,
		bool createIfMissing,
		out object dataObject)
	{
		dataObject = null;
		if (root == null || path == null || path.Length == 0)
		{
			return false;
		}

		object current = root;
		for (int i = 0; i < path.Length; i++)
		{
			if (current == null
				|| !TryGetMemberValue(current, path[i], out object next, out MemberInfo member))
			{
				return false;
			}

			if (next == null && i == path.Length - 1 && createIfMissing)
			{
				TryInstantiateMember(current, member, out next);
			}
			current = next;
		}

		if (!IsWorldDataObject(current))
		{
			return false;
		}

		dataObject = current;
		return true;
	}

	private static bool TryGetMemberValue(object instance, string memberName, out object value, out MemberInfo member)
	{
		value = null;
		member = null;
		Type type = instance.GetType();
		FieldInfo field = FindFieldInHierarchy(type, memberName);
		if (field != null)
		{
			member = field;
			value = field.GetValue(instance);
			return true;
		}
		PropertyInfo property = FindPropertyInHierarchy(type, memberName);
		if (property != null && property.CanRead)
		{
			member = property;
			value = property.GetValue(instance, null);
			return true;
		}
		return false;
	}

	private static bool TryInstantiateMember(object instance, MemberInfo member, out object created)
	{
		created = null;
		Type memberType = member switch
		{
			FieldInfo field => field.FieldType,
			PropertyInfo property => property.PropertyType,
			_ => null
		};
		if (memberType == null || memberType.IsAbstract || memberType.IsInterface)
		{
			return false;
		}

		try
		{
			created = Activator.CreateInstance(memberType);
			if (member is FieldInfo field)
			{
				field.SetValue(instance, created);
				return true;
			}
			if (member is PropertyInfo property && property.CanWrite)
			{
				property.SetValue(instance, created, null);
				return true;
			}
		}
		catch
		{
		}
		created = null;
		return false;
	}

	private static FieldInfo FindFieldInHierarchy(Type type, string name)
	{
		for (Type current = type; current != null; current = current.BaseType)
		{
			FieldInfo field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (field != null) return field;
		}
		return null;
	}

	private static PropertyInfo FindPropertyInHierarchy(Type type, string name)
	{
		for (Type current = type; current != null; current = current.BaseType)
		{
			PropertyInfo property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
			if (property != null) return property;
		}
		return null;
	}
}
