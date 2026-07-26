using System;
using System.Collections.Generic;
using XuanJianVNext.Core;
using XuanJianVNext.Systems.Family;
using XuanJianVNext.Systems.GongFa;
using XuanJianVNext.Systems.ZongMen;

namespace XuanJianVNext.Systems.Warehouse;

/// <summary>
/// 将角色身上的高阶功法快照回流到家族、宗门库存。
/// 写入端有等价去重，这里可在读档、登名石或晋升后重复调用。
/// </summary>
internal static class XjGongFaWarehouseReconciler
{
	internal static void ReconcileActor(Actor actor, int currentYear)
	{
		if (actor?.data == null || !actor.isAlive())
		{
			return;
		}

		long actorId = ((BaseSystemData)actor.data).id;
		if (actorId <= 0L)
		{
			return;
		}

		string actorName = actor.getName() ?? string.Empty;
		IReadOnlyList<XjGongFaInheritanceRecord> records = XjGongFaInheritanceSnapshot.BuildRecords(actor, 4);
		if (records != null && records.Count > 0)
		{
			if (XjFamilyReadModel.Shared.TryGetConfirmedFamilyStableId(actorId, out long familyStableId)
				&& familyStableId > 0L)
			{
				for (int i = 0; i < records.Count; i++)
				{
					XjGongFaInheritanceRecord record = records[i];
					if (!record.Found) continue;
					XjFamilyGongFaWarehouse.AddGongFaToFamily(
						actorId,
						familyStableId,
						record.Name,
						record.Grade,
						Math.Max(0, currentYear),
						XjFamilyGongFaWarehouse.SourceTypeGongFa,
						record.DaoTu,
						string.Empty,
						record.MappedXianJi,
						string.Empty);
				}
			}

			XjZongMenIdentitySnapshot zongMen = XjZongMenAccessor.BuildIdentity(actor);
			if (zongMen.Found && zongMen.ZongMenId > 0L)
			{
				for (int i = 0; i < records.Count; i++)
				{
					XjGongFaInheritanceRecord record = records[i];
					if (!record.Found) continue;
					XjZongMenGongFaPavilion.TryAddGongFa(
						zongMen.ZongMenId,
						zongMen.ZongMenName,
						actorId,
						actorName,
						record.Name,
						record.Grade,
						record.DaoTu,
						"DengMingShiSync",
						record.MappedXianJi,
						Math.Max(0, currentYear));
				}
			}
		}

		XjQiuJinFaWarehouseReconciler.ReconcileActor(actor, currentYear);
	}
}
