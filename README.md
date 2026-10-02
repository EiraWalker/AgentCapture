# AgentCapture

C# Windows 命令行截图工具，供 AI-Agent 通过进程调用。支持 WGC、PrintWindow 以及显式的自动回退；每次调用在 stdout 返回一行 UTF-8 JSON，截图保存为 PNG。

发布版本：1.0.0，Windows x64，**framework-dependent 目录发布，EXE 与 DLL 分开，不包含 .NET Runtime**。要求 Windows 10 2004 / Windows 11，以及 x64 `.NET 10 Desktop Runtime`。本机已确认安装 `Microsoft.WindowsDesktop.App 10.0.6`。

部署时复制整个 `bin/win-x64` 目录。`AgentCapture.exe` 是启动器；应用程序集、依赖 DLL、`.deps.json` 和 `.runtimeconfig.json` 都独立放在该目录，不嵌入 EXE。单独复制 EXE 无法运行。

## 下载发布版

从 [GitHub Releases](https://github.com/EriaWalker/AgentCapture/releases/latest) 下载 `AgentCapture-windows-x64.zip`，解压后运行 `AgentCapture/bin/win-x64/AgentCapture.exe`。包内已包含所需应用 DLL，无需编译；本机需要安装上面注明的 .NET 10 Desktop Runtime x64。下载旁边的 `.sha256` 文件可校验压缩包。

源码仓库的生成文件统一写入 `artifacts/`（交付包）、`work/`（临时工作文件）、`bin/win-x64/`（程序）和 `validation/`（验证结果），均不提交到 Git。

## 调用方式

```powershell
$tool = Join-Path (Get-Location) 'bin\win-x64\AgentCapture.exe'

# 枚举窗口；stdout 可直接交给 ConvertFrom-Json
& $tool list
& $tool list --title 'Unity'
& $tool list --pid 12345 --include-hidden

# 把示例 HWND 换成 list 返回的实际句柄
& $tool capture --hwnd 0x123456 --method wgc --output 'C:\Temp\wgc.png'
& $tool capture --hwnd 0x123456 --method printwindow --output 'C:\Temp\printwindow.png'
& $tool capture --hwnd 0x123456 --method auto --timeout-ms 8000 --output 'C:\Temp\auto.png'

# PID 恰好对应一个可用窗口时也可直接截图
& $tool capture --pid 12345 --output 'C:\Temp\pid.png'

# 已有文件默认保留；只有显式传入 --overwrite 才替换
& $tool capture --hwnd 0x123456 --method wgc --output 'C:\Temp\wgc.png' --overwrite
```

可用命令：

```text
list [--pid PID] [--title TEXT] [--include-hidden]
capture (--hwnd HWND | --pid PID) --output FILE.png
        [--method wgc|printwindow|auto] [--timeout-ms 8000]
        [--overwrite] [--printwindow-flags 0|2]
self-test --output-dir DIRECTORY
version
help
```

HWND 支持十进制或 `0x` 十六进制。`--title` 按窗口标题进行不区分大小写的子串匹配。PID 有多个可用窗口时返回 `ambiguous_window`，调用方应从 `list --pid` 选择 HWND，工具不擅自选择。

默认路径是 `wgc`；`auto` 必须显式请求。两种方法都捕获整个目标窗口表面，不提供屏幕区域截取或 client-only 裁剪；WGC 的像素边界与 PrintWindow 的窗口矩形可能不同，读取 `image.width/height` 获取实际结果尺寸。

PrintWindow 默认 flags=0。flags=2 请求 `PW_RENDERFULLCONTENT`，作为按应用尝试的兼容选项，不保证所有界面有效。

## JSON 协议

成功示例（句柄、尺寸、路径及哈希只是示例）：

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

实际返回还包含窗口类名、位置、窗口尺寸、是否为前台及可查询到的 display affinity。`foreground.changes` 是截取期间 `EVENT_SYSTEM_FOREGROUND` 事件中的窗口句柄；事件也可能来自用户切换窗口，不表示一定由工具造成。

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

已开始截图尝试的失败响应还包含 `target`、`requestedMethod`、`attempts` 和 `elapsedMs`。调用方首先检查 `ok`；成功再读取 `output` 和实际的 `method`。错误对象与结果允许增加字段，解析时忽略未知字段。

| 退出码 | 含义 |
| --- | --- |
| 0 | 命令成功 |
| 2 | 参数错误、输出已存在、PID 对应多个窗口 |
| 3 | 窗口失效、状态不支持、捕获失败、空图等 |
| 4 | 截图超时 |
| 5 | 未预期的运行时错误 |

常用错误码：`invalid_arguments`、`invalid_window`、`window_not_found`、`ambiguous_window`、`output_exists`、`window_not_capturable`、`capture_restricted`、`blank_frame`、`timeout`、`window_changed`、`window_state_changed`、`window_resized`、`worker_isolation_failed`。

Python 进程调用示例见 [examples/capture_from_python.py](./examples/capture_from_python.py)。使用 `subprocess` 的 `CREATE_NO_WINDOW` 可避免调用工具时新建控制台窗口；PowerShell 在已有终端中直接调用即可。

## 捕获行为

- 两条捕获路径均不调用激活、前置、移动、恢复或输入注入 API。
- WGC 直接按 HWND 建立捕获项目，不打开选择器；关闭鼠标捕获，保留系统默认提示边框，不主动请求去边框权限。
- WGC 拒绝最小化、隐藏和 cloaked 窗口；不会自动恢复。遮挡窗口可捕获。启动后短暂保留最新到达的 frame，降低初始旧表面的影响，但不能保证应用持续绘制或图像就是最新业务状态。
- PrintWindow 可以显式尝试隐藏或最小化窗口，是否有完整内容依赖目标应用。最小化时可能只拿到小尺寸的窗口表面，结果会带 `hidden_or_minimized_content_not_guaranteed`。
- `auto` 使用一个共享超时预算，WGC 最多先占约 60%，再将剩余预算交给 PrintWindow；不会退回到切前台后截取屏幕。
- 原生阻塞调用运行在独立子进程中；超时终止子进程。Windows Job Object 设置 `KILL_ON_JOB_CLOSE`，以便调用方结束监督进程时清理已分配的捕获子进程。
- PNG 先写入目标目录的唯一临时文件，成功后原子发布。失败和超时不替换已有目标，正常清理临时文件；若监督进程被强制终止，可能遗留 `.agentcapture-*.png` 临时文件，目标文件仍不会提前发布。
- 整张 RGB 全黑或完全透明的画面返回 `blank_frame`，不保存成功截图；这也会拒绝合法的纯黑画面。非黑图仍可能是旧帧、局部空白或错误内容，所以 `contentVerified` 始终为 false，需要 AI 检查图片。

运行权限保持 `asInvoker`，不自动提权。窗口关闭、系统锁定、受保护内容及目标应用行为都可能使捕获失败。

## 构建与验证

```powershell
# 要求 .NET 10 SDK；脚本生成 EXE 与 DLL 分开的目录发布，不包含 Runtime
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1

# 运行本机验证，不操作第三方应用
.\bin\win-x64\AgentCapture.exe self-test --output-dir .\validation
```

自测创建两扇自有 WinForms 窗口，放在 Z-order 底部，由已有前台窗口遮挡，并使用 NOACTIVATE。需要已有前台窗口足够大；自测改变的隐藏/最小化状态都只属于测试窗口。报告保存在生成的 `run-*` 目录下。

framework-dependent 发布版本通过 11 项自测：两条路径的 A/B 图像变化及前台事件、WGC 优先、已有输出保留、PID 歧义处理、PNG alpha、黑图拒绝、最小化不恢复、PrintWindow 最小化结果、隐藏状态下失败不显示、停滞子进程终止。机器相关的验证产物保留在本地 `validation/`，不提交到源码仓库。

停滞验证通过强制暂停的测试子进程调用同一执行器；本机没有复现 PrintWindow 原生调用阻塞。自测不等于游戏、Electron、浏览器或全部第三方应用兼容性验证。

## 源码结构与依据

`CaptureSupervisor` 负责窗口身份、子进程生命周期、总预算、回退和文件发布；`WgcCapture`/`PrintWindowCapture` 负责各自原生路径；`CapturedPixels` 负责 PNG 和内容诊断；`Windows`/`ForegroundMonitor` 负责窗口枚举和只读状态监测。未引入服务、常驻 daemon 或 GUI。

依赖锁定在 `packages.lock.json`；Direct3D11 包版本为 `Vortice.Direct3D11 3.8.3`。第三方许可见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。

API 依据：[CreateForWindow](https://learn.microsoft.com/en-us/windows/win32/api/windows.graphics.capture.interop/nf-windows-graphics-capture-interop-igraphicscaptureiteminterop-createforwindow)、[CreateFreeThreaded](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.capture.direct3d11captureframepool.createfreethreaded)、[PrintWindow](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-printwindow)、[WGC 遮挡捕获说明](https://learn.microsoft.com/en-us/windows/apps/dev-tools/winapp-cli/ui-automation)、[Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows)。

## Codex skill

仓库包含 [agentcapture skill](skills/agentcapture/SKILL.md)，支持自动选择，也可用 `$agentcapture` 显式调用。

构建程序后安装到本机 Codex：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\install-codex-skill.ps1
```

安装脚本保持已有 skill 不变，写入本机 `config.local.json` 指向 EXE。设置了 `CODEX_HOME` 时使用该目录，否则安装到当前用户的 `.codex/skills/agentcapture`。使用 Skillshare 的机器应安装到已有共享 source 并沿用 Codex 的链接方式，避免重复副本；skill 的 `metadata.targets` 限定为 Codex。

在 Codex 的新聊天中可请求：`用 $agentcapture 截取已运行应用的窗口，不切到前台。`

## 许可证

工具源码、调用适配器和 skill 以 [MIT License](LICENSE) 发布。第三方依赖保留各自的许可证，详见 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)。仓库不提交 .NET Runtime、构建后的 EXE 或本机配置；使用 `build.ps1` 生成本地可执行文件。
