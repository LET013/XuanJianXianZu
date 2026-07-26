using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Data.FaBao;
using XuanJianVNext.Data.CaiQi;
using XuanJianVNext.Systems.ActorSystem;
using XuanJianVNext.Systems.AutoCollect;
using XuanJianVNext.Data.Rules;
using XuanJianVNext.Data.Events;
using XuanJianVNext.Systems.Events;
using XuanJianVNext.Systems.Combat;
using XuanJianVNext.Systems.Warehouse;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.HighRealm;
using XuanJianVNext.UI.Common;

namespace XuanJianVNext.Systems.FaBao;

internal readonly struct XjFaBaoState
{
	internal static XjFaBaoState Empty { get; } = new XjFaBaoState(
		false,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		string.Empty,
		0,
		"Empty");

	internal readonly bool Found;
	internal readonly string Id;
	internal readonly string Name;
	internal readonly string DaoTu;
	internal readonly string ClassName;
	internal readonly string Kind;
	internal readonly string Role;
	internal readonly string Affixes;
	internal readonly string Description;
	internal readonly string Source;
	internal readonly int Year;
	internal readonly string ReasonCode;

	internal XjFaBaoState(
		bool found,
		string id,
		string name,
		string daoTu,
		string className,
		string kind,
		string role,
		string affixes,
		string description,
		string source,
		int year,
		string reasonCode)
	{
		Found = found;
		Id = id ?? string.Empty;
		Name = name ?? string.Empty;
		DaoTu = daoTu ?? string.Empty;
		ClassName = className ?? string.Empty;
		Kind = kind ?? string.Empty;
		Role = role ?? string.Empty;
		Affixes = affixes ?? string.Empty;
		Description = description ?? string.Empty;
		Source = source ?? string.Empty;
		Year = year < 0 ? 0 : year;
		ReasonCode = reasonCode ?? string.Empty;
	}

	internal XjFaBaoState(
		bool found,
		string id,
		string name,
		string daoTu,
		string className,
		string source,
		int year,
		string reasonCode)
		: this(found, id, name, daoTu, className, string.Empty, string.Empty, string.Empty, string.Empty, source, year, reasonCode)
	{
	}
}
