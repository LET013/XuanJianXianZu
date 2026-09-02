using System;
using System.ComponentModel;
using Newtonsoft.Json;

namespace XuanJianVNext.Data.History;

internal static class XjThreeBookSchema
{
	internal const int CurrentVersion = 1;
}

internal static class XjThreeBookEventTypes
{
	internal const string PersonalBirth = "PersonalBirth";
	internal const string PersonalAptitude = "PersonalAptitude";
	internal const string PersonalHalfYaoBloodline = "PersonalHalfYaoBloodline";
	internal const string PersonalCultivationQualified = "PersonalCultivationQualified";
	internal const string PersonalRealmBreakthrough = "PersonalRealmBreakthrough";
	internal const string PersonalDongTianJourney = "PersonalDongTianJourney";
	internal const string PersonalDongTianDeath = "PersonalDongTianDeath";
	internal const string PersonalGongFaObtained = "PersonalGongFaObtained";
	internal const string PersonalQiuJinFa = "PersonalQiuJinFa";
	internal const string PersonalFaBaoObtained = "PersonalFaBaoObtained";
	internal const string PersonalLingWuObtained = "PersonalLingWuObtained";
	internal const string PersonalMentorAccepted = "PersonalMentorAccepted";
	internal const string PersonalStudentAccepted = "PersonalStudentAccepted";
	internal const string PersonalSectFounded = "PersonalSectFounded";
	internal const string PersonalWeaponArt = "PersonalWeaponArt";
	internal const string PersonalFuQiCultivationPhase = "PersonalFuQiCultivationPhase";
	internal const string PersonalFuQiIntentStudy = "PersonalFuQiIntentStudy";
	internal const string PersonalFuQiHuangGuan = "PersonalFuQiHuangGuan";
	internal const string PersonalFuQiZhenRen = "PersonalFuQiZhenRen";
	internal const string PersonalFuQiShenMiaoPerfected = "PersonalFuQiShenMiaoPerfected";
	internal const string PersonalFuQiJinDanFailure = "PersonalFuQiJinDanFailure";
	internal const string PersonalFuQiZhenJunYuShi = "PersonalFuQiZhenJunYuShi";
	internal const string PersonalFuQiLongGengSuccession = "PersonalFuQiLongGengSuccession";
	internal const string PersonalFuQiToZiFu = "PersonalFuQiToZiFu";
	internal const string PersonalSectTournament = "PersonalSectTournament";
	internal const string PersonalDeath = "PersonalDeath";
	internal const string PersonalAcquaintance = "PersonalAcquaintance";
	internal const string PersonalCloseFriend = "PersonalCloseFriend";
	internal const string PersonalDaoCompanion = "PersonalDaoCompanion";
	internal const string PersonalRareCraft = "PersonalRareCraft";
	internal const string PersonalShenTongComprehended = "PersonalShenTongComprehended";
	internal const string PersonalOtherShenTongDeceived = "PersonalOtherShenTongDeceived";
	internal const string PersonalOtherShenTongDeceiver = "PersonalOtherShenTongDeceiver";
	internal const string PersonalJieLinSucceeded = "PersonalJieLinSucceeded";
	internal const string PersonalYuYiSucceeded = "PersonalYuYiSucceeded";
	internal const string PersonalJinDanReincarnation = "PersonalJinDanReincarnation";
	internal const string PersonalJinDanDaoObstructed = "PersonalJinDanDaoObstructed";
	internal const string PersonalJinDanDaoObstructor = "PersonalJinDanDaoObstructor";
	internal const string PersonalCraftAbility = "PersonalCraftAbility";
	internal const string PersonalAlchemyRecipe = "PersonalAlchemyRecipe";
	internal const string PersonalQuanBingSeized = "PersonalQuanBingSeized";
	internal const string PersonalQuanBingIntegrated = "PersonalQuanBingIntegrated";
	internal const string PersonalZhengWeiSuccession = "PersonalZhengWeiSuccession";
	internal const string PersonalShiEntered = "PersonalShiEntered";
	internal const string PersonalShiBaseline = "PersonalShiBaseline";
	internal const string PersonalShiConversion = "PersonalShiConversion";
	internal const string PersonalShiRealmChanged = "PersonalShiRealmChanged";
	internal const string PersonalShiDharmaFormStage = "PersonalShiDharmaFormStage";
	internal const string PersonalShiJinDiObtained = "PersonalShiJinDiObtained";
	internal const string PersonalShiReincarnation = "PersonalShiReincarnation";
	internal const string PersonalShiTrueSpiritReturn = "PersonalShiTrueSpiritReturn";
	internal const string PersonalShiTrueSpiritAnnihilated = "PersonalShiTrueSpiritAnnihilated";
	internal const string PersonalShiWorldHonored = "PersonalShiWorldHonored";
	internal const string PersonalShiPreaching = "PersonalShiPreaching";
	internal const string PersonalShiPosition = "PersonalShiPosition";
	internal const string PersonalShiHighRealmVictory = "PersonalShiHighRealmVictory";
	internal const string PersonalShiJinDiAbsorbed = "PersonalShiJinDiAbsorbed";
	internal const string PersonalShiAncientDuhua = "PersonalShiAncientDuhua";
	internal const string PersonalShiReturnToVoid = "PersonalShiReturnToVoid";

