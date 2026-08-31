namespace XuanJianVNext.Data.Archive;

/// <summary>
/// Versioned module-owned payload embedded in the world archive. The outer
/// archive remains backward compatible while new features can evolve their own
/// schema without adding fields to XjWorldArchiveData and its central codec.
/// </summary>
internal sealed class XjModuleArchiveDocument
{
	public string ModuleId { get; set; } = string.Empty;
	public int SchemaVersion { get; set; }
	public string Payload { get; set; } = string.Empty;

	internal XjModuleArchiveDocument Clone()
	{
		return new XjModuleArchiveDocument
		{
			ModuleId = ModuleId ?? string.Empty,
			SchemaVersion = SchemaVersion,
			Payload = Payload ?? string.Empty
		};
	}
}
