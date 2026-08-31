using XuanJianVNext.Data.Archive;

namespace XuanJianVNext.Systems.Archive;

internal static class XjWorldSchemaGuard
{
	private const int CurrentArchiveVersion = 126;
	internal const int MinimumSupportedV1ArchiveVersion = 116;
	internal static bool GameplayEnabled { get; private set; }
	internal static bool UnsupportedLegacyWorld { get; private set; }
	internal static int LoadedVersion { get; private set; }

	internal static bool AcceptArchive(XjWorldArchiveData archive)
	{
		int version = archive?.Version ?? 0;
		LoadedVersion = version;
		if (version > CurrentArchiveVersion)
		{
			GameplayEnabled = false;
			UnsupportedLegacyWorld = true;
			UnityEngine.Debug.LogError(
				"[玄鉴1.0] 检测到更高版本世界归档(version=" + version
				+ ", current=" + CurrentArchiveVersion
				+ ")。为避免旧版代码覆盖未来字段，已阻止玩法写入；请使用创建该存档的相同或更高版本玄鉴仙族。");
			return false;
		}

		if (version >= MinimumSupportedV1ArchiveVersion)
		{
			GameplayEnabled = true;
			UnsupportedLegacyWorld = false;
			return true;
		}

		GameplayEnabled = false;
		UnsupportedLegacyWorld = true;
		UnityEngine.Debug.LogError(
			"[玄鉴1.0] 检测到旧世界归档(version=" + version
			+ ")。1.0仅支持0.8.2后统一归档，请创建新世界；已阻止半迁移与玩法写入。");
		return false;
	}

	internal static void MarkNewWorld()
	{
		LoadedVersion = CurrentArchiveVersion;
		GameplayEnabled = true;
		UnsupportedLegacyWorld = false;
	}

	internal static void RejectMissingArchive(int worldYear)
	{
		LoadedVersion = 0;
		GameplayEnabled = false;
		UnsupportedLegacyWorld = true;
		UnityEngine.Debug.LogError(
			"[玄鉴1.0] 当前世界没有可迁移的1.0统一归档，但世界年份已到"
			+ worldYear
			+ "年。为避免把旧世界或损坏归档半迁移为新档，已阻止玩法写入；请使用新世界开始1.0。");
	}

	internal static void Clear()
	{
		LoadedVersion = 0;
		GameplayEnabled = false;
		UnsupportedLegacyWorld = false;
	}
}
