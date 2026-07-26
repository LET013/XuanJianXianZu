using System;
using System.Collections.Generic;
using System.Reflection;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.LingWu;
using XuanJianVNext.Systems.HighRealm;

namespace XuanJianVNext.Data.Lore;

internal enum XjCanonStatus
{
	ConfirmedCanon = 1,
	InferredCanon = 2,
	ModInterpretation = 3,
	PendingReview = 4
}

internal enum XjCanonReviewState
{
	Reviewed = 1,
	Pending = 2
}

internal static class XjCanonCategory
{
	internal const string DaoTu = "DaoTu";
	internal const string GongFa = "GongFa";
	internal const string XianJi = "XianJi";
	internal const string GuoWei = "GuoWei";
	internal const string QuanBing = "QuanBing";
	internal const string FaBao = "FaBao";
	internal const string DongTian = "DongTian";
}

internal readonly struct XjCanonMetadataRecord
{
	internal readonly bool Found;
	internal readonly string Category;
	internal readonly string Key;
	internal readonly string DisplayName;
	internal readonly XjCanonStatus Status;
	internal readonly XjCanonReviewState ReviewState;
	internal readonly string EvidenceSource;
	internal readonly string DefinitionLocation;
	internal readonly string Note;
	internal readonly bool IsDynamicGenerator;

	internal XjCanonMetadataRecord(
		bool found,
		string category,
		string key,
		string displayName,
		XjCanonStatus status,
		XjCanonReviewState reviewState,
		string evidenceSource,
		string definitionLocation,
		string note,
		bool isDynamicGenerator = false)
	{
		Found = found;
		Category = category ?? string.Empty;
		Key = key ?? string.Empty;
		DisplayName = displayName ?? string.Empty;
		Status = status;
		ReviewState = reviewState;
		EvidenceSource = evidenceSource ?? string.Empty;
		DefinitionLocation = definitionLocation ?? string.Empty;
		Note = note ?? string.Empty;
		IsDynamicGenerator = isDynamicGenerator;
	}
}

internal readonly struct XjCanonMetadataSummary
{
	internal readonly int FixedRecordCount;
	internal readonly int ConfirmedCanonCount;
	internal readonly int InferredCanonCount;
	internal readonly int ModInterpretationCount;
	internal readonly int DynamicGeneratorCount;
	internal readonly int PendingReviewCount;

	internal XjCanonMetadataSummary(
		int fixedRecordCount,
		int confirmedCanonCount,
		int inferredCanonCount,
		int modInterpretationCount,
		int dynamicGeneratorCount,
		int pendingReviewCount)
	{
		FixedRecordCount = fixedRecordCount;
		ConfirmedCanonCount = confirmedCanonCount;
		InferredCanonCount = inferredCanonCount;
		ModInterpretationCount = modInterpretationCount;
		DynamicGeneratorCount = dynamicGeneratorCount;
		PendingReviewCount = pendingReviewCount;
	}
}

internal static class XjCanonMetadataCatalog
{
	private const string EvidenceModDynamicName = "模组动态名称生成系统";
	private static readonly Dictionary<string, XjCanonMetadataRecord> Records =
		new Dictionary<string, XjCanonMetadataRecord>(StringComparer.Ordinal);
	private static bool initialized;

	internal static XjCanonMetadataRecord Resolve(string category, string key, string displayName = "")
	{
		EnsureInitialized();
		string normalizedCategory = NormalizeCategory(category);
		string normalizedKey = NormalizeKey(key);
		if (normalizedCategory.Length == 0)
		{
			normalizedCategory = XjCanonCategory.GongFa;
		}
		if (normalizedKey.Length == 0)
		{
			normalizedKey = NormalizeKey(displayName);
		}
		if (normalizedKey.Length > 0 && Records.TryGetValue(BuildRecordKey(normalizedCategory, normalizedKey), out XjCanonMetadataRecord record))
		{
			return record;
		}

		if (TryResolveGeneratorFallback(normalizedCategory, normalizedKey, displayName, out XjCanonMetadataRecord generated))
		{
			return generated;
		}

		return Pending(normalizedCategory, normalizedKey, displayName, "固定条目未在现有注册表、生成器或设计文档中找到可判定来源。");
	}

