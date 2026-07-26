using System.Globalization;

namespace XuanJianVNext.Data.GongFa;

internal static class XjGongFaGradeText
{
	internal static string Format(int grade)
	{
		int normalizedGrade = Normalize(grade);
		return normalizedGrade switch
		{
			1 => "一品",
			2 => "二品",
			3 => "三品",
			4 => "四品",
			5 => "五品",
			6 => "六品",
			_ => normalizedGrade > 0 ? normalizedGrade.ToString(CultureInfo.InvariantCulture) + "品" : "未定品"
		};
	}

	internal static int Normalize(int grade)
	{
		if (grade <= 0)
		{
			return 0;
		}

		return grade > XjGongFaDefinition.MaxGrade ? XjGongFaDefinition.MaxGrade : grade;
	}
}
