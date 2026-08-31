namespace XuanJianVNext.Data.Events;

/// <summary>
/// RC2/RC3 玄鉴历三千年“太渊证金”旧事件存档兼容结构。RC4 起不再驱动任何玩法。
/// 字段仅用于反序列化旧档，旧坎水／府水封锁与高境难度修正均已删除。
/// 不得再从本结构恢复、推导或重放任何旧事件副作用。
/// </summary>
internal sealed class XjTaiYuanJinXianEventArchiveData
{
	public bool Initialized { get; set; }
	public bool Triggered { get; set; }
	public bool LegacyBaseline { get; set; }
	public int TriggeredYear { get; set; }
}
