using System.Text;
using System.Text.Json;

namespace AgentCapture;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        try
        {
            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
                throw new CaptureError("unsupported_os", "Requires Windows 10 2004 (19041) or newer.");
            var command = new CommandLine(args);
            object result = command.Command switch
            {
                "help" => Help(),
                "version" => new { schemaVersion = 1, ok = true, version = "1.0.0", architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString() },
                "list" => new { schemaVersion = 1, ok = true, command = "list", windows = Windows.List(command.Pid(), command.Get("title"), command.Has("include-hidden")) },
                "capture" => CaptureSupervisor.Capture(command).GetAwaiter().GetResult(),
                "_capture" => CaptureSupervisor.Worker(command),
                "_fixture" => SelfTest.RunFixture(command),
                "self-test" => SelfTest.Run(command),
                "_stall" => SelfTest.StallWorker(),
                _ => throw CommandLine.Invalid("Unknown command.")
            };
            JsonOutput.Write(result);
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonOutput.Options));
            return doc.RootElement.GetProperty("ok").GetBoolean() ? 0 : doc.RootElement.GetProperty("error").GetProperty("exitCode").GetInt32();
        }
        catch (CaptureError error)
        { JsonOutput.Write(new { schemaVersion = 1, ok = false, error = new { code = error.Code, message = error.Message, exitCode = error.ExitCode } }); return error.ExitCode; }
        catch (Exception error)
        {
            JsonOutput.Write(new { schemaVersion = 1, ok = false, error = new { code = "internal_error", message = error.Message,
                hresult = $"0x{error.HResult:X8}", exitCode = 5 } });
            return 5;
        }
    }

    private static object Help() => new
    {
        schemaVersion = 1, ok = true, version = "1.0.0",
        commands = new[]
        {
            "list [--pid PID] [--title TEXT] [--include-hidden]",
            "capture (--hwnd HWND | --pid PID) --output FILE.png [--method wgc|printwindow|auto] [--timeout-ms 8000] [--overwrite] [--printwindow-flags 0|2]",
            "self-test --output-dir DIRECTORY",
            "version"
        },
        defaults = new { method = "wgc", timeoutMs = 8000, printWindowFlags = 0, foregroundActivation = false, restoreMinimized = false },
        exitCodes = new { success = 0, argumentsOrOutputExists = 2, captureFailed = 3, timeout = 4, internalError = 5 },
        notes = new[] { "stdout is one UTF-8 JSON object.", "Use a HWND from list; PID with multiple windows is rejected.",
            "auto tries WGC then PrintWindow within one shared deadline; no activation or screen-copy fallback.",
            "WGC preserves the default system capture border and excludes the cursor.",
            "All-black RGB frames are rejected. Other content and frame freshness still need caller verification." }
    };
}
