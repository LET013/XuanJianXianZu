using System;
using XuanJianVNext.Data.History;

namespace XuanJianVNext.Systems.History.Books;

internal static partial class XjThreeBookWriter
{
	internal static void RecordFamilySurnameChanged(
		long familyId,
		string oldFamilyName,
		string newFamilyName,
		string previousSurname,
		string nextSurname,
		int year,
		long actorId,
		string actorName)
	{
		if (familyId <= 0L || string.IsNullOrWhiteSpace(nextSurname)) return;
		string oldName = SafeName(oldFamilyName, SafeName(previousSurname, "旧姓") + "氏");
		string newName = SafeName(newFamilyName, nextSurname.Trim() + "氏");
		string body = oldName + "易姓为" + newName + "。血脉谱系、家业与世次仍承原族，改姓前已故先祖名讳不作追改；自此在世族人及后世新丁皆从新姓。";
		if (!string.IsNullOrWhiteSpace(actorName)) body = SafeName(actorName, "族中主事者") + "主其事，" + body;
		RecordFamily(
			familyId,
			newName,
			year,
			XjThreeBookEventTypes.FamilySurnameChanged,
			"family|surname-change|" + familyId + "|" + Math.Max(0, year) + "|" + nextSurname.Trim(),
			"易姓",
			"易姓续谱",
			body,
			3,
			true,
			actorId,
			actorName,
			result: XjHistoryResult.None);
	}
}
