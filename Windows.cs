using System.Diagnostics;
using System.Text;

namespace AgentCapture;

internal sealed record WindowSnapshot(string Hwnd, uint Pid, string ProcessName, string Title, string ClassName,
    bool Visible, bool Minimized, bool Cloaked, bool Foreground, int X, int Y, int Width, int Height, uint? DisplayAffinity);

internal static class Windows
{
    public static string Handle(nint hwnd) => $"0x{(ulong)hwnd:X}";

    public static WindowSnapshot Read(nint hwnd)
    {
        if (!Native.IsWindow(hwnd)) throw new CaptureError("invalid_window", "The window no longer exists.");
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        var title = new StringBuilder(4096);
        var className = new StringBuilder(512);
        Native.GetWindowText(hwnd, title, title.Capacity);
        Native.GetClassName(hwnd, className, className.Capacity);
        if (!Native.GetWindowRect(hwnd, out var rect)) throw new CaptureError("window_bounds_failed", "Cannot read window bounds.");
        string processName;
        try { using var p = Process.GetProcessById((int)pid); processName = p.ProcessName; }
        catch { processName = ""; }
        bool cloaked = Native.DwmGetWindowAttribute(hwnd, 14, out int cloak, sizeof(int)) == 0 && cloak != 0;
        uint? affinity = Native.GetWindowDisplayAffinity(hwnd, out uint a) ? a : null;
        return new(Handle(hwnd), pid, processName, title.ToString(), className.ToString(),
            Native.IsWindowVisible(hwnd), Native.IsIconic(hwnd), cloaked, hwnd == Native.GetForegroundWindow(),
            rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top, affinity);
    }

    public static List<WindowSnapshot> List(uint? pid, string? title, bool includeHidden)
    {
        var result = new List<WindowSnapshot>();
        Native.EnumWindowProc callback = (hwnd, _) =>
        {
            try
            {
                var window = Read(hwnd);
                if ((includeHidden || window.Visible) && (pid is null || pid == window.Pid) &&
                    (title is null || window.Title.Contains(title, StringComparison.OrdinalIgnoreCase))) result.Add(window);
            }
            catch (CaptureError) { /* A window may disappear during enumeration. */ }
            return true;
        };
        if (!Native.EnumWindows(callback, 0)) throw new CaptureError("enumeration_failed", "Cannot enumerate windows.");
        return result;
    }

    public static nint Resolve(string? hwnd, uint? pid)
    {
        if (hwnd is not null) return ParseHandle(hwnd);
        var matches = List(pid, null, false).Where(w => !w.Cloaked && w.Width > 0 && w.Height > 0).ToList();
        if (matches.Count == 0) throw new CaptureError("window_not_found", "No visible window found for this PID. Use list --include-hidden and an explicit HWND.");
        if (matches.Count != 1) throw new CaptureError("ambiguous_window", "PID has multiple windows. Use list --pid and select an explicit HWND.", 2);
        return ParseHandle(matches[0].Hwnd);
    }

    public static nint ParseHandle(string value)
    {
        try
        {
            ulong number = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? Convert.ToUInt64(value[2..], 16) : ulong.Parse(value);
            if (number == 0 || number > long.MaxValue) throw new FormatException();
            return (nint)(long)number;
        }
        catch { throw new CaptureError("invalid_arguments", "HWND must be a nonzero decimal or 0x hexadecimal handle.", 2); }
    }

    public static void Validate(WindowSnapshot target, string method)
    {
        if (target.Width <= 0 || target.Height <= 0 || (long)target.Width * target.Height > 40_000_000)
            throw new CaptureError("invalid_dimensions", "Window dimensions are empty or exceed the 40-megapixel limit.");
        if (target.DisplayAffinity is > 0) throw new CaptureError("capture_restricted", "Window reports a capture restriction.");
        if (method == "wgc" && (target.Minimized || !target.Visible || target.Cloaked))
            throw new CaptureError("window_not_capturable", "WGC requires a visible, non-minimized, non-cloaked window. The tool will not restore it.");
    }
}
