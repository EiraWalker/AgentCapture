# Locate or install the tool

Requires Windows 10 2004 / Windows 11 x64 and .NET 10 Desktop Runtime. The deployment is framework-dependent and does not bundle .NET. The EXE, application DLL, dependency DLLs, .deps.json, and .runtimeconfig.json remain separate files. Copy the complete bin/win-x64 directory when deploying; copying only the EXE does not work.

The local deployment writes `config.local.json` beside `SKILL.md` with an absolute `executable` path. Inspect that configuration when locating the installed tool. It is machine-specific and stays out of Git.

For another machine, build the MIT-licensed source at [EriaWalker/AgentCapture](https://github.com/EriaWalker/AgentCapture) with .NET 10 SDK using `build.ps1`; the EXE is emitted under `bin/win-x64`. Then set `AGENTCAPTURE_EXE`, put the executable on PATH, or copy `config.example.json` to `config.local.json` and set the path.

An explicit override is also supported:

```powershell
python -X utf8 '<skill-dir>\scripts\invoke.py' --tool 'C:\Tools\AgentCapture\AgentCapture.exe' version
```

The repository's `scripts/install-codex-skill.ps1` installs a standalone local Codex skill and writes its EXE configuration. When the machine uses a shared skill source such as Skillshare, install into that source and preserve its target-link convention instead of creating a duplicate standalone copy.

Verify `version` and a narrowly targeted `list`. A successful process call verifies the adapter, not screenshot contents or Codex UI discovery. If Codex's current skill list has not refreshed, use a new chat or the client's normal refresh mechanism; do not restart unrelated applications.
