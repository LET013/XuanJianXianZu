using System;

namespace XuanJianVNext.Systems.Family;

internal readonly struct XjFamilyMemberDisplayItem
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string Name;
	internal readonly int Generation;
	internal readonly string Realm;
	internal readonly string Status;
	internal readonly string RelationText;
	internal readonly string DisplayText;

	internal XjFamilyMemberDisplayItem(
		bool found,
		long actorId,
		string name,
		int generation,
		string realm,
		string status,
		string relationText,
		string displayText)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		Name = name ?? string.Empty;
		Generation = generation < 0 ? 0 : generation;
		Realm = realm ?? string.Empty;
		Status = status ?? string.Empty;
		RelationText = relationText ?? string.Empty;
		DisplayText = displayText ?? string.Empty;
	}
}

internal readonly struct XjFamilyPendingDisplayItem
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string Name;
	internal readonly string Reason;
	internal readonly long ParentId1;
	internal readonly long ParentId2;
	internal readonly long FatherActorId;
	internal readonly string DisplayText;

	internal XjFamilyPendingDisplayItem(
		bool found,
		long actorId,
		string name,
		string reason,
		long parentId1,
		long parentId2,
		long fatherActorId,
		string displayText)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		Name = name ?? string.Empty;
		Reason = reason ?? string.Empty;
		ParentId1 = parentId1 < 0L ? 0L : parentId1;
		ParentId2 = parentId2 < 0L ? 0L : parentId2;
		FatherActorId = fatherActorId < 0L ? 0L : fatherActorId;
		DisplayText = displayText ?? string.Empty;
	}
}

internal readonly struct XjFamilyMarriageDisplayItem
{
	internal readonly bool Found;
	internal readonly long ActorId;
	internal readonly string Name;
	internal readonly long SpouseActorId;
	internal readonly string SpouseName;
	internal readonly string RelationText;
	internal readonly string DisplayText;

	internal XjFamilyMarriageDisplayItem(
		bool found,
		long actorId,
		string name,
		long spouseActorId,
		string spouseName,
		string relationText,
		string displayText)
	{
		Found = found;
		ActorId = actorId < 0L ? 0L : actorId;
		Name = name ?? string.Empty;
		SpouseActorId = spouseActorId < 0L ? 0L : spouseActorId;
		SpouseName = spouseName ?? string.Empty;
		RelationText = relationText ?? string.Empty;
		DisplayText = displayText ?? string.Empty;
	}
}

internal readonly struct XjBloodlineDisplayState
{
	internal static XjBloodlineDisplayState Empty { get; } = new XjBloodlineDisplayState(false, true, string.Empty, 0, 0, string.Empty, string.Empty, 0);

	internal readonly bool Found;
	internal readonly bool IsInferred;
	internal readonly string Quality;
	internal readonly int Concentration;
	internal readonly int Generation;
	internal readonly string OriginDaoTu;
	internal readonly string Source;
	internal readonly int ExtraTalentInheritance;

	internal XjBloodlineDisplayState(
		bool found,
		bool isInferred,
		string quality,
		int concentration,
		int generation,
		string originDaoTu,
		string source,
		int extraTalentInheritance)
	{
		Found = found;
		IsInferred = isInferred;
		Quality = quality ?? string.Empty;
		Concentration = concentration < 0 ? 0 : concentration;
		Generation = generation < 0 ? 0 : generation;
		OriginDaoTu = originDaoTu ?? string.Empty;
		Source = source ?? string.Empty;
		ExtraTalentInheritance = extraTalentInheritance < 0 ? 0 : extraTalentInheritance;
	}
}
