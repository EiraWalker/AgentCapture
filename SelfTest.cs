using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace AgentCapture;

internal static class SelfTest
{
    private const uint ChangeState = 0x8000 + 17;
    public static object StallWorker() { Thread.Sleep(30_000); return new { ok = true }; }
    private sealed class ProbeForm : Form
    {
        private int mode;
        public ProbeForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            if (!Native.GetWindowRect(Native.GetForegroundWindow(), out var front) || front.Right - front.Left < 600 || front.Bottom - front.Top < 360)
                throw new CaptureError("test_environment", "Self-test needs an existing foreground window large enough to cover the fixture.");
            Location = new Point(front.Left + (front.Right - front.Left - 480) / 2, front.Top + (front.Bottom - front.Top - 240) / 2);
            ClientSize = new Size(480, 240);
            Text = "AgentCapture isolated test fixture";
        }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams
        {
            get { var parameters = base.CreateParams; parameters.ExStyle |= 0x08000000 | 0x00000080; return parameters; }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color color = mode switch { 1 => Color.FromArgb(220, 100, 50), 2 => Color.Black, _ => Color.FromArgb(32, 170, 95) };
            e.Graphics.Clear(color);
            if (mode != 2) e.Graphics.DrawString($"AgentCapture test fixture\nMarker {mode}", Font, Brushes.White, new PointF(24, 24));
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x46) // WM_WINDOWPOSCHANGING: keep the fixture below every existing window, without activation.
            {
                var pos = Marshal.PtrToStructure<Native.WindowPos>(m.LParam);
                pos.InsertAfter = 1; // HWND_BOTTOM
                pos.Flags = (pos.Flags & ~0x4U) | 0x10U; // remove NOZORDER; add NOACTIVATE
                Marshal.StructureToPtr(pos, m.LParam, false);
            }
            if ((uint)m.Msg == ChangeState)
            {
                mode = (int)m.WParam;
                if (mode == 4) WindowState = FormWindowState.Minimized;
                else if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                if (mode == 5) Hide();
                else if (!Visible) Show();
                Invalidate(); Update(); m.Result = 1; return;
            }
            if (m.Msg is 0x317 or 0x318) // WM_PRINT, WM_PRINTCLIENT
            {
                using var graphics = Graphics.FromHdc(m.WParam);
                OnPaint(new PaintEventArgs(graphics, ClientRectangle));
                m.Result = 1; return;
            }
            base.WndProc(ref m);
        }
    }

    public static object RunFixture(CommandLine command)
    {
        Application.EnableVisualStyles();
        using var form = new ProbeForm();
        using var secondary = new ProbeForm { Text = "AgentCapture secondary fixture" };
        form.Shown += (_, _) =>
        {
            secondary.Show();
            File.WriteAllText(command.Required("ready-file"), Windows.Handle(form.Handle));
        };
        Application.Run(form);
        return new { ok = true };
    }

    public static object Run(CommandLine command)
    {
        string root = Path.GetFullPath(command.Required("output-dir"));
        string output = Path.Combine(root, $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}");
        Directory.CreateDirectory(output);
        string ready = Path.Combine(output, "fixture-hwnd.txt");
        using var fixtureJob = new WorkerJob();
        using var fixture = Process.Start(CaptureSupervisor.StartInfo("_fixture", "--ready-file", ready))
            ?? throw new CaptureError("test_failed", "Cannot start fixture.");
        fixtureJob.Assign(fixture);
        var tests = new List<object>();
        var foregroundAtStart = Windows.Handle(Native.GetForegroundWindow());
        nint hwnd = 0;
        JsonElement Capture(string name, string method, int timeout = 8000)
        {
            var result = CaptureSupervisor.Capture(new CommandLine(["capture", "--hwnd", Windows.Handle(hwnd),
                "--method", method, "--output", Path.Combine(output, name + ".png"), "--timeout-ms", timeout.ToString()])).GetAwaiter().GetResult();
            return JsonSerializer.SerializeToElement(result, JsonOutput.Options);
        }
        void Mode(int mode)
        {
            if (Native.SendMessageTimeout(hwnd, ChangeState, (nuint)mode, 0, 2, 1000, out _) == 0)
                throw new CaptureError("test_failed", "Cannot change fixture state.");
        }
        void Expect(bool condition, string message)
        { if (!condition) throw new CaptureError("test_failed", message); }
        void CheckImage(JsonElement result, Color expected)
        {
            Expect(result.GetProperty("ok").GetBoolean(), result.ToString());
            using var bitmap = new Bitmap(result.GetProperty("output").GetString()!);
            Expect(bitmap.Width == 480 && bitmap.Height == 240, "Unexpected image dimensions.");
            Color actual = bitmap.GetPixel(200, 160);
            Expect(actual.R == expected.R && actual.G == expected.G && actual.B == expected.B && actual.A == 255,
                $"Pixel mismatch: {actual} vs {expected}.");
            var fg = result.GetProperty("foreground");
            Expect(fg.GetProperty("before").GetString() != Windows.Handle(hwnd) && fg.GetProperty("after").GetString() != Windows.Handle(hwnd), "Fixture became foreground.");
            Expect(!fg.GetProperty("changes").EnumerateArray().Any(v => v.GetString() == Windows.Handle(hwnd)), "Foreground hook observed fixture activation.");
        }
        try
        {
            var started = Stopwatch.StartNew();
            while (!File.Exists(ready) && !fixture.HasExited && started.ElapsedMilliseconds < 8000) Thread.Sleep(20);
            if (!File.Exists(ready)) throw new CaptureError("test_failed", "Fixture did not initialize.");
            hwnd = Windows.ParseHandle(File.ReadAllText(ready));
            foreach (string method in new[] { "printwindow", "wgc" })
            {
                Mode(0);
                var a = Capture(method + "-A", method);
                CheckImage(a, Color.FromArgb(32, 170, 95));
                Mode(1);
                var b = Capture(method + "-B", method);
                CheckImage(b, Color.FromArgb(220, 100, 50));
                Expect(a.GetProperty("image").GetProperty("sha256").GetString() != b.GetProperty("image").GetProperty("sha256").GetString(), "Changing fixture state did not change image.");
                tests.Add(new { name = method + "_fresh_occluded_frames", ok = true, a, b });
            }
            var automatic = Capture("auto", "auto");
            CheckImage(automatic, Color.FromArgb(220, 100, 50));
            Expect(automatic.GetProperty("method").GetString() == "wgc", "Auto did not prefer WGC.");
            tests.Add(new { name = "auto_prefers_wgc", ok = true, result = automatic });
            string existing = automatic.GetProperty("output").GetString()!;
            byte[] original = File.ReadAllBytes(existing);
            try
            {
                _ = CaptureSupervisor.Capture(new CommandLine(["capture", "--hwnd", Windows.Handle(hwnd), "--output", existing])).GetAwaiter().GetResult();
                throw new CaptureError("test_failed", "Existing output was accepted without --overwrite.");
            }
            catch (CaptureError error) when (error.Code == "output_exists") { }
            Expect(original.SequenceEqual(File.ReadAllBytes(existing)), "Existing output was modified.");
            tests.Add(new { name = "existing_output_preserved", ok = true });
            uint fixturePid = Windows.Read(hwnd).Pid;
            var windows = Windows.List(fixturePid, null, false);
            Expect(windows.Count >= 2 && windows.Any(w => w.Hwnd == Windows.Handle(hwnd)), "Enumeration did not return fixture windows.");
            try
            {
                _ = CaptureSupervisor.Capture(new CommandLine(["capture", "--pid", fixturePid.ToString(), "--output", Path.Combine(output, "ambiguous.png")])).GetAwaiter().GetResult();
                throw new CaptureError("test_failed", "Ambiguous PID silently selected a window.");
            }
            catch (CaptureError error) when (error.Code == "ambiguous_window") { }
            tests.Add(new { name = "pid_ambiguity_requires_hwnd", ok = true, windows });
            var alpha = new CapturedPixels(1, 1, [20, 40, 80, 128]);
            string alphaPath = Path.Combine(output, "alpha-roundtrip.png");
            _ = alpha.Save(alphaPath, "premultiplied");
            using (var bitmap = new Bitmap(alphaPath))
                Expect(bitmap.GetPixel(0, 0).ToArgb() == Color.FromArgb(128, 159, 80, 40).ToArgb(), "Premultiplied alpha was not encoded as straight PNG alpha.");
            tests.Add(new { name = "png_alpha_roundtrip", ok = true });
            Mode(2);
            var blank = Capture("black", "printwindow");
            Expect(!blank.GetProperty("ok").GetBoolean() && blank.GetProperty("error").GetProperty("code").GetString() == "blank_frame", "Black frame was not rejected.");
            Expect(!File.Exists(Path.Combine(output, "black.png")), "Black frame was published.");
            tests.Add(new { name = "blank_frame_rejected", ok = true, result = blank });
            Mode(4);
            var minimized = Capture("minimized", "wgc");
            Expect(!minimized.GetProperty("ok").GetBoolean() && minimized.GetProperty("error").GetProperty("code").GetString() == "window_not_capturable", "Minimized WGC did not fail.");
            Expect(Native.IsIconic(hwnd), "Tool restored minimized window.");
            tests.Add(new { name = "minimized_not_restored", ok = true, result = minimized });
            var minimizedFallback = Capture("auto-minimized", "auto");
            Expect(minimizedFallback.GetProperty("ok").GetBoolean() && minimizedFallback.GetProperty("method").GetString() == "printwindow" && Native.IsIconic(hwnd),
                "Auto did not report PrintWindow's result while leaving the fixture minimized.");
            tests.Add(new { name = "auto_printwindow_result_without_restore", ok = true, result = minimizedFallback,
                note = "This minimized-window result contains a small window surface, not proof of full application content." });
            Mode(5);
            var fallback = Capture("auto-fallback", "auto");
            Expect(!fallback.GetProperty("ok").GetBoolean() && fallback.GetProperty("attempts").GetArrayLength() == 2 &&
                !Native.IsWindowVisible(hwnd) && !File.Exists(Path.Combine(output, "auto-fallback.png")), "Auto failure changed the hidden fixture or published a blank image.");
            tests.Add(new { name = "auto_failure_without_showing", ok = true, result = fallback });
            var timeoutClock = Stopwatch.StartNew();
            var hung = CaptureSupervisor.RunWorker(CaptureSupervisor.StartInfo("_stall"), 1000).GetAwaiter().GetResult();
            Expect(hung.TimedOut && timeoutClock.ElapsedMilliseconds < 5000, "Stalled worker was not terminated promptly.");
            bool stillRunning;
            try { using var p = Process.GetProcessById(hung.ProcessId); stillRunning = !p.HasExited; }
            catch (ArgumentException) { stillRunning = false; }
            Expect(!stillRunning, "Timed-out worker remained alive.");
            tests.Add(new { name = "stalled_worker_terminated", ok = true, elapsedMs = timeoutClock.ElapsedMilliseconds, workerPid = hung.ProcessId,
                note = "Deterministic stalled process through the same supervisor used for captures; native PrintWindow blocking was not reproduced on this Windows build." });
            var summary = new { schemaVersion = 1, ok = true, command = "self-test", outputDirectory = output,
                scope = "Own cooperative WinForms window at bottom of Z order, covered by the existing foreground window; no third-party application compatibility claim.",
                foregroundAtStart, foregroundAfter = Windows.Handle(Native.GetForegroundWindow()), tests };
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions(JsonOutput.Options) { WriteIndented = true }));
            return summary;
        }
        finally
        {
            if (!fixture.HasExited) { fixture.Kill(entireProcessTree: true); fixture.WaitForExit(); }
            _ = fixture.StandardOutput.ReadToEnd(); _ = fixture.StandardError.ReadToEnd();
        }
    }
}
