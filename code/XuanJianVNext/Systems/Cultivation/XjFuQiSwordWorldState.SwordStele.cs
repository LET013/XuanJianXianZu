using System;
using XuanJianVNext.Systems.Archive;

namespace XuanJianVNext.Systems.Cultivation;

internal static partial class XjFuQiSwordWorldState
{
	internal static bool HasSwordStele => _state.Established && _state.SwordSteleCreated;
	internal static long SwordSteleCityId => _state.SwordSteleCityId;
	internal static string SwordSteleCityName => _state.SwordSteleCityName ?? string.Empty;
	internal static string SwordSteleInscription => _state.SwordSteleInscription ?? string.Empty;

	internal static bool IsFoundingActor(Actor actor)
	{
		return _state.Established
			&& actor?.data != null
			&& ((BaseSystemData)actor.data).id == _state.FounderActorId;
	}

	internal static bool TryCreateSwordStele(Actor founder, int currentYear, string inscription)
	{
		if (!_state.Established || _state.SwordSteleCreated || founder?.data == null || currentYear <= 0)
		{
			return false;
		}
		long actorId = ((BaseSystemData)founder.data).id;
		if (actorId <= 0L || actorId != _state.FounderActorId) return false;
		long cityId = founder.city?.data?.id ?? 0L;
		if (cityId <= 0L) return false;
		string cityName = string.Empty;
		try { cityName = ((BaseSystemData)founder.city.data).name ?? string.Empty; } catch (System.Exception xjCaught31) { XuanJianVNext.Core.XjExceptionDiagnostics.Report("code/XuanJianVNext/Systems/Cultivation/XjFuQiSwordWorldState.SwordStele.cs:31", xjCaught31); }
		_state.SwordSteleCreated = true;
		_state.SwordSteleCreatedYear = currentYear;
		_state.SwordSteleFounderActorId = actorId;
		_state.SwordSteleFounderName = SafeActorName(founder);
		_state.SwordSteleCityId = cityId;
		_state.SwordSteleCityName = cityName;
		_state.SwordSteleInscription = (inscription ?? string.Empty).Trim();
		_state.SwordSteleInsightCount = Math.Max(0, _state.SwordSteleInsightCount);
		XjWorldArchiveSystem.MarkChanged();
		XjWorldArchiveSystem.RequestProtectedCommit();
		return true;
	}

	internal static bool IsActorAtSwordStele(Actor actor)
	{
		return HasSwordStele
			&& actor?.data != null
			&& actor.city?.data != null
			&& actor.city.data.id == _state.SwordSteleCityId;
	}

	internal static void RecordSwordSteleInsight()
	{
		if (!HasSwordStele) return;
		_state.SwordSteleInsightCount = Math.Max(0, _state.SwordSteleInsightCount) + 1;
		XjWorldArchiveSystem.MarkChanged();
	}
}
