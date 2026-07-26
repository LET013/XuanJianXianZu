using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Sect;
using XuanJianVNext.Data.Formation;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Data.History;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Archive;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Systems.Formation;
using XuanJianVNext.Systems.History;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Sect;

internal static partial class XjSectRepository
{		private static XjCityFamilyGovernanceArchiveRecord Clone(XjCityFamilyGovernanceArchiveRecord source)
		{
			return new XjCityFamilyGovernanceArchiveRecord
			{
				SchemaVersion = source.SchemaVersion,
				CityId = source.CityId,
				SectId = source.SectId,
				GoverningFamilyId = source.GoverningFamilyId,
				ConfirmedYear = source.ConfirmedYear,
				LastChallengeYear = source.LastChallengeYear,
				ChallengerFamilyId = source.ChallengerFamilyId,
				ChallengeStartYear = source.ChallengeStartYear,
				ChallengeConsecutiveYears = source.ChallengeConsecutiveYears,
				IncumbentControlScore = source.IncumbentControlScore,
				ChallengerControlScore = source.ChallengerControlScore,
				State = source.State ?? "Stable"
			};
		}

		private static XjSectFamilySeatArchiveRecord Clone(XjSectFamilySeatArchiveRecord source)
		{
			return new XjSectFamilySeatArchiveRecord
			{
				SchemaVersion = source.SchemaVersion,
				SectId = source.SectId,
				FamilyId = source.FamilyId,
				State = source.State ?? XjSectFamilySeatState.Formal,
				JoinedYear = source.JoinedYear,
				GoverningCityCount = source.GoverningCityCount,
				StrengthScore = source.StrengthScore,
				TerritoryScore = source.TerritoryScore,
				ContributionScore = source.ContributionScore,
				CraftScore = source.CraftScore,
				OfficeHistoryScore = source.OfficeHistoryScore,
				VoiceScore = source.VoiceScore,
				VoiceTier = source.VoiceTier ?? XjSectVoiceTier.Ordinary,
				LastContributionYear = source.LastContributionYear,
				LastPublishedYear = source.LastPublishedYear,
				SupplyDebt = source.SupplyDebt,
				Responsibility = source.Responsibility ?? string.Empty,
				PrivilegeHeat = source.PrivilegeHeat,
				PrivilegeIncidentCount = source.PrivilegeIncidentCount,
				LastPrivilegeIncidentYear = source.LastPrivilegeIncidentYear
			};
		}
}

