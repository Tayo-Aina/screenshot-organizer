using Microsoft.Win32;

namespace ScreenshotOrganizer;

internal static class StartupManager
{
    private const string RunKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ScreenshotOrganizer";

    public static void Register()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

        var exePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "ScreenshotOrganizer.exe");
        key.SetValue(ValueName, $"\"{exePath}\"");
        Logger.Info($"Registered startup entry: {exePath}");
    }

    public static void Unregister()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        if (key?.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
            Logger.Info("Removed startup entry");
        }
    }

    public static bool IsRegistered()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) != null;
    }
}
