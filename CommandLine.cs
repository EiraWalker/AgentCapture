namespace AgentCapture;

internal sealed class CommandLine
{
    public string Command { get; }
    private readonly Dictionary<string, string> options = new(StringComparer.Ordinal);
    private static readonly HashSet<string> Flags = ["overwrite", "include-hidden"];

    public CommandLine(string[] args)
    {
        Command = args.Length == 0 ? "help" : args[0];
        if (Command is "--help" or "-h") Command = "help";
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw Invalid("Options must begin with --.");
            string key = args[i][2..];
            string value = "true";
            if (!Flags.Contains(key))
            {
                if (++i == args.Length || args[i].StartsWith("--")) throw Invalid($"Missing value for --{key}.");
                value = args[i];
            }
            if (!options.TryAdd(key, value)) throw Invalid($"Duplicate option --{key}.");
        }
        string[] allowed = Command switch
        {
            "list" => ["pid", "title", "include-hidden"],
            "capture" => ["pid", "hwnd", "output", "method", "timeout-ms", "overwrite", "printwindow-flags"],
            "_capture" => ["hwnd", "output", "method", "timeout-ms", "printwindow-flags", "expected-pid"],
            "self-test" => ["output-dir"],
            "_fixture" => ["ready-file"],
            "_stall" => [],
            "help" or "version" => [],
            _ => throw Invalid("Unknown command. Use help.")
        };
        foreach (string key in options.Keys)
            if (!allowed.Contains(key)) throw Invalid($"Unknown option --{key} for {Command}.");
    }

    public string? Get(string key) => options.GetValueOrDefault(key);
    public string Required(string key) => Get(key) ?? throw Invalid($"Missing --{key}.");
    public bool Has(string key) => options.ContainsKey(key);
    public uint? Pid(string key = "pid")
    {
        if (Get(key) is not { } value) return null;
        if (!uint.TryParse(value, out uint pid) || pid == 0 || pid > int.MaxValue) throw Invalid($"Invalid --{key}.");
        return pid;
    }
    public int Timeout()
    {
        if (Get("timeout-ms") is not { } value) return 8000;
        if (!int.TryParse(value, out int timeout) || timeout is < 500 or > 120000) throw Invalid("--timeout-ms must be 500..120000.");
        return timeout;
    }
    public uint PrintFlags()
    {
        string value = Get("printwindow-flags") ?? "0";
        if (value != "0" && value != "2") throw Invalid("--printwindow-flags must be 0 or 2 (full content).");
        return uint.Parse(value);
    }
    public string Method()
    {
        string method = Get("method") ?? "wgc";
        if (method is not ("wgc" or "printwindow" or "auto")) throw Invalid("--method must be wgc, printwindow, or auto.");
        return method;
    }
    public static CaptureError Invalid(string message) => new("invalid_arguments", message, 2);
}
