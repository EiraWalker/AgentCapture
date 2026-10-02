# AgentCapture 1.0.0 验证

验证日期：2026-10-03，Asia/Taipei。发布目标：Windows x64，framework-dependent 目录发布，EXE 与 DLL 分开，不包含 .NET Runtime。

- Release publish：0 warning，0 error。
- 11 项自测通过。WGC 与 PrintWindow 均从自有、被遮挡的 WinForms 窗口取得绿色 A / 橙色 B 状态，像素、PNG 尺寸与哈希变化符合预期，捕获期间未观察到测试窗口成为前台。
- 9 项独立 EXE 调用检查通过：help/version、无效命令、无效/失效 HWND、PID/HWND 冲突、无效 method、无效 timeout、重复参数。stdout 均为一行可解析 JSON，退出码与错误码匹配，失败未发布截图。
- 单文件发布关闭后，通过已安装 Codex skill 重新运行 11 项自测，全部通过；EXE 与 `dotnet AgentCapture.dll` 的 version 调用均成功。
- 发布目录含独立的应用程序集、依赖 DLL、deps.json 和 runtimeconfig.json，共 12 个文件；EXE 为 162,816 字节，DLL 不嵌入 EXE。
- 外部 runtimeconfig 使用 `frameworks` 引用 `Microsoft.NETCore.App` 与 `Microsoft.WindowsDesktop.App`，没有 `includedFrameworks`；发布目录不含 coreclr.dll 或 clrjit.dll。
- 改为目录发布前，实际启动 EXE 时曾确认 `coreclr.dll` 与 `clrjit.dll` 均来自 `C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.6\`。该模块路径检查没有在此次目录发布中重复。

结构化结果和 PNG 位于 `validation/`。`release-manifest.json` 记录发布目录各文件的大小、SHA-256、构建方式和源码哈希；ZIP 的外部哈希见旁边的 `.sha256` 文件。

验证边界：只有自有测试窗口，未截取第三方应用。最小化的 PrintWindow 结果只是一块小尺寸窗口表面，不是完整应用画面证明。原生 PrintWindow 阻塞未在本机复现；超时验证通过强制停滞的子进程走相同执行器完成。前台事件记录不等于目标应用持续渲染的证明。

早期把窗口完全移出屏幕时，WGC 确实遇到旧的合成画面；工具因此短暂保留最近到达的 frame，但仍明确返回 `freshness_not_verified`，由调用方检查实际内容。
