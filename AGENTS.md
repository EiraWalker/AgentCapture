# Workspace

Use this repository root as the working directory for all commands.

- Keep generated deliverables in `artifacts/`.
- Keep temporary scripts, logs, and build intermediates in `work/`.
- Keep the runnable EXE, separate DLLs, and runtime configuration together in `bin/win-x64/`.
- Keep screenshot validation results in `validation/`.
- Do not create new working files or deliverables in the previous Codex chat directory.

Configure the installed Codex skill to use `bin/win-x64/AgentCapture.exe`. When using a shared skill source, preserve its target-link convention and the local configuration.

Prefer codebase-memory-mcp graph tools for code discovery. Index the repository first when needed; use text search for configuration, literals, and uncovered files. Use Context7 for current library/API/CLI documentation.