	internal static XjCanonMetadataRecord ResolveDaoTu(string daoTu)
	{
		return Resolve(XjCanonCategory.DaoTu, daoTu, daoTu);
	}

	internal static XjCanonMetadataRecord ResolveGongFa(string gongFaName)
	{
		return Resolve(XjCanonCategory.GongFa, gongFaName, gongFaName);
	}

	internal static XjCanonMetadataRecord ResolveXianJi(string xianJiName)
	{
		return Resolve(XjCanonCategory.XianJi, xianJiName, xianJiName);
	}

	internal static XjCanonMetadataRecord ResolveGuoWei(string guoWeiName)
	{
		return Resolve(XjCanonCategory.GuoWei, guoWeiName, guoWeiName);
	}

	internal static XjCanonMetadataRecord ResolveQuanBing(string authorityName)
	{
		return Resolve(XjCanonCategory.QuanBing, authorityName, authorityName);
	}

	internal static XjCanonMetadataRecord ResolveFaBao(string faBaoName)
	{
		return Resolve(XjCanonCategory.FaBao, faBaoName, faBaoName);
	}

	internal static XjCanonMetadataRecord ResolveDongTian(string dongTianName)
	{
		return Resolve(XjCanonCategory.DongTian, dongTianName, dongTianName);
	}

	internal static bool ShouldDisplayInPlayerUi(XjCanonMetadataRecord record)
	{
		return record.Status != XjCanonStatus.PendingReview;
	}

	internal static string FormatStatus(XjCanonStatus status)
	{
		return status switch
		{
			XjCanonStatus.ConfirmedCanon => "原著已证",
			XjCanonStatus.InferredCanon => "原著推定",
			XjCanonStatus.ModInterpretation => "模组演绎",
			_ => "待校核"
		};
	}

	internal static string FormatBracketTag(XjCanonMetadataRecord record)
	{
		return "【" + FormatStatus(record.Status) + "】";
	}

	internal static string FormatColor(XjCanonStatus status)
	{
		return status switch
		{
			XjCanonStatus.ConfirmedCanon => "#A7E08A",
			XjCanonStatus.InferredCanon => "#9CD7FF",
			XjCanonStatus.ModInterpretation => "#FFD37A",
			_ => "#CFC7B2"
		};
	}

	internal static IReadOnlyList<XjCanonMetadataRecord> ReadAllRecords()
	{
		EnsureInitialized();
		List<XjCanonMetadataRecord> result = new List<XjCanonMetadataRecord>(Records.Values);
		result.Sort((left, right) => string.Compare(left.Category + left.Key, right.Category + right.Key, StringComparison.Ordinal));
		return result;
	}

	internal static IReadOnlyList<XjCanonMetadataRecord> ReadManualRecords()
	{
		return ReadAllRecords();
	}

	internal static IReadOnlyList<XjCanonMetadataRecord> ReadPendingReviewRecords()
	{
		EnsureInitialized();
		List<XjCanonMetadataRecord> result = new List<XjCanonMetadataRecord>();
		foreach (XjCanonMetadataRecord record in Records.Values)
		{
			if (record.Status == XjCanonStatus.PendingReview)
			{
				result.Add(record);
			}
		}
		result.Sort((left, right) => string.Compare(left.Category + left.Key, right.Category + right.Key, StringComparison.Ordinal));
		return result;
	}

	internal static XjCanonMetadataSummary ReadSummary()
	{
		EnsureInitialized();
		int fixedCount = 0;
		int confirmed = 0;
		int inferred = 0;
		int mod = 0;
		int dynamic = 0;
		int pending = 0;
		foreach (XjCanonMetadataRecord record in Records.Values)
		{
			if (record.IsDynamicGenerator)
			{
				dynamic++;
			}
			else
			{
				fixedCount++;
			}

			switch (record.Status)
			{
				case XjCanonStatus.ConfirmedCanon:
					confirmed++;
					break;
				case XjCanonStatus.InferredCanon:
					inferred++;
					break;
				case XjCanonStatus.ModInterpretation:
					mod++;
					break;
				default:
					pending++;
					break;
			}
		}

		return new XjCanonMetadataSummary(fixedCount, confirmed, inferred, mod, dynamic, pending);
	}

