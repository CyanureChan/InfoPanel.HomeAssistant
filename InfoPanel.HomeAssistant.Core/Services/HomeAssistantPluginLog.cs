namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Lightweight console logging for plugin simulator and test runs.</summary>
public static class HomeAssistantPluginLog
{
    private static readonly object Gate = new();

    static HomeAssistantPluginLog()
    {
        string? flag = Environment.GetEnvironmentVariable("INFOPANEL_PLUGIN_TEST");
        if (string.IsNullOrWhiteSpace(flag))
        {
            flag = Environment.GetEnvironmentVariable("INFOPANEL_HA_PLUGIN_LOG");
        }

        Enabled = string.Equals(flag, "1", StringComparison.Ordinal) ||
                  string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);
    }

    public static bool Enabled { get; set; }

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception ex, string message) =>
        Write("ERROR", $"{message}: {ex.Message}");

    private static void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        lock (Gate)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [HA:{level}] {message}");
        }
    }
}
