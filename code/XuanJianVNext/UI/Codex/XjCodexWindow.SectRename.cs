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
		bool editing = item != null && _renamingSectId == item.SectId;
		if (editing)
		{
			_renamingSectName = GUILayout.TextField(_renamingSectName ?? string.Empty, 16, GUILayout.MinWidth(180f), GUILayout.Height(38f));
			if (GUILayout.Button("确认改名", GUILayout.Width(95f), GUILayout.Height(38f)))
			{
				TryConfirmSectRename(item);
			}
			if (GUILayout.Button("取消", GUILayout.Width(62f), GUILayout.Height(38f)))
			{
				_renamingSectId = 0L;
				_renamingSectName = string.Empty;
				_sectRenameMessage = string.Empty;
			}
			GUI.enabled = previous;
			return;
		}

		if (GUILayout.Button("改名", options))
		{
			_renamingSectId = item.SectId;
			_renamingSectName = item.Name ?? string.Empty;
			_sectRenameMessage = string.Empty;
		}
		GUI.enabled = previous;
	}

	private void DrawSectRenameEditor(XjCodexSectItem item)
	{
		if (item == null || item.SectId <= 0L || _renamingSectId != item.SectId)
		{
			return;
		}

		if (!string.IsNullOrWhiteSpace(_sectRenameMessage))
		{
			GUILayout.BeginHorizontal();
			GUILayout.Label(Rich(_sectRenameMessage), GUILayout.Width(260f));
			GUILayout.FlexibleSpace();
			GUILayout.EndHorizontal();
		}
	}

	private void TryConfirmSectRename(XjCodexSectItem item)
	{
		if (item == null || item.SectId <= 0L)
		{
			return;
		}

		if (XjSectRepository.TryRenameSect(item.SectId, _renamingSectName, out string message))
		{
			_sectRenameMessage = message;
			_renamingSectId = 0L;
			_renamingSectName = string.Empty;
			XjCodexSnapshotPublisher.RefreshNow();
		}
		else
		{
			_sectRenameMessage = message;
		}
	}
}