	private static void EnsureInitialized()
	{
		if (initialized)
		{
			return;
		}

		initialized = true;
		RegisterDynamicGenerators();
		RegisterCaiQiBranches();
		RegisterLingWuDefinitions();
		RegisterFaBaoClasses();
		RegisterJinDanDaoSpells();
		RegisterXianJiPools();
		RegisterGuoWeiAndQuanBing();
		RegisterDongTianStages();
	}

	private static void RegisterDynamicGenerators()
	{
		AddRecord(XjCanonCategory.GongFa, "generated:gongfa", "随机功法名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Data/GongFa/XjGongFaNameLibrary.cs", "由道途词根、通用词根和后缀组合生成，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.GongFa, "generated:caiqifa", "随机采气法名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Data/CaiQi/XjCaiQiFaNameLibrary.cs", "由采气分支词根和后缀组合生成，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.GongFa, "generated:qiu_jin_fa", "随机求金法名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Systems/HighRealm/XjQiuJinFa.cs", "由求金法词根和来源功法组合生成，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.FaBao, "generated:fabao", "随机法宝名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Data/FaBao/XjFaBaoCatalog.cs", "由法宝类别、道途词根和器物词组合生成，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.FaBao, "generated:lingbao", "随机灵宝名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Data/FaBao/XjFaBaoCatalog.cs", "紫府灵宝沿用法宝名称生成系统，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.DongTian, "generated:dongtian", "动态洞天名生成器", XjCanonStatus.ModInterpretation, EvidenceModDynamicName, "Data/DongTian/XjDongTianRecord.cs", "洞天、福地和秘境名称绑定运行时记录，不逐项登记随机结果。", true);
		AddRecord(XjCanonCategory.GuoWei, "generated:guowei", "果位名生成器", XjCanonStatus.InferredCanon, "XjGuoWeiCalculator 按道途与正位/闰位/余位体系组合生成", "Systems/HighRealm/XjGuoWeiCalculator.cs", "果位名由既有道途和位格规则推导，不复制每个运行时果位实例。", true);
	}

	private static void RegisterCaiQiBranches()
	{
		for (int i = 0; i < XjCaiQiCatalog.Entries.Length; i++)
		{
			XjCaiQiCatalogEntry entry = XjCaiQiCatalog.Entries[i];
			AddRecord(
				XjCanonCategory.DaoTu,
				entry.DisplayName,
				entry.DisplayName,
				XjCanonStatus.InferredCanon,
				"XjCaiQiCatalog 固定采气分支，含旧 trait/resource 映射；按项目既有九大道途与十二炁体系推定",
				"Data/CaiQi/XjCaiQiDefinitions.cs",
				"只记录道途/采气分支稳定显示名，不复制采气产物和旧资源字段。");
		}
	}

	private static void RegisterLingWuDefinitions()
	{
		Dictionary<string, XjLingWuDef> byId = ReadPrivateDictionary<string, XjLingWuDef>(typeof(XjLingWuCatalog), "ById");
		foreach (KeyValuePair<string, XjLingWuDef> pair in byId)
		{
			if (pair.Value == null)
			{
				continue;
			}

			AddRecord(
				XjCanonCategory.FaBao,
				pair.Key,
				pair.Value.Name,
				XjCanonStatus.ModInterpretation,
				"XjLingWuCatalog 固定灵物目录；说明文本为模组内道途适配描述",
				"Data/LingWu/XjLingWuDefinitions.cs",
				"登记灵物 id 到正典状态的映射，不复制图片、描述和效果。");
			AddRecord(
				XjCanonCategory.FaBao,
				pair.Value.Name,
				pair.Value.Name,
				XjCanonStatus.ModInterpretation,
				"XjLingWuCatalog 固定灵物目录；说明文本为模组内道途适配描述",
				"Data/LingWu/XjLingWuDefinitions.cs",
				"显示名别名，指向同一灵物来源。");
		}
	}

	private static void RegisterFaBaoClasses()
	{
		AddRecord(XjCanonCategory.FaBao, XjFaBaoCatalog.ZhuJiFaQiClass, XjFaBaoCatalog.ZhuJiFaQiClass, XjCanonStatus.ModInterpretation, "XjFaBaoCatalog 法宝阶类与 WorldBox 数值适配", "Data/FaBao/XjFaBaoCatalog.cs", "只登记阶类标签，不复制加成曲线。");
		AddRecord(XjCanonCategory.FaBao, XjFaBaoCatalog.ZiFuLingBaoClass, XjFaBaoCatalog.ZiFuLingBaoClass, XjCanonStatus.ModInterpretation, "XjFaBaoCatalog 法宝阶类与 WorldBox 数值适配", "Data/FaBao/XjFaBaoCatalog.cs", "只登记阶类标签，不复制加成曲线。");
		AddRecord(XjCanonCategory.FaBao, XjFaBaoCatalog.JinDanFaBaoClass, XjFaBaoCatalog.JinDanFaBaoClass, XjCanonStatus.ModInterpretation, "XjFaBaoCatalog 法宝阶类与 WorldBox 数值适配", "Data/FaBao/XjFaBaoCatalog.cs", "只登记阶类标签，不复制加成曲线。");
	}

	private static void RegisterJinDanDaoSpells()
	{
		for (int i = 0; i < XjJinDanDaoSpellCatalog.All.Count; i++)
		{
			XjJinDanDaoSpellDefinition spell = XjJinDanDaoSpellCatalog.All[i];
			AddRecord(
				XjCanonCategory.GongFa,
				spell.Id,
				spell.DisplayName,
				XjCanonStatus.ModInterpretation,
				"XjJinDanDaoSpellCatalog 金丹道术战斗目录；含冷却、范围、动画和 WorldBox 结算字段",
				"Data/HighRealm/XjJinDanDaoSpellCatalog.cs",
				"登记道术 id 到正典状态，不复制战斗参数。");
			AddRecord(
				XjCanonCategory.GongFa,
				spell.DisplayName,
				spell.DisplayName,
				XjCanonStatus.ModInterpretation,
				"XjJinDanDaoSpellCatalog 金丹道术战斗目录；含冷却、范围、动画和 WorldBox 结算字段",
				"Data/HighRealm/XjJinDanDaoSpellCatalog.cs",
				"显示名别名，指向同一道术来源。");
		}
	}

	private static void RegisterXianJiPools()
	{
		RegisterXianJiDictionary("DocumentUpperXianJiMap", XjCanonStatus.InferredCanon, "XjXianJiCatalog 文档上品仙基池；项目已有文档池记录但未附外部页码，按原著体系推定");
		RegisterXianJiDictionary("DocumentLowerXianJiMap", XjCanonStatus.InferredCanon, "XjXianJiCatalog 文档下品仙基池；项目已有文档池记录但未附外部页码，按原著体系推定");
		RegisterXianJiDictionary("DaoTuXianJiMap", XjCanonStatus.ModInterpretation, "XjXianJiCatalog 运行固定仙基池；未进入文档池的补足项按模组演绎处理");
		RegisterXianJiDictionary("LowDaoTuXianJiMap", XjCanonStatus.ModInterpretation, "XjXianJiCatalog 运行下品仙基池；未进入文档池的补足项按模组演绎处理");
	}

	private static void RegisterXianJiDictionary(string fieldName, XjCanonStatus status, string evidence)
	{
		Dictionary<string, string[]> map = ReadPrivateDictionary<string, string[]>(typeof(XjXianJiCatalog), fieldName);
		foreach (KeyValuePair<string, string[]> pair in map)
		{
			string[] values = pair.Value ?? Array.Empty<string>();
			for (int i = 0; i < values.Length; i++)
			{
				string xianJi = NormalizeKey(values[i]);
				if (xianJi.Length == 0)
				{
					continue;
				}

				AddRecord(
					XjCanonCategory.XianJi,
					xianJi,
					xianJi,
					status,
					evidence,
					"Systems/HighRealm/XjXianJiCatalog.cs",
					"只登记仙基稳定名和来源池，不复制选择权重或相邻道途规则。");
			}
		}
	}

	private static void RegisterGuoWeiAndQuanBing()
	{
		IReadOnlyList<string> daoTus = XjGuoWeiAuthorityCatalog.GetAllDaoTus();
		for (int i = 0; i < daoTus.Count; i++)
		{
			string daoTu = daoTus[i];
			AddRecord(XjCanonCategory.GuoWei, XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, XjGuoWeiCalculator.ZhengWei, 1), XjGuoWeiCalculator.BuildGuoWeiSlotName(daoTu, XjGuoWeiCalculator.ZhengWei, 1), XjCanonStatus.InferredCanon, "XjGuoWeiCalculator 按道途与正位规则生成", "Systems/HighRealm/XjGuoWeiCalculator.cs", "固定道途正位显示名；闰位/余位多槽实例由 generated:guowei 统一处理。");

			IReadOnlyList<string> authorities = XjGuoWeiAuthorityCatalog.Get(daoTu);
			for (int j = 0; j < authorities.Count; j++)
			{
				string authority = NormalizeKey(authorities[j]);
				if (authority.Length == 0)
				{
					continue;
				}

				AddRecord(
					XjCanonCategory.QuanBing,
					authority,
					authority,
					XjCanonStatus.ModInterpretation,
					"XjGuoWeiAuthorityCatalog 固定权柄目录；服务果位战斗和权柄争夺系统",
					"Systems/HighRealm/XjGuoWeiAuthorityCatalog.cs",
					"只登记权柄名和来源目录，不复制争夺、流失或战斗规则。");
			}
		}
	}