	internal const string FamilyFounded = "FamilyFounded";
	internal const string FamilyBloodlineBirth = "FamilyBloodlineBirth";
	internal const string FamilyTalentEmerged = "FamilyTalentEmerged";
	internal const string FamilyCultivatorEmerged = "FamilyCultivatorEmerged";
	internal const string FamilyHighRealmEmerged = "FamilyHighRealmEmerged";
	internal const string FamilySupportSelected = "FamilySupportSelected";
	internal const string FamilySupportGranted = "FamilySupportGranted";
	internal const string FamilyInheritanceAdded = "FamilyInheritanceAdded";
	internal const string FamilyTreasureAdded = "FamilyTreasureAdded";
	internal const string FamilySectFounded = "FamilySectFounded";
	internal const string FamilyMemberAchievement = "FamilyMemberAchievement";
	internal const string FamilyStageChanged = "FamilyStageChanged";
	internal const string FamilySurnameChanged = "FamilySurnameChanged";
	internal const string FamilyMemberDeath = "FamilyMemberDeath";
	internal const string FamilyMemberMerit = "FamilyMemberMerit";
	internal const string FamilyDiscipline = "FamilyDiscipline";
	internal const string FamilyMentorshipLegacy = "FamilyMentorshipLegacy";
	internal const string FamilyRareCraft = "FamilyRareCraft";
	internal const string FamilyGuZunDissolved = "FamilyGuZunDissolved";
	internal const string FamilyShiAchievement = "FamilyShiAchievement";
	internal const string FamilyShiExpansion = "FamilyShiExpansion";

	internal const string SectFounded = "SectFounded";
	internal const string SectRenamed = "SectRenamed";
	internal const string SectEnrollment = "SectEnrollment";
	internal const string SectLecture = "SectLecture";
	internal const string SectTournament = "SectTournament";
	internal const string SectSecretRealmQualification = "SectSecretRealmQualification";
	internal const string SectPeakMasterChanged = "SectPeakMasterChanged";
	internal const string SectSovereignChanged = "SectSovereignChanged";
	internal const string SectInheritanceAdded = "SectInheritanceAdded";
	internal const string SectResourceChanged = "SectResourceChanged";
	internal const string SectRelationChanged = "SectRelationChanged";
	internal const string SectWarResult = "SectWarResult";
	internal const string SectHighRealmEmerged = "SectHighRealmEmerged";
	internal const string SectExtinct = "SectExtinct";
	internal const string SectFriendlyRelation = "SectFriendlyRelation";
	internal const string SectAlliance = "SectAlliance";
	internal const string SectResourceMilestone = "SectResourceMilestone";
	internal const string SectRareCraft = "SectRareCraft";
	internal const string SectGuZunDissolved = "SectGuZunDissolved";
	internal const string SectShiAchievement = "SectShiAchievement";
	internal const string SectShiExpansion = "SectShiExpansion";
}

