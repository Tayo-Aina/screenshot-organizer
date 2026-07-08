namespace ScreenshotOrganizer;

internal static class Logger
{
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ScreenshotOrganizer",
        "logs");

    private static readonly string LogFilePath = Path.Combine(LogDirectory, "app.log");
    private static readonly long MaxLogSizeBytes = 5 * 1024 * 1024;
    private static readonly object LockObj = new();

    static Logger()
    {
        Directory.CreateDirectory(LogDirectory);
    }

    public static void Info(string message) => WriteLog("INFO", message);
    public static void Warn(string message) => WriteLog("WARN", message);
    public static void Error(string message) => WriteLog("ERROR", message);
    public static void Error(string message, Exception ex) => WriteLog("ERROR", $"{message}: {ex}");

    private static void WriteLog(string level, string message)
    {
        var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";

        lock (LockObj)
        {
            try
            {
                RotateIfNeeded();
                File.AppendAllText(LogFilePath, entry + Environment.NewLine);
            }
            catch
            {
            }
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogFilePath))
            return;

        var info = new FileInfo(LogFilePath);
        if (info.Length < MaxLogSizeBytes)
            return;

        var rotatedPath = Path.Combine(LogDirectory, $"app.{DateTime.Now:yyyyMMddHHmmss}.log");
        File.Move(LogFilePath, rotatedPath);
    }
}
