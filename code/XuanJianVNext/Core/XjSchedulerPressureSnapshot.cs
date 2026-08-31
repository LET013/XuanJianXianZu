namespace XuanJianVNext.Core;

internal readonly struct XjSchedulerPressureSnapshot
{
	internal XjSchedulerPressureSnapshot(
		int annualIngress,
		int annualCore,
		int annualSecondary,
		int annualMaintenance,
		int oldestPendingSemanticYear)
	{
		AnnualIngress = annualIngress;
		AnnualCore = annualCore;
		AnnualSecondary = annualSecondary;
		AnnualMaintenance = annualMaintenance;
		OldestPendingSemanticYear = oldestPendingSemanticYear;
	}

	internal int AnnualIngress { get; }
	internal int AnnualCore { get; }
	internal int AnnualSecondary { get; }
	internal int AnnualMaintenance { get; }
	internal int OldestPendingSemanticYear { get; }
}