	private static void RegisterDongTianStages()
	{
		AddRecord(XjCanonCategory.DongTian, "SecretRealmStage:Fudi", "福地", XjCanonStatus.ModInterpretation, "XjSecretRealmStage 固定阶段；服务宗门秘境工程", "Data/DongTian/XjSecretRealmData.cs", "阶段标签，不复制宗门工程状态。");
		AddRecord(XjCanonCategory.DongTian, "SecretRealmStage:Dongtian", "洞天", XjCanonStatus.ModInterpretation, "XjSecretRealmStage 固定阶段；服务宗门秘境工程", "Data/DongTian/XjSecretRealmData.cs", "阶段标签，不复制宗门工程状态。");
		AddRecord(XjCanonCategory.DongTian, "SecretRealmStage:SecretRealm", "秘境", XjCanonStatus.ModInterpretation, "XjSecretRealmStage 固定阶段；服务宗门秘境工程", "Data/DongTian/XjSecretRealmData.cs", "秘境显示类别，不复制宗门工程状态。");
	}

	private static bool TryResolveGeneratorFallback(string category, string key, string displayName, out XjCanonMetadataRecord record)
	{
		string generatorKey = string.Empty;
		if (string.Equals(category, XjCanonCategory.GongFa, StringComparison.Ordinal))
		{
			string text = key + displayName;
			if (text.IndexOf("采气", StringComparison.Ordinal) >= 0)
			{
				generatorKey = "generated:caiqifa";
			}
			else if (text.IndexOf("求金", StringComparison.Ordinal) >= 0)
			{
				generatorKey = "generated:qiu_jin_fa";
			}
			else
			{
				generatorKey = "generated:gongfa";
			}
		}
		else if (string.Equals(category, XjCanonCategory.FaBao, StringComparison.Ordinal))
		{
			string text = key + displayName;
			generatorKey = text.IndexOf("灵宝", StringComparison.Ordinal) >= 0
				? "generated:lingbao"
				: "generated:fabao";
		}
		else if (string.Equals(category, XjCanonCategory.DongTian, StringComparison.Ordinal))
		{
			generatorKey = "generated:dongtian";
		}
		else if (string.Equals(category, XjCanonCategory.GuoWei, StringComparison.Ordinal))
		{
			generatorKey = "generated:guowei";
		}

		if (generatorKey.Length == 0 || !Records.TryGetValue(BuildRecordKey(category, generatorKey), out XjCanonMetadataRecord generator))
		{
			record = default;
			return false;
		}

		record = new XjCanonMetadataRecord(
			true,
			category,
			NormalizeKey(key).Length == 0 ? generatorKey : NormalizeKey(key),
			string.IsNullOrWhiteSpace(displayName) ? NormalizeKey(key) : displayName.Trim(),
			generator.Status,
			generator.ReviewState,
			generator.EvidenceSource,
			generator.DefinitionLocation,
			generator.Note,
			true);
		return true;
	}

