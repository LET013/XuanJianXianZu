using XuanJianVNext.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.Family;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.UI.Family;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Data.QianKunDai;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.UI.ZongMen;
using XuanJianVNext.Systems.QianKunDai;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.XianGuo;

namespace XuanJianVNext.UI.ActorInfo;

internal readonly partial struct XjActorInfoReadModel
{		private static string ResolveDisplayDaoTu(
			Actor actor,
			string snapshotDaoTu,
			in XjGongFaState gongFa,
			in XjJinDanState jinDan,
			in XjGuoWeiQuanBingState quanBing)
		{
			_ = quanBing;
			if (actor?.data != null && jinDan.Found
				&& string.Equals(XjGuoWeiRegistry.ResolveTypeFromName(jinDan.GuoWei),
					XjGuoWeiCalculator.RunWei, StringComparison.Ordinal))
			{
				XjHighRealmDaoStateService.ResolvePositionIdentity(
					actor, jinDan.GuoWei, out string sourceDaoTu, out string manifestDaoTu);
				_ = sourceDaoTu;
				string manifest = NormalizeDaoTuForDisplay(manifestDaoTu);
				if (!string.IsNullOrWhiteSpace(manifest))
				{
					// 闰位已经显化到目标道途后，人物道途就是显化道途本身。
					// “太阴闰厥阴”一类结构只属于求位来源信息，不再污染当前道途展示。
					return XjXianGuoSystem.ResolveDaoTuDisplay(actor, manifest);
				}
			}

			string daoTu = NormalizeDaoTuForDisplay(snapshotDaoTu);
			if (!string.IsNullOrWhiteSpace(daoTu)) return XjXianGuoSystem.ResolveDaoTuDisplay(actor, daoTu);
			string fallback = gongFa.Found ? NormalizeDaoTuForDisplay(gongFa.DaoTu) : string.Empty;
			return XjXianGuoSystem.ResolveDaoTuDisplay(actor, fallback);
		}

		private static string ResolveMechanicsDaoTu(string displayDaoTu)
		{
			string value = NormalizeDaoTuForDisplay(displayDaoTu);
			if (string.IsNullOrWhiteSpace(value)) return string.Empty;
			int marker = value.LastIndexOf('闰');
			return marker >= 0 && marker + 1 < value.Length
				? value.Substring(marker + 1).Trim()
				: value;
		}

		private static string NormalizeDaoTuForDisplay(string value)
		{
			string text = XjDisplayNameSanitizer.GameTerm(Normalize(value), string.Empty);
			return IsInvalidDaoTuAlias(text) ? string.Empty : text;
		}

		private static bool IsInvalidDaoTuAlias(string value)
		{
			string text = Normalize(value);
			return string.Equals(text, "玄门", StringComparison.Ordinal)
				|| string.Equals(text, "基础", StringComparison.Ordinal)
				|| string.Equals(text, "无道途", StringComparison.Ordinal);
		}


		private static string GetAptitudeDisplay(int xjZz)
		{
			return xjZz switch
			{
				1 => "朽木难雕",
				2 => "可琢之材",
				3 => "璞玉之资",
				4 => "上乘根骨",
				5 => "天公垂目",
				6 => "天授道脉",
				_ => "未定"
			};
		}

		private static string GetAptitudeOverlayDisplay(int overlayMask)
		{
			System.Collections.Generic.List<string> items = new System.Collections.Generic.List<string>();
			if ((overlayMask & (1 << 7)) != 0)
			{
				items.Add("先天道体");
			}
	
			if ((overlayMask & (1 << 8)) != 0)
			{
				items.Add("经脉堵塞");
			}
	
			if ((overlayMask & (1 << 9)) != 0)
			{
				items.Add("气血衰败");
			}
	
			return items.Count == 0 ? "无" : string.Join("、", items);
		}

		private static string GetChuShenDisplay(Actor actor)
		{
			if (actor?.data == null)
			{
				return "未定出身";
			}
	
			XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShen, out int chuShen);
			XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetInt(actor, XjActorDataKeys.ChuShenSpecial, out int special);
			string baseDisplay = chuShen switch
			{
				1 => "凡俗出身",
				2 => "家族弟子",
				3 => "门派弟子",
				4 => "宗门传承",
				5 => "家学传承",
				_ => "未定出身"
			};
			string specialDisplay = special switch
			{
				6 => "真人转世",
				7 => "真君重修",
				8 => "道胎之姿",
				_ => string.Empty
			};
			return string.IsNullOrWhiteSpace(specialDisplay) ? baseDisplay : baseDisplay + " - " + specialDisplay;
		}

		private static float ReadIntegerFloat(Actor actor, string key, float defaultValue)
		{
			return XuanJianVNext.Systems.ActorSystem.XjActorAccessor.TryGetFloat(actor, key, out float value)
				? (float)Math.Floor(Math.Max(0f, value))
				: (float)Math.Floor(Math.Max(0f, defaultValue));
		}


		private static string Normalize(string value)
		{
			return XjStringHelper.Normalize(value);
		}
}

