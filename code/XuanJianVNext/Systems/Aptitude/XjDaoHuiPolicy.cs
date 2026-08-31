using System;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;

namespace XuanJianVNext.Systems.Aptitude;

/// <summary>
/// 道慧唯一量纲策略。旧档仍沿用 HuiGuang 存档键，但所有新业务统一按 0-100 读取。
/// </summary>
internal static class XjDaoHuiPolicy
{
	internal const float Maximum = 100f;
	internal const float OrdinaryGrowthCeiling = 90f;
	internal const float RareGrowthCeiling = 95f;
	internal const float WorldGrowthCeiling = 100f;

	internal const float UnderstandPositionThreshold = 70f;

	// 0.9.9.3 位序难度重排：果位必须是三类位序中最难直接求证的一层。
	// 余/闰是本道的派生席位，不再反过来承担接近“空证新道”的高道慧门槛。
	// 已经被前人创出的余/闰只需稳定承继；首次创证依次为余 < 近邻闰 < 结构远闰 < 五现难位 < 果位。
	internal const float StableInheritanceThreshold = 70f;
	internal const float DeriveResidualThreshold = 72f;
	internal const float OpenIntercalaryThreshold = 75f;
	internal const float StructuredRemoteThreshold = 78f;
	internal const float DifficultPositionThreshold = 82f;
	internal const float FruitPositionThreshold = 85f;
	internal const float PositionRenamingThreshold = 90f;
	internal const float FuQiMaxTrueSpiritThreshold = 90f;
	// 95 只保留给真正“空证新道”类玩法；普通闰位不再借此把无关系外道硬解释成闰位。
	internal const float CompleteEmptyProofThreshold = 95f;

	internal static float Clamp(float value)
	{
		return Math.Clamp((float)Math.Floor(Math.Max(0f, value)), 0f, Maximum);
	}

	internal static float Normalize01(float value)
	{
		return Math.Clamp(Clamp(value) / Maximum, 0f, 1f);
	}

	internal static float Add(float current, float delta, float sourceCeiling)
	{
		float ceiling = Math.Clamp(sourceCeiling, 0f, Maximum);
		return Math.Min(ceiling, Clamp(current) + Math.Max(0f, delta));
	}

	internal static bool TryGetAptitudeRange(int xjZz, out int minimum, out int maximum)
	{
		(minimum, maximum) = xjZz switch
		{
			1 => (16, 30),
			2 => (28, 45),
			3 => (42, 60),
			4 => (58, 75),
			5 => (75, 90),
			6 => (88, 100),
			_ => (0, 0)
		};
		return maximum > 0;
	}

	internal static float Read(Actor actor)
	{
		if (actor?.data == null
			|| !XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value))
		{
			return 0f;
		}
		return ApplyDaoTuRealmModifier(actor, Clamp(value));
	}

	/// <summary>
	/// 并火的紫府金丹道以“越进境、越焚慧”为代价：境界越高有效道慧越低。
	/// 这里只修正运行期有效值，不反写角色先天/成长道慧；同时将最大损失压在约12%，
	/// 避免高境并火因为自身道性直接跌穿原本的资质层级。
	/// </summary>
	internal static float ApplyDaoTuRealmModifier(Actor actor, float value)
	{
		float normalizedValue = Clamp(value);
		if (actor?.data == null || !XjCultivationPathRules.IsZiFuJinDan(actor))
		{
			return normalizedValue;
		}

		if (!XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out string daoTu)
			|| !string.Equals((daoTu ?? string.Empty).Trim(), "并火", StringComparison.Ordinal))
		{
			return normalizedValue;
		}

		XjActorAccessor.TryGetString(actor, XjActorDataKeys.RealmId, out string realmId);
		string realm = XjRealmHelper.NormalizeId(realmId);
		float fixedPenalty = realm switch
		{
			XjRealmIds.LianQi => 1f,
			XjRealmIds.ZhuJi => 2f,
			XjRealmIds.ZiFu => 4f,
			XjRealmIds.JinDan => 7f,
			XjRealmIds.ShenDan => 8f,
			XjRealmIds.DaoTai => 10f,
			_ => 0f
		};
		if (fixedPenalty <= 0f)
		{
			return normalizedValue;
		}

		float floor = normalizedValue * 0.88f;
		return Clamp(Math.Max(floor, normalizedValue - fixedPenalty));
	}

	internal static void NormalizeStoredValue(Actor actor)
	{
		if (actor?.data == null)
		{
			return;
		}
		XjActorAccessor.TryGetFloat(actor, XjActorDataKeys.HuiGuang, out float value);
		float normalized = Clamp(value);
		if (Math.Abs(normalized - value) > 0.001f)
		{
			XjActorAccessor.SetFloat(actor, XjActorDataKeys.HuiGuang, normalized);
		}
	}
}
