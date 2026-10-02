# AgentCapture

> [!IMPORTANT]
> **目标软件窗口应保持打开且未最小化。** 窗口可以被其他应用遮挡，无需切到前台。
>
> 最小化时，可以尝试 `--method printwindow`。**目标程序必须支持 PrintWindow 绘制。** 结果可能为空白、不完整或旧画面。WGC 路径会拒绝最小化窗口。

AgentCapture 是供 AI-Agent 调用的 C# Windows 命令行截图工具。它通过 WGC 或 PrintWindow 捕获窗口，保存 PNG，并向 stdout 返回一行 UTF-8 JSON。

## 安装发布版

**运行前需安装 .NET 10 Desktop Runtime x64。发布包不包含 .NET Runtime。运行发布版不需要 SDK。

支持 Windows 10 2004 或更新版本，以及 Windows 11。发布版为 Windows x64，版本为 1.0.0。

1. 从 [微软下载页](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) 安装 **.NET Desktop Runtime → Windows → x64**。
2. 从 [GitHub Releases](https://github.com/EriaWalker/AgentCapture/releases/latest) 下载 `AgentCapture-windows-x64.zip`。
3. 解压到所需目录。
4. 在 `AgentCapture/bin/win-x64` 目录打开 PowerShell。

```powershell
.\AgentCapture.exe version
.\AgentCapture.exe list
```

程序采用 framework-dependent 目录发布。EXE、应用 DLL、依赖 DLL、`.deps.json` 和 `.runtimeconfig.json` 都是独立文件。DLL 不嵌入 EXE。

**部署时复制整个 `bin/win-x64` 目录。单独复制 EXE 无法运行。** 发布包旁的 `.sha256` 文件用于校验 ZIP。

## 快速截图

以下命令在源码根目录执行。使用解压后的发布包时，在 `AgentCapture` 目录执行。

```powershell
$tool = Join-Path (Get-Location) 'bin\win-x64\AgentCapture.exe'

# 查找窗口
& $tool list --title 'QQ'

# 将示例 HWND 替换为 list 返回的句柄
& $tool capture --hwnd 0x123456 --method wgc --output 'C:\Temp\wgc.png'

# 尝试 PrintWindow 完整内容选项
& $tool capture --hwnd 0x123456 --method printwindow --printwindow-flags 2 --output 'C:\Temp\printwindow.png'

# 显式启用 WGC → PrintWindow 回退
& $tool capture --hwnd 0x123456 --method auto --timeout-ms 8000 --output 'C:\Temp\auto.png'
```

先检查返回结果的 `ok`。成功后，读取 `output` 并查看 PNG。成功生成文件不代表图像包含完整或最新的应用内容。

已有输出文件默认保留。只有显式传入 `--overwrite` 时，工具才替换该文件。

## 选择截图路径

| 方法 | 窗口条件 | 行为 |
| --- | --- | --- |
| `wgc`，默认 | 可见、未最小化、未 cloaked | 支持被其他窗口遮挡的目标。拒绝隐藏或最小化窗口。 |
| `printwindow` | 取决于目标程序支持 | 可尝试隐藏或最小化窗口。结果可能为空白、旧画面或小尺寸表面。 |
| `auto` | 取决于两条路径 | 先尝试 WGC，失败后尝试 PrintWindow。必须显式选择。 |

PrintWindow 默认使用 flags=0。`--printwindow-flags 2` 请求 `PW_RENDERFULLCONTENT`。此选项可用于兼容性测试，但不保证目标程序返回有效内容。

两条路径都捕获整个目标窗口表面。工具不提供屏幕区域截取或 client-only 裁剪。两条路径的像素边界可能不同。以 `image.width` 和 `image.height` 为准。

## 命令参考

```text
list [--pid PID] [--title TEXT] [--include-hidden]
capture (--hwnd HWND | --pid PID) --output FILE.png
        [--method wgc|printwindow|auto] [--timeout-ms 8000]
        [--overwrite] [--printwindow-flags 0|2]
self-test --output-dir DIRECTORY
version
help
```

- HWND 支持十进制和 `0x` 十六进制。
- `--title` 按标题做子串匹配，不区分大小写。
- `list --pid PID --include-hidden` 可列出该进程的隐藏窗口。
- `capture --pid PID` 仅适用于恰好有一个可用窗口的进程。

PID 对应多个可用窗口时，工具返回 `ambiguous_window`。先调用 `list --pid PID`，再选择具体 HWND。

## 接入 Codex

仓库包含 [agentcapture skill](skills/agentcapture/SKILL.md)。Codex 可自动选择该 skill，也可通过 `$agentcapture` 显式调用。

在源码根目录运行安装脚本。使用发布包时，在解压后的 `AgentCapture` 目录运行。

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-codex-skill.ps1
```

安装脚本将 EXE 路径写入本机 `config.local.json`。目标目录已有 skill 时，脚本停止并保留该目录。

设置了 `CODEX_HOME` 时，脚本使用该目录。否则，脚本使用当前用户的 `.codex/skills/agentcapture`。

使用 Skillshare 时，将 skill 安装到已有共享 source。沿用 Codex 的链接方式。skill 的 `metadata.targets` 限定为 Codex。

在 Codex 新聊天中输入：

> 用 $agentcapture 截取已运行应用的窗口，不切到前台。

Python 调用示例见 [examples/capture_from_python.py](examples/capture_from_python.py)。Python 调用方可用 `CREATE_NO_WINDOW` 避免新建控制台。PowerShell 可在已有终端中直接调用 EXE。

## JSON 协议

每次调用返回一个 JSON 对象。调用方应忽略未知字段，以兼容后续扩展。

成功示例中的句柄、路径、尺寸和哈希仅用于说明：

```json
{
  "schemaVersion": 1,
  "ok": true,
  "command": "capture",
  "requestedMethod": "auto",
  "method": "wgc",
  "output": "C:\\Temp\\auto.png",
  "target": {
    "hwnd": "0x123456", "pid": 12345, "processName": "ExampleApp",
    "title": "Example", "visible": true, "minimized": false, "cloaked": false
  },
  "image": {
    "width": 480, "height": 240, "sha256": "...", "alphaMode": "straight",
    "uniformRgb": false, "contentVerified": false,
    "warnings": ["freshness_not_verified"]
  },
  "foreground": {
    "available": true, "before": "0xABC", "after": "0xABC", "changes": [],
    "targetActivationRequested": false
  },
  "warnings": ["system_capture_border_may_appear"],
  "attempts": [{"method": "wgc", "ok": true}],
  "elapsedMs": 650
}
```

`method` 表示实际使用的路径。`target` 还可包含窗口类名、位置、尺寸、前台状态和 display affinity。

`foreground.changes` 记录捕获期间的 `EVENT_SYSTEM_FOREGROUND` 事件。用户切换窗口也会产生事件。事件记录不能单独证明变化由工具造成。

失败示例：

```json
{
  "schemaVersion": 1,
  "ok": false,
  "error": {
    "code": "window_not_capturable",
    "message": "WGC requires a visible, non-minimized, non-cloaked window. The tool will not restore it.",
    "exitCode": 3
  }
}
```

截图尝试开始后，失败响应还包含 `target`、`requestedMethod`、`attempts` 和 `elapsedMs`。

| 退出码 | 含义 |
| --- | --- |
| 0 | 命令成功 |
| 2 | 参数错误、输出已存在，或窗口选择有歧义 |
| 3 | 窗口失效、状态不支持、捕获失败，或空图 |
| 4 | 截图超时 |
| 5 | 未预期的运行时错误 |

常用错误码包括 `invalid_window`、`window_not_found`、`ambiguous_window`、`output_exists`、`window_not_capturable`、`capture_restricted`、`blank_frame` 和 `timeout`。处理方式见 [skill 协议说明](skills/agentcapture/references/protocol.md)。

## 捕获限制

### 窗口状态与画面内容

工具不请求激活、前置、移动、恢复窗口或注入输入。运行权限保持 `asInvoker`，不自动提权。窗口关闭、系统锁定和受保护内容可能使捕获失败。

WGC 按 HWND 创建捕获项目，不打开选择器。它关闭鼠标捕获，并保留系统默认提示边框。工具不请求移除边框的权限。

WGC 启动后短暂保留最新到达的 frame。目标程序仍可能停止绘制。工具无法保证捕获到最新业务状态。

最小化的 PrintWindow 结果可能只有小尺寸窗口表面。此时，结果包含 `hidden_or_minimized_content_not_guaranteed` 警告。

整张 RGB 全黑或完全透明时，工具返回 `blank_frame`，不发布 PNG。此检查也会拒绝合法的纯黑画面。

其他图像仍可能含有旧帧、局部空白或错误内容。`contentVerified` 始终为 false。调用方需要检查图片。

### 超时与文件保护

`auto` 共用一个超时预算。WGC 最多先占约 60%。PrintWindow 使用剩余预算。工具不会切到前台后截取屏幕。

原生捕获调用在独立子进程中执行。超时后，监督进程终止该子进程。Windows Job Object 使用 `KILL_ON_JOB_CLOSE` 清理已分配的捕获子进程。

工具先将 PNG 写入目标目录的唯一临时文件。成功后，它原子发布目标文件。失败或超时不会替换已有目标文件。

监督进程被强制终止时，可能遗留 `.agentcapture-*.png` 临时文件。目标文件不会提前发布。

## 构建与验证

构建需要 .NET 10 SDK。在源码根目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
.\bin\win-x64\AgentCapture.exe self-test --output-dir .\validation
```

构建脚本生成 EXE 与 DLL 分开的目录发布。它不包含 .NET Runtime。

自测创建两扇自有 WinForms 窗口。它使用 NOACTIVATE 将测试窗口放在 Z-order 底部。已有前台窗口需足够大，才能遮挡测试窗口。

自测仅改变测试窗口的隐藏和最小化状态。报告保存在 `validation/run-*` 中。

目录发布版本已通过 11 项自测。验证覆盖两条捕获路径、窗口状态、输出保护、PNG 和停滞子进程终止。详情见 [VALIDATION.md](VALIDATION.md)。

停滞测试通过强制暂停的子进程调用同一执行器。本机没有复现 PrintWindow 原生阻塞。自测不能证明所有第三方应用都兼容。

生成文件不提交到 Git：

| 目录 | 用途 |
| --- | --- |
| `artifacts/` | 交付包与本地测试成果 |
| `work/` | 临时文件与构建中间文件 |
| `bin/win-x64/` | 程序及其依赖 |
| `validation/` | 自测报告与图片 |

## 源码与 API 依据

| 模块 | 职责 |
| --- | --- |
| `CaptureSupervisor` | 窗口身份、子进程、超时预算、回退与文件发布 |
| `WgcCapture` / `PrintWindowCapture` | 各自的原生捕获路径 |
| `CapturedPixels` | PNG 编码与内容诊断 |
| `Windows` / `ForegroundMonitor` | 窗口枚举与只读状态监测 |

工具没有服务、常驻 daemon 或 GUI。`packages.lock.json` 锁定依赖版本。Direct3D11 依赖为 `Vortice.Direct3D11 3.8.3`。

API 依据：[CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)、[CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)、[PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)、[WGC 遮挡捕获说明](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation)、[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)。

## 许可证

工具源码、调用适配器和 skill 使用 [MIT License](LICENSE)。第三方依赖使用各自的许可证，见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。
