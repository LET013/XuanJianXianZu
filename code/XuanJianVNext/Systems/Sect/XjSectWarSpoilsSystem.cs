using XuanJianVNext.Data.Sect;
using XuanJianVNext.Systems.Alchemy;
using XuanJianVNext.Systems.Craft;

namespace XuanJianVNext.Systems.Sect;

internal readonly struct XjSectWarSpoils
{
	internal readonly int GongFaEntries;
	internal readonly int QiuJinFaEntries;
	internal readonly int CaiQiStacks;
	internal readonly int CaiQiAmount;
	internal readonly int CaiQiFaEntries;
	internal readonly int CraftResourceStacks;
	internal readonly int CraftResourceQuantity;
	internal readonly int TalismanBatches;
	internal readonly int TalismanQuantity;
	internal readonly int AlchemyMaterialStacks;
	internal readonly int AlchemyMaterialQuantity;
	internal readonly int AlchemyRecipes;
	internal readonly int PillBatches;
	internal readonly int PillQuantity;
	internal readonly int InterruptedTasks;

	internal XjSectWarSpoils(
		int gongFaEntries,
		int qiuJinFaEntries,
		int caiQiStacks,
		int caiQiAmount,
		int caiQiFaEntries,
		int craftResourceStacks,
		int craftResourceQuantity,
		int talismanBatches,
		int talismanQuantity,
		int alchemyMaterialStacks,
		int alchemyMaterialQuantity,
		int alchemyRecipes,
		int pillBatches,
		int pillQuantity,
		int interruptedTasks)
	{
		GongFaEntries = gongFaEntries < 0 ? 0 : gongFaEntries;
		QiuJinFaEntries = qiuJinFaEntries < 0 ? 0 : qiuJinFaEntries;
		CaiQiStacks = caiQiStacks < 0 ? 0 : caiQiStacks;
		CaiQiAmount = caiQiAmount < 0 ? 0 : caiQiAmount;
		CaiQiFaEntries = caiQiFaEntries < 0 ? 0 : caiQiFaEntries;
		CraftResourceStacks = craftResourceStacks < 0 ? 0 : craftResourceStacks;
		CraftResourceQuantity = craftResourceQuantity < 0 ? 0 : craftResourceQuantity;
		TalismanBatches = talismanBatches < 0 ? 0 : talismanBatches;
		TalismanQuantity = talismanQuantity < 0 ? 0 : talismanQuantity;
		AlchemyMaterialStacks = alchemyMaterialStacks < 0 ? 0 : alchemyMaterialStacks;
		AlchemyMaterialQuantity = alchemyMaterialQuantity < 0 ? 0 : alchemyMaterialQuantity;
		AlchemyRecipes = alchemyRecipes < 0 ? 0 : alchemyRecipes;
		PillBatches = pillBatches < 0 ? 0 : pillBatches;
		PillQuantity = pillQuantity < 0 ? 0 : pillQuantity;
		InterruptedTasks = interruptedTasks < 0 ? 0 : interruptedTasks;
	}

	internal bool HasAny => GongFaEntries > 0 || QiuJinFaEntries > 0 || CaiQiStacks > 0 || CaiQiFaEntries > 0
		|| CraftResourceStacks > 0 || TalismanBatches > 0 || AlchemyMaterialStacks > 0 || AlchemyRecipes > 0 || PillBatches > 0 || InterruptedTasks > 0;
}

internal static class XjSectWarSpoilsSystem
{
	internal static XjSectWarSpoils ClaimAll(XjSectArchiveRecord victor, XjSectArchiveRecord loser, int currentYear)
	{
		if (victor == null || loser == null || victor.SectId <= 0L || loser.SectId <= 0L || victor.SectId == loser.SectId)
		{
			return default;
		}

		XjSectGongFaPavilion.TransferSectWarehouse(
			loser.SectId,
			victor.SectId,
			victor.Name,
			currentYear,
			out int gongFaEntries,
			out int qiuJinFaEntries);
		XjSectCaiQiWarehouse.TransferSectWarehouse(
			loser.SectId,
			victor.SectId,
			victor.Name,
			currentYear,
			out int caiQiStacks,
			out int caiQiAmount,
			out int caiQiFaEntries);
		XjCraftDomainRegistry.TransferSectWarehouse(
			loser.SectId,
			victor.SectId,
			currentYear,
			out int craftResourceStacks,
			out int craftResourceQuantity,
			out int talismanBatches,
			out int talismanQuantity,
			out int interruptedTasks);
		XjAlchemyInventoryRegistry.TransferSectInventory(
			loser.SectId,
			victor.SectId,
			currentYear,
			out int alchemyMaterialStacks,
			out int alchemyMaterialQuantity,
			out int alchemyRecipes,
			out int pillBatches,
			out int pillQuantity);

		return new XjSectWarSpoils(
			gongFaEntries,
			qiuJinFaEntries,
			caiQiStacks,
			caiQiAmount,
			caiQiFaEntries,
			craftResourceStacks,
			craftResourceQuantity,
			talismanBatches,
			talismanQuantity,
			alchemyMaterialStacks,
			alchemyMaterialQuantity,
			alchemyRecipes,
			pillBatches,
			pillQuantity,
			interruptedTasks);
	}
}