	private static void AddRecord(
		string category,
		string key,
		string displayName,
		XjCanonStatus status,
		string evidenceSource,
		string definitionLocation,
		string note,
		bool isDynamicGenerator = false)
	{
		string normalizedCategory = NormalizeCategory(category);
		string normalizedKey = NormalizeKey(key);
		if (normalizedCategory.Length == 0 || normalizedKey.Length == 0)
		{
			return;
		}

		XjCanonMetadataRecord record = new XjCanonMetadataRecord(
			true,
			normalizedCategory,
			normalizedKey,
			string.IsNullOrWhiteSpace(displayName) ? normalizedKey : displayName.Trim(),
			status,
			status == XjCanonStatus.PendingReview ? XjCanonReviewState.Pending : XjCanonReviewState.Reviewed,
			evidenceSource,
			definitionLocation,
			note,
			isDynamicGenerator);
		string recordKey = BuildRecordKey(normalizedCategory, normalizedKey);
		if (!Records.TryGetValue(recordKey, out XjCanonMetadataRecord existing)
			|| StatusRank(record.Status) < StatusRank(existing.Status))
		{
			Records[recordKey] = record;
		}
	}

	private static XjCanonMetadataRecord Pending(string category, string key, string displayName, string reason)
	{
		return new XjCanonMetadataRecord(
			false,
			category,
			key,
			string.IsNullOrWhiteSpace(displayName) ? key : displayName.Trim(),
			XjCanonStatus.PendingReview,
			XjCanonReviewState.Pending,
			"未能从现有代码注册表、注释或项目文档自动判定",
			string.Empty,
			reason);
	}

