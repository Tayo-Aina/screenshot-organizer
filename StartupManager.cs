using System.Diagnostics;
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

        var startupCommand = GetStartupCommand();
        key.SetValue(ValueName, startupCommand, RegistryValueKind.String);
        Logger.Info($"Registered startup entry: {startupCommand}");
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
        var registeredCommand = key?.GetValue(ValueName) as string;
        return string.Equals(
            NormalizeCommand(registeredCommand),
            NormalizeCommand(GetStartupCommand()),
            StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }

    private static string GetStartupCommand()
    {
        var exePath = GetExecutablePath();
        return $"\"{exePath}\"";
    }

    private static string GetExecutablePath()
    {
        var candidates = new[]
        {
            Environment.ProcessPath,
            Process.GetCurrentProcess().MainModule?.FileName,
            Path.Combine(AppContext.BaseDirectory, "ScreenshotOrganizer.exe")
        };

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException("Could not determine the executable path for startup registration.");
    }

    private static string NormalizeCommand(string? command)
    {
        return string.IsNullOrWhiteSpace(command)
            ? string.Empty
            : command.Trim().Replace("\"", string.Empty);
    }
}