/// <summary>
/// 三书的独立持久事件。正文在业务事实投影时一次生成，UI不得再从天下公告改写。
/// </summary>
internal sealed class XjThreeBookArchiveRecord
{
	public int SchemaVersion { get; set; } = XjThreeBookSchema.CurrentVersion;
	public long BookEventId { get; set; }
	public long SortSequence { get; set; }
	public string SourceFactId { get; set; } = string.Empty;
	public long SubjectId { get; set; }
	public string SubjectNameSnapshot { get; set; } = string.Empty;
	public int Year { get; set; }
	public string EventType { get; set; } = string.Empty;
	public string Category { get; set; } = XjWorldHistoryCategory.World;
	public string Tag { get; set; } = string.Empty;
	public string Title { get; set; } = string.Empty;
	public string Body { get; set; } = string.Empty;
	public string IconId { get; set; } = string.Empty;
	public int Importance { get; set; } = 1;
	public bool IsProtected { get; set; }
	public string Result { get; set; } = XjHistoryResult.None;

	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public long RelatedActorId { get; set; }
	public string RelatedActorName { get; set; } = string.Empty;
	public long FamilyId { get; set; }
	public string FamilyNameSnapshot { get; set; } = string.Empty;
	public long RelatedFamilyId { get; set; }
	public string RelatedFamilyNameSnapshot { get; set; } = string.Empty;
	public long SectId { get; set; }
	public string SectNameSnapshot { get; set; } = string.Empty;
	public long RelatedSectId { get; set; }
	public string RelatedSectNameSnapshot { get; set; } = string.Empty;
	public long CityId { get; set; }
	public string CityNameSnapshot { get; set; } = string.Empty;
	public int LocationX { get; set; }
	public int LocationY { get; set; }
	public bool HasLocation { get; set; }
}


/// <summary>
/// 已处理业务事实的紧凑账本。显示条目因容量裁剪后，账本仍负责阻止同一事实重新写入。
/// </summary>
internal sealed class XjThreeBookSourceFactRecord
{
	[JsonProperty("h")] public long SourceFactHash { get; set; }
	[JsonProperty("y", DefaultValueHandling = DefaultValueHandling.Ignore)] public int Year { get; set; }
	[JsonProperty("s", DefaultValueHandling = DefaultValueHandling.Ignore)] public long SubjectId { get; set; }
	[DefaultValue("")]
	[JsonProperty("e", DefaultValueHandling = DefaultValueHandling.Ignore)] public string EventType { get; set; } = string.Empty;
}

/// <summary>
/// 家族身份尚处于 Pending 时暂存的领域事实。只在家族确认后补写世家纪事，
/// 不重复触发仓库、天下纪事或其他玩法系统。
/// </summary>
internal sealed class XjThreeBookDeferredFamilyFactRecord
{
	public string DeferredId { get; set; } = string.Empty;
	public int QueuedYear { get; set; }
	public int Attempts { get; set; }
	public bool Found { get; set; }
	public string EventType { get; set; } = string.Empty;
	public long ActorId { get; set; }
	public string ActorName { get; set; } = string.Empty;
	public long FamilyStableId { get; set; }
	public string FamilyKey { get; set; } = string.Empty;
	public long ZongMenId { get; set; }
	public string ZongMenName { get; set; } = string.Empty;
	public int Year { get; set; }
	public string Source { get; set; } = string.Empty;
	public string RealmId { get; set; } = string.Empty;
	public string GongFaName { get; set; } = string.Empty;
	public int GongFaGrade { get; set; }
	public string QiuJinFaName { get; set; } = string.Empty;
	public string CaiQiResourceId { get; set; } = string.Empty;
	public int CaiQiAmount { get; set; }
	public string CaiQiFaName { get; set; } = string.Empty;
	public string CaiQiFaSourcePlace { get; set; } = string.Empty;
	public string FaBaoId { get; set; } = string.Empty;
	public string FaBaoName { get; set; } = string.Empty;
	public string FaBaoClass { get; set; } = string.Empty;
	public string DaoTu { get; set; } = string.Empty;
	public string GuoWei { get; set; } = string.Empty;
	public string MappedXianJi { get; set; } = string.Empty;
	public string BoundAuthority { get; set; } = string.Empty;
}
