namespace AgentCapture;

internal sealed class CaptureError(string code, string message, int exitCode = 3) : Exception(message)
{
    public string Code { get; } = code;
    public int ExitCode { get; } = exitCode;
}

internal static class JsonOutput
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static void Write(object value) => Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(value, Options));
}
