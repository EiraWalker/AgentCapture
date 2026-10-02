namespace AgentCapture;

// The hook runs on a dedicated message-pumping thread; no window or input is created.
internal sealed class ForegroundMonitor : IDisposable
{
    private readonly Thread thread;
    private readonly ManualResetEventSlim ready = new();
    private readonly object gate = new();
    private readonly List<string> changes = [];
    private readonly Native.WinEventProc callback;
    private uint threadId;
    private nint hook;
    public bool Available { get; private set; }
    public string Before { get; }

    public ForegroundMonitor()
    {
        Before = Windows.Handle(Native.GetForegroundWindow());
        callback = (_, _, hwnd, _, _, _, _) => { lock (gate) changes.Add(Windows.Handle(hwnd)); };
        thread = new Thread(() =>
        {
            threadId = Native.GetCurrentThreadId();
            Native.PeekMessage(out _, 0, 0, 0, 0);
            hook = Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 0); // EVENT_SYSTEM_FOREGROUND, OUTOFCONTEXT
            Available = hook != 0;
            ready.Set();
            try
            {
                while (Native.GetMessage(out var msg, 0, 0, 0) > 0)
                { Native.TranslateMessage(msg); Native.DispatchMessage(msg); }
            }
            finally { if (hook != 0) Native.UnhookWinEvent(hook); }
        }) { IsBackground = true, Name = "Foreground events" };
        thread.Start();
        if (!ready.Wait(TimeSpan.FromSeconds(2))) throw new CaptureError("monitor_failed", "Foreground event monitor did not initialize.");
    }

    public object Snapshot()
    {
        lock (gate) return new { available = Available, before = Before, after = Windows.Handle(Native.GetForegroundWindow()),
            changes = changes.ToArray(), targetActivationRequested = false,
            note = "Events can include user activity; absence of events is not proof that the target kept rendering." };
    }

    public void Dispose()
    {
        Native.PostThreadMessage(threadId, 0x12, 0, 0);
        thread.Join(TimeSpan.FromSeconds(2));
        // Callback stays rooted for the monitor's entire lifetime.
        GC.KeepAlive(callback);
        ready.Dispose();
    }
}
