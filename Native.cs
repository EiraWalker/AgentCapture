using System.Runtime.InteropServices;
using System.Text;

namespace AgentCapture;

internal static class Native
{
    internal delegate bool EnumWindowProc(nint hwnd, nint param);
    internal delegate void WinEventProc(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct WindowPos
    { public nint Hwnd, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] internal struct Msg
    { public nint Hwnd; public uint Message; public nuint WParam; public nint LParam; public uint Time; public Point Pt; public uint Private; }

    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowProc proc, nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetClassName(nint hwnd, StringBuilder text, int max);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool GetWindowDisplayAffinity(nint hwnd, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)] internal static extern bool PrintWindow(nint hwnd, nint hdc, uint flags);
    [DllImport("user32.dll")] internal static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProc callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] internal static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] internal static extern int GetMessage(out Msg msg, nint hwnd, uint min, uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out Msg msg, nint hwnd, uint min, uint max, uint flags);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread, uint message, nuint wp, nint lp);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(in Msg msg);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(in Msg msg);
    [DllImport("user32.dll", SetLastError = true)] internal static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wp, nint lp, uint flags, uint timeout, out nuint result);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(nint hwnd, uint attr, out int value, int size);
}
