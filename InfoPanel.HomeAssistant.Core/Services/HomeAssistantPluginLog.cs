namespace InfoPanel.HomeAssistant.Core.Services;

/// <summary>Lightweight logging for plugin simulator and test runs (console + file).</summary>
public static class HomeAssistantPluginLog
{
    private static readonly object Gate = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "InfoPanel",
        "logs");

    private static string? _logFilePath;

    static HomeAssistantPluginLog()
    {
        string? flag = Environment.GetEnvironmentVariable("INFOPANEL_PLUGIN_TEST");
        if (string.IsNullOrWhiteSpace(flag))
        {
            flag = Environment.GetEnvironmentVariable("INFOPANEL_HA_PLUGIN_LOG");
        }

        Enabled = string.Equals(flag, "1", StringComparison.Ordinal) ||
                  string.Equals(flag, "true", StringComparison.OrdinalIgnoreCase);

        if (Enabled)
        {
            EnsureLogFilePath();
        }
    }

    public static bool Enabled { get; set; }

    public static string? LogFilePath => _logFilePath;

    public static void Info(string message) => Write("INFO", message);

    public static void Debug(string message) => Write("DEBUG", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Error(Exception ex, string message) =>
        Write("ERROR", $"{message}: {ex.Message}");

    private static void EnsureLogFilePath()
    {
        if (_logFilePath != null)
        {
            return;
        }

        string? customPath = Environment.GetEnvironmentVariable("INFOPANEL_HA_PLUGIN_LOG_FILE");
        _logFilePath = string.IsNullOrWhiteSpace(customPath)
            ? Path.Combine(LogDirectory, "home-assistant-plugin-test.log")
            : customPath.Trim();

        Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
    }

    private static void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        EnsureLogFilePath();
        string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [HA:{level}] {message}";

        lock (Gate)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] [HA:{level}] {message}");

            if (_logFilePath == null)
            {
                return;
            }

            try
            {
                File.AppendAllText(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // Never break plugin updates because logging failed.
            }
        }
    }
}
