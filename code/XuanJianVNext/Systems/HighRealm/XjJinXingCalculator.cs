using System;
using UnityEngine;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.HighRealm;
using XuanJianVNext.Systems.FaBao;
using XuanJianVNext.Data.GongFa;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Systems.Cultivation;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.CaiQi;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.ZongMen;
using XuanJianVNext.Systems.Era;
using XuanJianVNext.Systems.LongShu;
using XuanJianVNext.Systems.Talisman;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Systems.Combat;

namespace XuanJianVNext.Systems.HighRealm;
internal static class XjJinXingCalculator
{
	private static readonly string[] XuanHuanWords =
	{
		"混元", "太初", "太玄", "太始", "太易", "太清", "玉清", "上清", "虚无", "混沌",
		"金庭", "玉虚", "紫霄", "青冥", "玄黄", "鸿蒙", "乾元", "坤元",
		"震雷", "巽风", "艮山", "兑泽", "太极", "两仪", "四象", "八卦", "九宫", "十方",
		"洞真", "玄府", "上元", "中元", "下元", "玉宸", "天成", "妙有", "无极", "重明",
		"高上", "圣为", "浚命", "覆苍", "玄钧", "泰和", "灵台", "元景", "真华", "道枢",
		"云篆", "丹书", "神霄", "清微", "明堂", "帝阙", "玄都", "紫府", "青阳", "白藏"
	};

	private static readonly string[] DaoTuTokens =
	{
		"太阳", "少阳", "明阳", "太阴", "少阴", "厥阴",
		"兑金", "库金", "庚金", "齐金", "逍金",
		"正木", "集木", "角木", "更木", "保木",
		"坎水", "渌水", "合水", "府水", "牝水",
		"离火", "灴火", "并火", "真火", "牡火",
		"艮土", "戊土", "归土", "宝土", "宣土", "青宣",
		"清炁", "邃炁", "紫炁", "真炁", "寒炁", "晞炁", "瑞炁", "煞炁", "谪炁", "华炁", "上仪", "下仪",
		"玄雷", "霄雷", "元雷"
	};

	internal static string Calculate(string daoTu, long actorId)
	{
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(normalizedDaoTu)
			|| !XjDaoTuVisibleTraitCatalog.TryResolveTraitId(normalizedDaoTu, out _))
		{
			return string.Empty;
		}
		if (string.Equals(normalizedDaoTu, "青宣", StringComparison.Ordinal))
		{
			return "青堰神岳伏元性";
		}

		int firstIndex = XjDeterministicHash.PositiveIndex(actorId, normalizedDaoTu, XuanHuanWords.Length);
		int secondIndex = XjDeterministicHash.PositiveIndex(actorId + 17L, normalizedDaoTu + "|jinxing", XuanHuanWords.Length);
		if (secondIndex == firstIndex && XuanHuanWords.Length > 1)
		{
			secondIndex = (secondIndex + 1) % XuanHuanWords.Length;
		}

		return XuanHuanWords[firstIndex] + normalizedDaoTu + XuanHuanWords[secondIndex] + "性";
	}

	internal static bool NeedsDaoTuNormalization(string jinXing, string daoTu)
	{
		string text = (jinXing ?? string.Empty).Trim();
		string normalizedDaoTu = (daoTu ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(normalizedDaoTu)) return false;
		if (string.Equals(normalizedDaoTu, "青宣", StringComparison.Ordinal)) return !string.Equals(text, "青堰神岳伏元性", StringComparison.Ordinal);

		for (int i = 0; i < DaoTuTokens.Length; i++)
		{
			string token = DaoTuTokens[i];
			if (!string.Equals(token, normalizedDaoTu, StringComparison.Ordinal) && text.Contains(token, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return !text.Contains(normalizedDaoTu, StringComparison.Ordinal);
	}
}

