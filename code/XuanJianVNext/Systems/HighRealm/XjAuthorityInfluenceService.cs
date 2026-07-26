using System;

namespace XuanJianVNext.Systems.HighRealm;

/// <summary>
/// 将道途现存权柄接回求金法参悟：权柄越残缺，求金法越难参悟。
/// 成丹主链只验证角色已真实持有六品主功法，不再反查求金法状态。
/// </summary>
internal static class XjAuthorityInfluenceService
{
	internal static float GetQiuJinComprehensionMultiplier(string daoTu)
	{
		int total = XjGuoWeiAuthorityCatalog.Get(daoTu).Count;
		if (total <= 0)
		{
			return 0f;
		}
		int available = XjGuoWeiQuanBingRegistry.CountAvailableAuthorities(daoTu);
		return Math.Clamp((float)available / total, 0f, 1f);
	}

}
