using System.Diagnostics;
using System.Text.Json;

namespace AgentCapture;

internal static class CaptureSupervisor
{
    internal sealed record WorkerReply(int ExitCode, string Stdout, string Stderr, bool TimedOut, int ProcessId);

    internal static async Task<WorkerReply> RunWorker(ProcessStartInfo info, int budget)
    {
        using var job = new WorkerJob();
        using var worker = Process.Start(info) ?? throw new CaptureError("worker_failed", "Cannot start capture worker.");
        job.Assign(worker);
        Task<string> stdout = worker.StandardOutput.ReadToEndAsync();
        Task<string> stderr = worker.StandardError.ReadToEndAsync();
        using var cancel = new CancellationTokenSource(budget);
        bool timedOut = false;
        try { await worker.WaitForExitAsync(cancel.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            if (!worker.HasExited) worker.Kill(entireProcessTree: true);
            await worker.WaitForExitAsync();
        }
        return new(worker.ExitCode, await stdout, await stderr, timedOut, worker.Id);
    }

    internal static ProcessStartInfo StartInfo(params string[] args)
    {
        string executable = Environment.ProcessPath ?? throw new CaptureError("worker_failed", "Cannot locate the executable.");
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            info.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "AgentCapture.dll"));
        foreach (string arg in args) info.ArgumentList.Add(arg);
        return info;
    }

    public static async Task<object> Capture(CommandLine command)
    {
        if ((command.Get("hwnd") is null) == (command.Pid() is null))
            throw CommandLine.Invalid("Specify exactly one of --hwnd or --pid.");
        string method = command.Method();
        uint flags = command.PrintFlags();
        int timeout = command.Timeout();
        string output = Path.GetFullPath(command.Required("output"));
        if (!Path.GetExtension(output).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw CommandLine.Invalid("--output must end in .png.");
        if (!command.Has("overwrite") && File.Exists(output)) throw new CaptureError("output_exists", "Destination exists. Use another path or --overwrite.", 2);
        nint hwnd = Windows.Resolve(command.Get("hwnd"), command.Pid());
        var target = Windows.Read(hwnd);
        Windows.Validate(target, "printwindow"); // State restrictions for WGC are checked within that attempt.
        string parent = Path.GetDirectoryName(output)!;
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, $".agentcapture-{Guid.NewGuid():N}.png");
        var attempts = new List<object>();
        var clock = Stopwatch.StartNew();
        try
        {
            string[] methods = method == "auto" ? ["wgc", "printwindow"] : [method];
            JsonElement? payload = null;
            CaptureError? lastError = null;
            foreach (string candidate in methods)
            {
                int remaining = timeout - (int)clock.ElapsedMilliseconds;
                if (remaining < 500) { lastError = new("timeout", "Capture deadline expired.", 4); break; }
                int budget = method == "auto" && candidate == "wgc" ? Math.Max(500, remaining * 3 / 5) : remaining;
                var info = StartInfo("_capture", "--hwnd", Windows.Handle(hwnd), "--expected-pid", target.Pid.ToString(),
                    "--method", candidate, "--output", stage, "--timeout-ms", Math.Max(500, budget - 150).ToString(),
                    "--printwindow-flags", flags.ToString());
                var reply = await RunWorker(info, budget);
                if (reply.TimedOut)
                {
                    if (File.Exists(stage)) File.Delete(stage);
                    lastError = new("timeout", $"{candidate} worker exceeded its deadline.", 4);
                    attempts.Add(new { method = candidate, ok = false, error = new { code = lastError.Code, message = lastError.Message } });
                    continue;
                }
                string text = reply.Stdout;
                string diagnostics = reply.Stderr;
                try
                {
                    using var document = JsonDocument.Parse(text);
                    var result = document.RootElement.Clone();
                    if (reply.ExitCode == 0 && result.GetProperty("ok").GetBoolean() && File.Exists(stage))
                    {
                        payload = result;
                        attempts.Add(new { method = candidate, ok = true });
                        break;
                    }
                    var error = result.GetProperty("error");
                    lastError = new(error.GetProperty("code").GetString()!, error.GetProperty("message").GetString()!,
                        reply.ExitCode is >= 2 and <= 5 ? reply.ExitCode : 3);
                }
                catch (JsonException) { lastError = new("worker_failed", "Worker did not return valid JSON. " + diagnostics.Trim()); }
                catch (InvalidOperationException) { lastError = new("worker_failed", "Worker returned an invalid response."); }
                catch (KeyNotFoundException) { lastError = new("worker_failed", "Worker response is missing required fields."); }
                attempts.Add(new { method = candidate, ok = false, error = new { code = lastError!.Code, message = lastError.Message } });
                if (File.Exists(stage)) File.Delete(stage);
                if (lastError.Code is "invalid_window" or "window_changed" or "capture_restricted") break;
            }
            if (payload is null)
                return new { schemaVersion = 1, ok = false, command = "capture", target,
                    requestedMethod = method, attempts, elapsedMs = clock.ElapsedMilliseconds,
                    error = new { code = lastError?.Code ?? "capture_failed", message = lastError?.Message ?? "No frame captured.",
                        exitCode = lastError?.ExitCode ?? 3 } };
            // Publication is atomic within the destination directory. Failures never overwrite an existing output.
            File.Move(stage, output, command.Has("overwrite"));
            return new { schemaVersion = 1, ok = true, command = "capture", requestedMethod = method,
                method = payload.Value.GetProperty("method"), output, target = payload.Value.GetProperty("target"),
                image = payload.Value.GetProperty("image"), foreground = payload.Value.GetProperty("foreground"),
                warnings = payload.Value.GetProperty("warnings"), attempts, elapsedMs = clock.ElapsedMilliseconds };
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }

    public static object Worker(CommandLine command)
    {
        nint hwnd = Windows.ParseHandle(command.Required("hwnd"));
        var target = Windows.Read(hwnd);
        if (target.Pid != command.Pid("expected-pid")) throw new CaptureError("window_changed", "The HWND no longer belongs to the selected process.");
        string method = command.Method();
        if (method == "auto") throw CommandLine.Invalid("A worker must use an explicit method.");
        using var monitor = new ForegroundMonitor();
        var pixels = method == "wgc" ? WgcCapture.Capture(hwnd, command.Timeout()) : PrintWindowCapture.Capture(hwnd, command.PrintFlags());
        var after = Windows.Read(hwnd);
        if (after.Pid != target.Pid) throw new CaptureError("window_changed", "Target process changed during capture.");
        if (after.Visible != target.Visible || after.Minimized != target.Minimized || after.Cloaked != target.Cloaked)
            throw new CaptureError("window_state_changed", "Window state changed during capture. Retry when it is stable.");
        if (after.Width != target.Width || after.Height != target.Height)
            throw new CaptureError("window_resized", "Window dimensions changed during capture. Retry when it is stable.");
        object image = pixels.Save(command.Required("output"), method == "wgc" ? "premultiplied" : "opaque");
        var warnings = new List<string>();
        if (method == "wgc") warnings.Add("system_capture_border_may_appear");
        if (!monitor.Available) warnings.Add("foreground_monitor_unavailable");
        if (target.Minimized || !target.Visible || target.Cloaked) warnings.Add("hidden_or_minimized_content_not_guaranteed");
        return new { schemaVersion = 1, ok = true, method, target, image, foreground = monitor.Snapshot(), warnings };
    }
}
