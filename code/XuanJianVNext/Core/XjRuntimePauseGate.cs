namespace XuanJianVNext.Core;

internal static class XjRuntimePauseGate
{
	internal static bool CodexOpen { get; private set; }

	internal static bool BlocksSimulation => CodexOpen;

	internal static void SetCodexOpen(bool open)
	{
		CodexOpen = open;
	}

	internal static void Clear()
	{
		CodexOpen = false;
	}
}