	private static int StatusRank(XjCanonStatus status)
	{
		return status switch
		{
			XjCanonStatus.ConfirmedCanon => 0,
			XjCanonStatus.InferredCanon => 1,
			XjCanonStatus.ModInterpretation => 2,
			_ => 3
		};
	}

	private static Dictionary<TKey, TValue> ReadPrivateDictionary<TKey, TValue>(Type type, string fieldName)
	{
		FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
		return field != null && field.GetValue(null) is Dictionary<TKey, TValue> dictionary
			? dictionary
			: new Dictionary<TKey, TValue>();
	}

	private static string BuildRecordKey(string category, string key)
	{
		return NormalizeCategory(category) + "|" + NormalizeKey(key);
	}

	private static string NormalizeCategory(string category)
	{
		string value = (category ?? string.Empty).Trim();
		if (string.Equals(value, XjCanonCategory.DaoTu, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.DaoTu;
		if (string.Equals(value, XjCanonCategory.GongFa, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.GongFa;
		if (string.Equals(value, XjCanonCategory.XianJi, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.XianJi;
		if (string.Equals(value, XjCanonCategory.GuoWei, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.GuoWei;
		if (string.Equals(value, XjCanonCategory.QuanBing, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.QuanBing;
		if (string.Equals(value, XjCanonCategory.FaBao, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.FaBao;
		if (string.Equals(value, XjCanonCategory.DongTian, StringComparison.OrdinalIgnoreCase)) return XjCanonCategory.DongTian;
		return value;
	}

	private static string NormalizeKey(string key)
	{
		return (key ?? string.Empty).Trim();
	}
}
