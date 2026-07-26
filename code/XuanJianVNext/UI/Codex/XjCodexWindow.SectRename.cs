using UnityEngine;
using XuanJianVNext.Data.Codex;
using XuanJianVNext.Systems.Codex;
using XuanJianVNext.Systems.Sect;

namespace XuanJianVNext.UI.Codex;

internal sealed partial class XjCodexWindow
{
	private void DrawSectRenameButton(XjCodexSectItem item, params GUILayoutOption[] options)
	{
		bool previous = GUI.enabled;
		GUI.enabled = previous && item != null && item.SectId > 0L;
		if (GUILayout.Button(_renamingSectId == item.SectId ? "收起改名" : "改名", options))
		{
			if (_renamingSectId == item.SectId)
			{
				_renamingSectId = 0L;
				_renamingSectName = string.Empty;
				_sectRenameMessage = string.Empty;
			}
			else
			{
				_renamingSectId = item.SectId;
				_renamingSectName = item.Name ?? string.Empty;
				_sectRenameMessage = string.Empty;
			}
		}
		GUI.enabled = previous;
	}

	private void DrawSectRenameEditor(XjCodexSectItem item)
	{
		if (item == null || item.SectId <= 0L || _renamingSectId != item.SectId)
		{
			return;
		}

		GUILayout.BeginHorizontal();
		GUILayout.Label("新宗名", GUILayout.Width(60f));
		_renamingSectName = GUILayout.TextField(_renamingSectName ?? string.Empty, 16, GUILayout.Width(240f), GUILayout.Height(30f));
		if (GUILayout.Button("确认改名", GUILayout.Width(95f), GUILayout.Height(30f)))
		{
			if (XjSectRepository.TryRenameSect(item.SectId, _renamingSectName, out string message))
			{
				_sectRenameMessage = message;
				_renamingSectId = 0L;
				_renamingSectName = string.Empty;
				XjCodexSnapshotPublisher.RequestRefresh();
			}
			else
			{
				_sectRenameMessage = message;
			}
		}

		if (GUILayout.Button("取消", GUILayout.Width(70f), GUILayout.Height(30f)))
		{
			_renamingSectId = 0L;
			_renamingSectName = string.Empty;
			_sectRenameMessage = string.Empty;
		}

		if (!string.IsNullOrWhiteSpace(_sectRenameMessage))
		{
			GUILayout.Label(Rich(_sectRenameMessage), GUILayout.Width(260f));
		}

		GUILayout.FlexibleSpace();
		GUILayout.EndHorizontal();
	}
}
