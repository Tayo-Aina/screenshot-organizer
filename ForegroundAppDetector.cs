using System.Diagnostics;
using System.Text;

namespace ScreenshotOrganizer;

internal static class ForegroundAppDetector
{
    private static readonly HashSet<string> SnippingToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "snipping",
        "snip & sketch",
        "screen clipping",
        "SnippingTool",
        "ScreenSketch",
        "SnippingTool.exe",
        "ScreenSketch.exe"
    };

    public static string GetForegroundAppName()
    {
        try
        {
            var fgWindow = NativeMethods.GetForegroundWindow();
            var name = GetAppNameFromWindow(fgWindow);

            if (IsSnippingTool(name))
            {
                Logger.Info($"Foreground window is a snipping tool ({name}), walking Z-order for the window behind it");
                var nextWindow = NativeMethods.GetWindow(fgWindow, NativeMethods.GW_HWNDNEXT);
                if (nextWindow != IntPtr.Zero)
                {
                    var behindName = GetAppNameFromWindow(nextWindow);
                    Logger.Info($"Z-order fallback detected: {behindName}");
                    return behindName;
                }
                Logger.Warn("No window behind snipping tool found in Z-order");
            }

            return name;
        }
        catch (Exception ex)
        {
            Logger.Error("Unexpected error in foreground app detection", ex);
            return "Unknown";
        }
    }

    private static string GetAppNameFromWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
            return "Unknown";

        NativeMethods.GetWindowThreadProcessId(hWnd, out uint processId);
        if (processId == 0)
            return "Unknown";

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;

            if (string.Equals(processName, "explorer", StringComparison.OrdinalIgnoreCase))
                return "Desktop";

            try
            {
                var description = process.MainModule?.FileVersionInfo?.FileDescription;
                if (!string.IsNullOrWhiteSpace(description))
                    return description;
            }
            catch
            {
                Logger.Warn($"Cannot access MainModule for process '{processName}', falling back to process name");
            }

            if (!string.IsNullOrWhiteSpace(processName))
                return processName;

            return "Unknown";
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to get process info for PID {processId}: {ex.Message}");
            return "Unknown";
        }
    }

    private static bool IsSnippingTool(string appName)
    {
        foreach (var name in SnippingToolNames)
        {
            if (appName.Contains(name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
