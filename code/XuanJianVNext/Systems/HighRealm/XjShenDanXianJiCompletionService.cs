using System;
using XuanJianVNext.Core;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 神丹的最小神通结构修复。
/// 正常求金挂靠神丹天然已经具备五门神通；道胎余位炼神丹、手动补录或异常恢复
/// 可能绕过自然求金前置。神丹既然已经作为金丹等效高境运行，就必须补足到五门，
/// 但补足只使用当前道途上位神通，不伪造求金法、自身果位或正常金丹成功记录。
/// </summary>
internal static class XjShenDanXianJiCompletionService
{
	internal static bool EnsureFiveShenTong(Actor actor, string daoTu, int currentYear, string source, out bool complete)
	{
		complete = false;
		if (actor?.data == null
			|| !XjCultivationPathRules.IsZiFuJinDan(actor)
			|| !string.Equals(XjRealmHelper.NormalizeId(XjRealmHelper.GetUnifiedId(actor, XjRealmHelper.GetTraitSnapshotForRouter)), XjRealmIds.ShenDan, StringComparison.Ordinal))
		{
			return false;
		}

		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedDaoTu))
		{
			XjActorAccessor.TryGetString(actor, XjActorDataKeys.DaoTu, out normalizedDaoTu);
			normalizedDaoTu = (normalizedDaoTu ?? string.Empty).Trim();
		}
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)) return false;

		long actorId = ((BaseSystemData)actor.data).id;
		int year = Math.Max(1, currentYear);
		string normalizedSource = string.IsNullOrWhiteSpace(source) ? "神丹神通补全" : source.Trim();
		bool changed = false;
		XjXianJiState state = XjXianJiAccessor.BuildState(actor);
		int guard = 0;
		while (state.Count < XjXianJiState.MaxCount && guard++ < XjXianJiState.MaxCount * 2)
		{
			int ordinal = state.Count + 1;
			if (!XjXianJiCatalog.TryPickUpperForProgression(
				normalizedDaoTu,
				ordinal,
				actorId + year * 131L + ordinal * 7919L,
				state.Ids,
				out string pickedId)
				|| string.IsNullOrWhiteSpace(pickedId)
				|| !XjXianJiAccessor.Add(actor, pickedId, year, string.Empty, normalizedSource))
			{
				break;
			}
			changed = true;
			state = XjXianJiAccessor.BuildState(actor);
		}

		complete = state.Count >= XjXianJiState.MaxCount;
		return changed;
	}
}
