---
name: agentcapture
description: "Capture already-running Windows application windows through AgentCapture using WGC or PrintWindow without bringing the target to the foreground. Use for background window screenshots, window discovery by HWND/PID, and diagnosing capture failures."
license: MIT
metadata:
  targets: [codex]
---

# AgentCapture

Use this skill to take PNG screenshots of an existing Windows application while preserving its foreground and minimized/hidden state. The executable returns one UTF-8 JSON object per command.

## Invoke

Run the bundled adapter with an available Python 3 interpreter, using the actual installed skill path:

```powershell
python -X utf8 '<skill-dir>\scripts\invoke.py' list --title 'Unity'
python -X utf8 '<skill-dir>\scripts\invoke.py' capture --hwnd 0x123456 --method wgc --output 'C:\Temp\window.png'
```

Replace the example HWND with a handle returned by `list`. Use `list --pid PID` to disambiguate a process with multiple windows; a PID-only capture rejects multiple matches.

The adapter locates the EXE from explicit `--tool`, `AGENTCAPTURE_EXE`, `config.local.json`, PATH, or a built repository's `bin/win-x64` folder, in that order. For a missing tool or runtime, consult [setup.md](references/setup.md); preserve existing installations and use the user's chosen installation location.

## Choose a path

- Use `--method wgc` for a visible, non-minimized window, including one covered by other windows. The default system capture border may appear; the tool excludes the cursor and opens no picker.
- Use `--method printwindow` for an explicit compatibility attempt. Hidden/minimized targets may return incomplete, stale or small window surfaces. `--printwindow-flags 2` is an optional full-content compatibility request.
- Use `--method auto` when automatic WGC → PrintWindow fallback is acceptable. The returned `method` identifies the actual backend; both attempts share `--timeout-ms` (default 8000).

The tool preserves window state. For `window_not_capturable`, report the state or try PrintWindow within the request; restoring, moving or activating the application requires separate user intent. Keep output paths explicit; existing files require `--overwrite` to replace.

## Assess the result

Parse `ok` and the exit code first. On success, read the returned absolute `output`, actual backend, dimensions, warnings, and `foreground` event record. Inspect the PNG before claiming it contains the requested application state; return a clickable path or display it when useful.

`contentVerified:false` and `freshness_not_verified` mean encoding success does not prove the application's latest state. A minimized PrintWindow image can be a small icon/window surface. A rejected `blank_frame` may also be a legitimate pure-black scene; report the ambiguity instead of repeatedly retrying. Foreground events can come from concurrent user activity and do not by themselves establish causality.

For field names, failure handling, and uncommon options, read [protocol.md](references/protocol.md). Stop retries when the same state/error persists; explain the remaining limitation and request the missing target information if needed.
